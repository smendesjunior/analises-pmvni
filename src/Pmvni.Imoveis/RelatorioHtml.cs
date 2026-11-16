using System.Globalization;
using System.Net;
using System.Text;

namespace Pmvni.Imoveis;

static class Html
{
    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
}

/// <summary>O relatório cadastral e territorial de um imóvel em HTML A4 (visual do modelo PMVNI rev.10: cabeçalho com o brasão e as faixas,
/// faixa de identificação, quatro quadros, mapa com a coluna de situação/legenda/fontes, ficha em duas colunas, confrontantes, cadastro
/// completo, geometria e fontes). Só o que a base mostra: sem índices urbanísticos, sem documentos pessoais, com as ressalvas de método.</summary>
public static class RelatorioHtml
{
    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    static string M2(double v) => v.ToString("N2", Br) + " m²";
    static string M(double v) => v.ToString("N2", Br) + " m";
    static string Pc(double v) => v.ToString("N1", Br) + "%";

    const string Css = """
        @page { size: A4; margin: 12mm 12mm 14mm; }
        * { box-sizing: border-box; }
        html { background: #E9EDF2; }
        body { margin: 0; font-family: Verdana, sans-serif; color: #161E28; font-size: 10.5px; line-height: 1.45; }
        .folha { width: 210mm; margin: 16px auto; background: #fff; padding: 12mm 12mm 10mm; box-shadow: 0 2px 10px rgba(20,45,71,.15); }
        @media print { html { background: #fff; } .folha { margin: 0; width: auto; padding: 0; box-shadow: none; } }
        .cab { display: flex; align-items: center; gap: 14px; padding-bottom: 10px; }
        .cab img { height: 54px; }
        .cab .sep { width: 1px; height: 40px; background: #DDE4EB; }
        .cab .org { margin-left: auto; text-align: right; }
        .cab .org b { display: block; color: #1D3D5E; font-size: 13px; }
        .cab .org span { color: #637083; font-size: 9.5px; letter-spacing: .04em; }
        .faixas { display: flex; height: 4px; margin-bottom: 18px; }
        .faixas i:nth-child(1) { flex: 48; background: #2470B3; } .faixas i:nth-child(2) { flex: 28; background: #2F9E62; } .faixas i:nth-child(3) { flex: 24; background: #F2C43D; }
        h1 { font-size: 22px; color: #142D47; margin: 0 0 4px; letter-spacing: .01em; }
        .sub { color: #3F4A58; font-size: 9.5px; margin-bottom: 12px; }
        .band { display: flex; background: linear-gradient(90deg,#1E4F86,#2364A6); color: #fff; font-weight: bold; padding: 9px 12px; gap: 24px; font-size: 10.5px; }
        .band span:last-child { margin-left: auto; text-align: right; }
        .aviso { background: #FFF5D9; border: 1px solid #F2C43D; color: #5C4300; padding: 7px 10px; margin: 10px 0; }
        .aviso.vermelho { background: #FDECEA; border-color: #E9A39B; color: #7A1A12; }
        .quadros { display: grid; grid-template-columns: 1fr 1fr; gap: 2px; background: #fff; margin: 10px 0 14px; }
        .quadros div { background: #EEF4FB; padding: 6px 9px; }
        .quadros b { display: block; color: #1A5A94; font-size: 9px; letter-spacing: .05em; }
        h2 { font-size: 14px; color: #142D47; margin: 18px 0 4px; text-transform: uppercase; letter-spacing: .02em; break-after: avoid; }
        .nota { color: #3F4A58; font-size: 9.5px; margin: 0 0 8px; }
        .loc { display: grid; grid-template-columns: 61% 1fr; gap: 12px; break-inside: avoid; }
        .mapa { width: 100%; height: auto; display: block; border: 1px solid #DDE4EB; }
        .mapa text { font-family: Verdana, sans-serif; text-anchor: middle; dominant-baseline: middle; }
        .mapa .zona { font-size: 11px; font-weight: bold; fill: #3F4A58; paint-order: stroke; stroke: #fff; stroke-width: 3px; }
        .mapa .rua { font-size: 8.5px; fill: #3F4A58; paint-order: stroke; stroke: #fff; stroke-width: 2px; }
        .mapa .num { font-size: 9px; font-weight: bold; fill: #fff; }
        .mapa .norte { font-size: 10px; font-weight: bold; fill: #161E28; }
        .mapa .escala { font-size: 9px; fill: #161E28; text-anchor: start; }
        .lado { background: #F7F9FC; padding: 8px 10px; font-size: 9.5px; }
        .lado h3 { color: #1A5A94; font-size: 9.5px; margin: 0 0 3px; text-transform: uppercase; }
        .lado p { margin: 0 0 9px; }
        .leg { display: flex; align-items: center; gap: 6px; margin: 2px 0; }
        .leg i { width: 14px; height: 10px; display: inline-block; border: 1px solid #9AA3AE; }
        .ficha { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px; break-inside: avoid; }
        table { width: 100%; border-collapse: collapse; }
        th { background: #142D47; color: #fff; text-align: left; padding: 6px 8px; font-size: 9.5px; text-transform: uppercase; letter-spacing: .03em; }
        td { padding: 5px 8px; border-bottom: 1px solid #DDE4EB; vertical-align: top; }
        tr:nth-child(even) td { background: #F7F9FC; }
        td.r { color: #3F4A58; width: 42%; }
        td.n { text-align: right; white-space: nowrap; }
        .ok { color: #2F8F5B; font-weight: bold; } .falta { color: #A87500; font-weight: bold; } .div { color: #C8601F; font-weight: bold; }
        .lista { columns: 2; column-gap: 14px; }
        .lista div { break-inside: avoid; display: flex; justify-content: space-between; gap: 8px; border-bottom: 1px solid #EDF1F5; padding: 3px 0; }
        .lista span:first-child { color: #3F4A58; }
        .lista span:last-child { text-align: right; }
        ul { margin: 4px 0; padding-left: 16px; } li { margin: 2px 0; }
        .rodape { margin-top: 18px; border-top: 1px solid #DDE4EB; padding-top: 6px; display: flex; justify-content: space-between; color: #637083; font-size: 8.5px; }
        """;

    public static string Gerar(BaseQgis b, AnaliseImovel a, byte[]? brasao, DateTimeOffset geradoEm)
    {
        var r = a.Registro;
        var s = new StringBuilder();
        s.Append("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Relatório cadastral e territorial ")
         .Append(Html.E(r.InscricaoMunicipal)).Append("</title><style>").Append(Css).Append("</style></head><body><div class=\"folha\">");

        // Cabeçalho
        s.Append("<div class=\"cab\">");
        if (brasao is { Length: > 0 }) s.Append($"<img alt=\"Prefeitura Municipal de Venda Nova do Imigrante\" src=\"data:image/png;base64,{Convert.ToBase64String(brasao)}\"><div class=\"sep\"></div>");
        s.Append("<div class=\"org\"><b>SECRETARIA MUNICIPAL DE OBRAS</b><span>CONSULTA CADASTRAL E TERRITORIAL</span></div></div><div class=\"faixas\"><i></i><i></i><i></i></div>");
        s.Append("<h1>RELATÓRIO CADASTRAL E TERRITORIAL</h1>");
        s.Append($"<div class=\"sub\">Consulta individual na base municipal • gerado em {geradoEm.ToString("dd/MM/yyyy 'às' HH:mm", Br)} • base lida em {b.LidaEm.ToString("dd/MM/yyyy HH:mm", Br)}</div>");
        s.Append($"<div class=\"band\"><span>INSCRIÇÃO {Html.E(r.InscricaoMunicipal)}</span><span>{Html.E(r.Contribuinte.ToUpper(Br))}</span></div>");

        var vinculoTexto = a.Vinculo switch
        {
            VinculoMapa.Confirmado => "Lote identificado na base pela inscrição municipal (um único polígono).",
            VinculoMapa.MesmoTerreno => "Não há lote com esta inscrição; o desenho mostra o lote do mesmo terreno (mesmos distrito, setor, quadra e lote; outra unidade).",
            VinculoMapa.Ambiguo => $"Há {a.Lotes.Count} polígonos candidatos para esta inscrição: a identificação do lote não é segura.",
            _ => "Nenhum lote desenhado na base com esta inscrição.",
        };
        if (!a.Conclusivo)
            s.Append($"<div class=\"aviso{(a.Lote is null ? " vermelho" : "")}\"><b>{Html.E(TextosConsulta.Rotulo(a.Vinculo).ToUpper(Br))}.</b> {Html.E(vinculoTexto)} Zona, APP e confrontantes ficam só como informação, sem conclusão.</div>");

        // Quadros
        var zonaTexto = a.Zonas.Count == 0 ? (a.Lote is null ? "—" : "Sem zona na camada") : string.Join(" + ", a.Zonas.Select(z => a.Zonas.Count > 1 ? $"{z.Zona} ({Pc(z.Percentual)})" : z.Zona));
        s.Append("<div class=\"quadros\">");
        s.Append($"<div><b>LOCALIZAÇÃO</b>{Html.E(Juntar(r.Endereco, r.Bairro))}</div>");
        s.Append($"<div><b>CARACTERIZAÇÃO</b>{Html.E(Juntar(r.Campo("ultilizacao_do_imovel"), r.Campo("ocupacao"), a.Edificacoes.Count > 0 ? "projeção de telhados: " + M2(a.Edificacoes.Sum(x => x.AreaNoLote)) : null))}</div>");
        s.Append($"<div><b>IDENTIFICAÇÃO CADASTRAL</b>{Html.E(Juntar(r.InscricaoMunicipal, "reduzida " + r.Campo("inscricao_reduzida"), r.Situacao))}</div>");
        s.Append($"<div><b>SITUAÇÃO TERRITORIAL</b>{Html.E(Juntar(zonaTexto, a.PerimetroUrbano))}</div>");
        s.Append("</div>");

        // Localização
        s.Append("<h2>Localização na base municipal</h2><p class=\"nota\">Mapa temático, situação da identificação e ficha do lote.</p>");
        if (a.Lote is not null)
        {
            s.Append("<div class=\"loc\"><div>").Append(MapaSvg.Gerar(b, a)).Append("</div><div class=\"lado\">");
            s.Append($"<h3>Situação do lote na base</h3><p><b class=\"{(a.Conclusivo ? "ok" : "falta")}\">{Html.E(TextosConsulta.Rotulo(a.Vinculo))}</b><br>{Html.E(vinculoTexto)}</p>");
            s.Append("<h3>Legenda</h3>");
            Leg(s, "#D92D20", ".18", "#C0261B", "Lote consultado");
            Leg(s, "#55524D", ".85", "#55524D", "Projeção das edificações");
            Leg(s, "#FFFFFF", "1", "#C9C2B4", "Logradouros (eixo)");
            Leg(s, "#2F7FC1", "1", "#2F7FC1", "Curso d'água (eixo)");
            Leg(s, "#3B82C4", ".16", "#9AA3AE", "Faixa de APP da camada");
            foreach (var z in a.Zonas.Take(4)) Leg(s, MapaSvg.CorDaZona(z.Zona), "1", "#9AA3AE", "Zona " + z.Zona);
            if (a.Confrontantes.Count > 0) s.Append("<div class=\"leg\"><i style=\"background:#142D47;border-radius:50%;width:10px\"></i>Confrontantes (tabela abaixo)</div>");
            s.Append("<h3 style=\"margin-top:9px\">Observações</h3><p>Distâncias à hidrografia medidas do eixo do curso d'água, não da margem. Projeção de telhados não é área construída total.</p>");
            s.Append("<h3>Fonte</h3><p>Base municipal QGIS (SIRGAS 2000 / UTM 24S): lotes, edificações, zoneamento, logradouros e Hidro_app.</p>");
            s.Append("</div></div>");
        }
        else s.Append("<div class=\"aviso vermelho\">Sem lote desenhado para esta inscrição: o mapa e os cruzamentos geográficos não podem ser feitos. Confirme a inscrição ou a localização com os documentos do processo.</div>");

        // Ficha
        s.Append("<div class=\"ficha\"><div>");
        Tabela(s, "Identificação", [
            ("Inscrição municipal", r.InscricaoMunicipal), ("Inscrição reduzida", r.Campo("inscricao_reduzida")), ("Contribuinte", r.Contribuinte),
            ("Endereço", r.Endereco), ("Bairro", r.Bairro), ("CEP", r.Campo("cep"))]);
        Tabela(s, "Cadastro e entorno", [
            ("Setor / quadra", JuntarCom(r.Campo("setor_contribuinte"), r.Campo("quadra_contribuinte"), " / ")),
            ("Lote / unidade", JuntarCom(r.Campo("lote_contribuinte"), r.Campo("unidade_contribuinte"), " / ")),
            ("Situação cadastral", r.Situacao), ("Utilização", r.Campo("ultilizacao_do_imovel")), ("Loteamento (cadastro)", r.Campo("loteamento")),
            ("Loteamento (desenho)", a.Loteamento), ("Quadra (desenho)", a.Quadra), ("Nº do lote (desenho)", a.NumeroLoteDesenho)]);
        s.Append("</div><div>");
        var areaCalc = a.Lote?.Area;
        Tabela(s, "Áreas e edificação", [
            ("Área do lote (calculada)", areaCalc is { } ac ? M2(ac) : null),
            ("Área do lote (atributo AREA)", a.AreaArmazenada is { } aa ? M2(aa) : null),
            ("Área da unidade (cadastro)", AnaliseImovel.Numero(r.Campo("area_unidade_m2")) is { } au ? M2(au) : r.Campo("area_da_unidade")),
            ("Projeção de telhados no lote", a.Edificacoes.Count > 0 ? $"{M2(a.Edificacoes.Sum(x => x.AreaNoLote))} ({a.Edificacoes.Count} polígono(s))" : (a.Lote is null ? null : "Nenhuma na camada")),
            ("Perímetro do lote", a.Lote is { } lg ? M(lg.Length) : null),
            ("Outras unidades no terreno", a.UnidadesDoTerreno.Count > 0 ? a.UnidadesDoTerreno.Count.ToString(Br) : "Nenhuma")]);
        var app = a.Hidrografia.FirstOrDefault();
        Tabela(s, "Zona e condicionantes", [
            ("Zona (zoneamento 2025)", a.Zonas.Count == 0 ? (a.Lote is null ? null : "Sem zona na camada") : string.Join("; ", a.Zonas.Select(z => $"{z.Zona}: {M2(z.Area)} ({Pc(z.Percentual)})"))),
            ("Zona indicada na quadra", a.ZonaDaQuadra),
            ("Perímetro urbano", a.PerimetroUrbano),
            ("Área urbana consolidada", a.Auc is null ? null : (a.Auc.Length == 0 ? "Fora das AUC da camada" : a.Auc)),
            ("Hidrografia mais próxima", app is null ? (b[Camada.HidroApp] is null ? null : "Nenhuma a até 100 m") : $"{app.Nome ?? app.Tipo} a {M(app.Distancia)} do eixo"),
            ("Faixa de APP da camada", app is null ? null : app.FaixaAtipica ? $"valor {app.FaixaApp?.ToString("0.##", Br) ?? "vazio"} (atípico, não usado)" : $"{app.FaixaApp!.Value.ToString("0.##", Br)} m do eixo"),
            ("Lote na faixa de APP", app is null || app.FaixaAtipica ? null : app.AreaNaFaixa > 0 ? $"{M2(app.AreaNaFaixa)} dentro da faixa" : "Fora da faixa"),
            ("Conclusão", a.Conclusivo ? "Conforme a base (confirmar em campo e nos documentos)" : "FALTA COMPROVAR a identidade do lote")]);
        s.Append("</div></div>");
        s.Append("<p class=\"nota\" style=\"margin-top:8px\"><b>A conferir:</b> identidade do lote, data da base, documentos de área e matrícula. Índices urbanísticos dependem da norma vigente e não são calculados aqui.</p>");

        // Confrontantes e frente
        s.Append("<h2>Confrontantes, frente e divisas</h2>");
        if (a.Lote is null) s.Append("<p class=\"nota\">Sem lote desenhado: não há confrontantes a listar.</p>");
        else
        {
            s.Append("<p class=\"nota\">Lotes da camada a menos de 0,5 m do lote consultado, com o comprimento da divisa comum; a direção é vista do centro do lote. Logradouros a até 40 m (eixo).</p>");
            s.Append("<table><tr><th style=\"width:34px\">Nº</th><th>Lado</th><th>Confrontante (inscrição • endereço no cadastro)</th><th class=\"n\">Divisa</th></tr>");
            foreach (var f in a.Frentes)
                s.Append($"<tr><td>—</td><td>Frente</td><td>{Html.E(f.Logradouro)} (eixo a {M(f.Distancia)})</td><td class=\"n\">{(f.Testada >= 0.5 ? M(f.Testada) : "—")}</td></tr>");
            foreach (var c in a.Confrontantes)
                s.Append($"<tr><td>{c.Numero}</td><td>{Html.E(c.Direcao)}</td><td>{Html.E(Juntar(c.Inscricao ?? "sem inscrição no desenho", c.Endereco, c.Situacao is { Length: > 0 } st && st != "Ativo" ? st : null))}</td><td class=\"n\">{M(c.Divisa)}</td></tr>");
            if (a.Frentes.Count == 0 && a.Confrontantes.Count == 0) s.Append("<tr><td colspan=\"4\">Nenhum lote nem logradouro encostado ao lote na base.</td></tr>");
            s.Append("</table>");
        }

        // Geometria
        if (a.Lote is { } geo)
        {
            s.Append("<h2>Geometria do lote</h2>");
            var coords = geo.NumGeometries == 1 && geo.GetGeometryN(0) is NetTopologySuite.Geometries.Polygon p ? p.ExteriorRing.Coordinates[..^1] : null;
            s.Append($"<p class=\"nota\">Coordenadas SIRGAS 2000 / UTM 24S. Área calculada {M2(geo.Area)}; perímetro {M(geo.Length)}; {(coords?.Length.ToString(Br) ?? geo.NumPoints.ToString(Br))} vértices.</p>");
            if (coords is { Length: <= 40 })
            {
                s.Append("<table><tr><th>Vértice</th><th class=\"n\">E (m)</th><th class=\"n\">N (m)</th><th class=\"n\">Lado até o seguinte</th></tr>");
                for (var i = 0; i < coords.Length; i++)
                {
                    var prox = coords[(i + 1) % coords.Length];
                    s.Append($"<tr><td>V{i + 1}</td><td class=\"n\">{coords[i].X.ToString("N2", Br)}</td><td class=\"n\">{coords[i].Y.ToString("N2", Br)}</td><td class=\"n\">{M(coords[i].Distance(prox))}</td></tr>");
                }
                s.Append("</table>");
            }
        }

        // Cadastro completo
        s.Append("<h2>Cadastro imobiliário completo</h2><p class=\"nota\">Todos os campos preenchidos do registro vigente (documentos pessoais omitidos).</p><div class=\"lista\">");
        foreach (var (k, v) in r.Campos.Where(p => p.Value is not null && !Cadastro.Ocultas.Contains(p.Key)))
            s.Append($"<div><span>{Html.E(Cadastro.RotuloDe(k))}</span><span>{Html.E(SemDocumentos(v))}</span></div>");
        s.Append("</div>");
        if (r.Campo("status_cruzamento_pessoas") is "AMBIGUA")
            s.Append("<div class=\"aviso\">O vínculo com a tabela Pessoas é <b>ambíguo</b> (mais de uma pessoa candidata pelo par código e documento): nenhuma foi escolhida.</div>");
        if (a.UnidadesDoTerreno.Count > 0)
        {
            s.Append("<h2>Outras unidades do mesmo terreno</h2><table><tr><th>Inscrição</th><th>Endereço</th><th>Situação</th></tr>");
            foreach (var u in a.UnidadesDoTerreno.Take(30)) s.Append($"<tr><td>{Html.E(u.InscricaoMunicipal)}</td><td>{Html.E(u.Endereco)}</td><td>{Html.E(u.Situacao)}</td></tr>");
            s.Append("</table>");
        }

        // Fontes e ressalvas
        s.Append("<h2>Fontes e método</h2><ul>");
        s.Append($"<li>Cadastro: {Html.E(Path.GetFileName(b.Cadastro.Arquivo))} ({Html.E(b.Cadastro.Origem)}, só registros vigentes), linha de origem {r.LinhaFonte}.");
        foreach (var f in b.Cadastro.Fontes) s.Append($" Fonte {Html.E(f.Arquivo)}{(f.ImportadoEmUtc is { } im ? ", importada em " + Html.E(im) : "")}.");
        s.Append("</li>");
        foreach (var c in Enum.GetValues<Camada>())
            if (b[c] is { } cam) s.Append($"<li>{Html.E(FontesQgis.Rotulo(c))}: {Html.E(cam.Fonte.Relativo)} ({cam.Feicoes.Count.ToString("N0", Br)} feições, arquivo de {cam.Fonte.ModificadoUtc.ToLocalTime().ToString("dd/MM/yyyy", Br)}).</li>");
        if (a.NaoConsultadas.Count > 0) s.Append($"<li>Não consultadas (ausentes na pasta): {Html.E(string.Join(", ", a.NaoConsultadas))}.</li>");
        s.Append("<li>Ligação cadastro → desenho: inscrição municipal (texto, com os zeros) igual ao campo InscImob dos lotes; sem ela, o lote do mesmo terreno (11 primeiros dígitos).</li>");
        s.Append("<li>Cursos d'água: geometria municipal (Hidro_app, eixo); nome pela camada AGERH só quando um trecho está a até 60 m.</li>");
        s.Append("</ul><h2>Ressalvas</h2><ul><li>Relatório de consulta à base municipal: não substitui matrícula, BCI, levantamento topográfico nem análise de projeto.</li>");
        s.Append("<li>A ausência de uma feição na camada não prova a ausência da restrição no terreno.</li>");
        s.Append("<li>Zona e APP só têm valor de conclusão com o lote identificado com segurança; índices urbanísticos exigem a norma vigente.</li></ul>");
        s.Append($"<div class=\"rodape\"><span>Inscrição {Html.E(r.InscricaoMunicipal)} • {geradoEm.ToString("dd/MM/yyyy HH:mm", Br)}</span><span>Editor de Relatórios PMVNI • consulta de imóveis</span></div>");
        s.Append("</div></body></html>");
        return s.ToString();
    }

    static void Leg(StringBuilder s, string cor, string op, string borda, string texto) =>
        s.Append($"<div class=\"leg\"><i style=\"background:{cor};border-color:{borda};{(op != "1" ? $"background:color-mix(in srgb,{cor} {(int)(double.Parse(op, CultureInfo.InvariantCulture) * 100)}%,#fff)" : "")}\"></i>{Html.E(texto)}</div>");

    static void Tabela(StringBuilder s, string titulo, (string Rotulo, string? Valor)[] linhas)
    {
        s.Append($"<table style=\"margin-bottom:8px\"><tr><th colspan=\"2\">{Html.E(titulo)}</th></tr>");
        foreach (var (rot, val) in linhas)
        {
            var v = string.IsNullOrWhiteSpace(val) ? "<span class=\"falta\" style=\"font-weight:normal\">não informado</span>" : Html.E(val);
            if (val == "FALTA COMPROVAR a identidade do lote") v = "<span class=\"falta\">FALTA COMPROVAR</span> a identidade do lote";
            s.Append($"<tr><td class=\"r\">{Html.E(rot)}</td><td>{v}</td></tr>");
        }
        s.Append("</table>");
    }

    /// <summary>CPF e CNPJ escritos com pontuação (em campos como "Vínculos") saem do relatório: minimização de dados pessoais.</summary>
    public static string SemDocumentos(string? s) => s is null ? "" : System.Text.RegularExpressions.Regex.Replace(s,
        @"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b|\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", "[documento omitido]");

    static string Juntar(params string?[] partes) => Juntar(partes, " • ");
    static string JuntarCom(string? a, string? b, string sep) => Juntar([a, b], sep);
    static string Juntar(string?[] partes, string sep) => string.Join(sep, partes.Where(p => !string.IsNullOrWhiteSpace(p) && p.Trim() != "reduzida"));
}
