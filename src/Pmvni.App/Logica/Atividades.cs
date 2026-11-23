using System.Globalization;

namespace Pmvni.App;

/// <summary>Tipo de uma linha do andamento: ação do agente, mensagem dele (texto que ele escreveu), erro ou texto solto.</summary>
public enum TipoAtividade { Acao, Mensagem, Erro, Texto }

/// <summary>Uma atividade da tela de acompanhamento (redesenho 2026-11-17): título em linguagem natural, detalhe opcional, ícone (Segoe Fluent Icons)
/// e o horário em que chegou (null quando a linha já existia ao abrir a tela).</summary>
public sealed record Atividade(TipoAtividade Tipo, string Titulo, string? Detalhe, string Glifo, DateTime? Hora)
{
    public string HoraTexto => Hora is { } h ? h.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "";
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
        var fimData = t.IndexOf(']');
        if (t.StartsWith('[') && fimData > 1 && DateTimeOffset.TryParseExact(t[1..fimData],
            ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var instante))
        {
            hora = instante.LocalDateTime;
            t = t[(fimData + 1)..].TrimStart();
        }

        if (t.StartsWith("etapa ", StringComparison.OrdinalIgnoreCase))
            return DaEtapa(t[6..], hora, pausada: false);
        if (t.StartsWith("pausa na etapa ", StringComparison.OrdinalIgnoreCase))
            return DaEtapa(t[15..], hora, pausada: true);
        if (t.StartsWith("fim: ", StringComparison.OrdinalIgnoreCase))
        {
            var titulo = t[5..].Trim().ToUpperInvariant() switch
            {
                "CONCLUIDO" => "Análise concluída",
                "FALHOU" => "Falha na análise",
                "OBJECOES" => "A revisão identificou pendências",
                "PAUSADO_COTA" => "Análise pausada até a renovação da cota",
                "INTERROMPIDA" => "Análise interrompida",
                "NAO_PARTIU" => "A análise não iniciou",
                "CANCELADO" => "Análise cancelada",
                _ => "Execução encerrada",
            };
            var erro = titulo is "Falha na análise" or "A análise não iniciou";
            return new(erro ? TipoAtividade.Erro : TipoAtividade.Texto, titulo, null, erro ? Glifos.Erro : Glifos.Atividade, hora);
        }

        if (t.StartsWith("Mensagem: ", StringComparison.Ordinal))
            return new(TipoAtividade.Mensagem, "Observação da IA", Maiuscula(t[10..]), Glifos.Comentario, hora);
        if (t.StartsWith("Erro: ", StringComparison.Ordinal))
            return new(TipoAtividade.Erro, "Falha na execução", Maiuscula(t[6..]), Glifos.Erro, hora);
        if (t.StartsWith("Ação: ", StringComparison.Ordinal))
        {
            var (titulo, detalhe, glifo) = DaAcao(t[6..]);
            return new(TipoAtividade.Acao, titulo, detalhe, glifo, hora);
        }
        return new(TipoAtividade.Texto, Maiuscula(t), null, Glifos.Comentario, hora);
    }

    static Atividade DaEtapa(string registro, DateTime? hora, bool pausada)
    {
        var partes = registro.Trim().Split([' ', ':'], 2, StringSplitOptions.TrimEntries);
        var (titulo, detalhe) = partes[0].ToLowerInvariant() switch
        {
            "prepare" => ("Preparação dos documentos", "Organização dos arquivos que serão usados na análise."),
            "analyze" => ("Análise do projeto", "Exame dos documentos e das pranchas do processo."),
            "technical_audit" => ("Conferência técnica", "Revisão dos achados e das evidências que os sustentam."),
            "produce" => ("Elaboração do relatório", "Organização dos resultados no relatório técnico."),
            "audit" => ("Conferência do relatório", "Comparação do relatório com os resultados da análise."),
            "final_review" => ("Revisão final", "Última conferência do relatório e das pendências."),
            _ => ("Etapa da análise", "Atualização do andamento do processo."),
        };
        var estado = partes.Length == 2 ? partes[1] : "";
        var falha = estado.StartsWith("falhou", StringComparison.OrdinalIgnoreCase) || estado == "FAILED";
        detalhe = pausada ? "A execução foi pausada nesta etapa." : estado switch
        {
            "EXECUTED" => "Etapa concluída.",
            "OBJECTIONS" => "A revisão identificou pontos que precisam de correção.",
            "CANCELLED" => "Etapa cancelada.",
            _ when falha => estado.Contains("nova tentativa", StringComparison.OrdinalIgnoreCase)
                ? "A etapa falhou e será tentada novamente." : "A etapa falhou.",
            _ => detalhe,
        };
        return new(falha ? TipoAtividade.Erro : TipoAtividade.Acao, titulo, detalhe, falha ? Glifos.Erro : Glifos.Conferir, hora);
    }

    /// <summary>Título natural, detalhe e ícone de uma ação (as descrições vêm de EventosCodex.DescreverComando).</summary>
    public static (string Titulo, string? Detalhe, string Glifo) DaAcao(string acao)
    {
        var a = acao.Trim();
        var m = a.ToLowerInvariant();
        if (m.Contains("instruções")) return ("Instruções de análise carregadas", "Diretrizes e critérios do processo aplicados.", Glifos.Relatorio);
        if (m.StartsWith("listando", StringComparison.Ordinal)) return ("Documentos do processo identificados", m.Contains("processo") ? null : "Arquivos da pasta listados.", Glifos.Pasta);
        if (m.Contains("posições") || m.Contains("inspecionando")) return ("Conteúdo das pranchas examinado", null, Glifos.Inspecionar);
        if (m.Contains("texto extraído") || m.Contains("extraindo o texto")) return ("Conteúdo dos documentos lido", "Textos extraídos e analisados.", Glifos.Relatorio);
        if (m.Contains("legislação")) return ("Legislação municipal consultada", Parenteses(a), Glifos.Livro);
        if (m.Contains("mapa")) return ("Mapa de localização preparado", null, Glifos.Mapa);
        if (m.Contains("fontes")) return ("Fontes documentais organizadas", null, Glifos.Explorador);
        if (m.Contains("evidências")) return ("Evidências visuais preparadas", null, Glifos.Camera);
        if (m.Contains("pesquisando")) return ("Documentos pesquisados", null, Glifos.Pesquisar);
        if (m.StartsWith("gravou", StringComparison.Ordinal))
        {
            var n = new string(a.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return n is "" or "1" ? ("Arquivo de trabalho salvo", null, Glifos.Salvar) : ($"{n} arquivos de trabalho salvos", null, Glifos.Salvar);
        }
        if (m.Contains("créditos")) return ("Créditos disponíveis consultados", null, Glifos.Creditos);
        if (m.StartsWith("consultou ", StringComparison.Ordinal)) return ("Ferramenta do kit consultada", Maiuscula(a[10..]), Glifos.Ferramenta);
        if (m.StartsWith("executando a etapa ", StringComparison.Ordinal)) return ("Etapa de apoio executada", Maiuscula(a[19..]), Glifos.Executar);
        if (m.StartsWith("lendo ", StringComparison.Ordinal)) return ("Arquivo lido", a[6..], Glifos.Relatorio);
        return (Maiuscula(a), null, Glifos.Executar);
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
