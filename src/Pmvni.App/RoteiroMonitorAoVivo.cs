using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>Ensaio com os arquivos de uma tentativa, o timer de produção e os controles XAML visíveis.
/// O agente falso mantém o processo aberto. As duas amostras de tokens e semana são de uma sessão Terra real.
/// As seções e a amostra final de cinco horas são controladas pelo ensaio.</summary>
internal static class RoteiroMonitorAoVivo
{
    internal static async Task ConferirAsync(PaginaNovoRelatorio pagina, ExecucaoEmCurso execucao, string corpus, string pasta,
        JsonObject resultado, Func<string, Task> abrirAba, Action<Button> invocar)
    {
        var controle = Directory.CreateDirectory(Path.Combine(execucao.Processo.Pasta, ".pmvni-ai", "analise")).FullName;
        var amostras = JsonNode.Parse(File.ReadAllText(Path.Combine(corpus, "agentes", "monitor-telemetria-terra.json")))!.AsArray();
        var relatorio = new JsonObject();
        resultado["arquivos_ate_controles_winui"] = relatorio;
        var etapa = Path.Combine(controle, "engine_state.json");
        var manifesto = Path.Combine(controle, "manifesto.json");
        var telemetria = Path.Combine(controle, "TELEMETRIA-IA.JSON");

        // O provedor fictício não publica progresso. Eventos de um roteiro antigo teriam prioridade sobre o manifesto.
        void ConferirOrigem()
        {
            var pasta = execucao.Registro.PastaProgresso;
            if (Directory.Exists(pasta) && Directory.EnumerateFiles(pasta, "*.json").Any())
                throw new InvalidOperationException("Eventos de progresso legado presentes no registro de teste.");
            if (execucao.Fim.IsCompleted) throw new InvalidOperationException("O agente de teste já encerrou.");
        }
        ConferirOrigem();
        var partida = DateTimeOffset.UtcNow.AddMinutes(-4).ToUnixTimeSeconds();
        Gravar(etapa, JsonSerializer.SerializeToNode(new { stages = new {
            prepare = new { status = "EXECUTED", ended = partida }, analyze = new { status = "RUNNING", started = partida }
        } })!);
        void Secoes(int quantidade) => Gravar(manifesto, JsonSerializer.SerializeToNode(new {
            section_coverage = Enumerable.Range(1, quantidade).Select(numero => new { section = numero, status = "ANALISADO" })
        })!);
        string Amostra(int indice, bool outraExecucao = false, DateTimeOffset? curta = null)
        {
            var amostra = amostras[indice]!.DeepClone();
            amostra["processo"] = Path.GetFileName(execucao.Processo.Pasta);
            amostra["execucao_id"] = outraExecucao ? "outra-execucao" : Path.GetFileName(execucao.Registro.Pasta);
            amostra["atualizado_em"] = DateTimeOffset.UtcNow;
            if (curta is { } hora)
            {
                var cota = amostra["cota"]!;
                cota["usada_pct"] = 18;
                cota["observada_em"] = hora;
                cota["fonte"] = "Amostra de teste";
            }
            Gravar(telemetria, amostra);
            return DateTimeOffset.Parse((string)amostra["tokens"]!["observados_em"]!, CultureInfo.InvariantCulture)
                .ToLocalTime().ToString("HH:mm:ss");
        }
        string Campo(string id)
        {
            var t = Visiveis(pagina.MonitorNativo).OfType<TextBlock>().FirstOrDefault(o => AutomationProperties.GetAutomationId(o) == id);
            if (t is null) return "";
            return t.Inlines.Count == 0 ? t.Text : string.Concat(t.Inlines.OfType<Run>().Select(run => run.Text));
        }
        bool Tem(string s) => Visiveis(pagina.ConteudoMonitor).OfType<TextBlock>().Any(t => t.Text == s);
        string Descrever() => string.Join(" | ", Visiveis(pagina.MonitorNativo).OfType<TextBlock>()
            .Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Take(70));
        async Task Esperar(string nome, Func<bool> condicao)
        {
            var prazo = Stopwatch.StartNew();
            while (!condicao())
            {
                if (prazo.Elapsed >= TimeSpan.FromSeconds(8))
                    throw new InvalidOperationException(nome + " — valores visíveis: " + Descrever());
                await Task.Delay(120);
            }
            relatorio[nome] = true;
        }

        Secoes(7);
        await abrirAba("Resumo");
        const string campoNumerico = "conferencia-numerica-resumo";
        await Esperar("conferencia_numerica_ausente", () => Campo(campoNumerico) == "");
        var pastaNumerica = Directory.CreateDirectory(Path.Combine(controle, "local")).FullName;
        var reciboNumerico = Path.Combine(pastaNumerica, Pmvni.Agentes.ConferenciaNumerica.ArquivoControle);
        var idExecucao = Path.GetFileName(execucao.Registro.Pasta);
        var reciboTeste = JsonSerializer.SerializeToNode(new
        {
            versao = 1, execucao = idExecucao, rota = "codex", fase = "montagem",
            linhas = new[] {
                new { indice = "to", situacao = "DIVERGENTE", declarado = "18,5%", calculado = "19%", fontes = new[] { "Prancha fictícia A-03" }, motivo = "Área do térreo." },
                new { indice = "ca", situacao = "INSUFICIENTE", declarado = "1,37", calculado = "", fontes = new[] { "Memorial fictício" }, motivo = "Falta área computável." },
                new { indice = "tp", situacao = "CONFERE", declarado = "11,05%", calculado = "11,05%", fontes = new[] { "Quadro fictício de áreas" }, motivo = "Área permeável dividida pela área do lote." } },
        })!;
        Gravar(reciboNumerico, reciboTeste);
        await Esperar("conferencia_numerica_visivel", () => Campo(campoNumerico).Contains("Divergentes: 1"));
        var expansor = Visiveis(pagina.MonitorNativo).OfType<Expander>().Single(e => Equals(e.Header, "Ver índices e fontes"));
        expansor.IsExpanded = true;
        await Esperar("conferencia_numerica_com_valores_e_fonte", () => Visiveis(pagina.MonitorNativo).OfType<TextBlock>()
            .Any(t => t.Text.Contains("18,5%") && t.Text.Contains("19%") && t.Text.Contains("Prancha fictícia A-03")));
        await Esperar("conferencia_mostra_confirmado_e_insuficiente", () => Visiveis(pagina.MonitorNativo).OfType<TextBlock>()
            .Any(t => t.Text.Contains("TP · CONFERE · declarado 11,05% · calculado 11,05%") && t.Text.Contains("Quadro fictício de áreas")
                && t.Text.Contains("CA · INSUFICIENTE · declarado 1,37 · calculado não disponível") && t.Text.Contains("Falta área computável.")));
        reciboTeste["execucao"] = "outra-execucao";
        Gravar(reciboNumerico, reciboTeste);
        await Esperar("conferencia_numerica_de_outro_registro_recusada", () => Campo(campoNumerico) == "");
        reciboTeste["execucao"] = idExecucao;
        Gravar(reciboNumerico, reciboTeste);
        var primeiraHora = Amostra(0);
        await abrirAba("Consumo");
        await Esperar("primeira_amostra", () => Tem("Tokens totais · 23,1 mil") && Tem("Medidos às " + primeiraHora)
            && Campo("weeklyPercent") == "53%" && Campo("shortPercent") == "—" && Tem("sem leitura"));
        await abrirAba("Etapas");
        await Esperar("janela_nativa_visivel", () => pagina.MonitorNativo.XamlRoot?.IsHostVisible == true);
        await Esperar("sete_de_28", () => Campo("stagePercent") == "25%" && Campo("result").Contains("7 de 28") && Campo("percent") == "21%");
        var geralInicial = Campo("percent");
        await Esperar("previsao_visivel", () => Campo("eta").Contains(':') && Campo("eta").Contains('·'));
        var etaInicial = Campo("eta");
        var antes = Campo("elapsed");
        await Esperar("relogio_em_andamento", () => Campo("elapsed") is { Length: > 0 } tempo && tempo != antes);
        if (Campo("percent") != geralInicial || Campo("stagePercent") != "25%")
            throw new InvalidOperationException("O progresso mudou apenas pela passagem do tempo.");
        relatorio["sete_secoes"] = new JsonObject { ["etapa"] = "25%", ["geral"] = geralInicial, ["eta"] = etaInicial,
            ["relogio_antes"] = antes, ["relogio_depois"] = Campo("elapsed") };

        Secoes(14);
        var ultimaHora = Amostra(1);
        await Esperar("quatorze_de_28", () => Campo("stagePercent") == "50%" && Campo("result").Contains("14 de 28")
            && Campo("percent") == "25%" && Campo("eta").Contains(':') && Campo("eta") != etaInicial);
        var geralMedido = Campo("percent");
        relatorio["quatorze_secoes"] = new JsonObject { ["etapa"] = "50%", ["geral"] = geralMedido, ["eta"] = Campo("eta") };
        await abrirAba("Consumo");
        bool AmostraFinal() => Tem("Tokens totais · 270,7 mil") && Tem("Medidos às " + ultimaHora)
            && Campo("weeklyPercent") == "54%" && Campo("cachePercent") == "91,4%";
        await Esperar("segunda_amostra", () => AmostraFinal() && Campo("shortPercent") == "—");
        Amostra(1, outraExecucao: true);
        await Esperar("registro_alheio_recusado", () => !Tem("Tokens totais · 270,7 mil") && Campo("weeklyPercent") == "—");

        invocar(pagina.BotaoNovaAnalise);
        await Esperar("monitor_fechado", () => !pagina.VistaDaExecucao);
        var horaCurta = DateTimeOffset.Now;
        Amostra(1, curta: horaCurta);
        invocar(pagina.BotaoExecucoes);
        await Esperar("monitor_reaberto", () => pagina.VistaDaExecucao && pagina.MonitorNativo.IsLoaded);
        await abrirAba("Consumo");
        var horaSemana = DateTimeOffset.Parse((string)amostras[1]!["cota"]!["semana"]!["observada_em"]!, CultureInfo.InvariantCulture).ToLocalTime();
        await Esperar("duas_janelas_na_reabertura", () => AmostraFinal() && Campo("shortPercent") == "18%"
            && Tem(horaCurta.ToString("dd/MM HH:mm:ss")) && Tem(horaSemana.ToString("dd/MM HH:mm:ss")) && Campo("percent") == geralMedido);
        await Roteiro.CapturarXamlAsync(pagina.MonitorNativo, Path.Combine(pasta, "monitor-nativo-consumo.png"));
        await abrirAba("Etapas");
        await Esperar("etapa_preservada_na_reabertura", () => Campo("stagePercent") == "50%" && Campo("result").Contains("14 de 28"));
        await Roteiro.CapturarXamlAsync(pagina.MonitorNativo, Path.Combine(pasta, "monitor-nativo-etapas.png"));
        ConferirOrigem();
        relatorio["segunda_amostra_renderizada"] = new JsonObject { ["tokens"] = "270,7 mil", ["cache"] = "91,4%",
            ["semana_real"] = "54%", ["cinco_horas_controlada"] = "18%", ["hora_da_medicao"] = ultimaHora };
        await abrirAba("Resumo");
        await Esperar("conferencia_numerica_na_reabertura", () => Campo(campoNumerico).Contains("Divergentes: 1"));
    }

    static void Gravar(string caminho, JsonNode conteudo)
    {
        var temporario = caminho + ".roteiro-tmp";
        File.WriteAllText(temporario, conteudo.ToJsonString());
        File.Move(temporario, caminho, overwrite: true);
    }

    // Começa na árvore da janela nativa. Ignora subárvores recolhidas e elementos sem layout.
    static IEnumerable<FrameworkElement> Visiveis(DependencyObject raiz)
    {
        if (raiz is UIElement { Visibility: Visibility.Collapsed }) yield break;
        if (raiz is FrameworkElement { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } elemento) yield return elemento;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
            foreach (var filho in Visiveis(VisualTreeHelper.GetChild(raiz, i))) yield return filho;
    }
}

