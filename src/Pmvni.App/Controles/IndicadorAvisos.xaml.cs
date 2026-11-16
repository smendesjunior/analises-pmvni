using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Indicador de layout (auditoria A2): resume os avisos da composição no rodapé e mostra a lista num flyout, cada aviso com
/// "Ir à folha N" (<see cref="IrParaFolha"/>). Com estouro, fica na cor crítica ("1 bloqueio"). Sem avisos, some.</summary>
public sealed partial class IndicadorAvisos : UserControl
{
    IReadOnlyList<AvisoLayout> avisos = [];

    public IndicadorAvisos() => InitializeComponent();

    /// <summary>"Ir à folha N" de uma linha da lista.</summary>
    public event Action<int>? IrParaFolha;

    public IReadOnlyList<AvisoLayout> Avisos => avisos;

    /// <summary>O resumo à vista ("" sem avisos).</summary>
    public string Resumo => avisos.Count == 0 ? "" : Texto.Text;

    public bool Visivel => Botao.Visibility == Visibility.Visible;

    public bool Critico => AvisosLayout.Bloqueia(avisos);

    public void Mostrar(IReadOnlyList<AvisoLayout> lista)
    {
        avisos = lista;
        Linhas.Children.Clear();
        if (lista.Count == 0)
        {
            Botao.Visibility = Visibility.Collapsed;
            Texto.Text = "";
            return;
        }
        var critico = AvisosLayout.Bloqueia(lista);
        Texto.Text = AvisosLayout.Resumo(lista);
        Texto.Style = (Style)Resources[critico ? "TextoCritico" : "TextoNormal"];
        Icone.Style = (Style)Resources[critico ? "IconeCritico" : "IconeNormal"];
        AutomationProperties.SetName(Botao, Texto.Text + ": abrir a lista");
        Dicas.Aplicar(Botao, "Avisos de layout", "Abre a lista dos problemas de layout das folhas, com o caminho até cada um.",
            motivo: critico ? "Há blocos maiores que a página: a exportação do PDF está bloqueada." : null);
        foreach (var a in lista)
        {
            var linha = new StackPanel { Spacing = 2 };
            var texto = new TextBlock { Text = a.Texto, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            if (a.Bloqueia) texto.Style = (Style)Resources["TextoCritico"];
            linha.Children.Add(texto);
            if (a.Folha is { } folha)
            {
                var ir = new HyperlinkButton { Content = $"Ir à folha {folha}", Padding = new Thickness(0, 2, 0, 2) };
                AutomationProperties.SetName(ir, $"Ir à folha {folha}");
                Dicas.Aplicar(ir, $"Ir à folha {folha}", "Rola as folhas até onde está este aviso.");
                ir.Click += (_, _) => { Lista.Hide(); IrParaFolha?.Invoke(folha); };
                linha.Children.Add(ir);
            }
            Linhas.Children.Add(linha);
        }
        Botao.Visibility = Visibility.Visible;
    }

    /// <summary>O roteiro escolhe "Ir à folha N" da linha <paramref name="i"/> (como o clique).</summary>
    internal bool EscolherLinha(int i)
    {
        if (i < 0 || i >= avisos.Count || avisos[i].Folha is not { } folha) return false;
        IrParaFolha?.Invoke(folha);
        return true;
    }
}
