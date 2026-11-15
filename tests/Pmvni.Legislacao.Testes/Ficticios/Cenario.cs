using System.Security.Cryptography;

namespace Pmvni.Legislacao.Testes.Ficticios;

/// <summary>Montagem comum dos testes: os PDFs fictícios copiados para <c>&lt;pasta&gt;\originais</c> (com os mesmos nomes e
/// subpastas), a pasta de dados <c>&lt;pasta&gt;\dados\legislacao</c>, relógio fixo e ids sequenciais.</summary>
public sealed class Cenario : IDisposable
{
    public static readonly DateTimeOffset Agora = new(2026, 11, 15, 14, 32, 0, TimeSpan.Zero);

    public const string Lei = "MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf";
    public const string Lei2 = "MUNICIPAL/LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf";
    public const string Nt = "ESTADUAL - CBMES/NT 99-2099 - FICTICIA.pdf";
    public const string Federal = "FEDERAL/LEI 99.999-2099 - FICTICIA.pdf";
    public const string Digitalizada = "OUTRAS/DIGITALIZADA - FICTICIA.pdf";
    public const string Senha = "OUTRAS/SENHA - FICTICIA.pdf";
    public const string Corrompido = "OUTRAS/CORROMPIDO - FICTICIA.pdf";
    public const string NaoEPdf = "OUTRAS/NAO-E-PDF - FICTICIA.pdf";
    public const string ComScript = "OUTRAS/COM-SCRIPT - FICTICIA.pdf";

    readonly PastaDeTeste pasta;
    int sequencia;

    public Cenario(string nome)
    {
        pasta = Ambiente.NovaPasta(nome);
        foreach (var arquivo in Directory.EnumerateFiles(Ambiente.Corpus, "*", SearchOption.AllDirectories))
        {
            var destino = Path.Combine(Originais, Path.GetRelativePath(Ambiente.Corpus, arquivo));
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(arquivo, destino);
        }
        Acervo = Novo();
    }

    public string Originais => Path.Combine(pasta.Caminho, "originais");
    public string Dados => Path.Combine(pasta.Caminho, "dados", "legislacao");
    public ExtratorFalso Extrator { get; } = new();
    public AcervoLegislacao Acervo { get; private set; }

    public string Original(string relativo) => Path.Combine(Originais, relativo.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Outro acervo sobre a mesma pasta (mesmo relógio e mesma sequência de ids).</summary>
    public AcervoLegislacao Novo() => new(Dados, () => Agora, () => "lg_" + (++sequencia).ToString("x32"));

    /// <summary>O fluxo de um arquivo: preparar, extrair e confirmar (recusa ou cancelamento descartam a entrada).</summary>
    public Anexo Anexar(string relativo, AcervoLegislacao? acervo = null) => AnexarCaminho(Original(relativo), acervo);

    public Anexo AnexarCaminho(string caminho, AcervoLegislacao? acervo = null)
    {
        acervo ??= Acervo;
        var entrada = acervo.Preparar(caminho);
        try
        {
            var r = Extrair(entrada.Caminho);
            return acervo.Confirmar(entrada, r);
        }
        catch
        {
            acervo.Descartar(entrada);
            throw;
        }
    }

    /// <summary>O extrator falso, de forma síncrona (ele conclui na hora).</summary>
    public ResultadoExtracao Extrair(string caminhoPdf) =>
        Extrator.ExtrairAsync(caminhoPdf, null, CancellationToken.None).GetAwaiter().GetResult();

    public IReadOnlyList<string> ArquivosDaPasta() =>
        Directory.Exists(Dados)
            ? [.. Directory.EnumerateFiles(Dados, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Dados, f)).Order(StringComparer.Ordinal)]
            : [];

    public string PastaEntrada => Path.Combine(Dados, ".entrada");

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(pasta.Caminho, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(f, FileAttributes.Normal); } catch (IOException) { }
        pasta.Dispose();
    }

    /// <summary>Sha, tamanho, data de alteração e atributos de um arquivo (para provar que o original não mudou).</summary>
    public sealed record Marca(string Sha, long Tamanho, DateTime Alterado, FileAttributes Atributos)
    {
        public static Marca De(string caminho) =>
            new(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(caminho))), new FileInfo(caminho).Length,
                File.GetLastWriteTimeUtc(caminho), File.GetAttributes(caminho));
    }
}
