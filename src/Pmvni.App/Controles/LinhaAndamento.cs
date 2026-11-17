using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>Uma linha do andamento da análise (2026-11-17, revisão de interface): ícone pelo tipo de ação, sem os prefixos "Mensagem:"/"Ação:".
/// Mensagens do agente em destaque (texto normal, quebrando linha, faixa de acento à esquerda); ações discretas (texto secundário, uma linha);
/// erros em vermelho.</summary>
public sealed class LinhaAndamento : Grid
{
    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(LinhaAndamento), new PropertyMetadata(null, (d, _) => ((LinhaAndamento)d).Montar()));

    public string? Texto { get => (string?)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }

    readonly FontIcon icone = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
    readonly TextBlock texto = new() { IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Top };
    readonly Border faixa = new() { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 10, 2) };

    public LinhaAndamento()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumn(icone, 1);
        SetColumn(texto, 2);
        Children.Add(faixa);
        Children.Add(icone);
        Children.Add(texto);
    }

    static Brush Recurso(string chave) => (Brush)Application.Current.Resources[chave];

    void Montar()
    {
        var t = Texto ?? "";
        string corpo;
        string glifo;
        bool mensagem = false, erro = false;
        if (t.StartsWith("Mensagem: ", StringComparison.Ordinal)) { corpo = t[10..]; glifo = ""; mensagem = true; }
        else if (t.StartsWith("Erro: ", StringComparison.Ordinal)) { corpo = t[6..]; glifo = ""; erro = true; }
        else if (t.StartsWith("Ação: ", StringComparison.Ordinal)) { corpo = t[6..]; glifo = GlifoDaAcao(corpo); }
        else { corpo = t; glifo = ""; }
        if (corpo.Length > 0) corpo = char.ToUpperInvariant(corpo[0]) + corpo[1..];

        icone.Glyph = glifo;
        texto.Text = corpo;
        faixa.Background = mensagem ? Recurso("AccentFillColorDefaultBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        icone.Foreground = erro ? Recurso("SystemFillColorCriticalBrush") : mensagem ? Recurso("AccentTextFillColorPrimaryBrush") : Recurso("TextFillColorSecondaryBrush");
        texto.Foreground = erro ? Recurso("SystemFillColorCriticalBrush") : mensagem ? Recurso("TextFillColorPrimaryBrush") : Recurso("TextFillColorSecondaryBrush");
        texto.FontSize = mensagem ? 14 : 13;
        texto.TextWrapping = mensagem || erro ? TextWrapping.Wrap : TextWrapping.NoWrap;
        texto.TextTrimming = mensagem || erro ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        texto.FontWeight = mensagem ? FontWeights.Normal : FontWeights.Normal;
        Margin = mensagem ? new Thickness(0, 8, 0, 6) : new Thickness(0, 1, 0, 1);
        ToolTipService.SetToolTip(this, mensagem ? null : corpo);
    }

    /// <summary>O ícone (Segoe Fluent Icons) pela descrição da ação (EventosCodex.DescreverComando).</summary>
    internal static string GlifoDaAcao(string a)
    {
        a = a.ToLowerInvariant();
        if (a.Contains("legislação")) return "";      // livro
        if (a.Contains("índice") || a.Contains("pesquis")) return "";   // lupa
        if (a.Contains("mapa")) return "";            // marcador de mapa
        if (a.Contains("inspecion") || a.Contains("evidências")) return "";   // olho
        if (a.Contains("listando")) return "";        // pasta
        if (a.Contains("gravou")) return "";          // disquete
        if (a.Contains("créditos") || a.Contains("consultou")) return "";
        if (a.Contains("extraindo") || a.Contains("organizando") || a.Contains("posições")) return "";   // lista
        if (a.Contains("lendo")) return "";           // documento
        return "";                                    // executar
    }
}
