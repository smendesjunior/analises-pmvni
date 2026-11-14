// Medidas em px CSS a 96 dpi (1 mm = 96/25,4 px). O Chromium posiciona em LayoutUnit (1/64 px): somas de alturas
// medidas podem diferir do layout real por frações de 1/64 px. Por isso "cabe" usa uma tolerância EPS_PX de 0,05 px
// (3,2 LayoutUnits, 0,013 mm): grande o bastante para absorver o arredondamento, pequena demais para esconder uma linha.

export const PX_POR_MM = 96 / 25.4;
export const EPS_PX = 0.05;

export const pxParaMm = (px: number): number => px / PX_POR_MM;
export const num = (v: string | null | undefined): number => {
  const n = parseFloat(v ?? '');
  return Number.isFinite(n) ? n : 0;
};
export const estilo = (el: Element): CSSStyleDeclaration => getComputedStyle(el);

/**
 * px de tela por px de layout no elemento (1/k quando a área está em escala de impressão k; ver folha.css). Os
 * comprimentos do estilo computado são de layout; os retângulos (getBoundingClientRect) são de tela.
 */
export function escalaVisual(el: Element): number {
  const h = el as HTMLElement;
  const w = h.offsetWidth;
  return w > 0 ? el.getBoundingClientRect().width / w : 1;
}

/** Bordas e preenchimentos do bloco, em px de tela (multiplicados por `escala`). */
export function caixa(cs: CSSStyleDeclaration, escala = 1) {
  return {
    bt: num(cs.borderTopWidth) * escala, bb: num(cs.borderBottomWidth) * escala,
    pt: num(cs.paddingTop) * escala, pb: num(cs.paddingBottom) * escala,
  };
}

/** Razão do Chromium entre o encolhimento máximo e o mínimo da impressão (kPrintingMaximumShrinkFactor 2 /
 * kPrintingMinimumShrinkFactor 1,333): além disso o conteúdo é cortado, não reduzido. */
export const ENCOLHIMENTO_MAXIMO = 1.5;

/**
 * Escala de impressão k do Chromium (shrink-to-fit): com o fluxo diagramado na largura da área (k = 1), a largura do
 * documento é a borda direita mais distante do transbordamento rolável (caixas de borda dos elementos, sem descer em
 * quem corta o transbordamento; scrollWidth como reserva para texto solto). Maior que a área → k = largura / área,
 * limitado a ENCOLHIMENTO_MAXIMO.
 */
export function escalaDeImpressao(fluxo: HTMLElement): number {
  const q = fluxo.getBoundingClientRect();
  let direita = q.right;
  const visitar = (el: Element) => {
    for (const f of Array.from(el.children)) {
      const cs = estilo(f);
      if (cs.display === 'none') continue;
      const r = f.getBoundingClientRect();
      if (r.width > 0 || r.height > 0) direita = Math.max(direita, r.right);
      if (cs.overflowX === 'visible') visitar(f);
    }
  };
  visitar(fluxo);
  direita = Math.max(direita, q.left + fluxo.scrollWidth - 1);
  const largura = direita - q.left;
  if (largura <= q.width + EPS_PX) return 1;
  return Math.min(largura / q.width, ENCOLHIMENTO_MAXIMO);
}

// ---------- estabilidade ----------

function quadro(): Promise<void> {
  // requestAnimationFrame pode não disparar num WebView sem janela visível: o setTimeout garante o andamento.
  return new Promise((ok) => {
    let feito = false;
    const fim = () => { if (!feito) { feito = true; ok(); } };
    requestAnimationFrame(fim);
    setTimeout(fim, 100);
  });
}

function comLimite<T>(p: Promise<T>, ms: number): Promise<T | undefined> {
  return Promise.race([p, new Promise<undefined>((ok) => setTimeout(() => ok(undefined), ms))]);
}

/**
 * Espera o documento ficar estável antes de medir: document.fonts.ready, decode() de todas as imagens e dois quadros
 * seguidos sem mudança de altura de `raiz`. Repete enquanto algo mudar (no máximo ~30 s).
 */
export async function esperarEstabilidade(raiz: HTMLElement): Promise<void> {
  const limite = performance.now() + 30000;
  for (;;) {
    await comLimite(document.fonts.ready, 10000);
    const imagens = Array.from(document.images);
    await comLimite(Promise.all(imagens.map((i) => i.decode().catch(() => undefined))), 15000);
    const antes = raiz.getBoundingClientRect().height;
    await quadro();
    await quadro();
    const depois = raiz.getBoundingClientRect().height;
    const carregando = imagens.some((i) => !i.complete) || Array.from(document.images).length !== imagens.length
      || document.fonts.status !== 'loaded';
    if (antes === depois && !carregando) return;
    if (performance.now() > limite) return;
  }
}

// ---------- linhas de texto ----------

/** Inline atômico (inline-block, imagem…): mede-se pelo retângulo dele, sem descer. */
const atomico = (cs: CSSStyleDeclaration): boolean => cs.display !== 'inline' && cs.display !== 'contents';

/** Linhas de um elemento com contexto de formatação inline: n linhas e as n-1 fronteiras (y da base da linha i). */
export function linhasDe(el: Element): { n: number; fronteiras: number[] } {
  const retangulos: { t: number; b: number }[] = [];
  const r = document.createRange();
  const coletar = (no: Node) => {
    for (const filho of Array.from(no.childNodes)) {
      if (filho.nodeType === Node.TEXT_NODE) {
        r.selectNodeContents(filho);
        for (const q of Array.from(r.getClientRects())) if (q.height > 0) retangulos.push({ t: q.top, b: q.bottom });
      } else if (filho.nodeType === Node.ELEMENT_NODE) {
        const e = filho as Element;
        if (e.localName === 'br') continue;
        const cs = estilo(e);
        if (cs.display === 'none' || cs.position === 'absolute' || cs.position === 'fixed') continue;
        if (atomico(cs)) {
          const q = e.getBoundingClientRect();
          if (q.height > 0) retangulos.push({ t: q.top, b: q.bottom });
        } else coletar(e);
      }
    }
  };
  coletar(el);
  retangulos.sort((a, b) => a.t - b.t || a.b - b.b);
  const linhas: { t: number; b: number }[] = [];
  for (const q of retangulos) {
    const ultima = linhas[linhas.length - 1];
    if (!ultima || q.t >= ultima.b - 0.5) linhas.push({ ...q });
    else { ultima.t = Math.min(ultima.t, q.t); ultima.b = Math.max(ultima.b, q.b); }
  }
  const n = linhas.length;
  if (n < 2) return { n, fronteiras: [] };
  // Linhas de altura uniforme (o caso comum): fronteiras exatas a partir da caixa de conteúdo.
  const cs = estilo(el);
  const q = el.getBoundingClientRect();
  const c = caixa(cs, escalaVisual(el));
  const topo = q.top + c.bt + c.pt;
  const passo = (q.bottom - c.bb - c.pb - topo) / n;
  const uniforme = linhas.every((l, i) => Math.abs((l.t + l.b) / 2 - (topo + (i + 0.5) * passo)) < 1);
  const fronteiras: number[] = [];
  for (let i = 1; i < n; i++) {
    fronteiras.push(uniforme ? topo + i * passo : (linhas[i - 1]!.b + linhas[i]!.t) / 2);
  }
  return { n, fronteiras };
}

// ---------- texto ↔ posição ----------

/** Nós de texto do elemento, na ordem do textContent, com o deslocamento de início de cada um. */
export function nosDeTexto(el: Element): { no: Text; inicio: number }[] {
  const lista: { no: Text; inicio: number }[] = [];
  const w = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
  let total = 0;
  for (let n = w.nextNode(); n; n = w.nextNode()) {
    const t = n as Text;
    lista.push({ no: t, inicio: total });
    total += t.data.length;
  }
  return lista;
}

/** Nó de texto e deslocamento local do caractere `desloc` do textContent (ou o fim do último nó). */
export function localizar(el: Element, desloc: number): { no: Text; local: number } | null {
  const nos = nosDeTexto(el);
  for (const { no, inicio } of nos) if (desloc < inicio + no.data.length) return { no, local: desloc - inicio };
  const ultimo = nos[nos.length - 1];
  return ultimo ? { no: ultimo.no, local: ultimo.no.data.length } : null;
}

const alta = (c: number) => c >= 0xd800 && c <= 0xdbff;
const baixa = (c: number) => c >= 0xdc00 && c <= 0xdfff;

/**
 * Deslocamento (no textContent) do primeiro caractere cujo centro fica abaixo de `fronteira`: o início da linha que
 * começa ali. Busca binária (o centro dos caracteres só cresce na ordem do texto); caracteres sem caixa (espaço
 * colapsado, texto oculto) assumem o lado do próximo caractere com caixa. Devolve null se não houver divisão útil.
 */
export function deslocamentoNaFronteira(el: Element, fronteira: number): number | null {
  const nos = nosDeTexto(el);
  const total = nos.reduce((s, x) => s + x.no.data.length, 0);
  if (total === 0) return null;
  const r = document.createRange();
  let k = 0;
  const centro = (o: number): number | undefined => {
    while (k > 0 && nos[k]!.inicio > o) k--;
    while (k < nos.length - 1 && o >= nos[k]!.inicio + nos[k]!.no.data.length) k++;
    const { no, inicio } = nos[k]!;
    const local = o - inicio;
    const fim = Math.min(no.data.length, local + (alta(no.data.charCodeAt(local)) ? 2 : 1));
    r.setStart(no, local);
    r.setEnd(no, fim);
    const q = r.getBoundingClientRect();
    if (q.width === 0 && q.height === 0) return undefined;
    return (q.top + q.bottom) / 2;
  };
  const abaixo = (o: number): boolean => {
    for (let j = o; j < Math.min(total, o + 256); j++) {
      const c = centro(j);
      if (c !== undefined) return c > fronteira;
    }
    return true;
  };
  let lo = 0, hi = total;
  while (lo < hi) {
    const meio = (lo + hi) >> 1;
    if (abaixo(meio)) hi = meio; else lo = meio + 1;
  }
  let o = lo;
  // Nunca no meio de um par substituto (emoji).
  if (o > 0 && o < total) {
    const loc = localizar(el, o)!;
    const antes = localizar(el, o - 1)!;
    if (baixa(loc.no.data.charCodeAt(loc.local)) && alta(antes.no.data.charCodeAt(antes.local))) o--;
  }
  return o > 0 && o < total ? o : null;
}
