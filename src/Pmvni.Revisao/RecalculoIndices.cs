using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Correção 2026-11-17: os valores ligados por uma conta na tabela ÍNDICES URBANÍSTICOS acompanham a edição manual, sem IA.
/// Antes, só a situação da própria linha era refeita (valor × limite); mudar a área de projeção ou a do lote no "Cálculo / descrição"
/// não mexia no resultado da conta, no "No projeto" nem na situação, e as outras taxas continuavam com a área antiga.
/// <para>O que se liga a quê (tudo num <see cref="Lote"/>, um Desfazer):</para>
/// <list type="bullet">
/// <item>Cálculo de uma linha (<c>126,40 ÷ 360,00 × 100 = 35,1%</c>, <see cref="ContaIndice"/>): mudou um operando e o resultado ficou
/// como estava → o resultado é refeito (se o usuário mudou o resultado, vale o dele).</item>
/// <item>Resultado da conta → "No projeto", só se o valor no projeto era o da conta (concordava com o resultado antigo). Um valor
/// declarado diferente da conta ("10%" com a conta em 35,1%) é o que o projeto informa e não muda.</item>
/// <item>Área do lote: a do subtítulo da tabela ("Lote de 360,00 m²") ou a da linha "Área do terreno". Mudou o divisor de uma conta que
/// usava essa área, ou o valor da linha do terreno → as outras contas com a mesma área, a linha do terreno e a chamada "lote" do
/// diagrama acompanham.</item>
/// <item>Valor no projeto e limite → situação, como antes (<see cref="Comparar"/>). Só muda sozinha a situação que é comparação de
/// números: INFORMAÇÕES DIVERGENTES fica enquanto o valor no projeto e a conta não batem; FALTA COMPROVAR só muda quando o usuário
/// edita o valor ou o limite da própria linha.</item>
/// <item>Taxa de permeabilidade → a área permeável da planta do diagrama ("35% no projeto", "35,1% no cálculo"), quando o número escrito
/// lá é o antigo.</item>
/// </list>
/// O subtítulo da tabela não é texto editável no editor: a área dele não muda (a tela diz isso no aviso).</summary>
public static partial class RecalculoLinha
{
    const string CampoCalculo = "calculo";

    /// <summary>As mudanças de um recálculo, antes de virarem comandos: o que cada texto, situação e justificativa passa a ser.
    /// Lê primeiro o que já foi mudado aqui, depois o projeto.</summary>
    sealed class Plano(Projeto p)
    {
        public Projeto P { get; } = p;
        readonly List<string> ordem = [];
        readonly Dictionary<string, List<(string Campo, string Texto)>> textos = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> situacoes = new(StringComparer.Ordinal);
        readonly Dictionary<string, string?> justificativas = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Linhas => ordem;

        public string? Texto(string linha, string campo) =>
            textos.TryGetValue(linha, out var l) && l.FindLastIndex(x => x.Campo == campo) is var i and >= 0 ? l[i].Texto : TabelasRevisao.TextoEfetivo(P, linha, campo);

        public string? Situacao(string linha) => situacoes.TryGetValue(linha, out var s) ? s : TabelasRevisao.SituacaoEfetiva(P, linha);

        public bool Mudou(string linha, string campo) => textos.TryGetValue(linha, out var l) && l.Exists(x => x.Campo == campo);

        public bool MudouSituacao(string linha) => situacoes.ContainsKey(linha);

        void Tocar(string linha)
        {
            if (!ordem.Contains(linha)) ordem.Add(linha);
        }

        /// <summary>Muda o texto (que tem de existir na linha); false se não existe ou já era esse.</summary>
        public bool Definir(string linha, string campo, string texto)
        {
            if (!TabelasRevisao.Editavel(P, linha, campo) || Texto(linha, campo) == texto) return false;
            Tocar(linha);
            if (!textos.TryGetValue(linha, out var l)) textos[linha] = l = [];
            var i = l.FindIndex(x => x.Campo == campo);
            if (i >= 0) l[i] = (campo, texto);
            else l.Add((campo, texto));
            return true;
        }

        public void DefinirSituacao(string linha, string situacao)
        {
            Tocar(linha);
            situacoes[linha] = situacao;
        }

        public void DefinirJustificativa(string linha, string? justificativa)
        {
            Tocar(linha);
            justificativas[linha] = justificativa;
        }

        public List<Comando> Comandos()
        {
            var r = new List<Comando>();
            foreach (var linha in ordem)
            {
                if (textos.TryGetValue(linha, out var l)) r.AddRange(l.Select(x => (Comando)new DefinirCampo(linha, x.Campo, x.Texto)));
                if (situacoes.TryGetValue(linha, out var s)) r.Add(new DefinirSituacao(linha, s));
                if (justificativas.TryGetValue(linha, out var j)) r.Add(new DefinirJustificativa(linha, j));
            }
            return r;
        }
    }

    enum Estado { SemCriterio, Ilegivel, Mantida, Calculada }

    /// <summary>O que a comparação do valor com o limite deu numa linha.</summary>
    sealed record Avaliacao(Estado Estado, string TextoValor, string TextoLimite, bool Atende, string Diferenca, string? Situacao, bool MudaSituacao,
        bool MudaTexto, bool Divergente);

    /// <summary>A edição do valor, do limite ou do cálculo de uma linha de índice, com tudo o que depende dela.</summary>
    static EdicaoDeLinha AoEditarIndice(Projeto p, string linha, string campo, string texto, EdicaoDeLinha simples)
    {
        if (TabelasRevisao.Original(p, linha) is not { } original || !TabelasRevisao.Caminhos(original).Contains(campo)) return simples;
        var novo = TextoEditado.Normalizar(texto);
        var antes = TabelasRevisao.TextoEfetivo(p, linha, campo);
        if (novo.Length == 0 || novo == antes) return simples;   // vazio é recusado adiante; igual não muda nada

        var pl = new Plano(p);
        pl.Definir(linha, campo, novo);
        var rotulo = TabelasRevisao.Rotulo(linha);
        Avaliacao propria;
        var areaNova = (decimal?)null;
        if (campo == CampoCalculo)
        {
            if (ContaIndice.Ler(novo) is not { } digitada) return simples;   // um cálculo sem conta é texto livre
            var anterior = ContaIndice.Ler(antes);
            var final = novo;
            // Mudou um operando e o resultado ficou como estava: o resultado é refeito. Se o usuário escreveu outro resultado, vale o dele.
            if (anterior is not null && antes![anterior.PosResultado] == novo[digitada.PosResultado]
                && (anterior.Dividendo.Valor != digitada.Dividendo.Valor || anterior.Divisor.Valor != digitada.Divisor.Valor))
            {
                final = digitada.Refazer(novo);
                pl.Definir(linha, CampoCalculo, final);
            }
            var depois = ContaIndice.Ler(final)!;
            SeguirResultado(pl, linha, anterior, depois);
            if (anterior is not null && anterior.Divisor.Valor != depois.Divisor.Valor && UsaAreaDoLote(p, linha))
            {
                PropagarArea(pl, anterior.Divisor.Valor, depois.Divisor.Valor, linha, exigeVinculo: true);
                areaNova = depois.Divisor.Valor;
            }
            propria = Reavaliar(pl, linha, direta: false);
        }
        else
        {
            if (campo == CampoValor && EhTerreno(p, linha) && LerMedida(antes) is { Unidade: "m²" } a && LerMedida(novo) is { Unidade: "m²" } n && a.Valor != n.Valor)
            {
                PropagarArea(pl, a.Valor, n.Valor, linha, exigeVinculo: false);
                areaNova = n.Valor;
            }
            propria = Reavaliar(pl, linha, direta: true);
        }
        foreach (var l in pl.Linhas.ToList()) DiagramaAcompanha(pl, l);

        var comandos = pl.Comandos();
        var outras = pl.Linhas.Where(l => l != linha).ToList();
        var soAEdicao = comandos.Count == 1;
        var ilegivel = "o texto mudou, mas o editor não conseguiu comparar \"" + propria.TextoValor + "\" com \"" + propria.TextoLimite + "\"";

        // Só a própria linha, editando o valor ou o limite: as mensagens de sempre.
        if (campo != CampoCalculo && outras.Count == 0)
        {
            if (propria.Estado == Estado.Ilegivel)
                return new EdicaoDeLinha(simples.Comando, false, true,
                    rotulo + ": " + ilegivel + "; a situação ficou como estava. Confira-a, ou use «Rever com a IA» nas Propriedades.");
            if (propria.Estado == Estado.Mantida && propria.Divergente && soAEdicao)
                return new EdicaoDeLinha(simples.Comando, false, false,
                    rotulo + ": o valor no projeto (" + propria.TextoValor + ") continua diferente do resultado da conta no cálculo; a situação continua "
                    + ConferenciaRevisao.TextoSituacao(propria.Situacao) + ". Confira-a, ou use «Rever com a IA» nas Propriedades.");
            if (soAEdicao) return simples;
            if (propria.Estado == Estado.Calculada)
                return new EdicaoDeLinha(new Lote(linha, comandos, ResumoDaComparacao(propria)), true, false,
                    rotulo + " recalculado pelo editor (sem IA): " + propria.TextoValor + (propria.Atende ? " atende a " : " não atende a ") + propria.TextoLimite
                    + (propria.MudaSituacao ? "; situação " : "; a situação continua ") + ConferenciaRevisao.TextoSituacao(propria.Situacao)
                    + (propria.MudaTexto ? "; texto: \"" + propria.Diferenca + "\"" : "") + ". Um Desfazer volta tudo.");
        }
        if (soAEdicao) return simples;

        var partes = new List<string>();
        if (DescreverLinha(p, pl, linha, propria) is { Length: > 0 } daPropria) partes.Add(daPropria);
        if (propria.Estado == Estado.Ilegivel && campo != CampoCalculo) partes.Add(ilegivel + "; a situação ficou como estava");
        foreach (var l in outras)
            if (DescreverLinha(p, pl, l, null) is { Length: > 0 } t) partes.Add(TabelasRevisao.Rotulo(l) + ": " + t);
        if (areaNova is { } area && TemSubtituloComArea(p))
            partes.Add("o subtítulo da tabela não é editável e continua com a área antiga; corrija-o na análise se precisar (área nova: "
                       + Formatar(area, ContaIndice.CasasDeArea, "m²") + ")");
        var mensagem = rotulo + " recalculado pelo editor (sem IA): " + string.Join("; ", partes) + ". Um Desfazer volta tudo.";
        var resumo = campo != CampoCalculo && propria.Estado == Estado.Calculada
            ? ResumoDaComparacao(propria)
            : "Recalculado: " + TabelasRevisao.NomeDoCampo(campo) + " " + (pl.Texto(linha, campo) ?? novo);
        return new EdicaoDeLinha(new Lote(linha, comandos, resumo), true, false, mensagem);
    }

    static string ResumoDaComparacao(Avaliacao a) => "Recalculado: " + a.TextoValor + (a.Atende ? " atende a " : " não atende a ") + a.TextoLimite;

    /// <summary>O que mudou numa linha, em poucas palavras ("conta = 36,1%; no projeto 36,1%; situação ATENDE").</summary>
    static string DescreverLinha(Projeto p, Plano pl, string linha, Avaliacao? avaliacao)
    {
        var partes = new List<string>();
        if (TabelasRevisao.TentarLer(linha, out var bloco, out _) && bloco != TabelasRevisao.Indices)
            return pl.Linhas.Contains(linha) ? "texto atualizado (" + string.Join(", ", CamposMudados(p, pl, linha)) + ")" : "";
        if (pl.Mudou(linha, CampoCalculo) && ContaIndice.Ler(pl.Texto(linha, CampoCalculo)) is { } c)
            partes.Add("conta = " + Formatar(c.Resultado.Valor, c.Resultado.Casas, c.Resultado.Unidade));
        if (pl.Mudou(linha, CampoValor)) partes.Add("no projeto " + pl.Texto(linha, CampoValor));
        var sit = pl.Situacao(linha);
        if (pl.MudouSituacao(linha)) partes.Add("situação " + ConferenciaRevisao.TextoSituacao(sit));
        else if (avaliacao is { Estado: Estado.Mantida, Divergente: true })
            partes.Add("a situação continua " + ConferenciaRevisao.TextoSituacao(sit) + " (o valor no projeto não bate com a conta)");
        else if (avaliacao is { Estado: Estado.Calculada or Estado.Mantida }) partes.Add("a situação continua " + ConferenciaRevisao.TextoSituacao(sit));
        return string.Join("; ", partes);
    }

    static IEnumerable<string> CamposMudados(Projeto p, Plano pl, string linha) =>
        TabelasRevisao.Original(p, linha) is { } o
            ? TabelasRevisao.Caminhos(o).Where(c => pl.Mudou(linha, c)).Select(TabelasRevisao.NomeDoCampo)
            : [];

    // ---------- a comparação de uma linha ----------

    /// <summary>Compara o valor com o limite da linha (com o que o plano já mudou) e escreve a situação, o texto da diferença e a
    /// justificativa do editor. <paramref name="direta"/>: o usuário editou o valor ou o limite desta linha (FALTA COMPROVAR pode virar
    /// ATENDE/NÃO ATENDE); senão a linha só acompanha outra edição e só a situação numérica muda.</summary>
    static Avaliacao Reavaliar(Plano pl, string linha, bool direta)
    {
        var p = pl.P;
        var textoValor = pl.Texto(linha, CampoValor) ?? "";
        var textoLimite = pl.Texto(linha, CampoLimite) ?? "";
        var situacao = pl.Situacao(linha);
        var caminhos = TabelasRevisao.Original(p, linha) is { } o ? TabelasRevisao.Caminhos(o) : [];
        if (situacao is null || !caminhos.Contains(CampoValor) || !caminhos.Contains(CampoLimite))
            return new Avaliacao(Estado.SemCriterio, textoValor, textoLimite, false, "", situacao, false, false, false);
        if (LerMedida(textoValor) is not { } valor || LerCriterio(textoLimite) is not { } criterio || Comparar(valor, criterio) is not var (atende, diferenca))
            return new Avaliacao(Estado.Ilegivel, textoValor, textoLimite, false, "", situacao, false, false, false);

        var conta = ContaIndice.Ler(pl.Texto(linha, CampoCalculo));
        var divergente = conta is not null && valor.Unidade == conta.Resultado.Unidade && !conta.ConcordaCom(valor);
        var mantem = situacao switch
        {
            "DIVERGENCIA" => divergente || !direta && conta is null,   // a divergência é juízo da análise enquanto os números não batem
            "PENDENTE" => !direta,                                     // falta comprovar: só o usuário, editando esta linha, resolve
            _ => !direta && divergente,
        };
        if (mantem) return new Avaliacao(Estado.Mantida, textoValor, textoLimite, atende, diferenca, situacao, false, false, divergente);

        var novaSituacao = atende ? "CONTA_CONFERE" : "NAO_ATENDE";
        var textoAtual = pl.Texto(linha, CampoTexto);
        var mudaTexto = false;
        if (caminhos.Contains(CampoTexto) && textoAtual is not null)
        {
            // Continua não atendendo: a citação "(item NN)" do texto antigo acompanha a nova diferença.
            var citacao = !atende && CitacaoFinal().Match(textoAtual) is { Success: true } m ? m.Value.TrimEnd() : "";
            var novoTexto = citacao.Length > 0 ? diferenca.TrimEnd('.') + citacao + "." : diferenca;
            // O texto só é refeito quando a situação muda ou quando ele já era um texto de diferença (o editor não apaga um texto livre).
            if (novoTexto != textoAtual && (novaSituacao != situacao || EhTextoDeDiferenca(textoAtual)))
                mudaTexto = pl.Definir(linha, CampoTexto, novoTexto);
        }
        var mudaSituacao = novaSituacao != situacao;
        if (mudaSituacao) pl.DefinirSituacao(linha, novaSituacao);
        if (mudaSituacao || mudaTexto)
        {
            // A conferência final exige justificativa quando a situação difere da original: o editor escreve a dele, se o usuário não escreveu.
            var justificativa = TabelasRevisao.Justificativa(p, linha);
            if (string.IsNullOrWhiteSpace(justificativa) || justificativa!.StartsWith(MarcaJustificativa, StringComparison.Ordinal))
            {
                var volta = novaSituacao == TabelasRevisao.SituacaoOriginal(p, linha);
                pl.DefinirJustificativa(linha, volta ? null : MarcaJustificativa + " no projeto " + textoValor + "; exigido " + textoLimite + ".");
            }
        }
        return new Avaliacao(Estado.Calculada, textoValor, textoLimite, atende, diferenca, novaSituacao, mudaSituacao, mudaTexto, divergente);
    }

    // ---------- o que acompanha a conta ----------

    /// <summary>O resultado da conta mudou: o "No projeto" acompanha, se era o resultado antigo (concordava com ele). Só o número muda
    /// ("35,1%" → "36,1%"; o resto do texto fica).</summary>
    static void SeguirResultado(Plano pl, string linha, ContaIndice? antes, ContaIndice depois)
    {
        if (antes is null) return;
        var texto = pl.Texto(linha, CampoValor);
        if (LerMedida(texto) is not { } valor || valor.Unidade != antes.Resultado.Unidade || !antes.ConcordaCom(valor)) return;
        if (antes.Resultado.Valor == depois.Resultado.Valor || depois.ConcordaCom(valor) && valor.Casas >= depois.Resultado.Casas) return;
        if (TrocarNumero(texto!, valor.Valor, valor.Casas, null, Formatar(depois.Resultado.Valor, depois.Resultado.Casas, "")) is { } novo)
            pl.Definir(linha, CampoValor, novo);
    }

    /// <summary>A área do lote mudou de <paramref name="antiga"/> para <paramref name="nova"/>: as contas das outras linhas que dividem
    /// por ela (e, com <paramref name="exigeVinculo"/>, que já dividiam pela área do lote no relatório recebido), a linha do terreno e a
    /// chamada "lote" do diagrama acompanham.</summary>
    static void PropagarArea(Plano pl, decimal antiga, decimal nova, string exceto, bool exigeVinculo)
    {
        var p = pl.P;
        foreach (var id in TabelasRevisao.Ids(p))
        {
            if (id == exceto || !TabelasRevisao.TentarLer(id, out var bloco, out _)) continue;
            if (bloco == TabelasRevisao.Chamadas)
            {
                if (TabelasRevisao.Original(p, id)?["ancora"] is JsonValue v && v.TryGetValue<string>(out var ancora) && ancora == "lote"
                    && TrocarNumero(pl.Texto(id, CampoTexto), antiga, null, "m²", Formatar(nova, ContaIndice.CasasDeArea, "")) is { } t)
                    pl.Definir(id, CampoTexto, t);
                continue;
            }
            if (bloco != TabelasRevisao.Indices) continue;
            if (EhTerreno(p, id))
            {
                if (LerMedida(pl.Texto(id, CampoValor)) is { Unidade: "m²" } m && m.Valor == antiga
                    && TrocarNumero(pl.Texto(id, CampoValor), antiga, null, null, Formatar(nova, ContaIndice.CasasDeArea, "")) is { } t)
                {
                    pl.Definir(id, CampoValor, t);
                    Reavaliar(pl, id, direta: false);
                }
                continue;
            }
            var calculo = pl.Texto(id, CampoCalculo);
            if (ContaIndice.Ler(calculo) is not { } conta || conta.Divisor.Valor != antiga || exigeVinculo && !UsaAreaDoLote(p, id)) continue;
            var novoCalculo = conta.TrocarDivisor(calculo!, nova);
            if (!pl.Definir(id, CampoCalculo, novoCalculo)) continue;
            SeguirResultado(pl, id, conta, ContaIndice.Ler(novoCalculo)!);
            Reavaliar(pl, id, direta: false);
        }
    }

    /// <summary>A taxa de permeabilidade mudou: a área permeável da planta do diagrama ("35% no projeto" / "35,1% no cálculo") acompanha,
    /// quando o número escrito lá é o antigo.</summary>
    static void DiagramaAcompanha(Plano pl, string linha)
    {
        var p = pl.P;
        if (!TabelasRevisao.TentarLer(linha, out var bloco, out _) || bloco != TabelasRevisao.Indices || !EhPermeabilidade(p, linha)) return;
        var planta = TabelasRevisao.Id(TabelasRevisao.Planta, 1);
        if (!TabelasRevisao.Existe(p, planta)) return;
        if (pl.Mudou(linha, CampoValor) && LerMedida(TabelasRevisao.TextoEfetivo(p, linha, CampoValor)) is { } va && LerMedida(pl.Texto(linha, CampoValor)) is { } vd
            && TrocarNumero(pl.Texto(planta, "permeavel/linha1"), va.Valor, va.Casas, "%", Formatar(vd.Valor, vd.Casas, "")) is { } l1)
            pl.Definir(planta, "permeavel/linha1", l1);
        if (pl.Mudou(linha, CampoCalculo) && ContaIndice.Ler(TabelasRevisao.TextoEfetivo(p, linha, CampoCalculo)) is { } ca
            && ContaIndice.Ler(pl.Texto(linha, CampoCalculo)) is { } cd && ca.Resultado.Valor != cd.Resultado.Valor
            && TrocarNumero(pl.Texto(planta, "permeavel/linha2"), ca.Resultado.Valor, ca.Resultado.Casas, "%", Formatar(cd.Resultado.Valor, cd.Resultado.Casas, "")) is { } l2)
            pl.Definir(planta, "permeavel/linha2", l2);
    }

    /// <summary>O texto com o número que vale <paramref name="valor"/> (na precisão <paramref name="casas"/>, ou exato) trocado por
    /// <paramref name="novo"/>; com <paramref name="unidade"/>, só o número seguido dela. Null se não há exatamente um número assim.</summary>
    static string? TrocarNumero(string? texto, decimal valor, int? casas, string? unidade, string novo)
    {
        if (string.IsNullOrEmpty(texto)) return null;
        var achados = new List<Match>();
        foreach (Match m in Numeros().Matches(texto))
        {
            var bruto = m.Value.TrimEnd('.', ',');
            if (LerNumero(bruto) is not var (v, c)) continue;
            if (casas is { } k ? !ContaIndice.Concorda(v, c, valor, k) : v != valor) continue;
            if (unidade is not null)
            {
                var depois = texto[(m.Index + bruto.Length)..].TrimStart();
                var ok = unidade == "m²" ? depois.StartsWith("m²", StringComparison.Ordinal) || depois.StartsWith("m2", StringComparison.OrdinalIgnoreCase) : depois.StartsWith(unidade, StringComparison.Ordinal);
                if (!ok) continue;
            }
            achados.Add(m);
        }
        if (achados.Count != 1) return null;
        var a = achados[0];
        var tamanho = a.Value.TrimEnd('.', ',').Length;
        return texto[..a.Index] + novo + texto[(a.Index + tamanho)..];
    }

    // ---------- que linha é qual ----------

    static string Nome(Projeto p, string linha) =>
        SemAcento(TabelasRevisao.TextoOriginal(p, linha, "nome") ?? "") + " " + SemAcento(TabelasRevisao.TextoEfetivo(p, linha, "nome") ?? "");

    static string SemAcento(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    static string? Icone(Projeto p, string linha) =>
        TabelasRevisao.Original(p, linha)?["icone"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A linha "Área do terreno" (ícone <c>plot</c> do kit, ou o nome diz terreno/área do lote) com o valor em m².</summary>
    static bool EhTerreno(Projeto p, string linha)
    {
        if (LerMedida(TabelasRevisao.TextoOriginal(p, linha, CampoValor)) is not { Unidade: "m²" }) return false;
        var nome = Nome(p, linha);
        return Icone(p, linha) == "plot" || nome.Contains("terreno", StringComparison.Ordinal) || nome.Contains("area do lote", StringComparison.Ordinal);
    }

    static bool EhPermeabilidade(Projeto p, string linha) => Icone(p, linha) == "tp" || Nome(p, linha).Contains("permeab", StringComparison.Ordinal);

    [GeneratedRegex(@"(?<n>[0-9]+(?:[.,][0-9]+)*)\s*(?:m²|m2|M2)", RegexOptions.CultureInvariant)]
    private static partial Regex AreaEmM2();

    /// <summary>A área do lote no relatório recebido: o valor da linha do terreno, senão a única área em m² do subtítulo da tabela
    /// ("Lote de 360,00 m² (matrícula) • zona ZOC 2."). Null se não se sabe.</summary>
    static decimal? AreaDoLoteOriginal(Projeto p)
    {
        foreach (var id in TabelasRevisao.Ids(p))
            if (TabelasRevisao.TentarLer(id, out var b, out _) && b == TabelasRevisao.Indices && EhTerreno(p, id)
                && LerMedida(TabelasRevisao.TextoOriginal(p, id, CampoValor)) is { Unidade: "m²" } m)
                return m.Valor;
        if (SubtituloDaTabela(p) is not { } sub) return null;
        var ms = AreaEmM2().Matches(sub);
        return ms.Count == 1 && LerNumero(ms[0].Groups["n"].Value) is var (v, _) ? v : null;
    }

    static string? SubtituloDaTabela(Projeto p) =>
        (p.Documento["indices"] as JsonObject)?["subtitulo"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static bool TemSubtituloComArea(Projeto p) => SubtituloDaTabela(p) is { } s && AreaEmM2().IsMatch(s);

    /// <summary>A conta da linha, como veio no relatório, divide pela área do lote (é uma taxa ou coeficiente sobre o terreno).</summary>
    static bool UsaAreaDoLote(Projeto p, string linha) =>
        AreaDoLoteOriginal(p) is { } area && ContaIndice.Ler(TabelasRevisao.TextoOriginal(p, linha, CampoCalculo)) is { } c && c.Divisor.Valor == area;
}
