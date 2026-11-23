using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Pmvni.App;

/// <summary>Autoteste do brilho "trabalhando" (1.0.0, correção dos halos acesos com o app ocioso): monta numa janela à parte o mesmo
/// arranjo das páginas Imóveis e Sites (uma página Grid com o quadro "Carregando…" em pílula) e confere o ciclo inteiro — ocioso sem
/// brilho, trabalhando com brilho, fim do trabalho, página recolhida com o quadro ainda marcado como visível (o defeito antigo), volta da
/// página, cantos originais devolvidos, relógio de quadros parado e camada do halo devolvida ao sair da árvore. Entra no "ok" do --autoteste.</summary>
internal static class AutotesteBrilho
{
    public static async Task<Dictionary<string, object?>> ConferirAsync(CoreWebView2Environment ambiente)
    {
        var r = new Dictionary<string, object?> { ["ok"] = false };
        Window? janela = null;
        try
        {
            janela = new Window { Title = "Autoteste do brilho" };
            var raiz = new Grid();
            var pagina = new Grid();
            var quadro = Trabalhando.Quadro("Carregando a página…");
            // Exercita o efeito legado exclusivamente na janela de diagnóstico.
            Trabalhando.SetAutomatico(((StackPanel)quadro.Child).Children.OfType<ProgressRing>().Single(), true);
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
            r["cantos_preservados_durante_trabalho"] = quadro.CornerRadius == cantoOriginal;
            r["halo_por_fora"] = Trabalhando.BrilhoDe(quadro)?.HaloVisivel == true;
            foreach (var tema in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                raiz.RequestedTheme = tema;
                await Assentar();
                r["contorno_" + tema] = quadro.ActualTheme == tema && Aceso();
            }
            r["escala_windows"] = raiz.XamlRoot.RasterizationScale;
            var movimento = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            r["preferencia_de_movimento_respeitada"] = movimento == (RelogioQuadros.Quantos > relogioAntes);
            r["modo_movimento"] = movimento ? "animado" : "estático";
            var brilho = Trabalhando.BrilhoDe(quadro)!;
            foreach (var escala in new[] { 1f, 1.5f, 2f })
            {
                var pixels = brilho.ConferirMascara(escala);
                r[$"mascara_{escala * 100:0}"] = pixels == ((int)Math.Ceiling((float)quadro.ActualWidth * escala),
                    (int)Math.Ceiling((float)quadro.ActualHeight * escala));
            }
            brilho.ConferirMascara((float)raiz.XamlRoot.RasterizationScale);
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
            var painel = new PaginaNovoRelatorio();
            var carregado = new TaskCompletionSource();
            painel.Loaded += (_, _) => carregado.TrySetResult();
            janela.Content = painel;
            await carregado.Task.WaitAsync(TimeSpan.FromSeconds(30));
            painel.MostrarExecucao(true);
            var linhas = new System.Collections.ObjectModel.ObservableCollection<string>(
                Enumerable.Range(1, 80).Select(i => $"Atividade fictícia {i}"));
            painel.LigarLinhas(linhas);
            var achados = Enumerable.Range(1, 40).Select(i => new AchadoExecucao($"{i:000}", 1, $"Item {i}", "ATENDE", false)).ToList();
            painel.MostrarAchados(achados);
            // 3.5.1: o monitor é nativo; a descoberta é achada pelo nome acessível dentro do conteúdo desenhado.
            Button? Terceiro() => Procurar(painel.ConteudoMonitor, "Item 3") as Button;
            bool Contem(string texto) => Textos(painel.ConteudoMonitor).Any(t => t.Contains(texto, StringComparison.Ordinal));
            async Task AguardarMonitor(string descricao, Func<bool> condicao)
            {
                var limite = DateTime.UtcNow.AddSeconds(30);
                do
                {
                    if (condicao()) return;
                    await Task.Delay(100);
                } while (DateTime.UtcNow < limite);
                throw new TimeoutException($"O monitor não apresentou {descricao}.");
            }
            await AguardarMonitor("a descoberta Item 3", () => Terceiro() is not null);
            // A descoberta deve continuar focada e a área visível deve ficar no mesmo ponto
            // quando a publicação de um novo achado atualiza o monitor que o analista lê.
            Terceiro()!.Focus(FocusState.Programmatic);
            painel.RolagemConteudo.ChangeView(null, 160, null, true);
            await Task.Delay(300);
            var focou = ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(painel.XamlRoot), Terceiro());
            var antes = painel.RolagemConteudo.VerticalOffset;
            linhas.Add("Novo evento fictício");
            painel.MostrarAchados([.. achados, new("999", 1, "Novo achado", "ATENDE", false)]);
            await AguardarMonitor("41 registros", () => Contem("41 registros"));
            await Task.Delay(300);
            r["foco_preservado"] = focou && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(painel.XamlRoot), Terceiro());
            var depois = painel.RolagemConteudo.VerticalOffset;
            r["rolagem_preservada"] = antes > 0 && Math.Abs(depois - antes) < 1;
            r["ok"] = r.Where(x => x.Key != "ok" && x.Value is bool).All(x => x.Value is true) && pagina.Children.Count == 0;
        }
        catch (Exception e) { r["erro"] = e.GetType().Name + ": " + e.Message; }
        finally { try { janela?.Close(); } catch (Exception) { } }
        return r;
    }

    static DependencyObject? Procurar(DependencyObject raiz, string nome)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(raiz, i);
            if (Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(filho) == nome) return filho;
            if (Procurar(filho, nome) is { } achado) return achado;
        }
        return null;
    }

    static IEnumerable<string> Textos(DependencyObject raiz)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(raiz, i);
            if (filho is TextBlock t) yield return t.Text;
            foreach (var x in Textos(filho)) yield return x;
        }
    }
}
