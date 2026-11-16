// Plano de quebras (espelho exato de src/Pmvni.Composicao.Web/PlanoQuebras.cs; JSON em camelCase, nulos gravados).
// O editor (tela) e o PDF aplicam o MESMO plano: tela e PDF batem por construção.

export const VERSAO_PLANO = 1 as const;

/**
 * Posição do primeiro conteúdo de uma folha no fluxo original (filhos do <body> do relatório).
 * caminho: índices de filhos-ELEMENTO a partir do fluxo ([7, 2] = 3º filho do 8º bloco).
 * Sem deslocamentoTexto nem linha: a folha começa no início do elemento do caminho.
 * deslocamentoTexto: caractere (UTF-16, no textContent do elemento) que abre a primeira linha da folha.
 * linha: índice (0 = primeira) da linha dos tbody da tabela do caminho que abre a folha.
 * forcada: a quebra que abre esta folha é forçada (break-before/after: page…): a margem de cima do 1º bloco fica.
 *   Nas quebras não forçadas (e sempre falso na 1ª folha) ela é truncada.
 */
export interface PosicaoFluxo {
  caminho: number[];
  deslocamentoTexto: number | null;
  linha: number | null;
  forcada: boolean;
}

/**
 * Folha (1 = primeira) cujo conteúdo não coube na área útil; ref = data-ref do bloco (ou do último bloco identificado
 * antes). alturaMm: altura do conteúdo da folha. larguraMm: só no estouro de largura (conteúdo mais largo que
 * ENCOLHIMENTO_MAXIMO × área), a largura que o bloco pede; null no estouro de altura.
 */
export interface Estouro {
  folha: number;
  ref: string | null;
  alturaMm: number;
  larguraMm: number | null;
}

/** Bloco mais largo que a área (o documento sai reduzido pela escala de impressão): folha, ref, a escala que ele
 * pede sozinho (largura dele / largura da área) e a largura em mm, para o editor avisar. */
export interface AvisoLargura {
  folha: number;
  ref: string | null;
  escala: number;
  larguraMm: number;
}

export interface PlanoQuebras {
  versao: number;
  inicioDasFolhas: PosicaoFluxo[];
  estouros: Estouro[];
  sha256Html: string;
  /** Escala de impressão k (1 a 1,5) do shrink-to-fit do Chromium: a área é diagramada k vezes maior e pintada em 1/k
   *  (folha.css, --pmvni-k). Quem aplica o plano usa este valor, sem medir de novo. */
  escala: number;
  avisosLargura: AvisoLargura[];
  /** Figuras reduzidas para caber na sobra da folha anterior (o paginador decide sozinho quantas cabem por folha): caminho do
   *  bloco .fig no fluxo e a altura máxima da imagem em px CSS. null/ausente = nenhuma. Quem aplica o plano aplica estas alturas. */
  figuras?: AjusteFigura[] | null;
}

/** Altura máxima (px CSS) imposta à imagem (.fi img) de uma figura (.fig) do fluxo. */
export interface AjusteFigura { caminho: number[]; alturaPx: number }

/** Resposta de `compor`. */
export interface Composto {
  plano: PlanoQuebras;
  folhas: number;
  refsPorFolha: string[][];
}

/** Erro com código para a ponte (a mensagem `erro` sai com este `codigo`). */
export class ErroComposicao extends Error {
  constructor(readonly codigo: string, mensagem: string) {
    super(mensagem);
  }
}

export const posicao = (caminho: number[], deslocamentoTexto: number | null = null, linha: number | null = null,
  forcada = false): PosicaoFluxo => ({ caminho, deslocamentoTexto, linha, forcada });

/** Início do documento: a 1ª folha começa no 1º filho do fluxo. */
export const INICIO_DO_FLUXO: PosicaoFluxo = posicao([0]);

/** Posição relativa a um elemento do caminho: o resto do caminho e o deslocamento/linha do elemento final. */
export function descer(p: PosicaoFluxo): PosicaoFluxo {
  return { caminho: p.caminho.slice(1), deslocamentoTexto: p.deslocamentoTexto, linha: p.linha, forcada: p.forcada };
}

/** A posição está DENTRO do elemento p.caminho[0] (e não no seu início)? */
export function dentroDoPrimeiro(p: PosicaoFluxo): boolean {
  return p.caminho.length > 1 || p.deslocamentoTexto !== null || p.linha !== null;
}

/** Margem de cima do 1º bloco da folha `indice` (0 = primeira) é truncada? Só depende do plano. */
export const truncaTopo = (inicio: PosicaoFluxo, indice: number): boolean => indice > 0 && !inicio.forcada;

/** Escala de impressão máxima aceita num plano (espelho de PlanoQuebras.EscalaMaxima e de ENCOLHIMENTO_MAXIMO). */
export const ESCALA_MAXIMA_PLANO = 1.5;

/**
 * Lê e valida um plano vindo da ponte (como PlanoQuebras.DeJson): versão 1, escala finita em [1; 1,5], ao menos uma
 * folha, caminhos de inteiros ≥ 0, deslocamento/linha inteiros ≥ 0 ou null. Lança ErroComposicao('plano_invalido').
 */
export function lerPlano(bruto: unknown): PlanoQuebras {
  const falha = (motivo: string): never => { throw new ErroComposicao('plano_invalido', 'Plano de quebras inválido: ' + motivo + '.'); };
  const obj = (x: unknown): x is Record<string, unknown> => typeof x === 'object' && x !== null && !Array.isArray(x);
  const inteiro = (x: unknown) => typeof x === 'number' && Number.isInteger(x) && x >= 0;
  const opcional = (x: unknown) => x === null || inteiro(x);
  if (!obj(bruto)) return falha('não é um objeto');
  const p = bruto;
  if (p.versao !== VERSAO_PLANO) falha('versão ' + String(p.versao));
  if (typeof p.escala !== 'number' || !Number.isFinite(p.escala) || p.escala < 1 || p.escala > ESCALA_MAXIMA_PLANO) falha('escala');
  if (typeof p.sha256Html !== 'string' || p.sha256Html === '') falha('sha256Html');
  if (!Array.isArray(p.inicioDasFolhas) || p.inicioDasFolhas.length === 0) falha('inicioDasFolhas');
  if (!Array.isArray(p.estouros) || !Array.isArray(p.avisosLargura)) falha('estouros/avisosLargura');
  const inicios = (p.inicioDasFolhas as unknown[]).map((i, n): PosicaoFluxo => {
    if (!obj(i) || !Array.isArray(i.caminho) || i.caminho.length === 0 || !i.caminho.every(inteiro)
      || !opcional(i.deslocamentoTexto ?? null) || !opcional(i.linha ?? null)) return falha(`inicioDasFolhas[${n}]`);
    return posicao([...(i.caminho as number[])], (i.deslocamentoTexto ?? null) as number | null, (i.linha ?? null) as number | null,
      i.forcada === true);
  });
  return {
    versao: VERSAO_PLANO, inicioDasFolhas: inicios, estouros: p.estouros as Estouro[], sha256Html: p.sha256Html as string,
    escala: p.escala as number, avisosLargura: p.avisosLargura as AvisoLargura[],
    figuras: Array.isArray(p.figuras)
      ? (p.figuras as unknown[]).map((a, n): AjusteFigura => {
        if (!obj(a) || !Array.isArray(a.caminho) || a.caminho.length === 0 || !a.caminho.every(inteiro)
          || typeof a.alturaPx !== 'number' || !Number.isFinite(a.alturaPx) || a.alturaPx <= 0) return falha(`figuras[${n}]`);
        return { caminho: [...(a.caminho as number[])], alturaPx: a.alturaPx };
      })
      : null,
  };
}
