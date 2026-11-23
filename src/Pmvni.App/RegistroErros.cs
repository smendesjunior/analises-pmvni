using System.Globalization;
using System.Text;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Erros não tratados em <c>&lt;dados&gt;\logs\erro-*.txt</c> (dentro e fora do autoteste). Só tipo, HRESULT e pilha de
/// cada exceção da cadeia: nenhuma mensagem (pode citar conteúdo do relatório, como um campo [ ]) e nada dos dados. Ficam os
/// <see cref="MaxArquivos"/> mais recentes.</summary>
internal static class RegistroErros
{
    const int MaxArquivos = 20;

    public static string Pasta => Path.Combine(PastaDados.Raiz, "logs");

    public static void Gravar(string origem, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            var agora = DateTimeOffset.UtcNow;
            var texto = new StringBuilder()
                .Append("quando: ").AppendLine(agora.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
                .Append("origem: ").AppendLine(origem)
                .Append("versao: ").AppendLine(typeof(RegistroErros).Assembly.GetName().Version?.ToString() ?? "?");
            for (var (x, n) = (e, 0); x is not null && n < 10; x = x.InnerException, n++)
            {
                texto.AppendLine("---")
                    .Append("tipo: ").AppendLine(x.GetType().FullName)
                    .Append("hresult: 0x").AppendLine(x.HResult.ToString("X8", CultureInfo.InvariantCulture))
                    .AppendLine("pilha:").AppendLine(x.StackTrace ?? "(sem pilha)");
            }
            var arquivo = Path.Combine(Pasta, $"erro-{agora:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.txt");
            File.WriteAllText(ArquivoGerado.Caminho(arquivo), texto.ToString(), new UTF8Encoding(false));
            foreach (var velho in new DirectoryInfo(Pasta).GetFiles("erro-*.txt").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(MaxArquivos))
                velho.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("[log] " + ex.GetType().Name);
        }
    }
}
