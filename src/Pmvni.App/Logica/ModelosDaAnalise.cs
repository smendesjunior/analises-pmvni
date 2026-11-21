using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Agentes;

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

    public CelulaModelos? Celula(ColunaModelos c, string etapa) => Celulas.TryGetValue((c.Rota, c.Perfil, etapa), out var x) ? x : null;
}

/// <summary>O ajuste local deste computador (<c>%APPDATA%\EditorRelatoriosPMVNI\modelos-por-rota.local.json</c>): só o que difere do padrão do kit.</summary>
public sealed class AjusteModelos
{
    public Dictionary<(string Rota, string Perfil, string Etapa), CelulaModelos> Celulas { get; } = new();
    public bool? SobePorObjecao { get; set; }
    /// <summary>true = o ajuste troca o limite de cota (<see cref="DescePorCota"/>; null nele = não desce por cota).</summary>
    public bool TrocaDescePorCota { get; set; }
    public double? DescePorCota { get; set; }
    /// <summary>(etapa, rota) → ligada; só etapas opcionais.</summary>
    public Dictionary<(string Etapa, string Rota), bool> Ligadas { get; } = new();

    public bool Vazio => Celulas.Count == 0 && SobePorObjecao is null && !TrocaDescePorCota && Ligadas.Count == 0;
}

/// <summary>Um aviso da aba ou da partida, por rota.</summary>
public sealed record AvisoModelos(string Rota, string Texto);

/// <summary>A tabela que vai para o controlador nesta execução e os avisos (modelo trocado pelo padrão do kit, que ficam no histórico).</summary>
public sealed record TabelaEfetiva(JsonObject Json, IReadOnlyList<AvisoModelos> Avisos)
{
    public TabelaDeModelos ParaExecucao(string rota) =>
        new(Json.ToJsonString(ModelosDaAnalise.Opcoes), Avisos.Where(a => a.Rota == rota).Select(a => a.Texto).ToList());
}

/// <summary>Tarefa 19: Ajustes › Inteligência artificial › Modelos da análise. O padrão vem do kit instalado (igual em todos os computadores); o ajuste
/// local opcional fica em <see cref="ArquivoLocal"/> (o Editor não é MSIX: grava direto em %APPDATA%). Antes de cada análise, <see cref="Efetiva"/> monta a
/// tabela que vai ao controlador: uma célula ajustada cujo modelo deixou de aparecer na lista do provedor usa o padrão do kit, com aviso (na aba, no Novo
/// relatório e no histórico da execução). As opções vêm das listas que o Editor já atualiza por provedor (<see cref="CatalogoModelosIa"/>); na rota Gemini,
/// dos modelos que a própria tabela do kit conhece. No computador do kit mestre, <see cref="Publicar"/> grava a tabela no kit e roda o
/// <c>INSTALAR_KIT_PMVNI.cmd manter</c> fora do Editor. Tudo aqui é testável sem WinUI.</summary>
public static class ModelosDaAnalise
{
    public const string NomeArquivoLocal = "modelos-por-rota.local.json";
    public const string CmdPublicar = "publicar-modelos-no-kit.cmd";
    public static readonly string[] Rotas = ["codex", "claude", "gemini"];

    public static string ArquivoLocal(string raizDados) => Path.Combine(raizDados, NomeArquivoLocal);

    internal static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true, IndentSize = 2, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static Provedor ProvedorDaRota(string rota) => rota switch { "claude" => Provedor.Claude, "gemini" => Provedor.Gemini, _ => Provedor.ChatGpt };

    public static string RotaDoProvedor(Provedor p) => p switch { Provedor.Claude => "claude", Provedor.Gemini => "gemini", _ => "codex" };

    public static string NomeDoNivel(string nivel) => nivel switch { "baixo" => "Baixo", "medio" => "Médio", "alto" => "Alto", _ => nivel };

    static readonly string[] NiveisDoKit = ["baixo", "medio", "alto"];
    static readonly Dictionary<string, string> EquivalenciaPadrao = new() { ["baixo"] = "low", ["medio"] = "medium", ["alto"] = "high" };

    // ---------- leitura ----------

    /// <summary>A tabela do kit; null se o texto não é a tabela (kit anterior ou arquivo quebrado: a aba avisa e não ajusta nada).</summary>
    public static TabelaModelosKit? LerTabela(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json.TrimStart('﻿')) is not JsonObject raiz || raiz["rotas"] is not JsonObject rotas) return null;
            var niveis = (raiz["niveis"] as JsonArray)?.Select(Texto).OfType<string>().ToList() is { Count: > 0 } n ? n : NiveisDoKit.ToList();
            var eq = raiz["equivalencia"] is JsonObject e ? e.ToDictionary(k => k.Key, k => Texto(k.Value) ?? k.Key) : EquivalenciaPadrao;
            var colunas = new List<ColunaModelos>();
            var celulas = new Dictionary<(string, string, string), CelulaModelos>();
            foreach (var rota in Rotas)
            {
                if (rotas[rota] is not JsonObject r || r["perfis"] is not JsonObject perfis) return null;
                var nomeRota = Texto(r["nome"]) ?? Provedores.Nome(ProvedorDaRota(rota));
                foreach (var (perfil, pNo) in perfis)
                {
                    if (pNo is not JsonObject p || p["etapas"] is not JsonObject etapas) return null;
                    colunas.Add(new ColunaModelos(rota, perfil, perfis.Count > 1 ? nomeRota + " · " + (Texto(p["nome"]) ?? perfil) : nomeRota));
                    foreach (var (etapa, cNo) in etapas)
                        if (cNo is JsonObject c && Texto(c["modelo"]) is { } modelo && Texto(c["nivel"]) is { } nivel && niveis.Contains(nivel))
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
            if (regras?["excecoes_por_rota"] is JsonObject exc)
                foreach (var (rota, x) in exc)
                    if (Texto(x?["nota"]) is { } nota) notas[rota] = nota;
            return new TabelaModelosKit
            {
                Bruto = raiz, Niveis = niveis, Equivalencia = eq, Colunas = colunas, Celulas = celulas, ModelosGemini = gemini,
                EtapasTecnicas = (raiz["etapas_tecnicas"] as JsonArray)?.Select(Texto).OfType<string>().ToList() ?? ["analyze", "technical_audit"],
                SobePorObjecao = regras?["sobe_por_objecao_tecnica"] is JsonValue s && s.TryGetValue<bool>(out var sobe) && sobe,
                DescePorCota = regras?["desce_com_cota_abaixo_de"] is JsonValue d && d.TryGetValue<double>(out var desce) ? desce : null,
                NotasDasRotas = notas,
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
        if (string.IsNullOrWhiteSpace(json)) return a;
        try
        {
            if (JsonNode.Parse(json.TrimStart('﻿')) is not JsonObject raiz) return a;
            if (raiz["rotas"] is JsonObject rotas)
                foreach (var (rota, r) in rotas)
                    if (r?["perfis"] is JsonObject perfis)
                        foreach (var (perfil, p) in perfis)
                            if (p?["etapas"] is JsonObject etapas)
                                foreach (var (etapa, c) in etapas)
                                    if (Texto(c?["modelo"]) is { } m && Texto(c?["nivel"]) is { } n && CatalogoIa.ModeloValido(m) && NiveisDoKit.Contains(n))
                                        a.Celulas[(rota, perfil, etapa)] = new CelulaModelos(m, n);
            if (raiz["regras"] is JsonObject regras)
            {
                if (regras["sobe_por_objecao_tecnica"] is JsonValue s && s.TryGetValue<bool>(out var sobe)) a.SobePorObjecao = sobe;
                if (regras.ContainsKey("desce_com_cota_abaixo_de"))
                {
                    a.TrocaDescePorCota = true;
                    a.DescePorCota = regras["desce_com_cota_abaixo_de"] is JsonValue d && d.TryGetValue<double>(out var desce) && desce is >= 0 and <= 1 ? desce : null;
                }
            }
            if (raiz["ligada_por_rota"] is JsonObject lig)
                foreach (var (etapa, x) in lig)
                    if (x is JsonObject porRota)
                        foreach (var (rota, v) in porRota)
                            if (v is JsonValue b && b.TryGetValue<bool>(out var ligada)) a.Ligadas[(etapa, rota)] = ligada;
        }
        catch (Exception x) when (x is JsonException or InvalidOperationException) { }
        return a;
    }

    /// <summary>O arquivo do ajuste local: o mesmo formato do kit, só com o que muda.</summary>
    public static string SerializarAjuste(AjusteModelos a) => Nucleo(a.Celulas, a.SobePorObjecao, a.TrocaDescePorCota, a.DescePorCota, a.Ligadas, "ajuste local do Editor de Relatórios")
        .ToJsonString(Opcoes) + "\n";

    static JsonObject Nucleo(IReadOnlyDictionary<(string Rota, string Perfil, string Etapa), CelulaModelos> celulas, bool? sobe, bool trocaDesce, double? desce,
        IReadOnlyDictionary<(string Etapa, string Rota), bool> ligadas, string origem)
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

    /// <summary>A lista de modelos da rota Gemini: os que a tabela do kit conhece, com os níveis de cada um (a rota não usa outros).</summary>
    public static ListaDaRota ListaGemini(TabelaModelosKit kit) =>
        new(kit.ModelosGemini.Select(m => new ModeloIa(m.Key, m.Key, m.Value.Select(n => kit.Equivalencia.GetValueOrDefault(n, n)).ToList(), null)).ToList(),
            Conferida: true, "da tabela do kit");

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

    /// <summary>Os níveis do kit (baixo, médio, alto) que o modelo aceita; sem a informação, os três.</summary>
    public static IReadOnlyList<string> NiveisDoModelo(TabelaModelosKit kit, ListaDaRota lista, string modelo)
    {
        var m = lista.Modelos.FirstOrDefault(x => x.Id == modelo);
        if (m is null || m.Niveis.Count == 0) return kit.Niveis;
        var aceitos = kit.Niveis.Where(n => m.Niveis.Contains(kit.Equivalencia.GetValueOrDefault(n, n))).ToList();
        return aceitos.Count > 0 ? aceitos : kit.Niveis;
    }

    // ---------- mudar o ajuste ----------

    /// <summary>Muda uma célula do ajuste. Recusa (com a mensagem) modelo que não está na lista do provedor ou nível que o modelo não aceita; o mesmo
    /// valor do padrão do kit tira a célula do ajuste. null = valeu.</summary>
    public static string? Ajustar(AjusteModelos a, TabelaModelosKit kit, ColunaModelos coluna, EtapaModelos etapa, string modelo, string nivel, ListaDaRota lista)
    {
        if (etapa.Programa) return $"A etapa {etapa.Nome} roda por programa, sem IA: não tem modelo.";
        if (kit.Celula(coluna, etapa.Id) is not { } padrao) return $"A tabela do kit não tem a etapa {etapa.Nome} em {coluna.Titulo}.";
        modelo = (modelo ?? "").Trim();
        if (!CatalogoIa.ModeloValido(modelo)) return "Modelo inválido: " + modelo + ".";
        var naLista = lista.Modelos.Any(m => m.Id == modelo);
        if (!naLista && modelo != padrao.Modelo && (lista.Conferida || coluna.Rota == "gemini"))
            return $"O modelo {modelo} não está na lista {lista.Fonte} ({coluna.Titulo}). Atualize a lista ou escolha um modelo oferecido.";
        if (!kit.Niveis.Contains(nivel)) return "Nível inválido: " + nivel + ".";
        if (!NiveisDoModelo(kit, lista, modelo).Contains(nivel))
            return $"O modelo {modelo} não aceita o nível {NomeDoNivel(nivel).ToLowerInvariant()} ({coluna.Titulo}).";
        var celula = new CelulaModelos(modelo, nivel);
        if (celula == padrao) a.Celulas.Remove((coluna.Rota, coluna.Perfil, etapa.Id));
        else a.Celulas[(coluna.Rota, coluna.Perfil, etapa.Id)] = celula;
        return null;
    }

    /// <summary>"Restaurar padrão do kit": o ajuste vazio (o arquivo local é apagado; vale o kit).</summary>
    public static AjusteModelos Restaurar() => new();

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
        File.WriteAllText(tmp, SerializarAjuste(a), new UTF8Encoding(false));
        File.Move(tmp, arquivo, overwrite: true);
    }

    // ---------- avisos e tabela efetiva ----------

    /// <summary>A tabela que vai ao controlador e os avisos. Uma célula do ajuste cujo modelo não está mais na lista conferida do provedor usa o padrão do
    /// kit (aviso "trocado"); o padrão do kit que também não está na lista só gera aviso (o kit tenta e, se o provedor recusar, a etapa para sem trocar de
    /// modelo). As células de etapas de programa não entram. Todas as rotas vão no arquivo; os avisos dizem a rota.</summary>
    public static TabelaEfetiva Efetiva(TabelaModelosKit kit, AjusteModelos ajuste, IReadOnlyList<EtapaModelos> etapas, Func<string, ListaDaRota?> listas)
    {
        var celulas = new Dictionary<(string, string, string), CelulaModelos>();
        var avisos = new List<AvisoModelos>();
        foreach (var col in kit.Colunas)
        {
            var lista = col.Rota == "gemini" ? ListaGemini(kit) : listas(col.Rota);
            foreach (var etapa in etapas.Where(e => !e.Programa))
            {
                if (kit.Celula(col, etapa.Id) is not { } padrao) continue;
                var celula = ajuste.Celulas.GetValueOrDefault((col.Rota, col.Perfil, etapa.Id)) ?? padrao;
                if (celula != padrao && Falta(lista, celula.Modelo))
                {
                    avisos.Add(new(col.Rota, $"{col.Titulo}, {etapa.Nome}: o modelo {celula.Modelo} não aparece mais na lista {lista!.Fonte}; esta etapa usa o padrão do kit ({padrao.Modelo}, nível {NomeDoNivel(padrao.Nivel).ToLowerInvariant()})."));
                    celula = padrao;
                }
                else if (celula != padrao && !NiveisDoModelo(kit, lista ?? new([], false, ""), celula.Modelo).Contains(celula.Nivel))
                {
                    avisos.Add(new(col.Rota, $"{col.Titulo}, {etapa.Nome}: o modelo {celula.Modelo} não aceita mais o nível {NomeDoNivel(celula.Nivel).ToLowerInvariant()}; esta etapa usa o padrão do kit ({padrao.Modelo}, nível {NomeDoNivel(padrao.Nivel).ToLowerInvariant()})."));
                    celula = padrao;
                }
                if (celula == padrao && Falta(lista, padrao.Modelo))
                    avisos.Add(new(col.Rota, $"{col.Titulo}, {etapa.Nome}: o modelo padrão do kit ({padrao.Modelo}) não aparece na lista {lista!.Fonte}; se o provedor não o aceitar, a etapa para sem trocar de modelo."));
                celulas[(col.Rota, col.Perfil, etapa.Id)] = celula;
            }
        }
        var json = Nucleo(celulas, ajuste.SobePorObjecao, ajuste.TrocaDescePorCota, ajuste.DescePorCota, ajuste.Ligadas,
            ajuste.Vazio ? "padrão do kit (sem ajuste local)" : "padrão do kit com o ajuste local do Editor de Relatórios");
        json["avisos"] = new JsonArray(avisos.Select(a => (JsonNode?)JsonValue.Create(a.Texto)).ToArray());
        return new TabelaEfetiva(json, avisos);
    }

    static bool Falta(ListaDaRota? lista, string modelo) => lista is { Conferida: true } l && l.Modelos.All(m => m.Id != modelo);

    /// <summary>Os avisos da aba: os da tabela efetiva (modelo que sumiu) e as etapas técnicas abaixo de médio (aviso, não bloqueio).</summary>
    public static IReadOnlyList<AvisoModelos> AvisosDaAba(TabelaModelosKit kit, AjusteModelos ajuste, IReadOnlyList<EtapaModelos> etapas, Func<string, ListaDaRota?> listas)
    {
        var efetiva = Efetiva(kit, ajuste, etapas, listas);
        var avisos = efetiva.Avisos.ToList();
        var medio = kit.Niveis.ToList().IndexOf("medio");
        foreach (var col in kit.Colunas)
            foreach (var etapa in etapas.Where(e => kit.EtapasTecnicas.Contains(e.Id) && !e.Programa))
            {
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

    /// <summary>A tabela do kit com o ajuste aplicado (células e regras), no formato do <c>modelos-por-rota.json</c>: o que "Publicar" grava no kit mestre.
    /// O ligado/desligado da audit fica no ajuste local (o padrão dele é o <c>etapas.json</c> do kit).</summary>
    public static string TabelaParaPublicar(TabelaModelosKit kit, AjusteModelos a)
    {
        var t = (JsonObject)kit.Bruto.DeepClone();
        foreach (var ((rota, perfil, etapa), c) in a.Celulas)
            if (t["rotas"]?[rota]?["perfis"]?[perfil]?["etapas"]?[etapa] is JsonObject cel)
            {
                cel["modelo"] = c.Modelo;
                cel["nivel"] = c.Nivel;
            }
        if (t["regras"] is JsonObject regras)
        {
            if (a.SobePorObjecao is { } s) regras["sobe_por_objecao_tecnica"] = s;
            if (a.TrocaDescePorCota) regras["desce_com_cota_abaixo_de"] = a.DescePorCota;
        }
        return t.ToJsonString(Opcoes) + "\n";
    }

    /// <summary>Onde a tabela fica no kit (mestre ou cópia): <c>01 - PLUGINS\plugins\analise\skills\analise\references\modelos-por-rota.json</c>.</summary>
    public static string ArquivoNoKit(string kit) =>
        Path.Combine(kit, "01 - PLUGINS", "plugins", PluginsInstalados.Analise, "skills", PluginsInstalados.Analise, "references", TabelaDeModelos.NomeNoKit);

    /// <summary>"Publicar como padrão do kit" (só no computador do kit mestre): grava a tabela no kit mestre e abre, fora do Editor, um <c>.cmd</c> que roda o
    /// <c>INSTALAR_KIT_PMVNI.cmd manter</c> (<paramref name="iniciar"/>(explorer.exe, .cmd)); a atualização automática leva aos outros computadores. Recusa
    /// sem kit mestre neste computador, com o kit sem a tabela (anterior à Tarefa 19) ou com análise em andamento. Devolve o texto para a aba.</summary>
    public static string Publicar(string? kitMestre, TabelaModelosKit kit, AjusteModelos a, IReadOnlyList<string> analisesEmAndamento, string pastaScripts, Action<string, string> iniciar)
    {
        ArgumentNullException.ThrowIfNull(analisesEmAndamento);
        ArgumentNullException.ThrowIfNull(iniciar);
        if (string.IsNullOrWhiteSpace(kitMestre)) return "Este computador não tem o kit mestre (o instalador do kit não foi configurado com --kit-local). Nada foi publicado.";
        if (analisesEmAndamento.Count > 0) return AtualizacaoKit.AvisoAnaliseEmAndamento(analisesEmAndamento);
        var destino = ArquivoNoKit(kitMestre);
        var instalador = Path.Combine(kitMestre, ComponentesDoKit.InstaladorDoKit);
        if (!File.Exists(destino)) return "O kit mestre (" + kitMestre + ") ainda não tem a tabela de modelos (analise 4.0.0 com a Tarefa 19). Nada foi publicado.";
        if (!File.Exists(instalador)) return "Não achei o instalador do kit em " + instalador + ". Nada foi publicado.";
        var texto = TabelaParaPublicar(kit, a);
        var tmp = destino + ".tmp";
        File.WriteAllText(tmp, texto, new UTF8Encoding(false));
        File.Move(tmp, destino, overwrite: true);
        Directory.CreateDirectory(pastaScripts);
        var cmd = Path.Combine(pastaScripts, CmdPublicar);
        File.WriteAllText(cmd, "@echo off\r\nrem Editor de Relatórios PMVNI: a tabela de modelos foi publicada no kit mestre; o instalador refaz o índice e as conferências (modo manter).\r\n"
            + "call \"" + instalador + "\" manter\r\n", PrimeiraAbertura.CodificacaoDoConsole());
        iniciar(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), cmd);
        return "Tabela publicada no kit mestre (" + destino + ") em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"))
            + ". O instalador do kit (manter) foi aberto fora do Editor; a atualização automática leva a tabela aos outros computadores.";
    }

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
