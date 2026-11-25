using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Pmvni.Composicao;

/// <summary>Modelo oficial de relatório já verificado contra o registro de templates.</summary>
public sealed record ModeloCarregado(
    string Html,
    string Css,
    byte[] Brasao,
    string Revisao,
    IReadOnlyDictionary<string, string> Sha256);

/// <summary>
/// Modelo HTML oficial do relatório (revisão 18), embutido byte a byte no assembly.
/// Os hashes vêm do registro TEMPLATES_OFICIAIS.json do kit de treinamento.
/// </summary>
public static class ModeloOficial
{
    public const string Revisao = "18";

    /// <summary>Arquivos embutidos como EmbeddedResource (nome lógico = nome do arquivo).</summary>
    private static readonly string[] ArquivosEmbutidos =
    {
        "modelo_relatorio.html",
        "relatorio.css",
        "brasao.png",
        "dados_modelo.json",
        "exemplo_figura.png",
        "exemplo_mapa.png",
    };

    /// <summary>Hashes sha256 (minúsculos) de todos os arquivos da revisão 18, inclusive o gerador.</summary>
    public static IReadOnlyDictionary<string, string> HashesDoRegistro { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gerar_relatorio.py"] = "60b831a5a3d4abe2edf7df2f8a8034d5972bf38a054999b3d9530802bbb57683",
            ["modelo_relatorio.html"] = "2e595fd33aa687fe25e99c784536d7c1709de91eb80097c27d04cb177380566c",
            ["relatorio.css"] = "b59b97a59892a33060bb71cdd4c0ae4f93063a01b8e09f004be49e522628a62a",
            ["brasao.png"] = "93f35890bd35ddd405ee407f05e5cea059301c7e62634415f19d5c4d9b4092cd",
            ["dados_modelo.json"] = "c56df9e6aec479887b028ce86e8f2962937a244cefabf5b8a308aca93f1ff816",
            ["exemplo_figura.png"] = "cbbf837e4eb816a42b642bf7e139090364ea98d6e9f603edcfc8e42a65a2244e",
            ["exemplo_mapa.png"] = "410de4f9e41fbedd895d479ba7cae3ce736de207495b3c711938a83fdd915cd2",
        });

    /// <summary>
    /// Lê e verifica os arquivos embutidos. Lança <see cref="InvalidOperationException"/>
    /// se algum arquivo não conferir com o registro da revisão 18.
    /// </summary>
    public static ModeloCarregado Carregar()
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var conteudos = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var nome in ArquivosEmbutidos)
        {
            var conteudo = LerArquivo(nome);
            var real = Sha256Hex(conteudo);
            var esperado = HashesDoRegistro[nome];

            if (!string.Equals(real, esperado, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Modelo oficial rev. {Revisao} inconsistente: '{nome}' tem sha256 {real}, " +
                    $"mas o registro exige {esperado}. Recompile com o modelo correto do kit.");
            }

            hashes[nome] = real;
            conteudos[nome] = conteudo;
        }

        return new ModeloCarregado(
            Html: Encoding.UTF8.GetString(conteudos["modelo_relatorio.html"]),
            Css: Encoding.UTF8.GetString(conteudos["relatorio.css"]),
            Brasao: conteudos["brasao.png"],
            Revisao: Revisao,
            Sha256: new ReadOnlyDictionary<string, string>(hashes));
    }

    /// <summary>Devolve os bytes exatos de um arquivo embutido (ex.: dados_modelo.json, exemplo_figura.png).</summary>
    public static byte[] LerArquivo(string nome)
    {
        if (!ArquivosEmbutidos.Contains(nome, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Arquivo '{nome}' não faz parte do modelo oficial rev. {Revisao}.", nameof(nome));
        }

        using var fluxo = typeof(ModeloOficial).Assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException($"Recurso embutido '{nome}' ausente do assembly.");
        using var memoria = new MemoryStream();
        fluxo.CopyTo(memoria);
        return memoria.ToArray();
    }

    private static string Sha256Hex(byte[] conteudo) =>
        Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant();
}
