// src/legislacao/mensagens.ts
var VERSAO = 1;
var TIPOS_HOST_LEITOR = ["abrir", "fechar", "irPagina", "irMarcador", "zoom", "procurar", "obterSelecao"];
var TIPOS_WEB_LEITOR = [
  "pronto",
  "paginaMudou",
  "selecaoMudou",
  "zoomMudou",
  "atalho",
  "cspViolacao",
  "aberto",
  "erro",
  "fechado",
  "paginaAtual",
  "zoomAplicado",
  "procurado",
  "selecao"
];
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
var lerMensagemHostLeitor = (bruto) => ler(bruto, TIPOS_HOST_LEITOR);
var lerMensagemWebLeitor = (bruto) => ler(bruto, TIPOS_WEB_LEITOR);

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

// src/legislacao/leitor.ts
var ATALHOS = /* @__PURE__ */ new Set([
  "Ctrl+F",
  "Ctrl+C",
  "Ctrl+Shift+C",
  "Ctrl+Plus",
  "Ctrl+Minus",
  "Ctrl+0",
  "Alt+ArrowUp",
  "Alt+ArrowDown",
  "F3",
  "Shift+F3",
  // Trilho de módulos (Ctrl+1 … Ctrl+5): valem também com o foco no texto da lei.
  "Ctrl+1",
  "Ctrl+2",
  "Ctrl+3",
  "Ctrl+4",
  "Ctrl+5"
]);
var ESCALA_MIN = 0.25;
var ESCALA_MAX = 4;
var MAX_TITULO = 300;
var webview = window.chrome?.webview;
var pdfjs = globalThis.pdfjsLib;
var visor = globalThis.pdfjsViewer;
var sequencia = 0;
function enviar(tipo, id, dados = {}) {
  const m = { v: VERSAO, tipo, id, dados };
  const r = lerMensagemWebLeitor(m);
  if (!r.ok) {
    console.error("[leitor] mensagem para o host descartada: " + r.motivo);
    return;
  }
  webview?.postMessage(m);
}
function evento(tipo, dados) {
  enviar(tipo, `evento-${++sequencia}`, dados);
}
var eventBus;
var links;
var busca;
var viewer;
var documento = null;
var tarefa = null;
var outline = [];
var termoAtual = "";
var geracao = 0;
function codigoDoErro(e) {
  if (pdfjs && e instanceof pdfjs.PasswordException) return "senha";
  if (pdfjs && e instanceof pdfjs.InvalidPDFException) return "invalido";
  const nome = typeof e === "object" && e !== null ? e.name : void 0;
  if (nome === "PasswordException") return "senha";
  if (nome === "InvalidPDFException") return "invalido";
  return "falhou";
}
function iniciarVisor(v) {
  const container = document.getElementById("container");
  eventBus = new v.EventBus();
  links = new v.PDFLinkService({ eventBus });
  links.externalLinkEnabled = false;
  busca = new v.PDFFindController({ eventBus, linkService: links, updateMatchesCountOnProgress: false, delay: 0 });
  viewer = new v.PDFViewer({
    container,
    eventBus,
    linkService: links,
    findController: busca,
    textLayerMode: 1,
    // ENABLE
    annotationMode: pdfjs.AnnotationMode.ENABLE,
    annotationEditorMode: pdfjs.AnnotationEditorType.DISABLE,
    removePageBorders: false
  });
  links.setViewer(viewer);
  eventBus.on("pagesinit", () => {
    viewer.currentScaleValue = "page-width";
  });
  eventBus.on("pagechanging", (e) => {
    evento("paginaMudou", { atual: e.pageNumber, total: viewer.pagesCount });
  });
  eventBus.on("scalechanging", () => evento("zoomMudou", { escala: viewer.currentScale }));
}
async function fecharAtual() {
  geracao++;
  termoAtual = "";
  outline = [];
  const doc = documento;
  const t = tarefa;
  documento = null;
  tarefa = null;
  viewer.setDocument(null);
  links.setDocument(null, null);
  try {
    await t?.destroy();
  } catch {
  }
  void doc;
}
function marcadores(itens) {
  return itens.map((i) => ({
    titulo: String(i.title ?? "").replace(/\s+/g, " ").trim().slice(0, MAX_TITULO),
    externo: !i.dest && !!(i.url || i.unsafeUrl),
    filhos: marcadores(i.items ?? [])
  }));
}
async function abrir(id, url, pagina, termo) {
  await fecharAtual();
  const minha = geracao;
  let t;
  try {
    t = pdfjs.getDocument(opcoesDocumento(url));
    tarefa = t;
    const doc = await t.promise;
    if (minha !== geracao) {
      try {
        await t.destroy();
      } catch {
      }
      return;
    }
    documento = doc;
    const iniciou = new Promise((ok) => eventBus.on("pagesinit", () => ok(), { once: true }));
    viewer.setDocument(doc);
    links.setDocument(doc, null);
    await iniciou;
    viewer.currentScaleValue = "page-width";
    outline = await doc.getOutline() ?? [];
    if (minha !== geracao) return;
    if (pagina !== null) viewer.currentPageNumber = Math.min(Math.max(1, Math.trunc(pagina)), doc.numPages);
    let procurado = null;
    if (termo) procurado = await procurar(termo, 0, false);
    enviar("aberto", id, { paginas: doc.numPages, marcadores: marcadores(outline), escala: viewer.currentScale, ...procurado ? { procurado } : {} });
  } catch (e) {
    if (minha === geracao) {
      documento = null;
      tarefa = null;
    }
    enviar("erro", id, { codigo: codigoDoErro(e), mensagem: e instanceof Error ? e.message : String(e) });
  }
}
async function resultadoDaBusca() {
  const b = busca;
  await b._firstPageCapability.promise;
  await Promise.all(b._extractTextPromises);
  await new Promise((ok) => setTimeout(ok, 0));
  let total = 0;
  for (const p of b.pageMatches) total += p?.length ?? 0;
  const { pageIdx, matchIdx } = b.selected;
  let atual = 0;
  if (matchIdx >= 0 && pageIdx >= 0) {
    for (let i = 0; i < pageIdx; i++) atual += b.pageMatches[i]?.length ?? 0;
    atual += matchIdx + 1;
  }
  if (total === 0 || atual < 1 || atual > total) return { total: 0, atual: 0, pagina: null };
  return { total, atual, pagina: pageIdx + 1 };
}
function despachar(query, anterior) {
  eventBus.dispatch("find", {
    source: null,
    type: "again",
    query,
    caseSensitive: false,
    entireWord: false,
    highlightAll: true,
    findPrevious: anterior,
    matchDiacritics: false
  });
}
async function procurar(termo, passo, desdeOInicio = true) {
  if (!documento) return { total: 0, atual: 0, pagina: null };
  if (termo === "") {
    if (termoAtual !== "") {
      despachar("", false);
      eventBus.dispatch("findbarclose", { source: null });
    }
    termoAtual = "";
    return { total: 0, atual: 0, pagina: null };
  }
  if (termo === termoAtual && passo === 0) return resultadoDaBusca();
  const novo = termo !== termoAtual;
  termoAtual = termo;
  if (novo && desdeOInicio) viewer.currentPageNumber = 1;
  despachar(termo, !novo && passo < 0);
  return resultadoDaBusca();
}
function paginaDe(no) {
  const el = no instanceof Element ? no : no?.parentElement ?? null;
  const pagina = el?.closest(".page[data-page-number]");
  const n = pagina ? Number(pagina.dataset.pageNumber) : NaN;
  return Number.isFinite(n) ? n : null;
}
function selecao() {
  const s = document.getSelection();
  const texto = s && !s.isCollapsed ? s.toString() : "";
  if (!s || texto.trim() === "") return { texto: "", paginaInicial: null, paginaFinal: null };
  const a = paginaDe(s.anchorNode);
  const b = paginaDe(s.focusNode);
  const paginas = [a, b].filter((x) => x !== null);
  if (paginas.length === 0) return { texto: "", paginaInicial: null, paginaFinal: null };
  return { texto, paginaInicial: Math.min(...paginas), paginaFinal: Math.max(...paginas) };
}
var tinhaSelecao = false;
document.addEventListener("selectionchange", () => {
  const tem = selecao().texto !== "";
  if (tem === tinhaSelecao) return;
  tinhaSelecao = tem;
  evento("selecaoMudou", { temSelecao: tem });
});
function aplicarZoom(acao) {
  if (!documento) return;
  if (acao === "mais") viewer.increaseScale();
  else if (acao === "menos") viewer.decreaseScale();
  else if (acao === "largura") viewer.currentScaleValue = "page-width";
  else if (acao === "pagina") viewer.currentScaleValue = "page-fit";
  if (viewer.currentScale > ESCALA_MAX) viewer.currentScale = ESCALA_MAX;
  if (viewer.currentScale < ESCALA_MIN) viewer.currentScale = ESCALA_MIN;
}
function paginaAtual() {
  return { atual: documento ? viewer.currentPageNumber : 0, total: documento ? viewer.pagesCount : 0 };
}
async function irMarcador(caminho) {
  if (!Array.isArray(caminho) || caminho.length === 0) return null;
  let itens = outline;
  let item;
  for (const i of caminho) {
    if (typeof i !== "number" || !Number.isInteger(i)) return null;
    item = itens[i];
    if (!item) return null;
    itens = item.items ?? [];
  }
  if (!item?.dest || !documento) return null;
  const dest = typeof item.dest === "string" ? await documento.getDestination(item.dest) : item.dest;
  const ref = Array.isArray(dest) ? dest[0] : null;
  let pagina = null;
  if (typeof ref === "number") pagina = ref + 1;
  else if (ref && typeof ref === "object") pagina = await documento.getPageIndex(ref) + 1;
  await links.goToDestination(item.dest);
  if (pagina !== null) viewer.currentPageNumber = pagina;
  await new Promise((ok) => setTimeout(ok, 0));
  if (pagina !== null && viewer.currentPageNumber !== pagina) viewer.currentPageNumber = pagina;
  return pagina;
}
async function tratar(msg) {
  const d = msg.dados;
  switch (msg.tipo) {
    case "abrir": {
      if (typeof d.url !== "string" || d.url === "") {
        enviar("erro", msg.id, { codigo: "falhou", mensagem: "abrir sem url" });
        return;
      }
      const pagina = typeof d.pagina === "number" ? d.pagina : null;
      const termo = typeof d.termo === "string" && d.termo !== "" ? d.termo : null;
      await abrir(msg.id, d.url, pagina, termo);
      return;
    }
    case "fechar":
      await fecharAtual();
      document.getSelection()?.removeAllRanges();
      enviar("fechado", msg.id, {});
      return;
    case "irPagina": {
      let alvo = null;
      if (documento && typeof d.pagina === "number" && Number.isFinite(d.pagina)) {
        alvo = Math.min(Math.max(1, Math.trunc(d.pagina)), viewer.pagesCount);
        viewer.currentPageNumber = alvo;
      }
      enviar("paginaAtual", msg.id, alvo === null ? paginaAtual() : { atual: alvo, total: viewer.pagesCount });
      return;
    }
    case "irMarcador": {
      const alvo = documento ? await irMarcador(d.caminho) : null;
      enviar("paginaAtual", msg.id, alvo === null ? paginaAtual() : { atual: alvo, total: viewer.pagesCount });
      return;
    }
    case "zoom":
      aplicarZoom(typeof d.acao === "string" ? d.acao : "");
      enviar("zoomAplicado", msg.id, { escala: documento ? viewer.currentScale : 0 });
      return;
    case "procurar": {
      const termo = typeof d.termo === "string" ? d.termo : "";
      const passo = d.passo === 1 || d.passo === -1 ? d.passo : 0;
      enviar("procurado", msg.id, await procurar(termo, passo));
      return;
    }
    case "obterSelecao": {
      const s = selecao();
      enviar("selecao", msg.id, { texto: s.texto, paginaInicial: s.paginaInicial, paginaFinal: s.paginaFinal });
      return;
    }
  }
}
var fila = Promise.resolve();
function aoReceber(bruto) {
  const r = lerMensagemHostLeitor(bruto);
  if (!r.ok) {
    console.warn("[leitor] mensagem do host descartada: " + r.motivo);
    enviar("erro", r.id ?? "sem-id", { codigo: "falhou", mensagem: "mensagem_invalida: " + r.motivo });
    return;
  }
  const msg = r.msg;
  fila = fila.then(() => tratar(msg)).catch((e) => {
    enviar("erro", msg.id, { codigo: "falhou", mensagem: e instanceof Error ? e.message : String(e) });
  });
}
function nomeDoAtalho(e) {
  if (e.metaKey) return null;
  let shift = e.shiftKey;
  let tecla;
  if (/^[a-z]$/i.test(e.key)) tecla = e.key.toUpperCase();
  else if (e.key === "+" || e.key === "=") {
    tecla = "Plus";
    shift = false;
  } else if (e.key === "-" || e.key === "_") {
    tecla = "Minus";
    shift = false;
  } else tecla = e.key;
  return [e.ctrlKey ? "Ctrl" : "", e.altKey ? "Alt" : "", shift ? "Shift" : "", tecla].filter((p) => p !== "").join("+");
}
window.addEventListener("keydown", (e) => {
  const nome = nomeDoAtalho(e);
  if (nome === null || !ATALHOS.has(nome)) return;
  e.preventDefault();
  e.stopPropagation();
  evento("atalho", { tecla: nome });
}, true);
window.addEventListener("wheel", (e) => {
  if (!e.ctrlKey) return;
  e.preventDefault();
  if (!documento) return;
  aplicarZoom(e.deltaY < 0 ? "mais" : "menos");
}, { passive: false, capture: true });
var ultimaViolacao = { chave: "", quando: -Infinity };
document.addEventListener("securitypolicyviolation", (e) => {
  const chave = `${e.violatedDirective}|${e.blockedURI}`;
  const agora = performance.now();
  if (chave === ultimaViolacao.chave && agora - ultimaViolacao.quando < 100) return;
  ultimaViolacao = { chave, quando: agora };
  evento("cspViolacao", { diretiva: e.violatedDirective, uri: e.blockedURI });
});
if (!webview) {
  console.error("[leitor] window.chrome.webview indisponivel");
} else if (!pdfjs || !visor) {
  enviar("erro", "inicio", { codigo: "falhou", mensagem: "O PDF.js nao foi carregado." });
} else {
  configurarPdfJs(pdfjs);
  iniciarVisor(visor);
  webview.addEventListener("message", (e) => aoReceber(e.data));
  enviar("pronto", "pronto", { versao: VERSAO, pdfjs: pdfjs.version });
}
