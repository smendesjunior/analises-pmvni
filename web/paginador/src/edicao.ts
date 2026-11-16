// Edição na tela (Tarefa 7, Etapa 2): seleção de itens, campos editáveis (data-campo), edição no próprio A4, âncora de
// rolagem, atalhos e menu de contexto. Tudo vale só no DOM da tela (as folhas que o host aplicou): data-campo, seleção,
// marcas e realce nunca existem no HTML exportado nem no fluxo de medida, e nada aqui muda a paginação (o span do título é
// inline, a seleção é `outline`, as marcas moram na camada fora das folhas, em marcas.ts).
//
// O conteúdo do relatório é NÃO CONFIÁVEL e o texto editado é texto puro: o campo em edição é `plaintext-only`, o colado
// entra só como texto simples e em uma linha, e o texto que sai para o host é lido como texto (nunca innerHTML).
//
// Protocolo (docs: Mensagens.cs / mensagens.ts): o host manda `estadoEditor` depois de cada aplicação do plano (seleção,
// campos, marcas, âncora), `selecionar`, `editarCampo`, `concluirEdicao` e `obterAncora`; a web avisa por eventos com id
// `evento-N` (selecionado, pedirEdicao, editar, edicaoCancelada, atalho, menuContexto, excluidoClicado). Depois de um
// `editar` (Enter) a tela mostra o texto novo até o host mandar `estadoEditor` (se o host recusou a edição, esse estado
// devolve o texto original); a recomposição do host troca as folhas e leva tudo junto.

import { ATR_OCULTO } from './fragmentar';
import type { Manipulador } from './index';
import { blocoDe, desenvolver, ehCaminho, ehLinha, marcarCamposLinhas, marcarLinhas, pedacosDoBloco } from './linhas';
import { CSS_MARCAS, desenharMarcas, ID_CAMADA, type EstadoMarcas, type Excluido } from './marcas';
import type { TipoHost, TipoWeb } from './mensagens';
import { ErroComposicao } from './plano';
import { definirGola, GOLA_COM_MARCAS_PX, GOLA_ESQUERDA_PX, rolarAteRef, zoomAtual } from './tela';

type Enviar = (tipo: TipoWeb, id: string, dados?: Record<string, unknown>) => void;
type Registrar = (tipo: TipoHost, manipulador: Manipulador) => void;

export const CAMPOS = ['titulo', 'verificacao', 'providencia', 'base_legal', 'referencia'] as const;
export type Campo = (typeof CAMPOS)[number];

const ID_CSS = 'pmvni-edicao-css';
const ATR_CAMPO = 'data-campo';
const ATR_SELECIONADO = 'data-pmvni-selecionado';
const ATR_EDITANDO = 'data-pmvni-editando';
/** Cada pedaço de um item (um card inteiro ou a parte dele que cabe numa folha) leva o data-ref do item. */
const SELETOR_PEDACOS = 'section.folha .area .card[data-ref]';
/** Figuras da seção IMAGENS DO PROJETO (Etapa 4): clique seleciona, duplo clique pede o editor de marcações (campo null). */
const SELETOR_FIGURAS = 'section.folha .area .fig[data-ref]';
const SEPARADOR_TITULO = ' — ';

/** Atalhos repassados ao host (nomes canônicos; fora da edição). */
const ATALHOS: ReadonlySet<string> = new Set([
  'Ctrl+O', 'Ctrl+I', 'Ctrl+S', 'Ctrl+Shift+S', 'Ctrl+E', 'Ctrl+Z', 'Ctrl+Y', 'Ctrl+F', 'Alt+ArrowUp', 'Alt+ArrowDown',
  'Delete', 'F2', 'F6', 'Ctrl+0', 'Ctrl+Plus', 'Ctrl+Minus',
  // Trilho de módulos (Ctrl+1 … Ctrl+5): valem também com o foco nas folhas.
  'Ctrl+1', 'Ctrl+2', 'Ctrl+3', 'Ctrl+4', 'Ctrl+5',
]);

const CSS_EDICAO = `
@media screen {
  [${ATR_SELECIONADO}] { outline: 2px solid #1a73e8; outline-offset: 1px; }
  section.folha [${ATR_CAMPO}]:hover { background-color: rgba(26, 115, 232, .10); }
  [${ATR_EDITANDO}] { outline: 2px solid #1a73e8; outline-offset: 1px; background-color: #fff8d6 !important; cursor: text; }
}
${CSS_MARCAS}`;

// ---------- estado ----------

let ativo = false; // só depois do primeiro estadoEditor/selecionar: o resto do paginador (compor, exportar) não reage a cliques
let contadorEventos = 0;

interface Edicao {
  ref: string;
  /** Um dos cinco campos do item, ou o caminho do texto numa linha de tabela (linhas.ts). */
  campo: string;
  primeiro: HTMLElement;
  demais: HTMLElement[];
  originais: Node[];
  visibilidade: string[];
  /** Enter dado: o texto foi ao host e a tela espera o próximo estadoEditor. */
  pendente: boolean;
}
let edicao: Edicao | null = null;

const ehCampo = (v: unknown): v is Campo => typeof v === 'string' && (CAMPOS as readonly string[]).includes(v);
/** Campo válido para o bloco: um dos cinco do item, ou um caminho de texto numa linha de tabela. */
const campoDoBloco = (ref: string, v: unknown): v is string => (ehLinha(ref) ? ehCaminho(v) : ehCampo(v));
/** Pedaços de um item (cards) ou de uma linha de tabela (as linhas visíveis), nas folhas. */
const pedacosDe = (ref: string): HTMLElement[] => pedacosDoBloco(ref);

function garantirCss(): void {
  if (document.getElementById(ID_CSS)) return;
  const css = document.createElement('style');
  css.id = ID_CSS;
  css.textContent = CSS_EDICAO;
  document.head.appendChild(css);
}

// ---------- campos (data-campo) ----------

/** O texto do título depois de `span.id` + " — " vai para um span inline (sem mudar o texto nem a medida do card). */
function campoTitulo(card: HTMLElement): HTMLElement | null {
  const ct = card.querySelector<HTMLElement>(':scope > .ch > .ct');
  if (!ct) return null;
  const marcado = ct.querySelector<HTMLElement>(`:scope > span[${ATR_CAMPO}="titulo"]`);
  if (marcado) return marcado;
  const primeiro = ct.querySelector(':scope > span.id')?.nextSibling;
  if (!primeiro || primeiro.nodeType !== Node.TEXT_NODE) return null;
  const texto = primeiro as Text;
  if (!texto.data.startsWith(SEPARADOR_TITULO)) return null;
  const resto = texto.data.slice(SEPARADOR_TITULO.length);
  const seguintes: Node[] = [];
  for (let n = texto.nextSibling; n; n = n.nextSibling) seguintes.push(n);
  if (resto === '' && seguintes.length === 0) return null;
  const span = document.createElement('span');
  texto.data = SEPARADOR_TITULO;
  if (resto !== '') span.appendChild(document.createTextNode(resto));
  span.append(...seguintes);
  ct.appendChild(span);
  return span;
}

function camposDoPedaco(card: HTMLElement): [Campo, HTMLElement | null][] {
  const q = (seletor: string) => card.querySelector<HTMLElement>(`:scope > ${seletor}`);
  return [
    ['titulo', campoTitulo(card)],
    ['verificacao', q(`p:not([${ATR_OCULTO}])`)],
    ['providencia', q(`.prov > p:not([${ATR_OCULTO}])`)],
    ['base_legal', q(`.two > div:nth-child(1) p.m:not([${ATR_OCULTO}])`)],
    ['referencia', q(`.two > div:nth-child(2) p.m:not([${ATR_OCULTO}])`)],
  ];
}

/** Marca os campos de todos os pedaços de item das folhas e devolve quantos elementos data-campo há. Idempotente. */
function marcarCampos(): number {
  for (const card of Array.from(document.querySelectorAll<HTMLElement>(SELETOR_PEDACOS))) {
    for (const [campo, el] of camposDoPedaco(card)) el?.setAttribute(ATR_CAMPO, campo);
  }
  marcarCamposLinhas(); // linhas das tabelas de índices e de iluminação e ventilação
  return document.querySelectorAll(`section.folha [${ATR_CAMPO}]`).length;
}

/** Desfaz a marcação: tira o atributo e devolve o título ao texto corrido original. */
function desmarcarCampos(): void {
  for (const el of Array.from(document.querySelectorAll<HTMLElement>(`section.folha [${ATR_CAMPO}]`))) {
    const pai = el.parentElement;
    if (el.localName === 'span' && el.getAttribute(ATR_CAMPO) === 'titulo' && pai?.classList.contains('ct')) {
      el.replaceWith(...Array.from(el.childNodes));
      pai.normalize();
    } else if (!desenvolver(el)) el.removeAttribute(ATR_CAMPO);
  }
}

// ---------- seleção ----------

function aplicarSelecao(ref: string | null): void {
  for (const e of Array.from(document.querySelectorAll(`[${ATR_SELECIONADO}]`))) e.removeAttribute(ATR_SELECIONADO);
  if (ref !== null) for (const p of pedacosDe(ref)) p.setAttribute(ATR_SELECIONADO, '');
}

/** Item (data-ref do card) ou linha de tabela (data-pmvni-linha) e campo (data-campo dentro dele) sob um alvo de evento; fora
 * deles, tudo nulo. */
function localizar(alvo: EventTarget | null): { ref: string | null; campo: string | null } {
  if (!(alvo instanceof Element)) return { ref: null, campo: null };
  const bloco = blocoDe(alvo);
  if (!bloco) return { ref: alvo.closest<HTMLElement>(SELETOR_FIGURAS)?.getAttribute('data-ref') ?? null, campo: null };
  const el = alvo.closest<HTMLElement>(`[${ATR_CAMPO}]`);
  const campo = el && bloco.el.contains(el) ? el.getAttribute(ATR_CAMPO) : null;
  return { ref: bloco.ref, campo: campoDoBloco(bloco.ref, campo) ? campo : null };
}

// ---------- edição no A4 ----------

/** Texto de um campo editável, lido como texto: quebras de linha viram espaço (o campo é de uma linha) e o espaço
 * rígido que o navegador põe ao digitar vira espaço comum. O que sobra de espaço nas pontas é com o host. */
function lerTexto(el: Node): string {
  let s = '';
  const visitar = (n: Node): void => {
    if (n.nodeType === Node.TEXT_NODE) { s += (n as Text).data; return; }
    if (n.nodeType !== Node.ELEMENT_NODE) return;
    const e = n as Element;
    if (e.localName === 'br') { s += '\n'; return; }
    if (/^(div|p|li)$/.test(e.localName) && s !== '' && !s.endsWith('\n')) s += '\n';
    e.childNodes.forEach(visitar);
  };
  el.childNodes.forEach(visitar);
  return s.replace(/[\r\n\u0085\u2028\u2029]+/g, ' ').replace(/\u00a0/g, ' ');
}

const umaLinha = (s: string): string => s.replace(/[\r\n\u0085\u2028\u2029]+/g, ' ');

function inserirTexto(texto: string): void {
  if (document.execCommand('insertText', false, texto)) return;
  const sel = window.getSelection();
  if (!sel || sel.rangeCount === 0) return;
  const r = sel.getRangeAt(0);
  r.deleteContents();
  const no = document.createTextNode(texto);
  r.insertNode(no);
  r.setStartAfter(no);
  r.collapse(true);
  sel.removeAllRanges();
  sel.addRange(r);
}

/** Termina a edição em curso (e a tela de pendência) devolvendo o DOM ao que era: texto e pedaços. */
function restaurarEdicao(): void {
  const e = edicao;
  if (!e) return;
  edicao = null;
  e.primeiro.removeAttribute('contenteditable');
  e.primeiro.removeAttribute(ATR_EDITANDO);
  e.primeiro.blur();
  e.primeiro.replaceChildren(...e.originais);
  e.demais.forEach((d, i) => { d.style.visibility = e.visibilidade[i] ?? ''; });
}

/** Põe o campo em edição: o primeiro pedaço recebe `texto` e fica `plaintext-only`; os outros pedaços do campo ficam ocultos. */
function iniciarEdicao(ref: string, campo: string, texto: string): void {
  const pedacos = pedacosDe(ref).flatMap((p) => {
    const el = p.querySelector<HTMLElement>(`[${ATR_CAMPO}="${CSS.escape(campo)}"]`);
    return el ? [el] : [];
  });
  const [primeiro, ...demais] = pedacos;
  if (!primeiro) throw new ErroComposicao('campo_inexistente', `O item "${ref}" não tem o campo "${campo}" marcado nas folhas.`);
  restaurarEdicao();
  edicao = {
    ref, campo, primeiro, demais, originais: Array.from(primeiro.childNodes),
    visibilidade: demais.map((d) => d.style.visibility), pendente: false,
  };
  primeiro.replaceChildren(document.createTextNode(texto));
  demais.forEach((d) => { d.style.visibility = 'hidden'; });
  primeiro.setAttribute('contenteditable', 'plaintext-only');
  primeiro.setAttribute(ATR_EDITANDO, '');
  const r = primeiro.getBoundingClientRect();
  if (r.bottom < 0 || r.top > window.innerHeight) primeiro.scrollIntoView({ block: 'center', behavior: 'instant' });
  primeiro.focus({ preventScroll: true });
  const sel = window.getSelection();
  const fim = document.createRange();
  fim.selectNodeContents(primeiro);
  fim.collapse(false);
  sel?.removeAllRanges();
  sel?.addRange(fim);
}

const editando = (): Edicao | null => (edicao && !edicao.pendente ? edicao : null);

/** Enter: sai do modo de edição, deixa o texto (puro) à vista até o próximo estado do host e devolve o que vai ao host. */
function concluirEdicao(): { ref: string; campo: string; texto: string } {
  const e = edicao!;
  const texto = lerTexto(e.primeiro);
  e.primeiro.removeAttribute('contenteditable');
  e.primeiro.removeAttribute(ATR_EDITANDO);
  e.primeiro.replaceChildren(document.createTextNode(texto));
  e.primeiro.blur();
  e.pendente = true;
  return { ref: e.ref, campo: e.campo, texto };
}

/** Rola até a linha de tabela (o primeiro pedaço visível), com folga acima. */
function rolarAteLinha(ref: string): void {
  const alvo = pedacosDe(ref)[0];
  if (!alvo) return;
  const r = alvo.getBoundingClientRect();
  if (r.top >= 0 && r.bottom <= window.innerHeight) return;
  window.scrollTo({ top: Math.max(0, window.scrollY + r.top - 48), behavior: 'instant' });
}

// ---------- âncora de rolagem ----------

interface Ancora { ref: string | null; deslocamentoPx: number }

/** O bloco `[data-ref]` que cruza o topo da janela (senão o primeiro abaixo dele) e a distância do topo do bloco ao topo da
 * janela, em px sem zoom (positiva quando o bloco começa acima do topo). Blocos em mais de uma folha: vale o 1º pedaço. */
function medirAncora(): Ancora {
  const blocos = Array.from(document.querySelectorAll<HTMLElement>('section.folha .area [data-ref]'))
    .filter((e) => e.getClientRects().length > 0);
  let atravessa: { e: HTMLElement; topo: number } | null = null;
  let abaixo: { e: HTMLElement; topo: number } | null = null;
  for (const e of blocos) {
    const r = e.getBoundingClientRect();
    if (r.top <= 0 && r.bottom > 0) { if (!atravessa || r.top > atravessa.topo) atravessa = { e, topo: r.top }; }
    else if (r.top > 0 && (!abaixo || r.top < abaixo.topo)) abaixo = { e, topo: r.top };
  }
  const escolhido = atravessa ?? abaixo;
  if (!escolhido) return { ref: null, deslocamentoPx: 0 };
  const ref = escolhido.e.getAttribute('data-ref')!;
  const primeiro = blocos.find((e) => e.getAttribute('data-ref') === ref) ?? escolhido.e;
  return { ref, deslocamentoPx: -primeiro.getBoundingClientRect().top / zoomAtual() };
}

/** Rola para que o bloco fique `deslocamentoPx` (sem zoom) acima do topo da janela; ref que não existe é ignorada. */
function restaurarAncora(a: { ref: string; deslocamentoPx: number }): void {
  const primeiro = Array.from(document.querySelectorAll<HTMLElement>('section.folha .area [data-ref]'))
    .find((e) => e.getAttribute('data-ref') === a.ref && e.getClientRects().length > 0);
  if (!primeiro) return;
  const topo = primeiro.getBoundingClientRect().top;
  window.scrollTo({ top: Math.max(0, window.scrollY + topo + a.deslocamentoPx * zoomAtual()), behavior: 'instant' });
}

// ---------- validação do que o host manda ----------

const ehObjeto = (x: unknown): x is Record<string, unknown> => typeof x === 'object' && x !== null && !Array.isArray(x);
const invalido = (m: string) => new ErroComposicao('estado_invalido', m);

interface EstadoEditor { selecao: string | null; campos: boolean; marcas: EstadoMarcas; ancora: { ref: string; deslocamentoPx: number } | null }

function lerEstado(d: Record<string, unknown>): EstadoEditor {
  if (d.selecao !== null && typeof d.selecao !== 'string') throw invalido('selecao deve ser texto ou nulo.');
  if (typeof d.campos !== 'boolean') throw invalido('campos deve ser verdadeiro ou falso.');
  const m = d.marcas;
  if (!ehObjeto(m) || typeof m.visiveis !== 'boolean') throw invalido('marcas.visiveis deve ser verdadeiro ou falso.');
  if (!Array.isArray(m.alterados) || m.alterados.some((r) => typeof r !== 'string')) throw invalido('marcas.alterados deve ser uma lista de refs.');
  if (!Array.isArray(m.excluidos)) throw invalido('marcas.excluidos deve ser uma lista.');
  const excluidos: Excluido[] = m.excluidos.map((x: unknown, i) => {
    if (!ehObjeto(x) || typeof x.id !== 'string' || typeof x.rotulo !== 'string' || typeof x.ancora !== 'string'
      || (x.lado !== 'antes' && x.lado !== 'depois')) throw invalido(`marcas.excluidos[${i}] invalido.`);
    return { id: x.id, rotulo: x.rotulo, ancora: x.ancora, lado: x.lado };
  });
  let ancora: EstadoEditor['ancora'] = null;
  if (d.ancora !== undefined && d.ancora !== null) {
    const a = d.ancora;
    if (!ehObjeto(a) || typeof a.ref !== 'string' || typeof a.deslocamentoPx !== 'number' || !Number.isFinite(a.deslocamentoPx)) {
      throw invalido('ancora deve ter ref (texto) e deslocamentoPx (número).');
    }
    ancora = { ref: a.ref, deslocamentoPx: a.deslocamentoPx };
  }
  return { selecao: d.selecao, campos: d.campos, marcas: { visiveis: m.visiveis, alterados: m.alterados as string[], excluidos }, ancora };
}

// ---------- atalhos ----------

/** Nome canônico do atalho (Ctrl+Shift+S, Alt+ArrowUp, Ctrl+Plus…) ou null. Mais e menos valem com ou sem Shift. */
function nomeDoAtalho(e: KeyboardEvent): string | null {
  if (e.metaKey) return null;
  let shift = e.shiftKey;
  let tecla: string;
  if (/^[a-z]$/i.test(e.key)) tecla = e.key.toUpperCase();
  else if (e.key === '+' || e.key === '=') { tecla = 'Plus'; shift = false; }
  else if (e.key === '-' || e.key === '_') { tecla = 'Minus'; shift = false; }
  else tecla = e.key;
  return [e.ctrlKey ? 'Ctrl' : '', e.altKey ? 'Alt' : '', shift ? 'Shift' : '', tecla].filter((p) => p !== '').join('+');
}

// ---------- registro ----------

export function registrarEdicao(registrar: Registrar, enviar: Enviar): void {
  const evento = (tipo: TipoWeb, dados: Record<string, unknown>): void => enviar(tipo, `evento-${++contadorEventos}`, dados);
  const ligar = (): void => { ativo = true; garantirCss(); };

  registrar('estadoEditor', (dados) => {
    const e = lerEstado(dados);
    ligar();
    if (edicao) {
      // Folhas trocadas: a edição ficou para trás. Edição pendente que sobrou: o host não a aplicou; volta ao original.
      if (!document.contains(edicao.primeiro)) edicao = null;
      else if (edicao.pendente || !e.campos) restaurarEdicao();
    }
    marcarLinhas(); // as linhas das tabelas se selecionam como os itens, com ou sem os campos marcados
    let camposMarcados = 0;
    if (e.campos) camposMarcados = marcarCampos();
    else desmarcarCampos();
    aplicarSelecao(e.selecao);
    definirGola(e.marcas.visiveis ? GOLA_COM_MARCAS_PX : GOLA_ESQUERDA_PX);
    const marcasDesenhadas = desenharMarcas(e.marcas, (id, x, y) => evento('excluidoClicado', { id, x, y }));
    if (e.ancora) restaurarAncora(e.ancora);
    return { tipo: 'estadoAplicado', dados: { camposMarcados, marcasDesenhadas } };
  });

  registrar('selecionar', (dados) => {
    const ref = dados.ref;
    if (ref !== null && typeof ref !== 'string') throw new ErroComposicao('selecao_invalida', 'ref deve ser texto ou nulo.');
    if (typeof dados.rolar !== 'boolean') throw new ErroComposicao('selecao_invalida', 'rolar deve ser verdadeiro ou falso.');
    if (ref !== null && ehLinha(ref)) marcarLinhas();
    if (ref !== null && pedacosDe(ref).length === 0) throw new ErroComposicao('ref_inexistente', `Nenhuma folha mostra o item "${ref}".`);
    ligar();
    aplicarSelecao(ref);
    if (ref !== null && dados.rolar) {
      if (ehLinha(ref)) rolarAteLinha(ref);
      else rolarAteRef(ref);
    }
    return { tipo: 'selecionado', dados: { ref, campo: null } };
  });

  registrar('editarCampo', (dados) => {
    if (typeof dados.ref !== 'string' || !campoDoBloco(dados.ref, dados.campo) || typeof dados.texto !== 'string') {
      throw new ErroComposicao('edicao_invalida', 'editarCampo pede ref (texto), campo (um dos cinco campos, ou o caminho do texto na linha da tabela) e texto.');
    }
    ligar();
    iniciarEdicao(dados.ref, dados.campo, dados.texto);
    return { tipo: 'edicaoIniciada', dados: { ref: dados.ref, campo: dados.campo } };
  });

  registrar('concluirEdicao', () => {
    if (!editando()) return { tipo: 'semEdicao', dados: {} };
    return { tipo: 'editar', dados: { ...concluirEdicao() } };
  });

  registrar('obterAncora', () => ({ tipo: 'ancora', dados: { ...medirAncora() } }));

  // --- mouse ---

  // Durante a edição o foco fica no campo: clicar fora dele não o abandona (Enter confirma, Esc cancela).
  document.addEventListener('mousedown', (e) => {
    const ed = editando();
    if (ed && !(e.target instanceof Node && ed.primeiro.contains(e.target))) e.preventDefault();
  }, true);

  document.addEventListener('click', (e) => {
    if (!ativo || edicao || e.button !== 0) return;
    if (e.target instanceof Element && e.target.closest(`#${ID_CAMADA}`)) return;
    const { ref, campo } = localizar(e.target);
    aplicarSelecao(ref);
    evento('selecionado', { ref, campo });
  });

  document.addEventListener('dblclick', (e) => {
    if (!ativo || edicao) return;
    const { ref, campo } = localizar(e.target);
    if (ref !== null && campo === null && e.target instanceof Element && e.target.closest(SELETOR_FIGURAS)) {
      evento('pedirEdicao', { ref, campo: null }); // figura: o host abre o editor de marcações
      return;
    }
    if (ref === null || campo === null) return;
    window.getSelection()?.removeAllRanges(); // o duplo clique selecionou uma palavra; o campo entra em edição a seguir
    evento('pedirEdicao', { ref, campo });
  });

  document.addEventListener('contextmenu', (e) => {
    if (!ativo) return;
    const ed = editando();
    if (ed && e.target instanceof Node && ed.primeiro.contains(e.target)) return; // recortar/copiar/colar do próprio campo
    e.preventDefault();
    if (edicao) return;
    const { ref, campo } = localizar(e.target);
    aplicarSelecao(ref);
    evento('menuContexto', { ref, campo, x: e.clientX, y: e.clientY });
  });

  // --- teclado ---

  window.addEventListener('keydown', (e) => {
    if (!ativo) return;
    const ed = editando();
    if (ed) {
      // Na edição, tudo (Ctrl+Z, Ctrl+Y, Delete, setas…) é do campo; ao host só vão Enter e Esc.
      if (e.isComposing || e.keyCode === 229) return;
      if (e.key === 'Enter') {
        e.preventDefault();
        e.stopPropagation();
        evento('editar', { ...concluirEdicao() });
      } else if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        const { ref, campo } = ed;
        restaurarEdicao();
        evento('edicaoCancelada', { ref, campo });
      }
      return;
    }
    if (edicao || e.isComposing) return;
    const nome = nomeDoAtalho(e);
    if (nome !== null && ATALHOS.has(nome)) {
      e.preventDefault();
      evento('atalho', { tecla: nome });
    }
  }, true);

  // O campo é de uma linha e de texto puro: quebras de linha não entram, e o colado vem só como texto simples.
  document.addEventListener('beforeinput', (e) => {
    const ed = editando();
    if (!ed || !(e.target instanceof Node) || !ed.primeiro.contains(e.target)) return;
    if (e.inputType === 'insertParagraph' || e.inputType === 'insertLineBreak') e.preventDefault();
    else if (e.inputType === 'insertText' && e.data && /[\r\n\u0085\u2028\u2029]/.test(e.data)) {
      e.preventDefault();
      inserirTexto(umaLinha(e.data));
    }
  }, true);

  document.addEventListener('paste', (e) => {
    const ed = editando();
    if (!ed || !(e.target instanceof Node) || !ed.primeiro.contains(e.target)) return;
    e.preventDefault();
    inserirTexto(umaLinha(e.clipboardData?.getData('text/plain') ?? ''));
  }, true);
}
