using System.Security.Cryptography;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pmvni.Revisao;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Pmvni.App;

/// <summary>Um Novo despacho em curso (desenho §10.2–§10.9), ou o "Gerar PDF" de um despacho do editor que ficou sem PDF: liga o
/// <see cref="RascunhoDespacho"/> e a <see cref="EmissaoDespacho"/> (preparar na pasta de trabalho <c>&lt;dados&gt;\despachos\&lt;guid&gt;</c>,
/// gravar na pasta do processo só depois da conferência das páginas) e desenha as páginas do PDF pelo <c>Windows.Data.Pdf</c>. O que é
/// lento (leitura do pacote, preparo, gravação) roda em <c>Task.Run</c>; a página é atualizada por quem chama (a janela), na thread da
/// interface. O kit é só lido; o <c>.pmvrel</c> é lido sem trava e nunca alterado.</summary>
internal sealed class SessaoNovoDespacho
{
    CancellationTokenSource? preparo;
    Task<Preparacao>? tarefaDoPreparo; // o trabalho em segundo plano do preparo em curso (o fechamento do app espera por ele)
    DespachoFinal? final;
    string? pastaTrabalho;

    SessaoNovoDespacho(TipoDespacho tipo, string raizKit, string raizTrabalho, string? docxGravado)
    {
        Tipo = tipo;
        RaizKit = raizKit;
        RaizTrabalho = raizTrabalho;
        DocxGravado = docxGravado;
    }

    /// <summary>Um Novo despacho do tipo <paramref name="tipo"/>, com os modelos lidos de <paramref name="raizKit"/>.</summary>
    public static SessaoNovoDespacho Novo(TipoDespacho tipo, string raizKit, string raizTrabalho) => new(tipo, raizKit, raizTrabalho, null);

    /// <summary>"Gerar PDF" de um despacho já gravado pelo editor (só o PDF; o DOCX não muda).</summary>
    public static SessaoNovoDespacho ParaPdf(string docx, string raizTrabalho) => new(TipoDespacho.NaoIdentificado, "", raizTrabalho, docx)
    {
        Passo = PassoNovoDespacho.Conferencia,
    };

    public TipoDespacho Tipo { get; private set; }
    public string RaizKit { get; }
    /// <summary><c>&lt;dados&gt;\despachos</c>: cada preparo usa uma subpasta nova (um guid), apagada depois de gravar, voltar ou cancelar.</summary>
    public string RaizTrabalho { get; }
    public string? DocxGravado { get; }
    public bool SoPdf => DocxGravado is not null;

    public ModeloDespacho? Modelo { get; private set; }
    public InfoModelo? Info { get; private set; }
    public TextosFixos? Textos { get; private set; }
    /// <summary>A página do modelo (cabeçalho, tarja, rodapé, margens e fontes), lida do DOCX do kit: a prévia é desenhada nela.</summary>
    public LayoutDespacho? Layout { get; private set; }

    public IReadOnlyList<Processo> Processos { get; set; } = [];
    public Processo? Processo { get; private set; }
    /// <summary>Os <c>.pmvrel</c> da pasta do processo (os relatórios base possíveis).</summary>
    public IReadOnlyList<RelatorioEncontrado> Relatorios { get; private set; } = [];
    /// <summary>Os relatórios escolhidos à mão ("Escolher arquivo…") para este processo: entram na lista depois dos da pasta.</summary>
    public List<string> RelatoriosEscolhidos { get; } = [];
    public string? CaminhoPacote { get; private set; }
    public PacoteAberto? Pacote { get; private set; }
    /// <summary>A etapa nova (primeira) e as existentes, da de maior número para a de menor.</summary>
    public IReadOnlyList<EtapaDespacho> Etapas { get; private set; } = [];
    public EtapaDespacho? Etapa { get; private set; }
    public IReadOnlyList<string> Bloqueios { get; private set; } = [];
    public List<string> AvisosConfirmados { get; } = [];
    public RascunhoDespacho? Rascunho { get; private set; }

    public PassoNovoDespacho Passo { get; set; } = PassoNovoDespacho.Origem;
    public Preparacao? Preparacao { get; private set; }
    public SortedSet<int> Conferidas { get; } = new();
    public bool Preparando => preparo is not null;

    /// <summary>Gravando na pasta do processo agora: trocar de módulo, abrir outro despacho ou fechar esperam (nunca "Nada foi gravado").</summary>
    public bool Gravando { get; private set; }

    /// <summary>Este processo já desenhou páginas de PDF pelo <c>Windows.Data.Pdf</c> (o <c>Program</c> então termina o processo direto no fim:
    /// o driver de vídeo da Intel trava a descarga das DLLs depois disso).</summary>
    public static bool DesenhouPaginas { get; private set; }
    public bool Descartada { get; private set; }

    /// <summary>O modelo, a estrutura dele e os textos fixos do tipo, lidos no lugar (síncrono, para <c>Task.Run</c>). Tipo indisponível:
    /// <see cref="InvalidOperationException"/> com o motivo do registro.</summary>
    public static (ModeloDespacho Modelo, InfoModelo Info, TextosFixos Textos, LayoutDespacho Layout) LerModelo(ModelosDisponiveis m, TipoDespacho t, string raizKit)
    {
        if (FluxoNovoDespacho.MotivoDesabilitado(t, m, false) is { } motivo) throw new InvalidOperationException(motivo);
        var modelo = m.Modelos.Single(x => x.Tipo == t);
        var bytes = File.ReadAllBytes(modelo.Caminho);
        var info = PreenchimentoDocx.LerModelo(bytes, t);
        return (modelo, info, TextosFixos.Ler(raizKit, t), LayoutDespacho.Ler(bytes));
    }

    public void DefinirModelo((ModeloDespacho Modelo, InfoModelo Info, TextosFixos Textos, LayoutDespacho Layout) m)
    {
        Tipo = m.Modelo.Tipo;
        Modelo = m.Modelo;
        Info = m.Info;
        Textos = m.Textos;
        Layout = m.Layout;
        Rascunho = null;
        AvisosConfirmados.Clear();
        Passo = PassoNovoDespacho.Origem;
    }

    /// <summary>Os <c>.pmvrel</c> e as etapas (a nova primeiro) do processo (síncrono, para <c>Task.Run</c>).</summary>
    public static (IReadOnlyList<RelatorioEncontrado> Relatorios, IReadOnlyList<EtapaDespacho> Etapas, EtapaDespacho Sugerida) LerProcesso(Processo p, DateOnly hoje)
    {
        var relatorios = Pmvni.Revisao.Processos.Detalhar(p, CancellationToken.None).Relatorios.Where(r => r.Tipo == TipoRelatorio.Pacote).ToList();
        var etapas = new List<EtapaDespacho> { Pmvni.Revisao.Etapas.Nova(p.Pasta, hoje) };
        etapas.AddRange(Pmvni.Revisao.Etapas.Existentes(p.Pasta));
        var sugerida = Pmvni.Revisao.Etapas.Sugerida(p.Pasta, hoje);
        return (relatorios, etapas, etapas.FirstOrDefault(e => e.Nome == sugerida.Nome) ?? sugerida);
    }

    public void DefinirProcesso(Processo p, IReadOnlyList<RelatorioEncontrado> relatorios, IReadOnlyList<EtapaDespacho> etapas, EtapaDespacho etapa)
    {
        if (Processo != p) RelatoriosEscolhidos.Clear();
        Processo = p;
        Relatorios = relatorios;
        Etapas = etapas;
        Etapa = etapas.FirstOrDefault(e => e.Nome == etapa.Nome) ?? etapa;
        CaminhoPacote = null;
        Pacote = null;
        Bloqueios = [];
        Rascunho = null;
        AvisosConfirmados.Clear();
        Passo = PassoNovoDespacho.Origem;
    }

    /// <summary>O pacote gravado (lido sem trava) e o que impede o despacho (desenho §10.3).</summary>
    public void DefinirPacote(string caminho, PacoteAberto pacote, IReadOnlyList<string> bloqueios)
    {
        CaminhoPacote = caminho;
        Pacote = pacote;
        Bloqueios = bloqueios;
        Rascunho = null;
        AvisosConfirmados.Clear();
        Passo = PassoNovoDespacho.Origem;
    }

    /// <summary>Monta o rascunho (passo 2) a partir do pacote gravado e da etapa.</summary>
    public void Montar()
    {
        if (Info is null || Etapa is null) throw new InvalidOperationException("Escolha o processo e a pasta da etapa.");
        // Sem relatório (correções 2026-11-15): o despacho começa só com o cabeçalho do processo; com relatório, só os itens que não atendem.
        Rascunho = Pacote is null
            ? ConteudoDespacho.MontarEmBranco(Tipo, Info, Etapa.Data, ProtocoloDoProcesso(), Processo?.Requerente ?? "")
            : ConteudoDespacho.Montar(Tipo, Pacote.Conteudo.Projeto, Info, Etapa);
        Passo = PassoNovoDespacho.Conteudo;
    }

    /// <summary>Outra pasta da etapa (outra data): um rascunho novo com o que já foi editado (<see cref="FluxoNovoDespacho.TransferirEdicoes"/>).</summary>
    public void DefinirEtapa(EtapaDespacho etapa)
    {
        Etapa = etapa;
        if (Rascunho is not { } antes || Info is null) return;
        var novo = Pacote is null
            ? ConteudoDespacho.MontarEmBranco(Tipo, Info, etapa.Data, antes.Protocolo, antes.Requerente)
            : ConteudoDespacho.Montar(Tipo, Pacote.Conteudo.Projeto, Info, etapa);
        if (Pacote is null) foreach (var p in antes.Pendencias) novo.AdicionarExigencia(p.Texto, p.Fundamento, p.Grupo);
        FluxoNovoDespacho.TransferirEdicoes(antes, novo);
        Rascunho = novo;
    }

    /// <summary>A prévia: o rascunho ou, antes dele, o próprio modelo do kit com os campos a preencher (nunca a folha em branco).</summary>
    public IReadOnlyList<ParagrafoPrevia> Previa() =>
        Info is { } i && Textos is { } t
            ? ConteudoDespacho.Previa(i, Rascunho ?? ConteudoDespacho.MontarEmBranco(Tipo, i, Etapa?.Data ?? DateOnly.FromDateTime(DateTime.Today), ProtocoloDoProcesso(), Processo?.Requerente ?? ""), t)
            : [];

    string ProtocoloDoProcesso() =>
        Processo is { Numero: { } n, Ano: { } a } ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n:D4}/{a}") : "";

    public void AtualizarEtapas(IReadOnlyList<EtapaDespacho> etapas) => Etapas = etapas;

    /// <summary>Volta a não ter relatório base (o despacho segue com o que já foi escrito à mão).</summary>
    public void SemRelatorio()
    {
        CaminhoPacote = null;
        Pacote = null;
        Bloqueios = [];
    }

    string NovaPastaDeTrabalho() => pastaTrabalho = Path.Combine(RaizTrabalho, Guid.NewGuid().ToString("N"));

    /// <summary>Passo 3: congela o rascunho e prepara DOCX, validação e PDF numa pasta de trabalho nova. Cancelado (<see cref="Interromper"/>):
    /// a pasta de trabalho é apagada e a <see cref="OperationCanceledException"/> segue.</summary>
    public async Task<Preparacao> PrepararAsync(IConversorPdf conv)
    {
        if (Rascunho is null || Textos is null || Modelo is null) throw new InvalidOperationException("O rascunho do despacho não está pronto.");
        var f = Rascunho.Congelar(Textos);
        var modelo = Modelo;
        return await PrepararNaPastaAsync((pasta, ct) => EmissaoDespacho.Preparar(f, modelo, pasta, conv, ct), f);
    }

    /// <summary>Gerar PDF: converte uma cópia do DOCX gravado (que tem de conferir com o recibo).</summary>
    public Task<Preparacao> PrepararPdfAsync(IConversorPdf conv)
    {
        var docx = DocxGravado ?? throw new InvalidOperationException("Não é um Gerar PDF.");
        return PrepararNaPastaAsync((pasta, ct) => EmissaoDespacho.PrepararPdf(docx, pasta, conv, ct), null);
    }

    async Task<Preparacao> PrepararNaPastaAsync(Func<string, CancellationToken, Preparacao> preparar, DespachoFinal? f)
    {
        ApagarTrabalho();
        var pasta = NovaPastaDeTrabalho();
        var cts = new CancellationTokenSource();
        preparo = cts;
        try
        {
            var tarefa = Task.Run(() => preparar(pasta, cts.Token), cts.Token);
            tarefaDoPreparo = tarefa;
            var p = await tarefa;
            cts.Token.ThrowIfCancellationRequested();
            Preparacao = p;
            final = f;
            Conferidas.Clear();
            Passo = PassoNovoDespacho.Conferencia;
            return p;
        }
        catch
        {
            ApagarTrabalho();
            throw;
        }
        finally
        {
            preparo = null;
            tarefaDoPreparo = null;
            cts.Dispose();
        }
    }

    /// <summary>Cancelar durante o preparo: interrompe (o LibreOffice é encerrado) e a pasta de trabalho é apagada.</summary>
    public void Interromper()
    {
        try { preparo?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>"Voltar e editar": o que foi gerado sai (pasta de trabalho apagada) e volta ao passo 2.</summary>
    public void VoltarParaEdicao()
    {
        ApagarTrabalho();
        Preparacao = null;
        final = null;
        Conferidas.Clear();
        Passo = Rascunho is null ? PassoNovoDespacho.Origem : PassoNovoDespacho.Conteudo;
    }

    /// <summary>Grava na pasta do processo (DOCX, PDF se houver e recibo; nunca sobrescreve). Em falha, nada fica pela metade (Tarefa 15).</summary>
    public async Task<Entrega> GravarAsync(string? motor, ContextoUsuario u, string appVersao)
    {
        if (Preparacao is not { } p || final is not { } f || Rascunho is not { } r || Modelo is not { } m || Textos is not { } tx || Etapa is not { } etapa
            || Processo is not { } processo)
            throw new InvalidOperationException("O despacho ainda não foi gerado e conferido.");
        // Sem relatório base: o recibo leva a origem vazia (o despacho foi escrito à mão ou pela IA).
        var origem = Pacote is { } pacote && CaminhoPacote is { } caminho
            ? new OrigemDespacho(processo, caminho, pacote.Disco.Sha256,
                Convert.ToHexStringLower(SHA256.HashData(DadosEfetivos.Gerar(pacote.Conteudo.Projeto).Json)), pacote.Conteudo.Historico.Count, appVersao,
                AvisosConfirmados.ToList())
            : new OrigemDespacho(processo, "", "", "", 0, appVersao, AvisosConfirmados.ToList());
        var paginas = p.Pdf is null ? null : Conferidas.ToList();
        Gravando = true;
        try
        {
            var e = await Task.Run(() => EmissaoDespacho.Gravar(p, r, f, m, tx, etapa, origem, paginas, motor, u));
            Passo = PassoNovoDespacho.Gravado;
            return e;
        }
        finally { Gravando = false; }
    }

    /// <summary>Gerar PDF: grava o PDF ao lado do DOCX e atualiza o recibo.</summary>
    public async Task<Entrega> GravarPdfAsync(string? motor, ContextoUsuario u)
    {
        if (Preparacao is not { } p || DocxGravado is not { } docx) throw new InvalidOperationException("O PDF ainda não foi gerado e conferido.");
        var paginas = Conferidas.ToList();
        Gravando = true;
        try
        {
            var e = await Task.Run(() => EmissaoDespacho.GravarPdf(p, docx, paginas, motor, u));
            Passo = PassoNovoDespacho.Gravado;
            return e;
        }
        finally { Gravando = false; }
    }

    /// <summary>Fecha sem gravar: interrompe o preparo e apaga a pasta de trabalho. Nada foi gravado na pasta do processo.</summary>
    public void Descartar()
    {
        Descartada = true;
        Interromper();
        if (!Preparando) ApagarTrabalho(); // com o preparo em curso, quem o espera apaga a pasta quando ele parar
    }

    /// <summary>O app fechando com o despacho aberto: interrompe o preparo e espera até <paramref name="espera"/> ele parar (o conversor
    /// encerra o soffice ao ver o cancelamento); se não parar, encerra já a árvore do soffice (síncrono), antes de o processo do app terminar.
    /// Depois apaga a pasta de trabalho (o que não sair agora sai na limpeza das órfãs).</summary>
    public void EncerrarAoFechar(TimeSpan espera)
    {
        Descartada = true;
        EncerramentoDoPreparo.Encerrar(tarefaDoPreparo, Interromper, espera, ApagarTrabalho);
    }

    void ApagarTrabalho()
    {
        var pasta = pastaTrabalho;
        pastaTrabalho = null;
        if (pasta is null) return;
        try { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* sai na limpeza das órfãs (mais de 1 dia) */ }
    }

    /// <summary>As páginas do PDF preparado, desenhadas pelo <c>Windows.Data.Pdf</c> a partir do arquivo da pasta de trabalho, com
    /// <paramref name="larguraPx"/> pixels de largura (600 DIP na escala do monitor). Na thread da interface (as imagens são dela).</summary>
    public async Task<IReadOnlyList<ImageSource>> PaginasAsync(uint larguraPx)
    {
        if (Preparacao is not { Pdf: { } pdf } || pastaTrabalho is null) return [];
        DesenhouPaginas = true;
        var caminho = Path.Combine(pastaTrabalho, "conferencia.pdf");
        await File.WriteAllBytesAsync(caminho, pdf);
        var arquivo = await StorageFile.GetFileFromPathAsync(caminho);
        var documento = await PdfDocument.LoadFromFileAsync(arquivo);
        var lista = new List<ImageSource>();
        for (uint i = 0; i < documento.PageCount; i++)
        {
            using var pagina = documento.GetPage(i);
            using var fluxo = new InMemoryRandomAccessStream();
            await pagina.RenderToStreamAsync(fluxo, new PdfPageRenderOptions { DestinationWidth = larguraPx });
            fluxo.Seek(0);
            var imagem = new BitmapImage();
            await imagem.SetSourceAsync(fluxo);
            lista.Add(imagem);
        }
        return lista;
    }
}
