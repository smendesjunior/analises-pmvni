using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Os cinco campos de texto que o usuário pode editar em cada item. Os demais campos do item ficam em
/// <see cref="ItemRevisao.Outros"/>, como vieram.</summary>
public static class Campos
{
    public static IReadOnlyList<string> Editaveis { get; } = new[] { "titulo", "verificacao", "providencia", "base_legal", "referencia" };

    /// <summary>Campos que o desenho deixa esvaziar. O plano recusa esvaziar qualquer um (a conferência do kit rejeita campo vazio até em
    /// rascunho); a lista fica aqui porque é parte do vocabulário do modelo.</summary>
    public static IReadOnlySet<string> PodemFicarVazios { get; } = new HashSet<string>(StringComparer.Ordinal) { "providencia", "base_legal", "referencia" };
}

/// <summary>Situação de um item: valor original, situação revisada (null = sem revisão) e justificativa digitada.</summary>
public sealed class SituacaoItem
{
    public JsonNode? Original { get; set; }
    public string? Revisada { get; set; }
    public string? Justificativa { get; set; }

    internal SituacaoItem Clonar() => new() { Original = Original?.DeepClone(), Revisada = Revisada, Justificativa = Justificativa };
}

/// <summary>Um campo de texto de item. <see cref="Original"/> já leva os tokens de citação; <see cref="Ausente"/> = a chave não existia no
/// original (só o usuário a criou); <see cref="Revisado"/> null = sem revisão.</summary>
public sealed class CampoTexto
{
    public JsonNode? Original { get; set; }
    public bool Ausente { get; set; }
    public string? Revisado { get; set; }

    internal CampoTexto Clonar() => new() { Original = Original?.DeepClone(), Ausente = Ausente, Revisado = Revisado };
}

/// <summary>Um item do relatório com o que o usuário revisou. <see cref="Chaves"/> é a ordem original das chaves do item no dados.json;
/// <see cref="Outros"/> são as chaves que o editor não modela (copiadas como vieram).</summary>
public sealed class ItemRevisao
{
    public string Id { get; set; } = "";
    public int Ordem { get; set; }
    public int NumeroOriginal { get; set; }
    public bool Incluido { get; set; } = true;
    public List<string> Chaves { get; set; } = new();
    public SituacaoItem Situacao { get; set; } = new();
    public Dictionary<string, CampoTexto> Campos { get; set; } = new(StringComparer.Ordinal);
    public JsonObject Outros { get; set; } = new();

    internal ItemRevisao Clonar()
    {
        var campos = new Dictionary<string, CampoTexto>(StringComparer.Ordinal);
        foreach (var (nome, campo) in Campos) campos[nome] = campo.Clonar();
        return new ItemRevisao
        {
            Id = Id,
            Ordem = Ordem,
            NumeroOriginal = NumeroOriginal,
            Incluido = Incluido,
            Chaves = new List<string>(Chaves),
            Situacao = Situacao.Clonar(),
            Campos = campos,
            Outros = (JsonObject)Outros.DeepClone(),
        };
    }
}

/// <summary>O estado revisado do relatório (conteúdo do <c>projeto.json</c>): os itens e o resto do dados.json como veio.</summary>
public sealed class Projeto
{
    public string VersaoEsquema { get; set; } = Manifesto.VersaoEsquemaAtual;
    public List<ItemRevisao> Itens { get; set; } = new();

    /// <summary>O dados.json sem os itens: a chave <c>itens</c> fica no lugar original com <c>[]</c>.</summary>
    public JsonObject Documento { get; set; } = new();

    /// <summary>A camada de edição das figuras (editor de figuras, recortes de prancha). Null = nenhuma figura foi editada: as figuras saem do
    /// <see cref="Documento"/> como vieram.</summary>
    public List<FiguraRevisao>? Figuras { get; set; }

    public Projeto Clonar() => new()
    {
        VersaoEsquema = VersaoEsquema,
        Itens = Itens.Select(i => i.Clonar()).ToList(),
        Documento = (JsonObject)Documento.DeepClone(),
        Figuras = Figuras?.Select(f => f.Clonar()).ToList(),
    };

    public ItemRevisao? Item(string id) => Itens.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    /// <summary>Os itens incluídos, por <see cref="ItemRevisao.Ordem"/>.</summary>
    public IReadOnlyList<ItemRevisao> Incluidos => Itens.Where(i => i.Incluido).OrderBy(i => i.Ordem).ToList();

    /// <summary>Posição (1 = primeiro) entre os incluídos; null se o item não existe ou está excluído.</summary>
    public int? NumeroAtual(string id)
    {
        var n = 0;
        foreach (var i in Incluidos)
        {
            n++;
            if (string.Equals(i.Id, id, StringComparison.Ordinal)) return n;
        }
        return null;
    }
}

/// <summary>Quem está editando e como o app mede o tempo e sorteia: tudo o que um comando precisa do ambiente (os testes trocam o relógio e o sorteio).</summary>
public sealed record ContextoUsuario(string Usuario, Func<DateTimeOffset> Relogio, RandomNumberGenerator Rng)
{
    /// <summary>O usuário do Windows (sem domínio), o relógio UTC e o sorteio do sistema.</summary>
    public static ContextoUsuario Atual()
    {
        var nome = Environment.UserName ?? "";
        var barra = nome.LastIndexOf('\\');
        if (barra >= 0) nome = nome[(barra + 1)..];
        return new ContextoUsuario(nome, () => DateTimeOffset.UtcNow, RandomNumberGenerator.Create());
    }
}

/// <summary>O que há dentro de um <c>.pmvrel</c>. <see cref="Assets"/>: chave <c>assets/&lt;sha256&gt;.&lt;ext&gt;</c> → bytes da imagem.</summary>
public sealed record ConteudoPacote(
    Manifesto Manifesto,
    byte[] Recebido,
    Projeto Projeto,
    IReadOnlyList<RegistroHistorico> Historico,
    IReadOnlyDictionary<string, byte[]> Assets)
{
    /// <summary>Grava cada asset em <c>&lt;pasta&gt;/assets/&lt;sha256&gt;.&lt;ext&gt;</c>, o caminho que o <c>documento</c> do projeto usa.</summary>
    public void ExtrairAssets(string pasta)
    {
        var raiz = Path.GetFullPath(pasta);
        foreach (var (chave, bytes) in Assets)
        {
            var destino = Path.GetFullPath(Path.Combine(raiz, chave.Replace('/', Path.DirectorySeparatorChar)));
            if (!destino.StartsWith(Path.TrimEndingDirectorySeparator(raiz) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Asset fora da pasta de destino: " + chave);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.WriteAllBytes(destino, bytes);
        }
    }
}
