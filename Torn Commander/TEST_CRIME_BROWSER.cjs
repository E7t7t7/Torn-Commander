const {chromium}=require(process.env.COMMANDER_PLAYWRIGHT||'playwright'),fs=require('fs'),assert=require('assert');
(async()=>{
const browser=await chromium.launch({headless:true,...(process.env.COMMANDER_EDGE?{executablePath:process.env.COMMANDER_EDGE}:{})});
const p=await browser.newPage();let requests=0,delay=0;
await p.route('https://www.torn.com/**',async route=>{
 if(route.request().url().includes('sid=crimesData')){requests++;if(delay)await new Promise(r=>setTimeout(r,delay));await route.fulfill({contentType:'application/json',body:JSON.stringify({DB:{ID:1,timestamp:Date.now(),skillLevelTotal:20.1,user:{nerve:98,money:1000},outcome:{result:'success',story:['You succeed'],rewards:[{type:'money',value:50}]}}})});}
 else await route.fulfill({contentType:'text/html',body:'<!doctype html><script uid="123" name="Fixture"></script><main class="crime-root"><article><h3>Apartment</h3><div>Confidence: 40%</div><button onclick="fetch(\'/page.php?sid=crimesData&step=attempt\')">Case</button><button onclick="fetch(\'/page.php?sid=crimesData&step=attempt\')">Burglarize</button></article></main>'});
});
await p.goto('https://www.torn.com/page.php?sid=crimes#/burglary');await p.addScriptTag({content:fs.readFileSync('Torn Commander/BundledScripts/Torn_Crime_Loop.js','utf8')});
const reading=await p.evaluate(()=>__tcCrimeLoop.read('burglary',123));assert(reading.ok&&reading.choices.length===2&&reading.choices[0].confidence===40,'Native scoped controls not parsed');
assert(!(await p.evaluate(()=>__tcCrimeLoop.read('burglary',456))).ok,'Wrong account allowed');
assert((await p.evaluate(()=>__tcCrimeLoop.start('token','burglary',123,'case|Apartment'))).dispatched);
assert((await p.evaluate(()=>__tcCrimeLoop.start('token','burglary',123,'case|Apartment'))).dispatched);
await p.waitForFunction(()=>__tcCrimeLoop.get('token').done);const receipt=await p.evaluate(()=>__tcCrimeLoop.get('token'));
assert(requests===1&&receipt.ok&&receipt.nerve===98&&receipt.cash===50&&receipt.skill===20.1,'Receipt mismatch or duplicate click');
await p.evaluate(()=>__tcCrimeLoop.start('token','burglary',123,'case|Apartment'));assert.equal(requests,1);
delay=100;await p.evaluate(()=>{document.querySelector('button').onclick=()=>{const x=new XMLHttpRequest();x.open('POST','/page.php?sid=crimesData&step=attempt');x.send();};__tcCrimeLoop.start('xhr','burglary',123,'case|Apartment');});
assert(!(await p.evaluate(()=>__tcCrimeLoop.start('other','burglary',123,'case|Apartment'))).ok,'Parallel action allowed');
await p.waitForFunction(()=>__tcCrimeLoop.get('xhr').done);assert.equal(requests,2);
await p.evaluate(()=>{document.querySelector('button').onclick=()=>{};__tcCrimeLoop.start('uncertain','burglary',123,'case|Apartment');});
assert(!(await p.evaluate(()=>__tcCrimeLoop.get('uncertain'))).done);assert(!(await p.evaluate(()=>__tcCrimeLoop.start('again','burglary',123,'case|Apartment'))).ok,'Uncertain action repeated');
await p.evaluate(()=>__tcCrimeLoop.acknowledge('uncertain'));assert((await p.evaluate(()=>__tcCrimeLoop.start('reviewed','burglary',123,'case|Apartment'))).dispatched,'Explicit recovery did not release lease');
const h=await browser.newPage();await h.route('https://www.torn.com/**',r=>r.fulfill({contentType:'text/html',body:'<!doctype html><script uid="123" name="Fixture"></script><main class="crime-root"><div class="interest"><span style="width:65%"></span></div><div class="suspicion"><span style="width:20%"></span></div><article><h3>Cornhole</h3><button>Demo</button><button>Winning</button></article><article><h3>Audience</h3><button>Gather</button></article></main>'}));
await h.goto('https://www.torn.com/page.php?sid=crimes#/hustling');await h.addScriptTag({content:fs.readFileSync('Torn Commander/BundledScripts/Torn_Crime_Loop.js','utf8')});
const hustle=await h.evaluate(()=>__tcCrimeLoop.read('hustling',123));assert(hustle.ok&&hustle.choices.length===3&&hustle.choices[0].interest===65&&hustle.choices[0].suspicion===20,'Native game/audience bars unreadable');
const ui=await browser.newPage({viewport:{width:900,height:950}}),errors=[];ui.on('pageerror',e=>errors.push(e.message));await ui.route('**/*',r=>r.abort());
const source=fs.readFileSync('Torn Commander/MainForm.ModernDashboard.cs','utf8'),coach=fs.readFileSync('Torn Commander/CasinoCoachScript.cs','utf8').split('"""')[1];
await ui.setContent(source.slice(source.indexOf('<!doctype html>'),source.indexOf('<script>')));
await ui.evaluate(()=>{window.__requests=[];window.chrome=window.chrome||{};window.chrome.webview={addEventListener(){},postMessage:m=>__requests.push(m)};});
await ui.addScriptTag({content:source.slice(source.indexOf('<script>')+8,source.indexOf('</script>')).replace('/*CASINO_COACH*/',()=>coach)});
await ui.evaluate(()=>{S.profileId='main';renderCrimeLoop({profileId:'main',options:{crime:'burglary',confidence:70,nerveBudget:100,actionLimit:30}});page('crimes');});
await ui.locator('#clConfidence').fill('75');await ui.getByRole('button',{name:'3. Run session',exact:true}).click();
const cmd=await ui.evaluate(()=>__requests.find(x=>x.cmd==='crimeStart'));assert(cmd.profileId==='main'&&cmd.options.confidence===75&&cmd.options.nerveBudget===100);
await ui.getByRole('button',{name:'Pause',exact:true}).click();assert(await ui.evaluate(()=>__requests.some(x=>x.cmd==='crimePause')));
await ui.evaluate(()=>renderCrimeLoop({profileId:'main',options:{confidence:70}}));assert.equal(await ui.locator('#clConfidence').inputValue(),'75','Heartbeat erased draft');
await ui.evaluate(()=>renderCrimeLoop({profileId:'wife',options:{confidence:100},rows:[]}));assert.equal(await ui.locator('#clConfidence').inputValue(),'100','Account settings retained');
for(const width of [900,600,390]){await ui.setViewportSize({width,height:950});assert(await ui.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+2),'Crime UI overflows '+width);}
assert(!errors.length,errors.join(';'));await browser.close();
console.log('CRIME BROWSER PASSED: native scoped controls and identity, fetch/XHR receipts, duplicate/parallel/uncertain refusal, actual GUI options/pause, draft/profile isolation and responsive layout. Local fixtures; no live nerve spent.');
})().catch(e=>{console.error(e);process.exit(1)});
