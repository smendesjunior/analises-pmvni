using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

internal sealed record PerfilDoPlugin(string Modelo, string Esforco)
{
    public static PerfilDoPlugin Ler(JsonNode item) =>
        new(item["modelo"]?.GetValue<string>() ?? throw new InvalidDataException("Modelo ausente."),
            item["esforco"]?.GetValue<string>() ?? throw new InvalidDataException("Inteligência ausente."));

    public void Aplicar(JsonNode item)
    {
        item["modelo"] = Modelo;
        item["esforco"] = Esforco;
    }
}

internal static class PerfisDoPlugin
{
    const string Arquivo = "perfis-do-plugin.local.json";
    internal const string Auditoria = "auditoria";
    static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };

    internal static Dictionary<string, PerfilDoPlugin> Ler(string raiz)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, PerfilDoPlugin>>(File.ReadAllBytes(Path.Combine(raiz, Arquivo)))
                ?? throw new InvalidDataException("Escolhas de modelos inválidas.");
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    internal static void Escolher(string raiz, string etapa, PerfilDoPlugin perfil, PerfilDoPlugin padrao)
    {
        Directory.CreateDirectory(raiz);
        using var trava = new Mutex(false, @"Local\PMVNI.EscolhaDeModelos");
        try { if (!trava.WaitOne(TimeSpan.FromSeconds(2))) throw new IOException("Outra janela está salvando os modelos."); }
        catch (AbandonedMutexException) { }
        try
        {
            var escolhas = Ler(raiz);
            if (perfil == padrao) escolhas.Remove(etapa);
            else escolhas[etapa] = perfil;
            var arquivo = Path.Combine(raiz, Arquivo);
            var tmp = arquivo + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(escolhas, Opcoes));
                File.Move(tmp, arquivo, true);
            }
            finally { File.Delete(tmp); }
        }
        finally { trava.ReleaseMutex(); }
    }

    // O registrador de grupos e o gerador do RPMVNI leem as referências relativas à própria skill.
    // A cópia é publicada de uma vez e compartilhada apenas entre as tentativas da mesma execução.
    internal static string PrepararExecucao(string skill, string pasta, string raizDados, bool retomadaAntiga)
    {
        var destino = Path.Combine(pasta, "plugin-da-execucao");
        if (Directory.Exists(destino)) return destino;
        if (retomadaAntiga) return skill;
        Directory.CreateDirectory(pasta);
        var tmp = Directory.CreateDirectory(Path.Combine(pasta, ".plugin-" + Guid.NewGuid().ToString("N")));
        try
        {
            foreach (var arquivo in Directory.EnumerateFiles(skill, "*", SearchOption.AllDirectories))
            {
                var copia = Path.Combine(tmp.FullName, Path.GetRelativePath(skill, arquivo));
                Directory.CreateDirectory(Path.GetDirectoryName(copia)!);
                File.Copy(arquivo, copia);
            }
            var escolhas = Ler(raizDados);
            if (escolhas.Count > 0)
            {
                var refs = Path.Combine(tmp.FullName, "references");
                var plano = Path.Combine(refs, "grupos.json");
                var grupos = JsonNode.Parse(File.ReadAllBytes(plano))!.AsArray();
                foreach (var grupo in grupos.OfType<JsonObject>())
                    if (escolhas.TryGetValue(grupo["id"]!.ToString(), out var perfil)) perfil.Aplicar(grupo);
                File.WriteAllText(plano, grupos.ToJsonString(Opcoes));
                if (escolhas.TryGetValue(Auditoria, out var auditor))
                {
                    var caminho = Path.Combine(refs, "perfil-auditoria.json");
                    var registro = JsonNode.Parse(File.ReadAllBytes(caminho))!.AsObject();
                    auditor.Aplicar(registro);
                    File.WriteAllText(caminho, registro.ToJsonString(Opcoes));
                }
            }
            Directory.Move(tmp.FullName, destino);
            return destino;
        }
        finally { if (tmp.Exists && Directory.Exists(tmp.FullName)) Directory.Delete(tmp.FullName, true); }
    }
}

