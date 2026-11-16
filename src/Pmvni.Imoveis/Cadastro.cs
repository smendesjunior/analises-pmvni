using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pmvni.Imoveis;

/// <summary>Uma linha da tabela <c>fontes</c> da base cadastral (de onde e quando a fotografia foi importada).</summary>
public sealed record FonteCadastral(string Arquivo, string? ModificadoEm, string? ImportadoEmUtc, string? Tabela, string? Registros);

/// <summary>A base cadastral auxiliar (SQLite), aberta só para leitura e carregada na memória uma vez: os registros vigentes, as colunas
/// (os parâmetros da pesquisa) e as fontes. Nada é gravado no arquivo (sem índices, sem migração, sem diário).</summary>
public sealed class Cadastro
{
    public string Arquivo { get; }
    public string Origem { get; }
    public IReadOnlyList<RegistroCadastro> Registros { get; }
    public IReadOnlyList<CampoPesquisa> Campos { get; }
    public IReadOnlyList<FonteCadastral> Fontes { get; }
    readonly Dictionary<string, List<RegistroCadastro>> porInscricao;

    Cadastro(string arquivo, string origem, List<RegistroCadastro> registros, List<CampoPesquisa> campos, List<FonteCadastral> fontes)
    {
        Arquivo = arquivo;
        Origem = origem;
        Registros = registros;
        Campos = campos;
        Fontes = fontes;
        porInscricao = registros.Where(r => r.InscricaoMunicipal.Length > 0).GroupBy(r => r.InscricaoMunicipal, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    public IReadOnlyList<RegistroCadastro> PorInscricao(string inscricao) =>
        porInscricao.TryGetValue(inscricao, out var l) ? l : [];

    public RegistroCadastro? PorLinha(long linha) => Registros.FirstOrDefault(r => r.LinhaFonte == linha);

    /// <summary>Colunas que nunca viram parâmetro de pesquisa nem aparecem no relatório (documentos pessoais e chaves internas).</summary>
    public static readonly IReadOnlySet<string> Ocultas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "n_documento", "codigo_pessoa", "tipo_de_documento", "pessoas_encontradas", "linha_fonte", "registro_vigente", "codigo_tipo_cadastro" };

    /// <summary>A ordem dos parâmetros mais usados; os demais vêm depois, pelo rótulo.</summary>
    static readonly string[] Prioridade =
    [
        "inscricao_municipal", "inscricao_reduzida", "nome_do_contribuinte", "logradouro", "n_metrico", "bairro", "setor_contribuinte",
        "quadra_contribuinte", "lote_contribuinte", "unidade_contribuinte", "loteamento", "situacao", "ultilizacao_do_imovel", "ocupacao",
        "matricula", "cep", "complemento", "inscricao_municipal_anterior",
    ];

    /// <summary>Rótulos legíveis das colunas conhecidas (o dicionário da base dá os demais).</summary>
    static readonly Dictionary<string, string> Rotulos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["inscricao_municipal"] = "Inscrição municipal",
        ["inscricao_reduzida"] = "Inscrição reduzida",
        ["nome_do_contribuinte"] = "Contribuinte",
        ["logradouro"] = "Logradouro",
        ["tipo_do_logradouro"] = "Tipo do logradouro",
        ["n_metrico"] = "Número",
        ["bairro"] = "Bairro",
        ["setor_contribuinte"] = "Setor",
        ["quadra_contribuinte"] = "Quadra",
        ["lote_contribuinte"] = "Lote",
        ["unidade_contribuinte"] = "Unidade",
        ["loteamento"] = "Loteamento",
        ["situacao"] = "Situação cadastral",
        ["ultilizacao_do_imovel"] = "Utilização do imóvel",
        ["ocupacao"] = "Ocupação",
        ["matricula"] = "Matrícula",
        ["cep"] = "CEP",
        ["complemento"] = "Complemento",
        ["inscricao_municipal_anterior"] = "Inscrição anterior",
        ["area_unidade_m2"] = "Área da unidade (m²)",
        ["area_da_unidade"] = "Área da unidade",
        ["situacao_do_terreno"] = "Situação do terreno",
        ["estado_de_conservacao"] = "Estado de conservação",
        ["descricao_segmento"] = "Segmento",
        ["status_cruzamento_pessoas"] = "Vínculo com Pessoas",
        ["quantidade_correspondencias_pessoas"] = "Correspondências em Pessoas",
        ["naturezas_juridicas_encontradas"] = "Natureza jurídica",
        ["tipos_encontrados"] = "Tipo de pessoa",
        ["nomes_razoes_encontrados"] = "Nome/razão social em Pessoas",
        ["data_inicial"] = "Data inicial",
        ["data_vigencia_inicial"] = "Início da vigência",
        ["tipo_de_cadastro"] = "Tipo de cadastro",
        ["codigo_logradouro"] = "Código do logradouro",
        ["codigo_bairro"] = "Código do bairro",
        ["municipio"] = "Município",
        ["regime_tributario"] = "Regime tributário",
        ["simples_nacional"] = "Simples Nacional",
        ["vinculos"] = "Vínculos",
        ["complemento_auxiliar"] = "Complemento auxiliar",
        ["inscricao_imobiliaria"] = "Inscrição imobiliária",
        ["codigo_segmento"] = "Código do segmento",
    };

    public static string RotuloDe(string coluna, IReadOnlyDictionary<string, string>? dicionario = null)
    {
        if (Rotulos.TryGetValue(coluna, out var r)) return r;
        if (dicionario is not null && dicionario.TryGetValue(coluna, out var d) && !string.IsNullOrWhiteSpace(d))
        {
            var t = d.Trim();
            return t.Length > 1 ? char.ToUpper(t[0], CultureInfo.GetCultureInfo("pt-BR")) + t[1..].ToLower(CultureInfo.GetCultureInfo("pt-BR")) : t;
        }
        var s = coluna.Replace('_', ' ');
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }

    public static Cadastro Carregar(string arquivo, CancellationToken ct = default)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = arquivo, Mode = SqliteOpenMode.ReadOnly, Pooling = false, Cache = SqliteCacheMode.Private }.ToString();
        using var con = new SqliteConnection(cs);
        con.Open();
        var objetos = Lista(con, "SELECT name FROM sqlite_master WHERE type IN ('table','view')").ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A vista de cruzamento traz o vínculo com Pessoas (unívoco/ambíguo); só os registros vigentes (sem data final).
        string origem, sql;
        if (objetos.Contains("vw_cadastro_cruzamento")) { origem = "vw_cadastro_cruzamento"; sql = "SELECT * FROM vw_cadastro_cruzamento WHERE registro_vigente = 1"; }
        else if (objetos.Contains("vw_cadastro_vigente")) { origem = "vw_cadastro_vigente"; sql = "SELECT * FROM vw_cadastro_vigente"; }
        else if (objetos.Contains("cadastro_imobiliario")) { origem = "cadastro_imobiliario"; sql = "SELECT * FROM cadastro_imobiliario"; }
        else throw new InvalidDataException("Esta base não tem a tabela cadastro_imobiliario: não é a base cadastral auxiliar.");

        var dicionario = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (objetos.Contains("dicionario_campos"))
            using (var c = con.CreateCommand())
            {
                c.CommandText = "SELECT campo_sql, campo_original FROM dicionario_campos";
                using var r = c.ExecuteReader();
                while (r.Read()) if (!r.IsDBNull(0) && !r.IsDBNull(1)) dicionario[r.GetString(0)] = r.GetString(1);
            }

        var registros = new List<RegistroCadastro>();
        var preenchidos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] colunas;
        using (var c = con.CreateCommand())
        {
            c.CommandText = sql;
            using var r = c.ExecuteReader();
            colunas = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
            var iLinha = Array.FindIndex(colunas, n => n.Equals("linha_fonte", StringComparison.OrdinalIgnoreCase));
            long seq = 0;
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var d = new Dictionary<string, string?>(colunas.Length, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < colunas.Length; i++)
                {
                    string? v = r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(v)) v = null;
                    else preenchidos[colunas[i]] = preenchidos.GetValueOrDefault(colunas[i]) + 1;
                    d[colunas[i]] = v;
                }
                seq++;
                var linha = iLinha >= 0 && long.TryParse(d[colunas[iLinha]], NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : seq;
                registros.Add(new RegistroCadastro(linha, d));
            }
        }

        var campos = colunas.Where(c => !Ocultas.Contains(c) && preenchidos.GetValueOrDefault(c) > 0)
            .Select(c => new CampoPesquisa(c, RotuloDe(c, dicionario), origem))
            .OrderBy(c => Array.IndexOf(Prioridade, c.Id) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(c => c.Rotulo, StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), true))
            .ToList();

        var fontes = new List<FonteCadastral>();
        if (objetos.Contains("fontes"))
            using (var c = con.CreateCommand())
            {
                c.CommandText = "SELECT * FROM fontes";
                using var r = c.ExecuteReader();
                string? Col(SqliteDataReader rd, string n) { for (var i = 0; i < rd.FieldCount; i++) if (rd.GetName(i).Equals(n, StringComparison.OrdinalIgnoreCase)) return rd.IsDBNull(i) ? null : Convert.ToString(rd.GetValue(i), CultureInfo.InvariantCulture); return null; }
                while (r.Read())
                    fontes.Add(new FonteCadastral(Path.GetFileName(Col(r, "arquivo") ?? ""), Col(r, "modificado_em"), Col(r, "importado_em_utc"), Col(r, "tabela_destino"), Col(r, "quantidade_registros")));
            }
        return new Cadastro(arquivo, origem, registros, campos, fontes);
    }

    static IEnumerable<string> Lista(SqliteConnection con, string sql)
    {
        using var c = con.CreateCommand();
        c.CommandText = sql;
        using var r = c.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }
}
