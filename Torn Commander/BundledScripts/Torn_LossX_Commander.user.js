// ==UserScript==
// @name Torn Commander - Loss X-change companion
// @namespace torn-commander
// @version 0.6.105
// @description Build local Commander orders from visible Loss X-change loss listings.
// @match https://www.torn.com/*
// @run-at document-end
// @noframes
// @grant none
// @license MIT
// ==/UserScript==
(()=>{
 'use strict';
 if(window.top!==window||!window.chrome?.webview||window.__tcLossXCommanderInstalled)return;
 window.__tcLossXCommanderInstalled=true;
 const pending=new Map();
 const read=card=>{
  if(!card.classList.contains('kind-loss')||card.classList.contains('kind-escape')||card.classList.contains('tlx-card-own'))return null;
  const title=card.querySelector('.tlx-card-title')?.textContent?.trim()||'';
  const target=title.match(/\[(\d{1,9})\]\s*$/);
  const count=[...card.querySelectorAll('.tlx-pill.open')].map(x=>x.textContent.trim().match(/^([\d,]+) losses? needed$/i)).filter(Boolean);
  const price=[...card.querySelectorAll('.tlx-sub')].map(x=>x.textContent.trim().match(/^\$([\d,]+)\/loss payout\b/i)).filter(Boolean);
  if(!target||count.length!==1||price.length!==1)return null;
  const targetId=Number(target[1]),remaining=Number(count[0][1].replaceAll(',','')),pay=Number(price[0][1].replaceAll(',',''));
  if(!Number.isSafeInteger(targetId)||targetId<=0||!Number.isSafeInteger(remaining)||remaining<1||remaining>1000000||!Number.isSafeInteger(pay)||pay<0||pay>1000000000000)return null;
  return {actionType:'loss',targetId,buyer:title.slice(0,target.index).trim()||String(targetId),remaining,pay};
 };
 const scan=()=>{
  for(const card of document.querySelectorAll('.tlx-card.kind-loss')){
   if(card.querySelector('[data-tc-lossx-claim]')||!read(card))continue;
   const button=document.createElement('button');button.type='button';button.setAttribute('data-tc-lossx-claim','1');button.textContent='Claim in Commander';button.style.cssText='margin:8px 0;padding:7px 12px;border:1px solid #69d8a4;border-radius:6px;background:#15352a;color:#e8fff5;cursor:pointer';
   button.addEventListener('click',event=>{
    event?.preventDefault();event?.stopPropagation();
    const listing=read(card);if(!listing){button.textContent='Listing changed — refresh Browse contracts';return;}
    const nonce=[...crypto.getRandomValues(new Uint8Array(16))].map(x=>x.toString(16).padStart(2,'0')).join('');
    const entry={button,timer:null};pending.set(nonce,entry);button.disabled=true;button.textContent='Sending listing to Commander…';
    try{
     entry.timer=setTimeout(()=>{if(!pending.has(nonce))return;pending.delete(nonce);button.disabled=false;button.textContent='No response — retry import';button.title='Commander did not acknowledge this listing. Refresh the Torn tab and try again.';},12000);
     window.chrome.webview.postMessage(JSON.stringify({type:'tc-lossx-import',nonce,...listing}));
    }catch{clearTimeout(entry.timer);pending.delete(nonce);button.disabled=false;button.textContent='Import failed — retry';}

   });
   card.appendChild(button);
  }
 };
 window.addEventListener('tc-lossx-import-result',event=>{
  const result=event.detail,entry=pending.get(result?.nonce);if(!entry)return;const button=entry.button;
  clearTimeout(entry.timer);button.title=String(result.message||'');
  if(result.state==='choose'){button.disabled=true;button.textContent='Finish account choice in Loss Desk';return;}
  pending.delete(result.nonce);button.disabled=false;button.textContent=result.ok?'Order ready — Start in Loss Desk':'Claim in Commander';

 });
 let scheduled=false;
 const queue=()=>{if(scheduled)return;scheduled=true;setTimeout(()=>{scheduled=false;scan();},100);};
 const start=()=>{scan();new MutationObserver(queue).observe(document.body,{childList:true,subtree:true,characterData:true});};
 if(document.body)start();else document.addEventListener('DOMContentLoaded',start,{once:true});
})();
