using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Autoteste do brilho "trabalhando" (1.0.0, correção dos halos acesos com o app ocioso): monta numa janela à parte o mesmo
/// arranjo das páginas Imóveis e Sites (uma página Grid com o quadro "Carregando…" em pílula) e confere o ciclo inteiro — ocioso sem
/// brilho, trabalhando com brilho, fim do trabalho, página recolhida com o quadro ainda marcado como visível (o defeito antigo), volta da
/// página, cantos originais devolvidos, relógio de quadros parado e camada do halo devolvida ao sair da árvore. Entra no "ok" do --autoteste.</summary>
internal static class AutotesteBrilho
{
    public static async Task<Dictionary<string, object?>> ConferirAsync()
    {
        var r = new Dictionary<string, object?> { ["ok"] = false };
        Window? janela = null;
        try
        {
            janela = new Window { Title = "Autoteste do brilho" };
            var raiz = new Grid();
            var pagina = new Grid();
            var quadro = Trabalhando.Quadro("Carregando a página…");
            quadro.VerticalAlignment = VerticalAlignment.Top;
            quadro.HorizontalAlignment = HorizontalAlignment.Center;
            pagina.Children.Add(quadro);
            raiz.Children.Add(pagina);
            janela.Content = raiz;
            try { janela.AppWindow.Resize(new Windows.Graphics.SizeInt32(640, 360)); } catch (Exception) { }
            janela.Activate();
            async Task Assentar() { for (var i = 0; i < 4; i++) { raiz.UpdateLayout(); await Task.Delay(120); } }
            await Assentar();
            var cantoOriginal = quadro.CornerRadius;
            var relogioAntes = RelogioQuadros.Quantos;

            bool Aceso() => Trabalhando.BrilhoDe(quadro) is { Rodando: true, ContornoVisivel: true };
            bool Apagado() => Trabalhando.BrilhoDe(quadro) is not { } b || (!b.Rodando && !b.ContornoVisivel && !b.HaloVisivel);

            r["ocioso_sem_brilho"] = Apagado();
            quadro.Visibility = Visibility.Visible;          // começou a carregar
            await Assentar();
            r["trabalhando_com_brilho"] = Aceso();
            r["halo_por_fora"] = Trabalhando.BrilhoDe(quadro)?.HaloVisivel == true;
            quadro.Visibility = Visibility.Collapsed;        // terminou (ou cancelou, ou deu erro: o mesmo caminho)
            await Assentar();
            r["fim_apaga"] = Apagado();
            r["cantos_restaurados"] = quadro.CornerRadius == cantoOriginal;
            r["relogio_parado"] = RelogioQuadros.Quantos == relogioAntes;
            quadro.Visibility = Visibility.Visible;
            await Assentar();
            pagina.Visibility = Visibility.Collapsed;        // trocou de módulo com o quadro ainda marcado: o defeito antigo
            await Assentar();
            r["pagina_oculta_apaga"] = Apagado();
            r["relogio_parado_com_pagina_oculta"] = RelogioQuadros.Quantos == relogioAntes;
            pagina.Visibility = Visibility.Visible;          // voltou ao módulo ainda carregando: o indicador legítimo volta
            await Assentar();
            r["indicador_legitimo_volta"] = Aceso();
            pagina.Children.Remove(quadro);                  // saiu da árvore: a camada do halo é devolvida
            await Assentar();
            r["saiu_da_arvore_apaga"] = Apagado() && Trabalhando.BrilhoDe(quadro)?.TemCamadaDoHalo != true;
            r["camadas_que_sobraram"] = pagina.Children.Count;
            r["ok"] = r.Where(x => x.Key != "ok" && x.Value is bool).All(x => x.Value is true) && pagina.Children.Count == 0;
        }
        catch (Exception e) { r["erro"] = e.GetType().Name + ": " + e.Message; }
        finally { try { janela?.Close(); } catch (Exception) { } }
        return r;
    }
}
