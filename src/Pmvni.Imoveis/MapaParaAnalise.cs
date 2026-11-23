using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pmvni.Imoveis;

/// <summary>Gera a ficha e o mapa locais da feição já identificada pelo kit na camada Lotes.</summary>
public static class MapaParaAnalise
{
    public const string Comando = "--mapa-qgis";

    public static int Executar(string[] args, TextWriter erros)
    {
        if (args.Length != 4 || args[0] != Comando || string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[3])
            || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var indice))
        {
            erros.WriteLine("Uso: --mapa-qgis <pasta da base> <índice da feição Lotes> <pasta de saída vazia>");
            return 2;
        }
        try
        {
            var pasta = Directory.CreateDirectory(args[3]);
            if (pasta.EnumerateFileSystemInfos().Any()) throw new IOException("A pasta de saída precisa estar vazia.");
            var b = BaseQgis.Carregar(args[1]);
            var lote = b[Camada.Lotes]?.Feicoes.FirstOrDefault(f => f.Indice == indice);
            if (lote?.Geometria is not { IsEmpty: false } geometria)
                throw new InvalidDataException($"A feição {indice} não foi encontrada na camada Lotes.");
            var mapa = MapaLocalizacao.Gerar(b, geometria, [lote], new OpcoesMapa { Candidato = true, Cotas = true, ComDonos = false });
            var ext = mapa.Extensao;
            var ficha = JsonSerializer.Serialize(new
            {
                feicao = lote.Indice, extent = new[] { ext.MinX, ext.MinY, ext.MaxX, ext.MaxY }, crs = MapaLocalizacao.Crs,
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
