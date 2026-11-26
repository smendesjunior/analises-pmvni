using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public class FormularioDespachoTestes
{
    [Theory]
    [InlineData("ENTREGA", 7)]
    [InlineData("DEVOLUÇÃO", 9)]
    [InlineData("GENÉRICO", 5)]
    public void EmiteModeloComCamposPreenchidos(string tipo, int quantidade)
    {
        var caminho = Path.Combine(Ambiente.ExigirKit(), "02 - MODELOS DE DOCUMENTOS", "DESPACHO", $"MODELO DESPACHO {tipo}.docx");
        var modelo = new FormularioDespacho(File.ReadAllBytes(caminho));
        Assert.Equal(quantidade, modelo.Campos.Count);
        Assert.Throws<ArgumentException>(() => modelo.Preencher(new Dictionary<string, string>()));
        var valores = modelo.Campos.ToDictionary(c => c, c => "Exemplo & " + c + "\nSegunda linha");
        using var pacote = new ZipArchive(new MemoryStream(modelo.Preencher(valores)));
        using var entrada = pacote.GetEntry("word/document.xml")!.Open();
        var xml = XDocument.Load(entrada);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var texto = string.Concat(xml.Descendants(w + "t").Select(t => t.Value));
        foreach (var campo in modelo.Campos)
        {
            Assert.Contains("Exemplo & " + campo, texto);
            Assert.DoesNotContain("[" + campo + "]", texto);
        }
        Assert.True(xml.Descendants(w + "br").Count() >= quantidade);
    }
}
