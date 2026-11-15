// Pesquisa no relatório (Tarefa 11, Etapa 2): realce das ocorrências pela CSS Custom Highlight API (CSS.highlights). Os
// realces moram FORA do DOM: nenhum elemento, atributo ou nó de texto é criado ou alterado nas folhas, então a
// paginação, o texto por folha e o PDF/HTML exportados não mudam (as regras ::highlight valem só em @media screen).
//
// Protocolo: o host manda `pesquisar {termo, passo}` e a web responde `pesquisado {total, atual, ref}`.
//   - termo vazio limpa os realces e responde total 0;
//   - a busca é feita dentro de cada nó de texto da área útil das folhas (nada atravessa dois nós), sem diferenciar
//     maiúsculas nem acentos: cada caractere vira a sua letra-base minúscula (NFD, 1:1 por caractere), então as posições
//     do texto normalizado são as do texto original;
//   - passo 0: busca de novo e mantém a ocorrência atual se o termo é o mesmo (a primeira se o termo mudou, e então rola até ela);
//     passo 1 / -1: próxima / anterior (com volta ao fim e ao começo) e rola até ela;
//   - passo 0 com `atual` (1-based): o host refaz a pesquisa depois de recompor (o documento é outro, o estado da página
//     recomeçou): realça de novo, com essa ocorrência como a atual, e NÃO rola (a tela já foi reancorada pelo host);
//   - `atual` é 1-based (0 sem ocorrências); `ref` é o data-ref do bloco que contém a ocorrência (ou null).

import { ATR_OCULTO } from './fragmentar';
import type { Manipulador } from './index';
import type { TipoHost } from './mensagens';
import { ErroComposicao } from './plano';

type Registrar = (tipo: TipoHost, manipulador: Manipulador) => void;

const ID_CSS = 'pmvni-pesquisa-css';
const NOME_BUSCA = 'pmvni-busca';
const NOME_ATUAL = 'pmvni-busca-atual';

const CSS_PESQUISA = `
@media screen {
  ::highlight(${NOME_BUSCA}) { background-color: #ffe27a; color: #000; }
  ::highlight(${NOME_ATUAL}) { background-color: #ff9a3c; color: #000; }
}
`;

let termoAtual = '';
let indice = 0;
let ocorrencias: Range[] = [];

/** Letra-base minúscula de um caractere (primeira peça da decomposição NFD); sempre um caractere só, para manter o 1:1. */
function base(c: string): string {
  const minusculo = c.normalize('NFD').toLowerCase();
  return minusculo.length > 0 ? minusculo.charAt(0) : c;
}

/** Texto sem caixa e sem acento, com o mesmo comprimento do original. */
export function normalizarBusca(s: string): string {
  let r = '';
  for (let i = 0; i < s.length; i++) r += base(s.charAt(i));
  return r;
}

function garantirCss(): void {
  if (document.getElementById(ID_CSS)) return;
  const css = document.createElement('style');
  css.id = ID_CSS;
  css.textContent = CSS_PESQUISA;
  document.head.appendChild(css);
}

/** O registro CSS.highlights (a lib.dom do TypeScript ainda não declara set/delete nele). */
interface Registro { set(nome: string, h: Highlight): void; delete(nome: string): boolean }
const api = (): Registro | undefined =>
  typeof CSS !== 'undefined' ? ((CSS as unknown as { highlights?: Registro }).highlights) : undefined;

function limparRealces(): void {
  const h = api();
  h?.delete(NOME_BUSCA);
  h?.delete(NOME_ATUAL);
  ocorrencias = [];
  indice = 0;
}

/** Os nós de texto da área útil das folhas que contam para a busca (fora do que o paginador oculta). */
function nosDeTexto(): Text[] {
  const nos: Text[] = [];
  for (const area of Array.from(document.querySelectorAll<HTMLElement>('section.folha > .area'))) {
    const passeio = document.createTreeWalker(area, NodeFilter.SHOW_TEXT);
    for (let n = passeio.nextNode(); n; n = passeio.nextNode()) {
      const no = n as Text;
      const pai = no.parentElement;
      if (!pai || no.data === '') continue;
      if (pai.closest(`[${ATR_OCULTO}], style, script`)) continue;
      nos.push(no);
    }
  }
  return nos;
}

/** Todas as ocorrências (não sobrepostas) do termo, na ordem do documento. */
function buscar(termo: string): Range[] {
  const alvo = normalizarBusca(termo);
  const achadas: Range[] = [];
  if (alvo === '') return achadas;
  for (const no of nosDeTexto()) {
    const texto = normalizarBusca(no.data);
    for (let i = texto.indexOf(alvo); i >= 0; i = texto.indexOf(alvo, i + alvo.length)) {
      const r = document.createRange();
      r.setStart(no, i);
      r.setEnd(no, i + alvo.length);
      achadas.push(r);
    }
  }
  return achadas;
}

function pintar(): void {
  const h = api();
  if (!h) throw new ErroComposicao('pesquisa_indisponivel', 'O navegador não tem a CSS Custom Highlight API.');
  const todas = new Highlight(...ocorrencias);
  todas.priority = 0;
  h.set(NOME_BUSCA, todas);
  const marcada = ocorrencias[indice];
  const atual = new Highlight(...(marcada ? [marcada] : []));
  atual.priority = 1;
  h.set(NOME_ATUAL, atual);
}

function rolarAteAtual(): void {
  const r = ocorrencias[indice];
  if (!r) return;
  const caixa = r.getBoundingClientRect(); // já com o zoom (transform) do <body>
  const alturaJanela = document.documentElement.clientHeight;
  window.scrollTo({ top: Math.max(0, window.scrollY + caixa.top - alturaJanela / 2 + caixa.height / 2), behavior: 'instant' });
}

function refDaAtual(): string | null {
  const no = ocorrencias[indice]?.startContainer;
  const el = no instanceof Element ? no : no?.parentElement;
  return el?.closest('[data-ref]')?.getAttribute('data-ref') ?? null;
}

function resposta(): Record<string, unknown> {
  return { total: ocorrencias.length, atual: ocorrencias.length === 0 ? 0 : indice + 1, ref: refDaAtual() };
}

export function registrarPesquisa(registrar: Registrar): void {
  registrar('pesquisar', (dados) => {
    const termo = dados.termo;
    const passo = dados.passo;
    if (typeof termo !== 'string') throw new ErroComposicao('pesquisa_invalida', '"termo" deve ser um texto.');
    const manter = dados.atual;
    if (passo !== -1 && passo !== 0 && passo !== 1) throw new ErroComposicao('pesquisa_invalida', '"passo" deve ser -1, 0 ou 1.');
    if (manter !== undefined && (typeof manter !== 'number' || !Number.isInteger(manter) || manter < 1 || passo !== 0)) {
      throw new ErroComposicao('pesquisa_invalida', '"atual" deve ser um inteiro a partir de 1 e só vale com passo 0.');
    }
    if (termo === '') {
      termoAtual = '';
      limparRealces();
      return { tipo: 'pesquisado', dados: resposta() };
    }
    garantirCss();
    const mudou = termo !== termoAtual;
    const anterior = indice;
    termoAtual = termo;
    ocorrencias = buscar(termo);
    const n = ocorrencias.length;
    if (n === 0) indice = 0;
    else if (manter !== undefined) indice = Math.min(manter - 1, n - 1);
    else if (passo === 0) indice = mudou ? 0 : Math.min(anterior, n - 1);
    else indice = mudou ? (passo === 1 ? 0 : n - 1) : (((anterior + passo) % n) + n) % n;
    pintar();
    if (manter === undefined && (passo !== 0 || mudou)) rolarAteAtual();
    return { tipo: 'pesquisado', dados: resposta() };
  });
}
