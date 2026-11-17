namespace Pmvni.App;

/// <summary>Tipo de uma linha do andamento: ação do agente, mensagem dele (texto que ele escreveu), erro ou texto solto.</summary>
public enum TipoAtividade { Acao, Mensagem, Erro, Texto }

/// <summary>Uma atividade da tela de acompanhamento (redesenho 2026-11-17): título em linguagem natural, detalhe opcional, ícone (Segoe Fluent Icons)
/// e o horário em que chegou (null quando a linha já existia ao abrir a tela).</summary>
public sealed record Atividade(TipoAtividade Tipo, string Titulo, string? Detalhe, string Glifo, DateTime? Hora)
{
    public string HoraTexto => Hora is { } h ? h.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "";
}

/// <summary>Uma descoberta: o que o agente relatou. Preliminar até a auditoria cruzada terminar; depois, verificada.</summary>
public sealed record DescobertaAnalise(string Texto, bool Verificada);

/// <summary>Traduz as linhas técnicas do andamento ("Ação: lendo o texto extraído dos documentos") em atividades legíveis
/// ("Conteúdo dos documentos lido"). Lógica pura, sem XAML.</summary>
public static class Atividades
{
    public static Atividade DaLinha(string? linha, DateTime? hora = null)
    {
        var t = (linha ?? "").Trim();
        if (t.StartsWith("Mensagem: ", StringComparison.Ordinal))
            return new(TipoAtividade.Mensagem, "Observação da IA", Maiuscula(t[10..]), "", hora);
        if (t.StartsWith("Erro: ", StringComparison.Ordinal))
            return new(TipoAtividade.Erro, "Falha na execução", Maiuscula(t[6..]), "", hora);
        if (t.StartsWith("Ação: ", StringComparison.Ordinal))
        {
            var (titulo, detalhe, glifo) = DaAcao(t[6..]);
            return new(TipoAtividade.Acao, titulo, detalhe, glifo, hora);
        }
        return new(TipoAtividade.Texto, Maiuscula(t), null, "", hora);
    }

    /// <summary>Título natural, detalhe e ícone de uma ação (as descrições vêm de EventosCodex.DescreverComando).</summary>
    public static (string Titulo, string? Detalhe, string Glifo) DaAcao(string acao)
    {
        var a = acao.Trim();
        var m = a.ToLowerInvariant();
        if (m.Contains("instruções")) return ("Instruções de análise carregadas", "Diretrizes e critérios do processo aplicados.", "");
        if (m.StartsWith("listando", StringComparison.Ordinal)) return ("Documentos do processo identificados", m.Contains("processo") ? null : "Arquivos da pasta listados.", "");
        if (m.Contains("posições") || m.Contains("inspecionando")) return ("Conteúdo das pranchas examinado", null, "");
        if (m.Contains("texto extraído") || m.Contains("extraindo o texto")) return ("Conteúdo dos documentos lido", "Textos extraídos e analisados.", "");
        if (m.Contains("legislação")) return ("Legislação municipal consultada", Parenteses(a), "");
        if (m.Contains("mapa")) return ("Mapa de localização preparado", null, "");
        if (m.Contains("fontes")) return ("Fontes documentais organizadas", null, "");
        if (m.Contains("evidências")) return ("Evidências visuais preparadas", null, "");
        if (m.Contains("pesquisando")) return ("Documentos pesquisados", null, "");
        if (m.StartsWith("gravou", StringComparison.Ordinal))
        {
            var n = new string(a.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return n is "" or "1" ? ("Arquivo de trabalho salvo", null, "") : ($"{n} arquivos de trabalho salvos", null, "");
        }
        if (m.Contains("créditos")) return ("Créditos disponíveis consultados", null, "");
        if (m.StartsWith("consultou ", StringComparison.Ordinal)) return ("Ferramenta do kit consultada", Maiuscula(a[10..]), "");
        if (m.StartsWith("executando a etapa ", StringComparison.Ordinal)) return ("Etapa de apoio executada", Maiuscula(a[19..]), "");
        if (m.StartsWith("lendo ", StringComparison.Ordinal)) return ("Arquivo lido", a[6..], "");
        return (Maiuscula(a), null, "");
    }

    /// <summary>As últimas mensagens do agente (no máximo <paramref name="maximo"/>, a mais recente primeiro), verificadas quando a análise
    /// passou da auditoria cruzada.</summary>
    public static IReadOnlyList<DescobertaAnalise> Descobertas(IEnumerable<string> linhas, bool verificadas, int maximo = 4) =>
        linhas.Where(l => l.StartsWith("Mensagem: ", StringComparison.Ordinal))
              .Select(l => Maiuscula(l[10..].Trim()))
              .Where(l => l.Length > 0)
              .Reverse().Take(maximo)
              .Select(l => new DescobertaAnalise(l, verificadas))
              .ToList();

    static string? Parenteses(string s)
    {
        var i = s.IndexOf('(');
        var j = s.LastIndexOf(')');
        return i >= 0 && j > i ? s[(i + 1)..j] : null;
    }

    static string Maiuscula(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
