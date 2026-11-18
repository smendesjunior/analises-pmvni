using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Tipos de obra da aprovação (caixas, nesta ordem).</summary>
public enum Intervencao { ObraNova, AlteracaoDeObra, AmpliacaoDeObra }

/// <summary>Uma exigência do despacho de correção, derivada de um item do relatório (situação ≠ ATENDE). Só o <see cref="RascunhoDespacho"/> a muda.</summary>
public sealed class PendenciaDespacho
{
    public string ItemId { get; internal set; } = "";
    public int Numero { get; internal set; }
    public string Situacao { get; internal set; } = "";
    public string Titulo { get; internal set; } = "";
    public string Grupo { get; internal set; } = "";
    public string Texto { get; internal set; } = "";
    public string Fundamento { get; internal set; } = "";
    public bool Incluida { get; internal set; } = true;
    public string TextoOriginal { get; internal set; } = "";
    public string FundamentoOriginal { get; internal set; } = "";
    internal string GrupoOriginal { get; set; } = "";

    /// <summary>O texto ou o fundamento difere do que veio do relatório.</summary>
    public bool Editada => Texto != TextoOriginal || Fundamento != FundamentoOriginal;

    internal string Rotulo => "ITEM " + Numero.ToString("D2", CultureInfo.InvariantCulture);
}

/// <summary>Falta preencher algo (ver <see cref="RascunhoDespacho.Faltando"/>): o despacho não é congelado.</summary>
public sealed class DespachoIncompletoException(IReadOnlyList<string> faltando) : Exception("Falta preencher: " + string.Join("; ", faltando))
{
    public IReadOnlyList<string> Faltando { get; } = faltando;
}

/// <summary>Exigência pronta para o DOCX: o texto e, num segundo run, o fundamento (<c>" (fundamento)."</c>).</summary>
public sealed record ExigenciaFinal(string Texto, string Fundamento);

public sealed record GrupoFinal(string Titulo, IReadOnlyList<ExigenciaFinal> Exigencias);

/// <summary>O despacho congelado: o que vai para o DOCX. <see cref="Corpo"/>: aprovação = os 4 parágrafos fixos resolvidos; correção = o fechamento.</summary>
public sealed record DespachoFinal(TipoDespacho Tipo, string Protocolo, string Requerente, string Assunto, string DataPorExtenso,
    IReadOnlyList<GrupoFinal> Grupos, IReadOnlyList<string> Corpo);

public enum EstiloPrevia { Titulo, Rotulo, Destinatario, Grupo, Corpo, Data, Assinatura, Vazio }

/// <summary>Um parágrafo da prévia de texto. <see cref="Faltando"/> preenchido = campo a preencher (o texto leva "[preencher: …]", só na tela).</summary>
/// <see cref="Chave"/>: o campo editável por trás do parágrafo (<c>cabecalho:protocolo|requerente|assunto</c> ou <c>exigencia:&lt;id&gt;</c>), para editar
/// direto na prévia; <see cref="Valor"/> é o texto do campo (sem rótulo nem fundamento).
/// <see cref="Trechos"/>: o parágrafo em trechos, como no DOCX (negrito do valor, número da exigência); um trecho com <see cref="TrechoPrevia.Chave"/>
/// é editável clicando nele na folha. <see cref="Modelo"/>: o índice do parágrafo do modelo cuja formatação vale (−1 = o padrão).
public sealed record ParagrafoPrevia(string Texto, EstiloPrevia Estilo, string? Faltando, string? Chave = null, string? Valor = null,
    IReadOnlyList<TrechoPrevia>? Trechos = null, int Modelo = -1)
{
    /// <summary>Os trechos do parágrafo; sem trechos definidos, o texto todo num trecho só (editável pela <see cref="Chave"/>, se houver).</summary>
    public IReadOnlyList<TrechoPrevia> TrechosOuTexto => Trechos ?? [new TrechoPrevia(Texto, false, Chave, Faltando, Valor)];
}

/// <summary>Um trecho de um parágrafo da prévia. <see cref="Chave"/> preenchida = editável na folha (ver <see cref="ConteudoDespacho.AplicarEdicao"/>);
/// <see cref="Valor"/> é o texto que o editor recebe (sem o "[preencher: …]" nem a pontuação do fundamento); <see cref="Faltando"/> = a preencher.</summary>
public sealed record TrechoPrevia(string Texto, bool Negrito = false, string? Chave = null, string? Faltando = null, string? Valor = null);

/// <summary>O despacho em edição. Todo texto do usuário passa por <see cref="TextoEditado.Normalizar"/> (texto puro, uma linha).</summary>
public sealed class RascunhoDespacho
{
    readonly List<PendenciaDespacho> _pendencias;
    readonly HashSet<Intervencao> _intervencoes = new();
    readonly string _protocoloOriginal, _requerenteOriginal, _assuntoOriginal;

    internal RascunhoDespacho(TipoDespacho tipo, string protocolo, string requerente, string assunto, DateOnly data, List<PendenciaDespacho> pendencias)
    {
        Tipo = tipo;
        Protocolo = _protocoloOriginal = protocolo;
        Requerente = _requerenteOriginal = requerente;
        Assunto = _assuntoOriginal = assunto;
        Data = data;
        _pendencias = pendencias;
    }

    public TipoDespacho Tipo { get; }
    public string Protocolo { get; private set; }
    public string Requerente { get; private set; }
    public string Assunto { get; private set; }
    public DateOnly Data { get; }
    public IReadOnlyList<PendenciaDespacho> Pendencias => _pendencias;
    public IReadOnlySet<Intervencao> Intervencoes => _intervencoes;
    public string AreaTotal { get; private set; } = "";

    /// <summary>A data por extenso que vai no despacho: a da pasta da etapa ou a escrita na prévia (correções 2026-11-15).</summary>
    public string DataPorExtenso => _dataEditada ?? Etapas.DataPorExtenso(Data);
    string? _dataEditada;

    /// <summary>Os campos que diferem do que veio do relatório e do modelo: <c>cabecalho:protocolo|requerente|assunto</c> e
    /// <c>exigencia:&lt;id&gt;:texto|fundamento|grupo</c>.</summary>
    public IReadOnlySet<string> CamposEditados
    {
        get
        {
            var s = new SortedSet<string>(StringComparer.Ordinal);
            if (Protocolo != _protocoloOriginal) s.Add("cabecalho:protocolo");
            if (Requerente != _requerenteOriginal) s.Add("cabecalho:requerente");
            if (Assunto != _assuntoOriginal) s.Add("cabecalho:assunto");
            if (_dataEditada is not null) s.Add("cabecalho:data");
            foreach (var p in _pendencias)
            {
                if (p.Texto != p.TextoOriginal) s.Add("exigencia:" + p.ItemId + ":texto");
                if (p.Fundamento != p.FundamentoOriginal) s.Add("exigencia:" + p.ItemId + ":fundamento");
                if (p.Grupo != p.GrupoOriginal) s.Add("exigencia:" + p.ItemId + ":grupo");
            }
            return s;
        }
    }

    public void DefinirCabecalho(string campo, string texto)
    {
        var t = TextoEditado.Normalizar(texto);
        switch (campo)
        {
            case "protocolo": Protocolo = t; break;
            case "requerente": Requerente = t; break;
            case "assunto": Assunto = t; break;
            case "data": _dataEditada = t == Etapas.DataPorExtenso(Data) ? null : t; break;
            default: throw new ArgumentException("Campo de cabeçalho desconhecido: " + campo, nameof(campo));
        }
    }

    PendenciaDespacho Pendencia(string itemId) =>
        _pendencias.FirstOrDefault(p => p.ItemId == itemId) ?? throw new ArgumentException("Exigência desconhecida: " + itemId, nameof(itemId));

    public void DefinirTexto(string itemId, string texto) => Pendencia(itemId).Texto = TextoEditado.Normalizar(texto);

    public void DefinirFundamento(string itemId, string texto) => Pendencia(itemId).Fundamento = TextoEditado.Normalizar(texto);

    public void DefinirGrupo(string itemId, string texto) => Pendencia(itemId).Grupo = TextoEditado.Normalizar(texto);

    /// <summary>Troca o título de um grupo em todas as exigências dele (edição do título na prévia).</summary>
    public void RenomearGrupo(string antigo, string novo)
    {
        var t = TextoEditado.Normalizar(novo);
        foreach (var p in _pendencias.Where(p => p.Grupo == antigo)) p.Grupo = t;
    }

    public void Marcar(string itemId, bool incluida) => Pendencia(itemId).Incluida = incluida;

    /// <summary>O grupo das exigências novas (o título do grupo do modelo).</summary>
    public string GrupoPadrao { get; init; } = "";

    /// <summary>Exigência escrita à mão ou pela IA (sem item do relatório). Devolve o id.</summary>
    public string AdicionarExigencia(string texto = "", string fundamento = "", string? grupo = null)
    {
        var numero = _pendencias.Count == 0 ? 1 : _pendencias.Max(p => p.Numero) + 1;
        var id = "manual-" + Guid.NewGuid().ToString("N")[..8];
        var t = TextoEditado.Normalizar(texto);
        var f = TextoEditado.Normalizar(fundamento);
        var g = TextoEditado.Normalizar(grupo ?? GrupoPadrao);
        _pendencias.Add(new PendenciaDespacho
        {
            ItemId = id, Numero = numero, Situacao = "NAO ATENDE", Titulo = "", Grupo = g, GrupoOriginal = g,
            Texto = t, TextoOriginal = "", Fundamento = f, FundamentoOriginal = "",
        });
        return id;
    }

    public void RemoverExigencia(string itemId) => _pendencias.Remove(Pendencia(itemId));

    /// <summary>Troca todas as exigências (resposta da IA a uma instrução de modificação).</summary>
    public void SubstituirExigencias(IEnumerable<(string Grupo, string Texto, string Fundamento)> novas)
    {
        _pendencias.Clear();
        foreach (var (g, t, f) in novas) AdicionarExigencia(t, f, string.IsNullOrWhiteSpace(g) ? null : g);
    }

    /// <summary>Move a exigência <paramref name="delta"/> posições (negativo = para cima), parando nas pontas.</summary>
    public void Mover(string itemId, int delta)
    {
        var p = Pendencia(itemId);
        var i = _pendencias.IndexOf(p);
        var j = Math.Clamp(i + delta, 0, _pendencias.Count - 1);
        if (i == j) return;
        _pendencias.RemoveAt(i);
        _pendencias.Insert(j, p);
    }

    public void DefinirIntervencao(Intervencao i, bool marcada)
    {
        if (marcada) _intervencoes.Add(i);
        else _intervencoes.Remove(i);
    }

    /// <summary>Tipo de obra digitado à mão (além dos três de marcar); "" = nenhum.</summary>
    public string IntervencaoManual { get; private set; } = "";

    public void DefinirIntervencaoManual(string? texto) => IntervencaoManual = TextoEditado.Normalizar(texto ?? "");

    /// <summary>O tipo de obra como entra no texto: os marcados e o digitado.</summary>
    public string TextoIntervencao => ConteudoDespacho.Intervencoes(_intervencoes, IntervencaoManual);

    public void DefinirArea(string texto) => AreaTotal = TextoEditado.Normalizar(texto);

    /// <summary>O que o usuário ainda precisa preencher, na ordem da tela; vazio = pode gerar.</summary>
    public IReadOnlyList<string> Faltando()
    {
        var f = new List<string>();
        if (Protocolo.Length == 0) f.Add(ConteudoDespacho.FaltaProtocolo);
        if (Requerente.Length == 0) f.Add(ConteudoDespacho.FaltaRequerente);
        if (Assunto.Length == 0) f.Add(ConteudoDespacho.FaltaAssunto);
        if (DataPorExtenso.Length == 0) f.Add(ConteudoDespacho.FaltaData);
        if (Tipo == TipoDespacho.Correcao)
        {
            var marcadas = _pendencias.Where(p => p.Incluida).ToList();
            if (marcadas.Count == 0) f.Add(ConteudoDespacho.FaltaExigencia);
            foreach (var p in marcadas)
            {
                if (p.Texto.Length == 0) f.Add(p.Rotulo + ": texto da exigência");
                if (p.Grupo.Length == 0) f.Add(p.Rotulo + ": grupo");
            }
        }
        else
        {
            if (_intervencoes.Count == 0 && IntervencaoManual.Length == 0) f.Add(ConteudoDespacho.FaltaIntervencao);
            if (!ConteudoDespacho.AreaValida(AreaTotal)) f.Add(ConteudoDespacho.FaltaArea);
        }
        return f;
    }

    /// <summary>O despacho pronto para o DOCX. Lança <see cref="DespachoIncompletoException"/> se <see cref="Faltando"/> não estiver vazio.</summary>
    public DespachoFinal Congelar(TextosFixos tx)
    {
        ArgumentNullException.ThrowIfNull(tx);
        if (tx.Tipo != Tipo) throw new ArgumentException("Os textos fixos são de outro tipo de despacho.", nameof(tx));
        var faltando = Faltando();
        if (faltando.Count > 0) throw new DespachoIncompletoException(faltando);

        var grupos = new List<GrupoFinal>();
        var corpo = new List<string>();
        if (Tipo == TipoDespacho.Correcao)
        {
            var marcadas = _pendencias.Where(p => p.Incluida).ToList();
            foreach (var titulo in marcadas.Select(p => p.Grupo).Distinct(StringComparer.Ordinal))
                grupos.Add(new GrupoFinal(titulo,
                    marcadas.Where(p => p.Grupo == titulo).Select(p => ConteudoDespacho.Exigencia(p.Texto, p.Fundamento)).ToList()));
            corpo.Add(tx.Paragrafos.Single(p => p.Id == "fechamento").Texto);
        }
        else
            corpo.AddRange(tx.Paragrafos.Select(p => ConteudoDespacho.Resolver(p.Texto, TextoIntervencao, AreaTotal)));
        return new DespachoFinal(Tipo, Protocolo, Requerente, Assunto, DataPorExtenso, grupos, corpo);
    }
}

/// <summary>O conteúdo do despacho derivado do relatório revisado (desenho §10.3): determinístico, sem IA, sem nada inventado. O que falta vira
/// campo que o usuário preenche.</summary>
public static class ConteudoDespacho
{
    internal const string FaltaProtocolo = "Protocolo nº", FaltaRequerente = "Requerente", FaltaAssunto = "Assunto",
        FaltaExigencia = "Ao menos uma exigência marcada", FaltaData = "Data",
        FaltaIntervencao = "Tipo de obra (obra nova, alteração de obra ou ampliação de obra)",
        FaltaArea = "Área total a construir (m², ex.: 210,52)";

    /// <summary>A regra <c>numero_ano</c> do kit para o processo da capa.</summary>
    static readonly Regex NumeroAno = new(@"^\s*0*(\d{1,7})\s*(?:[/\-]|\s)\s*((?:19|20)\d{2})\s*$", RegexOptions.CultureInvariant);
    static readonly Regex Area = new(@"^(\d{1,3}(\.\d{3})+|\d+),\d{2}$", RegexOptions.CultureInvariant);

    static readonly string[] NomesIntervencao = { "obra nova", "alteração de obra", "ampliação de obra" };

    public static string Intervencoes(IEnumerable<Intervencao> i) => Intervencoes(i, null);

    /// <summary>Com <paramref name="manual"/> (o tipo digitado à mão) no fim da lista.</summary>
    public static string Intervencoes(IEnumerable<Intervencao> i, string? manual)
    {
        ArgumentNullException.ThrowIfNull(i);
        var nomes = i.Distinct().Order().Select(x => NomesIntervencao[(int)x]).ToList();
        if (!string.IsNullOrWhiteSpace(manual)) nomes.Add(manual.Trim());
        return nomes.Count switch
        {
            0 => "",
            1 => nomes[0],
            _ => string.Join(", ", nomes.Take(nomes.Count - 1)) + " e " + nomes[^1],
        };
    }

    public static bool AreaValida(string area) => area is not null && Area.IsMatch(area);

    /// <summary>Aparar; tirar um ponto final do fundamento; sem fundamento, o texto como está; senão o ponto final do texto passa para depois
    /// do parêntese: <c>("Apresentar a cota", " (LC 70/1990).")</c>.</summary>
    public static ExigenciaFinal Exigencia(string providencia, string baseLegal)
    {
        ArgumentNullException.ThrowIfNull(providencia);
        ArgumentNullException.ThrowIfNull(baseLegal);
        var texto = providencia.Trim();
        var fundamento = baseLegal.Trim();
        if (fundamento.EndsWith('.')) fundamento = fundamento[..^1].TrimEnd();
        if (fundamento.Length == 0) return new ExigenciaFinal(texto, "");
        return texto.EndsWith('.')
            ? new ExigenciaFinal(texto[..^1], " (" + fundamento + ").")
            : new ExigenciaFinal(texto, " (" + fundamento + ")");
    }

    internal static string Resolver(string texto, string intervencao, string area) =>
        texto.Replace(TextosFixos.LacunaIntervencao, intervencao, StringComparison.Ordinal).Replace(TextosFixos.LacunaArea, area, StringComparison.Ordinal);

    /// <summary>(número, ano) do processo da capa pela regra do kit; null se fora do formato.</summary>
    internal static (int Numero, int Ano)? ProcessoDaCapa(string? capa)
    {
        if (capa is null) return null;
        var m = NumeroAno.Match(capa);
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Os itens efetivos (só os incluídos, na ordem, com texto e situação revisados) com o id de cada um.</summary>
    static List<(string Id, int Numero, JsonObject Item)> Itens(Projeto p, out JsonObject documento)
    {
        var g = DadosEfetivos.Gerar(p);
        documento = JsonFiel.Ler(g.Json);
        var itens = (documento["itens"] as JsonArray ?? new JsonArray()).ToList();
        return itens.Select((x, i) => (g.IdsDosItens[i], i + 1, x as JsonObject ?? new JsonObject())).ToList();
    }

    static string SituacaoDe(JsonObject item) => Texto(item["situacao"]) ?? item["situacao"]?.ToJsonString() ?? "";

    /// <summary>Despacho sem relatório (correções 2026-11-15): o cabeçalho do processo e nenhuma exigência (o usuário escreve ou pede à IA).</summary>
    public static RascunhoDespacho MontarEmBranco(TipoDespacho t, InfoModelo m, DateOnly data, string protocolo, string requerente)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (t is not (TipoDespacho.Correcao or TipoDespacho.Aprovacao)) throw new ArgumentOutOfRangeException(nameof(t), t, "Tipo de despacho não emitido.");
        return new RascunhoDespacho(t, protocolo ?? "", requerente ?? "", m.Assunto, data, []) { GrupoPadrao = m.TituloGrupo };
    }

    public static RascunhoDespacho Montar(TipoDespacho t, Projeto p, InfoModelo m, EtapaDespacho etapa)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(etapa);
        if (t is not (TipoDespacho.Correcao or TipoDespacho.Aprovacao)) throw new ArgumentOutOfRangeException(nameof(t), t, "Tipo de despacho não emitido.");
        if (m.Tipo != t) throw new ArgumentException("O modelo é de outro tipo de despacho.", nameof(m));
        var itens = Itens(p, out var doc);
        var capa = doc["capa"] as JsonObject;
        var pendencias = new List<PendenciaDespacho>();
        if (t == TipoDespacho.Correcao)
            foreach (var (id, numero, item) in itens)
            {
                var situacao = SituacaoDe(item);
                if (situacao == "ATENDE") continue;
                var texto = TextoEditado.Normalizar(Texto(item["providencia"]) ?? "");
                var fundamento = TextoEditado.Normalizar(Texto(item["base_legal"]) ?? "");
                pendencias.Add(new PendenciaDespacho
                {
                    ItemId = id, Numero = numero, Situacao = situacao, Titulo = Texto(item["titulo"]) ?? "",
                    Grupo = m.TituloGrupo, GrupoOriginal = m.TituloGrupo,
                    Texto = texto, TextoOriginal = texto, Fundamento = fundamento, FundamentoOriginal = fundamento,
                });
            }
        return new RascunhoDespacho(t, Texto(capa?["processo"]) ?? "", Texto(capa?["requerente"]) ?? "", m.Assunto, etapa.Data, pendencias) { GrupoPadrao = m.TituloGrupo };
    }

    /// <summary>O que não deixa abrir o despacho (desenho §10.3): erros da conferência da revisão, processo da capa diferente do da pasta ou
    /// fora do formato, pacote somente leitura e correção sem nenhum item pendente.</summary>
    public static IReadOnlyList<string> Bloqueios(TipoDespacho t, Projeto p, Processo processo, bool somenteLeitura)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(processo);
        var b = new List<string>(ConferenciaRevisao.Erros(p));
        var itens = Itens(p, out var doc);
        var capa = Texto((doc["capa"] as JsonObject)?["processo"]);
        var lido = ProcessoDaCapa(capa);
        if (processo.Numero is null && processo.Protocolo is { } protocolo)
        {
            if (!string.Equals(capa?.Trim(), protocolo, StringComparison.OrdinalIgnoreCase))
                b.Add($"O processo da capa ({capa}) difere do processo da pasta ({protocolo}).");
        }
        else if (lido is null)
            b.Add("O processo da capa (" + capa + ") não está no formato número/ano; confira o relatório.");
        else if (processo.Numero is { } n && processo.Ano is { } a && (lido.Value.Numero != n || lido.Value.Ano != a))
            b.Add("O processo da capa (" + capa + ") não é o da pasta ("
                  + string.Create(CultureInfo.InvariantCulture, $"{n:D4}/{a}") + "). Confira se o relatório é deste processo.");
        if (somenteLeitura)
            b.Add("Este relatório foi gravado por uma versão mais nova do editor (somente leitura); o despacho não pode ser emitido a partir dele.");
        if (t == TipoDespacho.Correcao && itens.All(x => SituacaoDe(x.Item) == "ATENDE"))
            b.Add("Nenhum item incluído tem situação diferente de ATENDE: não há exigência para o despacho de correção.");
        return b;
    }

    /// <summary>Aprovação com itens ≠ ATENDE: pede confirmação (a lista vai para o recibo).</summary>
    public static IReadOnlyList<string> Avisos(TipoDespacho t, Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (t != TipoDespacho.Aprovacao) return Array.Empty<string>();
        var pendentes = Itens(p, out _).Where(x => SituacaoDe(x.Item) != "ATENDE")
            .Select(x => "ITEM " + x.Numero.ToString("D2", CultureInfo.InvariantCulture)).ToList();
        if (pendentes.Count == 0) return Array.Empty<string>();
        var quantos = pendentes.Count == 1 ? "1 item" : pendentes.Count.ToString(CultureInfo.InvariantCulture) + " itens";
        return new[] { "O relatório tem " + quantos + " com situação diferente de ATENDE (" + string.Join(", ", pendentes) + "). Emitir a aprovação mesmo assim?" };
    }


    static string Preencher(string? falta) => "[preencher: " + falta + "]";

    /// <summary>O número da exigência no despacho ("01. "), em negrito antes do texto (correções 2026-11-15: como os despachos do kit).</summary>
    public static string Numero(int n) => n.ToString("D2", CultureInfo.InvariantCulture) + ". ";

    /// <summary>As chaves dos trechos editáveis na prévia (correções 2026-11-15).</summary>
    public const string ChaveIntervencao = "aprovacao:intervencao", ChaveArea = "aprovacao:area";

    /// <summary>Os nomes do tipo de obra, na ordem das caixas.</summary>
    public static IReadOnlyList<string> NomesDasIntervencoes => NomesIntervencao;

    /// <summary>Aplica ao rascunho a edição de um trecho da prévia: <c>cabecalho:protocolo|requerente|assunto|data</c>,
    /// <c>grupo:&lt;título atual&gt;</c>, <c>exigencia:&lt;id&gt;[:texto|:fundamento]</c>, <see cref="ChaveArea"/> e <see cref="ChaveIntervencao"/>
    /// (os nomes do tipo de obra separados por vírgula ou " e ", como aparecem no texto). false = chave desconhecida (nada muda).</summary>
    public static bool AplicarEdicao(RascunhoDespacho r, string chave, string texto)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(chave);
        texto ??= "";
        var partes = chave.Split(':', 3);
        switch (partes[0])
        {
            case "cabecalho" when partes.Length == 2 && partes[1] is "protocolo" or "requerente" or "assunto" or "data":
                r.DefinirCabecalho(partes[1], texto);
                return true;
            case "grupo" when partes.Length >= 2:
                r.RenomearGrupo(chave["grupo:".Length..], texto);
                return true;
            case "exigencia" when partes.Length >= 2 && r.Pendencias.Any(p => p.ItemId == partes[1]):
                var campo = partes.Length == 3 ? partes[2] : "texto";
                if (campo == "fundamento") r.DefinirFundamento(partes[1], texto);
                else if (campo == "texto") r.DefinirTexto(partes[1], texto);
                else return false;
                return true;
            case "aprovacao" when chave == ChaveArea:
                r.DefinirArea(texto);
                return true;
            case "aprovacao" when chave == ChaveIntervencao:
                var nomes = texto.Replace(" e ", ",", StringComparison.Ordinal).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < NomesIntervencao.Length; i++)
                    r.DefinirIntervencao((Intervencao)i, nomes.Any(n => string.Equals(n, NomesIntervencao[i], StringComparison.OrdinalIgnoreCase)));
                // O que não é um dos três tipos é o tipo digitado à mão (2026-11-17, "opção manual").
                r.DefinirIntervencaoManual(string.Join(", ", nomes.Where(n => !NomesIntervencao.Contains(n, StringComparer.OrdinalIgnoreCase))));
                return true;
            default:
                return false;
        }
    }

    /// <summary>A prévia: os parágrafos na ordem do DOCX (textos do modelo, valores, grupos, exigências numeradas, textos fixos, data e
    /// assinatura), em trechos como no DOCX, com o índice do parágrafo do modelo cuja formatação vale. O que falta aparece como
    /// "[preencher: …]" com <see cref="ParagrafoPrevia.Faltando"/>. Os trechos variáveis levam a chave para editar na folha.</summary>
    public static IReadOnlyList<ParagrafoPrevia> Previa(InfoModelo m, RascunhoDespacho r, TextosFixos tx)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(tx);
        var lista = new List<ParagrafoPrevia>();
        var antesDoProtocolo = true;
        var depoisDaAssinatura = false;
        var corpoPosto = false;
        var primeiraVaga = m.Paragrafos.ToList().FindIndex(p => p.Papel == PapelParagrafo.Vaga);

        void Rotulo(int i, string rotulo, string valor, string falta, string chave)
        {
            var semValor = valor.Length == 0;
            var mostrado = semValor ? Preencher(falta) : valor;
            lista.Add(new ParagrafoPrevia(rotulo + " " + mostrado, EstiloPrevia.Rotulo, semValor ? falta : null, chave, valor,
                [new TrechoPrevia(rotulo + " "), new TrechoPrevia(mostrado, true, chave, semValor ? falta : null, valor)], i));
        }

        for (var i = 0; i < m.Paragrafos.Count; i++)
        {
            var p = m.Paragrafos[i];
            switch (p.Papel)
            {
                case PapelParagrafo.Protocolo:
                    antesDoProtocolo = false;
                    Rotulo(i, PreenchimentoDocx.RotuloProtocolo, r.Protocolo, FaltaProtocolo, "cabecalho:protocolo");
                    break;
                case PapelParagrafo.Requerente: Rotulo(i, PreenchimentoDocx.RotuloRequerente, r.Requerente, FaltaRequerente, "cabecalho:requerente"); break;
                case PapelParagrafo.Assunto: Rotulo(i, PreenchimentoDocx.RotuloAssunto, r.Assunto, FaltaAssunto, "cabecalho:assunto"); break;
                case PapelParagrafo.Destinatario: lista.Add(new ParagrafoPrevia(p.Texto, EstiloPrevia.Destinatario, null, Modelo: i)); break;
                case PapelParagrafo.Grupo: Exigencias(lista, r, tx, i, primeiraVaga); break;
                case PapelParagrafo.Vaga:
                    // As vagas recebem as exigências e o fechamento (correção, já postos no lugar do grupo) ou o corpo da aprovação (na 1ª vaga).
                    if (r.Tipo == TipoDespacho.Aprovacao && !corpoPosto) CorpoAprovacao(lista, r, tx, i);
                    corpoPosto = true;
                    break;
                case PapelParagrafo.Data:
                    var corte = p.Texto.IndexOf(PreenchimentoDocx.MarcaData, StringComparison.Ordinal);
                    var antes = corte < 0 ? p.Texto : p.Texto[..corte];
                    var depois = corte < 0 ? "" : p.Texto[(corte + PreenchimentoDocx.MarcaData.Length)..];
                    var data = r.DataPorExtenso;
                    var semData = data.Length == 0;
                    var mostrada = semData ? Preencher(FaltaData) : data;
                    lista.Add(new ParagrafoPrevia(antes + mostrada + depois, EstiloPrevia.Data, semData ? FaltaData : null, "cabecalho:data", data,
                        [new TrechoPrevia(antes), new TrechoPrevia(mostrada, false, "cabecalho:data", semData ? FaltaData : null, data), new TrechoPrevia(depois)], i));
                    break;
                case PapelParagrafo.LinhaAssinatura:
                    depoisDaAssinatura = true;
                    lista.Add(new ParagrafoPrevia(p.Texto, EstiloPrevia.Assinatura, null, Modelo: i));
                    break;
                default:
                    var estilo = p.Texto.Trim().Length == 0 ? EstiloPrevia.Vazio
                        : depoisDaAssinatura ? EstiloPrevia.Assinatura
                        : antesDoProtocolo ? EstiloPrevia.Titulo : EstiloPrevia.Corpo;
                    lista.Add(new ParagrafoPrevia(p.Texto, estilo, null, Modelo: i));
                    break;
            }
        }
        return lista;
    }

    static void Exigencias(List<ParagrafoPrevia> lista, RascunhoDespacho r, TextosFixos tx, int iGrupo, int iVaga)
    {
        var marcadas = r.Pendencias.Where(p => p.Incluida).ToList();
        if (marcadas.Count == 0) lista.Add(new ParagrafoPrevia(Preencher(FaltaExigencia), EstiloPrevia.Corpo, FaltaExigencia, Modelo: iVaga));
        var n = 0;
        foreach (var grupo in marcadas.Select(p => p.Grupo).Distinct(StringComparer.Ordinal))
        {
            var doGrupo = marcadas.Where(p => p.Grupo == grupo).ToList();
            var chaveGrupo = "grupo:" + grupo;
            if (grupo.Length == 0)
            {
                var falta = doGrupo[0].Rotulo + ": grupo";
                lista.Add(new ParagrafoPrevia(Preencher(falta), EstiloPrevia.Grupo, falta, chaveGrupo, "",
                    [new TrechoPrevia(Preencher(falta), true, chaveGrupo, falta, "")], iGrupo));
            }
            else lista.Add(new ParagrafoPrevia(grupo, EstiloPrevia.Grupo, null, chaveGrupo, grupo, [new TrechoPrevia(grupo, true, chaveGrupo, null, grupo)], iGrupo));
            foreach (var p in doGrupo)
            {
                var numero = Numero(++n);
                var chave = "exigencia:" + p.ItemId;
                if (p.Texto.Length == 0)
                {
                    var falta = p.Rotulo + ": texto da exigência";
                    lista.Add(new ParagrafoPrevia(numero + Preencher(falta), EstiloPrevia.Corpo, falta, chave, "",
                        [new TrechoPrevia(numero, true), new TrechoPrevia(Preencher(falta), false, chave + ":texto", falta, "")], iVaga));
                    continue;
                }
                var e = Exigencia(p.Texto, p.Fundamento);
                var trechos = new List<TrechoPrevia> { new(numero, true), new(e.Texto, false, chave + ":texto", null, p.Texto) };
                if (e.Fundamento.Length > 0) trechos.Add(new TrechoPrevia(e.Fundamento, false, chave + ":fundamento", null, p.Fundamento));
                lista.Add(new ParagrafoPrevia(numero + e.Texto + e.Fundamento, EstiloPrevia.Corpo, null, chave, p.Texto, trechos, iVaga));
            }
        }
        if (tx.Paragrafos.FirstOrDefault(p => p.Id == "fechamento") is { } f) lista.Add(new ParagrafoPrevia(f.Texto, EstiloPrevia.Corpo, null, Modelo: iVaga));
    }

    static void CorpoAprovacao(List<ParagrafoPrevia> lista, RascunhoDespacho r, TextosFixos tx, int iVaga)
    {
        var intervencao = r.TextoIntervencao;
        var areaOk = AreaValida(r.AreaTotal);
        foreach (var p in tx.Paragrafos)
        {
            string? falta = null;
            var trechos = new List<TrechoPrevia>();
            var resto = p.Texto;
            // Divide o texto fixo nas lacunas; cada lacuna vira um trecho editável (o tipo de obra e a área).
            while (resto.Length > 0)
            {
                var iI = resto.IndexOf(TextosFixos.LacunaIntervencao, StringComparison.Ordinal);
                var iA = resto.IndexOf(TextosFixos.LacunaArea, StringComparison.Ordinal);
                var proxima = iI < 0 ? iA : iA < 0 ? iI : Math.Min(iI, iA);
                if (proxima < 0) { trechos.Add(new TrechoPrevia(resto)); break; }
                if (proxima > 0) trechos.Add(new TrechoPrevia(resto[..proxima]));
                if (proxima == iI)
                {
                    var f = intervencao.Length == 0 ? FaltaIntervencao : null;
                    falta = f ?? falta;
                    trechos.Add(new TrechoPrevia(f is null ? intervencao : Preencher(f), false, ChaveIntervencao, f, intervencao));
                    resto = resto[(proxima + TextosFixos.LacunaIntervencao.Length)..];
                }
                else
                {
                    var f = areaOk ? null : FaltaArea;
                    falta = f ?? falta;
                    trechos.Add(new TrechoPrevia(f is null ? r.AreaTotal : Preencher(f), false, ChaveArea, f, r.AreaTotal));
                    resto = resto[(proxima + TextosFixos.LacunaArea.Length)..];
                }
            }
            lista.Add(new ParagrafoPrevia(string.Concat(trechos.Select(t => t.Texto)), EstiloPrevia.Corpo, falta, Trechos: trechos, Modelo: iVaga));
        }
    }
}
