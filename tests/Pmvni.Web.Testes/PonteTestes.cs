using System.Text.Json;
using System.Text.RegularExpressions;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Ponte;
using Xunit;

namespace Pmvni.Web.Testes;

public sealed class MensagensTestes
{
    static bool Ler(string json, out MensagemWeb? m, out string? motivo) => Mensagens.TentarLerDaWeb(json, out m, out motivo);

    [Fact]
    public void mensagem_valida_e_lida()
    {
        Assert.True(Ler("""{"v":1,"tipo":"pronto","id":"a","dados":{"x":1}}""", out var m, out _));
        Assert.Equal("pronto", m!.Tipo);
        Assert.Equal("a", m.Id);
        Assert.Equal(1, m.Dados.GetProperty("x").GetInt32());
    }

    [Theory]
    [InlineData("""{"v":2,"tipo":"pronto","id":"a","dados":{}}""")]
    [InlineData("""{"tipo":"pronto","id":"a","dados":{}}""")]
    [InlineData("""{"v":1,"tipo":"inventado","id":"a","dados":{}}""")]
    [InlineData("""{"v":1,"tipo":"compor","id":"a","dados":{}}""")] // tipo de host->web não vale no sentido web->host
    [InlineData("""{"v":1,"tipo":"pronto","dados":{}}""")]
    [InlineData("""{"v":1,"tipo":"pronto","id":"","dados":{}}""")]
    [InlineData("""{"v":1,"tipo":"pronto","id":"a"}""")]
    [InlineData("""{"v":1,"tipo":"pronto","id":"a","dados":[1]}""")]
    [InlineData("""{"v":"1","tipo":"pronto","id":"a","dados":{}}""")]
    [InlineData("\"texto\"")]
    [InlineData("[1]")]
    [InlineData("nao e json")]
    public void mensagem_invalida_e_descartada(string json)
    {
        Assert.False(Ler(json, out var m, out var motivo));
        Assert.Null(m);
        Assert.False(string.IsNullOrEmpty(motivo));
    }

    /// <summary>Os tipos da ponte (Mensagens.cs) e os do TypeScript (mensagens.ts) são os mesmos, nos dois sentidos.</summary>
    [Fact]
    public void tipos_da_ponte_iguais_nos_dois_lados()
    {
        var ts = File.ReadAllText(Path.Combine(Ambiente.RaizRepositorio(), "web", "paginador", "src", "mensagens.ts"));
        static List<string> Lista(string fonte, string nome)
        {
            var m = Regex.Match(fonte, nome + @"\s*=\s*\[(?<itens>[^\]]*)\]\s*as const");
            Assert.True(m.Success, "lista " + nome + " não encontrada em mensagens.ts");
            return Regex.Matches(m.Groups["itens"].Value, "'([^']+)'").Select(x => x.Groups[1].Value).ToList();
        }
        var host = Lista(ts, "TIPOS_HOST");
        var web = Lista(ts, "TIPOS_WEB");
        Assert.Equal(host.Count, host.Distinct().Count());
        Assert.Equal(web.Count, web.Distinct().Count());
        Assert.Equal(Mensagens.TiposHostParaWeb.Order(StringComparer.Ordinal), host.Order(StringComparer.Ordinal));
        Assert.Equal(Mensagens.TiposWebParaHost.Order(StringComparer.Ordinal), web.Order(StringComparer.Ordinal));

        string[] novosHost = ["estadoEditor", "selecionar", "editarCampo", "concluirEdicao", "obterAncora"];
        string[] novosWeb = ["estadoAplicado", "selecionado", "edicaoIniciada", "editar", "semEdicao", "ancora", "pedirEdicao",
            "edicaoCancelada", "atalho", "menuContexto", "excluidoClicado"];
        Assert.All(novosHost, t => { Assert.Contains(t, host); Assert.Contains(t, (IEnumerable<string>)Mensagens.TiposHostParaWeb); Assert.DoesNotContain(t, (IEnumerable<string>)Mensagens.TiposWebParaHost); });
        Assert.All(novosWeb, t => { Assert.Contains(t, web); Assert.Contains(t, (IEnumerable<string>)Mensagens.TiposWebParaHost); Assert.DoesNotContain(t, (IEnumerable<string>)Mensagens.TiposHostParaWeb); });
    }

    [Fact]
    public void serializar_para_web_exige_tipo_de_host()
    {
        var json = Mensagens.SerializarParaWeb("definirZoom", "z1", new { fator = 1.5 });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("v").GetInt32());
        Assert.Equal("definirZoom", doc.RootElement.GetProperty("tipo").GetString());
        Assert.Equal(1.5, doc.RootElement.GetProperty("dados").GetProperty("fator").GetDouble());
        Assert.Throws<ArgumentException>(() => Mensagens.SerializarParaWeb("pronto", "z1", new { }));
    }
}

public sealed class PonteFixture : IAsyncLifetime
{
    public HostOculto Host = null!;
    public async Task InitializeAsync()
    {
        Host = await HostOculto.CriarAsync(900, 1200);
        await Host.Documento.CarregarAsync(Ambiente.Resultado("<p>x</p>"), Ambiente.Cfg);
    }
    public async Task DisposeAsync() => await Host.DisposeAsync();
}

public sealed class PonteTestes : IClassFixture<PonteFixture>
{
    readonly HostOculto host;
    public PonteTestes(PonteFixture f) => host = f.Host;

    [Fact]
    public async Task chamar_recebe_resposta_com_o_mesmo_id()
    {
        var resp = await host.Documento.ChamarAsync("textoPorFolha", new { }, TimeSpan.FromSeconds(10));
        Assert.Equal(JsonValueKind.Array, resp.GetProperty("folhas").ValueKind);
    }

    [Fact]
    public async Task mensagem_tipo_desconhecido_descartada()
    {
        var recebidas = new List<MensagemWeb>();
        void Ouvir(MensagemWeb m) { lock (recebidas) recebidas.Add(m); }
        host.Documento.Recebida += Ouvir;
        try
        {
            var antes = host.Documento.Descartadas.Count;
            await host.CdpAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = """chrome.webview.postMessage({v:1,tipo:"inventado",id:"x1",dados:{}}); chrome.webview.postMessage({v:2,tipo:"pronto",id:"x2",dados:{}}); 1"""
            }));
            await Esperar(() => host.Documento.Descartadas.Count >= antes + 2);
            Assert.Contains(host.Documento.Descartadas, d => d.Motivo.Contains("inventado"));
            Assert.Contains(host.Documento.Descartadas, d => d.Motivo.Contains("versao"));
            lock (recebidas) Assert.DoesNotContain(recebidas, m => m.Id is "x1" or "x2");
        }
        finally { host.Documento.Recebida -= Ouvir; }
    }

    [Fact]
    public async Task js_descarta_mensagem_invalida_do_host_e_avisa()
    {
        var erros = new TaskCompletionSource<MensagemWeb>();
        void Ouvir(MensagemWeb m) { if (m.Tipo == "erro") erros.TrySetResult(m); }
        host.Documento.Recebida += Ouvir;
        try
        {
            await host.Documento.EnviarBrutoAsync("""{"v":1,"tipo":"inventado","id":"h1","dados":{}}""");
            var m = await erros.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("h1", m.Id);
            Assert.Equal("mensagem_invalida", m.Dados.GetProperty("codigo").GetString());
        }
        finally { host.Documento.Recebida -= Ouvir; }
    }

    [Fact]
    public async Task resposta_erro_vira_excecao_tipada()
    {
        // 'rolarPara' sem pedido válido: o JS responde 'erro', que vira exceção tipada.
        var ex = await Assert.ThrowsAsync<ErroWebException>(() => host.Documento.ChamarAsync("rolarPara", new { }, TimeSpan.FromSeconds(10)));
        Assert.Equal("rolagem_invalida", ex.Codigo);
    }

    [Fact]
    public async Task chamar_sem_resposta_estoura_o_limite()
    {
        // Para provar o limite, registra-se um manipulador que nunca responde.
        await host.CdpAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = "Paginador.registrar('aplicarPlano', () => new Promise(() => {})); 1" }));
        await Assert.ThrowsAsync<TimeoutException>(() => host.Documento.ChamarAsync("aplicarPlano", new { }, TimeSpan.FromMilliseconds(500)));
    }

    static async Task Esperar(Func<bool> condicao)
    {
        var fim = DateTime.UtcNow.AddSeconds(10);
        while (!condicao() && DateTime.UtcNow < fim) await Task.Delay(50);
        Assert.True(condicao(), "condição não atendida em 10 s");
    }
}

public sealed class ConfigFolhasTestes
{
    [Fact]
    public void padrao_usa_os_textos_da_regra_page_do_kit()
    {
        var m = ModeloOficial.Carregar();
        var cfg = ConfigFolhas.Padrao(m);
        var esquerda = Regex.Match(m.Html, "@bottom-left \\{ content: \"([^\"]*)\"").Groups[1].Value;
        Assert.Equal(esquerda, cfg.RodapeEsquerda);
        Assert.Contains("  •  ", cfg.RodapeEsquerda);
        Assert.Equal("RELATÓRIO TÉCNICO  •  PÁGINA {n} DE {t}", cfg.RodapeDireitaModelo);
        Assert.Equal(Pmvni.Composicao.Kit.Cabecalho.CabecalhoUri("SECRETARIA MUNICIPAL DE OBRAS", m.Brasao), cfg.CabecalhoUri);
        Assert.NotEqual(cfg.CabecalhoUri, ConfigFolhas.Padrao(m, "OUTRO ORGAO").CabecalhoUri);
    }

    [Fact]
    public void padrao_de_dados_le_config_orgao()
    {
        var m = ModeloOficial.Carregar();
        var pasta = Path.Combine(PastaDados.Raiz, "teste-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var com = Path.Combine(pasta, "com.json");
            var sem = Path.Combine(pasta, "sem.json");
            File.WriteAllText(com, """{"config":{"orgao":"SECRETARIA X"}}""");
            File.WriteAllText(sem, """{"itens":[]}""");
            Assert.Equal(ConfigFolhas.Padrao(m, "SECRETARIA X"), ConfigFolhas.PadraoDeDados(m, com));
            Assert.Equal(ConfigFolhas.Padrao(m), ConfigFolhas.PadraoDeDados(m, sem));
        }
        finally { Directory.Delete(pasta, true); }
    }
}
