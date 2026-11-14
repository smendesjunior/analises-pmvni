// Tela (Tarefa 10): visualização contínua das folhas no WebView2 visível: fundo cinza, sombra, vão entre folhas, zoom,
// rolagem e indicador de folha. TODA a aparência fica fora das folhas e só vale na mídia de tela (@media screen): as folhas
// compostas (section.folha) não ganham nenhuma regra de impressão, então o PDF sai igual com ou sem a tela. O zoom é um
// transform: scale() no <body> com o tamanho da rolagem ajustado por --pmvni-tela-altura; nada é medido nem recomposto.
// Ativação: a primeira mensagem `definirZoom` injeta o <style> da tela (os testes de composição, que não pedem zoom, ficam
// como estavam).

import { ErroComposicao } from './plano';
import type { Manipulador } from './index';
import type { TipoHost, TipoWeb } from './mensagens';

const ID_CSS = 'pmvni-tela-css';
const ATR_TRANSBORDA = 'data-pmvni-transborda';
const PX_POR_MM = 96 / 25.4;
const LARGURA_FOLHA_PX = 210 * PX_POR_MM;
const ALTURA_FOLHA_PX = 297 * PX_POR_MM;
const MARGEM_AJUSTE_PX = 16;
const ZOOM_MIN = 0.5;
const ZOOM_MAX = 3;
const ESPERA_FOLHA_VISIVEL_MS = 120;

/** CSS só de tela. Tudo dentro de @media screen: a impressão (PDF) nunca vê estas regras. As variáveis --pmvni-zoom,
 * --pmvni-tela-x e --pmvni-tela-altura são definidas no <html> e só são usadas aqui. */
const CSS_TELA = `
@media screen {
  :root { --pmvni-zoom: 1; --pmvni-tela-x: 0px; --pmvni-tela-fundo: #e4e4e4; --pmvni-tela-sombra: rgba(0, 0, 0, .30); }
  html { background: var(--pmvni-tela-fundo) !important; height: var(--pmvni-tela-altura, auto); overflow-y: scroll; }
  body {
    background: transparent !important; margin: 0 !important; padding: 8mm 0 !important; width: 210mm !important;
    box-sizing: content-box !important; transform-origin: 0 0; transform: translateX(var(--pmvni-tela-x)) scale(var(--pmvni-zoom));
  }
  section.folha { margin: 0 0 8mm 0 !important; box-shadow: 0 1px 4px var(--pmvni-tela-sombra), 0 0 0 1px rgba(0, 0, 0, .08); }
  section.folha:last-of-type { margin-bottom: 0 !important; }
  /* Folha que transborda: faixa vermelha fina na margem cinza, fora da folha. */
  section.folha[${ATR_TRANSBORDA}] { box-shadow: -5px 0 0 0 #c42b1c, 0 1px 4px var(--pmvni-tela-sombra), 0 0 0 1px rgba(0, 0, 0, .08); }
}
@media screen and (prefers-color-scheme: dark) {
  :root { --pmvni-tela-fundo: #1f1f1f; --pmvni-tela-sombra: rgba(0, 0, 0, .75); }
}
`;

let zoom = 1;
let ativa = false;
let observador: IntersectionObserver | null = null;
let temporizador: number | undefined;
let ultimaFolha = 0;

type Enviar = (tipo: TipoWeb, id: string, dados?: Record<string, unknown>) => void;
type Registrar = (tipo: TipoHost, manipulador: Manipulador) => void;

const folhas = (): HTMLElement[] => Array.from(document.querySelectorAll<HTMLElement>('section.folha'));
const raiz = document.documentElement;

function ativar(enviar: Enviar): void {
  if (ativa) return;
  ativa = true;
  const css = document.createElement('style');
  css.id = ID_CSS;
  css.textContent = CSS_TELA;
  document.head.appendChild(css);
  window.addEventListener('resize', () => { centralizar(); agendarFolhaVisivel(enviar); });
}

/** Folha (1 = primeira) que cruza a linha do meio da janela; no vão entre folhas, a mais próxima; 0 se não há folhas. */
function folhaAtual(): number {
  const lista = folhas();
  if (lista.length === 0) return 0;
  const meio = raiz.clientHeight / 2;
  let melhor = 0;
  let distancia = Infinity;
  lista.forEach((f, i) => {
    const r = f.getBoundingClientRect();
    const d = meio < r.top ? r.top - meio : meio > r.bottom ? meio - r.bottom : 0;
    if (d < distancia) { distancia = d; melhor = i; }
  });
  return melhor + 1;
}

/** Centraliza as folhas na janela (translate antes da escala) e fixa o tamanho de rolagem. */
function centralizar(): void {
  const larguraNaTela = LARGURA_FOLHA_PX * zoom;
  const x = Math.max(0, (raiz.clientWidth - larguraNaTela) / 2);
  raiz.style.setProperty('--pmvni-tela-x', `${x}px`);
}

function aplicarZoom(novo: number): void {
  const antes = zoom;
  const centroDoc = (window.scrollY + raiz.clientHeight / 2) / antes;
  zoom = novo;
  raiz.style.setProperty('--pmvni-zoom', String(zoom));
  centralizar();
  // offsetHeight é o layout sem transform; a rolagem acompanha o tamanho escalado.
  raiz.style.setProperty('--pmvni-tela-altura', `${document.body.offsetHeight * zoom}px`);
  if (antes !== zoom) window.scrollTo({ top: Math.max(0, centroDoc * zoom - raiz.clientHeight / 2), behavior: 'instant' });
}

function limitar(z: number): number {
  return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, z));
}

function observar(enviar: Enviar): void {
  observador?.disconnect();
  observador = new IntersectionObserver(() => agendarFolhaVisivel(enviar), { threshold: [0, 0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1] });
  for (const f of folhas()) observador.observe(f);
  ultimaFolha = 0;
  agendarFolhaVisivel(enviar);
}

/** Indicador de folha: avisa o host (mensagem `folhaVisivel`) depois que a rolagem assenta, só quando a folha muda. */
function agendarFolhaVisivel(enviar: Enviar): void {
  window.clearTimeout(temporizador);
  temporizador = window.setTimeout(() => {
    const folha = folhaAtual();
    if (folha === 0 || folha === ultimaFolha) return;
    ultimaFolha = folha;
    enviar('folhaVisivel', 'folhaVisivel', estado());
  }, ESPERA_FOLHA_VISIVEL_MS);
}

function estado(): Record<string, unknown> {
  return { zoom, folha: folhaAtual(), total: folhas().length, rolagemY: Math.round(window.scrollY) };
}

function numeroOpcional(valor: unknown, nome: string): number | undefined {
  if (valor === undefined || valor === null) return undefined;
  if (typeof valor !== 'number' || !Number.isFinite(valor)) throw new ErroComposicao('zoom_invalido', `"${nome}" não é um número finito.`);
  return valor;
}

export function registrarTela(registrar: Registrar, enviar: Enviar): void {
  registrar('definirZoom', (dados) => {
    const pedido = numeroOpcional(dados.zoom, 'zoom');
    const ajustar = dados.ajustar;
    if (ajustar !== undefined && ajustar !== 'largura' && ajustar !== 'pagina') {
      throw new ErroComposicao('zoom_invalido', 'ajustar deve ser "largura" ou "pagina".');
    }
    const transbordam = dados.transbordam;
    if (transbordam !== undefined && (!Array.isArray(transbordam) || transbordam.some((n) => !Number.isInteger(n)))) {
      throw new ErroComposicao('zoom_invalido', 'transbordam deve ser uma lista de números de folha.');
    }
    ativar(enviar);
    const lista = folhas();
    if (transbordam) {
      lista.forEach((f, i) => f.toggleAttribute(ATR_TRANSBORDA, transbordam.includes(i + 1)));
    }
    let alvo = zoom;
    if (ajustar === 'largura') alvo = (raiz.clientWidth - 2 * MARGEM_AJUSTE_PX) / LARGURA_FOLHA_PX;
    else if (ajustar === 'pagina') {
      alvo = Math.min((raiz.clientWidth - 2 * MARGEM_AJUSTE_PX) / LARGURA_FOLHA_PX, (raiz.clientHeight - 2 * MARGEM_AJUSTE_PX) / ALTURA_FOLHA_PX);
    } else if (pedido !== undefined) alvo = pedido;
    aplicarZoom(limitar(alvo));
    observar(enviar);
    return { tipo: 'folhaVisivel', dados: estado() };
  });

  registrar('rolarPara', (dados) => {
    const lista = folhas();
    const margem = 8 * PX_POR_MM * zoom;
    let destino: HTMLElement;
    let ajuste = 0;
    if (typeof dados.ref === 'string') {
      const alvo = lista.map((f) => f.querySelector<HTMLElement>(`.area [data-ref="${CSS.escape(dados.ref as string)}"]`)).find((e) => e !== null && e !== undefined);
      if (!alvo) throw new ErroComposicao('ref_inexistente', `Nenhuma folha mostra o bloco "${dados.ref}".`);
      const folha = alvo.closest<HTMLElement>('section.folha')!;
      // Blocos no alto da folha: mostra a folha inteira (cabeçalho incluído); os demais ficam um pouco abaixo do topo.
      const naFolha = alvo.getBoundingClientRect().top - folha.getBoundingClientRect().top;
      destino = naFolha < 40 * PX_POR_MM * zoom ? folha : alvo;
      ajuste = destino === folha ? margem : 16;
    } else if (typeof dados.folha === 'number' && Number.isInteger(dados.folha)) {
      const f = lista[dados.folha - 1];
      if (!f) throw new ErroComposicao('folha_inexistente', `O documento não tem a folha ${dados.folha}.`);
      destino = f;
      ajuste = margem;
    } else {
      throw new ErroComposicao('rolagem_invalida', 'rolarPara pede "folha" (inteiro) ou "ref" (texto).');
    }
    window.scrollTo({ top: Math.max(0, window.scrollY + destino.getBoundingClientRect().top - ajuste), behavior: 'instant' });
    return { tipo: 'folhaVisivel', dados: estado() };
  });
}
