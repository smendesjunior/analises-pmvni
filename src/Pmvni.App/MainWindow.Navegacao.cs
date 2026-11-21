using Pmvni.Revisao;

namespace Pmvni.App;

public sealed partial class MainWindow
{
    readonly record struct PaginaVisitada(Modulo Modulo, bool NovoRelatorio, TipoDespacho? Despacho, string? Categoria);
    readonly List<PaginaVisitada> paginasVisitadas = [];
    int paginaVisitada = -1;
    bool percorrendoPaginas;

    PaginaVisitada DestinoAtual() => new(moduloAtual, moduloAtual == Modulo.Relatorios && novoRelatorioAberto,
        moduloAtual == Modulo.Despachos ? novo?.Tipo : null, moduloAtual == Modulo.Ajustes ? PaginaDeAjustes.CategoriaAtual : null);

    void RegistrarPaginaVisitada()
    {
        if (percorrendoPaginas || fechada) return;
        var destino = DestinoAtual();
        if (paginaVisitada >= 0 && paginasVisitadas[paginaVisitada] == destino) return;
        paginasVisitadas.RemoveRange(paginaVisitada + 1, paginasVisitadas.Count - paginaVisitada - 1);
        paginasVisitadas.Add(destino);
        paginaVisitada = paginasVisitadas.Count - 1;
        AtualizarSetasNavegacao();
    }

    void AtualizarSetasNavegacao()
    {
        BotaoPesquisaAnterior.IsEnabled = !percorrendoPaginas && paginaVisitada > 0;
        BotaoPesquisaProxima.IsEnabled = !percorrendoPaginas && paginaVisitada + 1 < paginasVisitadas.Count;
        Dicas.Aplicar(BotaoPesquisaAnterior, "Página anterior", "Volta para a página visitada anteriormente.",
            motivo: MotivoNavegacao(BotaoPesquisaAnterior.IsEnabled, "Não há uma página anterior no histórico."));
        Dicas.Aplicar(BotaoPesquisaProxima, "Próxima página", "Avança pelo histórico de páginas.",
            motivo: MotivoNavegacao(BotaoPesquisaProxima.IsEnabled, "Não há uma página seguinte no histórico."));
    }

    string? MotivoNavegacao(bool habilitado, string motivo) =>
        habilitado ? null : percorrendoPaginas ? "Aguarde a navegação terminar." : motivo;

    async Task PercorrerPaginasAsync(int direcao)
    {
        int indice = paginaVisitada + direcao;
        if (percorrendoPaginas || indice < 0 || indice >= paginasVisitadas.Count) return;
        var destino = paginasVisitadas[indice];
        percorrendoPaginas = true;
        AtualizarSetasNavegacao();
        try
        {
            await MudarModuloAsync(destino.Modulo);
            if (moduloAtual != destino.Modulo) return;
            if (destino.Modulo == Modulo.Relatorios && destino.NovoRelatorio != novoRelatorioAberto)
            {
                if (destino.NovoRelatorio) await NovoRelatorioAsync();
                else FecharPaginaNovoRelatorio();
            }
            if (destino.Modulo == Modulo.Despachos && destino.Despacho != novo?.Tipo)
            {
                if (destino.Despacho is { } tipo) await NovoDespachoAsync(tipo);
                else await SairDoNovoDespachoAsync();
            }
            if (destino.Categoria is { } categoria) PaginaDeAjustes.MostrarCategoria(categoria);
            if (DestinoAtual() == destino) paginaVisitada = indice;
        }
        finally
        {
            percorrendoPaginas = false;
            AtualizarSetasNavegacao();
        }
    }
}
