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
    internal string? FiguraSelecionada => figuraSelecionada;
    internal bool EditorFiguraAberto => janelaFigura is not null;
    internal void FecharEditorFigura() => janelaFigura?.Close();

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
    }

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
        await AplicarAsync(c);
        return ultimosErros.ToList();
    }

    Task IHostFiguras.DesfazerFiguraAsync() => ExecutarRevisaoAsync(null, s => s.Revisao.Desfazer());

    Task IHostFiguras.RefazerFiguraAsync() => ExecutarRevisaoAsync(null, s => s.Revisao.Refazer());

    /// <summary>Abre (ou traz para a frente) o editor de marcações da figura.</summary>
    public void AbrirEditorFigura(string figura)
    {
        if (sessao is not { Atual: not null } s) return;
        if (janelaFigura is { } aberta && aberta.Figura == figura) { aberta.Activate(); return; }
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
        if (janelaPrancha is { } aberta) { aberta.Activate(); return; }
        var j = new JanelaPrancha(this, s, depoisDe);
        j.Closed += (_, _) => { if (ReferenceEquals(janelaPrancha, j)) janelaPrancha = null; };
        janelaPrancha = j;
        j.Activate();
    }
}
