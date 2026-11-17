const fs=require('fs'),vm=require('vm'),assert=require('assert'),path=require('path');
const src=fs.readFileSync(path.join(__dirname,'BundledScripts/Torn_LossX_Commander.user.js'),'utf8');
const posts=[],listeners={},timers=new Map();let timerId=0;
function card(kind='kind-loss',own=false,count='20 losses needed'){
 const classes=new Set(['tlx-card',kind,...(own?['tlx-card-own']:[])]),buttons=[];
 return {classList:{contains:x=>classes.has(x)},querySelector:s=>s==='.tlx-card-title'?{textContent:'Buyer [123]'}:s==='[data-tc-lossx-claim]'?buttons[0]:null,
 querySelectorAll:s=>s==='.tlx-pill.open'?[{textContent:count}]:s==='.tlx-sub'?[{textContent:'$300,000/loss payout, paid in cash once invoiced and validated'}]:[],
 appendChild:b=>buttons.push(b),buttons};
}
const valid=card(),own=card('kind-loss',true),escape=card('kind-escape'),empty=card('kind-loss',false,'0 losses needed'),malformed=card('kind-loss',false,'Unknown');
const window={chrome:{webview:{postMessage:s=>posts.push(JSON.parse(s))}},addEventListener:(n,f)=>listeners[n]=f};window.top=window;
const document={body:{},querySelectorAll:()=>[valid,own,escape,empty,malformed],createElement:()=>({style:{},setAttribute(){},addEventListener(n,f){this[n]=f;}})};
let serial=0;const crypto={getRandomValues:a=>{a.fill(++serial);return a;}};
const ctx={window,document,crypto,Uint8Array,Map,MutationObserver:class{constructor(f){this.callback=f}observe(){}},setTimeout:(f,ms)=>{const id=++timerId;timers.set(id,f);return id;},clearTimeout:id=>timers.delete(id)};
vm.runInNewContext(src,ctx);
assert.equal(valid.buttons.length,1);assert.equal(own.buttons.length+escape.buttons.length+empty.buttons.length+malformed.buttons.length,0);
valid.buttons[0].click();
assert.equal(posts[0].type,'tc-lossx-import');assert.equal(posts[0].targetId,123);assert.equal(posts[0].remaining,20);assert.equal(posts[0].pay,300000);assert.equal(posts[0].buyer,'Buyer');
assert(valid.buttons[0].disabled);
listeners['tc-lossx-import-result']({detail:{nonce:posts[0].nonce,ok:false,state:'choose',message:'Choose accounts in Loss Desk'}});
assert(valid.buttons[0].disabled);assert.equal(valid.buttons[0].textContent,'Finish account choice in Loss Desk');assert.equal(timers.size,0);
listeners['tc-lossx-import-result']({detail:{nonce:posts[0].nonce,ok:true,message:'Ready'}});
assert(!valid.buttons[0].disabled);assert.equal(valid.buttons[0].textContent,'Order ready — Start in Loss Desk');
valid.buttons[0].click();for(const timeout of [...timers.values()])timeout();assert(!valid.buttons[0].disabled);assert.equal(valid.buttons[0].textContent,'No response — retry import');
vm.runInNewContext(src,ctx);assert.equal(valid.buttons.length,1,'Script injected twice');
console.log('LOSS X-CHANGE COMPANION PASSED: visible loss listings, dynamic claim fields, acknowledgements, own/escape/sold-out exclusion and duplicate injection.');
