using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Pmvni.Agentes;

internal sealed class PerfilAoVivoCodex
{
    readonly Dictionary<string, (long Tamanho, bool Ativa)> registros = new();
    string? pastaAnterior;

    public PerfilRodadaChatGpt? Ler(string pasta, string? casa)
    {
        if (pastaAnterior != pasta) { registros.Clear(); pastaAnterior = pasta; }
        try
        {
            casa = string.IsNullOrWhiteSpace(casa) ? Environment.GetEnvironmentVariable("CODEX_HOME") : casa;
            casa ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            var banco = Directory.EnumerateFiles(casa, "state_*.sqlite").MaxBy(File.GetLastWriteTimeUtc);
            if (banco is null) return null;
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = banco,
                Mode = SqliteOpenMode.ReadOnly, DefaultTimeout = 1 }.ToString());
            db.Open();
            using var consulta = db.CreateCommand();
            consulta.CommandText = """
                WITH locais AS (SELECT * FROM threads WHERE cwd COLLATE NOCASE IN ($pasta, $longa) AND archived = 0),
                pai AS (SELECT id FROM locais WHERE source = 'exec' ORDER BY created_at_ms DESC LIMIT 1)
                SELECT rollout_path, model, reasoning_effort FROM locais
                WHERE model IS NOT NULL AND (id = (SELECT id FROM pai) OR
                    CASE WHEN json_valid(source) THEN json_extract(source,'$.subagent.thread_spawn.parent_thread_id') END = (SELECT id FROM pai))
                ORDER BY (source = 'exec'), created_at_ms DESC
                """;
            consulta.Parameters.AddWithValue("$pasta", pasta);
            consulta.Parameters.AddWithValue("$longa", @"\\?\" + pasta);
            using var linhas = consulta.ExecuteReader();
            while (linhas.Read())
                if (!linhas.IsDBNull(0) && EmAtividade(linhas.GetString(0)))
                    return new(0, linhas.GetString(1), linhas.IsDBNull(2) ? "" : linhas.GetString(2));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException or JsonException) { }
        return null;
    }

    bool EmAtividade(string caminho)
    {
        if (!File.Exists(caminho)) return false;
        using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var anterior = registros.GetValueOrDefault(caminho);
        var tamanho = arquivo.Length;
        if (anterior.Tamanho == tamanho) return anterior.Ativa;
        if (anterior.Tamanho > tamanho) anterior = default;
        // Só consumir linhas completas, inclusive quando o Codex ainda está escrevendo a última.
        arquivo.Position = tamanho - 1;
        if (arquivo.ReadByte() != '\n') return anterior.Ativa;
        arquivo.Position = anterior.Tamanho;
        using var leitor = new StreamReader(arquivo);
        var ativa = anterior.Ativa;
        while (leitor.ReadLine() is { } linha)
        {
            if (!linha.Contains("\"task_started\"") && !linha.Contains("\"task_complete\"") && !linha.Contains("\"turn_aborted\"")) continue;
            using var json = JsonDocument.Parse(linha);
            var raiz = json.RootElement;
            if (raiz.GetProperty("type").GetString() != "event_msg" || !raiz.GetProperty("payload").TryGetProperty("type", out var tipo)) continue;
            if (tipo.GetString() == "task_started") ativa = true;
            if (tipo.GetString() is "task_complete" or "turn_aborted") ativa = false;
        }
        registros[caminho] = (tamanho, ativa);
        return ativa;
    }
}
