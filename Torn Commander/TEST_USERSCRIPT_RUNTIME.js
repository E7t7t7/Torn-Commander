const fs=require('fs'),vm=require('vm'),assert=require('assert');
const wrappers=JSON.parse(fs.readFileSync(process.argv[2],'utf8'));
function fixture(url='https://www.torn.com/index.php',loading=true,frame=false){
 const nodes={},listeners={},posts=[];
 const element=tag=>({tag,style:{},dataset:{},children:[],appendChild(n){this.children.push(n);if(n.id)nodes[n.id]=n},querySelectorAll(tag){return this.children.filter(n=>n.tag===tag)}});
 const document={readyState:loading?'loading':'complete',documentElement:element('html'),body:element('body'),getElementById:id=>nodes[id]??null,createElement:element,addEventListener:(e,fn)=>{(listeners[e]??=[]).push(fn)}};
 const c={document,location:{href:url},console,Map,localStorage:{getItem:()=>null,setItem:()=>{}},chrome:{webview:{postMessage:m=>posts.push(JSON.parse(m))}}};
 c.window=c;c.top=frame?{}:c;c.self=c;vm.createContext(c);
 return {c,nodes,posts,ready(){document.readyState='complete';for(const fn of listeners.DOMContentLoaded||[])fn()}};
}
let f=fixture();vm.runInContext(wrappers[0],f.c);assert(!f.c.runs);f.ready();assert.equal(f.c.runs,1);
let menu=f.nodes['tc-script-menu'];assert(menu);const button=menu.querySelectorAll('button')[0];assert(button.textContent.includes('Open fixture'));button.onclick();assert(f.c.commandRan);
f=fixture();vm.runInContext(wrappers[1],f.c);assert.equal(f.c.runs,1);
f=fixture('https://www.torn.com/excluded.php');vm.runInContext(wrappers[2],f.c);f.ready();assert(!f.c.runs);
f=fixture(undefined,false,true);vm.runInContext(wrappers[3],f.c);assert(!f.c.runs);
f=fixture(undefined,false);vm.runInContext(wrappers[4],f.c);assert.equal(f.c.runs,1);assert(f.nodes['tc-script-menu']);
f=fixture(undefined,false);vm.runInContext(wrappers[5],f.c);assert.equal(f.posts.length,1);f.c.__tcGmXhrResolve(f.posts[0].id,{status:404,responseText:'missing'});assert.equal(f.c.httpStatus,404);assert(!f.c.networkError);
console.log('USERSCRIPT RUNTIME PASSED: real production wrapper, timing, page exclusions, frames, visible menu callbacks, raw-script compatibility and HTTP responses.');
