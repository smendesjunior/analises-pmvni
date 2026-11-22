using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Padrão da interface (3.2.2): botões, menus, chaves, rótulos e ícones iguais em todo o editor, no padrão do WinUI 3. Lê o XAML e
/// o C# que monta interface em <c>src\Pmvni.App</c> e falha apontando arquivo e linha de cada desvio, para nenhuma versão sair despadronizada.
/// Altura, peso de texto, raio dos cantos e hover dos botões vêm só dos estilos de <c>Estilos.xaml</c>.</summary>
public class PadraoInterfaceTestes
{
    static readonly string Fonte = Path.Combine(Ambiente.PastaTestes, "..", "src", "Pmvni.App");

    // Arquivos que definem o padrão (os estilos) ou desenham ícones coloridos fixos; ficam fora das regras de uso.
    static readonly string[] Definicoes = ["Estilos.xaml", "App.xaml", "Trilho.xaml", "IconesModulos.xaml", "CoresTonais.xaml", "Brilho.cs", "BrilhoArcoIris.cs", "AutotesteBrilho.cs"];

    const string Botao = "Button|DropDownButton|ToggleButton|SplitButton|RepeatButton|HyperlinkButton|AppBarButton|AppBarToggleButton";
    const string ItemMenu = "MenuFlyoutItem|ToggleMenuFlyoutItem|MenuFlyoutSubItem|RadioMenuFlyoutItem";

    public static TheoryData<string, string> Regras() => new()
    {
        { "botão com altura própria (o padrão é 32, do estilo)", $@"<({Botao})\b[^>]*\b(Min)?Height=""(?!32"")\d+""|new ({Botao})\s*\{{[^}}]*\b(Min)?Height\s*=\s*(?!32\b)\d+|\b\w+\.(Min)?Height\s*=\s*(36|40|44)\s*;" },
        { "botão com tamanho ou peso de texto próprio", $@"<({Botao})\b[^>]*\bFont(Size|Weight)=""|new ({Botao})\s*\{{[^}}]*\bFont(Size|Weight)\s*=" },
        { "botão com raio dos cantos próprio (o padrão é ControlCornerRadius)", $@"<({Botao})\b[^>]*\bCornerRadius=""(?!\{{)|new ({Botao})\s*\{{[^}}]*\bCornerRadius\s*=" },
        { "brilho decorativo ligado", @"Brilho\.Ativo=""True""|Brilho\.SetAtivo\([^,]+,\s*true\)" },
        { "item de menu com texto, espaçamento ou altura próprios", $@"<({ItemMenu})\b[^>]*\b(FontSize|FontWeight|Padding|Height|MinHeight)=""|new ({ItemMenu})\s*\{{[^}}]*\b(FontSize|FontWeight|Padding|Height|MinHeight)\s*=" },
        { "letras espaçadas", "CharacterSpacing" },
        { "ícone de menu fora de 16 px", @"\.Icon>\s*<FontIcon\b(?=[^>]*FontSize="")(?![^>]*FontSize=""16"")[^>]*/>" },
    };

    [Theory]
    [MemberData(nameof(Regras))]
    public void interface_sem_desvio_do_padrao(string regra, string padrao)
    {
        var desvios = new List<string>();
        foreach (var arquivo in Directory.EnumerateFiles(Fonte, "*.*", SearchOption.AllDirectories)
                     .Where(a => a.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                     .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .Where(a => !Definicoes.Contains(Path.GetFileName(a), StringComparer.OrdinalIgnoreCase)))
        {
            var texto = File.ReadAllText(arquivo);
            foreach (Match m in Regex.Matches(texto, padrao))
                desvios.Add($"{Path.GetRelativePath(Fonte, arquivo)}:{texto.AsSpan(0, m.Index).Count('\n') + 1}");
        }
        Assert.True(desvios.Count == 0, $"{regra}: {string.Join(", ", desvios)}");
    }

    [Fact]
    public void estilos_de_botao_usam_a_mesma_altura_e_o_mesmo_raio()
    {
        var estilos = File.ReadAllText(Path.Combine(Fonte, "Estilos.xaml"));
        foreach (var chave in new[] { "PmvniBotaoPrimarioStyle", "PmvniBotaoTonalStyle", "PmvniFaixaMenuPrimarioStyle" })
        {
            var m = Regex.Match(estilos, $@"<Style x:Key=""{chave}""[\s\S]*?</Style>");
            Assert.True(m.Success, chave);
            Assert.DoesNotContain("FontWeight", m.Value);
            Assert.DoesNotContain("Brilho.Ativo", m.Value);
            Assert.Contains(@"Property=""MinHeight"" Value=""32""", m.Value);
            Assert.Contains(@"Property=""CornerRadius"" Value=""{ThemeResource ControlCornerRadius}""", m.Value);
        }
    }
}
