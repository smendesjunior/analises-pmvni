using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Legislacao;

/// <summary>Os tipos de bloco da leitura padronizada de uma lei (correções 2026-11-15).</summary>
public enum TipoBlocoLei { Epigrafe, Ementa, Titulo, Capitulo, Secao, Subsecao, Artigo, Paragrafo, Inciso, Alinea, Texto }

/// <summary>Um bloco da leitura padronizada: o <see cref="Rotulo"/> ("Art. 1º", "§ 2º", "TÍTULO I"…) e o texto que segue.</summary>
public sealed record BlocoLei(TipoBlocoLei Tipo, string Rotulo, string Texto, int Pagina);

/// <summary>Processador interno da legislação importada (correções 2026-11-15): do texto extraído do PDF, página a página, monta a leitura
/// padronizada (epígrafe, ementa, títulos, capítulos, seções, artigos, parágrafos, incisos e alíneas), juntando as linhas quebradas pelo PDF
/// e tirando cabeçalhos e rodapés que se repetem nas páginas. Não muda o PDF: é só outra forma de ler o mesmo texto.</summary>
public static partial class LeituraPadronizada
{
    [GeneratedRegex(@"^(LEI(\s+COMPLEMENTAR|\s+ORDIN[ÁA]RIA)?|DECRETO(-LEI)?|RESOLU[ÇC][ÃA]O|PORTARIA|INSTRU[ÇC][ÃA]O\s+NORMATIVA|EMENDA)\b.*\b(N[º°O.]|N\.º)\s*[\d.]+.*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Epigrafe();

    [GeneratedRegex(@"^(T[ÍI]TULO|CAP[ÍI]TULO|SE[ÇC][ÃA]O|SUBSE[ÇC][ÃA]O)\s+([IVXLCDM]+|[ÚU]NIC[OA])\b\s*[-–—.:]?\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Divisao();

    [GeneratedRegex(@"^(Art(?:igo)?\.?\s*\d+(?:\s*[º°o]|\.)?(?:-[A-Z])?)\s*[-–—.]?\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Artigo();

    [GeneratedRegex(@"^((?:§\s*\d+\s*[º°o]?(?:-[A-Z])?)|Par[áa]grafo\s+[úu]nico)\s*[-–—.:]?\s*(.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Paragrafo();

    [GeneratedRegex(@"^([IVXLCDM]+)\s*[-–—)]\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Inciso();

    [GeneratedRegex(@"^([a-z])\)\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Alinea();

    [GeneratedRegex(@"^\s*(p[áa]g(ina)?\.?\s*)?\d+(\s*(de|/)\s*\d+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumeroDePagina();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    /// <summary>Monta os blocos a partir do texto de cada página (a 1ª página é a 1).</summary>
    public static IReadOnlyList<BlocoLei> Estruturar(IReadOnlyList<string> paginas)
    {
        ArgumentNullException.ThrowIfNull(paginas);
        var repetidas = LinhasRepetidas(paginas);
        var blocos = new List<BlocoLei>();
        TipoBlocoLei? atual = null;
        string rotulo = "";
        var texto = new StringBuilder();
        var paginaDoBloco = 1;
        var viuArtigo = false;
        var viuEpigrafe = false;

        void Fechar()
        {
            if (atual is { } t && (rotulo.Length > 0 || texto.Length > 0))
                blocos.Add(new BlocoLei(t, rotulo, Espacos().Replace(texto.ToString(), " ").Trim(), paginaDoBloco));
            atual = null;
            rotulo = "";
            texto.Clear();
        }

        void Abrir(TipoBlocoLei t, string r, string resto, int pagina)
        {
            Fechar();
            atual = t;
            rotulo = Espacos().Replace(r, " ").Trim();
            texto.Append(resto);
            paginaDoBloco = pagina;
        }

        for (var p = 0; p < paginas.Count; p++)
        {
            foreach (var bruta in (paginas[p] ?? "").Split('\n'))
            {
                var linha = Espacos().Replace(bruta, " ").Trim();
                if (linha.Length == 0 || NumeroDePagina().IsMatch(linha) || repetidas.Contains(linha)) continue;
                Match m;
                if (!viuEpigrafe && !viuArtigo && (m = Epigrafe().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Epigrafe, "", linha, p + 1);
                    viuEpigrafe = true;
                    Fechar();
                    atual = TipoBlocoLei.Ementa;
                    paginaDoBloco = p + 1;
                    continue;
                }
                if ((m = Divisao().Match(linha)).Success)
                {
                    var tipo = m.Groups[1].Value.ToUpperInvariant() switch
                    {
                        var s when s.StartsWith("T", StringComparison.Ordinal) => TipoBlocoLei.Titulo,
                        var s when s.StartsWith("C", StringComparison.Ordinal) => TipoBlocoLei.Capitulo,
                        var s when s.StartsWith("SUB", StringComparison.Ordinal) => TipoBlocoLei.Subsecao,
                        _ => TipoBlocoLei.Secao,
                    };
                    Abrir(tipo, m.Groups[1].Value.ToUpperInvariant() + " " + m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value, p + 1);
                    continue;
                }
                if ((m = Artigo().Match(linha)).Success)
                {
                    viuArtigo = true;
                    Abrir(TipoBlocoLei.Artigo, NormalizarArtigo(m.Groups[1].Value), m.Groups[2].Value, p + 1);
                    continue;
                }
                if (viuArtigo && (m = Paragrafo().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Paragrafo, m.Groups[1].Value, m.Groups[2].Value, p + 1);
                    continue;
                }
                if (viuArtigo && (m = Inciso().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Inciso, m.Groups[1].Value + " –", m.Groups[2].Value, p + 1);
                    continue;
                }
                if (viuArtigo && (m = Alinea().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Alinea, m.Groups[1].Value + ")", m.Groups[2].Value, p + 1);
                    continue;
                }
                // Linha de continuação: nome da divisão em linha própria (só maiúsculas) ou o texto que o PDF quebrou.
                if (atual is null) { Abrir(TipoBlocoLei.Texto, "", linha, p + 1); continue; }
                if (texto.Length > 0 && texto[^1] == '-' && char.IsLower(linha[0])) texto.Length--; // hifenização do PDF
                else if (texto.Length > 0) texto.Append(' ');
                texto.Append(linha);
            }
        }
        Fechar();
        return blocos;
    }

    static string NormalizarArtigo(string s)
    {
        var numero = new string(s.Where(char.IsDigit).ToArray());
        var sufixo = s.Contains('-', StringComparison.Ordinal) ? s[s.LastIndexOf('-')..] : "";
        return int.TryParse(numero, out var n) && n <= 9 ? $"Art. {n}º{sufixo}" : $"Art. {numero}{sufixo}";
    }

    /// <summary>Linhas curtas que aparecem em mais da metade das páginas (com 3 ou mais): cabeçalho ou rodapé.</summary>
    static HashSet<string> LinhasRepetidas(IReadOnlyList<string> paginas)
    {
        var r = new HashSet<string>(StringComparer.Ordinal);
        if (paginas.Count < 3) return r;
        var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pag in paginas)
            foreach (var l in (pag ?? "").Split('\n').Select(x => Espacos().Replace(x, " ").Trim()).Where(x => x.Length is > 0 and <= 120).Distinct())
                contagem[l] = contagem.GetValueOrDefault(l) + 1;
        foreach (var (l, n) in contagem)
            if (n * 2 > paginas.Count && !Artigo().IsMatch(l)) r.Add(l);
        return r;
    }
}
