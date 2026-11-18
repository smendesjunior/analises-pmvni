using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Criar o processo como o <c>criar</c> do <c>pasta_processo.py</c> do kit (desenho §12.3): <c>&lt;raiz&gt;\&lt;ano&gt;\&lt;nº&gt; - &lt;ano&gt; - &lt;REQUERENTE&gt;</c>
/// com <c>DOCUMENTOS</c> e <c>PROJETO\INICIAL</c>. Se já existe pasta desse número e ano (inclusive "FINALIZADO - …" ou "0007-2099 - …"),
/// usa a existente, sem renomear nem duplicar.</summary>
public static class CriacaoProcesso
{
    static readonly Regex Invalidos = new(@"[<>:""/\\|?*\x00-\x1f]", RegexOptions.CultureInvariant);
    static readonly Regex Espacos = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>Maiúsculas, sem acentos, caracteres inválidos de nome de pasta e espaços repetidos viram um espaço; sem ponto no fim; até 80 caracteres.</summary>
    public static string NomeRequerente(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var limpo = Espacos.Replace(Invalidos.Replace(sb.ToString().Normalize(NormalizationForm.FormC), " "), " ").Trim().Trim('.').ToUpperInvariant();
        if (limpo.Length == 0) throw new ArgumentException("Informe o nome do requerente.", nameof(s));
        return (limpo.Length > 80 ? limpo[..80] : limpo).TrimEnd();
    }

    static bool EhDoProcesso(Processo p, int numero, int ano)
    {
        if (p.Numero == numero && p.Ano == ano) return true;
        var m = Regex.Match(p.NomePasta, @"^FINALIZADO\s*-\s*0*(?<n>\d{1,7})\s*-\s*(?<a>\d{4})(?:\s*-|\s*$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return m.Success
            && int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture) == numero
            && int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture) == ano;
    }

    /// <summary>Cria (ou reutiliza) a pasta do processo. <c>Criado</c> é false quando já existia.</summary>
    public static (Processo Processo, bool Criado) Criar(string raiz, int numero, int ano, string requerente)
    {
        if (numero < 1 || numero > 9999999) throw new ArgumentException("Informe o número do processo.", nameof(numero));
        return Criar(raiz, numero.ToString(CultureInfo.InvariantCulture), ano, requerente);
    }

    internal const string ArquivoIdentificador = ".pmvni-identificador.txt";

    internal static string Componente(string id)
    {
        if (id.Length <= 120 && !id.StartsWith("PMVNI-ID-", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(id, @"^[\p{L}\p{Nd}][\p{L}\p{Nd}._-]*\z", RegexOptions.CultureInvariant))
            return id;
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return "PMVNI-ID-" + Convert.ToHexString(hash);
    }

    public static (Processo Processo, bool Criado) Criar(string raiz, string identificador, int ano, string requerente)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identificador);
        identificador = identificador.Trim();
        if (ano is < 1900 or > 2099) throw new ArgumentException("Informe o ano do protocolo (ex.: 2026).", nameof(ano));
        if (!Directory.Exists(raiz)) throw new DirectoryNotFoundException("A pasta de processos não foi encontrada: " + raiz);
        var nome = NomeRequerente(requerente);

        var numerico = int.TryParse(identificador, NumberStyles.None, CultureInfo.InvariantCulture, out var numero);
        var existentes = Processos.Listar(raiz, CancellationToken.None).Itens.Where(p =>
            (p.Ano == ano && string.Equals(p.Identificador, identificador, StringComparison.OrdinalIgnoreCase))
            || (numerico && EhDoProcesso(p, numero, ano))).ToList();
        // Como o kit: primeiro a pasta do ano do protocolo; se não houver, qualquer ano.
        var anoTexto = ano.ToString(CultureInfo.InvariantCulture);
        var existente = existentes.FirstOrDefault(p => Path.GetFileName(Path.GetDirectoryName(p.Pasta)) == anoTexto) ?? existentes.FirstOrDefault();
        if (existente is not null) return (existente, false);

        var pastaAno = Path.Combine(raiz, anoTexto);
        if (Directory.Exists(pastaAno) && new DirectoryInfo(pastaAno).LinkTarget is not null)
            throw new IOException($"A pasta {anoTexto} é um atalho (link); o editor não cria processos nela.");

        var componente = Componente(identificador);
        var pasta = Path.Combine(raiz, anoTexto, string.Create(CultureInfo.InvariantCulture, $"{componente} - {ano} - {nome}"));
        Directory.CreateDirectory(pasta);
        if (Processos.EhLink(pasta)) throw new IOException("A pasta do processo é um link.");
        if (componente != identificador)
        {
            var arquivo = Path.Combine(pasta, ArquivoIdentificador);
            var temp = Path.Combine(pasta, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, identificador);
                try { File.Move(temp, arquivo); }
                catch (IOException) when (File.Exists(arquivo) && !Processos.EhLink(arquivo) && File.ReadAllText(arquivo) == identificador) { }
            }
            finally { File.Delete(temp); }
        }
        Directory.CreateDirectory(Path.Combine(pasta, "DOCUMENTOS"));
        Directory.CreateDirectory(Path.Combine(pasta, "PROJETO", "INICIAL"));
        return (new Processo(pasta, Path.GetFileName(pasta), numerico ? numero : null, ano, nome) { Identificador = identificador }, true);
    }
}
