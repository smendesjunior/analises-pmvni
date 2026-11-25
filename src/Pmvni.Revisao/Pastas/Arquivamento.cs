using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Para onde um anexo vai dentro da pasta do processo (a escolha do usuário, linha por linha).</summary>
public enum DestinoAnexo { Documento, ProjetoInicial, ReapresentacaoNova, ReapresentacaoAtual, Bci, ArtRrt, EscrituraCertidao, OutrosDocumentos }

/// <summary>Uma linha da prévia: o caminho final (relativo ao processo), se o mesmo conteúdo já estava lá, ou o motivo da recusa.</summary>
public sealed record PlanoAnexo(string Origem, DestinoAnexo Destino, string? Relativo, bool JaEstava, string? Recusa);

/// <summary>O que foi arquivado (ou já estava): caminho de origem, relativo ao processo, sha256 (minúsculas) e tamanho.</summary>
public sealed record ArquivoArquivado(string Origem, string Relativo, string Sha256, long Tamanho, bool JaEstava)
{
    public string? Categoria { get; init; }
}

/// <summary>Anexos no processo, pela regra do kit (<c>organizacao-arquivos.md</c> e <c>pasta_processo.py</c>, só leitura; desenho §12.3):
/// destino escolhido por anexo, <c>REVISÃO NN</c>, cópia (nunca movimento) sem sobrescrever, sha conferido. Tudo síncrono, para <c>Task.Run</c>.</summary>
public static class Arquivamento
{
    public const string MsgPasta = "Arraste arquivos, não pastas: {0}.";
    public const string MsgAtalho = "{0} é um atalho; anexe o arquivo de destino.";
    /// <summary>{0} = nome, {1} = motivo.</summary>
    public const string MsgIlegivel = "{0} não pôde ser lido: {1}.";
    /// <summary>{0} = a pasta do processo que é um atalho (por exemplo <c>DOCUMENTOS</c>).</summary>
    public const string MsgLinkDestino = "A pasta {0} do processo é um atalho (link); o editor não grava nela.";

    const string SubDocumentos = "DOCUMENTOS";
    const string SubProjeto = "PROJETO";
    const string SubInicial = "INICIAL";
    const string Revisao = "REVISÃO";

    static readonly Regex ComAcento = new(@"^REVISÃO\s+(\d+)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex SemAcento = new(@"^REVISAO\s+(\d+)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Primeira apresentação se <c>PROJETO\INICIAL</c> não existe ou não tem arquivo nenhum; senão, reapresentação nova.</summary>
    public static DestinoAnexo Padrao(string pastaProcesso)
    {
        var inicial = Path.Combine(pastaProcesso, SubProjeto, SubInicial);
        try
        {
            if (Directory.Exists(inicial) && Directory.EnumerateFiles(inicial, "*", SearchOption.AllDirectories).Any())
                return DestinoAnexo.ReapresentacaoNova;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return DestinoAnexo.ReapresentacaoNova; }
        return DestinoAnexo.ProjetoInicial;
    }

    static (string Nome, int Numero)? MaiorRevisao(string pastaProcesso)
    {
        var projeto = Path.Combine(pastaProcesso, SubProjeto);
        if (!Directory.Exists(projeto)) return null;
        var nomes = Directory.EnumerateDirectories(projeto).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
        foreach (var regex in new[] { ComAcento, SemAcento })       // como o kit: com acento; só se não houver, sem acento
        {
            var achadas = nomes
                .Select(n => (Nome: n, M: regex.Match(n)))
                .Where(x => x.M.Success)
                .Select(x => (x.Nome, Numero: int.Parse(x.M.Groups[1].Value, CultureInfo.InvariantCulture)))
                .OrderBy(x => x.Numero)
                .ThenBy(x => x.Nome, StringComparer.Ordinal)
                .ToList();
            if (achadas.Count > 0) return achadas[^1];
        }
        return null;
    }

    /// <summary>O nome da pasta da última revisão (<c>"REVISÃO 02"</c>, como está no disco) ou null.</summary>
    public static string? RevisaoAtual(string pastaProcesso) => MaiorRevisao(pastaProcesso)?.Nome;

    /// <summary><c>"REVISÃO 03"</c>: o maior número + 1, com 2 dígitos e acento; <c>"REVISÃO 01"</c> se não houver nenhuma.</summary>
    public static string ProximaRevisao(string pastaProcesso) =>
        string.Create(CultureInfo.InvariantCulture, $"{Revisao} {(MaiorRevisao(pastaProcesso)?.Numero ?? 0) + 1:D2}");

    static string Sha(string caminho)
    {
        using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(fs));
    }

    public static string? Categoria(DestinoAnexo destino) => destino switch
    {
        DestinoAnexo.ProjetoInicial or DestinoAnexo.ReapresentacaoAtual or DestinoAnexo.ReapresentacaoNova => "projetos",
        DestinoAnexo.Bci => "bci",
        DestinoAnexo.ArtRrt => "art_rrt",
        DestinoAnexo.EscrituraCertidao => "escritura_certidao",
        DestinoAnexo.OutrosDocumentos => "outros_documentos",
        _ => null
    };

    static string PastaDestino(string pastaProcesso, DestinoAnexo destino) => destino switch
    {
        DestinoAnexo.Documento => SubDocumentos,
        DestinoAnexo.Bci => Path.Combine(SubDocumentos, "BCI"),
        DestinoAnexo.ArtRrt => Path.Combine(SubDocumentos, "ART-RRT"),
        DestinoAnexo.EscrituraCertidao => Path.Combine(SubDocumentos, "ESCRITURA OU CERTIDÃO DE INTEIRO TEOR"),
        DestinoAnexo.OutrosDocumentos => Path.Combine(SubDocumentos, "OUTROS DOCUMENTOS"),
        DestinoAnexo.ProjetoInicial => Path.Combine(SubProjeto, SubInicial),
        DestinoAnexo.ReapresentacaoAtual => Path.Combine(SubProjeto, RevisaoAtual(pastaProcesso) ?? ProximaRevisao(pastaProcesso)),
        _ => Path.Combine(SubProjeto, ProximaRevisao(pastaProcesso)),
    };

    /// <summary>O primeiro trecho de <paramref name="relativo"/> (a partir da pasta do processo) que existe e é junção ou link simbólico; null se nenhum.
    /// A própria pasta do processo também conta (devolve ".").</summary>
    internal static string? LinkNoCaminho(string pastaProcesso, string relativo)
    {
        try
        {
            if (new DirectoryInfo(pastaProcesso).LinkTarget is not null) return ".";
            var atual = pastaProcesso;
            foreach (var parte in relativo.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                atual = Path.Combine(atual, parte);
                var info = new DirectoryInfo(atual);
                if (!info.Exists) return null;            // o que ainda não existe será criado por nós: não é atalho
                if (info.LinkTarget is not null) return Path.GetRelativePath(pastaProcesso, atual);
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return relativo;    // sem conseguir saber, recusa (o lado seguro)
        }
    }

    static string NomeComSufixo(string nome, int k)
    {
        if (k <= 1) return nome;
        var raiz = Path.GetFileNameWithoutExtension(nome);
        return string.Create(CultureInfo.InvariantCulture, $"{raiz} ({k}){Path.GetExtension(nome)}");
    }

    static string? Recusar(string origem)
    {
        var nome = Path.GetFileName(origem);
        if (Directory.Exists(origem)) return string.Format(CultureInfo.InvariantCulture, MsgPasta, nome);
        var ext = Path.GetExtension(origem);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || EhLinkSimbolico(origem))
            return string.Format(CultureInfo.InvariantCulture, MsgAtalho, nome);
        if (!File.Exists(origem)) return string.Format(CultureInfo.InvariantCulture, MsgIlegivel, nome, "o arquivo não foi encontrado");
        try
        {
            using var fs = new FileStream(origem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Format(CultureInfo.InvariantCulture, MsgIlegivel, nome, e.Message.TrimEnd('.', ' '));
        }
        return null;
    }

    static bool EhLinkSimbolico(string caminho)
    {
        try { return new FileInfo(caminho).LinkTarget is not null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return true; }
    }

    /// <summary>A prévia: só lê (nada é criado). Cada anexo vira um <see cref="PlanoAnexo"/> com o caminho final ou com a recusa; os recusados
    /// não impedem os outros.</summary>
    public static IReadOnlyList<PlanoAnexo> Planejar(string pastaProcesso, IReadOnlyList<(string Origem, DestinoAnexo Destino)> anexos)
    {
        var plano = new List<PlanoAnexo>();
        var reservados = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // relativo → sha já previsto nesta remessa
        foreach (var (origem, destino) in anexos)
        {
            var recusa = Recusar(origem);
            if (recusa is not null) { plano.Add(new PlanoAnexo(origem, destino, null, false, recusa)); continue; }

            string sha;
            try { sha = Sha(origem); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                plano.Add(new PlanoAnexo(origem, destino, null, false,
                    string.Format(CultureInfo.InvariantCulture, MsgIlegivel, Path.GetFileName(origem), e.Message.TrimEnd('.', ' '))));
                continue;
            }

            var pasta = PastaDestino(pastaProcesso, destino);
            if (LinkNoCaminho(pastaProcesso, pasta) is { } link)
            {
                plano.Add(new PlanoAnexo(origem, destino, null, false, string.Format(CultureInfo.InvariantCulture, MsgLinkDestino, link)));
                continue;
            }
            var nome = Path.GetFileName(origem).ToUpperInvariant();
            for (var k = 1; ; k++)
            {
                var relativo = Path.Combine(pasta, NomeComSufixo(nome, k));
                if (reservados.TryGetValue(relativo, out var shaReservado))
                {
                    if (shaReservado == sha) { plano.Add(new PlanoAnexo(origem, destino, relativo, true, null)); break; }
                    continue;
                }
                var caminho = Path.Combine(pastaProcesso, relativo);
                if (File.Exists(caminho))
                {
                    var igual = false;
                    try { igual = Sha(caminho) == sha; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    if (igual)
                    {
                        reservados[relativo] = sha;
                        plano.Add(new PlanoAnexo(origem, destino, relativo, true, null));
                        break;
                    }
                    continue;
                }
                reservados[relativo] = sha;
                plano.Add(new PlanoAnexo(origem, destino, relativo, false, null));
                break;
            }
        }
        return plano;
    }

    /// <summary>Copia o que o plano manda (os recusados e os "já estava" não geram cópia): cria as pastas, grava cada cópia num <c>.tmp</c> oculto
    /// da mesma pasta, confere o sha e a move SEM sobrescrever. A origem é só lida. Devolve um item por anexo aceito, na ordem do plano.</summary>
    public static IReadOnlyList<ArquivoArquivado> Arquivar(string pastaProcesso, IReadOnlyList<PlanoAnexo> plano, CancellationToken ct)
    {
        var feitos = new List<ArquivoArquivado>();
        var copiadosNestaRemessa = new Dictionary<string, (string Sha, long Tamanho)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plano)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Recusa is not null || item.Relativo is null) continue;

            var destinoPasta = Path.GetDirectoryName(Path.Combine(pastaProcesso, item.Relativo))!;
            var existente = Path.Combine(pastaProcesso, item.Relativo);
            if (item.JaEstava && (copiadosNestaRemessa.ContainsKey(item.Relativo) || File.Exists(existente)))
            {
                // O mesmo conteúdo já está no destino (de antes ou copiado neste mesmo lote): nada a copiar.
                if (copiadosNestaRemessa.TryGetValue(item.Relativo, out var info))
                    feitos.Add(new ArquivoArquivado(item.Origem, item.Relativo, info.Sha, info.Tamanho, true) { Categoria = Categoria(item.Destino) });
                else
                    feitos.Add(new ArquivoArquivado(item.Origem, item.Relativo, Sha(existente), new FileInfo(existente).Length, true) { Categoria = Categoria(item.Destino) });
                continue;
            }

            // O plano pode ser velho: de novo, nada é gravado através de um atalho (junção ou link simbólico) posto no lugar de uma pasta do processo.
            if (LinkNoCaminho(pastaProcesso, Path.GetRelativePath(pastaProcesso, destinoPasta)) is { } link)
                throw new IOException(string.Format(CultureInfo.InvariantCulture, MsgLinkDestino, link));
            Directory.CreateDirectory(destinoPasta);
            feitos.Add(Copiar(pastaProcesso, item, destinoPasta, copiadosNestaRemessa, ct));
        }
        return feitos;
    }

    static ArquivoArquivado Copiar(string pastaProcesso, PlanoAnexo item, string destinoPasta,
        Dictionary<string, (string Sha, long Tamanho)> copiados, CancellationToken ct)
    {
        var tmp = Path.Combine(destinoPasta, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string shaOrigem;
            long tamanho;
            DateTime escritaUtc;
            using (var entrada = new FileStream(item.Origem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var saida = new FileStream(ArquivoGerado.Caminho(tmp), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                File.SetAttributes(tmp, FileAttributes.Hidden);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1 << 20];
                int n;
                tamanho = 0;
                while ((n = entrada.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, n);
                    saida.Write(buffer, 0, n);
                    tamanho += n;
                }
                saida.Flush(true);
                shaOrigem = Convert.ToHexStringLower(hash.GetHashAndReset());
                escritaUtc = File.GetLastWriteTimeUtc(item.Origem);
            }
            if (Sha(tmp) != shaOrigem) throw new IOException($"A cópia de {Path.GetFileName(item.Origem)} não confere com o original.");

            // Move sem sobrescrever; se alguém criou o nome nesse meio tempo, segue a regra do kit (mesmo sha: já estava; senão "(n)").
            var nomeOrigem = Path.GetFileName(item.Origem).ToUpperInvariant();
            var nomePlanejado = Path.GetFileName(item.Relativo!);
            for (var k = 1; k <= 1000; k++)
            {
                ct.ThrowIfCancellationRequested();
                var nomeAlvo = k == 1 ? nomePlanejado : NomeComSufixo(nomeOrigem, k);
                if (k > 1 && nomeAlvo.Equals(nomePlanejado, StringComparison.OrdinalIgnoreCase)) continue;
                var alvo = Path.Combine(destinoPasta, nomeAlvo);
                try
                {
                    File.Move(tmp, ArquivoGerado.Caminho(alvo), overwrite: false);
                    File.SetAttributes(alvo, FileAttributes.Normal);
                    File.SetLastWriteTimeUtc(alvo, escritaUtc);
                    var relativo = Path.GetRelativePath(pastaProcesso, alvo);
                    copiados[relativo] = (shaOrigem, tamanho);
                    return new ArquivoArquivado(item.Origem, relativo, shaOrigem, tamanho, false) { Categoria = Categoria(item.Destino) };
                }
                catch (IOException) when (File.Exists(alvo))
                {
                    if (Sha(alvo) == shaOrigem)
                    {
                        var relativo = Path.GetRelativePath(pastaProcesso, alvo);
                        copiados[relativo] = (shaOrigem, tamanho);
                        return new ArquivoArquivado(item.Origem, relativo, shaOrigem, tamanho, true) { Categoria = Categoria(item.Destino) };
                    }
                }
            }
            throw new IOException($"Não foi possível achar um nome livre para {nomeOrigem} em {destinoPasta}.");
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* o .tmp oculto fica; nada mais a fazer */ }
        }
    }
}
