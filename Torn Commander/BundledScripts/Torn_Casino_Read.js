// Read-only, user-requested snapshot of the visible Torn Blackjack table.
// No observers, timers, network calls or game-control clicks.
(() => {
 'use strict';
 const visible=e=>e&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'&&getComputedStyle(e).display!=='none';
 const fail=note=>({ok:false,note});
 if(document.visibilityState==='hidden')return fail('Focus the Blackjack game page before reading cards.');
 const hands=[...document.querySelectorAll('.player-cards')].filter(visible);
 if(hands.length!==1)return fail(hands.length?'Multiple split hands are visible. Enter the active hand manually.':'Player cards are not visible yet. Deal a hand manually, or enter the cards below.');
 const rank=e=>{
  if(!visible(e))return null;
  const css=String(e.className||'').match(/(?:^|\s)card-[a-z]+-(10|[2-9TJQKA])(?:\s|$)/i);
  if(css)return css[1].toUpperCase();
  const text=(e.getAttribute('data-rank')||'').trim().toUpperCase();
  return /^(10|[2-9TJQKA])$/.test(text)?text:null;
 };
 const cards=root=>[...root.querySelectorAll('div[class*="card-"], [data-rank]')].filter(visible).map(rank);
 const player=cards(hands[0]);
 const dealers=[...document.querySelectorAll('.dealer-cards')].filter(visible);
 if(player.length<2||player.length>6||player.some(x=>!x)||dealers.length!==1)return fail('Card layout is incomplete or unsupported. Enter the ranks manually; hidden cards are never guessed.');
 const dealer=cards(dealers[0]).filter(Boolean);
 if(dealer.length!==1)return fail('One exposed dealer card is needed during an active hand. Wait for your turn or enter the up-card manually.');
 const available=sel=>[...document.querySelectorAll(sel)].some(e=>visible(e)&&!e.disabled&&e.getAttribute('aria-disabled')!=='true');
 if(!available('#hit, #stand'))return fail('No active player decision is visible. Start or finish the hand manually.');
 return {ok:true,cards:player.join(' '),dealer:dealer[0],double:available('#double, #double-down, #doubleDown'),split:available('#split'),surrender:available('#surrender'),note:'Visible hand read once. Re-read after cards change; no bet or play control was clicked.'};
})()
