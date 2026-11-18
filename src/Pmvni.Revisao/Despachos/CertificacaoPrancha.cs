using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Pmvni.Revisao;

public static class CertificacaoPrancha
{
    public const string Razao = "Gerado pela Prefeitura Municipal de Venda Nova do Imigrante";
    const string Recurso = "Pmvni.CertificadoEmissaoLocal.cer";

    public static X509Certificate2 CertificadoPublico()
    {
        using var fluxo = typeof(CertificacaoPrancha).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException("Certificado público institucional ausente.");
        using var bytes = new MemoryStream();
        fluxo.CopyTo(bytes);
        return X509CertificateLoader.LoadCertificate(bytes.ToArray());
    }

    public const string PacoteRede = @"Z:\Engenharia\KIT DE TREINAMENTO DE IA (PMVNI)\Certificado de emissão\PMVNI-Emissao.pfx";

    public static bool DisponivelNesteWindows()
    {
        using var publico = CertificadoPublico();
        try
        {
            using var instalado = CertificadoDigital.AbrirDoWindows(publico.Thumbprint);
            return instalado.Certificado.RawData.AsSpan().SequenceEqual(publico.RawData);
        }
        catch (AssinaturaDigitalException) { return false; }
    }

    public static void InstalarNesteWindows(string senha)
    {
        using var publico = CertificadoPublico();
        var pacote = File.ReadAllBytes(PacoteRede);
        using var teste = X509CertificateLoader.LoadPkcs12(pacote, senha, X509KeyStorageFlags.EphemeralKeySet);
        if (!teste.RawData.AsSpan().SequenceEqual(publico.RawData) || CertificadoDigital.Problema(teste, DateTimeOffset.Now) is not null)
            throw new AssinaturaDigitalException("O pacote do kit não corresponde ao certificado de emissão válido do aplicativo.");
        using var certificado = X509CertificateLoader.LoadPkcs12(pacote, senha,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificado);
    }

    public static byte[] Emitir(PdfAssinado visto, DateTimeOffset agora)
    {
        using var publico = CertificadoPublico();
        using var titular = CertificadoDigital.AbrirDoWindows(publico.Thumbprint);
        return Certificar(visto, agora, titular, publico);
    }

    internal static byte[] Certificar(PdfAssinado visto, DateTimeOffset agora, CertificadoAberto titular, X509Certificate2 publico)
    {
        if (!titular.Certificado.RawData.AsSpan().SequenceEqual(publico.RawData))
            throw new AssinaturaDigitalException("A identidade do emissor diverge do certificado institucional.");
        if (CertificadoDigital.Problema(titular.Certificado, agora) is { } motivo) throw new AssinaturaDigitalException(motivo);
        using var cadeia = new X509Chain();
        cadeia.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        cadeia.ChainPolicy.CustomTrustStore.Add(publico);
        cadeia.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        cadeia.ChainPolicy.VerificationTime = agora.LocalDateTime;
        if (!cadeia.Build(titular.Certificado)) throw new AssinaturaDigitalException("A cadeia do emissor local é inválida.");

        using var entrada = new MemoryStream(visto.Pdf, writable: false);
        using var doc = PdfReader.Open(entrada, PdfDocumentOpenMode.Modify);
        var catalogo = doc.Internals.Catalog;
        var campos = catalogo.Elements.GetDictionary("/AcroForm")?.Elements.GetArray("/Fields");
        if (catalogo.Elements.ContainsKey("/Perms") || campos?.Elements.Count != 1)
            throw new AssinaturaDigitalException("A prancha precisa conter apenas o visto do analista antes da certificação.");
        var campoVisto = campos.Elements.GetDictionary(0);
        if (campoVisto?.Elements.GetDictionary("/V")?.Elements.GetString("/Reason") != "Visto do Analista Técnico")
            throw new AssinaturaDigitalException("O visto do analista não foi encontrado na prancha.");
        var anotacoesVisto = doc.Pages[visto.Local.Pagina].Elements.GetArray("/Annots");
        var indice = Enumerable.Range(0, anotacoesVisto?.Elements.Count ?? 0)
            .Single(i => ReferenceEquals(anotacoesVisto!.Elements.GetDictionary(i), campoVisto));
        anotacoesVisto!.Elements.RemoveAt(indice);
        campos.Elements.Clear();
        // A assinatura de certificação deve ser a primeira do PDF externo. A via exata
        // assinada pelo analista permanece verificável no anexo e a aparência na página.
        doc.AddEmbeddedFile("Prancha com visto digital do analista.pdf", new MemoryStream(visto.Pdf, writable: false));

        var sig = new PdfDictionary(doc);
        doc.Internals.AddObject(sig);
        sig.Elements.SetName("/Type", "/Sig");
        sig.Elements.SetName("/Filter", "/Adobe.PPKLite");
        sig.Elements.SetName("/SubFilter", "/ETSI.CAdES.detached");
        sig.Elements.SetString("/Reason", Razao);
        sig.Elements.SetString("/Name", titular.Info.Titular);
        sig.Elements.SetString("/M", "D:" + agora.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z");
        var parametros = new PdfDictionary(doc);
        parametros.Elements.SetName("/Type", "/TransformParams");
        parametros.Elements.SetName("/V", "/1.2");
        parametros.Elements.SetInteger("/P", 1);
        var referencia = new PdfDictionary(doc);
        referencia.Elements.SetName("/Type", "/SigRef");
        referencia.Elements.SetName("/TransformMethod", "/DocMDP");
        referencia.Elements["/TransformParams"] = parametros;
        referencia.Elements["/Data"] = catalogo.Reference!;
        sig.Elements["/Reference"] = new PdfArray(doc, [referencia]);
        var permissoes = new PdfDictionary(doc);
        permissoes.Elements["/DocMDP"] = sig.Reference!;
        catalogo.Elements["/Perms"] = permissoes;

        // Marcadores únicos de comprimento fixo mantêm intactas as posições do xref.
        var marcador = "<" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16384)) + ">";
        const string faixa = "[0 1999999991 1999999992 1999999993]";
        sig.Elements["/Contents"] = new PdfLiteral(marcador);
        sig.Elements["/ByteRange"] = new PdfLiteral(faixa);
        var pagina = doc.Pages[doc.PageCount - 1];
        var rect = AparenciaNoCanto(doc, pagina, agora);
        var widget = new PdfDictionary(doc);
        doc.Internals.AddObject(widget);
        widget.Elements.SetName("/Type", "/Annot");
        widget.Elements.SetName("/Subtype", "/Widget");
        widget.Elements.SetName("/FT", "/Sig");
        widget.Elements.SetString("/T", "CertificacaoInstitucional");
        widget.Elements.SetInteger("/F", 4);
        widget.Elements["/V"] = sig.Reference!;
        widget.Elements["/P"] = pagina.Reference!;
        widget.Elements["/Rect"] = rect;
        campos.Elements.Add(widget.Reference!);
        var anotacoes = pagina.Elements.GetArray("/Annots");
        if (anotacoes is null) pagina.Elements["/Annots"] = anotacoes = new PdfArray(doc);
        anotacoes.Elements.Add(widget.Reference!);
        using var saida = new MemoryStream();
        doc.Save(saida);
        var pdf = saida.ToArray();
        var pos = Unico(pdf, marcador);
        var fim = pos + marcador.Length;
        var byteRange = FormattableString.Invariant($"[0 {pos,10} {fim,10} {pdf.Length - fim,10}]");
        Encoding.ASCII.GetBytes(byteRange).CopyTo(pdf, Unico(pdf, faixa));
        var protegido = new byte[pdf.Length - marcador.Length];
        Array.Copy(pdf, protegido, pos);
        Array.Copy(pdf, fim, protegido, pos, pdf.Length - fim);
        var cms = new SignedCms(new ContentInfo(protegido), detached: true);
        var signatario = new CmsSigner(titular.Certificado) { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.EndCertOnly };
        signatario.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), AssinaturaPdf.CertificadoAssinanteV2(publico)));
        cms.ComputeSignature(signatario);
        var der = cms.Encode();
        if (der.Length * 2 > marcador.Length - 2) throw new AssinaturaDigitalException("Espaço insuficiente para a assinatura institucional.");
        pdf.AsSpan(pos + 1, marcador.Length - 2).Fill((byte)'0');
        Encoding.ASCII.GetBytes(Convert.ToHexString(der)).CopyTo(pdf, pos + 1);
        var verificacao = new SignedCms(new ContentInfo(protegido), detached: true);
        verificacao.Decode(der);
        verificacao.CheckSignature(true);
        return pdf;
    }

    static int Unico(byte[] pdf, string marcador)
    {
        var padrao = Encoding.ASCII.GetBytes(marcador);
        int pos = pdf.AsSpan().IndexOf(padrao);
        if (pos < 0 || pdf.AsSpan(pos + padrao.Length).IndexOf(padrao) >= 0)
            throw new AssinaturaDigitalException("Marcador de assinatura ambíguo no PDF.");
        return pos;
    }

    internal static PdfRectangle AparenciaNoCanto(PdfDocument doc, PdfPage pagina, DateTimeOffset agora)
    {
        var media = pagina.MediaBox;
        var crop = pagina.Elements.ContainsKey("/CropBox") ? pagina.CropBox : media;
        if (crop.IsZero) crop = media;
        var l = Math.Max(media.X1, crop.X1);
        var b = Math.Max(media.Y1, crop.Y1);
        var r = Math.Min(media.X2, crop.X2);
        var t = Math.Min(media.Y2, crop.Y2);
        var unidade = pagina.Elements.ContainsKey("/UserUnit") ? pagina.Elements.GetReal("/UserUnit") : 1;
        var giro = (pagina.Rotate % 360 + 360) % 360;
        if (!double.IsFinite(unidade) || unidade <= 0 || r <= l || t <= b || giro % 90 != 0)
            throw new AssinaturaDigitalException("Geometria inválida na última página.");
        var escala = Math.Min(1 / unidade, Math.Min((giro % 180 == 0 ? r-l : t-b)/220, (giro % 180 == 0 ? t-b : r-l)/34));
        var w = 220 * escala;
        var h = 34 * escala;
        var (a, c, d, e, x, y) = giro switch
        {
            0 => (escala, 0d, 0d, escala, r-w, b),
            90 => (0d, escala, -escala, 0d, r, t-w),
            180 => (-escala, 0d, 0d, -escala, l+w, t),
            _ => (0d, -escala, escala, 0d, l, b+w),
        };
        var fonte = new PdfDictionary(doc);
        fonte.Elements.SetName("/Type", "/Font");
        fonte.Elements.SetName("/Subtype", "/Type1");
        fonte.Elements.SetName("/BaseFont", "/Helvetica");
        fonte.Elements.SetName("/Encoding", "/WinAnsiEncoding");
        var fontes = pagina.Resources.Elements.GetDictionary("/Font");
        if (fontes is null) pagina.Resources.Elements["/Font"] = fontes = new PdfDictionary(doc);
        string id = "/PMVNI" + Guid.NewGuid().ToString("N");
        fontes.Elements[id] = fonte;
        var desenho = FormattableString.Invariant($"q {a:0.########} {c:0.########} {d:0.########} {e:0.########} {x:0.########} {y:0.########} cm 1 g 0 0 220 34 re f 0 g BT {id} 8 Tf 3 22 Td (Prefeitura Municipal de Venda Nova do Imigrante) Tj 0 -12 Td (Assinatura digital - {agora:dd/MM/yyyy HH:mm zzz}) Tj ET Q");
        pagina.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(desenho));
        return giro switch
        {
            0 => new PdfRectangle(new PdfSharp.Drawing.XRect(r-w, b, w, h)),
            90 => new PdfRectangle(new PdfSharp.Drawing.XRect(r-h, t-w, h, w)),
            180 => new PdfRectangle(new PdfSharp.Drawing.XRect(l, t-h, w, h)),
            _ => new PdfRectangle(new PdfSharp.Drawing.XRect(l, b, h, w)),
        };
    }
}
