using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>O que o diário guardou: os registros, o instante do último e se o pacote ainda é o mesmo de quando o diário começou.</summary>
public sealed record Recuperacao(IReadOnlyList<RegistroHistorico> Registros, DateTimeOffset UltimoUtc, bool DaMesmaVersao)
{
    /// <summary>Os <c>N</c> que existem tanto no diário quanto no histórico do pacote mas com conteúdo diferente.
    /// <see cref="SessaoRevisao.Reaplicar"/> ignora registros com <c>N</c> já presente; quem recupera deve checar isto antes
    /// e não aplicar em silêncio um diário que diverge do pacote.</summary>
    public IReadOnlyList<int> Divergencias(IReadOnlyList<RegistroHistorico> historicoDoPacote)
    {
        ArgumentNullException.ThrowIfNull(historicoDoPacote);
        var porN = historicoDoPacote.ToLookup(r => r.N);
        var divergentes = new List<int>();
        foreach (var registro in Registros)
        {
            if (!porN.Contains(registro.N)) continue;
            var linha = HistoricoJsonl.Linha(registro);
            if (porN[registro.N].Any(r => HistoricoJsonl.Linha(r) != linha)) divergentes.Add(registro.N);
        }
        return divergentes;
    }
}

/// <summary>Diário de recuperação: um jsonl em <c>&lt;raiz&gt;\recuperacao</c>, com cabeçalho e uma linha por registro, gravado e
/// descarregado em disco a cada registro, para que uma queda não perca o trabalho não gravado no pacote.</summary>
public sealed class Diario : IDisposable
{
    private FileStream? _f;
    private readonly string _caminho;

    private Diario(string caminho, FileStream f)
    {
        _caminho = caminho;
        _f = f;
    }

    public static string CaminhoDe(string raizDados, string pacote) => Path.Combine(raizDados, "recuperacao", Hash(pacote) + ".jsonl");

    private static string Hash(string pacote) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(pacote).ToLowerInvariant()))).ToLowerInvariant();

    public static Diario Iniciar(string raizDados, string pacote, string sha256Base)
    {
        var caminho = CaminhoDe(raizDados, pacote);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        // Nunca apaga em silêncio um diário que tem trabalho: guarda-o como órfão antes de começar outro.
        if (File.Exists(caminho) && Ler(raizDados, pacote, sha256Base) is not null) Arquivar(raizDados, pacote);
        var f = new FileStream(ArquivoGerado.Caminho(caminho), FileMode.Create, FileAccess.Write, FileShare.Read);
        var d = new Diario(caminho, f);
        d.Acrescentar(new JsonObject
        {
            ["tipo"] = "cabecalho",
            ["pacote"] = Path.GetFullPath(pacote),
            ["sha256_base"] = sha256Base,
            ["iniciado_utc"] = Manifesto.TextoUtc(DateTimeOffset.UtcNow),
        }.ToJsonString());
        return d;
    }

    public void Registrar(RegistroHistorico r)
    {
        ArgumentNullException.ThrowIfNull(r);
        Acrescentar(HistoricoJsonl.Linha(r));
    }

    private void Acrescentar(string linha)
    {
        if (_f is null) throw new ObjectDisposedException(nameof(Diario));
        var b = new UTF8Encoding(false).GetBytes(linha + "\n");
        _f.Write(b, 0, b.Length);
        _f.Flush(true);
    }

    /// <summary>Fecha e apaga o diário (o pacote foi gravado).</summary>
    public void Descartar()
    {
        Dispose();
        try { File.Delete(_caminho); } catch (IOException) { }
    }

    /// <summary>Fecha sem apagar (o diário fica para uma recuperação).</summary>
    public void Dispose()
    {
        _f?.Dispose();
        _f = null;
    }

    /// <summary>Lê o diário do pacote, ou null se não há ou não tem registros. Uma linha final truncada (queda no meio da escrita)
    /// é ignorada; uma linha inválida no meio encerra a leitura ali (o que vem depois não é confiável).</summary>
    public static Recuperacao? Ler(string raizDados, string pacote, string sha256Atual)
    {
        var caminho = CaminhoDe(raizDados, pacote);
        if (!File.Exists(caminho)) return null;
        string texto;
        try
        {
            using var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(f, new UTF8Encoding(false), false);
            texto = sr.ReadToEnd();
        }
        catch (IOException) { return null; }

        var linhas = texto.Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (linhas.Count == 0) return null;
        string? sha;
        try
        {
            var cab = JsonFiel.Ler(Encoding.UTF8.GetBytes(linhas[0]));
            if (cab["tipo"]?.GetValue<string>() != "cabecalho") return null;
            sha = cab["sha256_base"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }

        var registros = new List<RegistroHistorico>();
        foreach (var l in linhas.Skip(1))
        {
            try { registros.AddRange(HistoricoJsonl.Ler(Encoding.UTF8.GetBytes(l))); }
            catch (JsonException) { break; }
        }
        if (registros.Count == 0) return null;
        return new Recuperacao(registros, registros.Max(r => r.DataUtc), string.Equals(sha, sha256Atual, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Renomeia o diário para <c>&lt;sha&gt;.orfao-aaaaMMddHHmmss.jsonl</c> (a recuperação foi recusada) e devolve o novo caminho.</summary>
    public static string Arquivar(string raizDados, string pacote)
    {
        var caminho = CaminhoDe(raizDados, pacote);
        var baseNome = Path.Combine(Path.GetDirectoryName(caminho)!,
            Path.GetFileNameWithoutExtension(caminho) + ".orfao-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        // Nunca sobrescreve um órfão que já existe: acrescenta -2, -3...
        for (var n = 1; ; n++)
        {
            var novo = n == 1 ? baseNome + ".jsonl" : baseNome + "-" + n.ToString(CultureInfo.InvariantCulture) + ".jsonl";
            if (File.Exists(novo)) continue;
            try
            {
                File.Move(caminho, ArquivoGerado.Caminho(novo), false);
                // A idade do órfão (limpeza de 30 dias) conta do dia em que foi guardado, não da última gravação do diário:
                // o Move preserva a data antiga, e um diário velho guardado agora sairia na abertura seguinte.
                try { File.SetLastWriteTimeUtc(novo, DateTime.UtcNow); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                return novo;
            }
            catch (IOException) when (File.Exists(novo)) { }
        }
    }
}
