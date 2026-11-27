namespace Pmvni.Agentes;

/// <summary>A rota das sessões técnicas do analise, resolvida antes da partida: <c>openai</c> ou <c>codex-lb</c> (<see cref="Rota"/>), de onde veio
/// (<see cref="Origem"/>, para a tela e o registro) e, se o provedor não serve, o motivo exato (<see cref="Recusa"/>; nesse caso nada é iniciado).</summary>
public sealed record RotaResolvida(string? Rota, string Origem, string? Recusa);

/// <summary>Erros 6 e 7 do diagnóstico do processo 19486: o editor resolve a rota do Codex uma vez, antes de iniciar qualquer processo, e entrega a mesma rota
/// ao controlador por uma variável (<see cref="Variavel"/>) e pelo <c>rota_codex_tecnica</c> do <c>execucao.json</c> (que o kit já lê). "Usar a configuração
/// do Codex neste PC" (<c>herdar</c>) lê o <c>model_provider</c> do <c>config.toml</c> (<c>CODEX_HOME</c>, ou <c>%USERPROFILE%\.codex</c>); sem a chave, vale
/// <c>openai</c>, como no kit. Provedor fora de <c>openai</c>/<c>codex-lb</c> é recusado aqui, nunca depois de a análise começar.</summary>
public static class RotaCodex
{
    /// <summary>A variável com que o editor declara ao <c>executar_analise_62.py</c> a rota das sessões técnicas: <c>openai</c> ou <c>codex-lb</c>.</summary>
    public const string Variavel = "PMVNI_ANALISE_ROTA_CODEX";

    internal static string[] ArgumentosConta(string rota) =>
        ["-c", $"model_provider=\"{rota}\"", "-c", "forced_login_method=\"chatgpt\""];

    static readonly string[] Aceitas = ["openai", "codex-lb"];

    public static RotaResolvida Resolver(ModoAcesso modo, string? escolhida, string? codexHome)
    {
        // Chave de API: sempre a OpenAI direta (a chave vai por CODEX_API_KEY); a rota da conta não se aplica.
        if (modo == ModoAcesso.Chave) return new RotaResolvida("openai", "chave de API da OpenAI", null);
        if (escolhida is "openai" or "codex-lb") return new RotaResolvida(escolhida, "Ajustes › Inteligência artificial", null);

        var config = codexHome is null ? null : Path.Combine(codexHome, "config.toml");
        var origem = "configuração do Codex neste PC (" + (config ?? "config.toml") + ")";
        string? provedor;
        try { provedor = config is not null && File.Exists(config) ? ProvedorDoToml(File.ReadAllLines(config)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new RotaResolvida(null, origem, $"O config.toml do Codex não pôde ser lido ({e.Message.TrimEnd('.')}). "
                + "Escolha a rota em Ajustes › Inteligência artificial. Nada foi iniciado.");
        }
        provedor ??= "openai";
        if (Aceitas.Contains(provedor, StringComparer.Ordinal)) return new RotaResolvida(provedor, origem, null);
        return new RotaResolvida(null, origem,
            $"A configuração do Codex neste PC usa o provedor \"{provedor}\" (model_provider em {config}), que a análise PMVNI não aceita: só a OpenAI direta "
            + "ou o Codex-LB. Escolha \"Conectar diretamente à OpenAI\" ou o Codex-LB em Ajustes › Inteligência artificial. Nada foi iniciado.");
    }

    public static string TextoDaConexao(RotaResolvida resolvida, ModoAcesso modo) => resolvida.Rota switch
    {
        "codex-lb" => "Este computador usa o Codex-LB.",
        "openai" when modo == ModoAcesso.Chave => "Este computador usa a API da OpenAI diretamente.",
        "openai" => "Este computador usa o Codex diretamente com a conta da OpenAI.",
        _ => "Confira a configuração de conexão do Codex neste computador."
    };

    /// <summary>"OpenAI direto" ou "Codex-LB".</summary>
    public static string Nome(string rota) => rota == "codex-lb" ? "Codex-LB" : "OpenAI direto";

    /// <summary>A linha que a tela mostra na partida: "Rota do Codex: Codex-LB (configuração do Codex neste PC (…config.toml))".</summary>
    public static string Descrever(RotaResolvida r) => "Rota do Codex: " + Nome(r.Rota ?? "openai") + " (" + r.Origem + ")";

    /// <summary>O <c>model_provider</c> do topo do arquivo (antes da primeira tabela <c>[...]</c>); aspas simples ou duplas, comentário no fim da linha.</summary>
    internal static string? ProvedorDoToml(IEnumerable<string> linhas)
    {
        foreach (var bruta in linhas)
        {
            var linha = bruta.Trim();
            if (linha.Length == 0 || linha[0] == '#') continue;
            if (linha[0] == '[') return null;
            var igual = linha.IndexOf('=');
            if (igual <= 0 || linha[..igual].Trim() != "model_provider") continue;
            var valor = linha[(igual + 1)..].Trim();
            if (valor.Length >= 2 && (valor[0] == '"' || valor[0] == '\''))
            {
                var fim = valor.IndexOf(valor[0], 1);
                return fim > 1 ? valor[1..fim].Trim() : null;
            }
            var comentario = valor.IndexOf('#');
            return (comentario >= 0 ? valor[..comentario] : valor).Trim() is { Length: > 0 } v ? v : null;
        }
        return null;
    }
}
