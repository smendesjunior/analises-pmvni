namespace Pmvni.Revisao.Testes;

/// <summary>Conversor de teste: devolve o que a função mandar (recebe o caminho do DOCX da pasta de trabalho). Nunca abre o LibreOffice.</summary>
public sealed class ConversorFalso(Func<string, ResultadoConversao> converter) : IConversorPdf
{
    public bool Disponivel => true;

    public string? Versao => "Conversor falso 1.0";

    public int Chamadas { get; private set; }

    public ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct)
    {
        Chamadas++;
        return converter(docx);
    }

    /// <summary>Sempre devolve <paramref name="pdf"/>.</summary>
    public static ConversorFalso Com(byte[] pdf) => new(_ => new ResultadoConversao(pdf, null, null));
}

/// <summary>Computador sem LibreOffice.</summary>
public sealed class ConversorAusente : IConversorPdf
{
    public bool Disponivel => false;

    public string? Versao => null;

    public ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct) => new(null, "SEM_LIBREOFFICE", null);
}
