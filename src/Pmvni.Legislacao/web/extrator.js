// src/legislacao/mensagens.ts
var VERSAO = 1;
var TIPOS_HOST = ["extrair", "cancelar"];
var TIPOS_WEB = ["pronto", "progresso", "textoPagina", "cspViolacao", "extraido", "erro"];
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
var atual = null;
var fila = Promise.resolve();
function codigoDoErro(e) {
  if (pdfjs && e instanceof pdfjs.PasswordException) return "senha";
  if (pdfjs && e instanceof pdfjs.InvalidPDFException) return "invalido";
  const nome = typeof e === "object" && e !== null ? e.name : void 0;
  if (nome === "PasswordException") return "senha";
  if (nome === "InvalidPDFException") return "invalido";
  return "falhou";
}
async function extrair(id, url) {
  const estado = { id, cancelada: false, tarefa: null };
  atual = estado;
  try {
    if (!pdfjs) throw new Error("O PDF.js nao foi carregado.");
    const tarefa = pdfjs.getDocument(opcoesDocumento(url));
    estado.tarefa = tarefa;
    const doc = await tarefa.promise;
    const total = doc.numPages;
    for (let p = 1; p <= total; p++) {
      if (estado.cancelada) throw new Cancelada();
      const pagina = await doc.getPage(p);
      const conteudo = await pagina.getTextContent();
      let texto = "";
      for (const item of conteudo.items) {
        if ("str" in item) {
          texto += item.str;
          if (item.hasEOL) texto += "\n";
        }
      }
      pagina.cleanup();
      if (estado.cancelada) throw new Cancelada();
      evento("textoPagina", id, { pagina: p, texto });
      evento("progresso", id, { pagina: p, total });
    }
    await tarefa.destroy();
    enviar("extraido", id, { paginas: total });
  } catch (e) {
    const codigo = estado.cancelada ? "falhou" : codigoDoErro(e);
    const mensagem = estado.cancelada ? "Extracao cancelada." : e instanceof Error ? e.message : String(e);
    enviar("erro", id, { codigo, mensagem });
    try {
      await estado.tarefa?.destroy();
    } catch {
    }
  } finally {
    if (atual === estado) atual = null;
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
    fila = fila.then(() => extrair(msg.id, url));
  } else if (msg.tipo === "cancelar") {
    if (atual) {
      atual.cancelada = true;
      void atual.tarefa?.destroy();
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
