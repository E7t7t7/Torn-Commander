// Offline full-cycle fixture. No real browser, API, account, fights or medicine.
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
function fixture(ambiguous=false,missingMedical=false,missingJoin=false,options={}){
 let clock=1700000000000,interval,h,scene='home',hospital=false,stock=5,remaining=2,expanded=false,fightHits=0,fightJoinAt=0;
 const messages=[],replies=[],counts={pillow:0,join:0,medicine:0,wrong:0,scroll:0,expand:0,pillowTimes:[],joinTimes:[]},local=new Map(),session=new Map();
 class Node{
  constructor(tag,text='',attrs={},children=[]){this.tagName=tag.toUpperCase();this.text=text;this.attrs=attrs;this.className=attrs.class||'';this.children=children;for(const c of children)c.parentElement=this;}
  get textContent(){return [this.text,...this.children.map(c=>c.textContent)].join(' ').trim()}
  get innerText(){return this.textContent}
  getAttribute(k){return this.attrs[k]??null}
  getClientRects(){return [{}]}
  contains(n){return this===n||this.children.some(c=>c.contains(n))}
  scrollIntoView(){counts.scroll++}
  click(){this.onclick?.()}
  querySelectorAll(selector){
   const patterns=selector.split(',').map(s=>s.trim());
   const matches=(el,s)=>{
    const tag=s.match(/^[a-z][a-z0-9]*/i)?.[0];
    if(tag&&tag.toUpperCase()!==el.tagName)return false;
    for(const a of s.matchAll(/\[([\w-]+)(?:(\*=|=)["']?([^"'\]]*)["']?)?\]/g)){
     const v=el.getAttribute(a[1]);if(v===null)return false;
     if(a[2]==='='&&v!==a[3])return false;if(a[2]==='*='&&!v.includes(a[3]))return false;
    }
    return true;
   };
   const out=[];const walk=node=>{for(const child of node.children){if(patterns.some(s=>matches(child,s)))out.push(child);walk(child)}};walk(this);return out;
  }
 }
 const node=(t,text='',a={},c=[])=>new Node(t,text,a,c);
 const button=(text,fn,a={})=>{const n=node('button',text,a);n.onclick=fn;return n};
 function buildBody(){
  if(scene==='fight'){
   const pillow=button('',()=>{counts.pillow++;counts.pillowTimes.push(clock);if(++fightHits>=(options.pillowHits||1)){hospital=true;scene='loss'}}, {},);
   Object.defineProperty(pillow,'disabled',{get:()=>clock-fightJoinAt<(options.pillowDisabledMs||0)});
   pillow.children=[node('img','',{src:'/images/items/440/large.png',alt:'Equipped weapon',title:'Pillow'}),node('span','Pillow')];for(const c of pillow.children)c.parentElement=pillow;
   let weapon=pillow;
   if(options.replacedControls&&fightHits>0){
    const action=node('div','',{'role':'button','aria-label':'Attack with Pillow',id:'weapon_melee'},[node('span','Turn artwork')]);
    action.onclick=pillow.onclick;action.attrs['aria-disabled']=clock-(counts.pillowTimes.at(-1)||0)<1500?'true':'false';weapon=action;
   }
   const area=node('div','',{class:'playerArea_outer'},[node('div','',{class:'playerArea_inner'},[weapon])]);
   const defender=node('div','',{class:'defenderArea'},[button('Pillow',()=>counts.wrong++)]);
   const roots=[area,defender];if(ambiguous)roots.push(node('div','',{class:'playerArea_second'},[button('Pillow',()=>counts.wrong++)]));
   return node('body','Combat',{},roots);
  }
  if(scene==='loss')return node('body','Seller lost to Target.');
  if(scene==='attack'&&missingJoin)return node('body','Waiting for combat controls');
  if(scene==='attack')return hospital?node('body','You cannot attack while in hospital.'):node('body','',{},[button('Join fight',()=>{counts.join++;fightHits=0;fightJoinAt=clock;counts.joinTimes.push(clock);scene='fight'})]);
  const medical=node('a','Medical');medical.onclick=()=>scene='medical';
  if(scene==='items')return node('body','Your items',{},missingMedical?[]:[node('li','',{},[medical])]);
  if(scene==='medical'||scene==='confirm'){
   const name=node('span','First Aid Kit');name.onclick=()=>{counts.expand++;if(!options.expandFails)expanded=true;};
   const controls=[name];if(!options.collapsed||expanded)controls.push(button('Use',()=>scene='confirm'));
   if(options.collapsed)controls.unshift(node('span','x'+stock));
   const row=node('li','',options.collapsed?{role:'tablist',class:'first-in-row m-first-in-row t-first-in-row last-row'}:{'data-quantity':String(stock)},controls);
   const wrong=node('li','',{'data-item-id':'68'},[node('span','Small First Aid Kit'),button('Use',()=>counts.wrong++)]);
   const children=[row,wrong];if(options.ambiguousKit)children.push(node('li','',{'data-item-id':'67'},[node('span','First Aid Kit')]));
   if(scene==='confirm')children.push(node('div','Use First Aid Kit?',{role:'dialog'},[button('Okay',()=>{stock--;counts.medicine++;hospital=false;scene='medical'})]));
   return node('body','Your items - Medical',{},children);
  }
  return node('body','Torn');
 }
 let lastKey='',cached;
 function body(){const key=scene+stock+expanded+(options.replacedControls?fightHits+':'+clock:'');if(key!==lastKey){lastKey=key;cached=buildBody()}return cached;}
 const document={title:'Torn',get body(){return body()},querySelectorAll:s=>body().querySelectorAll(s)};
 const location={href:'https://www.torn.com/index.php',pathname:'/index.php',assign(url){const u=new URL(url);this.href=url;this.pathname=u.pathname;scene=u.pathname==='/item.php'?'items':'attack';expanded=false}};
 class Clock extends Date{static now(){return clock}}
 const store=m=>({getItem:k=>m.get(k)??null,setItem:(k,v)=>m.set(k,v),removeItem:k=>m.delete(k)});
 const window={chrome:{webview:{postMessage:raw=>{
  const m=JSON.parse(raw);messages.push(m);
  if(m.type==='tc-loss-claim')replies.push(()=>h.onClaim(m.requestId,true,'lease-'+remaining,'Granted'));
  if(m.type==='tc-loss-commit')replies.push(()=>h.onCommit(m.claimId,true,--remaining,'Saved'));
 }}}};
 const context={window,document,location,URL,Date:Clock,Math,console,localStorage:store(local),sessionStorage:store(session),getComputedStyle:()=>({visibility:'visible',display:'block'}),setInterval:fn=>{interval=fn},setTimeout:()=>{}};
 vm.createContext(context);vm.runInContext(source,context);h=window.TornCommanderLossCycle;
 h.start({profileId:'one',slot:'A',orderId:'fixture',targetId:'123',selfName:'Seller',maxKits:4,attackDelayMs:options.delay??1900});
 return {h,counts,messages,async step(milliseconds=1000){h.pump();while(replies.length)replies.shift()();clock+=milliseconds;await Promise.resolve()}};
}
(async()=>{
 let f=fixture();for(let i=0;i<100&&f.h.getState().stage!=='complete';i++)await f.step();
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);
 assert.equal(f.counts.pillow,2);assert.equal(f.counts.join,2);assert.equal(f.counts.medicine,2);assert.equal(f.counts.wrong,0);
 assert.equal(f.messages.filter(m=>m.type==='tc-loss-commit').length,2);assert(f.counts.scroll>0);
 f=fixture(false,false,false,{collapsed:true});for(let i=0;i<110&&f.h.getState().stage!=='complete';i++)await f.step();
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.expand,2);assert.equal(f.counts.medicine,2);assert.equal(f.counts.wrong,0);
 f=fixture(false,false,false,{collapsed:true,expandFails:true});for(let i=0;i<60&&f.h.getState().stage!=='paused';i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert(f.h.getState().status.includes('Use control did not appear'));assert.equal(f.counts.expand,1);assert.equal(f.counts.medicine,0);
 f=fixture(false,false,false,{collapsed:true,ambiguousKit:true});for(let i=0;i<100&&f.h.getState().stage!=='paused';i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert.equal(f.counts.expand,0);assert.equal(f.counts.medicine,0);
 f=fixture(false,false,false,{pillowHits:3,delay:1000,replacedControls:true,collapsed:true});
 for(let i=0;i<800&&f.h.getState().stage!=='complete';i++)await f.step(250);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.pillow,6);assert.equal(f.counts.medicine,2);assert.equal(f.counts.wrong,0);
 for(let i=1;i<f.counts.pillowTimes.length;i++)assert(f.counts.pillowTimes[i]-f.counts.pillowTimes[i-1]>=1500);
 f=fixture(false,false,false,{pillowHits:25,delay:1000});
 for(let i=0;i<500&&f.h.getState().stage!=='complete';i++)await f.step(1000);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.pillow,50);assert.equal(f.counts.wrong,0);
 f=fixture(false,false,false,{pillowHits:3,delay:500,pillowDisabledMs:1500});
 assert.equal(f.h.getState().attackDelayMs,500);assert.equal(f.h.setAttackDelay(10),false);assert.equal(f.h.setAttackDelay(Infinity),false);assert.equal(f.h.getState().attackDelayMs,500);
 assert.equal(f.h.setAttackDelay(700),true);assert.equal(f.h.getState().attackDelayMs,700);
 for(let i=0;i<800&&f.h.getState().stage!=='complete';i++)await f.step(100);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.pillow,6);
 assert(f.counts.pillowTimes[0]-f.counts.joinTimes[0]>=1500,'Disabled Pillow control was clicked');
 for(let i=1;i<f.counts.pillowTimes.length;i++)assert(f.counts.pillowTimes[i]-f.counts.pillowTimes[i-1]>=700,'Pillow delay was bypassed');
 f=fixture(true);for(let i=0;i<55;i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert.equal(f.counts.pillow,0);assert.equal(f.counts.wrong,0);
 f=fixture(false,true);for(let i=0;i<60;i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert(f.h.getState().status.includes('Medical tab'));assert.equal(f.counts.medicine,0);
 f=fixture(false,false,true);for(let i=0;i<70;i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert(f.h.getState().status.includes('JOIN FIGHT'));assert.equal(f.counts.pillow,0);
 console.log('LOSS FULL CYCLE PASSED: two solo losses, nested player roots, image identity, scroll, medical tab, label-only FAK row, confirmation, return, final recovery collapsed-kit expansion, bounded expansion failure, ambiguous-kit refusal, adjustable attack spacing and ambiguous-player refusal.');
})().catch(e=>{console.error(e);process.exitCode=1});
