using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Ajustes › Inteligência artificial (Tarefa 19; desenho §12.8 e §12.9): um cartão por agente local (ChatGPT/Codex e Claude/análise pro) com o
/// programa, a situação ("Testar" só roda comandos locais, nunca pede nada ao modelo), o acesso (conta do programa ou chave de API guardada pelo Windows com
/// DPAPI, nunca no <c>ajustes.json</c>) e o aviso. O editor só inicia o agente do computador; nenhum botão "Analisar" mora aqui (Tarefa 20). Todo comando
/// entra na fila de comandos; ler as peças do disco e rodar os programas de diagnóstico não prende a thread da janela (<see cref="Task.Run(Func{Task})"/> e processos).</summary>
public sealed partial class MainWindow
{
    static readonly Pecas PecasNaoLidas = new(null, null, null, false, null, null, null, false, null, true);

    Pecas pecasAgentes = PecasNaoLidas;
    Task tarefaDosAgentes = Task.CompletedTask;
    readonly Dictionary<Provedor, string> situacaoDosAgentes = new();
    readonly Dictionary<Provedor, IReadOnlyList<string>> avisosDosAgentes = new();
    readonly Dictionary<Provedor, string> mensagensDosAgentes = new();
    readonly HashSet<Provedor> testandoAgentes = new();
    readonly Dictionary<Provedor, Task<SituacaoProvedor>> testesEmCurso = new();
    int sequenciaPecas, sequenciaPecasAplicada;
    InfoLocalAgentes infoLocal = InfoLocalAgentes.Vazia;
    readonly AcompanhamentoPlugins acompanhamentoPlugins = new();
    readonly Dictionary<Provedor, string> notasDasContas = new();
    readonly HashSet<Provedor> testarAoVoltar = new();

    /// <summary>O que a página de IA mostra além das peças (correções 2026-11-15): a conta lida neste computador (nunca o token) e os plugins do kit.</summary>
    internal sealed record InfoLocalAgentes(ContaLocal ContaChatGpt, ContaLocal ContaClaude, PluginInstalado? AnaliseNoCodex, PluginInstalado? AnaliseNoClaude,
        PluginInstalado? AnaliseProNoCodex)
    {
        public static readonly InfoLocalAgentes Vazia = new(ContaLocal.Nenhuma, ContaLocal.Nenhuma, null, null, null);

        public ContaLocal Conta(Provedor p) => p == Provedor.ChatGpt ? ContaChatGpt : ContaClaude;

        public static InfoLocalAgentes Ler(OpcoesLocalizacao o) => new(ContasLocais.Ler(Provedor.ChatGpt, o), ContasLocais.Ler(Provedor.Claude, o),
            PluginsInstalados.NoCodex(o.CodexHome, PluginsInstalados.Analise), PluginsInstalados.NoClaude(o.PastaClaude, PluginsInstalados.Analise),
            PluginsInstalados.NoCodex(o.CodexHome, PluginsInstalados.AnalisePro));
    }

    // ---------- acessores do roteiro ----------

    /// <summary>De onde ler as peças (Codex, Claude Code, Python, kit). O padrão é o computador de verdade; o roteiro troca pelo agente falso.</summary>
    internal Func<OpcoesLocalizacao> OpcoesAgentes { get; set; } = null!;

    /// <summary>As peças da última leitura (ao abrir Ajustes, ao Testar, ao escolher programa e ao trocar a pasta do kit).</summary>
    internal Pecas PecasAgentes => pecasAgentes;

    /// <summary>A conta e os plugins da última leitura (o roteiro confere).</summary>
    internal InfoLocalAgentes InfoLocalDosAgentes => infoLocal;

    /// <summary>A página de IA está à vista (Ajustes aberto nessa categoria): ler as peças ali também testa sozinho e registra as versões dos plugins.</summary>
    bool IaVisivel => PaginaAjustesVisivel && PaginaDeAjustes.CategoriaAtual == CategoriasAjustes.Ia;

    internal CartaoAgente CartaoDoAgente(Provedor p) => p == Provedor.ChatGpt ? PaginaDeAjustes.CartaoChatGpt : PaginaDeAjustes.CartaoClaude;

    /// <summary>A linha de situação do cartão (a do último "Testar", ou "Ainda não testado…").</summary>
    internal string LinhaSituacaoAgente(Provedor p) => situacaoDosAgentes.TryGetValue(p, out var s) ? s : TextosAgentes.NaoTestado;

    internal IReadOnlyList<string> AvisosDoAgente(Provedor p) => avisosDosAgentes.TryGetValue(p, out var a) ? a : [];

    /// <summary>A última recusa mostrada no cartão (programa inválido, chave vazia, API do Claude…); null se não há.</summary>
    internal string? MensagemDoAgente(Provedor p) => mensagensDosAgentes.TryGetValue(p, out var m) ? m : null;

    internal ModoAcesso ModoDoAgente(Provedor p) => ajustes.Agentes[p].Modo;

    internal string? ProgramaEscolhidoDoAgente(Provedor p) => ajustes.Agentes[p].Executavel;

    internal bool AvisoAceitoDoAgente(Provedor p) => ajustes.Agentes[p].AvisoAceito;

    /// <summary>Espera a leitura das peças que a abertura de Ajustes (ou a troca da pasta do kit) começou.</summary>
    internal Task AguardarAgentesAsync() => tarefaDosAgentes;

    /// <summary>Entrar/Sair (correções 2026-11-15): abre o login (ou o logout) do programa numa janela de console própria (o login é interativo e abre o
    /// navegador). Ao voltar ao editor, o cartão relê a conta e testa de novo.</summary>
    bool AbrirLogin(string? exe, string nome, string argumento)
    {
        try
        {
            var programa = string.IsNullOrEmpty(exe) ? nome : "\"" + exe + "\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/k " + programa + " " + argumento) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Entrar", "Não foi possível abrir o login: " + ex.Message);
            return false;
        }
    }

    /// <summary>O link Entrar/Sair do alto do cartão: sem sessão, abre o login; com sessão, pergunta e abre o logout oficial do programa.</summary>
    async Task EntrarOuSairAsync(Provedor p)
    {
        var sair = infoLocal.Conta(p).Conectado;
        if (sair && !await Perguntas.Atual.ConfirmarAsync(TextosAgentes.PerguntaSair(p), TextosAgentes.ExplicacaoSair, "Sair", "Cancelar")) return;
        var (exe, nome) = p == Provedor.ChatGpt ? (pecasAgentes.Codex, "codex") : (pecasAgentes.Claude, "claude");
        var argumento = p == Provedor.ChatGpt ? (sair ? "logout" : "login") : (sair ? "/logout" : "/login");
        if (!AbrirLogin(exe, nome, argumento)) return;
        notasDasContas[p] = TextosAgentes.LoginAberto;
        testarAoVoltar.Add(p);
        MostrarAgentes();
    }

    void IniciarAgentes()
    {
        OpcoesAgentes = () => OpcoesLocalizacao.DoAmbiente(ajustes.PastaKitEfetiva, ajustes.Agentes[Provedor.ChatGpt].Executavel, ajustes.Agentes[Provedor.Claude].Executavel);
        // A página de IA se consulta sozinha ao ser vista (sem clicar em Testar); ao voltar de um Entrar/Sair, relê a conta e testa aquele agente de novo.
        PaginaDeAjustes.CategoriaMostrada += tag => { if (tag == CategoriasAjustes.Ia && PaginaAjustesVisivel) AtualizarAgentes(); };
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated || !IaVisivel || testarAoVoltar.Count == 0) return;
            foreach (var p in testarAoVoltar) { situacaoDosAgentes.Remove(p); avisosDosAgentes.Remove(p); notasDasContas.Remove(p); }
            testarAoVoltar.Clear();
            AtualizarAgentes();
        };
        foreach (var provedor in Enum.GetValues<Provedor>())
        {
            var p = provedor;
            var c = CartaoDoAgente(p);
            c.Configurar(p);
            c.BotaoTestar.Click += async (_, _) => await TestarAgenteAsync(p);
            c.BotaoConta.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => EntrarOuSairAsync(p)));
            c.BotaoEscolherPrograma.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(async () =>
            {
                if (await Perguntas.Atual.AbrirArquivoAsync(".exe") is { } caminho) await EscolherProgramaAsync(p, caminho);
            }));
            c.BotaoProgramaPadrao.Click += async (_, _) => await EscolherProgramaAsync(p, null);
            c.OpcaoConta.Click += async (_, _) => await DefinirModoAsync(p, ModoAcesso.Conta);
            c.OpcaoChave.Click += async (_, _) => await DefinirModoAsync(p, ModoAcesso.Chave);
            c.BotaoGuardarChave.Click += async (_, _) => await GuardarChaveAsync(p, c.CaixaChave.Password);
            c.BotaoRemoverChave.Click += async (_, _) => await RemoverChaveAsync(p);
            c.BotaoReverAviso.Click += async (_, _) => await ReverAvisoAsync(p);
        }
        MostrarAgentes();
    }

    /// <summary>Lê de novo as peças, a conta e os plugins do disco (fora da thread da janela) e atualiza os cartões. Ao abrir Ajustes, ao mostrar a página
    /// de IA e ao trocar a pasta do kit. Com a página de IA à vista, também registra as versões dos plugins vistas e testa sozinho o agente que ainda não foi
    /// testado nesta sessão (só comandos locais; Testar continua para conferir de novo).</summary>
    void AtualizarAgentes() => tarefaDosAgentes = RelerAgentesAsync(IaVisivel);

    async Task RelerAgentesAsync(bool consultarSozinho)
    {
        try
        {
            await LerPecasAsync();
            if (consultarSozinho) RegistrarPluginsVistos();
            MostrarAgentes();
            if (!consultarSozinho) return;
            var testes = Enum.GetValues<Provedor>().Where(p => !situacaoDosAgentes.ContainsKey(p) || testesEmCurso.ContainsKey(p)).Select(TestarAgenteAsync).ToList();
            await Task.WhenAll(testes);
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[agentes] " + e.Message); }
    }

    /// <summary>Compara as versões instaladas dos plugins com as da última vez (ajustes.json) e guarda as de agora; a mudança fica avisada no cartão até fechar.</summary>
    void RegistrarPluginsVistos()
    {
        var mudou = acompanhamentoPlugins.Registrar(ajustes.PluginsVistos, AcompanhamentoPlugins.Chave(Provedor.ChatGpt, PluginsInstalados.Analise), infoLocal.AnaliseNoCodex?.Versao);
        mudou |= acompanhamentoPlugins.Registrar(ajustes.PluginsVistos, AcompanhamentoPlugins.Chave(Provedor.Claude, PluginsInstalados.Analise), infoLocal.AnaliseNoClaude?.Versao);
        mudou |= acompanhamentoPlugins.Registrar(ajustes.PluginsVistos, AcompanhamentoPlugins.Chave(Provedor.Claude, PluginsInstalados.AnalisePro), infoLocal.AnaliseProNoCodex?.Versao);
        if (mudou) SalvarAjustes();
    }

    /// <summary>Lê as peças fora da thread da janela. Leituras que terminam fora de ordem não se sobrepõem: só vale a mais nova (número de sequência);
    /// quem chamou recebe sempre a sua. O modo "chave" do Claude guardado antes de o kit passar a proibir a API vira "conta".</summary>
    async Task<Pecas> LerPecasAsync()
    {
        var minha = ++sequenciaPecas;
        var opcoes = OpcoesAgentes();
        var (pecas, info) = await Task.Run(() => (Localizacao.Ler(opcoes), InfoLocalAgentes.Ler(opcoes)));
        if (minha > sequenciaPecasAplicada)
        {
            sequenciaPecasAplicada = minha;
            pecasAgentes = pecas;
            infoLocal = info;
            if (ajustes.NormalizarModos(pecas.ApiPermitidaNoKit)) SalvarAjustes();
        }
        return pecas;
    }

    // ---------- comandos ----------

    /// <summary>"Testar": só comandos locais do programa (<c>--version</c>, login/auth status, <c>python --version</c>), 15 s cada, sem pedir nada ao modelo.
    /// O resultado vira a linha de situação do cartão. É só leitura e NÃO entra na fila de comandos (um programa que não responde não prende Salvar nem
    /// Exportar): um teste por provedor de cada vez (chamar de novo enquanto roda devolve o mesmo teste) e o botão desabilita já no clique.</summary>
    internal Task<SituacaoProvedor> TestarAgenteAsync(Provedor p)
    {
        if (testesEmCurso.TryGetValue(p, out var emCurso)) return emCurso;
        var tarefa = ExecutarTesteAsync(p);   // roda até o primeiro await aqui mesmo: já marca "Testando…" e desabilita o botão
        if (!tarefa.IsCompleted) testesEmCurso[p] = tarefa;
        return tarefa;
    }

    async Task<SituacaoProvedor> ExecutarTesteAsync(Provedor p)
    {
        testandoAgentes.Add(p);
        mensagensDosAgentes.Remove(p);
        situacaoDosAgentes[p] = TextosAgentes.Testando;
        avisosDosAgentes[p] = [];
        MostrarAgentes();
        SituacaoProvedor resultado;
        try
        {
            var pecas = await LerPecasAsync();
            resultado = await Diagnostico.TestarAsync(p, pecas, ajustes.Agentes[p].Modo, CofreChaves.Existe(PastaDados.Raiz, p), CancellationToken.None);
        }
        catch (Exception e) { resultado = new SituacaoProvedor("Não foi possível testar: " + Erro(e), []); }
        finally
        {
            testandoAgentes.Remove(p);
            testesEmCurso.Remove(p);
        }
        situacaoDosAgentes[p] = resultado.Linha;
        avisosDosAgentes[p] = resultado.Avisos;
        MostrarAgentes();
        return resultado;
    }

    /// <summary>Guarda a chave de API protegida pelo Windows (DPAPI, só para este usuário) e limpa a caixa. Nunca vai para <c>ajustes.json</c>, log ou registro.</summary>
    internal Task GuardarChaveAsync(Provedor p, string chave) => NaFilaAsync(() => TentarAsync(() =>
    {
        var caixa = CartaoDoAgente(p).CaixaChave;
        try
        {
            if (string.IsNullOrWhiteSpace(chave)) mensagensDosAgentes[p] = TextosAgentes.ChaveVazia;
            else
            {
                try
                {
                    CofreChaves.Guardar(PastaDados.Raiz, p, chave);
                    mensagensDosAgentes.Remove(p);
                }
                catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
                {
                    mensagensDosAgentes[p] = "Não foi possível guardar a chave: " + Erro(e);
                }
            }
        }
        finally { caixa.Password = ""; }    // a chave nunca fica na caixa, qualquer que seja a falha
        MostrarAgentes();
        return Task.CompletedTask;
    }));

    /// <summary>Remove a chave guardada, depois de perguntar.</summary>
    internal Task RemoverChaveAsync(Provedor p) => NaFilaAsync(() => TentarAsync(async () =>
    {
        if (!CofreChaves.Existe(PastaDados.Raiz, p)) return;
        if (await Perguntas.Atual.ConfirmarAsync(TextosAgentes.PerguntaRemover(p), TextosAgentes.ExplicacaoRemover, "Remover", "Cancelar"))
        {
            CofreChaves.Remover(PastaDados.Raiz, p);
            mensagensDosAgentes.Remove(p);
        }
        MostrarAgentes();
    }));

    /// <summary>Usar a conta do programa ou a chave de API. O Claude só aceita a chave se o análise pro instalado do kit não a proibir; senão nada muda.</summary>
    internal Task DefinirModoAsync(Provedor p, ModoAcesso m) => NaFilaAsync(() => TentarAsync(async () =>
    {
        if (p == Provedor.Claude && m == ModoAcesso.Chave && !(await LerPecasAsync()).ApiPermitidaNoKit)
            mensagensDosAgentes[p] = TextosAgentes.ClaudeSoConta;
        else
        {
            mensagensDosAgentes.Remove(p);
            if (ajustes.Agentes[p].Modo != m)
            {
                ajustes.Agentes[p].Modo = m;
                SalvarAjustes();
            }
        }
        MostrarAgentes();
    }));

    /// <summary>"Escolher programa…" (o <c>codex.exe</c> do ChatGPT; o <c>python.exe</c> do Claude) ou, com null, "Usar o padrão". Um caminho que não termina em
    /// <c>.exe</c> ou não existe é recusado e nada muda.</summary>
    internal Task EscolherProgramaAsync(Provedor p, string? caminho) => NaFilaAsync(() => TentarAsync(async () =>
    {
        if (caminho is not null && !(caminho.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(caminho)))
        {
            mensagensDosAgentes[p] = TextosAgentes.ProgramaInvalido;
            MostrarAgentes();
            return;
        }
        mensagensDosAgentes.Remove(p);
        ajustes.Agentes[p].Executavel = caminho is null ? null : Path.GetFullPath(caminho);
        SalvarAjustes();
        situacaoDosAgentes.Remove(p);   // era do programa de antes
        avisosDosAgentes.Remove(p);
        await LerPecasAsync();
        MostrarAgentes();
    }));

    /// <summary>"Rever aviso": mostra o aviso do provedor; ao concordar, guarda a versão e a data (UTC) do aceite. true = concordou.</summary>
    internal async Task<bool> ReverAvisoAsync(Provedor p)
    {
        var concordou = false;
        await NaFilaAsync(() => TentarAsync(async () =>
        {
            concordou = await Perguntas.Atual.ConfirmarAsync($"Analisar com o {Provedores.Nome(p)}", TextosAgentes.Aviso(p), "Concordo", "Cancelar", destrutiva: false);
            if (!concordou) return;
            var a = ajustes.Agentes[p];
            a.AvisoVersao = Ajustes.VersaoAviso;
            a.AvisoEm = DateTimeOffset.UtcNow;
            SalvarAjustes();
            MostrarAgentes();
        }));
        return concordou;
    }

    // ---------- a tela ----------

    void MostrarAgentes()
    {
        if (fechada) return;
        foreach (var p in Enum.GetValues<Provedor>()) MostrarCartao(p);
    }

    void MostrarCartao(Provedor p)
    {
        var c = CartaoDoAgente(p);
        var a = ajustes.Agentes[p];
        var pecas = pecasAgentes;

        var conta = infoLocal.Conta(p);

        // Conta (lida neste computador) e Assinatura.
        c.BotaoConta.Content = TextosAgentes.AcaoDaConta(conta);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(c.BotaoConta, TextosAgentes.AcaoDaConta(conta) + " no " + TextosAgentes.NomeDoServico(p));
        Dicas.Aplicar(c.BotaoConta, TextosAgentes.AcaoDaConta(conta), conta.Conectado
            ? "Sai da conta do " + TextosAgentes.NomeDoServico(p) + " neste computador."
            : "Entra na sua conta do " + TextosAgentes.NomeDoServico(p) + " para o app poder usar a IA.");
        c.TextoNomeConta.Text = TextosAgentes.NomeDaConta(conta);
        c.FotoConta.DisplayName = conta.Conectado ? conta.Nome ?? conta.Email ?? "" : "";
        var email = TextosAgentes.EmailDaConta(conta);
        c.TextoEmailConta.Text = email;
        c.TextoEmailConta.Visibility = email.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var nota = notasDasContas.TryGetValue(p, out var n) ? n : conta.Conectado ? "" : TextosAgentes.SemSessao;
        c.TextoNotaConta.Text = nota;
        c.TextoNotaConta.Visibility = nota.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        c.TextoAssinatura.Text = TextosAgentes.Assinatura(conta);

        // Plugins do kit: versão, data e o aviso quando mudou desde a última vez.
        MostrarPlugin(c.TextoPluginAnalise, c.SeloPluginAnalise, c.TextoPluginAnaliseNovo, p, PluginsInstalados.Analise,
            p == Provedor.ChatGpt ? infoLocal.AnaliseNoCodex : infoLocal.AnaliseNoClaude, pecas.PluginAnaliseHabilitado);
        if (p == Provedor.Claude)
            MostrarPlugin(c.TextoPluginPro, c.SeloPluginPro, c.TextoPluginProNovo, p, PluginsInstalados.AnalisePro, infoLocal.AnaliseProNoCodex, pecas.PluginAnaliseHabilitado);

        var achado = p == Provedor.ChatGpt ? pecas.Codex is not null : pecas.Python is not null && pecas.Claude is not null;
        c.SeloPrograma.Definir(achado, achado ? TextosAgentes.Encontrado : TextosAgentes.NaoEncontrado);
        c.TextoPrograma.Text = p == Provedor.ChatGpt
            ? TextosAgentes.LinhaDoPrograma("Codex", pecas.Codex, a.Executavel)
            : TextosAgentes.LinhaDoPrograma("Python", pecas.Python, a.Executavel) + (pecas.PythonPeloLauncher ? " (pelo py.exe -3)" : "")
              + Environment.NewLine + "Claude Code: " + (pecas.Claude ?? "não encontrado");
        c.BotaoProgramaPadrao.IsEnabled = !string.IsNullOrWhiteSpace(a.Executavel);

        var mensagem = MensagemDoAgente(p);
        c.TextoMensagem.Text = mensagem ?? "";
        c.TextoMensagem.Visibility = mensagem is null ? Visibility.Collapsed : Visibility.Visible;

        c.TextoSituacao.Text = LinhaSituacaoAgente(p);
        var avisos = AvisosDoAgente(p);
        c.TextoAvisosDoTeste.Text = string.Join(Environment.NewLine, avisos);
        c.TextoAvisosDoTeste.Visibility = avisos.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        c.BotaoTestar.IsEnabled = !testandoAgentes.Contains(p);

        c.OpcaoConta.IsChecked = a.Modo == ModoAcesso.Conta;
        c.OpcaoChave.IsChecked = a.Modo == ModoAcesso.Chave;
        var apiProibida = p == Provedor.Claude && !pecas.ApiPermitidaNoKit;
        c.OpcaoChave.IsEnabled = !apiProibida;
        ToolTipService.SetToolTip(c.OpcaoChave, apiProibida ? TextosAgentes.ClaudeSoConta : null);
        c.TextoDicaChave.Text = apiProibida ? TextosAgentes.ClaudeSoConta : "";
        c.TextoDicaChave.Visibility = apiProibida ? Visibility.Visible : Visibility.Collapsed;
        c.PainelChave.Visibility = a.Modo == ModoAcesso.Chave && !apiProibida ? Visibility.Visible : Visibility.Collapsed;
        var guardada = CofreChaves.Existe(PastaDados.Raiz, p);
        c.TextoChave.Text = guardada ? TextosAgentes.ChaveGuardada : TextosAgentes.SemChave;
        c.SeloChave.Definir(guardada, guardada ? TextosAgentes.Configurada : TextosAgentes.NaoConfigurada);
        c.BotaoRemoverChave.IsEnabled = guardada;

        c.TextoAviso.Text = TextosAgentes.SituacaoDoAviso(a);
    }

    void MostrarPlugin(TextBlock descricao, SeloEstado selo, TextBlock novo, Provedor p, string plugin, PluginInstalado? instalado, bool habilitadoNoCodex)
    {
        descricao.Text = TextosAgentes.DescricaoPlugin(TextosAgentes.OndePlugin(p, plugin, habilitadoNoCodex), instalado);
        selo.Definir(instalado is not null, TextosAgentes.SeloPlugin(instalado));
        var anterior = acompanhamentoPlugins.Anterior(AcompanhamentoPlugins.Chave(p, plugin));
        novo.Text = anterior is null ? "" : TextosAgentes.AtualizadoDesde(anterior);
        novo.Visibility = anterior is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
