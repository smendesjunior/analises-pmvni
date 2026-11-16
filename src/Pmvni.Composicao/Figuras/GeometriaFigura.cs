using System.Globalization;

namespace Pmvni.Composicao.Figuras;

/// <summary>Tipos de marcação desenhados sobre a imagem de uma figura.</summary>
public static class TiposMarcacao
{
    /// <summary>Caixa vermelha hachurada sobre o elemento + seta + número na margem (erro ou pendência).</summary>
    public const string Regiao = "regiao";

    /// <summary>Só a seta até o ponto + número na margem (o que atende).</summary>
    public const string Seta = "seta";

    /// <summary>Texto livre em vermelho sobre a imagem, sem número.</summary>
    public const string Texto = "texto";

    public static bool Numerada(string tipo) => tipo is Regiao or Seta;

    public static bool Valido(string tipo) => tipo is Regiao or Seta or Texto;
}

/// <summary>Uma marcação a desenhar, em coordenadas NORMALIZADAS 0–1 da imagem base (a mesma convenção do
/// <c>prepare_visual_evidence.py</c> do kit: <c>target</c> = <see cref="Alvo"/>, <c>target_box</c> = <see cref="Caixa"/>).
/// <see cref="Lado"/> (<c>esquerda</c>/<c>direita</c>) e <see cref="Altura"/> (0–1) dizem onde fica o número, na margem lateral, como o
/// <c>start</c> do kit. <see cref="Posicao"/> é o centro de um texto livre. <see cref="Numero"/> é o número visível (null no texto).</summary>
public sealed record MarcaDesenho(
    string Tipo,
    int? Numero,
    double[]? Alvo,
    double[]? Caixa,
    string Lado,
    double Altura,
    double[]? Posicao,
    string? Texto,
    string? Id = null);

/// <summary>Um ponto em pixels da figura composta (imagem base + margens laterais).</summary>
public readonly record struct Ponto(double X, double Y);

/// <summary>A geometria calculada de uma marcação, em pixels da figura composta.</summary>
public sealed record FormaMarca(
    MarcaDesenho Marca,
    Ponto? Numero,
    Ponto? InicioSeta,
    Ponto? PontaSeta,
    (double X0, double Y0, double X1, double Y1)? Caixa,
    double RaioCaixa,
    Ponto? Texto);

/// <summary>Medidas da política visual 4 do kit para uma imagem de <c>iw × ih</c> px: tamanho da fonte do número, raio do círculo,
/// largura de cada margem lateral, espessura do traço, halo branco e tamanho da ponta da seta (as mesmas fórmulas do
/// <c>annotate_v4</c>).</summary>
public sealed record MedidasFigura(int LarguraImagem, int AlturaImagem, int Fonte, int Raio, int Margem, int Traco, int Halo, int Ponta)
{
    public int LarguraTotal => LarguraImagem + 2 * Margem;
    public int AlturaTotal => AlturaImagem;

    public static MedidasFigura De(int iw, int ih)
    {
        if (iw <= 0 || ih <= 0) throw new ArgumentOutOfRangeException(nameof(iw), "Imagem sem tamanho.");
        var size = Math.Max(18, Arred(iw / 40.0));
        var r = Math.Max(14, Arred(size * .8));
        var gutter = Arred(2 * r + size * 1.8);
        var width = Math.Max(2, Arred(iw / 600.0));
        var halo = width + 3;
        var head = Math.Max(12, Arred(r * .8));
        return new MedidasFigura(iw, ih, size, r, gutter, width, halo, head);
    }

    /// <summary><c>round()</c> do Python 3 (metade para o par).</summary>
    static int Arred(double x) => (int)Math.Round(x, MidpointRounding.ToEven);

    // ---------- transformações (documentadas na especificação §9) ----------

    /// <summary>Normalizado (0–1 da imagem base) → pixel da figura composta.</summary>
    public Ponto ParaPixel(double nx, double ny) => new(Margem + nx * LarguraImagem, ny * AlturaImagem);

    /// <summary>Pixel da figura composta → normalizado (0–1 da imagem base), sem limitar (a margem dá x &lt; 0 ou &gt; 1).</summary>
    public (double X, double Y) ParaNormalizado(Ponto p) => ((p.X - Margem) / LarguraImagem, p.Y / AlturaImagem);

    /// <summary>Centro do número de uma marcação no lado e altura dados (altura limitada para o círculo caber, como o kit).</summary>
    public Ponto CentroNumero(string lado, double altura)
    {
        var cx = lado == "direita" ? Margem + LarguraImagem + Margem / 2.0 : Margem / 2.0;
        var lo = Raio + 4.0;
        var hi = AlturaImagem - Raio - 4.0;
        var cy = altura * AlturaImagem;
        cy = hi < lo ? AlturaImagem / 2.0 : Math.Clamp(cy, lo, hi);
        return new Ponto(cx, cy);
    }

    /// <summary>Raio do arredondamento da caixa (como o kit: até 12 px, sem passar de ¼ do lado menor).</summary>
    public static double RaioDaCaixa(double w, double h) => Math.Max(0, Math.Min(12, Math.Min(w / 4, h / 4)));
}

/// <summary>Calcula onde cada marcação fica na figura composta (puro e determinístico; usado pelo SVG do relatório e pelo editor).</summary>
public static class GeometriaFigura
{
    public static IReadOnlyList<FormaMarca> Calcular(MedidasFigura m, IEnumerable<MarcaDesenho> marcas)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(marcas);
        var lista = new List<FormaMarca>();
        foreach (var x in marcas) lista.Add(Forma(m, x));
        return lista;
    }

    public static FormaMarca Forma(MedidasFigura m, MarcaDesenho x)
    {
        if (x.Tipo == TiposMarcacao.Texto)
        {
            var pos = x.Posicao is { Length: 2 } p ? m.ParaPixel(p[0], p[1]) : m.ParaPixel(.5, .5);
            return new FormaMarca(x, null, null, null, null, 0, pos);
        }

        (double, double, double, double)? caixa = null;
        double raio = 0;
        if (x.Tipo == TiposMarcacao.Regiao && x.Caixa is { Length: 4 } b)
        {
            var a = m.ParaPixel(Math.Min(b[0], b[2]), Math.Min(b[1], b[3]));
            var z = m.ParaPixel(Math.Max(b[0], b[2]), Math.Max(b[1], b[3]));
            caixa = (a.X, a.Y, z.X, z.Y);
            raio = MedidasFigura.RaioDaCaixa(z.X - a.X, z.Y - a.Y);
        }

        var centro = m.CentroNumero(x.Lado, x.Altura);
        Ponto ponta;
        if (caixa is { } c)
        {
            // Ponta da seta: na borda da área voltada para o número (como o kit); número dentro da faixa horizontal da caixa: no centro.
            var px = x.Lado == "direita" ? c.Item3 : c.Item1;
            var py = Math.Clamp(centro.Y, c.Item2, c.Item4);
            ponta = new Ponto(px, py);
        }
        else
        {
            var alvo = x.Alvo is { Length: 2 } t ? t : new[] { .5, .5 };
            ponta = m.ParaPixel(alvo[0], alvo[1]);
        }

        var dx = ponta.X - centro.X;
        var dy = ponta.Y - centro.Y;
        var l = Math.Sqrt(dx * dx + dy * dy);
        Ponto? inicio = l > m.Raio ? new Ponto(centro.X + dx / l * m.Raio, centro.Y + dy / l * m.Raio) : null;
        return new FormaMarca(x, centro, inicio, inicio is null ? null : ponta, caixa, raio, null);
    }

    /// <summary>Os três vértices da ponta da seta (como o <c>annotate_v4</c>: comprimento <c>Ponta</c>, abertura de 0,32 para cada lado; o halo
    /// usa 0,42).</summary>
    public static (Ponto A, Ponto B, Ponto C) Cabeca(Ponto inicio, Ponto ponta, double tamanho, double abertura)
    {
        var dx = ponta.X - inicio.X;
        var dy = ponta.Y - inicio.Y;
        var l = Math.Sqrt(dx * dx + dy * dy);
        if (l == 0) l = 1;
        var ux = dx / l;
        var uy = dy / l;
        var b = new Ponto(ponta.X - ux * tamanho - uy * tamanho * abertura, ponta.Y - uy * tamanho + ux * tamanho * abertura);
        var c = new Ponto(ponta.X - ux * tamanho + uy * tamanho * abertura, ponta.Y - uy * tamanho - ux * tamanho * abertura);
        return (ponta, b, c);
    }

    /// <summary>A haste da seta termina antes da ponta (0,8 da cabeça), para a ponta ficar fina.</summary>
    public static Ponto FimDaHaste(Ponto inicio, Ponto ponta, double tamanho)
    {
        var dx = ponta.X - inicio.X;
        var dy = ponta.Y - inicio.Y;
        var l = Math.Sqrt(dx * dx + dy * dy);
        if (l <= tamanho * .8) return inicio;
        return new Ponto(ponta.X - dx / l * tamanho * .8, ponta.Y - dy / l * tamanho * .8);
    }

    internal static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
