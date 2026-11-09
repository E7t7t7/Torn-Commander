const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2]||'BundledScripts/Torn_City_Scanner.js','utf8');
function harness({layers=[],images=[],mapReady=true,host='www.torn.com'}={}){
 let pans=0,removed=0,clicks=0;const overlays=[];
 const map={eachLayer:fn=>layers.forEach(fn),hasLayer:l=>layers.includes(l),panTo:()=>pans++,removeLayer:()=>removed++};
 const context={location:{protocol:'https:',hostname:host,pathname:'/city.php',href:'https://'+host+'/city.php#map'},URL,document:{querySelectorAll:()=>images,querySelector:()=>mapReady?{}:null},console};
 context.window=context;context.torn=mapReady?{map:{lmap:map}}:null;
 context.L={layerGroup:()=>({addTo:()=>({})}),circleMarker:(pos,opt)=>{assert.strictEqual(opt.interactive,false);overlays.push(pos);return{addTo(){}}}};
 vm.createContext(context);vm.runInContext(source,context);return {api:context.__TornCommanderCity,context,map,counts:()=>({pans,removed,clicks}),overlays};
}
const marker=id=>({options:{iconUrls:['/images/city_items/'+id+'/small.png']},getLatLng:()=>({lat:1,lng:2}),fire(){throw Error('Collection event must never be fired')}});
const a=marker(67),b=marker(67),wrong={options:{iconUrls:['https://evil.invalid/city_items/68/a.png']},getLatLng:()=>({lat:1,lng:2})};
const h=harness({layers:[a,b,wrong]});let result=h.api.scan();assert.equal(result.items.length,2,'duplicate item copies lost or hostile URL accepted');assert.equal(h.overlays.length,2);
assert(h.api.focus(0));assert.equal(h.counts().pans,1);assert(!h.api.focus(-1));assert(!h.api.focus(99));
h.map.hasLayer=()=>false;assert(!h.api.focus(0),'collected stale marker focus accepted');
h.api.clear();assert(!h.api.focus(0));assert.equal(h.counts().removed,1);
assert.equal(harness({host:'evil.invalid'}).api.scan().state,'wrong-page');
assert.equal(harness({mapReady:false}).api.scan().state,'not-ready');
assert.equal(harness().api.scan().items.length,0);
const image={src:'https://www.torn.com/images/items/68/small.png',alt:'Small First Aid Kit',isConnected:true,style:{outline:'old'},scrollIntoView(){this.scrolled=true}};
const dom=harness({mapReady:false,images:[image]});result=dom.api.scan();assert.equal(result.items[0].id,68);assert(dom.api.focus(0));assert(image.scrolled);dom.api.clear();assert.equal(image.style.outline,'old');
const existing=h.api;vm.runInContext(source,h.context);assert.strictEqual(h.context.__TornCommanderCity,existing);
assert(!/\b(fetch|XMLHttpRequest|setInterval|MutationObserver)\b|\.(click|fire)\s*\(/.test(source),'Scanner added network/automatic event behavior');
console.log('CITY SCANNER PASSED: canvas and image markers, duplicate quantities, hostile URLs, wrong/loading/empty pages, passive rings, manual focus, stale markers, clearing and reinjection. No network or collection events.');
