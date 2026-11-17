using System.Globalization;
using System.Text;

namespace Pmvni.Imoveis;

static class Html
{
    /// <summary>Escapa só o que o HTML e o XML exigem (os acentos ficam como estão: o documento é UTF-8).</summary>
    public static string E(string? s) => string.IsNullOrEmpty(s) ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
}

/// <summary>O relatório cadastral e territorial de um imóvel em HTML A4, com o aspecto do relatório técnico de análise do kit: usa o CSS
/// oficial do modelo (rev. 10, embutido sem alteração) e as mesmas peças — capa com a faixa de identificação e os quatro quadros, o bloco
/// LOCALIZAÇÃO NA BASE MUNICIPAL (o mapa do kit, aqui 4:3 na largura toda, com a legenda numa linha abaixo; situação, observações e
/// fonte embaixo) e a ficha em duas colunas com os
/// grupos Identificação, Cadastro e entorno, Áreas e edificação e Zona e condicionantes, com as etiquetas. Depois vêm os quadros próprios
/// da consulta (confrontantes, geometria, cadastro completo, fontes e ressalvas), nas tabelas do mesmo modelo. Só o que a base mostra: sem
/// índices urbanísticos, sem documentos pessoais, com as ressalvas de método.</summary>
public static class RelatorioHtml
{
    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    static string M2(double v) => v.ToString("N2", Br) + " m²";
    static string M(double v) => v.ToString("N2", Br) + " m";
    static string Pc(double v) => v.ToString("N1", Br) + "%";

    static readonly Lazy<string> cssOficial = new(() =>
    {
        using var f = typeof(RelatorioHtml).Assembly.GetManifestResourceStream("relatorio.css")
            ?? throw new InvalidOperationException("O CSS oficial do relatório (relatorio.css) não está embutido no módulo Imóveis.");
        using var l = new StreamReader(f, Encoding.UTF8);
        return l.ReadToEnd();
    });

    /// <summary>O CSS oficial do relatório do kit (rev. 10), byte a byte o de <c>Pmvni.Composicao\ModeloOficial</c>.</summary>
    public static string CssOficial => cssOficial.Value;

    /// <summary>Só a moldura que o modelo deixa para o gerador de PDF do kit: a folha na tela, a margem da página (o cabeçalho aqui vai
    /// no corpo, não na margem) e o alinhamento dos números nas tabelas da consulta.</summary>
    const string CssDaFolha = """
        @page { size: A4; margin: 12mm 14mm 14mm; }
        html { background: #E9EDF2; }
        .folha { width: 210mm; margin: 16px auto; background: #fff; padding: 12mm 14mm 12mm; box-shadow: 0 2px 10px rgba(20,45,71,.15); }
        @media print { html { background: #fff; } .folha { margin: 0; width: auto; padding: 0; box-shadow: none; } }
        .cabp { display: block; width: 100%; height: auto; margin-bottom: 5mm; }
        .tb td.n, .tb th.n { text-align: right; white-space: nowrap; }
        .rf b { overflow-wrap: anywhere; }
        .locblk { break-inside: auto; } .locblk > .sec { break-after: avoid; }
        .lmapa { break-inside: avoid; margin-top: 2px; }
        @media print { .lmapa .lmap { width: fit-content; max-width: 100%; margin: 0 auto; } .lmapa .lmap img { width: auto; max-width: 100%; max-height: 128mm; } }
        .lmapa .lmap { border-radius: 8px; }
        .lmapa .lleg { display: flex; flex-wrap: wrap; justify-content: center; gap: 2px 9px; margin: 4px 0 0; font-size: 6.1pt; line-height: 1.2; }
        .lmapa .lleg span { gap: 4px; white-space: nowrap; }
        .lmapa .lk { width: 12px; height: 8px; border-radius: 2px; }
        .lmapa .lk.rio { height: 3px; }
        .locblk > .lside { display: grid; grid-template-columns: 1fr 1fr; gap: 1px 12px; margin-top: 6px; break-inside: avoid; }
        .locblk > .lside .src { grid-column: 1 / -1; margin-top: 2px; padding-top: 0; }
        .rodape { margin-top: 12px; border-top: .6pt solid var(--line); padding-top: 5px; display: flex; justify-content: space-between; color: #637083; font-size: 6.2pt; }
        """;

    public static string Gerar(BaseQgis b, AnaliseImovel a, byte[]? brasao, DateTimeOffset geradoEm, bool cotas = true)
    {
        var r = a.Registro;
        var s = new StringBuilder();
        s.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Relatório cadastral e territorial ")
         .Append(Html.E(r.InscricaoMunicipal)).Append("</title><style>").Append(CssOficial).Append('\n').Append(CssDaFolha).Append("</style></head><body><div class=\"folha\">");
        s.Append(Cabecalho(brasao));

        var mapa = MapaLocalizacao.DaAnalise(b, a, new OpcoesMapa
        {
            Endereco = Juntar([r.Endereco, r.Bairro], ", "),
            Proprietario = r.Contribuinte.Trim(),
            Cotas = cotas,
        });

        var vinculoTexto = a.Vinculo switch
        {
            VinculoMapa.Confirmado => "Lote identificado na base pela inscrição municipal (um único polígono).",
            VinculoMapa.MesmoTerreno => "Não há lote com esta inscrição; o desenho mostra o lote do mesmo terreno (mesmos distrito, setor, quadra e lote; outra unidade).",
            VinculoMapa.Ambiguo => $"Há {a.Lotes.Count} polígonos candidatos para esta inscrição: a identificação do lote não é segura.",
            VinculoMapa.InscricaoAnterior => $"Não há lote com esta inscrição; o desenho mostra o lote da inscrição anterior deste imóvel ({r.Campo("inscricao_municipal_anterior")}). Confira a identificação.",
            _ when TextosConsulta.Baixada(r) => "Inscrição baixada no cadastro (cancelada, unificada ou desmembrada): não tem lote no desenho atual da base.",
            _ => "Nenhum lote desenhado na base com esta inscrição.",
        };
        var zonaTexto = a.Zonas.Count == 0 ? (a.Lote is null ? "Sem lote no mapa" : "Sem zona na camada")
            : string.Join(" + ", a.Zonas.Select(z => a.Zonas.Count > 1 ? $"{z.Zona} ({Pc(z.Percentual)})" : z.Zona));

        // ── capa ──
        s.Append("<div class=\"cover\"><div class=\"ttl-row\"><div class=\"ttl\"><span class=\"vbar\"></span><h1>RELATÓRIO CADASTRAL<br>E TERRITORIAL</h1></div>");
        s.Append($"<div class=\"dates\"><div><span>CONSULTA</span><b>{geradoEm.ToString("dd/MM/yyyy", Br)}</b></div><div><span>BASE LIDA EM</span><b>{b.LidaEm.ToString("dd/MM/yyyy", Br)}</b></div></div></div>");
        s.Append($"<div class=\"band\"><div class=\"proc\">INSCRIÇÃO {Html.E(r.InscricaoMunicipal)}</div><div class=\"name\">{Html.E(r.Contribuinte.ToUpper(Br))}</div></div>");
        s.Append("<div class=\"info\">");
        Quadro(s, r.Endereco.Length > 0 ? "g" : "y", "LOCALIZAÇÃO", r.Endereco.Length > 0 ? r.Endereco : "Endereço não informado", r.Bairro);
        Quadro(s, r.Campo("ultilizacao_do_imovel") is null ? "y" : "g", "CARACTERIZAÇÃO", r.Campo("ultilizacao_do_imovel") ?? "Utilização não informada",
            Juntar(r.Campo("ocupacao"), mapa is { Edificacoes: > 0 } ? "edificação na base: " + M2(mapa.AreaEdificada) : null));
        Quadro(s, r.Situacao.Length > 0 ? "g" : "y", "CADASTRO IMOBILIÁRIO", r.Situacao.Length > 0 ? "Situação: " + r.Situacao : "Situação não informada",
            Juntar(r.Campo("inscricao_reduzida") is { } red ? "inscrição reduzida " + red : null, r.Campo("matricula") is { } mat ? "matrícula " + mat : null));
        Quadro(s, a.Conclusivo ? "g" : "y", "SITUAÇÃO TERRITORIAL", a.Lote is null || a.Zonas.Count == 0 ? zonaTexto : "Zona " + zonaTexto, a.PerimetroUrbano);
        s.Append("</div>");
        s.Append($"<div class=\"confirm\"><span class=\"bang\">!</span><b>PONTOS A CONFIRMAR</b><em></em><span>{Html.E(a.Conclusivo
            ? "Identidade do lote nos documentos • Área e matrícula • Data da base"
            : TextosConsulta.Rotulo(a.Vinculo) + ": zona, APP e confrontantes só como informação • Área e matrícula • Data da base")}</span></div></div>");

        // ── localização na base municipal ──
        s.Append("<div class=\"locblk\"><div class=\"sec\"><div><h2>LOCALIZAÇÃO NA BASE MUNICIPAL</h2><p>Conferência do cadastro com a base cartográfica do município.</p></div>");
        s.Append($"<span class=\"pill {(a.Conclusivo ? "g" : a.Lote is null ? "r" : "a")}\"><i></i>{Html.E(TextosConsulta.Rotulo(a.Vinculo).ToUpper(Br))}</span></div>");
        if (mapa is not null)
        {
            // o mapa 4:3 na largura toda (2026-11-17), a legenda numa linha logo abaixo dele e, embaixo, a situação, as observações e a fonte
            s.Append($"<div class=\"lmapa\"><div class=\"lmap\"><img alt=\"Mapa de localização do lote na base municipal\" src=\"{mapa.SvgUri()}\"></div><div class=\"lleg\">");
            foreach (var i in mapa.Legenda) s.Append($"<span><i class=\"lk {i.Tipo}\"></i>{Html.E(i.Texto)}</span>");
            s.Append("</div></div><div class=\"lside\"><div>");
            s.Append($"<div class=\"lbl\">SITUAÇÃO DO LOTE NA BASE</div><p>{Html.E(vinculoTexto)}{(a.Conclusivo ? "" : " Zona, APP e confrontantes ficam só como informação.")}</p></div>");
            s.Append("<div><div class=\"lbl\">OBSERVAÇÕES DA CONSULTA</div>");
            s.Append("<p>Distâncias medidas até o eixo do curso d'água e da rodovia, não até a margem. A edificação é a projeção desenhada na base, não a área construída.</p></div>");
            s.Append($"<div class=\"src\">FONTE: Base municipal: {Html.E(string.Join(", ", mapa.Camadas))}; cadastro imobiliário vigente; {Html.E(MapaLocalizacao.Crs)}.</div></div>");
        }
        else s.Append("<div class=\"note\"><span class=\"bang\">!</span><b>SEM MAPA</b><em></em><span>Sem lote desenhado para esta inscrição: o mapa e os cruzamentos geográficos não podem ser feitos. Confirme a inscrição ou a localização com os documentos do processo.</span></div>");
        Ficha(s, DadosDaFicha(a, mapa));
        s.Append("</div>");

        // ── confrontantes e frente ──
        if (a.Lote is not null)
        {
            Secao(s, "CONFRONTANTES, FRENTE E DIVISAS", "Lotes a menos de 0,5 m do lote consultado, com a divisa comum; a direção é vista do centro do lote. Logradouros a até 40 m (eixo).");
            s.Append("<table class=\"tb\"><colgroup><col style=\"width:8%\"><col style=\"width:14%\"><col><col style=\"width:14%\"></colgroup><thead><tr><th>Nº</th><th>LADO</th><th>CONFRONTANTE (INSCRIÇÃO • ENDEREÇO NO CADASTRO)</th><th class=\"n\">DIVISA</th></tr></thead><tbody>");
            var z = false;
            string Tr() => (z = !z) ? "<tr>" : "<tr class=\"z\">";
            foreach (var f in a.Frentes)
                s.Append($"{Tr()}<td>—</td><td>Frente</td><td>{Html.E(f.Logradouro)} (eixo a {M(f.Distancia)})</td><td class=\"n\">{(f.Testada >= 0.5 ? M(f.Testada) : "—")}</td></tr>");
            foreach (var c in a.Confrontantes)
                s.Append($"{Tr()}<td>{c.Numero}</td><td>{Html.E(c.Direcao)}</td><td>{Html.E(Juntar(c.Inscricao ?? "sem inscrição no desenho", c.Endereco, c.Situacao is { Length: > 0 } st && st != "Ativo" ? st : null))}</td><td class=\"n\">{M(c.Divisa)}</td></tr>");
            if (a.Frentes.Count == 0 && a.Confrontantes.Count == 0) s.Append("<tr><td colspan=\"4\">Nenhum lote nem logradouro encostado ao lote na base.</td></tr>");
            s.Append("</tbody></table>");
        }

        // ── geometria ──
        if (a.Lote is { } geo)
        {
            var coords = geo.NumGeometries == 1 && geo.GetGeometryN(0) is NetTopologySuite.Geometries.Polygon p ? p.ExteriorRing.Coordinates[..^1] : null;
            Secao(s, "GEOMETRIA DO LOTE", $"Coordenadas SIRGAS 2000 / UTM 24S. Área calculada {M2(geo.Area)}; perímetro {M(geo.Length)}; {(coords?.Length.ToString(Br) ?? geo.NumPoints.ToString(Br))} vértices.");
            if (coords is { Length: <= 40 })
            {
                s.Append("<table class=\"tb\"><thead><tr><th>VÉRTICE</th><th class=\"n\">E (m)</th><th class=\"n\">N (m)</th><th class=\"n\">LADO ATÉ O SEGUINTE</th></tr></thead><tbody>");
                for (var i = 0; i < coords.Length; i++)
                {
                    var prox = coords[(i + 1) % coords.Length];
                    s.Append($"<tr{(i % 2 == 1 ? " class=\"z\"" : "")}><td>V{i + 1}</td><td class=\"n\">{coords[i].X.ToString("N2", Br)}</td><td class=\"n\">{coords[i].Y.ToString("N2", Br)}</td><td class=\"n\">{M(coords[i].Distance(prox))}</td></tr>");
                }
                s.Append("</tbody></table>");
            }
        }

        // ── cadastro completo ──
        Secao(s, "CADASTRO IMOBILIÁRIO", "Todos os campos preenchidos do registro vigente (documentos pessoais omitidos).");
        var campos = r.Campos.Where(p => p.Value is not null && !Cadastro.Ocultas.Contains(p.Key)).ToList();
        var metade = (campos.Count + 1) / 2;
        s.Append("<div class=\"two\">");
        foreach (var parte in new[] { campos.Take(metade), campos.Skip(metade) })
        {
            s.Append("<div class=\"refs\">");
            foreach (var (k, v) in parte) s.Append($"<div class=\"rf\"><b>{Html.E(Cadastro.RotuloDe(k))}</b><p>{Html.E(SemDocumentos(v))}</p></div>");
            s.Append("</div>");
        }
        s.Append("</div>");
        if (r.Campo("status_cruzamento_pessoas") is "AMBIGUA")
            s.Append("<div class=\"note\"><span class=\"bang\">!</span><b>VÍNCULO AMBÍGUO</b><em></em><span>O vínculo com a tabela Pessoas é ambíguo (mais de uma pessoa candidata pelo par código e documento): nenhuma foi escolhida.</span></div>");
        if (a.UnidadesDoTerreno.Count > 0)
        {
            Secao(s, "OUTRAS UNIDADES DO MESMO TERRENO", "Registros vigentes com os mesmos distrito, setor, quadra e lote.");
            s.Append("<table class=\"tb\"><colgroup><col style=\"width:22%\"><col><col style=\"width:18%\"></colgroup><thead><tr><th>INSCRIÇÃO</th><th>ENDEREÇO</th><th>SITUAÇÃO</th></tr></thead><tbody>");
            var n = 0;
            foreach (var u in a.UnidadesDoTerreno.Take(30)) s.Append($"<tr{(n++ % 2 == 1 ? " class=\"z\"" : "")}><td>{Html.E(u.InscricaoMunicipal)}</td><td>{Html.E(u.Endereco)}</td><td>{Html.E(u.Situacao)}</td></tr>");
            s.Append("</tbody></table>");
        }

        // ── fontes e ressalvas ──
        s.Append("<div class=\"endblk\">");
        Secao(s, "FONTES E REFERÊNCIAS", "Arquivos da base municipal consultados e o método da ligação.");
        s.Append("<div class=\"refs\">");
        var cadastro = new StringBuilder($"{Path.GetFileName(b.Cadastro.Arquivo)} ({b.Cadastro.Origem}, só registros vigentes), linha de origem {r.LinhaFonte}.");
        foreach (var f in b.Cadastro.Fontes) cadastro.Append($" Fonte {f.Arquivo}{(f.ImportadoEmUtc is { } im ? ", importada em " + im : "")}.");
        Fonte(s, "Cadastro", cadastro.ToString());
        foreach (var c in Enum.GetValues<Camada>())
            if (b[c] is { } cam) Fonte(s, FontesQgis.Rotulo(c), $"{cam.Fonte.Relativo} ({cam.Feicoes.Count.ToString("N0", Br)} feições, arquivo de {cam.Fonte.ModificadoUtc.ToLocalTime().ToString("dd/MM/yyyy", Br)}).");
        if (a.NaoConsultadas.Count > 0) Fonte(s, "Não consultadas", $"Ausentes na pasta: {string.Join(", ", a.NaoConsultadas)}.");
        Fonte(s, "Ligação com o desenho", "Inscrição municipal (texto, com os zeros) igual ao campo InscImob dos lotes; sem ela, o lote do mesmo terreno (11 primeiros dígitos).");
        Fonte(s, "Cursos d'água", "Geometria municipal (Hidro_app, eixo); nome pela camada AGERH/GEOBASES só quando um trecho está a até 60 m.");
        Fonte(s, "Mapa", "Mesmo desenho do mapa de localização do relatório técnico de análise (kit PMVNI): zonas, lotes, edificações, ruas, APP e faixas da BR-262 (domínio de 40 m a confirmar no DNIT; faixa não edificável de 15 m, Lei 6.766/1979, art. 4º, III).");
        s.Append("</div>");
        Secao(s, "RESSALVAS", "Alcance desta consulta.");
        s.Append("<div class=\"concl\">Relatório de consulta à base municipal: não substitui matrícula, BCI, levantamento topográfico nem análise de projeto. " +
            "A ausência de uma feição na camada não prova a ausência da restrição no terreno. Zona e APP só têm valor de conclusão com o lote identificado com segurança; " +
            "índices urbanísticos dependem da norma vigente e não são calculados aqui.</div></div>");
        s.Append($"<div class=\"rodape\"><span>INSCRIÇÃO {Html.E(r.InscricaoMunicipal)}  •  {geradoEm.ToString("dd/MM/yyyy HH:mm", Br)}</span><span>EDITOR DE RELATÓRIOS PMVNI  •  CONSULTA DE IMÓVEIS</span></div>");
        s.Append("</div></body></html>");
        return s.ToString();
    }

    /// <summary>O cabeçalho do relatório do kit (brasão, órgão e as três faixas), aqui no corpo da primeira página.</summary>
    static string Cabecalho(byte[]? brasao)
    {
        const int wd = 688, hd = 64;
        string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var logo = brasao is { Length: > 0 }
            ? $"<image href=\"data:image/png;base64,{Convert.ToBase64String(brasao)}\" x=\"0\" y=\"2\" width=\"160\" height=\"52.2\"/><line x1=\"172\" y1=\"8\" x2=\"172\" y2=\"48\" stroke=\"#DDE4EB\" stroke-width=\"1\"/>"
            : "";
        return $"<svg class=\"cabp\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {wd} {hd}\" role=\"img\" aria-label=\"Prefeitura Municipal de Venda Nova do Imigrante — Secretaria Municipal de Obras\">{logo}" +
            $"<text x=\"{wd}\" y=\"26\" text-anchor=\"end\" font-family=\"Verdana\" font-weight=\"bold\" font-size=\"14\" style=\"fill:#1D3D5E\">SECRETARIA MUNICIPAL DE OBRAS</text>" +
            $"<text x=\"{wd}\" y=\"41\" text-anchor=\"end\" font-family=\"Verdana\" font-size=\"9.8\" style=\"fill:#637083\">CONSULTA CADASTRAL E TERRITORIAL</text>" +
            $"<rect x=\"0\" y=\"{hd - 4}\" width=\"{F(wd * .48)}\" height=\"3.5\" fill=\"#2470B3\"/><rect x=\"{F(wd * .48)}\" y=\"{hd - 4}\" width=\"{F(wd * .28)}\" height=\"3.5\" fill=\"#2F9E62\"/><rect x=\"{F(wd * .76)}\" y=\"{hd - 4}\" width=\"{F(wd * .24)}\" height=\"3.5\" fill=\"#F2C43D\"/></svg>";
    }

    static void Quadro(StringBuilder s, string cor, string titulo, string destaque, string? apoio) =>
        s.Append($"<div class=\"ic {cor}\"><h4>{Html.E(titulo)}</h4><b>{Html.E(destaque)}</b><span>{Html.E(apoio)}</span></div>");

    static void Secao(StringBuilder s, string titulo, string texto) =>
        s.Append($"<div class=\"sec brk\"><div><h2>{Html.E(titulo)}</h2><p>{Html.E(texto)}</p></div></div>");

    static void Fonte(StringBuilder s, string rotulo, string texto) =>
        s.Append($"<div class=\"rf\"><b>{Html.E(rotulo)}</b><p>{Html.E(texto)}</p></div>");

    /// <summary>A ficha da localização: os dados do mapa do kit, com o que a consulta sabe a mais (inscrição reduzida, áreas calculada e
    /// da unidade, zonas por parte do lote, situação do registro consultado) e a etiqueta do vínculo. Rótulos curtos: o modelo não os quebra.</summary>
    internal static List<DadoLocalizacao> DadosDaFicha(AnaliseImovel a, MapaGerado? mapa)
    {
        var r = a.Registro;
        var d = mapa?.Dados.ToList() ?? [];
        const string id = MapaLocalizacao.GrupoId, ar = MapaLocalizacao.GrupoAreas, zc = MapaLocalizacao.GrupoZona, ce = MapaLocalizacao.GrupoCadastro;
        int Onde(string grupo, string rotulo) => d.FindIndex(x => x.Grupo == grupo && x.Rotulo == rotulo);
        void Depois(string grupo, string rotulo, DadoLocalizacao novo)
        {
            var i = Onde(grupo, rotulo);
            if (i >= 0) d.Insert(i + 1, novo);
            else if (d.FindLastIndex(x => x.Grupo == grupo) is var u && u >= 0) d.Insert(u + 1, novo);
            else d.Add(novo);
        }

        if (mapa is null)
        {
            if (r.Endereco.Length > 0) d.Add(new(id, "Endereço", Juntar([r.Endereco, r.Bairro], ", ")));
            if (r.Contribuinte.Trim().Length > 0) d.Add(new(id, "Proprietário", r.Contribuinte.Trim()));
            d.Add(new(id, "Inscrição", r.InscricaoMunicipal, "SEM LOTE NO MAPA", "r"));
            d.Add(new(ce, "Parcelamento", r.Campo("loteamento") is { } lot ? $"Loteamento {lot} (cadastro)" : "Sem loteamento no cadastro", Nota: "Indício; a regularidade se comprova pela matrícula."));
        }
        else
        {
            // A inscrição é a do registro consultado; a etiqueta diz se o desenho é mesmo o dela.
            var i = Onde(id, "Inscrição");
            var inscricao = new DadoLocalizacao(id, "Inscrição", r.InscricaoMunicipal, a.Conclusivo ? "CONFERE" : "A CONFIRMAR", a.Conclusivo ? "g" : "a",
                a.Conclusivo ? null : $"{TextosConsulta.Rotulo(a.Vinculo)}; no desenho: {(mapa.Inscricao.Length > 0 ? mapa.Inscricao : "sem inscrição")}.");
            if (i >= 0) d[i] = inscricao; else d.Add(inscricao);
            Depois(ar, "Área na base", new(ar, "Área calculada", M2(mapa.AreaGeometria)));
            if (a.Zonas.Count > 1 && Onde(zc, "Zona (2025)") is var iz && iz >= 0)
                d[iz] = d[iz] with { Valor = string.Join(" · ", a.Zonas.Select(z => $"{z.Zona} {Pc(z.Percentual)}")), Nota = "Lote em mais de uma zona da base." };
        }
        if (r.Campo("inscricao_reduzida") is { } red) Depois(id, "Inscrição", new(id, "Reduzida", red));
        var areaUnidade = AnaliseImovel.Numero(r.Campo("area_unidade_m2")) is { } au ? M2(au) : r.Campo("area_da_unidade");
        if (areaUnidade is not null) Depois(ar, "Área calculada", new(ar, "Área unidade", areaUnidade, Nota: "Área da unidade no cadastro, não a do terreno."));
        // A situação é a do registro consultado (o kit lê a do lote desenhado, que pode ser de outra unidade).
        var situacao = Juntar([r.Situacao, r.Campo("ocupacao"), r.Campo("matricula") is { } mt ? "matrícula " + mt : null], " · ");
        if (situacao.Length > 0)
        {
            var novo = new DadoLocalizacao(ce, "Situação", situacao);
            if (Onde(ce, "Situação") is var isit && isit >= 0) d[isit] = novo;
            else if (d.FindIndex(x => x.Grupo == ce) is var ic && ic >= 0) d.Insert(ic, novo);
            else d.Add(novo);
        }
        if (a.UnidadesDoTerreno.Count > 0) d.Add(new(ce, "Unidades", $"{a.UnidadesDoTerreno.Count + 1} no mesmo terreno"));
        return d;
    }

    /// <summary>A ficha em duas tabelas (Identificação + Cadastro e entorno | Áreas e edificação + Zona e condicionantes), como o molde.</summary>
    static void Ficha(StringBuilder s, List<DadoLocalizacao> dados)
    {
        string[][] colunas = [[MapaLocalizacao.GrupoId, MapaLocalizacao.GrupoCadastro], [MapaLocalizacao.GrupoAreas, MapaLocalizacao.GrupoZona]];
        s.Append("<div class=\"lficha\">");
        foreach (var col in colunas)
        {
            var grupos = col.Select(g => (Titulo: g, Dados: dados.Where(x => x.Grupo == g).ToList())).Where(g => g.Dados.Count > 0).ToList();
            if (grupos.Count == 0) continue;
            s.Append("<table class=\"lft\"><colgroup><col style=\"width:24%\"><col></colgroup><tbody>");
            foreach (var (titulo, linhas) in grupos)
            {
                s.Append($"<tr class=\"lfh\"><th colspan=\"2\">{Html.E(titulo)}</th></tr>");
                foreach (var x in linhas)
                    s.Append($"<tr><td class=\"lfl\">{Html.E(x.Rotulo)}</td><td class=\"lfv\">{(x.Chip is null ? "" : $"<i class=\"lchip {x.Alerta}\">{Html.E(x.Chip)}</i>")}<b>{Html.E(x.Valor)}</b>{(x.Nota is null ? "" : $"<small>{Html.E(x.Nota)}</small>")}</td></tr>");
            }
            s.Append("</tbody></table>");
        }
        s.Append("</div>");
    }

    /// <summary>CPF e CNPJ escritos com pontuação (em campos como "Vínculos") saem do relatório: minimização de dados pessoais.</summary>
    public static string SemDocumentos(string? s) => s is null ? "" : System.Text.RegularExpressions.Regex.Replace(s,
        @"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b|\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", "[documento omitido]");

    static string Juntar(params string?[] partes) => Juntar(partes, " • ");
    static string Juntar(string?[] partes, string sep) => string.Join(sep, partes.Where(p => !string.IsNullOrWhiteSpace(p)));
}
