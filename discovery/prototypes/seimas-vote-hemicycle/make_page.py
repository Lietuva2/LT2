import json,sys
bid=sys.argv[1]; title=sys.argv[2] if len(sys.argv)>2 else ""
d=json.load(open(f"vote_{bid}.json"))
page='''<!doctype html><html lang="lt"><head><meta charset="utf-8"><style>
body{margin:0;background:#f4f7f3;font-family:system-ui,sans-serif;color:#13201a}
.card{width:620px;margin:20px;background:#fff;border:1px solid #d3dcd3;border-radius:16px;padding:22px}
.id{font:12px monospace;color:#6f7c75}h3{margin:6px 0 0;font-size:18px}
svg{width:100%;height:auto;display:block;margin-top:10px}
.leg{display:flex;gap:14px;flex-wrap:wrap;font-size:13px;color:#44524a;margin-top:6px}
.leg i{display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:5px}
table{border-collapse:collapse;font-size:12.5px;margin-top:12px;width:100%}td,th{padding:3px 6px;border-bottom:1px solid #eee;text-align:right}td:first-child,th:first-child{text-align:left}
.note{font-size:12px;color:#6b4d00;background:#fff4d6;border-radius:8px;padding:8px 10px;margin-top:10px}
</style></head><body><div class="card" id="c"></div><script>
const D=__DATA__, TITLE=__TITLE__;
const col={"Už":"#2a78d6","Prieš":"#e34948","Susilaikė":"#b5b4ae"};
const NS="http://www.w3.org/2000/svg";
const seatsN=D.data.reduce((a,f)=>a+f.mps.length,0);
const cx=300,cy=300,R0=120,R1=285,rows=9;
const radii=[];for(let i=0;i<rows;i++)radii.push(R0+(R1-R0)*i/(rows-1));
const sum=radii.reduce((a,b)=>a+b,0);const counts=radii.map(r=>Math.round(seatsN*r/sum));counts[rows-1]+=seatsN-counts.reduce((a,b)=>a+b,0);
const seats=[];radii.forEach((r,i)=>{for(let j=0;j<counts[i];j++){const a=Math.PI-Math.PI*(j+.5)/counts[i];seats.push({x:cx+r*Math.cos(a),y:cy-r*Math.sin(a),a});}});
seats.sort((p,q)=>q.a-p.a);
const ord={"Už":0,"Prieš":1,"Susilaikė":2,"":3};
let k=0;const svg=document.createElementNS(NS,"svg");svg.setAttribute("viewBox","0 0 600 310");
D.data.forEach(f=>{const list=[...f.mps].sort((a,b)=>ord[a.v]-ord[b.v]);
  seats.slice(k,k+list.length).sort((p,q)=>Math.hypot(p.x-cx,p.y-cy)-Math.hypot(q.x-cx,q.y-cy)).forEach((s,i)=>{const m=list[i];
    const c=document.createElementNS(NS,"circle");c.setAttribute("cx",s.x.toFixed(1));c.setAttribute("cy",s.y.toFixed(1));
    if(m.v){c.setAttribute("r",7.4);c.setAttribute("fill",col[m.v]);}else{c.setAttribute("r",6.2);c.setAttribute("fill","none");c.setAttribute("stroke","#c2c8c2");c.setAttribute("stroke-width",2);}
    const t=document.createElementNS(NS,"title");t.textContent=m.n+" ("+(f.f||"be frakcijos")+"): "+(m.v||"nebalsavo");c.appendChild(t);svg.appendChild(c);});
  k+=list.length;});
const T=D.tot;const big=document.createElementNS(NS,"text");big.setAttribute("x",cx);big.setAttribute("y",cy-18);big.setAttribute("text-anchor","middle");big.setAttribute("font-size","34");big.setAttribute("font-weight","800");big.textContent=T["už"]+" : "+T["prieš"];svg.appendChild(big);
const sm=document.createElementNS(NS,"text");sm.setAttribute("x",cx);sm.setAttribute("y",cy+2);sm.setAttribute("text-anchor","middle");sm.setAttribute("font-size","13");sm.setAttribute("fill","#6f7c75");sm.textContent="susilaikė "+T["susilaikė"]+" · balsavo "+T["balsavo"]+" iš "+T["viso"];svg.appendChild(sm);
const c=document.getElementById("c");
c.innerHTML='<div class="id">Tikri duomenys · apps.lrs.lt · balsavimo_id '+D.bid+' · '+T.balsavimo_laikas+'</div><h3>'+TITLE+'</h3>';
c.appendChild(svg);
c.insertAdjacentHTML("beforeend",'<div class="leg"><span><i style="background:#2a78d6"></i>Už</span><span><i style="background:#e34948"></i>Prieš</span><span><i style="background:#b5b4ae"></i>Susilaikė</span><span><i style="box-shadow:inset 0 0 0 2px #c2c8c2"></i>Nebalsavo</span></div>');
let rows_='<table><tr><th>Frakcija</th><th>Už</th><th>Prieš</th><th>Susil.</th><th>Nebals.</th></tr>';
D.data.forEach(f=>{const n=v=>f.mps.filter(m=>m.v===v).length;rows_+='<tr><td>'+(f.f||"be frakcijos")+'</td><td>'+n("Už")+'</td><td>'+n("Prieš")+'</td><td>'+n("Susilaikė")+'</td><td>'+n("")+'</td></tr>';});
c.insertAdjacentHTML("beforeend",rows_+'</table>');
if(T.komentaras)c.insertAdjacentHTML("beforeend",'<div class="note">Seimo pastaba: '+T.komentaras+'</div>');
</script></body></html>'''
page=page.replace("__DATA__",json.dumps(d,ensure_ascii=False)).replace("__TITLE__",json.dumps(title,ensure_ascii=False))
open(f"hemi_{bid}.html","w").write(page); print("written")
