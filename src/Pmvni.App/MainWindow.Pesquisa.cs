using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;

namespace Pmvni.App;

/// <summary>Pesquisa no relatório (Tarefa 11; desenho §5.5): caixa na barra de título ("Pesquisar no relatório"), "3 de 12" ao lado,
/// Enter = próxima, Shift+Enter = anterior, Esc limpa e devolve o foco às folhas, Ctrl+F foca a caixa. O realce e a rolagem são da página
/// (<c>pesquisar</c>/<c>pesquisado</c>, CSS Custom Highlight API: nada entra no DOM das folhas nem no PDF). Todo pedido passa pela
/// fila de comandos; depois de cada recomposição a pesquisa é refeita sobre as folhas novas, sem mudar a ocorrência atual.</summary>
public sealed partial class MainWindow
{
    static readonly TimeSpan EsperaDigitacao = TimeSpan.FromMilliseconds(300);

    DispatcherQueueTimer esperaPesquisa = null!;
    bool definindoPesquisa;
    int pesquisaTotal, pesquisaAtual;
    string? pesquisaRef;

    // ---------- acessores do roteiro ----------
    internal string TermoPesquisa => CaixaPesquisa.Text;
    internal string TextoResultadoPesquisa => TextoPesquisa.Text;
    internal int PesquisaTotal => pesquisaTotal;
    internal int PesquisaAtual => pesquisaAtual;
    internal string? PesquisaRef => pesquisaRef;
    internal bool CaixaPesquisaHabilitada => CaixaPesquisa.IsEnabled;
    internal bool CaixaPesquisaComFoco => Raiz.XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(Raiz.XamlRoot), CaixaPesquisa);

    void IniciarPesquisa()
    {
        esperaPesquisa = DispatcherQueue.GetForCurrentThread().CreateTimer();
        esperaPesquisa.Interval = EsperaDigitacao;
        esperaPesquisa.IsRepeating = false;
        esperaPesquisa.Tick += async (_, _) =>
        {
            esperaPesquisa.Stop();
            await TentarAsync(() => PesquisarAsync(CaixaPesquisa.Text, 0));
        };
        CaixaPesquisa.TextChanged += (_, _) =>
        {
            if (definindoPesquisa) return;
            esperaPesquisa.Stop();
            // Processos e Despachos: filtro da lista, em memória (nada de esperar a digitação assentar).
            if (moduloAtual is Modulo.Processos or Modulo.Despachos) { AplicarTermoDeLista(CaixaPesquisa.Text); return; }
            // Vazio limpa na hora; o resto espera a digitação assentar (cada letra não refaz a busca no relatório inteiro).
            if (CaixaPesquisa.Text.Length == 0) _ = TentarAsync(() => PesquisarAsync("", 0));
            else esperaPesquisa.Start();
        };
        CaixaPesquisa.KeyDown += AoTeclarNaPesquisa;
        BotaoPesquisaAnterior.Click += async (_, _) => await TentarAsync(() => IrNaPesquisaAsync(-1));
        BotaoPesquisaProxima.Click += async (_, _) => await TentarAsync(() => IrNaPesquisaAsync(+1));
        manipuladores[ComandosApp.PesquisaFocar] = () =>
        {
            CaixaPesquisa.Focus(FocusState.Keyboard);
            CaixaPesquisa.SelectAll();
            return Task.CompletedTask;
        };
    }

    async void AoTeclarNaPesquisa(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
            await TentarAsync(() => IrNaPesquisaAsync(shift ? -1 : 1));
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            esperaPesquisa.Stop();
            if (CaixaPesquisa.Text.Length > 0) CaixaPesquisa.Text = ""; // o TextChanged limpa o realce
            if (sessao?.Atual is not null && moduloAtual == Modulo.Relatorios) R.Navegador.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>Enter / Shift+Enter e os botões: a ocorrência seguinte (+1) ou anterior (-1) do termo da caixa.</summary>
    internal Task IrNaPesquisaAsync(int passo)
    {
        esperaPesquisa.Stop();
        return PesquisarAsync(CaixaPesquisa.Text, passo);
    }

    /// <summary>Escreve o termo na caixa (como se digitado, sem esperar a digitação assentar) e pesquisa; <paramref name="passo"/> 0 = a
    /// primeira ocorrência. É a rota do roteiro.</summary>
    internal async Task DigitarPesquisaAsync(string termo)
    {
        esperaPesquisa.Stop();
        definindoPesquisa = true;
        try { CaixaPesquisa.Text = termo; }
        finally { definindoPesquisa = false; }
        if (moduloAtual is Modulo.Processos or Modulo.Despachos) { AplicarTermoDeLista(termo); return; }
        termosPesquisa[moduloAtual] = termo;
        await PesquisarAsync(termo, 0);
    }

    /// <summary>Pede à página a pesquisa de <paramref name="termo"/> (vazio limpa) e mostra "N de M". Na fila de comandos.
    /// <paramref name="manter"/> (só com passo 0): a ocorrência (1-based) que continua a atual depois de recompor, sem rolar.</summary>
    internal Task PesquisarAsync(string termo, int passo, int? manter = null) => NaFilaAsync(async () =>
    {
        // Processos e Despachos filtram as listas (a pesquisa nas folhas não tem o que fazer aqui): só confere o texto "N de M".
        if (moduloAtual is Modulo.Processos or Modulo.Despachos) { AtualizarTextoDaPesquisaDeLista(); return; }
        if (documento is null || sessao?.Atual is null || moduloAtual != Modulo.Relatorios)
        {
            MostrarResultadoDaPesquisa(0, 0, null, termo);
            return;
        }
        var d = manter is { } m
            ? await documento.ChamarAsync("pesquisar", new { termo, passo, atual = m }, LimiteTela)
            : await documento.ChamarAsync("pesquisar", new { termo, passo }, LimiteTela);
        var total = d.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
        var atual = d.TryGetProperty("atual", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : 0;
        var @ref = d.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        MostrarResultadoDaPesquisa(total, atual, @ref, termo);
    });

    void MostrarResultadoDaPesquisa(int total, int atual, string? @ref, string termo)
    {
        if (fechada) return;
        pesquisaTotal = total;
        pesquisaAtual = atual;
        pesquisaRef = @ref;
        TextoPesquisa.Text = termo.Length == 0 ? "" : total == 0 ? "Nenhum resultado" : $"{atual.ToString(CultureInfo.InvariantCulture)} de {total.ToString(CultureInfo.InvariantCulture)}";
        BotaoPesquisaAnterior.IsEnabled = BotaoPesquisaProxima.IsEnabled = total > 0;
    }

    /// <summary>Depois de recompor, as folhas são outras: refaz o realce com o termo da caixa (a ocorrência atual não muda e a tela não rola).</summary>
    void ReaplicarPesquisa()
    {
        if (moduloAtual != Modulo.Relatorios || CaixaPesquisa.Text.Length == 0 || sessao?.Atual is null) return;
        var atual = pesquisaAtual;
        _ = TentarAsync(() => PesquisarAsync(CaixaPesquisa.Text, 0, atual > 0 ? atual : null));
    }

    /// <summary>Relatório fechado: a caixa e o resultado voltam a vazio.</summary>
    void LimparPesquisa()
    {
        esperaPesquisa?.Stop();
        termosPesquisa[Modulo.Relatorios] = "";
        pesquisaTotal = pesquisaAtual = 0;
        pesquisaRef = null;
        // Com Processos ou Despachos à vista, a caixa é do filtro deles: não se mexe nela.
        if (moduloAtual is Modulo.Processos or Modulo.Despachos) return;
        definindoPesquisa = true;
        try { CaixaPesquisa.Text = ""; }
        finally { definindoPesquisa = false; }
        MostrarResultadoDaPesquisa(0, 0, null, "");
    }

    void AtualizarPesquisaHabilitada(bool temRelatorio)
    {
        // Relatórios: o texto das folhas (precisa de um relatório aberto); Processos e Despachos: o filtro da lista; Ajustes: nada.
        var habilitada = moduloAtual switch { Modulo.Relatorios => temRelatorio, Modulo.Processos or Modulo.Despachos => true, _ => false };
        CaixaPesquisa.IsEnabled = habilitada;
        // Anterior e próxima só andam pelas ocorrências nas folhas.
        BotaoPesquisaAnterior.IsEnabled = BotaoPesquisaProxima.IsEnabled = habilitada && moduloAtual == Modulo.Relatorios && pesquisaTotal > 0;
    }

    /// <summary>Troca o alvo da caixa de pesquisa com o módulo: a dica ("Pesquisar no relatório", "Pesquisar processos", "Pesquisar despachos") e o
    /// termo, que cada módulo guarda para si (voltar a um módulo traz o termo de antes).</summary>
    void TrocarPesquisaDeModulo(Modulo anterior, Modulo novo)
    {
        if (anterior != novo) termosPesquisa[anterior] = CaixaPesquisa.Text;
        var dica = novo switch { Modulo.Processos => "Pesquisar processos", Modulo.Despachos => "Pesquisar despachos", _ => "Pesquisar no relatório" };
        CaixaPesquisa.PlaceholderText = dica;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CaixaPesquisa, dica);
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(CaixaPesquisa, dica + " (Ctrl+F)");
        esperaPesquisa?.Stop();
        definindoPesquisa = true;
        try { CaixaPesquisa.Text = novo == Modulo.Ajustes ? "" : TermoDe(novo); }
        finally { definindoPesquisa = false; }
        if (novo == Modulo.Relatorios) MostrarResultadoDaPesquisa(pesquisaTotal, pesquisaAtual, pesquisaRef, TermoDe(Modulo.Relatorios));
        else TextoPesquisa.Text = "";
    }
}
