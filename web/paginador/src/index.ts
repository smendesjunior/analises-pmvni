// Ponte JS do paginador: valida o que o host manda, despacha para os manipuladores registrados e responde com o mesmo id.
import { aplicarPlano, compor, lerPedido, lerPedidoAplicar } from './folhas';
import { ErroComposicao } from './plano';
import { textoPorFolha } from './texto';
import { registrarEdicao } from './edicao';
import { registrarTela } from './tela';

// Expostos no global Paginador para os testes: montagem de uma folha pelo plano e avisos/estouros de largura.
export { larguraNoPlano, montarFolhaDoPlano } from './folhas';
export { textoPorFolha } from './texto';
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
    const codigo = e instanceof ErroComposicao ? e.codigo : 'falha_no_manipulador';
    enviar('erro', msg.id, { codigo, mensagem: e instanceof Error ? e.message : String(e) });
  }
}

// Paginador (Tarefa 7): compoe o documento em folhas A4 e devolve o plano de quebras.
registrar('compor', async (dados) => ({ tipo: 'composto', dados: { ...(await compor(lerPedido(dados))) } }));

// Tarefa 8: aplica um plano ja calculado (sem medir para decidir) e devolve o texto visivel de cada folha.
registrar('aplicarPlano', async (dados) => ({ tipo: 'planoAplicado', dados: { ...(await aplicarPlano(lerPedidoAplicar(dados))) } }));
registrar('textoPorFolha', () => ({ tipo: 'textoPorFolha', dados: { folhas: textoPorFolha() } }));

// Tarefa 10: tela (zoom, rolagem, indicador de folha); aparência só de tela, fora das folhas.
registrarTela(registrar, enviar);

// Tarefa 7 (Etapa 2): selecao, campos editaveis, edicao no A4, marcas de revisao, ancora, atalhos e menu de contexto.
registrarEdicao(registrar, enviar);

if (webview) {
  webview.addEventListener('message', (e) => void aoReceber(e.data));
  enviar('pronto', 'pronto', { versao: VERSAO });
} else {
  console.error('[ponte] window.chrome.webview indisponivel');
}
