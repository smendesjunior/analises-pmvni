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
        var itens = historicoAnalises.Where(h => h.Situacao is not ("concluida" or "descartada")).ToList();
        if (itens.Count == 0)
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Retomar análise", "Nenhuma análise pendente foi encontrada na pasta de processos.");
            return;
        }
        var indice = await Perguntas.Atual.EscolherNaListaAsync("Retomar análise", "Escolha a execução que deseja continuar.",
            itens.Select(h => h.Identificacao).ToArray(), 0, "Abrir");
        if (indice is int i && i >= 0 && i < itens.Count) await AbrirHistoricoAsync(itens[i]);
    }

    async Task AbrirHistoricoAsync(AnaliseHistorica selecionada)
    {
        var item = await Task.Run(() => AnaliseHistorica.Ler(selecionada.Processo, selecionada.Registro));
        if (item is null) { await CarregarHistoricoAsync(); return; }
        if (item.Situacao == "concluida")
        {
            if (item.Pacote is { } pacote) await NaFilaAsync(async () => { await AbrirPacoteAsync(pacote); });
            else if (item.Documento is { } doc) await ImportarPendenteAsync(new(item.Provedor, item.Processo, item.Registro, doc, item.Pdf));
            else MostrarMensagem(InfoBarSeverity.Warning, "Relatório indisponível", item.Registro);
            return;
        }
        if (item.Situacao == "descartada")
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Análise descartada", item.Registro);
            return;
        }
        if (!await NovoRelatorioAsync()) return;
        if (sessaoNR.Execucoes.FirstOrDefault(e => !e.Fim.IsCompleted && !e.Abandonada && MesmoRegistro(e.Registro.Pasta, item.Registro)) is { } ativa)
        {
            MostrarExecucaoNR(ativa);
            return;
        }
        var (escolha, _) = await PerguntarRetomadaAsync(item.Processo, item.Registro, item.Provedor, item.Situacao.Replace('_', ' '));
        if (escolha is null) return;
        if (escolha == Provedor.ChatGpt && item.Provedor == Provedor.ChatGpt)
        {
            continuarChatGpt = new(item.Processo, item.Registro, item.Sessao);
            await ContinuarAnaliseAsync();
        }
        else await ContinuarComOutraIaAsync(escolha.Value, item.Processo, item.Registro);
    }
}
