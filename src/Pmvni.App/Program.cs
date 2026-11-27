using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Pmvni.App;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.FirstOrDefault() is "--exportar-analise" or "--conferir-pdf-analise")
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var impressaoNativa = args is ["--exportar-analise", _, _, "--impressao-nativa"];
            if (args.Length != 3 && !impressaoNativa) return 2;
            try
            {
                var dados = Path.GetFullPath(args[1]);
                var pdf = Path.GetFullPath(args[2]);
                if (args[0] == "--conferir-pdf-analise")
                    Pmvni.Composicao.Web.Exportador.ConferirPdfAnaliseAsync(dados, pdf).GetAwaiter().GetResult();
                else
                    Pmvni.Composicao.Web.Exportador.ExportarAnaliseAsync(dados, pdf, impressaoNativa).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
                return 1;
            }
        }
        if (args.FirstOrDefault() == Pmvni.Agentes.OrquestradorAnalise.ComandoCheckpoint)
#if PMVNI_DISTRIBUICAO
            return 2;
#else
            return Pmvni.Agentes.OrquestradorAnalise.SalvarRetomada(args, Console.Error);
#endif
        if (args.FirstOrDefault() == OcrDaAnalise.Comando)
            return OcrDaAnalise.Executar(args).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == Pmvni.Imoveis.ConsultaParaAnalise.Comando)
            return Pmvni.Imoveis.ConsultaParaAnalise.Executar(args, Console.Error);
        if (args.FirstOrDefault() == "--rasterizar-vista")
        {
            if (args.Length != 3) return 2;
            try { Pmvni.Imoveis.MapaParaAnalise.RasterizarVista(args[1], args[2]); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        }
        if (args.FirstOrDefault() == Pmvni.Imoveis.MapaParaAnalise.Comando)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            return Pmvni.Imoveis.MapaParaAnalise.Executar(args, Console.Error);
        }
        return IniciarJanela(args);
    }

    static int IniciarJanela(string[] args)
    {
#if PMVNI_DISTRIBUICAO
        if (Array.IndexOf(args, "--roteiro") >= 0) return 2;
#endif
        Environment.SetEnvironmentVariable("PMVNI_EDITOR_OCR_EXE", OcrDaAnalise.Disponivel() ? Environment.ProcessPath : null);
        Autoteste.Configurar(args);
        Argumentos.Ler(args);
#if !PMVNI_DISTRIBUICAO
        if (Argumentos.Atual.Roteiro is { } roteiro) Roteiro.TravarProgramas(roteiro);
#endif
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parametros =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        NotificacoesWindows.Encerrar();
        // Depois de o Windows.Data.Pdf desenhar páginas neste processo (passo 3 do Novo despacho), o driver de vídeo da Intel
        // (igd10um64xe.dll) fica girando a 100% na descarga das DLLs e o processo nunca termina (visto no roteiro da Tarefa 16: uma thread
        // só, dentro do driver, depois de tudo gravado). Quando o laço de mensagens acaba, tudo já foi gravado e fechado: o processo sai
        // direto, sem as notificações de descarga.
        // Ajustes › Sistema › "Reiniciar agora" depois de uma atualização: abre o executável (já trocado) de novo.
        Atualizador.ReabrirSePedido();
#if PMVNI_DISTRIBUICAO
        var codigo = Autoteste.CodigoSaida;
#else
        var codigo = Argumentos.Atual.Roteiro is null ? Autoteste.CodigoSaida : Roteiro.CodigoSaida;
#endif
        if (SessaoNovoDespacho.DesenhouPaginas) TerminateProcess(GetCurrentProcess(), (uint)codigo);
        return codigo;
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TerminateProcess(IntPtr processo, uint codigo);
}
