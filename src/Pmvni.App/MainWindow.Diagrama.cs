using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Pmvni.Revisao;
using Windows.Foundation;

namespace Pmvni.App;

/// <summary>Correção 2026-11-16: clique no desenho da VOLUMETRIA DA EDIFICAÇÃO abre um pedido à IA ("mais um pavimento", "sem telhado",
/// "mostrar que o afastamento lateral direito está errado"). A IA devolve os textos da linha <c>volumetria/01</c> (alturas e a forma:
/// pavimentos, telhado, destaque) e, se preciso, das chamadas; tudo entra como edição comum (desfazível). O desenho base é sempre o mesmo.</summary>
public sealed partial class MainWindow
{
    internal Flyout? UltimoPedidoVolumetria { get; private set; }

    void MostrarPedidoVolumetria(Point ponto)
    {
        if (sessao?.Revisao.Projeto is not { } p || !TabelasRevisao.Existe(p, TabelasRevisao.Id(TabelasRevisao.Volumetria, 1))) return;
        var painel = new StackPanel { Spacing = 8, Width = 420 };
        painel.Children.Add(new TextBlock { Text = "Ajustar a volumetria com a IA", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        var caixa = new TextBox
        {
            PlaceholderText = "Ex.: mais um pavimento; sem telhado; mostrar que o afastamento lateral direito está errado",
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinHeight = 64,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixa, "Pedido para a volumetria");
        painel.Children.Add(caixa);
        painel.Children.Add(new TextBlock
        {
            Text = "O desenho continua o mesmo; mudam os pavimentos, o telhado, as alturas e o destaque em vermelho. Desfazer volta.",
            Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"], TextWrapping = TextWrapping.Wrap,
        });
        var aplicar = new Button { Content = "Aplicar", Style = (Style)Application.Current.Resources["AccentButtonStyle"], IsEnabled = Editavel };
        var anel = new ProgressRing { IsActive = false, Width = 18, Height = 18, Visibility = Visibility.Collapsed };
        var linhaBotoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { aplicar, anel } };
        painel.Children.Add(linhaBotoes);
        var flyout = new Flyout { Content = painel, Placement = FlyoutPlacementMode.Bottom };
        async Task Enviar()
        {
            var pedido = caixa.Text.Trim();
            if (pedido.Length == 0 || !aplicar.IsEnabled || ajustandoComIa) return;
            aplicar.IsEnabled = caixa.IsEnabled = false;
            anel.Visibility = Visibility.Visible;
            anel.IsActive = true;
            var ok = false;
            try { ok = await AjustarVolumetriaComIaAsync(pedido); }
            finally
            {
                anel.IsActive = false;
                anel.Visibility = Visibility.Collapsed;
                aplicar.IsEnabled = caixa.IsEnabled = Editavel;
                if (ok) flyout.Hide(); // no erro, a caixa fica aberta com o pedido para tentar de novo
            }
        }
        aplicar.Click += async (_, _) => await Enviar();
        caixa.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await Enviar();
        };
        flyout.Opened += (_, _) => caixa.Focus(FocusState.Programmatic);
        UltimoPedidoVolumetria = flyout;
        flyout.ShowAt(R.Navegador, new FlyoutShowOptions { Position = ponto });
    }

    /// <summary>Manda à IA a volumetria e as chamadas do diagrama com o pedido; aplica o que voltar.</summary>
    internal async Task<bool> AjustarVolumetriaComIaAsync(string pedido)
    {
        if (ajustandoComIa || sessao is not { Revisao.Projeto: { } p } s) return false;
        ajustandoComIa = true;
        var ok = true;
        try
        {
        var linhas = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var id in TabelasRevisao.Ids(p).Where(i => TabelasRevisao.TentarLer(i, out var b, out _)
                     && b is TabelasRevisao.Volumetria or TabelasRevisao.Chamadas))
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in TabelasRevisao.Caminhos(TabelasRevisao.Original(p, id)!)) d[c] = TabelasRevisao.TextoEfetivo(p, id, c) ?? "";
            d["situacao"] = TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(p, id)) ?? "";
            linhas[id] = d;
        }
        var pastaProcesso = RecortePrancha.PastaDoProcesso(s.Caminho);
        try
        {
            var pecas = await LerPecasAsync();
            var saida = await IaDespacho.PerguntarAsync(pecas.Claude, pecas.Codex, IaRecalculoIndices.PromptVolumetria(pedido, linhas),
                CancellationToken.None, pastaProcesso, []);
            if (!ReferenceEquals(sessao, s)) return false;
            if (IaRecalculoIndices.Ler(saida, linhas.Keys) is not { } novas)
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Volumetria", "A resposta da IA não veio no formato esperado; nada mudou.");
                return false;
            }
            var mudou = 0;
            foreach (var (id, campos) in novas)
            {
                if (!linhas.TryGetValue(id, out var antes)) continue;
                foreach (var (caminho, texto) in campos)
                {
                    if (caminho is "situacao" or "justificativa" || !antes.ContainsKey(caminho) || texto.Trim().Length == 0) continue;
                    if (TextoEditado.Normalizar(texto) == TextoEditado.Normalizar(antes[caminho])) continue;
                    if ((TabelasRevisao.TextoEfetivo(p, id, caminho) ?? "") != antes[caminho]) continue; // editado durante a espera
                    await TentarAsync(() => AplicarAsync(new DefinirCampo(id, caminho, texto)));
                    mudou++;
                }
            }
            MostrarMensagem(InfoBarSeverity.Success, "Volumetria",
                mudou == 0 ? "A IA não mudou nada na volumetria." : $"A IA ajustou a volumetria ({mudou} {(mudou == 1 ? "texto" : "textos")}). Desfazer volta.");
        }
        catch (Exception e)
        {
            // Auditoria 2026-11-16: nenhum erro da IA (JSON, tempo, rede, processo) derruba o editor; o detalhe vai para o registro.
            RegistroErros.Gravar("ia", e);
            ok = false;
            MostrarMensagem(InfoBarSeverity.Error, "IA", e is InvalidOperationException or IOException ? e.Message : "A chamada à IA falhou: " + e.Message);
        }
        return ok;
        }
        finally { ajustandoComIa = false; }
    }
}
