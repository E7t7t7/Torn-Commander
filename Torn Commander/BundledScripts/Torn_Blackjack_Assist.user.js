// ==UserScript==
// @name         Torn Blackjack Assist
// @namespace    torn.blackjack.assist
// @version      2.2
// @description  Displays real-time basic strategy advice for Blackjack on both desktop and Torn PDA.
// @author       eaksquad
// @match        https://www.torn.com/page.php?sid=blackjack*
// @match        https://www.torn.com/pda.php*step=blackjack*
// @grant        none
// @license      MIT
// @downloadURL https://update.greasyfork.org/scripts/540778/Torn%20Blackjack%20Assist.user.js
// @updateURL https://update.greasyfork.org/scripts/540778/Torn%20Blackjack%20Assist.meta.js
// ==/UserScript==

(function() {
    'use strict';

    const A = { HIT: 'Hit', STAND: 'Stand', DOUBLE: 'Double', SPLIT: 'Split' };
    function getCardValue(cardElement) {
        if (!cardElement) return 0;
        const match = cardElement.className.match(/card-\w+-(\w+)/);
        if (!match || !match[1]) return 0;
        const rank = match[1];
        if (['J', 'Q', 'K'].includes(rank)) return 10;
        if (rank === 'A') return 11;
        return parseInt(rank, 10);
    }

    function getHandInfo(cardContainerSelector) {
        const cardElements = document.querySelectorAll(`${cardContainerSelector} div[class*="card-"]`);
        const cards = Array.from(cardElements).map(getCardValue);
        if(cards.some(v=>!Number.isFinite(v)||v<2||v>11))return {cards:[],total:0,isSoft:false,isPair:false};
        let sum = cards.reduce((a, b) => a + b, 0);
        let aces = cards.filter(v => v === 11).length;
        while (sum > 21 && aces-- > 0) sum -= 10;
        const isPair = cards.length === 2 && cards[0] === cards[1];
        return { cards, total: sum, isSoft: aces > 0 && sum <= 21, isPair };
    }

    function getDecision(d,hand) {
        const t=hand.total,p=hand.cards[0];
        if(hand.isPair && (p===11||p===8||(p===9&&[2,3,4,5,6,8,9].includes(d))||(p===7&&d<=7)||(p===6&&d<=6)||(p===4&&[5,6].includes(d))||([2,3].includes(p)&&d<=7)))return A.SPLIT;
        if(hand.cards.length===5&&t<=11)return A.HIT;
        if(hand.isSoft){
            if(t>=19)return A.STAND;
            if(t===18)return [3,4,5,6].includes(d)?A.DOUBLE:d>=9?A.HIT:A.STAND;
            if((t===17&&d>=3&&d<=6)||(t>=15&&t<=16&&d>=4&&d<=6)||(t>=13&&t<=14&&d>=5&&d<=6))return A.DOUBLE;
            return A.HIT;
        }
        if(t>=17)return A.STAND;
        if(t>=13&&d<=6||t===12&&d>=4&&d<=6)return A.STAND;
        if(t===11&&d!==11||t===10&&d<=9||t===9&&d>=3&&d<=6)return A.DOUBLE;
        return A.HIT;
    }

    function mainObserverCallback() {
        if(document.querySelectorAll(".player-cards").length!==1){updateOverlay("---");return;}
        const dealerCardEl = document.querySelector('.dealer-cards div[class*="card-"]');
        const dealerCardValue = getCardValue(dealerCardEl);
        const playerHand = getHandInfo('.player-cards');
        const canTakeAction = [...document.querySelectorAll('#hit, #stand')].some(el=>!el.disabled && el.getAttribute('aria-disabled')!=='true' && el.getClientRects().length);

        if (!dealerCardValue || playerHand.cards.length < 2 || !canTakeAction || playerHand.total >= 21 || playerHand.cards.length >= 6) {
            updateOverlay('---');
            return;
        }

        const available = selector => [...document.querySelectorAll(selector)].some(el=>!el.disabled && el.getAttribute('aria-disabled')!=='true' && el.getClientRects().length);
        let decision = getDecision(dealerCardValue, playerHand);
        if(decision===A.SPLIT && !available('#split')) decision=getDecision(dealerCardValue,{...playerHand,isPair:false});
        if(decision===A.DOUBLE && (playerHand.cards.length!==2 || !available('#double, #double-down, #doubleDown')))
            decision=playerHand.isSoft && playerHand.total===18?A.STAND:A.HIT;
        updateOverlay(decision, playerHand.total);
    }

    // --- UI, Styling, Draggable Logic ---
    function addStyles() {
        const style = document.createElement('style');
        style.innerHTML = `#bj-helper-overlay { position: fixed; background-color: rgba(0,0,0,0.85); color: white; padding: 12px 18px; border-radius: 10px; border: 2px solid; font-family: Arial,"Helvetica Neue",Helvetica,sans-serif; text-align: center; z-index: 9999; box-shadow: 0 4px 8px rgba(0,0,0,0.5); min-width: 150px; transition: all 0.3s ease; cursor: move; user-select: none; } #bj-helper-advice-text { font-size: 24px; font-weight: bold; text-shadow: 0 0 5px #000; } #bj-helper-hand-total { font-size: 14px; opacity: 0.8; margin-top: 4px; } .bj-hit { border-color: #ff9900; background-color: rgba(102,60,0,0.7); } .bj-stand, .bj-double { border-color: #4CAF50; background-color: rgba(30,77,32,0.7); } .bj-split { border-color: #2196F3; background-color: rgba(13,60,99,0.7); } .bj-idle { border-color: #666; }`;
        document.head.appendChild(style);
    }

    function makeDraggable(element) {
        const STORAGE_POS_KEY = 'bjHelperPosition'; let isDragging = false, offsetX, offsetY; const savedPos = localStorage.getItem(STORAGE_POS_KEY); if (savedPos) { const { top, left } = JSON.parse(savedPos); element.style.top = top; element.style.left = left; } else { element.style.right = '20px'; element.style.bottom = '20px'; } const onDragStart = (e) => { isDragging = true; e.preventDefault(); const event = e.touches ? e.touches[0] : e; offsetX = event.clientX - element.offsetLeft; offsetY = event.clientY - element.offsetTop; element.style.right = ''; element.style.bottom = ''; window.addEventListener('mousemove', onDragMove, { passive: false }); window.addEventListener('touchmove', onDragMove, { passive: false }); window.addEventListener('mouseup', onDragEnd); window.addEventListener('touchend', onDragEnd); }; const onDragMove = (e) => { if (!isDragging) return; e.preventDefault(); const event = e.touches ? e.touches[0] : e; element.style.left = `${event.clientX - offsetX}px`; element.style.top = `${event.clientY - offsetY}px`; }; const onDragEnd = () => { isDragging = false; localStorage.setItem(STORAGE_POS_KEY, JSON.stringify({ top: element.style.top, left: element.style.left })); window.removeEventListener('mousemove', onDragMove); window.removeEventListener('touchmove', onDragMove); window.removeEventListener('mouseup', onDragEnd); window.removeEventListener('touchend', onDragEnd); }; element.addEventListener('mousedown', onDragStart); element.addEventListener('touchstart', onDragStart);
    }

    function createOverlay() {
        if (document.getElementById('bj-helper-overlay')) return;
        const overlay = document.createElement('div');
        overlay.id = 'bj-helper-overlay';
        overlay.innerHTML = `<div><div id="bj-helper-advice-text">---</div><div id="bj-helper-hand-total">Waiting for hand</div></div>`;
        document.body.appendChild(overlay);
        makeDraggable(overlay);
    }

    function updateOverlay(decision = '---', total = 0) {
        const overlay = document.getElementById('bj-helper-overlay');
        const adviceEl = document.getElementById('bj-helper-advice-text');
        const totalEl = document.getElementById('bj-helper-hand-total');
        if (!overlay || !adviceEl || !totalEl) return;
        adviceEl.textContent = decision;
        totalEl.textContent = total > 0 ? `on your ${total}` : 'Waiting for hand';
        overlay.className = '';
        if (decision !== '---') { overlay.classList.add(`bj-${decision.toLowerCase()}`); } else { overlay.classList.add('bj-idle'); }
    }

    let activeGame=null,gameObserver=null;
    function initialize() {
        const gameContainer = document.querySelector('.blackjack, .blackjack-wrap');
        if (!gameContainer || gameContainer===activeGame) return;
        gameObserver?.disconnect();activeGame=gameContainer;
        if (!document.getElementById('bj-helper-overlay')) {
            addStyles();
            createOverlay();
        }
        gameObserver = new MutationObserver(mainObserverCallback);
        gameObserver.observe(gameContainer, { childList: true, subtree: true, attributes:true, attributeFilter:['class','disabled','aria-disabled'] });
        mainObserverCallback();
    }

    new MutationObserver(initialize).observe(document.documentElement,{childList:true,subtree:true});
    if (document.readyState === 'loading') {
        window.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
