"use strict";
const host = window.chrome.webview;
const article = document.querySelector("article");
const header = document.querySelector("header");
const dialog = document.querySelector("dialog");
const ns = "http://www.w3.org/2000/svg";
let state, page, signature, filter, expanded, showAll = false;
let headerKey = "", processKey = "", board;
const names = ["Resumo","Etapas","Detalhes","Consumo","Log"];
const dash = "—";
const format = n => n == null ? dash : n.toLocaleString("pt-BR",{maximumFractionDigits:1});
const pct = n => n == null ? dash : format(n) + "%";
function el(tag,cls,value) {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (value != null) node.textContent = value;
    return node;
}
function svgElement(tag,attrs) {
    const node = document.createElementNS(ns,tag);
    for (const [k,v] of Object.entries(attrs)) node.setAttribute(k,v);
    return node;
}
function part(node,key) { return node.querySelector('[data-part="'+key+'"]'); }
function block(id,values = {}) {
    const svg = document.getElementById(id).content.firstElementChild.cloneNode(true);
    for (const node of svg.querySelectorAll("[data-field]")) {
        const value = String(values[node.dataset.field] ?? "");
        node.querySelector("tspan").textContent = value;
        const title = svgElement("title",{}); title.textContent = value; node.append(title);
    }
    return svg;
}
function add(node,gap = 0,target = board) {
    if (gap) node.style.marginTop = gap+"px";
    target.append(node);
    return node;
}
function action(node,label,act) {
    node.setAttribute("role","button"); node.setAttribute("tabindex","0");
    node.setAttribute("aria-label",label); node.classList.add("action");
    node.addEventListener("click",act);
    node.addEventListener("keydown",e => {
        if (e.key === "Enter" || e.key === " ") {e.preventDefault(); act();}
    });
}
function openText(title,text) {
    dialog.querySelector("h2").textContent=title;
    dialog.querySelector("p").textContent=text;
    dialog.showModal();
}
document.getElementById("close").onclick=()=>dialog.close();
function button(label,act) {
    const b=el("button","link",label); b.onclick=act; return b;
}
function card() { return add(el("div","card")); }
function ring(parent,value,size,color,thickness,delta) {
    if (!parent) return;
    const r=(size-thickness)/2, c=size/2;
    const group=svgElement("g",{transform:"rotate(-90 "+c+" "+c+")"});
    const circle=(stroke,v) => {
        const attrs={cx:c,cy:c,r,fill:"none",stroke,"stroke-width":thickness,pathLength:100};
        if (v!=null && v<100) attrs["stroke-dasharray"]=Math.min(100,Math.max(0,v))+" 100";
        group.append(svgElement("circle",attrs));
    };
    circle("#e6e3de"); if(value>0) circle(color,value);
    if(delta!=null && value>0) circle("#65748e",Math.max(0,value-Math.min(delta,value)));
    parent.prepend(group);
}
function unitRing(svg,key,value,color) {
    const p=part(svg,key);
    p.replaceChildren();
    // Draw at device-independent pixel scale before fitting the unit SVG group.
    const normalized = svgElement("g",{transform:"scale(.01)"});
    p.append(normalized);
    ring(normalized,value,100,color,8);
}
function nav(name) {
    if (!names.includes(name)) return;
    host.postMessage(name);
}
document.querySelector("nav").onclick=e=>nav(e.target.closest("button")?.dataset.name);
function nativeTop() {
    const mobile=window.innerWidth<880;
    const top=mobile ? header.getBoundingClientRect().bottom-41 : header.getBoundingClientRect().top+window.innerWidth*.021;
    host.postMessage("topo:"+top);
}
function size() {
    if (board) board.style.zoom=article.clientWidth / Number(board.dataset.width);
    nativeTop();
}
new ResizeObserver(size).observe(article);
addEventListener("resize",size); addEventListener("scroll",nativeTop,{passive:true});
function head() {
    const wide=["Resumo","Consumo"].includes(page);
    const key=JSON.stringify([wide,state.process,state.applicant]);
    if(key===headerKey) return;
    headerKey=key;
    header.replaceChildren(block(wide?"header-wide":"header-normal",{process:state.process,applicant:state.applicant}));
    // O nome do processo pode ocupar mais espaço que a amostra da prancheta.
    const field=header.querySelector('[data-field="process"]');
    const applicant=header.querySelector('[data-field="applicant"]');
    if(field && applicant) {
        const shift=Math.max(0,field.getBBox().width-85);
        if(shift) {
            const processGroup=field.parentNode.parentNode;
            const elements=[...processGroup.parentNode.children];
            for(const node of elements.slice(elements.indexOf(processGroup)+1))
                node.setAttribute("transform","translate("+shift+" 0) "+(node.getAttribute("transform")||""));
        }
    }
}
function stageTrail(stages, live) {
    const list = el("ol","stage-trail");
    list.setAttribute("aria-label","Progresso das etapas");
    stages.forEach((stage,i) => {
        const item = el("li");
        item.dataset.state = stage.state;
        if (stage.state === "Agora") item.setAttribute("aria-current","step");
        if (live && stages[i + 1]?.state === "Agora") item.classList.add("approaching");
        const marker = el("span","trail-marker",stage.state === "Feita" ? "✓" : "");
        marker.setAttribute("aria-hidden","true");
        item.append(marker,el("span","trail-label",String(i + 1).padStart(2,"0")+" "+stage.name));
        item.setAttribute("aria-label",stage.name+" · "+stage.status);
        list.append(item);
    });
    return list;
}
function summary() {
    const d=state;
    const values={...d,estimated:d.estimated?"Estimado":""};
    const overview=add(el("section","overview"));
    const status=block("summary",values);
    overview.append(status);
    status.querySelector('[data-field="eta"] title').textContent=d.etaHelp||d.eta;
    part(status,"live").style.display=d.live?"":"none";
    unitRing(status,"ring",d.value,"#176b43");
    overview.append(stageTrail(d.stages,d.live));
    add(block("discoveries"),28);
    const counts=add(block("counts",Object.fromEntries(["ok","fail","unknown","conflicts"].map((k,i)=>[k,d.totals[i]]))),16);
    for(const field of counts.querySelectorAll("[data-field]")){
        const caption=field.nextElementSibling;
        if(caption)caption.setAttribute("transform","translate("+(9+String(field.querySelector("tspan").textContent).length*17)+" 7.41)");
    }
    const rows=(d.findings||[]).filter(r=>!filter||r.classification===filter);
    const heading=add(block("evidence-heading",{count:rows.length+" registros"}),28);
    const menu=el("div","filters"); menu.hidden=true;
    action([...heading.querySelectorAll("text")].at(-1).closest("g").parentNode,"Filtrar descobertas",()=>{menu.hidden=!menu.hidden;});
    for(const value of [null,...new Set((d.findings||[]).map(r=>r.classification))])
        menu.append(button(value??"Todas as classificações",()=>{filter=value;showAll=false;render(true);}));
    add(menu);const list=card();
    for(const row of rows.slice(0,showAll?Infinity:4)) {
        const classification=({g:"Atende",r:"Não atende",a:"Falta comprovar"})[row.color] || "Informações divergentes";
        const item=block("finding",{...row,classification,title:row.id+" · "+row.title,verification:row.verified?"Verificada":"Preliminar"});
        item.querySelector('[data-field="classification"]').setAttribute("fill",({g:"#176b43",r:"#a4262c",a:"#995900"})[row.color]||"#365990");
        const chip=item.querySelector('[data-field="verification"]').parentElement.parentElement;
        chip.setAttribute("transform",chip.getAttribute("transform").replace(/,102\.02,/,","+Math.max(56,classification.length*7.7+10)+","));
        action(item,row.title,()=>openText(row.id+" · "+row.title,row.classification+" · "+(row.verified?"Verificada":"Preliminar")));
        list.append(item);
    }
    if(!rows.length)list.append(el("p","empty",filter?"Nenhuma descoberta nesta classificação.":"Aguardando primeiras descobertas."));
    if(rows.length>4)add(button(showAll?"Recolher para a prévia":"Prévia de 4 dos "+rows.length+" registros identificados. Ver todos",()=>{showAll=!showAll;render(true);}),12);
    evidenceButton();
}
function evidenceButton() {if(state.evidence)add(button("Ver evidências por PDF e página",()=>host.postMessage("evidencias")),12);}
function stages() {
    const d=state;
    add(block("stages-heading",{status:d.live?"Em andamento":d.status}));
    add(block("stages-count",{count:d.count,progress:"Progresso total: "+d.percent}),20);
    const list=card();list.style.marginTop="24px";
    const current=expanded===undefined?d.stages.findIndex(s=>s.state==="Agora"):expanded;
    d.stages.forEach((s,i)=>{
        const isOpen=current===i;
        const values={...s,percent:s.percent == null ? dash : Math.round(s.percent)+"%",result:s.result||s.status};
        (s.fronts||[]).forEach((f,j)=>{values["front"+j]="Seções "+f.inicio+"–"+f.fim;values["count"+j]=f.prontas+" de "+f.total;});
        const nameIndex={"Preparo":0,"Análise":1,"Auditoria técnica":2,"Auditoria cruzada":3,"Produção do relatório":4,"Revisão final":5}[s.name];
        const styleIndex=s.state==="Feita"?0:Math.max(2,nameIndex??2);
        const row=block(isOpen?"stage-expanded":"stage-"+styleIndex,values);
        row.dataset.state=s.state;
        if(isOpen) {
            unitRing(row,"ring",s.percent,"#176b43");
            const fronts=part(row,"fronts");
            fronts.style.display=s.fronts?.length?"":"none";
            const check=fronts.querySelector("path").cloneNode(true);
            for(let k=0;k<3;k++){
                const count=fronts.querySelector('[data-field="count'+k+'"]');
                const f=s.fronts[k],group=count.parentElement;
                group.querySelector("path")?.remove();
                if(f?.concluida)group.insertBefore(check.cloneNode(true),count);
                count.setAttribute("transform","translate("+(f?.concluida?22:0)+" 25)");
                group.style.display=f?"":"none";
            }
        }
        row.setAttribute("aria-expanded",isOpen);
        action(row,s.name+" · "+s.status,()=>{expanded=isOpen?-1:i;render(true);
            if(!isOpen)article.querySelector('[aria-expanded="true"]')?.classList.add("unfolding");});
        list.append(row);
    });
    const log=block("see-log");log.style.width="148px";
    action(log,"Ver registros",()=>nav("Log"));add(log,24);
}
function detailList(container, label, items) {
    const lines = items.flatMap(item => String(item ?? "").split(/\r?\n/))
        .map(line => line.trim()).filter(Boolean);
    if (!lines.length) return;
    container.append(el("strong", "detail-label", label));
    const list = el("ul", "detail-items");
    for (const line of lines) list.append(el("li", "", line));
    container.append(list);
}
function details() {
    const d = state;
    add(block("details-heading", {count:d.stages.length + " etapas mapeadas"}));
    const table = el("table", "detail-table");
    table.setAttribute("aria-label", "Plano e execução por etapa");
    const head = table.createTHead().insertRow();
    for (const name of ["Etapa", "Plano e execução", "Situação"]) {
        const cell = el("th", "", name);
        cell.scope = "col";
        head.append(cell);
    }
    const body = table.createTBody();
    for (const [i, stage] of d.stages.entries()) {
        const row = body.insertRow();
        const name = el("th", "", String(i + 1).padStart(2, "0") + "  " + stage.name);
        name.scope = "row";
        row.append(name);
        const work = row.insertCell();
        work.append(el("strong", "detail-scope", stage.scope || stage.description));
        if (stage.plannedWork?.startsWith("Saídas previstas: ")) {
            detailList(work, "Entregas previstas no preparo", stage.plannedWork.slice("Saídas previstas: ".length).split(", "));
        } else if (stage.plannedWork) {
            detailList(work, "Plano registrado no preparo", [stage.plannedWork]);
        }
        detailList(work, "Passos registrados pela IA", stage.plans);
        detailList(work, "Frentes de análise", stage.fronts.map(f =>
            "Seções " + f.inicio + "–" + f.fim + " · " + f.prontas + " de " + f.total +
            (f.atividade ? " · " + f.atividade : "")));
        const records = (stage.record || "").split(/\r?\n/).map(s => s === "nova" ? "Sessão iniciada" : s);
        detailList(work, "Registros da execução", records);
        detailList(work, "Objeções", stage.objections);
        const facts = [stage.executor, ...stage.measures].filter(Boolean);
        if (facts.length) {
            const footer = el("ul", "detail-facts");
            for (const fact of facts) footer.append(el("li", "", fact));
            work.append(footer);
        }
        const status = el("span", "detail-status", stage.status);
        status.dataset.state = stage.state;
        row.insertCell().append(status);
    }
    const wrap = card();
    wrap.classList.add("detail-wrap");
    wrap.append(table);
    for (const [title, items] of [["Avisos e consertos registrados", d.warnings],
        ["Falha da execução", [d.error]], ["Última mensagem", [d.lastMessage]]]) {
        if (!items.some(Boolean)) continue;
        const panel = el("section", "detail");
        detailList(panel, title, items);
        add(panel, 16);
    }
}
function log() {
    const d=state;
    add(block("log-heading",{status:d.live?"Em execução":d.status}));
    add(block("log-count",{count:d.events.length+" eventos registrados",last:d.last}),20);
    const list=card();list.style.marginTop="24px";list.append(block("log-columns"));
    for(const e of d.events){
        const row=block("event-"+(e.type==="IA"?2:e.type==="CONCLUÍDO"?1:["ERRO","ATENÇÃO"].includes(e.type)?3:0),{...e,time:e.time||dash});
        action(row,e.message,()=>openText(e.time+" · "+e.type,e.message));list.append(row);
    }
    if(!d.events.length)list.append(el("p","empty","Aguardando registros de execução."));
    evidenceButton();
}
function usage() {
    const u=state.usage,rows=u.rows,ias=rows.filter(r=>!r.local);
    const short=ias.findLast(r=>r.usada5h!=null),week=ias.findLast(r=>r.usadaSemana!=null);
    const recordedDelta=(entry,key)=> {
        if(!entry)return null;
        const measured=ias.filter(r=>r.executor===entry.executor && r[key]!=null);
        return measured.length ? measured.reduce((sum,r)=>sum+r[key],0) : null;
    };
    const shortUsed=recordedDelta(short,"curta"),weeklyUsed=recordedDelta(week,"semanal");
    const points=n=>n==null?dash:format(n)+" p.p.";
    const values={...u,cache:u.cached,elapsed:state.elapsed,eta:"Previsão de término "+state.eta,inputHelp:ias.at(-1)?.executor??"Aguardando medição",
        outputHelp:u.outputPercent==null?"Saída informada pela IA":pct(u.outputPercent)+" do volume de entrada",
        cacheHelp:u.cachePercent==null?"Cache informado pela IA":pct(u.cachePercent)+" da entrada reaproveitada",
        cachePercent:pct(u.cachePercent),shortPercent:pct(short?.usada5h),weeklyPercent:pct(week?.usadaSemana),
        shortDelta:points(shortUsed)+" nesta análise",weeklyDelta:points(weeklyUsed)+" nesta análise",durationCaption:"Onde os "+state.elapsed+" foram gastos"};
    add(block("usage-heading"));add(block("metrics",values),20);
    const charts=add(block("charts",values),20);
    ring(part(charts,"cacheRing"),u.cachePercent,148,"#176b43",20);
    ring(part(charts,"shortRing"),short?.usada5h,112,shortUsed==null?"#65748e":"#b67b17",14,shortUsed);
    ring(part(charts,"weeklyRing"),week?.usadaSemana,112,weeklyUsed==null?"#65748e":"#647bb4",14,weeklyUsed);
    const times=part(charts,"times");
    const groups=Map.groupBy(rows.filter(r=>r.seconds!=null),r=>r.stage);
    const durations=[...groups].map(([name,rs])=>({name,seconds:rs.reduce((a,r)=>a+r.seconds,0),time:rs.length===1?rs[0].time:format(rs.reduce((a,r)=>a+r.seconds,0)/60)+" min"}));
    const max=Math.max(1,...durations.map(r=>r.seconds));
    durations.forEach((r,i)=>{
        const y=17+i*Math.min(46,196/durations.length);
        for(const [x,anchor,label] of [[0,"start",r.name],[450,"end",r.time]]) {
            const txt=svgElement("text",{x,y,fill:"#424242","font-size":13,"text-anchor":anchor});txt.textContent=label;times.append(txt);
        }
        for(const [width,fill] of [[450,"#e5e3df"],[450*r.seconds/max,"#176b43"]])
            times.append(svgElement("rect",{x:0,y:y+7,width,height:5,rx:2,fill}));
    });
    const executors=card();executors.style.marginTop="20px";executors.append(block("executors-heading"));
    for(const [name,group] of Map.groupBy(rows,r=>r.executor)){
        const r=group.at(-1),sum=k=>group.some(v=>v[k]!=null)?group.reduce((a,v)=>a+(v[k]||0),0):null;
        const title=[...new Set(group.map(v=>v.stage))].join(", ");
        const row=block("executor-"+(r.local?1:0),{name,caption:title,label1:r.local?"CPU":"Cota de 5 h",value1:r.local?r.cpu:points(sum("curta")),label2:r.local?"RAM":"Cota semanal",value2:r.local?r.ram:points(sum("semanal"))});
        for(const [key,value] of [["bar1",sum("curta")],["bar2",sum("semanal")]]){
            const p=part(row,key);if(!p)continue;
            p.replaceChildren(svgElement("rect",{width:1,height:1,fill:"#e5e3df"}),svgElement("rect",{width:Math.min(100,Math.max(0,value||0))/100,height:1,fill:key==="bar1"?"#b67b17":"#687ab2"}));
        }
        executors.append(row);
    }
    const table=card();table.style.marginTop="20px";table.append(block("usage-table-heading",{count:new Set(rows.map(r=>r.stage)).size+" de "+state.stages.length+" etapas com registro"}),block("usage-columns"));
    rows.forEach(r=>{
        const row=block("usage-row",{...r,short:points(r.curta),weekly:points(r.semanal)});
        if(r.local) part(row,"executor-icon").replaceChildren(svgElement("use",{href:"#monitor-cpu",transform:"scale(.8) translate(-8 -8)"}));
        action(row,r.stage+" · "+r.executor,()=>openText(r.stage+" · "+r.executor,r.details.join("\n")||"Sem medição."));
        table.append(row);
    });
    if(!rows.length)for(const box of [executors,table])box.append(el("p","empty","Aguardando medição."));
}
function render(force=false) {
    if(!state)return;
    const changed=page!==state.aba;
    page=state.aba;head();
    // Atualizar o relógio não deve recriar tabelas, interromper a seleção ou deslocar o foco.
    const key=JSON.stringify(page==="Log"?[page,state.events,state.last,state.status,state.evidence]:page==="Etapas"?
        [page,state.stages,state.status,state.count,state.percent,state.live]:page==="Detalhes"?
        [page,state.stages,state.plan,state.warnings,state.error,state.lastMessage]:state);
    if(!force && signature===key)return;
    signature=key;
    const buttons=[...document.querySelectorAll("nav button")];
    for(const b of buttons)b.setAttribute("aria-current",String(b.dataset.name===page));
    const followLog=page==="Log" && !changed && window.scrollY+window.innerHeight>=document.documentElement.scrollHeight-4;
    const active=document.activeElement,focusKey=article.contains(active)?active.getAttribute("aria-label"):null;
    board=el("div","board");
    if(changed)board.classList.add("entering");
    board.dataset.width=["Resumo","Consumo"].includes(page)?1536:1376;
    board.style.width=board.dataset.width+"px";
    ({Resumo:summary,Etapas:stages,Detalhes:details,Consumo:usage,Log:log})[page]();
    article.setAttribute("aria-label",page);article.replaceChildren(board);size();
    if(changed)window.scrollTo(0,0);
    else if(followLog)window.scrollTo(0,document.documentElement.scrollHeight);
    else if(focusKey)[...article.querySelectorAll("[aria-label]")].find(n=>n.getAttribute("aria-label")===focusKey)?.focus({preventScroll:true});
}
host.addEventListener("message",({data})=>{
    if(data?.clock && state) {
        state.elapsed=data.elapsed;state.eta=data.eta;state.etaHelp=data.etaHelp;
        for(const [name,value] of [["elapsed",data.elapsed],["eta",page==="Consumo"?"Previsão de término "+data.eta:data.eta],["durationCaption","Onde os "+data.elapsed+" foram gastos"]]) {
            const field=article.querySelector('[data-field="'+name+'"]');
            if(field){field.querySelector("tspan").textContent=value;field.querySelector("title").textContent=name==="eta"?(data.etaHelp||value):value;}
        }
        return;
    }
    if(!data || !names.includes(data.aba))return;
    if(processKey!==data.run){filter=null;expanded=undefined;showAll=false;processKey=data.run;}
    state=data;render();
});
host.postMessage("pronto");
