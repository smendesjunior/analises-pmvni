using System.Globalization;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>O pedido que o Codex recebe pela entrada padrão (desenho §12.4; a primeira linha é o <c>defaultPrompt</c> do plugin analise 2.4.0).</summary>
public static class PedidoAgente
{
    public const string PrimeiraLinha =
        "Use $analise para analisar o processo PMVNI desta pasta, com as 28 seções, as referências do índice do kit e o relatório oficial em PDF.";

    /// <summary>Linhas separadas por "\n", sem "\n" no fim. As linhas de anexos e de pedido só existem quando há conteúdo; o texto vai como foi digitado.</summary>
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
            linhas.Add("Pedido do usuário:");
            linhas.Add(texto);
        }
        return string.Join("\n", linhas);
    }
}
