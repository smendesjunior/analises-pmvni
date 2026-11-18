using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

public static class PeticaoDaPrancha
{
    static readonly Regex Identificador = new(
        @"^\s*(?:(?:PROCC?\.?|PROCESSO|PETI[ÇC][ÃA]O)\s*)?(?<n>[\p{L}\p{Nd}][\p{L}\p{Nd}._-]*)\s*[-_]\s*(?<ano>(?:19|20)[0-9]{2})(?=\s*-|\s*$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? Identificar(string caminho)
    {
        string? numero = null;
        var arquivo = new FileInfo(caminho);
        IEnumerable<string> Nomes()
        {
            yield return Path.GetFileNameWithoutExtension(arquivo.Name);
            for (var pasta = arquivo.Directory; pasta is not null; pasta = pasta.Parent)
                {
                var processo = Processos.Interpretar(pasta.FullName);
                if (processo.Protocolo is { } protocolo) yield return protocolo;
                else yield return pasta.Name;
            }
        }
        foreach (var nome in Nomes())
        {
            if (nome.Contains('/'))
            {
                if (numero is not null && !string.Equals(numero, nome, StringComparison.OrdinalIgnoreCase)) return null;
                numero = nome;
                continue;
            }
            var m = Identificador.Match(nome);
            if (!m.Success) continue;
            var encontrado = m.Groups["n"].Value + "/" + m.Groups["ano"].Value;
            if (numero is not null && numero != encontrado) return null;
            numero = encontrado;
        }
        return numero;
    }
}
