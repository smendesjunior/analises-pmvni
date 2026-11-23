using System.Globalization;

namespace Pmvni.Legislacao;

/// <summary>Mensagens ao usuário do anexar/editar (desenho §11.1 e §11.2), exatas.</summary>
public static class MensagensAnexo
{
    public const string Igual = "O arquivo escolhido é igual ao já anexado.";
    public const string TituloVazio = "O título não pode ficar vazio.";
    public const string TituloLongo = "O título pode ter até 200 caracteres.";
    public const string SomenteLeitura = "O catálogo da legislação foi gravado por uma versão mais nova do editor; os anexos ficam só para leitura.";
    public const string Corrompido = "O catálogo da legislação estava danificado; foi usada a cópia anterior (catalogo.json.bak).";

    public const string CopiaEmUso = "O arquivo da cópia está em uso; ele será removido na próxima abertura.";

    public static string Vazio(string nome) => $"“{nome}” está vazio.";

    /// <summary>MB = 1.048.576 bytes, uma casa, vírgula.</summary>
    public static string Grande(string nome, long bytes) =>
        $"“{nome}” tem {(bytes / 1_048_576.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')} MB; o limite é 200 MB por arquivo.";

    public static string NaoPdf(string nome) => $"“{nome}” não é um arquivo PDF.";
    public static string Repetido(string nome, string titulo) => $"“{nome}” já está anexado como “{titulo}”.";
    public static string Senha(string nome) => $"“{nome}” está protegido por senha e não pode ser aberto pelo editor. Anexe uma cópia sem senha.";
    public static string Invalido(string nome) => $"“{nome}” não pôde ser lido como PDF (arquivo danificado ou incompleto).";
    public static string Ilegivel(string nome, string msg) => $"“{nome}” não pôde ser lido: {msg.Trim().TrimEnd('.')}.";

    /// <summary>"Ilegível" com o motivo em português pelo tipo da falha de E/S (o texto do sistema, às vezes em inglês, não vai à tela).</summary>
    public static string Ilegivel(string nome, Exception e) => Ilegivel(nome, MotivoDeEs(e));

    /// <summary>O motivo de uma falha de leitura em português.</summary>
    public static string MotivoDeEs(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "o arquivo não foi encontrado",
        UnauthorizedAccessException or System.Security.SecurityException => "acesso negado",
        PathTooLongException => "o caminho é longo demais",
        IOException io when (io.HResult & 0xFFFF) is 32 or 33 => "o arquivo está em uso por outro programa",
        IOException io when (io.HResult & 0xFFFF) is 112 => "não há espaço no disco",
        _ => "erro de leitura do disco",
    };

    /// <summary>O leitor de PDF (PDF.js) falhou por outro motivo que não senha nem arquivo danificado.</summary>
    public static string LeitorFalhou(string nome) => $"“{nome}” não pôde ser lido pelo leitor de PDF do editor.";

    /// <summary>O leitor de PDF não pôde ser iniciado (WebView2).</summary>
    public const string LeitorIndisponivel = "O leitor de PDF do editor não pôde ser iniciado.";

    public static string Recuperados(int n) => n == 1
        ? "1 PDF sem registro no catálogo foi recuperado; confira o título e a esfera em Ajustes › Módulos › Legislação."
        : $"{n} PDFs sem registro no catálogo foram recuperados; confira o título e a esfera em Ajustes › Módulos › Legislação.";
}

/// <summary>Recusa com a mensagem já pronta para o usuário.</summary>
/// <summary><see cref="Repetido"/>: a lei já está anexada (mesmo conteúdo); ao anexar, ela é ignorada em vez de recusada (2026-11-17).</summary>
public sealed class AnexoRecusadoException(string mensagem, Exception? causa = null, bool repetido = false) : Exception(mensagem, causa)
{
    public bool Repetido { get; } = repetido;
}
