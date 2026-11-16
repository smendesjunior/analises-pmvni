using System.Text.Json.Nodes;
using Pmvni.Composicao;

namespace Pmvni.Revisao.Testes;

/// <summary>Uma cópia da árvore fictícia de processos (<c>tests/corpus/processos</c>) com o <c>Relatorio_0001_2099.rpmvni</c> importado do
/// <see cref="RelatorioFicticio.Curto"/> na pasta do processo 0001.</summary>
public sealed record ArvoreFicticia(string Raiz, Processo Processo, string CaminhoPacote, PacoteAberto Pacote);

/// <summary>O relatório fictício dos testes de despacho: o <c>curto</c> do corpus com a capa trocada (nada de dados reais).</summary>
public static class RelatorioFicticio
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    /// <summary>Bytes do <c>tests/corpus/curto.json</c> com <c>capa.processo</c> e <c>capa.requerente</c> trocados (por <see cref="JsonFiel"/>).</summary>
    public static byte[] CurtoJson(string processo = "0001/2099", string requerente = "FULANO DE EXEMPLO (FICTICIO)")
    {
        var d = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus("curto.json")));
        var capa = (JsonObject)d["capa"]!;
        capa["processo"] = processo;
        capa["requerente"] = requerente;
        return JsonFiel.Escrever(d);
    }

    /// <summary>O <see cref="CurtoJson"/> importado (em memória).</summary>
    public static ConteudoPacote Curto(string processo = "0001/2099", string requerente = "FULANO DE EXEMPLO (FICTICIO)") =>
        Importador.Importar(CurtoJson(processo, requerente), Ambiente.Corpus("curto.json"), Modelo, Ambiente.Contexto());

    /// <summary>Copia a árvore fictícia para <paramref name="pasta"/> e grava nela o pacote do 0001.</summary>
    public static ArvoreFicticia Arvore(string pasta)
    {
        var raiz = Path.Combine(pasta, "processos");
        Copiar(Ambiente.Corpus("processos"), raiz);
        var processo = Processos.Listar(raiz, CancellationToken.None).Itens.Single(p => p.NomePasta.StartsWith("0001 - 2099", StringComparison.Ordinal));
        var caminho = Path.Combine(processo.Pasta, NomesArquivo.Importacao(processo.Numero, processo.Ano));
        Pacote.Gravar(caminho, Curto(), null);
        return new ArvoreFicticia(raiz, processo, caminho, Pacote.Ler(caminho));
    }

    static void Copiar(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var d in Directory.GetDirectories(origem, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destino, Path.GetRelativePath(origem, d)));
        foreach (var f in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(destino, Path.GetRelativePath(origem, f)));
    }

    /// <summary>(caminho relativo, sha256) de cada arquivo sob <paramref name="raiz"/>, por caminho: para conferir que nada mudou.</summary>
    public static IReadOnlyList<(string Caminho, string Sha)> Retrato(string raiz) =>
        Directory.GetFiles(raiz, "*", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(raiz, f), DocxFicticio.Sha(File.ReadAllBytes(f))))
            .OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
}
