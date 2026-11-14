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
}

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
