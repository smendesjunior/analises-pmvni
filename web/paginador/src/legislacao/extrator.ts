// Extrator de texto dos PDFs anexados: roda no WebView2 oculto, na pagina https://leg.pmvni.invalid/extrator.html.
// O host manda `extrair {url}`; para cada pagina vem `textoPagina` e `progresso`, e no fim `extraido {paginas}` ou `erro`.
// Texto da pagina = `str` dos itens na ordem, com "\n" depois de cada item com `hasEOL`. Sem OCR, sem tentar senha.
// Figuras (correcoes 2026-11-15): o host manda `figuras {url}`; para cada pagina vem `figurasPagina {pagina, figuras}` e no fim
// `figurasExtraidas {paginas}` ou `erro` (veja figuras.ts).
import type { PDFDocumentLoadingTask, PDFPageProxy } from 'pdfjs-dist';
import { lerOperacoes, linhaDaFigura, montarTexto, regioes, type Caixa, type FiguraWeb } from './figuras';
import { lerMensagemHost, lerMensagemWeb, VERSAO, type CodigoErro, type TipoWeb } from './mensagens';
import { configurarPdfJs, opcoesDocumento, type BibliotecaPdfJs } from './opcoes-pdfjs';

interface WebView2Web {
  postMessage(mensagem: unknown): void;
  addEventListener(tipo: 'message', ouvinte: (e: { data: unknown }) => void): void;
}

const webview: WebView2Web | undefined = (window as unknown as { chrome?: { webview?: WebView2Web } }).chrome?.webview;
const pdfjs: BibliotecaPdfJs | undefined = (globalThis as unknown as { pdfjsLib?: BibliotecaPdfJs }).pdfjsLib;

let sequencia = 0;

/** Envia uma mensagem ao host (validada antes de sair). */
function enviar(tipo: TipoWeb, id: string, dados: Record<string, unknown> = {}): void {
  const m = { v: VERSAO, tipo, id, dados };
  const r = lerMensagemWeb(m);
  if (!r.ok) {
    console.error('[extrator] mensagem para o host descartada: ' + r.motivo);
    return;
  }
  webview?.postMessage(m);
}

/** Evento (id evento-N) ligado a uma extracao: o host ignora o que sobrar de uma extracao cancelada. */
function evento(tipo: TipoWeb, extracao: string | null, dados: Record<string, unknown>): void {
  enviar(tipo, `evento-${++sequencia}`, extracao === null ? dados : { extracao, ...dados });
}

interface Extracao {
  id: string;
  cancelada: boolean;
  tarefa: PDFDocumentLoadingTask | null;
}

class Cancelada extends Error {}

let atual: Extracao | null = null;
let fila: Promise<void> = Promise.resolve();

function codigoDoErro(e: unknown): CodigoErro {
  if (pdfjs && e instanceof pdfjs.PasswordException) return 'senha';
  if (pdfjs && e instanceof pdfjs.InvalidPDFException) return 'invalido';
  const nome = typeof e === 'object' && e !== null ? (e as { name?: unknown }).name : undefined;
  if (nome === 'PasswordException') return 'senha';
  if (nome === 'InvalidPDFException') return 'invalido';
  return 'falhou';
}

async function extrair(id: string, url: string): Promise<void> {
  const estado: Extracao = { id, cancelada: false, tarefa: null };
  atual = estado;
  try {
    if (!pdfjs) throw new Error('O PDF.js nao foi carregado.');
    const tarefa = pdfjs.getDocument(opcoesDocumento(url));
    estado.tarefa = tarefa;
    const doc = await tarefa.promise;
    const total = doc.numPages;
    for (let p = 1; p <= total; p++) {
      if (estado.cancelada) throw new Cancelada();
      const pagina = await doc.getPage(p);
      const conteudo = await pagina.getTextContent();
      const { texto } = montarTexto(conteudo.items);
      pagina.cleanup();
      if (estado.cancelada) throw new Cancelada();
      evento('textoPagina', id, { pagina: p, texto });
      evento('progresso', id, { pagina: p, total });
    }
    await tarefa.destroy();
    enviar('extraido', id, { paginas: total });
  } catch (e) {
    const codigo: CodigoErro = estado.cancelada ? 'falhou' : codigoDoErro(e);
    const mensagem = estado.cancelada ? 'Extracao cancelada.' : e instanceof Error ? e.message : String(e);
    enviar('erro', id, { codigo, mensagem });
    try { await estado.tarefa?.destroy(); } catch { /* ja destruida */ }
  } finally {
    if (atual === estado) atual = null;
  }
}

// ---- Figuras da leitura padronizada (correcoes 2026-11-15) ----

const MAX_PIXELS = 4_000_000; // pagina desenhada com no maximo ~4 MP (A4 a 2x)
const MAX_FIGURAS_PAGINA = 40;

/** Recorta a regiao (em pixels do canvas) e devolve PNG (ou JPEG, se for grande) em base64. */
function recortar(origem: HTMLCanvasElement, x: number, y: number, w: number, h: number): { tipo: string; dados: string } {
  const c = document.createElement('canvas');
  c.width = Math.max(1, Math.round(w));
  c.height = Math.max(1, Math.round(h));
  const ctx = c.getContext('2d');
  if (!ctx) throw new Error('canvas sem contexto 2d');
  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, c.width, c.height);
  ctx.drawImage(origem, Math.round(x), Math.round(y), c.width, c.height, 0, 0, c.width, c.height);
  const tipo = c.width * c.height > 1_200_000 ? 'image/jpeg' : 'image/png';
  const url = c.toDataURL(tipo, 0.86);
  const virgula = url.indexOf(',');
  if (!url.startsWith('data:' + tipo) || virgula < 0) throw new Error('recorte vazio');
  return { tipo, dados: url.slice(virgula + 1) };
}

async function figurasDaPagina(pagina: PDFPageProxy, estado: Extracao): Promise<FiguraWeb[]> {
  const conteudo = await pagina.getTextContent();
  const texto = montarTexto(conteudo.items);
  const [vx0, vy0, vx1, vy1] = pagina.view as [number, number, number, number];
  const vista = { x0: Math.min(vx0, vx1), y0: Math.min(vy0, vy1), x1: Math.max(vx0, vx1), y1: Math.max(vy0, vy1) };
  let lista: { caixa: Caixa; imagem: boolean }[] = [];
  let semLugar = false;
  try {
    const ops = await pagina.getOperatorList({ intent: 'print', annotationMode: pdfjs!.AnnotationMode.DISABLE });
    const g = lerOperacoes(ops.fnArray, ops.argsArray, pdfjs!.OPS as unknown as Record<string, number>);
    lista = regioes(g, vista, texto).slice(0, MAX_FIGURAS_PAGINA);
    semLugar = g.semLugar && lista.length === 0;
  } catch {
    semLugar = true; // lista de operacoes ilegivel: a pagina inteira garante que nada some
  }
  if (lista.length === 0 && !semLugar) return [];
  if (estado.cancelada) throw new Cancelada();

  const base = pagina.getViewport({ scale: 1 });
  const escala = Math.min(2, Math.sqrt(MAX_PIXELS / Math.max(1, base.width * base.height)));
  const viewport = pagina.getViewport({ scale: escala });
  const largura = vista.x1 - vista.x0;
  const paginaInteira = (origem: 'pagina' | 'falhou', tipo = '', dados = ''): FiguraWeb =>
    ({ linha: texto.linhasY.length, x: 0, y: 0, w: 1, h: 1, largura, origem, tipo, dados });
  const canvas = document.createElement('canvas');
  canvas.width = Math.ceil(viewport.width);
  canvas.height = Math.ceil(viewport.height);
  try {
    // intent 'print': sem requestAnimationFrame, que fica parado no WebView2 oculto (com 'display' o desenho nunca termina).
    // Limite de 30 s por página: uma página que não termina de desenhar vira o aviso, sem segurar as outras.
    const tarefa = pagina.render({ canvas, viewport, intent: 'print', annotationMode: pdfjs!.AnnotationMode.DISABLE });
    let limite: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([tarefa.promise, new Promise((_, falhar) => { limite = setTimeout(() => falhar(new Error('tempo')), 30_000); })]);
    } catch (e) {
      tarefa.cancel();
      throw e;
    } finally {
      clearTimeout(limite);
    }
  } catch {
    return [paginaInteira('falhou')];
  }
  try {
    if (semLugar) {
      const r = recortar(canvas, 0, 0, canvas.width, canvas.height);
      return [paginaInteira('pagina', r.tipo, r.dados)];
    }
    const figuras: FiguraWeb[] = [];
    for (const reg of lista) {
      const [ax, ay] = viewport.convertToViewportPoint(reg.caixa.x0, reg.caixa.y0) as number[];
      const [bx, by] = viewport.convertToViewportPoint(reg.caixa.x1, reg.caixa.y1) as number[];
      const x = Math.max(0, Math.min(ax!, bx!));
      const y = Math.max(0, Math.min(ay!, by!));
      const w = Math.min(canvas.width, Math.max(ax!, bx!)) - x;
      const h = Math.min(canvas.height, Math.max(ay!, by!)) - y;
      if (w < 2 || h < 2) continue;
      const r = recortar(canvas, x, y, w, h);
      figuras.push({
        linha: linhaDaFigura(texto.linhasY, reg.caixa.y1),
        x: x / canvas.width, y: y / canvas.height, w: w / canvas.width, h: h / canvas.height,
        largura: reg.caixa.x1 - reg.caixa.x0, origem: reg.imagem ? 'imagem' : 'desenho', tipo: r.tipo, dados: r.dados,
      });
    }
    return figuras;
  } catch {
    // Recorte falhou (canvas grande demais, memoria): tenta a pagina inteira, menor.
    try {
      const r = recortar(canvas, 0, 0, canvas.width, canvas.height);
      return [paginaInteira('pagina', r.tipo, r.dados)];
    } catch {
      return [paginaInteira('falhou')];
    }
  } finally {
    canvas.width = canvas.height = 0; // solta a memoria do canvas
  }
}

async function extrairFiguras(id: string, url: string): Promise<void> {
  const estado: Extracao = { id, cancelada: false, tarefa: null };
  atual = estado;
  try {
    if (!pdfjs) throw new Error('O PDF.js nao foi carregado.');
    const tarefa = pdfjs.getDocument(opcoesDocumento(url));
    estado.tarefa = tarefa;
    const doc = await tarefa.promise;
    const total = doc.numPages;
    for (let p = 1; p <= total; p++) {
      if (estado.cancelada) throw new Cancelada();
      const pagina = await doc.getPage(p);
      let figuras: FiguraWeb[];
      try {
        figuras = await figurasDaPagina(pagina, estado);
      } catch (e) {
        if (e instanceof Cancelada || estado.cancelada) throw e;
        figuras = [{ linha: 1e6, x: 0, y: 0, w: 1, h: 1, largura: 0, origem: 'falhou', tipo: '', dados: '' }];
      }
      pagina.cleanup();
      if (estado.cancelada) throw new Cancelada();
      evento('figurasPagina', id, { pagina: p, figuras });
    }
    await tarefa.destroy();
    enviar('figurasExtraidas', id, { paginas: total });
  } catch (e) {
    const codigo: CodigoErro = estado.cancelada ? 'falhou' : codigoDoErro(e);
    const mensagem = estado.cancelada ? 'Extracao cancelada.' : e instanceof Error ? e.message : String(e);
    enviar('erro', id, { codigo, mensagem });
    try { await estado.tarefa?.destroy(); } catch { /* ja destruida */ }
  } finally {
    if (atual === estado) atual = null;
  }
}

function aoReceber(bruto: unknown): void {
  const r = lerMensagemHost(bruto);
  if (!r.ok) {
    console.warn('[extrator] mensagem do host descartada: ' + r.motivo);
    enviar('erro', r.id ?? 'sem-id', { codigo: 'falhou', mensagem: 'mensagem_invalida: ' + r.motivo });
    return;
  }
  const { msg } = r;
  if (msg.tipo === 'extrair') {
    const url = msg.dados.url;
    if (typeof url !== 'string' || url === '') {
      enviar('erro', msg.id, { codigo: 'falhou', mensagem: 'extrair sem url' });
      return;
    }
    // Uma extracao por vez: a nova espera a anterior (cancelada ou nao) terminar.
    fila = fila.then(() => extrair(msg.id, url));
  } else if (msg.tipo === 'figuras') {
    const url = msg.dados.url;
    if (typeof url !== 'string' || url === '') {
      enviar('erro', msg.id, { codigo: 'falhou', mensagem: 'figuras sem url' });
      return;
    }
    fila = fila.then(() => extrairFiguras(msg.id, url));
  } else if (msg.tipo === 'cancelar') {
    if (atual) {
      atual.cancelada = true;
      void atual.tarefa?.destroy();
    }
  }
}

// Violacao de CSP (script em linha, eval, rede fora do host): informada ao host, que conta. A pagina tem duas politicas
// iguais (meta e cabecalho) e o Chromium dispara um evento por politica; o mesmo bloqueio visto em ate 100 ms conta uma vez.
let ultimaViolacao = { chave: '', quando: -Infinity };
document.addEventListener('securitypolicyviolation', (e) => {
  const chave = `${e.violatedDirective}|${e.blockedURI}`;
  const agora = performance.now();
  if (chave === ultimaViolacao.chave && agora - ultimaViolacao.quando < 100) return;
  ultimaViolacao = { chave, quando: agora };
  evento('cspViolacao', null, { diretiva: e.violatedDirective, uri: e.blockedURI });
});

if (!webview) {
  console.error('[extrator] window.chrome.webview indisponivel');
} else if (!pdfjs) {
  enviar('erro', 'inicio', { codigo: 'falhou', mensagem: 'O PDF.js nao foi carregado.' });
} else {
  configurarPdfJs(pdfjs);
  webview.addEventListener('message', (e) => aoReceber(e.data));
  enviar('pronto', 'pronto', { versao: VERSAO, pdfjs: pdfjs.version });
}
