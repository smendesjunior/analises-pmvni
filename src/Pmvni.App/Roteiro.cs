using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Pmvni.Agentes;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Legislacao;
using Pmvni.Legislacao;
using Pmvni.Revisao;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Pmvni.App;

/// <summary>Respostas fixas do roteiro: nenhum seletor nem diálogo abre (ninguém está olhando a janela). Confirmar responde
/// <see cref="Confirmar"/>; cada pergunta fica registrada.</summary>
internal sealed class PerguntasFixas : IPerguntas
{
    public Task<string?> PedirTextoAsync(string titulo, string rotulo, string sugestao, string botao = "Criar") => Task.FromResult<string?>(sugestao);

    public bool Confirmar { get; set; } = true;
    public RespostaRecuperacao Recuperacao { get; set; } = RespostaRecuperacao.Descartar;
    public List<string> Feitas { get; } = [];

    /// <summary>O arquivo que o seletor de abrir "escolhe" (null = cancelado); vale uma vez e volta a null.</summary>
    public string? ArquivoParaAbrir { get; set; }
    public Task<string?> AbrirArquivoAsync(string extensao)
    {
        Feitas.Add("abrir " + extensao);
        var escolhido = ArquivoParaAbrir;
        ArquivoParaAbrir = null;
        return Task.FromResult(escolhido);
    }
    /// <summary>As listas que o seletor múltiplo com filtro ("Anexar PDFs…") "escolhe", uma por chamada, na ordem; sem lista = cancelado.</summary>
    public Queue<IReadOnlyList<string>> ListasParaAbrir { get; } = new();
    public Task<IReadOnlyList<string>> AbrirArquivosAsync(string extensao)
    {
        Feitas.Add("abrir vários " + extensao);
        return Task.FromResult(ListasParaAbrir.TryDequeue(out var l) ? l : (IReadOnlyList<string>)[]);
    }
    /// <summary>O caminho que o seletor de salvar "escolhe" (null = cancelado).</summary>
    public string? ArquivoParaSalvar { get; set; }
    public Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao) { Feitas.Add("salvar " + nomeSugerido); return Task.FromResult(ArquivoParaSalvar); }
    /// <summary>A pasta que o seletor de pastas "escolhe" (null = cancelado).</summary>
    public string? PastaEscolhida { get; set; }
    public Task<string?> EscolherPastaAsync() { Feitas.Add("escolher pasta"); return Task.FromResult(PastaEscolhida); }
    public Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao, bool destrutiva = true)
    {
        Feitas.Add("confirmar " + titulo);
        Destrutivas[titulo] = destrutiva;
        Confirmacoes.Add((titulo, texto, sim, nao));
        return Task.FromResult(RespostaPorTitulo.TryGetValue(titulo, out var r) ? r : Confirmar);
    }
    /// <summary>Se a última confirmação de cada título era destrutiva (Enter = "não") ou não (Enter = botão principal).</summary>
    public Dictionary<string, bool> Destrutivas { get; } = new(StringComparer.Ordinal);
    /// <summary>Respostas por título da confirmação (as outras seguem <see cref="Confirmar"/>).</summary>
    public Dictionary<string, bool> RespostaPorTitulo { get; } = new(StringComparer.Ordinal);
    public List<(string Titulo, string Texto, string Sim, string Nao)> Confirmacoes { get; } = [];
    /// <summary>Os arquivos que "Anexar arquivos…" escolhe (null = cancelado).</summary>
    public IReadOnlyList<string>? ArquivosParaAnexar { get; set; }
    public Task<IReadOnlyList<string>?> AbrirArquivosAsync() { Feitas.Add("anexar arquivos"); return Task.FromResult(ArquivosParaAnexar); }
    /// <summary>O que o diálogo "Criar processo" responde (null = cancelado).</summary>
    public DadosNovoProcesso? NovoProcesso { get; set; }
    public Task<DadosNovoProcesso?> PedirNovoProcessoAsync() { Feitas.Add("criar processo"); return Task.FromResult(NovoProcesso); }
    public Task InformarAsync(string titulo, string texto) { Feitas.Add("informar " + titulo); Informados.Add((titulo, texto)); return Task.CompletedTask; }
    public List<(string Titulo, string Texto)> Informados { get; } = [];
    public Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos) { Feitas.Add($"recuperar {comandos}"); return Task.FromResult(Recuperacao); }
    public RespostaFechar Fechar { get; set; } = RespostaFechar.FecharSemSalvar;
    public Task<RespostaFechar> FecharSemGravarAsync(string motivo, bool conflito)
    {
        Feitas.Add("fechar sem gravar" + (conflito ? " (conflito)" : "") + ": " + motivo);
        Fechamentos.Add(TextosGravacao.FecharSemGravar(motivo, conflito));
        return Task.FromResult(Fechar);
    }
    public List<string> Fechamentos { get; } = [];
    /// <summary>A resposta a "O texto em edição não pôde ser aplicado… Fechar mesmo assim?": true = fechar sem o texto.</summary>
    public bool FecharSemOTexto { get; set; }
    public Task<bool> FecharSemOTextoAsync(string motivo)
    {
        Feitas.Add("fechar com texto recusado: " + motivo);
        Fechamentos.Add(TextosGravacao.FecharSemOTexto(motivo));
        return Task.FromResult(FecharSemOTexto);
    }
    /// <summary>Tarefa 14: o índice da versão que "Qual versão da peça vale?" escolhe (null = cancelar); cada pergunta fica em <see cref="EscolhasDePeca"/>.</summary>
    public int? VersaoDaPeca { get; set; }
    public List<(string Titulo, string Motivo, IReadOnlyList<PecaCandidata> Versoes)> EscolhasDePeca { get; } = [];
    public Task<string?> EscolherPecaAsync(string titulo, string motivo, string aviso, IReadOnlyList<PecaCandidata> versoes)
    {
        Feitas.Add("escolher peça " + titulo);
        EscolhasDePeca.Add((titulo, motivo, versoes));
        return Task.FromResult(VersaoDaPeca is { } i && i >= 0 && i < versoes.Count ? versoes[i].Valor : null);
    }
}

/// <summary>Computador sem LibreOffice (passo 38 do roteiro): o despacho sai só em DOCX, com o motivo no recibo.</summary>
internal sealed class ConversorSemLibreOffice : IConversorPdf
{
    public bool Disponivel => false;

    public string? Versao => null;

    public ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct) => new(null, "SEM_LIBREOFFICE", null);
}

/// <summary>O conversor real, medido (onda 1, item 11): quanto levou cada conversão, na ordem.</summary>
internal sealed class ConversorMedido(IConversorPdf real) : IConversorPdf
{
    public List<long> Milissegundos { get; } = [];

    public bool Disponivel => real.Disponivel;

    public string? Versao => real.Versao;

    public ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct)
    {
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        try { return real.Converter(docx, pastaTrabalho, ct); }
        finally { lock (Milissegundos) Milissegundos.Add(relogio.ElapsedMilliseconds); }
    }
}

/// <summary>Modo de diagnóstico <c>--roteiro &lt;saida.json&gt;</c>: exercita o shell e o módulo Relatórios pelos mesmos caminhos dos
/// comandos da faixa (importar, rolagem, zoom, navegação, exportação, corpus com estouro, tema escuro, campos em aberto, pacote mudado no
/// disco, faixa por módulo, reabrir o pacote; Tarefa 9: seleção e edição no A4, situação e justificativa, exclusão e marca, desfazer e
/// refazer, salvar/reabrir/exportar, menu de contexto e atalhos, latência; Tarefa 10: salvamento automático, gravação que falha sem
/// perder nada, recuperação depois de uma queda, trava e somente leitura, conflito sem sobrescrever; Tarefa 11: pesquisa no relatório,
/// tema do app, Recentes e Comparar com o original; Tarefa 13: módulos Processos e Despachos sobre uma cópia da árvore fictícia, pasta de processos
/// nos Ajustes, faixa e pesquisa por módulo, pastas intactas; Tarefa 16: Novo despacho de correção e de aprovação, sem LibreOffice e Gerar PDF,
/// nomes sem sobrescrever e bloqueios, sobre outra cópia da árvore fictícia, com o kit real só lido e o LibreOffice real) e grava o resultado em JSON. Tudo o que o roteiro cria (pacotes, PDFs, capturas PNG) fica
/// na pasta do &lt;saida.json&gt; (tests\saida\roteiro). As perguntas têm respostas fixas (<see cref="PerguntasFixas"/>). Cada passo é
/// independente: falha num não impede os seguintes.</summary>
internal static class Roteiro
{
    static readonly List<string> programasIniciados = [];
    static string pastaDoAgenteFalso = null!;
    static string? sofficeDoRoteiro;
    static IReadOnlyList<string> programasPermitidos = [];

    internal static void TravarProgramas(string resultado)
    {
        var saida = Path.GetDirectoryName(Path.GetDirectoryName(resultado)!)!;
        pastaDoAgenteFalso = Path.Combine(saida, "agente-falso");
        Environment.SetEnvironmentVariable(PastaDados.VariavelAmbiente, Path.Combine(saida, "dados-app"));
        var soffice = ConversorLibreOffice.Localizar();
        sofficeDoRoteiro = soffice is not null && Path.GetFileName(soffice).Equals("soffice.com", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(soffice) : null;
        programasPermitidos = sofficeDoRoteiro is null ? [pastaDoAgenteFalso] : [pastaDoAgenteFalso, sofficeDoRoteiro];
        TravaDeProgramas.Permitidos = programasPermitidos;
        TravaDeProgramas.AoIniciar = c => { lock (programasIniciados) programasIniciados.Add(c); };
    }

    internal static OpcoesLocalizacao OpcoesDosAgentes(string resultado, string kit, string? codex, string? python)
    {
        var pasta = Path.GetDirectoryName(resultado)!;
        var agenteFalso = Path.Combine(pastaDoAgenteFalso, "AgenteFalso.exe");
        return new OpcoesLocalizacao(Path.Combine(pasta, "local-app-data"), Path.Combine(pasta, "codex-home"), [pastaDoAgenteFalso],
            kit, codex ?? agenteFalso, python ?? agenteFalso);
    }

    public static async Task ExecutarAsync(MainWindow j, Argumentos args)
    {
        var dadosLongo = args.Dados ?? throw new InvalidOperationException("--roteiro pede também o caminho do dados.json (tests\\corpus\\longo.json).");
        var corpus = Path.GetDirectoryName(dadosLongo)!;
        var pasta = Path.GetDirectoryName(args.Roteiro!)!;
        Directory.CreateDirectory(pasta);
        // Progresso em <pasta>\progresso.log (uma linha por etapa que começa e que termina): um roteiro que trava mostra onde.
        var progresso = Path.Combine(pasta, "progresso.log");
        File.WriteAllText(progresso, "");
        void Registrar(string linha) { try { File.AppendAllText(progresso, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + linha + Environment.NewLine); } catch (IOException) { } }
        Registrar("início da limpeza das rodadas anteriores");
        var perguntas = new PerguntasFixas();
        Perguntas.Atual = perguntas;
        // Tarefa 13: Processos e Despachos leem uma CÓPIA da árvore fictícia (nunca a pasta real de processos) e nenhum arquivo é aberto fora
        // do app (as aberturas só são registradas). A cópia e a "fotografia" dela (caminho → sha256) são feitas antes de qualquer módulo.
        AberturaExterna.Simular = true;
        // Tarefa 14: a trava de programas vale no roteiro inteiro (desde o passo 1) e para todo programa que o editor inicia: só o agente falso
        // (a pasta tests\saida\agente-falso) e o soffice do LibreOffice dos despachos (o programa exato que o editor acha) podem partir; cada
        // programa que partiu fica listado no resultado (programas_iniciados), o LibreOffice inclusive.
        var inicioDoRoteiro = DateTime.UtcNow;
        // As chaves falsas que os passos 50 e 53 sorteiam (e o valor herdado do 53): o passo 64 confere que nenhuma ficou em tests\saida.
        var chaveHerdada = "herdada-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var chavesDoRoteiro = new List<string> { chaveHerdada };
        var copiaProcessos = Path.Combine(pasta, "processos");
        if (Directory.Exists(copiaProcessos)) ApagarArvore(copiaProcessos);
        CopiarArvore(Path.Combine(corpus, "processos"), copiaProcessos);
        var antesDasPastas = FotografarArvore(copiaProcessos);
        j.DefinirPastaProcessos(copiaProcessos);
        var pacoteLongo = Path.Combine(pasta, "longo.rpmvni");
        // Pacotes de rodadas anteriores (só os do roteiro, só nesta pasta), com travas, cópias de conflito e diários de recuperação
        // (na pasta de dados do roteiro, tests\saida\dados-app): a importação nunca sobrescreve e nada de antes vira "recuperação".
        var recuperacao = Path.Combine(PastaDados.Raiz, "recuperacao");
        foreach (var nome in new[] { "longo", "estouro", "curto-colchetes", "curto-muda", "curto-auto", "recentes-a", "recentes-b", "curto-citacoes", "onda1", "rever-sem-ia" })
        {
            var arquivos = Directory.GetFiles(pasta, nome + ".rpmvni*").Concat(Directory.GetFiles(pasta, ".~" + nome + ".rpmvni*"))
                .Concat(Directory.GetFiles(pasta, nome + " (cópia *")).Concat(Directory.GetFiles(pasta, ".~" + nome + " (cópia *")).ToList();
            // ToList: o laço abaixo acrescenta a "arquivos" (uma consulta preguiçosa sobre ela falharia numa rodada com restos da anterior).
            var pacotes = arquivos.Where(a => a.EndsWith(".rpmvni", StringComparison.OrdinalIgnoreCase)).Append(Path.Combine(pasta, nome + ".rpmvni")).ToList();
            if (Directory.Exists(recuperacao))
                foreach (var pc in pacotes)
                    arquivos.AddRange(Directory.GetFiles(recuperacao, Path.GetFileNameWithoutExtension(Diario.CaminhoDe(PastaDados.Raiz, pc)) + "*"));
            foreach (var a in arquivos.Distinct()) { File.SetAttributes(a, FileAttributes.Normal); File.Delete(a); }
        }
        // Tarefa 10: salvamento automático com 15 s nos ajustes do roteiro (o menor intervalo permitido).
        Registrar("limpeza concluída");
        j.DefinirIntervaloAutosave(15);
        var saida = new JsonObject
        {
            ["gerado_em"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["webview2"] = j.NucleoWeb.Environment.BrowserVersionString,
            ["escala_do_monitor"] = j.ElementoRaiz.XamlRoot?.RasterizationScale,
            ["janela_px"] = new JsonObject { ["largura"] = j.ElementoRaiz.ActualWidth, ["altura"] = j.ElementoRaiz.ActualHeight },
        };
        var passos = new JsonObject();
        saida["passos"] = passos;
        var falhas = new JsonArray();
        string? planoDaImportacao = null;

        var somenteMonitor = Environment.GetEnvironmentVariable("PMVNI_ROTEIRO_MONITOR") == "1";
        var somenteAjustes = Environment.GetEnvironmentVariable("PMVNI_ROTEIRO_AJUSTES") == "1";
        async Task Passo(string nome, Func<JsonObject, Task> corpoPasso)
        {
            if (somenteAjustes && nome is not ("plugins_e_ferramentas" or "modelos_da_analise")) return;
            if (somenteMonitor && nome != "monitor_html_controles") return;
            var p = new JsonObject();
            Registrar("início " + nome);
            try { await corpoPasso(p); p["ok"] ??= true; }
            catch (Exception e) { p["ok"] = false; p["excecao"] = e.GetType().Name + ": " + e.Message; }
            passos[nome] = p;
            if (p["ok"]?.GetValue<bool>() != true) falhas.Add(nome);
            Registrar("fim " + nome + (p["ok"]?.GetValue<bool>() == true ? " ok" : " FALHOU"));
        }

        await Passo("rotulo_de_composicao", async p =>
        {
            j.Ocupar("Compondo as folhas…");
            try
            {
                await Task.Delay(220);
                var pagina = j.ElementoRaiz.FindName("Relatorios") as Microsoft.UI.Xaml.FrameworkElement;
                var espera = (Microsoft.UI.Xaml.Controls.Grid)pagina!.FindName("Espera");
                var borda = (Microsoft.UI.Xaml.Controls.Border)espera.Children[0];
                var texto = (Microsoft.UI.Xaml.Controls.TextBlock)pagina.FindName("TextoEspera");
                var anel = (Microsoft.UI.Xaml.Controls.ProgressRing)pagina.FindName("Anel");
                var pos = texto.TransformToVisual(borda).TransformPoint(default);
                var anelPos = anel.TransformToVisual(borda).TransformPoint(default);
                p["indicador_e_texto_inteiros"] = anelPos.X >= 0 && anelPos.Y >= 0
                    && anelPos.Y + anel.ActualHeight <= borda.ActualHeight && anelPos.X + anel.ActualWidth <= pos.X
                    && pos.Y >= 0 && pos.Y + texto.ActualHeight <= borda.ActualHeight && pos.X + texto.ActualWidth <= borda.ActualWidth;
                p["ok"] = p["indicador_e_texto_inteiros"]!.GetValue<bool>() && borda.ActualHeight > 0;
            }
            finally { j.Liberar(); }
        });

        await Passo("busca_ao_digitar_sem_relatorio", async p =>
        {
            var raiz = j.ElementoRaiz;
            var caixa = (Microsoft.UI.Xaml.Controls.TextBox)raiz.FindName("CaixaPesquisa");
            var atalho = (TextBlock)raiz.FindName("AtalhoPesquisa");
            var inicioAtalho = atalho.TransformToVisual(caixa).TransformPoint(new Windows.Foundation.Point()).X;
            p["campo_separado_do_atalho"] = atalho.Visibility == Visibility.Collapsed || caixa.ActualWidth <= inicioAtalho;
            caixa.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            foreach (var termo in new[] { "l", "le", "lei" })
            {
                caixa.Text = termo;
                bool visivel = await EsperarAteAsync(() => Task.FromResult(j.ResultadosVisiveisPara(termo)), 5000);
                p[termo] = visivel;
            }
            caixa.Text = "";
            p["fechou"] = await EsperarAteAsync(() => Task.FromResult(!j.ListaGeralAberta), 2000);
            p["ok"] = p.All(item => item.Value?.GetValue<bool>() == true);
        });

        await Passo("1_importar_longo", async p =>
        {
            if (!await j.ImportarParaAsync(dadosLongo, pacoteLongo)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            var plano = a.Composicao.Plano;
            planoDaImportacao = plano.ParaJson();
            var capa = JsonNode.Parse(await File.ReadAllTextAsync(dadosLongo))!["capa"]!;
            var tituloEsperado = TituloJanela.De(capa["processo"]?.GetValue<string>(), capa["requerente"]?.GetValue<string>(), "longo");
            p["pacote_criado"] = File.Exists(pacoteLongo);
            p["folhas"] = a.Composicao.Folhas;
            p["escala_de_impressao"] = plano.Escala;
            p["avisos_de_largura"] = plano.AvisosLargura.Count;
            p["estouros"] = plano.Estouros.Count;
            p["folhas_que_transbordam_na_tela"] = new JsonArray(a.Aplicacao.FolhasQueTransbordam.Select(n => (JsonNode)n).ToArray());
            p["titulo_da_barra"] = j.TextoTituloAtual;
            p["titulo_confere"] = j.TextoTituloAtual == tituloEsperado && tituloEsperado.StartsWith("Proc. ", StringComparison.Ordinal);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            // Correções 2026-11-15: cabeçalho com o processo em duas linhas (número em cima, maior e em negrito; requerente embaixo), o selo
            // "Salvo HH:MM" e os rótulos sempre à vista de Desfazer, Refazer, Anterior e Próximo.
            var linhasCabecalho = TituloJanela.Linhas(capa["processo"]?.GetValue<string>(), capa["requerente"]?.GetValue<string>(), "longo");
            p["cabecalho"] = new JsonArray(j.TextoProcessoCabecalho, j.TextoRequerenteCabecalho, j.TextoSeloGravacao);
            p["cabecalho_rotulos"] = new JsonArray(j.RotulosDoCabecalho.Select(x => (JsonNode)x).ToArray());
            var cabecalhoOk = j.TextoProcessoCabecalho == linhasCabecalho.Principal && j.TextoRequerenteCabecalho == linhasCabecalho.Secundaria
                && j.ProcessoEmNegrito && j.TextoSeloGravacao == j.TextoEstadoAtual
                && j.RotulosDoCabecalho.SequenceEqual(["Desfazer", "Refazer", "Anterior", "Próxima"]) && j.PilulasDoCabecalhoConferem;
            p["cabecalho_confere"] = cabecalhoOk;
            p["composicao"] = j.TextoComposicaoAtual;
            p["itens_na_lista"] = j.Itens.Count;
            p["primeiros_itens"] = new JsonArray(j.Itens.Take(6).Select(i => (JsonNode)$"{i.Rotulo} (f. {i.Folha})").ToArray());
            // Auditoria A2: os avisos de layout vão para o indicador do rodapé; nenhuma InfoBar de layout abre sobre as folhas.
            var indicador = j.IndicadorLayout;
            var reducaoNoIndicador = indicador.Avisos.FirstOrDefault(x => x.Tipo == TipoAvisoLayout.Reducao);
            p["indicador_de_layout"] = indicador.Resumo;
            p["indicador_avisos"] = new JsonArray(indicador.Avisos.Select(x => (JsonNode)$"{x.Tipo}: {x.Texto}").ToArray());
            p["aviso_largura_aberto"] = j.InfoLargura.IsOpen;
            p["aviso_largura_texto"] = reducaoNoIndicador?.Texto;
            var reducao = AvisosLayout.ReducaoPercentual(plano.Escala);
            p["aviso_largura_confere"] = !j.InfoLargura.IsOpen && (reducaoNoIndicador is not null) == (plano.AvisosLargura.Count > 0)
                && (plano.AvisosLargura.Count == 0 || reducaoNoIndicador!.Texto == $"Conteúdo mais largo que a página: o documento foi reduzido {reducao}% para caber (como na impressão do kit).");
            p["nenhuma_infobar_de_layout"] = !j.InfoLargura.IsOpen && !j.InfoForaDaArea.IsOpen && !j.InfoEstouro.IsOpen;
            p["exportar_habilitado"] = j.ExportarHabilitado;
            p["rodape_folha"] = j.TextoFolhaAtual;
            p["latencia_recomposicao_ms"] = a.Milissegundos;
            p["convivencia_webview2"] = "tela visível + host oculto de composição abertos no mesmo processo, mesma pasta de dados";
            p["folhas_com_conteudo_alem_da_borda"] = new JsonArray(a.Aplicacao.FolhasQueTransbordamNaLargura.Select(n => (JsonNode)n).ToArray());
            p["aviso_fora_da_area_aberto"] = j.InfoForaDaArea.IsOpen;
            var faixas = (await Js(j, "document.querySelectorAll('section.folha[data-pmvni-transborda]').length")).GetInt32();
            p["folhas_com_faixa_vermelha"] = faixas;
            // O estado do editor chegou à página (o CSS da edição só entra com o primeiro estadoEditor).
            p["estado_do_editor_aplicado"] = (await Js(j, "!!document.getElementById('pmvni-edicao-css')")).GetBoolean();
            // D4: tela e host oculto InPrivate (nada do relatório no perfil); atalhos do navegador desligados nos dois.
            var host = await j.HostComposicao!.ExecutarAsync(core => Task.FromResult((core.Profile.IsInPrivateModeEnabled, core.Settings.AreBrowserAcceleratorKeysEnabled)));
            p["inprivate_tela"] = j.NucleoWeb.Profile.IsInPrivateModeEnabled;
            p["inprivate_host_oculto"] = host.Item1;
            p["atalhos_do_navegador_tela"] = j.NucleoWeb.Settings.AreBrowserAcceleratorKeysEnabled;
            p["atalhos_do_navegador_host_oculto"] = host.Item2;
            var protegido = j.NucleoWeb.Profile.IsInPrivateModeEnabled && host.Item1 && !j.NucleoWeb.Settings.AreBrowserAcceleratorKeysEnabled && !host.Item2;
            p["ok"] = File.Exists(pacoteLongo) && a.Composicao.Folhas > 3 && plano.Estouros.Count == 0 && j.ExportarHabilitado
                && p["aviso_largura_confere"]!.GetValue<bool>() && p["titulo_confere"]!.GetValue<bool>() && cabecalhoOk
                && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal) && p["estado_do_editor_aplicado"]!.GetValue<bool>()
                && protegido && faixas == a.Aplicacao.FolhasMarcadas.Count && p["nenhuma_infobar_de_layout"]!.GetValue<bool>()
                && indicador.Avisos.Count(x => x.Tipo == TipoAvisoLayout.AlemDaBorda) == a.Aplicacao.FolhasQueTransbordamNaLargura.Count
                && indicador.Visivel == (indicador.Avisos.Count > 0) && !indicador.Critico;
            await Esperar(600);
            await CapturarWebAsync(j, Path.Combine(pasta, "01-tela-100.png"));
            var desalinhamento = j.FaixaAtual.DesalinhamentoOriginal();
            p["desalinhamento_original_dip"] = desalinhamento;
            if (desalinhamento > .5) throw new InvalidOperationException("O botão Original difere dos vizinhos na barra.");
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "01-janela-xaml.png"));
        });

        await Passo("2_rolagem_continua", async p =>
        {
            var g = await Js(j, """
                (() => { const f = [...document.querySelectorAll('section.folha')];
                  const tops = f.map(x => x.getBoundingClientRect().top + scrollY);
                  const passos = tops.slice(1).map((t, i) => t - tops[i]);
                  return { zoom: Number(getComputedStyle(document.documentElement).getPropertyValue('--pmvni-zoom')) || 1, folhas: f.length, passoMin: Math.min(...passos), passoMax: Math.max(...passos), passoEsperado: (297 + 8) * 96 / 25.4,
                           rolagemTotal: document.documentElement.scrollHeight, janela: document.documentElement.clientHeight,
                           fundoDaPagina: getComputedStyle(document.documentElement).backgroundColor, fundoDaFolha: getComputedStyle(f[0]).backgroundColor,
                           sombraDaFolha: getComputedStyle(f[0]).boxShadow, devicePixelRatio, topos: tops.slice(0, 3) }; })()
                """);
            p["geometria"] = JsonNode.Parse(g.GetRawText());
            var n = g.GetProperty("folhas").GetInt32();
            // Correções 2026-11-15: com o painel de seções mais largo, a folha abre ajustada à largura (zoom < 100 %); o passo na tela é
            // (297 + 8) mm vezes o zoom (o zoom é um scale() nas folhas).
            var zoomMedido = g.GetProperty("zoom").GetDouble();
            var passo = g.GetProperty("passoEsperado").GetDouble() * zoomMedido;
            p["zoom"] = zoomMedido;
            var continuo = Math.Abs(g.GetProperty("passoMin").GetDouble() - passo) < 1 && Math.Abs(g.GetProperty("passoMax").GetDouble() - passo) < 1;
            var visitas = new JsonArray();
            var coerente = true;
            foreach (var alvo in new[] { 1, 4, 9, n / 2, n - 1, n })
            {
                await Js(j, $"window.scrollTo({{ top: document.querySelectorAll('section.folha')[{alvo - 1}].getBoundingClientRect().top + scrollY - 40, behavior: 'instant' }}); 1");
                await Esperar(450);
                visitas.Add(new JsonObject { ["rolou_para_folha"] = alvo, ["indicador"] = j.TextoFolhaAtual, ["folha_no_indicador"] = j.FolhaAtual });
                coerente &= j.FolhaAtual == alvo;
            }
            p["visitas_por_rolagem"] = visitas;
            p["folhas_contiguas_com_8mm"] = continuo;
            p["indicador_acompanha_a_rolagem"] = coerente;
            p["ok"] = continuo && coerente;
            await Js(j, "window.scrollTo({ top: 0, behavior: 'instant' }); 1");
            await Esperar(300);
        });

        await Passo("3_zoom_nao_recompoe", async p =>
        {
            var a = j.Aberto!;
            var antes = await j.Documento!.TextoPorFolhaAsync();
            var planoAntes = a.Composicao.Plano.ParaJson();
            await Js(j, "window.__marcaFolhas = [...document.querySelectorAll('section.folha')]; 1");
            var amostras = new JsonArray();
            var igual = true;
            async Task Confere(string como)
            {
                await Esperar(400);
                var depois = await j.Documento!.TextoPorFolhaAsync();
                var mesmas = (await Js(j, "(() => { const f = [...document.querySelectorAll('section.folha')]; return f.length === window.__marcaFolhas.length && f.every((x, i) => x === window.__marcaFolhas[i]); })()")).GetBoolean();
                var larguraFolha = (await Js(j, "document.querySelector('section.folha').getBoundingClientRect().width")).GetDouble();
                igual &= depois.SequenceEqual(antes) && mesmas;
                amostras.Add(new JsonObject { ["como"] = como, ["zoom"] = j.Zoom, ["texto_igual"] = depois.SequenceEqual(antes), ["mesmas_folhas_no_dom"] = mesmas, ["largura_da_folha_px"] = Math.Round(larguraFolha, 1) });
            }
            foreach (var z in new[] { 0.5, 2.0, 3.0, 1.0 }) { await j.ZoomAsync(new { zoom = z }, "manual"); await Confere("definirZoom " + z); }
            await j.ZoomAsync(new { zoom = 1.0 }, "manual");
            var controleZoom = ((PaginaRelatorios)j.ElementoRaiz.FindName("Relatorios")).ControleZoom;
            foreach (var percentual in new[] { 50, 300, 135 })
            {
                controleZoom.Value = percentual;
                await Confere($"controle deslizante {percentual}%");
                igual &= Math.Abs(j.Zoom * 100 - percentual) < 0.01;
            }
            // Rodapé (botões) e faixa (aba Exibir): os mesmos comandos.
            foreach (var botao in new[] { j.BotoesZoom[1], j.BotoesZoom[1], j.BotoesZoom[0], j.BotoesZoom[2], j.BotoesZoom[3] })
            {
                var nomeBotao = botao.Content is string s ? s : Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(botao);
                ((IInvokeProvider)new ButtonAutomationPeer(botao).GetPattern(PatternInterface.Invoke)).Invoke();
                await Confere("botão " + nomeBotao);
            }
            foreach (var id in new[] { ComandosApp.ExibirMais, ComandosApp.ExibirMenos, ComandosApp.ExibirPagina, ComandosApp.ExibirLargura })
            {
                await j.ExecutarComandoAsync(id);
                await Confere("comando " + id);
            }
            await j.ZoomAsync(new { zoom = 1.0 }, "manual");
            var recomposto = (await j.HostComposicao!.Documento.ComporAsync()).ParaJson();
            p["amostras"] = amostras;
            p["plano_igual_depois_do_zoom"] = recomposto == planoAntes && a.Composicao.Plano.ParaJson() == planoAntes;
            p["texto_por_folha_igual"] = igual;
            p["ok"] = igual && recomposto == planoAntes;
            await j.ZoomAsync(new { zoom = 0.5 }, "manual");
            await Esperar(700);
            await CapturarWebAsync(j, Path.Combine(pasta, "03-tela-50.png"));
            await j.ZoomAsync(new { zoom = 1.0 }, "manual");
            await Esperar(300);
        });

        await Passo("4_navegacao", async p =>
        {
            var lista = j.Itens;
            var escolhidos = new List<ItemNavegacao> { lista[0] };
            escolhidos.AddRange(lista.Where(i => i.Secao).Skip(1).Take(3));
            escolhidos.AddRange(lista.Where(i => !i.Secao).Where((_, k) => k % 11 == 5).Take(4));
            escolhidos.Add(lista[^1]);
            var amostras = new JsonArray();
            var todos = true;
            foreach (var item in escolhidos)
            {
                await j.RolarParaAsync(item);
                await Esperar(450);
                var v = await Js(j, $"(() => {{ const f = document.querySelectorAll('section.folha')[{item.Folha - 1}].getBoundingClientRect(); return {{ topo: f.top, base: f.bottom, janela: document.documentElement.clientHeight }}; }})()");
                var visivel = v.GetProperty("topo").GetDouble() < v.GetProperty("janela").GetDouble() && v.GetProperty("base").GetDouble() > 0;
                var certa = j.FolhaAtual == item.Folha || j.FolhaAtual == item.Folha + 1;
                todos &= visivel && certa;
                amostras.Add(new JsonObject { ["item"] = item.Rotulo, ["folha_do_item"] = item.Folha, ["folha_na_tela"] = j.FolhaAtual, ["folha_visivel"] = visivel,
                    ["indicador_da_lista_marcado"] = j.Itens.Any(i => i.Atual), ["rodape"] = j.TextoFolhaAtual });
            }
            p["amostras"] = amostras;
            p["ok"] = todos;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "04-janela-xaml-navegacao.png"));
            await CapturarWebAsync(j, Path.Combine(pasta, "04-tela-depois-de-navegar.png"));
        });

        await Passo("5_exportar_pdf", async p =>
        {
            var pdf = Path.Combine(pasta, "longo.pdf");
            File.Delete(pdf);
            var (ok, x, erro, _) = await j.ExportarParaAsync(pdf);
            if (!ok || x is null) throw new InvalidOperationException("exportação falhou: " + erro);
            var bytes = await File.ReadAllBytesAsync(pdf);
            var info = InfoPdf.Ler(bytes);
            using var recibo = JsonDocument.Parse(await File.ReadAllBytesAsync(x.CaminhoRecibo));
            var a = j.Aberto!;
            var shaPacote = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(pacoteLongo)));
            p["paginas_do_pdf"] = info.Paginas;
            p["folhas_na_tela"] = a.Composicao.Folhas;
            p["pagina_mm"] = $"{info.LarguraMm:0.##} x {info.AlturaMm:0.##}";
            p["recibo_sha256_html_confere"] = recibo.RootElement.GetProperty("sha256_html").GetString() == PlanoQuebras.Sha256De(a.Preparado.Render.Html);
            p["recibo_sha256_pdf_confere"] = recibo.RootElement.GetProperty("sha256_pdf").GetString() == Convert.ToHexStringLower(SHA256.HashData(bytes));
            p["recibo_sha256_pacote_confere"] = recibo.RootElement.TryGetProperty("sha256_pacote", out var sp) && sp.GetString() == shaPacote;
            p["plano_do_pdf_igual_ao_da_tela"] = x.Plano.ParaJson() == a.Composicao.Plano.ParaJson();
            p["aviso_de_sucesso"] = j.InfoMensagem.IsOpen && j.InfoMensagem.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
            p["ok"] = info.Paginas == a.Composicao.Folhas && p["recibo_sha256_html_confere"]!.GetValue<bool>() && p["recibo_sha256_pdf_confere"]!.GetValue<bool>()
                && p["recibo_sha256_pacote_confere"]!.GetValue<bool>() && p["plano_do_pdf_igual_ao_da_tela"]!.GetValue<bool>() && p["aviso_de_sucesso"]!.GetValue<bool>();
        });

        await Passo("6_estouro_recusado", async p =>
        {
            var pacote = Path.Combine(pasta, "estouro.rpmvni");
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "estouro.json"), pacote)) throw new InvalidOperationException("não importou o estouro: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            p["estouros"] = new JsonArray(a.Composicao.Plano.Estouros.Select(e => (JsonNode)$"folha {e.Folha}, ref {e.Ref}").ToArray());
            p["composicao"] = j.TextoComposicaoAtual;
            // Auditoria A2: o estouro aparece como bloqueio no indicador do rodapé (cor crítica, uma linha por bloco com "Ir à folha N").
            var bloqueios = j.IndicadorLayout.Avisos.Where(x => x.Bloqueia).ToList();
            p["indicador_de_layout"] = j.IndicadorLayout.Resumo;
            p["aviso_de_erro_aberto"] = j.IndicadorLayout.Critico && j.IndicadorLayout.Visivel && bloqueios.Count == a.Composicao.Plano.Estouros.Count
                && bloqueios.Select(x => x.Folha).SequenceEqual(a.Composicao.Plano.Estouros.Select(e => (int?)e.Folha)) && !j.InfoEstouro.IsOpen;
            p["aviso_de_erro_texto"] = string.Join(" ", bloqueios.Select(x => x.Texto));
            p["exportar_habilitado"] = j.ExportarHabilitado;
            p["dica_do_botao"] = j.DicaExportar;
            var pdf = Path.Combine(pasta, "estouro.pdf");
            File.Delete(pdf);
            var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
            p["exportacao_recusada"] = !ok && excecao is ExportacaoRecusadaException;
            p["mensagem_da_recusa"] = erro;
            p["nenhum_pdf_gravado"] = !File.Exists(pdf);
            var faixas = (await Js(j, "document.querySelectorAll('section.folha[data-pmvni-transborda]').length")).GetInt32();
            p["folhas_com_faixa_vermelha"] = faixas;
            p["folhas_que_transbordam_na_tela"] = new JsonArray(a.Aplicacao.FolhasQueTransbordam.Select(n => (JsonNode)n).ToArray());
            p["ok"] = a.Composicao.Plano.Estouros.Count > 0 && !j.ExportarHabilitado && j.DicaExportar.StartsWith("A exportação está bloqueada", StringComparison.Ordinal) && p["aviso_de_erro_aberto"]!.GetValue<bool>()
                && p["exportacao_recusada"]!.GetValue<bool>() && !File.Exists(pdf) && faixas == a.Aplicacao.FolhasMarcadas.Count;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "06-janela-xaml-estouro.png"));
            await j.RolarParaAsync(new ItemNavegacao("folha do estouro", a.Composicao.Plano.Estouros[0].Folha, null, false));
            await Esperar(500);
            var vista = await Js(j, "(() => { const f = [...document.querySelectorAll('section.folha')]; const i = f.findIndex(x => { const r = x.getBoundingClientRect(); return r.top <= innerHeight / 2 && r.bottom >= innerHeight / 2; }); const e = f[i]; return { folhaNaTela: i + 1, comFaixa: e.hasAttribute('data-pmvni-transborda'), sombra: getComputedStyle(e).boxShadow, esquerda: e.getBoundingClientRect().left, zoom: getComputedStyle(document.documentElement).getPropertyValue('--pmvni-zoom') }; })()");
            p["folha_vista_apos_rolar_ate_o_estouro"] = JsonNode.Parse(vista.GetRawText());
            p["faixa_vermelha_na_folha_vista"] = vista.GetProperty("comFaixa").GetBoolean() && vista.GetProperty("folhaNaTela").GetInt32() == a.Composicao.Plano.Estouros[0].Folha;
            if (!p["faixa_vermelha_na_folha_vista"]!.GetValue<bool>()) p["ok"] = false;
            await CapturarWebAsync(j, Path.Combine(pasta, "06-tela-estouro.png"));
        });

        await Passo("7_tema_escuro", async p =>
        {
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            var antes = await j.Documento!.TextoPorFolhaAsync();
            const string Cores = "({ escuro: matchMedia('(prefers-color-scheme: dark)').matches, fundoDaPagina: getComputedStyle(document.documentElement).backgroundColor, " +
                "corpo: getComputedStyle(document.body).backgroundColor, fundoDaFolha: getComputedStyle(document.querySelector('section.folha')).backgroundColor })";
            j.DefinirTema(ElementTheme.Light);
            await Esperar(700);
            var claro = await Js(j, Cores);
            await CapturarWebAsync(j, Path.Combine(pasta, "07-tema-claro.png"));
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "07-janela-xaml-claro.png"));
            j.DefinirTema(ElementTheme.Dark);
            await Esperar(900);
            var escuro = await Js(j, Cores);
            var depois = await j.Documento!.TextoPorFolhaAsync();
            await CapturarWebAsync(j, Path.Combine(pasta, "07-tema-escuro.png"));
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "07-janela-xaml-escuro.png"));
            p["claro"] = JsonNode.Parse(claro.GetRawText());
            p["escuro"] = JsonNode.Parse(escuro.GetRawText());
            var fundoMuda = claro.GetProperty("fundoDaPagina").GetString() != escuro.GetProperty("fundoDaPagina").GetString();
            var folhaBranca = claro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)" && escuro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)";
            p["so_o_fundo_fora_das_folhas_muda"] = fundoMuda && folhaBranca;
            p["texto_por_folha_igual"] = depois.SequenceEqual(antes);
            p["ok"] = escuro.GetProperty("escuro").GetBoolean() && !claro.GetProperty("escuro").GetBoolean() && fundoMuda && folhaBranca && depois.SequenceEqual(antes) && a.Composicao.Folhas > 0;
            j.DefinirTema(ElementTheme.Default);
        });

        // Cópia do curto (sem imagens) na pasta do roteiro: tudo o que este passo grava e apaga fica nela.
        await Passo("8_campos_em_aberto_bloqueiam_exportar", async p =>
        {
            var copia = Path.Combine(pasta, "curto-colchetes.json");
            var no = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(corpus, "curto.json")))!;
            no["capa"]!["requerente"] = "[Nome do requerente]";
            await File.WriteAllTextAsync(copia, no.ToJsonString());
            var pdf = Path.Combine(pasta, "curto-colchetes.pdf");
            File.Delete(pdf);
            try
            {
                if (!await j.ImportarParaAsync(copia, Path.Combine(pasta, "curto-colchetes.rpmvni"))) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
                var a = j.Aberto!;
                p["erros_finais"] = a.Preparado.ErrosFinais.Count;
                p["exportar_habilitado"] = j.ExportarHabilitado;
                p["dica_do_botao"] = j.DicaExportar;
                p["composicao"] = j.TextoComposicaoAtual;
                var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
                p["exportacao_bloqueada"] = !ok && excecao is ConferenciaFinalException;
                p["mensagem"] = erro;
                p["aviso_de_bloqueio_aberto"] = j.InfoBloqueio.IsOpen;
                p["aviso_de_bloqueio_texto"] = j.InfoBloqueio.Message;
                p["ok"] = j.InfoBloqueio.IsOpen && a.Preparado.ErrosFinais.Count > 0 && !j.ExportarHabilitado && j.DicaExportar.StartsWith("A exportação está bloqueada", StringComparison.Ordinal)
                    && p["exportacao_bloqueada"]!.GetValue<bool>() && !File.Exists(pdf);
            }
            finally { File.Delete(copia); }
        });

        await Passo("9_pacote_mudou_recusa_exportar", async p =>
        {
            var pacote = Path.Combine(pasta, "curto-muda.rpmvni");
            var pdf = Path.Combine(pasta, "curto-muda.pdf");
            File.Delete(pdf);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), pacote)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            // Outro programa grava o mesmo pacote, com o requerente trocado, depois de aberto.
            var lido = Pacote.Ler(pacote);
            lido.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO REQUERENTE DEPOIS DE ABRIR";
            Pacote.Gravar(pacote, lido.Conteudo, lido.Disco);
            var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
            p["recusada"] = !ok && excecao is ExportacaoDivergenteException;
            p["mensagem"] = erro;
            p["nenhum_pdf_gravado"] = !File.Exists(pdf) && !File.Exists(Exportador.CaminhoReciboDe(pdf));
            p["ok"] = p["recusada"]!.GetValue<bool>() && erro == ExportacaoDivergenteException.ArquivoMudou && p["nenhum_pdf_gravado"]!.GetValue<bool>();
        });

        await Passo("10_faixa_por_modulo", async p =>
        {
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            await j.MudarModuloAsync(Modulo.Relatorios);
            var sem = j.ComandosSemManipulador();
            var def = Faixas.Para(Modulo.Relatorios);
            var faixa = j.FaixaAtual;
            p["modulo_atual"] = j.ModuloAtual.ToString();
            p["comandos_sem_manipulador"] = new JsonArray(sem.Select(s => (JsonNode)s).ToArray());
            p["modulos_no_trilho"] = new JsonArray(j.TrilhoAtual.Modulos.Select(m => (JsonNode)m.ToString()).ToArray());
            p["abas"] = new JsonArray(faixa.Definicao!.Abas.Select(a => (JsonNode)a.Nome).ToArray());
            p["aba_aberta"] = faixa.AbaAtual;
            var abasConferem = faixa.Definicao.Modulo == Modulo.Relatorios && faixa.Definicao.Abas.Select(a => a.Nome).SequenceEqual(def.Abas.Select(a => a.Nome));
            var trilhoConfere = j.TrilhoAtual.Modulos.SequenceEqual(Faixas.Disponiveis);
            var ajustesJson = Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo);
            string? AbaLembrada() => JsonNode.Parse(File.ReadAllText(ajustesJson))?["aba_por_modulo"]?["Relatorios"]?.GetValue<string>();
            faixa.EscolherAba("Ajuda");
            await Esperar(200);
            var botoesAjuda = faixa.IdsVisiveis;
            var primarioNaAjuda = faixa.IdPrimario;
            var lembradaAjuda = AbaLembrada();
            faixa.EscolherAba("Arquivo");
            await Esperar(200);
            var botoesArquivo = faixa.IdsVisiveis;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "10-janela-xaml-arquivo.png"));
            faixa.EscolherAba("Exibir");
            await Esperar(200);
            var lembradaExibir = AbaLembrada();
            var painelAntes = j.PainelVisivel;
            await j.ExecutarComandoAsync(ComandosApp.ExibirPainel);
            var painelDepois = j.PainelVisivel;
            await j.ExecutarComandoAsync(ComandosApp.ExibirPainel);
            p["botoes_aba_ajuda"] = new JsonArray(botoesAjuda.Select(s => (JsonNode)s).ToArray());
            p["botoes_aba_arquivo"] = new JsonArray(botoesArquivo.Select(s => (JsonNode)s).ToArray());
            p["aba_lembrada_em_ajustes"] = new JsonArray(lembradaAjuda, lembradaExibir);
            p["painel_alterna"] = painelAntes && !painelDepois && j.PainelVisivel;
            // Auditoria §1.5: o primário (Novo relatório) fica à esquerda em todas as abas; a Página Inicial não o repete.
            faixa.EscolherAba("Página Inicial");
            await Esperar(200);
            var botoesInicio = faixa.IdsVisiveis;
            p["botao_primario"] = faixa.IdPrimario;
            p["primario_em_todas_as_abas"] = primarioNaAjuda == ComandosApp.RelatoriosNovo && faixa.IdPrimario == ComandosApp.RelatoriosNovo;
            p["pagina_inicial_sem_novo_relatorio"] = !botoesInicio.Contains(ComandosApp.RelatoriosNovo);
            p["ok"] = p["primario_em_todas_as_abas"]!.GetValue<bool>() && p["pagina_inicial_sem_novo_relatorio"]!.GetValue<bool>() && sem.Count == 0 && abasConferem && trilhoConfere && lembradaAjuda == "Ajuda" && lembradaExibir == "Exibir"
                && botoesAjuda.SequenceEqual(def.Abas[^1].Comandos.Select(c => c.Id)) && botoesArquivo.SequenceEqual(def.Abas[0].Comandos.Select(c => c.Id))
                && p["painel_alterna"]!.GetValue<bool>();
        });

        await Passo("faixas_e_rotulos_do_monitor", async p =>
        {
            var faixa = j.FaixaAtual;
            string[] comandos = [ComandosApp.ExibirLargura, ComandosApp.ExibirPagina, ComandosApp.ExibirPainel,
                ComandosApp.ExibirPropriedades, ComandosApp.RevisaoMarcas, ComandosApp.RevisaoSomenteAlterados];
            var habilitadosNasFolhas = comandos.All(faixa.Habilitado);
            await j.NovoRelatorioAsync();
            var monitor = (PaginaNovoRelatorio)j.ElementoRaiz.FindName("PaginaDeNovoRelatorio");
            try
            {
                faixa.EscolherAba("Revisão");
                var indisponiveisNoMonitor = comandos.All(id => !faixa.Habilitado(id) && faixa.Dica(id).Contains("Volte às folhas"));
                monitor.MostrarExecucao(true);
                monitor.MostrarResumo(true, null, [("Preparo", EstadoEtapa.Agora), ("Análise", EstadoEtapa.Pendente)]);
                var agora = DateTimeOffset.Now;
                monitor.MostrarEstimativa(new EstimativaAnalise("prepare", 47.5, 12.8, agora.AddMinutes(18)));
                monitor.MostrarRelogios(TimeSpan.FromMinutes(7), agora);
                await Esperar(250);
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "monitor-rotulos-revisao.png"));
                faixa.EscolherAba("Exibir");
                await Esperar(250);
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "monitor-rotulos-exibir.png"));
                p["ok"] = habilitadosNasFolhas && indisponiveisNoMonitor;
            }
            finally
            {
                monitor.MostrarResumo(false, null, []);
                monitor.MostrarEstimativa(null);
                monitor.MostrarExecucao(false);
                Invocar(monitor.BotaoVoltarRelatorio);
                await EsperarAteAsync(() => Task.FromResult(j.FolhasVisiveis), 3000);
                faixa.EscolherAba("Página Inicial");
            }
        });

        await Passo("11_reabrir_pacote", async p =>
        {
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            p["fechado_sem_relatorio"] = j.Sessao is null && j.TextoTituloAtual == "Análise de Projetos – PMVNI" && !j.ExportarHabilitado;
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            p["plano_igual_ao_da_importacao"] = planoDaImportacao is not null && a.Composicao.Plano.ParaJson() == planoDaImportacao;
            p["historico"] = j.Sessao!.Revisao.HistoricoCompleto.Count;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["titulo_da_barra"] = j.TextoTituloAtual;
            p["ok"] = p["plano_igual_ao_da_importacao"]!.GetValue<bool>() && p["fechado_sem_relatorio"]!.GetValue<bool>()
                && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal) && j.TextoTituloAtual.StartsWith("Proc. ", StringComparison.Ordinal);
        });

        await Passo("12_salvar_duas_vezes_sem_conflito", async p =>
        {
            // Ctrl+S duas vezes (e mais um Salvar direto) ao mesmo tempo: as gravações vão em fila, nunca em conflito consigo mesmas.
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            await Task.WhenAll(j.ExecutarComandoAsync(ComandosApp.ArquivoSalvar), j.ExecutarComandoAsync(ComandosApp.ArquivoSalvar), s.GravarAsync());
            var lido = Pacote.Ler(pacoteLongo);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["disco_confere"] = lido.Disco.Sha256 == s.Disco.Sha256;
            p["aviso_de_erro"] = j.InfoMensagem.IsOpen && j.InfoMensagem.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
            var pdf = Path.Combine(pasta, "longo-depois-de-salvar.pdf");
            File.Delete(pdf);
            var (ok, _, erro, _) = await j.ExportarParaAsync(pdf);
            p["exporta_depois"] = ok;
            p["erro_ao_exportar"] = erro;
            p["ok"] = s.Estado == EstadoGravacao.Salvo && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal)
                && p["disco_confere"]!.GetValue<bool>() && !p["aviso_de_erro"]!.GetValue<bool>() && ok;
        });

        // ---------- Tarefa 9: revisão dos itens no app (o longo continua aberto, salvo) ----------
        string? id3 = null, id5 = null;
        const string NovaVerificacao = "Verificação refeita pelo roteiro: cota & recuo <conferidos> \"na planta\".";
        const string Justificativa = "Conferido na planta de implantação pelo roteiro.";

        await Passo("13_selecionar_e_editar_no_a4", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var item = proj.Incluidos[2];
            id3 = item.Id;
            // Clique (CDP) no título do ITEM 03: a página manda "selecionado" e as Propriedades mostram o item.
            var ponto = await PontoDoCampoAsync(j, item.Id, "titulo");
            await CliqueAsync(j, ponto);
            await EsperarAteAsync(() => Task.FromResult(j.Selecionado == item.Id), 3000);
            p["selecionado_pelo_clique"] = j.Selecionado == item.Id;
            p["propriedades_titulo"] = j.TextoPropriedadesTitulo;
            var camposNaTela = (await Js(j, "document.querySelectorAll('section.folha [data-campo]').length")).GetInt32();
            p["campos_marcados_na_tela"] = camposNaTela;
            // Duplo clique abre a edição no A4 (pedirEdicao → editarCampo); Esc cancela (edicaoCancelada → estadoEditor).
            await GarantirTecladoAsync(j);
            await CliqueAsync(j, ponto, cliques: 2);
            var abriu = await EsperarAteAsync(async () => (await Js(j, "!!document.querySelector('[data-pmvni-editando]')")).GetBoolean(), 3000);
            p["duplo_clique_abre_a_edicao"] = abriu;
            await TeclaAsync(j, "Escape", "Escape", 27);
            p["esc_cancela_a_edicao"] = await EsperarAteAsync(async () => !(await Js(j, "!!document.querySelector('[data-pmvni-editando]')")).GetBoolean(), 3000);
            // A edição pelo host (a mesma rota do Enter no A4).
            await j.EditarTextoAsync(item.Id, "verificacao", NovaVerificacao);
            var efetivo = DadosEfetivos.TextoEfetivo(proj, item, "verificacao");
            var folhas = await j.Documento!.TextoPorFolhaAsync();
            var html = j.Aberto!.Preparado.Render.Html;
            p["erros_da_edicao"] = new JsonArray(j.UltimosErros.Select(e => (JsonNode)e).ToArray());
            p["modelo_tem_o_texto_novo"] = efetivo == NovaVerificacao;
            p["folhas_tem_o_texto_novo"] = folhas.Any(f => f.Contains(NovaVerificacao, StringComparison.Ordinal));
            p["data_campo_fora_do_html_preparado"] = !html.Contains("data-campo", StringComparison.Ordinal);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["lista_marca_alterado"] = j.Itens.FirstOrDefault(i => i.Ref == item.Id)?.Alterado == true;
            // F2: edita o último campo clicado (o título).
            await j.ExecutarComandoAsync(ComandosApp.InicioEditarTexto);
            var campoF2 = await EsperarValorAsync(j, "(() => { const e = document.querySelector('[data-pmvni-editando]'); return e ? e.getAttribute('data-campo') : null; })()", 3000);
            p["f2_edita_o_ultimo_campo_clicado"] = campoF2;
            await TeclaAsync(j, "Escape", "Escape", 27);
            await EsperarAteAsync(async () => !(await Js(j, "!!document.querySelector('[data-pmvni-editando]')")).GetBoolean(), 3000);
            p["ok"] = j.Selecionado == item.Id && j.TextoPropriedadesTitulo == "ITEM 03" && camposNaTela > 0 && abriu
                && efetivo == NovaVerificacao && p["folhas_tem_o_texto_novo"]!.GetValue<bool>() && p["data_campo_fora_do_html_preparado"]!.GetValue<bool>()
                && j.TextoEstadoAtual == "Não salvo" && p["lista_marca_alterado"]!.GetValue<bool>() && campoF2 == "titulo";
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "13-janela-xaml-propriedades.png"));
            await CapturarWebAsync(j, Path.Combine(pasta, "13-tela-editado.png"));
        });

        await Passo("14_situacao_e_justificativa", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var item = s.Revisao.Projeto.Item(id3 ?? throw new InvalidOperationException("passo 13 não rodou"))!;
            var original = item.Situacao.Original!.GetValue<string>();
            await j.AplicarAsync(new DefinirSituacao(item.Id, "ATENDE"));
            var esperado = $"ITEM 03: a situação mudou de {Pmvni.Composicao.Kit.Situacoes.Sit[original].Texto} para ATENDE sem justificativa";
            var erros = j.Aberto!.Preparado.ErrosFinais;
            p["erros_finais"] = new JsonArray(erros.Select(e => (JsonNode)e).ToArray());
            p["mensagem_esperada"] = esperado;
            p["mensagem_exata"] = erros.Contains(esperado);
            p["erros_sem_repeticao"] = erros.Distinct().Count() == erros.Count;
            var habilitadoSem = j.ExportarHabilitado;
            p["exportar_habilitado_sem_justificativa"] = habilitadoSem;
            var pdf = Path.Combine(pasta, "longo-sem-justificativa.pdf");
            File.Delete(pdf);
            var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
            p["exportacao_recusada"] = !ok && excecao is ConferenciaFinalException && !File.Exists(pdf);
            p["mensagem_da_recusa"] = erro;
            var conferencia = j.TextoConferencia();
            await j.ExecutarComandoAsync(ComandosApp.RevisaoConferencia);
            var mostrada = perguntas.Informados.LastOrDefault();
            p["conferencia_mostra_o_erro"] = conferencia.Contains(esperado, StringComparison.Ordinal) && mostrada.Titulo == "Conferência" && mostrada.Texto == conferencia;
            p["propriedades_aviso_de_justificativa"] = j.TextoPropriedadesTitulo;
            await j.AplicarAsync(new DefinirJustificativa(item.Id, Justificativa));
            p["erros_finais_com_justificativa"] = j.Aberto!.Preparado.ErrosFinais.Count;
            p["exportar_habilitado_com_justificativa"] = j.ExportarHabilitado;
            p["ok"] = p["mensagem_exata"]!.GetValue<bool>() && p["erros_sem_repeticao"]!.GetValue<bool>() && !habilitadoSem
                && p["exportacao_recusada"]!.GetValue<bool>() && p["conferencia_mostra_o_erro"]!.GetValue<bool>()
                && j.Aberto!.Preparado.ErrosFinais.Count == 0 && j.ExportarHabilitado && item.Situacao.Justificativa == Justificativa;
        });

        await Passo("15_excluir_e_marca", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var antes = proj.Incluidos.Count;
            var quinto = proj.Incluidos[4];
            var sexto = proj.Incluidos[5];
            id5 = quinto.Id;
            await j.SelecionarAsync(quinto.Id);
            p["rotulo_antes"] = j.RotuloExcluirRestaurar;
            await j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            p["itens_incluidos"] = proj.Incluidos.Count;
            var rotuloDoSexto = await Js(j, $"(() => {{ const e = document.querySelector('section.folha .card[data-ref=\"{sexto.Id}\"] .ct span.id'); return e ? e.textContent : null; }})()");
            p["rotulo_do_antigo_06"] = rotuloDoSexto.ValueKind == JsonValueKind.String ? rotuloDoSexto.GetString() : null;
            p["aba_excluidos"] = j.TextoAbaExcluidos;
            p["linhas_excluidos"] = j.LinhasExcluidos;
            p["rotulo_depois"] = j.RotuloExcluirRestaurar;
            var marcas = await Js(j, "(() => { const b = [...document.querySelectorAll('#pmvni-camada .pmvni-marca-excl')]; return { n: b.length, textos: b.map(x => x.textContent), dentroDeFolha: b.some(x => !!x.closest('section.folha')) }; })()");
            p["marcas_de_excluido"] = JsonNode.Parse(marcas.GetRawText());
            var textos = marcas.GetProperty("textos").EnumerateArray().Select(x => x.GetString()).ToList();
            p["ok"] = proj.Incluidos.Count == antes - 1 && p["rotulo_do_antigo_06"]?.GetValue<string>() == "ITEM 05"
                && j.TextoAbaExcluidos == "Excluídos (1)" && j.LinhasExcluidos == 1 && textos.Contains("ITEM EXCLUÍDO 05")
                && !marcas.GetProperty("dentroDeFolha").GetBoolean() && j.RotuloExcluirRestaurar == "Restaurar item";
            await CapturarWebAsync(j, Path.Combine(pasta, "15-tela-excluido.png"));
        });

        await Passo("16_desfazer_refazer", async p =>
        {
            // Quatro comandos desde a importação (edição, situação, justificativa, exclusão): desfazer 4× volta ao plano da importação.
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var depoisDosComandos = j.Aberto!.Composicao.Plano.ParaJson();
            for (var k = 0; k < 4; k++) await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var voltou = j.Aberto!.Composicao.Plano.ParaJson() == planoDaImportacao;
            p["plano_igual_ao_da_importacao"] = voltou;
            p["itens_incluidos_desfeito"] = s.Revisao.Projeto.Incluidos.Count;
            var podeDesfazerMais = s.Revisao.PodeDesfazer;
            p["pode_desfazer_mais"] = podeDesfazerMais;
            var item3 = s.Revisao.Projeto.Item(id3!)!;
            var semRevisao = item3.Situacao.Revisada is null && item3.Campos.Values.All(c => c.Revisado is null) && item3.Situacao.Justificativa is null;
            p["item_03_sem_revisao_depois_de_desfazer"] = semRevisao;
            p["item_03_depois_de_desfazer"] = $"revisada={item3.Situacao.Revisada ?? "null"}; justificativa={item3.Situacao.Justificativa ?? "null"}; campos revisados={item3.Campos.Count(c => c.Value.Revisado is not null)}";
            for (var k = 0; k < 4; k++) await j.ExecutarComandoAsync(ComandosApp.EditarRefazer);
            var refeito = j.Aberto!.Composicao.Plano.ParaJson() == depoisDosComandos;
            p["plano_refeito_igual"] = refeito;
            var registros = s.Revisao.HistoricoCompleto;
            p["registros_desfeito"] = registros.Count(r => r.Tipo == "desfeito");
            p["registros_refeito"] = registros.Count(r => r.Tipo == "refeito");
            p["historico_na_tela"] = new JsonArray(j.LinhasHistorico.Take(3).Select(l => (JsonNode)l).ToArray());
            p["linhas_do_historico"] = $"{j.LinhasHistorico.Count} na tela, {registros.Count} no histórico";
            p["item_05_excluido_de_novo"] = s.Revisao.Projeto.Item(id5!)!.Incluido == false;
            p["ok"] = voltou && !podeDesfazerMais && semRevisao && refeito && s.Revisao.Projeto.Item(id5!)!.Incluido == false
                && registros.Count(r => r.Tipo == "desfeito") == 4 && registros.Count(r => r.Tipo == "refeito") == 4
                && j.LinhasHistorico.Count == registros.Count && j.LinhasHistorico[0].Contains("Refeito: ", StringComparison.Ordinal);
        });

        await Passo("17_salvar_reabrir_exportar", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            // Ctrl+S com o foco nas folhas: a página repassa o atalho (mensagem "atalho") e a janela salva.
            await GarantirTecladoAsync(j);
            await TeclaAsync(j, "s", "KeyS", 83, 2);
            var salvou = await EsperarAteAsync(() => Task.FromResult(s.Estado == EstadoGravacao.Salvo), 10000);
            p["ctrl_s_salvou"] = salvou;
            var registrosAntes = s.Revisao.HistoricoCompleto.Count;
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var proj = j.Sessao!.Revisao.Projeto;
            var item3 = proj.Item(id3!)!;
            var reaberto = proj.Item(id5!)!.Incluido == false && item3.Situacao.Revisada == "ATENDE" && item3.Situacao.Justificativa == Justificativa
                && DadosEfetivos.TextoEfetivo(proj, item3, "verificacao") == NovaVerificacao;
            p["alteracoes_reabertas"] = reaberto;
            var historico = j.Sessao.Revisao.HistoricoCompleto;
            var porN = historico.ToDictionary(r => r.N);
            var desfeitosRefeitos = historico.Where(r => r.Tipo is "desfeito" or "refeito").ToList();
            var refsValidas = desfeitosRefeitos.All(r => r.Ref is { } n && porN.TryGetValue(n, out var o) && o.Tipo is not ("desfeito" or "refeito")
                && r.Depois is JsonObject d && d["tipo"]?.GetValue<string>() == o.Tipo);
            p["registros_no_pacote"] = historico.Count;
            p["desfeitos_e_refeitos_com_ref"] = refsValidas && desfeitosRefeitos.Count == 8;
            var historicoReaberto = historico.Count == registrosAntes;
            var pdf = Path.Combine(pasta, "longo-revisado.pdf");
            File.Delete(pdf);
            // A conferência do kit exige corrigir as indicações que ainda citam o antigo ITEM 05.
            var (exportouComCitacao, _, recusa, _) = await j.ExportarParaAsync(pdf);
            var recusouCitacoes = !exportouComCitacao && !File.Exists(pdf) && recusa is not null
                && Enumerable.Range(3, 3).All(n => recusa.Contains($"Figura {n} (indicação ", StringComparison.Ordinal))
                && recusa.Split("cita o item excluído que era o item 05", StringSplitOptions.None).Length == 4;
            p["recusou_citacoes_ao_item_excluido"] = recusouCitacoes;
            var citacoes = CamadaFiguras.Inicial(proj)
                .SelectMany(f => f.Marcacoes.Select(m => (Figura: f.Id, Marcacao: m.Id, Itens: CamadaFiguras.ItensCitados(m.Itens))))
                .Where(m => m.Itens.Contains(id5!)).ToArray();
            foreach (var (figura, marcacao, itens) in citacoes)
                await j.AplicarAsync(new VincularItens(figura, marcacao, itens.Where(id => id != id5).ToArray()));
            p["indicacoes_corrigidas"] = citacoes.Length;
            var (ok, x, erro, _) = await j.ExportarParaAsync(pdf);
            if (!ok || x is null) throw new InvalidOperationException("exportação falhou: " + erro);
            var info = InfoPdf.Ler(await File.ReadAllBytesAsync(pdf));
            p["paginas_do_pdf"] = info.Paginas;
            p["folhas_na_tela"] = j.Aberto!.Composicao.Folhas;
            p["ok"] = salvou && reaberto && historicoReaberto && refsValidas && desfeitosRefeitos.Count == 8 && recusouCitacoes && citacoes.Length == 3
                && info.Paginas == j.Aberto.Composicao.Folhas && x.Plano.ParaJson() == j.Aberto.Composicao.Plano.ParaJson();
        });

        await Passo("18_menu_de_contexto_e_atalhos", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var alvo = proj.Incluidos[9];
            // Botão direito (CDP) sobre o ITEM 10: menuContexto → MenuFlyout com as ações do item.
            await CliqueAsync(j, await PontoDoCampoAsync(j, alvo.Id, "titulo"), botao: "right");
            var abriu = await EsperarAteAsync(() => Task.FromResult(j.UltimoMenuContexto is { IsOpen: true }), 3000);
            var menu = j.UltimoMenuContexto;
            p["menu_aberto"] = abriu;
            p["menu_entradas"] = new JsonArray((menu?.Items ?? []).Select(i => (JsonNode)(i switch
            {
                Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem sub => sub.Text + " ▸",
                Microsoft.UI.Xaml.Controls.MenuFlyoutItem mi => mi.Text,
                _ => i.GetType().Name,
            })).ToArray());
            var acoesEsperadas = new[] { "Situação ▸", "Editar texto", "Excluir item", "Novo item abaixo", "Original", "Propriedades" };
            var acoesDoItem = ((JsonArray)p["menu_entradas"]!).Select(item => item!.GetValue<string>()).ToHashSet();
            var entradasConferem = acoesEsperadas.All(acoesDoItem.Contains);
            menu?.Hide();
            p["selecionado_pelo_menu"] = j.Selecionado == alvo.Id;
            await Esperar(300);

            // Delete nas folhas (mensagem "atalho"): exclui o selecionado.
            var antes = proj.Incluidos.Count;
            await GarantirTecladoAsync(j);
            await TeclaAsync(j, "Delete", "Delete", 46);
            var excluiu = await EsperarAteAsync(() => Task.FromResult(proj.Incluidos.Count == antes - 1 && !j.Ocupado && !j.Aplicando), 6000);
            p["delete_excluiu"] = excluiu;
            p["selecionado_depois_do_delete"] = j.Selecionado == alvo.Id;
            // Alt+↓: o próximo incluído depois do excluído.
            var proximo = proj.Incluidos.First(i => i.Ordem > alvo.Ordem).Id;
            await TeclaAsync(j, "ArrowDown", "ArrowDown", 40, 1);
            var foi = await EsperarAteAsync(() => Task.FromResult(j.Selecionado == proximo), 3000);
            p["alt_seta_seleciona_o_proximo"] = foi;
            p["selecionado_depois_de_alt_seta"] = j.Selecionado == alvo.Id ? "o excluído" : j.Selecionado == proximo ? "o próximo" : j.Selecionado ?? "nenhum";
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            p["desfeito_o_delete"] = proj.Item(alvo.Id)!.Incluido;

            // Marca "ITEM EXCLUÍDO 05": clique abre o flyout com o título e Restaurar.
            var botao = await Js(j, "(() => { const b = document.querySelector('#pmvni-camada .pmvni-marca-excl'); if (!b) return null; b.scrollIntoView({ block: 'center', behavior: 'instant' }); const r = b.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()");
            var flyout = false;
            if (botao.ValueKind == JsonValueKind.Object)
            {
                await Esperar(200);
                botao = await Js(j, "(() => { const r = document.querySelector('#pmvni-camada .pmvni-marca-excl').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()");
                await CliqueAsync(j, (botao.GetProperty("x").GetDouble(), botao.GetProperty("y").GetDouble()));
                flyout = await EsperarAteAsync(() => Task.FromResult(j.UltimoFlyoutExcluido is { IsOpen: true }), 3000);
                j.UltimoFlyoutExcluido?.Hide();
            }
            p["flyout_do_excluido"] = flyout;

            // Revisão e Exibir: Somente alterados filtra a lista; Marcas tira e põe a camada; Propriedades recolhe.
            await j.ExecutarComandoAsync(ComandosApp.RevisaoSomenteAlterados);
            var itensFiltrados = j.Itens.Where(i => !i.Secao).ToList();
            var filtra = itensFiltrados.Count > 0 && itensFiltrados.All(i => i.Alterado);
            await j.ExecutarComandoAsync(ComandosApp.RevisaoSomenteAlterados);
            await j.ExecutarComandoAsync(ComandosApp.RevisaoMarcas);
            await Esperar(300);
            var semMarcas = !(await Js(j, "!!document.getElementById('pmvni-camada')")).GetBoolean();
            var chaveDosAjustesAcompanha = !j.AjustesPagina.AlternarMarcas.IsOn;   // revisão I2: a chave dos Ajustes segue a faixa
            await j.ExecutarComandoAsync(ComandosApp.RevisaoMarcas);
            await Esperar(300);
            var comMarcas = (await Js(j, "!!document.getElementById('pmvni-camada')")).GetBoolean();
            await j.ExecutarComandoAsync(ComandosApp.ExibirPropriedades);
            var recolheu = !j.PropriedadesVisivel;
            await j.ExecutarComandoAsync(ComandosApp.ExibirPropriedades);
            p["somente_alterados_filtra"] = filtra;
            chaveDosAjustesAcompanha &= j.AjustesPagina.AlternarMarcas.IsOn;
            p["marcas_alternam"] = semMarcas && comMarcas;
            p["marcas_na_chave_dos_ajustes"] = chaveDosAjustesAcompanha;
            p["propriedades_recolhe"] = recolheu && j.PropriedadesVisivel;
            p["aceleradores_de_teclado"] = j.AceleradoresDeTeclado;
            var sem = j.AtalhosSemManipulador();
            p["atalhos_sem_manipulador"] = new JsonArray(sem.Select(x => (JsonNode)x).ToArray());
            p["comandos_sem_manipulador"] = new JsonArray(j.ComandosSemManipulador().Select(x => (JsonNode)x).ToArray());
            // Auditoria M2: Ctrl+3 com o foco nas folhas (a página repassa a tecla) leva a Processos; Ctrl+1 (atalho da janela) volta.
            await GarantirTecladoAsync(j);
            await TeclaAsync(j, "3", "Digit3", 51, 2);
            var foiAProcessos = await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == Modulo.Processos), 5000);
            await j.ExecutarComandoAsync(ComandosApp.ModuloRelatorios);
            var voltou = await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == Modulo.Relatorios && j.TrilhoAtual.Ativo == Modulo.Relatorios), 5000);
            p["ctrl_3_da_pagina_leva_a_processos"] = foiAProcessos;
            p["ctrl_1_volta_a_relatorios"] = voltou;
            p["ok"] = abriu && entradasConferem && j.Selecionado is not null && excluiu && foi && proj.Item(alvo.Id)!.Incluido && flyout
                && filtra && semMarcas && comMarcas && chaveDosAjustesAcompanha && recolheu && j.PropriedadesVisivel && sem.Count == 0 && j.ComandosSemManipulador().Count == 0
                && foiAProcessos && voltou;
        });

        await Passo("19_latencia", async p =>
        {
            // Mediana de 5 comandos no longo (situação de um item, cada vez outra); só registra (meta 1,5 s, desenho §8.4).
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var item = s.Revisao.Projeto.Incluidos[19];
            var medidas = new List<long>();
            var registros = s.Revisao.HistoricoCompleto.Count;
            for (var k = 0; k < 5; k++)
            {
                var atual = item.Situacao.Revisada ?? item.Situacao.Original!.GetValue<string>();
                var ordem = Faixas.OrdemSituacoes.ToList();
                await j.AplicarAsync(new DefinirSituacao(item.Id, ordem[(ordem.IndexOf(atual) + 1) % ordem.Count]));
                medidas.Add(j.UltimaLatenciaMs);
            }
            var aplicados = s.Revisao.HistoricoCompleto.Count - registros;
            for (var k = 0; k < aplicados; k++) await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var ordenadas = medidas.Order().ToList();
            p["medidas_ms"] = new JsonArray(medidas.Select(m => (JsonNode)m).ToArray());
            p["latencia_ms"] = ordenadas[ordenadas.Count / 2];
            p["meta_ms"] = 1500;
            p["dentro_da_meta"] = ordenadas[ordenadas.Count / 2] <= 1500;
            p["ok"] = aplicados == 5;
        });

        await Passo("20_comandos_em_sequencia", async p =>
        {
            // Justificativa (como ao sair da caixa) e, sem esperar, Situação e Excluir pela faixa; depois uma situação e, sem esperar, um
            // "editar" da página. Nada é descartado: tudo entra na fila e é aplicado na ordem, cada um testado contra o projeto de então.
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var item = proj.Incluidos[10];
            var outro = proj.Incluidos[11];
            await j.SelecionarAsync(item.Id);
            var registros = s.Revisao.HistoricoCompleto.Count;
            var atual = item.Situacao.Revisada ?? item.Situacao.Original!.GetValue<string>();
            var nova = Faixas.OrdemSituacoes.First(c => c != atual);
            var t1 = j.AplicarAsync(new DefinirJustificativa(item.Id, "Justificativa digitada antes de mudar a situação."));
            var t2 = j.ExecutarComandoAsync(ComandosApp.ComandoDaSituacao(nova));
            var t3 = j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            p["na_fila_ao_mesmo_tempo"] = j.ComandosNaFila;
            await Task.WhenAll(t1, t2, t3);
            var tipos = s.Revisao.HistoricoCompleto.Skip(registros).Select(r => r.Tipo).ToList();
            p["registros_em_ordem"] = new JsonArray(tipos.Select(t => (JsonNode)t).ToArray());
            var primeiros = tipos.SequenceEqual(["DefinirJustificativa", "DefinirSituacao", "Excluir"])
                && item.Situacao.Revisada == nova && item.Situacao.Justificativa is not null && !item.Incluido;

            var atualOutro = outro.Situacao.Revisada ?? outro.Situacao.Original!.GetValue<string>();
            var novaOutro = Faixas.OrdemSituacoes.First(c => c != atualOutro);
            const string TextoNaFila = "Texto digitado no A4 enquanto outra alteração era aplicada.";
            var t4 = j.AplicarAsync(new DefinirSituacao(outro.Id, novaOutro));
            var t5 = j.EditarTextoAsync(outro.Id, "providencia", TextoNaFila);
            await Task.WhenAll(t4, t5);
            var depois = s.Revisao.HistoricoCompleto.Skip(registros).Select(r => r.Tipo).ToList();
            var edicaoEntrou = outro.Situacao.Revisada == novaOutro && DadosEfetivos.TextoEfetivo(proj, outro, "providencia") == TextoNaFila
                && depois.Skip(3).SequenceEqual(["DefinirSituacao", "DefinirCampo"]);
            var folhas = await j.Documento!.TextoPorFolhaAsync();
            p["edicao_durante_outra_entrou"] = edicaoEntrou;
            p["tela_tem_o_texto"] = folhas.Any(f => f.Contains(TextoNaFila, StringComparison.Ordinal));
            var aplicados = s.Revisao.HistoricoCompleto.Count - registros;
            for (var k = 0; k < aplicados; k++) await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            p["desfeito_tudo"] = item.Incluido && item.Situacao.Revisada is null && outro.Situacao.Revisada is null
                && outro.Campos["providencia"].Revisado is null;
            p["ok"] = primeiros && edicaoEntrou && p["tela_tem_o_texto"]!.GetValue<bool>() && aplicados == 5 && p["desfeito_tudo"]!.GetValue<bool>();
        });

        // ---------- Tarefa 10: salvamento automático, gravação que falha, recuperação, trava e conflito (no curto) ----------
        var pacoteAuto = Path.Combine(pasta, "curto-auto.rpmvni");
        var raizDados = PastaDados.Raiz;
        string SituacaoEfetiva(ItemRevisao it) => it.Situacao.Revisada ?? it.Situacao.Original!.GetValue<string>();

        await Passo("21_autosave", async p =>
        {
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), pacoteAuto)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var item = s.Revisao.Projeto.Incluidos[0];
            var nova = Faixas.OrdemSituacoes.First(c => c != SituacaoEfetiva(item));
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            await j.AplicarAsync(new DefinirSituacao(item.Id, nova));
            var reg = s.Revisao.HistoricoCompleto[^1];
            p["estado_depois_do_comando"] = j.TextoEstadoAtual;
            var naoSalvo = s.Estado == EstadoGravacao.NaoSalvo;
            p["diario_tem_o_registro"] = Diario.Ler(raizDados, pacoteAuto, s.Disco.Sha256)?.Registros.Any(r => r.N == reg.N) == true;
            var salvou = await EsperarAteAsync(() => Task.FromResult(s.Estado == EstadoGravacao.Salvo), 25000);
            var segundos = relogio.Elapsed.TotalSeconds;
            p["salvo_sozinho_em_s"] = Math.Round(segundos, 1);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            var lido = Pacote.Ler(pacoteAuto);
            p["pacote_tem_o_registro"] = lido.Conteudo.Historico.Any(r => r.N == reg.N && r.Tipo == reg.Tipo);
            var linhas = LinhasDoDiario(Diario.CaminhoDe(raizDados, pacoteAuto));
            p["linhas_do_diario"] = linhas.Length;
            var soCabecalho = linhas.Length == 1 && linhas[0].Contains("\"cabecalho\"", StringComparison.Ordinal) && linhas[0].Contains(lido.Disco.Sha256, StringComparison.Ordinal);
            p["diario_so_com_o_cabecalho_do_sha_novo"] = soCabecalho;
            p["ok"] = naoSalvo && p["diario_tem_o_registro"]!.GetValue<bool>() && salvou && segundos >= 14 && j.TextoEstadoAtual.StartsWith("Salvo ", StringComparison.Ordinal)
                && p["pacote_tem_o_registro"]!.GetValue<bool>() && soCabecalho;
        });

        await Passo("22_gravacao_falha_sem_perder", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var item = s.Revisao.Projeto.Incluidos[0];
            var antes = await File.ReadAllBytesAsync(pacoteAuto);
            await j.AplicarAsync(new DefinirJustificativa(item.Id, "Justificativa gravada depois de soltar o arquivo."));
            var reg = s.Revisao.HistoricoCompleto[^1];
            bool gravouPreso, igual, erroVisivel, naoSalvo, diarioTem, semTmp;
            // O roteiro segura o arquivo (como o OneDrive ou o antivírus): a gravação falha antes da troca.
            using (var preso = new FileStream(pacoteAuto, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                gravouPreso = await j.GravarAgoraAsync();
                naoSalvo = j.TextoEstadoAtual == "Não salvo";
                erroVisivel = j.InfoMensagem.IsOpen && j.InfoMensagem.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
                p["mensagem_de_erro"] = j.InfoMensagem.Title + ": " + j.InfoMensagem.Message;
                // Onda 1, item 7: o texto do .NET em inglês (com o caminho inteiro) nunca chega ao aviso.
                p["mensagem_em_portugues"] = j.InfoMensagem.Message.Contains("está em uso por outro programa", StringComparison.Ordinal)
                    && !j.InfoMensagem.Message.Contains("process", StringComparison.OrdinalIgnoreCase) && !j.InfoMensagem.Message.Contains(pasta, StringComparison.OrdinalIgnoreCase);
                using var ms = new MemoryStream();
                preso.CopyTo(ms);
                igual = ms.ToArray().AsSpan().SequenceEqual(antes);
                semTmp = !File.Exists(pacoteAuto + ".tmp");
                diarioTem = Diario.Ler(raizDados, pacoteAuto, s.Disco.Sha256)?.Registros.Any(r => r.N == reg.N) == true;
            }
            p["gravou_com_o_arquivo_preso"] = gravouPreso;
            p["estado_nao_salvo"] = naoSalvo;
            p["infobar_de_erro"] = erroVisivel;
            p["pacote_em_disco_igual_ao_de_antes"] = igual;
            p["sem_tmp_deixado"] = semTmp;
            p["diario_tem_o_registro"] = diarioTem;
            var gravouSolto = await j.GravarAgoraAsync();
            var lido = Pacote.Ler(pacoteAuto);
            p["solto_grava"] = gravouSolto;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["pacote_tem_o_registro"] = lido.Conteudo.Historico.Any(r => r.N == reg.N);
            p["ok"] = !gravouPreso && naoSalvo && erroVisivel && igual && semTmp && diarioTem && gravouSolto && s.Estado == EstadoGravacao.Salvo
                && p["pacote_tem_o_registro"]!.GetValue<bool>() && p["mensagem_em_portugues"]!.GetValue<bool>();
        });

        await Passo("23_recuperacao_apos_queda", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            await j.AplicarAsync(new DefinirCampo(proj.Incluidos[1].Id, "titulo", "Título alterado antes da queda"));
            await j.AplicarAsync(new DefinirCampo(proj.Incluidos[2].Id, "verificacao", "Verificação alterada antes da queda."));
            var projetoAntes = Convert.ToHexString(ProjetoJson.Escrever(proj));
            var registros = s.Revisao.HistoricoCompleto.Count;
            j.SimularQuedaParaRoteiro();
            p["trava_solta"] = !File.Exists(Trava.CaminhoDe(pacoteAuto));
            p["diario_ficou_com"] = Diario.Ler(raizDados, pacoteAuto, EstadoDisco.Ler(pacoteAuto).Sha256)?.Registros.Count ?? 0;
            perguntas.Recuperacao = RespostaRecuperacao.Recuperar;
            var feitas = perguntas.Feitas.Count;
            try
            {
                if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não reabriu: " + j.InfoMensagem.Message);
            }
            finally { perguntas.Recuperacao = RespostaRecuperacao.Descartar; }
            var pergunta = perguntas.Feitas.Skip(feitas).FirstOrDefault(f => f.StartsWith("recuperar", StringComparison.Ordinal));
            var r = j.Sessao!;
            var mesmo = Convert.ToHexString(ProjetoJson.Escrever(r.Revisao.Projeto)) == projetoAntes;
            p["pergunta"] = pergunta;
            p["mesmo_projeto_json"] = mesmo;
            p["historico_igual"] = r.Revisao.HistoricoCompleto.Count == registros;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["aviso"] = j.InfoMensagem.Message;
            var diarioNovo = Diario.Ler(raizDados, pacoteAuto, r.Disco.Sha256);
            p["diario_novo_com_os_pendentes"] = diarioNovo?.Registros.Count ?? 0;
            var naoSalvo = r.Estado == EstadoGravacao.NaoSalvo;
            var gravou = await j.GravarAgoraAsync();
            p["gravado_depois"] = gravou;
            p["ok"] = pergunta == "recuperar 2" && mesmo && naoSalvo && p["diario_ficou_com"]!.GetValue<int>() == 2 && p["trava_solta"]!.GetValue<bool>()
                && diarioNovo?.Registros.Count == 2 && p["historico_igual"]!.GetValue<bool>() && gravou;
        });

        await Passo("24_trava_e_somente_leitura", async p =>
        {
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            var trava = Trava.CaminhoDe(pacoteAuto);
            p["trava_solta_ao_fechar"] = !File.Exists(trava);
            var inicio = DateTimeOffset.UtcNow.AddMinutes(-10);
            // Outra instância viva, em outra máquina, com batimento agora.
            EscreverTrava(trava, "OUTRA-MAQUINA", "outro.usuario", 4242, inicio, DateTimeOffset.UtcNow);
            if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var esperado = $"Aberto por outro.usuario em OUTRA-MAQUINA desde {inicio.ToLocalTime():HH:mm}: somente leitura.";
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["aviso"] = j.InfoSomenteLeitura.Message;
            var aviso = j.InfoSomenteLeitura.IsOpen && j.InfoSomenteLeitura.Message == esperado;
            await j.SelecionarAsync(s.Revisao.Projeto.Incluidos[0].Id);
            var excluirSomente = j.FaixaAtual.Habilitado(ComandosApp.InicioExcluirRestaurar);
            var salvarSomente = j.FaixaAtual.Habilitado(ComandosApp.ArquivoSalvar);
            var registros = s.Revisao.HistoricoCompleto.Count;
            await j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            var nadaMudou = s.Revisao.HistoricoCompleto.Count == registros && s.Revisao.Projeto.Incluidos.Count == 3;
            var somenteLeitura = j.TextoEstadoAtual == "Somente leitura";
            p["excluir_habilitado"] = excluirSomente;
            p["salvar_habilitado"] = salvarSomente;
            p["nada_mudou"] = nadaMudou;
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou o somente leitura");
            var doOutro = LerPidDaTrava(trava) == 4242;
            p["trava_do_outro_intacta"] = doOutro;

            // Batimento de 3 min atrás: abandonada, retomada; abre editável.
            EscreverTrava(trava, "OUTRA-MAQUINA", "outro.usuario", 4242, inicio, DateTimeOffset.UtcNow.AddMinutes(-3));
            if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
            var e = j.Sessao!;
            p["estado_com_trava_abandonada"] = j.TextoEstadoAtual;
            await j.SelecionarAsync(e.Revisao.Projeto.Incluidos[0].Id);
            var excluirEditavel = j.FaixaAtual.Habilitado(ComandosApp.InicioExcluirRestaurar);
            var nossa = LerPidDaTrava(trava) == Environment.ProcessId;
            p["excluir_habilitado_editavel"] = excluirEditavel;
            p["trava_retomada_por_este_app"] = nossa;
            var editavel = !e.SomenteLeitura && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal) && excluirEditavel && nossa && !j.InfoSomenteLeitura.IsOpen;

            // Trava perdida com o arquivo aberto (outro programa a apagou): o batimento vira somente leitura; fechar sem salvar guarda a
            // recuperação, e reabrir a oferece.
            const string TituloAntesDePerder = "Título feito antes de perder a trava";
            var alvo = e.Revisao.Projeto.Incluidos[1];
            await j.AplicarAsync(new DefinirCampo(alvo.Id, "titulo", TituloAntesDePerder));
            File.SetAttributes(trava, FileAttributes.Normal);
            File.Delete(trava);
            var bateu = await e.BaterAsync();
            var perdida = !bateu && e.SomenteLeitura && j.TextoEstadoAtual == "Somente leitura" && j.InfoSomenteLeitura.IsOpen
                && j.InfoSomenteLeitura.Message == TextosGravacao.TravaPerdida && !j.FaixaAtual.Habilitado(ComandosApp.InicioExcluirRestaurar);
            p["trava_perdida_vira_somente_leitura"] = perdida;
            // Exportar com alterações que o arquivo não tem (e não pode receber): recusado, apontando Salvar como.
            var pdfSemTrava = Path.Combine(pasta, "curto-auto-sem-trava.pdf");
            File.Delete(pdfSemTrava);
            var (exportouSemTrava, _, erroSemTrava, excSemTrava) = await j.ExportarParaAsync(pdfSemTrava);
            var exportarRecusado = !exportouSemTrava && excSemTrava is ExportacaoDivergenteException && erroSemTrava == TextosGravacao.ExportarSemGravar(false)
                && !File.Exists(pdfSemTrava) && !File.Exists(Exportador.CaminhoReciboDe(pdfSemTrava));
            p["exportar_sem_trava_recusado"] = exportarRecusado;
            p["mensagem_exportar_sem_trava"] = erroSemTrava;
            var feitas = perguntas.Feitas.Count;
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou depois de perder a trava");
            var perguntouAoFechar = perguntas.Feitas.Skip(feitas).Any(f => f.StartsWith("fechar sem gravar", StringComparison.Ordinal));
            p["perguntou_ao_fechar"] = perguntouAoFechar;
            perguntas.Recuperacao = RespostaRecuperacao.Recuperar;
            try { if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não reabriu: " + j.InfoMensagem.Message); }
            finally { perguntas.Recuperacao = RespostaRecuperacao.Descartar; }
            var pergunta = perguntas.Feitas.Skip(feitas).FirstOrDefault(f => f.StartsWith("recuperar", StringComparison.Ordinal));
            var rec = j.Sessao!;
            var recuperou = pergunta == "recuperar 1" && rec.Estado == EstadoGravacao.NaoSalvo
                && DadosEfetivos.TextoEfetivo(rec.Revisao.Projeto, rec.Revisao.Projeto.Item(alvo.Id)!, "titulo") == TituloAntesDePerder;
            p["recuperou_depois_de_fechar_sem_salvar"] = recuperou;
            var gravou = await j.GravarAgoraAsync();
            p["ok"] = p["trava_solta_ao_fechar"]!.GetValue<bool>() && somenteLeitura && aviso && !excluirSomente && !salvarSomente && nadaMudou && doOutro
                && editavel && perdida && exportarRecusado && perguntouAoFechar && recuperou && gravou;
        });

        await Passo("25_conflito_nao_sobrescreve", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            if (s.SomenteLeitura) throw new InvalidOperationException("aberto somente para leitura");
            // Outro programa grava o mesmo pacote depois de aberto.
            var outro = Pacote.Ler(pacoteAuto);
            outro.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO PROGRAMA GRAVOU DEPOIS DE ABRIR";
            Pacote.Gravar(pacoteAuto, outro.Conteudo, outro.Disco);
            var bytesDoOutro = await File.ReadAllBytesAsync(pacoteAuto);
            const string Titulo = "Título alterado antes do conflito";
            var item = s.Revisao.Projeto.Incluidos[0];
            await j.AplicarAsync(new DefinirCampo(item.Id, "titulo", Titulo));
            var reg = s.Revisao.HistoricoCompleto[^1];
            var gravou = await j.GravarAgoraAsync();
            p["gravou"] = gravou;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            var conflito = j.TextoEstadoAtual == "Conflito" && j.InfoConflito.IsOpen;
            p["infobar_conflito"] = j.InfoConflito.IsOpen;
            var intacto = (await File.ReadAllBytesAsync(pacoteAuto)).AsSpan().SequenceEqual(bytesDoOutro);
            p["disco_com_os_bytes_do_outro"] = intacto;
            var pdf = Path.Combine(pasta, "curto-auto-conflito.pdf");
            File.Delete(pdf);
            var (exportou, _, erroExportar, excecao) = await j.ExportarParaAsync(pdf);
            var exportarRecusa = !exportou && excecao is ExportacaoDivergenteException && !File.Exists(pdf) && erroExportar == TextosGravacao.ExportarSemGravar(true);
            p["exportar_recusa"] = exportarRecusa;
            p["erro_ao_exportar"] = erroExportar;

            var copiou = await j.SalvarCopiaDeConflitoAsync();
            var copia = s.Caminho;
            var lidaCopia = Pacote.Ler(copia);
            var copiaConfere = copiou && !string.Equals(copia, pacoteAuto, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(copia).StartsWith("curto-auto (cópia ", StringComparison.Ordinal)
                && lidaCopia.Conteudo.Historico.Any(r => r.N == reg.N)
                && DadosEfetivos.TextoEfetivo(lidaCopia.Conteudo.Projeto, lidaCopia.Conteudo.Projeto.Item(item.Id)!, "titulo") == Titulo;
            p["copia"] = Path.GetFileName(copia);
            p["copia_tem_as_alteracoes"] = copiaConfere;
            var originalIntacto = (await File.ReadAllBytesAsync(pacoteAuto)).AsSpan().SequenceEqual(bytesDoOutro);
            p["original_continua_o_do_outro"] = originalIntacto;
            p["estado_depois_da_copia"] = j.TextoEstadoAtual;
            var travas = !File.Exists(Trava.CaminhoDe(pacoteAuto)) && LerPidDaTrava(Trava.CaminhoDe(copia)) == Environment.ProcessId;
            p["trava_passou_para_a_copia"] = travas;
            var copiaOk = copiaConfere && originalIntacto && s.Estado == EstadoGravacao.Salvo && !j.InfoConflito.IsOpen && travas;

            // Recarregar: outro programa muda a cópia; a alteração desta sessão vai para um órfão do diário, nunca é apagada.
            var outra = Pacote.Ler(copia);
            outra.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO PROGRAMA DE NOVO";
            Pacote.Gravar(copia, outra.Conteudo, outra.Disco);
            await j.AplicarAsync(new DefinirCampo(s.Revisao.Projeto.Incluidos[1].Id, "titulo", "Título que o Recarregar guarda no órfão"));
            var conflitoDeNovo = !await j.GravarAgoraAsync() && s.Estado == EstadoGravacao.Conflito;
            var prefixo = Path.GetFileNameWithoutExtension(Diario.CaminhoDe(raizDados, copia));
            int Orfaos() => Directory.GetFiles(Path.Combine(raizDados, "recuperacao"), prefixo + ".orfao-*").Length;
            var orfaosAntes = Orfaos();
            var recarregou = await j.RecarregarAsync();
            var r = j.Sessao;
            var requerente = r?.Revisao.Projeto.Documento["capa"]?["requerente"]?.GetValue<string>();
            p["recarregou"] = recarregou;
            p["requerente_depois_de_recarregar"] = requerente;
            p["orfao_guardado"] = Orfaos() == orfaosAntes + 1;
            var recarregarOk = conflitoDeNovo && recarregou && requerente == "OUTRO PROGRAMA DE NOVO" && r!.Estado == EstadoGravacao.Salvo
                && Orfaos() == orfaosAntes + 1 && !j.InfoConflito.IsOpen;
            // Fechar em conflito: a pergunta não promete a recuperação (o arquivo é outra versão) e a saída principal é Salvar como cópia.
            var aberto = r!.Caminho;
            var terceiro = Pacote.Ler(aberto);
            terceiro.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO PROGRAMA PELA TERCEIRA VEZ";
            Pacote.Gravar(aberto, terceiro.Conteudo, terceiro.Disco);
            const string TituloAoFechar = "Título salvo na cópia ao fechar em conflito";
            await j.AplicarAsync(new DefinirCampo(r.Revisao.Projeto.Incluidos[2].Id, "titulo", TituloAoFechar));
            var regFechar = r.Revisao.HistoricoCompleto[^1];
            var conflitoAoFechar = !await j.GravarAgoraAsync() && r.Estado == EstadoGravacao.Conflito;
            var feitas = perguntas.Feitas.Count;
            var copiasAntes = Directory.GetFiles(pasta, Path.GetFileNameWithoutExtension(aberto) + " (cópia *.rpmvni").ToHashSet();
            perguntas.Fechar = RespostaFechar.SalvarCopia;
            bool fechou;
            try { fechou = await j.FecharSessaoAsync(); }
            finally { perguntas.Fechar = RespostaFechar.FecharSemSalvar; }
            var perguntaConflito = perguntas.Feitas.Skip(feitas).FirstOrDefault(f => f.StartsWith("fechar sem gravar", StringComparison.Ordinal));
            var textoConflito = perguntas.Fechamentos.LastOrDefault() ?? "";
            var novaCopia = Directory.GetFiles(pasta, Path.GetFileNameWithoutExtension(aberto) + " (cópia *.rpmvni").Where(c => !copiasAntes.Contains(c)).ToList();
            var copiaAoFechar = novaCopia.Count == 1 && Pacote.Ler(novaCopia[0]).Conteudo.Historico.Any(x => x.N == regFechar.N);
            p["pergunta_ao_fechar_em_conflito"] = perguntaConflito;
            p["texto_ao_fechar_em_conflito"] = textoConflito;
            p["copia_ao_fechar"] = novaCopia.Count == 1 ? Path.GetFileName(novaCopia[0]) : null;
            var fecharOk = conflitoAoFechar && fechou && j.Sessao is null && perguntaConflito?.StartsWith("fechar sem gravar (conflito)", StringComparison.Ordinal) == true
                && !textoConflito.Contains("oferece recuperá-las", StringComparison.Ordinal) && textoConflito.Contains("não serão oferecidas de novo", StringComparison.Ordinal)
                && copiaAoFechar;
            p["fechar_em_conflito_salva_copia"] = fecharOk;
            p["ok"] = !gravou && conflito && intacto && exportarRecusa && copiaOk && recarregarOk && fecharOk;
        });

        // ---------- Tarefa 11: pesquisa, tema, recentes e comparar com o original (no longo e em cópias do curto) ----------
        // O contador de ocorrências é independente da pesquisa da página: texto de cada nó da área útil sem acento nem caixa.
        const string ContadorJs = "((termo) => { const sem = (s) => s.normalize('NFD').replace(/[\\u0300-\\u036f]/g, '').toLowerCase(); const alvo = sem(termo); let total = 0; " +
            "for (const area of document.querySelectorAll('section.folha > .area')) { const w = document.createTreeWalker(area, NodeFilter.SHOW_TEXT); " +
            "for (let n = w.nextNode(); n; n = w.nextNode()) { if (n.parentElement.closest('[data-pmvni-oculto], style, script')) continue; total += sem(n.data).split(alvo).length - 1; } } return total; })";
        const string OcorrenciaAtualJs = "(() => { const h = CSS.highlights.get('pmvni-busca-atual'); return (h && h.size === 1) ? [...h][0] : null; })()";

        await Passo("26_pesquisa", async p =>
        {
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var proj = s.Revisao.Projeto;
            var item10 = proj.Incluidos[9];
            var titulo = DadosEfetivos.TextoEfetivo(proj, item10, "titulo")!;
            var elementosAntes = (await Js(j, "document.body.querySelectorAll('*').length")).GetInt32();
            var textoAntes = await j.Documento!.TextoPorFolhaAsync();

            // Ctrl+F foca a caixa da barra de título.
            await j.ExecutarComandoAsync(ComandosApp.PesquisaFocar);
            p["caixa_habilitada"] = j.CaixaPesquisaHabilitada;
            p["ctrl_f_foca_a_caixa"] = j.CaixaPesquisaComFoco;

            // O título do ITEM 10: ao menos uma ocorrência, "1 de N", a página mostra a primeira e ela está dentro do card do item 10.
            await j.DigitarPesquisaAsync(titulo);
            p["titulo_pesquisado"] = titulo;
            p["total_do_titulo"] = j.PesquisaTotal;
            p["resultado_do_titulo"] = j.TextoResultadoPesquisa;
            p["ref_do_titulo"] = j.PesquisaRef;
            var dentroDoCard = (await Js(j, $"((r) => !r ? false : [...document.querySelectorAll('section.folha .card[data-ref=\"{item10.Id}\"]')].some((c) => {{ const a = r.getBoundingClientRect(), b = c.getBoundingClientRect(); return a.top >= b.top - 1 && a.bottom <= b.bottom + 1 && a.height > 0; }}))({OcorrenciaAtualJs})")).GetBoolean();
            var naJanela = (await Js(j, $"((r) => !r ? false : (() => {{ const a = r.getBoundingClientRect(); return a.top >= 0 && a.bottom <= window.innerHeight; }})())({OcorrenciaAtualJs})")).GetBoolean();
            p["rolou_para_o_item_10"] = dentroDoCard && naJanela;
            var tituloOk = j.PesquisaTotal >= 1 && j.TextoResultadoPesquisa == $"1 de {j.PesquisaTotal}" && j.PesquisaRef == item10.Id && dentroDoCard && naJanela;
            await CapturarWebAsync(j, Path.Combine(pasta, "26-pesquisa-titulo.png"));
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "26-janela-xaml-pesquisa.png"));

            // Sem acento e sem caixa; Enter avança, Shift+Enter volta, com volta ao fim e ao começo (os mesmos caminhos das teclas).
            await j.DigitarPesquisaAsync("VERIFICAÇÃO");
            var totalMaiusculo = j.PesquisaTotal;
            await j.DigitarPesquisaAsync("verificacao");
            var total = j.PesquisaTotal;
            var contados = (await Js(j, ContadorJs + "('verificacao')")).GetInt32();
            p["total_verificacao"] = total;
            p["contador_independente"] = contados;
            var semAcento = total >= 60 && total == totalMaiusculo && total == contados && j.TextoResultadoPesquisa == $"1 de {total}";
            await j.IrNaPesquisaAsync(+1);
            var enter = j.PesquisaAtual == 2 && j.TextoResultadoPesquisa == $"2 de {total}";
            await j.IrNaPesquisaAsync(-1);
            var shiftEnter = j.PesquisaAtual == 1;
            await j.IrNaPesquisaAsync(-1);
            var voltaAoFim = j.PesquisaAtual == total && j.TextoResultadoPesquisa == $"{total} de {total}";
            await j.IrNaPesquisaAsync(+1);
            var voltaAoComeco = j.PesquisaAtual == 1;
            p["enter_avanca"] = enter;
            p["shift_enter_volta"] = shiftEnter;
            p["volta_ao_fim_e_ao_comeco"] = voltaAoFim && voltaAoComeco;

            // O realce não toca o DOM das folhas.
            var elementosDepois = (await Js(j, "document.body.querySelectorAll('*').length")).GetInt32();
            var textoDepois = await j.Documento!.TextoPorFolhaAsync();
            var realcesNaPagina = (await Js(j, "CSS.highlights.get('pmvni-busca').size")).GetInt32();
            p["elementos_do_body_antes_e_depois"] = new JsonArray(elementosAntes, elementosDepois);
            p["ocorrencias_realcadas"] = realcesNaPagina;
            var domIgual = elementosAntes == elementosDepois && textoAntes.SequenceEqual(textoDepois) && realcesNaPagina == total;

            // Depois de recompor (um comando), a pesquisa é refeita sobre as folhas novas, na mesma ocorrência, sem rolar.
            await j.IrNaPesquisaAsync(+1);
            await j.IrNaPesquisaAsync(+1);
            var atualAntes = j.PesquisaAtual;
            var item = proj.Incluidos[19];
            var atual = item.Situacao.Revisada ?? item.Situacao.Original!.GetValue<string>();
            await j.AplicarAsync(new DefinirSituacao(item.Id, Faixas.OrdemSituacoes.First(c => c != atual)));
            var refeita = await EsperarAteAsync(async () => (await Js(j, "(CSS.highlights.get('pmvni-busca') || { size: 0 }).size")).GetInt32() == total, 8000);
            p["pesquisa_refeita_depois_de_recompor"] = refeita;
            p["ocorrencia_atual_mantida"] = j.PesquisaAtual == atualAntes && atualAntes == 3;
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            await EsperarAteAsync(async () => (await Js(j, "(CSS.highlights.get('pmvni-busca') || { size: 0 }).size")).GetInt32() == total, 8000);

            // Exportar com o realce na tela: o PDF sai das folhas do host de composição, sem realce nem marcas.
            var pdf = Path.Combine(pasta, "longo-com-pesquisa.pdf");
            File.Delete(pdf);
            var (ok, x, erro, _) = await j.ExportarParaAsync(pdf);
            if (!ok || x is null) throw new InvalidOperationException("exportação falhou: " + erro);
            var info = InfoPdf.Ler(await File.ReadAllBytesAsync(pdf));
            var html = j.Aberto!.Preparado.Render.Html;
            var paginasIguais = info.Paginas == j.Aberto.Composicao.Folhas;
            var planoIgual = x.Plano.ParaJson() == j.Aberto.Composicao.Plano.ParaJson();
            // "<mark>" como elemento (o SVG do kit tem <marker>, que não conta).
            var htmlLimpo = !html.Contains("pmvni-busca", StringComparison.Ordinal) && !html.Contains("data-pmvni", StringComparison.Ordinal)
                && !System.Text.RegularExpressions.Regex.IsMatch(html, @"<mark[\s>/]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var pdfOk = paginasIguais && planoIgual && htmlLimpo;
            p["pdf_com_pesquisa_ativa"] = $"{info.Paginas} páginas, plano igual, HTML sem realce";

            // Termo vazio limpa tudo.
            await j.DigitarPesquisaAsync("");
            var limpou = j.TextoResultadoPesquisa == "" && j.PesquisaTotal == 0 && (await Js(j, "CSS.highlights.size")).GetInt32() == 0;
            p["termo_vazio_limpa"] = limpou;
            p["conferencias"] = new JsonObject
            {
                ["caixa_habilitada"] = j.CaixaPesquisaHabilitada, ["titulo"] = tituloOk, ["sem_acento_e_sem_caixa"] = semAcento, ["dom_igual"] = domIgual,
                ["pdf"] = pdfOk, ["pdf_paginas_iguais"] = paginasIguais, ["pdf_plano_igual"] = planoIgual, ["html_sem_realce"] = htmlLimpo, ["limpa"] = limpou, ["total_maiusculo"] = totalMaiusculo,
            };
            p["ok"] = j.CaixaPesquisaHabilitada && p["ctrl_f_foca_a_caixa"]!.GetValue<bool>() && tituloOk && semAcento && enter && shiftEnter && voltaAoFim && voltaAoComeco
                && domIgual && refeita && p["ocorrencia_atual_mantida"]!.GetValue<bool>() && pdfOk && limpou;
        });

        await Passo("27_tema_chrome", async p =>
        {
            // O tema do app (janela, faixa, painéis) pelos comandos Exibir › Tema; as folhas continuam brancas nos dois. Guardado em ajustes.json.
            if (j.Sessao is null) throw new InvalidOperationException("nenhum relatório aberto");
            const string Cores = "({ escuro: matchMedia('(prefers-color-scheme: dark)').matches, fundoDaPagina: getComputedStyle(document.documentElement).backgroundColor, " +
                "fundoDaFolha: getComputedStyle(document.querySelector('section.folha')).backgroundColor })";
            var ajustesJson = Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo);
            string? Guardado() => JsonNode.Parse(File.ReadAllText(ajustesJson))?["tema"]?.GetValue<string>();
            var textoAntes = await j.Documento!.TextoPorFolhaAsync();
            var faixa = j.FaixaAtual;

            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaEscuro);
            await Esperar(900);
            var escuro = await Js(j, Cores);
            var escuroOk = j.TemaAtual == ElementTheme.Dark && j.TemaDosAjustes == "escuro" && Guardado() == "escuro"
                && faixa.Marcado(ComandosApp.ExibirTemaEscuro) && !faixa.Marcado(ComandosApp.ExibirTemaClaro) && !faixa.Marcado(ComandosApp.ExibirTemaSistema)
                && j.TemaMarcadoNaPagina == "escuro" && escuro.GetProperty("escuro").GetBoolean();
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "27-janela-xaml-escuro.png"));
            await CapturarXamlAsync(j.TrilhoAtual, Path.Combine(pasta, "27-trilho-escuro.png"));
            await CapturarWebAsync(j, Path.Combine(pasta, "27-tela-escuro.png"));

            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaClaro);
            await Esperar(900);
            var claro = await Js(j, Cores);
            var claroOk = j.TemaAtual == ElementTheme.Light && j.TemaDosAjustes == "claro" && Guardado() == "claro"
                && faixa.Marcado(ComandosApp.ExibirTemaClaro) && !faixa.Marcado(ComandosApp.ExibirTemaEscuro)
                && j.TemaMarcadoNaPagina == "claro" && !claro.GetProperty("escuro").GetBoolean();
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "27-janela-xaml-claro.png"));
            await CapturarXamlAsync(j.TrilhoAtual, Path.Combine(pasta, "27-trilho-claro.png"));

            // Pela página Ajustes › Aparência › Tema (a caixa de escolha, como o usuário) e de volta ao sistema.
            j.AjustesPagina.CaixaTema.SelectedIndex = Ajustes.Temas.ToList().IndexOf("escuro");
            var pelaPagina = j.TemaAtual == ElementTheme.Dark && Guardado() == "escuro";
            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaSistema);
            await Esperar(400);
            var sistemaOk = j.TemaDosAjustes == "sistema" && Guardado() == "sistema" && faixa.Marcado(ComandosApp.ExibirTemaSistema) && j.TemaMarcadoNaPagina == "sistema";

            // Página Ajustes (módulo do trilho, embaixo): tema, intervalo do salvamento, pasta de recuperação e versões.
            await j.MudarModuloAsync(Modulo.Ajustes);
            var pag = j.AjustesPagina;
            var noTrilho = j.TrilhoAtual.Modulos.Contains(Modulo.Ajustes);
            var visivel = j.PaginaAjustesVisivel && j.ModuloAtual == Modulo.Ajustes && j.FaixaAtual.Definicao!.Modulo == Modulo.Ajustes && !j.CaixaPesquisaHabilitada;
            var versoes = pag.TextoVersoes.Text;
            var recuperacao = pag.TextoPastaRecuperacao.Text;
            // A página abre em Geral; intervalo e recuperação ficam em "Relatórios e revisão" (auditoria §3.3).
            var abriuEmGeral = pag.CategoriaAtual == CategoriasAjustes.Geral;
            pag.MostrarCategoria(CategoriasAjustes.Relatorios);
            await Esperar(200);
            var textosOk = versoes.Contains("Versão do app: ", StringComparison.Ordinal) && versoes.Contains($"Modelo do relatório: rev{ModeloOficial.Revisao}", StringComparison.Ordinal)
                && versoes.Contains("WebView2: " + j.NucleoWeb.Environment.BrowserVersionString, StringComparison.Ordinal)
                && recuperacao == Path.Combine(PastaDados.Raiz, "recuperacao");
            var intervaloAntes = pag.CaixaIntervalo.SelectedItem is Microsoft.UI.Xaml.Controls.ComboBoxItem { Tag: int i0 } ? i0 : -1;
            pag.CaixaIntervalo.SelectedIndex = Ajustes.Intervalos.ToList().IndexOf(60); // como a escolha do usuário na caixa
            string? Intervalo() => JsonNode.Parse(File.ReadAllText(ajustesJson))?["intervalo_autosave_s"]?.GetValue<int>().ToString();
            var intervaloOk = intervaloAntes == 15 && j.IntervaloAutosaveAtual == 60 && Intervalo() == "60";
            pag.CaixaIntervalo.SelectedIndex = Ajustes.Intervalos.ToList().IndexOf(15);
            intervaloOk = intervaloOk && j.IntervaloAutosaveAtual == 15 && Intervalo() == "15";
            await Esperar(300);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "27-janela-xaml-ajustes.png"));
            p["pagina_ajustes"] = new JsonObject { ["abriu_em_geral"] = abriuEmGeral, ["categoria"] = pag.CategoriaAtual, ["no_trilho"] = noTrilho, ["visivel_com_a_faixa_dos_ajustes"] = visivel, ["versoes"] = versoes, ["pasta_de_recuperacao"] = recuperacao, ["intervalo_guardado"] = intervaloOk };
            await j.MudarModuloAsync(Modulo.Relatorios);
            var voltou = !j.PaginaAjustesVisivel && j.ModuloAtual == Modulo.Relatorios && j.CaixaPesquisaHabilitada;
            var ajustesOk = noTrilho && visivel && textosOk && intervaloOk && voltou && pag.CategoriaAtual == CategoriasAjustes.Relatorios;
            p["escuro"] = JsonNode.Parse(escuro.GetRawText());
            p["claro"] = JsonNode.Parse(claro.GetRawText());
            p["tema_guardado_em_ajustes"] = Guardado();
            var fundoMuda = escuro.GetProperty("fundoDaPagina").GetString() != claro.GetProperty("fundoDaPagina").GetString();
            var folhasBrancas = escuro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)" && claro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)";
            p["folhas_brancas_nos_dois"] = folhasBrancas;
            p["texto_por_folha_igual"] = (await j.Documento!.TextoPorFolhaAsync()).SequenceEqual(textoAntes);
            p["conferencias"] = new JsonObject
            {
                ["escuro"] = escuroOk, ["claro"] = claroOk, ["pela_pagina_ajustes"] = pelaPagina, ["sistema"] = sistemaOk, ["fundo_muda"] = fundoMuda, ["pagina_ajustes"] = ajustesOk,
                ["tema_depois_de_escuro"] = j.TemaDosAjustes,
            };
            p["ok"] = escuroOk && claroOk && pelaPagina && sistemaOk && fundoMuda && folhasBrancas && p["texto_por_folha_igual"]!.GetValue<bool>() && ajustesOk;
        });

        var recenteA = Path.Combine(pasta, "recentes-a.rpmvni");
        var recenteB = Path.Combine(pasta, "recentes-b.rpmvni");
        await Passo("28_recentes", async p =>
        {
            // Dois pacotes abertos (o mais recente primeiro no menu), abrir pelo menu, arquivo apagado some do menu, no máximo 10.
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), recenteA)) throw new InvalidOperationException("não importou A: " + j.InfoMensagem.Message);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), recenteB)) throw new InvalidOperationException("não importou B: " + j.InfoMensagem.Message);
            var faixa = j.FaixaAtual;
            faixa.EscolherAba("Arquivo");
            await Esperar(250);
            var valores = faixa.AtualizarMenuDinamico(ComandosApp.ArquivoRecentes);
            p["menu_recentes"] = new JsonArray(faixa.TextosDoMenuDinamico(ComandosApp.ArquivoRecentes).Take(4).Select(t => (JsonNode)t).ToArray());
            var doisNoTopo = valores.Count >= 2 && valores[0] == Path.GetFullPath(recenteB) && valores[1] == Path.GetFullPath(recenteA);
            p["os_dois_no_topo_mais_recente_primeiro"] = doisNoTopo;
            p["menu_habilitado"] = faixa.Habilitado(ComandosApp.ArquivoRecentes);
            string? GuardadoNoTopo() => JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["recentes"]?[0]?.GetValue<string>();
            var guardado = GuardadoNoTopo() == Path.GetFullPath(recenteB);

            // Escolher o A no menu abre o A e ele sobe ao topo (o B fecha).
            await j.AbrirRecenteAsync(valores[1]);
            var abriuA = string.Equals(j.Sessao?.Caminho, Path.GetFullPath(recenteA), StringComparison.OrdinalIgnoreCase);
            var topoAgora = faixa.AtualizarMenuDinamico(ComandosApp.ArquivoRecentes);
            var aNoTopo = topoAgora.Count >= 2 && topoAgora[0] == Path.GetFullPath(recenteA) && topoAgora[1] == Path.GetFullPath(recenteB);

            // Apagar o B (fechado) por fora: some do menu (nada desabilitado); a tentativa de abrir um item velho avisa.
            foreach (var f in Directory.GetFiles(pasta, "recentes-b.rpmvni*").Concat(Directory.GetFiles(pasta, ".~recentes-b.rpmvni*"))) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
            var depois = faixa.AtualizarMenuDinamico(ComandosApp.ArquivoRecentes);
            var sumiu = !depois.Contains(Path.GetFullPath(recenteB), StringComparer.OrdinalIgnoreCase) && depois.Count > 0 && depois[0] == Path.GetFullPath(recenteA);
            var abertoAntes = j.Sessao?.Caminho;
            await j.AbrirRecenteAsync(Path.GetFullPath(recenteB));
            var avisou = j.InfoMensagem.IsOpen && j.InfoMensagem.Title == "O arquivo não existe mais" && j.Sessao?.Caminho == abertoAntes;
            var max = j.RecentesNosAjustes.Count <= Ajustes.MaxRecentes && j.RecentesNosAjustes.Count == j.RecentesNosAjustes.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            p["depois_de_abrir_o_a"] = new JsonArray(topoAgora.Take(2).Select(t => (JsonNode)Path.GetFileName(t)).ToArray());
            p["depois_de_apagar_o_b"] = new JsonArray(depois.Take(3).Select(t => (JsonNode)Path.GetFileName(t)).ToArray());
            p["aviso_de_arquivo_ausente"] = avisou;
            p["recentes_guardados"] = j.RecentesNosAjustes.Count;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "28-janela-xaml-arquivo.png"));
            p["ok"] = doisNoTopo && guardado && abriuA && aNoTopo && sumiu && avisou && max && faixa.Habilitado(ComandosApp.ArquivoRecentes);
        });

        var pacoteCitacoes = Path.Combine(pasta, "curto-citacoes.rpmvni");
        await Passo("29_comparar_com_o_original", async p =>
        {
            // O longo, com as alterações dos passos 13–17 (ITEM 03: verificação, situação e justificativa; ITEM 05 excluído).
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var proj = s.Revisao.Projeto;
            var dados = JsonNode.Parse(await File.ReadAllTextAsync(dadosLongo))!["itens"]!.AsArray();
            var item3 = dados[2]!.AsObject();
            var codigoOriginal = item3["situacao"]!.GetValue<string>();
            var verificacaoOriginal = item3["verificacao"]!.GetValue<string>();
            await j.ExecutarComandoAsync(ComandosApp.RevisaoComparar);
            var mostrada = perguntas.Informados.LastOrDefault();
            var esperado = string.Join("\n",
                "2 itens alterados em relação ao original recebido:",
                "",
                "ITEM 03",
                "  Situação: " + Pmvni.Composicao.Kit.Situacoes.Sit[codigoOriginal].Texto + " → " + Pmvni.Composicao.Kit.Situacoes.Sit["ATENDE"].Texto,
                "  Justificativa: " + Justificativa,
                "  Verificação técnica:",
                "    Original: " + verificacaoOriginal,
                "    Revisado: " + NovaVerificacao,
                "",
                "Item excluído (era o 05)",
                "  Excluído do relatório (não sai no PDF).",
                "",
                "Citações convertidas na importação (0):",
                "  Nenhuma.");
            p["texto_mostrado"] = mostrada.Texto;
            p["titulo_do_dialogo"] = mostrada.Titulo;
            var diferencas = Comparacao.Diferencas(proj, s.Revisao.HistoricoCompleto);
            p["itens_alterados"] = new JsonArray(diferencas.Select(d => (JsonNode)Comparacao.Rotulo(d)).ToArray());
            var prefixo = esperado + "\n\nFiguras alteradas:\n";
            var blocoFiguras = mostrada.Texto?.StartsWith(prefixo, StringComparison.Ordinal) == true ? mostrada.Texto[prefixo.Length..] : "";
            var figurasCorrigidas = Enumerable.Range(3, 3).All(n => blocoFiguras.Contains($"Figura {n} — ", StringComparison.Ordinal))
                && blocoFiguras.Split('\n').Count(l => l.EndsWith(": itens «item 05» → «»", StringComparison.OrdinalIgnoreCase)) == 3;
            p["figuras_com_citacoes_corrigidas"] = figurasCorrigidas;
            var longoOk = mostrada.Titulo == "Comparar com o original" && figurasCorrigidas && j.TextoComparacao() == mostrada.Texto
                && diferencas.Count == 2 && diferencas[1].Excluido && diferencas[1].NumeroOriginal == 5;
            p["longo_confere_os_textos_exatos"] = longoOk;

            // Citações convertidas na importação aparecem em seção própria, mesmo no item sem alteração (PF13).
            var copia = Path.Combine(pasta, "curto-citacoes.json");
            var no = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(corpus, "curto.json")))!;
            var v3 = no["itens"]![2]!["verificacao"]!.GetValue<string>();
            no["itens"]![2]!["verificacao"] = v3 + " Ver item 02 e ITEM 01.";
            await File.WriteAllTextAsync(copia, no.ToJsonString());
            try
            {
                if (!await j.ImportarParaAsync(copia, pacoteCitacoes)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            }
            finally { File.Delete(copia); }
            await j.ExecutarComandoAsync(ComandosApp.RevisaoComparar);
            var citacoes = perguntas.Informados.LastOrDefault().Texto;
            var esperadoCitacoes = string.Join("\n",
                "Nenhum item foi alterado em relação ao original recebido.",
                "",
                "Citações convertidas na importação (2):",
                "  ITEM 03 · Verificação técnica: «item 02» → ITEM 02",
                "  ITEM 03 · Verificação técnica: «ITEM 01» → ITEM 01");
            p["texto_com_citacoes"] = citacoes;
            var citacoesOk = citacoes == esperadoCitacoes;
            p["citacoes_em_secao_propria"] = citacoesOk;
            p["ok"] = longoOk && citacoesOk && j.FaixaAtual.Habilitado(ComandosApp.RevisaoComparar);
        });

        // ---------- Etapa 4: editor de figura (marcações editáveis sobre a imagem) ----------
        await Passo("29b_figuras_marcacoes", async p =>
        {
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var dadosAntes = DadosEfetivos.Gerar(s.Revisao.Projeto).Json;
            var svgAntes = Ocorrencias(s.Atual!.Preparado.Render.Html, "data:image/svg+xml;base64,");
            var fig = CamadaFiguras.Inicial(s.Revisao.Projeto)[0].Id;
            j.AbrirEditorFigura(fig);
            p["editor_abriu"] = j.EditorFiguraAberto;
            var item2 = s.Revisao.Projeto.Incluidos[1].Id;
            await j.AplicarAsync(new IncluirMarcacao(fig, "mk_roteiro", Pmvni.Composicao.Figuras.TiposMarcacao.Regiao, Caixa: [.2, .2, .45, .45], Elemento: "Região do roteiro"));
            await j.AplicarAsync(new VincularItens(fig, "mk_roteiro", [item2]));
            var html = s.Atual!.Preparado.Render.Html;
            var folhas = await j.Documento!.TextoPorFolhaAsync();
            var f = s.Revisao.Projeto.Figuras!.Single(x => x.Id == fig);
            var numero = CamadaFiguras.Numero(f, "mk_roteiro");
            p["numero_visivel"] = numero;
            p["figura_virou_svg"] = Ocorrencias(html, "data:image/svg+xml;base64,") == svgAntes + 1;
            p["legenda_nas_folhas"] = folhas.Any(t => t.Contains("Região do roteiro.", StringComparison.Ordinal) && t.Contains("(item 02)", StringComparison.Ordinal));
            await j.ExecutarComandoAsync(ComandosApp.RevisaoComparar);
            p["comparar_lista_a_figura"] = j.TextoComparacao().Contains("Marcação 3 (Região do roteiro) incluída.", StringComparison.Ordinal);
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var voltou = DadosEfetivos.Gerar(s.Revisao.Projeto).Json.AsSpan().SequenceEqual(dadosAntes);
            p["desfazer_volta_ao_kit"] = voltou;
            p["erros"] = new JsonArray(j.UltimosErros.Select(e => (JsonNode)e).ToArray());
            p["ok"] = j.EditorFiguraAberto && numero == 3 && (bool)p["figura_virou_svg"]! && (bool)p["legenda_nas_folhas"]! && (bool)p["comparar_lista_a_figura"]! && voltou;
            if (j.ConteudoEditorFigura is { } editor)
                await CapturarXamlAsync(editor, Path.Combine(pasta, "29b-editor-figura.png"));
            j.FecharEditorFigura();
            // Inserir região da prancha: a janela tem de abrir (correção 2026-11-16: o zoom mínimo 0,05 derrubava a janela na montagem).
            await j.ExecutarComandoAsync(ComandosApp.FigurasInserirRegiao);
            p["recorte_abriu"] = j.RecortePranchaAberto;
            p["ok"] = (bool)p["ok"]! && j.RecortePranchaAberto;
            if (j.JanelaDeRecorte is { Content: FrameworkElement recorte } janela)
            {
                await EsperarAteAsync(() => Task.FromResult(recorte.ActualWidth > 0 && janela.ProntaParaEscolher), 5000);
                await CapturarXamlAsync(recorte, Path.Combine(pasta, "29b-recorte-prancha.png"));
                var pdfPrancha = Environment.GetEnvironmentVariable("PMVNI_ROTEIRO_PRANCHA") ?? Path.Combine(pasta, "longo.pdf");
                perguntas.ArquivoParaAbrir = pdfPrancha;
                Invocar(janela.EscolherPdf);
                if (!await EsperarAteAsync(() => Task.FromResult(janela.PaginaPronta && perguntas.ArquivoParaAbrir is null), 15000))
                    throw new InvalidOperationException("O PDF de teste não abriu para recorte.");
                ((IInvokeProvider)new AppBarButtonAutomationPeer(janela.SelecionarPagina).GetPattern(PatternInterface.Invoke)).Invoke();
                if (!await EsperarAteAsync(() => Task.FromResult(janela.InserirRecorte.IsEnabled), 5000))
                    throw new InvalidOperationException("Selecionar a página não habilitou a inserção.");
                janela.SelecionarRegiao(.12, .18, .46, .54);
                Invocar(janela.InserirRecorte);
                var inserida = await EsperarAteAsync(() => Task.FromResult(j.EditorFiguraAberto && !j.RecortePranchaAberto), 30000);
                p["recorte_inserido"] = inserida;
                p["ok"] = (bool)p["ok"]! && inserida;
                if (inserida)
                {
                    var nova = s.Revisao.Projeto.Figuras!.Single(f => f.Incluida && f.Id.StartsWith("fig_", StringComparison.Ordinal) && f.Recorte?["pdf"]?.ToString() == Path.GetFileName(pdfPrancha)).Id;
                    var editorReal = j.EditorDeFigura!;
                    var idRegiao = await editorReal.ConferirMarcacoesAsync();
                    await CapturarXamlAsync((FrameworkElement)editorReal.Content, Path.Combine(pasta, "29b-prancha-marcada.png"));
                    j.FecharEditorFigura();
                    double[] caixaMovida = [.35, .25, .55, .45];
                    var copia = Path.Combine(pasta, "figura-conferida.rpmvni");
                    perguntas.ArquivoParaSalvar = copia;
                    await j.ExecutarComandoAsync(ComandosApp.ArquivoSalvarComo);
                    if (!await j.AbrirPacoteAsync(copia)) throw new InvalidOperationException("Falha ao reabrir o relatório com o recorte.");
                    var salva = j.Sessao!.Revisao.Projeto.Figuras!.Single(f => f.Id == nova);
                    using var pacoteConferido = System.IO.Compression.ZipFile.OpenRead(copia);
                    using var recurso = pacoteConferido.GetEntry(salva.Imagem)!.Open();
                    var cabecalhoPng = new byte[24];
                    recurso.ReadExactly(cabecalhoPng);
                    var larguraPng = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(cabecalhoPng.AsSpan(16, 4));
                    var alturaPng = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(cabecalhoPng.AsSpan(20, 4));
                    p["resolucao_real_200_dpi"] = larguraPng == (int)salva.Recorte!["largura_px"]! && alturaPng == (int)salva.Recorte["altura_px"]!;
                    var regiao = salva.Buscar(idRegiao);
                    var exportacao = await j.ExportarParaAsync(Path.Combine(pasta, "figura-conferida.pdf"));
                    p["erro_ao_exportar_recorte"] = exportacao.Item3;
                    var folhasComRecorte = await j.Documento!.TextoPorFolhaAsync();
                    var persistiu = regiao?.Caixa is { Length: 4 } caixa && caixa.Zip(caixaMovida).All(par => Math.Abs(par.First - par.Second) < .0001)
                        && salva.Buscar("seta_recorte")?.Elemento == "Segundo ponto" && regiao.Texto == "Legenda de conferência"
                        && salva.Buscar("texto_recorte")?.Texto == "Texto de conferência"
                        && folhasComRecorte.Any(t => t.Contains("Legenda de conferência", StringComparison.Ordinal))
                        && !j.Sessao.Atual!.Preparado.Render.Html.Contains("<span class=\"ids\">()</span>", StringComparison.Ordinal);
                    p["recorte_reaberto_com_texto_legenda_e_regiao_movida"] = persistiu;
                    p["ok"] = (bool)p["ok"]! && persistiu && exportacao.Item1 && (bool)p["resolucao_real_200_dpi"]!;
                    await j.AbrirPacoteAsync(pacoteLongo);
                    var marca = await JanelaAprovacao.ConferirNoRoteiroAsync(await File.ReadAllBytesAsync(pdfPrancha), pasta);
                    p["aprovacao_navegacao_zoom_carimbo_confirmado"] = marca;
                    p["ok"] = (bool)p["ok"]! && marca;
                }
            }
            j.FecharRecortePrancha();
            static int Ocorrencias(string texto, string trecho) => (texto.Length - texto.Replace(trecho, "", StringComparison.Ordinal).Length) / trecho.Length;
        });

        // ---------- Tarefa 13: módulos Processos e Despachos, pasta de processos nos Ajustes ----------
        // Numeração: o plano previa 28 (processos), 29 (despachos), 31, 32 e 33; 28 e 29 já são Recentes e Comparar (Tarefa 11) e o 30 fica
        // reservado à Legislação (Tarefa 18). Aqui: 31 processos, 32 despachos, 33 pasta nos Ajustes, 34 faixa e pesquisa por módulo, 35 pastas intactas.
        // Tudo sobre a CÓPIA da árvore fictícia (<pasta>\processos, feita no início); nenhuma abertura externa acontece (só é registrada).
        var processo0001 = Path.Combine(copiaProcessos, "2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)");
        // Correção 2026-11-16: a importação grava na pasta DESPACHO NN - AAAA-MM-DD sugerida pela regra do kit (a mesma conta aqui).
        var etapaImportacao = Pmvni.Revisao.Etapas.Sugerida(processo0001, j.Hoje()).Pasta;
        var pmvrel1 = Path.Combine(etapaImportacao, "Relatorio_0001_2099.rpmvni");
        var pmvrel2 = Path.Combine(etapaImportacao, "Relatorio_0001_2099_2.rpmvni");
        var nomesDosProcessos = new[]
        {
            "0012-2099 - BELTRANO DE EXEMPLO (FICTICIO)", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", "pasta fora do padrao", "0007 - 2098 - CICLANO DE EXEMPLO (FICTICIO)",
        };

        await Passo("31_processos", async p =>
        {
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var faixa = j.FaixaAtual;
            string[] Nomes() => j.ProcessosMostrados.Select(l => l.Processo.NomePasta).ToArray();
            string[] Detalhe() => j.DetalheMostrado.Select(l => l.Texto).ToArray();
            async Task<bool> EscolherProcessoAsync(string inicio)
            {
                await j.EscolherProcessoNaListaAsync(inicio);
                return await EsperarAteAsync(() => Task.FromResult(j.ProcessoEscolhido?.NomePasta.StartsWith(inicio, StringComparison.Ordinal) == true && j.DetalheMostrado.Count > 0), 5000);
            }

            // A lista: os 4 processos da árvore fictícia, na ordem da Tarefa 12; a pasta e a dica certas; a página de Processos à vista.
            var ordemOk = Nomes().SequenceEqual(nomesDosProcessos);
            p["processos_na_ordem"] = new JsonArray(Nomes().Select(n => (JsonNode)n).ToArray());
            p["contagem"] = j.TextoContagemProcessos;
            var listaOk = ordemOk && j.TextoContagemProcessos == "4 processos" && j.PaginaProcessosVisivel && !j.PaginaDespachosVisivel
                && j.ModuloAtual == Modulo.Processos && !j.AvisoPastaProcessos.IsOpen && j.PastaProcessosEfetiva == copiaProcessos;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "31-janela-xaml-processos.png"));

            // A pesquisa da barra de título filtra por número ou requerente (sem caixa nem acento): "fulano" → 1; vazio → 4.
            await j.DigitarPesquisaAsync("fulano");
            var fulano = Nomes();
            var textoFulano = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("FÚLANO");
            var acento = Nomes().Length == 1;
            await j.DigitarPesquisaAsync("12");
            var numero = Nomes().SequenceEqual([nomesDosProcessos[0]]);
            await j.DigitarPesquisaAsync("");
            p["filtro_fulano"] = new JsonArray(fulano.Select(n => (JsonNode)n).ToArray());
            p["texto_do_filtro"] = textoFulano;
            var filtroOk = fulano.Length == 1 && fulano[0].StartsWith("0001", StringComparison.Ordinal) && textoFulano == "1 de 4" && acento && numero
                && Nomes().Length == 4 && j.TextoResultadoPesquisa == "";

            // Filtro por ano (menu dinâmico, anos da pasta) e ordenar por requerente (✓ no menu); de volta ao padrão.
            faixa.EscolherAba("Página Inicial");
            await Esperar(250);
            var anos = faixa.AtualizarMenuDinamico(ComandosApp.ProcessosAno);
            var textosAno = faixa.TextosDoMenuDinamico(ComandosApp.ProcessosAno);
            await j.DefinirAnoAsync("2098");
            var so2098 = Nomes().SequenceEqual([nomesDosProcessos[3]]) && j.TextoContagemProcessos == "1 de 4 processos · 2098";
            faixa.AtualizarMenuDinamico(ComandosApp.ProcessosAno);
            var textosAno2 = faixa.TextosDoMenuDinamico(ComandosApp.ProcessosAno);
            await j.DefinirAnoAsync("");
            p["menu_ano"] = new JsonArray(textosAno.Select(t => (JsonNode)t).ToArray());
            var anoOk = anos.SequenceEqual(["", "2099", "2098"]) && textosAno[0].StartsWith('✓') && so2098 && textosAno2[2].StartsWith('✓') && Nomes().Length == 4;
            faixa.EscolherAba("Exibir");
            await Esperar(250);
            await j.ExecutarComandoAsync(ComandosApp.ProcessosOrdenarRequerente);
            var porRequerente = Nomes();
            var marcaRequerente = faixa.Marcado(ComandosApp.ProcessosOrdenarRequerente) && !faixa.Marcado(ComandosApp.ProcessosOrdenarNumero);
            await j.ExecutarComandoAsync(ComandosApp.ProcessosOrdenarNumero);
            p["por_requerente"] = new JsonArray(porRequerente.Select(n => (JsonNode)n).ToArray());
            var ordenarOk = porRequerente.SequenceEqual([nomesDosProcessos[0], nomesDosProcessos[3], nomesDosProcessos[1], nomesDosProcessos[2]])
                && marcaRequerente && Nomes().SequenceEqual(nomesDosProcessos) && j.OrdemDeProcessos == OrdemProcessos.Numero;
            faixa.EscolherAba("Página Inicial");
            await Esperar(250);

            // O detalhe do 0001: relatório (a análise da IA), despachos e, se pedido, PROJETO e DOCUMENTOS.
            var escolheu = await EscolherProcessoAsync("0001");
            var detalhe = Detalhe();
            p["detalhe_do_0001"] = new JsonArray(detalhe.Select(t => (JsonNode)t).ToArray());
            var detalheOk = escolheu && detalhe.Take(3).SequenceEqual(["Relatórios (1)", "dados_0001.json", "Despachos (2)"])
                && detalhe.Contains("PROJETO (2)")
                && j.DetalheMostrado.Count(l => l.Tipo == TipoLinhaDetalhe.ArquivoDeDespacho) == 3;
            // PROJETO e DOCUMENTOS aparecem sempre (2026-11-16: o botão "PROJETO e DOCS" saiu).
            var comPastas = Detalhe();
            var pastasOk = comPastas.Contains("PROJETO (2)") && comPastas.Contains("DOCUMENTOS (1)");

            // Abrir arquivo: um PDF do PROJETO é só registrado (modo roteiro); um script (.cmd) é recusado com a mensagem exata e nada é registrado.
            AberturaExterna.Registradas.Clear();
            var escolheuPdf = j.EscolherEntradaNaLista(l => l.Texto == "ARQ 01-03.pdf");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirArquivo);
            var pdfRegistrado = AberturaExterna.Registradas.SequenceEqual([Path.Combine(processo0001, "PROJETO", "ARQ 01-03.pdf")]);
            var escolheuScript = j.EscolherEntradaNaLista(l => l.Texto == "script.cmd");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirArquivo);
            var scriptRecusado = AberturaExterna.Registradas.Count == 1 && j.MensagemDeProcessos.IsOpen && j.MensagemDeProcessos.Title == "O arquivo não foi aberto"
                && j.MensagemDeProcessos.Message == "Este tipo de arquivo não é aberto pelo editor (.cmd). Use Mostrar no Explorador.";
            p["mensagem_do_script"] = j.MensagemDeProcessos.Message;
            // Abrir pasta no Explorador: só registra (modo roteiro).
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirPasta);
            var pastaRegistrada = AberturaExterna.Registradas.Count == 2 && AberturaExterna.Registradas[1] == "explorador: " + processo0001;
            p["registradas"] = new JsonArray(AberturaExterna.Registradas.Select(t => (JsonNode)t).ToArray());
            var abrirOk = escolheuPdf && pdfRegistrado && escolheuScript && scriptRecusado && pastaRegistrada;

            // Importar análise da IA do 0001: cria Relatorio_0001_2099.rpmvni na pasta do processo e abre no módulo Relatórios com 3 itens.
            var escolheuAnalise = j.EscolherEntradaNaLista(l => l.Tipo == TipoLinhaDetalhe.Analise);
            await j.ExecutarComandoAsync(ComandosApp.ProcessosImportarAnalise);
            var primeira = File.Exists(pmvrel1) && j.ModuloAtual == Modulo.Relatorios && j.Sessao is not null
                && string.Equals(j.Sessao.Caminho, pmvrel1, StringComparison.OrdinalIgnoreCase) && j.Sessao.Revisao.Projeto.Incluidos.Count == 3;
            p["primeira_importacao_itens"] = j.Sessao?.Revisao.Projeto.Incluidos.Count;
            var titulo1 = j.TextoTituloAtual;
            var sha1 = File.Exists(pmvrel1) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pmvrel1))) : "";

            // Repetir: nunca sobrescreve (o nome livre seguinte, _2) e o primeiro fica como estava.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var escolheuDeNovo = j.EscolherEntradaNaLista(l => l.Tipo == TipoLinhaDetalhe.Analise);
            var apareceu = Detalhe().Contains("Relatorio_0001_2099.rpmvni");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosImportarAnalise);
            var segunda = File.Exists(pmvrel2) && string.Equals(j.Sessao?.Caminho, pmvrel2, StringComparison.OrdinalIgnoreCase) && j.ModuloAtual == Modulo.Relatorios
                && j.Sessao!.Revisao.Projeto.Incluidos.Count == 3;
            var primeiraIntacta = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pmvrel1))) == sha1;
            var criados = Pmvrels(processo0001).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
            p["arquivos_criados"] = new JsonArray(criados.Select(n => (JsonNode)n).ToArray());

            // Abrir relatório: o .rpmvni listado abre no módulo Relatórios.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var escolheuPacote = j.EscolherEntradaNaLista(l => l.Tipo == TipoLinhaDetalhe.Relatorio && l.Texto == "Relatorio_0001_2099.rpmvni");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirRelatorio);
            var abriu = escolheuPacote && string.Equals(j.Sessao?.Caminho, pmvrel1, StringComparison.OrdinalIgnoreCase) && j.ModuloAtual == Modulo.Relatorios;
            p["titulo_da_janela"] = titulo1;
            var importarOk = escolheuAnalise && primeira && titulo1.Length > 0 && escolheuDeNovo && apareceu && segunda && primeiraIntacta && abriu;
            p["conferencias"] = new JsonObject
            {
                ["lista"] = listaOk, ["filtro"] = filtroOk, ["ano"] = anoOk, ["ordenar"] = ordenarOk, ["detalhe"] = detalheOk && pastasOk, ["abrir_arquivo_e_pasta"] = abrirOk,
                ["primeira_importacao"] = primeira, ["segunda_importacao_nome_livre"] = segunda, ["primeira_intacta"] = primeiraIntacta, ["abrir_relatorio"] = abriu,
            };
            p["ok"] = listaOk && filtroOk && anoOk && ordenarOk && detalheOk && pastasOk && abrirOk && importarOk;
        });

        await Passo("32_despachos", async p =>
        {
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            var faixa = j.FaixaAtual;
            List<LinhaDespacho> Despachos() => j.DespachosMostrados.Where(i => i.EhDespacho).Select(i => i.Linha!).ToList();
            string[] Datas() => Despachos().Select(d => d.Despacho.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
            var esperadas = new[] { "2099-03-05", "2099-02-20", "2099-01-15", "2098-03-02" };
            p["datas_de_todos"] = new JsonArray(Datas().Select(d => (JsonNode)d).ToArray());
            p["contagem"] = j.TextoContagemDespachos;
            var todosOk = Datas().SequenceEqual(esperadas) && j.TextoContagemDespachos == "4 despachos" && j.PaginaDespachosVisivel && !j.PaginaProcessosVisivel
                && j.DicaDaPesquisa == "Pesquisar despachos" && j.CaixaPesquisaHabilitada && !j.AvisoPastaDespachos.IsOpen;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "32-janela-xaml-despachos.png"));

            // Filtro por tipo: Aprovação → 2 (os dois de aprovação); Correção → 1; Todos → 4.
            await j.ExecutarComandoAsync(ComandosApp.DespachosFiltroAprovacao);
            var aprovacao = Datas();
            var marcaAprovacao = faixa.Marcado(ComandosApp.DespachosFiltroAprovacao) && !faixa.Marcado(ComandosApp.DespachosFiltroTodos);
            await j.ExecutarComandoAsync(ComandosApp.DespachosFiltroCorrecao);
            var correcao = Datas();
            await j.ExecutarComandoAsync(ComandosApp.DespachosFiltroTodos);
            p["aprovacao"] = new JsonArray(aprovacao.Select(d => (JsonNode)d).ToArray());
            var filtroOk = aprovacao.SequenceEqual(["2099-02-20", "2098-03-02"]) && marcaAprovacao && correcao.SequenceEqual(["2099-01-15"]) && Datas().Length == 4;

            // A pesquisa da barra de título é pelo nome do arquivo, sem caixa nem acento.
            await j.DigitarPesquisaAsync("CORREÇÃO");
            var porNome = Datas();
            var textoNome = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("");
            var pesquisaOk = porNome.SequenceEqual(["2099-01-15"]) && textoNome == "1 de 4" && Datas().Length == 4;

            // Abrir PDF do despacho de aprovação do 0001: só registra (modo roteiro). Abrir prefere o DOCX; o do 0012 só tem DOCX (sem Abrir PDF).
            AberturaExterna.Registradas.Clear();
            var aprovacao0001 = Path.Combine(processo0001, "DESPACHO 02 - 2099-02-20", "DESPACHO_APROVACAO_0001_2099.pdf");
            var escolheuAprovacao = j.EscolherDespachoNaLista(l => l.Despacho.Tipo == TipoDespacho.Aprovacao && l.Despacho.Processo?.Numero == 1);
            var pdfHabilitado = faixa.Habilitado(ComandosApp.DespachosAbrirPdf);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAbrirPdf);
            var pdfRegistrado = AberturaExterna.Registradas.SequenceEqual([aprovacao0001]);
            var escolheuCorrecao = j.EscolherDespachoNaLista(l => l.Despacho.Tipo == TipoDespacho.Correcao);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAbrir);
            var docxRegistrado = AberturaExterna.Registradas.Count == 2
                && AberturaExterna.Registradas[1] == Path.Combine(processo0001, "DESPACHO 01 - 2099-01-15", "DESPACHO_CORRECAO_0001_2099.docx");
            var escolheuSoDocx = j.EscolherDespachoNaLista(l => l.Despacho.Processo?.Numero == 12);
            var semPdf = !faixa.Habilitado(ComandosApp.DespachosAbrirPdf) && faixa.Habilitado(ComandosApp.DespachosAbrir);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAbrirPdf);
            var recusouSemPdf = AberturaExterna.Registradas.Count == 2 && j.MensagemDeDespachos.IsOpen
                && j.MensagemDeDespachos.Message == "«Abrir PDF» não está disponível agora: o despacho escolhido não tem PDF.";
            p["registradas"] = new JsonArray(AberturaExterna.Registradas.Select(t => (JsonNode)t).ToArray());

            // A abertura pela AberturaExterna recusa o atalho .lnk com a mensagem exata e nada é registrado.
            var atalho = Path.Combine(copiaProcessos, "MODELOS", "atalho.lnk");
            var abriuAtalho = AberturaExterna.Abrir(atalho, out var motivoAtalho);
            p["motivo_do_atalho"] = motivoAtalho;
            var atalhoOk = !abriuAtalho && motivoAtalho == "Este tipo de arquivo não é aberto pelo editor (.lnk). Use Mostrar no Explorador." && AberturaExterna.Registradas.Count == 2;
            await j.ExecutarComandoAsync(ComandosApp.DespachosMostrar);
            var mostrarOk = AberturaExterna.Registradas.Count == 3 && AberturaExterna.Registradas[2].StartsWith("explorador: ", StringComparison.Ordinal);
            var abrirOk = escolheuAprovacao && pdfHabilitado && pdfRegistrado && escolheuCorrecao && docxRegistrado && escolheuSoDocx && semPdf && recusouSemPdf && atalhoOk && mostrarOk;

            // Agrupar por processo (um título por processo) e ordenar pelos mais antigos; de volta a "por data" e "mais recentes".
            await j.ExecutarComandoAsync(ComandosApp.DespachosAgruparProcesso);
            var titulosGrupo = j.DespachosMostrados.Where(i => i.EhGrupo).Select(i => i.Titulo).ToArray();
            await j.ExecutarComandoAsync(ComandosApp.DespachosOrdenarAntigos);
            var antigos = Datas();
            var marcaOrdem = faixa.Marcado(ComandosApp.DespachosOrdenarAntigos) && !faixa.Marcado(ComandosApp.DespachosOrdenarRecentes) && faixa.Marcado(ComandosApp.DespachosAgruparProcesso);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAgruparData);
            await j.ExecutarComandoAsync(ComandosApp.DespachosOrdenarRecentes);
            p["grupos_por_processo"] = new JsonArray(titulosGrupo.Select(t => (JsonNode)t).ToArray());
            var agruparOk = titulosGrupo.Length == 3 && titulosGrupo[0].StartsWith("0012/2099", StringComparison.Ordinal) && marcaOrdem
                && antigos.Length == 4 && Datas().SequenceEqual(esperadas);

            // Ir ao processo do despacho escolhido (módulo Processos, processo escolhido, detalhe lido) e "só do processo escolhido" de volta aqui.
            var escolheuDoUm = j.EscolherDespachoNaLista(l => l.Despacho.Processo?.Numero == 1 && l.Despacho.Tipo == TipoDespacho.Correcao);
            await j.ExecutarComandoAsync(ComandosApp.DespachosIrAoProcesso);
            await j.AguardarLeiturasAsync();
            var foiAoProcesso = escolheuDoUm && j.ModuloAtual == Modulo.Processos && j.ProcessoEscolhido?.NomePasta.StartsWith("0001", StringComparison.Ordinal) == true
                && j.DetalheMostrado.Any(l => l.Texto == "dados_0001.json");
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            await j.ExecutarComandoAsync(ComandosApp.DespachosDoProcesso);
            var soDoUm = Datas();
            var ligado = faixa.Ligado(ComandosApp.DespachosDoProcesso);
            var textoDoUm = j.TextoContagemDespachos;
            await j.ExecutarComandoAsync(ComandosApp.DespachosDoProcesso);
            p["so_do_processo"] = new JsonArray(soDoUm.Select(d => (JsonNode)d).ToArray());
            p["contagem_do_processo"] = textoDoUm;
            var escopoOk = foiAoProcesso && soDoUm.SequenceEqual(["2099-02-20", "2099-01-15"]) && ligado && textoDoUm == "2 despachos · só do processo 0001/2099" && Datas().Length == 4;

            p["conferencias"] = new JsonObject
            {
                ["todos_por_data"] = todosOk, ["filtro_por_tipo"] = filtroOk, ["pesquisa_por_nome"] = pesquisaOk, ["abrir"] = abrirOk, ["agrupar_e_ordenar"] = agruparOk,
                ["ir_ao_processo_e_so_do_processo"] = escopoOk,
            };
            p["ok"] = todosOk && filtroOk && pesquisaOk && abrirOk && agruparOk && escopoOk;
        });

        await Passo("33_ajustes_pastas", async p =>
        {
            // Pasta inexistente: a mensagem exata com o botão "Abrir Ajustes", nos dois módulos (nada da pasta real é lido).
            var inexistente = Path.Combine(pasta, "nao-existe-processos");
            if (Directory.Exists(inexistente)) ApagarArvore(inexistente);
            j.DefinirPastaProcessos(inexistente);
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var mensagem = $"A pasta de processos não foi encontrada: {inexistente}. Escolha a pasta em Ajustes.";
            p["mensagem"] = j.AvisoPastaProcessos.Message;
            var botao = j.BotaoAbrirAjustesProcessos;
            var processosOk = j.AvisoPastaProcessos.IsOpen && j.AvisoPastaProcessos.Message == mensagem && botao.Content as string == "Abrir Ajustes"
                && j.ProcessosMostrados.Count == 0 && j.TextoContagemProcessos == "";
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            var despachosOk = j.AvisoPastaDespachos.IsOpen && j.AvisoPastaDespachos.Message == mensagem && j.DespachosMostrados.Count == 0;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "33-janela-xaml-pasta-ausente.png"));

            // O botão leva à página Ajustes, que mostra a pasta em uso e avisa que ela não existe.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            ((IInvokeProvider)new ButtonAutomationPeer(j.BotaoAbrirAjustesProcessos).GetPattern(PatternInterface.Invoke)).Invoke();
            var foiAosAjustes = await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == Modulo.Ajustes && j.PaginaAjustesVisivel), 5000);
            await j.AguardarLeiturasAsync();
            var textoPasta = j.TextoPastaNosAjustes.Text;
            var avisoPasta = j.TextoAvisoPastaNosAjustes.Text;
            p["pagina_ajustes"] = new JsonObject { ["pasta"] = textoPasta, ["aviso"] = avisoPasta };
            // O botão abre direto Ajustes › Pastas e kit (auditoria §3.2).
            p["categoria_aberta"] = j.AjustesPagina.CategoriaAtual;
            var ajustesOk = foiAosAjustes && textoPasta == inexistente && avisoPasta.StartsWith("A pasta não foi encontrada", StringComparison.Ordinal)
                && j.AjustesPagina.CategoriaAtual == CategoriasAjustes.Pastas;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "33-janela-xaml-ajustes-pasta.png"));

            // Escolher a pasta (seletor com resposta fixa) pelo botão: vale na hora, fica em ajustes.json e o aviso some.
            perguntas.PastaEscolhida = copiaProcessos;
            ((IInvokeProvider)new ButtonAutomationPeer(j.AjustesPagina.BotaoEscolherPasta).GetPattern(PatternInterface.Invoke)).Invoke();
            var escolheu = await EsperarAteAsync(() => Task.FromResult(j.PastaProcessosEscolhida == copiaProcessos), 5000);
            await j.AguardarLeiturasAsync();
            var guardada = JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["pasta_processos"]?.GetValue<string>();
            var escolhaOk = escolheu && guardada == copiaProcessos && j.TextoPastaNosAjustes.Text == copiaProcessos
                && j.TextoAvisoPastaNosAjustes.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed && perguntas.Feitas.Contains("escolher pasta");
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var voltou = j.ProcessosMostrados.Count == 4 && !j.AvisoPastaProcessos.IsOpen;
            p["conferencias"] = new JsonObject
            {
                ["mensagem_em_processos"] = processosOk, ["mensagem_em_despachos"] = despachosOk, ["botao_leva_aos_ajustes"] = ajustesOk,
                ["escolher_pasta"] = escolhaOk, ["lista_volta"] = voltou,
            };
            p["ok"] = processosOk && despachosOk && ajustesOk && escolhaOk && voltou;
        });

        await Passo("34_faixa_e_pesquisa_por_modulo", async p =>
        {
            // O relatório fica aberto atrás (com um item selecionado) enquanto se visita cada módulo.
            await j.MudarModuloAsync(Modulo.Relatorios);
            if (j.Sessao is null && !await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não abriu o longo: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            await j.SelecionarAsync(s.Revisao.Projeto.Incluidos[0].Id);
            string? Guardado(string chave) => JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?[chave]?.GetValue<string>();
            string? AbaGuardada(Modulo m) => JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["aba_por_modulo"]?[m.ToString()]?.GetValue<string>();
            var dicas = new Dictionary<Modulo, string>
            {
                [Modulo.Relatorios] = "Pesquisar no relatório", [Modulo.Despachos] = "Pesquisar despachos", [Modulo.Processos] = "Pesquisar processos",
                [Modulo.Acompanhamento] = "Pesquisar na planilha",
                [Modulo.Legislacao] = "Pesquisar em todas as leis", // nenhuma lei aberta (Tarefa 18)
            };
            var linhas = new JsonArray();
            var tudoOk = true;
            foreach (var m in Faixas.Disponiveis)
            {
                await j.MudarModuloAsync(m);
                await j.AguardarLeiturasAsync();
                var def = Faixas.Para(m);
                var abas = j.FaixaAtual.Definicao!.Abas.Select(a => a.Nome).ToArray();
                var abasOk = j.FaixaAtual.Definicao.Modulo == m && abas.SequenceEqual(def.Abas.Select(a => a.Nome));
                var dicaOk = !dicas.TryGetValue(m, out var dicaEsperada) ? !j.CaixaPesquisaHabilitada : j.DicaDaPesquisa == dicaEsperada && j.CaixaPesquisaHabilitada;
                var semManipulador = j.ComandosSemManipulador();
                var abaAberta = j.FaixaAtual.AbaAtual;
                var padraoOk = abaAberta == (AbaGuardada(m) ?? def.Abas[def.AbaPadrao].Nome); // a lembrada, ou a padrão (a segunda; em Ajustes, a primeira)
                // Auditoria §1.5: o primário de cada módulo (Ajustes sem primário) e o trilho marcando o módulo.
                var primarioOk = j.FaixaAtual.IdPrimario == def.Primario?.Id && j.TrilhoAtual.Ativo == m && j.TrilhoAtual.EstadoAcessivel(m) == "Selecionado";
                var ok = abasOk && dicaOk && semManipulador.Count == 0 && padraoOk && j.ModuloAtual == m && primarioOk;
                tudoOk &= ok;
                linhas.Add(new JsonObject
                {
                    ["modulo"] = m.ToString(), ["primario"] = j.FaixaAtual.IdPrimario, ["rotulo_do_primario"] = j.FaixaAtual.RotuloPrimario, ["abas"] = new JsonArray(abas.Select(a => (JsonNode)a).ToArray()), ["aba_aberta"] = abaAberta, ["dica_da_pesquisa"] = j.DicaDaPesquisa,
                    ["caixa_habilitada"] = j.CaixaPesquisaHabilitada, ["comandos_sem_manipulador"] = new JsonArray(semManipulador.Select(c => (JsonNode)c).ToArray()), ["ok"] = ok,
                });
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, $"34-janela-xaml-{m.ToString().ToLowerInvariant()}.png"));
            }
            p["modulos"] = linhas;


            // Módulo e aba lembrados: Exibir em Processos; sair e voltar mantém a aba; ajustes.json guarda a aba e o módulo.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            j.FaixaAtual.EscolherAba("Exibir");
            await Esperar(250);
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.MudarModuloAsync(Modulo.Processos);
            var abaLembrada = j.FaixaAtual.AbaAtual == "Exibir" && AbaGuardada(Modulo.Processos) == "Exibir" && Guardado("modulo") == "Processos";
            j.FaixaAtual.EscolherAba("Página Inicial");
            await Esperar(250);

            // Cada módulo guarda o seu termo de pesquisa: "fulano" em Processos some em Despachos e volta ao voltar.
            await j.DigitarPesquisaAsync("fulano");
            await j.MudarModuloAsync(Modulo.Despachos);
            var vazioEmDespachos = j.TermoPesquisa == "" && j.DicaDaPesquisa == "Pesquisar despachos";
            await j.MudarModuloAsync(Modulo.Processos);
            var voltouTermo = j.TermoPesquisa == "fulano" && j.ProcessosMostrados.Count == 1;
            await j.DigitarPesquisaAsync("");
            var termosOk = vazioEmDespachos && voltouTermo;

            // Com Processos à vista, os comandos do relatório (Excluir, Desfazer) não mexem no relatório escondido.
            var historicoAntes = s.Revisao.HistoricoCompleto.Count;
            var incluidosAntes = s.Revisao.Projeto.Incluidos.Count;
            await j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var escondidoIntacto = s.Revisao.HistoricoCompleto.Count == historicoAntes && s.Revisao.Projeto.Incluidos.Count == incluidosAntes
                && !j.InfoMensagemAtual.IsOpen && j.ModuloAtual == Modulo.Processos;

            // A pesquisa de Relatórios volta como estava: o termo digitado nas folhas é lembrado ao sair e voltar.
            await j.MudarModuloAsync(Modulo.Relatorios);
            await j.DigitarPesquisaAsync("verificacao");
            var total = j.PesquisaTotal;
            await j.MudarModuloAsync(Modulo.Processos);
            await j.MudarModuloAsync(Modulo.Relatorios);
            var pesquisaVolta = total > 0 && j.TermoPesquisa == "verificacao" && j.TextoResultadoPesquisa == $"1 de {total}";
            await j.DigitarPesquisaAsync("");
            p["conferencias"] = new JsonObject
            {
                ["faixas_e_dicas"] = tudoOk, ["aba_lembrada"] = abaLembrada, ["termo_por_modulo"] = termosOk, ["relatorio_escondido_intacto"] = escondidoIntacto,
                ["pesquisa_do_relatorio_volta"] = pesquisaVolta,
            };
            p["ok"] = tudoOk && abaLembrada && termosOk && escondidoIntacto && pesquisaVolta;

        });

        await Passo("35_pastas_intactas", async p =>
        {
            // Fecha o relatório (solta a trava) e compara a árvore com a de antes de tudo: só os .rpmvni das importações são novos.
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            var depois = FotografarArvore(copiaProcessos);
            var novos = depois.Keys.Except(antesDasPastas.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            var sumiram = antesDasPastas.Keys.Except(depois.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var mudaram = antesDasPastas.Where(kv => depois.TryGetValue(kv.Key, out var sha) && sha != kv.Value).Select(kv => kv.Key).ToList();
            p["arquivos_antes"] = antesDasPastas.Count;
            p["arquivos_depois"] = depois.Count;
            p["novos"] = new JsonArray(novos.Select(n => (JsonNode)n).ToArray());
            p["sumiram"] = new JsonArray(sumiram.Select(n => (JsonNode)n).ToArray());
            p["mudaram"] = new JsonArray(mudaram.Select(n => (JsonNode)n).ToArray());
            var esperadosNovos = new[]
            {
                Path.GetRelativePath(copiaProcessos, pmvrel1),
                Path.GetRelativePath(copiaProcessos, pmvrel2),
            };
            // Nenhum .lock, .bak ou .tmp ficou ao lado dos pacotes depois de fechar.
            var restos = depois.Keys.Where(k => k.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)).ToList();
            p["restos"] = new JsonArray(restos.Select(n => (JsonNode)n).ToArray());
            p["ok"] = novos.SequenceEqual(esperadosNovos.Order(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase) && sumiram.Count == 0 && mudaram.Count == 0 && restos.Count == 0;
        });

        // ---------- Tarefa 16: Novo despacho (tipo, prévia editável, conferência das páginas, gravar e abrir) ----------
        // Numeração: o plano previa 34 a 38, mas 34 e 35 já são da Tarefa 13. Aqui: 36 correção (pela faixa de Relatórios), 37 aprovação
        // (pela faixa de Processos), 38 sem LibreOffice e depois Gerar PDF (pela faixa de Despachos), 39 não sobrescreve, 40 bloqueios.
        // Tudo numa CÓPIA separada da árvore fictícia (<pasta>\processos-despacho); o kit real é só lido no lugar (pasta do kit nos Ajustes);
        // o PDF sai do LibreOffice real (este roteiro roda fora do pacote do Claude, aberto pelo explorer.exe), com perfil e TEMP na pasta de
        // trabalho do app (tests\saida\dados-app\despachos). A data de hoje é fixa (2099-03-10). Nenhum arquivo é aberto fora (só registrado).
        var copiaDespacho = Path.Combine(pasta, "processos-despacho");
        var p0001 = Path.Combine(copiaDespacho, "2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)");
        var pmvrelD = Path.Combine(p0001, "Relatorio_0001_2099.rpmvni");
        var pmvrelD2 = Path.Combine(p0001, "Relatorio_0001_2099_2.rpmvni");
        var etapa03 = Path.Combine(p0001, "DESPACHO 03 - 2099-03-10");
        var baseCorrecao = "Despacho_Correcao_1_2099_Fulano_De_Exemplo_Ficticio";
        var baseAprovacao = "Despacho_Aprovacao_1_2099_Fulano_De_Exemplo_Ficticio";
        var kitEnv = Environment.GetEnvironmentVariable("PMVNI_KIT_RAIZ");
        var kitReal = string.IsNullOrWhiteSpace(kitEnv) ? Ajustes.PastaKitPadrao : kitEnv;
        var antesDoDespacho = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] kitAntes = [];
        var gravadosNaCopia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Onda 1, item 11: o conversor real medido (cada conversão do LibreOffice, em ms), para a 1ª e a 2ª com o perfil persistente.
        var conversorReal = new ConversorMedido(j.Conversor);
        var preparadoDespacho = false;
        bool Mesmo(string? a, string? b) => a is not null && b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        string Sha(string arquivo) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(arquivo)));
        void Anotar(Entrega e) { foreach (var a in new[] { e.Docx, e.Pdf, e.Recibo }) if (a is not null) gravadosNaCopia.Add(Path.GetRelativePath(copiaDespacho, a)); }
        void Invocar(Microsoft.UI.Xaml.Controls.Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
        void Exigir() { if (!preparadoDespacho) throw new InvalidOperationException("a preparação do passo 36 não terminou"); }
        // Marca todas as páginas (botão "Marcar todas") e espera "Gravar" habilitar.
        async Task<bool> MarcarTodasAsync()
        {
            Invocar(j.NovoDespacho.BotaoMarcarTodas);
            return await EsperarAteAsync(() => Task.FromResult(j.NovoDespacho.BotaoGravar.IsEnabled), 5000);
        }

        await Passo("36_novo_despacho_correcao", async p =>
        {
            // Preparação comum (vale para 36–40): a cópia da árvore, o kit real nos Ajustes, hoje fixo e o curto do 0001 importado.
            Environment.SetEnvironmentVariable("PMVNI_KIT_RAIZ", null); // o kit vem só dos Ajustes (o passo 40 troca a pasta)
            // O perfil do LibreOffice (<dados>\lo) começa do zero nesta rodada: a 1ª conversão mede a inicialização dele (passo 43).
            var perfilLo = Path.Combine(PastaDados.Raiz, "lo");
            if (Directory.Exists(perfilLo)) ApagarArvore(perfilLo);
            if (Directory.Exists(copiaDespacho)) ApagarArvore(copiaDespacho);
            CopiarArvore(Path.Combine(corpus, "processos"), copiaDespacho);
            foreach (var (k, v) in FotografarArvore(copiaDespacho)) antesDoDespacho[k] = v;
            kitAntes = RetratoDoKit(kitReal);
            j.DefinirPastaKit(kitReal);
            j.DefinirPastaProcessos(copiaDespacho);
            j.Hoje = () => new DateOnly(2099, 3, 10);
            j.Conversor = conversorReal;
            var json = Path.Combine(pasta, "curto-0001.json");
            var no = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(corpus, "curto.json")))!;
            no["capa"]!["processo"] = "0001/2099";
            no["capa"]!["requerente"] = "FULANO DE EXEMPLO (FICTICIO)";
            await File.WriteAllTextAsync(json, no.ToJsonString());
            if (!await j.ImportarParaAsync(json, pmvrelD)) throw new InvalidOperationException("não importou o curto-0001: " + j.InfoMensagem.Message);
            await j.AguardarNovoDespachoAsync();
            preparadoDespacho = true;
            p["libreoffice"] = conversorReal.Versao;
            p["kit"] = kitReal;

            // Pela faixa de Relatórios, com o pacote aberto: a página abre em Despachos com tudo sugerido.
            await j.MudarModuloAsync(Modulo.Relatorios);
            j.FaixaAtual.EscolherAba("Página Inicial");
            await Esperar(250);
            var habilitado = j.FaixaAtual.Habilitado(ComandosApp.DespachosNovoCorrecao) && j.FaixaAtual.Habilitado(ComandosApp.DespachosNovoAprovacao);
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var s = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página Novo despacho não abriu: " + j.InfoMensagemAtual.Message);
            var origemOk = j.ModuloAtual == Modulo.Despachos && j.PaginaNovoDespachoVisivel && !j.PaginaDespachosVisivel && s.Passo == PassoNovoDespacho.Conteudo
                && s.Processo?.Numero == 1 && Mesmo(s.CaminhoPacote, pmvrelD) && s.Etapa is { Nome: "DESPACHO 03 - 2099-03-10", Nova: true }
                && j.FaixaAtual.Habilitado(ComandosApp.DespachosNovoAprovacao);
            var exigencias = s.Rascunho!.Pendencias.Select(x => "ITEM " + x.Numero.ToString("D2", CultureInfo.InvariantCulture)).ToArray();
            p["exigencias"] = new JsonArray(exigencias.Select(x => (JsonNode)x).ToArray());
            var previa = j.NovoDespacho.TextoDaPrevia;
            var previaOk = previa.Contains("Protocolo nº: 0001/2099", StringComparison.Ordinal) && j.NovoDespacho.BotaoGerar.IsEnabled;
            var exigenciasOk = exigencias.SequenceEqual(["ITEM 02", "ITEM 03"]);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "36-janela-xaml-novo-despacho.png"));

            // Gerar e conferir com o LibreOffice real: as páginas aparecem; nada ainda na pasta do processo.
            await j.GerarEConferirAsync();
            var prep = s.Preparacao;
            string? leitor = null;
            try { if (prep?.Pdf is { } pdf) leitor = InfoPdf.Ler(pdf).Paginas.ToString(CultureInfo.InvariantCulture); }
            catch (Exception ex) { leitor = "ERRO: " + ex.Message; }
            p["preparacao"] = new JsonObject
            {
                ["paginas"] = prep?.Paginas, ["fontes"] = new JsonArray((prep?.Fontes ?? []).Select(f => (JsonNode)f).ToArray()), ["infopdf"] = leitor,
                ["sem_pdf_motivo"] = prep?.SemPdfMotivo, ["mensagem"] = prep?.MensagemSemPdf, ["erros"] = new JsonArray((prep?.Erros ?? []).Select(f => (JsonNode)f).ToArray()),
                ["texto_fontes"] = j.NovoDespacho.TextoFontes.Text,
            };
            var pdfOk = prep is { Pdf: not null, Erros.Count: 0 } && prep.Paginas >= 1 && prep.Fontes.Any(f => f.StartsWith("Verdana", StringComparison.Ordinal))
                && leitor == prep.Paginas?.ToString(CultureInfo.InvariantCulture) && j.NovoDespacho.CaixasDasPaginas.Count == prep.Paginas
                && j.NovoDespacho.TextoFontes.Text.StartsWith("Fontes do PDF: Verdana", StringComparison.Ordinal);
            var nadaAntes = !Directory.Exists(etapa03) && !j.NovoDespacho.BotaoGravar.IsEnabled;
            var marcou = await MarcarTodasAsync();
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "36-janela-xaml-conferencia.png"));

            // Gravar: os 3 arquivos na pasta nova, validados como o kit; o recibo confere; a lista volta com o novo escolhido.
            var e = await j.GravarDespachoAsync() ?? throw new InvalidOperationException("não gravou: " + j.InfoMensagemAtual.Message);
            Anotar(e);
            var modelo = j.ModelosDoKit!.Modelos.Single(m => m.Tipo == TipoDespacho.Correcao);
            var comparar = ValidacaoDespacho.Comparar(await File.ReadAllBytesAsync(modelo.Caminho), await File.ReadAllBytesAsync(e.Docx));
            var recibo = ReciboDespacho.Ler(e.Recibo);
            var arquivosOk = Mesmo(e.Pasta, etapa03) && Mesmo(e.Docx, Path.Combine(etapa03, baseCorrecao + ".docx"))
                && Mesmo(e.Pdf, Path.Combine(etapa03, baseCorrecao + ".pdf")) && Mesmo(e.Recibo, Path.Combine(etapa03, baseCorrecao + ".recibo-editor.json"))
                && Directory.GetFiles(etapa03).Length == 3 && comparar.Count == 0
                && string.Equals(recibo["docx"]?["sha256"]?.GetValue<string>(), Sha(e.Docx), StringComparison.OrdinalIgnoreCase)
                && string.Equals(recibo["pdf"]?["sha256"]?.GetValue<string>(), Sha(e.Pdf!), StringComparison.OrdinalIgnoreCase)
                && recibo["sem_pdf_motivo"] is null && recibo["paginas_conferidas"]?["paginas"]?.AsArray().Count == prep!.Paginas;
            p["arquivos"] = new JsonArray(Directory.GetFiles(etapa03).Select(f => (JsonNode)Path.GetFileName(f)).ToArray());
            p["validacao"] = new JsonArray(comparar.Select(c => (JsonNode)c).ToArray());
            var aviso = j.AvisoDespachoGravado;
            p["aviso"] = aviso.Message;
            var escolhido = j.DespachoEscolhido?.Despacho;
            var listaOk = j.PaginaDespachosVisivel && !j.PaginaNovoDespachoVisivel && aviso.IsOpen && aviso.Title == "Despacho gravado"
                && aviso.Message == $"3 arquivos em {etapa03}. Recibo: {baseCorrecao}.recibo-editor.json."
                && escolhido is { Tipo: TipoDespacho.Correcao, Numero: 3 } && escolhido.Data == new DateOnly(2099, 3, 10) && Mesmo(escolhido.Pasta, etapa03);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "36-janela-xaml-despacho-gravado.png"));
            AberturaExterna.Registradas.Clear();
            Invocar(j.BotaoAbrirPdfGravado);
            var abriuPdf = await EsperarAteAsync(() => Task.FromResult(AberturaExterna.Registradas.Contains(e.Pdf!)), 5000);
            p["conferencias"] = new JsonObject
            {
                ["menu_habilitado"] = habilitado, ["origem_sugerida"] = origemOk, ["duas_exigencias"] = exigenciasOk, ["previa"] = previaOk, ["pdf_do_libreoffice"] = pdfOk,
                ["nada_antes_de_gravar"] = nadaAntes, ["marcar_todas"] = marcou, ["tres_arquivos_validados"] = arquivosOk, ["lista_com_o_novo"] = listaOk, ["abrir_pdf"] = abriuPdf,
            };
            p["ok"] = habilitado && origemOk && exigenciasOk && previaOk && pdfOk && nadaAntes && marcou && arquivosOk && listaOk && abriuPdf;
        });

        await Passo("36b_correcao_sem_ia", async p =>
        {
            // Tarefa 15: o Novo despacho de correção com relatório monta as exigências em código (ResumoExigencias), sem IA, e diz isso;
            // a caixa de instrução ("Pedir à IA") continua à mão, mas nada é pedido sozinho.
            Exigir();
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            j.FaixaAtual.EscolherAba("Página Inicial");
            await Esperar(250);
            int iniciadosAntes;
            lock (programasIniciados) iniciadosAntes = programasIniciados.Count;
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var s = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
            var r = s.Rascunho ?? throw new InvalidOperationException("o rascunho não foi montado");
            var esperadas = s.Pacote is { } pac ? ResumoExigencias.Pendentes(ResumoExigencias.DoProjeto(pac.Conteudo.Projeto)) : [];
            p["exigencias"] = new JsonArray(r.Pendencias.Select(x => (JsonNode)("ITEM " + x.Numero.ToString("D2", CultureInfo.InvariantCulture) + ": " + x.Texto)).ToArray());
            var exigenciasOk = esperadas.Count > 0 && r.Pendencias.Select(x => (x.ItemId, x.Texto, x.Fundamento)).SequenceEqual(esperadas.Select(x => (x.Id, x.Texto, x.Fundamento)))
                && r.Pendencias.All(x => !x.Editada);
            var aviso = j.InfoMensagemAtual;
            p["aviso"] = aviso.Title + " · " + aviso.Message;
            var avisoOk = aviso.IsOpen && aviso.Title == FluxoNovoDespacho.TituloExigenciasSemIa && aviso.Message == FluxoNovoDespacho.ExigenciasSemIa(r);
            await Esperar(500);
            int iniciadosDepois;
            lock (programasIniciados) iniciadosDepois = programasIniciados.Count;
            var n = j.NovoDespacho;
            var semIa = iniciadosDepois == iniciadosAntes && !n.AnelInstrucao.IsActive && n.BotaoAplicarInstrucao.Visibility == Visibility.Visible;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "36b-janela-xaml-correcao-sem-ia.png"));
            p["conferencias"] = new JsonObject { ["exigencias_do_relatorio"] = exigenciasOk, ["aviso_sem_ia"] = avisoOk, ["nenhuma_ia_chamada"] = semIa };
            p["ok"] = exigenciasOk && avisoOk && semIa;
        });

        await Passo("37_novo_despacho_aprovacao", async p =>
        {
            Exigir();
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            await j.EscolherProcessoNaListaAsync("0001");
            j.FaixaAtual.EscolherAba("Página Inicial");
            await Esperar(250);
            var shasCorrecao = Directory.GetFiles(etapa03).ToDictionary(f => f, Sha);
            perguntas.Confirmar = true; // "Emitir mesmo assim"
            var feitasAntes = perguntas.Feitas.Count;
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoAprovacao);
            await j.AguardarNovoDespachoAsync();
            var s = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
            var esperado = ConteudoDespacho.Avisos(TipoDespacho.Aprovacao, s.Pacote!.Conteudo.Projeto);
            p["aviso"] = esperado.FirstOrDefault();
            var avisoOk = esperado.Count == 1 && esperado[0].StartsWith("O relatório tem 2 itens com situação diferente de ATENDE", StringComparison.Ordinal)
                && s.AvisosConfirmados.SequenceEqual(esperado) && perguntas.Feitas.Skip(feitasAntes).Contains("confirmar Emitir a aprovação?");
            var faltando = s.Rascunho!.Faltando();
            p["faltando"] = new JsonArray(faltando.Select(f => (JsonNode)f).ToArray());
            var faltandoOk = faltando.SequenceEqual(["Tipo de obra (obra nova, alteração de obra ou ampliação de obra)", "Área total a construir (m², ex.: 210,52)"])
                && j.NovoDespacho.LinhasFaltando.SequenceEqual(faltando) && !j.NovoDespacho.BotaoGerar.IsEnabled
                && j.NovoDespacho.TextoDaPrevia.Contains("[preencher: ", StringComparison.Ordinal);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "37-janela-xaml-aprovacao-faltando.png"));
            j.NovoDespacho.CaixaObraNova.IsChecked = true;
            j.NovoDespacho.CaixaArea.Text = "210,52";
            var habilitou = await EsperarAteAsync(() => Task.FromResult(j.NovoDespacho.BotaoGerar.IsEnabled), 5000) && s.Rascunho.AreaTotal == "210,52";
            // A etapa existente (a da correção): o que já foi preenchido continua.
            await j.EscolherEtapaAsync("DESPACHO 03 - 2099-03-10");
            var etapaOk = s.Etapa is { Nome: "DESPACHO 03 - 2099-03-10", Nova: false } && s.Rascunho!.Faltando().Count == 0 && j.NovoDespacho.BotaoGerar.IsEnabled
                && j.NovoDespacho.TextoDaPrevia.Contains("obra nova", StringComparison.Ordinal) && j.NovoDespacho.TextoDaPrevia.Contains("210,52", StringComparison.Ordinal);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "37-janela-xaml-aprovacao-preenchida.png"));
            await j.GerarEConferirAsync();
            var pdfOk = s.Preparacao is { Pdf: not null, Erros.Count: 0 } && j.NovoDespacho.CaixasDasPaginas.Count == s.Preparacao.Paginas;
            var marcou = await MarcarTodasAsync();
            var e = await j.GravarDespachoAsync() ?? throw new InvalidOperationException("não gravou: " + j.InfoMensagemAtual.Message);
            Anotar(e);
            var recibo = ReciboDespacho.Ler(e.Recibo);
            var arquivosOk = Mesmo(e.Docx, Path.Combine(etapa03, baseAprovacao + ".docx")) && Mesmo(e.Pdf, Path.Combine(etapa03, baseAprovacao + ".pdf"))
                && Mesmo(e.Recibo, Path.Combine(etapa03, baseAprovacao + ".recibo-editor.json")) && Directory.GetFiles(etapa03).Length == 6
                && shasCorrecao.All(kv => File.Exists(kv.Key) && Sha(kv.Key) == kv.Value)
                && recibo["avisos_confirmados"]!.AsArray().Select(a => a!.GetValue<string>()).SequenceEqual(esperado)
                && recibo["etapa"]?["nova"]?.GetValue<bool>() == false;
            p["arquivos"] = new JsonArray(Directory.GetFiles(etapa03).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).Select(f => (JsonNode)f).ToArray());
            p["conferencias"] = new JsonObject
            {
                ["aviso_confirmado"] = avisoOk, ["faltando_bloqueia_gerar"] = faltandoOk, ["preencher_habilita"] = habilitou, ["etapa_existente"] = etapaOk,
                ["pdf"] = pdfOk, ["marcar_todas"] = marcou, ["arquivos_e_recibo"] = arquivosOk,
            };
            p["ok"] = avisoOk && faltandoOk && habilitou && etapaOk && pdfOk && marcou && arquivosOk;
        });

        await Passo("38_despacho_sem_libreoffice", async p =>
        {
            Exigir();
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            j.FaixaAtual.EscolherAba("Página Inicial");
            await Esperar(250);
            j.Conversor = new ConversorSemLibreOffice();
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var s = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
            var etapa04 = Path.Combine(p0001, "DESPACHO 04 - 2099-03-10");
            var origemOk = s.Processo?.Numero == 1 && s.Etapa is { Nome: "DESPACHO 04 - 2099-03-10", Nova: true };
            await j.GerarEConferirAsync();
            var semPdf = "O LibreOffice não está instalado neste computador. O despacho será gravado só em Word (DOCX). Para o PDF, rode o instalador do kit "
                + "(INSTALAR_KIT_PMVNI.cmd) e use Despachos › Gerar PDF.";
            var n = j.NovoDespacho;
            p["mensagem"] = n.AvisoSemPdf.Message;
            var avisoOk = s.Passo == PassoNovoDespacho.Conferencia && n.AvisoSemPdf.IsOpen && n.AvisoSemPdf.Message == semPdf
                && n.BotaoGravar.Content as string == "Gravar só o Word (.docx)" && n.BotaoGravar.IsEnabled && n.CaixasDasPaginas.Count == 0;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "38-janela-xaml-sem-libreoffice.png"));
            var e = await j.GravarDespachoAsync() ?? throw new InvalidOperationException("não gravou: " + j.InfoMensagemAtual.Message);
            Anotar(e);
            var recibo = ReciboDespacho.Ler(e.Recibo);
            var soDocx = e.Pdf is null && Mesmo(e.Pasta, etapa04) && Directory.GetFiles(etapa04).Length == 2 && !File.Exists(Path.ChangeExtension(e.Docx, ".pdf"))
                && recibo["sem_pdf_motivo"]?.GetValue<string>() == "SEM_LIBREOFFICE" && recibo["pdf"] is null
                && j.AvisoDespachoGravado.Message == $"2 arquivos em {etapa04}. Recibo: {baseCorrecao}.recibo-editor.json.";
            var shaDocx = Sha(e.Docx);

            // O conversor real volta: Despachos › Gerar PDF nesse despacho (o escolhido na lista) → páginas → marcar → gravar só o PDF.
            j.Conversor = conversorReal;
            await j.AguardarLeiturasAsync();
            var gerarHabilitado = Mesmo(j.DocxSemPdf, e.Docx) && j.FaixaAtual.Habilitado(ComandosApp.DespachosGerarPdf) && Mesmo(j.DespachoEscolhido?.Despacho.Pasta, etapa04);
            await j.ExecutarComandoAsync(ComandosApp.DespachosGerarPdf);
            var s2 = j.DespachoEmEdicao;
            var paginasOk = s2 is { SoPdf: true, Passo: PassoNovoDespacho.Conferencia, Preparacao: { Pdf: not null, Erros.Count: 0 } }
                && n.CaixasDasPaginas.Count == s2.Preparacao.Paginas && !n.BotaoGravar.IsEnabled;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "38-janela-xaml-gerar-pdf.png"));
            var marcou = await MarcarTodasAsync();
            var e2 = await j.GravarDespachoAsync() ?? throw new InvalidOperationException("não gravou o PDF: " + j.InfoMensagemAtual.Message);
            Anotar(e2);
            var recibo2 = ReciboDespacho.Ler(e2.Recibo);
            var pdfOk = Mesmo(e2.Pdf, Path.ChangeExtension(e.Docx, ".pdf")) && File.Exists(e2.Pdf!) && Sha(e.Docx) == shaDocx && Mesmo(e2.Recibo, e.Recibo)
                && recibo2["sem_pdf_motivo"] is null && string.Equals(recibo2["pdf"]?["sha256"]?.GetValue<string>(), Sha(e2.Pdf!), StringComparison.OrdinalIgnoreCase)
                && Directory.GetFiles(etapa04).Length == 3 && j.DocxSemPdf is null && !j.FaixaAtual.Habilitado(ComandosApp.DespachosGerarPdf);
            p["arquivos"] = new JsonArray(Directory.GetFiles(etapa04).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).Select(f => (JsonNode)f).ToArray());
            p["conferencias"] = new JsonObject
            {
                ["etapa_nova"] = origemOk, ["aviso_sem_pdf"] = avisoOk, ["so_docx"] = soDocx, ["gerar_pdf_habilitado"] = gerarHabilitado, ["paginas"] = paginasOk,
                ["marcar_todas"] = marcou, ["pdf_gravado"] = pdfOk,
            };
            p["ok"] = origemOk && avisoOk && soDocx && gerarHabilitado && paginasOk && marcou && pdfOk;
        });

        await Passo("39_despacho_nao_sobrescreve", async p =>
        {
            Exigir();
            j.Conversor = conversorReal;
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            var antes = Directory.GetFiles(etapa03).ToDictionary(f => f, Sha);
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            _ = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
            await j.EscolherEtapaAsync("DESPACHO 03 - 2099-03-10");
            await j.GerarEConferirAsync();
            var marcou = await MarcarTodasAsync();
            var e = await j.GravarDespachoAsync() ?? throw new InvalidOperationException("não gravou: " + j.InfoMensagemAtual.Message);
            Anotar(e);
            var b2 = baseCorrecao + " (2)";
            var nomesOk = Mesmo(e.Docx, Path.Combine(etapa03, b2 + ".docx")) && Mesmo(e.Pdf, Path.Combine(etapa03, b2 + ".pdf"))
                && Mesmo(e.Recibo, Path.Combine(etapa03, b2 + ".recibo-editor.json"));
            var intactos = antes.All(kv => File.Exists(kv.Key) && Sha(kv.Key) == kv.Value) && Directory.GetFiles(etapa03).Length == antes.Count + 3;
            var temporarios = Directory.GetFiles(copiaDespacho, "*.tmp", SearchOption.AllDirectories);
            p["arquivos"] = new JsonArray(Directory.GetFiles(etapa03).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).Select(f => (JsonNode)f).ToArray());
            p["temporarios"] = new JsonArray(temporarios.Select(f => (JsonNode)Path.GetRelativePath(copiaDespacho, f)).ToArray());
            p["conferencias"] = new JsonObject { ["marcar_todas"] = marcou, ["nomes_com_2"] = nomesOk, ["anteriores_intactos"] = intactos, ["sem_tmp"] = temporarios.Length == 0 };
            p["ok"] = marcou && nomesOk && intactos && temporarios.Length == 0;
        });

        await Passo("40_despacho_bloqueios", async p =>
        {
            Exigir();
            var n = j.NovoDespacho;
            // (a) Situação mudada sem justificativa no pacote aberto: a mensagem da conferência da revisão e só o passo 1.
            if (!Mesmo(j.Sessao?.Caminho, pmvrelD) && !await j.AbrirPacoteAsync(pmvrelD)) throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
            await j.MudarModuloAsync(Modulo.Relatorios);
            var sessaoA = j.Sessao!;
            await j.AplicarAsync(new DefinirSituacao(sessaoA.Revisao.Projeto.Incluidos[1].Id, "ATENDE"));
            var erros = ConferenciaRevisao.Erros(sessaoA.Revisao.Projeto);
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var sa = j.DespachoEmEdicao;
            await j.GerarEConferirAsync(); // não faz nada: não há rascunho
            p["erros_da_revisao"] = new JsonArray(erros.Select(x => (JsonNode)x).ToArray());
            p["bloqueio_a"] = n.AvisoBloqueio.Message;
            var aOk = erros.Count == 1 && sa is { Passo: PassoNovoDespacho.Origem, Rascunho: null, Preparacao: null } && sa.Bloqueios.Contains(erros[0])
                && n.AvisoBloqueio.IsOpen && n.AvisoBloqueio.Message.Split(Environment.NewLine).Contains(erros[0])
                && n.PainelConteudo.Visibility == Visibility.Collapsed && !n.BotaoGerar.IsEnabled;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "40-janela-xaml-bloqueio-justificativa.png"));
            await j.MudarModuloAsync(Modulo.Relatorios); // passo 1: sai sem perguntar
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var desfez = ConferenciaRevisao.Erros(sessaoA.Revisao.Projeto).Count == 0 && await j.GravarAgoraAsync() && j.DespachoEmEdicao is null;

            // (b) A análise da IA da cópia (capa 9001/2026) importada como _2 e escolhida como relatório base: o processo não confere.
            var dados = Directory.GetFiles(Path.Combine(p0001, ".pmvni-ai"), "dados_0001.json", SearchOption.AllDirectories).Single();
            if (!await j.ImportarParaAsync(dados, pmvrelD2)) throw new InvalidOperationException("não importou o dados_0001: " + j.InfoMensagem.Message);
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            await j.EscolherProcessoNaListaAsync("0001");
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var sb = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
            await j.EscolherRelatorioAsync(pmvrelD); // o outro relatório abre o passo 2…
            var outroAbre = sb.Passo == PassoNovoDespacho.Conteudo;
            await j.EscolherRelatorioAsync(pmvrelD2); // …e o da análise errada para no passo 1
            var msgB = "O processo da capa (9001/2026) não é o da pasta (0001/2099). Confira se o relatório é deste processo.";
            p["bloqueio_b"] = n.AvisoBloqueio.Message;
            var bOk = outroAbre && Mesmo(sb.CaminhoPacote, pmvrelD2) && sb.Passo == PassoNovoDespacho.Origem && sb.Rascunho is null && sb.Bloqueios.Contains(msgB)
                && n.AvisoBloqueio.IsOpen && n.AvisoBloqueio.Message.Split(Environment.NewLine).Contains(msgB);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "40-janela-xaml-bloqueio-processo.png"));

            // (c) Kit vazio nos Ajustes e nada instalado (um "Codex" vazio: sem a pasta do kit, valeriam as cópias instaladas, Tarefa 22):
            // os dois tipos desabilitados com o motivo; o kit volta.
            await j.MudarModuloAsync(Modulo.Processos);
            var kitVazio = Path.Combine(pasta, "kit-vazio");
            if (Directory.Exists(kitVazio)) ApagarArvore(kitVazio);
            Directory.CreateDirectory(kitVazio);
            var codexVazioC = Path.Combine(pasta, "codex-vazio-40c");
            Directory.CreateDirectory(codexVazioC);
            j.CodexHomeDosModelos = () => codexVazioC;
            j.DefinirPastaKit(kitVazio);
            await j.AguardarNovoDespachoAsync();
            var msgC = FontesModelos.NaoEncontrados(kitVazio);
            var faixa = j.FaixaAtual;
            p["dica_c"] = faixa.Dica(ComandosApp.DespachosNovoCorrecao);
            var desabilitados = !faixa.Habilitado(ComandosApp.DespachosNovoCorrecao) && !faixa.Habilitado(ComandosApp.DespachosNovoAprovacao)
                && faixa.Dica(ComandosApp.DespachosNovoCorrecao) == msgC && faixa.Dica(ComandosApp.DespachosNovoAprovacao) == msgC
                && j.TextoModelosNosAjustes.Text == "Correção: " + msgC + Environment.NewLine + "Aprovação: " + msgC;
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            var naoAbriu = j.DespachoEmEdicao is null && j.InfoMensagemAtual.IsOpen && j.InfoMensagemAtual.Message.Contains(msgC.TrimEnd('.'), StringComparison.Ordinal);
            await j.MudarModuloAsync(Modulo.Ajustes);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "40-janela-xaml-kit-vazio.png"));
            j.CodexHomeDosModelos = null;
            j.DefinirPastaKit(kitReal);
            await j.AguardarNovoDespachoAsync();
            var voltou = j.FaixaAtual.Habilitado(ComandosApp.DespachosNovoCorrecao) && j.FaixaAtual.Habilitado(ComandosApp.DespachosNovoAprovacao)
                && j.TextoModelosNosAjustes.Text.StartsWith("Correção: disponível" + Environment.NewLine + "Aprovação: disponível" + Environment.NewLine
                                                            + "Modelos lidos de: ", StringComparison.Ordinal);
            var cOk = desabilitados && naoAbriu && voltou;

            // (e) Com o despacho no passo 2, trocar de módulo pergunta: "Continuar editando" fica na página; "Descartar" fecha sem gravar nada.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            await j.EscolherProcessoNaListaAsync("0001");
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var se = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
            await j.EscolherRelatorioAsync(pmvrelD);
            var noPasso2 = se.Passo == PassoNovoDespacho.Conteudo;
            var feitasE = perguntas.Feitas.Count;
            perguntas.Confirmar = false; // "Continuar editando"
            await j.MudarModuloAsync(Modulo.Relatorios);
            var ficou = j.ModuloAtual == Modulo.Despachos && ReferenceEquals(j.DespachoEmEdicao, se) && j.PaginaNovoDespachoVisivel
                && perguntas.Feitas.Skip(feitasE).Contains("confirmar Descartar o despacho em edição?");
            perguntas.Confirmar = true; // "Descartar"
            await j.MudarModuloAsync(Modulo.Relatorios);
            var descartou = j.ModuloAtual == Modulo.Relatorios && j.DespachoEmEdicao is null;
            var eOk = noPasso2 && ficou && descartou;

            // (d) A cópia: o que havia + só os arquivos gravados em 36–39 e os .rpmvni importados; nada mudou no kit.
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            var depois = FotografarArvore(copiaDespacho);
            var novos = depois.Keys.Except(antesDoDespacho.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            // O .bak do Relatorio_0001_2099.rpmvni é o da gravação pedida no (a) (o relatório aberto é gravado antes do despacho): o app pode
            // criá-lo ao lado de um pacote que ele abriu (restrições globais). Nenhum outro arquivo é aceito.
            var bak = Path.GetRelativePath(copiaDespacho, pmvrelD) + ".bak";
            var esperados = gravadosNaCopia.Append(Path.GetRelativePath(copiaDespacho, pmvrelD)).Append(Path.GetRelativePath(copiaDespacho, pmvrelD2))
                .Concat(File.Exists(Path.Combine(copiaDespacho, bak)) ? [bak] : Array.Empty<string>())
                .Order(StringComparer.Ordinal).ToList();
            var sumiram = antesDoDespacho.Keys.Except(depois.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var mudaram = antesDoDespacho.Where(kv => depois.TryGetValue(kv.Key, out var sha) && sha != kv.Value).Select(kv => kv.Key).ToList();
            p["novos"] = new JsonArray(novos.Select(x => (JsonNode)x).ToArray());
            p["sumiram"] = new JsonArray(sumiram.Select(x => (JsonNode)x).ToArray());
            p["mudaram"] = new JsonArray(mudaram.Select(x => (JsonNode)x).ToArray());
            var kitDepois = RetratoDoKit(kitReal);
            p["kit_entradas"] = kitDepois.Length;
            var dOk = novos.SequenceEqual(esperados, StringComparer.OrdinalIgnoreCase) && sumiram.Count == 0 && mudaram.Count == 0 && kitAntes.Length > 0
                && kitDepois.SequenceEqual(kitAntes, StringComparer.Ordinal);
            p["conferencias"] = new JsonObject
            {
                ["a_sem_justificativa"] = aOk, ["a_desfeito"] = desfez, ["b_processo_errado"] = bOk, ["c_kit_vazio"] = cOk,
                ["e_descartar_pergunta"] = eOk, ["d_copia_e_kit"] = dOk,
            };
            p["ok"] = aOk && desfez && bOk && cOk && eOk && dOk;
        });

        await Passo("40b_despacho_sem_plugin", async p =>
        {
            // Tarefa 22: o despacho é do editor. Com o kit dos Ajustes vazio e o "Codex" só com o plugin analise que traz os modelos em
            // skills\analise\references\despacho (sem plugin despacho em lugar nenhum), os dois tipos ficam disponíveis, o rascunho de
            // correção sai do relatório em código, sem programa de IA, e o DOCX/PDF sai do modelo desse plugin. Kit antigo: o plugin despacho
            // instalado só empresta os modelos. Sem nada: "Atualize o kit PMVNI". Nada é gravado na pasta do processo.
            Exigir();
            var oficiais = j.ModelosDoKit ?? throw new InvalidOperationException("os modelos do kit não foram lidos");
            if (oficiais.Modelos.Count != 2 || oficiais.Fonte is not { } fonteKit) throw new InvalidOperationException("o kit real não tem os dois modelos");
            var codexComAnalise = Path.Combine(pasta, "codex-analise-com-modelos");
            var codexAntigo = Path.Combine(pasta, "codex-despacho-antigo");
            var codexVazio = Path.Combine(pasta, "codex-vazio");
            foreach (var d in new[] { codexComAnalise, codexAntigo, codexVazio }) if (Directory.Exists(d)) ApagarArvore(d);
            Directory.CreateDirectory(codexVazio);
            void Montar(string pastaRegistro, string pastaModelos, string prefixo, string pastaTextos)
            {
                Directory.CreateDirectory(pastaModelos);
                Directory.CreateDirectory(pastaTextos);
                var templates = new JsonArray();
                foreach (var m in oficiais.Modelos)
                {
                    File.Copy(m.Caminho, Path.Combine(pastaModelos, Path.GetFileName(m.Caminho)));
                    templates.Add(new JsonObject { ["role"] = m.Papel, ["path"] = prefixo + Path.GetFileName(m.Caminho), ["sha256"] = m.Sha256 });
                }
                foreach (var t in new[] { TipoDespacho.Correcao, TipoDespacho.Aprovacao })
                    File.Copy(Path.Combine(fonteKit.PastaTextos, TextosFixos.NomeArquivo(t)), Path.Combine(pastaTextos, TextosFixos.NomeArquivo(t)));
                File.WriteAllText(Path.Combine(pastaRegistro, FontesModelos.NomeRegistro), new JsonObject { ["templates_revision"] = 10, ["templates"] = templates }.ToJsonString());
            }
            var dadosAnalise = Path.Combine(codexComAnalise, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "references", "despacho");
            Montar(dadosAnalise, Path.Combine(dadosAnalise, "modelos"), "modelos/", dadosAnalise);
            var skillAntiga = Path.Combine(codexAntigo, "plugins", "cache", "pmvni-kit", "despacho", "2.3.0", "skills", "despacho");
            Montar(Path.Combine(skillAntiga, "assets"), Path.Combine(skillAntiga, "assets", "modelos"), "assets/modelos/", Path.Combine(skillAntiga, "references"));
            var kitVazio = Path.Combine(pasta, "kit-vazio-40b");
            if (Directory.Exists(kitVazio)) ApagarArvore(kitVazio);
            Directory.CreateDirectory(kitVazio);
            var antesDaCopia = FotografarArvore(copiaDespacho);

            async Task<string> Ler(string codexHome)
            {
                j.CodexHomeDosModelos = () => codexHome;
                j.DefinirPastaKit(kitVazio);
                await j.AguardarNovoDespachoAsync();
                return j.TextoModelosNosAjustes.Text;
            }
            bool Disponiveis() => j.FaixaAtual.Habilitado(ComandosApp.DespachosNovoCorrecao) && j.FaixaAtual.Habilitado(ComandosApp.DespachosNovoAprovacao);

            // (a) Só o plugin analise com os dados de despacho.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var textoA = await Ler(codexComAnalise);
            p["ajustes_a"] = textoA;
            var aDisponivel = Disponiveis() && textoA.EndsWith("Modelos lidos de: plugin analise instalado (versão 4.0.0)", StringComparison.Ordinal);
            await j.EscolherProcessoNaListaAsync("0001");
            int iniciadosAntes;
            lock (programasIniciados) iniciadosAntes = programasIniciados.Count;
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var s = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
            if (!Mesmo(s.CaminhoPacote, pmvrelD) || s.Passo != PassoNovoDespacho.Conteudo) await j.EscolherRelatorioAsync(pmvrelD);
            var esperadas = s.Pacote is { } pac ? ResumoExigencias.Pendentes(ResumoExigencias.DoProjeto(pac.Conteudo.Projeto)).Select(x => x.Id).ToList() : [];
            var rascunhoOk = s.Rascunho is { } r && esperadas.Count > 0 && r.Pendencias.Select(x => x.ItemId).SequenceEqual(esperadas)
                && s.Modelo is { } mod && mod.Caminho.StartsWith(dadosAnalise, StringComparison.OrdinalIgnoreCase);
            await j.GerarEConferirAsync();
            var prep = s.Preparacao;
            p["preparacao_erros"] = new JsonArray((prep?.Erros ?? []).Select(x => (JsonNode)x).ToArray());
            p["paginas"] = prep?.Paginas;
            var docxOk = prep is { Erros.Count: 0 } && prep.Docx.Length > 0
                && ValidacaoDespacho.Comparar(PreenchimentoDocx.ComProfissional(await File.ReadAllBytesAsync(s.Modelo!.Caminho), Pmvni.Composicao.Profissional.Atual),
                    prep.Docx).Count == 0;
            string[] programasDoDespacho;
            lock (programasIniciados) programasDoDespacho = programasIniciados.Skip(iniciadosAntes).ToArray();
            var semIa = programasDoDespacho.Length == 1 && Mesmo(programasDoDespacho[0], sofficeDoRoteiro);
            p["programas_iniciados"] = new JsonArray(programasDoDespacho.Select(c => (JsonNode)c).ToArray());
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "40b-janela-xaml-despacho-sem-plugin.png"));
            perguntas.Confirmar = true; // "Descartar": nada é gravado
            await j.MudarModuloAsync(Modulo.Processos);
            var fechou = j.DespachoEmEdicao is null;

            // (b) Kit antigo: só o plugin despacho instalado (os modelos dele; ele não é chamado).
            var textoB = await Ler(codexAntigo);
            p["ajustes_b"] = textoB;
            var bOk = Disponiveis() && textoB.Contains("Modelos lidos de: plugin despacho antigo instalado (versão 2.3.0)", StringComparison.Ordinal);

            // (c) Nada: os dois tipos desabilitados com "Atualize o kit PMVNI".
            var textoC = await Ler(codexVazio);
            p["ajustes_c"] = textoC;
            var msgNada = FontesModelos.NaoEncontrados(kitVazio);
            var cOk = !Disponiveis() && j.FaixaAtual.Dica(ComandosApp.DespachosNovoCorrecao) == msgNada
                && textoC == "Correção: " + msgNada + Environment.NewLine + "Aprovação: " + msgNada;

            // Volta ao kit real e ao Codex de verdade para os passos seguintes (o kit vale antes das cópias instaladas); a cópia dos
            // processos não mudou.
            j.CodexHomeDosModelos = null;
            j.DefinirPastaKit(kitReal);
            await j.AguardarNovoDespachoAsync();
            var voltou = Disponiveis();
            var depois = FotografarArvore(copiaDespacho);
            var copiaIgual = depois.Count == antesDaCopia.Count && depois.All(kv => antesDaCopia.TryGetValue(kv.Key, out var sha) && sha == kv.Value);
            p["conferencias"] = new JsonObject
            {
                ["a_disponivel_pelo_analise"] = aDisponivel, ["a_rascunho_em_codigo"] = rascunhoOk, ["a_docx_do_modelo"] = docxOk, ["a_sem_ia"] = semIa,
                ["a_descartado"] = fechou, ["b_kit_antigo"] = bOk, ["c_atualize_o_kit"] = cOk, ["kit_real_de_volta"] = voltou, ["copia_igual"] = copiaIgual,
            };
            p["ok"] = aDisponivel && rascunhoOk && docxOk && semIa && fechou && bOk && cOk && voltou && copiaIgual;
        });

        // ---------- Onda de correções 1 (revisão geral antecipada e pendências da Tarefa 16): passos 41 a 45 ----------
        // 41 a edição aberta no A4 entra em Exportar, Salvar e Fechar; 42 recomposição que falha bloqueia exportar até Recompor; 43 o
        // batimento toca a pasta de trabalho; 44 tempos do LibreOffice com o perfil persistente (<dados>\lo); 45 gravando o despacho não
        // troca de módulo e a troca da pasta de processos fecha (ou mantém) o despacho aberto.
        var pacoteOnda = Path.Combine(pasta, "onda1.rpmvni");
        async Task<bool> EditandoAsync() => (await Js(j, "!!document.querySelector('[data-pmvni-editando]')")).GetBoolean();
        // Abre a edição da verificação do item no A4 (Editar texto) e "digita" o texto (sem Enter: a edição continua aberta).
        async Task<bool> DigitarAsync(string itemId, string texto)
        {
            await j.SelecionarAsync(itemId);
            await j.ExecutarComandoAsync(ComandosApp.InicioEditarTexto);
            var abriu = await EsperarAteAsync(EditandoAsync, 3000);
            await Js(j, "(() => { const e = document.querySelector('[data-pmvni-editando]'); if (!e) return false; e.textContent = "
                + JsonSerializer.Serialize(texto) + "; return true; })()");
            return abriu && await EditandoAsync();
        }
        string ShaHex(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

        await Passo("41_edicao_aberta_concluida", async p =>
        {
            await j.MudarModuloAsync(Modulo.Relatorios);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), pacoteOnda)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var ids = s.Revisao.Projeto.Incluidos.Select(i => i.Id).ToList();
            string? Verificacao(Projeto pr, string id) => DadosEfetivos.TextoEfetivo(pr, pr.Item(id)!, "verificacao");

            // Exportar com o campo aberto: o texto entra no relatório, a tela é recomposta e o PDF sai dos dados com ele.
            const string TextoExportar = "Texto digitado no A4 e exportado sem Enter pelo roteiro.";
            var abriu1 = await DigitarAsync(ids[0], TextoExportar);
            var pdf = Path.Combine(pasta, "onda1-edicao-aberta.pdf");
            File.Delete(pdf);
            var (ok, x, erro, _) = await j.ExportarParaAsync(pdf);
            var efetivo = DadosEfetivos.Gerar(s.Revisao.Projeto).Json;
            var recibo = x is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(x.CaminhoRecibo));
            var folhas = await j.Documento!.TextoPorFolhaAsync();
            var exportou = abriu1 && ok && Verificacao(s.Revisao.Projeto, ids[0]) == TextoExportar
                && recibo?["sha256_dados"]?.GetValue<string>() == ShaHex(efetivo) && System.Text.Encoding.UTF8.GetString(efetivo).Contains(TextoExportar, StringComparison.Ordinal)
                && folhas.Any(f => f.Contains(TextoExportar, StringComparison.Ordinal)) && !await EditandoAsync();
            p["exportar"] = new JsonObject { ["abriu_a_edicao"] = abriu1, ["exportou"] = ok, ["erro"] = erro, ["pdf_com_o_texto"] = exportou };

            // Salvar (Ctrl+S pela faixa) com o campo aberto: o arquivo tem o texto.
            const string TextoSalvar = "Texto digitado no A4 e salvo sem Enter pelo roteiro.";
            var abriu2 = await DigitarAsync(ids[1], TextoSalvar);
            await j.ExecutarComandoAsync(ComandosApp.ArquivoSalvar);
            var noArquivo = Pacote.Ler(pacoteOnda).Conteudo.Projeto;
            var salvou = abriu2 && s.Estado == EstadoGravacao.Salvo && Verificacao(noArquivo, ids[1]) == TextoSalvar && !await EditandoAsync();
            p["salvar"] = salvou;

            // Fechar com o campo aberto: gravado e fechado com o texto.
            const string TextoFechar = "Texto digitado no A4 e gravado ao fechar pelo roteiro.";
            var abriu3 = await DigitarAsync(ids[2], TextoFechar);
            var fechou = await j.FecharSessaoAsync() && j.Sessao is null;
            var aoFechar = Pacote.Ler(pacoteOnda).Conteudo.Projeto;
            var fechouComTexto = abriu3 && fechou && Verificacao(aoFechar, ids[2]) == TextoFechar;
            p["fechar"] = fechouComTexto;
            p["ok"] = exportou && salvou && fechouComTexto;
        });

        await Passo("42_recomposicao_que_falha_bloqueia_exportar", async p =>
        {
            if (!await j.AbrirPacoteAsync(pacoteOnda)) throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var item = s.Revisao.Projeto.Incluidos[^1]; // o título do último item (o curto tem 3)
            const string Titulo = "Título mudado com a recomposição falhando (roteiro)";
            // A composição cai depois de o comando ser aplicado: o relatório tem a alteração, a tela não.
            s.FalhaDeRoteiroNaRecomposicao = () => Task.FromException(new InvalidOperationException("falha simulada da composição (roteiro)"));
            await j.AplicarAsync(new DefinirCampo(item.Id, "titulo", Titulo));
            s.FalhaDeRoteiroNaRecomposicao = null;
            var aplicada = DadosEfetivos.TextoEfetivo(s.Revisao.Projeto, s.Revisao.Projeto.Item(item.Id)!, "titulo") == Titulo;
            var bloqueou = s.Desatualizada && j.InfoDesatualizado.IsOpen && !j.ExportarHabilitado
                && j.DicaExportar == "A exportação está bloqueada: " + SessaoEdicao.TextoDesatualizada;
            p["mensagem"] = j.InfoMensagem.Title + ": " + j.InfoMensagem.Message;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "42-janela-xaml-desatualizada.png"));
            var pdf = Path.Combine(pasta, "onda1-desatualizada.pdf");
            File.Delete(pdf);
            var (ok1, _, erro1, _) = await j.ExportarParaAsync(pdf);
            var recusou = !ok1 && erro1 == SessaoEdicao.TextoDesatualizada && !File.Exists(pdf);
            p["exportar_recusado"] = erro1;
            // Recompor (o botão do aviso): a tela volta a ser a do relatório e a exportação vale de novo, com o PDF do relatório novo.
            var recompos = await j.RecomporDesatualizadaAsync();
            var voltou = recompos && !s.Desatualizada && !j.InfoDesatualizado.IsOpen && j.ExportarHabilitado;
            var (ok2, x2, erro2, _) = await j.ExportarParaAsync(pdf);
            var efetivo = DadosEfetivos.Gerar(s.Revisao.Projeto).Json;
            var recibo = x2 is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(x2.CaminhoRecibo));
            var folhas = await j.Documento!.TextoPorFolhaAsync();
            var exportou = ok2 && recibo?["sha256_dados"]?.GetValue<string>() == ShaHex(efetivo)
                && recibo?["sha256_pacote"]?.GetValue<string>() == s.Disco.Sha256 && folhas.Any(f => f.Contains(Titulo, StringComparison.OrdinalIgnoreCase)); // o título do card sai em caixa alta (CSS)
            p["exporta_depois_detalhe"] = new JsonObject { ["dados"] = recibo?["sha256_dados"]?.GetValue<string>() == ShaHex(efetivo), ["pacote"] = recibo?["sha256_pacote"]?.GetValue<string>() == s.Disco.Sha256, ["folhas"] = folhas.Any(f => f.Contains(Titulo, StringComparison.OrdinalIgnoreCase)) };
            p["conferencias"] = new JsonObject
            {
                ["alteracao_aplicada"] = aplicada, ["exportar_bloqueado"] = bloqueou, ["exportar_recusado"] = recusou, ["recompor"] = voltou,
                ["exporta_depois"] = exportou, ["erro_depois"] = erro2,
            };
            p["ok"] = aplicada && bloqueou && recusou && voltou && exportou;
        });

        await Passo("43_batimento_toca_a_pasta_de_trabalho", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var trabalho = s.Recompositor.PastaTrabalho;
            Directory.SetLastWriteTimeUtc(trabalho, DateTime.UtcNow.AddDays(-2));
            await s.BaterAsync();
            var idade = DateTime.UtcNow - Directory.GetLastWriteTimeUtc(trabalho);
            p["idade_depois_do_batimento_s"] = Math.Round(idade.TotalSeconds, 1);
            p["ok"] = idade < TimeSpan.FromMinutes(1);
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
        });

        await Passo("44_libreoffice_perfil_persistente", p =>
        {
            Exigir();
            var tempos = conversorReal.Milissegundos.ToList();
            var perfil = Path.Combine(PastaDados.Raiz, "lo");
            p["tempos_das_conversoes_ms"] = new JsonArray(tempos.Select(t => (JsonNode)t).ToArray());
            p["perfil_existe"] = Directory.Exists(perfil);
            p["perfil_inicializado"] = Directory.Exists(Path.Combine(perfil, "user"));
            p["sem_pid_deixado"] = !File.Exists(ConversorLibreOffice.ArquivoPid(perfil));
            p["ok"] = tempos.Count >= 2 && Directory.Exists(Path.Combine(perfil, "user")) && !File.Exists(ConversorLibreOffice.ArquivoPid(perfil));
            return Task.CompletedTask;
        });

        await Passo("45_despacho_gravando_e_pasta_de_processos", async p =>
        {
            Exigir();
            perguntas.Confirmar = true;
            async Task<SessaoNovoDespacho> AbrirNoPasso2Async()
            {
                await j.MudarModuloAsync(Modulo.Processos);
                await j.AguardarLeiturasAsync();
                await j.EscolherProcessoNaListaAsync("0001");
                await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
                await j.AguardarNovoDespachoAsync();
                var s = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página não abriu: " + j.InfoMensagemAtual.Message);
                await j.EscolherRelatorioAsync(pmvrelD);
                if (s.Passo != PassoNovoDespacho.Conteudo) throw new InvalidOperationException("não chegou ao passo 2: " + j.NovoDespacho.AvisoBloqueio.Message);
                return s;
            }

            // (a) Trocar a pasta de processos com o despacho no passo 2: pergunta; "Continuar editando" mantém a pasta e o despacho.
            var outra = Path.Combine(pasta, "processos");
            var s1 = await AbrirNoPasso2Async();
            var feitas = perguntas.Feitas.Count;
            perguntas.Confirmar = false;
            var trocouComContinuar = await j.TrocarPastaProcessosAsync(outra);
            var manteve = !trocouComContinuar && ReferenceEquals(j.DespachoEmEdicao, s1) && Mesmo(j.PastaProcessosEfetiva, copiaDespacho)
                && perguntas.Feitas.Skip(feitas).Contains("confirmar Descartar o despacho em edição?");
            perguntas.Confirmar = true;
            var trocou = await j.TrocarPastaProcessosAsync(outra) && j.DespachoEmEdicao is null && Mesmo(j.PastaProcessosEfetiva, outra);
            var voltou = await j.TrocarPastaProcessosAsync(copiaDespacho) && Mesmo(j.PastaProcessosEfetiva, copiaDespacho);
            p["pasta"] = new JsonObject { ["continuar_mantem"] = manteve, ["descartar_troca"] = trocou, ["voltou"] = voltou };

            // (b) Gravando na pasta do processo (só DOCX, sem LibreOffice): trocar de módulo não descarta nem diz "Nada foi gravado".
            j.Conversor = new ConversorSemLibreOffice();
            var s2 = await AbrirNoPasso2Async();
            await j.GerarEConferirAsync();
            var prontoParaGravar = s2.Passo == PassoNovoDespacho.Conferencia && j.NovoDespacho.BotaoGravar.IsEnabled;
            var feitas2 = perguntas.Feitas.Count;
            var gravando = j.GravarDespachoAsync();
            var viuGravando = s2.Gravando;
            await j.MudarModuloAsync(Modulo.Relatorios);
            var ficou = j.ModuloAtual == Modulo.Despachos && !perguntas.Feitas.Skip(feitas2).Any(f => f.Contains("Descartar", StringComparison.Ordinal))
                && j.InfoMensagemAtual.Message == FluxoNovoDespacho.GravandoAguarde;
            var entrega = await gravando;
            j.Conversor = conversorReal;
            if (entrega is not null) Anotar(entrega);
            var gravou = entrega is { Pdf: null } && File.Exists(entrega.Docx) && File.Exists(entrega.Recibo) && j.DespachoEmEdicao is null;
            p["gravando"] = new JsonObject { ["pronto"] = prontoParaGravar, ["viu_gravando"] = viuGravando, ["nao_trocou"] = ficou, ["gravou"] = gravou };
            p["ok"] = manteve && trocou && voltou && prontoParaGravar && viuGravando && ficou && gravou;
        });
        Environment.SetEnvironmentVariable("PMVNI_KIT_RAIZ", kitEnv);

        // ---------- Onda de correções 2: passos 46 e 47 ----------
        // 46 Salvar como e Salvar como cópia (conflito) concluem a edição aberta no A4: a cópia tem o texto; 47 fechar com a edição
        // recusada pela conferência pergunta (Voltar à edição / Fechar sem ele) em vez de perder o texto em silêncio.
        string Completo(string c) => Path.GetFullPath(c);
        void ApagarSe(string c) { if (File.Exists(c)) File.Delete(c); }

        await Passo("46_salvar_como_com_edicao_aberta", async p =>
        {
            await j.MudarModuloAsync(Modulo.Relatorios);
            var original = Path.Combine(pasta, "onda2-a.rpmvni");
            var copia = Path.Combine(pasta, "onda2-a-copia.rpmvni");
            ApagarSe(original); ApagarSe(copia);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), original)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var ids = s.Revisao.Projeto.Incluidos.Select(i => i.Id).ToList();
            string? Verificacao(Projeto pr, string id) => DadosEfetivos.TextoEfetivo(pr, pr.Item(id)!, "verificacao");

            // Salvar como com o campo aberto: a cópia tem o texto e passa a ser o relatório aberto.
            const string TextoCopia = "Texto digitado no A4 e gravado pelo Salvar como (roteiro).";
            var abriu1 = await DigitarAsync(ids[0], TextoCopia);
            perguntas.ArquivoParaSalvar = copia;
            try { await j.ExecutarComandoAsync(ComandosApp.ArquivoSalvarComo); }
            finally { perguntas.ArquivoParaSalvar = null; }
            var naCopia = File.Exists(copia) ? Pacote.Ler(copia).Conteudo.Projeto : null;
            var salvouComo = abriu1 && naCopia is not null && Verificacao(naCopia, ids[0]) == TextoCopia && j.Sessao is { } sc
                && Completo(sc.Caminho) == Completo(copia) && sc.Estado == EstadoGravacao.Salvo && !await EditandoAsync();
            p["salvar_como"] = salvouComo;

            // Salvar como cópia (conflito) com o campo aberto: a cópia ao lado tem o texto.
            var outro = Pacote.Ler(copia);
            outro.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO PROGRAMA GRAVOU (roteiro 46)";
            Pacote.Gravar(copia, outro.Conteudo, outro.Disco);
            const string TextoConflito = "Texto digitado no A4 e gravado na cópia do conflito (roteiro).";
            var abriu2 = await DigitarAsync(ids[1], TextoConflito);
            var copiou = await j.SalvarCopiaDeConflitoAsync();
            var caminhoCopia = j.Sessao!.Caminho;
            var naCopiaDoConflito = copiou && Completo(caminhoCopia) != Completo(copia) ? Pacote.Ler(caminhoCopia).Conteudo.Projeto : null;
            var copiaConflito = abriu2 && naCopiaDoConflito is not null && Verificacao(naCopiaDoConflito, ids[1]) == TextoConflito && !await EditandoAsync();
            p["copia_de_conflito"] = copiaConflito;
            p["ok"] = salvouComo && copiaConflito;
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
        });

        await Passo("47_fechar_com_edicao_recusada", async p =>
        {
            await j.MudarModuloAsync(Modulo.Relatorios);
            var arquivo = Path.Combine(pasta, "onda2-b.rpmvni");
            ApagarSe(arquivo);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), arquivo)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var id = s.Revisao.Projeto.Incluidos[0].Id;
            var antes = DadosEfetivos.TextoEfetivo(s.Revisao.Projeto, s.Revisao.Projeto.Item(id)!, "verificacao");
            const string Branco = "   "; // só espaços: o kit exige a verificação preenchida, então a conferência recusa

            // (a) "Voltar à edição": o relatório continua aberto, a pergunta traz o motivo e o campo volta a ser editado no A4.
            var abriu = await DigitarAsync(id, Branco);
            var feitas = perguntas.Feitas.Count;
            perguntas.FecharSemOTexto = false;
            var fechou1 = await j.FecharSessaoAsync();
            var pergunta = perguntas.Feitas.Skip(feitas).FirstOrDefault(f => f.StartsWith("fechar com texto recusado: ", StringComparison.Ordinal));
            var textoPergunta = perguntas.Fechamentos.LastOrDefault() ?? "";
            var voltou = !fechou1 && j.Sessao is not null && await EsperarAteAsync(EditandoAsync, 3000);
            var textoNoCampo = (await Js(j, "(() => { const e = document.querySelector('[data-pmvni-editando]'); return e ? e.textContent : null; })()")).GetString();
            p["pergunta"] = textoPergunta;
            p["voltou_a_edicao"] = voltou;
            p["texto_devolvido_ao_campo"] = textoNoCampo == Branco;

            // (b) "Fechar sem ele": fecha; o arquivo continua com o texto de antes.
            perguntas.FecharSemOTexto = true;
            var fechou2 = await j.FecharSessaoAsync();
            perguntas.FecharSemOTexto = false;
            var noArquivo = Pacote.Ler(arquivo).Conteudo.Projeto;
            var fechouSemEle = fechou2 && j.Sessao is null && DadosEfetivos.TextoEfetivo(noArquivo, noArquivo.Item(id)!, "verificacao") == antes;
            p["fechou_sem_o_texto"] = fechouSemEle;
            p["ok"] = abriu && pergunta is not null && textoPergunta.StartsWith("O texto em edição não pôde ser aplicado: ", StringComparison.Ordinal)
                && textoPergunta.EndsWith(". Fechar mesmo assim?", StringComparison.Ordinal) && voltou && textoNoCampo == Branco && fechouSemEle;
        });

        // ---------- Tarefa 19: passos 49 e 50 (Ajustes › Inteligência artificial), só com o agente falso ----------
        // O passo 48 fica reservado. Os passos 49 a 56 compartilham a preparação abaixo: o AgenteFalso.exe (publicado por tools\roteiro.ps1 em
        // tests\saida\agente-falso) faz o papel do codex.exe, do claude.exe e do python.exe; o CODEX_HOME é uma pasta do roteiro; e
        // ExecucaoProcesso.SomenteExecutaveisEm recusa qualquer programa fora da pasta do agente falso (o roteiro nunca toca o codex nem o claude
        // de verdade, nem guarda uma chave real).
        var agenteFalsoPasta = Path.GetFullPath(Path.Combine(pasta, "..", "agente-falso"));
        var agenteFalso = Path.Combine(agenteFalsoPasta, "AgenteFalso.exe");
        var codexHome = Path.Combine(pasta, "codex-home");
        var configCodex = Path.Combine(codexHome, "config.toml");
        const string ConfigHabilitado = "[plugins.\"analise@pmvni-kit\"]\nenabled = true\n";
        var agentesPreparados = false;

        async Task PrepararAgentesAsync()
        {
            if (agentesPreparados) return;
            // Nunca mexe no cofre nem nos ajustes de verdade do usuário: a pasta de dados do roteiro tem de estar em tests\saida.
            var saidaDeTestes = Path.GetFullPath(Path.Combine(pasta, ".."));
            if (!Path.GetFullPath(PastaDados.Raiz).StartsWith(saidaDeTestes + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A pasta de dados (" + PastaDados.Raiz + ") não está em " + saidaDeTestes + ": o roteiro não toca o cofre nem os ajustes de verdade.");
            if (!File.Exists(agenteFalso)) throw new InvalidOperationException("O agente falso não foi publicado em " + agenteFalsoPasta + " (tools\\roteiro.ps1 o publica).");
            // A trava do roteiro inteiro (agente falso + soffice) já vale desde o passo 1: confere em vez de trocar a lista.
            if (ExecucaoProcesso.SomenteExecutaveisEm != agenteFalsoPasta) throw new InvalidOperationException("A trava de programas do roteiro não está ligada.");
            File.Copy(agenteFalso, Path.Combine(agenteFalsoPasta, "claude.exe"), overwrite: true);   // o mesmo programa faz o papel do claude.exe (e do python.exe)
            if (Directory.Exists(codexHome)) ApagarArvore(codexHome);
            // Tarefa 12: o editor roda o controlador do analise direto (o agente falso faz o papel do python.exe com o executar_analise_62.py).
            var scriptsAnalise = Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "scripts", "rotas", "codex");
            Directory.CreateDirectory(scriptsAnalise);
            File.WriteAllText(Path.Combine(scriptsAnalise, "executar_analise_62.py"), "# executar_analise_62.py fictício do roteiro\n");
            var scripts = Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "scripts", "rotas", "claude");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "pro_runtime.py"), "# pro_runtime.py fictício do roteiro\nraise ValueError('API paga desativada no analise-pro.')\n");
            // 1.1.0 (Gemini): o mesmo agente falso faz o papel do agy.exe (no PATH do roteiro) e do google_runtime.py do análise Google.
            File.Copy(agenteFalso, Path.Combine(agenteFalsoPasta, "agy.exe"), overwrite: true);
            var scriptsGoogle = Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "scripts", "rotas", "gemini");
            Directory.CreateDirectory(scriptsGoogle);
            File.WriteAllText(Path.Combine(scriptsGoogle, "google_runtime.py"), "# google_runtime.py fictício do roteiro\nROTA_VAR = 'PMVNI_ANALISE_GOOGLE_ROTA'\n");
            File.WriteAllText(configCodex, ConfigHabilitado);
            // Estado de partida: conta, programa padrão, sem chave (um roteiro anterior que caiu no meio não deixa nada).
            foreach (var pv in Enum.GetValues<Provedor>())
            {
                CofreChaves.Remover(PastaDados.Raiz, pv);
                await j.DefinirModoAsync(pv, ModoAcesso.Conta);
                await j.EscolherProgramaAsync(pv, null);
            }
            agentesPreparados = true;
        }

        await Passo("49_ajustes_ia", async p =>
        {
            await PrepararAgentesAsync();
            await j.MudarModuloAsync(Modulo.Ajustes);
            await j.AguardarAgentesAsync();
            var pag = j.AjustesPagina;
            var chatgpt = j.CartaoDoAgente(Provedor.ChatGpt);
            var claude = j.CartaoDoAgente(Provedor.Claude);
            var gemini = j.CartaoDoAgente(Provedor.Gemini);

            // (a) Os três cartões consultam conta, plugins e os programas locais ao abrir a categoria, antes de clicar em Testar.
            // Ajustes › Inteligência artificial (a página abre em Geral; os cartões ficam nesta categoria, auditoria §3.3).
            pag.MostrarCategoria(CategoriasAjustes.Ia);
            await j.AguardarAgentesAsync();
            await Esperar(300);
            pag.CartaoChatGpt.StartBringIntoView();
            await Esperar(300);
            var situacaoSozinha = chatgpt.TextoSituacao.Text;
            var secao = j.PaginaAjustesVisivel && pag.CategoriaAtual == CategoriasAjustes.Ia && pag.TituloAgentes.Text == "Inteligência artificial";
            var cartoes = chatgpt.Titulo.Text == "ChatGPT (Codex · análise, rota ChatGPT)" && claude.Titulo.Text == "Claude (Claude Code · análise, rota Claude)"
                && gemini.Titulo.Text == "Gemini (Antigravity · análise, rota Gemini)" && gemini.CaixaPerfil.Items.Count == TextosAgentes.OpcoesPerfilGemini.Count
                && chatgpt.TextoServico.Text == "Codex (ChatGPT)" && claude.TextoServico.Text == "Claude" && gemini.TextoServico.Text == "Antigravity (Gemini)"
                && new[] { chatgpt, claude, gemini }.All(c => c.ActualHeight > 0 && c.BotaoTestar.IsEnabled && c.OpcaoConta.IsChecked == true)
                && chatgpt.OpcaoConta.Content as string == "Usar minha conta (login do programa)"
                && (chatgpt.OpcaoChave.Content as string ?? "").StartsWith("Usar chave de API", StringComparison.Ordinal);
            var contas = chatgpt.TextoNomeConta.Text == TextosAgentes.NaoConectado && claude.TextoNomeConta.Text == "Conta gerenciada no Claude"
                && (string)claude.BotaoConta.Content == "Abrir Claude"
                && chatgpt.BotaoConta.Content as string == "Entrar" && chatgpt.TextoAssinatura.Text == "—"
                && new[] { chatgpt, claude, gemini }.All(c => c.SeloChave.Texto == TextosAgentes.NaoConfigurada);
            var plugins = chatgpt.SeloPluginAnalise.Texto == "Versão 4.0.0" && chatgpt.SeloPluginAnalise.Ok && claude.SeloPluginPro.Texto == "Versão 4.0.0"
                && claude.LinhaPluginPro.Visibility == Visibility.Visible && chatgpt.LinhaPluginPro.Visibility == Visibility.Collapsed
                && chatgpt.TextoPluginAnalise.Text.StartsWith("No Codex, habilitado · instalado ou atualizado em ", StringComparison.Ordinal)
                && j.AjustesAtuais.PluginsVistos.TryGetValue("chatgpt.analise", out var vista) && vista == "4.0.0";
            p["secao_e_texto"] = secao;
            p["tres_cartoes"] = cartoes;
            p["contas_locais"] = contas;
            p["plugins_do_kit"] = plugins;
            p["situacao_sozinha"] = situacaoSozinha;

            // (b) As peças achadas são só as do agente falso (o codex e o claude de verdade nunca são procurados nem iniciados).
            var pecas = j.PecasAgentes;
            bool Dentro(string? c) => c is not null && Path.GetFullPath(c).StartsWith(agenteFalsoPasta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            var soFalso = Dentro(pecas.Codex) && Dentro(pecas.Claude) && Dentro(pecas.Python) && Dentro(pecas.Agy) && !pecas.PythonPeloLauncher
                && pecas.PluginAnaliseHabilitado && pecas.VersaoPluginAnalise == "4.0.0" && pecas.VersaoAnalisePro == "4.0.0" && !pecas.ApiPermitidaNoKit;
            var foraDaLista = Path.Combine(pasta, "programas-bloqueados", "fora-da-lista.exe");
            string? bloqueio = null;
            try { await ExecucaoProcesso.RodarAsync(foraDaLista, [], pasta, _ => { }, TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch (InvalidOperationException e) { bloqueio = e.Message; }
            var travado = ExecucaoProcesso.SomenteExecutaveisEm == agenteFalsoPasta && bloqueio == "Programa fora da pasta permitida: " + foraDaLista;
            var claudeExterno = Path.Combine(pasta, "programas-bloqueados", "claude.exe");
            var catalogo = new CatalogoModelosIa(Path.Combine(pasta, "catalogo-bloqueado"), () => codexHome);
            var falhasCatalogo = await catalogo.AtualizarAsync(pecas with { Claude = claudeExterno }, [FonteIa.ClaudeCode], CancellationToken.None);
            var catalogoTravado = falhasCatalogo.GetValueOrDefault(FonteIa.ClaudeCode) == "Programa fora da pasta permitida: " + claudeExterno;
            p["so_o_agente_falso"] = soFalso;
            p["trava_de_programas"] = travado;
            p["trava_do_catalogo_claude"] = catalogoTravado;

            // (c) Testar o ChatGPT e o Claude (só comandos locais, no agente falso).
            const string LinhaChatGpt = "Codex 0.99.0 · conectado com a conta do ChatGPT · plugin analise 4.0.0 habilitado";
            var rChatGpt = await j.TestarAgenteAsync(Provedor.ChatGpt);
            var testouChatGpt = rChatGpt.Linha == LinhaChatGpt && chatgpt.TextoSituacao.Text == LinhaChatGpt && j.LinhaSituacaoAgente(Provedor.ChatGpt) == LinhaChatGpt
                && rChatGpt.Avisos.Count == 0 && chatgpt.BotaoTestar.IsEnabled;
            var versaoDoKit = LerVersaoDoKit(j.PastaKitEfetiva) ?? "não encontrado";
            var rClaude = await j.TestarAgenteAsync(Provedor.Claude);
            var linhaClaude = $"Claude Code 2.9.0 · conectado com a conta do Claude · Análise — rota Claude 4.0.0 (kit: {versaoDoKit}) · Python 3.14.0";
            var testouClaude = rClaude.Linha == linhaClaude && claude.TextoSituacao.Text == linhaClaude;
            p["linha_chatgpt"] = rChatGpt.Linha;
            p["linha_claude"] = rClaude.Linha;
            p["avisos_claude"] = new JsonArray(rClaude.Avisos.Select(a => (JsonNode)a).ToArray());
            p["testar"] = testouChatGpt && testouClaude;
            var consultouSozinho = situacaoSozinha == LinhaChatGpt;

            // (d) Claude + chave de API (2026-11-17): habilitado, com a dica de que o análise pro segue pela conta; o modo muda e volta.
            await j.DefinirModoAsync(Provedor.Claude, ModoAcesso.Chave);
            var claudeSoConta = claude.OpcaoChave.IsEnabled && claude.TextoDicaChave.Text == TextosAgentes.ClaudeSoConta
                && j.ModoDoAgente(Provedor.Claude) == ModoAcesso.Chave && j.MensagemDoAgente(Provedor.Claude) is null && claude.OpcaoChave.IsChecked == true
                && chatgpt.OpcaoChave.IsEnabled;
            await j.DefinirModoAsync(Provedor.Claude, ModoAcesso.Conta);
            claudeSoConta &= j.ModoDoAgente(Provedor.Claude) == ModoAcesso.Conta;
            p["claude_so_conta"] = claudeSoConta;

            // (e) Programa que não existe (ou que não é .exe): mensagem exata e o programa não muda.
            const string Invalido = "Escolha um programa .exe que exista.";
            await j.EscolherProgramaAsync(Provedor.ChatGpt, Path.Combine(pasta, "nao-existe.exe"));
            var recusouInexistente = j.MensagemDoAgente(Provedor.ChatGpt) == Invalido && chatgpt.TextoMensagem.Text == Invalido && j.ProgramaEscolhidoDoAgente(Provedor.ChatGpt) is null;
            await j.EscolherProgramaAsync(Provedor.ChatGpt, configCodex);
            var recusouNaoExe = j.MensagemDoAgente(Provedor.ChatGpt) == Invalido && j.ProgramaEscolhidoDoAgente(Provedor.ChatGpt) is null;
            await j.EscolherProgramaAsync(Provedor.ChatGpt, agenteFalso);   // um programa que existe vale e "Usar o padrão" volta
            var escolheu = j.MensagemDoAgente(Provedor.ChatGpt) is null && j.ProgramaEscolhidoDoAgente(Provedor.ChatGpt) == agenteFalso && chatgpt.BotaoProgramaPadrao.IsEnabled
                && chatgpt.TextoPrograma.Text.EndsWith("(escolhido)", StringComparison.Ordinal);
            await j.EscolherProgramaAsync(Provedor.ChatGpt, null);
            var padrao = j.ProgramaEscolhidoDoAgente(Provedor.ChatGpt) is null && !chatgpt.BotaoProgramaPadrao.IsEnabled && chatgpt.TextoPrograma.Text.EndsWith("(padrão)", StringComparison.Ordinal);
            p["programa_invalido_recusado"] = recusouInexistente && recusouNaoExe;
            p["programa_escolhido_e_padrao"] = escolheu && padrao;

            // (f) Plugin desabilitado no config.toml do Codex: a linha diz; ao voltar, volta.
            File.WriteAllText(configCodex, "[plugins.\"analise@pmvni-kit\"]\nenabled = false\n");
            var desabilitado = (await j.TestarAgenteAsync(Provedor.ChatGpt)).Linha;
            File.WriteAllText(configCodex, ConfigHabilitado);
            var reabilitado = (await j.TestarAgenteAsync(Provedor.ChatGpt)).Linha;
            p["linha_plugin_desabilitado"] = desabilitado;
            p["plugin_desabilitado_e_de_volta"] = desabilitado == "Codex 0.99.0 · conectado com a conta do ChatGPT · plugin analise não habilitado no Codex" && reabilitado == LinhaChatGpt;

            await Esperar(300);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "49-ajustes-ia-xaml.png"));

            // (g) Testar é só leitura e roda FORA da fila de comandos: com um agente falso que não responde (--version dorme), Gravar e Exportar o relatório aberto
            // andam na hora; o botão desabilita já no clique e um segundo pedido devolve o mesmo teste; o teste termina sozinho em 15 s com a linha "não respondeu".
            var trava = Path.Combine(pasta, "agente-trava.json");
            File.WriteAllText(trava, "{ \"trava\": \"versao\" }");
            var pmvrelTeste = Path.Combine(pasta, "ia-testar.rpmvni");
            var pdfTeste = Path.Combine(pasta, "ia-testar.pdf");
            ApagarSe(pmvrelTeste); ApagarSe(pdfTeste);
            await j.MudarModuloAsync(Modulo.Relatorios);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), pmvrelTeste)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            Environment.SetEnvironmentVariable("AGENTE_FALSO_CENARIO", trava);
            bool travado2 = false, mesmoTeste = false, botaoNoClique = false, gravouNaHora = false, exportou = false;
            string linhaTravada = "";
            try
            {
                var teste = j.TestarAgenteAsync(Provedor.ChatGpt);
                botaoNoClique = !chatgpt.BotaoTestar.IsEnabled && chatgpt.TextoSituacao.Text == TextosAgentes.Testando;
                mesmoTeste = ReferenceEquals(teste, j.TestarAgenteAsync(Provedor.ChatGpt));
                await Esperar(1500);   // o programa já foi iniciado e está dormindo
                travado2 = !teste.IsCompleted;
                var relogio = System.Diagnostics.Stopwatch.StartNew();
                gravouNaHora = await j.GravarAgoraAsync() && relogio.ElapsedMilliseconds < 5000 && !teste.IsCompleted;
                var (okPdf, _, erroPdf, _) = await j.ExportarParaAsync(pdfTeste);
                exportou = okPdf && File.Exists(pdfTeste);
                p["exportar_com_teste_rodando"] = new JsonObject { ["exportou"] = exportou, ["teste_ainda_rodando"] = !teste.IsCompleted, ["erro"] = erroPdf };
                linhaTravada = (await teste).Linha;
            }
            finally { Environment.SetEnvironmentVariable("AGENTE_FALSO_CENARIO", null); }
            await j.FecharSessaoAsync();
            var terminou = linhaTravada == "Codex não verificado: não respondeu em 15 s" && chatgpt.TextoSituacao.Text == linhaTravada && chatgpt.BotaoTestar.IsEnabled;
            p["linha_do_teste_que_travou"] = linhaTravada;
            p["testar_fora_da_fila"] = new JsonObject { ["botao_desabilita_no_clique"] = botaoNoClique, ["mesmo_teste_se_repetir"] = mesmoTeste, ["rodando_enquanto_gravava"] = travado2, ["gravou_na_hora"] = gravouNaHora, ["terminou_com_nao_respondeu"] = terminou };
            var foraDaFila = botaoNoClique && mesmoTeste && travado2 && gravouNaHora && exportou && terminou;
            p["ok"] = secao && cartoes && contas && plugins && consultouSozinho && foraDaFila && soFalso && travado && catalogoTravado && testouChatGpt && testouClaude && claudeSoConta && recusouInexistente && recusouNaoExe && escolheu && padrao
                && desabilitado.EndsWith("plugin analise não habilitado no Codex", StringComparison.Ordinal) && reabilitado == LinhaChatGpt;
        });

        await Passo("50_chave_api_protegida", async p =>
        {
            await PrepararAgentesAsync();
            await j.MudarModuloAsync(Modulo.Ajustes);
            await j.AguardarAgentesAsync();
            var chatgpt = j.CartaoDoAgente(Provedor.ChatGpt);
            perguntas.Confirmar = true;
            var chave = "chave-falsa-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));   // aleatória, só deste roteiro; nunca vai para o resultado
            chavesDoRoteiro.Add(chave);
            var trechos = TrechosDaChave(chave);
            var dpapi = Path.Combine(PastaDados.Raiz, "segredos", "chatgpt.dpapi");
            var ajustesJson = Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo);
            string ModoNoArquivo() => JsonNode.Parse(File.ReadAllText(ajustesJson))!["agentes"]!["chatgpt"]!["modo"]!.GetValue<string>();

            // (a) ChatGPT › Usar chave de API › Guardar chave.
            await j.DefinirModoAsync(Provedor.ChatGpt, ModoAcesso.Chave);
            var painel = chatgpt.PainelChave.Visibility == Visibility.Visible && chatgpt.OpcaoChave.IsChecked == true && chatgpt.TextoChave.Text == TextosAgentes.SemChave && !chatgpt.BotaoRemoverChave.IsEnabled;
            chatgpt.CaixaChave.Password = chave;
            await j.GuardarChaveAsync(Provedor.ChatGpt, chatgpt.CaixaChave.Password);
            const string Guardada = "Chave guardada, protegida pelo Windows para este usuário.";
            var guardou = chatgpt.TextoChave.Text == Guardada && chatgpt.CaixaChave.Password.Length == 0 && File.Exists(dpapi) && chatgpt.BotaoRemoverChave.IsEnabled;
            var protegida = File.Exists(dpapi) && !ArquivoContem(dpapi, trechos);
            var ajustesTexto = File.ReadAllText(ajustesJson);
            var soModo = ModoNoArquivo() == "chave" && !ajustesTexto.Contains(chave, StringComparison.Ordinal) && !ajustesTexto.Contains("\"chave\":", StringComparison.Ordinal);
            p["painel_da_chave"] = painel;
            p["guardou_e_limpou_a_caixa"] = guardou;
            p["arquivo_protegido"] = protegida;
            p["ajustes_so_com_o_modo"] = soModo;

            // (b) Testar no modo chave só acrescenta o aviso (a chave só é conferida pelo provedor na primeira análise; nada é enviado).
            var teste = await j.TestarAgenteAsync(Provedor.ChatGpt);
            var avisoDaChave = teste.Avisos.Contains("Chave guardada; ela só é conferida pelo provedor na primeira análise.");
            p["aviso_no_teste"] = avisoDaChave;

            // (c) Remover: "Cancelar" mantém a chave; a confirmação "Remover" apaga o arquivo.
            var feitas = perguntas.Feitas.Count;
            perguntas.Confirmar = false;
            await j.RemoverChaveAsync(Provedor.ChatGpt);
            var manteve = File.Exists(dpapi) && chatgpt.TextoChave.Text == Guardada;
            perguntas.Confirmar = true;
            await j.RemoverChaveAsync(Provedor.ChatGpt);
            var perguntou = perguntas.Feitas.Skip(feitas).Count(f => f == "confirmar Remover a chave de API do ChatGPT?") == 2;
            var removeu = !File.Exists(dpapi) && chatgpt.TextoChave.Text == TextosAgentes.SemChave && !chatgpt.BotaoRemoverChave.IsEnabled;
            p["cancelar_mantem"] = manteve;
            p["remover_apaga"] = perguntou && removeu;

            // (d) Nenhuma ocorrência da chave (UTF-8, UTF-16, base64) nos arquivos do roteiro nem nos dados do app.
            var varredura = VarrerChave([pasta, PastaDados.Raiz], trechos);
            p["arquivos_varridos"] = varredura.Lidos;
            p["arquivos_trancados_pulados"] = varredura.Ilegiveis;
            p["chave_em_arquivos"] = varredura.Achados.Count;

            // (e) Volta para "Usar minha conta".
            await j.DefinirModoAsync(Provedor.ChatGpt, ModoAcesso.Conta);
            var voltou = j.ModoDoAgente(Provedor.ChatGpt) == ModoAcesso.Conta && chatgpt.OpcaoConta.IsChecked == true && chatgpt.PainelChave.Visibility == Visibility.Collapsed && ModoNoArquivo() == "conta";
            p["voltou_para_a_conta"] = voltou;
            p["ok"] = painel && guardou && protegida && soModo && avisoDaChave && manteve && perguntou && removeu && varredura.Achados.Count == 0 && varredura.Lidos > 0 && voltou;
        });

        // ---------- Tarefa 20: passos 51 a 56 (Novo relatório), só com o agente falso ----------
        // Sobre uma cópia da árvore fictícia em <pasta>\processos-agentes (Ajustes › Pasta de processos aponta para ela) e anexos fictícios gerados em <pasta>\anexos.
        // O orquestrador do roteiro lê o cenário do agente falso por AGENTE_FALSO_CENARIO e consulta a situação e o log a cada 200 ms.
        var processosAgentes = Path.Combine(pasta, "processos-agentes");
        var anexosPasta = Path.Combine(pasta, "anexos");
        var planta = Path.Combine(anexosPasta, "planta-ficticia.pdf");
        var requerimento = Path.Combine(anexosPasta, "requerimento-ficticio.pdf");
        var memorial = Path.Combine(anexosPasta, "memorial.docx");
        var foto = Path.Combine(anexosPasta, "foto.heic");
        var fotosPasta = Path.Combine(anexosPasta, "fotos");
        var atalho = Path.Combine(anexosPasta, "atalho.lnk");
        var outroAnexo = Path.Combine(anexosPasta, "complemento-ficticio.pdf");
        var claudeAnexo = Path.Combine(anexosPasta, "levantamento-ficticio.pdf");
        var recusadoAnexo = Path.Combine(anexosPasta, "nao-copiar.pdf");
        string P0001() => Directory.GetDirectories(Path.Combine(processosAgentes, "2099"), "0001 - 2099 - *").Single();
        string P0012() => Directory.GetDirectories(Path.Combine(processosAgentes, "2099"), "0012-2099 - *").Single();
        Dictionary<string, string> antesDoNovoRelatorio = new();
        Dictionary<string, (string Sha, long Tamanho, DateTime Escrita, DateTime Criacao, FileAttributes Atributos)> fotoDosAnexos = new();
        var novoRelatorioPreparado = false;

        OrquestradorAnalise Orq(string cenario) => new()
        {
            IntervaloSituacao = TimeSpan.FromMilliseconds(200),
            IntervaloLog = TimeSpan.FromMilliseconds(200),
            AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = Path.Combine(corpus, "agentes", cenario + ".json") },
        };

        static async Task<T> Prazo<T>(Task<T> tarefa, int segundos)
        {
            if (await Task.WhenAny(tarefa, Task.Delay(TimeSpan.FromSeconds(segundos))) != tarefa) throw new TimeoutException($"não terminou em {segundos} s");
            return await tarefa;
        }

        static string ShaDe(string arquivo) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(arquivo)));

        async Task PrepararNovoRelatorioAsync()
        {
            await PrepararAgentesAsync();
            if (novoRelatorioPreparado) return;
            if (Directory.Exists(processosAgentes)) ApagarArvore(processosAgentes);
            CopiarArvore(Path.Combine(corpus, "processos"), processosAgentes);
            j.DefinirPastaProcessos(processosAgentes);
            if (Directory.Exists(anexosPasta)) ApagarArvore(anexosPasta);
            Directory.CreateDirectory(fotosPasta);
            File.WriteAllBytes(planta, PdfFicticio("Planta baixa fictícia do roteiro"));
            File.WriteAllBytes(requerimento, PdfFicticio("Requerimento fictício do roteiro"));
            File.WriteAllBytes(outroAnexo, PdfFicticio("Complemento fictício do roteiro"));
            File.WriteAllBytes(claudeAnexo, PdfFicticio("Levantamento fictício do roteiro"));
            File.WriteAllBytes(recusadoAnexo, PdfFicticio("Anexo de uma análise recusada no aviso"));
            using (var zip = new System.IO.Compression.ZipArchive(File.Create(memorial), System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Memorial fictício</w:t></w:r></w:p></w:body></w:document>");
            File.WriteAllBytes(foto, [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 0, (byte)'m', (byte)'i', (byte)'f', (byte)'1', 1, 2, 3, 4, 5, 6, 7, 8]);
            File.WriteAllText(Path.Combine(fotosPasta, "f1.jpg"), "foto fictícia");
            File.WriteAllBytes(atalho, [0x4C, 0, 0, 0, 1, 0x14, 2, 0]);
            foreach (var a in Directory.GetFiles(anexosPasta, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(a);
                fotoDosAnexos[a] = (ShaDe(a), info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc, SemNuvem(info.Attributes));
            }
            antesDoNovoRelatorio = FotografarArvore(processosAgentes);
            j.EsquecerExecucoesClaude();
            foreach (var pv in Enum.GetValues<Provedor>()) j.EsquecerAviso(pv);
            novoRelatorioPreparado = true;
        }

        // Ruling FD-I3: o relatório da análise só abre sozinho sem relatório aberto (e com a vista da execução à frente); os passos que conferem a
        // abertura direta começam sem relatório aberto, para não dependerem do que o passo anterior deixou.
        async Task SemRelatorioAbertoAsync()
        {
            if (j.Sessao is not null && !await j.FecharSessaoAsync()) throw new InvalidOperationException("o relatório aberto não fechou: " + j.InfoMensagemAtual.Message);
        }

        async Task<(ExecucaoEmCurso E, FimAnalise Fim)> AnalisarAteOFimAsync(Provedor pv, string cenario)
        {
            j.Orquestrador = Orq(cenario);
            var e = await j.AnalisarAsync(pv) ?? throw new InvalidOperationException("a análise não partiu: " + j.InfoMensagemAtual.Message);
            return (e, await Prazo(j.AguardarAnaliseAsync(e), 180));
        }

        JsonObject Recebido(string pastaProcesso) => JsonNode.Parse(File.ReadAllText(Path.Combine(pastaProcesso, ".pmvni-ai", "agente-falso", "recebido.json")))!.AsObject();

        JsonObject ExecucaoJson(ExecucaoEmCurso e) => JsonNode.Parse(File.ReadAllText(Path.Combine(e.Registro.Pasta, "execucao.json")))!.AsObject();

        // Correção 2026-11-16: o relatório importado fica numa pasta DESPACHO NN - AAAA-MM-DD do processo (regra do kit).
        static bool NaEtapa(string pmvrel, string processo) =>
            string.Equals(Path.GetFullPath(Path.GetDirectoryName(Path.GetDirectoryName(pmvrel)!)!), Path.GetFullPath(processo), StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(pmvrel)!).StartsWith("DESPACHO ", StringComparison.Ordinal);
        static string[] Pmvrels(string processo) =>
            Directory.GetFiles(processo, "*.rpmvni").Concat(Directory.GetDirectories(processo, "DESPACHO *").SelectMany(d => Directory.GetFiles(d, "*.rpmvni"))).ToArray();

        string[] Chamadas(string pastaProcesso, string acao)
        {
            var arquivo = Path.Combine(pastaProcesso, ".pmvni-ai", "agente-falso", "chamadas.txt");
            return File.Exists(arquivo) ? File.ReadAllLines(arquivo, System.Text.Encoding.UTF8).Where(l => l.StartsWith(acao + "\t", StringComparison.Ordinal)).ToArray() : [];
        }

        if (somenteMonitor)
        {
            await Passo("monitor_html_controles", async p =>
            {
                await PrepararNovoRelatorioAsync();
                await j.MudarModuloAsync(Modulo.Relatorios);
                var n = j.NovoRelatorio;
                var web = (Microsoft.UI.Xaml.Controls.WebView2)n.FindName("WebMonitor");
                void Verificar(string nome, bool ok)
                {
                    p[nome] = ok;
                    if (!ok) throw new InvalidOperationException(nome);
                }
                async Task EsperarHtml(string expr) =>
                    Verificar(expr, await EsperarAteAsync(async () => web.CoreWebView2 != null &&
                        await web.ExecuteScriptAsync(expr) == "true", 15000));
                async Task AbrirPagina(string aba)
                {
                    await web.ExecuteScriptAsync($"document.querySelector('[data-name={aba}]').click()");
                    await EsperarHtml($"document.querySelector('article').ariaLabel === '{aba}'");
                }
                File.WriteAllText(configCodex, "model_provider = \"codex-lb\"\n" + ConfigHabilitado);
                var controlador = j.PecasAgentes.Analise62 ?? throw new InvalidOperationException("Controlador fictício ausente");
                var scriptsKit = Directory.GetParent(controlador)!.Parent!.Parent!.FullName;
                File.WriteAllText(Path.Combine(scriptsKit, "orcamento_lb.py"), "# consulta de teste");
                await j.NovoRelatorioAsync();
                Verificar("Cota por conta antes da análise", n.TextoContasChatGpt.Visibility == Visibility.Visible
                    && n.TextoContasChatGpt.Text.Contains("Conta fictícia · 5 h 80% livres · semana 15% livres"));
                File.WriteAllText(configCodex, ConfigHabilitado);
                foreach (var pausar in new[]{true,false})
                {
                    await j.NovoRelatorioAsync();
                    var pastaProcesso = pausar ? P0001() : P0012();
                    if (!pausar)
                    {
                        var documentos = Directory.CreateDirectory(Path.Combine(pastaProcesso,"DOCUMENTOS")).FullName;
                        File.Copy(requerimento,Path.Combine(documentos,"requerimento-teste.pdf"), overwrite:true);
                    }
                    await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == pastaProcesso));
                    var evidencias = Directory.CreateDirectory(Path.Combine(pastaProcesso,".pmvni-ai","cache-pdf")).FullName;
                    j.Orquestrador = Orq("lento");
                    perguntas.Confirmar = true;
                    var e = await j.AnalisarAsync(Provedor.ChatGpt) ?? throw new InvalidOperationException(j.InfoMensagemAtual.Message);
                    try
                    {
                        await EsperarHtml("!!document.querySelector('article .board')");
                        Invocar(n.BotaoMostrarRegistro);
                        Verificar("Abrir registro", await EsperarAteAsync(() => Task.FromResult(AberturaExterna.Registradas.Contains("explorador: " + e.Registro.Pasta)),3000));
                        await AbrirPagina("Etapas");
                        await web.ExecuteScriptAsync("document.querySelector('svg[aria-label=\"Ver registros\"]').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
                        await EsperarHtml("document.querySelector('article').ariaLabel === 'Log'");
                        await web.ExecuteScriptAsync("[...document.querySelectorAll('article button')].find(b=>b.textContent.includes('PDF')).click()");
                        Verificar("Evidências", await EsperarAteAsync(() => Task.FromResult(AberturaExterna.Registradas.Contains("explorador: " + evidencias)),3000));
                        await AbrirPagina("Detalhes");
                        await AbrirPagina("Consumo");
                        await AbrirPagina("Resumo");
                        if (pausar)
                        {
                            Invocar(n.BotaoNovaAnalise);
                            Verificar("Nova análise",await EsperarAteAsync(() => Task.FromResult(!n.VistaDaExecucao && !e.Fim.IsCompleted),3000));
                            Invocar(n.BotaoExecucoes);
                            await EsperarHtml("!!document.querySelector('article .board')");
                            Verificar("Retorno ao monitor",n.VistaDaExecucao);
                            Invocar(n.BotaoPausarAnalise);
                        }
                        else
                        {
                            perguntas.Confirmar = false;
                            Invocar(n.BotaoCancelarAnalise);
                            await Esperar(400);
                            Verificar("Recusar cancelamento mantém execução", !e.Fim.IsCompleted);
                            perguntas.Confirmar = true;
                            Invocar(n.BotaoCancelarAnalise);
                        }
                        var fim = await Prazo(j.AguardarAnaliseAsync(e),30);
                        Verificar(pausar ? "Pausa preserva retomada" : "Cancelamento confirmado",
                            fim.Situacao == "cancelada" && e.PausaSolicitada == pausar &&
                            (!pausar || n.BotaoContinuar.Visibility == Visibility.Visible || n.BotaoDescartar.Visibility == Visibility.Visible));
                        Verificar("Trava liberada",!File.Exists(Path.Combine(pastaProcesso,".pmvni-ai","editor","execucao.lock")));
                    }
                    finally { if (!e.Fim.IsCompleted) { perguntas.Confirmar = true; await j.CancelarAnaliseAsync(e); } }
                }
                await j.NovoRelatorioAsync();
                await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == P0012()));
                j.Orquestrador = Orq("ok");
                perguntas.Confirmar = true;
                var concluindo = await j.AnalisarAsync(Provedor.ChatGpt) ?? throw new InvalidOperationException(j.InfoMensagemAtual.Message);
                var pastaAviso = Directory.CreateDirectory(Path.Combine(P0012(), ".pmvni-ai", "analise", "validadores"));
                File.WriteAllText(Path.Combine(pastaAviso.FullName, "avisos.json"), """[{"regra":"palavra_repetida","onde":"itens[0]","texto":"item item"}]""");
                var entregue = await Prazo(j.AguardarAnaliseAsync(concluindo), 90);
                Verificar("Aviso registrado no processo aparece ao entregar", entregue.Situacao == "concluida"
                    && j.InfoMensagemAtual.IsOpen && j.InfoMensagemAtual.Title == "Avisos desta análise"
                    && j.InfoMensagemAtual.Message.Contains("palavra_repetida") && j.InfoMensagemAtual.Message.Contains("item item"));
                // A fonte fornece valores de teste ao mesmo modelo que recebe o progresso do kit.
                await j.NovoRelatorioAsync();
                n.MostrarExecucao(true);
                var ordem = new[] { "prepare","analyze","produce","final_review" };
                n.MostrarResumo(true,null,FluxoNovoRelatorio.Etapas("analyze","",ordem),"ChatGPT");
                n.MostrarProgresso(new ProgressoEtapa("analyze",1,7,28,Secoes:true));
                n.MostrarDetalhesDasEtapas(new DetalhesDasEtapas(
                    new Dictionary<string,DetalheEtapa>{["analyze"]=new("analyze",false,"modelo-teste","medium")},
                    [new(1,9,7,true,false)],[]){Plano=ordem,DescricoesDoPlano=new Dictionary<string,string>{["analyze"]="Saídas previstas: manifesto.json"},Registros=new Dictionary<string,string>{["analyze"]="Registro de teste"}});
                var agora = DateTimeOffset.Now;
                n.MostrarEstimativa(new EstimativaAnalise("analyze",25,40,agora.AddMinutes(30),PeloEditor:true,Inicial:true));
                n.MostrarRelogios(TimeSpan.FromMinutes(20),agora);
                n.MostrarAchados(Enumerable.Range(1,6).Select(i=>new AchadoExecucao("ID"+i,i,"Achado "+i,i==1?"NAO_ATENDE":"ATENDE",i==1)).ToArray());
                n.MostrarConsumo([new UsoEtapa("analyze","GPT","modelo-teste",1000,120,250,TimeSpan.FromMinutes(20),2,1)
                    { Usada5h=40,UsadaSemana=12,Plano="Plano específico da execução" }]);
                n.LigarLinhas(new System.Collections.ObjectModel.ObservableCollection<string>{ "Mensagem: registro sem hora", $"[{agora:O}] Ação: atividade com hora" });
                const string campo = "k=>document.querySelector('article [data-field='+k+'] tspan')?.textContent";
                await AbrirPagina("Resumo");
                async Task Conferir(string rotulo,string teste)
                {
                    Verificar(rotulo,await EsperarAteAsync(async()=>await web.ExecuteScriptAsync("(()=>{const campo="+campo+";return ("+teste+")})()")=="true",5000));
                }
                await Conferir("Progresso geral 40%, tempo ativo 20 min e restante 30 min",
                    "campo('percent').includes('40%') && campo('elapsed').includes('20 min') && campo('eta').includes('30 min')");
                n.MostrarRelogios(TimeSpan.FromMinutes(21),agora.AddMinutes(1));
                await Conferir("Relógio e tempo restante avançam juntos","campo('elapsed').includes('21 min') && campo('eta').includes('29 min')");
                await Conferir("Classificações e prévia de quatro achados","campo('ok')==='5' && campo('fail')==='1' && document.querySelectorAll('article [data-field=verification]').length===4");
                await web.ExecuteScriptAsync("document.querySelector('[aria-label=\"Filtrar descobertas\"]').dispatchEvent(new MouseEvent('click',{bubbles:true}));document.querySelector('.filters button:last-child').click()");
                await Conferir("Filtro de classificação","document.querySelectorAll('article [data-field=verification]').length===4 && campo('count')==='5 registros'");
                await web.ExecuteScriptAsync("[...document.querySelectorAll('article .link')].find(b=>b.textContent.includes('Ver todos')).click()");
                await Conferir("Ver todos os registros","document.querySelectorAll('article [data-field=verification]').length===5");
                await web.ExecuteScriptAsync("document.querySelector('article .card svg').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
                await Conferir("Abertura do achado","document.querySelector('dialog').open && document.querySelector('dialog h2').textContent.includes('Achado')");
                await web.ExecuteScriptAsync("document.getElementById('close').click()");
                await AbrirPagina("Etapas");
                await Conferir("Etapa 25%, sete de 28 e frente sete de nove","campo('percent')==='25%' && campo('result').includes('7 de 28') && campo('count0')==='7 de 9'");
                await web.ExecuteScriptAsync("document.querySelector('article [aria-expanded=true]').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
                await Conferir("Recolher etapa","!document.querySelector('article [aria-expanded=true]')");
                await AbrirPagina("Detalhes");
                await Conferir("Ordem do plano, registro e plano do executor","[...document.querySelectorAll('.detail-table tbody th')].map(n=>n.textContent.trim().slice(2).trim()).join('|')==='Preparo|Análise|Produção do relatório|Revisão final' && [...document.querySelectorAll('.detail-items li')].some(n=>n.textContent==='manifesto.json') && document.querySelector('article').innerText.includes('Plano específico da execução') && document.querySelector('article').innerText.includes('Registro de teste')");
                await AbrirPagina("Consumo");
                await Conferir("Cache 25%, saída 12%, cotas 40% e 12%","campo('cachePercent')==='25%' && campo('outputHelp').includes('12%') && campo('shortPercent')==='40%' && campo('weeklyPercent')==='12%'");
                await web.ExecuteScriptAsync("document.querySelector('article svg[aria-label*=modelo-teste]').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
                await Conferir("Detalhes da medição por etapa","document.querySelector('dialog').open && document.querySelector('dialog h2').textContent.includes('modelo-teste')");
                await web.ExecuteScriptAsync("document.getElementById('close').click()");
                n.MostrarConsumo([]);
                await Conferir("Sem telemetria não inventa percentuais","campo('cachePercent')==='—' && campo('shortPercent')==='—' && campo('weeklyPercent')==='—'");
                await AbrirPagina("Log");
                await Conferir("Log cronológico, horário ausente e evento com hora","campo('count')==='2 eventos registrados' && campo('time')==='—' && document.querySelectorAll('article [data-field=time] tspan')[1].textContent.includes(':')");
                await web.ExecuteScriptAsync("document.querySelector('article .card svg[role=button]').dispatchEvent(new MouseEvent('click',{bubbles:true}))");
                await Conferir("Abrir e ler evento completo","document.querySelector('dialog').open && document.querySelector('dialog p').textContent.includes('sem hora')");
                await web.ExecuteScriptAsync("document.getElementById('close').click()");
                n.ReiniciarEstimativa();
                n.MostrarRelogios(TimeSpan.Zero,agora);
                await AbrirPagina("Resumo");
                await Conferir("Sem previsão informa que aguarda a primeira etapa","campo('eta')==='Após a 1ª etapa'");
                n.MostrarEstimativa(new EstimativaAnalise("analyze",25,40,null,PeloEditor:true));
                n.MostrarRelogios(TimeSpan.FromMinutes(20),agora);
                await Conferir("Sem histórico suficiente, o percentual do roteiro não extrapola um horário","campo('eta')==='Histórico insuficiente'");
                n.ReiniciarEstimativa();
                n.MostrarResumo(false,null,FluxoNovoRelatorio.Etapas(null,"CONCLUIDO",ordem));
                n.MostrarRelogios(TimeSpan.FromMinutes(22),agora.AddMinutes(2));
                await AbrirPagina("Resumo");
                await Conferir("Todas as etapas concluídas e 100%","campo('percent')==='100%' && campo('status')==='Análise concluída' && campo('eta')==='Concluída'");
            });
            saida["ok"] = falhas.Count == 0;
            Gravar(args.Roteiro!, saida);
            Application.Current.Exit();
            return;
        }

        await Passo("51_novo_relatorio_anexos", async p =>
        {
            await PrepararNovoRelatorioAsync();
            await j.MudarModuloAsync(Modulo.Relatorios);
            await j.ExecutarComandoAsync(ComandosApp.RelatoriosNovo);   // pela faixa de Relatórios
            await j.AguardarNovoRelatorioAsync();
            var abriu = j.ModuloAtual == Modulo.Relatorios && j.PaginaNovoRelatorioVisivel && !j.FolhasVisiveis
                // 2026-11-16: Novo relatório é o botão verde (primário), com Abrir verde ao lado; não se repete na aba Arquivo.
                && j.FaixaAtual.Definicao!.Primario?.Id == ComandosApp.RelatoriosNovo && j.FaixaAtual.Definicao.Secundario?.Id == ComandosApp.ArquivoAbrir;
            p["pagina_aberta_pela_faixa"] = abriu;

            // Criar processo… (9998/2099 "Fulána de Tál") e, de novo, o mesmo número: nenhuma pasta nova e o aviso exato.
            perguntas.NovoProcesso = new DadosNovoProcesso(9998, 2099, "Fulána de Tál");
            var criado = await j.CriarProcessoAsync();
            var pastaNova = Path.Combine(processosAgentes, "2099", "9998 - 2099 - FULANA DE TAL");
            var criou = criado?.Pasta == pastaNova && Directory.Exists(Path.Combine(pastaNova, "DOCUMENTOS")) && Directory.Exists(Path.Combine(pastaNova, "PROJETO", "INICIAL"))
                && j.ProcessoDoNovoRelatorio?.Pasta == pastaNova;
            var pastasAntes = Directory.GetDirectories(processosAgentes, "*", SearchOption.AllDirectories).Length;
            var repetido = await j.CriarProcessoAsync();
            var avisoRepetido = j.InfoMensagemAtual.Message;
            var naoDuplicou = repetido?.Pasta == pastaNova && Directory.GetDirectories(processosAgentes, "*", SearchOption.AllDirectories).Length == pastasAntes
                && avisoRepetido == $"O processo 9998/2099 já existe: {pastaNova}.";
            perguntas.NovoProcesso = null;
            p["criar_processo"] = criou;
            p["criar_de_novo_nao_duplica"] = naoDuplicou;
            p["aviso_ja_existe"] = avisoRepetido;

            // O 0001 (sem PROJETO\INICIAL): anexos pelo botão e arrastados (uma pasta e um atalho recusados).
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == P0001()));
            var antesDosAnexos = FotografarArvore(processosAgentes);
            perguntas.ArquivosParaAnexar = [planta, requerimento, memorial];
            await j.AnexarAsync();
            await j.SoltarArquivosAsync([foto, fotosPasta, atalho]);
            await j.SoltarArquivosAsync([foto]);    // o mesmo caminho duas vezes entra uma vez
            await j.AguardarNovoRelatorioAsync();
            var linhas = j.AnexosDoNovoRelatorio;
            var recusas = j.RecusasDoNovoRelatorio.ToList();
            await j.SoltarArquivosAsync([fotosPasta, atalho]);
            await j.AguardarNovoRelatorioAsync();
            recusas = j.RecusasDoNovoRelatorio.ToList();
            const string RecusaPasta = "Arraste arquivos, não pastas: fotos.";
            const string RecusaAtalho = "atalho.lnk é um atalho; anexe o arquivo de destino.";
            var quatro = linhas.Count == 4 && j.AnexosDoNovoRelatorio.Count == 4 && recusas.SequenceEqual([RecusaPasta, RecusaAtalho]);
            var padrao = j.AnexosDoNovoRelatorio.All(a => a.Destino == DestinoAnexo.ProjetoInicial)
                && j.AnexosDoNovoRelatorio[3].Previa == @"PROJETO\INICIAL\foto.heic";
            var semJuntar = j.OpcoesDeDestino.SequenceEqual(["Documento (DOCUMENTOS)", @"Projeto — primeira apresentação (PROJETO\INICIAL)", "Projeto — reapresentação (nova REVISÃO NN)"]);
            p["linhas"] = new JsonArray(j.AnexosDoNovoRelatorio.Select(a => (JsonNode)(Path.GetFileName(a.Origem) + " → " + a.Previa)).ToArray());
            p["recusas"] = new JsonArray(recusas.Select(r => (JsonNode)r).ToArray());
            p["quatro_linhas_e_duas_recusas"] = quatro;
            p["destino_padrao_primeira_apresentacao"] = padrao;
            p["sem_juntar_a_revisao_atual"] = semJuntar;

            j.DefinirDestino(0, DestinoAnexo.ReapresentacaoNova);
            j.DefinirDestino(2, DestinoAnexo.ReapresentacaoNova);
            j.DefinirDestino(1, DestinoAnexo.Documento);
            await j.AguardarNovoRelatorioAsync();
            var a2 = j.AnexosDoNovoRelatorio;
            var previas = a2[0].Previa == @"PROJETO\REVISÃO 01\planta-ficticia.pdf" && a2[2].Previa == @"PROJETO\REVISÃO 01\memorial.docx"
                && a2[1].Previa == @"DOCUMENTOS\requerimento-ficticio.pdf" && a2[3].Previa == @"PROJETO\INICIAL\foto.heic";
            p["previas"] = new JsonArray(a2.Select(a => (JsonNode)a.Previa).ToArray());
            p["previas_por_destino"] = previas;

            // Nada é gravado antes do clique: a lista de arquivos e o sha de cada um são os mesmos.
            var depois = FotografarArvore(processosAgentes);
            var nadaGravado = depois.Count == antesDosAnexos.Count && depois.All(kv => antesDosAnexos.TryGetValue(kv.Key, out var s) && s == kv.Value);
            p["nada_gravado_antes_de_analisar"] = nadaGravado;
            await Esperar(300);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "51-novo-relatorio-xaml.png"));
            p["ok"] = abriu && criou && naoDuplicou && quatro && padrao && semJuntar && previas && nadaGravado;
        });

        await Passo("52_novo_relatorio_chatgpt", async p =>
        {
            await PrepararNovoRelatorioAsync();
            await SemRelatorioAbertoAsync();
            var processo = P0001();
            if (j.ProcessoDoNovoRelatorio?.Pasta != processo || j.AnexosDoNovoRelatorio.Count != 4)
            {
                // O passo 51 falhou no meio: refaz aqui o que ele deixa (o 0001 escolhido, os 4 anexos e os destinos), para este passo não depender dele.
                await j.MudarModuloAsync(Modulo.Relatorios);
                await j.NovoRelatorioAsync();
                await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == processo));
                perguntas.ArquivosParaAnexar = [planta, requerimento, memorial];
                await j.AnexarAsync();
                await j.SoltarArquivosAsync([foto]);
                await j.AguardarNovoRelatorioAsync();
                j.DefinirDestino(0, DestinoAnexo.ReapresentacaoNova);
                j.DefinirDestino(2, DestinoAnexo.ReapresentacaoNova);
                j.DefinirDestino(1, DestinoAnexo.Documento);
                await j.AguardarNovoRelatorioAsync();
                p["estado_refeito"] = true;
            }
            j.PedidoTexto = "Confira a reapresentação fictícia.\nVeja as pendências do despacho anterior.";
            var texto = j.PedidoTexto;
            var motivo = j.MotivoBotao(Provedor.ChatGpt);
            var confirmacoes = perguntas.Confirmacoes.Count;
            j.Orquestrador = Orq("ok");
            var e = await j.AnalisarAsync(Provedor.ChatGpt) ?? throw new InvalidOperationException("não partiu: " + j.InfoMensagemAtual.Message);
            var avisos = perguntas.Confirmacoes.Skip(confirmacoes).Count(c => c.Titulo == "Analisar com o ChatGPT" && c.Sim == "Concordar e analisar" && c.Texto == FluxoNovoRelatorio.TextoAviso(Provedor.ChatGpt));
            var avisoGravado = JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["agentes"]?["chatgpt"]?["aviso_versao"]?.GetValue<int>() == 1;
            var titulo = j.TituloAnalise ?? "";
            var vista = j.ExecucaoMostrada == e && j.NovoRelatorio.VistaDaExecucao && j.BotaoDoTituloVisivel;
            var fim = await Prazo(j.AguardarAnaliseAsync(e), 120);
            p["fim"] = fim.Situacao + (fim.Mensagem is null ? "" : ": " + fim.Mensagem);
            p["titulo_durante"] = titulo;
            // Tarefa 12: a tela mostra a rota do Codex usada e o que o controlador do kit escreve; JSON (como os eventos do cenário) não aparece.
            var linhas = e.Linhas.Count >= 1 && e.Linhas[0].StartsWith("Rota do Codex: ", StringComparison.Ordinal) && !e.Linhas.Any(l => l.StartsWith('{'));
            p["linhas"] = new JsonArray(e.Linhas.Take(10).Select(l => (JsonNode)l).ToArray());

            // Os anexos copiados (sha = origem) e a origem intacta.
            bool Copia(string relativo, string origem) => File.Exists(Path.Combine(processo, relativo)) && ShaDe(Path.Combine(processo, relativo)) == ShaDe(origem);
            var copiados = Copia(@"PROJETO\REVISÃO 01\planta-ficticia.pdf", planta) && Copia(@"PROJETO\REVISÃO 01\memorial.docx", memorial)
                && Copia(@"PROJETO\INICIAL\foto.heic", foto) && Copia(@"DOCUMENTOS\requerimento-ficticio.pdf", requerimento)
                && Directory.GetFiles(Path.Combine(processo, "PROJETO", "REVISÃO 01")).Length == 2;
            p["anexos_copiados"] = copiados;

            // O registro: pedido.md (o texto digitado), anexos.json (4), saida.jsonl, situacoes.jsonl e execucao.json; diretrizes.md no controle do analise.
            var reg = e.Registro.Pasta;
            var anexosJson = JsonNode.Parse(File.ReadAllText(Path.Combine(reg, "anexos.json")))!.AsArray();
            var arquivados = anexosJson.Select(x => new ArquivoArquivado(x!["origem"]!.GetValue<string>(), x["destino"]!.GetValue<string>(), x["sha256"]!.GetValue<string>(),
                x["tamanho"]!.GetValue<long>(), x["situacao"]!.GetValue<string>() == "JA_ARQUIVADO")).ToList();
            var pedido = File.ReadAllText(Path.Combine(reg, "pedido.md"), System.Text.Encoding.UTF8);
            var diretrizes = File.ReadAllText(Path.Combine(processo, ".pmvni-ai", "analise", "diretrizes.md"), System.Text.Encoding.UTF8);
            var ex = ExecucaoJson(e);
            var registro = reg.StartsWith(Path.Combine(processo, ".pmvni-ai", "editor") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && pedido == texto && diretrizes == PedidoAgente.Diretrizes(texto)      // Tarefa 13: sem instruções de progresso para a IA
                && !Directory.Exists(e.Registro.PastaProgresso)
                && anexosJson.Count == 4 && File.Exists(Path.Combine(reg, "saida.jsonl")) && File.Exists(Path.Combine(reg, "situacoes.jsonl"))
                && ex["situacao"]?.GetValue<string>() == "concluida" && ex["modo"]?.GetValue<string>() == "conta" && ex["pmvrel"]?.GetValue<string>() is { Length: > 0 };
            p["registro"] = registro;

            // O agente falso (no papel do Python) recebeu a linha exata do controlador do analise, na pasta do processo, com as peças em --inputs e nada pela
            // entrada padrão: nenhum "codex exec" e nenhum pedido "Use $analise" (erros 1–3).
            var recebido = Recebido(processo);
            var argv = recebido["argv"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();
            var script = Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "scripts", "rotas", "codex", "executar_analise_62.py");
            var linhaExata = argv.Take(7).SequenceEqual(["-B", "-X", "utf8", script, "--case", processo, "--inputs"])
                && arquivados.All(a => argv.Contains(Path.Combine(processo, a.Relativo), StringComparer.OrdinalIgnoreCase))
                && string.Equals(recebido["cwd"]!.GetValue<string>().TrimEnd('\\'), processo, StringComparison.OrdinalIgnoreCase) && recebido["entrada"]!.GetValue<string>() == ""
                && !argv.Contains("exec") && (string?)ex["rota_codex_tecnica"] is "openai" or "codex-lb";
            p["linha_de_comando_exata"] = linhaExata;

            // O relatório importado e aberto em Relatórios, com a InfoBar exata.
            var s = j.Sessao;
            var importado = s is not null && NaEtapa(s.Caminho, processo) && Path.GetFileName(s.Caminho).StartsWith("Relatorio_0001_2099", StringComparison.Ordinal)
                && j.ModuloAtual == Modulo.Relatorios && j.FolhasVisiveis && s.Revisao.Projeto.Itens.Count == 3
                && j.InfoMensagem.Message == FluxoNovoRelatorio.TextoImportado(Provedor.ChatGpt, s.Caminho, fim.Pdf) && j.InfoMensagem.IsOpen;
            p["pmvrel"] = s is null ? null : Path.GetFileName(s.Caminho);
            p["infobar"] = j.InfoMensagem.Message;
            p["importado_e_aberto"] = importado;

            // De novo, com outro anexo: o aviso não é pedido outra vez; no fim, nada em andamento.
            await j.NovoRelatorioAsync();
            await j.SoltarArquivosAsync([outroAnexo]);
            await j.AguardarNovoRelatorioAsync();
            var antes = perguntas.Confirmacoes.Count;
            var abertoAntes = j.Sessao?.Caminho;
            var (e2, fim2) = await AnalisarAteOFimAsync(Provedor.ChatGpt, "ok");
            // Com um relatório aberto, o fim não troca nada: o .rpmvni novo é criado, "Relatório pronto" com "Abrir relatório", e só o botão o abre.
            var pronto = j.RelatorioPronto;
            var aviso2 = j.InfoMensagemAtual;
            var naoInterrompeu = abertoAntes is not null && j.Sessao?.Caminho == abertoAntes && pronto is not null && pronto != abertoAntes && File.Exists(pronto)
                && NaEtapa(pronto, processo) && aviso2.IsOpen && aviso2.Title == FluxoNovoRelatorio.RelatorioProntoTitulo
                && aviso2.Message == FluxoNovoRelatorio.TextoRelatorioPronto(Provedor.ChatGpt, pronto, fim2.Pdf) && aviso2.ActionButton is Microsoft.UI.Xaml.Controls.Button { Content: "Abrir relatório" }
                && j.RecentesNosAjustes.FirstOrDefault() == Path.GetFullPath(pronto);
            if (pronto is not null) await j.AbrirRelatorioProntoAsync(pronto);
            var abriuNoBotao = pronto is not null && j.Sessao?.Caminho == pronto && j.FolhasVisiveis;
            p["relatorio_pronto"] = new JsonObject { ["nao_interrompeu"] = naoInterrompeu, ["abriu_no_botao"] = abriuNoBotao, ["infobar"] = aviso2.Message };
            var semAviso = perguntas.Confirmacoes.Skip(antes).All(c => c.Titulo != "Analisar com o ChatGPT");
            var semAndamento = j.TituloAnalise is null && !j.BotaoDoTituloVisivel && j.ExecucoesEmCurso.Count == 0;
            p["aviso_nao_repetido"] = semAviso;
            p["segunda_analise"] = fim2.Situacao;
            p["ok"] = motivo is null && avisos == 1 && avisoGravado && titulo.StartsWith("Analisando com o ChatGPT · 0001/2099", StringComparison.Ordinal) && vista
                && fim.Situacao == "concluida" && linhas && copiados && registro && linhaExata && importado && semAviso && fim2.Situacao == "concluida" && semAndamento && naoInterrompeu && abriuNoBotao;
        });

        await Passo("53_novo_relatorio_chatgpt_chave", async p =>
        {
            await PrepararNovoRelatorioAsync();
            var processo = P0001();
            await j.NovoRelatorioAsync();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == processo));
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", chaveHerdada);
            var chave = "chave-falsa-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));   // aleatória, nunca vai para o resultado
            chavesDoRoteiro.Add(chave);
            try
            {
                // Conta: a chave herdada do ambiente do editor não chega ao agente.
                var (_, fimConta) = await AnalisarAteOFimAsync(Provedor.ChatGpt, "chave");
                var semChave = Recebido(processo)["chave"]!.GetValue<string>() == "CHAVE_AUSENTE";
                p["conta_sem_chave_herdada"] = semChave;

                // Chave guardada: só ela chega (8 hex do sha256, conferidos aqui).
                await j.DefinirModoAsync(Provedor.ChatGpt, ModoAcesso.Chave);
                await j.GuardarChaveAsync(Provedor.ChatGpt, chave);
                var (eChave, fimChave) = await AnalisarAteOFimAsync(Provedor.ChatGpt, "chave");
                var esperado = "CHAVE_PRESENTE " + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(chave)))[..8];
                var comChave = Recebido(processo)["chave"]!.GetValue<string>() == esperado && ExecucaoJson(eChave)["modo"]?.GetValue<string>() == "chave";
                p["chave_entregue_so_ao_agente"] = comChave;

                // Nenhuma ocorrência da chave nem do valor herdado em tests\saida (o .dpapi é o cofre e fica de fora).
                var saidaDeTestes = Path.GetFullPath(Path.Combine(pasta, ".."));
                var trechos = TrechosDaChave(chave).Concat(TrechosDaChave(chaveHerdada)).ToList();
                var varredura = VarrerChave([saidaDeTestes], trechos, ignorar: a => a.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase));
                p["arquivos_varridos"] = varredura.Lidos;
                p["arquivos_trancados_pulados"] = varredura.Ilegiveis;
                p["chave_em_arquivos"] = varredura.Achados.Count;

                // Remover a chave: o botão do ChatGPT diz por quê; volta para a conta.
                perguntas.Confirmar = true;
                await j.RemoverChaveAsync(Provedor.ChatGpt);
                var motivoSemChave = j.MotivoBotao(Provedor.ChatGpt);
                p["motivo_sem_chave"] = motivoSemChave;
                await j.DefinirModoAsync(Provedor.ChatGpt, ModoAcesso.Conta);
                p["ok"] = fimConta.Situacao == "concluida" && semChave && fimChave.Situacao == "concluida" && comChave && varredura.Achados.Count == 0 && varredura.Lidos > 0
                    && motivoSemChave == "Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta." && j.ModoDoAgente(Provedor.ChatGpt) == ModoAcesso.Conta;
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
                CofreChaves.Remover(PastaDados.Raiz, Provedor.ChatGpt);
            }
        });

        await Passo("54_novo_relatorio_claude", async p =>
        {
            await PrepararNovoRelatorioAsync();
            await SemRelatorioAbertoAsync();
            var processo = P0001();
            await j.NovoRelatorioAsync();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == processo));
            var textoSob = j.NovoRelatorio.TextoPedidoClaude.Text == "A rota Claude do plugin analise usa o Claude para analisar o processo. No campo “Pedido”, você pode indicar algo que merece atenção, como uma pendência de despacho anterior. A análise segue as etapas habituais.";
            await j.SoltarArquivosAsync([claudeAnexo]);
            await j.AguardarNovoRelatorioAsync();
            j.PedidoTexto = "Pedido guardado só no registro.";
            var iniciarAntes = Chamadas(processo, "iniciar").Length;
            var situacaoAntes = Chamadas(processo, "situacao").Length;
            var confirmacoes = perguntas.Confirmacoes.Count;
            j.Orquestrador = Orq("claude_ok");
            var e = await j.AnalisarAsync(Provedor.Claude) ?? throw new InvalidOperationException("não partiu: " + j.InfoMensagemAtual.Message);
            var aviso = perguntas.Confirmacoes.Skip(confirmacoes).Count(c => c.Titulo == "Analisar com o Claude" && c.Texto == FluxoNovoRelatorio.TextoAviso(Provedor.Claude)) == 1;
            var pendenteDurante = j.ExecucoesClaudePendentes.Count == 1;
            // As etapas vistas na tela enquanto a análise do kit anda.
            var etapasVistas = new List<string>();
            while (!e.Fim.IsCompleted)
            {
                if (e.Situacao?.Etapa is { } et && (etapasVistas.Count == 0 || etapasVistas[^1] != et)) etapasVistas.Add(et);
                await Esperar(100);
            }
            var linhasDoLog = e.Linhas.Count;
            var fim = await Prazo(j.AguardarAnaliseAsync(e), 120);
            p["fim"] = fim.Situacao + (fim.Mensagem is null ? "" : ": " + fim.Mensagem);
            p["etapas_vistas"] = new JsonArray(etapasVistas.Select(x => (JsonNode)x).ToArray());
            var iniciar = Chamadas(processo, "iniciar").Skip(iniciarAntes).ToArray();
            var argsIniciar = iniciar.Length == 1 ? JsonNode.Parse(iniciar[0].Split('\t')[1])!.AsArray().Select(a => a!.GetValue<string>()).ToArray() : [];
            var proRuntime = Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "scripts", "rotas", "claude", "pro_runtime.py");
            var linhaIniciar = argsIniciar.SequenceEqual(["-B", "-X", "utf8", proRuntime, "--case", processo, "iniciar"]);
            var situacoes = Chamadas(processo, "situacao").Length - situacaoAntes;
            p["situacao_chamadas"] = situacoes;
            var reg = e.Registro.Pasta;
            var registro = File.Exists(Path.Combine(reg, "situacoes.jsonl")) && File.ReadAllText(Path.Combine(reg, "pedido.md"), System.Text.Encoding.UTF8) == "Pedido guardado só no registro.";
            var s = j.Sessao;
            var importado = fim.Situacao == "concluida" && s is not null && NaEtapa(s.Caminho, processo)
                && fim.Documento == Path.Combine(processo, ".pmvni-ai", "analise-pro", "relatorio_dados.json") && j.FolhasVisiveis
                && j.InfoMensagem.Message == FluxoNovoRelatorio.TextoImportado(Provedor.Claude, s.Caminho, fim.Pdf);
            p["infobar"] = j.InfoMensagem.Message;
            var semPendente = j.ExecucoesClaudePendentes.Count == 0;
            var etapas = etapasVistas.Count >= 2 && etapasVistas[0] is "prepare" or "analyze" && etapasVistas[^1] == "final_review";

            // Objeções: a InfoBar com o rótulo e o erro do kit e "Continuar a análise", que dá outra partida.
            await j.NovoRelatorioAsync();
            var (eObj, fimObj) = await AnalisarAteOFimAsync(Provedor.Claude, "objecoes");
            var infoObj = j.NovoRelatorio.AvisoResultado;
            var objecoes = fimObj.Situacao == "falhou" && infoObj.IsOpen && infoObj.Message == "parou com objeções da revisão final: A revisão final apontou objeções ao relatório."
                && j.NovoRelatorio.BotaoContinuar.Visibility == Visibility.Visible;
            p["objecoes"] = infoObj.Message;
            var antesContinuar = Chamadas(processo, "iniciar").Length;
            var eCont = await j.ContinuarAnaliseAsync() ?? throw new InvalidOperationException("Continuar não partiu: " + j.InfoMensagemAtual.Message);
            await Prazo(j.AguardarAnaliseAsync(eCont), 120);
            var continuou = Chamadas(processo, "iniciar").Length == antesContinuar + 1;
            p["continuar_da_outra_partida"] = continuou;
            p["ok"] = textoSob && aviso && pendenteDurante && linhaIniciar && situacoes >= 2 && linhasDoLog > 0 && registro && importado && semPendente && etapas && objecoes && continuou;
        });

        // 1.1.0: o terceiro botão, de ponta a ponta na tela, com o agente falso no papel do análise Google (conferência, partida, etapas, importação),
        // o perfil da chave escolhido à mão, a confirmação do processo fictício e a pausa por cota com "Continuar a análise" da etapa pendente.
        await Passo("54b_novo_relatorio_gemini", async p =>
        {
            await PrepararNovoRelatorioAsync();
            await SemRelatorioAbertoAsync();
            var processo = P0001();
            string[] Col(string linha) => linha.Split('\t');
            await j.DefinirModoAsync(Provedor.Gemini, ModoAcesso.Conta);
            await j.DefinirPerfilGeminiAsync(null);
            await j.NovoRelatorioAsync();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == processo));
            var n = j.NovoRelatorio;
            var botao = (n.BotaoGemini.Content as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text == "Analisar com o Gemini"
                && j.BotaoAnaliseHabilitado(Provedor.Gemini) && j.BotaoAnaliseHabilitado(Provedor.ChatGpt) && j.BotaoAnaliseHabilitado(Provedor.Claude)
                && n.TextoPluginGemini.Text == "Pelo Antigravity · Análise — rota Gemini v4.0.0" && n.TextoPerfilGemini.Text == "Perfil: Conta Google (plano Ultra)";
            p["rotulo_do_botao"] = n.TextoPluginGemini.Text;

            // Conta Google: aviso na primeira vez, conferência antes da partida, as etapas na tela e o relatório importado.
            j.PedidoTexto = "Diretriz fictícia para o Gemini.";
            j.EsquecerAviso(Provedor.Gemini);
            var conferirAntes = Chamadas(processo, "conferir").Length;
            var iniciarAntes = Chamadas(processo, "iniciar").Length;
            var confirmacoes = perguntas.Confirmacoes.Count;
            j.Orquestrador = Orq("claude_ok");
            var e = await j.AnalisarAsync(Provedor.Gemini) ?? throw new InvalidOperationException("não partiu: " + j.InfoMensagemAtual.Message);
            var aviso = perguntas.Confirmacoes.Skip(confirmacoes).Count(c => c.Titulo == "Analisar com o Gemini" && c.Texto == FluxoNovoRelatorio.TextoAviso(Provedor.Gemini)) == 1;
            var pendenteDurante = j.ExecucoesClaudePendentes.Count == 1;
            var etapasVistas = new List<string>();
            var painelDasEtapas = false;
            var painelSemHalo = true;
            while (!e.Fim.IsCompleted)
            {
                if (e.Situacao?.Etapa is { } et && (etapasVistas.Count == 0 || etapasVistas[^1] != et)) etapasVistas.Add(et);
                painelSemHalo &= Trabalhando.BrilhoDe(n.PainelClaude) is not { HaloVisivel: true };
                painelDasEtapas |= ReferenceEquals(j.ExecucaoMostrada, e) && n.PainelClaude.Visibility == Visibility.Visible;
                await Esperar(100);
            }
            var fim = await Prazo(j.AguardarAnaliseAsync(e), 120);
            p["fim"] = fim.Situacao + (fim.Mensagem is null ? "" : ": " + fim.Mensagem);
            p["etapas_vistas"] = new JsonArray(etapasVistas.Select(x => (JsonNode)x).ToArray());
            var conferir = Chamadas(processo, "conferir").Skip(conferirAntes).Select(Col).ToArray();
            var iniciar = Chamadas(processo, "iniciar").Skip(iniciarAntes).Select(Col).ToArray();
            var googleRuntime = Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "scripts", "rotas", "gemini", "google_runtime.py");
            var partida = conferir.Length == 1 && iniciar.Length == 1
                && new[] { conferir[0], iniciar[0] }.All(c => c[2] == "CHAVE_AUSENTE" && c[3] == "conta" && c[4] == "conta_ultra" && c[5] == "-")
                && JsonNode.Parse(iniciar[0][1])!.AsArray().Select(a => a!.GetValue<string>()).SequenceEqual(["-B", "-X", "utf8", googleRuntime, "--case", processo, "iniciar"]);
            var execucao = JsonNode.Parse(File.ReadAllText(Path.Combine(e.Registro.Pasta, "execucao.json"), System.Text.Encoding.UTF8))!;
            var registro = (string?)execucao["provedor"] == "gemini" && (string?)execucao["autenticacao"] == "conta_google" && (string?)execucao["perfil"] == "conta_ultra"
                && (string?)execucao["plugin"] == "Análise — rota Gemini" && (string?)execucao["versao_do_plugin"] == "4.0.0"
                && File.ReadAllText(Path.Combine(processo, ".pmvni-ai", "analise-google", "diretrizes.md"), System.Text.Encoding.UTF8)
                    == "Diretriz fictícia para o Gemini.\n";      // só o texto do analista (Tarefa 13)
            var s = j.Sessao;
            var importado = fim.Situacao == "concluida" && s is not null && NaEtapa(s.Caminho, processo)
                && fim.Documento == Path.Combine(processo, ".pmvni-ai", "analise-google", "relatorio_dados.json") && j.FolhasVisiveis
                && j.InfoMensagem.Message == FluxoNovoRelatorio.TextoImportado(Provedor.Gemini, s.Caminho, fim.Pdf);
            p["infobar"] = j.InfoMensagem.Message;
            var semPendente = j.ExecucoesClaudePendentes.Count == 0;
            var etapas = etapasVistas.Count >= 2 && etapasVistas[0] is "prepare" or "analyze" && etapasVistas[^1] == "final_review" && painelDasEtapas && painelSemHalo;
            p["etapas_sem_halo"] = painelSemHalo;

            // Chave: sem perfil escolhido o botão explica; com o perfil, pede a chave; nada é presumido nem trocado.
            await SemRelatorioAbertoAsync();
            await j.NovoRelatorioAsync();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == processo));
            await j.DefinirModoAsync(Provedor.Gemini, ModoAcesso.Chave);
            var cartao = j.CartaoDoAgente(Provedor.Gemini);
            var semPerfil = j.MotivoBotao(Provedor.Gemini) == FluxoNovoRelatorio.SemPerfilApi && !j.BotaoAnaliseHabilitado(Provedor.Gemini)
                && cartao.PainelPerfil.Visibility == Visibility.Visible && cartao.CaixaPerfil.SelectedIndex == 0 && n.TextoPerfilGemini.Text == "Perfil da chave não escolhido"
                && j.BotaoAnaliseHabilitado(Provedor.ChatGpt) && j.BotaoAnaliseHabilitado(Provedor.Claude);
            await j.DefinirPerfilGeminiAsync(PerfilGemini.ApiGratuita);
            var semChave = j.MotivoBotao(Provedor.Gemini) == "Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta." && cartao.CaixaPerfil.SelectedIndex == 2
                && cartao.TextoMatriz.Text == TextosAgentes.MatrizDoGemini(ModoAcesso.Chave, PerfilGemini.ApiGratuita);
            var chave = "gemini-ficticia-roteiro-" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            await j.GuardarChaveAsync(Provedor.Gemini, chave);
            var comChave = j.MotivoBotao(Provedor.Gemini) is null && j.BotaoAnaliseHabilitado(Provedor.Gemini) && cartao.CaixaChave.Password.Length == 0;
            bool gratuitaRecusada, gratuitaAceita;
            try
            {
                // API gratuita: sem a confirmação de processo fictício, nada parte; com ela, a chave e o perfil vão só à conferência e à partida.
                iniciarAntes = Chamadas(processo, "iniciar").Length;
                confirmacoes = perguntas.Confirmacoes.Count;
                perguntas.Confirmar = false;
                var recusada = await j.AnalisarAsync(Provedor.Gemini);
                gratuitaRecusada = recusada is null && Chamadas(processo, "iniciar").Length == iniciarAntes
                    && perguntas.Confirmacoes.Skip(confirmacoes).Count(c => c.Titulo == TextosAgentes.FicticioTitulo && c.Texto == TextosAgentes.FicticioTexto) == 1;
                perguntas.Confirmar = true;
                j.Orquestrador = Orq("claude_ok");
                var eApi = await j.AnalisarAsync(Provedor.Gemini) ?? throw new InvalidOperationException("API gratuita não partiu: " + j.InfoMensagemAtual.Message);
                var fimApi = await Prazo(j.AguardarAnaliseAsync(eApi), 120);
                var iniciarApi = Chamadas(processo, "iniciar").Skip(iniciarAntes).Select(Col).ToArray();
                var sha8 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(chave)))[..8];
                var execucaoApi = JsonNode.Parse(File.ReadAllText(Path.Combine(eApi.Registro.Pasta, "execucao.json"), System.Text.Encoding.UTF8))!;
                gratuitaAceita = fimApi.Situacao == "concluida" && iniciarApi.Length == 1 && iniciarApi[0][2] == "CHAVE_PRESENTE " + sha8 && iniciarApi[0][3] == "api"
                    && iniciarApi[0][4] == "api_gratuita" && iniciarApi[0][5] == "1" && (string?)execucaoApi["autenticacao"] == "gemini_api" && (string?)execucaoApi["perfil"] == "api_gratuita"
                    && Chamadas(processo, "situacao").Select(Col).Where(c => c.Length > 4).All(c => c[2] == "CHAVE_AUSENTE")
                    && !File.ReadAllText(Path.Combine(eApi.Registro.Pasta, "execucao.json"), System.Text.Encoding.UTF8).Contains(chave, StringComparison.Ordinal)
                    && !File.ReadAllText(Path.Combine(PastaDados.Raiz, "ajustes.json"), System.Text.Encoding.UTF8).Contains(chave, StringComparison.Ordinal);
                p["api_gratuita"] = fimApi.Situacao + (fimApi.Mensagem is null ? "" : ": " + fimApi.Mensagem);
            }
            finally
            {
                perguntas.Confirmar = true;
                CofreChaves.Remover(PastaDados.Raiz, Provedor.Gemini);
                await j.DefinirModoAsync(Provedor.Gemini, ModoAcesso.Conta);
            }
            var perfilGuardado = j.PerfilApiDoGemini == PerfilGemini.ApiGratuita && j.PerfilDoGemini == PerfilGemini.ContaUltra;     // a escolha fica; a conta volta a valer

            // Pausa por cota numa etapa: o motivo do plugin fica à vista com "Continuar a análise", que retoma da etapa pendente.
            await SemRelatorioAbertoAsync();
            await j.NovoRelatorioAsync();
            var outro = P0012();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == outro));
            var cenarioPausa = Path.Combine(pasta, "gemini-pausa.json");
            File.WriteAllText(cenarioPausa, new JsonObject
            {
                ["relatorio"] = Path.Combine(corpus, "agentes", "relatorio_dados_0001.json"),
                ["auto"] = new JsonObject { ["etapa_ms"] = 200, ["final"] = "CONCLUIDO", ["pausar_na_etapa"] = "audit", ["motivo"] = "cota_conta", ["erro"] = "Cota da conta Google esgotada na etapa audit." },
            }.ToJsonString(), new System.Text.UTF8Encoding(false));
            OrquestradorAnalise OrqPausa() => new()
            {
                IntervaloSituacao = TimeSpan.FromMilliseconds(200), IntervaloLog = TimeSpan.FromMilliseconds(200),
                AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = cenarioPausa },
            };
            j.Orquestrador = OrqPausa();
            var ePausa = await j.AnalisarAsync(Provedor.Gemini) ?? throw new InvalidOperationException("não partiu: " + j.InfoMensagemAtual.Message);
            var fimPausa = await Prazo(j.AguardarAnaliseAsync(ePausa), 120);
            var infoPausa = n.AvisoResultado;
            string[] Feitas() => File.Exists(Path.Combine(outro, ".pmvni-ai", "agente-falso", "etapas.txt")) ? File.ReadAllLines(Path.Combine(outro, ".pmvni-ai", "agente-falso", "etapas.txt")) : [];
            var pausou = fimPausa.Situacao == "interrompida" && infoPausa.IsOpen && infoPausa.Title == "A análise com o Gemini parou"
                && infoPausa.Message.EndsWith("Cota da conta Google esgotada na etapa audit.", StringComparison.Ordinal)
                && n.BotaoContinuar.Visibility == Visibility.Visible && Feitas().SequenceEqual(["prepare", "analyze", "technical_audit", "produce"]);
            p["pausa"] = infoPausa.Message;
            j.Orquestrador = OrqPausa();
            var eCont = await j.ContinuarAnaliseAsync() ?? throw new InvalidOperationException("Continuar não partiu: " + j.InfoMensagemAtual.Message);
            var fimCont = await Prazo(j.AguardarAnaliseAsync(eCont), 120);
            var retomou = fimCont.Situacao == "concluida" && eCont.Provedor == Provedor.Gemini
                && Feitas().SequenceEqual(["prepare", "analyze", "technical_audit", "produce", "audit", "final_review"])      // cada etapa uma vez: nada concluído se repete
                && Chamadas(outro, "iniciar").Select(Col).All(c => c[3] == "conta" && c[4] == "conta_ultra");
            p["retomada"] = fimCont.Situacao + (fimCont.Mensagem is null ? "" : ": " + fimCont.Mensagem);
            await SemRelatorioAbertoAsync();
            p["partes"] = new JsonObject
            {
                ["botao"] = botao, ["aviso"] = aviso, ["pendente_durante"] = pendenteDurante, ["partida"] = partida, ["registro"] = registro, ["importado"] = importado,
                ["sem_pendente"] = semPendente, ["etapas"] = etapas, ["sem_perfil"] = semPerfil, ["sem_chave"] = semChave, ["com_chave"] = comChave,
                ["gratuita_recusada"] = gratuitaRecusada, ["gratuita_aceita"] = gratuitaAceita, ["perfil_guardado"] = perfilGuardado, ["pausou"] = pausou, ["retomou"] = retomou,
            };
            p["ok"] = botao && aviso && pendenteDurante && partida && registro && importado && semPendente && etapas && semPerfil && semChave && comChave
                && gratuitaRecusada && gratuitaAceita && perfilGuardado && pausou && retomou;
        });

        await Passo("55_novo_relatorio_segundo_plano_e_cancelar", async p =>
        {
            await PrepararNovoRelatorioAsync();
            var processo = P0001();
            var outro = P0012();     // a árvore fictícia não tem 0002: o segundo processo é o 0012
            await j.NovoRelatorioAsync();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == processo));
            var pmvrelAntes = Pmvrels(processo).Length;
            j.Orquestrador = Orq("lento");
            var e = await j.AnalisarAsync(Provedor.ChatGpt) ?? throw new InvalidOperationException("não partiu: " + j.InfoMensagemAtual.Message);
            await EsperarAteAsync(() => Task.FromResult(e.Linhas.Count > 0), 30000);
            var linhasAntes = e.Linhas.Count;

            // Trocar de módulo não interrompe a execução.
            await j.MudarModuloAsync(Modulo.Processos);
            await Esperar(600);
            await j.MudarModuloAsync(Modulo.Relatorios);
            var segue = !e.Fim.IsCompleted && j.ExecucoesEmCurso.Contains(e) && j.BotaoDoTituloVisivel && e.Linhas.Count >= linhasAntes;
            var motivo0001 = j.MotivoBotao(Provedor.ChatGpt);
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == outro));
            var motivo0012 = j.MotivoBotao(Provedor.ChatGpt);
            p["segue_em_segundo_plano"] = segue;
            p["motivo_no_0001"] = motivo0001;

            // Fechar com o ChatGPT rodando: "Continuar" não fecha e a análise segue.
            perguntas.RespostaPorTitulo["Pausar a análise e fechar?"] = false;
            var confirmacoes = perguntas.Confirmacoes.Count;
            var podeFechar = await j.PodeFecharComAnalisesAsync();
            perguntas.RespostaPorTitulo.Remove("Pausar a análise e fechar?");
            var perguntaFechar = perguntas.Confirmacoes.Skip(confirmacoes).FirstOrDefault(c => c.Titulo == "Pausar a análise e fechar?");
            var naoFechou = !podeFechar && !e.Fim.IsCompleted && perguntaFechar.Texto == "A análise com o ChatGPT em 0001/2099 será pausada. Ao abrir o editor de novo, você poderá retomá-la de onde parou."
                && perguntaFechar.Sim == FluxoNovoRelatorio.FecharSim && perguntaFechar.Nao == "Continuar";
            // A segunda checagem do fechamento (na fila, depois de um Analisar que estava copiando) só pergunta pelas execuções novas.
            var tratadas = new HashSet<ExecucaoEmCurso>();
            perguntas.RespostaPorTitulo["Pausar a análise e fechar?"] = false;
            var antesPrimeira = perguntas.Confirmacoes.Count;
            var fecharPrimeira = await j.PodeFecharComAnalisesAsync(tratadas);
            var perguntouPrimeira = perguntas.Confirmacoes.Count == antesPrimeira + 1;
            tratadas.Add(e);       // "Continuar" não marca; aqui simula a execução já perguntada nesta tentativa de fechar
            var antesSegunda = perguntas.Confirmacoes.Count;
            var fecharSegunda = await j.PodeFecharComAnalisesAsync(tratadas);
            perguntas.RespostaPorTitulo.Remove("Pausar a análise e fechar?");
            var segundaSoNovas = !fecharPrimeira && perguntouPrimeira && fecharSegunda && perguntas.Confirmacoes.Count == antesSegunda && !e.Fim.IsCompleted;
            p["segunda_checagem_so_novas"] = segundaSoNovas;

            // Cancelar (confirmado): em até 5 s nenhum processo do agente falso vivo, "cancelada", nenhum .rpmvni novo e a trava apagada.
            var pids = JsonNode.Parse(File.ReadAllText(Path.Combine(processo, ".pmvni-ai", "agente-falso", "pids.json")))!.AsObject()
                .Select(kv => kv.Value!.GetValue<int>()).ToList();
            perguntas.Confirmar = true;
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            var cancelou = await j.CancelarAnaliseAsync(e);
            var fim = await Prazo(j.AguardarAnaliseAsync(e), 30);
            var mortos = await EsperarAteAsync(() => Task.FromResult(pids.All(pid => !Vivo(pid))), 5000);
            p["cancelar_ms"] = relogio.ElapsedMilliseconds;
            var cancelado = cancelou && mortos && fim.Situacao == "cancelada" && ExecucaoJson(e)["situacao"]?.GetValue<string>() == "cancelada"
                && Pmvrels(processo).Length == pmvrelAntes && !File.Exists(Path.Combine(processo, ".pmvni-ai", "editor", "execucao.lock"));
            p["chatgpt_cancelado"] = cancelado;

            // Fechar pausa o Claude, preserva a retomada e encerra o processo do kit.
            j.Orquestrador = Orq("claude_lento");
            var eC = await j.AnalisarAsync(Provedor.Claude) ?? throw new InvalidOperationException("Claude não partiu: " + j.InfoMensagemAtual.Message);
            await EsperarAteAsync(() => Task.FromResult(eC.Situacao?.Codigo == "EM_ANDAMENTO" && eC.Situacao.Pid is not null), 60000);
            var pidKit = eC.Situacao?.Pid ?? 0;
            var antesDeFechar = perguntas.Confirmacoes.Count;
            var podeFecharClaude = await j.PodeFecharComAnalisesAsync();
            await Prazo(j.AguardarAnaliseAsync(eC), 30);
            var confirmou = perguntas.Confirmacoes.Skip(antesDeFechar).Any(c => c.Titulo == FluxoNovoRelatorio.FecharTitulo);
            var pendente = j.ExecucoesClaudePendentes.Count == 1 && j.ExecucoesClaudePendentes[0].Processo == outro;
            var encerrou = pidKit != 0 && await EsperarAteAsync(() => Task.FromResult(!Vivo(pidKit)), 5000);
            p["fechar_com_claude"] = new JsonObject { ["pode_fechar"] = podeFecharClaude, ["confirmou"] = confirmou, ["pendente"] = pendente, ["encerrou"] = encerrou };
            j.DescartarAnalisePendente();
            // (d) Ajustes editados à mão: um registro fora de <processo>\.pmvni-ai\editor é descartado com o motivo, e nada é gravado nele.
            var foraDoEditor = Path.Combine(pasta, "registro-falso");
            Directory.CreateDirectory(foraDoEditor);
            j.AdicionarPendenteClaude(new ExecucaoClaudePendente(outro, foraDoEditor));
            await j.RetomarExecucoesClaudeAsync();
            var descartou = j.ExecucoesClaudePendentes.Count == 0 && j.ExecucoesEmCurso.Count == 0 && !File.Exists(Path.Combine(foraDoEditor, "execucao.json"))
                && j.InfoMensagemAtual.Message.StartsWith("O registro da análise (" + foraDoEditor + ") não fica em ", StringComparison.Ordinal);
            p["registro_fora_do_editor_descartado"] = descartou;

            // (e) O Claude termina com o editor "fechado": reaberto, "Importar relatório"; a 1ª tentativa é cancelada (o relatório aberto tem um texto que a
            // conferência recusa e o usuário escolhe "Voltar à edição"): a pendência e o botão ficam; a 2ª importa e só então a pendência sai.
            var cenarioConcluido = Path.Combine(pasta, "claude-conclui-com-o-editor-fechado.json");
            // O relatório fictício do agente falso é relativo à pasta do cenário: este cenário fica no roteiro, então aponta para o do corpus.
            File.WriteAllText(cenarioConcluido, new JsonObject
            {
                ["auto"] = new JsonObject { ["etapa_ms"] = 1500, ["final"] = "CONCLUIDO" },
                ["relatorio"] = Path.Combine(corpus, "agentes", "relatorio_dados_0001.json"),
            }.ToJsonString());
            j.Orquestrador = new OrquestradorAnalise
            {
                IntervaloSituacao = TimeSpan.FromMilliseconds(200), IntervaloLog = TimeSpan.FromMilliseconds(200),
                AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = cenarioConcluido },
            };
            var eD = await j.AnalisarAsync(Provedor.Claude) ?? throw new InvalidOperationException("Claude não partiu: " + j.InfoMensagemAtual.Message);
            await EsperarAteAsync(() => Task.FromResult(eD.Situacao?.Codigo == "EM_ANDAMENTO"), 60000);
            // Simula um registro concluído ainda sem importação, como o de uma versão anterior.
            eD.Abandonada = true;
            await Prazo(j.AguardarAnaliseAsync(eD), 60);
            var statusKit = Path.Combine(outro, ".pmvni-ai", "analise-pro", "auto-status.json");
            await EsperarAteAsync(() =>
            {
                try { return Task.FromResult(SituacaoAnalisePro.Ler(File.ReadAllText(statusKit)).Codigo == "CONCLUIDO"); }
                catch (IOException) { return Task.FromResult(false); }
            }, 60000);
            string Ler(string f) { try { return File.Exists(f) ? File.ReadAllText(f) : "(não existe)"; } catch (IOException e) { return "(ilegível: " + e.Message + ")"; } }
            p["retomada_diagnostico_antes"] = new JsonObject
            {
                ["status_do_kit"] = Ler(statusKit), ["pendentes_nos_ajustes"] = j.ExecucoesClaudePendentes.Count, ["em_curso"] = j.ExecucoesEmCurso.Count,
                ["situacao_vista"] = eD.Situacao?.Codigo, ["fim"] = eD.Fim.IsCompleted ? eD.Fim.Result.Situacao : "(rodando)",
            };
            await j.RetomarExecucoesClaudeAsync();
            p["retomada_diagnostico_depois"] = new JsonObject
            {
                ["para_importar"] = j.PendentesParaImportar.Count, ["pendentes_nos_ajustes"] = j.ExecucoesClaudePendentes.Count, ["em_curso"] = j.ExecucoesEmCurso.Count,
                ["mensagem"] = j.InfoMensagemAtual.Message, ["resultado"] = j.NovoRelatorio.AvisoResultado.Message,
            };
            if (j.PendentesParaImportar.Count == 0) throw new InvalidOperationException("a retomada não ofereceu Importar relatório");
            var oferecido = j.PendentesParaImportar.Count == 1 && j.ExecucoesEmCurso.Count == 0 && j.NovoRelatorio.AvisoResultado.IsOpen
                && j.NovoRelatorio.AvisoResultado.Message == "A análise com o Claude de 0012/2099 terminou." && j.NovoRelatorio.BotaoImportarPendente.Visibility == Visibility.Visible;
            var aberto = Path.Combine(pasta, "retomada-aberto.rpmvni");
            ApagarSe(aberto);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), aberto)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var digitou = await DigitarAsync(j.Sessao!.Revisao.Projeto.Incluidos[0].Id, "   ");
            perguntas.FecharSemOTexto = false;
            var primeira = await j.ImportarPendenteAsync(j.PendentesParaImportar[0]);
            var ficou = !primeira && j.PendentesParaImportar.Count == 1 && j.ExecucoesClaudePendentes.Count == 1 && j.Sessao?.Caminho == aberto
                && (j.InfoMensagemAtual.ActionButton as Microsoft.UI.Xaml.Controls.Button)?.Content as string == "Importar relatório";
            perguntas.FecharSemOTexto = true;
            var segunda = await j.ImportarPendenteAsync(j.PendentesParaImportar[0]);
            perguntas.FecharSemOTexto = false;
            var importouRetomada = segunda && j.PendentesParaImportar.Count == 0 && j.ExecucoesClaudePendentes.Count == 0 && j.Sessao is { } sr
                && NaEtapa(sr.Caminho, outro) && JsonNode.Parse(File.ReadAllText(Path.Combine(eD.Registro.Pasta, "execucao.json")))?["pmvrel"]?.GetValue<string>() == sr.Caminho
                && j.InfoMensagem.Message == FluxoNovoRelatorio.TextoImportado(Provedor.Claude, sr.Caminho, Path.Combine(outro, ".pmvni-ai", "analise-pro", "relatorio.pdf"));
            p["retomada_concluida"] = new JsonObject
            {
                ["oferecido"] = oferecido, ["digitou"] = digitou, ["cancelada_fica"] = ficou, ["importou_na_segunda"] = importouRetomada, ["infobar"] = j.InfoMensagem.Message,
            };
            p["ok"] = segue && motivo0001 == "Já há uma análise em andamento neste processo." && motivo0012 is null && naoFechou && segundaSoNovas && cancelado
                && podeFecharClaude && confirmou && pendente && encerrou && descartou && oferecido && digitou && ficou && importouRetomada;
        });

        await Passo("56_novo_relatorio_falhas", async p =>
        {
            await PrepararNovoRelatorioAsync();
            var processo = P0001();
            await j.NovoRelatorioAsync();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == processo));
            var pmvrelAntes = Pmvrels(processo).Length;
            var resultados = new JsonObject();
            var avisos = new JsonObject();
            var tudo = true;
            foreach (var (cenario, conferir) in new (string, Func<ExecucaoEmCurso, FimAnalise, bool>)[]
            {
                ("sem_resultado", (e, f) => f.Situacao == "sem_resultado" && f.Mensagem == $"A análise terminou, mas não deixou relatório novo (delivery.json não foi atualizado). Veja o registro: {e.Registro.Pasta}."),
                ("falha", (e, f) => f.Situacao == "falhou" && f.Mensagem == $"O controlador do analise parou na etapa Revisão final: falhou de propósito. Veja o registro: {e.Registro.Pasta}."),
                ("json_invalido", (e, f) => f.Situacao == "sem_resultado" && f.Mensagem!.StartsWith("O delivery.json da análise não pôde ser lido: ", StringComparison.Ordinal)),
                ("fora_do_processo", (e, f) => f.Situacao == "sem_resultado" && f.Mensagem!.StartsWith("O relatório indicado pela análise está fora da pasta do processo: ", StringComparison.Ordinal)),
            })
            {
                var (e, fim) = await AnalisarAteOFimAsync(Provedor.ChatGpt, cenario);
                var n = j.NovoRelatorio;
                var info = n.AvisoResultado;
                var retomavel = OrquestradorAnalise.PodeRetomarChatGpt(processo, e.Registro.Pasta);
                var complemento = cenario == "falha" && retomavel ? " Você pode clicar em \"Continuar a análise\" para seguir de onde parou." : "";
                var ok = conferir(e, fim) && info.IsOpen && info.Message == fim.Mensagem + complemento
                    && (n.BotaoDescartar.Visibility == Visibility.Visible) == retomavel && Pmvrels(processo).Length == pmvrelAntes;
                resultados[cenario] = (ok ? "" : "FALHOU · ") + fim.Situacao + ": " + fim.Mensagem;
                if (!ok) avisos[cenario] = new JsonObject { ["texto"] = info.Message, ["aberto"] = info.IsOpen, ["retomavel"] = retomavel };
                tudo &= ok;
            }
            p["resultados"] = resultados;
            if (avisos.Count > 0) p["avisos_que_divergiram"] = avisos;

            // Codex ausente: o motivo exato e o botão desabilitado.
            var opcoes = j.OpcoesAgentes;
            j.OpcoesAgentes = () => opcoes() with { CodexEscolhido = Path.Combine(pasta, "nao-existe", "codex.exe") };
            await j.AtualizarPecasNovoRelatorioAsync();
            var semCodex = j.MotivoBotao(Provedor.ChatGpt) == "O Codex não foi encontrado neste computador. Instale o app do ChatGPT/Codex ou escolha o programa em Ajustes › Inteligência artificial."
                && !j.BotaoAnaliseHabilitado(Provedor.ChatGpt);
            j.OpcoesAgentes = opcoes;
            await j.AtualizarPecasNovoRelatorioAsync();
            p["codex_ausente"] = semCodex;

            // Aviso recusado: nada copiado, nenhum registro, nenhum processo iniciado.
            j.EsquecerAviso(Provedor.ChatGpt);
            await j.SoltarArquivosAsync([recusadoAnexo]);
            await j.AguardarNovoRelatorioAsync();
            var editor = Path.Combine(processo, ".pmvni-ai", "editor");
            var registrosAntes = Directory.GetDirectories(editor).Length;
            var recebidoAntes = File.GetLastWriteTimeUtc(Path.Combine(processo, ".pmvni-ai", "agente-falso", "recebido.json"));
            perguntas.RespostaPorTitulo["Analisar com o ChatGPT"] = false;
            j.Orquestrador = Orq("ok");
            var recusado = await j.AnalisarAsync(Provedor.ChatGpt);
            perguntas.RespostaPorTitulo.Remove("Analisar com o ChatGPT");
            var nada = recusado is null && Directory.GetDirectories(editor).Length == registrosAntes
                && File.GetLastWriteTimeUtc(Path.Combine(processo, ".pmvni-ai", "agente-falso", "recebido.json")) == recebidoAntes
                && !Directory.EnumerateFiles(processosAgentes, "nao-copiar*.pdf", SearchOption.AllDirectories).Any() && j.AvisoVersaoDoAgente(Provedor.ChatGpt) is null;
            p["aviso_recusado_nada_feito"] = nada;
            j.RemoverAnexo(0);

            // A cópia da árvore: o que já existia não mudou; o que é novo são só os anexos copiados, o registro do editor, o que o agente falso gravou e os .rpmvni.
            var depois = FotografarArvore(processosAgentes);
            var alterados = antesDoNovoRelatorio.Where(kv => !depois.TryGetValue(kv.Key, out var s) || s != kv.Value).Select(kv => kv.Key).ToList();
            static bool Permitido(string relativo)
            {
                var partes = relativo.Split(Path.DirectorySeparatorChar);
                var nome = partes[^1];
                if (relativo.Contains(@"\.pmvni-ai\editor\", StringComparison.OrdinalIgnoreCase) || relativo.Contains(@"\.pmvni-ai\agente-falso\", StringComparison.OrdinalIgnoreCase)
                    || relativo.Contains(@"\.pmvni-ai\analise\", StringComparison.OrdinalIgnoreCase) || relativo.Contains(@"\.pmvni-ai\analise-pro\", StringComparison.OrdinalIgnoreCase)
                    || relativo.Contains(@"\.pmvni-ai\analise-google\", StringComparison.OrdinalIgnoreCase))     // 1.1.0: a pasta de controle do análise Google (passo 54b)
                    return true;
                // Os .rpmvni importados no 0001 e no 0012 (e o .bak de uma gravação) e a trava do relatório aberto.
                if (nome.StartsWith("Relatorio_0001_2099", StringComparison.Ordinal) || nome.StartsWith("Relatorio_0012_2099", StringComparison.Ordinal)) return true;
                if (nome.StartsWith(".~Relatorio_0001_2099", StringComparison.Ordinal) || nome.StartsWith(".~Relatorio_0012_2099", StringComparison.Ordinal)) return true;
                return relativo.EndsWith(@"\PROJETO\REVISÃO 01\planta-ficticia.pdf", StringComparison.Ordinal) || relativo.EndsWith(@"\PROJETO\REVISÃO 01\memorial.docx", StringComparison.Ordinal)
                    || relativo.EndsWith(@"\PROJETO\INICIAL\foto.heic", StringComparison.Ordinal) || relativo.EndsWith(@"\DOCUMENTOS\requerimento-ficticio.pdf", StringComparison.Ordinal)
                    || relativo.EndsWith(@"\PROJETO\complemento-ficticio.pdf", StringComparison.Ordinal) || relativo.EndsWith(@"\complemento-ficticio.pdf", StringComparison.Ordinal)
                    || relativo.EndsWith(@"\levantamento-ficticio.pdf", StringComparison.Ordinal);
            }
            var novos = depois.Keys.Where(k => !antesDoNovoRelatorio.ContainsKey(k)).ToList();
            var foraDoPermitido = novos.Where(k => !Permitido(k)).ToList();
            p["arquivos_novos"] = novos.Count;
            p["novos_fora_do_permitido"] = new JsonArray(foraDoPermitido.Select(k => (JsonNode)k).ToArray());
            p["existentes_alterados"] = new JsonArray(alterados.Select(k => (JsonNode)k).ToArray());

            // Os anexos originais: mesmo sha, tamanho, datas e atributos.
            var anexosIntactos = fotoDosAnexos.All(kv =>
            {
                var info = new FileInfo(kv.Key);
                return info.Exists && ShaDe(kv.Key) == kv.Value.Sha && info.Length == kv.Value.Tamanho && info.LastWriteTimeUtc == kv.Value.Escrita
                    && info.CreationTimeUtc == kv.Value.Criacao && SemNuvem(info.Attributes) == kv.Value.Atributos;
            });
            p["anexos_originais_intactos"] = anexosIntactos;
            var soOFalso = ExecucaoProcesso.SomenteExecutaveisEm == agenteFalsoPasta;
            p["programas_iniciados"] = soOFalso ? "agente falso e soffice (trava SomenteExecutaveisEm)" : "trava desligada";
            p["ok"] = tudo && semCodex && nada && alterados.Count == 0 && foraDoPermitido.Count == 0 && anexosIntactos && soOFalso && j.ExecucoesEmCurso.Count == 0;
        });


        // ---------- Legislação: Ajustes › Legislação (Tarefa 17; passos 57 a 60) ----------
        // Só os PDFs fictícios de tests\corpus\legislacao, copiados para <pasta>\legislacao-originais (a pasta real LEGISLAÇÃO nunca
        // é lida). Sha, tamanho, data e atributos de cada original são registrados antes e conferidos no fim de cada passo.
        var legOriginais = Path.Combine(pasta, "legislacao-originais");
        if (Directory.Exists(legOriginais)) ApagarArvore(legOriginais);
        CopiarArvore(Path.Combine(corpus, "legislacao"), legOriginais);
        string Leg(string relativo) => Path.Combine(legOriginais, relativo);
        var legLei = Leg(@"MUNICIPAL\LEI 9.999-2099 - FICTICIA.pdf");
        var legLei2 = Leg(@"MUNICIPAL\LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf");
        var legNt = Leg(@"ESTADUAL - CBMES\NT 99-2099 - FICTICIA.pdf");
        var legFederal = Leg(@"FEDERAL\LEI 99.999-2099 - FICTICIA.pdf");
        var legDigital = Leg(@"OUTRAS\DIGITALIZADA - FICTICIA.pdf");
        var legSenha = Leg(@"OUTRAS\SENHA - FICTICIA.pdf");
        var legCorrompido = Leg(@"OUTRAS\CORROMPIDO - FICTICIA.pdf");
        var legNaoPdf = Leg(@"OUTRAS\NAO-E-PDF - FICTICIA.pdf");
        File.SetAttributes(legNt, File.GetAttributes(legNt) | FileAttributes.ReadOnly); // um original só leitura continua só leitura
        static string ShaLeg(string arquivo) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(arquivo)));
        static Dictionary<string, (string Sha, long Tamanho, DateTime Escrita, FileAttributes Atributos)> FotoLeg(string raiz) =>
            // Correções 2026-11-15: o cache das imagens extraídas do PDF (<sha>.figuras.json.gz) é dado derivado, gravado quando a extração
            // termina em segundo plano (às vezes durante este passo); não conta como gravação. Qualquer outro arquivo continua conferido.
            Directory.GetFiles(raiz, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".figuras.json.gz", StringComparison.OrdinalIgnoreCase)).ToDictionary(f => f, f =>
            {
                var i = new FileInfo(f);
                return (ShaLeg(f), i.Length, i.LastWriteTimeUtc, SemNuvem(i.Attributes));
            }, StringComparer.OrdinalIgnoreCase);
        var fotoLegOriginais = FotoLeg(legOriginais);
        bool OriginaisLegIntactos(JsonObject p)
        {
            var agora = FotoLeg(legOriginais);
            var ok = agora.Count == fotoLegOriginais.Count && fotoLegOriginais.All(kv => agora.TryGetValue(kv.Key, out var v) && v == kv.Value);
            p["originais_intactos"] = ok;
            return ok;
        }
        var dadosLeg = Path.Combine(PastaDados.Raiz, "legislacao");
        string CopiaLeg(string original) => Path.Combine(dadosLeg, ShaLeg(original) + ".pdf");
        string IndiceLeg(string original) => Path.Combine(dadosLeg, ShaLeg(original) + ".texto.json.gz");
        IReadOnlyList<string> CopiasLeg() => Directory.Exists(dadosLeg)
            ? Directory.GetFiles(dadosLeg, "*.pdf").Select(f => Path.GetFileName(f)).Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, "^[0-9a-f]{64}\\.pdf$")).Order(StringComparer.Ordinal).ToList()
            : [];
        int EntradasLeg() => Directory.Exists(Path.Combine(dadosLeg, ".entrada")) ? Directory.GetFileSystemEntries(Path.Combine(dadosLeg, ".entrada")).Length : 0;
        JsonArray LinhasEmJson(IReadOnlyList<LinhaAnexo> l) => new(l.Select(x => (JsonNode)$"{x.Titulo} | {Esferas.Rotulo(x.Esfera)} | {x.Detalhe} | {x.Selo}").ToArray());
        LinhaAnexo? LinhaDe(string titulo) => j.LinhasLegislacaoAjustes.FirstOrDefault(l => l.Titulo == titulo);
        var anexadoEm = new System.Text.RegularExpressions.Regex(@"^anexado em \d{2}/\d{2}/\d{4} \d{2}:\d{2}$");
        const string TituloLei = "Lei nº 9.999/2099 (fictícia)";

        // Tarefa 14: os passos 58–63, 30 e 48 partem do estado que o anterior deixa. Se ele falhou e deixou outro, o estado é refeito aqui pelo
        // app (excluir tudo e anexar de novo), para uma falha não derrubar os seguintes; o passo registra "estado_refeito".
        async Task GarantirLegislacaoAsync(int depoisDo, JsonObject p)
        {
            string[] esperado = depoisDo switch
            {
                57 => ["LEI 9.999-2099 - FICTICIA", "NT 99-2099 - FICTICIA"],
                58 => ["DIGITALIZADA - FICTICIA", "LEI 9.999-2099 - FICTICIA", "NT 99-2099 - FICTICIA"],
                59 => ["DIGITALIZADA - FICTICIA", "LEI 9.999-2099 - FICTICIA", "LEI 99.999-2099 - FICTICIA", "NT 99-2099 - FICTICIA"],
                _ => ["DIGITALIZADA - FICTICIA", "LEI 99.999-2099 - FICTICIA", "NT 99-2099 - FICTICIA", TituloLei],
            };
            var atuais = j.LinhasLegislacaoAjustes.Select(l => l.Titulo).Order(StringComparer.Ordinal).ToList();
            if (atuais.SequenceEqual(esperado.Order(StringComparer.Ordinal)) && (depoisDo < 60 || LinhaDe(TituloLei)?.Paginas == "4 páginas")) return;
            var confirmar = perguntas.Confirmar;
            perguntas.Confirmar = true;
            try
            {
                foreach (var l in j.LinhasLegislacaoAjustes.ToList()) await j.ExcluirAnexoAsync(l.Id);
                string[] lista = depoisDo switch
                {
                    57 => [legLei, legNt],
                    58 => [legLei, legNt, legDigital],
                    59 => [legFederal, legNt, legLei, legDigital],
                    _ => [legFederal, legNt, legLei2, legDigital],
                };
                perguntas.ListasParaAbrir.Enqueue(lista);
                await j.AnexarLegislacaoAsync();
                if (depoisDo >= 60)
                {
                    var v2 = LinhaDe("LEI 9.999-2099 - FICTICIA (VERSAO 2)") ?? throw new InvalidOperationException("a versão 2 da lei não foi anexada ao refazer o estado");
                    await j.RenomearAnexoAsync(v2.Id, TituloLei);
                }
            }
            finally { perguntas.Confirmar = confirmar; }
            p["estado_refeito"] = "depois do passo " + depoisDo;
        }

        await Passo("57_legislacao_anexar_dois", async p =>
        {
            await j.ReindexacaoLegislacao; // nada em segundo plano mexendo na pasta enquanto ela é apagada
            if (Directory.Exists(dadosLeg)) ApagarArvore(dadosLeg);
            await j.MudarModuloAsync(Modulo.Ajustes);
            perguntas.ListasParaAbrir.Enqueue([legLei, legNt]);
            var resumo = await j.AnexarLegislacaoAsync();
            var linhas = j.LinhasLegislacaoAjustes;
            p["resumo"] = resumo;
            p["linhas"] = LinhasEmJson(linhas);
            p["total"] = j.TotalLegislacaoAjustes;
            var linhasOk = linhas.Count == 2
                && linhas[0] is { Titulo: "NT 99-2099 - FICTICIA", Esfera: Esfera.Estadual, Arquivo: "NT 99-2099 - FICTICIA.pdf", Paginas: "2 páginas", Selo: null }
                && linhas[1] is { Titulo: "LEI 9.999-2099 - FICTICIA", Esfera: Esfera.Municipal, Arquivo: "LEI 9.999-2099 - FICTICIA.pdf", Paginas: "3 páginas", Selo: null }
                && linhas[0].Tamanho == ListaLegislacao.Tamanho(new FileInfo(legNt).Length) && linhas[1].Tamanho == ListaLegislacao.Tamanho(new FileInfo(legLei).Length)
                && linhas.All(l => anexadoEm.IsMatch(l.AnexadoEm));
            var copias = CopiasLeg();
            var pastaOk = copias.SequenceEqual(new[] { ShaLeg(legLei) + ".pdf", ShaLeg(legNt) + ".pdf" }.Order(StringComparer.Ordinal))
                && File.ReadAllBytes(CopiaLeg(legLei)).AsSpan().SequenceEqual(File.ReadAllBytes(legLei))
                && File.ReadAllBytes(CopiaLeg(legNt)).AsSpan().SequenceEqual(File.ReadAllBytes(legNt))
                && File.Exists(IndiceLeg(legLei)) && File.Exists(IndiceLeg(legNt)) && File.Exists(Path.Combine(dadosLeg, "catalogo.json")) && EntradasLeg() == 0;
            p["pasta_de_dados"] = new JsonArray(copias.Select(c => (JsonNode)c).ToArray());
            p["ok"] = resumo == "2 PDFs anexados." && j.AvisoLegislacaoAjustes.IsOpen && j.AvisoLegislacaoAjustes.Message == resumo
                && linhasOk && pastaOk && j.TotalLegislacaoAjustes.StartsWith("2 leis anexadas · ", StringComparison.Ordinal) && OriginaisLegIntactos(p);
        });

        await Passo("58_legislacao_recusas", async p =>
        {
            await GarantirLegislacaoAsync(57, p);
            // Anexar a partir do módulo Legislação: o andamento, o Cancelar e o resumo (com as recusas) aparecem nele; um segundo clique avisa.
            var moduloAntes = j.ModuloAtual;
            await j.MudarModuloAsync(Modulo.Legislacao);
            perguntas.ListasParaAbrir.Enqueue([legLei, legNaoPdf, legSenha, legCorrompido, legDigital]);
            var anexando = j.AnexarLegislacaoAsync();
            var segundo = await j.AnexarLegislacaoAsync();
            var andamentoNaLegislacao = j.ProgressoNaLegislacao;
            var progressoVisivel = j.LeitorLegislacao.ProgressoAnexo.Visibility == Visibility.Visible;
            var resumo = await anexando;
            var avisoLeg = j.LeitorLegislacao.AvisoMensagem;
            var naLegislacao = segundo == MainWindow.AnexacaoEmAndamento && andamentoNaLegislacao.Length > 0
                && progressoVisivel
                && avisoLeg.IsOpen && avisoLeg.Message == resumo && avisoLeg.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning
                && j.LeitorLegislacao.ProgressoAnexo.Visibility == Visibility.Collapsed;
            p["no_modulo_legislacao"] = new JsonObject { ["segundo_clique"] = segundo, ["andamento"] = andamentoNaLegislacao, ["ok"] = naLegislacao };
            // Passos independentes: a InfoBar do resumo sai (aberta, ela muda a altura do leitor quando outro passo a fechar, e o PDF.js refaz a página).
            avisoLeg.IsOpen = false;
            if (moduloAntes != Modulo.Legislacao) await j.MudarModuloAsync(moduloAntes);
            p["resumo"] = resumo;
            var linhas = j.LinhasLegislacaoAjustes;
            p["linhas"] = LinhasEmJson(linhas);
            const string Esperado = "1 PDF anexado (1 já estava anexado e foi ignorado); 3 recusados:\n“NAO-E-PDF - FICTICIA.pdf” não é um arquivo PDF.\n“SENHA - FICTICIA.pdf” está protegido por senha e não pode ser aberto pelo editor. Anexe uma cópia sem senha.\n“CORROMPIDO - FICTICIA.pdf” não pôde ser lido como PDF (arquivo danificado ou incompleto).";
            var digital = LinhaDe("DIGITALIZADA - FICTICIA");
            p["ok"] = resumo == Esperado && naLegislacao && linhas.Count == 3 && digital is { Esfera: Esfera.Outras, Paginas: "1 página", Selo: "Sem texto pesquisável" }
                && CopiasLeg().Count == 3 && EntradasLeg() == 0 && j.AvisoLegislacaoAjustes.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning
                && OriginaisLegIntactos(p);
        });

        await Passo("59_legislacao_excluir_e_anexar_novos", async p =>
        {
            await GarantirLegislacaoAsync(58, p);
            var nt = LinhaDe("NT 99-2099 - FICTICIA") ?? throw new InvalidOperationException("a NT não está na lista");
            var lei = LinhaDe("LEI 9.999-2099 - FICTICIA") ?? throw new InvalidOperationException("a lei não está na lista");
            var outros = new[] { CopiaLeg(legLei), IndiceLeg(legLei), CopiaLeg(legDigital), IndiceLeg(legDigital) }
                .ToDictionary(f => f, f => (ShaLeg(f), File.GetLastWriteTimeUtc(f)));
            const string Titulo = "Excluir “NT 99-2099 - FICTICIA”?";
            const string Texto = "A cópia do PDF guardada pelo editor e o seu índice de pesquisa serão apagados. O arquivo original (NT 99-2099 - FICTICIA.pdf) não é alterado.";
            perguntas.RespostaPorTitulo[Titulo] = true;
            var antes = perguntas.Confirmacoes.Count;
            var excluiu = await j.ExcluirAnexoAsync(nt.Id);
            perguntas.RespostaPorTitulo.Remove(Titulo);
            var pergunta = perguntas.Confirmacoes.Skip(antes).ToList();
            var perguntaOk = pergunta.Count == 1 && pergunta[0] == (Titulo, Texto, "Excluir", "Cancelar");
            var depoisDeExcluir = j.LinhasLegislacaoAjustes.Select(l => l.Titulo).ToList();
            var apagou = !File.Exists(CopiaLeg(legNt)) && !File.Exists(IndiceLeg(legNt));
            var outrosIguais = outros.All(kv => File.Exists(kv.Key) && (ShaLeg(kv.Key), File.GetLastWriteTimeUtc(kv.Key)) == kv.Value);
            var pesquisa = (await j.Legislacao.IndiceAsync()).Pesquisar("afastamento");
            p["excluir"] = new JsonObject
            {
                ["pergunta"] = pergunta.Count == 1 ? pergunta[0].Titulo + " / " + pergunta[0].Texto : "(" + pergunta.Count + " perguntas)",
                ["linhas"] = new JsonArray(depoisDeExcluir.Select(t => (JsonNode)t).ToArray()), ["pesquisa"] = pesquisa.Total,
            };
            var excluirOk = excluiu && perguntaOk && apagou && outrosIguais
                && depoisDeExcluir.SequenceEqual(["LEI 9.999-2099 - FICTICIA", "DIGITALIZADA - FICTICIA"])
                && pesquisa.Total == 3 && pesquisa.Resultados.All(r => r.AnexoId == lei.Id) && pesquisa.Resultados.Select(r => r.Pagina).SequenceEqual([1, 2, 3]);

            perguntas.ListasParaAbrir.Enqueue([legFederal, legNt]);
            var resumo = await j.AnexarLegislacaoAsync();
            var linhas = j.LinhasLegislacaoAjustes;
            var pesquisa4 = (await j.Legislacao.IndiceAsync()).Pesquisar("afastamento");
            var ntNova = LinhaDe("NT 99-2099 - FICTICIA");
            p["anexar_de_novo"] = new JsonObject { ["resumo"] = resumo, ["linhas"] = LinhasEmJson(linhas), ["pesquisa"] = pesquisa4.Total };
            var anexarOk = resumo == "2 PDFs anexados."
                && linhas.Select(l => l.Titulo).SequenceEqual(["LEI 99.999-2099 - FICTICIA", "NT 99-2099 - FICTICIA", "LEI 9.999-2099 - FICTICIA", "DIGITALIZADA - FICTICIA"])
                && linhas.Select(l => l.Esfera).SequenceEqual([Esfera.Federal, Esfera.Estadual, Esfera.Municipal, Esfera.Outras])
                && ntNova is not null && ntNova.Id != nt.Id && pesquisa4.Total == 4;

            // "Cancelar" na pergunta: nada muda (linhas e arquivos).
            var federal = LinhaDe("LEI 99.999-2099 - FICTICIA")!;
            var tituloFederal = "Excluir “LEI 99.999-2099 - FICTICIA”?";
            var linhasAntes = j.LinhasLegislacaoAjustes.ToList();
            var arquivosAntes = FotoLeg(dadosLeg);
            perguntas.RespostaPorTitulo[tituloFederal] = false;
            var cancelou = !await j.ExcluirAnexoAsync(federal.Id);
            perguntas.RespostaPorTitulo.Remove(tituloFederal);
            var nadaMudou = cancelou && j.LinhasLegislacaoAjustes.SequenceEqual(linhasAntes)
                && FotoLeg(dadosLeg) is var arquivosDepois && arquivosDepois.Count == arquivosAntes.Count
                && arquivosAntes.All(kv => arquivosDepois.TryGetValue(kv.Key, out var v) && v == kv.Value);
            p["cancelar_nao_muda"] = nadaMudou;
            p["ok"] = excluirOk && anexarOk && nadaMudou && OriginaisLegIntactos(p);
        });

        await Passo("60_legislacao_editar_e_substituir", async p =>
        {
            await GarantirLegislacaoAsync(59, p);
            // As linhas ficam em Ajustes › Legislação (auditoria §3.3).
            j.AjustesPagina.MostrarCategoria(CategoriasAjustes.Legislacao);
            var lei = LinhaDe("LEI 9.999-2099 - FICTICIA") ?? throw new InvalidOperationException("a lei não está na lista");
            const string Novo = "Lei nº 9.999/2099 (fictícia)";
            var renomeou = await j.RenomearAnexoAsync(lei.Id, Novo) is null && j.LinhasLegislacaoAjustes.Any(l => l.Id == lei.Id && l.Titulo == Novo);
            var vazio = await j.RenomearAnexoAsync(lei.Id, "");
            var vazioOk = vazio == "O título não pode ficar vazio." && j.AvisoLegislacaoAjustes.Message == vazio
                && j.LinhasLegislacaoAjustes.Single(l => l.Id == lei.Id).Titulo == Novo;
            // Esfera: muda e volta (o estado que o leitor usa fica o mesmo).
            var digital = LinhaDe("DIGITALIZADA - FICTICIA")!;
            await j.DefinirEsferaAnexoAsync(digital.Id, Esfera.Municipal);
            var mudouEsfera = j.LinhasLegislacaoAjustes.Single(l => l.Id == digital.Id).Esfera == Esfera.Municipal
                && new AcervoLegislacao(dadosLeg).Listar().Single(a => a.Id == digital.Id).Esfera == Esfera.Municipal;
            await j.DefinirEsferaAnexoAsync(digital.Id, Esfera.Outras);
            var voltouEsfera = j.LinhasLegislacaoAjustes.Single(l => l.Id == digital.Id).Esfera == Esfera.Outras;

            var copiaAntiga = CopiaLeg(legLei);
            var indiceAntigo = IndiceLeg(legLei);
            perguntas.ArquivoParaAbrir = legLei2;
            var recusa = await j.SubstituirAnexoAsync(lei.Id);
            var depois = j.LinhasLegislacaoAjustes.SingleOrDefault(l => l.Id == lei.Id);
            var versao2 = (await j.Legislacao.IndiceAsync()).Pesquisar("versao 2");
            p["substituir"] = new JsonObject
            {
                ["recusa"] = recusa, ["linha"] = depois is null ? null : $"{depois.Titulo} | {depois.Detalhe}", ["versao_2"] = versao2.Total,
            };
            var substituiu = recusa is null && depois is { Titulo: Novo, Esfera: Esfera.Municipal, Paginas: "4 páginas", Arquivo: "LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf" }
                && versao2.Total == 1 && versao2.Resultados[0] is { Pagina: 4 } r && r.AnexoId == lei.Id
                && !File.Exists(copiaAntiga) && !File.Exists(indiceAntigo) && File.Exists(CopiaLeg(legLei2)) && EntradasLeg() == 0;

            // Gravado de verdade: um acervo novo sobre a mesma pasta lista as mesmas 4 linhas com os mesmos campos.
            var novo = new AcervoLegislacao(dadosLeg);
            var relidas = novo.Listar().Select(a => ListaLegislacao.Linha(a, novo.Estado(a.Id), false, TimeZoneInfo.Local)).ToList();
            var gravado = relidas.SequenceEqual(j.LinhasLegislacaoAjustes);
            p["linhas"] = LinhasEmJson(j.LinhasLegislacaoAjustes);
            try { await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "60-ajustes-legislacao.png")); } catch (Exception e) { p["captura"] = e.Message; }
            p["ok"] = renomeou && vazioOk && mudouEsfera && voltouEsfera && substituiu && gravado && relidas.Count == 4 && OriginaisLegIntactos(p);
        });


        // ---------- Legislação: o módulo e o leitor (Tarefa 18; passos 30, 61, 62, 63 e 48, nessa ordem, sobre o estado do 60) ----------
        // O estado que os passos 57–60 deixam: Federal, NT, lei ("Lei nº 9.999/2099 (fictícia)", versão 2, 4 páginas, 5 marcadores) e
        // digitalizada. A área de transferência e as aberturas externas só são registradas.
        AreaTransferencia.Simular = true;
        string IdDe(string titulo) => j.LinhasLegislacaoAjustes.Single(l => l.Titulo == titulo).Id;
        async Task<JsonElement> JsLeitor(string expressao)
        {
            var r = await j.LeitorPdf!.AvaliarAsync(expressao);
            return r.TryGetProperty("value", out var v) ? v : r;
        }
        async Task<bool> EsperarPagina(int pagina) => await EsperarAteAsync(() => Task.FromResult(j.RodapeLegislacao.StartsWith($"Página {pagina} de ", StringComparison.Ordinal)), 5000);
        async Task TeclaLeitor(string key, string code, int vk, int modificadores)
        {
            // O foco no contêiner do PDF (só sem seleção: a tecla de citar precisa da seleção feita antes).
            await JsLeitor("(document.getSelection().isCollapsed && document.getElementById('container').focus(), true)");
            await j.LeitorPdf!.CdpAsync("Input.dispatchKeyEvent", new { type = "rawKeyDown", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
            await j.LeitorPdf!.CdpAsync("Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
        }
        async Task<bool> EsperarTextoDaPagina(int pagina)
        {
            await j.IrPaginaLegislacaoAsync(pagina);
            return await EsperarAteAsync(async () => (await JsLeitor($"document.querySelectorAll('.page[data-page-number=\"{pagina}\"] .textLayer span').length")).GetInt32() > 0, 10000);
        }
        Task<JsonElement> SelecionarNoLeitor(int pi, string inicio, int pf, string fim) => JsLeitor($$"""
            (() => {
              const achar = (p, t) => {
                const spans = [...document.querySelectorAll('.page[data-page-number="' + p + '"] .textLayer span')];
                for (const s of spans) { const n = s.firstChild; if (n && n.nodeType === 3 && n.data.includes(t)) return [n, n.data.indexOf(t)]; }
                return null;
              };
              const a = achar({{pi}}, {{JsonSerializer.Serialize(inicio)}}), b = achar({{pf}}, {{JsonSerializer.Serialize(fim)}});
              if (!a || !b) return false;
              const r = document.createRange();
              r.setStart(a[0], a[1]);
              r.setEnd(b[0], b[1] + {{JsonSerializer.Serialize(fim)}}.length);
              const s = document.getSelection(); s.removeAllRanges(); s.addRange(r);
              return true;
            })()
            """);

        await Passo("30_legislacao_leitor", async p =>
        {
            await GarantirLegislacaoAsync(60, p);
            var fotoAntes = FotoLeg(dadosLeg);
            var trilho = j.TrilhoAtual.Modulos.Select(Modulos.Rotulo).ToList();
            p["trilho"] = new JsonArray(trilho.Select(t => (JsonNode)t).ToArray());
            // Os rótulos e, auditoria §1.2, o ícone colorido de cada módulo (o template de IconesModulos.xaml, não o glifo de reserva).
            var trilhoOk = trilho.SequenceEqual(Faixas.Disponiveis.Select(Modulos.Rotulo))
                && j.TrilhoAtual.Modulos.All(j.TrilhoAtual.IconeCarregado);
            await j.MudarModuloAsync(Modulo.Legislacao);
            await EsperarAteAsync(() => Task.FromResult(j.LeitorPdf is not null), 30000);
            var grupos = j.LeisMostradas;
            p["grupos"] = new JsonArray(grupos.Select(g => (JsonNode)(g.Grupo + ": " + string.Join(" | ", g.Linhas.Select(l => l.Titulo)))).ToArray());
            var listaOk = j.AbaDoPainelLegislacao == "Leis" && grupos.Select(g => g.Grupo).SequenceEqual(["Federal", "Estadual", "Municipal", "Outras"])
                && grupos.All(g => g.Linhas.Count == 1) && grupos.Sum(g => g.Linhas.Count) == 4;
            j.FiltrarLeis("nt");
            var filtroOk = j.LeisMostradas is [{ Grupo: "Estadual", Linhas: [{ Titulo: "NT 99-2099 - FICTICIA" }] }];
            j.FiltrarLeis("");
            var lei = IdDe(TituloLei);
            var abriu = await j.AbrirLeiAsync(lei);
            var url = j.LeitorPdf!.Core.Source;
            p["url"] = url;
            p["rodape_ao_abrir"] = j.RodapeLegislacao;
            var abrirOk = abriu && url == "https://leg.pmvni.invalid/leitor.html" && j.RodapeLegislacao.StartsWith("Página 1 de 4", StringComparison.Ordinal);
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoPaginaProxima);
            var proximaOk = await EsperarPagina(2);
            await TeclaLeitor("ArrowDown", "ArrowDown", 40, 1); // Alt+↓ dentro do PDF: a página repassa o atalho
            var atalhoOk = await EsperarPagina(3);
            p["rodape_depois_do_atalho"] = j.RodapeLegislacao;
            var marcadores = j.MarcadoresDaLei;
            p["marcadores"] = new JsonArray(marcadores.Select(m => (JsonNode)m.Titulo).ToArray());
            var marcadoresOk = j.LeitorLegislacao.AbaMarcadores.IsEnabled && marcadores.Count == 5;
            await j.IrPaginaLegislacaoAsync(1);
            var anexo = marcadores.Select((m, i) => (m, i)).Single(x => x.m.Titulo == "Anexo I").i;
            await j.IrMarcadorLegislacaoAsync([anexo]);
            var anexoOk = await EsperarPagina(3);
            var escalas = new JsonObject();
            var escalaOk = true;
            foreach (var (id, nome) in new[] { (ComandosApp.LegislacaoZoomMais, "mais"), (ComandosApp.LegislacaoZoomMenos, "menos"), (ComandosApp.LegislacaoPaginaInteira, "pagina"), (ComandosApp.LegislacaoAjustarLargura, "largura") })
            {
                var antes = j.RodapeLegislacao;
                await j.ExecutarComandoAsync(id);
                escalas[nome] = j.RodapeLegislacao;
                escalaOk &= j.RodapeLegislacao != antes;
            }
            p["escalas"] = escalas;
            var digital = IdDe("DIGITALIZADA - FICTICIA");
            await j.AbrirLeiAsync(digital);
            var aviso = j.LeitorLegislacao.AvisoSemTexto;
            var semTextoOk = aviso.IsOpen && aviso.Message == "Este PDF não tem texto pesquisável: a pesquisa e a cópia de texto não funcionam nele."
                && !j.LeitorLegislacao.AbaMarcadores.IsEnabled && !j.FaixaAtual.Habilitado(ComandosApp.LegislacaoMarcadores)
                && !j.FaixaAtual.Habilitado(ComandosApp.LegislacaoCopiar) && j.RodapeLegislacao.StartsWith("Página 1 de 1", StringComparison.Ordinal);
            var fotoDepois = FotoLeg(dadosLeg);
            var nadaGravado = fotoDepois.Count == fotoAntes.Count && fotoAntes.All(kv => fotoDepois.TryGetValue(kv.Key, out var v) && v == kv.Value);
            p["bloqueadas"] = j.RequisicoesBloqueadasLegislacao;
            p["csp"] = j.ViolacoesCspLegislacao;
            p["conferencias"] = new JsonObject
            {
                ["trilho"] = trilhoOk, ["lista"] = listaOk, ["filtro"] = filtroOk, ["abrir"] = abrirOk, ["proxima"] = proximaOk, ["atalho"] = atalhoOk,
                ["marcadores"] = marcadoresOk, ["anexo_i"] = anexoOk, ["escala"] = escalaOk, ["sem_texto"] = semTextoOk, ["nada_gravado"] = nadaGravado,
            };
            try { await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "30-legislacao.png")); } catch (Exception e) { p["captura"] = e.Message; }
            p["ok"] = trilhoOk && listaOk && filtroOk && abrirOk && proximaOk && atalhoOk && marcadoresOk && anexoOk && escalaOk && semTextoOk && nadaGravado
                && j.RequisicoesBloqueadasLegislacao == 0 && j.ViolacoesCspLegislacao == 0;
        });

        await Passo("61_legislacao_pesquisa_na_lei", async p =>
        {
            await GarantirLegislacaoAsync(60, p);
            await j.MudarModuloAsync(Modulo.Legislacao);
            await j.AbrirLeiAsync(IdDe(TituloLei));
            var dica = j.DicaDaPesquisa;
            var r = new JsonObject();
            await j.DigitarPesquisaAsync("AFASTAMENTO");
            r["afastamento"] = j.TextoResultadoPesquisa;
            await j.IrNaPesquisaAsync(1);
            r["enter"] = j.TextoResultadoPesquisa;
            await j.IrNaPesquisaAsync(-1);
            await j.IrNaPesquisaAsync(-1);
            r["shift_enter_2x"] = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("ocupacao");
            r["ocupacao"] = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("inexistentexyz");
            r["inexistente"] = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("");
            var realces = await EsperarAteAsync(async () => (await JsLeitor("document.querySelectorAll('.highlight').length")).GetInt32() == 0, 3000);
            await j.AbrirLeiAsync(IdDe("DIGITALIZADA - FICTICIA"));
            await j.DigitarPesquisaAsync("x");
            r["digitalizada"] = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("");
            p["contadores"] = r;
            p["dica"] = dica;
            p["ok"] = dica == "Pesquisar nesta lei" && (string?)r["afastamento"] == "1 de 3" && (string?)r["enter"] == "2 de 3" && (string?)r["shift_enter_2x"] == "3 de 3"
                && (string?)r["ocupacao"] == "1 de 2" && (string?)r["inexistente"] == "Nenhuma ocorrência" && realces
                && (string?)r["digitalizada"] == "Este PDF não tem texto pesquisável.";
        });

        await Passo("62_legislacao_pesquisa_em_todas", async p =>
        {
            await GarantirLegislacaoAsync(60, p);
            await j.MudarModuloAsync(Modulo.Legislacao);
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoEscopoTodas);
            var dica = j.DicaDaPesquisa;
            await j.DigitarPesquisaAsync("afastamento");
            var grupos = j.ResultadosMostrados;
            p["cabecalho"] = j.CabecalhoResultados;
            p["resultados"] = new JsonArray(grupos.Select(g => (JsonNode)(g.Lei + ": " + string.Join(" | ", g.Linhas))).ToArray());
            var listaOk = j.AbaDoPainelLegislacao == "Resultados" && j.CabecalhoResultados == "4 resultados em 2 leis" && grupos.Count == 2
                && grupos[0].Lei == "NT 99-2099 - FICTICIA" && grupos[0].Linhas.SequenceEqual(["p. 2 — 5.1 O afastamento entre edificações fictícias é de 4,00 m."])
                && grupos[1].Lei == TituloLei && grupos[1].Linhas.Select(l => l[..5]).SequenceEqual(["p. 1 ", "p. 2 ", "p. 3 "]);
            var p3 = j.ResultadosDaPesquisa.Single(x => x.AnexoId == IdDe(TituloLei) && x.Pagina == 3);
            var abriu = await j.AbrirResultadoAsync(p3, "afastamento");
            await EsperarPagina(3);
            p["depois_de_clicar"] = new JsonObject { ["rodape"] = j.RodapeLegislacao, ["contador"] = j.TextoResultadoPesquisa, ["dica"] = j.DicaDaPesquisa };
            var clicarOk = abriu && j.LeiAbertaNoLeitor?.Titulo == TituloLei && j.RodapeLegislacao.StartsWith("Página 3 de 4", StringComparison.Ordinal)
                && !j.EscopoTodasAsLeis && j.DicaDaPesquisa == "Pesquisar nesta lei" && j.TextoResultadoPesquisa == "3 de 3";
            await j.DigitarPesquisaAsync(""); // Esc / caixa vazia: volta à aba Leis
            var voltouOk = j.AbaDoPainelLegislacao == "Leis";
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoEscopoTodas);
            await j.DigitarPesquisaAsync("a");
            p["termo_curto"] = j.CabecalhoResultados;
            var curtoOk = j.CabecalhoResultados == "Digite ao menos 2 caracteres." && j.ResultadosMostrados.Count == 0;
            await j.DigitarPesquisaAsync("");
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoEscopoLei);
            p["ok"] = dica == "Pesquisar em todas as leis" && listaOk && clicarOk && voltouOk && curtoOk;
        });

        await Passo("63_legislacao_copiar_e_citar", async p =>
        {
            await GarantirLegislacaoAsync(60, p);
            await j.MudarModuloAsync(Modulo.Legislacao);
            await j.AbrirLeiAsync(IdDe(TituloLei), 2);
            await JsLeitor("document.getSelection().removeAllRanges(), true");
            await EsperarAteAsync(() => Task.FromResult(!j.FaixaAtual.Habilitado(ComandosApp.LegislacaoCopiar)), 3000);
            var f = j.FaixaAtual;
            var semSelecaoOk = !f.Habilitado(ComandosApp.LegislacaoCopiar) && !f.Habilitado(ComandosApp.LegislacaoCopiarCitacao)
                && f.Dica(ComandosApp.LegislacaoCopiar) == "Selecione um trecho do texto." && f.Dica(ComandosApp.LegislacaoCopiarCitacao) == "Selecione um trecho do texto.";
            const string Trecho = "O afastamento frontal mínimo é de 3,00 m.";
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoPaginaInteira);
            var texto2 = await EsperarTextoDaPagina(2);
            var selecionou = texto2 && (await SelecionarNoLeitor(2, "O afastamento frontal", 2, "3,00 m.")).GetBoolean();
            var habilitou = await EsperarAteAsync(() => Task.FromResult(f.Habilitado(ComandosApp.LegislacaoCopiar) && f.Habilitado(ComandosApp.LegislacaoCopiarCitacao)), 3000);
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoCopiar);
            var copiou = AreaTransferencia.Copiados.LastOrDefault();
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoCopiarCitacao);
            var citou = AreaTransferencia.Copiados.LastOrDefault();
            var antes = AreaTransferencia.Copiados.Count;
            await TeclaLeitor("C", "KeyC", 67, 2 | 8); // Ctrl+Shift+C dentro do PDF
            await EsperarAteAsync(() => Task.FromResult(AreaTransferencia.Copiados.Count > antes), 3000);
            var atalho = AreaTransferencia.Copiados.Count > antes ? AreaTransferencia.Copiados[^1] : null;
            // A seleção mudou com a tecla? Não: Ctrl+Shift+C não mexe na seleção. Agora, da página 2 à 3.
            var texto3 = await EsperarTextoDaPagina(3);
            var duas = texto3 && (await SelecionarNoLeitor(2, "Art. 3º", 3, "ANEXO I")).GetBoolean();
            // A troca de seleção passa por "sem seleção" (removeAllRanges): espera a faixa voltar a habilitar antes de citar.
            await Esperar(300);
            await EsperarAteAsync(() => Task.FromResult(f.Habilitado(ComandosApp.LegislacaoCopiarCitacao)), 3000);
            p["selecao_duas_paginas"] = duas;
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoCopiarCitacao);
            var citouDuas = AreaTransferencia.Copiados.LastOrDefault();
            await JsLeitor("document.getSelection().removeAllRanges(), true");
            p["copiados"] = new JsonArray(new[] { copiou, citou, atalho, citouDuas }.Select(c => (JsonNode?)c).ToArray());
            const string Citacao = "“O afastamento frontal mínimo é de 3,00 m.” (Lei nº 9.999/2099 (fictícia), página 2)";
            p["ok"] = semSelecaoOk && selecionou && habilitou && copiou == Trecho && citou == Citacao && atalho == Citacao
                && duas && citouDuas is not null && citouDuas.EndsWith("páginas 2–3)", StringComparison.Ordinal) && citouDuas.StartsWith("“Art. 3º", StringComparison.Ordinal);
        });

        await Passo("48_legislacao_seguranca", async p =>
        {
            await GarantirLegislacaoAsync(60, p);
            var script = Leg(@"OUTRAS\COM-SCRIPT - FICTICIA.pdf");
            perguntas.ListasParaAbrir.Enqueue([script]);
            var resumo = await j.AnexarLegislacaoAsync();
            p["resumo"] = resumo;
            await j.MudarModuloAsync(Modulo.Legislacao);
            var id = IdDe("COM-SCRIPT - FICTICIA");
            var bloqueadasAntes = j.RequisicoesBloqueadasLegislacao;
            var cspAntes = j.ViolacoesCspLegislacao;
            var abriu = await j.AbrirLeiAsync(id);
            await Esperar(1000); // o OpenAction do PDF teria rodado aqui
            var semNada = j.RequisicoesBloqueadasLegislacao == bloqueadasAntes && j.ViolacoesCspLegislacao == cspAntes;
            // Clique por CDP no link /URI: nada navega, nenhuma janela nova, nada é aberto fora do app.
            var aberturas = AberturaExterna.Registradas.Count;
            var desenhou = await EsperarAteAsync(async () => (await JsLeitor("document.querySelectorAll('.annotationLayer a').length")).GetInt32() > 0, 10000);
            var ponto = await JsLeitor("(() => { const a = document.querySelector('.annotationLayer a'); a.scrollIntoView({block:'center'}); const r = a.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()");
            double x = ponto.GetProperty("x").GetDouble(), y = ponto.GetProperty("y").GetDouble();
            await j.LeitorPdf!.CdpAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 });
            await j.LeitorPdf!.CdpAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 });
            await Esperar(1000);
            var linkOk = desenhou && j.LeitorPdf.Core.Source == LeitorPdfJs.Pagina && AberturaExterna.Registradas.Count == aberturas
                && j.RequisicoesBloqueadasLegislacao == bloqueadasAntes;
            // Navegação de nível superior para outra URL do host: cancelada (1 bloqueio), o leitor continua.
            await JsLeitor("location.href = 'https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs', true");
            await EsperarAteAsync(() => Task.FromResult(j.RequisicoesBloqueadasLegislacao > bloqueadasAntes), 3000);
            var navegacaoOk = j.RequisicoesBloqueadasLegislacao == bloqueadasAntes + 1 && (await JsLeitor("location.href")).GetString() == LeitorPdfJs.Pagina
                && j.LeitorPdf.Core.Source == LeitorPdfJs.Pagina && (await j.ProcurarNaLeiAsync("script", 0)) == "1 de 1";
            await j.ProcurarNaLeiAsync("", 0);
            var catalogo = (await JsLeitor("fetch('https://leg.pmvni.invalid/catalogo.json').then(r => r.status)")).GetInt32();
            // Excluir o anexo com o leitor aberto: o leitor fecha (aba Leis, nada aberto) e a URL do PDF dele passa a 403.
            var urlPdf = j.LeitorPdf.UrlAberta;
            perguntas.RespostaPorTitulo["Excluir “COM-SCRIPT - FICTICIA”?"] = true;
            var excluiu = await j.ExcluirAnexoAsync(id);
            perguntas.RespostaPorTitulo.Remove("Excluir “COM-SCRIPT - FICTICIA”?");
            var fechou = await EsperarAteAsync(() => Task.FromResult(j.LeiAbertaNoLeitor is null && j.LeitorPdf.UrlAberta is null), 5000);
            var pdf = urlPdf is null ? 0 : (await JsLeitor($"fetch({JsonSerializer.Serialize(urlPdf)}).then(r => r.status)")).GetInt32();
            p["seguranca"] = new JsonObject
            {
                ["abriu"] = abriu, ["script_nao_rodou"] = semNada, ["link"] = linkOk, ["navegacao"] = navegacaoOk, ["catalogo"] = catalogo,
                ["excluiu"] = excluiu, ["fechou"] = fechou, ["aba"] = j.AbaDoPainelLegislacao, ["pdf_depois"] = pdf,
                ["bloqueadas"] = j.RequisicoesBloqueadasLegislacao, ["csp"] = j.ViolacoesCspLegislacao,
            };
            p["ok"] = resumo == "1 PDF anexado." && abriu && semNada && linkOk && navegacaoOk && catalogo == 403 && excluiu && fechou
                && j.AbaDoPainelLegislacao == "Leis" && pdf == 403 && j.ViolacoesCspLegislacao == 0 && j.LinhasLegislacaoAjustes.Count == 4
                && OriginaisLegIntactos(p);
        });

        // ---------- Tarefa 14: passo 64, o fluxo completo em sequência única ----------
        // Novo relatório (agente falso, cenário ok) → editar, situação, excluir e desfazer → salvar, fechar, reabrir → exportar → pesquisar → tema →
        // Recentes → Comparar → Novo despacho › Correção → Ajustes › Legislação → leitor (pesquisa em todas, citação) → excluir a lei; por fim o
        // registro da execução, o anexo original, nenhuma chave em tests\saida, nada gravado fora de tests\saida e só o agente falso iniciado.
        await Passo("64_fluxo_completo", async p =>
        {
            var conf = new JsonObject();
            bool Conf(string nome, bool valor) { conf[nome] = valor; return valor; }
            await j.FecharSessaoAsync();
            var fluxoPasta = Path.Combine(pasta, "fluxo");
            if (Directory.Exists(fluxoPasta)) ApagarArvore(fluxoPasta);
            var fluxoProcessos = Path.Combine(fluxoPasta, "processos");
            CopiarArvore(Path.Combine(corpus, "processos"), fluxoProcessos);
            var fluxoLeg = Path.Combine(fluxoPasta, "legislacao");
            CopiarArvore(Path.Combine(corpus, "legislacao"), fluxoLeg);
            var fluxoAnexo = Path.Combine(fluxoPasta, "anexos", "planta-ficticia.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(fluxoAnexo)!);
            File.WriteAllBytes(fluxoAnexo, PdfFicticio("Planta baixa ficticia do fluxo completo"));
            var fotoAnexo = FotoLeg(Path.GetDirectoryName(fluxoAnexo)!);
            await PrepararAgentesAsync();
            j.DefinirPastaProcessos(fluxoProcessos);
            Environment.SetEnvironmentVariable("PMVNI_KIT_RAIZ", null);
            j.DefinirPastaKit(kitReal);
            j.Hoje = () => new DateOnly(2099, 3, 10);
            j.Conversor = conversorReal;
            perguntas.Confirmar = true;
            var proc = Directory.GetDirectories(Path.Combine(fluxoProcessos, "2099"), "0001 - 2099 - *").Single();

            // (1) Novo relatório pela faixa de Relatórios, com o anexo na reapresentação, e Analisar com o ChatGPT (agente falso). Sem relatório aberto:
            // o importado abre direto (Ruling FD-I3).
            await SemRelatorioAbertoAsync();
            await j.MudarModuloAsync(Modulo.Relatorios);
            await j.ExecutarComandoAsync(ComandosApp.RelatoriosNovo);
            await j.AguardarNovoRelatorioAsync();
            await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == proc));
            perguntas.ArquivosParaAnexar = [fluxoAnexo];
            await j.AnexarAsync();
            await j.AguardarNovoRelatorioAsync();
            j.DefinirDestino(0, DestinoAnexo.ReapresentacaoNova);
            await j.AguardarNovoRelatorioAsync();
            var previaAnexo = j.AnexosDoNovoRelatorio.Count == 1 ? j.AnexosDoNovoRelatorio[0].Previa : null;
            p["previa_do_anexo"] = previaAnexo;
            Conf("novo_relatorio_pela_faixa", j.PaginaNovoRelatorioVisivel && previaAnexo == @"PROJETO\REVISÃO 01\planta-ficticia.pdf");
            j.PedidoTexto = "Fluxo completo do roteiro (fictício).";
            j.Orquestrador = Orq("ok");
            var exec = await j.AnalisarAsync(Provedor.ChatGpt) ?? throw new InvalidOperationException("a análise não partiu: " + j.InfoMensagemAtual.Message);
            var fimExec = await Prazo(j.AguardarAnaliseAsync(exec), 180);
            p["analise"] = fimExec.Situacao + (fimExec.Mensagem is null ? "" : ": " + fimExec.Mensagem);
            var sessao = j.Sessao ?? throw new InvalidOperationException("o relatório importado não abriu: " + j.InfoMensagemAtual.Message);
            var pmvrelFluxo = sessao.Caminho;
            p["pmvrel"] = Path.GetFileName(pmvrelFluxo);
            Conf("importado_e_aberto", fimExec.Situacao == "concluida" && NaEtapa(pmvrelFluxo, proc)
                && Path.GetFileName(pmvrelFluxo).StartsWith("Relatorio_0001_2099", StringComparison.Ordinal) && j.FolhasVisiveis && sessao.Revisao.Projeto.Itens.Count == 3);
            var idsFluxo = sessao.Revisao.Projeto.Incluidos.Select(i => i.Id).ToList();

            // (2) Editar o texto no A4 (Enter), mudar a situação com justificativa, excluir um item e desfazer.
            const string TextoFluxo = "Verificação refeita no fluxo completo do roteiro.";
            const string JustificativaFluxo = "Conferido na planta pelo fluxo completo do roteiro.";
            string? VerificacaoFluxo() => DadosEfetivos.TextoEfetivo(sessao.Revisao.Projeto, sessao.Revisao.Projeto.Item(idsFluxo[0])!, "verificacao");
            var abriuA4 = await DigitarAsync(idsFluxo[0], TextoFluxo);
            await TeclaAsync(j, "Enter", "Enter", 13);
            var gravouTexto = abriuA4 && await EsperarAteAsync(() => Task.FromResult(VerificacaoFluxo() == TextoFluxo), 5000);
            var fechouEdicao = gravouTexto && await EsperarAteAsync(async () => !await EditandoAsync(), 3000);
            p["edicao_a4"] = new JsonObject { ["abriu"] = abriuA4, ["texto"] = VerificacaoFluxo(), ["fechou"] = fechouEdicao };
            var editou = fechouEdicao;
            Conf("editou_no_a4", editou);
            await j.AplicarAsync(new DefinirSituacao(idsFluxo[1], "ATENDE"));
            await j.AplicarAsync(new DefinirJustificativa(idsFluxo[1], JustificativaFluxo));
            var item2 = sessao.Revisao.Projeto.Item(idsFluxo[1])!;
            Conf("situacao_com_justificativa", item2.Situacao.Revisada == "ATENDE" && item2.Situacao.Justificativa == JustificativaFluxo && j.Aberto!.Preparado.ErrosFinais.Count == 0);
            await j.SelecionarAsync(idsFluxo[2]);
            await j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            var excluiuItem = sessao.Revisao.Projeto.Item(idsFluxo[2])!.Incluido == false;
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            Conf("excluir_e_desfazer", excluiuItem && sessao.Revisao.Projeto.Item(idsFluxo[2])!.Incluido);

            // (3) Salvar, fechar e reabrir: o mesmo estado e o mesmo histórico.
            await j.ExecutarComandoAsync(ComandosApp.ArquivoSalvar);
            var salvou = await EsperarAteAsync(() => Task.FromResult(sessao.Estado == EstadoGravacao.Salvo), 10000);
            var historicoAntes = sessao.Revisao.HistoricoCompleto.Count;
            var reabriu = await j.FecharSessaoAsync() && await j.AbrirPacoteAsync(pmvrelFluxo);
            sessao = j.Sessao ?? throw new InvalidOperationException("não reabriu: " + j.InfoMensagem.Message);
            var pj = sessao.Revisao.Projeto;
            var mesmoEstado = reabriu && VerificacaoFluxo() == TextoFluxo && pj.Item(idsFluxo[1])!.Situacao.Revisada == "ATENDE"
                && pj.Item(idsFluxo[1])!.Situacao.Justificativa == JustificativaFluxo && pj.Item(idsFluxo[2])!.Incluido && sessao.Revisao.HistoricoCompleto.Count == historicoAntes;
            p["registros_no_historico"] = historicoAntes;
            Conf("salvar_fechar_reabrir", salvou && mesmoEstado);

            // (4) Exportar o PDF: páginas = folhas; o recibo leva o sha do pacote gravado.
            var pdfFluxo = Path.Combine(fluxoPasta, "fluxo-completo.pdf");
            var (okPdf, xPdf, erroPdf, _) = await j.ExportarParaAsync(pdfFluxo);
            var paginasPdf = okPdf ? InfoPdf.Ler(await File.ReadAllBytesAsync(pdfFluxo)).Paginas : 0;
            var shaRecibo = xPdf is null ? null : JsonNode.Parse(await File.ReadAllTextAsync(xPdf.CaminhoRecibo))?["sha256_pacote"]?.GetValue<string>();
            p["exportar"] = new JsonObject { ["paginas"] = paginasPdf, ["folhas"] = j.Aberto?.Composicao.Folhas, ["erro"] = erroPdf };
            Conf("exportou_pdf", okPdf && paginasPdf == j.Aberto!.Composicao.Folhas && string.Equals(shaRecibo, ShaDe(pmvrelFluxo), StringComparison.OrdinalIgnoreCase));

            // (5) Pesquisar no relatório, (6) trocar o tema e voltar, (7) Recentes com o pacote no topo, (8) Comparar com as 2 alterações restantes.
            await j.DigitarPesquisaAsync("fluxo completo");
            p["pesquisa"] = j.TextoResultadoPesquisa;
            Conf("pesquisa", j.PesquisaTotal >= 1 && j.TextoResultadoPesquisa == $"1 de {j.PesquisaTotal}" && j.PesquisaRef == idsFluxo[0]);
            await j.DigitarPesquisaAsync("");
            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaEscuro);
            await Esperar(600);
            var ficouEscuro = j.TemaAtual == ElementTheme.Dark;
            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaSistema);
            await Esperar(400);
            Conf("tema", ficouEscuro && j.TemaDosAjustes == "sistema");
            var faixaArquivo = j.FaixaAtual;
            faixaArquivo.EscolherAba("Arquivo");
            await Esperar(250);
            var recentesFluxo = faixaArquivo.AtualizarMenuDinamico(ComandosApp.ArquivoRecentes);
            Conf("analise_recente_no_topo", recentesFluxo.Count > 0 && Mesmo(recentesFluxo[0], exec.Registro.Pasta)
                && !recentesFluxo.Any(c => Mesmo(c, pmvrelFluxo)) && Mesmo(j.RecentesNosAjustes.FirstOrDefault(), pmvrelFluxo));
            await j.ExecutarComandoAsync(ComandosApp.RevisaoComparar);
            var comparado = perguntas.Informados.LastOrDefault();
            var diferencas = Comparacao.Diferencas(sessao.Revisao.Projeto, sessao.Revisao.HistoricoCompleto);
            p["comparar"] = comparado.Texto?.Split('\n')[0];
            Conf("comparar_duas_alteracoes", comparado.Titulo == "Comparar com o original" && diferencas.Count == 2
                && comparado.Texto?.StartsWith("2 itens alterados", StringComparison.Ordinal) == true);

            // (9) Novo despacho › Correção pela faixa de Relatórios: processo e relatório pré-escolhidos, etapa nova, exigências = itens ≠ ATENDE agora.
            var esperadas = sessao.Revisao.Projeto.Incluidos.Select((it, k) => (it, n: k + 1))
                .Where(t => (t.it.Situacao.Revisada ?? t.it.Situacao.Original!.GetValue<string>()) != "ATENDE")
                .Select(t => "ITEM " + t.n.ToString("D2", CultureInfo.InvariantCulture)).ToArray();
            await j.MudarModuloAsync(Modulo.Relatorios);
            j.FaixaAtual.EscolherAba("Página Inicial");
            await Esperar(250);
            await j.ExecutarComandoAsync(ComandosApp.DespachosNovoCorrecao);
            await j.AguardarNovoDespachoAsync();
            var desp = j.DespachoEmEdicao ?? throw new InvalidOperationException("a página Novo despacho não abriu: " + j.InfoMensagemAtual.Message);
            var exigencias = desp.Rascunho?.Pendencias.Select(x => "ITEM " + x.Numero.ToString("D2", CultureInfo.InvariantCulture)).ToArray() ?? [];
            p["exigencias"] = new JsonArray(exigencias.Select(x => (JsonNode)x).ToArray());
            p["etapa"] = desp.Etapa?.Nome;
            // O relatório já está na pasta DESPACHO NN da etapa (regra do kit, 2026-11-16): o despacho reaproveita essa pasta.
            Conf("origem_pre_escolhida", desp.Processo?.Numero == 1 && Mesmo(desp.CaminhoPacote, pmvrelFluxo) && desp.Etapa is { } etapaFluxo
                && Mesmo(etapaFluxo.Pasta, Path.GetDirectoryName(pmvrelFluxo)!));
            Conf("exigencias_do_estado_atual", exigencias.SequenceEqual(esperadas) && esperadas.SequenceEqual(["ITEM 03"]));
            await j.GerarEConferirAsync();
            Conf("paginas_para_conferir", desp.Preparacao is { Pdf: not null, Erros.Count: 0 } && j.NovoDespacho.CaixasDasPaginas.Count == desp.Preparacao.Paginas);
            var marcouTodas = await MarcarTodasAsync();
            var entrega = await j.GravarDespachoAsync() ?? throw new InvalidOperationException("não gravou o despacho: " + j.InfoMensagemAtual.Message);
            var reciboDesp = ReciboDespacho.Ler(entrega.Recibo);
            p["despacho"] = new JsonArray(Directory.GetFiles(entrega.Pasta).Select(f => (JsonNode)Path.GetFileName(f)).ToArray());
            Conf("despacho_gravado", marcouTodas && File.Exists(entrega.Docx) && entrega.Pdf is not null && File.Exists(entrega.Pdf) && File.Exists(entrega.Recibo)
                && Directory.GetFiles(entrega.Pasta, "Despacho_*").Length == 3 && Mesmo(Path.GetDirectoryName(entrega.Pasta), proc)
                && string.Equals(reciboDesp["relatorio"]?["sha256_pacote"]?.GetValue<string>(), ShaDe(pmvrelFluxo), StringComparison.OrdinalIgnoreCase));
            var escolhidoFluxo = j.DespachoEscolhido?.Despacho;
            Conf("despachos_lista_correcao", j.PaginaDespachosVisivel && escolhidoFluxo is { Tipo: TipoDespacho.Correcao } && Mesmo(escolhidoFluxo.Pasta, entrega.Pasta));

            // (10) Ajustes › Legislação com a pasta vazia: anexar a lei fictícia, uma linha, o título.
            await j.ReindexacaoLegislacao;
            await j.MudarModuloAsync(Modulo.Ajustes);
            foreach (var l in j.LinhasLegislacaoAjustes.ToList()) await j.ExcluirAnexoAsync(l.Id);
            Conf("legislacao_vazia", j.LinhasLegislacaoAjustes.Count == 0 && CopiasLeg().Count == 0);
            var leiFluxo = Path.Combine(fluxoLeg, "MUNICIPAL", "LEI 9.999-2099 - FICTICIA.pdf");
            var fotoLeiFluxo = FotoLeg(fluxoLeg);
            perguntas.ListasParaAbrir.Enqueue([leiFluxo]);
            var resumoLeg = await j.AnexarLegislacaoAsync();
            p["anexar"] = resumoLeg;
            Conf("uma_linha", resumoLeg == "1 PDF anexado." && j.LinhasLegislacaoAjustes.Count == 1);
            var idLeiFluxo = j.LinhasLegislacaoAjustes.Single().Id;
            Conf("titulo", await j.RenomearAnexoAsync(idLeiFluxo, TituloLei) is null && j.LinhasLegislacaoAjustes.Single().Titulo == TituloLei);

            // (11) Legislação: pesquisar em todas as leis, abrir o resultado da p. 2, selecionar e Copiar citação.
            await j.MudarModuloAsync(Modulo.Legislacao);
            await EsperarAteAsync(() => Task.FromResult(j.LeitorPdf is not null), 30000);
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoEscopoTodas);
            await j.DigitarPesquisaAsync("afastamento frontal");
            var paginasAchadas = j.ResultadosDaPesquisa.Select(r => r.Pagina).ToList();
            p["pesquisa_em_todas"] = j.CabecalhoResultados + " (p. " + string.Join(", ", paginasAchadas) + ")";
            Conf("pesquisa_em_todas", j.CabecalhoResultados == "2 resultados em 1 lei" && paginasAchadas.SequenceEqual([1, 2]));
            var resultado2 = j.ResultadosDaPesquisa.Single(r => r.Pagina == 2);
            var abriuResultado = await j.AbrirResultadoAsync(resultado2, "afastamento frontal");
            await EsperarPagina(2);
            p["contador"] = j.TextoResultadoPesquisa;
            Conf("abriu_o_da_pagina_2", abriuResultado && j.TextoResultadoPesquisa == "2 de 2" && j.RodapeLegislacao.StartsWith("Página 2 de ", StringComparison.Ordinal));
            await j.DigitarPesquisaAsync("");
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoPaginaInteira);
            var temTexto = await EsperarTextoDaPagina(2);
            // O PDF.js pode refazer a camada de texto da página depois de "Página inteira" (a escala nova chega depois do texto antigo): a seleção
            // feita nesse meio some. Seleciona de novo (até 3 vezes) até a faixa habilitar Copiar citação.
            var selecionou = false;
            var habilitouCitar = false;
            var tentativasSelecao = 0;
            while (temTexto && !habilitouCitar && tentativasSelecao < 3)
            {
                tentativasSelecao++;
                selecionou = (await SelecionarNoLeitor(2, "O afastamento frontal", 2, "3,00 m.")).GetBoolean();
                habilitouCitar = selecionou && await EsperarAteAsync(() => Task.FromResult(j.FaixaAtual.Habilitado(ComandosApp.LegislacaoCopiarCitacao)), 3000);
            }
            p["tentativas_de_selecao"] = tentativasSelecao;
            await j.ExecutarComandoAsync(ComandosApp.LegislacaoCopiarCitacao);
            var citacao = AreaTransferencia.Copiados.LastOrDefault();
            p["citacao"] = citacao;
            p["citacao_diagnostico"] = new JsonObject { ["tem_texto"] = temTexto, ["selecionou"] = selecionou, ["copiar_citacao_habilitado"] = habilitouCitar, ["estado"] = j.EstadoDosComandosLegislacao };
            Conf("copiar_citacao", selecionou && citacao == "“O afastamento frontal mínimo é de 3,00 m.” (Lei nº 9.999/2099 (fictícia), página 2)");
            await JsLeitor("document.getSelection().removeAllRanges(), true");

            // (12) Ajustes › Legislação › Excluir (confirmado): nenhuma linha, a cópia e o índice apagados, o original igual (sha, tamanho, data, atributos).
            var copiaLei = CopiaLeg(leiFluxo);
            var indiceLei = IndiceLeg(leiFluxo);
            var existiam = File.Exists(copiaLei) && File.Exists(indiceLei);
            await j.MudarModuloAsync(Modulo.Ajustes);
            var excluiuLei = await j.ExcluirAnexoAsync(idLeiFluxo);
            var fotoLeiDepois = FotoLeg(fluxoLeg);
            Conf("excluir_lei", existiam && excluiuLei && j.LinhasLegislacaoAjustes.Count == 0 && !File.Exists(copiaLei) && !File.Exists(indiceLei)
                && fotoLeiDepois.Count == fotoLeiFluxo.Count && fotoLeiFluxo.All(kv => fotoLeiDepois.TryGetValue(kv.Key, out var v) && v == kv.Value));

            // (13) Por fim: o registro da execução do início, o anexo original, nenhuma chave, nada gravado fora de tests\saida, só o agente falso iniciado.
            var execucao = ExecucaoJson(exec);
            var pmvrelRegistrado = execucao["pmvrel"]?.GetValue<string>();
            p["execucao"] = execucao["situacao"]?.GetValue<string>();
            Conf("execucao_concluida", execucao["situacao"]?.GetValue<string>() == "concluida" && pmvrelRegistrado is { Length: > 0 }
                && File.Exists(Path.IsPathRooted(pmvrelRegistrado) ? pmvrelRegistrado : Path.Combine(proc, pmvrelRegistrado))
                && exec.Registro.Pasta.StartsWith(Path.Combine(proc, ".pmvni-ai", "editor") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            var copiaDoAnexo = Path.Combine(proc, "PROJETO", "REVISÃO 01", "planta-ficticia.pdf");
            var fotoAnexoDepois = FotoLeg(Path.GetDirectoryName(fluxoAnexo)!);
            Conf("anexo_original_intacto", fotoAnexoDepois.Count == fotoAnexo.Count && fotoAnexo.All(kv => fotoAnexoDepois.TryGetValue(kv.Key, out var v) && v == kv.Value)
                && File.Exists(copiaDoAnexo) && ShaDe(copiaDoAnexo) == ShaDe(fluxoAnexo));
            var saidaTestes = Path.GetFullPath(Path.Combine(pasta, ".."));
            var trechosChaves = chavesDoRoteiro.SelectMany(TrechosDaChave).ToList();
            var varreduraFluxo = VarrerChave([saidaTestes], trechosChaves, ignorar: a => a.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase));
            p["chaves_procuradas"] = chavesDoRoteiro.Count;
            p["arquivos_varridos"] = varreduraFluxo.Lidos;
            p["arquivos_trancados_pulados"] = varreduraFluxo.Ilegiveis;
            p["chave_em_arquivos"] = varreduraFluxo.Achados.Count;
            // Passos independentes: basta haver uma chave para procurar (a herdada existe sempre; os passos 50 e 53 somam as deles).
            Conf("nenhuma_chave_em_tests_saida", varreduraFluxo.Achados.Count == 0 && varreduraFluxo.Lidos > 0 && chavesDoRoteiro.Count >= 1);
            var raizRepo = Path.GetFullPath(Path.Combine(saidaTestes, "..", ".."));
            var fora = EscritasFora(raizRepo, inicioDoRoteiro,
                [Path.Combine(saidaTestes, "roteiro"), Path.Combine(saidaTestes, "dados-app"), Path.Combine(saidaTestes, "agente-falso")]);
            p["gravados_fora_de_tests_saida"] = new JsonArray(fora.Take(10).Select(f => (JsonNode)f).ToArray());
            Conf("nada_gravado_fora_de_tests_saida", fora.Count == 0);
            string[] iniciadosAgora;
            lock (programasIniciados) iniciadosAgora = programasIniciados.ToArray();
            // Só o agente falso e o soffice permitido partiram (o despacho deste passo converte com o LibreOffice: ele aparece na lista).
            Conf("so_programas_permitidos_iniciados", iniciadosAgora.Length > 0 && ExecucaoProcesso.SomenteExecutaveisEm == pastaDoAgenteFalso
                && iniciadosAgora.All(c => TravaDeProgramas.Permitido(c, programasPermitidos))
                && (sofficeDoRoteiro is null || iniciadosAgora.Any(c => string.Equals(c, sofficeDoRoteiro, StringComparison.OrdinalIgnoreCase))));
            p["conferencias"] = conf;
            try { await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "64-fluxo-completo.png")); } catch (Exception e) { p["captura"] = e.Message; }
            p["ok"] = conf.All(kv => kv.Value!.GetValue<bool>());
        });

        // ---------- Rodada "Interface e configurações" (auditoria da interface): passos 65 a 69 ----------

        await Passo("65_trilho_icones_e_selecao", async p =>
        {
            var trilho = j.TrilhoAtual;
            var linhas = new JsonArray();
            var tudoOk = true;
            foreach (var m in Faixas.Disponiveis)
            {
                // Ctrl+1 … Ctrl+5 (o mesmo comando dos aceleradores da janela e da tecla repassada pela página).
                await j.ExecutarComandoAsync(Modulos.Comando(m));
                await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == m), 5000);
                await j.AguardarLeiturasAsync();
                var iconeOk = trilho.IconeCarregado(m);
                var soOAtivo = Faixas.Disponiveis.All(o => trilho.FundoVisivel(o) == (o == m) && (trilho.EstadoAcessivel(o) == "Selecionado") == (o == m));
                var ok = j.ModuloAtual == m && trilho.Ativo == m && iconeOk && soOAtivo;
                tudoOk &= ok;
                linhas.Add(new JsonObject { ["modulo"] = m.ToString(), ["atalho"] = "Ctrl+" + Modulos.Numero(m), ["icone_colorido"] = iconeOk, ["so_o_ativo_tem_fundo"] = soOAtivo, ["ok"] = ok });
            }
            p["modulos"] = linhas;
            // Setas entre os módulos (uma parada de Tab para o trilho, XYFocusKeyboardNavigation).
            await j.MudarModuloAsync(Modulo.Relatorios);
            trilho.Botao(Modulo.Relatorios)!.Focus(FocusState.Keyboard);
            await Esperar(150);
            var moveu = FocusManager.TryMoveFocus(FocusNavigationDirection.Down, new FindNextElementOptions { SearchRoot = j.ElementoRaiz.XamlRoot!.Content });
            await Esperar(150);
            var focado = FocusManager.GetFocusedElement(j.ElementoRaiz.XamlRoot);
            p["seta_para_baixo_vai_a_despachos"] = moveu && ReferenceEquals(focado, trilho.Botao(Modulo.Despachos));
            // Revisão I3: Processos e Despachos têm a tela vazia completa (título, botões) e a frase segue o texto da lista vazia.
            var textoVazio = j.TextoListaVaziaProcessos.Text;
            j.TextoListaVaziaProcessos.Text = "Frase de teste do roteiro";
            var fraseSegue = j.EstadoVazioProcessos.TextoFrase == "Frase de teste do roteiro";
            j.TextoListaVaziaProcessos.Text = textoVazio;
            var telasVazias = j.EstadoVazioProcessos.TextoTitulo == "Nenhum processo" && j.EstadoVazioProcessos.TextoPrimario == "Atualizar lista"
                && j.EstadoVazioDespachos.TextoTitulo == "Criar um despacho" && j.EstadoVazioDespachos.TextoPrimario == "Despacho de correção" && fraseSegue; // Despachos só cria (correções 2026-11-15)
            p["telas_vazias_de_processos_e_despachos"] = telasVazias;
            tudoOk &= telasVazias;
            // Captura do trilho nos dois temas (o roteiro não muda o tema guardado).
            j.DefinirTema(ElementTheme.Light);
            await Esperar(600);
            await CapturarXamlAsync(trilho, Path.Combine(pasta, "65-trilho-claro.png"));
            j.DefinirTema(ElementTheme.Dark);
            await Esperar(600);
            await CapturarXamlAsync(trilho, Path.Combine(pasta, "65-trilho-escuro.png"));
            j.DefinirTema(ElementTheme.Default);
            await Esperar(300);
            p["ok"] = tudoOk && p["seta_para_baixo_vai_a_despachos"]!.GetValue<bool>();
        });

        await Passo("66_avisos_de_layout_no_indicador", async p =>
        {
            await j.MudarModuloAsync(Modulo.Relatorios);
            var pacoteEstouro = Path.Combine(pasta, "estouro.rpmvni");
            if (!await j.AbrirPacoteAsync(pacoteEstouro)) throw new InvalidOperationException("não abriu o estouro: " + j.InfoMensagem.Message);
            await Esperar(500);
            var a = j.Aberto!;
            var plano = a.Composicao.Plano;
            var indicador = j.IndicadorLayout;
            var esperados = plano.Estouros.Count + (plano.AvisosLargura.Count > 0 ? 1 : 0) + a.Aplicacao.FolhasQueTransbordamNaLargura.Count;
            p["resumo"] = indicador.Resumo;
            p["avisos"] = new JsonArray(indicador.Avisos.Select(x => (JsonNode)$"{x.Tipo} f.{x.Folha}: {x.Texto}").ToArray());
            var contagemOk = indicador.Avisos.Count == esperados && indicador.Resumo == AvisosLayout.Resumo(indicador.Avisos) && indicador.Critico == (plano.Estouros.Count > 0);
            // "Ir à folha N" leva as folhas até lá.
            var linha = indicador.Avisos.ToList().FindIndex(x => x.Folha is not null);
            var folhaAlvo = linha >= 0 ? indicador.Avisos[linha].Folha!.Value : 0;
            if (linha >= 0) indicador.EscolherLinha(linha);
            var foi = linha >= 0 && await EsperarAteAsync(() => Task.FromResult(j.FolhaAtual == folhaAlvo), 5000);
            p["ir_a_folha"] = new JsonObject { ["folha"] = folhaAlvo, ["chegou"] = foi };
            // A área das folhas não muda de altura com os avisos no indicador; em "Faixas no topo" (Ajustes › Aparência) as InfoBars voltam.
            var alturaIndicador = j.AlturaDasFolhas;
            var pag = j.AjustesPagina;
            pag.CaixaAvisosLayout.SelectedIndex = AvisosLayout.Modos.ToList().IndexOf("faixas");
            await Esperar(500);
            var faixasAbertas = j.InfoEstouro.IsOpen == (plano.Estouros.Count > 0) && !indicador.Visivel;
            var alturaFaixas = j.AlturaDasFolhas;
            var guardadoFaixas = JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["avisos_layout"]?.GetValue<string>();
            pag.CaixaAvisosLayout.SelectedIndex = AvisosLayout.Modos.ToList().IndexOf("indicador");
            await Esperar(500);
            var alturaDeVolta = j.AlturaDasFolhas;
            p["alturas"] = new JsonObject { ["indicador"] = alturaIndicador, ["faixas"] = alturaFaixas, ["de_volta"] = alturaDeVolta };
            var alturaOk = Math.Abs(alturaDeVolta - alturaIndicador) < 0.5 && (esperados == 0 || alturaFaixas < alturaIndicador);
            // Área de avisos (M7): no máximo 2 à vista, o resto em "+n"; informativos fecham sozinhos (encurtado aqui para 1 s).
            var area = j.AreaAvisosRelatorios;
            j.InfoMensagem.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
            j.InfoMensagem.Title = "Roteiro";
            j.InfoMensagem.Message = "aviso 1";
            j.InfoMensagem.IsOpen = true;
            j.InfoBloqueio.IsOpen = true;
            j.InfoSomenteLeitura.IsOpen = true;
            await Esperar(300);
            var recolhidas = area.Recolhidas.Count;
            var mais = area.TextoMais;
            var maisOk = area.Abertas.Count >= 3 && recolhidas == area.Abertas.Count - AreaDeAvisos.MaxVisiveis && mais.StartsWith('+');
            j.InfoBloqueio.IsOpen = false;
            j.InfoSomenteLeitura.IsOpen = false;
            AreaDeAvisos.FecharSozinho = true;
            AreaDeAvisos.TempoParaFechar = TimeSpan.FromSeconds(1);
            try
            {
                j.InfoMensagem.IsOpen = false;
                j.InfoMensagem.Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
                j.InfoMensagem.Message = "informativo do roteiro";
                j.InfoMensagem.IsOpen = true;
                var fechouSozinho = await EsperarAteAsync(() => Task.FromResult(!j.InfoMensagem.IsOpen), 4000);
                p["informativo_fecha_sozinho"] = fechouSozinho;
            }
            finally
            {
                AreaDeAvisos.FecharSozinho = false;
                AreaDeAvisos.TempoParaFechar = TimeSpan.FromSeconds(8);
            }
            p["area_de_avisos"] = new JsonObject { ["recolhidas"] = recolhidas, ["texto_mais"] = mais };
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "66-indicador-de-layout.png"));
            p["ok"] = contagemOk && foi && faixasAbertas && guardadoFaixas == "faixas" && alturaOk && maisOk && p["informativo_fecha_sozinho"]!.GetValue<bool>()
                && !j.ExportarHabilitado && j.DicaExportar.StartsWith("A exportação está bloqueada", StringComparison.Ordinal);
            await j.FecharSessaoAsync();
        });

        await Passo("67_ajustes_categorias", async p =>
        {
            await j.MudarModuloAsync(Modulo.Ajustes);
            var pag = j.AjustesPagina;
            var ajustesJson = Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo);
            JsonNode? Guardado(string chave) => JsonNode.Parse(File.ReadAllText(ajustesJson))?[chave];
            var menuOk = pag.TitulosDoMenu.SequenceEqual(CategoriasAjustes.Menu.Select(c => c.Titulo));
            p["menu"] = new JsonArray(pag.TitulosDoMenu.Select(t => (JsonNode)t).ToArray());
            var categoriasOk = true;
            foreach (var c in CategoriasAjustes.Menu)
            {
                pag.MostrarCategoria(c.Tag);
                await Esperar(250);
                categoriasOk &= pag.CategoriaAtual == c.Tag;
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, $"67-ajustes-{c.Tag}.png"));
            }
            // Cada opção nova, escolhida na tela como o usuário, vai para ajustes.json e vale ao reabrir (Ajustes.Carregar).
            var recentesAntes = j.RecentesNosAjustes;
            var leisAntes = j.LinhasLegislacaoAjustes.Count;
            var modoAgente = j.AjustesAtuais.Agentes[Provedor.ChatGpt].Modo;
            static void Escolher(Microsoft.UI.Xaml.Controls.ComboBox c, object valor) =>
                c.SelectedItem = c.Items.OfType<Microsoft.UI.Xaml.Controls.ComboBoxItem>().First(i => Equals(i.Tag, valor));
            // Revisão m9: se o passo falhar no meio, os ajustes voltam ao padrão mesmo assim (confirmações ligadas para os passos seguintes).
            var restaurado = false;
            try
            {
            Escolher(pag.CaixaAbrirEm, "Processos");
            Escolher(pag.CaixaRecentes, 20);
            Escolher(pag.CaixaRotulosFaixa, "icones");
            var soIcones = j.FaixaAtual.Compacta;
            Escolher(pag.CaixaRotulosFaixa, "sempre");
            var sempreRotulos = !j.FaixaAtual.Compacta;
            Escolher(pag.CaixaZoomInicial, "pagina");
            Escolher(pag.CaixaRecuperacao, 90);
            Escolher(pag.CaixaOrdemProcessos, "requerente");
            Escolher(pag.CaixaAgruparDespachos, "processo");
            Escolher(pag.CaixaOrdemDespachos, "antigos");
            Escolher(pag.CaixaEscopo, "todas");
            pag.AlternarJanela.IsOn = true;
            pag.AlternarPaineis.IsOn = true;
            pag.AlternarAbrirPdf.IsOn = true;
            pag.AlternarConfirmarCancelar.IsOn = false;
            pag.AlternarConfirmarFechar.IsOn = false;
            pag.MostrarCategoria(CategoriasAjustes.Ia);
            await Esperar(200);
            pag.CaixaPedidoPadrao.Focus(FocusState.Keyboard);
            pag.CaixaPedidoPadrao.Text = "Pedido padrão do roteiro";
            pag.BotaoVerAtalhos.Focus(FocusState.Keyboard);
            pag.CartaoChatGpt.BotaoTestar.Focus(FocusState.Keyboard);
            await Esperar(300);
            var esperado = new Dictionary<string, string>
            {
                ["abrir_em"] = "Processos", ["recentes_max"] = "20", ["faixa_rotulos"] = "sempre", ["zoom_inicial"] = "pagina", ["recuperacao_dias"] = "90",
                ["processos_ordem"] = "requerente", ["despachos_agrupar"] = "processo", ["despachos_ordem"] = "antigos", ["legislacao_escopo"] = "todas",
                ["lembrar_janela"] = "true", ["lembrar_paineis"] = "true", ["despacho_abrir_pdf"] = "true",
                ["confirmar_cancelar"] = "false", ["confirmar_fechar"] = "false", ["pedido_padrao"] = "Pedido padrão do roteiro",
            };
            var gravadas = new JsonObject();
            var gravadasOk = true;
            foreach (var (chave, valor) in esperado)
            {
                var g = Guardado(chave);
                var texto = g is JsonValue v && v.TryGetValue<bool>(out var b) ? (b ? "true" : "false") : g?.ToString();
                gravadas[chave] = texto;
                gravadasOk &= texto == valor;
            }
            p["gravadas_em_ajustes_json"] = gravadas;
            var reaberto = Ajustes.Carregar(PastaDados.Raiz);
            var reabertoOk = reaberto.AbrirEm == "Processos" && reaberto.ModuloAoAbrir() == Modulo.Processos && reaberto.RecentesMax == 20 && reaberto.ZoomInicial == "pagina"
                && reaberto.RecuperacaoDias == 90 && reaberto.ProcessosOrdem == "requerente" && reaberto.LegislacaoEscopo == "todas" && reaberto.LembrarPaineis
                && !reaberto.ConfirmarCancelar && reaberto.PedidoPadrao == "Pedido padrão do roteiro";
            // Restaurar os ajustes (com a confirmação respondida "Restaurar"): tudo ao padrão; chaves, leis e recentes ficam.
            perguntas.Confirmar = true;
            var restaurou = await j.RestaurarAjustesAsync();
            restaurado = restaurou;
            var padrao = new Ajustes();
            var a = j.AjustesAtuais;
            var restaurouOk = restaurou && a.AbrirEm == padrao.AbrirEm && a.RecentesMax == padrao.RecentesMax && a.FaixaRotulos == padrao.FaixaRotulos
                && a.ZoomInicial == padrao.ZoomInicial && a.RecuperacaoDias == padrao.RecuperacaoDias && a.LembrarJanela == padrao.LembrarJanela
                && a.ConfirmarCancelar && a.PedidoPadrao == "" && Guardado("abrir_em")?.GetValue<string>() == "ultimo";
            var preservou = j.RecentesNosAjustes.SequenceEqual(recentesAntes) && j.LinhasLegislacaoAjustes.Count == leisAntes && a.Agentes[Provedor.ChatGpt].Modo == modoAgente;
            p["restaurar"] = new JsonObject { ["restaurou"] = restaurouOk, ["preservou_recentes_leis_e_agentes"] = preservou };
            p["conferencias"] = new JsonObject
            {
                ["menu_com_8_categorias"] = menuOk, ["cada_categoria_abre"] = categoriasOk, ["so_icones"] = soIcones, ["sempre_rotulos"] = sempreRotulos,
                ["gravadas"] = gravadasOk, ["valem_ao_reabrir"] = reabertoOk,
            };
            p["ok"] = menuOk && categoriasOk && soIcones && sempreRotulos && gravadasOk && reabertoOk && restaurouOk && preservou;
            }
            finally
            {
                if (!restaurado) { perguntas.Confirmar = true; await j.RestaurarAjustesAsync(); }
                // O roteiro volta ao que os passos seguintes esperam (pasta de processos da cópia fictícia e salvamento automático em 15 s).
                j.DefinirPastaProcessos(copiaProcessos);
                j.DefinirIntervaloAutosave(15);
            }
            await j.MudarModuloAsync(Modulo.Relatorios);
        });

        await Passo("68_nomes_acessiveis_das_listas", async p =>
        {
            // Nenhum item de lista com nome igual ao do tipo ("Pmvni.App.ItemNavegacao"): o Narrador lê o que a linha mostra.
            static List<string> Nomes(Microsoft.UI.Xaml.Controls.ListViewBase lista)
            {
                var nomes = new List<string>();
                for (var i = 0; i < lista.Items.Count; i++)
                    if (lista.ContainerFromIndex(i) is Microsoft.UI.Xaml.UIElement c && FrameworkElementAutomationPeer.CreatePeerForElement(c) is { } peer)
                        nomes.Add(peer.GetName());
                return nomes;
            }
            static bool Bons(List<string> nomes) => nomes.Count > 0 && nomes.All(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith("Pmvni.", StringComparison.Ordinal));
            await j.MudarModuloAsync(Modulo.Relatorios);
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não abriu o longo: " + j.InfoMensagem.Message);
            await Esperar(500);
            var secoes = Nomes(j.ListaDeSecoes);
            var itemComSituacao = j.Itens.FirstOrDefault(i => i.Situacao is not null);
            var situacaoPorExtenso = itemComSituacao is not null && itemComSituacao.NomeAcessivel.Contains(itemComSituacao.TextoSituacao, StringComparison.Ordinal);
            // Correções 2026-11-15: a situação na lista é a etiqueta do relatório (texto do kit) e é modificável: escolher outra no menu dela
            // usa o comando da faixa (DefinirSituacao, no histórico) e abre as Propriedades para a justificativa; Desfazer volta.
            var etiquetas = j.EtiquetasNaListaDeSecoes();
            p["etiquetas"] = new JsonArray(etiquetas.Take(4).Select(n => (JsonNode)n).ToArray());
            var etiquetasOk = etiquetas.Count > 0 && etiquetas.All(n => n.StartsWith("Situação: ", StringComparison.Ordinal))
                && itemComSituacao is not null && etiquetas.Contains(EtiquetasSituacao.NomeAcessivel(itemComSituacao.Situacao));
            var sessaoLonga = j.Sessao!;
            var alvo = sessaoLonga.Revisao.Projeto.Incluidos.First(i => i.Situacao.Revisada is null);
            var antesDaEtiqueta = alvo.Situacao.Original!.GetValue<string>();
            var novaPelaEtiqueta = Faixas.OrdemSituacoes.First(c => c != antesDaEtiqueta);
            var registrosAntes = sessaoLonga.Revisao.HistoricoCompleto.Count;
            var propriedadesAntes = j.PropriedadesVisivel;
            await j.MudarSituacaoPelaEtiquetaAsync(alvo.Id, novaPelaEtiqueta);
            var tiposEtiqueta = sessaoLonga.Revisao.HistoricoCompleto.Skip(registrosAntes).Select(r => r.Tipo).ToList();
            var mudouPelaEtiqueta = alvo.Situacao.Revisada == novaPelaEtiqueta && tiposEtiqueta.SequenceEqual(["DefinirSituacao"])
                && j.Itens.Any(i => i.Ref == alvo.Id && i.Situacao == novaPelaEtiqueta) && j.PropriedadesVisivel;
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var desfeitoPelaEtiqueta = alvo.Situacao.Revisada is null;
            if (!propriedadesAntes && j.PropriedadesVisivel) await j.ExecutarComandoAsync(ComandosApp.ExibirPropriedades);
            p["etiqueta_muda_a_situacao"] = mudouPelaEtiqueta;
            p["etiqueta_desfaz"] = desfeitoPelaEtiqueta;
            etiquetasOk &= mudouPelaEtiqueta && desfeitoPelaEtiqueta;
            p["etiquetas_ok"] = etiquetasOk;
            // A lista só tem contêineres depois de ter itens e de um layout com a página à vista.
            async Task<List<string>> NomesDepoisDeLer(Microsoft.UI.Xaml.Controls.ListViewBase lista)
            {
                await j.AguardarLeiturasAsync();
                await EsperarAteAsync(() => Task.FromResult(lista.Items.Count > 0), 5000);
                lista.UpdateLayout();
                await Esperar(200);
                return Nomes(lista);
            }
            await j.MudarModuloAsync(Modulo.Processos);
            var processos = await NomesDepoisDeLer(j.ListaDeProcessos);
            p["itens_processos"] = j.ListaDeProcessos.Items.Count;
            await j.MudarModuloAsync(Modulo.Despachos);
            var despachos = await NomesDepoisDeLer(j.ListaDeDespachos);
            p["itens_despachos"] = j.ListaDeDespachos.Items.Count;
            var trilho = Faixas.Disponiveis.Select(m => AutomationProperties.GetName(j.TrilhoAtual.Botao(m)!)).ToList();
            p["secoes"] = new JsonArray(secoes.Take(6).Select(n => (JsonNode)n).ToArray());
            p["processos"] = new JsonArray(processos.Take(4).Select(n => (JsonNode)n).ToArray());
            p["despachos"] = new JsonArray(despachos.Take(4).Select(n => (JsonNode)n).ToArray());
            p["trilho"] = new JsonArray(trilho.Select(n => (JsonNode)n).ToArray());
            p["situacao_por_extenso"] = situacaoPorExtenso;
            p["ok"] = Bons(secoes) && Bons(processos) && situacaoPorExtenso && etiquetasOk /* a lista de despachos saiu da tela (correções 2026-11-15) */ && trilho.SequenceEqual(Faixas.Disponiveis.Select(Modulos.Rotulo));
            await j.MudarModuloAsync(Modulo.Relatorios);
        });

        // Correções 2026-11-15 (dicas dos botões): todo botão da faixa (primário, os das abas e os itens dos menus) e do trilho tem a dica
        // no formato do app, com título (nome e atalho) e uma descrição do que faz; "Abrir" mostra "Abrir (Ctrl+O)" e não só o atalho.
        await Passo("68b_dicas_dos_botoes", async p =>
        {
            var semDica = new JsonArray();
            var conferidos = 0;
            var abrirOk = false;
            foreach (var m in Faixas.Disponiveis)
            {
                await j.MudarModuloAsync(m);
                await j.AguardarLeiturasAsync();
                var def = Faixas.Para(m);
                var faixa = j.FaixaAtual;
                foreach (var aba in def.Abas)
                {
                    faixa.EscolherAba(aba.Nome);
                    await Esperar(80);
                    foreach (var (id, dica) in faixa.DicasVisiveis)
                    {
                        conferidos++;
                        if (m == Modulo.Relatorios && id == ComandosApp.ArquivoAbrir) abrirOk = dica?.Titulo == "Abrir (Ctrl+O)";
                        if (dica is { Titulo.Length: > 0, Descricao.Length: > 0 }) continue;
                        semDica.Add(new JsonObject { ["modulo"] = m.ToString(), ["aba"] = aba.Nome, ["id"] = id, ["dica"] = dica?.ToString() });
                    }
                }
                faixa.EscolherAba(def.Abas[def.AbaPadrao].Nome);
                var noTrilho = Dicas.De(j.TrilhoAtual.Botao(m)!);
                conferidos++;
                if (noTrilho is not { Titulo.Length: > 0, Descricao.Length: > 0 })
                    semDica.Add(new JsonObject { ["modulo"] = m.ToString(), ["aba"] = "trilho", ["id"] = Modulos.Comando(m), ["dica"] = noTrilho?.ToString() });
            }
            p["botoes_conferidos"] = conferidos;
            p["sem_dica"] = semDica;
            p["abrir_com_titulo_e_atalho"] = abrirOk;
            p["ok"] = conferidos > 0 && semDica.Count == 0 && abrirOk;
            await j.MudarModuloAsync(Modulo.Relatorios);
        });

        await Passo("69_botao_primario", async p =>
        {
            var linhas = new JsonArray();
            var tudoOk = true;
            foreach (var m in Faixas.Disponiveis)
            {
                await j.MudarModuloAsync(m);
                await j.AguardarLeiturasAsync();
                var def = Faixas.Para(m);
                var faixa = j.FaixaAtual;
                var linha = new JsonObject { ["modulo"] = m.ToString(), ["primario"] = faixa.IdPrimario, ["rotulo"] = faixa.RotuloPrimario };
                bool ok;
                if (def.Primario is not { } primario) ok = faixa.IdPrimario is null && faixa.BotaoPrimario is null;
                else
                {
                    var botao = faixa.BotaoPrimario!;
                    var tipoOk = (primario.Tipo == TipoComando.Menu) == (botao is Microsoft.UI.Xaml.Controls.DropDownButton);
                    // Desabilitado mostra o motivo na dica (Habilitar/DefinirDica guardam por id e valem para o primário).
                    var habilitadoAntes = faixa.Habilitado(primario.Id);
                    var dicaAntes = faixa.Dica(primario.Id);
                    faixa.Habilitar(primario.Id, false);
                    faixa.DefinirDica(primario.Id, "Motivo de teste do roteiro");
                    var desabilitadoOk = !botao.IsEnabled && Dicas.De(botao)?.Motivo == "Motivo de teste do roteiro";
                    faixa.Habilitar(primario.Id, habilitadoAntes);
                    faixa.DefinirDica(primario.Id, dicaAntes.Length > 0 ? dicaAntes : primario.TextoCompleto);
                    // O primário fica em todas as abas do módulo.
                    var emTodas = true;
                    foreach (var aba in def.Abas)
                    {
                        faixa.EscolherAba(aba.Nome);
                        await Esperar(80);
                        emTodas &= faixa.IdPrimario == primario.Id;
                    }
                    faixa.EscolherAba(def.Abas[def.AbaPadrao].Nome);
                    await CapturarXamlAsync(faixa, Path.Combine(pasta, $"69-primario-{m.ToString().ToLowerInvariant()}.png"));
                    linha["tipo_ok"] = tipoOk;
                    linha["desabilitado_com_motivo"] = desabilitadoOk;
                    linha["em_todas_as_abas"] = emTodas;
                    ok = faixa.IdPrimario == primario.Id && faixa.RotuloPrimario == primario.TextoCompleto && tipoOk && desabilitadoOk && emTodas
                        && botao.IsEnabled == habilitadoAntes;
                }
                linha["ok"] = ok;
                tudoOk &= ok;
                linhas.Add(linha);
            }
            p["modulos"] = linhas;
            // Revisão I1: depois de passar por Despachos (primário Novo despacho ▾), os itens Correção/Aprovação do menu da Página Inicial de
            // Processos e de Relatórios continuam recebendo Habilitar e a dica.
            var itensDaAba = new JsonArray();
            foreach (var m in new[] { Modulo.Processos, Modulo.Relatorios })
            {
                await j.MudarModuloAsync(Modulo.Despachos);
                await j.MudarModuloAsync(m);
                await j.AguardarLeiturasAsync();
                var faixa = j.FaixaAtual;
                var abaInicial = faixa.Definicao!.Abas.First(a => a.Comandos.Any(c => c.Id == ComandosApp.DespachosNovo)).Nome;
                faixa.EscolherAba(abaInicial);
                await Esperar(80);
                var item = faixa.ItemDoMenuDaAba(ComandosApp.DespachosNovoCorrecao);
                var antes = faixa.Habilitado(ComandosApp.DespachosNovoCorrecao);
                var dicaAntes = faixa.Dica(ComandosApp.DespachosNovoCorrecao);
                faixa.Habilitar(ComandosApp.DespachosNovoCorrecao, false);
                faixa.DefinirDica(ComandosApp.DespachosNovoCorrecao, "Motivo de teste do roteiro");
                var desligou = item is { IsEnabled: false } && Dicas.De(item)?.Motivo == "Motivo de teste do roteiro";
                faixa.Habilitar(ComandosApp.DespachosNovoCorrecao, antes);
                faixa.DefinirDica(ComandosApp.DespachosNovoCorrecao, dicaAntes);
                var voltou = item is not null && item.IsEnabled == antes;
                itensDaAba.Add(new JsonObject { ["modulo"] = m.ToString(), ["aba"] = abaInicial, ["achou_item"] = item is not null, ["atualiza"] = desligou && voltou });
                tudoOk &= desligou && voltou;
            }
            p["novo_despacho_na_aba_depois_de_despachos"] = itensDaAba;
            // Capturas da janela para mostrar ao usuário (tests\saida\capturas, ignorada pelo git): trilho com o módulo ativo, faixa e página,
            // nos temas claro e escuro (o tema guardado não muda). Último passo: nada mais confere escritas depois daqui.
            var capturas = Path.Combine(Path.GetDirectoryName(pasta)!, "capturas");
            Directory.CreateDirectory(capturas);
            var arquivosCaptura = new JsonArray();
            async Task Capturar(FrameworkElement alvo, string nome)
            {
                await CapturarXamlAsync(alvo, Path.Combine(capturas, nome));
                arquivosCaptura.Add(nome);
            }
            foreach (var (tema, nome) in new[] { (ElementTheme.Light, "claro"), (ElementTheme.Dark, "escuro") })
            {
                j.DefinirTema(tema);
                await j.MudarModuloAsync(Modulo.Ajustes);
                j.AjustesPagina.MostrarCategoria(CategoriasAjustes.Geral);
                await Esperar(800);
                await Capturar(j.ElementoRaiz, $"janela-ajustes-{nome}.png");
                await j.MudarModuloAsync(Modulo.Processos);
                await j.AguardarLeiturasAsync();
                await Esperar(800);
                await Capturar(j.ElementoRaiz, $"janela-processos-{nome}.png");
                await Capturar(j.TrilhoAtual, $"trilho-{nome}.png");
            }
            j.DefinirTema(ElementTheme.Default);
            await Esperar(300);
            p["capturas"] = arquivosCaptura;
            p["comandos_sem_manipulador"] = new JsonArray(j.ComandosSemManipulador().Select(c => (JsonNode)c).ToArray());
            p["ok"] = tudoOk && j.ComandosSemManipulador().Count == 0;
            await j.MudarModuloAsync(Modulo.Relatorios);
        });

        await Passo("progresso_visivel_das_seis_etapas", p =>
        {
            var monitor = (PaginaNovoRelatorio)j.ElementoRaiz.FindName("PaginaDeNovoRelatorio");
            var valores = new JsonArray();
            var tabelaDoKit = ModelosDaAnalise.ArquivoNoKit(j.PastaKitEfetiva);
            var arquivoEtapas = Path.Combine(Path.GetDirectoryName(tabelaDoKit)!, "etapas.json");
            var ordem = OrdemDasEtapas.Ler(arquivoEtapas);
            var etapas = ordem.Para(Provedor.ChatGpt);
            var tabela = ModelosDaAnalise.LerTabela(File.ReadAllText(tabelaDoKit)) ?? throw new InvalidOperationException("A tabela do kit não abriu.");
            var etapasDoKit = ModelosDaAnalise.LerEtapas(File.ReadAllText(arquivoEtapas), tabela);
            var etapasDePrograma = etapasDoKit.Where(e => e.Programa).Select(e => e.Id).ToArray();
            var nome = FluxoNovoRelatorio.NomeDaEtapa("technical_audit");
            p["ordem_do_kit"] = new JsonArray(etapas.Select(e => (JsonNode)e).ToArray());
            p["etapas_de_programa"] = new JsonArray(etapasDePrograma.Select(e => (JsonNode)e).ToArray());
            monitor.MostrarProgresso(null);
            monitor.MostrarEstimativa(null);
            monitor.MostrarResumo(true, null, FluxoNovoRelatorio.Etapas(null, "INICIANDO", etapas));
            valores.Add(monitor.BarraResumoEtapas.Value);
            monitor.MostrarResumo(true, null, FluxoNovoRelatorio.Etapas("technical_audit", "EM_ANDAMENTO", etapas));
            valores.Add(monitor.BarraResumoEtapas.Value);
            var pelaContagem = !monitor.BarraResumoEtapas.IsIndeterminate
                && monitor.RotuloEtapa(nome, EstadoEtapa.Agora, true) == "Em andamento";
            monitor.MostrarProgresso(new ProgressoEtapa("technical_audit", 1, 5, 10));
            valores.Add(monitor.BarraResumoEtapas.Value);
            var rotuloDosItens = monitor.RotuloEtapa(nome, EstadoEtapa.Agora, true) == "Em andamento · 50% dos itens avaliados";
            monitor.MostrarEstimativa(new EstimativaAnalise("technical_audit", 72.4, 48.6, null));
            valores.Add(monitor.BarraResumoEtapas.Value);
            var rotuloDaEstimativa = monitor.RotuloEtapa(nome, EstadoEtapa.Agora, true).Contains("estimado pela IA", StringComparison.Ordinal);
            monitor.MostrarResumo(false, null, FluxoNovoRelatorio.Etapas(null, "CONCLUIDO", etapas));
            valores.Add(monitor.BarraResumoEtapas.Value);
            monitor.MostrarEstimativa(null);
            monitor.MostrarResumo(true, null, []);
            p["percentuais"] = valores;
            p["conferencias"] = new JsonObject { ["pela_contagem"] = pelaContagem, ["rotulo_dos_itens"] = rotuloDosItens, ["rotulo_da_estimativa"] = rotuloDaEstimativa };
            p["ok"] = ordem.DoArquivo && etapas.SequenceEqual(["prepare", "analyze", "technical_audit", "audit", "produce", "final_review"])
                && etapasDePrograma.SequenceEqual(["prepare", "audit", "produce", "final_review"])
                && pelaContagem && rotuloDosItens && rotuloDaEstimativa && monitor.BarraResumoEtapas.IsIndeterminate
                && valores.Select(x => x!.GetValue<double>()).Zip(new[] { 0d, 200d / 6, 200d / 6, 48.6, 100d })
                    .All(par => Math.Abs(par.First - par.Second) < 0.01);
            monitor.MostrarResumo(false, null, []);
            monitor.MostrarProgresso(null);
            return Task.CompletedTask;
        });

        // Tarefa 14: o kit achou duas versões da planta (decisão do tipo "pecas"). O Editor mostra as duas lado a lado (miniatura do cache-pdf ou o nome do
        // arquivo), com "Abrir o PDF" em cada uma; a escolha volta como o caminho da opção e "Cancelar" deixa a análise parada (nada é gravado aqui).
        await Passo("escolha_de_pecas_ambiguas", async p =>
        {
            var caso = Path.Combine(pasta, "pecas-ambiguas");
            if (Directory.Exists(caso)) Directory.Delete(caso, true);
            var a = Path.Combine(caso, "PROJETO", "INICIAL", "planta.pdf");
            var b = Path.Combine(caso, "PROJETO", "REVISÃO 01", "planta.pdf");
            foreach (var (arq, texto) in new[] { (a, "%PDF-1.4 versão A"), (b, "%PDF-1.4 versão B") })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(arq)!);
                File.WriteAllText(arq, texto);
            }
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a))).ToLowerInvariant();
            var paginas = Path.Combine(caso, ".pmvni-ai", "cache-pdf", "v1", "pdfium-roteiro", sha, "paginas");
            Directory.CreateDirectory(paginas);
            File.WriteAllBytes(Path.Combine(paginas, sha[..12] + "_p001_150dpi.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
            var decisao = SituacaoAnalise62.Ler(new JsonObject
            {
                ["situacao"] = "DECISAO", ["etapa"] = "prepare",
                ["decisao"] = new JsonObject
                {
                    ["tipo"] = "pecas", ["id"] = "pecas-roteiro", ["pergunta"] = "Há duas versões da planta baixa com a mesma data. Qual vale para a análise?",
                    ["opcoes"] = new JsonArray(new JsonObject { ["valor"] = @"PROJETO\INICIAL\planta.pdf", ["texto"] = "Planta da pasta INICIAL" },
                        new JsonObject { ["valor"] = @"PROJETO\REVISÃO 01\planta.pdf", ["texto"] = "Planta da REVISÃO 01" }),
                    ["arquivo_resposta"] = "decisao_resposta.json",
                },
            }.ToJsonString()).Decisao!;

            // O caminho do Editor: escolher a segunda devolve o caminho dela; cancelar devolve nada.
            perguntas.VersaoDaPeca = 1;
            var escolhida = await j.EscolherVersaoDaPecaAsync(caso, "Qual versão da peça vale?", Provedor.Claude, decisao);
            var pergunta = perguntas.EscolhasDePeca[^1];
            perguntas.VersaoDaPeca = null;
            var cancelada = await j.EscolherVersaoDaPecaAsync(caso, "Qual versão da peça vale?", Provedor.Gemini, decisao);
            var caminho = escolhida == @"PROJETO\REVISÃO 01\planta.pdf" && cancelada is null
                && pergunta.Motivo == decisao.Pergunta && pergunta.Versoes.Count == 2
                && pergunta.Versoes[0].Miniatura is { } m && m.StartsWith(paginas, StringComparison.OrdinalIgnoreCase) && pergunta.Versoes[1].Miniatura is null
                && pergunta.Versoes[1].Arquivo == b;

            // O diálogo: miniatura na primeira, o nome do arquivo na segunda, "Abrir o PDF" nas duas (no roteiro só registra), a marcação vira a escolha.
            var dialogo = new EscolhaDePecas(decisao.Pergunta, "A análise está parada.", pergunta.Versoes);
            var marcou = false;
            dialogo.EscolhaMudou += () => marcou = true;
            var abertas = AberturaExterna.Registradas.Count;
            var abriu = dialogo.Abrir(1) && AberturaExterna.Registradas.Count == abertas + 1 && AberturaExterna.Registradas[^1] == b;
            dialogo.Marcar(1);
            var tela = dialogo.Quantas == 2 && dialogo.MostraMiniatura(0) && !dialogo.MostraMiniatura(1) && dialogo.PodeAbrir(0) && dialogo.PodeAbrir(1)
                && marcou && dialogo.Escolhida?.Valor == @"PROJETO\REVISÃO 01\planta.pdf" && dialogo.TextoDaEscolha.StartsWith("Escolhida: Planta da REVISÃO 01", StringComparison.Ordinal);
            p["partes"] = new JsonObject { ["caminho"] = caminho, ["abriu"] = abriu, ["tela"] = tela };
            p["ok"] = caminho && abriu && tela && !File.Exists(Path.Combine(caso, ".pmvni-ai", "analise", "decisao_resposta.json"));
            Directory.Delete(caso, true);
        });

        await Passo("historico_das_paginas", async p =>
        {
            await j.MudarModuloAsync(Modulo.Processos);
            await j.MudarModuloAsync(Modulo.Legislacao);
            await j.MudarModuloAsync(Modulo.Ajustes);
            var voltar = (Button)j.ElementoRaiz.FindName("BotaoPesquisaAnterior");
            var avancar = (Button)j.ElementoRaiz.FindName("BotaoPesquisaProxima");
            Invocar(voltar);
            var voltou = await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == Modulo.Legislacao && avancar.IsEnabled), 5000);
            Invocar(avancar);
            var avancou = await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == Modulo.Ajustes && voltar.IsEnabled), 5000);
            Invocar(voltar);
            await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == Modulo.Legislacao && avancar.IsEnabled), 5000);
            await j.MudarModuloAsync(Modulo.Relatorios);
            p["ok"] = voltou && avancou && !avancar.IsEnabled;
        });

        await Passo("70_estabilidade_do_cabecalho", async p =>
        {
            var raiz = j.ElementoRaiz;
            var campo = (FrameworkElement)raiz.FindName("MolduraPesquisa");
            var busca = (TextBlock)raiz.FindName("TextoPesquisa");
            var analise = (FrameworkElement)raiz.FindName("MolduraAnalise");
            var botao = (FrameworkElement)raiz.FindName("BotaoAnalise");
            var antes = j.AppWindow.Size;
            var escala = raiz.XamlRoot?.RasterizationScale ?? throw new InvalidOperationException("A janela não está montada.");
            var amostras = new JsonArray();
            var recuos = new List<double>();
            var desviosLaterais = new List<double>();
            var folgas = new List<double>();
            var moldura = (FrameworkElement)raiz.FindName("MolduraBarras");
            var paginas = (FrameworkElement)raiz.FindName("AreaPaginas");
            var relatorios = (PaginaRelatorios)raiz.FindName("Relatorios");
            var barras = new[] { paginas, (FrameworkElement)relatorios.FindName("CorpoRelatorio"),
                (FrameworkElement)relatorios.FindName("BarraRodape"), relatorios.AreaAvisos };
            var alturas = new List<double>();
            try
            {
                foreach (var largura in new[] { 1000, 1280, 1550 })
                {
                    j.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(largura * escala), antes.Height));
                    foreach (var ativo in new[] { false, true })
                    {
                        busca.Text = ativo ? "1 de 10" : "";
                        analise.Visibility = botao.Visibility = ativo ? Visibility.Visible : Visibility.Collapsed;
                        await Esperar(250);
                        var centros = new[] { campo, (FrameworkElement)raiz.FindName("GrupoHistorico"), (FrameworkElement)raiz.FindName("GrupoOcorrencias") }
                            .Select(e => e.TransformToVisual(raiz).TransformPoint(new Windows.Foundation.Point()).Y + e.ActualHeight / 2).ToArray();
                        alturas.AddRange(centros);
                        var extremidade = j.FaixaAtual.TransformToVisual(raiz).TransformPoint(new Windows.Foundation.Point(j.FaixaAtual.ActualWidth, 0)).X;
                        recuos.Add(raiz.ActualWidth - extremidade);
                        var origem = moldura.TransformToVisual(raiz).TransformPoint(new Windows.Foundation.Point()).X;
                        folgas.Add(extremidade - j.FaixaAtual.ActualWidth - origem);
                        folgas.Add(origem + moldura.ActualWidth - extremidade);
                        foreach (var barra in barras)
                        {
                            var cantoPagina = barra.TransformToVisual(raiz).TransformPoint(new Windows.Foundation.Point());
                            desviosLaterais.Add(Math.Abs(cantoPagina.X + barra.ActualWidth - extremidade));
                            desviosLaterais.Add(Math.Abs(cantoPagina.X - (extremidade - j.FaixaAtual.ActualWidth)));
                        }
                        amostras.Add(new JsonObject { ["largura"] = raiz.ActualWidth, ["indicador"] = ativo, ["centro_y"] = centros[0], ["largura_pesquisa"] = campo.ActualWidth });
                    }
                }
                p["amostras"] = amostras;
                p["deslocamento_vertical_dip"] = alturas.Max() - alturas.Min();
                p["recuo_direito_barra_dip"] = recuos.Min();
                p["desalinhamento_lateral_dip"] = desviosLaterais.Max();
                p["folga_entre_camadas_dip"] = folgas.Min();
                p["ok"] = folgas.All(v => Math.Abs(v - 8) < 1) && desviosLaterais.Max() < 1 && alturas.Max() - alturas.Min() < 1 && recuos.All(valor => Math.Abs(valor - 13) < 1);
            }
            finally { j.AppWindow.Resize(antes); }
        });

        await Passo("rever_tabela_sem_ia", async p =>
        {
            // Tarefa 15: "Rever" numa tabela de índices é conta do editor, sem IA: a TO de 240,01 ÷ 300,00 escrita "80,0%" (ATENDE) passa a
            // NÃO ATENDE (a conta exata é 80,0033%), o aviso mostra o valor de antes e o de agora, e um Desfazer volta tudo. Com uma instrução
            // digitada na justificativa (Tarefa 15, correção 1), o botão principal continua "Rever (sem IA)" e "Rever com a IA" aparece como
            // segunda opção (PropReverComIa; não é clicada aqui).
            const string To = "indices/02";
            var amostra = Path.Combine(pasta, "amostra-rever-sem-ia");
            CopiarArvore(Path.Combine(corpus, "img"), Path.Combine(amostra, "img"));
            var json = Path.Combine(amostra, "rever-sem-ia.json");
            var no = JsonNode.Parse(await File.ReadAllTextAsync(dadosLongo))!;
            var to = no["indices"]!["linhas"]![1]!.AsObject();
            to["icone"] = "to";
            to["nome"] = "Taxa de ocupação";
            to["calculo"] = "240,01 ÷ 300,00 × 100 = 80,0%";
            to["projeto"] = "80,0%";
            to["limite"] = "≤ 80%";
            to["situacao"] = "CONTA_CONFERE";
            to["texto"] = "Dentro do limite.";
            await File.WriteAllTextAsync(json, no.ToJsonString());
            try
            {
                if (!await j.ImportarParaAsync(json, Path.Combine(pasta, "rever-sem-ia.rpmvni"))) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
                var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
                await j.SelecionarAsync(To);
                var rotulo = j.RotuloDoBotaoDaIa;
                var antes = ProjetoJson.Escrever(s.Revisao.Projeto);
                int iniciadosAntes;
                lock (programasIniciados) iniciadosAntes = programasIniciados.Count;
                await j.AjustarTextoComIaAsync();
                var aviso = j.InfoMensagemAtual;
                p["aviso"] = aviso.Title + " · " + aviso.Message;
                var reviu = TabelasRevisao.SituacaoEfetiva(s.Revisao.Projeto, To) == "NAO_ATENDE" && TabelasRevisao.TextoEfetivo(s.Revisao.Projeto, To, "projeto") == "80,003%"
                    && aviso.IsOpen && aviso.Title == "Rever" && aviso.Message.StartsWith("Revisto pelo editor, sem IA: ", StringComparison.Ordinal)
                    && aviso.Message.Contains("ÍNDICE 02 — No projeto: 80,0% → 80,003%", StringComparison.Ordinal)
                    && aviso.Message.Contains("ÍNDICE 02 — situação: ATENDE → NÃO ATENDE", StringComparison.Ordinal);
                int iniciadosDepois;
                lock (programasIniciados) iniciadosDepois = programasIniciados.Count;
                var semIa = iniciadosDepois == iniciadosAntes && !j.AjustandoComIa;
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "rever-sem-ia-janela-xaml.png"));
                await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
                var desfez = ProjetoJson.Escrever(s.Revisao.Projeto).AsSpan().SequenceEqual(antes);
                await j.AplicarAsync(new DefinirJustificativa(To, "Considerar a projeção de 230,00 m² da prancha 02."));
                await j.SelecionarAsync(To);
                var rotuloComInstrucao = j.RotuloDoBotaoDaIa;
                var opcaoComIa = j.OpcaoReverComIaVisivel;
                await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
                p["rotulos"] = rotulo + " / " + rotuloComInstrucao + (opcaoComIa ? " + Rever com a IA" : "");
                p["conferencias"] = new JsonObject
                {
                    ["rotulo_sem_ia"] = rotulo == "Rever (sem IA)", ["reviu_com_antes_e_depois"] = reviu, ["nenhuma_ia_chamada"] = semIa, ["um_desfazer"] = desfez,
                    ["instrucao_oferece_ia"] = rotuloComInstrucao == "Rever (sem IA)" && opcaoComIa,
                };
                p["ok"] = rotulo == "Rever (sem IA)" && reviu && semIa && desfez && rotuloComInstrucao == "Rever (sem IA)" && opcaoComIa;
            }
            finally { File.Delete(json); }
        });

        await Passo("plugins_e_ferramentas", async p =>
        {
            // Tarefa 21: o cartão "Plugins e ferramentas" (Ajustes › Pastas e kit). As pastas lidas são fictícias (Codex, resumo do motor PDF,
            // fonte e cópia do kit, estado.json do instalador) e o lançador é falso: nenhum .cmd é aberto de verdade. A fonte tem o analise 4.1.0 e o
            // Codex o 4.0.0 (atualização disponível); o "Atualizar" da linha grava o .cmd só do analise; "Atualizar todos" grava o do instalador do kit.
            var raiz = Path.Combine(pasta, "kit-componentes");
            string Em(string nome) { var d = Path.Combine(raiz, nome); Directory.CreateDirectory(d); return d; }
            var (codexHome, motor, pmvni, kitLocal, fonte, scripts) = (Em("codex"), Em("MotorPdf"), Em("PMVNI"), Em("KIT"), Em("fonte"), Em("bootstrap"));
            foreach (var kit in new[] { fonte, kitLocal })
            {
                var d = Directory.CreateDirectory(Path.Combine(kit, "01 - PLUGINS", "plugins", "analise", ".codex-plugin")).FullName;
                await File.WriteAllTextAsync(Path.Combine(d, "plugin.json"), "{\"name\":\"analise\",\"version\":\"4.1.0\"}");
            }
            await File.WriteAllTextAsync(Path.Combine(kitLocal, ComponentesDoKit.InstaladorDoKit), "rem fictício");
            Directory.CreateDirectory(Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0"));
            await File.WriteAllTextAsync(Path.Combine(codexHome, "pmvni-kit.json"), new JsonObject { ["kit_root"] = kitLocal, ["fonte"] = fonte }.ToJsonString());
            await File.WriteAllTextAsync(Path.Combine(motor, "resumo-instalacao.txt"),
                "Blueprint Pipeline    INSTALADO\r\nFYP-Floorplan    INSTALADO\r\nDocling MCP 3.5.0    INSTALADO\r\nPyMuPDF 2.1    INSTALADO\r\nCodex / analise    INSTALADO 4.0.0\r\n");
            await File.WriteAllTextAsync(Path.Combine(motor, "mcp_pmvni.py"), "# servidor fictício");
            await File.WriteAllTextAsync(Path.Combine(pmvni, "estado.json"), "{\"modo\":\"atualizar\",\"atualizado\":\"2026-11-21T10:15:00\",\"situacao\":\"CONCLUIDO\"}");
            await File.WriteAllTextAsync(Path.Combine(scripts, ComponentesDoKit.Script), "# fictício");
            var bootstrap = await InstalacaoMotorPdf.ExtrairAsync(motor);
            var fonteServidor = Path.Combine(bootstrap, "mcp_pmvni.py");
            await File.WriteAllTextAsync(fonteServidor, "# cópia anterior do mesmo servidor");
            var fonteDivergia = ComponentesDoKit.HashDe(fonteServidor) != InstalacaoMotorPdf.HashDoServidorPdf();
            await InstalacaoMotorPdf.ExtrairAsync(motor);
            var fonteRestaurada = fonteDivergia && ComponentesDoKit.HashDe(fonteServidor) == InstalacaoMotorPdf.HashDoServidorPdf();
            p["bootstrap_recuperado"] = fonteRestaurada;
            var lancados = new List<(string Programa, string Cmd)>();
            j.OrigensDoKitDoRoteiro = new OrigensDoKit(codexHome, null, motor, pmvni, kitLocal, ComponentesDoKit.HashDe(Path.Combine(motor, "mcp_pmvni.py")), () => true);
            j.PastaScriptsDoKitDoRoteiro = scripts;
            j.LancadorDoKit = (programa, cmd) => lancados.Add((programa, cmd));
            try
            {
                await j.MudarModuloAsync(Modulo.Ajustes);
                var pag = j.AjustesPagina;
                pag.MostrarCategoria(CategoriasAjustes.Pastas);
                await j.VerificarComponentesAsync();
                await Esperar(300);
                var l = j.LeituraDoKitAtual ?? throw new InvalidOperationException("o cartão não leu os componentes");
                LinhaComponente Linha(string id) => l.Linhas.Single(x => x.Componente.Id == id);
                var painelPastas = pag.CartoesDasPastas;
                painelPastas.UpdateLayout();
                var processos = (FrameworkElement)painelPastas.Children[0];
                var kit = (FrameworkElement)painelPastas.Children[1];
                var origem = new Windows.Foundation.Point(0, 0);
                var topo = processos.TransformToVisual(painelPastas).TransformPoint(origem);
                var seguinte = kit.TransformToVisual(painelPastas).TransformPoint(origem);
                var linhaInteira = painelPastas.ActualWidth > 0 && Math.Abs(topo.X - seguinte.X) < 1
                    && Math.Abs(processos.ActualWidth - painelPastas.ActualWidth) < 1
                    && Math.Abs(kit.ActualWidth - painelPastas.ActualWidth) < 1
                    && Math.Abs(seguinte.Y - topo.Y - processos.ActualHeight - painelPastas.Spacing) < 1;
                var cartao = linhaInteira && pag.BotaoRepararInstalacao.Content as string == "Reparar instalação"
                    && ReferenceEquals(pag.BotaoRepararInstalacao.Parent, pag.BotaoAtualizarTodosComponentes.Parent)
                    && pag.ListaComponentesKit.Children.Count == ComponentesDoKit.Lista.Count && l.FonteAcessivel
                    && Linha("analise-codex").Situacao == SituacaoComponente.AtualizacaoDisponivel && Linha("analise-codex").TextoVersoes == "Instalada: 4.0.0 · disponível: 4.1.0"
                    && Linha("analise-claude").Situacao == SituacaoComponente.Ausente && Linha("pmvni-pdf").Situacao == SituacaoComponente.EmDia
                    && Linha("pymupdf").Situacao == SituacaoComponente.EmDia && Linha("fyp").Situacao == SituacaoComponente.EmDia
                    && !l.Linhas.Any(x => x.Componente.Nome.Contains("espacho", StringComparison.Ordinal))
                    && pag.TextoTarefaKit.Text.StartsWith("Última execução do instalador do kit: 21/11/2026 às 10:15 (atualizar) · concluída sem pendências.", StringComparison.Ordinal);
                pag.ListaComponentesKit.StartBringIntoView();
                await Esperar(300);
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "plugins-e-ferramentas-xaml.png"));

                ((IInvokeProvider)new ButtonAutomationPeer(j.BotoesDosComponentes["analise-codex"]).GetPattern(PatternInterface.Invoke)).Invoke();
                await Esperar(500);
                var umSo = lancados.Count == 1 && Path.GetFileName(lancados[0].Programa).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
                    && lancados[0].Cmd == Path.Combine(scripts, ComponentesDoKit.CmdDe("analise"))
                    && File.ReadAllText(lancados[0].Cmd).Contains("\" -Componente analise", StringComparison.Ordinal)
                    && pag.TextoResultadoComponentes.Text.StartsWith("Atualização iniciada fora do Editor: a análise no Codex e no Claude Code", StringComparison.Ordinal);
                ((IInvokeProvider)new ButtonAutomationPeer(pag.BotaoAtualizarTodosComponentes).GetPattern(PatternInterface.Invoke)).Invoke();
                await Esperar(500);
                var todos = lancados.Count == 2 && lancados[1].Cmd == Path.Combine(scripts, ComponentesDoKit.CmdTodos)
                    && File.ReadAllText(lancados[1].Cmd).Contains("\" -AtualizarKit -KitLocal \"" + kitLocal + "\"", StringComparison.Ordinal);
                var todosOsBotoes = true;
                foreach (var linha in l.Linhas)
                {
                    var antes = lancados.Count;
                    ((IInvokeProvider)new ButtonAutomationPeer(j.BotoesDosComponentes[linha.Componente.Id]).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Esperar(300);
                    var cmdEsperado = linha.SoPeloTodos || linha.Componente.Parte is null
                        ? ComponentesDoKit.CmdTodos : ComponentesDoKit.CmdDe(linha.Componente.Parte);
                    todosOsBotoes &= lancados.Count == antes + 1 && Path.GetFileName(lancados[^1].Cmd) == cmdEsperado;
                }
                ((IInvokeProvider)new ButtonAutomationPeer(pag.BotaoRepararInstalacao).GetPattern(PatternInterface.Invoke)).Invoke();
                await Esperar(300);
                var reparou = Path.GetFileName(lancados[^1].Cmd) == ComponentesDoKit.CmdDe("reparar");
                pag.TextoResultadoComponentes.StartBringIntoView();
                await Esperar(300);
                pag.TextoResultadoComponentes.UpdateLayout();
                var resultado = pag.TextoResultadoComponentes;
                var rodapeInteiro = !resultado.IsTextTrimmed && resultado.ActualHeight > 0
                    && resultado.TransformToVisual(pag.TextoTarefaKit).TransformPoint(default).Y + resultado.ActualHeight <= 1;
                ((IInvokeProvider)new ButtonAutomationPeer(pag.BotaoVerificarComponentes).GetPattern(PatternInterface.Invoke)).Invoke();
                await Esperar(500);
                var verificou = resultado.Text.StartsWith("Verificado às ", StringComparison.Ordinal);
                p["botoes_das_sete_linhas"] = todosOsBotoes;
                p["reparar"] = reparou;
                p["verificar"] = verificou;
                p["rodape_inteiro"] = rodapeInteiro;
                p["resultado"] = pag.TextoResultadoComponentes.Text;
                p["linhas"] = new JsonArray(l.Linhas.Select(x => (JsonNode)(x.Componente.Nome + ": " + x.TextoSituacao + " (" + x.TextoVersoes + ")")).ToArray());
                p["conferencias"] = new JsonObject { ["cartao"] = cartao, ["atualizar_um_so"] = umSo, ["atualizar_todos"] = todos };
                // Desconectar apenas a fonte da cópia de teste e usar o mesmo comando da tela.
                static string[] LogsDeErro() => Directory.Exists(RegistroErros.Pasta)
                    ? Directory.GetFiles(RegistroErros.Pasta, "erro-*.txt").Order().ToArray() : [];
                var logsAntes = LogsDeErro();
                Directory.Move(fonte, fonte + "-desconectada");
                await j.VerificarComponentesAsync();
                var offline = j.LeituraDoKitAtual is { FonteAcessivel: false } indisponivel
                    && indisponivel.TextoFonte.Contains("fora do alcance") && logsAntes.SequenceEqual(LogsDeErro());
                p["fonte_desconectada_sem_log_de_erro"] = offline;
                p["ok"] = cartao && umSo && todos && todosOsBotoes && reparou && verificou && rodapeInteiro && fonteRestaurada && offline;
            }
            finally
            {
                j.OrigensDoKitDoRoteiro = null;
                j.PastaScriptsDoKitDoRoteiro = null;
                j.LancadorDoKit = null;
                try { Directory.Delete(raiz, true); } catch (IOException) { }
            }
        });

        await Passo("modelos_da_analise", async p =>
        {
            // Tarefa 19: Ajustes › Inteligência artificial › Modelos da análise. A tabela e o etapas.json são fictícios, no plugin analise do CODEX_HOME do
            // roteiro; o ajuste vai para a pasta de dados do roteiro (tests\saida), nunca para a de verdade. Confere a grade (as 4 etapas de IA × 3 colunas, modelo e
            // nível em cada célula; preparo e produção são de programa, como no analise 4.0.0: linha "programa", sem célula), uma troca de modelo pela caixa (o ajuste local guarda só ela) e "Restaurar padrão do kit" (o ajuste some).
            await PrepararAgentesAsync();
            var refs = Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "references");
            Directory.CreateDirectory(refs);
            string[] ids = ["prepare", "analyze", "technical_audit", "audit", "produce", "final_review"];
            string[] iaIds = ["analyze", "technical_audit", "autocorrecao"];
            string Celulas(Func<string, (string Modelo, string Nivel)> c) =>
                string.Join(",", iaIds.Select(id => $"\"{id}\":{{\"modelo\":\"{c(id).Modelo}\",\"nivel\":\"{c(id).Nivel}\"}}"));
            var tecnica = (string id) => id is "analyze" or "technical_audit";
            await File.WriteAllTextAsync(Path.Combine(refs, TabelaDeModelos.NomeNoKit), "{\"versao\":1,\"niveis\":[\"baixo\",\"medio\",\"alto\"],"
                + "\"equivalencia\":{\"baixo\":\"low\",\"medio\":\"medium\",\"alto\":\"high\"},\"etapas_tecnicas\":[\"analyze\",\"technical_audit\"],"
                + "\"regras\":{\"sobe_por_objecao_tecnica\":true,\"desce_com_cota_abaixo_de\":0.15,\"excecoes_por_rota\":{\"gemini\":{\"desce_com_cota_abaixo_de\":null,\"nota\":\"Rota Gemini (fictícia): nada muda por cota.\"}}},"
                + "\"rotas\":{\"codex\":{\"nome\":\"ChatGPT\",\"perfis\":{\"padrao\":{\"etapas\":{" + Celulas(id => tecnica(id) ? ("gpt-6.1-sol", "alto") : ("gpt-6.1-terra", "medio")) + "}}}},"
                + "\"claude\":{\"nome\":\"Claude\",\"perfis\":{\"padrao\":{\"etapas\":{" + Celulas(id => tecnica(id) ? ("claude-opus-5-5", "alto") : ("claude-sonnet-5-5", "medio")) + "}}}},"
                + "\"gemini\":{\"nome\":\"Gemini\",\"modelos\":{\"gemini-3.8-flash\":{\"niveis\":[\"baixo\",\"medio\",\"alto\"]}},"
                + "\"perfis\":{\"conta_ultra\":{\"nome\":\"Conta Antigravity\",\"etapas\":{" + Celulas(_ => ("gemini-3.8-flash", "medio")) + "}}}}}}");
            await File.WriteAllTextAsync(Path.Combine(refs, "etapas.json"), "{\"versao\":1,\"ordem\":[" + string.Join(",", ids.Select(i => "\"" + i + "\"")) + "],\"etapas\":{"
                + string.Join(",", ids.Select(i => "\"" + i + "\":" + (tecnica(i) ? "{\"executor\":\"ia\"}" : "{\"executor\":\"programa\"}"))) + "}}");
            var local = ModelosDaAnalise.ArquivoLocal(PastaDados.Raiz);
            if (File.Exists(local)) File.Delete(local);
            // Um computador que não é o do kit mestre (sem instalacao.json): o botão "Publicar como padrão do kit" fica escondido.
            j.OrigensDoKitDoRoteiro = new OrigensDoKit(codexHome, null, Path.Combine(pasta, "motor-modelos"), Path.Combine(pasta, "pmvni-modelos"), j.PastaKitEfetiva, null);
            try
            {
                await j.AtualizarPecasNovoRelatorioAsync();
                await j.MudarModuloAsync(Modulo.Ajustes);
                var pag = j.AjustesPagina;
                pag.MostrarCategoria(CategoriasAjustes.Modelos);
                await Esperar(800);
                var grade = pag.PainelModelosAnalise.Children.OfType<Grid>().FirstOrDefault();
                var caixas = grade?.Children.OfType<Grid>().SelectMany(s => s.Children.OfType<ComboBox>()).ToList() ?? [];
                var gradeOk = grade is not null && caixas.Count == iaIds.Length * 2;
                pag.PainelModelosAnalise.StartBringIntoView();
                await Esperar(300);
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "modelos-da-analise-xaml.png"));

                // Troca o modelo da Análise no ChatGPT pela caixa: o ajuste local guarda só essa célula.
                var caixa = caixas.FirstOrDefault(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == "Modelo de Análise em ChatGPT");
                var outro = caixa?.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == "gpt-6.1-terra");
                if (caixa is not null && outro is not null) caixa.SelectedItem = outro;
                await Esperar(800);
                var ajuste = File.Exists(local) ? ModelosDaAnalise.LerAjuste(await File.ReadAllTextAsync(local)) : new AjusteModelos();
                var trocou = ajuste.Celulas.Count == 1 && ajuste.Celulas.GetValueOrDefault(("codex", "padrao", "analyze"))?.Modelo == "gpt-6.1-terra";
                // O nível da Auditoria técnica abaixo de médio: aviso, sem bloquear.
                var avisoAntes = pag.AvisoModelosAnalise.IsOpen;

                perguntas.RespostaPorTitulo["Restaurar o padrão do kit?"] = true;
                ((IInvokeProvider)new ButtonAutomationPeer(pag.BotaoRestaurarModelos).GetPattern(PatternInterface.Invoke)).Invoke();
                await Esperar(800);
                var restaurou = !File.Exists(local) && pag.TextoModelosAnalise.Text.StartsWith("Padrão do kit restaurado", StringComparison.Ordinal);
                var rotas = new JsonObject();
                var rotaOk = true;
                for (var indice = 0; indice < pag.CartoesRotas.Children.Count; indice++)
                {
                    var cartao = (Microsoft.UI.Xaml.Controls.Primitives.ToggleButton)pag.CartoesRotas.Children[indice];
                    if (cartao.IsChecked != true)
                    {
                        ((IToggleProvider)new ToggleButtonAutomationPeer(cartao).GetPattern(PatternInterface.Toggle)).Toggle();
                        await Esperar(300);
                    }
                    var novaGrade = pag.PainelModelosAnalise.Children.OfType<Grid>().Single();
                    var seletores = novaGrade.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<ComboBox>()).ToList();
                    var modelo = seletores.Single(c => AutomationProperties.GetName(c).StartsWith("Modelo de Autocorreção em "));
                    var nivel = seletores.Single(c => AutomationProperties.GetName(c).StartsWith("Nível de Autocorreção em "));
                    var titulo = pag.TituloRota.Text;
                    var padraoEsperado = indice switch { 0 => "gpt-6.1-terra", 1 => "claude-sonnet-5-5", _ => "gemini-3.8-flash" };
                    var padraoOk = (modelo.SelectedItem as ComboBoxItem)?.Tag as string == padraoEsperado
                        && (nivel.SelectedItem as ComboBoxItem)?.Tag as string == "medio";
                    var nota = novaGrade.Children.OfType<TextBlock>().Single(b => b.Text.Contains("consertos automáticos"));
                    nivel.SelectedItem = nivel.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "alto");
                    await Esperar(300);
                    var salvou = File.Exists(local) && ModelosDaAnalise.LerAjuste(await File.ReadAllTextAsync(local)).Celulas
                        .Any(kv => kv.Key.Etapa == "autocorrecao" && kv.Value == new CelulaModelos(padraoEsperado, "alto"));
                    ((IInvokeProvider)new ButtonAutomationPeer(pag.BotaoRestaurarModelos).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Esperar(300);
                    rotas[titulo] = padraoOk && salvou && !File.Exists(local) && !nota.IsTextTrimmed;
                    rotaOk &= rotas[titulo]!.GetValue<bool>();
                }
                p["autocorrecao_por_rota"] = rotas;
                rotaOk &= rotas.Count == 3;
                p["texto"] = pag.TextoModelosAnalise.Text;
                p["conferencias"] = new JsonObject
                {
                    ["grade"] = gradeOk, ["caixas"] = caixas.Count, ["troca_pela_caixa"] = trocou, ["sem_aviso_no_padrao"] = !avisoAntes, ["restaurar"] = restaurou,
                    ["publicar_oculto_fora_do_kit_mestre"] = pag.BotaoPublicarModelos.Visibility == Visibility.Collapsed,
                };
                p["ok"] = rotaOk && gradeOk && trocou && !avisoAntes && restaurou && pag.BotaoPublicarModelos.Visibility == Visibility.Collapsed;
                if (somenteAjustes)
                {
                    // A seleção da janela deve chegar ao registro que o executor recebe.
                    var botaoCodex = (Microsoft.UI.Xaml.Controls.Primitives.ToggleButton)pag.CartoesRotas.Children[0];
                    ((IToggleProvider)new ToggleButtonAutomationPeer(botaoCodex).GetPattern(PatternInterface.Toggle)).Toggle();
                    await Esperar(300);
                    var gradeCodex = pag.PainelModelosAnalise.Children.OfType<Grid>().Single();
                    var raciocinio = gradeCodex.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<ComboBox>())
                        .Single(c => AutomationProperties.GetName(c).StartsWith("Nível de Autocorreção em "));
                    raciocinio.SelectedItem = raciocinio.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "alto");
                    await Esperar(400);
                    await PrepararNovoRelatorioAsync();

                    var ensaio = Directory.CreateDirectory(Path.Combine(pasta, "prevoo-" + Guid.NewGuid().ToString("N"))).FullName;
                    var motor = Directory.CreateDirectory(Path.Combine(ensaio, "motor")).FullName;
                    var kitLocal = Directory.CreateDirectory(Path.Combine(ensaio, "kit")).FullName;
                    var claude = Directory.CreateDirectory(Path.Combine(ensaio, "claude", "plugins"));
                    File.WriteAllText(Path.Combine(claude.FullName, "installed_plugins.json"),
                        """{"plugins":{"analise@pmvni-local":[{"version":"4.0.0"}]}}""");
                    var servidor = Path.Combine(motor, "mcp_pmvni.py");
                    File.WriteAllText(servidor, "VERSAO = '1.1.0'");
                    var hashServidor = ComponentesDoKit.HashDe(servidor);
                    File.WriteAllText(servidor, "VERSAO = '1.0.0'");
                    File.WriteAllText(Path.Combine(kitLocal, ComponentesDoKit.InstaladorDoKit), "rem teste");
                    File.WriteAllText(Path.Combine(motor, ComponentesDoKit.Script), "# teste");
                    var instalou = false;
                    j.OrigensDoKitDoRoteiro = new OrigensDoKit(codexHome, claude.Parent!.FullName, motor, ensaio, kitLocal,
                        hashServidor, () => true, "1.1.0", (_, _) => Task.FromResult<JsonObject?>(instalou
                            ? new() { ["status"] = "CURRENT" }
                            : new() { ["status"] = "CHECK_FAILED", ["different_skill_files"] = new JsonArray("scripts/validar.py") }));
                    j.PastaScriptsDoKitDoRoteiro = motor;
                    j.LancadorDoKit = (_, cmd) =>
                    {
                        if (Path.GetFileName(cmd) != ComponentesDoKit.CmdTodos) throw new InvalidOperationException(cmd);
                        instalou = true;
                        File.WriteAllText(servidor, "VERSAO = '1.1.0'");
                        File.WriteAllText(Path.Combine(motor, "resumo-instalacao.txt"),
                            "Blueprint Pipeline    INSTALADO\r\nFYP-Floorplan    INSTALADO\r\nDocling MCP 3.5.0    INSTALADO\r\nPyMuPDF 2.1    INSTALADO\r\n");
                    };
                    await j.VerificarComponentesAsync();
                    await j.NovoRelatorioAsync();
                    await j.EscolherProcessoNovoRelatorioAsync(j.ProcessosDoNovoRelatorio.First(x => x.Pasta == P0001()));
                    perguntas.Confirmar = true;
                    var (execucao, fim) = await AnalisarAteOFimAsync(Provedor.ChatGpt, "ok");
                    var confirmacao = perguntas.Confirmacoes.Last(c => c.Titulo == "O kit precisa de atualização");
                    var prevoo = instalou && fim.Situacao == "concluida" && confirmacao.Sim == "Atualizar kit e continuar"
                        && confirmacao.Texto.Contains("scripts/validar.py") && ComponentesDoKit.Lista.All(c => confirmacao.Texto.Contains(c.Nome));
                    var arq = Path.Combine(execucao.Registro.Pasta, TabelaDeModelos.NomeDoArquivo);
                    var politica = JsonNode.Parse(File.ReadAllText(arq))!["rotas"]!["codex"]!["perfis"]!["padrao"]!["etapas"]!["autocorrecao"]!;
                    p["conferencia_antes_de_partir"] = prevoo;
                    p["tabela_da_execucao"] = arq;
                    p["ok"] = p["ok"]!.GetValue<bool>() && prevoo && (string?)politica["nivel"] == "alto";
                    // Reabrir um registro interrompido com uma objeção. Nenhuma IA deve partir.
                    var casoPendente = P0012();
                    var regPendente = RegistroExecucao.Criar(casoPendente, Provedor.ChatGpt, DateTime.Now);
                    var controle = Directory.CreateDirectory(Path.Combine(casoPendente, ".pmvni-ai", "analise")).FullName;
                    var estado = Path.Combine(controle, "engine_state.json");
                    var objecao = """{"stages":{"prepare":{"status":"EXECUTED"},"technical_audit":{"status":"OBJECTIONS","note":"Objeto fictício para conferir"}}}""";
                    File.WriteAllText(estado, objecao);
                    j.AjustesAtuais.ExecucoesChatGpt.Add(new ExecucaoClaudePendente(casoPendente, regPendente.Pasta));
                    await j.NovoRelatorioAsync();
                    var confirmacoesAntes = perguntas.Confirmacoes.Count;
                    await j.RetomarExecucoesChatGptAsync();
                    var aviso = j.NovoRelatorio.AvisoResultado;
                    var avisou = aviso.IsOpen && aviso.Severity == InfoBarSeverity.Warning && aviso.Message.Contains("Objeto fictício para conferir")
                        && aviso.Message.Contains("Auditoria técnica") && aviso.Message.Contains("Retomar análise")
                        && j.ExecucoesEmCurso.Count == 0 && perguntas.Confirmacoes.Count == confirmacoesAntes && File.ReadAllText(estado) == objecao;
                    p["retomada_com_objecao_sem_reexecutar"] = avisou;
                    p["ok"] = p["ok"]!.GetValue<bool>() && avisou;
                    j.AjustesAtuais.ExecucoesChatGpt.RemoveAll(x => x.Registro == regPendente.Pasta);
                    j.LancadorDoKit = null;
                    j.PastaScriptsDoKitDoRoteiro = null;
                    await j.MudarModuloAsync(Modulo.Ajustes);
                    pag.MostrarCategoria(CategoriasAjustes.Modelos);
                    ((IInvokeProvider)new ButtonAutomationPeer(pag.BotaoRestaurarModelos).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Esperar(300);
                }
            }
            finally { j.OrigensDoKitDoRoteiro = null; }
        });

        await j.FecharSessaoAsync();
        string[] iniciados;
        lock (programasIniciados) iniciados = programasIniciados.ToArray();
        var foraDosPermitidos = iniciados.Count(c => !TravaDeProgramas.Permitido(c, programasPermitidos));
        saida["programas_iniciados"] = new JsonObject
        {
            ["programas"] = new JsonArray(iniciados
                .Select(c => c.StartsWith(pastaDoAgenteFalso + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(pastaDoAgenteFalso, c) : c)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).Select(c => (JsonNode)c).ToArray()),
            ["permitidos"] = new JsonArray(programasPermitidos.Select(c => (JsonNode)c).ToArray()),
            ["inicios"] = iniciados.Length,
            ["libreoffice"] = iniciados.Count(c => string.Equals(c, sofficeDoRoteiro, StringComparison.OrdinalIgnoreCase)),
            ["fora_do_agente_falso"] = foraDosPermitidos,
        };
        if (foraDosPermitidos > 0 || TravaDeProgramas.Permitidos != programasPermitidos) falhas.Add("programas_iniciados");
        saida["perguntas_feitas"] = new JsonArray(perguntas.Feitas.Select(s => (JsonNode)s).ToArray());
        saida["falhas"] = falhas;
        saida["ok"] = falhas.Count == 0;
        Gravar(args.Roteiro!, saida);
        Application.Current.Exit();
    }

    /// <summary>A versão do plugin analise no kit (<c>01 - PLUGINS\plugins\analise\.codex-plugin\plugin.json</c>), só lida; null se não houver (Tarefa 18: um só plugin).</summary>
    static string? LerVersaoDoKit(string kit)
    {
        var json = Path.Combine(kit, "01 - PLUGINS", "plugins", "analise", ".codex-plugin", "plugin.json");
        try { return File.Exists(json) ? JsonNode.Parse(File.ReadAllText(json))?["version"]?.GetValue<string>() : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>A chave como poderia aparecer num arquivo: UTF-8, UTF-16 e base64 (nas três posições possíveis dentro de um texto em base64).</summary>
    static IReadOnlyList<byte[]> TrechosDaChave(string chave)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(chave);
        var lista = new List<byte[]> { utf8, System.Text.Encoding.Unicode.GetBytes(chave) };
        for (var deslocamento = 0; deslocamento < 3; deslocamento++)
        {
            var b64 = Convert.ToBase64String(new byte[deslocamento].Concat(utf8).ToArray());
            var pular = deslocamento switch { 0 => 0, 1 => 2, _ => 3 };
            lista.Add(System.Text.Encoding.ASCII.GetBytes(b64.Substring(pular, b64.Length - pular - 2)));
        }
        return lista;
    }

    /// <summary>true se o arquivo contém algum dos trechos (lido em blocos, sem trancar o arquivo para quem o usa).</summary>
    static bool ArquivoContem(string arquivo, IReadOnlyList<byte[]> trechos)
    {
        var maior = trechos.Max(t => t.Length);
        const int Bloco = 1 << 20;
        var buffer = new byte[Bloco + maior];
        var resto = 0;
        using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        while (true)
        {
            var n = f.Read(buffer, resto, Bloco);
            if (n <= 0) return false;
            var total = resto + n;
            var janela = buffer.AsSpan(0, total);
            foreach (var t in trechos)
                if (janela.IndexOf(t) >= 0) return true;
            resto = Math.Min(maior - 1, total);
            buffer.AsSpan(total - resto, resto).CopyTo(buffer);
        }
    }

    /// <summary>Procura os trechos da chave em todos os arquivos das pastas. Um arquivo trancado por outro programa (o perfil do WebView2) é contado à parte.</summary>
    static (int Lidos, int Ilegiveis, List<string> Achados) VarrerChave(IEnumerable<string> pastas, IReadOnlyList<byte[]> trechos, Func<string, bool>? ignorar = null)
    {
        int lidos = 0, ilegiveis = 0;
        var achados = new List<string>();
        foreach (var raiz in pastas.Where(Directory.Exists))
            foreach (var arquivo in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories))
            {
                if (ignorar?.Invoke(arquivo) == true) continue;
                try
                {
                    if (ArquivoContem(arquivo, trechos)) achados.Add(Path.GetRelativePath(raiz, arquivo));
                    lidos++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ilegiveis++; }
            }
        return (lidos, ilegiveis, achados);
    }

    /// <summary>Os atributos sem os bits de placeholder do OneDrive (fixado, não fixado, recuperar ao abrir/ao acessar): o OneDrive os muda sozinho nos
    /// arquivos do roteiro (tests\saida fica dentro do OneDrive), sem tocar no conteúdo, nas datas nem nos atributos que o editor poderia mudar.</summary>
    static FileAttributes SemNuvem(FileAttributes a) => a & ~(FileAttributes)(0x00040000 | 0x00080000 | 0x00100000 | 0x00400000);

    /// <summary>Arquivos do repositório gravados desde <paramref name="desde"/> fora das pastas permitidas (tests\saida\roteiro, dados-app e
    /// agente-falso). Ficam de fora só, na raiz do repositório, o .git, o .superpowers e o .claude (relatórios, progresso e worktrees que os agentes
    /// de desenvolvimento escrevem enquanto o roteiro roda; o app nunca escreve ali), o web\paginador\node_modules e o desktop.ini do OneDrive. Nenhuma exclusão por
    /// nome em outro nível (um bin ou obj gravado durante o roteiro aparece: o tools\roteiro.ps1 publica o agente falso antes de abrir o app).</summary>
    static List<string> EscritasFora(string raiz, DateTime desde, IReadOnlyList<string> permitidas)
    {
        var achados = new List<string>();
        var pendentes = new Stack<string>([raiz]);
        // .claude na raiz: as worktrees e a memória dos agentes de desenvolvimento (outro agente pode compilar nela enquanto o roteiro roda).
        var ignoradas = new[] { Path.Combine(raiz, ".git"), Path.Combine(raiz, ".superpowers"), Path.Combine(raiz, ".claude"), Path.Combine(raiz, "web", "paginador", "node_modules") };
        while (pendentes.Count > 0)
        {
            var dir = pendentes.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (ignoradas.Any(i => string.Equals(Path.GetFullPath(sub).TrimEnd('\\'), Path.GetFullPath(i).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) continue;
                    if (permitidas.Any(pp => string.Equals(Path.GetFullPath(sub).TrimEnd('\\'), Path.GetFullPath(pp).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) continue;
                    pendentes.Push(sub);
                }
                foreach (var f in Directory.EnumerateFiles(dir))
                    if (!Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTimeUtc(f) >= desde)
                        achados.Add(Path.GetRelativePath(raiz, f));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return achados;
    }

    /// <summary>Um PDF fictício mínimo (uma página em branco com um comentário): só serve de anexo, o editor não o abre.</summary>
    static byte[] PdfFicticio(string comentario) => System.Text.Encoding.ASCII.GetBytes(
        "%PDF-1.4\n% " + new string(comentario.Select(c => c < 128 ? c : '?').ToArray()) + "\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
        + "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    /// <summary>O processo ainda roda?</summary>
    static bool Vivo(int pid)
    {
        if (pid <= 0) return false;
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }

    /// <summary>Copia a árvore (pastas e arquivos, ocultos inclusive) para um destino novo.</summary>
    static void CopiarArvore(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var d in Directory.GetDirectories(origem, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destino, Path.GetRelativePath(origem, d)));
        foreach (var f in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(destino, Path.GetRelativePath(origem, f)));
    }

    /// <summary>Apaga uma árvore que o próprio roteiro criou antes (arquivos ocultos ou só-leitura inclusive).</summary>
    static void ApagarArvore(string pasta)
    {
        // A pasta fica no OneDrive: o sincronizador (ou o Explorer) pode segurar um arquivo ou uma subpasta por instantes e o Delete lança
        // "O diretório não está vazio". Tenta de novo por até ~10 s antes de desistir.
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                if (!Directory.Exists(pasta)) return;
                foreach (var f in Directory.GetFiles(pasta, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                // As subpastas que o OneDrive marca (reparse point "sempre neste dispositivo") não são percorridas pelo Delete recursivo, que
                // então recusa a pasta de cima ("acesso negado"/"não está vazia"): apaga uma a uma, da mais funda para cima.
                // O OneDrive também marca essas pastas como somente leitura (e "fixadas"): o atributo sai antes de apagar cada uma.
                foreach (var d in Directory.GetDirectories(pasta, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    File.SetAttributes(d, FileAttributes.Directory);
                    Directory.Delete(d, false);
                }
                File.SetAttributes(pasta, FileAttributes.Directory);
                Directory.Delete(pasta, false);
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && tentativa < 40) { Thread.Sleep(250); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                var restantes = Directory.Exists(pasta) ? string.Join(", ", Directory.GetFileSystemEntries(pasta, "*", SearchOption.AllDirectories).Take(5)) : "";
                throw new IOException($"não consegui apagar {pasta} ({e.Message}); ainda há: {restantes}", e);
            }
        }
    }

    /// <summary>Os modelos de despacho do kit: <c>02 - MODELOS DE DOCUMENTOS</c>, as referências do antigo plugin despacho e o
    /// <c>references\despacho</c> do analise (caminho, tamanho e data de cada arquivo e pasta): o roteiro prova que o kit só foi lido.</summary>
    static string[] RetratoDoKit(string kit) =>
        new[]
            {
                Path.Combine(kit, "02 - MODELOS DE DOCUMENTOS"), Path.Combine(kit, TextosFixos.PastaReferencias.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(kit, FontesModelos.AnaliseNoKit.Replace('/', Path.DirectorySeparatorChar), FontesModelos.PastaNoAnalise.Replace('/', Path.DirectorySeparatorChar)),
            }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFileSystemEntries(d, "*", SearchOption.AllDirectories).Append(d))
            .Select(f =>
            {
                FileSystemInfo info = File.Exists(f) ? new FileInfo(f) : new DirectoryInfo(f);
                var tamanho = info is FileInfo fi ? fi.Length.ToString(CultureInfo.InvariantCulture) : "pasta";
                return Path.GetRelativePath(kit, f) + " | " + tamanho + " | " + info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);
            })
            .Order(StringComparer.Ordinal).ToArray();

    /// <summary>Caminho relativo → sha256 de cada arquivo da árvore (para provar que o app só leu).</summary>
    static Dictionary<string, string> FotografarArvore(string raiz) =>
        Directory.GetFiles(raiz, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(raiz, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), StringComparer.OrdinalIgnoreCase);

    /// <summary>As linhas do diário (aberto pela sessão para escrita; lido sem atrapalhar).</summary>
    static string[] LinhasDoDiario(string arquivo)
    {
        if (!File.Exists(arquivo)) return [];
        using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(f);
        return r.ReadToEnd().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
    }

    /// <summary>Uma trava "de outro" no formato da <see cref="Trava"/> (datas UTC com 7 casas).</summary>
    static void EscreverTrava(string caminho, string maquina, string usuario, int pid, DateTimeOffset inicio, DateTimeOffset batimento)
    {
        static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        if (File.Exists(caminho)) { File.SetAttributes(caminho, FileAttributes.Normal); File.Delete(caminho); }
        File.WriteAllText(caminho, new JsonObject { ["maquina"] = maquina, ["usuario"] = usuario, ["pid"] = pid, ["inicio_utc"] = Utc(inicio), ["batimento_utc"] = Utc(batimento) }.ToJsonString());
    }

    static int? LerPidDaTrava(string caminho)
    {
        try
        {
            using var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(f)?["pid"]?.GetValue<int>();
        }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }

    /// <summary>Centro (um pouco à esquerda, no texto) do campo <paramref name="campo"/> do item, depois de rolar até ele.</summary>
    static async Task<(double X, double Y)> PontoDoCampoAsync(MainWindow j, string item, string campo)
    {
        var achar = $"[...document.querySelectorAll('section.folha .card[data-ref=\"{item}\"] [data-campo=\"{campo}\"]')][0]";
        var ok = await Js(j, $"(() => {{ const e = {achar}; if (!e) return false; e.scrollIntoView({{ block: 'center', behavior: 'instant' }}); return true; }})()");
        if (ok.ValueKind != JsonValueKind.True) throw new InvalidOperationException($"o campo {campo} do item {item} não está nas folhas");
        await Esperar(250);
        var r = await Js(j, $"(() => {{ const r = {achar}.getBoundingClientRect(); return {{ x: r.left + Math.min(r.width / 2, 60), y: r.top + Math.min(r.height / 2, 9) }}; }})()");
        return (r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble());
    }

    static async Task CliqueAsync(MainWindow j, (double X, double Y) ponto, int cliques = 1, string botao = "left")
    {
        var mascara = botao == "right" ? 2 : 1;
        await Cdp(j, "Input.dispatchMouseEvent", new { type = "mouseMoved", x = ponto.X, y = ponto.Y, button = "none", buttons = 0 });
        for (var n = 1; n <= cliques; n++)
        {
            await Cdp(j, "Input.dispatchMouseEvent", new { type = "mousePressed", x = ponto.X, y = ponto.Y, button = botao, buttons = mascara, clickCount = n });
            await Cdp(j, "Input.dispatchMouseEvent", new { type = "mouseReleased", x = ponto.X, y = ponto.Y, button = botao, buttons = 0, clickCount = n });
        }
        await Esperar(150);
    }

    /// <summary>Tecla pelo CDP (modificadores: 1 Alt, 2 Ctrl, 8 Shift).</summary>
    static async Task TeclaAsync(MainWindow j, string key, string code, int vk, int modificadores = 0)
    {
        await Cdp(j, "Input.dispatchKeyEvent", new { type = "rawKeyDown", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
        await Cdp(j, "Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
    }

    /// <summary>Foco nas folhas e o teclado chegando à página: manda Shift (fora da lista de atalhos, sem efeito) até ela registrar um keydown.</summary>
    static async Task GarantirTecladoAsync(MainWindow j)
    {
        j.FocarFolhas();
        await Js(j, "window.__pmvniTeclas = 0; if (!window.__pmvniEscuta) { window.__pmvniEscuta = true; addEventListener('keydown', () => { window.__pmvniTeclas++; }, true); } 1");
        for (var k = 0; k < 40; k++)
        {
            await TeclaAsync(j, "Shift", "ShiftLeft", 16, 8);
            await Esperar(50);
            if ((await Js(j, "window.__pmvniTeclas")).GetInt32() > 0) return;
        }
        throw new InvalidOperationException("O teclado não chegou à página (40 tentativas com Shift).");
    }

    static Task Cdp(MainWindow j, string metodo, object parametros) =>
        j.NucleoWeb.CallDevToolsProtocolMethodAsync(metodo, JsonSerializer.Serialize(parametros)).AsTask();

    static async Task<bool> EsperarAteAsync(Func<Task<bool>> condicao, int ms)
    {
        var fim = DateTime.UtcNow.AddMilliseconds(ms);
        while (true)
        {
            if (await condicao()) return true;
            if (DateTime.UtcNow > fim) return false;
            await Esperar(50);
        }
    }

    static async Task<string?> EsperarValorAsync(MainWindow j, string expressao, int ms)
    {
        string? valor = null;
        await EsperarAteAsync(async () => { var v = await Js(j, expressao); valor = v.ValueKind == JsonValueKind.String ? v.GetString() : null; return valor is not null; }, ms);
        return valor;
    }

    static async Task<JsonElement> Js(MainWindow j, string expressao)
    {
        var json = await j.NucleoWeb.ExecuteScriptAsync(expressao);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    static Task Esperar(int ms) => Task.Delay(ms);

    static async Task CapturarWebAsync(MainWindow j, string arquivo)
    {
        using var fluxo = new InMemoryRandomAccessStream();
        await j.NucleoWeb.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fluxo);
        fluxo.Seek(0);
        var bytes = new byte[fluxo.Size];
        await fluxo.ReadAsync(bytes.AsBuffer(), (uint)fluxo.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(arquivo, bytes);
    }

    internal static async Task CapturarXamlAsync(FrameworkElement elemento, string arquivo)
    {
        // O Mica não entra na captura: fundo sólido do tema só durante a captura, para o texto não sumir.
        var grade = elemento as Microsoft.UI.Xaml.Controls.Grid;
        var anterior = grade?.Background;
        if (grade is not null) grade.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(elemento.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
        var rtb = new RenderTargetBitmap();
        try { await rtb.RenderAsync(elemento); }
        finally { if (grade is not null) grade.Background = anterior; }
        var pixels = await rtb.GetPixelsAsync();
        using var fluxo = new InMemoryRandomAccessStream();
        var codificador = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fluxo);
        codificador.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels.ToArray());
        await codificador.FlushAsync();
        fluxo.Seek(0);
        var bytes = new byte[fluxo.Size];
        await fluxo.ReadAsync(bytes.AsBuffer(), (uint)fluxo.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(arquivo, bytes);
    }

    // Grava num temporário ao lado e renomeia: quem espera pelo arquivo nunca lê JSON pela metade.
    static void Gravar(string arquivo, JsonObject dados)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var temporario = arquivo + ".tmp";
        File.WriteAllText(temporario, dados.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.Move(temporario, arquivo, overwrite: true);
    }
}
