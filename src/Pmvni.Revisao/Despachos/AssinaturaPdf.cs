using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;

namespace Pmvni.Revisao;

/// <summary>Onde a assinatura visível fica no PDF: a página (a partir de 0) e o retângulo em pontos, com a origem no canto de baixo à
/// esquerda da página (o sistema do PDF).</summary>
public sealed record LocalAssinatura(int Pagina, double XPt, double YPt, double LarguraPt, double AlturaPt);

/// <summary>A logomarca que vai à esquerda da assinatura visível: os bytes de uma imagem (PNG ou JPEG) e o recorte dela, em frações de 0 a 1
/// (a logomarca é um pedaço da faixa do cabeçalho do modelo).</summary>
public sealed record LogoAssinatura(byte[] Imagem, double CorteEsquerdo, double CorteTopo, double CorteDireito, double CorteBaixo);

/// <summary>O PDF assinado, onde a assinatura visível ficou, de quem é o certificado e a hora da assinatura.</summary>
public sealed record PdfAssinado(byte[] Pdf, LocalAssinatura Local, InfoCertificado Certificado, DateTimeOffset Quando);

/// <summary>Quem assina o PDF do despacho (o app usa o <see cref="AssinadorComCertificado"/>; os testes, um certificado gerado na hora).</summary>
public interface IAssinadorPdf
{
    /// <summary>Assina <paramref name="pdf"/>; <paramref name="docx"/> é o despacho que o gerou (de onde vem a logomarca do cabeçalho).
    /// Qualquer falha: <see cref="AssinaturaDigitalException"/>.</summary>
    PdfAssinado Assinar(byte[] pdf, byte[] docx, CancellationToken ct);
}

/// <summary>Assina com um certificado já aberto (a senha já foi conferida), na hora de <paramref name="agora"/>.</summary>
public sealed class AssinadorComCertificado(CertificadoAberto certificado, Func<DateTimeOffset> agora) : IAssinadorPdf
{
    public PdfAssinado Assinar(byte[] pdf, byte[] docx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return AssinaturaPdf.Assinar(pdf, certificado, agora(), AssinaturaPdf.LogoDoDespacho(docx));
    }
}

/// <summary>Assinatura digital do PDF do despacho (2026-11-16): uma assinatura PKCS#7 destacada (<c>adbe.pkcs7.detached</c>, SHA-256, com o
/// certificado do titular, a cadeia que veio no arquivo, a hora da assinatura e o atributo <c>signing-certificate-v2</c>), num campo de
/// assinatura com aparência visível: a logomarca da Prefeitura à esquerda e, à direita, "Assinado de forma digital por NOME:CPF / Dados:
/// AAAA.MM.DD HH:MM:SS -03'00'" (a aparência padrão das assinaturas visíveis de PDF). A aparência fica sempre logo acima da linha de
/// assinatura do despacho, com a largura da linha, na página em que a linha cair.</summary>
public static class AssinaturaPdf
{
    /// <summary>A altura da aparência em relação à largura (a da linha de assinatura), medida no despacho assinado de referência.</summary>
    public const double AlturaPorLargura = 0.23;

    /// <summary>A parte da largura que cabe à logomarca; o resto é do texto.</summary>
    public const double ParteDaLogo = 0.487;

    /// <summary>Distância entre o traço da linha de assinatura e a base da aparência, em pontos.</summary>
    public const double FolgaDaLinhaPt = 1.5;

    /// <summary>Quantos traços seguidos fazem uma linha de assinatura (o modelo tem 35).</summary>
    const int TracosDaLinha = 10;

    public const string SemLinha = "A linha de assinatura não foi encontrada no PDF do despacho; a assinatura digital não foi aplicada e nada foi gravado.";
    const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    const string OidCertificadoAssinanteV2 = "1.2.840.113549.1.9.16.2.47";

    // O recorte da logomarca dentro da faixa do cabeçalho dos modelos do kit (imagem de 1299 × 138 px: o brasão e o nome ficam entre as
    // colunas 503 e 813 e da linha 46 até a última; medido em 16/11/2026), com uma pequena folga.
    const double LogoEsquerda = 0.380, LogoTopo = 0.310, LogoDireita = 0.368, LogoBaixo = 0.0;

    static readonly object TravaDeFontes = new();
    static bool fontesProntas;

    /// <summary>A logomarca do despacho: a faixa do cabeçalho do DOCX (a imagem da largura da página), recortada no brasão com o nome.
    /// Sem a faixa (ou DOCX ilegível): null, e a aparência sai só com o texto.</summary>
    public static LogoAssinatura? LogoDoDespacho(byte[] docx)
    {
        try
        {
            var l = LayoutDespacho.Ler(docx);
            var faixa = l.Imagens.Where(i => i.LarguraPt > l.LarguraPt * 0.8 && i.AlturaPt < l.AlturaPt / 4).OrderBy(i => i.YPt).FirstOrDefault();
            return faixa is null ? null : new LogoAssinatura(faixa.Bytes, LogoEsquerda, LogoTopo, LogoDireita, LogoBaixo);
        }
        catch (ModeloForaDoPadraoException) { return null; }
    }

    /// <summary>As duas frases da aparência: "Assinado de forma digital por NOME:CPF" e "Dados: 2026.11.10 13:38:45 -03'00'".</summary>
    public static IReadOnlyList<string> Frases(string titular, DateTimeOffset quando)
    {
        var fuso = (quando.Offset < TimeSpan.Zero ? "-" : "+") + Math.Abs(quando.Offset.Hours).ToString("D2", CultureInfo.InvariantCulture) + "'"
                   + Math.Abs(quando.Offset.Minutes).ToString("D2", CultureInfo.InvariantCulture) + "'";
        return ["Assinado de forma digital por " + titular, "Dados: " + quando.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + fuso];
    }

    /// <summary>Acha a linha de assinatura (a última sequência de traços "_" do documento) e devolve o retângulo da aparência: logo acima
    /// dela, com a mesma largura. Sem linha: <see cref="AssinaturaDigitalException"/>.</summary>
    public static LocalAssinatura Localizar(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        LocalAssinatura? local = null;
        try
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
            foreach (var pagina in doc.GetPages())
            {
                // Os traços de uma mesma linha de texto (mesma base, com meio ponto de tolerância), da esquerda para a direita.
                var linhas = pagina.Letters.Where(l => l.Value == "_")
                    .GroupBy(l => Math.Round(l.StartBaseLine.Y * 2, MidpointRounding.AwayFromZero))
                    .Where(g => g.Count() >= TracosDaLinha)
                    .OrderByDescending(g => g.Key); // do alto da página para baixo
                foreach (var g in linhas)
                {
                    var esquerda = g.Min(l => l.BoundingBox.Left);
                    var direita = g.Max(l => l.BoundingBox.Right);
                    var topo = g.Max(l => l.BoundingBox.Top);
                    var largura = direita - esquerda;
                    if (largura < 72) continue;
                    // A última linha de traços do documento é a da assinatura (a de mais baixo na última página que tiver uma).
                    local = new LocalAssinatura(pagina.Number - 1, esquerda, topo + FolgaDaLinhaPt, largura, largura * AlturaPorLargura);
                }
            }
        }
        catch (Exception e) when (e is not AssinaturaDigitalException and not OutOfMemoryException)
        {
            throw new AssinaturaDigitalException("O PDF do despacho não pôde ser lido para a assinatura digital (" + MensagensDeErro.ParaUsuario(e) + ").");
        }
        return local ?? throw new AssinaturaDigitalException(SemLinha);
    }

    /// <summary>Assina o PDF com o certificado, com a aparência visível no lugar de <see cref="Localizar"/>. O que impede (certificado
    /// vencido, PDF sem a linha, token que recusa): <see cref="AssinaturaDigitalException"/>; nada é gravado em disco aqui.</summary>
    public static PdfAssinado Assinar(byte[] pdf, CertificadoAberto certificado, DateTimeOffset quando, LogoAssinatura? logo)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(certificado);
        if (CertificadoDigital.Problema(certificado.Certificado, quando) is { } problema) throw new AssinaturaDigitalException(problema);
        return AssinarNoLocal(pdf, certificado, quando, logo, Localizar(pdf), "Despacho da Secretaria Municipal de Obras");
    }

    internal static PdfAssinado AssinarNoLocal(byte[] pdf, CertificadoAberto certificado, DateTimeOffset quando,
        LogoAssinatura? logo, LocalAssinatura local, string motivo)
    {
        if (CertificadoDigital.Problema(certificado.Certificado, quando) is { } problema)
            throw new AssinaturaDigitalException(problema);
        var info = certificado.Info;
        PrepararFontes();
        try
        {
            using var doc = PdfReader.Open(new MemoryStream(pdf, writable: false), PdfDocumentOpenMode.Modify);
            if (local.Pagina >= doc.PageCount) throw new AssinaturaDigitalException(SemLinha);
            var pagina = doc.Pages[local.Pagina];
            var aparencia = new Aparencia(Frases(info.Titular, quando), logo);
            // A impressão pode omitir widgets. O conteúdo visível entra no documento antes do cálculo do CMS.
            using (var gfx = XGraphics.FromPdfPage(pagina, XGraphicsPdfPageOptions.Append))
            {
                gfx.TranslateTransform(local.XPt, pagina.Height.Point - local.YPt - local.AlturaPt);
                var area = new XRect(0, 0, local.LarguraPt, local.AlturaPt);
                gfx.IntersectClip(area);
                aparencia.DrawAppearance(gfx, area);
            }
            var opcoes = new DigitalSignatureOptions
            {
                Reason = motivo,
                Location = "Venda Nova do Imigrante/ES",
                AppName = "Editor de Relatórios PMVNI",
                PageIndex = local.Pagina,
                Rectangle = new XRect(local.XPt, local.YPt, local.LarguraPt, local.AlturaPt),
                AppearanceHandler = aparencia,
            };
            DigitalSignatureHandler.ForDocument(doc, new Assinante(certificado, quando), opcoes);
            using var saida = new MemoryStream();
            doc.SaveAsync(saida).GetAwaiter().GetResult();
            return new PdfAssinado(saida.ToArray(), local, info, quando);
        }
        catch (AssinaturaDigitalException) { throw; }
        catch (CryptographicException e)
        {
            // Token desconectado, PIN cancelado ou recusado, chave que não assina: a mensagem do sistema, sem nada do certificado.
            throw new AssinaturaDigitalException("O certificado não pôde assinar o despacho (" + MensagensDeErro.ParaUsuario(e) + "). Nada foi gravado.");
        }
        catch (Exception e) when (e is PdfReaderException or InvalidOperationException or IOException or NotSupportedException or ArgumentException)
        {
            throw new AssinaturaDigitalException("O PDF do despacho não pôde ser assinado (" + MensagensDeErro.ParaUsuario(e) + "). Nada foi gravado.");
        }
    }

    /// <summary>O PDFsharp desenha o texto da aparência com uma fonte do Windows (Arial), que vai embutida no PDF.</summary>
    internal static void PrepararFontes()
    {
        lock (TravaDeFontes)
        {
            if (fontesProntas) return;
            // O PDFsharp só aceita esta opção uma vez por processo (até ler o valor já a fixa): se outra parte do programa já a definiu, fica como está.
            try { GlobalFontSettings.UseWindowsFontsUnderWindows = true; }
            catch (InvalidOperationException) { }
            fontesProntas = true;
        }
    }

    // ---------- a assinatura (CMS) ----------

    /// <summary>A assinatura PKCS#7 destacada do trecho do PDF que o PDFsharp entrega (tudo menos o próprio campo <c>/Contents</c>).</summary>
    sealed class Assinante(CertificadoAberto c, DateTimeOffset quando) : IDigitalSigner
    {
        public string CertificateName => c.Info.Titular;

        /// <summary>O espaço reservado para a assinatura, sem assinar duas vezes (um token pediria a senha duas vezes): os certificados que
        /// vão junto, a assinatura (o tamanho da chave) e uma folga larga para os atributos e a estrutura.</summary>
        public Task<int> GetSignatureSizeAsync()
        {
            var chave = c.Certificado.GetRSAPublicKey()?.KeySize ?? c.Certificado.GetECDsaPublicKey()?.KeySize ?? 4096;
            return Task.FromResult(c.Certificado.RawData.Length + c.Cadeia.Sum(x => x.RawData.Length) + chave / 8 * 2 + 2048);
        }

        public Task<byte[]> GetSignatureAsync(Stream stream)
        {
            // O fluxo do PDFsharp (as faixas do arquivo fora do /Contents) só sabe ler em frente.
            // desde o começo, até o tamanho que ele informa.
            var conteudo = new byte[stream.Length];
            stream.Position = 0;
            for (int lidos = 0, n; lidos < conteudo.Length; lidos += n)
                if ((n = stream.Read(conteudo, lidos, conteudo.Length - lidos)) <= 0) throw new CryptographicException("o conteúdo a assinar veio incompleto");
            var cms = new SignedCms(new ContentInfo(conteudo), detached: true);
            var assinante = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, c.Certificado)
            {
                DigestAlgorithm = new Oid(OidSha256),
                // Só os certificados em mãos: montar a cadeia pelo Windows poderia ir buscá-la na internet.
                IncludeOption = X509IncludeOption.EndCertOnly,
            };
            foreach (var x in c.Cadeia) assinante.Certificates.Add(x);
            assinante.SignedAttributes.Add(new Pkcs9SigningTime(quando.UtcDateTime));
            assinante.SignedAttributes.Add(new AsnEncodedData(new Oid(OidCertificadoAssinanteV2), CertificadoAssinanteV2(c.Certificado)));
            cms.ComputeSignature(assinante, silent: false);
            return Task.FromResult(cms.Encode());
        }
    }

    /// <summary><c>SigningCertificateV2</c> (RFC 5035) com o SHA-256 do certificado do titular: liga a assinatura a este certificado.</summary>
    static byte[] CertificadoAssinanteV2(X509Certificate2 c)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())      // SigningCertificateV2
        using (w.PushSequence())      // certs
        using (w.PushSequence())      // ESSCertIDv2 (hashAlgorithm omitido = SHA-256)
            w.WriteOctetString(SHA256.HashData(c.RawData));
        return w.Encode();
    }

    // ---------- a aparência ----------

    /// <summary>Desenha a aparência no retângulo do campo: a logomarca à esquerda (inteira, sem deformar, centrada na altura) e o texto
    /// à direita, com o maior tamanho de letra (até 9,5 pt) em que todas as linhas cabem.</summary>
    sealed class Aparencia(IReadOnlyList<string> frases, LogoAssinatura? logo) : IAnnotationAppearanceHandler
    {
        const string Fonte = "Arial";
        const double Entrelinha = 1.18, TamanhoMaximo = 9.5, TamanhoMinimo = 4, Vao = 0.018;

        public void DrawAppearance(XGraphics gfx, XRect rect)
        {
            // O PDFsharp entrega o retângulo do campo na página, mas o desenho é na caixa da própria aparência: de (0,0) à largura e altura.
            rect = new XRect(0, 0, rect.Width, rect.Height);
            var larguraLogo = logo is null ? 0 : rect.Width * ParteDaLogo;
            if (logo is not null) DesenharLogo(gfx, new XRect(rect.X, rect.Y, larguraLogo, rect.Height), logo);
            var xTexto = rect.X + larguraLogo + (logo is null ? 0 : rect.Width * Vao);
            var areaTexto = new XRect(xTexto, rect.Y, rect.Right - xTexto, rect.Height);

            var tamanho = TamanhoMaximo;
            XFont fonte;
            List<string> linhas;
            while (true)
            {
                fonte = new XFont(Fonte, tamanho, XFontStyleEx.Regular);
                linhas = frases.SelectMany(f => Quebrar(gfx, fonte, f, areaTexto.Width)).ToList();
                if (linhas.Count * tamanho * Entrelinha <= areaTexto.Height || tamanho <= TamanhoMinimo) break;
                tamanho -= 0.25;
            }
            var passo = tamanho * Entrelinha;
            var y = areaTexto.Y + Math.Max(0, (areaTexto.Height - linhas.Count * passo) / 2);
            foreach (var linha in linhas)
            {
                gfx.DrawString(linha, fonte, XBrushes.Black, new XRect(areaTexto.X, y, areaTexto.Width, passo), XStringFormats.CenterLeft);
                y += passo;
            }
        }

        static void DesenharLogo(XGraphics gfx, XRect area, LogoAssinatura logo)
        {
            XImage imagem;
            try { imagem = XImage.FromStream(new MemoryStream(logo.Imagem, writable: false)); }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or NotSupportedException or IOException) { return; } // sem a imagem, só o texto
            using (imagem)
            {
                var fw = Math.Max(0.01, 1 - logo.CorteEsquerdo - logo.CorteDireito);
                var fh = Math.Max(0.01, 1 - logo.CorteTopo - logo.CorteBaixo);
                // O pedaço recortado, em pontos da própria imagem, cabe inteiro na área sem deformar.
                double pw = imagem.PointWidth * fw, ph = imagem.PointHeight * fh;
                var escala = Math.Min(area.Width / pw, area.Height / ph);
                double w = pw * escala, h = ph * escala;
                var destino = new XRect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
                var estado = gfx.Save();
                gfx.IntersectClip(destino);
                gfx.DrawImage(imagem, destino.X - logo.CorteEsquerdo * w / fw, destino.Y - logo.CorteTopo * h / fh, w / fw, h / fh);
                gfx.Restore(estado);
            }
        }

        /// <summary>Quebra a frase entre palavras para caber na largura (uma palavra maior que a linha fica sozinha).</summary>
        static IEnumerable<string> Quebrar(XGraphics gfx, XFont fonte, string frase, double largura)
        {
            var atual = "";
            foreach (var palavra in frase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
                if (atual.Length > 0 && gfx.MeasureString(tentativa, fonte).Width > largura)
                {
                    yield return atual;
                    atual = palavra;
                }
                else atual = tentativa;
            }
            if (atual.Length > 0) yield return atual;
        }
    }
}
