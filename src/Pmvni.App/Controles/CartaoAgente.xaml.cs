using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Cartão de um agente local em Ajustes › Inteligência artificial: só a parte visual. Quem o preenche e reage aos botões é a janela
/// (<c>MainWindow.Agentes.cs</c>), pelos elementos <c>internal</c>.</summary>
public sealed partial class CartaoAgente : UserControl
{
    public CartaoAgente() => InitializeComponent();

    /// <summary>Um cartão por provedor: dá o título, o grupo do acesso (os dois cartões não se misturam) e os nomes para quem lê a tela.</summary>
    internal void Configurar(Provedor p)
    {
        var nome = Provedores.Nome(p);
        Titulo.Text = TextosAgentes.Titulo(p);
        TextoServico.Text = TextosAgentes.NomeDoServico(p);
        LogoOpenAi.Visibility = p == Provedor.ChatGpt ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        LogoClaude.Visibility = p == Provedor.Claude ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        LogoGemini.Visibility = p == Provedor.Gemini ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        LinkConta.Content = TextosAgentes.RotuloConta(p);
        LinkConta.NavigateUri = new Uri(TextosAgentes.EnderecoConta(p));
        TituloPrograma.Text = p == Provedor.ChatGpt ? "Programa" : "Programas";
        TituloPluginAnalise.Text = TextosAgentes.TituloPlugin(PluginsInstalados.Analise);
        // A segunda linha de plugin é a do controlador do kit: análise pro (Claude) ou análise Google (Gemini, que não usa o plugin analise).
        TituloPluginPro.Text = TextosAgentes.TituloPlugin(p == Provedor.Gemini ? PluginsInstalados.AnaliseGoogle : PluginsInstalados.AnalisePro);
        LinhaPluginPro.Visibility = Provedores.PeloKit(p) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        LinhaPluginAnalise.Visibility = p == Provedor.Gemini ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        if (p == Provedor.Gemini)
        {
            // O Python do Gemini é o mesmo escolhido no cartão do Claude; o Antigravity é achado sozinho.
            PainelBotoesPrograma.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            OpcaoContaGemini.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            OpcaoConta.Content = "Conta Google (login do Antigravity, cota do plano)";
            OpcaoChave.Content = "Chave de API do Google AI Studio (guardada protegida pelo Windows, só neste usuário)";
            foreach (var opcao in TextosAgentes.OpcoesPerfilGemini) CaixaPerfil.Items.Add(opcao);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CaixaPerfil, "Perfil da chave do Gemini");
        }
        OpcaoContaGemini.GroupName = OpcaoConta.GroupName = OpcaoChave.GroupName = "acesso-" + Provedores.Sufixo(p);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoEscolherPrograma, "Escolher o programa do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoProgramaPadrao, "Usar o programa padrão do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoTestar, "Testar o " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CaixaChave, "Chave de API do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoGuardarChave, "Guardar a chave de API do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoRemoverChave, "Remover a chave de API do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoReverAviso, "Rever o aviso do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LinkConta, TextosAgentes.RotuloConta(p) + " (abre no navegador)");
        Dicas.Aplicar(LinkConta, TextosAgentes.RotuloConta(p), "Abre no navegador a página da sua conta do " + nome + ".");
    }
}
