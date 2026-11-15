using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Um modelo de despacho do registro oficial do kit, conferido: <see cref="Caminho"/> é o arquivo (dentro da raiz do kit) cujo sha256 é
/// <see cref="Sha256"/>, igual ao do registro; <see cref="RevisaoRegistro"/> e <see cref="Sha256Registro"/> vão para o recibo.</summary>
public sealed record ModeloDespacho(TipoDespacho Tipo, string Papel, string CaminhoRelativo, string Caminho, string Sha256, int RevisaoRegistro, string Sha256Registro);

/// <summary>Os tipos oferecidos (<see cref="Modelos"/>, na ordem Correção, Aprovação) e o motivo de cada tipo indisponível.</summary>
public sealed record ModelosDisponiveis(IReadOnlyList<ModeloDespacho> Modelos, IReadOnlyDictionary<TipoDespacho, string> Indisponiveis);

/// <summary>Leitura do registro oficial de modelos do kit (<c>TEMPLATES_OFICIAIS.json</c>, desenho §10.1). Só lê; nada é gravado no kit.
/// Papéis: <c>correction</c> → Correção e <c>approval_simple</c> → Aprovação; os outros são ignorados.</summary>
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

    public static ModelosDisponiveis Ler(string raizKit)
    {
        ArgumentNullException.ThrowIfNull(raizKit);
        var modelos = new List<ModeloDespacho>();
        var indisponiveis = new Dictionary<TipoDespacho, string>();
        ModelosDisponiveis Todos(string motivo)
        {
            foreach (var (t, _) in Papeis) indisponiveis[t] = motivo;
            return new ModelosDisponiveis(modelos, indisponiveis);
        }

        var raiz = Path.GetFullPath(raizKit);
        var caminhoRegistro = Path.Combine(raiz, CaminhoRegistro.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(raiz) || !File.Exists(caminhoRegistro))
            return Todos("Kit PMVNI não encontrado em " + raizKit + ". Escolha a pasta do kit em Ajustes.");

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
            return Todos("O registro de modelos do kit não pôde ser lido (" + e.Message + ").");
        }
        if (revisao < RevisaoMinima)
            return Todos($"O registro de modelos do kit está na revisão {revisao}; o editor precisa da revisão {RevisaoMinima} ou posterior.");
        var shaRegistro = Sha(bytesRegistro);
        var entradas = (registro["templates"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();

        foreach (var (tipo, papel) in Papeis)
        {
            var entrada = entradas.FirstOrDefault(e => Texto(e, "role") == papel);
            var motivo = Conferir(raiz, tipo, papel, entrada, out var modelo, revisao, shaRegistro);
            if (modelo is not null) modelos.Add(modelo);
            else indisponiveis[tipo] = motivo!;
        }
        return new ModelosDisponiveis(modelos, indisponiveis);
    }

    static string? Conferir(string raiz, TipoDespacho tipo, string papel, JsonObject? entrada, out ModeloDespacho? modelo, int revisao, string shaRegistro)
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

        try { TextosFixos.Ler(raiz, tipo); }
        catch (TextosForaDoPadraoException e) { return e.Message; }
        try { PreenchimentoDocx.LerModelo(bytes, tipo); }
        catch (ModeloForaDoPadraoException e) { return "O modelo " + relativo + " não está no formato esperado (âncora \"" + e.Ancora + "\")."; }

        modelo = new ModeloDespacho(tipo, papel, relativo, caminho, sha, revisao, shaRegistro);
        return null;
    }

    static string? Texto(JsonObject o, string chave) => o[chave] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
}
