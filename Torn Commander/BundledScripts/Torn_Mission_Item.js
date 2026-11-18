// Native item delivery adapter. Never calls a game endpoint directly.
(()=>{
 'use strict';const r=__MISSION_ITEM_REQUEST__,clean=x=>String(x||'').replace(/\s+/g,' ').trim(),visible=e=>!!e&&e.getClientRects().length>0&&getComputedStyle(e).display!=='none'&&getComputedStyle(e).visibility!=='hidden';
 const fail=note=>({ok:false,note}),url=new URL(location.href),name=clean(r.itemName).toLowerCase();
 if(url.protocol!=='https:'||!['www.torn.com','torn.com'].includes(url.hostname)||url.pathname!=='/item.php'||document.hidden)return fail('Keep this account’s Items / Send page open.');
 const ids=new Set([...document.querySelectorAll('script[uid][name]')].map(e=>Number(e.getAttribute('uid'))).filter(x=>Number.isSafeInteger(x)&&x>0));
 if(ids.size!==1||[...ids][0]!==r.owner||!Number.isSafeInteger(r.recipient)||r.recipient<=0||r.recipient===r.owner||!Number.isSafeInteger(r.itemId)||r.itemId<=0||!name)return fail('Item, recipient or account identity was not verified.');
 const inChat=e=>{for(let n=e;n;n=n.parentElement){if((n.id||'').startsWith('chat_panel:')||(n.id||'').startsWith('chat_panel_button:'))return true;}return false;};
 const targetId=a=>{try{const u=new URL(a.getAttribute('href'),location.href);return u.protocol==='https:'&&['www.torn.com','torn.com'].includes(u.hostname)&&u.pathname==='/profiles.php'?Number(u.searchParams.get('XID')):0;}catch{return 0;}};
 const sent=window.__tcMissionItemOps||(window.__tcMissionItemOps=new Set()),key=r.token+'|'+r.action;
 if(!/^[a-f0-9]{32}$/.test(r.token||''))return fail('Missing durable delivery token.');
 const proof=[...document.querySelectorAll('p,div,li')].filter(e=>!inChat(e)&&visible(e)&&clean(e.textContent).length<600&&/^you (?:have )?sent\b/i.test(clean(e.textContent))&&clean(e.textContent).toLowerCase().includes(name)&&[...e.querySelectorAll('a[href]')].some(a=>targetId(a)===r.recipient));
 if(proof.length)return {ok:true,phase:'sent',note:'Visible item-send receipt matches the item and recipient.'};
 const forms=[...document.querySelectorAll('form')].filter(e=>!inChat(e)&&visible(e)&&clean(e.textContent).toLowerCase().includes(name)&&[...e.querySelectorAll('input')].some(x=>/^(itemID|item_id|item)$/i.test(x.name)&&Number(x.value)===r.itemId));
 if(forms.length===1){
  const form=forms[0],inputs=[...form.querySelectorAll('input')];
  const recipients=inputs.filter(e=>visible(e)&&/^(recipient|receiver|userID|user_id|XID|playerID|player_id)$/i.test(e.name));
  const quantities=inputs.filter(e=>visible(e)&&/^(quantity|amount|qty)$/i.test(e.name));
  if(recipients.length!==1||quantities.length!==1)return fail('Native Send form fields are not unambiguous. Complete this delivery manually.');
  const recipient=recipients[0],quantity=quantities[0];
  if(r.action==='fill'){
   if(sent.has(key))return {ok:true,phase:'resolve',note:'Fields already entered; waiting for native recipient verification.'};
   sent.add(key);const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
   setter.call(recipient,String(r.recipient));recipient.dispatchEvent(new Event('input',{bubbles:true}));recipient.dispatchEvent(new Event('change',{bubbles:true}));
   setter.call(quantity,'1');quantity.dispatchEvent(new Event('input',{bubbles:true}));quantity.dispatchEvent(new Event('change',{bubbles:true}));
   return {ok:true,phase:'resolve',note:'Entered one item and exact recipient ID. Waiting for Torn to resolve the recipient.'};
  }
  const matched=[...form.querySelectorAll('a[href]')].filter(a=>visible(a)&&targetId(a)===r.recipient);
  if(recipient.value.trim()!==String(r.recipient)||quantity.value.trim()!=='1'||matched.length!==1)return {ok:true,phase:'fill',note:'The native form has not verified the exact recipient and one-item quantity.'};
  const buttons=[...form.querySelectorAll('button,input[type="submit"],input[type="button"]')].filter(e=>visible(e)&&!e.disabled&&/^send(?: item)?$/i.test(clean(e.textContent||e.value)));
  if(buttons.length!==1)return fail('One native Send button is not available.');
  if(r.action==='send'){
   if(sent.has(key))return {ok:true,phase:'wait',note:'Send was already dispatched. No repeated transfer.'};
   sent.add(key);buttons[0].click();return {ok:true,phase:'wait',note:'One native Send submitted. Waiting for its receipt; never retry blindly.'};
  }
  return {ok:true,phase:'send',note:'The native form verifies one exact item and recipient.'};
 }
 if(forms.length>1)return fail('Multiple Send forms match this item. No transfer.');
 const rows=[...new Set([...document.querySelectorAll('li span.name,li .name')].filter(e=>!inChat(e)&&visible(e)&&clean(e.textContent).toLowerCase()===name).map(e=>e.closest('li')))].filter(e=>!(/faction|armory|borrowed/i.test(clean(e.textContent)))&&e.getAttribute('data-faction-owned')!=='true');
 if(rows.length!==1)return fail('Open the inventory category containing the exact mission item. No item was sent.');
 const row=rows[0];const rowId=Number(row.getAttribute('data-item')||row.getAttribute('data-itemid')||row.getAttribute('data-item-id'));
 if(rowId!==r.itemId)return fail('The inventory row does not prove the exact mission item ID. Use native Send manually.');
 const sends=[...row.querySelectorAll('button,a,[role="button"]')].filter(e=>visible(e)&&!e.disabled&&/^(send|send this item|send item)$/i.test(clean(e.getAttribute('aria-label')||e.textContent)));
 if(sends.length===1){
  if(r.action==='openSend'&&!sent.has(key)){sent.add(key);sends[0].click();}
  return {ok:true,phase:'openSend',note:'Opening this exact item’s native Send form.'};
 }
 const titles=[...row.querySelectorAll('.title-wrap,[role="tab"]')].filter(visible);
 if(titles.length!==1)return fail('Expand the mission item manually; no send control could be verified.');
 if(r.action==='expand'&&!sent.has(key)){sent.add(key);titles[0].click();}
 return {ok:true,phase:'expand',note:'Opening the exact item row. No item consumed or transferred.'};
})()
