using System.Collections.Frozen;
using Pmvni.Composicao.Web.Ponte;

namespace Pmvni.Composicao.Web.Legislacao;

/// <summary>Envelope e validação da ponte da legislação (espelho de web/paginador/src/legislacao/mensagens.ts): a mesma regra da
/// <see cref="Mensagens"/> da Etapa 1 (versão 1, tipo do sentido certo, <c>id</c> e <c>dados</c>), com os tipos do extrator e os do
/// leitor (Tarefa 18).</summary>
public static class MensagensLegislacao
{
    /// <summary>Host → web do extrator: <c>extrair {url}</c> e <c>cancelar {}</c>.</summary>
    public static readonly FrozenSet<string> TiposHostExtrator = new[] { "extrair", "cancelar" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Host → web do leitor (Tarefa 18): <c>abrir {url, pagina, termo}</c>, <c>fechar</c>, <c>irPagina {pagina}</c>,
    /// <c>irMarcador {caminho}</c>, <c>zoom {acao}</c>, <c>procurar {termo, passo}</c> e <c>obterSelecao</c>.</summary>
    public static readonly FrozenSet<string> TiposHostLeitor =
        new[] { "abrir", "fechar", "irPagina", "irMarcador", "zoom", "procurar", "obterSelecao" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Host → web: os do extrator e os do leitor.</summary>
    public static readonly FrozenSet<string> TiposHostParaWeb = TiposHostExtrator.Union(TiposHostLeitor).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Web → host do extrator: <c>pronto</c>; respostas de <c>extrair</c> (<c>extraido</c> ou <c>erro</c>); eventos <c>progresso</c>,
    /// <c>textoPagina</c> (id <c>evento-N</c>, com <c>extracao</c> = id do <c>extrair</c>) e <c>cspViolacao</c>.</summary>
    public static readonly FrozenSet<string> TiposWebExtrator =
        new[] { "pronto", "progresso", "textoPagina", "cspViolacao", "extraido", "erro" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Web → host do leitor: <c>pronto</c>; respostas <c>aberto</c>, <c>erro</c>, <c>fechado</c>, <c>paginaAtual</c>,
    /// <c>zoomAplicado</c>, <c>procurado</c>, <c>selecao</c>; eventos <c>paginaMudou</c>, <c>selecaoMudou</c>, <c>zoomMudou</c>,
    /// <c>atalho</c> e <c>cspViolacao</c>.</summary>
    public static readonly FrozenSet<string> TiposWebLeitor = new[]
    {
        "pronto", "paginaMudou", "selecaoMudou", "zoomMudou", "atalho", "cspViolacao",
        "aberto", "erro", "fechado", "paginaAtual", "zoomAplicado", "procurado", "selecao",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Web → host: os do extrator e os do leitor.</summary>
    public static readonly FrozenSet<string> TiposWebParaHost = TiposWebExtrator.Union(TiposWebLeitor).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Lê e valida uma mensagem web→host: versão diferente de 1, tipo desconhecido (ou do sentido oposto) ou campo
    /// faltando ⇒ false com o motivo.</summary>
    public static bool TentarLerDaWeb(string json, out MensagemWeb? m, out string? motivo) =>
        Mensagens.TentarLer(json, TiposWebParaHost, out m, out motivo);

    /// <summary>Serializa uma mensagem host→web; o tipo precisa ser um dos tipos do host.</summary>
    public static string SerializarParaWeb(string tipo, string id, object dados) =>
        Mensagens.Serializar(TiposHostParaWeb, tipo, id, dados);
}
