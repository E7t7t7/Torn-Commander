const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
const card=(r,hidden=false)=>({className:'card-h-'+r,getClientRects:()=>hidden?[]:[{}],getAttribute:()=>null});
function read(options={}){
 const hand={getClientRects:()=>[{}],querySelectorAll:()=>options.player||[card('A'),card('7')]};
 const dealer={getClientRects:()=>[{}],querySelectorAll:()=>options.dealer||[card('6'),card('back')]};
 const button={disabled:!!options.disabled,getAttribute:()=>null,getClientRects:()=>[{}]};
 const document={visibilityState:options.hidden?'hidden':'visible',querySelectorAll:s=>s==='.player-cards'?(options.split?[hand,hand]:[hand]):s==='.dealer-cards'?[dealer]:s==='#hit, #stand'?[button]:s==='#split'&&options.canSplit?[button]:[]};
 return vm.runInNewContext(source,{document,getComputedStyle:()=>({display:'block',visibility:'visible'})});
}
let result=read();assert(result.ok&&result.cards==='A 7'&&result.dealer==='6'&&!result.double&&!result.split);
assert(!read({split:true}).ok,'Ambiguous split hand guessed');
assert(!read({player:[card('A'),card('unknown')]}).ok,'Unknown player card guessed');
assert(!read({dealer:[card('6'),card('K')]}).ok,'Completed dealer hand treated as up-card');
assert(!read({hidden:true}).ok,'Unfocused page scraped');
assert(!read({disabled:true}).ok,'Advice read while no player action exists');
assert(!/fetch\s*\(|XMLHttpRequest|\.click\s*\(|setInterval|MutationObserver/.test(source),'Read-only helper performs game/network/background actions');
console.log('CASINO READ PASSED: visible cards, hidden-card refusal, split ambiguity, completed hand, focused page and zero game actions.');
