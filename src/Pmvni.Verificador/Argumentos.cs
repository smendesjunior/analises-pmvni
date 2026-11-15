using System.Globalization;

namespace Pmvni.Verificador;

/// <summary>Linha de comando do Verificador. <see cref="Erro"/> preenchido = argumentos inválidos (o programa mostra
/// <see cref="Uso"/> e sai com 2). Comandos de documento: <c>&lt;comando&gt; &lt;dados.json&gt; &lt;saida&gt;</c>, com
/// <c>--instrucoes</c> e <c>--rascunho</c> (as mesmas opções do gerador do kit); <c>capturas</c> aceita também <c>--dpi N</c>; <c>html</c> aceita <c>--via-pacote</c>.</summary>
internal sealed record Argumentos(string Nome, string? Dados, string? Saida, bool Instrucoes, bool Rascunho, int Dpi, bool ViaPacote, string? Erro)
{
    public const int DpiPadrao = 96, DpiMinimo = 24, DpiMaximo = 1200;

    public const string Uso = """
        Uso: Pmvni.Verificador <comando> ...
          html     <dados.json> <saida.html>  [--instrucoes] [--rascunho] [--via-pacote]   HTML do relatório (como o gerador do kit);
                                              --via-pacote: importa, grava <saida>.pmvrel, relê e gera o HTML dos dados efetivos
          plano    <dados.json> <saida.json>  [--instrucoes] [--rascunho]   plano de quebras composto no host oculto
          pdf      <dados.json> <saida.pdf>   [--instrucoes] [--rascunho]   exporta o PDF (+ recibo); estouro: recusa, código 3
          texto    <dados.json> <saida.json>  [--instrucoes] [--rascunho]   texto de cada folha da tela (plano aplicado)
          capturas <dados.json> <pasta> [--dpi N] [--instrucoes] [--rascunho]   PNG de cada folha da tela (folha-001.png …)
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
}
