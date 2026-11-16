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

/// <summary>De onde veio a figura da leitura padronizada (correções 2026-11-15).</summary>
public static class OrigemFigura
{
    /// <summary>Imagem do PDF (objeto de imagem, imagem embutida ou máscara), recortada da página desenhada pelo PDF.js.</summary>
    public const string Imagem = "imagem";
    /// <summary>Desenho vetorial (linhas, áreas) com pouco texto dentro, recortado da página desenhada.</summary>
    public const string Desenho = "desenho";
    /// <summary>A página inteira desenhada: o conteúdo gráfico dela não pôde ser localizado ("Conteúdo gráfico da página N").</summary>
    public const string Pagina = "pagina";
    /// <summary>Nem a página inteira pôde ser desenhada: a leitura mostra só o aviso e manda ver o PDF original.</summary>
    public const string Falhou = "falhou";

    public static bool Valida(string? origem) => origem is Imagem or Desenho or Pagina or Falhou;
}

/// <summary>Uma figura de uma página do PDF para a leitura padronizada (correções 2026-11-15): a imagem recortada da página desenhada
/// (PNG ou JPEG; vazia quando <see cref="Origem"/> é <see cref="OrigemFigura.Falhou"/>), onde ela fica na página (<see cref="X"/>,
/// <see cref="Y"/>, <see cref="W"/>, <see cref="H"/>: frações de 0 a 1 a partir do canto de cima à esquerda) e <see cref="Linha"/>, o índice
/// da linha do texto da página (o texto do índice, separado por <c>\n</c>) antes da qual a figura entra. <see cref="Largura"/> é a largura
/// da região em pontos do PDF (1/72").</summary>
public sealed record FiguraPdf(int Pagina, int Linha, double X, double Y, double W, double H, string Origem, string Tipo, byte[] Dados, double Largura)
{
    /// <summary>Tipos aceitos para <see cref="Dados"/>.</summary>
    public static bool TipoValido(string? tipo) => tipo is "image/png" or "image/jpeg";

    /// <summary>Os bytes começam pela assinatura do tipo declarado (PNG ou JPEG).</summary>
    public static bool AssinaturaConfere(string tipo, ReadOnlySpan<byte> dados) => tipo switch
    {
        "image/png" => dados.Length > 8 && dados[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/jpeg" => dados.Length > 3 && dados[0] == 0xFF && dados[1] == 0xD8 && dados[2] == 0xFF,
        _ => false,
    };
}

/// <summary>Lê as figuras de um PDF com o PDF.js (correções 2026-11-15): imagens, desenhos e, quando não dá para localizá-los, a página
/// inteira. Separado de <see cref="IExtratorPdf"/>: só a leitura padronizada usa, sob demanda, e o resultado fica guardado no acervo.</summary>
public interface IExtratorFiguras
{
    Task<IReadOnlyList<FiguraPdf>> ExtrairFigurasAsync(string caminhoPdf, CancellationToken ct);
}
