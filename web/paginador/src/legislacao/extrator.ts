// Extrator de texto dos PDFs anexados: roda no WebView2 oculto, na pagina https://leg.pmvni.invalid/extrator.html.
// O host manda `extrair {url}`; para cada pagina vem `textoPagina` e `progresso`, e no fim `extraido {paginas}` ou `erro`.
// Texto da pagina = `str` dos itens na ordem, com "\n" depois de cada item com `hasEOL`. Sem OCR, sem tentar senha.
import type { PDFDocumentLoadingTask } from 'pdfjs-dist';
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
      let texto = '';
      for (const item of conteudo.items) {
        if ('str' in item) {
          texto += item.str;
          if (item.hasEOL) texto += '\n';
        }
      }
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
