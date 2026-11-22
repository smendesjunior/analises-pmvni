namespace Pmvni.App;

/// <summary>Chave do ícone de tipo de arquivo (correções 2026-11-15): o ícone vem do Windows pela extensão, sem abrir o arquivo (os
/// arquivos do OneDrive só na nuvem não são baixados), e é guardado uma vez por chave.</summary>
public static class TiposArquivo
{
    /// <summary>Chave de pasta (o ícone de pasta do Windows).</summary>
    public const string Pasta = "<pasta>";

    /// <summary>O grupo dos arquivos sem extensão (o último dos grupos por tipo).</summary>
    public const string Outros = "Outros";

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
        Pasta => Glifos.Pasta,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" or ".heic" => Glifos.Figuras,
        _ => Glifos.Relatorio,
    };

    /// <summary>O tipo de arquivo como grupo da página do processo (agrupar e ordenar por tipo), a partir da <see cref="Chave"/>:
    /// "PDF", "Imagens", "DWG e CAD", "Word"…; extensão que não está na lista vira "Arquivos XYZ"; sem extensão, <see cref="Outros"/>.</summary>
    public static string Categoria(string chave) => chave switch
    {
        Pasta => "Pastas",
        "" => Outros,
        ".pdf" => "PDF",
        ".png" or ".jpg" or ".jpeg" or ".jfif" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" or ".heic" or ".heif" or ".svg" => "Imagens",
        ".dwg" or ".dxf" or ".dwf" or ".dwt" or ".rvt" or ".ifc" or ".skp" => "DWG e CAD",
        ".doc" or ".docx" or ".docm" or ".dotx" or ".odt" or ".rtf" => "Word",
        ".xls" or ".xlsx" or ".xlsm" or ".ods" or ".csv" => "Excel",
        ".ppt" or ".pptx" or ".odp" => "PowerPoint",
        ".rpmvni" or ".pmvrel" => "Relatórios do editor",
        ".json" => "Análises da IA (JSON)",
        ".txt" or ".md" or ".log" => "Texto",
        ".zip" or ".rar" or ".7z" => "Compactados",
        ".kml" or ".kmz" or ".shp" or ".gpkg" or ".qgz" or ".qgs" or ".geojson" => "Mapas",
        ".mp4" or ".mov" or ".avi" or ".mkv" => "Vídeos",
        _ => "Arquivos " + chave[1..].ToUpperInvariant(),
    };
}
