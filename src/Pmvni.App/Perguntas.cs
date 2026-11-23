using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Pmvni.App;

/// <summary>Resposta à pergunta de recuperação do diário (Tarefa 10 usa).</summary>
public enum RespostaRecuperacao { Recuperar, Descartar }

/// <summary>Resposta à pergunta de fechar quando a gravação falhou (Tarefa 10).</summary>
public enum RespostaFechar { SalvarComo, SalvarCopia, FecharSemSalvar, Cancelar }

/// <summary>Tudo o que o app pergunta ao usuário (seletores de arquivo e diálogos). O roteiro troca <see cref="Perguntas.Atual"/>
/// por respostas fixas: nenhuma janela modal fica esperando quem não está olhando.</summary>
internal interface IPerguntas
{
    /// <summary>Seletor de abrir com o filtro <paramref name="extensao"/> (".json", ".rpmvni"; várias separadas por ";"); null se cancelado.</summary>
    Task<string?> AbrirArquivoAsync(string extensao);

    /// <summary>Seletor de abrir com seleção múltipla e qualquer tipo de arquivo (os anexos do Novo relatório); null se cancelado.</summary>
    Task<IReadOnlyList<string>?> AbrirArquivosAsync();

    /// <summary>Seletor de abrir com seleção múltipla e o filtro <paramref name="extensao"/> (".pdf": "Anexar PDFs…" da legislação),
    /// na ordem em que vieram; cancelado ⇒ lista vazia.</summary>
    Task<IReadOnlyList<string>> AbrirArquivosAsync(string extensao);

    /// <summary>"Criar processo": número, ano do protocolo e requerente ("Criar" só com os três válidos); null se cancelado.</summary>
    Task<DadosNovoProcesso?> PedirNovoProcessoAsync();

    /// <summary>"Criar processo" com o requerente já preenchido (o boletim cadastral sugere o contribuinte). Padrão: a pergunta sem sugestão.</summary>
    Task<DadosNovoProcesso?> PedirNovoProcessoAsync(string requerente) => PedirNovoProcessoAsync();

    /// <summary>Seletor de salvar com o nome sugerido; null se cancelado.</summary>
    Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao);

    /// <summary>Seletor de pasta (a pasta de processos nos Ajustes); null se cancelado.</summary>
    Task<string?> EscolherPastaAsync();

    /// <summary>Pede um texto curto (nome de pasta etc.); null = cancelou.</summary>
    /// <summary><paramref name="botao"/>: o texto do botão principal ("Criar" por padrão; "Responder" numa decisão da análise sem opções).</summary>
    Task<string?> PedirTextoAsync(string titulo, string rotulo, string sugestao, string botao = "Criar");

    /// <summary>Confirmação de dois botões. <paramref name="destrutiva"/> (descartar, excluir, cancelar, fechar sem salvar): Enter escolhe "não";
    /// nas outras (consentimento, "Exportar", "Emitir mesmo assim") Enter aciona o botão principal (decisão do controlador sobre o FD-I3).</summary>
    Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao, bool destrutiva = true);

    /// <summary>Mostra um texto só para leitura (ex.: a Conferência), com o botão Fechar.</summary>
    Task InformarAsync(string titulo, string texto);

    Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos);

    /// <summary>Fechar com alterações que não puderam ser gravadas (<paramref name="motivo"/>): "Salvar como…", "Fechar sem salvar (a
    /// recuperação fica guardada)" ou "Cancelar". Em <paramref name="conflito"/>: "Salvar como cópia" (principal), "Fechar sem salvar"
    /// (fica na pasta de recuperação, sem ser oferecida de novo) ou "Cancelar".</summary>
    Task<RespostaFechar> FecharSemGravarAsync(string motivo, bool conflito);

    /// <summary>Fechar com um campo em edição no A4 cujo texto a conferência recusou (<paramref name="motivo"/>): "Voltar à edição" (o
    /// padrão; também Esc) ou "Fechar sem ele". true = fechar sem o texto.</summary>
    Task<bool> FecharSemOTextoAsync(string motivo);

    /// <summary>A senha do certificado digital, numa caixa de senha (os caracteres não aparecem), com a opção "Lembrar neste computador"
    /// (marcada conforme <paramref name="lembrar"/>); null = cancelou. Quem não está na frente da tela (o roteiro) não tem senha a dar:
    /// o padrão é cancelar.</summary>
    Task<RespostaSenha?> PedirSenhaAsync(string titulo, string texto, bool lembrar) => Task.FromResult<RespostaSenha?>(null);

    /// <summary>Escolhe um item de uma lista curta (os certificados do Windows); o índice escolhido, ou null se cancelou. Padrão: cancelar.</summary>
    Task<int?> EscolherNaListaAsync(string titulo, string texto, IReadOnlyList<string> itens) => Task.FromResult<int?>(null);

    /// <summary>Como a outra, já com o item <paramref name="selecionado"/> marcado (Enter confirma) e o texto do botão principal;
    /// <paramref name="botao"/> vazio = sem botão principal, o clique no item escolhe.</summary>
    Task<int?> EscolherNaListaAsync(string titulo, string texto, IReadOnlyList<string> itens, int selecionado, string botao, string secundario = "") => EscolherNaListaAsync(titulo, texto, itens);

    /// <summary>Tarefa 14: qual versão da peça vale (decisão do tipo <c>pecas</c>), com as versões lado a lado, o motivo da dúvida e "Abrir o PDF" em cada
    /// uma; o <see cref="Pmvni.Agentes.PecaCandidata.Valor"/> escolhido, ou null se cancelou. Padrão: cancelar (a análise fica parada, sem gastar nada).</summary>
    Task<string?> EscolherPecaAsync(string titulo, string motivo, string aviso, IReadOnlyList<Pmvni.Agentes.PecaCandidata> versoes) => Task.FromResult<string?>(null);
}

internal static class Perguntas
{
    /// <summary>As perguntas em uso (a janela define as suas na criação; o roteiro troca).</summary>
    public static IPerguntas Atual { get; set; } = null!;
}

/// <summary>Perguntas de verdade: seletores do Windows ligados à janela e <see cref="ContentDialog"/> no XamlRoot dela.</summary>
internal sealed class PerguntasDaJanela(Window janela, Func<XamlRoot?> raiz) : IPerguntas
{
    static readonly IReadOnlyDictionary<string, string> Tipos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "Dados do relatório (dados.json)",
        [".rpmvni"] = "Relatório em revisão (.rpmvni)",
        [".pmvrel"] = "Relatório em revisão, formato antigo (.pmvrel)",
        [".pdf"] = "PDF",
    };

    IntPtr Janela => WinRT.Interop.WindowNative.GetWindowHandle(janela);

    public async Task<string?> AbrirArquivoAsync(string extensao)
    {
        var seletor = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, Janela);
        foreach (var e in extensao.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) seletor.FileTypeFilter.Add(e);
        return (await seletor.PickSingleFileAsync())?.Path;
    }

    public async Task<IReadOnlyList<string>?> AbrirArquivosAsync()
    {
        var seletor = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, Janela);
        seletor.FileTypeFilter.Add("*");
        var arquivos = await seletor.PickMultipleFilesAsync();
        if (arquivos is null || arquivos.Count == 0) return null;
        return arquivos.Select(a => a.Path).Where(c => !string.IsNullOrEmpty(c)).ToList();
    }

    public async Task<IReadOnlyList<string>> AbrirArquivosAsync(string extensao)
    {
        var seletor = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, Janela);
        seletor.FileTypeFilter.Add(extensao);
        var arquivos = await seletor.PickMultipleFilesAsync();
        if (arquivos is null || arquivos.Count == 0) return [];
        return arquivos.Select(a => a.Path).Where(c => !string.IsNullOrEmpty(c)).ToList();
    }

    public Task<DadosNovoProcesso?> PedirNovoProcessoAsync() => PedirNovoProcessoAsync("");

    public async Task<DadosNovoProcesso?> PedirNovoProcessoAsync(string sugestao)
    {
        var anoAtual = DateTime.Now.Year;
        var numero = new TextBox { Header = "Número do processo", PlaceholderText = "ex.: 123" };
        var ano = new TextBox { Header = "Ano do protocolo", Text = anoAtual.ToString(CultureInfo.InvariantCulture), MaxLength = 4, InputScope = Numerico() };
        var requerente = new TextBox { Header = "Requerente", PlaceholderText = "Nome do requerente", Text = sugestao ?? "" };
        var painel = new StackPanel { Spacing = 10, MinWidth = 360 };
        painel.Children.Add(numero);
        painel.Children.Add(ano);
        painel.Children.Add(requerente);
        painel.Children.Add(new TextBlock
        {
            Text = "A pasta é criada na pasta de processos, no ano do protocolo, com DOCUMENTOS e PROJETO\\INICIAL. Se o processo já existe, a pasta dele é usada.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"],
        });
        var d = new ContentDialog
        {
            XamlRoot = raiz(),
            Title = "Criar processo",
            Content = painel,
            PrimaryButtonText = "Criar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        // "Criar" desabilitado diz por quê (auditoria de textos).
        var motivo = new TextBlock { Text = "Preencha o número, o ano (4 dígitos) e o requerente para criar.", TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] };
        painel.Children.Add(motivo);
        void Conferir()
        {
            d.IsPrimaryButtonEnabled = FluxoNovoRelatorio.LerNovoProcesso(numero.Text, ano.Text, requerente.Text, anoAtual) is not null;
            motivo.Visibility = d.IsPrimaryButtonEnabled ? Visibility.Collapsed : Visibility.Visible;
        }
        numero.TextChanged += (_, _) => Conferir();
        ano.TextChanged += (_, _) => Conferir();
        requerente.TextChanged += (_, _) => Conferir();
        if (await Mostrar(d) != ContentDialogResult.Primary) return null;
        return FluxoNovoRelatorio.LerNovoProcesso(numero.Text, ano.Text, requerente.Text, anoAtual);
    }

    static Microsoft.UI.Xaml.Input.InputScope Numerico()
    {
        var escopo = new Microsoft.UI.Xaml.Input.InputScope();
        escopo.Names.Add(new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number));
        return escopo;
    }

    public async Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao)
    {
        var seletor = new Microsoft.Windows.Storage.Pickers.FileSavePicker(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(Janela));
        seletor.SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        seletor.FileTypeChoices.Add(Tipos.TryGetValue(extensao, out var t) ? t : extensao, [extensao.ToUpperInvariant()]);
        seletor.SuggestedFileName = Path.GetFileNameWithoutExtension(nomeSugerido).ToUpperInvariant();
        var escolhido = await seletor.PickSaveFileAsync();
        return escolhido is null ? null : ArquivoGerado.Caminho(escolhido.Path);
    }

    public async Task<string?> EscolherPastaAsync()
    {
        var seletor = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, Janela);
        seletor.FileTypeFilter.Add("*");
        return (await seletor.PickSingleFolderAsync())?.Path;
    }

    /// <summary>Destrutiva (descartar, cancelar a análise, excluir, fechar): Enter escolhe "não" (Ruling FD-I3), nunca a ação. Não destrutiva
    /// (consentimento, exportar, emitir): Enter aciona o botão principal.</summary>
    public async Task<string?> PedirTextoAsync(string titulo, string rotulo, string sugestao, string botao = "Criar")
    {
        var caixa = new TextBox { Header = rotulo, Text = sugestao, MinWidth = 360 };
        var d = new ContentDialog
        {
            XamlRoot = raiz(), Title = titulo, Content = caixa, PrimaryButtonText = botao, CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
        };
        caixa.Loaded += (_, _) => caixa.SelectAll();
        return await Mostrar(d) == ContentDialogResult.Primary ? caixa.Text : null;
    }

    /// <summary>A caixa de senha do certificado (2026-11-16): <see cref="PasswordBox"/> (nunca mostra o que foi digitado, sem botão de
    /// revelar) e a caixa "Lembrar neste computador". "Assinar" só com alguma senha digitada.</summary>
    public async Task<RespostaSenha?> PedirSenhaAsync(string titulo, string texto, bool lembrar)
    {
        var senha = new PasswordBox { Header = "Senha", PasswordRevealMode = PasswordRevealMode.Hidden, MinWidth = 360 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(senha, "Senha do certificado");
        var caixa = new CheckBox { Content = "Lembrar neste computador (guardada cifrada, só para este usuário do Windows)", IsChecked = lembrar };
        var painel = new StackPanel { Spacing = 12 };
        painel.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        painel.Children.Add(senha);
        painel.Children.Add(caixa);
        var d = new ContentDialog
        {
            XamlRoot = raiz(), Title = titulo, Content = painel, PrimaryButtonText = "Assinar", CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false,
        };
        senha.PasswordChanged += (_, _) => d.IsPrimaryButtonEnabled = senha.Password.Length > 0;
        senha.Loaded += (_, _) => senha.Focus(FocusState.Programmatic);
        var ok = await Mostrar(d) == ContentDialogResult.Primary && senha.Password.Length > 0;
        var resposta = ok ? new RespostaSenha(senha.Password, caixa.IsChecked == true) : null;
        senha.Password = ""; // a caixa não fica com a senha depois de fechada
        return resposta;
    }

    public Task<int?> EscolherNaListaAsync(string titulo, string texto, IReadOnlyList<string> itens) => EscolherNaListaAsync(titulo, texto, itens, -1, "Usar este");

    public async Task<int?> EscolherNaListaAsync(string titulo, string texto, IReadOnlyList<string> itens, int selecionado, string botao, string secundario = "")
    {
        var lista = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320 };
        foreach (var i in itens) lista.Items.Add(new TextBlock { Text = i, TextWrapping = TextWrapping.Wrap });
        var painel = new StackPanel { Spacing = 12, MinWidth = 420 };
        painel.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap });
        painel.Children.Add(lista);
        var d = new ContentDialog
        {
            XamlRoot = raiz(), Title = titulo, Content = painel, PrimaryButtonText = botao, SecondaryButtonText = secundario, CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false,
        };
        lista.SelectionChanged += (_, _) => d.IsPrimaryButtonEnabled = lista.SelectedIndex >= 0;
        if (selecionado >= 0 && selecionado < itens.Count) lista.SelectedIndex = selecionado;
        if (botao.Length == 0)
        {
            // Sem botão principal (2026-11-17, pedido do usuário): clicar no item já é a escolha; só "Cancelar" fica embaixo.
            d.PrimaryButtonText = "";
            int? clicado = null;
            lista.SelectionMode = ListViewSelectionMode.None;
            lista.IsItemClickEnabled = true;
            lista.ItemClick += (_, e) => { clicado = lista.Items.IndexOf(e.ClickedItem); d.Hide(); };
            await Mostrar(d);
            return clicado is >= 0 ? clicado : null;
        }
        var resultado = await Mostrar(d);
        if (resultado == ContentDialogResult.Secondary) return itens.Count;
        return resultado == ContentDialogResult.Primary && lista.SelectedIndex >= 0 ? lista.SelectedIndex : null;
    }

    public async Task<string?> EscolherPecaAsync(string titulo, string motivo, string aviso, IReadOnlyList<Pmvni.Agentes.PecaCandidata> versoes)
    {
        var escolha = new EscolhaDePecas(motivo, aviso, versoes);
        var d = new ContentDialog
        {
            XamlRoot = raiz(), Title = titulo, Content = escolha, PrimaryButtonText = "Usar esta versão", CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false,
        };
        d.Resources["ContentDialogMaxWidth"] = 820d;
        escolha.EscolhaMudou += () => d.IsPrimaryButtonEnabled = escolha.Escolhida is not null;
        return await Mostrar(d) == ContentDialogResult.Primary ? escolha.Escolhida?.Valor : null;
    }

    public async Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao, bool destrutiva = true)
    {
        var d = Dialogo(titulo, texto, sim, nao);
        d.DefaultButton = destrutiva ? ContentDialogButton.Close : ContentDialogButton.Primary;
        return await Mostrar(d) == ContentDialogResult.Primary;
    }

    /// <summary>Um diálogo de cada vez (o WinUI recusa dois ContentDialog abertos): quem chega com outro aberto espera a vez em vez de falhar.</summary>
    readonly SemaphoreSlim umDialogo = new(1, 1);

    async Task<ContentDialogResult> Mostrar(ContentDialog d)
    {
        await umDialogo.WaitAsync();
        try
        {
            if (raiz() is not { } atual) return ContentDialogResult.None;
            d.XamlRoot = atual;
            return await d.ShowAsync();
        }
        finally { umDialogo.Release(); }
    }

    public async Task InformarAsync(string titulo, string texto)
    {
        var d = new ContentDialog
        {
            XamlRoot = raiz(),
            Title = titulo,
            Content = new ScrollViewer { MaxHeight = 480, Content = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
            CloseButtonText = "Fechar",
        };
        await Mostrar(d);
    }

    public async Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos)
    {
        var quando = ultimo.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", Formatos.Cultura);
        var texto = (comandos == 1 ? "Há 1 alteração" : $"Há {comandos} alterações")
            + (comandos == 1 ? " que não chegou" : " que não chegaram") + $" ao arquivo (a última em {quando}). Recuperar refaz essas alterações; Descartar abre o arquivo como está gravado e guarda essas alterações na pasta de recuperação.";
        // Auditoria de acessibilidade: Esc (ou fechar o diálogo sem querer) não descarta; só o botão "Descartar" descarta.
        var d = Dialogo("Recuperar alterações não gravadas?", texto, "Recuperar", "Descartar");
        d.CloseButtonText = "";
        d.SecondaryButtonText = "Descartar";
        d.DefaultButton = ContentDialogButton.Primary;
        var r = await Mostrar(d);
        return r == ContentDialogResult.Secondary ? RespostaRecuperacao.Descartar : RespostaRecuperacao.Recuperar;
    }

    public async Task<RespostaFechar> FecharSemGravarAsync(string motivo, bool conflito)
    {
        var d = Dialogo("As alterações não foram gravadas", TextosGravacao.FecharSemGravar(motivo, conflito),
            conflito ? "Salvar como cópia" : "Salvar como…", "Cancelar");
        d.SecondaryButtonText = conflito ? "Fechar sem salvar (fica na pasta de recuperação)" : "Fechar sem salvar (a recuperação fica guardada)";
        return await Mostrar(d) switch
        {
            ContentDialogResult.Primary => conflito ? RespostaFechar.SalvarCopia : RespostaFechar.SalvarComo,
            ContentDialogResult.Secondary => RespostaFechar.FecharSemSalvar,
            _ => RespostaFechar.Cancelar,
        };
    }

    public async Task<bool> FecharSemOTextoAsync(string motivo)
    {
        var d = Dialogo("O texto em edição não foi aplicado", TextosGravacao.FecharSemOTexto(motivo), "Voltar à edição", "");
        d.SecondaryButtonText = "Fechar sem ele";
        return await Mostrar(d) == ContentDialogResult.Secondary; // Esc e o resto voltam à edição: o texto nunca se perde sem pedir
    }

    ContentDialog Dialogo(string titulo, string texto, string sim, string nao) => new()
    {
        XamlRoot = raiz(),
        Title = titulo,
        Content = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap },
        PrimaryButtonText = sim,
        CloseButtonText = nao,
        DefaultButton = ContentDialogButton.Primary,
    };
}
