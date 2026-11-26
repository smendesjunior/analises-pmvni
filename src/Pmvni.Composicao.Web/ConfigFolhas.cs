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
    public static ConfigFolhas PadraoDeBytes(ModeloCarregado m, byte[] dados, IDictionary<string, TextoDoModelo>? textos = null)
    {
        ArgumentNullException.ThrowIfNull(dados);
        using var doc = JsonDocument.Parse(dados);
        var orgao = Cabecalho.OrgaoPadrao;
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object
            && config.TryGetProperty("orgao", out var o) && o.ValueKind == JsonValueKind.String)
            orgao = o.GetString()!;
        var revisoes = doc.RootElement.TryGetProperty("_textos_da_revisao", out var revisao) ? revisao : default;
        var cabecalho = Campo("cabecalho", orgao);
        var esquerda = Campo("rodape-esquerda", RodapeEsquerdaPadrao);
        var direita = Campo("rodape-direita", RodapeDireitaPadrao);
        return new ConfigFolhas(Cabecalho.CabecalhoUri(cabecalho.Visivel ? cabecalho.Atual : "", m.Brasao),
            esquerda.Visivel ? esquerda.Atual : "", direita.Visivel ? direita.Atual : "");

        TextoDoModelo Campo(string chave, string original)
        {
            var atual = original;
            var visivel = true;
            if (revisoes.ValueKind == JsonValueKind.Object && revisoes.TryGetProperty(chave, out var c) && c.ValueKind == JsonValueKind.Object)
            {
                if (c.TryGetProperty("texto", out var texto) && texto.ValueKind == JsonValueKind.String) atual = texto.GetString()!;
                if (c.TryGetProperty("visivel", out var visao) && visao.ValueKind == JsonValueKind.False) visivel = false;
            }
            var campo = new TextoDoModelo(original, atual, visivel);
            if (textos is not null) textos[chave] = campo;
            return campo;
        }
    }
}
