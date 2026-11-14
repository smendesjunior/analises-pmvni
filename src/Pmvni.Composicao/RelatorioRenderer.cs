using System.Text;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Kit;

namespace Pmvni.Composicao;

/// <summary>Opções do render (as do kit: <c>--instrucoes</c> e <c>--rascunho</c>; rascunho vale também com instruções).</summary>
public sealed record OpcoesRender(bool Instrucoes, bool Rascunho);

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

    public static ResultadoRender Renderizar(string caminhoDados, OpcoesRender op, ModeloCarregado m)
    {
        var arquivo = Path.GetFullPath(caminhoDados);
        var baseDir = Path.GetDirectoryName(arquivo)!;
        // read_text(encoding='utf-8-sig'): UTF-8 estrito, sem o BOM.
        var json = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(arquivo));
        var d = Dados.Dict(JsonModelo.Ler(json));

        var erros = Conferencia.Conferir(d, rel => File.Exists(Path.GetFullPath(Path.Combine(baseDir, rel))), op.Rascunho || op.Instrucoes);
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
            l["imagem_uri"] = DataUriPng(baseDir, Dados.Exigir(loc, "imagem"));
            ctx["localizacao"] = l;
        }

        var figuras = Dados.Lista(Dados.Obter(d, "figuras", new List<object?>()));
        ctx["figuras"] = figuras.Select(f =>
        {
            var nova = new Dictionary<string, object?>();
            foreach (var par in Dados.Dict(f)) nova[par.Key] = par.Value;
            nova["imagem_uri"] = DataUriPng(baseDir, Dados.Exigir(f, "imagem"));
            return (object?)nova;
        }).ToList();

        var diagrama = Dados.Obter(d, "diagrama");
        if (PythonTexto.Verdadeiro(diagrama)) ctx["diagrama"] = Diagrama.MontarDiagrama(Dados.Dict(diagrama));

        var html = Molde.Compilar(NovasLinhas(m.Html)).Renderizar(ctx, Filtros.Todos);
        return new ResultadoRender(html, erros, MontarRefs(html, itens, figuras));
    }

    /// <summary><c>Path.read_text()</c> do Python converte \r\n e \r em \n; o gerador do kit lê o molde e o CSS assim.</summary>
    internal static string NovasLinhas(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static Seguro DataUriPng(string baseDir, object? imagem)
    {
        var rel = imagem as string ?? throw new MoldeErro("unsupported operand type(s) for /: 'PosixPath' and '" + PythonTexto.NomeTipo(imagem) + "'");
        return new Seguro("data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(baseDir, rel))));
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
