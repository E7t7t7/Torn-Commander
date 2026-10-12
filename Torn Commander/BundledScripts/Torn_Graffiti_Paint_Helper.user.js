// ==UserScript==
// @name         Torn Commander - Graffiti Paint Helper
// @namespace    torn.commander
// @version      1.6.1
// @description  Shows paint charge percentages and supports Torn Commander guided one-action Graffiti setup/submission when the user explicitly presses the guide button.
// @match        https://www.torn.com/page.php?sid=crimes*
// @match        https://www.torn.com/loader.php?sid=crimes*
// @grant        none
// ==/UserScript==

(() => {
    'use strict';

    const COLORS = ['Black','Red','Pink','Purple','Blue','Green','White','Orange'];
    const TARGETS = ['East Side','West Side','North Side','Residential District','Red-Light District','Financial District','City Centre'];
    const ITEM_TO_COLOR = {
        856:'Black', 857:'Red', 858:'Pink', 859:'Purple',
        860:'Blue', 861:'Green', 862:'White', 863:'Orange'
    };
    const STORE_KEY = 'tcGraffitiPaintStateV1';
    const POS_KEY = 'tcGraffitiPaintHelperPositionV1';
    const state = loadState();
    let dirty = false;
    let lastScan = 0;
    let guideBusy = false;
    let overlayCollapsed = false;
    let lastOverlayMarkup = '';
    let lastGuideStatus = 'Idle. Guided actions select one district and one paint only.';
    let lastGuideResult = 'idle';
    let lastGuideTime = null;

    function loadState() {
        try {
            return JSON.parse(localStorage.getItem(STORE_KEY) || '{}') || {};
        } catch { return {}; }
    }

    function saveState() {
        try { localStorage.setItem(STORE_KEY, JSON.stringify(state)); } catch {}
    }

    function setPaint(color, pct, source = '', active = false) {
        if (!COLORS.includes(color)) return;
        const n = Number(pct);
        if (!Number.isFinite(n) || n < 0 || n > 100) return;
        const rounded = Math.round(n * 10) / 10;

        // Active paint is exclusive. Older builds could leave a previously-active can
        // marked active in cached state, which made guided actions prefer the wrong can.
        if (active) {
            for (const c of COLORS) {
                if (c !== color && state[c]?.active) {
                    state[c].active = false;
                    dirty = true;
                }
            }
        }

        const prev = state[color];
        // A non-selected duplicate DOM node for the same paint must not erase a genuine
        // active marker seen elsewhere. Active is cleared only when another color is
        // positively identified as active above.
        const nextActive = active ? true : !!prev?.active;
        if (!prev || prev.pct !== rounded || prev.active !== nextActive || prev.source !== source) {
            state[color] = { pct: rounded, source, active: nextActive, seen: Date.now() };
            dirty = true;
        }
    }

    function textPercent(text) {
        if (!text) return null;
        const m = String(text).match(/(?:^|\D)(100|\d{1,2})(?:\.\d+)?\s*%/);
        return m ? Number(m[1]) : null;
    }

    function colorFromText(text) {
        const s = String(text || '').toLowerCase();
        return COLORS.find(c => s.includes(c.toLowerCase())) || null;
    }

    function isGraffitiPage() {
        const href = location.href.toLowerCase();
        const body = (document.body?.innerText || '').toLowerCase();
        return href.includes('sid=crimes') && (body.includes('graffiti') || href.includes('graffiti'));
    }

    function nearbyText(el) {
        let out = '';
        let node = el;
        for (let i = 0; node && i < 3; i++, node = node.parentElement) {
            out += ' ' + (node.innerText || node.textContent || '');
            if (out.length > 800) break;
        }
        return out;
    }

    function attrsText(el) {
        const pieces = [];
        try {
            for (const a of el.attributes || []) pieces.push(`${a.name}=${a.value}`);
        } catch {}
        if (el.tagName === 'IMG') pieces.push(el.alt || '', el.title || '', el.src || '');
        return pieces.join(' ');
    }

    function scanVisibleDom() {
        if (!isGraffitiPage()) return;
        const els = document.querySelectorAll('button, [role="button"], img, [title], [aria-label], [data-item], [data-itemid], [data-id], li, [class*="paint" i], [class*="spray" i]');
        let count = 0;
        for (const el of els) {
            if (++count > 2500) break;
            const combined = `${attrsText(el)} ${nearbyText(el)}`.replace(/\s+/g, ' ');
            const color = colorFromText(combined);
            if (!color) continue;
            const pct = textPercent(combined);
            if (pct != null) {
                const active = el.getAttribute?.('aria-selected') === 'true'
                    || el.getAttribute?.('aria-checked') === 'true'
                    || /selected|active|equipped|chosen/i.test(String(el.className || ''));
                setPaint(color, pct, 'page', active);
            }
        }

        // A broad text pass catches layouts where the percentage is rendered in a sibling
        // far enough away that the element-level pass misses it.
        const text = (document.body?.innerText || '').replace(/\s+/g, ' ');
        for (const color of COLORS) {
            const patterns = [
                new RegExp(`(?:Spray\\s*Paint\\s*:?\\s*)?${color}[^%]{0,90}?(100|\\d{1,2})(?:\\.\\d+)?\\s*%`, 'i'),
                new RegExp(`(100|\\d{1,2})(?:\\.\\d+)?\\s*%[^%]{0,90}?(?:Spray\\s*Paint\\s*:?\\s*)?${color}`, 'i')
            ];
            for (const re of patterns) {
                const m = text.match(re);
                if (m) { setPaint(color, Number(m[1]), 'page-text'); break; }
            }
        }
    }

    function primitiveNumber(obj, keys) {
        for (const key of keys) {
            const value = obj?.[key];
            if (typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 100) return value;
            if (typeof value === 'string' && /^\d{1,3}(?:\.\d+)?%?$/.test(value.trim())) {
                const n = Number(value.replace('%',''));
                if (n >= 0 && n <= 100) return n;
            }
        }
        return null;
    }

    function inspectObjectTree(root, source = 'page-data') {
        const seen = new WeakSet();
        let visited = 0;
        function walk(value, depth, parentColor = null) {
            if (visited++ > 12000 || depth > 7 || value == null) return;
            if (typeof value !== 'object') return;
            if (seen.has(value)) return;
            seen.add(value);

            let color = parentColor;
            let id = null;
            let active = false;
            try {
                id = Number(value.item_id ?? value.itemId ?? value.id ?? value.ID ?? NaN);
                if (ITEM_TO_COLOR[id]) color = ITEM_TO_COLOR[id];
                const nameish = [value.name, value.item_name, value.itemName, value.title, value.label, value.color, value.colour]
                    .filter(v => typeof v === 'string').join(' ');
                color = colorFromText(nameish) || color;
                active = Boolean(value.selected ?? value.active ?? value.equipped ?? value.chosen ?? false);

                if (color) {
                    const pct = primitiveNumber(value, [
                        'percentage','percent','pct','remaining','remaining_percent','remainingPercent',
                        'charges','charge','charge_percent','chargePercent','durability','condition','capacity','fill'
                    ]);
                    if (pct != null) setPaint(color, pct, source, active);
                }
            } catch {}

            let entries;
            try { entries = Object.entries(value); } catch { return; }
            for (const [k,v] of entries) {
                if (['return','child','sibling','stateNode','ownerDocument','parentNode','nextSibling','previousSibling'].includes(k)) continue;
                if (typeof v === 'object' && v != null) walk(v, depth + 1, color);
            }
        }
        walk(root, 0, null);
    }

    function scanReactInternals() {
        if (!isGraffitiPage()) return;
        const roots = [document.body, ...document.querySelectorAll('main, #react-root, #root, [class*="crime" i], [class*="graffiti" i]')];
        let inspected = 0;
        for (const el of roots) {
            if (!el || inspected++ > 120) break;
            let keys = [];
            try { keys = Object.keys(el); } catch {}
            for (const key of keys) {
                if (key.startsWith('__reactProps$') || key.startsWith('__reactFiber$') || key.startsWith('__reactContainer$')) {
                    try { inspectObjectTree(el[key], 'react'); } catch {}
                }
            }
        }

        for (const key of ['__NEXT_DATA__','__APOLLO_STATE__','__INITIAL_STATE__','initialState','store','reduxStore']) {
            try { if (window[key]) inspectObjectTree(window[key], `window.${key}`); } catch {}
        }
    }

    function inspectPayload(payload, source) {
        try {
            if (payload && typeof payload === 'object') inspectObjectTree(payload, source);
            const text = typeof payload === 'string' ? payload : JSON.stringify(payload);
            if (!text || text.length > 4_000_000) return;
            for (const color of COLORS) {
                const m1 = text.match(new RegExp(`(?:Spray\\s*Paint\\s*:?\\s*)?${color}.{0,220}?(?:remaining|percent(?:age)?|pct|charge(?:s)?)\\D{0,15}(100|\\d{1,2})(?:\\.\\d+)?`, 'i'));
                const m2 = text.match(new RegExp(`(?:remaining|percent(?:age)?|pct|charge(?:s)?)\\D{0,15}(100|\\d{1,2})(?:\\.\\d+)?.{0,220}?(?:Spray\\s*Paint\\s*:?\\s*)?${color}`, 'i'));
                const m = m1 || m2;
                if (m) setPaint(color, Number(m[1]), source);
            }
        } catch {}
    }

    function hookNetwork() {
        if (window.__tcGraffitiNetworkHooked) return;
        window.__tcGraffitiNetworkHooked = true;

        try {
            const realFetch = window.fetch;
            window.fetch = async function(...args) {
                const response = await realFetch.apply(this, args);
                try {
                    const clone = response.clone();
                    clone.text().then(t => {
                        if (/paint|graffiti|856|857|858|859|860|861|862|863/i.test(t)) {
                            try { inspectPayload(JSON.parse(t), 'fetch-json'); }
                            catch { inspectPayload(t, 'fetch-text'); }
                            render();
                        }
                    }).catch(() => {});
                } catch {}
                return response;
            };
        } catch {}

        try {
            const realOpen = XMLHttpRequest.prototype.open;
            const realSend = XMLHttpRequest.prototype.send;
            XMLHttpRequest.prototype.open = function(method, url, ...rest) {
                this.__tcUrl = String(url || '');
                return realOpen.call(this, method, url, ...rest);
            };
            XMLHttpRequest.prototype.send = function(...args) {
                this.addEventListener('load', function() {
                    try {
                        const t = typeof this.responseText === 'string' ? this.responseText : '';
                        if (/paint|graffiti|856|857|858|859|860|861|862|863/i.test(t)) {
                            try { inspectPayload(JSON.parse(t), 'xhr-json'); }
                            catch { inspectPayload(t, 'xhr-text'); }
                            render();
                        }
                    } catch {}
                }, { once: true });
                return realSend.apply(this, args);
            };
        } catch {}
    }

    function activeColor() {
        return COLORS.find(c => state[c]?.active) || null;
    }

    function recommendation() {
        const found = COLORS.filter(c => state[c] && Number.isFinite(state[c].pct));
        if (!found.length) return 'Scanning the loaded Graffiti page for paint charge data…';
        const active = activeColor();
        if (active) {
            const pct = state[active].pct;
            if (pct <= 15) return `Finish ${active} (${fmt(pct)} left). A successful spray can consume up to ~15%, so this can may empty soon.`;
            return `Keep using ${active} (${fmt(pct)} left). Switching an allocated can discards its remaining charge.`;
        }
        const lowest = [...found].sort((a,b) => state[a].pct - state[b].pct)[0];
        const highest = [...found].sort((a,b) => state[b].pct - state[a].pct)[0];
        if (state[lowest].pct <= 15) return `Charge-only suggestion: finish ${lowest} (${fmt(state[lowest].pct)} left) before opening another can.`;
        return `No active paint detected. Charge-only suggestion: avoid swapping a partly used allocated can; if choosing fresh, ${highest} has the most charge (${fmt(state[highest].pct)}).`;
    }

    function fmt(n) { return `${Math.round(Number(n) * 10) / 10}%`; }

    function createUi() {
        if (document.getElementById('tc-graffiti-paint-helper')) return;
        const style = document.createElement('style');
        style.textContent = `
#tc-graffiti-paint-helper{position:fixed;right:8px;top:140px;width:clamp(205px,35vw,385px);max-width:calc(100vw - 16px);max-height:calc(100dvh - 16px);z-index:2147483000;background:#12171d;color:#e8edf2;border:1px solid #42505f;border-radius:10px;box-shadow:0 8px 26px rgba(0,0,0,.5);font:clamp(11px,1.2vw,13px)/1.35 Arial,sans-serif;overflow:hidden;display:flex;flex-direction:column}
#tc-gph-head{display:flex;align-items:center;gap:5px;padding:7px 8px;background:#1b232c;cursor:move;user-select:none;border-bottom:1px solid #34414e;flex:0 0 auto}
#tc-gph-head strong{flex:1;min-width:0;color:#55b8ff;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.tc-gph-btn{border:1px solid #4a5a6a;background:#202a35;color:#dce5ee;border-radius:5px;padding:2px 6px;cursor:pointer;flex:0 0 auto}.tc-gph-btn:hover{background:#2a3744}
#tc-gph-body{padding:8px;min-width:0;min-height:0;overflow-y:auto;overflow-x:hidden}.tc-gph-row{display:grid;grid-template-columns:minmax(52px,23%) minmax(0,1fr) minmax(42px,17%);gap:5px;align-items:center;margin:5px 0;min-width:0}.tc-gph-track{height:8px;background:#2b333d;border-radius:6px;overflow:hidden}.tc-gph-fill{height:100%;background:#5cb9ff}.tc-gph-active .tc-gph-fill{background:#74dd88}.tc-gph-pct{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.tc-gph-missing{opacity:.45}.tc-gph-rec{margin-top:8px;padding:7px;background:#19222b;border-radius:6px;border-left:3px solid #58b9ff;overflow-wrap:anywhere}.tc-gph-note{opacity:.72;font-size:10px;margin-top:6px;overflow-wrap:anywhere}.tc-gph-debug{white-space:pre-wrap;font-size:11px;max-height:180px;overflow:auto;margin-top:8px;background:#0c1015;padding:6px;border-radius:5px;display:none}
#tc-graffiti-paint-helper.tc-gph-collapsed #tc-gph-body{display:none}
@media(max-width:480px){#tc-gph-head{padding:5px 6px}#tc-gph-body{padding:5px}#tc-gph-head strong{font-size:11px}.tc-gph-row{grid-template-columns:minmax(49px,28%) 1fr minmax(40px,18%);gap:4px}}
`;
        document.head.appendChild(style);

        const root = document.createElement('div');
        root.id = 'tc-graffiti-paint-helper';
        root.innerHTML = `<div id="tc-gph-head"><strong>Commander • Paint</strong><button class="tc-gph-btn" id="tc-gph-collapse" title="Collapse paint window">−</button><button class="tc-gph-btn" id="tc-gph-refresh" title="Refresh">↻</button><button class="tc-gph-btn" id="tc-gph-debugbtn">Debug</button></div><div id="tc-gph-body"></div>`;
        document.body.appendChild(root);
        makeDraggable(root, root.querySelector('#tc-gph-head'));
        root.querySelector('#tc-gph-refresh').addEventListener('click', () => { scanAll(true); });
        root.querySelector('#tc-gph-collapse').addEventListener('click', () => { overlayCollapsed = !overlayCollapsed; root.classList.toggle('tc-gph-collapsed', overlayCollapsed); root.querySelector('#tc-gph-collapse').textContent = overlayCollapsed ? '+' : '−'; clampOverlay(root); });
        root.querySelector('#tc-gph-debugbtn').addEventListener('click', copyDebug);
        render();
    }

    function render() {
        if (!isGraffitiPage()) {
            const old = document.getElementById('tc-graffiti-paint-helper');
            if (old) old.style.display = 'none';
            return;
        }
        createUi();
        const root = document.getElementById('tc-graffiti-paint-helper');
        if (!root) return;
        root.style.display = '';
        const body = root.querySelector('#tc-gph-body');
        const rows = COLORS.map(color => {
            const rec = state[color];
            const pct = rec?.pct;
            const cls = rec ? (rec.active ? 'tc-gph-row tc-gph-active' : 'tc-gph-row') : 'tc-gph-row tc-gph-missing';
            const width = rec ? Math.max(0, Math.min(100, pct)) : 0;
            return `<div class="${cls}"><span>${color}${rec?.active ? ' ✓' : ''}</span><div class="tc-gph-track"><div class="tc-gph-fill" style="width:${width}%"></div></div><span class="tc-gph-pct">${rec ? fmt(pct) : '—'}</span></div>`;
        }).join('');
        const content = rows + `<div class="tc-gph-rec"><b>Suggestion</b><br>${escapeHtml(recommendation())}</div><div class="tc-gph-rec" id="tc-gph-guide-status"><b>Guided selection</b><br>${escapeHtml(lastGuideStatus)}${lastGuideTime ? `<div class="tc-gph-note">Last update: ${escapeHtml(lastGuideTime)}</div>` : ''}</div><div class="tc-gph-note">Percentages are remaining paint charge, not success chance. No guessing or automatic retries after a failed guided selection.</div>`;
        // Replacing identical HTML causes MutationObserver to wake repeatedly, even with
        // no Torn changes. Avoid modifying our own overlay unless its display has changed.
        if (content !== lastOverlayMarkup) {
            body.innerHTML = content;
            lastOverlayMarkup = content;
        }
        if (dirty) { saveState(); dirty = false; }
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
    }

    function clampOverlay(root) {
        if (!root) return;
        const margin = 8;
        const maxLeft = Math.max(margin, window.innerWidth - root.offsetWidth - margin);
        const maxTop = Math.max(margin, window.innerHeight - root.offsetHeight - margin);
        const currentLeft = root.getBoundingClientRect().left;
        const currentTop = root.getBoundingClientRect().top;
        if (currentLeft < margin || currentLeft > maxLeft) {
            root.style.left = `${Math.max(margin, Math.min(maxLeft, currentLeft))}px`;
            root.style.right = 'auto';
        }
        if (currentTop < margin || currentTop > maxTop)
            root.style.top = `${Math.max(margin, Math.min(maxTop, currentTop))}px`;
    }

    function makeDraggable(root, handle) {
        try {
            const saved = JSON.parse(localStorage.getItem(POS_KEY) || 'null');
            if (saved && Number.isFinite(saved.leftRatio) && Number.isFinite(saved.topRatio)) {
                root.style.left = `${Math.round(saved.leftRatio * window.innerWidth)}px`;
                root.style.top = `${Math.round(saved.topRatio * window.innerHeight)}px`;
                root.style.right = 'auto';
            } else if (saved && Number.isFinite(saved.left) && Number.isFinite(saved.top)) {
                root.style.left = `${saved.left}px`; root.style.top = `${saved.top}px`; root.style.right = 'auto';
            }
        } catch {}
        let moving = false, dx = 0, dy = 0;
        handle.addEventListener('mousedown', e => {
            if (e.target.closest('button')) return;
            moving = true; dx = e.clientX - root.offsetLeft; dy = e.clientY - root.offsetTop;
            root.style.right = 'auto'; e.preventDefault();
        });
        window.addEventListener('mousemove', e => {
            if (!moving) return;
            const x = Math.max(8, Math.min(window.innerWidth - root.offsetWidth - 8, e.clientX - dx));
            const y = Math.max(8, Math.min(window.innerHeight - root.offsetHeight - 8, e.clientY - dy));
            root.style.left = `${x}px`;
            root.style.top = `${y}px`;
        });
        window.addEventListener('mouseup', () => {
            if (!moving) return;
            moving = false;
            clampOverlay(root);
            try { localStorage.setItem(POS_KEY, JSON.stringify({ leftRatio: root.offsetLeft / Math.max(1, window.innerWidth), topRatio: root.offsetTop / Math.max(1, window.innerHeight) })); } catch {}
        });
        window.addEventListener('resize', () => clampOverlay(root));
        requestAnimationFrame(() => clampOverlay(root));
    }

    function getSafeDebug() {
        const controlList = [...document.querySelectorAll('button,[role="button"],[role="radio"],[role="option"],label,input[type="radio"],[aria-label],[title],[data-testid]')]
            .filter(el => isInCrimeContent(el) && (TARGETS.some(t => candidateText(el).toLowerCase().includes(t.toLowerCase())) || COLORS.some(t => candidateText(el).toLowerCase().includes(t.toLowerCase()))))
            .slice(0, 60).map(el => ({
                tag:el.tagName, text:candidateText(el).slice(0, 90),
                attrs:attrsText(el).slice(0, 280),
                parentTag:el.parentElement?.tagName || null,
                parentClass:String(el.parentElement?.className || '').slice(0, 100),
                selected:isSelectedElement(el)
            }));
        return {
            helperVersion:'1.6.1', route:location.pathname + location.search + location.hash,
            selectionStatus:lastGuideStatus, actionState:lastGuideResult,
            graffitiRoute:/#\/?graffiti(?:$|[/?])/i.test(location.hash || ''),
            districtCandidateCount:findGuideCandidates('Residential District','target').length,
            paintCandidateCount:findGuideCandidates('Purple','paint').length,
            targetMatches:debugGuideCandidates('Residential District','target'),
            paintMatches:debugGuideCandidates('Purple','paint'),
            selectedDistricts:selectedTargetEvidence(), selectedPaints:selectedPaintEvidence(),
            controls:controlList
        };
    }

    async function copyDebug() {
        scanAll(true);
        const text = JSON.stringify(getSafeDebug(), null, 2);
        try {
            await navigator.clipboard.writeText(text);
            alert('Graffiti selection diagnostics copied. Paste them into ChatGPT (no Spray was submitted).');
        } catch {
            console.log('Torn Commander graffiti debug', text);
            alert('Clipboard blocked. Use Commander Coach > Copy Graffiti Debug instead.');
        }
    }

    function scanAll(force = false) {
        const now = Date.now();
        if (!force && now - lastScan < 650) return;
        lastScan = now;
        if (!isGraffitiPage()) { render(); return; }
        scanVisibleDom();
        scanReactInternals();
        render();
    }

    function notifyCommander(message, result = 'info') {
        lastGuideStatus = String(message).replace(/^Graffiti\s*/i, '').slice(0, 360);
        lastGuideResult = result;
        lastGuideTime = new Date().toLocaleTimeString();
        render();
        try { window.chrome?.webview?.postMessage(`Graffiti [${result}]: ${message}`); } catch {}
    }

    function isVisible(el) {
        if (!el || el.closest?.('#tc-graffiti-paint-helper')) return false;
        try {
            const style = getComputedStyle(el);
            const rect = el.getBoundingClientRect();
            return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity || 1) > 0 && rect.width > 2 && rect.height > 2;
        } catch { return false; }
    }

    function candidateText(el) {
        const bits = [
            el.innerText, el.textContent, el.getAttribute?.('aria-label'), el.getAttribute?.('title'),
            el.getAttribute?.('alt'), el.getAttribute?.('value'), el.getAttribute?.('data-name'),
            el.getAttribute?.('data-title'), String(el.className || '')
        ].filter(Boolean).join(' ');
        return bits.replace(/\s+/g, ' ').trim();
    }

    function isSelectedElement(el) {
        if (!el) return false;
        // Selected state must belong to this *specific* option. A shared active
        // district/paint panel is NOT proof that its children are all selected.
        let cur = el;
        for (let depth = 0; cur && depth < 3; depth++, cur = cur.parentElement) {
            if (!isVisible(cur)) return false;
            const content = (cur.innerText || '').replace(/\s+/g,' ').toLowerCase();
            const options = [...TARGETS, ...COLORS].filter(x => content.includes(x.toLowerCase()));
            if (options.length > 1) break;
            const classes = String(cur.className || '');
            const stateText = `${cur.getAttribute?.('data-state') || ''} ${cur.getAttribute?.('data-status') || ''}`;
            if (cur.getAttribute?.('aria-selected') === 'true'
                || cur.getAttribute?.('aria-checked') === 'true'
                || cur.getAttribute?.('data-selected') === 'true'
                || cur.getAttribute?.('data-active') === 'true'
                || /(?:^|[\s_-])(selected|chosen|equipped|checked)(?:[\s_-]|$)/i.test(classes)
                || /(?:selected|checked|chosen)/i.test(stateText)
                || (depth <= 1 && options.length === 1 &&
                    (/(?:^|[\s_-])(active|current)(?:[\s_-]|$)/i.test(classes)
                     || /^(active|current)$/i.test(stateText.trim())))) return true;
        }
        return false;
    }

    function allAttributeText(el) {
        try {
            return [...(el.attributes || [])].map(a => `${a.name} ${a.value}`).join(' ');
        } catch { return ''; }
    }

    function interactiveFromNode(el) {
        if (!el) return null;
        const selector = 'button,[role="button"],a,label,[tabindex],input[type="button"],input[type="radio"],input[type="checkbox"]';
        if (el.matches?.(selector)) return el;
        let cur = el.parentElement;
        for (let i = 0; cur && i < 7; i++, cur = cur.parentElement) {
            if (cur.matches?.(selector)) return cur;
            if (/button|clickable|option|choice|target|location|district|area/i.test(String(cur.className || ''))) return cur;
        }
        return el.querySelector?.(selector) || null;
    }


    function isSafeCrimeControl(el) {
        if (!el) return false;
        const anchor = el.matches?.('a[href]') ? el : el.closest?.('a[href]');
        if (!anchor) return true;
        const raw = String(anchor.getAttribute('href') || '').trim();
        if (!raw || raw === '#' || raw.startsWith('#/')) return true;
        try {
            const u = new URL(raw, location.href);
            if (!/(^|\.)torn\.com$/i.test(u.hostname)) return false;
            const sid = u.searchParams.get('sid');
            return sid === 'crimes' && (/\/(loader|page)\.php$/i.test(u.pathname));
        } catch {
            return false;
        }
    }

    // Guided crime actions must not use page-wide fuzzy matching. A parent spanning
    // the whole crime page often contains every district/color and can click the wrong
    // top navigation or the default first option.
    function guideLabel(node) {
        const direct = [...(node.childNodes || [])]
            .filter(n => n.nodeType === 3).map(n => n.textContent || '').join(' ');
        const attrs = ['aria-label','title','alt','data-name','data-title','data-testid']
            .map(k => node.getAttribute?.(k) || '').join(' ');
        const text = [direct, attrs, node.tagName === 'BUTTON' ? node.innerText : '']
            .join(' ').replace(/\s+/g, ' ').trim();
        return text.slice(0, 200);
    }

    function isInCrimeContent(el) {
        if (!isVisible(el) || !/#\/?graffiti(?:$|[/?])/i.test(location.hash || '')) return false;
        if (el.closest?.('#tc-graffiti-paint-helper,header,nav,aside,[role="navigation"],footer')) return false;
        if (el.closest?.('a[href]')) return false; // never follow links while selecting
        if (el.closest?.('[aria-hidden="true"],[inert]')) return false;
        return true;
    }

    function optionLabelMatches(text, label, mode) {
        const clean = x => String(x || '').toLowerCase().replace(/[^a-z0-9]+/g,' ').trim();
        const wanted = clean(label);
        const t = clean(text);
        if (!t || t.length > 100 || !wanted) return false;
        const aliases = mode === 'target' ? targetAliases(label).map(clean) : [wanted, clean('Spray Paint : '+label), clean(label+' spray paint')];
        if (!aliases.some(a => a && (t === a || t.startsWith(a + ' ') && t.length - a.length <= 32))) return false;
        // A control showing several options is a container, not a selectable card.
        const options = mode === 'target' ? TARGETS : COLORS;
        const other = options.filter(v => clean(v) !== wanted && clean(v) !== clean(label.replace(/ District$/i,'')));
        if (other.some(x => (' ' + t + ' ').includes(' ' + clean(x) + ' '))) return false;
        return true;
    }

    function optionControlFromNode(node) {
        let cur = node;
        for (let n = 0; cur && n < 5; n++, cur = cur.parentElement) {
            if (!isInCrimeContent(cur)) return null;
            if (cur.matches?.('button,[role="button"],label,input[type="radio"],[role="radio"],[role="option"],input[type="checkbox"]')) return cur;
            const classes = String(cur.className || '');
            if (n > 0 && /(^|[\s_-])(option|card|district|area|location|paint|color|colour|choice|selector)([\s_-]|$)/i.test(classes)) return cur;
            if (n > 0 && cur.hasAttribute?.('data-testid') && /paint|color|district|location|area/i.test(cur.getAttribute('data-testid'))) return cur;
        }
        return null;
    }

    function findGuideCandidates(label, mode) {
        const out = new Map();
        const nodes = document.querySelectorAll('button,[role="button"],label,[role="option"],[role="radio"],input[type="radio"],img,[aria-label],[title],[data-testid],span,p,div');
        let seen = 0;
        for (const node of nodes) {
            if (++seen > 6000) break;
            if (!isInCrimeContent(node)) continue;
            const own = guideLabel(node);
            if (!optionLabelMatches(own, label, mode)) continue;
            const control = optionControlFromNode(node);
            if (!control || !isInCrimeContent(control) || control.disabled || control.getAttribute?.('aria-disabled') === 'true') continue;
            const controlText = (control.innerText || '').replace(/\s+/g,' ').trim();
            if (controlText.length > 170) continue;
            const others = mode === 'paint' ? COLORS : TARGETS;
            if (others.filter(x => (' '+controlText.toLowerCase().replace(/[^a-z0-9]+/g,' ')+' ').includes(' '+x.toLowerCase().replace(/[^a-z0-9]+/g,' ')+' ')).length > 1) continue;
            out.set(control, {el:control, text:own});
        }
        return [...out.values()];
    }

    function findClickable(label, mode = 'general') {
        if (!['target','paint'].includes(mode)) return null;
        const candidates = findGuideCandidates(label,mode);
        // Ambiguous control resolution is a STOP, not permission to try every match.
        return candidates.length === 1 ? candidates[0].el : null;
    }

    function debugGuideCandidates(label, mode) {
        return findGuideCandidates(label, mode).slice(0, 15).map(c => ({
            text: c.text, tag: c.el.tagName,
            className: String(c.el.className || '').slice(0,120),
            ariaSelected: c.el.getAttribute?.('aria-selected'),
            outerHTML: c.el.outerHTML?.slice(0,450)
        }));
    }

    async function waitFor(fn, timeout = 3500, step = 120) {
        const until = Date.now() + timeout;
        while (Date.now() < until) {
            try { const value = fn(); if (value) return value; } catch {}
            await new Promise(r => setTimeout(r, step));
        }
        return null;
    }

    async function ensureGraffitiOpen() {
        if (!isGraffitiPage()) return false;

        // Commander now navigates directly to Torn's Graffiti hash route. Do not
        // click a generic element named "Graffiti" here: Torn's global navigation
        // can contain broad clickable ancestors and a false match can leave Crimes.
        const onGraffitiRoute = /#\/?graffiti(?:$|[/?])/i.test(location.hash || '');
        if (!onGraffitiRoute) return false;

        const ready = await waitFor(() => findGuideCandidates('Residential District', 'target').length === 1, 4500);
        return !!ready;
    }

    function smartGuideColor(fallback = 'Purple') {
        // This is only for the paint-helper overlay's charge-aware suggestion.
        // A guided Commander action must execute the exact color shown in the Coach.
        scanAll(true);
        const active = activeColor();
        if (active) return active;
        const desired = COLORS.find(c => c.toLowerCase() === String(fallback || '').toLowerCase());
        if (desired && (!state[desired] || Number(state[desired].pct) > 0)) return desired;
        const known = COLORS.filter(c => state[c] && Number.isFinite(state[c].pct) && state[c].pct > 0)
            .sort((a,b) => state[b].pct - state[a].pct);
        return known[0] || desired || 'Purple';
    }

    function strictGuideColor(requested = 'Purple') {
        return COLORS.find(c => c.toLowerCase() === String(requested || '').trim().toLowerCase()) || null;
    }

    function normalizeTargetName(value) {
        return String(value || '').toLowerCase().replace(/[-–—]/g, ' ').replace(/\bdistrict\b/g, '').replace(/\bcentre\b/g, 'center').replace(/\s+/g, ' ').trim();
    }

    function targetAliases(value) {
        const raw = String(value || '').trim();
        const aliases = [raw];
        if (/\bdistrict\b/i.test(raw)) aliases.push(raw.replace(/\s*district\s*/i, ' ').trim());
        if (/city centre/i.test(raw)) aliases.push(raw.replace(/centre/i, 'Center'));
        if (/city center/i.test(raw)) aliases.push(raw.replace(/center/i, 'Centre'));
        return [...new Set(aliases.filter(Boolean))];
    }

    function findGuideTarget(value) {
        for (const alias of targetAliases(value)) {
            const el = findClickable(alias, 'target');
            if (el) return el;
        }
        return null;
    }

    function selectedTargetEvidence() {
        const found = [];
        for (const name of TARGETS) {
            const el = findGuideTarget(name);
            if (el && isSelectedElement(el)) found.push(name);
        }
        return [...new Set(found)];
    }

    function selectedPaintEvidence() {
        return COLORS.filter(name => {
            const candidates = findGuideCandidates(name, 'paint');
            return candidates.length === 1 && isSelectedElement(candidates[0].el);
        });
    }

    async function deliberateClick(el) {
        // Exactly one click event per explicit user request. A synthetic mousedown/up
        // sequence followed by .click() can dispatch multiple game actions in React.
        if (!el || !el.isConnected || !isInCrimeContent(el)) return false;
        if (el.matches?.(':disabled') || el.getAttribute?.('aria-disabled') === 'true') return false;
        try { el.scrollIntoView?.({ block:'center', inline:'nearest' }); } catch {}
        await new Promise(r => setTimeout(r, 90));
        if (!el.isConnected || !isInCrimeContent(el)) return false;
        try { el.focus?.({preventScroll:true}); } catch {}
        try { el.click(); return true; } catch { return false; }
    }

    async function verifyStableSelection(kind, expected, timeout = 2000) {
        const matches = () => {
            const choices = kind === 'target' ? selectedTargetEvidence() : selectedPaintEvidence();
            return choices.length === 1 && (kind === 'target'
                ? normalizeTargetName(choices[0]) === normalizeTargetName(expected)
                : choices[0] === expected);
        };
        if (!(await waitFor(matches, timeout))) return false;
        await new Promise(r => setTimeout(r, 180));
        return matches();
    }

    async function selectGuideSetup(target = 'Residential District', requestedPaint = 'Purple') {
        const paint = strictGuideColor(requestedPaint);
        if (!paint) return {ok:false,reason:`Unknown paint color ${requestedPaint}. No selection attempted.`};
        if (!(await ensureGraffitiOpen())) return {ok:false, reason:'Graffiti page controls are not ready; open the Graffiti crime page first'};
        const targets = findGuideCandidates(target, 'target');
        if (targets.length !== 1) return {ok:false,reason:`Expected one ${target} district control; found ${targets.length}. Debug is available in the paint window.`};
        const targetEl = targets[0].el;
        if (!isSelectedElement(targetEl)) {
            if (!(await deliberateClick(targetEl)))
                return {ok:false,reason:`Could not safely click ${target} district. No paint or Spray touched.`};
        }
        const targetVerified = await verifyStableSelection('target', target, 2500);
        if (!targetVerified) {
            return {ok:false,reason:`Could not positively verify ${target} selection. Paint and Spray were not touched. Copy Graffiti Debug.`};
        }
        notifyCommander(`Verified district: ${target}. Checking requested paint.`, 'CHECKING');
        // Torn can re-render the paint list after the district changes. Wait for
        // one uniquely identifiable paint control; do not click a default color.
        const paints = await waitFor(() => {
            const list = findGuideCandidates(paint, 'paint');
            return list.length === 1 ? list : null;
        }, 3200);
        if (!paints) return {ok:false,reason:`Could not uniquely locate ${paint} paint. No Spray. Copy Graffiti Debug.`};
        if (paints[0].el === targetEl) return {ok:false,reason:'District and paint resolved to the same control. No Spray.'};
        if (!isSelectedElement(paints[0].el)) {
            if (!(await deliberateClick(paints[0].el)))
                return {ok:false,reason:`Could not safely click ${paint} paint. No Spray.`};
        }
        const paintVerified = await verifyStableSelection('paint', paint, 2500);
        if (!paintVerified) {
            return {ok:false,reason:`Could not positively verify ${paint} paint selection. No Spray. Copy Graffiti Debug.`};
        }
        notifyCommander(`Verified paint: ${paint}. Checking for the final Spray control.`, 'CHECKING');
        return {ok:true,target,paint,targetVerification:'explicitly selected',paintVerification:'explicitly selected'};
    }

    function findFinalSprayButton() {
        const candidates = [...document.querySelectorAll('button,[role="button"],input[type="button"],input[type="submit"],[data-testid],[aria-label],a[role="button"]')]
            .filter(el => isInCrimeContent(el) && !el.disabled && el.getAttribute?.('aria-disabled') !== 'true')
            .filter(el => {
                const label = [guideLabel(el), el.getAttribute?.('value') || '', el.getAttribute?.('aria-label') || '', el.getAttribute?.('data-testid') || '']
                    .join(' ').replace(/\s+/g,' ').trim().toLowerCase();
                if (!label) return false;
                const sprayish = /(^|\b)(spray|spray graffiti|start spray|commit spray|go spray)(\b|$)/.test(label);
                return sprayish && !COLORS.some(color => label.includes(color.toLowerCase()));
            });
        if (candidates.length === 1) return candidates[0];
        const exact = candidates.filter(el => {
            const label = [guideLabel(el), el.getAttribute?.('value') || '', el.getAttribute?.('aria-label') || '']
                .join(' ').replace(/\s+/g,' ').trim().toLowerCase();
            return label === 'spray' || label === 'spray graffiti';
        });
        return exact.length === 1 ? exact[0] : null;
    }

    let lastSprayClickAt = 0;
    async function runGuideAction(target = 'Residential District', fallbackPaint = 'Purple', runOnce = false) {
        if (runOnce && Date.now() - lastSprayClickAt < 4500) {
            notifyCommander('Run Once cooldown active after the previous Spray click. No new click.', 'STOPPED');
            return {ok:false,reason:'Duplicate Spray click prevented by cooldown'};
        }
        if (guideBusy) {
            notifyCommander('An earlier Graffiti action is still checking selections. No additional clicks.', 'STOPPED');
            return {ok:false,reason:'A Graffiti action is already in progress; no additional clicks made.'};
        }
        guideBusy = true;
        notifyCommander('Checking requested district and paint controls. No Spray yet.', 'CHECKING');
        try {
            const configured = await selectGuideSetup(target, fallbackPaint);
            if (!configured.ok) {
                notifyCommander(`Stopped: ${configured.reason}`, 'STOPPED');
                return configured;
            }
            if (!runOnce) {
                notifyCommander(`Verified ${configured.target} + ${configured.paint}. One manual Spray remains.`, 'VERIFIED');
                return configured;
            }
            // Re-verify immediately before committing. Never rely on saved/cached paint state.
            const finalTargets = selectedTargetEvidence();
            const finalPaints = selectedPaintEvidence();
            if (finalTargets.length !== 1 || normalizeTargetName(finalTargets[0]) !== normalizeTargetName(target)
                || finalPaints.length !== 1 || finalPaints[0] !== configured.paint) {
                notifyCommander('Spray prevented: target or paint selection changed or is ambiguous.', 'STOPPED');
                return {ok:false,reason:'Final selection re-verification failed; no Spray'};
            }
            const actionButton = await waitFor(findFinalSprayButton, 3200);
            if (!actionButton) {
                notifyCommander('Selections verified, but Spray button could not be unambiguously located. No attempt submitted.', 'STOPPED');
                return {...configured,submitted:false,reason:'Spray button not verified'};
            }
            notifyCommander(`Spray control found. Sending one click for ${configured.target} + ${configured.paint}.`, 'CHECKING');
            if (!(await deliberateClick(actionButton))) {
                notifyCommander('Spray control disappeared or became disabled. No click sent.', 'STOPPED');
                return {...configured,submitted:false,reason:'Spray control not clickable'};
            }
            lastSprayClickAt = Date.now();
            notifyCommander(`Sent one Spray click for ${configured.target} + ${configured.paint}. Confirm Torn's actual result before counting success.`, 'CLICK_SENT');
            setTimeout(() => scanAll(true), 900);
            return {...configured,submitted:true};
        } catch (e) {
            notifyCommander(`Graffiti guide error: ${e?.message || e}. No retry will occur.`, 'ERROR');
            return {ok:false,reason:String(e?.message || e)};
        } finally { guideBusy = false; }
    }

    window.__TornCommanderGraffiti = {
        version: '1.6.1',
        state,
        scan: () => scanAll(true),
        recommendation,
        debugJson: () => JSON.stringify(getSafeDebug(), null, 2),
        recommendedColor: smartGuideColor,
        selectGuideSetup,
        runGuideAction
    };

    hookNetwork();
    const observer = new MutationObserver(records => {
        // The overlay is inside document.body. Its own repaint used to retrigger
        // the page-wide scan indefinitely. Only changes outside our overlay count.
        if (records.every(record => {
            const node = record.target;
            return node instanceof Element && (node.id === 'tc-graffiti-paint-helper' || !!node.closest('#tc-graffiti-paint-helper'));
        })) return;
        scanAll(false);
    });
    const start = () => {
        if (!document.body) return setTimeout(start, 100);
        observer.observe(document.body, {subtree:true, childList:true, attributes:true, attributeFilter:['class','aria-selected','aria-checked','title','aria-label']});
        setInterval(() => scanAll(false), 1800);
        scanAll(true);
    };
    start();
})();
