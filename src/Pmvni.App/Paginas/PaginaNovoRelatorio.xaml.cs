using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>Página Novo relatório (desenho §12.2): só a parte visual. Quem preenche, decide e inicia o agente é a janela (<c>MainWindow.NovoRelatorio.cs</c>)
/// com a <see cref="SessaoNovoRelatorio"/>.</summary>
public sealed partial class PaginaNovoRelatorio : UserControl
{
    ScrollViewer? rolagemDasLinhas;

    public PaginaNovoRelatorio()
    {
        InitializeComponent();
        CaixaPedido.PlaceholderText = FluxoNovoRelatorio.DicaPedido;
        TextoPedidoClaude.Text = FluxoNovoRelatorio.TextoPedidoClaude;
        TextoArraste.Text = FluxoNovoRelatorio.ArrasteAqui;
        // Correções 2026-11-15 (image7): a coluna do formulário tem a largura da área visível, menos as margens, até 1080 (centralizada).
        PainelFormulario.SizeChanged += (_, e) => AjustarLarguraDoFormulario(e.NewSize.Width);
    }

    /// <summary>Largura da coluna do formulário: a da área visível menos 24 de cada lado, entre 280 e 1080.</summary>
    internal static double LarguraDoFormulario(double larguraVisivel) => Math.Clamp(larguraVisivel - 48, 280, 1080);

    void AjustarLarguraDoFormulario(double larguraVisivel)
    {
        if (double.IsNaN(larguraVisivel) || larguraVisivel <= 0) return;
        var nova = Math.Round(LarguraDoFormulario(larguraVisivel));
        if (double.IsNaN(ColunaFormulario.Width) || Math.Abs(ColunaFormulario.Width - nova) >= 1) ColunaFormulario.Width = nova; // sem realimentar o layout por frações de pixel

    }

    /// <summary>Formulário (false) ou vista da execução (true).</summary>
    internal void MostrarExecucao(bool execucao)
    {
        PainelFormulario.Visibility = execucao ? Visibility.Collapsed : Visibility.Visible;
        PainelExecucao.Visibility = execucao ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool VistaDaExecucao => PainelExecucao.Visibility == Visibility.Visible;

    /// <summary>A lista do andamento está no fim (ou ainda não rola): as linhas novas a mantêm no fim.</summary>
    internal bool LinhasNoFim()
    {
        rolagemDasLinhas ??= Achar<ScrollViewer>(ListaLinhas);
        return rolagemDasLinhas is null || rolagemDasLinhas.VerticalOffset >= rolagemDasLinhas.ScrollableHeight - 4;
    }

    internal void RolarLinhasAoFim()
    {
        if (ListaLinhas.Items.Count > 0) ListaLinhas.ScrollIntoView(ListaLinhas.Items[^1]);
    }

    static T? Achar<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T t) return t;
            if (Achar<T>(filho) is { } achado) return achado;
        }
        return null;
    }

    /// <summary>As seis etapas do análise pro: feita (✓), agora (▶) ou pendente (○).</summary>
    internal void MostrarEtapas(IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas)
    {
        PainelEtapas.Children.Clear();
        foreach (var (nome, estado) in etapas)
            PainelEtapas.Children.Add(new TextBlock
            {
                Text = (estado switch { EstadoEtapa.Feita => "✓ ", EstadoEtapa.Agora => "▶ ", _ => "○ " }) + nome,
                Opacity = estado == EstadoEtapa.Pendente ? 0.6 : 1,
                FontWeight = estado == EstadoEtapa.Agora ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            });
    }
}
