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
        OpcaoConta.GroupName = OpcaoChave.GroupName = "acesso-" + Provedores.Sufixo(p);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoEscolherPrograma, "Escolher o programa do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoProgramaPadrao, "Usar o programa padrão do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoTestar, "Testar o " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CaixaChave, "Chave de API do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoGuardarChave, "Guardar a chave de API do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoRemoverChave, "Remover a chave de API do " + nome);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoReverAviso, "Rever o aviso do " + nome);
    }
}
