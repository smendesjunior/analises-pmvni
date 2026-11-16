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
            // Trocar a janela de monitor (escala 150 % ⇄ 100 %) pode fazer o WinUI desistir de um layout que não assentou
            // (LayoutCycleException, 0x802B0014; registros de 2026-11-16): o quadro seguinte se refaz; o editor não fecha por isso.
            if (e.Exception is Microsoft.UI.Xaml.LayoutCycleException && !Autoteste.Ativo) { e.Handled = true; return; }
            if (Autoteste.Ativo) { Autoteste.GravarErro(e.Exception?.ToString() ?? e.Message); e.Handled = true; Exit(); }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RegistroErros.Gravar("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => RegistroErros.Gravar("TaskScheduler.UnobservedTaskException", e.Exception);
        // Falha de disco ou de permissão mostrada num aviso: o usuário lê o texto em português; o tipo e o código vão para o registro.
        Pmvni.Revisao.MensagensDeErro.Registrar = e => RegistroErros.Gravar("aviso ao usuário", e);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        janela = new MainWindow();
        janela.Activate();
    }
}
