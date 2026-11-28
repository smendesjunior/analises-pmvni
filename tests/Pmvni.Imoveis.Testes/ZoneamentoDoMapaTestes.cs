using NetTopologySuite.Geometries;
using Pmvni.Imoveis;
using Xunit;

namespace Pmvni.Imoveis.Testes;

public class ZoneamentoDoMapaTestes
{
    [Fact]
    public void testa_cobertura_do_lote_inteiro_e_preserva_contato_de_borda()
    {
        var g = new GeometryFactory();
        Geometry Caixa(double inicio, double fim) => g.ToGeometry(new Envelope(inicio, fim, 0, 10));
        Fe Zona(int id, string nome, double inicio, double fim) => MapaLocalizacao.De(
            new Feicao(id, Caixa(inicio, fim), new Dictionary<string, string?> { ["ZONA"] = nome }));
        var lote = Caixa(0, 10);
        Assert.Equal("A", MapaLocalizacao.CruzarZoneamento(lote, [Zona(1,"A",0,10), Zona(2,"B",10,15)]).Zona);
        var duas = MapaLocalizacao.CruzarZoneamento(lote, [Zona(1,"A",0,9), Zona(2,"B",9,10)]);
        Assert.Empty(duas.Zona);
        Assert.Equal("Mais de uma zona (A, B)", duas.Motivo);
        Assert.Empty(MapaLocalizacao.CruzarZoneamento(lote, [Zona(1,"A",0,9)]).Zona);
        Assert.Equal("A", MapaLocalizacao.CruzarZoneamento(lote, [Zona(1,"A",0,9), Zona(2,"A",9,10)]).Zona);
    }
}
