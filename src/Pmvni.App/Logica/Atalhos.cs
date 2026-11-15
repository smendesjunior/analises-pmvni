namespace Pmvni.App;

/// <summary>Atalhos de teclado do app (desenho §5.5): tecla canônica → id do comando. É o mesmo mapa para os
/// <c>KeyboardAccelerator</c>s da janela e para a mensagem <c>atalho</c> da página (as 16 teclas que <c>edicao.ts</c> repassa).
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
        ["Ctrl+Z"] = ComandosApp.EditarDesfazer,
        ["Ctrl+Y"] = ComandosApp.EditarRefazer,
        ["Ctrl+F"] = ComandosApp.PesquisaFocar,
        ["Alt+ArrowUp"] = ComandosApp.InicioItemAnterior,
        ["Alt+ArrowDown"] = ComandosApp.InicioItemProximo,
        ["Delete"] = ComandosApp.InicioExcluirRestaurar,
        ["F2"] = ComandosApp.InicioEditarTexto,
        ["F6"] = ComandosApp.JanelaRegioes,
        ["Ctrl+0"] = ComandosApp.ExibirLargura,
        ["Ctrl+Plus"] = ComandosApp.ExibirMais,
        ["Ctrl+Minus"] = ComandosApp.ExibirMenos,
    };

    static readonly Dictionary<string, string> PorComando = Mapa.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    /// <summary>O comando da tecla canônica, ou null se ela não é atalho do app.</summary>
    public static string? Comando(string tecla) => Mapa.TryGetValue(tecla, out var id) ? id : null;

    /// <summary>A tecla canônica do comando (para dicas e para a lista de atalhos), ou null se ele não tem atalho.</summary>
    public static string? Tecla(string id) => PorComando.TryGetValue(id, out var t) ? t : null;

    /// <summary>A tecla como o usuário lê: "Ctrl++", "Alt+↑", "Del".</summary>
    public static string Legivel(string tecla) => tecla
        .Replace("Plus", "+", StringComparison.Ordinal).Replace("Minus", "−", StringComparison.Ordinal)
        .Replace("ArrowUp", "↑", StringComparison.Ordinal).Replace("ArrowDown", "↓", StringComparison.Ordinal)
        .Replace("Delete", "Del", StringComparison.Ordinal);
}
