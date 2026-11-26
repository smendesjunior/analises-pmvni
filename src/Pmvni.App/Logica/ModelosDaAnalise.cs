using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Uma coluna da grade "Modelos da análise": a rota do kit (<c>codex</c>, <c>claude</c>, <c>gemini</c>) e o perfil dela na tabela.</summary>
public sealed record ColunaModelos(string Rota, string Perfil, string Titulo)
{
    public Provedor Provedor => ModelosDaAnalise.ProvedorDaRota(Rota);
}

/// <summary>O modelo (id do provedor) e o nível (<c>baixo</c>, <c>medio</c>, <c>alto</c>) de uma etapa.</summary>
public sealed record CelulaModelos(string Modelo, string Nivel);

/// <summary>Uma linha da grade: a etapa do <c>etapas.json</c> do kit. <paramref name="Programa"/>: a etapa roda sem IA (<c>executor: programa</c>), sem modelo.
/// <paramref name="LigadaPorRota"/>: só nas etapas opcionais (hoje, a audit), o padrão do kit por rota.</summary>
public sealed record EtapaModelos(string Id, string Nome, bool Programa, bool Opcional, IReadOnlyDictionary<string, bool> LigadaPorRota);

/// <summary>A lista de modelos que o provedor oferece para uma rota. <paramref name="Conferida"/>: a lista foi lida do provedor (app, API ou a última leitura
/// guardada) — só então "não está na lista" quer dizer "deixou de existir"; com a lista conhecida do Editor (reserva), nada é recusado nem trocado.</summary>
public sealed record ListaDaRota(IReadOnlyList<ModeloIa> Modelos, bool Conferida, string Fonte);

/// <summary>A tabela padrão do kit (<c>references\modelos-por-rota.json</c> do plugin analise instalado).</summary>
public sealed class TabelaModelosKit
{
    public required JsonObject Bruto { get; init; }
    public required IReadOnlyList<string> Niveis { get; init; }
    public required IReadOnlyDictionary<string, string> Equivalencia { get; init; }
    public required IReadOnlyList<ColunaModelos> Colunas { get; init; }
    public required IReadOnlyDictionary<(string Rota, string Perfil, string Etapa), CelulaModelos> Celulas { get; init; }
    /// <summary>Os modelos que a rota Gemini conhece e os níveis (baixo/medio/alto) de cada um.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ModelosGemini { get; init; }
    public required IReadOnlyList<string> EtapasTecnicas { get; init; }
    public required bool SobePorObjecao { get; init; }
    public required double? DescePorCota { get; init; }
    /// <summary>A nota da exceção de cada rota (texto do kit), para a tela.</summary>
    public required IReadOnlyDictionary<string, string> NotasDasRotas { get; init; }
    /// <summary>A exceção de <c>sobe_por_objecao_tecnica</c> de cada rota no kit (a rota que não está aqui segue a regra geral).</summary>
    public IReadOnlyDictionary<string, bool> SobePorRota { get; init; } = new Dictionary<string, bool>();

    /// <summary>As rotas em que a regra geral "objeção técnica sobe um nível" vale (as exceções do kit ficam de fora).</summary>
    public IReadOnlyList<string> RotasQueSobem => Colunas.Select(c => c.Rota).Distinct().Where(r => !SobePorRota.TryGetValue(r, out var v) || v).ToList();

    public CelulaModelos? Celula(ColunaModelos c, string etapa) => Celulas.TryGetValue((c.Rota, c.Perfil, etapa), out var x) ? x : null;
}

/// <summary>O ajuste local deste computador (<c>%LOCALAPPDATA%\EditorRelatoriosPMVNI\modelos-por-rota.local.json</c>, na
/// <see cref="Pmvni.Composicao.Web.PastaDados.Raiz"/>; o Recriar PC guarda esse arquivo por um passo próprio do <c>recriar.json</c>): só o que difere do padrão do kit.</summary>
public sealed class AjusteModelos
{
    public Dictionary<(string Rota, string Perfil, string Etapa), CelulaModelos> Celulas { get; } = new();
    public bool? SobePorObjecao { get; set; }
    public bool ReconferenciaEconomica { get; set; }
    /// <summary>true = o ajuste troca o limite de cota (<see cref="DescePorCota"/>; null nele = não desce por cota).</summary>
    public bool TrocaDescePorCota { get; set; }
    public double? DescePorCota { get; set; }
    /// <summary>(etapa, rota) → ligada; só etapas opcionais.</summary>
    public Dictionary<(string Etapa, string Rota), bool> Ligadas { get; } = new();

    public bool Vazio => !ReconferenciaEconomica && Celulas.Count == 0 && SobePorObjecao is null && !TrocaDescePorCota && Ligadas.Count == 0;
}

/// <summary>Um aviso da aba ou da partida, por rota.</summary>
public sealed record AvisoModelos(string Rota, string Texto);

/// <summary>A tabela que vai para o controlador nesta execução e os avisos (modelo trocado pelo padrão do kit, que ficam no histórico).</summary>
public sealed record TabelaEfetiva(JsonObject Json, IReadOnlyList<AvisoModelos> Avisos)
{
    /// <summary>A tabela desta execução: o arquivo e o histórico levam só os avisos da rota que roda.</summary>
    public TabelaDeModelos ParaExecucao(string rota)
    {
        var avisos = Avisos.Where(a => a.Rota == rota).Select(a => a.Texto).ToList();
        var json = (JsonObject)Json.DeepClone();
        json["avisos"] = new JsonArray(avisos.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
        return new(json.ToJsonString(ModelosDaAnalise.Opcoes), avisos);
    }
}

/// <summary>Tarefa 19: Ajustes › Inteligência artificial › Modelos da análise. O padrão vem do kit instalado (igual em todos os computadores); o ajuste
/// local opcional fica em <see cref="ArquivoLocal"/> (o Editor não é MSIX: grava direto em %LOCALAPPDATA%\EditorRelatoriosPMVNI). Antes de cada análise, <see cref="Efetiva"/> monta a
/// tabela que vai ao controlador: uma célula ajustada ausente do catálogo é preservada, com aviso (na aba, no Novo
/// relatório e no histórico da execução). As opções vêm das listas que o Editor já atualiza por provedor (<see cref="CatalogoModelosIa"/>); na rota Gemini,
/// da lista que a rota Gemini do kit devolve (<see cref="LerListaGemini"/>: agy models ou a Gemini API; o Gemini Web não lista e fica com os modelos da tabela).
/// No computador do kit mestre, <see cref="Publicar"/> grava a tabela no kit e roda o
/// <c>INSTALAR_KIT_PMVNI.cmd manter</c> fora do Editor. Tudo aqui é testável sem WinUI.</summary>
public static class ModelosDaAnalise
{
    public const string NomeArquivoLocal = "modelos-por-rota.local.json";
    public const string CmdPublicar = "publicar-modelos-no-kit.cmd";
    public static IReadOnlyList<string> RotasAtivas { get; } = Provedores.Ativos.Select(RotaDoProvedor).ToArray();
    public static readonly string[] Rotas = ["codex", "claude", "gemini"];
    internal static bool PorTarefa(string rota, string etapa) => etapa == "analyze" && RotasAtivas.Contains(rota);

    public static string ArquivoLocal(string raizDados) => Path.Combine(raizDados, NomeArquivoLocal);

    internal static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true, IndentSize = 2, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static Provedor ProvedorDaRota(string rota) => rota switch { "claude" => Provedor.Claude, "gemini" => Provedor.Gemini, _ => Provedor.ChatGpt };

    public static string RotaDoProvedor(Provedor p) => p switch { Provedor.Claude => "claude", Provedor.Gemini => "gemini", _ => "codex" };

    public static string NomeDoNivel(string nivel) => nivel switch { "baixo" => "Baixo", "medio" => "Médio", "alto" => "Alto", _ => CatalogoIa.NomeNivel(nivel) };

    static readonly string[] NiveisDoKit = ["baixo", "medio", "alto"];
    static readonly Dictionary<string, string> EquivalenciaPadrao = new() { ["baixo"] = "low", ["medio"] = "medium", ["alto"] = "high" };

    static IReadOnlyList<string> NiveisDaEtapa(JsonObject tabela, IReadOnlyList<string> padrao, string rota, string etapa)
    {
        if (etapa == "technical_audit" && tabela["niveis_auditoria_por_rota"]?[rota] is JsonArray niveis)
            return niveis.Select(Texto).OfType<string>().Distinct().ToList();
        return padrao;
    }

    // ---------- leitura ----------

    /// <summary>A tabela do kit; null se o texto não é a tabela (kit anterior ou arquivo quebrado: a aba avisa e não ajusta nada).</summary>
    public static TabelaModelosKit? LerTabela(string? json, bool apenasAtivas = false)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json.TrimStart('﻿')) is not JsonObject raiz || raiz["rotas"] is not JsonObject rotas) return null;
            var niveis = (raiz["niveis"] as JsonArray)?.Select(Texto).OfType<string>().ToList() is { Count: > 0 } n ? n : NiveisDoKit.ToList();
            var eq = raiz["equivalencia"] is JsonObject e ? e.ToDictionary(k => k.Key, k => Texto(k.Value) ?? k.Key) : EquivalenciaPadrao;
            var colunas = new List<ColunaModelos>();
            var celulas = new Dictionary<(string, string, string), CelulaModelos>();
            foreach (var rota in apenasAtivas ? RotasAtivas : Rotas)
            {
                if (rota == "gemini" && rotas[rota] is null) continue;
                if (rotas[rota] is not JsonObject r || r["perfis"] is not JsonObject perfis) return null;
                var nomeRota = Texto(r["nome"]) ?? Provedores.Nome(ProvedorDaRota(rota));
                foreach (var (perfil, pNo) in perfis)
                {
                    if (pNo is not JsonObject p || p["etapas"] is not JsonObject etapas) return null;
                    colunas.Add(new ColunaModelos(rota, perfil, perfis.Count > 1 ? nomeRota + " · " + (Texto(p["nome"]) ?? perfil) : nomeRota));
                    foreach (var (etapa, cNo) in etapas)
                        if (cNo is JsonObject c && Texto(c["modelo"]) is { } modelo && Texto(c["nivel"]) is { } nivel && NiveisDaEtapa(raiz, niveis, rota, etapa).Contains(nivel))
                            celulas[(rota, perfil, etapa)] = new CelulaModelos(modelo, nivel);
                        else return null;
                }
            }
            var gemini = new Dictionary<string, IReadOnlyList<string>>();
            if (rotas["gemini"]?["modelos"] is JsonObject mg)
                foreach (var (m, info) in mg)
                    gemini[m] = (info?["niveis"] as JsonArray)?.Select(Texto).OfType<string>().ToList() ?? [];
            var regras = raiz["regras"] as JsonObject;
            var notas = new Dictionary<string, string>();
            var sobePorRota = new Dictionary<string, bool>();
            if (regras?["excecoes_por_rota"] is JsonObject exc)
                foreach (var (rota, x) in exc)
                {
                    if (apenasAtivas && !RotasAtivas.Contains(rota)) continue;
                    if (Texto(x?["nota"]) is { } nota) notas[rota] = nota;
                    if (x?["sobe_por_objecao_tecnica"] is JsonValue sv && sv.TryGetValue<bool>(out var sobeRota)) sobePorRota[rota] = sobeRota;
                }
            return new TabelaModelosKit
            {
                Bruto = raiz, Niveis = niveis, Equivalencia = eq, Colunas = colunas, Celulas = celulas, ModelosGemini = gemini,
                EtapasTecnicas = (raiz["etapas_tecnicas"] as JsonArray)?.Select(Texto).OfType<string>().ToList() ?? ["analyze", "technical_audit"],
                SobePorObjecao = regras?["sobe_por_objecao_tecnica"] is JsonValue s && s.TryGetValue<bool>(out var sobe) && sobe,
                DescePorCota = regras?["desce_com_cota_abaixo_de"] is JsonValue d && d.TryGetValue<double>(out var desce) ? desce : null,
                NotasDasRotas = notas, SobePorRota = sobePorRota,
            };
        }
        catch (Exception x) when (x is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>As etapas do <c>etapas.json</c> do kit, na ordem (as linhas da grade). Sem o arquivo: as etapas da própria tabela, todas de IA.</summary>
    public static IReadOnlyList<EtapaModelos> LerEtapas(string? etapasJson, TabelaModelosKit? tabela)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(etapasJson) && JsonNode.Parse(etapasJson.TrimStart('﻿')) is JsonObject raiz
                && raiz["ordem"] is JsonArray ordem && raiz["etapas"] is JsonObject etapas)
            {
                var lista = new List<EtapaModelos>();
                foreach (var id in ordem.Select(Texto).OfType<string>())
                {
                    var e = etapas[id] as JsonObject;
                    var opcional = e?["opcional"] is JsonValue o && o.TryGetValue<bool>(out var op) && op;
                    var ligadas = new Dictionary<string, bool>();
                    if (opcional && e?["ligada_por_rota"] is JsonObject lp)
                        foreach (var rota in Rotas) ligadas[rota] = lp[rota] is not JsonValue v || !v.TryGetValue<bool>(out var b) || b;
                    lista.Add(new EtapaModelos(id, SituacaoAnalisePro.NomeEtapa(id), Texto(e?["executor"]) == "programa", opcional, ligadas));
                    if (id == "technical_audit" && tabela?.Celulas.Keys.Any(k => k.Etapa == "autocorrecao") == true)
                        lista.Add(new EtapaModelos("autocorrecao", "Autocorreção", false, false, new Dictionary<string, bool>()));
                }
                if (lista.Count > 0) return lista;
            }
        }
        catch (Exception x) when (x is JsonException or InvalidOperationException) { }
        var ids = tabela?.Celulas.Keys.Select(k => k.Etapa).Distinct().ToList() ?? [];
        return ids.Select(id => new EtapaModelos(id, SituacaoAnalisePro.NomeEtapa(id), false, false, new Dictionary<string, bool>())).ToList();
    }

    /// <summary>O ajuste local (vazio se o arquivo não existe ou não se lê: vale o padrão do kit).</summary>
    public static AjusteModelos LerAjuste(string? json)
    {
        var a = new AjusteModelos();
        if (json is null) return a;
        static JsonObject Objeto(JsonNode? no, string campo) => no as JsonObject
            ?? throw new InvalidDataException($"O campo {campo} precisa ser um objeto.");
        static JsonObject Grupo(JsonObject o, string campo) => o.ContainsKey(campo) ? Objeto(o[campo], campo) : new();
        static bool Booleano(JsonNode? no, string campo) => no is JsonValue v && v.TryGetValue<bool>(out var b)
            ? b : throw new InvalidDataException($"Valor inválido em {campo}.");

        try
        {
            var raiz = Objeto(JsonNode.Parse(json.TrimStart('\uFEFF')), "raiz");
            if (raiz.ContainsKey("versao") && !(raiz["versao"] is JsonValue v && v.TryGetValue<int>(out var versao) && versao == 1))
                throw new InvalidDataException("Versão de ajuste de modelos incompatível.");
            foreach (var (rota, r) in Grupo(raiz, "rotas"))
                foreach (var (perfil, p) in Objeto(Objeto(r, rota)["perfis"], "perfis"))
                    foreach (var (etapa, c) in Objeto(Objeto(p, perfil)["etapas"], "etapas"))
                    {
                        var celula = Objeto(c, etapa);
                        if (Texto(celula["modelo"]) is not { } m || !CatalogoIa.ModeloValido(m) ||
                            Texto(celula["nivel"]) is not { } n || !(NiveisDoKit.Contains(n) || etapa == "technical_audit" && (rota == "codex" && n is ("xhigh" or "max" or "ultra") || rota == "claude" && n is ("xhigh" or "max"))))
                            throw new InvalidDataException($"Modelo ou nível inválido em {rota} / {perfil} / {etapa}.");
                        a.Celulas[(rota, perfil, etapa)] = new CelulaModelos(m, n);
                    }
            if (raiz.ContainsKey("reconferencia_economica"))
                a.ReconferenciaEconomica = Booleano(raiz["reconferencia_economica"], "reconferencia_economica");
            var regras = Grupo(raiz, "regras");
            if (regras.ContainsKey("sobe_por_objecao_tecnica"))
                a.SobePorObjecao = Booleano(regras["sobe_por_objecao_tecnica"], "sobe_por_objecao_tecnica");
            if (regras.ContainsKey("desce_com_cota_abaixo_de"))
            {
                a.TrocaDescePorCota = true;
                if (regras["desce_com_cota_abaixo_de"] is { } limite)
                    a.DescePorCota = limite is JsonValue d && d.TryGetValue<double>(out var valor) && valor is >= 0 and <= 1
                        ? valor : throw new InvalidDataException("Limite de cota fora do intervalo de 0 a 1.");
            }
            foreach (var (etapa, x) in Grupo(raiz, "ligada_por_rota"))
                foreach (var (rota, valor) in Objeto(x, etapa))
                    a.Ligadas[(etapa, rota)] = Booleano(valor, $"{etapa} / {rota}");
            return a;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("O arquivo de modelos contém JSON inválido.", ex);
        }
    }

    public static AjusteModelos LerArquivoLocal(string raizDados)
    {
        try { return LerAjuste(File.ReadAllText(ArquivoLocal(raizDados), Encoding.UTF8)); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
    }

    /// <summary>O arquivo do ajuste local: o mesmo formato do kit, só com o que muda.</summary>
    public static string SerializarAjuste(AjusteModelos a) => Nucleo(a.Celulas, a.SobePorObjecao, a.TrocaDescePorCota, a.DescePorCota, a.Ligadas, "ajuste local do Editor de Relatórios", a.ReconferenciaEconomica)
        .ToJsonString(Opcoes) + "\n";

    static JsonObject Nucleo(IReadOnlyDictionary<(string Rota, string Perfil, string Etapa), CelulaModelos> celulas, bool? sobe, bool trocaDesce, double? desce,
        IReadOnlyDictionary<(string Etapa, string Rota), bool> ligadas, string origem, bool reconferenciaEconomica = false)
    {
        var rotas = new JsonObject();
        foreach (var ((rota, perfil, etapa), c) in celulas.OrderBy(k => Array.IndexOf(Rotas, k.Key.Rota)))
        {
            var r = rotas[rota] as JsonObject ?? (JsonObject)(rotas[rota] = new JsonObject { ["perfis"] = new JsonObject() })!;
            var perfis = (JsonObject)r["perfis"]!;
            var p = perfis[perfil] as JsonObject ?? (JsonObject)(perfis[perfil] = new JsonObject { ["etapas"] = new JsonObject() })!;
            ((JsonObject)p["etapas"]!)[etapa] = new JsonObject { ["modelo"] = c.Modelo, ["nivel"] = c.Nivel };
        }
        var o = new JsonObject { ["versao"] = 1, ["origem"] = origem, ["rotas"] = rotas };
        if (reconferenciaEconomica) o["reconferencia_economica"] = true;
        if (sobe is not null || trocaDesce)
        {
            var regras = new JsonObject();
            if (sobe is { } s) regras["sobe_por_objecao_tecnica"] = s;
            if (trocaDesce) regras["desce_com_cota_abaixo_de"] = desce;
            o["regras"] = regras;
        }
        if (ligadas.Count > 0)
        {
            var lig = new JsonObject();
            foreach (var ((etapa, rota), v) in ligadas)
                ((JsonObject)(lig[etapa] ??= new JsonObject()))[rota] = v;
            o["ligada_por_rota"] = lig;
        }
        return o;
    }

    // ---------- opções da grade ----------

    /// <summary>Os modelos que a tabela do kit conhece para a rota Gemini, com os níveis de cada um. É a lista do Gemini Web (que não lista modelos;
    /// <paramref name="conferida"/> true: só esses valem) e a de reserva dos outros perfis enquanto o provedor não respondeu (false: nada é recusado).</summary>
    public static ListaDaRota ListaGemini(TabelaModelosKit kit, bool conferida = true, string fonte = "da tabela do kit (o Gemini Web não lista modelos)") =>
        new(kit.ModelosGemini.Select(m => new ModeloIa(m.Key, m.Key, m.Value.Select(n => kit.Equivalencia.GetValueOrDefault(n, n)).ToList(), null)).ToList(),
            conferida, fonte);

    /// <summary>A lista que a rota Gemini do kit devolveu (<c>google_runtime.py modelos</c>, <see cref="ModelosDoGemini"/>): <c>{modelos: [{modelo, niveis}],
    /// lista_do_provedor, fonte}</c>. Lista do provedor (agy ou API): conferida — um modelo novo aparece sem nova versão do Editor ou do kit, e o que
    /// sumiu dela é trocado pelo padrão do kit. Sem lista do provedor (Gemini Web): os modelos da tabela. null = saída ilegível ou sem modelos.</summary>
    public static ListaDaRota? LerListaGemini(string? json, TabelaModelosKit kit)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o || o["modelos"] is not JsonArray modelos) return null;
            var lista = new List<ModeloIa>();
            foreach (var m in modelos.OfType<JsonObject>())
            {
                if (Texto(m["modelo"]) is not { } id || !CatalogoIa.ModeloValido(id) || lista.Any(x => x.Id == id)) continue;
                var niveis = (m["niveis"] as JsonArray)?.Select(Texto).OfType<string>().Where(kit.Niveis.Contains)
                    .Select(n => kit.Equivalencia.GetValueOrDefault(n, n)).ToList() ?? [];
                lista.Add(new ModeloIa(id, id, niveis, null));
            }
            if (lista.Count == 0) return null;
            var doProvedor = o["lista_do_provedor"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
            return new ListaDaRota(lista, Conferida: true, doProvedor ? "do Gemini (" + (Texto(o["fonte"]) ?? "provedor") + ")" : "da tabela do kit (o Gemini Web não lista modelos)");
        }
        catch (JsonException) { return null; }
    }

    /// <summary>As etapas Gemini da tabela efetiva de um perfil (modelo e esforço do runtime: <c>low</c>, <c>medium</c>, <c>high</c>), para o "Testar" do cartão
    /// Gemini conferir o que a análise vai pedir. Perfil fora da tabela: lista vazia.</summary>
    public static IReadOnlyList<EtapaGemini> EtapasGemini(TabelaEfetiva t, TabelaModelosKit kit, string perfil)
    {
        if (t.Json["rotas"]?["gemini"]?["perfis"]?[perfil]?["etapas"] is not JsonObject etapas) return [];
        return etapas.Select(e => (e.Key, Modelo: Texto(e.Value?["modelo"]), Nivel: Texto(e.Value?["nivel"])))
            .Where(e => e.Modelo is not null && e.Nivel is not null)
            .Select(e => new EtapaGemini(e.Key, e.Modelo!, kit.Equivalencia.GetValueOrDefault(e.Nivel!, e.Nivel!))).ToList();
    }

    /// <summary>O grupo de lista de um perfil Gemini: a Conta Antigravity (agy), a chave (as duas APIs usam a mesma lista) ou o Gemini Web (sem lista).</summary>
    public static string GrupoGemini(string perfil) => perfil switch { "conta_ultra" => "conta", "conta_gemini" => "web", _ => "api" };

    /// <summary>Os modelos da caixa de uma célula: todos os da lista do provedor (um modelo novo aparece sem nova versão do Editor ou do kit), mais o
    /// escolhido e o padrão do kit quando não estão nela (marcados).</summary>
    public static IReadOnlyList<(string Id, string Texto)> OpcoesDeModelo(ListaDaRota lista, CelulaModelos padrao, CelulaModelos? atual)
    {
        var opcoes = lista.Modelos.Select(m => (m.Id, Texto: m.Nome == m.Id ? m.Id : m.Nome + " (" + m.Id + ")")).ToList();
        foreach (var id in new[] { padrao.Modelo, atual?.Modelo })
            if (id is not null && opcoes.All(o => o.Id != id))
                opcoes.Add((id, id + (lista.Conferida ? " (não oferecido agora)" : "")));
        return opcoes.Select(o => o.Id == padrao.Modelo ? (o.Id, o.Texto + " — padrão do kit") : o).ToList();
    }

    /// <summary>Esforços permitidos para a etapa e oferecidos pelo modelo no catálogo disponível.</summary>
    public static IReadOnlyList<string> NiveisDoModelo(TabelaModelosKit kit, ListaDaRota lista, string modelo, string rota = "", string etapa = "")
    {
        var permitidos = NiveisDaEtapa(kit.Bruto, kit.Niveis, rota, etapa);
        var catalogo = lista.Modelos.FirstOrDefault(x => x.Id == modelo);
        if (catalogo is null || catalogo.Niveis.Count == 0) return permitidos;
        return permitidos.Where(n => catalogo.Niveis.Contains(kit.Equivalencia.GetValueOrDefault(n, n))).ToList();
    }

    // ---------- mudar o ajuste ----------

    /// <summary>Muda uma célula do ajuste. Recusa (com a mensagem) modelo que não está na lista do provedor ou nível que o modelo não aceita; o mesmo
    /// valor do padrão do kit tira a célula do ajuste. null = valeu.</summary>
    public static string? Ajustar(AjusteModelos a, TabelaModelosKit kit, ColunaModelos coluna, EtapaModelos etapa, string modelo, string nivel, ListaDaRota lista)
    {
        if (etapa.Programa) return $"A etapa {etapa.Nome} roda por programa com o perfil definido pelo kit.";
        if (PorTarefa(coluna.Rota, etapa.Id)) return "Na análise, o kit escolhe o modelo conforme a tarefa.";
        if (kit.Celula(coluna, etapa.Id) is not { } padrao) return $"A tabela do kit não tem a etapa {etapa.Nome} em {coluna.Titulo}.";
        modelo = (modelo ?? "").Trim();
        if (!CatalogoIa.ModeloValido(modelo)) return "Modelo inválido: " + modelo + ".";
        var naLista = lista.Modelos.Any(m => m.Id == modelo);
        if (!naLista && modelo != padrao.Modelo && lista.Conferida)
            return $"O modelo {modelo} não está na lista {lista.Fonte} ({coluna.Titulo}). Atualize a lista ou escolha um modelo oferecido.";
        if (!NiveisDaEtapa(kit.Bruto, kit.Niveis, coluna.Rota, etapa.Id).Contains(nivel)) return "Nível inválido: " + nivel + ".";
        if (!NiveisDoModelo(kit, lista, modelo, coluna.Rota, etapa.Id).Contains(nivel))
            return $"O modelo {modelo} não aceita o nível {NomeDoNivel(nivel).ToLowerInvariant()} ({coluna.Titulo}).";
        var celula = new CelulaModelos(modelo, nivel);
        if (celula == padrao) a.Celulas.Remove((coluna.Rota, coluna.Perfil, etapa.Id));
        else a.Celulas[(coluna.Rota, coluna.Perfil, etapa.Id)] = celula;
        return null;
    }

    /// <summary>"Restaurar padrão do kit": o ajuste vazio (o arquivo local é apagado; vale o kit).</summary>
    public static AjusteModelos Restaurar() => new();

    public static AjusteModelos AjustesAtivos(AjusteModelos ajuste)
    {
        var ativos = new AjusteModelos { ReconferenciaEconomica = ajuste.ReconferenciaEconomica, SobePorObjecao = ajuste.SobePorObjecao, DescePorCota = ajuste.DescePorCota, TrocaDescePorCota = ajuste.TrocaDescePorCota };
        foreach (var (k, v) in ajuste.Celulas.Where(i => RotasAtivas.Contains(i.Key.Rota))) ativos.Celulas[k] = v;
        foreach (var (k, v) in ajuste.Ligadas.Where(i => RotasAtivas.Contains(i.Key.Rota))) ativos.Ligadas[k] = v;
        return ativos;
    }

    public static AjusteModelos PreservarRotasInativas(AjusteModelos original, AjusteModelos atualizado)
    {
        foreach (var (k, v) in original.Celulas.Where(i => !RotasAtivas.Contains(i.Key.Rota))) atualizado.Celulas[k] = v;
        foreach (var (k, v) in original.Ligadas.Where(i => !RotasAtivas.Contains(i.Key.Rota))) atualizado.Ligadas[k] = v;
        return atualizado;
    }

    /// <summary>Grava o ajuste local (vazio = apaga o arquivo). Gravação atômica: arquivo novo ao lado, depois troca.</summary>
    public static void GravarAjuste(string raizDados, AjusteModelos a)
    {
        var arquivo = ArquivoLocal(raizDados);
        if (a.Vazio)
        {
            if (File.Exists(arquivo)) File.Delete(arquivo);
            return;
        }
        Directory.CreateDirectory(raizDados);
        var tmp = arquivo + ".tmp";
        File.WriteAllText(ArquivoGerado.Caminho(tmp), SerializarAjuste(a), new UTF8Encoding(false));
        File.Move(tmp, ArquivoGerado.Caminho(arquivo), overwrite: true);
    }

    // ---------- avisos e tabela efetiva ----------

    /// <summary>A tabela que vai ao controlador e os avisos. Um modelo ausente da lista conferida é solicitado como configurado. Uma recusa da conta abre a escolha por etapa. As etapas de programa (<c>executor: programa</c> no <c>etapas.json</c>: hoje o preparo e a produção) não têm modelo: não entram na tabela, e uma célula
    /// delas num ajuste local antigo é ignorada com aviso (o kit também a ignora). Todas as rotas vão no arquivo; os avisos dizem a rota.</summary>
    public static TabelaEfetiva Efetiva(TabelaModelosKit kit, AjusteModelos ajuste, IReadOnlyList<EtapaModelos> etapas, Func<ColunaModelos, ListaDaRota?> listas)
    {
        var celulas = new Dictionary<(string, string, string), CelulaModelos>();
        var avisos = new List<AvisoModelos>();
        foreach (var col in kit.Colunas)
        {
            var lista = listas(col);
            foreach (var etapa in etapas.Where(e => e.Programa))
                if (ajuste.Celulas.ContainsKey((col.Rota, col.Perfil, etapa.Id)))
                    avisos.Add(new(col.Rota, $"{col.Titulo}, {etapa.Nome}: o ajuste local deste computador tem modelo para uma etapa que agora roda por programa com o perfil definido pelo kit; esse ajuste foi ignorado."));
            foreach (var etapa in etapas.Where(e => !e.Programa))
            {
                if (kit.Celula(col, etapa.Id) is not { } padrao) continue;
                var celula = ajuste.Celulas.GetValueOrDefault((col.Rota, col.Perfil, etapa.Id)) ?? padrao;
                if (PorTarefa(col.Rota, etapa.Id))
                {
                    // O kit utiliza a célula padrão para o julgamento e escolhe as leituras por tarefa.
                    celulas[(col.Rota, col.Perfil, etapa.Id)] = padrao;
                    continue;
                }
                if (Falta(lista, celula.Modelo))
                    avisos.Add(new(col.Rota, $"{col.Titulo}, {etapa.Nome}: o modelo {celula.Modelo} não aparece mais na lista {lista!.Fonte}. Se a conta recusar, escolha o substituto nesta execução."));
                else if (lista is { Conferida: true } && celula != padrao && !NiveisDoModelo(kit, lista, celula.Modelo, col.Rota, etapa.Id).Contains(celula.Nivel))
                    avisos.Add(new(col.Rota, $"{col.Titulo}, {etapa.Nome}: a lista {lista.Fonte} não confirma o esforço {NomeDoNivel(celula.Nivel)} para {celula.Modelo}. A escolha é mantida até a resposta da conta."));
                celulas[(col.Rota, col.Perfil, etapa.Id)] = celula;
            }
        }
        var json = Nucleo(celulas, ajuste.SobePorObjecao, ajuste.TrocaDescePorCota, ajuste.DescePorCota, ajuste.Ligadas,
            ajuste.Vazio ? "padrão do kit (sem ajuste local)" : "padrão do kit com o ajuste local do Editor de Relatórios",
            ajuste.ReconferenciaEconomica && kit.Bruto.ContainsKey("reconferencia_economica"));
        if (kit.Bruto["rotas"]?["codex"]?["preparo"] is JsonObject preparo)
            json["preparo_codex"] = preparo.DeepClone();
        json["reparo_julgamento_previsto"] = kit.Bruto["rotas"]?["codex"]?["reparo_julgamento"]?.DeepClone();
        if (ajuste.SobePorObjecao is { } subir) json["subir_nova_tentativa_chatgpt"] = subir;
        if (ajuste.TrocaDescePorCota) json["cota_baixa_da_execucao"] = ajuste.DescePorCota;
        json["avisos"] = new JsonArray(avisos.Select(a => (JsonNode?)JsonValue.Create(a.Texto)).ToArray());
        return new TabelaEfetiva(json, avisos);
    }

    static bool Falta(ListaDaRota? lista, string modelo) => lista is { Conferida: true } l && l.Modelos.All(m => m.Id != modelo);

    /// <summary>Revisão final I4: a tabela da partida de uma análise, sem esperar o provedor. Monta a tabela pelas listas já guardadas
    /// (<paramref name="listasGuardadas"/>) e só então dispara <paramref name="atualizarEmSegundoPlano"/> sem esperar (a lista nova vale na próxima
    /// partida e na aba; o pré-voo <c>conferir</c> do kit continua sendo a barreira). O catálogo guardado serve para os avisos. A resposta da conta determina se é necessária a escolha de substituto.
    /// <paramref name="falha"/> recebe o erro da atualização (o registro de erros).</summary>
    public static TabelaDeModelos ParaPartida(TabelaModelosKit kit, AjusteModelos ajuste, IReadOnlyList<EtapaModelos> etapas,
        Func<ColunaModelos, ListaDaRota?> listasGuardadas, string rota, Func<Task>? atualizarEmSegundoPlano, Action<Exception>? falha = null)
    {
        var tabela = Efetiva(kit, ajuste, etapas, listasGuardadas).ParaExecucao(rota);
        if (atualizarEmSegundoPlano is not null) EmSegundoPlano(atualizarEmSegundoPlano, falha);
        return tabela;
    }

    static void EmSegundoPlano(Func<Task> atualizar, Action<Exception>? falha)
    {
        Task t;
        try { t = atualizar(); }
        catch (Exception e) { falha?.Invoke(e); return; }
        _ = t.ContinueWith(x => falha?.Invoke(x.Exception!.GetBaseException()), CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Os avisos da aba: os da tabela efetiva (modelo que sumiu) e as etapas técnicas abaixo de médio (aviso, não bloqueio).</summary>
    public static IReadOnlyList<AvisoModelos> AvisosDaAba(TabelaModelosKit kit, AjusteModelos ajuste, IReadOnlyList<EtapaModelos> etapas, Func<ColunaModelos, ListaDaRota?> listas)
    {
        var efetiva = Efetiva(kit, ajuste, etapas, listas);
        var avisos = efetiva.Avisos.ToList();
        var medio = kit.Niveis.ToList().IndexOf("medio");
        foreach (var col in kit.Colunas)
            foreach (var etapa in etapas.Where(e => kit.EtapasTecnicas.Contains(e.Id) && !e.Programa))
            {
                if (PorTarefa(col.Rota, etapa.Id)) continue;
                var c = ajuste.Celulas.GetValueOrDefault((col.Rota, col.Perfil, etapa.Id)) ?? kit.Celula(col, etapa.Id);
                if (c is not null && medio >= 0 && kit.Niveis.ToList().IndexOf(c.Nivel) < medio)
                    avisos.Add(new(col.Rota, $"{col.Titulo}, {etapa.Nome}: nível {NomeDoNivel(c.Nivel).ToLowerInvariant()} numa etapa técnica (que decide mérito). É permitido, mas o recomendado é médio ou alto."));
            }
        return avisos;
    }

    /// <summary>A etapa opcional está ligada na rota: o ajuste local, senão o padrão do <c>etapas.json</c> do kit.</summary>
    public static bool Ligada(AjusteModelos a, EtapaModelos e, string rota) =>
        a.Ligadas.TryGetValue((e.Id, rota), out var v) ? v : !e.Opcional || e.LigadaPorRota.GetValueOrDefault(rota, true);

    /// <summary>Liga ou desliga a etapa opcional na rota; igual ao padrão do kit tira do ajuste.</summary>
    public static void Ligar(AjusteModelos a, EtapaModelos e, string rota, bool ligada)
    {
        if (!e.Opcional) return;
        if (ligada == e.LigadaPorRota.GetValueOrDefault(rota, true)) a.Ligadas.Remove((e.Id, rota));
        else a.Ligadas[(e.Id, rota)] = ligada;
    }

    // ---------- publicar no kit mestre ----------

    /// <summary>A tabela do kit mestre (<paramref name="mestre"/>, o arquivo atual dele, não a cópia instalada) com o ajuste aplicado (células e regras), no
    /// formato do <c>modelos-por-rota.json</c>: o que "Publicar" grava. Edições do kit mestre que ainda não foram instaladas ficam. Um modelo ou nível Gemini
    /// que o provedor oferece e a tabela ainda não conhece entra em <c>rotas.gemini.modelos</c> (a tabela do kit só aceita os que conhece). O ligado/desligado
    /// da audit fica no ajuste local (o padrão dele é o <c>etapas.json</c> do kit).</summary>
    public static string TabelaParaPublicar(TabelaModelosKit mestre, AjusteModelos a)
    {
        var t = (JsonObject)mestre.Bruto.DeepClone();
        foreach (var ((rota, perfil, etapa), c) in a.Celulas)
            if (!PorTarefa(rota, etapa) && t["rotas"]?[rota]?["perfis"]?[perfil]?["etapas"]?[etapa] is JsonObject cel)
            {
                cel["modelo"] = c.Modelo;
                cel["nivel"] = c.Nivel;
                if (rota == "gemini" && t["rotas"]?["gemini"]?["modelos"] is JsonObject conhecidos)
                {
                    var info = conhecidos[c.Modelo] as JsonObject ?? (JsonObject)(conhecidos[c.Modelo] = new JsonObject { ["niveis"] = new JsonArray() })!;
                    var niveis = info["niveis"] as JsonArray ?? (JsonArray)(info["niveis"] = new JsonArray())!;
                    var atuais = niveis.Select(Texto).OfType<string>().Append(c.Nivel).Distinct().ToList();
                    info["niveis"] = new JsonArray(mestre.Niveis.Where(atuais.Contains).Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
                }
            }
        if (t["regras"] is JsonObject regras)
        {
            if (a.SobePorObjecao is { } s) regras["sobe_por_objecao_tecnica"] = s;
            if (a.TrocaDescePorCota) regras["desce_com_cota_abaixo_de"] = a.DescePorCota;
        }
        return t.ToJsonString(Opcoes) + "\n";
    }

    /// <summary>Por que o ajuste não pode ser publicado sobre a tabela do kit mestre (lista vazia = pode): célula de rota, perfil ou etapa que o kit mestre
    /// não tem (célula de etapa de programa não conta: é ignorada); modelo que não aparece mais na lista conferida do provedor; nível que o modelo não aceita.</summary>
    public static IReadOnlyList<string> ProblemasParaPublicar(TabelaModelosKit mestre, AjusteModelos a, IReadOnlyList<EtapaModelos> etapas, Func<ColunaModelos, ListaDaRota?> listas)
    {
        var problemas = new List<string>();
        foreach (var ((rota, perfil, etapaId), c) in a.Celulas)
        {
            var col = mestre.Colunas.FirstOrDefault(x => x.Rota == rota && x.Perfil == perfil);
            var nome = etapas.FirstOrDefault(e => e.Id == etapaId)?.Nome ?? etapaId;
            if (PorTarefa(rota, etapaId) || etapas.FirstOrDefault(e => e.Id == etapaId) is { Programa: true }) continue;   // etapa de programa: o ajuste antigo é ignorado (aviso na aba), não publicado nem bloqueia
            if (col is null || mestre.Celula(col, etapaId) is null)
            {
                problemas.Add($"a tabela do kit mestre não tem {nome} em {rota}/{perfil}");
                continue;
            }
            if (!CatalogoIa.ModeloValido(c.Modelo) || !NiveisDaEtapa(mestre.Bruto, mestre.Niveis, rota, etapaId).Contains(c.Nivel)) { problemas.Add($"{col.Titulo}, {nome}: modelo ou nível inválido ({c.Modelo}, {c.Nivel})"); continue; }
            var lista = listas(col);
            if (c.Modelo != mestre.Celula(col, etapaId)!.Modelo && Falta(lista, c.Modelo)) problemas.Add($"{col.Titulo}, {nome}: o modelo {c.Modelo} não aparece mais na lista {lista!.Fonte}");
            else if (lista is not null && !NiveisDoModelo(mestre, lista, c.Modelo, rota, etapaId).Contains(c.Nivel))
                problemas.Add($"{col.Titulo}, {nome}: o modelo {c.Modelo} não aceita o nível {NomeDoNivel(c.Nivel).ToLowerInvariant()}");
        }
        return problemas;
    }

    /// <summary>O ajuste depois de publicado: sem as células e as regras (agora são o padrão do kit mestre); fica só o liga/desliga da audit, que não se
    /// publica. Assim o ajuste antigo não esconde um padrão novo que vier depois.</summary>
    public static AjusteModelos SemOPublicado(AjusteModelos a)
    {
        var novo = new AjusteModelos { ReconferenciaEconomica = a.ReconferenciaEconomica };
        foreach (var (k, v) in a.Ligadas) novo.Ligadas[k] = v;
        return novo;
    }

    /// <summary>Onde a tabela fica no kit (mestre ou cópia): <c>01 - PLUGINS\plugins\analise\skills\analise\references\modelos-por-rota.json</c>.</summary>
    public static string ArquivoNoKit(string kit) =>
        Path.Combine(kit, "01 - PLUGINS", "plugins", PluginsInstalados.Analise, "skills", PluginsInstalados.Analise, "references", TabelaDeModelos.NomeNoKit);

    /// <summary>A pasta <c>scripts</c> da skill analise de um kit (onde fica o <c>modelos_por_rota.py</c>).</summary>
    public static string ScriptsNoKit(string kit) => Path.Combine(kit, "01 - PLUGINS", "plugins", PluginsInstalados.Analise, "skills", PluginsInstalados.Analise, "scripts");

    /// <summary>A conferência do próprio kit (<c>modelos_por_rota.conferir</c>, a mesma regra que os controladores aplicam) sobre um arquivo de tabela, pelo
    /// Python do computador: null = válida; senão os erros, em uma frase. <paramref name="argumentosPython"/>: os que vêm antes do script (ex.: <c>-3</c> do
    /// launcher <c>py.exe</c>).</summary>
    public static string? ConferirComOKit(string python, IReadOnlyList<string> argumentosPython, string pastaScripts, string arquivo)
    {
        if (!File.Exists(Path.Combine(pastaScripts, "modelos_por_rota.py"))) return "o kit não tem a conferência da tabela (modelos_por_rota.py) em " + pastaScripts;
        var si = new System.Diagnostics.ProcessStartInfo(python)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = pastaScripts,
        };
        foreach (var x in argumentosPython) si.ArgumentList.Add(x);
        foreach (var x in new[] { "-I", "-B", "-X", "utf8", "-c",
                     "import sys, json; sys.path.insert(0, sys.argv[1]); import modelos_por_rota as m; "
                     + "e = m.conferir(json.loads(open(sys.argv[2], encoding='utf-8-sig').read())); print('; '.join(e)); sys.exit(1 if e else 0)",
                     pastaScripts, arquivo })
            si.ArgumentList.Add(x);
        si.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        si.Environment.Remove(TabelaDeModelos.Variavel);
        try
        {
            using var p = ProcessoInterno.Iniciar(si);
            var saida = p.StandardOutput.ReadToEndAsync();
            var erro = p.StandardError.ReadToEndAsync();
            if (!p.Processo.WaitForExit(60_000)) { try { p.Processo.Kill(true); } catch (InvalidOperationException) { } return "a conferência do kit não terminou em 60 s"; }
            if (p.Processo.ExitCode == 0) return null;
            var texto = saida.Result.Trim();
            return texto.Length > 0 ? texto : "a conferência do kit falhou (" + erro.Result.Trim().Split('\n').LastOrDefault()?.Trim() + ")";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return "não foi possível rodar a conferência do kit: " + e.Message; }
    }

    /// <summary>O resultado de "Publicar": o texto para a aba e se a tabela foi gravada no kit mestre.</summary>
    public sealed record ResultadoPublicacao(string Texto, bool Publicada);

    /// <summary>"Publicar como padrão do kit" (só no computador do kit mestre). Parte do arquivo atual do kit mestre (não da cópia instalada), aplica o ajuste
    /// local por cima (<see cref="TabelaParaPublicar"/>), confere a tabela final com a conferência do próprio kit (<paramref name="conferirNoKit"/>(arquivo
    /// temporário) → null ou os erros) e só então troca o arquivo e abre, fora do Editor, um <c>.cmd</c> que roda o <c>INSTALAR_KIT_PMVNI.cmd manter</c>
    /// (<paramref name="iniciar"/>(explorer.exe, .cmd)); a atualização automática leva aos outros computadores. Recusa, sem gravar nem abrir nada: sem kit
    /// mestre neste computador, com o kit sem a tabela, com análise em andamento, com célula que o kit mestre não tem ou com modelo que sumiu da lista do
    /// provedor (<see cref="ProblemasParaPublicar"/>), ou com a tabela final recusada pelo kit.</summary>
    public static ResultadoPublicacao Publicar(string? kitMestre, AjusteModelos a, IReadOnlyList<EtapaModelos> etapas, Func<ColunaModelos, ListaDaRota?> listas,
        IReadOnlyList<string> analisesEmAndamento, string pastaScripts, Func<string, string?> conferirNoKit, Action<string, string> iniciar)
    {
        ArgumentNullException.ThrowIfNull(analisesEmAndamento);
        ArgumentNullException.ThrowIfNull(conferirNoKit);
        ArgumentNullException.ThrowIfNull(iniciar);
        static ResultadoPublicacao Recusa(string texto) => new(texto, false);
        if (string.IsNullOrWhiteSpace(kitMestre)) return Recusa("Este computador não tem o kit mestre (o instalador do kit não foi configurado com --kit-local). Nada foi publicado.");
        if (analisesEmAndamento.Count > 0) return Recusa(AtualizacaoKit.AvisoAnaliseEmAndamento(analisesEmAndamento));
        var destino = ArquivoNoKit(kitMestre);
        var instalador = Path.Combine(kitMestre, ComponentesDoKit.InstaladorDoKit);
        if (!File.Exists(destino)) return Recusa("O kit mestre (" + kitMestre + ") ainda não tem a tabela de modelos (analise 4.0.0 com a Tarefa 19). Nada foi publicado.");
        if (!File.Exists(instalador)) return Recusa("Não achei o instalador do kit em " + instalador + ". Nada foi publicado.");
        if (LerTabela(File.ReadAllText(destino, Encoding.UTF8)) is not { } mestre)
            return Recusa("A tabela de modelos do kit mestre (" + destino + ") não se lê. Nada foi publicado; confira o arquivo no kit.");
        if (ProblemasParaPublicar(mestre, a, etapas, listas) is { Count: > 0 } problemas)
            return Recusa("Não publicado: " + string.Join("; ", problemas) + ". Ajuste essas células (ou restaure o padrão do kit) e publique de novo.");
        var tmp = destino + ".tmp";
        File.WriteAllText(ArquivoGerado.Caminho(tmp), TabelaParaPublicar(mestre, a), new UTF8Encoding(false));
        string? erro;
        try { erro = conferirNoKit(tmp); }
        catch (Exception e) when (e is IOException or InvalidOperationException) { erro = e.Message; }
        if (erro is not null)
        {
            File.Delete(tmp);
            return Recusa("Não publicado: a conferência do kit recusou a tabela (" + erro + "). O kit mestre não mudou.");
        }
        File.Move(tmp, ArquivoGerado.Caminho(destino), overwrite: true);
        Directory.CreateDirectory(pastaScripts);
        var cmd = Path.Combine(pastaScripts, CmdPublicar);
        File.WriteAllText(ArquivoGerado.Caminho(cmd), "@echo off\r\nrem Editor de Relatórios PMVNI: a tabela de modelos foi publicada no kit mestre; o instalador refaz o índice e as conferências (modo manter).\r\n"
            + "call \"" + instalador + "\" manter\r\n", PrimeiraAbertura.CodificacaoDoConsole());
        iniciar(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), cmd);
        return new("Tabela publicada no kit mestre (" + destino + ") em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))
            + ". O instalador do kit (manter) foi aberto fora do Editor; a atualização automática leva a tabela aos outros computadores.", true);
    }

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
