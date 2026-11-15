using System.Collections.Frozen;
using Pmvni.Composicao.Web.Ponte;

namespace Pmvni.Composicao.Web.Legislacao;

/// <summary>Envelope e validação da ponte da legislação (espelho de web/paginador/src/legislacao/mensagens.ts): a mesma regra da
/// <see cref="Mensagens"/> da Etapa 1 (versão 1, tipo do sentido certo, <c>id</c> e <c>dados</c>), com os tipos do extrator.
/// A Tarefa 18 acrescenta os do leitor.</summary>
public static class MensagensLegislacao
{
    /// <summary>Host → web: <c>extrair {url}</c> e <c>cancelar {}</c>.</summary>
    public static readonly FrozenSet<string> TiposHostParaWeb = new[] { "extrair", "cancelar" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Web → host: <c>pronto</c>; respostas de <c>extrair</c> (<c>extraido</c> ou <c>erro</c>); eventos <c>progresso</c>,
    /// <c>textoPagina</c> (id <c>evento-N</c>, com <c>extracao</c> = id do <c>extrair</c>) e <c>cspViolacao</c>.</summary>
    public static readonly FrozenSet<string> TiposWebParaHost =
        new[] { "pronto", "progresso", "textoPagina", "cspViolacao", "extraido", "erro" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Lê e valida uma mensagem web→host: versão diferente de 1, tipo desconhecido (ou do sentido oposto) ou campo
    /// faltando ⇒ false com o motivo.</summary>
    public static bool TentarLerDaWeb(string json, out MensagemWeb? m, out string? motivo) =>
        Mensagens.TentarLer(json, TiposWebParaHost, out m, out motivo);

    /// <summary>Serializa uma mensagem host→web; o tipo precisa ser um dos tipos do host.</summary>
    public static string SerializarParaWeb(string tipo, string id, object dados) =>
        Mensagens.Serializar(TiposHostParaWeb, tipo, id, dados);
}
