using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Página do módulo Relatórios. Só a parte visual: quem a dirige (abrir, recompor, zoom, exportar) é a janela, pelos
/// elementos <c>internal</c>. É criada uma vez e trocada por Visibility: o WebView2 nunca é recriado.</summary>
public sealed partial class PaginaRelatorios : UserControl
{
    public PaginaRelatorios() => InitializeComponent();
}
