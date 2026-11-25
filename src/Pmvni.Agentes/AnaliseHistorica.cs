using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

public sealed record AnaliseHistorica(Processo Processo, string Registro, Provedor Provedor, string Situacao,
    string? Pacote, string? Documento, string? Pdf, string? Sessao, string? Usuario, DateTime Alterada)
{
    public string Identificacao => $"{Processo.Protocolo ?? Processo.NomePasta} Â· {Provedores.Nome(Provedor)} Â· {Situacao.Replace('_', ' ')} Â· {Alterada.ToLocalTime():dd/MM/yyyy HH:mm}";

    public static IReadOnlyList<AnaliseHistorica> Listar(string raiz)
    {
        var itens = new List<AnaliseHistorica>();
        foreach (var processo in Processos.Listar(raiz, CancellationToken.None).Itens)
        {
            try
            {
                foreach (var registro in ControlesDoProcesso.Registros(processo.Pasta))
                    if (Ler(processo, registro) is { } item) itens.Add(item);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return itens.OrderByDescending(a => a.Alterada).ToList();
    }

    public static AnaliseHistorica? Ler(Processo processo, string registro)
    {
        if (RegistroExecucao.MotivoInvalido(processo.Pasta, registro) is not null) return null;
        try
        {
            var arquivo = new FileInfo(Path.Combine(registro, "execucao.json"));
            if (!arquivo.Exists || arquivo.LinkTarget is not null) return null;
            var j = JsonNode.Parse(RegistroExecucao.LerTextoDaExecucao(registro));
            var provedor = j?["provedor"]?.GetValue<string>() switch
            {
                "chatgpt" => Provedor.ChatGpt, "claude" => Provedor.Claude, "gemini" => Provedor.Gemini, _ => (Provedor?)null
            };
            if (provedor is null || j?["situacao"]?.GetValue<string>() is not { } situacao) return null;
            string? Arquivo(string campo)
            {
                var declarado = j[campo]?.GetValue<string>();
                return ResultadoAnalise.PdfDentro(processo.Pasta, declarado) ?? ResultadoAnalise.ProdutoArquivado(processo.Pasta, declarado);
            }
            var documento = Arquivo("document");
            if (documento is null && j["document"]?.GetValue<string>() is { Length: > 0 } declarado)
            {
                var produtos = Path.Combine(ControlesDoProcesso.Diretorio(processo.Pasta, "entregas"), Path.GetFileName(registro), "produtos");
                AreaAnaliseLocal.ConferirCaminho(produtos, local: true);
                documento = ResultadoAnalise.PdfDentro(produtos, declarado);
            }
            return new(processo, registro, provedor.Value, situacao, Arquivo("pmvrel"), documento, Arquivo("pdf"),
                j["sessao_codex"]?.GetValue<string>(), j["usuario"]?.GetValue<string>(), arquivo.LastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { return null; }
    }
}
