using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>O registro de uma execução (desenho §12.10): <c>&lt;processo&gt;\.pmvni-ai\editor\AAAA-MM-DD_HHMMSS-&lt;chatgpt|claude&gt;[_2…]</c>, criado no clique e nunca
/// reutilizado. Nada aqui entra nas pastas de controle do kit (<c>.pmvni-ai\analise</c>, <c>analise-pro</c>): elas são do agente. A chave de API nunca é gravada.</summary>
public sealed class RegistroExecucao : IDisposable
{
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly object trava = new();
    readonly JsonObject execucao = new();
    readonly Dictionary<string, List<string>> pendentes = new();
    readonly Dictionary<string, long> usados = new();      // bytes já no arquivo (e na fila) de cada saída
    readonly HashSet<string> truncados = new();
    long bytesPendentes;
    bool descartado;

    /// <summary>Tamanho máximo de cada arquivo de saída (<c>saida.jsonl</c>, <c>erros.txt</c>, <c>situacoes.jsonl</c>): 50 MB. Passou disso, grava uma linha
    /// "saída truncada…" e descarta o resto (a leitura do agente continua, para ele nunca ficar bloqueado).</summary>
    public long LimiteArquivoBytes { get; set; } = 50L * 1024 * 1024;

    const long DescarregarAcima = 4L * 1024 * 1024;

    RegistroExecucao(string pasta) => Pasta = pasta;

    public string Pasta { get; }

    /// <summary>Cria a pasta (e as de cima). Se o nome já existe (mesmo segundo), usa <c>_2</c>, <c>_3</c>…</summary>
    public static RegistroExecucao Criar(string pastaProcesso, Provedor p, DateTime agoraLocal)
    {
        var baseNome = agoraLocal.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + "-" + Provedores.Sufixo(p);
        var editor = Path.Combine(pastaProcesso, ".pmvni-ai", "editor");
        Directory.CreateDirectory(editor);
        for (var k = 1; ; k++)
        {
            var pasta = Path.Combine(editor, k == 1 ? baseNome : baseNome + "_" + k.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(pasta) || File.Exists(pasta)) continue;
            Directory.CreateDirectory(pasta);
            return new RegistroExecucao(pasta);
        }
    }

    static string Caminho(string pasta, string nome) => Path.Combine(pasta, nome);

    /// <summary><c>pedido.md</c>: o pedido exato, UTF-8 sem BOM.</summary>
    public void GravarPedido(string texto) => File.WriteAllText(Caminho(Pasta, "pedido.md"), texto, Utf8);

    /// <summary><c>anexos.json</c>: por anexo, o nome de origem, o destino relativo, o sha256, o tamanho e <c>COPIADO</c> ou <c>JA_ARQUIVADO</c>.</summary>
    public void GravarAnexos(IReadOnlyList<ArquivoArquivado> anexos)
    {
        var lista = new JsonArray();
        foreach (var a in anexos)
            lista.Add(new JsonObject
            {
                ["origem"] = Path.GetFileName(a.Origem),
                ["destino"] = a.Relativo,
                ["sha256"] = a.Sha256,
                ["tamanho"] = a.Tamanho,
                ["situacao"] = a.JaEstava ? "JA_ARQUIVADO" : "COPIADO",
            });
        File.WriteAllText(Caminho(Pasta, "anexos.json"), lista.ToJsonString(Opcoes), Utf8);
    }

    /// <summary>Muda o <c>execucao.json</c> e o grava de forma atômica (arquivo novo ao lado, depois troca): quem lê nunca vê JSON pela metade.</summary>
    public void Atualizar(Action<JsonObject> mudar)
    {
        lock (trava)
        {
            mudar(execucao);
            var destino = Caminho(Pasta, "execucao.json");
            var tmp = destino + ".tmp";
            File.WriteAllText(tmp, execucao.ToJsonString(Opcoes), Utf8);
            // Quem está lendo o execucao.json naquele instante (a tela, o OneDrive, um antivírus) pode segurar o destino por milissegundos.
            for (var tentativa = 1; ; tentativa++)
            {
                try { File.Move(tmp, destino, overwrite: true); return; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (tentativa >= 200) { try { File.Delete(tmp); } catch (IOException) { } throw; }
                    Thread.Sleep(5);
                }
            }
        }
    }

    public void AcrescentarSaida(string linha) => Acrescentar("saida.jsonl", linha);

    public void AcrescentarErro(string linha) => Acrescentar("erros.txt", linha);

    public void AcrescentarSituacao(string json) => Acrescentar("situacoes.jsonl", json);

    void Acrescentar(string arquivo, string linha)
    {
        lock (trava)
        {
            if (descartado || truncados.Contains(arquivo)) return;
            if (!pendentes.TryGetValue(arquivo, out var lista)) pendentes[arquivo] = lista = new List<string>();
            if (!usados.TryGetValue(arquivo, out var jaUsados))
            {
                try { jaUsados = File.Exists(Caminho(Pasta, arquivo)) ? new FileInfo(Caminho(Pasta, arquivo)).Length : 0; }
                catch (IOException) { jaUsados = 0; }
            }
            var bytes = Utf8.GetByteCount(linha) + 1;
            if (jaUsados + bytes > LimiteArquivoBytes)
            {
                var marca = "saída truncada: limite de " + (LimiteArquivoBytes >= 1024 * 1024 ? LimiteArquivoBytes / (1024 * 1024) + " MB" : LimiteArquivoBytes / 1024 + " KB")
                            + " do arquivo atingido";
                lista.Add(marca);
                truncados.Add(arquivo);
                usados[arquivo] = jaUsados;
                bytesPendentes += Utf8.GetByteCount(marca) + 1;
                return;
            }
            usados[arquivo] = jaUsados + bytes;
            lista.Add(linha);
            bytesPendentes += bytes;
            if (bytesPendentes > DescarregarAcima) DescarregarSemTrava();      // linhas grandes não se acumulam na memória
        }
    }

    /// <summary>Grava no disco o que foi acrescentado (abre, acrescenta e fecha: quem acompanha o registro ao vivo nunca encontra o arquivo preso).</summary>
    public void Descarregar()
    {
        lock (trava) DescarregarSemTrava();
    }

    void DescarregarSemTrava()
    {
        foreach (var (arquivo, linhas) in pendentes)
        {
            if (linhas.Count == 0) continue;
            using var fs = new FileStream(Caminho(Pasta, arquivo), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var w = new StreamWriter(fs, Utf8, 64 * 1024) { NewLine = "\n" };
            foreach (var l in linhas) w.WriteLine(l);
            linhas.Clear();
        }
        bytesPendentes = 0;
    }

    /// <summary>Grava o que ficou pendente e não aceita mais nada.</summary>
    public void Dispose()
    {
        lock (trava)
        {
            if (descartado) return;
            try { DescarregarSemTrava(); }
            finally { descartado = true; }
        }
    }
}
