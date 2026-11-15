using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Gravação sem perder trabalho (Tarefa 10; desenho §5.1 e §8.6): salvamento automático depois de
/// <see cref="Ajustes.IntervaloAutosaveS"/> sem comando, ao trocar de módulo ou de arquivo e ao fechar; batimento da trava a cada
/// <see cref="Trava.Batimento"/> (perdida ⇒ somente leitura); recuperação do diário na abertura; Conflito com "Salvar como cópia" e
/// "Recarregar". Toda gravação passa pela fila de comandos (<see cref="NaFilaAsync(Func{Task})"/>) e pelo <see cref="GravadorPacote"/>.</summary>
public sealed partial class MainWindow
{
    DispatcherQueueTimer autosave = null!, batimento = null!;

    // ---------- acessores do roteiro ----------
    internal InfoBar InfoConflito => R.AvisoConflito;
    internal InfoBar InfoSomenteLeitura => R.AvisoSomenteLeitura;

    /// <summary>Muda o intervalo do salvamento automático (15, 30, 60 ou 120 s) e o guarda nos ajustes.</summary>
    internal void DefinirIntervaloAutosave(int segundos)
    {
        if (!Ajustes.Intervalos.Contains(segundos)) throw new ArgumentOutOfRangeException(nameof(segundos), segundos, "Intervalo não permitido.");
        ajustes.IntervaloAutosaveS = segundos;
        SalvarAjustes();
    }

    void IniciarGravacao()
    {
        var fila = DispatcherQueue.GetForCurrentThread();
        autosave = fila.CreateTimer();
        autosave.IsRepeating = false;
        autosave.Tick += async (_, _) => await AutosalvarAsync();
        batimento = fila.CreateTimer();
        batimento.IsRepeating = true;
        batimento.Interval = Trava.Batimento;
        batimento.Tick += async (_, _) => await BaterAsync();
        R.BotaoSalvarCopia.Click += async (_, _) => await SalvarCopiaDeConflitoAsync();
        R.BotaoRecarregar.Click += async (_, _) => await RecarregarAsync();
        // T10-C1: órfãos do diário com mais de 30 dias saem na abertura do app (fora da thread da interface; falha só não limpa).
        _ = Task.Run(() =>
        {
            try { DiarioDaSessao.LimparOrfaosAntigos(Pmvni.Composicao.Web.PastaDados.Raiz, DateTime.UtcNow); }
            catch (Exception e) { System.Diagnostics.Debug.WriteLine("[órfãos] " + e.Message); }
        });
    }

    void PararTemporizadores()
    {
        autosave.Stop();
        batimento.Stop();
    }

    // ---------- sessão aberta ----------

    void LigarSessao(SessaoEdicao s)
    {
        s.Mudou += AtualizarBarra;
        s.Revisao.Registrado += AoRegistrar;
        s.FalhaNoDiario += MostrarFalhaNoDiario;
        s.DiarioVoltou += AoVoltarODiario;
        s.TravaMudou += AoMudarATrava;
        if (s.TemTrava) batimento.Start();
    }

    void DesligarSessao(SessaoEdicao s)
    {
        s.Mudou -= AtualizarBarra;
        s.Revisao.Registrado -= AoRegistrar;
        s.FalhaNoDiario -= MostrarFalhaNoDiario;
        s.DiarioVoltou -= AoVoltarODiario;
        s.TravaMudou -= AoMudarATrava;
        PararTemporizadores();
    }

    /// <summary>Fecha o relatório sem gravar nada (quem chamou já decidiu): solta a trava; o diário fica se houver alterações não gravadas.</summary>
    async Task DescartarSessaoAsync(SessaoEdicao s)
    {
        if (ReferenceEquals(sessao, s)) sessao = null;
        DesligarSessao(s);
        Reiniciar();
        await s.DisposeAsync();
    }

    /// <summary>Fecha o relatório aberto. Alterações não gravadas: grava sozinho; só pergunta se a gravação falhar (ou não puder ser
    /// feita: conflito, somente leitura): "Salvar como…", "Fechar sem salvar (a recuperação fica guardada)" ou "Cancelar". Devolve false
    /// se o relatório continua aberto.</summary>
    internal async Task<bool> FecharSessaoAsync()
    {
        if (sessao is not { } s) return true;
        if (s.TemAlteracoesNaoGravadas)
        {
            string motivo;
            if (s.Estado == EstadoGravacao.Conflito) motivo = "o arquivo mudou no disco depois de aberto";
            else if (s.SomenteLeitura) motivo = s.MotivoSomenteLeitura ?? "o relatório está aberto somente para leitura";
            else
            {
                motivo = "";
                try { await s.GravarAsync(); }
                catch (ConflitoNoDiscoException) { motivo = "o arquivo mudou no disco depois de aberto"; MostrarConflito(); }
                catch (Exception e) { motivo = e.Message; }
            }
            if (s.TemAlteracoesNaoGravadas)
            {
                // Em conflito o diário não será oferecido de novo (o arquivo é outra versão): a pergunta diz isso e oferece a cópia.
                var conflito = s.Estado == EstadoGravacao.Conflito;
                switch (await Perguntas.Atual.FecharSemGravarAsync(motivo.Length > 0 ? motivo : "erro desconhecido", conflito))
                {
                    case RespostaFechar.Cancelar:
                        return false;
                    case RespostaFechar.SalvarComo:
                        await SalvarComoAsync();
                        if (!ReferenceEquals(sessao, s) || s.TemAlteracoesNaoGravadas) return false;
                        break;
                    case RespostaFechar.SalvarCopia:
                        if (!await SalvarCopiaDeConflitoAsync() || !ReferenceEquals(sessao, s) || s.TemAlteracoesNaoGravadas) return false;
                        break;
                    case RespostaFechar.FecharSemSalvar:
                        if (!s.DiarioAtivo)
                            MostrarMensagem(InfoBarSeverity.Warning, "Fechado sem salvar", "O diário de recuperação não estava sendo gravado: as alterações não gravadas não puderam ser guardadas.");
                        break;
                }
            }
        }
        await DescartarSessaoAsync(s);
        return true;
    }

    // ---------- salvamento automático ----------

    void AoRegistrar(RegistroHistorico _) => ReiniciarAutosave();

    /// <summary>Conta de novo o intervalo sem comando (cada comando recomeça a contagem).</summary>
    void ReiniciarAutosave()
    {
        autosave.Stop();
        if (sessao is not { SomenteLeitura: false }) return;
        autosave.Interval = TimeSpan.FromSeconds(ajustes.IntervaloAutosaveS);
        autosave.Start();
    }

    async Task AutosalvarAsync()
    {
        if (sessao is not { Estado: EstadoGravacao.NaoSalvo, SomenteLeitura: false }) return;
        var ok = await GravarAgoraAsync();
        // Falhou (arquivo preso pelo OneDrive, disco cheio…): tenta de novo no próximo intervalo; em conflito, espera o usuário decidir.
        if (!ok && sessao is { Estado: EstadoGravacao.NaoSalvo }) ReiniciarAutosave();
    }

    /// <summary>Grava agora, na fila de comandos. Devolve true se o arquivo ficou com tudo; falha ⇒ InfoBar de erro (o arquivo antigo fica
    /// intacto e o diário com as alterações); conflito ⇒ InfoBar "Conflito" com Salvar como cópia e Recarregar.</summary>
    internal Task<bool> GravarAgoraAsync() => NaFilaAsync(GravarNaVezAsync);

    async Task<bool> GravarNaVezAsync()
    {
        if (sessao is not { } s) return false;
        if (s.SomenteLeitura)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Não foi possível salvar", (s.MotivoSomenteLeitura ?? "O relatório está aberto somente para leitura.") + " Use Salvar como.");
            return false;
        }
        try
        {
            await s.GravarAsync();
            if (ReferenceEquals(sessao, s)) R.AvisoConflito.IsOpen = false;
            return s.Estado == EstadoGravacao.Salvo;
        }
        catch (ConflitoNoDiscoException) { MostrarConflito(); return false; }
        catch (Exception e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível salvar", e.Message.TrimEnd('.') + ". O arquivo no disco não foi alterado"
                + (s.DiarioAtivo ? "; as alterações continuam guardadas no diário de recuperação e o salvamento será tentado de novo." : "."));
            return false;
        }
    }

    // ---------- trava ----------

    async Task BaterAsync()
    {
        if (sessao is not { TemTrava: true } s) { batimento.Stop(); return; }
        await s.BaterAsync(); // perdida ⇒ TravaMudou
    }

    void AoMudarATrava()
    {
        if (sessao is not { } s || fechada) return;
        if (s.TemTrava) batimento.Start(); else batimento.Stop();
        if (s.SomenteLeitura)
        {
            autosave.Stop();
            MostrarSomenteLeitura(s);
        }
        else R.AvisoSomenteLeitura.IsOpen = false;
        AtualizarPropriedades();
        AtualizarComandos();
        // A página deixa de aceitar edição no A4 (ou volta a aceitar). Seguro sem await: EnviarEstadoAsync registra e engole o próprio erro.
        _ = NaFilaAsync(() => sessao?.EnviarEstadoAsync() ?? Task.CompletedTask);
    }

    void MostrarSomenteLeitura(SessaoEdicao s)
    {
        if (fechada) return;
        R.AvisoSomenteLeitura.Message = s.MotivoSomenteLeitura ?? "O relatório não pode ser alterado.";
        R.AvisoSomenteLeitura.IsOpen = true;
    }

    // ---------- diário ----------

    void MostrarFalhaNoDiario(string motivo)
    {
        if (fechada) return;
        R.AvisoDiario.Message = motivo.TrimEnd('.') + ". O app tenta gravar o relatório agora e recomeçar o diário no próximo comando; até lá, uma queda do app pode perder as alterações não gravadas.";
        R.AvisoDiario.IsOpen = true;
        // T10-C2: o que só está na memória vai já para o arquivo. Só com alterações pendentes (uma falha logo depois de gravar não grava de
        // novo, então não há laço). Fora da vez atual da fila: a falha chega no meio de um comando (Registrado) ou de uma gravação, e a
        // gravação tem de esperar a vez deles; o fluxo do contexto (a vez da fila, AsyncLocal) não segue para o callback.
        if (sessao is { Estado: EstadoGravacao.NaoSalvo, SomenteLeitura: false, TemAlteracoesNaoGravadas: true })
        {
            using (ExecutionContext.SuppressFlow())
                // Seguro sem await: GravarNaVezAsync trata toda exceção (InfoBar) e devolve false.
                DispatcherQueue.TryEnqueue(() => _ = GravarAgoraAsync());
        }
    }

    void AoVoltarODiario() { if (!fechada) R.AvisoDiario.IsOpen = false; }

    /// <summary>Diário deixado para o arquivo (queda do app): da mesma versão ⇒ pergunta (Recuperar reaplica, "Não salvo"; Descartar guarda o
    /// diário como órfão); de outra versão ou divergente ⇒ guarda como órfão sem aplicar. Devolve o aviso a mostrar depois da abertura.</summary>
    async Task<string?> RecuperarSeHouverAsync(SessaoEdicao s)
    {
        var rec = s.LerRecuperacao();
        var d = RecuperacaoAbertura.Decidir(rec, s.Revisao.HistoricoCompleto);
        string Guardado(string? orfao) => orfao is null ? "" : $" Ficaram guardadas em {Path.GetFileName(orfao)} (pasta recuperacao dos dados do app).";
        switch (d.Acao)
        {
            case AcaoRecuperacao.Arquivar:
                return d.Motivo + Guardado(s.ArquivarDiario());
            case AcaoRecuperacao.Perguntar:
                if (await Perguntas.Atual.RecuperarAsync(rec!.UltimoUtc, d.Comandos) == RespostaRecuperacao.Recuperar)
                {
                    try
                    {
                        s.Recuperar(rec);
                        return d.Comandos == 1 ? "1 alteração recuperada (ainda não gravada no arquivo)." : $"{d.Comandos} alterações recuperadas (ainda não gravadas no arquivo).";
                    }
                    catch (InvalidOperationException e) { return $"Não foi possível recuperar as alterações ({e.Message.TrimEnd('.')})." + Guardado(s.ArquivarDiario()); }
                }
                return "As alterações não gravadas foram descartadas." + Guardado(s.ArquivarDiario());
            default:
                return null;
        }
    }

    // ---------- conflito ----------

    void MostrarConflito()
    {
        if (fechada) return;
        R.AvisoConflito.Message = "Outro programa (ou o OneDrive) alterou o relatório depois que ele foi aberto; nada foi sobrescrito. "
            + "Salve as suas alterações numa cópia ao lado do arquivo ou recarregue o arquivo do disco (as alterações desta sessão ficam guardadas na recuperação).";
        R.AvisoConflito.IsOpen = true;
    }

    /// <summary>"Salvar como cópia": grava as alterações em "&lt;nome&gt; (cópia AAAA-MM-DD HH-MM).pmvrel" ao lado (sem seletor, sem
    /// sobrescrever) e passa a editar a cópia; o original fica como o outro programa o deixou.</summary>
    internal Task<bool> SalvarCopiaDeConflitoAsync() => NaFilaAsync(async () =>
    {
        if (sessao is not { } s) return false;
        var destino = NomesArquivo.CopiaDeConflito(s.Caminho, DateTime.Now);
        try
        {
            await s.GravarComoAsync(destino);
            RegistrarRecente(s.Caminho);
            R.AvisoConflito.IsOpen = false;
            AtualizarBarra();
            MostrarMensagem(InfoBarSeverity.Success, "Cópia salva",
                $"As suas alterações foram gravadas em {Path.GetFileName(s.Caminho)}, ao lado do original (que não foi alterado). O relatório aberto agora é a cópia.");
            return true;
        }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível salvar a cópia", e.Message); return false; }
    });

    /// <summary>"Recarregar" (confirma): descarta a sessão e abre o arquivo como está no disco; o diário com as alterações desta sessão é
    /// guardado como órfão, nunca apagado.</summary>
    internal Task<bool> RecarregarAsync() => NaFilaAsync(async () =>
    {
        if (sessao is not { } s) return false;
        if (s.TemAlteracoesNaoGravadas && !s.DiarioAtivo)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Recarregar não foi feito",
                "As alterações desta sessão não estão no diário de recuperação e se perderiam. Use Salvar como cópia antes de recarregar.");
            return false;
        }
        if (!await Perguntas.Atual.ConfirmarAsync("Recarregar do disco?",
                "O relatório volta a ser o que está gravado no arquivo agora. As alterações desta sessão que não chegaram ao arquivo saem da tela, "
                + "mas ficam guardadas na pasta de recuperação.", "Recarregar", "Cancelar"))
            return false;
        var caminho = s.Caminho;
        var orfao = s.ArquivarDiario();
        await DescartarSessaoAsync(s);
        if (!await AbrirPacoteAsync(caminho)) return false;
        MostrarMensagem(InfoBarSeverity.Informational, "Recarregado do disco", orfao is null
            ? "O relatório é o que está gravado no arquivo."
            : $"O relatório é o que está gravado no arquivo. As alterações desta sessão ficaram guardadas em {Path.GetFileName(orfao)} (pasta recuperacao dos dados do app).");
        return true;
    });

    // ---------- roteiro ----------

    /// <summary>Só o roteiro: o app "cai" sem gravar — solta o pacote, a trava e os temporizadores; o diário fica no disco.</summary>
    internal void SimularQuedaParaRoteiro()
    {
        if (sessao is not { } s) return;
        sessao = null;
        DesligarSessao(s);
        s.SimularQueda();
        Reiniciar();
    }
}
