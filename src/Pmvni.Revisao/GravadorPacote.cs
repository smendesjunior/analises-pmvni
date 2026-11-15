namespace Pmvni.Revisao;

/// <summary>As gravações de um pacote aberto, uma de cada vez: cada gravação espera a anterior, pega o conteúdo só quando chega a vez
/// (o estado mais novo) e usa como "esperado" o disco deixado pela anterior. Assim dois Salvar seguidos (ou Salvar como, ou fechar com
/// gravação) nunca dão conflito consigo mesmos; só a mudança feita por outro programa vira <see cref="ConflitoNoDiscoException"/>.</summary>
public sealed class GravadorPacote(string caminho, EstadoDisco disco)
{
    private readonly SemaphoreSlim _fila = new(1, 1);

    /// <summary>O arquivo atual (muda com <see cref="GravarComoAsync"/>).</summary>
    public string Caminho { get; private set; } = Path.GetFullPath(caminho);

    /// <summary>O disco como foi aberto ou gravado por último por este gravador.</summary>
    public EstadoDisco Disco { get; private set; } = disco;

    /// <summary>Grava no mesmo arquivo. <paramref name="conteudo"/> é chamado quando chega a vez desta gravação (na mesma thread que
    /// pediu, se ela tiver contexto de sincronização): deve devolver uma cópia que a gravação possa ler fora dela.</summary>
    public async Task<EstadoDisco> GravarAsync(Func<ConteudoPacote> conteudo)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        await _fila.WaitAsync();
        try
        {
            var c = conteudo();
            var destino = Caminho;
            var esperado = Disco;
            Disco = await Task.Run(() => Pacote.Gravar(destino, c, esperado));
            return Disco;
        }
        finally { _fila.Release(); }
    }

    /// <summary>Grava num arquivo novo (nunca sobrescreve: se existe, <see cref="IOException"/>) e passa a gravar nele.</summary>
    public async Task<EstadoDisco> GravarComoAsync(string novo, Func<ConteudoPacote> conteudo)
    {
        ArgumentNullException.ThrowIfNull(conteudo);
        var destino = Path.GetFullPath(novo);
        await _fila.WaitAsync();
        try
        {
            if (string.Equals(destino, Caminho, StringComparison.OrdinalIgnoreCase))
            {
                var mesmo = conteudo();
                var esperado = Disco;
                Disco = await Task.Run(() => Pacote.Gravar(destino, mesmo, esperado));
                return Disco;
            }
            if (File.Exists(destino)) throw new IOException("Já existe um arquivo com esse nome: " + Path.GetFileName(destino) + ". Escolha outro nome.");
            var c = conteudo();
            Disco = await Task.Run(() => Pacote.Gravar(destino, c, esperado: null));
            Caminho = destino;
            return Disco;
        }
        finally { _fila.Release(); }
    }
}
