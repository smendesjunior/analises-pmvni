using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Linha de um cartão dos Ajustes (como <c>OnSettingRowSizeChanged</c> do WinStatusBar): título e ajuda à esquerda, controle numa
/// coluna de 240 à direita; abaixo de <see cref="LarguraMinima"/> o controle desce para baixo do texto, ocupando a largura toda.</summary>
public static class LinhaAjuste
{
    public const double LarguraMinima = 540;
    const double LarguraDoControle = 240;

    /// <summary>Arruma a linha <paramref name="linha"/> (Grid de 2 colunas e 2 linhas, o controle na coluna 1) para a largura dada.</summary>
    public static void Ajustar(Grid linha, double largura)
    {
        if (linha.ColumnDefinitions.Count < 2 || linha.RowDefinitions.Count < 2) return;
        var estreita = largura < LarguraMinima;
        linha.ColumnDefinitions[1].Width = estreita ? new GridLength(0) : new GridLength(LarguraDoControle);
        linha.ColumnSpacing = estreita ? 0 : 20;
        foreach (var filho in linha.Children.OfType<FrameworkElement>())
        {
            // O controle é o filho que não está na coluna 0 da linha 0 (o texto); ele tem a marca "controle" depois da primeira passada.
            var ehControle = filho.Tag as string == LinhaControle || Grid.GetColumn(filho) == 1;
            if (!ehControle) continue;
            filho.Tag ??= LinhaControle;
            Grid.SetColumn(filho, estreita ? 0 : 1);
            Grid.SetRow(filho, estreita ? 1 : 0);
            if (filho is ComboBox or TextBox) filho.HorizontalAlignment = HorizontalAlignment.Stretch;
            else if (filho is ToggleSwitch or Button) filho.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }

    const string LinhaControle = "controle-da-linha";
}
