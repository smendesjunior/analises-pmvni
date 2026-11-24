using System.Security.Cryptography;
using System.Text.Json;

namespace Pmvni.Agentes;

internal static partial class RecuperacaoAnaliseLocal
{
    // A raiz registrada serve somente para interpretar nomes absolutos dentro dos JSONs.
    // Um checkpoint é consultado pelo manifesto atual e por hashes conhecidos, sem restaurar arquivos.
    internal sealed record Consulta(string RaizRegistrada, bool EmDisco, Func<string, JsonElement?> LerJson);

    internal static Consulta? Consultar(string origem, string registro, string? documentos = null)
    {
        if (!AreaAnaliseLocal.UsaAreaLocal(registro)) return null;
        var id = Path.GetFileName(registro);
        var local = AreaAnaliseLocal.LocalExistente(origem, registro, documentos);
        if (local is not null && GeracaoAtual(origem, local, id))
            return new(local, true, relativo =>
            {
                var caminho = Dentro(local, relativo);
                if (!File.Exists(caminho)) return null;
                using var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(f);
                return doc.RootElement.Clone();
            });

        var pasta = DiretorioCheckpoint(origem, id);
        var ponteiro = LerPonteiro(pasta) ?? throw new IOException("A análise está em outra estação e ainda não possui ponto de recuperação.");
        var manifesto = LerManifesto(origem, pasta, id, ponteiro.Atual);
        var indice = new Dictionary<string, Arquivo>(Nomes);
        foreach (var a in manifesto.Arquivos)
            if (!indice.TryAdd(a.Nome, a)) throw new InvalidDataException("Arquivo repetido no checkpoint.");
        JsonElement? Ler(string relativo)
        {
            if (!indice.TryGetValue(relativo, out var a)) return null;
            var dados = File.ReadAllBytes(Objeto(pasta, a.Sha256));
            if (dados.LongLength != a.Bytes || Convert.ToHexStringLower(SHA256.HashData(dados)) != a.Sha256)
                throw new InvalidDataException("Divergência no checkpoint ao consultar " + relativo);
            using var doc = JsonDocument.Parse(dados);
            return doc.RootElement.Clone();
        }
        var inventario = Ler(Path.Combine(".pmvni-ai", "editor", id, "INVENTARIO-ORIGINAIS.JSON"));
        using var dadosOrigem = JsonDocument.Parse(RegistroExecucao.LerTextoDaExecucao(registro));
        if (inventario is not { ValueKind: JsonValueKind.Object } i ||
            !Nomes.Equals(ProgressoLocal.Texto(i, "Execucao"), id) ||
            !Nomes.Equals(ProgressoLocal.Texto(i, "Identificador"), ProgressoLocal.Texto(dadosOrigem.RootElement, AreaAnaliseLocal.ChaveIdentificador)))
            throw new InvalidDataException("O inventário do checkpoint pertence a outro registro.");
        return new(manifesto.RaizLocal, false, Ler);
    }

    // Rebaseia somente o caminho relativo da peça. Nunca abre um caminho absoluto recebido do manifesto.
    internal static string? OriginalConsultado(string origem, string raizRegistrada, string declarado)
    {
        if (!Path.IsPathFullyQualified(declarado)) return null;
        var relativo = Path.GetRelativePath(raizRegistrada, declarado);
        if (relativo.Split(['/', '\\']).Any(p => p is "." or ".." || Nomes.Equals(p, ".pmvni-ai")) || Path.IsPathRooted(relativo))
            return null;
        return Dentro(origem, relativo);
    }
}
