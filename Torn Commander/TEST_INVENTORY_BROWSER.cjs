
const {chromium}=require(process.env.COMMANDER_PLAYWRIGHT||'playwright'),fs=require('fs'),assert=require('assert');
(async()=>{
 const browser=await chromium.launch({headless:true,...(process.env.COMMANDER_EDGE?{executablePath:process.env.COMMANDER_EDGE}:{})});
 const page=await browser.newPage({viewport:{width:1100,height:950}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const src=fs.readFileSync('Torn Commander/MainForm.ModernDashboard.cs','utf8'),coach=fs.readFileSync('Torn Commander/CasinoCoachScript.cs','utf8').split('"""')[1];
 await page.route('**/*',r=>r.abort());await page.setContent(src.slice(src.indexOf('<!doctype html>'),src.indexOf('<script>')));
 await page.evaluate(()=>{window.__requests=[];window.chrome=window.chrome||{};window.chrome.webview={addEventListener(){},postMessage:m=>__requests.push(JSON.parse(JSON.stringify(m)))};});
 await page.addScriptTag({content:src.slice(src.indexOf('<script>')+8,src.indexOf('</script>')).replace('/*CASINO_COACH*/',()=>coach)});
 await page.evaluate(()=>{S.profileId='main';renderSales({profileId:'main',inventoryUtc:'2026-11-20T12:00:00Z',rows:[{id:1,name:'One',amount:10,selected:0},{id:2,name:'Two',amount:20,selected:0}],minimumRating:0,maxTraders:3,onlineOnly:false});page('trading');});
 await page.locator('[data-sale-id="1"]').fill('5');await page.locator('[data-sale-id="2"]').fill('8');await page.locator('#saleCompare').click();
 const command=await page.evaluate(()=>__requests.find(x=>x.cmd==='saleCompare'));assert(command?.profileId==='main'&&command.pool.length===2&&command.pool[0].quantity===5&&command.rating===0&&command.maxTraders===3);
 await page.locator('#saleSearch').fill('One');assert(!(await page.locator('[data-sale-id="2"]').isVisible()));
 await page.evaluate(()=>renderSales({profileId:'main',inventoryUtc:'2026-11-20T12:00:00Z',rows:[{id:1,name:'One',amount:10},{id:2,name:'Two',amount:20}],single:{total:1000,traders:1,missing:[],lines:[{item:1,itemName:'One',quantity:5,trader:123,traderName:'Buyer',unitPrice:200,total:1000}]},split:{total:1100,traders:2,missing:[],lines:[]},extra:100}));
 assert((await page.locator('#saleDifference').innerText()).includes('100'));assert.equal(await page.locator('[data-sale-id="1"]').inputValue(),'5','Heartbeat erased draft pool');
 await page.getByRole('button',{name:'Open trade with this buyer'}).click();assert(await page.evaluate(()=>__requests.some(x=>x.cmd==='saleOpen'&&x.trader===123)));
 for(const width of [1100,850,600,390]){await page.setViewportSize({width,height:950});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+2),'Sale UI overflow '+width);}
 await page.evaluate(()=>{S.profileId='wife';renderSales({profileId:'wife',rows:[]});});assert.equal(await page.locator('[data-sale-id]').count(),0,'Other profile inventory leaked');assert(!errors.length,errors.join(';'));
 const paint=await browser.newPage();await paint.route('**/*',r=>r.fulfill({status:200,contentType:'text/html',body:'<!doctype html><title>Paint fixture</title><body>Graffiti</body>'}));
 await paint.goto('https://www.torn.com/page.php?sid=crimes#/graffiti');const helper=fs.readFileSync('Torn Commander/BundledScripts/Torn_Graffiti_Paint_Helper.user.js','utf8');await paint.addScriptTag({content:helper});
 assert(await paint.locator('#tc-graffiti-paint-helper').isVisible());await paint.locator('#tc-gph-hide').click();await paint.waitForTimeout(2100);assert(!(await paint.locator('#tc-graffiti-paint-helper').isVisible()),'Observer/timer restored hidden overlay');
 await paint.reload();await paint.addScriptTag({content:helper});assert.equal(await paint.locator('#tc-graffiti-paint-helper').count(),0,'Hidden overlay recreated after reload');
 await paint.evaluate(()=>__TornCommanderGraffiti.setOverlayVisible(true));assert(await paint.locator('#tc-graffiti-paint-helper').isVisible(),'Restore failed');
 await browser.close();console.log('INVENTORY / PAINT BROWSER PASSED: actual pool command, search, draft retention, plans and buyer navigation, account clearing, responsive layout; native hide persists through timer/reload and restores. Local fixtures only.');
})().catch(e=>{console.error(e);process.exit(1)});
