using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Quem está com o pacote aberto: gravado na trava, com o último batimento.</summary>
public sealed record InfoTrava(string Maquina, string Usuario, int Pid, DateTimeOffset InicioUtc, DateTimeOffset BatimentoUtc);

/// <summary>Resultado de <see cref="Trava.Adquirir"/>: a trava é nossa (<see cref="Adquirida"/>), há outro dono vivo (<see cref="Ocupada"/>)
/// ou não foi possível travar (<see cref="Erro"/>: pasta sem escrita, disco cheio...; quem chama abre o pacote só para leitura).</summary>
public sealed record ResultadoTrava(Trava? Adquirida, InfoTrava? Ocupada, string? Erro = null);

/// <summary>Trava de edição do pacote: o arquivo oculto <c>.~nome.pmvrel.lock</c> ao lado dele, renovado a cada <see cref="Batimento"/>.
/// Uma trava sem batimento há mais de <see cref="Abandono"/>, com batimento mais de <see cref="ToleranciaFuturo"/> no futuro
/// (relógio errado) ou do mesmo usuário e máquina com o processo já encerrado é considerada abandonada e é retomada.</summary>
public sealed class Trava : IDisposable
{
    public static readonly TimeSpan Batimento = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Abandono = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ToleranciaFuturo = TimeSpan.FromMinutes(5);

    /// <summary>Só para testes: chamado entre a leitura da trava abandonada e a sua remoção.</summary>
    internal static Action<string>? AntesDeApagar;

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

    /// <summary>Nunca lança por falha de disco: devolve <see cref="ResultadoTrava.Erro"/>.</summary>
    public static ResultadoTrava Adquirir(string pacote, InfoTrava eu, Func<DateTimeOffset> relogio)
    {
        ArgumentNullException.ThrowIfNull(eu);
        ArgumentNullException.ThrowIfNull(relogio);
        try
        {
            return AdquirirCore(pacote, eu, relogio);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return new ResultadoTrava(null, null, e.Message);
        }
    }

    private static ResultadoTrava AdquirirCore(string pacote, InfoTrava eu, Func<DateTimeOffset> relogio)
    {
        var caminho = CaminhoDe(pacote);
        var minha = eu with { BatimentoUtc = relogio() };

        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            if (TentarCriar(caminho, minha))
            {
                // Confere que a trava gravada ainda é a nossa (outra janela pode ter retomado a mesma trava abandonada).
                var gravada = Ler(caminho);
                if (gravada is not null && MesmoDono(gravada, minha)) return new ResultadoTrava(new Trava(caminho, minha, relogio), null);
                return new ResultadoTrava(null, gravada ?? Desconhecida(relogio()));
            }

            var atual = Ler(caminho);
            var agora = relogio();
            if (atual is not null)
            {
                if (!Abandonada(atual, eu, agora)) return new ResultadoTrava(null, atual);
                AntesDeApagar?.Invoke(caminho);
                // Só apaga se ainda for exatamente a mesma trava abandonada que lemos.
                var reLida = Ler(caminho);
                if (reLida is null) continue;
                if (reLida != atual) return new ResultadoTrava(null, reLida);
            }
            else if (File.Exists(caminho))
            {
                // Ilegível (provavelmente sendo regravada agora): só é abandonada se o arquivo também estiver velho.
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(caminho) <= Abandono) return new ResultadoTrava(null, Desconhecida(agora));
                AntesDeApagar?.Invoke(caminho);
                if (Ler(caminho) is { } nova) return new ResultadoTrava(null, nova);
            }
            Apagar(caminho);
        }
        return new ResultadoTrava(null, Ler(caminho) ?? Desconhecida(relogio()));
    }

    private static InfoTrava Desconhecida(DateTimeOffset agora) => new("?", "?", 0, agora, agora);

    private static bool Abandonada(InfoTrava t, InfoTrava eu, DateTimeOffset agora)
    {
        if (agora - t.BatimentoUtc > Abandono) return true;
        if (t.BatimentoUtc - agora > ToleranciaFuturo) return true;
        // Reinício depois de uma queda: mesmo usuário e máquina, processo que não existe mais.
        return t.Maquina == eu.Maquina && t.Usuario == eu.Usuario && t.Pid != eu.Pid && !ProcessoRodando(t.Pid);
    }

    private static bool ProcessoRodando(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Exception) { return true; } // sem como saber: considera vivo
    }

    /// <summary>Renova o batimento. <c>false</c> = a trava deixou de ser nossa (apagada ou retomada por outro): quem chama deve avisar o usuário
    /// e parar de gravar. Falha passageira de disco não lança: devolve <c>true</c> e a próxima batida tenta de novo.</summary>
    public bool Bater()
    {
        if (_liberada) return false;
        try
        {
            if (!File.Exists(_caminho)) return false;
            var atual = Ler(_caminho);
            if (atual is null) return true; // existe mas não deu para ler agora: tenta na próxima
            if (!MesmoDono(atual, _info)) return false;
            var novo = _info with { BatimentoUtc = _relogio() };
            using (var f = new FileStream(_caminho, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var b = Conteudo(novo);
                f.SetLength(0);
                f.Write(b, 0, b.Length);
                f.Flush(true);
            }
            _info = novo;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return File.Exists(_caminho); // some = não dá para confirmar que ainda é nossa
        }
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
        try { File.SetAttributes(caminho, File.GetAttributes(caminho) | FileAttributes.Hidden); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* sumiu ou não deu: a conferência seguinte decide */ }
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
