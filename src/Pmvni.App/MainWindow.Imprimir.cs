using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Imprimir (pedido de 2026-11-17: "em relatórios, PDFs, relatórios de imóveis, leis, os sites… botão de imprimir, listando as
/// impressoras e a opção de gerar PDF, na barra de opções"). Um comando só (<see cref="ComandosApp.ArquivoImprimir"/>, Ctrl+P) na faixa de
/// cada módulo que mostra um documento; o que se imprime depende do módulo à vista:
/// <list type="bullet">
/// <item>Relatórios: o MESMO PDF da exportação (composto no host oculto, sem os recursos do editor: seleção, marcas, zoom), gerado
/// numa pasta temporária e mostrado na <see cref="JanelaImpressao"/>; ela é apagada quando a janela fecha.</item>
/// <item>Legislação: o PDF inteiro da lei aberta (uma cópia com o título como nome), não as páginas que o leitor desenhou.</item>
/// <item>Processos: o arquivo escolhido — PDF, imagem ou texto na janela de impressão; Word, Excel e afins pelo programa do Windows
/// (verbo "print", depois de confirmar); o resto fica desabilitado com o motivo.</item>
/// <item>Despachos: o PDF do despacho escolhido na lista ou, sem lista, o do último despacho gravado.</item>
/// <item>Essencial, GPI, Alfresco: a página à vista, pela impressão do próprio navegador embutido.</item>
/// </list>
/// Imóveis tem o seu Imprimir (<see cref="ComandosApp.ImoveisImprimir"/>, em MainWindow.Imoveis.cs); aqui só o Ctrl+P aponta para ele
/// (<see cref="Atalhos.MapaImoveis"/>). Toda impressão é a do Edge (<see cref="CoreWebView2PrintDialogKind.Browser"/>): lista de
/// impressoras e "Salvar como PDF", dentro da janela, sem o diálogo do sistema que trava esperando a impressora.</summary>
public sealed partial class MainWindow
{
    readonly List<JanelaImpressao> janelasImpressao = [];

    void RegistrarManipuladoresImpressao()
    {
        manipuladores[ComandosApp.ArquivoImprimir] = ImprimirAsync;
        Closed += (_, _) =>
        {
            foreach (var j in janelasImpressao.ToList()) try { j.Close(); } catch (Exception) { /* já fechada */ }
            janelasImpressao.Clear();
        };
    }

    /// <summary>Habilita Imprimir pelo módulo à vista; desabilitado, a dica diz o motivo (<see cref="ComandosImpressao"/>).</summary>
    void AtualizarComandosDeImpressao(bool pronto, bool temRelatorio, bool podeExportar, string? motivoExportacao)
    {
        string? motivo = moduloAtual switch
        {
            Modulo.Relatorios => ComandosImpressao.MotivoRelatorio(pronto, temRelatorio, novoRelatorioAberto, podeExportar, motivoExportacao),
            Modulo.Legislacao => ComandosImpressao.MotivoLegislacao(leiAberta is not null && leitorPdf is not null),
            Modulo.Processos => ComandosImpressao.MotivoProcessos(entradaEscolhida?.Tipo, entradaEscolhida?.Caminho),
            Modulo.Despachos => ComandosImpressao.MotivoDespachos(PaginaNovoDespachoVisivel, PdfDoDespachoParaImprimir()),
            var m when Modulos.EhSite(m) => ComandosImpressao.MotivoSite(PaginaDeSites.Atual is not null),
            _ => "Este módulo não tem documento para imprimir.",
        };
        Faixa.Habilitar(ComandosApp.ArquivoImprimir, motivo is null);
        Faixa.DefinirDica(ComandosApp.ArquivoImprimir, motivo ?? "");
    }

    /// <summary>O PDF do despacho de Despachos: o escolhido na lista, se ela está à vista; senão o do último despacho gravado.</summary>
    string? PdfDoDespachoParaImprimir() =>
        PaginaDeDespachos.ListaDespachos.Visibility == Visibility.Visible && despachoEscolhido?.ArquivoPdf is { } escolhido
            ? escolhido : UltimaEntrega?.Pdf;

    async Task ImprimirAsync()
    {
        switch (moduloAtual)
        {
            case Modulo.Relatorios: await ImprimirRelatorioAsync(); break;
            case Modulo.Legislacao: await ImprimirLeiAsync(); break;
            case Modulo.Processos: await ImprimirArquivoAsync(entradaEscolhida?.Caminho); break;
            case Modulo.Despachos: await ImprimirDespachoAsync(); break;
            case var m when Modulos.EhSite(m):
                if (PaginaDeSites.Atual is not { } site) return;
                if (AberturaExterna.Simular) { AberturaExterna.Registradas.Add("imprimir: " + site.Source); return; }
                site.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                break;
        }
    }

    async Task ImprimirDespachoAsync()
    {
        var caminho = PdfDoDespachoParaImprimir();
        if (caminho is null || !File.Exists(caminho) || AberturaExterna.Simular)
        {
            await ImprimirArquivoAsync(caminho);
            return;
        }
        var via = await Task.Run(() => ImpressaoDespacho.PrepararCopia(File.ReadAllBytes(caminho)));
        if (via is null)
        {
            await ImprimirArquivoAsync(caminho);
            return;
        }
        var pasta = NovaPastaDeImpressao();
        try
        {
            var arquivo = Path.Combine(pasta, Path.GetFileName(caminho));
            await File.WriteAllBytesAsync(arquivo, via);
            await AbrirJanelaImpressaoAsync(arquivo, "Via de impressão — " + Path.GetFileNameWithoutExtension(caminho), pasta);
        }
        catch
        {
            ApagarPastaDeImpressao(pasta);
            throw;
        }
    }

    /// <summary>Relatórios: exporta (com todas as conferências da exportação) para um PDF temporário e o abre na janela de impressão.</summary>
    async Task ImprimirRelatorioAsync()
    {
        if (sessao?.Atual is not { } r) return;
        var pasta = NovaPastaDeImpressao();
        var nome = r.Dados.Processo is { } p ? "Relatório " + NomeDeArquivo(p) : "Relatório";
        var (ok, x, _, _) = await ExportarParaAsync(Path.Combine(pasta, nome + ".pdf"), paraImprimir: true);
        if (!ok || x is null) { ApagarPastaDeImpressao(pasta); return; }
        await AbrirJanelaImpressaoAsync(x.CaminhoPdf, nome, pasta);
    }

    /// <summary>Legislação: uma cópia do PDF da lei aberta, com o título como nome (o "Salvar como PDF" e a fila da impressora mostram o
    /// título, não o sha da cópia do acervo).</summary>
    async Task ImprimirLeiAsync()
    {
        if (leiAberta is not { } lei) return;
        var origem = await Task.Run(() => legislacao.Acervo.CaminhoPdf(lei.Id));
        if (origem is null)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "A lei não foi impressa", "A cópia do PDF desta lei não está mais no acervo. Anexe a lei de novo em Ajustes › Legislação.");
            return;
        }
        var pasta = NovaPastaDeImpressao();
        var copia = Path.Combine(pasta, NomeDeArquivo(lei.Titulo) + ".pdf");
        await Task.Run(() => File.Copy(origem, copia, overwrite: true));
        await AbrirJanelaImpressaoAsync(copia, lei.Titulo, pasta);
    }

    /// <summary>Processos e Despachos: PDF, imagem ou texto na janela de impressão; documentos do Office pelo programa do Windows, depois
    /// de confirmar (ele manda para a impressora padrão, sem lista de impressoras).</summary>
    async Task ImprimirArquivoAsync(string? caminho)
    {
        if (caminho is null) return;
        if (!File.Exists(caminho))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "O arquivo não foi impresso", "O arquivo não existe mais. Atualize a lista.");
            return;
        }
        var nome = Path.GetFileName(caminho);
        switch (ComandosImpressao.ModoDoArquivo(caminho))
        {
            case ModoImpressao.Visualizador:
                await AbrirJanelaImpressaoAsync(caminho, Path.GetFileNameWithoutExtension(caminho), null);
                break;
            case ModoImpressao.ProgramaDoWindows:
                if (!AbrirExterno.Permitido(caminho, out var motivo))
                {
                    MostrarMensagem(InfoBarSeverity.Warning, "O arquivo não foi impresso", motivo ?? "O arquivo não pode ser aberto pelo app.");
                    return;
                }
                if (!await Perguntas.Atual.ConfirmarAsync("Imprimir pelo programa do Windows?",
                        $"«{nome}» não abre na impressão do app. O Windows vai abri-lo no programa dele e mandá-lo direto para a impressora padrão, " +
                        "sem a lista de impressoras. Para escolher a impressora ou salvar como PDF, use Abrir arquivo e imprima pelo programa.",
                        "Imprimir", "Cancelar", destrutiva: false))
                    return;
                if (AberturaExterna.Simular) { AberturaExterna.Registradas.Add("imprimir: " + caminho); return; }
                try
                {
                    TravaDeProgramas.Iniciar(new ProcessStartInfo(caminho) { UseShellExecute = true, Verb = "print" })?.Dispose();
                    MostrarMensagem(InfoBarSeverity.Informational, "Enviado para a impressora", $"«{nome}» foi para a impressora padrão do Windows.");
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    MostrarMensagem(InfoBarSeverity.Warning, "O arquivo não foi impresso",
                        "O Windows não tem um programa que imprima este tipo de arquivo. Use Abrir arquivo e imprima pelo programa. (" + e.Message + ")");
                }
                break;
            default:
                MostrarMensagem(InfoBarSeverity.Informational, "Comando indisponível", ComandosImpressao.TipoSemImpressao);
                break;
        }
    }

    async Task AbrirJanelaImpressaoAsync(string arquivo, string titulo, string? pastaTemporaria)
    {
        if (AberturaExterna.Simular)
        {
            AberturaExterna.Registradas.Add("imprimir: " + arquivo);
            if (pastaTemporaria is not null) ApagarPastaDeImpressao(pastaTemporaria);
            return;
        }
        var ambiente = R.Navegador.CoreWebView2?.Environment ?? await ambienteDaTela.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var j = JanelaImpressao.Abrir(ambiente, arquivo, titulo, TemaAtual, pastaTemporaria);
        janelasImpressao.Add(j);
        j.Closed += (_, _) => janelasImpressao.Remove(j);
    }

    /// <summary>Uma pasta nova em %TEMP%\Pmvni.Impressao; as que sobraram de impressões antigas (mais de um dia) saem antes.</summary>
    static string NovaPastaDeImpressao()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "Pmvni.Impressao");
        try
        {
            if (Directory.Exists(raiz))
                foreach (var velha in Directory.GetDirectories(raiz).Where(d => Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddDays(-1)))
                    ApagarPastaDeImpressao(velha);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* limpeza é só conveniência */ }
        var pasta = Path.Combine(raiz, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(pasta);
        return pasta;
    }

    static void ApagarPastaDeImpressao(string pasta)
    {
        try { if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* sai na próxima impressão */ }
    }

    /// <summary>O texto como nome de arquivo: sem os caracteres que o Windows recusa, no máximo 120 caracteres.</summary>
    static string NomeDeArquivo(string texto)
    {
        var limpo = string.Concat(texto.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim().TrimEnd('.');
        if (limpo.Length > 120) limpo = limpo[..120].TrimEnd();
        return limpo.Length > 0 ? limpo : "Documento";
    }
}
