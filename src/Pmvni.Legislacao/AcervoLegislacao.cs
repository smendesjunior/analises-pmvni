using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Legislacao;

/// <summary>Uma cópia do PDF em processamento (<c>.entrada\&lt;32 hex&gt;.pdf</c>), já verificada e com o sha256 calculado.</summary>
public sealed record Entrada(string Caminho, string Sha256, long Tamanho, string NomeOriginal, Esfera EsferaSugerida, string? SubstituiId);

/// <summary>A pasta <c>&lt;dados&gt;\legislacao</c>: o catálogo, as cópias dos PDFs e os índices de texto (desenho §11.1–§11.3).
/// Síncrono e sem UI (o app chama em <c>Task.Run</c>). Toda alteração é ler, alterar e gravar o catálogo dentro de um
/// <see cref="Mutex"/> nomeado da sessão do Windows, de modo que duas janelas do editor não perdem alterações uma da outra.
/// O arquivo original do usuário só é aberto para leitura: nem conteúdo, nem atributos, nem datas mudam.</summary>
public sealed class AcervoLegislacao
{
    public const int VersaoCatalogo = 1;
    public const int VersaoIndice = 1;
    public const int MaximoTitulo = 200;
    const string NomeMutex = @"Local\EditorRelatoriosPMVNI.Legislacao";
    const string Leitor = "pdf.js " + RecursosLegislacao.VersaoPdfJs;
    static readonly TimeSpan IdadeEntradaOrfa = TimeSpan.FromDays(1);
    static readonly TimeSpan IdadeCorrompido = TimeSpan.FromDays(30);
    static readonly Regex NomePdfDeCopia = new(@"^[0-9a-f]{64}\.pdf$", RegexOptions.CultureInvariant);
    static readonly Mutex Trava = new(false, NomeMutex);
    static readonly StringComparer ComparadorTitulo = StringComparer.Create(new CultureInfo("pt-BR"), ignoreCase: true);

    readonly Func<DateTimeOffset> agora;
    readonly Func<string> novoId;
    readonly Dictionary<string, (long Tamanho, long Ticks, bool Valido)> validadeIndices = new(StringComparer.Ordinal);
    readonly List<string> avisos = []; // acumulam até alguém ler (LerAvisos); sempre sob lock(avisos)

    public AcervoLegislacao(string pasta, Func<DateTimeOffset>? agora = null, Func<string>? novoId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        Pasta = Path.GetFullPath(pasta);
        this.agora = agora ?? (() => DateTimeOffset.UtcNow);
        this.novoId = novoId ?? (() => "lg_" + Guid.NewGuid().ToString("N"));
    }

    /// <summary><c>&lt;dados&gt;\legislacao</c>.</summary>
    public string Pasta { get; }

    /// <summary>O catálogo é de uma versão mais nova que a conhecida: só leitura (nada é gravado).</summary>
    public bool SomenteLeitura { get; private set; }

    /// <summary>Avisos pendentes (catálogo danificado, PDFs recuperados, cópia em uso), sem limpá-los. Qualquer carga os acumula
    /// (o <see cref="IndiceLegislacao"/> também carrega o catálogo e não os consome): <see cref="LerAvisos"/> devolve e limpa.</summary>
    public IReadOnlyList<string> Avisos { get { lock (avisos) return [.. avisos]; } }

    /// <summary>Devolve os avisos acumulados e os limpa (a tela os mostra uma vez).</summary>
    public IReadOnlyList<string> LerAvisos()
    {
        lock (avisos)
        {
            var lista = avisos.ToArray();
            avisos.Clear();
            return lista;
        }
    }

    void Avisar(string aviso) { lock (avisos) avisos.Add(aviso); }

    /// <summary>Gancho de teste: roda depois de gravar o <c>catalogo.json.tmp</c> e antes de trocá-lo pelo catálogo.</summary>
    internal Action? AntesDeTrocarCatalogo { get; set; }

    /// <summary>Limite de tamanho por arquivo (gancho de teste; o padrão é <see cref="VerificacaoPdf.LimiteBytes"/>).</summary>
    internal long LimiteBytes { get; set; } = VerificacaoPdf.LimiteBytes;

    string Catalogo => Path.Combine(Pasta, "catalogo.json");
    string CatalogoBak => Catalogo + ".bak";
    string PastaEntrada => Path.Combine(Pasta, ".entrada");
    string CaminhoCopia(string sha) => Path.Combine(Pasta, sha + ".pdf");
    string CaminhoIndice(string sha) => Path.Combine(Pasta, sha + ".texto.json.gz");

    // ---- Leitura ----

    /// <summary>Relê o catálogo (recuperando o danificado e os PDFs sem linha): ordem <see cref="Esferas.Ordem"/>, depois título
    /// (pt-BR, sem diferenciar caixa).</summary>
    public IReadOnlyList<Anexo> Listar() => ComTrava(() =>
    {
        return Ordenar(Carregar().Anexos);
    });

    public EstadoAnexo Estado(string id) => ComTrava(() => EstadoDe(Achar(Carregar(), id)));

    /// <summary>Os anexos (na ordem de <see cref="Listar"/>) com o estado de cada um, lidos numa só carga do catálogo: um anexo excluído
    /// por outra janela no meio nunca aparece numa lista e falta na outra.</summary>
    public IReadOnlyList<(Anexo Anexo, EstadoAnexo Estado)> ListarComEstado() => ComTrava(() =>
        (IReadOnlyList<(Anexo, EstadoAnexo)>)[.. Ordenar(Carregar().Anexos).Select(a => (a, EstadoDe(a)))]);

    EstadoAnexo EstadoDe(Anexo a) =>
        !File.Exists(CaminhoCopia(a.Sha256)) ? EstadoAnexo.SemCopia : IndiceValido(a.Sha256) ? EstadoAnexo.Pronto : EstadoAnexo.SemIndice;

    /// <summary>A cópia do PDF, ou null se o anexo não existe ou a cópia sumiu.</summary>
    public string? CaminhoPdf(string id) => ComTrava(() =>
    {
        var a = Carregar().Anexos.FirstOrDefault(x => x.Id == id);
        return a is not null && File.Exists(CaminhoCopia(a.Sha256)) ? CaminhoCopia(a.Sha256) : null;
    });

    /// <summary>Anexos com cópia mas sem índice de texto válido (faltando, de outra versão ou que não confere com o sha).</summary>
    public IReadOnlyList<Anexo> SemIndice() => ComTrava(() =>
    {
        var c = Carregar();
        if (c.SomenteLeitura) return (IReadOnlyList<Anexo>)[];
        return (IReadOnlyList<Anexo>)[.. Ordenar(c.Anexos).Where(a => File.Exists(CaminhoCopia(a.Sha256)) && !IndiceValido(a.Sha256))];
    });

    /// <summary>O texto de cada página, do índice; null se o índice falta ou não vale.</summary>
    public IReadOnlyList<string>? TextoPorPagina(string id) => ComTrava(() =>
    {
        var a = Carregar().Anexos.FirstOrDefault(x => x.Id == id);
        return a is null ? null : LerIndice(a.Sha256);
    });

    // ---- Anexar ----

    /// <summary>Verifica o original (tamanho antes de ler, assinatura, sha256, repetido) e o copia para <c>.entrada</c> numa só
    /// leitura. Recusa com <see cref="AnexoRecusadoException"/> (mensagem já pronta); catálogo de versão mais nova ⇒
    /// <see cref="InvalidOperationException"/>. Com <paramref name="substituiId"/>, vale para "Substituir arquivo…": o mesmo
    /// arquivo já anexado naquela linha é <see cref="MensagensAnexo.Igual"/>.</summary>
    public Entrada Preparar(string caminhoOriginal, string? substituiId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoOriginal);
        var nome = Path.GetFileName(caminhoOriginal);
        ComTrava(() =>
        {
            var c = Carregar();
            ExigirEscrita(c);
            if (substituiId is not null) Achar(c, substituiId);
            return 0;
        });

        FileStream origem;
        try { origem = new FileStream(caminhoOriginal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            throw new AnexoRecusadoException(MensagensAnexo.Ilegivel(nome, e), e);
        }

        string? destino = null;
        try
        {
            using (origem)
            {
                var tamanho = origem.Length;
                if (tamanho == 0) throw new AnexoRecusadoException(MensagensAnexo.Vazio(nome));
                if (tamanho > LimiteBytes) throw new AnexoRecusadoException(MensagensAnexo.Grande(nome, tamanho));

                var buffer = new byte[81920];
                var inicio = LerAte(origem, buffer, 1024);
                if (!VerificacaoPdf.TemAssinatura(buffer.AsSpan(0, inicio))) throw new AnexoRecusadoException(MensagensAnexo.NaoPdf(nome));

                Directory.CreateDirectory(PastaEntrada);
                var info = new DirectoryInfo(PastaEntrada);
                if ((info.Attributes & FileAttributes.Hidden) == 0) info.Attributes |= FileAttributes.Hidden;
                destino = Path.Combine(PastaEntrada, Guid.NewGuid().ToString("N") + ".pdf");

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long total = inicio;
                using (var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
                {
                    saida.Write(buffer, 0, inicio);
                    hash.AppendData(buffer, 0, inicio);
                    int lido;
                    while ((lido = origem.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += lido;
                        if (total > LimiteBytes) throw new AnexoRecusadoException(MensagensAnexo.Grande(nome, total));
                        saida.Write(buffer, 0, lido);
                        hash.AppendData(buffer, 0, lido);
                    }
                }
                var sha = Convert.ToHexStringLower(hash.GetHashAndReset());

                ComTrava(() =>
                {
                    var c = Carregar();
                    var igual = c.Anexos.FirstOrDefault(a => a.Sha256 == sha);
                    if (igual is null) return 0;
                    if (substituiId is not null && igual.Id == substituiId) throw new AnexoRecusadoException(MensagensAnexo.Igual);
                    throw new AnexoRecusadoException(MensagensAnexo.Repetido(nome, igual.Titulo));
                });
                return new Entrada(destino, sha, total, nome, Esferas.Sugerir(caminhoOriginal), substituiId);
            }
        }
        catch (Exception e)
        {
            ApagarSilencioso(destino);
            if (e is IOException or UnauthorizedAccessException) throw new AnexoRecusadoException(MensagensAnexo.Ilegivel(nome, e), e);
            throw;
        }
    }

    /// <summary>Move a entrada para <c>&lt;sha&gt;.pdf</c>, grava o índice e depois a linha do catálogo; se o catálogo não
    /// puder ser gravado, a cópia e o índice criados agora são desfeitos.</summary>
    public Anexo Confirmar(Entrada e, ResultadoExtracao r)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(r);
        return ComTrava(() =>
        {
            var c = Carregar();
            ExigirEscrita(c);
            var igual = c.Anexos.FirstOrDefault(a => a.Sha256 == e.Sha256);
            if (igual is not null)
            {
                ApagarEntrada(e);
                throw new AnexoRecusadoException(MensagensAnexo.Repetido(e.NomeOriginal, igual.Titulo));
            }
            var anexo = new Anexo(novoId(), e.Sha256, TituloInicial(e.NomeOriginal), e.EsferaSugerida, e.NomeOriginal, e.Tamanho,
                r.Paginas, r.PaginasSemTexto, Agora(), null);
            var (criouCopia, criouIndice) = PublicarArquivos(e, r);
            try { Salvar(c.Anexos.Append(anexo)); }
            catch
            {
                if (criouCopia) ApagarSilencioso(CaminhoCopia(e.Sha256));
                if (criouIndice) ApagarSilencioso(CaminhoIndice(e.Sha256));
                throw;
            }
            ApagarSilencioso(MarcaDeExclusao(e.Sha256)); // o mesmo PDF voltou ao catálogo: a exclusão pendente deixa de valer
            return anexo;
        });
    }

    /// <summary>Nova versão da mesma lei: mantém Id, Título, Esfera e AnexadoEm, troca o arquivo, as páginas e o índice e grava
    /// <c>substituido_em</c>; os arquivos do sha anterior só são apagados depois do catálogo novo gravado.</summary>
    public Anexo ConfirmarSubstituicao(string id, Entrada e, ResultadoExtracao r)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(r);
        if (e.SubstituiId is not null && e.SubstituiId != id) throw new ArgumentException("A entrada foi preparada para outro anexo.", nameof(e));
        return ComTrava(() =>
        {
            var c = Carregar();
            ExigirEscrita(c);
            var atual = Achar(c, id);
            var igual = c.Anexos.FirstOrDefault(a => a.Sha256 == e.Sha256);
            if (igual is not null)
            {
                ApagarEntrada(e);
                throw new AnexoRecusadoException(igual.Id == id ? MensagensAnexo.Igual : MensagensAnexo.Repetido(e.NomeOriginal, igual.Titulo));
            }
            var novo = atual with
            {
                Sha256 = e.Sha256, ArquivoOriginal = e.NomeOriginal, Tamanho = e.Tamanho, Paginas = r.Paginas,
                PaginasSemTexto = r.PaginasSemTexto, SubstituidoEm = Agora(),
            };
            var (criouCopia, criouIndice) = PublicarArquivos(e, r);
            try { Salvar(c.Anexos.Select(a => a.Id == id ? novo : a)); }
            catch
            {
                if (criouCopia) ApagarSilencioso(CaminhoCopia(e.Sha256));
                if (criouIndice) ApagarSilencioso(CaminhoIndice(e.Sha256));
                throw;
            }
            ApagarSilencioso(MarcaDeExclusao(e.Sha256));
            if (!ApagarArquivosDe(atual.Sha256)) Avisar(MensagensAnexo.CopiaEmUso);
            return novo;
        });
    }

    /// <summary>Apaga a cópia de entrada (recusa ou cancelamento). Nunca toca fora de <c>.entrada</c>.</summary>
    public void Descartar(Entrada e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ApagarEntrada(e);
    }

    // ---- Edição ----

    public void Renomear(string id, string titulo)
    {
        var t = (titulo ?? "").Trim();
        if (t.Length == 0) throw new ArgumentException(MensagensAnexo.TituloVazio);
        if (t.Length > MaximoTitulo) throw new ArgumentException(MensagensAnexo.TituloLongo);
        ComTrava(() =>
        {
            var c = Carregar();
            ExigirEscrita(c);
            var a = Achar(c, id);
            if (a.Titulo != t) Salvar(c.Anexos.Select(x => x.Id == id ? x with { Titulo = t } : x));
            return 0;
        });
    }

    public void DefinirEsfera(string id, Esfera esfera) => ComTrava(() =>
    {
        var c = Carregar();
        ExigirEscrita(c);
        var a = Achar(c, id);
        if (a.Esfera != esfera) Salvar(c.Anexos.Select(x => x.Id == id ? x with { Esfera = esfera } : x));
        return 0;
    });

    /// <summary>Grava o catálogo sem a linha e só depois apaga <c>&lt;sha&gt;.pdf</c> e o índice. O original do usuário nunca é
    /// tocado. Anexo inexistente: nada a fazer. Se a cópia estiver em uso por outro programa e não puder ser apagada, o
    /// catálogo já está gravado: devolve <see cref="MensagensAnexo.CopiaEmUso"/> e deixa uma marca <c>&lt;sha&gt;.excluir</c>, e a
    /// próxima carga apaga a cópia em vez de "recuperá-la". Sem problema: null.</summary>
    public string? Excluir(string id) => ComTrava(() =>
    {
        var c = Carregar();
        ExigirEscrita(c);
        var a = c.Anexos.FirstOrDefault(x => x.Id == id);
        if (a is null) return null;
        Salvar(c.Anexos.Where(x => x.Id != id));
        return ApagarArquivosDe(a.Sha256) ? null : MensagensAnexo.CopiaEmUso;
    });

    // ---- Índice de texto ----

    /// <summary>Grava o índice de texto do anexo (a pedido do reindexar em segundo plano) e, se a extração trouxe páginas
    /// diferentes das do catálogo (PDF recuperado, por exemplo), as acerta no catálogo.</summary>
    public void GravarIndice(string id, ResultadoExtracao r)
    {
        ArgumentNullException.ThrowIfNull(r);
        ComTrava(() =>
        {
            var c = Carregar();
            ExigirEscrita(c);
            var a = Achar(c, id);
            EscreverIndice(a.Sha256, r);
            if (a.Paginas != r.Paginas || !a.PaginasSemTexto.SequenceEqual(r.PaginasSemTexto))
                Salvar(c.Anexos.Select(x => x.Id == id ? x with { Paginas = r.Paginas, PaginasSemTexto = r.PaginasSemTexto } : x));
            return 0;
        });
    }

    /// <summary>Apaga de <c>.entrada</c> o que tem mais de um dia (restos de uma queda do app) e os
    /// <c>catalogo.corrompido-*.json</c> com mais de 30 dias.</summary>
    public void LimparEntradasOrfas() => ComTrava(() =>
    {
        LimparEntradasOrfasNaTrava();
        return 0;
    });

    void LimparEntradasOrfasNaTrava()
    {
        var agoraUtc = agora().UtcDateTime;
        if (Directory.Exists(PastaEntrada))
            foreach (var f in Directory.EnumerateFiles(PastaEntrada)) ApagarSeAntigo(f, agoraUtc - IdadeEntradaOrfa);
        if (Directory.Exists(Pasta))
            foreach (var f in Directory.EnumerateFiles(Pasta, "catalogo.corrompido-*.json")) ApagarSeAntigo(f, agoraUtc - IdadeCorrompido);
    }

    static void ApagarSeAntigo(string arquivo, DateTime limite)
    {
        try { if (File.GetLastWriteTimeUtc(arquivo) < limite) File.Delete(arquivo); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* em uso: fica para a próxima */ }
    }

    // ---- Carga e gravação do catálogo (sempre sob o Mutex) ----

    sealed record Carga(int Versao, List<Anexo> Anexos, bool SomenteLeitura);

    Carga Carregar()
    {
        var corrompido = false;
        DadosCatalogo? dados = null;
        var bytes = LerBytes(Catalogo);
        if (bytes is not null && !CatalogoLegislacao.TentarLer(bytes, out dados)) { corrompido = true; dados = null; }

        if (dados is null)
        {
            var bak = LerBytes(CatalogoBak);
            DadosCatalogo? doBak = null;
            var bakValido = bak is not null && CatalogoLegislacao.TentarLer(bak, out doBak);
            if (bakValido && doBak!.Versao > VersaoCatalogo) bakValido = false; // um .bak de versão mais nova não substitui nada
            if (corrompido) GuardarCorrompido();
            if (bakValido)
            {
                dados = doBak!;
                if (corrompido) Avisar(MensagensAnexo.Corrompido);
                try { File.Copy(CatalogoBak, Catalogo, overwrite: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a próxima gravação refaz */ }
            }
            else dados = new DadosCatalogo(VersaoCatalogo, []);
        }

        var somenteLeitura = dados.Versao > VersaoCatalogo;
        SomenteLeitura = somenteLeitura;
        var anexos = dados.Anexos;
        if (!somenteLeitura) RecuperarSemLinha(anexos, ApagarExclusoesPendentes(anexos));
        return new Carga(dados.Versao, anexos, somenteLeitura);
    }

    /// <summary>Um <c>&lt;sha&gt;.pdf</c> sem linha no catálogo (catálogo perdido, ou queda entre a cópia e a gravação) nunca é
    /// apagado: volta como "PDF recuperado &lt;8 hex&gt;", esfera Outras.</summary>
    void RecuperarSemLinha(List<Anexo> anexos, HashSet<string> excluidosPendentes)
    {
        if (!Directory.Exists(Pasta)) return;
        var conhecidos = anexos.Select(a => a.Sha256).ToHashSet(StringComparer.Ordinal);
        conhecidos.UnionWith(excluidosPendentes); // excluídos cuja cópia estava em uso: não voltam como "recuperados"
        var recuperados = 0;
        foreach (var arquivo in Directory.EnumerateFiles(Pasta, "*.pdf").Order(StringComparer.Ordinal))
        {
            var nome = Path.GetFileName(arquivo);
            if (!NomePdfDeCopia.IsMatch(nome)) continue;
            var sha = nome[..64];
            if (!conhecidos.Add(sha)) continue;
            var texto = LerIndice(sha);
            var semTexto = texto is null ? Array.Empty<int>() : Enumerable.Range(1, texto.Count).Where(i => !ResultadoExtracao.TemLetraOuDigito(texto[i - 1])).ToArray();
            anexos.Add(new Anexo(novoId(), sha, "PDF recuperado " + sha[..8], Esfera.Outras, nome, new FileInfo(arquivo).Length,
                texto?.Count ?? 0, semTexto, Agora(), null));
            recuperados++;
        }
        if (recuperados == 0) return;
        Avisar(MensagensAnexo.Recuperados(recuperados));
        try { Salvar(anexos); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* segue em memória; a próxima carga tenta de novo */ }
    }

    void GuardarCorrompido()
    {
        try
        {
            var nome = "catalogo.corrompido-" + agora().UtcDateTime.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture) + ".json";
            File.Move(Catalogo, Path.Combine(Pasta, nome), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* o catálogo danificado segue no lugar; a gravação o troca */ }
    }

    /// <summary>tmp, flush, <see cref="File.Replace(string,string,string?)"/> com o <c>.bak</c> (a primeira vez, <see cref="File.Move(string,string)"/>).
    /// Falha antes da troca deixa o catálogo anterior intacto e o <c>.tmp</c> some.</summary>
    void Salvar(IEnumerable<Anexo> anexos)
    {
        Directory.CreateDirectory(Pasta);
        var tmp = Catalogo + ".tmp";
        var bytes = CatalogoLegislacao.Serializar(VersaoCatalogo, anexos);
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            AntesDeTrocarCatalogo?.Invoke();
            if (File.Exists(Catalogo)) File.Replace(tmp, Catalogo, CatalogoBak, ignoreMetadataErrors: true);
            else File.Move(tmp, Catalogo);
        }
        finally
        {
            ApagarSilencioso(tmp);
        }
    }

    T ComTrava<T>(Func<T> acao)
    {
        try { if (!Trava.WaitOne(TimeSpan.FromSeconds(60))) throw new IOException("O catálogo da legislação está em uso por outra janela do editor."); }
        catch (AbandonedMutexException) { /* a outra janela caiu com o catálogo: o mutex é nosso */ }
        try { return acao(); }
        finally { Trava.ReleaseMutex(); }
    }

    void ExigirEscrita(Carga c)
    {
        if (c.SomenteLeitura) throw new InvalidOperationException(MensagensAnexo.SomenteLeitura);
    }

    static Anexo Achar(Carga c, string id) =>
        c.Anexos.FirstOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException("Anexo desconhecido: " + id);

    static readonly Esfera[] OrdemEsferas = [.. Esferas.Ordem];

    static IReadOnlyList<Anexo> Ordenar(IEnumerable<Anexo> anexos) =>
        [.. anexos.OrderBy(a => Array.IndexOf(OrdemEsferas, a.Esfera)).ThenBy(a => a.Titulo, ComparadorTitulo).ThenBy(a => a.Id, StringComparer.Ordinal)];

    DateTimeOffset Agora()
    {
        var t = agora().UtcDateTime;
        return new DateTimeOffset(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    static string TituloInicial(string nome)
    {
        var t = nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? nome[..^4] : nome;
        t = t.Trim();
        if (t.Length == 0) t = nome.Trim();
        return t.Length > MaximoTitulo ? t[..MaximoTitulo] : t;
    }

    // ---- Arquivos ----

    /// <summary>Move a entrada para <c>&lt;sha&gt;.pdf</c> (se já houver um com o mesmo sha, a entrada é apagada) e grava o índice.</summary>
    (bool CriouCopia, bool CriouIndice) PublicarArquivos(Entrada e, ResultadoExtracao r)
    {
        if (!File.Exists(e.Caminho) && !File.Exists(CaminhoCopia(e.Sha256))) throw new FileNotFoundException("A cópia de entrada não existe mais.", e.Caminho);
        var criouCopia = false;
        var destino = CaminhoCopia(e.Sha256);
        if (File.Exists(destino)) ApagarEntrada(e);
        else { File.Move(e.Caminho, destino); criouCopia = true; }
        var criouIndice = !File.Exists(CaminhoIndice(e.Sha256));
        try { EscreverIndice(e.Sha256, r); }
        catch
        {
            if (criouCopia) ApagarSilencioso(destino);
            throw;
        }
        return (criouCopia, criouIndice);
    }

    void ApagarEntrada(Entrada e)
    {
        var inteiro = Path.GetFullPath(e.Caminho);
        if (!string.Equals(Path.GetDirectoryName(inteiro), PastaEntrada, StringComparison.OrdinalIgnoreCase)) return;
        ApagarSilencioso(inteiro);
    }

    string MarcaDeExclusao(string sha) => Path.Combine(Pasta, sha + ".excluir");

    /// <summary>Apaga a cópia e o índice do sha (o catálogo já foi gravado). Se algum não puder ser apagado (preso por outro
    /// programa), deixa a marca <c>&lt;sha&gt;.excluir</c> para a próxima carga terminar o serviço, e devolve false.</summary>
    bool ApagarArquivosDe(string sha)
    {
        validadeIndices.Remove(sha);
        if (ApagarComTentativas(CaminhoCopia(sha), 4) & ApagarComTentativas(CaminhoIndice(sha), 4)) return true;
        try { File.WriteAllBytes(MarcaDeExclusao(sha), []); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* sem a marca, só resta o aviso */ }
        return false;
    }

    /// <summary>Termina as exclusões que ficaram pendentes (<c>&lt;sha&gt;.excluir</c>): apaga cópia e índice e a marca; marca
    /// de um sha que voltou ao catálogo só some. Devolve os shas ainda pendentes (cópia ainda em uso).</summary>
    HashSet<string> ApagarExclusoesPendentes(List<Anexo> anexos)
    {
        var pendentes = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(Pasta)) return pendentes;
        foreach (var marca in Directory.EnumerateFiles(Pasta, "*.excluir"))
        {
            var sha = Path.GetFileName(marca)[..^".excluir".Length];
            if (!CatalogoLegislacao.EhSha(sha)) continue;
            if (anexos.Any(a => a.Sha256 == sha)) { ApagarSilencioso(marca); continue; }
            validadeIndices.Remove(sha);
            if (ApagarComTentativas(CaminhoCopia(sha), 1) & ApagarComTentativas(CaminhoIndice(sha), 1)) ApagarSilencioso(marca);
            else pendentes.Add(sha);
        }
        return pendentes;
    }

    /// <summary>Apaga o arquivo (já ausente conta como apagado); false se continuar preso depois das tentativas.</summary>
    static bool ApagarComTentativas(string caminho, int tentativas)
    {
        for (var i = 0; i < tentativas; i++)
        {
            try { File.Delete(caminho); return true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { if (i + 1 < tentativas) Thread.Sleep(50); }
        }
        return false;
    }

    static void ApagarSilencioso(string? caminho)
    {
        if (caminho is null) return;
        try { File.Delete(caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static byte[]? LerBytes(string caminho)
    {
        try { return File.Exists(caminho) ? File.ReadAllBytes(caminho) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static int LerAte(Stream s, byte[] buffer, int quantos)
    {
        var total = 0;
        while (total < quantos)
        {
            var n = s.Read(buffer, total, quantos - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    // ---- Índice de texto: gzip de { versao_indice, sha256, leitor, paginas[] } ----

    static readonly System.Text.Json.JsonSerializerOptions OpcoesIndice = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    void EscreverIndice(string sha, ResultadoExtracao r)
    {
        var paginas = new JsonArray();
        for (var i = 0; i < r.Paginas; i++)
            paginas.Add(ResultadoExtracao.TemLetraOuDigito(r.TextoPorPagina[i]) ? Higienizar(r.TextoPorPagina[i]) : ""); // sem OCR: sem texto = ""
        var raiz = new JsonObject { ["versao_indice"] = VersaoIndice, ["sha256"] = sha, ["leitor"] = Leitor, ["paginas"] = paginas };
        var json = new UTF8Encoding(false).GetBytes(raiz.ToJsonString(OpcoesIndice));
        Directory.CreateDirectory(Pasta);
        var destino = CaminhoIndice(sha);
        var tmp = destino + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
                gz.Write(json);
            File.Move(tmp, destino, overwrite: true);
        }
        finally { ApagarSilencioso(tmp); }
        validadeIndices.Remove(sha);
    }

    /// <summary>Surrogates soltos (texto de PDF mal formado) não cabem em UTF-8: viram U+FFFD.</summary>
    static string Higienizar(string s)
    {
        if (s.All(c => !char.IsSurrogate(c))) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; }
            else sb.Append(char.IsSurrogate(c) ? '�' : c);
        }
        return sb.ToString();
    }

    /// <summary>O texto de cada página, se o índice existe, é desta versão e confere com o sha; senão null.</summary>
    IReadOnlyList<string>? LerIndice(string sha)
    {
        var caminho = CaminhoIndice(sha);
        try
        {
            if (!File.Exists(caminho)) return null;
            using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            if (JsonNode.Parse(gz) is not JsonObject raiz) return null;
            if (raiz["versao_indice"] is not JsonValue v || !v.TryGetValue<int>(out var versao) || versao != VersaoIndice) return null;
            if (raiz["sha256"] is not JsonValue s || !s.TryGetValue<string>(out var shaIndice) || shaIndice != sha) return null;
            if (raiz["paginas"] is not JsonArray paginas) return null;
            var texto = new string[paginas.Count];
            for (var i = 0; i < texto.Length; i++)
                texto[i] = paginas[i] is JsonValue p && p.TryGetValue<string>(out var t) ? t : throw new FormatException("página inválida");
            return texto;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    bool IndiceValido(string sha)
    {
        var caminho = CaminhoIndice(sha);
        FileInfo info;
        try { info = new FileInfo(caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        if (!info.Exists) { validadeIndices.Remove(sha); return false; }
        if (validadeIndices.TryGetValue(sha, out var c) && c.Tamanho == info.Length && c.Ticks == info.LastWriteTimeUtc.Ticks) return c.Valido;
        var valido = LerIndice(sha) is not null;
        validadeIndices[sha] = (info.Length, info.LastWriteTimeUtc.Ticks, valido);
        return valido;
    }
}
