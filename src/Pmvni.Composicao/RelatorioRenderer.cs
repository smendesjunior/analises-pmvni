using System.Text;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Kit;

namespace Pmvni.Composicao;

/// <summary>Opções do render (as do kit: <c>--instrucoes</c> e <c>--rascunho</c>; rascunho vale também com instruções).
/// <see cref="ImagensSoNaPasta"/>: as imagens só podem estar dentro da pasta do dados.json (sem <c>..</c>; a pasta de trabalho de um
/// pacote aberto). Caminho absoluto ou de rede é recusado sempre, e <c>..</c> só dentro da pasta do processo (<see cref="CaminhoImagem"/>);
/// só <c>.png .jpg .jpeg .webp .gif</c> com assinatura conferida; recusado conta como imagem não encontrada.</summary>
public sealed record OpcoesRender(bool Instrucoes, bool Rascunho, bool ImagensSoNaPasta = false, bool UsarProfissionalLocal = true, bool EditarTextos = false);

/// <summary>Elemento do HTML que o paginador (Tarefa 6+) precisa reconhecer: seletor CSS, posição (0 = primeiro) entre os
/// elementos do mesmo seletor, id estável e, nas seções, o texto do h2. Os refs NÃO alteram o HTML.</summary>
public sealed record RefElemento(string Seletor, int Indice, string Id, string? Rotulo);

/// <summary>Resultado do render. Com erros de conferência, <see cref="Html"/> é "" e <see cref="Refs"/> é vazio.</summary>
public sealed record ResultadoRender(string Html, IReadOnlyList<string> Erros, IReadOnlyList<RefElemento> Refs)
{
    public bool Ok => Erros.Count == 0;
    public IReadOnlyDictionary<string, TextoDoModelo> Textos { get; init; } = new Dictionary<string, TextoDoModelo>();
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
        var d = new Dictionary<string, object?>(Dados.Dict(JsonModelo.Ler(json)));
        var editarTextos = op.EditarTextos || Equals(Dados.Obter(d, "_edicao_do_relatorio"), true);
        if (op.UsarProfissionalLocal && Profissional.Atual is { } pro)
            d["assinatura"] = new Dictionary<string, object?>
            {
                ["nome"] = pro.NomeLinha ?? "",
                ["cargo"] = pro.Cargo()
            };

        var estrito = op.ImagensSoNaPasta;
        var erros = Conferencia.Conferir(d, rel => CaminhoImagem.Resolver(baseDir, rel, estrito) is { } c && CaminhoImagem.ImagemAceita(rel, c), op.Rascunho || op.Instrucoes);
        if (erros.Count > 0) return new ResultadoRender("", erros, Array.Empty<RefElemento>());
        AplicarCamposOcultos(d);

        var ctx = new Dictionary<string, object?>();
        foreach (var par in d) ctx[par.Key] = par.Value;
        ctx["instrucoes"] = op.Instrucoes;
        ctx["css"] = new Seguro(NovasLinhas(m.Css));
        var config = Dados.Obter(d, "config", new Dictionary<string, object?>());
        ctx["cabecalho_svg"] = new Seguro(Cabecalho.CabecalhoUri(Dados.Txt(Dados.Obter(config, "orgao", Cabecalho.OrgaoPadrao)), m.Brasao));
        var itens = Dados.Lista(Dados.Exigir(d, "itens"));
        ctx["resumo"] = Conferencia.Resumo(itens);
        AplicarApresentacao(ctx, "indices", "linhas");
        AplicarApresentacao(ctx, "iluminacao", "ambientes");
        var loc = Dados.Obter(d, "localizacao");
        if (PythonTexto.Verdadeiro(loc))
        {
            var l = Localizacao.Agrupar(Dados.Dict(loc));
            if (!l.ContainsKey("origem_grafica") && PythonTexto.Verdadeiro(Dados.Obter(loc, "grafica_brilliant")))
                l["origem_grafica"] = "legado";
            var imagem = LerImagem(baseDir, Dados.Exigir(loc, "imagem"), estrito);
            l["imagem_uri"] = new Seguro("data:image/png;base64," + Convert.ToBase64String(imagem));
            // Os pacotes antigos deste recorte já traziam a legenda no PNG e também nos dados.
            // Só reconhecer o conteúdo conferido: outros mapas vetoriais requerem a legenda do JSON.
            if (!l.ContainsKey("legenda_na_imagem") && Equals(Dados.Obter(l, "origem_grafica"), "terra_vetorial"))
                l["legenda_na_imagem"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(imagem))
                    == "E200F404DD28CC2A271B722D91BB3A810DE69BE2348BDD969C8D91AE6A882FCB";
            ctx["localizacao"] = l;
        }

        var figuras = Dados.Lista(Dados.Obter(d, "figuras", new List<object?>()));
        var temMarcacoesEditaveis = false;
        ctx["figuras"] = figuras.Select(f =>
        {
            var nova = new Dictionary<string, object?>();
            foreach (var par in Dados.Dict(f)) nova[par.Key] = par.Value;
            // Marcações editáveis (Etapa 4, editor de figuras): a figura sai como SVG (imagem base + camada de marcações). Sem a chave
            // "marcacoes" (o dados.json do kit), nada muda: o HTML é o do kit byte a byte.
            if (Dados.Obter(f, "marcacoes") is { } marcacoes && Figuras.SvgFigura.DoDados(marcacoes) is { Count: > 0 } marcas)
            {
                temMarcacoesEditaveis = true;
                try { nova["imagem_uri"] = new Seguro(Figuras.SvgFigura.DataUri(LerImagem(baseDir, Dados.Exigir(f, "imagem"), estrito), marcas)); }
                catch (InvalidOperationException e) { throw new MoldeErro(e.Message); }
            }
            else nova["imagem_uri"] = DataUriPng(baseDir, Dados.Exigir(f, "imagem"), estrito);
            return (object?)nova;
        }).ToList();

        var diagrama = Dados.Obter(d, "diagrama");
        if (PythonTexto.Verdadeiro(diagrama))
        {
            var dadosDiagrama = Dados.Dict(diagrama);
            if (Dados.Obter(dadosDiagrama, "sem_calculo") is IReadOnlyDictionary<string, object?> falta
                && !PythonTexto.Verdadeiro(Dados.Obter(dadosDiagrama, "chamadas")))
            {
                var vistas = new Dictionary<string, object?>
                {
                    ["chamadas_planta"] = Array.Empty<object>(),
                    ["chamadas_volumetria"] = Array.Empty<object>(),
                    ["nota"] = Dados.Txt(Dados.Exigir(falta, "motivo")) + " " + Dados.Txt(Dados.Exigir(falta, "evidencia")),
                };
                foreach (var nome in new[] { "planta", "volumetria" })
                {
                    var imagem = dadosDiagrama.TryGetValue(nome, out var vista) && vista is IReadOnlyDictionary<string, object?> campos
                        ? Dados.Obter(campos, "imagem") as string : null;
                    if (imagem is not null && CaminhoImagem.Resolver(baseDir, imagem, estrito) is { } caminho && CaminhoImagem.ImagemAceita(imagem, caminho))
                        vistas[nome + "_svg"] = new Seguro($"""<img style="width:100%;height:auto" alt="{nome} do projeto" src="{DataUriPng(baseDir, imagem, estrito).Valor}">""");
                    else
                        vistas[nome + "_svg"] = new Seguro("<p>Vista pendente de comprovação.</p><p>" + Dados.E(Dados.Obter(falta, "motivo")) + "</p>");
                }
                ctx["diagrama"] = vistas;
            }
            else
            {
                var vistas = Diagrama.MontarDiagrama(dadosDiagrama, editarTextos);
                foreach (var nome in new[] { "planta", "volumetria" })
                {
                    var vista = Dados.Dict(Dados.Exigir(dadosDiagrama, nome));
                    if (Dados.Obter(vista, "imagem") is string imagem && imagem.Length > 0)
                    {
                        var uri = DataUriPng(baseDir, imagem, estrito).Valor;
                        vistas[nome + "_svg"] = new Seguro($"""<img data-vista-diagrama="{nome}" style="width:100%;height:auto" alt="{nome} do projeto" src="{uri}">""");
                    }
                }
                ctx["diagrama"] = vistas;
            }
        }

        var revisoes = Dados.Obter(d, "_textos_da_revisao") as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
        var textos = editarTextos || revisoes.Count > 0 ? new TextosDoModelo(revisoes) : null;
        var fonte = NovasLinhas(m.Html);
        if (loc is not null && Equals(Dados.Obter(loc, "origem_grafica"), "relatorio_pdf"))
        {
            fonte = fonte.Replace("{% if localizacao.origem_grafica %}{% else %}{{ localizacao.situacao|pill }}{% endif %}",
                "{{ localizacao.situacao|pill }}", StringComparison.Ordinal);
        }
        if (loc is not null && Equals(Dados.Obter(loc, "estilo_zoneamento"), "neutro"))
            fonte = fonte.Replace(".mapa-composto .lk.zona { background:#CEB0EF; border:1px dashed #674395; }",
                ".mapa-composto .lk.zona { background:#EFF2EE; border:1px dashed #89988E; }", StringComparison.Ordinal);
        if (op.Instrucoes)
            fonte = fonte.Replace("Ele é desenhado pelo gerador a partir dos rótulos do JSON: não é cópia do projeto e não tem escala.",
                "A vista superior segue a situação e a implantação da prancha. A volumetria mostra os níveis do terreno e dos acessos observados nos cortes, com a forma e as alturas das fachadas.")
                .Replace("A nota deve dizer que o esquema não representa a implantação real.",
                    "Identifique na nota as pranchas usadas e as limitações do perfil natural ou projetado.");
        if (editarTextos)
            fonte = fonte.Replace("{{ a.situacao|pill_vao }}", """
                {% if a._situacao_nominal %}{{ a._situacao_nominal|pill_vao }}{% if a._situacao_tecnica %} {{ a._situacao_tecnica|pill }}{% endif %}{% else %}{{ a.situacao|pill_vao }}{% endif %}{% if a._pendencia_nominal %}<div class="st">{{ a._pendencia_nominal }}</div>{% endif %}
                """, StringComparison.Ordinal);
        if (textos is not null) fonte = TextosDoModelo.Instrumentar(fonte);
        var html = Molde.Compilar(fonte).Renderizar(ctx, Filtros.Todos, textos);
        if (temMarcacoesEditaveis)
            html = html.Replace(" <span class=\"ids\">()</span>", "", StringComparison.Ordinal);
        if (textos is not null) html = GruposDoModelo.Aplicar(html, textos.Campos, revisoes);
        return new ResultadoRender(html, erros, MontarRefs(html, html.Contains("<div class=\"card ") ? itens : [], html.Contains("<div class=\"fig\">") ? figuras : []))
        {
            Textos = textos?.Campos ?? new Dictionary<string, TextoDoModelo>(),
        };
    }

    static void AplicarCamposOcultos(Dictionary<string, object?> dados)
    {
        if (Dados.Obter(dados, "_campos_ocultos") is not List<object?> caminhos) return;
        foreach (var caminho in caminhos.OfType<string>())
        {
            var partes = caminho.Split('/');
            object? no = dados;
            for (var i = 0; i < partes.Length; i++)
            {
                var ultimo = i == partes.Length - 1;
                if (no is Dictionary<string, object?> objeto && objeto.TryGetValue(partes[i], out var filho))
                {
                    if (ultimo && filho is string) objeto[partes[i]] = "";
                    no = filho;
                }
                else if (no is List<object?> lista && int.TryParse(partes[i], out var indice) && indice >= 0 && indice < lista.Count)
                {
                    no = lista[indice];
                    if (ultimo && no is string) lista[indice] = "";
                }
                else break;
                if (ultimo && no is Dictionary<string, object?> oculto) oculto["_oculto_na_revisao"] = true;
            }
        }
    }

    // A situação técnica original fica nos dados. O gerador do kit usa a apresentação na tabela impressa.
    private static void AplicarApresentacao(Dictionary<string, object?> contexto, string grupo, string chave)
    {
        var tabela = Dados.Obter(contexto, grupo);
        if (!PythonTexto.Verdadeiro(tabela)) return;
        var linhas = Dados.Lista(Dados.Obter(tabela, chave, new List<object?>()));
        for (var i = 0; i < linhas.Count; i++)
        {
            var apresentacao = Dados.Obter(linhas[i], "apresentacao");
            var linha = new Dictionary<string, object?>(Dados.Dict(linhas[i]));
            if (Dados.Obter(linha, "icone") is not string { Length: > 0 })
                linha["icone"] = grupo == "iluminacao" ? "bed" : "plot";
            linhas[i] = linha;
            if (!PythonTexto.Verdadeiro(apresentacao)) continue;
            var situacao = Dados.Obter(apresentacao, "situacao") as string;
            if (grupo == "iluminacao" && Equals(Dados.Obter(linha, "situacao"), "DIVERGENCIA") &&
                (situacao == "DECISAO_DO_ANALISTA" || Equals(Dados.Obter(apresentacao, "situacao_adicional"), "DECISAO_DO_ANALISTA")))
                linha["situacao_adicional"] = "DECISAO_DO_ANALISTA";
            else if (!string.IsNullOrEmpty(situacao))
                linha["situacao"] = situacao;
            if (Dados.Obter(apresentacao, "nota") is string { Length: > 0 } nota)
            {
                if (grupo == "indices")
                    linha["texto"] = Dados.Obter(linha, "texto") is string { Length: > 0 } texto && texto != nota ? texto + " " + nota : nota;
                else
                {
                    var textos = Dados.Lista(Dados.Obter(linha, "textos", new List<object?>())).Where(t => !Equals(t, nota)).ToList();
                    textos.Insert(0, nota);
                    linha["textos"] = textos;
                }
            }
        }
        contexto[grupo] = new Dictionary<string, object?>(Dados.Dict(tabela)) { [chave] = linhas };
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
            var rotulo = Regex.Replace(s.Groups[1].Value, "</?pmvni-texto[^>]*>", "", RegexOptions.CultureInvariant);
            refs.Add(new RefElemento(".sec", n, "secao-" + (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), System.Net.WebUtility.HtmlDecode(rotulo)));
            n++;
        }
        return refs;
    }
}
