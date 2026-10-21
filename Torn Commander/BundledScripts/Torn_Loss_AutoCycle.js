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
    try { window.chrome?.webview?.postMessage(JSON.stringify(payload)); } catch {}
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
    return /you can(?:not|'t|â€™t) attack while in (?:the )?hospital|you are (?:currently |still )?in (?:the )?hospital/i.test(bodyText());
  }

  function insufficientEnergy() {
    // Use Torn's explicit refusal, never infer a balance from unrelated page numbers.
    return /you (?:do not|don't|donâ€™t) have (?:enough|sufficient) energy|you have (?:insufficient|not enough) energy|not enough energy to (?:attack|start|join)|you need (?:at least )?25 energy to (?:attack|start|join)/i.test(bodyText());
  }
  function pauseForEnergy(state) {
    return pauseState(state,'Not enough energy to attack. This account is paused and its slot released; the other running account can continue. Resume this account after restoring energy.',true);
  }

  function joinFightButton() {
    const buttons = Array.from(document.querySelectorAll('button,[role="button"],input[type="button"],a'))
      .filter(visible)
      .filter(el => !el.disabled && el.getAttribute?.('aria-disabled') !== 'true');
    const exact = buttons.filter(el => /^(join fight|start fight|start attack|begin fight)$/i.test(clean(el.innerText || el.value || el.getAttribute?.('aria-label') || '')));
    return exact.length === 1 ? exact[0] : null;
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
    return /your items\s*[-â€“â€”]\s*medical\b/i.test(text) || firstAidRow() !== null;
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
    state.healEnteredAt=0;state.aidExpandedAt=0;state.medicalClickedAt=0;
    state.healClickedAt=0;state.healBeforeCount=null;state.confirmClicked=false;
  }
  function openAttack(state){openPage(state,ATTACK_URL(state.targetId),'attack page',true);}

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
      if (now() - Number(state.startedAt || now()) > 12 * 60 * 60 * 1000) {
        pauseState(state, '12-hour auto-cycle safety limit reached. Review the order before resuming.', true);
        return;
      }

      if (state.stage === 'claim') {
        if (now() < Number(state.retryAt || 0)) return;
        const requestId = 'claim_' + now() + '_' + Math.random().toString(36).slice(2);
        state.claimAttempts = Number(state.claimAttempts || 0) + 1;
        state.replyWaitAt = now();
        state.pendingClaimRequest = requestId;
        state.stage = 'claim-wait';
        state.status = 'Requesting the next loss slot from Commander.';
        save(state);
        post({ type: 'tc-loss-claim', requestId, profileId: state.profileId, slot: state.slot, orderId: state.orderId, targetId: state.targetId });
        return;
      }

      if (state.stage === 'claim-wait' || state.stage === 'commit-wait') {
        if (!state.replyWaitAt) { state.replyWaitAt=now(); save(state); return; }
        if (now()-state.replyWaitAt < 10000) return;
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
        if (outcome === 'win') {
          pauseState(state, 'Fight result was NOT a loss. Nothing was counted; review the order.', true);
          return;
        }
        if (insufficientEnergy()) { pauseForEnergy(state); return; }
        if (now() - Number(state.fightStartedAt || now()) > 9 * 60 * 1000) {
          pauseState(state, 'No clear fight result after nine minutes. Paused rather than guessing.', false);
          return;
        }
        if (Number(state.pillowTurns || 0) >= MAX_PILLOW_TURNS) {
          pauseState(state, '100 Pillow attempts reached without a visible result. Fight paused; check the combat page before resuming. Nothing was counted.', false);
          return;
        }
        if (now() - Number(state.lastPillowAt || 0) < attackDelay(state.attackDelayMs ?? 1900)) return;
        const pillow = pillowControl();
        if (!pillow) {
          state.pillowMissingSince = state.pillowMissingSince || now();
          state.status = 'Pillow control not found or not ready yet. Waiting for Torn combat UI to settle.';
          save(state);
          if (now() - state.pillowMissingSince > 45000) pauseState(state, 'Pillow control could not be identified for 45 seconds. Auto-cycle paused.', false);
          return;
        }
        if (!click(pillow)) {
          state.status = 'Pillow changed before the click. Rechecking the current weapon control.';
          save(state);
          return;
        }
        state.pillowMissingSince = 0;
        state.pillowTurns = Number(state.pillowTurns || 0) + 1;
        state.lastPillowAt = now();
        state.status = `Pillow turn ${state.pillowTurns}; watching for the visible loss result.`;
        save(state);
        return;
      }

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
        if (now() - Number(state.probeStartedAt || now()) > 15000) {
          pauseState(state, 'Recovery state could not be verified on the attack page. No extra item was used.', true);
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
        profileId: String(config.profileId || ''),
        slot: String(config.slot || ''),
        orderId: String(config.orderId || ''),
        targetId: String(config.targetId || ''),
        selfName: String(config.selfName || ''),
        attackDelayMs: attackDelay(config.attackDelayMs ?? 1900),
        maxKits: Math.max(1, Math.min(8, Number(config.maxKits || 4))),
        running: true,
        stage: 'claim',
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
      const reconcile = state.previousStage === 'commit-wait' && !!state.claimId;
      const resumeFight = state.claimId && state.previousStage === 'fight' && isAttackPage(state.targetId);
      state.stage = reconcile ? 'commit-wait' : resumeFight ? 'fight' : state.claimId ? 'recovery-probe' : 'claim';
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
          save(state);
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
      state.claimId = String(claimId || '');
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
      state.stage = state.finishAfterRecovery ? 'final-recovery' : 'heal-open';
      state.probeStartedAt = now();
      state.healEnteredAt = 0; state.aidExpandedAt = 0;
      state.medicalClickedAt = 0;
      state.healClickedAt=0;state.healBeforeCount=null;state.confirmClicked=false;state.aidExpandedAt=0;
      state.kitsThisLoss = 0;
      state.status = state.finishAfterRecovery
        ? 'Final loss committed. Verifying recovery before completing the order.'
        : `Loss committed. ${remaining} remaining; opening Items to recover.`;
      save(state);
      if (state.finishAfterRecovery) openPage(state,ATTACK_URL(state.targetId),'attack recovery page');
      else openPage(state,ITEMS_URL,'Items / Medical');
      return true;
    },
    diagnostics() {
      const summary=el=>el?{tag:el.tagName,id:el.getAttribute?.('id'),disabled:Boolean(el.disabled)||el.getAttribute?.('aria-disabled')==='true',className:String(el.className||''),role:el.getAttribute?.('role'),label:labelOf(el).slice(0,100)}:null;
      return {pillowTurns:Number(load()?.pillowTurns||0),previousStage:load()?.previousStage,stage:load()?.stage,status:load()?.status,path:location.pathname,
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
