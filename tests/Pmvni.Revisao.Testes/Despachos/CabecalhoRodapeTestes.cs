using System.Globalization;
using System.Xml.Linq;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>Só roda com <c>PMVNI_TESTES_LIBREOFFICE=1</c> (o LibreOffice real não entra na rodada comum do <c>dotnet test</c>) e com o
/// LibreOffice instalado; senão aparece como ignorado.</summary>
public sealed class FatoComLibreOfficeAttribute : FactAttribute
{
    public FatoComLibreOfficeAttribute()
    {
        if (Environment.GetEnvironmentVariable("PMVNI_TESTES_LIBREOFFICE") != "1") Skip = "Defina PMVNI_TESTES_LIBREOFFICE=1 para converter com o LibreOffice real.";
        else if (ConversorLibreOffice.Localizar() is null) Skip = "LibreOffice não encontrado neste computador.";
    }
}

/// <summary>O corpo do despacho nunca entra na faixa do cabeçalho nem no rodapé (defeito de 2026-11-16: no modelo do kit a margem de cima,
/// 2,5 cm, é menor que o fim da faixa do cabeçalho, 2,82 cm; da 2ª folha em diante a primeira linha saía por cima do brasão — medido no PDF
/// do LibreOffice: faixa de 17,0 a 80,0 pt e primeira linha de 70,8 a 85,4 pt). A correção é só na cópia gerada: o modelo do kit não muda.</summary>
public sealed class CabecalhoRodapeTestes(ITestOutputHelper saida)
{
    static readonly XNamespace W = PreenchimentoDocxTestes.W;

    static XElement Margens(byte[] docx) =>
        PreenchimentoDocxTestes.Documento(docx).Root!.Element(W + "body")!.Elements(W + "sectPr").Last().Element(W + "pgMar")!;

    [Theory]
    [InlineData("Correcao")]
    [InlineData("Aprovacao")]
    public void no_modelo_do_kit_a_faixa_do_cabecalho_passa_da_margem_de_cima(string tipo)
    {
        var l = LayoutDespacho.Ler(PreenchimentoDocxTestes.Modelo(Enum.Parse<TipoDespacho>(tipo)));

        // A causa do defeito, medida no modelo: a faixa (imagem flutuante, não empurra o texto) termina abaixo da margem de cima.
        Assert.Equal(80.0, l.FimDoCabecalhoPt, 0);
        Assert.True(l.MargemTopoPt < l.FimDoCabecalhoPt);
        // A tarja lateral (fora da coluna de texto) não conta.
        Assert.True(l.FimDoCabecalhoPt < 100);

        // O corpo começa abaixo da faixa, com folga; a 1ª folha é compensada no parágrafo vazio que abre o corpo.
        Assert.True(l.TopoDoCorpoPt >= l.FimDoCabecalhoPt + LayoutDespacho.FolgaCabecalhoPt);
        Assert.True(l.TopoDoCorpoPt < l.FimDoCabecalhoPt + LayoutDespacho.FolgaCabecalhoPt + 0.06, "arredondado para cima, em twips");
        Assert.True(l.PrimeiroParagrafoVazio);
        Assert.Equal(l.TopoDoCorpoPt - l.MargemTopoPt, l.CompensacaoInicialPt, 6);
        Assert.Equal(l.Formatos[0].DepoisPt - l.CompensacaoInicialPt, l.Formato(0).DepoisPt, 6);
        Assert.Equal(l.AlturaPt - l.TopoDoCorpoPt - l.MargemBaixoPt, l.AlturaUtilPt, 6);

        // Rodapé: o corpo termina antes da linha vermelha e do endereço.
        var fimDoCorpo = l.AlturaPt - l.MargemBaixoPt;
        Assert.All(l.Linhas, x => Assert.True(Math.Min(x.Y1Pt, x.Y2Pt) > fimDoCorpo));
        Assert.All(l.Rodape.Where(r => r.Trechos.Count > 0), r => Assert.True(r.TopoPt > fimDoCorpo));
    }

    [Theory]
    [InlineData("Correcao", 2)]
    [InlineData("Correcao", 60)]
    [InlineData("Aprovacao", 0)]
    public void o_docx_gerado_comeca_o_corpo_abaixo_da_faixa_e_a_primeira_folha_fica_igual(string tipo, int exigencias)
    {
        var t = Enum.Parse<TipoDespacho>(tipo);
        var modelo = PreenchimentoDocxTestes.Modelo(t);
        var d = PreenchimentoDocxTestes.Final(t, exigencias);

        var docx = PreenchimentoDocx.Preencher(modelo, d);

        var antes = LayoutDespacho.Ler(modelo);
        var depois = LayoutDespacho.Ler(docx);
        // Em todas as folhas o corpo começa abaixo da faixa do cabeçalho (a margem de cima da seção é o começo do corpo).
        Assert.True(depois.MargemTopoPt >= depois.FimDoCabecalhoPt + LayoutDespacho.FolgaCabecalhoPt);
        Assert.Equal(antes.TopoDoCorpoPt, depois.MargemTopoPt, 6);
        Assert.Equal(depois.MargemTopoPt, depois.TopoDoCorpoPt, 6); // já corrigido: nada mais a afastar
        Assert.Equal(0, depois.CompensacaoInicialPt, 6);
        // A 1ª folha fica como no modelo: o que a margem ganhou, o parágrafo vazio de abertura perdeu.
        Assert.Equal(antes.MargemTopoPt + antes.Formatos[0].DepoisPt, depois.MargemTopoPt + depois.Formatos[0].DepoisPt, 6);
        // A prévia (desenhada a partir do modelo) e o DOCX gerado usam as mesmas medidas.
        Assert.Equal(antes.Formato(0).DepoisPt, depois.Formatos[0].DepoisPt, 6);
        Assert.Equal(antes.AlturaUtilPt, depois.AlturaUtilPt, 6);
        // Só a margem de cima muda na seção; o rodapé e as outras margens ficam como no modelo.
        var (a, b) = (Margens(modelo), Margens(docx));
        Assert.Equal(a.Attributes().Select(x => x.Name), b.Attributes().Select(x => x.Name));
        Assert.All(a.Attributes().Where(x => x.Name != W + "top"), x => Assert.Equal(x.Value, b.Attribute(x.Name)!.Value));
        Assert.Equal(((int)Math.Round(antes.TopoDoCorpoPt * 20)).ToString(CultureInfo.InvariantCulture), b.Attribute(W + "top")!.Value);
        Assert.Equal(antes.MargemBaixoPt, depois.MargemBaixoPt);

        // O fecho (data, linha, nome, cargo, CAU e matrícula) não se separa entre folhas: "manter com o próximo" da data ao penúltimo.
        var ps = PreenchimentoDocxTestes.Paragrafos(PreenchimentoDocxTestes.Documento(docx));
        var data = ps.FindIndex(p => PreenchimentoDocxTestes.Texto(p).Contains(d.DataPorExtenso, StringComparison.Ordinal));
        Assert.True(data > 0);
        Assert.Contains(ps.Skip(data), p => PreenchimentoDocxTestes.Texto(p).StartsWith("MATRÍCULA", StringComparison.Ordinal));
        Assert.All(ps.Skip(data).SkipLast(1), p => Assert.NotNull(p.Element(W + "pPr")?.Element(W + "keepNext")));

        // A validação do editor aceita esta correção, e só ela.
        Assert.Empty(ValidacaoDespacho.Comparar(modelo, docx));
        Assert.Empty(ValidacaoDespacho.Conteudo(modelo, docx, d));
    }

    [Fact]
    public void outra_margem_de_cima_continua_sendo_erro()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Correcao);
        var docx = PreenchimentoDocx.Preencher(modelo, PreenchimentoDocxTestes.Final(TipoDespacho.Correcao));
        var corrigida = Margens(docx).Attribute(W + "top")!.Value;
        var outra = (int.Parse(corrigida, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);

        var mexido = ValidacaoDespachoTestes.TrocarXml(docx, "w:top=\"" + corrigida + "\"", "w:top=\"" + outra + "\"");

        Assert.Equal(new[] { "Seções, margens, formato, orientação ou vínculos de cabeçalho/rodapé alterados." }, ValidacaoDespacho.Comparar(modelo, mexido));
    }

    // ---------- no PDF do LibreOffice real ----------

    sealed record Palavra(string Texto, double Topo, double Baixo, double Esquerda);

    sealed record PaginaMedida(double FimDaFaixa, IReadOnlyList<Palavra> Corpo, IReadOnlyList<Palavra> Rodape);

    /// <summary>As palavras de cada página, com as medidas a partir do alto (em pontos): a faixa do cabeçalho é a imagem da largura da página;
    /// o rodapé é o que começa abaixo da margem de baixo do modelo.</summary>
    static List<PaginaMedida> Medir(byte[] pdf, LayoutDespacho l)
    {
        var paginas = new List<PaginaMedida>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var pg in doc.GetPages())
        {
            var h = pg.Height;
            var faixa = pg.GetImages().Where(i => i.Bounds.Width > pg.Width * 0.8).Select(i => h - i.Bounds.Bottom).DefaultIfEmpty(0).Max();
            var palavras = pg.GetWords().Where(w => w.Text.Trim().Length > 0)
                .Select(w => new Palavra(w.Text, h - w.BoundingBox.Top, h - w.BoundingBox.Bottom, w.BoundingBox.Left)).ToList();
            var limite = l.AlturaPt - l.MargemBaixoPt;
            paginas.Add(new PaginaMedida(faixa, palavras.Where(p => p.Topo < limite).OrderBy(p => p.Topo).ThenBy(p => p.Esquerda).ToList(),
                palavras.Where(p => p.Topo >= limite).OrderBy(p => p.Topo).ThenBy(p => p.Esquerda).ToList()));
        }
        return paginas;
    }

    void Conferir(string caso, List<PaginaMedida> paginas, LayoutDespacho l)
    {
        for (var i = 0; i < paginas.Count; i++)
        {
            var p = paginas[i];
            var topo = p.Corpo.Count == 0 ? double.NaN : p.Corpo.Min(x => x.Topo);
            var baixo = p.Corpo.Count == 0 ? double.NaN : p.Corpo.Max(x => x.Baixo);
            var rodape = p.Rodape.Count == 0 ? double.NaN : p.Rodape.Min(x => x.Topo);
            saida.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{caso} página {i + 1}/{paginas.Count}: faixa do cabeçalho até {p.FimDaFaixa:0.0} pt; corpo de {topo:0.0} a {baixo:0.0} pt; rodapé a partir de {rodape:0.0} pt"));
            Assert.InRange(p.FimDaFaixa, 75, 85); // a faixa está em toda página, onde o modelo a põe
            Assert.NotEmpty(p.Corpo);
            // Cabeçalho: nenhuma tinta do corpo encosta na faixa.
            Assert.True(topo >= p.FimDaFaixa + 2, $"{caso}, página {i + 1}: o corpo começa em {topo:0.0} pt, dentro da faixa do cabeçalho (até {p.FimDaFaixa:0.0} pt)");
            // Rodapé: o corpo termina na área do corpo, e abaixo dela só há o rodapé do modelo (que começa pelo nome da Prefeitura).
            Assert.True(baixo <= l.AlturaPt - l.MargemBaixoPt + 0.5, $"{caso}, página {i + 1}: o corpo desce até {baixo:0.0} pt, dentro do rodapé");
            Assert.True(p.Rodape.Count >= 3);
            Assert.Equal("Prefeitura Municipal de", string.Join(" ", p.Rodape.Take(3).Select(x => x.Texto)));
            Assert.True(rodape >= baixo + 6, $"{caso}, página {i + 1}: o corpo ({baixo:0.0} pt) encosta no rodapé ({rodape:0.0} pt)");
        }
    }

    /// <summary>A página só tem o fecho: a data ("… Imigrante/ES, …") e a assinatura, sem exigência nem fechamento do texto.</summary>
    static bool SoOFecho(PaginaMedida p) =>
        p.Corpo.Any(x => x.Texto == "Imigrante/ES,") && p.Corpo.Any(x => x.Texto.StartsWith("MATR", StringComparison.Ordinal))
        && !p.Corpo.Any(x => x.Texto is "Fechamento" or "Exigência");

    /// <summary>Despachos curtos e longos, dos dois tipos, convertidos pelo caminho do app (<see cref="EmissaoDespacho.Preparar"/> com o
    /// <see cref="ConversorLibreOffice"/>): em toda página o corpo fica entre a faixa do cabeçalho e o rodapé. Inclui o caso em que só o
    /// fecho (data e assinatura) cai na página seguinte: ele vai inteiro, e começa abaixo da faixa.</summary>
    [FatoComLibreOffice]
    public void no_pdf_o_corpo_fica_entre_o_cabecalho_e_o_rodape()
    {
        var kit = Ambiente.ExigirKit();
        var modelos = RegistroModelos.Ler(kit).Modelos;
        // Perfil e pasta de trabalho fora do OneDrive e num caminho curto (o LibreOffice não aguenta perfil fundo); apagados no fim.
        var raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pmvni-fid", "lo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(raiz);
        try
        {
            var conv = new ConversorLibreOffice(ConversorLibreOffice.Localizar(), TimeSpan.FromMinutes(4), Path.Combine(raiz, "lo"));
            var n = 0;
            List<PaginaMedida> Gerar(TipoDespacho t, int exigencias, out LayoutDespacho l)
            {
                var m = modelos.Single(x => x.Tipo == t);
                var p = EmissaoDespacho.Preparar(PreenchimentoDocxTestes.Final(t, exigencias), m, Path.Combine(raiz, "t" + (++n).ToString(CultureInfo.InvariantCulture)), conv, CancellationToken.None);
                Assert.Empty(p.Erros);
                Assert.True(p.Pdf is not null, p.MensagemSemPdf);
                l = LayoutDespacho.Ler(p.Docx);
                return Medir(p.Pdf!, l);
            }

            // Curto, uma página só.
            var curto = Gerar(TipoDespacho.Correcao, 1, out var layout);
            Conferir("correção curta", curto, layout);

            // Só o fecho na página seguinte: aumenta as exigências até a data abrir a última página.
            List<PaginaMedida>? soFecho = null;
            for (var e = 2; e <= 12 && soFecho is null; e++)
            {
                var paginas = Gerar(TipoDespacho.Correcao, e, out layout);
                Conferir("correção com " + e.ToString(CultureInfo.InvariantCulture) + " exigências", paginas, layout);
                if (paginas.Count >= 2 && SoOFecho(paginas[^1])) soFecho = paginas;
            }
            Assert.True(soFecho is not null, "nenhum despacho de 2 a 12 exigências deixou só o fecho na última página");
            // O fecho foi inteiro: a data, a linha e a assinatura estão na mesma página, e a anterior não ficou com pedaço dele.
            Assert.Contains(soFecho![^1].Corpo, x => x.Texto.StartsWith("____", StringComparison.Ordinal));
            Assert.Contains(soFecho[^1].Corpo, x => x.Texto == "SIDNEY");
            Assert.DoesNotContain(soFecho[^2].Corpo, x => x.Texto.StartsWith("____", StringComparison.Ordinal) || x.Texto.StartsWith("MATR", StringComparison.Ordinal) || x.Texto == "Imigrante/ES,");
            Assert.Contains(soFecho[^2].Corpo, x => x.Texto == "Fechamento");

            // Longo: várias páginas com o corpo começando no alto de cada uma.
            var longo = Gerar(TipoDespacho.Correcao, 60, out layout);
            Assert.True(longo.Count >= 3);
            Conferir("correção longa", longo, layout);

            // Aprovação.
            Conferir("aprovação", Gerar(TipoDespacho.Aprovacao, 0, out layout), layout);
        }
        finally
        {
            try { Directory.Delete(raiz, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            try { Directory.Delete(Path.GetDirectoryName(raiz)!); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
