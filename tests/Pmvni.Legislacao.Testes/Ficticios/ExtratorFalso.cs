using System.Security.Cryptography;

namespace Pmvni.Legislacao.Testes.Ficticios;

/// <summary>Extrator dos testes puros: reconhece o PDF fictício pelo conteúdo (sha256) e devolve o texto que o gerador pôs nele
/// (as linhas de cada página unidas por <c>\n</c>), <c>[""]</c> para a digitalizada, e recusa a com senha e a corrompida.</summary>
public sealed class ExtratorFalso : IExtratorPdf
{
    static readonly Dictionary<string, string> PorSha = PdfLegislacaoFicticio.Todos
        .ToDictionary(p => Convert.ToHexStringLower(SHA256.HashData(p.Value)), p => p.Key);

    /// <summary>Quando verdadeiro, a extração só termina quando é cancelada.</summary>
    public bool EsperarCancelamento { get; set; }

    public int Chamadas { get; private set; }

    public async Task<ResultadoExtracao> ExtrairAsync(string caminhoPdf, IProgress<(int Pagina, int Total)>? progresso, CancellationToken ct)
    {
        Chamadas++;
        if (EsperarCancelamento) await Task.Delay(Timeout.Infinite, ct);
        ct.ThrowIfCancellationRequested();
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(caminhoPdf)));
        if (!PorSha.TryGetValue(sha, out var chave)) throw new ExtracaoRecusadaException(FalhaExtracao.Invalido, "PDF desconhecido do gerador");
        if (chave.EndsWith("SENHA - FICTICIA.pdf", StringComparison.Ordinal)) throw new ExtracaoRecusadaException(FalhaExtracao.Senha, "PasswordException");
        if (chave.EndsWith("CORROMPIDO - FICTICIA.pdf", StringComparison.Ordinal) || chave.EndsWith("NAO-E-PDF - FICTICIA.pdf", StringComparison.Ordinal))
            throw new ExtracaoRecusadaException(FalhaExtracao.Invalido, "InvalidPDFException");
        string[] paginas = chave.EndsWith("DIGITALIZADA - FICTICIA.pdf", StringComparison.Ordinal)
            ? [""]
            : [.. PdfLegislacaoFicticio.Textos[chave].Select(linhas => string.Join("\n", linhas))];
        for (var i = 1; i <= paginas.Length; i++) progresso?.Report((i, paginas.Length));
        return new ResultadoExtracao(paginas);
    }
}
