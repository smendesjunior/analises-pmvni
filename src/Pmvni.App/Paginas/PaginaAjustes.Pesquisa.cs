using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>Pesquisa geral (2026-11-17): a página entrega as linhas que mostra (categoria, grupo, título e ajuda, lidos da própria tela, então
/// uma linha nova entra sozinha) e traz uma delas à vista com o foco no controle dela.</summary>
public sealed partial class PaginaAjustes
{
    IReadOnlyList<OpcaoAjusteTela>? opcoesDaTela;

    /// <summary>As linhas e os grupos de cartões de cada categoria, na ordem da tela. Lidas uma vez (as páginas são criadas uma vez); listas
    /// geradas (leis anexadas, agentes) ficam de fora.</summary>
    internal IReadOnlyList<OpcaoAjusteTela> OpcoesDaTela() => opcoesDaTela ??= LerOpcoes();

    IReadOnlyList<OpcaoAjusteTela> LerOpcoes()
    {
        var recursos = Application.Current.Resources;
        var estiloTitulo = recursos.TryGetValue("PmvniAjusteTitulo", out var t) ? t as Style : null;
        var estiloGrupo = recursos.TryGetValue("PmvniAjusteGrupoTitulo", out var g) ? g as Style : null;
        var estiloLinha = Resources.TryGetValue("Linha", out var l) ? l as Style : null;
        var lista = new List<OpcaoAjusteTela>();
        foreach (var c in CategoriasAjustes.Menu)
        {
            if (!categorias.TryGetValue(c.Tag, out var pagina)) continue;
            var grupo = "";
            Visitar(pagina);

            void Visitar(DependencyObject? o)
            {
                switch (o)
                {
                    case null or ItemsRepeater:
                        return;
                    case TextBlock tb when estiloGrupo is not null && ReferenceEquals(tb.Style, estiloGrupo):
                        grupo = tb.Text;
                        if (grupo.Length > 0) lista.Add(new OpcaoAjusteTela(c.Tag, "", grupo, "", tb, EhGrupo: true));
                        return;
                    case TextBlock tb when estiloTitulo is not null && ReferenceEquals(tb.Style, estiloTitulo):
                        if (tb.Text.Length > 0) lista.Add(new OpcaoAjusteTela(c.Tag, grupo, tb.Text, AjudaAoLado(tb), LinhaDe(tb, estiloLinha)));
                        return;
                    case Panel p:
                        foreach (var filho in p.Children) Visitar(filho);
                        return;
                    case Border b:
                        Visitar(b.Child);
                        return;
                    case ContentControl { Content: DependencyObject d }:
                        Visitar(d);
                        return;
                }
            }
        }
        return lista;
    }

    /// <summary>A frase de ajuda que vem logo depois do título, no mesmo painel.</summary>
    static string AjudaAoLado(TextBlock titulo)
    {
        if (titulo.Parent is not Panel p) return "";
        var i = p.Children.IndexOf(titulo);
        return i >= 0 && i + 1 < p.Children.Count && p.Children[i + 1] is TextBlock ajuda ? ajuda.Text : "";
    }

    /// <summary>A linha (Grid de estilo "Linha") que contém o título; sem ela, o painel do título.</summary>
    static FrameworkElement LinhaDe(TextBlock titulo, Style? estiloLinha)
    {
        FrameworkElement atual = titulo;
        for (var i = 0; i < 4 && atual.Parent is FrameworkElement pai; i++)
        {
            if (pai is Grid grade && (estiloLinha is null || ReferenceEquals(grade.Style, estiloLinha))) return grade;
            atual = pai;
        }
        return titulo.Parent as FrameworkElement ?? titulo;
    }

    /// <summary>Mostra a categoria da opção, rola até a linha e põe o foco no controle dela (caixa, chave, botão), como as Configurações do Windows.</summary>
    internal void MostrarOpcao(OpcaoAjusteTela opcao)
    {
        MostrarCategoria(opcao.Categoria);
        if (opcao.Alvo is not FrameworkElement alvo) return;
        // A categoria acabou de ficar visível: rola e foca depois da medida dela.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            alvo.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.25, AnimationDesired = true });
            if (!opcao.EhGrupo && PrimeiroControle(alvo) is { } controle) controle.Focus(FocusState.Keyboard);
        });
    }

    /// <summary>O primeiro controle focável e habilitado dentro da linha (em largura: o controle da linha vem antes dos de dentro dele).</summary>
    static Control? PrimeiroControle(DependencyObject raiz)
    {
        var fila = new Queue<DependencyObject>();
        fila.Enqueue(raiz);
        while (fila.Count > 0)
        {
            var o = fila.Dequeue();
            if (o is Control { IsTabStop: true, IsEnabled: true, Visibility: Visibility.Visible } c && !ReferenceEquals(o, raiz)) return c;
            if (o is UIElement { Visibility: Visibility.Collapsed } && !ReferenceEquals(o, raiz)) continue;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++) fila.Enqueue(VisualTreeHelper.GetChild(o, i));
        }
        return null;
    }
}
