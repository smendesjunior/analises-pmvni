// Empacota src/index.ts em um IIFE para o Chromium do WebView2 e grava o resultado como recurso embutido do projeto C#.
import { build } from 'esbuild';
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
