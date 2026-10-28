(() => {
  'use strict';
  if (window.__tcLossAutoInstalled) return;
  window.__tcLossAutoInstalled = true;

  const STORAGE_KEY = '__tc_loss_auto_cycle_v1';
  const SESSION_KEY = '__tc_loss_auto_cycle_session_v1';
  const ATTACK_URL = id => `https://www.torn.com/page.php?sid=attack&user2ID=${encodeURIComponent(id)}`;
  const ITEMS_URL = 'https://www.torn.com/item.php';
  const MAX_PILLOW_TURNS = 100;
  const TICK_MS = 250;
  const attackDelay = value => Number.isFinite(Number(value)) ? Math.max(500, Math.min(5000, Math.round(Number(value)))) : 1900;
  let busy = false;

  const now = () => Date.now();
  const clean = s => String(s || '').replace(/\s+/g, ' ').trim();
  const bodyText = () => clean(document.body?.innerText || '');
  const visible = el => Boolean(el && el.getClientRects?.().length && getComputedStyle(el).visibility !== 'hidden' && getComputedStyle(el).display !== 'none');
  const click = el => {
    if (!el || !visible(el) || el.disabled || el.getAttribute?.('aria-disabled') === 'true') return false;
    el.scrollIntoView?.({block:'center',inline:'nearest',behavior:'instant'});
    if (typeof el.click === 'function') el.click();
    else el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
    return true;
  };
  const labelOf = el => clean(el?.innerText || el?.value || el?.getAttribute?.('aria-label') || el?.getAttribute?.('title') || '').toLowerCase();
  const post = payload => {
    try { if(load()?.mode==='level'&&payload.type?.startsWith('tc-loss-'))payload.type=payload.type.replace('tc-loss-','tc-level-');window.chrome?.webview?.postMessage(JSON.stringify(payload)); } catch {}
  };

  function load() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  }
  function save(state) {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(state)); } catch {}
    report(state);
    return state;
  }
  function clear() {
    try { localStorage.removeItem(STORAGE_KEY); } catch {}
  }
  function report(state, extra = {}) {
    post({
      type: 'tc-loss-status',
      profileId: state?.profileId || '',
      slot: state?.slot || '',
      orderId: state?.orderId || '',
      claimId: state?.claimId || '',
      stage: state?.stage || 'idle',
      running: Boolean(state?.running),
      status: state?.status || 'Idle',
      pillowTurns: Number(state?.pillowTurns || 0),
      kitsThisLoss: Number(state?.kitsThisLoss || 0),
      ...extra
    });
  }

  function challengeVisible() {
    const title = document.title.toLowerCase();
    const text = (document.body?.innerText || '').slice(0, 1800).toLowerCase();
    return title.includes('just a moment') || /verifying you are human|verify you are human|checking your browser|complete the security check/i.test(text);
  }

  function isAttackPage(targetId) {
    try {
      const u = new URL(location.href);
      return u.pathname.endsWith('/page.php') && u.searchParams.get('sid') === 'attack' && Number(u.searchParams.get('user2ID')) === Number(targetId);
    } catch { return false; }
  }
  function isItemsPage() { return location.pathname === '/item.php'; }

  function hospitalBanner() {
    return /you can(?:not|'t|â€™t) attack(?: anyone)? while (?:you are )?in (?:the )?hospital|you are (?:currently |still )?in (?:the )?hospital/i.test(bodyText());
  }

  function insufficientEnergy() {
    // Use Torn's explicit refusal, never infer a balance from unrelated page numbers.
    return /you (?:do not|don't|donâ€™t) have (?:enough|sufficient) energy|you have (?:insufficient|not enough) energy|not enough energy to (?:attack|start|join)|you need (?:at least )?25 energy to (?:attack|start|join)/i.test(bodyText());
  }
  function pauseForEnergy(state) {
    return pauseState(state,'Not enough energy to attack. This account is paused and its slot released; the other running account can continue. Resume this account after restoring energy.',true);
  }

  function joinFightCandidates() {
    const action=/^(join fight|start fight|start attack|begin fight)$/i;
    const buttons=Array.from(document.querySelectorAll('button,[role="button"],input[type="button"],a'))
      .filter(el=>visible(el)&&enabledControl(el,document.body?.parentElement))
      .filter(el=>[el.innerText,el.value,el.getAttribute?.('aria-label')].some(text=>action.test(clean(text))));
    // A semantic wrapper and its nested real button describe one action, not two.
    // Retain the innermost control; independent matching controls remain ambiguous.
    return buttons.filter(el=>!buttons.some(other=>other!==el&&el.contains?.(other)));
  }
  function joinFightButton() {
    const buttons=joinFightCandidates();return buttons.length===1?buttons[0]:null;
  }

  const outerRoots = nodes => [...new Set(nodes)].filter(a=>!nodes.some(b=>b!==a&&b.contains?.(a)));
  function playerArea() {
    const roots=outerRoots(Array.from(document.querySelectorAll('[class*="playerArea"]')).filter(visible));
    if(roots.length===1)return roots[0];
    // Both combatants can use Torn's playerArea class. Only the attacker's
    // actionable weapon slot identifies ownership; DOM order and Pillow artwork
    // do not (the opponent may also equip a Pillow).
    const slots=roots.flatMap(area=>Array.from(area.querySelectorAll('[id="weapon_melee"]')).filter(visible).map(slot=>({area,slot})));
    if(slots.length){
      // Torn also renders weapon_melee on the opponent's unavailable weapon.
      // The attack action name, not that duplicated ID, establishes ownership.
      const attacks=slots.filter(({slot})=>[slot,...slot.querySelectorAll('[aria-label],[title],span')].some(el=>
        [el.getAttribute?.('aria-label'),el.getAttribute?.('title'),el.innerText].some(v=>/^attack with pillow[.!]?$/i.test(clean(v)))));
      if(attacks.length)return attacks.length===1?attacks[0].area:null;
      return slots.length===1?slots[0].area:null;
    }
    const actionable=roots.filter(area=>Array.from(area.querySelectorAll('button,[role="button"],[aria-label]')).some(el=>
      visible(el)&&[el.getAttribute?.('aria-label'),el.innerText].some(v=>/^attack with pillow[.!]?$/i.test(clean(v)))));
    return actionable.length===1?actionable[0]:null;
  }
  function enabledControl(el, boundary) {
    for(let node=el;node&&node!==boundary;node=node.parentElement)
      if(!visible(node)||node.disabled||node.getAttribute?.('aria-disabled')==='true')return false;
    return true;
  }
  function pillowControl(reveal=true) {
    const area=playerArea();if(!area)return null;
    if(reveal)area.scrollIntoView?.({block:'center',inline:'nearest'});
    // Target the weapon slot itself, not a nested artwork/info button. Torn
    // replaces descendants between turns; the stable slot owns the attack action.
    const melee=Array.from(area.querySelectorAll('[id="weapon_melee"]')).filter(visible);
    if(melee.length){
      if(melee.length!==1)return null;
      const slot=melee[0];
      const identity=[slot,...slot.querySelectorAll('[aria-label],[title],[alt],img')].some(el=>
        ['aria-label','title','alt'].some(k=>/^(?:attack with )?pillow[.!]?$/i.test(clean(el.getAttribute?.(k))))||
        /\/(?:items|weapons)\/440(?:[/.?]|$)/i.test(el.getAttribute?.('src')||''))||
        Array.from(slot.querySelectorAll('span,strong,p')).some(el=>/^pillow$/i.test(clean(el.textContent)));
      // A known melee slot holding another weapon must never fall through.
      return identity&&enabledControl(slot,area)?slot:null;
    }
    // Torn may replace the weapon artwork with a turn animation. Its named attack
    // control remains the authoritative target; resolve it afresh on every turn.
    const named=Array.from(area.querySelectorAll('[aria-label],[title],button,[role="button"]'))
      .filter(el=>visible(el)&&[el.getAttribute?.('aria-label'),el.getAttribute?.('title'),el.innerText].some(v=>/^attack with pillow[.!]?$/i.test(clean(v))));
    const namedRoots=outerRoots(named);
    if(namedRoots.length)return namedRoots.length===1&&enabledControl(namedRoots[0],area)?namedRoots[0]:null;
    const attributes=['alt','title','aria-label','data-tooltip','data-tippy-content'];
    const markers=Array.from(area.querySelectorAll('[data-item-id="440"],[data-id="440"],[data-itemid="440"],[alt],[title],[aria-label],[data-tooltip],[data-tippy-content],img'))
      .filter(visible).filter(el=>['data-item-id','data-id','data-itemid'].some(k=>el.getAttribute?.(k)==='440')||
        attributes.some(k=>/^pillow(?:\s*\([^)]*\))?$/i.test(clean(el.getAttribute?.(k))))||
        /\/(?:items|weapons)\/440(?:[/.?]|$)/i.test(el.getAttribute?.('src')||''));
    for(const leaf of area.querySelectorAll('span,div,strong,p'))if(!leaf.children?.length&&/^pillow$/i.test(clean(leaf.textContent))&&visible(leaf))markers.push(leaf);
    const candidates=[];
    for(const marker of markers){
      let fallback=null,control=null;
      for(let node=marker,depth=0;node&&node!==area&&depth<8;node=node.parentElement,depth++){
        if(!visible(node))continue;
        if(node.disabled||node.getAttribute?.('aria-disabled')==='true'){control=null;fallback=null;break;}
        const tag=String(node.tagName||'').toLowerCase(),role=node.getAttribute?.('role');
        if(tag==='button'||tag==='a'||role==='button'){control=node;break;}
        if(!fallback&&/weapon|melee|slot|equipment/i.test(String(node.className?.baseVal??node.className??'')))fallback=node;
      }
      if(control||fallback)candidates.push(control||fallback);
    }
    const roots=outerRoots(candidates);
    return roots.length===1&&enabledControl(roots[0],area)?roots[0]:null;
  }

  function levelingWeapon(state){
    const aliases={weapon_primary:['weapon_primary','weapon_main'],weapon_secondary:['weapon_secondary','weapon_second'],weapon_melee:['weapon_melee']};
    const ids=aliases[state.weaponSlot];if(!ids)return null;
    const slots=outerRoots(Array.from(document.querySelectorAll(ids.map(id=>'[id="'+id+'"]').join(','))).filter(visible));
    const candidates=slots.filter(slot=>{
      if(!enabledControl(slot,slot.parentElement))return false;
      const names=[slot.getAttribute?.('aria-label'),slot.getAttribute?.('title'),...Array.from(slot.querySelectorAll('[aria-label],[title],span')).map(x=>x.getAttribute?.('aria-label')||x.getAttribute?.('title')||x.textContent)].map(clean);
      return names.some(x=>/^attack with (?!pillow\b).+/i.test(x))&&!names.some(x=>/this weapon is not available/i.test(x))&&!/\bno ammo\b/i.test(labelOf(slot));
    });return candidates.length===1?candidates[0]:null;
  }
  function levelingEmptyGun(state){
    const ids=state.weaponSlot==='weapon_primary'?['weapon_primary','weapon_main']:state.weaponSlot==='weapon_secondary'?['weapon_secondary','weapon_second']:[];
    if(!ids.length)return false;
    const slots=outerRoots(Array.from(document.querySelectorAll(ids.map(id=>'[id="'+id+'"]').join(','))).filter(visible));
    const own=slots.filter(slot=>[slot,...slot.querySelectorAll('[aria-label],[title]')].some(el=>/^attack with (?!pillow\b).+/i.test(clean(el.getAttribute?.('aria-label')||el.getAttribute?.('title'))))&&!/this weapon is not available/i.test(labelOf(slot)));
    return own.length===1&&/\bno ammo\b/i.test(labelOf(own[0]));
  }
  function levelingContinueControl(){
    const roots=outerRoots(Array.from(document.querySelectorAll('[class*="playerArea"]')).filter(visible));
    if(roots.length!==1)return null;
    const buttons=Array.from(roots[0].querySelectorAll('button,a,[role="button"]')).filter(el=>visible(el)&&enabledControl(el,el.parentElement)&&/^continue$/i.test(labelOf(el)));
    return buttons.length===1?buttons[0]:null;
  }
  function levelingLeaveControl(requireChoices=false){
    const controls=Array.from(document.querySelectorAll('button,a,[role="button"],input[type="button"]')).filter(el=>visible(el)&&enabledControl(el,el.parentElement));
    const leave=controls.filter(el=>/^leave(?:\s+(?:them|him|her|target|on the street).*)?$/i.test(labelOf(el)));
    if(requireChoices&&!controls.some(el=>/^(?:mug|hospitalize|hospitalise)$/i.test(labelOf(el))))return null;
    return leave.length===1?leave[0]:null;
  }
  function finishLevelingMedical(state){
    if(state.finishAfterRecovery){state.running=false;state.stage='complete';state.status=state.levelStopMessage||'Leveling pass complete; recovery verified.';save(state);post({type:'tc-loss-finished',profileId:state.profileId,orderId:state.orderId});}
    else{state.stage=state.claimId?'attack-open':'claim';state.status='Recovery verified. Checking the next leveling target.';state.navigationUrl='';save(state);}
  }
  function levelCommit(state,terminal=false){state.levelTerminalSeen=terminal;state.stage='commit-wait';state.commitAttempts=1;state.replyWaitAt=now();state.status='Verifying the completed leveling fight with Torn.';save(state);post({type:'tc-loss-commit',profileId:state.profileId,slot:state.slot,orderId:state.orderId,claimId:state.claimId,terminal});}
  function detectFightResult(state) {
    const text = bodyText();
    const self = clean(state.selfName || '');
    if (!text) return null;
    if (self) {
      const escaped = self.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\ /g, '\\s+');
      const rx = new RegExp('(?:^|\\b)' + escaped + '\\s+(lost to|defeated|won against)\\s+([^.!?\\n\\r]+)', 'i');
      const m = text.match(rx);
      if (m) return /^lost to$/i.test(m[1]) ? 'loss' : 'win';
    }
    if (/\byou\s+(?:have\s+)?lost\s+to\b/i.test(text) || /\bdefeat(?:ed)?\b.*\byou lost\b/i.test(text)) return 'loss';
    if (/\byou\s+(?:have\s+)?defeated\b/i.test(text) || /\byou won against\b/i.test(text)) return 'win';
    return null;
  }

  // Closed personal-medical executor. Planning/consent live in the native backend.
  const medicalPage=()=>isItemsPage()&&new URL(location.href).origin==='https://www.torn.com';
  const medicalNames={66:'Morphine',67:'First Aid Kit',68:'Small First Aid Kit',739:'Blood Bag : O-'};
  const sameMedicalName=(text,name)=>clean(text).replace(/\s*:\s*/g,':').toLowerCase()===clean(name).replace(/\s*:\s*/g,':').toLowerCase();
  function medicalIdentity(){
    const labeled=new Set(),sidebar=new Set(),named=new Set(),chatIds=new Set();
    const inChat=link=>{for(let node=link;node;node=node.parentElement){const id=node.getAttribute?.("id")||"";if(id.startsWith("chat_panel_button:")||id.startsWith("chat_panel:"))return true;}return false;};
    const idOf=link=>{try{const u=new URL(link.getAttribute('href'),location.href);return u.protocol==='https:'&&(u.hostname==='www.torn.com'||u.hostname==='torn.com')?Number(u.searchParams.get('XID')):0;}catch{return 0;}};
    // The responsive account Name link can expose its label through ARIA rather than textContent.
    for(const link of document.querySelectorAll('a[href*="profiles.php"]')){
      if(inChat(link))continue;
      const labels=[link.getAttribute('aria-label'),link.getAttribute('title'),link.textContent].map(clean);
      if(!labels.some(text=>text.length<=300&&/^(?:name|username)\s*:/i.test(text)))continue;
      const id=idOf(link);if(Number.isSafeInteger(id)&&id>0)labeled.add(id);
    }
    // Torn's own Name row remains identifiable even when responsive sidebar wrappers change.
    for(const label of document.querySelectorAll('span,strong,b,div,dt,td,p,li,dd,label,th,section')){
      if(inChat(label)||clean(label.textContent).length>300||!/^(?:name|username)\s*:/i.test(clean(label.textContent)))continue;
      let scope=label;
      for(let depth=0;scope&&depth<3;depth++,scope=scope.parentElement){
        const text=clean(scope.textContent);if(text.length>300||!/^(?:name|username)\s*:/i.test(text))break;
        const ids=new Set(Array.from(scope.querySelectorAll('a[href*="profiles.php"]')).filter(link=>!inChat(link)).map(idOf).filter(id=>Number.isSafeInteger(id)&&id>0));
        if(ids.size===1){labeled.add([...ids][0]);break;}
      }
    }
    const self=clean(load()?.selfName).toLowerCase();
    for(const link of document.querySelectorAll('[id="sidebar"] a[href*="profiles.php"],[id="sidebarroot"] a[href*="profiles.php"],[class*="sidebar"] a[href*="profiles.php"]')){
      const id=idOf(link);if(!Number.isSafeInteger(id)||id<=0)continue;if(inChat(link)){chatIds.add(id);continue;}sidebar.add(id);
      const text=clean(link.textContent).toLowerCase();if(self&&(text===self||text==='name: '+self))named.add(id);
    }
    // A single sidebar profile identity is usable even when Torn decorates or hides its name text.
    // Never select the expected ID from multiple candidates or override explicit Name fields.
    const uniqueSidebar=!labeled.size&&!named.size&&sidebar.size===1;
    const owner=labeled.size===1?[...labeled][0]:labeled.size>1?null:named.size===1?[...named][0]:uniqueSidebar?[...sidebar][0]:null;
    return{owner,strategy:labeled.size?'name-field':named.size?'sidebar-name':uniqueSidebar?'sidebar-unique':'unavailable',labeledIds:[...labeled],sidebarIds:[...sidebar],excludedChatIds:[...chatIds]};
  }
  function medicalOwnId(){return medicalIdentity().owner;}
  function medicalRow(id){
    const name=medicalNames[id];if(!name||!medicalPage())return null;
    const markers=Array.from(document.querySelectorAll('span,strong,b,h3,h4,a,div')).filter(el=>visible(el)&&!el.children?.length&&sameMedicalName(el.textContent,name));
    const rows=[];
    for(const marker of markers)for(let node=marker,depth=0;node&&depth<8;node=node.parentElement,depth++){
      if(!visible(node))continue;
      const tag=String(node.tagName||'').toLowerCase();
      if(!['li','tr'].includes(tag))continue;
      if(/faction|armory|borrowed/i.test(clean(node.textContent))||['data-faction-owned','data-armory','data-borrowed'].some(k=>['true','1'].includes(node.getAttribute?.(k))))break;
      const ids=[node,...node.querySelectorAll('[data-item-id],[data-itemid],[data-item]')].flatMap(n=>['data-item-id','data-itemid','data-item'].map(k=>n.getAttribute?.(k)).filter(Boolean));
      if(ids.some(x=>String(x)!==String(id)))break;
      rows.push(node);break;
    }
    const roots=outerRoots(rows);return roots.length===1?roots[0]:null;
  }
  function medicalCount(id){
    const row=medicalRow(id);if(!row)return null;
    const text=clean(row.innerText||row.textContent).replace(/\s*:\s*/g,':'),name=medicalNames[id].replace(/\s*:\s*/g,':').replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
    const m=text.match(new RegExp('(?:^|\\s)[x×]\\s*(\\d+)\\s*'+name+'(?=\\s|$)','i'))||text.match(new RegExp(name+'\\s*[x×]\\s*(\\d+)\\b','i'));
    if(m)return Number(m[1]);
    // A consumed final item can remain as a remove-act row, with its old data quantity.
    // Require the row's own successful-use text and no visible quantity before treating it as zero.
    const rowText=text.toLowerCase();
    if(/(?:^|\s)remove-act(?:\s|$)/.test(String(row.className||''))&&rowText.includes('you use your '+medicalNames[id].toLowerCase()))return 0;
    // Visible quantities update before Torn refreshes its cached quantity attributes.
    for(const node of [row,...row.querySelectorAll('[data-qty],[data-quantity]')])for(const key of ['data-qty','data-quantity']){const q=node.getAttribute?.(key);if(q!=null&&/^\d+$/.test(q))return Number(q);}
    if(medicalUse(id)&&!rowText.includes('you use your '))return 1;
    return null;
  }
  function medicalInventory(){
    const rows=[];for(const id of Object.keys(medicalNames).map(Number)){const q=medicalCount(id);if(q!==null)rows.push({id,quantity:q});}return rows;
  }
  function medicalUse(id){
    const row=medicalRow(id);if(!row)return null;
    const candidates=Array.from(row.querySelectorAll('button,a,[role="button"],[data-action],span,div')).filter(el=>visible(el)&&enabledControl(el,row.parentElement)&&
      (/^(use|use item)$/i.test(labelOf(el))||labelOf(el)==='use '+medicalNames[id].toLowerCase()||el.getAttribute?.('data-action')==='use'||(!labelOf(el)&&/\b(use|check|consume)\b/.test(String(el.className||'').replace(/[-_]/g,' ')))));
    const roots=outerRoots(candidates);return roots.length===1?roots[0]:null;
  }
  function medicalExpand(id){
    const row=medicalRow(id);if(!row)return null;
    if([row,...row.querySelectorAll('[aria-expanded]')].some(x=>x.getAttribute?.('aria-expanded')==='true'))return null;
    const headers=outerRoots(Array.from(row.querySelectorAll('[role="tab"],[id*="ui-accordion-"]')).filter(el=>visible(el)&&enabledControl(el,row.parentElement)&&(el.getAttribute?.('role')==='tab'||/-header-/.test(el.getAttribute?.('id')||''))));
    if(headers.length)return headers.length===1?headers[0]:null;
    const names=Array.from(row.querySelectorAll('span,strong,b,h3,h4,a')).filter(el=>visible(el)&&!el.children?.length&&sameMedicalName(el.textContent,medicalNames[id]));return names.length===1?names[0]:null;
  }
  function medicalConfirm(id){
    const row=medicalRow(id),name=medicalNames[id];if(!row||!name)return null;
    // Torn's inline medical prompt uses an empty confirmation link labelled by wai-action-desc.
    const question=text=>clean(text).replace(/\s+\?/g,'?').toLowerCase()==='would you like to use the '+name.toLowerCase()+'?'||clean(text).toLowerCase()==='would you like to use '+name.toLowerCase()+'?';
    const prompts=Array.from(row.querySelectorAll('[id="wai-action-desc"],[role="status"]')).filter(el=>visible(el)&&question(el.textContent));
    if(prompts.length===1){
      const prompt=prompts[0],promptId=prompt.getAttribute('id');
      const inline=Array.from(row.querySelectorAll('button,a,[role="button"]')).filter(el=>{
        if(!visible(el)||!enabledControl(el,row.parentElement))return false;
        const labels=[labelOf(el),el.getAttribute('aria-label'),el.getAttribute('title')];
        const refs=clean(el.getAttribute('aria-labelledby')).split(' ');
        return labels.some(question)||(promptId&&refs.includes(promptId)&&!labels.some(x=>/cancel|no|send|trash/i.test(clean(x))));
      });
      if(inline.length)return inline.length===1?inline[0]:null;
    }
    const roots=outerRoots(Array.from(document.querySelectorAll('[role="dialog"],[class*="confirm"],[class*="Confirm"],[class*="modal"],[class*="dialog"]')).filter(el=>visible(el)&&clean(el.textContent).length<1200&&clean(el.textContent).toLowerCase().includes(name.toLowerCase())&&
      !Object.entries(medicalNames).some(([other,label])=>Number(other)!==id&&clean(el.textContent).toLowerCase().includes(label.toLowerCase())&&(id!==68||Number(other)!==67))));
    if(roots.length!==1)return null;
    const buttons=Array.from(roots[0].querySelectorAll('button,a,[role="button"]')).filter(el=>visible(el)&&enabledControl(el,roots[0].parentElement)&&/^(okay|ok|confirm|use)$/i.test(labelOf(el)));return buttons.length===1?buttons[0]:null;
  }
  function requestMedical(state,type,extra={},identity=medicalIdentity()){
    const owner=identity.owner;
    if(owner===null){if(state.stage!=='smart-medical-identity')state.medicalIdentityWaitAt=now();state.medicalIdentityWaitAt=state.medicalIdentityWaitAt||now();state.medicalIdentityRequest={type,extra};state.stage='smart-medical-identity';state.status='Waiting for Torn to show this account’s Name / profile identity. No item authorized.';save(state);return;}
    if(owner!==Number(state.playerId)){pauseState(state,'Medical page belongs to player '+owner+'; this worker expects '+state.playerId+' (profile '+state.profileId+'). Check this profile’s Torn login and API key. No item authorized.',true);return;}
    state.medicalIdentityWaitAt=0;state.medicalIdentityLostAt=0;state.medicalIdentityReloads=0;state.medicalIdentityRequest=null;
    state.medicalRequest='med_'+now()+'_'+Math.random().toString(36).slice(2);state.medicalReplyAt=now();state.stage='smart-medical-await';state.status=type==='tc-medical-plan'?'Checking hospital time and medical cooldown with Torn API.':type==='tc-medical-authorize'?'Checking medical permission before use.':'Verifying item use and hospital recovery with Torn API.';save(state);
    const continueVerifiedMedical=type==='tc-medical-plan'&&state.medicalContinueVerified===true;if(type==='tc-medical-plan'){state.medicalContinueVerified=false;save(state);}
    const cancelBeforeDispatch=type==='tc-medical-plan'&&state.medicalCancelBeforeDispatch===true;if(type==='tc-medical-plan'){state.medicalCancelBeforeDispatch=false;save(state);}
    post({cancelBeforeDispatch,type,requestId:state.medicalRequest,profileId:state.profileId,orderId:state.orderId,medicalSession:state.medicalSession,browserPlayerId:owner,inventory:medicalInventory(),operationToken:state.medicalOp?.token||'',medicalDispatched:state.medicalDispatched===true,continueVerifiedMedical,...extra});
  }
  function replanMedical(state,reason,cancelBeforeDispatch=false){
    state.medicalReplans=Number(state.medicalReplans||0)+1;
    if(state.medicalReplans>3){pauseState(state,'Medical state changed repeatedly; review before continuing. No uncertain item was retried.',true);return;}
    beginHealing(state);state.medicalCancelBeforeDispatch=cancelBeforeDispatch;state.medicalReplanAt=now()+3000;state.status=reason;save(state);
  }
  function waitMedicalIdentity(state,started){
    // Refresh only before dispatch. A dispatched operation must retain its receipt/token.
    const elapsed=now()-Number(started||now());
    if(state.medicalDispatched){
      if(elapsed>60000)pauseState(state,'Account identity unavailable while verifying the previous medicine. No additional item used; resume after the sidebar returns.',true);
      return;
    }
    if(elapsed<15000)return;
    if(Number(state.medicalIdentityReloads||0)>=2){
      pauseState(state,'Account identity is still unavailable after two safe Medical page refreshes. No item consumed.',true);return;
    }
    state.medicalIdentityReloads=Number(state.medicalIdentityReloads||0)+1;
    state.medicalIdentityWaitAt=0;state.medicalIdentityLostAt=0;state.medicalIdentityRequest=null;
    state.stage='heal-open';state.healEnteredAt=0;state.medicalClickedAt=0;state.medicalInventorySignature='';state.medicalInventorySettledAt=0;
    state.status='Refreshing Medical to restore account identity ('+state.medicalIdentityReloads+'/2). No item clicked.';
    save(state);location.assign(ITEMS_URL);
  }
  function smartMedicalTick(state){
    if(state.stage==='smart-medical-identity'){
      if(!medicalPage()){pauseState(state,'Medical page changed while waiting for account identity.',true);return;}
      const identity=medicalIdentity(),owner=identity.owner;
      if(owner!==null){const request=state.medicalIdentityRequest;if(!request){pauseState(state,'Medical identity request was lost; stop and restart the worker.',true);return;}requestMedical(state,request.type,request.extra||{},identity);return;}
      waitMedicalIdentity(state,state.medicalIdentityWaitAt);return;
    }
    if(!medicalPage()){pauseState(state,'Medical page changed. No further item clicks allowed.',true);return;}
    const owner=medicalOwnId();
    if(owner===null){
      state.medicalIdentityLostAt=state.medicalIdentityLostAt||now();
      waitMedicalIdentity(state,state.medicalIdentityLostAt);if(!state.running)return;
      state.status='Waiting for the medical account identity to finish refreshing. No item clicked.';save(state);return;
    }
    state.medicalIdentityLostAt=0;state.medicalIdentityReloads=0;
    if(owner!==Number(state.playerId)){pauseState(state,'Medical page belongs to another account. No further item clicks allowed.',true);return;}
    if(state.stage==='medical-cooldown'){
      if(now()<state.medicalWakeAt)return;
      state.medicalOp=null;requestMedical(state,'tc-medical-plan');return;
    }
    if(state.stage==='smart-medical-await'){
      if(now()-state.medicalReplyAt>45000)pauseState(state,'Medical backend did not verify state within 45 seconds. No action was retried.',true);return;
    }
    const op=state.medicalOp;
    if(!op||!medicalNames[op.itemId]||!sameMedicalName(op.name,medicalNames[op.itemId])){
      pauseState(state,'Medical operation is missing or outside the allowlist. No item retry.',true);return;
    }
    if(!state.medicalDispatched&&now()>Date.parse(op.expires)){
      replanMedical(state,'Unused medical plan expired; refreshing before any click.');return;
    }
    if(state.stage==='smart-medical-ready'){
      if(medicalCount(op.itemId)!==op.quantity){replanMedical(state,'Medical stock changed before dispatch; recalculating without a click.');return;}
      const use=medicalUse(op.itemId);
      if(!use){
        if(!state.medicalExpandedAt){const header=medicalExpand(op.itemId);if(header){state.medicalExpandedAt=now();save(state);click(header);return;}}
        if(now()-Number(state.medicalExpandedAt||state.medicalReadyAt||state.medicalReplyAt)>15000)pauseState(state,'Selected medical item did not expose one verified Use control. No item consumed.',true);return;
      }
      requestMedical(state,'tc-medical-authorize',{token:op.token});return;
    }
    if(state.stage==='smart-medical-execute'){
      if(state.medicalDispatched){pauseState(state,'Medical item already dispatched. Await verification before another item.',true);return;}
      if(medicalCount(op.itemId)!==op.quantity){replanMedical(state,'Selected medical supply changed or ran out. Recalculating from remaining permitted supplies.',true);return;}
      const use=medicalUse(op.itemId);if(!use){replanMedical(state,'Unused medical control disappeared. Refreshing the remaining personal supplies.',true);return;}
      state.medicalDispatched=true;state.medicalUseAt=now();state.medicalConfirmed=false;state.stage='smart-medical-wait';save(state);
      if(!click(use))pauseState(state,'Medical click could not be dispatched; review before restarting.',true);return;
    }
    if(state.stage==='smart-medical-receipt-wait'){
      if(now()<state.medicalVerifyAt)return;requestMedical(state,'tc-medical-verify',{token:op.token});return;
    }
    if(state.stage==='smart-medical-wait'){
      let count=medicalCount(op.itemId);
      if(count===null&&op.quantity===1&&medicalVisible()&&!medicalRow(op.itemId))count=0; // Backend still requires cooldown AND hospital evidence.
      if(count===op.quantity-1){requestMedical(state,'tc-medical-verify',{token:op.token});return;}
      if(count!==null&&count!==op.quantity){pauseState(state,'Medical stock changed by an unexpected amount; no retry.',true);return;}
      const confirm=medicalConfirm(op.itemId);
      if(confirm&&!state.medicalConfirmed){state.medicalConfirmed=true;save(state);click(confirm);return;}
      if(now()-state.medicalUseAt>20000)pauseState(state,'Medical consumption not verified. No second item or drug fallback.',true);
    }
  }

  function firstAidRow() {
    const markers=Array.from(document.querySelectorAll('[data-item-id="67"],[data-id="67"],[data-item="67"],[data-itemid="67"]')).filter(visible);
    for(const leaf of document.querySelectorAll('span,div,strong,b,h3,h4,p'))if(visible(leaf)&&!leaf.children?.length&&clean(leaf.textContent)==='First Aid Kit')markers.push(leaf);
    const rows=[];
    for(const marker of markers){
      for(let node=marker,depth=0;node&&depth<8;node=node.parentElement,depth++){
        const text=clean(node.textContent);
        if(text.length>1500||/\bSmall First Aid Kit\b/i.test(text))break;
        const id=['data-item-id','data-item','data-itemid'].map(k=>node.getAttribute?.(k)).find(Boolean);
        if(id&&id!=='67')break;
        const structural=['li','tr'].includes(String(node.tagName||'').toLowerCase())||id==='67'||node.getAttribute?.('data-id')==='67';
        const hasUse=Array.from(node.querySelectorAll('button,a,[role="button"],[data-action]')).some(el=>/^(use|use item|use first aid kit)$/i.test(labelOf(el))||el.getAttribute?.('data-action')==='use');
        const others=Array.from(node.querySelectorAll('[data-item-id],[data-item],[data-itemid]')).some(el=>['data-item-id','data-item','data-itemid'].some(k=>{const v=el.getAttribute?.(k);return v&&v!=='67'}));
        if(others)break;
        if((structural||hasUse)&&(id==='67'||/\bFirst Aid Kit\b/i.test(text))){rows.push(node);break;}
      }
    }
    const roots=outerRoots(rows);
    return roots.length===1?roots[0]:null;
  }

  function firstAidExpandControl() {
    const row=firstAidRow();if(!row)return null;
    // An open or temporarily disabled item must not be toggled closed while its controls settle.
    if([row,...row.querySelectorAll('[aria-expanded]')].some(el=>el.getAttribute?.('aria-expanded')==='true'))return null;
    if(Array.from(row.querySelectorAll('button,a,[role="button"]')).some(el=>visible(el)&&
      /^(use|use item|use first aid kit)$/i.test(labelOf(el))&&(el.disabled||el.getAttribute?.('aria-disabled')==='true')))return null;
    // Click only the verified item's own name/image/header; never a generic page control.
    const names=Array.from(row.querySelectorAll('span,strong,b,h3,h4,a,button,div'))
      .filter(el=>visible(el)&&!el.children?.length&&/^first aid kit$/i.test(clean(el.textContent)));
    const expanded=Array.from(row.querySelectorAll('[aria-expanded]')).filter(el=>visible(el)&&el.getAttribute?.('aria-expanded')==='false');
    const own=expanded.filter(el=>/first aid kit/i.test(clean(el.textContent))&&!/small first aid kit/i.test(clean(el.textContent)));
    if(own.length===1)return own[0];
    // The item-name span is only a label in Torn's accordion layout.
    // Activate the verified row's tab/header, where the expand handler lives.
    const headers=Array.from(row.querySelectorAll('[role="tab"],[id*="ui-accordion-"]'))
      .filter(el=>visible(el)&&enabledControl(el,row.parentElement)&&
        (el.getAttribute?.('role')==='tab'||/-header-/.test(el.getAttribute?.('id')||''))&&
        /\bFirst Aid Kit\b/i.test(clean(el.textContent))&&!/\bSmall First Aid Kit\b/i.test(clean(el.textContent)));
    const headerRoots=outerRoots(headers);
    if(headerRoots.length)return headerRoots.length===1?headerRoots[0]:null;
    if(names.length===1)return names[0];
    const images=Array.from(row.querySelectorAll('img')).filter(el=>visible(el)&&
      (/^first aid kit$/i.test(clean(el.getAttribute?.('alt')||el.getAttribute?.('title')))||/\/items\/67(?:[/.?]|$)/i.test(el.getAttribute?.('src')||'')));
    return images.length===1?images[0]:null;
  }

  function aidCount() {
    const row=firstAidRow();if(!row)return null;
    const counts=[row,...row.querySelectorAll('[data-qty],[data-quantity]')];
    for(const node of counts)for(const key of ['data-qty','data-quantity']){
      const value=node.getAttribute?.(key);if(value!==null&&value!==undefined&&/^\d+$/.test(value))return Number(value);
    }
    const match=clean(row.textContent).match(/First Aid Kit\s*[xÃ—]\s*(\d+)\b/i);
    if(match)return Number(match[1]);
    const before=clean(row.textContent).match(/(?:^|\s)[xÃ—]\s*(\d+)\s*First Aid Kit\b/i);
    return before?Number(before[1]):null;
  }

  function firstAidUseButton() {
    const row = firstAidRow();
    if (!row) return null;
    const candidates = Array.from(row.querySelectorAll('button,a,[role="button"],[tabindex],label,span,div'))
      .filter(visible)
      .filter(el => enabledControl(el,row.parentElement));
    const exact = candidates.filter(el => {
      const label = labelOf(el);
      const action = clean(el.getAttribute?.('data-action') || '').toLowerCase();
      const cls = String(el.className || '').toLowerCase().replace(/[-_]/g, ' ');
      return /^(use|use item|use first aid kit)$/i.test(label) || /^use$/i.test(action) || (!label && /\b(use|check|consume)\b/.test(cls));
    });
    const unique = exact.filter((el, i) => !exact.some((other, j) => i !== j && other.contains?.(el) && other !== el));
    return unique.length === 1 ? unique[0] : (exact.length === 1 ? exact[0] : null);
  }

  function medicalTab() {
    const nodes = Array.from(document.querySelectorAll('button,a,[role="tab"],[role="button"],li'))
      .filter(visible)
      .filter(el => /^(medical|medicine|medical items)$/i.test(clean(el.getAttribute?.('title') || el.getAttribute?.('aria-label') || el.innerText || '')));
    const actions=nodes.filter(el=>['a','button'].includes(String(el.tagName||'').toLowerCase())||['tab','button'].includes(el.getAttribute?.('role')));
    const unique=(actions.length?actions:nodes).filter(a=>!(actions.length?actions:nodes).some(b=>b!==a&&a.contains?.(b)));
    return unique.length===1?unique[0]:null;
  }

  function medicalVisible() {
    const text = bodyText();
    return /your items\s*[-â€“â€”]\s*medical\b/i.test(text) || firstAidRow() !== null || (load()?.smartMedical&&medicalInventory().length>0);
  }

  function confirmUseButton() {
    // Confirm only inside a visible First Aid Kit dialog/inline confirmation.
    const possibleRoots = Array.from(document.querySelectorAll('[role="dialog"],[class*="dialog"],[class*="Dialog"],[class*="modal"],[class*="Modal"],[class*="confirm"],[class*="Confirm"]'))
      .filter(visible)
      .filter(el => /\bFirst Aid Kit\b/i.test(clean(el.textContent)) && !/\bSmall First Aid Kit\b/i.test(clean(el.textContent)));
    const roots = possibleRoots.length === 1 ? possibleRoots : [];
    if (!roots.length) {
      const buttons = Array.from(document.querySelectorAll('button,[role="button"],input[type="button"],a')).filter(visible);
      const anchored = buttons.filter(el => {
        if (!/^(okay|ok|confirm)$/i.test(clean(el.innerText || el.value || el.getAttribute?.('aria-label') || ''))) return false;
        let node = el;
        for (let d = 0; node && d < 7; d++, node = node.parentElement) {
          const text = clean(node.textContent);
          if (text.length > 900) continue;
          if (/\bFirst Aid Kit\b/i.test(text) && !/\bSmall First Aid Kit\b/i.test(text)) return true;
        }
        return false;
      });
      return anchored.length === 1 ? anchored[0] : null;
    }
    const buttons = Array.from(roots[0].querySelectorAll('button,[role="button"],input[type="button"],a'))
      .filter(visible)
      .filter(el => !el.disabled && el.getAttribute?.('aria-disabled') !== 'true')
      .filter(el => /^(okay|ok|confirm|use)$/i.test(clean(el.innerText || el.value || el.getAttribute?.('aria-label') || '')));
    return buttons.length === 1 ? buttons[0] : null;
  }


  function pauseState(state, reason, abandonClaim = false) {
    state.running = false;
    if (state.stage !== 'paused') state.previousStage = state.stage;
    state.stage = 'paused';
    state.pendingClaimRequest = '';
    state.status = reason;
    if (abandonClaim && state.claimId) {
      post({ type: 'tc-loss-abandon', profileId: state.profileId, slot: state.slot, orderId: state.orderId, claimId: state.claimId, reason });
      state.claimId = '';
    }
    return save(state);
  }

  function openPage(state,url,label,abandon=false) {
    const time=now();
    if(state.navigationUrl!==url){state.navigationUrl=url;state.navigationStartedAt=time;state.navigationAttempts=0;}
    if(time-state.navigationStartedAt>=30000){
      pauseState(state,label+' did not open within 30 seconds. Inspect the worker browser for login, verification or navigation errors.',abandon);return;
    }
    if(state.navigationAttempts&&time-Number(state.navigationLastAt||0)<15000)return;
    if(Number(state.navigationAttempts||0)>=2)return;
    state.navigationAttempts=Number(state.navigationAttempts||0)+1;state.navigationLastAt=time;
    state.status='Opening '+label+' ('+state.navigationAttempts+'/2).';save(state);
    try{location.assign(url)}catch(error){pauseState(state,'Navigation failed: '+error.message,abandon);}
  }
  function beginHealing(state) {
    state.stage='heal-open';
    state.healEnteredAt=0;state.aidExpandedAt=0;state.medicalClickedAt=0;state.medicalInventorySignature='';state.medicalInventorySettledAt=0;state.medicalContinueVerified=false;state.medicalCancelBeforeDispatch=false;state.medicalIdentityWaitAt=0;state.medicalIdentityLostAt=0;state.medicalIdentityReloads=0;state.medicalIdentityRequest=null;
    state.healClickedAt=0;state.healBeforeCount=null;state.confirmClicked=false;
  }
  function openAttack(state){openPage(state,ATTACK_URL(state.targetId),'attack page',true);}

  function levelingChoice(state){
    if(state.mode!=='level')return false;
    const dialogs=[...document.querySelectorAll('[role="dialog"],[class*="ui-dialog"]')].filter(visible);
    for(const dialog of dialogs){
      if(!/level/i.test(clean(dialog.textContent)))continue;
      const controls=[...dialog.querySelectorAll('button,a,input[type="button"],input[type="submit"]')].filter(visible);
      const label=el=>clean(el.textContent||el.value).toLowerCase();
      const hold=controls.filter(el=>/^(hold(?: (?:my |current )?level)?|remain (?:at )?(?:this|current) level)$/.test(label(el)));
      const accept=controls.filter(el=>/^(level up|upgrade(?: level)?|accept level(?: up)?)$/.test(label(el)));
      if(hold.length!==1||accept.length!==1)continue;
      if(medicalOwnId()!==Number(state.playerId)){pauseState(state,'Level-up choice needs verified account identity. No choice clicked.',false);return true;}
      if(state.levelChoiceAt){
        if(now()-state.levelChoiceAt>15000)pauseState(state,'Level-up choice did not close. Check the dialog; no repeated click.',false);
        return true;
      }
      const target=state.acceptLevelUps===true?accept[0]:hold[0];
      if(!enabledControl(target,dialog))return true;
      state.levelChoiceAt=now();state.status=state.acceptLevelUps?'Accepting the offered level-up.':'Holding the current level.';save(state);target.click();return true;
    }
    if(state.levelChoiceAt){state.levelChoiceAt=0;save(state);}
    return false;
  }

  async function tick() {
    if (busy) return;
    busy = true;
    try {
      let state = load();
      if (!state) return;
      report(state);
      if (!state.running) return;
      if(state.navigationUrl&&((state.navigationUrl===ITEMS_URL&&isItemsPage())||
          (state.navigationUrl!==ITEMS_URL&&isAttackPage(state.targetId)))){
        state.navigationUrl='';
        if(state.stage==='recovery-probe'||state.stage==='final-recovery')state.probeStartedAt=now();
        save(state);
      }

      const session = sessionStorage.getItem(SESSION_KEY);
      if (!session || session !== state.sessionToken) {
        state.running = false;
        state.previousStage = state.stage;
        state.stage = 'paused';
        state.status = 'Previous auto-cycle session detected after browser/app restart. Press Resume explicitly.';
        save(state);
        return;
      }

      if (challengeVisible()) {
        pauseState(state, 'Verification screen detected. Auto-cycle paused; resolve it manually before resuming.', state.stage !== 'fight');
        return;
      }
      if(levelingChoice(state))return;
      if(isItemsPage()&&/^TORN\s*[-\u2013\u2014]\s*50[0-4]\s*Error$/i.test(clean(document.title))){
        if(state.medicalDispatched){pauseState(state,'Torn returned a server error after an item dispatch. Resume only after the page recovers; no item will be repeated.',true);return;}
        state.medicalServerSince=state.medicalServerSince||now();
        if(now()-state.medicalServerSince<3000){state.status='Torn returned a server error. Waiting before a safe page reload; no item clicked.';save(state);return;}
        if(state.medicalServerLastAt&&now()-state.medicalServerLastAt<15000)return;
        if(Number(state.medicalServerAttempts||0)>=2){pauseState(state,'Torn medical page still returns a server error after two reloads. Try again later; no item consumed.',true);return;}
        state.medicalServerAttempts=Number(state.medicalServerAttempts||0)+1;state.medicalServerLastAt=now();state.status='Retrying Torn medical server error ('+state.medicalServerAttempts+'/2). No item clicked.';save(state);location.assign(ITEMS_URL);return;
      }
      if (now() - Number(state.startedAt || now()) > 12 * 60 * 60 * 1000) {
        pauseState(state, '12-hour auto-cycle safety limit reached. Review the order before resuming.', true);
        return;
      }

      if(state.mode==='level'&&state.stage==='level-target-wait'){if(now()<state.levelTargetWakeAt)return;state.stage='claim';state.status='A hospitalized target is due out. Rechecking availability.';save(state);return;}
      if(state.mode==='level'&&state.stage==='level-api-wait'){
        if(now()<state.levelApiWakeAt)return;
        state.stage=state.levelApiReturnStage;state.replyWaitAt=now();
        if(state.stage==='level-verify')state.levelVerifyAt=now();
        save(state);return;
      }
      if (state.stage === 'claim') {
        if (now() < Number(state.retryAt || 0)) return;
        const requestId = 'claim_' + now() + '_' + Math.random().toString(36).slice(2);
        state.claimAttempts = Number(state.claimAttempts || 0) + 1;
        state.replyWaitAt = now();
        state.pendingClaimRequest = requestId;
        state.stage = 'claim-wait';
        state.status = state.mode==='level'?'Checking target availability with Commander.':'Requesting the next loss slot from Commander.';
        save(state);
        post({ type: 'tc-loss-claim', requestId, profileId: state.profileId, slot: state.slot, orderId: state.orderId, targetId: state.targetId });
        return;
      }

      if(state.mode==='level'&&state.claimId&&['level-finish','level-verify'].includes(state.stage)&&levelingContinueControl())state.levelTerminalSeen=true;
      if(state.mode==='level'&&state.stage==='level-finish'){
        if(levelingContinueControl()){levelCommit(state,true);return;}
        const leave=levelingLeaveControl();
        if(leave){state.levelLeaveClicked=true;save(state);if(click(leave))levelCommit(state);else pauseState(state,'Leave control changed before dispatch. Review the fight.',false);return;}
        if(now()-state.levelFinishAt>20000)pauseState(state,'A single verified Leave control did not appear. Finish the fight manually, then Resume to verify its receipt.',false);return;
      }
      if(state.mode==='level'&&state.stage==='level-verify'){
        if(now()<state.levelVerifyAt)return;
        if(Number(state.commitAttempts||1)>=8){pauseState(state,'Official attack log did not update after eight checks. Resume verifies this same fight; no new attack will start.',false);return;}
        state.commitAttempts=Number(state.commitAttempts||1)+1;state.stage='commit-wait';state.replyWaitAt=now();save(state);post({type:'tc-loss-commit',profileId:state.profileId,orderId:state.orderId,claimId:state.claimId,terminal:!!state.levelTerminalSeen});return;
      }
      if (state.stage === 'claim-wait' || state.stage === 'commit-wait') {
        if (!state.replyWaitAt) { state.replyWaitAt=now(); save(state); return; }
        if (now()-state.replyWaitAt < (state.mode==='level'?60000:10000)) return;
        if (state.stage === 'claim-wait') {
          state.pendingClaimRequest='';
          if (Number(state.claimAttempts || 0) >= 3) {
            pauseState(state,'Commander did not acknowledge the slot after three requests. No attack started; check the worker connection.',true);
          } else {
            state.stage='claim'; state.retryAt=now(); state.status='Slot reply missing; retrying the same worker reservation.'; save(state);
          }
        } else {
          if (Number(state.commitAttempts || 1) >= 3) {
            pauseState(state,'Loss acknowledgement missing after three requests. The visible loss is retained for reconciliation; Resume retries this same claim, never a new attack.',false);
          } else {
            state.commitAttempts=Number(state.commitAttempts || 1)+1; state.replyWaitAt=now();
            state.status='Loss reply missing; replaying the same commit receipt without a new fight.'; save(state);
            post({type:'tc-loss-commit',profileId:state.profileId,slot:state.slot,orderId:state.orderId,claimId:state.claimId});
          }
        }
        return;
      }

      if (state.stage === 'attack-open') {
        if (!isAttackPage(state.targetId)) {
          openAttack(state);
          return;
        }
        if (hospitalBanner()) {
          if (Number(state.kitsThisLoss || 0) >= Number(state.maxKits || 4)) {
            pauseState(state, `Still hospitalized after ${state.kitsThisLoss || 0} First Aid Kits. Kit limit reached.`, true);
            return;
          }
          beginHealing(state);
          state.status = 'Hospital block detected. Opening Items for recovery.';
          save(state);
          openPage(state,ITEMS_URL,'Items / Medical');
          return;
        }
        if(state.mode==='level'&&/a mission is needed to be activated in order to attack this npc/i.test(bodyText())){const claimId=state.claimId;state.claimId='';state.stage='claim';state.pendingClaimRequest='';state.status='Mission-only NPC skipped. Looking for a player target.';save(state);post({type:'tc-loss-skip',profileId:state.profileId,orderId:state.orderId,claimId,reason:'Mission-only NPC'});return;}
        if(state.mode==='level'&&/(?:this player|this person|your target|the target) (?:is |is currently |has been )?(?:in (?:the )?hospital|in jail|traveling|travelling|abroad|unavailable|already in a fight)/i.test(bodyText())){
          const claimId=state.claimId;state.claimId='';state.stage='claim';state.pendingClaimRequest='';state.status='Target became unavailable. Skipping to the next account.';save(state);post({type:'tc-loss-skip',profileId:state.profileId,orderId:state.orderId,claimId});return;
        }
        if (insufficientEnergy()) { pauseForEnergy(state); return; }
        const join = joinFightButton();
        if (!join) {
          if(!state.attackWaitStartedAt){state.attackWaitStartedAt=now();state.status='Waiting for a verified Join Fight control.';save(state);}
          if (now() - state.attackWaitStartedAt > 60000) {
            pauseState(state, 'JOIN FIGHT was not found for 60 seconds. Torn layout may have changed.', true);
          }
          return;
        }
        state.attackWaitStartedAt = 0;
        state.stage = 'fight';
        state.fightStartedAt = now();
        state.lastPillowAt = 0;
        state.pillowTurns = 0;
        state.status = 'JOIN FIGHT clicked. Looking for the equipped Pillow.';
        save(state);
        click(join);
        return;
      }

      if (state.stage === 'fight') {
        if (!isAttackPage(state.targetId)) {
          pauseState(state, 'Attack page changed during the fight. Paused rather than guessing the result.', false);
          return;
        }
        if(state.mode==='level'&&levelingContinueControl()){levelCommit(state,true);return;}
        if(state.mode==='level'&&levelingLeaveControl(true)){state.stage='level-finish';state.levelFinishAt=now();state.status='Finishing choices are visible. Preparing Leave.';save(state);return;}
        const outcome = detectFightResult(state);
        if (outcome === 'loss') {
          state.stage = 'commit-wait';
          state.commitAttempts = 1;
          state.replyWaitAt = now();
          state.status = 'Visible loss detected. Committing exactly one loss to Commander.';
          save(state);
          post({ type: 'tc-loss-commit', profileId: state.profileId, slot: state.slot, orderId: state.orderId, claimId: state.claimId });
          return;
        }
        if (outcome === 'win'&&state.mode==='level'){state.stage='level-finish';state.levelFinishAt=now();state.status='Fight won. Looking for a single Leave control.';save(state);return;}
        if (outcome === 'win') {
          pauseState(state, 'Fight result was NOT a loss. Nothing was counted; review the order.', true);
          return;
        }
        if(state.mode==='level'&&hospitalBanner()){
          if(Number(state.pillowTurns||0)>0){levelCommit(state);return;}
          beginHealing(state);state.status='Hospitalized before combat began. Recovering before retrying this target.';save(state);openPage(state,ITEMS_URL,'Items / Medical');return;
        }
        if (insufficientEnergy()) { pauseForEnergy(state); return; }
        if (now() - Number(state.fightStartedAt || now()) > 9 * 60 * 1000) {
          pauseState(state, 'No clear fight result after nine minutes. Paused rather than guessing.', false);
          return;
        }
        if (Number(state.pillowTurns || 0) >= MAX_PILLOW_TURNS) {
          pauseState(state, state.mode==='level'?'100 combat actions reached without a verified result. Paused for review; no win counted.':'100 Pillow attempts reached without a visible result. Fight paused; check the combat page before resuming. Nothing was counted.', false);
          return;
        }
        if (now() - Number(state.lastPillowAt || 0) < attackDelay(state.attackDelayMs ?? 1900)) return;
        if(state.mode==='level'&&levelingEmptyGun(state)){pauseState(state,'Selected gun has no ammo. Equip ammunition or choose an equipped melee weapon; no empty-gun clicks or win counted.',false);return;}
        const pillow = state.mode==='level'?levelingWeapon(state):pillowControl();
        if (!pillow) {
          state.pillowMissingSince = state.pillowMissingSince || now();
          state.status = state.mode==='level'?'Selected fighting weapon is unavailable, empty, or ambiguous. Waiting for its verified attack control.':'Pillow control not found or not ready yet. Waiting for Torn combat UI to settle.';
          save(state);
          if (now() - state.pillowMissingSince > 45000) pauseState(state, state.mode==='level'?'Selected fighting weapon could not be verified for 45 seconds. Check equipment/ammo; no alternative weapon was clicked.':'Pillow control could not be identified for 45 seconds. Auto-cycle paused.', false);
          return;
        }
        if(state.mode==='level'&&/\breload\b/i.test(labelOf(pillow))){
          const signature=labelOf(pillow);
          if(state.levelReloadSignature===signature){if(now()-state.levelReloadAt>10000)pauseState(state,'Selected gun still shows Reload after ten seconds. No repeated reload click or win counted.',false);return;}
          state.levelReloadSignature=signature;state.levelReloadAt=now();save(state);
        }else if(state.mode==='level'){state.levelReloadSignature='';}
        if (!click(pillow)) {
          state.status = 'Pillow changed before the click. Rechecking the current weapon control.';
          save(state);
          return;
        }
        state.pillowMissingSince = 0;
        state.pillowTurns = Number(state.pillowTurns || 0) + 1;
        state.lastPillowAt = now();
        state.status = state.mode==='level'?`Leveling turn ${state.pillowTurns}; watching for the fight result.`:`Pillow turn ${state.pillowTurns}; watching for the visible loss result.`;
        save(state);
        return;
      }

      if(state.smartMedical&&(state.stage.startsWith('smart-medical-')||state.stage==='medical-cooldown')){smartMedicalTick(state);return;}
      if (state.stage === 'heal-open') {
        if (!isItemsPage()) {
          state.status = 'Opening Items / Medical.';
          save(state);
          openPage(state,ITEMS_URL,'Items / Medical');
          return;
        }
        if(!state.healEnteredAt){state.healEnteredAt=now();save(state);}
        if (!medicalVisible()) {
          if (!state.medicalClickedAt) {
            const tab = medicalTab();
            if (!tab) {
              if (now() - state.healEnteredAt > 45000) pauseState(state, 'Medical tab could not be identified. Auto-cycle paused before using any item.', true);
              return;
            }
            state.medicalClickedAt = now();
            state.status = 'Opening the Medical category.';
            save(state);
            click(tab);
            return;
          }
          if (now() - state.medicalClickedAt > 10000) {
            pauseState(state, 'Medical category did not open after the click. No medicine was consumed.', true);
          }
          return;
        }
        state.medicalClickedAt = 0;
        if(state.smartMedical){
          if(now()<Number(state.medicalReplanAt||0))return;
          const signature=JSON.stringify(medicalInventory());
          if(state.medicalInventorySignature!==signature){state.medicalInventorySignature=signature;state.medicalInventorySettledAt=now();save(state);return;}
          if(now()-Number(state.medicalInventorySettledAt||0)<1250)return;
          state.medicalInventorySignature='';state.medicalExpandedAt=0;if(!state.medicalOp)state.medicalDispatched=false;requestMedical(state,'tc-medical-plan');return;
        }
        if (Number(state.kitsThisLoss || 0) >= Number(state.maxKits || 4)) {
          pauseState(state, 'First Aid Kit limit reached. Review hospital time before continuing.', true);
          return;
        }
        const existingConfirm = confirmUseButton();
        if (existingConfirm && state.healClickedAt) {
          state.stage = 'heal-wait';
          save(state);
          return;
        }
        const use = firstAidUseButton();
        if (!use) {
          if(aidCount()===0){pauseState(state,'No First Aid Kits remain in the verified item row.',true);return;}
          if(state.aidExpandedAt){
            if(now()-state.aidExpandedAt>15000)pauseState(state,'First Aid Kit expansion was requested, but its Use control did not appear within 15 seconds. Copy the worker diagnostic; no item was consumed.',true);
            return;
          }
          const expand=firstAidExpandControl();
          if(expand){
            state.aidExpandedAt=now();
            state.status='Requested First Aid Kit expansion; waiting for its Use control.';
            save(state);click(expand);return;
          }
          if (now() - state.healEnteredAt > 65000) pauseState(state, 'First Aid Kit Use/checkmark or item header could not be identified. Auto-cycle paused before consuming anything.', true);
          return;
        }
        state.aidExpandedAt=0;
        state.healBeforeCount = aidCount();
        state.healClickedAt = now();
        state.confirmClicked = false;
        state.stage = 'heal-wait';
        state.status = `Using one First Aid Kit (${state.kitsThisLoss || 0}/${state.maxKits || 4} used for this recovery so far).`;
        save(state);
        click(use);
        return;
      }

      if (state.stage === 'heal-wait') {
        if (!isItemsPage()) {
          pauseState(state, 'Items page changed during healing. Paused to avoid double-using medicine.', true);
          return;
        }
        if (now() - Number(state.healClickedAt || now()) > 25000) {
          pauseState(state, 'First Aid Kit use was not visibly confirmed within 25 seconds. Paused to avoid double-using medicine.', true);
          return;
        }
        const confirm = confirmUseButton();
        if (confirm && !state.confirmClicked) {
          state.confirmClicked = true;
          state.status = 'Confirming First Aid Kit use.';
          save(state);
          click(confirm);
          return;
        }
        const current = aidCount();
        const before = state.healBeforeCount;
        if (before !== null && before !== undefined && current !== null && current < before) {
          if (current !== before - 1) {
            pauseState(state, 'First Aid Kit count changed by more than one. Paused for manual review.', true);
            return;
          }
          state.kitsThisLoss = Number(state.kitsThisLoss || 0) + 1;
          state.stage = 'recovery-probe';
          state.probeStartedAt = now();
          state.status = `One First Aid Kit consumed. Checking hospital status before the next attack.`;
          save(state);
          openPage(state,ATTACK_URL(state.targetId),'attack recovery page');
          return;
        }
        // Some inventory layouts hide the count. Once the confirmation disappears, probe the attack page rather than consuming another kit blindly.
        if (state.confirmClicked && !confirm && now() - Number(state.healClickedAt || now()) > 3500) {
          state.kitsThisLoss = Number(state.kitsThisLoss || 0) + 1;
          state.stage = 'recovery-probe';
          state.probeStartedAt = now();
          state.status = 'Item confirmation closed. Probing the attack page before deciding whether another kit is needed.';
          save(state);
          openPage(state,ATTACK_URL(state.targetId),'attack recovery page');
        }
        return;
      }

      if (state.stage === 'recovery-probe' || state.stage === 'final-recovery') {
        if (!isAttackPage(state.targetId)) {
          openPage(state,ATTACK_URL(state.targetId),'attack recovery page');
          return;
        }
        if (now() - Number(state.probeStartedAt || now()) < 1600) return;
        if (hospitalBanner()) {
          if (Number(state.kitsThisLoss || 0) >= Number(state.maxKits || 4)) {
            pauseState(state, `Still hospitalized after ${state.kitsThisLoss || 0} First Aid Kits. Kit limit reached.`, true);
            return;
          }
          beginHealing(state);
          state.medicalClickedAt = 0;
          state.status = 'Still hospitalized. Returning to Medical for one more First Aid Kit.';
          save(state);
          openPage(state,ITEMS_URL,'Items / Medical');
          return;
        }
        const join = joinFightButton();
        if (join || insufficientEnergy()) {
          if (state.finishAfterRecovery) {
            state.running = false;
            state.stage = 'complete';
            state.claimId = '';
            state.status = 'All requested losses are recorded and final recovery is confirmed.';
            save(state);
            post({ type: 'tc-loss-finished', profileId: state.profileId, slot: state.slot, orderId: state.orderId });
            return;
          }
          if(insufficientEnergy()){pauseForEnergy(state);return;}
          // Recovery before a fight still owns its reservation. Keep it until
          // a visible loss is committed or it is explicitly abandoned.
          state.stage = state.claimId ? 'attack-open' : 'claim';
          state.claimAttempts = 0;
          state.attackWaitStartedAt = 0;
          state.kitsThisLoss = 0;
          state.status = state.claimId ? 'Recovered. Continuing the reserved attack.' : 'Recovered. Requesting the next loss slot.';
          save(state);
          return;
        }
        state.recoveryProbe={elapsedMs:now()-Number(state.probeStartedAt||now()),readyState:document.readyState||'unknown',startControls:joinFightCandidates().length};
        if (now() - Number(state.probeStartedAt || now()) > 60000) {
          pauseState(state, 'Recovery page did not expose one verified Start Fight control within 60 seconds. No extra item was used. Copy worker diagnostic.', true);
        }
        return;
      }

      pauseState(state, `Unexpected auto-cycle state: ${state.stage}.`, true);
    } catch (e) {
      const state = load();
      if (state?.running) pauseState(state, `Auto-cycle error: ${String(e?.message || e).slice(0, 180)}`, true);
    } finally {
      busy = false;
    }
  }

  window.TornCommanderLossCycle = {
    start(config) {
      const token = 'sess_' + now() + '_' + Math.random().toString(36).slice(2);
      sessionStorage.setItem(SESSION_KEY, token);
      const state = {
        mode:config.mode==='level'?'level':'loss',acceptLevelUps:config.acceptLevelUps===true,weaponSlot:String(config.weaponSlot||'weapon_primary'),
        profileId: String(config.profileId || ''),
        slot: String(config.slot || ''),
        orderId: String(config.orderId || ''),
        targetId: String(config.targetId || ''),
        selfName: String(config.selfName || ''),
        smartMedical:config.smartMedical===true,playerId:Number(config.playerId||0),medicalSession:String(config.medicalSession||''),
        attackDelayMs: attackDelay(config.attackDelayMs ?? 1900),
        maxKits: Math.max(1, Math.min(8, Number(config.maxKits || 4))),
        running: true,
        stage: config.smartMedical===true?'heal-open':'claim',
        claimId: '',
        startedAt: now(),
        sessionToken: token,
        kitsThisLoss: 0,
        pillowTurns: 0,
        status: 'Auto-cycle armed. Requesting the first loss slot from Commander.'
      };
      save(state);
      return true;
    },
    setAttackDelay(milliseconds) {
      const value=Number(milliseconds);
      if(!Number.isFinite(value)||value<500||value>5000)return false;
      const state=load();if(!state)return false;
      state.attackDelayMs=attackDelay(value);save(state);return true;
    },
    pause() {
      const state = load();
      if (!state) return false;
      pauseState(state, 'Paused from Torn Commander.', false);
      return true;
    },
    resume() {
      const state = load();
      if (!state || state.running) return false;
      const token = sessionStorage.getItem(SESSION_KEY) || ('sess_' + now() + '_' + Math.random().toString(36).slice(2));
      sessionStorage.setItem(SESSION_KEY, token);
      state.sessionToken = token;
      state.running = true;
      const reconcile = (state.previousStage === 'commit-wait'||state.mode==='level'&&['level-verify','level-finish'].includes(state.previousStage)) && !!state.claimId;
      const resumeFight = state.claimId && state.previousStage === 'fight' && isAttackPage(state.targetId);
      // Resume the pending medical operation before requesting another attack.
      // A dispatched item is observed/verified only; it must never be clicked again.
      const resumeMedical=state.smartMedical&&(state.medicalOp||state.previousStage==='smart-medical-identity')&&String(state.previousStage||'').startsWith('smart-medical-');
      state.stage = resumeMedical ? (state.medicalDispatched?'smart-medical-wait':'heal-open') : reconcile ? 'commit-wait' : resumeFight ? 'fight' : state.claimId ? 'recovery-probe' : 'claim';
      if(resumeMedical&&!state.medicalDispatched){state.medicalIdentityWaitAt=0;state.medicalIdentityLostAt=0;state.medicalIdentityReloads=0;state.medicalIdentityRequest=null;state.medicalInventorySignature='';state.medicalExpandedAt=0;state.healEnteredAt=0;}

      if(resumeFight){state.pillowMissingSince=0;state.fightStartedAt=now();}
      state.claimAttempts=0; state.commitAttempts=1; state.replyWaitAt=now();
      state.pendingClaimRequest='';
      state.probeStartedAt = now();
      state.status = 'Resumed from Commander. Rechecking page state before the next action.';
      save(state);
      if(reconcile) post({type:'tc-loss-commit',profileId:state.profileId,slot:state.slot,orderId:state.orderId,claimId:state.claimId});
      return true;
    },
    stop() {
      const state = load();
      if (!state) return false;
      if (state.claimId) post({ type: 'tc-loss-abandon', profileId: state.profileId, slot: state.slot, orderId: state.orderId, claimId: state.claimId, reason: 'Stopped from Commander' });
      state.running = false;
      state.previousStage = state.stage;
      state.stage = 'stopped';
      state.pendingClaimRequest = '';
      state.claimId = '';
      state.status = 'Stopped from Torn Commander.';
      save(state);
      return true;
    },
    reset() { clear(); return true; },
    onClaim(requestId, ok, claimId, message) {
      const state = load();
      if (!state || !state.running || state.stage !== 'claim-wait' || state.pendingClaimRequest !== requestId) {
        // A late grant cannot wake a stopped/paused worker. Release only when no
        // active request/fight could still own the same reservation.
        if(ok && claimId && (!state || !state.running) && state?.claimId !== claimId)
          post({type:'tc-loss-abandon',profileId:state?.profileId || '',slot:state?.slot || '',orderId:state?.orderId || '',claimId,reason:'Late grant after stop/pause'});
        return false;
      }
      state.claimAttempts = 0;
      state.pendingClaimRequest = '';
      if (!ok) {
        if (/complete|finished|no remaining/i.test(String(message || ''))) {
          state.running = false;
          state.stage = 'complete';
          state.status = message || 'No losses remain.';
          save(state);if(state.mode==='level')post({type:'tc-loss-finished',profileId:state.profileId,orderId:state.orderId});
          return true;
        }
        if (/paused|no longer exists|assigned to/i.test(String(message || ''))) {
          pauseState(state,message || 'Order cannot run.',false); return true;
        }
        state.stage = 'claim';
        state.retryAt = now() + 1800;
        state.status = message || 'Loss slot is busy; waiting to retry.';
        save(state);
        return true;
      }
      if (!claimId) { pauseState(state,'Commander granted an empty claim. No attack started.',false); return false; }
      state.claimId = String(claimId || '');state.levelTerminalSeen=false;state.levelReloadSignature='';
      state.kitsThisLoss = 0;
      state.healClickedAt=0;state.healBeforeCount=null;state.confirmClicked=false;state.aidExpandedAt=0;
      state.stage = 'attack-open';
      state.navigationUrl='';
      state.status = 'Loss slot granted. Opening target attack page.';
      save(state);
      if (!isAttackPage(state.targetId)) openAttack(state);
      return true;
    },
    onCommit(claimId, ok, remaining, message) {
      const state = load();
      if (!state || !state.running || state.stage !== 'commit-wait' || state.claimId !== claimId) return false;
      if (!ok) {
        pauseState(state, message || 'Commander rejected the loss commit. Paused for review.', false);
        return true;
      }
      state.claimId = '';
      state.commitAttempts = 0;
      state.replyWaitAt = 0;
      state.finishAfterRecovery = Number(remaining || 0) <= 0;
      state.stage = state.mode==='level'?'heal-open':state.finishAfterRecovery ? 'final-recovery' : 'heal-open';
      state.probeStartedAt = now();
      state.healEnteredAt = 0; state.aidExpandedAt = 0;
      state.medicalClickedAt = 0;
      state.healClickedAt=0;state.healBeforeCount=null;state.confirmClicked=false;state.aidExpandedAt=0;
      state.kitsThisLoss = 0;
      state.status = state.finishAfterRecovery
        ? 'Final loss committed. Verifying recovery before completing the order.'
        : `Loss committed. ${remaining} remaining; opening Items to recover.`;
      save(state);
      if (state.finishAfterRecovery&&state.mode!=='level') openPage(state,ATTACK_URL(state.targetId),'attack recovery page');
      else openPage(state,ITEMS_URL,'Items / Medical');
      return true;
    },
    onLevelReply(reply){
      const state=load();if(!state?.running||state.mode!=='level')return false;
      if(reply.kind==='api-wait'){
        const claimWait=state.stage==='claim-wait'&&state.pendingClaimRequest===reply.requestId;
        const commitWait=['commit-wait','level-verify'].includes(state.stage)&&state.claimId===reply.claimId;
        if(!claimWait&&!commitWait)return false;
        const at=Date.parse(reply.waitUntil);if(!Number.isFinite(at))return false;
        state.levelApiReturnStage=claimWait?'claim':'level-verify';state.levelApiWakeAt=Math.max(now()+1000,at);
        state.stage='level-api-wait';state.status=reply.message;save(state);return true;
      }
      if(reply.kind==='paused'){pauseState(state,reply.message||'Leveling verification failed.',false);return true;}
      if(reply.kind==='wait'){if(state.stage!=='claim-wait'||state.pendingClaimRequest!==reply.requestId)return false;const at=Date.parse(reply.waitUntil);if(!Number.isFinite(at)){pauseState(state,'Invalid target availability time.',false);return false;}state.stage='level-target-wait';state.levelTargetWakeAt=Math.max(now()+1000,at);state.status=reply.message;save(state);return true;}
      if(reply.kind==='scan'){if(state.stage!=='claim-wait'||state.pendingClaimRequest!==reply.requestId)return false;state.replyWaitAt=now();state.status=reply.message;save(state);return true;}
      if(reply.kind==='claim'||reply.kind==='heal'){
        if(state.stage!=='claim-wait'||state.pendingClaimRequest!==reply.requestId)return false;
        if(reply.kind==='heal'){beginHealing(state);state.pendingClaimRequest='';save(state);openPage(state,ITEMS_URL,'Items / Medical');return true;}
        if(reply.ok){if(!Number.isSafeInteger(Number(reply.targetId))||Number(reply.targetId)<=0||Number(reply.targetId)===state.playerId){pauseState(state,'Invalid leveling target grant.',false);return false;}state.targetId=String(reply.targetId);save(state);}
        return window.TornCommanderLossCycle.onClaim(reply.requestId,!!reply.ok,reply.claimId||'',reply.message||'');
      }
      if(state.claimId!==reply.claimId||!['commit-wait','level-verify'].includes(state.stage))return false;
      if(reply.kind==='pending'){state.stage='level-verify';state.levelVerifyAt=now()+12000;state.status=reply.message;save(state);return true;}
      if(reply.kind==='commit'){state.stage='commit-wait';if(reply.stop)state.levelStopMessage=reply.message;save(state);return window.TornCommanderLossCycle.onCommit(reply.claimId,true,reply.remaining,reply.message);}
      return false;
    },
    onMedical(requestId,reply){
      const state=load();if(!medicalPage()||!state?.running||!state.smartMedical||state.stage!=='smart-medical-await'||state.medicalRequest!==requestId)return false;
      if(!state.medicalDispatched&&/^TORN\s*[-\u2013\u2014]\s*50[0-4]\s*Error$/i.test(clean(document.title))&&reply.state!=='Ready'&&reply.state!=='Authorized'){state.stage='heal-open';state.status='Torn medical server error; waiting for page recovery without an item click.';save(state);return true;}
      if(reply.state==='Ready'){
        if(!medicalNames[reply.itemId]||!sameMedicalName(reply.name,medicalNames[reply.itemId])){pauseState(state,'Backend medical item is outside the fixed allowlist.',true);return false;}
        state.medicalVerification=null;state.medicalOp=reply;state.medicalDispatched=false;state.medicalExpandedAt=0;state.medicalReadyAt=now();
        if(!reply.automatic){pauseState(state,'Recommendation only: '+reply.name+'. Enable automatic consumption explicitly in Smart Medical Management to use it.',true);return true;}
        state.stage='smart-medical-ready';state.status='Plan: '+reply.name+'. Verifying personal inventory and account.';save(state);return true;
      }
      if(reply.state==='Authorized'&&state.medicalOp?.token===reply.token&&state.medicalOp.itemId===reply.itemId){state.medicalOp.expires=reply.expires;state.stage='smart-medical-execute';save(state);return true;}
      if(reply.verification)state.medicalVerification=reply.verification;
      if(reply.state==='PendingVerification'&&state.medicalDispatched&&state.medicalOp?.token===reply.token){state.stage='smart-medical-receipt-wait';state.medicalVerifyAt=now()+Math.max(2000,Math.min(10000,Number(reply.retryAfterMs)||10000));state.status=reply.reason;save(state);return true;}
      if(reply.state==='Verified'&&state.medicalOp?.token===reply.token&&state.mode==='level'){state.medicalOp=null;state.medicalDispatched=false;state.medicalInventorySignature='';state.medicalReplans=0;beginHealing(state);state.medicalContinueVerified=true;save(state);return true;}
      if(reply.state==='Verified'&&state.medicalOp?.token===reply.token){state.medicalOp=null;state.medicalDispatched=false;state.medicalReplans=0;state.stage='recovery-probe';state.probeStartedAt=now();state.status='Medical quantity, cooldown and hospital changes verified. Rechecking recovery.';save(state);openPage(state,ATTACK_URL(state.targetId),'attack recovery page');return true;}
      if(reply.state==='Replan'){replanMedical(state,reply.reason||'Recalculating medical plan before dispatch.');return true;}
      if(reply.state==='Recovered'&&state.mode==='level'){finishLevelingMedical(state);return true;}
      if(reply.state==='Recovered'){state.stage='recovery-probe';state.probeStartedAt=now();save(state);openPage(state,ATTACK_URL(state.targetId),'attack recovery page');return true;}
      if(reply.state==='Cooldown'&&reply.autoResume&&Number.isFinite(Date.parse(reply.waitUntil))){
        if(state.claimId){post({type:'tc-loss-abandon',profileId:state.profileId,slot:state.slot,orderId:state.orderId,claimId:state.claimId,reason:'Medical cooldown waiting'});state.claimId='';}
        state.stage='medical-cooldown';state.medicalWakeAt=Math.max(now()+1000,Date.parse(reply.waitUntil));state.status='Medical cooldown/reserve wait until '+new Date(state.medicalWakeAt).toLocaleTimeString()+'. Drugs permanently disabled.';save(state);return true;
      }
      pauseState(state,(reply.reason||'Medical state could not be verified.')+(reply.waitUntil?' Available after '+new Date(reply.waitUntil).toLocaleString():''),true);return true;
    },
    diagnostics() {
      const summary=el=>el?{tag:el.tagName,id:el.getAttribute?.('id'),disabled:Boolean(el.disabled)||el.getAttribute?.('aria-disabled')==='true',className:String(el.className||''),role:el.getAttribute?.('role'),label:labelOf(el).slice(0,100)}:null;
      return {medicalIdentityWaitAt:load()?.medicalIdentityWaitAt,medicalIdentityLostAt:load()?.medicalIdentityLostAt,medicalIdentityReloads:load()?.medicalIdentityReloads,levelTerminalSeen:load()?.levelTerminalSeen,levelContinue:summary(levelingContinueControl()),levelEmptyGun:levelingEmptyGun(load()||{}),medicalServerAttempts:load()?.medicalServerAttempts,healEnteredAt:load()?.healEnteredAt,medicalClickedAt:load()?.medicalClickedAt,medicalWakeAt:load()?.medicalWakeAt,medicalVisible:medicalVisible(),recoveryProbe:load()?.recoveryProbe,joinCandidates:joinFightCandidates().map(summary),medicalProfileId:load()?.profileId,medicalExpectedPlayerId:load()?.playerId,medicalIdentity:medicalIdentity(),mode:load()?.mode,weaponSlot:load()?.weaponSlot,medicalSession:load()?.medicalSession,medicalPlannedQuantity:load()?.medicalOp?.quantity,medicalVerification:load()?.medicalVerification,medicalItem:load()?.medicalOp?.itemId,medicalOwner:medicalOwnId(),medicalInventory:medicalInventory(),medicalDispatched:load()?.medicalDispatched,pillowTurns:Number(load()?.pillowTurns||0),previousStage:load()?.previousStage,stage:load()?.stage,status:load()?.status,path:location.pathname,
        playerAreas:Array.from(document.querySelectorAll('[class*="playerArea"]')).filter(visible).map(el=>String(el.className||'')),
        combatAreas:Array.from(document.querySelectorAll('[class*="playerArea"]')).filter(visible).map(area=>({id:area.getAttribute?.('id'),className:String(area.className||''),controls:Array.from(area.querySelectorAll('[id="weapon_melee"],button,[role="button"]')).filter(visible).map(summary).slice(0,12)})),pillow:summary(pillowControl(false)),firstAidRow:summary(firstAidRow()),firstAidUse:summary(firstAidUseButton()),firstAidExpand:summary(firstAidExpandControl()),attackDelayMs:attackDelay(load()?.attackDelayMs ?? 1900),medicalTab:summary(medicalTab()),confirmation:summary(confirmUseButton())};
    },
    pump() { void tick(); return true; },
    getState() { return load(); }
  };

  const prior = load();
  if (prior?.running) {
    const session = sessionStorage.getItem(SESSION_KEY);
    if (!session || session !== prior.sessionToken) {
      prior.running = false;
      prior.previousStage = prior.stage;
      prior.stage = 'paused';
      prior.status = 'Previous auto-cycle session detected after browser/app restart. Press Resume explicitly.';
      save(prior);
    }
  }

  setInterval(tick, TICK_MS);
  setTimeout(tick, 250);
})();
