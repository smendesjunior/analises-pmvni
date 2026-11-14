"use strict";
var Paginador = (() => {
  var __defProp = Object.defineProperty;
  var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __hasOwnProp = Object.prototype.hasOwnProperty;
  var __export = (target, all) => {
    for (var name in all)
      __defProp(target, name, { get: all[name], enumerable: true });
  };
  var __copyProps = (to, from, except, desc) => {
    if (from && typeof from === "object" || typeof from === "function") {
      for (let key of __getOwnPropNames(from))
        if (!__hasOwnProp.call(to, key) && key !== except)
          __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
    }
    return to;
  };
  var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

  // src/index.ts
  var index_exports = {};
  __export(index_exports, {
    enviar: () => enviar,
    registrar: () => registrar
  });

  // src/mensagens.ts
  var VERSAO = 1;
  var TIPOS_HOST = ["compor", "aplicarPlano", "textoPorFolha", "rolarPara", "definirZoom"];
  var TIPOS_WEB = ["pronto", "composto", "planoAplicado", "textoPorFolha", "folhaVisivel", "erro"];
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

  // src/index.ts
  var webview = window.chrome?.webview;
  var manipuladores = /* @__PURE__ */ new Map();
  function enviar(tipo, id, dados = {}) {
    const m = { v: VERSAO, tipo, id, dados };
    const r = lerMensagemWeb(m);
    if (!r.ok) {
      console.error("[ponte] mensagem para o host descartada: " + r.motivo);
      return;
    }
    webview?.postMessage(m);
  }
  function registrar(tipo, manipulador) {
    manipuladores.set(tipo, manipulador);
  }
  async function aoReceber(bruto) {
    const r = lerMensagemHost(bruto);
    if (!r.ok) {
      console.warn("[ponte] mensagem do host descartada: " + r.motivo);
      enviar("erro", r.id ?? "sem-id", { codigo: "mensagem_invalida", motivo: r.motivo });
      return;
    }
    const { msg } = r;
    const manipulador = manipuladores.get(msg.tipo);
    if (!manipulador) {
      enviar("erro", msg.id, { codigo: "sem_manipulador", tipo: msg.tipo });
      return;
    }
    try {
      const resposta = await manipulador(msg.dados, msg);
      if (resposta) enviar(resposta.tipo, msg.id, resposta.dados);
    } catch (e) {
      enviar("erro", msg.id, { codigo: "falha_no_manipulador", mensagem: e instanceof Error ? e.message : String(e) });
    }
  }
  registrar("textoPorFolha", () => ({ tipo: "textoPorFolha", dados: { folhas: [] } }));
  if (webview) {
    webview.addEventListener("message", (e) => void aoReceber(e.data));
    enviar("pronto", "pronto", { versao: VERSAO });
  } else {
    console.error("[ponte] window.chrome.webview indisponivel");
  }
  return __toCommonJS(index_exports);
})();
