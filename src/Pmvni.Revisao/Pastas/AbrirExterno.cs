namespace Pmvni.Revisao;

/// <summary>Lista de permissão para abrir um arquivo fora do editor (pelo shell): só documentos e imagens conhecidos, arquivos comuns.
/// O resto (scripts, atalhos, executáveis, pastas, links, fluxos alternativos) é recusado, com motivo em pt-BR.</summary>
public static class AbrirExterno
{
    public static IReadOnlySet<string> Extensoes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".png", ".jpg", ".jpeg", ".dwg", ".dxf", ".txt",
    };

    public static bool Permitido(string caminho, out string? motivo)
    {
        motivo = null;
        if (string.IsNullOrWhiteSpace(caminho)) { motivo = "Nenhum arquivo informado."; return false; }

        // ':' só pode aparecer na unidade ("C:\"); depois dela indicaria fluxo alternativo (NTFS) ou nome inválido.
        var raiz = Path.GetPathRoot(caminho) ?? "";
        if (caminho.AsSpan(raiz.Length).Contains(':'))
        {
            motivo = "O nome do arquivo tem caracteres não permitidos. Use Mostrar no Explorador.";
            return false;
        }

        var ext = Path.GetExtension(caminho);
        if (!Extensoes.Contains(ext))
        {
            motivo = $"Este tipo de arquivo não é aberto pelo editor ({(ext.Length == 0 ? "sem extensão" : ext)}). Use Mostrar no Explorador.";
            return false;
        }

        try
        {
            FileAttributes atributos = File.GetAttributes(caminho);
            if ((atributos & FileAttributes.Directory) != 0)
            {
                motivo = "Pastas não são abertas pelo editor. Use Mostrar no Explorador.";
                return false;
            }
            if (Processos.EhLink(caminho))
            {
                motivo = "Links e atalhos não são abertos pelo editor. Use Mostrar no Explorador.";
                return false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            motivo = "O arquivo não foi encontrado ou não pode ser lido.";
            return false;
        }
        return true;
    }
}
