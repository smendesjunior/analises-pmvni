// Ponte JS do paginador: valida o que o host manda, despacha para os manipuladores registrados e responde com o mesmo id.
import { lerMensagemHost, lerMensagemWeb, VERSAO, type MensagemHost, type TipoHost, type TipoWeb } from './mensagens';

export type Resposta = { tipo: TipoWeb; dados: Record<string, unknown> };
export type Manipulador = (dados: Record<string, unknown>, msg: MensagemHost) => Resposta | void | Promise<Resposta | void>;

interface WebView2Web {
  postMessage(mensagem: unknown): void;
  addEventListener(tipo: 'message', ouvinte: (e: { data: unknown }) => void): void;
}

const webview: WebView2Web | undefined = (window as unknown as { chrome?: { webview?: WebView2Web } }).chrome?.webview;
const manipuladores = new Map<TipoHost, Manipulador>();

/** Envia uma mensagem ao host (validada antes de sair). */
export function enviar(tipo: TipoWeb, id: string, dados: Record<string, unknown> = {}): void {
  const m = { v: VERSAO, tipo, id, dados };
  const r = lerMensagemWeb(m);
  if (!r.ok) {
    console.error('[ponte] mensagem para o host descartada: ' + r.motivo);
    return;
  }
  webview?.postMessage(m);
}

/** Registra o manipulador de um tipo host->web. A Resposta devolvida sai com o id da requisicao. */
export function registrar(tipo: TipoHost, manipulador: Manipulador): void {
  manipuladores.set(tipo, manipulador);
}

async function aoReceber(bruto: unknown): Promise<void> {
  const r = lerMensagemHost(bruto);
  if (!r.ok) {
    console.warn('[ponte] mensagem do host descartada: ' + r.motivo);
    enviar('erro', r.id ?? 'sem-id', { codigo: 'mensagem_invalida', motivo: r.motivo });
    return;
  }
  const { msg } = r;
  const manipulador = manipuladores.get(msg.tipo);
  if (!manipulador) {
    enviar('erro', msg.id, { codigo: 'sem_manipulador', tipo: msg.tipo });
    return;
  }
  try {
    const resposta = await manipulador(msg.dados, msg);
    if (resposta) enviar(resposta.tipo, msg.id, resposta.dados);
  } catch (e) {
    enviar('erro', msg.id, { codigo: 'falha_no_manipulador', mensagem: e instanceof Error ? e.message : String(e) });
  }
}

// Manipulador provisorio: o paginador (Tarefa 7) o substitui. Sem folhas ainda, devolve a lista vazia.
registrar('textoPorFolha', () => ({ tipo: 'textoPorFolha', dados: { folhas: [] } }));

if (webview) {
  webview.addEventListener('message', (e) => void aoReceber(e.data));
  enviar('pronto', 'pronto', { versao: VERSAO });
} else {
  console.error('[ponte] window.chrome.webview indisponivel');
}
