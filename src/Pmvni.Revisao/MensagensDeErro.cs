using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>O texto de uma falha de disco ou de permissão para o usuário, em português ("o arquivo X está em uso por outro programa…"):
/// o texto do .NET (em inglês, com o caminho inteiro) nunca chega a um aviso da tela. O detalhe técnico vai só para o registro
/// (<see cref="Registrar"/>, ligado pelo app). Exceções que não são do Windows (as do próprio editor, já em português) passam como vieram.</summary>
public static class MensagensDeErro
{
    /// <summary>Quem guarda o detalhe técnico (o app liga ao arquivo de registro); null = nada é guardado.</summary>
    public static Action<Exception>? Registrar { get; set; }

    static readonly Regex Citado = new("'(?<c>[^']+)'", RegexOptions.CultureInvariant);

    /// <summary>Registra o detalhe e devolve o texto para o usuário (sem ponto final).</summary>
    public static string ParaUsuario(Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        try { Registrar?.Invoke(e); }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { /* o registro nunca derruba o aviso */ }
        return Traduzir(e);
    }

    /// <summary>O texto para o usuário, sem registrar.</summary>
    public static string Traduzir(Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var nome = NomeDoArquivo(e);
        var arquivo = nome is null ? "o arquivo" : "o arquivo " + nome;
        switch (e)
        {
            case UnauthorizedAccessException:
                return "sem permissão para acessar " + (nome is null ? "o arquivo ou a pasta" : nome) + " (confira se o arquivo não está marcado como somente leitura e se a pasta permite gravar)";
            case PathTooLongException:
                return "o caminho d" + arquivo + " é longo demais para o Windows; use uma pasta com nome mais curto";
            case FileNotFoundException:
                return arquivo + " não foi encontrado";
            case DirectoryNotFoundException:
                return "a pasta não foi encontrada" + (nome is null ? "" : " (" + nome + ")");
        }
        if (e is not IOException || (e.HResult & 0xFFFF0000) != 0x80070000) return e.Message.TrimEnd('.');
        var codigo = e.HResult & 0xFFFF;
        return codigo switch
        {
            32 or 33 => arquivo + " está em uso por outro programa (feche-o ou espere o OneDrive terminar de sincronizar e tente de novo)",
            5 => "sem permissão para acessar " + (nome ?? "o arquivo ou a pasta"),
            2 => arquivo + " não foi encontrado",
            3 => "a pasta não foi encontrada" + (nome is null ? "" : " (" + nome + ")"),
            39 or 112 => "não há espaço suficiente no disco para gravar " + arquivo,
            19 => "o disco está protegido contra gravação",
            80 or 183 => "já existe um arquivo com esse nome" + (nome is null ? "" : " (" + nome + ")"),
            206 => "o caminho d" + arquivo + " é longo demais para o Windows; use uma pasta com nome mais curto",
            53 or 64 or 67 or 1231 => "o local de rede não está acessível",
            362 => "o OneDrive não está em execução: abra o OneDrive e tente de novo",
            _ => "erro de disco ao acessar " + arquivo + " (código " + codigo.ToString(CultureInfo.InvariantCulture) + ")",
        };
    }

    /// <summary>O nome (sem a pasta) do arquivo da falha: o de <see cref="FileNotFoundException.FileName"/> ou o primeiro trecho entre aspas.</summary>
    static string? NomeDoArquivo(Exception e)
    {
        var caminho = e is FileNotFoundException f && !string.IsNullOrEmpty(f.FileName) ? f.FileName : Citado.Match(e.Message) is { Success: true } m ? m.Groups["c"].Value : null;
        if (string.IsNullOrWhiteSpace(caminho)) return null;
        try
        {
            var n = Path.GetFileName(caminho.TrimEnd('\\', '/'));
            return n.Length == 0 ? null : n;
        }
        catch (ArgumentException) { return null; }
    }
}
