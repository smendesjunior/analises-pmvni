using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Quem está com o pacote aberto: gravado na trava, com o último batimento.</summary>
public sealed record InfoTrava(string Maquina, string Usuario, int Pid, DateTimeOffset InicioUtc, DateTimeOffset BatimentoUtc);

/// <summary>Resultado de <see cref="Trava.Adquirir"/>: ou a trava é nossa (<see cref="Adquirida"/>) ou há outro dono vivo (<see cref="Ocupada"/>).</summary>
public sealed record ResultadoTrava(Trava? Adquirida, InfoTrava? Ocupada);

/// <summary>Trava de edição do pacote: o arquivo oculto <c>.~nome.pmvrel.lock</c> ao lado dele, renovado a cada <see cref="Batimento"/>.
/// Uma trava sem batimento há mais de <see cref="Abandono"/> é considerada abandonada e é retomada.</summary>
public sealed class Trava : IDisposable
{
    public static readonly TimeSpan Batimento = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Abandono = TimeSpan.FromMinutes(2);

    private readonly string _caminho;
    private readonly Func<DateTimeOffset> _relogio;
    private InfoTrava _info;
    private bool _liberada;

    private Trava(string caminho, InfoTrava info, Func<DateTimeOffset> relogio)
    {
        _caminho = caminho;
        _info = info;
        _relogio = relogio;
    }

    public static string CaminhoDe(string pacote)
    {
        ArgumentException.ThrowIfNullOrEmpty(pacote);
        var completo = Path.GetFullPath(pacote);
        return Path.Combine(Path.GetDirectoryName(completo)!, ".~" + Path.GetFileName(completo) + ".lock");
    }

    public static ResultadoTrava Adquirir(string pacote, InfoTrava eu, Func<DateTimeOffset> relogio)
    {
        ArgumentNullException.ThrowIfNull(eu);
        ArgumentNullException.ThrowIfNull(relogio);
        var caminho = CaminhoDe(pacote);
        var minha = eu with { BatimentoUtc = relogio() };

        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            if (TentarCriar(caminho, minha)) return new ResultadoTrava(new Trava(caminho, minha, relogio), null);

            var atual = Ler(caminho);
            var agora = relogio();
            if (atual is not null)
            {
                if (agora - atual.BatimentoUtc <= Abandono) return new ResultadoTrava(null, atual);
            }
            else if (File.Exists(caminho))
            {
                // Ilegível (provavelmente sendo regravada agora): só é abandonada se o arquivo também estiver velho.
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(caminho) <= Abandono)
                    return new ResultadoTrava(null, new InfoTrava("?", "?", 0, agora, agora));
            }
            Apagar(caminho);
        }
        var depois = Ler(caminho) ?? new InfoTrava("?", "?", 0, relogio(), relogio());
        return new ResultadoTrava(null, depois);
    }

    /// <summary>Renova o batimento. Se a trava deixou de ser nossa (retomada por outro), não faz nada.</summary>
    public void Bater()
    {
        if (_liberada) return;
        var atual = Ler(_caminho);
        if (atual is null || !MesmoDono(atual, _info)) return;
        _info = _info with { BatimentoUtc = _relogio() };
        using var f = new FileStream(_caminho, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        f.SetLength(0);
        var b = Conteudo(_info);
        f.Write(b, 0, b.Length);
        f.Flush(true);
    }

    /// <summary>Apaga a trava só se ainda for a nossa.</summary>
    public void Dispose()
    {
        if (_liberada) return;
        _liberada = true;
        var atual = Ler(_caminho);
        if (atual is not null && MesmoDono(atual, _info)) Apagar(_caminho);
    }

    private static bool MesmoDono(InfoTrava a, InfoTrava b) =>
        a.Pid == b.Pid && a.Maquina == b.Maquina && a.Usuario == b.Usuario && a.InicioUtc == b.InicioUtc;

    private static bool TentarCriar(string caminho, InfoTrava info)
    {
        try
        {
            using var f = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var b = Conteudo(info);
            f.Write(b, 0, b.Length);
            f.Flush(true);
        }
        catch (IOException) when (File.Exists(caminho)) { return false; }
        File.SetAttributes(caminho, File.GetAttributes(caminho) | FileAttributes.Hidden);
        return true;
    }

    private static void Apagar(string caminho)
    {
        try
        {
            File.SetAttributes(caminho, FileAttributes.Normal);
            File.Delete(caminho);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static byte[] Conteudo(InfoTrava i) => Encoding.UTF8.GetBytes(new JsonObject
    {
        ["maquina"] = i.Maquina,
        ["usuario"] = i.Usuario,
        ["pid"] = i.Pid,
        ["inicio_utc"] = Manifesto.TextoUtc(i.InicioUtc),
        ["batimento_utc"] = Manifesto.TextoUtc(i.BatimentoUtc),
    }.ToJsonString());

    private static InfoTrava? Ler(string caminho)
    {
        try
        {
            using var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            f.CopyTo(ms);
            var j = JsonNode.Parse(ms.ToArray()) as JsonObject;
            if (j is null) return null;
            return new InfoTrava(
                j["maquina"]!.GetValue<string>(), j["usuario"]!.GetValue<string>(), j["pid"]!.GetValue<int>(),
                Manifesto.DataUtc(j["inicio_utc"]!.GetValue<string>()), Manifesto.DataUtc(j["batimento_utc"]!.GetValue<string>()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NullReferenceException
                                      or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
