// Fragmentação, espelhando o modelo do Chromium (LayoutNG) para a impressão do kit:
// - pontos de quebra entre irmãos em fluxo (classe A), entre linhas de tabela e entre linhas de texto (classe B);
// - cada ponto tem um "apelo": 3 perfeito; 2 viola orphans/widows; 1 viola break-*: avoid (o próprio, de um
//   ancestral com break-inside: avoid, ou o break-after/before propagado pela cadeia de último/primeiro filho);
// - a folha termina no ponto que cabe com o MAIOR apelo e, entre iguais, o mais tardio (folhas.ts);
// - tudo é lido do estilo computado, nunca de nomes de classe.
// O mesmo módulo recorta o fluxo entre duas posições (montagem das folhas no cálculo e, na Tarefa 8, na aplicação).

import { caixa, deslocamentoNaFronteira, EPS_PX, escalaVisual, estilo, linhasDe, localizar, num } from './medida';
import { descer, dentroDoPrimeiro, posicao, type PosicaoFluxo } from './plano';

export const PERFEITO = 3, VIOLA_ORFAS = 2, VIOLA_EVITAR = 1;

export const ATR_OCULTO = 'data-pmvni-oculto';   // irmão fora do intervalo, mantido oculto (seletores estruturais)
export const ATR_CONT = 'data-pmvni-cont';       // pedaço que continua de uma folha anterior
export const ATR_SEGUE = 'data-pmvni-segue';     // pedaço que continua na folha seguinte
export const ATR_REPETIDO = 'data-pmvni-repetido'; // thead repetido

export type Tipo = 'conteiner' | 'linhas' | 'tabela' | 'monolitico';

const SUBSTITUIDOS = new Set(['img', 'svg', 'video', 'audio', 'canvas', 'iframe', 'object', 'embed', 'input',
  'textarea', 'select', 'button', 'picture', 'math', 'hr', 'meter', 'progress']);
const FORCADAS = new Set(['page', 'left', 'right', 'recto', 'verso', 'always']);
const EVITAR = new Set(['avoid', 'avoid-page']);

const ehInline = (cs: CSSStyleDeclaration) => cs.display.startsWith('inline') || cs.display === 'contents';
const foraDoFluxo = (cs: CSSStyleDeclaration) =>
  cs.display === 'none' || cs.position === 'absolute' || cs.position === 'fixed' || cs.float !== 'none';

/** Filhos-elemento que participam do fluxo em bloco, com o índice em `children`. */
export function filhosEmFluxo(el: Element): { el: Element; i: number }[] {
  const lista: { el: Element; i: number }[] = [];
  Array.from(el.children).forEach((f, i) => {
    const cs = estilo(f);
    if (!foraDoFluxo(cs) && cs.display !== 'contents') lista.push({ el: f, i });
  });
  return lista;
}

/** Como o bloco pode ser dividido, pelo estilo computado. Misto (blocos e texto soltos) é tratado como indivisível. */
export function classificar(el: Element): Tipo {
  if (SUBSTITUIDOS.has(el.localName.toLowerCase())) return 'monolitico';
  const cs = estilo(el);
  if (cs.display === 'table') return 'tabela';
  if (ehInline(cs)) return 'monolitico';
  if (cs.overflowY === 'auto' || cs.overflowY === 'scroll') return 'monolitico';
  if (/^(flex|grid)$/.test(cs.display)) return 'conteiner';
  if (!['block', 'flow-root', 'list-item'].includes(cs.display)) return 'monolitico';
  let texto = false, inline = false, bloco = false;
  for (const n of Array.from(el.childNodes)) {
    if (n.nodeType === Node.TEXT_NODE) { if (/\S/.test((n as Text).data)) texto = true; continue; }
    if (n.nodeType !== Node.ELEMENT_NODE) continue;
    const f = n as Element;
    const c = estilo(f);
    if (c.display === 'none' || c.position === 'absolute' || c.position === 'fixed') continue;
    if (f.localName === 'br' || ehInline(c)) inline = true; else bloco = true;
  }
  if (bloco && !texto && !inline) return 'conteiner';
  if ((texto || inline) && !bloco) return 'linhas';
  return 'monolitico';
}

const evitaDentro = (el: Element) => EVITAR.has(estilo(el).breakInside);

/** break-after (ou before) do elemento e da cadeia de último (ou primeiro) filho, que o Chromium propaga ao pai. */
function cadeia(el: Element, depois: boolean): string[] {
  const valores: string[] = [];
  for (let e: Element | undefined = el, n = 0; e && n < 32; n++) {
    const cs = estilo(e);
    valores.push(depois ? cs.breakAfter : cs.breakBefore);
    if (cs.display === 'table' || classificar(e) !== 'conteiner') break;
    const filhos = filhosEmFluxo(e);
    e = (depois ? filhos[filhos.length - 1] : filhos[0])?.el;
  }
  return valores;
}

export interface Ponto {
  indice: number;
  /** Início do conteúdo depois da quebra; null = fim do documento. Para linhas de texto, resolvido sob demanda. */
  pos: PosicaoFluxo | null;
  apelo: number;
  forcado: boolean;
  /** Fim (base da caixa de borda) do conteúdo antes da quebra, em coordenadas do fluxo de medida. */
  yFim: number;
  /** Início (topo da caixa de borda; margem truncada, salvo quebra forçada) do conteúdo depois da quebra. */
  yInicio: number;
  /** Borda+preenchimento de baixo dos ancestrais abertos com box-decoration-break: clone. */
  decoFim: number;
  /** Borda+preenchimento de cima dos ancestrais abertos com clone, mais o thead repetido. */
  decoInicio: number;
  texto?: { el: Element; caminho: number[]; fronteira: number };
}

interface Aberto { topo: number; base: number }

function aberto(el: Element): Aberto {
  const cs = estilo(el);
  const quebra = cs.getPropertyValue('box-decoration-break') || cs.getPropertyValue('-webkit-box-decoration-break');
  const escala = escalaVisual(el);
  const c = caixa(cs, escala);
  let topo = quebra === 'clone' ? c.bt + c.pt : 0;
  const base = quebra === 'clone' ? c.bb + c.pb : 0;
  if (cs.display === 'table') {
    const t = el as HTMLTableElement;
    if (t.tHead && estilo(t.tHead).display === 'table-header-group') {
      topo += t.tHead.getBoundingClientRect().height + num(cs.borderSpacing.split(' ').pop()) * escala;
    }
  }
  return { topo, base };
}

const soma = (a: Aberto[], k: keyof Aberto) => a.reduce((s, x) => s + x[k], 0);

/** Todos os pontos de quebra do fluxo, na ordem do documento, terminando no ponto "fim". */
export function enumerarPontos(fluxo: HTMLElement): Ponto[] {
  const pontos: Ponto[] = [];
  const add = (p: Omit<Ponto, 'indice'>) => pontos.push({ ...p, indice: pontos.length });

  const percorrer = (el: Element, caminho: number[], abertos: Aberto[], evitar: boolean) => {
    const tipo = classificar(el);
    if (tipo === 'conteiner') conteiner(el, caminho, abertos, evitar);
    else if (tipo === 'tabela') tabela(el as HTMLTableElement, caminho, abertos, evitar);
    else if (tipo === 'linhas') linhas(el, caminho, abertos, evitar);
  };

  const conteiner = (el: Element, caminho: number[], abertos: Aberto[], evitar: boolean) => {
    const filhos = filhosEmFluxo(el);
    const q = filhos.map((f) => f.el.getBoundingClientRect());
    // Só há ponto entre j e j+1 se tudo antes termina acima de tudo depois (filhos lado a lado — flex em linha,
    // grade na mesma linha — não se separam).
    const fimAte: number[] = [], inicioDesde: number[] = new Array(filhos.length);
    q.forEach((r, j) => fimAte.push(Math.max(r.bottom, j ? fimAte[j - 1]! : -Infinity)));
    for (let j = filhos.length - 1; j >= 0; j--) inicioDesde[j] = Math.min(q[j]!.top, j < filhos.length - 1 ? inicioDesde[j + 1]! : Infinity);
    // Itens de flex em linha e de grade não se dividem por dentro (o Chromium, medido na impressão do kit, não quebra
    // dentro do item flex do .sec); só entre linhas de itens. Flex em coluna é empilhado como um bloco comum.
    const cs = estilo(el);
    const porDentro = !(cs.display === 'grid' || (cs.display === 'flex' && !cs.flexDirection.startsWith('column')));
    filhos.forEach((f, j) => {
      if (porDentro) percorrer(f.el, [...caminho, f.i], [...abertos, aberto(f.el)], evitar || evitaDentro(f.el));
      const prox = filhos[j + 1];
      if (!prox || fimAte[j]! > inicioDesde[j + 1]! + EPS_PX) return;
      const depois = cadeia(f.el, true), antes = cadeia(prox.el, false);
      const forcado = depois.some((v) => FORCADAS.has(v)) || antes.some((v) => FORCADAS.has(v));
      const apelo = evitar || depois.some((v) => EVITAR.has(v)) || antes.some((v) => EVITAR.has(v)) ? VIOLA_EVITAR : PERFEITO;
      add({
        pos: posicao([...caminho, prox.i]), apelo, forcado,
        yFim: fimAte[j]!,
        yInicio: inicioDesde[j + 1]! - (forcado ? num(estilo(prox.el).marginTop) * escalaVisual(prox.el) : 0),
        decoFim: soma(abertos, 'base'), decoInicio: soma(abertos, 'topo'),
      });
    });
  };

  const tabela = (t: HTMLTableElement, caminho: number[], abertos: Aberto[], evitar: boolean) => {
    const linhasCorpo = linhasDoCorpo(t);
    // Uma célula com rowspan que atravessa a fronteira impede a quebra ali.
    const alcance = linhasCorpo.map((tr, r) => Math.max(r, ...Array.from(tr.cells).map((c) => r + Math.max(1, c.rowSpan) - 1)));
    let maxAlcance = -1;
    for (let r = 1; r < linhasCorpo.length; r++) {
      maxAlcance = Math.max(maxAlcance, alcance[r - 1]!);
      if (maxAlcance >= r) continue;
      const anterior = linhasCorpo[r - 1]!, atual = linhasCorpo[r]!;
      const ca = estilo(anterior), cb = estilo(atual);
      if (ca.display === 'none' || cb.display === 'none') continue;
      const forcado = FORCADAS.has(ca.breakAfter) || FORCADAS.has(cb.breakBefore);
      const apelo = evitar || EVITAR.has(ca.breakAfter) || EVITAR.has(cb.breakBefore) ? VIOLA_EVITAR : PERFEITO;
      add({
        pos: posicao(caminho, null, r), apelo, forcado,
        yFim: anterior.getBoundingClientRect().bottom, yInicio: atual.getBoundingClientRect().top,
        decoFim: soma(abertos, 'base'), decoInicio: soma(abertos, 'topo'),
      });
    }
  };

  const linhas = (el: Element, caminho: number[], abertos: Aberto[], evitar: boolean) => {
    const { n, fronteiras } = linhasDe(el);
    if (n < 2) return;
    const cs = estilo(el);
    const orfas = Math.max(1, Math.round(num(cs.orphans)) || 2), viuvas = Math.max(1, Math.round(num(cs.widows)) || 2);
    for (let i = 1; i < n; i++) {
      let apelo = i < orfas || n - i < viuvas ? VIOLA_ORFAS : PERFEITO;
      if (evitar) apelo = VIOLA_EVITAR;
      const y = fronteiras[i - 1]!;
      add({
        pos: null, texto: { el, caminho, fronteira: y }, apelo, forcado: false,
        yFim: y, yInicio: y, decoFim: soma(abertos, 'base'), decoInicio: soma(abertos, 'topo'),
      });
    }
  };

  conteiner(fluxo, [], [], false);
  const filhos = filhosEmFluxo(fluxo);
  const topo = fluxo.getBoundingClientRect().top;
  const fim = filhos.reduce((m, f) => Math.max(m, f.el.getBoundingClientRect().bottom), topo);
  add({ pos: null, apelo: PERFEITO, forcado: false, yFim: fim, yInicio: fim, decoFim: 0, decoInicio: 0 });
  return pontos;
}

/** Resolve a posição de um ponto (as de linha de texto são calculadas só quando o ponto é escolhido). */
export function resolver(p: Ponto): PosicaoFluxo | null | undefined {
  if (!p.texto) return p.pos;
  if (p.pos) return p.pos;
  const d = deslocamentoNaFronteira(p.texto.el, p.texto.fronteira);
  if (d === null) return undefined; // sem divisão útil: ponto descartado
  p.pos = posicao(p.texto.caminho, d);
  return p.pos;
}

/** Linhas dos tbody da tabela, na ordem (a numeração de PosicaoFluxo.linha). */
export function linhasDoCorpo(t: HTMLTableElement): HTMLTableRowElement[] {
  const lista: HTMLTableRowElement[] = [];
  for (const sec of Array.from(t.children)) {
    if (sec.localName !== 'tbody') continue;
    for (const tr of Array.from(sec.children)) if (tr.localName === 'tr') lista.push(tr as HTMLTableRowElement);
  }
  return lista;
}

/** Elemento do fluxo no caminho (ou null). */
export function elementoEm(fluxo: Element, caminho: number[]): Element | null {
  let e: Element | null = fluxo;
  for (const i of caminho) { e = e?.children[i] ?? null; if (!e) return null; }
  return e;
}

// ---------- recorte ----------

/** Irmão fora do intervalo: cópia rasa e oculta, para que :first-child, :last-child, :nth-child e + continuem valendo. */
function marcador(el: Element): Element {
  const m = el.cloneNode(false) as Element;
  for (const a of ['data-ref', 'src', 'srcset', 'id']) m.removeAttribute(a);
  m.setAttribute(ATR_OCULTO, '');
  (m as HTMLElement).style?.setProperty('display', 'none', 'important');
  return m;
}

/**
 * Copia para `destino` os filhos de `origem` entre `de` (inclusive) e `ate` (exclusive), relativos a `origem`
 * (null = do começo / até o fim). Irmãos fora do intervalo viram marcadores ocultos.
 */
export function copiarIntervalo(origem: Element, destino: Element, de: PosicaoFluxo | null, ate: PosicaoFluxo | null): void {
  const filhos = Array.from(origem.children);
  const iDe = de ? de.caminho[0]! : 0;
  const deDentro = de !== null && dentroDoPrimeiro(de);
  const iAte = ate ? ate.caminho[0]! : filhos.length;
  const ateDentro = ate !== null && dentroDoPrimeiro(ate);
  filhos.forEach((f, i) => {
    if (i < iDe || i > iAte || (i === iAte && !ateDentro)) { destino.appendChild(marcador(f)); return; }
    const parteDe = i === iDe && deDentro ? descer(de!) : null;
    const parteAte = i === iAte && ateDentro ? descer(ate!) : null;
    destino.appendChild(parteDe || parteAte ? pedaco(f, parteDe, parteAte) : f.cloneNode(true));
  });
}

/** Pedaço de um elemento: casca rasa + o conteúdo entre `de` e `ate` (relativos ao próprio elemento). */
function pedaco(el: Element, de: PosicaoFluxo | null, ate: PosicaoFluxo | null): Element {
  const folhaDe = de && de.caminho.length === 0 ? de : null;
  const folhaAte = ate && ate.caminho.length === 0 ? ate : null;
  let casca: Element;
  if (folhaDe?.deslocamentoTexto != null || folhaAte?.deslocamentoTexto != null) {
    casca = pedacoTexto(el, folhaDe?.deslocamentoTexto ?? null, folhaAte?.deslocamentoTexto ?? null);
  } else if (folhaDe?.linha != null || folhaAte?.linha != null) {
    casca = pedacoTabela(el as HTMLTableElement, folhaDe?.linha ?? null, folhaAte?.linha ?? null);
  } else {
    casca = el.cloneNode(false) as Element;
    copiarIntervalo(el, casca, de, ate);
  }
  if (de) casca.setAttribute(ATR_CONT, '');
  if (ate) casca.setAttribute(ATR_SEGUE, '');
  return casca;
}

/** Fronteira do Range no caractere `desloc`; no início de um inline, antes do inline mais externo que começa ali
 * (evita uma cópia vazia do inline no fim do pedaço anterior). */
function fronteira(r: Range, el: Element, desloc: number, inicio: boolean): void {
  const l = localizar(el, desloc);
  if (!l) { if (inicio) r.setStart(el, el.childNodes.length); else r.setEnd(el, el.childNodes.length); return; }
  if (l.local === 0) {
    let alvo: Node = l.no;
    while (alvo.parentNode && alvo.parentNode !== el && alvo.previousSibling === null) alvo = alvo.parentNode;
    if (inicio) r.setStartBefore(alvo); else r.setEndBefore(alvo);
  } else if (inicio) r.setStart(l.no, l.local);
  else r.setEnd(l.no, l.local);
}

function pedacoTexto(el: Element, de: number | null, ate: number | null): Element {
  const casca = el.cloneNode(false) as Element;
  const r = document.createRange();
  r.selectNodeContents(el);
  if (de !== null) fronteira(r, el, de, true);
  if (ate !== null) fronteira(r, el, ate, false);
  casca.appendChild(r.cloneContents());
  return casca;
}

function pedacoTabela(t: HTMLTableElement, de: number | null, ate: number | null): Element {
  const casca = t.cloneNode(false) as Element;
  let g = 0;
  for (const sec of Array.from(t.children)) {
    switch (sec.localName) {
      case 'tbody': {
        const corpo = sec.cloneNode(false) as Element;
        for (const tr of Array.from(sec.children)) {
          if (tr.localName !== 'tr') { corpo.appendChild(tr.cloneNode(true)); continue; }
          const dentro = (de === null || g >= de) && (ate === null || g < ate);
          corpo.appendChild(dentro ? tr.cloneNode(true) : marcador(tr));
          g++;
        }
        casca.appendChild(corpo);
        break;
      }
      case 'thead': {
        const repete = estilo(sec).display === 'table-header-group';
        if (de === null) casca.appendChild(sec.cloneNode(true));
        else if (repete) { const c = sec.cloneNode(true) as Element; c.setAttribute(ATR_REPETIDO, ''); casca.appendChild(c); }
        else casca.appendChild(marcador(sec));
        break;
      }
      case 'tfoot':
        casca.appendChild(ate === null ? sec.cloneNode(true) : marcador(sec));
        break;
      case 'caption':
        casca.appendChild(de === null ? sec.cloneNode(true) : marcador(sec));
        break;
      default:
        casca.appendChild(sec.cloneNode(true)); // colgroup/col
    }
  }
  return casca;
}

// ---------- acabamento da folha ----------

const visivel = (e: Element) => !e.hasAttribute(ATR_OCULTO);

function primeiroBlocoVisivel(el: Element): HTMLElement | null {
  for (const f of Array.from(el.children)) {
    if (!visivel(f)) continue;
    const cs = estilo(f);
    if (foraDoFluxo(cs) || cs.display === 'contents') continue;
    return ehInline(cs) ? null : (f as HTMLElement);
  }
  return null;
}

function ultimoBlocoVisivel(el: Element): HTMLElement | null {
  const filhos = Array.from(el.children);
  for (let i = filhos.length - 1; i >= 0; i--) {
    const f = filhos[i]!;
    if (!visivel(f)) continue;
    const cs = estilo(f);
    if (foraDoFluxo(cs) || cs.display === 'contents') continue;
    return ehInline(cs) ? null : (f as HTMLElement);
  }
  return null;
}

const zerar = (el: Element, props: string[]) => props.forEach((p) => (el as HTMLElement).style.setProperty(p, '0px', 'important'));

/**
 * Estilos de quebra nos pedaços da área, antes de medir: com slice, o 1º pedaço perde borda/cantos/preenchimento de
 * baixo e o seguinte os de cima (com clone ficam); margens junto da quebra são truncadas (a de baixo do pedaço que
 * segue e do seu último filho; a de cima do pedaço que continua). Se `truncarTopo`, a margem de cima do primeiro
 * bloco da folha (e da cadeia de primeiros filhos cuja margem colapsa com ela) é zerada, como numa quebra não forçada.
 */
export function acabamento(area: HTMLElement, truncarTopo: boolean): void {
  for (const p of Array.from(area.querySelectorAll(`[${ATR_CONT}],[${ATR_SEGUE}]`))) {
    const cs = estilo(p);
    const clone = (cs.getPropertyValue('box-decoration-break') || cs.getPropertyValue('-webkit-box-decoration-break')) === 'clone';
    if (p.hasAttribute(ATR_SEGUE)) {
      zerar(p, ['margin-bottom']);
      if (!clone) zerar(p, ['border-bottom-width', 'padding-bottom', 'border-bottom-left-radius', 'border-bottom-right-radius']);
      if (cs.display !== 'table') { const u = ultimoBlocoVisivel(p); if (u) zerar(u, ['margin-bottom']); }
    }
    if (p.hasAttribute(ATR_CONT)) {
      zerar(p, ['margin-top']);
      if (!clone) zerar(p, ['border-top-width', 'padding-top', 'border-top-left-radius', 'border-top-right-radius']);
    }
  }
  if (!truncarTopo) return;
  for (let el = primeiroBlocoVisivel(area), n = 0; el && n < 64; n++) {
    zerar(el, ['margin-top']);
    const cs = estilo(el);
    if (cs.display === 'table') break;
    const c = caixa(cs);
    const colapsa = (cs.display === 'block' || cs.display === 'list-item') && cs.overflowY === 'visible' && c.bt === 0 && c.pt === 0;
    if (!el.hasAttribute(ATR_CONT) && !colapsa) break;
    el = primeiroBlocoVisivel(el);
  }
}

/**
 * Base da área útil na tela: o topo do rodapé da folha (folha.css põe os dois em 282 mm). A caixa da área não serve
 * quando k > 1: diagramada com 254 mm e pintada em 1/k, ela termina antes (ver folha.css). Sem rodapé, a base da área.
 */
export function fimDaArea(area: HTMLElement): number {
  const rodape = area.parentElement?.querySelector(':scope > .folha-rodape');
  return (rodape ?? area).getBoundingClientRect()[rodape ? 'top' : 'bottom'];
}

/** Altura útil da área na tela (do topo da área ao topo do rodapé). */
export const alturaUtil = (area: HTMLElement): number => fimDaArea(area) - area.getBoundingClientRect().top;

/** Altura do conteúdo da área (da borda de cima da área à base da caixa de borda mais baixa dos blocos em fluxo). */
export function alturaConteudo(area: HTMLElement): number {
  const topo = area.getBoundingClientRect().top;
  let fim = topo;
  for (const f of Array.from(area.children)) {
    if (!visivel(f) || foraDoFluxo(estilo(f))) continue;
    fim = Math.max(fim, f.getBoundingClientRect().bottom);
  }
  return fim - topo;
}

/**
 * Como no Chromium, a caixa que quebra ocupa o resto da folha: cada pedaço que segue (exceto tabelas e inline) ganha
 * min-height até o fim da área, descontada a borda+preenchimento de baixo dos pedaços que o contêm. Grade/flex recebem
 * align-content: start para que as trilhas não estiquem. Chamar depois da verificação da altura.
 */
export function estenderPedacos(area: HTMLElement): void {
  const fimArea = fimDaArea(area);
  for (const p of Array.from(area.querySelectorAll<HTMLElement>(`[${ATR_SEGUE}]`))) {
    const cs = estilo(p);
    if (cs.display === 'table' || ehInline(cs)) continue;
    let desconto = 0;
    for (let a = p.parentElement; a && a !== area; a = a.parentElement) {
      if (a.hasAttribute(ATR_SEGUE)) { const c = caixa(estilo(a), escalaVisual(a)); desconto += c.bb + c.pb; }
    }
    const q = p.getBoundingClientRect();
    const alvo = fimArea - desconto - q.top; // altura da caixa de borda
    if (alvo <= q.height) continue;
    const c = caixa(cs); // px de layout
    const alvoLayout = alvo / escalaVisual(p);
    const altura = cs.boxSizing === 'border-box' ? alvoLayout : alvoLayout - (c.bt + c.pt + c.pb + c.bb);
    if (/grid|flex/.test(cs.display) && cs.alignContent === 'normal') p.style.setProperty('align-content', 'start', 'important');
    p.style.setProperty('min-height', altura + 'px', 'important');
  }
}
