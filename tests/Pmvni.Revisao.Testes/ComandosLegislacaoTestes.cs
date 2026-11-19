using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Comandos da faixa da Legislação habilitados por estado do leitor (desenho §11.5).</summary>
public class ComandosLegislacaoTestes
{
    static readonly string[] Sempre =
    [
        ComandosApp.LegislacaoConsultar, ComandosApp.LegislacaoGerenciar, ComandosApp.ArquivoImportar, ComandosApp.ArquivoAbrir, ComandosApp.ArquivoRecentes,
        ComandosApp.LegislacaoEscopo, ComandosApp.LegislacaoEscopoTodas, ComandosApp.LegislacaoLista,
    ];

    static readonly string[] ComLei =
    [
        ComandosApp.LegislacaoPaginaAnterior, ComandosApp.LegislacaoPaginaProxima, ComandosApp.LegislacaoZoomMenos, ComandosApp.LegislacaoZoomMais,
        ComandosApp.LegislacaoAjustarLargura, ComandosApp.LegislacaoPaginaInteira, ComandosApp.LegislacaoEscopoLei, ComandosApp.ArquivoImprimir,
    ];

    static readonly string[] Ocorrencias = [ComandosApp.LegislacaoOcorrenciaAnterior, ComandosApp.LegislacaoOcorrenciaProxima];
    static readonly string[] Copiar = [ComandosApp.LegislacaoCopiar, ComandosApp.LegislacaoCopiarCitacao];

    static string[] Ordem(IEnumerable<string> ids) => ids.Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void comandos_habilitados_por_estado()
    {
        Assert.Equal(Ordem(Sempre.Append(ComandosApp.LegislacaoAnexar)), Ordem(ComandosLegislacao.Habilitados(false, false, false, false, false)));
        // Lei com texto, sem seleção, com marcadores.
        Assert.Equal(Ordem(Sempre.Append(ComandosApp.LegislacaoAnexar).Concat(ComLei).Concat(Ocorrencias).Append(ComandosApp.LegislacaoMarcadores)),
            Ordem(ComandosLegislacao.Habilitados(true, true, false, true, false)));
        // Com seleção: + Copiar e Copiar citação.
        Assert.Equal(Ordem(Sempre.Append(ComandosApp.LegislacaoAnexar).Concat(ComLei).Concat(Ocorrencias).Append(ComandosApp.LegislacaoMarcadores).Concat(Copiar)),
            Ordem(ComandosLegislacao.Habilitados(true, true, true, true, false)));
        // Sem texto: sem ocorrências; sem marcadores: sem Marcadores.
        var semTexto = ComandosLegislacao.Habilitados(true, false, false, false, false);
        Assert.DoesNotContain(ComandosApp.LegislacaoOcorrenciaProxima, semTexto);
        Assert.DoesNotContain(ComandosApp.LegislacaoOcorrenciaAnterior, semTexto);
        Assert.DoesNotContain(ComandosApp.LegislacaoMarcadores, semTexto);
        Assert.DoesNotContain(ComandosApp.LegislacaoCopiar, semTexto);
        // Só leitura: sem Anexar (o resto continua).
        Assert.DoesNotContain(ComandosApp.LegislacaoAnexar, ComandosLegislacao.Habilitados(false, false, false, false, true));
        Assert.Contains(ComandosApp.LegislacaoGerenciar, ComandosLegislacao.Habilitados(false, false, false, false, true));
        Assert.Equal("Selecione um trecho do texto.", ComandosLegislacao.DicaSemSelecao);
        // "Em todas as leis": F3/Shift+F3 (ocorrências na lei aberta) não valem; os resultados ficam na aba Resultados.
        var emTodas = ComandosLegislacao.Habilitados(true, true, false, true, false, emTodas: true);
        Assert.DoesNotContain(ComandosApp.LegislacaoOcorrenciaProxima, emTodas);
        Assert.DoesNotContain(ComandosApp.LegislacaoOcorrenciaAnterior, emTodas);
        Assert.Contains(ComandosApp.LegislacaoPaginaProxima, emTodas);
    }

    [Fact]
    public void todo_id_habilitavel_esta_na_faixa_da_legislacao()
    {
        var faixa = Faixas.Comandos(Faixas.Para(Modulo.Legislacao)).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(ComandosLegislacao.Habilitados(true, true, true, true, false), id => Assert.Contains(id, faixa));
        // E todo comando da faixa (menos Ajuda) tem uma regra: nenhum fica desabilitado para sempre.
        var ajuda = Faixas.Para(Modulo.Legislacao).Abas.Single(a => a.Nome == "Ajuda").Comandos.Select(c => c.Id);
        Assert.Equal(Ordem(faixa.Except(ajuda)), Ordem(ComandosLegislacao.Habilitados(true, true, true, true, false)));
    }
}
