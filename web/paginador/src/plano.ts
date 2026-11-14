// Plano de quebras (espelho exato de src/Pmvni.Composicao.Web/PlanoQuebras.cs; JSON em camelCase, nulos gravados).
// O editor (tela) e o PDF aplicam o MESMO plano: tela e PDF batem por construção.

export const VERSAO_PLANO = 1 as const;

/**
 * Posição do primeiro conteúdo de uma folha no fluxo original (filhos do <body> do relatório).
 * caminho: índices de filhos-ELEMENTO a partir do fluxo ([7, 2] = 3º filho do 8º bloco).
 * Sem deslocamentoTexto nem linha: a folha começa no início do elemento do caminho.
 * deslocamentoTexto: caractere (UTF-16, no textContent do elemento) que abre a primeira linha da folha.
 * linha: índice (0 = primeira) da linha dos tbody da tabela do caminho que abre a folha.
 */
export interface PosicaoFluxo {
  caminho: number[];
  deslocamentoTexto: number | null;
  linha: number | null;
}

/** Folha (1 = primeira) que não coube na área útil; ref = data-ref do bloco (ou do último bloco identificado antes). */
export interface Estouro {
  folha: number;
  ref: string | null;
  alturaMm: number;
}

export interface PlanoQuebras {
  versao: number;
  inicioDasFolhas: PosicaoFluxo[];
  estouros: Estouro[];
  sha256Html: string;
  /** Escala de impressão k (>= 1) do shrink-to-fit do Chromium: a área é diagramada k vezes maior e pintada em 1/k
   *  (folha.css, --pmvni-k). Quem aplica o plano usa este valor, sem medir de novo. */
  escala: number;
}

/** Resposta de `compor`. */
export interface Composto {
  plano: PlanoQuebras;
  folhas: number;
  refsPorFolha: string[][];
}

export const posicao = (caminho: number[], deslocamentoTexto: number | null = null, linha: number | null = null): PosicaoFluxo =>
  ({ caminho, deslocamentoTexto, linha });

/** Início do documento: a 1ª folha começa no 1º filho do fluxo. */
export const INICIO_DO_FLUXO: PosicaoFluxo = posicao([0]);

/** Posição relativa a um elemento do caminho: o resto do caminho e o deslocamento/linha do elemento final. */
export function descer(p: PosicaoFluxo): PosicaoFluxo {
  return { caminho: p.caminho.slice(1), deslocamentoTexto: p.deslocamentoTexto, linha: p.linha };
}

/** A posição está DENTRO do elemento p.caminho[0] (e não no seu início)? */
export function dentroDoPrimeiro(p: PosicaoFluxo): boolean {
  return p.caminho.length > 1 || p.deslocamentoTexto !== null || p.linha !== null;
}
