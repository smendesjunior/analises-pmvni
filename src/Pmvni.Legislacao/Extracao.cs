namespace Pmvni.Legislacao;

/// <summary>Texto de cada página de um PDF, como o PDF.js entrega (itens unidos, fim de linha vira <c>\n</c>). Sem OCR.</summary>
public sealed record ResultadoExtracao(IReadOnlyList<string> TextoPorPagina)
{
    public int Paginas => TextoPorPagina.Count;

    /// <summary>Páginas (1-based) sem nenhuma letra nem dígito.</summary>
    public IReadOnlyList<int> PaginasSemTexto { get; } = Calcular(TextoPorPagina);

    static int[] Calcular(IReadOnlyList<string> paginas)
    {
        var lista = new List<int>();
        for (var i = 0; i < paginas.Count; i++)
            if (!TemLetraOuDigito(paginas[i])) lista.Add(i + 1);
        return [.. lista];
    }

    internal static bool TemLetraOuDigito(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s) if (char.IsLetterOrDigit(c)) return true;
        return false;
    }
}

public enum FalhaExtracao { Senha, Invalido, Falhou }

/// <summary>O leitor recusou o PDF (senha de abertura, arquivo danificado) ou falhou ao extrair.</summary>
public sealed class ExtracaoRecusadaException(FalhaExtracao falha, string detalhe) : Exception(detalhe)
{
    public FalhaExtracao Falha { get; } = falha;
    public string Detalhe { get; } = detalhe;
}

/// <summary>Lê as páginas e o texto de um PDF (o PDF.js, num WebView2 oculto). Uma extração por vez.</summary>
public interface IExtratorPdf
{
    Task<ResultadoExtracao> ExtrairAsync(string caminhoPdf, IProgress<(int Pagina, int Total)>? progresso, CancellationToken ct);
}
