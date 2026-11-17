using System.Globalization;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>O pedido que o Codex recebe pela entrada padrão (desenho §12.4; a primeira linha é o <c>defaultPrompt</c> do plugin analise 2.4.0).</summary>
public static class PedidoAgente
{
    public const string PrimeiraLinha =
        "Use $analise para analisar o processo PMVNI desta pasta, com as 28 seções, as referências do índice do kit e o relatório oficial em PDF.";

    /// <summary>Como o pedido digitado entra nos dois plugins (2026-11-16): diretriz complementar, que não muda as etapas da análise.</summary>
    public const string CabecalhoDiretrizes =
        "Diretrizes do usuário (complementares: execute todas as etapas e seções da análise normalmente; isto só acrescenta pontos a " +
        "verificar, por exemplo as pendências dos despachos anteriores, e não substitui, não pula nem encurta nenhuma etapa):";

    /// <summary>Linhas separadas por "\n", sem "\n" no fim. As linhas de anexos e de pedido só existem quando há conteúdo; o texto vai como foi digitado.</summary>
    /// <summary>A ordem enviada ao continuar uma sessão do Codex que parou (app fechado de repente ou cota esgotada).</summary>
    public const string Continuar =
        "A análise anterior foi interrompida (o editor fechou ou a cota de uso acabou). Continue exatamente de onde parou, sem refazer o que já está pronto, " +
        "seguindo as mesmas instruções do pedido original, até entregar o relatório final.";

    public static string Codex(string pastaProcesso, Processo p, IReadOnlyList<ArquivoArquivado> anexos, string? texto)
    {
        var linhas = new List<string> { PrimeiraLinha, "Pasta do processo: " + pastaProcesso };

        string identificacao;
        if (p.Numero is { } n && p.Ano is { } a)
        {
            identificacao = string.Create(CultureInfo.InvariantCulture, $"{n:D4}/{a}");
            if (!string.IsNullOrWhiteSpace(p.Requerente)) identificacao += " — " + p.Requerente;
        }
        else identificacao = p.NomePasta;
        linhas.Add("Processo: " + identificacao);

        if (anexos.Count > 0)
        {
            linhas.Add("Arquivos que o usuário anexou agora, já copiados na pasta do processo pelo Editor de Relatórios PMVNI:");
            foreach (var anexo in anexos)
                linhas.Add("- " + anexo.Relativo + (anexo.JaEstava ? " (já estava no processo)" : " (copiado)"));
        }
        if (!string.IsNullOrWhiteSpace(texto))
        {
            linhas.Add(CabecalhoDiretrizes);
            linhas.Add(texto);
        }
        return string.Join("\n", linhas);
    }
}
