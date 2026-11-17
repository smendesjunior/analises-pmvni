using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Resultado de uma leitura de pastas: os itens achados e os avisos (pasta ou arquivo ilegível não derruba a leitura).</summary>
public sealed record Leitura<T>(IReadOnlyList<T> Itens, IReadOnlyList<string> Avisos);

public sealed record Processo(string Pasta, string NomePasta, int? Numero, int? Ano, string? Requerente);

public sealed record ArquivoEncontrado(string Caminho, string Relativo, long Tamanho, DateTime AlteradoUtc);

public enum TipoRelatorio { Pacote, AnaliseIa }

public sealed record RelatorioEncontrado(string Caminho, TipoRelatorio Tipo, DateTime AlteradoUtc);

public enum TipoDespacho { Correcao, Aprovacao, NaoIdentificado }

public sealed record Despacho(string Pasta, int Numero, DateOnly Data, TipoDespacho Tipo, IReadOnlyList<string> Arquivos, Processo? Processo);

public sealed record DetalheProcesso(
    Processo Processo,
    IReadOnlyList<RelatorioEncontrado> Relatorios,
    IReadOnlyList<Despacho> Despachos,
    IReadOnlyList<ArquivoEncontrado> Projeto,
    IReadOnlyList<ArquivoEncontrado> Documentos,
    IReadOnlyList<string> Avisos);

/// <summary>Leitura das pastas de processos (<c>&lt;raiz&gt;\&lt;ano&gt;\&lt;nº&gt; - &lt;ano&gt; - &lt;requerente&gt;</c>). Só lê: nunca move, renomeia nem apaga.
/// Tudo síncrono, para rodar em <c>Task.Run</c>; o cancelamento lança <see cref="OperationCanceledException"/>.</summary>
public static class Processos
{
    const int NiveisMaximos = 3;

    static readonly Regex PastaAno = new(@"^\d{4}$", RegexOptions.CultureInvariant);
    static readonly Regex NomeProcesso = new(@"^(?<num>\d{1,6})\s*-\s*(?<ano>\d{4})\s+-\s+(?<req>.+)$", RegexOptions.CultureInvariant);
    static readonly Regex NomeDespacho = new(@"^DESPACHO\s+(?<n>\d{1,3})\s+-\s+(?<data>\d{4}-\d{2}-\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly string[] ExtensoesDespacho = { ".docx", ".doc", ".pdf" };

    static bool Ilegivel(Exception e) => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    /// <summary>Link de verdade: link simbólico ou junção (<see cref="FileSystemInfo.LinkTarget"/> preenchido). Um ponto de reparse qualquer
    /// NÃO basta: os marcadores de arquivo só-na-nuvem do OneDrive (Files On-Demand) também têm o atributo ReparsePoint, mas são arquivos e
    /// pastas comuns e precisam ser lidos. Se não der para saber, trata como link (o app só lê, então recusar é o lado seguro).</summary>
    internal static bool EhLink(string caminho)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(caminho) ? new DirectoryInfo(caminho) : new FileInfo(caminho);
            return info.LinkTarget is not null;
        }
        catch (Exception e) when (Ilegivel(e)) { return true; }
    }

    public static Leitura<Processo> Listar(string raiz, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var avisos = new List<string>();
        if (!Directory.Exists(raiz))
            return new Leitura<Processo>(Array.Empty<Processo>(), new[] { $"A pasta de processos não foi encontrada: {raiz}" });

        var achados = new List<(int AnoPasta, Processo P)>();
        try
        {
            foreach (var pastaAno in Directory.EnumerateDirectories(raiz))
            {
                ct.ThrowIfCancellationRequested();
                var nomeAno = Path.GetFileName(pastaAno);
                if (!PastaAno.IsMatch(nomeAno) || EhLink(pastaAno)) continue;
                var anoPasta = int.Parse(nomeAno, CultureInfo.InvariantCulture);
                try
                {
                    foreach (var pasta in Directory.EnumerateDirectories(pastaAno))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (EhLink(pasta)) continue;
                        achados.Add((anoPasta, Interpretar(pasta)));
                    }
                }
                catch (Exception e) when (Ilegivel(e))
                {
                    avisos.Add($"A pasta {pastaAno} não pôde ser lida: {e.Message}");
                }
            }
        }
        catch (Exception e) when (Ilegivel(e))
        {
            avisos.Add($"A pasta {raiz} não pôde ser lida: {e.Message}");
        }

        var itens = achados
            .OrderByDescending(a => a.AnoPasta)
            .ThenBy(a => a.P.Numero is null ? 1 : 0)
            .ThenByDescending(a => a.P.Numero ?? 0)
            .ThenBy(a => a.P.NomePasta, StringComparer.OrdinalIgnoreCase)
            .Select(a => a.P)
            .ToList();
        return new Leitura<Processo>(itens, avisos);
    }

    static Processo Interpretar(string pasta)
    {
        var nome = Path.GetFileName(pasta);
        var m = NomeProcesso.Match(nome);
        return m.Success
            ? new Processo(pasta, nome, int.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups["ano"].Value, CultureInfo.InvariantCulture), m.Groups["req"].Value.Trim())
            : new Processo(pasta, nome, null, null, null);
    }

    /// <summary>Minúsculas e sem acentos, para comparar o que o usuário digita.</summary>
    internal static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    static readonly Regex PontoDeMilhar = new(@"(?<=\d)\.(?=\d{3}(\D|$))", RegexOptions.CultureInvariant);

    /// <summary>Termo vazio mostra tudo. 1.0.0 (correção do filtro "Filtrar por número ou requerente"): o termo é partido em palavras e
    /// TODAS precisam casar. Palavra só de dígitos casa com o número (parcial, sem zeros à esquerda: "0007" acha o 7), com o ano ("2026",
    /// "26") ou com dígitos do requerente; palavra com letras casa com o requerente ou o nome da pasta, sem acento e sem caixa. Assim
    /// "15004/2026", "15004-2026", "15.004/2026", "15004 2026", "2026", "fulano 2026" e "jose" (para JOSÉ) funcionam. O termo inteiro
    /// também vale contra o nome da pasta, para quem cola o nome dela.</summary>
    public static bool Filtrar(Processo p, string termo)
    {
        var t = Normalizar(PontoDeMilhar.Replace(termo ?? "", "")).Trim();
        if (t.Length == 0) return true;
        var pasta = Normalizar(p.NomePasta);
        if (pasta.Contains(t, StringComparison.Ordinal)) return true;
        var requerente = Normalizar(p.Requerente ?? (p.Numero is null ? p.NomePasta : ""));
        var numero = p.Numero?.ToString(CultureInfo.InvariantCulture);
        var ano = p.Ano?.ToString(CultureInfo.InvariantCulture);
        var palavras = Regex.Split(t, @"[^\p{L}\p{Nd}]+").Where(x => x.Length > 0).ToArray();
        if (palavras.Length == 0) return false;
        foreach (var w in palavras)
        {
            bool casa;
            if (w.All(char.IsAsciiDigit))
            {
                var semZeros = w.TrimStart('0');
                if (semZeros.Length == 0) semZeros = "0";
                casa = (numero is not null && (numero.Contains(semZeros, StringComparison.Ordinal) || numero == w))
                    || (ano is not null && ano.Contains(w, StringComparison.Ordinal))
                    || requerente.Contains(w, StringComparison.Ordinal)
                    || (p.Numero is null && pasta.Contains(w, StringComparison.Ordinal));
            }
            else casa = requerente.Contains(w, StringComparison.Ordinal) || pasta.Contains(w, StringComparison.Ordinal);
            if (!casa) return false;
        }
        return true;
    }

    public static DetalheProcesso Detalhar(Processo p, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var avisos = new List<string>();
        var relatorios = new List<RelatorioEncontrado>();

        try
        {
            // Os relatórios ficam nas pastas DESPACHO NN - AAAA-MM-DD (regra do kit); os antigos, na raiz do processo, continuam valendo.
            var pastasDeRelatorio = new List<string> { p.Pasta };
            foreach (var d in Directory.EnumerateDirectories(p.Pasta))
                if (NomeDespacho.IsMatch(Path.GetFileName(d)) && !EhLink(d)) pastasDeRelatorio.Add(d);
            foreach (var pasta in pastasDeRelatorio)
                foreach (var f in Directory.EnumerateFiles(pasta, "*.rpmvni").Concat(Directory.EnumerateFiles(pasta, "*.pmvrel")))  // .pmvrel: formato antigo
                {
                    ct.ThrowIfCancellationRequested();
                    if (EhLink(f)) continue;
                    relatorios.Add(new RelatorioEncontrado(f, TipoRelatorio.Pacote, File.GetLastWriteTimeUtc(f)));
                }
            var ia = Path.Combine(p.Pasta, ".pmvni-ai");
            if (Directory.Exists(ia) && !EhLink(ia))
                foreach (var revisao in Directory.EnumerateDirectories(ia))
                {
                    ct.ThrowIfCancellationRequested();
                    if (EhLink(revisao)) continue;
                    foreach (var f in Directory.EnumerateFiles(revisao, "dados*.json").Concat(Directory.EnumerateFiles(revisao, "relatorio_dados*.json")))
                        if (!EhLink(f)) relatorios.Add(new RelatorioEncontrado(f, TipoRelatorio.AnaliseIa, File.GetLastWriteTimeUtc(f)));
                }
        }
        catch (Exception e) when (Ilegivel(e))
        {
            avisos.Add($"Os relatórios de {p.NomePasta} não puderam ser lidos: {e.Message}");
        }

        return new DetalheProcesso(
            p,
            relatorios.OrderByDescending(r => r.AlteradoUtc).ThenBy(r => r.Caminho, StringComparer.OrdinalIgnoreCase).ToList(),
            LerDespachos(p, avisos, ct),
            ArquivosAte3Niveis(p, "PROJETO", avisos, ct),
            ArquivosAte3Niveis(p, "DOCUMENTOS", avisos, ct),
            avisos);
    }

    static bool TemRelatorio(string pasta)
    {
        try { return Directory.EnumerateFiles(pasta, "*.rpmvni").Any() || Directory.EnumerateFiles(pasta, "*.pmvrel").Any(); }
        catch (Exception e) when (Ilegivel(e)) { return false; }
    }

    internal static List<Despacho> LerDespachos(Processo p, List<string> avisos, CancellationToken ct)
    {
        var lista = new List<Despacho>();
        try
        {
            foreach (var pasta in Directory.EnumerateDirectories(p.Pasta))
            {
                ct.ThrowIfCancellationRequested();
                var m = NomeDespacho.Match(Path.GetFileName(pasta));
                if (!m.Success || EhLink(pasta)) continue;
                if (!DateOnly.TryParseExact(m.Groups["data"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                {
                    avisos.Add($"A data da pasta {Path.GetFileName(pasta)} não é válida.");
                    continue;
                }
                var arquivos = new List<string>();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(pasta))
                        if (ExtensoesDespacho.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase) && !EhLink(f))
                            arquivos.Add(f);
                }
                catch (Exception e) when (Ilegivel(e))
                {
                    avisos.Add($"A pasta {Path.GetFileName(pasta)} não pôde ser lida: {e.Message}");
                }
                arquivos.Sort(StringComparer.OrdinalIgnoreCase);
                // A pasta da etapa que até agora só tem o relatório (o .rpmvni importado, 2026-11-16) ainda não é um despacho.
                if (arquivos.Count == 0 && TemRelatorio(pasta)) continue;
                lista.Add(new Despacho(pasta, int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture), data, TipoPeloNome(arquivos), arquivos, p));
            }
        }
        catch (Exception e) when (Ilegivel(e))
        {
            avisos.Add($"Os despachos de {p.NomePasta} não puderam ser lidos: {e.Message}");
        }
        return lista.OrderByDescending(d => d.Data).ThenByDescending(d => d.Numero).ToList();
    }

    static TipoDespacho TipoPeloNome(IEnumerable<string> arquivos)
    {
        var nomes = arquivos.Select(a => Normalizar(Path.GetFileName(a))).ToList();
        if (nomes.Any(n => n.Contains("correcao", StringComparison.Ordinal))) return TipoDespacho.Correcao;
        if (nomes.Any(n => n.Contains("aprovacao", StringComparison.Ordinal))) return TipoDespacho.Aprovacao;
        return TipoDespacho.NaoIdentificado;
    }

    /// <summary>Arquivos de <c>PROJETO</c> ou <c>DOCUMENTOS</c>: até 3 níveis (a própria pasta e duas de subpastas), por nome; links ignorados.</summary>
    static List<ArquivoEncontrado> ArquivosAte3Niveis(Processo p, string nome, List<string> avisos, CancellationToken ct)
    {
        var lista = new List<ArquivoEncontrado>();
        var pasta = Path.Combine(p.Pasta, nome);
        if (!Directory.Exists(pasta) || EhLink(pasta)) return lista;
        Varrer(pasta, 1);
        return lista.OrderBy(a => a.Relativo, StringComparer.OrdinalIgnoreCase).ToList();

        void Varrer(string dir, int nivel)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (EhLink(f)) continue;
                    var info = new FileInfo(f);
                    lista.Add(new ArquivoEncontrado(f, Path.GetRelativePath(p.Pasta, f), info.Length, info.LastWriteTimeUtc));
                }
                if (nivel < NiveisMaximos)
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if (!EhLink(sub)) Varrer(sub, nivel + 1);
            }
            catch (Exception e) when (Ilegivel(e))
            {
                avisos.Add($"A pasta {dir} não pôde ser lida: {e.Message}");
            }
        }
    }

    /// <summary>Caminho (sem sobrescrever) do pacote de uma importação: número/ano da pasta, senão o da capa. Regra do kit (correção
    /// 2026-11-16): o relatório é entrega da etapa e fica em <c>DESPACHO NN - AAAA-MM-DD</c> — a etapa existente de maior número que ainda
    /// não tem despacho, senão a próxima, com a data de hoje (<see cref="Etapas.Sugerida"/>). A pasta da etapa é criada aqui.</summary>
    public static string NomeImportacao(Processo p, string? processoDaCapa, DateOnly hoje)
    {
        var (numero, ano) = p.Numero is not null && p.Ano is not null ? (p.Numero, p.Ano) : NomesArquivo.DoProcessoDaCapa(processoDaCapa);
        var etapa = Etapas.Sugerida(p.Pasta, hoje);
        Directory.CreateDirectory(etapa.Pasta);
        return NomesArquivo.Livre(etapa.Pasta, NomesArquivo.Importacao(numero, ano));
    }
}
