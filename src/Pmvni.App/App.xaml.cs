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
            // Auditoria 2026-11-16: fora do autoteste e do roteiro, um erro solto na interface não fecha o editor (o trabalho aberto
            // se perderia): fica registrado e o usuário é avisado. Falta de memória e pilha estourada seguem fatais.
            else if (Argumentos.Atual.Roteiro is null && e.Exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                e.Handled = true;
                janela?.AvisarErroInesperado();
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RegistroErros.Gravar("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => RegistroErros.Gravar("TaskScheduler.UnobservedTaskException", e.Exception);
        // Falha de disco ou de permissão mostrada num aviso: o usuário lê o texto em português; o tipo e o código vão para o registro.
        Pmvni.Revisao.MensagensDeErro.Registrar = e => RegistroErros.Gravar("aviso ao usuário", e);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Botões coloridos com brilho (como os do WinStatusBar): o AccentButtonStyle ganha o setter antes da primeira janela usá-lo
        // (no construtor do App os recursos ainda não podem ser lidos: 0x8000FFFF).
        Brilho.LigarNoDestaque(this);
        janela = new MainWindow();
        janela.Activate();
    }
}
