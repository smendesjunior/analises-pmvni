// Opcoes do PDF.js 6.4.299 no host virtual leg.pmvni.invalid (desenho §11.4). Os tipos de pdfjs-dist so existem no
// typecheck (import type); em execucao o codigo usa globalThis.pdfjsLib, que o pdf.min.mjs vendorizado deixa no global.
import type * as PdfJs from 'pdfjs-dist';

export type BibliotecaPdfJs = typeof PdfJs;
export type OpcoesDocumento = Parameters<BibliotecaPdfJs['getDocument']>[0];

export const HOST_LEG = 'https://leg.pmvni.invalid';
export const WORKER_SRC = `${HOST_LEG}/pdfjs/build/pdf.worker.min.mjs`;

/** Aponta o worker para o arquivo vendorizado (a CSP so deixa esse). */
export function configurarPdfJs(pdfjs: BibliotecaPdfJs): void {
  pdfjs.GlobalWorkerOptions.workerSrc = WORKER_SRC;
}

/**
 * Opcoes de getDocument para um PDF servido pelo host. O JavaScript dos PDFs nunca roda: nao ha PDFScriptingManager nem
 * sandbox (enableScripting fica no padrao, falso), XFA desligado e fontes do sistema desligadas. A opcao isEvalSupported do
 * PDF.js antigo nao existe mais na 6.4.299 (a biblioteca nao usa eval nem new Function; a CSP sem unsafe-eval garante).
 */
export function opcoesDocumento(url: string): OpcoesDocumento {
  return {
    url,
    enableXfa: false,
    useSystemFonts: false,
    disableAutoFetch: true,
    rangeChunkSize: 1048576,
    standardFontDataUrl: `${HOST_LEG}/pdfjs/standard_fonts/`,
    cMapUrl: `${HOST_LEG}/pdfjs/cmaps/`,
    cMapPacked: true,
    wasmUrl: `${HOST_LEG}/pdfjs/wasm/`,
    iccUrl: `${HOST_LEG}/pdfjs/iccs/`,
    stopAtErrors: false,
  };
}
