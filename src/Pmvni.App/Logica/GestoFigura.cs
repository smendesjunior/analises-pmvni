using Pmvni.Composicao.Figuras;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Um gesto de mouse. Concluir consome o estado antes de liberar a captura do ponteiro;
/// a notificação de perda de captura pode cancelar depois, sem apagar o comando já concluído.</summary>
public sealed class GestoFigura
{
    MedidasFigura? medidas;
    MarcaDesenho? original;
    AlcaFigura alca;
    public Ponto Inicio { get; private set; }
    public MarcaDesenho? Atual { get; private set; }
    public bool NovaRegiao { get; private set; }
    public bool Ativo => medidas is not null;

    public void IniciarRegiao(MedidasFigura m, Ponto inicio)
    {
        Cancelar();
        medidas = m;
        Inicio = inicio;
        NovaRegiao = true;
    }

    public void IniciarArrasto(MedidasFigura m, MarcaDesenho marca, AlcaFigura parte, Ponto inicio)
    {
        Cancelar();
        medidas = m;
        original = Atual = marca;
        alca = parte;
        Inicio = inicio;
    }

    public void Mover(Ponto ponto)
    {
        if (medidas is { } m && original is { } x)
            Atual = InteracaoFigura.Arrastar(m, x, alca, Inicio, ponto);
    }

    public Comando? Concluir(string figura, Ponto ponto)
    {
        var m = medidas;
        var x = original;
        var parte = alca;
        var inicio = Inicio;
        var nova = NovaRegiao;
        Cancelar();
        if (m is null) return null;
        if (nova)
        {
            // Clique sem arrastar (ou arrasto mínimo): região padrão em volta do clique. Antes não fazia nada e parecia que o botão não funcionava.
            var caixa = InteracaoFigura.CaixaDoArrasto(m, inicio, ponto);
            if (caixa[2] - caixa[0] < .005 || caixa[3] - caixa[1] < .005) caixa = InteracaoFigura.CaixaDoClique(m, inicio);
            return caixa is null ? null : new IncluirMarcacao(figura, Marcacao.NovoId(), TiposMarcacao.Regiao, Caixa: caixa);
        }
        return x is null ? null : InteracaoFigura.ComandoDoArrasto(figura, x,
            InteracaoFigura.Arrastar(m, x, parte, inicio, ponto), parte);
    }

    public void Cancelar()
    {
        medidas = null;
        original = Atual = null;
        NovaRegiao = false;
        alca = AlcaFigura.Nenhuma;
    }
}
