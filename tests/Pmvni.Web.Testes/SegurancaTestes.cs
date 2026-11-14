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
        await using var proprio = await HostOculto.CriarAsync(800, 600); // host próprio: a Seguranca é aplicada a outra pasta
        var bloqueios = new List<string>();
        var pasta = Path.Combine(PastaDados.Raiz, "sessao", "teste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, "sem-csp.html"), "<!DOCTYPE html><html><body><img id=\"i\" src=\"https://exemplo.com/x.png\"></body></html>");
            await proprio.ExecutarAsync(async core =>
            {
                await Seguranca.AplicarAsync(core, pasta, u => { lock (bloqueios) bloqueios.Add(u); });
                await HostOculto.NavegarParaUrlAsync(core, Seguranca.UrlBase + "sem-csp.html");
                return true;
            });
            await Avaliar(proprio, "new Promise(r => { const i = document.getElementById('i'); if (i.complete) r(1); else { i.onload = i.onerror = () => r(1); } })");
            Assert.Equal(0, (await Avaliar(proprio, "document.getElementById('i').naturalWidth")).GetInt32());
            lock (bloqueios) Assert.Contains(bloqueios, u => u.StartsWith("https://exemplo.com/x.png", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(pasta, true); } catch { } }
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
