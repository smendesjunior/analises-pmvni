using System.Globalization;
using System.Text.Json.Nodes;
using Pmvni.Composicao.Kit;

namespace Pmvni.Revisao;

/// <summary>O que o usuário revisou numa linha de tabela (ÍNDICES URBANÍSTICOS ou ILUMINAÇÃO E VENTILAÇÃO). O original fica no
/// <see cref="Projeto.Documento"/>, como veio; aqui só a revisão: <see cref="Situacao"/> revisada (null = sem revisão), a
/// <see cref="Justificativa"/> digitada e os textos revisados por caminho (<c>nome</c>, <c>detalhes/0</c>, <c>vaos/1/tipo</c>…).</summary>
public sealed class LinhaRevisao
{
    public string? Situacao { get; set; }
    public string? Justificativa { get; set; }
    public SortedDictionary<string, string> Campos { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Nada revisado nem digitado: a linha sai do projeto.</summary>
    public bool Vazia => Situacao is null && Justificativa is null && Campos.Count == 0;

    /// <summary>Situação ou algum texto revisado (a justificativa sozinha não conta).</summary>
    public bool Revisada => Situacao is not null || Campos.Count > 0;

    internal LinhaRevisao Clonar() => new()
    {
        Situacao = Situacao,
        Justificativa = Justificativa,
        Campos = new SortedDictionary<string, string>(Campos, StringComparer.Ordinal),
    };
}

/// <summary>As linhas das tabelas do relatório que se editam como os itens. Id estável de cada linha: o bloco e a posição no original
/// recebido (<c>indices/02</c>, <c>ambientes/03</c>). O original nunca muda e as linhas não se excluem nem se reordenam, então a posição
/// é estável como o id de um item; o mesmo número sai impresso na linha ("ÍNDICE 02", "AMBIENTE 03"). Um texto se endereça pelo caminho
/// dentro da linha: chave, e índice nas listas (<c>vaos/1/tipo</c>).</summary>
public static class TabelasRevisao
{
    public const string Indices = "indices";
    public const string Ambientes = "ambientes";
    /// <summary>Correção 2026-11-16: o DIAGRAMA DOS ÍNDICES também se revisa (as chamadas são linhas; a planta e a volumetria, uma
    /// linha cada), para acompanhar os valores novos dos índices.</summary>
    public const string Chamadas = "chamadas";
    public const string Planta = "planta";
    public const string Volumetria = "volumetria";

    public static IReadOnlyList<string> Blocos { get; } = new[] { Indices, Ambientes, Chamadas, Planta, Volumetria };

    /// <summary>Formas da volumetria que o desenho entende; entram na linha "volumetria/01" mesmo que o original não as tenha
    /// (o original do kit desenha 2 pavimentos com telhado de duas águas e nenhum destaque).</summary>
    public static IReadOnlyDictionary<string, string> FormaPadraoVolumetria { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["pavimentos"] = "2", ["telhado"] = "duas águas", ["destaque"] = "nenhum",
    };

    /// <summary>Chaves da linha que não são texto editável (o ícone e a situação, que tem comando próprio).</summary>
    private static readonly HashSet<string> NaoEditaveis = new(StringComparer.Ordinal) { "icone", "situacao", "ancora" };

    /// <summary>Campo de texto que a tecla F2 edita quando nenhum foi clicado.</summary>
    public static string CampoPadrao(string id) => Bloco(id) == Ambientes ? "nome" : "calculo";

    // ---------- ids ----------

    public static string Id(string bloco, int posicao) =>
        bloco + "/" + posicao.ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>O id tem a forma de linha de tabela (não diz se ela existe; ver <see cref="Existe"/>).</summary>
    public static bool EhLinha(string? id) => id is not null && TentarLer(id, out _, out _);

    public static bool TentarLer(string id, out string bloco, out int posicao)
    {
        bloco = "";
        posicao = 0;
        var barra = id.IndexOf('/', StringComparison.Ordinal);
        if (barra <= 0) return false;
        var b = id[..barra];
        if (!Blocos.Contains(b)) return false;
        var n = id[(barra + 1)..];
        if (n.Length < 2 || !n.All(char.IsAsciiDigit) || !int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p < 1) return false;
        if (Id(b, p) != id) return false; // uma só grafia por linha
        bloco = b;
        posicao = p;
        return true;
    }

    private static string? Bloco(string id) => TentarLer(id, out var b, out _) ? b : null;

    /// <summary>A lista de linhas do bloco no documento (null se o relatório não tem a tabela).</summary>
    internal static JsonArray? Lista(JsonObject documento, string bloco) => bloco switch
    {
        Indices => (documento["indices"] as JsonObject)?["linhas"] as JsonArray,
        Ambientes => (documento["iluminacao"] as JsonObject)?["ambientes"] as JsonArray,
        Chamadas => (documento["diagrama"] as JsonObject)?["chamadas"] as JsonArray,
        _ => null,
    };

    /// <summary>Planta e volumetria: um objeto só (posição 01).</summary>
    static JsonObject? Unico(JsonObject documento, string bloco) =>
        bloco is Planta or Volumetria ? (documento["diagrama"] as JsonObject)?[bloco] as JsonObject : null;

    /// <summary>A volumetria com as chaves de forma que faltam (cópia; o documento não muda).</summary>
    static JsonObject ComForma(JsonObject v)
    {
        var c = (JsonObject)v.DeepClone();
        foreach (var (k, valor) in FormaPadraoVolumetria) if (!c.ContainsKey(k)) c[k] = valor;
        return c;
    }

    /// <summary>A linha <paramref name="id"/> em <paramref name="documento"/>; null se não existe.</summary>
    internal static JsonObject? Linha(JsonObject documento, string id)
    {
        if (!TentarLer(id, out var bloco, out var posicao)) return null;
        if (bloco is Planta or Volumetria) return posicao == 1 ? Unico(documento, bloco) : null;
        var lista = Lista(documento, bloco);
        return lista is not null && posicao <= lista.Count ? lista[posicao - 1] as JsonObject : null;
    }

    /// <summary>A linha como veio no original recebido.</summary>
    public static JsonObject? Original(Projeto p, string id)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(id);
        var l = Linha(p.Documento, id);
        return l is not null && id == Id(Volumetria, 1) ? ComForma(l) : l;
    }

    public static bool Existe(Projeto p, string? id) => id is not null && Original(p, id) is not null;

    /// <summary>Todas as linhas editáveis do relatório, na ordem em que saem (índices, depois ambientes).</summary>
    public static IReadOnlyList<string> Ids(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var ids = new List<string>();
        foreach (var bloco in Blocos)
            if (Unico(p.Documento, bloco) is not null) ids.Add(Id(bloco, 1));
            else if (Lista(p.Documento, bloco) is { } lista)
                for (var i = 0; i < lista.Count; i++)
                    if (lista[i] is JsonObject) ids.Add(Id(bloco, i + 1));
        return ids;
    }

    // ---------- textos ----------

    /// <summary>Os caminhos dos textos editáveis da linha original, na ordem em que aparecem no dados.json: todo texto não vazio, menos o
    /// ícone, a situação e as chaves de instrução (<c>_…</c>). Números, verdadeiro/falso e listas vazias ficam de fora.</summary>
    public static IReadOnlyList<string> Caminhos(JsonObject linha)
    {
        ArgumentNullException.ThrowIfNull(linha);
        var lista = new List<string>();
        foreach (var (chave, valor) in linha)
        {
            if (NaoEditaveis.Contains(chave) || chave.StartsWith('_')) continue;
            Juntar(valor, chave, lista);
        }
        return lista;
    }

    private static void Juntar(JsonNode? no, string caminho, List<string> lista)
    {
        switch (no)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                if (s.Trim().Length > 0) lista.Add(caminho);
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) Juntar(a[i], caminho + "/" + i.ToString(CultureInfo.InvariantCulture), lista);
                break;
            case JsonObject o:
                foreach (var (chave, valor) in o)
                    if (!chave.StartsWith('_')) Juntar(valor, caminho + "/" + chave, lista);
                break;
        }
    }

    /// <summary>O nó no caminho (chaves e índices separados por "/"); null se não existe.</summary>
    internal static JsonNode? No(JsonNode? raiz, string caminho)
    {
        var no = raiz;
        foreach (var parte in caminho.Split('/'))
        {
            no = no switch
            {
                JsonObject o => o[parte],
                JsonArray a when int.TryParse(parte, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < a.Count => a[i],
                _ => null,
            };
            if (no is null) return null;
        }
        return no;
    }

    /// <summary>Troca o texto no caminho (que já existe e é texto); false se não existe.</summary>
    internal static bool Trocar(JsonObject linha, string caminho, string texto)
    {
        var barra = caminho.LastIndexOf('/');
        var pai = barra < 0 ? linha : No(linha, caminho[..barra]);
        var ultima = barra < 0 ? caminho : caminho[(barra + 1)..];
        switch (pai)
        {
            case JsonObject o when o[ultima] is JsonValue:
                o[ultima] = texto;
                return true;
            case JsonArray a when int.TryParse(ultima, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < a.Count && a[i] is JsonValue:
                a[i] = texto;
                return true;
            default:
                return false;
        }
    }

    public static string? TextoOriginal(Projeto p, string id, string caminho) =>
        Original(p, id) is { } l && No(l, caminho) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>O texto como sai no relatório: o revisado, senão o original; null se o caminho não é texto da linha.</summary>
    public static string? TextoEfetivo(Projeto p, string id, string caminho)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Linhas.TryGetValue(id, out var r) && r.Campos.TryGetValue(caminho, out var revisado)) return revisado;
        return TextoOriginal(p, id, caminho);
    }

    public static bool Editavel(Projeto p, string id, string caminho) =>
        Original(p, id) is { } l && Caminhos(l).Contains(caminho);

    // ---------- situação ----------

    public static string? SituacaoOriginal(Projeto p, string id) =>
        Original(p, id)?["situacao"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static string? SituacaoEfetiva(Projeto p, string id) =>
        p.Linhas.TryGetValue(id, out var r) && r.Situacao is not null ? r.Situacao : SituacaoOriginal(p, id);

    public static string? Justificativa(Projeto p, string id) => p.Linhas.TryGetValue(id, out var r) ? r.Justificativa : null;

    public static bool Revisada(Projeto p, string id) => p.Linhas.TryGetValue(id, out var r) && r.Revisada;

    /// <summary>Situação mudada sem justificativa (erro da conferência final, como nos itens).</summary>
    public static bool FaltaJustificativa(LinhaRevisao r) => r.Situacao is not null && string.IsNullOrWhiteSpace(r.Justificativa);

    /// <summary>As situações das linhas são as do kit para tabelas (<see cref="Situacoes.SitIndice"/>), impressas com os mesmos textos
    /// das situações de item: ATENDE, NÃO ATENDE, FALTA COMPROVAR, INFORMAÇÕES DIVERGENTES. Converte a de item (a da faixa) na de linha.</summary>
    public static string? SituacaoDeLinha(string codigoDeItem) => codigoDeItem switch
    {
        "ATENDE" or "CONTA_CONFERE" => "CONTA_CONFERE",
        "NAO_ATENDE" => "NAO_ATENDE",
        "NAO_COMPROVADO" or "PENDENTE" => "PENDENTE",
        "DIVERGENCIA_INTERNA" or "DIVERGENCIA" => "DIVERGENCIA",
        _ => null,
    };

    /// <summary>O inverso de <see cref="SituacaoDeLinha"/>: a situação de item com o mesmo texto impresso.</summary>
    public static string? SituacaoDeItem(string? codigoDeLinha) => codigoDeLinha switch
    {
        "CONTA_CONFERE" => "ATENDE",
        "NAO_ATENDE" => "NAO_ATENDE",
        "PENDENTE" => "NAO_COMPROVADO",
        "DIVERGENCIA" => "DIVERGENCIA_INTERNA",
        _ => null,
    };

    // ---------- nomes para a tela ----------

    /// <summary><c>ÍNDICE 02</c> ou <c>AMBIENTE 03</c> (como a tabela imprime).</summary>
    public static string Rotulo(string id) =>
        TentarLer(id, out var bloco, out var n) ? bloco switch
        {
            Indices => "ÍNDICE " + n.ToString("D2", CultureInfo.InvariantCulture),
            Ambientes => "AMBIENTE " + n.ToString("D2", CultureInfo.InvariantCulture),
            Chamadas => "DIAGRAMA · CHAMADA " + n.ToString("D2", CultureInfo.InvariantCulture),
            Planta => "DIAGRAMA · PLANTA",
            _ => "DIAGRAMA · VOLUMETRIA",
        } : id;

    /// <summary><c>Índice 02 (Taxa de ocupação (TO))</c>: o rótulo com o nome atual da linha, para listas e mensagens.</summary>
    public static string Descricao(Projeto p, string id)
    {
        var nome = TextoEfetivo(p, id, "nome");
        var r = Rotulo(id);
        r = r[..1] + r[1..].ToLower(CultureInfo.GetCultureInfo("pt-BR"));
        return string.IsNullOrWhiteSpace(nome) ? r : r + " (" + nome + ")";
    }

    /// <summary>O nome de um texto da linha, como a tela mostra: "Cálculo / descrição", "Vão 2 — medidas", "Detalhe 1"…</summary>
    public static string NomeDoCampo(string caminho)
    {
        var p = caminho.Split('/');
        string N(int i) => (int.Parse(p[i], CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        bool Indice(int i) => p.Length > i && p[i].All(char.IsAsciiDigit) && p[i].Length > 0;
        return p[0] switch
        {
            "nome" when p.Length == 1 => "Nome",
            "descricao" when p.Length == 1 => "Descrição",
            "calculo" when p.Length == 1 => "Cálculo / descrição",
            "projeto" when p.Length == 1 => "No projeto",
            "limite" when p.Length == 1 => "Limite",
            "texto" when p.Length == 1 => "Texto da situação",
            "situacao_titulo" when p.Length == 1 => "Título da situação",
            "detalhes" when p.Length == 2 && Indice(1) => "Detalhe " + N(1),
            "textos" when p.Length == 2 && Indice(1) => "Texto da situação " + N(1),
            "minimo" when p.Length == 3 && Indice(1) && p[2] == "texto" => "Mínimo exigido " + N(1),
            "vaos" when p.Length == 3 && Indice(1) => "Vão " + N(1) + " — " + p[2] switch
            {
                "codigo" => "código",
                "medidas" => "medidas",
                "tipo" => "tipo",
                "area" => "área",
                "sem_area" => "aviso sem área",
                _ => p[2],
            },
            "resultado" when p.Length == 2 => p[1] switch
            {
                "total" => "Vão total",
                "conta" => "Conta do vão total",
                "comparacao" => "Comparação do vão total",
                _ => "Vão total — " + p[1],
            },
            _ => caminho,
        };
    }

    // ---------- dados.json efetivo ----------

    /// <summary>Põe as revisões das linhas em <paramref name="documento"/> (uma cópia do documento do projeto): situação e textos
    /// revisados no lugar dos originais, sem mudar a ordem das chaves. Sem revisões, o documento não muda.</summary>
    internal static void Aplicar(Projeto p, JsonObject documento)
    {
        foreach (var (id, r) in p.Linhas)
        {
            if (Linha(documento, id) is not { } linha) continue;
            if (id == Id(Volumetria, 1))
                foreach (var (k, valor) in FormaPadraoVolumetria)
                    if (!linha.ContainsKey(k) && r.Campos.ContainsKey(k)) linha[k] = valor;
            if (r.Situacao is not null && linha.ContainsKey("situacao")) linha["situacao"] = r.Situacao;
            foreach (var (caminho, texto) in r.Campos) Trocar(linha, caminho, texto);
        }
    }
}

/// <summary>Os comandos de revisão (<see cref="DefinirSituacao"/>, <see cref="DefinirJustificativa"/>, <see cref="DefinirCampo"/>,
/// <see cref="RestaurarCampo"/>, <see cref="RestaurarItemOriginal"/>) quando o alvo é uma linha de tabela (<see cref="TabelasRevisao"/>).
/// Formatos de <c>Antes</c>/<c>Depois</c>, os mesmos dos itens: situação = código efetivo; justificativa = texto ou null; campo =
/// <c>{texto}</c> com o texto efetivo; restaurar a linha = <c>{situacao, campos{caminho: revisado}}</c> com só o que está revisado.</summary>
internal static class MotorLinhas
{
    public static Efeito? Calcular(Projeto p, Comando c)
    {
        var original = TabelasRevisao.Original(p, c.Item) ?? throw new ComandoRecusadoException("A linha não existe na tabela do relatório.");
        p.Linhas.TryGetValue(c.Item, out var r);
        return c switch
        {
            DefinirSituacao d => CalcularSituacao(p, d),
            DefinirJustificativa d => CalcularJustificativa(r, d),
            DefinirCampo d => CalcularCampo(p, original, d),
            RestaurarCampo d => CalcularRestaurarCampo(p, r, d),
            Excluir or Restaurar => throw new ComandoRecusadoException("As linhas das tabelas não podem ser excluídas: edite os textos ou a situação."),
            RestaurarItemOriginal => CalcularRestaurarLinha(c.Item, r),
            _ => throw new ComandoRecusadoException("Comando desconhecido: " + c.GetType().Name),
        };
    }

    public static void Definir(Projeto p, string tipo, string id, string? campo, JsonNode? valor)
    {
        if (TabelasRevisao.Original(p, id) is null) throw new InvalidOperationException("O registro cita uma linha de tabela que não existe: " + id);
        if (!p.Linhas.TryGetValue(id, out var r)) r = new LinhaRevisao();
        switch (tipo)
        {
            case nameof(DefinirSituacao):
                var sit = valor?.GetValue<string>();
                r.Situacao = sit is null || sit == TabelasRevisao.SituacaoOriginal(p, id) ? null : sit;
                break;
            case nameof(DefinirJustificativa):
                r.Justificativa = valor?.GetValue<string>();
                break;
            case nameof(DefinirCampo):
            case nameof(RestaurarCampo):
                DefinirTexto(p, id, r, campo ?? throw new InvalidOperationException("O registro de campo não diz qual campo."), valor?["texto"]?.GetValue<string>());
                break;
            case nameof(RestaurarItemOriginal):
                r.Situacao = valor?["situacao"]?.GetValue<string>();
                r.Campos.Clear();
                if (valor?["campos"] is JsonObject revisados)
                    foreach (var (caminho, texto) in revisados) DefinirTexto(p, id, r, caminho, texto?.GetValue<string>());
                break;
            default:
                throw new InvalidOperationException("Tipo de registro desconhecido para linha de tabela: " + tipo);
        }
        if (r.Vazia) p.Linhas.Remove(id);
        else p.Linhas[id] = r;
    }

    private static void DefinirTexto(Projeto p, string id, LinhaRevisao r, string caminho, string? texto)
    {
        if (texto is null || texto == TabelasRevisao.TextoOriginal(p, id, caminho)) r.Campos.Remove(caminho);
        else r.Campos[caminho] = texto;
    }

    private static Efeito? CalcularSituacao(Projeto p, DefinirSituacao d)
    {
        if (!Situacoes.SitIndice.Contains(d.Situacao))
            throw new ComandoRecusadoException("Situação inválida para a linha da tabela: " + d.Situacao + ".");
        var atual = TabelasRevisao.SituacaoEfetiva(p, d.Item);
        if (atual == d.Situacao) return null;
        return new Efeito(nameof(DefinirSituacao), d.Item, null, atual is null ? null : JsonValue.Create(atual), JsonValue.Create(d.Situacao));
    }

    private static Efeito? CalcularJustificativa(LinhaRevisao? r, DefinirJustificativa d)
    {
        var novo = d.Texto is null ? null : TextoEditado.Normalizar(d.Texto);
        if (novo is { Length: 0 }) novo = null;
        var atual = r?.Justificativa;
        if (atual is { Length: 0 }) atual = null;
        if (novo == atual) return null;
        return new Efeito(nameof(DefinirJustificativa), d.Item, null, JsonValue.Create(r?.Justificativa), JsonValue.Create(novo));
    }

    private static Efeito? CalcularCampo(Projeto p, JsonObject original, DefinirCampo d)
    {
        if (!TabelasRevisao.Caminhos(original).Contains(d.Campo))
            throw new ComandoRecusadoException("Este texto da tabela não pode ser editado (" + d.Campo + ").");
        var texto = TextoEditado.Normalizar(d.Texto ?? "");
        if (texto.Length == 0) throw new ComandoRecusadoException(TabelasRevisao.NomeDoCampo(d.Campo) + ": o texto não pode ficar vazio.");
        var antes = TabelasRevisao.TextoEfetivo(p, d.Item, d.Campo);
        if (antes == texto) return null;
        return new Efeito(nameof(DefinirCampo), d.Item, d.Campo, new JsonObject { ["texto"] = antes }, new JsonObject { ["texto"] = texto });
    }

    private static Efeito? CalcularRestaurarCampo(Projeto p, LinhaRevisao? r, RestaurarCampo d)
    {
        if (r is null || !r.Campos.TryGetValue(d.Campo, out var revisado)) return null;
        return new Efeito(nameof(RestaurarCampo), d.Item, d.Campo,
            new JsonObject { ["texto"] = revisado }, new JsonObject { ["texto"] = TabelasRevisao.TextoOriginal(p, d.Item, d.Campo) });
    }

    private static Efeito? CalcularRestaurarLinha(string id, LinhaRevisao? r)
    {
        if (r is null || !r.Revisada) return null;
        var campos = new JsonObject();
        foreach (var (caminho, texto) in r.Campos) campos[caminho] = texto;
        var antes = new JsonObject { ["situacao"] = r.Situacao, ["campos"] = campos };
        var depois = new JsonObject { ["situacao"] = null, ["campos"] = new JsonObject() };
        return new Efeito(nameof(RestaurarItemOriginal), id, null, antes, depois);
    }
}
