// Envelope da ponte do extrator de texto dos PDFs anexados (espelho de src/Pmvni.Composicao.Web/Legislacao/MensagensLegislacao.cs):
// { v: 1, tipo, id, dados }. Mensagem com v diferente de 1, tipo desconhecido ou campo faltando e descartada nos dois lados.
//
// host -> web: extrair {url}, cancelar {}.
// web -> host: pronto {versao, pdfjs}; respostas de `extrair` (mesmo id): extraido {paginas} ou erro {codigo, mensagem} com
// codigo "senha" | "invalido" | "falhou"; eventos (id evento-N, todos com `extracao` = id do `extrair` a que pertencem):
// progresso {pagina, total}, textoPagina {pagina, texto}; e cspViolacao {diretiva, uri} (sem `extracao`).

export const VERSAO = 1 as const;

export const TIPOS_HOST = ['extrair', 'cancelar'] as const;
export const TIPOS_WEB = ['pronto', 'progresso', 'textoPagina', 'cspViolacao', 'extraido', 'erro'] as const;

export type TipoHost = (typeof TIPOS_HOST)[number];
export type TipoWeb = (typeof TIPOS_WEB)[number];

export type CodigoErro = 'senha' | 'invalido' | 'falhou';

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
