using System.Globalization;
using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pmvni.Imoveis;

/// <summary>Gera a ficha e o mapa locais da feição já identificada pelo kit na camada Lotes.</summary>
public static class MapaParaAnalise
{
    public const string Comando = "--mapa-qgis";

    public static MapaGerado Gerar(string pastaBase, int indice, bool confirmado, CancellationToken ct = default, bool exigirCompleto = true)
    {
        var b = BaseQgis.Carregar(pastaBase, ct: ct);
        var lote = b[Camada.Lotes]?.Feicoes.FirstOrDefault(f => f.Indice == indice);
        if (lote?.Geometria is not { IsEmpty: false } geometria)
            throw new InvalidDataException($"A feição {indice} não foi encontrada na camada Lotes.");
        var obrigatorias = new[] { Camada.Edificacoes, Camada.Zoneamento, Camada.Logradouros, Camada.HidroApp };
        var ausentes = obrigatorias.Where(c => b[c] is null).Select(FontesQgis.Rotulo).ToArray();
        if (exigirCompleto && ausentes.Length > 0)
            throw new InvalidDataException("Base incompleta para o mapa de localização. Confira as camadas: " + string.Join(", ", ausentes));
        return MapaLocalizacao.Gerar(b, geometria, [lote], new OpcoesMapa { Candidato = !confirmado, Cotas = true, ComDonos = false });
    }

    public static byte[] ImagemPng(MapaGerado mapa)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A imagem cartográfica requer o Windows.");
        // O rasterizador ignora paint-order. Pinte o halo por baixo das letras apenas na cópia para impressão.
        var desenho = XDocument.Parse(mapa.Svg);
        foreach (var texto in desenho.Descendants().Where(e => (string?)e.Attribute("paint-order") == "stroke").ToList())
        {
            var halo = new XElement(texto);
            halo.SetAttributeValue("fill", "none");
            halo.Attribute("paint-order")!.Remove();
            texto.AddBeforeSelf(halo);
            texto.SetAttributeValue("stroke", "none");
            texto.Attribute("paint-order")!.Remove();
        }
        var svg = Svg.SvgDocument.FromSvg<Svg.SvgDocument>(desenho.ToString(SaveOptions.DisableFormatting));
        using var bitmap = svg.Draw(MapaLocalizacao.Largura * 2, MapaLocalizacao.Altura * 2);
        using var saida = new MemoryStream();
        bitmap.Save(saida, System.Drawing.Imaging.ImageFormat.Png);
        return saida.ToArray();
    }

    public static void RasterizarVista(string entrada, string saida)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!string.Equals(Path.GetExtension(entrada), ".svg", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(saida), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Uso: --rasterizar-vista <desenho.svg> <imagem.png>");
        var svg = Svg.SvgDocument.FromSvg<Svg.SvgDocument>(File.ReadAllText(entrada));
        var tamanho = svg.GetDimensions();
        if (!(tamanho.Width >= 1 && tamanho.Height >= 1 && tamanho.Width <= 8192 && tamanho.Height <= 8192 && tamanho.Width * tamanho.Height <= 32_000_000))
            throw new InvalidDataException("Confira as dimensões em pixels do SVG.");
        using var bitmap = svg.Draw();
        using var png = new MemoryStream();
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllBytes(saida, png.ToArray());
    }

    public static int Executar(string[] args, TextWriter erros)
    {
        var opcoes = args.Skip(4).ToArray();
        var confirmado = opcoes.Contains("--confirmado");
        var completo = opcoes.Contains("--completo");
        if (args.Length < 4 || opcoes.Any(o => o is not ("--confirmado" or "--completo")) || opcoes.Distinct().Count() != opcoes.Length ||
            args[0] != Comando || string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[3])
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var indice))
        {
            erros.WriteLine("Uso: --mapa-qgis <pasta da base> <índice da feição Lotes> <pasta de saída vazia> [--confirmado] [--completo]");
            return 2;
        }
        try
        {
            var pasta = Directory.CreateDirectory(args[3]);
            if (pasta.EnumerateFileSystemInfos().Any()) throw new IOException("A pasta de saída precisa estar vazia.");
            var mapa = Gerar(args[1], indice, confirmado, exigirCompleto: completo);
            var ext = mapa.Extensao;
            var png = ImagemPng(mapa);
            var ficha = JsonSerializer.Serialize(new
            {
                feicao = indice, inscricao = mapa.Inscricao, completo, resultado = confirmado ? "CONFIRMADO" : "CANDIDATO",
                sha256_png = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png)), extent = new[] { ext.MinX, ext.MinY, ext.MaxX, ext.MaxY }, crs = MapaLocalizacao.Crs,
                legend = mapa.Legenda, layers = mapa.Camadas, dados = mapa.Dados, cotas = true
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true });
            // Arquivos novos em pasta vazia. Em caso de falha, desfaça apenas o que esta chamada criou.
            var criados = new List<string>();
            try
            {
                void Gravar(string nome, string texto)
                {
                    var caminho = Path.Combine(pasta.FullName, nome);
                    using var arquivo = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    criados.Add(caminho);
                    using var escritor = new StreamWriter(arquivo);
                    escritor.Write(texto);
                }
                Gravar("MAPA.SVG", mapa.Svg);
                Gravar("FICHA.JSON", ficha);
                var caminhoPng = Path.Combine(pasta.FullName, "MAPA.PNG");
                using var imagem = new FileStream(caminhoPng, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                criados.Add(caminhoPng);
                imagem.Write(png);
            }
            catch
            {
                foreach (var caminho in criados) File.Delete(caminho);
                throw;
            }
            return 0;
        }
        catch (Exception e)
        {
            erros.WriteLine($"Falha ao gerar o mapa QGIS: {e.Message}");
            return 1;
        }
    }
}
