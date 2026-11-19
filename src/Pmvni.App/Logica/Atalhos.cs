namespace Pmvni.App;

/// <summary>Atalhos de teclado do app (desenho §5.5): tecla canônica → id do comando. É o mesmo mapa para os
/// <c>KeyboardAccelerator</c>s da janela e para a mensagem <c>atalho</c> da página (as 22 teclas que <c>edicao.ts</c> repassa; Ctrl+1 … Ctrl+5 levam aos módulos do trilho).
/// Nomes canônicos: modificadores na ordem Ctrl, Alt, Shift; letras maiúsculas; <c>Plus</c>/<c>Minus</c>; teclas pelo nome do
/// <c>KeyboardEvent.key</c> (<c>ArrowUp</c>, <c>Delete</c>, <c>F2</c>).</summary>
public static class Atalhos
{
    public static IReadOnlyDictionary<string, string> Mapa { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Ctrl+O"] = ComandosApp.ArquivoAbrir,
        ["Ctrl+I"] = ComandosApp.ArquivoImportar,
        ["Ctrl+S"] = ComandosApp.ArquivoSalvar,
        ["Ctrl+Shift+S"] = ComandosApp.ArquivoSalvarComo,
        ["Ctrl+E"] = ComandosApp.ArquivoExportarPdf,
        ["Ctrl+P"] = ComandosApp.ArquivoImprimir,
        ["Ctrl+Z"] = ComandosApp.EditarDesfazer,
        ["Ctrl+Y"] = ComandosApp.EditarRefazer,
        ["Ctrl+K"] = ComandosApp.PesquisaFocar,
        ["Ctrl+F"] = ComandosApp.PesquisaFocar,
        ["Alt+ArrowUp"] = ComandosApp.InicioItemAnterior,
        ["Alt+ArrowDown"] = ComandosApp.InicioItemProximo,
        ["Delete"] = ComandosApp.InicioExcluirRestaurar,
        ["F2"] = ComandosApp.InicioEditarTexto,
        ["F6"] = ComandosApp.JanelaRegioes,
        ["Ctrl+0"] = ComandosApp.ExibirLargura,
        ["Ctrl+Plus"] = ComandosApp.ExibirMais,
        ["Ctrl+Minus"] = ComandosApp.ExibirMenos,
        ["Ctrl+1"] = ComandosApp.ModuloRelatorios,
        ["Ctrl+2"] = ComandosApp.ModuloDespachos,
        ["Ctrl+3"] = ComandosApp.ModuloProcessos,
        ["Ctrl+4"] = ComandosApp.ModuloLegislacao,
        ["Ctrl+5"] = ComandosApp.ModuloAjustes,
    };

    /// <summary>Os atalhos do leitor da Legislação (desenho §11.4): exatamente as 16 teclas que <c>leitor.ts</c> repassa ao host (as do leitor e Ctrl+1 … Ctrl+5 do trilho).</summary>
    public static IReadOnlyDictionary<string, string> MapaLegislacao { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Ctrl+K"] = ComandosApp.PesquisaFocar,
        ["Ctrl+F"] = ComandosApp.PesquisaFocar,
        ["Ctrl+C"] = ComandosApp.LegislacaoCopiar,
        ["Ctrl+Shift+C"] = ComandosApp.LegislacaoCopiarCitacao,
        ["Ctrl+Plus"] = ComandosApp.LegislacaoZoomMais,
        ["Ctrl+Minus"] = ComandosApp.LegislacaoZoomMenos,
        ["Ctrl+0"] = ComandosApp.LegislacaoAjustarLargura,
        ["Alt+ArrowUp"] = ComandosApp.LegislacaoPaginaAnterior,
        ["Alt+ArrowDown"] = ComandosApp.LegislacaoPaginaProxima,
        ["F3"] = ComandosApp.LegislacaoOcorrenciaProxima,
        ["Shift+F3"] = ComandosApp.LegislacaoOcorrenciaAnterior,
        ["Ctrl+P"] = ComandosApp.ArquivoImprimir,
        ["Ctrl+1"] = ComandosApp.ModuloRelatorios,
        ["Ctrl+2"] = ComandosApp.ModuloDespachos,
        ["Ctrl+3"] = ComandosApp.ModuloProcessos,
        ["Ctrl+4"] = ComandosApp.ModuloLegislacao,
        ["Ctrl+5"] = ComandosApp.ModuloAjustes,
    };

    /// <summary>Imóveis: o <see cref="Mapa"/>, com Ctrl+P no Imprimir do relatório do imóvel (<see cref="ComandosApp.ImoveisImprimir"/>).</summary>
    public static IReadOnlyDictionary<string, string> MapaImoveis { get; } =
        Mapa.ToDictionary(p => p.Key, p => p.Value == ComandosApp.ArquivoImprimir ? ComandosApp.ImoveisImprimir : p.Value, StringComparer.Ordinal);

    /// <summary>O mapa do módulo: o do leitor na Legislação; o de Imóveis lá; o <see cref="Mapa"/> nos outros.</summary>
    public static IReadOnlyDictionary<string, string> MapaDe(Modulo m) => m switch
    {
        Modulo.Legislacao => MapaLegislacao,
        Modulo.Imoveis => MapaImoveis,
        _ => Mapa,
    };

    /// <summary>O comando da tecla no módulo <paramref name="m"/>, ou null.</summary>
    public static string? Comando(string tecla, Modulo m) => MapaDe(m).TryGetValue(tecla, out var id) ? id : null;

    static readonly Dictionary<string, string> PorComando = Mapa.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    /// <summary>O comando da tecla canônica, ou null se ela não é atalho do app.</summary>
    public static string? Comando(string tecla) => Mapa.TryGetValue(tecla, out var id) ? id : null;

    /// <summary>A tecla canônica do comando (para dicas e para a lista de atalhos), ou null se ele não tem atalho.</summary>
    public static string? Tecla(string id) => PorComando.TryGetValue(id, out var t) ? t : null;

    static readonly Dictionary<string, string> PorComandoLegislacao = MapaLegislacao.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
    static readonly Dictionary<string, string> PorComandoImoveis = MapaImoveis.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    /// <summary>A tecla do comando no módulo (para dicas e para a lista de atalhos), ou null.</summary>
    public static string? Tecla(string id, Modulo m) =>
        (m switch { Modulo.Legislacao => PorComandoLegislacao, Modulo.Imoveis => PorComandoImoveis, _ => PorComando }).TryGetValue(id, out var t) ? t : null;

    /// <summary>A tecla como o usuário lê: "Ctrl++", "Alt+↑", "Del".</summary>
    public static string Legivel(string tecla) => tecla
        .Replace("Plus", "+", StringComparison.Ordinal).Replace("Minus", "−", StringComparison.Ordinal)
        .Replace("ArrowUp", "↑", StringComparison.Ordinal).Replace("ArrowDown", "↓", StringComparison.Ordinal)
        .Replace("Delete", "Del", StringComparison.Ordinal);
}
