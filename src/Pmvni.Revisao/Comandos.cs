using System.Text.Json.Nodes;
using Pmvni.Composicao.Kit;

namespace Pmvni.Revisao;

/// <summary>Um comando de revisão sobre um item. Quem aplica é a <see cref="SessaoRevisao"/>.</summary>
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
        if (c is ComandoFigura cf) return MotorFiguras.Calcular(p, cf);
        var it = p.Item(c.Item) ?? throw new ComandoRecusadoException("O item não existe no relatório.");
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
        if (MotorFiguras.EhTipo(tipo)) { MotorFiguras.Definir(p, valor); return; }
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
