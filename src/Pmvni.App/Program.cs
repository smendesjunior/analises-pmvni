using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Pmvni.App;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.FirstOrDefault() == Pmvni.Agentes.OrquestradorAnalise.ComandoCheckpoint)
            return Pmvni.Agentes.OrquestradorAnalise.SalvarRetomada(args, Console.Error);
        if (args.FirstOrDefault() == OcrDaAnalise.Comando)
            return OcrDaAnalise.Executar(args).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == Pmvni.Imoveis.MapaParaAnalise.Comando)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            return Pmvni.Imoveis.MapaParaAnalise.Executar(args, Console.Error);
        }
        return IniciarJanela(args);
    }

    static int IniciarJanela(string[] args)
    {
        Environment.SetEnvironmentVariable("PMVNI_EDITOR_OCR_EXE", OcrDaAnalise.Disponivel() ? Environment.ProcessPath : null);
        Autoteste.Configurar(args);
        Argumentos.Ler(args);
        if (Argumentos.Atual.Roteiro is { } roteiro) Roteiro.TravarProgramas(roteiro);
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
        if (SessaoNovoDespacho.DesenhouPaginas) TerminateProcess(GetCurrentProcess(), 0);
        return 0;
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TerminateProcess(IntPtr processo, uint codigo);
}
