using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;

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

    async Task EscolherHistoricoPendenteAsync()
    {
        await CarregarHistoricoAsync();
        var itens = historicoAnalises.Where(h => Provedores.Ativos.Contains(h.Provedor) && h.Situacao is not ("concluida" or "descartada")).ToList();
        if (itens.Count == 0)
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Retomar análise", "Nenhuma análise pendente foi encontrada na pasta de processos.");
            return;
        }
        var indice = await Perguntas.Atual.EscolherNaListaAsync("Retomar análise", "Selecione a análise pendente que deseja abrir.",
            itens.Select(h => h.Identificacao).ToArray(), 0, "Abrir");
        if (indice is int i && i >= 0 && i < itens.Count) await AbrirHistoricoAsync(itens[i]);
    }

    async Task AbrirHistoricoAsync(AnaliseHistorica selecionada)
    {
        var item = await Task.Run(() => AnaliseHistorica.Ler(selecionada.Processo, selecionada.Registro));
        if (item is null) { await CarregarHistoricoAsync(); return; }
        var conferencia = await Task.Run(() => ConferenciaNumerica.Ler(item.Processo.Pasta, item.Provedor, item.Registro));
        if (item.Situacao is "concluida" or "para_revisao")
        {
            if (item.Pacote is { } pacote) await NaFilaAsync(async () => { await AbrirPacoteAsync(pacote); });
            else if (item.Documento is { } doc)
            {
                if (!await ImportarPendenteAsync(new(item.Provedor, item.Processo, item.Registro, doc, item.Pdf))) return;
            }
            else { MostrarMensagem(InfoBarSeverity.Warning, "Relatório indisponível", item.Registro); return; }
            if (conferencia is not null && !(AvisoDoModulo.IsOpen && AvisoDoModulo.Severity == InfoBarSeverity.Error))
                MostrarConferenciaDoHistorico(conferencia);
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
        if (conferencia is not null) MostrarConferenciaDoHistorico(conferencia);
        var (escolha, _) = await PerguntarRetomadaAsync(item.Processo, item.Registro, item.Provedor, item.Situacao.Replace('_', ' '));
        if (escolha is null) return;
        if (escolha == Provedor.ChatGpt && item.Provedor == Provedor.ChatGpt)
        {
            continuarChatGpt = new(item.Processo, item.Registro);
            await ContinuarAnaliseAsync();
        }
        else await ContinuarComOutraIaAsync(escolha.Value, item.Processo, item.Registro);
    }

    void MostrarConferenciaDoHistorico(ConferenciaNumerica conferencia)
    {
        var titulo = conferencia.Titulo;
        MostrarMensagem(InfoBarSeverity.Informational, titulo, conferencia.Resumo);
        var botao = new Button { Content = "Ver índices e fontes" };
        botao.Click += async (_, _) => await TentarAsync(() => MostrarDialogoAsync(titulo, new ScrollViewer
        {
            MaxHeight = 420,
            Content = new TextBlock { Text = conferencia.Detalhes, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, IsTextSelectionEnabled = true },
        }));
        AvisoDoModulo.ActionButton = botao;
    }
}
