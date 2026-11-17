using Pmvni.Composicao.Figuras;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Parte de uma marcação sob o mouse no editor de figura.</summary>
public enum AlcaFigura { Nenhuma, Numero, Caixa, CantoSuperiorEsquerdo, CantoSuperiorDireito, CantoInferiorDireito, CantoInferiorEsquerdo, Ponta, Haste, Texto }

/// <summary>O que está sob o mouse: a marcação (id) e a parte dela.</summary>
public sealed record AcertoFigura(string Marcacao, AlcaFigura Alca);

/// <summary>A matemática do editor de figura, sem tela (testável): o que está sob o mouse, a marcação durante um arrasto e o comando que
/// o arrasto gera. Tudo em pixels da figura composta (imagem + margens) e em coordenadas normalizadas da imagem.</summary>
public static class InteracaoFigura
{
    /// <summary>O que está em <paramref name="p"/>: primeiro as alças da marcação selecionada (cantos da caixa, ponta da seta), depois, da
    /// marcação de cima para a de baixo, o número, o texto, a caixa e a haste. <paramref name="tolerancia"/> em pixels da figura.</summary>
    public static AcertoFigura? Acertar(MedidasFigura m, IReadOnlyList<MarcaDesenho> marcas, Ponto p, string? selecionada, double tolerancia)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(marcas);
        var formas = GeometriaFigura.Calcular(m, marcas);
        if (selecionada is not null && formas.FirstOrDefault(f => f.Marca.Id == selecionada) is { } sel)
        {
            if (sel.Caixa is { } c)
            {
                var cantos = new (double X, double Y, AlcaFigura A)[]
                {
                    (c.X0, c.Y0, AlcaFigura.CantoSuperiorEsquerdo), (c.X1, c.Y0, AlcaFigura.CantoSuperiorDireito),
                    (c.X1, c.Y1, AlcaFigura.CantoInferiorDireito), (c.X0, c.Y1, AlcaFigura.CantoInferiorEsquerdo),
                };
                foreach (var k in cantos)
                    if (Math.Abs(p.X - k.X) <= tolerancia && Math.Abs(p.Y - k.Y) <= tolerancia) return new(selecionada, k.A);
            }
            if (sel.Marca.Tipo == TiposMarcacao.Seta && sel.PontaSeta is { } ponta && Distancia(p, ponta) <= tolerancia * 1.5)
                return new(selecionada, AlcaFigura.Ponta);
        }
        for (var i = formas.Count - 1; i >= 0; i--)
        {
            var f = formas[i];
            var id = f.Marca.Id ?? "";
            if (f.Numero is { } n && Distancia(p, n) <= m.Raio + 2 + tolerancia / 2) return new(id, AlcaFigura.Numero);
            if (f.Texto is { } t)
            {
                var (w, h) = TamanhoTexto(m, f.Marca.Texto ?? "");
                if (Math.Abs(p.X - t.X) <= w / 2 + tolerancia / 2 && Math.Abs(p.Y - t.Y) <= h / 2 + tolerancia / 2) return new(id, AlcaFigura.Texto);
            }
            if (f.Caixa is { } c && p.X >= c.X0 - tolerancia / 2 && p.X <= c.X1 + tolerancia / 2 && p.Y >= c.Y0 - tolerancia / 2 && p.Y <= c.Y1 + tolerancia / 2)
                return new(id, AlcaFigura.Caixa);
            if (f.InicioSeta is { } a && f.PontaSeta is { } b && DistanciaAoSegmento(p, a, b) <= tolerancia / 2 + m.Traco)
                return new(id, f.Marca.Tipo == TiposMarcacao.Seta ? AlcaFigura.Haste : AlcaFigura.Caixa);
        }
        return null;
    }

    /// <summary>Tamanho aproximado de um texto livre (largura média de 0,6 da fonte por caractere, negrito).</summary>
    public static (double Largura, double Altura) TamanhoTexto(MedidasFigura m, string texto) =>
        (Math.Max(m.Fonte, texto.Length * m.Fonte * .6), m.Fonte * 1.3);

    /// <summary>A marcação como fica com a alça arrastada de <paramref name="inicio"/> até <paramref name="atual"/> (normalizado e limitado à
    /// imagem; a caixa nunca inverte nem some).</summary>
    public static MarcaDesenho Arrastar(MedidasFigura m, MarcaDesenho x, AlcaFigura alca, Ponto inicio, Ponto atual)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(x);
        var (nx, ny) = m.ParaNormalizado(atual);
        var dx = (atual.X - inicio.X) / m.LarguraImagem;
        var dy = (atual.Y - inicio.Y) / m.AlturaImagem;
        switch (alca)
        {
            case AlcaFigura.Numero:
                return x with { Lado = atual.X < m.Margem + m.LarguraImagem / 2.0 ? "esquerda" : "direita", Altura = Math.Clamp(ny, 0, 1) };
            case AlcaFigura.Ponta:
            case AlcaFigura.Haste when x.Alvo is not null:
                if (alca == AlcaFigura.Haste)
                    return x with { Alvo = [Math.Clamp(x.Alvo![0] + dx, 0, 1), Math.Clamp(x.Alvo[1] + dy, 0, 1)] };
                return x with { Alvo = [Math.Clamp(nx, 0, 1), Math.Clamp(ny, 0, 1)] };
            case AlcaFigura.Texto when x.Posicao is { Length: 2 } pos:
                return x with { Posicao = [Math.Clamp(pos[0] + dx, 0, 1), Math.Clamp(pos[1] + dy, 0, 1)] };
            case AlcaFigura.Caixa when x.Caixa is { Length: 4 } c:
            {
                var w = c[2] - c[0];
                var h = c[3] - c[1];
                var x0 = Math.Clamp(c[0] + dx, 0, 1 - w);
                var y0 = Math.Clamp(c[1] + dy, 0, 1 - h);
                // A seta acompanha a caixa: o número sobe ou desce junto.
                return x with { Caixa = [x0, y0, x0 + w, y0 + h], Altura = Math.Clamp(x.Altura + (y0 - c[1]), 0, 1) };
            }
            case AlcaFigura.CantoSuperiorEsquerdo or AlcaFigura.CantoSuperiorDireito or AlcaFigura.CantoInferiorDireito or AlcaFigura.CantoInferiorEsquerdo
                when x.Caixa is { Length: 4 } c:
            {
                double x0 = c[0], y0 = c[1], x1 = c[2], y1 = c[3];
                var px = Math.Clamp(nx, 0, 1);
                var py = Math.Clamp(ny, 0, 1);
                const double minimo = .01;
                if (alca is AlcaFigura.CantoSuperiorEsquerdo or AlcaFigura.CantoInferiorEsquerdo) x0 = Math.Min(px, x1 - minimo); else x1 = Math.Max(px, x0 + minimo);
                if (alca is AlcaFigura.CantoSuperiorEsquerdo or AlcaFigura.CantoSuperiorDireito) y0 = Math.Min(py, y1 - minimo); else y1 = Math.Max(py, y0 + minimo);
                return x with { Caixa = [Math.Max(0, x0), Math.Max(0, y0), Math.Min(1, x1), Math.Min(1, y1)] };
            }
            default:
                return x;
        }
    }

    /// <summary>O comando que grava o arrasto (null se nada mudou).</summary>
    public static Comando? ComandoDoArrasto(string figura, MarcaDesenho antes, MarcaDesenho depois, AlcaFigura alca)
    {
        ArgumentNullException.ThrowIfNull(antes);
        ArgumentNullException.ThrowIfNull(depois);
        var id = antes.Id ?? throw new ArgumentException("Marcação sem id.", nameof(antes));
        if (Igual(antes.Alvo, depois.Alvo) && Igual(antes.Caixa, depois.Caixa) && Igual(antes.Posicao, depois.Posicao) && antes.Lado == depois.Lado
            && Math.Abs(antes.Altura - depois.Altura) < 1e-6)
            return null;
        return alca switch
        {
            AlcaFigura.Numero => new MoverMarcacao(figura, id, Lado: depois.Lado, Altura: depois.Altura),
            AlcaFigura.Ponta or AlcaFigura.Haste => new MoverMarcacao(figura, id, Alvo: depois.Alvo),
            AlcaFigura.Texto => new MoverMarcacao(figura, id, Posicao: depois.Posicao),
            AlcaFigura.Caixa => new MoverMarcacao(figura, id, Caixa: depois.Caixa, Altura: depois.Altura),
            AlcaFigura.Nenhuma => null,
            _ => new RedimensionarMarcacao(figura, id, depois.Caixa!),
        };
    }

    /// <summary>A caixa normalizada do retângulo arrastado entre dois pixels da figura (limitada à imagem).</summary>
    public static double[] CaixaDoArrasto(MedidasFigura m, Ponto a, Ponto b)
    {
        var (ax, ay) = m.ParaNormalizado(a);
        var (bx, by) = m.ParaNormalizado(b);
        return [Math.Clamp(Math.Min(ax, bx), 0, 1), Math.Clamp(Math.Min(ay, by), 0, 1), Math.Clamp(Math.Max(ax, bx), 0, 1), Math.Clamp(Math.Max(ay, by), 0, 1)];
    }

    /// <summary>O ponto está sobre a imagem (e não nas margens laterais, onde ficam os números)?</summary>
    public static bool DentroDaImagem(MedidasFigura m, Ponto p)
    {
        ArgumentNullException.ThrowIfNull(m);
        var (nx, ny) = m.ParaNormalizado(p);
        return nx is >= 0 and <= 1 && ny is >= 0 and <= 1;
    }

    /// <summary>A região de um clique sem arrasto com "Marcar região": um quadrado (na tela) de 12% do lado menor da imagem, no mínimo
    /// 24 px, centrado no clique e empurrado para dentro da imagem. Null fora da imagem.</summary>
    public static double[]? CaixaDoClique(MedidasFigura m, Ponto p)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (!DentroDaImagem(m, p)) return null;
        var (nx, ny) = m.ParaNormalizado(p);
        var lado = Math.Max(24, Math.Min(m.LarguraImagem, m.AlturaImagem) * .12);
        var w = Math.Min(1, lado / m.LarguraImagem);
        var h = Math.Min(1, lado / m.AlturaImagem);
        var x0 = Math.Clamp(nx - w / 2, 0, 1 - w);
        var y0 = Math.Clamp(ny - h / 2, 0, 1 - h);
        return [x0, y0, x0 + w, y0 + h];
    }

    static bool Igual(double[]? a, double[]? b) =>
        a is null ? b is null : b is not null && a.Length == b.Length && a.Zip(b).All(z => Math.Abs(z.First - z.Second) < 1e-6);

    static double Distancia(Ponto a, Ponto b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    static double DistanciaAoSegmento(Ponto p, Ponto a, Ponto b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var l2 = vx * vx + vy * vy;
        if (l2 == 0) return Distancia(p, a);
        var t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2, 0, 1);
        return Distancia(p, new Ponto(a.X + t * vx, a.Y + t * vy));
    }
}
