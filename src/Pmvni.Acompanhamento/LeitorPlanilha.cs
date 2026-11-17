using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pmvni.Acompanhamento;

/// <summary>Lê a planilha padrão de acompanhamento: acha o cabeçalho (a linha com "Processo"), as colunas, as listas da validação de dados,
/// as cores da formatação condicional e as linhas de processo. Só lê: o arquivo é aberto compartilhado (pode estar aberto no Excel).</summary>
public static class LeitorPlanilha
{
    static readonly XNamespace S = PacoteXlsx.S;

    public static PlanilhaLida Ler(string caminho)
    {
        using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var ms = new MemoryStream();
        fs.CopyTo(ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return Ler(new PacoteXlsx(zip), caminho);
    }

    internal static PlanilhaLida Ler(PacoteXlsx p, string caminho)
    {
        var (aba, parte) = p.Abas[0];
        var doc = p.Ler(parte) ?? throw new InvalidDataException($"A aba \"{aba}\" não pôde ser lida.");
        var dados = doc.Root!.Element(S + "sheetData") ?? throw new InvalidDataException($"A aba \"{aba}\" está vazia.");

        // Todas as células, por linha e coluna (com as fórmulas compartilhadas resolvidas pelo texto da mestra).
        var celulas = new Dictionary<int, Dictionary<int, (Celula Valor, int Estilo)>>();
        var mestras = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in dados.Descendants(S + "c"))
            if (c.Element(S + "f") is { } f && (string?)f.Attribute("t") == "shared" && f.Value.Length > 0) mestras[(string?)f.Attribute("si") ?? ""] = f.Value;
        foreach (var row in dados.Elements(S + "row"))
        {
            var r = (int?)row.Attribute("r") ?? 0;
            var linha = celulas[r] = [];
            foreach (var c in row.Elements(S + "c"))
            {
                var refc = (string?)c.Attribute("r");
                if (refc is null) continue;
                var (col, _) = Planilha.Referencia(refc);
                var v = p.Valor(c);
                if (c.Element(S + "f") is { } f && f.Value.Length == 0 && mestras.TryGetValue((string?)f.Attribute("si") ?? "", out var m)) v = v with { Formula = m };
                linha[col] = (v, (int?)c.Attribute("s") ?? 0);
            }
        }
        Celula Em(int r, int c) => celulas.TryGetValue(r, out var l) && l.TryGetValue(c, out var x) ? x.Valor : Celula.Vazia;

        // Cabeçalho: a primeira linha (nas 60 primeiras) com "Processo" e mais dois títulos.
        var cab = celulas.Keys.Where(r => r <= 60).OrderBy(r => r)
            .FirstOrDefault(r => celulas[r].Values.Count(v => v.Valor.Texto.Trim().Length > 0 && v.Valor.Formula is null) >= 3
                                 && celulas[r].Values.Any(v => Planilha.ChaveDe(v.Valor.Texto) == ChaveColuna.Processo));
        if (cab == 0) throw new InvalidDataException("Não achei o cabeçalho da planilha (a linha com a coluna \"Processo\"). Ela segue o modelo padrão?");

        // Tabela do Excel (se houver) define o intervalo das colunas; senão, os títulos do cabeçalho.
        var (colIni, colFim, fimTabela) = IntervaloDaTabela(p, parte, cab);
        if (colIni < 0)
        {
            var titulos = celulas[cab].Where(kv => kv.Value.Valor.Texto.Trim().Length > 0).Select(kv => kv.Key).ToList();
            (colIni, colFim) = (titulos.Min(), titulos.Max());
        }

        var listas = Listas(p, doc, celulas);
        var regras = Regras(p, doc);
        var ultima = Math.Max(fimTabela, celulas.Keys.DefaultIfEmpty(cab).Max());

        var colunas = new List<Coluna>();
        for (var c = colIni; c <= colFim; c++)
        {
            var titulo = Em(cab, c).Texto.Trim();
            if (titulo.Length == 0) titulo = "Coluna " + Planilha.Letra(c);
            var chave = Planilha.ChaveDe(titulo);
            if (chave != ChaveColuna.Outra && colunas.Any(x => x.Chave == chave)) chave = ChaveColuna.Outra; // título repetido: só o primeiro tem o papel
            var amostra = Enumerable.Range(cab + 1, Math.Max(0, ultima - cab)).Select(r => (r, celulas.TryGetValue(r, out var l) && l.TryGetValue(c, out var x) ? x : (Celula.Vazia, 0))).ToList();
            var comValor = amostra.Where(a => !a.Item2.Item1.EhVazia).ToList();
            listas.TryGetValue(c, out var lista);
            TipoColuna tipo;
            if (lista is { Data: true } || comValor.Any(a => a.Item2.Item1.Numero is not null && p.EstiloDeData(a.Item2.Item2)) || chave is ChaveColuna.DataInicial or ChaveColuna.Recebido or ChaveColuna.Atualizacao)
                tipo = TipoColuna.Data;
            else if (lista is { Opcoes.Count: > 0 }) tipo = TipoColuna.Lista;
            else if (chave == ChaveColuna.DiasSemAtualizacao || (comValor.Count > 0 && comValor.Count(a => a.Item2.Item1.Formula is not null) * 2 >= comValor.Count)) tipo = TipoColuna.Calculada;
            else if (chave == ChaveColuna.Numero || (comValor.Count > 0 && comValor.All(a => a.Item2.Item1.Numero is not null))) tipo = TipoColuna.Numero;
            else tipo = TipoColuna.Texto;
            colunas.Add(new Coluna
            {
                Indice = c, Titulo = titulo, Chave = chave, Tipo = tipo,
                Opcoes = lista?.Opcoes ?? [],
                Regras = regras.Where(x => x.Colunas.Contains(c)).Select(x => x.Regra).OrderBy(x => x.Prioridade).ToList(),
            });
        }

        // Linhas de processo: abaixo do cabeçalho, com algum valor digitado (fórmula sozinha não conta).
        var linhas = new List<LinhaProcesso>();
        foreach (var r in celulas.Keys.Where(r => r > cab).OrderBy(r => r))
        {
            var vals = new Dictionary<int, Celula>();
            for (var c = colIni; c <= colFim; c++)
            {
                var v = Em(r, c);
                if (!v.EhVazia) vals[c] = v;
            }
            if (!vals.Values.Any(v => v.Formula is null && (v.Texto.Trim().Length > 0 || v.Numero is not null))) continue;
            linhas.Add(new LinhaProcesso { Linha = r, Celulas = vals });
        }

        // Títulos acima do cabeçalho (fora o brasão): o primeiro é o título, o segundo o subtítulo.
        var textos = celulas.Where(kv => kv.Key < cab).OrderBy(kv => kv.Key)
            .SelectMany(kv => kv.Value.Where(x => x.Key >= colIni && x.Key <= colFim).OrderBy(x => x.Key).Select(x => x.Value.Valor))
            .Where(v => v.Formula is null && v.Texto.Trim().Length > 0)
            .Select(v => v.Texto.Trim())
            .Where(t => !t.StartsWith("Prefeitura", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("Estado d", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var lida = new PlanilhaLida
        {
            Caminho = caminho, Aba = aba, Colunas = colunas, Linhas = linhas, LinhaCabecalho = cab,
            Titulo = textos.ElementAtOrDefault(0) ?? "", Subtitulo = textos.ElementAtOrDefault(1) ?? "",
        };
        foreach (var l in linhas) l.Dono = lida;
        return lida;
    }

    /// <summary>A tabela do Excel que contém o cabeçalho: colunas e a última linha; (−1, −1, 0) sem tabela.</summary>
    internal static (int ColIni, int ColFim, int Fim) IntervaloDaTabela(PacoteXlsx p, string parte, int cab)
    {
        foreach (var (tipo, caminho) in p.Relacoes(parte).Values)
        {
            if (!tipo.EndsWith("/table", StringComparison.Ordinal)) continue;
            var t = p.Ler(caminho)?.Root;
            var refT = (string?)t?.Attribute("ref");
            if (refT is null || !refT.Contains(':')) continue;
            var (a, b) = (Planilha.Referencia(refT.Split(':')[0]), Planilha.Referencia(refT.Split(':')[1]));
            if (a.Linha == cab) return (a.Coluna, b.Coluna, b.Linha);
        }
        return (-1, -1, 0);
    }

    sealed record Lista(IReadOnlyList<string> Opcoes, bool Data);

    /// <summary>A validação de dados por coluna: listas (literais ou intervalos, inclusive de outra aba) e datas.</summary>
    static Dictionary<int, Lista> Listas(PacoteXlsx p, XDocument doc, Dictionary<int, Dictionary<int, (Celula Valor, int Estilo)>> celulas)
    {
        var r = new Dictionary<int, Lista>();
        void Registrar(string? tipo, string? formula, string? sqref)
        {
            if (sqref is null) return;
            Lista? l = tipo switch
            {
                "list" when formula is not null => new Lista(Opcoes(p, formula, celulas), false),
                "date" => new Lista([], true),
                _ => null,
            };
            if (l is null) return;
            foreach (var c in ColunasDe(sqref)) r.TryAdd(c, l);
        }
        foreach (var dv in doc.Root!.Element(S + "dataValidations")?.Elements(S + "dataValidation") ?? [])
            Registrar((string?)dv.Attribute("type"), dv.Element(S + "formula1")?.Value, (string?)dv.Attribute("sqref"));
        foreach (var dv in doc.Root!.Descendants(PacoteXlsx.X14 + "dataValidation"))
            Registrar((string?)dv.Attribute("type"), dv.Element(PacoteXlsx.X14 + "formula1")?.Element(PacoteXlsx.XM + "f")?.Value, dv.Element(PacoteXlsx.XM + "sqref")?.Value);
        return r;
    }

    static IReadOnlyList<string> Opcoes(PacoteXlsx p, string formula, Dictionary<int, Dictionary<int, (Celula Valor, int Estilo)>> celulas)
    {
        var f = formula.Trim();
        if (f.StartsWith('"')) return f.Trim('"').Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        // Intervalo: "$AB$6:$AB$12" ou "Listas!$A$1:$A$9" (outra aba).
        var m = Regex.Match(f, @"^(?:'?(?<aba>[^'!]+)'?!)?\$?(?<c1>[A-Z]+)\$?(?<l1>\d+)(?::\$?(?<c2>[A-Z]+)\$?(?<l2>\d+))?$");
        if (!m.Success) return [];
        var dados = celulas;
        if (m.Groups["aba"].Success && p.Abas.FirstOrDefault(a => a.Nome == m.Groups["aba"].Value) is { Caminho.Length: > 0 } outra && outra != p.Abas[0])
        {
            dados = [];
            foreach (var row in p.Ler(outra.Caminho)?.Root!.Element(S + "sheetData")?.Elements(S + "row") ?? [])
            {
                var l = dados[(int?)row.Attribute("r") ?? 0] = [];
                foreach (var c in row.Elements(S + "c"))
                    if ((string?)c.Attribute("r") is { } rr) l[Planilha.Referencia(rr).Coluna] = (p.Valor(c), 0);
            }
        }
        int c1 = Planilha.Indice(m.Groups["c1"].Value), l1 = int.Parse(m.Groups["l1"].Value, CultureInfo.InvariantCulture);
        int c2 = m.Groups["c2"].Success ? Planilha.Indice(m.Groups["c2"].Value) : c1, l2 = m.Groups["l2"].Success ? int.Parse(m.Groups["l2"].Value, CultureInfo.InvariantCulture) : l1;
        var r = new List<string>();
        for (var l = l1; l <= l2; l++)
            for (var c = c1; c <= c2; c++)
                if (dados.TryGetValue(l, out var linha) && linha.TryGetValue(c, out var v) && v.Valor.Texto.Trim().Length > 0) r.Add(v.Valor.Texto.Trim());
        return r;
    }

    internal static IEnumerable<int> ColunasDe(string sqref)
    {
        foreach (var parte in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var ab = parte.Split(':');
            var a = Planilha.Referencia(ab[0]).Coluna;
            var b = ab.Length > 1 ? Planilha.Referencia(ab[1]).Coluna : a;
            for (var c = Math.Min(a, b); c <= Math.Max(a, b); c++) yield return c;
        }
    }

    /// <summary>A formatação condicional: igual a um texto (<c>$I9="EM ANÁLISE"</c>), contém texto e faixas numéricas.</summary>
    static List<(HashSet<int> Colunas, RegraCor Regra)> Regras(PacoteXlsx p, XDocument doc)
    {
        var r = new List<(HashSet<int>, RegraCor)>();
        var igual = new Regex(@"^\$?[A-Z]+\$?\d+\s*=\s*""(?<v>.*)""$");
        foreach (var cf in doc.Root!.Elements(S + "conditionalFormatting"))
        {
            var cols = ColunasDe((string?)cf.Attribute("sqref") ?? "").ToHashSet();
            foreach (var rule in cf.Elements(S + "cfRule"))
            {
                if ((int?)rule.Attribute("dxfId") is not { } dxf || p.CorDxf(dxf) is not { } cor) continue;
                var prio = (int?)rule.Attribute("priority") ?? 1000;
                var formulas = rule.Elements(S + "formula").Select(f => f.Value.Trim()).ToList();
                RegraCor? regra = (string?)rule.Attribute("type") switch
                {
                    "expression" when formulas.Count > 0 && igual.Match(formulas[0]) is { Success: true } m => new RegraCor(TipoRegra.Igual, m.Groups["v"].Value, 0, 0, cor, prio),
                    "containsText" => new RegraCor(TipoRegra.Contem, (string?)rule.Attribute("text") ?? "", 0, 0, cor, prio),
                    "cellIs" => CelulaE((string?)rule.Attribute("operator"), formulas, cor, prio),
                    _ => null,
                };
                if (regra is not null) r.Add((cols, regra));
            }
        }
        return r;
    }

    static RegraCor? CelulaE(string? op, List<string> f, Cor cor, int prio)
    {
        static double? N(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
        var a = N(f.ElementAtOrDefault(0));
        if (op == "equal" && a is null && f.Count > 0) return new RegraCor(TipoRegra.Igual, f[0].Trim('"'), 0, 0, cor, prio);
        if (a is null) return null;
        return op switch
        {
            "between" when N(f.ElementAtOrDefault(1)) is { } b => new RegraCor(TipoRegra.Entre, "", a.Value, b, cor, prio),
            "greaterThanOrEqual" => new RegraCor(TipoRegra.MaiorOuIgual, "", a.Value, 0, cor, prio),
            "lessThanOrEqual" => new RegraCor(TipoRegra.MenorOuIgual, "", a.Value, 0, cor, prio),
            "greaterThan" => new RegraCor(TipoRegra.Maior, "", a.Value, 0, cor, prio),
            "lessThan" => new RegraCor(TipoRegra.Menor, "", a.Value, 0, cor, prio),
            "equal" => new RegraCor(TipoRegra.Entre, "", a.Value, a.Value, cor, prio),
            _ => null,
        };
    }
}
