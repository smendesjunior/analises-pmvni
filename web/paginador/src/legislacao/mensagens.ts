// Envelope da ponte do extrator de texto dos PDFs anexados (espelho de src/Pmvni.Composicao.Web/Legislacao/MensagensLegislacao.cs):
// { v: 1, tipo, id, dados }. Mensagem com v diferente de 1, tipo desconhecido ou campo faltando e descartada nos dois lados.
//
// host -> web: extrair {url}, cancelar {}.
// web -> host: pronto {versao, pdfjs}; respostas de `extrair` (mesmo id): extraido {paginas} ou erro {codigo, mensagem} com
// codigo "senha" | "invalido" | "falhou"; eventos (id evento-N, todos com `extracao` = id do `extrair` a que pertencem):
// progresso {pagina, total}, textoPagina {pagina, texto}; e cspViolacao {diretiva, uri} (sem `extracao`).
//
// Leitor (Tarefa 18; pagina leitor.html, tipos proprios em TIPOS_HOST_LEITOR / TIPOS_WEB_LEITOR):
// host -> web: abrir {url, pagina, termo} -> aberto {paginas, marcadores, escala, procurado?} ou erro {codigo}; fechar {} -> fechado {};
// irPagina {pagina} e irMarcador {caminho} -> paginaAtual {atual, total}; zoom {acao} -> zoomAplicado {escala};
// procurar {termo, passo} -> procurado {total, atual, pagina}; obterSelecao {} -> selecao {texto, paginaInicial, paginaFinal}.
// web -> host (eventos, id evento-N): pronto, paginaMudou {atual, total}, selecaoMudou {temSelecao}, zoomMudou {escala},
// atalho {tecla} e cspViolacao {diretiva, uri}.

export const VERSAO = 1 as const;

// Figuras da leitura padronizada (correcoes 2026-11-15): host -> web figuras {url}; web -> host figurasPagina {pagina, figuras}
// (evento, com `extracao`) e a resposta figurasExtraidas {paginas} ou erro.
export const TIPOS_HOST = ['extrair', 'cancelar', 'figuras'] as const;
export const TIPOS_WEB = ['pronto', 'progresso', 'textoPagina', 'cspViolacao', 'extraido', 'erro', 'figurasPagina', 'figurasExtraidas'] as const;

export const TIPOS_HOST_LEITOR = ['abrir', 'fechar', 'irPagina', 'irMarcador', 'zoom', 'procurar', 'obterSelecao'] as const;
export const TIPOS_WEB_LEITOR = [
  'pronto', 'paginaMudou', 'selecaoMudou', 'zoomMudou', 'atalho', 'cspViolacao',
  'aberto', 'erro', 'fechado', 'paginaAtual', 'zoomAplicado', 'procurado', 'selecao',
] as const;

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
export type TipoHostLeitor = (typeof TIPOS_HOST_LEITOR)[number];
export type TipoWebLeitor = (typeof TIPOS_WEB_LEITOR)[number];
export type MensagemHostLeitor = Envelope<TipoHostLeitor>;
export type MensagemWebLeitor = Envelope<TipoWebLeitor>;

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
/** Leitor: valida uma mensagem recebida do host. */
export const lerMensagemHostLeitor = (bruto: unknown): Leitura<MensagemHostLeitor> => ler(bruto, TIPOS_HOST_LEITOR);
/** Leitor: valida uma mensagem que a web vai enviar ao host. */
export const lerMensagemWebLeitor = (bruto: unknown): Leitura<MensagemWebLeitor> => ler(bruto, TIPOS_WEB_LEITOR);
