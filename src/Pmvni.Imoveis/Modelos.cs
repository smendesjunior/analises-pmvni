namespace Pmvni.Imoveis;

/// <summary>As seis condições da pesquisa (nem mais, nem menos: contrato do desenho aprovado).</summary>
public enum Condicao { Contem, NaoContem, Igual, Diferente, ComecaCom, TerminaCom }

/// <summary>Como o registro cadastral se liga ao lote desenhado na base (Lotes.shp, campo InscImob).</summary>
public enum VinculoMapa
{
    /// <summary>Exatamente um lote com a mesma inscrição municipal.</summary>
    Confirmado,
    /// <summary>Nenhum lote com a inscrição; um único lote com o mesmo terreno (13 primeiros dígitos, unidade diferente).</summary>
    MesmoTerreno,
    /// <summary>Mais de um polígono candidato: a ligação não é segura.</summary>
    Ambiguo,
    /// <summary>Nenhum lote desenhado para a inscrição.</summary>
    SemLote,
}

/// <summary>Um parâmetro da pesquisa: a coluna real da base e o rótulo legível.</summary>
public sealed record CampoPesquisa(string Id, string Rotulo, string Fonte);

public sealed record Consulta(string CampoId, Condicao Condicao, string Valor, int Pagina = 1, int Tamanho = 50);

public sealed record PaginaResultado(IReadOnlyList<RegistroCadastro> Itens, int Total, int Pagina, int Tamanho)
{
    public int Paginas => Total == 0 ? 0 : (Total + Tamanho - 1) / Tamanho;
}

/// <summary>Uma linha do cadastro imobiliário vigente (todas as colunas, como texto; null = vazio na origem).</summary>
public sealed record RegistroCadastro(long LinhaFonte, IReadOnlyDictionary<string, string?> Campos)
{
    public string? Campo(string nome) => Campos.TryGetValue(nome, out var v) ? v : null;
    public string InscricaoMunicipal => Campo("inscricao_municipal") ?? "";
    public string Contribuinte => Campo("nome_do_contribuinte") ?? "";
    public string Bairro => Campo("bairro") ?? "";
    public string Situacao => Campo("situacao") ?? "";

    /// <summary>"Rua X, 120 - complemento" (só o que existe).</summary>
    public string Endereco
    {
        get
        {
            var rua = string.Join(" ", new[] { Campo("tipo_do_logradouro"), Campo("logradouro") }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            var numero = Campo("n_metrico");
            var e = string.IsNullOrWhiteSpace(numero) ? rua : $"{rua}, {numero}";
            var comp = Campo("complemento");
            return string.IsNullOrWhiteSpace(comp) ? e : $"{e} - {comp}";
        }
    }
}

public static class TextosConsulta
{
    public static IReadOnlyList<Condicao> Condicoes { get; } = Enum.GetValues<Condicao>();

    public static string Rotulo(Condicao c) => c switch
    {
        Condicao.Contem => "Contém",
        Condicao.NaoContem => "Não contém",
        Condicao.Igual => "Igual",
        Condicao.Diferente => "Diferente",
        Condicao.ComecaCom => "Começa com",
        Condicao.TerminaCom => "Termina com",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    public static string Rotulo(VinculoMapa v) => v switch
    {
        VinculoMapa.Confirmado => "Lote identificado",
        VinculoMapa.MesmoTerreno => "Lote do mesmo terreno",
        VinculoMapa.Ambiguo => "Vínculo ambíguo",
        _ => "Sem lote no mapa",
    };
}
