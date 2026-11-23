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
/// é procurada pelo número do processo. <see cref="Excluir"/> apaga os valores da linha (os estilos e as fórmulas ficam).
/// <see cref="SomenteSeVazias"/> (preenchimento automático): cada valor só entra se a célula ainda estiver vazia na hora de gravar.</summary>
public sealed record Alteracao(int? Linha, string? ProcessoEsperado, IReadOnlyDictionary<int, ValorNovo> Valores, bool Excluir = false, bool SomenteSeVazias = false);

/// <summary>A planilha está aberta (travada) em outro programa, normalmente o Excel.</summary>
public sealed class PlanilhaEmUsoException(string mensagem, Exception interna) : IOException(mensagem, interna);

/// <summary>A linha que o app ia mudar não é mais daquele processo, e o processo não foi achado na planilha.</summary>
public sealed class ConflitoPlanilhaException(string mensagem) : Exception(mensagem);

/// <summary>Grava as mudanças do app na planilha, mexendo só nas células alteradas: formatação condicional, validações, tabela, fórmulas,
/// imagens e o restante do pacote ficam como estão. Antes de gravar, guarda uma cópia do arquivo em <c>pastaCopias</c> (as 15 últimas
/// de cada planilha). O arquivo é aberto exclusivo: aberto no Excel, a gravação falha com <see cref="PlanilhaEmUsoException"/> e nada muda.
/// A versão nova é escrita num <c>.tmp</c> ao lado, conferida relendo e só então trocada (<c>File.Replace</c>), como o pacote do relatório:
/// uma falha no meio nunca deixa a planilha pela metade.</summary>
public static class GravadorPlanilha
{
    static readonly XNamespace S = PacoteXlsx.S;
    const string MsgEmUso = "A planilha está aberta em outro programa (provavelmente o Excel). Feche-a e tente de novo.";

    /// <summary>Aplica as alterações na aba <paramref name="aba"/> (null = a primeira) e devolve a linha de cada uma (na ordem recebida).</summary>
    public static IReadOnlyList<int> Gravar(string caminho, IReadOnlyList<Alteracao> alteracoes, string? pastaCopias = null, DateTime? hoje = null, string? aba = null)
    {
        FileStream fs;
        try { fs = new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) when (e is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new PlanilhaEmUsoException(MsgEmUso, e);
        }
        var pastaPlanilha = Path.GetDirectoryName(Path.GetFullPath(caminho))!;
        var tmp = Path.Combine(pastaPlanilha, "~" + Path.GetFileName(caminho) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        ApagarTemporariosVelhos(pastaPlanilha, Path.GetFileName(caminho));
        try
        {
            List<int> linhas;
            using (fs)
            {
                var original = new byte[fs.Length];
                fs.ReadExactly(original);

                var ms = new MemoryStream();
                ms.Write(original);
                ms.Position = 0;
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
                    linhas = Aplicar(new PacoteXlsx(zip), alteracoes, hoje ?? DateTime.Today, aba);
                // A cópia só depois de as alterações caberem na planilha: uma gravação que falha (e é tentada de novo a cada 15 s) não
                // enche a pasta das cópias com o mesmo arquivo nem empurra o histórico bom para fora das 15.
                if (pastaCopias is not null) GuardarCopia(caminho, original, pastaCopias);

                using (var saida = new FileStream(ArquivoGerado.Caminho(tmp), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    File.SetAttributes(tmp, FileAttributes.Hidden);
                    ms.Position = 0;
                    ms.CopyTo(saida);
                    saida.Flush(true);
                }
                // Confere relendo o .tmp: tem de abrir como planilha, com a mesma aba e as mesmas linhas de processo que a gravação deixou.
                using (var conf = new ZipArchive(new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read))
                    LeitorPlanilha.Ler(new PacoteXlsx(conf), caminho, aba);
            }
            // O arquivo só é solto aqui; outro programa pode pegá-lo nesse instante: aí é "em uso" e nada mudou.
            File.SetAttributes(tmp, FileAttributes.Normal);
            try { File.Replace(tmp, caminho, null, ignoreMetadataErrors: true); }
            catch (IOException e) when (e is not FileNotFoundException and not DirectoryNotFoundException)
            {
                throw new PlanilhaEmUsoException(MsgEmUso, e);
            }
            return linhas;
        }
        finally
        {
            try { if (File.Exists(tmp)) { File.SetAttributes(tmp, FileAttributes.Normal); File.Delete(tmp); } }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Temporários de gravações que não terminaram (o editor caiu entre escrever o .tmp e a troca), com mais de 1 hora: saem.</summary>
    static void ApagarTemporariosVelhos(string pasta, string nome)
    {
        try
        {
            foreach (var f in new DirectoryInfo(pasta).GetFiles("~" + nome + ".*.tmp"))
                if (DateTime.UtcNow - f.LastWriteTimeUtc > TimeSpan.FromHours(1))
                    try { f.Attributes = FileAttributes.Normal; f.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A pasta das cópias de uma planilha: uma subpasta por arquivo (nome + resumo do caminho), para a limpeza não misturar planilhas.</summary>
    public static string PastaDasCopias(string caminho, string pastaCopias)
    {
        var resumo = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(caminho).ToUpperInvariant())))[..8];
        return Path.Combine(pastaCopias, Path.GetFileNameWithoutExtension(caminho) + " " + resumo);
    }

    static void GuardarCopia(string caminho, byte[] bytes, string pastaCopias)
    {
        var pasta = PastaDasCopias(caminho, pastaCopias);
        Directory.CreateDirectory(pasta);
        // Milissegundos e, se ainda assim o nome existir, um contador: uma cópia nunca sobrescreve outra.
        var baseNome = Path.GetFileNameWithoutExtension(caminho) + " " + DateTime.Now.ToString("yyyy-MM-dd HHmmss-fff", CultureInfo.InvariantCulture);
        var destino = Path.Combine(pasta, baseNome + ".xlsx");
        for (var k = 2; File.Exists(destino); k++) destino = Path.Combine(pasta, $"{baseNome} ({k}).xlsx");
        using (var f = new FileStream(ArquivoGerado.Caminho(destino), FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.Write(bytes);
        foreach (var velho in new DirectoryInfo(pasta).GetFiles("*.xlsx").OrderByDescending(f => f.CreationTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).Skip(15))
            try { velho.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        // As cópias da versão 0.3.0 ficavam soltas na raiz da pasta das cópias: seguem a mesma regra (as 15 mais novas ficam).
        foreach (var velho in new DirectoryInfo(pastaCopias).GetFiles("*.xlsx").OrderByDescending(f => f.LastWriteTimeUtc).Skip(15))
            try { velho.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    internal static List<int> Aplicar(PacoteXlsx p, IReadOnlyList<Alteracao> alteracoes, DateTime hoje, string? aba = null)
    {
        var lida = LeitorPlanilha.Ler(p, "", aba);
        if (aba is not null && lida.AbaPedidaAusente)
            throw new ConflitoPlanilhaException($"A aba \"{aba}\" não está mais na planilha (ela mudou fora do app). Recarregue e tente de novo.");
        var parte = p.Abas.First(a => a.Nome == lida.Aba).Caminho;
        var doc = p.Ler(parte)!;
        var dados = doc.Root!.Element(S + "sheetData")!;
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
                    var processoEsperado = Planilha.ProcessoNormal(esperado);
                    var atual = lida.Linhas.FirstOrDefault(x => x.Linha == l)?.ProcessoNormalizado;
                    if (!string.Equals(atual, processoEsperado, StringComparison.OrdinalIgnoreCase))
                    {
                        var achada = lida.Linhas.FirstOrDefault(x => string.Equals(x.ProcessoNormalizado, processoEsperado, StringComparison.OrdinalIgnoreCase))
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
                // As fórmulas ficam (a linha volta a ser "livre", só com fórmula): apagar a mestra de uma fórmula compartilhada estragaria a coluna.
                foreach (var c in colunas)
                    if (Celula(row, linha, c)?.Element(S + "f") is null) Escrever(row, linha, c, ValorNovo.Vazio, null);
                ocupadas.Remove(linha);
            }
            else
            {
                var atual = lida.Linhas.FirstOrDefault(x => x.Linha == linha);
                foreach (var (c, v) in a.Valores)
                {
                    // Preenchimento automático: o que já está na célula na hora de gravar (digitado no Excel ou no app) nunca é trocado.
                    if (a.SomenteSeVazias && atual?.Celula(c) is { } existe && (existe.Texto.Trim().Length > 0 || existe.Numero is not null)) continue;
                    DesfazerMestra(dados, Celula(row, linha, c));
                    Escrever(row, linha, c, v, Celula(Linha(dados, modelo, null), modelo, c));
                }
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
        var (colMestra, linhaMestra) = Planilha.Referencia((string)mestra.Parent!.Attribute("r")!);
        return Deslocar(mestra.Value, linhaMestra, r, c - colMestra);
    }

    /// <summary>As referências relativas à linha <paramref name="de"/> passam para a linha <paramref name="para"/> e andam
    /// <paramref name="colunas"/> colunas (as partes com $ ficam). Nomes de função como LOG10( e DEC2BIN( não são referências.</summary>
    internal static string Deslocar(string formula, int de, int para, int colunas = 0) =>
        Regex.Replace(formula, @"""[^""]*""|'(?:[^']|'')*'|\[[^\]]*\]|(?<![A-Za-z0-9_.])(\$?)([A-Z]{1,3})(\$?)(\d+)(?![A-Za-z0-9_(!])", m =>
        {
            if (!m.Groups[2].Success) return m.Value;
            var col = m.Groups[1].Value == "$" || colunas == 0 ? m.Groups[2].Value : Planilha.Letra(Planilha.Indice(m.Groups[2].Value) + colunas);
            var linha = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            var novaLinha = m.Groups[3].Value == "$" ? linha : linha + para - de;
            return m.Groups[1].Value + col + m.Groups[3].Value + novaLinha.ToString(CultureInfo.InvariantCulture);
        });

    /// <summary>Antes de a célula mestra de uma fórmula compartilhada receber outro valor, cada célula do grupo passa a ter a própria fórmula
    /// (deslocada a partir da mestra); sem isso, as outras linhas ficariam apontando para uma mestra que não existe e o Excel "repararia" o arquivo.</summary>
    static void DesfazerMestra(XElement dados, XElement? cel)
    {
        if (cel?.Element(S + "f") is not { } f || (string?)f.Attribute("t") != "shared" || f.Value.Length == 0) return;
        var si = (string?)f.Attribute("si");
        var (cm, lm) = Planilha.Referencia((string)cel.Attribute("r")!);
        foreach (var dep in dados.Descendants(S + "f").Where(x => x != f && (string?)x.Attribute("t") == "shared" && (string?)x.Attribute("si") == si && x.Value.Length == 0).ToList())
        {
            var (cd, ld) = Planilha.Referencia((string)dep.Parent!.Attribute("r")!);
            dep.ReplaceWith(new XElement(S + "f", Deslocar(f.Value, lm, ld, cd - cm)));
        }
    }

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
