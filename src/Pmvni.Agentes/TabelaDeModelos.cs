namespace Pmvni.Agentes;

/// <summary>Tarefa 19: a tabela efetiva de modelos e níveis da análise (o padrão do kit em <c>references\modelos-por-rota.json</c> com o ajuste local de
/// Ajustes › Inteligência artificial › Modelos da análise) que o Editor passa ao controlador do kit nesta execução. <paramref name="Json"/> está no formato do
/// ajuste do kit (<c>rotas</c> → <c>perfis</c> → <c>etapas</c> → <c>{modelo, nivel}</c>, <c>regras</c>, <c>ligada_por_rota</c>); o
/// <see cref="OrquestradorAnalise"/> o grava em <c>&lt;registro&gt;\modelos-por-rota.json</c>, passa o caminho em <see cref="Variavel"/> e registra a tabela
/// e os <paramref name="Avisos"/> (modelo que sumiu da lista do provedor e foi trocado pelo padrão do kit) no <c>execucao.json</c>. Nunca chave nem token.</summary>
public sealed record TabelaDeModelos(string Json, IReadOnlyList<string> Avisos)
{
    /// <summary>A variável que o kit lê (<c>scripts/modelos_por_rota.py</c>).</summary>
    public const string Variavel = "PMVNI_MODELOS_POR_ROTA";

    /// <summary>O arquivo na pasta do registro da execução.</summary>
    public const string NomeDoArquivo = "modelos-por-rota.json";

    /// <summary>O padrão do kit, em <c>skills\analise\references</c> do plugin analise.</summary>
    public const string NomeNoKit = "modelos-por-rota.json";

    /// <summary>A chave do <c>execucao.json</c> com a tabela usada.</summary>
    public const string ChaveNoRegistro = "modelos_da_analise";
}
