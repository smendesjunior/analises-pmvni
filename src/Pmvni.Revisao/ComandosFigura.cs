using System.Globalization;
using System.Text.Json.Nodes;
using Pmvni.Composicao.Figuras;

namespace Pmvni.Revisao;

/// <summary>Um comando sobre uma figura (o <see cref="Comando.Item"/> é o id da figura). Vai para o histórico como os de item: desfazer,
/// refazer, diário de recuperação e Comparar valem igual.</summary>
public abstract record ComandoFigura(string Figura) : Comando(Figura);

/// <summary>Inclui uma marcação editável: <c>regiao</c> (caixa + seta + número), <c>seta</c> (seta + número) ou <c>texto</c> (texto livre).
/// Coordenadas normalizadas 0–1 da imagem; <paramref name="Lado"/>/<paramref name="Altura"/> = onde fica o número (na margem).</summary>
public sealed record IncluirMarcacao(string Figura, string Marcacao, string Tipo, double[]? Alvo = null, double[]? Caixa = null,
    string? Lado = null, double? Altura = null, double[]? Posicao = null, string? Texto = null, string? Elemento = null) : ComandoFigura(Figura);

/// <summary>Move uma marcação: a ponta da seta (<paramref name="Alvo"/>), a caixa inteira (<paramref name="Caixa"/>), o número
/// (<paramref name="Lado"/>/<paramref name="Altura"/>) ou o texto livre (<paramref name="Posicao"/>). O que vem null fica como está.</summary>
public sealed record MoverMarcacao(string Figura, string Marcacao, double[]? Alvo = null, double[]? Caixa = null, string? Lado = null,
    double? Altura = null, double[]? Posicao = null) : ComandoFigura(Figura);

/// <summary>Redimensiona a caixa vermelha de uma marcação de região (pelas alças).</summary>
public sealed record RedimensionarMarcacao(string Figura, string Marcacao, double[] Caixa) : ComandoFigura(Figura);

/// <summary>Tira uma marcação da figura (os números das demais se refazem).</summary>
public sealed record ExcluirMarcacao(string Figura, string Marcacao) : ComandoFigura(Figura);

/// <summary>Edita a legenda (elemento em negrito e o texto da solução) ou, no texto livre, o texto escrito na imagem. Null = não muda.</summary>
public sealed record DefinirLegenda(string Figura, string Marcacao, string? Elemento, string? Texto) : ComandoFigura(Figura);

/// <summary>Liga a marcação aos itens (a legenda diz "(item 12)" e acompanha a renumeração dos itens).</summary>
public sealed record VincularItens(string Figura, string Marcacao, IReadOnlyList<string> Itens) : ComandoFigura(Figura);

/// <summary>Edita um texto da figura: <c>prancha</c>, <c>titulo</c>, <c>texto</c> ou <c>fonte</c>.</summary>
public sealed record DefinirTextoFigura(string Figura, string Campo, string Texto) : ComandoFigura(Figura);

/// <summary>Inclui uma figura nova (um recorte de prancha já guardado em <c>assets/</c>) na seção de imagens; <paramref name="Depois"/> =
/// id da figura que fica antes dela (null = no fim).</summary>
public sealed record IncluirFigura(string Figura, string Imagem, string Prancha, string Titulo, string Fonte, JsonObject? Recorte = null,
    string? Depois = null) : ComandoFigura(Figura);

/// <summary>Tira a figura do relatório (ela continua no projeto: desfazer ou <see cref="RestaurarFigura"/> a devolvem).</summary>
public sealed record ExcluirFigura(string Figura) : ComandoFigura(Figura);

/// <summary>Devolve ao relatório uma figura excluída.</summary>
public sealed record RestaurarFigura(string Figura) : ComandoFigura(Figura);

/// <summary>O efeito dos comandos de figura: <c>Antes</c>/<c>Depois</c> são a camada de figuras inteira (null = sem camada, o relatório
/// como veio do kit), <c>Item</c> = a figura, <c>Campo</c> = a marcação (ou o campo de texto). Desfazer até o primeiro comando devolve o
/// relatório exatamente ao do kit.</summary>
internal static class MotorFiguras
{
    static readonly HashSet<string> Tipos = new(StringComparer.Ordinal)
    {
        nameof(IncluirMarcacao), nameof(MoverMarcacao), nameof(RedimensionarMarcacao), nameof(ExcluirMarcacao), nameof(DefinirLegenda),
        nameof(VincularItens), nameof(DefinirTextoFigura), nameof(IncluirFigura), nameof(ExcluirFigura), nameof(RestaurarFigura),
    };

    public static bool EhTipo(string tipo) => Tipos.Contains(tipo);

    public static void Definir(Projeto p, JsonNode? valor) => p.Figuras = valor is null ? null : CamadaFiguras.Ler(valor);

    public static Efeito? Calcular(Projeto p, ComandoFigura c)
    {
        var camada = (p.Figuras ?? CamadaFiguras.Inicial(p)).Select(f => f.Clonar()).ToList();
        string? campo = null;
        if (c is IncluirFigura inc)
        {
            if (camada.Any(f => f.Id == inc.Figura)) throw new ComandoRecusadoException("Já existe uma figura com esse id.");
            if (!inc.Imagem.StartsWith("assets/", StringComparison.Ordinal)) throw new ComandoRecusadoException("A imagem da figura tem de estar no relatório.");
            var nova = new FiguraRevisao
            {
                Id = inc.Figura,
                Imagem = inc.Imagem,
                Recorte = (JsonObject?)inc.Recorte?.DeepClone(),
                Base = new JsonObject
                {
                    ["prancha"] = Texto(inc.Prancha, "PRANCHA"),
                    ["titulo"] = Texto(inc.Titulo, "Recorte da prancha"),
                    ["imagem"] = inc.Imagem,
                    ["texto"] = "",
                    ["notas"] = new JsonArray(),
                    ["fonte"] = Texto(inc.Fonte, Texto(inc.Prancha, "Prancha") + "."),
                    ["id"] = inc.Figura,
                },
            };
            var pos = inc.Depois is null ? camada.Count : camada.FindIndex(f => f.Id == inc.Depois) + 1;
            camada.Insert(pos <= 0 ? camada.Count : pos, nova);
        }
        else
        {
            var fig = camada.FirstOrDefault(f => f.Id == c.Figura) ?? throw new ComandoRecusadoException("A figura não existe no relatório.");
            switch (c)
            {
                case ExcluirFigura:
                    fig.Incluida = false;
                    break;
                case RestaurarFigura:
                    fig.Incluida = true;
                    break;
                case DefinirTextoFigura d:
                    if (!CamadaFiguras.CamposTexto.Contains(d.Campo)) throw new ComandoRecusadoException("Esse texto da figura não pode ser editado.");
                    var t = TextoEditado.Normalizar(d.Texto ?? "");
                    if (t.Length == 0 && d.Campo != "texto") throw new ComandoRecusadoException("O texto não pode ficar vazio.");
                    if (fig.TextoBase(d.Campo) != t) fig.Base[d.Campo] = t;
                    campo = d.Campo;
                    break;
                case IncluirMarcacao d:
                    fig.Marcacoes.Add(NovaMarcacao(fig, d));
                    campo = d.Marcacao;
                    break;
                default:
                    campo = AplicarNaMarcacao(p, fig, c);
                    break;
            }
        }

        var antes = p.Figuras is null ? null : CamadaFiguras.Escrever(p.Figuras);
        var depois = CamadaFiguras.Escrever(camada);
        // Sem mudança: nada a registrar (comparado com a camada atual, ou com a inicial se ainda não há camada).
        var referencia = antes ?? CamadaFiguras.Escrever(CamadaFiguras.Inicial(p));
        if (JsonNode.DeepEquals(referencia, depois)) return null;
        return new Efeito(c.GetType().Name, c.Figura, campo, antes, depois);
    }

    static string Texto(string? t, string padrao)
    {
        var s = TextoEditado.Normalizar(t ?? "");
        return s.Length == 0 ? padrao : s;
    }

    static Marcacao NovaMarcacao(FiguraRevisao fig, IncluirMarcacao d)
    {
        if (string.IsNullOrWhiteSpace(d.Marcacao) || fig.Buscar(d.Marcacao) is not null) throw new ComandoRecusadoException("Id de marcação inválido.");
        if (!TiposMarcacao.Valido(d.Tipo)) throw new ComandoRecusadoException("Tipo de marcação desconhecido: " + d.Tipo + ".");
        var m = new Marcacao { Id = d.Marcacao, Tipo = d.Tipo };
        if (m.Numerada && CamadaFiguras.Numeradas(fig).Count >= CamadaFiguras.MaximoNumeradas)
            throw new ComandoRecusadoException("No máximo " + CamadaFiguras.MaximoNumeradas + " números por figura. Divida em dois recortes ou tire uma marcação.");
        switch (d.Tipo)
        {
            case TiposMarcacao.Regiao:
                m.Caixa = Caixa(d.Caixa ?? throw new ComandoRecusadoException("Marque a região arrastando o mouse sobre a imagem."));
                m.Altura = d.Altura is { } a ? Limitar(a) : (m.Caixa[1] + m.Caixa[3]) / 2;
                m.Lado = Lado(d.Lado) ?? ((m.Caixa[0] + m.Caixa[2]) / 2 > .5 ? "direita" : "esquerda");
                m.Elemento = TextoEditado.Normalizar(d.Elemento ?? "") is { Length: > 0 } e1 ? e1 : "Região marcada";
                break;
            case TiposMarcacao.Seta:
                m.Alvo = Ponto(d.Alvo ?? throw new ComandoRecusadoException("Clique no ponto que a seta deve indicar."));
                m.Altura = d.Altura is { } h ? Limitar(h) : m.Alvo[1];
                m.Lado = Lado(d.Lado) ?? (m.Alvo[0] > .5 ? "direita" : "esquerda");
                m.Elemento = TextoEditado.Normalizar(d.Elemento ?? "") is { Length: > 0 } e2 ? e2 : "Ponto indicado";
                break;
            default:
                m.Posicao = Ponto(d.Posicao ?? throw new ComandoRecusadoException("Clique onde o texto deve ficar."));
                m.Texto = TextoEditado.Normalizar(d.Texto ?? "");
                if (m.Texto.Length == 0) throw new ComandoRecusadoException("Escreva o texto.");
                return m;
        }
        m.Texto = TextoEditado.Normalizar(d.Texto ?? "");
        return m;
    }

    /// <summary>Move, redimensiona, exclui, edita a legenda ou liga aos itens; devolve o id da marcação (o "campo" do registro).</summary>
    static string AplicarNaMarcacao(Projeto p, FiguraRevisao fig, ComandoFigura c)
    {
        var id = c switch
        {
            MoverMarcacao d => d.Marcacao,
            RedimensionarMarcacao d => d.Marcacao,
            ExcluirMarcacao d => d.Marcacao,
            DefinirLegenda d => d.Marcacao,
            VincularItens d => d.Marcacao,
            _ => throw new ComandoRecusadoException("Comando desconhecido: " + c.GetType().Name),
        };
        var m = fig.Buscar(id) ?? throw new ComandoRecusadoException("A marcação não existe nesta figura.");
        switch (c)
        {
            case MoverMarcacao d:
                if (m.Gravada) throw new ComandoRecusadoException(TextoGravada);
                if (d.Alvo is not null) { if (m.Tipo != TiposMarcacao.Seta) throw new ComandoRecusadoException("Só a seta tem ponta para mover."); m.Alvo = Ponto(d.Alvo); }
                if (d.Caixa is not null)
                {
                    if (m.Tipo != TiposMarcacao.Regiao) throw new ComandoRecusadoException("Só a região tem caixa.");
                    m.Caixa = Caixa(d.Caixa);
                }
                if (d.Lado is not null) m.Lado = Lado(d.Lado) ?? m.Lado;
                if (d.Altura is { } a) m.Altura = Limitar(a);
                if (d.Posicao is not null) { if (m.Tipo != TiposMarcacao.Texto) throw new ComandoRecusadoException("Só o texto livre tem posição."); m.Posicao = Ponto(d.Posicao); }
                break;
            case RedimensionarMarcacao d:
                if (m.Gravada) throw new ComandoRecusadoException(TextoGravada);
                if (m.Tipo != TiposMarcacao.Regiao) throw new ComandoRecusadoException("Só a região tem caixa para redimensionar.");
                m.Caixa = Caixa(d.Caixa);
                break;
            case ExcluirMarcacao:
                if (m.Gravada)
                    throw new ComandoRecusadoException("Esta marcação está gravada na imagem recebida: o número não sai do desenho. Para refazer as marcações, "
                        + "use \"Inserir região da prancha\" e marque de novo sobre o recorte limpo (e exclua esta figura).");
                fig.Marcacoes.Remove(m);
                break;
            case DefinirLegenda d:
                if (m.Tipo == TiposMarcacao.Texto)
                {
                    var t = TextoEditado.Normalizar(d.Texto ?? m.Texto);
                    if (t.Length == 0) throw new ComandoRecusadoException("O texto não pode ficar vazio (para tirar, use Remover).");
                    m.Texto = t;
                    break;
                }
                if (d.Elemento is not null)
                {
                    var e = TextoEditado.Normalizar(d.Elemento);
                    if (e.Length == 0) throw new ComandoRecusadoException("O título da indicação não pode ficar vazio.");
                    m.Elemento = e;
                }
                if (d.Texto is not null) m.Texto = TextoEditado.Normalizar(d.Texto);
                break;
            case VincularItens d:
                if (!m.Numerada) throw new ComandoRecusadoException("O texto livre não tem legenda para ligar a itens.");
                foreach (var it in d.Itens)
                    if (p.Item(it) is null) throw new ComandoRecusadoException("O item não existe no relatório.");
                m.Itens = CamadaFiguras.TextoDosItens(p, d.Itens);
                break;
        }
        return id;
    }

    const string TextoGravada = "Esta marcação está gravada na imagem recebida e não pode ser movida; edite a legenda ou acrescente marcações novas por cima.";

    static double Limitar(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : throw new ComandoRecusadoException("Coordenada inválida.");

    static double[] Ponto(double[] v)
    {
        if (v.Length != 2) throw new ComandoRecusadoException("Coordenada inválida.");
        return new[] { Limitar(v[0]), Limitar(v[1]) };
    }

    /// <summary>Caixa normalizada [x0, y0, x1, y1] dentro da imagem, ordenada, com tamanho mínimo de 0,5% de cada lado.</summary>
    internal static double[] Caixa(double[] v)
    {
        if (v.Length != 4) throw new ComandoRecusadoException("Região inválida.");
        double x0 = Limitar(Math.Min(v[0], v[2])), x1 = Limitar(Math.Max(v[0], v[2]));
        double y0 = Limitar(Math.Min(v[1], v[3])), y1 = Limitar(Math.Max(v[1], v[3]));
        if (x1 - x0 < .005 || y1 - y0 < .005) throw new ComandoRecusadoException("A região é pequena demais: arraste um retângulo maior.");
        return new[] { x0, y0, x1, y1 };
    }

    static string? Lado(string? l) => l switch { "direita" => "direita", "esquerda" => "esquerda", null => null, _ => throw new ComandoRecusadoException("Lado inválido.") };

    // ---------- descrição no histórico ----------

    public static string? Descrever(string tipo, string? figura, string? campo, Projeto p)
    {
        var f = figura is null ? null : p.Figuras?.FirstOrDefault(x => x.Id == figura);
        var rotulo = RotuloFigura(p, figura);
        string Marca()
        {
            if (f is null || campo is null || f.Buscar(campo) is not { } m) return "uma marcação";
            if (!m.Numerada) return "o texto «" + m.Texto + "»";
            var n = CamadaFiguras.Numero(f, campo);
            return n is null ? "uma marcação" : "a marcação " + n.Value.ToString(CultureInfo.InvariantCulture);
        }
        return tipo switch
        {
            nameof(IncluirMarcacao) => "Marcação incluída na " + rotulo,
            nameof(MoverMarcacao) => "Marcação movida na " + rotulo,
            nameof(RedimensionarMarcacao) => "Região redimensionada na " + rotulo,
            nameof(ExcluirMarcacao) => "Marcação removida da " + rotulo,
            nameof(DefinirLegenda) => "Legenda editada: " + Marca() + " da " + rotulo,
            nameof(VincularItens) => "Itens ligados a " + Marca() + " da " + rotulo,
            nameof(DefinirTextoFigura) => "Texto da " + rotulo + " editado (" + campo + ")",
            nameof(IncluirFigura) => "Recorte de prancha incluído como " + rotulo,
            nameof(ExcluirFigura) => "Excluída a " + rotulo,
            nameof(RestaurarFigura) => "Restaurada a " + rotulo,
            _ => null,
        };
    }

    /// <summary>"Figura 3" pela posição entre as incluídas de agora (ou "figura" se não está no relatório).</summary>
    public static string RotuloFigura(Projeto p, string? figura)
    {
        var lista = p.Figuras ?? CamadaFiguras.Inicial(p);
        var n = 0;
        foreach (var f in lista.Where(x => x.Incluida))
        {
            n++;
            if (f.Id == figura) return "Figura " + n.ToString(CultureInfo.InvariantCulture);
        }
        return "figura";
    }
}
