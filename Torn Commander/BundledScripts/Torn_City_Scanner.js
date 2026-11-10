// Torn Commander city-map scanner. One original pickup event per explicit user command; never loops or requests pickup endpoints.
(() => {
 'use strict';
 const key='__TornCommanderCity';
 if(window[key])return;
 let entries=[],highlights=null,lastMap=null,styled=[],pending=null,ticket="";
 const attempted=new WeakSet();
 const alive=e=>e.marker?!!(e.map&&typeof e.map.hasLayer==="function"&&e.map.hasLayer(e.marker)):!!(e.el&&e.el.isConnected);
 const here=()=>location.protocol==='https:'&&['www.torn.com','torn.com'].includes(location.hostname)&&location.pathname==='/city.php';
 function clear(){
  ticket="";
  if(highlights&&lastMap){try{lastMap.removeLayer(highlights);}catch(_){}}
  highlights=null;lastMap=null;
  for(const [el,old] of styled){if(el&&el.style)el.style.outline=old;}styled=[];entries=[];
 }
 function idFrom(value,city){
  try{const u=new URL(String(value),location.href);if(!['www.torn.com','torn.com'].includes(u.hostname))return 0;
   const m=u.pathname.match(city?/\/city_items\/([0-9]+)\//:/\/images\/items\/([0-9]+)\//);
   const id=m?Number(m[1]):0;return Number.isSafeInteger(id)&&id>0&&id<=2147483647?id:0;
  }catch(_){return 0;}
 }
 function scan(){
  clear();
  if(!here())return {state:'wrong-page',note:'Open City Map in the selected account, then press Scan City.',items:[]};
  const map=window.torn&&window.torn.map&&window.torn.map.lmap;
  if(map&&typeof map.eachLayer==='function'){
   map.eachLayer(layer=>{
    const urls=layer.options&&layer.options.iconUrls;
    if(!Array.isArray(urls)||typeof layer.getLatLng!=='function')return;
    const id=urls.map(u=>idFrom(u,true)).find(Boolean);if(!id)return;
    const pos=layer.getLatLng();if(!pos||!Number.isFinite(pos.lat)||!Number.isFinite(pos.lng))return;
    if(entries.some(e=>e.marker===layer))return;
    entries.push({id,marker:layer,map,pos,name:String(layer.options.title||'').slice(0,100)});
   });
   lastMap=map;
   if(window.L&&typeof L.layerGroup==='function'&&typeof L.circleMarker==='function'){
    highlights=L.layerGroup().addTo(map);
    for(const e of entries)L.circleMarker(e.pos,{radius:18,color:'#54d6c1',weight:3,fillOpacity:0.12,interactive:false}).addTo(highlights);
   }
  }else{
   for(const el of document.querySelectorAll('img.map-user-item-icon')){
    const id=idFrom(el.currentSrc||el.src,false);if(!id||!el.isConnected||entries.some(e=>e.el===el))continue;
    entries.push({id,el,name:String(el.alt||el.title||'').slice(0,100)});styled.push([el,el.style.outline]);el.style.outline='3px solid #54d6c1';
   }
  }
  const ready=!!map||!!document.querySelector('.leaflet-container')||entries.length>0;
  ticket='city_'+Date.now()+'_'+Math.random().toString(36).slice(2);
  if(pending&&!alive(pending))pending=null;
  return {state:ready?'scanned':'not-ready',pending:!!pending,ticket,note:!ready?'City map is not ready. Select Map, let it load, then scan again.':pending?'Pickup still pending. If Torn shows a confirmation, complete it manually. Press again after the marker disappears; it will not be clicked twice.':entries.length?'Press Collect next item once for one original item action. Each next item needs another press; any Torn confirmation stays manual.':'No item markers were found in the loaded map. This does not guarantee there are no items elsewhere or still loading.',observedUtc:new Date().toISOString(),items:entries.map((e,index)=>({index,id:e.id,name:e.name||'Item #'+e.id}))};
 }
 function focus(index){
  if(!here()||!Number.isInteger(index)||!entries[index])return false;
  const e=entries[index];
  if(e.marker){if(!lastMap||typeof lastMap.hasLayer!=='function'||!lastMap.hasLayer(e.marker))return false;lastMap.panTo(e.pos);return true;}
  if(!e.el||!e.el.isConnected)return false;e.el.scrollIntoView({block:'center',inline:'center'});return true;
 }
 function collectNext(expectedTicket){
  if(!here()||document.hidden)return {state:'blocked',note:'Keep the selected City Map visible before collecting.',items:[]};
  if(!expectedTicket||expectedTicket!==ticket)return {state:'stale',note:'This click belongs to an old scan. Scan City again; no item was clicked.',items:[]};
  ticket=''; // Consume the command before any page event; rapid duplicate messages cannot repeat it.
  if(pending&&alive(pending)){const result=scan();result.state='waiting';return result;}
  pending=null;
  const next=entries.find(e=>alive(e)&&!attempted.has(e.marker||e.el));
  if(!next){const result=scan();result.note='No unattempted item remains in the scanned map. Scan again or check Torn’s visible result.';return result;}
  const control=next.marker||next.el;
  if(next.marker&&typeof next.marker.fire!=='function'||next.el&&typeof next.el.click!=='function'){const result=scan();result.note='This marker has no supported pickup action. Use Show on map and click it manually.';return result;}
  pending=next;attempted.add(control);
  try{
   if(next.marker)next.marker.fire('click');else next.el.click();
  }catch(_){const result=scan();result.state='uncertain';result.note='The item action did not finish normally. Check Torn’s result manually; it will not be retried automatically.';return result;}
  const result=scan();
  result.state=pending?'waiting':'dispatched';
  result.note=pending?'One item action sent. Wait for Torn; complete any visible confirmation manually. Press again to check and move to the next item.':'One item action sent; its marker disappeared. Check Torn’s receipt. Press again for the next item.';
  return result;
 }
 window[key]={scan,focus,clear,collectNext};
})();
