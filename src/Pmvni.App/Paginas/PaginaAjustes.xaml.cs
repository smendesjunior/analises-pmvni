using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Página do módulo Ajustes (desenho §9): só a parte visual. Quem a preenche e reage às escolhas é a janela
/// (<c>MainWindow.Ajustes.cs</c>), pelos elementos <c>internal</c> e pelos eventos das linhas da legislação. É criada uma vez e
/// trocada por Visibility.</summary>
public sealed partial class PaginaAjustes : UserControl
{
    public PaginaAjustes() => InitializeComponent();

    /// <summary>Título digitado numa linha (ao sair da caixa ou com Enter): devolve a mensagem de recusa, ou null se valeu.</summary>
    internal Func<string, string, Task<string?>>? TituloEditado { get; set; }

    /// <summary>O usuário saiu de uma caixa de título (aplicada ou não): a janela refaz a tela adiada durante a edição.</summary>
    internal event Action? TituloConcluido;

    /// <summary>Esfera escolhida numa linha (id, esfera).</summary>
    internal event Action<string, Esfera>? EsferaEscolhida;

    /// <summary>"Substituir arquivo…" de uma linha (id).</summary>
    internal event Action<string>? SubstituirPedido;

    /// <summary>"Excluir" de uma linha (id).</summary>
    internal event Action<string>? ExcluirPedido;

    public static int IndiceDaEsfera(Esfera e) => Esferas.Ordem.ToList().IndexOf(e);

    public static Visibility VisivelSe(string? texto) => string.IsNullOrEmpty(texto) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A linha de um controle da lista: o ItemsRepeater põe o item no DataContext do elemento (e dos filhos).</summary>
    static LinhaAnexo? LinhaDe(object sender) => (sender as FrameworkElement)?.DataContext as LinhaAnexo;

    async void AoSairDoTitulo(object sender, RoutedEventArgs e)
    {
        await AplicarTituloAsync(sender as TextBox);
        TituloConcluido?.Invoke();
    }

    async void AoTeclarNoTitulo(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await AplicarTituloAsync(sender as TextBox);
    }

    async Task AplicarTituloAsync(TextBox? caixa)
    {
        if (caixa is null || LinhaDe(caixa) is not { } linha || TituloEditado is null) return;
        if (caixa.Text.Trim() == linha.Titulo) return;
        string? recusa;
        try { recusa = await TituloEditado(linha.Id, caixa.Text); }
        catch (Exception) { recusa = ""; } // a janela já mostrou o motivo; a caixa volta ao título anterior
        // Recusado: a caixa volta ao título anterior (a mensagem aparece na InfoBar da seção).
        if (recusa is not null && ReferenceEquals(LinhaDe(caixa), linha)) caixa.Text = linha.Titulo;
    }

    void AoMudarEsfera(object sender, SelectionChangedEventArgs e)
    {
        // A recarga da linha (ItemsRepeater reciclando o elemento) também muda a seleção: só vale o que difere do item atual.
        if (sender is not ComboBox { SelectedIndex: >= 0 } c || LinhaDe(c) is not { } linha) return;
        var esfera = Esferas.Ordem[c.SelectedIndex];
        if (esfera != linha.Esfera) EsferaEscolhida?.Invoke(linha.Id, esfera);
    }

    void AoSubstituir(object sender, RoutedEventArgs e) { if (LinhaDe(sender) is { } l) SubstituirPedido?.Invoke(l.Id); }

    void AoExcluir(object sender, RoutedEventArgs e) { if (LinhaDe(sender) is { } l) ExcluirPedido?.Invoke(l.Id); }
}
