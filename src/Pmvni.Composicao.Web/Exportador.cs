using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pmvni.Composicao;

namespace Pmvni.Composicao.Web;

/// <summary>Exportação do relatório em PDF: compõe num <see cref="HostOculto"/> novo, recusa se houver estouro, imprime o
/// <b>mesmo</b> DOM composto (sem recompor), confere páginas do PDF = folhas e grava o recibo do editor ao lado do PDF.</summary>
public static class Exportador
{
    const int LarguraHostPx = 1024, AlturaHostPx = 1400;
    const int VersaoRecibo = 1;

    /// <summary>Recibo do PDF: <c>relatorio.pdf</c> → <c>relatorio.recibo-editor.json</c> (como o <c>DADOS.recibo.json</c> do kit).</summary>
    public static string CaminhoReciboDe(string caminhoPdf) => Path.ChangeExtension(Path.GetFullPath(caminhoPdf), ".recibo-editor.json");

    /// <summary>Exporta <paramref name="r"/> para <paramref name="caminhoPdf"/>. <paramref name="caminhoDados"/> é o dados.json
    /// que gerou o render (o recibo leva o sha256 dos bytes dele); <paramref name="modelo"/>, o modelo oficial verificado
    /// (padrão: <see cref="ModeloOficial.Carregar"/>). Com estouro no plano, lança <see cref="ExportacaoRecusadaException"/>
    /// sem gravar nada. PDF e recibo só são gravados depois de conferidos.</summary>
    public static async Task<ResultadoExportacao> ExportarAsync(ResultadoRender r, ConfigFolhas cfg, string caminhoPdf, string caminhoDados,
        ModeloCarregado? modelo = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentException.ThrowIfNullOrEmpty(caminhoPdf);
        ArgumentException.ThrowIfNullOrEmpty(caminhoDados);
        if (!r.Ok) throw new ArgumentException("Render com erros de conferência não pode ser exportado: " + string.Join("; ", r.Erros), nameof(r));
        modelo ??= ModeloOficial.Carregar();
        var dados = await File.ReadAllBytesAsync(caminhoDados);
        var pdfFinal = Path.GetFullPath(caminhoPdf);
        var reciboFinal = CaminhoReciboDe(pdfFinal);

        byte[] pdf;
        PlanoQuebras plano;
        int folhas;
        string webview2;
        await using (var host = await HostOculto.CriarAsync(LarguraHostPx, AlturaHostPx))
        {
            var doc = host.Documento;
            await doc.CarregarAsync(r, cfg);
            var c = await doc.ComporDetalhadoAsync();
            if (c.Plano.Estouros.Count > 0) throw new ExportacaoRecusadaException(c.Plano.Estouros);
            plano = c.Plano;
            folhas = c.Folhas;
            webview2 = await doc.VersaoWebView2Async();
            pdf = await doc.ImprimirPdfAsync(); // o mesmo DOM composto: nada é recomposto entre a composição e a impressão
        }

        var info = InfoPdf.Ler(pdf);
        if (info.Paginas != folhas)
            throw new InvalidDataException($"O PDF saiu com {info.Paginas} página(s) para {folhas} folha(s) compostas; nada foi gravado.");
        var sha256Pdf = Hex(SHA256.HashData(pdf));

        Directory.CreateDirectory(Path.GetDirectoryName(pdfFinal)!);
        await GravarAsync(pdfFinal, pdf);
        var recibo = new
        {
            recibo_versao = VersaoRecibo,
            modelo_revisao = int.Parse(modelo.Revisao, CultureInfo.InvariantCulture),
            sha256_modelo = new SortedDictionary<string, string>(modelo.Sha256.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal),
            sha256_dados = Hex(SHA256.HashData(dados)),
            sha256_html = PlanoQuebras.Sha256De(r.Html),
            plano_versao = plano.Versao,
            webview2,
            sha256_pdf = sha256Pdf,
            folhas,
            gerado_em = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            // Tamanho medido no MediaBox do PDF (não faz parte da lista do brief; registrado para a calibração da página).
            pagina_mm = new { largura = Math.Round(info.LarguraMm, 3), altura = Math.Round(info.AlturaMm, 3) },
        };
        await GravarAsync(reciboFinal, JsonSerializer.SerializeToUtf8Bytes(recibo, new JsonSerializerOptions { WriteIndented = true }));
        return new ResultadoExportacao(pdfFinal, reciboFinal, folhas, info.LarguraMm, info.AlturaMm, sha256Pdf, plano);
    }

    /// <summary>Exportação final: renderiza de novo SEM rascunho (a conferência do kit recusa campos entre colchetes ainda não
    /// preenchidos) e só então exporta. Com erros de conferência lança <see cref="ConferenciaFinalException"/> sem gravar nada.
    /// A tela pode abrir em rascunho (é só uma visão), mas o PDF final nunca sai com "[...]".</summary>
    public static async Task<ResultadoExportacao> ExportarFinalAsync(string caminhoDados, ConfigFolhas cfg, string caminhoPdf, ModeloCarregado? modelo = null)
    {
        modelo ??= ModeloOficial.Carregar();
        var final = await Task.Run(() => RelatorioRenderer.Renderizar(caminhoDados, new OpcoesRender(false, false), modelo));
        if (!final.Ok) throw new ConferenciaFinalException(final.Erros);
        return await ExportarAsync(final, cfg, caminhoPdf, caminhoDados, modelo);
    }

    static string Hex(byte[] b) => Convert.ToHexStringLower(b);

    /// <summary>Grava num temporário da mesma pasta e troca: o arquivo final nunca fica pela metade.</summary>
    static async Task GravarAsync(string caminho, byte[] conteudo)
    {
        var temporario = caminho + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporario, conteudo);
            File.Move(temporario, caminho, overwrite: true);
        }
        finally { if (File.Exists(temporario)) File.Delete(temporario); }
    }
}

/// <summary>PDF exportado: caminhos, folhas (= páginas), tamanho da página medido no PDF (mm), sha256 do PDF e o plano usado.</summary>
public sealed record ResultadoExportacao(string CaminhoPdf, string CaminhoRecibo, int Folhas, double LarguraPaginaMm, double AlturaPaginaMm,
    string Sha256Pdf, PlanoQuebras Plano);

/// <summary>Exportação final recusada: a conferência do kit (sem rascunho) achou erros, como campos [ ] não preenchidos.</summary>
public sealed class ConferenciaFinalException(IReadOnlyList<string> erros) : InvalidOperationException("Exportação recusada pela conferência do kit: " + string.Join(" ", erros))
{
    public IReadOnlyList<string> Erros { get; } = erros;
}

/// <summary>Exportação recusada porque o plano tem estouros (bloco maior que a página). A mensagem cita só folha, ref e
/// medidas, nunca o texto do conteúdo.</summary>
public sealed class ExportacaoRecusadaException(IReadOnlyList<Estouro> estouros) : InvalidOperationException(Mensagem(estouros))
{
    public IReadOnlyList<Estouro> Estouros { get; } = estouros;

    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    static string Mensagem(IReadOnlyList<Estouro> estouros) =>
        "Exportação recusada: " + string.Join(" ", estouros.Select(e =>
        {
            var bloco = e.Ref is null ? "Bloco sem identificação" : $"Bloco '{e.Ref}'";
            return e.LarguraMm is { } l
                ? string.Format(Br, "{0} excede a página {1} em largura ({2:0.#} mm, além da redução máxima de impressão).", bloco, e.Folha, l)
                : string.Format(Br, "{0} excede a página {1} (altura do conteúdo: {2:0.#} mm).", bloco, e.Folha, e.AlturaMm);
        }));
}

/// <summary>Leitura mínima de um PDF (sem dependências): número de páginas e tamanho (MediaBox) em mm. Conta os objetos
/// <c>/Type /Page</c> e confere com o <c>/Count</c> da raiz <c>/Pages</c>; recusa o que não sabe ler (fluxos de objetos,
/// páginas de tamanhos diferentes, contagens que não batem) com <see cref="InvalidDataException"/>.</summary>
public sealed record InfoPdf(int Paginas, double LarguraMm, double AlturaMm)
{
    static readonly Regex RePagina = new(@"/Type\s*/Page(?![A-Za-z0-9])", RegexOptions.CultureInvariant);
    static readonly Regex ReNoPaginas = new(@"/Type\s*/Pages(?![A-Za-z0-9])", RegexOptions.CultureInvariant);
    static readonly Regex Contagem = new(@"/Count\s+(\d+)", RegexOptions.CultureInvariant);
    static readonly Regex Caixa = new(@"/MediaBox\s*\[\s*([-+\d.]+)\s+([-+\d.]+)\s+([-+\d.]+)\s+([-+\d.]+)\s*\]", RegexOptions.CultureInvariant);
    static readonly Regex Objeto = new(@"\d+\s+\d+\s+obj\b(.*?)\bendobj", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    const double MmPorPt = 25.4 / 72;

    public static InfoPdf Ler(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var texto = Encoding.Latin1.GetString(pdf);
        if (!texto.StartsWith("%PDF-", StringComparison.Ordinal)) throw new InvalidDataException("Não é um PDF.");
        if (texto.Contains("/ObjStm", StringComparison.Ordinal))
            throw new InvalidDataException("PDF com fluxos de objetos (/ObjStm): o leitor mínimo não sabe contar as páginas.");
        var paginas = RePagina.Count(texto);
        // /Count da raiz: o maior entre os nós /Pages (a raiz soma todos).
        var contagem = Objeto.Matches(texto).Select(m => m.Groups[1].Value).Where(o => ReNoPaginas.IsMatch(o))
            .Select(o => Contagem.Match(o)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(-1).Max();
        if (paginas == 0 || contagem != paginas)
            throw new InvalidDataException($"Contagem de páginas do PDF incoerente: {paginas} objetos /Page, /Count {contagem}.");
        var caixas = Caixa.Matches(texto).Select(m =>
        {
            double V(int i) => double.Parse(m.Groups[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            return (L: Math.Abs(V(3) - V(1)) * MmPorPt, A: Math.Abs(V(4) - V(2)) * MmPorPt);
        }).ToList();
        if (caixas.Count == 0) throw new InvalidDataException("PDF sem /MediaBox.");
        var (l, a) = caixas[0];
        if (caixas.Any(c => Math.Abs(c.L - l) > 0.01 || Math.Abs(c.A - a) > 0.01))
            throw new InvalidDataException("O PDF tem páginas de tamanhos diferentes.");
        return new InfoPdf(paginas, l, a);
    }
}
