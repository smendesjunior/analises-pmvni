using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Eventos <c>--json</c> do Codex (um JSON por linha) → a linha que a tela mostra (desenho §12.4). Tolerante: o que não é conhecido não aparece;
/// linha que não é JSON aparece como veio.</summary>
public static class EventosCodex
{
    public const int LimiteLinha = 2000;

    /// <summary><c>null</c> = não mostrar.</summary>
    public static string? Linha(string jsonl)
    {
        if (string.IsNullOrWhiteSpace(jsonl)) return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonl); }
        catch (JsonException) { return Cortar(jsonl); }
        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return Cortar(jsonl);
            var tipo = Texto(raiz, "type");
            switch (tipo)
            {
                case "item.completed":
                    return raiz.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object ? Item(item) : null;
                case "error":
                    return Erro(Texto(raiz, "message"));
                case "turn.failed":
                    return raiz.TryGetProperty("error", out var erro) && erro.ValueKind == JsonValueKind.Object
                        ? Erro(Texto(erro, "message"))
                        : Erro(Texto(raiz, "message"));
                default:
                    return null;
            }
        }
    }

    static readonly System.Text.RegularExpressions.Regex LinhaDeEtapa =
        new(@"^(?<etapa>prepare|analyze|technical_audit|produce|audit|final_review):\s*(?<modelo>\S+)\s*/\s*(?<esforco>\S+)\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Uma linha da saída do <c>executar_analise_62.py</c> (Tarefa 12) → a linha da tela; null = não mostrar. A situação do contrato (o JSON da
    /// última linha) e qualquer JSON não aparecem: o editor os lê e mostra a situação. "analyze: gpt-6.2-sol / high" vira
    /// "Etapa Análise: gpt-6.2-sol, raciocínio high". A cota e a decisão não são mais adivinhadas pelo texto: vêm dos códigos 20 e 21 do controlador.</summary>
    /// <summary>A etapa que o controlador anunciou ("analyze: gpt-6.2-sol / high" → <c>analyze</c>); null nas outras linhas.</summary>
    public static string? EtapaDoControlador(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha)) return null;
        var m = LinhaDeEtapa.Match(linha.Trim());
        return m.Success ? m.Groups["etapa"].Value : null;
    }

    public static string? LinhaDoControlador(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha)) return null;
        var t = linha.Trim();
        if (t[0] is '{' or '[' or '}' or ']' || t.StartsWith('"')) return null;
        var m = LinhaDeEtapa.Match(t);
        if (m.Success)
            return Cortar("Etapa " + SituacaoAnalisePro.NomeEtapa(m.Groups["etapa"].Value) + ": " + m.Groups["modelo"].Value + ", raciocínio " + m.Groups["esforco"].Value);
        return Cortar(t);
    }

    static string? Item(JsonElement item)
    {
        switch (Texto(item, "type"))
        {
            case "agent_message":
                return Texto(item, "text") is { } texto ? Cortar("Mensagem: " + texto) : null;
            case "command_execution":
                return Texto(item, "command") is { } comando ? Cortar("Ação: " + DescreverComando(comando)) : null;
            case "file_change":
                return item.TryGetProperty("changes", out var mudancas) && mudancas.ValueKind == JsonValueKind.Array
                    ? (mudancas.GetArrayLength() == 1 ? "Ação: gravou 1 arquivo de trabalho" : $"Ação: gravou {mudancas.GetArrayLength()} arquivos de trabalho")
                    : null;
            case "mcp_tool_call":
            {
                var ferramenta = Texto(item, "tool");
                if (ferramenta is null) return null;
                return Cortar("Ação: consultou " + (ferramenta.Contains("credit", StringComparison.OrdinalIgnoreCase)
                    ? "os créditos disponíveis" : ferramenta.Replace('_', ' ')));
            }
            default:
                return null;
        }
    }

    static readonly (string Chave, string Descricao)[] Scripts =
    {
        ("kit_pmvni.py", "consultando o índice do kit PMVNI"),
        ("buscar_norma.py", "consultando a legislação"),
        ("mapa_localizacao", "gerando o mapa de localização"),
        ("inspecionar.py", "inspecionando as pranchas"),
        ("fontes_preparo", "organizando as fontes documentais"),
        ("prepare_visual", "preparando as evidências visuais"),
    };

    static readonly System.Text.RegularExpressions.Regex Arquivo =
        new(@"([\w\-.]+?\.(?:txt|json|md|pdf|html|csv|py))(?![\w])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static readonly System.Text.RegularExpressions.Regex Lei =
        new(@"--lei[\s=\\""']+([^\s\\""']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>O comando de terminal do Codex em linguagem de gente (2026-11-17, pedido do usuário): "consultando a legislação (LC 123)",
    /// "lendo o texto extraído das pranchas", "listando os arquivos do processo"... Sem caminhos nem sintaxe de PowerShell.</summary>
    public static string DescreverComando(string comando)
    {
        var c = comando.ToLowerInvariant();
        foreach (var (chave, descricao) in Scripts)
            if (c.Contains(chave))
                return chave == "buscar_norma.py" && Lei.Match(comando) is { Success: true } m ? $"{descricao} ({m.Groups[1].Value})" : descricao;
        if (c.Contains("get-content") || c.Contains(" read ") || c.Contains("cat "))
        {
            if (c.Contains(@"preparo\\texto") || c.Contains(@"preparo\texto") || c.Contains("preparo/texto")) return "lendo o texto extraído dos documentos";
            if (c.Contains("posicoes")) return "lendo as posições do texto nas pranchas";
            var nomes = Arquivo.Matches(comando).Select(x => x.Groups[1].Value.Trim()).Where(n => n.Length > 0).Distinct().Take(2).ToList();
            if (c.Contains("skill") || nomes.Any(n => n.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))) return "lendo as instruções da análise";
            return nomes.Count > 0 ? "lendo " + string.Join(" e ", nomes) : "lendo um arquivo";
        }
        if (c.Contains("rg --files") || c.Contains("get-childitem") || c.Contains(" ls ") || c.Contains(" dir "))
            return c.Contains("projeto") || c.Contains("documentos") ? "listando os arquivos do processo" : "listando arquivos";
        if (c.Contains("rg ") || c.Contains("select-string") || c.Contains("findstr")) return "pesquisando nos documentos";
        if (c.Contains("python") && Arquivo.Matches(comando).Select(x => x.Groups[1].Value).FirstOrDefault(n => n.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) is { } py)
            return "executando a etapa " + Path.GetFileNameWithoutExtension(py.Trim()).Replace('_', ' ');
        if (c.Contains("pdftotext") || c.Contains("pdfplumber") || c.Contains("fitz")) return "extraindo o texto dos PDFs";
        return "executando um passo de apoio";
    }

    static string? Erro(string? mensagem) => mensagem is null ? null : Cortar("Erro: " + mensagem);

    static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Uma linha só (quebras viram espaço) e no máximo 2.000 caracteres.</summary>
    static string Cortar(string s)
    {
        if (s.Contains('\n') || s.Contains('\r'))
            s = string.Join(' ', s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
        return s.Length > LimiteLinha ? s[..LimiteLinha] : s;
    }
}
