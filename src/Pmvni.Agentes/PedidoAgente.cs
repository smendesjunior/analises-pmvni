namespace Pmvni.Agentes;

/// <summary>Os textos que acompanham a análise. Desde a Tarefa 12 nenhuma IA recebe um pedido do editor: o controlador do kit é rodado direto, e as diretrizes do
/// analista e o tratamento dos dados vão no <c>diretrizes.md</c> da pasta de controle do kit (<see cref="Diretrizes"/>), lido pelas sessões técnicas.
/// <see cref="PrimeiraLinha"/> e <see cref="CabecalhoDiretrizes"/> ficam para ler o <c>pedido.md</c> das análises do Editor 2.2 (com a sessão controladora).</summary>
public static class PedidoAgente
{
    /// <summary>A primeira linha do pedido que o Editor 2.2 mandava à sessão controladora do Codex (registros antigos começam com ela).</summary>
    public const string PrimeiraLinha =
        "Use $analise para analisar o processo PMVNI desta pasta, com as 28 seções, as referências do índice do kit e o relatório oficial em PDF.";

    /// <summary>Como o pedido digitado entra nos plugins (2026-11-16): diretriz complementar, que não muda as etapas da análise.</summary>
    public const string CabecalhoDiretrizes =
        "Diretrizes do usuário (complementares: execute todas as etapas e seções da análise normalmente; isto só acrescenta pontos a " +
        "verificar, por exemplo as pendências dos despachos anteriores, e não substitui, não pula nem encurta nenhuma etapa):";

    public const string TratamentoDoProcesso = "Na análise iniciada pelo usuário no Editor de Relatórios com ChatGPT, o Codex e as sessões técnicas do plugin analise podem tratar na OpenAI os dados pessoais necessários das peças do processo selecionado, inclusive nomes em caminhos, arquivos e pranchas. A autorização vale somente para essa análise. Preservar o sigilo do cadastro municipal, de outros processos e de dados sem relação com o pedido. Não publicar dados do processo, enviá-los a conectores adicionais ou gravá-los na memória da IA.";

    /// <summary>O <c>diretrizes.md</c> do analise (ChatGPT): o tratamento dos dados e, se houver, o pedido digitado (como foi digitado) sob o cabeçalho das
    /// diretrizes. Linhas separadas por "\n", sem "\n" no fim.</summary>
    public static string Diretrizes(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? TratamentoDoProcesso : TratamentoDoProcesso + "\n" + CabecalhoDiretrizes + "\n" + texto;
}
