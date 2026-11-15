using Pmvni.Revisao;

namespace Pmvni.Composicao.Web;

/// <summary>Pasta de trabalho de um pacote aberto: <c>&lt;dados&gt;\edicao\&lt;guid&gt;</c> com as imagens do pacote em
/// <c>assets\</c>, onde o renderizador do kit as lê pelos caminhos do <c>documento</c> do projeto. O <c>dados.json</c> efetivo
/// nunca é gravado (o render recebe os bytes); <see cref="CaminhoDados"/> só dá a pasta-base. Apagada no <see cref="Dispose"/>;
/// as que sobraram de uma queda do app (mais de um dia sem alteração) são apagadas na criação de uma nova.</summary>
public sealed class PastaTrabalho : IDisposable
{
    int apagada;

    PastaTrabalho(string caminho) => Caminho = caminho;

    /// <summary>A pasta (<c>&lt;dados&gt;\edicao\&lt;guid&gt;</c>).</summary>
    public string Caminho { get; }

    /// <summary><c>&lt;Caminho&gt;\dados.json</c>: o arquivo não precisa existir.</summary>
    public string CaminhoDados => Path.Combine(Caminho, "dados.json");

    /// <summary>Preenchido quando a pasta não pôde ser apagada no <see cref="Dispose"/> (arquivo preso).</summary>
    public Exception? ErroAoApagar { get; private set; }

    public static PastaTrabalho Criar(ConteudoPacote c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var raiz = Path.Combine(PastaDados.Raiz, "edicao");
        DocumentoWeb.LimparOrfas(raiz);
        var pasta = Path.Combine(raiz, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try { c.ExtrairAssets(pasta); }
        catch
        {
            try { Directory.Delete(pasta, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return new PastaTrabalho(pasta);
    }

    /// <summary>Marca a pasta como viva (data de alteração = agora): a varredura de órfãs (mais de um dia sem alteração) de outra instância
    /// nunca apaga a pasta de uma sessão aberta. Falha (pasta presa, apagada) só não toca.</summary>
    public void Tocar()
    {
        try { if (Directory.Exists(Caminho)) Directory.SetLastWriteTimeUtc(Caminho, DateTime.UtcNow); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref apagada, 1) != 0) return;
        Exception? ultimo = null;
        for (var i = 0; i < 20; i++)
        {
            try { if (Directory.Exists(Caminho)) Directory.Delete(Caminho, true); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ultimo = e; Thread.Sleep(50); }
        }
        ErroAoApagar = ultimo;
    }
}
