using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Ajustes, tema, Recentes e Comparar com o original (Tarefa 11; desenho §5.5, §8.5 e §9): a página Ajustes (tema, intervalo do
/// salvamento automático, pasta de recuperação, versões), o menu Exibir › Tema, o menu Arquivo › Recentes (só o que ainda existe) e a
/// janela "Comparar com o original". As escolhas ficam em <c>ajustes.json</c> (<see cref="Ajustes"/>); nada disso entra no pacote.</summary>
public sealed partial class MainWindow
{
    bool atualizandoAjustes;
    bool haRecentes;

    // ---------- acessores do roteiro ----------
    internal string TemaDosAjustes => ajustes.Tema;
    internal ElementTheme TemaAtual => Raiz.ActualTheme;
    internal IReadOnlyList<string> RecentesNosAjustes => ajustes.Recentes.ToList();
    internal PaginaAjustes AjustesPagina => PaginaDeAjustes;
    internal bool PaginaAjustesVisivel => PaginaDeAjustes.Visibility == Visibility.Visible;
    internal int IntervaloAutosaveAtual => ajustes.IntervaloAutosaveS;
    /// <summary>O tema marcado na página Ajustes (<c>sistema</c>, <c>claro</c> ou <c>escuro</c>).</summary>
    internal string? TemaMarcadoNaPagina => PaginaDeAjustes.OpcoesTema.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    void IniciarAjustes()
    {
        foreach (var s in Ajustes.Intervalos) PaginaDeAjustes.CaixaIntervalo.Items.Add(new ComboBoxItem { Content = s + " segundos", Tag = s });
        foreach (var opcao in PaginaDeAjustes.OpcoesTema.Children.OfType<RadioButton>())
            opcao.Click += (o, _) => { if (((RadioButton)o).Tag is string tema) EscolherTema(tema); };
        PaginaDeAjustes.CaixaIntervalo.SelectionChanged += (_, _) =>
        {
            if (atualizandoAjustes || PaginaDeAjustes.CaixaIntervalo.SelectedItem is not ComboBoxItem { Tag: int s } || s == ajustes.IntervaloAutosaveS) return;
            DefinirIntervaloAutosave(s);
            ReiniciarAutosave();
        };
        Faixa.ItensDinamicos = ItensDoMenuDaFaixa;
        Faixa.ItemDinamicoEscolhido += async (menu, valor) =>
        {
            if (menu == ComandosApp.ArquivoRecentes) await AbrirRecenteAsync(valor);
            else if (menu == ComandosApp.ProcessosAno) await DefinirAnoAsync(valor);
        };
        foreach (var t in Ajustes.Temas)
        {
            var tema = t;
            manipuladores["exibir.tema." + tema] = () => { EscolherTema(tema); return Task.CompletedTask; };
        }
        manipuladores[ComandosApp.RevisaoComparar] = CompararAsync;
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) AtualizarRecentes(); };
        PaginaDeAjustes.TextoPastaRecuperacao.Text = Path.Combine(PastaDados.Raiz, "recuperacao");
        PaginaDeAjustes.TextoVersoes.Text = TextoDasVersoes();
        SincronizarAjustes();
        AtualizarRecentes();
    }

    // ---------- tema ----------

    /// <summary>Exibir › Tema e a página Ajustes: guarda a escolha, aplica ao app (as folhas continuam brancas) e marca o item em uso.</summary>
    internal void EscolherTema(string tema)
    {
        if (!Ajustes.Temas.Contains(tema)) throw new ArgumentOutOfRangeException(nameof(tema), tema, "Tema desconhecido.");
        ajustes.Tema = tema;
        SalvarAjustes();
        AplicarTemaDosAjustes();
        SincronizarAjustes();
    }

    /// <summary>Põe a faixa e a página Ajustes de acordo com os ajustes (tema marcado, intervalo escolhido).</summary>
    void SincronizarAjustes()
    {
        atualizandoAjustes = true;
        try
        {
            foreach (var t in Ajustes.Temas) Faixa.MarcarItem("exibir.tema." + t, t == ajustes.Tema);
            foreach (var opcao in PaginaDeAjustes.OpcoesTema.Children.OfType<RadioButton>())
                opcao.IsChecked = opcao.Tag as string == ajustes.Tema;
            foreach (var item in PaginaDeAjustes.CaixaIntervalo.Items.OfType<ComboBoxItem>())
                if (item.Tag is int s && s == ajustes.IntervaloAutosaveS) PaginaDeAjustes.CaixaIntervalo.SelectedItem = item;
        }
        finally { atualizandoAjustes = false; }
    }

    string TextoDasVersoes()
    {
        var versao = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "?";
        var webview = R.Navegador.CoreWebView2?.Environment.BrowserVersionString ?? FabricaWebView2.VersaoDisponivel() ?? "ausente";
        return $"Versão do app: {versao}{Environment.NewLine}Modelo do relatório: rev{ModeloOficial.Revisao}{Environment.NewLine}WebView2: {webview}";
    }

    // ---------- recentes ----------

    /// <summary>O arquivo que acabou de ser aberto ou gravado vai para o topo dos Recentes (guardados nos ajustes, no máximo 10).</summary>
    void RegistrarRecente(string caminho)
    {
        ajustes.AdicionarRecente(Path.GetFullPath(caminho));
        SalvarAjustes();
        AtualizarRecentes();
    }

    /// <summary>Confere quais recentes ainda existem (só o que existe aparece; nada fica desabilitado) e habilita o menu se houver algum.</summary>
    void AtualizarRecentes()
    {
        if (fechada) return;
        haRecentes = ajustes.RecentesExistentes().Count > 0;
        AtualizarComandos();
    }

    IReadOnlyList<(string Texto, string Valor, string Dica)> ItensDoMenuDaFaixa(string menu)
    {
        if (menu == ComandosApp.ProcessosAno) return ItensDoMenuAno();
        if (menu != ComandosApp.ArquivoRecentes) return [];
        var lista = ajustes.RecentesExistentes();
        haRecentes = lista.Count > 0;
        return lista.Select(c =>
        {
            var pasta = Path.GetFileName(Path.GetDirectoryName(c)) ?? "";
            return (pasta.Length == 0 ? Path.GetFileName(c) : Path.GetFileName(c) + "  ·  " + pasta, c, c);
        }).ToList();
    }

    /// <summary>Arquivo › Recentes: abre o relatório escolhido (na fila de comandos, como Abrir); se ele sumiu, avisa.</summary>
    internal Task AbrirRecenteAsync(string caminho) => NaFilaAsync(() => TentarAsync(async () =>
    {
        if (!File.Exists(caminho))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "O arquivo não existe mais", Path.GetFileName(caminho) + " não está mais em " + Path.GetDirectoryName(caminho) + ".");
            AtualizarRecentes();
            return;
        }
        await AbrirPacoteAsync(caminho);
    }));

    // ---------- comparar com o original ----------

    /// <summary>O texto de "Comparar com o original" para o relatório aberto (o mesmo que a janela mostra).</summary>
    internal string TextoComparacao()
    {
        if (sessao?.Atual is null) return "Nenhum relatório aberto.";
        var p = sessao.Revisao.Projeto;
        var h = sessao.Revisao.HistoricoCompleto;
        return Comparacao.Texto(Comparacao.Diferencas(p, h), Comparacao.CitacoesImportadas(p, h));
    }

    Task CompararAsync() => Perguntas.Atual.InformarAsync("Comparar com o original", TextoComparacao());
}
