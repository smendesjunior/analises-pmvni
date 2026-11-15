using Pmvni.Composicao.Kit;
using Pmvni.Revisao;

namespace Pmvni.Composicao.Web;

/// <summary>O que um <see cref="Projeto"/> vira para a tela e para a exportação. <see cref="Dados"/>: o dados.json efetivo (sha256 do
/// recibo); <see cref="Render"/>: em rascunho (a tela abre com campos [ ] em aberto), com os refs dos cards trocados pelos ids
/// estáveis dos itens; <see cref="ErrosFinais"/>: a conferência do kit sem rascunho mais a da revisão (vazia = pode exportar);
/// <see cref="AvisosExportacao"/>: citações a itens excluídos, que só avisam.</summary>
public sealed record Preparado(byte[] Dados, string CaminhoDados, ResultadoRender Render, ConfigFolhas Config,
    IReadOnlyList<string> ErrosFinais, IReadOnlyList<string> AvisosExportacao);

/// <summary>A edição produziu um relatório que a conferência do kit não aceita nem em rascunho (<see cref="Erros"/>): a tela não muda.</summary>
public sealed class RecomposicaoRecusadaException(IReadOnlyList<string> erros)
    : InvalidOperationException("Recomposição recusada pela conferência do kit: " + string.Join(" ", erros))
{
    public IReadOnlyList<string> Erros { get; } = erros;
}

/// <summary>Projeto → relatório: gera o dados.json efetivo, renderiza com o kit (em rascunho, com os ids de sessão dos itens) e
/// compõe as folhas num <see cref="HostOculto"/>. Dono da <see cref="PastaTrabalho"/> do pacote aberto (as imagens do <c>documento</c>).</summary>
public sealed class Recompositor : IDisposable
{
    static readonly OpcoesRender Rascunho = new(false, true);
    static readonly OpcoesRender Final = new(false, false);

    readonly ModeloCarregado modelo;
    readonly Pmvni.Composicao.Web.PastaTrabalho pasta;

    public Recompositor(ConteudoPacote pacote, ModeloCarregado m)
    {
        ArgumentNullException.ThrowIfNull(pacote);
        modelo = m ?? throw new ArgumentNullException(nameof(m));
        pasta = Pmvni.Composicao.Web.PastaTrabalho.Criar(pacote);
    }

    /// <summary>Pasta de trabalho do pacote (<c>&lt;dados&gt;\edicao\&lt;guid&gt;</c>).</summary>
    public string PastaTrabalho => pasta.Caminho;

    /// <summary>Gera o dados.json efetivo de <paramref name="p"/> e o renderiza. Se o kit recusa até em rascunho (por exemplo um campo
    /// vazio), lança <see cref="RecomposicaoRecusadaException"/>. Não muda <paramref name="p"/>; não usa o WebView2.</summary>
    public Preparado Preparar(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var gerado = DadosEfetivos.Gerar(p);
        var caminho = pasta.CaminhoDados;
        var render = RelatorioRenderer.Renderizar(gerado.Json, caminho, Rascunho, modelo);
        if (!render.Ok) throw new RecomposicaoRecusadaException(render.Erros);
        render = DadosEfetivos.RefsDeSessao(render, gerado.IdsDosItens);

        var erros = new List<string>(RelatorioRenderer.Renderizar(gerado.Json, caminho, Final, modelo).Erros);
        erros.AddRange(ConferenciaRevisao.Erros(p));
        // A conferência do kit e a da revisão podem repetir uma mensagem: cada uma aparece uma vez.
        return new Preparado(gerado.Json, caminho, render, ConfigFolhas.PadraoDeBytes(modelo, gerado.Json), erros.Distinct(StringComparer.Ordinal).ToList(),
            ConferenciaRevisao.CitacoesAExcluidos(p));
    }

    /// <summary>Carrega o render no documento do <paramref name="host"/> (de composição: fixa a mídia de impressão) e compõe as folhas.</summary>
    public async Task<ResultadoComposicao> ComporAsync(Preparado e, HostOculto host)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(host);
        await host.Documento.CarregarAsync(e.Render, e.Config);
        return await host.Documento.ComporDetalhadoAsync();
    }

    public void Dispose() => pasta.Dispose();
}
