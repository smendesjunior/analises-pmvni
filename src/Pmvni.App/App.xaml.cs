using Microsoft.UI.Xaml;

namespace Pmvni.App;

public partial class App : Application
{
    MainWindow? janela;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            if (Autoteste.Ativo) { Autoteste.GravarErro(e.Exception?.ToString() ?? e.Message); e.Handled = true; Exit(); }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        janela = new MainWindow();
        janela.Activate();
    }
}
