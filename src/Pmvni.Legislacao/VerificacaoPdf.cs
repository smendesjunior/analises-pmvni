namespace Pmvni.Legislacao;

/// <summary>Verificações baratas de um PDF escolhido, antes de qualquer gravação.</summary>
public static class VerificacaoPdf
{
    /// <summary>200 MB por arquivo.</summary>
    public const long LimiteBytes = 209_715_200;

    public static bool TamanhoPermitido(long n) => n >= 1 && n <= LimiteBytes;

    static ReadOnlySpan<byte> Assinatura => "%PDF-"u8;

    /// <summary><c>%PDF-</c> em qualquer posição dos primeiros 1.024 bytes (a mesma tolerância do PDF.js).</summary>
    public static bool TemAssinatura(ReadOnlySpan<byte> primeiros1024)
    {
        if (primeiros1024.Length > 1024) primeiros1024 = primeiros1024[..1024];
        return primeiros1024.IndexOf(Assinatura) >= 0;
    }
}
