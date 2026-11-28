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
    static string Arquivo(Provedor provedor) => provedor == Provedor.Claude ? "perfis-do-plugin-claude.local.json" : "perfis-do-plugin.local.json";
    internal const string Auditoria = "auditoria";
    static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };

    internal static Dictionary<string, PerfilDoPlugin> Ler(string raiz, Provedor provedor = Provedor.ChatGpt)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, PerfilDoPlugin>>(File.ReadAllBytes(Path.Combine(raiz, Arquivo(provedor))))
                ?? throw new InvalidDataException("Escolhas de modelos inválidas.");
        }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    internal static void Escolher(string raiz, string etapa, PerfilDoPlugin perfil, PerfilDoPlugin padrao, Provedor provedor = Provedor.ChatGpt)
    {
        Directory.CreateDirectory(raiz);
        using var trava = new Mutex(false, @"Local\PMVNI.EscolhaDeModelos");
        try { if (!trava.WaitOne(TimeSpan.FromSeconds(2))) throw new IOException("Outra janela está salvando os modelos."); }
        catch (AbandonedMutexException) { }
        try
        {
            var escolhas = Ler(raiz, provedor);
            if (perfil == padrao) escolhas.Remove(etapa);
            else escolhas[etapa] = perfil;
            var arquivo = Path.Combine(raiz, Arquivo(provedor));
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
    internal static string PrepararExecucao(string skill, string pasta, string raizDados, bool retomadaAntiga, Provedor provedor = Provedor.ChatGpt)
    {
        var destino = Path.Combine(pasta, "plugin-da-execucao");
        bool claude = provedor == Provedor.Claude;
        string SkillEm(string raiz) => claude ? Path.Combine(raiz, "skills", OrquestradorAnalise.NomeAnaliseClaude) : raiz;
        var origem = claude ? Directory.GetParent(skill)!.Parent!.FullName : skill;
        if (Directory.Exists(destino)) return SkillEm(destino);
        if (retomadaAntiga)
        {
            if (claude) throw new InvalidDataException("A cópia dos perfis Claude desta execução está ausente.");
            return skill;
        }
        Directory.CreateDirectory(pasta);
        var tmp = Directory.CreateDirectory(Path.Combine(pasta, ".plugin-" + Guid.NewGuid().ToString("N")));
        try
        {
            foreach (var arquivo in Directory.EnumerateFiles(origem, "*", SearchOption.AllDirectories))
            {
                var copia = Path.Combine(tmp.FullName, Path.GetRelativePath(origem, arquivo));
                Directory.CreateDirectory(Path.GetDirectoryName(copia)!);
                File.Copy(arquivo, copia);
            }
            var escolhas = Ler(raizDados, provedor);
            if (claude || escolhas.Count > 0)
            {
                var refs = Path.Combine(SkillEm(tmp.FullName), "references");
                var plano = Path.Combine(refs, "grupos.json");
                var grupos = JsonNode.Parse(File.ReadAllBytes(plano))!.AsArray();
                foreach (var grupo in grupos.OfType<JsonObject>())
                {
                    var id = grupo["id"]!.GetValue<int>();
                    if (escolhas.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var perfil)) perfil.Aplicar(grupo);
                    if (claude)
                    {
                        var nome = $"grupo-{id:00}";
                        ConfigurarAgenteClaude(tmp.FullName, "grupo", nome, PerfilDoPlugin.Ler(grupo));
                        grupo["subagente"] = $"analise-claude:{nome}";
                    }
                }
                File.WriteAllText(plano, grupos.ToJsonString(Opcoes));
                if (escolhas.TryGetValue(Auditoria, out var auditor))
                {
                    var caminho = Path.Combine(refs, "perfil-auditoria.json");
                    var registro = JsonNode.Parse(File.ReadAllBytes(caminho))!.AsObject();
                    auditor.Aplicar(registro);
                    File.WriteAllText(caminho, registro.ToJsonString(Opcoes));
                }
            }
            if (claude)
            {
                var arquivo = Path.Combine(SkillEm(tmp.FullName), "references", "perfil-auditoria.json");
                ConfigurarAgenteClaude(tmp.FullName, "auditor", "auditor", PerfilDoPlugin.Ler(JsonNode.Parse(File.ReadAllBytes(arquivo))!));
            }
            Directory.Move(tmp.FullName, destino);
            return SkillEm(destino);
        }
        finally { if (tmp.Exists && Directory.Exists(tmp.FullName)) Directory.Delete(tmp.FullName, true); }
    }

    static void ConfigurarAgenteClaude(string pacote, string baseAgente, string nome, PerfilDoPlugin perfil)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(perfil.Modelo, @"^claude-[a-z0-9][a-z0-9.-]*$")
            || perfil.Esforco is not ("low" or "medium" or "high" or "xhigh" or "max"))
            throw new InvalidDataException($"Perfil Claude inválido para {nome}.");
        var agentes = Path.Combine(pacote, "agents");
        var linhas = File.ReadAllLines(Path.Combine(agentes, baseAgente + ".md"));
        int fim = Array.IndexOf(linhas, "---", 1);
        if (linhas.FirstOrDefault() != "---" || fim < 1) throw new InvalidDataException("Cabeçalho do agente Claude inválido.");
        void Definir(string campo, string valor)
        {
            var i = Array.FindIndex(linhas, 1, fim - 1, l => l.StartsWith(campo + ":", StringComparison.Ordinal));
            if (i < 0) throw new InvalidDataException($"Campo {campo} ausente no agente {baseAgente}.");
            linhas[i] = campo + ": " + valor;
        }
        Definir("name", nome);
        Definir("model", perfil.Modelo);
        Definir("effort", perfil.Esforco);
        File.WriteAllLines(Path.Combine(agentes, nome + ".md"), linhas);
    }
}
