using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Página do módulo Processos (desenho §9): só a parte visual. Quem a preenche e reage às escolhas é a janela
/// (<c>MainWindow.Pastas.cs</c>), pelos elementos <c>internal</c>. É criada uma vez e trocada por Visibility.</summary>
public sealed partial class PaginaProcessos : UserControl
{
    public PaginaProcessos() => InitializeComponent();
}
