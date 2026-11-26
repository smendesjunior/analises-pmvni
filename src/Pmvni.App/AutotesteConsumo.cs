using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pmvni.Agentes;

namespace Pmvni.App;

internal static class AutotesteConsumo
{
    internal static async Task ConferirAsync()
    {
        var pasta = Directory.CreateTempSubdirectory("pmvni-teste-consumo-");
        var janela = new Window { Title = "Autoteste do monitor de consumo" };
        var pagina = new PaginaNovoRelatorio();
        try
        {
            var agora = DateTimeOffset.UtcNow;
            var arquivo = Path.Combine(pasta.FullName, "teste" + DiarioAnaliseChatGpt.Sufixo);
            var consumo = new ConsumoDoPlugin("Processo fictício", agora.AddMinutes(-2))
            {
                Entrada = 1000, Cache = 250, Saida = 200, TurnosMedidos = 1,
                Ultima = new AmostraDaConta(agora, "teste", "teste", [new(10080, 11, null)])
            };
            File.WriteAllText(arquivo, JsonSerializer.Serialize(consumo));
            File.WriteAllText(DiarioAnaliseChatGpt.LogDe(arquivo), JsonSerializer.Serialize(
                new EventoDoPlugin(agora, "teste", "Evento grande", new string('x', 1_000_000))) + "\n");
            pagina.ConsumoDoPlugin = consumo;
            pagina.ArquivoDoDiario = arquivo;
            janela.Content = pagina;
            janela.Activate();
            await Esperar(() => pagina.IsLoaded);
            pagina.MostrarExecucao(true);
            void Abrir(string nome) => pagina.MenuMonitor.SelectedItem = pagina.MenuMonitor.MenuItems
                .OfType<NavigationViewItem>().Single(i => Equals(i.Content, nome));
            string? Valor(string id) => Controles(pagina.ConteudoMonitor).OfType<TextBlock>()
                .FirstOrDefault(t => AutomationProperties.GetAutomationId(t) == id)?.Text;
            Abrir("Consumo");
            await Esperar(() => Valor("weeklyPercent") == "11%" && Valor("shortPercent") == "—" && Valor("cachePercent") == "25%");
            Abrir("Log");
            await Esperar(() => Controles(pagina.ConteudoMonitor).OfType<ListView>().Any(t => t.Items.Count > 200 && t.Items[0].ToString()!.Contains("Evento grande")));
            File.AppendAllText(DiarioAnaliseChatGpt.LogDe(arquivo), JsonSerializer.Serialize(
                new EventoDoPlugin(agora, "teste", "Última linha", "Fim do terminal")) + "\n");
            await Esperar(() => Controles(pagina.ConteudoMonitor).OfType<ListView>().Any(t =>
                Equals(t.Items.LastOrDefault(), "Fim do terminal") && Controles(t).OfType<ScrollViewer>()
                    .Any(v => v.ScrollableHeight > 0 && v.VerticalOffset >= v.ScrollableHeight - 2)));
            Abrir("Consumo");
            await Esperar(() => Valor("weeklyPercent") == "11%");
        }
        finally
        {
            janela.Close();
            pasta.Delete(true);
        }
    }

    static async Task Esperar(Func<bool> pronto)
    {
        var prazo = System.Diagnostics.Stopwatch.StartNew();
        while (!pronto())
        {
            if (prazo.Elapsed.TotalSeconds > 12) throw new TimeoutException("A página de consumo ou log não respondeu ao autoteste.");
            await Task.Delay(80);
        }
    }

    static IEnumerable<DependencyObject> Controles(DependencyObject raiz)
    {
        yield return raiz;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
            foreach (var item in Controles(VisualTreeHelper.GetChild(raiz, i))) yield return item;
    }
}
