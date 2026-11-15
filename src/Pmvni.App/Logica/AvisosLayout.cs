namespace Pmvni.App;

/// <summary>Os tipos de aviso de layout da composição: documento reduzido para caber na largura, conteúdo além da borda lateral da área
/// útil (informativos) e bloco maior que a página (bloqueia a exportação).</summary>
public enum TipoAvisoLayout { Estouro, Reducao, AlemDaBorda }

/// <summary>Um aviso de layout: o tipo, o texto curto (como aparece na lista do indicador) e a folha a que leva ("Ir à folha N"); null
/// quando vale para o documento inteiro.</summary>
public sealed record AvisoLayout(TipoAvisoLayout Tipo, string Texto, int? Folha)
{
    public bool Bloqueia => Tipo == TipoAvisoLayout.Estouro;
}

/// <summary>Indicador de layout (auditoria da interface, A2): os avisos que antes eram faixas empilhadas sobre as folhas viram uma lista só,
/// resumida no rodapé ("2 avisos de layout", "1 bloqueio"). Sem tipos WinUI: compilado também nos testes. Nenhum aviso some: a lista tem
/// todos, e "Faixas no topo" (Ajustes › Aparência) volta ao jeito antigo.</summary>
public static class AvisosLayout
{
    /// <summary>Onde os avisos aparecem: <c>indicador</c> (rodapé, padrão) ou <c>faixas</c> (InfoBars sobre as folhas).</summary>
    public static readonly IReadOnlyList<string> Modos = ["indicador", "faixas"];
    public const string ModoPadrao = "indicador";

    /// <summary>Quanto o documento foi reduzido para caber na largura, em % inteiro ("reduzido 20%" para a escala 1,25).</summary>
    public static int ReducaoPercentual(double escala) => (int)Math.Round((1 - 1 / escala) * 100, MidpointRounding.AwayFromZero);

    /// <summary>"Conteúdo mais largo que a página: o documento foi reduzido N% para caber (como na impressão do kit)."</summary>
    public static string TextoReducao(double escala) =>
        $"Conteúdo mais largo que a página: o documento foi reduzido {ReducaoPercentual(escala)}% para caber (como na impressão do kit).";

    /// <summary>A lista do plano de composição: primeiro os estouros (bloqueiam), depois a redução (se o plano avisou largura) e uma linha por
    /// folha com conteúdo além da borda.</summary>
    public static IReadOnlyList<AvisoLayout> Montar(double escala, int avisosLargura, IReadOnlyList<int> foraDaArea, IReadOnlyList<(int Folha, string Texto)> estouros)
    {
        var lista = new List<AvisoLayout>();
        foreach (var (folha, texto) in estouros) lista.Add(new AvisoLayout(TipoAvisoLayout.Estouro, texto, folha));
        if (avisosLargura > 0) lista.Add(new AvisoLayout(TipoAvisoLayout.Reducao, TextoReducao(escala), null));
        foreach (var folha in foraDaArea)
            lista.Add(new AvisoLayout(TipoAvisoLayout.AlemDaBorda,
                $"Folha {folha}: há conteúdo além da borda lateral da área útil (faixa vermelha). Nada é cortado no PDF do editor.", folha));
        return lista;
    }

    public static bool Bloqueia(IReadOnlyList<AvisoLayout> avisos) => avisos.Any(a => a.Bloqueia);

    /// <summary>O texto do indicador: "" sem avisos; "1 aviso de layout", "3 avisos de layout"; com estouro, "1 bloqueio · 2 avisos".</summary>
    public static string Resumo(IReadOnlyList<AvisoLayout> avisos)
    {
        var b = avisos.Count(a => a.Bloqueia);
        var n = avisos.Count - b;
        if (b == 0) return n == 0 ? "" : n == 1 ? "1 aviso de layout" : $"{n} avisos de layout";
        var bloqueios = b == 1 ? "1 bloqueio" : $"{b} bloqueios";
        return n == 0 ? bloqueios : bloqueios + " · " + (n == 1 ? "1 aviso" : $"{n} avisos");
    }
}
