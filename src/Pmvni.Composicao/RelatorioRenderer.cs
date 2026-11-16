using System.Text;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Kit;

namespace Pmvni.Composicao;

/// <summary>Opções do render (as do kit: <c>--instrucoes</c> e <c>--rascunho</c>; rascunho vale também com instruções).
/// <see cref="ImagensSoNaPasta"/>: as imagens só podem estar dentro da pasta do dados.json (sem <c>..</c>; a pasta de trabalho de um
/// pacote aberto). Caminho absoluto ou de rede é recusado sempre, e <c>..</c> só dentro da pasta do processo (<see cref="CaminhoImagem"/>);
/// só <c>.png .jpg .jpeg .webp .gif</c> com assinatura conferida; recusado conta como imagem não encontrada.</summary>
public sealed record OpcoesRender(bool Instrucoes, bool Rascunho, bool ImagensSoNaPasta = false);

/// <summary>Elemento do HTML que o paginador (Tarefa 6+) precisa reconhecer: seletor CSS, posição (0 = primeiro) entre os
/// elementos do mesmo seletor, id estável e, nas seções, o texto do h2. Os refs NÃO alteram o HTML.</summary>
public sealed record RefElemento(string Seletor, int Indice, string Id, string? Rotulo);

/// <summary>Resultado do render. Com erros de conferência, <see cref="Html"/> é "" e <see cref="Refs"/> é vazio.</summary>
public sealed record ResultadoRender(string Html, IReadOnlyList<string> Erros, IReadOnlyList<RefElemento> Refs)
{
    public bool Ok => Erros.Count == 0;
}

/// <summary>Dados (dados.json) → HTML do relatório, byte a byte igual ao <c>texto</c> que <c>main()</c> do gerar_relatorio.py do kit
/// entrega ao Edge. A conferência é a mesma do kit e, havendo erros, nada é renderizado.</summary>
public static class RelatorioRenderer
{
    private static readonly Regex Secao = new(@"<div class=""sec(?: [^""]*)?""><div><h2>(.*?)</h2>", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static ResultadoRender Renderizar(string caminhoDados, OpcoesRender op, ModeloCarregado m) =>
        Renderizar(File.ReadAllBytes(Path.GetFullPath(caminhoDados)), caminhoDados, op, m);

    /// <summary>Como <see cref="Renderizar(string, OpcoesRender, ModeloCarregado)"/>, com os bytes do dados.json já lidos
    /// (<paramref name="caminhoDados"/> só dá a pasta das imagens): quem precisa do sha256 dos bytes exatos que geraram o
    /// HTML lê o arquivo uma vez só.</summary>
    public static ResultadoRender Renderizar(byte[] bytesDados, string caminhoDados, OpcoesRender op, ModeloCarregado m)
    {
        ArgumentNullException.ThrowIfNull(bytesDados);
        var arquivo = Path.GetFullPath(caminhoDados);
        var baseDir = Path.GetDirectoryName(arquivo)!;
        // read_text(encoding='utf-8-sig'): UTF-8 estrito, sem o BOM.
        var json = new UTF8Encoding(false, true).GetString(bytesDados);
        var d = Dados.Dict(JsonModelo.Ler(json));

        var estrito = op.ImagensSoNaPasta;
        var erros = Conferencia.Conferir(d, rel => CaminhoImagem.Resolver(baseDir, rel, estrito) is { } c && CaminhoImagem.ImagemAceita(rel, c), op.Rascunho || op.Instrucoes);
        if (erros.Count > 0) return new ResultadoRender("", erros, Array.Empty<RefElemento>());

        var ctx = new Dictionary<string, object?>();
        foreach (var par in d) ctx[par.Key] = par.Value;
        ctx["instrucoes"] = op.Instrucoes;
        ctx["css"] = new Seguro(NovasLinhas(m.Css));
        var config = Dados.Obter(d, "config", new Dictionary<string, object?>());
        ctx["cabecalho_svg"] = new Seguro(Cabecalho.CabecalhoUri(Dados.Txt(Dados.Obter(config, "orgao", Cabecalho.OrgaoPadrao)), m.Brasao));
        var itens = Dados.Lista(Dados.Exigir(d, "itens"));
        ctx["resumo"] = Conferencia.Resumo(itens);

        var loc = Dados.Obter(d, "localizacao");
        if (PythonTexto.Verdadeiro(loc))
        {
            var l = Localizacao.Agrupar(Dados.Dict(loc));
            l["imagem_uri"] = DataUriPng(baseDir, Dados.Exigir(loc, "imagem"), estrito);
            ctx["localizacao"] = l;
        }

        var figuras = Dados.Lista(Dados.Obter(d, "figuras", new List<object?>()));
        ctx["figuras"] = figuras.Select(f =>
        {
            var nova = new Dictionary<string, object?>();
            foreach (var par in Dados.Dict(f)) nova[par.Key] = par.Value;
            nova["imagem_uri"] = DataUriPng(baseDir, Dados.Exigir(f, "imagem"), estrito);
            // Marcações editáveis (Etapa 4, editor de figuras): a figura sai como SVG (imagem base + camada de marcações). Sem a chave
            // "marcacoes" (o dados.json do kit), nada muda: o HTML é o do kit byte a byte.
            if (Dados.Obter(f, "marcacoes") is { } marcacoes && Figuras.SvgFigura.DoDados(marcacoes) is { Count: > 0 } marcas)
            {
                try { nova["imagem_uri"] = new Seguro(Figuras.SvgFigura.DataUri(LerImagem(baseDir, Dados.Exigir(f, "imagem"), estrito), marcas)); }
                catch (InvalidOperationException e) { throw new MoldeErro(e.Message); }
            }
            return (object?)nova;
        }).ToList();

        var diagrama = Dados.Obter(d, "diagrama");
        if (PythonTexto.Verdadeiro(diagrama)) ctx["diagrama"] = Diagrama.MontarDiagrama(Dados.Dict(diagrama));

        var html = Molde.Compilar(NovasLinhas(m.Html)).Renderizar(ctx, Filtros.Todos);
        return new ResultadoRender(html, erros, MontarRefs(html, itens, figuras));
    }

    /// <summary><c>Path.read_text()</c> do Python converte \r\n e \r em \n; o gerador do kit lê o molde e o CSS assim.</summary>
    internal static string NovasLinhas(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static Seguro DataUriPng(string baseDir, object? imagem, bool estrito) =>
        new("data:image/png;base64," + Convert.ToBase64String(LerImagem(baseDir, imagem, estrito)));

    private static byte[] LerImagem(string baseDir, object? imagem, bool estrito)
    {
        var rel = imagem as string ?? throw new MoldeErro("unsupported operand type(s) for /: 'PosixPath' and '" + PythonTexto.NomeTipo(imagem) + "'");
        // A conferência já recusou um caminho inaceitável; esta é a segunda barreira (nunca lê fora do permitido).
        var caminho = CaminhoImagem.Resolver(baseDir, rel, estrito) ?? throw new MoldeErro("caminho de imagem recusado: " + rel);
        if (!CaminhoImagem.ExtensaoPermitida(rel)) throw new MoldeErro("imagem em formato não aceito: " + rel);
        var bytes = File.ReadAllBytes(caminho);
        if (CaminhoImagem.ExtensaoPelaAssinatura(bytes) is null) throw new MoldeErro("imagem em formato não aceito: " + rel);
        return bytes;
    }

    private static string IdOu(object? item, int i, string prefixo)
    {
        var id = item is IReadOnlyDictionary<string, object?> dic ? Dados.Obter(dic, "id") : null;
        return PythonTexto.Verdadeiro(id) ? Dados.Txt(id) : prefixo + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static List<RefElemento> MontarRefs(string html, List<object?> itens, List<object?> figuras)
    {
        var refs = new List<RefElemento>();
        for (var i = 0; i < itens.Count; i++) refs.Add(new RefElemento(".card", i, IdOu(itens[i], i, "item-"), null));
        for (var i = 0; i < figuras.Count; i++) refs.Add(new RefElemento(".fig", i, IdOu(figuras[i], i, "figura-"), null));
        var n = 0;
        foreach (Match s in Secao.Matches(html))
        {
            refs.Add(new RefElemento(".sec", n, "secao-" + (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), s.Groups[1].Value));
            n++;
        }
        return refs;
    }
}
