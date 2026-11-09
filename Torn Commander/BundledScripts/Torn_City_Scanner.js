// Torn Commander city-map scanner. Reads the open map only; no background requests or collection.
(() => {
 'use strict';
 const key='__TornCommanderCity';
 if(window[key])return;
 let entries=[],highlights=null,lastMap=null,styled=[];
 const here=()=>location.protocol==='https:'&&['www.torn.com','torn.com'].includes(location.hostname)&&location.pathname==='/city.php';
 function clear(){
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
    entries.push({id,marker:layer,pos,name:String(layer.options.title||'').slice(0,100)});
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
  return {state:ready?'scanned':'not-ready',note:!ready?'City map is not ready. Select Map, let it load, then scan again.':entries.length?'Items are highlighted. Show each on the map, then click the original item yourself to collect it. Rescan after pickups.':'No item markers were found in the loaded map. This does not guarantee there are no items elsewhere or still loading.',observedUtc:new Date().toISOString(),items:entries.map((e,index)=>({index,id:e.id,name:e.name||'Item #'+e.id}))};
 }
 function focus(index){
  if(!here()||!Number.isInteger(index)||!entries[index])return false;
  const e=entries[index];
  if(e.marker){if(!lastMap||typeof lastMap.hasLayer!=='function'||!lastMap.hasLayer(e.marker))return false;lastMap.panTo(e.pos);return true;}
  if(!e.el||!e.el.isConnected)return false;e.el.scrollIntoView({block:'center',inline:'center'});return true;
 }
 window[key]={scan,focus,clear};
})();
