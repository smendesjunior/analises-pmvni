using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Trilho à esquerda com os módulos de <see cref="Faixas.Disponiveis"/> (só os que funcionam). Clicar avisa por
/// <see cref="ModuloEscolhido"/>; quem troca a página e a faixa é a janela, que depois chama <see cref="Marcar"/>.</summary>
public sealed partial class Trilho : UserControl
{
    readonly Dictionary<Modulo, (Button Botao, Border Indicador)> botoes = new();

    public Trilho() => InitializeComponent();

    public event Action<Modulo>? ModuloEscolhido;

    /// <summary>Os módulos do trilho, na ordem dada (Ajustes vai para o pé).</summary>
    public IReadOnlyList<Modulo> Modulos => botoes.Keys.ToList();

    public void Montar(IReadOnlyList<Modulo> modulos)
    {
        Cima.Children.Clear();
        Baixo.Children.Clear();
        botoes.Clear();
        foreach (var m in modulos)
        {
            var indicador = new Border { Style = (Style)Application.Current.Resources["PmvniRailIndicatorStyle"], Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 0, 0) };
            var conteudo = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            conteudo.Children.Add(new FontIcon { Glyph = Pmvni.App.Modulos.Glifo(m), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center });
            conteudo.Children.Add(new TextBlock { Text = Pmvni.App.Modulos.Rotulo(m), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var grade = new Grid();
            grade.Children.Add(conteudo);
            grade.Children.Add(indicador);
            var botao = new Button
            {
                Content = grade,
                Style = (Style)Application.Current.Resources["PmvniTrilhoButtonStyle"],
                Tag = m,
            };
            AutomationProperties.SetName(botao, Pmvni.App.Modulos.Rotulo(m));
            ToolTipService.SetToolTip(botao, Pmvni.App.Modulos.Rotulo(m));
            botao.Click += (_, _) => ModuloEscolhido?.Invoke(m);
            (Pmvni.App.Modulos.Embaixo(m) ? Baixo : Cima).Children.Add(botao);
            botoes[m] = (botao, indicador);
        }
    }

    /// <summary>Mostra o indicador no módulo ativo (e só nele).</summary>
    public void Marcar(Modulo atual)
    {
        foreach (var (m, (b, ind)) in botoes)
        {
            ind.Visibility = m == atual ? Visibility.Visible : Visibility.Collapsed;
            b.FontWeight = m == atual ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }
}
