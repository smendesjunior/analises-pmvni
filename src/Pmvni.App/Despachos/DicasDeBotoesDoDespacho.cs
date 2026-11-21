using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

internal static class DicasDeBotoesDoDespacho
{
    internal static Border Suporte(Button botao) => new() { Child = botao, Background = new SolidColorBrush(Colors.Transparent) };

    internal static void Atualizar(Button botao, string? motivo)
    {
        if (Dicas.De(botao) is not { } dica) return;

        motivo = botao.IsEnabled ? null : motivo;
        if (dica.Motivo != motivo)
        {
            dica = dica with { Motivo = motivo };
            Dicas.Aplicar(botao, dica);
        }

        // No WinUI o botão desabilitado não abre a dica. A borda externa recebe a dica nesse estado.
        var dicaDoSuporte = motivo is null ? null : dica;
        if (botao.Parent is Border suporte && Dicas.De(suporte) != dicaDoSuporte)
            ToolTipService.SetToolTip(suporte, dicaDoSuporte is null ? null : Dicas.Criar(dicaDoSuporte));
    }
}
