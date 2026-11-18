using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>Certificados e PDFs fictícios da assinatura digital: tudo gerado no teste (nenhum certificado real, nenhum CPF real).</summary>
internal static class AssinaturaFicticia
{
    public const string Titular = "FULANO DE EXEMPLO (FICTICIO):00000000000";
    public const string Senha = "senha-ficticia-123";
    public const string Tracos = "___________________________________"; // 35, como no modelo

    /// <summary>Um certificado autoassinado de assinatura (RSA 2048), como PFX com senha.</summary>
    public static byte[] Pfx(string titular = Titular, string senha = Senha, DateTimeOffset? de = null, DateTimeOffset? ate = null, bool comChave = true,
        X509KeyUsageFlags uso = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=" + titular + ", O=Teste, C=BR", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        pedido.CertificateExtensions.Add(new X509KeyUsageExtension(uso, critical: true));
        using var c = pedido.CreateSelfSigned(de ?? DateTimeOffset.Now.AddDays(-1), ate ?? DateTimeOffset.Now.AddYears(1));
        if (comChave) return c.Export(X509ContentType.Pkcs12, senha);
        using var semChave = X509CertificateLoader.LoadCertificate(c.RawData);
        return semChave.Export(X509ContentType.Pkcs12, senha);
    }

    public static string GravarPfx(string pasta, byte[] pfx, string nome = "certificado-ficticio.pfx")
    {
        var caminho = Path.Combine(pasta, nome);
        File.WriteAllBytes(caminho, pfx);
        return caminho;
    }

    public sealed record PdfDeTeste(byte[] Bytes, int PaginaDaLinha, double EsquerdaPt, double LarguraPt, double BaseDaLinhaPt, double AlturaPaginaPt);

    /// <summary>Um "despacho" em PDF feito no teste (A4, Verdana): texto em todas as páginas e, na última, a data, a linha de assinatura
    /// (os 35 traços, centrada) e o nome. <paramref name="baseDaLinha"/>: a base da linha a partir do alto da página, em pontos.</summary>
    public static PdfDeTeste Pdf(int paginas = 2, double baseDaLinha = 300, bool comLinha = true)
    {
        AssinaturaPdf.PrepararFontes();
        using var doc = new PdfDocument();
        var fonte = new XFont("Verdana", 11, XFontStyleEx.Regular);
        var negrito = new XFont("Verdana", 11, XFontStyleEx.Bold);
        double esquerda = 0, largura = 0, altura = 0;
        for (var i = 0; i < paginas; i++)
        {
            var p = doc.AddPage();
            p.Width = XUnit.FromPoint(595.3);
            p.Height = XUnit.FromPoint(841.9);
            altura = p.Height.Point;
            using var g = XGraphics.FromPdfPage(p);
            g.DrawString("Texto fictício da página " + (i + 1).ToString(CultureInfo.InvariantCulture) + " com um_traço só no meio.", fonte, XBrushes.Black, new XPoint(85, 120));
            if (i != paginas - 1) continue;
            g.DrawString("Venda Nova do Imigrante/ES, 10 de março de 2099", fonte, XBrushes.Black, new XPoint(250, baseDaLinha - 90));
            if (comLinha)
            {
                largura = g.MeasureString(Tracos, fonte).Width;
                esquerda = (p.Width.Point - largura) / 2;
                g.DrawString(Tracos, fonte, XBrushes.Black, new XPoint(esquerda, baseDaLinha));
            }
            g.DrawString("FULANO DE EXEMPLO (FICTICIO)", negrito, XBrushes.Black, new XPoint(200, baseDaLinha + 22));
        }
        using var ms = new MemoryStream();
        doc.Save(ms);
        return new PdfDeTeste(ms.ToArray(), paginas - 1, esquerda, largura, baseDaLinha, altura);
    }

    /// <summary>O trecho assinado (tudo menos o <c>/Contents</c>) e a assinatura CMS (DER) de um PDF assinado.</summary>
    public static (byte[] Conteudo, byte[] Cms, long[] Faixas) Extrair(byte[] pdf)
    {
        var texto = Encoding.Latin1.GetString(pdf);
        var m = Regex.Match(texto, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        Assert.True(m.Success, "o PDF não tem /ByteRange");
        var f = Enumerable.Range(1, 4).Select(i => long.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        var conteudo = pdf.AsSpan((int)f[0], (int)f[1]).ToArray().Concat(pdf.AsSpan((int)f[2], (int)f[3]).ToArray()).ToArray();
        var hex = texto[(int)(f[0] + f[1])..(int)f[2]];
        Assert.StartsWith("<", hex);
        Assert.EndsWith(">", hex);
        var bytes = Convert.FromHexString(hex[1..^1]);
        // DER: 0x30, 0x82, comprimento em 2 bytes (uma assinatura tem sempre entre 256 e 65535 bytes); o resto do espaço reservado é zero.
        Assert.Equal(0x30, bytes[0]);
        Assert.Equal(0x82, bytes[1]);
        var total = 4 + (bytes[2] << 8 | bytes[3]);
        Assert.All(bytes.Skip(total), b => Assert.Equal(0, b));
        return (conteudo, bytes[..total], f);
    }

    /// <summary>O campo de assinatura do PDF: a página (a partir de 0) e o retângulo, na origem do PDF (de baixo para cima).</summary>
    public static (int Pagina, PdfRectangle Retangulo, PdfDictionary Campo) Campo(byte[] pdf)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        for (var i = 0; i < doc.PageCount; i++)
        {
            var anots = doc.Pages[i].Elements.GetArray("/Annots");
            if (anots is null) continue;
            foreach (var item in anots.Elements)
            {
                var d = item is PdfSharp.Pdf.Advanced.PdfReference r ? r.Value as PdfDictionary : item as PdfDictionary;
                if (d is not null && d.Elements.GetName("/FT") == "/Sig") return (i, d.Elements.GetRectangle("/Rect"), d);
            }
        }
        Assert.Fail("o PDF não tem campo de assinatura visível");
        return default;
    }
}

/// <summary>Assinatura digital do PDF do despacho (2026-11-16).</summary>
public sealed class AssinaturaDigitalTestes
{
    // A hora da assinatura vai num UTCTime (ASN.1), que só chega a 2049: a data fictícia fica antes disso.
    static readonly DateTimeOffset Quando = new(2031, 3, 10, 13, 38, 45, TimeSpan.FromHours(-3));

    static CertificadoAberto Abrir(PastaTeste pasta, byte[]? pfx = null, string senha = AssinaturaFicticia.Senha) =>
        CertificadoDigital.AbrirArquivo(AssinaturaFicticia.GravarPfx(pasta, pfx ?? AssinaturaFicticia.Pfx()), senha);

    [Fact]
    public void assinatura_permanece_no_conteudo_impresso_sem_anotacoes()
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura-impressa");
        using var certificado = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddDays(1)));
        var entrada = AssinaturaFicticia.Pdf();
        var resultado = AssinaturaPdf.Assinar(entrada.Bytes, certificado, Quando, null);
        using var doc = UglyToad.PdfPig.PdfDocument.Open(resultado.Pdf);
        var pagina = doc.GetPage(resultado.Local.Pagina + 1);
        Assert.Contains("Assinado de forma digital", pagina.Text);
        Assert.Contains("FULANO", pagina.Text);
        Assert.DoesNotContain("Assinado de forma digital", doc.GetPage(1).Text);
        var (bytes, assinatura, _) = AssinaturaFicticia.Extrair(resultado.Pdf);
        var cms = new SignedCms(new ContentInfo(bytes), detached: true);
        cms.Decode(assinatura);
        cms.CheckSignature(verifySignatureOnly: true);
    }

    // ---------- o certificado ----------

    [Fact]
    public void abre_o_pfx_com_a_senha_e_mostra_titular_e_validade()
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura-abrir");
        var de = new DateTimeOffset(2098, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var ate = new DateTimeOffset(2101, 1, 2, 0, 0, 0, TimeSpan.Zero);

        using var c = Abrir(pasta, AssinaturaFicticia.Pfx(de: de, ate: ate));

        Assert.Equal(AssinaturaFicticia.Titular, c.Info.Titular);
        Assert.Equal(ate.LocalDateTime, c.Info.ValidoAte);
        Assert.Equal(de.LocalDateTime, c.Info.ValidoDe);
        Assert.Equal(40, c.Info.ImpressaoDigital.Length);
        Assert.StartsWith(AssinaturaFicticia.Titular + " · válido até ", c.Info.Resumo);
        Assert.True(c.Certificado.HasPrivateKey);
        Assert.Null(CertificadoDigital.Problema(c.Certificado, new DateTimeOffset(2099, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Contains("venceu em", CertificadoDigital.Problema(c.Certificado, new DateTimeOffset(2102, 6, 1, 0, 0, 0, TimeSpan.Zero)), StringComparison.Ordinal);
        Assert.Contains("só vale a partir de", CertificadoDigital.Problema(c.Certificado, new DateTimeOffset(2097, 6, 1, 0, 0, 0, TimeSpan.Zero)), StringComparison.Ordinal);
    }

    [Fact]
    public void senha_errada_arquivo_invalido_ausente_ou_sem_chave_dizem_o_que_houve()
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura-erros");
        var antes = Directory.GetFileSystemEntries(pasta, "*", SearchOption.AllDirectories).Length;

        var errada = Assert.Throws<AssinaturaDigitalException>(() => Abrir(pasta, senha: "outra-senha-456"));
        Assert.Equal(CertificadoDigital.SenhaIncorreta, errada.Message);
        Assert.Equal("A senha do certificado não confere. Nada foi assinado nem gravado.", errada.Message);
        Assert.DoesNotContain("outra-senha-456", errada.ToString(), StringComparison.Ordinal);
        Assert.Null(errada.InnerException);
        var vazia = Assert.Throws<AssinaturaDigitalException>(() => Abrir(pasta, senha: ""));
        Assert.Equal(CertificadoDigital.SenhaIncorreta, vazia.Message);

        var lixo = AssinaturaFicticia.GravarPfx(pasta, Encoding.UTF8.GetBytes("isto não é um certificado"), "lixo.pfx");
        Assert.Equal(CertificadoDigital.ArquivoInvalido, Assert.Throws<AssinaturaDigitalException>(() => CertificadoDigital.AbrirArquivo(lixo, AssinaturaFicticia.Senha)).Message);
        Assert.Equal(CertificadoDigital.ArquivoAusente,
            Assert.Throws<AssinaturaDigitalException>(() => CertificadoDigital.AbrirArquivo(Path.Combine(pasta, "nao-existe.pfx"), AssinaturaFicticia.Senha)).Message);
        Assert.Equal(CertificadoDigital.SemChavePrivada,
            Assert.Throws<AssinaturaDigitalException>(() => Abrir(pasta, AssinaturaFicticia.Pfx(comChave: false))).Message);

        // Nada além dos dois arquivos que o próprio teste gravou (o .pfx e o lixo): abrir o certificado não grava nada.
        Assert.Equal(antes + 2, Directory.GetFileSystemEntries(pasta, "*", SearchOption.AllDirectories).Length);
        Assert.Equal("AB12CD", CertificadoDigital.Limpar("ab 12:cd"));
    }

    [Fact]
    public void certificado_que_nao_e_de_assinatura_ou_esta_vencido_nao_assina()
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura-uso");
        var pdf = AssinaturaFicticia.Pdf();

        using var cifra = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1), uso: X509KeyUsageFlags.KeyEncipherment));
        Assert.Contains("não é de assinatura digital", Assert.Throws<AssinaturaDigitalException>(() => AssinaturaPdf.Assinar(pdf.Bytes, cifra, Quando, null)).Message, StringComparison.Ordinal);

        using var vencido = Abrir(pasta, AssinaturaFicticia.Pfx(de: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), ate: new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Contains("venceu em", Assert.Throws<AssinaturaDigitalException>(() => AssinaturaPdf.Assinar(pdf.Bytes, vencido, Quando, null)).Message, StringComparison.Ordinal);
    }

    // ---------- a assinatura ----------

    [Fact]
    public void o_pdf_sai_com_assinatura_pkcs7_que_confere_com_o_conteudo()
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura-cms");
        using var c = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));
        var pdf = AssinaturaFicticia.Pdf();

        var a = AssinaturaPdf.Assinar(pdf.Bytes, c, Quando, null);

        Assert.Equal(AssinaturaFicticia.Titular, a.Certificado.Titular);
        Assert.Equal(Quando, a.Quando);
        var texto = Encoding.Latin1.GetString(a.Pdf);
        Assert.Contains("/Type/Sig", texto.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("/adbe.pkcs7.detached", texto, StringComparison.Ordinal);
        Assert.Contains("/Adobe.PPKLite", texto, StringComparison.Ordinal);

        // As faixas assinadas cobrem o arquivo inteiro, menos a própria assinatura.
        var (conteudo, der, faixas) = AssinaturaFicticia.Extrair(a.Pdf);
        Assert.Equal(0, faixas[0]);
        Assert.Equal(a.Pdf.Length, faixas[2] + faixas[3]);
        Assert.True(faixas[1] > 0 && faixas[2] > faixas[1]);

        // A assinatura confere com o conteúdo, é do certificado do titular e leva a hora da assinatura.
        var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
        cms.Decode(der);
        cms.CheckSignature(verifySignatureOnly: true);
        var assinante = Assert.Single(cms.SignerInfos.Cast<SignerInfo>());
        Assert.Equal(c.Certificado.Thumbprint, assinante.Certificate!.Thumbprint);
        Assert.Equal("2.16.840.1.101.3.4.2.1", assinante.DigestAlgorithm.Value); // SHA-256
        var atributos = assinante.SignedAttributes.Cast<CryptographicAttributeObject>().ToDictionary(x => x.Oid.Value!, x => x.Values[0]);
        Assert.Equal(Quando.UtcDateTime, Assert.IsType<Pkcs9SigningTime>(atributos["1.2.840.113549.1.9.5"]).SigningTime);
        // signing-certificate-v2: o SHA-256 do certificado do titular.
        Assert.EndsWith(Convert.ToHexString(SHA256.HashData(c.Certificado.RawData)), Convert.ToHexString(atributos["1.2.840.113549.1.9.16.2.47"].RawData));

        // Um byte do documento mudado depois de assinar: a assinatura deixa de conferir.
        var mexido = (byte[])conteudo.Clone();
        mexido[100] ^= 0x01;
        var outro = new SignedCms(new ContentInfo(mexido), detached: true);
        outro.Decode(der);
        Assert.ThrowsAny<CryptographicException>(() => outro.CheckSignature(verifySignatureOnly: true));

        // O PDF continua um PDF que o editor lê: mesmas páginas, mesma fonte.
        Assert.Equal(InfoPdf.Ler(pdf.Bytes).Paginas, InfoPdf.Ler(a.Pdf).Paginas);
        Assert.Contains(InfoPdf.Fontes(a.Pdf), f => f.StartsWith("Verdana", StringComparison.Ordinal));
    }

    [Fact]
    public void Via_de_impressao_incorpora_widget_antigo_sem_modificar_o_arquivo()
    {
        using var pasta = Ambiente.PastaDeTeste("impressao-legado");
        using var c = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));
        var novo = AssinaturaPdf.Assinar(AssinaturaFicticia.Pdf().Bytes, c, Quando, null);
        Assert.Null(ImpressaoDespacho.PrepararCopia(novo.Pdf));
        // A estrutura antiga s� cont�m a apar�ncia no widget. Remover a �ltima camada
        // de p�gina reproduz essa condi��o, preservando o XObject e suas fontes.
        using var documento = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(novo.Pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        var conteudos = documento.Pages[novo.Local.Pagina].Contents.Elements;
        conteudos.RemoveAt(conteudos.Count - 1);
        using var memoria = new MemoryStream();
        documento.Save(memoria);
        var antigo = memoria.ToArray();
        var sha = SHA256.HashData(antigo);
        var via = ImpressaoDespacho.PrepararCopia(antigo);
        Assert.NotNull(via);
        Assert.Equal(sha, SHA256.HashData(antigo));
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(via);
        Assert.Contains("Assinado de forma digital", string.Join(" ", pdf.GetPage(novo.Local.Pagina + 1).GetWords().Select(p => p.Text)));
        Assert.Null(ImpressaoDespacho.PrepararCopia(via));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Certificacao_no_limite_da_folha_mantem_integridade(int giro)
    {
        using var pasta = Ambiente.PastaDeTeste("certificacao-prancha");
        using var c = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));
        var basePdf = AssinaturaFicticia.Pdf(2, comLinha: false).Bytes;
        using var entrada = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(basePdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
        var ultima = entrada.Pages[1];
        ultima.Rotate = giro;
        ultima.CropBox = new PdfSharp.Pdf.PdfRectangle(new PdfSharp.Drawing.XRect(15, 25, 500, 700));
        ultima.Elements.SetReal("/UserUnit", 2);
        using var memoria = new MemoryStream();
        entrada.Save(memoria);
        var visto = CarimboAprovado.Aplicar(memoria.ToArray(), 1, 10, 10, "TESTE", c, Quando);
        var bytes = CertificacaoPrancha.Certificar(visto, Quando, c, c.Certificado);
        using var resultado = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(bytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var sig = resultado.Internals.Catalog.Elements.GetDictionary("/Perms")!.Elements.GetDictionary("/DocMDP")!;
        Assert.Equal("/ETSI.CAdES.detached", sig.Elements.GetName("/SubFilter"));
        Assert.Equal(1, sig.Elements.GetArray("/Reference")!.Elements.GetDictionary(0)!.Elements.GetDictionary("/TransformParams")!.Elements.GetInteger("/P"));
        var campo = resultado.Pages[1].Elements.GetArray("/Annots")!.Elements.GetDictionary(0)!;
        var rect = campo.Elements.GetRectangle("/Rect");
        var esperado = giro switch { 0 => (405,25,515,42), 90 => (498,615,515,725), 180 => (15,708,125,725), _ => (15,25,32,135) };
        Assert.Equal(esperado, ((int)rect.X1,(int)rect.Y1,(int)rect.X2,(int)rect.Y2));
        var range = sig.Elements.GetArray("/ByteRange")!.Elements;
        var primeiro = range.GetInteger(1);
        var segundo = range.GetInteger(2);
        Assert.Equal(bytes.Length, segundo + range.GetInteger(3));
        var conteudo = bytes[..primeiro].Concat(bytes[segundo..]).ToArray();
        var der = Convert.FromHexString(System.Text.Encoding.ASCII.GetString(bytes[(primeiro + 1)..(segundo - 1)]));
        var cms = new SignedCms(new ContentInfo(conteudo), true);
        cms.Decode(der);
        cms.CheckSignature(true);
        conteudo[10] ^= 1;
        var adulterado = new SignedCms(new ContentInfo(conteudo), true);
        adulterado.Decode(der);
        Assert.ThrowsAny<CryptographicException>(() => adulterado.CheckSignature(true));
        if (Environment.GetEnvironmentVariable("PMVNI_TESTES_GUARDAR_PDF") is { Length: > 0 } dest && Directory.Exists(dest))
            File.WriteAllBytes(Path.Combine(dest, $"certificado-ficticio-{giro}.pdf"), bytes);
    }
    [Fact]
    public void Carimbo_preserva_peticao_data_e_observacoes_selecionaveis()
    {
        using var pasta = Ambiente.PastaDeTeste("carimbo-aprovado");
        using var c = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));
        var original = AssinaturaFicticia.Pdf(1, comLinha: false);
        var selecionadas = new[] { ObservacaoPrancha.Predefinidas[1], ObservacaoPrancha.Predefinidas[6],
            new ObservacaoPrancha("Exemplo de observação.", "Referência fictícia.") };
        var a = CarimboAprovado.Aplicar(original.Bytes, 0, 12, 12, "TESTE-42", c, Quando,
            selecionadas, new PdfSharp.Drawing.XRect(15, 230, 450, 240));
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(a.Pdf);
        var texto = string.Join(" ", pdf.GetPage(1).GetWords().Select(p => p.Text));
        Assert.Contains("TESTE-42", texto);
        Assert.Contains(Quando.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), texto);
        Assert.Contains("OBSERVAÇÕES:", texto);
        Assert.Contains("cisterna", ObservacaoPrancha.Predefinidas[3].Texto);
        Assert.DoesNotContain("cisterna", texto);
        Assert.Contains("Exemplo", texto);
        var (conteudo, der, _) = AssinaturaFicticia.Extrair(a.Pdf);
        var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
        cms.Decode(der);
        cms.CheckSignature(verifySignatureOnly: true);
    }
    [Theory]
    [InlineData(1, 300)]
    [InlineData(2, 180)]
    [InlineData(3, 640)]
    public void a_aparencia_fica_logo_acima_da_linha_de_assinatura_na_pagina_em_que_ela_cai(int paginas, double baseDaLinha)
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura-aparencia");
        using var c = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));
        var pdf = AssinaturaFicticia.Pdf(paginas, baseDaLinha);
        var logo = AssinaturaPdf.LogoDoDespacho(PreenchimentoDocxTestes.Modelo(TipoDespacho.Correcao));
        Assert.NotNull(logo);

        var a = AssinaturaPdf.Assinar(pdf.Bytes, c, Quando, logo);

        // O retângulo esperado: a largura da linha, a altura na proporção fixa, a base logo acima dos traços.
        var (pagina, r, campo) = AssinaturaFicticia.Campo(a.Pdf);
        Assert.Equal(pdf.PaginaDaLinha, pagina);
        Assert.Equal(pdf.PaginaDaLinha, a.Local.Pagina);
        Assert.InRange(r.X1, pdf.EsquerdaPt - 1.5, pdf.EsquerdaPt + 1.5);
        Assert.InRange(r.Width, pdf.LarguraPt - 3, pdf.LarguraPt + 0.5);
        Assert.Equal(r.Width * AssinaturaPdf.AlturaPorLargura, r.Height, 1);
        var baseNoPdf = pdf.AlturaPaginaPt - pdf.BaseDaLinhaPt; // a base da linha, na origem do PDF
        // O traço fica um pouco abaixo da base do texto; a aparência começa 1,5 pt acima do alto do traço: nunca por cima do nome (abaixo).
        Assert.InRange(r.Y1, baseNoPdf - 2.5, baseNoPdf + 2);
        Assert.Equal(a.Local.XPt, r.X1, 2);
        Assert.Equal(a.Local.YPt, r.Y1, 2);
        Assert.Equal(a.Local.LarguraPt, r.Width, 2);
        Assert.Equal(a.Local.AlturaPt, r.Height, 2);

        // A aparência tem a logomarca (imagem) e o texto (fonte) desenhados.
        var ap = campo.Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!;
        var recursos = ap.Elements.GetDictionary("/Resources")!;
        Assert.NotNull(recursos.Elements.GetDictionary("/XObject"));
        Assert.NotNull(recursos.Elements.GetDictionary("/Font"));

        // E desenhados DENTRO da caixa da aparência (0,0 – largura,altura): o recorte da logomarca na metade esquerda, a primeira linha
        // de texto na metade direita, perto do alto, e as frases da assinatura visível, na ordem.
        var caixa = ap.Elements.GetRectangle("/BBox");
        Assert.Equal(r.Width, caixa.Width, 1);
        Assert.Equal(r.Height, caixa.Height, 1);
        var desenho = Encoding.Latin1.GetString(ap.Stream!.UnfilteredValue);
        static double N(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        var recorte = Regex.Match(desenho, @"(-?[\d.]+) (-?[\d.]+) m\b");
        Assert.True(recorte.Success);
        Assert.InRange(N(recorte.Groups[1].Value), 0, caixa.Width * AssinaturaPdf.ParteDaLogo);
        Assert.InRange(N(recorte.Groups[2].Value), 0, caixa.Height);
        var texto = Regex.Match(desenho, @"(-?[\d.]+) (-?[\d.]+) Td\b");
        Assert.True(texto.Success);
        Assert.InRange(N(texto.Groups[1].Value), caixa.Width * AssinaturaPdf.ParteDaLogo, caixa.Width * 0.6);
        Assert.InRange(N(texto.Groups[2].Value), caixa.Height * 0.5, caixa.Height);
        var linhas = Regex.Matches(desenho, @"\((.*?)\) Tj").Select(m => m.Groups[1].Value.Replace("\\(", "(", StringComparison.Ordinal).Replace("\\)", ")", StringComparison.Ordinal)).ToList();
        Assert.InRange(linhas.Count, 3, 6);
        Assert.Equal("Assinado de forma digital por " + AssinaturaFicticia.Titular + " Dados: 2031.03.10 13:38:45 -03'00'", string.Join(" ", linhas));
        var tamanho = N(Regex.Match(desenho, @"/F\d+ ([\d.]+) Tf").Groups[1].Value);
        Assert.InRange(tamanho, 6, 9.5);
        Assert.True(linhas.Count * tamanho * 1.18 <= caixa.Height + 0.01, "o texto não cabe na altura da aparência");
    }

    [Fact]
    public void as_frases_da_aparencia_sao_as_da_assinatura_visivel_padrao()
    {
        Assert.Equal(new[] { "Assinado de forma digital por " + AssinaturaFicticia.Titular, "Dados: 2031.03.10 13:38:45 -03'00'" },
            AssinaturaPdf.Frases(AssinaturaFicticia.Titular, Quando));
        Assert.Equal("Dados: 2031.03.10 16:38:45 +00'00'", AssinaturaPdf.Frases("X", Quando.ToUniversalTime())[1]);
        Assert.Equal("Dados: 2031.03.10 22:08:45 +05'30'", AssinaturaPdf.Frases("X", Quando.ToOffset(new TimeSpan(5, 30, 0)))[1]);
    }

    [Fact]
    public void pdf_sem_linha_de_assinatura_nao_e_assinado()
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura-sem-linha");
        using var c = Abrir(pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));

        var e = Assert.Throws<AssinaturaDigitalException>(() => AssinaturaPdf.Assinar(AssinaturaFicticia.Pdf(comLinha: false).Bytes, c, Quando, null));
        Assert.Equal(AssinaturaPdf.SemLinha, e.Message);
        Assert.Throws<AssinaturaDigitalException>(() => AssinaturaPdf.Localizar([1, 2, 3]));
    }

    [Fact]
    public void a_logomarca_e_a_faixa_do_cabecalho_do_despacho_recortada()
    {
        foreach (var t in new[] { TipoDespacho.Correcao, TipoDespacho.Aprovacao })
        {
            var docx = PreenchimentoDocx.Preencher(PreenchimentoDocxTestes.Modelo(t), PreenchimentoDocxTestes.Final(t));
            var logo = AssinaturaPdf.LogoDoDespacho(docx);
            Assert.NotNull(logo);
            Assert.Equal(0x89, logo!.Imagem[0]); // PNG
            // O recorte fica no meio da faixa (o brasão com o nome), sem as faixas coloridas das pontas.
            Assert.InRange(logo.CorteEsquerdo, 0.36, 0.3872);
            Assert.InRange(1 - logo.CorteDireito, 0.6266, 0.6474);
            Assert.InRange(logo.CorteTopo, 0.25, 0.3333);
            Assert.Equal(0, logo.CorteBaixo);
        }
        Assert.Null(AssinaturaPdf.LogoDoDespacho([1, 2, 3]));
    }

    // ---------- na emissão ----------

    static AssinadorComCertificado Assinador(CertificadoAberto c) => new(c, () => Quando);

    [Fact]
    public void a_emissao_assina_o_pdf_e_o_recibo_registra_a_assinatura()
    {
        using var cena = new EmissaoDespachoTestes.Cena("assinatura-emissao");
        using var c = Abrir(cena.Pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));
        var pdf = AssinaturaFicticia.Pdf(2);

        var p = EmissaoDespacho.Preparar(cena.Final, cena.Modelo, cena.Trabalho, ConversorFalso.Com(pdf.Bytes), Assinador(c), CancellationToken.None);

        Assert.Empty(p.Erros);
        Assert.NotNull(p.Assinatura);
        Assert.Equal(AssinaturaFicticia.Titular, p.Assinatura!.Certificado.Titular);
        Assert.Equal(1, p.Assinatura.Local.Pagina);
        Assert.Equal(2, p.Paginas);
        Assert.NotEqual(pdf.Bytes, p.Pdf); // o PDF que se confere e que se grava é o assinado
        AssinaturaFicticia.Extrair(p.Pdf!);
        // A aparência leva a logomarca do cabeçalho do próprio despacho.
        var campo = AssinaturaFicticia.Campo(p.Pdf!).Campo;
        Assert.NotNull(campo.Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject"));

        var e = cena.Gravar(p, new[] { 1, 2 });
        Assert.Equal(p.Pdf, File.ReadAllBytes(e.Pdf!));
        var r = ReciboDespacho.Ler(e.Recibo);
        Assert.Equal(DocxFicticio.Sha(p.Pdf!), (string?)r["pdf"]!["sha256"]);
        var a = r["pdf"]!["assinatura_digital"]!;
        Assert.Equal("2031-03-10T16:38:45Z", (string?)a["assinado_em"]);
        Assert.Equal(c.Certificado.Thumbprint, (string?)a["certificado_sha1"]);
        Assert.Equal(2, (int)a["pagina"]!);
        // Nada do titular nem da senha no recibo.
        var recibo = File.ReadAllText(e.Recibo);
        Assert.DoesNotContain("00000000000", recibo, StringComparison.Ordinal);
        Assert.DoesNotContain(AssinaturaFicticia.Senha, recibo, StringComparison.Ordinal);
    }

    [Fact]
    public void sem_assinador_o_despacho_sai_como_antes()
    {
        using var cena = new EmissaoDespachoTestes.Cena("assinatura-sem");
        var pdf = AssinaturaFicticia.Pdf(2);

        var p = cena.Preparar(ConversorFalso.Com(pdf.Bytes));

        Assert.Empty(p.Erros);
        Assert.Null(p.Assinatura);
        Assert.Equal(pdf.Bytes, p.Pdf);
        var e = cena.Gravar(p, new[] { 1, 2 });
        Assert.Null(ReciboDespacho.Ler(e.Recibo)["pdf"]!["assinatura_digital"]);
    }

    [Fact]
    public void assinatura_que_falha_nao_deixa_gravar_nada()
    {
        using var cena = new EmissaoDespachoTestes.Cena("assinatura-falha");
        // Certificado vencido na hora da assinatura; e um PDF sem a linha de assinatura.
        using var vencido = Abrir(cena.Pasta, AssinaturaFicticia.Pfx(de: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), ate: new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        using var valido = Abrir(cena.Pasta, AssinaturaFicticia.Pfx(de: Quando.AddDays(-1), ate: Quando.AddYears(1)));
        var antes = RelatorioFicticio.Retrato(cena.Arvore.Raiz);

        var p1 = EmissaoDespacho.Preparar(cena.Final, cena.Modelo, cena.Trabalho, ConversorFalso.Com(AssinaturaFicticia.Pdf(2).Bytes), Assinador(vencido), CancellationToken.None);
        var p2 = EmissaoDespacho.Preparar(cena.Final, cena.Modelo, cena.Trabalho + "-2", ConversorFalso.Com(AssinaturaFicticia.Pdf(2, comLinha: false).Bytes), Assinador(valido), CancellationToken.None);

        Assert.Contains("venceu em", Assert.Single(p1.Erros), StringComparison.Ordinal);
        Assert.Equal(AssinaturaPdf.SemLinha, Assert.Single(p2.Erros));
        foreach (var p in new[] { p1, p2 })
        {
            Assert.Null(p.Pdf);
            Assert.Null(p.Assinatura);
            Assert.False(Pmvni.App.FluxoNovoDespacho.PodeGravar(p, Array.Empty<int>()));
            Assert.Throws<InvalidOperationException>(() => cena.Gravar(p, null));
        }
        // Nada na pasta do processo: nem DOCX, nem PDF, nem recibo, nem a pasta da etapa.
        Assert.Equal(antes, RelatorioFicticio.Retrato(cena.Arvore.Raiz));
        Assert.False(Directory.Exists(cena.Etapa.Pasta));
    }

    [Fact]
    public void senha_errada_para_antes_de_gerar_e_nada_e_gravado()
    {
        using var cena = new EmissaoDespachoTestes.Cena("assinatura-senha-errada");
        var pfx = AssinaturaFicticia.GravarPfx(cena.Pasta, AssinaturaFicticia.Pfx());
        var antes = RelatorioFicticio.Retrato(cena.Arvore.Raiz);
        var conv = ConversorFalso.Com(AssinaturaFicticia.Pdf(2).Bytes);

        // É assim que o app faz: o certificado é aberto (e a senha conferida) antes de gerar; com a senha errada, nem o DOCX é preparado.
        var e = Assert.Throws<AssinaturaDigitalException>(() =>
        {
            using var c = CertificadoDigital.AbrirArquivo(pfx, "senha-errada");
            EmissaoDespacho.Preparar(cena.Final, cena.Modelo, cena.Trabalho, conv, Assinador(c), CancellationToken.None);
        });

        Assert.Equal("A senha do certificado não confere. Nada foi assinado nem gravado.", e.Message);
        Assert.Equal(0, conv.Chamadas);
        Assert.False(Directory.Exists(cena.Trabalho));
        Assert.Equal(antes, RelatorioFicticio.Retrato(cena.Arvore.Raiz));
        Assert.False(Directory.Exists(cena.Etapa.Pasta));
    }

    /// <summary>No PDF do LibreOffice real (só com <c>PMVNI_TESTES_LIBREOFFICE=1</c>): a assinatura visível fica na página do fecho, entre a
    /// data e a linha, com a largura da linha, sem cobrir nenhum texto; e a assinatura confere. Com <c>PMVNI_TESTES_GUARDAR_PDF</c> (uma
    /// pasta), o PDF assinado fica lá para ser aberto no leitor.</summary>
    [FatoComLibreOffice]
    public void no_pdf_do_libreoffice_a_assinatura_fica_entre_a_data_e_a_linha()
    {
        var kit = Ambiente.ExigirKit();
        var modelos = RegistroModelos.Ler(kit).Modelos;
        var raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pmvni-fid", "as-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(raiz);
        try
        {
            var conv = new ConversorLibreOffice(ConversorLibreOffice.Localizar(), TimeSpan.FromMinutes(4), Path.Combine(raiz, "lo"));
            var agora = DateTimeOffset.Now;
            using var c = CertificadoDigital.AbrirArquivo(AssinaturaFicticia.GravarPfx(raiz, AssinaturaFicticia.Pfx()), AssinaturaFicticia.Senha);
            var n = 0;
            foreach (var (tipo, exigencias) in new[] { (TipoDespacho.Correcao, 2), (TipoDespacho.Correcao, 4), (TipoDespacho.Correcao, 60), (TipoDespacho.Aprovacao, 0) })
            {
                var p = EmissaoDespacho.Preparar(PreenchimentoDocxTestes.Final(tipo, exigencias), modelos.Single(m => m.Tipo == tipo),
                    Path.Combine(raiz, "t" + (++n).ToString(CultureInfo.InvariantCulture)), conv, new AssinadorComCertificado(c, () => agora), CancellationToken.None);
                Assert.Empty(p.Erros);
                Assert.True(p.Pdf is not null, p.MensagemSemPdf);
                var local = p.Assinatura!.Local;
                Assert.Equal(p.Paginas!.Value - 1, local.Pagina); // o fecho está sempre na última página

                var (conteudo, der, _) = AssinaturaFicticia.Extrair(p.Pdf!);
                var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
                cms.Decode(der);
                cms.CheckSignature(verifySignatureOnly: true);

                using var doc = UglyToad.PdfPig.PdfDocument.Open(p.Pdf!);
                var pagina = doc.GetPage(local.Pagina + 1);
                var palavras = pagina.GetWords().Where(w => w.Text.Trim().Length > 0).ToList();
                var linha = palavras.Single(w => w.Text.StartsWith("__________", StringComparison.Ordinal)).BoundingBox;
                var data = palavras.Where(w => w.Text == "Imigrante/ES,").Select(w => w.BoundingBox).OrderBy(b => b.Bottom).First();
                // Com a largura da linha, logo acima dela e abaixo da data; 245 × 56 pt no modelo do kit.
                Assert.InRange(local.XPt, linha.Left - 1, linha.Left + 1);
                Assert.InRange(local.LarguraPt, linha.Width - 2, linha.Width + 1);
                Assert.InRange(local.LarguraPt, 235, 255);
                Assert.Equal(local.LarguraPt * AssinaturaPdf.AlturaPorLargura, local.AlturaPt, 3);
                Assert.InRange(local.YPt - linha.Top, 0.5, 3);
                Assert.True(local.YPt + local.AlturaPt < data.Bottom - 2, "a assinatura visível encosta na data");
                // Nenhum texto da página fica debaixo da assinatura visível.
                var original = EmissaoDespacho.Preparar(PreenchimentoDocxTestes.Final(tipo, exigencias), modelos.Single(m => m.Tipo == tipo),
                    Path.Combine(raiz, "original" + n), conv, CancellationToken.None);
                Assert.Empty(original.Erros);
                using var semAssinatura = UglyToad.PdfPig.PdfDocument.Open(original.Pdf!);
                Assert.DoesNotContain(semAssinatura.GetPage(local.Pagina + 1).GetWords(), w => w.BoundingBox.Right > local.XPt && w.BoundingBox.Left < local.XPt + local.LarguraPt
                    && w.BoundingBox.Top > local.YPt && w.BoundingBox.Bottom < local.YPt + local.AlturaPt);
                // O nome continua logo abaixo da linha.
                Assert.Contains(palavras, w => w.Text == "SIDNEY" && w.BoundingBox.Top < linha.Bottom);

                if (Environment.GetEnvironmentVariable("PMVNI_TESTES_GUARDAR_PDF") is { Length: > 0 } guardar && Directory.Exists(guardar))
                    File.WriteAllBytes(Path.Combine(guardar, "assinado-" + tipo + "-" + exigencias.ToString(CultureInfo.InvariantCulture) + ".pdf"), p.Pdf!);
            }
        }
        finally
        {
            try { Directory.Delete(raiz, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            try { Directory.Delete(Path.GetDirectoryName(raiz)!); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
