using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Página Novo despacho (desenho §10.4): só a parte visual e o desenho da prévia, dos cartões das exigências e das páginas do PDF.
/// Quem decide e grava é a janela (<c>MainWindow.NovoDespacho.cs</c>) com a <see cref="SessaoNovoDespacho"/>.</summary>
public sealed partial class PaginaNovoDespacho : UserControl
{
    static readonly FontFamily Verdana = new("Verdana");
    static readonly SolidColorBrush Preto = new(Colors.Black);
    static readonly SolidColorBrush Vermelho = new(Windows.UI.Color.FromArgb(255, 0xC4, 0x2B, 0x1C));
    const string Marca = "[preencher: ";

    IReadOnlyList<ParagrafoPrevia> previa = [];
    readonly List<CheckBox> caixasPaginas = [];

    public PaginaNovoDespacho() => InitializeComponent();

    /// <summary>O texto da prévia como está na tela, um parágrafo por linha (o roteiro confere).</summary>
    internal string TextoDaPrevia => string.Join("\n", previa.Select(p => p.Texto));

    internal IReadOnlyList<ParagrafoPrevia> ParagrafosDaPrevia => previa;

    /// <summary>As caixas "Conferi a página N", na ordem das páginas.</summary>
    internal IReadOnlyList<CheckBox> CaixasDasPaginas => caixasPaginas;

    internal IReadOnlyList<string> LinhasFaltando { get; private set; } = [];

    /// <summary>Os parágrafos na ordem do DOCX: o que falta aparece como "[preencher: …]" em vermelho (só aqui, nunca no DOCX).</summary>
    /// <summary>Edição direta na prévia (correções 2026-11-15): a chave do campo (<see cref="ParagrafoPrevia.Chave"/>) e o texto novo.</summary>
    internal event Action<string, string>? PreviaEditada;

    internal void MostrarPrevia(IReadOnlyList<ParagrafoPrevia> paragrafos)
    {
        previa = paragrafos;
        Previa.Children.Clear();
        foreach (var p in paragrafos)
        {
            var alinhamento = p.Estilo switch
            {
                EstiloPrevia.Titulo or EstiloPrevia.Assinatura => TextAlignment.Center,
                EstiloPrevia.Data => TextAlignment.Right,
                EstiloPrevia.Corpo => TextAlignment.Justify,
                _ => TextAlignment.Left,
            };
            var peso = p.Estilo is EstiloPrevia.Titulo or EstiloPrevia.Grupo or EstiloPrevia.Destinatario ? FontWeights.Bold : FontWeights.Normal;
            if (p.Chave is { } chave)
            {
                Previa.Children.Add(CampoEditavel(p, chave, alinhamento, peso));
                continue;
            }
            var t = new TextBlock
            {
                FontFamily = Verdana, FontSize = 12, Foreground = Preto, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                TextAlignment = alinhamento, FontWeight = peso,
            };
            if (p.Estilo == EstiloPrevia.Vazio) t.Text = " ";
            else Trechos(t, p.Texto);
            Previa.Children.Add(t);
        }
    }

    /// <summary>Um campo da prévia que se edita como num editor de texto: sem borda, na fonte da folha; grava ao sair do campo.</summary>
    FrameworkElement CampoEditavel(ParagrafoPrevia p, string chave, TextAlignment alinhamento, Windows.UI.Text.FontWeight peso)
    {
        var valor = p.Valor ?? "";
        var rotulo = "";
        if (p.Estilo == EstiloPrevia.Rotulo)
        {
            var corte = valor.Length > 0 && p.Texto.EndsWith(valor, StringComparison.Ordinal) ? p.Texto.Length - valor.Length : p.Texto.IndexOf(Marca, StringComparison.Ordinal);
            rotulo = corte > 0 ? p.Texto[..corte] : "";
        }
        var caixa = new TextBox
        {
            Text = valor, FontFamily = Verdana, FontSize = 12, Foreground = Preto, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false,
            TextAlignment = alinhamento, FontWeight = peso, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(2, 0, 2, 0), MinHeight = 0,
            Background = new SolidColorBrush(Colors.Transparent), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 0, 0, 0)),
            PlaceholderText = p.Faltando is { } f ? Marca + f + "]" : "",
            RequestedTheme = ElementTheme.Light,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixa, p.Faltando ?? chave);
        caixa.LostFocus += (_, _) => { if (caixa.Text != valor) PreviaEditada?.Invoke(chave, caixa.Text); };
        if (rotulo.Length == 0) return caixa;
        var g = new Grid { ColumnSpacing = 2 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = rotulo, FontFamily = Verdana, FontSize = 12, Foreground = Preto, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(caixa, 1);
        g.Children.Add(caixa);
        return g;
    }

    /// <summary>Divide o texto nos "[preencher: …]" e pinta só esses trechos de vermelho.</summary>
    static void Trechos(TextBlock t, string texto)
    {
        var i = 0;
        while (i < texto.Length)
        {
            var ini = texto.IndexOf(Marca, i, StringComparison.Ordinal);
            var fim = ini < 0 ? -1 : texto.IndexOf(']', ini);
            if (ini < 0 || fim < 0)
            {
                t.Inlines.Add(new Run { Text = texto[i..] });
                return;
            }
            if (ini > i) t.Inlines.Add(new Run { Text = texto[i..ini] });
            t.Inlines.Add(new Run { Text = texto[ini..(fim + 1)], Foreground = Vermelho, FontWeight = FontWeights.Bold });
            i = fim + 1;
        }
    }

    internal void MostrarFaltando(IReadOnlyList<string> faltando)
    {
        LinhasFaltando = faltando;
        PainelFaltando.Visibility = faltando.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextoFaltando.Text = string.Join(Environment.NewLine, faltando.Select(f => "• " + f));
    }

    /// <summary>Um cartão por exigência (correção): marcar/desmarcar, "ITEM 05 · NÃO ATENDE · título", grupo, texto e fundamento editáveis em
    /// texto puro de uma linha, mover acima/abaixo e o original do relatório para consulta.</summary>
    internal void MostrarExigencias(RascunhoDespacho r, Action<string, bool> marcar, Action<string, string, string> definir, Action<string, int> mover)
    {
        PainelExigencias.Children.Clear();
        if (r.Tipo != TipoDespacho.Correcao) return;
        PainelExigencias.Children.Add(new TextBlock { Text = "Exigências", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        for (var i = 0; i < r.Pendencias.Count; i++)
        {
            var p = r.Pendencias[i];
            var id = p.ItemId;
            var cartao = new StackPanel { Spacing = 6 };
            var cabeca = new Grid { ColumnSpacing = 4 };
            cabeca.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cabeca.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cabeca.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var marcada = new CheckBox
            {
                IsChecked = p.Incluida, Tag = id,
                Content = new TextBlock
                {
                    Text = $"ITEM {p.Numero:D2} · {TextoSituacao(p.Situacao)} · {p.Titulo}", TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                },
            };
            marcada.Checked += (_, _) => marcar(id, true);
            marcada.Unchecked += (_, _) => marcar(id, false);
            cabeca.Children.Add(marcada);
            var acima = BotaoMover("", "Mover acima", i > 0, () => mover(id, -1));
            Grid.SetColumn(acima, 1);
            cabeca.Children.Add(acima);
            var abaixo = BotaoMover("", "Mover abaixo", i < r.Pendencias.Count - 1, () => mover(id, 1));
            Grid.SetColumn(abaixo, 2);
            cabeca.Children.Add(abaixo);
            cartao.Children.Add(cabeca);
            cartao.Children.Add(Caixa("Grupo", p.Grupo, t => definir(id, "grupo", t)));
            cartao.Children.Add(Caixa("Texto da exigência", p.Texto, t => definir(id, "texto", t)));
            cartao.Children.Add(Caixa("Fundamento", p.Fundamento, t => definir(id, "fundamento", t)));
            cartao.Children.Add(new TextBlock
            {
                Text = "Original do relatório: " + p.TextoOriginal + (p.FundamentoOriginal.Length > 0 ? " (" + p.FundamentoOriginal + ")" : ""),
                TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"],
            });
            PainelExigencias.Children.Add(new Border
            {
                Child = cartao, Padding = new Thickness(12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            });
        }
    }

    static string TextoSituacao(string codigo) =>
        Pmvni.Composicao.Kit.Situacoes.Sit.TryGetValue(codigo, out var s) ? s.Texto : codigo;

    static Button BotaoMover(string glifo, string nome, bool habilitado, Action acao)
    {
        var b = new Button
        {
            Content = new FontIcon { Glyph = glifo, FontSize = 12 }, IsEnabled = habilitado, Padding = new Thickness(8, 4, 8, 4),
            Style = (Style)Application.Current.Resources["PmvniBarraButtonStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, nome);
        ToolTipService.SetToolTip(b, nome);
        b.Click += (_, _) => acao();
        return b;
    }

    static TextBox Caixa(string rotulo, string valor, Action<string> mudou)
    {
        var t = new TextBox { Header = rotulo, Text = valor, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(t, rotulo);
        t.TextChanged += (_, _) => mudou(t.Text);
        return t;
    }

    /// <summary>Passo 1 e 2 à vista (o 2 só com o rascunho pronto) ou o passo 3.</summary>
    internal void MostrarPasso(PassoNovoDespacho passo, bool soPdf)
    {
        var conferencia = passo == PassoNovoDespacho.Conferencia;
        PainelEdicao.Visibility = conferencia ? Visibility.Collapsed : Visibility.Visible;
        PainelConferencia.Visibility = conferencia ? Visibility.Visible : Visibility.Collapsed;
        PainelConteudo.Visibility = passo == PassoNovoDespacho.Conteudo ? Visibility.Visible : Visibility.Collapsed;
        BotaoGerar.Visibility = conferencia ? Visibility.Collapsed : Visibility.Visible;
        BotaoGravar.Visibility = conferencia ? Visibility.Visible : Visibility.Collapsed;
        BotaoVoltar.Visibility = conferencia && !soPdf ? Visibility.Visible : Visibility.Collapsed;
        if (passo != PassoNovoDespacho.Conteudo) MostrarFaltando([]);
        if (passo == PassoNovoDespacho.Origem) { Previa.Children.Clear(); previa = []; BotaoGerar.IsEnabled = false; }
    }

    /// <summary>A página limpa, para um despacho novo (ou para o Gerar PDF de um que já existe).</summary>
    internal void Reiniciar(string titulo)
    {
        TextoTitulo.Text = titulo;
        TextoTituloConferencia.Text = "3. Conferência";
        AvisoBloqueio.IsOpen = false;
        AvisoMensagem.IsOpen = false;
        LimparConferencia();
        PainelExigencias.Children.Clear();
        MostrarPasso(PassoNovoDespacho.Origem, false);
    }

    internal void LimparConferencia()
    {
        AvisoErros.IsOpen = false;
        AvisoSemPdf.IsOpen = false;
        TextoFontes.Text = "";
        TextoPaginas.Text = "";
        BotaoMarcarTodas.Visibility = Visibility.Collapsed;
        ListaPaginas.Children.Clear();
        caixasPaginas.Clear();
        BotaoGravar.IsEnabled = false;
        BotaoGravar.Content = "Gravar na pasta do processo";
    }

    internal void MostrarBloqueios(IReadOnlyList<string> bloqueios)
    {
        AvisoBloqueio.Title = bloqueios.Count == 1 ? "O despacho não pode ser emitido" : $"O despacho não pode ser emitido ({bloqueios.Count} motivos)";
        AvisoBloqueio.Message = string.Join(Environment.NewLine, bloqueios);
        AvisoBloqueio.IsOpen = bloqueios.Count > 0;
    }

    /// <summary>Cada página do PDF (600 DIP de largura) com a sua caixa "Conferi a página N".</summary>
    internal void MostrarPaginas(IReadOnlyList<ImageSource> paginas, Action<int, bool> conferida)
    {
        ListaPaginas.Children.Clear();
        caixasPaginas.Clear();
        for (var i = 0; i < paginas.Count; i++)
        {
            var n = i + 1;
            var caixa = new CheckBox { Content = $"Conferi a página {n}" };
            caixa.Checked += (_, _) => conferida(n, true);
            caixa.Unchecked += (_, _) => conferida(n, false);
            caixasPaginas.Add(caixa);
            var bloco = new StackPanel { Spacing = 6 };
            bloco.Children.Add(new Border
            {
                Child = new Image { Source = paginas[i], Width = 600, Stretch = Stretch.Uniform },
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x33, 0, 0, 0)), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Colors.White),
            });
            bloco.Children.Add(caixa);
            ListaPaginas.Children.Add(bloco);
        }
        BotaoMarcarTodas.Visibility = paginas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextoPaginas.Text = paginas.Count == 1 ? "Confira a página do PDF antes de gravar." : $"Confira as {paginas.Count} páginas do PDF antes de gravar.";
    }

    internal void MostrarEspera(string? texto, bool interromper)
    {
        Espera.Visibility = texto is null ? Visibility.Collapsed : Visibility.Visible;
        Anel.IsActive = texto is not null;
        TextoEspera.Text = texto ?? "";
        BotaoInterromper.Visibility = interromper ? Visibility.Visible : Visibility.Collapsed;
        BotaoCancelar.IsEnabled = BotaoVoltar.IsEnabled = texto is null;
        if (texto is not null) BotaoGerar.IsEnabled = BotaoGravar.IsEnabled = false;
    }
}
