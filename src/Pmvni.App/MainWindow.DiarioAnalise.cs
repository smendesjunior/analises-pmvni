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

    static readonly JsonSerializerOptions JsonDoDetalhe = new(DiarioAnaliseChatGpt.Json) { WriteIndented = true };

    internal static FrameworkElement LeitorDoDiario(string arquivo, string resumoInicial)
    {
        var painel = new StackPanel { Spacing = 6 };
        var resumo = new TextBlock { Text = resumoInicial, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        painel.Children.Add(resumo);
        var controles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var anterior = new Button { Content = "Página anterior" };
        var proximo = new Button { Content = "Próxima página" };
        var atualizar = new Button { Content = "Atualizar" };
        foreach (var botao in new[] { anterior, proximo, atualizar }) controles.Children.Add(botao);
        painel.Children.Add(controles);
        var seguir = new CheckBox { Content = "Acompanhar os eventos mais recentes", IsChecked = true };
        painel.Children.Add(seguir);
        var lista = new ListView { Height = 140, SelectionMode = ListViewSelectionMode.Single };
        var posicao = new TextBlock();
        painel.Children.Add(posicao);
        painel.Children.Add(lista);
        var detalhe = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 166 };
        ScrollViewer.SetVerticalScrollBarVisibility(detalhe, ScrollBarVisibility.Auto);
        painel.Children.Add(detalhe);
        var pagina = 0;
        const int quantidade = 40;
        List<EventoDoPlugin> exibidos = [];
        bool carregando = false;
        const int tamanhoDoTrecho = 4096;
        string textoSelecionado = "";
        int inicioDoTrecho = 0;
        var trechos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
        var voltar = new Button { Content = "Trecho anterior" };
        var avancar = new Button { Content = "Próximo trecho" };
        trechos.Children.Add(voltar);
        trechos.Children.Add(avancar);
        painel.Children.Add(trechos);
        void MostrarTrecho()
        {
            var tamanho = Math.Min(tamanhoDoTrecho, textoSelecionado.Length - inicioDoTrecho);
            detalhe.Text = textoSelecionado.Substring(inicioDoTrecho, tamanho);
            trechos.Visibility = textoSelecionado.Length > tamanhoDoTrecho ? Visibility.Visible : Visibility.Collapsed;
            voltar.IsEnabled = inicioDoTrecho > 0;
            avancar.IsEnabled = inicioDoTrecho + tamanho < textoSelecionado.Length;
        }
        voltar.Click += (_, _) => { inicioDoTrecho -= tamanhoDoTrecho; MostrarTrecho(); };
        avancar.Click += (_, _) => { seguir.IsChecked = false; inicioDoTrecho += tamanhoDoTrecho; MostrarTrecho(); };
        lista.SelectionChanged += (_, _) =>
        {
            inicioDoTrecho = 0;
            textoSelecionado = "";
            if (lista.SelectedIndex is int n && n >= 0 && n < exibidos.Count)
            {
                if (!carregando && n != exibidos.Count - 1) seguir.IsChecked = false;
                textoSelecionado = exibidos[n].Detalhe;
                if (textoSelecionado.Length <= tamanhoDoTrecho && (textoSelecionado.StartsWith('{') || textoSelecionado.StartsWith('[')))
                {
                    try
                    {
                        using var json = JsonDocument.Parse(textoSelecionado);
                        textoSelecionado = JsonSerializer.Serialize(json.RootElement, JsonDoDetalhe);
                    }
                    catch (JsonException) { }
                }
            }
            MostrarTrecho();
        };
        long tamanhoLido = 0;
        var paginas = new List<long> { 0 };
        var buffer = new byte[65536];
        int total = 0;
        async Task CarregarAsync()
        {
            if (carregando) return;
            carregando = true;
            var indiceAnterior = lista.SelectedIndex;
            var acompanhar = seguir.IsChecked == true;
            seguir.IsEnabled = false;
            anterior.IsEnabled = proximo.IsEnabled = atualizar.IsEnabled = false;
            try
            {
                var (proxima, novoResumo) = await Task.Run(() =>
                {
                    var eventos = new List<EventoDoPlugin>();
                    using var fs = new FileStream(DiarioAnaliseChatGpt.LogDe(arquivo), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    // Indexa as quebras de linha novas. Cada atualização lê somente a página solicitada.
                    fs.Position = tamanhoLido;
                    var tamanho = fs.Length;
                    while (fs.Position < tamanho)
                    {
                        var inicio = fs.Position;
                        var lidos = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, tamanho - inicio));
                        if (lidos == 0) break;
                        for (var j = 0; j < lidos; j++)
                            if (buffer[j] == (byte)'\n' && ++total % quantidade == 0) paginas.Add(inicio + j + 1);
                    }
                    tamanhoLido = fs.Position;
                    if (acompanhar) pagina = Math.Max(0, total - 1) / quantidade;
                    fs.Position = paginas[pagina];
                    using var leitor = new StreamReader(fs);
                    var nestaPagina = Math.Min(quantidade, total - pagina * quantidade);
                    while (eventos.Count < nestaPagina && leitor.ReadLine() is { } linha)
                    {
                        if (JsonSerializer.Deserialize<EventoDoPlugin>(linha) is { } evento) eventos.Add(evento);
                    }
                    return (eventos, DiarioAnaliseChatGpt.LerConsumo(arquivo)?.Texto());
                });
                proximo.IsEnabled = (pagina + 1) * quantidade < total;
                exibidos = proxima;
                lista.ItemsSource = exibidos.Select(x => $"{x.Hora.LocalDateTime:T} · {x.Titulo}").ToList();
                detalhe.Text = "";
                posicao.Text = $"Página {pagina + 1} · Eventos {Math.Min(pagina * quantidade + 1, pagina * quantidade + exibidos.Count)} a {pagina * quantidade + exibidos.Count}";
                lista.SelectedIndex = acompanhar ? exibidos.Count - 1 : exibidos.Count > 0 ? Math.Clamp(indiceAnterior, 0, exibidos.Count - 1) : -1;
                if (lista.SelectedItem is { } selecionado) lista.ScrollIntoView(selecionado);
                if (novoResumo is not null && resumo.Text != novoResumo) resumo.Text = novoResumo;
            }
            catch (JsonException) { detalhe.Text = "Esta página contém um evento com formato inválido."; }
            catch (IOException e) { detalhe.Text = "Falha ao ler o registro. " + e.Message; }
            catch (UnauthorizedAccessException e) { detalhe.Text = "Acesso ao registro indisponível. " + e.Message; }
            finally { carregando = false; seguir.IsEnabled = true; atualizar.IsEnabled = true; anterior.IsEnabled = pagina > 0; }
        }
        anterior.Click += async (_, _) => { seguir.IsChecked = false; pagina--; await CarregarAsync(); };
        proximo.Click += async (_, _) => { seguir.IsChecked = false; pagina++; await CarregarAsync(); };
        seguir.Checked += async (_, _) => await CarregarAsync();
        atualizar.Click += async (_, _) => await CarregarAsync();
        var pulso = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        pulso.Tick += async (_, _) =>
        {
            if (carregando || seguir.IsChecked != true) return;
            try
            {
                var mudou = await Task.Run(() => new FileInfo(DiarioAnaliseChatGpt.LogDe(arquivo)).Length != tamanhoLido);
                if (mudou && painel.IsLoaded && seguir.IsChecked == true) await CarregarAsync();
            }
            catch (IOException) { }
        };
        painel.Loaded += async (_, _) => { pulso.Start(); await CarregarAsync(); };
        painel.Unloaded += (_, _) => pulso.Stop();
        return painel;
    }
}
