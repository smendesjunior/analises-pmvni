using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pmvni.Imoveis;

namespace Pmvni.App;

/// <summary>Boletim de Cadastro Imobiliário (BCI) oficial do GPI, gerado direto em PDF (2026-11-17, pedido do usuário: "sem abrir site nem nada").
/// O GPI roda num navegador invisível, com o mesmo perfil e sessão do módulo GPI, sempre "visível" para o WebView2 (opacidade 0, atrás das
/// páginas, sem receber cliques) para a página ter layout real. Fluxo conferido no GPI real em 17/11/2026: busca de funcionalidades →
/// Contribuintes Manutenção → Inscrição Municipal + Igual → única linha exata → Consultar → ficha em Consulta conferida → Boletim Cadastral, cujo
/// envio (POST para .../acessoclientbase/report) é refeito pelo script para receber o PDF em vez de abrir uma aba.
/// Resistência a atualizações do GPI: o app usa só o que aparece na tela (rótulos e botões), nunca as chamadas internas do site (GWT-RPC, que
/// mudam a cada versão); cada falha diz o passo em que parou (aviso e log), oferece "Abrir no GPI" para terminar por lá, e Ajustes ›
/// Fontes de dados tem "Testar o boletim" para conferir o fluxo inteiro antes de precisar.</summary>
public sealed partial class MainWindow
{
    WebView2? gpiOculto;
    Task<CoreWebView2>? criandoGpiOculto;

    Task<CoreWebView2> GpiOcultoAsync()
    {
        if (criandoGpiOculto is { IsFaulted: false, IsCanceled: false } t) return t;
        return criandoGpiOculto = CriarGpiOcultoAsync();
    }

    async Task<CoreWebView2> CriarGpiOcultoAsync()
    {
        if (gpiOculto is { } velho) { (velho.Parent as Panel)?.Children.Remove(velho); try { velho.Close(); } catch (Exception) { } }
        var web = new WebView2
        {
            Width = 1400, Height = 900, Opacity = 0, IsHitTestVisible = false, IsTabStop = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(web, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        gpiOculto = web;
        var pai = (Panel)PaginaDeImoveis.Parent;
        pai.Children.Insert(0, web);
        try
        {
            await web.EnsureCoreWebView2Async(await PaginaSites.Ambiente());
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.IsMuted = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;   // nada abre janela a partir do GPI invisível
            core.DownloadStarting += (_, e) => e.Cancel = true;
            await PaginaDeSites.PrepararPerfilAsync(core);
            await NavegarAsync(core, Modulos.Endereco(Modulo.Gpi));
            return core;
        }
        catch
        {
            pai.Children.Remove(web);
            gpiOculto = null;
            throw;
        }
    }

    static Task NavegarAsync(CoreWebView2 core, string url)
    {
        var fim = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Pronto(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs e) { core.NavigationCompleted -= Pronto; fim.TrySetResult(); }
        core.NavigationCompleted += Pronto;
        core.Navigate(url);
        return fim.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary>"Gerar boletim cadastral" de uma linha de Imóveis: tudo no GPI invisível; no fim, o PDF oficial é gravado e aberto.</summary>
    async Task GerarBciDoImovelAsync(RegistroCadastro reg)
    {
        if (ChaveBci.Estrita(reg.InscricaoMunicipal) is null)
        {
            MostrarAvisoBci(InfoBarSeverity.Warning, ChaveBci.Texto(new ResultadoBci(EstadoBci.InscricaoInvalida)), emCurso: false);
            return;
        }
        // Antes de ir ao GPI: a pasta do processo deste imóvel (o BCI sempre vai para DOCUMENTOS do processo; sem processo, não gera).
        MostrarAvisoBci(InfoBarSeverity.Informational, "Procurando o processo deste imóvel…", emCurso: true);
        var (seguir, documentos) = await PastaDoProcessoDoImovelAsync(reg);
        if (!seguir) return;
        var r = await ExecutarBciAsync(reg.InscricaoMunicipal, m => MostrarAvisoBci(InfoBarSeverity.Informational, m, emCurso: true));
        if (r.Estado == EstadoBci.Aberto && r.Pdf is { } pdf)
        {
            // Sem processo ("Só abrir, sem salvar"): o PDF vai para uma pasta temporária do app e só é aberto.
            var semVinculo = documentos is null;
            var pasta = documentos ?? Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PMVNI-Boletins")).FullName;
            var arquivo = GravarBoletim(pasta, reg.InscricaoMunicipal, reg.Contribuinte, pdf);
            MostrarAvisoBci(InfoBarSeverity.Success, ChaveBci.Texto(r) + (semVinculo ? " Aberto sem salvar em processo (cópia temporária)." : " Gravado em " + arquivo), emCurso: false, arquivo: arquivo);
            AbrirBoletim(arquivo, reg.InscricaoMunicipal);
            return;
        }
        var grave = r.Estado is EstadoBci.Cancelado or EstadoBci.LoginNecessario ? InfoBarSeverity.Informational : InfoBarSeverity.Warning;
        MostrarAvisoBci(grave, ChaveBci.Texto(r), emCurso: false, planoB: r.Estado is not (EstadoBci.Cancelado or EstadoBci.Ocupado or EstadoBci.InscricaoInvalida));
    }

    async Task<ResultadoBci> ExecutarBciAsync(string inscricao, Action<string> andamento)
    {
        cancelarBci?.Cancel();
        using var cts = new CancellationTokenSource();
        cancelarBci = cts;
        coordenadorBci ??= new CoordenadorBci(new AdaptadorGpi(this), e => RegistroErros.Gravar("sites.bci", e));
        andamento("Gerando o boletim cadastral de " + inscricao + " no GPI…");
        try
        {
            var r = await coordenadorBci.GerarAsync(inscricao, cts.Token);
            if (r.Estado != EstadoBci.Aberto)
                RegistroErros.Gravar("sites.bci.passo", new InvalidOperationException("BCI " + r.Estado + " no passo " + (r.Passo ?? "?") + ": " + (r.Detalhe ?? "")));
            return r;
        }
        finally { if (cancelarBci == cts) cancelarBci = null; }
    }

    void MostrarAvisoBci(InfoBarSeverity s, string mensagem, bool emCurso, bool planoB = false, string? arquivo = null, bool irParaProcessos = false)
    {
        MostrarAvisoImoveis(s, "Boletim cadastral", mensagem);
        Trabalhando.Definir(I.Aviso, emCurso);   // brilho arco-íris enquanto o GPI trabalha
        if (arquivo is not null)
        {
            // Sucesso: "Abrir PDF" e "Abrir pasta" (pedido do usuário), mesmo com o PDF já aberto.
            var abrirPdf = new Button { Content = Rotulo("", "Abrir PDF"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            abrirPdf.Click += (_, _) => AbrirBoletim(arquivo, Path.GetFileNameWithoutExtension(arquivo));
            var abrirPasta = new Button { Content = Rotulo("", "Abrir pasta"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            abrirPasta.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + arquivo + "\""); }
                catch (Exception e) { RegistroErros.Gravar("sites.bci.pasta", e); }
            };
            I.Aviso.ActionButton = null;
            I.Aviso.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8), Children = { abrirPdf, abrirPasta } };
            return;
        }
        Button? botao = null;
        if (irParaProcessos)
        {
            botao = new Button { Content = "Ir para Processos" };
            botao.Click += async (_, _) => await MudarModuloAsync(Modulo.Processos);
        }
        else if (emCurso)
        {
            botao = new Button { Content = "Cancelar" };
            botao.Click += (_, _) => cancelarBci?.Cancel();
        }
        else if (planoB)
        {
            botao = new Button { Content = "Abrir no GPI" };
            ToolTipService.SetToolTip(botao, "Abre o GPI para terminar por lá (Tributário › Cadastro de Contribuintes › Contribuintes Manutenção).");
            botao.Click += async (_, _) => await MudarModuloAsync(Modulo.Gpi);
        }
        I.Aviso.ActionButton = botao;
    }

    /// <summary>A pasta DOCUMENTOS do processo do imóvel (<see cref="VinculoBci"/>): pistas rápidas primeiro (inscrição da consulta QGIS da
    /// análise, requerente da pasta); sem inscrição exata, lê também os PDFs de DOCUMENTOS e PROJETO (escritura, matrícula, projeto). Empate:
    /// o usuário escolhe. Nenhuma pista: aviso para criar a pasta do processo, e (false, null): nada é gerado.
    /// 2026-11-17: devolve (seguir, pasta); "Só abrir, sem salvar em processo" = (true, null).</summary>
    async Task<(bool Seguir, string? Documentos)> PastaDoProcessoDoImovelAsync(RegistroCadastro reg)
    {
        void Aviso(string m) => MostrarAvisoBci(InfoBarSeverity.Warning, m, emCurso: false, irParaProcessos: true);
        if (string.IsNullOrWhiteSpace(ajustes.PastaProcessos) || !Directory.Exists(ajustes.PastaProcessos))
        {
            Aviso("Escolha a pasta dos processos em Ajustes antes de gerar o boletim: ele é salvo na pasta DOCUMENTOS do processo do imóvel.");
            return (false, null);
        }
        var processos = await PastasDeProcessoAsync();
        var insc = reg.InscricaoMunicipal;
        var socios = VinculoBci.Socios(reg.Campos, reg.Contribuinte);
        var pistas = await Task.Run(() => processos.Select(p => new PistasProcesso(p, DetectorProcesso.Inscricao(p.Pasta), null)).ToList());
        var notas = pistas.Select(p => VinculoBci.Pontuar(insc, reg.Contribuinte, socios, p)).ToList();
        var (escolhido, _) = VinculoBci.Escolher(notas);
        if (escolhido is not { Nota: >= VinculoBci.InscricaoExata })
        {
            MostrarAvisoBci(InfoBarSeverity.Informational, "Procurando o processo deste imóvel nos documentos (escritura, matrícula, projeto)…", emCurso: true);
            pistas = await Task.Run(() => pistas.Select(p => p with { TextoPdfs = TextoDosPdfs(p.Processo.Pasta) }).ToList());
            notas = pistas.Select(p => VinculoBci.Pontuar(insc, reg.Contribuinte, socios, p)).ToList();
        }

        // Sempre confirma onde salvar (2026-11-17, pedido do usuário): o processo achado (marcado), os outros prováveis, outra pasta ou um processo novo.
        var candidatos = notas.Where(n => n.Nota > 0).OrderByDescending(n => n.Nota).ThenBy(n => n.Processo.NomePasta, StringComparer.CurrentCultureIgnoreCase).Take(6).ToList();
        var itens = candidatos.Select(n => n.Processo.NomePasta + "  (" + n.Motivo + ")").ToList();
        var iOutro = itens.Count; itens.Add("Outro processo… (escolher a pasta)");
        var iNovo = itens.Count; itens.Add("Criar novo processo…");
        var iSoAbrir = itens.Count; itens.Add("Só abrir o boletim, sem salvar em processo");
        var quem = reg.Contribuinte + (socios.Count > 0 ? " · sócios: " + string.Join(", ", socios) : "");
        var texto = candidatos.Count > 0
            ? "Processo achado para " + ChaveBci.Estrita(insc) + " (" + quem + "). Confirme onde salvar o boletim, na pasta DOCUMENTOS."
            : "Nenhum processo achado para " + ChaveBci.Estrita(insc) + " (" + quem + "). Escolha uma pasta de processo ou crie um processo novo.";
        var escolha = await Perguntas.Atual.EscolherNaListaAsync("Onde salvar o boletim?", texto, itens, candidatos.Count > 0 ? 0 : iNovo, "Salvar aqui");
        string? pasta = null;
        if (escolha is { } k && k < candidatos.Count) pasta = candidatos[k].Processo.Pasta;
        else if (escolha == iOutro) pasta = await Perguntas.Atual.EscolherPastaAsync();
        else if (escolha == iNovo) pasta = (await CriarPastaProcessoAsync(reg.Contribuinte))?.Pasta;
        else if (escolha == iSoAbrir) return (true, null);
        if (pasta is null)
        {
            MostrarAvisoBci(InfoBarSeverity.Informational, "Boletim não gerado: nenhuma pasta escolhida.", emCurso: false);
            return (false, null);
        }
        var documentos = Path.Combine(pasta, "DOCUMENTOS");
        Directory.CreateDirectory(documentos);
        return (true, documentos);
    }

    static readonly Dictionary<(string, DateTime), string> textosLidos = new();

    /// <summary>O texto das 3 primeiras páginas dos PDFs de DOCUMENTOS e PROJETO (até 2 níveis, 40 arquivos, 60 MB cada), guardado por arquivo.</summary>
    static string TextoDosPdfs(string pasta)
    {
        var sb = new System.Text.StringBuilder();
        var arquivos = new List<FileInfo>();
        foreach (var sub in new[] { "DOCUMENTOS", "PROJETO" })
        {
            var d = new DirectoryInfo(Path.Combine(pasta, sub));
            if (!d.Exists) continue;
            try
            {
                arquivos.AddRange(d.EnumerateFiles("*.pdf", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
                    .Where(f => f.Length <= 60L * 1024 * 1024 && !f.Name.StartsWith("BCI - ", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        foreach (var f in arquivos.Take(40))
        {
            var chave = (f.FullName, f.LastWriteTimeUtc);
            string? texto;
            lock (textosLidos) textosLidos.TryGetValue(chave, out texto);
            if (texto is null)
            {
                try
                {
                    using var doc = UglyToad.PdfPig.PdfDocument.Open(f.FullName);
                    var partes = new List<string>();
                    for (var n = 1; n <= Math.Min(3, doc.NumberOfPages); n++)
                        partes.Add(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(doc.GetPage(n)));
                    texto = string.Join("\n", partes);
                }
                catch (Exception e) when (e is not OutOfMemoryException) { texto = ""; }
                lock (textosLidos) textosLidos[chave] = texto;
            }
            sb.AppendLine(texto);
        }
        return sb.ToString();
    }

    static StackPanel Rotulo(string glifo, string texto) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 8,
        Children = { new FontIcon { Glyph = glifo, FontSize = 16 }, new TextBlock { Text = texto, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } },
    };

    /// <summary>Grava o PDF na pasta DOCUMENTOS do processo como "BCI - CONTRIBUINTE - 01011180077002.pdf" (pedido do usuário, 2026-11-17); o mesmo
    /// nome é trocado pela versão nova. Caracteres que o Windows não aceita em nome de arquivo saem.</summary>
    static string GravarBoletim(string pasta, string inscricao, string? contribuinte, byte[] pdf)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var nome = new string((contribuinte ?? "").Where(c => !invalidos.Contains(c)).ToArray()).Trim().TrimEnd('.');
        nome = System.Text.RegularExpressions.Regex.Replace(nome, @"\s+", " ");
        if (nome.Length > 120) nome = nome[..120].Trim();
        var arquivo = Path.Combine(pasta, "BCI - " + (nome.Length > 0 ? nome + " - " : "") + ChaveBci.Estrita(inscricao) + ".pdf");
        File.WriteAllBytes(arquivo, pdf);
        return arquivo;
    }

    /// <summary>Abre o PDF numa janela do app (o visualizador do WebView2 tem salvar e imprimir); se falhar, no programa padrão de PDF.</summary>
    async void AbrirBoletim(string arquivo, string inscricao)
    {
        try
        {
            var janela = new Window { Title = "Boletim cadastral " + inscricao };
            var web = new WebView2();
            janela.Content = web;
            janela.AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 1100));
            janela.Activate();
            await web.EnsureCoreWebView2Async(await PaginaSites.Ambiente());
            web.CoreWebView2.Navigate(new Uri(arquivo).AbsoluteUri);
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("sites.bci.abrir", e);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(arquivo) { UseShellExecute = true }); }
            catch (Exception ex) { RegistroErros.Gravar("sites.bci.abrir2", ex); }
        }
    }

    /// <summary>Ajustes › Fontes de dados › "Testar o boletim": roda o fluxo inteiro com uma inscrição e diz em que passo parou. Nada é gravado.</summary>
    internal async Task TestarBoletimAsync(string inscricao, TextBlock saida)
    {
        if (ChaveBci.Estrita(inscricao) is null) { saida.Text = "Digite uma inscrição municipal com 14 algarismos."; return; }
        var r = await ExecutarBciAsync(inscricao, m => saida.Text = m);
        saida.Text = r.Estado == EstadoBci.Aberto
            ? "Tudo certo: o GPI devolveu o boletim (" + (r.Pdf?.Length / 1024 ?? 0) + " KB" + (r.Ano is { Length: > 0 } a ? ", ano " + a : "") + "). Nada foi gravado."
            : "Parou em: " + (r.Passo ?? "?") + ". " + ChaveBci.Texto(r);
    }

    /// <summary>O adaptador do GPI: scripts curtos (<see cref="ScriptsSites.Gpi"/>) e esperas com tempo-limite, sempre na thread da janela.</summary>
    sealed class AdaptadorGpi(MainWindow j) : IAdaptadorGpi
    {
        CoreWebView2? core;

        async Task<JsonElement> Op(object arg, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            core ??= await j.GpiOcultoAsync();
            var r = await core.ExecuteScriptAsync(ScriptsSites.Chamar(ScriptsSites.Gpi, arg));
            ct.ThrowIfCancellationRequested();
            using var d = JsonDocument.Parse(r);
            return d.RootElement.Clone();
        }

        static bool B(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
        static string S(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

        static async Task<JsonElement?> EsperarAsync(Func<CancellationToken, Task<JsonElement?>> cond, TimeSpan limite, CancellationToken ct)
        {
            var fim = DateTime.UtcNow + limite;
            while (DateTime.UtcNow < fim)
            {
                if (await cond(ct) is { } ok) return ok;
                await Task.Delay(400, ct);
            }
            return null;
        }

        /// <summary>Espera o GPI assentar: logado (a busca de funcionalidades existe) ou pedindo senha; null se nenhum dos dois em 40 s
        /// (sem sessão, o GPI chega a mandar para outro endereço).</summary>
        async Task<bool?> Logado(CancellationToken ct)
        {
            bool? r = null;
            await EsperarAsync(async t =>
            {
                var x = await Op(new { op = "estado" }, t);
                if (B(x, "ok") && B(x, "logado")) { r = true; return x; }
                if (B(x, "ok") && B(x, "senha")) { r = false; return x; }
                return null;
            }, TimeSpan.FromSeconds(40), ct);
            return r;
        }

        public async Task<bool> GarantirSessaoAsync(CancellationToken ct)
        {
            core ??= await j.GpiOcultoAsync();
            if (!(core.Source ?? "").StartsWith("https://gpi12.cloud.el.com.br/", StringComparison.Ordinal))
                await NavegarAsync(core, Modulos.Endereco(Modulo.Gpi));   // ficou fora do GPI (sessão caiu): volta ao início
            var r = await Logado(ct);
            if (r == true) return true;
            if (r == false && await j.PaginaDeSites.EntrarSozinhoAsync(Modulo.Gpi, core) && await Logado(ct) == true) return true;
            // Pode ser que o usuário tenha entrado agora pelo módulo GPI: recarrega uma vez com os cookies novos.
            await NavegarAsync(core, Modulos.Endereco(Modulo.Gpi));
            return await Logado(ct) == true;
        }

        public async Task<bool> AbrirManutencaoAsync(CancellationToken ct)
        {
            var e = await Op(new { op = "estado" }, ct);
            var mexeu = false;
            if (B(e, "ficha")) { await Op(new { op = "fecharFicha" }, ct); mexeu = true; }
            var ok = await EsperarAsync(async t =>
            {
                var x = await Op(new { op = "estado" }, t);
                if (B(x, "manutencao") && !B(x, "ficha")) return x;
                await Op(new { op = "abrirTela" }, t);
                mexeu = true;
                await Task.Delay(900, t);
                return null;
            }, TimeSpan.FromSeconds(30), ct) is not null;
            if (ok && mexeu)
            {
                // Fechar a ficha ou abrir a tela faz o GPI recarregar a lista ~1,8 s depois: espera essa recarga passar antes de pesquisar.
                var viu = false;
                await EsperarAsync(async t => { var x = await Op(new { op = "estado" }, t); if (B(x, "carregando")) viu = true; return viu && !B(x, "carregando") ? x : null; },
                    TimeSpan.FromSeconds(4), ct);
                await Task.Delay(500, ct);
            }
            return ok;
        }

        /// <summary>"Igual" primeiro (a inscrição tem 14 algarismos fixos: o GPI devolve só aquele cadastro). Sem nenhuma linha, tenta "Contém",
        /// caso o GPI guarde a inscrição com outra formatação; a conferência exata é sempre feita aqui (só a inscrição pedida vale).</summary>
        public async Task<IReadOnlyList<CandidatoGpi>> PesquisarExatoAsync(string inscricao, CancellationToken ct)
        {
            var igual = await PesquisarAsync(inscricao, "Igual", ct);
            return igual.Count > 0 ? igual : await PesquisarAsync(inscricao, "Contém", ct);
        }

        async Task<IReadOnlyList<CandidatoGpi>> PesquisarAsync(string inscricao, string cond, CancellationToken ct)
        {
            var p = await Op(new { op = "pesquisar", insc = inscricao, cond }, ct);
            for (var i = 0; i < 3 && B(p, "mudou"); i++)
            {
                await Task.Delay(1200, ct);   // o GPI redesenha a barra de pesquisa depois de trocar o filtro
                p = await Op(new { op = "pesquisar", insc = inscricao, cond }, ct);
            }
            if (!B(p, "ok") || B(p, "mudou")) throw new InvalidOperationException("a pesquisa do GPI não aceitou o filtro (" + S(p, "erro") + ")");
            var r = await EsperarAsync(async t => { var x = await Op(new { op = "linhas", insc = inscricao, cond }, t); return B(x, "pronto") ? x : null; }, TimeSpan.FromSeconds(40), ct)
                ?? throw new TimeoutException("a lista do GPI não respondeu");
            return r.GetProperty("linhas").EnumerateArray().Select(l => new CandidatoGpi(S(l, "inscricao"), S(l, "tipo"))).ToList();
        }

        public async Task<FichaGpi?> AbrirConsultaAsync(string inscricao, CancellationToken ct)
        {
            var c = await Op(new { op = "consultar", insc = inscricao }, ct);
            if (!B(c, "ok")) return null;
            var f = await EsperarAsync(async t => { var x = await Op(new { op = "ficha", insc = inscricao }, t); return B(x, "pronto") ? x : null; }, TimeSpan.FromSeconds(30), ct);
            return f is { } v ? new FichaGpi(S(v, "inscricao"), S(v, "ano"), B(v, "consulta")) : null;
        }

        public async Task<byte[]?> EmitirAsync(string inscricao, CancellationToken ct)
        {
            // No ensaio, um clique logo que a ficha apareceu foi ignorado pelo GPI: espera um pouco e, sem PDF em 15 s, clica mais uma vez
            // (a captura só faz um envio por pedido).
            JsonElement? p = null;
            for (var tentativa = 0; tentativa < 2 && p is null; tentativa++)
            {
                await Task.Delay(tentativa == 0 ? 1500 : 500, ct);
                var r = await Op(new { op = "emitir", insc = inscricao }, ct);
                if (!B(r, "ok")) throw new InvalidOperationException("o botão Boletim Cadastral não foi achado na ficha (" + S(r, "erro") + ")");
                p = await EsperarAsync(async t => { var x = await Op(new { op = "pdf" }, t); return B(x, "pronto") ? x : null; },
                    TimeSpan.FromSeconds(tentativa == 0 ? 15 : 45), ct);
            }
            if (p is not { } pdf) throw new TimeoutException("o GPI não devolveu o PDF");
            var tipo = S(pdf, "tipo");
            if (!tipo.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("o GPI respondeu " + (tipo.Length == 0 ? S(pdf, "erro") : tipo) + " em vez do PDF");
            return Convert.FromBase64String(S(pdf, "base64"));
        }
    }
}
