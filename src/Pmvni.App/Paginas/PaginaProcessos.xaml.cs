using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Página do módulo Processos (desenho §9): só a parte visual. Quem a preenche e reage às escolhas é a janela
/// (<c>MainWindow.Pastas.cs</c>), pelos elementos <c>internal</c>. É criada uma vez e trocada por Visibility.</summary>
public sealed partial class PaginaProcessos : UserControl
{
    bool sincronizando;

    public PaginaProcessos()
    {
        InitializeComponent();
        // A frase da tela vazia acompanha o texto que a janela escreve em TextoListaVazia.
        TextoListaVazia.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Vazio.DefinirFrase(TextoListaVazia.Text));
        // Correções 2026-11-15: as linhas do detalhe vão para três colunas; a escolha numa coluna é a escolha da ListaDetalhe.
        ListaDetalhe.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (_, _) => Distribuir());
        foreach (var c in Colunas)
            c.SelectionChanged += (_, _) =>
            {
                if (sincronizando || c.SelectedItem is not LinhaDetalhe l) return;
                sincronizando = true;
                foreach (var outra in Colunas) if (!ReferenceEquals(outra, c)) outra.SelectedItem = null;
                ListaDetalhe.SelectedItem = l;
                sincronizando = false;
            };
        ListaDetalhe.SelectionChanged += (_, _) =>
        {
            if (sincronizando) return;
            sincronizando = true;
            foreach (var c in Colunas)
                c.SelectedItem = ListaDetalhe.SelectedItem is LinhaDetalhe l && (c.ItemsSource as IList<LinhaDetalhe>)?.Contains(l) == true ? l : null;
            sincronizando = false;
        };
    }

    internal IReadOnlyList<ListView> Colunas => [ColunaRelatorios, ColunaDespachos, ColunaProjeto];

    void Distribuir()
    {
        var (rel, desp, proj) = ListasPastas.EmColunas(ListaDetalhe.ItemsSource as IReadOnlyList<LinhaDetalhe> ?? []);
        ColunaRelatorios.ItemsSource = rel;
        ColunaDespachos.ItemsSource = desp;
        ColunaProjeto.ItemsSource = proj;
    }
}
