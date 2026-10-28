// Offline full-cycle fixture. No real browser, API, account, fights or medicine.
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
function fixture(ambiguous=false,missingMedical=false,missingJoin=false,options={}){
 let energy=options.energy??Infinity;
 let clock=1700000000000,interval,h,scene='home',hospital=Boolean(options.initialHospital),stock=options.lastMedicalItem||options.switchMedicalAfterFirst?1:20,remaining=2,expanded=false,fightHits=0,fightJoinAt=0,attackPageAt=0;
 let levelChoiceDone=false,apiWaited=false,depleted=false;let serverRecovered=false;let targetWaits=0;let expandAt=0;let hospitalRace=0,forcedHeal=false;let ownerGlitchAt=0;let activeLease='',medicalToken='',cooldownWaits=0,medicalPlans=0,levelPending=0,medicalPending=0;
 const messages=[],replies=[],counts={pillow:0,join:0,medicine:0,wrong:0,leave:0,levelCommits:0,scroll:0,expand:0,pillowTimes:[],joinTimes:[],medicalVerifyTimes:[],medicalPlanModes:[]},local=new Map(),session=new Map();
 class Node{
  constructor(tag,text='',attrs={},children=[]){this.tagName=tag.toUpperCase();this.text=text;this.attrs=attrs;this.className=attrs.class||'';this.children=children;for(const c of children)c.parentElement=this;}
  get textContent(){return [this.text,...this.children.map(c=>c.textContent)].join(' ').trim()}
  get innerText(){return this.textContent}
  getAttribute(k){return this.attrs[k]??null}
  getClientRects(){return [{}]}
  contains(n){return this===n||this.children.some(c=>c.contains(n))}
  scrollIntoView(){counts.scroll++}
  click(){this.onclick?.()}
  querySelectorAll(selector){
   const patterns=selector.split(',').map(s=>s.trim());
   const matches=(el,s)=>{
    const tag=s.match(/^[a-z][a-z0-9]*/i)?.[0];
    if(tag&&tag.toUpperCase()!==el.tagName)return false;
    for(const a of s.matchAll(/\[([\w-]+)(?:(\*=|=)["']?([^"'\]]*)["']?)?\]/g)){
     const v=el.getAttribute(a[1]);if(v===null)return false;
     if(a[2]==='='&&v!==a[3])return false;if(a[2]==='*='&&!v.includes(a[3]))return false;
    }
    return true;
   };
   const out=[];const walk=node=>{for(const child of node.children){if(patterns.some(s=>matches(child,s)))out.push(child);walk(child)}};walk(this);return out;
  }
 }
 const node=(t,text='',a={},c=[])=>new Node(t,text,a,c);
 const button=(text,fn,a={})=>{const n=node('button',text,a);n.onclick=fn;return n};
 function buildBody(){
  if(options.levelChoice&&!levelChoiceDone)return node('body','',{},[node('div','Level up',{role:'dialog'},[
   button('Hold level',()=>{assert(!options.acceptLevelUps);levelChoiceDone=true;}),
   button('Level up',()=>{assert(options.acceptLevelUps);levelChoiceDone=true;})
  ])]);

  if(options.medicalServerError&&!serverRecovered&&scene==='items')return node('body','Torn server error');
  if(scene==='blocked')return node('body','You cannot attack while in hospital.');
  if(scene==='items'&&options.itemsTabDelayMs&&clock-attackPageAt<options.itemsTabDelayMs)return node('body','Items loading');
  if(scene==='fight'){
   const pillow=button('',()=>{counts.pillow++;counts.pillowTimes.push(clock);if(++fightHits>=(options.pillowHits||1)){hospital=true;scene=options.leveling?(options.continueEnded?'ended':'victory'):'loss'}}, {},);
   Object.defineProperty(pillow,'disabled',{get:()=>clock-fightJoinAt<(options.pillowDisabledMs||0)});
   pillow.children=[node('img','',{src:'/images/items/440/large.png',alt:'Equipped weapon',title:'Pillow'}),node('span','Pillow')];for(const c of pillow.children)c.parentElement=pillow;
   let weapon=pillow;
   if(options.replacedControls&&fightHits>0){
    const action=node('div','',{'role':'button','aria-label':'Attack with Pillow',id:'weapon_melee'},[node('span','Turn artwork')]);
    action.onclick=pillow.onclick;action.attrs['aria-disabled']=clock-(counts.pillowTimes.at(-1)||0)<1500?'true':'false';weapon=action;
   }
   if(options.nestedMelee){
    const art=node('button','Weapon information');art.onclick=()=>counts.wrong++;
    const label=node('span','',{'aria-label':'Attack with Pillow'});
    const slot=node('div','',{id:'weapon_melee',role:'button'},[art,label]);
    slot.onclick=pillow.onclick;
    slot.attrs['aria-disabled']=clock-(counts.pillowTimes.at(-1)||fightJoinAt)<(options.turnWait||0)?'true':'false';
    weapon=slot;
   }
   if(options.leveling){weapon=node('div','',{id:options.renderedWeaponSlot||'weapon_primary',role:'button','aria-label':options.pillowLevelWeapon?'Attack with Pillow':'Attack with Rifle'},[node('span',options.noAmmo?'No ammo':options.stuckReload?'Reload':'Equipped weapon')]);weapon.onclick=options.stuckReload?()=>{counts.pillow++;}:pillow.onclick;}
   const area=options.sharedPlayerClass?node('div','',{class:'playerArea___wkSjF'},[weapon]):node('div','',{class:'playerArea_outer'},[node('div','',{class:'playerArea_inner'},[weapon])]);
   const defender=node('div','',{class:options.sharedPlayerClass?'playerArea___wkSjF':'defenderArea'},[options.sharedPlayerClass?button('This weapon is not available',()=>counts.wrong++,{id:'weapon_melee'}):button('Pillow',()=>counts.wrong++)]);
   if(options.duplicateMelee){const other=node('div','',{id:'weapon_melee',role:'button','aria-label':'Attack with Pillow'});other.onclick=()=>counts.wrong++;defender.children.push(other);other.parentElement=defender;}
   if(options.leveling){defender.children=[button('This weapon is not available',()=>counts.wrong++,{id:options.renderedWeaponSlot||'weapon_primary'})];for(const x of defender.children)x.parentElement=defender;}
   const roots=options.defenderFirst?[defender,area]:[area,defender];if(ambiguous)roots.push(node('div','',{class:'playerArea_second'},[button('Pillow',()=>counts.wrong++)]));
   return node('body','Combat',{},roots);
  }
  if(scene==='ended'){
   const weapon=node('div','',{id:'weapon_main',role:'button','aria-label':'Attack with Rifle'},[node('span','No ammo')]);weapon.onclick=()=>counts.wrong++;
   const cont=button('Continue',()=>counts.wrong++);
   const area=node('div','',{class:'playerArea___wkSjF'},[weapon,cont]);
   return node('body','Someone else finished this target.',{},[area]);
  }
  if(scene==='victory')return node('body',options.noVictoryText?'Choose an outcome':'Seller defeated Target.',{},[button('Leave',()=>{counts.leave++;scene='finished'}),button('Mug',()=>counts.wrong++)]);
  if(scene==='finished')return node('body','You left Target on the street.');
  if(scene==='loss')return node('body','Seller lost to Target.');
  if(scene==='attack'&&options.missionOnly)return node('body','A MISSION IS NEEDED TO BE ACTIVATED IN ORDER TO ATTACK THIS NPC!');
  if(scene==='attack'&&missingJoin)return node('body','Waiting for combat controls');
  if(scene==='attack'&&!hospital&&energy<25)return node('body','YOU DO NOT HAVE ENOUGH ENERGY');
  if(scene==='attack'){
   if(hospital)return node('body','You cannot attack while in hospital.');
   if(clock-attackPageAt<(options.joinDelayMs||0))return node('body','Loading combatants...');
   let join=button(options.startFightLabel||'Join fight',()=>{counts.join++;if(options.hospitalAfterJoin&&hospitalRace++===0){hospital=true;scene='blocked';return;}energy-=25;fightHits=0;fightJoinAt=clock;counts.joinTimes.push(clock);scene='fight'});
   if(options.nestedJoin)join=node('div','',{role:'button'},[join]);
   const controls=[join];if(options.ambiguousJoin)controls.push(button('Start fight',()=>counts.wrong++));
   return node('body','',{},controls);
  }
  const medical=node('a','Medical');medical.onclick=()=>scene='medical';
  if(scene==='items')return node('body','Your items',{},missingMedical?[]:[node('li','',{},[medical])]);
  if(scene==='medical'||scene==='confirm'){
   const chosenName=({66:'Morphine',67:'First Aid Kit',68:'Small First Aid Kit',739:'Blood Bag : O-'})[options.selectedMedicalId||67];
   if(expandAt&&clock>=expandAt)expanded=true;
   const name=node('span',chosenName);name.onclick=()=>{counts.expand++;if(!options.expandFails){if(options.expandDelayMs)expandAt=clock+options.expandDelayMs;else expanded=true;}};
   let heading=name;if(options.headerOnly){name.onclick=()=>{};heading=node('div','',{role:'tab',id:'ui-accordion-6-header-0'},[name]);heading.onclick=()=>{counts.expand++;if(options.expandDelayMs)expandAt=clock+options.expandDelayMs;else expanded=true;};}
   const controls=[heading];if(!options.collapsed||expanded)controls.push(button('Use',()=>scene='confirm'));
   if((options.collapsed||options.staleMedicalQuantity||options.lastMedicalItem)&&(!options.lastMedicalItem||stock>0))controls.unshift(node('span','x'+stock));
   const row=node('li','',options.collapsed?{role:'tablist',class:'first-in-row m-first-in-row t-first-in-row last-row'}:{'data-quantity':String(options.lastMedicalItem?1:options.staleMedicalQuantity?20:stock)},controls);
   const wrong=node('li','',{'data-item-id':'68'},[node('span',options.selectedMedicalId===68?'First Aid Kit':'Small First Aid Kit'),button('Use',()=>counts.wrong++)]);
   if(options.lastMedicalItem&&stock===0){row.className+=' remove-act';row.children.push(node('span','You use your '+chosenName+' on your wounded body.'));}
   const children=[row,wrong];if(options.ambiguousKit)children.push(node('li','',{'data-item-id':'67'},[node('span','First Aid Kit')]));
   if(scene==='confirm'){
    const consume=()=>{if(options.failedMedicalUse){scene='medical';return;}stock--;counts.medicine++;if(options.identityGlitchAfterUse&&counts.medicine===1)ownerGlitchAt=clock;hospital=counts.medicine%(options.kitsRequired||1)!==0;scene='medical'};
    if(options.inlineMedicalConfirmation){
     const prompt=node('span','Would you like to use the '+(options.wrongMedicalPrompt?'Morphine':chosenName)+'?',{id:'wai-action-desc',role:'status'});
     const confirm=node('a','',{'aria-labelledby':'wai-action-desc',href:'#'});confirm.onclick=consume;
     const cancel=node('a','Cancel',{href:'#'});cancel.onclick=()=>counts.wrong++;
     row.children.push(prompt,confirm,cancel);for(const x of [prompt,confirm,cancel])x.parentElement=row;
    }else children.push(node('div','Use '+chosenName+'?',{role:'dialog'},[button('Okay',consume)]));
   }
   return node('body',options.medicalHeading||'Your items - Medical',{},children);
  }
  return node('body','Torn');
 }
 let lastKey='',cached;
 function body(){const key=scene+stock+expanded+serverRecovered+levelChoiceDone+options.selectedMedicalId+(options.itemsTabDelayMs?':'+clock:'')+(options.expandDelayMs?':'+clock:'')+(options.joinDelayMs?':'+clock:'')+((options.replacedControls||options.nestedMelee)?fightHits+':'+clock:'');if(key!==lastKey){lastKey=key;cached=buildBody()}return cached;}
 const owner=node('a',options.decoratedOwner?'2BDecided [wife]':'Seller',{...(options.ariaMedicalOwner?{'aria-label':'Name: Seller'}:{}),href:'/profiles.php?XID='+String(options.wrongMedicalAccount?999:321)});
 const ownerLabel=node('span','Name:');const ownerRow=node('div','',{},[ownerLabel,owner]);if(options.medicalOwnerHidden){ownerLabel.getClientRects=()=>[];owner.getClientRects=()=>[];}const stranger=node('a','Unrelated chat user',{href:'/profiles.php?XID='+String(options.sidebarNpc?102:777)});
 const secondStranger=node('a','Other chat avatar',{href:'/profiles.php?XID=888'});
 if(options.chatSidebar){node('button','',{id:'chat_panel_button:777'},[stranger]);node('button','',{id:'chat_panel_button:888'},[secondStranger]);}
 const identityGlitch=()=>options.identityGlitchAfterUse&&ownerGlitchAt>0&&clock-ownerGlitchAt<2500;
 const pageAccount=node('script','',{uid:String(options.wrongPageAccount?999:321),name:'Seller'});const otherPageAccount=node('script','',{uid:'999',name:'Other'});
 const document={get title(){return options.medicalServerError&&!serverRecovered&&scene==='items'?'TORN - 500 Error':'Torn'},get body(){return body()},querySelectorAll:s=>{
  if(s==='script[uid][name]')return options.pageAccount?options.conflictingPageAccount?[pageAccount,otherPageAccount]:[pageAccount]:[];
  if(s.startsWith('[id="sidebar"] a'))return identityGlitch()||options.missingMedicalAccount||options.noSidebarWrapper||clock-1700000000000<(options.identityDelayMs||0)?[]:options.chatSidebar?[stranger,secondStranger,owner]:options.sidebarOtherIds?[owner,stranger]:[owner];
  if(s==='a[href*="profiles.php"]')return identityGlitch()||options.missingMedicalAccount||clock-1700000000000<(options.identityDelayMs||0)?[]:options.chatSidebar?[stranger,secondStranger,owner]:[owner];
  const found=body().querySelectorAll(s);if(s.startsWith('span,strong,b,div,dt,td')&&options.labeledMedicalOwner&&clock-1700000000000>=(options.identityDelayMs||0))found.push(ownerLabel);return found;
 }};
 const location={href:'https://www.torn.com/index.php',pathname:'/index.php',assign(url){const u=new URL(url);if(options.medicalServerError&&!options.permanentServerError&&scene==='items'&&u.pathname==='/item.php')serverRecovered=true;this.href=url;this.pathname=u.pathname;scene=u.pathname==='/item.php'?'items':'attack';attackPageAt=clock;expanded=false}};
 class Clock extends Date{static now(){return clock}}
 const store=m=>({getItem:k=>m.get(k)??null,setItem:(k,v)=>m.set(k,v),removeItem:k=>m.delete(k)});
 const window={chrome:{webview:{postMessage:raw=>{
  const m=JSON.parse(raw);messages.push(m);
  if(m.type==='tc-level-skip'){assert.equal(m.claimId,activeLease);activeLease='';remaining--;return;}
  if(m.type==='tc-level-claim'){
   if(options.apiWaitOnce&&!apiWaited){apiWaited=true;replies.push(()=>h.onLevelReply({kind:'api-wait',requestId:m.requestId,waitUntil:new Date(clock+5000).toISOString(),message:'Torn rate limit wait'}));return;}

   if(options.levelHealAfterWinOnce&&counts.levelCommits===1&&!forcedHeal){forcedHeal=true;clock+=60000;hospital=true;replies.push(()=>h.onLevelReply({kind:'heal',requestId:m.requestId}));return;}
   if(options.levelWaitOnce&&targetWaits++===0){replies.push(()=>h.onLevelReply({kind:'wait',requestId:m.requestId,waitUntil:new Clock(clock+5000).toISOString(),message:'Waiting for hospital release'}));return;}
   if(options.levelProgress)replies.push(()=>{assert.equal(h.onLevelReply({kind:'scan',requestId:'wrong',message:'wrong'}),false);assert.equal(h.onLevelReply({kind:'scan',requestId:m.requestId,message:'Checked target: Hospital'}),true);assert.equal(h.getState().stage,'claim-wait');});
   if(!remaining){replies.push(()=>h.onLevelReply({kind:'claim',requestId:m.requestId,ok:false,message:'Complete: no targets remain.'}));return;}
   activeLease='level-'+remaining;replies.push(()=>h.onLevelReply({kind:'claim',requestId:m.requestId,ok:true,claimId:activeLease,targetId:remaining===2?123:456,message:'Verified leveling target'}));return;
  }
  if(m.type==='tc-level-commit'){
   if(options.apiCommitWaitOnce&&!apiWaited){apiWaited=true;replies.push(()=>h.onLevelReply({kind:'api-wait',requestId:m.requestId,claimId:m.claimId,waitUntil:new Date(clock+5000).toISOString(),message:'Connection retry'}));return;}
   assert.equal(m.claimId,activeLease);
   if(options.levelPendingOnce&&levelPending++===0){replies.push(()=>h.onLevelReply({kind:'pending',claimId:m.claimId,message:'API cache pending'}));return;}
   counts.levelCommits++;remaining--;activeLease='';replies.push(()=>h.onLevelReply({kind:'commit',claimId:m.claimId,remaining,message:'Verified Leave',stop:false}));return;
  }
  if(m.type.startsWith('tc-medical-')){
   assert.equal(m.medicalSession,'session-'+(options.slot||'A'));assert.equal(m.browserPlayerId,321);
   assert(m.inventory.every(x=>[66,67,68,739].includes(x.id)),'Non-medical inventory/action');
   if(m.type==='tc-medical-plan'){
    if(options.depleteBeforeExecute&&depleted&&medicalPlans===1){assert.equal(m.cancelBeforeDispatch,true);assert.equal(m.medicalDispatched,false);assert.equal(m.operationToken,medicalToken);}
    medicalPlans++;counts.medicalPlanModes.push(m.continueVerifiedMedical===true);
    if(options.planApiDelayMs)clock+=options.planApiDelayMs;
    if(options.medicalCooldownOnce&&hospital&&cooldownWaits++===0){replies.push(()=>h.onMedical(m.requestId,{state:'Cooldown',reason:'Fixture cooldown',waitUntil:new Date(clock+5000).toISOString(),autoResume:options.medicalAutoResume===true}));return;}
    medicalToken='medical-'+medicalPlans;
    replies.push(()=>h.onMedical(m.requestId,!hospital?{state:'Recovered'}:{state:'Ready',itemId:options.badMedicalItem?206:(options.selectedMedicalId||67),name:options.badMedicalItem?'Xanax':({66:'Morphine',67:'First Aid Kit',68:'Small First Aid Kit',739:'Blood Bag : O-'})[options.selectedMedicalId||67],quantity:stock,token:medicalToken,expires:new Date(clock+40000).toISOString(),automatic:!options.medicalRecommend}));
   }
   if(m.type==='tc-medical-authorize'){assert.equal(m.token,medicalToken);if(options.alwaysMedicalReplan||(options.replanMedicalOnce&&medicalPlans===1)){replies.push(()=>h.onMedical(m.requestId,{state:'Replan',reason:'Fixture changed before dispatch'}));return;}replies.push(()=>{
    h.onMedical(m.requestId,{state:'Authorized',itemId:options.selectedMedicalId||67,token:medicalToken,expires:new Date(clock+30000).toISOString()});
    if(options.depleteBeforeExecute&&!depleted){depleted=true;options.selectedMedicalId=68;stock=20;}
   });}
   if(m.type==='tc-medical-verify')counts.medicalVerifyTimes.push(clock);
   if(m.type==='tc-medical-verify'&&options.levelMedicalPending&&medicalPending++===0){replies.push(()=>h.onMedical(m.requestId,{state:'PendingVerification',token:medicalToken,retryAfterMs:options.medicalRetryMs,reason:'API life snapshot pending'}));return;}
   if(m.type==='tc-medical-verify'){assert.equal(m.token,medicalToken);assert.equal(h.onMedical('stale-reply',{state:'Verified',token:medicalToken}),false);replies.push(()=>{
    h.onMedical(m.requestId,options.failMedicalVerification?{state:'Paused',reason:'Fixture failed verification'}:{state:'Verified',token:medicalToken});
    if(options.switchMedicalAfterFirst&&counts.medicine===1){assert.equal(stock,0,'Final morphine not used');options.selectedMedicalId=67;stock=20;}
   });}
   return;
  }
  if(options.coordinator&&m.type==='tc-loss-abandon'){assert.equal(options.coordinator.owner,options.slot);options.coordinator.owner='';return;}
  if(options.coordinator&&m.type==='tc-loss-claim'){replies.push(()=>options.coordinator.claim(options.slot,h,m));return;}
  if(options.coordinator&&m.type==='tc-loss-commit'){replies.push(()=>options.coordinator.commit(options.slot,h,m));return;}
  if(m.type==='tc-loss-claim')replies.push(()=>{assert(!activeLease,'Worker requested another slot while its hospital-recovery reservation was still held');activeLease='lease-'+remaining;h.onClaim(m.requestId,true,activeLease,'Granted')});
  if(m.type==='tc-loss-commit')replies.push(()=>{assert.equal(m.claimId,activeLease);activeLease='';h.onCommit(m.claimId,true,--remaining,'Saved')});
 }}}};
 const context={window,document,location,URL,Date:Clock,Math,console,localStorage:store(local),sessionStorage:store(session),getComputedStyle:()=>({visibility:'visible',display:'block'}),setInterval:fn=>{interval=fn},setTimeout:()=>{}};
 vm.createContext(context);vm.runInContext(source,context);h=window.TornCommanderLossCycle;
 h.start({acceptLevelUps:!!options.acceptLevelUps,mode:options.leveling?'level':'loss',weaponSlot:options.configuredWeaponSlot||'weapon_primary',profileId:options.slot||'one',slot:options.slot||'A',orderId:'fixture',targetId:'123',selfName:'Seller',smartMedical:!!options.smartMedical,playerId:321,medicalSession:'session-'+(options.slot||'A'),maxKits:4,attackDelayMs:options.delay??1900});
 return {h,counts,messages,async step(milliseconds=1000){h.pump();while(replies.length)replies.shift()();clock+=milliseconds;await Promise.resolve()}};
}
(async()=>{
 const coordinator={remaining:4,owner:'',receipt:'',commits:0,waits:0,
 claim(slot,h,m){if(this.owner){this.waits++;h.onClaim(m.requestId,false,'','Target busy');return;}if(!this.remaining){h.onClaim(m.requestId,false,'','No remaining losses');return;}this.owner=slot;this.receipt='shared-'+this.remaining;h.onClaim(m.requestId,true,this.receipt,'Granted');},
 commit(slot,h,m){assert.equal(slot,this.owner);assert.equal(m.claimId,this.receipt);this.owner='';this.commits++;h.onCommit(m.claimId,true,--this.remaining,'Saved');}};
 const dualA=fixture(false,false,false,{coordinator,slot:'A',nestedMelee:true,sharedPlayerClass:true,defenderFirst:true,pillowHits:4,turnWait:1500,initialHospital:true,kitsRequired:2,collapsed:true,headerOnly:true});
 const dualB=fixture(false,false,false,{coordinator,slot:'B',nestedMelee:true,sharedPlayerClass:true,pillowHits:3,turnWait:2000,initialHospital:true,kitsRequired:2,collapsed:true,headerOnly:true});
 for(let i=0;i<1500&&(dualA.h.getState().stage!=='complete'||dualB.h.getState().stage!=='complete');i++){await dualA.step(250);await dualB.step(250);}
 assert.equal(dualA.h.getState().stage,'complete',dualA.h.getState().status);assert.equal(dualB.h.getState().stage,'complete',dualB.h.getState().status);
 assert.equal(coordinator.commits,4);assert.equal(coordinator.owner,'');assert(coordinator.waits>0);assert.equal(dualA.counts.join+dualB.counts.join,4);
 coordinator.remaining=4;coordinator.owner='';coordinator.commits=0;
 const low=fixture(false,false,false,{coordinator,slot:'A',energy:25});
 const high=fixture(false,false,false,{coordinator,slot:'B',energy:75});
 for(let i=0;i<1500&&high.h.getState().stage!=='complete';i++){await low.step(250);await high.step(250);}
 assert.equal(low.h.getState().stage,'paused',low.h.getState().status);assert(low.h.getState().status.includes('Not enough energy'));
 assert.equal(high.h.getState().stage,'complete',high.h.getState().status);assert.equal(low.counts.join,1);assert.equal(high.counts.join,3);assert.equal(coordinator.commits,4);assert.equal(coordinator.owner,'');
 coordinator.remaining=4;coordinator.owner='';coordinator.commits=0;
 const empty=fixture(false,false,false,{coordinator,slot:'A',energy:0});
 const full=fixture(false,false,false,{coordinator,slot:'B',energy:100});
 for(let i=0;i<1500&&full.h.getState().stage!=='complete';i++){await empty.step(250);await full.step(250);}
 assert.equal(empty.h.getState().stage,'paused');assert.equal(empty.counts.join,0);assert.equal(full.h.getState().stage,'complete',full.h.getState().status);assert.equal(coordinator.commits,4);assert.equal(coordinator.owner,'');
 coordinator.remaining=4;coordinator.owner='';coordinator.commits=0;
 const exhaustedA=fixture(false,false,false,{coordinator,slot:'A',energy:0}),exhaustedB=fixture(false,false,false,{coordinator,slot:'B',energy:0});
 for(let i=0;i<50;i++){await exhaustedA.step(250);await exhaustedB.step(250);}
 assert.equal(exhaustedA.h.getState().stage,'paused');assert.equal(exhaustedB.h.getState().stage,'paused');assert.equal(coordinator.remaining,4);assert.equal(coordinator.commits,0);assert.equal(coordinator.owner,'');
 for(const layout of [{joinDelayMs:24000,startFightLabel:'Start fight'},{joinDelayMs:24000,startFightLabel:'START FIGHT',nestedJoin:true}]){
  const recovered=fixture(false,false,false,{smartMedical:true,initialHospital:true,collapsed:true,headerOnly:true,...layout});
  for(let i=0;i<2400&&recovered.h.getState().stage!=='complete';i++)await recovered.step(250);
  assert.equal(recovered.h.getState().stage,'complete',recovered.h.getState().status);
  assert.equal(recovered.counts.join,2);assert.equal(recovered.counts.wrong,0);assert.equal(recovered.counts.medicine,3);
 }
 const ambiguousStart=fixture(false,false,false,{ambiguousJoin:true});
 for(let i=0;i<400&&ambiguousStart.h.getState().stage!=='paused';i++)await ambiguousStart.step(250);
 assert.equal(ambiguousStart.counts.join,0);assert.equal(ambiguousStart.counts.wrong,0);assert.equal(ambiguousStart.h.getState().stage,'paused');
 const duplicates=fixture(false,false,false,{nestedMelee:true,sharedPlayerClass:true,duplicateMelee:true});
 for(let i=0;i<250&&duplicates.h.getState().stage!=='paused';i++)await duplicates.step(250);
 assert.equal(duplicates.h.getState().stage,'paused');assert.equal(duplicates.counts.pillow,0);assert.equal(duplicates.counts.wrong,0);
 let resumed=fixture(false,false,false,{nestedMelee:true,sharedPlayerClass:true,defenderFirst:true,pillowHits:4,turnWait:1500,delay:1000});
 for(let i=0;i<100&&resumed.counts.pillow<1;i++)await resumed.step(250);
 assert.equal(resumed.counts.pillow,1);resumed.h.pause();assert.equal(resumed.h.resume(),true);
 assert.equal(resumed.h.getState().stage,'fight','Resume must continue an unfinished fight with its existing reservation');
 for(let i=0;i<800&&resumed.h.getState().stage!=='complete';i++)await resumed.step(250);
 assert.equal(resumed.h.getState().stage,'complete',resumed.h.getState().status);
 assert.equal(resumed.counts.pillow,8);assert.equal(resumed.counts.wrong,0);
 assert.equal(resumed.messages.filter(m=>m.type==='tc-loss-claim').length,2);
 let f=fixture(false,false,false,{initialHospital:true,kitsRequired:2,collapsed:true,headerOnly:true,pillowHits:3,replacedControls:true});
 for(let i=0;i<1000&&f.h.getState().stage!=='complete';i++)await f.step(250);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.medicine,6);assert.equal(f.counts.join,2);assert.equal(f.messages.filter(m=>m.type==='tc-loss-claim').length,2);
 f=fixture();for(let i=0;i<100&&f.h.getState().stage!=='complete';i++)await f.step();
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);
 assert.equal(f.counts.pillow,2);assert.equal(f.counts.join,2);assert.equal(f.counts.medicine,2);assert.equal(f.counts.wrong,0);
 assert.equal(f.messages.filter(m=>m.type==='tc-loss-commit').length,2);assert(f.counts.scroll>0);
 f=fixture(false,false,false,{collapsed:true,headerOnly:true,pillowHits:3,replacedControls:true,delay:1000});
 for(let i=0;i<800&&f.h.getState().stage!=='complete';i++)await f.step(250);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.expand,2);assert.equal(f.counts.medicine,2);assert.equal(f.counts.wrong,0);
 f=fixture(false,false,false,{collapsed:true});for(let i=0;i<110&&f.h.getState().stage!=='complete';i++)await f.step();
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.expand,2);assert.equal(f.counts.medicine,2);assert.equal(f.counts.wrong,0);
 f=fixture(false,false,false,{collapsed:true,expandFails:true});for(let i=0;i<60&&f.h.getState().stage!=='paused';i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert(f.h.getState().status.includes('Use control did not appear'));assert.equal(f.counts.expand,1);assert.equal(f.counts.medicine,0);
 f=fixture(false,false,false,{collapsed:true,ambiguousKit:true});for(let i=0;i<100&&f.h.getState().stage!=='paused';i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert.equal(f.counts.expand,0);assert.equal(f.counts.medicine,0);
 f=fixture(false,false,false,{pillowHits:3,delay:1000,replacedControls:true,collapsed:true});
 for(let i=0;i<800&&f.h.getState().stage!=='complete';i++)await f.step(250);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.pillow,6);assert.equal(f.counts.medicine,2);assert.equal(f.counts.wrong,0);
 for(let i=1;i<f.counts.pillowTimes.length;i++)assert(f.counts.pillowTimes[i]-f.counts.pillowTimes[i-1]>=1500);
 f=fixture(false,false,false,{pillowHits:25,delay:1000});
 for(let i=0;i<500&&f.h.getState().stage!=='complete';i++)await f.step(1000);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.pillow,50);assert.equal(f.counts.wrong,0);
 f=fixture(false,false,false,{pillowHits:3,delay:500,pillowDisabledMs:1500});
 assert.equal(f.h.getState().attackDelayMs,500);assert.equal(f.h.setAttackDelay(10),false);assert.equal(f.h.setAttackDelay(Infinity),false);assert.equal(f.h.getState().attackDelayMs,500);
 assert.equal(f.h.setAttackDelay(700),true);assert.equal(f.h.getState().attackDelayMs,700);
 for(let i=0;i<800&&f.h.getState().stage!=='complete';i++)await f.step(100);
 assert.equal(f.h.getState().stage,'complete',f.h.getState().status);assert.equal(f.counts.pillow,6);
 assert(f.counts.pillowTimes[0]-f.counts.joinTimes[0]>=1500,'Disabled Pillow control was clicked');
 for(let i=1;i<f.counts.pillowTimes.length;i++)assert(f.counts.pillowTimes[i]-f.counts.pillowTimes[i-1]>=700,'Pillow delay was bypassed');
 f=fixture(true);for(let i=0;i<55;i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert.equal(f.counts.pillow,0);assert.equal(f.counts.wrong,0);
 f=fixture(false,true);for(let i=0;i<60;i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert(f.h.getState().status.includes('Medical tab'));assert.equal(f.counts.medicine,0);
 f=fixture(false,false,true);for(let i=0;i<70;i++)await f.step();
 assert.equal(f.h.getState().stage,'paused');assert(f.h.getState().status.includes('JOIN FIGHT'));assert.equal(f.counts.pillow,0);
 const smart=fixture(false,false,false,{smartMedical:true,collapsed:true,headerOnly:true,pillowHits:3,nestedMelee:true,sharedPlayerClass:true});
 for(let i=0;i<1200&&smart.h.getState().stage!=='complete';i++)await smart.step(250);
 assert.equal(smart.h.getState().stage,'complete',smart.h.getState().status);assert.equal(smart.counts.medicine,2);assert.equal(smart.counts.wrong,0);
 assert.equal(smart.messages.filter(m=>m.type==='tc-medical-authorize').length,2);assert.equal(smart.messages.filter(m=>m.type==='tc-medical-verify').length,2);
 // Interruptions must resume/reconcile the same medical operation before another attack.
 for(const pauseAt of ['smart-medical-ready','smart-medical-execute','consumed']){
  const interrupted=fixture(false,false,false,{smartMedical:true,initialHospital:true,collapsed:true,headerOnly:true});
  for(let i=0;i<150;i++){await interrupted.step(250);if(pauseAt==='consumed'?interrupted.counts.medicine===1:interrupted.h.getState().stage===pauseAt)break;}
  assert(pauseAt==='consumed'?interrupted.counts.medicine===1:interrupted.h.getState().stage===pauseAt,'Missing interruption stage '+pauseAt);
  interrupted.h.pause();interrupted.h.resume();
  assert.equal(interrupted.h.getState().stage,pauseAt==='consumed'?'smart-medical-wait':'heal-open','Resume discarded pending medicine');
  assert.equal(interrupted.messages.filter(m=>m.type==='tc-loss-claim').length,0,'Claimed attack before recovery verification');
  for(let i=0;i<1200&&interrupted.h.getState().stage!=='complete';i++)await interrupted.step(250);
  assert.equal(interrupted.h.getState().stage,'complete',interrupted.h.getState().status);
  assert.equal(interrupted.counts.medicine,3,'Interrupted recovery double-consumed medicine');
  assert.equal(interrupted.messages.filter(m=>m.type==='tc-medical-verify').length,3);
 }
 for(const mode of [{medicalRecommend:true},{badMedicalItem:true},{wrongMedicalAccount:true},{missingMedicalAccount:true},{failedMedicalUse:true},{failMedicalVerification:true}]){
  const one=fixture(false,false,false,{smartMedical:true,initialHospital:true,collapsed:true,headerOnly:true,...mode});
  for(let i=0;i<400&&one.h.getState().stage!=='paused';i++)await one.step(250);
  assert.equal(one.h.getState().stage,'paused',JSON.stringify(mode)+one.h.getState().status);
  assert.equal(one.counts.medicine,mode.failMedicalVerification?1:0,'Unsafe medical consumption '+JSON.stringify(mode));
  assert(one.messages.filter(m=>m.type==='tc-medical-authorize').length<=1,'Repeated failed authorization');assert.equal(one.counts.wrong,0);
 }
 for(const identity of [{pageAccount:true,decoratedOwner:true,sidebarOtherIds:true,sidebarNpc:true},{pageAccount:true,decoratedOwner:true,sidebarOtherIds:true,sidebarNpc:true,leveling:true},{medicalServerError:true,leveling:true},{identityGlitchAfterUse:true,leveling:true,inlineMedicalConfirmation:true},{identityGlitchAfterUse:true,inlineMedicalConfirmation:true},{decoratedOwner:true,chatSidebar:true,inlineMedicalConfirmation:true,staleMedicalQuantity:true,collapsed:false,levelMedicalPending:true},{decoratedOwner:true,chatSidebar:true,inlineMedicalConfirmation:true,staleMedicalQuantity:true,collapsed:false},{decoratedOwner:true,chatSidebar:true,inlineMedicalConfirmation:true},{decoratedOwner:true,chatSidebar:true,inlineMedicalConfirmation:true,leveling:true},{decoratedOwner:true,chatSidebar:true},{decoratedOwner:true,chatSidebar:true,leveling:true},{decoratedOwner:true,ariaMedicalOwner:true,sidebarOtherIds:true},{decoratedOwner:true},{decoratedOwner:true,leveling:true},{labeledMedicalOwner:true,sidebarOtherIds:true},{labeledMedicalOwner:true,noSidebarWrapper:true,medicalOwnerHidden:true},{labeledMedicalOwner:true,noSidebarWrapper:true},{labeledMedicalOwner:true,noSidebarWrapper:true,identityDelayMs:7000}]){
  const identified=fixture(false,false,false,{smartMedical:true,initialHospital:true,collapsed:true,headerOnly:true,...identity});
  for(let i=0;i<1400&&identified.h.getState().stage!=='complete';i++)await identified.step(250);
  assert.equal(identified.h.getState().stage,'complete',identified.h.getState().status);assert.equal(identified.counts.medicine,3);assert.equal(identified.counts.wrong,0);if(identity.pageAccount){const d=identified.h.diagnostics();assert.equal(d.medicalIdentity.strategy,'page-account');assert.deepEqual(Array.from(d.medicalIdentity.sidebarIds),[321,102]);assert.equal(d.medicalIdentity.owner,321);}
 }
 for(const extra of [{pageAccount:true,labeledMedicalOwner:true,wrongMedicalAccount:true},{pageAccount:true,wrongPageAccount:true,decoratedOwner:true,sidebarOtherIds:true},{pageAccount:true,conflictingPageAccount:true,decoratedOwner:true,sidebarOtherIds:true},{decoratedOwner:true,sidebarOtherIds:true},{decoratedOwner:true,wrongMedicalAccount:true},{decoratedOwner:true,missingMedicalAccount:true}]){
  const blocked=fixture(false,false,false,{smartMedical:true,initialHospital:true,...extra});
  for(let i=0;i<400&&blocked.h.getState().stage!=='paused';i++)await blocked.step(250);
  assert.equal(blocked.counts.medicine,0,'Unverified sidebar identity consumed medicine');
  assert.equal(blocked.h.getState().stage,'paused');
  assert.equal(blocked.messages.filter(m=>m.type==='tc-medical-authorize').length,0);
 }
 const serverDown=fixture(false,false,false,{smartMedical:true,initialHospital:true,medicalServerError:true,permanentServerError:true});
 for(let i=0;i<500&&serverDown.h.getState().stage!=='paused';i++)await serverDown.step(250);
 assert.equal(serverDown.h.getState().stage,'paused');assert.equal(serverDown.counts.medicine,0);assert(serverDown.h.getState().status.includes('server error'));assert.equal(serverDown.h.getState().medicalServerAttempts,2);
 const lastKit=fixture(false,false,false,{smartMedical:true,initialHospital:true,lastMedicalItem:true,inlineMedicalConfirmation:true});
 for(let i=0;i<400&&!lastKit.messages.some(m=>m.type==='tc-medical-verify');i++)await lastKit.step(250);
 assert.equal(lastKit.counts.medicine,1);assert(lastKit.messages.some(m=>m.type==='tc-medical-verify'),'Last consumed row was not verified');
 assert.equal(lastKit.messages.find(x=>x.type==='tc-medical-verify').inventory.find(x=>x.id===67)?.quantity,0,'Last-item stale attribute was treated as inventory');
 const wrongPrompt=fixture(false,false,false,{smartMedical:true,initialHospital:true,inlineMedicalConfirmation:true,wrongMedicalPrompt:true});
 for(let i=0;i<250&&wrongPrompt.h.getState().stage!=='paused';i++)await wrongPrompt.step(250);
 assert.equal(wrongPrompt.counts.medicine,0);assert.equal(wrongPrompt.counts.wrong,0);assert.equal(wrongPrompt.h.getState().stage,'paused');
 const wrongAria=fixture(false,false,false,{smartMedical:true,initialHospital:true,decoratedOwner:true,ariaMedicalOwner:true,wrongMedicalAccount:true,chatSidebar:true});
 for(let i=0;i<100&&wrongAria.h.getState().stage!=='paused';i++)await wrongAria.step(250);
 assert.equal(wrongAria.counts.medicine,0);assert.equal(wrongAria.h.getState().stage,'paused');assert(wrongAria.h.getState().status.includes('expects 321'));
 const wrongLabeled=fixture(false,false,false,{smartMedical:true,initialHospital:true,labeledMedicalOwner:true,noSidebarWrapper:true,wrongMedicalAccount:true});
 for(let i=0;i<100&&wrongLabeled.h.getState().stage!=='paused';i++)await wrongLabeled.step(250);
 assert.equal(wrongLabeled.counts.medicine,0);assert.equal(wrongLabeled.h.getState().stage,'paused');assert(wrongLabeled.h.getState().status.includes('expects 321'),'Wrong account was not distinguished from missing identity');
 for(const selectedMedicalId of [66,68,739]){
  const med=fixture(false,false,false,{smartMedical:true,selectedMedicalId,initialHospital:true,collapsed:true,headerOnly:true});
  for(let i=0;i<1200&&med.h.getState().stage!=='complete';i++)await med.step(250);
  assert.equal(med.h.getState().stage,'complete',String(selectedMedicalId)+med.h.getState().status);assert.equal(med.counts.wrong,0);assert.equal(med.counts.medicine,3);
 }
 const replan=fixture(false,false,false,{smartMedical:true,initialHospital:true,replanMedicalOnce:true});
 for(let i=0;i<1200&&replan.h.getState().stage!=='complete';i++)await replan.step(250);
 assert.equal(replan.h.getState().stage,'complete',replan.h.getState().status);assert.equal(replan.counts.medicine,3);
 const unstable=fixture(false,false,false,{smartMedical:true,initialHospital:true,alwaysMedicalReplan:true});
 for(let i=0;i<500&&unstable.h.getState().stage!=='paused';i++)await unstable.step(250);
 assert.equal(unstable.h.getState().stage,'paused');assert.equal(unstable.counts.medicine,0);assert(unstable.h.getState().status.includes('changed repeatedly'));
 const wait=fixture(false,false,false,{smartMedical:true,initialHospital:true,medicalCooldownOnce:true,medicalAutoResume:true});
 for(let i=0;i<1200&&wait.h.getState().stage!=='complete';i++)await wait.step(250);
 assert.equal(wait.h.getState().stage,'complete',wait.h.getState().status);assert.equal(wait.counts.medicine,3);
 const noResume=fixture(false,false,false,{smartMedical:true,initialHospital:true,medicalCooldownOnce:true});
 for(let i=0;i<60&&noResume.h.getState().stage!=='paused';i++)await noResume.step(250);
 assert.equal(noResume.h.getState().stage,'paused');assert.equal(noResume.counts.medicine,0);
 coordinator.remaining=4;coordinator.owner='';coordinator.commits=0;
 const medA=fixture(false,false,false,{coordinator,slot:'A',smartMedical:true,pillowHits:2}),medB=fixture(false,false,false,{coordinator,slot:'B',smartMedical:true,pillowHits:3});
 for(let i=0;i<1800&&(medA.h.getState().stage!=='complete'||medB.h.getState().stage!=='complete');i++){await medA.step(250);await medB.step(250);}
 assert.equal(medA.h.getState().stage,'complete',medA.h.getState().status);assert.equal(medB.h.getState().stage,'complete',medB.h.getState().status);assert.equal(coordinator.commits,4);assert.equal(medA.counts.medicine+medB.counts.medicine,4);
 console.log('SMART MEDICAL WORKER PASSED: explicit consent, preflight, one-use dispatch, failed use/verification, account mismatch, drug rejection, cooldown/resume and simultaneous sessions.');
 for(const pending of [false,true]){
  const fighter=fixture(false,false,false,{leveling:true,smartMedical:true,pillowHits:3,collapsed:true,headerOnly:true,levelProgress:true,levelPendingOnce:pending,levelMedicalPending:pending,noVictoryText:pending});
  let resumed=false;
  for(let i=0;i<1600&&fighter.h.getState().stage!=='complete';i++){
   await fighter.step(250);
   if(pending&&!resumed&&fighter.h.getState().stage==='level-verify'){fighter.h.pause();fighter.h.resume();resumed=true;}
  }
  assert.equal(fighter.h.getState().stage,'complete',fighter.h.getState().status);
  assert.equal(fighter.counts.pillow,6,'Repeated weapon turns missing');assert.equal(fighter.counts.leave,2,'Leave repeated/missing');assert.equal(fighter.counts.levelCommits,2);assert.equal(fighter.counts.wrong,0);
  assert.equal(fighter.counts.medicine,2,'Recovery between leveling fights missing');
  assert.equal(fighter.messages.filter(m=>m.type==='tc-loss-commit').length,0,'Leveling polluted loss ledger');
  assert.equal(fighter.messages.filter(m=>m.type==='tc-level-claim').length,2,'New attack claimed before receipt');
 }
 for(const slots of [{configuredWeaponSlot:'weapon_primary',renderedWeaponSlot:'weapon_main'},{configuredWeaponSlot:'weapon_secondary',renderedWeaponSlot:'weapon_second'}]){
  const liveSlots=fixture(false,false,false,{leveling:true,smartMedical:true,sharedPlayerClass:true,defenderFirst:true,...slots});
  for(let i=0;i<1800&&liveSlots.h.getState().stage!=='complete';i++)await liveSlots.step(250);
  assert.equal(liveSlots.h.getState().stage,'complete',liveSlots.h.getState().status);assert.equal(liveSlots.counts.levelCommits,2);assert.equal(liveSlots.counts.wrong,0);
 }
 const mission=fixture(false,false,false,{leveling:true,smartMedical:true,missionOnly:true});
 for(let i=0;i<600&&mission.h.getState().stage!=='complete';i++)await mission.step(250);
 assert.equal(mission.h.getState().stage,'complete',mission.h.getState().status);assert.equal(mission.counts.join,0);assert.equal(mission.counts.pillow,0);assert.equal(mission.counts.levelCommits,0);assert.equal(mission.messages.filter(m=>m.type==='tc-level-skip').length,2);
 const waiting=fixture(false,false,false,{leveling:true,smartMedical:true,levelWaitOnce:true});
 for(let i=0;i<1600&&waiting.h.getState().stage!=='complete';i++)await waiting.step(250);
 assert.equal(waiting.h.getState().stage,'complete',waiting.h.getState().status);assert.equal(waiting.counts.levelCommits,2);assert.equal(waiting.messages.filter(m=>m.type==='tc-level-claim').length,3,'Hospital wait duplicated/lost a claim');
 const ended=fixture(false,false,false,{leveling:true,smartMedical:true,continueEnded:true,levelPendingOnce:true,pillowHits:2});
 for(let i=0;i<1800&&ended.h.getState().stage!=='complete';i++)await ended.step(250);
 assert.equal(ended.h.getState().stage,'complete',ended.h.getState().status);assert.equal(ended.counts.levelCommits,2);assert.equal(ended.counts.pillow,4);assert.equal(ended.counts.wrong,0);assert(ended.messages.some(m=>m.type==='tc-level-commit'&&m.terminal===true),'Ended-page evidence not sent');
 const emptyGun=fixture(false,false,false,{leveling:true,smartMedical:true,noAmmo:true});
 for(let i=0;i<300&&emptyGun.h.getState().stage!=='paused';i++)await emptyGun.step(250);
 assert.equal(emptyGun.h.getState().stage,'paused');assert.equal(emptyGun.counts.pillow,0);assert(/no ammo/i.test(emptyGun.h.getState().status));
 const reload=fixture(false,false,false,{leveling:true,smartMedical:true,stuckReload:true});
 for(let i=0;i<300&&reload.h.getState().stage!=='paused';i++)await reload.step(250);
 assert.equal(reload.h.getState().stage,'paused');assert.equal(reload.counts.pillow,1,'Repeated unchanged reload');
 const slowPlan=fixture(false,false,false,{smartMedical:true,collapsed:true,headerOnly:true,planApiDelayMs:20000,expandDelayMs:3000});
 for(let i=0;i<1600&&slowPlan.h.getState().stage!=='complete';i++)await slowPlan.step(250);
 assert.equal(slowPlan.h.getState().stage,'complete',slowPlan.h.getState().status);assert.equal(slowPlan.counts.medicine,2);assert.equal(slowPlan.counts.wrong,0);
 const quickReceipt=fixture(false,false,false,{smartMedical:true,levelMedicalPending:true,medicalRetryMs:2500});
 for(let i=0;i<1400&&quickReceipt.h.getState().stage!=='complete';i++)await quickReceipt.step(250);
 assert.equal(quickReceipt.h.getState().stage,'complete',quickReceipt.h.getState().status);assert.equal(quickReceipt.counts.medicine,2);assert(quickReceipt.counts.medicalVerifyTimes[1]-quickReceipt.counts.medicalVerifyTimes[0]>=2500&&quickReceipt.counts.medicalVerifyTimes[1]-quickReceipt.counts.medicalVerifyTimes[0]<5000,'Fast pending receipt retry was not bounded near 2.5 seconds');
 const newEpisode=fixture(false,false,false,{leveling:true,smartMedical:true,levelHealAfterWinOnce:true,itemsTabDelayMs:3000});
 for(let i=0;i<1800&&newEpisode.h.getState().stage!=='complete';i++)await newEpisode.step(250);
 assert.equal(newEpisode.h.getState().stage,'complete',newEpisode.h.getState().status);assert.equal(newEpisode.counts.levelCommits,2);assert.equal(newEpisode.counts.wrong,0);
 const hospRace=fixture(false,false,false,{leveling:true,smartMedical:true,hospitalAfterJoin:true});
 for(let i=0;i<1800&&hospRace.h.getState().stage!=='complete';i++)await hospRace.step(250);
 assert.equal(hospRace.h.getState().stage,'complete',hospRace.h.getState().status);assert.equal(hospRace.counts.levelCommits,2);assert.equal(hospRace.counts.join,3);assert.equal(hospRace.messages.filter(m=>m.type==='tc-level-claim').length,2,'Hospital recovery lost the reservation');assert.equal(hospRace.counts.medicalPlanModes[0],false);assert.equal(hospRace.counts.medicalPlanModes[1],false,'New hospital reused prior healthy state');assert(hospRace.counts.medicalPlanModes.some(x=>x),'Same-episode verified continuation lost');
 const onlySmall=fixture(false,false,false,{smartMedical:true,selectedMedicalId:68,medicalHeading:'Medical inventory'});
 for(let i=0;i<1800&&onlySmall.h.getState().stage!=='complete';i++)await onlySmall.step(250);
 assert.equal(onlySmall.h.getState().stage,'complete',onlySmall.h.getState().status);assert.equal(onlySmall.counts.medicine,2);
 for(const opts of [{apiWaitOnce:true},{apiCommitWaitOnce:true},{levelChoice:true},{levelChoice:true,acceptLevelUps:true}]){
  const recovery=fixture(false,false,false,{leveling:true,smartMedical:true,...opts});
  for(let i=0;i<1800&&recovery.h.getState().stage!=='complete';i++)await recovery.step(250);
  assert.equal(recovery.h.getState().stage,'complete',recovery.h.getState().status);assert.equal(recovery.counts.levelCommits,2);assert.equal(recovery.counts.wrong,0);
 }
 for(const opts of [
  {identityDelayMs:24000},
  {switchMedicalAfterFirst:true,selectedMedicalId:66},
  {depleteBeforeExecute:true,selectedMedicalId:66}
 ]){
  const supplies=fixture(false,false,false,{leveling:true,smartMedical:true,collapsed:true,headerOnly:true,...opts});
  for(let i=0;i<1800&&supplies.h.getState().stage!=='complete';i++)await supplies.step(250);
  assert.equal(supplies.h.getState().stage,'complete',JSON.stringify(opts)+supplies.h.getState().status);assert.equal(supplies.counts.medicine,2);assert.equal(supplies.counts.wrong,0);
 }
 const badWeapon=fixture(false,false,false,{leveling:true,smartMedical:true,pillowLevelWeapon:true});
 for(let i=0;i<300&&badWeapon.h.getState().stage!=='paused';i++)await badWeapon.step(250);
 assert.equal(badWeapon.h.getState().stage,'paused');assert.equal(badWeapon.counts.pillow,0,'Pillow used for leveling');
 console.log('AUTOMATIC LEVELING WORKER PASSED: repeated weapon attacks, distinct target grants, Leave only, cached receipt wait/resume, medical recovery, no loss-ledger writes and Pillow refusal.');
 console.log('LOSS FULL CYCLE PASSED: shared combatant classes, opponent Pillow in either DOM order, ambiguous-slot refusal, two solo losses, nested player roots, image identity, scroll, medical tab, label-only FAK row, confirmation, return, final recovery collapsed-kit expansion, bounded expansion failure, ambiguous-kit refusal, adjustable attack spacing and ambiguous-player refusal.');
})().catch(e=>{console.error(e);process.exitCode=1});
