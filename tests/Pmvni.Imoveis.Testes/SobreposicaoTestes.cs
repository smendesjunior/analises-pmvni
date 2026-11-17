using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Pmvni.Imoveis;
using Xunit;

namespace Pmvni.Imoveis.Testes;

/// <summary>Confere o SVG final do mapa (2026-11-17): nenhum texto (ou pílula de cota) cobre outro, e nenhum texto fica por cima da linha
/// de uma cota. Lê os elementos <c>&lt;text&gt;</c> com as medidas da Segoe UI (<see cref="Letras"/>), o giro (<c>rotate</c>, do próprio
/// texto ou do grupo) e, nas pílulas, o retângulo de fundo.</summary>
public static class VerificadorSvg
{
    static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    static double D(XElement e, string a) => double.Parse((string?)e.Attribute(a) ?? "0", CultureInfo.InvariantCulture);

    /// <summary>O giro (graus, sentido do SVG) e o centro do <c>rotate(...)</c> do elemento ou do grupo pai.</summary>
    static (double Ang, double Cx, double Cy) Giro(XElement e)
    {
        var t = (string?)e.Attribute("transform") ?? (e.Parent?.Name == Ns + "g" ? (string?)e.Parent.Attribute("transform") : null);
        if (t is null || !t.StartsWith("rotate(", StringComparison.Ordinal)) return (0, 0, 0);
        var p = t[7..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return (p[0], p.Length > 1 ? p[1] : 0, p.Length > 2 ? p[2] : 0);
    }

    static PontoTela[] Girar(double x0, double y0, double x1, double y1, (double Ang, double Cx, double Cy) g, double encolher)
    {
        x0 += encolher; y0 += encolher; x1 -= encolher; y1 -= encolher;
        var a = g.Ang * Math.PI / 180;
        double co = Math.Cos(a), si = Math.Sin(a);
        return new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }
            .Select(q => new PontoTela(g.Cx + (q.Item1 - g.Cx) * co - (q.Item2 - g.Cy) * si, g.Cy + (q.Item1 - g.Cx) * si + (q.Item2 - g.Cy) * co)).ToArray();
    }

    /// <summary>As sobreposições encontradas, em texto (vazia = nenhuma).</summary>
    public static List<string> Sobreposicoes(string svg)
    {
        var doc = XDocument.Parse(svg);
        var caixas = new List<(string Texto, PontoTela[] Poli, XElement? Grupo)>();
        foreach (var t in doc.Descendants(Ns + "text"))
        {
            var texto = t.Value;
            var g = Giro(t);
            var fundo = t.ElementsBeforeSelf(Ns + "rect").LastOrDefault();
            if (fundo is not null && t.Parent?.Name == Ns + "g")
            {
                double x = D(fundo, "x"), y = D(fundo, "y");
                caixas.Add((texto, Girar(x, y, x + D(fundo, "width"), y + D(fundo, "height"), g, 1), t.Parent.Parent?.Element(Ns + "title") is null ? t.Parent : t.Parent.Parent));
                continue;
            }
            var tam = D(t, "font-size");
            var (w, cima, baixo) = Letras.Medir(texto, tam, (string?)t.Attribute("font-weight") == "700");
            double tx = D(t, "x"), ty = D(t, "y");
            var x0 = (string?)t.Attribute("text-anchor") == "middle" ? tx - w / 2 : tx;
            caixas.Add((texto, Girar(x0, ty - cima, x0 + w, ty + baixo, g, 1), null));
        }
        var r = new List<string>();
        for (var i = 0; i < caixas.Count; i++)
            for (var j = i + 1; j < caixas.Count; j++)
                if (Rotulagem.Sobrepoe(caixas[i].Poli, caixas[j].Poli)) r.Add($"«{caixas[i].Texto}» × «{caixas[j].Texto}»");
        // as linhas das cotas (grupos com <title>): nenhum texto de fora do grupo por cima delas
        foreach (var grupo in doc.Descendants(Ns + "g").Where(g => g.Element(Ns + "title") is not null))
        {
            var d = (string?)grupo.Element(Ns + "path")?.Attribute("d") ?? "";
            var nums = d[1..].Split('M')[0].Split('L').Select(p => p.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
            if (nums.Length < 2) continue;
            PontoTela a = new(nums[0][0], nums[0][1]), c = new(nums[1][0], nums[1][1]);
            var comp = Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y));
            if (comp < 4) continue;
            var linha = Rotulagem.CaixaGirada(new PontoTela((a.X + c.X) / 2, (a.Y + c.Y) / 2), comp - 4, 1, Cotas.AnguloLegivel(a, c));
            foreach (var (texto, poli, dono) in caixas)
                if (dono != grupo && Rotulagem.Sobrepoe(linha, poli))
                    r.Add($"«{texto}» × linha da cota «{grupo.Descendants(Ns + "text").FirstOrDefault()?.Value}»");
        }
        return r;
    }
}

public class SobreposicaoTestes : IDisposable
{
    readonly string pasta = BaseFicticia.Criar();
    readonly BaseQgis b;

    public SobreposicaoTestes() => b = BaseQgis.Carregar(pasta);

    public void Dispose()
    {
        try { Directory.Delete(pasta, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void o_verificador_acha_textos_sobrepostos()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><text x=\"100\" y=\"100\" font-size=\"30\" text-anchor=\"middle\">Rua Um</text>"
            + "<text x=\"110\" y=\"105\" font-size=\"30\" text-anchor=\"middle\">Rua Dois</text><text x=\"400\" y=\"400\" font-size=\"30\">Longe</text></svg>";
        Assert.Equal(["«Rua Um» × «Rua Dois»"], VerificadorSvg.Sobreposicoes(svg));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void o_mapa_da_base_ficticia_nao_tem_texto_sobre_texto(bool cotas)
    {
        foreach (var ins in new[] { BaseFicticia.Lote4, BaseFicticia.Lote3, BaseFicticia.Lote5 })
        {
            var m = MapaLocalizacao.PorInscricao(b, ins, new OpcoesMapa { Cotas = cotas });
            Assert.Empty(VerificadorSvg.Sobreposicoes(m.Svg));
        }
    }

    [Fact]
    public void texto_utf8_lido_como_1252_e_consertado()
    {
        Assert.Equal("RUA JOÃO MINETE", Shapefile.Reparar("RUA JOÃƒO MINETE"));
        Assert.Equal("RUA ANGELO ALTOÉ", Shapefile.Reparar("RUA ANGELO ALTOÃ‰"));
        Assert.Equal("Rua Angelo Altoé", Shapefile.Reparar("Rua Angelo AltoÃ©"));
        // texto certo não muda
        foreach (var certo in new[] { "RUA JOÃO MINETE", "SÃO JOÃO", "Rua Angelo Altoé", "Â", "ÃO" })
            Assert.Equal(certo, Shapefile.Reparar(certo));
    }

    [Fact]
    public void a_codificacao_do_dbf_vem_do_conteudo_quando_o_cpg_falta_ou_erra()
    {
        var w1252 = Encoding.GetEncoding(1252);
        byte[][] utf8 = [Encoding.UTF8.GetBytes("RUA JOÃO MINETE"), Encoding.UTF8.GetBytes("RUA ANGELO ALTOÉ"), Encoding.ASCII.GetBytes("BR 262")];
        byte[][] latino = [w1252.GetBytes("RUA JOÃO MINETE"), w1252.GetBytes("VIÇOSA")];
        Assert.IsType<UTF8Encoding>(Shapefile.DetectarCodificacao(utf8, null));                 // sem .cpg, mas é UTF-8
        Assert.Equal(1252, Shapefile.DetectarCodificacao(latino, null).CodePage);              // sem .cpg: Windows-1252
        Assert.Equal(1252, Shapefile.DetectarCodificacao(latino, new UTF8Encoding(false)).CodePage); // o .cpg diz UTF-8 e não é
        Assert.Equal(28591, Shapefile.DetectarCodificacao(latino, Encoding.Latin1).CodePage);  // o .cpg certo vale
        // UTF-8 cortado no fim do campo (largura fixa) continua UTF-8
        var cortado = Encoding.UTF8.GetBytes("RUA JOÃO")[..^1];
        Assert.IsType<UTF8Encoding>(Shapefile.DetectarCodificacao([cortado, utf8[0]], null));
    }

    [Fact]
    public void dbf_utf8_sem_cpg_le_os_nomes_com_acento()
    {
        var dir = Directory.CreateTempSubdirectory("pmvni-dbf-");
        try
        {
            var dbf = Path.Combine(dir.FullName, "Logradouro.dbf");
            string[] valores = ["RUA JOÃO MINETE", "RUA ANGELO ALTOÉ", "RUA CATARINA ZANDONADE BUSATO"];
            const int tam = 40;
            var tamCab = 32 + 32 + 1;
            var tamReg = 1 + tam;
            var b = new byte[tamCab + valores.Length * tamReg];
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), valores.Length);
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(8), (short)tamCab);
            BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(10), (short)tamReg);
            Encoding.ASCII.GetBytes("NomeLogra").CopyTo(b, 32);
            b[32 + 11] = (byte)'C';
            b[32 + 16] = tam;
            b[tamCab - 1] = 0x0D;
            for (var i = 0; i < valores.Length; i++)
            {
                var o = tamCab + i * tamReg;
                b.AsSpan(o, tamReg).Fill((byte)' ');
                Encoding.UTF8.GetBytes(valores[i]).CopyTo(b, o + 1);
            }
            File.WriteAllBytes(dbf, b);
            Assert.Equal(valores, Shapefile.LerDbf(dbf, null).Select(d => d["NomeLogra"]));
            Assert.Equal("Rua João Minete", MapaLocalizacao.NomeRua("RUA JOÃO MINETE"));
        }
        finally { dir.Delete(true); }
    }
}
