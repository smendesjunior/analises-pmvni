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
        ListaNavegacao.LayoutUpdated += (_, _) => AtualizarRodape();
        foreach (var aba in AbasPainel.Items) FaixaDeOpcoes.AjustarIndicador(aba);
    }

    internal int TotalItensFiltrados { get; set; }

    void AtualizarRodape()
    {
        if (ListaNavegacao.ItemsPanelRoot is not ItemsStackPanel painel || ListaNavegacao.Visibility != Visibility.Visible) return;
        var total = ListaNavegacao.Items.Count;
        var ultimo = Math.Min(total, painel.LastVisibleIndex + 1);
        int primeiroItem = 0, ultimoItem = 0;
        for (var i = Math.Max(0, painel.FirstVisibleIndex); i < ultimo; i++)
        {
            if (ListaNavegacao.Items[i] is not ItemNavegacao { OrdemNoFiltro: > 0 } item) continue;
            if (primeiroItem == 0) primeiroItem = item.OrdemNoFiltro;
            ultimoItem = item.OrdemNoFiltro;
        }
        var texto = primeiroItem > 0 ? $"Itens {primeiroItem:00}–{ultimoItem:00} de {TotalItensFiltrados}" : "";
        if (ContagemNavegacao.Text != texto) ContagemNavegacao.Text = texto;
        MaisItens.IsEnabled = ultimo > 0 && ultimo < total;
    }

    void AvancarNavegacao(object sender, RoutedEventArgs e)
    {
        if (ListaNavegacao.ItemsPanelRoot is ItemsStackPanel painel && painel.LastVisibleIndex + 1 < ListaNavegacao.Items.Count)
            ListaNavegacao.ScrollIntoView(ListaNavegacao.Items[painel.LastVisibleIndex + 1], ScrollIntoViewAlignment.Leading);
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
