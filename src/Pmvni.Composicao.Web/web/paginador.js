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
    larguraNoPlano: () => larguraNoPlano,
    montarFolhaDoPlano: () => montarFolhaDoPlano,
    registrar: () => registrar,
    textoPorFolha: () => textoPorFolha
  });

  // folha.css
  var folha_default = "/* Folhas A4 do paginador (embutido no paginador.js). As regras @page do kit saem do documento e entram estas.\r\n   CONSTANTES DE POSIÇÃO: só aqui. Elas reproduzem as caixas de margem do kit (@page margin 28/14/15/14 mm,\r\n   @top-center com padding-bottom 5 mm, @bottom-left/right com padding-top 2,5 mm), calibradas na Tarefa 9 contra o PDF\r\n   do próprio kit (medidas com PyMuPDF em docs/fidelidade.md). O Chromium do kit encaixa a caixa da página em pixels\r\n   inteiros (1 px = 1/96 pol.): a área útil começa em (53 px, 106 px) — 14 mm = 52,91 px e 28 mm = 105,83 px arredondados —\r\n   e tem 960 px (254 mm) de altura; por isso as posições abaixo vêm em px. O paginador lê a área útil daqui (largura de\r\n   .area; altura = topo do rodapé − topo da área), nunca de constantes no JS. */\r\n@page { size: 210mm 297mm; margin: 0; }\r\n\r\n/* Escala de impressão k (>= 1), definida pelo paginador: quando o conteúdo é mais largo que a página, o Chromium\r\n   diagrama a página k vezes maior (até 1,5) e a imprime reduzida (shrink-to-fit). A área reproduz isso: é diagramada com\r\n   182k × 254k mm e pintada em escala 1/k, ocupando os mesmos 182 × 254 mm da folha. */\r\n:root { --pmvni-k: 1; }\r\n\r\n.folha {\r\n  position: relative; display: block; box-sizing: border-box;\r\n  width: 210mm; height: 297mm; margin: 0; padding: 0; border: 0;\r\n  overflow: visible; background: #fff; break-after: page; break-inside: avoid;\r\n}\r\nsection.folha:last-of-type { break-after: auto; }\r\n\r\n/* Área útil: 182 × 254 mm (na folha) a partir de (53 px, 106 px). Nunca overflow: hidden (estouro fica visível).\r\n   A altura da caixa fica em 254 mm (não 254k): com k > 1 a caixa diagramada (28 + 254k mm) passaria da folha e, na\r\n   impressão, o fragmento que sobra da última folha vira uma página em branco a mais. A altura útil (254 mm na folha) é\r\n   lida da posição do rodapé (fimDaArea em fragmentar.ts), não desta caixa. */\r\n.folha > .area {\r\n  position: absolute; left: 53px; top: 106px; width: calc(182mm * var(--pmvni-k)); height: 254mm;\r\n  display: flow-root; margin: 0; padding: 0; border: 0; box-sizing: border-box; overflow: visible;\r\n  transform: scale(calc(1 / var(--pmvni-k))); transform-origin: 0 0;\r\n}\r\n\r\n/* Caixa monolítica (contain: size) nas folhas: com k > 1 o conteúdo é diagramado até 28 + 254k mm, além da folha, e o\r\n   Chromium, ao imprimir, fragmentava esse conteúdo pelo layout não escalado — empurrava o bloco indivisível do pé da\r\n   folha para a página seguinte, onde era recortado (sumia do PDF). Monolítica, a área imprime como está na tela.\r\n   Teste: ExportacaoTestes.pdf_com_escala_mostra_as_mesmas_linhas_que_a_tela. */\r\nsection.folha > .area { contain: size; }\r\n\r\n/* Cabeçalho: a imagem no tamanho intrínseco (o SVG do kit tem 688 × 64 px; o kit a põe por content: url(), sem\r\n   redimensionar), centrada nos 182 mm. Base: 106 px − 5 mm (padding-bottom do @top-center) − 4 px; os 4 px são o vão\r\n   abaixo da linha de base da caixa de margem do kit, onde a imagem fica apoiada (medido: imagem 4 px = 1,058 mm acima). */\r\nsection.folha > .folha-cabecalho {\r\n  position: absolute; left: 14mm; top: 0; width: 182mm; height: 23mm;\r\n  display: flex; align-items: flex-end; justify-content: center; margin: 0; padding: 0 0 4px; box-sizing: border-box;\r\n}\r\nsection.folha > .folha-cabecalho > img { display: block; width: auto; height: auto; }\r\n\r\n/* Rodapé: duas caixas como @bottom-left e @bottom-right do kit. O topo da caixa (1066 px = 106 + 960) é o fim da área\r\n   útil (fimDaArea em fragmentar.ts); a linha e o texto ficam 1 px abaixo (margin-top), onde o kit os desenha (linha em\r\n   1067 px = 282,31 mm). A divisão entre as duas caixas difere da do kit (lá, proporcional ao texto: 139,7 mm; aqui,\r\n   134,1 mm), sem efeito visível: as duas linhas têm a mesma cor e se emendam. */\r\nsection.folha > .folha-rodape {\r\n  position: absolute; left: 14mm; top: 1066px; width: 182mm; height: 15mm;\r\n  display: flex; align-items: flex-start; margin: 0; padding: 0;\r\n}\r\nsection.folha > .folha-rodape > div {\r\n  flex: 1 1 auto; white-space: nowrap; box-sizing: border-box;\r\n  border-top: .6pt solid #DDE4EB; padding-top: 2.5mm; font: 6.2pt Verdana; line-height: normal; margin-top: 1px;\r\n}\r\nsection.folha > .folha-rodape > .folha-rodape-esq { color: #637083; text-align: left; }\r\nsection.folha > .folha-rodape > .folha-rodape-dir { color: #1D3D5E; font-weight: bold; text-align: right; }\r\n\r\n/* Fluxo de medida (div.folha.pmvni-medida > .area): o conteúdo original, com a mesma largura da área e altura livre,\r\n   fora da vista. As folhas de verdade são section.folha. */\r\ndiv.folha.pmvni-medida { position: absolute; left: 0; top: 0; height: auto; visibility: hidden; pointer-events: none; }\r\ndiv.folha.pmvni-medida > .area { position: relative; left: 0; top: 0; height: auto; }\r\ndiv.folha.pmvni-medida[data-pmvni-guardado] { display: none; }\r\n";

  // src/plano.ts
  var VERSAO_PLANO = 1;
  var ErroComposicao = class extends Error {
    constructor(codigo, mensagem) {
      super(mensagem);
      this.codigo = codigo;
    }
  };
  var posicao = (caminho, deslocamentoTexto = null, linha = null, forcada = false) => ({ caminho, deslocamentoTexto, linha, forcada });
  var INICIO_DO_FLUXO = posicao([0]);
  function descer(p) {
    return { caminho: p.caminho.slice(1), deslocamentoTexto: p.deslocamentoTexto, linha: p.linha, forcada: p.forcada };
  }
  function dentroDoPrimeiro(p) {
    return p.caminho.length > 1 || p.deslocamentoTexto !== null || p.linha !== null;
  }
  var truncaTopo = (inicio, indice) => indice > 0 && !inicio.forcada;
  var ESCALA_MAXIMA_PLANO = 1.5;
  function lerPlano(bruto) {
    const falha = (motivo) => {
      throw new ErroComposicao("plano_invalido", "Plano de quebras inválido: " + motivo + ".");
    };
    const obj = (x) => typeof x === "object" && x !== null && !Array.isArray(x);
    const inteiro = (x) => typeof x === "number" && Number.isInteger(x) && x >= 0;
    const opcional = (x) => x === null || inteiro(x);
    if (!obj(bruto)) return falha("não é um objeto");
    const p = bruto;
    if (p.versao !== VERSAO_PLANO) falha("versão " + String(p.versao));
    if (typeof p.escala !== "number" || !Number.isFinite(p.escala) || p.escala < 1 || p.escala > ESCALA_MAXIMA_PLANO) falha("escala");
    if (typeof p.sha256Html !== "string" || p.sha256Html === "") falha("sha256Html");
    if (!Array.isArray(p.inicioDasFolhas) || p.inicioDasFolhas.length === 0) falha("inicioDasFolhas");
    if (!Array.isArray(p.estouros) || !Array.isArray(p.avisosLargura)) falha("estouros/avisosLargura");
    const inicios = p.inicioDasFolhas.map((i, n) => {
      if (!obj(i) || !Array.isArray(i.caminho) || i.caminho.length === 0 || !i.caminho.every(inteiro) || !opcional(i.deslocamentoTexto ?? null) || !opcional(i.linha ?? null)) return falha(`inicioDasFolhas[${n}]`);
      return posicao(
        [...i.caminho],
        i.deslocamentoTexto ?? null,
        i.linha ?? null,
        i.forcada === true
      );
    });
    return {
      versao: VERSAO_PLANO,
      inicioDasFolhas: inicios,
      estouros: p.estouros,
      sha256Html: p.sha256Html,
      escala: p.escala,
      avisosLargura: p.avisosLargura
    };
  }

  // src/medida.ts
  var PX_POR_MM = 96 / 25.4;
  var EPS_PX = 0.05;
  var pxParaMm = (px) => px / PX_POR_MM;
  var num = (v) => {
    const n = parseFloat(v ?? "");
    return Number.isFinite(n) ? n : 0;
  };
  var estilo = (el) => getComputedStyle(el);
  function escalaVisual(el) {
    const h = el;
    const w = h.offsetWidth;
    return w > 0 ? el.getBoundingClientRect().width / w : 1;
  }
  function caixa(cs, escala = 1) {
    return {
      bt: num(cs.borderTopWidth) * escala,
      bb: num(cs.borderBottomWidth) * escala,
      pt: num(cs.paddingTop) * escala,
      pb: num(cs.paddingBottom) * escala
    };
  }
  var ENCOLHIMENTO_MAXIMO = 1.5;
  function medirLargura(fluxo) {
    const q = fluxo.getBoundingClientRect();
    let direita = q.right;
    const largos = [];
    const visitar = (el, ramo) => {
      for (const f of Array.from(el.children)) {
        const cs = estilo(f);
        if (cs.display === "none") continue;
        const r = f.getBoundingClientRect();
        let alvo = ramo;
        if (r.width > 0 || r.height > 0) {
          direita = Math.max(direita, r.right);
          if (alvo) alvo.direita = Math.max(alvo.direita, r.right);
          else if (r.right > q.right + EPS_PX) {
            alvo = { el: f, direita: r.right };
            largos.push(alvo);
          }
        }
        if (cs.overflowX === "visible") visitar(f, alvo);
      }
    };
    visitar(fluxo, null);
    direita = Math.max(direita, q.left + fluxo.scrollWidth - 1);
    const largura = direita - q.left;
    const pedida = largura <= q.width + EPS_PX ? 1 : largura / q.width;
    if (pedida > 1 && largos.length === 0) {
      const procurar = (el) => {
        for (const f of Array.from(el.children)) {
          const cs = estilo(f);
          if (cs.display === "none" || cs.overflowX !== "visible") continue;
          const h = f;
          const r = f.getBoundingClientRect();
          const fim = r.left + h.scrollWidth * escalaVisual(f);
          if (h.scrollWidth > h.clientWidth + 1 && fim > q.right + EPS_PX) largos.push({ el: f, direita: fim });
          else procurar(f);
        }
      };
      procurar(fluxo);
    }
    return { escala: Math.min(pedida, ENCOLHIMENTO_MAXIMO), pedida, largos, area: q.width, esquerda: q.left };
  }
  async function folhasProntas(folhas2, limiteMs = LIMITE_ESTABILIDADE_MS) {
    const fim = performance.now() + limiteMs;
    if (await comLimite(document.fonts.ready, limiteMs) === ESGOTADO || document.fonts.status !== "loaded") {
      throw new ErroComposicao("instavel", `As fontes não carregaram em ${limiteMs} ms.`);
    }
    for (const f of folhas2) await imagensProntas(f, Math.max(1, fim - performance.now()));
  }
  var LIMITE_ESTABILIDADE_MS = 3e4;
  function quadro() {
    return new Promise((ok) => {
      let feito = false;
      const fim = () => {
        if (!feito) {
          feito = true;
          ok();
        }
      };
      requestAnimationFrame(fim);
      setTimeout(fim, 100);
    });
  }
  var ESGOTADO = Symbol("esgotado");
  function comLimite(p, ms) {
    return Promise.race([p, new Promise((ok) => setTimeout(() => ok(ESGOTADO), Math.max(0, ms)))]);
  }
  async function esperarEstabilidade(raiz2, limiteMs = LIMITE_ESTABILIDADE_MS) {
    const fim = performance.now() + limiteMs;
    const resta = () => fim - performance.now();
    for (; ; ) {
      await comLimite(document.fonts.ready, resta());
      const imagens = Array.from(document.images);
      await comLimite(Promise.all(imagens.map((i) => i.decode().catch(() => void 0))), resta());
      const antes = raiz2.getBoundingClientRect().height;
      await quadro();
      await quadro();
      const depois = raiz2.getBoundingClientRect().height;
      const pendentes = imagens.filter((i) => !i.complete).length;
      const novas = document.images.length !== imagens.length;
      const fontes = document.fonts.status !== "loaded";
      if (antes === depois && !pendentes && !novas && !fontes) return;
      if (resta() <= 0) {
        const motivos = [
          pendentes ? `${pendentes} imagem(ns) carregando` : "",
          novas ? "imagens novas" : "",
          fontes ? "fontes carregando" : "",
          antes !== depois ? "altura mudando" : ""
        ].filter(Boolean).join(", ");
        throw new ErroComposicao("instavel", `O documento não estabilizou em ${limiteMs} ms (${motivos}).`);
      }
    }
  }
  async function imagensProntas(raiz2, limiteMs = LIMITE_ESTABILIDADE_MS) {
    const pendentes = Array.from(raiz2.querySelectorAll("img")).filter((i) => !i.complete);
    if (!pendentes.length) return;
    const r = await comLimite(Promise.all(pendentes.map((i) => i.decode().catch(() => void 0))), limiteMs);
    if (r === ESGOTADO) {
      throw new ErroComposicao("instavel", `${pendentes.filter((i) => !i.complete).length} imagem(ns) não carregaram em ${limiteMs} ms.`);
    }
  }
  var atomico = (cs) => cs.display !== "inline" && cs.display !== "contents";
  function linhasDe(el) {
    const retangulos = [];
    const r = document.createRange();
    const coletar = (no) => {
      for (const filho of Array.from(no.childNodes)) {
        if (filho.nodeType === Node.TEXT_NODE) {
          r.selectNodeContents(filho);
          for (const q2 of Array.from(r.getClientRects())) if (q2.height > 0) retangulos.push({ t: q2.top, b: q2.bottom });
        } else if (filho.nodeType === Node.ELEMENT_NODE) {
          const e = filho;
          if (e.localName === "br") continue;
          const cs2 = estilo(e);
          if (cs2.display === "none" || cs2.position === "absolute" || cs2.position === "fixed") continue;
          if (atomico(cs2)) {
            const q2 = e.getBoundingClientRect();
            if (q2.height > 0) retangulos.push({ t: q2.top, b: q2.bottom });
          } else coletar(e);
        }
      }
    };
    coletar(el);
    retangulos.sort((a, b) => a.t - b.t || a.b - b.b);
    const linhas = [];
    for (const q2 of retangulos) {
      const ultima = linhas[linhas.length - 1];
      if (!ultima || q2.t >= ultima.b - 0.5) linhas.push({ ...q2 });
      else {
        ultima.t = Math.min(ultima.t, q2.t);
        ultima.b = Math.max(ultima.b, q2.b);
      }
    }
    const n = linhas.length;
    if (n < 2) return { n, fronteiras: [] };
    const cs = estilo(el);
    const q = el.getBoundingClientRect();
    const c = caixa(cs, escalaVisual(el));
    const topo = q.top + c.bt + c.pt;
    const passo = (q.bottom - c.bb - c.pb - topo) / n;
    const uniforme = linhas.every((l, i) => Math.abs((l.t + l.b) / 2 - (topo + (i + 0.5) * passo)) < 1);
    const fronteiras = [];
    for (let i = 1; i < n; i++) {
      fronteiras.push(uniforme ? topo + i * passo : (linhas[i - 1].b + linhas[i].t) / 2);
    }
    return { n, fronteiras };
  }
  function nosDeTexto(el) {
    const lista = [];
    const w = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    let total = 0;
    for (let n = w.nextNode(); n; n = w.nextNode()) {
      const t = n;
      lista.push({ no: t, inicio: total });
      total += t.data.length;
    }
    return lista;
  }
  function localizar(el, desloc) {
    const nos = nosDeTexto(el);
    for (const { no, inicio } of nos) if (desloc < inicio + no.data.length) return { no, local: desloc - inicio };
    const ultimo = nos[nos.length - 1];
    return ultimo ? { no: ultimo.no, local: ultimo.no.data.length } : null;
  }
  var alta = (c) => c >= 55296 && c <= 56319;
  var baixa = (c) => c >= 56320 && c <= 57343;
  function deslocamentoNaFronteira(el, fronteira2) {
    const nos = nosDeTexto(el);
    const total = nos.reduce((s, x) => s + x.no.data.length, 0);
    if (total === 0) return null;
    const r = document.createRange();
    let k = 0;
    const centro = (o2) => {
      while (k > 0 && nos[k].inicio > o2) k--;
      while (k < nos.length - 1 && o2 >= nos[k].inicio + nos[k].no.data.length) k++;
      const { no, inicio } = nos[k];
      const local = o2 - inicio;
      const fim = Math.min(no.data.length, local + (alta(no.data.charCodeAt(local)) ? 2 : 1));
      r.setStart(no, local);
      r.setEnd(no, fim);
      const q = r.getBoundingClientRect();
      if (q.width === 0 && q.height === 0) return void 0;
      return (q.top + q.bottom) / 2;
    };
    const abaixo = (o2) => {
      for (let j = o2; j < Math.min(total, o2 + 256); j++) {
        const c = centro(j);
        if (c !== void 0) return c > fronteira2;
      }
      return true;
    };
    let lo = 0, hi = total;
    while (lo < hi) {
      const meio = lo + hi >> 1;
      if (abaixo(meio)) hi = meio;
      else lo = meio + 1;
    }
    let o = lo;
    if (o > 0 && o < total) {
      const loc = localizar(el, o);
      const antes = localizar(el, o - 1);
      if (baixa(loc.no.data.charCodeAt(loc.local)) && alta(antes.no.data.charCodeAt(antes.local))) o--;
    }
    return o > 0 && o < total ? o : null;
  }

  // src/fragmentar.ts
  var PERFEITO = 3;
  var VIOLA_ORFAS = 2;
  var VIOLA_EVITAR = 1;
  var ATR_OCULTO = "data-pmvni-oculto";
  var ATR_CONT = "data-pmvni-cont";
  var ATR_SEGUE = "data-pmvni-segue";
  var ATR_REPETIDO = "data-pmvni-repetido";
  var SUBSTITUIDOS = /* @__PURE__ */ new Set([
    "img",
    "svg",
    "video",
    "audio",
    "canvas",
    "iframe",
    "object",
    "embed",
    "input",
    "textarea",
    "select",
    "button",
    "picture",
    "math",
    "hr",
    "meter",
    "progress"
  ]);
  var FORCADAS = /* @__PURE__ */ new Set(["page", "left", "right", "recto", "verso", "always"]);
  var EVITAR = /* @__PURE__ */ new Set(["avoid", "avoid-page"]);
  var ehInline = (cs) => cs.display.startsWith("inline") || cs.display === "contents";
  var foraDoFluxo = (cs) => cs.display === "none" || cs.position === "absolute" || cs.position === "fixed" || cs.float !== "none";
  function filhosEmFluxo(el) {
    const lista = [];
    Array.from(el.children).forEach((f, i) => {
      const cs = estilo(f);
      if (!foraDoFluxo(cs) && cs.display !== "contents") lista.push({ el: f, i });
    });
    return lista;
  }
  function classificar(el) {
    if (SUBSTITUIDOS.has(el.localName.toLowerCase())) return "monolitico";
    const cs = estilo(el);
    if (cs.display === "table") return "tabela";
    if (ehInline(cs)) return "monolitico";
    if (cs.overflowY === "auto" || cs.overflowY === "scroll") return "monolitico";
    if (/^(flex|grid)$/.test(cs.display)) return "conteiner";
    if (!["block", "flow-root", "list-item"].includes(cs.display)) return "monolitico";
    let texto2 = false, inline = false, bloco = false;
    for (const n of Array.from(el.childNodes)) {
      if (n.nodeType === Node.TEXT_NODE) {
        if (/\S/.test(n.data)) texto2 = true;
        continue;
      }
      if (n.nodeType !== Node.ELEMENT_NODE) continue;
      const f = n;
      const c = estilo(f);
      if (c.display === "none" || c.position === "absolute" || c.position === "fixed") continue;
      if (f.localName === "br" || ehInline(c)) inline = true;
      else bloco = true;
    }
    if (bloco && !texto2 && !inline) return "conteiner";
    if ((texto2 || inline) && !bloco) return "linhas";
    return "monolitico";
  }
  var evitaDentro = (el) => EVITAR.has(estilo(el).breakInside);
  function cadeia(el, depois) {
    const valores = [];
    for (let e = el, n = 0; e && n < 32; n++) {
      const cs = estilo(e);
      valores.push(depois ? cs.breakAfter : cs.breakBefore);
      if (cs.display === "table" || classificar(e) !== "conteiner") break;
      const filhos = filhosEmFluxo(e);
      e = (depois ? filhos[filhos.length - 1] : filhos[0])?.el;
    }
    return valores;
  }
  function aberto(el) {
    const cs = estilo(el);
    const quebra = cs.getPropertyValue("box-decoration-break") || cs.getPropertyValue("-webkit-box-decoration-break");
    const escala = escalaVisual(el);
    const c = caixa(cs, escala);
    let topo = quebra === "clone" ? c.bt + c.pt : 0;
    const base = quebra === "clone" ? c.bb + c.pb : 0;
    if (cs.display === "table") {
      const t = el;
      if (t.tHead && estilo(t.tHead).display === "table-header-group") {
        topo += t.tHead.getBoundingClientRect().height + num(cs.borderSpacing.split(" ").pop()) * escala;
      }
    }
    return { topo, base };
  }
  var soma = (a, k) => a.reduce((s, x) => s + x[k], 0);
  function enumerarPontos(fluxo) {
    const pontos = [];
    const add = (p) => pontos.push({ ...p, indice: pontos.length });
    const percorrer = (el, caminho, abertos, evitar) => {
      const tipo = classificar(el);
      if (tipo === "conteiner") conteiner(el, caminho, abertos, evitar);
      else if (tipo === "tabela") tabela(el, caminho, abertos, evitar);
      else if (tipo === "linhas") linhas(el, caminho, abertos, evitar);
    };
    const conteiner = (el, caminho, abertos, evitar) => {
      const filhos2 = filhosEmFluxo(el);
      const q = filhos2.map((f) => f.el.getBoundingClientRect());
      const fimAte = [], inicioDesde = new Array(filhos2.length);
      q.forEach((r, j) => fimAte.push(Math.max(r.bottom, j ? fimAte[j - 1] : -Infinity)));
      for (let j = filhos2.length - 1; j >= 0; j--) inicioDesde[j] = Math.min(q[j].top, j < filhos2.length - 1 ? inicioDesde[j + 1] : Infinity);
      const cs = estilo(el);
      const porDentro = !(cs.display === "grid" || cs.display === "flex" && !cs.flexDirection.startsWith("column"));
      filhos2.forEach((f, j) => {
        if (porDentro) percorrer(f.el, [...caminho, f.i], [...abertos, aberto(f.el)], evitar || evitaDentro(f.el));
        const prox = filhos2[j + 1];
        if (!prox || fimAte[j] > inicioDesde[j + 1] + EPS_PX) return;
        const depois = cadeia(f.el, true), antes = cadeia(prox.el, false);
        const forcado = depois.some((v) => FORCADAS.has(v)) || antes.some((v) => FORCADAS.has(v));
        const apelo = evitar || depois.some((v) => EVITAR.has(v)) || antes.some((v) => EVITAR.has(v)) ? VIOLA_EVITAR : PERFEITO;
        add({
          pos: posicao([...caminho, prox.i]),
          apelo,
          forcado,
          yFim: fimAte[j],
          yInicio: inicioDesde[j + 1] - (forcado ? num(estilo(prox.el).marginTop) * escalaVisual(prox.el) : 0),
          decoFim: soma(abertos, "base"),
          decoInicio: soma(abertos, "topo")
        });
      });
    };
    const tabela = (t, caminho, abertos, evitar) => {
      const linhasCorpo = linhasDoCorpo(t);
      const alcance = linhasCorpo.map((tr, r) => Math.max(r, ...Array.from(tr.cells).map((c) => r + Math.max(1, c.rowSpan) - 1)));
      let maxAlcance = -1;
      for (let r = 1; r < linhasCorpo.length; r++) {
        maxAlcance = Math.max(maxAlcance, alcance[r - 1]);
        if (maxAlcance >= r) continue;
        const anterior = linhasCorpo[r - 1], atual = linhasCorpo[r];
        const ca = estilo(anterior), cb = estilo(atual);
        if (ca.display === "none" || cb.display === "none") continue;
        const forcado = FORCADAS.has(ca.breakAfter) || FORCADAS.has(cb.breakBefore);
        const apelo = evitar || EVITAR.has(ca.breakAfter) || EVITAR.has(cb.breakBefore) ? VIOLA_EVITAR : PERFEITO;
        add({
          pos: posicao(caminho, null, r),
          apelo,
          forcado,
          yFim: anterior.getBoundingClientRect().bottom,
          yInicio: atual.getBoundingClientRect().top,
          decoFim: soma(abertos, "base"),
          decoInicio: soma(abertos, "topo")
        });
      }
    };
    const linhas = (el, caminho, abertos, evitar) => {
      const { n, fronteiras } = linhasDe(el);
      if (n < 2) return;
      const cs = estilo(el);
      const orfas = Math.max(1, Math.round(num(cs.orphans)) || 2), viuvas = Math.max(1, Math.round(num(cs.widows)) || 2);
      for (let i = 1; i < n; i++) {
        let apelo = i < orfas || n - i < viuvas ? VIOLA_ORFAS : PERFEITO;
        if (evitar) apelo = VIOLA_EVITAR;
        const y = fronteiras[i - 1];
        add({
          pos: null,
          texto: { el, caminho, fronteira: y },
          apelo,
          forcado: false,
          yFim: y,
          yInicio: y,
          decoFim: soma(abertos, "base"),
          decoInicio: soma(abertos, "topo")
        });
      }
    };
    conteiner(fluxo, [], [], false);
    const filhos = filhosEmFluxo(fluxo);
    const topo = fluxo.getBoundingClientRect().top;
    const fim = filhos.reduce((m, f) => Math.max(m, f.el.getBoundingClientRect().bottom), topo);
    add({ pos: null, apelo: PERFEITO, forcado: false, yFim: fim, yInicio: fim, decoFim: 0, decoInicio: 0 });
    return pontos;
  }
  function resolver(p) {
    if (!p.texto) return p.pos;
    if (p.pos) return p.pos;
    const d = deslocamentoNaFronteira(p.texto.el, p.texto.fronteira);
    if (d === null) return void 0;
    p.pos = posicao(p.texto.caminho, d);
    return p.pos;
  }
  function linhasDoCorpo(t) {
    const lista = [];
    for (const sec of Array.from(t.children)) {
      if (sec.localName !== "tbody") continue;
      for (const tr of Array.from(sec.children)) if (tr.localName === "tr") lista.push(tr);
    }
    return lista;
  }
  function elementoEm(fluxo, caminho) {
    let e = fluxo;
    for (const i of caminho) {
      e = e?.children[i] ?? null;
      if (!e) return null;
    }
    return e;
  }
  function marcador(el) {
    const m = el.cloneNode(false);
    for (const a of ["data-ref", "src", "srcset", "id"]) m.removeAttribute(a);
    m.setAttribute(ATR_OCULTO, "");
    m.style?.setProperty("display", "none", "important");
    return m;
  }
  function copiarIntervalo(origem, destino, de, ate) {
    const filhos = Array.from(origem.children);
    const iDe = de ? de.caminho[0] : 0;
    const deDentro = de !== null && dentroDoPrimeiro(de);
    const iAte = ate ? ate.caminho[0] : filhos.length;
    const ateDentro = ate !== null && dentroDoPrimeiro(ate);
    filhos.forEach((f, i) => {
      if (i < iDe || i > iAte || i === iAte && !ateDentro) {
        destino.appendChild(marcador(f));
        return;
      }
      const parteDe = i === iDe && deDentro ? descer(de) : null;
      const parteAte = i === iAte && ateDentro ? descer(ate) : null;
      destino.appendChild(parteDe || parteAte ? pedaco(f, parteDe, parteAte) : f.cloneNode(true));
    });
  }
  function pedaco(el, de, ate) {
    const folhaDe = de && de.caminho.length === 0 ? de : null;
    const folhaAte = ate && ate.caminho.length === 0 ? ate : null;
    let casca;
    if (folhaDe?.deslocamentoTexto != null || folhaAte?.deslocamentoTexto != null) {
      casca = pedacoTexto(el, folhaDe?.deslocamentoTexto ?? null, folhaAte?.deslocamentoTexto ?? null);
    } else if (folhaDe?.linha != null || folhaAte?.linha != null) {
      casca = pedacoTabela(el, folhaDe?.linha ?? null, folhaAte?.linha ?? null);
    } else {
      casca = el.cloneNode(false);
      copiarIntervalo(el, casca, de, ate);
    }
    if (de) casca.setAttribute(ATR_CONT, "");
    if (ate) casca.setAttribute(ATR_SEGUE, "");
    return casca;
  }
  function fronteira(r, el, desloc, inicio) {
    const l = localizar(el, desloc);
    if (!l) {
      if (inicio) r.setStart(el, el.childNodes.length);
      else r.setEnd(el, el.childNodes.length);
      return;
    }
    if (l.local === 0) {
      let alvo = l.no;
      while (alvo.parentNode && alvo.parentNode !== el && alvo.previousSibling === null) alvo = alvo.parentNode;
      if (inicio) r.setStartBefore(alvo);
      else r.setEndBefore(alvo);
    } else if (inicio) r.setStart(l.no, l.local);
    else r.setEnd(l.no, l.local);
  }
  function pedacoTexto(el, de, ate) {
    const casca = el.cloneNode(false);
    const r = document.createRange();
    r.selectNodeContents(el);
    if (de !== null) fronteira(r, el, de, true);
    if (ate !== null) fronteira(r, el, ate, false);
    casca.appendChild(r.cloneContents());
    return casca;
  }
  function pedacoTabela(t, de, ate) {
    const casca = t.cloneNode(false);
    let g = 0;
    for (const sec of Array.from(t.children)) {
      switch (sec.localName) {
        case "tbody": {
          const corpo = sec.cloneNode(false);
          for (const tr of Array.from(sec.children)) {
            if (tr.localName !== "tr") {
              corpo.appendChild(tr.cloneNode(true));
              continue;
            }
            const dentro = (de === null || g >= de) && (ate === null || g < ate);
            corpo.appendChild(dentro ? tr.cloneNode(true) : marcador(tr));
            g++;
          }
          casca.appendChild(corpo);
          break;
        }
        case "thead": {
          const repete = estilo(sec).display === "table-header-group";
          if (de === null) casca.appendChild(sec.cloneNode(true));
          else if (repete) {
            const c = sec.cloneNode(true);
            c.setAttribute(ATR_REPETIDO, "");
            casca.appendChild(c);
          } else casca.appendChild(marcador(sec));
          break;
        }
        case "tfoot":
          casca.appendChild(ate === null ? sec.cloneNode(true) : marcador(sec));
          break;
        case "caption":
          casca.appendChild(de === null ? sec.cloneNode(true) : marcador(sec));
          break;
        default:
          casca.appendChild(sec.cloneNode(true));
      }
    }
    return casca;
  }
  var visivel = (e) => !e.hasAttribute(ATR_OCULTO);
  function primeiroBlocoVisivel(el) {
    for (const f of Array.from(el.children)) {
      if (!visivel(f)) continue;
      const cs = estilo(f);
      if (foraDoFluxo(cs) || cs.display === "contents") continue;
      return ehInline(cs) ? null : f;
    }
    return null;
  }
  function ultimoBlocoVisivel(el) {
    const filhos = Array.from(el.children);
    for (let i = filhos.length - 1; i >= 0; i--) {
      const f = filhos[i];
      if (!visivel(f)) continue;
      const cs = estilo(f);
      if (foraDoFluxo(cs) || cs.display === "contents") continue;
      return ehInline(cs) ? null : f;
    }
    return null;
  }
  var zerar = (el, props) => props.forEach((p) => el.style.setProperty(p, "0px", "important"));
  function acabamento(area, truncarTopo) {
    for (const p of Array.from(area.querySelectorAll(`[${ATR_CONT}],[${ATR_SEGUE}]`))) {
      const cs = estilo(p);
      const clone = (cs.getPropertyValue("box-decoration-break") || cs.getPropertyValue("-webkit-box-decoration-break")) === "clone";
      if (p.hasAttribute(ATR_SEGUE)) {
        zerar(p, ["margin-bottom"]);
        if (!clone) zerar(p, ["border-bottom-width", "padding-bottom", "border-bottom-left-radius", "border-bottom-right-radius"]);
        if (cs.display !== "table") {
          const u = ultimoBlocoVisivel(p);
          if (u) zerar(u, ["margin-bottom"]);
        }
      }
      if (p.hasAttribute(ATR_CONT)) {
        zerar(p, ["margin-top"]);
        if (!clone) zerar(p, ["border-top-width", "padding-top", "border-top-left-radius", "border-top-right-radius"]);
      }
    }
    if (!truncarTopo) return;
    for (let el = primeiroBlocoVisivel(area), n = 0; el && n < 64; n++) {
      zerar(el, ["margin-top"]);
      const cs = estilo(el);
      if (cs.display === "table") break;
      const c = caixa(cs);
      const colapsa = (cs.display === "block" || cs.display === "list-item") && cs.overflowY === "visible" && c.bt === 0 && c.pt === 0;
      if (!el.hasAttribute(ATR_CONT) && !colapsa) break;
      el = primeiroBlocoVisivel(el);
    }
  }
  function fimDaArea(area) {
    const rodape = area.parentElement?.querySelector(":scope > .folha-rodape");
    return (rodape ?? area).getBoundingClientRect()[rodape ? "top" : "bottom"];
  }
  var alturaUtil = (area) => fimDaArea(area) - area.getBoundingClientRect().top;
  function alturaConteudo(area) {
    const topo = area.getBoundingClientRect().top;
    let fim = topo;
    for (const f of Array.from(area.children)) {
      if (!visivel(f) || foraDoFluxo(estilo(f))) continue;
      fim = Math.max(fim, f.getBoundingClientRect().bottom);
    }
    return fim - topo;
  }
  function estenderPedacos(area) {
    const fimArea = fimDaArea(area);
    for (const p of Array.from(area.querySelectorAll(`[${ATR_SEGUE}]`))) {
      const cs = estilo(p);
      if (cs.display === "table" || ehInline(cs)) continue;
      let desconto = 0;
      for (let a = p.parentElement; a && a !== area; a = a.parentElement) {
        if (a.hasAttribute(ATR_SEGUE)) {
          const c2 = caixa(estilo(a), escalaVisual(a));
          desconto += c2.bb + c2.pb;
        }
      }
      const q = p.getBoundingClientRect();
      const alvo = fimArea - desconto - q.top;
      if (alvo <= q.height) continue;
      const c = caixa(cs);
      const alvoLayout = alvo / escalaVisual(p);
      const altura = cs.boxSizing === "border-box" ? alvoLayout : alvoLayout - (c.bt + c.pt + c.pb + c.bb);
      if (/grid|flex/.test(cs.display) && cs.alignContent === "normal") p.style.setProperty("align-content", "start", "important");
      p.style.setProperty("min-height", altura + "px", "important");
    }
  }

  // src/folhas.ts
  var ID_CSS = "pmvni-folha-css";
  var MAX_FOLHAS = 5e3;
  var ATR_LARGO = "data-pmvni-largo";
  function prepararDocumento() {
    const existente = document.querySelector("div.folha.pmvni-medida > .area");
    if (existente) return existente;
    const tirarPage = (lista, apagar) => {
      for (let i = lista.length - 1; i >= 0; i--) {
        const r = lista[i];
        if (r instanceof CSSPageRule) apagar(i);
        else if (r instanceof CSSGroupingRule) tirarPage(r.cssRules, (j) => r.deleteRule(j));
      }
    };
    for (const s of Array.from(document.styleSheets)) {
      try {
        tirarPage(s.cssRules, (i) => s.deleteRule(i));
      } catch {
      }
    }
    const css = document.createElement("style");
    css.id = ID_CSS;
    css.textContent = folha_default;
    document.head.appendChild(css);
    const medida = document.createElement("div");
    medida.className = "folha pmvni-medida";
    const fluxo = document.createElement("div");
    fluxo.className = "area pmvni-fluxo";
    while (document.body.firstChild) fluxo.appendChild(document.body.firstChild);
    medida.appendChild(fluxo);
    document.body.appendChild(medida);
    return fluxo;
  }
  function criarFolha(cfg) {
    const folha = document.createElement("section");
    folha.className = "folha";
    const cab = document.createElement("div");
    cab.className = "folha-cabecalho";
    const img = document.createElement("img");
    img.alt = "";
    img.src = cfg.cabecalhoUri;
    cab.appendChild(img);
    const area = document.createElement("div");
    area.className = "area";
    const rodape = document.createElement("div");
    rodape.className = "folha-rodape";
    const esquerda = document.createElement("div");
    esquerda.className = "folha-rodape-esq";
    esquerda.textContent = cfg.rodapeEsquerda;
    const direita = document.createElement("div");
    direita.className = "folha-rodape-dir";
    rodape.append(esquerda, direita);
    folha.append(cab, area, rodape);
    return { folha, area, direita };
  }
  function montarFolha(fluxo, cfg, de, ate, truncarTopo) {
    const f = criarFolha(cfg);
    document.body.appendChild(f.folha);
    copiarIntervalo(fluxo, f.area, de, ate);
    acabamento(f.area, truncarTopo);
    return f;
  }
  function montarFolhaDoPlano(plano, indice, cfg) {
    const fluxo = prepararDocumento();
    document.documentElement.style.setProperty("--pmvni-k", String(plano.escala));
    const de = plano.inicioDasFolhas[indice];
    if (!de) throw new ErroComposicao("folha_inexistente", `O plano não tem a folha ${indice + 1}.`);
    const f = montarFolha(fluxo, cfg, de, plano.inicioDasFolhas[indice + 1] ?? null, truncaTopo(de, indice));
    estenderPedacos(f.area);
    return f;
  }
  async function aplicarPlano(pedido) {
    const { plano, config } = pedido;
    if (plano.sha256Html !== pedido.sha256Documento) {
      throw new ErroComposicao("plano_de_outro_documento", "O plano de quebras foi calculado para outro documento.");
    }
    const limite = pedido.limiteEstabilidadeMs ?? LIMITE_ESTABILIDADE_MS;
    const fluxo = prepararDocumento();
    plano.inicioDasFolhas.forEach((p, i) => {
      if (!elementoEm(fluxo, p.caminho)) {
        throw new ErroComposicao("plano_invalido", `O início da folha ${i + 1} do plano não existe neste documento.`);
      }
    });
    for (const f of Array.from(document.querySelectorAll("section.folha"))) f.remove();
    fluxo.parentElement.setAttribute("data-pmvni-guardado", "");
    aplicarRefs(fluxo, pedido.refs);
    document.documentElement.style.setProperty("--pmvni-k", String(plano.escala));
    const montadas = plano.inicioDasFolhas.map((de, i) => montarFolha(fluxo, config, de, plano.inicioDasFolhas[i + 1] ?? null, truncaTopo(de, i)));
    numerarRodapes(montadas.map((f) => f.direita), config);
    await folhasProntas(montadas.map((f) => f.folha), limite);
    const H = alturaUtil(montadas[0].area);
    const folhasQueTransbordam = montadas.flatMap((f, i) => alturaConteudo(f.area) > H + EPS_PX ? [i + 1] : []);
    for (const f of montadas) estenderPedacos(f.area);
    return { folhas: montadas.length, folhasQueTransbordam, refsPorFolha: refsDasFolhas(montadas.map((f) => f.folha)) };
  }
  function numerarRodapes(direitas, cfg) {
    direitas.forEach((d, i) => {
      d.textContent = cfg.rodapeDireitaModelo.replaceAll("{n}", String(i + 1)).replaceAll("{t}", String(direitas.length));
    });
  }
  function refsDasFolhas(folhas2) {
    return folhas2.map((f) => {
      const ids = [];
      for (const e of Array.from(f.querySelectorAll(".area [data-ref]"))) {
        const id = e.getAttribute("data-ref");
        if (!ids.includes(id) && estilo(e).display !== "none") ids.push(id);
      }
      return ids;
    });
  }
  function larguraNoPlano(largura, areas, fluxo) {
    const visivelEm = (a, seletor) => Array.from(a.querySelectorAll(seletor)).some((e) => !e.closest(`[${ATR_OCULTO}]`));
    const avisos = /* @__PURE__ */ new Map();
    const registrar2 = (a) => {
      const chave = a.folha + "|" + (a.ref ?? "");
      if ((avisos.get(chave)?.escala ?? 0) < a.escala) avisos.set(chave, a);
    };
    largura.largos.forEach((l, i) => {
      const ref = refDe(fluxo, l.el);
      let folha = 1 + areas.findIndex((a) => visivelEm(a, `[${ATR_LARGO}="${i}"]`));
      if (folha === 0 && ref !== null) folha = 1 + areas.findIndex((a) => visivelEm(a, `[data-ref="${CSS.escape(ref)}"]`));
      if (folha === 0) folha = 1;
      registrar2({ folha, ref, escala: (l.direita - largura.esquerda) / largura.area, larguraMm: mm(l.direita - largura.esquerda) });
    });
    if (largura.largos.length === 0 && largura.pedida > 1) {
      registrar2({ folha: 1, ref: null, escala: largura.pedida, larguraMm: mm(largura.pedida * largura.area) });
    }
    const avisosLargura = [...avisos.values()].sort((a, b) => a.folha - b.folha || b.escala - a.escala);
    const estouros = [];
    for (const a of avisosLargura) {
      if (a.escala > ENCOLHIMENTO_MAXIMO + 1e-9) {
        const area = areas[a.folha - 1];
        estouros.push({ folha: a.folha, ref: a.ref, alturaMm: area ? mm(alturaConteudo(area)) : 0, larguraMm: a.larguraMm });
      }
    }
    return { avisosLargura, estouros };
  }
  function aplicarRefs(fluxo, refs) {
    for (const e of Array.from(fluxo.querySelectorAll("[data-ref]"))) e.removeAttribute("data-ref");
    for (const r of refs) fluxo.querySelectorAll(r.seletor)[r.indice]?.setAttribute("data-ref", r.id);
  }
  function refDe(fluxo, el) {
    if (!el) return null;
    const proprio = el.closest("[data-ref]");
    if (proprio && fluxo.contains(proprio)) return proprio.getAttribute("data-ref");
    let ultimo = null;
    for (const c of Array.from(fluxo.querySelectorAll("[data-ref]"))) {
      if (c.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) ultimo = c.getAttribute("data-ref");
      else break;
    }
    return ultimo;
  }
  var mm = (px) => Math.round(pxParaMm(px) * 100) / 100;
  async function compor(pedido) {
    const limite = pedido.limiteEstabilidadeMs ?? LIMITE_ESTABILIDADE_MS;
    const fluxo = prepararDocumento();
    const medida = fluxo.parentElement;
    for (const f of Array.from(document.querySelectorAll("section.folha"))) f.remove();
    medida.removeAttribute("data-pmvni-guardado");
    aplicarRefs(fluxo, pedido.refs);
    const raiz2 = document.documentElement;
    raiz2.style.setProperty("--pmvni-k", "1");
    await esperarEstabilidade(fluxo, limite);
    const largura = medirLargura(fluxo);
    for (const e of Array.from(fluxo.querySelectorAll(`[${ATR_LARGO}]`))) e.removeAttribute(ATR_LARGO);
    largura.largos.forEach((l, i) => l.el.setAttribute(ATR_LARGO, String(i)));
    const escala = largura.escala;
    if (escala !== 1) {
      raiz2.style.setProperty("--pmvni-k", String(escala));
      await esperarEstabilidade(fluxo, limite);
    }
    const sonda = criarFolha(pedido.config);
    document.body.appendChild(sonda.folha);
    const H = alturaUtil(sonda.area);
    sonda.folha.remove();
    const pontos = enumerarPontos(fluxo);
    const inicios = [];
    const estouros = [];
    const direitas = [];
    const areas = [];
    let ini = { pos: INICIO_DO_FLUXO, indice: -1, yInicio: fluxo.getBoundingClientRect().top, decoInicio: 0 };
    for (let n = 1; ; n++) {
      if (n > MAX_FOLHAS) throw new ErroComposicao("folhas_demais", `O documento passaria de ${MAX_FOLHAS} folhas.`);
      const truncar = truncaTopo(ini.pos, n - 1);
      const altura = (p2) => p2.yFim - ini.yInicio + ini.decoInicio + p2.decoFim;
      const candidatos = [];
      for (let j = ini.indice + 1; j < pontos.length; j++) {
        const p2 = pontos[j];
        if (p2.yFim - ini.yInicio > H + EPS_PX) break;
        if (altura(p2) <= H + EPS_PX) candidatos.push(p2);
        if (p2.forcado) break;
      }
      const ordem = [...candidatos].sort((a, b) => Number(b.forcado) - Number(a.forcado) || b.apelo - a.apelo || b.indice - a.indice);
      let escolhido = null;
      let montada = null;
      for (const p2 of ordem) {
        const pos2 = resolver(p2);
        if (pos2 === void 0) continue;
        const f = montarFolha(fluxo, pedido.config, ini.pos, pos2, truncar);
        await imagensProntas(f.area, limite);
        if (alturaConteudo(f.area) <= H + EPS_PX) {
          escolhido = { p: p2, pos: pos2 };
          montada = f;
          break;
        }
        f.folha.remove();
      }
      if (!escolhido) {
        for (let j = ini.indice + 1; j < pontos.length && !escolhido; j++) {
          const pos2 = resolver(pontos[j]);
          if (pos2 !== void 0) escolhido = { p: pontos[j], pos: pos2 };
        }
        montada = montarFolha(fluxo, pedido.config, ini.pos, escolhido.pos, truncar);
        await imagensProntas(montada.area, limite);
        const h = alturaConteudo(montada.area);
        if (h > H + EPS_PX) {
          estouros.push({ folha: n, ref: refDe(fluxo, elementoEm(fluxo, ini.pos.caminho)), alturaMm: mm(h), larguraMm: null });
        }
      }
      estenderPedacos(montada.area);
      inicios.push(ini.pos);
      direitas.push(montada.direita);
      areas.push(montada.area);
      const { p, pos } = escolhido;
      if (pos === null) break;
      ini = { pos: { ...pos, forcada: p.forcado }, indice: p.indice, yInicio: p.yInicio, decoInicio: p.decoInicio };
    }
    const { avisosLargura, estouros: deLargura } = larguraNoPlano(largura, areas, fluxo);
    estouros.push(...deLargura);
    estouros.sort((a, b) => a.folha - b.folha);
    medida.setAttribute("data-pmvni-guardado", "");
    const folhas2 = Array.from(document.querySelectorAll("section.folha"));
    numerarRodapes(direitas, pedido.config);
    await imagensProntas(document.body, limite);
    const refsPorFolha = refsDasFolhas(folhas2);
    return {
      plano: { versao: VERSAO_PLANO, inicioDasFolhas: inicios, estouros, sha256Html: pedido.sha256Html, escala, avisosLargura },
      folhas: folhas2.length,
      refsPorFolha
    };
  }
  var texto = (v, nome) => {
    if (typeof v !== "string") throw new Error(`campo ${nome} invalido`);
    return v;
  };
  function lerComum(d) {
    if (!Array.isArray(d.refs)) throw new Error("campo refs invalido");
    const refs = d.refs.map((r, i) => {
      const o = r;
      if (typeof o !== "object" || o === null || typeof o.indice !== "number") throw new Error(`refs[${i}] invalido`);
      return { seletor: texto(o.seletor, "seletor"), indice: o.indice, id: texto(o.id, "id") };
    });
    const c = d.config;
    if (typeof c !== "object" || c === null) throw new Error("campo config invalido");
    const limite = d.limiteEstabilidadeMs;
    if (limite != null && (typeof limite !== "number" || !(limite > 0))) throw new Error("campo limiteEstabilidadeMs invalido");
    return {
      refs,
      config: {
        cabecalhoUri: texto(c.cabecalhoUri, "cabecalhoUri"),
        rodapeEsquerda: texto(c.rodapeEsquerda, "rodapeEsquerda"),
        rodapeDireitaModelo: texto(c.rodapeDireitaModelo, "rodapeDireitaModelo")
      },
      limiteEstabilidadeMs: limite == null ? null : limite
    };
  }
  function lerPedido(d) {
    return { sha256Html: texto(d.sha256Html, "sha256Html"), ...lerComum(d) };
  }
  function lerPedidoAplicar(d) {
    return { plano: lerPlano(d.plano), sha256Documento: texto(d.sha256Documento, "sha256Documento"), ...lerComum(d) };
  }

  // src/texto.ts
  var normalizar = (s) => s.replace(/\s+/g, " ").trim();
  function textoPorFolha() {
    return Array.from(document.querySelectorAll("section.folha")).map((f) => {
      const area = f.querySelector(":scope > .area");
      return area ? normalizar(area.innerText) : "";
    });
  }

  // src/tela.ts
  var ID_CSS2 = "pmvni-tela-css";
  var ATR_TRANSBORDA = "data-pmvni-transborda";
  var PX_POR_MM2 = 96 / 25.4;
  var LARGURA_FOLHA_PX = 210 * PX_POR_MM2;
  var ALTURA_FOLHA_PX = 297 * PX_POR_MM2;
  var MARGEM_AJUSTE_PX = 16;
  var ZOOM_MIN = 0.5;
  var ZOOM_MAX = 3;
  var ESPERA_FOLHA_VISIVEL_MS = 120;
  var GOLA_ESQUERDA_PX = 12;
  var CSS_TELA = `
@media screen {
  :root { --pmvni-zoom: 1; --pmvni-tela-x: 0px; --pmvni-tela-fundo: #e4e4e4; --pmvni-tela-sombra: rgba(0, 0, 0, .30); }
  html { background: var(--pmvni-tela-fundo) !important; height: var(--pmvni-tela-altura, auto); overflow-y: scroll; }
  body {
    background: transparent !important; margin: 0 !important; padding: 8mm 0 !important; width: 210mm !important;
    box-sizing: content-box !important; transform-origin: 0 0; transform: translateX(var(--pmvni-tela-x)) scale(var(--pmvni-zoom));
  }
  section.folha { margin: 0 0 8mm 0 !important; box-shadow: 0 1px 4px var(--pmvni-tela-sombra), 0 0 0 1px rgba(0, 0, 0, .08); }
  section.folha:last-of-type { margin-bottom: 0 !important; }
  /* Folha que transborda: faixa vermelha fina na margem cinza, fora da folha. */
  section.folha[${ATR_TRANSBORDA}] { box-shadow: -5px 0 0 0 #c42b1c, 0 1px 4px var(--pmvni-tela-sombra), 0 0 0 1px rgba(0, 0, 0, .08); }
}
@media screen and (prefers-color-scheme: dark) {
  :root { --pmvni-tela-fundo: #1f1f1f; --pmvni-tela-sombra: rgba(0, 0, 0, .75); }
}
`;
  var zoom = 1;
  var ativa = false;
  var observador = null;
  var temporizador;
  var ultimaFolha = 0;
  var folhas = () => Array.from(document.querySelectorAll("section.folha"));
  var raiz = document.documentElement;
  function ativar(enviar2) {
    if (ativa) return;
    ativa = true;
    const css = document.createElement("style");
    css.id = ID_CSS2;
    css.textContent = CSS_TELA;
    document.head.appendChild(css);
    window.addEventListener("resize", () => {
      centralizar();
      agendarFolhaVisivel(enviar2);
    });
  }
  function folhaAtual() {
    const lista = folhas();
    if (lista.length === 0) return 0;
    const meio = raiz.clientHeight / 2;
    let melhor = 0;
    let distancia = Infinity;
    lista.forEach((f, i) => {
      const r = f.getBoundingClientRect();
      const d = meio < r.top ? r.top - meio : meio > r.bottom ? meio - r.bottom : 0;
      if (d < distancia) {
        distancia = d;
        melhor = i;
      }
    });
    return melhor + 1;
  }
  function centralizar() {
    const larguraNaTela = LARGURA_FOLHA_PX * zoom;
    const x = Math.max(GOLA_ESQUERDA_PX, (raiz.clientWidth - larguraNaTela) / 2);
    raiz.style.setProperty("--pmvni-tela-x", `${x}px`);
  }
  function aplicarZoom(novo) {
    const antes = zoom;
    const centroDoc = (window.scrollY + raiz.clientHeight / 2) / antes;
    zoom = novo;
    raiz.style.setProperty("--pmvni-zoom", String(zoom));
    centralizar();
    raiz.style.setProperty("--pmvni-tela-altura", `${document.body.offsetHeight * zoom}px`);
    if (antes !== zoom) window.scrollTo({ top: Math.max(0, centroDoc * zoom - raiz.clientHeight / 2), behavior: "instant" });
  }
  function limitar(z) {
    return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, z));
  }
  function observar(enviar2) {
    observador?.disconnect();
    observador = new IntersectionObserver(() => agendarFolhaVisivel(enviar2), { threshold: [0, 0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1] });
    for (const f of folhas()) observador.observe(f);
    ultimaFolha = 0;
    agendarFolhaVisivel(enviar2);
  }
  function agendarFolhaVisivel(enviar2) {
    window.clearTimeout(temporizador);
    temporizador = window.setTimeout(() => {
      const folha = folhaAtual();
      if (folha === 0 || folha === ultimaFolha) return;
      ultimaFolha = folha;
      enviar2("folhaVisivel", "folhaVisivel", estado());
    }, ESPERA_FOLHA_VISIVEL_MS);
  }
  function estado() {
    return { zoom, folha: folhaAtual(), total: folhas().length, rolagemY: Math.round(window.scrollY) };
  }
  function numeroOpcional(valor, nome) {
    if (valor === void 0 || valor === null) return void 0;
    if (typeof valor !== "number" || !Number.isFinite(valor)) throw new ErroComposicao("zoom_invalido", `"${nome}" não é um número finito.`);
    return valor;
  }
  function registrarTela(registrar2, enviar2) {
    registrar2("definirZoom", (dados) => {
      const pedido = numeroOpcional(dados.zoom, "zoom");
      const ajustar = dados.ajustar;
      if (ajustar !== void 0 && ajustar !== "largura" && ajustar !== "pagina") {
        throw new ErroComposicao("zoom_invalido", 'ajustar deve ser "largura" ou "pagina".');
      }
      const transbordam = dados.transbordam;
      if (transbordam !== void 0 && (!Array.isArray(transbordam) || transbordam.some((n) => !Number.isInteger(n)))) {
        throw new ErroComposicao("zoom_invalido", "transbordam deve ser uma lista de números de folha.");
      }
      ativar(enviar2);
      const lista = folhas();
      if (transbordam) {
        lista.forEach((f, i) => f.toggleAttribute(ATR_TRANSBORDA, transbordam.includes(i + 1)));
      }
      const maximo = numeroOpcional(dados.maximo, "maximo");
      let alvo = zoom;
      if (ajustar === "largura") {
        alvo = (raiz.clientWidth - 2 * MARGEM_AJUSTE_PX) / LARGURA_FOLHA_PX;
        if (maximo !== void 0) alvo = Math.min(alvo, maximo);
      } else if (ajustar === "pagina") {
        alvo = Math.min((raiz.clientWidth - 2 * MARGEM_AJUSTE_PX) / LARGURA_FOLHA_PX, (raiz.clientHeight - 2 * MARGEM_AJUSTE_PX) / ALTURA_FOLHA_PX);
      } else if (pedido !== void 0) alvo = pedido;
      aplicarZoom(limitar(alvo));
      observar(enviar2);
      return { tipo: "folhaVisivel", dados: estado() };
    });
    registrar2("rolarPara", (dados) => {
      const lista = folhas();
      const margem = 8 * PX_POR_MM2 * zoom;
      let destino;
      let ajuste = 0;
      if (typeof dados.ref === "string") {
        const alvo = lista.map((f) => f.querySelector(`.area [data-ref="${CSS.escape(dados.ref)}"]`)).find((e) => e !== null && e !== void 0);
        if (!alvo) throw new ErroComposicao("ref_inexistente", `Nenhuma folha mostra o bloco "${dados.ref}".`);
        const folha = alvo.closest("section.folha");
        const naFolha = alvo.getBoundingClientRect().top - folha.getBoundingClientRect().top;
        destino = naFolha < 40 * PX_POR_MM2 * zoom ? folha : alvo;
        ajuste = destino === folha ? margem : 16;
      } else if (typeof dados.folha === "number" && Number.isInteger(dados.folha)) {
        const f = lista[dados.folha - 1];
        if (!f) throw new ErroComposicao("folha_inexistente", `O documento não tem a folha ${dados.folha}.`);
        destino = f;
        ajuste = margem;
      } else {
        throw new ErroComposicao("rolagem_invalida", 'rolarPara pede "folha" (inteiro) ou "ref" (texto).');
      }
      window.scrollTo({ top: Math.max(0, window.scrollY + destino.getBoundingClientRect().top - ajuste), behavior: "instant" });
      return { tipo: "folhaVisivel", dados: estado() };
    });
  }

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
      const codigo = e instanceof ErroComposicao ? e.codigo : "falha_no_manipulador";
      enviar("erro", msg.id, { codigo, mensagem: e instanceof Error ? e.message : String(e) });
    }
  }
  registrar("compor", async (dados) => ({ tipo: "composto", dados: { ...await compor(lerPedido(dados)) } }));
  registrar("aplicarPlano", async (dados) => ({ tipo: "planoAplicado", dados: { ...await aplicarPlano(lerPedidoAplicar(dados)) } }));
  registrar("textoPorFolha", () => ({ tipo: "textoPorFolha", dados: { folhas: textoPorFolha() } }));
  registrarTela(registrar, enviar);
  if (webview) {
    webview.addEventListener("message", (e) => void aoReceber(e.data));
    enviar("pronto", "pronto", { versao: VERSAO });
  } else {
    console.error("[ponte] window.chrome.webview indisponivel");
  }
  return __toCommonJS(index_exports);
})();
