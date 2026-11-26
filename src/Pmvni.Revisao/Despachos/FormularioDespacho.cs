using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pmvni.Revisao;

public sealed class FormularioDespacho
{
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly Regex Campo = new(@"\[([^\[\]\r\n]+)\]|^(?:USO GERAL|INSIRA AQUI O TEXTO DO DESPACHO)$");
    static string NomeCampo(Match marca) => marca.Value switch
    {
        "USO GERAL" => "Título",
        "INSIRA AQUI O TEXTO DO DESPACHO" => "Texto",
        _ => marca.Groups[1].Value
    };
    const string Parte = "word/document.xml";
    readonly byte[] modelo;
    public IReadOnlyList<string> Campos { get; }

    public FormularioDespacho(byte[] modelo)
    {
        this.modelo = modelo;
        using var pacote = new ZipArchive(new MemoryStream(modelo));
        using var stream = pacote.GetEntry(Parte)!.Open();
        var xml = XDocument.Load(stream);
        Campos = xml.Descendants(W + "t").SelectMany(t => Campo.Matches(t.Value))
            .Select(NomeCampo).Distinct().ToArray();
        if (Campos.Count == 0) throw new InvalidDataException("O modelo não contém campos editáveis.");
    }

    public static bool UsaFormulario(TipoDespacho tipo) => tipo is TipoDespacho.Entrega or TipoDespacho.Devolucao or TipoDespacho.Generico;

    public static ModelosDisponiveis IncluirModelos(ModelosDisponiveis registro, string kit)
    {
        var modelos = registro.Modelos.ToList();
        var falhas = new Dictionary<TipoDespacho, string>(registro.Indisponiveis);
        foreach (var tipo in RegistroModelos.TiposEmitidos.Where(UsaFormulario))
        {
            var nome = RegistroModelos.Rotulo(tipo).Replace("/", " e ").ToUpperInvariant();
            var relativo = Path.Combine("02 - MODELOS DE DOCUMENTOS", "DESPACHO", $"MODELO DESPACHO {nome}.docx");
            var caminho = Path.Combine(kit, relativo);
            try
            {
                var bytes = File.ReadAllBytes(caminho);
                _ = new FormularioDespacho(bytes);
                var sha = RegistroModelos.Sha(bytes);
                modelos.Add(new ModeloDespacho(tipo, RegistroModelos.NomeNoArquivo(tipo), relativo, caminho, sha, 0, ""));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                falhas[tipo] = $"Não foi possível ler {caminho}: {e.Message}";
            }
        }
        return registro with { Modelos = modelos, Indisponiveis = falhas };
    }

    internal IReadOnlyList<ParagrafoPrevia> Previa(InfoModelo info, RascunhoDespacho r)
    {
        var lista = new List<ParagrafoPrevia>();
        foreach (var p in info.Paragrafos)
        {
            var trechos = new List<TrechoPrevia>();
            var inicio = 0;
            foreach (Match marca in Campo.Matches(p.Texto))
            {
                if (marca.Index > inicio) trechos.Add(new(p.Texto[inicio..marca.Index], p.Negrito));
                var campo = NomeCampo(marca);
                var valor = r.ValorFormulario(campo);
                var chave = campo switch
                {
                    "Protocolo" or "Requerente" or "Assunto" or "Data" => "cabecalho:" + campo.ToLowerInvariant(),
                    _ => "formulario:" + campo
                };
                trechos.Add(new(valor.Length > 0 ? valor : marca.Value, p.Negrito, chave, valor.Length == 0 ? campo : null, valor));
                inicio = marca.Index + marca.Length;
            }
            if (inicio < p.Texto.Length) trechos.Add(new(p.Texto[inicio..], p.Negrito));
            lista.Add(new(string.Concat(trechos.Select(t => t.Texto)), EstiloPrevia.Corpo, null, Trechos: trechos, Modelo: lista.Count));
        }
        return lista;
    }

    public byte[] Preencher(IReadOnlyDictionary<string, string> valores)
    {
        foreach (var campo in Campos)
            if (!valores.TryGetValue(campo, out var v) || string.IsNullOrWhiteSpace(v))
                throw new ArgumentException("Preencha o campo " + campo + ".");
        using var saida = new MemoryStream();
        saida.Write(modelo);
        using (var pacote = new ZipArchive(saida, ZipArchiveMode.Update, true))
        {
            var parte = pacote.GetEntry(Parte)!;
            XDocument xml;
            using (var entrada = parte.Open()) xml = XDocument.Load(entrada, LoadOptions.PreserveWhitespace);
            foreach (var t in xml.Descendants(W + "t").Where(t => Campo.IsMatch(t.Value)).ToList())
            {
                var linhas = Campo.Replace(t.Value, m => valores[NomeCampo(m)].Trim()).ReplaceLineEndings("\n").Split('\n');
                t.Value = linhas[0];
                t.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                XNode anterior = t;
                foreach (var linha in linhas.Skip(1))
                {
                    var seguinte = new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), linha);
                    anterior.AddAfterSelf(new XElement(W + "br"), seguinte);
                    anterior = seguinte;
                }
            }
            using var destino = parte.Open();
            destino.SetLength(0);
            xml.Save(destino, SaveOptions.DisableFormatting);
        }
        return saida.ToArray();
    }
}
