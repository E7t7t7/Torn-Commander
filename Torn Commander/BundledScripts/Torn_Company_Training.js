// One user-requested highlight. Does not click, train, send a request or monitor another page.
(() => {
 const id=String(__EMPLOYEE_ID__);
 const visible=e=>e&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden';
 if(document.visibilityState==='hidden')return {note:'Focus your company Employees page before finding the Train button.'};
 const links=[...document.querySelectorAll('a[href*="profiles.php"]')].filter(a=>{
  if(!visible(a)||a.closest('[id*="chat"], [class*="chat"]'))return false;
  try{const u=new URL(a.href,location.href);return (u.searchParams.get('XID')||u.searchParams.get('userID'))===id;}catch{return false;}
 });
 const matches=[];
 for(const link of links){let row=link;for(let depth=0;row&&depth<7;depth++,row=row.parentElement){
  const controls=[...row.querySelectorAll('a,button,input[type="button"]')].filter(e=>visible(e)&&/^train$/i.test((e.textContent||e.value||'').trim()));
  const people=new Set([...row.querySelectorAll('a[href*="profiles.php"]')].map(a=>{try{const u=new URL(a.href,location.href);return u.searchParams.get('XID')||u.searchParams.get('userID');}catch{return null;}}).filter(Boolean));
  if(controls.length===1&&people.size===1){matches.push(controls[0]);break;}
 }}
 const found=[...new Set(matches)];
 if(found.length!==1)return {note:'A unique Train button was not found for this employee. Open Employees in Torn and use their native Train link manually.'};
 const button=found[0];button.scrollIntoView({block:'center'});button.style.outline='3px solid #65dcb0';button.style.outlineOffset='4px';
 return {note:'The selected employee’s Train button is highlighted. Click it yourself in Torn, then refresh training receipts in Commander. Nothing has been counted yet.'};
})()
