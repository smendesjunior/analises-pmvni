using System.Globalization;
using System.Text;

namespace Pmvni.Revisao;

/// <summary>Os três caminhos de um despacho gravado (DOCX, PDF e recibo), nenhum deles existente.</summary>
public sealed record NomesLivres(string Docx, string Pdf, string Recibo);

/// <summary>Nomes dos arquivos do despacho, como o kit: <c>Despacho_&lt;Tipo&gt;_&lt;nº&gt;_&lt;ano&gt;_&lt;Nome&gt;</c>, sem nunca sobrescrever.</summary>
public static class NomesDespacho
{
    const int MaximoNome = 60;
    internal const string ExtensaoRecibo = ".recibo-editor.json";

    public static string Base(TipoDespacho t, int numero, int ano, string requerente) =>
        Base(t, numero.ToString(CultureInfo.InvariantCulture), ano, requerente);

    public static string Base(TipoDespacho t, string numero, int ano, string requerente)
    {
        ArgumentNullException.ThrowIfNull(requerente);
        var nome = Nome(requerente);
        var b = string.Create(CultureInfo.InvariantCulture, $"Despacho_{RegistroModelos.NomeNoArquivo(t)}_{CriacaoProcesso.Componente(numero)}_{ano}");
        return nome.Length > 0 ? b + "_" + nome : b;
    }

    /// <summary>Sem acentos; o que não é letra ou dígito ASCII separa palavras; cada palavra com inicial maiúscula, unidas por <c>_</c>, até
    /// 60 caracteres cortando na última palavra que cabe.</summary>
    static string Nome(string requerente)
    {
        var sb = new StringBuilder();
        foreach (var c in requerente.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : ' ');
        }
        var palavras = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()).ToList();
        var nome = "";
        foreach (var p in palavras)
        {
            var proximo = nome.Length == 0 ? p : nome + "_" + p;
            if (proximo.Length > MaximoNome) break;
            nome = proximo;
        }
        if (nome.Length == 0 && palavras.Count > 0) nome = palavras[0][..MaximoNome]; // uma palavra só, maior que o limite
        return nome;
    }

    /// <summary>O primeiro de <c>base</c>, <c>base (2)</c>, <c>base (3)</c>… em que nenhum dos três arquivos existe.</summary>
    public static NomesLivres Livres(string pasta, string nomeBase)
    {
        ArgumentNullException.ThrowIfNull(pasta);
        ArgumentNullException.ThrowIfNull(nomeBase);
        for (var i = 1; ; i++)
        {
            var b = i == 1 ? nomeBase : string.Create(CultureInfo.InvariantCulture, $"{nomeBase} ({i})");
            var n = new NomesLivres(Path.Combine(pasta, b + ".docx"), Path.Combine(pasta, b + ".pdf"), Path.Combine(pasta, b + ExtensaoRecibo));
            if (!Existe(n.Docx) && !Existe(n.Pdf) && !Existe(n.Recibo)) return n;
        }
    }

    static bool Existe(string caminho) => File.Exists(caminho) || Directory.Exists(caminho);
}
