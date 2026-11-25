using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Uma execução guarda na estação os seus próprios documentos e controles do kit.</summary>
internal sealed class AreaAnaliseLocal
{
    const string NomeInventario = "INVENTARIO-ORIGINAIS.JSON";
    const string NomeTransporte = "TRANSPORTE-PENDENTE.JSON";
    internal const string Marcador = "area_local_versao";
    internal const string ChaveIdentificador = "area_local_id";
    static readonly StringComparer Comparador = StringComparer.OrdinalIgnoreCase;
    sealed record Inventario(int Versao, string Processo, string Execucao, string Identificador, Dictionary<string, string> Arquivos);
    sealed record Transporte(string Anterior, string Controle);
    readonly string origem;
    readonly Dictionary<string, string> arquivos;
    public string Identificador { get; }
    public string ProcessoLocal { get; }
    public string RegistroLocal { get; }
    public string? LocalAnterior { get; private set; }
    public string? ControleAnterior { get; private set; }
    public bool RetomadaLegada { get; }
    public bool InicioPendente { get; private set; }

    AreaAnaliseLocal(string origem, string processo, string id, Inventario inventario, bool legada)
    {
        this.origem = origem;
        Identificador = inventario.Identificador;
        arquivos = new(inventario.Arquivos, Comparador);
        ProcessoLocal = processo;
        RegistroLocal = Path.Combine(processo, ".pmvni-ai", "editor", id);
        RetomadaLegada = legada;
    }

    internal static bool UsaAreaLocal(string registro) => IdPersistido(registro, out _) is not null;

    static string? IdPersistido(string registro, out bool solicitada)
    {
        solicitada = false;
        try
        {
            using var d = JsonDocument.Parse(RegistroExecucao.LerTextoDaExecucao(registro));
            solicitada = d.RootElement.TryGetProperty("area_local_solicitada", out var s) && s.ValueKind == JsonValueKind.True;
            if (!d.RootElement.TryGetProperty(Marcador, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n == 1 &&
                d.RootElement.TryGetProperty(ChaveIdentificador, out var identificador) && identificador.ValueKind == JsonValueKind.String &&
                Guid.TryParseExact(identificador.GetString(), "N", out var id)) return id.ToString("N");
            throw new InvalidDataException("Identificação da execução local ausente ou inválida no registro do processo.");
        }
        catch (FileNotFoundException) { return null; }
    }

    internal static string? LocalExistente(string origem, string registro, string? documentosLocais = null, bool consultarLegado = false)
    {
        ValidarRegistro(origem, registro);
        var persistido = IdPersistido(registro, out _);
        if (persistido is null) return null;
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(registro));
        var area = CaminhoLocal(origem, id, documentosLocais);
        if (consultarLegado && documentosLocais is null && !Directory.Exists(area))
            area = CaminhoLocal(origem, id, DocumentosLegados());
        return ComInventario(origem, area, id, persistido);
    }

    static string? ComInventario(string origem, string area, string id, string persistido)
    {
        ConferirCaminho(area, local: true);
        var arquivo = Path.Combine(area, ".pmvni-ai", "editor", id, NomeInventario);
        ConferirCaminho(arquivo, local: true);
        if (!File.Exists(arquivo)) return null;
        var registroLocal = Path.GetDirectoryName(arquivo)!;
        if (File.Exists(Path.Combine(registroLocal, NomeTransporte))) return null;
        var inventario = LerInventario(origem, registroLocal, id);
        return Comparador.Equals(inventario.Identificador, persistido) ? area : null;
    }

    internal static AreaAnaliseLocal AbrirAtiva(string origem, string registro, string? documentosLocais = null)
    {
        ValidarRegistro(origem, registro);
        var persistido = IdPersistido(registro, out _) ?? throw new IOException("A execução não tem área local identificada.");
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(registro));
        var local = LocalExistente(origem, registro, documentosLocais) ?? throw new IOException("Área local ativa indisponível.");
        var area = Abrir(origem, local, id, persistido, false);
        if (area.LocalAnterior is not null) throw new IOException("O transporte da execução ainda não foi confirmado.");
        return area;
    }

    internal static AreaAnaliseLocal Preparar(PedidoExecucao pedido, IReadOnlyList<string> entradas, CancellationToken ct, string? documentosLocais = null, string? documentosAnteriores = null)
    {
        var origem = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pedido.Processo.Pasta));
        var registro = pedido.Registro.Pasta;
        ValidarRegistro(origem, registro);
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(registro));
        ct.ThrowIfCancellationRequested();
        var idOrigem = IdPersistido(registro, out var solicitada);
        var inicioPendente = pedido.Retomar && idOrigem is null && solicitada && !TemEtapa(origem, id);
        var legada = pedido.Retomar && idOrigem is null && !inicioPendente;
        var local = CaminhoLocal(origem, id, documentosLocais);
        if (Dentro(origem, local) || Dentro(local, origem)) throw new IOException("A área local precisa ficar fora da pasta original do processo.");
        ConferirCaminho(origem, local: false);
        ConferirCaminho(local, local: true);
        var diretorios = new List<string>();
        var lista = Listar(origem, ct, diretorios);
        foreach (var entrada in entradas.Concat(pedido.Anexos.Select(a => Path.Combine(origem, a.Relativo))))
        {
            var rel = Path.GetRelativePath(origem, Path.GetFullPath(entrada));
            if (!lista.Contains(rel)) throw new IOException("Peça selecionada ausente ou fora dos originais do processo: " + entrada);
        }
        var existente = Directory.Exists(local);
        if (existente && inicioPendente && TemEtapa(local, id))
            throw new IOException("Há uma etapa na área local e o registro da origem perdeu o vínculo. As duas cópias foram preservadas.");
        var transportePendente = false;
        if (existente && idOrigem is not null)
        {
            var area = Abrir(origem, local, id, idOrigem, legada);
            transportePendente = area.LocalAnterior is not null;
            if (area.LocalAnterior is null && RecuperacaoAnaliseLocal.GeracaoAtual(origem, local, id))
            {
                area.ConferirOriginais(ct);
                ConferirAnexos(pedido, area.arquivos);
                return area;
            }
        }
        var antiga = pedido.Retomar && (!existente || transportePendente) && idOrigem is not null && (documentosLocais is null || documentosAnteriores is not null)
            ? ComInventario(origem, CaminhoLocal(origem, id, documentosAnteriores ?? DocumentosLegados()), id, idOrigem) : null;
        if (antiga is not null && !RecuperacaoAnaliseLocal.GeracaoAtual(origem, antiga, id)) antiga = null;
        var restaurar = idOrigem is not null && RecuperacaoAnaliseLocal.Existe(origem, id);
        if (idOrigem is not null && !restaurar && antiga is null)
            throw new IOException("Esta execução local ainda não tem ponto de recuperação no servidor. Retome na estação de origem.");
        var pai = Directory.CreateDirectory(Path.GetDirectoryName(local)!).FullName;
        var provisoria = Path.Combine(pai, ".preparo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(provisoria);
        try
        {
            var hashes = new Dictionary<string, string>(Comparador);
            foreach (var rel in diretorios)
                Directory.CreateDirectory(Path.Combine(provisoria, rel));
            foreach (var rel in lista)
                hashes.Add(rel, CopiarArquivo(origem, provisoria, rel, ct));
            ConferirAnexos(pedido, hashes);
            var reg = Path.Combine(provisoria, ".pmvni-ai", "editor", id);
            if (restaurar || legada || antiga is not null)
            {
                var controle = legada || antiga is not null ? ControleDoLegado(registro, id) : RecuperacaoAnaliseLocal.ControleDoCheckpoint(origem, id)
                    ?? throw new IOException("A rota do ponto de recuperação não está disponível no servidor.");
                var anterior = antiga is not null
                    ? RecuperacaoAnaliseLocal.TransportarDaEstacao(antiga, provisoria, id, controle, ct)
                    : legada
                    ? RecuperacaoAnaliseLocal.RestaurarLegado(origem, provisoria, id, controle, ct)
                    : RecuperacaoAnaliseLocal.Restaurar(origem, provisoria, id, controle, ct)
                        ?? throw new IOException("O ponto de recuperação já não está disponível no servidor.");
                if (antiga is not null && !RecuperacaoAnaliseLocal.GeracaoAtual(origem, provisoria, id))
                    throw new IOException("O checkpoint mudou durante o transporte. As cópias foram preservadas para nova retomada.");
                if (legada)
                    Gravar(Path.Combine(reg, NomeInventario), new Inventario(1, Identidade(origem), id, Guid.NewGuid().ToString("N"), hashes));
                else
                {
                    var inventario = LerInventario(origem, reg, id);
                    var esperados = new Dictionary<string, string>(inventario.Arquivos, Comparador);
                    if (!Comparador.Equals(idOrigem, inventario.Identificador) || hashes.Count != esperados.Count ||
                        hashes.Any(i => !esperados.TryGetValue(i.Key, out var h) || !Comparador.Equals(h, i.Value)))
                        throw new IOException("Os originais ou o identificador divergem do inventário desta execução. O ponto de recuperação foi preservado.");
                }
                if (legada && Comparador.Equals(Path.GetFullPath(anterior), local))
                    throw new IOException("A raiz declarada pelo controle legado coincide com o destino da cópia.");
                // Duas estações com o mesmo perfil podem usar caminhos locais idênticos; o kit valida também esse caso.
                Gravar(Path.Combine(reg, NomeTransporte), new Transporte(anterior, controle));
            }
            else
            {
                Directory.CreateDirectory(reg);
                foreach (var nome in new[] { "EXECUCAO.JSON", "PEDIDO.MD", "ANEXOS.JSON", TabelaDeModelos.NomeDoArquivo })
                    if (File.Exists(Path.Combine(registro, nome))) CopiarArquivo(registro, reg, nome, ct);
                Gravar(Path.Combine(reg, NomeInventario), new Inventario(1, Identidade(origem), id, Guid.NewGuid().ToString("N"), hashes));
            }
            // Valida a cópia lida do disco e detecta inclusive troca de conteúdo da origem durante o preparo.
            var preparada = Abrir(origem, provisoria, id, idOrigem, legada);
            preparada.ConferirOriginais(ct);
            ct.ThrowIfCancellationRequested();
            InstalarCopia(provisoria, local, existente);
        }
        finally
        {
            if (Directory.Exists(provisoria))
            {
                ConferirCaminho(provisoria, local: true);
                Directory.Delete(provisoria, true);
            }
        }
        var instalada = Abrir(origem, local, id, idOrigem, legada);
        instalada.InicioPendente = inicioPendente;
        return instalada;
    }

    static string ControleDoLegado(string registro, string id)
    {
        using var doc = JsonDocument.Parse(RegistroExecucao.LerTextoDaExecucao(registro));
        var nome = doc.RootElement.TryGetProperty("provedor", out var p) ? p.GetString() : id.Split('-')[^1].Split('_')[0];
        foreach (var provedor in Enum.GetValues<Provedor>())
            if (Comparador.Equals(Provedores.Sufixo(provedor), nome)) return Path.GetFileName(ResultadoAnalise.PastaControle(provedor));
        throw new InvalidDataException("O provedor original da análise não foi identificado no registro.");
    }

    static bool TemEtapa(string processo, string id)
    {
        if (File.Exists(Path.Combine(processo, ".pmvni-ai", "editor", id, "continuidade.json"))) return true;
        foreach (var provedor in Enum.GetValues<Provedor>())
        {
            var estado = Path.Combine(processo, ResultadoAnalise.PastaControle(provedor), "engine_state.json");
            ConferirCaminho(estado, local: false);
            if (!File.Exists(estado)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(estado));
            if (!doc.RootElement.TryGetProperty("editor_execution", out var execucao) || Comparador.Equals(execucao.GetString(), id)) return true;
        }
        return false;
    }

    static void ConferirAnexos(PedidoExecucao pedido, IReadOnlyDictionary<string, string> hashes)
    {
        foreach (var anexo in pedido.Anexos)
        {
            var arquivo = Path.GetFullPath(Path.Combine(pedido.Processo.Pasta, anexo.Relativo));
            var rel = Path.GetRelativePath(pedido.Processo.Pasta, arquivo);
            if (!hashes.TryGetValue(rel, out var hash) || !Comparador.Equals(hash, anexo.Sha256) || new FileInfo(arquivo).Length != anexo.Tamanho)
                throw new IOException("O anexo arquivado diverge do pedido da análise: " + rel);
        }
    }

    static void InstalarCopia(string provisoria, string local, bool existente)
    {
        if (!existente) { Directory.Move(provisoria, local); return; }
        // Uma falha de transporte ou um checkpoint de outra estação pode deixar trabalho local único.
        // Primeiro prepara a substituta inteira. A área anterior continua acessível ao lado do caso.
        var anterior = Path.Combine(Path.GetDirectoryName(local)!, "reserva-" + Guid.NewGuid().ToString("N"));
        ConferirCaminho(local, local: true);
        Directory.Move(local, anterior);
        try { Directory.Move(provisoria, local); }
        catch { Directory.Move(anterior, local); throw; }
    }

    static AreaAnaliseLocal Abrir(string origem, string local, string id, string? idOrigem, bool legada)
    {
        var registro = Path.Combine(local, ".pmvni-ai", "editor", id);
        var inventario = LerInventario(origem, registro, id);
        if (idOrigem is not null && !Comparador.Equals(idOrigem, inventario.Identificador))
            throw new IOException("A área local pertence a uma execução diferente da identificada na origem.");
        var area = new AreaAnaliseLocal(origem, local, id, inventario, legada);
        var pendente = Path.Combine(registro, NomeTransporte);
        ConferirCaminho(pendente, local: true);
        if (File.Exists(pendente))
        {
            var transporte = JsonSerializer.Deserialize<Transporte>(File.ReadAllBytes(pendente));
            if (transporte is null || string.IsNullOrEmpty(transporte.Anterior) || !Path.IsPathFullyQualified(transporte.Anterior) ||
                !Enum.GetValues<Provedor>().Any(p => Comparador.Equals(transporte.Controle, Path.GetFileName(ResultadoAnalise.PastaControle(p)))))
                throw new InvalidDataException("Registro da origem do ponto de recuperação inválido.");
            area.LocalAnterior = transporte.Anterior;
            area.ControleAnterior = transporte.Controle;
        }
        return area;
    }

    internal void ConfirmarTransporte()
    {
        if (LocalAnterior is null) return;
        var caminho = Path.Combine(RegistroLocal, NomeTransporte);
        ConferirCaminho(caminho, local: true);
        File.Delete(caminho);
        LocalAnterior = null;
        ControleAnterior = null;
    }

    internal void ConferirOriginais(CancellationToken ct = default, Func<string, Stream>? abrirCopia = null)
    {
        abrirCopia ??= File.OpenRead;
        if (!Listar(origem, ct).SetEquals(arquivos.Keys))
            throw new IOException("A lista de arquivos originais do processo mudou desde o início da análise.");
        foreach (var (rel, esperado) in arquivos)
        {
            var original = Path.Combine(origem, rel);
            var copia = Path.Combine(ProcessoLocal, rel);
            ConferirCaminho(original, local: false);
            ConferirCaminho(copia, local: true);
            using var fonte = File.OpenRead(original);
            ConferirHash(fonte, original);
            try
            {
                using var leitura = abrirCopia(copia);
                ConferirHash(leitura, copia);
            }
            catch (UnauthorizedAccessException)
            {
                // A via local pode ter herdado uma ACL antiga. O novo arquivo nasce na pasta da execução.
                var temporario = Path.Combine(Path.GetDirectoryName(copia)!, Guid.NewGuid().ToString("N") + ".tmp");
                fonte.Position = 0;
                try
                {
                    using (var escrita = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (!Comparador.Equals(Transferir(fonte, escrita, ct), esperado))
                            throw new IOException("A origem mudou durante a recuperação: " + original);
                        escrita.Flush(true);
                    }
                    File.SetLastWriteTimeUtc(temporario, File.GetLastWriteTimeUtc(original));
                    ct.ThrowIfCancellationRequested();
                    File.Move(temporario, copia, true);
                    using var recuperada = abrirCopia(copia);
                    ConferirHash(recuperada, copia);
                }
                finally { File.Delete(temporario); }
            }
            void ConferirHash(Stream fluxo, string caminho)
            {
                if (!Comparador.Equals(Transferir(fluxo, null, ct), esperado))
                    throw new IOException("O arquivo diverge da cópia inicial da análise: " + caminho);
            }
        }
    }

    static Inventario LerInventario(string origem, string registro, string id)
    {
        var arquivo = Path.Combine(registro, NomeInventario);
        ConferirCaminho(arquivo, local: true);
        var m = JsonSerializer.Deserialize<Inventario>(File.ReadAllBytes(arquivo));
        if (m is not { Versao: 1, Arquivos: not null } || !Guid.TryParseExact(m.Identificador, "N", out _) || !Comparador.Equals(m.Execucao, id) ||
            string.IsNullOrEmpty(m.Processo) || !Path.IsPathFullyQualified(m.Processo) ||
            !RecuperacaoAnaliseLocal.MesmoProcesso(origem, m.Processo, Path.Combine(".pmvni-ai", "editor", id, "EXECUCAO.JSON")))
            throw new InvalidDataException("O inventário da área local pertence a outra execução ou processo.");
        foreach (var (rel, h) in m.Arquivos)
            if (Path.IsPathRooted(rel) || !Comparador.Equals(Path.GetRelativePath(origem, Path.Combine(origem, rel)), rel) ||
                !Dentro(origem, Path.Combine(origem, rel)) || rel.Split(Path.DirectorySeparatorChar).Any(p =>
                    p.Length == 0 || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Comparador.Equals(p, ".pmvni-ai")) ||
                h is not { Length: 64 } || !h.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Entrada inválida no inventário da área local.");
        return m;
    }

    internal static bool EntregaDoEditor(string raiz, string caminho) =>
        Dentro(Path.Combine(raiz, "RELATÓRIOS DE ANÁLISE", "Entregas do Editor"), caminho);

    static HashSet<string> Listar(string raiz, CancellationToken ct, List<string>? diretorios = null)
    {
        var arquivos = new HashSet<string>(Comparador);
        var pastas = new Stack<string>();
        pastas.Push(raiz);
        while (pastas.TryPop(out var pasta))
            foreach (var item in new DirectoryInfo(pasta).EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if (Comparador.Equals(item.Name, ".pmvni-ai") || EntregaDoEditor(raiz, item.FullName)) continue;
                if (item.LinkTarget is not null) throw new IOException("Há um link ou junção entre os originais: " + item.FullName);
                var rel = Path.GetRelativePath(raiz, item.FullName);
                if (item is DirectoryInfo)
                {
                    pastas.Push(item.FullName);
                    diretorios?.Add(rel);
                }
                else if (!OrquestradorAnalise.ArquivoTransitorio(item.FullName, item.Attributes)) arquivos.Add(rel);
            }
        return arquivos;
    }

    static string CopiarArquivo(string origem, string destino, string rel, CancellationToken ct)
    {
        var de = Path.Combine(origem, rel);
        var para = Path.Combine(destino, rel);
        ConferirCaminho(de, local: false);
        ConferirCaminho(para, local: true);
        Directory.CreateDirectory(Path.GetDirectoryName(para)!);
        using var entrada = File.OpenRead(de);
        // A própria pasta de preparo só recebe seu nome definitivo após a validação de todos os arquivos.
        string hash;
        using (var saida = new FileStream(para, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            hash = Transferir(entrada, saida, ct);
            saida.Flush(true);
        }
        File.SetLastWriteTimeUtc(para, File.GetLastWriteTimeUtc(de));
        return hash;
    }

    static string Transferir(Stream de, Stream? para, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int n;
        while ((n = de.Read(buffer)) != 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, n);
            para?.Write(buffer, 0, n);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    static void Gravar<T>(string caminho, T valor) => File.WriteAllBytes(caminho, JsonSerializer.SerializeToUtf8Bytes(valor));

    static string DocumentosLegados()
    {
        var pasta = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(pasta) || TravaExecucao.EmVolumeDeRede(pasta) || EmNuvem(pasta) || Redirecionado(pasta, true)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents") : pasta;
    }

    static string CaminhoLocal(string origem, string id, string? documentos)
    {
        documentos ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!Path.IsPathFullyQualified(documentos) || TravaExecucao.EmVolumeDeRede(documentos) || EmNuvem(documentos) || Redirecionado(documentos, true))
            throw new IOException("A área de trabalho precisa estar em um volume local da estação.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Identidade(origem) + "\n" + id.ToUpperInvariant())));
        var chave = id.Length >= 17 && DateTime.TryParseExact(id[..17], "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            ? data.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + digest[..16] : digest[..24];
        return Path.Combine(Path.GetFullPath(documentos), "PMVNI", "Analises", chave, Path.GetFileName(Path.TrimEndingDirectorySeparator(origem)));
    }

    static bool EmNuvem(string pasta)
    {
        foreach (var variavel in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            if (Environment.GetEnvironmentVariable(variavel) is { Length: > 0 } raiz && Dentro(raiz, pasta)) return true;
        return pasta.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => Comparador.Equals(p, "OneDrive") || p.StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase));
    }

    static bool Dentro(string raiz, string caminho)
    {
        var rel = Path.GetRelativePath(raiz, caminho);
        return !Path.IsPathRooted(rel) && rel != ".." && !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    static void ValidarRegistro(string origem, string registro)
    {
        if (RegistroExecucao.MotivoInvalido(origem, registro) is { } erro) throw new IOException(erro);
    }

    static bool Redirecionado(string caminho, bool local)
    {
        for (var p = Path.GetFullPath(caminho); p is not null; p = Path.GetDirectoryName(p))
        {
            FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
            if (info.LinkTarget is not null || local && info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        }
        return false;
    }

    static void ConferirCaminho(string caminho, bool local)
    {
        if (Redirecionado(caminho, local)) throw new IOException("Link, junção ou redirecionamento no caminho da análise: " + caminho);
    }

    internal static string Identidade(string origem)
    {
        var caminho = Path.TrimEndingDirectorySeparator(Path.GetFullPath(origem));
        var raiz = Path.GetPathRoot(caminho)!;
        if (OperatingSystem.IsWindows() && !raiz.StartsWith(@"\\") && new DriveInfo(raiz).DriveType == DriveType.Network)
        {
            var nome = new StringBuilder(2048);
            int tamanho = nome.Capacity;
            if (WNetGetConnection(raiz[..2], nome, ref tamanho) != 0) throw new IOException("Não foi possível identificar o compartilhamento do processo.");
            caminho = Path.Combine(nome.ToString(), Path.GetRelativePath(raiz, caminho));
        }
        return caminho.ToUpperInvariant();
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    static extern int WNetGetConnection(string unidade, StringBuilder remoto, ref int tamanho);
}
