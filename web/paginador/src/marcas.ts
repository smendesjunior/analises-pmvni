// Marcas de revisão (Tarefa 7, Etapa 2): traço azul de 3 px à esquerda de cada pedaço de item alterado; onde um item
// excluído estava, a etiqueta vermelha "ITEM EXCLUÍDO NN" na margem cinza e uma linha vermelha atravessando a folha.
// Tudo mora na camada #pmvni-camada, filha do <body> e irmã das folhas: nenhuma marca entra em section.folha, nada muda
// a paginação e nada vai ao PDF (a camada some na mídia de impressão). As posições são do documento sem zoom (o <body>
// escala junto com as folhas); a etiqueta e a espessura da linha têm o tamanho constante na tela (contra-escala por
// --pmvni-zoom).

import { pedacosDoBloco } from './linhas';
import { zoomAtual } from './tela';

export const ID_CAMADA = 'pmvni-camada';
export const CLASSE_MARCA = 'pmvni-marca';
const CLASSE_TRACO = 'pmvni-marca-traco';
const CLASSE_EXCLUIDO = 'pmvni-marca-excl';
const CLASSE_LINHA = 'pmvni-marca-linha';
const TRACO_LARGURA_PX = 3;
/** Distância (px de tela) do traço à borda esquerda da folha: o traço fica na margem cinza, longe da faixa vermelha. */
const TRACO_AFASTAMENTO_PX = 12;
/** Folga (px de tela) entre o botão e a borda esquerda da folha. */
const BOTAO_FOLGA_PX = 6;
const BOTAO_ALTURA_PX = 18;
/** Espessura (px de tela) da linha vermelha que atravessa a folha onde o item excluído estava. */
const LINHA_ESPESSURA_PX = 2;
/** Folga (px de tela) entre a etiqueta do excluído e a borda esquerda da janela. */
const BOTAO_FOLGA_JANELA_PX = 6;

export interface Excluido { id: string; rotulo: string; ancora: string; lado: 'antes' | 'depois' }
export interface EstadoMarcas { visiveis: boolean; alterados: string[]; excluidos: Excluido[] }

/** CSS da camada: só na tela; na impressão a camada inteira some. Os valores de tela usam --pmvni-zoom (da tela.ts). */
export const CSS_MARCAS = `
@media screen {
  #${ID_CAMADA} { position: absolute; left: 0; top: 0; width: 0; height: 0; overflow: visible; z-index: 5; pointer-events: none; }
  #${ID_CAMADA} .${CLASSE_TRACO} { position: absolute; width: calc(${TRACO_LARGURA_PX}px / var(--pmvni-zoom, 1)); background: #1a73e8; border-radius: 2px; pointer-events: none; }
  #${ID_CAMADA} .${CLASSE_EXCLUIDO} {
    position: absolute; margin: 0; padding: 0 7px; box-sizing: border-box; height: ${BOTAO_ALTURA_PX}px; white-space: nowrap;
    font: 600 11px/16px "Segoe UI", system-ui, sans-serif; letter-spacing: .2px; color: #fff; background: #c42b1c; border: 1px solid #c42b1c; border-radius: 9px;
    cursor: pointer; pointer-events: auto; transform-origin: 0 0; transform: scale(calc(1 / var(--pmvni-zoom, 1))) translateX(-100%);
  }
  #${ID_CAMADA} .${CLASSE_EXCLUIDO}:hover { background: #a32418; border-color: #a32418; }
  #${ID_CAMADA} .${CLASSE_LINHA} { position: absolute; height: calc(${LINHA_ESPESSURA_PX}px / var(--pmvni-zoom, 1)); background: #c42b1c; pointer-events: none; }
}
@media screen and (prefers-color-scheme: dark) {
  #${ID_CAMADA} .${CLASSE_TRACO} { background: #6ea8fe; }
  #${ID_CAMADA} .${CLASSE_EXCLUIDO}:hover { background: #d9483a; border-color: #d9483a; }
}
@media print { #${ID_CAMADA} { display: none !important; } }
`;

/** Pedaços de um item alterado (cards) ou de uma linha de tabela alterada (linhas.ts). */
const pedacosDe = (ref: string): HTMLElement[] => pedacosDoBloco(ref);

/** Remove a camada (e com ela todas as marcas). */
export function limparMarcas(): void {
  document.getElementById(ID_CAMADA)?.remove();
}

function camada(): HTMLElement {
  let c = document.getElementById(ID_CAMADA);
  if (c && c.parentElement !== document.body) { c.remove(); c = null; }
  if (!c) {
    c = document.createElement('div');
    c.id = ID_CAMADA;
    document.body.appendChild(c);
  }
  return c;
}

/**
 * Vão (px de tela) que a margem cinza precisa ter à esquerda das folhas para a etiqueta de excluído mais larga caber
 * inteira; 0 sem etiquetas. A largura de layout da etiqueta já é a de tela (a contra-escala é um transform).
 */
export function golaDosExcluidos(): number {
  const botoes = document.querySelectorAll<HTMLElement>(`#${ID_CAMADA} .${CLASSE_EXCLUIDO}`);
  let maior = 0;
  botoes.forEach((b) => { maior = Math.max(maior, b.offsetWidth); });
  return maior === 0 ? 0 : Math.ceil(maior + BOTAO_FOLGA_PX + BOTAO_FOLGA_JANELA_PX);
}

/**
 * Redesenha as marcas e devolve quantas foram desenhadas: uma por item alterado achado nas folhas (com um traço por
 * pedaço dele) e uma por excluído cuja âncora foi achada (a etiqueta; a linha vermelha dele não conta à parte).
 * `aoClicarExcluido` recebe o id e o ponto do clique. Sem `visiveis`, remove a camada e devolve 0.
 */
export function desenharMarcas(m: EstadoMarcas, aoClicarExcluido: (id: string, x: number, y: number) => void): number {
  limparMarcas();
  if (!m.visiveis) return 0;
  const c = camada();
  const origem = c.getBoundingClientRect(); // a camada tem tamanho zero em left/top 0: dá a origem das coordenadas locais
  const z = zoomAtual();
  const localX = (tela: number) => (tela - origem.left) / z;
  const localY = (tela: number) => (tela - origem.top) / z;
  let desenhadas = 0;

  for (const ref of m.alterados) {
    const pedacos = pedacosDe(ref);
    if (pedacos.length === 0) continue;
    desenhadas++;
    for (const p of pedacos) {
      const r = p.getBoundingClientRect();
      const folha = p.closest('section.folha')!.getBoundingClientRect();
      const traco = document.createElement('div');
      traco.className = `${CLASSE_MARCA} ${CLASSE_TRACO}`;
      traco.style.left = `calc(${localX(folha.left)}px - ${TRACO_AFASTAMENTO_PX}px / var(--pmvni-zoom, 1))`;
      traco.style.top = `${localY(r.top)}px`;
      traco.style.height = `${r.height / z}px`;
      c.appendChild(traco);
    }
  }

  // Excluídos: etiqueta na altura do topo do item âncora ("antes") ou da base do item anterior ("depois"); duas na mesma
  // altura se empilham em vez de se cobrir. A linha vermelha fica sempre na altura real (uma só por altura e folha).
  const posicoes = m.excluidos.flatMap((e) => {
    const pedacos = pedacosDe(e.ancora);
    if (pedacos.length === 0) return [];
    const p = e.lado === 'antes' ? pedacos[0]! : pedacos[pedacos.length - 1]!;
    const r = p.getBoundingClientRect();
    const folha = p.closest('section.folha')!.getBoundingClientRect();
    return [{ e, x: localX(folha.left), largura: folha.width / z, y: localY(e.lado === 'antes' ? r.top : r.bottom) }];
  }).sort((a, b) => a.y - b.y);
  let ultimo = -Infinity;
  const linhas = new Set<string>();
  for (const { e, x, largura, y } of posicoes) {
    const chave = `${x.toFixed(1)}|${y.toFixed(1)}`;
    if (!linhas.has(chave)) {
      linhas.add(chave);
      const linha = document.createElement('div');
      linha.className = `${CLASSE_MARCA} ${CLASSE_LINHA}`;
      linha.style.left = `${x}px`;
      linha.style.width = `${largura}px`;
      linha.style.top = `calc(${y}px - ${LINHA_ESPESSURA_PX / 2}px / var(--pmvni-zoom, 1))`;
      c.appendChild(linha);
    }
    const topo = Math.max(y, ultimo + (BOTAO_ALTURA_PX + 2) / z); // centros a 20 px de tela, no mínimo
    ultimo = topo;
    const b = document.createElement('button');
    b.type = 'button';
    b.className = `${CLASSE_MARCA} ${CLASSE_EXCLUIDO}`;
    b.textContent = `ITEM EXCLUÍDO ${e.rotulo}`;
    b.title = `Item ${e.rotulo} excluído: clique para ver`;
    b.style.left = `calc(${x}px - ${BOTAO_FOLGA_PX}px / var(--pmvni-zoom, 1))`;
    b.style.top = `calc(${topo}px - ${BOTAO_ALTURA_PX / 2}px / var(--pmvni-zoom, 1))`;
    b.addEventListener('click', (ev) => { ev.stopPropagation(); aoClicarExcluido(e.id, ev.clientX, ev.clientY); });
    c.appendChild(b);
    desenhadas++;
  }
  return desenhadas;
}
