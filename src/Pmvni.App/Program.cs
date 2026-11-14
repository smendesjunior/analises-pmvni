using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Pmvni.App;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Autoteste.Configurar(args);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parametros =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }
}
