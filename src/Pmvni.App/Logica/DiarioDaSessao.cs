using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O diário de recuperação do pacote aberto no app (desenho §5.1): começa na abertura (com o que foi recuperado), recebe cada
/// registro e recomeça depois de cada gravação com o sha novo. Nunca apaga um diário com trabalho que o pacote ainda não tem: o que
/// chegou durante a gravação vai para o diário novo e o anterior é guardado como órfão. Falha de disco (disco cheio, pasta sem escrita)
/// nunca é engolida: o método devolve <c>false</c> e <see cref="UltimoErro"/> diz o motivo, para a janela mostrar.</summary>
public sealed class DiarioDaSessao(string raizDados) : IDisposable
{
    Diario? diario;
    (string Pacote, string Sha)? alvo; // o diário que deveria estar aberto (para tentar de novo depois de uma falha)

    /// <summary>Órfãos (<c>*.orfao-*.jsonl</c>) parados há mais de <see cref="IdadeOrfao"/> saem na abertura do app (T10-C1).</summary>
    public static readonly TimeSpan IdadeOrfao = TimeSpan.FromDays(30);

    /// <summary>Apaga de <c>&lt;raiz&gt;\recuperacao</c> só os órfãos cuja última gravação tem mais de 30 dias (nunca um mais novo, nunca um
    /// diário vivo). Devolve quantos saíram; falha num arquivo não impede os outros.</summary>
    public static int LimparOrfaosAntigos(string raizDados, DateTime agoraUtc)
    {
        var pasta = Path.Combine(raizDados, "recuperacao");
        if (!Directory.Exists(pasta)) return 0;
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(pasta, "*.orfao-*.jsonl"))
        {
            try
            {
                if (agoraUtc - File.GetLastWriteTimeUtc(f) <= IdadeOrfao) continue;
                File.Delete(f);
                n++;
            }
            catch (Exception e) when (Falha(e)) { }
        }
        return n;
    }

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
        alvo = (pacote, sha256Base);
        try
        {
            var d = Diario.Iniciar(raizDados, pacote, sha256Base);
            try { foreach (var r in pendentes) d.Registrar(r); }
            catch { d.Dispose(); throw; }
            diario = d;
            UltimoErro = null;
            return true;
        }
        catch (Exception e) when (Falha(e)) { UltimoErro = MensagensDeErro.ParaUsuario(e); return false; }
    }

    /// <summary>Acrescenta o registro (descarregado no disco). <c>false</c> se o diário não está aberto ou a escrita falhou. Com
    /// <paramref name="pendentes"/> (todos os registros que o pacote ainda não tem, este incluído), um diário que não pôde começar (ou que
    /// falhou numa escrita e foi fechado) é começado de novo com eles (T10-C2): nada fica só na memória por causa de uma falha passageira.</summary>
    public bool Registrar(RegistroHistorico r, IReadOnlyList<RegistroHistorico>? pendentes = null)
    {
        if (diario is null && pendentes is not null && alvo is { } a) return Iniciar(a.Pacote, a.Sha, pendentes);
        if (diario is not { } d)
        {
            UltimoErro ??= "o diário de recuperação não está aberto";
            return false;
        }
        try { d.Registrar(r); return true; }
        catch (Exception e) when (Falha(e))
        {
            UltimoErro = MensagensDeErro.ParaUsuario(e);
            // Uma escrita pela metade deixaria o resto do arquivo ilegível: fecha (guardando) e o próximo registro recomeça com tudo.
            if (pendentes is not null)
            {
                diario = null;
                try { d.Dispose(); } catch (Exception e2) when (Falha(e2)) { }
            }
            return false;
        }
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
        // Se algo abaixo falhar, o próximo registro tenta começar o diário do arquivo gravado, com o sha novo.
        alvo = continuar ? (caminhoAgora, sha256Novo) : null;
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
        catch (Exception e) when (Falha(e)) { velho?.Dispose(); UltimoErro = MensagensDeErro.ParaUsuario(e); return false; }
    }

    /// <summary>Fecha o diário: <paramref name="guardar"/> = true deixa o arquivo para uma recuperação (há alterações não gravadas);
    /// false o apaga (tudo está no pacote).</summary>
    public void Fechar(bool guardar)
    {
        var d = diario;
        diario = null;
        alvo = null;
        if (d is null) return;
        try { if (guardar) d.Dispose(); else d.Descartar(); }
        catch (Exception e) when (Falha(e)) { UltimoErro = MensagensDeErro.ParaUsuario(e); }
    }

    /// <summary>Fecha e guarda o diário do <paramref name="pacote"/> como órfão (Recarregar, recuperação recusada ou de outra versão);
    /// devolve o caminho do órfão, ou null se não havia diário (ou não deu para renomear: <see cref="UltimoErro"/>).</summary>
    public string? Arquivar(string pacote)
    {
        Fechar(guardar: true);
        try { return File.Exists(Diario.CaminhoDe(raizDados, pacote)) ? Diario.Arquivar(raizDados, pacote) : null; }
        catch (Exception e) when (Falha(e)) { UltimoErro = MensagensDeErro.ParaUsuario(e); return null; }
    }

    /// <summary>Fecha sem apagar: o que estiver no diário fica para uma recuperação.</summary>
    public void Dispose() => Fechar(guardar: true);

    static bool Falha(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException;
}

/// <summary>O que fazer, na abertura, com o diário encontrado para o pacote.</summary>
public enum AcaoRecuperacao { Nada, Perguntar, Arquivar }

/// <summary><see cref="AcaoRecuperacao.Perguntar"/>: <see cref="Comandos"/> registros novos para reaplicar; <see cref="AcaoRecuperacao.Arquivar"/>:
/// o <see cref="Motivo"/> (mostrado ao usuário junto com o nome do órfão; null = guardar em silêncio, nada se perdeu).</summary>
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
        // Queda entre a troca do arquivo e o recomeço do diário: o diário ficou com o sha antigo, mas cada registro dele (mesmo N, mesmo
        // conteúdo) já está no histórico do pacote. Nada se perdeu: guarda como órfão sem mensagem (Motivo null).
        if (!rec.DaMesmaVersao && TodosNoPacote(rec, historicoDoPacote)) return new(AcaoRecuperacao.Arquivar);
        if (!rec.DaMesmaVersao)
            return new(AcaoRecuperacao.Arquivar, Motivo: "Havia alterações não gravadas de outra versão deste arquivo (ele mudou depois delas); não foram aplicadas.");
        if (rec.Divergencias(historicoDoPacote).Count > 0)
            return new(AcaoRecuperacao.Arquivar, Motivo: "Havia alterações não gravadas que não batem com o histórico do arquivo; não foram aplicadas.");
        var ultimo = historicoDoPacote.Count == 0 ? 0 : historicoDoPacote.Max(r => r.N);
        var novos = rec.Registros.Count(r => r.N > ultimo);
        return novos == 0 ? new(AcaoRecuperacao.Nada) : new(AcaoRecuperacao.Perguntar, novos);
    }

    static bool TodosNoPacote(Recuperacao rec, IReadOnlyList<RegistroHistorico> historico)
    {
        var porN = historico.GroupBy(r => r.N).ToDictionary(g => g.Key, g => HistoricoJsonl.Linha(g.First()));
        return rec.Registros.All(r => porN.TryGetValue(r.N, out var l) && l == HistoricoJsonl.Linha(r));
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

    /// <summary>A pergunta de fechar com alterações que não foram gravadas. Em conflito o diário não volta a ser oferecido (o arquivo no disco
    /// é outra versão: na abertura ele vira órfão), então o texto não promete isso e indica Salvar como cópia.</summary>
    public static string FecharSemGravar(string motivo, bool conflito) => conflito
        ? $"Não foi possível gravar as alterações no arquivo: {motivo.TrimEnd('.')}. Salvar como cópia grava as suas alterações ao lado, sem mexer "
          + "no arquivo. Se fechar sem salvar, elas ficam guardadas na pasta de recuperação dos dados do app, mas não serão oferecidas de novo ao "
          + "abrir o arquivo (ele agora é outra versão)."
        : $"Não foi possível gravar as alterações no arquivo: {motivo.TrimEnd('.')}. Se fechar sem salvar, elas ficam guardadas na recuperação: "
          + "ao abrir este arquivo de novo, o app oferece recuperá-las.";

    /// <summary>Fechar com um campo em edição no A4 cujo texto a conferência recusou: o texto não entra em lugar nenhum se fechar.</summary>
    public static string FecharSemOTexto(string motivo) =>
        $"O texto em edição não pôde ser aplicado: {motivo.TrimEnd('.')}. Fechar mesmo assim?";

    /// <summary>Exportar com alterações que o arquivo não tem e não pode receber agora (somente leitura ou conflito): o PDF e o recibo
    /// (sha do pacote) não bateriam.</summary>
    public static string ExportarSemGravar(bool conflito) => conflito
        ? "O arquivo mudou no disco e as alterações desta sessão não estão nele: o PDF não corresponderia ao pacote do recibo. Use Salvar como cópia e exporte depois."
        : "O relatório está somente para leitura e as alterações desta sessão não estão no arquivo: o PDF não corresponderia ao pacote do recibo. Use Salvar como para gravá-las num arquivo e exporte depois.";

    /// <summary>"Salvar como" escolhendo o próprio arquivo aberto: com a sessão somente leitura (esquema mais novo, trava de outro, sem
    /// trava, trava perdida) ou em conflito, gravar por cima furaria a proteção; devolve o motivo da recusa (null = pode seguir).</summary>
    public static string? RecusaSalvarComo(string destino, string atual, bool somenteLeitura, bool conflito, string? motivoSomenteLeitura)
    {
        if (!string.Equals(Path.GetFullPath(destino), Path.GetFullPath(atual), StringComparison.OrdinalIgnoreCase)) return null;
        if (conflito)
            return "O arquivo mudou no disco depois de aberto: gravar por cima dele apagaria a versão que está lá. Use Salvar como cópia ou escolha outro nome.";
        if (somenteLeitura)
            return (motivoSomenteLeitura ?? "O relatório está aberto somente para leitura").TrimEnd('.')
                   + ". Não é possível gravar por cima deste arquivo: escolha outro nome em Salvar como.";
        return null;
    }

    public const string TravaPerdida =
        "A trava de edição deste arquivo foi perdida (apagada ou retomada por outro programa): somente leitura. "
        + "As alterações que ainda não estavam no arquivo continuam no diário de recuperação; use Salvar como para guardá-las.";
}
