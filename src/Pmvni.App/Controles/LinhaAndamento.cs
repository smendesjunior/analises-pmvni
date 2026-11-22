using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Item da lista de atividades: a atividade e se ela é a que está em curso (a última, enquanto a análise roda).</summary>
public sealed class ItemAtividade(Atividade atividade) : INotifyPropertyChanged
{
    bool emCurso;
    public Atividade Atividade { get; } = atividade;
    public bool EmCurso { get => emCurso; set { if (emCurso == value) return; emCurso = value; PropertyChanged?.Invoke(this, new(nameof(EmCurso))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Uma entrada do log do Monitor, em ordem de chegada, com a hora e o marcador da atividade em curso.</summary>
public sealed class LinhaAndamento : Grid
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(Atividade), typeof(LinhaAndamento), new PropertyMetadata(null, (d, _) => ((LinhaAndamento)d).Montar()));
    public static readonly DependencyProperty EmCursoProperty = DependencyProperty.Register(
        nameof(EmCurso), typeof(bool), typeof(LinhaAndamento), new PropertyMetadata(false, (d, _) => ((LinhaAndamento)d).Montar()));

    public Atividade? Item { get => (Atividade?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public bool EmCurso { get => (bool)GetValue(EmCursoProperty); set => SetValue(EmCursoProperty, value); }

    readonly TextBlock ponto = new() { Text = "●", TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border cartao = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 6, 8, 6) };
    readonly TextBlock titulo = new() { Style = Estilo("BodyStrongTextBlockStyle"), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock detalhe = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    readonly TextBlock hora = new() { Style = Estilo("PmvniPainelRotulo") };

    public LinhaAndamento()
    {
        Margin = new Thickness(0, 0, 0, 5);
        AutomationProperties.SetAccessibilityView(ponto, AccessibilityView.Raw);
        var marcador = new StackPanel { Spacing = 4, MinWidth = 41 };
        marcador.Children.Add(hora);
        marcador.Children.Add(ponto);
        var textos = new StackPanel { Spacing = 2 };
        textos.Children.Add(titulo);
        textos.Children.Add(detalhe);
        var conteudo = new Grid { ColumnSpacing = 6 };
        conteudo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        conteudo.ColumnDefinitions.Add(new ColumnDefinition());
        SetColumn(textos, 1);
        conteudo.Children.Add(marcador);
        conteudo.Children.Add(textos);
        cartao.Child = conteudo;
        Children.Add(cartao);
    }

    static Style Estilo(string chave) => (Style)Application.Current.Resources[chave];

    void Montar()
    {
        if (Item is not { } a) return;
        var erro = a.Tipo == TipoAtividade.Erro;
        titulo.Text = a.Titulo;
        detalhe.Text = a.Detalhe ?? "";
        detalhe.Visibility = string.IsNullOrEmpty(a.Detalhe) ? Visibility.Collapsed : Visibility.Visible;
        detalhe.Style = Estilo(erro ? "PmvniTextoErroStyle" : "PmvniSecondaryTextStyle");
        detalhe.TextAlignment = TextAlignment.Left;
        hora.Text = a.HoraTexto.Length == 0 ? "—" : a.HoraTexto;

        cartao.Style = Estilo(erro ? "PmvniSeloStatusErroStyle" : "PmvniCartaoCompactoStyle");
        ponto.Style = Estilo(EmCurso ? "PmvniTextoDestaqueStyle" : "PmvniSecondaryTextStyle");

        var textoCompleto = string.IsNullOrEmpty(a.Detalhe) ? a.Titulo : $"{a.Titulo} · {a.Detalhe}";
        ToolTipService.SetToolTip(this, new TextBlock { Text = textoCompleto, MaxWidth = 430, TextWrapping = TextWrapping.Wrap });
        var status = EmCurso ? "Em andamento" : erro ? "Erro" : null;
        AutomationProperties.SetName(this, string.Join(" · ", new[] { a.HoraTexto, textoCompleto, status }.Where(s => !string.IsNullOrEmpty(s))));
    }
}
