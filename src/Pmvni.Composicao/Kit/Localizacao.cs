namespace Pmvni.Composicao.Kit;

/// <summary>Agrupamento da ficha de localização em grupos e colunas (o bloco de <c>main()</c> do gerar_relatorio.py do kit).</summary>
public static class Localizacao
{
    private static readonly string[] OrdemDosGrupos = { "Identificação", "Cadastro e entorno", "Áreas e edificação", "Zona e condicionantes" };

    /// <summary>Coluna (0 ou 1) dos quatro grupos conhecidos.</summary>
    private static int ColunaConhecida(string titulo) => Array.IndexOf(OrdemDosGrupos, titulo) switch { 0 or 1 => 0, _ => 1 };

    /// <summary>
    /// Devolve o equivalente de <c>dict(d['localizacao'], legenda=leg, grupos=grupos, colunas=colunas)</c>
    /// (sem <c>imagem_uri</c>, que depende do arquivo): <c>grupos</c> = lista de {titulo, dados}, em ordem de
    /// aparição e com grupos repetidos reunidos; <c>colunas</c> = duas colunas de grupos (as vazias saem);
    /// <c>legenda</c> = a dos dados ou a padrão.
    /// </summary>
    public static Dictionary<string, object?> Agrupar(IReadOnlyDictionary<string, object?> localizacao)
    {
        var grupos = new List<Dictionary<string, object?>>();
        foreach (var x in Dados.Lista(Dados.Obter(localizacao, "dados", new List<object?>())))
        {
            var t = Dados.Obter(x, "grupo");
            if (!PythonTexto.Verdadeiro(t)) t = "Dados do imóvel";
            Dictionary<string, object?>? g;
            if (grupos.Count == 0 || !Equals(grupos[^1]["titulo"], t))
            {
                g = grupos.FirstOrDefault(gr => Equals(gr["titulo"], t));
                if (g is null)
                {
                    g = new Dictionary<string, object?> { ["titulo"] = t, ["dados"] = new List<object?>() };
                    grupos.Add(g);
                }
            }
            else g = grupos[^1];
            ((List<object?>)g["dados"]!).Add(x);
        }

        List<List<Dictionary<string, object?>>> colunas;
        if (grupos.All(g => g["titulo"] is string s && Array.IndexOf(OrdemDosGrupos, s) >= 0))
        {
            colunas = new List<List<Dictionary<string, object?>>>();
            foreach (var k in new[] { 0, 1 })
                colunas.Add(grupos.Where(g => ColunaConhecida((string)g["titulo"]!) == k)
                    .OrderBy(g => Array.IndexOf(OrdemDosGrupos, (string)g["titulo"]!)).ToList());
        }
        else
        {
            colunas = new List<List<Dictionary<string, object?>>> { new(), new() };
            var soma = new[] { 0, 0 };
            foreach (var g in grupos)
            {
                var k = soma[0] <= soma[1] ? 0 : 1;
                colunas[k].Add(g);
                soma[k] += ((List<object?>)g["dados"]!).Count + 1;
            }
        }

        var legenda = Dados.Obter(localizacao, "legenda");
        if (!PythonTexto.Verdadeiro(legenda)) legenda = Situacoes.LegendaPadrao;

        var r = new Dictionary<string, object?>();
        foreach (var par in localizacao) r[par.Key] = par.Value;
        r["legenda"] = legenda;
        r["grupos"] = grupos.Cast<object?>().ToList();
        r["colunas"] = colunas.Where(c => c.Count > 0).Select(c => (object?)c.Cast<object?>().ToList()).ToList();
        return r;
    }
}
