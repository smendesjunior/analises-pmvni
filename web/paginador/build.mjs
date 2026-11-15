// 1) Empacota src/index.ts em um IIFE para o Chromium do WebView2 e grava o resultado como recurso embutido do projeto C#.
// 2) Legislacao (Etapa 2, Tarefa 17): empacota src/legislacao/extrator.ts (ESM, sem empacotar o pdf.js: o codigo usa
//    globalThis.pdfjsLib) em src/Pmvni.Legislacao/web/extrator.js e vendoriza os 283 arquivos de pdfjs-dist em
//    src/Pmvni.Legislacao/web/pdfjs/, com o VERSAO.json (versao, licenca e sha256 de cada arquivo).
// 3) Legislacao (Tarefa 18): empacota src/legislacao/leitor.ts (ESM) em src/Pmvni.Legislacao/web/leitor.js.
import { build } from 'esbuild';
import { createHash } from 'node:crypto';
import { cp, mkdir, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const aqui = path.dirname(fileURLToPath(import.meta.url));
const saida = path.resolve(aqui, '../../src/Pmvni.Composicao.Web/web/paginador.js');

await build({
  entryPoints: [path.join(aqui, 'src/index.ts')],
  outfile: saida,
  bundle: true,
  format: 'iife',
  globalName: 'Paginador',
  target: 'chrome120',
  legalComments: 'none',
  charset: 'utf8',
  loader: { '.css': 'text' }, // folha.css entra no bundle como texto
  logLevel: 'info',
});

// ---- Legislacao: extrator ----
const raizLegislacao = path.resolve(aqui, '../../src/Pmvni.Legislacao/web');

await build({
  entryPoints: [path.join(aqui, 'src/legislacao/extrator.ts')],
  outfile: path.join(raizLegislacao, 'extrator.js'),
  bundle: true,
  format: 'esm',
  target: 'chrome120',
  legalComments: 'none',
  charset: 'utf8',
  logLevel: 'info',
});

// ---- Legislacao: leitor (Tarefa 18) ----
// ESM, sem empacotar o pdf.js nem o pdf_viewer: o codigo usa globalThis.pdfjsLib e globalThis.pdfjsViewer.
await build({
  entryPoints: [path.join(aqui, 'src/legislacao/leitor.ts')],
  outfile: path.join(raizLegislacao, 'leitor.js'),
  bundle: true,
  format: 'esm',
  target: 'chrome120',
  legalComments: 'none',
  charset: 'utf8',
  logLevel: 'info',
});

// ---- Legislacao: PDF.js vendorizado ----
const VERSAO_PDFJS = '6.4.299';
const ARQUIVOS_ESPERADOS = 283; // desenho §11.4, medido em 15/11/2026 na listagem publicada do pacote
const BYTES_ESPERADOS = 4_754_750;

async function listar(raiz, relativo) {
  const pasta = path.join(raiz, relativo);
  const itens = await readdir(pasta, { withFileTypes: true });
  const arquivos = [];
  for (const item of itens) {
    const rel = `${relativo}/${item.name}`;
    if (item.isDirectory()) arquivos.push(...(await listar(raiz, rel)));
    else arquivos.push(rel);
  }
  return arquivos;
}

async function vendorizarPdfJs() {
  const origem = path.join(aqui, 'node_modules', 'pdfjs-dist');
  const pacote = JSON.parse(await readFile(path.join(origem, 'package.json'), 'utf8'));
  if (pacote.version !== VERSAO_PDFJS) {
    throw new Error(`node_modules/pdfjs-dist e a ${pacote.version}; o esperado e ${VERSAO_PDFJS} (rode npm ci).`);
  }
  // O que entra (nao entram: web/viewer.*, quickjs-eval, *_nowasm_fallback.js, versoes nao minificadas, .map, legacy/, types/).
  const arquivos = [
    'build/pdf.min.mjs', 'build/pdf.worker.min.mjs', 'web/pdf_viewer.mjs', 'web/pdf_viewer.css', 'LICENSE',
    'wasm/openjpeg.wasm', 'wasm/jbig2.wasm', 'wasm/qcms_bg.wasm',
  ];
  for (const pasta of ['web/images', 'standard_fonts', 'cmaps', 'iccs']) arquivos.push(...(await listar(origem, pasta)));
  arquivos.push(...(await readdir(path.join(origem, 'wasm'))).filter((n) => n.startsWith('LICENSE')).map((n) => `wasm/${n}`));
  arquivos.sort();

  const destino = path.join(raizLegislacao, 'pdfjs');
  await rm(destino, { recursive: true, force: true });
  const shas = {};
  let bytes = 0;
  for (const rel of arquivos) {
    const conteudo = await readFile(path.join(origem, rel));
    bytes += conteudo.length;
    shas[rel] = createHash('sha256').update(conteudo).digest('hex');
    await mkdir(path.dirname(path.join(destino, rel)), { recursive: true });
    await cp(path.join(origem, rel), path.join(destino, rel));
  }
  if (arquivos.length !== ARQUIVOS_ESPERADOS || bytes !== BYTES_ESPERADOS) {
    throw new Error(`pdfjs-dist ${VERSAO_PDFJS}: ${arquivos.length} arquivos e ${bytes} bytes; o desenho espera ${ARQUIVOS_ESPERADOS} e ${BYTES_ESPERADOS}. Confira o que mudou no pacote antes de mexer nesses numeros.`);
  }
  const versao = { pacote: 'pdfjs-dist', versao: pacote.version, licenca: pacote.license, arquivos: shas };
  await writeFile(path.join(destino, 'VERSAO.json'), JSON.stringify(versao, null, 2) + '\n', 'utf8');
  console.log(`  pdfjs-dist ${pacote.version}: ${arquivos.length} arquivos, ${bytes} bytes -> ${path.relative(path.resolve(aqui, '../..'), destino)}`);
  await stat(path.join(destino, 'VERSAO.json'));
}

await vendorizarPdfJs();
