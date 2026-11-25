using System.Collections;
using System.Numerics;
using Pmvni.Composicao;
using Pmvni.Composicao.Kit;
using Xunit;

namespace Pmvni.Composicao.Testes;

/// <summary>Cada função portada é comparada, byte a byte, com a saída do próprio kit (tests/referencias).</summary>
public class KitTestes
{
    private static IReadOnlyDictionary<string, object?> D(object? o) => Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(o);
    private static List<object?> L(object? o) => Assert.IsType<List<object?>>(o);

    private static List<(string, int)> Marcas(object? o) =>
        L(o).Select(m => (Assert.IsType<string>(L(m)[0]), (int)Assert.IsType<long>(L(m)[1]))).ToList();

    private static string ChaveDe(string arquivo, string prefixo) => arquivo[prefixo.Length..^".txt".Length];

    /// <summary>Normaliza para comparação: Seguro vira texto, números inteiros viram BigInteger.</summary>
    private static object? Norm(object? v) => v switch
    {
        Seguro s => s.Valor,
        long l => new BigInteger(l),
        int i => new BigInteger(i),
        IReadOnlyDictionary<string, object?> d => d.ToDictionary(p => p.Key, p => Norm(p.Value)),
        IEnumerable e and not string => e.Cast<object?>().Select(Norm).ToList(),
        _ => v,
    };

    private static void IgualProfundo(object? esperado, object? obtido, string caminho)
    {
        switch (esperado)
        {
            case Dictionary<string, object?> de:
                var dobt = Assert.IsType<Dictionary<string, object?>>(obtido);
                Assert.True(de.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(dobt.Keys.OrderBy(k => k, StringComparer.Ordinal)),
                    $"{caminho}: chaves [{string.Join(",", de.Keys)}] x [{string.Join(",", dobt.Keys)}]");
                foreach (var (k, v) in de) IgualProfundo(v, dobt[k], caminho + "." + k);
                break;
            case List<object?> le:
                var lobt = Assert.IsType<List<object?>>(obtido);
                Assert.True(le.Count == lobt.Count, $"{caminho}: tamanho {le.Count} x {lobt.Count}");
                for (var i = 0; i < le.Count; i++) IgualProfundo(le[i], lobt[i], $"{caminho}[{i}]");
                break;
            default:
                Assert.True(Equals(esperado, obtido), $"{caminho}: [{PythonTexto.Repr(esperado)}] x [{PythonTexto.Repr(obtido)}]");
                break;
        }
    }

    private static void Igual(object? esperado, object? obtido) => IgualProfundo(Norm(esperado), Norm(obtido), "$");

    // ---------- tabelas e situação ----------

    [Fact]
    public void conferencia_humana_preserva_as_classes_e_textos_das_demais_situacoes()
    {
        Assert.Equal(("ATENDE", "g"), Situacoes.Sit["ATENDE"]);
        Assert.Equal(("NÃO ATENDE", "r"), Situacoes.Sit["NAO_ATENDE"]);
        Assert.Equal(("FALTA COMPROVAR", "a"), Situacoes.Sit["NAO_COMPROVADO"]);
        Assert.Equal(("INFORMAÇÕES DIVERGENTES", "o"), Situacoes.Sit["DIVERGENCIA_INTERNA"]);
        Assert.Equal(Situacoes.Sit["DIVERGENCIA_INTERNA"], Situacoes.Sit["DIVERGENCIA"]);
        Assert.Equal(("O ANALISTA DEVE CONFERIR", "conferir"), Situacoes.Sit["DECISAO_DO_ANALISTA"]);
    }

    [Fact]
    public void tabelas_iguais_as_do_kit()
    {
        var t = D(Referencias.Json("tabelas.json"));
        var sit = D(t["SIT"]);
        Assert.Equal(sit.Keys, Situacoes.Sit.Keys);
        foreach (var (k, v) in sit)
        {
            Assert.Equal(Assert.IsType<string>(L(v)[0]), Situacoes.Sit[k].Texto);
            Assert.Equal(Assert.IsType<string>(L(v)[1]), Situacoes.Sit[k].Cor);
        }
        Assert.Equal(L(t["SIT_LOCAL"]).Cast<string>().Order(StringComparer.Ordinal), Situacoes.SitLocal.Order(StringComparer.Ordinal));
        Assert.Equal(L(t["SIT_ITEM"]).Cast<string>().Order(StringComparer.Ordinal), Situacoes.SitItem.Order(StringComparer.Ordinal));
        Assert.Equal(L(t["SIT_INDICE"]).Cast<string>().Order(StringComparer.Ordinal), Situacoes.SitIndice.Order(StringComparer.Ordinal));
        Igual(t["LEGENDA_PADRAO"], Situacoes.LegendaPadrao);
        Assert.Equal(D(t["POS_PLANTA"]).Keys, Diagrama.PosPlanta.Keys);
        foreach (var (k, v) in D(t["POS_PLANTA"])) Assert.Equal(((long)L(v)[0]!, (long)L(v)[1]!), ((long)Diagrama.PosPlanta[k].X, (long)Diagrama.PosPlanta[k].Y));
        Assert.Equal(D(t["POS_VOLUME"]).Keys, Diagrama.PosVolume.Keys);
        foreach (var (k, v) in D(t["POS_VOLUME"])) Assert.Equal(((long)L(v)[0]!, (long)L(v)[1]!), ((long)Diagrama.PosVolume[k].X, (long)Diagrama.PosVolume[k].Y));
    }

    [Fact]
    public void pill_igual_ao_kit_para_cada_situacao()
    {
        var arqs = Referencias.Nomes("pill_", ".txt").Where(n => n != "pill_erro.txt").ToList();
        Assert.Equal(Situacoes.Sit.Count, arqs.Count);
        foreach (var a in arqs) Assert.Equal(Referencias.Texto(a), Situacoes.Pill(ChaveDe(a, "pill_")));
    }

    [Fact]
    public void cor_igual_ao_kit_para_cada_situacao()
    {
        var arqs = Referencias.Nomes("cor_", ".txt").Where(n => !n.StartsWith("cor_erro", StringComparison.Ordinal)).ToList();
        Assert.Equal(Situacoes.Sit.Count, arqs.Count);
        foreach (var a in arqs) Assert.Equal(Referencias.Texto(a), Situacoes.Cor(ChaveDe(a, "cor_")));
    }

    [Fact]
    public void erros_de_situacao_iguais_ao_kit()
    {
        Assert.Equal(Referencias.Texto("pill_erro.txt"), "ERRO: " + Assert.Throws<MoldeErro>(() => Situacoes.Pill("XX")).Message);
        Assert.Equal(Referencias.Texto("cor_erro.txt"), "ERRO: " + Assert.Throws<MoldeErro>(() => Situacoes.Cor("XX")).Message);
        Assert.Equal(Referencias.Texto("cor_erro_nulo.txt"), "ERRO: " + Assert.Throws<MoldeErro>(() => Situacoes.Cor(null)).Message);
    }

    [Fact]
    public void icone_igual_ao_kit_para_cada_icone()
    {
        var arqs = Referencias.Nomes("icone_", ".txt").Where(n => n != "icone_erro.txt").ToList();
        Assert.Equal(12, arqs.Count);
        foreach (var a in arqs) Assert.Equal(Referencias.Texto(a), Icones.Icone(ChaveDe(a, "icone_")));
        Assert.Equal(Referencias.Texto("icone_erro.txt"), "ERRO: " + Assert.Throws<MoldeErro>(() => Icones.Icone("zz")).Message);
    }

    [Fact]
    public void filtros_do_kit_funcionam_no_molde()
    {
        var m = Molde.Compilar("{{ s|pill }}|{{ s|cor }}|{{ i|icone }}|{{ n|n2 }}");
        var ctx = new Dictionary<string, object?> { ["s"] = "ATENDE", ["i"] = "pin", ["n"] = 3L };
        Assert.Equal(Situacoes.Pill("ATENDE") + "|g|" + Icones.Icone("pin") + "|03", m.Renderizar(ctx, Filtros.Todos));
        Assert.Throws<MoldeErro>(() => Molde.Compilar("{{ s|cor }}").Renderizar(new Dictionary<string, object?> { ["s"] = "ZZ" }, Filtros.Todos));
    }

    // ---------- cabeçalho ----------

    [Fact]
    public void cabecalho_igual_ao_kit()
    {
        var brasao = ModeloOficial.Carregar().Brasao;
        Assert.Equal(Referencias.Texto("cabecalho_padrao.txt"), Cabecalho.CabecalhoUri(Cabecalho.OrgaoPadrao, brasao));
        Assert.Equal(Referencias.Texto("cabecalho_orgao.txt"), Cabecalho.CabecalhoUri("SECRETARIA <TESTE> & \"CIA\" D'ÓRGÃOS", brasao));
    }

    // ---------- diagrama ----------

    [Fact]
    public void planta_svg_igual_ao_kit()
    {
        var arqs = Referencias.Nomes("planta_", ".txt").ToList();
        Assert.Equal(4, arqs.Count);
        foreach (var a in arqs)
        {
            var ent = D(Referencias.Json(a[..^".txt".Length] + ".entrada.json"));
            Assert.Equal(Referencias.Texto(a), Diagrama.PlantaSvg(D(ent["p"]), Marcas(ent["marcas"])));
        }
    }

    [Fact]
    public void volumetria_svg_igual_ao_kit()
    {
        var arqs = Referencias.Nomes("volumetria_", ".txt").ToList();
        Assert.Equal(4, arqs.Count);
        foreach (var a in arqs)
        {
            var ent = D(Referencias.Json(a[..^".txt".Length] + ".entrada.json"));
            Assert.Equal(Referencias.Texto(a), Diagrama.VolumetriaSvg(D(ent["v"]), Marcas(ent["marcas"])));
        }
    }

    [Fact]
    public void montar_diagrama_igual_ao_kit()
    {
        var ent = D(Referencias.Json("diagrama_ok.entrada.json"));
        var obtido = Diagrama.MontarDiagrama(ent);
        Igual(Referencias.Json("diagrama_ok.json"), obtido);
        Assert.IsType<Seguro>(obtido["planta_svg"]);
        Assert.IsType<Seguro>(obtido["volumetria_svg"]);
    }

    [Fact]
    public void montar_diagrama_erros_iguais_ao_kit()
    {
        foreach (var caso in new[] { "diagrama_erro_ancora", "diagrama_erro_situacao" })
        {
            var ent = D(Referencias.Json(caso + ".entrada.json"));
            Assert.Equal(Referencias.Texto(caso + ".txt"), "ERRO: " + Assert.Throws<MoldeErro>(() => Diagrama.MontarDiagrama(ent)).Message);
        }
    }

    // ---------- resumo e conferência ----------

    [Fact]
    public void resumo_igual_ao_kit()
    {
        var arqs = Referencias.Nomes("resumo_", ".txt").ToList();
        Assert.Equal(4, arqs.Count);
        foreach (var a in arqs)
            Assert.Equal(Referencias.Texto(a), Conferencia.Resumo(L(Referencias.Json(a[..^".txt".Length] + ".entrada.json"))));
    }

    [Fact]
    public void conferir_mensagens_iguais_ao_kit_na_mesma_ordem()
    {
        var arqs = Referencias.Nomes("conferir_", ".json").ToList();
        Assert.True(arqs.Count >= 10, "casos de conferir: " + arqs.Count);
        foreach (var a in arqs)
        {
            var caso = D(Referencias.Json(a));
            var imgs = L(caso["imagens"]).Cast<string>().ToHashSet(StringComparer.Ordinal);
            var esperado = L(caso["erros"]).Cast<string>().ToList();
            var obtido = Conferencia.Conferir(D(caso["dados"]), imgs.Contains, Assert.IsType<bool>(caso["rascunho"]));
            Assert.True(esperado.SequenceEqual(obtido), $"{a}:\nesperado:\n{string.Join("\n", esperado)}\nobtido:\n{string.Join("\n", obtido)}");
        }
    }

    [Fact]
    public void conferir_valido_nao_tem_erros()
    {
        var caso = D(Referencias.Json("conferir_valido.json"));
        Assert.Empty(Conferencia.Conferir(D(caso["dados"]), n => n is "fig1.png" or "mapa.png", false));
    }

    // ---------- localização ----------

    [Fact]
    public void localizacao_grupos_colunas_e_legenda_iguais_ao_kit()
    {
        var arqs = Referencias.Nomes("localizacao_colunas_", ".json").ToList();
        Assert.True(arqs.Count >= 7, "casos de localização: " + arqs.Count);
        foreach (var a in arqs)
        {
            var caso = D(Referencias.Json(a));
            var r = Localizacao.Agrupar(D(caso["entrada"]));
            Igual(caso["grupos"], r["grupos"]);
            Igual(caso["colunas"], r["colunas"]);
            Igual(caso["legenda"], r["legenda"]);
        }
    }
}
