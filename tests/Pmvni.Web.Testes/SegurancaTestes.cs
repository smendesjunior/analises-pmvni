using System.Text.Json;
using Pmvni.Composicao.Web;
using Xunit;

namespace Pmvni.Web.Testes;

public sealed class SegurancaFixture : IAsyncLifetime
{
    public HostOculto Host = null!;
    public async Task InitializeAsync() => Host = await HostOculto.CriarAsync(900, 1200);
    public async Task DisposeAsync() => await Host.DisposeAsync();
}

public sealed class SegurancaTestes : IClassFixture<SegurancaFixture>
{
    readonly HostOculto host;
    public SegurancaTestes(SegurancaFixture f) => host = f.Host;

    static Task<JsonElement> Avaliar(HostOculto h, string expressao) => AvaliarEm(h, expressao);
    Task<JsonElement> Avaliar(string expressao) => AvaliarEm(host, expressao);

    static async Task<JsonElement> AvaliarEm(HostOculto host, string expressao)
    {
        var resp = await host.CdpAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(resp);
        return doc.RootElement.GetProperty("result").GetProperty("value").Clone();
    }

    [Fact]
    public async Task requisicao_externa_bloqueada()
    {
        // Documento SEM CSP na pasta da sessão: só o filtro de requisições protege. A resposta é 403 e nada chega à página.
        await using var proprio = await HostOculto.CriarAsync(800, 600);
        File.WriteAllText(Path.Combine(proprio.Documento.PastaSessao, "sem-csp.html"),
            "<!DOCTYPE html><html><body><img id=\"i\" src=\"https://exemplo.com/x.png\"></body></html>");
        await proprio.Documento.NavegarAsync(Seguranca.UrlBase + "sem-csp.html");
        await Avaliar(proprio, "new Promise(r => { const i = document.getElementById('i'); if (i.complete) r(1); else { i.onload = i.onerror = () => r(1); } })");
        Assert.Equal(0, (await Avaliar(proprio, "document.getElementById('i').naturalWidth")).GetInt32());
        Assert.Contains(proprio.Documento.RequisicoesBloqueadas, u => u.StartsWith("https://exemplo.com/x.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task core_protegido_desde_a_criacao_sem_passar_por_carregar()
    {
        // ExecutarAsync é público: a navegação crua também esbarra na sandbox.
        await using var proprio = await HostOculto.CriarAsync(800, 600);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proprio.ExecutarAsync(async core =>
        {
            await HostOculto.NavegarParaUrlAsync(core, "https://exemplo.com/", TimeSpan.FromSeconds(5));
            return true;
        }));
        Assert.Contains("Navegação falhou", ex.Message);
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proprio.ExecutarAsync(async core =>
        {
            await HostOculto.NavegarParaUrlAsync(core, "data:text/html,<p>oi</p>", TimeSpan.FromSeconds(5));
            return true;
        }));
        Assert.Contains("Navegação falhou", ex.Message);
        Assert.False((await proprio.ExecutarAsync(core => Task.FromResult(core.Settings.AreDefaultContextMenusEnabled))));
    }

    /// <summary>Atalhos do navegador (Ctrl+P, F5, Ctrl+R…) desligados e, depois do carregamento, nenhuma navegação de nível
    /// superior: nem recarga, nem a mesma URL do documento, nem outra página do host (só CarregarAsync navega).</summary>
    [Fact]
    public async Task atalhos_e_recarga_do_documento_bloqueados()
    {
        await host.Documento.CarregarAsync(Ambiente.Resultado("<p>x</p>"), Ambiente.Cfg);
        Assert.False(await host.ExecutarAsync(core => Task.FromResult(core.Settings.AreBrowserAcceleratorKeysEnabled)));
        await Avaliar("window.__marca = 42; 1");
        var url = (await Avaliar("location.href")).GetString()!;
        Assert.StartsWith(Seguranca.UrlBase + "documento.html?c=", url);
        File.WriteAllText(Path.Combine(host.Documento.PastaSessao, "outra.html"), "<p>outra</p>");
        foreach (var destino in new[] { url, Seguranca.UrlBase + "documento.html", Seguranca.UrlBase + "outra.html" })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.ExecutarAsync(async core =>
            {
                await HostOculto.NavegarParaUrlAsync(core, destino, TimeSpan.FromSeconds(5));
                return true;
            }));
            Assert.Contains("Navegação falhou", ex.Message);
            Assert.Contains(host.Documento.RequisicoesBloqueadas, u => u == destino);
        }
        // F5 / Ctrl+R / location.reload(): recarga cancelada, o documento segue o mesmo (a marca do JS continua lá).
        await host.ExecutarAsync(core => { core.Reload(); return Task.FromResult(true); });
        await Avaliar("location.reload(); 1");
        await Task.Delay(800);
        Assert.Equal(42, (await Avaliar("window.__marca")).GetInt32());
        Assert.Equal(url, (await Avaliar("location.href")).GetString());
        // E um novo CarregarAsync continua funcionando.
        await host.Documento.CarregarAsync(Ambiente.Resultado("<p>y</p>"), Ambiente.Cfg);
        Assert.NotEqual(url, (await Avaliar("location.href")).GetString());
    }

    /// <summary>D4: controllers InPrivate; abre um relatório com uma marca única (no requerente e no processo, que vai ao
    /// &lt;title&gt;), compõe, aplica e imprime, fecha o host e procura a marca (UTF-8 e UTF-16) em todos os arquivos da pasta
    /// de dados. Limite: aqui o processo do navegador segue vivo (outros hosts do teste), e um perfil comum só grava o
    /// histórico ao sair; a prova com o processo encerrado (e o controle negativo, sem InPrivate) é a verificação
    /// <c>perfil_webview2</c> de tools/fidelidade.py.</summary>
    [Fact]
    public async Task conteudo_do_relatorio_nao_fica_no_perfil_do_webview2()
    {
        var marca = "MARCAPERFIL" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var m = Pmvni.Composicao.ModeloOficial.Carregar();
        var origem = Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", "curto.json");
        var temp = Path.Combine(Path.GetDirectoryName(origem)!, "tmp-perfil-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            var no = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(origem))!;
            no["capa"]!["requerente"] = marca;
            no["capa"]!["processo"] = marca; // vai também ao <title> (histórico do perfil, se gravasse)
            File.WriteAllText(temp, no.ToJsonString());
            var r = Pmvni.Composicao.RelatorioRenderer.Renderizar(temp, new Pmvni.Composicao.OpcoesRender(false, true), m);
            Assert.True(r.Ok, string.Join("; ", r.Erros));
            await using (var proprio = await HostOculto.CriarAsync(1024, 1400))
            {
                Assert.True(await proprio.ExecutarAsync(core => Task.FromResult(core.Profile.IsInPrivateModeEnabled)));
                await proprio.Documento.CarregarAsync(r, ConfigFolhas.PadraoDeDados(m, temp));
                var plano = await proprio.Documento.ComporAsync();
                await proprio.Documento.AplicarPlanoAsync(plano);
                Assert.Contains(marca, string.Concat(await proprio.Documento.TextoPorFolhaAsync()));
                Assert.NotEmpty(await proprio.Documento.ImprimirPdfAsync());
            }
            await Task.Delay(1500); // o navegador termina de gravar o que fosse gravar
            var achados = ProcurarMarca(PastaDados.Raiz, marca);
            Assert.True(achados.Count == 0, "marca encontrada em: " + string.Join(", ", achados));
        }
        finally { File.Delete(temp); }
    }

    static List<string> ProcurarMarca(string raiz, string marca)
    {
        var padroes = new[] { System.Text.Encoding.UTF8.GetBytes(marca), System.Text.Encoding.Unicode.GetBytes(marca) };
        var achados = new List<string>();
        foreach (var arquivo in Directory.EnumerateFiles(raiz, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            byte[] bytes;
            try
            {
                using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var mem = new MemoryStream();
                f.CopyTo(mem);
                bytes = mem.ToArray();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            if (padroes.Any(p => bytes.AsSpan().IndexOf(p) >= 0)) achados.Add(Path.GetRelativePath(raiz, arquivo));
        }
        return achados;
    }

    [Theory]
    [InlineData("https://doc.pmvni.invalid/documento.html", true)]
    [InlineData("http://doc.pmvni.invalid/documento.html", false)]
    [InlineData("https://exemplo.com/", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("", false)]
    public void origem_de_mensagem_exige_https_e_host_do_documento(string origem, bool esperado) =>
        Assert.Equal(esperado, Seguranca.EhHostDocumento(origem));

    [Fact]
    public async Task falha_ao_apagar_a_pasta_fica_registrada()
    {
        var host2 = await HostOculto.CriarAsync(800, 600);
        var doc = host2.Documento;
        FalhaRegistrada = null;
        doc.FalhaAoApagarPasta += e => FalhaRegistrada = e;
        using (new FileStream(Path.Combine(doc.PastaSessao, "preso.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            await host2.DisposeAsync();
        Assert.NotNull(doc.ErroAoApagarPasta);
        Assert.NotNull(FalhaRegistrada);
        try { Directory.Delete(doc.PastaSessao, true); } catch { }
    }
    static Exception? FalhaRegistrada;

    [Fact]
    public async Task sessoes_orfas_antigas_sao_apagadas_na_criacao()
    {
        var raiz = Path.Combine(PastaDados.Raiz, "sessao");
        var velha = Path.Combine(raiz, "orfa-" + Guid.NewGuid().ToString("N"));
        var recente = Path.Combine(raiz, "viva-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(velha);
        File.WriteAllText(Path.Combine(velha, "documento.html"), "<p>dados pessoais</p>");
        Directory.CreateDirectory(recente);
        Directory.SetLastWriteTimeUtc(velha, DateTime.UtcNow.AddDays(-2));
        try
        {
            await using var h = await HostOculto.CriarAsync(800, 600);
            Assert.False(Directory.Exists(velha));
            Assert.True(Directory.Exists(recente));
        }
        finally { try { Directory.Delete(recente, true); } catch { } }
    }

    [Fact]
    public async Task csp_bloqueia_imagem_externa_no_documento_carregado()
    {
        await host.Documento.CarregarAsync(Ambiente.Resultado("<img id=\"i\" src=\"https://exemplo.com/x.png\">"), Ambiente.Cfg);
        Assert.Equal(0, (await Avaliar("document.getElementById('i').naturalWidth")).GetInt32());
        Assert.Equal("https://doc.pmvni.invalid/documento.html", (await Avaliar("location.origin + location.pathname")).GetString());
    }

    [Fact]
    public async Task script_inline_nao_executa()
    {
        await host.Documento.CarregarAsync(Ambiente.Resultado("<script>window.__executou = true;</script><p>x</p>"), Ambiente.Cfg);
        Assert.False((await Avaliar("window.__executou === true")).GetBoolean());
        // o paginador.js autorizado, esse sim, executa
        Assert.True((await Avaliar("typeof Paginador === 'object'")).GetBoolean());
    }

    [Fact]
    public async Task csp_inserida_so_na_copia_da_sessao()
    {
        var r = Ambiente.Resultado("<p>x</p>");
        await host.Documento.CarregarAsync(r, Ambiente.Cfg);
        var copia = File.ReadAllText(Path.Combine(host.Documento.PastaSessao, "documento.html"));
        Assert.Contains("Content-Security-Policy", copia);
        Assert.Contains("<script src=\"https://doc.pmvni.invalid/paginador.js\"", copia);
        Assert.DoesNotContain("Content-Security-Policy", r.Html);
        Assert.Equal(
            "default-src 'none'; img-src data: https://doc.pmvni.invalid; style-src 'unsafe-inline'; script-src https://doc.pmvni.invalid/paginador.js",
            Seguranca.Csp);
    }

    [Fact]
    public async Task janela_nova_e_ajustes_de_seguranca()
    {
        await host.Documento.CarregarAsync(Ambiente.Resultado("<p>x</p>"), Ambiente.Cfg);
        Assert.True((await Avaliar("window.open('https://doc.pmvni.invalid/documento.html') === null")).GetBoolean());
        var ajustes = await host.ExecutarAsync(core => Task.FromResult(
            (core.Settings.AreDefaultContextMenusEnabled, core.Settings.IsGeneralAutofillEnabled, core.Settings.IsPasswordAutosaveEnabled,
             core.Settings.IsStatusBarEnabled, core.Settings.AreHostObjectsAllowed)));
        Assert.Equal((false, false, false, false, false), ajustes);
    }

    [Fact]
    public async Task pasta_da_sessao_apagada_ao_fechar()
    {
        string pasta;
        await using (var outro = await HostOculto.CriarAsync(800, 600))
        {
            await outro.Documento.CarregarAsync(Ambiente.Resultado("<p>x</p>"), Ambiente.Cfg);
            pasta = outro.Documento.PastaSessao;
            Assert.True(Directory.Exists(pasta));
            Assert.StartsWith(Path.Combine(PastaDados.Raiz, "sessao"), pasta);
        }
        Assert.False(Directory.Exists(pasta));
    }

    [Fact]
    public async Task relatorio_real_do_corpus_carrega_sem_bloqueios()
    {
        var m = Pmvni.Composicao.ModeloOficial.Carregar();
        var dados = Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", "curto.json");
        var r = Pmvni.Composicao.RelatorioRenderer.Renderizar(dados, new Pmvni.Composicao.OpcoesRender(false, false), m);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        await host.Documento.CarregarAsync(r, ConfigFolhas.PadraoDeDados(m, dados));
        Assert.True((await Avaliar("document.body.innerText.length > 100")).GetBoolean());
        Assert.Empty(host.Documento.RequisicoesBloqueadas);
        Assert.Empty(host.Documento.Descartadas);
    }
}
