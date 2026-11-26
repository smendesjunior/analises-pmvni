using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public class FormularioDespachoTestes
{
    [Theory]
    [InlineData(TipoDespacho.Entrega)]
    [InlineData(TipoDespacho.Devolucao)]
    [InlineData(TipoDespacho.Generico)]
    public void CamposDaPreviaGeramDocumentoConferido(TipoDespacho tipo)
    {
        var kit = Ambiente.ExigirKit();
        var modelos = FormularioDespacho.IncluirModelos(RegistroModelos.Ler(kit), kit);
        var modelo = modelos.Modelos.Single(m => m.Tipo == tipo);
        var bytes = File.ReadAllBytes(modelo.Caminho);
        var info = PreenchimentoDocx.LerModelo(bytes, tipo);
        var textos = new TextosFixos(tipo, modelo.CaminhoRelativo, modelo.Sha256, []);
        var r = ConteudoDespacho.MontarEmBranco(tipo, info, new DateOnly(2099, 3, 10), "1000/2099", "Exemplo");
        Assert.NotEmpty(r.Faltando());
        var campos = ConteudoDespacho.Previa(info, r, textos).SelectMany(p => p.TrechosOuTexto).Where(t => t.Chave != null).ToList();
        Assert.Equal(info.Formulario!.Campos.Count, campos.Count);
        foreach (var c in campos.Where(t => t.Faltando != null))
            Assert.True(ConteudoDespacho.AplicarEdicao(r, c.Chave!, c.Chave == "formulario:Texto" ? "Linha um\nLinha dois" : "Valor fictício"));
        Assert.Empty(r.Faltando());
        var final = r.Congelar(textos);
        var documento = PreenchimentoDocx.Preencher(bytes, final);
        Assert.Empty(ValidacaoDespacho.Comparar(bytes, documento));
        Assert.Empty(ValidacaoDespacho.Conteudo(bytes, documento, final));
    }

    [Theory]
    [InlineData("RETIRADA DE PROCESSOS E PROJETOS", 7)]
    [InlineData("DEVOLUÇÃO DE PROCESSOS E PROJETOS", 9)]
    [InlineData("USO GERAL", 5)]
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
