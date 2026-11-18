const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
const start=source.slice(source.indexOf('function slotsStart(){'),source.indexOf('function renderSlots(d){'));
assert(start.startsWith('function slotsStart(){')&&start.includes('casinoAction'),'Actual Slots start handler missing');
const globalNum=source.match(/function num\(id,min,max\)\{[^\n]+/)[0];
function submit(overrides={}){
 const fields={slotsBet:{value:'10000'},slotsCount:{value:'100'},slotsAll:{checked:true},slotsLoss:{value:'0'},slotsTarget:{value:'0'},casinoSessionNote:{textContent:''}};
 for(const [id,value] of Object.entries(overrides))Object.assign(fields[id],value);
 const messages=[],context=vm.createContext({$:id=>fields[id],casinoAction:(cmd,data)=>messages.push(JSON.parse(JSON.stringify({cmd,profileId:'main',...data})))});
 vm.runInContext(globalNum+'\n'+start+'\nslotsStart();',context);
 return {messages,note:fields.casinoSessionNote.textContent};
}
let r=submit();assert.equal(r.messages.length,1);assert.deepEqual(r.messages[0],{cmd:'slotsStart',profileId:'main',bet:10000,count:100,all:true,stopLoss:0,target:0});
assert(!JSON.stringify(r.messages).includes('null'),'NaN/null crossed the native bridge');
r=submit({slotsAll:{checked:false},slotsBet:{value:'1000'},slotsLoss:{value:'25000'},slotsTarget:{value:'50000'}});
assert(r.messages[0].stopLoss===25000&&r.messages[0].target===50000&&!r.messages[0].all);
assert.equal(submit({slotsLoss:{value:'1.15'}}).messages[0].stopLoss,1.15,'Valid two-decimal amount rejected');
for(const change of [{slotsLoss:{value:''}},{slotsLoss:{value:'-1'}},{slotsTarget:{value:'NaN'}},{slotsTarget:{value:'Infinity'}},{slotsLoss:{value:'0.001'}},{slotsCount:{value:'0'}},{slotsCount:{value:'1.5'}},{slotsBet:{value:'2500'}}]){
 r=submit(change);assert.equal(r.messages.length,0,'Invalid command sent: '+JSON.stringify(change));assert(r.note,'Missing visible validation');
}
console.log('SLOTS COMMAND PASSED: actual form handler + global number helper, JSON bridge roundtrip, screenshot $10k/all-tokens/zero limits, specified limits and invalid-input rejection.');
