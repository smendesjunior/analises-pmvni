using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Pmvni.App;

/// <summary>Item da lista de atividades: a atividade e se ela é a que está em curso (a última, enquanto a análise roda).</summary>
public sealed class ItemAtividade(Atividade atividade) : INotifyPropertyChanged
{
    bool emCurso;
    public Atividade Atividade { get; } = atividade;
    public bool EmCurso { get => emCurso; set { if (emCurso == value) return; emCurso = value; PropertyChanged?.Invoke(this, new(nameof(EmCurso))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Uma linha das "Atividades da análise" (redesenho 2026-11-17): marcador de status na linha do tempo (concluída ✓, em curso ◌, erro !),
/// cartão com ícone, título natural, detalhe e horário. Mensagens da IA quebram linha; ações ficam numa linha.</summary>
public sealed class LinhaAndamento : Grid
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(Atividade), typeof(LinhaAndamento), new PropertyMetadata(null, (d, _) => ((LinhaAndamento)d).Montar()));
    public static readonly DependencyProperty EmCursoProperty = DependencyProperty.Register(
        nameof(EmCurso), typeof(bool), typeof(LinhaAndamento), new PropertyMetadata(false, (d, _) => ((LinhaAndamento)d).Montar()));

    public Atividade? Item { get => (Atividade?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public bool EmCurso { get => (bool)GetValue(EmCursoProperty); set => SetValue(EmCursoProperty, value); }

    readonly Grid marcador = new() { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
    readonly Ellipse circulo = new();
    readonly FontIcon simbolo = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly ProgressRing anel = new() { Width = 20, Height = 20, IsActive = false, Visibility = Visibility.Collapsed };
    readonly Rectangle trilho = new() { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, -6) };
    readonly Border cartao = new() { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 12, 8), BorderThickness = new Thickness(1) };
    readonly Border fundoIcone = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top };
    readonly FontIcon icone = new() { FontSize = 16 };
    readonly TextBlock titulo = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock detalhe = new() { FontSize = 12, IsTextSelectionEnabled = true };
    readonly TextBlock hora = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0) };

    public LinhaAndamento()
    {
        ColumnSpacing = 10;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Margin = new Thickness(0, 3, 0, 3);

        Children.Add(trilho);
        marcador.Children.Add(circulo);
        marcador.Children.Add(simbolo);
        marcador.Children.Add(anel);
        Children.Add(marcador);

        fundoIcone.Child = icone;
        var textos = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(titulo);
        textos.Children.Add(detalhe);
        var conteudo = new Grid { ColumnSpacing = 12 };
        conteudo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        conteudo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        conteudo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(textos, 1);
        SetColumn(hora, 2);
        conteudo.Children.Add(fundoIcone);
        conteudo.Children.Add(textos);
        conteudo.Children.Add(hora);
        cartao.Child = conteudo;
        SetColumn(cartao, 1);
        Children.Add(cartao);
    }

    static Brush Recurso(string chave) => (Brush)Application.Current.Resources[chave];

    void Montar()
    {
        if (Item is not { } a) return;
        var erro = a.Tipo == TipoAtividade.Erro;
        var mensagem = a.Tipo is TipoAtividade.Mensagem or TipoAtividade.Texto;

        icone.Glyph = a.Glifo;
        titulo.Text = a.Titulo;
        detalhe.Text = a.Detalhe ?? "";
        detalhe.Visibility = string.IsNullOrEmpty(a.Detalhe) ? Visibility.Collapsed : Visibility.Visible;
        detalhe.TextWrapping = mensagem || erro ? TextWrapping.Wrap : TextWrapping.NoWrap;
        detalhe.TextTrimming = mensagem || erro ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        detalhe.Foreground = erro ? Recurso("SystemFillColorCriticalBrush") : mensagem ? Recurso("TextFillColorPrimaryBrush") : Recurso("TextFillColorSecondaryBrush");
        hora.Text = a.HoraTexto;
        hora.Foreground = Recurso("TextFillColorSecondaryBrush");

        cartao.Background = Recurso(erro ? "SystemFillColorCriticalBackgroundBrush" : "SubtleFillColorSecondaryBrush");
        cartao.BorderBrush = erro ? Recurso("SystemFillColorCriticalBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        fundoIcone.Background = Recurso("ControlFillColorDefaultBrush");
        icone.Foreground = erro ? Recurso("SystemFillColorCriticalBrush") : mensagem ? Recurso("AccentTextFillColorPrimaryBrush") : Recurso("TextFillColorSecondaryBrush");
        trilho.Fill = Recurso("DividerStrokeColorDefaultBrush");

        anel.IsActive = EmCurso && !erro;
        anel.Visibility = EmCurso && !erro ? Visibility.Visible : Visibility.Collapsed;
        circulo.Visibility = simbolo.Visibility = EmCurso && !erro ? Visibility.Collapsed : Visibility.Visible;
        circulo.Fill = Recurso(erro ? "SystemFillColorCriticalBrush" : "SystemFillColorSuccessBrush");
        simbolo.Glyph = erro ? "" : "";
        simbolo.Foreground = Recurso("TextOnAccentFillColorPrimaryBrush");
        cartao.BorderBrush = EmCurso && !erro ? Recurso("AccentFillColorDefaultBrush") : cartao.BorderBrush;
        AutomationPropertiesNome($"{a.Titulo}. {a.Detalhe} {(EmCurso ? "Em andamento" : erro ? "Erro" : "Concluída")} {a.HoraTexto}");
    }

    void AutomationPropertiesNome(string s) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, s.Trim());
}
