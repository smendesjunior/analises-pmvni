using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Página do módulo Relatórios. Só a parte visual: quem a dirige (abrir, recompor, zoom, exportar) é a janela, pelos
/// elementos <c>internal</c>. É criada uma vez e trocada por Visibility: o WebView2 nunca é recriado.</summary>
public sealed partial class PaginaRelatorios : UserControl
{
    public PaginaRelatorios()
    {
        InitializeComponent();
        foreach (var aba in AbasPainel.Items) FaixaDeOpcoes.AjustarIndicador(aba);
    }

    /// <summary>Clique na etiqueta de situação de um item da lista de seções: a janela abre o menu das situações junto dela.</summary>
    internal event Action<ItemNavegacao, FrameworkElement>? EtiquetaClicada;

    void AoClicarEtiqueta(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ItemNavegacao item } alvo) EtiquetaClicada?.Invoke(item, alvo);
    }

    /// <summary>Clique na seta de uma seção da lista: a janela recolhe ou expande os itens dela.</summary>
    internal event Action<ItemNavegacao>? SecaoAlternada;

    void AoClicarChevron(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ItemNavegacao item }) SecaoAlternada?.Invoke(item);
    }
}
