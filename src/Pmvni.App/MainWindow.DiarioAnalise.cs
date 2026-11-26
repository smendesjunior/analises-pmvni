using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

public sealed partial class MainWindow
{
    async Task ConsultarMonitorSalvoAsync(string? trabalho)
    {
        var registros = await Task.Run(() =>
        {
            var raiz = Path.Combine(PastaDados.Raiz, OrquestradorAnalise.NomeAnaliseChatGpt);
            IEnumerable<string> pastas = trabalho is not null ? [trabalho] : !Directory.Exists(raiz) ? [] :
                Directory.GetDirectories(raiz).SelectMany(p => Directory.GetDirectories(p));
            return pastas.SelectMany(p => DiarioAnaliseChatGpt.Listar(Path.Combine(p, DiarioAnaliseChatGpt.Pasta)))
                .OrderByDescending(x => x.Consumo.Inicio).ToArray();
        });
        if (registros.Length == 0)
        {
            await MostrarDialogoAsync("Consumo e logs", new TextBlock { Text = "Ainda não há registros de monitoramento para esta seleção." });
            return;
        }
        var i = registros.Length == 1 ? 0 : await Perguntas.Atual.EscolherNaListaAsync("Consumo e logs", "Selecione uma execução.",
            registros.Select(x => $"{x.Consumo.Inicio.LocalDateTime:g} · {x.Consumo.Processo} · {x.Consumo.Estado}").ToArray(), 0, "Consultar");
        if (i is not int indice || indice < 0 || indice >= registros.Length) return;
        var (arquivo, consumo) = registros[indice];
        await MostrarDialogoAsync("Consumo e log integral", new ScrollViewer { MaxHeight = 540, Content = LeitorDoDiario(arquivo, consumo.Texto()) });
    }

    internal static ListView TerminalDoMonitor()
    {
        var terminal = new ListView
        {
            Height = 410, SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 27, 31)),
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 13,
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                """<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding}" TextWrapping="Wrap" IsTextSelectionEnabled="True" Foreground="WhiteSmoke" FontFamily="Consolas" FontSize="13"/></DataTemplate>""")
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(terminal, "Terminal da análise");
        var estilo = new Style(typeof(ListViewItem));
        estilo.Setters.Add(new Setter(Control.MinHeightProperty, 0d));
        estilo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 0, 8, 0)));
        estilo.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        terminal.ItemContainerStyle = estilo;
        return terminal;
    }

    internal static FrameworkElement LeitorDoDiario(string arquivo, string resumoInicial)
    {
        var painel = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrEmpty(resumoInicial))
            painel.Children.Add(new TextBlock { Text = resumoInicial, TextWrapping = TextWrapping.Wrap });
        var seguir = new CheckBox { Content = "Acompanhar o fim do log", IsChecked = true };
        painel.Children.Add(seguir);
        var terminal = TerminalDoMonitor();
        painel.Children.Add(terminal);
        long posicao = 0;
        bool lendo = false;
        async Task Atualizar()
        {
            if (lendo) return;
            lendo = true;
            try
            {
                var lote = await Task.Run(() => LerTrechoDoLog(DiarioAnaliseChatGpt.LogDe(arquivo), posicao));
                if (!painel.IsLoaded) return;
                if (lote.Reiniciar) terminal.Items.Clear();
                foreach (var linha in lote.Linhas) terminal.Items.Add(linha);
                posicao = lote.Proxima;
                if (seguir.IsChecked == true && lote.Linhas.Count > 0) terminal.ScrollIntoView(terminal.Items.Last());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                var aviso = "Leitura do log: " + e.Message;
                if (terminal.Items.LastOrDefault() as string != aviso) terminal.Items.Add(aviso);
            }
            finally { lendo = false; }
        }
        var pulso = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        pulso.Tick += async (_, _) => await Atualizar();
        painel.Loaded += async (_, _) => { pulso.Start(); await Atualizar(); };
        painel.Unloaded += (_, _) => pulso.Stop();
        return painel;
    }

    static (long Proxima, bool Reiniciar, List<string> Linhas) LerTrechoDoLog(string caminho, long posicao)
    {
        using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var reiniciar = arquivo.Length < posicao;
        arquivo.Position = reiniciar ? 0 : posicao;
        var proxima = arquivo.Position;
        var linhas = new List<string>();
        using var evento = new MemoryStream();
        var buffer = new byte[65536];
        int lidos;
        while ((lidos = arquivo.Read(buffer)) > 0)
        {
            var inicio = 0;
            for (int i = 0; i < lidos; i++)
            {
                if (buffer[i] != 10) continue;
                evento.Write(buffer, inicio, i - inicio);
                var texto = System.Text.Encoding.UTF8.GetString(evento.GetBuffer(), 0, (int)evento.Length);
                try
                {
                    if (JsonSerializer.Deserialize<EventoDoPlugin>(texto) is { } e)
                        texto = $"[{e.Hora.LocalDateTime:T}] {e.Titulo}" + (e.Detalhe.Length == 0 ? "" : Environment.NewLine + e.Detalhe);
                }
                catch (JsonException) { }
                for (int j = 0; j < texto.Length; j += 4096)
                    linhas.Add(texto.Substring(j, Math.Min(4096, texto.Length - j)));
                proxima = arquivo.Position - lidos + i + 1;
                if (linhas.Count >= 64) return (proxima, reiniciar, linhas);
                evento.SetLength(0);
                inicio = i + 1;
            }
            evento.Write(buffer, inicio, lidos - inicio);
        }
        return (proxima, reiniciar, linhas);
    }
}
