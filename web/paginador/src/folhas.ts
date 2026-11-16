// Folhas A4: prepara o documento (tira as regras @page do kit, insere folha.css, guarda o fluxo original num contêiner
// de medida com a largura da área) e compõe folha a folha, escolhendo as quebras como o Chromium (ver fragmentar.ts).

import folhaCss from '../folha.css';
import {
  acabamento, alturaConteudo, alturaUtil, ATR_OCULTO, copiarIntervalo, elementoEm, enumerarPontos, estenderPedacos, resolver, type Ponto,
} from './fragmentar';
import {
  ENCOLHIMENTO_MAXIMO, EPS_PX, esperarEstabilidade, estilo, folhasProntas, imagensProntas, LIMITE_ESTABILIDADE_MS, medirLargura,
  pxParaMm, type Largura,
} from './medida';
import {
  ErroComposicao, INICIO_DO_FLUXO, lerPlano, truncaTopo, VERSAO_PLANO,
  type AvisoLargura, type Composto, type Estouro, type PlanoQuebras, type PosicaoFluxo,
} from './plano';

export interface ConfigFolhas { cabecalhoUri: string; rodapeEsquerda: string; rodapeDireitaModelo: string }
export interface RefDoc { seletor: string; indice: number; id: string }
export interface PedidoCompor { sha256Html: string; refs: RefDoc[]; config: ConfigFolhas; limiteEstabilidadeMs: number | null }

const ID_CSS = 'pmvni-folha-css';
const MAX_FOLHAS = 5000;
const ATR_LARGO = 'data-pmvni-largo';

/** Fluxo de medida (div.folha.pmvni-medida > .area) com os filhos originais do <body>; criado na 1ª composição. */
export function prepararDocumento(): HTMLElement {
  const existente = document.querySelector<HTMLElement>('div.folha.pmvni-medida > .area');
  if (existente) return existente;
  // Regras @page do kit (inclusive dentro de @media) saem; só folha.css define a página.
  const tirarPage = (lista: CSSRuleList, apagar: (i: number) => void) => {
    for (let i = lista.length - 1; i >= 0; i--) {
      const r = lista[i]!;
      if (r instanceof CSSPageRule) apagar(i);
      else if (r instanceof CSSGroupingRule) tirarPage(r.cssRules, (j) => r.deleteRule(j));
    }
  };
  for (const s of Array.from(document.styleSheets)) {
    try { tirarPage(s.cssRules, (i) => s.deleteRule(i)); } catch { /* folha de estilo inacessível: não há no documento da sessão */ }
  }
  const css = document.createElement('style');
  css.id = ID_CSS;
  css.textContent = folhaCss;
  document.head.appendChild(css);

  const medida = document.createElement('div');
  medida.className = 'folha pmvni-medida';
  const fluxo = document.createElement('div');
  fluxo.className = 'area pmvni-fluxo';
  while (document.body.firstChild) fluxo.appendChild(document.body.firstChild);
  medida.appendChild(fluxo);
  document.body.appendChild(medida);
  return fluxo;
}

function criarFolha(cfg: ConfigFolhas): { folha: HTMLElement; area: HTMLElement; direita: HTMLElement } {
  const folha = document.createElement('section');
  folha.className = 'folha';
  const cab = document.createElement('div');
  cab.className = 'folha-cabecalho';
  const img = document.createElement('img');
  img.alt = '';
  img.src = cfg.cabecalhoUri;
  cab.appendChild(img);
  const area = document.createElement('div');
  area.className = 'area';
  const rodape = document.createElement('div');
  rodape.className = 'folha-rodape';
  const esquerda = document.createElement('div');
  esquerda.className = 'folha-rodape-esq';
  esquerda.textContent = cfg.rodapeEsquerda;
  const direita = document.createElement('div');
  direita.className = 'folha-rodape-dir';
  rodape.append(esquerda, direita);
  folha.append(cab, area, rodape);
  return { folha, area, direita };
}

/** Monta uma folha com o conteúdo do fluxo entre `de` e `ate` (null = até o fim) e aplica o acabamento das quebras. */
export function montarFolha(fluxo: HTMLElement, cfg: ConfigFolhas, de: PosicaoFluxo, ate: PosicaoFluxo | null,
  truncarTopo: boolean): { folha: HTMLElement; area: HTMLElement; direita: HTMLElement } {
  const f = criarFolha(cfg);
  document.body.appendChild(f.folha);
  copiarIntervalo(fluxo, f.area, de, ate);
  acabamento(f.area, truncarTopo);
  return f;
}

/**
 * Monta a folha `indice` (0 = primeira) de um plano já calculado, só com os dados do plano: --pmvni-k = plano.escala,
 * conteúdo entre inicioDasFolhas[indice] e o início da seguinte, margem de cima truncada se a quebra não for forçada
 * (truncaTopo), acabamento das quebras e extensão dos pedaços. Rodapé sem numeração. É o caminho da Tarefa 8.
 */
export function montarFolhaDoPlano(plano: PlanoQuebras, indice: number, cfg: ConfigFolhas): { folha: HTMLElement; area: HTMLElement } {
  const fluxo = prepararDocumento();
  document.documentElement.style.setProperty('--pmvni-k', String(plano.escala));
  aplicarAjustesFiguras(fluxo, plano.figuras ?? []);
  const de = plano.inicioDasFolhas[indice];
  if (!de) throw new ErroComposicao('folha_inexistente', `O plano não tem a folha ${indice + 1}.`);
  const f = montarFolha(fluxo, cfg, de, plano.inicioDasFolhas[indice + 1] ?? null, truncaTopo(de, indice));
  estenderPedacos(f.area);
  return f;
}

export interface PedidoAplicar {
  plano: PlanoQuebras;
  /** sha256 do HTML carregado (calculado pelo host); o plano precisa ser deste documento. */
  sha256Documento: string;
  refs: RefDoc[];
  config: ConfigFolhas;
  limiteEstabilidadeMs: number | null;
}

/** Resposta de `aplicarPlano`: folhas montadas, as que transbordam a área em altura e as que têm conteúdo além das
 * bordas esquerda/direita da área (1 = primeira; só informativo) e os refs. */
export interface Aplicado { folhas: number; folhasQueTransbordam: number[]; folhasQueTransbordamNaLargura: number[]; refsPorFolha: string[][] }

/**
 * Aplica um plano já calculado (pelo host oculto) sem medir para decidir: as folhas saem exatamente das posições do
 * plano, com --pmvni-k = plano.escala (nunca medirLargura), refs do documento, margem de cima pela regra truncaTopo,
 * acabamento, numeração do rodapé e extensão dos pedaços. Recusa ('plano_de_outro_documento') se o plano for de outro
 * HTML e ('plano_invalido') se um início não existir neste documento. Antes de medir espera as fontes e as imagens das
 * folhas; a única medida é a altura do conteúdo de cada folha, para informar folhasQueTransbordam (na tela, com outra
 * escala de monitor, pode haver transbordamento sem que o conteúdo das folhas mude).
 */
export async function aplicarPlano(pedido: PedidoAplicar): Promise<Aplicado> {
  const { plano, config } = pedido;
  if (plano.sha256Html !== pedido.sha256Documento) {
    throw new ErroComposicao('plano_de_outro_documento', 'O plano de quebras foi calculado para outro documento.');
  }
  const limite = pedido.limiteEstabilidadeMs ?? LIMITE_ESTABILIDADE_MS;
  const fluxo = prepararDocumento();
  plano.inicioDasFolhas.forEach((p, i) => {
    if (!elementoEm(fluxo, p.caminho)) {
      throw new ErroComposicao('plano_invalido', `O início da folha ${i + 1} do plano não existe neste documento.`);
    }
  });
  for (const f of Array.from(document.querySelectorAll('section.folha'))) f.remove();
  // O fluxo não é medido: fica fora do layout, como depois de compor.
  fluxo.parentElement!.setAttribute('data-pmvni-guardado', '');
  aplicarRefs(fluxo, pedido.refs);
  document.documentElement.style.setProperty('--pmvni-k', String(plano.escala));
  aplicarAjustesFiguras(fluxo, plano.figuras ?? []);

  const montadas = plano.inicioDasFolhas.map((de, i) =>
    montarFolha(fluxo, config, de, plano.inicioDasFolhas[i + 1] ?? null, truncaTopo(de, i)));
  numerarRodapes(montadas.map((f) => f.direita), config);
  await folhasProntas(montadas.map((f) => f.folha), limite);
  const H = alturaUtil(montadas[0]!.area);
  const folhasQueTransbordam = montadas.flatMap((f, i) => (alturaConteudo(f.area) > H + EPS_PX ? [i + 1] : []));
  for (const f of montadas) estenderPedacos(f.area);
  const folhasQueTransbordamNaLargura = montadas.flatMap((f, i) => (transbordaNaLargura(f.area) ? [i + 1] : []));
  return { folhas: montadas.length, folhasQueTransbordam, folhasQueTransbordamNaLargura, refsPorFolha: refsDasFolhas(montadas.map((f) => f.folha)) };
}

/** Há conteúdo pintado além da borda esquerda ou direita da área útil? Mede as caixas de tudo o que a área contém (texto
 * inclusive: um `nowrap` que passa da própria caixa aparece nos retângulos do Range, não no da caixa), nas coordenadas da
 * tela (a escala 1/k já aplicada). O editor não corta nada; isto só avisa que algo sai da área (o kit cortaria). */
function transbordaNaLargura(area: HTMLElement): boolean {
  const r = area.getBoundingClientRect();
  const intervalo = document.createRange();
  intervalo.selectNodeContents(area);
  for (const q of Array.from(intervalo.getClientRects())) {
    if (q.width > 0 && q.height > 0 && (q.right > r.right + EPS_PX + 0.45 || q.left < r.left - EPS_PX - 0.45)) return true;
  }
  return false;
}

function numerarRodapes(direitas: HTMLElement[], cfg: ConfigFolhas): void {
  direitas.forEach((d, i) => {
    d.textContent = cfg.rodapeDireitaModelo.replaceAll('{n}', String(i + 1)).replaceAll('{t}', String(direitas.length));
  });
}

/** data-ref visíveis de cada folha, na ordem do documento, sem repetição. */
function refsDasFolhas(folhas: HTMLElement[]): string[][] {
  return folhas.map((f) => {
    const ids: string[] = [];
    for (const e of Array.from(f.querySelectorAll('.area [data-ref]'))) {
      const id = e.getAttribute('data-ref')!;
      if (!ids.includes(id) && estilo(e).display !== 'none') ids.push(id);
    }
    return ids;
  });
}

/**
 * Avisos e estouros de largura: um aviso por folha e ref (o bloco mais largo); além do encolhimento máximo, também
 * estouro. Nunca descarta um bloco largo: se a folha dele não for achada pelo marcador, vale a primeira folha que
 * mostra o ref dele, senão a 1ª. Se a largura pedida vier só do scrollWidth (sem bloco identificado), o aviso e o
 * estouro saem na 1ª folha, sem ref.
 */
export function larguraNoPlano(largura: Pick<Largura, 'pedida' | 'largos' | 'area' | 'esquerda'>, areas: HTMLElement[],
  fluxo: HTMLElement): { avisosLargura: AvisoLargura[]; estouros: Estouro[] } {
  const visivelEm = (a: HTMLElement, seletor: string) =>
    Array.from(a.querySelectorAll(seletor)).some((e) => !e.closest(`[${ATR_OCULTO}]`));
  const avisos = new Map<string, AvisoLargura>();
  const registrar = (a: AvisoLargura) => {
    const chave = a.folha + '|' + (a.ref ?? '');
    if ((avisos.get(chave)?.escala ?? 0) < a.escala) avisos.set(chave, a);
  };
  largura.largos.forEach((l, i) => {
    const ref = refDe(fluxo, l.el);
    let folha = 1 + areas.findIndex((a) => visivelEm(a, `[${ATR_LARGO}="${i}"]`));
    if (folha === 0 && ref !== null) folha = 1 + areas.findIndex((a) => visivelEm(a, `[data-ref="${CSS.escape(ref)}"]`));
    if (folha === 0) folha = 1;
    registrar({ folha, ref, escala: (l.direita - largura.esquerda) / largura.area, larguraMm: mm(l.direita - largura.esquerda) });
  });
  if (largura.largos.length === 0 && largura.pedida > 1) {
    registrar({ folha: 1, ref: null, escala: largura.pedida, larguraMm: mm(largura.pedida * largura.area) });
  }
  const avisosLargura = [...avisos.values()].sort((a, b) => a.folha - b.folha || b.escala - a.escala);
  const estouros: Estouro[] = [];
  for (const a of avisosLargura) {
    if (a.escala > ENCOLHIMENTO_MAXIMO + 1e-9) {
      const area = areas[a.folha - 1];
      estouros.push({ folha: a.folha, ref: a.ref, alturaMm: area ? mm(alturaConteudo(area)) : 0, larguraMm: a.larguraMm });
    }
  }
  return { avisosLargura, estouros };
}

function aplicarRefs(fluxo: HTMLElement, refs: RefDoc[]): void {
  for (const e of Array.from(fluxo.querySelectorAll('[data-ref]'))) e.removeAttribute('data-ref');
  for (const r of refs) fluxo.querySelectorAll(r.seletor)[r.indice]?.setAttribute('data-ref', r.id);
}

/** Ref de um elemento do fluxo: o data-ref dele ou de um ancestral; senão, o do último bloco identificado antes dele. */
function refDe(fluxo: HTMLElement, el: Element | null): string | null {
  if (!el) return null;
  const proprio = el.closest('[data-ref]');
  if (proprio && fluxo.contains(proprio)) return proprio.getAttribute('data-ref');
  let ultimo: string | null = null;
  for (const c of Array.from(fluxo.querySelectorAll('[data-ref]'))) {
    if (c.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) ultimo = c.getAttribute('data-ref');
    else break;
  }
  return ultimo;
}

const mm = (px: number) => Math.round(pxParaMm(px) * 100) / 100;

/**
 * Compõe o documento: espera estabilidade (falha com 'instavel' se não houver), enumera os pontos de quebra no fluxo de
 * medida e enche folha a folha. Para cada folha, entre os pontos cuja altura prevista cabe na área, tenta primeiro a
 * quebra forçada que cabe, depois o maior apelo e, entre iguais, o mais tardio; monta a folha e confere a altura real;
 * se não couber, tenta os demais candidatos na mesma ordem. Sem candidato que caiba, coloca a menor porção possível
 * (o próximo ponto) e registra o estouro. Conteúdo mais largo que a área gera AvisoLargura (documento reduzido) e,
 * além de ENCOLHIMENTO_MAXIMO, também Estouro de largura.
 */
export async function compor(pedido: PedidoCompor): Promise<Composto> {
  const limite = pedido.limiteEstabilidadeMs ?? LIMITE_ESTABILIDADE_MS;
  const fluxo = prepararDocumento();
  const medida = fluxo.parentElement!;
  for (const f of Array.from(document.querySelectorAll('section.folha'))) f.remove();
  medida.removeAttribute('data-pmvni-guardado');
  aplicarRefs(fluxo, pedido.refs);
  // Escala de impressão: mede com k = 1 e, se o conteúdo for mais largo que a área, rediagrama com k (folha.css).
  const raiz = document.documentElement;
  raiz.style.setProperty('--pmvni-k', '1');
  await esperarEstabilidade(fluxo, limite);
  const largura = medirLargura(fluxo);
  for (const e of Array.from(fluxo.querySelectorAll(`[${ATR_LARGO}]`))) e.removeAttribute(ATR_LARGO);
  largura.largos.forEach((l, i) => l.el.setAttribute(ATR_LARGO, String(i)));
  const escala = largura.escala;
  if (escala !== 1) {
    raiz.style.setProperty('--pmvni-k', String(escala));
    await esperarEstabilidade(fluxo, limite);
  }

  // A altura útil vem de folha.css (constantes num só lugar).
  const sonda = criarFolha(pedido.config);
  document.body.appendChild(sonda.folha);
  const H = alturaUtil(sonda.area);
  sonda.folha.remove();

  // Quantas figuras cabem por folha, sem o usuário escolher: pagina; se uma figura foi empurrada para a folha seguinte e
  // caberia na sobra da anterior com a imagem um pouco menor, reduz a imagem e pagina de novo (poucas rodadas).
  const ajustes = new Map<Element, number>();
  aplicarAjustesFiguras(fluxo, []);
  let r = await paginar(fluxo, pedido, H, limite);
  for (let rodada = 0; rodada < RODADAS_FIGURAS; rodada++) {
    const novos = ajustesDeFiguras(fluxo, r, H);
    if (novos.size === 0) break;
    for (const [fig, px] of novos) ajustes.set(fig, px);
    for (const f of Array.from(document.querySelectorAll('section.folha'))) f.remove();
    aplicarAjustesFiguras(fluxo, [...ajustes].map(([fig, px]) => ({ caminho: caminhoDe(fluxo, fig), alturaPx: px })));
    await esperarEstabilidade(fluxo, limite);
    r = await paginar(fluxo, pedido, H, limite);
  }
  const { inicios, estouros, direitas, areas } = r;
  const figuras = ajustes.size === 0 ? null : [...ajustes].map(([fig, px]) => ({ caminho: caminhoDe(fluxo, fig), alturaPx: px }));

  // Largura: avisos por folha e ref; além do encolhimento máximo, também estouro de largura (nunca descartado).
  const { avisosLargura, estouros: deLargura } = larguraNoPlano(largura, areas, fluxo);
  estouros.push(...deLargura);
  estouros.sort((a, b) => a.folha - b.folha);

  medida.setAttribute('data-pmvni-guardado', '');
  const folhas = Array.from(document.querySelectorAll<HTMLElement>('section.folha'));
  numerarRodapes(direitas, pedido.config);
  await imagensProntas(document.body, limite);
  const refsPorFolha = refsDasFolhas(folhas);
  return {
    plano: { versao: VERSAO_PLANO, inicioDasFolhas: inicios, estouros, sha256Html: pedido.sha256Html, escala, avisosLargura, figuras },
    folhas: folhas.length,
    refsPorFolha,
  };
}

/** Rodadas de reajuste das figuras (cada uma repagina o documento). */
const RODADAS_FIGURAS = 3;
/** A imagem de uma figura pode encolher até esta fração da altura que tinha… */
const FIGURA_REDUCAO_MAXIMA = 0.4;
/** …e nunca abaixo desta altura (mm): a prancha precisa continuar legível. */
const FIGURA_ALTURA_MINIMA_MM = 40;
const SELETOR_IMG_FIGURA = ':scope > .fi img';

interface Paginado { inicios: PosicaoFluxo[]; estouros: Estouro[]; direitas: HTMLElement[]; areas: HTMLElement[] }

/** Caminho (índices de filhos-elemento) de `el` a partir do fluxo. */
function caminhoDe(fluxo: Element, el: Element): number[] {
  const c: number[] = [];
  for (let e: Element = el; e !== fluxo && e.parentElement; e = e.parentElement) c.unshift(Array.prototype.indexOf.call(e.parentElement.children, e));
  return c;
}

/** Tira as alturas impostas antes e aplica as do plano (max-height inline na imagem da figura). */
function aplicarAjustesFiguras(fluxo: HTMLElement, ajustes: readonly { caminho: number[]; alturaPx: number }[]): void {
  for (const img of Array.from(fluxo.querySelectorAll<HTMLElement>('.fig > .fi img'))) img.style.removeProperty('max-height');
  for (const a of ajustes) {
    const img = elementoEm(fluxo, a.caminho)?.querySelector<HTMLElement>(SELETOR_IMG_FIGURA);
    if (img) img.style.setProperty('max-height', `${a.alturaPx.toFixed(2)}px`);
  }
}

/** Figuras que abrem uma folha (quebra não forçada) e caberiam na sobra da folha anterior com a imagem reduzida dentro dos
 * limites: a nova altura máxima (px CSS) da imagem de cada uma, no fluxo. */
function ajustesDeFiguras(fluxo: HTMLElement, r: Paginado, H: number): Map<Element, number> {
  const novos = new Map<Element, number>();
  for (let i = 0; i + 1 < r.areas.length; i++) {
    if (r.inicios[i + 1]!.forcada) continue;
    const fig = primeiroVisivel(r.areas[i + 1]!);
    if (!fig?.matches('.fig')) continue;
    const img = fig.querySelector<HTMLElement>(SELETOR_IMG_FIGURA);
    const original = elementoEm(fluxo, r.inicios[i + 1]!.caminho);
    const imgFluxo = original?.querySelector<HTMLElement>(SELETOR_IMG_FIGURA);
    if (!img || !original || !imgFluxo || !original.matches('.fig')) continue;
    const sobra = H - alturaConteudo(r.areas[i]!);
    const rFig = fig.getBoundingClientRect();
    const margem = parseFloat(estilo(fig).marginTop) || 0;
    const precisa = rFig.height + margem - sobra + 2;
    const rImg = img.getBoundingClientRect();
    if (precisa <= 0 || rImg.height <= 0) continue;
    // A figura é uma grade (imagem | textos): só encolhe enquanto a imagem for a parte mais alta.
    const textos = Array.from(fig.children).filter((c) => !c.matches('.fi')).reduce((m, c) => Math.max(m, c.getBoundingClientRect().height), 0);
    const novaTela = rImg.height - precisa;
    const escalaTela = rImg.height / imgFluxo.offsetHeight; // tela ÷ px CSS (a folha pinta em 1/k)
    const minimoTela = Math.max(rImg.height * (1 - FIGURA_REDUCAO_MAXIMA), mmParaPx(FIGURA_ALTURA_MINIMA_MM) * escalaTela, textos);
    if (novaTela < minimoTela || !Number.isFinite(escalaTela) || escalaTela <= 0) continue;
    novos.set(original, novaTela / escalaTela);
  }
  return novos;
}

function primeiroVisivel(area: HTMLElement): Element | null {
  for (const c of Array.from(area.children)) if (!c.hasAttribute(ATR_OCULTO) && estilo(c).display !== 'none') return c;
  return null;
}

const mmParaPx = (mm: number) => (mm * 96) / 25.4;

/** Pagina o fluxo (já medido e estável) folha a folha, escolhendo as quebras como o Chromium. */
async function paginar(fluxo: HTMLElement, pedido: PedidoCompor, H: number, limite: number): Promise<Paginado> {
  const pontos = enumerarPontos(fluxo);
  const inicios: PosicaoFluxo[] = [];
  const estouros: Estouro[] = [];
  const direitas: HTMLElement[] = [];
  const areas: HTMLElement[] = [];
  let ini = { pos: INICIO_DO_FLUXO, indice: -1, yInicio: fluxo.getBoundingClientRect().top, decoInicio: 0 };

  for (let n = 1; ; n++) {
    if (n > MAX_FOLHAS) throw new ErroComposicao('folhas_demais', `O documento passaria de ${MAX_FOLHAS} folhas.`);
    const truncar = truncaTopo(ini.pos, n - 1);
    const altura = (p: Ponto) => p.yFim - ini.yInicio + ini.decoInicio + p.decoFim;
    const candidatos: Ponto[] = [];
    for (let j = ini.indice + 1; j < pontos.length; j++) {
      const p = pontos[j]!;
      if (p.yFim - ini.yInicio > H + EPS_PX) break;
      if (altura(p) <= H + EPS_PX) candidatos.push(p);
      if (p.forcado) break;
    }
    const ordem = [...candidatos].sort((a, b) => Number(b.forcado) - Number(a.forcado) || b.apelo - a.apelo || b.indice - a.indice);

    let escolhido: { p: Ponto; pos: PosicaoFluxo | null } | null = null;
    let montada: ReturnType<typeof montarFolha> | null = null;
    for (const p of ordem) {
      const pos = resolver(p);
      if (pos === undefined) continue;
      const f = montarFolha(fluxo, pedido.config, ini.pos, pos, truncar);
      await imagensProntas(f.area, limite);
      if (alturaConteudo(f.area) <= H + EPS_PX) { escolhido = { p, pos }; montada = f; break; }
      f.folha.remove();
    }
    if (!escolhido) {
      // Nada cabe: a menor porção possível (o próximo ponto) vai para esta folha, e o estouro fica registrado.
      for (let j = ini.indice + 1; j < pontos.length && !escolhido; j++) {
        const pos = resolver(pontos[j]!);
        if (pos !== undefined) escolhido = { p: pontos[j]!, pos };
      }
      montada = montarFolha(fluxo, pedido.config, ini.pos, escolhido!.pos, truncar);
      await imagensProntas(montada.area, limite);
      const h = alturaConteudo(montada.area);
      if (h > H + EPS_PX) {
        estouros.push({ folha: n, ref: refDe(fluxo, elementoEm(fluxo, ini.pos.caminho)), alturaMm: mm(h), larguraMm: null });
      }
    }
    estenderPedacos(montada!.area);
    inicios.push(ini.pos);
    direitas.push(montada!.direita);
    areas.push(montada!.area);
    const { p, pos } = escolhido!;
    if (pos === null) break;
    ini = { pos: { ...pos, forcada: p.forcado }, indice: p.indice, yInicio: p.yInicio, decoInicio: p.decoInicio };
  }
  return { inicios, estouros, direitas, areas };
}

const texto = (v: unknown, nome: string) => { if (typeof v !== 'string') throw new Error(`campo ${nome} invalido`); return v; };

/** Refs, config e limite, comuns a `compor` e `aplicarPlano`. */
function lerComum(d: Record<string, unknown>): Omit<PedidoCompor, 'sha256Html'> {
  if (!Array.isArray(d.refs)) throw new Error('campo refs invalido');
  const refs = d.refs.map((r: unknown, i) => {
    const o = r as Record<string, unknown>;
    if (typeof o !== 'object' || o === null || typeof o.indice !== 'number') throw new Error(`refs[${i}] invalido`);
    return { seletor: texto(o.seletor, 'seletor'), indice: o.indice, id: texto(o.id, 'id') };
  });
  const c = d.config as Record<string, unknown> | undefined;
  if (typeof c !== 'object' || c === null) throw new Error('campo config invalido');
  const limite = d.limiteEstabilidadeMs;
  if (limite != null && (typeof limite !== 'number' || !(limite > 0))) throw new Error('campo limiteEstabilidadeMs invalido');
  return {
    refs,
    config: { cabecalhoUri: texto(c.cabecalhoUri, 'cabecalhoUri'), rodapeEsquerda: texto(c.rodapeEsquerda, 'rodapeEsquerda'),
      rodapeDireitaModelo: texto(c.rodapeDireitaModelo, 'rodapeDireitaModelo') },
    limiteEstabilidadeMs: limite == null ? null : limite,
  };
}

/** Valida o pedido de `compor` vindo da ponte. */
export function lerPedido(d: Record<string, unknown>): PedidoCompor {
  return { sha256Html: texto(d.sha256Html, 'sha256Html'), ...lerComum(d) };
}

/** Valida o pedido de `aplicarPlano` vindo da ponte (o plano com lerPlano: 'plano_invalido'). */
export function lerPedidoAplicar(d: Record<string, unknown>): PedidoAplicar {
  return { plano: lerPlano(d.plano), sha256Documento: texto(d.sha256Documento, 'sha256Documento'), ...lerComum(d) };
}
