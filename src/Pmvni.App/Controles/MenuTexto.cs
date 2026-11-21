using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;

namespace Pmvni.App;

/// <summary>Menu do texto selecionável (2026-11-17): o menu padrão do WinUI para o TextBlock (barra translúcida "Copiar / Selecionar
/// Tudo") destoava dos outros menus do app. Aqui ele vira um MenuFlyout comum, com ícones e atalhos, igual aos demais: Copiar (a
/// seleção ou, sem seleção, o parágrafo inteiro) e Selecionar tudo. A barrinha que aparece ao selecionar também sai.</summary>
static class MenuTexto
{
    /// <summary>Um menu só para todos os textos: quem abriu é o <see cref="FlyoutBase.Target"/>.</summary>
    static MenuFlyout? menu;

    /// <summary>Aplica o menu a todo TextBlock selecionável dentro de <paramref name="raiz"/> (percorre painéis, bordas e conteúdos).</summary>
    public static void Aplicar(DependencyObject? raiz)
    {
        switch (raiz)
        {
            case TextBlock tb when tb.IsTextSelectionEnabled:
                tb.ContextFlyout = Menu();
                tb.SelectionFlyout = null;
                break;
            case Panel p:
                foreach (var c in p.Children) Aplicar(c);
                break;
            case Border b:
                Aplicar(b.Child);
                break;
            case ContentControl cc:
                Aplicar(cc.Content as DependencyObject);
                break;
            case Viewbox v:
                Aplicar(v.Child);
                break;
        }
    }

    static MenuFlyout Menu()
    {
        if (menu is not null) return menu;
        var m = new MenuFlyout();
        var copiar = new MenuFlyoutItem { Text = "Copiar", Icon = new FontIcon { Glyph = Glifos.Copiar, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, KeyboardAcceleratorTextOverride = "Ctrl+C" };
        var tudo = new MenuFlyoutItem { Text = "Selecionar tudo", Icon = new FontIcon { Glyph = Glifos.SelecionarTudo, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, KeyboardAcceleratorTextOverride = "Ctrl+A" };
        m.Items.Add(tudo);
        m.Items.Add(new MenuFlyoutSeparator());
        m.Items.Add(copiar);
        m.Opening += (_, _) =>
        {
            var tb = m.Target as TextBlock;
            copiar.Text = tb is not null && string.IsNullOrEmpty(tb.SelectedText) ? "Copiar parágrafo" : "Copiar";
        };
        copiar.Click += (_, _) =>
        {
            if (m.Target is not TextBlock tb) return;
            var texto = string.IsNullOrEmpty(tb.SelectedText) ? TextoDe(tb) : tb.SelectedText;
            if (texto.Length == 0) return;
            var pacote = new DataPackage();
            pacote.SetText(texto);
            Clipboard.SetContent(pacote);
        };
        tudo.Click += (_, _) => (m.Target as TextBlock)?.SelectAll();
        return menu = m;
    }

    static string TextoDe(TextBlock tb)
    {
        if (!string.IsNullOrEmpty(tb.Text)) return tb.Text;
        var sb = new System.Text.StringBuilder();
        foreach (var i in tb.Inlines)
            if (i is Microsoft.UI.Xaml.Documents.Run r) sb.Append(r.Text);
            else if (i is Microsoft.UI.Xaml.Documents.LineBreak) sb.AppendLine();
        return sb.ToString();
    }
}
