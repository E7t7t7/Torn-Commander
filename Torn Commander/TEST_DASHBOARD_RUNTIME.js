const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
const html=source.slice(source.indexOf('<!doctype html>'),source.indexOf('<script>'));
const script=source.slice(source.indexOf('<script>')+8,source.indexOf('</script>'));
const ids=new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(x=>x[1]));
for(const match of script.matchAll(/\$\('([^']+)'\)/g)) assert(match[1]=='mobilePageSelect'||ids.has(match[1]),'Missing markup ID: '+match[1]);
const nodes=Object.fromEntries([...ids].map(id=>[id,{id,value:'',textContent:'',innerHTML:'',hidden:true,style:{},dataset:{}}]));
const context={$:id=>nodes[id]??null,money:x=>String(x??''),esc:x=>String(x??''),document:{activeElement:null},console:{error:()=>{}}};
vm.createContext(context);
vm.runInContext(script.slice(script.indexOf("const chartRanges="),script.indexOf("let factionFormIdentity=")),context);
vm.runInContext(script.slice(script.indexOf("let factionFormIdentity="),script.indexOf("let coRecordIdentity=")),context);
vm.runInContext(script.slice(script.indexOf('let coRecordIdentity='),script.indexOf('let progressionIdentity=')),context);
context.renderCompany({name:'Fixture Company',business:{state:'SPENDING ALERT',note:'Fixture'},rows:[]},'one');
assert.strictEqual(nodes.coName.textContent,'Fixture Company');
assert.strictEqual(nodes.coBusinessState.textContent,'SPENDING ALERT');
assert.strictEqual(nodes.coCopyReport.disabled,true);
context.renderCompany({id:7,name:'Fixture Company',health:{efficiency:71,environment:83,popularity:42,trains:3},
 scan:{token:'one',at:'fixture UTC',warnings:['Restricted stock access'],jobs:[{id:1,name:'Manual',required:{manualLabor:100,intelligence:0,endurance:50},count:1}],stock:[{name:'Candle',inStock:0,onOrder:2,cost:10,price:9}]},
 team:{state:'TEAM PLAN READY',before:8,after:72,note:'Fixture plan',actions:['Train staff'],rows:[{name:'Worker',id:11,current:'Manual',recommended:'Office',stats:{manualLabor:10,intelligence:80,endurance:0},before:4,after:36,nextStep:'INT gap'}]},rows:[]},'one');
assert(nodes.coTeamRows.innerHTML.includes('Office')&&nodes.coTeamRows.innerHTML.includes('INT 80'),'Staff scan not rendered');
assert(nodes.coTeamScore.textContent.includes('72')&&nodes.coJobs.innerHTML.includes('data-company-role="1"'),'Allocation or role counts not rendered');
assert(nodes.coStockRows.innerHTML.includes('Candle')&&!nodes.coScanWarnings.hidden,'Stock details or scan warnings missing');

assert(nodes.coTeamRows.innerHTML.includes('<details')&&nodes.coTeamRows.innerHTML.includes('Why this position?'),'Expandable position reasoning missing');
const retainedRows=nodes.coTeamRows.innerHTML;
nodes.coTeamRows.innerHTML='DETAILS_REMAIN_OPEN';
context.renderCompany({id:7,team:{rows:[{name:'Worker',id:11,current:'Manual',recommended:'Office',stats:{manualLabor:10,intelligence:80,endurance:0},before:4,after:36,nextStep:'INT gap'}]},rows:[]},'one');
assert.strictEqual(nodes.coTeamRows.innerHTML,'DETAILS_REMAIN_OPEN','Identical staff refresh replaces open details');
nodes.coTeamRows.innerHTML=retainedRows;
context.renderCompany({id:7,layouts:{stage:'1-star Candle Shop · 9 scanned staff',selected:'Cash and staff growth',note:'Trial layout',options:[{id:'growth',name:'Cash and staff growth',suggested:true,countsText:'3 Chandler · 3 Salesperson',workingScore:300,underqualified:9,reason:'Training tradeoff'}],checks:['Compare a full daily report']},rows:[]},'one');
assert(nodes.coLayoutStage.textContent.includes('9 scanned')&&nodes.coLayouts.innerHTML.includes('data-company-layout="growth"')&&nodes.coLayoutSelected.textContent.includes('Cash and staff growth'),'Guided nine-person layout not rendered');
context.send=(cmd,data)=>{context.layoutMessage={cmd,...data}};
context.previewCompanyLayout('growth');
assert.strictEqual(context.layoutMessage.scenarioId,'growth');
context.renderCompany({id:7,team:{state:'CURRENT SETUP — REVIEW ONLY',rows:[]},layouts:{selected:'Old planner suggestion — rebuild before using',options:[]},rows:[]},'one');
assert(nodes.coTeamScore.textContent.includes('not an endorsement'),'Current setup was endorsed on scan');
assert(nodes.coLayoutSelected.textContent.includes('Old planner'),'Old plan invalidation not displayed');
context.renderCompany({id:7,team:{state:'STARTER DRAFT — REVIEW BEFORE APPLYING',rows:[{id:2,name:'Filler',current:'Chandler',recommended:'Salesperson',explanation:{summary:'Keep this person included in the general sales team'}}]},layouts:{options:[{id:'starter-v2',name:'Starter draft',suggested:false,countsText:'1 Cleaner',underqualified:9,workingScore:100}]},rows:[]},'one');
assert(nodes.coTeamRows.innerHTML.includes('Keep this person included'),'Plain-language filler explanation missing from simple view');
assert(!nodes.coLayouts.innerHTML.includes('Suggested starting trial')&&nodes.coLayouts.innerHTML.includes('Unvalidated draft'),'Draft presented as endorsed optimal plan');
context.renderCompany({id:999,name:'Other',rows:[]},'two');
assert(!nodes.coTeamRows.innerHTML.includes('Worker')&&!nodes.coJobs.innerHTML.includes('Manual')&&!nodes.coStockRows.innerHTML.includes('Candle'),'Previous profile company data retained in view');
assert(!nodes.coLayouts.innerHTML.includes('Cash and staff growth'),'Previous company guided layouts retained');
context.renderFaction({id:71,name:'Faction Fixture',role:'Leader',position:'Captain',abilitiesLoaded:true,abilities:['War Management'],membersLoaded:true,members:[{id:30,name:'Member Fixture',position:'Captain',isMe:true,status:'Okay',inCrime:true,idleDays:0}],crimesLoaded:true,crimes:[{id:1,name:'Crime Fixture',status:'Planning',slots:[{position:'Driver',name:'Member Fixture',userId:30,passRate:50,progress:25}]}],chain:{loaded:true,count:10,max:100,remaining:45},advice:['Leadership priority'],personal:['Your participation'],tasks:[]},'one');
assert.strictEqual(nodes.faLeadershipPanel.style.order,'-3','Leader priorities not first');
assert(nodes.faMembers.innerHTML.includes('Member Fixture')&&nodes.faCrimes.innerHTML.includes('Assigned member CPR'),'Faction roster/crime slots missing');
context.renderFaction({id:71,role:'Member',position:'Member',personal:['Your participation'],chain:{loaded:true,count:10,remaining:-1},crimesLoaded:true,crimes:[{id:2,name:'Open Crime',slots:[{name:'Open slot',passRate:80}]}]},'two');
assert.strictEqual(nodes.faPersonalPanel.style.order,'-3','Member priorities not first');
assert(nodes.faCrimes.innerHTML.includes('Your CPR for open slot')&&nodes.faChain.textContent.includes('deadline passed'),'Open-slot CPR or stale chain deadline misleading');
assert(!nodes.faMembers.innerHTML.includes('Member Fixture'),'Faction member data leaked between profile views');
context.renderFaction({},'three');
assert.strictEqual(nodes.faCopy.disabled,true);
assert(!nodes.faCrimes.innerHTML.includes('Open Crime')&&nodes.faCrimes.innerHTML.includes('unavailable'),'Restricted/missing faction data retained old crime');
vm.runInContext(script.slice(script.indexOf('function renderSection('),script.indexOf('function render(d)')),context);
context.renderSection('Fixture',()=>{throw Error('panel failure')});
assert.strictEqual(nodes.dashboardRenderWarning.hidden,false);
const render=script.slice(script.indexOf('function render(d)'),script.indexOf("if(window.chrome?.webview)",script.indexOf('function render(d)')));
assert(render.indexOf("$('profileSelect').replaceChildren")<render.indexOf("renderSection('Company'"),'Company renderer blocks profiles');
console.log('DASHBOARD RUNTIME PASSED: real markup IDs, company rendering, profile-first order and panel failure isolation.');

for(const node of Object.values(nodes)) node.replaceChildren=function(...children){this.options=children};
Object.assign(context,{S:{},casinoAdviceGeneration:0,Option:function(name,id){this.text=name;this.value=id},applyTheme:()=>{},renderApiActivity:()=>{},renderItinerary:()=>{},renderArrival:()=>{},setFilters:()=>{},renderTravelRows:()=>{},renderProgression:()=>{},renderScriptTools:()=>{},renderTrading:()=>{}});
vm.runInContext(script.slice(script.indexOf("let itemConfigIdentity="),script.indexOf("let lossEditorIdentity=")),context);
vm.runInContext(script.slice(script.indexOf("let lossEditorIdentity="),script.indexOf('function renderSection(')),context);
vm.runInContext(render,context);
const state={profileId:'one',profiles:[{id:'one',name:'Account A'},{id:'two',name:'Account B'}],buildPath:'Fixture.exe',company:{name:'Fixture Company',rows:[]}};
context.render(state);
assert.strictEqual(nodes.profileSelect.options.length,2);
assert.strictEqual(nodes.profileSelect.value,'one');
assert.strictEqual(nodes.actualBuildPath.textContent,'Fixture.exe');
assert(nodes.profilesSummary.textContent.startsWith('2 of'));
const realCompanyRenderer=context.renderCompany;
context.renderCompany=()=>{throw Error('broken optional panel')};
context.render(state);
assert.strictEqual(nodes.profileSelect.options.length,2);
assert.strictEqual(nodes.dashboardRenderWarning.hidden,false);
console.log('PROFILE STATE RENDER PASSED: both accounts load even when the company panel fails.');

context.renderLossStatus({workers:{a:'Paused',b:'Pillow selected'},loss:{buyer:'Fixture',target:'123',completed:1,requested:2,remaining:1,gross:100,status:'Recovering'}});
assert.strictEqual(nodes.workerB.textContent,'Pillow selected');assert(nodes.lossCard.innerHTML.includes('1 / 2'));

context.renderLossStatus({lossAttackDelayMs:700});
assert.strictEqual(nodes.lossAttackDelay.value,700);
nodes.lossAttackDelay.value=500;
context.renderLossStatus({lossAttackDelayMs:700});
assert.strictEqual(nodes.lossAttackDelay.value,500,'Worker heartbeat overwrote an unsaved timing edit');
context.renderLossStatus({lossAttackDelayMs:900});
assert.strictEqual(nodes.lossAttackDelay.value,900);
console.log('LOSS TIMING DASHBOARD PASSED: saved timing rendering and unsaved draft preservation.');

context.renderCompany=realCompanyRenderer;
context.renderCompany({market:{available:true,weeklyIncome:200,scannedUtc:'fixture',stale:true,rows:[{stars:2,count:3,beatenPercent:33.3,lowest:100,median:200,next:true}]},rows:[]},'one');
assert(nodes.coMarketRows.innerHTML.includes('33.3%') && nodes.coMarketRows.innerHTML.includes('next star'),'Market comparison missing');
assert(nodes.coMarketSummary.textContent.includes('OLD DATA'),'Stale market warning missing');
context.renderCompany({rows:[]},'two');
assert(nodes.coMarketRows.innerHTML==='','Market rows leaked between profiles');
console.log('COMPANY MARKET DASHBOARD PASSED: peer percentage, next-star marker, stale warning and profile clearing.');

context.renderStocks({report:{holdingsKnown:true,value:100,rows:[{id:1,name:'<Unsafe>',symbol:'FIX',price:10,shares:2,history:[],target:{days:7},signal:'Watching'}]}},'stock-one');
assert(nodes.stockRows.innerHTML.includes('FIX') && nodes.stockRows.innerHTML.includes('Net P/L'),'Stock card missing');
nodes.stockSearch.value='does-not-match';
context.renderStocks({report:{rows:[{id:1,name:'Fixture',symbol:'FIX'}]}},'stock-one');
assert(nodes.stockRows.innerHTML.includes('No matching stocks'),'Stock search failed');
context.renderStocks({report:{rows:[]}},'stock-two');
assert(nodes.stockSearch.value==='' && !nodes.stockRows.innerHTML.includes('FIX'),'Stock profile clearing failed');
context.renderFaction({activity:{enabled:true,hours:1,heatmap:[{day:4,hour:12,samples:2,activePercent:50}],members:[{name:'Fixture',activePercent:50,online:1,idle:0,offline:1}]}},'activity-one');
assert(nodes.faActivityEnabled.checked && nodes.faHeatmap.innerHTML.includes('50% active') && nodes.faActivityMembers.innerHTML.includes('Fixture'),'Activity view missing');
context.renderFaction({},'activity-two');
assert(!nodes.faActivityEnabled.checked && nodes.faActivityMembers.innerHTML==='','Activity profile clearing failed');
console.log('STOCK / ACTIVITY DASHBOARD PASSED: cards, filters, targets, heatmap and profile clearing.');

context.renderWar({warId:7,factionId:8,budget:100,attackPercent:70,minimumAttacks:2,cap:50,unallocated:10,rows:[{id:1,name:'Fixture fighter',attacks:5,score:2,amount:50,eligible:true}]},'war-one');
assert(nodes.warRows.innerHTML.includes('Fixture fighter')&&nodes.warSummary.textContent.includes('10'),'War preview missing');
nodes.warId.value='99';
context.renderWar({warId:7,rows:[]},'war-one');
assert(nodes.warId.value==='99','Typed war ID overwritten by refresh');
context.renderWar({},'war-two');
assert(nodes.warRows.innerHTML===''&&nodes.warId.value==='','War profile isolation failed');
console.log('WAR PAYOUT DASHBOARD PASSED: preview, typed report ID and profile isolation.');

const chart=context.insightChart([{day:'2026-10-13',income:100},{day:'2026-10-14',income:null},{day:'2026-10-15',income:200}],'day',[{key:'income',label:'Income',color:'#60c7fa'}],true);
assert(chart.includes('<svg')&&chart.includes('Income')&&chart.includes('M60.0,195.0M830.0,25.0'),'Chart must preserve gaps rather than drawing through missing data');
assert(context.insightChart([], 'day', []).includes('first scan'),'Missing history should have an honest empty state');
vm.runInContext(script.slice(script.indexOf('function rehabLegHtml('),script.indexOf('function renderItinerary(')),context);
assert(context.rehabLegHtml({departure:'A',arrival:'B',back:'C'}).includes('Treatment cost varies'),'Rehab leg must not claim a purchase/profit');
console.log('INSIGHT CHARTS PASSED: real points, missing-data gaps and honest rehab rendering.');

context.renderAdTrial({id:1,adTrial:{active:true,state:'GROWTH AT AN EXTRA COST',budget:700000,allowedDailyLoss:300000,baselineDays:3,testDays:3,extraSales:73000,afterAdsChange:-227000,note:'Fixture',disclaimer:'Observed snapshots',overAllowance:true}});
assert(nodes.adTrialReport.innerHTML.includes('GROWTH AT AN EXTRA COST')&&nodes.adTrialStart.disabled,'Growth trial not rendered or start can overwrite it');
context.renderAdTrial({id:2});
assert(!nodes.adTrialReport.innerHTML.includes('Fixture')&&nodes.adTrialStop.disabled,'Advertising trial leaked between companies');
nodes.completedItem.value='Private fixture';nodes.completedBuy.value='123';
context.renderTradeResults({unknownFees:2},'another-profile');
assert(nodes.completedItem.value===''&&nodes.completedBuy.value===''&&nodes.tradeFeeCoverage.textContent.includes('2'),'Trade draft/fee coverage profile isolation failed');
console.log('MONEY TOOLS DASHBOARD PASSED: advertising trial, subsidy outcome and completed-sale form isolation.');

vm.runInContext(script.slice(script.indexOf('let tradeFormIdentity='),script.indexOf('function renderScriptTools(')),context);
nodes.tradeAdviceRows.querySelectorAll=()=>[];
context.renderTrading({settings:'risk-fixture',rows:[{itemId:1,name:'Fixture',decision:'Candidate',observedUtc:'2026-10-16T00:00:00Z',riskAnalyzed:true,stressNet:1200,breakEvenSell:85,exposurePercent:16,reason:'Fixture reasoning'}]},'risk-profile');
assert(nodes.tradeAdviceRows.innerHTML.includes('Stress-case net')&&nodes.tradeAdviceRows.innerHTML.includes('Break-even resale')&&nodes.tradeAdviceRows.innerHTML.includes('16.0%'),'Computed risk figures are not visible in trade cards');
console.log('TRADING RISK DISPLAY PASSED: downside net, break-even and capital exposure.');

context.renderLossStatus({lossAttackDelayMs:1000});
context.renderLossStatus({workers:{a:'Fighting'}});
assert.strictEqual(nodes.lossAttackDelay.value,1000,'Partial worker status reset the saved timing');

context.renderLossOrders({profileId:'one',profiles:[{id:'one',name:'A'},{id:'two',name:'B'}],lossOrders:[{id:'order-one',buyer:'Buyer',completed:2,requested:20,status:'Active'}],loss:{id:'order-one'}});
assert.equal(nodes.lossAccountB.options[0].value,'');assert.equal(nodes.lossOrderSelect.value,'order-one');
nodes.lossTarget.value='12345';context.renderLossOrders({profileId:'one',profiles:[{id:'one',name:'A'},{id:'two',name:'B'}],lossOrders:[],loss:null});
assert.equal(nodes.lossTarget.value,'12345','Background updates erased the new-order draft');
console.log('UNIFIED LOSS DESK PASSED: optional solo account, selected order and draft preservation.');

context.renderItemIntelligence({status:'Fixture',catalog:[{id:361,name:'Neumune Tablet',location:'Switzerland'}],cards:[{watch:{itemId:361,name:'Neumune Tablet',location:'Switzerland'},providers:[]}],health:[],count:0},'one');
assert(nodes.iiCards.innerHTML.includes('No timestamped stock reports'),'Invented item availability in empty state');
nodes.iiClientKey.value='fixture-not-a-key';context.renderItemIntelligence({status:'Fixture',catalog:[],cards:[],health:[]},'two');assert.equal(nodes.iiClientKey.value,'','Credential input crossed profiles');

context.renderCompanyHistory({selected:7,liveCompany:7,companies:[{id:7,name:'Company A'}],staff:[{id:1,name:'Worker',position:'Sales',manualDelta:100,intelligenceDelta:null,enduranceDelta:20,wage:0}],cohorts:[],heatmap:[],baseline:{revenue:1000,net:null,note:'Fixture'}},null,'one');
assert(nodes.coScenarioResult.innerHTML.includes('Unknown'),'Unknown scenario costs became a false profit');assert(nodes.coEmployeeHistory.innerHTML.includes('100'),'Employee delta missing');
nodes.coScenarioAds.value='123';context.renderCompanyHistory({selected:8,liveCompany:8,companies:[{id:8,name:'Company B'}],staff:[],cohorts:[],heatmap:[]},null,'two');assert.equal(nodes.coScenarioAds.value,'','Scenario input crossed companies/profiles');
context.renderTravelWait({waitAdvice:{location:'Hawaii',at:'2026-10-20T10:00:00Z',options:[{kind:'Buy available alternative',item:'Fixture',waitMinutes:0,futureMinutes:20,conditionalProfit:1000,conditionalProfitPerHour:3000,source:'Fixture',evidence:'Verify shelf'}],note:'Scenario'}});
assert(nodes.travelWaitAdvice.innerHTML.includes('Verify shelf')&&nodes.travelWaitAdvice.innerHTML.includes('3000'),'Wait alternatives missing');
