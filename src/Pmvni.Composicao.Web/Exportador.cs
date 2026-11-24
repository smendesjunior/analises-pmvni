using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// (padrão: <see cref="ModeloOficial.Carregar"/>); <paramref name="rascunho"/> vai para o recibo (o render aceitou campos
    /// [ ] em aberto). Com estouro no plano, lança <see cref="ExportacaoRecusadaException"/> sem gravar nada. PDF e recibo só
    /// são gravados depois de conferidos. Rota do Verificador e dos testes; o app usa <see cref="ExportarFinalAsync"/>.</summary>
    public static async Task<ResultadoExportacao> ExportarAsync(ResultadoRender r, ConfigFolhas cfg, string caminhoPdf, string caminhoDados,
        ModeloCarregado? modelo = null, bool rascunho = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(caminhoDados);
        return await ExportarRenderAsync(r, cfg, caminhoPdf, await File.ReadAllBytesAsync(caminhoDados), modelo ?? ModeloOficial.Carregar(), rascunho, null, null);
    }

    /// <summary>Compõe <paramref name="r"/> num host novo e, se <paramref name="planoTela"/> vier, recusa com
    /// <see cref="ExportacaoDivergenteException"/> o que não for o que a tela mostrou (outro HTML, outros inícios de folha ou
    /// outra escala). <paramref name="dados"/>: os bytes exatos que geraram <paramref name="r"/> (sha256 do recibo);
    /// <paramref name="sha256Pacote"/>, quando vem, entra no recibo (o pacote .rpmvni de onde os dados efetivos saíram).</summary>
    static async Task<ResultadoExportacao> ExportarRenderAsync(ResultadoRender r, ConfigFolhas cfg, string caminhoPdf, byte[] dados,
        ModeloCarregado modelo, bool rascunho, PlanoQuebras? planoTela, string? sha256Pacote)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(cfg);
        ArgumentException.ThrowIfNullOrEmpty(caminhoPdf);
        if (!r.Ok) throw new ArgumentException("Render com erros de conferência não pode ser exportado: " + string.Join("; ", r.Erros), nameof(r));
        var shaHtml = PlanoQuebras.Sha256De(r.Html);
        if (planoTela is not null && !string.Equals(planoTela.Sha256Html, shaHtml, StringComparison.Ordinal))
            throw new ExportacaoDivergenteException(ExportacaoDivergenteException.ArquivoMudou);
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
            // A tela é o que o usuário viu: o PDF sai com as mesmas folhas ou não sai.
            if (planoTela is not null && !MesmasFolhas(planoTela, c.Plano))
                throw new ExportacaoDivergenteException(ExportacaoDivergenteException.ComposicaoDiferente);
            if (c.Plano.Estouros.Count > 0) throw new ExportacaoRecusadaException(c.Plano.Estouros);
            plano = c.Plano;
            folhas = c.Folhas;
            webview2 = await doc.VersaoWebView2Async();
            var campos = rascunho ? null : await doc.CamposParaImpressaoAsync();
            pdf = await doc.ImprimirPdfAsync(); // o mesmo DOM composto: nada é recomposto entre a composição e a impressão
            if (campos is not null) await Task.Run(() => ConferenciaImpressao.Conferir(pdf, campos));
        }

        var info = InfoPdf.Ler(pdf);
        if (info.Paginas != folhas)
            throw new InvalidDataException($"O PDF saiu com {info.Paginas} página(s) para {folhas} folha(s) compostas; nada foi gravado.");
        var sha256Pdf = Hex(SHA256.HashData(pdf));

        Directory.CreateDirectory(Path.GetDirectoryName(pdfFinal)!);
        await GravarAsync(pdfFinal, pdf);
        // Dictionary preserva a ordem de inserção (só se acrescenta): o recibo sai com as chaves nesta ordem.
        var recibo = new Dictionary<string, object?>
        {
            ["recibo_versao"] = VersaoRecibo,
            ["modelo_revisao"] = int.Parse(modelo.Revisao, CultureInfo.InvariantCulture),
            ["sha256_modelo"] = new SortedDictionary<string, string>(modelo.Sha256.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal),
            ["sha256_dados"] = Hex(SHA256.HashData(dados)),
        };
        if (sha256Pacote is not null) recibo["sha256_pacote"] = sha256Pacote;
        recibo["sha256_html"] = shaHtml;
        recibo["rascunho"] = rascunho;
        recibo["plano_versao"] = plano.Versao;
        recibo["webview2"] = webview2;
        recibo["sha256_pdf"] = sha256Pdf;
        recibo["folhas"] = folhas;
        recibo["gerado_em"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        // Tamanho medido no MediaBox do PDF (não faz parte da lista do brief; registrado para a calibração da página).
        recibo["pagina_mm"] = new { largura = Math.Round(info.LarguraMm, 3), altura = Math.Round(info.AlturaMm, 3) };
        await GravarAsync(reciboFinal, JsonSerializer.SerializeToUtf8Bytes(recibo, new JsonSerializerOptions { WriteIndented = true }));
        return new ResultadoExportacao(pdfFinal, reciboFinal, folhas, info.LarguraMm, info.AlturaMm, sha256Pdf, plano);
    }

    /// <summary>Exportação final (a do app): lê o dados.json UMA vez, renderiza esses bytes SEM rascunho (a conferência do kit
    /// recusa campos entre colchetes ainda não preenchidos) e só então exporta; o recibo leva o sha256 desses mesmos bytes.
    /// <paramref name="planoTela"/> é o plano das folhas que a tela mostra (D2: o PDF é a impressão delas): se o HTML final
    /// não for o da tela (o arquivo ou uma imagem mudou depois de aberto) ou a composição da exportação não der os mesmos
    /// inícios de folha e a mesma escala, lança <see cref="ExportacaoDivergenteException"/>. Com erros de conferência lança
    /// <see cref="ConferenciaFinalException"/>. Em toda recusa, nada é gravado. A tela pode abrir em rascunho (é só uma
    /// visão), mas o PDF final nunca sai com "[...]".</summary>
    public static async Task<ResultadoExportacao> ExportarFinalAsync(string caminhoDados, ConfigFolhas cfg, string caminhoPdf, PlanoQuebras planoTela,
        ModeloCarregado? modelo = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(caminhoDados);
        var bytes = await Task.Run(() => File.ReadAllBytes(Path.GetFullPath(caminhoDados)));
        return await ExportarFinalAsync(bytes, caminhoDados, cfg, caminhoPdf, planoTela, modelo);
    }

    /// <summary>Como a versão por caminho, com os bytes do dados.json já em mãos (o dados.json efetivo gerado do pacote, que não
    /// é um arquivo): <paramref name="caminhoDados"/> só dá a pasta das imagens (não precisa existir). O recibo leva o sha256 de
    /// <paramref name="bytesDados"/> e, se informado, <c>sha256_pacote</c> (<paramref name="sha256Pacote"/>: o .rpmvni de origem).</summary>
    public static async Task<ResultadoExportacao> ExportarFinalAsync(byte[] bytesDados, string caminhoDados, ConfigFolhas cfg, string caminhoPdf,
        PlanoQuebras planoTela, ModeloCarregado? modelo = null, string? sha256Pacote = null)
    {
        ArgumentNullException.ThrowIfNull(bytesDados);
        ArgumentException.ThrowIfNullOrEmpty(caminhoDados);
        ArgumentNullException.ThrowIfNull(planoTela);
        modelo ??= ModeloOficial.Carregar();
        var m = modelo;
        // Com o pacote de origem (o app), as imagens só podem vir da pasta de trabalho dele (assets\), nunca de fora.
        var op = new OpcoesRender(false, false, ImagensSoNaPasta: sha256Pacote is not null);
        var final = await Task.Run(() => RelatorioRenderer.Renderizar(bytesDados, caminhoDados, op, m));
        if (!final.Ok) throw new ConferenciaFinalException(final.Erros);
        return await ExportarRenderAsync(final, cfg, caminhoPdf, bytesDados, m, rascunho: false, planoTela, sha256Pacote);
    }

    /// <summary>Mesmas folhas: mesmo HTML, mesmos inícios (posição a posição) e mesma escala de impressão.</summary>
    internal static bool MesmasFolhas(PlanoQuebras tela, PlanoQuebras exportacao) =>
        string.Equals(tela.Sha256Html, exportacao.Sha256Html, StringComparison.Ordinal)
        && tela.Escala.Equals(exportacao.Escala)
        && tela.InicioDasFolhas.SequenceEqual(exportacao.InicioDasFolhas);

    static string Hex(byte[] b) => Convert.ToHexStringLower(b);

    /// <summary>Grava num temporário da mesma pasta e troca: o arquivo final nunca fica pela metade.</summary>
    static async Task GravarAsync(string caminho, byte[] conteudo)
    {
        var temporario = caminho + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(ArquivoGerado.Caminho(temporario), conteudo);
            File.Move(temporario, ArquivoGerado.Caminho(caminho), overwrite: true);
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

/// <summary>Exportação recusada porque o PDF não seria o que a tela mostra: o arquivo (ou uma imagem) mudou desde que foi
/// aberto, ou a composição da exportação deu outras folhas. A mensagem não cita conteúdo.</summary>
public sealed class ExportacaoDivergenteException(string mensagem) : InvalidOperationException(mensagem)
{
    public const string ArquivoMudou = "O arquivo mudou desde que foi aberto; reabra antes de exportar.";
    public const string ComposicaoDiferente = "A exportação compôs folhas diferentes das que estão na tela; reabra antes de exportar.";
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
