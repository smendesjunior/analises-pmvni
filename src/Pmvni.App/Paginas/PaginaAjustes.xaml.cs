using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Página do módulo Ajustes (desenho §9): só a parte visual. Quem a preenche e reage às escolhas é a janela
/// (<c>MainWindow.Ajustes.cs</c>), pelos elementos <c>internal</c>. É criada uma vez e trocada por Visibility.</summary>
public sealed partial class PaginaAjustes : UserControl
{
    public PaginaAjustes() => InitializeComponent();
}
