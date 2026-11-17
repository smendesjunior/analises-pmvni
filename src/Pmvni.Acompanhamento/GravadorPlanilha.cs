using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pmvni.Acompanhamento;

/// <summary>O novo valor de uma célula: texto, data ou número; tudo null = apagar o conteúdo (o estilo fica).</summary>
public sealed record ValorNovo(string? Texto = null, DateTime? Data = null, double? Numero = null)
{
    public static readonly ValorNovo Vazio = new();
    public bool EhVazio => string.IsNullOrEmpty(Texto) && Data is null && Numero is null;
}

/// <summary>Uma mudança numa linha: <see cref="Linha"/> null = processo novo (vai para a primeira linha livre da tabela).
/// <see cref="ProcessoEsperado"/> confere que a linha ainda é daquele processo (a planilha pode ter mudado no Excel); se não for, a linha
/// é procurada pelo número do processo. <see cref="Excluir"/> apaga os valores da linha (os estilos ficam).</summary>
public sealed record Alteracao(int? Linha, string? ProcessoEsperado, IReadOnlyDictionary<int, ValorNovo> Valores, bool Excluir = false);

/// <summary>A planilha está aberta (travada) em outro programa, normalmente o Excel.</summary>
public sealed class PlanilhaEmUsoException(string mensagem, Exception interna) : IOException(mensagem, interna);

/// <summary>A linha que o app ia mudar não é mais daquele processo, e o processo não foi achado na planilha.</summary>
public sealed class ConflitoPlanilhaException(string mensagem) : Exception(mensagem);

/// <summary>Grava as mudanças do app na planilha, mexendo só nas células alteradas: formatação condicional, validações, tabela, fórmulas,
/// imagens e o restante do pacote ficam como estão. Antes de gravar, guarda uma cópia do arquivo em <c>pastaCopias</c> (as 15 últimas).
/// O arquivo é aberto exclusivo: aberto no Excel, a gravação falha com <see cref="PlanilhaEmUsoException"/> e nada muda.</summary>
public static class GravadorPlanilha
{
    static readonly XNamespace S = PacoteXlsx.S;

    /// <summary>Aplica as alterações e devolve a linha de cada uma (na ordem recebida).</summary>
    public static IReadOnlyList<int> Gravar(string caminho, IReadOnlyList<Alteracao> alteracoes, string? pastaCopias = null, DateTime? hoje = null)
    {
        FileStream fs;
        try { fs = new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) when (e is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new PlanilhaEmUsoException("A planilha está aberta em outro programa (provavelmente o Excel). Feche-a e tente de novo.", e);
        }
        using (fs)
        {
            var original = new byte[fs.Length];
            fs.ReadExactly(original);
            if (pastaCopias is not null) GuardarCopia(caminho, original, pastaCopias);

            var ms = new MemoryStream();
            ms.Write(original);
            ms.Position = 0;
            List<int> linhas;
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
                linhas = Aplicar(new PacoteXlsx(zip), alteracoes, hoje ?? DateTime.Today);
            fs.Position = 0;
            fs.SetLength(0);
            ms.Position = 0;
            ms.CopyTo(fs);
            fs.Flush(true);
            return linhas;
        }
    }

    static void GuardarCopia(string caminho, byte[] bytes, string pasta)
    {
        Directory.CreateDirectory(pasta);
        var nome = Path.GetFileNameWithoutExtension(caminho) + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture) + ".xlsx";
        File.WriteAllBytes(Path.Combine(pasta, nome), bytes);
        foreach (var velho in new DirectoryInfo(pasta).GetFiles("*.xlsx").OrderByDescending(f => f.LastWriteTimeUtc).Skip(15))
            try { velho.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal static List<int> Aplicar(PacoteXlsx p, IReadOnlyList<Alteracao> alteracoes, DateTime hoje)
    {
        var lida = LeitorPlanilha.Ler(p, "");
        var parte = p.Abas[0].Caminho;
        var doc = p.Ler(parte)!;
        var dados = doc.Root!.Element(S + "sheetData")!;
        var colProcesso = lida.Coluna(ChaveColuna.Processo)!.Indice;
        var (_, _, fimTabela) = LeitorPlanilha.IntervaloDaTabela(p, parte, lida.LinhaCabecalho);
        var colunas = lida.Colunas.Select(c => c.Indice).ToList();
        var ocupadas = lida.Linhas.Select(l => l.Linha).ToHashSet();
        var modelo = lida.Linhas.Count > 0 ? lida.Linhas[0].Linha : lida.LinhaCabecalho + 1;
        var resultado = new List<int>();

        foreach (var a in alteracoes)
        {
            int linha;
            if (a.Linha is { } l)
            {
                linha = l;
                if (a.ProcessoEsperado is { } esperado)
                {
                    var atual = lida.Linhas.FirstOrDefault(x => x.Linha == l)?.ProcessoNormalizado;
                    if (!string.Equals(atual, Planilha.ProcessoNormal(esperado), StringComparison.OrdinalIgnoreCase))
                    {
                        var achada = lida.Linhas.FirstOrDefault(x => string.Equals(x.ProcessoNormalizado, Planilha.ProcessoNormal(esperado), StringComparison.OrdinalIgnoreCase))
                            ?? throw new ConflitoPlanilhaException($"O processo {esperado} não está mais na planilha (ela mudou fora do app). Recarregue e tente de novo.");
                        linha = achada.Linha;
                    }
                }
            }
            else
            {
                linha = lida.LinhaCabecalho + 1;
                while (ocupadas.Contains(linha)) linha++;
                ocupadas.Add(linha);
            }

            var row = Linha(dados, linha, Linha(dados, modelo, null));
            if (a.Excluir)
            {
                foreach (var c in colunas) Escrever(row, linha, c, ValorNovo.Vazio, null);
                ocupadas.Remove(linha);
            }
            else
            {
                foreach (var (c, v) in a.Valores) Escrever(row, linha, c, v, Celula(Linha(dados, modelo, null), modelo, c));
                // Linha nova: as fórmulas da linha-modelo (ex.: dias sem atualização) vêm junto, com as referências na linha nova.
                if (a.Linha is null)
                    foreach (var c in colunas.Where(c => !a.Valores.ContainsKey(c)))
                        if (FormulaDe(dados, modelo, c) is { } f)
                        {
                            var cel = Celula(row, linha, c, Celula(Linha(dados, modelo, null), modelo, c));
                            cel.Elements().Remove();
                            cel.Attribute("t")?.Remove();
                            cel.Add(new XElement(S + "f", Deslocar(f, modelo, linha)));
                        }
            }
            resultado.Add(linha);
        }

        var ultimaUsada = ocupadas.DefaultIfEmpty(lida.LinhaCabecalho + 1).Max();
        if (fimTabela > 0 && ultimaUsada > fimTabela) EstenderAte(p, parte, doc, fimTabela, ultimaUsada);
        AtualizarDimensao(doc);
        p.Gravar(parte, doc);
        RecalcularAoAbrir(p);
        return resultado;
    }

    static XElement Linha(XElement dados, int r, XElement? modelo)
    {
        var existente = dados.Elements(S + "row").FirstOrDefault(x => (int?)x.Attribute("r") == r);
        if (existente is not null) return existente;
        var nova = new XElement(S + "row", new XAttribute("r", r));
        if (modelo is not null)
            foreach (var at in modelo.Attributes().Where(at => at.Name.LocalName is "s" or "customFormat" or "ht" or "customHeight" or "spans" || at.Name.LocalName == "dyDescent"))
                nova.SetAttributeValue(at.Name, at.Value);
        var depois = dados.Elements(S + "row").FirstOrDefault(x => ((int?)x.Attribute("r") ?? 0) > r);
        if (depois is null) dados.Add(nova); else depois.AddBeforeSelf(nova);
        return nova;
    }

    static XElement? Celula(XElement? row, int r, int c) =>
        row?.Elements(S + "c").FirstOrDefault(x => (string?)x.Attribute("r") == Planilha.Letra(c) + r.ToString(CultureInfo.InvariantCulture));

    static XElement Celula(XElement row, int r, int c, XElement? modelo)
    {
        var refc = Planilha.Letra(c) + r.ToString(CultureInfo.InvariantCulture);
        var existente = row.Elements(S + "c").FirstOrDefault(x => (string?)x.Attribute("r") == refc);
        if (existente is not null) return existente;
        var nova = new XElement(S + "c", new XAttribute("r", refc));
        if (modelo?.Attribute("s") is { } s) nova.SetAttributeValue("s", s.Value);
        var depois = row.Elements(S + "c").FirstOrDefault(x => (string?)x.Attribute("r") is { } rr && Planilha.Referencia(rr).Coluna > c);
        if (depois is null) row.Add(nova); else depois.AddBeforeSelf(nova);
        return nova;
    }

    static void Escrever(XElement row, int r, int c, ValorNovo v, XElement? modelo)
    {
        var existente = Celula(row, r, c);
        if (v.EhVazio)
        {
            if (existente is null) return;
            existente.Elements().Remove();
            existente.Attribute("t")?.Remove();
            return;
        }
        var cel = existente ?? Celula(row, r, c, modelo);
        cel.Elements().Remove();
        cel.Attribute("t")?.Remove();
        if (v.Data is { } d) cel.Add(new XElement(S + "v", d.ToOADate().ToString("R", CultureInfo.InvariantCulture)));
        else if (v.Numero is { } n) cel.Add(new XElement(S + "v", n.ToString("R", CultureInfo.InvariantCulture)));
        else
        {
            cel.SetAttributeValue("t", "inlineStr");
            var t = new XElement(S + "t", v.Texto);
            if (v.Texto!.Trim() != v.Texto) t.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            cel.Add(new XElement(S + "is", t));
        }
    }

    /// <summary>A fórmula da célula da linha-modelo (resolvida pela mestra, se for compartilhada); null sem fórmula.</summary>
    static string? FormulaDe(XElement dados, int r, int c)
    {
        var cel = Celula(dados.Elements(S + "row").FirstOrDefault(x => (int?)x.Attribute("r") == r), r, c);
        if (cel?.Element(S + "f") is not { } f) return null;
        if (f.Value.Length > 0 && (string?)f.Attribute("t") != "shared") return f.Value;
        var si = (string?)f.Attribute("si");
        var mestra = dados.Descendants(S + "f").FirstOrDefault(x => (string?)x.Attribute("t") == "shared" && (string?)x.Attribute("si") == si && x.Value.Length > 0);
        if (mestra is null) return null;
        var linhaMestra = Planilha.Referencia((string)mestra.Parent!.Attribute("r")!).Linha;
        return Deslocar(mestra.Value, linhaMestra, r);
    }

    /// <summary>As referências relativas à linha <paramref name="de"/> passam para a linha <paramref name="para"/> (as com $ ficam).</summary>
    internal static string Deslocar(string formula, int de, int para) =>
        Regex.Replace(formula, @"""[^""]*""|(\$?)([A-Z]{1,3})(\$?)(\d+)\b", m =>
        {
            if (m.Value.StartsWith('"') || m.Groups[3].Value == "$") return m.Value;
            var linha = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            return m.Groups[1].Value + m.Groups[2].Value + (linha + para - de).ToString(CultureInfo.InvariantCulture);
        });

    /// <summary>A tabela, a formatação condicional e as validações que terminavam na última linha da tabela passam a ir até a nova.</summary>
    static void EstenderAte(PacoteXlsx p, string parte, XDocument doc, int fimAntigo, int fimNovo)
    {
        string Ajustar(string refs) => string.Join(' ', refs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(r =>
        {
            var ab = r.Split(':');
            if (ab.Length != 2) return r;
            var (c2, l2) = Planilha.Referencia(ab[1]);
            return l2 == fimAntigo ? ab[0] + ":" + Planilha.Letra(c2) + fimNovo.ToString(CultureInfo.InvariantCulture) : r;
        }));
        foreach (var (tipo, caminho) in p.Relacoes(parte).Values)
        {
            if (!tipo.EndsWith("/table", StringComparison.Ordinal)) continue;
            var t = p.Ler(caminho);
            if (t?.Root is not { } raiz || (string?)raiz.Attribute("ref") is not { } r) continue;
            raiz.SetAttributeValue("ref", Ajustar(r));
            if (raiz.Element(S + "autoFilter") is { } af && (string?)af.Attribute("ref") is { } ar) af.SetAttributeValue("ref", Ajustar(ar));
            p.Gravar(caminho, t);
        }
        foreach (var cf in doc.Root!.Elements(S + "conditionalFormatting"))
            if ((string?)cf.Attribute("sqref") is { } sq) cf.SetAttributeValue("sqref", Ajustar(sq));
        foreach (var dv in doc.Root!.Element(S + "dataValidations")?.Elements(S + "dataValidation") ?? [])
            if ((string?)dv.Attribute("sqref") is { } sq) dv.SetAttributeValue("sqref", Ajustar(sq));
    }

    static void AtualizarDimensao(XDocument doc)
    {
        if (doc.Root!.Element(S + "dimension") is not { } dim || (string?)dim.Attribute("ref") is not { } r || !r.Contains(':')) return;
        var ultima = doc.Root!.Element(S + "sheetData")!.Elements(S + "row").Select(x => (int?)x.Attribute("r") ?? 0).DefaultIfEmpty(1).Max();
        var (c2, l2) = Planilha.Referencia(r.Split(':')[1]);
        if (ultima > l2) dim.SetAttributeValue("ref", r.Split(':')[0] + ":" + Planilha.Letra(c2) + ultima.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>O Excel refaz as contas ao abrir (fórmulas novas sem valor guardado) e reconstrói a cadeia de cálculo, que é apagada: uma
    /// cadeia que cita células mudadas faria o Excel "reparar" o arquivo.</summary>
    static void RecalcularAoAbrir(PacoteXlsx p)
    {
        var wb = p.Ler("xl/workbook.xml")!;
        var raiz = wb.Root!;
        var calc = raiz.Element(S + "calcPr");
        if (calc is null)
        {
            calc = new XElement(S + "calcPr");
            var antes = raiz.Element(S + "definedNames") ?? raiz.Element(S + "sheets");
            antes!.AddAfterSelf(calc);
        }
        calc.SetAttributeValue("fullCalcOnLoad", "1");
        p.Gravar("xl/workbook.xml", wb);

        if (!p.Existe("xl/calcChain.xml")) return;
        p.Apagar("xl/calcChain.xml");
        var nomeRels = PacoteXlsx.NomeRels("xl/workbook.xml");
        if (p.Ler(nomeRels) is { } rels)
        {
            rels.Root!.Elements(PacoteXlsx.PR + "Relationship").Where(x => ((string?)x.Attribute("Type") ?? "").EndsWith("/calcChain", StringComparison.Ordinal)).Remove();
            p.Gravar(nomeRels, rels);
        }
        if (p.Ler("[Content_Types].xml") is { } ct)
        {
            ct.Root!.Elements().Where(x => ((string?)x.Attribute("PartName") ?? "") == "/xl/calcChain.xml").Remove();
            p.Gravar("[Content_Types].xml", ct);
        }
    }
}
