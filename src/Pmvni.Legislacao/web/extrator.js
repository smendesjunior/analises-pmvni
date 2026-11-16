// src/legislacao/figuras.ts
var IDENTIDADE = [1, 0, 0, 1, 0, 0];
function montarTexto(itens) {
  let texto = "";
  const linhasY = [NaN];
  const caixas = [];
  let linha = 0;
  for (const bruto of itens) {
    if (typeof bruto !== "object" || bruto === null || !("str" in bruto)) continue;
    const item = bruto;
    texto += item.str;
    const t = item.transform;
    if (item.str.trim() !== "" && Array.isArray(t) && t.length >= 6) {
      const x = Number(t[4]);
      const y = Number(t[5]);
      if (Number.isNaN(linhasY[linha] ?? NaN)) linhasY[linha] = y;
      const alt = Math.abs(Number(item.height) || Math.abs(Number(t[3])) || 0);
      caixas.push({ caixa: { x0: x, y0: y, x1: x + Math.abs(Number(item.width) || 0), y1: y + alt }, letras: item.str.replace(/\s/g, "").length });
    }
    if (item.hasEOL) {
      texto += "\n";
      linha++;
      linhasY[linha] = NaN;
    }
  }
  return { texto, linhasY, itens: caixas };
}
function compor(m, n) {
  const [a, b, c, d, e, f] = m;
  const n0 = Number(n[0]), n1 = Number(n[1]), n2 = Number(n[2]), n3 = Number(n[3]), n4 = Number(n[4]), n5 = Number(n[5]);
  return [a * n0 + c * n1, b * n0 + d * n1, a * n2 + c * n3, b * n2 + d * n3, a * n4 + c * n5 + e, b * n4 + d * n5 + f];
}
function caixaDe(m, x0, y0, x1, y1) {
  const pontos = [[x0, y0], [x1, y0], [x0, y1], [x1, y1]].map(([x, y]) => [m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]]);
  const xs = pontos.map((p) => p[0]);
  const ys = pontos.map((p) => p[1]);
  const c = { x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) };
  return [c.x0, c.y0, c.x1, c.y1].every(Number.isFinite) ? c : null;
}
var area = (c) => Math.max(0, c.x1 - c.x0) * Math.max(0, c.y1 - c.y0);
var perto = (a, b, folga) => a.x0 <= b.x1 + folga && b.x0 <= a.x1 + folga && a.y0 <= b.y1 + folga && b.y0 <= a.y1 + folga;
var unir = (a, b) => ({ x0: Math.min(a.x0, b.x0), y0: Math.min(a.y0, b.y0), x1: Math.max(a.x1, b.x1), y1: Math.max(a.y1, b.y1) });
var cortar = (c, v) => {
  const r = { x0: Math.max(c.x0, v.x0), y0: Math.max(c.y0, v.y0), x1: Math.min(c.x1, v.x1), y1: Math.min(c.y1, v.y1) };
  return r.x1 > r.x0 && r.y1 > r.y0 ? r : null;
};
function juntar(regioes2, folga) {
  const r = regioes2.slice();
  for (let mudou = true; mudou; ) {
    mudou = false;
    for (let i = 0; i < r.length && !mudou; i++) {
      for (let j = i + 1; j < r.length; j++) {
        if (perto(r[i].caixa, r[j].caixa, folga)) {
          r[i] = { caixa: unir(r[i].caixa, r[j].caixa), imagem: r[i].imagem || r[j].imagem };
          r.splice(j, 1);
          mudou = true;
          break;
        }
      }
    }
  }
  return r;
}
function lerOperacoes(fnArray, argsArray, OPS) {
  const r = { imagens: [], desenhos: [], semLugar: false };
  let m = IDENTIDADE;
  const pilha = [];
  const pintura = /* @__PURE__ */ new Set([OPS.stroke, OPS.closeStroke, OPS.fill, OPS.eoFill, OPS.fillStroke, OPS.eoFillStroke, OPS.closeFillStroke, OPS.closeEOFillStroke]);
  const imagem = (mm) => {
    const c = caixaDe(mm, 0, 0, 1, 1);
    if (c) r.imagens.push(c);
    else r.semLugar = true;
  };
  for (let i = 0; i < fnArray.length; i++) {
    const fn = fnArray[i];
    const args = argsArray[i] ?? [];
    try {
      switch (fn) {
        case OPS.save:
        case OPS.beginGroup:
          pilha.push(m);
          break;
        case OPS.restore:
        case OPS.endGroup:
        case OPS.paintFormXObjectEnd:
          m = pilha.pop() ?? m;
          break;
        case OPS.transform:
          m = compor(m, args);
          break;
        case OPS.paintFormXObjectBegin: {
          pilha.push(m);
          const matriz = args[0];
          if (matriz && matriz.length >= 6) m = compor(m, matriz);
          break;
        }
        case OPS.paintImageXObject:
        case OPS.paintInlineImageXObject:
        case OPS.paintImageMaskXObject:
        case OPS.paintSolidColorImageMask:
          imagem(m);
          break;
        case OPS.paintImageXObjectRepeat: {
          const [, sx, sy, pos] = args;
          for (let k = 0; k + 1 < pos.length; k += 2) imagem(compor(m, [sx, 0, 0, sy, pos[k], pos[k + 1]]));
          break;
        }
        case OPS.paintImageMaskXObjectRepeat: {
          const [, sx, kx, ky, sy, pos] = args;
          for (let k = 0; k + 1 < pos.length; k += 2) imagem(compor(m, [sx, kx, ky, sy, pos[k], pos[k + 1]]));
          break;
        }
        case OPS.paintImageMaskXObjectGroup:
        case OPS.paintInlineImageXObjectGroup: {
          const lista = fn === OPS.paintImageMaskXObjectGroup ? args[0] : args[1];
          if (!lista || lista.length === 0) {
            r.semLugar = true;
            break;
          }
          for (const item of lista) {
            if (item?.transform && item.transform.length >= 6) imagem(compor(m, item.transform));
            else r.semLugar = true;
          }
          break;
        }
        case OPS.constructPath: {
          const [op, , minMax] = args;
          if (!pintura.has(op)) break;
          if (!minMax || minMax.length < 4) {
            r.semLugar = true;
            break;
          }
          const c = caixaDe(m, Number(minMax[0]), Number(minMax[1]), Number(minMax[2]), Number(minMax[3]));
          if (c) r.desenhos.push(c);
          else r.semLugar = true;
          break;
        }
        case OPS.shadingFill:
          r.semLugar = true;
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
function regioes(g, vista, texto) {
  const areaPagina = area(vista);
  const letrasDentro = (c) => texto.itens.reduce((n, it) => {
    const cx = (it.caixa.x0 + it.caixa.x1) / 2;
    const cy = (it.caixa.y0 + it.caixa.y1) / 2;
    return cx >= c.x0 && cx <= c.x1 && cy >= c.y0 && cy <= c.y1 ? n + it.letras : n;
  }, 0);
  const letrasPagina = texto.itens.reduce((n, it) => n + it.letras, 0);
  let imagens = g.imagens.map((c) => cortar(c, vista)).filter((c) => c !== null && c.x1 - c.x0 >= 4 && c.y1 - c.y0 >= 4).map((caixa) => ({ caixa, imagem: true }));
  imagens = juntar(imagens, 2).filter((r) => !(area(r.caixa) >= 0.7 * areaPagina && letrasPagina >= 200));
  let desenhos = g.desenhos.map((c) => cortar({ x0: c.x0 - 0.5, y0: c.y0 - 0.5, x1: c.x1 + 0.5, y1: c.y1 + 0.5 }, vista)).filter((c) => c !== null && area(c) < 0.6 * areaPagina).map((caixa) => ({ caixa, imagem: false }));
  if (desenhos.length > 800) desenhos = [{ caixa: desenhos.map((r) => r.caixa).reduce(unir), imagem: false }];
  desenhos = juntar(desenhos, 6).filter((r) => {
    const w = r.caixa.x1 - r.caixa.x0;
    const h = r.caixa.y1 - r.caixa.y0;
    const a = area(r.caixa);
    return w >= 24 && h >= 24 && a >= 0.015 * areaPagina && a <= 0.95 * areaPagina && letrasDentro(r.caixa) * 400 < a;
  });
  return juntar([...imagens, ...desenhos], 2).map((r) => ({ ...r, caixa: cortar({ x0: r.caixa.x0 - 2, y0: r.caixa.y0 - 2, x1: r.caixa.x1 + 2, y1: r.caixa.y1 + 2 }, vista) ?? r.caixa })).sort((a, b) => b.caixa.y1 - a.caixa.y1);
}
function linhaDaFigura(linhasY, topo) {
  for (let k = 0; k < linhasY.length; k++) {
    const y = linhasY[k];
    if (!Number.isNaN(y) && y <= topo) return k;
  }
  return linhasY.length;
}

// src/legislacao/mensagens.ts
var VERSAO = 1;
var TIPOS_HOST = ["extrair", "cancelar", "figuras"];
var TIPOS_WEB = ["pronto", "progresso", "textoPagina", "cspViolacao", "extraido", "erro", "figurasPagina", "figurasExtraidas"];
function ehObjeto(x) {
  return typeof x === "object" && x !== null && !Array.isArray(x);
}
function ler(bruto, tipos) {
  if (!ehObjeto(bruto)) return { ok: false, motivo: "mensagem nao e um objeto", id: null };
  const id = typeof bruto.id === "string" && bruto.id !== "" ? bruto.id : null;
  const falha = (motivo) => ({ ok: false, motivo, id });
  if (!("v" in bruto)) return falha('campo "v" faltando');
  if (bruto.v !== VERSAO) return falha("versao nao suportada: " + String(bruto.v));
  if (typeof bruto.tipo !== "string") return falha('campo "tipo" faltando');
  if (!tipos.includes(bruto.tipo)) return falha("tipo desconhecido: " + bruto.tipo);
  if (id === null) return falha('campo "id" faltando');
  if (!ehObjeto(bruto.dados)) return falha('campo "dados" faltando ou invalido');
  return { ok: true, msg: { v: VERSAO, tipo: bruto.tipo, id, dados: bruto.dados } };
}
var lerMensagemHost = (bruto) => ler(bruto, TIPOS_HOST);
var lerMensagemWeb = (bruto) => ler(bruto, TIPOS_WEB);

// src/legislacao/opcoes-pdfjs.ts
var HOST_LEG = "https://leg.pmvni.invalid";
var WORKER_SRC = `${HOST_LEG}/pdfjs/build/pdf.worker.min.mjs`;
function configurarPdfJs(pdfjs2) {
  pdfjs2.GlobalWorkerOptions.workerSrc = WORKER_SRC;
}
function opcoesDocumento(url) {
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
    stopAtErrors: false
  };
}

// src/legislacao/extrator.ts
var webview = window.chrome?.webview;
var pdfjs = globalThis.pdfjsLib;
var sequencia = 0;
function enviar(tipo, id, dados = {}) {
  const m = { v: VERSAO, tipo, id, dados };
  const r = lerMensagemWeb(m);
  if (!r.ok) {
    console.error("[extrator] mensagem para o host descartada: " + r.motivo);
    return;
  }
  webview?.postMessage(m);
}
function evento(tipo, extracao, dados) {
  enviar(tipo, `evento-${++sequencia}`, extracao === null ? dados : { extracao, ...dados });
}
var Cancelada = class extends Error {
};
function novaExtracao(id) {
  let rejeitar = () => {
  };
  const aoCancelar = new Promise((_, falhar) => {
    rejeitar = falhar;
  });
  aoCancelar.catch(() => {
  });
  const estado = {
    id,
    cancelada: false,
    tarefa: null,
    aoCancelar,
    cancelar() {
      if (estado.cancelada) return;
      estado.cancelada = true;
      rejeitar(new Cancelada());
    }
  };
  return estado;
}
var TempoEsgotado = class extends Error {
};
function ate(p, estado, limiteMs = 0) {
  if (estado.cancelada) return Promise.reject(new Cancelada());
  const corrida = [p, estado.aoCancelar];
  let limite;
  if (limiteMs > 0) corrida.push(new Promise((_, falhar) => {
    limite = setTimeout(() => falhar(new TempoEsgotado("tempo")), limiteMs);
  }));
  return Promise.race(corrida).finally(() => clearTimeout(limite));
}
async function destruir(estado) {
  const tarefa = estado.tarefa;
  estado.tarefa = null;
  if (!tarefa) return;
  let limite;
  try {
    await Promise.race([tarefa.destroy(), new Promise((pronto) => {
      limite = setTimeout(pronto, 2e3);
    })]);
  } catch {
  } finally {
    clearTimeout(limite);
  }
}
var emCurso = /* @__PURE__ */ new Map();
var canceladasAntes = /* @__PURE__ */ new Set();
var filaTexto = Promise.resolve();
var filaFiguras = Promise.resolve();
function comecar(id) {
  const estado = novaExtracao(id);
  if (canceladasAntes.delete(id)) estado.cancelar();
  emCurso.set(id, estado);
  return estado;
}
function terminar(estado) {
  if (emCurso.get(estado.id) === estado) emCurso.delete(estado.id);
}
function codigoDoErro(e) {
  if (pdfjs && e instanceof pdfjs.PasswordException) return "senha";
  if (pdfjs && e instanceof pdfjs.InvalidPDFException) return "invalido";
  const nome = typeof e === "object" && e !== null ? e.name : void 0;
  if (nome === "PasswordException") return "senha";
  if (nome === "InvalidPDFException") return "invalido";
  return "falhou";
}
async function extrair(id, url) {
  const estado = comecar(id);
  try {
    if (!pdfjs) throw new Error("O PDF.js nao foi carregado.");
    if (estado.cancelada) throw new Cancelada();
    const tarefa = pdfjs.getDocument(opcoesDocumento(url));
    estado.tarefa = tarefa;
    const doc = await ate(tarefa.promise, estado);
    const total = doc.numPages;
    for (let p = 1; p <= total; p++) {
      if (estado.cancelada) throw new Cancelada();
      const pagina = await ate(doc.getPage(p), estado);
      const conteudo = await ate(pagina.getTextContent(), estado);
      const { texto } = montarTexto(conteudo.items);
      pagina.cleanup();
      if (estado.cancelada) throw new Cancelada();
      evento("textoPagina", id, { pagina: p, texto });
      evento("progresso", id, { pagina: p, total });
    }
    await destruir(estado);
    enviar("extraido", id, { paginas: total });
  } catch (e) {
    const codigo = estado.cancelada ? "falhou" : codigoDoErro(e);
    const mensagem = estado.cancelada ? "Extracao cancelada." : e instanceof Error ? e.message : String(e);
    enviar("erro", id, { codigo, mensagem });
    await destruir(estado);
  } finally {
    terminar(estado);
  }
}
var MAX_PIXELS = 4e6;
var MAX_FIGURAS_PAGINA = 40;
function recortar(origem, x, y, w, h) {
  const c = document.createElement("canvas");
  c.width = Math.max(1, Math.round(w));
  c.height = Math.max(1, Math.round(h));
  const ctx = c.getContext("2d");
  if (!ctx) throw new Error("canvas sem contexto 2d");
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, c.width, c.height);
  ctx.drawImage(origem, Math.round(x), Math.round(y), c.width, c.height, 0, 0, c.width, c.height);
  const tipo = c.width * c.height > 12e5 ? "image/jpeg" : "image/png";
  const url = c.toDataURL(tipo, 0.86);
  const virgula = url.indexOf(",");
  if (!url.startsWith("data:" + tipo) || virgula < 0) throw new Error("recorte vazio");
  return { tipo, dados: url.slice(virgula + 1) };
}
var LIMITE_PASSO_MS = 3e4;
async function figurasDaPagina(pagina, estado) {
  const conteudo = await ate(pagina.getTextContent(), estado, LIMITE_PASSO_MS);
  const texto = montarTexto(conteudo.items);
  const [vx0, vy0, vx1, vy1] = pagina.view;
  const vista = { x0: Math.min(vx0, vx1), y0: Math.min(vy0, vy1), x1: Math.max(vx0, vx1), y1: Math.max(vy0, vy1) };
  let lista = [];
  let semLugar = false;
  try {
    const ops = await ate(pagina.getOperatorList({ intent: "print", annotationMode: pdfjs.AnnotationMode.DISABLE }), estado, LIMITE_PASSO_MS);
    const g = lerOperacoes(ops.fnArray, ops.argsArray, pdfjs.OPS);
    lista = regioes(g, vista, texto).slice(0, MAX_FIGURAS_PAGINA);
    semLugar = g.semLugar && lista.length === 0;
  } catch (e) {
    if (e instanceof Cancelada || estado.cancelada) throw new Cancelada();
    semLugar = true;
  }
  if (lista.length === 0 && !semLugar) return [];
  if (estado.cancelada) throw new Cancelada();
  const base = pagina.getViewport({ scale: 1 });
  const escala = Math.min(2, Math.sqrt(MAX_PIXELS / Math.max(1, base.width * base.height)));
  const viewport = pagina.getViewport({ scale: escala });
  const largura = vista.x1 - vista.x0;
  const paginaInteira = (origem, tipo = "", dados = "") => ({ linha: texto.linhasY.length, x: 0, y: 0, w: 1, h: 1, largura, origem, tipo, dados });
  const canvas = document.createElement("canvas");
  canvas.width = Math.ceil(viewport.width);
  canvas.height = Math.ceil(viewport.height);
  try {
    const tarefa = pagina.render({ canvas, viewport, intent: "print", annotationMode: pdfjs.AnnotationMode.DISABLE });
    try {
      await ate(tarefa.promise, estado, LIMITE_PASSO_MS);
    } catch (e) {
      tarefa.cancel();
      throw e;
    }
  } catch (e) {
    if (e instanceof Cancelada || estado.cancelada) {
      canvas.width = canvas.height = 0;
      throw new Cancelada();
    }
    canvas.width = canvas.height = 0;
    return [paginaInteira("falhou")];
  }
  try {
    if (semLugar) {
      const r = recortar(canvas, 0, 0, canvas.width, canvas.height);
      return [paginaInteira("pagina", r.tipo, r.dados)];
    }
    const figuras = [];
    for (const reg of lista) {
      const [ax, ay] = viewport.convertToViewportPoint(reg.caixa.x0, reg.caixa.y0);
      const [bx, by] = viewport.convertToViewportPoint(reg.caixa.x1, reg.caixa.y1);
      const x = Math.max(0, Math.min(ax, bx));
      const y = Math.max(0, Math.min(ay, by));
      const w = Math.min(canvas.width, Math.max(ax, bx)) - x;
      const h = Math.min(canvas.height, Math.max(ay, by)) - y;
      if (w < 2 || h < 2) continue;
      const r = recortar(canvas, x, y, w, h);
      figuras.push({
        linha: linhaDaFigura(texto.linhasY, reg.caixa.y1),
        x: x / canvas.width,
        y: y / canvas.height,
        w: w / canvas.width,
        h: h / canvas.height,
        largura: reg.caixa.x1 - reg.caixa.x0,
        origem: reg.imagem ? "imagem" : "desenho",
        tipo: r.tipo,
        dados: r.dados
      });
    }
    return figuras;
  } catch {
    try {
      const r = recortar(canvas, 0, 0, canvas.width, canvas.height);
      return [paginaInteira("pagina", r.tipo, r.dados)];
    } catch {
      return [paginaInteira("falhou")];
    }
  } finally {
    canvas.width = canvas.height = 0;
  }
}
async function extrairFiguras(id, url) {
  const estado = comecar(id);
  try {
    if (!pdfjs) throw new Error("O PDF.js nao foi carregado.");
    if (estado.cancelada) throw new Cancelada();
    const tarefa = pdfjs.getDocument(opcoesDocumento(url));
    estado.tarefa = tarefa;
    const doc = await ate(tarefa.promise, estado);
    const total = doc.numPages;
    for (let p = 1; p <= total; p++) {
      if (estado.cancelada) throw new Cancelada();
      const pagina = await ate(doc.getPage(p), estado, LIMITE_PASSO_MS);
      let figuras;
      try {
        figuras = await figurasDaPagina(pagina, estado);
      } catch (e) {
        if (e instanceof Cancelada || estado.cancelada) throw new Cancelada();
        figuras = [{ linha: 1e6, x: 0, y: 0, w: 1, h: 1, largura: 0, origem: "falhou", tipo: "", dados: "" }];
      }
      pagina.cleanup();
      if (estado.cancelada) throw new Cancelada();
      evento("figurasPagina", id, { pagina: p, figuras });
    }
    await destruir(estado);
    enviar("figurasExtraidas", id, { paginas: total });
  } catch (e) {
    const codigo = estado.cancelada ? "falhou" : codigoDoErro(e);
    const mensagem = estado.cancelada ? "Extracao cancelada." : e instanceof Error ? e.message : String(e);
    enviar("erro", id, { codigo, mensagem });
    await destruir(estado);
  } finally {
    terminar(estado);
  }
}
function aoReceber(bruto) {
  const r = lerMensagemHost(bruto);
  if (!r.ok) {
    console.warn("[extrator] mensagem do host descartada: " + r.motivo);
    enviar("erro", r.id ?? "sem-id", { codigo: "falhou", mensagem: "mensagem_invalida: " + r.motivo });
    return;
  }
  const { msg } = r;
  if (msg.tipo === "extrair") {
    const url = msg.dados.url;
    if (typeof url !== "string" || url === "") {
      enviar("erro", msg.id, { codigo: "falhou", mensagem: "extrair sem url" });
      return;
    }
    filaTexto = filaTexto.then(() => extrair(msg.id, url));
  } else if (msg.tipo === "figuras") {
    const url = msg.dados.url;
    if (typeof url !== "string" || url === "") {
      enviar("erro", msg.id, { codigo: "falhou", mensagem: "figuras sem url" });
      return;
    }
    filaFiguras = filaFiguras.then(() => extrairFiguras(msg.id, url));
  } else if (msg.tipo === "cancelar") {
    const alvo = msg.dados.extracao;
    if (typeof alvo === "string" && alvo !== "") {
      const estado = emCurso.get(alvo);
      if (estado) estado.cancelar();
      else {
        canceladasAntes.add(alvo);
        if (canceladasAntes.size > 100) canceladasAntes.delete(canceladasAntes.values().next().value);
      }
    } else {
      for (const estado of emCurso.values()) estado.cancelar();
    }
  }
}
var ultimaViolacao = { chave: "", quando: -Infinity };
document.addEventListener("securitypolicyviolation", (e) => {
  const chave = `${e.violatedDirective}|${e.blockedURI}`;
  const agora = performance.now();
  if (chave === ultimaViolacao.chave && agora - ultimaViolacao.quando < 100) return;
  ultimaViolacao = { chave, quando: agora };
  evento("cspViolacao", null, { diretiva: e.violatedDirective, uri: e.blockedURI });
});
if (!webview) {
  console.error("[extrator] window.chrome.webview indisponivel");
} else if (!pdfjs) {
  enviar("erro", "inicio", { codigo: "falhou", mensagem: "O PDF.js nao foi carregado." });
} else {
  configurarPdfJs(pdfjs);
  webview.addEventListener("message", (e) => aoReceber(e.data));
  enviar("pronto", "pronto", { versao: VERSAO, pdfjs: pdfjs.version });
}
