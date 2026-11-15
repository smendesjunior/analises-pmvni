using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Legislacao;
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
        return $"Versão do app: {versao}{Environment.NewLine}Modelo do relatório: rev{ModeloOficial.Revisao}{Environment.NewLine}WebView2: {webview}"
            + $"{Environment.NewLine}PDF.js {RecursosLegislacao.VersaoPdfJs} (Apache-2.0)";
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

    // ---------- legislação (desenho §11.1): Ajustes › Legislação ----------

    SessaoLegislacao legislacao = null!;
    CancellationTokenSource? cancelarAnexo;
    readonly CancellationTokenSource cancelarReindexacao = new();
    Task reindexacao = Task.CompletedTask;
    IReadOnlyList<LinhaAnexo> linhasLegislacao = [];
    readonly SemaphoreSlim releituraLegislacao = new(1, 1);
    readonly System.Collections.ObjectModel.ObservableCollection<LinhaAnexo> linhasNaTela = [];
    bool releituraAdiada;

    internal SessaoLegislacao Legislacao => legislacao;
    /// <summary>As linhas da seção Legislação dos Ajustes (uma por anexo), como estão na tela.</summary>
    internal IReadOnlyList<LinhaAnexo> LinhasLegislacaoAjustes => linhasLegislacao;
    internal InfoBar AvisoLegislacaoAjustes => PaginaDeAjustes.AvisoLegislacao;
    internal InfoBar AvisosAcervoAjustes => PaginaDeAjustes.AvisosAcervoLegislacao;
    internal string TotalLegislacaoAjustes => PaginaDeAjustes.TextoTotalLegislacao.Text;
    /// <summary>O índice que está sendo refeito em segundo plano (o roteiro espera antes de mexer na pasta).</summary>
    internal Task ReindexacaoLegislacao => reindexacao;

    void IniciarLegislacaoAjustes()
    {
        legislacao = new SessaoLegislacao(Path.Combine(PastaDados.Raiz, "legislacao"), CriarExtratorLegislacaoAsync);
        legislacao.Mudou += () => DispatcherQueue.TryEnqueue(() => _ = AtualizarListaLegislacaoAsync());
        var a = PaginaDeAjustes;
        a.ListaAnexos.ItemsSource = linhasNaTela;
        // Os manipuladores daqui são async void: nenhuma exceção sai deles (mensagem em português na InfoBar, detalhe no registro).
        a.BotaoAnexarLegislacao.Click += async (_, _) => await SemDerrubarAsync(AnexarLegislacaoAsync);
        a.BotaoCancelarLegislacao.Click += (_, _) => cancelarAnexo?.Cancel();
        a.TituloEditado = async (id, t) =>
        {
            try { return await RenomearAnexoAsync(id, t); }
            catch (Exception e) { var texto = Erro(e); MostrarAvisoLegislacao(InfoBarSeverity.Error, "Título não alterado", texto); return texto; }
        };
        a.TituloConcluido += async () => { if (releituraAdiada) await SemDerrubarAsync(AtualizarListaLegislacaoAsync); };
        a.EsferaEscolhida += async (id, e) => await SemDerrubarAsync(() => DefinirEsferaAnexoAsync(id, e));
        a.SubstituirPedido += async id => await SemDerrubarAsync(() => SubstituirAnexoAsync(id));
        a.ExcluirPedido += async id => await SemDerrubarAsync(() => ExcluirAnexoAsync(id));
        _ = AtualizarListaLegislacaoAsync();
    }

    /// <summary>O extrator real (PDF.js num WebView2 oculto e InPrivate sobre esta janela, no ambiente da tela), criado na thread da
    /// interface, de onde quer que a sessão o peça.</summary>
    Task<IExtratorPdf> CriarExtratorLegislacaoAsync()
    {
        var tcs = new TaskCompletionSource<IExtratorPdf>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (fechada) throw new InvalidOperationException("A janela foi fechada.");
                var ambiente = R.Navegador.CoreWebView2?.Environment ?? throw new InvalidOperationException("A visualização ainda não está pronta.");
                var janela = WinRT.Interop.WindowNative.GetWindowHandle(this);
                tcs.TrySetResult(await Pmvni.Composicao.Web.Legislacao.ExtratorPdfJs.CriarAsync(ambiente, janela, legislacao.Servidor));
            }
            catch (Exception e) { tcs.TrySetException(e); }
        })) tcs.TrySetException(new InvalidOperationException("A janela foi fechada."));
        return tcs.Task;
    }

    /// <summary>Ao abrir os Ajustes: apaga entradas órfãs (mais de 1 dia), mostra os avisos da carga e refaz em segundo plano os índices
    /// que faltam (selo "Preparando a pesquisa…").</summary>
    async Task EntrarNaLegislacaoDosAjustesAsync()
    {
        try
        {
            await Task.Run(legislacao.Acervo.LimparEntradasOrfas);
            await AtualizarListaLegislacaoAsync();
            if (reindexacao.IsCompleted && !legislacao.Acervo.SomenteLeitura)
                reindexacao = legislacao.ReindexarPendentesAsync(cancelarReindexacao.Token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MostrarAvisoLegislacao(InfoBarSeverity.Error, "Legislação", Erro(e)); }
    }

    /// <summary>Relê o acervo e redesenha a seção: total, linhas, só leitura e os avisos pendentes do acervo (lidos uma vez).</summary>
    internal async Task AtualizarListaLegislacaoAsync()
    {
        // Uma releitura por vez, na ordem: a que termina por último leu o acervo por último (quem espera esta vê o estado de agora).
        await releituraLegislacao.WaitAsync();
        try { await RelerLegislacaoAsync(); }
        catch (Exception e)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível ler a legislação anexada", Erro(e));
            ReabilitarLegislacao();
        }
        finally { releituraLegislacao.Release(); }
    }

    /// <summary>Roda uma ação da tela da legislação sem deixar exceção escapar (manipuladores async void).</summary>
    async Task SemDerrubarAsync(Func<Task> acao)
    {
        try { await acao(); }
        catch (Exception e) { MostrarAvisoLegislacao(InfoBarSeverity.Error, "A operação falhou", Erro(e)); }
    }

    void ReabilitarLegislacao()
    {
        if (fechada) return;
        var livre = !legislacao.Acervo.SomenteLeitura && cancelarAnexo is null;
        PaginaDeAjustes.BotaoAnexarLegislacao.IsEnabled = livre;
        PaginaDeAjustes.ConteudoAnexos.IsEnabled = livre;
    }

    /// <summary>Uma caixa de título das linhas está com o foco (o usuário está digitando).</summary>
    bool EditandoTituloDeAnexo() =>
        Raiz.XamlRoot is not null && FocusManager.GetFocusedElement(Raiz.XamlRoot) is TextBox { DataContext: LinhaAnexo };

    /// <summary>Atualiza as linhas na tela no lugar, por id: o que não mudou fica com o mesmo elemento (foco, rolagem e texto).</summary>
    void SincronizarLinhas(IReadOnlyList<LinhaAnexo> novas)
    {
        var col = linhasNaTela;
        var ids = novas.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = col.Count - 1; i >= 0; i--) if (!ids.Contains(col[i].Id)) col.RemoveAt(i);
        for (var i = 0; i < novas.Count; i++)
        {
            var j = -1;
            for (var k = i; k < col.Count; k++) if (col[k].Id == novas[i].Id) { j = k; break; }
            if (j < 0) { col.Insert(i, novas[i]); continue; }
            if (j != i) col.Move(j, i);
            if (col[i] != novas[i]) col[i] = novas[i];
        }
    }

    async Task RelerLegislacaoAsync()
    {
        IReadOnlyList<Anexo> anexos;
        IReadOnlyList<LinhaAnexo> linhas;
        try { (anexos, linhas) = await legislacao.ListarAsync(TimeZoneInfo.Local); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível ler a legislação anexada", Erro(e));
            return;
        }
        if (fechada) return;
        linhasLegislacao = linhas;
        var a = PaginaDeAjustes;
        // Com um título em edição, a tela espera o usuário sair da caixa (o texto digitado nunca se perde); os dados já estão novos.
        releituraAdiada = EditandoTituloDeAnexo();
        if (!releituraAdiada) SincronizarLinhas(linhas);
        a.TextoTotalLegislacao.Text = ListaLegislacao.Total(anexos);
        var somenteLeitura = legislacao.Acervo.SomenteLeitura;
        a.TextoSomenteLeituraLegislacao.Text = somenteLeitura ? MensagensAnexo.SomenteLeitura : "";
        a.TextoSomenteLeituraLegislacao.Visibility = somenteLeitura ? Visibility.Visible : Visibility.Collapsed;
        a.BotaoAnexarLegislacao.IsEnabled = !somenteLeitura && cancelarAnexo is null;
        a.ConteudoAnexos.IsEnabled = !somenteLeitura && cancelarAnexo is null;
        var avisos = legislacao.Acervo.LerAvisos();
        if (avisos.Count > 0)
        {
            a.AvisosAcervoLegislacao.Title = "Legislação";
            a.AvisosAcervoLegislacao.Message = string.Join("\n", avisos);
            a.AvisosAcervoLegislacao.IsOpen = true;
        }
        LinhasLegislacaoMudaram(anexos);
    }

    /// <summary>Gancho para o módulo Legislação acompanhar a lista (Tarefa 18).</summary>
    partial void LinhasLegislacaoMudaram(IReadOnlyList<Anexo> anexos);

    void MostrarAvisoLegislacao(InfoBarSeverity severidade, string titulo, string mensagem)
    {
        if (fechada) return;
        var aviso = PaginaDeAjustes.AvisoLegislacao;
        aviso.Severity = severidade;
        aviso.Title = titulo;
        aviso.Message = mensagem;
        aviso.IsOpen = true;
    }

    /// <summary>"Anexar PDFs…": o seletor (múltiplo, .pdf) na fila de comandos; a leitura dos arquivos (cópia, sha, PDF.js) corre fora
    /// dela, com a linha de progresso e "Cancelar". No fim, a InfoBar com o resumo exato (<see cref="ListaLegislacao.Resumo"/>), que
    /// também é devolvido ("" se nada foi escolhido ou outra anexação já corre).</summary>
    internal async Task<string> AnexarLegislacaoAsync()
    {
        IReadOnlyList<string> caminhos = [];
        CancellationTokenSource? cts = null;
        await NaFilaAsync(() => TentarAsync(async () =>
        {
            if (!PodeAlterarLegislacao()) return;
            caminhos = await Perguntas.Atual.AbrirArquivosAsync(".pdf");
            if (caminhos.Count == 0 || cancelarAnexo is not null) return;
            cts = cancelarAnexo = new CancellationTokenSource();
            ComecarProgressoLegislacao();
        }));
        if (cts is null) return "";
        try
        {
            var progresso = new Progress<ProgressoAnexo>(p => MostrarProgressoLegislacao("Anexando", p));
            var (anexados, recusas) = await legislacao.AnexarAsync(caminhos, progresso, cts.Token);
            var resumo = ListaLegislacao.Resumo(anexados.Count, recusas);
            var cancelado = cts.IsCancellationRequested;
            if (cancelado) resumo += "\nAnexação cancelada: o arquivo em andamento não entrou.";
            // Cancelar não é erro: aviso neutro (só as recusas pedem atenção).
            var severidade = recusas.Count > 0 ? InfoBarSeverity.Warning : cancelado ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
            MostrarAvisoLegislacao(severidade, "Anexar PDFs", resumo);
            return resumo;
        }
        catch (Exception e)
        {
            var texto = Erro(e);
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível anexar", texto);
            return texto;
        }
        finally
        {
            TerminarProgressoLegislacao(cts);
            await AtualizarListaLegislacaoAsync();
        }
    }

    /// <summary>"Excluir" de uma linha: confirma com os textos do desenho §11.1 e apaga a linha, a cópia e o índice (o original nunca).
    /// true se excluiu. Cópia presa por outro programa: excluída do catálogo e o aviso aparece.</summary>
    internal Task<bool> ExcluirAnexoAsync(string id) => NaFilaAsync(async () =>
    {
        if (!PodeAlterarLegislacao()) return false;
        Anexo? anexo;
        try { anexo = (await Task.Run(legislacao.Acervo.Listar)).FirstOrDefault(a => a.Id == id); }
        catch (Exception e)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível excluir", Erro(e));
            return false;
        }
        if (anexo is null) return false;
        var confirmou = await Perguntas.Atual.ConfirmarAsync($"Excluir “{anexo.Titulo}”?",
            $"A cópia do PDF guardada pelo editor e o seu índice de pesquisa serão apagados. O arquivo original ({anexo.ArquivoOriginal}) não é alterado.",
            "Excluir", "Cancelar");
        if (!confirmou) return false;
        try
        {
            var aviso = await legislacao.ExcluirAsync(id);
            if (aviso is not null) MostrarAvisoLegislacao(InfoBarSeverity.Warning, $"“{anexo.Titulo}” foi excluído", aviso);
            else MostrarAvisoLegislacao(InfoBarSeverity.Success, "Excluído", $"“{anexo.Titulo}” foi excluído. O arquivo original não foi alterado.");
            return true;
        }
        catch (Exception e)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível excluir", Erro(e));
            return false;
        }
        finally { await AtualizarListaLegislacaoAsync(); }
    });

    /// <summary>"Substituir arquivo…": o seletor na fila de comandos; a leitura do arquivo novo corre fora dela (com o progresso). Devolve a
    /// mensagem de recusa, ou null (substituído, ou nada escolhido).</summary>
    internal async Task<string?> SubstituirAnexoAsync(string id)
    {
        string? caminho = null;
        CancellationTokenSource? cts = null;
        await NaFilaAsync(() => TentarAsync(async () =>
        {
            if (!PodeAlterarLegislacao()) return;
            caminho = await Perguntas.Atual.AbrirArquivoAsync(".pdf");
            if (caminho is null || cancelarAnexo is not null) return;
            cts = cancelarAnexo = new CancellationTokenSource();
            ComecarProgressoLegislacao();
        }));
        if (cts is null || caminho is null) return null;
        try
        {
            var progresso = new Progress<ProgressoAnexo>(p => MostrarProgressoLegislacao("Substituindo pelo arquivo", p));
            var recusa = await legislacao.SubstituirAsync(id, caminho, cts.Token, progresso);
            if (recusa is not null) MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Substituir arquivo", recusa);
            else MostrarAvisoLegislacao(InfoBarSeverity.Success, "Substituir arquivo", $"O arquivo foi substituído por “{Path.GetFileName(caminho)}”.");
            return recusa;
        }
        catch (OperationCanceledException)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Informational, "Substituir arquivo", "Substituição cancelada: o arquivo anterior continua.");
            return null;
        }
        catch (Exception e)
        {
            var texto = Erro(e);
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível substituir", texto);
            return texto;
        }
        finally
        {
            TerminarProgressoLegislacao(cts);
            await AtualizarListaLegislacaoAsync();
        }
    }

    /// <summary>Título de uma linha (ao sair da caixa ou com Enter): devolve a mensagem de recusa ("O título não pode ficar vazio.",
    /// "O título pode ter até 200 caracteres.") ou null.</summary>
    internal Task<string?> RenomearAnexoAsync(string id, string titulo) => NaFilaAsync(async () =>
    {
        string? recusa = null;
        try { await legislacao.RenomearAsync(id, titulo); }
        catch (Exception e)
        {
            recusa = MotivoLegislacao(e);
            MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Título não alterado", recusa);
        }
        await AtualizarListaLegislacaoAsync();
        return recusa;
    });

    internal Task DefinirEsferaAnexoAsync(string id, Esfera e) => NaFilaAsync(async () =>
    {
        try { await legislacao.DefinirEsferaAsync(id, e); }
        catch (Exception x) { MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Esfera não alterada", MotivoLegislacao(x)); }
        await AtualizarListaLegislacaoAsync();
    });

    /// <summary>O motivo de uma alteração recusada, em português: as mensagens do acervo já são (título vazio, só leitura); o resto passa
    /// por <see cref="Erro"/> (o detalhe vai para o registro).</summary>
    string MotivoLegislacao(Exception e) => e switch
    {
        ArgumentException or InvalidOperationException => e.Message,
        KeyNotFoundException => "Esta lei não está mais anexada.",
        _ => Erro(e),
    };

    bool PodeAlterarLegislacao()
    {
        if (!legislacao.Acervo.SomenteLeitura) return true;
        MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Legislação só para leitura", MensagensAnexo.SomenteLeitura);
        return false;
    }

    void ComecarProgressoLegislacao()
    {
        var a = PaginaDeAjustes;
        a.AvisoLegislacao.IsOpen = false;
        a.TextoProgressoLegislacao.Text = "Preparando…";
        a.BarraProgressoLegislacao.IsIndeterminate = true;
        a.ProgressoLegislacao.Visibility = Visibility.Visible;
        a.BotaoAnexarLegislacao.IsEnabled = false;
        a.ConteudoAnexos.IsEnabled = false;
    }

    /// <summary>"Anexando “LEI 1.382-2020.pdf” — página 12 de 81".</summary>
    void MostrarProgressoLegislacao(string acao, ProgressoAnexo p)
    {
        if (fechada) return;
        var a = PaginaDeAjustes;
        a.TextoProgressoLegislacao.Text = p.Total > 0 ? $"{acao} “{p.Nome}” — página {p.Pagina} de {p.Total}" : $"{acao} “{p.Nome}”…";
        a.BarraProgressoLegislacao.IsIndeterminate = p.Total <= 0;
        if (p.Total > 0) { a.BarraProgressoLegislacao.Maximum = p.Total; a.BarraProgressoLegislacao.Value = p.Pagina; }
    }

    void TerminarProgressoLegislacao(CancellationTokenSource cts)
    {
        if (ReferenceEquals(cancelarAnexo, cts)) cancelarAnexo = null;
        cts.Dispose();
        if (fechada) return;
        PaginaDeAjustes.ProgressoLegislacao.Visibility = Visibility.Collapsed;
    }
}
