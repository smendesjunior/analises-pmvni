namespace Pmvni.Legislacao;

/// <summary>Uma lei anexada: a linha do catálogo. <see cref="Paginas"/> e <see cref="PaginasSemTexto"/> (1-based) vêm da
/// extração; só o <b>nome</b> do arquivo original é guardado, nunca o caminho.</summary>
public sealed record Anexo(
    string Id,
    string Sha256,
    string Titulo,
    Esfera Esfera,
    string ArquivoOriginal,
    long Tamanho,
    int Paginas,
    IReadOnlyList<int> PaginasSemTexto,
    DateTimeOffset AnexadoEm,
    DateTimeOffset? SubstituidoEm)
{
    /// <summary>Alguma página tem texto pesquisável (sem OCR: página sem texto fica sem texto).</summary>
    public bool TemTexto => PaginasSemTexto.Count < Paginas;
}

/// <summary>Situação de um anexo na pasta do editor.</summary>
public enum EstadoAnexo
{
    /// <summary>Cópia e índice presentes e válidos.</summary>
    Pronto,
    /// <summary>Cópia presente; o índice de texto falta, é de outra versão ou não confere com o sha.</summary>
    SemIndice,
    /// <summary>A cópia <c>&lt;sha&gt;.pdf</c> não está na pasta (só Substituir ou Excluir).</summary>
    SemCopia,
}
