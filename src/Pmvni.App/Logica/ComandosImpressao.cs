namespace Pmvni.App;

/// <summary>Como um arquivo é impresso: na janela de impressão do app (o visualizador do WebView2, com a lista de impressoras e
/// "Salvar como PDF" do Edge), pelo programa padrão do Windows (verbo "print" do shell, depois de confirmar) ou não é impresso.</summary>
public enum ModoImpressao { Nenhum, Visualizador, ProgramaDoWindows }

/// <summary>Imprimir (pedido de 2026-11-17: "botão de imprimir, listando as impressoras e a opção de gerar PDF, na barra de opções"): o
/// comando <see cref="ComandosApp.ArquivoImprimir"/> de cada módulo que mostra um documento, quando ele fica habilitado e o motivo de
/// quando não fica (a dica do botão desabilitado). Sem tipos WinUI: compilado também nos testes.</summary>
public static class ComandosImpressao
{
    /// <summary>Os módulos com Imprimir na faixa (Imóveis tem o seu, <see cref="ComandosApp.ImoveisImprimir"/>).</summary>
    public static IReadOnlyList<Modulo> Modulos { get; } =
        [Modulo.Relatorios, Modulo.Despachos, Modulo.Processos, Modulo.Legislacao, Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco];

    static readonly HashSet<string> NoVisualizador = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".txt",
    };

    static readonly HashSet<string> NoPrograma = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".rtf", ".odt", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp", ".tif", ".tiff",
    };

    /// <summary>Como imprimir o arquivo, pela extensão: PDF, imagens comuns e texto no visualizador; documentos do Office e TIFF pelo
    /// programa do Windows; o resto (DWG, compactados, executáveis…) não se imprime pelo app.</summary>
    public static ModoImpressao ModoDoArquivo(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return ModoImpressao.Nenhum;
        var ext = Path.GetExtension(caminho);
        if (NoVisualizador.Contains(ext)) return ModoImpressao.Visualizador;
        if (NoPrograma.Contains(ext)) return ModoImpressao.ProgramaDoWindows;
        return ModoImpressao.Nenhum;
    }

    public const string SemRelatorio = "Abra ou importe um relatório para imprimir.";
    public const string Ocupado = "Aguarde a operação em andamento terminar.";
    public const string NovoRelatorioAberto = "Feche a página Novo relatório para imprimir o relatório aberto.";
    public const string SemLei = "Abra uma lei na lista para imprimir.";
    public const string SemSite = "Espere a página do sistema carregar para imprimir.";
    public const string SemArquivo = "Escolha um arquivo do processo na lista da direita para imprimir.";
    public const string RelatorioNaLista = "Abra o relatório (Abrir relatório) e imprima pelo módulo Relatórios, com as folhas do PDF.";
    public const string TipoSemImpressao = "Este tipo de arquivo não se imprime pelo app; abra-o no programa dele (Abrir arquivo).";
    public const string SemDespacho = "Grave um despacho em PDF (ou escolha um) para imprimir.";
    public const string DespachoEmEdicao = "Conclua o despacho em edição: o PDF é impresso depois de gravado.";

    /// <summary>Relatórios: imprime o mesmo PDF da exportação, então vale o que vale para Exportar PDF (<paramref name="motivoExportacao"/>
    /// é o motivo que a exportação dá quando está bloqueada).</summary>
    public static string? MotivoRelatorio(bool pronto, bool temRelatorio, bool novoRelatorioAberto, bool podeExportar, string? motivoExportacao)
    {
        if (!temRelatorio) return pronto ? SemRelatorio : Ocupado;
        if (novoRelatorioAberto) return NovoRelatorioAberto;
        if (!podeExportar)
            return string.IsNullOrWhiteSpace(motivoExportacao)
                ? "O relatório ainda não pode virar PDF: veja Revisão › Conferência."
                : "Imprimir sai pelo mesmo PDF da exportação, que está bloqueada: " + motivoExportacao.Trim();
        return null;
    }

    /// <summary>Legislação: o PDF da lei aberta no leitor.</summary>
    public static string? MotivoLegislacao(bool leiAberta) => leiAberta ? null : SemLei;

    /// <summary>Processos: o arquivo escolhido na lista da direita (<paramref name="tipo"/> e <paramref name="caminho"/> da linha).</summary>
    public static string? MotivoProcessos(TipoLinhaDetalhe? tipo, string? caminho)
    {
        if (tipo is null || caminho is null) return SemArquivo;
        if (tipo is TipoLinhaDetalhe.Relatorio or TipoLinhaDetalhe.Analise) return RelatorioNaLista;
        if (tipo is not (TipoLinhaDetalhe.Arquivo or TipoLinhaDetalhe.ArquivoDeDespacho)) return SemArquivo;
        return ModoDoArquivo(caminho) == ModoImpressao.Nenhum ? TipoSemImpressao : null;
    }

    /// <summary>Despachos: o PDF do despacho escolhido ou do que acabou de ser gravado; com a página Novo despacho à frente, nada.</summary>
    public static string? MotivoDespachos(bool compondo, string? pdf)
    {
        if (compondo) return DespachoEmEdicao;
        return string.IsNullOrWhiteSpace(pdf) ? SemDespacho : null;
    }

    /// <summary>Sites (Essencial, GPI, Alfresco): a página à vista no navegador embutido.</summary>
    public static string? MotivoSite(bool carregado) => carregado ? null : SemSite;
}
