(() => {
  'use strict';
  if (window.__tcLossAutoInstalled) return;
  window.__tcLossAutoInstalled = true;

  const STORAGE_KEY = '__tc_loss_auto_cycle_v1';
  const SESSION_KEY = '__tc_loss_auto_cycle_session_v1';
  const ATTACK_URL = id => `https://www.torn.com/page.php?sid=attack&user2ID=${encodeURIComponent(id)}`;
  const ITEMS_URL = 'https://www.torn.com/item.php';
  const MAX_PILLOW_TURNS = 20;
  const TICK_MS = 900;
  let busy = false;

  const now = () => Date.now();
  const clean = s => String(s || '').replace(/\s+/g, ' ').trim();
  const bodyText = () => clean(document.body?.innerText || '');
  const visible = el => Boolean(el && el.getClientRects?.().length && getComputedStyle(el).visibility !== 'hidden' && getComputedStyle(el).display !== 'none');
  const click = el => {
    if (!el || !visible(el) || el.disabled || el.getAttribute?.('aria-disabled') === 'true') return false;
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
    return /you can(?:not|'t|’t) attack while in hospital/i.test(bodyText());
  }

  function joinFightButton() {
    const buttons = Array.from(document.querySelectorAll('button,[role="button"],input[type="button"],a'))
      .filter(visible)
      .filter(el => !el.disabled && el.getAttribute?.('aria-disabled') !== 'true');
    const exact = buttons.filter(el => /^(join fight|start fight|start attack|begin fight)$/i.test(clean(el.innerText || el.value || el.getAttribute?.('aria-label') || '')));
    return exact.length === 1 ? exact[0] : null;
  }

  function pillowControl() {
    // Scope Pillow selection to Torn's own player weapon area. If the layout exposes
    // multiple possible player areas, fail closed rather than risk clicking a defender control.
    const areas = Array.from(document.querySelectorAll('[class*="playerArea"]')).filter(visible);
    if (areas.length !== 1) return null;
    const area = areas[0];
    const markers = Array.from(area.querySelectorAll('[data-item-id="440"],[data-id="440"],[data-itemid="440"],[alt],[title],[aria-label],[data-tooltip],[data-tippy-content]'))
      .filter(visible)
      .filter(el => ['data-item-id','data-id','data-itemid'].some(k => el.getAttribute?.(k) === '440') ||
        /^pillow(?:\s*\([^)]*\))?$/i.test(clean(el.getAttribute?.('alt') || el.getAttribute?.('title') || el.getAttribute?.('aria-label') || el.getAttribute?.('data-tooltip') || el.getAttribute?.('data-tippy-content') || '')));

    for (const leaf of area.querySelectorAll('span,div,strong,p')) {
      if (leaf.children?.length === 0 && clean(leaf.textContent) === 'Pillow' && visible(leaf)) markers.push(leaf);
    }

    const candidates = [];
    for (const marker of markers) {
      let node = marker;
      for (let depth = 0; node && depth < 6; depth++, node = node.parentElement) {
        if (node === area) break;
        if (!visible(node)) continue;
        const tag = String(node.tagName || '').toLowerCase();
        const role = node.getAttribute?.('role') || '';
        const cls = String(node.className?.baseVal ?? node.className ?? '');
        if ((tag === 'button' || role === 'button' || /weapon|melee|slot|equipment/i.test(cls)) && !node.disabled && node.getAttribute?.('aria-disabled') !== 'true') {
          if (!candidates.includes(node)) candidates.push(node);
          break;
        }
      }
      if (marker !== area && visible(marker) && typeof marker.click === 'function' && !candidates.includes(marker)) candidates.push(marker);
    }
    if (candidates.length === 1) return candidates[0];
    if (candidates.length > 1) {
      const roots = candidates.filter(a => !candidates.some(b => a !== b && b.contains?.(a)));
      return roots.length === 1 ? roots[0] : null;
    }
    return null;
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
    const direct = Array.from(document.querySelectorAll('[data-item-id="67"],[data-id="67"],[data-item="67"]')).filter(visible);
    if (direct.length === 1) return direct[0];
    const leaves = Array.from(document.querySelectorAll('span,div,strong,b,h3,h4,p'))
      .filter(el => visible(el) && el.children.length === 0 && clean(el.textContent) === 'First Aid Kit');
    const rows = [];
    for (const leaf of leaves) {
      let node = leaf;
      for (let d = 0; node && d < 8; d++, node = node.parentElement) {
        const txt = clean(node.textContent);
        if (txt.length > 600) continue;
        if (/\bSmall First Aid Kit\b/i.test(txt)) continue;
        if (/\bFirst Aid Kit(?:\s*[x×]\s*\d+)?\b/i.test(txt)) {
          if (!rows.includes(node)) rows.push(node);
          break;
        }
      }
    }
    return rows.length === 1 ? rows[0] : null;
  }

  function aidCount() {
    const raw = String(document.body?.innerText || '');
    const m = raw.match(/(?:^|\n)\s*First Aid Kit\s*[x×]\s*(\d+)\b/im);
    return m ? Number(m[1]) : null;
  }

  function firstAidUseButton() {
    const row = firstAidRow();
    if (!row) return null;
    const candidates = Array.from(row.querySelectorAll('button,a,[role="button"],[tabindex],label,span,div'))
      .filter(visible)
      .filter(el => !el.disabled && el.getAttribute?.('aria-disabled') !== 'true');
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
    return nodes.length === 1 ? nodes[0] : null;
  }

  function medicalVisible() {
    const text = bodyText();
    return /your items\s*[-–—]\s*medical\b/i.test(text) || firstAidRow() !== null;
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
        if (!/^(okay|ok|confirm|use)$/i.test(clean(el.innerText || el.value || el.getAttribute?.('aria-label') || ''))) return false;
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

  async function tick() {
    if (busy) return;
    busy = true;
    try {
      let state = load();
      if (!state) return;
      report(state);
      if (!state.running) return;

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
          state.status = 'Opening target attack page.';
          save(state);
          location.assign(ATTACK_URL(state.targetId));
          return;
        }
        if (hospitalBanner()) {
          if (Number(state.kitsThisLoss || 0) >= Number(state.maxKits || 4)) {
            pauseState(state, `Still hospitalized after ${state.kitsThisLoss || 0} First Aid Kits. Kit limit reached.`, true);
            return;
          }
          state.stage = 'heal-open';
          state.healEnteredAt = 0;
          state.status = 'Hospital block detected. Opening Items for recovery.';
          save(state);
          location.assign(ITEMS_URL);
          return;
        }
        const join = joinFightButton();
        if (!join) {
          state.attackWaitStartedAt = state.attackWaitStartedAt || now();
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
        if (now() - Number(state.fightStartedAt || now()) > 9 * 60 * 1000) {
          pauseState(state, 'No clear fight result after nine minutes. Paused rather than guessing.', false);
          return;
        }
        if (Number(state.pillowTurns || 0) >= MAX_PILLOW_TURNS) {
          state.status = 'Pillow turn limit reached; waiting for the visible fight result.';
          save(state);
          return;
        }
        if (now() - Number(state.lastPillowAt || 0) < 1900) return;
        const pillow = pillowControl();
        if (!pillow) {
          state.pillowMissingSince = state.pillowMissingSince || now();
          state.status = 'Pillow control not found yet. Waiting for Torn combat UI to settle.';
          save(state);
          if (now() - state.pillowMissingSince > 45000) pauseState(state, 'Pillow control could not be identified for 45 seconds. Auto-cycle paused.', false);
          return;
        }
        state.pillowMissingSince = 0;
        state.pillowTurns = Number(state.pillowTurns || 0) + 1;
        state.lastPillowAt = now();
        state.status = `Pillow turn ${state.pillowTurns}; watching for the visible loss result.`;
        save(state);
        click(pillow);
        return;
      }

      if (state.stage === 'heal-open') {
        if (!isItemsPage()) {
          state.status = 'Opening Items / Medical.';
          save(state);
          location.assign(ITEMS_URL);
          return;
        }
        state.healEnteredAt = state.healEnteredAt || now();
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
          if (now() - state.healEnteredAt > 65000) pauseState(state, 'First Aid Kit Use/checkmark could not be identified. Auto-cycle paused before consuming anything.', true);
          return;
        }
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
          location.assign(ATTACK_URL(state.targetId));
          return;
        }
        // Some inventory layouts hide the count. Once the confirmation disappears, probe the attack page rather than consuming another kit blindly.
        if (state.confirmClicked && !confirm && now() - Number(state.healClickedAt || now()) > 3500) {
          state.kitsThisLoss = Number(state.kitsThisLoss || 0) + 1;
          state.stage = 'recovery-probe';
          state.probeStartedAt = now();
          state.status = 'Item confirmation closed. Probing the attack page before deciding whether another kit is needed.';
          save(state);
          location.assign(ATTACK_URL(state.targetId));
        }
        return;
      }

      if (state.stage === 'recovery-probe' || state.stage === 'final-recovery') {
        if (!isAttackPage(state.targetId)) {
          location.assign(ATTACK_URL(state.targetId));
          return;
        }
        if (now() - Number(state.probeStartedAt || now()) < 1600) return;
        if (hospitalBanner()) {
          if (Number(state.kitsThisLoss || 0) >= Number(state.maxKits || 4)) {
            pauseState(state, `Still hospitalized after ${state.kitsThisLoss || 0} First Aid Kits. Kit limit reached.`, true);
            return;
          }
          state.stage = 'heal-open';
          state.healEnteredAt = 0;
          state.medicalClickedAt = 0;
          state.status = 'Still hospitalized. Returning to Medical for one more First Aid Kit.';
          save(state);
          location.assign(ITEMS_URL);
          return;
        }
        const join = joinFightButton();
        if (join || /\bsolo attack\b/i.test(bodyText())) {
          if (state.finishAfterRecovery) {
            state.running = false;
            state.stage = 'complete';
            state.claimId = '';
            state.status = 'All requested losses are recorded and final recovery is confirmed.';
            save(state);
            post({ type: 'tc-loss-finished', profileId: state.profileId, slot: state.slot, orderId: state.orderId });
            return;
          }
          state.stage = 'claim';
          state.claimAttempts = 0;
          state.claimId = '';
          state.kitsThisLoss = 0;
          state.status = 'Recovered. Requesting the next loss slot.';
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
      state.stage = reconcile ? 'commit-wait' : state.claimId ? 'recovery-probe' : 'claim';
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
      state.stage = 'attack-open';
      state.status = 'Loss slot granted. Opening target attack page.';
      save(state);
      if (!isAttackPage(state.targetId)) location.assign(ATTACK_URL(state.targetId));
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
      state.healEnteredAt = 0;
      state.medicalClickedAt = 0;
      state.kitsThisLoss = 0;
      state.status = state.finishAfterRecovery
        ? 'Final loss committed. Verifying recovery before completing the order.'
        : `Loss committed. ${remaining} remaining; opening Items to recover.`;
      save(state);
      if (state.finishAfterRecovery) location.assign(ATTACK_URL(state.targetId));
      else location.assign(ITEMS_URL);
      return true;
    },
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
