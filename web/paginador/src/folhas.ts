// Folhas A4: prepara o documento (tira as regras @page do kit, insere folha.css, guarda o fluxo original num contêiner
// de medida com a largura da área) e compõe folha a folha, escolhendo as quebras como o Chromium (ver fragmentar.ts).

import folhaCss from '../folha.css';
import {
  acabamento, alturaConteudo, copiarIntervalo, elementoEm, enumerarPontos, estenderPedacos, resolver, type Ponto,
} from './fragmentar';
import { EPS_PX, escalaDeImpressao, esperarEstabilidade, estilo, pxParaMm } from './medida';
import { INICIO_DO_FLUXO, VERSAO_PLANO, type Composto, type Estouro, type PosicaoFluxo } from './plano';

export interface ConfigFolhas { cabecalhoUri: string; rodapeEsquerda: string; rodapeDireitaModelo: string }
export interface RefDoc { seletor: string; indice: number; id: string }
export interface PedidoCompor { sha256Html: string; refs: RefDoc[]; config: ConfigFolhas }

const ID_CSS = 'pmvni-folha-css';
const MAX_FOLHAS = 5000;

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

function aplicarRefs(fluxo: HTMLElement, refs: RefDoc[]): void {
  for (const e of Array.from(fluxo.querySelectorAll('[data-ref]'))) e.removeAttribute('data-ref');
  for (const r of refs) fluxo.querySelectorAll(r.seletor)[r.indice]?.setAttribute('data-ref', r.id);
}

/** Ref do bloco na posição: o data-ref dele ou de um ancestral; senão, o do último bloco identificado antes dele. */
function refDe(fluxo: HTMLElement, pos: PosicaoFluxo): string | null {
  const el = elementoEm(fluxo, pos.caminho);
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

const quebraForcada = (p: Ponto) => p.forcado;

async function imagensProntas(raiz: HTMLElement): Promise<void> {
  const pendentes = Array.from(raiz.querySelectorAll('img')).filter((i) => !i.complete);
  if (pendentes.length) await Promise.all(pendentes.map((i) => i.decode().catch(() => undefined)));
}

/**
 * Compõe o documento: espera estabilidade, enumera os pontos de quebra no fluxo de medida e enche folha a folha.
 * Para cada folha, entre os pontos cuja altura prevista cabe na área, escolhe o de maior apelo e, entre iguais, o
 * mais tardio (quebra forçada encerra a folha); monta a folha e confere a altura real; se não couber, tenta o próximo
 * candidato. Sem candidato, coloca a menor porção possível (o próximo ponto) e registra o estouro.
 */
export async function compor(pedido: PedidoCompor): Promise<Composto> {
  const fluxo = prepararDocumento();
  const medida = fluxo.parentElement!;
  for (const f of Array.from(document.querySelectorAll('section.folha'))) f.remove();
  medida.removeAttribute('data-pmvni-guardado');
  aplicarRefs(fluxo, pedido.refs);
  // Escala de impressão: mede com k = 1 e, se o conteúdo for mais largo que a área, rediagrama com k (folha.css).
  const raiz = document.documentElement;
  raiz.style.setProperty('--pmvni-k', '1');
  await esperarEstabilidade(fluxo);
  const escala = escalaDeImpressao(fluxo);
  if (escala !== 1) {
    raiz.style.setProperty('--pmvni-k', String(escala));
    await esperarEstabilidade(fluxo);
  }

  // A altura útil vem de folha.css (constantes num só lugar).
  const sonda = criarFolha(pedido.config);
  document.body.appendChild(sonda.folha);
  const H = sonda.area.getBoundingClientRect().height;
  sonda.folha.remove();

  const pontos = enumerarPontos(fluxo);
  const inicios: PosicaoFluxo[] = [];
  const estouros: Estouro[] = [];
  const direitas: HTMLElement[] = [];
  let ini = { pos: INICIO_DO_FLUXO, indice: -1, yInicio: fluxo.getBoundingClientRect().top, decoInicio: 0, forcado: false };

  for (let n = 1; n <= MAX_FOLHAS; n++) {
    const truncar = n > 1 && !ini.forcado;
    const altura = (p: Ponto) => p.yFim - ini.yInicio + ini.decoInicio + p.decoFim;
    const candidatos: Ponto[] = [];
    for (let j = ini.indice + 1; j < pontos.length; j++) {
      const p = pontos[j]!;
      if (p.yFim - ini.yInicio > H + EPS_PX) break;
      if (altura(p) <= H + EPS_PX) candidatos.push(p);
      if (quebraForcada(p)) break;
    }
    const ultimo = candidatos[candidatos.length - 1];
    const ordem = ultimo && quebraForcada(ultimo)
      ? [ultimo]
      : [...candidatos].sort((a, b) => b.apelo - a.apelo || b.indice - a.indice);

    let escolhido: { p: Ponto; pos: PosicaoFluxo | null } | null = null;
    let montada: ReturnType<typeof montarFolha> | null = null;
    for (const p of ordem) {
      const pos = resolver(p);
      if (pos === undefined) continue;
      const f = montarFolha(fluxo, pedido.config, ini.pos, pos, truncar);
      await imagensProntas(f.area);
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
      await imagensProntas(montada.area);
      const h = alturaConteudo(montada.area);
      if (h > H + EPS_PX) estouros.push({ folha: n, ref: refDe(fluxo, ini.pos), alturaMm: Math.round(pxParaMm(h) * 100) / 100 });
    }
    estenderPedacos(montada!.area);
    inicios.push(ini.pos);
    direitas.push(montada!.direita);
    const { p, pos } = escolhido!;
    if (pos === null) break;
    ini = { pos, indice: p.indice, yInicio: p.yInicio, decoInicio: p.decoInicio, forcado: p.forcado };
  }

  medida.setAttribute('data-pmvni-guardado', '');
  const folhas = Array.from(document.querySelectorAll<HTMLElement>('section.folha'));
  folhas.forEach((_, i) => {
    direitas[i]!.textContent = pedido.config.rodapeDireitaModelo.replaceAll('{n}', String(i + 1)).replaceAll('{t}', String(folhas.length));
  });
  await imagensProntas(document.body);
  const refsPorFolha = folhas.map((f) => {
    const ids: string[] = [];
    for (const e of Array.from(f.querySelectorAll('.area [data-ref]'))) {
      const id = e.getAttribute('data-ref')!;
      if (!ids.includes(id) && estilo(e).display !== 'none') ids.push(id);
    }
    return ids;
  });
  return {
    plano: { versao: VERSAO_PLANO, inicioDasFolhas: inicios, estouros, sha256Html: pedido.sha256Html, escala },
    folhas: folhas.length,
    refsPorFolha,
  };
}

/** Valida o pedido de `compor` vindo da ponte. */
export function lerPedido(d: Record<string, unknown>): PedidoCompor {
  const texto = (v: unknown, nome: string) => { if (typeof v !== 'string') throw new Error(`campo ${nome} invalido`); return v; };
  const sha256Html = texto(d.sha256Html, 'sha256Html');
  if (!Array.isArray(d.refs)) throw new Error('campo refs invalido');
  const refs = d.refs.map((r: unknown, i) => {
    const o = r as Record<string, unknown>;
    if (typeof o !== 'object' || o === null || typeof o.indice !== 'number') throw new Error(`refs[${i}] invalido`);
    return { seletor: texto(o.seletor, 'seletor'), indice: o.indice, id: texto(o.id, 'id') };
  });
  const c = d.config as Record<string, unknown> | undefined;
  if (typeof c !== 'object' || c === null) throw new Error('campo config invalido');
  return {
    sha256Html, refs,
    config: { cabecalhoUri: texto(c.cabecalhoUri, 'cabecalhoUri'), rodapeEsquerda: texto(c.rodapeEsquerda, 'rodapeEsquerda'),
      rodapeDireitaModelo: texto(c.rodapeDireitaModelo, 'rodapeDireitaModelo') },
  };
}
