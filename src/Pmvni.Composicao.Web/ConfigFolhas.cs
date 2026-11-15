using System.Text.Json;
using Pmvni.Composicao.Kit;

namespace Pmvni.Composicao.Web;

/// <summary>Cabeçalho e rodapé das folhas A4. <see cref="RodapeDireitaModelo"/> usa <c>{n}</c> (página) e <c>{t}</c> (total).</summary>
public sealed record ConfigFolhas(string CabecalhoUri, string RodapeEsquerda, string RodapeDireitaModelo)
{
    // Copiados da regra @page do modelo oficial (rev. 10); note os espaços duplos em volta do "•".
    public const string RodapeEsquerdaPadrao = "PREFEITURA MUNICIPAL DE VENDA NOVA DO IMIGRANTE  •  SECRETARIA MUNICIPAL DE OBRAS";
    public const string RodapeDireitaPadrao = "RELATÓRIO TÉCNICO  •  PÁGINA {n} DE {t}";

    /// <summary>Configuração do kit: cabeçalho do órgão (<paramref name="orgao"/>) com o brasão do modelo e os rodapés padrão.</summary>
    public static ConfigFolhas Padrao(ModeloCarregado m, string orgao = Cabecalho.OrgaoPadrao) =>
        new(Cabecalho.CabecalhoUri(orgao, m.Brasao), RodapeEsquerdaPadrao, RodapeDireitaPadrao);

    /// <summary>Como <see cref="Padrao"/>, com o órgão lido de <c>config.orgao</c> do dados.json (como o gerador do kit).</summary>
    public static ConfigFolhas PadraoDeDados(ModeloCarregado m, string caminhoDados) =>
        PadraoDeBytes(m, File.ReadAllBytes(caminhoDados));

    /// <summary>Como <see cref="PadraoDeDados"/>, com os bytes do dados.json já lidos (o efetivo gerado a partir do pacote não é um arquivo).</summary>
    public static ConfigFolhas PadraoDeBytes(ModeloCarregado m, byte[] dados)
    {
        ArgumentNullException.ThrowIfNull(dados);
        using var doc = JsonDocument.Parse(dados);
        var orgao = Cabecalho.OrgaoPadrao;
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("orgao", out var o) && o.ValueKind == JsonValueKind.String)
            orgao = o.GetString()!;
        return Padrao(m, orgao);
    }
}
