using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Pmvni.App;

public sealed partial class LinhaCategoriaAnexos : UserControl
{
    public LinhaCategoriaAnexos() => InitializeComponent();

    internal StackPanel Lista => Arquivos;
    internal event DragEventHandler? ArquivosSoltos;
    internal void Configurar(string nome, string glifo, string? descricao, RoutedEventHandler anexar)
    {
        Nome.Text = nome;
        Icone.Glyph = glifo;
        Descricao.Text = descricao ?? "";
        Descricao.Visibility = descricao is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(Zona, "Área de soltura de " + nome);
        AutomationProperties.SetName(Botao, "Anexar arquivos em " + nome);
        ToolTipService.SetToolTip(Botao, "Selecionar arquivos para " + nome);
        Botao.Click += anexar;
    }

    internal void AtualizarDica() => Dica.Text = Arquivos.Children.Count > 0
        ? "Arraste mais arquivos aqui" : "Arraste e solte arquivos aqui";

    internal const double LarguraTabela = 620;

    void AjustarLinha(object sender, SizeChangedEventArgs e)
    {
        bool compacta = e.NewSize.Width < LarguraTabela;
        Linha.ColumnSpacing = compacta ? 0 : 12;
        Linha.RowSpacing = compacta ? 8 : 0;
        Grid.SetColumnSpan((FrameworkElement)Linha.Children[0], compacta ? 3 : 1);
        Grid.SetColumnSpan(Zona, compacta ? 3 : 1);
        Grid.SetColumn(Zona, compacta ? 0 : 1);
        Grid.SetRow(Zona, compacta ? 1 : 0);
        Grid.SetColumn(Botao, compacta ? 0 : 2);
        Grid.SetRow(Botao, compacta ? 2 : 0);
        Grid.SetColumnSpan(Botao, compacta ? 3 : 1);
    }

    void Arrastar(object sender, DragEventArgs e)
    {
        var aceita = IsEnabled && e.DataView.Contains(StandardDataFormats.StorageItems);
        Realce.Visibility = aceita ? Visibility.Visible : Visibility.Collapsed;
        e.AcceptedOperation = aceita ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (aceita) e.DragUIOverride.Caption = "Anexar em " + Nome.Text;
        e.Handled = true;
    }

    void Sair(object sender, DragEventArgs e) => Realce.Visibility = Visibility.Collapsed;

    void Soltar(object sender, DragEventArgs e)
    {
        Realce.Visibility = Visibility.Collapsed;
        if (IsEnabled) ArquivosSoltos?.Invoke(this, e);
    }
}
