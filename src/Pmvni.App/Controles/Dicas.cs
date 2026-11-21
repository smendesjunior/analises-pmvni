using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>O formato único das dicas do app (correções 2026-11-15): título em negrito com o nome do botão e o atalho entre parênteses,
/// embaixo uma frase curta dizendo o que ele faz e, se o botão está desabilitado, o motivo em itálico. A dica fica no
/// <see cref="ToolTip.Tag"/> como <see cref="TextoDica"/> (o roteiro confere por <see cref="De"/>), e a descrição vai também para o
/// <see cref="AutomationProperties.HelpTextProperty"/> (o Narrador lê).
/// <para>Em XAML: <c>local:Dicas.Descricao="Abre …"</c> (o título vem do <c>AutomationProperties.Name</c> ou do texto do botão);
/// opcionais <c>local:Dicas.Titulo</c> e <c>local:Dicas.Atalho</c>. Em código: <see cref="Aplicar(DependencyObject, TextoDica)"/>.</para></summary>
public static class Dicas
{
    /// <summary>Uma dica no formato do app.</summary>
    public static ToolTip Criar(TextoDica d)
    {
        var painel = new StackPanel { Spacing = 2, MaxWidth = 340 };
        painel.Children.Add(new TextBlock { Text = d.Titulo, Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"], TextWrapping = TextWrapping.Wrap });
        if (d.Descricao.Length > 0) painel.Children.Add(new TextBlock { Text = d.Descricao, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
        if (d.Motivo is { Length: > 0 } motivo)
            painel.Children.Add(new TextBlock { Text = motivo, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"], FontStyle = Windows.UI.Text.FontStyle.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return new ToolTip { Content = painel, Tag = d };
    }

    /// <summary>Põe a dica no elemento (e a descrição como texto de ajuda acessível).</summary>
    public static void Aplicar(DependencyObject alvo, TextoDica d)
    {
        ToolTipService.SetToolTip(alvo, Criar(d));
        AutomationProperties.SetHelpText(alvo, d.Motivo is { Length: > 0 } m ? $"{d.Descricao} {m}".Trim() : d.Descricao);
    }

    /// <summary>Atalho: dica livre com nome, descrição e, se houver, atalho e motivo.</summary>
    public static void Aplicar(DependencyObject alvo, string nome, string descricao, string? atalho = null, string? motivo = null) =>
        Aplicar(alvo, DicasComandos.Livre(nome, descricao, atalho, motivo));

    /// <summary>A dica de um comando de <see cref="ComandosApp"/> (descrição da tabela e atalho do módulo).</summary>
    public static void AplicarComando(DependencyObject alvo, string id, string nome, Modulo m = Modulo.Relatorios, string? motivo = null) =>
        Aplicar(alvo, DicasComandos.DoComando(id, nome, m, motivo));

    /// <summary>A dica no formato do app que está no elemento, ou null (sem dica, ou dica de texto simples).</summary>
    public static TextoDica? De(DependencyObject alvo) => ToolTipService.GetToolTip(alvo) is ToolTip { Tag: TextoDica d } ? d : null;

    /// <summary>O texto da dica do elemento, qualquer que seja o formato (texto simples ou o do app), ou "".</summary>
    public static string Texto(DependencyObject alvo) => ToolTipService.GetToolTip(alvo) switch
    {
        ToolTip { Tag: TextoDica d } => d.ToString(),
        string s => s,
        ToolTip { Content: string s } => s,
        _ => "",
    };

    // ---------- propriedades anexadas (XAML) ----------

    public static readonly DependencyProperty DescricaoProperty =
        DependencyProperty.RegisterAttached("Descricao", typeof(string), typeof(Dicas), new PropertyMetadata(null, AoMudar));

    public static readonly DependencyProperty TituloProperty =
        DependencyProperty.RegisterAttached("Titulo", typeof(string), typeof(Dicas), new PropertyMetadata(null, AoMudar));

    public static readonly DependencyProperty AtalhoProperty =
        DependencyProperty.RegisterAttached("Atalho", typeof(string), typeof(Dicas), new PropertyMetadata(null, AoMudar));

    public static string GetDescricao(DependencyObject o) => (string)o.GetValue(DescricaoProperty);
    public static void SetDescricao(DependencyObject o, string v) => o.SetValue(DescricaoProperty, v);
    public static string GetTitulo(DependencyObject o) => (string)o.GetValue(TituloProperty);
    public static void SetTitulo(DependencyObject o, string v) => o.SetValue(TituloProperty, v);
    public static string GetAtalho(DependencyObject o) => (string)o.GetValue(AtalhoProperty);
    public static void SetAtalho(DependencyObject o, string v) => o.SetValue(AtalhoProperty, v);

    static void AoMudar(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        AplicarAnexada(o);
        // O nome acessível e o texto do botão podem vir depois no XAML (ou mudar no código): refaz ao carregar.
        if (o is FrameworkElement fe && !fe.IsLoaded)
        {
            void Carregou(object s, RoutedEventArgs a) { fe.Loaded -= Carregou; AplicarAnexada(fe); }
            fe.Loaded += Carregou;
        }
    }

    static void AplicarAnexada(DependencyObject o)
    {
        if (GetDescricao(o) is not { Length: > 0 } descricao) return;
        Aplicar(o, NomeDe(o), descricao, GetAtalho(o));
        // O nome acessível pode vir de um x:Bind que muda depois (ex.: "Recolher seção" ⇄ "Expandir seção"): a dica se refaz ao abrir.
        if (ToolTipService.GetToolTip(o) is ToolTip tt)
            tt.Opened += (_, _) =>
            {
                if (GetDescricao(o) is not { Length: > 0 } atual) return;
                var d = DicasComandos.Livre(NomeDe(o), atual, GetAtalho(o));
                if (tt.Tag is TextoDica antes && antes == d) return;
                var novo = Criar(d);
                var conteudo = novo.Content;
                novo.Content = null;
                tt.Content = conteudo;
                tt.Tag = d;
            };
    }

    /// <summary>O nome do botão para o título: o <c>Dicas.Titulo</c>, o nome acessível, o texto do conteúdo ou o primeiro texto dentro dele.</summary>
    static string NomeDe(DependencyObject o)
    {
        if (GetTitulo(o) is { Length: > 0 } t) return t;
        if (AutomationProperties.GetName(o) is { Length: > 0 } n) return n;
        return o switch
        {
            ContentControl { Content: string s } when s.Length > 0 => s,
            ContentControl { Content: DependencyObject d } when PrimeiroTexto(d) is { Length: > 0 } s => s,
            _ => "",
        };
    }

    static string? PrimeiroTexto(DependencyObject d)
    {
        if (d is TextBlock tb && tb.Text.Length > 0) return tb.Text;
        if (d is Panel p)
            foreach (var c in p.Children)
                if (PrimeiroTexto(c) is { } s) return s;
        return null;
    }
}
