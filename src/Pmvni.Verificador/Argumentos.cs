using System.Globalization;

namespace Pmvni.Verificador;

/// <summary>Linha de comando do Verificador. <see cref="Erro"/> preenchido = argumentos inválidos (o programa mostra
/// <see cref="Uso"/> e sai com 2). Comandos de documento: <c>&lt;comando&gt; &lt;dados.json&gt; &lt;saida&gt;</c>, com
/// <c>--instrucoes</c> e <c>--rascunho</c> (as mesmas opções do gerador do kit); <c>capturas</c> aceita também <c>--dpi N</c>; <c>html</c> aceita <c>--via-pacote</c>.
/// <c>despacho &lt;correcao|aprovacao&gt; &lt;dados.json&gt; &lt;saida.docx&gt; --data AAAA-MM-DD [--kit &lt;raiz&gt;] [--intervencao …]… [--area …]</c> (V6).</summary>
internal sealed record Argumentos(string Nome, string? Dados, string? Saida, bool Instrucoes, bool Rascunho, int Dpi, bool ViaPacote, string? Erro)
{
    /// <summary>despacho: <c>correcao</c> ou <c>aprovacao</c>.</summary>
    public string? TipoDespacho { get; init; }

    public DateOnly? Data { get; init; }

    public string? Kit { get; init; }

    /// <summary>despacho: <c>obra-nova</c>, <c>alteracao</c>, <c>ampliacao</c> (pode repetir).</summary>
    public IReadOnlyList<string> Intervencoes { get; init; } = Array.Empty<string>();

    public string? Area { get; init; }

    public static readonly IReadOnlyList<string> IntervencoesValidas = new[] { "obra-nova", "alteracao", "ampliacao" };

    public const int DpiPadrao = 96, DpiMinimo = 24, DpiMaximo = 1200;

    public const string Uso = """
        Uso: Pmvni.Verificador <comando> ...
          html     <dados.json> <saida.html>  [--instrucoes] [--rascunho] [--via-pacote]   HTML do relatório (como o gerador do kit);
                                              --via-pacote: importa, grava <saida>.pmvrel, relê e gera o HTML dos dados efetivos
          plano    <dados.json> <saida.json>  [--instrucoes] [--rascunho]   plano de quebras composto no host oculto
          pdf      <dados.json> <saida.pdf>   [--instrucoes] [--rascunho]   exporta o PDF (+ recibo); estouro: recusa, código 3
          texto    <dados.json> <saida.json>  [--instrucoes] [--rascunho]   texto de cada folha da tela (plano aplicado)
          capturas <dados.json> <pasta> [--dpi N] [--instrucoes] [--rascunho]   PNG de cada folha da tela (folha-001.png …)
          despacho <correcao|aprovacao> <dados.json> <saida.docx> --data AAAA-MM-DD [--kit <raiz>]
                   [--intervencao obra-nova|alteracao|ampliacao]... [--area 210,52]
                                              importa em memória, monta o despacho com todas as exigências, preenche o modelo do
                                              kit, valida e grava só o DOCX (DESPACHO_OK <sha256>); faltando: código 4; validação: 5
          autoteste-pdf <saida.pdf>
          autoteste-falhas
        """;

    static readonly HashSet<string> Documento = new(StringComparer.Ordinal) { "html", "plano", "pdf", "texto", "capturas" };

    public static Argumentos Ler(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Argumentos Falha(string nome, string msg) => new(nome, null, null, false, false, DpiPadrao, false, msg);
        if (args.Length == 0) return Falha("", "Nenhum comando.");
        var nome = args[0];
        if (nome == "autoteste-falhas")
            return args.Length == 1 ? new(nome, null, null, false, false, DpiPadrao, false, null) : Falha(nome, "autoteste-falhas não tem argumentos.");
        if (nome == "autoteste-pdf")
            return args.Length == 2 && !args[1].StartsWith("--", StringComparison.Ordinal)
                ? new(nome, null, args[1], false, false, DpiPadrao, false, null) : Falha(nome, "autoteste-pdf pede só <saida.pdf>.");
        if (nome == "despacho") return LerDespacho(args, Falha);
        if (!Documento.Contains(nome)) return Falha(nome, "Comando desconhecido: " + nome);

        var pos = new List<string>();
        bool instrucoes = false, rascunho = false, viaPacote = false;
        int? dpi = null;
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--instrucoes": instrucoes = true; break;
                case "--rascunho": rascunho = true; break;
                case "--via-pacote" when nome == "html": viaPacote = true; break;
                case "--dpi" when nome == "capturas":
                    if (i + 1 >= args.Length) return Falha(nome, "--dpi sem valor.");
                    if (!int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var d) || d < DpiMinimo || d > DpiMaximo)
                        return Falha(nome, $"--dpi deve ser um inteiro entre {DpiMinimo} e {DpiMaximo}.");
                    dpi = d;
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal)) return Falha(nome, "Opção desconhecida para " + nome + ": " + a);
                    pos.Add(a);
                    break;
            }
        }
        if (pos.Count != 2) return Falha(nome, nome + " pede <dados.json> e a saída.");
        return new(nome, pos[0], pos[1], instrucoes, rascunho, dpi ?? DpiPadrao, viaPacote, null);
    }

    static Argumentos LerDespacho(string[] args, Func<string, string, Argumentos> falha)
    {
        const string nome = "despacho";
        var pos = new List<string>();
        var intervencoes = new List<string>();
        DateOnly? data = null;
        string? kit = null, area = null;
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            string? Valor() => i + 1 < args.Length ? args[++i] : null;
            switch (a)
            {
                case "--data":
                    var d = Valor();
                    if (d is null || !DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lida))
                        return falha(nome, "--data pede uma data AAAA-MM-DD.");
                    data = lida;
                    break;
                case "--kit":
                    kit = Valor();
                    if (kit is null) return falha(nome, "--kit sem valor.");
                    break;
                case "--area":
                    area = Valor();
                    if (area is null) return falha(nome, "--area sem valor.");
                    break;
                case "--intervencao":
                    var v = Valor();
                    if (v is null || !IntervencoesValidas.Contains(v))
                        return falha(nome, "--intervencao pede " + string.Join(", ", IntervencoesValidas) + ".");
                    intervencoes.Add(v);
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal)) return falha(nome, "Opção desconhecida para despacho: " + a);
                    pos.Add(a);
                    break;
            }
        }
        if (pos.Count != 3) return falha(nome, "despacho pede <correcao|aprovacao> <dados.json> <saida.docx>.");
        if (pos[0] is not ("correcao" or "aprovacao"))
            return falha(nome, "Tipo de despacho desconhecido: " + pos[0] + " (use correcao ou aprovacao).");
        if (data is null) return falha(nome, "despacho pede --data AAAA-MM-DD.");
        return new Argumentos(nome, pos[1], pos[2], false, false, DpiPadrao, false, null)
        {
            TipoDespacho = pos[0], Data = data, Kit = kit, Intervencoes = intervencoes, Area = area,
        };
    }
}
