using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Selo "✓ texto" (verde) ou "✗ texto" (neutro) das linhas dos cartões dos agentes.</summary>
public sealed partial class SeloEstado : UserControl
{
    public SeloEstado() => InitializeComponent();

    /// <summary>O texto à vista (o roteiro confere).</summary>
    public string Texto { get; private set; } = "";

    /// <summary>true = verde com ✓.</summary>
    public bool Ok { get; private set; }

    public void Definir(bool ok, string texto)
    {
        Ok = ok;
        Texto = texto;
        TextoOk.Text = TextoNeutro.Text = texto;
        SeloOk.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        SeloNeutro.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, texto);
    }
}
