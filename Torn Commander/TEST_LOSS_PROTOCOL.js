// Synthetic worker protocol: no game requests, profiles or real fights.
'use strict';
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
function fixture(){
 let clock=1700000000000,tick;
 const local=new Map(),session=new Map(),messages=[],navigations=[];
 const store=m=>({getItem:k=>m.get(k)??null,setItem:(k,v)=>m.set(k,v),removeItem:k=>m.delete(k)});
 class Clock extends Date {static now(){return clock;}}
 const window={chrome:{webview:{postMessage:x=>messages.push(JSON.parse(x))}}};
 const ctx={window,Date:Clock,Math,console,URL,localStorage:store(local),sessionStorage:store(session),
 document:{title:'Torn',body:{innerText:''},querySelector:()=>null,querySelectorAll:()=>[]},
 location:{href:'https://www.torn.com/index.php',hostname:'www.torn.com',pathname:'/index.php',search:'',assign:x=>navigations.push(x)},
 getComputedStyle:()=>({display:'block',visibility:'visible'}),setInterval:fn=>{tick=fn;return 1;},setTimeout:()=>1};
 vm.createContext(ctx);vm.runInContext(source,ctx);
 const h=window.TornCommanderLossCycle;
 h.start({profileId:'one',slot:'A',orderId:'order',targetId:'123'});
 const update=fields=>{const state=h.getState();Object.assign(state,fields);local.set('__tc_loss_auto_cycle_v1',JSON.stringify(state));};
 return {h,tick:()=>tick(),advance:ms=>clock+=ms,messages,navigations,update};
}
(async()=>{
 let f=fixture();await f.tick();const first=f.h.getState().pendingClaimRequest;
 f.h.stop();assert.strictEqual(f.h.onClaim(first,true,'late','Granted'),false);
 assert.strictEqual(f.h.getState().stage,'stopped');assert.strictEqual(f.navigations.length,0,'Late grant restarted stopped worker');
 assert(f.messages.some(x=>x.type==='tc-loss-abandon'&&x.claimId==='late'),'Late lease leaked');
 f=fixture();await f.tick();const request=f.h.getState().pendingClaimRequest;
 f.h.pause();f.h.onClaim(request,true,'late-paused','Granted');
 assert.strictEqual(f.h.getState().stage,'paused');assert.strictEqual(f.navigations.length,0,'Late grant restarted paused worker');
 f=fixture();await f.tick();const granted=f.h.getState().pendingClaimRequest;
 f.h.onClaim(granted,true,'held','Granted');f.h.pause();
 const abandonBefore=f.messages.filter(x=>x.type==='tc-loss-abandon').length;
 f.h.onClaim(granted,true,'held','Late duplicate');
 assert.strictEqual(f.messages.filter(x=>x.type==='tc-loss-abandon').length,abandonBefore,'Late duplicate released held paused-fight lease');
 f=fixture();await f.tick();
 for(let i=0;i<3;i++){f.advance(10001);await f.tick();if(i<2)await f.tick();}
 assert.strictEqual(f.h.getState().stage,'paused','Claim waited forever');
 assert.strictEqual(f.messages.filter(x=>x.type==='tc-loss-claim').length,3,'Claim retry bound wrong');
 assert.strictEqual(f.navigations.length,0,'Retry started unclaimed attack');
 f=fixture();f.update({stage:'commit-wait',claimId:'receipt',replyWaitAt:1700000000000,commitAttempts:1});
 for(let i=0;i<3;i++){f.advance(10001);await f.tick();}
 assert.strictEqual(f.h.getState().stage,'paused','Commit waited forever');
 assert(f.messages.filter(x=>x.type==='tc-loss-commit').every(x=>x.claimId==='receipt'),'Retry changed claim');
 assert.strictEqual(f.navigations.length,0,'Commit replay began new fight');
 f.h.pause();assert.strictEqual(f.h.getState().previousStage,'commit-wait','Repeated pause lost pending loss');
 f.h.resume();assert.strictEqual(f.h.getState().stage,'commit-wait','Resume discarded pending visible loss');
 assert.strictEqual(f.h.onCommit('wrong',true,2,'Old reply'),false,'Wrong acknowledgement accepted');
 assert.strictEqual(f.h.onCommit('receipt',true,2,'Saved'),true);
 assert.strictEqual(f.h.getState().stage,'heal-open');assert.strictEqual(f.navigations.length,1);
 assert.strictEqual(f.h.onCommit('receipt',true,2,'Duplicate'),false,'Duplicate advanced twice');
 f=fixture();await f.tick();
 assert.strictEqual(f.h.onClaim(f.h.getState().pendingClaimRequest,true,'','Bad grant'),false);
 assert.strictEqual(f.navigations.length,0,'Empty claim started fight');
 console.log('LOSS PROTOCOL PASSED: late grants, bounded retries, same-claim replay, resume reconciliation, stale replies and invalid grants.');
})().catch(e=>{console.error(e);process.exitCode=1;});
