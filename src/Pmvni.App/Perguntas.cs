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
    /// <summary>Seletor de abrir com o filtro <paramref name="extensao"/> (".json", ".pmvrel"); null se cancelado.</summary>
    Task<string?> AbrirArquivoAsync(string extensao);

    /// <summary>Seletor de abrir com seleção múltipla e qualquer tipo de arquivo (os anexos do Novo relatório); null se cancelado.</summary>
    Task<IReadOnlyList<string>?> AbrirArquivosAsync();

    /// <summary>Seletor de abrir com seleção múltipla e o filtro <paramref name="extensao"/> (".pdf": "Anexar PDFs…" da legislação),
    /// na ordem em que vieram; cancelado ⇒ lista vazia.</summary>
    Task<IReadOnlyList<string>> AbrirArquivosAsync(string extensao);

    /// <summary>"Criar processo": número, ano do protocolo e requerente ("Criar" só com os três válidos); null se cancelado.</summary>
    Task<DadosNovoProcesso?> PedirNovoProcessoAsync();

    /// <summary>Seletor de salvar com o nome sugerido; null se cancelado.</summary>
    Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao);

    /// <summary>Seletor de pasta (a pasta de processos nos Ajustes); null se cancelado.</summary>
    Task<string?> EscolherPastaAsync();

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
        [".pmvrel"] = "Relatório em revisão (.pmvrel)",
        [".pdf"] = "PDF",
    };

    IntPtr Janela => WinRT.Interop.WindowNative.GetWindowHandle(janela);

    public async Task<string?> AbrirArquivoAsync(string extensao)
    {
        var seletor = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, Janela);
        seletor.FileTypeFilter.Add(extensao);
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

    public async Task<DadosNovoProcesso?> PedirNovoProcessoAsync()
    {
        var anoAtual = DateTime.Now.Year;
        var numero = new TextBox { Header = "Número do processo", PlaceholderText = "ex.: 123", MaxLength = 6, InputScope = Numerico() };
        var ano = new TextBox { Header = "Ano do protocolo", Text = anoAtual.ToString(CultureInfo.InvariantCulture), MaxLength = 4, InputScope = Numerico() };
        var requerente = new TextBox { Header = "Requerente", PlaceholderText = "Nome do requerente" };
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
        void Conferir() => d.IsPrimaryButtonEnabled = FluxoNovoRelatorio.LerNovoProcesso(numero.Text, ano.Text, requerente.Text, anoAtual) is not null;
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
        var seletor = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, Janela);
        seletor.FileTypeChoices.Add(Tipos.TryGetValue(extensao, out var t) ? t : extensao, [extensao]);
        seletor.SuggestedFileName = Path.GetFileNameWithoutExtension(nomeSugerido);
        return (await seletor.PickSaveFileAsync())?.Path;
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
        try { return await d.ShowAsync(); }
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
            + $" que não chegou ao arquivo (a última em {quando}). Recuperar refaz essas alterações; descartar abre o arquivo como está gravado.";
        var r = await Mostrar(Dialogo("Recuperar alterações não gravadas?", texto, "Recuperar", "Descartar"));
        return r == ContentDialogResult.Primary ? RespostaRecuperacao.Recuperar : RespostaRecuperacao.Descartar;
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
