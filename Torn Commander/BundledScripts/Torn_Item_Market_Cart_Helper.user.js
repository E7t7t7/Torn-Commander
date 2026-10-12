// ==UserScript==
// @name         Torn Commander Item Market Cart Helper
// @namespace    torn.commander.cart
// @version      1.0.0
// @description  Helps Torn Commander detect manual Item Market purchases so the local cart stays in sync.
// @match        https://www.torn.com/page.php?sid=ItemMarket*
// @grant        none
// ==/UserScript==

(() => {
    'use strict';

    if (window.__tcCartPurchaseObserverInstalled) return;
    window.__tcCartPurchaseObserverInstalled = true;

    const visible = el => !!(el && (el.offsetWidth || el.offsetHeight || el.getClientRects().length));
    const text = el => String(el?.innerText || el?.textContent || '').replace(/\s+/g, ' ').trim();

    function currentItemId() {
        const m = location.href.match(/[?&#]itemID=(\d+)/i);
        return m ? Number(m[1]) : 0;
    }

    function numberFromInput(input) {
        if (!input) return 0;
        const value = Number(String(input.value || '').replace(/[^0-9]/g, ''));
        return Number.isFinite(value) ? value : 0;
    }

    function findQuantity(container) {
        if (!container) return 1;
        const inputs = [...container.querySelectorAll('input[type="number"],input[inputmode="numeric"],input[type="text"]')].filter(visible);
        const qty = inputs.find(i => /qty|quantity|amount/i.test((i.name || '') + ' ' + (i.placeholder || '') + ' ' + (i.getAttribute('aria-label') || ''))) || inputs[0];
        return Math.max(1, numberFromInput(qty) || 1);
    }

    function findUnitPrice(container) {
        const s = text(container);
        const explicit = [...s.matchAll(/\$\s*([\d,]+)/g)].map(m => Number(m[1].replace(/,/g, ''))).filter(n => Number.isFinite(n) && n > 0);
        if (!explicit.length) return 0;

        // The smallest visible money value in a buy dialog is usually the unit price;
        // if Torn only shows a total, Commander still records quantity correctly and
        // leaves cost as best-effort.
        return Math.min(...explicit);
    }

    function postPurchase(payload) {
        try {
            if (window.chrome?.webview?.postMessage) {
                window.chrome.webview.postMessage('TC_CART_PURCHASE|' + JSON.stringify(payload));
            }
        } catch {}
    }

    let pending = null;
    let pendingTimer = 0;

    function finalizePending(reason) {
        if (!pending) return;
        const payload = { ...pending, reason };
        pending = null;
        clearTimeout(pendingTimer);
        postPurchase(payload);
    }

    document.addEventListener('click', e => {
        const button = e.target?.closest?.('button,[role="button"],input[type="submit"]');
        if (!button || !visible(button)) return;

        const label = (text(button) || button.value || '').trim();
        if (!/\b(buy|purchase|confirm)\b/i.test(label) || /cancel|close|back/i.test(label)) return;

        const dialog = button.closest('[role="dialog"],.modal,.dialog,[class*="modal"],[class*="dialog"],[class*="confirm"]');
        if (!dialog || !visible(dialog)) return; // first listing-row Buy click just opens the dialog

        const itemId = currentItemId();
        if (!itemId) return;

        pending = {
            itemId,
            quantity: findQuantity(dialog),
            unitPrice: findUnitPrice(dialog)
        };

        clearTimeout(pendingTimer);
        pendingTimer = setTimeout(() => {
            // Torn's success UI varies. If the dialog disappears after the final
            // confirmation click, treat it as a submitted manual purchase. Commander
            // keeps Undo Last as the safety valve if Torn rejected the listing.
            if (pending && !document.contains(dialog)) finalizePending('dialog-closed');
        }, 1400);
    }, true);

    const observer = new MutationObserver(mutations => {
        if (!pending) return;

        let addedText = '';
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                if (node.nodeType === Node.ELEMENT_NODE || node.nodeType === Node.TEXT_NODE)
                    addedText += ' ' + String(node.innerText || node.textContent || '');
            }
        }

        if (/\b(success|successful|purchased|bought|purchase complete|you bought)\b/i.test(addedText))
            finalizePending('success-ui');
    });

    observer.observe(document.documentElement, { childList: true, subtree: true });
})();
