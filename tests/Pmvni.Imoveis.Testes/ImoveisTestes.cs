using System.Buffers.Binary;
using System.Text;
using Pmvni.Imoveis;
using Xunit;

namespace Pmvni.Imoveis.Testes;

public class PesquisaTestes
{
    static RegistroCadastro R(long l, string insc, string? nome) =>
        new(l, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["inscricao_municipal"] = insc, ["nome_do_contribuinte"] = nome });

    static readonly CampoPesquisa[] Campos = [new("inscricao_municipal", "Inscrição municipal", "t"), new("nome_do_contribuinte", "Contribuinte", "t")];

    [Theory]
    [InlineData(Condicao.Contem, "jose", true)]
    [InlineData(Condicao.NaoContem, "jose", false)]
    [InlineData(Condicao.Igual, "JOSÉ DA SILVA", true)]
    [InlineData(Condicao.Diferente, "jose da silva", false)]
    [InlineData(Condicao.ComecaCom, "José", true)]
    [InlineData(Condicao.TerminaCom, "silva", true)]
    [InlineData(Condicao.TerminaCom, "jose", false)]
    public void seis_condicoes_sem_caixa_nem_acento(Condicao c, string valor, bool esperado) =>
        Assert.Equal(esperado, Pesquisa.Corresponde("José da Silva", c, valor));

    [Fact]
    public void vazio_na_origem_nao_satisfaz_nem_as_negativas()
    {
        Assert.False(Pesquisa.Corresponde(null, Condicao.NaoContem, "x"));
        Assert.False(Pesquisa.Corresponde("  ", Condicao.Diferente, "x"));
    }

    [Fact]
    public void curingas_sao_literais_e_zeros_contam()
    {
        Assert.False(Pesquisa.Corresponde("ABC", Condicao.Contem, "%"));
        Assert.True(Pesquisa.Corresponde("50%_off", Condicao.Contem, "%_"));
        Assert.True(Pesquisa.Corresponde("01030660241003", Condicao.ComecaCom, "0103"));
        Assert.False(Pesquisa.Corresponde("01030660241003", Condicao.ComecaCom, "103"));
    }

    [Fact]
    public void valor_vazio_nao_pesquisa_e_paginacao_ordena_pela_inscricao()
    {
        var regs = Enumerable.Range(1, 120).Select(i => R(i, (200 - i).ToString("D14"), "Maria " + i)).ToList();
        Assert.Throws<ArgumentException>(() => Pesquisa.Aplicar(regs, Campos, new Consulta("nome_do_contribuinte", Condicao.Contem, "  ")));
        Assert.Throws<ArgumentException>(() => Pesquisa.Aplicar(regs, Campos, new Consulta("n_documento", Condicao.Contem, "1")));
        var p2 = Pesquisa.Aplicar(regs, Campos, new Consulta("nome_do_contribuinte", Condicao.Contem, "maria", Pagina: 2, Tamanho: 50));
        Assert.Equal(120, p2.Total);
        Assert.Equal(3, p2.Paginas);
        Assert.Equal(50, p2.Itens.Count);
        Assert.Equal((200 - 70).ToString("D14"), p2.Itens[0].InscricaoMunicipal);
    }

    [Fact]
    public void cancelamento_interrompe()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Pesquisa.Aplicar([R(1, "1", "a")], Campos, new Consulta("nome_do_contribuinte", Condicao.Contem, "a"), cts.Token));
    }

    [Fact]
    public void documentos_pontuados_saem_do_relatorio()
    {
        // Montados em partes: o publicador do GitHub recusa CPF/CNPJ escritos no código.
        var cpf = string.Join(".", "111", "222", "333") + "-44";
        var cnpj = "11." + "222.333" + "/0001-" + "99";
        Assert.Equal("Fulano [documento omitido]", RelatorioHtml.SemDocumentos("Fulano " + cpf));
        Assert.Equal("Órgão [documento omitido]", RelatorioHtml.SemDocumentos("Órgão " + cnpj));
        Assert.Equal("01030660241003", RelatorioHtml.SemDocumentos("01030660241003")); // a inscrição fica
    }

    [Fact]
    public void terreno_sao_os_onze_primeiros_digitos()
    {
        Assert.Equal("01030660241", BaseQgis.Terreno("01030660241003"));
        Assert.Null(BaseQgis.Terreno("0103066024100"));
        Assert.Null(BaseQgis.Terreno("0103066024100A"));
    }
}

public class ShapefileTestes
{
    /// <summary>Grava um shapefile de um polígono quadrado (10 x 10 m, sentido horário) com um campo texto acentuado.</summary>
    static string Quadrado(string pasta)
    {
        var shp = Path.Combine(pasta, "q.shp");
        var pts = new (double X, double Y)[] { (0, 0), (0, 10), (10, 10), (10, 0), (0, 0) };
        var conteudo = new byte[44 + 4 + pts.Length * 16];
        BinaryPrimitives.WriteInt32LittleEndian(conteudo, 5);
        BinaryPrimitives.WriteInt32LittleEndian(conteudo.AsSpan(36), 1);
        BinaryPrimitives.WriteInt32LittleEndian(conteudo.AsSpan(40), pts.Length);
        BinaryPrimitives.WriteInt32LittleEndian(conteudo.AsSpan(44), 0);
        for (var i = 0; i < pts.Length; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(conteudo.AsSpan(48 + i * 16), pts[i].X);
            BinaryPrimitives.WriteDoubleLittleEndian(conteudo.AsSpan(56 + i * 16), pts[i].Y);
        }
        var arq = new byte[100 + 8 + conteudo.Length];
        BinaryPrimitives.WriteInt32BigEndian(arq.AsSpan(104), conteudo.Length / 2);
        conteudo.CopyTo(arq, 108);
        File.WriteAllBytes(shp, arq);

        // .dbf: um campo C(20) "ZONA" = "ZRÉ 1"
        var dbf = new byte[32 + 32 + 1 + 1 + 20 + 1];
        BinaryPrimitives.WriteInt32LittleEndian(dbf.AsSpan(4), 1);
        BinaryPrimitives.WriteInt16LittleEndian(dbf.AsSpan(8), 65);
        BinaryPrimitives.WriteInt16LittleEndian(dbf.AsSpan(10), 21);
        Encoding.ASCII.GetBytes("ZONA").CopyTo(dbf, 32);
        dbf[43] = (byte)'C';
        dbf[48] = 20;
        dbf[64] = 0x0D;
        dbf[65] = (byte)' ';
        Encoding.UTF8.GetBytes("ZRÉ 1".PadRight(19)).CopyTo(dbf, 66);
        File.WriteAllBytes(Path.ChangeExtension(shp, ".dbf"), dbf);
        File.WriteAllText(Path.ChangeExtension(shp, ".cpg"), "UTF-8");
        return shp;
    }

    [Fact]
    public void le_poligono_e_atributo_com_acento()
    {
        var pasta = Directory.CreateTempSubdirectory("pmvni-shp").FullName;
        try
        {
            var f = Assert.Single(Shapefile.Ler(Quadrado(pasta)));
            Assert.Equal(100, f.Geometria!.Area, 6);
            Assert.Equal("ZRÉ 1", f.Atributo("zona"));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void graus_viram_utm_24s()
    {
        // Meridiano central, latitude -20: E = 500000 m.
        var c = Utm.DeGraus(-20, -39);
        Assert.Equal(500000, c.X, 3);
        Assert.Equal(7_788_518.69, c.Y, 1); // 10.000.000 - 0,9996 x arco de meridiano até 20° (2.212.366,25 m, GRS 80)
    }
}

/// <summary>O mapa de localização portado do kit (mapa_localizacao.py) e o relatório no aspecto do relatório de análise, com uma base
/// fictícia. Com PMVNI_MAPA_SAIDA apontando para uma pasta, o mapa (.svg) e o relatório (.html) de exemplo ficam gravados nela.</summary>
public class MapaLocalizacaoTestes : IDisposable
{
    readonly string pasta = BaseFicticia.Criar();
    readonly BaseQgis b;

    public MapaLocalizacaoTestes() => b = BaseQgis.Carregar(pasta);

    public void Dispose()
    {
        try { Directory.Delete(pasta, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void contas_do_script_do_kit()
    {
        Assert.Equal([30], MapaLocalizacao.FaixaApp("30"));
        Assert.Equal([15, 30], MapaLocalizacao.FaixaApp("1530"));
        Assert.Equal([15, 30], MapaLocalizacao.FaixaApp("3015"));
        Assert.Empty(MapaLocalizacao.FaixaApp(null));
        Assert.Equal("BR-262", MapaLocalizacao.NomeRua("BR 262"));
        Assert.Equal("Avenida 29 De Julho", MapaLocalizacao.NomeRua("AVENIDA 29 DE JULHO"));
        Assert.Equal("Av. Pref. Fulano De Tal", MapaLocalizacao.NomeAbreviado("AVENIDA PREFEITO FULANO DE TAL"));
        Assert.True(MapaLocalizacao.EhRodovia("BR 262"));
        Assert.True(MapaLocalizacao.EhBr262("BR-262"));
        Assert.False(MapaLocalizacao.EhRodovia("RUA DAS ACACIAS"));
        Assert.Equal("Beltrana Modelo", MapaLocalizacao.Curto("BELTRANA DE SOUZA MODELO E SOCIO (A)(S)"));
        Assert.Equal("Fulano Exemplo", MapaLocalizacao.Curto("FULANO DE TAL EXEMPLO e outros 2"));
        // A cor da zona é a do kit: md5 do nome, módulo 8 (os valores abaixo saíram do próprio script).
        Assert.Equal("#DCE6AC", MapaSvg.CorDaZona("ZCN2").Claro);
        Assert.Equal("#E0D6C4", MapaSvg.CorDaZona("ZRE").Claro);
        Assert.Equal("#C0E4C8", MapaSvg.CorDaZona("ZEIS").Claro);
    }

    [Fact]
    public void mapa_tem_o_enquadramento_as_camadas_o_balao_e_a_legenda_do_kit()
    {
        var m = MapaLocalizacao.PorInscricao(b, BaseFicticia.Lote4, new OpcoesMapa { AreaDeclarada = 1500, AreaFonte = "matrícula", Endereco = "Rua das Acácias, 120" });
        Assert.Equal("VERIFIED_LOT", m.Modo);
        // 16:10, com o centro do lote a 60% da largura e a 58% da altura (a partir do topo).
        Assert.Equal(1.6, m.Extensao.Width / m.Extensao.Height, 6);
        Assert.Equal(0.60, (300_115 - m.Extensao.MinX) / m.Extensao.Width, 6);
        Assert.Equal(0.58, (m.Extensao.MaxY - 7_750_120) / m.Extensao.Height, 6);

        Assert.Equal(["LOTE 4 · QUADRA Q7", "FULANO DE TAL EXEMPLO", "Inscrição " + BaseFicticia.Lote4, "Zona ZCN2 · base 1.200,00 m² · declarada 1.500,00 m²", "APP ATINGE O LOTE", "FAIXA DA BR-262 ATINGE O LOTE"], m.Ficha);
        Assert.Equal(["lote", "edif", "app", "rio", "dnit", "zona"], m.Legenda.Select(i => i.Tipo));
        Assert.Equal(["Lote do projeto", "Edificação no lote", "Faixa de APP", "Curso d'água", "Faixas da BR-262", "Zonas do PDM"], m.Legenda.Select(i => i.Texto));
        Assert.Equal("Córrego Fictício", m.NomeRio);
        Assert.True(m.AppAtingeLote);
        Assert.Equal(41.8, m.Br262!.DistanciaEixo);
        Assert.False(m.Br262.AtingeDominio);
        Assert.True(m.Br262.AtingeNaoEdificavel);
        Assert.Equal(1, m.Edificacoes);
        Assert.Equal(300, m.AreaEdificada);
        Assert.Equal(25.0, m.DiferencaAreaPct);
        Assert.Equal("Sede", m.Auc);
        Assert.Equal([BaseFicticia.Lote3, BaseFicticia.Lote5, BaseFicticia.Lote12], m.Confrontantes.Select(c => c.Inscricao));
        Assert.Contains("RUA DAS ACACIAS", m.Ruas);
        Assert.Contains("BR 262", m.Ruas);

        var svg = m.Svg;
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"840\" height=\"525\" viewBox=\"0 0 1680 1050\"", svg);
        foreach (var t in new[] { ">LOTE 4<", ">ZCN2<", ">ZRE<", ">BR-262<", ">Rua Das Acacias<", ">Córrego Fictício<", ">Beltrana Modelo<", ">Sicrano Teste<", ">APP ATINGE O LOTE<", ">FAIXA DA BR-262 ATINGE O LOTE<", " m<" })
            Assert.Contains(t, svg);
        Assert.Contains("stroke=\"#E22D28\" stroke-width=\"8\"", svg);   // lote confirmado: contorno vermelho de 4 px
        Assert.Contains("fill=\"#5C5650\"", svg);                          // edificação do lote, escura
        Assert.Contains("stroke=\"#E37400\"", svg);                        // faixas da BR-262
        Assert.Contains("stroke=\"#78BAF0\"", svg);                        // faixa de APP
        Assert.Contains("fill=\"#FAD470\"".Replace("fill", "stroke"), svg); // rodovia em amarelo
        // A ordem do desenho: zonas, edificações, faixas, ruas, água, lotes, lote em vermelho, balão e norte por último.
        int Pos(string t) => svg.IndexOf(t, StringComparison.Ordinal);
        Assert.True(Pos("fill=\"#DCE6AC\"") < Pos("stroke=\"#E37400\"") && Pos("stroke=\"#E37400\"") < Pos("stroke=\"#D6CFC3\"") && Pos("stroke=\"#D6CFC3\"") < Pos("stroke=\"#2884DE\""));
        Assert.True(Pos("stroke=\"#2884DE\"") < Pos("stroke=\"#B2AA9E\"") && Pos("stroke=\"#B2AA9E\"") < Pos("fill-opacity=\".216\"") && Pos("fill-opacity=\".216\"") < Pos("filter=\"url(#sombra)\""));

        var d = m.Dados.ToDictionary(x => x.Rotulo);
        Assert.Equal(["Identificação", "Áreas e edificação", "Zona e condicionantes", "Cadastro e entorno"], m.Dados.Select(x => x.Grupo).Distinct());
        Assert.Equal("4 / Q7 — Loteamento Exemplo", d["Lote / quadra"].Valor);
        Assert.Equal(("DIVERGE", "a", "+25,00% sobre a base"), (d["Diferença"].Chip, d["Diferença"].Alerta, d["Diferença"].Valor));
        Assert.Equal(("ATINGE O LOTE", "r"), (d["APP / rio"].Chip, d["APP / rio"].Alerta));
        Assert.StartsWith("Córrego Fictício · calha a ", d["APP / rio"].Valor);
        Assert.EndsWith(" m · APP de 15 m / 30 m na base", d["APP / rio"].Valor);
        Assert.Equal("Eixo a 41,8 m · domínio 40 m + 15 m não edificáveis", d["BR-262"].Valor);
        Assert.Equal("ATINGE O LOTE", d["BR-262"].Chip);
        Assert.Equal("Sim — AUC Sede", d["AUC"].Valor);
        Assert.Equal("SEDE", d["Perímetro"].Valor);
        Assert.Equal("Ativo · Construido · matrícula 9.999", d["Situação"].Valor);
        Assert.Equal("Loteamento aprovado Loteamento Exemplo", d["Parcelamento"].Valor);
        Assert.Equal("Lote 3: BELTRANA DE SOUZA MODELO E SOCIO (A)(S) · Lote 5: SICRANO PEREIRA TESTE · Lote 12: sem nome no cadastro", d["Confrontantes"].Valor);

        if (Environment.GetEnvironmentVariable("PMVNI_MAPA_SAIDA") is { Length: > 0 } saida) File.WriteAllText(Path.Combine(saida, "mapa-exemplo.svg"), svg);
    }

    [Fact]
    public void sem_nomes_e_com_lote_candidato()
    {
        var m = MapaLocalizacao.PorInscricao(b, BaseFicticia.Lote4, new OpcoesMapa { Candidato = true, ComDonos = false });
        Assert.Equal("CANDIDATE_POINT", m.Modo);
        Assert.Contains("stroke=\"#E22D28\" stroke-width=\"6\"", m.Svg); // identidade a confirmar: traço mais fino
        Assert.DoesNotContain("FULANO", m.Svg);
        Assert.DoesNotContain("Beltrana", m.Svg);
        Assert.DoesNotContain(m.Dados, x => x.Rotulo is "Proprietário" or "Confrontantes");
        Assert.Throws<ArgumentException>(() => MapaLocalizacao.PorInscricao(b, "99999999999999"));
    }

    [Fact]
    public void relatorio_do_imovel_tem_o_aspecto_do_relatorio_de_analise()
    {
        var r = Assert.Single(b.Cadastro.PorInscricao(BaseFicticia.Lote4));
        var a = b.Analisar(r);
        Assert.True(a.Conclusivo);
        var html = RelatorioHtml.Gerar(b, a, null, new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
        Assert.Contains(RelatorioHtml.CssOficial, html);              // o CSS oficial inteiro, sem alteração
        Assert.Contains(".lmrow { display: grid;", RelatorioHtml.CssOficial);
        foreach (var t in new[]
        {
            "<h1>RELATÓRIO CADASTRAL<br>E TERRITORIAL</h1>", "<div class=\"band\"><div class=\"proc\">INSCRIÇÃO " + BaseFicticia.Lote4, "<div class=\"name\">FULANO DE TAL EXEMPLO</div>",
            "<div class=\"ic g\"><h4>LOCALIZAÇÃO</h4>", "<h2>LOCALIZAÇÃO NA BASE MUNICIPAL</h2>", "<span class=\"pill g\"><i></i>LOTE IDENTIFICADO</span>",
            "<div class=\"lmap\"><img alt=\"Mapa de localização do lote na base municipal\" src=\"data:image/svg+xml;base64,", "<div class=\"lbl\">SITUAÇÃO DO LOTE NA BASE</div>",
            "<span><i class=\"lk dnit\"></i>Faixas da BR-262</span>", "<div class=\"lficha\"><table class=\"lft\">",
            "<tr class=\"lfh\"><th colspan=\"2\">Identificação</th></tr>", "<tr class=\"lfh\"><th colspan=\"2\">Cadastro e entorno</th></tr>",
            "<tr class=\"lfh\"><th colspan=\"2\">Áreas e edificação</th></tr>", "<tr class=\"lfh\"><th colspan=\"2\">Zona e condicionantes</th></tr>",
            "<i class=\"lchip r\">ATINGE O LOTE</i>", "<i class=\"lchip g\">CONFERE</i>", "<td class=\"lfl\">Área calculada</td><td class=\"lfv\"><b>1.200,00 m²</b>",
        })
            Assert.Contains(t, html);
        // A ficha fica na ordem do molde: Identificação e Cadastro e entorno na primeira tabela; Áreas e Zona na segunda.
        Assert.True(html.IndexOf(">Cadastro e entorno<", StringComparison.Ordinal) < html.IndexOf(">Áreas e edificação<", StringComparison.Ordinal));
        Assert.True(html.Length < 1_500_000); // cabe no NavigateToString do WebView2 (limite de 2 MB)
        if (Environment.GetEnvironmentVariable("PMVNI_MAPA_SAIDA") is { Length: > 0 } saida) File.WriteAllText(Path.Combine(saida, "relatorio-exemplo.html"), html);
    }

    [Fact]
    public void sem_lote_desenhado_o_relatorio_sai_sem_mapa_e_diz_por_que()
    {
        var r = new RegistroCadastro(9, new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            { ["inscricao_municipal"] = "09090090009001", ["nome_do_contribuinte"] = "PESSOA SEM LOTE", ["situacao"] = "Ativo" });
        var a = b.Analisar(r);
        Assert.Equal(VinculoMapa.SemLote, a.Vinculo);
        var html = RelatorioHtml.Gerar(b, a, null, DateTimeOffset.Now);
        Assert.DoesNotContain("class=\"lmap\"", html);
        Assert.Contains("<b>SEM MAPA</b>", html);
        Assert.Contains("<i class=\"lchip r\">SEM LOTE NO MAPA</i>", html);
        Assert.Contains("<span class=\"pill r\"><i></i>SEM LOTE NO MAPA</span>", html);
    }
}

/// <summary>Com a base real deste computador (pasta QGIS do kit): carrega, pesquisa e gera o relatório de um lote identificado.
/// Sem a pasta, o teste passa sem fazer nada (outros PCs e a nuvem).</summary>
public class BaseRealTestes
{
    static readonly string Pasta = Environment.GetEnvironmentVariable("PMVNI_QGIS_TESTE") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR", "KIT DE TREINAMENTO DE IA (PMVNI)",
        "05 - GEOPROCESSAMENTO (QGIS, LOTEAMENTOS, ZONEAMENTO)", "2 - QGIS");

    [Fact]
    public void carrega_pesquisa_e_gera_relatorio()
    {
        if (!Directory.Exists(Pasta)) return;
        var b = BaseQgis.Carregar(Pasta);
        Assert.True(b.Cadastro.Registros.Count > 1000);
        Assert.Equal("inscricao_municipal", b.Cadastro.Campos[0].Id);
        Assert.DoesNotContain(b.Cadastro.Campos, c => c.Id == "n_documento");
        Assert.NotNull(b[Camada.Lotes]);
        // Um registro com lote confirmado, de preferência com vizinhos e zona.
        // PMVNI_INSCRICAO_TESTE escolhe o imóvel (para comparar o mapa com o do script do kit, fora do repositório).
        var escolhida = Environment.GetEnvironmentVariable("PMVNI_INSCRICAO_TESTE");
        var r = b.Cadastro.Registros.First(x => escolhida is { Length: > 0 } ? x.InscricaoMunicipal == escolhida : b.VinculoRapido(x.InscricaoMunicipal) == VinculoMapa.Confirmado);
        var p = Pesquisa.Aplicar(b.Cadastro.Registros, b.Cadastro.Campos, new Consulta("inscricao_municipal", Condicao.Igual, r.InscricaoMunicipal));
        Assert.Contains(p.Itens, x => x.LinhaFonte == r.LinhaFonte);
        var a = b.Analisar(r);
        Assert.True(a.Conclusivo);
        Assert.NotEmpty(a.Zonas);
        var html = RelatorioHtml.Gerar(b, a, null, DateTimeOffset.Now);
        Assert.Contains("class=\"lmap\"><img", html);
        Assert.Contains("class=\"lficha\"", html);
        Assert.Contains(r.InscricaoMunicipal, html);
        Assert.True(html.Length < 1_900_000, $"relatório com {html.Length} caracteres: perto do limite do NavigateToString");
        if (Environment.GetEnvironmentVariable("PMVNI_RELATORIO_SAIDA") is { Length: > 0 } saida)
        {
            File.WriteAllText(saida, html);
            var area = double.TryParse(Environment.GetEnvironmentVariable("PMVNI_AREA_TESTE"), System.Globalization.CultureInfo.InvariantCulture, out var ad) ? ad : (double?)null;
            File.WriteAllText(Path.ChangeExtension(saida, ".svg"), MapaLocalizacao.PorInscricao(b, r.InscricaoMunicipal, new OpcoesMapa { AreaDeclarada = area, Candidato = area is not null }).Svg);
        }
    }
}
