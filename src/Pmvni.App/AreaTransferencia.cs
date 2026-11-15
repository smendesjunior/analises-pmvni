using Windows.ApplicationModel.DataTransfer;

namespace Pmvni.App;

/// <summary>Copiar e Copiar citação do leitor da legislação (desenho §11.7): o texto vai para a área de transferência do Windows. No
/// roteiro (<see cref="Simular"/>) só é registrado em <see cref="Copiados"/>, que guarda também o que foi copiado de verdade.</summary>
internal static class AreaTransferencia
{
    public static bool Simular { get; set; }

    public static List<string> Copiados { get; } = [];

    public static void Copiar(string texto)
    {
        Copiados.Add(texto);
        if (Simular) return;
        var pacote = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        pacote.SetText(texto);
        Clipboard.SetContent(pacote);
        // O texto continua na área de transferência depois de o editor fechar.
        try { Clipboard.Flush(); }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { RegistroErros.Gravar("area-transferencia", e); }
    }
}
