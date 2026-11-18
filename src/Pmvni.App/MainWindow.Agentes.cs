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
        PluginInstalado? AnaliseProNoCodex, PluginInstalado? AnaliseGoogleNoCodex = null)
    {
        public static readonly InfoLocalAgentes Vazia = new(ContaLocal.Nenhuma, ContaLocal.Nenhuma, null, null, null);

        /// <summary>A conta lida do disco. O Gemini não tem arquivo de conta que o editor leia: o login do Antigravity só é conferido pelo "Testar".</summary>
        public ContaLocal Conta(Provedor p) => p switch { Provedor.ChatGpt => ContaChatGpt, Provedor.Claude => ContaClaude, _ => ContaLocal.Nenhuma };

        public static InfoLocalAgentes Ler(OpcoesLocalizacao o) => new(ContasLocais.Ler(Provedor.ChatGpt, o), ContasLocais.Ler(Provedor.Claude, o),
            PluginsInstalados.NoCodex(o.CodexHome, PluginsInstalados.Analise), PluginsInstalados.NoClaude(o.PastaClaude, PluginsInstalados.Analise),
            PluginsInstalados.NoCodex(o.CodexHome, PluginsInstalados.AnalisePro), PluginsInstalados.NoCodex(o.CodexHome, PluginsInstalados.AnaliseGoogle));
    }

    /// <summary>O que o último "Testar" do Gemini disse do login do Antigravity (conta Google): true, false ou null (ainda não testado ou não foi possível saber).</summary>
    bool? contaGeminiConectada;
    Window? janelaLoginGemini;
    bool sessaoGeminiWebConectada;
    string? emailGeminiWeb;
    bool mostrandoAgentes;

    /// <summary>A conta que o cartão mostra: a lida do disco (ChatGPT e Claude) ou, no Gemini, o que o último teste viu do login do Antigravity.</summary>
    ContaLocal ContaDoCartao(Provedor p) =>
        p == Provedor.Gemini
            ? ((ModoDoAgente(p) == ModoAcesso.ContaGemini ? sessaoGeminiWebConectada : contaGeminiConectada == true)
                ? new ContaLocal(true, null, ModoDoAgente(p) == ModoAcesso.ContaGemini ? emailGeminiWeb : null, null) : ContaLocal.Nenhuma)
            : infoLocal.Conta(p);

    internal PerfilGemini? PerfilApiDoGemini => ajustes.Agentes[Provedor.Gemini].PerfilApi;

    /// <summary>O perfil do Gemini que vale agora em Ajustes (conta ⇒ conta Ultra; chave ⇒ o perfil escolhido, ou null).</summary>
    internal PerfilGemini? PerfilDoGemini => MatrizGemini.PerfilDoModo(ajustes.Agentes[Provedor.Gemini].Modo, ajustes.Agentes[Provedor.Gemini].PerfilApi);

    /// <summary>Autoteste do executável (1.1.0): os três provedores estão de fato na janela publicada — os três botões "Analisar com o …" em Novo
    /// relatório, cada um ligado ao seu provedor, e os três cartões em Ajustes › Inteligência artificial, com o perfil da chave e a matriz no do Gemini.
    /// Só olha a tela montada: não inicia análise, não roda programa e não fala com a rede.</summary>
    internal Dictionary<string, object?> AutotesteDosAgentes()
    {
        var d = new Dictionary<string, object?>();
        try
        {
            var botoes = Enum.GetValues<Provedor>().ToDictionary(p => Provedores.Sufixo(p), p => BotaoDoProvedor(p));
            var rotulos = Enum.GetValues<Provedor>().ToDictionary(p => Provedores.Sufixo(p), p =>
                (BotaoDoProvedor(p).Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text);
            d["botoes"] = rotulos;
            var botoesOk = botoes.Values.Distinct().Count() == 3
                && rotulos.All(r => r.Value == "Analisar com o " + Provedores.Nome(Enum.GetValues<Provedor>().Single(p => Provedores.Sufixo(p) == r.Key)));
            var cartoes = Enum.GetValues<Provedor>().Select(CartaoDoAgente).ToList();
            var gemini = CartaoDoAgente(Provedor.Gemini);
            var cartoesOk = cartoes.Distinct().Count() == 3 && gemini.CaixaPerfil.Items.Count == TextosAgentes.OpcoesPerfilGemini.Count
                && (gemini.TextoServico.Text == TextosAgentes.NomeDoServico(Provedor.Gemini) || gemini.TextoServico.Text == "Gemini Web");
            d["cartoes"] = cartoes.Count;
            d["perfis_do_gemini"] = gemini.CaixaPerfil.Items.Count;
            d["matriz"] = Enum.GetValues<PerfilGemini>().ToDictionary(p => MatrizGemini.Codigo(p), p => MatrizGemini.Etapas(p).Select(e => e.IdRuntime).ToArray());
            var matrizOk = Enum.GetValues<PerfilGemini>().All(p => MatrizGemini.Etapas(p).Count == SituacaoAnalisePro.Etapas.Count);
            d["ok"] = botoesOk && cartoesOk && matrizOk;
            if (!botoesOk) d["erro"] = "os três botões Analisar não estão na página Novo relatório";
            else if (!cartoesOk) d["erro"] = "o cartão do Gemini não está completo em Ajustes";
            else if (!matrizOk) d["erro"] = "a matriz do Gemini não tem as seis etapas";
        }
        catch (Exception e)
        {
            d["ok"] = false;
            d["erro"] = e.GetType().Name + ": " + e.Message;
        }
        return d;
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

    internal CartaoAgente CartaoDoAgente(Provedor p) =>
        p switch { Provedor.ChatGpt => PaginaDeAjustes.CartaoChatGpt, Provedor.Claude => PaginaDeAjustes.CartaoClaude, _ => PaginaDeAjustes.CartaoGemini };

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
        if (p == Provedor.Gemini)
        {
            if (ajustes.Agentes[p].Modo == ModoAcesso.ContaGemini)
            {
                await AbrirLoginGeminiAsync();
                return;
            }
            // O Antigravity não tem comando de login/logout fora da tela dele: abre o agy (pede o login do Google se não houver; /logout sai).
            if (!AbrirLogin(pecasAgentes.Agy, "agy", "")) return;
            notasDasContas[p] = TextosAgentes.GeminiLoginAberto;
            testarAoVoltar.Add(p);
            MostrarAgentes();
            return;
        }
        var sair = infoLocal.Conta(p).Conectado;
        if (sair && !await Perguntas.Atual.ConfirmarAsync(TextosAgentes.PerguntaSair(p), TextosAgentes.ExplicacaoSair, "Sair", "Cancelar")) return;
        var (exe, nome) = p == Provedor.ChatGpt ? (pecasAgentes.Codex, "codex") : (pecasAgentes.Claude, "claude");
        var argumento = p == Provedor.ChatGpt ? (sair ? "logout" : "login") : (sair ? "/logout" : "/login");
        if (!AbrirLogin(exe, nome, argumento)) return;
        notasDasContas[p] = TextosAgentes.LoginAberto;
        testarAoVoltar.Add(p);
        MostrarAgentes();
    }

    async Task AbrirLoginGeminiAsync()
    {
        if (janelaLoginGemini is { } aberta)
        {
            aberta.AppWindow.Show();
            aberta.Activate();
            return;
        }

        var web = new WebView2();
        var janela = new Window { Title = "Conta Google · Gemini", Content = web };
        janelaLoginGemini = janela;
        PonteGeminiWeb? ponte = null;
        var timer = janela.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        bool conferindo = false;
        timer.Tick += async (_, _) =>
        {
            if (conferindo || ponte is null) return;
            conferindo = true;
            try
            {
                bool conectada = await ponte.ConectadoAsync();
                var email = conectada ? await ponte.EmailDaContaAsync() : null;
                if (janelaLoginGemini != janela || (sessaoGeminiWebConectada == conectada && emailGeminiWeb == email)) return;
                emailGeminiWeb = email;
                sessaoGeminiWebConectada = conectada;
                MostrarAgentes();
            }
            catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or System.Text.Json.JsonException) { }
            finally { conferindo = false; }
        };
        bool encerrando = false;
        void EncerrarComEditor(object sender, WindowEventArgs args)
        {
            encerrando = true;
            if (janelaLoginGemini == janela) janela.Close();
        }
        Closed += EncerrarComEditor;
        janela.AppWindow.Closing += (_, args) =>
        {
            if (encerrando) return;
            args.Cancel = true;
            janela.AppWindow.Hide();
        };
        janela.Closed += (_, _) =>
        {
            Closed -= EncerrarComEditor;
            timer.Stop();
            janelaLoginGemini = null;
            sessaoGeminiWebConectada = false;
            emailGeminiWeb = null;
            ponte?.Dispose();
            web.Close();
            MostrarAgentes();
        };
        try
        {
            janela.Activate();
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EditorRelatoriosPMVNI", "GoogleWeb", "Profile");
            await web.EnsureCoreWebView2Async(await FabricaWebView2.CriarAmbienteAsync(pasta));
            if (janelaLoginGemini != janela) return;
            web.Source = new Uri("https://gemini.google.com/app");
            ponte = new PonteGeminiWeb(web, Path.GetDirectoryName(pasta)!);
            timer.Start();
        }
        catch
        {
            encerrando = true;
            if (janelaLoginGemini == janela) janela.Close();
            throw;
        }
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
            c.OpcaoContaGemini.Click += async (_, _) => await DefinirModoAsync(p, ModoAcesso.ContaGemini);
            c.OpcaoConta.Click += async (_, _) => await DefinirModoAsync(p, ModoAcesso.Conta);
            c.OpcaoChave.Click += async (_, _) => await DefinirModoAsync(p, ModoAcesso.Chave);
            c.BotaoGuardarChave.Click += async (_, _) => await GuardarChaveAsync(p, c.CaixaChave.Password);
            c.BotaoRemoverChave.Click += async (_, _) => await RemoverChaveAsync(p);
            c.BotaoReverAviso.Click += async (_, _) => await ReverAvisoAsync(p);
            if (p == Provedor.Gemini)
                c.CaixaPerfil.SelectionChanged += async (_, _) => { if (!mostrandoAgentes) await DefinirPerfilGeminiAsync(TextosAgentes.PerfilDoIndice(c.CaixaPerfil.SelectedIndex)); };
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
        mudou |= acompanhamentoPlugins.Registrar(ajustes.PluginsVistos, AcompanhamentoPlugins.Chave(Provedor.Gemini, PluginsInstalados.AnaliseGoogle), infoLocal.AnaliseGoogleNoCodex?.Versao);
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
            // Gemini, modo chave: a chave sai do cofre só para a conferência na API do Google (lista de modelos); nunca vai para a tela nem para o registro.
            var chaveGemini = p == Provedor.Gemini && ajustes.Agentes[p].Modo == ModoAcesso.Chave ? await Task.Run(() => CofreChaves.Ler(PastaDados.Raiz, p)) : null;
            resultado = await Diagnostico.TestarAsync(p, pecas, ajustes.Agentes[p].Modo, CofreChaves.Existe(PastaDados.Raiz, p), CancellationToken.None,
                ajustes.Agentes[p].PerfilApi, chaveGemini);
            if (p == Provedor.Gemini && ajustes.Agentes[p].Modo == ModoAcesso.Conta) contaGeminiConectada = resultado.ContaConectada;
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
        AtualizarBotoesNR();     // a chave guardada habilita (ou explica) o botão Analisar do provedor
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
        AtualizarBotoesNR();
    }));

    /// <summary>Usar a conta do programa ou a chave de API. O Claude só aceita a chave se o análise pro instalado do kit não a proibir; senão nada muda.</summary>
    internal Task DefinirModoAsync(Provedor p, ModoAcesso m) => NaFilaAsync(() => TentarAsync(async () =>
    {
        await Task.CompletedTask;
        mensagensDosAgentes.Remove(p);
        if (ajustes.Agentes[p].Modo != m)
        {
            ajustes.Agentes[p].Modo = m;
            SalvarAjustes();
            if (p == Provedor.Gemini) { contaGeminiConectada = null; situacaoDosAgentes.Remove(p); avisosDosAgentes.Remove(p); }     // o teste anterior valia para o outro modo
        }
        MostrarAgentes();
        AtualizarBotoesNR();     // o modo muda o motivo do botão Analisar (chave guardada, perfil do Gemini)
        if (catalogoIa is not null) MostrarModelosIa();   // a Claude API entra/sai da lista de Modelos de IA
    }));

    /// <summary>Gemini, modo chave: o perfil da chave (com créditos ou gratuita). Só muda por escolha do usuário; null = "Escolha o perfil…".</summary>
    internal Task DefinirPerfilGeminiAsync(PerfilGemini? perfil) => NaFilaAsync(() => TentarAsync(async () =>
    {
        await Task.CompletedTask;
        var a = ajustes.Agentes[Provedor.Gemini];
        if (perfil is not (PerfilGemini.ApiCreditos or PerfilGemini.ApiGratuita)) perfil = null;     // a conta não é perfil de chave
        if (a.PerfilApi == perfil) return;
        a.PerfilApi = perfil;
        SalvarAjustes();
        situacaoDosAgentes.Remove(Provedor.Gemini);     // o teste anterior valia para o outro perfil
        avisosDosAgentes.Remove(Provedor.Gemini);
        MostrarAgentes();
        AtualizarBotoesNR();
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
        mostrandoAgentes = true;     // marcar a caixa do perfil pelo código não é uma escolha do usuário
        try { foreach (var p in Enum.GetValues<Provedor>()) MostrarCartao(p); }
        finally { mostrandoAgentes = false; }
        MostrarModelosIa();   // a chave guardada ou o programa achado mudam as fontes de Modelos de IA
    }

    void MostrarCartao(Provedor p)
    {
        var c = CartaoDoAgente(p);
        var a = ajustes.Agentes[p];
        var pecas = pecasAgentes;

        var conta = ContaDoCartao(p);
        var gemini = p == Provedor.Gemini;

        // Conta (lida neste computador) e Assinatura.
        bool contaWeb = gemini && a.Modo == ModoAcesso.ContaGemini;
        var servico = contaWeb ? "Gemini Web" : TextosAgentes.NomeDoServico(p);
        var acao = contaWeb && conta.Conectado ? "Abrir" : TextosAgentes.AcaoDaConta(conta);
        c.TextoServico.Text = servico;
        c.BotaoConta.Content = acao;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(c.BotaoConta, acao + " no " + servico);
        Dicas.Aplicar(c.BotaoConta, acao, contaWeb ? "Abre a janela da Conta Google no Gemini Web." : conta.Conectado
            ? "Sai da conta do " + TextosAgentes.NomeDoServico(p) + " neste computador."
            : "Entra na sua conta do " + TextosAgentes.NomeDoServico(p) + " para o app poder usar a IA.");
        c.TextoNomeConta.Text = contaWeb && conta.Conectado ? "Conectado" : TextosAgentes.NomeDaConta(conta);
        c.FotoConta.DisplayName = conta.Conectado ? conta.Nome ?? conta.Email ?? "" : "";
        var email = contaWeb ? conta.Email ?? "" : TextosAgentes.EmailDaConta(conta);
        c.TextoEmailConta.Text = email;
        c.TextoEmailConta.Visibility = email.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var nota = notasDasContas.TryGetValue(p, out var n) ? n : conta.Conectado ? ""
            : contaWeb ? "Abra a janela do Gemini para conferir a sessão da Conta Google."
            : gemini && contaGeminiConectada is null ? TextosAgentes.GeminiContaNaoVerificada : TextosAgentes.SemSessao;
        c.TextoNotaConta.Text = nota;
        c.TextoNotaConta.Visibility = nota.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        c.TextoAssinatura.Text = TextosAgentes.Assinatura(conta);

        // Plugins do kit: versão, data e o aviso quando mudou desde a última vez.
        if (!gemini)
            MostrarPlugin(c.TextoPluginAnalise, c.SeloPluginAnalise, c.TextoPluginAnaliseNovo, p, PluginsInstalados.Analise,
                p == Provedor.ChatGpt ? infoLocal.AnaliseNoCodex : infoLocal.AnaliseNoClaude, pecas.PluginAnaliseHabilitado);
        if (p == Provedor.Claude)
            MostrarPlugin(c.TextoPluginPro, c.SeloPluginPro, c.TextoPluginProNovo, p, PluginsInstalados.AnalisePro, infoLocal.AnaliseProNoCodex, pecas.PluginAnaliseHabilitado);
        if (gemini)
            MostrarPlugin(c.TextoPluginPro, c.SeloPluginPro, c.TextoPluginProNovo, p, PluginsInstalados.AnaliseGoogle, infoLocal.AnaliseGoogleNoCodex, pecas.PluginAnaliseHabilitado);

        var achado = p switch
        {
            Provedor.ChatGpt => pecas.Codex is not null,
            Provedor.Claude => pecas.Python is not null && pecas.Claude is not null,
            _ => pecas.Python is not null && pecas.Agy is not null,
        };
        c.SeloPrograma.Definir(achado, achado ? TextosAgentes.Encontrado : TextosAgentes.NaoEncontrado);
        c.TextoPrograma.Text = p == Provedor.ChatGpt
            ? TextosAgentes.LinhaDoPrograma("Codex", pecas.Codex, a.Executavel)
            : gemini
            ? TextosAgentes.LinhaDoPrograma("Python", pecas.Python, ajustes.Agentes[Provedor.Claude].Executavel) + (pecas.PythonPeloLauncher ? " (pelo py.exe -3)" : "")
              + Environment.NewLine + "Antigravity CLI (agy): " + (pecas.Agy ?? "não encontrado")
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

        c.OpcaoContaGemini.IsChecked = a.Modo == ModoAcesso.ContaGemini;
        c.OpcaoConta.IsChecked = a.Modo == ModoAcesso.Conta;
        c.OpcaoChave.IsChecked = a.Modo == ModoAcesso.Chave;
        var apiProibida = p == Provedor.Claude && !pecas.ApiPermitidaNoKit;
        // 2026-11-17: no Claude, "Usar chave de API" é o modo Anthropic API do análise pro (1.6.4+); a dica diz o que isso muda ou por que não roda.
        var dica = gemini ? (a.Modo switch
        {
            ModoAcesso.Chave => TextosAgentes.GeminiModoApi,
            ModoAcesso.ContaGemini => "Use Entrar para abrir o Gemini. Fechar a janela do Gemini mantém a sessão disponível enquanto o editor estiver aberto.",
            _ => TextosAgentes.GeminiModoConta,
        })
            : p != Provedor.Claude ? "" : apiProibida ? TextosAgentes.ClaudeSoConta : a.Modo == ModoAcesso.Chave ? TextosAgentes.ClaudeModoApi : "";
        c.OpcaoChave.IsEnabled = true;
        ToolTipService.SetToolTip(c.OpcaoChave, gemini ? TextosAgentes.GeminiModoApi : apiProibida ? TextosAgentes.ClaudeSoConta : p == Provedor.Claude ? TextosAgentes.ClaudeModoApi : null);
        if (gemini)
        {
            // O perfil da chave (só no modo chave) e os modelos de cada etapa no perfil que vale agora.
            c.PainelPerfil.Visibility = a.Modo == ModoAcesso.Chave ? Visibility.Visible : Visibility.Collapsed;
            var indice = TextosAgentes.IndiceDoPerfil(a.PerfilApi);
            if (c.CaixaPerfil.SelectedIndex != indice) c.CaixaPerfil.SelectedIndex = indice;
            c.TextoMatriz.Text = TextosAgentes.MatrizDoGemini(a.Modo, a.PerfilApi);
            c.TextoMatriz.Visibility = Visibility.Visible;
        }
        c.TextoDicaChave.Text = dica;
        c.TextoDicaChave.Visibility = dica.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        c.PainelChave.Visibility = a.Modo == ModoAcesso.Chave ? Visibility.Visible : Visibility.Collapsed;
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
