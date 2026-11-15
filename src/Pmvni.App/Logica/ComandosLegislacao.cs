namespace Pmvni.App;

/// <summary>Quais comandos da faixa da Legislação (desenho §11.5) ficam habilitados em cada estado do leitor. Sem tipos WinUI:
/// compilado também nos testes.</summary>
public static class ComandosLegislacao
{
    /// <summary>A dica de Copiar e Copiar citação sem seleção.</summary>
    public const string DicaSemSelecao = "Selecione um trecho do texto.";

    /// <summary>Sempre: Gerenciar anexos, Importar, Abrir, Recentes, Pesquisar em ▾ (com "Em todas as leis") e Lista de leis; Anexar PDFs…
    /// fora do catálogo só leitura. Com uma lei aberta: páginas, os 4 de escala e "Nesta lei"; as ocorrências se ela tem texto, Marcadores
    /// se ela tem marcadores, Copiar e Copiar citação com texto selecionado.</summary>
    public static IReadOnlySet<string> Habilitados(bool leiAberta, bool temTexto, bool temSelecao, bool temMarcadores, bool somenteLeitura)
    {
        var h = new HashSet<string>(StringComparer.Ordinal)
        {
            ComandosApp.LegislacaoGerenciar, ComandosApp.ArquivoImportar, ComandosApp.ArquivoAbrir, ComandosApp.ArquivoRecentes,
            ComandosApp.LegislacaoEscopo, ComandosApp.LegislacaoEscopoTodas, ComandosApp.LegislacaoLista,
        };
        if (!somenteLeitura) h.Add(ComandosApp.LegislacaoAnexar);
        if (!leiAberta) return h;
        h.UnionWith([
            ComandosApp.LegislacaoPaginaAnterior, ComandosApp.LegislacaoPaginaProxima,
            ComandosApp.LegislacaoZoomMenos, ComandosApp.LegislacaoZoomMais, ComandosApp.LegislacaoAjustarLargura, ComandosApp.LegislacaoPaginaInteira,
            ComandosApp.LegislacaoEscopoLei,
        ]);
        if (temTexto) h.UnionWith([ComandosApp.LegislacaoOcorrenciaAnterior, ComandosApp.LegislacaoOcorrenciaProxima]);
        if (temMarcadores) h.Add(ComandosApp.LegislacaoMarcadores);
        if (temSelecao) h.UnionWith([ComandosApp.LegislacaoCopiar, ComandosApp.LegislacaoCopiarCitacao]);
        return h;
    }
}
