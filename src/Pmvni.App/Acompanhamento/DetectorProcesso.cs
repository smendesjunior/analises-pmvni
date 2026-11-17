using System.Text.RegularExpressions;
using Pmvni.Acompanhamento;
using Pmvni.Revisao;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Pmvni.App;

/// <summary>Procura o local e o autor do projeto de um processo nos arquivos que o editor já conhece (2026-11-17), nesta ordem:
/// 1) o relatório analisado mais recente (.rpmvni ou dados*.json da IA: capa com LOCALIZAÇÃO e AUTORIA);
/// 2) a consulta QGIS do processo (EVIDENCIA_QGIS.json, gis_consulta.json…: a inscrição do lote) cruzada com o cadastro da base QGIS;
/// 3) o carimbo das pranchas em PDF da pasta PROJETO ("AUTOR DO PROJETO:", "ENDEREÇO:"…).
/// Só lê. Sem UI: roda fora da thread da janela.</summary>
static class DetectorProcesso
{
    const long LimiteJson = 30L * 1024 * 1024;
    const long LimitePdf = 80L * 1024 * 1024;
    static readonly string[] NomesConsulta = ["EVIDENCIA_QGIS.json", "gis_consulta.json", "cadastro_consulta.json", "consulta_qgis.json"];

    public static Descoberta Descobrir(Processo p, Pmvni.Imoveis.BaseQgis? baseQgis)
    {
        DetalheProcesso detalhe;
        try { detalhe = Processos.Detalhar(p, CancellationToken.None); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Descoberta.Nada; }

        var d = Descoberta.Nada;
        foreach (var r in detalhe.Relatorios)
        {
            if (!string.IsNullOrWhiteSpace(d.Local) && !string.IsNullOrWhiteSpace(d.Autor)) break;
            try
            {
                byte[] json;
                if (r.Tipo == TipoRelatorio.Pacote) json = Pacote.Ler(r.Caminho).Conteudo.Recebido;
                else
                {
                    if (new FileInfo(r.Caminho).Length > LimiteJson) continue;
                    json = File.ReadAllBytes(r.Caminho);
                }
                d = d.Completar(DadosDoProcesso.DaCapa(json, "relatório " + Path.GetFileName(r.Caminho)));
            }
            catch (Exception e) when (e is not OutOfMemoryException) { }
        }

        if (string.IsNullOrWhiteSpace(d.Local) && baseQgis is not null && Inscricao(p.Pasta) is { } insc)
        {
            var reg = baseQgis.Cadastro.PorInscricao(insc).FirstOrDefault();
            if (reg is not null && reg.Endereco.Trim().Length > 0)
            {
                var local = reg.Endereco + (reg.Bairro.Length > 0 ? ", " + reg.Bairro : "");
                d = d.Completar(new Descoberta(local, $"consulta QGIS (inscrição {insc})", null, null));
            }
        }

        if (string.IsNullOrWhiteSpace(d.Local) || string.IsNullOrWhiteSpace(d.Autor))
            foreach (var pdf in detalhe.Projeto.Where(a => a.Caminho.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && a.Tamanho <= LimitePdf)
                         .OrderByDescending(a => a.AlteradoUtc).Take(5))
            {
                if (!string.IsNullOrWhiteSpace(d.Local) && !string.IsNullOrWhiteSpace(d.Autor)) break;
                d = d.Completar(DoCarimbo(pdf.Caminho));
            }
        return d;
    }

    /// <summary>A inscrição do lote da consulta QGIS mais recente da pasta do processo (até 3 níveis).</summary>
    internal static string? Inscricao(string pasta)
    {
        try
        {
            var arquivos = new List<FileInfo>();
            void Varrer(string dir, int nivel)
            {
                foreach (var n in NomesConsulta)
                {
                    var f = new FileInfo(Path.Combine(dir, n));
                    if (f.Exists && f.Length <= LimiteJson) arquivos.Add(f);
                }
                if (nivel < 3)
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if (!Path.GetFileName(sub).Equals("local-cache", StringComparison.OrdinalIgnoreCase)) Varrer(sub, nivel + 1);
            }
            Varrer(pasta, 0);
            foreach (var f in arquivos.OrderByDescending(f => f.LastWriteTimeUtc))
                if (DadosDoProcesso.InscricaoDaConsulta(File.ReadAllBytes(f.FullName)) is { } i) return i;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    static readonly Regex RotuloAutor = new(@"^(?:AUTOR(?:IA)?(?:\s+DO\s+PROJETO)?|RESPONS[ÁA]VEL\s+T[ÉE]CNICO|PROJETISTA|PROFISSIONAL\s+RESPONS[ÁA]VEL)\b\s*[:\-–]?\s*(?<v>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex RotuloLocal = new(@"^(?:ENDERE[ÇC]O(?:\s+DA\s+OBRA)?|LOCAL(?:\s+DA\s+OBRA)?|LOCALIZA[ÇC][ÃA]O|LOGRADOURO)\b\s*[:\-–]\s*(?<v>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex Registro = new(@"\s*[-–,]?\s*(?:CAU|CREA|RRT|ART|REGISTRO)\b.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>O carimbo da primeira página da prancha: o texto depois de "AUTOR DO PROJETO" (ou na linha de baixo) e o "ENDEREÇO:".</summary>
    internal static Descoberta DoCarimbo(string pdf)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            if (doc.NumberOfPages == 0) return Descoberta.Nada;
            var texto = ContentOrderTextExtractor.GetText(doc.GetPage(1));
            return DoTexto(texto, "prancha " + Path.GetFileName(pdf));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return Descoberta.Nada; }
    }

    internal static Descoberta DoTexto(string texto, string fonte)
    {
        var linhas = texto.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        string? autor = null, local = null;
        for (var i = 0; i < linhas.Count; i++)
        {
            if (autor is null && RotuloAutor.Match(linhas[i]) is { Success: true } ma)
            {
                var v = Registro.Replace(ma.Groups["v"].Value, "").Trim(' ', ':', '-', '–');
                if (v.Length == 0 && i + 1 < linhas.Count) v = Registro.Replace(linhas[i + 1], "").Trim(' ', ':', '-', '–');
                if (NomeValido(v)) autor = v;
            }
            if (local is null && RotuloLocal.Match(linhas[i]) is { Success: true } ml)
            {
                var v = ml.Groups["v"].Value.Trim(' ', ':', '-', '–');
                if (v.Length >= 6 && v.Length <= 120 && v.Any(char.IsLetter)) local = v;
            }
        }
        return new Descoberta(local, local is null ? null : fonte, autor, autor is null ? null : fonte);
    }

    static bool NomeValido(string v) =>
        v.Length is >= 5 and <= 70 && v.Count(char.IsLetter) >= 5 && v.Contains(' ') && !v.Contains(':') && !v.Any(char.IsDigit);
}
