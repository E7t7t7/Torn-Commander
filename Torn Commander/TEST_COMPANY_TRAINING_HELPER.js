const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8').replace('__EMPLOYEE_ID__','123');
function fixture(ambiguous=false,hidden=false){
 const button={textContent:'Train',style:{},getClientRects:()=>[{}],scrollIntoView(){this.shown=true;}};
 const target={href:'https://www.torn.com/profiles.php?XID=123',getClientRects:()=>[{}],closest:()=>null};
 const other={href:'https://www.torn.com/profiles.php?XID=999',getClientRects:()=>[{}],closest:()=>null};
 const row={querySelectorAll:s=>s.includes('input')?[button]:ambiguous?[target,other]:[target],parentElement:null};target.parentElement=row;
 target.querySelectorAll=()=>[];
 const document={visibilityState:hidden?'hidden':'visible',querySelectorAll:()=>[target]};
 const result=vm.runInNewContext(source,{document,URL,location:{href:'https://www.torn.com/companies.php'},getComputedStyle:()=>({visibility:'visible'})});
 return {result,button};
}
const good=fixture();assert(good.button.shown&&good.button.style.outline&&good.result.note.includes('Nothing has been counted'));
assert(!fixture(true).button.shown,'Highlighted an ambiguous multi-employee group');
assert(!fixture(false,true).button.shown,'Read an unfocused page');
assert(!/\.click\s*\(|fetch\s*\(|XMLHttpRequest|setInterval|MutationObserver/.test(source),'Helper trains, requests data, or monitors pages');
console.log('COMPANY TRAINING HELPER PASSED: exact visible employee, ambiguous-row refusal, focus check and no game actions.');
