// Envelope da ponte host <-> web (espelho de Ponte/Mensagens.cs): { v: 1, tipo, id, dados }.
// Mensagem com v diferente de 1, tipo desconhecido ou campo faltando e descartada (e registrada) nos dois lados.

export const VERSAO = 1 as const;

export const TIPOS_HOST = [
  'compor', 'aplicarPlano', 'textoPorFolha', 'rolarPara', 'definirZoom',
  // Tarefa 7 (Etapa 2): estado do editor, selecao, edicao no A4 e ancora de rolagem.
  'estadoEditor', 'selecionar', 'editarCampo', 'concluirEdicao', 'obterAncora',
  // Tarefa 11: pesquisa no relatorio (realce por CSS Custom Highlight API; responde `pesquisado`).
  'pesquisar',
] as const;
export const TIPOS_WEB = [
  'pronto', 'composto', 'planoAplicado', 'textoPorFolha', 'folhaVisivel', 'erro',
  // Tarefa 7: respostas (estadoAplicado, selecionado, edicaoIniciada, editar, semEdicao, ancora) e eventos de interacao
  // (selecionado, pedirEdicao, editar, edicaoCancelada, atalho, menuContexto, excluidoClicado; id evento-N).
  'estadoAplicado', 'selecionado', 'edicaoIniciada', 'editar', 'semEdicao', 'ancora',
  'pedirEdicao', 'edicaoCancelada', 'atalho', 'menuContexto', 'excluidoClicado',
  'pesquisado',
] as const;

export type TipoHost = (typeof TIPOS_HOST)[number];
export type TipoWeb = (typeof TIPOS_WEB)[number];

export interface Envelope<T extends string> {
  v: typeof VERSAO;
  tipo: T;
  id: string;
  dados: Record<string, unknown>;
}

export type MensagemHost = Envelope<TipoHost>;
export type MensagemWeb = Envelope<TipoWeb>;

export type Leitura<T> = { ok: true; msg: T } | { ok: false; motivo: string; id: string | null };

function ehObjeto(x: unknown): x is Record<string, unknown> {
  return typeof x === 'object' && x !== null && !Array.isArray(x);
}

function ler<T extends string>(bruto: unknown, tipos: readonly T[]): Leitura<Envelope<T>> {
  if (!ehObjeto(bruto)) return { ok: false, motivo: 'mensagem nao e um objeto', id: null };
  const id = typeof bruto.id === 'string' && bruto.id !== '' ? bruto.id : null;
  const falha = (motivo: string): Leitura<Envelope<T>> => ({ ok: false, motivo, id });
  if (!('v' in bruto)) return falha('campo "v" faltando');
  if (bruto.v !== VERSAO) return falha('versao nao suportada: ' + String(bruto.v));
  if (typeof bruto.tipo !== 'string') return falha('campo "tipo" faltando');
  if (!(tipos as readonly string[]).includes(bruto.tipo)) return falha('tipo desconhecido: ' + bruto.tipo);
  if (id === null) return falha('campo "id" faltando');
  if (!ehObjeto(bruto.dados)) return falha('campo "dados" faltando ou invalido');
  return { ok: true, msg: { v: VERSAO, tipo: bruto.tipo as T, id, dados: bruto.dados } };
}

/** Valida uma mensagem recebida do host. */
export const lerMensagemHost = (bruto: unknown): Leitura<MensagemHost> => ler(bruto, TIPOS_HOST);
/** Valida uma mensagem que a web vai enviar ao host (garantia do lado de ca). */
export const lerMensagemWeb = (bruto: unknown): Leitura<MensagemWeb> => ler(bruto, TIPOS_WEB);
