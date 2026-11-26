using System.Text.Json.Nodes;
using Pmvni.Composicao.Kit;

namespace Pmvni.Revisao;

/// <summary>Um comando de revisão sobre um item ou sobre uma linha de tabela (<see cref="TabelasRevisao"/>: <c>indices/02</c>,
/// <c>ambientes/03</c>; o campo é o caminho do texto na linha). Quem aplica é a <see cref="SessaoRevisao"/>.</summary>
public abstract record Comando(string Item);

/// <summary>Muda a situação do item (só <see cref="Situacoes.SitItem"/>); voltar à original zera a revisão.</summary>
public sealed record DefinirSituacao(string Item, string Situacao) : Comando(Item);

/// <summary>Define (ou, com null/vazio, apaga) a justificativa da mudança de situação.</summary>
public sealed record DefinirJustificativa(string Item, string? Texto) : Comando(Item);

/// <summary>Edita um dos cinco campos de texto (<see cref="Campos.Editaveis"/>); o texto é normalizado e as citações "item NN" são reconvertidas.</summary>
public sealed record DefinirCampo(string Item, string Campo, string Texto) : Comando(Item);

/// <summary>Descarta a revisão de um campo e volta ao texto original.</summary>
public sealed record RestaurarCampo(string Item, string Campo) : Comando(Item);

/// <summary>Tira o item do relatório (ele continua no projeto e pode voltar).</summary>
public sealed record Excluir(string Item) : Comando(Item);

/// <summary>Devolve ao relatório um item excluído, na posição de origem.</summary>
public sealed record Restaurar(string Item) : Comando(Item);

/// <summary>Descarta todas as revisões de texto e de situação do item (a inclusão e a justificativa digitada ficam).</summary>
public sealed record RestaurarItemOriginal(string Item) : Comando(Item);

/// <summary>Cria um item de análise novo ("Novo item", correções 2026-11-16), logo depois de <see cref="DepoisDe"/> (null = no fim), com
/// os mesmos campos dos outros (título, situação, verificação técnica, providência, base legal, referência) preenchidos com os textos
/// de <see cref="ItemNovo"/> para o usuário trocar. <see cref="Item"/> é o id novo (<see cref="IdItem.Novo"/>): quem cria escolhe, para o
/// registro se reaplicar igual. O item entra na numeração (os seguintes sobem um número) e sai no PDF e no resumo como os outros.</summary>
public sealed record CriarItem(string Item, string? DepoisDe = null, string Situacao = ItemNovo.SituacaoInicial) : Comando(Item);

/// <summary>Os textos com que um item novo nasce. Enquanto algum estiver lá, a conferência final bloqueia a exportação
/// (<see cref="ConferenciaRevisao.Erros"/>): um item por preencher não vai para o PDF por engano.</summary>
public static class ItemNovo
{
    /// <summary>FALTA COMPROVAR: o item novo nasce pendente, e o usuário escolhe a situação.</summary>
    public const string SituacaoInicial = "NAO_COMPROVADO";

    public static IReadOnlyDictionary<string, string> Textos { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["titulo"] = "Novo item",
        ["verificacao"] = "Descreva aqui a verificação técnica.",
        ["providencia"] = "Descreva aqui a providência.",
        ["base_legal"] = "Indique aqui a base legal.",
        ["referencia"] = "Indique aqui a referência no projeto.",
    };

    /// <summary>Os campos do item criado que ainda estão com o texto de nascença.</summary>
    public static IReadOnlyList<string> PorPreencher(ItemRevisao it)
    {
        ArgumentNullException.ThrowIfNull(it);
        if (!it.Criado) return [];
        return Campos.Editaveis.Where(c => !it.Campos.TryGetValue(c, out var campo) || campo.Revisado is null || campo.Revisado == Textos[c]).ToList();
    }
}

/// <summary>Acrescenta uma esquadria (vão) a um ambiente da tabela ILUMINAÇÃO E VENTILAÇÃO (correções 2026-11-16): entra no fim da
/// lista de vãos do ambiente e passa a ser editada como as outras. <see cref="Item"/> é a linha do ambiente (<c>ambientes/01</c>).</summary>
public sealed record AcrescentarVao(string Item, string Codigo, string Medidas, string Tipo, string Area) : Comando(Item);

/// <summary>Acrescenta uma chamada numerada ao DIAGRAMA DOS ÍNDICES (correções 2026-11-16). <see cref="Item"/> é o id da linha nova
/// (<see cref="TabelasRevisao.ProximaChamada"/>); <see cref="Ancora"/> diz em que desenho e em que ponto fica a marca com o número
/// (lote, permeavel, edificacao, lateral: planta; telhado, altura, frente, lateral_direita…: volumetria); a situação é a de linha de
/// tabela. <see cref="Vista"/> (correção 2026-11-17: "planta" ou "volumetria") diz em que desenho e em que coluna a chamada entra; com
/// ela, a âncora vale dentro da vista (uma da outra vista vira a equivalente ou um ponto livre). Sem vista, vale a da âncora.</summary>
public sealed record AcrescentarChamada(string Item, string Ancora, string Titulo, string Texto, string Situacao, string? Vista = null) : Comando(Item);

/// <summary>Acrescenta um ambiente à tabela ILUMINAÇÃO E VENTILAÇÃO (pedido do usuário, 2026-11-16: "você adiciona um ambiente e pode
/// pôr janelas e portas"). <see cref="Item"/> é o id da linha nova (<see cref="TabelasRevisao.ProximoAmbiente"/>); entra sem esquadrias,
/// com o mínimo exigido = área do piso ÷ divisor e a situação "falta comprovar" até ganhar as esquadrias (cada uma refaz a conta).</summary>
public sealed record AcrescentarAmbiente(string Item, string Nome, decimal Piso, decimal Divisor, string Pavimento) : Comando(Item);

/// <summary>Remove algo que o usuário acrescentou: a esquadria <see cref="Campo"/> (<c>vaos/2</c>) do ambiente <see cref="Item"/>, ou,
/// sem campo, a chamada <see cref="Item"/>. Só o último acréscimo de cada lista (os outros não mudam de posição). O que veio no
/// relatório recebido nunca é removido.</summary>
public sealed record RemoverAcrescimo(string Item, string? Campo = null) : Comando(Item);

/// <summary>Vários comandos aplicados de uma vez, como UMA alteração: um registro no histórico e um passo do Desfazer (correções
/// 2026-11-16: o ajuste do item pela IA, o recálculo de uma linha de tabela depois de editar o valor). <see cref="Item"/> é o item ou a
/// linha a que o lote pertence (o histórico dele); <see cref="Descricao"/> é o texto do registro ("Recalculado: …"). Os comandos são
/// aplicados na ordem, cada um sobre o resultado do anterior; um recusado recusa o lote inteiro; os que não mudam nada ficam de fora.</summary>
public sealed record Lote(string Item, IReadOnlyList<Comando> Comandos, string Descricao) : Comando(Item);

/// <summary>O comando foi recusado (o texto é a mensagem para o usuário); o projeto não mudou e nada foi registrado.</summary>
public sealed class ComandoRecusadoException(string mensagem) : Exception(mensagem);

/// <summary>O que um comando muda, no formato do histórico: <see cref="Antes"/> e <see cref="Depois"/> são o valor do que mudou
/// (cada <see cref="Tipo"/> tem o seu formato, ver <see cref="Motor"/>); com eles se refaz, desfaz e reaplica sem guardar mais nada.</summary>
internal sealed record Efeito(string Tipo, string Item, string? Campo, JsonNode? Antes, JsonNode? Depois);

/// <summary>Calcula o efeito de um comando sobre um <see cref="Projeto"/> (<see cref="Calcular"/>, sem mudar nada) e aplica um valor
/// registrado (<see cref="Definir"/>, sem validar: serve a aplicar, desfazer, refazer e reaplicar o diário).
/// Formatos de <c>Antes</c>/<c>Depois</c> por tipo: DefinirSituacao = código da situação efetiva (texto); DefinirJustificativa = texto ou null;
/// DefinirCampo/RestaurarCampo = <c>{texto}</c> com o texto guardado (com tokens de citação; null = campo sem texto), e em DefinirCampo o
/// <c>Depois</c> traz também <c>citacoes[{trecho, alvo}]</c>; Excluir/Restaurar = bool "incluído"; RestaurarItemOriginal =
/// <c>{situacao, campos{nome: revisado}}</c> com só o que está revisado.</summary>
internal static class Motor
{
    public static Efeito? Calcular(Projeto p, Comando c)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(c);
        if (c is Lote lote) return CalcularLote(p, lote);
        if (c is DefinirTextoDoModelo texto) return TextosRevisados.Calcular(p, texto);
        if (c is DefinirVisibilidadeDoCampo visibilidade) return TextosRevisados.Calcular(p, visibilidade);
        if (TabelasRevisao.EhLinha(c.Item)) return MotorLinhas.Calcular(p, c); // linha de tabela (Linhas.cs)
        if (c is ComandoFigura cf) return MotorFiguras.Calcular(p, cf);
        if (c is CriarItem criar) return CalcularCriacao(p, criar);
        var it = p.Item(c.Item) ?? throw new ComandoRecusadoException("O item não existe no relatório.");
        if (it.Criado && c is RestaurarCampo or RestaurarItemOriginal)
            throw new ComandoRecusadoException("Este item foi criado na revisão: não há texto original para restaurar.");
        return c switch
        {
            DefinirSituacao d => CalcularSituacao(it, d),
            DefinirJustificativa d => CalcularJustificativa(it, d),
            DefinirCampo d => CalcularCampo(p, it, d),
            RestaurarCampo d => CalcularRestaurarCampo(it, d),
            Excluir => CalcularInclusao(p, it, incluir: false),
            Restaurar => CalcularInclusao(p, it, incluir: true),
            RestaurarItemOriginal => CalcularRestaurarItem(it),
            _ => throw new ComandoRecusadoException("Comando desconhecido: " + c.GetType().Name),
        };
    }

    /// <summary>Põe <paramref name="valor"/> (o "depois" de um registro) no item. <see cref="InvalidOperationException"/> se o item não existe.</summary>
    public static void Definir(Projeto p, string tipo, string item, string? campo, JsonNode? valor)
    {
        if (tipo == nameof(Lote)) { DefinirLote(p, valor); return; }
        if (tipo is nameof(DefinirTextoDoModelo) or nameof(DefinirVisibilidadeDoCampo)) { TextosRevisados.Definir(p, campo!, valor); return; }
        if (TabelasRevisao.EhLinha(item)) { MotorLinhas.Definir(p, tipo, item, campo, valor); return; } // linha de tabela (Linhas.cs)
        if (MotorFiguras.EhTipo(tipo)) { MotorFiguras.Definir(p, valor); return; }
        if (tipo == nameof(CriarItem)) { DefinirCriacao(p, item, valor); return; }
        var it = p.Item(item) ?? throw new InvalidOperationException("O registro cita um item que não existe: " + item);
        switch (tipo)
        {
            case nameof(DefinirSituacao):
                var sit = valor?.GetValue<string>();
                it.Situacao.Revisada = sit is null || (it.Situacao.Original is JsonValue o && o.TryGetValue<string>(out var so) && so == sit) ? null : sit;
                break;
            case nameof(DefinirJustificativa):
                it.Situacao.Justificativa = valor?.GetValue<string>();
                break;
            case nameof(DefinirCampo):
            case nameof(RestaurarCampo):
                DefinirTexto(it, campo ?? throw new InvalidOperationException("O registro de campo não diz qual campo."), valor?["texto"]?.GetValue<string>());
                break;
            case nameof(Excluir):
            case nameof(Restaurar):
                it.Incluido = valor!.GetValue<bool>();
                break;
            case nameof(RestaurarItemOriginal):
                it.Situacao.Revisada = valor?["situacao"]?.GetValue<string>();
                var revisados = valor?["campos"] as JsonObject;
                foreach (var nome in Campos.Editaveis)
                    if (it.Campos.ContainsKey(nome) || revisados?[nome] is not null)
                        DefinirTexto(it, nome, revisados?[nome]?.GetValue<string>());
                break;
            default:
                throw new InvalidOperationException("Tipo de registro desconhecido: " + tipo);
        }
    }

    // ---------- item novo ----------

    /// <summary>O efeito de criar o item: <c>Antes</c> null, <c>Depois</c> = o item inteiro no formato do projeto.json (com a ordem em
    /// que entra). Os itens seguintes sobem uma posição ao aplicar e descem ao desfazer (<see cref="DefinirCriacao"/>).</summary>
    private static Efeito CalcularCriacao(Projeto p, CriarItem c)
    {
        if (!IdItem.Valido(c.Item)) throw new ComandoRecusadoException("O id do item novo é inválido.");
        if (p.Item(c.Item) is not null) throw new ComandoRecusadoException("Já existe um item com este id.");
        if (!Situacoes.SitItem.Contains(c.Situacao)) throw new ComandoRecusadoException("Situação inválida: " + c.Situacao + ".");
        int ordem;
        if (c.DepoisDe is null) ordem = p.Itens.Count == 0 ? 1 : p.Itens.Max(i => i.Ordem) + 1;
        else ordem = (p.Item(c.DepoisDe) ?? throw new ComandoRecusadoException("O item de referência não existe no relatório.")).Ordem + 1;

        // As mesmas chaves, na mesma ordem, de um item recebido (só as que o editor modela); sem nenhum, a ordem do kit.
        var chaves = p.Itens.FirstOrDefault(i => !i.Criado)?.Chaves.Where(k => k == "situacao" || Campos.Editaveis.Contains(k)).ToList() ?? new List<string>();
        foreach (var k in new[] { "titulo", "situacao", "verificacao", "providencia", "base_legal", "referencia" })
            if (!chaves.Contains(k)) chaves.Add(k);
        var novo = new ItemRevisao
        {
            Id = c.Item,
            Ordem = ordem,
            // Depois do último número recebido: nenhuma citação nem nota de figura do original aponta para ele.
            NumeroOriginal = p.Itens.Count == 0 ? 1 : p.Itens.Max(i => i.NumeroOriginal) + 1,
            Incluido = true,
            Criado = true,
            Chaves = chaves,
            Situacao = new SituacaoItem { Original = null, Revisada = c.Situacao },
        };
        foreach (var nome in Campos.Editaveis) novo.Campos[nome] = new CampoTexto { Ausente = true, Revisado = ItemNovo.Textos[nome] };
        return new Efeito(nameof(CriarItem), c.Item, null, null, ProjetoJson.EscreverItem(novo));
    }

    private static void DefinirCriacao(Projeto p, string id, JsonNode? valor)
    {
        if (valor is null)
        {
            var it = p.Item(id) ?? throw new InvalidOperationException("O registro cita um item que não existe: " + id);
            p.Itens.Remove(it);
            foreach (var i in p.Itens.Where(i => i.Ordem > it.Ordem)) i.Ordem--;
            return;
        }
        if (p.Item(id) is not null) throw new InvalidOperationException("O registro cria um item que já existe: " + id);
        var novo = ProjetoJson.LerItem(valor.AsObject());
        foreach (var i in p.Itens.Where(i => i.Ordem >= novo.Ordem)) i.Ordem++;
        p.Itens.Add(novo);
    }

    // ---------- lote ----------

    /// <summary>O efeito do lote: <c>Antes</c>/<c>Depois</c> = <c>{descricao, passos[{tipo, item, campo, valor}]}</c>; os passos do
    /// <c>Antes</c> ficam na ordem inversa (desfazer é aplicá-los como estão). Calculado numa cópia: o projeto não muda.</summary>
    private static Efeito? CalcularLote(Projeto p, Lote l)
    {
        if (l.Comandos.Any(c => c is Lote)) throw new ComandoRecusadoException("Um lote não pode conter outro lote.");
        var copia = p.Clonar();
        var efeitos = new List<Efeito>();
        foreach (var c in l.Comandos)
        {
            if (Calcular(copia, c) is not { } ef) continue;
            Definir(copia, ef.Tipo, ef.Item, ef.Campo, ef.Depois);
            efeitos.Add(ef);
        }
        if (efeitos.Count == 0) return null;
        static JsonNode Passo(Efeito e, JsonNode? valor) =>
            new JsonObject { ["tipo"] = e.Tipo, ["item"] = e.Item, ["campo"] = e.Campo, ["valor"] = valor?.DeepClone() };
        var antes = new JsonObject { ["descricao"] = l.Descricao, ["passos"] = new JsonArray(Enumerable.Reverse(efeitos).Select(e => (JsonNode?)Passo(e, e.Antes)).ToArray()) };
        var depois = new JsonObject { ["descricao"] = l.Descricao, ["passos"] = new JsonArray(efeitos.Select(e => (JsonNode?)Passo(e, e.Depois)).ToArray()) };
        return new Efeito(nameof(Lote), l.Item, null, antes, depois);
    }

    private static void DefinirLote(Projeto p, JsonNode? valor)
    {
        if (valor?["passos"] is not JsonArray passos) throw new InvalidOperationException("O registro do lote não tem os passos.");
        foreach (var n in passos)
        {
            var o = n as JsonObject ?? throw new InvalidOperationException("Passo inválido no registro do lote.");
            Definir(p, o["tipo"]!.GetValue<string>(), o["item"]!.GetValue<string>(), o["campo"]?.GetValue<string>(), o["valor"]);
        }
    }

    /// <summary>Quantos passos tem o registro de um lote e a descrição dele (para o histórico).</summary>
    internal static (string Descricao, int Passos) ResumoDoLote(JsonNode? valor) =>
        (valor?["descricao"] is JsonValue v && v.TryGetValue<string>(out var d) ? d : "", (valor?["passos"] as JsonArray)?.Count ?? 0);

    // ---------- situação e justificativa ----------

    private static Efeito? CalcularSituacao(ItemRevisao it, DefinirSituacao d)
    {
        if (!Situacoes.SitItem.Contains(d.Situacao))
            throw new ComandoRecusadoException("Situação inválida: " + d.Situacao + ".");
        var atual = SituacaoEfetiva(it);
        if (atual == d.Situacao) return null;
        return new Efeito(nameof(DefinirSituacao), it.Id, null, atual is null ? null : JsonValue.Create(atual), JsonValue.Create(d.Situacao));
    }

    internal static string? SituacaoEfetiva(ItemRevisao it) =>
        it.Situacao.Revisada ?? (it.Situacao.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null);

    private static Efeito? CalcularJustificativa(ItemRevisao it, DefinirJustificativa d)
    {
        var novo = d.Texto is null ? null : TextoEditado.Normalizar(d.Texto);
        if (novo is { Length: 0 }) novo = null;
        var atual = it.Situacao.Justificativa;
        if (atual is { Length: 0 }) atual = null;
        if (novo == atual) return null;
        return new Efeito(nameof(DefinirJustificativa), it.Id, null, JsonValue.Create(it.Situacao.Justificativa), JsonValue.Create(novo));
    }

    // ---------- campos ----------

    private static string? Armazenado(CampoTexto c) =>
        c.Revisado ?? (c.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null);

    private static string? OriginalTexto(CampoTexto? c) =>
        c?.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static void ExigirEditavel(string campo)
    {
        if (!Campos.Editaveis.Contains(campo))
            throw new ComandoRecusadoException("O campo \"" + campo + "\" não pode ser editado.");
    }

    private static string MensagemVazio(string campo) => campo switch
    {
        "titulo" => "O título não pode ficar vazio.",
        "verificacao" => "A verificação técnica não pode ficar vazia.",
        "providencia" => "A providência não pode ficar vazia.",
        "base_legal" => "A base legal não pode ficar vazia.",
        _ => "A referência não pode ficar vazia.",
    };

    private static Efeito? CalcularCampo(Projeto p, ItemRevisao it, DefinirCampo d)
    {
        ExigirEditavel(d.Campo);
        var texto = TextoEditado.Normalizar(d.Texto ?? "");
        if (texto.Length == 0) throw new ComandoRecusadoException(MensagemVazio(d.Campo));

        it.Campos.TryGetValue(d.Campo, out var campo);
        if (DadosEfetivos.TextoEfetivo(p, it, d.Campo) == texto) return null;

        var original = OriginalTexto(campo);
        string gravar;
        IReadOnlyList<CitacaoConvertida> convertidas = Array.Empty<CitacaoConvertida>();
        if (original is not null && DadosEfetivos.Imprimir(p, original) == texto)
            gravar = original; // voltou ao original impresso: sem revisão (o original guarda os tokens das citações)
        else
        {
            // A edição devolve números ("item 03"): religa as citações inequívocas. Os números que o texto já citava (como estão
            // impressos agora, inclusive o número original de um item excluído) continuam apontando para o mesmo item; só os números
            // novos usam a numeração de agora.
            (gravar, convertidas) = Citacoes.Converter(texto, p.Incluidos.Select(i => i.Id).ToList(), CitacoesAtuais(p, campo));
        }

        var antes = campo is null ? null : Armazenado(campo);
        if (antes == gravar) return null;

        var depois = new JsonObject
        {
            ["texto"] = gravar,
            ["citacoes"] = new JsonArray(convertidas.Select(c => (JsonNode?)new JsonObject { ["trecho"] = c.Trecho, ["alvo"] = c.Alvo }).ToArray()),
        };
        return new Efeito(nameof(DefinirCampo), it.Id, d.Campo, new JsonObject { ["texto"] = antes }, depois);
    }

    /// <summary>Número impresso → id, para cada citação (token) do texto guardado do campo antes da edição. Se dois itens imprimem o
    /// mesmo número (um excluído e um incluído), vale o primeiro citado.</summary>
    private static IReadOnlyDictionary<int, string> CitacoesAtuais(Projeto p, CampoTexto? campo)
    {
        var mapa = new Dictionary<int, string>();
        var texto = campo is null ? null : Armazenado(campo);
        if (texto is null) return mapa;
        foreach (var id in Citacoes.Alvos(texto))
        {
            var numero = p.NumeroAtual(id) ?? p.Item(id)?.NumeroOriginal;
            if (numero is not null) mapa.TryAdd(numero.Value, id);
        }
        return mapa;
    }

    private static Efeito? CalcularRestaurarCampo(ItemRevisao it, RestaurarCampo d)
    {
        ExigirEditavel(d.Campo);
        if (!it.Campos.TryGetValue(d.Campo, out var campo) || campo.Revisado is null) return null;
        return new Efeito(nameof(RestaurarCampo), it.Id, d.Campo,
            new JsonObject { ["texto"] = campo.Revisado }, new JsonObject { ["texto"] = OriginalTexto(campo) });
    }

    /// <summary>Põe o texto guardado do campo: null ou igual ao original = sem revisão; um campo que o usuário criou e ficou sem texto some do modelo.</summary>
    private static void DefinirTexto(ItemRevisao it, string campo, string? texto)
    {
        if (!it.Campos.TryGetValue(campo, out var c))
        {
            if (texto is null) return;
            c = it.Campos[campo] = new CampoTexto { Ausente = true };
        }
        c.Revisado = texto is null || OriginalTexto(c) == texto ? null : texto;
        if (c.Revisado is null && c.Ausente) it.Campos.Remove(campo);
    }

    // ---------- inclusão ----------

    private static Efeito? CalcularInclusao(Projeto p, ItemRevisao it, bool incluir)
    {
        if (it.Incluido == incluir) return null;
        if (!incluir && p.Incluidos.Count <= 1)
            throw new ComandoRecusadoException("O relatório precisa de ao menos um item incluído.");
        return new Efeito(incluir ? nameof(Restaurar) : nameof(Excluir), it.Id, null, JsonValue.Create(!incluir), JsonValue.Create(incluir));
    }

    // ---------- item inteiro ----------

    private static Efeito? CalcularRestaurarItem(ItemRevisao it)
    {
        var revisados = new JsonObject();
        foreach (var nome in Campos.Editaveis)
            if (it.Campos.TryGetValue(nome, out var c) && c.Revisado is not null) revisados[nome] = c.Revisado;
        if (it.Situacao.Revisada is null && revisados.Count == 0) return null;
        var antes = new JsonObject { ["situacao"] = it.Situacao.Revisada, ["campos"] = revisados };
        var depois = new JsonObject { ["situacao"] = null, ["campos"] = new JsonObject() };
        return new Efeito(nameof(RestaurarItemOriginal), it.Id, null, antes, depois);
    }
}
