using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Legislação › aba Leis (lista nova, 2026-11-16): cartões das esferas (Federal, Estadual, Municipal) com a contagem, botões de
/// tipo (Todas, Leis, Decretos, LC, NT), um cartão por lei (ícone, título, ementa, selo do tipo, páginas, aviso e estrela de favorita) e,
/// no cartão da lei aberta, os tópicos com os artigos resumidos (clicar leva o texto até o artigo). A ementa é lida em segundo plano do
/// texto já indexado da lei (nada muda no catálogo); as favoritas ficam nos ajustes e sobem para o topo.</summary>
public sealed partial class MainWindow
{
    Esfera? esferaDaLista;
    TipoNorma? tipoDaLista;
    /// <summary>A ementa de cada lei, pelo sha do PDF (vazio = a lei não tem ementa reconhecível).</summary>
    readonly Dictionary<string, string> ementasLeis = new(StringComparer.Ordinal);
    Task lendoEmentas = Task.CompletedTask;
    StackPanel? topicosNoCartao;
    string? topicosNoCartaoId;

    void IniciarListaDeLeis()
    {
        Raiz.ActualThemeChanged += (_, _) => MontarListaDeLeis();
    }

    /// <summary>A cor do tema do app (claro ou escuro, como a janela está agora) para uma chave de <c>IconesModulos.xaml</c>: os itens desta
    /// lista são montados no código, então a cor é escolhida pelo tema da janela e a lista é remontada quando ele muda.</summary>
    Brush CorDoTema(string chave)
    {
        var tema = Raiz.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var d in Application.Current.Resources.MergedDictionaries)
            if (d.ThemeDictionaries.TryGetValue(tema, out var t) && t is ResourceDictionary rd && rd.TryGetValue(chave, out var v) && v is Brush b) return b;
        return Application.Current.Resources.TryGetValue(chave, out var r) && r is Brush br ? br : new SolidColorBrush(Colors.Gray);
    }

    void MontarListaDeLeis()
    {
        if (fechada) return;
        var favoritas = ajustes.LeisFavoritas.ToHashSet(StringComparer.Ordinal);
        var filtro = P.FiltroLeis.Text;
        var linhas = ListaLegislacao.Filtrar(linhasLegislacao, filtro, esferaDaLista, tipoDaLista, favoritas);
        LeisMostradas = ListaLegislacao.Agrupar(linhas, null);
        MontarCartoesEsferas(filtro);
        MontarBotoesTipos(filtro);

        var lista = P.ListaLeis;
        lista.Items.Clear();
        topicosNoCartao = null;
        topicosNoCartaoId = null;
        FrameworkElement? aberta = null;
        foreach (var l in linhas)
        {
            var cartao = CartaoDaLei(l, favoritas.Contains(l.Id), l.Id == leiAberta?.Id);
            lista.Items.Add(cartao);
            if (l.Id == leiAberta?.Id) aberta = cartao;
        }
        lista.SelectedItem = aberta;
        var vazia = linhasLegislacao.Count == 0;
        P.VazioLeis.Visibility = vazia ? Visibility.Visible : Visibility.Collapsed;
        P.ListaLeis.Visibility = vazia ? Visibility.Collapsed : Visibility.Visible;
        P.NenhumaLeiFiltro.Visibility = !vazia && linhas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        P.CartoesEsferas.Visibility = vazia ? Visibility.Collapsed : Visibility.Visible;
        P.BotoesTipos.Visibility = vazia ? Visibility.Collapsed : Visibility.Visible;
        P.FiltroLeis.IsEnabled = !vazia;
        P.BotaoAnexar.IsEnabled = !legislacao.Acervo.SomenteLeitura && cancelarAnexo is null;
        LerEmentasQueFaltam();
    }

    // ---------- cabeçalho: esferas e tipos ----------

    void MontarCartoesEsferas(string filtro)
    {
        var contagem = ListaLegislacao.ContarEsferas(linhasLegislacao, filtro);
        var grade = P.CartoesEsferas;
        grade.Children.Clear();
        var coluna = 0;
        foreach (var e in ListaLegislacao.EsferasComCartao)
        {
            var nome = e.ToString();
            var fundo = CorDoTema($"PmvniEsfera{nome}Fundo");
            var texto = CorDoTema($"PmvniEsfera{nome}Texto");
            var borda = CorDoTema($"PmvniEsfera{nome}Borda");
            var escolhida = esferaDaLista == e;
            var conteudo = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
            conteudo.Children.Add(new FontIcon { Glyph = "", FontSize = 20, Foreground = escolhida ? texto : borda, Margin = new Thickness(0, 0, 0, 2) });
            conteudo.Children.Add(new TextBlock { Text = Esferas.Rotulo(e), FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = texto, HorizontalAlignment = HorizontalAlignment.Center });
            conteudo.Children.Add(new TextBlock { Text = contagem[e].ToString(System.Globalization.CultureInfo.InvariantCulture), FontWeight = FontWeights.Bold, FontSize = 17, Foreground = texto, HorizontalAlignment = HorizontalAlignment.Center });
            var b = new Button
            {
                Content = conteudo, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(4, 8, 4, 6), CornerRadius = new CornerRadius(8),
                Background = fundo, BorderBrush = escolhida ? borda : new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(2),
            };
            // Cores próprias também ao passar o mouse e ao pressionar (sem o cinza padrão do botão).
            b.Resources["ButtonBackgroundPointerOver"] = fundo;
            b.Resources["ButtonBackgroundPressed"] = fundo;
            b.Resources["ButtonBorderBrushPointerOver"] = borda;
            b.Resources["ButtonBorderBrushPressed"] = borda;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, $"{Esferas.Rotulo(e)}: {contagem[e]} leis{(escolhida ? ", filtro ligado" : "")}");
            ToolTipService.SetToolTip(b, escolhida ? $"Mostrando só as leis da esfera {Esferas.Rotulo(e).ToLowerInvariant()}. Clique de novo para ver todas." : $"Mostrar só as leis da esfera {Esferas.Rotulo(e).ToLowerInvariant()}.");
            var esfera = e;
            b.Click += (_, _) => { esferaDaLista = esferaDaLista == esfera ? null : esfera; tipoDaLista = null; MontarListaDeLeis(); };
            Grid.SetColumn(b, coluna++);
            grade.Children.Add(b);
        }
    }

    void MontarBotoesTipos(string filtro)
    {
        var grade = P.BotoesTipos;
        grade.Children.Clear();
        var coluna = 0;
        foreach (var (tipo, quantidade) in ListaLegislacao.ContarTipos(linhasLegislacao, filtro, esferaDaLista))
        {
            var rotulo = tipo is { } t ? ListaLegislacao.RotuloBotaoTipo(t) : "Todas";
            var conteudo = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            conteudo.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            conteudo.Children.Add(new TextBlock { Text = quantidade.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            var b = new ToggleButton
            {
                Content = conteudo, IsChecked = tipoDaLista == tipo, HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(2, 4, 2, 4), CornerRadius = new CornerRadius(8), IsEnabled = quantidade > 0 || tipoDaLista == tipo,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, $"{rotulo}: {quantidade}");
            var escolha = tipo;
            b.Click += (_, _) => { tipoDaLista = tipoDaLista == escolha ? null : escolha; MontarListaDeLeis(); };
            Grid.SetColumn(b, coluna++);
            grade.Children.Add(b);
        }
    }

    // ---------- o cartão da lei ----------

    FrameworkElement CartaoDaLei(LinhaAnexo l, bool favorita, bool aberta)
    {
        var grade = new Grid { Tag = l.Id, Padding = new Thickness(4, 8, 0, 8), ColumnSpacing = 10 };
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grade.Children.Add(new FontIcon { Glyph = "", FontSize = 26, Foreground = CorDoTema("PmvniIconeLei"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });

        var textos = new StackPanel { Spacing = 2 };
        textos.Children.Add(new TextBlock { Text = l.Titulo, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        var ementa = EmentaDe(l.Id);
        if (!string.IsNullOrEmpty(ementa))
        {
            var e = new TextBlock { Text = ementa, FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            if (Application.Current.Resources.TryGetValue("PmvniSecondaryTextStyle", out var estilo) && estilo is Style s) { e.Style = s; e.FontSize = 12; }
            ToolTipService.SetToolTip(e, ementa);
            textos.Children.Add(e);
        }
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 3, 0, 0) };
        var tipo = ListaLegislacao.TipoDoTitulo(l.Titulo);
        if (tipo != TipoNorma.Outro)
        {
            meta.Children.Add(new Border
            {
                Background = CorDoTema("PmvniSeloTipoFundo"), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 1, 9, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = ListaLegislacao.RotuloTipo(tipo), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = CorDoTema("PmvniSeloTipoTexto") },
            });
            meta.Children.Add(new Border { Width = 1, Height = 14, Style = (Style)Application.Current.Resources["PmvniDivisoriaVerticalStyle"], VerticalAlignment = VerticalAlignment.Center });
        }
        meta.Children.Add(new FontIcon { Glyph = "", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        meta.Children.Add(new TextBlock { Text = l.Paginas, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        if (l.Selo is { } selo)
        {
            var aviso = new FontIcon { Glyph = "", FontSize = 14, Style = (Style)Application.Current.Resources["PmvniIconeAtencaoStyle"], VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(aviso, selo);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(aviso, selo);
            meta.Children.Add(aviso);
        }
        textos.Children.Add(meta);
        Grid.SetColumn(textos, 1);
        grade.Children.Add(textos);

        var estrela = new Button
        {
            Content = new FontIcon { Glyph = favorita ? "" : "", FontSize = 16, Foreground = favorita ? CorDoTema("PmvniFavorito") : null },
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top,
        };
        var nomeEstrela = favorita ? "Tirar dos favoritos" : "Marcar como favorita";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(estrela, $"{nomeEstrela}: {l.Titulo}");
        ToolTipService.SetToolTip(estrela, favorita ? "Favorita (fica no topo da lista). Clique para tirar." : "Marcar como favorita: a lei passa a ficar no topo da lista.");
        var id = l.Id;
        estrela.Click += (_, _) =>
        {
            if (!ajustes.LeisFavoritas.Remove(id)) ajustes.LeisFavoritas.Add(id);
            SalvarAjustes();
            MontarListaDeLeis();
        };
        Grid.SetColumn(estrela, 2);
        grade.Children.Add(estrela);

        if (aberta)
        {
            var topicos = new StackPanel { Spacing = 0, Margin = new Thickness(0, 8, 6, 0), Style = (Style)Application.Current.Resources["PmvniPilhaContornoStyle"], BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(4, 0, 0, 0) };
            Grid.SetRow(topicos, 1);
            Grid.SetColumn(topicos, 0);
            Grid.SetColumnSpan(topicos, 3);
            grade.Children.Add(topicos);
            topicosNoCartao = topicos;
            topicosNoCartaoId = l.Id;
            PreencherTopicos();
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grade, l.Titulo);
        return grade;
    }

    /// <summary>Repõe os tópicos da lei aberta no cartão dela (depois que a leitura padronizada termina de desenhar a lei).</summary>
    void AtualizarTopicosNaLista()
    {
        // O cartão expandido é o de outra lei (ou nenhum): a lista é remontada com o cartão da lei aberta expandido.
        if (topicosNoCartaoId != leiAberta?.Id) { MontarListaDeLeis(); return; }
        PreencherTopicos();
    }

    void PreencherTopicos()
    {
        if (topicosNoCartao is not { } painel) return;
        painel.Children.Clear();
        if (topicosLei.Count == 0)
        {
            painel.Children.Add(new TextBlock
            {
                Text = leituraPadronizadaLigada ? "Esta lei não tem tópicos nem artigos reconhecíveis." : "Os artigos aparecem aqui quando o texto da lei estiver pronto.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(6, 2, 0, 2),
                Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"],
            });
            return;
        }
        foreach (var (b, alvo) in topicosLei) painel.Children.Add(ItemDoTopico(b, alvo));
    }

    // ---------- ementas ----------

    string? EmentaDe(string id)
    {
        var a = anexosDoModulo.FirstOrDefault(x => x.Id == id);
        return a is not null && ementasLeis.TryGetValue(a.Sha256, out var e) ? e : null;
    }

    /// <summary>Lê, em segundo plano, a ementa das leis que ainda não têm (das 3 primeiras páginas do texto indexado) e remonta a lista
    /// quando terminar. Uma leitura por vez; lei sem texto fica sem ementa.</summary>
    void LerEmentasQueFaltam()
    {
        if (!lendoEmentas.IsCompleted) return;
        var faltam = anexosDoModulo.Where(a => a.TemTexto && !ementasLeis.ContainsKey(a.Sha256)).Select(a => (a.Id, a.Sha256)).ToList();
        if (faltam.Count == 0) return;
        var acervo = legislacao.Acervo;
        lendoEmentas = Task.Run(() =>
        {
            var lidas = new List<(string Sha, string Ementa)>();
            foreach (var (id, sha) in faltam)
            {
                try
                {
                    var paginas = acervo.TextoPorPagina(id);
                    if (paginas is null) continue; // índice ainda não pronto: tenta de novo na próxima montagem
                    var blocos = LeituraPadronizada.Estruturar(paginas.Take(3).ToList());
                    var ementa = blocos.FirstOrDefault(b => b.Tipo == TipoBlocoLei.Ementa)?.Texto.Trim() ?? "";
                    lidas.Add((sha, ementa.Length > 240 ? ementa[..240] : ementa));
                }
                catch (Exception e) { RegistroErros.Gravar("legislacao.ementa", e); lidas.Add((sha, "")); }
            }
            return lidas;
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result.Count > 0)
                DispatcherQueue.TryEnqueue(() =>
                {
                    foreach (var (sha, ementa) in t.Result) ementasLeis[sha] = ementa;
                    MontarListaDeLeis();
                });
        }, TaskScheduler.Default);
    }
}
