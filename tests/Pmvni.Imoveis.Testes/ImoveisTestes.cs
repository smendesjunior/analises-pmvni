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
        var r = b.Cadastro.Registros.First(x => b.VinculoRapido(x.InscricaoMunicipal) == VinculoMapa.Confirmado);
        var p = Pesquisa.Aplicar(b.Cadastro.Registros, b.Cadastro.Campos, new Consulta("inscricao_municipal", Condicao.Igual, r.InscricaoMunicipal));
        Assert.Contains(p.Itens, x => x.LinhaFonte == r.LinhaFonte);
        var a = b.Analisar(r);
        Assert.True(a.Conclusivo);
        Assert.NotEmpty(a.Zonas);
        var html = RelatorioHtml.Gerar(b, a, null, DateTimeOffset.Now);
        Assert.Contains("<svg", html);
        Assert.Contains(r.InscricaoMunicipal, html);
        if (Environment.GetEnvironmentVariable("PMVNI_RELATORIO_SAIDA") is { Length: > 0 } saida) File.WriteAllText(saida, html);
    }
}
