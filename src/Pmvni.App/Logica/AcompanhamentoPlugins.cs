using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Acompanhamento das versões dos plugins do kit em Ajustes › Inteligência artificial (correções 2026-11-15). A cada vez que a página é vista,
/// a versão instalada é comparada com a última vista (<see cref="Ajustes.PluginsVistos"/>): se mudou, o cartão mostra "Atualizado desde a última vez"
/// até o editor fechar, e a nova passa a ser a vista. Sem tipos do WinUI (testado em Pmvni.Revisao.Testes).</summary>
public sealed class AcompanhamentoPlugins
{
    readonly Dictionary<string, string> atualizadosNestaSessao = new(StringComparer.Ordinal);

    /// <summary>A chave em <c>ajustes.json</c>: <c>chatgpt.analise</c>, <c>claude.analise</c>, <c>claude.analise-pro</c>.</summary>
    public static string Chave(Provedor p, string plugin) => Provedores.Sufixo(p) + "." + plugin;

    /// <summary>Registra a versão vista agora. true = <paramref name="vistos"/> mudou (é preciso salvar os ajustes). Plugin ausente não apaga a última vista:
    /// se voltar com outra versão, avisa.</summary>
    public bool Registrar(IDictionary<string, string> vistos, string chave, string? atual)
    {
        if (atual is null) return false;
        vistos.TryGetValue(chave, out var ultima);
        if (PluginsInstalados.VersaoAnteriorSeMudou(ultima, atual) is { } anterior) atualizadosNestaSessao[chave] = anterior;
        if (ultima == atual) return false;
        vistos[chave] = atual;
        return true;
    }

    /// <summary>A versão de antes quando o plugin foi atualizado desde a última vez que a página foi vista (nesta sessão do editor); senão null.</summary>
    public string? Anterior(string chave) => atualizadosNestaSessao.TryGetValue(chave, out var a) ? a : null;
}
