// Commander-controlled Slots operation: no timers, direct network calls,
// animation changes, token refills, money withdrawals or hidden results.
(() => {
 'use strict';
 const request=__SLOTS_REQUEST__,clean=s=>String(s||'').replace(/\s+/g,' ').trim();
 const visible=e=>!!e&&e.getClientRects().length>0&&getComputedStyle(e).display!=='none'&&getComputedStyle(e).visibility!=='hidden';
 const fail=(note,retryable=false)=>({ok:false,ready:false,retryable,note,dispatched:false});
 const u=new URL(location.href);
 if(u.protocol!=='https:'||!['www.torn.com','torn.com'].includes(u.hostname)||!['/loader.php','/page.php'].includes(u.pathname)||u.searchParams.get('sid')!=='slots')return fail('Open the Torn Slots game page first.',location.href==='about:blank'||document.readyState==='loading');
 if(document.hidden)return fail('Slots page is hidden. Keep it open while the batch runs.');
 if(/just a moment|verify.*human|security check/i.test(document.title))return fail('A browser or security check needs your attention.');
 const inChat=e=>{for(let n=e;n;n=n.parentElement){const id=n.id||'';if(id.startsWith('chat_panel:')||id.startsWith('chat_panel_button:'))return true;}return false;};
 const idOf=a=>{try{const p=new URL(a.getAttribute('href'),location.href);return p.protocol==='https:'&&['www.torn.com','torn.com'].includes(p.hostname)?Number(p.searchParams.get('XID')):0;}catch{return 0;}};
 const auth=new Set(),sidebar=new Set(),named=new Set();
 for(const s of document.querySelectorAll('script[uid][name]')){const id=Number(s.getAttribute('uid'));if(Number.isSafeInteger(id)&&id>0&&clean(s.getAttribute('name')))auth.add(id);}
 for(const a of document.querySelectorAll('a[href*="profiles.php"]')){
  if(inChat(a))continue;
  if([a.getAttribute('aria-label'),a.getAttribute('title'),a.textContent].some(t=>/^(name|username)\s*:/i.test(clean(t)))){const id=idOf(a);if(id>0)auth.add(id);}
 }
 for(const a of document.querySelectorAll('#sidebar a[href*="profiles.php"],#sidebarroot a[href*="profiles.php"],[class*="sidebar"] a[href*="profiles.php"]')){
  if(inChat(a))continue;const id=idOf(a);if(!Number.isSafeInteger(id)||id<=0)continue;sidebar.add(id);
  const label=clean(a.textContent).toLowerCase(),self=clean(request.name).toLowerCase();if(self&&(label===self||label==='name: '+self))named.add(id);
 }
 const owner=auth.size===1?[...auth][0]:auth.size>1?null:named.size===1?[...named][0]:sidebar.size===1?[...sidebar][0]:null;
 if(owner===null)return fail('Waiting for the logged-in account identity.',true);
 if(owner!==Number(request.owner))return fail('The Slots browser belongs to another account. No bet placed.');
 const tokenNodes=[...document.querySelectorAll('#tokens')].filter(visible);
 if(tokenNodes.length!==1)return fail('Waiting for one visible casino token count.',true);
 const tokenText=clean(tokenNodes[0].textContent).replace(/,/g,'');
 if(!/^\d+$/.test(tokenText))return fail('Casino token count is not a verified whole number.');
 const tokens=Number(tokenText);if(!Number.isSafeInteger(tokens)||tokens<0)return fail('Casino token count is invalid.');
 const choices=[...document.querySelectorAll('.slots-btn-list .betbtn')].filter(e=>visible(e)&&Number(e.getAttribute('data-bet'))===Number(request.bet));
 if(choices.length!==1)return fail(choices.length?'Multiple controls match this denomination.':'The chosen denomination is not available yet.',choices.length===0);
 const button=choices[0],barrels=[...document.querySelectorAll('#barrel0,#barrel1,#barrel2')].filter(visible);
 if(barrels.length!==3)return fail('Waiting for all three slot reels.',true);
 let state=window.__tcSlotsBatch;
 if(!state){state={ops:new Map(),busy:null,changed:Date.now(),nodes:[],observer:null};window.__tcSlotsBatch=state;}
 if(state.nodes.length!==3||state.nodes.some((n,i)=>n!==barrels[i])){
  state.observer?.disconnect();state.nodes=barrels;state.changed=Date.now();
  state.observer=new MutationObserver(()=>{state.changed=Date.now();});
  for(const reel of barrels)state.observer.observe(reel,{attributes:true,attributeFilter:['style','class'],childList:true,subtree:true});
 }
 if(request.action==='ack'){
  if(state.busy===request.token)state.busy=null;
  return {ok:true,owner,tokens,ready:false};
 }
 const disabled=button.disabled||button.getAttribute('aria-disabled')==='true'||button.classList.contains('disabled')||!!button.closest('[inert]'),
 ready=!disabled&&!state.busy&&Date.now()-state.changed>=800;
 const board={ok:true,owner,tokens,ready,retryable:!ready,note:ready?'Slots ready.':'Waiting for the current spin/animation to settle.'};
 if(request.action!=='spin')return board;
 if(state.ops.has(request.token))return {dispatched:true,note:'This operation was already dispatched; no repeated click.'};
 if(!ready||tokens<1)return {...fail(tokens<1?'No casino tokens remain.':'Slots changed before dispatch; no bet clicked.'),owner,tokens};
 if(!/^[a-f0-9]{32}$/.test(request.token||''))return fail('Invalid spin operation.');
 // Persist the operation in the page BEFORE the only game-control click.
 state.ops.set(request.token,{tokens,at:Date.now()});state.busy=request.token;state.changed=Date.now();
 try{button.scrollIntoView({block:'center'});button.click();return {dispatched:true,owner,tokens,note:'One spin clicked.'};}
 catch{return {dispatched:true,owner,tokens,note:'Dispatch outcome uncertain. Verify the log; never repeat this token.'};}
})()
