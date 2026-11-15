using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Pmvni.App;

/// <summary>Resposta à pergunta de recuperação do diário (Tarefa 10 usa).</summary>
public enum RespostaRecuperacao { Recuperar, Descartar }

/// <summary>Tudo o que o app pergunta ao usuário (seletores de arquivo e diálogos). O roteiro troca <see cref="Perguntas.Atual"/>
/// por respostas fixas: nenhuma janela modal fica esperando quem não está olhando.</summary>
internal interface IPerguntas
{
    /// <summary>Seletor de abrir com o filtro <paramref name="extensao"/> (".json", ".pmvrel"); null se cancelado.</summary>
    Task<string?> AbrirArquivoAsync(string extensao);

    /// <summary>Seletor de salvar com o nome sugerido; null se cancelado.</summary>
    Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao);

    Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao);

    Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos);
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

    public async Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao)
    {
        var seletor = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, Janela);
        seletor.FileTypeChoices.Add(Tipos.TryGetValue(extensao, out var t) ? t : extensao, [extensao]);
        seletor.SuggestedFileName = Path.GetFileNameWithoutExtension(nomeSugerido);
        return (await seletor.PickSaveFileAsync())?.Path;
    }

    public async Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao) =>
        await Dialogo(titulo, texto, sim, nao).ShowAsync() == ContentDialogResult.Primary;

    public async Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos)
    {
        var quando = ultimo.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
        var texto = (comandos == 1 ? "Há 1 alteração" : $"Há {comandos} alterações")
            + $" que não chegou ao arquivo (a última em {quando}). Recuperar refaz essas alterações; descartar abre o arquivo como está gravado.";
        var r = await Dialogo("Recuperar alterações não gravadas?", texto, "Recuperar", "Descartar").ShowAsync();
        return r == ContentDialogResult.Primary ? RespostaRecuperacao.Recuperar : RespostaRecuperacao.Descartar;
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
