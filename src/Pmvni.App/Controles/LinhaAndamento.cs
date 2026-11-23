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

/// <summary>Uma entrada do log do Monitor, em texto simples (3.3.0, pedido do usuário): "23:36  Título — detalhe" numa linha que quebra se
/// preciso, sem cartão. A atividade em curso fica na cor de destaque; a de erro, na cor crítica.</summary>
public sealed class LinhaAndamento : Grid
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(Atividade), typeof(LinhaAndamento), new PropertyMetadata(null, (d, _) => ((LinhaAndamento)d).Montar()));
    public static readonly DependencyProperty EmCursoProperty = DependencyProperty.Register(
        nameof(EmCurso), typeof(bool), typeof(LinhaAndamento), new PropertyMetadata(false, (d, _) => ((LinhaAndamento)d).Montar()));

    public Atividade? Item { get => (Atividade?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public bool EmCurso { get => (bool)GetValue(EmCursoProperty); set => SetValue(EmCursoProperty, value); }

    readonly TextBlock hora = new() { Style = Estilo("PmvniSecondaryTextStyle"), MinWidth = 40, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock tipo = new() { Style = Estilo("PmvniPainelRotulo"), VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock texto = new() { Style = Estilo("BodyTextBlockStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    readonly Border selo = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(6, 2, 6, 2) };

    public LinhaAndamento()
    {
        Padding = new Thickness(8, 8, 0, 8);
        ColumnSpacing = 8;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Insert(1, new ColumnDefinition { Width = new GridLength(90) });
        selo.Child = tipo;
        SetColumn(selo, 1);
        SetColumn(texto, 2);
        Children.Add(selo);
        Children.Add(hora);
        Children.Add(texto);
    }

    static Style Estilo(string chave) => (Style)Application.Current.Resources[chave];

    internal static (string Nome, string Estilo) Classificar(Atividade a) => a switch
        {
            { Tipo: TipoAtividade.Erro } => ("ERRO", "PmvniSeloErro"),
            { Titulo: "Análise concluída" } or { Detalhe: "Etapa concluída." } => ("CONCLUÍDO", "PmvniSeloSucesso"),
            { Titulo: "A revisão identificou pendências" } or { Detalhe: "A revisão identificou pontos que precisam de correção." }
                or { Detalhe: "A execução foi pausada nesta etapa." } => ("ATENÇÃO", "PmvniSeloAtencao"),
            { Tipo: TipoAtividade.Mensagem } => ("IA", "PmvniSeloNeutro"),
            { Tipo: TipoAtividade.Acao } => ("AÇÃO", "PmvniSeloNeutro"),
            _ => ("SISTEMA", "PmvniSeloNeutro"),
        };

    void Montar()
    {
        if (Item is not { } a) return;
        var erro = a.Tipo == TipoAtividade.Erro;
        var (nome, estilo) = Classificar(a);
        tipo.Style = Estilo(a.Tipo == TipoAtividade.Mensagem ? "PmvniTextoDestaqueStyle" : "PmvniPainelRotulo");
        tipo.Text = nome;
        selo.Style = Estilo(estilo);
        hora.Text = a.HoraTexto.Length == 0 ? "—" : a.HoraTexto;
        ToolTipService.SetToolTip(hora, a.Hora is null ? "Horário não registrado" : a.Hora.Value.ToString("g"));
        texto.Inlines.Clear();
        var titulo = new Microsoft.UI.Xaml.Documents.Run { Text = a.Titulo };
        texto.Style = Estilo(erro ? "PmvniTextoErroStyle" : EmCurso ? "PmvniTextoDestaqueStyle" : "BodyTextBlockStyle");
        texto.TextWrapping = TextWrapping.Wrap;
        texto.Inlines.Add(titulo);
        if (!string.IsNullOrEmpty(a.Detalhe))
        {
            var detalhe = new Microsoft.UI.Xaml.Documents.Run { Text = " — " + a.Detalhe };
            if (!erro) detalhe.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            texto.Inlines.Add(detalhe);
        }

        var textoCompleto = string.IsNullOrEmpty(a.Detalhe) ? a.Titulo : $"{a.Titulo} · {a.Detalhe}";
        var status = EmCurso ? "Em andamento" : erro ? "Erro" : null;
        AutomationProperties.SetName(this, string.Join(" · ", new[] { a.Hora is null ? "Horário não registrado" : a.HoraTexto, nome, textoCompleto, status }.Where(s => !string.IsNullOrEmpty(s))));
    }
}
