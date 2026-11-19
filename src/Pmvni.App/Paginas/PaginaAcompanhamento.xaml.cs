using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Módulo Acompanhamento de processos: só a casca; a janela preenche e reage (MainWindow.Acompanhamento.cs).</summary>
public sealed partial class PaginaAcompanhamento : UserControl
{
    void AoDimensionarCabecalho(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        bool estreito = e.NewSize.Width < 990;
        Grid.SetRow(Acoes, estreito ? 1 : 0);
        Grid.SetColumn(Acoes, estreito ? 0 : 2);
        Grid.SetColumnSpan(Acoes, estreito ? 3 : 1);
        Cabecalho.RowSpacing = estreito ? 8 : 0;
    }

    void AoDimensionarCartoes(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) => DistribuirCartoes();

    internal void DistribuirCartoes()
    {
        int quantidade = CartoesStatus.Children.Count;
        if (quantidade == 0) return;
        int colunas = Math.Clamp((int)((CartoesStatus.ActualWidth + 6) / 112), 1, quantidade);
        while (CartoesStatus.ColumnDefinitions.Count < colunas) CartoesStatus.ColumnDefinitions.Add(new ColumnDefinition());
        while (CartoesStatus.ColumnDefinitions.Count > colunas) CartoesStatus.ColumnDefinitions.RemoveAt(colunas);
        int linhas = (quantidade + colunas - 1) / colunas;
        while (CartoesStatus.RowDefinitions.Count < linhas) CartoesStatus.RowDefinitions.Add(new RowDefinition { Height = Microsoft.UI.Xaml.GridLength.Auto });
        while (CartoesStatus.RowDefinitions.Count > linhas) CartoesStatus.RowDefinitions.RemoveAt(linhas);
        for (int i = 0; i < quantidade; i++)
        {
            var cartao = (Microsoft.UI.Xaml.FrameworkElement)CartoesStatus.Children[i];
            Grid.SetRow(cartao, i / colunas);
            Grid.SetColumn(cartao, i % colunas);
        }
    }

    public PaginaAcompanhamento() => InitializeComponent();
}
