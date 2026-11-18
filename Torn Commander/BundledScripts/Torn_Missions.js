// One bounded mission-page operation. No direct game requests or timers.
(()=>{
 'use strict';
 const r=__MISSION_REQUEST__,clean=x=>String(x||'').replace(/\s+/g,' ').trim(),visible=e=>!!e&&e.getClientRects().length>0&&getComputedStyle(e).display!=='none'&&getComputedStyle(e).visibility!=='hidden';
 const fail=note=>({ok:false,note,cards:[]}),u=new URL(location.href);
 if(u.protocol!=='https:'||!['www.torn.com','torn.com'].includes(u.hostname)||!['/page.php','/loader.php'].includes(u.pathname)||u.searchParams.get('sid')!=='missions')return fail('Open the Missions page in this account and scan again.');
 if(document.hidden)return fail('Keep the Missions game tab open.');
 if(/just a moment|verify.*human|security check/i.test(document.title))return fail('The browser needs your attention.');
 const owners=new Set();for(const e of document.querySelectorAll('script[uid][name]')){const id=Number(e.getAttribute('uid'));if(Number.isSafeInteger(id)&&id>0&&clean(e.getAttribute('name')))owners.add(id);}
 if(owners.size!==1||[...owners][0]!==r.owner)return fail('The logged-in mission account could not be verified. No action authorized.');
 const controls=e=>[...e.querySelectorAll('button,a,[role="button"],input[type="button"],input[type="submit"]')].filter(x=>visible(x)&&!x.disabled&&x.getAttribute('aria-disabled')!=='true'&&!x.classList.contains('disabled'));
 const label=e=>clean(e.getAttribute('aria-label')||e.textContent||e.value);
 const accept=e=>/^accept(?: (?:contract|mission))?$/i.test(label(e)),complete=e=>/^complete(?: (?:contract|mission))?$/i.test(label(e));
 const root=document.querySelector('#missionsMainContainer');if(!root)return fail('The mission list has not loaded yet.');
 const panels=[...root.querySelectorAll('.giver-cont-wrap > div[id^="mission"]')];
 if(new Set(panels.map(x=>x.id)).size!==panels.length)return fail('Mission IDs are ambiguous; no action authorized.');
 const cards=panels.map(e=>{
  const head=e.querySelector('.title-black'),title=clean(e.getAttribute('data-original-title')||(head?.childNodes?.[0]?.textContent)||head?.textContent);
  const task=e.querySelector('.tasks-wrap'),copy=e.cloneNode(true);for(const x of copy.querySelectorAll('.tt-mission-information'))x.remove();
  const objective=clean(task?.textContent),text=clean(copy.textContent).slice(0,12000),targets=[];
  for(const a of (task||e).querySelectorAll('a[href]')){try{const x=new URL(a.getAttribute('href'),location.href);if(x.protocol!=='https:'||!['www.torn.com','torn.com'].includes(x.hostname))continue;const id=Number(x.pathname==='/profiles.php'?x.searchParams.get('XID'):x.searchParams.get('sid')==='attack'?x.searchParams.get('user2ID'):0);if(Number.isSafeInteger(id)&&id>0&&!targets.includes(id))targets.push(id);}catch{}}
  const items=[];for(const a of (task||e).querySelectorAll('a[href]')){try{const x=new URL(a.getAttribute('href'),location.href),id=Number(x.searchParams.get('itemID'));if(x.protocol==='https:'&&['www.torn.com','torn.com'].includes(x.hostname)&&Number.isSafeInteger(id)&&id>0)items.push({id,name:clean(a.textContent)});}catch{}}
  const buttons=controls(e),canAccept=buttons.some(accept),canComplete=buttons.some(complete);
  return {id:e.id,title,objective,text,targets,itemId:items.length===1?items[0].id:0,itemName:items.length===1?items[0].name:"",canAccept,canComplete,active:!canAccept&&(canComplete||buttons.some(x=>/^(?:abandon|cancel)(?: contract| mission)?$/i.test(label(x)))||/time\s+(?:left|remaining)|expires?\s+in|contract accepted|mission in progress/i.test(text)),visible:visible(e),signature:JSON.stringify([title,objective,targets,items])};
 }).filter(x=>x.title);
 if(r.action==='scan')return {ok:true,owner:r.owner,cards,note:cards.length?'Read contracts loaded for the current agent.':'No loaded contracts found. Select an agent/contract in Torn and scan again.'};
 const card=cards.find(x=>x.id===r.id&&x.title===r.title),panel=panels.find(x=>x.id===r.id);if(!card||!panel)return fail('The selected contract changed. Scan again.');
 if(r.action==='select'){
  if(!card.visible){const links=[...root.querySelectorAll('a[href]')].filter(a=>visible(a)&&a.getAttribute('href')==='#'+card.id);if(links.length!==1)return fail('Select this mission in Torn, then scan again.');links[0].click();}
  return {ok:true,note:'Mission selected.'};
 }
 if(!card.visible||card.signature!==r.signature)return fail('Mission visibility or objective changed. Scan again before acting.');
 const buttons=controls(panel).filter(r.action==='accept'?accept:r.action==='complete'?complete:()=>false);
 if(buttons.length!==1)return fail('One verified '+r.action+' control is not available. Nothing clicked.');
 if(!/^[a-f0-9]{32}$/.test(r.token||''))return fail('Missing mission action token.');
 const sent=window.__tcMissionActions||(window.__tcMissionActions=new Set());if(sent.has(r.token))return {ok:true,dispatched:true,note:'Action already dispatched; not clicked twice.'};
 sent.add(r.token);buttons[0].click();return {ok:true,dispatched:true,note:'One native mission action dispatched. Verify its page result before another.'};
})()
