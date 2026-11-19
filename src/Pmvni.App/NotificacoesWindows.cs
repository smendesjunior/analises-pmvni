using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Pmvni.App;

internal static class NotificacoesWindows
{
    static AppNotificationManager? gerenciador;
    static Action? ativarJanela;
    static bool falhaDeEnvioRegistrada;

    internal static void Registrar(Action ativar)
    {
        var argumentos = Argumentos.Atual;
        if (Autoteste.Ativo || argumentos.Roteiro is not null || argumentos.Captura is not null || argumentos.AnexarLeis is not null)
            return;
        try
        {
            if (!AppNotificationManager.IsSupported()) return;
            var nativo = AppNotificationManager.Default;
            ativarJanela = ativar;
            nativo.NotificationInvoked += AoInvocar;
            try { nativo.Register(); }
            catch
            {
                nativo.NotificationInvoked -= AoInvocar;
                ativarJanela = null;
                throw;
            }
            gerenciador = nativo;
        }
        catch (Exception e) { RegistroErros.Gravar("registro das notificações do Windows", e); }
    }

    internal static void Mostrar(string titulo, string texto)
    {
        if (gerenciador is not { } nativo) return;
        try
        {
            nativo.Show(new AppNotificationBuilder().AddText(titulo).AddText(texto).BuildNotification());
        }
        catch (Exception e)
        {
            if (falhaDeEnvioRegistrada) return;
            falhaDeEnvioRegistrada = true;
            RegistroErros.Gravar("envio de notificação do Windows", e);
        }
    }

    static void AoInvocar(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => ativarJanela?.Invoke();

    internal static void Encerrar()
    {
        if (gerenciador is not { } nativo) return;
        gerenciador = null;
        ativarJanela = null;
        try
        {
            nativo.NotificationInvoked -= AoInvocar;
            nativo.Unregister();
        }
        catch (Exception e) { RegistroErros.Gravar("encerramento das notificações do Windows", e); }
    }
}
