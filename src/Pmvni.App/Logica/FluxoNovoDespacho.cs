using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Os passos da página Novo despacho (desenho §10.2): origem (tipo, processo, relatório base, pasta da etapa), conteúdo (a prévia
/// editável), conferência (as páginas do PDF gerado na pasta de trabalho) e gravado (na pasta do processo).</summary>
public enum PassoNovoDespacho { Origem, Conteudo, Conferencia, Gravado }

/// <summary>O arquivo escolhido à mão como relatório base: um <c>.rpmvni</c> (usado como está) ou o <c>dados.json</c> da análise do kit
/// (importado para um <c>.rpmvni</c> novo antes de usar).</summary>
public enum ArquivoBase { Pacote, DadosJson }

/// <summary>O que a página já traz escolhido ao abrir: o processo, o relatório base (<c>.rpmvni</c>) e a pasta da etapa. Null = o usuário escolhe.</summary>
public sealed record OrigemSugerida(Processo? Processo, string? Pacote, EtapaDespacho? Etapa);

/// <summary>Onde a correção automática para (<see cref="FluxoNovoDespacho.PlanejarAutomatico"/>).</summary>
public enum DestinoAutomatico { EscolherProcesso, SemRelatorio, Bloqueado, FaltaPreencher, Conferir }

/// <summary>O que a correção automática faz e a mensagem que a página mostra (título e texto).</summary>
public sealed record PlanoAutomatico(DestinoAutomatico Destino, string Titulo, string Mensagem);

/// <summary>Lógica do Novo despacho sem tipos WinUI (compilada também nos testes): a origem sugerida pelo módulo de onde se veio, o motivo de um
/// tipo desabilitado e quando se pode gerar e gravar. Nada aqui grava; <see cref="Sugerir"/> só lê pastas (para <c>Task.Run</c>).</summary>
public static class FluxoNovoDespacho
{
    public const string Compondo = "Termine ou cancele o despacho em edição.";

    /// <summary>Trocar de módulo, abrir outro despacho ou trocar a pasta de processos durante "Gravando na pasta do processo…".</summary>
    public const string GravandoAguarde = "O despacho está sendo gravado na pasta do processo; tente de novo quando a gravação terminar.";

    public const string SemPmvrel =
        "Este processo não tem relatório revisado (.rpmvni). Importe a análise da IA em Processos antes de emitir o despacho.";
    /// <summary>"Escolher arquivo…" com um arquivo que não é relatório.</summary>
    public const string ArquivoNaoServe = "Escolha um relatório revisado (.rpmvni) ou o dados.json da análise do kit.";

    /// <summary>O filtro do seletor de "Escolher arquivo…" (as extensões separadas por ";").</summary>
    public const string FiltroArquivoBase = ".rpmvni;.pmvrel;.json";

    /// <summary>O tipo do arquivo escolhido à mão pela extensão; null = não serve.</summary>
    public static ArquivoBase? TipoDoArquivoBase(string? caminho) =>
        System.IO.Path.GetExtension(caminho ?? "").ToLowerInvariant() switch
        {
            ".rpmvni" or ".pmvrel" => ArquivoBase.Pacote, // .pmvrel: formato antigo, mesmo conteúdo
            ".json" => ArquivoBase.DadosJson,
            _ => null,
        };

    /// <summary>A lista da caixa "Relatório base": os <c>.rpmvni</c> da pasta do processo e, depois, os escolhidos à mão que ainda não
    /// estão nela (mesmo caminho sem diferença de maiúsculas). Cada um com <c>Escolhido</c> = veio do "Escolher arquivo…".</summary>
    public static IReadOnlyList<(string Caminho, bool Escolhido)> RelatoriosDaLista(IEnumerable<RelatorioEncontrado> daPasta, IEnumerable<string> escolhidos)
    {
        ArgumentNullException.ThrowIfNull(daPasta);
        ArgumentNullException.ThrowIfNull(escolhidos);
        var lista = daPasta.Where(r => r.Tipo == TipoRelatorio.Pacote).Select(r => (r.Caminho, false)).ToList();
        var vistos = new HashSet<string>(lista.Select(x => Normalizar(x.Caminho)), StringComparer.OrdinalIgnoreCase);
        foreach (var c in escolhidos)
            if (vistos.Add(Normalizar(c))) lista.Add((c, true));
        return lista;
    }

    public const string NaoSalvou =
        "O relatório não pôde ser salvo; o despacho usa sempre a versão gravada. Resolva a gravação e tente de novo.";

    /// <summary>De Relatórios: o processo cuja pasta contém o pacote aberto, e esse pacote. De Processos ou Despachos: o processo selecionado
    /// e o <c>.rpmvni</c> mais novo dele. A etapa é <see cref="Etapas.Sugerida"/>. Sem contexto (ou pacote fora de qualquer processo): nada.</summary>
    public static OrigemSugerida Sugerir(Modulo origem, Processo? selecionado, string? pacoteAberto, IReadOnlyList<Processo> processos,
        Func<Processo, IReadOnlyList<RelatorioEncontrado>> relatorios, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(processos);
        ArgumentNullException.ThrowIfNull(relatorios);
        Processo? p;
        string? pacote;
        if (origem == Modulo.Relatorios)
        {
            if (pacoteAberto is null) return new OrigemSugerida(null, null, null);
            // O relatório fica na pasta do processo ou, desde 2026-11-16, numa pasta DESPACHO NN dela: vale o processo que o contém.
            var arquivo = Normalizar(Path.GetFullPath(pacoteAberto));
            p = processos.Where(x => arquivo.StartsWith(Normalizar(x.Pasta) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Pasta.Length).FirstOrDefault();
            if (p is null) return new OrigemSugerida(null, null, null);
            pacote = pacoteAberto;
        }
        else
        {
            if (selecionado is null) return new OrigemSugerida(null, null, null);
            p = selecionado;
            pacote = MaisNovo(relatorios(p));
        }
        return new OrigemSugerida(p, pacote, EtapaDoRelatorio(p, pacote, hoje));
    }

    /// <summary>Regra do kit: o despacho da mesma análise reutiliza a pasta dela. Relatório dentro de uma pasta DESPACHO NN - AAAA-MM-DD do
    /// processo = essa etapa; senão, a sugerida (<see cref="Etapas.Sugerida"/>).</summary>
    static EtapaDespacho EtapaDoRelatorio(Processo p, string? pacote, DateOnly hoje)
    {
        if (pacote is not null)
        {
            var pastaDoPacote = Normalizar(Path.GetDirectoryName(Path.GetFullPath(pacote)));
            var daAnalise = Etapas.Existentes(p.Pasta).FirstOrDefault(e => string.Equals(Normalizar(e.Pasta), pastaDoPacote, StringComparison.OrdinalIgnoreCase));
            // Só se a pasta é de hoje (2026-11-17): o despacho emitido hoje vai para a pasta de hoje, não para a de uma análise antiga.
            if (daAnalise is not null && daAnalise.Data == hoje) return daAnalise;
        }
        return Etapas.Sugerida(p.Pasta, hoje);
    }

    static string Normalizar(string? pasta) => pasta is null ? "" : Path.GetFullPath(pasta).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>O <c>.rpmvni</c> alterado por último (empate: pelo nome); null se o processo não tem nenhum.</summary>
    public static string? MaisNovo(IEnumerable<RelatorioEncontrado> relatorios) =>
        relatorios.Where(r => r.Tipo == TipoRelatorio.Pacote).OrderByDescending(r => r.AlteradoUtc)
            .ThenBy(r => r.Caminho, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Caminho;

    /// <summary>null se o tipo pode ser emitido; senão o motivo que vai para a dica do item do menu: com um despacho em edição,
    /// <see cref="Compondo"/>; senão o motivo do registro do kit.</summary>
    public static string? MotivoDesabilitado(TipoDespacho t, ModelosDisponiveis m, bool compondo)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (compondo) return Compondo;
        if (m.Indisponiveis.TryGetValue(t, out var motivo)) return motivo;
        return m.Modelos.Any(x => x.Tipo == t) ? null : "O registro de modelos do kit não tem este tipo de despacho.";
    }

    // ---------- correção automática (2026-11-16) ----------

    /// <summary>O rótulo do botão do painel e a dica do item do menu.</summary>
    public const string RotuloAutomatico = "Criar automaticamente a partir do relatório";

    public const string AutomaticoSemProcesso =
        "Escolha o processo na lista e clique em \"" + RotuloAutomatico + "\": o despacho é montado com os itens pendentes do relatório mais recente dele.";

    public const string AutomaticoSemRelatorio =
        "Este processo não tem relatório revisado (.rpmvni), então não há de onde tirar as exigências. Importe a análise da IA em Processos "
        + "(ou use \"Escolher arquivo…\") e crie de novo; se preferir, escreva as exigências aqui mesmo.";

    public const string AutomaticoBloqueado = "O despacho não foi criado automaticamente: resolva o que está indicado acima e tente de novo.";

    /// <summary>O que a correção automática faz com o que encontrou: para no passo 1 (sem processo, ou relatório que não serve), fica no
    /// passo 2 (sem relatório, ou falta preencher algo) ou segue direto para a conferência (passo 3).</summary>
    public static PlanoAutomatico PlanejarAutomatico(Processo? processo, string? pacote, IReadOnlyList<string> bloqueios, RascunhoDespacho? rascunho)
    {
        ArgumentNullException.ThrowIfNull(bloqueios);
        if (processo is null) return new PlanoAutomatico(DestinoAutomatico.EscolherProcesso, "Escolha o processo", AutomaticoSemProcesso);
        if (pacote is null) return new PlanoAutomatico(DestinoAutomatico.SemRelatorio, "Processo sem relatório", AutomaticoSemRelatorio);
        // Entre os bloqueios está o de não haver item pendente ("Nenhum item incluído tem situação diferente de ATENDE…").
        if (bloqueios.Count > 0 || rascunho is null) return new PlanoAutomatico(DestinoAutomatico.Bloqueado, "Despacho não criado", AutomaticoBloqueado);
        var faltando = rascunho.Faltando();
        if (faltando.Count > 0)
            return new PlanoAutomatico(DestinoAutomatico.FaltaPreencher, "Falta preencher",
                ResumoAutomatico(rascunho) + " Antes de gerar, preencha: " + string.Join("; ", faltando) + ".");
        return new PlanoAutomatico(DestinoAutomatico.Conferir, "Despacho criado a partir do relatório", ResumoAutomatico(rascunho) + " Confira as páginas antes de gravar.");
    }

    /// <summary>"3 exigências do relatório: 1 NÃO ATENDE, 1 FALTA COMPROVAR e 1 INFORMAÇÕES DIVERGENTES." (na ordem do kit; só as que há).</summary>
    public static string ResumoAutomatico(RascunhoDespacho r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var marcadas = r.Pendencias.Where(p => p.Incluida).ToList();
        var partes = Pmvni.Composicao.Kit.Situacoes.SitItem.Where(s => s != "ATENDE")
            .Select(s => (Texto: EtiquetasSituacao.Texto(s), Quantas: marcadas.Count(p => p.Situacao == s)))
            .Where(x => x.Quantas > 0).Select(x => x.Quantas.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + x.Texto).ToList();
        var lista = partes.Count <= 1 ? string.Concat(partes) : string.Join(", ", partes.Take(partes.Count - 1)) + " e " + partes[^1];
        var n = marcadas.Count;
        return (n == 1 ? "1 exigência do relatório" : n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " exigências do relatório")
               + (lista.Length == 0 ? "." : ": " + lista + ".");
    }

    /// <summary>O relatório que a correção automática usa a partir do painel: o que está escolhido na caixa ou, com "Sem relatório", o mais
    /// novo da pasta do processo; null se o processo não tem nenhum.</summary>
    public static string? RelatorioDoAutomatico(string? escolhido, IEnumerable<RelatorioEncontrado> daPasta)
    {
        ArgumentNullException.ThrowIfNull(daPasta);
        return string.IsNullOrEmpty(escolhido) ? MaisNovo(daPasta) : escolhido;
    }

    /// <summary>O rascunho tem trabalho do usuário que a correção automática apagaria (campo editado ou exigência escrita à mão ou pela IA).</summary>
    public static bool TemEdicoes(RascunhoDespacho? r) =>
        r is not null && (r.CamposEditados.Count > 0 || r.Pendencias.Any(p => p.ItemId.StartsWith("manual-", StringComparison.Ordinal) || !p.Incluida));

    /// <summary>"Gerar e conferir" só sem nada a preencher.</summary>
    public static bool PodeGerar(RascunhoDespacho r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Faltando().Count == 0;
    }

    /// <summary>"Gravar na pasta do processo": sem erros e, com PDF, com cada página (1..N, nenhuma a mais) conferida; sem PDF, o DOCX.</summary>
    public static bool PodeGravar(Preparacao p, IReadOnlyCollection<int> conferidas)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(conferidas);
        if (p.Erros.Count > 0) return false;
        if (p.Pdf is null) return p.Docx.Length > 0;
        var n = p.Paginas ?? 0;
        return n > 0 && conferidas.Distinct().Order().SequenceEqual(Enumerable.Range(1, n));
    }

    /// <summary>Trocar a pasta da etapa (outra data) monta um rascunho novo; o que o usuário já editou passa para ele: cabeçalho, exigências
    /// (texto, fundamento, grupo, marcada e a ordem) ou, na aprovação, o tipo de obra e a área.</summary>
    public static void TransferirEdicoes(RascunhoDespacho de, RascunhoDespacho para)
    {
        ArgumentNullException.ThrowIfNull(de);
        ArgumentNullException.ThrowIfNull(para);
        if (de.Tipo != para.Tipo) return;
        foreach (var (chave, valor) in de.TextosDoCorpo) para.EditarTrechoDoCorpo(chave, valor);
        para.DefinirCabecalho("protocolo", de.Protocolo);
        para.DefinirCabecalho("requerente", de.Requerente);
        para.DefinirCabecalho("assunto", de.Assunto);
        if (de.CamposEditados.Contains("cabecalho:data")) para.DefinirCabecalho("data", de.DataPorExtenso);
        foreach (var i in Enum.GetValues<Intervencao>()) para.DefinirIntervencao(i, de.Intervencoes.Contains(i));
        para.DefinirIntervencaoManual(de.IntervencaoManual);
        para.DefinirArea(de.AreaTotal);
        var ids = para.Pendencias.Select(p => p.ItemId).ToHashSet(StringComparer.Ordinal);
        var ordem = de.Pendencias.Where(p => ids.Contains(p.ItemId)).ToList();
        foreach (var p in ordem)
        {
            para.DefinirTexto(p.ItemId, p.Texto);
            para.DefinirFundamento(p.ItemId, p.Fundamento);
            para.DefinirGrupo(p.ItemId, p.Grupo);
            para.Marcar(p.ItemId, p.Incluida);
        }
        for (var i = 0; i < ordem.Count; i++)
        {
            var atual = para.Pendencias.ToList().FindIndex(x => x.ItemId == ordem[i].ItemId);
            if (atual != i) para.Mover(ordem[i].ItemId, i - atual);
        }
    }
}
