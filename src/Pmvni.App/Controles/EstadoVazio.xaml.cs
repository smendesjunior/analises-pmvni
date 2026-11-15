using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Tela vazia de um módulo (auditoria M5): ícone do módulo, título, frase, botão tonal e secundário (os comandos são os da faixa,
/// pelo id: <see cref="ComandoEscolhido"/>) e, em Relatórios, os recentes (<see cref="RecenteEscolhido"/>).</summary>
public sealed partial class EstadoVazio : UserControl
{
    string? idPrimario, idSecundario;

    public EstadoVazio()
    {
        InitializeComponent();
        BotaoPrimario.Click += (_, _) => { if (idPrimario is not null) ComandoEscolhido?.Invoke(idPrimario); };
        BotaoSecundario.Click += (_, _) => { if (idSecundario is not null) ComandoEscolhido?.Invoke(idSecundario); };
    }

    /// <summary>Um dos botões foi clicado: o id do comando (o mesmo da faixa).</summary>
    public event Action<string>? ComandoEscolhido;

    /// <summary>Um recente foi clicado: o caminho.</summary>
    public event Action<string>? RecenteEscolhido;

    public string TextoTitulo => Titulo.Text;

    public string TextoPrimario => BotaoPrimario.Visibility == Visibility.Visible ? AutomationProperties.GetName(BotaoPrimario) : "";

    public int QuantosRecentes => ListaRecentes.Children.Count;

    /// <summary>Monta a tela: o ícone do módulo, os textos e os dois botões (rótulo, glifo e id do comando; null = sem o botão).</summary>
    public void Definir(Modulo modulo, string titulo, string frase, (string Rotulo, string Glifo, string Id)? primario, (string Rotulo, string Id)? secundario)
    {
        Icone.ContentTemplate = Application.Current.Resources.TryGetValue(Modulos.ChaveIcone(modulo), out var t) ? t as DataTemplate : null;
        Titulo.Text = titulo;
        Frase.Text = frase;
        Frase.Visibility = frase.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        idPrimario = primario?.Id;
        if (primario is { } p)
        {
            var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            conteudo.Children.Add(new FontIcon { Glyph = p.Glifo, FontSize = 16 });
            conteudo.Children.Add(new TextBlock { Text = p.Rotulo, VerticalAlignment = VerticalAlignment.Center });
            BotaoPrimario.Content = conteudo;
            AutomationProperties.SetName(BotaoPrimario, p.Rotulo);
            BotaoPrimario.Visibility = Visibility.Visible;
        }
        else BotaoPrimario.Visibility = Visibility.Collapsed;
        idSecundario = secundario?.Id;
        if (secundario is { } s)
        {
            BotaoSecundario.Content = s.Rotulo;
            AutomationProperties.SetName(BotaoSecundario, s.Rotulo);
            BotaoSecundario.Visibility = Visibility.Visible;
        }
        else BotaoSecundario.Visibility = Visibility.Collapsed;
    }

    /// <summary>Os recentes que ainda existem (nome e pasta, caminho na dica), no máximo 5; vazio = some a seção.</summary>
    public void DefinirRecentes(IReadOnlyList<(string Texto, string Caminho)> recentes)
    {
        ListaRecentes.Children.Clear();
        foreach (var (texto, caminho) in recentes.Take(5))
        {
            var b = new HyperlinkButton { Content = texto, Padding = new Thickness(0, 3, 0, 3) };
            AutomationProperties.SetName(b, "Abrir " + texto);
            ToolTipService.SetToolTip(b, caminho);
            b.Click += (_, _) => RecenteEscolhido?.Invoke(caminho);
            ListaRecentes.Children.Add(b);
        }
        PainelRecentes.Visibility = ListaRecentes.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
