using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Composicao;

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
    static readonly Regex FonteBase = new(@"/BaseFont\s*/([^\s/\[\]<>(){}%]+)", RegexOptions.CultureInvariant);
    static readonly Regex PrefixoSubconjunto = new(@"^[A-Z]{6}\+", RegexOptions.CultureInvariant);
    const double MmPorPt = 25.4 / 72;

    /// <summary>Os nomes de <c>/BaseFont</c> do PDF, sem o prefixo de subconjunto (<c>BAAAAA+Verdana</c> → <c>Verdana</c>), sem repetir, na
    /// ordem em que aparecem.</summary>
    public static IReadOnlyList<string> Fontes(byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var texto = Encoding.Latin1.GetString(pdf);
        var nomes = new List<string>();
        foreach (Match m in FonteBase.Matches(texto))
        {
            var nome = PrefixoSubconjunto.Replace(m.Groups[1].Value, "");
            if (nome.Length > 0 && !nomes.Contains(nome, StringComparer.Ordinal)) nomes.Add(nome);
        }
        return nomes;
    }

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
