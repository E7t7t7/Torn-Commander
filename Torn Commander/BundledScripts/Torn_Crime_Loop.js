// Native visible controls only. Passive receipts; never constructs crime requests.
(()=>{
'use strict';
if(window.__tcCrimeLoop?.version===1)return true;
const clean=x=>String(x||'').replace(/\s+/g,' ').trim(),visible=e=>!!e&&e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden',
label=e=>clean(e.getAttribute('aria-label')||e.textContent||e.value);
const jobs=new Map();let active=null;
function valid(crime){const u=new URL(location.href);return u.protocol==='https:'&&['www.torn.com','torn.com'].includes(u.hostname)&&u.searchParams.get('sid')==='crimes'&&u.hash.toLowerCase().includes('/'+crime)&&!document.hidden&&!/just a moment|verify.*human|security check/i.test(document.title);}
function owner(){const ids=new Set([...document.querySelectorAll('script[uid][name]')].map(e=>Number(e.getAttribute('uid'))).filter(x=>x>0));if(ids.size===1)return [...ids][0];const names=[...document.querySelectorAll('#sidebar [class*="name"] a[href*="profiles.php"],#sidebar a[aria-label="Your profile"]')];const own=new Set(names.map(e=>Number(new URL(e.href,location.href).searchParams.get('XID'))).filter(x=>x>0));return own.size===1?[...own][0]:0;}
function controls(root){return [...root.querySelectorAll('button,[role="button"],input[type="button"],input[type="submit"]')].filter(e=>visible(e)&&!e.disabled&&e.getAttribute('aria-disabled')!=='true'&&!/disabled/.test(e.className));}
function percent(e,name){
 const values=[];
 for(const n of e.querySelectorAll('[aria-label],[title],[role="progressbar"]')){
  const l=clean(n.getAttribute('aria-label')||n.getAttribute('title'));
  if(l.toLowerCase().includes(name)){const v=Number(n.getAttribute('aria-valuenow'));if(n.hasAttribute('aria-valuenow')&&v>=0&&v<=100)values.push(v);else{const m=l.match(/(\d+(?:\.\d+)?)\s*%/);if(m)values.push(Number(m[1]));}}
 }
 for(const n of e.querySelectorAll('[class*="'+name+'"] [style*="width"],[class*="'+name+'"][style*="width"]')){const m=n.style.width.match(/^(\d+(?:\.\d+)?)%$/);if(m&&Number(m[1])<=100)values.push(Number(m[1]));}
 if(values.length)return name==='suspicion'?Math.max(...values):Math.min(...values);
 const m=clean(e.textContent).match(new RegExp(name+'\\s*[:\\-]?\\s*(\\d+(?:\\.\\d+)?)\\s*%','i'));return m?Number(m[1]):null;
}
function read(crime,expected){
 if(!valid(crime)||owner()!==expected)return {ok:false,note:'Open this account’s selected crime in the game browser. Account or page could not be verified.',choices:[]};
 const root=document.querySelector('.crime-root');if(!root||root.querySelector('[class*="loader___"]'))return {ok:false,note:'Crime page is still loading.',choices:[]};
 if(/you are (?:in jail|in hospital)|not enough nerve|insufficient nerve/i.test(clean(root.textContent)))return {ok:false,note:'Torn reports a blocked state or insufficient nerve.',choices:[]};
 const choices=[],elements=new Map(),actions=crime==='burglary'?{scout:/^scout(?:\s*(?:3|\(3\))\s*(?:nerve)?)?$/i,case:/^case(?:\s*(?:2|\(2\))\s*(?:nerve)?)?$/i,burglarize:/^burglar(?:ize|ise)(?:\s*(?:6|\(6\))\s*(?:nerve)?)?$/i}:{gather:/^gather(?: audience(?: members)?)?(?:\s*(?:4|\(4\))\s*(?:nerve)?)?$/i,demo:/^demo(?:nstrate)?(?:\s*(?:2|\(2\))\s*(?:nerve)?)?$/i,win:/^(?:win(?:ning)?|hustle)(?:\s*(?:2|\(2\))\s*(?:nerve)?)?$/i};
 for(const b of controls(root)){
  const action=Object.keys(actions).find(k=>actions[k].test(label(b)));if(!action)continue;
  let card=b.parentElement;while(card&&card!==root&&!card.querySelector('h2,h3,h4,[class*="title"],[class*="name"]'))card=card.parentElement;
  if(!card||card===root)continue;
  const head=card.querySelector('h2,h3,h4,[class*="title"],[class*="name"]'),target=clean(head?.textContent);
  if(!target||target.length>120)continue;
  if(action==='scout'&&!/^(?:residential|commercial|industrial)(?: targets?)?$/i.test(target))continue;
  const confidence=/all clear/i.test(clean(card.textContent))?100:percent(card,'confidence');
  const key=action+'|'+target;if(elements.has(key))return {ok:false,note:'Duplicate target controls are ambiguous. Open one target/game before scanning.',choices:[]};
  const c={key,action,target,cost:action==='scout'?3:action==='burglarize'?6:action==='gather'?4:2,confidence,interest:percent(card,'interest')??percent(root,'interest'),suspicion:percent(card,'suspicion')??percent(root,'suspicion'),dangerous:/too dangerous|temporarily unavailable/i.test(clean(card.textContent))};
  choices.push(c);elements.set(key,b);
 }
 return {ok:true,owner:expected,crime,choices,note:choices.length?'Visible crime controls scanned.':'No unambiguous supported controls. Open a property or game and scan again.',elements};
}
function receipt(url,data,token){
 if(!active||!token||token!==active)return;let u;try{u=new URL(url,location.href);}catch{return;}
 if(u.origin!==location.origin||u.searchParams.get('sid')!=='crimesData'||u.searchParams.get('step')!=='attempt')return;
 const db=data?.DB;if(!db?.outcome||!db.user||!Number.isFinite(db.user.nerve))return;
 const j=jobs.get(active);if(!j||j.done||!valid(j.crime)||owner()!==j.owner)return;
 const rewards=Array.isArray(db.outcome.rewards)?db.outcome.rewards:[],result=String(db.outcome.result||'');
 j.done=true;j.ok=true;j.result=result;j.critical=/critical|jail|hospital/i.test(result+' '+JSON.stringify(db.outcome.story||[]));j.nerve=db.user.nerve;j.skill=Number.isFinite(db.skillLevelTotal)?db.skillLevelTotal:null;
 j.cash=rewards.filter(x=>x.type==='money'&&Number.isFinite(x.value)).reduce((a,x)=>a+x.value,0);
 j.signature=String(db.timestamp)+':'+String(db.ID);j.note='One native crime result received. Cash rewards exclude unpriced item loot.';active=null;
}
function attempt(url){try{const u=new URL(url,location.href);return u.origin===location.origin&&u.searchParams.get('sid')==='crimesData'&&u.searchParams.get('step')==='attempt';}catch{return false;}}
const original=window.fetch;window.fetch=function(...args){const url=args[0]?.url||args[0],token=active,p=original.apply(this,args);if(token&&attempt(url))p.then(response=>{response.clone().json().then(d=>receipt(response.url||url,d,token)).catch(()=>{});}).catch(()=>{});return p;};
const open=XMLHttpRequest.prototype.open,send=XMLHttpRequest.prototype.send;
XMLHttpRequest.prototype.open=function(method,url,...rest){this.__tcCrimeUrl=url;return open.call(this,method,url,...rest);};
XMLHttpRequest.prototype.send=function(...args){const token=active,url=this.__tcCrimeUrl;if(token&&attempt(url))this.addEventListener('load',()=>{try{receipt(this.responseURL||url,this.responseType==='json'?this.response:JSON.parse(this.responseText),token);}catch{}},{once:true});return send.apply(this,args);};
window.__tcCrimeLoop={version:1,read:(crime,expected)=>{const r=read(crime,expected);delete r.elements;return r;},start:(token,crime,expected,key)=>{
 if(jobs.has(token))return {ok:true,dispatched:true,note:'Already dispatched; no duplicate click.'};
 if(active)return {ok:false,note:'Previous action is still awaiting its result.'};
 const r=read(crime,expected),b=r.elements?.get(key);if(!r.ok||!b)return {ok:false,note:r.note||'The planned control changed.'};
 const job={token,crime,owner:expected,done:false,ok:false,note:'Waiting for the native result.'};jobs.set(token,job);active=token;
 try{b.scrollIntoView({block:'center'});b.click();return {ok:true,dispatched:true};}catch{job.done=true;job.note='Dispatch uncertain. Review the game before another action.';active=null;return {ok:false,note:job.note};}
},acknowledge:token=>{if(active===token)active=null;return {cleared:true};},get:token=>jobs.get(token)||{token,done:false,ok:false,note:'Pending result is unavailable on this page. Review before clearing.'}};
return true;
})()
