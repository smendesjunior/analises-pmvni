using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Figuras;

namespace Pmvni.Revisao;

/// <summary>Uma marcação de figura (camada independente sobre a imagem). <see cref="Id"/> é permanente; o número visível é calculado
/// (<see cref="CamadaFiguras.Numeradas"/>). Coordenadas normalizadas 0–1 da imagem base, como o kit. <see cref="Gravada"/>: a marcação já
/// está desenhada na imagem recebida do kit (só a legenda é editável; não se move nem se tira do desenho). Na legenda: <see cref="Elemento"/>
/// (negrito), <see cref="Texto"/> (a solução ou o que foi conferido; no tipo texto, o texto escrito na imagem) e <see cref="Itens"/>
/// ("item 12", guardado com os tokens de citação, impresso com a numeração atual).</summary>
public sealed partial class Marcacao
{
    public string Id { get; set; } = "";
    public string Tipo { get; set; } = TiposMarcacao.Seta;
    public bool Gravada { get; set; }
    public double[]? Alvo { get; set; }
    public double[]? Caixa { get; set; }
    public string Lado { get; set; } = "esquerda";
    public double Altura { get; set; } = .5;
    public double[]? Posicao { get; set; }
    public string Elemento { get; set; } = "";
    public string Texto { get; set; } = "";
    public string Itens { get; set; } = "";

    /// <summary>A nota como veio do kit (marcação gravada): as chaves que o editor não modela saem como vieram.</summary>
    public JsonObject? NotaOriginal { get; set; }

    public bool Numerada => TiposMarcacao.Numerada(Tipo);

    /// <summary>Um id novo, curto e aleatório (<c>mk_</c> + 10 caracteres).</summary>
    public static string NovoId() => "mk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();

    internal Marcacao Clonar() => new()
    {
        Id = Id, Tipo = Tipo, Gravada = Gravada, Alvo = (double[]?)Alvo?.Clone(), Caixa = (double[]?)Caixa?.Clone(), Lado = Lado, Altura = Altura,
        Posicao = (double[]?)Posicao?.Clone(), Elemento = Elemento, Texto = Texto, Itens = Itens, NotaOriginal = (JsonObject?)NotaOriginal?.DeepClone(),
    };

    internal JsonObject ParaJson()
    {
        var o = new JsonObject { ["id"] = Id, ["tipo"] = Tipo };
        if (Gravada) o["gravada"] = true;
        if (Alvo is not null) o["alvo"] = Vetor(Alvo);
        if (Caixa is not null) o["caixa"] = Vetor(Caixa);
        if (Numerada && !Gravada) { o["lado"] = Lado; o["altura"] = Altura; }
        if (Posicao is not null) o["posicao"] = Vetor(Posicao);
        o["elemento"] = Elemento;
        o["texto"] = Texto;
        o["itens"] = Itens;
        if (NotaOriginal is not null) o["nota_original"] = NotaOriginal.DeepClone();
        return o;
    }

    internal static Marcacao DeJson(JsonObject o) => new()
    {
        Id = o["id"]!.GetValue<string>(),
        Tipo = o["tipo"]!.GetValue<string>(),
        Gravada = o["gravada"]?.GetValue<bool>() ?? false,
        Alvo = LerVetor(o["alvo"]),
        Caixa = LerVetor(o["caixa"]),
        Lado = o["lado"]?.GetValue<string>() ?? "esquerda",
        Altura = o["altura"]?.GetValue<double>() ?? .5,
        Posicao = LerVetor(o["posicao"]),
        Elemento = o["elemento"]?.GetValue<string>() ?? "",
        Texto = o["texto"]?.GetValue<string>() ?? "",
        Itens = o["itens"]?.GetValue<string>() ?? "",
        NotaOriginal = (JsonObject?)o["nota_original"]?.DeepClone(),
    };

    internal static JsonArray Vetor(double[] v) => new(v.Select(x => (JsonNode?)JsonValue.Create(Math.Round(x, 5))).ToArray());

    internal static double[]? LerVetor(JsonNode? n) => n is JsonArray a ? a.Select(x => x!.GetValue<double>()).ToArray() : null;
}

/// <summary>Uma figura da seção IMAGENS DO PROJETO na camada de edição. <see cref="Base"/> é o objeto da figura no formato do kit
/// (<c>prancha, titulo, imagem, texto, notas, fonte</c> e o que mais vier, na ordem original); o editor troca só <c>imagem</c>,
/// <c>notas</c>, <c>id</c> e <c>marcacoes</c> ao gerar o dados.json. <see cref="Recorte"/>: a origem de um recorte de prancha (PDF, sha256,
/// página, rotação, retângulo, dpi, tamanho em pixels); o PDF original nunca é alterado.</summary>
public sealed class FiguraRevisao
{
    public string Id { get; set; } = "";
    public int? Origem { get; set; }
    public bool Incluida { get; set; } = true;
    public string Imagem { get; set; } = "";
    public JsonObject Base { get; set; } = new();
    public JsonObject? Recorte { get; set; }
    public List<Marcacao> Marcacoes { get; set; } = new();

    /// <summary>A imagem recebida já traz números e setas desenhados (figura do kit sem o plano de marcações).</summary>
    public bool TemGravadas => Marcacoes.Any(m => m.Gravada);

    public string TextoBase(string campo) => Base[campo] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public Marcacao? Buscar(string id) => Marcacoes.FirstOrDefault(m => m.Id == id);

    internal FiguraRevisao Clonar() => new()
    {
        Id = Id, Origem = Origem, Incluida = Incluida, Imagem = Imagem, Base = (JsonObject)Base.DeepClone(),
        Recorte = (JsonObject?)Recorte?.DeepClone(), Marcacoes = Marcacoes.Select(m => m.Clonar()).ToList(),
    };

    internal JsonObject ParaJson() => new()
    {
        ["id"] = Id,
        ["origem"] = Origem,
        ["incluida"] = Incluida,
        ["imagem"] = Imagem,
        ["base"] = Base.DeepClone(),
        ["recorte"] = Recorte?.DeepClone(),
        ["marcacoes"] = new JsonArray(Marcacoes.Select(m => (JsonNode?)m.ParaJson()).ToArray()),
    };

    internal static FiguraRevisao DeJson(JsonObject o) => new()
    {
        Id = o["id"]!.GetValue<string>(),
        Origem = o["origem"]?.GetValue<int>(),
        Incluida = o["incluida"]?.GetValue<bool>() ?? true,
        Imagem = o["imagem"]!.GetValue<string>(),
        Base = (JsonObject)o["base"]!.DeepClone(),
        Recorte = (JsonObject?)o["recorte"]?.DeepClone(),
        Marcacoes = o["marcacoes"]!.AsArray().Select(x => Marcacao.DeJson(x!.AsObject())).ToList(),
    };
}

/// <summary>A camada de edição das figuras: criação a partir do dados.json recebido, (de)serialização, numeração visível e a figura
/// efetiva (formato do kit) que vai para o dados.json.</summary>
public static partial class CamadaFiguras
{
    /// <summary>Máximo de números por figura (regra do kit; a conferência recusa mais de 6 notas).</summary>
    public const int MaximoNumeradas = 6;

    /// <summary>Campos de texto da figura editáveis pelo comando <see cref="DefinirTextoFigura"/>.</summary>
    public static IReadOnlyList<string> CamposTexto { get; } = new[] { "prancha", "titulo", "texto", "fonte" };

    /// <summary>A camada inicial, a partir das figuras do documento como vieram. Uma figura com a chave <c>marcacoes</c> (o plano de
    /// marcações junto com a imagem sem marcas) importa as marcações como editáveis; sem ela, as notas viram marcações gravadas na imagem.
    /// As citações "item NN" das notas são ligadas aos itens pela numeração ORIGINAL (a do dados.json recebido).</summary>
    public static List<FiguraRevisao> Inicial(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var lista = new List<FiguraRevisao>();
        if (p.Documento["figuras"] is not JsonArray figuras) return lista;
        var ids = IdsPorNumeroOriginal(p);
        var usados = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < figuras.Count; i++)
        {
            if (figuras[i] is not JsonObject f) continue;
            var id = f["id"] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 && !usados.Contains(s)
                ? s
                : "figura-" + (i + 1).ToString(CultureInfo.InvariantCulture);
            usados.Add(id);
            var fig = new FiguraRevisao
            {
                Id = id,
                Origem = i,
                Imagem = f["imagem"] is JsonValue im && im.TryGetValue<string>(out var img) ? img : "",
                Base = (JsonObject)f.DeepClone(),
            };
            var notas = f["notas"] as JsonArray ?? new JsonArray();
            var vetoriais = MarcasDoDados(f["marcacoes"]);
            var k = 0;
            foreach (var n in notas)
            {
                if (n is not JsonObject nota) continue;
                var m = vetoriais.Count > k ? vetoriais[k] : new Marcacao { Gravada = true, Tipo = TiposMarcacao.Seta };
                k++;
                m.Id = "mk_" + id + "_" + k.ToString(CultureInfo.InvariantCulture);
                m.Elemento = TextoDe(nota["elemento"]);
                m.Texto = TextoDe(nota["texto"]);
                m.Itens = ConverterItens(TextoDe(nota["itens"]), ids);
                m.NotaOriginal = (JsonObject)nota.DeepClone();
                fig.Marcacoes.Add(m);
            }
            // Marcações vetoriais sem nota (texto livre) entram também.
            foreach (var resto in vetoriais.Skip(k).Where(x => !x.Numerada))
            {
                resto.Id = "mk_" + id + "_t" + (++k).ToString(CultureInfo.InvariantCulture);
                fig.Marcacoes.Add(resto);
            }
            lista.Add(fig);
        }
        return lista;
    }

    /// <summary>As marcações numeradas na ordem do número visível: primeiro as gravadas na imagem (na ordem das notas), depois as
    /// editáveis como o kit numera (margem esquerda de cima para baixo, depois a direita). Mover um número reordena sozinho.</summary>
    public static IReadOnlyList<(Marcacao Marcacao, int Numero)> Numeradas(FiguraRevisao f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var gravadas = f.Marcacoes.Where(m => m.Gravada).ToList();
        var vetoriais = f.Marcacoes.Select((m, i) => (m, i)).Where(x => !x.m.Gravada && x.m.Numerada)
            .OrderBy(x => x.m.Lado == "direita" ? 1 : 0).ThenBy(x => x.m.Altura).ThenBy(x => x.i).Select(x => x.m);
        return gravadas.Concat(vetoriais).Select((m, i) => (m, i + 1)).ToList();
    }

    /// <summary>Quantos números ainda cabem na figura (região e seta ocupam um; o texto livre não). Zero: o editor avisa antes de
    /// tentar, em vez de deixar o comando ser recusado.</summary>
    public static int VagasNumeradas(FiguraRevisao f) => Math.Max(0, MaximoNumeradas - Numeradas(f).Count);

    /// <summary>O número visível de uma marcação (null no texto livre ou se não existe).</summary>
    public static int? Numero(FiguraRevisao f, string marcacao) =>
        Numeradas(f).Where(x => x.Marcacao.Id == marcacao).Select(x => (int?)x.Numero).FirstOrDefault();

    /// <summary>As marcações a desenhar por cima da imagem (as editáveis), já com o número visível.</summary>
    public static IReadOnlyList<MarcaDesenho> Desenho(FiguraRevisao f)
    {
        var numeros = Numeradas(f).ToDictionary(x => x.Marcacao.Id, x => x.Numero, StringComparer.Ordinal);
        return f.Marcacoes.Where(m => !m.Gravada)
            .Select(m => new MarcaDesenho(m.Tipo, numeros.TryGetValue(m.Id, out var n) ? n : null, m.Alvo, m.Caixa, m.Lado, m.Altura, m.Posicao,
                m.Tipo == TiposMarcacao.Texto ? m.Texto : null, m.Id))
            .ToList();
    }

    public static JsonArray Escrever(IReadOnlyList<FiguraRevisao> figuras) =>
        new(figuras.Select(f => (JsonNode?)f.ParaJson()).ToArray());

    public static List<FiguraRevisao> Ler(JsonNode? n) =>
        n is JsonArray a ? a.Select(x => FiguraRevisao.DeJson(x!.AsObject())).ToList() : throw new FormatException("Camada de figuras inválida.");

    /// <summary>As figuras incluídas no formato do kit (o que vai em <c>figuras</c> do dados.json efetivo).</summary>
    public static JsonArray Efetivas(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var lista = new JsonArray();
        foreach (var f in p.Figuras ?? new List<FiguraRevisao>())
            if (f.Incluida) lista.Add(Efetiva(p, f));
        return lista;
    }

    static JsonObject Efetiva(Projeto p, FiguraRevisao f)
    {
        var o = (JsonObject)f.Base.DeepClone();
        if (TextoDe(o["imagem"]) != f.Imagem) o["imagem"] = f.Imagem;
        var notas = new JsonArray();
        foreach (var (m, _) in Numeradas(f))
        {
            var nota = (JsonObject?)m.NotaOriginal?.DeepClone() ?? new JsonObject();
            Definir(nota, "elemento", m.Elemento);
            Definir(nota, "texto", m.Texto);
            var referencia = DadosEfetivos.Imprimir(p, m.Itens);
            // O modelo imprime a origem entre parênteses mesmo quando o analista não vinculou a marca a um item.
            if (referencia.Length == 0 && !m.Gravada && m.NotaOriginal is null)
                referencia = "Marcação manual";
            Definir(nota, "itens", referencia);
            notas.Add(nota);
        }
        if (!(o["notas"] is JsonArray atuais && JsonNode.DeepEquals(atuais, notas))) o["notas"] = notas;
        if (!(o["id"] is JsonValue iv && iv.TryGetValue<string>(out var id) && id == f.Id)) o["id"] = f.Id;
        var desenho = Desenho(f);
        if (desenho.Count == 0) o.Remove("marcacoes");
        else
        {
            o["marcacoes"] = new JsonArray(desenho.Select(d =>
            {
                var x = new JsonObject { ["tipo"] = d.Tipo };
                if (d.Numero is { } n) x["numero"] = n;
                if (d.Alvo is not null) x["alvo"] = Marcacao.Vetor(d.Alvo);
                if (d.Caixa is not null) x["caixa"] = Marcacao.Vetor(d.Caixa);
                if (TiposMarcacao.Numerada(d.Tipo)) { x["lado"] = d.Lado; x["altura"] = Math.Round(d.Altura, 5); }
                if (d.Posicao is not null) x["posicao"] = Marcacao.Vetor(d.Posicao);
                if (d.Texto is not null) x["texto"] = d.Texto;
                return (JsonNode?)x;
            }).ToArray());
        }
        return o;
    }

    /// <summary>Escreve o texto na chave só se ele muda o que está lá (uma nota sem edição sai como veio, byte a byte).</summary>
    static void Definir(JsonObject nota, string chave, string valor)
    {
        if (nota[chave] is JsonValue v && v.TryGetValue<string>(out var s) && s == valor) return;
        if (!nota.ContainsKey(chave) && valor.Length == 0) return;
        if (nota[chave] is null && nota.ContainsKey(chave) && valor.Length == 0) return;
        nota[chave] = valor;
    }

    /// <summary>Sem camada de edição: as notas das figuras do documento como vieram, com o "item NN" das notas renumerado (a numeração
    /// original ligada ao item, impressa com a de agora). Sem exclusões, o texto não muda.</summary>
    internal static void RenumerarNotas(Projeto p, JsonObject documento)
    {
        if (documento["figuras"] is not JsonArray figuras) return;
        var ids = IdsPorNumeroOriginal(p);
        foreach (var f in figuras)
        {
            if (f is not JsonObject fo || fo["notas"] is not JsonArray notas) continue;
            foreach (var n in notas)
            {
                if (n is not JsonObject nota || nota["itens"] is not JsonValue v || !v.TryGetValue<string>(out var itens)) continue;
                var impresso = DadosEfetivos.Imprimir(p, ConverterItens(itens, ids));
                if (impresso != itens) nota["itens"] = impresso;
            }
        }
    }

    // Só os itens recebidos: um item criado na revisão não é citado por nenhuma nota do original.
    static IReadOnlyList<string> IdsPorNumeroOriginal(Projeto p) => p.Itens.Where(i => !i.Criado).OrderBy(i => i.NumeroOriginal).Select(i => i.Id).ToList();

    [GeneratedRegex(@"^\s*(?:item|itens)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ComecaPorItem();

    [GeneratedRegex(@"(?<![\p{L}\p{N}{:])([0-9]{1,3})(?![\p{L}\p{N}}:])(?![.,][0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex NumeroSolto();

    /// <summary>"item 12" / "itens 03 e 12" (o campo de itens de uma nota) → texto com os tokens de citação. Começando por item/itens, todo
    /// número de 1 a 3 dígitos é número de item (o campo só cita itens); fora disso, a regra comum das citações (<see cref="Citacoes"/>).
    /// Número fora de 1..<paramref name="ids"/>.Count fica literal.</summary>
    public static string ConverterItens(string texto, IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(texto);
        if (!ComecaPorItem().IsMatch(texto)) return Citacoes.Converter(texto, ids).Texto;
        return NumeroSolto().Replace(texto, m =>
        {
            var n = int.Parse(m.Value, CultureInfo.InvariantCulture);
            return n < 1 || n > ids.Count ? m.Value : Token(ids[n - 1], m.Value.Length);
        });
    }

    static string Token(string id, int largura) => "{{ref:" + id + ":" + largura.ToString(CultureInfo.InvariantCulture) + "}}";

    /// <summary>O campo de itens de uma nota ligado aos itens dados (na ordem da numeração atual): "item 03", "itens 03 e 12", "itens 01, 03 e 12".</summary>
    public static string TextoDosItens(Projeto p, IEnumerable<string> itens)
    {
        var lista = itens.Distinct(StringComparer.Ordinal)
            .OrderBy(id => p.NumeroAtual(id) ?? p.Item(id)?.NumeroOriginal ?? int.MaxValue).Select(id => Token(id, 2)).ToList();
        if (lista.Count == 0) return "";
        if (lista.Count == 1) return "item " + lista[0];
        return "itens " + string.Join(", ", lista.Take(lista.Count - 1)) + " e " + lista[^1];
    }

    /// <summary>O campo de itens da marcação como sai na legenda ("item 03"), com a numeração atual.</summary>
    public static string ItensImpressos(Projeto p, Marcacao m) => DadosEfetivos.Imprimir(p, m.Itens);

    /// <summary>Os ids dos itens citados no campo de itens (tokens).</summary>
    public static IReadOnlyList<string> ItensCitados(string itens) => Citacoes.Alvos(itens ?? "");

    static string TextoDe(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    static List<Marcacao> MarcasDoDados(JsonNode? n)
    {
        var lista = new List<Marcacao>();
        if (n is not JsonArray a) return lista;
        foreach (var x in a.OfType<JsonObject>().OrderBy(o => o["numero"]?.GetValue<int>() ?? int.MaxValue))
        {
            var tipo = TextoDe(x["tipo"]);
            if (!TiposMarcacao.Valido(tipo)) continue;
            lista.Add(new Marcacao
            {
                Tipo = tipo,
                Alvo = Marcacao.LerVetor(x["alvo"]),
                Caixa = Marcacao.LerVetor(x["caixa"]),
                Lado = TextoDe(x["lado"]) == "direita" ? "direita" : "esquerda",
                Altura = x["altura"] is JsonValue h && h.TryGetValue<double>(out var d) ? d : .5,
                Posicao = Marcacao.LerVetor(x["posicao"]),
                Texto = tipo == TiposMarcacao.Texto ? TextoDe(x["texto"]) : "",
            });
        }
        // As numeradas primeiro (casam com as notas, na ordem), as de texto livre depois.
        return lista.Where(m => m.Numerada).Concat(lista.Where(m => !m.Numerada)).ToList();
    }
}
