using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>As peças dos painéis laterais em cartões (correções 2026-11-16), para o que é montado em código: o cartão de seção com
/// cabeçalho, o rótulo pequeno em maiúsculas acima de cada valor, o separador, a marca ("alterado", "novo") e o campo (rótulo +
/// acessórios + valor). Só aplicam os estilos <c>PmvniPainel…</c> de Estilos.xaml (um lugar só para a aparência; vale no claro e no
/// escuro). Usadas pelo painel Propriedades do relatório; o painel do Novo despacho usa as mesmas.</summary>
public static class CartoesDePainel
{
    static Style Estilo(string chave) => (Style)Application.Current.Resources[chave];

    /// <summary>Um cartão de seção: cabeçalho (ícone + título em maiúsculas) e o corpo, devolvido em <paramref name="corpo"/>.</summary>
    public static Border Cartao(string titulo, string glifo, out StackPanel corpo)
    {
        var cabecalho = new Border { Style = Estilo("PmvniPainelCartaoCabecalho") };
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        linha.Children.Add(new FontIcon { Glyph = glifo, Style = Estilo("PmvniPainelCartaoIcone") });
        linha.Children.Add(new TextBlock { Text = titulo.ToUpper(Formatos.Cultura), Style = Estilo("PmvniPainelCartaoTitulo") });
        cabecalho.Child = linha;
        corpo = new StackPanel { Style = Estilo("PmvniPainelCartaoCorpo") };
        var tudo = new StackPanel();
        tudo.Children.Add(cabecalho);
        tudo.Children.Add(corpo);
        return new Border { Style = Estilo("PmvniPainelCartao"), Child = tudo };
    }

    /// <summary>O rótulo de um valor: pequeno, em maiúsculas espaçadas, na cor secundária.</summary>
    public static TextBlock Rotulo(string texto) => new() { Text = texto.ToUpper(Formatos.Cultura), Style = Estilo("PmvniPainelRotulo") };

    /// <summary>Um valor só de leitura (selecionável). <paramref name="forte"/>: em negrito (totais, resultados).</summary>
    public static TextBlock Valor(string texto, bool forte = false) => new() { Text = texto, Style = Estilo(forte ? "PmvniPainelValorForte" : "PmvniPainelValor") };

    /// <summary>Um texto de apoio (cor secundária, pequeno).</summary>
    public static TextBlock Apoio(string texto) => new() { Text = texto, Style = Estilo("PmvniSecondaryTextStyle"), TextWrapping = TextWrapping.Wrap };

    /// <summary>A linha fina que separa dois grupos dentro do cartão (vai de borda a borda).</summary>
    public static Border Separador() => new() { Style = Estilo("PmvniPainelSeparador") };

    /// <summary>A marca pequena ao lado do rótulo: "alterado", "novo".</summary>
    public static Border Marca(string texto) => new()
    {
        Style = Estilo("PmvniPainelMarca"),
        Child = new TextBlock { Text = texto, Style = Estilo("PmvniPainelMarcaTexto") },
    };

    /// <summary>Um botão pequeno de campo ("Restaurar", "Remover"), com nome acessível e dica.</summary>
    public static Button BotaoPequeno(string texto, string nomeAcessivel, string descricao)
    {
        var b = new Button { Content = texto, Style = Estilo("PmvniPainelBotaoPequeno") };
        AutomationProperties.SetName(b, nomeAcessivel);
        Dicas.Aplicar(b, texto, descricao);
        return b;
    }

    /// <summary>Um campo: na primeira linha o rótulo e, à direita, os acessórios (marca, botão); embaixo, o valor.</summary>
    public static StackPanel Campo(string rotulo, UIElement valor, params UIElement[] acessorios)
    {
        var bloco = new StackPanel { Spacing = 4 };
        bloco.Children.Add(Cabecalho(Rotulo(rotulo), acessorios));
        bloco.Children.Add(valor);
        return bloco;
    }

    /// <summary>Rótulo à esquerda e acessórios à direita, na mesma linha.</summary>
    public static Grid Cabecalho(UIElement rotulo, params UIElement[] acessorios)
    {
        var g = new Grid { ColumnSpacing = 6, MinHeight = 24 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(rotulo);
        for (var i = 0; i < acessorios.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn((FrameworkElement)acessorios[i], i + 1);
            g.Children.Add(acessorios[i]);
        }
        return g;
    }

    /// <summary>Rótulo à esquerda e valor à direita, numa linha só (totais: "VÃO TOTAL … 3,48 m²").</summary>
    public static Grid Linha(string rotulo, FrameworkElement valor)
    {
        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(Rotulo(rotulo));
        valor.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(valor, 1);
        g.Children.Add(valor);
        return g;
    }

    /// <summary>A caixa de texto de um campo editável no próprio painel (quebra de linha automática; Enter aplica).</summary>
    public static TextBox Caixa(string texto, string nomeAcessivel, bool editavel)
    {
        var c = new TextBox { Text = texto, Tag = texto, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, IsReadOnly = !editavel, IsSpellCheckEnabled = false };
        AutomationProperties.SetName(c, nomeAcessivel);
        return c;
    }

    /// <summary>Uma caixa pequena para um número (largura, altura, piso), alinhada à direita.</summary>
    public static TextBox CaixaDeNumero(string texto, string nomeAcessivel, bool editavel, double largura = 68)
    {
        var c = new TextBox
        {
            Text = texto, Tag = texto, Width = largura, MinWidth = 0, TextAlignment = TextAlignment.Right, IsReadOnly = !editavel, IsSpellCheckEnabled = false,
            VerticalAlignment = VerticalAlignment.Center, InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } },
        };
        AutomationProperties.SetName(c, nomeAcessivel);
        return c;
    }

    /// <summary>O estilo do texto de um resultado: true = atende (verde), false = não atende (vermelho), null = sem como comparar.</summary>
    public static Style EstiloDoResultado(bool? atende) =>
        Estilo(atende == true ? "PmvniPainelResultadoOk" : atende == false ? "PmvniPainelResultadoErro" : "PmvniPainelResultadoNeutro");
}
