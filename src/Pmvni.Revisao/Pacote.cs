using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>O pacote não é um <c>.pmvrel</c> aceitável (ZIP ilegível, entrada proibida, limite estourado, hash ou assinatura que não confere).
/// A mensagem é para o usuário, em português.</summary>
public sealed class PacoteInvalidoException(string mensagem) : Exception(mensagem);

/// <summary>O arquivo no disco mudou desde o <see cref="EstadoDisco"/> esperado (outra janela, OneDrive, outro PC): a gravação não trocou nada.
/// <see cref="Atual"/> é o estado que está no disco agora.</summary>
public sealed class ConflitoNoDiscoException(EstadoDisco atual)
    : Exception("O arquivo foi alterado fora deste editor desde que foi aberto; nada foi sobrescrito.")
{
    public EstadoDisco Atual { get; } = atual;
}

/// <summary>Como o arquivo está no disco: tamanho, data de gravação (UTC) e sha256 do conteúdo. Serve para detectar mudança de fora.</summary>
public sealed record EstadoDisco(long Tamanho, DateTime GravadoUtc, string Sha256)
{
    /// <summary>Mesmo conteúdo (sha256 e tamanho). A data não conta: o OneDrive pode mexer só nela, e isso não é conflito.</summary>
    public bool MesmoConteudo(EstadoDisco? outro) =>
        outro is not null && Tamanho == outro.Tamanho && string.Equals(Sha256, outro.Sha256, StringComparison.Ordinal);

    /// <summary>Lê o arquivo inteiro (sha256). <see cref="IOException"/> se outro processo o segura de modo que não dá para ler.</summary>
    public static EstadoDisco Ler(string caminho)
    {
        ArgumentNullException.ThrowIfNull(caminho);
        using var fs = Abrir(caminho);
        return De(fs);
    }

    internal static FileStream Abrir(string caminho) =>
        new(caminho, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);

    internal static EstadoDisco De(FileStream fs)
    {
        fs.Position = 0;
        var sha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        return new EstadoDisco(fs.Length, File.GetLastWriteTimeUtc(fs.SafeFileHandle), sha);
    }
}

/// <summary>Limites da leitura de um pacote (contra ZIP malicioso): bytes por entrada, bytes no total e razão máxima descomprimido/comprimido.</summary>
public sealed record LimitesPacote(long PorEntrada = 52_428_800, long Total = 524_288_000, double RazaoMaxima = 100);

/// <summary>Um pacote lido: o conteúdo, o estado do arquivo no momento da leitura e se só pode ser aberto para leitura (esquema mais novo).</summary>
public sealed record PacoteAberto(ConteudoPacote Conteudo, EstadoDisco Disco, bool SomenteLeitura, string? MotivoSomenteLeitura);

/// <summary>O <c>.pmvrel</c> (um ZIP): leitura que nunca confia no arquivo e gravação que nunca deixa o pacote antigo pela metade.</summary>
public static class Pacote
{
    /// <summary>A razão de compressão só é conferida em entradas acima de 1 MB descomprimido; os limites absolutos valem sempre.</summary>
    private const long PisoDaRazao = 1_048_576;

    private const string NomeManifesto = "manifesto.json";
    private const string NomeRecebido = "original/recebido.json";
    private const string NomeProjeto = "projeto.json";
    private const string NomeHistorico = "historico.jsonl";

    private static readonly Regex NomeAsset = new("^assets/(?<sha>[0-9a-f]{64})\\.(?<ext>png|jpg|webp|gif)$", RegexOptions.CultureInvariant);

    public static PacoteAberto Ler(string caminho, LimitesPacote? limites = null)
    {
        ArgumentNullException.ThrowIfNull(caminho);
        limites ??= new LimitesPacote();

        using var fs = EstadoDisco.Abrir(caminho);
        var disco = EstadoDisco.De(fs);
        fs.Position = 0;
        try
        {
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: true);
            return Ler(zip, disco, limites);
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or JsonException)
        {
            throw new PacoteInvalidoException("O arquivo não é um pacote .pmvrel válido: " + e.Message);
        }
    }

    private static PacoteAberto Ler(ZipArchive zip, EstadoDisco disco, LimitesPacote limites)
    {
        // 1. Nomes: nada que escape da pasta, nada repetido (o Windows não distingue caixa).
        var entradas = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (NomeInseguro(e.FullName)) throw new PacoteInvalidoException("O pacote tem uma entrada com nome proibido.");
            if (!entradas.TryAdd(e.FullName, e)) throw new PacoteInvalidoException("O pacote tem uma entrada repetida: " + e.FullName);
        }

        long total = 0;
        byte[] Conteudo(string nome)
        {
            if (!entradas.TryGetValue(nome, out var e)) throw new PacoteInvalidoException("Falta " + nome + " no pacote.");
            return LerEntrada(e, limites, ref total);
        }

        // 2. Manifesto e versão do esquema.
        var manifesto = LerManifesto(Conteudo(NomeManifesto));
        var versao = VersaoDe(manifesto.VersaoEsquema);
        var atual = Version.Parse(Manifesto.VersaoEsquemaAtual);
        if (versao < atual) throw new PacoteInvalidoException("Versão do esquema não reconhecida: " + manifesto.VersaoEsquema + ".");
        var maior = versao > atual;

        // 3. Entradas conhecidas; as demais só são toleradas num esquema mais novo (e ignoradas).
        var fixas = new HashSet<string>(StringComparer.Ordinal) { NomeManifesto, NomeRecebido, NomeProjeto, NomeHistorico };
        var assets = new List<(string Nome, Match M)>();
        foreach (var nome in entradas.Keys)
        {
            if (fixas.Contains(nome)) continue;
            var m = NomeAsset.Match(nome);
            if (m.Success) assets.Add((nome, m));
            else if (!maior) throw new PacoteInvalidoException("O pacote tem uma entrada desconhecida: " + nome);
        }
        foreach (var nome in fixas)
            if (!entradas.ContainsKey(nome)) throw new PacoteInvalidoException("Falta " + nome + " no pacote.");
        foreach (var chave in manifesto.Partes.Keys)
            if (!fixas.Contains(chave) && !NomeAsset.IsMatch(chave) && !maior)
                throw new PacoteInvalidoException("O manifesto lista uma parte desconhecida: " + chave);

        // 4. Conteúdo conferido contra o manifesto.
        byte[] Parte(string nome)
        {
            var bytes = Conteudo(nome);
            if (!manifesto.Partes.TryGetValue(nome, out var esperado)) throw new PacoteInvalidoException(nome + " não está no manifesto.");
            if (!string.Equals(esperado, Sha(bytes), StringComparison.OrdinalIgnoreCase))
                throw new PacoteInvalidoException(nome + " não confere com o manifesto (arquivo alterado ou corrompido).");
            return bytes;
        }

        var recebido = Parte(NomeRecebido);
        var projetoBytes = Parte(NomeProjeto);
        var historicoBytes = Parte(NomeHistorico);

        var imagens = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (nome, m) in assets.OrderBy(a => a.Nome, StringComparer.Ordinal))
        {
            var bytes = Parte(nome);
            var ext = Importador.ExtensaoPelaAssinatura(bytes);
            if (ext is null || ext != m.Groups["ext"].Value)
                throw new PacoteInvalidoException(nome + ": a assinatura da imagem não confere com a extensão (só PNG, JPEG, WebP ou GIF).");
            if (!string.Equals(m.Groups["sha"].Value, Sha(bytes), StringComparison.Ordinal))
                throw new PacoteInvalidoException(nome + ": o nome da imagem não é o sha256 do seu conteúdo.");
            imagens[nome] = bytes;
        }
        foreach (var chave in manifesto.Partes.Keys)
            if (NomeAsset.IsMatch(chave) && !imagens.ContainsKey(chave))
                throw new PacoteInvalidoException("Falta " + chave + " no pacote.");

        Projeto projeto;
        IReadOnlyList<RegistroHistorico> historico;
        try
        {
            projeto = ProjetoJson.Ler(projetoBytes);
            historico = HistoricoJsonl.Ler(historicoBytes);
        }
        catch (JsonException e)
        {
            throw new PacoteInvalidoException("O pacote não pôde ser lido" + (maior ? " (versão mais nova do esquema)" : "") + ": " + e.Message);
        }

        ConferirImagensDoDocumento(projeto, imagens);
        foreach (var id in projeto.Linhas.Keys)
            if (!TabelasRevisao.Existe(projeto, id))
                throw new PacoteInvalidoException("O pacote revisa a linha de tabela " + id + ", que não existe no relatório.");

        if (!maior && VersaoDe(projeto.VersaoEsquema) > atual) maior = true;
        string? motivo = maior
            ? "Este pacote é de uma versão mais nova do editor (esquema " + manifesto.VersaoEsquema + "; este editor conhece o " + Manifesto.VersaoEsquemaAtual
              + "). Ele só pode ser aberto para leitura."
            : null;
        return new PacoteAberto(new ConteudoPacote(manifesto, recebido, projeto, historico, imagens), disco, maior, motivo);
    }

    /// <summary>Toda imagem do <c>documento</c> (o mapa da localização e cada figura) tem de ser <c>assets/&lt;sha256&gt;.&lt;ext&gt;</c>, um asset
    /// que está no pacote: um <c>.pmvrel</c> adulterado com caminho absoluto, de rede (<c>\\host\share\x.png</c>) ou com <c>..</c> nunca chega ao
    /// renderizador (que leria o arquivo do computador ou abriria uma conexão SMB).</summary>
    private static void ConferirImagensDoDocumento(Projeto projeto, IReadOnlyDictionary<string, byte[]> imagens)
    {
        void Conferir(JsonNode? dono, string onde)
        {
            if (dono is not JsonObject o || !o.TryGetPropertyValue("imagem", out var no)) return;
            var nome = no is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            if (nome is null || !NomeAsset.IsMatch(nome) || !imagens.ContainsKey(nome))
                throw new PacoteInvalidoException(onde + ": a imagem não é uma das imagens do pacote (" + (nome ?? no?.ToJsonString() ?? "null") + ").");
        }

        Conferir(projeto.Documento["localizacao"], "Localização");
        if (projeto.Documento["figuras"] is JsonArray figuras)
            for (var i = 0; i < figuras.Count; i++) Conferir(figuras[i], "Figura " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Manifesto LerManifesto(byte[] bytes)
    {
        try { return Manifesto.Ler(bytes); }
        catch (JsonException e) { throw new PacoteInvalidoException("O manifesto do pacote é inválido: " + e.Message); }
    }

    private static Version VersaoDe(string texto)
    {
        if (!Version.TryParse(texto, out var v)) throw new PacoteInvalidoException("Versão do esquema inválida: " + texto + ".");
        return v;
    }

    /// <summary>Nome com <c>..</c>, começando por <c>/</c> ou <c>\</c>, com <c>:</c> ou <c>\</c>, vazio ou com caractere de controle.</summary>
    private static bool NomeInseguro(string nome) =>
        nome.Length == 0
        || nome.Contains("..", StringComparison.Ordinal)
        || nome[0] is '/' or '\\'
        || nome.Contains(':') || nome.Contains('\\')
        || nome.Any(char.IsControl);

    /// <summary>O tamanho vale pelo que foi LIDO (o cabeçalho do ZIP pode mentir) e a leitura para no limite.</summary>
    private static byte[] LerEntrada(ZipArchiveEntry e, LimitesPacote limites, ref long total)
    {
        var comprimido = Math.Max(1, e.CompressedLength);
        using var s = e.Open();
        using var memoria = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            memoria.Write(buffer, 0, n);
            total += n;
            if (memoria.Length > limites.PorEntrada)
                throw new PacoteInvalidoException(e.FullName + " passa do limite de " + limites.PorEntrada + " bytes por entrada.");
            if (total > limites.Total)
                throw new PacoteInvalidoException("O pacote passa do limite de " + limites.Total + " bytes descomprimidos.");
            if (memoria.Length > PisoDaRazao && memoria.Length / (double)comprimido > limites.RazaoMaxima)
                throw new PacoteInvalidoException(e.FullName + " tem razão de compressão acima de " + limites.RazaoMaxima + " (possível bomba de compressão).");
        }
        return memoria.ToArray();
    }

    /// <summary>Grava o pacote sem nunca deixar o arquivo antigo pela metade: escreve <c>nome.pmvrel.tmp</c> (oculto) ao lado, confere relendo, e só
    /// então troca (<c>File.Replace</c>, guardando <c>nome.pmvrel.bak</c>) ou move. Se o destino existe, ele tem de estar igual a
    /// <paramref name="esperado"/>, senão <see cref="ConflitoNoDiscoException"/> e nada é trocado. Em qualquer exceção o <c>.tmp</c> é apagado.
    /// <paramref name="antesDeTrocar"/> e <paramref name="depoisDeTrocar"/> são ganchos de teste (depois da conferência e antes da troca; logo
    /// depois da troca). A conferência de conflito compara o conteúdo (sha256 e tamanho), não a data. Depois da troca nada mais pode falhar: o
    /// disco já tem a versão nova. Devolve o novo estado do disco.</summary>
    public static EstadoDisco Gravar(string caminho, ConteudoPacote c, EstadoDisco? esperado, Action? antesDeTrocar = null, Action? depoisDeTrocar = null)
    {
        ArgumentNullException.ThrowIfNull(caminho);
        ArgumentNullException.ThrowIfNull(c);
        var destino = Path.GetFullPath(caminho);
        var tmp = destino + ".tmp";
        var bak = destino + ".bak";
        var trocando = false;

        try
        {
            ApagarSePreciso(tmp);
            var partes = Escrever(tmp, c);
            var relido = Ler(tmp);
            var gravado = relido.Disco; // os bytes que vão para o destino: sha256 e tamanho conhecidos
            var lidas = relido.Conteudo.Manifesto.Partes;
            if (lidas.Count != partes.Count || partes.Any(p => !lidas.TryGetValue(p.Key, out var sha) || sha != p.Value))
                throw new IOException("A conferência do pacote gravado falhou: o conteúdo relido não é o que foi gravado.");

            antesDeTrocar?.Invoke();

            if (File.Exists(destino))
            {
                var atual = EstadoDisco.Ler(destino);
                if (!atual.MesmoConteudo(esperado)) throw new ConflitoNoDiscoException(atual);
                trocando = true;
                File.Replace(tmp, destino, bak);
            }
            else
            {
                trocando = true;
                File.Move(tmp, destino);
            }

            depoisDeTrocar?.Invoke();
            return DepoisDaTroca(destino, gravado);
        }
        catch
        {
            // Se a troca falhou e o destino sumiu, o .tmp é a única cópia da versão nova: fica (o .bak guarda a antiga).
            if (!(trocando && !File.Exists(destino))) ApagarSePreciso(tmp);
            throw;
        }
    }

    /// <summary>Acabamento depois da troca, que não pode derrubar a gravação (o disco já tem a versão nova; o OneDrive ou o antivírus podem segurar
    /// o arquivo por um instante): tira o atributo oculto e relê o estado, com poucas tentativas curtas; se não conseguir, devolve o estado dos bytes
    /// que acabaram de ser gravados.</summary>
    private static EstadoDisco DepoisDaTroca(string destino, EstadoDisco gravado)
    {
        const int tentativas = 5;
        for (var i = 0; i < tentativas; i++)
        {
            try
            {
                var atributos = File.GetAttributes(destino) & ~FileAttributes.Hidden;
                File.SetAttributes(destino, atributos == 0 ? FileAttributes.Normal : atributos);
                break;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(50); }
        }
        for (var i = 0; i < tentativas; i++)
        {
            try { return EstadoDisco.Ler(destino); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(50); }
        }
        return gravado;
    }

    private static void ApagarSePreciso(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return;
            File.SetAttributes(caminho, FileAttributes.Normal);
            File.Delete(caminho);
        }
        catch (IOException) { /* limpeza: não esconde a exceção original */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Escreve o ZIP em <paramref name="tmp"/> (oculto) e devolve as partes (nome → sha256) que o manifesto gravado lista.</summary>
    private static Dictionary<string, string> Escrever(string tmp, ConteudoPacote c)
    {
        var recebido = c.Recebido;
        var projeto = ProjetoJson.Escrever(c.Projeto);
        var historico = HistoricoJsonl.Escrever(c.Historico);

        var partes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NomeRecebido] = Sha(recebido),
            [NomeProjeto] = Sha(projeto),
            [NomeHistorico] = Sha(historico),
        };
        foreach (var (nome, bytes) in c.Assets.OrderBy(a => a.Key, StringComparer.Ordinal)) partes[nome] = Sha(bytes);
        var manifesto = (c.Manifesto with { Partes = partes }).Escrever();

        using var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        File.SetAttributes(tmp, FileAttributes.Hidden);
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            Adicionar(zip, NomeManifesto, manifesto);
            Adicionar(zip, NomeRecebido, recebido);
            Adicionar(zip, NomeProjeto, projeto);
            Adicionar(zip, NomeHistorico, historico);
            foreach (var (nome, bytes) in c.Assets.OrderBy(a => a.Key, StringComparer.Ordinal)) Adicionar(zip, nome, bytes);
        }
        fs.Flush(true);
        return partes;
    }

    private static void Adicionar(ZipArchive zip, string nome, byte[] bytes)
    {
        var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal);
        using var s = entrada.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}
