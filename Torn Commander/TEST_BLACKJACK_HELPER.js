const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
let player=['A','7'],dealer='6',split=false,double=false,containers=1,result='';
const card=r=>({className:'card-h-'+r});
const button={disabled:false,getAttribute:()=>null,getClientRects:()=>[{}]};
const document={
 querySelector:selector=>selector.includes('dealer-cards')?card(dealer):{},
 querySelectorAll:selector=>selector==='#hit, #stand'?[button]:selector==='.player-cards'?Array.from({length:containers},()=>({})):
 selector.includes('player-cards')?player.map(card):selector==='#split'?(split?[button]:[]):double?[button]:[]
};
const c={document,updateOverlay:r=>result=r};vm.createContext(c);
vm.runInContext(source.slice(source.indexOf('    const A ='),source.indexOf('    // --- UI,')),c);
c.mainObserverCallback();assert.equal(result,'Stand');
double=true;c.mainObserverCallback();assert.equal(result,'Double');
player=['8','8'];dealer='10';split=true;c.mainObserverCallback();assert.equal(result,'Split');
split=false;c.mainObserverCallback();assert.equal(result,'Hit');
containers=2;c.mainObserverCallback();assert.equal(result,'---');
containers=1;player=['unknown','7'];c.mainObserverCallback();assert.equal(result,'---');
console.log('LIVE BLACKJACK HELPER PASSED: available double/split fallbacks, ambiguous hands and unknown ranks.');
