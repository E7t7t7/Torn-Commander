namespace TornCommander;
internal static class CasinoCoachScript
{
 public const string Code = """

const CasinoCoach=(()=>{
 const tokens=s=>String(s).trim()?String(s).trim().split(/[,\s]+/):[];
 const rank=s=>{s=String(s).toUpperCase();const n=({A:14,K:13,Q:12,J:11,T:10})[s]||Number(s);if(!Number.isInteger(n)||n<2||n>14)throw Error('Use ranks 2-10, J, Q, K or A.');return n};
 function blackjack(text,dealer,double=true,split=true,surrender=false){
  const ranks=tokens(text).map(rank),v=ranks.map(r=>r===14?11:Math.min(r,10));
  if(v.length<2||v.length>6)throw Error('Enter 2-6 player ranks.');
  const d=rank(dealer)===14?11:Math.min(rank(dealer),10);
  let total=v.reduce((a,b)=>a+b,0),aces=v.filter(x=>x===11).length;
  while(total>21&&aces){total-=10;aces--}
  if(total>21)return 'Busted - hand is over.';
  if(total===21||v.length===6)return 'Stand / hand complete - '+total+'.';
  let action='';
  if(split&&v.length===2&&v[0]===v[1]){
   const p=v[0];
   if(p===11||p===8||(p===9&&[2,3,4,5,6,8,9].includes(d))||(p===7&&d<=7)||(p===6&&d<=6)||(p===4&&[5,6].includes(d))||([2,3].includes(p)&&d<=7))action='Split';
  }
  if(!action&&surrender&&v.length===2&&!aces&&((total===16&&d>=9)||(total===15&&d===10)))action='Surrender';
  if(!action&&aces){
   if(total>=19)action='Stand';
   else if(total===18)action=[3,4,5,6].includes(d)?'Double (otherwise Stand)':d>=9?'Hit':'Stand';
   else if((total===17&&d>=3&&d<=6)||(total>=15&&total<=16&&d>=4&&d<=6)||(total>=13&&total<=14&&d>=5&&d<=6))action='Double (otherwise Hit)';
   else action='Hit';
  }
  if(!action){
   if(total>=17)action='Stand';
   else if(total>=13&&d<=6||total===12&&d>=4&&d<=6)action='Stand';
   else if(total===11&&d!==11||total===10&&d<=9||total===9&&d>=3&&d<=6)action='Double (otherwise Hit)';
   else action='Hit';
  }
  if(action.startsWith('Double')&&(!double||v.length!==2))action=action.includes('otherwise Stand')?'Stand':'Hit';
  if(v.length===5&&total<=11)action='Hit';
  return action+' - '+(aces?'soft ':'hard ')+total+'. Eight-deck S17 baseline. Six-card Charlie can change multi-card choices; those are approximate. Decline insurance in basic strategy.';
 }
 function parseCards(s){return tokens(s).map(t=>{const m=t.match(/^(10|[2-9TJQKA])([CDHS])$/i);if(!m)throw Error('Use cards such as As Kh 10d 2c.');return (rank(m[1])-2)*4+'cdhs'.indexOf(m[2].toLowerCase())})}
 function five(cards){
  const ranks=cards.map(c=>Math.floor(c/4)+2).sort((a,b)=>b-a),groups=[...new Set(ranks)].map(r=>[r,ranks.filter(x=>x===r).length]).sort((a,b)=>b[1]-a[1]||b[0]-a[0]);
  const flush=cards.every(c=>c%4===cards[0]%4),unique=[...new Set(ranks)];
  const straight=unique.length===5?(unique[0]-unique[4]===4?unique[0]:unique.join(',')==='14,5,4,3,2'?5:0):0;
  let category=0,k=ranks;
  if(flush&&straight){category=8;k=[straight]}
  else if(groups[0][1]===4){category=7;k=groups.map(g=>g[0])}
  else if(groups[0][1]===3&&groups[1][1]===2){category=6;k=groups.map(g=>g[0])}
  else if(flush)category=5;
  else if(straight){category=4;k=[straight]}
  else if(groups[0][1]===3){category=3;k=groups.map(g=>g[0])}
  else if(groups[0][1]===2&&groups[1][1]===2){category=2;k=groups.map(g=>g[0])}
  else if(groups[0][1]===2){category=1;k=groups.map(g=>g[0])}
  let score=category;for(let i=0;i<5;i++)score=score*15+(k[i]||0);return score;
 }
 function best(cards){
  let score=0;
  for(let a=0;a<cards.length-4;a++)for(let b=a+1;b<cards.length-3;b++)for(let c=b+1;c<cards.length-2;c++)for(let d=c+1;d<cards.length-1;d++)for(let e=d+1;e<cards.length;e++)score=Math.max(score,five([cards[a],cards[b],cards[c],cards[d],cards[e]]));
  return score;
 }
 async function poker(holeText,boardText,opponents,pot,call,samples=1200,cancelled=()=>false){
  const hole=parseCards(holeText),board=parseCards(boardText),known=[...hole,...board];
  if(hole.length!==2||![0,3,4,5].includes(board.length)||new Set(known).size!==known.length)throw Error('Enter two unique hole cards and 0, 3, 4 or 5 unique board cards.');
  if(!Number.isInteger(opponents)||opponents<1||opponents>8||!Number.isFinite(pot)||pot<0||!Number.isFinite(call)||call<0)throw Error('Use 1-8 opponents and nonnegative pot/call amounts.');
  const deck=Array.from({length:52},(_,i)=>i).filter(c=>!known.includes(c));let equity=0,seed=424242;
  const random=()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296};
  for(let n=0;n<samples;n++){
   const draw=deck.slice();let at=0;
   const take=()=>{const j=at+Math.floor(random()*(draw.length-at));[draw[at],draw[j]]=[draw[j],draw[at]];return draw[at++]};
   const full=board.slice();while(full.length<5)full.push(take());
   const hero=best([...hole,...full]);let ties=1,won=true;
   for(let p=0;p<opponents;p++){const villain=best([take(),take(),...full]);if(villain>hero)won=false;else if(villain===hero)ties++}
   if(won)equity+=1/ties;
   if(n%100===99){await new Promise(resolve=>setTimeout(resolve,0));if(cancelled())throw Error("Hand changed; calculation cancelled.");}
  }
  equity/=samples;
  const needed=pot+call>0?call/(pot+call):0,uncertainty=1/Math.sqrt(samples);
  const action=call===0?'Check is free; no raise size chosen.':equity+uncertainty<needed?'Fold under this model.':equity-uncertainty>needed?'Calling clears the simple pot-odds test.':'Borderline - too close to choose confidently.';
  return action+' Estimated pot share '+(100*equity).toFixed(1)+'%; needed '+(100*needed).toFixed(1)+'%. '+samples+' samples, approximate uncertainty +/-'+(100*uncertainty).toFixed(1)+' points. Assumes random opponents. Betting ranges, rake and future bets excluded; not a solver or guaranteed best move.';
 }
 function highlow(card,growth,cashout){
  const r=rank(card);
  if(!Number.isFinite(growth)||growth<0||growth>100||![50,100].includes(cashout))throw Error('Enter growth percent and available 50% or 100% cash-out.');
  const higher=4*(14-r)/51,lower=4*(r-2)/51,expected=Math.max(higher,lower)*(1+growth/100);
  return (expected>cashout/100?(higher>lower?'Higher':'Lower')+' has greater estimated value.':'Cash out has greater estimated value.')+' Higher '+(100*higher).toFixed(1)+'%, lower '+(100*lower).toFixed(1)+'%; expected next pot '+(100*expected).toFixed(1)+'% versus cash-out '+cashout+'%. Assumes fresh 52-card deck with current card removed, ties lose; verify this assumption.';
 }
 function expected(chance,payout,stake){
  if(![chance,payout,stake].every(Number.isFinite)||chance<0||chance>100||payout<0||stake<0)throw Error('Use chance 0-100 and nonnegative net payout/stake.');
  const value=stake*(chance/100*payout-(1-chance/100));
  return 'Expected net per bet: $'+value.toFixed(2)+'. '+(value<=0?'For cash growth, skip this bet.':'Positive only if the entered odds are accurate.')+' Payout excludes returned stake. Past random results do not identify the next result.';
 }
 return {blackjack,poker,highlow,expected,parseCards,best};
})();
if(typeof module!=='undefined')module.exports=CasinoCoach;

""";
}
