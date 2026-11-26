using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Tela vazia de um módulo (auditoria M5): ícone do módulo, título, frase, botão principal e secundário (os comandos são os da faixa,
/// pelo id: <see cref="ComandoEscolhido"/>) e, em Relatórios, os recentes (<see cref="RecenteEscolhido"/>).</summary>
public sealed partial class EstadoVazio : UserControl
{
    string? idPrimario, idSecundario;
    Modulo? moduloAtual;
    bool iconeVetorial;
    XamlRoot? raizDoIcone;
    const double LadoIcone = 48;

    public EstadoVazio()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            raizDoIcone = XamlRoot;
            raizDoIcone.Changed += AoMudarEscala;
            AtualizarIcone();
        };
        Unloaded += (_, _) =>
        {
            if (raizDoIcone is not null) raizDoIcone.Changed -= AoMudarEscala;
            raizDoIcone = null;
        };
        BotaoPrimario.Click += (_, _) => { if (idPrimario is not null) ComandoEscolhido?.Invoke(idPrimario); };
        BotaoSecundario.Click += (_, _) => { if (idSecundario is not null) ComandoEscolhido?.Invoke(idSecundario); };
    }

    /// <summary>Um dos botões foi clicado: o id do comando (o mesmo da faixa).</summary>
    public event Action<string>? ComandoEscolhido;

    /// <summary>Um recente foi clicado: o caminho.</summary>
    public event Action<string>? RecenteEscolhido;

    void AoMudarEscala(XamlRoot sender, XamlRootChangedEventArgs args) => AtualizarIcone();

    void AtualizarIcone()
    {
        if (moduloAtual is not { } modulo) return;
        var vetorial = LadoIcone * (raizDoIcone?.RasterizationScale ?? 1) > 128;
        if (VagaIcone.Child is not null && vetorial == iconeVetorial) return;
        iconeVetorial = vetorial;
        VagaIcone.Child = Trilho.NovoIcone(modulo, LadoIcone, vetorial);
    }

    public string TextoTitulo => Titulo.Text;

    public string TextoPrimario => BotaoPrimario.Visibility == Visibility.Visible ? AutomationProperties.GetName(BotaoPrimario) : "";

    public int QuantosRecentes => ListaRecentes.Children.Count;

    /// <summary>Monta a tela: o ícone do módulo, os textos e os dois botões (rótulo, glifo e id do comando; null = sem o botão).</summary>
    public void Definir(Modulo modulo, string titulo, string frase, (string Rotulo, string Glifo, string Id)? primario, (string Rotulo, string Id)? secundario, string? glifoSecundario = null)
    {
        if (moduloAtual != modulo) VagaIcone.Child = null;
        moduloAtual = modulo;
        AtualizarIcone();
        Titulo.Text = titulo;
        DefinirFrase(frase);
        idPrimario = primario?.Id;
        if (primario is { } p)
        {
            var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            conteudo.Children.Add(new FontIcon { Glyph = p.Glifo, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] });
            conteudo.Children.Add(new TextBlock { Text = p.Rotulo, VerticalAlignment = VerticalAlignment.Center });
            BotaoPrimario.Content = conteudo;
            AutomationProperties.SetName(BotaoPrimario, p.Rotulo);
            Dicas.AplicarComando(BotaoPrimario, p.Id, p.Rotulo, modulo);
            BotaoPrimario.Visibility = Visibility.Visible;
        }
        else BotaoPrimario.Visibility = Visibility.Collapsed;
        idSecundario = secundario?.Id;
        if (secundario is { } s)
        {
            var c2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            c2.Children.Add(new FontIcon { Glyph = glifoSecundario ?? Glifos.Abrir, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] });
            c2.Children.Add(new TextBlock { Text = s.Rotulo, VerticalAlignment = VerticalAlignment.Center });
            BotaoSecundario.Content = c2;
            AutomationProperties.SetName(BotaoSecundario, s.Rotulo);
            Dicas.AplicarComando(BotaoSecundario, s.Id, s.Rotulo, modulo);
            BotaoSecundario.Visibility = Visibility.Visible;
        }
        else BotaoSecundario.Visibility = Visibility.Collapsed;
    }

    internal void DefinirAcoesAdicionais(Modulo modulo, IEnumerable<ComandoFaixa> comandos)
    {
        AcoesAdicionais.Children.Clear();
        foreach (var comando in comandos)
        {
            var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            conteudo.Children.Add(new FontIcon { Glyph = Glifos.Adicionar, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] });
            conteudo.Children.Add(new TextBlock { Text = comando.Rotulo });
            var botao = new Button { Content = conteudo, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(botao, comando.Rotulo);
            Dicas.AplicarComando(botao, comando.Id, comando.Rotulo, modulo);
            botao.Click += (_, _) => ComandoEscolhido?.Invoke(comando.Id);
            AcoesAdicionais.Children.Add(botao);
        }
        AcoesAdicionais.Visibility = AcoesAdicionais.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Troca só a frase (ex.: "Nenhum processo combina com o filtro."); vazia = some.</summary>
    public void DefinirFrase(string frase)
    {
        Frase.Text = frase;
        Frase.Visibility = frase.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public string TextoFrase => Frase.Text;

    /// <summary>O ícone do módulo está desenhado (roteiro).</summary>
    public bool IconeDesenhado => Trilho.IconeDesenhado(VagaIcone.Child as FrameworkElement);

    /// <summary>Os recentes que ainda existem (nome e pasta, caminho na dica), no máximo 5; vazio = some a seção.</summary>
    public void DefinirRecentes(IReadOnlyList<(string Texto, string Caminho)> recentes)
    {
        ListaRecentes.Children.Clear();
        foreach (var (texto, caminho) in recentes.Take(5))
        {
            // Linha: ícone do relatório na cor de destaque, o nome e, embaixo, a pasta do processo (o caminho inteiro na dica).
            var pasta = Path.GetFileName(Path.GetDirectoryName(caminho) ?? "") ?? "";
            var linha = new Grid { ColumnSpacing = 12 };
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            linha.Children.Add(new FontIcon { Glyph = Glifos.Relatorio, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["PmvniIconeAcaoSecundario"] });
            var textos = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock { Text = Path.GetFileName(caminho), Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"], TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
            if (pasta.Length > 0) textos.Children.Add(new TextBlock { Text = pasta, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(textos, 1);
            linha.Children.Add(textos);
            var seta = new FontIcon { Glyph = Glifos.ExpandirLinha, Style = (Style)Application.Current.Resources["PmvniIconeAcaoSecundario"], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(seta, 2);
            linha.Children.Add(seta);
            var b = new Button
            {
                Height = double.NaN, Content = linha, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8, 12, 8), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            AutomationProperties.SetName(b, "Abrir " + texto);
            Dicas.Aplicar(b, texto, "Abre este relatório aberto há pouco: " + caminho);
            b.Click += (_, _) => RecenteEscolhido?.Invoke(caminho);
            ListaRecentes.Children.Add(b);
        }
        PainelRecentes.Visibility = ListaRecentes.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
