using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O que se sabe de uma pasta de processo para ligá-la a um imóvel: a inscrição da consulta QGIS da análise e o texto dos PDFs
/// (escritura, matrícula, projeto; null enquanto não foi lido).</summary>
public sealed record PistasProcesso(Processo Processo, string? InscricaoAnalise, string? TextoPdfs);

public sealed record NotaProcesso(Processo Processo, int Nota, string Motivo);

/// <summary>Em que pasta de processo o BCI vai ser salvo (2026-11-17, pedido do usuário: "sempre na pasta do processo, em DOCUMENTOS"; "procure
/// por pasta de processo, nome do requerente, na escritura, no projeto"). Cada processo ganha uma nota pelas pistas; vale o de maior nota,
/// se for o único com ela; empate pede escolha; nenhuma pista pede para criar a pasta do processo.</summary>
public static class VinculoBci
{
    public const int InscricaoExata = 100, MesmoLote = 60, Requerente = 50, InscricaoNoPdf = 90, NomeNoPdf = 30;

    public static NotaProcesso Pontuar(string inscricao, string? contribuinte, PistasProcesso p) => Pontuar(inscricao, contribuinte, [], p);

    /// <summary>Com os sócios (2026-11-17, pedido do usuário: "precisa ver o nome dos sócios também"): o requerente ou os documentos podem ter o
    /// nome de um sócio, não o do contribuinte ("FULANO E SÓCIO (A)(S)").</summary>
    public static NotaProcesso Pontuar(string inscricao, string? contribuinte, IReadOnlyList<string> socios, PistasProcesso p)
    {
        var nomes = new[] { contribuinte }.Concat(socios).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var alvo = Normalizar(inscricao);
        var nota = 0;
        var motivos = new List<string>();
        void Somar(int n, string m) { nota += n; motivos.Add(m); }

        if (alvo is not null && Normalizar(p.InscricaoAnalise) is { } daAnalise)
        {
            if (daAnalise == alvo) Somar(InscricaoExata, "inscrição da análise");
            else if (daAnalise[..11] == alvo[..11]) Somar(MesmoLote, "mesmo lote da análise");
        }
        if (NomesBatem(p.Processo.Requerente, contribuinte)) Somar(Requerente, "requerente");
        else if (socios.FirstOrDefault(s => NomesBatem(p.Processo.Requerente, s)) is { } socio) Somar(Requerente, "requerente é o sócio " + socio.Trim());
        if (p.TextoPdfs is { Length: > 0 } texto)
        {
            if (alvo is not null && InscricaoNoTexto(texto, alvo)) Somar(InscricaoNoPdf, "inscrição nos documentos");
            else if (nomes.Any(n => NomeNoTexto(texto, n))) Somar(NomeNoPdf, "nome nos documentos");
        }
        return new NotaProcesso(p.Processo, nota, string.Join(", ", motivos));
    }

    /// <summary>O processo escolhido (null se nenhum tem pista ou se há empate no topo) e os empatados no topo.</summary>
    public static (NotaProcesso? Escolhido, IReadOnlyList<NotaProcesso> Empate) Escolher(IEnumerable<NotaProcesso> notas)
    {
        var com = notas.Where(n => n.Nota > 0).OrderByDescending(n => n.Nota).ToList();
        if (com.Count == 0) return (null, []);
        var topo = com.Where(n => n.Nota == com[0].Nota).ToList();
        return topo.Count == 1 ? (topo[0], []) : (null, topo);
    }

    /// <summary>Os sócios e demais nomes ligados ao imóvel no cadastro: colunas com nome de pessoa ("nomes_razoes_encontrados", "socios",
    /// "proprietarios"…), separadas por ";", "|", "/", "," ou quebra de linha. Sem o contribuinte, sem repetidos, só nomes de 2+ palavras.</summary>
    public static IReadOnlyList<string> Socios(IReadOnlyDictionary<string, string?> campos, string? contribuinte)
    {
        var r = new List<string>();
        foreach (var (k, v) in campos)
        {
            if (string.IsNullOrWhiteSpace(v) || k.Equals("nome_do_contribuinte", StringComparison.OrdinalIgnoreCase)) continue;
            var n = k.ToLowerInvariant();
            // "vinculos" (base QGIS, conferido em 17/11/2026): "FULANO DE TAL SILVA 00000000191, BELTRANA DE SOUZA LIMA 00000000272".
            if (!(n.Contains("vinculo") || n.Contains("socio") || n.Contains("propriet") || n.Contains("compromiss") || n.Contains("nomes") || n.Contains("razoes") || n.Contains("titular"))) continue;
            foreach (var parte in Regex.Split(v, @"[;|,\r\n]+"))
            {
                var nome = Regex.Replace(parte, @"\b\d[\d./\-]{7,}\d\b", " ").Trim();   // sem CPF/CNPJ
                if (Palavras(nome).Count < 2 || NomesBatem(nome, contribuinte) || r.Any(x => NomesBatem(x, nome))) continue;
                r.Add(nome);
            }
        }
        return r;
    }

    /// <summary>A mesma forma de 14 dígitos de Pmvni.Imoveis.Inscricoes.Normalizar (copiada: este arquivo também compila nos testes, sem Imóveis).</summary>
    static string? Normalizar(string? inscricao)
    {
        if (string.IsNullOrWhiteSpace(inscricao)) return null;
        var d = new string(inscricao.Where(char.IsAsciiDigit).ToArray());
        return d.Length switch
        {
            14 => d,
            13 => d[..7] + "0" + d[7..],
            15 when d[7] == '0' => d[..7] + d[8..],
            17 when d[11..14] == d[14..] => d[..14],
            _ => null,
        };
    }

    static readonly HashSet<string> Vazias = ["E", "DE", "DA", "DO", "DAS", "DOS", "SOCIO", "SOCIOS", "SOCIA", "A", "S", "LTDA", "ME", "EIRELI", "EPP", "SA", "OUTROS", "OUTRO", "OUTRA"];

    public static string Normal(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : ' ');
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    static List<string> Palavras(string? s) => Normal(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Vazias.Contains(w) && w.Length > 1).ToList();

    /// <summary>"JOANILSON GALAVOTE MOREIRA" bate com "JOANILSON GALAVOTE MOREIRA E SOCIO (A) (S)": todas as palavras (2 ou mais) de um estão
    /// no outro, sem contar "E", "DE", "SÓCIO"…</summary>
    public static bool NomesBatem(string? a, string? b)
    {
        var pa = Palavras(a); var pb = Palavras(b);
        if (pa.Count < 2 || pb.Count < 2) return false;
        var (menor, maior) = pa.Count <= pb.Count ? (pa, pb.ToHashSet()) : (pb, pa.ToHashSet());
        return menor.All(maior.Contains);
    }

    /// <summary>A inscrição no texto, com ou sem a máscara (01.01.118.0077.001, 01011180077001), sem pegar um pedaço de número maior.</summary>
    public static bool InscricaoNoTexto(string texto, string inscricao14)
    {
        var padrao = @"(?<!\d)" + string.Join(@"[\s.\-/]?", inscricao14.Select(c => c.ToString())) + @"(?!\d)";
        return Regex.IsMatch(texto, padrao);
    }

    /// <summary>O nome do contribuinte (sem "E SÓCIOS"…) no texto, com pelo menos duas palavras seguidas.</summary>
    public static bool NomeNoTexto(string texto, string? contribuinte)
    {
        var p = Palavras(contribuinte);
        if (p.Count < 2) return false;
        return Normal(texto).Contains(string.Join(' ', p), StringComparison.Ordinal);
    }
}
