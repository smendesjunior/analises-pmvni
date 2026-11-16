namespace Pmvni.App;

/// <summary>Chave do ícone de tipo de arquivo (correções 2026-11-15): o ícone vem do Windows pela extensão, sem abrir o arquivo (os
/// arquivos do OneDrive só na nuvem não são baixados), e é guardado uma vez por chave.</summary>
public static class TiposArquivo
{
    /// <summary>Chave de pasta (o ícone de pasta do Windows).</summary>
    public const string Pasta = "<pasta>";

    /// <summary>".pdf", ".docx"… (minúsculas); <see cref="Pasta"/> para pastas; "" sem extensão ou sem caminho.</summary>
    public static string Chave(string? caminho, bool ehPasta = false)
    {
        if (ehPasta) return Pasta;
        if (string.IsNullOrWhiteSpace(caminho)) return "";
        var nome = caminho.TrimEnd('\\', '/');
        if (nome.Length < caminho.Length) return Pasta; // termina com barra: é pasta
        var ext = Path.GetExtension(nome);
        // ".tmp " ou extensões com espaço não são extensões de verdade.
        return ext.Length is > 1 and <= 16 && !ext.Contains(' ') ? ext.ToLowerInvariant() : "";
    }

    /// <summary>Glifo Segoe Fluent de reserva, quando o Windows não devolve ícone: pasta, imagem, ou documento genérico.</summary>
    public static string GlifoReserva(string chave) => chave switch
    {
        Pasta => "",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" or ".heic" => "",
        _ => "",
    };
}
