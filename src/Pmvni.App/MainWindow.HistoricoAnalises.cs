using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

public sealed partial class MainWindow
{
    IReadOnlyList<AnaliseHistorica> historicoAnalises = [];
    int geracaoHistorico;

    async Task CarregarHistoricoAsync()
    {
        var geracao = ++geracaoHistorico;
        var raiz = ajustes.PastaProcessosEfetiva;
        var itens = await Task.Run(() => AnaliseHistorica.Listar(raiz));
        if (fechada || geracao != geracaoHistorico) return;
        historicoAnalises = itens;
        AtualizarRecentes();
    }

    async Task EscolherDescarteAsync()
    {
        await CarregarHistoricoAsync();
        bool Pendente(AnaliseHistorica h) => h.Situacao is not ("concluida" or "descartada" or "para_revisao")
            && OrquestradorAnalise.RegistroAnaliseChatGpt(h.Registro)
            && !sessaoNR.Execucoes.Any(e => !e.Fim.IsCompleted && MesmoRegistro(e.Registro.Pasta, h.Registro));
        var itens = historicoAnalises.Where(Pendente).ToList();
        if (itens.Count == 0)
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Descartar pendência", "Nenhuma análise parada para descartar.");
            return;
        }
        var escolha = await Perguntas.Atual.EscolherNaListaAsync("Descartar pendência", "Escolha a análise que deseja retirar da retomada.",
            itens.Select(h => h.Identificacao).ToArray(), 0, "Descartar");
        if (escolha is not int i || i < 0 || i >= itens.Count) return;
        var item = AnaliseHistorica.Ler(itens[i].Processo, itens[i].Registro);
        if (item is not null && Pendente(item)) DescartarAnalisePendente(item.Processo, item.Registro);
    }

    async Task EscolherHistoricoPendenteAsync()
    {
        await CarregarHistoricoAsync();
        var itens = historicoAnalises.Where(h => h.Situacao is not ("concluida" or "descartada") &&
            (RoteiroLegado ? Provedores.Ativos.Contains(h.Provedor) : OrquestradorAnalise.RegistroAnaliseChatGpt(h.Registro))).ToList();
        if (itens.Count == 0)
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Retomar análise", "Nenhuma análise pendente foi encontrada na pasta de processos.");
            return;
        }
        var indice = await Perguntas.Atual.EscolherNaListaAsync("Retomar análise", "Selecione a análise pendente que deseja abrir.",
            itens.Select(h => h.Identificacao).ToArray(), 0, "Abrir");
        if (indice is int i && i >= 0 && i < itens.Count) await AbrirHistoricoAsync(itens[i]);
    }

    Task AbrirHistoricoAsync(AnaliseHistorica selecionada) => AbrirHistoricoAsync(selecionada.Processo, selecionada.Registro);

    async Task AbrirHistoricoAsync(Processo processo, string registro)
    {
        var item = await Task.Run(() => AnaliseHistorica.Ler(processo, registro));
        if (item is null)
        {
            await CarregarHistoricoAsync();
            MostrarMensagem(InfoBarSeverity.Warning, "Registro indisponível", "Confira se a pasta da análise está acessível.");
            return;
        }
        var conferencia = await Task.Run(() => ConferenciaNumerica.Ler(item.Processo.Pasta, item.Provedor, item.Registro));
        if (item.Situacao is "concluida" or "para_revisao")
        {
            if (item.Pacote is { } pacote) await NaFilaAsync(async () => { await AbrirPacoteAsync(pacote); });
            else if (item.Documento is { } doc)
            {
                if (!await ImportarPendenteAsync(new(item.Provedor, item.Processo, item.Registro, doc, item.Pdf, item.Situacao == "para_revisao"))) return;
            }
            else { MostrarMensagem(InfoBarSeverity.Warning, "Relatório indisponível", item.Registro); return; }
            if (conferencia is not null && !(AvisoDoModulo.IsOpen && AvisoDoModulo.Severity == InfoBarSeverity.Error))
                MostrarConferenciaDoHistorico(conferencia, item.Processo.Pasta, item.Registro);
            return;
        }
        if (item.Situacao == "descartada")
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Análise descartada", item.Registro);
            return;
        }
        if (!Provedores.Ativos.Contains(item.Provedor))
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Análise histórica", Provedores.RotaDesativada);
            return;
        }
        if (!await NovoRelatorioAsync()) return;
        if (sessaoNR.Execucoes.FirstOrDefault(e => !e.Fim.IsCompleted && !e.Abandonada && MesmoRegistro(e.Registro.Pasta, item.Registro)) is { } ativa)
        {
            MostrarExecucaoNR(ativa);
            return;
        }
        if (conferencia is not null) MostrarConferenciaDoHistorico(conferencia, item.Processo.Pasta, item.Registro);
        var (escolha, _) = await PerguntarRetomadaAsync(item.Processo, item.Registro, item.Provedor, item.Situacao.Replace('_', ' '));
        if (escolha is null) return;
        if (escolha == Provedor.ChatGpt && item.Provedor == Provedor.ChatGpt)
        {
            continuarChatGpt = new(item.Processo, item.Registro);
            await ContinuarAnaliseAsync();
        }
        else await ContinuarComOutraIaAsync(escolha.Value, item.Processo, item.Registro);
    }

    async void ExibirEvidenciaDaParcela(string origem, string registro, ConferenciaNumerica.Evidencia evidencia, bool recorte)
    {
        await TentarAsync(async () =>
        {
            using var arquivo = await Task.Run(() => AreaAnaliseLocal.AbrirAtiva(origem, registro).AbrirEvidencia(evidencia, recorte));
            using var fluxo = arquivo.AsRandomAccessStream();
            var imagem = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            if (recorte)
                await imagem.SetSourceAsync(fluxo);
            else
            {
                var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(fluxo);
                if (evidencia.Pagina > pdf.PageCount) throw new InvalidDataException("A página não existe na peça validada.");
                using var pagina = pdf.GetPage((uint)evidencia.Pagina - 1);
                using var pixels = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var escala = 2300 / Math.Max(1, Math.Max(pagina.Size.Width, pagina.Size.Height));
                await pagina.RenderToStreamAsync(pixels, new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(pagina.Size.Width * escala)),
                    DestinationHeight = (uint)Math.Max(1, Math.Round(pagina.Size.Height * escala))
                });
                pixels.Seek(0);
                await imagem.SetSourceAsync(pixels);
            }
            var detalhes = new StackPanel { Spacing = 8 };
            detalhes.Children.Add(new TextBlock { Text = evidencia.Titulo + Environment.NewLine + evidencia.Regiao,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, IsTextSelectionEnabled = true });
            detalhes.Children.Add(new Image { Source = imagem, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform });
            var janela = new Microsoft.UI.Xaml.Window { Title = recorte ? "Recorte da parcela" : "Página da parcela",
                Content = new ScrollViewer { Content = detalhes, ZoomMode = ZoomMode.Enabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            janela.Activate();
        });
    }

    void MostrarConferenciaDoHistorico(ConferenciaNumerica conferencia, string origem, string registro)
    {
        var titulo = conferencia.Titulo;
        MostrarMensagem(InfoBarSeverity.Informational, titulo, conferencia.Resumo);
        var botao = new Button { Content = "Ver índices e fontes" };
        botao.Click += async (_, _) => await TentarAsync(() => MostrarDialogoAsync(titulo, new ScrollViewer
        {
            MaxHeight = 420,
            Content = PaginaNovoRelatorio.MemoriaDeCalculo(conferencia, (evidencia, recorte) => ExibirEvidenciaDaParcela(origem, registro, evidencia, recorte)),
        }));
        AvisoDoModulo.ActionButton = botao;
    }
}
