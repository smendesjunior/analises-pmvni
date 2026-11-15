using Microsoft.UI.Xaml;

namespace Pmvni.App;

public partial class App : Application
{
    MainWindow? janela;

    public App()
    {
        InitializeComponent();
        // Todo erro não tratado vai para <dados>\logs (sem conteúdo do relatório), com ou sem autoteste.
        UnhandledException += (_, e) =>
        {
            RegistroErros.Gravar("Application.UnhandledException", e.Exception);
            if (Autoteste.Ativo) { Autoteste.GravarErro(e.Exception?.ToString() ?? e.Message); e.Handled = true; Exit(); }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RegistroErros.Gravar("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => RegistroErros.Gravar("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        janela = new MainWindow();
        janela.Activate();
    }
}
