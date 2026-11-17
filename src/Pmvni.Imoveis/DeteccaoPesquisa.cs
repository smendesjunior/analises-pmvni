using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Imoveis;

/// <summary>Um campo a pesquisar com o valor já ajustado a ele (a inscrição sem pontos, o logradouro sem o tipo).</summary>
public sealed record AlvoPesquisa(string CampoId, string Valor);

/// <summary>O que a detecção automática concluiu: os campos alternativos (basta um corresponder), o número do imóvel quando veio junto do
/// logradouro ("Rua X, 120"; tem de ser igual), a frase para o usuário ("Buscando por Contribuinte") e, se nada for achado, a busca de
/// reserva (todos os campos de texto).</summary>
public sealed record Deteccao(IReadOnlyList<AlvoPesquisa> Alternativas, string Descricao, AlvoPesquisa? Numero = null, Deteccao? Reserva = null);

/// <summary>"Detectar automaticamente" (2026-11-17): a partir do que foi digitado, escolhe o(s) campo(s) da pesquisa. Sem caixa nem acento.
/// <list type="bullet">
/// <item>Só dígitos (com pontos, traços, barras ou espaços) que formam uma inscrição (14 dígitos, ou 13/15 corrigíveis; ou o padrão
/// "01.01.118.0077.001") → Inscrição municipal, sem os pontos.</item>
/// <item>Só dígitos, de 6 a 12 → começo da inscrição municipal ou inscrição reduzida. De 1 a 5 → inscrição reduzida ou número; com zero à
/// esquerda e a largura do campo (quadra 3, lote 4, unidade 3), também quadra, lote ou unidade.</item>
/// <item>Começa com Rua, R., Av., Avenida, Travessa, Rodovia, Estrada, ES-, BR-… → Logradouro (sem o tipo; o tipo fica em outra coluna),
/// e o número depois da vírgula, se houver.</item>
/// <item>Igual (ou o começo, com 4 letras ou mais) a um bairro ou loteamento da base → Bairro ou Loteamento.</item>
/// <item>O resto → Contribuinte; sem nenhum achado, todos os campos de texto.</item>
/// </list></summary>
public static class DeteccaoPesquisa
{
    /// <summary>O id da opção "Detectar automaticamente" na lista de parâmetros (não é coluna da base).</summary>
    public const string Automatico = "*automatico";
    public const string RotuloAutomatico = "Detectar automaticamente";

    static readonly CompareInfo Cmp = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
    const CompareOptions Sem = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    static readonly Regex SoDigitos = new(@"^[\d.\-/\s]+$", RegexOptions.CultureInvariant);
    static readonly Regex InscricaoPontuada = new(@"^\d{2}[.\-/\s]\d{2}[.\-/\s]\d{3}[.\-/\s]\d{3,5}[.\-/\s]\d{3}$", RegexOptions.CultureInvariant);
    static readonly Regex Rodovia = new(@"^\s*(BR|ES)\s*-?\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex TipoLogradouro = new(
        @"^\s*(rua|r\.|av\.?|avenida|travessa|tv\.?|trav\.?|rodovia|rod\.?|estrada|estr\.?|alameda|al\.|pra[cç]a|p[cç]a\.?|beco|ladeira|largo|servid[aã]o)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex NumeroNoFim = new(@"^(?<rua>.+?)\s*(,|\s+n[º°o.]*\s*|\s+-\s+)\s*(?<num>\d{1,6}[A-Za-z]?)\s*$", RegexOptions.CultureInvariant);

    /// <summary>Sem acento e em maiúsculas, espaços simples (para comparar nomes da base).</summary>
    public static string Chave(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        var espaco = false;
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c)) { espaco = sb.Length > 0; continue; }
            if (espaco) { sb.Append(' '); espaco = false; }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Detecta os campos para <paramref name="valor"/>. Só entram campos que existem em <paramref name="campos"/>.</summary>
    public static Deteccao Detectar(string valor, IReadOnlyList<CampoPesquisa> campos, IEnumerable<string> bairros, IEnumerable<string> loteamentos)
    {
        var v = valor.Trim();
        var existe = campos.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Rotulo(string id) => campos.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Rotulo ?? id;
        Deteccao Com(IEnumerable<AlvoPesquisa> alvos, AlvoPesquisa? numero = null, Deteccao? reserva = null)
        {
            var l = alvos.Where(a => existe.Contains(a.CampoId)).ToList();
            var desc = "Buscando por " + JuntarOu(l.Select(a => Rotulo(a.CampoId)).Distinct().ToList())
                + (numero is not null && existe.Contains(numero.CampoId) ? $", {Rotulo(numero.CampoId).ToLower(CultureInfo.GetCultureInfo("pt-BR"))} {numero.Valor}" : "");
            return new Deteccao(l, desc, numero is not null && existe.Contains(numero.CampoId) ? numero : null, reserva);
        }
        var todosTexto = campos.Where(c => c.Id != Automatico).Select(c => new AlvoPesquisa(c.Id, v)).ToList();
        var reservaTodos = new Deteccao(todosTexto, "Nada no campo detectado; buscando em todos os campos");

        // números (inscrições, códigos, número do imóvel)
        if (SoDigitos.IsMatch(v) && v.Any(char.IsAsciiDigit))
        {
            var d = new string(v.Where(char.IsAsciiDigit).ToArray());
            var pontuada = InscricaoPontuada.IsMatch(v);
            if ((d.Length is 13 or 14 or 15 || pontuada) && Inscricoes.Normalizar(d) is { } insc)
                return Com([new("inscricao_municipal", insc)], reserva: Com([new("inscricao_municipal", d), new("inscricao_municipal_anterior", insc)]));
            if (d.Length >= 6)
                return Com([new("inscricao_municipal", d), new("inscricao_reduzida", d), new("inscricao_municipal_anterior", d)]);
            var alvos = new List<AlvoPesquisa> { new("inscricao_reduzida", d), new("n_metrico", d) };
            if (d[0] == '0')
            {
                if (d.Length == 3) { alvos.Add(new("quadra_contribuinte", d)); alvos.Add(new("unidade_contribuinte", d)); }
                if (d.Length == 4) alvos.Add(new("lote_contribuinte", d));
                if (d.Length == 2) alvos.Add(new("setor_contribuinte", d));
            }
            return Com(alvos);
        }

        // logradouro (com o número depois da vírgula)
        if (Rodovia.IsMatch(v) || TipoLogradouro.IsMatch(v))
        {
            var rua = v;
            AlvoPesquisa? numero = null;
            // "Rua X, 120", "Rua X nº 120", "BR-262 - 15" (o número da rodovia, "BR 262", não é número do imóvel)
            if (NumeroNoFim.Match(v) is { Success: true } m)
            {
                rua = m.Groups["rua"].Value.Trim();
                numero = new("n_metrico", m.Groups["num"].Value);
            }
            var semTipo = Rodovia.IsMatch(rua) ? rua.Trim() : TipoLogradouro.Replace(rua, "").Trim();
            if (semTipo.Length == 0) semTipo = rua.Trim();
            var alvos = new List<AlvoPesquisa> { new("logradouro", semTipo) };
            return Com(alvos, numero, reserva: numero is null ? reservaTodos : Com(alvos, null, reservaTodos));
        }

        // bairro ou loteamento conhecido
        var k = Chave(v);
        bool Conhece(IEnumerable<string> nomes) => k.Length >= 3 && nomes.Any(n => Chave(n) is var c && (c == k || (k.Length >= 4 && c.StartsWith(k, StringComparison.Ordinal) && (c.Length == k.Length || c[k.Length] == ' '))));
        var porLugar = new List<AlvoPesquisa>();
        if (Conhece(bairros)) porLugar.Add(new("bairro", v));
        if (Conhece(loteamentos)) porLugar.Add(new("loteamento", v));
        if (porLugar.Count > 0 && porLugar.Any(a => existe.Contains(a.CampoId))) return Com(porLugar, reserva: Com([new("nome_do_contribuinte", v)], reserva: reservaTodos));

        return existe.Contains("nome_do_contribuinte") ? Com([new("nome_do_contribuinte", v)], reserva: reservaTodos) : reservaTodos;
    }

    static string JuntarOu(IReadOnlyList<string> l) => l.Count switch
    {
        0 => "nenhum campo",
        1 => l[0],
        _ => string.Join(", ", l.Take(l.Count - 1)) + " ou " + l[^1],
    };

    /// <summary>O registro atende à detecção com a condição? Condições positivas: basta um campo. Negativas (não contém, diferente): todos
    /// os campos preenchidos têm de atender (o valor não aparece em nenhum). O número, quando há, tem de ser igual.</summary>
    public static bool Atende(RegistroCadastro r, Deteccao d, Condicao condicao)
    {
        if (d.Numero is { } n && !Pesquisa.Corresponde(r.Campo(n.CampoId), Condicao.Igual, n.Valor)) return false;
        var negativa = condicao is Condicao.NaoContem or Condicao.Diferente;
        var algum = false;
        foreach (var a in d.Alternativas)
        {
            var conteudo = r.Campo(a.CampoId);
            if (negativa)
            {
                if (string.IsNullOrWhiteSpace(conteudo)) continue;
                if (!Pesquisa.Corresponde(conteudo, condicao, a.Valor)) return false;
                algum = true;
            }
            else if (Pesquisa.Corresponde(conteudo, condicao, a.Valor)) return true;
        }
        return negativa && algum;
    }
}
