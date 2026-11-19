using System.Globalization;
using System.Text;

namespace Pmvni.Acompanhamento;

/// <summary>O papel de uma coluna da planilha, reconhecido pelo título do cabeçalho (sem acento, sem caixa). Coluna desconhecida: <see cref="Outra"/>
/// (aparece e é editada como texto, com o título da planilha).</summary>
public enum ChaveColuna { Outra, Numero, DataInicial, Recebido, Prioridade, Processo, Requerente, Local, Atualizacao, Status, Aprovacao, Profissional, DiasSemAtualizacao, Observacao }

/// <summary>Como a coluna é lida e editada: pela validação de dados (lista, data), pelo formato das células e pelas fórmulas.</summary>
public enum TipoColuna { Texto, Data, Numero, Lista, Calculada }

/// <summary>Uma cor da formatação condicional: fundo e texto em "#RRGGBB" (null = a regra não muda aquele lado).</summary>
public sealed record Cor(string? Fundo, string? Texto);

public enum TipoRegra { Igual, Contem, Entre, MaiorOuIgual, MenorOuIgual, Maior, Menor }

/// <summary>Uma regra de formatação condicional portada da planilha (igual a um texto, contém um texto, faixa numérica). <see cref="Prioridade"/>
/// menor vale primeiro, como no Excel.</summary>
public sealed record RegraCor(TipoRegra Tipo, string Valor, double A, double B, Cor Cor, int Prioridade)
{
    public bool Vale(string texto, double? numero) => Tipo switch
    {
        TipoRegra.Igual => string.Equals(Planilha.Normal(texto), Planilha.Normal(Valor), StringComparison.Ordinal),
        TipoRegra.Contem => Valor.Length > 0 && Planilha.Normal(texto).Contains(Planilha.Normal(Valor), StringComparison.Ordinal),
        TipoRegra.Entre => numero is { } n && n >= Math.Min(A, B) && n <= Math.Max(A, B),
        TipoRegra.MaiorOuIgual => numero >= A,
        TipoRegra.MenorOuIgual => numero <= A,
        TipoRegra.Maior => numero > A,
        TipoRegra.Menor => numero < A,
        _ => false,
    };
}

/// <summary>Uma coluna da planilha: posição (0 = A), letra, título do cabeçalho, papel, tipo, opções da lista e regras de cor.</summary>
public sealed class Coluna
{
    public required int Indice { get; init; }
    public string Letra => Planilha.Letra(Indice);
    public required string Titulo { get; init; }
    public required ChaveColuna Chave { get; init; }
    public required TipoColuna Tipo { get; init; }
    public IReadOnlyList<string> Opcoes { get; init; } = [];
    public IReadOnlyList<RegraCor> Regras { get; init; } = [];

    /// <summary>A cor da formatação condicional para o valor (a primeira regra que vale, pela prioridade); null = sem cor.</summary>
    public Cor? CorDe(string texto, double? numero) => Regras.FirstOrDefault(r => r.Vale(texto, numero))?.Cor;

    public override string ToString() => $"{Letra} {Titulo} ({Chave}, {Tipo})";
}

/// <summary>Uma célula: o texto como aparece, o número (datas são números seriais do Excel) e a fórmula, se houver.</summary>
public sealed record Celula(string Texto, double? Numero, string? Formula)
{
    public static readonly Celula Vazia = new("", null, null);
    public bool EhVazia => Texto.Length == 0 && Numero is null && Formula is null;
}

/// <summary>Uma linha de processo da planilha (<see cref="Linha"/> é o número da linha no Excel).</summary>
public sealed class LinhaProcesso
{
    public required int Linha { get; init; }
    public required IReadOnlyDictionary<int, Celula> Celulas { get; init; }
    internal PlanilhaLida? Dono { get; set; }

    public Celula Celula(int coluna) => Celulas.TryGetValue(coluna, out var c) ? c : global::Pmvni.Acompanhamento.Celula.Vazia;
    Coluna? ColunaDe(ChaveColuna k) => Dono?.Coluna(k);

    /// <summary>O texto da coluna (datas em dd/MM/yyyy; dias calculados pelo app).</summary>
    public string Texto(ChaveColuna k) => ColunaDe(k) is { } c ? Dono!.TextoDe(this, c) : "";
    public DateTime? Data(ChaveColuna k) => ColunaDe(k) is { } c ? Planilha.ComoData(Celula(c.Indice)) : null;

    public string Processo => Texto(ChaveColuna.Processo);
    public string Status => Texto(ChaveColuna.Status);
    public string Aprovacao => Texto(ChaveColuna.Aprovacao);
    public DateTime? Atualizacao => Data(ChaveColuna.Atualizacao);

    /// <summary>Os dias sem atualização: hoje − Atualização quando a planilha calcula (fórmula com HOJE), senão o número gravado.</summary>
    public int? Dias(DateTime hoje)
    {
        if (ColunaDe(ChaveColuna.DiasSemAtualizacao) is not { } c) return null;
        var cel = Celula(c.Indice);
        if (cel.Formula is { } f && f.Contains("TODAY()", StringComparison.OrdinalIgnoreCase))
            return Atualizacao is { } a ? (int)(hoje.Date - a.Date).TotalDays : null;
        return cel.Numero is { } n ? (int)n : null;
    }

    /// <summary>O número do processo sem espaços (a planilha tem "19490/ 2026" com espaço duro).</summary>
    public string ProcessoNormalizado => Planilha.ProcessoNormal(Processo);
}

/// <summary>A planilha lida: aba, títulos, colunas (com listas e cores) e linhas de processo.</summary>
public sealed class PlanilhaLida
{
    public required string Caminho { get; init; }
    public required string Aba { get; init; }
    /// <summary>Todas as abas da pasta de trabalho, na ordem do arquivo.</summary>
    public IReadOnlyList<string> Abas { get; init; } = [];
    /// <summary>A aba pedida não existe mais (renomeada ou apagada no Excel): esta é a primeira, só para mostrar.</summary>
    public bool AbaPedidaAusente { get; init; }
    public string Titulo { get; init; } = "";
    public string Subtitulo { get; init; } = "";
    public required IReadOnlyList<Coluna> Colunas { get; init; }
    public required IReadOnlyList<LinhaProcesso> Linhas { get; init; }
    public required int LinhaCabecalho { get; init; }
    public DateTime LidoEm { get; init; } = DateTime.Now;

    public Coluna? Coluna(ChaveColuna k) => k == ChaveColuna.Outra ? null : Colunas.FirstOrDefault(c => c.Chave == k);

    public string TextoDe(LinhaProcesso l, Coluna c)
    {
        var cel = l.Celula(c.Indice);
        if (c.Chave == ChaveColuna.DiasSemAtualizacao) return l.Dias(DateTime.Today) is { } d ? d.ToString(CultureInfo.InvariantCulture) : "";
        if (c.Tipo == TipoColuna.Data) return Planilha.ComoData(cel) is { } dt ? dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : cel.Texto;
        if (cel.Numero is { } n && cel.Texto.Length == 0) return n.ToString(CultureInfo.InvariantCulture);
        return cel.Texto;
    }

    /// <summary>As opções de Status (a lista da planilha; sem lista, os valores encontrados, na ordem em que aparecem).</summary>
    public IReadOnlyList<string> Opcoes(ChaveColuna k)
    {
        var c = Coluna(k);
        if (c is null) return [];
        if (c.Opcoes.Count > 0) return c.Opcoes;
        return Linhas.Select(l => l.Texto(k)).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Quantos processos em cada opção da coluna (todas as opções da lista aparecem, mesmo com zero).</summary>
    public IReadOnlyList<(string Valor, int Quantidade)> Contagem(ChaveColuna k)
    {
        var ops = Opcoes(k);
        if (ops.Count == 0) return [];
        var totais = Linhas.CountBy(l => Planilha.Normal(l.Texto(k)), StringComparer.Ordinal).ToDictionary();
        return ops.Select(o => (o, totais.GetValueOrDefault(Planilha.Normal(o)))).ToList();
    }
}

/// <summary>Utilidades comuns da planilha (nomes de coluna, normalização, datas).</summary>
public static class Planilha
{
    public static string Letra(int indice)
    {
        var s = "";
        for (var n = indice + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    public static int Indice(string letras)
    {
        var n = 0;
        foreach (var ch in letras.ToUpperInvariant()) n = n * 26 + (ch - 'A' + 1);
        return n - 1;
    }

    /// <summary>"B12" → (1, 12).</summary>
    public static (int Coluna, int Linha) Referencia(string r)
    {
        var inicio = r.StartsWith('$') ? 1 : 0;
        var i = inicio;
        while (i < r.Length && char.IsAsciiLetter(r[i])) i++;
        var linha = i < r.Length && r[i] == '$' ? i + 1 : i;
        return (Indice(r[inicio..i]), int.Parse(r.AsSpan(linha), CultureInfo.InvariantCulture));
    }

    /// <summary>Sem acento, minúsculas, espaços simples (inclusive o espaço duro).</summary>
    public static string Normal(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.Replace(' ', ' ').Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        var espaco = false;
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) { if (!espaco) sb.Append(' '); espaco = true; continue; }
            espaco = false;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().Trim();
    }

    public static string ProcessoNormal(string s) => new(s.Where(ch => !char.IsWhiteSpace(ch) && ch != ' ').ToArray());

    public static DateTime? ComoData(Celula c)
    {
        if (c.Numero is { } n && n > 1 && n < 2958465) return DateTime.FromOADate(n);
        if (c.Texto.Length > 0 && DateTime.TryParseExact(c.Texto.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }

    /// <summary>O papel da coluna pelo título do cabeçalho.</summary>
    public static ChaveColuna ChaveDe(string titulo)
    {
        var t = Normal(titulo);
        return t switch
        {
            "nº" or "n°" or "no" or "n" or "numero" or "n." or "nº." or "item" => ChaveColuna.Numero,
            "data inicial" or "data de entrada" or "entrada" or "data" => ChaveColuna.DataInicial,
            "recebido" or "recebimento" or "data de recebimento" => ChaveColuna.Recebido,
            "prioridade" => ChaveColuna.Prioridade,
            "processo" or "protocolo" or "n do processo" or "nº do processo" => ChaveColuna.Processo,
            "requerente" or "interessado" => ChaveColuna.Requerente,
            "local" or "endereco" or "localizacao" => ChaveColuna.Local,
            "atualizacao" or "ultima atualizacao" => ChaveColuna.Atualizacao,
            "status" or "situacao" => ChaveColuna.Status,
            "status da aprovacao" or "aprovacao" => ChaveColuna.Aprovacao,
            "profissional" or "autor" or "autor do projeto" or "responsavel tecnico" or "responsavel" => ChaveColuna.Profissional,
            "observacao" or "observacoes" or "obs" or "obs." => ChaveColuna.Observacao,
            _ when t.StartsWith("dias", StringComparison.Ordinal) => ChaveColuna.DiasSemAtualizacao,
            _ => ChaveColuna.Outra,
        };
    }
}
