using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>A página do modelo lida do DOCX do kit (correções 2026-11-15): é nela que a prévia é desenhada.</summary>
public sealed class LayoutDespachoTestes
{
    [Theory]
    [InlineData("Correcao")]
    [InlineData("Aprovacao")]
    public void le_pagina_cabecalho_e_rodape_do_modelo(string tipo)
    {
        var t = Enum.Parse<TipoDespacho>(tipo);
        var docx = PreenchimentoDocxTestes.Modelo(t);

        var l = LayoutDespacho.Ler(docx);

        // A4, margens do modelo (1417/1701/2113/1701 twips) e Verdana 11.
        Assert.Equal(595.3, l.LarguraPt, 1);
        Assert.Equal(841.9, l.AlturaPt, 1);
        Assert.Equal(70.85, l.MargemTopoPt, 2);
        Assert.Equal(85.05, l.MargemEsquerdaPt, 2);
        Assert.Equal(105.65, l.MargemBaixoPt, 2);
        Assert.Equal("Verdana", l.Fonte);
        Assert.Equal(11, l.TamanhoPt);

        // Um formato por parágrafo do corpo, na ordem do mapa do modelo.
        var info = PreenchimentoDocx.LerModelo(docx, t);
        Assert.Equal(info.Paragrafos.Count, l.Formatos.Count);
        int Indice(PapelParagrafo p) => info.Paragrafos.ToList().FindIndex(x => x.Papel == p);
        var protocolo = l.Formato(Indice(PapelParagrafo.Protocolo));
        Assert.Equal(12, protocolo.TamanhoPt);
        Assert.Equal(1.5, protocolo.Entrelinha, 3);
        var destinatario = l.Formato(Indice(PapelParagrafo.Destinatario));
        Assert.True(destinatario.Negrito && destinatario.Sublinhado);
        Assert.Equal(14, destinatario.TamanhoPt);
        Assert.Equal(18, destinatario.AntesPt);
        var vaga = l.Formato(Indice(PapelParagrafo.Vaga));
        Assert.Equal("both", vaga.Alinhamento);
        Assert.Equal(12, vaga.TamanhoPt); // as vagas recebem o formato do rótulo do protocolo
        Assert.Equal(10, vaga.AntesPt);
        Assert.Equal(16, vaga.DepoisPt);
        Assert.Equal("right", l.Formato(Indice(PapelParagrafo.Data)).Alinhamento);
        var titulo = l.Formato(1);
        Assert.True(titulo.Negrito);
        Assert.Equal(15, titulo.TamanhoPt);

        // Cabeçalho: a faixa com o brasão no alto, da largura da página, e a tarja lateral à direita do texto, recortada da imagem.
        Assert.Equal(2, l.Imagens.Count);
        var faixa = l.Imagens.Single(i => i.LarguraPt > 500);
        Assert.InRange(faixa.YPt, 0, 40);
        Assert.InRange(faixa.XPt, -1, 1);
        Assert.True(faixa.AtrasDoTexto);
        var tarja = l.Imagens.Single(i => i.LarguraPt < 100);
        Assert.True(tarja.XPt > l.LarguraPt - l.MargemDireitaPt, "a tarja fica fora da coluna de texto, à direita");
        Assert.True(tarja.XPt + tarja.LarguraPt <= l.LarguraPt + 1);
        Assert.Equal(0.79903, tarja.CorteEsquerdo, 5);
        Assert.Equal(0.30415, tarja.CorteBaixo, 5);
        Assert.All(l.Imagens, i => Assert.Equal(0x89, i.Bytes[0])); // PNG

        // Rodapé: a linha vermelha e o endereço, telefone, CNPJ e site, acima da distância do rodapé.
        var linha = Assert.Single(l.Linhas);
        Assert.Equal("#FF0000", linha.Cor);
        Assert.True(linha.X1Pt < l.MargemEsquerdaPt && linha.X2Pt > l.LarguraPt - l.MargemDireitaPt);
        var textos = l.Rodape.Select(r => string.Concat(r.Trechos.Select(x => x.Texto))).ToList();
        Assert.Contains("Prefeitura Municipal de Venda Nova do Imigrante/ES", textos);
        Assert.Contains(textos, x => x.Contains("Telefax: (28) 3546-1188", StringComparison.Ordinal));
        Assert.Contains(textos, x => x.Contains("CNPJ", StringComparison.Ordinal) && x.EndsWith("www.vendanova.es.gov.br", StringComparison.Ordinal));
        Assert.All(l.Rodape, r => Assert.Equal("center", r.Alinhamento));
        Assert.True(l.Rodape[^1].TopoPt + l.Rodape[^1].AlturaPt <= l.AlturaPt - 30);
        Assert.True(l.Rodape[0].TopoPt > l.AlturaPt - l.MargemBaixoPt, "o rodapé fica na margem de baixo");
        Assert.True(linha.Y1Pt > l.AlturaPt - l.MargemBaixoPt);
    }

    [Fact]
    public void medidas_do_vml()
    {
        Assert.Equal(6.1, LayoutDespacho.Medida("6.1pt")!.Value, 5);
        Assert.Equal(0.79 * 72 / 25.4, LayoutDespacho.Medida(".79mm")!.Value, 5);
        Assert.Equal(72, LayoutDespacho.Medida("1in")!.Value, 5);
        Assert.Equal(-62.9, LayoutDespacho.Medida("-62.9pt")!.Value, 5);
        Assert.Null(LayoutDespacho.Medida("x"));
    }

    [Fact]
    public void docx_sem_corpo_e_recusado() =>
        Assert.Throws<ModeloForaDoPadraoException>(() => LayoutDespacho.Ler([1, 2, 3]));
}
