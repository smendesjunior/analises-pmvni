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
public sealed record ParagrafoPrevia(string Texto, EstiloPrevia Estilo, string? Faltando);

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
            default: throw new ArgumentException("Campo de cabeçalho desconhecido: " + campo, nameof(campo));
        }
    }

    PendenciaDespacho Pendencia(string itemId) =>
        _pendencias.FirstOrDefault(p => p.ItemId == itemId) ?? throw new ArgumentException("Exigência desconhecida: " + itemId, nameof(itemId));

    public void DefinirTexto(string itemId, string texto) => Pendencia(itemId).Texto = TextoEditado.Normalizar(texto);

    public void DefinirFundamento(string itemId, string texto) => Pendencia(itemId).Fundamento = TextoEditado.Normalizar(texto);

    public void DefinirGrupo(string itemId, string texto) => Pendencia(itemId).Grupo = TextoEditado.Normalizar(texto);

    public void Marcar(string itemId, bool incluida) => Pendencia(itemId).Incluida = incluida;

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

    public void DefinirArea(string texto) => AreaTotal = TextoEditado.Normalizar(texto);

    /// <summary>O que o usuário ainda precisa preencher, na ordem da tela; vazio = pode gerar.</summary>
    public IReadOnlyList<string> Faltando()
    {
        var f = new List<string>();
        if (Protocolo.Length == 0) f.Add(ConteudoDespacho.FaltaProtocolo);
        if (Requerente.Length == 0) f.Add(ConteudoDespacho.FaltaRequerente);
        if (Assunto.Length == 0) f.Add(ConteudoDespacho.FaltaAssunto);
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
            if (_intervencoes.Count == 0) f.Add(ConteudoDespacho.FaltaIntervencao);
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
            corpo.AddRange(tx.Paragrafos.Select(p => ConteudoDespacho.Resolver(p.Texto, ConteudoDespacho.Intervencoes(_intervencoes), AreaTotal)));
        return new DespachoFinal(Tipo, Protocolo, Requerente, Assunto, Etapas.DataPorExtenso(Data), grupos, corpo);
    }
}

/// <summary>O conteúdo do despacho derivado do relatório revisado (desenho §10.3): determinístico, sem IA, sem nada inventado. O que falta vira
/// campo que o usuário preenche.</summary>
public static class ConteudoDespacho
{
    internal const string FaltaProtocolo = "Protocolo nº", FaltaRequerente = "Requerente", FaltaAssunto = "Assunto",
        FaltaExigencia = "Ao menos uma exigência marcada",
        FaltaIntervencao = "Tipo de obra (obra nova, alteração de obra ou ampliação de obra)",
        FaltaArea = "Área total a construir (m², ex.: 210,52)";

    /// <summary>A regra <c>numero_ano</c> do kit para o processo da capa.</summary>
    static readonly Regex NumeroAno = new(@"^\s*0*(\d{1,7})\s*(?:[/\-]|\s)\s*((?:19|20)\d{2})\s*$", RegexOptions.CultureInvariant);
    static readonly Regex Area = new(@"^(\d{1,3}(\.\d{3})+|\d+),\d{2}$", RegexOptions.CultureInvariant);

    static readonly string[] NomesIntervencao = { "obra nova", "alteração de obra", "ampliação de obra" };

    public static string Intervencoes(IEnumerable<Intervencao> i)
    {
        ArgumentNullException.ThrowIfNull(i);
        var nomes = i.Distinct().Order().Select(x => NomesIntervencao[(int)x]).ToList();
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
                var texto = Texto(item["providencia"]) ?? "";
                var fundamento = Texto(item["base_legal"]) ?? "";
                pendencias.Add(new PendenciaDespacho
                {
                    ItemId = id, Numero = numero, Situacao = situacao, Titulo = Texto(item["titulo"]) ?? "",
                    Grupo = m.TituloGrupo, GrupoOriginal = m.TituloGrupo,
                    Texto = texto, TextoOriginal = texto, Fundamento = fundamento, FundamentoOriginal = fundamento,
                });
            }
        return new RascunhoDespacho(t, Texto(capa?["processo"]) ?? "", Texto(capa?["requerente"]) ?? "", m.Assunto, etapa.Data, pendencias);
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
        if (lido is null)
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

    /// <summary>A prévia de texto: os parágrafos na ordem do DOCX (textos do modelo, valores, grupos, exigências, textos fixos, data e assinatura).
    /// O que falta aparece como "[preencher: …]" com <see cref="ParagrafoPrevia.Faltando"/>.</summary>
    public static IReadOnlyList<ParagrafoPrevia> Previa(InfoModelo m, RascunhoDespacho r, TextosFixos tx)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(tx);
        var lista = new List<ParagrafoPrevia>();
        var antesDoProtocolo = true;
        var depoisDaAssinatura = false;
        var corpoPosto = false;

        void Rotulo(string rotulo, string valor, string falta)
        {
            var semValor = valor.Length == 0;
            lista.Add(new ParagrafoPrevia(rotulo + " " + (semValor ? Preencher(falta) : valor), EstiloPrevia.Rotulo, semValor ? falta : null));
        }

        foreach (var p in m.Paragrafos)
        {
            switch (p.Papel)
            {
                case PapelParagrafo.Protocolo:
                    antesDoProtocolo = false;
                    Rotulo(PreenchimentoDocx.RotuloProtocolo, r.Protocolo, FaltaProtocolo);
                    break;
                case PapelParagrafo.Requerente: Rotulo(PreenchimentoDocx.RotuloRequerente, r.Requerente, FaltaRequerente); break;
                case PapelParagrafo.Assunto: Rotulo(PreenchimentoDocx.RotuloAssunto, r.Assunto, FaltaAssunto); break;
                case PapelParagrafo.Destinatario: lista.Add(new ParagrafoPrevia(p.Texto, EstiloPrevia.Destinatario, null)); break;
                case PapelParagrafo.Grupo: Exigencias(lista, r, tx); break;
                case PapelParagrafo.Vaga:
                    // As vagas recebem as exigências e o fechamento (correção, já postos no lugar do grupo) ou o corpo da aprovação (na 1ª vaga).
                    if (r.Tipo == TipoDespacho.Aprovacao && !corpoPosto) CorpoAprovacao(lista, r, tx);
                    corpoPosto = true;
                    break;
                case PapelParagrafo.Data:
                    lista.Add(new ParagrafoPrevia(p.Texto.Replace(PreenchimentoDocx.MarcaData, Etapas.DataPorExtenso(r.Data), StringComparison.Ordinal),
                        EstiloPrevia.Data, null));
                    break;
                case PapelParagrafo.LinhaAssinatura:
                    depoisDaAssinatura = true;
                    lista.Add(new ParagrafoPrevia(p.Texto, EstiloPrevia.Assinatura, null));
                    break;
                default:
                    var estilo = p.Texto.Trim().Length == 0 ? EstiloPrevia.Vazio
                        : depoisDaAssinatura ? EstiloPrevia.Assinatura
                        : antesDoProtocolo ? EstiloPrevia.Titulo : EstiloPrevia.Corpo;
                    lista.Add(new ParagrafoPrevia(p.Texto, estilo, null));
                    break;
            }
        }
        return lista;
    }

    static void Exigencias(List<ParagrafoPrevia> lista, RascunhoDespacho r, TextosFixos tx)
    {
        var marcadas = r.Pendencias.Where(p => p.Incluida).ToList();
        if (marcadas.Count == 0) lista.Add(new ParagrafoPrevia(Preencher(FaltaExigencia), EstiloPrevia.Corpo, FaltaExigencia));
        foreach (var grupo in marcadas.Select(p => p.Grupo).Distinct(StringComparer.Ordinal))
        {
            var doGrupo = marcadas.Where(p => p.Grupo == grupo).ToList();
            if (grupo.Length == 0)
            {
                var falta = doGrupo[0].Rotulo + ": grupo";
                lista.Add(new ParagrafoPrevia(Preencher(falta), EstiloPrevia.Grupo, falta));
            }
            else lista.Add(new ParagrafoPrevia(grupo, EstiloPrevia.Grupo, null));
            foreach (var p in doGrupo)
            {
                if (p.Texto.Length == 0)
                {
                    var falta = p.Rotulo + ": texto da exigência";
                    lista.Add(new ParagrafoPrevia(Preencher(falta), EstiloPrevia.Corpo, falta));
                    continue;
                }
                var e = Exigencia(p.Texto, p.Fundamento);
                lista.Add(new ParagrafoPrevia(e.Texto + e.Fundamento, EstiloPrevia.Corpo, null));
            }
        }
        if (tx.Paragrafos.FirstOrDefault(p => p.Id == "fechamento") is { } f) lista.Add(new ParagrafoPrevia(f.Texto, EstiloPrevia.Corpo, null));
    }

    static void CorpoAprovacao(List<ParagrafoPrevia> lista, RascunhoDespacho r, TextosFixos tx)
    {
        var intervencao = Intervencoes(r.Intervencoes);
        var areaOk = AreaValida(r.AreaTotal);
        foreach (var p in tx.Paragrafos)
        {
            string? falta = null;
            var texto = p.Texto;
            if (texto.Contains(TextosFixos.LacunaIntervencao, StringComparison.Ordinal) && intervencao.Length == 0)
            {
                falta = FaltaIntervencao;
                texto = texto.Replace(TextosFixos.LacunaIntervencao, Preencher(falta), StringComparison.Ordinal);
            }
            if (texto.Contains(TextosFixos.LacunaArea, StringComparison.Ordinal) && !areaOk)
            {
                falta = FaltaArea;
                texto = texto.Replace(TextosFixos.LacunaArea, Preencher(falta), StringComparison.Ordinal);
            }
            lista.Add(new ParagrafoPrevia(Resolver(texto, intervencao, r.AreaTotal), EstiloPrevia.Corpo, falta));
        }
    }
}
