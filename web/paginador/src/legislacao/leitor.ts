// Leitor dos PDFs anexados (desenho §11.4): roda no WebView2 do modulo Legislacao, na pagina https://leg.pmvni.invalid/leitor.html.
// pdf.min.mjs (globalThis.pdfjsLib) e pdf_viewer.mjs (globalThis.pdfjsViewer) entram antes, como modulos, nessa ordem; este arquivo
// nao empacota o PDF.js. Paginas em rolagem continua com camada de texto selecionavel, escala, marcadores e pesquisa pelo
// PDFFindController. O JavaScript dos PDFs nunca roda (sem PDFScriptingManager, sem sandbox) e os links externos ficam
// desligados (externalLinkEnabled = false). Toda conversa com o host e pelo envelope { v, tipo, id, dados }, validado aqui e la.
import type * as Visor from 'pdfjs-dist/web/pdf_viewer.mjs';
import { lerMensagemHostLeitor, lerMensagemWebLeitor, VERSAO, type CodigoErro, type MensagemHostLeitor, type TipoWebLeitor } from './mensagens';
import { configurarPdfJs, opcoesDocumento, type BibliotecaPdfJs } from './opcoes-pdfjs';

type BibliotecaVisor = typeof Visor;
type Documento = Awaited<ReturnType<BibliotecaPdfJs['getDocument']>['promise']>;
type Tarefa = ReturnType<BibliotecaPdfJs['getDocument']>;

interface WebView2Web {
  postMessage(mensagem: unknown): void;
  addEventListener(tipo: 'message', ouvinte: (e: { data: unknown }) => void): void;
}

/** Os atalhos que vao ao host (as teclas de Atalhos.MapaLegislacao), com preventDefault. */
const ATALHOS: ReadonlySet<string> = new Set([
  'Ctrl+F', 'Ctrl+C', 'Ctrl+Shift+C', 'Ctrl+Plus', 'Ctrl+Minus', 'Ctrl+0', 'Alt+ArrowUp', 'Alt+ArrowDown', 'F3', 'Shift+F3',
  // Imprimir (2026-11-17): o host abre a impressão do PDF da lei (o arquivo inteiro, não as páginas desenhadas pelo leitor).
  'Ctrl+P',
  // Trilho de módulos (Ctrl+1 … Ctrl+5): valem também com o foco no texto da lei.
  'Ctrl+1', 'Ctrl+2', 'Ctrl+3', 'Ctrl+4', 'Ctrl+5',
]);

const ESCALA_MIN = 0.25;
const ESCALA_MAX = 4;
const MAX_TITULO = 300;

const webview: WebView2Web | undefined = (window as unknown as { chrome?: { webview?: WebView2Web } }).chrome?.webview;
const pdfjs: BibliotecaPdfJs | undefined = (globalThis as unknown as { pdfjsLib?: BibliotecaPdfJs }).pdfjsLib;
const visor: BibliotecaVisor | undefined = (globalThis as unknown as { pdfjsViewer?: BibliotecaVisor }).pdfjsViewer;

let sequencia = 0;

function enviar(tipo: TipoWebLeitor, id: string, dados: Record<string, unknown> = {}): void {
  const m = { v: VERSAO, tipo, id, dados };
  const r = lerMensagemWebLeitor(m);
  if (!r.ok) {
    console.error('[leitor] mensagem para o host descartada: ' + r.motivo);
    return;
  }
  webview?.postMessage(m);
}

function evento(tipo: TipoWebLeitor, dados: Record<string, unknown>): void {
  enviar(tipo, `evento-${++sequencia}`, dados);
}

// ---------- estado ----------

interface Marcador { titulo: string; externo: boolean; filhos: Marcador[] }
interface ItemOutline { title: string; dest: unknown; url: string | null; unsafeUrl?: string; items: ItemOutline[] }

let eventBus: InstanceType<BibliotecaVisor['EventBus']>;
let links: InstanceType<BibliotecaVisor['PDFLinkService']>;
let busca: InstanceType<BibliotecaVisor['PDFFindController']>;
let viewer: InstanceType<BibliotecaVisor['PDFViewer']>;
let documento: Documento | null = null;
let tarefa: Tarefa | null = null;
let outline: ItemOutline[] = [];
let termoAtual = '';
let geracao = 0; // cada abrir/fechar invalida o que ainda estava em andamento do anterior

function codigoDoErro(e: unknown): CodigoErro {
  if (pdfjs && e instanceof pdfjs.PasswordException) return 'senha';
  if (pdfjs && e instanceof pdfjs.InvalidPDFException) return 'invalido';
  const nome = typeof e === 'object' && e !== null ? (e as { name?: unknown }).name : undefined;
  if (nome === 'PasswordException') return 'senha';
  if (nome === 'InvalidPDFException') return 'invalido';
  return 'falhou';
}

function iniciarVisor(v: BibliotecaVisor): void {
  const container = document.getElementById('container') as HTMLDivElement;
  eventBus = new v.EventBus();
  links = new v.PDFLinkService({ eventBus });
  links.externalLinkEnabled = false;
  busca = new v.PDFFindController({ eventBus, linkService: links, updateMatchesCountOnProgress: false, delay: 0 });
  viewer = new v.PDFViewer({
    container,
    eventBus,
    linkService: links,
    findController: busca,
    textLayerMode: 1, // ENABLE
    annotationMode: pdfjs!.AnnotationMode.ENABLE,
    annotationEditorMode: pdfjs!.AnnotationEditorType.DISABLE,
    removePageBorders: false,
  });
  links.setViewer(viewer);
  eventBus.on('pagesinit', () => { viewer.currentScaleValue = 'page-width'; });
  eventBus.on('pagechanging', (e: { pageNumber: number }) => {
    evento('paginaMudou', { atual: e.pageNumber, total: viewer.pagesCount });
  });
  eventBus.on('scalechanging', () => evento('zoomMudou', { escala: viewer.currentScale }));
}

// ---------- abrir e fechar ----------

async function fecharAtual(): Promise<void> {
  geracao++;
  termoAtual = '';
  outline = [];
  const doc = documento;
  const t = tarefa;
  documento = null;
  tarefa = null;
  viewer.setDocument(null as unknown as Documento);
  links.setDocument(null, null);
  try { await t?.destroy(); } catch { /* ja destruida */ }
  void doc;
}

function marcadores(itens: ItemOutline[]): Marcador[] {
  return itens.map((i) => ({
    titulo: String(i.title ?? '').replace(/\s+/g, ' ').trim().slice(0, MAX_TITULO),
    externo: !i.dest && !!(i.url || i.unsafeUrl),
    filhos: marcadores(i.items ?? []),
  }));
}

async function abrir(id: string, url: string, pagina: number | null, termo: string | null): Promise<void> {
  await fecharAtual();
  const minha = geracao;
  let t: Tarefa;
  try {
    t = pdfjs!.getDocument(opcoesDocumento(url));
    tarefa = t;
    const doc = await t.promise;
    if (minha !== geracao) { try { await t.destroy(); } catch { /* */ } return; }
    documento = doc;
    const iniciou = new Promise<void>((ok) => eventBus.on('pagesinit', () => ok(), { once: true }));
    viewer.setDocument(doc);
    links.setDocument(doc, null);
    await iniciou;
    viewer.currentScaleValue = 'page-width';
    outline = ((await doc.getOutline()) ?? []) as unknown as ItemOutline[];
    if (minha !== geracao) return;
    if (pagina !== null) viewer.currentPageNumber = Math.min(Math.max(1, Math.trunc(pagina)), doc.numPages);
    let procurado: Record<string, unknown> | null = null;
    if (termo) procurado = await procurar(termo, 0, false);
    enviar('aberto', id, { paginas: doc.numPages, marcadores: marcadores(outline), escala: viewer.currentScale, ...(procurado ? { procurado } : {}) });
  } catch (e) {
    if (minha === geracao) { documento = null; tarefa = null; }
    enviar('erro', id, { codigo: codigoDoErro(e), mensagem: e instanceof Error ? e.message : String(e) });
  }
}

// ---------- pesquisa na lei aberta ----------

/** Espera o PDFFindController varrer todas as paginas (texto extraido e ocorrencias contadas) e le o contador dele. */
async function resultadoDaBusca(): Promise<Record<string, unknown>> {
  const b = busca as unknown as {
    _firstPageCapability: { promise: Promise<unknown> };
    _extractTextPromises: Promise<unknown>[];
    pageMatches: number[][];
    selected: { pageIdx: number; matchIdx: number };
  };
  await b._firstPageCapability.promise;
  await Promise.all(b._extractTextPromises);
  await new Promise((ok) => setTimeout(ok, 0));
  let total = 0;
  for (const p of b.pageMatches) total += p?.length ?? 0;
  const { pageIdx, matchIdx } = b.selected;
  let atual = 0;
  if (matchIdx >= 0 && pageIdx >= 0) {
    for (let i = 0; i < pageIdx; i++) atual += b.pageMatches[i]?.length ?? 0;
    atual += matchIdx + 1;
  }
  if (total === 0 || atual < 1 || atual > total) return { total: 0, atual: 0, pagina: null };
  return { total, atual, pagina: pageIdx + 1 };
}

function despachar(query: string, anterior: boolean): void {
  eventBus.dispatch('find', {
    source: null, type: 'again', query, caseSensitive: false, entireWord: false, highlightAll: true,
    findPrevious: anterior, matchDiacritics: false,
  });
}

/** Termo novo digitado comeca da primeira pagina; o de abrir {pagina, termo} comeca da pagina pedida (desdeOInicio falso). */
async function procurar(termo: string, passo: number, desdeOInicio = true): Promise<Record<string, unknown>> {
  if (!documento) return { total: 0, atual: 0, pagina: null };
  if (termo === '') {
    if (termoAtual !== '') {
      despachar('', false); // o proximo termo, mesmo igual ao anterior, recomeca do zero
      eventBus.dispatch('findbarclose', { source: null });
    }
    termoAtual = '';
    return { total: 0, atual: 0, pagina: null };
  }
  if (termo === termoAtual && passo === 0) return resultadoDaBusca();
  const novo = termo !== termoAtual;
  termoAtual = termo;
  if (novo && desdeOInicio) viewer.currentPageNumber = 1;
  despachar(termo, !novo && passo < 0);
  return resultadoDaBusca();
}

// ---------- selecao ----------

function paginaDe(no: Node | null): number | null {
  const el = no instanceof Element ? no : no?.parentElement ?? null;
  const pagina = el?.closest('.page[data-page-number]') as HTMLElement | null;
  const n = pagina ? Number(pagina.dataset.pageNumber) : NaN;
  return Number.isFinite(n) ? n : null;
}

function selecao(): { texto: string; paginaInicial: number | null; paginaFinal: number | null } {
  const s = document.getSelection();
  const texto = s && !s.isCollapsed ? s.toString() : '';
  if (!s || texto.trim() === '') return { texto: '', paginaInicial: null, paginaFinal: null };
  const a = paginaDe(s.anchorNode);
  const b = paginaDe(s.focusNode);
  const paginas = [a, b].filter((x): x is number => x !== null);
  if (paginas.length === 0) return { texto: '', paginaInicial: null, paginaFinal: null };
  return { texto, paginaInicial: Math.min(...paginas), paginaFinal: Math.max(...paginas) };
}

let tinhaSelecao = false;
document.addEventListener('selectionchange', () => {
  const tem = selecao().texto !== '';
  if (tem === tinhaSelecao) return;
  tinhaSelecao = tem;
  evento('selecaoMudou', { temSelecao: tem });
});

// ---------- escala e navegacao ----------

function aplicarZoom(acao: string): void {
  if (!documento) return;
  if (acao === 'mais') viewer.increaseScale();
  else if (acao === 'menos') viewer.decreaseScale();
  else if (acao === 'largura') viewer.currentScaleValue = 'page-width';
  else if (acao === 'pagina') viewer.currentScaleValue = 'page-fit';
  if (viewer.currentScale > ESCALA_MAX) viewer.currentScale = ESCALA_MAX;
  if (viewer.currentScale < ESCALA_MIN) viewer.currentScale = ESCALA_MIN;
}

function paginaAtual(): Record<string, unknown> {
  return { atual: documento ? viewer.currentPageNumber : 0, total: documento ? viewer.pagesCount : 0 };
}

/** Vai ao destino do marcador e devolve a pagina dele (null: sem destino ou link externo). */
async function irMarcador(caminho: unknown): Promise<number | null> {
  if (!Array.isArray(caminho) || caminho.length === 0) return null;
  let itens = outline;
  let item: ItemOutline | undefined;
  for (const i of caminho) {
    if (typeof i !== 'number' || !Number.isInteger(i)) return null;
    item = itens[i];
    if (!item) return null;
    itens = item.items ?? [];
  }
  if (!item?.dest || !documento) return null; // link externo ou sem destino: nada a fazer
  // A pagina do destino (nome ou destino explicito); o PDF.js rola ate ela e o contador fica nela.
  const dest = typeof item.dest === 'string' ? await documento.getDestination(item.dest) : (item.dest as unknown[]);
  const ref = Array.isArray(dest) ? dest[0] : null;
  let pagina: number | null = null;
  if (typeof ref === 'number') pagina = ref + 1;
  else if (ref && typeof ref === 'object') pagina = (await documento.getPageIndex(ref as Parameters<Documento['getPageIndex']>[0])) + 1;
  await links.goToDestination(item.dest as string | unknown[]);
  if (pagina !== null) viewer.currentPageNumber = pagina;
  await new Promise((ok) => setTimeout(ok, 0));
  if (pagina !== null && viewer.currentPageNumber !== pagina) viewer.currentPageNumber = pagina;
  return pagina;
}

// ---------- mensagens do host ----------

async function tratar(msg: MensagemHostLeitor): Promise<void> {
  const d = msg.dados;
  switch (msg.tipo) {
    case 'abrir': {
      if (typeof d.url !== 'string' || d.url === '') { enviar('erro', msg.id, { codigo: 'falhou', mensagem: 'abrir sem url' }); return; }
      const pagina = typeof d.pagina === 'number' ? d.pagina : null;
      const termo = typeof d.termo === 'string' && d.termo !== '' ? d.termo : null;
      await abrir(msg.id, d.url, pagina, termo);
      return;
    }
    case 'fechar':
      await fecharAtual();
      document.getSelection()?.removeAllRanges();
      enviar('fechado', msg.id, {});
      return;
    case 'irPagina': {
      // Responde a pagina pedida (ajustada a 1..M): e nela que o PDF.js acabou de pôr a vista.
      let alvo: number | null = null;
      if (documento && typeof d.pagina === 'number' && Number.isFinite(d.pagina)) {
        alvo = Math.min(Math.max(1, Math.trunc(d.pagina)), viewer.pagesCount);
        viewer.currentPageNumber = alvo;
      }
      enviar('paginaAtual', msg.id, alvo === null ? paginaAtual() : { atual: alvo, total: viewer.pagesCount });
      return;
    }
    case 'irMarcador': {
      const alvo = documento ? await irMarcador(d.caminho) : null;
      enviar('paginaAtual', msg.id, alvo === null ? paginaAtual() : { atual: alvo, total: viewer.pagesCount });
      return;
    }
    case 'zoom':
      aplicarZoom(typeof d.acao === 'string' ? d.acao : '');
      enviar('zoomAplicado', msg.id, { escala: documento ? viewer.currentScale : 0 });
      return;
    case 'procurar': {
      const termo = typeof d.termo === 'string' ? d.termo : '';
      const passo = d.passo === 1 || d.passo === -1 ? d.passo : 0;
      enviar('procurado', msg.id, await procurar(termo, passo));
      return;
    }
    case 'obterSelecao': {
      const s = selecao();
      enviar('selecao', msg.id, { texto: s.texto, paginaInicial: s.paginaInicial, paginaFinal: s.paginaFinal });
      return;
    }
  }
}

let fila: Promise<void> = Promise.resolve();

function aoReceber(bruto: unknown): void {
  const r = lerMensagemHostLeitor(bruto);
  if (!r.ok) {
    console.warn('[leitor] mensagem do host descartada: ' + r.motivo);
    enviar('erro', r.id ?? 'sem-id', { codigo: 'falhou', mensagem: 'mensagem_invalida: ' + r.motivo });
    return;
  }
  const msg = r.msg;
  // Uma mensagem por vez, na ordem em que chegaram (abrir espera o fechar do anterior, procurar espera o abrir).
  fila = fila.then(() => tratar(msg)).catch((e) => {
    enviar('erro', msg.id, { codigo: 'falhou', mensagem: e instanceof Error ? e.message : String(e) });
  });
}

// ---------- teclado, roda e CSP ----------

function nomeDoAtalho(e: KeyboardEvent): string | null {
  if (e.metaKey) return null;
  let shift = e.shiftKey;
  let tecla: string;
  if (/^[a-z]$/i.test(e.key)) tecla = e.key.toUpperCase();
  else if (e.key === '+' || e.key === '=') { tecla = 'Plus'; shift = false; }
  else if (e.key === '-' || e.key === '_') { tecla = 'Minus'; shift = false; }
  else tecla = e.key;
  return [e.ctrlKey ? 'Ctrl' : '', e.altKey ? 'Alt' : '', shift ? 'Shift' : '', tecla].filter((p) => p !== '').join('+');
}

window.addEventListener('keydown', (e) => {
  const nome = nomeDoAtalho(e);
  if (nome === null || !ATALHOS.has(nome)) return;
  e.preventDefault();
  e.stopPropagation();
  evento('atalho', { tecla: nome });
}, true);

window.addEventListener('wheel', (e) => {
  if (!e.ctrlKey) return;
  e.preventDefault();
  if (!documento) return;
  aplicarZoom(e.deltaY < 0 ? 'mais' : 'menos');
}, { passive: false, capture: true });

let ultimaViolacao = { chave: '', quando: -Infinity };
document.addEventListener('securitypolicyviolation', (e) => {
  const chave = `${e.violatedDirective}|${e.blockedURI}`;
  const agora = performance.now();
  if (chave === ultimaViolacao.chave && agora - ultimaViolacao.quando < 100) return;
  ultimaViolacao = { chave, quando: agora };
  evento('cspViolacao', { diretiva: e.violatedDirective, uri: e.blockedURI });
});

if (!webview) {
  console.error('[leitor] window.chrome.webview indisponivel');
} else if (!pdfjs || !visor) {
  enviar('erro', 'inicio', { codigo: 'falhou', mensagem: 'O PDF.js nao foi carregado.' });
} else {
  configurarPdfJs(pdfjs);
  iniciarVisor(visor);
  webview.addEventListener('message', (e) => aoReceber(e.data));
  enviar('pronto', 'pronto', { versao: VERSAO, pdfjs: pdfjs.version });
}
