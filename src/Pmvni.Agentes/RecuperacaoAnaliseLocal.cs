using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pmvni.Agentes;

/// <summary>Os recibos do kit são opacos ao transporte. O checkpoint preserva seus bytes e informa a raiz em que foram assinados.</summary>
internal static partial class RecuperacaoAnaliseLocal
{
    static readonly string[] Rotas = ["analise", "analise-pro", "analise-google"];
    static readonly StringComparer Nomes = StringComparer.OrdinalIgnoreCase;
    sealed record Arquivo(string Nome, string Sha256, long Bytes, DateTime Modificado);
    sealed record Manifesto(int Versao, string Processo, string Execucao, string Estacao, string RaizLocal, string Rota, List<Arquivo> Arquivos);
    sealed record Ponteiro(string Atual, string? Anterior);
    internal static readonly string Produtos = Path.Combine("RELATÓRIOS DE ANÁLISE", "Entregas do Editor");
    sealed record Entrega(int Versao, string Processo, string Execucao, string Controle, string Modo, string? EtapaPendente,
        string Documento, string? Pdf, List<Arquivo> Arquivos);

    static string Raiz(string caminho) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(caminho));
    static string Digest(Stream fluxo) => Convert.ToHexStringLower(SHA256.HashData(fluxo));
    static string Hash(string caminho) { using var fluxo = File.OpenRead(caminho); return Digest(fluxo); }

    static string NomeExecucao(string id) => Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]{1,128}\z")
        ? id : throw new InvalidDataException("Identificador de execução inválido.");

    static string NomeRota(string rota)
    {
        var nome = rota.Replace('/', '\\');
        if (nome.StartsWith(@".pmvni-ai\", StringComparison.OrdinalIgnoreCase)) nome = nome[10..];
        return Rotas.FirstOrDefault(r => Nomes.Equals(r, nome)) ?? throw new InvalidDataException("Pasta de controle inválida.");
    }

    static string Dentro(string raiz, string relativo)
    {
        if (string.IsNullOrWhiteSpace(relativo) || Path.IsPathRooted(relativo) || relativo.Contains(':') ||
            relativo.Split(['/', '\\']).Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(' ') || p.EndsWith('.') ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Caminho relativo inválido: " + relativo);
        var destino = Path.GetFullPath(Path.Combine(raiz, relativo));
        if (!destino.StartsWith(Raiz(raiz) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Destino fora da pasta da análise.");
        for (var atual = destino; atual != null && atual.Length >= Raiz(raiz).Length; atual = Path.GetDirectoryName(atual))
        {
            FileSystemInfo info = Directory.Exists(atual) ? new DirectoryInfo(atual) : new FileInfo(atual);
            if (info.LinkTarget != null) throw new InvalidDataException("Caminho redirecionado: " + atual);
        }
        return destino;
    }

    static string DiretorioCheckpoint(string origem, string execucao) =>
        Dentro(origem, Path.Combine(".pmvni-ai", "retomadas-locais", NomeExecucao(execucao)));

    internal static bool Existe(string origem, string execucao) => LerPonteiro(DiretorioCheckpoint(origem, execucao)) != null;

    static IEnumerable<string> Percorrer(string pasta, bool topoDoControle = false)
    {
        foreach (var info in new DirectoryInfo(pasta).EnumerateFileSystemInfos())
        {
            if (info.LinkTarget != null) throw new InvalidDataException("Link no controle da análise: " + info.FullName);
            if (info is DirectoryInfo)
            {
                if (Nomes.Equals(info.Name, "__pycache__") || Nomes.Equals(info.Name, "edge-perfil") ||
                    topoDoControle && Nomes.Equals(info.Name, "historico")) continue;
                foreach (var filho in Percorrer(info.FullName)) yield return filho;
            }
            else if (!Nomes.Equals(info.Name, "desktop.ini") && !Nomes.Equals(info.Extension, ".lock") && !Nomes.Equals(info.Extension, ".tmp") &&
                     !Nomes.Equals(info.Name, "PUBLICACAO-PENDENTE.JSON") && !Nomes.Equals(info.Name, "TRANSPORTE-PENDENTE.JSON") &&
                     !Nomes.Equals(info.Name, "GERACAO-RECUPERACAO.LOCAL"))
                yield return info.FullName;
        }
    }

    static IEnumerable<string> Referencias(JsonElement valor)
    {
        switch (valor.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var campo in valor.EnumerateObject())
                    foreach (var item in Referencias(campo.Value)) yield return item;
                break;
            case JsonValueKind.Array:
                foreach (var filho in valor.EnumerateArray())
                    foreach (var item in Referencias(filho)) yield return item;
                break;
            case JsonValueKind.String:
                var caminho = valor.GetString()!.Replace('/', '\\');
                const string trecho = @".pmvni-ai\cache-pdf\";
                var i = caminho.IndexOf(trecho, StringComparison.OrdinalIgnoreCase);
                if (i == 0 || i > 0 && Path.IsPathFullyQualified(caminho) && caminho[i-1] == '\\' && !caminho.Any(char.IsControl))
                    yield return caminho[i..];
                break;
        }
    }

    static List<string> Inventariar(string caso, string id, string rota, bool legado)
    {
        var registro = Dentro(caso, Path.Combine(".pmvni-ai", "editor", id));
        if (!Directory.Exists(registro)) throw new DirectoryNotFoundException("Registro da execução ausente: " + id);
        var arquivos = new HashSet<string>(Percorrer(registro), Nomes);
        if (!arquivos.Contains(Dentro(registro, "EXECUCAO.JSON"))) throw new InvalidDataException("O registro não contém EXECUCAO.JSON.");
        foreach (var r in Rotas)
        {
            var controle = Dentro(caso, Path.Combine(".pmvni-ai", r));
            if (!Directory.Exists(controle)) continue;
            var estado = Dentro(controle, "engine_state.json");
            if (File.Exists(estado))
            {
                using var dados = JsonDocument.Parse(File.ReadAllBytes(estado));
                var identificada = dados.RootElement.TryGetProperty("editor_execution", out var exec);
                if (!identificada || !Nomes.Equals(exec.GetString(), id))
                {
                    if (r == rota) throw new InvalidDataException("Controle sem vínculo com a execução solicitada: " + r);
                    continue;
                }
            }
            else if (legado || r != rota)
            {
                if (r == rota && legado) throw new InvalidDataException("Estado do kit ausente para a retomada legada.");
                continue;
            }
            arquivos.UnionWith(Percorrer(controle, topoDoControle: true));
            var preparo = Dentro(controle, @"preparo\preparo.json");
            if (!File.Exists(preparo)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(preparo));
            foreach (var rel in Referencias(doc.RootElement).Distinct(Nomes))
            {
                var cache = Dentro(caso, rel);
                // Exists retorna false também para certas falhas de permissão. Evidência declarada precisa ser legível.
                if ((File.GetAttributes(cache) & FileAttributes.Directory) != 0) arquivos.UnionWith(Percorrer(cache));
                else arquivos.Add(cache);
            }
        }
        return arquivos.OrderBy(x => x, Nomes).ToList();
    }

    static Arquivo Medir(string caso, string caminho, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new FileInfo(caminho);
        var nome = Path.GetRelativePath(caso, caminho);
        using var f = File.OpenRead(Dentro(caso, nome));
        return new(nome, Digest(f), f.Length, info.LastWriteTimeUtc);
    }

    static bool RegistroAoVivo(string rel, string id)
    {
        var registro = Path.Combine(".pmvni-ai", "editor", id);
        var nome = Path.GetFileName(rel);
        return Nomes.Equals(Path.GetDirectoryName(rel), registro) &&
               new[] { "EXECUCAO.JSON", "SAIDA.JSONL", "SITUACOES.JSONL", "ERROS.TXT" }.Contains(nome, Nomes) ||
               Nomes.Equals(nome, "auto-log.txt") && Rotas.Any(r => Nomes.Equals(Path.GetDirectoryName(rel), Path.Combine(".pmvni-ai", r)));
    }

    // O cronômetro do Editor troca o registro a cada minuto. Os logs aceitam acréscimos enquanto
    // o controlador aguarda o callback. Capturar o tamanho do handle aberto preserva um instante
    // do acompanhamento sem bloquear o monitor durante a transferência de todas as evidências.
    sealed class CopiasDoMonitor(string caso, string id, CancellationToken ct) : IDisposable
    {
        readonly string nomePasta = ".checkpoint-" + Guid.NewGuid().ToString("N");
        readonly Dictionary<string, string> copias = new(Nomes);
        public string Obter(string rel)
        {
            var fonte = Dentro(caso, rel);
            if (!RegistroAoVivo(rel, id)) return fonte;
            if (copias.TryGetValue(rel, out var copia)) return copia;
            var pasta = Dentro(caso, nomePasta);
            Directory.CreateDirectory(pasta);
            copia = Dentro(pasta, copias.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var entrada = new FileStream(fonte, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var data = File.GetLastWriteTimeUtc(entrada.SafeFileHandle);
            var restante = entrada.Length;
            var buffer = new byte[128 * 1024];
            using (var saida = new FileStream(copia, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                while (restante > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var n = entrada.Read(buffer, 0, (int)Math.Min(buffer.Length, restante));
                    if (n == 0) throw new IOException("O registro foi truncado durante a captura: " + rel);
                    saida.Write(buffer, 0, n);
                    restante -= n;
                }
            }
            File.SetLastWriteTimeUtc(copia, data);
            copias.Add(rel, copia);
            return copia;
        }

        public void Dispose()
        {
            var pasta = Dentro(caso, nomePasta);
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        }
    }

    static void ConferirEstabilidade(string caso, string id, string rota, bool legado, List<Arquivo> arquivos, CancellationToken ct)
    {
        var atuais = Inventariar(caso, id, rota, legado)
            .Where(p => legado || !RegistroAoVivo(Path.GetRelativePath(caso, p), id)).ToList();
        var estaveis = legado ? arquivos : arquivos.Where(a => !RegistroAoVivo(a.Nome, id)).ToList();
        if (atuais.Count != estaveis.Count) throw new IOException("O conjunto de arquivos da execução mudou durante a cópia.");
        for (var i = 0; i < estaveis.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var atual = new FileInfo(atuais[i]);
            var esperado = estaveis[i];
            if (!Nomes.Equals(Path.GetRelativePath(caso, atual.FullName), esperado.Nome) ||
                atual.Length != esperado.Bytes || atual.LastWriteTimeUtc != esperado.Modificado)
                throw new IOException("O arquivo da execução mudou durante a cópia: " + esperado.Nome);
        }
    }

    static void Copiar(string origem, string destino, Arquivo esperado, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        using (var entrada = File.OpenRead(origem))
        using (var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            entrada.CopyTo(saida);
            saida.Flush(true);
        }
        if (new FileInfo(destino).Length != esperado.Bytes || Hash(destino) != esperado.Sha256)
            throw new IOException("O arquivo mudou ou a cópia divergiu: " + esperado.Nome);
        File.SetLastWriteTimeUtc(destino, esperado.Modificado);
    }

    static void InstalarArquivo(string destino, Action<string> gravar, bool substituir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var temporario = Path.Combine(Path.GetDirectoryName(destino)!, Guid.NewGuid().ToString("N") + ".tmp");
        try { gravar(temporario); Trocar(() => File.Move(temporario, destino, substituir)); }
        finally { if (File.Exists(temporario)) Trocar(() => File.Delete(temporario)); }
    }

    // O sincronizador/antivírus pode abrir um handle sem compartilhamento de exclusão entre a
    // gravação conferida e o rename. Repetir só a operação atômica, por prazo limitado.
    static void Trocar(Action acao, CancellationToken ct = default)
    {
        for (var i = 0; ; i++)
        {
            ct.ThrowIfCancellationRequested();
            try { acao(); return; }
            catch (Exception e) when (i < 15 && e is IOException or UnauthorizedAccessException && (e.HResult & 0xffff) is 5 or 32 or 33)
            { ct.WaitHandle.WaitOne(Math.Min(250, 25 * (i + 1))); }
        }
    }

    static FileStream Reservar(string pasta, CancellationToken ct)
    {
        var limite = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Dentro(pasta, "PUBLICACAO.LOCK"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33 && limite.Elapsed < TimeSpan.FromMinutes(2))
            { ct.WaitHandle.WaitOne(100); }
        }
    }

    static string Objeto(string pasta, string sha)
    {
        ConferirSha(sha);
        return Dentro(pasta, Path.Combine("objetos", sha[..2], sha));
    }

    static void ConferirSha(string sha)
    {
        if (sha == null || !Regex.IsMatch(sha, @"\A[0-9a-f]{64}\z")) throw new InvalidDataException("SHA-256 inválido no manifesto.");
    }

    static Ponteiro? LerPonteiro(string pasta)
    {
        var caminho = Dentro(pasta, "ATUAL.JSON");
        byte[] dados;
        try { dados = File.ReadAllBytes(caminho); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
        var p = JsonSerializer.Deserialize<Ponteiro>(dados) ?? throw new InvalidDataException("Ponteiro inválido.");
        ConferirSha(p.Atual);
        return p;
    }

    static string CaminhoMarcador(string caso, string id) => Dentro(caso, Path.Combine(".pmvni-ai", "editor", NomeExecucao(id), "GERACAO-RECUPERACAO.LOCAL"));

    static string? GeracaoLocal(string local, string id)
    {
        var caminho = CaminhoMarcador(local, id);
        try { return File.ReadAllText(caminho).Trim(); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    static void MarcarGeracao(string local, string id, string sha) =>
        InstalarArquivo(CaminhoMarcador(local, id), tmp => File.WriteAllText(tmp, sha), true);

    internal static bool GeracaoAtual(string origem, string local, string id) =>
        GeracaoLocal(local, id) == LerPonteiro(DiretorioCheckpoint(origem, id))?.Atual;

    [StructLayout(LayoutKind.Sequential)]
    struct InformacaoArquivo
    {
        public uint Atributos;
        public System.Runtime.InteropServices.ComTypes.FILETIME Criacao, Acesso, Modificacao;
        public uint Volume, TamanhoAlto, TamanhoBaixo, NumeroLinks, IndiceAlto, IndiceBaixo;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle arquivo, out InformacaoArquivo info);

    internal static bool MesmoProcesso(string origem, string antiga, string relativo)
    {
        var a = AreaAnaliseLocal.Identidade(origem);
        var b = AreaAnaliseLocal.Identidade(antiga);
        if (Nomes.Equals(a, b)) return true;
        if (!OperatingSystem.IsWindows()) return false;
        // Um endereço vindo do manifesto não pode provocar uma conexão com outro servidor.
        if (b.StartsWith(@"\\"))
        {
            if (!a.StartsWith(@"\\")) return false;
            var hostA = a[2..].Split('\\')[0];
            var hostB = b[2..].Split('\\')[0];
            if (!(Nomes.Equals(hostA, hostB) || hostA.StartsWith(hostB + ".", StringComparison.OrdinalIgnoreCase) ||
                hostB.StartsWith(hostA + ".", StringComparison.OrdinalIgnoreCase))) return false;
        }
        try
        {
            using var f = File.OpenHandle(Dentro(origem, relativo), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var g = File.OpenHandle(Dentro(antiga, relativo), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(f, out var x) && GetFileInformationByHandle(g, out var y) && x.Volume != 0 &&
                (x.IndiceAlto != 0 || x.IndiceBaixo != 0) && x.Volume == y.Volume && x.IndiceAlto == y.IndiceAlto && x.IndiceBaixo == y.IndiceBaixo;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    static Manifesto LerManifesto(string origem, string pasta, string id, string sha)
    {
        var dados = File.ReadAllBytes(Objeto(pasta, sha));
        if (Convert.ToHexStringLower(SHA256.HashData(dados)) != sha) throw new InvalidDataException("Manifesto do checkpoint divergiu.");
        var m = JsonSerializer.Deserialize<Manifesto>(dados) ?? throw new InvalidDataException("Checkpoint sem manifesto.");
        if (m.Versao != 1 || m.Execucao != id || !Rotas.Contains(m.Rota) ||
            !MesmoProcesso(origem, m.Processo, Path.Combine(".pmvni-ai", "editor", id, "EXECUCAO.JSON")) ||
            !Path.IsPathFullyQualified(m.RaizLocal) || m.Arquivos is null || m.Arquivos.Count == 0)
            throw new InvalidDataException("Checkpoint incompatível com o processo ou a execução.");
        return m;
    }

    internal static string? ControleDoCheckpoint(string origem, string id)
    {
        var pasta = DiretorioCheckpoint(origem, id);
        return LerPonteiro(pasta) is { } p ? LerManifesto(origem, pasta, id, p.Atual).Rota : null;
    }

    static void ConferirRaizes(string origem, string local)
    {
        if (Nomes.Equals(origem, local) || origem.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            local.StartsWith(origem + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("As pastas do processo e da área local precisam ser independentes.");
    }

    internal static void Salvar(string origem, string local, string execucao, string controleRota, CancellationToken ct = default)
    {
        origem = Raiz(origem);
        local = Raiz(local);
        ConferirRaizes(origem, local);
        var rota = NomeRota(controleRota);
        var pasta = DiretorioCheckpoint(origem, execucao);
        Directory.CreateDirectory(pasta);
        using var trava = Reservar(pasta, ct);
        var anterior = LerPonteiro(pasta)?.Atual;
        // Compare de novo sob exclusividade: duas estações podem ter aberto o mesmo ponto.
        var baseLocal = GeracaoLocal(local, execucao);
        using var monitor = new CopiasDoMonitor(local, execucao, ct);
        var arquivos = Inventariar(local, execucao, rota, false).Select(caminho =>
        {
            var rel = Path.GetRelativePath(local, caminho);
            return Medir(local, monitor.Obter(rel), ct) with { Nome = rel };
        }).ToList();
        var dados = JsonSerializer.SerializeToUtf8Bytes(new Manifesto(1, AreaAnaliseLocal.Identidade(origem), execucao, Environment.MachineName, local, rota, arquivos));
        var sha = Convert.ToHexStringLower(SHA256.HashData(dados));
        if (baseLocal != anterior && anterior != sha)
            throw new IOException("Outra estação já publicou um ponto diferente para esta análise. As duas cópias foram preservadas.");
        // Enumerar metadados por prefixo evita várias viagens SMB por página já copiada.
        foreach (var grupo in arquivos.DistinctBy(a => a.Sha256).GroupBy(a => a.Sha256[..2]))
        {
            var prefixo = Dentro(pasta, Path.Combine("objetos", grupo.Key));
            var dir = Directory.CreateDirectory(prefixo);
            var existentes = dir.EnumerateFiles().ToDictionary(f => f.Name, Nomes);
            foreach (var a in grupo)
            {
                ct.ThrowIfCancellationRequested();
                if (existentes.TryGetValue(a.Sha256, out var blob))
                {
                    if (blob.Length != a.Bytes || blob.Attributes.HasFlag(FileAttributes.ReparsePoint) && blob.LinkTarget != null)
                        throw new InvalidDataException("Objeto corrompido no checkpoint: " + a.Nome);
                }
                else InstalarArquivo(Path.Combine(prefixo, a.Sha256), tmp => Copiar(monitor.Obter(a.Nome), tmp, a, ct), false);
            }
        }
        ConferirEstabilidade(local, execucao, rota, false, arquivos, ct);
        var objeto = Objeto(pasta, sha);
        if (!File.Exists(objeto)) InstalarArquivo(objeto, tmp => File.WriteAllBytes(tmp, dados), false);
        if (Hash(objeto) != sha) throw new InvalidDataException("Manifesto do checkpoint corrompido.");
        var ponteiro = Dentro(pasta, "ATUAL.JSON");
        if (anterior == sha) { MarcarGeracao(local, execucao, sha); return; }
        ct.ThrowIfCancellationRequested();
        var novo = JsonSerializer.SerializeToUtf8Bytes(new Ponteiro(sha, anterior));
        InstalarArquivo(ponteiro, tmp => File.WriteAllBytes(tmp, novo), true);
        MarcarGeracao(local, execucao, sha);
    }

    internal static string? Restaurar(string origem, string local, string execucao, string controleRota, CancellationToken ct = default)
    {
        origem = Raiz(origem);
        local = Raiz(local);
        ConferirRaizes(origem, local);
        var rota = NomeRota(controleRota);
        var pasta = DiretorioCheckpoint(origem, execucao);
        if (LerPonteiro(pasta) is not { } ids) return null;
        var m = LerManifesto(origem, pasta, execucao, ids.Atual);
        if (m.Rota != rota) throw new InvalidDataException("Checkpoint incompatível com a rota de origem.");
        InstalarControle(local, m.Arquivos, a => Objeto(pasta, a.Sha256), execucao, ct);
        MarcarGeracao(local, execucao, ids.Atual);
        return m.RaizLocal;
    }

    internal static string TransportarDaEstacao(string antiga, string provisoria, string id, string rota, CancellationToken ct)
    {
        var geracao = GeracaoLocal(antiga, id);
        var arquivos = Inventariar(antiga, id, rota, false).Select(p => Medir(antiga, p, ct)).ToList();
        InstalarControle(provisoria, arquivos, a => Dentro(antiga, a.Nome), id, ct,
            () => ConferirEstabilidade(antiga, id, rota, false, arquivos, ct));
        if (geracao != GeracaoLocal(antiga, id)) throw new IOException("A execução anterior mudou durante o transporte.");
        if (geracao is not null) MarcarGeracao(provisoria, id, geracao);
        return antiga;
    }

    internal static string RestaurarLegado(string origem, string local, string execucao, string controleRota, CancellationToken ct = default)
    {
        origem = Raiz(origem);
        local = Raiz(local);
        ConferirRaizes(origem, local);
        var id = NomeExecucao(execucao);
        var rota = NomeRota(controleRota);
        // O executor legado trava o primeiro byte com msvcrt. Abrir somente a trava já existente mantém o legado intacto.
        var caminhoTrava = Dentro(origem, Path.Combine(".pmvni-ai", "editor", id, "EXECUTOR.LOCK"));
        using var executor = new FileStream(caminhoTrava, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        try { executor.Lock(0, 1); }
        catch (IOException ex) { throw new IOException("O executor da análise está ativo ou a estação não obteve a trava do servidor.", ex); }
        var controle = Dentro(origem, Path.Combine(".pmvni-ai", rota, "engine_state.json"));
        using var estado = JsonDocument.Parse(File.ReadAllBytes(controle));
        var anterior = Campo(estado.RootElement, "case");
        if (!Path.IsPathFullyQualified(anterior)) throw new InvalidDataException("Raiz antiga ilegível.");
        if (!MesmoProcesso(origem, anterior, Path.Combine(".pmvni-ai", rota, "engine_state.json")))
            throw new InvalidDataException("A pasta da execução legada não corresponde à origem nesta estação. Confira o compartilhamento ou retome na estação de origem.");
        var arquivos = Inventariar(origem, id, rota, true).Select(c => Medir(origem, c, ct)).ToList();
        InstalarControle(local, arquivos, a => Dentro(origem, a.Nome), id, ct,
            () => ConferirEstabilidade(origem, id, rota, true, arquivos, ct));
        return anterior;
    }

    static JsonElement Requerido(JsonElement obj, string nome, JsonValueKind tipo) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(nome, out var v) && v.ValueKind == tipo
            ? v : throw new InvalidDataException("Campo obrigatório ausente ou inválido: " + nome);

    static string Campo(JsonElement obj, string nome) => Requerido(obj, nome, JsonValueKind.String).GetString() is { Length: > 0 } valor
        ? valor : throw new InvalidDataException("Campo obrigatório vazio: " + nome);

    static string Declarado(string raiz, string nome)
    {
        var caminho = Path.GetFullPath(nome, raiz);
        return Dentro(raiz, Path.GetRelativePath(raiz, caminho));
    }

    static List<(string Relativo, string Caminho)> Imagens(string local, string documento)
    {
        var lista = new List<(string, string)>();
        if (!Nomes.Equals(Path.GetExtension(documento), ".json")) return lista;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(documento));
        void Incluir(JsonElement obj)
        {
            if (!obj.TryGetProperty("imagem", out var imagem) || imagem.ValueKind == JsonValueKind.Null) return;
            var rel = imagem.GetString()!;
            if (Pmvni.Composicao.CaminhoImagem.Resolver(Path.GetDirectoryName(documento)!, rel, false) is not { } caminho ||
                !File.Exists(caminho)) throw new InvalidDataException("Imagem da entrega indisponível: " + rel);
            if (!new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" }.Contains(Path.GetExtension(caminho), Nomes))
                throw new InvalidDataException("Formato inválido de imagem declarada: " + rel);
            lista.Add((rel, Declarado(local, caminho)));
        }
        if (doc.RootElement.TryGetProperty("localizacao", out var localizacao) && localizacao.ValueKind == JsonValueKind.Object) Incluir(localizacao);
        if (doc.RootElement.TryGetProperty("figuras", out var figuras))
            foreach (var f in figuras.EnumerateArray()) Incluir(f);
        return lista;
    }

    static string ConferirEntregaDoKit(string local, string controle, string id, string documento, string? pdf, out string? pendente,
        string? shaDocumentoCopiado = null, string? shaPdfCopiado = null)
    {
        var estado = Dentro(controle, "engine_state.json");
        var declaracao = Dentro(controle, "delivery.json");
        using var json = JsonDocument.Parse(File.ReadAllBytes(estado));
        using var entrega = JsonDocument.Parse(File.ReadAllBytes(declaracao));
        var e = json.RootElement;
        var d = entrega.RootElement;
        if (Campo(e, "editor_execution") != id || !Nomes.Equals(Raiz(Campo(e, "case")), local) ||
            !Nomes.Equals(Declarado(local, Campo(d, "document")), documento)) throw new InvalidDataException("A entrega pertence a outra execução.");
        pendente = null;
        if (d.TryGetProperty("modo", out var modo) && modo.GetString() == "para_revisao")
        {
            pendente = Campo(d, "etapa_pendente");
            var pastaRevisao = Path.GetDirectoryName(documento)!;
            if (!Nomes.Equals(Path.GetDirectoryName(pastaRevisao), Dentro(controle, "para-revisao"))
                || !Guid.TryParseExact(Path.GetFileName(pastaRevisao), "N", out _)
                || !Nomes.Equals(Path.GetFileName(documento), "VERSAO-PARA-REVISAO.JSON")
                || Campo(d, "execucao") != id || Hash(documento) != Campo(d, "document_sha256"))
                throw new InvalidDataException("A versão para revisão diverge do registro desta execução.");
            using var arquivo = JsonDocument.Parse(File.ReadAllBytes(documento));
            var revisao = Requerido(arquivo.RootElement, "revisao", JsonValueKind.Object);
            if (Campo(revisao, "execucao") != id || Campo(revisao, "etapa") != pendente
                || Campo(revisao, "motivo") != Campo(d, "motivo"))
                throw new InvalidDataException("Os dados para revisão não identificam a pendência da execução.");
            if (d.TryGetProperty("pdf", out _) != (pdf is not null) || pdf is not null &&
                (!Nomes.Equals(pdf, Dentro(pastaRevisao, "VERSAO-PARA-REVISAO.PDF"))
                || !Nomes.Equals(pdf, Declarado(local, Campo(d, "pdf"))) || Hash(pdf) != Campo(d, "pdf_sha256")))
                throw new InvalidDataException("O PDF para revisão diverge dos dados de entrega.");
            return "para_revisao";
        }
        if (d.TryGetProperty("modo", out modo) && modo.GetString() == "edicao_manual")
        {
            var etapa = Campo(d, "etapa_pendente");
            if (pdf != null || d.TryGetProperty("pdf", out _) || !Nomes.Equals(documento, Dentro(controle, @"edicao-manual\RELATORIO_DADOS.JSON")) ||
                Campo(e, "decisao_cota") != "manual" || !Requerido(e, "decisoes", JsonValueKind.Array).EnumerateArray().Any(dec =>
                    dec.TryGetProperty("tipo", out var tipo) && tipo.GetString() == "limite_cota" &&
                    Campo(dec, "etapa") == etapa && Campo(dec, "resposta") == "manual"))
                throw new InvalidDataException("O kit não registrou a escolha da edição manual nesta etapa.");
            pendente = etapa;
            return "edicao_manual";
        }
        var gate = Requerido(e, "gate", JsonValueKind.Object);
        if (pdf == null || !Nomes.Equals(Path.GetExtension(pdf), ".pdf") || !Nomes.Equals(Declarado(local, Campo(d, "pdf")), pdf) ||
            Campo(gate, "validators") != "ALL_PASSED" || !Nomes.Equals(Declarado(local, Campo(gate, "pdf")), pdf))
            throw new InvalidDataException("A entrega ainda não passou pelos validadores finais do kit.");
        var etapas = Requerido(e, "stages", JsonValueKind.Object);
        var producao = Requerido(etapas, "produce", JsonValueKind.Object);
        var certs = Requerido(producao, "evidence", JsonValueKind.Array);
        if (certs.GetArrayLength() != 1) throw new InvalidDataException("Certificado da montagem ausente.");
        var montagem = Declarado(local, Campo(certs[0], "path"));
        var pasta = Path.GetDirectoryName(montagem);
        if (!Nomes.Equals(pasta, Path.GetDirectoryName(documento)) || !Nomes.Equals(pasta, Path.GetDirectoryName(pdf)) ||
            Hash(montagem) != Campo(certs[0], "sha256")) throw new InvalidDataException("Certificado da montagem divergente.");
        using var cert = JsonDocument.Parse(File.ReadAllBytes(montagem));
        var shaPdf = shaPdfCopiado ?? Hash(pdf);
        if (shaPdf != Campo(gate, "pdf_sha256") || shaPdf != Campo(cert.RootElement, "pdf_sha256") ||
            (shaDocumentoCopiado ?? Hash(documento)) != Campo(cert.RootElement, "document_sha256") || Hash(declaracao) != Campo(cert.RootElement, "delivery_sha256"))
            throw new InvalidDataException("Os arquivos divergem dos hashes da montagem aprovada.");
        return "validada";
    }

    static string IndiceEntrega(string origem, string id) => Dentro(origem, Path.Combine(".pmvni-ai", "entregas", NomeExecucao(id)));

    internal static FimAnalise Publicar(string origem, string local, string id, string controleRota, FimAnalise fim,
        Action conferirOriginais, CancellationToken ct = default)
    {
        origem = Raiz(origem);
        local = Raiz(local);
        ConferirRaizes(origem, local);
        var rota = NomeRota(controleRota);
        var controle = Dentro(local, Path.Combine(".pmvni-ai", rota));
        var documento = Declarado(local, fim.Documento ?? throw new InvalidDataException("Documento não declarado."));
        var pdf = fim.Pdf is null ? null : Declarado(local, fim.Pdf);
        var modo = ConferirEntregaDoKit(local, controle, id, documento, pdf, out var etapa);
        var imagens = Imagens(local, documento);
        // Preserva byte a byte o JSON certificado. Uma figura citada em pasta irmã precisa da árvore original.
        var espelhar = imagens.Any(i => i.Relativo.Split('/', '\\').Contains(".."));
        var nomeDoc = espelhar ? Path.GetRelativePath(local, documento) : Path.GetFileName(documento);
        var mapa = new Dictionary<string, string>(Nomes) { [nomeDoc] = documento };
        foreach (var (rel, caminho) in imagens)
        {
            var nome = espelhar ? Path.GetRelativePath(local, caminho) : Path.GetRelativePath(Path.GetDirectoryName(documento)!, caminho);
            mapa[nome] = caminho;
        }
        var nomePdf = pdf is null ? null : Path.GetFileName(pdf);
        if (nomePdf != null) mapa.Add(nomePdf, pdf!);
        var arquivos = mapa.OrderBy(p => p.Key, Nomes).Select(p => Medir(local, p.Value, ct) with { Nome = p.Key }).ToList();
        var manifesto = new Entrega(1, AreaAnaliseLocal.Identidade(origem), id, rota, modo, etapa, nomeDoc, nomePdf, arquivos);
        var dados = JsonSerializer.SerializeToUtf8Bytes(manifesto);
        var sha = Convert.ToHexStringLower(SHA256.HashData(dados));
        var indice = IndiceEntrega(origem, id);
        Directory.CreateDirectory(indice);
        using var trava = Reservar(indice, ct);
        var pasta = Dentro(origem, Path.Combine(Produtos, NomeExecucao(id)));
        var destino = Dentro(pasta, sha);
        var shaDoc = arquivos.Single(a => Nomes.Equals(a.Nome, nomeDoc)).Sha256;
        var shaPdf = arquivos.SingleOrDefault(a => Nomes.Equals(a.Nome, nomePdf))?.Sha256;
        void ConferirPublicacao()
        {
            if (ConferirEntregaDoKit(local, controle, id, documento, pdf, out var etapaAtual, shaDoc, shaPdf) != modo || etapaAtual != etapa ||
                (modo == "edicao_manual" && Hash(documento) != shaDoc))
                throw new InvalidDataException("A entrega foi modificada durante a publicação.");
            conferirOriginais();
            ct.ThrowIfCancellationRequested();
        }
        var caminhoManifesto = Dentro(indice, sha + ".json");
        if (!File.Exists(caminhoManifesto)) InstalarArquivo(caminhoManifesto, tmp => File.WriteAllBytes(tmp, dados), false);
        if (Hash(caminhoManifesto) != sha) throw new InvalidDataException("Manifesto de entrega divergente.");
        Resultado resultado;
        if (!Directory.Exists(destino))
        {
            var temporario = Dentro(indice, "produto-" + Guid.NewGuid().ToString("N") + ".tmp");
            Directory.CreateDirectory(temporario);
            try
            {
                foreach (var a in arquivos)
                {
                    var caminho = Dentro(temporario, a.Nome);
                    try { Copiar(mapa[a.Nome], caminho, a, ct); }
                    catch (IOException) when (!ct.IsCancellationRequested && Hash(mapa[a.Nome]) == a.Sha256)
                    {
                        if (File.Exists(caminho)) File.Delete(caminho);
                        Copiar(mapa[a.Nome], caminho, a, ct);
                    }
                }
                ConferirPublicacao();
                Directory.CreateDirectory(pasta);
                Trocar(() => Directory.Move(temporario, destino), ct);
                resultado = new(Dentro(destino, nomeDoc), nomePdf == null ? null : Dentro(destino, nomePdf), null);
            }
            finally { if (Directory.Exists(temporario)) Trocar(() => Directory.Delete(Dentro(indice, Path.GetFileName(temporario)), true)); }
        }
        else
        {
            resultado = ValidarProduto(origem, id, sha, manifesto);
            ConferirPublicacao();
        }
        var anterior = LerPonteiro(indice)?.Atual;
        if (anterior != sha)
            InstalarArquivo(Dentro(indice, "ATUAL.JSON"), tmp => File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(new Ponteiro(sha, anterior))), true);
        return fim with { Documento = resultado.Documento, Pdf = resultado.Pdf,
            Situacao = modo == "para_revisao" ? "para_revisao" : fim.Situacao,
            Mensagem = modo == "para_revisao" ? "Versão para revisão. Etapa pendente: " + etapa : fim.Mensagem };
    }

    static Resultado ValidarProduto(string origem, string id, string sha, Entrega entrega)
    {
        if (entrega.Versao != 1 || entrega.Execucao != id ||
            !MesmoProcesso(origem, entrega.Processo, Path.Combine(".pmvni-ai", "entregas", id, "ATUAL.JSON")) ||
            !Rotas.Contains(entrega.Controle) || entrega.Modo is not ("validada" or "edicao_manual" or "para_revisao") ||
            (entrega.Modo != "para_revisao" && (entrega.Pdf is null) != (entrega.Modo == "edicao_manual")))
            throw new InvalidDataException("A publicação não pertence a esta execução.");
        var pasta = Dentro(origem, Path.Combine(Produtos, id, sha));
        var nomes = new HashSet<string>(Nomes);
        foreach (var a in entrega.Arquivos)
        {
            var caminho = Dentro(pasta, a.Nome);
            if (!nomes.Add(a.Nome) || new FileInfo(caminho).Length != a.Bytes || Hash(caminho) != a.Sha256)
                throw new InvalidDataException("Divergência no arquivo publicado: " + a.Nome);
        }
        if (!nomes.Contains(entrega.Documento) || entrega.Pdf != null && !nomes.Contains(entrega.Pdf))
            throw new InvalidDataException("O manifesto não identifica os arquivos publicados.");
        return new(Dentro(pasta, entrega.Documento), entrega.Pdf is null ? null : Dentro(pasta, entrega.Pdf), null);
    }

    internal static Resultado? EntregaPublicada(string origem, string id)
    {
        origem = Raiz(origem);
        var indice = IndiceEntrega(origem, id);
        if (LerPonteiro(indice) is not { } p) return null;
        var arquivo = Dentro(indice, p.Atual + ".json");
        var dados = File.ReadAllBytes(arquivo);
        if (Convert.ToHexStringLower(SHA256.HashData(dados)) != p.Atual) throw new InvalidDataException("Manifesto de entrega corrompido.");
        var entrega = JsonSerializer.Deserialize<Entrega>(dados) ?? throw new InvalidDataException("Manifesto de entrega vazio.");
        return ValidarProduto(origem, id, p.Atual, entrega);
    }

    static void InstalarControle(string local, List<Arquivo> arquivos, Func<Arquivo, string> obter, string id, CancellationToken ct,
        Action? conferirAntesDaTroca = null)
    {
        var destino = Dentro(local, ".pmvni-ai");
        if (Directory.Exists(destino)) throw new IOException("A área já contém um controle local. A recuperação exige uma área vazia.");
        var preparacao = Dentro(local, ".recuperacao-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(preparacao);
        try
        {
            foreach (var a in arquivos)
            {
                var rel = a.Nome.Replace('/', '\\');
                var partes = rel.Split('\\');
                if (partes.Length < 3 || !Nomes.Equals(partes[0], ".pmvni-ai") ||
                    !(Rotas.Contains(partes[1], Nomes) || Nomes.Equals(partes[1], "cache-pdf") ||
                    partes.Length >= 4 && Nomes.Equals(partes[1], "editor") && Nomes.Equals(partes[2], id)))
                    throw new InvalidDataException("Arquivo fora dos controles desta execução: " + a.Nome);
                Copiar(obter(a), Dentro(preparacao, rel), a, ct);
            }
            ct.ThrowIfCancellationRequested();
            conferirAntesDaTroca?.Invoke();
            Trocar(() => Directory.Move(Path.Combine(preparacao, ".pmvni-ai"), destino), ct);
        }
        finally { Trocar(() => Directory.Delete(Dentro(local, Path.GetFileName(preparacao)), true)); }
    }
}
