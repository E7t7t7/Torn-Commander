const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
function fixture(options={}){
 let now=10000,clicks=0;
 const element=(text='',attrs={})=>({textContent:text,disabled:false,parentElement:null,id:'',getClientRects:()=>[{}],getAttribute:k=>attrs[k]??null,classList:{contains:()=>false},closest:()=>null,scrollIntoView(){},click(){clicks++}});
 const auth=element('',{uid:String(options.owner||123),name:'Player'});
 const token=element(options.tokens??'100');
 const button=element('',{'data-bet':'1000'});button.disabled=!!options.disabled;
 const reels=[element(),element(),element()];
 const document={hidden:!!options.hidden,title:options.challenge?'Just a moment...':'Slots',readyState:'complete',querySelectorAll:s=>s==='script[uid][name]'?[auth]:s==='#tokens'?[token]:s==='.slots-btn-list .betbtn'?(options.noBet?[]:[button]):s==='#barrel0,#barrel1,#barrel2'?reels:[]};
 const context=vm.createContext({document,window:{},location:{href:options.url||'https://www.torn.com/loader.php?sid=slots'},URL,Map,Set,Date:{now:()=>now},getComputedStyle:()=>({display:'block',visibility:'visible'}),MutationObserver:class{observe(){}disconnect(){}}});
 const run=(action='inspect',token='a'.repeat(32))=>vm.runInContext(source.replace('__SLOTS_REQUEST__',JSON.stringify({action,token,owner:123,name:'Player',bet:1000})),context);
 return {run,get clicks(){return clicks},advance:()=>now+=1000};
}
let f=fixture();assert(!f.run().ready);f.advance();assert(f.run().ready);
for(let i=0;i<100;i++){
 const token=i.toString(16).padStart(32,'0');assert(f.run('spin',token).dispatched);
 assert(f.run('spin',token).dispatched);assert.equal(f.clicks,i+1,'Duplicate spin click');
 assert(!f.run().ready);f.run('ack',token);f.advance();
}
assert.equal(f.clicks,100);
for(const options of [{owner:456},{disabled:true},{hidden:true},{challenge:true},{tokens:'0'},{tokens:'unknown'},{noBet:true},{url:'https://example.com/loader.php?sid=slots'}]){
 f=fixture(options);f.run();f.advance();f.run('spin');assert.equal(f.clicks,0,JSON.stringify(options));
}
f=fixture();f.run();f.advance();assert(!f.run('spin','bad').dispatched&&f.clicks===0);
assert(!/fetch\s*\(|XMLHttpRequest|setInterval|clearInterval|JSON\.parse\s*=/.test(source),'Network or animation bypass introduced');
console.log('SLOTS DOM PASSED: 100 native fixed-denomination clicks, duplicate suppression, pending animation, wrong identity, hidden/challenge pages, disabled controls, zero tokens and no network/refills.');
