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

  // correcoes.css
  var correcoes_default = '/* Correções do editor ao modelo oficial (embutido no paginador.js e inserido depois do relatorio.css do kit, que fica\n   byte a byte como veio). Só entra aqui o que o modelo desenha errado com texto livre: caixa `white-space: nowrap` de\n   largura fixa cujo texto, quando é maior que o previsto, cobre o vizinho ou sai da folha.\n   REGRA DE OURO: onde o texto já cabia, nada muda (mesmas caixas, mesma paginação, mesma fidelidade com o PDF do kit).\n   Teste: CorrecoesDoModeloTestes (liga e desliga esta folha e compara as caixas no corpus e no dados_modelo.json). */\n\n/* Ficha da Localização (tabelas rótulo | valor): o modelo pressupõe "rótulos curtos" (`.lfl` com nowrap numa coluna de\n   24 %, tabela de layout fixo); um rótulo maior ("INSCRIÇÃO IMOBILIÁRIA", "ÁREA DO LOTE (MATRÍCULA)") era pintado por\n   cima do valor. O rótulo passa a quebrar linha dentro da coluna (e, se for uma palavra só maior que a coluna, quebra\n   dentro da palavra). Sem o padding direito, a coluna dá ao texto os mesmos 8 px que ele já usava sem tocar no valor\n   (o valor tem o próprio padding esquerdo): rótulo que cabia em uma linha continua em uma linha, no mesmo lugar. */\n.lft td.lfl { white-space: normal; overflow-wrap: break-word; padding-right: 0; }\n\n/* Etiqueta do título de seção (`.sec > .tag`: resumo da análise, "ZONA INDICADA: …", etiqueta da iluminação): nowrap e\n   `flex: none`, com texto livre do dados.json (indices.zona, iluminacao.tag). Etiqueta longa espremia o título até o\n   mínimo e depois saía da folha pela direita (o documento inteiro era reduzido, ou a exportação recusada). Agora, só\n   quando o título já está no mínimo, a etiqueta cede e quebra linha. O encolhimento do título pesa um milhão de vezes\n   mais que o da etiqueta: enquanto o título ainda pode encolher, a etiqueta fica na largura inteira, como no kit. */\n.sec > .tag { flex: 0 1 auto; white-space: normal; }\n.sec > div:first-child { flex-shrink: 1000000; }\n';

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
  var truncaTopo = (inicio, indice2) => indice2 > 0 && !inicio.forcada;
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
      avisosLargura: p.avisosLargura,
      figuras: Array.isArray(p.figuras) ? p.figuras.map((a, n) => {
        if (!obj(a) || !Array.isArray(a.caminho) || a.caminho.length === 0 || !a.caminho.every(inteiro) || typeof a.alturaPx !== "number" || !Number.isFinite(a.alturaPx) || a.alturaPx <= 0) return falha(`figuras[${n}]`);
        return { caminho: [...a.caminho], alturaPx: a.alturaPx };
      }) : null
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
    const base2 = quebra === "clone" ? c.bb + c.pb : 0;
    if (cs.display === "table") {
      const t = el;
      if (t.tHead && estilo(t.tHead).display === "table-header-group") {
        topo += t.tHead.getBoundingClientRect().height + num(cs.borderSpacing.split(" ").pop()) * escala;
      }
    }
    return { topo, base: base2 };
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
  var ID_CSS_CORRECOES = "pmvni-correcoes-css";
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
    const correcoes = document.createElement("style");
    correcoes.id = ID_CSS_CORRECOES;
    correcoes.textContent = correcoes_default;
    document.head.appendChild(correcoes);
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
  function montarFolhaDoPlano(plano, indice2, cfg) {
    const fluxo = prepararDocumento();
    document.documentElement.style.setProperty("--pmvni-k", String(plano.escala));
    aplicarAjustesFiguras(fluxo, plano.figuras ?? []);
    const de = plano.inicioDasFolhas[indice2];
    if (!de) throw new ErroComposicao("folha_inexistente", `O plano não tem a folha ${indice2 + 1}.`);
    const f = montarFolha(fluxo, cfg, de, plano.inicioDasFolhas[indice2 + 1] ?? null, truncaTopo(de, indice2));
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
    aplicarAjustesFiguras(fluxo, plano.figuras ?? []);
    const montadas = plano.inicioDasFolhas.map((de, i) => montarFolha(fluxo, config, de, plano.inicioDasFolhas[i + 1] ?? null, truncaTopo(de, i)));
    numerarRodapes(montadas.map((f) => f.direita), config);
    await folhasProntas(montadas.map((f) => f.folha), limite);
    const H = alturaUtil(montadas[0].area);
    const folhasQueTransbordam = montadas.flatMap((f, i) => alturaConteudo(f.area) > H + EPS_PX ? [i + 1] : []);
    for (const f of montadas) estenderPedacos(f.area);
    const folhasQueTransbordamNaLargura = montadas.flatMap((f, i) => transbordaNaLargura(f.area) ? [i + 1] : []);
    return { folhas: montadas.length, folhasQueTransbordam, folhasQueTransbordamNaLargura, refsPorFolha: refsDasFolhas(montadas.map((f) => f.folha)) };
  }
  function transbordaNaLargura(area) {
    const r = area.getBoundingClientRect();
    const intervalo = document.createRange();
    intervalo.selectNodeContents(area);
    for (const q of Array.from(intervalo.getClientRects())) {
      if (q.width > 0 && q.height > 0 && (q.right > r.right + EPS_PX + 0.45 || q.left < r.left - EPS_PX - 0.45)) return true;
    }
    return false;
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
    const ajustes = /* @__PURE__ */ new Map();
    aplicarAjustesFiguras(fluxo, []);
    let r = await paginar(fluxo, pedido, H, limite);
    for (let rodada = 0; rodada < RODADAS_FIGURAS; rodada++) {
      const novos = ajustesDeFiguras(fluxo, r, H);
      if (novos.size === 0) break;
      for (const [fig, px] of novos) ajustes.set(fig, px);
      for (const f of Array.from(document.querySelectorAll("section.folha"))) f.remove();
      aplicarAjustesFiguras(fluxo, [...ajustes].map(([fig, px]) => ({ caminho: caminhoDe(fluxo, fig), alturaPx: px })));
      await esperarEstabilidade(fluxo, limite);
      r = await paginar(fluxo, pedido, H, limite);
    }
    const { inicios, estouros, direitas, areas } = r;
    const figuras = ajustes.size === 0 ? null : [...ajustes].map(([fig, px]) => ({ caminho: caminhoDe(fluxo, fig), alturaPx: px }));
    const { avisosLargura, estouros: deLargura } = larguraNoPlano(largura, areas, fluxo);
    estouros.push(...deLargura);
    estouros.sort((a, b) => a.folha - b.folha);
    medida.setAttribute("data-pmvni-guardado", "");
    const folhas2 = Array.from(document.querySelectorAll("section.folha"));
    numerarRodapes(direitas, pedido.config);
    await imagensProntas(document.body, limite);
    const refsPorFolha = refsDasFolhas(folhas2);
    return {
      plano: { versao: VERSAO_PLANO, inicioDasFolhas: inicios, estouros, sha256Html: pedido.sha256Html, escala, avisosLargura, figuras },
      folhas: folhas2.length,
      refsPorFolha
    };
  }
  var RODADAS_FIGURAS = 3;
  var FIGURA_REDUCAO_MAXIMA = 0.4;
  var FIGURA_ALTURA_MINIMA_MM = 40;
  var SELETOR_IMG_FIGURA = ":scope > .fi img";
  function caminhoDe(fluxo, el) {
    const c = [];
    for (let e = el; e !== fluxo && e.parentElement; e = e.parentElement) c.unshift(Array.prototype.indexOf.call(e.parentElement.children, e));
    return c;
  }
  function aplicarAjustesFiguras(fluxo, ajustes) {
    for (const img of Array.from(fluxo.querySelectorAll(".fig > .fi img"))) img.style.removeProperty("max-height");
    for (const a of ajustes) {
      const img = elementoEm(fluxo, a.caminho)?.querySelector(SELETOR_IMG_FIGURA);
      if (img) img.style.setProperty("max-height", `${a.alturaPx.toFixed(2)}px`);
    }
  }
  function ajustesDeFiguras(fluxo, r, H) {
    const novos = /* @__PURE__ */ new Map();
    for (let i = 0; i + 1 < r.areas.length; i++) {
      if (r.inicios[i + 1].forcada) continue;
      const fig = primeiroVisivel(r.areas[i + 1]);
      if (!fig?.matches(".fig")) continue;
      const img = fig.querySelector(SELETOR_IMG_FIGURA);
      const original = elementoEm(fluxo, r.inicios[i + 1].caminho);
      const imgFluxo = original?.querySelector(SELETOR_IMG_FIGURA);
      if (!img || !original || !imgFluxo || !original.matches(".fig")) continue;
      const sobra = H - alturaConteudo(r.areas[i]);
      const rFig = fig.getBoundingClientRect();
      const margem = parseFloat(estilo(fig).marginTop) || 0;
      const precisa = rFig.height + margem - sobra + 2;
      const rImg = img.getBoundingClientRect();
      if (precisa <= 0 || rImg.height <= 0) continue;
      const textos = Array.from(fig.children).filter((c) => !c.matches(".fi")).reduce((m, c) => Math.max(m, c.getBoundingClientRect().height), 0);
      const novaTela = rImg.height - precisa;
      const escalaTela = rImg.height / imgFluxo.offsetHeight;
      const minimoTela = Math.max(rImg.height * (1 - FIGURA_REDUCAO_MAXIMA), mmParaPx(FIGURA_ALTURA_MINIMA_MM) * escalaTela, textos);
      if (novaTela < minimoTela || !Number.isFinite(escalaTela) || escalaTela <= 0) continue;
      novos.set(original, novaTela / escalaTela);
    }
    return novos;
  }
  function primeiroVisivel(area) {
    for (const c of Array.from(area.children)) if (!c.hasAttribute(ATR_OCULTO) && estilo(c).display !== "none") return c;
    return null;
  }
  var mmParaPx = (mm2) => mm2 * 96 / 25.4;
  async function paginar(fluxo, pedido, H, limite) {
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
    return { inicios, estouros, direitas, areas };
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

  // src/linhas.ts
  var ATR_LINHA = "data-pmvni-linha";
  var ATR_ENVOLTO = "data-pmvni-envolto";
  var ATR_CAMPO = "data-campo";
  var SELETOR_LINHAS = `section.folha .area tr[${ATR_LINHA}]`;
  var SELETOR_CARDS = "section.folha .area .card[data-ref]";
  var SEPARADOR_VAO = " • ";
  var PREFIXO_AREA = "ÁREA: ";
  var TAG_TRECHO = "pmvni-trecho";
  var TABELAS = [["ind", "indices"], ["ilu", "ambientes"]];
  var ehLinha = (ref) => typeof ref === "string" && /^(indices|ambientes)\/\d{2,}$/.test(ref);
  var ehCaminho = (v) => typeof v === "string" && /^[a-z_]+(\/([0-9]+|[a-z_]+))*$/.test(v);
  var visivel2 = (e) => !e.closest(`[${ATR_OCULTO}]`);
  function pedacosDoBloco(ref) {
    if (ehLinha(ref)) {
      return Array.from(document.querySelectorAll(SELETOR_LINHAS)).filter((e) => e.getAttribute(ATR_LINHA) === ref && visivel2(e));
    }
    return Array.from(document.querySelectorAll(SELETOR_CARDS)).filter((e) => e.getAttribute("data-ref") === ref);
  }
  function blocoDe(alvo) {
    const card = alvo.closest(SELETOR_CARDS);
    if (card) return { el: card, ref: card.getAttribute("data-ref"), linha: false };
    const tr = alvo.closest(SELETOR_LINHAS);
    if (tr) return { el: tr, ref: tr.getAttribute(ATR_LINHA), linha: true };
    return null;
  }
  function marcarLinhas() {
    for (const [classe, bloco] of TABELAS) {
      for (const t of Array.from(document.querySelectorAll(`section.folha .area table.tb.${classe}`))) {
        const linhas = Array.from(t.tBodies).flatMap((b) => Array.from(b.rows));
        linhas.forEach((tr, i) => {
          const rotulo = tr.querySelector(":scope > td.nm span.sm")?.textContent ?? "";
          const m = /(\d+)\s*$/.exec(rotulo);
          const n = m ? parseInt(m[1], 10) : i + 1;
          tr.setAttribute(ATR_LINHA, `${bloco}/${String(n).padStart(2, "0")}`);
        });
      }
    }
  }
  function marcar(el, campo) {
    if (!el) return 0;
    el.setAttribute(ATR_CAMPO, campo);
    return 1;
  }
  function envolverTexto(no, campo) {
    if (no.data.length === 0) return 0;
    const span = document.createElement(TAG_TRECHO);
    span.setAttribute(ATR_ENVOLTO, "");
    span.setAttribute(ATR_CAMPO, campo);
    no.replaceWith(span);
    span.appendChild(no);
    return 1;
  }
  function textoUnico(el) {
    return el.childNodes.length === 1 && el.firstChild.nodeType === Node.TEXT_NODE ? el.firstChild : null;
  }
  var jaMarcados = (el) => el.querySelectorAll(`:scope > [${ATR_ENVOLTO}]`).length;
  function marcarIndice(tr) {
    const [nm, calculo, projeto, limite, situacao] = Array.from(tr.cells);
    marcar(nm?.querySelector(":scope .nmw > div > b"), "nome");
    marcar(nm?.querySelector(":scope .nmw > div > small"), "descricao");
    marcar(calculo, "calculo");
    marcar(projeto, "projeto");
    marcar(limite, "limite");
    marcar(situacao?.querySelector(":scope > .st"), "texto");
  }
  function marcarAmbiente(tr) {
    const [nm, vaos, minimo, situacao] = Array.from(tr.cells);
    marcar(nm?.querySelector(":scope .nmw > div > b"), "nome");
    nm?.querySelectorAll(":scope .nmw > div > small").forEach((s, i) => marcar(s, `detalhes/${i}`));
    vaos?.querySelectorAll(":scope > .vao").forEach((v, k) => {
      const b = v.querySelector(":scope > b");
      if (b && jaMarcados(b) === 0) {
        const no = textoUnico(b);
        const sep = no ? no.data.indexOf(SEPARADOR_VAO) : -1;
        if (!no || sep < 0) marcar(b, `vaos/${k}/codigo`);
        else {
          const resto = no.splitText(sep);
          const medidas = resto.splitText(SEPARADOR_VAO.length);
          envolverTexto(no, `vaos/${k}/codigo`);
          envolverTexto(medidas, `vaos/${k}/medidas`);
        }
      }
      marcar(v.querySelector(":scope > span"), `vaos/${k}/tipo`);
      const em = v.querySelector(":scope > em");
      if (em?.classList.contains("o")) marcar(em, `vaos/${k}/sem_area`);
      else if (em && jaMarcados(em) === 0) {
        const no = textoUnico(em);
        if (no?.data.startsWith(PREFIXO_AREA)) envolverTexto(no.splitText(PREFIXO_AREA.length), `vaos/${k}/area`);
      }
    });
    minimo?.querySelectorAll(":scope > div").forEach((d, i) => marcar(d, `minimo/${i}/texto`));
    if (situacao) {
      marcar(situacao.querySelector(":scope > .vres .vrt > b"), "resultado/total");
      marcar(situacao.querySelector(":scope > .vres > .vrc"), "resultado/conta");
      marcar(situacao.querySelector(":scope > .vres > .vrm"), "resultado/comparacao");
      marcar(situacao.querySelector(":scope > .st.sb"), "situacao_titulo");
      situacao.querySelectorAll(":scope > .st:not(.sb)").forEach((s, i) => marcar(s, `textos/${i}`));
    }
  }
  function marcarCamposLinhas() {
    marcarLinhas();
    for (const tr of Array.from(document.querySelectorAll(SELETOR_LINHAS))) {
      if (tr.closest("table")?.classList.contains("ind")) marcarIndice(tr);
      else marcarAmbiente(tr);
    }
  }
  function desenvolver(el) {
    if (!el.hasAttribute(ATR_ENVOLTO)) return false;
    const pai = el.parentElement;
    el.replaceWith(...Array.from(el.childNodes));
    pai?.normalize();
    return true;
  }

  // src/tela.ts
  var ID_CSS2 = "pmvni-tela-css";
  var ATR_TRANSBORDA = "data-pmvni-transborda";
  var PX_POR_MM2 = 96 / 25.4;
  var LARGURA_FOLHA_PX = 210 * PX_POR_MM2;
  var ALTURA_FOLHA_PX = 297 * PX_POR_MM2;
  var MARGEM_AJUSTE_PX = 16;
  var ZOOM_MIN = 0.5;
  var ZOOM_MIN_AJUSTE = 0.25;
  var ZOOM_MAX = 3;
  var ESPERA_FOLHA_VISIVEL_MS = 120;
  var GOLA_ESQUERDA_PX = 12;
  var GOLA_COM_MARCAS_PX = 72;
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
  var golaEsquerda = GOLA_ESQUERDA_PX;
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
    const x = Math.max(golaEsquerda, (raiz.clientWidth - larguraNaTela) / 2);
    raiz.style.setProperty("--pmvni-tela-x", `${x}px`);
  }
  var zoomAtual = () => zoom;
  function definirGola(px) {
    golaEsquerda = px;
    if (ativa) centralizar();
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
  function limitar(z, minimo = ZOOM_MIN) {
    return Math.min(ZOOM_MAX, Math.max(minimo, z));
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
      aplicarZoom(limitar(alvo, ajustar ? ZOOM_MIN_AJUSTE : ZOOM_MIN));
      observar(enviar2);
      return { tipo: "folhaVisivel", dados: estado() };
    });
    registrar2("rolarPara", (dados) => {
      if (typeof dados.ref === "string") {
        rolarAteRef(dados.ref);
      } else if (typeof dados.folha === "number" && Number.isInteger(dados.folha)) {
        const f = folhas()[dados.folha - 1];
        if (!f) throw new ErroComposicao("folha_inexistente", `O documento não tem a folha ${dados.folha}.`);
        irPara(f, 8 * PX_POR_MM2 * zoom);
      } else {
        throw new ErroComposicao("rolagem_invalida", 'rolarPara pede "folha" (inteiro) ou "ref" (texto).');
      }
      return { tipo: "folhaVisivel", dados: estado() };
    });
  }
  function irPara(destino, ajuste) {
    window.scrollTo({ top: Math.max(0, window.scrollY + destino.getBoundingClientRect().top - ajuste), behavior: "instant" });
  }
  function rolarAteRef(ref) {
    const alvo = folhas().map((f) => f.querySelector(`.area [data-ref="${CSS.escape(ref)}"]`)).find((e) => e !== null && e !== void 0);
    if (!alvo) throw new ErroComposicao("ref_inexistente", `Nenhuma folha mostra o bloco "${ref}".`);
    const folha = alvo.closest("section.folha");
    const naFolha = alvo.getBoundingClientRect().top - folha.getBoundingClientRect().top;
    const naFolhaTopo = naFolha < 40 * PX_POR_MM2 * zoom;
    irPara(naFolhaTopo ? folha : alvo, naFolhaTopo ? 8 * PX_POR_MM2 * zoom : 16);
  }

  // src/marcas.ts
  var ID_CAMADA = "pmvni-camada";
  var CLASSE_MARCA = "pmvni-marca";
  var CLASSE_TRACO = "pmvni-marca-traco";
  var CLASSE_EXCLUIDO = "pmvni-marca-excl";
  var CLASSE_LINHA = "pmvni-marca-linha";
  var TRACO_LARGURA_PX = 3;
  var TRACO_AFASTAMENTO_PX = 12;
  var BOTAO_FOLGA_PX = 6;
  var BOTAO_ALTURA_PX = 18;
  var LINHA_ESPESSURA_PX = 2;
  var BOTAO_FOLGA_JANELA_PX = 6;
  var CSS_MARCAS = `
@media screen {
  #${ID_CAMADA} { position: absolute; left: 0; top: 0; width: 0; height: 0; overflow: visible; z-index: 5; pointer-events: none; }
  #${ID_CAMADA} .${CLASSE_TRACO} { position: absolute; width: calc(${TRACO_LARGURA_PX}px / var(--pmvni-zoom, 1)); background: #1a73e8; border-radius: 2px; pointer-events: none; }
  #${ID_CAMADA} .${CLASSE_EXCLUIDO} {
    position: absolute; margin: 0; padding: 0 7px; box-sizing: border-box; height: ${BOTAO_ALTURA_PX}px; white-space: nowrap;
    font: 600 11px/16px "Segoe UI", system-ui, sans-serif; letter-spacing: .2px; color: #fff; background: #c42b1c; border: 1px solid #c42b1c; border-radius: 9px;
    cursor: pointer; pointer-events: auto; transform-origin: 0 0; transform: scale(calc(1 / var(--pmvni-zoom, 1))) translateX(-100%);
  }
  #${ID_CAMADA} .${CLASSE_EXCLUIDO}:hover { background: #a32418; border-color: #a32418; }
  #${ID_CAMADA} .${CLASSE_LINHA} { position: absolute; height: calc(${LINHA_ESPESSURA_PX}px / var(--pmvni-zoom, 1)); background: #c42b1c; pointer-events: none; }
}
@media screen and (prefers-color-scheme: dark) {
  #${ID_CAMADA} .${CLASSE_TRACO} { background: #6ea8fe; }
  #${ID_CAMADA} .${CLASSE_EXCLUIDO}:hover { background: #d9483a; border-color: #d9483a; }
}
@media print { #${ID_CAMADA} { display: none !important; } }
`;
  var pedacosDe = (ref) => pedacosDoBloco(ref);
  function limparMarcas() {
    document.getElementById(ID_CAMADA)?.remove();
  }
  function camada() {
    let c = document.getElementById(ID_CAMADA);
    if (c && c.parentElement !== document.body) {
      c.remove();
      c = null;
    }
    if (!c) {
      c = document.createElement("div");
      c.id = ID_CAMADA;
      document.body.appendChild(c);
    }
    return c;
  }
  function golaDosExcluidos() {
    const botoes = document.querySelectorAll(`#${ID_CAMADA} .${CLASSE_EXCLUIDO}`);
    let maior = 0;
    botoes.forEach((b) => {
      maior = Math.max(maior, b.offsetWidth);
    });
    return maior === 0 ? 0 : Math.ceil(maior + BOTAO_FOLGA_PX + BOTAO_FOLGA_JANELA_PX);
  }
  function desenharMarcas(m, aoClicarExcluido) {
    limparMarcas();
    if (!m.visiveis) return 0;
    const c = camada();
    const origem = c.getBoundingClientRect();
    const z = zoomAtual();
    const localX = (tela) => (tela - origem.left) / z;
    const localY = (tela) => (tela - origem.top) / z;
    let desenhadas = 0;
    for (const ref of m.alterados) {
      const pedacos = pedacosDe(ref);
      if (pedacos.length === 0) continue;
      desenhadas++;
      for (const p of pedacos) {
        const r = p.getBoundingClientRect();
        const folha = p.closest("section.folha").getBoundingClientRect();
        const traco = document.createElement("div");
        traco.className = `${CLASSE_MARCA} ${CLASSE_TRACO}`;
        traco.style.left = `calc(${localX(folha.left)}px - ${TRACO_AFASTAMENTO_PX}px / var(--pmvni-zoom, 1))`;
        traco.style.top = `${localY(r.top)}px`;
        traco.style.height = `${r.height / z}px`;
        c.appendChild(traco);
      }
    }
    const posicoes = m.excluidos.flatMap((e) => {
      const pedacos = pedacosDe(e.ancora);
      if (pedacos.length === 0) return [];
      const p = e.lado === "antes" ? pedacos[0] : pedacos[pedacos.length - 1];
      const r = p.getBoundingClientRect();
      const folha = p.closest("section.folha").getBoundingClientRect();
      return [{ e, x: localX(folha.left), largura: folha.width / z, y: localY(e.lado === "antes" ? r.top : r.bottom) }];
    }).sort((a, b) => a.y - b.y);
    let ultimo = -Infinity;
    const linhas = /* @__PURE__ */ new Set();
    for (const { e, x, largura, y } of posicoes) {
      const chave = `${x.toFixed(1)}|${y.toFixed(1)}`;
      if (!linhas.has(chave)) {
        linhas.add(chave);
        const linha = document.createElement("div");
        linha.className = `${CLASSE_MARCA} ${CLASSE_LINHA}`;
        linha.style.left = `${x}px`;
        linha.style.width = `${largura}px`;
        linha.style.top = `calc(${y}px - ${LINHA_ESPESSURA_PX / 2}px / var(--pmvni-zoom, 1))`;
        c.appendChild(linha);
      }
      const topo = Math.max(y, ultimo + (BOTAO_ALTURA_PX + 2) / z);
      ultimo = topo;
      const b = document.createElement("button");
      b.type = "button";
      b.className = `${CLASSE_MARCA} ${CLASSE_EXCLUIDO}`;
      b.textContent = `ITEM EXCLUÍDO ${e.rotulo}`;
      b.title = `Item ${e.rotulo} excluído: clique para ver`;
      b.style.left = `calc(${x}px - ${BOTAO_FOLGA_PX}px / var(--pmvni-zoom, 1))`;
      b.style.top = `calc(${topo}px - ${BOTAO_ALTURA_PX / 2}px / var(--pmvni-zoom, 1))`;
      b.addEventListener("click", (ev) => {
        ev.stopPropagation();
        aoClicarExcluido(e.id, ev.clientX, ev.clientY);
      });
      c.appendChild(b);
      desenhadas++;
    }
    return desenhadas;
  }

  // src/edicao.ts
  var CAMPOS = ["titulo", "verificacao", "providencia", "base_legal", "referencia"];
  var ID_CSS3 = "pmvni-edicao-css";
  var ATR_CAMPO2 = "data-campo";
  var ATR_SELECIONADO = "data-pmvni-selecionado";
  var ATR_EDITANDO = "data-pmvni-editando";
  var SELETOR_PEDACOS = "section.folha .area .card[data-ref]";
  var SELETOR_FIGURAS = "section.folha .area .fig[data-ref]";
  var SEPARADOR_TITULO = " — ";
  var ATALHOS = /* @__PURE__ */ new Set([
    "Ctrl+O",
    "Ctrl+I",
    "Ctrl+S",
    "Ctrl+Shift+S",
    "Ctrl+E",
    "Ctrl+Z",
    "Ctrl+Y",
    "Ctrl+F",
    "Alt+ArrowUp",
    "Alt+ArrowDown",
    "Delete",
    "F2",
    "F6",
    "Ctrl+0",
    "Ctrl+Plus",
    "Ctrl+Minus",
    // Trilho de módulos (Ctrl+1 … Ctrl+5): valem também com o foco nas folhas.
    "Ctrl+1",
    "Ctrl+2",
    "Ctrl+3",
    "Ctrl+4",
    "Ctrl+5"
  ]);
  var CSS_EDICAO = `
@media screen {
  [${ATR_SELECIONADO}] { outline: 2px solid #1a73e8; outline-offset: 1px; }
  section.folha [${ATR_CAMPO2}]:hover { background-color: rgba(26, 115, 232, .10); }
  [${ATR_EDITANDO}] { outline: 2px solid #1a73e8; outline-offset: 1px; background-color: #fff8d6 !important; cursor: text; }
}
${CSS_MARCAS}`;
  var ativo = false;
  var contadorEventos = 0;
  var edicao = null;
  var ehCampo = (v) => typeof v === "string" && CAMPOS.includes(v);
  var campoDoBloco = (ref, v) => ehLinha(ref) ? ehCaminho(v) : ehCampo(v);
  var pedacosDe2 = (ref) => pedacosDoBloco(ref);
  function garantirCss() {
    if (document.getElementById(ID_CSS3)) return;
    const css = document.createElement("style");
    css.id = ID_CSS3;
    css.textContent = CSS_EDICAO;
    document.head.appendChild(css);
  }
  function campoTitulo(card) {
    const ct = card.querySelector(":scope > .ch > .ct");
    if (!ct) return null;
    const marcado = ct.querySelector(`:scope > span[${ATR_CAMPO2}="titulo"]`);
    if (marcado) return marcado;
    const primeiro = ct.querySelector(":scope > span.id")?.nextSibling;
    if (!primeiro || primeiro.nodeType !== Node.TEXT_NODE) return null;
    const texto2 = primeiro;
    if (!texto2.data.startsWith(SEPARADOR_TITULO)) return null;
    const resto = texto2.data.slice(SEPARADOR_TITULO.length);
    const seguintes = [];
    for (let n = texto2.nextSibling; n; n = n.nextSibling) seguintes.push(n);
    if (resto === "" && seguintes.length === 0) return null;
    const span = document.createElement("span");
    texto2.data = SEPARADOR_TITULO;
    if (resto !== "") span.appendChild(document.createTextNode(resto));
    span.append(...seguintes);
    ct.appendChild(span);
    return span;
  }
  function camposDoPedaco(card) {
    const q = (seletor) => card.querySelector(`:scope > ${seletor}`);
    return [
      ["titulo", campoTitulo(card)],
      ["verificacao", q(`p:not([${ATR_OCULTO}])`)],
      ["providencia", q(`.prov > p:not([${ATR_OCULTO}])`)],
      ["base_legal", q(`.two > div:nth-child(1) p.m:not([${ATR_OCULTO}])`)],
      ["referencia", q(`.two > div:nth-child(2) p.m:not([${ATR_OCULTO}])`)]
    ];
  }
  function marcarCampos() {
    for (const card of Array.from(document.querySelectorAll(SELETOR_PEDACOS))) {
      for (const [campo, el] of camposDoPedaco(card)) el?.setAttribute(ATR_CAMPO2, campo);
    }
    marcarCamposLinhas();
    return document.querySelectorAll(`section.folha [${ATR_CAMPO2}]`).length;
  }
  function desmarcarCampos() {
    for (const el of Array.from(document.querySelectorAll(`section.folha [${ATR_CAMPO2}]`))) {
      const pai = el.parentElement;
      if (el.localName === "span" && el.getAttribute(ATR_CAMPO2) === "titulo" && pai?.classList.contains("ct")) {
        el.replaceWith(...Array.from(el.childNodes));
        pai.normalize();
      } else if (!desenvolver(el)) el.removeAttribute(ATR_CAMPO2);
    }
  }
  function aplicarSelecao(ref) {
    for (const e of Array.from(document.querySelectorAll(`[${ATR_SELECIONADO}]`))) e.removeAttribute(ATR_SELECIONADO);
    if (ref !== null) for (const p of pedacosDe2(ref)) p.setAttribute(ATR_SELECIONADO, "");
  }
  function localizar2(alvo) {
    if (!(alvo instanceof Element)) return { ref: null, campo: null };
    const bloco = blocoDe(alvo);
    if (!bloco) return { ref: alvo.closest(SELETOR_FIGURAS)?.getAttribute("data-ref") ?? null, campo: null };
    const el = alvo.closest(`[${ATR_CAMPO2}]`);
    const campo = el && bloco.el.contains(el) ? el.getAttribute(ATR_CAMPO2) : null;
    return { ref: bloco.ref, campo: campoDoBloco(bloco.ref, campo) ? campo : null };
  }
  function lerTexto(el) {
    let s = "";
    const visitar = (n) => {
      if (n.nodeType === Node.TEXT_NODE) {
        s += n.data;
        return;
      }
      if (n.nodeType !== Node.ELEMENT_NODE) return;
      const e = n;
      if (e.localName === "br") {
        s += "\n";
        return;
      }
      if (/^(div|p|li)$/.test(e.localName) && s !== "" && !s.endsWith("\n")) s += "\n";
      e.childNodes.forEach(visitar);
    };
    el.childNodes.forEach(visitar);
    return s.replace(/[\r\n\u0085\u2028\u2029]+/g, " ").replace(/\u00a0/g, " ");
  }
  var umaLinha = (s) => s.replace(/[\r\n\u0085\u2028\u2029]+/g, " ");
  function inserirTexto(texto2) {
    if (document.execCommand("insertText", false, texto2)) return;
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return;
    const r = sel.getRangeAt(0);
    r.deleteContents();
    const no = document.createTextNode(texto2);
    r.insertNode(no);
    r.setStartAfter(no);
    r.collapse(true);
    sel.removeAllRanges();
    sel.addRange(r);
  }
  function restaurarEdicao() {
    const e = edicao;
    if (!e) return;
    edicao = null;
    e.primeiro.removeAttribute("contenteditable");
    e.primeiro.removeAttribute(ATR_EDITANDO);
    e.primeiro.blur();
    e.primeiro.replaceChildren(...e.originais);
    e.demais.forEach((d, i) => {
      d.style.visibility = e.visibilidade[i] ?? "";
    });
  }
  function iniciarEdicao(ref, campo, texto2) {
    const pedacos = pedacosDe2(ref).flatMap((p) => {
      const el = p.querySelector(`[${ATR_CAMPO2}="${CSS.escape(campo)}"]`);
      return el ? [el] : [];
    });
    const [primeiro, ...demais] = pedacos;
    if (!primeiro) throw new ErroComposicao("campo_inexistente", `O item "${ref}" não tem o campo "${campo}" marcado nas folhas.`);
    restaurarEdicao();
    edicao = {
      ref,
      campo,
      primeiro,
      demais,
      originais: Array.from(primeiro.childNodes),
      visibilidade: demais.map((d) => d.style.visibility),
      pendente: false
    };
    primeiro.replaceChildren(document.createTextNode(texto2));
    demais.forEach((d) => {
      d.style.visibility = "hidden";
    });
    primeiro.setAttribute("contenteditable", "plaintext-only");
    primeiro.setAttribute(ATR_EDITANDO, "");
    const r = primeiro.getBoundingClientRect();
    if (r.bottom < 0 || r.top > window.innerHeight) primeiro.scrollIntoView({ block: "center", behavior: "instant" });
    primeiro.focus({ preventScroll: true });
    const sel = window.getSelection();
    const fim = document.createRange();
    fim.selectNodeContents(primeiro);
    fim.collapse(false);
    sel?.removeAllRanges();
    sel?.addRange(fim);
  }
  var editando = () => edicao && !edicao.pendente ? edicao : null;
  function concluirEdicao() {
    const e = edicao;
    const texto2 = lerTexto(e.primeiro);
    e.primeiro.removeAttribute("contenteditable");
    e.primeiro.removeAttribute(ATR_EDITANDO);
    e.primeiro.replaceChildren(document.createTextNode(texto2));
    e.primeiro.blur();
    e.pendente = true;
    return { ref: e.ref, campo: e.campo, texto: texto2 };
  }
  function rolarAteLinha(ref) {
    const alvo = pedacosDe2(ref)[0];
    if (!alvo) return;
    const r = alvo.getBoundingClientRect();
    if (r.top >= 0 && r.bottom <= window.innerHeight) return;
    window.scrollTo({ top: Math.max(0, window.scrollY + r.top - 48), behavior: "instant" });
  }
  function medirAncora() {
    const blocos = Array.from(document.querySelectorAll("section.folha .area [data-ref]")).filter((e) => e.getClientRects().length > 0);
    let atravessa = null;
    let abaixo = null;
    for (const e of blocos) {
      const r = e.getBoundingClientRect();
      if (r.top <= 0 && r.bottom > 0) {
        if (!atravessa || r.top > atravessa.topo) atravessa = { e, topo: r.top };
      } else if (r.top > 0 && (!abaixo || r.top < abaixo.topo)) abaixo = { e, topo: r.top };
    }
    const escolhido = atravessa ?? abaixo;
    if (!escolhido) return { ref: null, deslocamentoPx: 0 };
    const ref = escolhido.e.getAttribute("data-ref");
    const primeiro = blocos.find((e) => e.getAttribute("data-ref") === ref) ?? escolhido.e;
    return { ref, deslocamentoPx: -primeiro.getBoundingClientRect().top / zoomAtual() };
  }
  function restaurarAncora(a) {
    const primeiro = Array.from(document.querySelectorAll("section.folha .area [data-ref]")).find((e) => e.getAttribute("data-ref") === a.ref && e.getClientRects().length > 0);
    if (!primeiro) return;
    const topo = primeiro.getBoundingClientRect().top;
    window.scrollTo({ top: Math.max(0, window.scrollY + topo + a.deslocamentoPx * zoomAtual()), behavior: "instant" });
  }
  var ehObjeto = (x) => typeof x === "object" && x !== null && !Array.isArray(x);
  var invalido = (m) => new ErroComposicao("estado_invalido", m);
  function lerEstado(d) {
    if (d.selecao !== null && typeof d.selecao !== "string") throw invalido("selecao deve ser texto ou nulo.");
    if (typeof d.campos !== "boolean") throw invalido("campos deve ser verdadeiro ou falso.");
    const m = d.marcas;
    if (!ehObjeto(m) || typeof m.visiveis !== "boolean") throw invalido("marcas.visiveis deve ser verdadeiro ou falso.");
    if (!Array.isArray(m.alterados) || m.alterados.some((r) => typeof r !== "string")) throw invalido("marcas.alterados deve ser uma lista de refs.");
    if (!Array.isArray(m.excluidos)) throw invalido("marcas.excluidos deve ser uma lista.");
    const excluidos = m.excluidos.map((x, i) => {
      if (!ehObjeto(x) || typeof x.id !== "string" || typeof x.rotulo !== "string" || typeof x.ancora !== "string" || x.lado !== "antes" && x.lado !== "depois") throw invalido(`marcas.excluidos[${i}] invalido.`);
      return { id: x.id, rotulo: x.rotulo, ancora: x.ancora, lado: x.lado };
    });
    let ancora = null;
    if (d.ancora !== void 0 && d.ancora !== null) {
      const a = d.ancora;
      if (!ehObjeto(a) || typeof a.ref !== "string" || typeof a.deslocamentoPx !== "number" || !Number.isFinite(a.deslocamentoPx)) {
        throw invalido("ancora deve ter ref (texto) e deslocamentoPx (número).");
      }
      ancora = { ref: a.ref, deslocamentoPx: a.deslocamentoPx };
    }
    return { selecao: d.selecao, campos: d.campos, marcas: { visiveis: m.visiveis, alterados: m.alterados, excluidos }, ancora };
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
  function registrarEdicao(registrar2, enviar2) {
    const evento = (tipo, dados) => enviar2(tipo, `evento-${++contadorEventos}`, dados);
    const ligar = () => {
      ativo = true;
      garantirCss();
    };
    registrar2("estadoEditor", (dados) => {
      const e = lerEstado(dados);
      ligar();
      if (edicao) {
        if (!document.contains(edicao.primeiro)) edicao = null;
        else if (edicao.pendente || !e.campos) restaurarEdicao();
      }
      marcarLinhas();
      let camposMarcados = 0;
      if (e.campos) camposMarcados = marcarCampos();
      else desmarcarCampos();
      aplicarSelecao(e.selecao);
      const marcasDesenhadas = desenharMarcas(e.marcas, (id, x, y) => evento("excluidoClicado", { id, x, y }));
      definirGola(e.marcas.visiveis ? Math.max(GOLA_COM_MARCAS_PX, golaDosExcluidos()) : GOLA_ESQUERDA_PX);
      if (e.ancora) restaurarAncora(e.ancora);
      return { tipo: "estadoAplicado", dados: { camposMarcados, marcasDesenhadas } };
    });
    registrar2("selecionar", (dados) => {
      const ref = dados.ref;
      if (ref !== null && typeof ref !== "string") throw new ErroComposicao("selecao_invalida", "ref deve ser texto ou nulo.");
      if (typeof dados.rolar !== "boolean") throw new ErroComposicao("selecao_invalida", "rolar deve ser verdadeiro ou falso.");
      if (ref !== null && ehLinha(ref)) marcarLinhas();
      if (ref !== null && pedacosDe2(ref).length === 0) throw new ErroComposicao("ref_inexistente", `Nenhuma folha mostra o item "${ref}".`);
      ligar();
      aplicarSelecao(ref);
      if (ref !== null && dados.rolar) {
        if (ehLinha(ref)) rolarAteLinha(ref);
        else rolarAteRef(ref);
      }
      return { tipo: "selecionado", dados: { ref, campo: null } };
    });
    registrar2("editarCampo", (dados) => {
      if (typeof dados.ref !== "string" || !campoDoBloco(dados.ref, dados.campo) || typeof dados.texto !== "string") {
        throw new ErroComposicao("edicao_invalida", "editarCampo pede ref (texto), campo (um dos cinco campos, ou o caminho do texto na linha da tabela) e texto.");
      }
      ligar();
      iniciarEdicao(dados.ref, dados.campo, dados.texto);
      return { tipo: "edicaoIniciada", dados: { ref: dados.ref, campo: dados.campo } };
    });
    registrar2("concluirEdicao", () => {
      if (!editando()) return { tipo: "semEdicao", dados: {} };
      return { tipo: "editar", dados: { ...concluirEdicao() } };
    });
    registrar2("obterAncora", () => ({ tipo: "ancora", dados: { ...medirAncora() } }));
    document.addEventListener("mousedown", (e) => {
      const ed = editando();
      if (ed && !(e.target instanceof Node && ed.primeiro.contains(e.target))) e.preventDefault();
    }, true);
    document.addEventListener("click", (e) => {
      if (!ativo || edicao || e.button !== 0) return;
      if (e.target instanceof Element && e.target.closest(`#${ID_CAMADA}`)) return;
      const { ref, campo } = localizar2(e.target);
      aplicarSelecao(ref);
      const painel = e.target instanceof Element ? e.target.closest(".panel") : null;
      const volumetria = painel !== null && e.target instanceof Element && e.target.closest("svg.dg") !== null && (painel.querySelector(".chip")?.textContent ?? "").toUpperCase().includes("VOLUMETRIA");
      evento("selecionado", volumetria ? { ref, campo, volumetria: true, x: e.clientX, y: e.clientY } : { ref, campo });
    });
    document.addEventListener("dblclick", (e) => {
      if (!ativo || edicao) return;
      const { ref, campo } = localizar2(e.target);
      if (ref !== null && campo === null && e.target instanceof Element && e.target.closest(SELETOR_FIGURAS)) {
        evento("pedirEdicao", { ref, campo: null });
        return;
      }
      if (ref === null || campo === null) return;
      window.getSelection()?.removeAllRanges();
      evento("pedirEdicao", { ref, campo });
    });
    document.addEventListener("contextmenu", (e) => {
      if (!ativo) return;
      const ed = editando();
      if (ed && e.target instanceof Node && ed.primeiro.contains(e.target)) return;
      e.preventDefault();
      if (edicao) return;
      const { ref, campo } = localizar2(e.target);
      aplicarSelecao(ref);
      evento("menuContexto", { ref, campo, x: e.clientX, y: e.clientY });
    });
    window.addEventListener("keydown", (e) => {
      if (!ativo) return;
      const ed = editando();
      if (ed) {
        if (e.isComposing || e.keyCode === 229) return;
        if (e.key === "Enter") {
          e.preventDefault();
          e.stopPropagation();
          evento("editar", { ...concluirEdicao() });
        } else if (e.key === "Escape") {
          e.preventDefault();
          e.stopPropagation();
          const { ref, campo } = ed;
          restaurarEdicao();
          evento("edicaoCancelada", { ref, campo });
        }
        return;
      }
      if (edicao || e.isComposing) return;
      const nome = nomeDoAtalho(e);
      if (nome !== null && ATALHOS.has(nome)) {
        e.preventDefault();
        evento("atalho", { tecla: nome });
      }
    }, true);
    document.addEventListener("beforeinput", (e) => {
      const ed = editando();
      if (!ed || !(e.target instanceof Node) || !ed.primeiro.contains(e.target)) return;
      if (e.inputType === "insertParagraph" || e.inputType === "insertLineBreak") e.preventDefault();
      else if (e.inputType === "insertText" && e.data && /[\r\n\u0085\u2028\u2029]/.test(e.data)) {
        e.preventDefault();
        inserirTexto(umaLinha(e.data));
      }
    }, true);
    document.addEventListener("paste", (e) => {
      const ed = editando();
      if (!ed || !(e.target instanceof Node) || !ed.primeiro.contains(e.target)) return;
      e.preventDefault();
      inserirTexto(umaLinha(e.clipboardData?.getData("text/plain") ?? ""));
    }, true);
  }

  // src/pesquisa.ts
  var ID_CSS4 = "pmvni-pesquisa-css";
  var NOME_BUSCA = "pmvni-busca";
  var NOME_ATUAL = "pmvni-busca-atual";
  var CSS_PESQUISA = `
@media screen {
  ::highlight(${NOME_BUSCA}) { background-color: #ffe27a; color: #000; }
  ::highlight(${NOME_ATUAL}) { background-color: #ff9a3c; color: #000; }
}
`;
  var termoAtual = "";
  var indice = 0;
  var ocorrencias = [];
  function base(c) {
    const minusculo = c.normalize("NFD").toLowerCase();
    return minusculo.length > 0 ? minusculo.charAt(0) : c;
  }
  function normalizarBusca(s) {
    let r = "";
    for (let i = 0; i < s.length; i++) r += base(s.charAt(i));
    return r;
  }
  function garantirCss2() {
    if (document.getElementById(ID_CSS4)) return;
    const css = document.createElement("style");
    css.id = ID_CSS4;
    css.textContent = CSS_PESQUISA;
    document.head.appendChild(css);
  }
  var api = () => typeof CSS !== "undefined" ? CSS.highlights : void 0;
  function limparRealces() {
    const h = api();
    h?.delete(NOME_BUSCA);
    h?.delete(NOME_ATUAL);
    ocorrencias = [];
    indice = 0;
  }
  function nosDeTexto2() {
    const nos = [];
    for (const area of Array.from(document.querySelectorAll("section.folha > .area"))) {
      const passeio = document.createTreeWalker(area, NodeFilter.SHOW_TEXT);
      for (let n = passeio.nextNode(); n; n = passeio.nextNode()) {
        const no = n;
        const pai = no.parentElement;
        if (!pai || no.data === "") continue;
        if (pai.closest(`[${ATR_OCULTO}], style, script`)) continue;
        nos.push(no);
      }
    }
    return nos;
  }
  function buscar(termo) {
    const alvo = normalizarBusca(termo);
    const achadas = [];
    if (alvo === "") return achadas;
    for (const no of nosDeTexto2()) {
      const texto2 = normalizarBusca(no.data);
      for (let i = texto2.indexOf(alvo); i >= 0; i = texto2.indexOf(alvo, i + alvo.length)) {
        const r = document.createRange();
        r.setStart(no, i);
        r.setEnd(no, i + alvo.length);
        achadas.push(r);
      }
    }
    return achadas;
  }
  function pintar() {
    const h = api();
    if (!h) throw new ErroComposicao("pesquisa_indisponivel", "O navegador não tem a CSS Custom Highlight API.");
    const todas = new Highlight(...ocorrencias);
    todas.priority = 0;
    h.set(NOME_BUSCA, todas);
    const marcada = ocorrencias[indice];
    const atual = new Highlight(...marcada ? [marcada] : []);
    atual.priority = 1;
    h.set(NOME_ATUAL, atual);
  }
  function rolarAteAtual() {
    const r = ocorrencias[indice];
    if (!r) return;
    const caixa2 = r.getBoundingClientRect();
    const alturaJanela = document.documentElement.clientHeight;
    window.scrollTo({ top: Math.max(0, window.scrollY + caixa2.top - alturaJanela / 2 + caixa2.height / 2), behavior: "instant" });
  }
  function refDaAtual() {
    const no = ocorrencias[indice]?.startContainer;
    const el = no instanceof Element ? no : no?.parentElement;
    return el?.closest("[data-ref]")?.getAttribute("data-ref") ?? null;
  }
  function resposta() {
    return { total: ocorrencias.length, atual: ocorrencias.length === 0 ? 0 : indice + 1, ref: refDaAtual() };
  }
  function registrarPesquisa(registrar2) {
    registrar2("pesquisar", (dados) => {
      const termo = dados.termo;
      const passo = dados.passo;
      if (typeof termo !== "string") throw new ErroComposicao("pesquisa_invalida", '"termo" deve ser um texto.');
      const manter = dados.atual;
      if (passo !== -1 && passo !== 0 && passo !== 1) throw new ErroComposicao("pesquisa_invalida", '"passo" deve ser -1, 0 ou 1.');
      if (manter !== void 0 && (typeof manter !== "number" || !Number.isInteger(manter) || manter < 1 || passo !== 0)) {
        throw new ErroComposicao("pesquisa_invalida", '"atual" deve ser um inteiro a partir de 1 e só vale com passo 0.');
      }
      if (termo === "") {
        termoAtual = "";
        limparRealces();
        return { tipo: "pesquisado", dados: resposta() };
      }
      garantirCss2();
      const mudou = termo !== termoAtual;
      const anterior = indice;
      termoAtual = termo;
      ocorrencias = buscar(termo);
      const n = ocorrencias.length;
      if (n === 0) indice = 0;
      else if (manter !== void 0) indice = Math.min(manter - 1, n - 1);
      else if (passo === 0) indice = mudou ? 0 : Math.min(anterior, n - 1);
      else indice = mudou ? passo === 1 ? 0 : n - 1 : ((anterior + passo) % n + n) % n;
      pintar();
      if (manter === void 0 && (passo !== 0 || mudou)) rolarAteAtual();
      return { tipo: "pesquisado", dados: resposta() };
    });
  }

  // src/mensagens.ts
  var VERSAO = 1;
  var TIPOS_HOST = [
    "compor",
    "aplicarPlano",
    "textoPorFolha",
    "rolarPara",
    "definirZoom",
    // Tarefa 7 (Etapa 2): estado do editor, selecao, edicao no A4 e ancora de rolagem.
    "estadoEditor",
    "selecionar",
    "editarCampo",
    "concluirEdicao",
    "obterAncora",
    // Tarefa 11: pesquisa no relatorio (realce por CSS Custom Highlight API; responde `pesquisado`).
    "pesquisar"
  ];
  var TIPOS_WEB = [
    "pronto",
    "composto",
    "planoAplicado",
    "textoPorFolha",
    "folhaVisivel",
    "erro",
    // Tarefa 7: respostas (estadoAplicado, selecionado, edicaoIniciada, editar, semEdicao, ancora) e eventos de interacao
    // (selecionado, pedirEdicao, editar, edicaoCancelada, atalho, menuContexto, excluidoClicado; id evento-N).
    "estadoAplicado",
    "selecionado",
    "edicaoIniciada",
    "editar",
    "semEdicao",
    "ancora",
    "pedirEdicao",
    "edicaoCancelada",
    "atalho",
    "menuContexto",
    "excluidoClicado",
    "pesquisado"
  ];
  function ehObjeto2(x) {
    return typeof x === "object" && x !== null && !Array.isArray(x);
  }
  function ler(bruto, tipos) {
    if (!ehObjeto2(bruto)) return { ok: false, motivo: "mensagem nao e um objeto", id: null };
    const id = typeof bruto.id === "string" && bruto.id !== "" ? bruto.id : null;
    const falha = (motivo) => ({ ok: false, motivo, id });
    if (!("v" in bruto)) return falha('campo "v" faltando');
    if (bruto.v !== VERSAO) return falha("versao nao suportada: " + String(bruto.v));
    if (typeof bruto.tipo !== "string") return falha('campo "tipo" faltando');
    if (!tipos.includes(bruto.tipo)) return falha("tipo desconhecido: " + bruto.tipo);
    if (id === null) return falha('campo "id" faltando');
    if (!ehObjeto2(bruto.dados)) return falha('campo "dados" faltando ou invalido');
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
      const resposta2 = await manipulador(msg.dados, msg);
      if (resposta2) enviar(resposta2.tipo, msg.id, resposta2.dados);
    } catch (e) {
      const codigo = e instanceof ErroComposicao ? e.codigo : "falha_no_manipulador";
      enviar("erro", msg.id, { codigo, mensagem: e instanceof Error ? e.message : String(e) });
    }
  }
  registrar("compor", async (dados) => ({ tipo: "composto", dados: { ...await compor(lerPedido(dados)) } }));
  registrar("aplicarPlano", async (dados) => ({ tipo: "planoAplicado", dados: { ...await aplicarPlano(lerPedidoAplicar(dados)) } }));
  registrar("textoPorFolha", () => ({ tipo: "textoPorFolha", dados: { folhas: textoPorFolha() } }));
  registrarTela(registrar, enviar);
  registrarEdicao(registrar, enviar);
  registrarPesquisa(registrar);
  if (webview) {
    webview.addEventListener("message", (e) => void aoReceber(e.data));
    enviar("pronto", "pronto", { versao: VERSAO });
  } else {
    console.error("[ponte] window.chrome.webview indisponivel");
  }
  return __toCommonJS(index_exports);
})();
