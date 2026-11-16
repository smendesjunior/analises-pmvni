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
        LinkConta.Content = TextosAgentes.RotuloConta(p);
        LinkConta.NavigateUri = new Uri(TextosAgentes.EnderecoConta(p));
        TituloPrograma.Text = p == Provedor.ChatGpt ? "Programa" : "Programas";
        TituloPluginAnalise.Text = TextosAgentes.TituloPlugin(PluginsInstalados.Analise);
        TituloPluginPro.Text = TextosAgentes.TituloPlugin(PluginsInstalados.AnalisePro);
        LinhaPluginPro.Visibility = p == Provedor.Claude ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        OpcaoConta.GroupName = OpcaoChave.GroupName = "acesso-" + Provedores.Sufixo(p);
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
