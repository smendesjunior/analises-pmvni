// Linhas das tabelas ÍNDICES URBANÍSTICOS (table.tb.ind) e ILUMINAÇÃO E VENTILAÇÃO (table.tb.ilu) editáveis como os itens.
// Como o data-campo dos itens, tudo aqui vale só no DOM da tela: o atributo da linha (data-pmvni-linha), o data-campo de cada
// texto e os elementos <pmvni-trecho> (inline) que separam dois textos impressos no mesmo elemento ("J3 • 2,00 × 1,70 m", "ÁREA: 3,40 m²") nunca existem
// no HTML exportado nem no fluxo de medida, e não mudam o texto nem a medida (elemento inline).
//
// Id da linha (o mesmo do host, Pmvni.Revisao/Linhas.cs): bloco e posição no original, "indices/02" ou "ambientes/03"; a
// posição é o número impresso na própria linha ("ÍNDICE 02", "AMBIENTE 03"). O campo é o caminho do texto no dados.json da
// linha: "calculo", "detalhes/0", "vaos/1/tipo", "minimo/0/texto", "textos/2", "resultado/total"…

import { ATR_OCULTO } from './fragmentar';

export const ATR_LINHA = 'data-pmvni-linha';
/** Elemento criado só para marcar parte de um texto; ao desmarcar, volta a ser texto corrido. */
export const ATR_ENVOLTO = 'data-pmvni-envolto';
const ATR_CAMPO = 'data-campo';
const SELETOR_LINHAS = `section.folha .area tr[${ATR_LINHA}]`;
const SELETOR_CARDS = 'section.folha .area .card[data-ref]';
const SEPARADOR_VAO = ' • ';
const PREFIXO_AREA = 'ÁREA: ';
/** Elemento que envolve parte de um texto: um nome próprio (inline, sem estilo do kit), porque o CSS do kit estiliza `.vao span`
 * como bloco e um span ali mudaria a medida. */
const TAG_TRECHO = 'pmvni-trecho';

const TABELAS: readonly [string, string][] = [['ind', 'indices'], ['ilu', 'ambientes']];

/** O id tem a forma de linha de tabela. */
export const ehLinha = (ref: unknown): ref is string => typeof ref === 'string' && /^(indices|ambientes)\/\d{2,}$/.test(ref);

/** Caminho de texto dentro da linha: chaves e índices separados por "/". */
export const ehCaminho = (v: unknown): v is string => typeof v === 'string' && /^[a-z_]+(\/([0-9]+|[a-z_]+))*$/.test(v);

const visivel = (e: Element): boolean => !e.closest(`[${ATR_OCULTO}]`);

/** Pedaços de um bloco nas folhas: os cards de um item (data-ref) ou as linhas de tabela visíveis (data-pmvni-linha). */
export function pedacosDoBloco(ref: string): HTMLElement[] {
  if (ehLinha(ref)) {
    return Array.from(document.querySelectorAll<HTMLElement>(SELETOR_LINHAS))
      .filter((e) => e.getAttribute(ATR_LINHA) === ref && visivel(e));
  }
  return Array.from(document.querySelectorAll<HTMLElement>(SELETOR_CARDS)).filter((e) => e.getAttribute('data-ref') === ref);
}

/** O bloco (card de item ou linha de tabela) sob um elemento, com o seu ref. */
export function blocoDe(alvo: Element): { el: HTMLElement; ref: string; linha: boolean } | null {
  const card = alvo.closest<HTMLElement>(SELETOR_CARDS);
  if (card) return { el: card, ref: card.getAttribute('data-ref')!, linha: false };
  const tr = alvo.closest<HTMLElement>(SELETOR_LINHAS);
  if (tr) return { el: tr, ref: tr.getAttribute(ATR_LINHA)!, linha: true };
  return null;
}

/** Dá a cada linha das duas tabelas, em todas as folhas, o seu id. Idempotente. O número vem do rótulo impresso ("ÍNDICE 02");
 * sem rótulo legível, a posição da linha no corpo da tabela. */
export function marcarLinhas(): void {
  for (const [classe, bloco] of TABELAS) {
    for (const t of Array.from(document.querySelectorAll<HTMLTableElement>(`section.folha .area table.tb.${classe}`))) {
      const linhas = Array.from(t.tBodies).flatMap((b) => Array.from(b.rows));
      linhas.forEach((tr, i) => {
        const rotulo = tr.querySelector(':scope > td.nm span.sm')?.textContent ?? '';
        const m = /(\d+)\s*$/.exec(rotulo);
        const n = m ? parseInt(m[1]!, 10) : i + 1;
        tr.setAttribute(ATR_LINHA, `${bloco}/${String(n).padStart(2, '0')}`);
      });
    }
  }
}

/** Marca `el` como o campo; devolve 1 se marcou. */
function marcar(el: Element | null | undefined, campo: string): number {
  if (!el) return 0;
  el.setAttribute(ATR_CAMPO, campo);
  return 1;
}

/** Põe um nó de texto inteiro num <pmvni-trecho> marcado como `campo` (texto vazio fica como está). */
function envolverTexto(no: Text, campo: string): number {
  if (no.data.length === 0) return 0;
  const span = document.createElement(TAG_TRECHO);
  span.setAttribute(ATR_ENVOLTO, '');
  span.setAttribute(ATR_CAMPO, campo);
  no.replaceWith(span);
  span.appendChild(no);
  return 1;
}

/** O único filho de `el`, se for texto. */
function textoUnico(el: Element): Text | null {
  return el.childNodes.length === 1 && el.firstChild!.nodeType === Node.TEXT_NODE ? (el.firstChild as Text) : null;
}

/** Já marcado antes (estado repetido): os trechos que estão lá. */
const jaMarcados = (el: Element): number => el.querySelectorAll(`:scope > [${ATR_ENVOLTO}]`).length;

function marcarIndice(tr: HTMLTableRowElement): void {
  const [nm, calculo, projeto, limite, situacao] = Array.from(tr.cells);
  marcar(nm?.querySelector(':scope .nmw > div > b'), 'nome');
  marcar(nm?.querySelector(':scope .nmw > div > small'), 'descricao');
  marcar(calculo, 'calculo');
  marcar(projeto, 'projeto');
  marcar(limite, 'limite');
  marcar(situacao?.querySelector(':scope > .st'), 'texto');
}

function marcarAmbiente(tr: HTMLTableRowElement): void {
  const [nm, vaos, minimo, situacao] = Array.from(tr.cells);
  marcar(nm?.querySelector(':scope .nmw > div > b'), 'nome');
  nm?.querySelectorAll(':scope .nmw > div > small').forEach((s, i) => marcar(s, `detalhes/${i}`));
  vaos?.querySelectorAll(':scope > .vao').forEach((v, k) => {
    const b = v.querySelector(':scope > b');
    if (b && jaMarcados(b) === 0) {
      // "código • medidas" sai num só <b>: cada parte vira um <pmvni-trecho> (sem medidas, o <b> inteiro é o código).
      const no = textoUnico(b);
      const sep = no ? no.data.indexOf(SEPARADOR_VAO) : -1;
      if (!no || sep < 0) marcar(b, `vaos/${k}/codigo`);
      else {
        const resto = no.splitText(sep);
        const medidas = resto.splitText(SEPARADOR_VAO.length);
        envolverTexto(no, `vaos/${k}/codigo`);
        envolverTexto(medidas, `vaos/${k}/medidas`);
      }
    }
    marcar(v.querySelector(':scope > span'), `vaos/${k}/tipo`);
    const em = v.querySelector(':scope > em');
    if (em?.classList.contains('o')) marcar(em, `vaos/${k}/sem_area`);
    else if (em && jaMarcados(em) === 0) {
      // "ÁREA: x": só o valor é o campo.
      const no = textoUnico(em);
      if (no?.data.startsWith(PREFIXO_AREA)) envolverTexto(no.splitText(PREFIXO_AREA.length), `vaos/${k}/area`);
    }
  });
  minimo?.querySelectorAll(':scope > div').forEach((d, i) => marcar(d, `minimo/${i}/texto`));
  if (situacao) {
    marcar(situacao.querySelector(':scope > .vres .vrt > b'), 'resultado/total');
    marcar(situacao.querySelector(':scope > .vres > .vrc'), 'resultado/conta');
    marcar(situacao.querySelector(':scope > .vres > .vrm'), 'resultado/comparacao');
    marcar(situacao.querySelector(':scope > .st.sb'), 'situacao_titulo');
    situacao.querySelectorAll(':scope > .st:not(.sb)').forEach((s, i) => marcar(s, `textos/${i}`));
  }
}

/** Marca os textos de todas as linhas das tabelas nas folhas (data-campo = caminho). Idempotente. */
export function marcarCamposLinhas(): void {
  marcarLinhas();
  for (const tr of Array.from(document.querySelectorAll<HTMLTableRowElement>(SELETOR_LINHAS))) {
    if (tr.closest('table')?.classList.contains('ind')) marcarIndice(tr);
    else marcarAmbiente(tr);
  }
}

/** Desfaz um <pmvni-trecho> criado por {@link marcarCamposLinhas}: o texto volta a ser corrido. */
export function desenvolver(el: Element): boolean {
  if (!el.hasAttribute(ATR_ENVOLTO)) return false;
  const pai = el.parentElement;
  el.replaceWith(...Array.from(el.childNodes));
  pai?.normalize();
  return true;
}
