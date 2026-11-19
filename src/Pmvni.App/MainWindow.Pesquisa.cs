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
    /// <summary>A pesquisa do módulo à vista vale (folhas com relatório aberto, listas, planilha, leis). A caixa em si fica sempre habilitada:
    /// a pesquisa geral (<see cref="IniciarPesquisaGeral"/>) vale em todo módulo.</summary>
    bool pesquisaDoModuloHabilitada;
    /// <summary>O alvo da pesquisa do módulo ("Pesquisar no relatório", "Pesquisar processos"…): vai na dica; o texto de espera é o da pesquisa geral.</summary>
    string dicaDoModulo = "Pesquisar no relatório";

    // ---------- acessores do roteiro ----------
    internal string TermoPesquisa => CaixaPesquisa.Text;
    internal string TextoResultadoPesquisa => TextoPesquisa.Text;
    internal int PesquisaTotal => pesquisaTotal;
    internal int PesquisaAtual => pesquisaAtual;
    internal string? PesquisaRef => pesquisaRef;
    /// <summary>A pesquisa do módulo vale (a caixa fica sempre habilitada para a pesquisa geral).</summary>
    internal bool CaixaPesquisaHabilitada => pesquisaDoModuloHabilitada;
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
            // A lista da pesquisa geral (ajustes, funções, arquivos, leis…) acompanha a digitação em qualquer módulo.
            AgendarPesquisaGeral();
            if (!pesquisaDoModuloHabilitada) return;
            // Processos e Despachos: filtro da lista, em memória (nada de esperar a digitação assentar).
            if (moduloAtual is Modulo.Processos or Modulo.Despachos) { AplicarTermoDeLista(CaixaPesquisa.Text); return; }
            // Acompanhamento (auditoria 2026-11-17): a pesquisa da barra é a busca da tabela (antes eram duas caixas).
            if (moduloAtual == Modulo.Acompanhamento) { Ac.CaixaBusca.Text = CaixaPesquisa.Text; return; }
            // Vazio limpa na hora; o resto espera a digitação assentar (cada letra não refaz a busca no relatório inteiro).
            if (CaixaPesquisa.Text.Length == 0) _ = TentarAsync(() => PesquisarAsync("", 0));
            else esperaPesquisa.Start();
        };
        CaixaPesquisa.KeyDown += AoTeclarNaPesquisa;
        BotaoPesquisaAnterior.Click += async (_, _) => await TentarAsync(() => PercorrerPaginasAsync(-1));
        BotaoPesquisaProxima.Click += async (_, _) => await TentarAsync(() => PercorrerPaginasAsync(1));
        Dicas.Aplicar(BotaoPesquisaAnterior, "Página anterior", "Volta para a página visitada anteriormente.");
        Dicas.Aplicar(BotaoPesquisaProxima, "Próxima página", "Avança pelo histórico de páginas.");
        PaginaDeAjustes.CategoriaMostrada += _ => RegistrarPaginaVisitada();
        manipuladores[ComandosApp.PesquisaFocar] = () =>
        {
            CaixaPesquisa.Focus(FocusState.Keyboard);
            CaixaPesquisa.SelectAll();
            if (CaixaPesquisa.Text.Trim().Length >= PesquisaGeral.MinimoDoTermo) AgendarPesquisaGeral(); // a lista volta com o termo que ficou
            return Task.CompletedTask;
        };
        IniciarPesquisaGeral();
    }

    async void AoTeclarNaPesquisa(object sender, KeyRoutedEventArgs e)
    {
        // Sem pesquisa no módulo (Ajustes, Imóveis, sites, Relatórios sem relatório), Enter só vale na lista da pesquisa geral.
        if (e.Key == VirtualKey.Enter && !pesquisaDoModuloHabilitada) { e.Handled = true; return; }
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
        if (moduloAtual == Modulo.Acompanhamento) { Ac.CaixaBusca.Text = termo; return; }
        // Legislação: "Nesta lei" (PDFFindController do leitor) ou "Em todas as leis" (índice do app), pelo menu "Pesquisar em".
        if (moduloAtual == Modulo.Legislacao) { await PesquisarNaLegislacaoAsync(termo, passo); return; }
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
        // Com Processos, Despachos ou o Acompanhamento à vista, a caixa é do filtro deles: não se mexe nela.
        if (moduloAtual is Modulo.Processos or Modulo.Despachos or Modulo.Acompanhamento) return;
        definindoPesquisa = true;
        try { CaixaPesquisa.Text = ""; }
        finally { definindoPesquisa = false; }
        MostrarResultadoDaPesquisa(0, 0, null, "");
    }

    void AtualizarPesquisaHabilitada(bool temRelatorio)
    {
        // Relatórios: o texto das folhas (precisa de um relatório aberto); Processos e Despachos: o filtro da lista; Legislação: a lei
        // aberta ou todas as leis; Ajustes: nada.
        var habilitada = moduloAtual switch { Modulo.Relatorios => temRelatorio, _ => TemPesquisaPropria(moduloAtual) };
        pesquisaDoModuloHabilitada = habilitada;
        // Pesquisa geral (2026-11-17): a caixa fica sempre à vista e habilitada (ajustes, funções, arquivos e leis valem em todo módulo).
        CaixaPesquisa.IsEnabled = true;
        var comPesquisa = TemPesquisaPropria(moduloAtual);
        CaixaPesquisa.Visibility = IconePesquisa.Visibility = Visibility.Visible;
        if (!comPesquisa) TextoPesquisa.Visibility = Visibility.Collapsed;
        AtualizarCabecalhoDoModulo();
        // Sem pesquisa no módulo, a dica diz o motivo (auditoria M6) e que a caixa ainda acha o resto do app.
        var motivo = habilitada ? "" : moduloAtual == Modulo.Relatorios ? "Abra um relatório para pesquisar nas folhas dele." : "";
        AplicarDicaDaPesquisa(motivo);
    }

    /// <summary>A dica da caixa de pesquisa no formato do app: o alvo ("Pesquisar no relatório") com Ctrl+F, o que ela faz no módulo e,
    /// desabilitada, o motivo (auditoria M6: também para o Narrador, no texto de ajuda).</summary>
    void AplicarDicaDaPesquisa(string? motivo, Modulo? modulo = null)
    {
        var m = modulo ?? moduloAtual;
        var doModulo = m switch
        {
            Modulo.Relatorios => "Procura também nas folhas do relatório; Enter vai ao próximo resultado.",
            Modulo.Processos => "Também filtra a lista de processos.",
            Modulo.Despachos => "Também filtra a lista de despachos.",
            Modulo.Acompanhamento => "Também filtra a tabela da planilha.",
            Modulo.Legislacao => "Procura também na lei aberta ou em todas, conforme \"Pesquisar em\".",
            _ => "Enter abre o primeiro resultado.",
        };
        const string geral = "Mostra ajustes, funções, processos, relatórios, despachos e leis com o termo; ↑↓ escolhem, Enter abre, Esc fecha.";
        Dicas.Aplicar(CaixaPesquisa, PesquisaGeral.NomeAcessivel, $"{geral} {doModulo}", "Ctrl+F / Ctrl+K", string.IsNullOrEmpty(motivo) ? null : motivo);
    }

    /// <summary>Os módulos com pesquisa própria na caixa (Relatórios só com um relatório aberto).</summary>
    static bool TemPesquisaPropria(Modulo m) => m is Modulo.Relatorios or Modulo.Processos or Modulo.Despachos or Modulo.Legislacao or Modulo.Acompanhamento;

    /// <summary>Troca o alvo da caixa de pesquisa com o módulo: a dica ("Pesquisar no relatório", "Pesquisar processos", "Pesquisar despachos") e o
    /// termo, que cada módulo guarda para si (voltar a um módulo traz o termo de antes).</summary>
    void TrocarPesquisaDeModulo(Modulo anterior, Modulo novo)
    {
        if (anterior != novo) termosPesquisa[anterior] = TemPesquisaPropria(anterior) ? CaixaPesquisa.Text : "";
        // O texto de espera é sempre o da pesquisa geral; o alvo do módulo fica guardado (dica e roteiro).
        dicaDoModulo = DicaDaPesquisaDe(novo);
        CaixaPesquisa.PlaceholderText = PesquisaGeral.Placeholder;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CaixaPesquisa, PesquisaGeral.NomeAcessivel);
        AplicarDicaDaPesquisa(null, novo); // o motivo (Relatórios sem relatório) vem de AtualizarPesquisaHabilitada
        esperaPesquisa?.Stop();
        FecharPesquisaGeral();
        definindoPesquisa = true;
        try { CaixaPesquisa.Text = TemPesquisaPropria(novo) ? TermoDe(novo) : ""; }
        finally { definindoPesquisa = false; }
        if (novo == Modulo.Relatorios) MostrarResultadoDaPesquisa(pesquisaTotal, pesquisaAtual, pesquisaRef, TermoDe(Modulo.Relatorios));
        else TextoPesquisa.Text = "";
    }

    /// <summary>A dica da caixa no módulo: na Legislação ela segue "Pesquisar em" ("Pesquisar nesta lei" / "Pesquisar em todas as leis").</summary>
    string DicaDaPesquisaDe(Modulo m) => m switch
    {
        Modulo.Processos => "Pesquisar processos",
        Modulo.Despachos => "Pesquisar despachos",
        Modulo.Acompanhamento => "Pesquisar na planilha",
        Modulo.Legislacao => escopoTodas || leiAberta is null ? "Pesquisar em todas as leis" : "Pesquisar nesta lei",
        _ => "Pesquisar no relatório",
    };
}
