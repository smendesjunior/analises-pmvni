using System.Buffers.Binary;
using System.Text;

namespace Pmvni.Composicao.Figuras;

/// <summary>Largura e altura de uma imagem PNG, JPEG ou GIF lidas do cabeçalho (sem decodificar). Null se não der para ler.</summary>
public static class TamanhoImagem
{
    public static (int Largura, int Altura)? Ler(ReadOnlySpan<byte> b)
    {
        switch (CaminhoImagem.ExtensaoPelaAssinatura(b))
        {
            case "png":
                if (b.Length < 24) return null;
                return Valido((int)BinaryPrimitives.ReadUInt32BigEndian(b[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(b[20..]));
            case "gif":
                if (b.Length < 10) return null;
                return Valido(BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));
            case "jpg":
                return Jpeg(b);
            default:
                return null;
        }
    }

    static (int, int)? Valido(int w, int h) => w > 0 && h > 0 && w <= 100_000 && h <= 100_000 ? (w, h) : null;

    static (int, int)? Jpeg(ReadOnlySpan<byte> b)
    {
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            var marca = b[i + 1];
            if (marca == 0xFF) { i++; continue; }
            if (marca is 0xD8 or 0x01 || marca is >= 0xD0 and <= 0xD7) { i += 2; continue; }
            var tamanho = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
            // SOF0..SOF15, menos DHT (C4), JPG (C8) e DAC (CC).
            if (marca is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return Valido(BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
            i += 2 + tamanho;
        }
        return null;
    }
}

/// <summary>A figura composta em SVG: a imagem base (embutida, sem perdas) entre duas margens brancas e, numa camada por cima, as marcações
/// no mesmo visual do <c>annotate_v4</c> do kit (números vermelhos com anel branco nas margens, setas vermelhas com halo branco, caixa com
/// contorno vermelho arredondado, véu translúcido e hachura diagonal). O relatório usa o SVG como <c>&lt;img&gt;</c> (data URI): a
/// imagem do projeto nunca é alterada, só desenhada por baixo.</summary>
public static class SvgFigura
{
    public const string Vermelho = "#C00000";

    public static string Compor(byte[] imagem, IReadOnlyList<MarcaDesenho> marcas)
    {
        ArgumentNullException.ThrowIfNull(imagem);
        ArgumentNullException.ThrowIfNull(marcas);
        var tam = TamanhoImagem.Ler(imagem) ?? throw new InvalidOperationException("Não foi possível ler o tamanho da imagem da figura.");
        var mime = CaminhoImagem.ExtensaoPelaAssinatura(imagem) switch
        {
            "png" => "image/png",
            "jpg" => "image/jpeg",
            "gif" => "image/gif",
            _ => throw new InvalidOperationException("Imagem da figura em formato não aceito."),
        };
        var m = MedidasFigura.De(tam.Largura, tam.Altura);
        var formas = GeometriaFigura.Calcular(m, marcas);
        var n = GeometriaFigura.N;
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"").Append(m.LarguraTotal)
          .Append("\" height=\"").Append(m.AlturaTotal).Append("\" viewBox=\"0 0 ").Append(m.LarguraTotal).Append(' ').Append(m.AlturaTotal).Append("\">");
        sb.Append("<defs><pattern id=\"h\" patternUnits=\"userSpaceOnUse\" width=\"9\" height=\"9\" patternTransform=\"rotate(-45)\">")
          .Append("<line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"9\" stroke=\"").Append(Vermelho).Append("\" stroke-opacity=\"0.27\" stroke-width=\"1\"/></pattern></defs>");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>");
        sb.Append("<image x=\"").Append(m.Margem).Append("\" y=\"0\" width=\"").Append(m.LarguraImagem).Append("\" height=\"").Append(m.AlturaImagem)
          .Append("\" preserveAspectRatio=\"none\" xlink:href=\"data:").Append(mime).Append(";base64,").Append(Convert.ToBase64String(imagem)).Append("\"/>");

        // 1) caixas (véu + hachura + contorno com halo)
        foreach (var f in formas)
        {
            if (f.Caixa is not { } c) continue;
            var w = c.X1 - c.X0;
            var h = c.Y1 - c.Y0;
            var geo = "x=\"" + n(c.X0) + "\" y=\"" + n(c.Y0) + "\" width=\"" + n(w) + "\" height=\"" + n(h) + "\" rx=\"" + n(f.RaioCaixa) + "\"";
            sb.Append("<rect ").Append(geo).Append(" fill=\"").Append(Vermelho).Append("\" fill-opacity=\"0.10\"/>");
            sb.Append("<rect ").Append(geo).Append(" fill=\"url(#h)\"/>");
            sb.Append("<rect ").Append(geo).Append(" fill=\"none\" stroke=\"#fff\" stroke-width=\"").Append(m.Halo).Append("\"/>");
            sb.Append("<rect ").Append(geo).Append(" fill=\"none\" stroke=\"").Append(Vermelho).Append("\" stroke-width=\"").Append(m.Traco).Append("\"/>");
        }

        // 2) setas: primeiro todos os halos brancos, depois o vermelho (como o kit)
        foreach (var (cor, largura, abertura) in new[] { ("#fff", m.Halo, .42), (Vermelho, m.Traco, .32) })
        {
            foreach (var f in formas)
            {
                if (f.InicioSeta is not { } a || f.PontaSeta is not { } p) continue;
                var fim = GeometriaFigura.FimDaHaste(a, p, m.Ponta);
                sb.Append("<line x1=\"").Append(n(a.X)).Append("\" y1=\"").Append(n(a.Y)).Append("\" x2=\"").Append(n(fim.X)).Append("\" y2=\"").Append(n(fim.Y))
                  .Append("\" stroke=\"").Append(cor).Append("\" stroke-width=\"").Append(largura).Append("\" stroke-linecap=\"round\"/>");
                var (v1, v2, v3) = GeometriaFigura.Cabeca(a, p, m.Ponta, abertura);
                sb.Append("<polygon points=\"").Append(n(v1.X)).Append(',').Append(n(v1.Y)).Append(' ').Append(n(v2.X)).Append(',').Append(n(v2.Y))
                  .Append(' ').Append(n(v3.X)).Append(',').Append(n(v3.Y)).Append("\" fill=\"").Append(cor).Append("\"/>");
            }
        }

        // 3) números
        foreach (var f in formas)
        {
            if (f.Numero is not { } c || f.Marca.Numero is not { } numero) continue;
            sb.Append("<circle cx=\"").Append(n(c.X)).Append("\" cy=\"").Append(n(c.Y)).Append("\" r=\"").Append(m.Raio + 2).Append("\" fill=\"#fff\"/>");
            sb.Append("<circle cx=\"").Append(n(c.X)).Append("\" cy=\"").Append(n(c.Y)).Append("\" r=\"").Append(m.Raio).Append("\" fill=\"").Append(Vermelho).Append("\"/>");
            sb.Append("<text x=\"").Append(n(c.X)).Append("\" y=\"").Append(n(c.Y)).Append("\" fill=\"#fff\" font-family=\"Arial, Helvetica, sans-serif\" font-weight=\"bold\" font-size=\"")
              .Append(m.Fonte).Append("\" text-anchor=\"middle\" dominant-baseline=\"central\">").Append(numero).Append("</text>");
        }

        // 4) textos livres
        foreach (var f in formas)
        {
            if (f.Texto is not { } t || string.IsNullOrWhiteSpace(f.Marca.Texto)) continue;
            sb.Append("<text x=\"").Append(n(t.X)).Append("\" y=\"").Append(n(t.Y)).Append("\" fill=\"").Append(Vermelho).Append("\" stroke=\"#fff\" stroke-width=\"")
              .Append(m.Halo).Append("\" paint-order=\"stroke\" stroke-linejoin=\"round\" font-family=\"Arial, Helvetica, sans-serif\" font-weight=\"bold\" font-size=\"")
              .Append(m.Fonte).Append("\" text-anchor=\"middle\" dominant-baseline=\"central\">").Append(Escapar(f.Marca.Texto!)).Append("</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>O SVG como data URI (base64), pronto para o <c>src</c> de um <c>&lt;img&gt;</c>.</summary>
    public static string DataUri(byte[] imagem, IReadOnlyList<MarcaDesenho> marcas) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(new UTF8Encoding(false).GetBytes(Compor(imagem, marcas)));

    static string Escapar(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>As marcações de uma figura do dados.json (chave <c>marcacoes</c>, lista de objetos), no modelo do molde. Itens inválidos
    /// são ignorados (o relatório nunca quebra por uma marcação malformada).</summary>
    internal static List<MarcaDesenho> DoDados(object? marcacoes)
    {
        var lista = new List<MarcaDesenho>();
        if (marcacoes is not System.Collections.IEnumerable e || marcacoes is string) return lista;
        foreach (var o in e)
        {
            if (o is not IReadOnlyDictionary<string, object?> d) continue;
            var tipo = d.TryGetValue("tipo", out var t) ? t as string : null;
            if (tipo is null || !TiposMarcacao.Valido(tipo)) continue;
            int? numero = d.TryGetValue("numero", out var nn) && Numero(nn) is { } dn ? (int)dn : null;
            var lado = d.TryGetValue("lado", out var l) && l as string == "direita" ? "direita" : "esquerda";
            var altura = d.TryGetValue("altura", out var a) && Numero(a) is { } da ? da : .5;
            lista.Add(new MarcaDesenho(tipo, numero, Vetor(d, "alvo", 2), Vetor(d, "caixa", 4), lado, altura, Vetor(d, "posicao", 2),
                d.TryGetValue("texto", out var tx) ? tx as string : null));
        }
        return lista;
    }

    static double? Numero(object? o) => o switch
    {
        double d when double.IsFinite(d) => d,
        float f when float.IsFinite(f) => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        System.Numerics.BigInteger b => (double)b,
        _ => null,
    };

    static double[]? Vetor(IReadOnlyDictionary<string, object?> d, string chave, int tamanho)
    {
        if (!d.TryGetValue(chave, out var o) || o is not System.Collections.IEnumerable e || o is string) return null;
        var v = new List<double>();
        foreach (var x in e)
        {
            if (Numero(x) is not { } n) return null;
            v.Add(n);
        }
        return v.Count == tamanho ? v.ToArray() : null;
    }
}
