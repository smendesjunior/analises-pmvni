namespace Pmvni.Revisao;

/// <summary>O app fechando com o preparo de um despacho em curso (onda de correções 1, item 12): interrompe o preparo e espera até
/// <c>espera</c> ele parar (o conversor encerra o soffice ao ver o cancelamento); se não parar, encerra já, de forma síncrona, a árvore do
/// soffice (<see cref="ConversorLibreOffice.EncerrarEmCurso"/>), antes de o processo do app terminar e deixar o soffice órfão segurando o
/// perfil. Depois apaga a pasta de trabalho. Fica aqui, sem tipos de interface, para o <c>dotnet test</c> exercitar com um soffice fictício.</summary>
public static class EncerramentoDoPreparo
{
    public static void Encerrar(Task? preparo, Action interromper, TimeSpan espera, Action apagarTrabalho)
    {
        ArgumentNullException.ThrowIfNull(interromper);
        ArgumentNullException.ThrowIfNull(apagarTrabalho);
        interromper();
        if (preparo is { IsCompleted: false } t && !((IAsyncResult)t).AsyncWaitHandle.WaitOne(espera))
        {
            ConversorLibreOffice.EncerrarEmCurso();
            ((IAsyncResult)t).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
        }
        apagarTrabalho();
    }
}
