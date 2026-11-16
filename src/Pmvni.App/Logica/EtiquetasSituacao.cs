using Pmvni.Composicao.Kit;

namespace Pmvni.App;

/// <summary>Cores de uma etiqueta de situação (ARGB): fundo, texto, borda e o ponto da esquerda.</summary>
public readonly record struct CoresEtiqueta(uint Fundo, uint Texto, uint Borda, uint Ponto);

/// <summary>Etiqueta de situação da lista de seções (correções 2026-11-15): a mesma "pill" do relatório (classes g, a, o, r do
/// relatorio.css do kit: fundo claro, texto escuro, borda e ponto), com uma variante para o tema escuro (fundo translúcido da cor do
/// ponto e texto claro). O texto é o impresso no relatório (<see cref="Situacoes.Sit"/>), em caixa alta.</summary>
public static class EtiquetasSituacao
{
    // Tema claro: exatamente as cores do .pill do kit (rev10/relatorio.css).
    static readonly Dictionary<string, CoresEtiqueta> Claro = new(StringComparer.Ordinal)
    {
        ["g"] = new(0xFFD3EFDD, 0xFF1E6E43, 0xFF8FD0A9, 0xFF2F9E62),
        ["a"] = new(0xFFFDEBB3, 0xFF7A5600, 0xFFEBC55A, 0xFFE0A800),
        ["o"] = new(0xFFFBD9C4, 0xFF9A4310, 0xFFEFA77A, 0xFFE2701F),
        ["r"] = new(0xFFF9D2CE, 0xFF9B1C12, 0xFFEC9890, 0xFFD93025),
    };

    // Tema escuro (painel de navegação, correções 2026-11-15: cores menos saturadas): o ponto do kit, fundo com 16% dele e borda
    // discreta com 30%; texto clareado para o contraste sobre o fundo escuro.
    static readonly Dictionary<string, CoresEtiqueta> Escuro = new(StringComparer.Ordinal)
    {
        ["g"] = new(0x292F9E62, 0xFF9BE0B7, 0x4D2F9E62, 0xFF4CC283),
        ["a"] = new(0x29E0A800, 0xFFF6D67A, 0x4DE0A800, 0xFFF0BE2A),
        ["o"] = new(0x29E2701F, 0xFFF7B88C, 0x4DE2701F, 0xFFF08A45),
        ["r"] = new(0x29D93025, 0xFFF6ABA4, 0x4DD93025, 0xFFF0584D),
    };

    /// <summary>Texto da etiqueta ("ATENDE", "FALTA COMPROVAR"…); "" sem situação ou com código desconhecido.</summary>
    public static string Texto(string? codigo) => codigo is not null && Situacoes.Sit.TryGetValue(codigo, out var s) ? s.Texto : "";

    /// <summary>Classe de cor do kit (g, a, o, r); null sem situação ou com código desconhecido.</summary>
    public static string? Classe(string? codigo) => codigo is not null && Situacoes.Sit.TryGetValue(codigo, out var s) ? s.Cor : null;

    /// <summary>Cores da etiqueta no tema pedido; null sem situação ou com código desconhecido.</summary>
    public static CoresEtiqueta? Cores(string? codigo, bool escuro) =>
        Classe(codigo) is { } c && (escuro ? Escuro : Claro).TryGetValue(c, out var cores) ? cores : null;

    /// <summary>Nome acessível do botão da etiqueta: "Situação: FALTA COMPROVAR. Mudar a situação".</summary>
    public static string NomeAcessivel(string? codigo) => Texto(codigo) is { Length: > 0 } t ? $"Situação: {t}. Mudar a situação" : "";
}
