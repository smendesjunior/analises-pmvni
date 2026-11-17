using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Eventos <c>--json</c> do Codex viram linhas legíveis (desenho §12.4); o que não é conhecido não aparece.</summary>
public class EventosCodexTestes
{
    static string Item(string json) => "{\"type\":\"item.completed\",\"item\":" + json + "}";

    public static IEnumerable<object?[]> Eventos()
    {
        yield return new object?[] { "{\"type\":\"thread.started\",\"thread_id\":\"abc\"}", null };
        yield return new object?[] { "{\"type\":\"turn.started\"}", null };
        yield return new object?[] { "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1}}", null };
        yield return new object?[] { Item("{\"id\":\"i1\",\"type\":\"agent_message\",\"text\":\"Lendo o processo\"}"), "Mensagem: Lendo o processo" };
        yield return new object?[] { Item("{\"type\":\"command_execution\",\"command\":\"python conferir.py\",\"status\":\"completed\"}"), "Ação: executando a etapa conferir" };
        yield return new object?[] { Item("{\"type\":\"file_change\",\"changes\":[{\"path\":\"a\",\"kind\":\"add\"},{\"path\":\"b\",\"kind\":\"update\"}]}"), "Ação: gravou 2 arquivos de trabalho" };
        yield return new object?[] { Item("{\"type\":\"file_change\",\"changes\":[]}"), "Ação: gravou 0 arquivos de trabalho" };
        yield return new object?[] { Item("{\"type\":\"mcp_tool_call\",\"server\":\"kit\",\"tool\":\"buscar\"}"), "Ação: consultou buscar" };
        yield return new object?[] { Item("{\"type\":\"reasoning\",\"text\":\"pensando\"}"), null };
        yield return new object?[] { "{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\",\"command\":\"x\"}}", null };
        yield return new object?[] { "{\"type\":\"error\",\"message\":\"sem rede\"}", "Erro: sem rede" };
        yield return new object?[] { "{\"type\":\"turn.failed\",\"error\":{\"message\":\"cota esgotada\"}}", "Erro: cota esgotada" };
        yield return new object?[] { "texto solto", "texto solto" };
        yield return new object?[] { "{ json quebrado", "{ json quebrado" };
        yield return new object?[] { "42", "42" };                     // JSON que não é objeto: aparece como veio
        yield return new object?[] { "", null };
        yield return new object?[] { "   ", null };
        yield return new object?[] { "{\"sem_tipo\":true}", null };    // objeto desconhecido
        yield return new object?[] { Item("{\"type\":\"agent_message\"}"), null };    // sem texto: tolerante, nada a mostrar
        yield return new object?[] { Item("{\"type\":\"mcp_tool_call\",\"tool\":\"so_ferramenta\"}"), "Ação: consultou so ferramenta" };
    }

    [Theory]
    [MemberData(nameof(Eventos))]
    public void eventos_do_codex_viram_linhas(string jsonl, string? esperado) => Assert.Equal(esperado, EventosCodex.Linha(jsonl));

    [Fact]
    public void linha_de_5000_caracteres_vira_2000()
    {
        var solta = EventosCodex.Linha(new string('x', 5000));
        Assert.Equal(2000, solta!.Length);
        var mensagem = EventosCodex.Linha(Item("{\"type\":\"agent_message\",\"text\":\"" + new string('y', 5000) + "\"}"));
        Assert.Equal(2000, mensagem!.Length);
        Assert.StartsWith("Mensagem: yyy", mensagem);
    }

    [Fact]
    public void texto_com_quebra_de_linha_fica_numa_linha_so()
    {
        var m = EventosCodex.Linha(Item("{\"type\":\"agent_message\",\"text\":\"a\\nb\\r\\nc\"}"));
        Assert.Equal("Mensagem: a b c", m);
    }
}
