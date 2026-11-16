using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>As opções novas dos Ajustes (auditoria da interface, §3.3): padrão igual ao comportamento de antes, ida e volta pelo
/// ajustes.json, valor fora da lista volta ao padrão, arquivo antigo (sem as chaves novas) lido sem perda e "Restaurar os ajustes"
/// sem tocar em recentes, agentes nem análises em andamento.</summary>
public class AjustesOpcoesTestes
{
    [Fact]
    public void padroes_iguais_ao_comportamento_de_antes()
    {
        var a = new Ajustes();
        Assert.Equal("ultimo", a.AbrirEm);
        Assert.False(a.LembrarJanela);
        Assert.Null(a.Janela);
        Assert.Equal(10, a.RecentesMax);
        Assert.Equal("automatico", a.FaixaRotulos);
        Assert.Equal("indicador", a.AvisosLayout);
        Assert.Equal("largura", a.ZoomInicial);
        Assert.False(a.LembrarPaineis);
        Assert.True(a.PainelLateral);
        Assert.True(a.PainelPropriedades);
        Assert.Equal(30, a.RecuperacaoDias);
        Assert.Equal("numero", a.ProcessosOrdem);
        Assert.False(a.ProcessosPastas);
        Assert.Equal("data", a.DespachosAgrupar);
        Assert.Equal("recentes", a.DespachosOrdem);
        Assert.Null(a.Soffice);
        Assert.False(a.DespachoAbrirPdf);
        Assert.Equal("lei", a.LegislacaoEscopo);
        Assert.Equal("", a.PedidoPadrao);
        Assert.True(a.ConfirmarCancelar);
        Assert.True(a.ConfirmarFechar);
        // "Último usado" abre no módulo lembrado.
        a.Modulo = Modulo.Processos;
        Assert.Equal(Modulo.Processos, a.ModuloAoAbrir());
    }

    [Fact]
    public void opcoes_novas_vao_e_voltam_pelo_arquivo()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-opcoes");
        var a = new Ajustes
        {
            AbrirEm = "Legislacao", LembrarJanela = true, Janela = new JanelaGuardada(10, 20, 1200, 800, true), RecentesMax = 5,
            FaixaRotulos = "icones", AvisosLayout = "faixas", ZoomInicial = "100", LembrarPaineis = true, PainelLateral = false,
            PainelPropriedades = false, RecuperacaoDias = 90, ProcessosOrdem = "requerente", ProcessosPastas = true, DespachosAgrupar = "processo",
            DespachosOrdem = "antigos", Soffice = @"C:\LibreOffice\program\soffice.exe", DespachoAbrirPdf = true, LegislacaoEscopo = "todas",
            PedidoPadrao = "Conferir recuos e taxa de ocupação.", ConfirmarCancelar = false, ConfirmarFechar = false,
        };
        a.Salvar(pasta);
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, "ajustes.json")))!.AsObject();
        foreach (var chave in new[] { "abrir_em", "lembrar_janela", "janela", "recentes_max", "faixa_rotulos", "avisos_layout", "zoom_inicial",
                     "lembrar_paineis", "paineis", "recuperacao_dias", "processos_ordem", "processos_pastas", "despachos_agrupar", "despachos_ordem",
                     "soffice", "despacho_abrir_pdf", "legislacao_escopo", "pedido_padrao", "confirmar_cancelar", "confirmar_fechar" })
            Assert.True(j.ContainsKey(chave), chave);

        var b = Ajustes.Carregar(pasta);
        Assert.Equal("Legislacao", b.AbrirEm);
        Assert.Equal(Modulo.Legislacao, b.ModuloAoAbrir());
        Assert.True(b.LembrarJanela);
        Assert.Equal(new JanelaGuardada(10, 20, 1200, 800, true), b.Janela);
        Assert.Equal(5, b.RecentesMax);
        Assert.Equal("icones", b.FaixaRotulos);
        Assert.Equal("faixas", b.AvisosLayout);
        Assert.Equal("100", b.ZoomInicial);
        Assert.True(b.LembrarPaineis);
        Assert.False(b.PainelLateral);
        Assert.False(b.PainelPropriedades);
        Assert.Equal(90, b.RecuperacaoDias);
        Assert.Equal("requerente", b.ProcessosOrdem);
        Assert.True(b.ProcessosPastas);
        Assert.Equal("processo", b.DespachosAgrupar);
        Assert.Equal("antigos", b.DespachosOrdem);
        Assert.Equal(@"C:\LibreOffice\program\soffice.exe", b.Soffice);
        Assert.True(b.DespachoAbrirPdf);
        Assert.Equal("todas", b.LegislacaoEscopo);
        Assert.Equal("Conferir recuos e taxa de ocupação.", b.PedidoPadrao);
        Assert.False(b.ConfirmarCancelar);
        Assert.False(b.ConfirmarFechar);
    }

    [Fact]
    public void valores_fora_da_lista_voltam_ao_padrao()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-opcoes-invalidas");
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"), """
            { "abrir_em": "Ajustes", "lembrar_janela": "sim", "janela": { "x": 0, "y": 0, "l": 50, "a": 9000000, "max": false },
              "recentes_max": 7, "faixa_rotulos": "grande", "avisos_layout": "popup", "zoom_inicial": "200", "lembrar_paineis": 1,
              "paineis": { "lateral": "não" }, "recuperacao_dias": 365, "processos_ordem": "tamanho", "processos_pastas": null,
              "despachos_agrupar": "mes", "despachos_ordem": "aleatorio", "soffice": "   ", "despacho_abrir_pdf": "x",
              "legislacao_escopo": "kit", "pedido_padrao": 42, "confirmar_cancelar": "talvez", "confirmar_fechar": [] }
            """);
        var a = Ajustes.Carregar(pasta);
        var p = new Ajustes();
        Assert.Equal(p.AbrirEm, a.AbrirEm);
        Assert.Equal(p.LembrarJanela, a.LembrarJanela);
        Assert.Null(a.Janela);
        Assert.Equal(p.RecentesMax, a.RecentesMax);
        Assert.Equal(p.FaixaRotulos, a.FaixaRotulos);
        Assert.Equal(p.AvisosLayout, a.AvisosLayout);
        Assert.Equal(p.ZoomInicial, a.ZoomInicial);
        Assert.Equal(p.LembrarPaineis, a.LembrarPaineis);
        Assert.Equal(p.PainelLateral, a.PainelLateral);
        Assert.Equal(p.RecuperacaoDias, a.RecuperacaoDias);
        Assert.Equal(p.ProcessosOrdem, a.ProcessosOrdem);
        Assert.Equal(p.ProcessosPastas, a.ProcessosPastas);
        Assert.Equal(p.DespachosAgrupar, a.DespachosAgrupar);
        Assert.Equal(p.DespachosOrdem, a.DespachosOrdem);
        Assert.Null(a.Soffice);
        Assert.Equal(p.DespachoAbrirPdf, a.DespachoAbrirPdf);
        Assert.Equal(p.LegislacaoEscopo, a.LegislacaoEscopo);
        Assert.Equal("", a.PedidoPadrao);
        Assert.True(a.ConfirmarCancelar);
        Assert.True(a.ConfirmarFechar);
    }

    [Fact]
    public void arquivo_antigo_sem_as_chaves_novas_e_lido_sem_perda()
    {
        // O ajustes.json da Etapa 2 antes desta rodada: as chaves de sempre, nenhuma das novas.
        using var pasta = Ambiente.PastaDeTeste("ajustes-antigo");
        var recentes = Enumerable.Range(1, 10).Select(i => $@"C:\r\{i}.rpmvni").ToArray();
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"), new JsonObject
        {
            ["modulo"] = "Despachos", ["aba_por_modulo"] = new JsonObject { ["Relatorios"] = "Revisão" }, ["tema"] = "escuro",
            ["intervalo_autosave_s"] = 60, ["marcas_revisao"] = false,
            ["recentes"] = new JsonArray(recentes.Select(r => (JsonNode)r).ToArray()),
            ["pasta_processos"] = @"C:\proc", ["pasta_kit"] = @"C:\kit",
            ["agentes"] = new JsonObject { ["chatgpt"] = new JsonObject { ["modo"] = "chave", ["aviso_versao"] = 1 } },
            ["execucoes_claude"] = new JsonArray(new JsonObject { ["processo"] = @"C:\p", ["registro"] = @"C:\p\r" }),
        }.ToJsonString());
        var a = Ajustes.Carregar(pasta);
        Assert.Equal(Modulo.Despachos, a.Modulo);
        Assert.Equal(Modulo.Despachos, a.ModuloAoAbrir());
        Assert.Equal("Revisão", a.AbaPorModulo[Modulo.Relatorios]);
        Assert.Equal("escuro", a.Tema);
        Assert.Equal(60, a.IntervaloAutosaveS);
        Assert.False(a.MarcasRevisao);
        Assert.Equal(recentes, a.Recentes);
        Assert.Equal(@"C:\proc", a.PastaProcessos);
        Assert.Equal(@"C:\kit", a.PastaKit);
        Assert.Equal(ModoAcesso.Chave, a.Agentes[Provedor.ChatGpt].Modo);
        Assert.Single(a.ExecucoesClaude);
        // As novas no padrão; regravar mantém tudo o que veio.
        Assert.Equal(new Ajustes().ZoomInicial, a.ZoomInicial);
        a.Salvar(pasta);
        var b = Ajustes.Carregar(pasta);
        Assert.Equal(recentes, b.Recentes);
        Assert.Equal("escuro", b.Tema);
        Assert.Equal(@"C:\kit", b.PastaKit);
        Assert.Equal(ModoAcesso.Chave, b.Agentes[Provedor.ChatGpt].Modo);
    }

    [Fact]
    public void recentes_seguem_o_maximo_escolhido_e_a_lista_se_limpa()
    {
        var a = new Ajustes { RecentesMax = 20 };
        for (var i = 0; i < 25; i++) a.AdicionarRecente($@"C:\r\{i}.rpmvni");
        Assert.Equal(20, a.Recentes.Count);
        Assert.Equal(@"C:\r\24.rpmvni", a.Recentes[0]);
        a.RecentesMax = 5;
        Assert.Equal(5, a.Recentes.Count);
        Assert.Equal(@"C:\r\24.rpmvni", a.Recentes[0]);
        a.LimparRecentes();
        Assert.Empty(a.Recentes);
        Assert.Throws<ArgumentOutOfRangeException>(() => a.RecentesMax = 7);
        Assert.Equal(new[] { 5, 10, 20 }, Ajustes.OpcoesRecentes);
    }

    [Fact]
    public void restaurar_os_ajustes_nao_toca_em_recentes_agentes_nem_analises()
    {
        var a = new Ajustes
        {
            Tema = "escuro", IntervaloAutosaveS = 120, MarcasRevisao = false, PastaProcessos = @"C:\p", PastaKit = @"C:\k", AbrirEm = "Processos",
            ZoomInicial = "pagina", AvisosLayout = "faixas", RecuperacaoDias = 7, PedidoPadrao = "x", ConfirmarFechar = false, LembrarJanela = true,
            Janela = new JanelaGuardada(1, 2, 900, 700, false), Soffice = @"C:\so.exe", RecentesMax = 20,
        };
        a.Recentes.Add(@"C:\a.rpmvni");
        a.Agentes[Provedor.Claude].Modo = ModoAcesso.Chave;
        a.Agentes[Provedor.Claude].AvisoVersao = 1;
        a.ExecucoesClaude.Add(new ExecucaoClaudePendente(@"C:\p", @"C:\p\r"));
        a.Modulo = Modulo.Legislacao;

        a.RestaurarPadrao();

        var p = new Ajustes();
        Assert.Equal(p.Tema, a.Tema);
        Assert.Equal(p.IntervaloAutosaveS, a.IntervaloAutosaveS);
        Assert.Equal(p.MarcasRevisao, a.MarcasRevisao);
        Assert.Null(a.PastaProcessos);
        Assert.Null(a.PastaKit);
        Assert.Equal(p.AbrirEm, a.AbrirEm);
        Assert.Equal(p.ZoomInicial, a.ZoomInicial);
        Assert.Equal(p.AvisosLayout, a.AvisosLayout);
        Assert.Equal(p.RecuperacaoDias, a.RecuperacaoDias);
        Assert.Equal("", a.PedidoPadrao);
        Assert.True(a.ConfirmarFechar);
        Assert.False(a.LembrarJanela);
        Assert.Null(a.Janela);
        Assert.Null(a.Soffice);
        Assert.Equal(10, a.RecentesMax);
        // Preservados.
        Assert.Equal(new[] { @"C:\a.rpmvni" }, a.Recentes);
        Assert.Equal(ModoAcesso.Chave, a.Agentes[Provedor.Claude].Modo);
        Assert.Equal(1, a.Agentes[Provedor.Claude].AvisoVersao);
        Assert.Single(a.ExecucoesClaude);
        Assert.Equal(Modulo.Legislacao, a.Modulo);
    }

    [Fact]
    public void pedido_padrao_ate_2000_caracteres()
    {
        var a = new Ajustes { PedidoPadrao = new string('x', 2500) };
        Assert.Equal(2000, a.PedidoPadrao.Length);
        Assert.Equal(2000, Ajustes.MaxPedidoPadrao);
    }

    [Fact]
    public void janela_guardada_so_vale_com_tamanho_plausivel()
    {
        Assert.True(new JanelaGuardada(0, 0, 900, 600, false).Valida);
        Assert.False(new JanelaGuardada(0, 0, 100, 600, false).Valida);
        Assert.False(new JanelaGuardada(0, 0, 900, 50000, false).Valida);
    }
}
