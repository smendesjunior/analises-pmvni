namespace Pmvni.App;

/// <summary>Na thread da interface, conserva as últimas extensões inseridas, inclusive as sem ícone.</summary>
internal sealed class CacheDeIcones<T>(int limite)
{
    readonly Dictionary<string, T> imagens = new(StringComparer.Ordinal);
    readonly Queue<string> ordem = new();

    public T Obter(string chave, Func<string, T> carregar)
    {
        if (imagens.TryGetValue(chave, out var imagem)) return imagem;
        imagem = carregar(chave);
        if (ordem.Count >= limite) imagens.Remove(ordem.Dequeue());
        ordem.Enqueue(chave);
        imagens.Add(chave, imagem);
        return imagem;
    }
}
