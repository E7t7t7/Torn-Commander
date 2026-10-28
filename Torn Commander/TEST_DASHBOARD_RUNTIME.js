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
context.renderCompany({id:7,layouts:{stage:'1-star Candle Shop Â· 9 scanned staff',selected:'Cash and staff growth',note:'Trial layout',options:[{id:'growth',name:'Cash and staff growth',suggested:true,countsText:'3 Chandler Â· 3 Salesperson',workingScore:300,underqualified:9,reason:'Training tradeoff'}],checks:['Compare a full daily report']},rows:[]},'one');
assert(nodes.coLayoutStage.textContent.includes('9 scanned')&&nodes.coLayouts.innerHTML.includes('data-company-layout="growth"')&&nodes.coLayoutSelected.textContent.includes('Cash and staff growth'),'Guided nine-person layout not rendered');
context.send=(cmd,data)=>{context.layoutMessage={cmd,...data}};
context.previewCompanyLayout('growth');
assert.strictEqual(context.layoutMessage.scenarioId,'growth');
context.renderCompany({id:7,team:{state:'CURRENT SETUP â€” REVIEW ONLY',rows:[]},layouts:{selected:'Old planner suggestion â€” rebuild before using',options:[]},rows:[]},'one');
assert(nodes.coTeamScore.textContent.includes('not an endorsement'),'Current setup was endorsed on scan');
assert(nodes.coLayoutSelected.textContent.includes('Old planner'),'Old plan invalidation not displayed');
context.renderCompany({id:7,team:{state:'STARTER DRAFT â€” REVIEW BEFORE APPLYING',rows:[{id:2,name:'Filler',current:'Chandler',recommended:'Salesperson',explanation:{summary:'Keep this person included in the general sales team'}}]},layouts:{options:[{id:'starter-v2',name:'Starter draft',suggested:false,countsText:'1 Cleaner',underqualified:9,workingScore:100}]},rows:[]},'one');
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

assert(context.renderWatchMarket(null).includes('No official market-price sample'),'Missing market history was invented');
const marketMarkup=context.renderWatchMarket({latest:{lowestReturnedAsk:null,returnedQuantity:0,observedUtc:new Date(Date.now()-3600000).toISOString()},points:[]});
assert(marketMarkup.includes('Unknown / no returned offers')&&marketMarkup.includes('stale sample'),'Empty or stale market report misleading');
context.renderItemIntelligence({catalog:[],cards:[],health:[],marketQuotes:true,diagnostics:[{dataset:'Market',source:'Official Torn',scope:'Current profile',detail:'Fixture'}]},'market-A');
assert(nodes.iiMarketQuotes.checked&&nodes.iiDatasetSources.innerHTML.includes('Official Torn'),'Market collection settings/diagnostics missing');
context.renderItemIntelligence({catalog:[],cards:[],health:[],marketQuotes:false,diagnostics:[]},'market-B');
assert(!nodes.iiMarketQuotes.checked&&!nodes.iiDatasetSources.innerHTML.includes('Official Torn'),'Market settings/diagnostics leaked across profiles');
console.log('MARKET HISTORY UI PASSED: unknown asks, staleness, opt-in collection and profile isolation.');

vm.runInContext(script.slice(script.indexOf('const medItems='),script.indexOf('function saveLossTiming(')),context);
nodes.medProfile.value='A';
const medProfiles=[{id:'A',name:'Account A',settings:{enabled:true,automatic:false,autoDetect:true,permissions:{66:true,67:true,68:true},reserves:{67:5}}},{id:'B',name:'Account B',settings:{enabled:true,automatic:true,autoDetect:false,manualBonusPercent:20,manualMaximumMinutes:540,permissions:{66:true}}}];
context.renderSmartMedical({profiles:medProfiles,sessions:[{slot:'A',profile:'A',note:'Fixture',cooldownSeconds:1200,maximumSeconds:21600,hospitalSeconds:1800,itemsUsed:2,cooldownUsed:1800,savedSeconds:-300,losses:2,inventory:{67:10},next:'First Aid Kit',plannedCooldownSeconds:900}]});
assert.equal(nodes.medMode.value,'recommend');assert(nodes.medItemSettings.innerHTML.includes('Small FAK')&&nodes.medItemSettings.innerHTML.includes('value="5"'),'Medical permissions/reserves missing');
assert(nodes.medicalSessions.innerHTML.includes('extra cost')&&nodes.medicalSessions.innerHTML.includes('Account A'),'Misleading savings or session identity');
nodes.medBonus.value='19';context.renderSmartMedical({profiles:medProfiles,sessions:[]});assert.equal(nodes.medBonus.value,'19','Background status erased medical draft');
nodes.medProfile.value='B';context.loadMedicalSettings();assert.equal(nodes.medBonus.value,20);assert.equal(nodes.medMode.value,'automatic');
assert(!nodes.medItemSettings.innerHTML.includes('Xanax'),'Drug permission present');
let medicalMessage;context.send=(type,data)=>{medicalMessage={type,data}};
for(const id of [66,67,68,739]){nodes['medAllow'+id]={checked:id===66};nodes['medKeep'+id]={value:'2'};nodes['medPrice'+id]={value:'0'};}
context.saveMedicalSettings();assert.equal(medicalMessage.data.profileId,'B');assert(!Object.keys(medicalMessage.data.settings.permissions).includes('206'),'Drug entered settings payload');
console.log('SMART MEDICAL DASHBOARD PASSED: explicit consent, profile-specific permissions, reserve drafts, account stats and honest extra-cost display.');

// Leveling functions are included by the medical/dashboard script slice above.
context.S={profileId:'A'};let levelingMessage;context.send=(cmd,data)=>levelingMessage={cmd,data};
const levelSession={energyReserve:25,lifePercent:100,attackLimit:5,wins:2,inventory:{68:5,67:3,66:2},targets:[{id:123,name:'Fixture',status:'Okay'}],pendingTarget:123,note:'Ready'};
context.renderLeveling({profileId:'A',stale:false,session:levelSession,account:{energy:150,life:100,maximum:100,at:new Date().toISOString(),recovery:{state:'Ready',reason:'Full life',items:[]}},estimatedAttacks:5});
assert.equal(nodes.levelLife.value,100);assert(!nodes.levelOpen.disabled);assert(nodes.levelRows.innerHTML.includes('Fixture'));
nodes.levelReserve.value=99;context.renderLeveling({profileId:'A',stale:true,session:levelSession});
assert.equal(nodes.levelReserve.value,99,'Leveling render erased draft');assert(nodes.levelOpen.disabled,'Stale check enabled attack');
context.renderLeveling({profileId:'B',stale:true,session:{energyReserve:0,lifePercent:50,inventory:{},targets:[]}});
assert.equal(nodes.levelReserve.value,0);assert.equal(nodes.levelLife.value,50);assert(!nodes.levelRows.innerHTML.includes('Fixture'),'Targets leaked across accounts');
context.S={profileId:'B'};nodes.levelTargets.value='123';nodes.levelLimit.value=0;context.saveLeveling();assert.equal(levelingMessage.cmd,'levelSave');assert.equal(levelingMessage.data.profileId,'B');assert.equal(levelingMessage.data.targets,'123');
console.log('LEVELING UI PASSED: full-life option, stale gating, draft retention, profile isolation and save payload.');

assert(ids.has('levelAutoHeal')&&ids.has('levelWeapon')&&ids.has('levelDelay'),'Automatic leveling controls missing');
nodes.levelAutoHeal.checked=true;nodes.levelWeapon.value='weapon_primary';nodes.levelDelay.value='1000';context.saveLeveling();assert(levelingMessage.data.autoHeal&&levelingMessage.data.weapon==='weapon_primary'&&levelingMessage.data.delay===1000,'Automatic permissions/weapon not saved');
nodes.levelBaldrGroup.value="Baldr's List 1";context.loadBaldrList();assert.equal(levelingMessage.cmd,'levelLoadBaldr');assert.equal(levelingMessage.data.group,"Baldr's List 1");assert.equal(levelingMessage.data.profileId,'B');
context.renderLeveling({profileId:'B',stale:true,session:{baldrLoadedUtc:'2026-10-23T12:00:00Z',targets:[{id:1,name:'Fixture',source:"Baldr's List 1",listedBattleStats:640}]}});
assert(nodes.levelBaldrSource.textContent.includes('OranWeb')&&nodes.levelRows.innerHTML.includes('historical'),'Baldr source or stats labeling missing');
console.log('AUTOMATIC LEVELING / BALDR UI PASSED: medical permission, equipped weapon, timing, direct list load, source and historical stats.');

context.S={profileId:'B'};
nodes.levelStatRatio.value='50';nodes.levelMaxFF.value='2';nodes.levelEstimateDays.value='30';nodes.levelInactive.checked=true;
context.saveScoutFilters();assert.equal(levelingMessage.cmd,'levelScoutSave');assert.equal(levelingMessage.data.ratio,0.5);assert(levelingMessage.data.inactive);
const filtersRequest=levelingMessage.data.requestId;
context.renderLeveling({profileId:'B',scouterFeedback:{requestId:filtersRequest,success:true,message:'Filters saved'},session:{maxStatRatio:0.5}});
nodes.levelScoutKey.value='TESTKEYONLY00000';context.saveScouterKey();assert.equal(nodes.levelScoutKey.value,'TESTKEYONLY00000','Key erased before save confirmation');assert.equal(levelingMessage.cmd,'levelScoutKey');assert.equal(levelingMessage.data.profileId,'B');
const failedKeyRequest=levelingMessage.data.requestId;
context.renderLeveling({profileId:'B',scouterFeedback:{requestId:failedKeyRequest,success:false,message:'Wait for current check'},session:{targets:[{id:999}]}});
assert.equal(nodes.levelScoutKey.value,'TESTKEYONLY00000','Failed save or background list update erased key');
assert(nodes.levelScoutStatus.textContent.includes('Wait for current check'));
context.saveScouterKey();const goodKeyRequest=levelingMessage.data.requestId;
context.renderLeveling({profileId:'B',scouterConfigured:true,scouterFeedback:{requestId:goodKeyRequest,success:true,message:'Key saved'},session:{openedUtc:'2026-10-23T20:13:00Z',pendingTarget:123}});
assert.equal(nodes.levelScoutKey.value,'','Successful save did not clear secret');assert(nodes.levelPendingFight.textContent.includes('Unfinished manual fight'));

const scoutingData={profileId:'B',scouterConfigured:true,session:{ownStats:300,ownStatsUtc:new Date().toISOString(),maxStatRatio:0.5,maxFairFight:2,maxEstimateDays:30,targets:[{id:5,name:'Weak',estimatedStats:100,estimateUtc:new Date().toISOString(),scannedUtc:new Date().toISOString()},{id:6,name:'TooStrong',estimatedStats:1000,estimateUtc:new Date().toISOString(),scannedUtc:new Date().toISOString()}]}};
context.renderLeveling(scoutingData);nodes.levelScoutView.value='within';context.renderLeveling(scoutingData);
assert(nodes.levelRows.innerHTML.includes('Weak')&&!nodes.levelRows.innerHTML.includes('TooStrong'),'Strength list filter failed');
assert(nodes.levelScoutSummary.textContent.includes('150'),'300-stat account cap missing');
nodes.levelStatRatio.value='25';context.renderLeveling({...scoutingData,session:{...scoutingData.session,targets:[...scoutingData.session.targets,{id:7,name:'Added'}]}});assert.equal(nodes.levelStatRatio.value,'25','Status erased scouting draft');
context.renderLeveling({profileId:'C',session:{targets:[]}});assert.equal(nodes.levelScoutView.value,'all');assert.equal(nodes.levelScoutKey.value,'');assert(!nodes.levelRows.innerHTML.includes('Weak'),'Scouting target leaked across accounts');
console.log('SCOUTING UI PASSED: account-specific filters, 300-stat cap, strength view, credential clearing and draft preservation.');

context.S={profileId:'P'};
context.renderLeveling({profileId:'P',session:{targets:Array.from({length:120},(_,i)=>({id:i+1,name:'PageTarget'+(i+1)}))}});
assert(nodes.levelPageSummary.textContent.includes('1-50 of 120'));assert(!nodes.levelRows.innerHTML.includes('PageTarget51</b>'));
context.changeLevelPage(1);assert(nodes.levelPageSummary.textContent.includes('51-100 of 120'));assert(nodes.levelRows.innerHTML.includes('PageTarget51'));
console.log('LEVELING PAGINATION PASSED: full queue retained and bounded visible target cards.');

nodes.levelBaldrGroup.value="Baldr's List 1";context.loadBaldrList(true);
assert.equal(levelingMessage.data.group,'');assert.equal(levelingMessage.data.scan,true);
context.renderLeveling({profileId:'P',busy:true,session:{targets:[]}});
for(const id of ['levelLoadScan','levelLoadList','levelScanList','levelFindTargets'])assert(nodes[id].disabled,'Concurrent scan control enabled');
context.renderLeveling({profileId:'P',busy:false,session:{targets:[]}});
assert(!nodes.levelLoadScan.disabled);
console.log('BALDR LOAD-AND-SCAN PASSED: all groups requested before filtering, busy controls and existing list-only action.');

context.renderLeveling({profileId:'P',session:{autoResumeMedical:true,targets:[]},medical:{itemsUsed:7,cooldownUsed:8400,currentCooldown:21600,maximumCooldown:32400,observed:new Date().toISOString()}});
assert(nodes.levelWorker.textContent.includes('Current medical cooldown 360.0m / 540m'),'Current cooldown/max missing');
assert(nodes.levelWorker.textContent.includes('Added by this session 140.0m'),'Session cost confused with current cooldown');
assert(nodes.levelAutoResumeMedical.checked);context.saveLeveling();assert.equal(levelingMessage.data.autoResumeMedical,true);
console.log('LEVELING MEDICAL STATUS PASSED: current balance versus session additions, capacity and auto-resume payload.');
