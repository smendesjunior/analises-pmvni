using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Página do módulo Despachos (desenho §9): só a parte visual. Quem a preenche e reage às escolhas é a janela
/// (<c>MainWindow.Pastas.cs</c>), pelos elementos <c>internal</c>. É criada uma vez e trocada por Visibility.</summary>
public sealed partial class PaginaDespachos : UserControl
{
    public PaginaDespachos()
    {
        InitializeComponent();
        // A frase da tela vazia acompanha o texto que a janela escreve em TextoListaVazia.
        // Despachos é só para criar (correções 2026-11-15): a lista dos emitidos fica em Processos; a frase da tela de criação é fixa.
    }
}
