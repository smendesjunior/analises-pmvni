using System.Text.Json;

namespace Pmvni.App;

/// <summary>Os scripts que o app roda dentro das páginas dos sistemas (2026-11-17). Cada chamada é <c>(função)(argumento JSON)</c>; o argumento é
/// sempre serializado (nunca concatenado em aspas) e a resposta volta como objeto JSON. Nada aqui lê cookies, tokens ou faz requisições
/// próprias; só lê o que está na tela e clica/preenche como o usuário faria.</summary>
static class ScriptsSites
{
    public static string Chamar(string funcao, object? argumento) => "(" + funcao + ")(" + JsonSerializer.Serialize(argumento) + ")";

    /// <summary>Utilidades comuns: visível, texto, escrever num campo como se fosse digitado.</summary>
    const string Base = """
        const vis = e => !!e && e.getClientRects().length > 0 && getComputedStyle(e).visibility !== 'hidden' && getComputedStyle(e).display !== 'none';
        const todos = (s, r = document) => [...r.querySelectorAll(s)].filter(vis);
        const txt = e => (e && (e.innerText || e.textContent) || '').replace(/\s+/g, ' ').trim();
        const escrever = (el, v) => {
          const d = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value');
          el.focus(); d && d.set ? d.set.call(el, v) : (el.value = v);
          for (const t of ['input', 'change', 'keyup']) el.dispatchEvent(new Event(t, { bubbles: true }));
          // AngularJS (Essencial): grava também no modelo do formulário, que é o que o site envia no login.
          try { const m = window.angular && window.angular.element(el).controller('ngModel'); if (m) { m.$setViewValue(v); m.$render(); const s = window.angular.element(el).scope(); s && s.$applyAsync && s.$applyAsync(); } } catch (e) { }
        };
        const clicar = el => { el.scrollIntoView({ block: 'center' }); for (const t of ['pointerdown', 'mousedown', 'pointerup', 'mouseup']) el.dispatchEvent(new MouseEvent(t, { bubbles: true })); el.click(); };
        """;

    /// <summary>Login genérico: há um campo de senha à vista? Com usuário e senha: preenche e envia. Volta {senha, enviado}.</summary>
    public const string Login = "a => {" + Base + """
          const senhas = todos('input[type=password]');
          if (senhas.length === 0) return { senha: false, enviado: false };
          if (!a || !a.usuario) return { senha: true, enviado: false };
          const s = senhas[0];
          const campos = todos('input:not([type=hidden]):not([type=password]):not([type=checkbox]):not([type=radio]):not([type=submit]):not([type=button])');
          const antes = campos.filter(c => c.compareDocumentPosition(s) & Node.DOCUMENT_POSITION_FOLLOWING);
          const u = antes[antes.length - 1];
          if (!u) return { senha: true, enviado: false };
          escrever(u, a.usuario); escrever(s, a.senha);
          for (const c of todos('input[type=checkbox]')) { const l = txt(c.closest('label') || c.parentElement).toLowerCase(); if (/lembr|manter|remember|conectado/.test(l) && !c.checked) clicar(c); }
          const f = s.form;
          const botao = (f ? todos('button, input[type=submit]', f) : []).find(b => b.type === 'submit' || /entrar|acessar|login|logar|ok/i.test(txt(b) || b.value))
            || todos('button, input[type=submit], a').find(b => /^(entrar|acessar|login|logar)$/i.test(txt(b) || b.value || ''));
          if (botao) clicar(botao); else if (f) f.requestSubmit ? f.requestSubmit() : f.submit();
          else s.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 13, bubbles: true }));
          return { senha: true, enviado: true };
        }
        """;

    /// <summary>Mantém a sessão viva: uma leitura simples da própria página, com os cookies do site (o servidor renova a sessão).</summary>
    public const string ManterViva = "() => { fetch(location.href, { credentials: 'include', cache: 'no-store' }).catch(() => {}); return true; }";

    /// <summary>GPI: o estado da tela e as ações da consulta/BCI. a = { op, insc?, texto? }. Só age em https://gpi12.cloud.el.com.br.</summary>
    public const string Gpi = "a => {" + Base + """
          if (location.origin !== 'https://gpi12.cloud.el.com.br') return { ok: false, erro: 'origem' };
          const chave = v => { if (!/^[0-9.\s-]+$/.test(v || '')) return null; const d = v.replace(/[^0-9]/g, ''); return d.length === 14 ? d : null; };
          // A lupa da pesquisa da tela (não a busca de funcionalidades do topo, que é um typeahead .tt-input).
          const lupa = () => todos('.input-group').find(g => g.querySelector('.fa-search') && !g.querySelector('.tt-input') && [...g.querySelectorAll('input.form-control')].some(vis));
          const filtros = () => {
            const g = lupa(); if (!g) return null;
            let c = g; let selA = null, selB = null;
            for (let i = 0; i < 6 && c && !(selA && selB); i++) {
              c = c.parentElement; if (!c) break;
              const ss = todos('select', c);
              selA = ss.find(s => [...s.options].some(o => o.text.trim() === 'Inscrição Municipal'));
              selB = ss.find(s => [...s.options].some(o => o.text.trim() === 'Igual') && [...s.options].some(o => o.text.trim() === 'Contém'));
            }
            return selA && selB ? { g, selA, selB, campo: [...g.querySelectorAll('input.form-control')].find(vis), botao: g.querySelector('.fa-search').closest('button, a, span') } : null;
          };
          const fichaInsc = () => todos('input[tid="TrbContribuinteCadastro.inscricaoMunicipal"]');
          const botaoBci = () => todos('button[title="Imprimir Ficha"]').filter(b => /Boletim Cadastral/i.test(txt(b)));
          const carregando = () => todos('.dx-loadpanel-content, .dx-loadindicator, .gwt-PopupPanelGlass, .loading, .blockUI').length > 0;
          const lerAno = () => {
            const rot = todos('label, span, div, td, b, strong').find(e => e.children.length === 0 && /^Ano:?$/.test(txt(e)));
            if (!rot) return '';
            // O primeiro campo depois do rótulo "Ano:" (no cabeçalho da ficha: Nome, Inscrição Municipal, CPF/CNPJ, Ano).
            const inp = todos('input').find(i => rot.compareDocumentPosition(i) & Node.DOCUMENT_POSITION_FOLLOWING);
            return inp && /^\d{4}$/.test(inp.value.trim()) ? inp.value.trim() : '';
          };
          const grades = () => todos('.dx-datagrid').map(g => {
            const cab = [...g.querySelectorAll('.dx-header-row td')].map(txt);
            const ci = cab.findIndex(t => t === 'Inscrição Municipal'), ct = cab.findIndex(t => t === 'Tipo de Cadastro');
            return { g, ci, ct };
          }).filter(x => x.ci >= 0);
          const linhasExatas = insc => grades().flatMap(({ g, ci, ct }) => todos('tr.dx-data-row', g).filter(r => !r.hasAttribute('data-pmvni-velha')).map(r => {
            const cells = [...r.children].filter(e => e.tagName === 'TD');
            return { r, inscricao: txt(cells[ci]), tipo: ct >= 0 ? txt(cells[ct]) : '' };
          })).filter(x => chave(x.inscricao) === insc);

          switch (a.op) {
            case 'estado': {
              const f = fichaInsc();
              return { ok: true, senha: todos('input[type=password]').length > 0, logado: !!document.querySelector('input.tt-input'),
                       manutencao: !!filtros(), ficha: f.length > 0, carregando: carregando() };
            }
            case 'abrirTela': {
              // Conferido no GPI em 17/11/2026: as telas abertas são abas ("Página Principal", "Contribuintes Manutenção ×"). Se a aba da
              // manutenção já existe, vai para ela; senão, na Página Principal, a busca de funcionalidades ("Pesquisar… Ctrl+Alt+P", um
              // typeahead) recebe o nome e a sugestão exata é clicada.
              const aba = nome => [...document.querySelectorAll('a')].filter(vis).find(e => txt(e).replace(/\s*×$/, '') === nome);
              const manut = aba('Contribuintes Manutenção');
              if (manut) { clicar(manut); return { ok: true, passo: 'aba' }; }
              const sug = [...document.querySelectorAll('.tt-suggestion')].filter(vis).find(e => txt(e).endsWith('Contribuintes Manutenção'));
              if (sug) { clicar(sug); return { ok: true, passo: 'sugestao' }; }
              const busca = document.querySelector('input.tt-input');
              if (!busca) return { ok: false, erro: 'busca' };
              if (!vis(busca)) { const p = aba('Página Principal'); if (p) clicar(p); return { ok: true, passo: 'principal' }; }
              escrever(busca, 'Contribuintes Manutenção');
              return { ok: true, passo: 'digitou' };
            }
            case 'pdf': {
              const p = window.__pmvniPdf || { pronto: false };
              if (p.pronto) window.__pmvniPdf = null;
              return Object.assign({ ok: true }, p);
            }
            case 'clicarTexto': {
              const alvo = todos('a, span, div, li, td, button').filter(e => txt(e) === a.texto).sort((x, y) => x.querySelectorAll('*').length - y.querySelectorAll('*').length)[0];
              if (!alvo) return { ok: false, erro: 'sem ' + a.texto };
              clicar(alvo); return { ok: true };
            }
            case 'fecharFicha': {
              const fechar = todos('button, a').filter(b => /Fechar/.test(txt(b)));
              if (fichaInsc().length && fechar.length) { clicar(fechar[0]); return { ok: true }; }
              return { ok: false };
            }
            case 'pesquisar': {
              const f = filtros(); if (!f || !f.campo || !f.botao) return { ok: false, erro: 'filtros' };
              // Trocar um filtro faz o GPI redesenhar a barra (o campo escrito antes se perde, conferido em 17/11/2026): troca só o que
              // precisa e volta sem pesquisar; quem chama espera e pede de novo.
              let mudou = false;
              for (const [s, t] of [[f.selA, 'Inscrição Municipal'], [f.selB, a.cond || 'Igual']]) {
                const o = [...s.options].find(o => o.text.trim() === t);
                if (s.value === o.value) continue;
                s.value = o.value; o.selected = true; mudou = true;
                s.dispatchEvent(new Event('change', { bubbles: true }));
              }
              if (mudou) return { ok: true, mudou: true };
              for (const r of document.querySelectorAll('tr.dx-data-row')) r.setAttribute('data-pmvni-velha', '');
              window.__pmvniBusca = { inicio: Date.now(), viuCarga: false };
              escrever(f.campo, a.insc);
              if (f.campo.value !== a.insc) return { ok: false, erro: 'campo' };
              clicar(f.botao);
              return { ok: true };
            }
            case 'linhas': {
              const novas = grades().some(({ g }) => todos('tr.dx-data-row', g).some(r => !r.hasAttribute('data-pmvni-velha')));
              // A lista vazia de uma tela recém-aberta já mostra "sem dados", e o GPI só mostra a carga ~1,7 s depois do clique (conferido em
              // 17/11/2026): "nenhum resultado" só vale depois de ver a carga passar (ou 6 s sem carga nenhuma).
              const busca = window.__pmvniBusca || { inicio: 0, viuCarga: true };
              if (carregando()) { busca.viuCarga = true; return { ok: true, pronto: false }; }
              const semDados = todos('.dx-datagrid-nodata').length > 0 && (busca.viuCarga || Date.now() - busca.inicio > 6000);
              if (!novas && !semDados) return { ok: true, pronto: false };
              // Fechar a ficha (ou abrir a tela) faz o GPI recarregar a lista anterior ~1,8 s depois (conferido em 17/11/2026); essa recarga
              // pode chegar depois do clique na lupa. Com "Igual", toda linha do resultado tem a inscrição pedida: linhas novas de outra
              // inscrição são dessa recarga atrasada, e a espera continua (até 20 s; depois vale o que houver).
              if (novas) {
                const todasNovas = grades().flatMap(({ g, ci }) => todos('tr.dx-data-row', g).filter(r => !r.hasAttribute('data-pmvni-velha'))
                  .map(r => chave(txt([...r.children].filter(e => e.tagName === 'TD')[ci]))));
                const alheias = (a.cond || 'Igual') === 'Igual' && todasNovas.some(v => v !== a.insc);
                if (alheias && Date.now() - busca.inicio < 20000) {
                  for (const r of document.querySelectorAll('tr.dx-data-row')) r.setAttribute('data-pmvni-velha', '');
                  return { ok: true, pronto: false, alheias: true };
                }
              }
              return { ok: true, pronto: true, linhas: linhasExatas(a.insc).map(x => ({ inscricao: x.inscricao, tipo: x.tipo })) };
            }
            case 'consultar': {
              const ls = linhasExatas(a.insc);
              if (ls.length !== 1) return { ok: false, erro: 'linhas ' + ls.length };
              const bs = todos('a[title="Consultar"]', ls[0].r);
              if (bs.length !== 1) return { ok: false, erro: 'botao' };
              clicar(bs[0]); return { ok: true };
            }
            case 'ficha': {
              const f = fichaInsc();
              if (f.length !== 1) return { ok: true, pronto: false, quantas: f.length };
              const consulta = f[0].disabled || f[0].readOnly || todos('span, a, div, label').some(e => e.children.length <= 1 && txt(e) === 'Consulta');
              return { ok: true, pronto: !carregando(), inscricao: f[0].value, ano: lerAno(), consulta, bci: botaoBci().length };
            }
            case 'emitir': {
              const f = fichaInsc(), b = botaoBci();
              if (f.length !== 1 || chave(f[0].value) !== a.insc) return { ok: false, erro: 'ficha' };
              if (b.length !== 1) return { ok: false, erro: 'botao ' + b.length };
              // O GPI emite o boletim enviando um formulário (POST, alvo _blank) para .../acessoclientbase/report, de dentro de um dos
              // quadros da página (conferido em 17/11/2026). Em vez de abrir a aba nova, o mesmo envio é refeito aqui e o PDF devolvido
              // fica em window.__pmvniPdf (lido pela operação 'pdf'). Os campos são os que o próprio GPI montou; nada é inventado.
              const topo = window;
              topo.__pmvniPdf = { pronto: false };
              const janelas = [window, ...[...document.querySelectorAll('iframe')].map(f => { try { return f.contentWindow; } catch (e) { return null; } })].filter(Boolean);
              for (const w of janelas) {
                try {
                  const proto = w.HTMLFormElement.prototype;
                  if (proto.__pmvniOriginal) continue;
                  proto.__pmvniOriginal = proto.submit;
                  proto.submit = function () {
                    if (!/\/acessoclientbase\/report$/.test(this.action) || !topo.__pmvniPdf || topo.__pmvniPdf.pronto) return proto.__pmvniOriginal.call(this);
                    fetch(this.action, { method: 'POST', body: new URLSearchParams(new w.FormData(this)) }).then(async r => {
                      const tipo = r.headers.get('content-type') || '';
                      const bytes = new Uint8Array(await r.arrayBuffer());
                      let s = ''; for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
                      topo.__pmvniPdf = { pronto: true, status: r.status, tipo, base64: btoa(s) };
                    }).catch(e => { topo.__pmvniPdf = { pronto: true, status: 0, erro: String(e) }; });
                  };
                } catch (e) { }
              }
              b[0].click(); return { ok: true };   // o clique simples (o GPI ignorou a sequência completa de eventos no ensaio)
            }
          }
          return { ok: false, erro: 'op' };
        }
        """;
}
