using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Shapes;

namespace Pmvni.App;

/// <summary>Etiqueta de situação da lista de seções (correções 2026-11-15): a "pill" do relatório (ponto + texto em caixa alta, cores do kit,
/// com cores do tema conforme <see cref="EtiquetasSituacao"/>). É um botão: clicar abre o menu das situações (quem liga é a janela).</summary>
public sealed partial class EtiquetaSituacao : Button
{
    public static readonly DependencyProperty SituacaoProperty = DependencyProperty.Register(
        nameof(Situacao), typeof(string), typeof(EtiquetaSituacao), new PropertyMetadata(null, (d, _) => ((EtiquetaSituacao)d).Aplicar()));

    readonly Ellipse ponto = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock texto = new()
    {
        Style = (Style)Application.Current.Resources["PmvniPainelCartaoTitulo"],
        TextWrapping = TextWrapping.NoWrap,
    };

    public EtiquetaSituacao()
    {
        if (Application.Current.Resources.TryGetValue("PmvniEtiquetaSituacaoStyle", out var estilo) && estilo is Style s) Style = s;
        var cor = new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) };
        ponto.SetBinding(Shape.FillProperty, cor);
        texto.SetBinding(TextBlock.ForegroundProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        var painel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        painel.Children.Add(ponto);
        painel.Children.Add(texto);
        Content = painel;
        Visibility = Visibility.Collapsed; // sem situação (seções, capa, figuras), nada aparece
    }

    public static readonly DependencyProperty CompactaProperty = DependencyProperty.Register(
        nameof(Compacta), typeof(bool), typeof(EtiquetaSituacao), new PropertyMetadata(false, (d, _) => ((EtiquetaSituacao)d).Aplicar()));

    /// <summary>Só a bolinha colorida (lista de seções em uma linha, auditoria de interface 2026-11-17); o nome da situação fica na dica e
    /// no Narrador, e clicar continua abrindo o menu das situações.</summary>
    public bool Compacta
    {
        get => (bool)GetValue(CompactaProperty);
        set => SetValue(CompactaProperty, value);
    }

    public static readonly DependencyProperty MostrarProperty = DependencyProperty.Register(
        nameof(Mostrar), typeof(bool), typeof(EtiquetaSituacao), new PropertyMetadata(true, (d, _) => ((EtiquetaSituacao)d).Aplicar()));

    /// <summary>false esconde a etiqueta mesmo com situação (a lista de seções escolhe entre a bolinha e a etiqueta inteira).</summary>
    public bool Mostrar
    {
        get => (bool)GetValue(MostrarProperty);
        set => SetValue(MostrarProperty, value);
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
        var estilo = EtiquetasSituacao.Classe(Situacao) switch
        {
            "g" => "PmvniEtiquetaSituacaoSucesso",
            "a" => "PmvniEtiquetaSituacaoAtencao",
            "conferir" => "PmvniEtiquetaSituacaoConferir",
            "o" => "PmvniEtiquetaSituacaoDivergente",
            "r" => "PmvniEtiquetaSituacaoCritico",
            _ => null,
        };
        if (!Mostrar || estilo is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Style = (Style)Application.Current.Resources[estilo];
        Visibility = Visibility.Visible;
        var dica = DicasComandos.Livre("Situação: " + texto.Text, "Clique para mudar a situação deste item.");
        if (Dicas.De(this) != dica) Dicas.Aplicar(this, dica);
        texto.Visibility = Compacta ? Visibility.Collapsed : Visibility.Visible;
        ponto.Width = ponto.Height = Compacta ? 10 : 7;
        if (Compacta)
        {
            Padding = new Thickness(4);
            if (Situacao == "DECISAO_DO_ANALISTA")
            {
                ClearValue(BackgroundProperty);
                ClearValue(BorderBrushProperty);
            }
            else Background = BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"];
        }
        else
        {
            ClearValue(PaddingProperty);
            ClearValue(BackgroundProperty);
            ClearValue(BorderBrushProperty);
        }
    }
}
