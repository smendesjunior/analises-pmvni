using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Um modelo de despacho do registro oficial do kit, conferido: <see cref="Caminho"/> é o arquivo (dentro da raiz do kit) cujo sha256 é
/// <see cref="Sha256"/>, igual ao do registro; <see cref="RevisaoRegistro"/> e <see cref="Sha256Registro"/> vão para o recibo.</summary>
public sealed record ModeloDespacho(TipoDespacho Tipo, string Papel, string CaminhoRelativo, string Caminho, string Sha256, int RevisaoRegistro, string Sha256Registro);

/// <summary>Os tipos oferecidos (<see cref="Modelos"/>, na ordem Correção, Aprovação) e o motivo de cada tipo indisponível. <see cref="Fonte"/>:
/// de onde os modelos foram lidos (null = nenhuma fonte achada); os textos fixos vêm da mesma fonte. <see cref="Falhas"/>: as fontes que
/// existem mas não serviram, antes ou depois da usada ("&lt;origem&gt;: &lt;motivo&gt;"), para Ajustes mostrar qual falhou.</summary>
public sealed record ModelosDisponiveis(IReadOnlyList<ModeloDespacho> Modelos, IReadOnlyDictionary<TipoDespacho, string> Indisponiveis, FonteModelos? Fonte = null)
{
    public IReadOnlyList<string> Falhas { get; init; } = [];
}

/// <summary>Leitura do registro oficial de modelos do kit (<c>TEMPLATES_OFICIAIS.json</c>, desenho §10.1), na fonte escolhida por
/// <see cref="FontesModelos"/> (Tarefa 22). Só lê; nada é gravado no kit. Papéis: <c>correction</c> → Correção e <c>approval_simple</c> →
/// Aprovação; os outros são ignorados. É a porta do <c>check_registry</c> do <c>validate_template_fidelity.py</c>: papel repetido,
/// caminho fora da raiz, arquivo ausente e sha256 diferente deixam o tipo indisponível.</summary>
public static class RegistroModelos
{
    public const string CaminhoRegistro = "02 - MODELOS DE DOCUMENTOS/TEMPLATES_OFICIAIS.json";
    const int RevisaoMinima = 10;

    static readonly (TipoDespacho Tipo, string Papel)[] Papeis = { (TipoDespacho.Correcao, "correction"), (TipoDespacho.Aprovacao, "approval_simple") };

    public static string Rotulo(TipoDespacho t) => t switch
    {
        TipoDespacho.Correcao => "Correção",
        TipoDespacho.Aprovacao => "Aprovação",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Tipo de despacho não emitido pelo editor."),
    };

    public static string NomeNoArquivo(TipoDespacho t) => t switch
    {
        TipoDespacho.Correcao => "Correcao",
        TipoDespacho.Aprovacao => "Aprovacao",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Tipo de despacho não emitido pelo editor."),
    };

    /// <summary>Só a pasta do kit (<see cref="FontesModelos.DaPastaDoKit"/>); usado pelo verificador e pelos testes.</summary>
    public static ModelosDisponiveis Ler(string raizKit)
    {
        ArgumentNullException.ThrowIfNull(raizKit);
        var raiz = Path.GetFullPath(raizKit);
        return Ler(FontesModelos.DaPastaDoKit(raiz), raiz);
    }

    /// <summary>As fontes na ordem (<see cref="FontesModelos.Candidatas"/>): vale a primeira que existe e oferece os dois tipos; uma fonte que
    /// existe mas falha cai para a próxima, e a falha vai para <see cref="ModelosDisponiveis.Falhas"/>. Se nenhuma oferece os dois, fica a
    /// primeira que existe (com o motivo de cada tipo indisponível). Sem nenhuma: <see cref="FontesModelos.NaoEncontrados"/> com a pasta do
    /// kit (<paramref name="raizKit"/>).</summary>
    public static ModelosDisponiveis Ler(IReadOnlyList<FonteModelos> candidatas, string? raizKit = null)
    {
        ArgumentNullException.ThrowIfNull(candidatas);
        var lidas = candidatas.Where(f => f.Existe).Select(Ler).ToList();
        if (lidas.Count == 0)
        {
            var motivo = FontesModelos.NaoEncontrados(raizKit);
            return new ModelosDisponiveis([], Papeis.ToDictionary(p => p.Tipo, _ => motivo));
        }
        var usada = lidas.FirstOrDefault(m => m.Indisponiveis.Count == 0) ?? lidas[0];
        var falhas = lidas.Where(m => !ReferenceEquals(m, usada) && m.Indisponiveis.Count > 0)
            .Select(m => m.Fonte!.Origem + ": " + string.Join(" ", m.Indisponiveis.Values.Distinct(StringComparer.Ordinal))).ToList();
        return usada with { Falhas = falhas };
    }

    public static ModelosDisponiveis Ler(FonteModelos fonte)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        var modelos = new List<ModeloDespacho>();
        var indisponiveis = new Dictionary<TipoDespacho, string>();
        ModelosDisponiveis Todos(string motivo)
        {
            foreach (var (t, _) in Papeis) indisponiveis[t] = motivo;
            return new ModelosDisponiveis(modelos, indisponiveis, fonte);
        }

        var raiz = Path.GetFullPath(fonte.Raiz);
        var caminhoRegistro = fonte.Registro;
        if (!Directory.Exists(raiz) || !File.Exists(caminhoRegistro)) return Todos("O registro de modelos não foi encontrado em " + caminhoRegistro + ".");

        byte[] bytesRegistro;
        JsonObject registro;
        int revisao;
        try
        {
            bytesRegistro = File.ReadAllBytes(caminhoRegistro);
            registro = JsonFiel.Ler(bytesRegistro);
            revisao = registro["templates_revision"]!.GetValue<int>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
                                      or FormatException or NullReferenceException)
        {
            return Todos("O registro de modelos do kit não pôde ser lido (" + MensagensDeErro.ParaUsuario(e) + ").");
        }
        if (revisao < RevisaoMinima)
            return Todos($"O registro de modelos do kit está na revisão {revisao}; o editor precisa da revisão {RevisaoMinima} ou posterior.");
        var shaRegistro = Sha(bytesRegistro);
        var entradas = (registro["templates"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();

        foreach (var (tipo, papel) in Papeis)
        {
            var doPapel = entradas.Where(e => Texto(e, "role") == papel).ToList();
            ModeloDespacho? modelo = null;
            // Como o check_registry do kit: papel repetido é erro (não se escolhe um dos dois).
            var motivo = doPapel.Count > 1 ? "O registro de modelos do kit tem o modelo " + papel + " repetido."
                : Conferir(fonte, raiz, tipo, papel, doPapel.FirstOrDefault(), out modelo, revisao, shaRegistro);
            if (motivo is null && modelo is not null) modelos.Add(modelo);
            else indisponiveis[tipo] = motivo!;
        }
        return new ModelosDisponiveis(modelos, indisponiveis, fonte);
    }

    static string? Conferir(FonteModelos fonte, string raiz, TipoDespacho tipo, string papel, JsonObject? entrada, out ModeloDespacho? modelo, int revisao, string shaRegistro)
    {
        modelo = null;
        if (entrada is null) return "O registro de modelos do kit não tem o modelo " + papel + ".";
        var relativo = Texto(entrada, "path");
        var shaEsperado = Texto(entrada, "sha256");
        if (relativo is null || shaEsperado is null) return "O registro de modelos do kit não tem o modelo " + papel + ".";

        string caminho;
        try { caminho = Path.GetFullPath(Path.Combine(raiz, relativo.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "O registro aponta um modelo fora da pasta do kit: " + relativo + ".";
        }
        var raizComBarra = raiz.EndsWith(Path.DirectorySeparatorChar) ? raiz : raiz + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relativo) || !caminho.StartsWith(raizComBarra, StringComparison.OrdinalIgnoreCase))
            return "O registro aponta um modelo fora da pasta do kit: " + relativo + ".";
        if (!File.Exists(caminho)) return "O modelo " + relativo + " não foi encontrado no kit.";

        byte[] bytes;
        try { bytes = File.ReadAllBytes(caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "O modelo " + relativo + " não foi encontrado no kit."; }
        var sha = Sha(bytes);
        if (!string.Equals(sha, shaEsperado, StringComparison.OrdinalIgnoreCase))
            return "O modelo " + relativo + " não confere com o registro do kit (sha256 diferente).";

        try { TextosFixos.Ler(fonte, tipo); }
        catch (TextosForaDoPadraoException e) { return e.Message; }
        try { PreenchimentoDocx.LerModelo(bytes, tipo); }
        catch (ModeloForaDoPadraoException e) { return "O modelo " + relativo + " não está no formato esperado (âncora \"" + e.Ancora + "\")."; }

        modelo = new ModeloDespacho(tipo, papel, relativo, caminho, sha, revisao, shaRegistro);
        return null;
    }

    static string? Texto(JsonObject o, string chave) => o[chave] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
}
