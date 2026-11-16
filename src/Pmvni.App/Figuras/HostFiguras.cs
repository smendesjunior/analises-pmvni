using Microsoft.UI.Xaml;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O que o editor de figura e a janela de recorte de prancha precisam da janela principal. Todo comando passa pela fila da janela
/// principal (<see cref="AplicarFiguraAsync"/>): testa, aplica, registra no histórico e recompõe as folhas, como os comandos de item.</summary>
internal interface IHostFiguras
{
    /// <summary>O relatório aberto (null = fechado: as janelas de figura se fecham).</summary>
    SessaoEdicao? Sessao { get; }

    /// <summary>Aplica o comando; devolve os motivos da recusa (vazio = aplicado ou sem efeito).</summary>
    Task<IReadOnlyList<string>> AplicarFiguraAsync(Comando c);

    Task DesfazerFiguraAsync();

    Task RefazerFiguraAsync();

    /// <summary>O tema da janela principal (as janelas de figura acompanham).</summary>
    ElementTheme TemaAtual { get; }

    /// <summary>Abre o editor de marcações da figura.</summary>
    void AbrirEditorFigura(string figura);

    /// <summary>Abre a janela "Inserir região da prancha"; a figura nova entra depois de <paramref name="depoisDe"/> (null = no fim).</summary>
    void AbrirRecortePrancha(string? depoisDe);
}
