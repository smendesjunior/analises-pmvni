using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Figuras (Etapa 4, especificação §9): a figura selecionada nas folhas (clique; duplo clique abre o editor), a aba Figuras da
/// faixa (Inserir região da prancha, Editar figura, Excluir figura) e as janelas do editor de marcações e do recorte de prancha. Os
/// comandos de figura passam pela mesma fila e pelo mesmo fluxo dos comandos de item (<see cref="AplicarAsync"/>).</summary>
public sealed partial class MainWindow : IHostFiguras
{
    string? figuraSelecionada;
    JanelaFigura? janelaFigura;
    JanelaPrancha? janelaPrancha;

    // ---------- acessores do roteiro ----------
    internal JanelaFigura? EditorDeFigura => janelaFigura;
    internal FrameworkElement? ConteudoEditorFigura => janelaFigura?.Content as FrameworkElement;
    internal JanelaPrancha? JanelaDeRecorte => janelaPrancha;
    internal string? FiguraSelecionada => figuraSelecionada;
    internal bool EditorFiguraAberto => janelaFigura is not null;
    internal void FecharEditorFigura() => janelaFigura?.Close();
    internal bool RecortePranchaAberto => janelaPrancha is not null;
    internal void FecharRecortePrancha() => janelaPrancha?.Close();

    void RegistrarManipuladoresFiguras()
    {
        manipuladores[ComandosApp.FigurasEditar] = () =>
        {
            if (figuraSelecionada is { } f) AbrirEditorFigura(f);
            else MostrarMensagem(InfoBarSeverity.Informational, "Editar figura", "Clique numa figura da seção IMAGENS DO PROJETO (ou dê um duplo clique nela).");
            return Task.CompletedTask;
        };
        manipuladores[ComandosApp.FigurasInserirRegiao] = () => { AbrirRecortePrancha(figuraSelecionada); return Task.CompletedTask; };
        manipuladores[ComandosApp.FigurasExcluir] = () => figuraSelecionada is { } f ? AplicarAsync(new ExcluirFigura(f)) : Task.CompletedTask;
        Closed += (_, _) =>
        {
            janelaFigura?.Close();
            janelaPrancha?.Close();
        };
    }

    void AtualizarComandosFiguras(bool temRelatorio)
    {
        if (figuraSelecionada is not null && FiguraDoRef(figuraSelecionada) is null) figuraSelecionada = null;
        var editavel = temRelatorio && Editavel;
        Faixa.Habilitar(ComandosApp.FigurasInserirRegiao, editavel);
        Faixa.Habilitar(ComandosApp.FigurasEditar, temRelatorio && figuraSelecionada is not null);
        Faixa.Habilitar(ComandosApp.FigurasExcluir, editavel && figuraSelecionada is not null);
        // Com relatório editável o menu sempre tem ao menos "Adicionar figura…".
        Faixa.Habilitar(ComandosApp.FigurasAbrirEditor, temRelatorio && (editavel || sessao?.Revisao.Projeto is { } p && FigurasDoMenu(p).Count > 0));
    }

    /// <summary>As figuras incluídas, na ordem do relatório: (id, "Figura N — título").</summary>
    static IReadOnlyList<(string Id, string Texto)> FigurasDoMenu(Projeto p) =>
        MenuFiguras.Itens((p.Figuras ?? CamadaFiguras.Inicial(p)).Where(f => f.Incluida).Select(f => (f.Id, (string?)f.TextoBase("titulo"))).ToList());

    /// <summary>Os itens de Página Inicial › Figuras ▾ (correções 2026-11-16): cada figura incluída; a selecionada nas folhas vem marcada.</summary>
    IReadOnlyList<(string Texto, string Valor, string Dica)> ItensDoMenuFiguras()
    {
        if (sessao?.Atual is null) return [];
        var itens = FigurasDoMenu(sessao.Revisao.Projeto)
            .Select(f => ((f.Id == figuraSelecionada ? "✓ " : "") + f.Texto, f.Id, "Abre esta figura no editor de figuras."))
            .ToList();
        // No fim do menu (pedido do usuário, 2026-11-17): adicionar uma figura nova abrindo o PDF do projeto (a mesma janela de
        // "Inserir região da prancha"); entra depois da figura selecionada, ou no fim.
        if (Editavel) itens.Add(("＋ Adicionar figura…", ValorAdicionarFigura, "Abre o PDF do projeto para recortar uma região e inseri-la como figura nova."));
        return itens;
    }

    /// <summary>Valor do item "Adicionar figura…" no menu Figuras (não é id de figura).</summary>
    const string ValorAdicionarFigura = "\u0001adicionar";

    /// <summary>O id da figura incluída com esse data-ref (null se o ref não é de figura).</summary>
    string? FiguraDoRef(string? @ref)
    {
        if (@ref is null || sessao is not { } s) return null;
        var p = s.Revisao.Projeto;
        return (p.Figuras ?? CamadaFiguras.Inicial(p)).Any(f => f.Id == @ref && f.Incluida) ? @ref : null;
    }

    // ---------- IHostFiguras ----------

    SessaoEdicao? IHostFiguras.Sessao => sessao;

    ElementTheme IHostFiguras.TemaAtual => Raiz.ActualTheme;

    async Task<IReadOnlyList<string>> IHostFiguras.AplicarFiguraAsync(Comando c)
    {
        var origem = sessao;
        return await NaFilaAsync<IReadOnlyList<string>>(async () =>
        {
            if (origem is null || sessao != origem) return new[] { "O relatório aberto foi alterado. Abra novamente o editor de figuras." };
            if (ocupado) return new[] { "Outra operação está em andamento. Tente novamente em seguida." };
            await AplicarAsync(c);
            // Captura o resultado ainda dentro da fila, antes do próximo comando limpar os erros.
            return ultimosErros.ToList();
        });
    }

    Task IHostFiguras.DesfazerFiguraAsync() => HistoricoFiguraAsync(false);

    Task IHostFiguras.RefazerFiguraAsync() => HistoricoFiguraAsync(true);

    Task HistoricoFiguraAsync(bool refazer)
    {
        var origem = sessao;
        return NaFilaAsync(async () =>
        {
            if (origem is null || origem != sessao) return;
            await ExecutarRevisaoAsync(null, s => refazer ? s.Revisao.Refazer() : s.Revisao.Desfazer());
        });
    }

    /// <summary>Abre (ou traz para a frente) o editor de marcações da figura.</summary>
    public void AbrirEditorFigura(string figura)
    {
        if (sessao is not { Atual: not null } s) return;
        if (janelaFigura is { } aberta && aberta.PertenceASessao(s) && aberta.Figura == figura) { aberta.Activate(); return; }
        janelaFigura?.Close();
        try
        {
            var j = new JanelaFigura(this, s, figura);
            j.Closed += (_, _) => { if (ReferenceEquals(janelaFigura, j)) janelaFigura = null; };
            janelaFigura = j;
            j.Activate();
        }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível abrir o editor da figura", Erro(e)); }
    }

    /// <summary>Abre a janela "Inserir região da prancha".</summary>
    public void AbrirRecortePrancha(string? depoisDe)
    {
        if (sessao is not { Atual: not null } s) { AvisarIndisponivel(ComandosApp.FigurasInserirRegiao); return; }
        if (s.SomenteLeitura) { MostrarMensagem(InfoBarSeverity.Informational, "Inserir região da prancha", "O relatório está aberto somente para leitura."); return; }
        if (janelaPrancha is { } aberta && aberta.PertenceASessao(s)) { aberta.Activate(); return; }
        janelaPrancha?.Close();
        try
        {
            var j = new JanelaPrancha(this, s, depoisDe);
            j.Closed += (_, _) => { if (ReferenceEquals(janelaPrancha, j)) janelaPrancha = null; };
            janelaPrancha = j;
            j.Activate();
        }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível abrir a prancha", Erro(e)); }
    }
}
