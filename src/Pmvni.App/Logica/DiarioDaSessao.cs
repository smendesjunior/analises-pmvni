using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O diário de recuperação do pacote aberto no app (desenho §5.1): começa na abertura (com o que foi recuperado), recebe cada
/// registro e recomeça depois de cada gravação com o sha novo. Nunca apaga um diário com trabalho que o pacote ainda não tem: o que
/// chegou durante a gravação vai para o diário novo e o anterior é guardado como órfão. Falha de disco (disco cheio, pasta sem escrita)
/// nunca é engolida: o método devolve <c>false</c> e <see cref="UltimoErro"/> diz o motivo, para a janela mostrar.</summary>
public sealed class DiarioDaSessao(string raizDados) : IDisposable
{
    Diario? diario;

    /// <summary>O diário está aberto e recebendo os registros.</summary>
    public bool Ativo => diario is not null;

    /// <summary>O motivo da última falha (null depois de um começo bem-sucedido).</summary>
    public string? UltimoErro { get; private set; }

    /// <summary>Começa o diário do <paramref name="pacote"/> (o sha do arquivo como está no disco) e grava nele os
    /// <paramref name="pendentes"/> (o que foi recuperado e ainda não está no pacote). Um diário anterior com trabalho é guardado
    /// como órfão (<see cref="Diario.Iniciar"/>), nunca truncado.</summary>
    public bool Iniciar(string pacote, string sha256Base, IEnumerable<RegistroHistorico> pendentes)
    {
        Fechar(guardar: true);
        try
        {
            var d = Diario.Iniciar(raizDados, pacote, sha256Base);
            try { foreach (var r in pendentes) d.Registrar(r); }
            catch { d.Dispose(); throw; }
            diario = d;
            UltimoErro = null;
            return true;
        }
        catch (Exception e) when (Falha(e)) { UltimoErro = e.Message; return false; }
    }

    /// <summary>Acrescenta o registro (descarregado no disco). <c>false</c> se o diário não está aberto ou a escrita falhou.</summary>
    public bool Registrar(RegistroHistorico r)
    {
        if (diario is not { } d)
        {
            UltimoErro ??= "o diário de recuperação não está aberto";
            return false;
        }
        try { d.Registrar(r); return true; }
        catch (Exception e) when (Falha(e)) { UltimoErro = e.Message; return false; }
    }

    /// <summary>O pacote foi gravado (em <paramref name="caminhoAgora"/>; igual a <paramref name="caminhoAntes"/> num Salvar, outro num
    /// Salvar como): recomeça o diário com o sha novo e os <paramref name="extras"/> (comandos aplicados durante a gravação, que o arquivo
    /// ainda não tem). Sem extras, o diário anterior sai (tudo está no arquivo); com extras, ele é guardado como órfão.
    /// <paramref name="continuar"/> = false (a sessão ficou só para leitura): não começa outro.</summary>
    public bool DepoisDeGravar(string caminhoAntes, string caminhoAgora, string sha256Novo, IReadOnlyList<RegistroHistorico> extras, bool continuar)
    {
        var mesmo = string.Equals(Path.GetFullPath(caminhoAntes), Path.GetFullPath(caminhoAgora), StringComparison.OrdinalIgnoreCase);
        var velho = diario;
        diario = null;
        try
        {
            if (!continuar || mesmo)
            {
                // Mesmo arquivo: o anterior precisa estar fechado antes de começar outro no mesmo caminho.
                if (extras.Count == 0) velho?.Descartar(); else velho?.Dispose();
                return !continuar || Iniciar(caminhoAgora, sha256Novo, extras);
            }
            var ok = Iniciar(caminhoAgora, sha256Novo, extras);
            if (ok || extras.Count == 0) velho?.Descartar(); else velho?.Dispose();
            return ok;
        }
        catch (Exception e) when (Falha(e)) { velho?.Dispose(); UltimoErro = e.Message; return false; }
    }

    /// <summary>Fecha o diário: <paramref name="guardar"/> = true deixa o arquivo para uma recuperação (há alterações não gravadas);
    /// false o apaga (tudo está no pacote).</summary>
    public void Fechar(bool guardar)
    {
        var d = diario;
        diario = null;
        if (d is null) return;
        try { if (guardar) d.Dispose(); else d.Descartar(); }
        catch (Exception e) when (Falha(e)) { UltimoErro = e.Message; }
    }

    /// <summary>Fecha e guarda o diário do <paramref name="pacote"/> como órfão (Recarregar, recuperação recusada ou de outra versão);
    /// devolve o caminho do órfão, ou null se não havia diário (ou não deu para renomear: <see cref="UltimoErro"/>).</summary>
    public string? Arquivar(string pacote)
    {
        Fechar(guardar: true);
        try { return File.Exists(Diario.CaminhoDe(raizDados, pacote)) ? Diario.Arquivar(raizDados, pacote) : null; }
        catch (Exception e) when (Falha(e)) { UltimoErro = e.Message; return null; }
    }

    /// <summary>Fecha sem apagar: o que estiver no diário fica para uma recuperação.</summary>
    public void Dispose() => Fechar(guardar: true);

    static bool Falha(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException;
}

/// <summary>O que fazer, na abertura, com o diário encontrado para o pacote.</summary>
public enum AcaoRecuperacao { Nada, Perguntar, Arquivar }

/// <summary><see cref="AcaoRecuperacao.Perguntar"/>: <see cref="Comandos"/> registros novos para reaplicar; <see cref="AcaoRecuperacao.Arquivar"/>:
/// o <see cref="Motivo"/> (mostrado ao usuário junto com o nome do órfão).</summary>
public sealed record DecisaoRecuperacao(AcaoRecuperacao Acao, int Comandos = 0, string? Motivo = null);

public static class RecuperacaoAbertura
{
    /// <summary>Diário da mesma versão do pacote, sem divergência com o histórico dele e com registros que o pacote não tem ⇒ perguntar.
    /// De outra versão ou divergente ⇒ guardar como órfão, sem aplicar nada (nunca em silêncio). Sem diário, ou com tudo já no pacote ⇒
    /// nada (o <see cref="Diario.Iniciar"/> seguinte guarda o antigo).</summary>
    public static DecisaoRecuperacao Decidir(Recuperacao? rec, IReadOnlyList<RegistroHistorico> historicoDoPacote)
    {
        ArgumentNullException.ThrowIfNull(historicoDoPacote);
        if (rec is null) return new(AcaoRecuperacao.Nada);
        if (!rec.DaMesmaVersao)
            return new(AcaoRecuperacao.Arquivar, Motivo: "Havia alterações não gravadas de outra versão deste arquivo (ele mudou depois delas); não foram aplicadas.");
        if (rec.Divergencias(historicoDoPacote).Count > 0)
            return new(AcaoRecuperacao.Arquivar, Motivo: "Havia alterações não gravadas que não batem com o histórico do arquivo; não foram aplicadas.");
        var ultimo = historicoDoPacote.Count == 0 ? 0 : historicoDoPacote.Max(r => r.N);
        var novos = rec.Registros.Count(r => r.N > ultimo);
        return novos == 0 ? new(AcaoRecuperacao.Nada) : new(AcaoRecuperacao.Perguntar, novos);
    }
}

/// <summary>Textos de somente leitura, trava e diário mostrados pela janela.</summary>
public static class TextosGravacao
{
    /// <summary><c>"Aberto por maria em PC-OBRAS desde 14:05: somente leitura."</c> (hora local do início da trava).</summary>
    public static string SomenteLeituraOcupada(InfoTrava t) =>
        t.Maquina == "?" && t.Usuario == "?"
            ? "Aberto em outra janela ou por outro usuário (a trava não pôde ser lida): somente leitura."
            : $"Aberto por {t.Usuario} em {t.Maquina} desde {t.InicioUtc.ToLocalTime():HH:mm}: somente leitura.";

    public static string SomenteLeituraSemTrava(string erro) =>
        $"Não foi possível travar o arquivo para edição ({erro.TrimEnd('.')}): somente leitura.";

    public const string TravaPerdida =
        "A trava de edição deste arquivo foi perdida (apagada ou retomada por outro programa): somente leitura. "
        + "As alterações que ainda não estavam no arquivo continuam no diário de recuperação; use Salvar como para guardá-las.";
}
