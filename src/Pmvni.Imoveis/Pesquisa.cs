using System.Globalization;

namespace Pmvni.Imoveis;

/// <summary>A pesquisa em memória: caixa e acentos ignorados (pt-BR), o valor digitado é literal (%, _, aspas e barras não são curingas),
/// a inscrição é texto (zeros à esquerda contam) e um campo vazio na origem não satisfaz nenhuma condição, nem as negativas.</summary>
public static class Pesquisa
{
    static readonly CompareInfo Comparador = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
    const CompareOptions Opcoes = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    public const int MaximoValor = 512;

    public static bool Corresponde(string? conteudo, Condicao condicao, string valor)
    {
        if (!Enum.IsDefined(condicao)) throw new ArgumentOutOfRangeException(nameof(condicao));
        if (string.IsNullOrWhiteSpace(valor)) throw new ArgumentException("Digite um valor para pesquisar.", nameof(valor));
        if (string.IsNullOrWhiteSpace(conteudo)) return false; // ausência não é "diferente"
        var c = conteudo.Trim();
        var v = valor.Trim();
        return condicao switch
        {
            Condicao.Contem => Comparador.IndexOf(c, v, Opcoes) >= 0,
            Condicao.NaoContem => Comparador.IndexOf(c, v, Opcoes) < 0,
            Condicao.Igual => Comparador.Compare(c, v, Opcoes) == 0,
            Condicao.Diferente => Comparador.Compare(c, v, Opcoes) != 0,
            Condicao.ComecaCom => Comparador.IsPrefix(c, v, Opcoes),
            Condicao.TerminaCom => Comparador.IsSuffix(c, v, Opcoes),
            _ => throw new ArgumentOutOfRangeException(nameof(condicao)),
        };
    }

    /// <summary>Filtra, ordena (inscrição municipal, depois linha da fonte) e pagina.</summary>
    public static PaginaResultado Aplicar(IEnumerable<RegistroCadastro> registros, IReadOnlyList<CampoPesquisa> campos, Consulta c, CancellationToken ct = default)
    {
        var automatico = c.CampoId == DeteccaoPesquisa.Automatico;
        if (!automatico && !campos.Any(x => x.Id == c.CampoId)) throw new ArgumentException("Este parâmetro não existe na base carregada.");
        if (c.Pagina < 1 || c.Tamanho is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(c));
        if (string.IsNullOrWhiteSpace(c.Valor) || c.Valor.Length > MaximoValor) throw new ArgumentException($"Digite de 1 a {MaximoValor} caracteres.");
        var lista = registros as IReadOnlyList<RegistroCadastro> ?? registros.ToList();
        List<RegistroCadastro> achados;
        string? descricao = null;
        if (automatico)
        {
            // "Detectar automaticamente": os campos saem do valor digitado; sem achado, a busca de reserva (e a dela).
            var bairros = lista.Select(r => r.Bairro).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var loteamentos = lista.Select(r => r.Campo("loteamento") ?? "").Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Deteccao? d = DeteccaoPesquisa.Detectar(c.Valor, campos, bairros, loteamentos);
            achados = [];
            while (d is not null)
            {
                descricao = d.Descricao;
                foreach (var r in lista)
                {
                    ct.ThrowIfCancellationRequested();
                    if (DeteccaoPesquisa.Atende(r, d, c.Condicao)) achados.Add(r);
                }
                if (achados.Count > 0) break;
                d = d.Reserva;
            }
        }
        else
        {
            achados = [];
            foreach (var r in lista)
            {
                ct.ThrowIfCancellationRequested();
                if (Corresponde(r.Campo(c.CampoId), c.Condicao, c.Valor)) achados.Add(r);
            }
        }
        var ordenados = achados.OrderBy(x => x.InscricaoMunicipal, StringComparer.Ordinal).ThenBy(x => x.LinhaFonte).ToList();
        return new(ordenados.Skip((c.Pagina - 1) * c.Tamanho).Take(c.Tamanho).ToList(), ordenados.Count, c.Pagina, c.Tamanho) { Deteccao = descricao };
    }
}
