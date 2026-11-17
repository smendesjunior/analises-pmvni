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
    /// <summary>Nenhum lote com a inscrição; o lote da inscrição anterior do mesmo registro (renumeração). Não é conclusivo.</summary>
    InscricaoAnterior,
}

/// <summary>O tom do selo de vínculo na tabela de resultados.</summary>
public enum TomVinculo { Sucesso, Atencao, Erro, Neutro }

/// <summary>O selo de vínculo de uma linha de resultado: texto, tom e explicação (dica). <see cref="Sucessora"/> é a inscrição que
/// substituiu uma inscrição baixada, quando o cadastro a indica.</summary>
public sealed record SeloVinculo(string Texto, TomVinculo Tom, string Dica, string? Sucessora = null);

/// <summary>Um parâmetro da pesquisa: a coluna real da base e o rótulo legível.</summary>
public sealed record CampoPesquisa(string Id, string Rotulo, string Fonte);

public sealed record Consulta(string CampoId, Condicao Condicao, string Valor, int Pagina = 1, int Tamanho = 50);

public sealed record PaginaResultado(IReadOnlyList<RegistroCadastro> Itens, int Total, int Pagina, int Tamanho)
{
    public int Paginas => Total == 0 ? 0 : (Total + Tamanho - 1) / Tamanho;
    /// <summary>Com "Detectar automaticamente": a frase do que foi buscado ("Buscando por Contribuinte"); senão, null.</summary>
    public string? Deteccao { get; init; }
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
        VinculoMapa.InscricaoAnterior => "Lote da inscrição anterior",
        _ => "Sem lote no mapa",
    };

    public static bool Baixada(RegistroCadastro r) => r.Situacao.Trim().StartsWith("baixad", StringComparison.OrdinalIgnoreCase);

    /// <summary>O selo da tabela (2026-11-17). Inscrição baixada (cancelada, unificada ou desmembrada) sem lote não é erro: o desenho só tem
    /// os lotes atuais. Ela leva um selo neutro, a explicação e, se o cadastro indicar, a inscrição que a substituiu.</summary>
    public static SeloVinculo Selo(RegistroCadastro r, VinculoMapa v, string? sucessora = null)
    {
        if (v == VinculoMapa.SemLote && Baixada(r))
            return new("Inscrição baixada", TomVinculo.Neutro,
                "Inscrição baixada no cadastro (cancelada, unificada ou desmembrada): não tem lote no desenho atual da base."
                + (sucessora is null ? " O cadastro não indica a inscrição que a substituiu." : $" Foi substituída pela inscrição {sucessora}."), sucessora);
        return v switch
        {
            VinculoMapa.Confirmado => new(Rotulo(v), TomVinculo.Sucesso, "Um único lote no desenho com esta inscrição."),
            VinculoMapa.MesmoTerreno => new(Rotulo(v), TomVinculo.Atencao, "Nenhum lote com esta inscrição; o desenho tem um lote do mesmo terreno (outra unidade, como num condomínio)."),
            VinculoMapa.Ambiguo => new(Rotulo(v), TomVinculo.Atencao, "Mais de um polígono candidato: a ligação com o desenho não é segura."),
            VinculoMapa.InscricaoAnterior => new(Rotulo(v), TomVinculo.Atencao, "Nenhum lote com esta inscrição; o desenho ainda usa a inscrição anterior deste imóvel. Confira."),
            _ => new(Rotulo(v), TomVinculo.Erro, "Nenhum lote desenhado na base com esta inscrição, nem com a do mesmo terreno."),
        };
    }
}
