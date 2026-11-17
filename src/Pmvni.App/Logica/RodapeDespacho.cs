using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O rodapé da prévia do despacho (1.0.0): como no Word, o rodapé fica preso à distância de baixo da folha; se um parágrafo quebra
/// em mais linhas (o endereço com o site institucional, no modelo do kit), o rodapé cresce para cima. Sem tipos WinUI: compilada nos testes.</summary>
public static class RodapeDespacho
{
    /// <summary>O topo (pt) de cada parágrafo do rodapé, empilhados de baixo para cima com as alturas medidas, e quanto o rodapé subiu
    /// (pt) em relação ao desenho de uma linha por parágrafo (a linha vermelha sobe o mesmo).</summary>
    public static (IReadOnlyList<double> Topos, double Subida) Posicionar(IReadOnlyList<ParagrafoRodape> rodape, IReadOnlyList<double> alturas)
    {
        if (rodape.Count == 0 || alturas.Count != rodape.Count) return (rodape.Select(r => r.TopoPt).ToList(), 0);
        var fim = rodape[^1].TopoPt + rodape[^1].AlturaPt;
        var subida = Math.Max(0, alturas.Sum() - rodape.Sum(r => r.AlturaPt));
        var topos = new double[rodape.Count];
        var y = fim;
        for (var i = rodape.Count - 1; i >= 0; i--)
        {
            y -= alturas[i];
            topos[i] = y;
        }
        return (topos, subida);
    }
}
