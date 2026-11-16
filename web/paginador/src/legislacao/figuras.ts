// Figuras dos PDFs anexados para a leitura padronizada (correcoes 2026-11-15). Para cada pagina: a lista de operacoes do PDF.js
// diz onde ha imagens (objetos de imagem, imagens embutidas, mascaras, grupos e repeticoes) e desenhos vetoriais; a pagina e
// desenhada num canvas e cada regiao e recortada dele (assim imagens embutidas, mascaras e vetores saem iguais ao PDF). Quando a
// pagina tem conteudo grafico que nao da para localizar (degrade, erro na lista de operacoes), vai a pagina inteira; se nem ela
// puder ser desenhada, vai so o aviso ("falhou"). Cada figura leva `linha`: o indice da linha do texto da pagina (o mesmo texto do
// indice, separado por "\n") antes da qual ela entra.

/** Item de texto do PDF.js (so o que importa aqui). */
export interface ItemTexto {
  str: string;
  hasEOL?: boolean;
  transform: number[];
  width: number;
  height: number;
}

/** O texto da pagina como o extrator sempre devolveu, a altura (base, em pontos do PDF) de cada linha e as caixas dos itens. */
export interface TextoPagina {
  texto: string;
  linhasY: number[];
  itens: { caixa: Caixa; letras: number }[];
}

/** Retangulo em pontos do PDF (y para cima). */
export interface Caixa { x0: number; y0: number; x1: number; y1: number }

export type OrigemFigura = 'imagem' | 'desenho' | 'pagina' | 'falhou';

export interface FiguraWeb {
  linha: number;
  x: number; y: number; w: number; h: number; // fracoes da pagina, do canto de cima a esquerda
  largura: number; // largura da regiao em pontos do PDF
  origem: OrigemFigura;
  tipo: string; // image/png | image/jpeg | '' (falhou)
  dados: string; // base64, sem o prefixo data:
}

type Matriz = [number, number, number, number, number, number];
const IDENTIDADE: Matriz = [1, 0, 0, 1, 0, 0];

/** Texto da pagina = `str` dos itens na ordem, com "\n" depois de cada item com `hasEOL` (exatamente como o extrator de texto). */
export function montarTexto(itens: readonly unknown[]): TextoPagina {
  let texto = '';
  const linhasY: number[] = [NaN];
  const caixas: { caixa: Caixa; letras: number }[] = [];
  let linha = 0;
  for (const bruto of itens) {
    if (typeof bruto !== 'object' || bruto === null || !('str' in bruto)) continue;
    const item = bruto as ItemTexto;
    texto += item.str;
    const t = item.transform;
    if (item.str.trim() !== '' && Array.isArray(t) && t.length >= 6) {
      const x = Number(t[4]);
      const y = Number(t[5]);
      if (Number.isNaN(linhasY[linha] ?? NaN)) linhasY[linha] = y;
      const alt = Math.abs(Number(item.height) || Math.abs(Number(t[3])) || 0);
      caixas.push({ caixa: { x0: x, y0: y, x1: x + Math.abs(Number(item.width) || 0), y1: y + alt }, letras: item.str.replace(/\s/g, '').length });
    }
    if (item.hasEOL) {
      texto += '\n';
      linha++;
      linhasY[linha] = NaN;
    }
  }
  return { texto, linhasY, itens: caixas };
}

function compor(m: Matriz, n: ArrayLike<number>): Matriz {
  const [a, b, c, d, e, f] = m;
  const n0 = Number(n[0]), n1 = Number(n[1]), n2 = Number(n[2]), n3 = Number(n[3]), n4 = Number(n[4]), n5 = Number(n[5]);
  return [a * n0 + c * n1, b * n0 + d * n1, a * n2 + c * n3, b * n2 + d * n3, a * n4 + c * n5 + e, b * n4 + d * n5 + f];
}

function caixaDe(m: Matriz, x0: number, y0: number, x1: number, y1: number): Caixa | null {
  const pontos = [[x0, y0], [x1, y0], [x0, y1], [x1, y1]].map(([x, y]) => [m[0] * x! + m[2] * y! + m[4], m[1] * x! + m[3] * y! + m[5]]);
  const xs = pontos.map((p) => p[0]!);
  const ys = pontos.map((p) => p[1]!);
  const c = { x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) };
  return [c.x0, c.y0, c.x1, c.y1].every(Number.isFinite) ? c : null;
}

const area = (c: Caixa): number => Math.max(0, c.x1 - c.x0) * Math.max(0, c.y1 - c.y0);
const perto = (a: Caixa, b: Caixa, folga: number): boolean =>
  a.x0 <= b.x1 + folga && b.x0 <= a.x1 + folga && a.y0 <= b.y1 + folga && b.y0 <= a.y1 + folga;
const unir = (a: Caixa, b: Caixa): Caixa => ({ x0: Math.min(a.x0, b.x0), y0: Math.min(a.y0, b.y0), x1: Math.max(a.x1, b.x1), y1: Math.max(a.y1, b.y1) });
const cortar = (c: Caixa, v: Caixa): Caixa | null => {
  const r = { x0: Math.max(c.x0, v.x0), y0: Math.max(c.y0, v.y0), x1: Math.min(c.x1, v.x1), y1: Math.min(c.y1, v.y1) };
  return r.x1 > r.x0 && r.y1 > r.y0 ? r : null;
};

interface Regiao { caixa: Caixa; imagem: boolean }

/** Junta as regioes que se tocam (com folga), ate nao sobrar nenhuma para juntar. */
function juntar(regioes: Regiao[], folga: number): Regiao[] {
  const r = regioes.slice();
  for (let mudou = true; mudou;) {
    mudou = false;
    for (let i = 0; i < r.length && !mudou; i++) {
      for (let j = i + 1; j < r.length; j++) {
        if (perto(r[i]!.caixa, r[j]!.caixa, folga)) {
          r[i] = { caixa: unir(r[i]!.caixa, r[j]!.caixa), imagem: r[i]!.imagem || r[j]!.imagem };
          r.splice(j, 1);
          mudou = true;
          break;
        }
      }
    }
  }
  return r;
}

/** O que a lista de operacoes diz da pagina: caixas de imagem, caixas de desenho e se ha grafico sem lugar conhecido. */
export interface Graficos { imagens: Caixa[]; desenhos: Caixa[]; semLugar: boolean }

/** Percorre a lista de operacoes acompanhando a matriz atual (save/restore/transform e formularios). */
export function lerOperacoes(fnArray: ArrayLike<number>, argsArray: ArrayLike<unknown>, OPS: Record<string, number>): Graficos {
  const r: Graficos = { imagens: [], desenhos: [], semLugar: false };
  let m: Matriz = IDENTIDADE;
  const pilha: Matriz[] = [];
  const pintura = new Set([OPS.stroke, OPS.closeStroke, OPS.fill, OPS.eoFill, OPS.fillStroke, OPS.eoFillStroke, OPS.closeFillStroke, OPS.closeEOFillStroke]);
  const imagem = (mm: Matriz): void => {
    const c = caixaDe(mm, 0, 0, 1, 1);
    if (c) r.imagens.push(c); else r.semLugar = true;
  };
  for (let i = 0; i < fnArray.length; i++) {
    const fn = fnArray[i];
    const args = (argsArray[i] ?? []) as unknown[];
    try {
      switch (fn) {
        case OPS.save: case OPS.beginGroup: pilha.push(m); break;
        case OPS.restore: case OPS.endGroup: case OPS.paintFormXObjectEnd: m = pilha.pop() ?? m; break;
        case OPS.transform: m = compor(m, args as number[]); break;
        case OPS.paintFormXObjectBegin: {
          pilha.push(m);
          const matriz = args[0] as ArrayLike<number> | null | undefined;
          if (matriz && matriz.length >= 6) m = compor(m, matriz);
          break;
        }
        case OPS.paintImageXObject: case OPS.paintInlineImageXObject: case OPS.paintImageMaskXObject: case OPS.paintSolidColorImageMask:
          imagem(m);
          break;
        case OPS.paintImageXObjectRepeat: {
          const [, sx, sy, pos] = args as [unknown, number, number, ArrayLike<number>];
          for (let k = 0; k + 1 < pos.length; k += 2) imagem(compor(m, [sx, 0, 0, sy, pos[k]!, pos[k + 1]!]));
          break;
        }
        case OPS.paintImageMaskXObjectRepeat: {
          const [, sx, kx, ky, sy, pos] = args as [unknown, number, number, number, number, ArrayLike<number>];
          for (let k = 0; k + 1 < pos.length; k += 2) imagem(compor(m, [sx, kx, ky, sy, pos[k]!, pos[k + 1]!]));
          break;
        }
        case OPS.paintImageMaskXObjectGroup: case OPS.paintInlineImageXObjectGroup: {
          const lista = (fn === OPS.paintImageMaskXObjectGroup ? args[0] : args[1]) as { transform?: ArrayLike<number> }[] | undefined;
          if (!lista || lista.length === 0) { r.semLugar = true; break; }
          for (const item of lista) {
            if (item?.transform && item.transform.length >= 6) imagem(compor(m, item.transform)); else r.semLugar = true;
          }
          break;
        }
        case OPS.constructPath: {
          const [op, , minMax] = args as [number, unknown, ArrayLike<number> | null | undefined];
          if (!pintura.has(op)) break; // so recorte (clip) ou caminho sem pintura
          if (!minMax || minMax.length < 4) { r.semLugar = true; break; }
          const c = caixaDe(m, Number(minMax[0]), Number(minMax[1]), Number(minMax[2]), Number(minMax[3]));
          if (c) r.desenhos.push(c); else r.semLugar = true;
          break;
        }
        case OPS.shadingFill:
          r.semLugar = true; // degrade sem caixa conhecida: a pagina inteira garante que ele aparece
          break;
        default:
          break;
      }
    } catch {
      r.semLugar = true;
    }
  }
  return r;
}

/** As regioes a recortar (em pontos do PDF), na ordem de cima para baixo, a partir do que a lista de operacoes achou. */
export function regioes(g: Graficos, vista: Caixa, texto: TextoPagina): Regiao[] {
  const areaPagina = area(vista);
  const letrasDentro = (c: Caixa): number =>
    texto.itens.reduce((n, it) => {
      const cx = (it.caixa.x0 + it.caixa.x1) / 2;
      const cy = (it.caixa.y0 + it.caixa.y1) / 2;
      return cx >= c.x0 && cx <= c.x1 && cy >= c.y0 && cy <= c.y1 ? n + it.letras : n;
    }, 0);
  const letrasPagina = texto.itens.reduce((n, it) => n + it.letras, 0);

  // Imagens: no tamanho em que aparecem (as minusculas, como marcadores de lista, ficam de fora).
  let imagens: Regiao[] = g.imagens
    .map((c) => cortar(c, vista))
    .filter((c): c is Caixa => c !== null && c.x1 - c.x0 >= 4 && c.y1 - c.y0 >= 4)
    .map((caixa) => ({ caixa, imagem: true }));
  imagens = juntar(imagens, 2)
    // Imagem de fundo do tamanho da pagina numa pagina com texto (papel timbrado, digitalizacao com texto reconhecido): nao e figura.
    .filter((r) => !(area(r.caixa) >= 0.7 * areaPagina && letrasPagina >= 200));

  // Desenhos: os tracos que se tocam viram uma regiao; fica a que e grande o bastante e tem pouco texto (diagrama, nao tabela).
  // Linhas finas tem area zero: a caixa de cada traco ganha meia folga antes de cortar pela pagina e de juntar.
  let desenhos: Regiao[] = g.desenhos
    .map((c) => cortar({ x0: c.x0 - 0.5, y0: c.y0 - 0.5, x1: c.x1 + 0.5, y1: c.y1 + 0.5 }, vista))
    .filter((c): c is Caixa => c !== null && area(c) < 0.6 * areaPagina) // moldura da pagina inteira nao e desenho
    .map((caixa) => ({ caixa, imagem: false }));
  // Desenho muito detalhado (mapa, planta): milhares de tracos viram uma regiao so, sem a junção par a par (lenta demais).
  if (desenhos.length > 800) desenhos = [{ caixa: desenhos.map((r) => r.caixa).reduce(unir), imagem: false }];
  desenhos = juntar(desenhos, 6)
    .filter((r) => {
      const w = r.caixa.x1 - r.caixa.x0;
      const h = r.caixa.y1 - r.caixa.y0;
      const a = area(r.caixa);
      return w >= 24 && h >= 24 && a >= 0.015 * areaPagina && a <= 0.95 * areaPagina && letrasDentro(r.caixa) * 400 < a;
    });

  return juntar([...imagens, ...desenhos], 2)
    .map((r) => ({ ...r, caixa: cortar({ x0: r.caixa.x0 - 2, y0: r.caixa.y0 - 2, x1: r.caixa.x1 + 2, y1: r.caixa.y1 + 2 }, vista) ?? r.caixa }))
    .sort((a, b) => b.caixa.y1 - a.caixa.y1);
}

/** Indice da linha do texto antes da qual entra uma figura cujo topo esta em `topo` (pontos do PDF, y para cima). */
export function linhaDaFigura(linhasY: readonly number[], topo: number): number {
  for (let k = 0; k < linhasY.length; k++) {
    const y = linhasY[k]!;
    if (!Number.isNaN(y) && y <= topo) return k;
  }
  return linhasY.length;
}
