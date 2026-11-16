using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Pmvni.App;

/// <summary>Etiqueta de situação da lista de seções (correções 2026-11-15): a "pill" do relatório (ponto + texto em caixa alta, cores do kit,
/// com variante para o tema escuro: <see cref="EtiquetasSituacao"/>). É um botão: clicar abre o menu das situações (quem liga é a janela).</summary>
public sealed partial class EtiquetaSituacao : Button
{
    public static readonly DependencyProperty SituacaoProperty = DependencyProperty.Register(
        nameof(Situacao), typeof(string), typeof(EtiquetaSituacao), new PropertyMetadata(null, (d, _) => ((EtiquetaSituacao)d).Aplicar()));

    readonly Ellipse ponto = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock texto = new()
    {
        FontSize = 10.5, FontWeight = FontWeights.Bold, CharacterSpacing = 30, VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };

    public EtiquetaSituacao()
    {
        if (Application.Current.Resources.TryGetValue("PmvniEtiquetaSituacaoStyle", out var estilo) && estilo is Style s) Style = s;
        var painel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        painel.Children.Add(ponto);
        painel.Children.Add(texto);
        Content = painel;
        Visibility = Visibility.Collapsed; // sem situação (seções, capa, figuras), nada aparece
        ActualThemeChanged += (_, _) => Aplicar();
    }

    /// <summary>Código da situação (ATENDE, NAO_ATENDE…), como em <see cref="Pmvni.Composicao.Kit.Situacoes.Sit"/>.</summary>
    public string? Situacao
    {
        get => (string?)GetValue(SituacaoProperty);
        set => SetValue(SituacaoProperty, value);
    }

    void Aplicar()
    {
        texto.Text = EtiquetasSituacao.Texto(Situacao);
        AutomationProperties.SetName(this, EtiquetasSituacao.NomeAcessivel(Situacao));
        if (EtiquetasSituacao.Cores(Situacao, ActualTheme == ElementTheme.Dark) is not { } c)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        var dica = DicasComandos.Livre("Situação: " + texto.Text, "Clique para mudar a situação deste item.");
        if (Dicas.De(this) != dica) Dicas.Aplicar(this, dica);
        Background = Pincel(c.Fundo);
        BorderBrush = Pincel(c.Borda);
        texto.Foreground = Pincel(c.Texto);
        ponto.Fill = Pincel(c.Ponto);
    }

    static SolidColorBrush Pincel(uint argb) =>
        new(Microsoft.UI.ColorHelper.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
}
