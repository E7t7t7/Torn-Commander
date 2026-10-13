// Synthetic DOM only. Never connects to Torn, spends nerve or reads account data.
'use strict';
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(process.argv[2],'utf8');
function fixture(){
 const nodes=[], messages=[];
 class El{
  constructor(text,selected=false){this.tagName='BUTTON';this.innerText=text;this.textContent=text;this.childNodes=[{nodeType:3,textContent:text}];this.className=selected?'selected':'';this.parentElement=null;this.disabled=false;this.isConnected=true;this.attrs={};nodes.push(this);}
  get attributes(){return Object.entries(this.attrs).map(([name,value])=>({name,value}));}
  getAttribute(k){return this.attrs[k]??null;}
  hasAttribute(k){return k in this.attrs;}
  matches(s){return s.split(',').some(v=>{v=v.trim();return v===':disabled'?this.disabled:v==='button'?this.tagName==='BUTTON':v==='input[type="radio"]'?this.tagName==='INPUT'&&this.attrs.type==='radio':v==='input[type="checkbox"]'?this.tagName==='INPUT'&&this.attrs.type==='checkbox':false;});}
  closest(s){return null;}
  querySelector(){return null;}
  getBoundingClientRect(){return {width:100,height:30};}
  scrollIntoView(){}
  focus(){}
  click(){this.clicks=(this.clicks||0)+1;if(this.onClick)this.onClick();else if(this!==spray)this.className='selected';}
 }
 const district=new El('Residential District',true),paint=new El('Purple',true),spray=new El('Spray');
 const window={chrome:{webview:{postMessage:x=>messages.push(x)}}};
 const document={body:{innerText:'Graffiti'},querySelectorAll:()=>nodes,getElementById:()=>null};
 const context={window,document,location:{href:'https://www.torn.com/page.php?sid=crimes#/graffiti',hash:'#/graffiti'},
  localStorage:{getItem:()=>null,setItem:()=>{}},getComputedStyle:()=>({display:'block',visibility:'visible',opacity:'1'}),
  Date,Map,Set,URL,console,setTimeout,clearTimeout};
 vm.createContext(context);
 const instrumented=source.replace('    hookNetwork();','    render=()=>{}; scanAll=()=>{}; window.__test={isSelectedElement,deliberateClick}; return;\n    hookNetwork();');
 vm.runInContext(instrumented,context);
 return {context,window,nodes,district,paint,spray,El,messages,helper:window.__TornCommanderGraffiti};
}
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
async function finish(f,id){for(let i=0;i<160;i++){const r=f.helper.getGuideAction(id);if(r.done)return r.result;await sleep(50);}throw Error('Test command did not finish');}
(async()=>{
 let f=fixture();
 const ack=f.helper.startGuideAction('one','Residential District','Purple',true);
 assert.strictEqual(ack.started,true);
 assert.strictEqual(typeof ack.then,'undefined','Native acknowledgement must not be a Promise');
 assert.strictEqual(f.helper.getGuideAction('one').done,false);
 assert.strictEqual(f.helper.startGuideAction('two','Residential District','Purple',true).started,false,'Concurrent command accepted');
 let result=await finish(f,'one');
 assert.strictEqual(result.submitted,true);assert.strictEqual(f.spray.clicks,1,'Run Once sent more than one Spray');
 assert.strictEqual(f.helper.startGuideAction('one','Residential District','Purple',true).started,false,'Duplicate command ID retried');
 await f.helper.runGuideAction('Residential District','Purple',true);
 assert.strictEqual(f.spray.clicks,1,'Cooldown allowed a second Spray');

 f=fixture();f.helper.startGuideAction('configure','Residential District','Purple',false);
 result=await finish(f,'configure');assert.strictEqual(result.ok,true);assert.strictEqual(f.spray.clicks||0,0,'Configure submitted Spray');

 f=fixture();f.helper.startGuideAction('cancel','Residential District','Purple',true);
 f.helper.cancelGuideAction('cancel');await finish(f,'cancel');
 assert.strictEqual(f.spray.clicks||0,0,'Cancelled command submitted Spray');

 f=fixture();f.helper.startGuideAction('change','Residential District','Purple',true);
 // Selection changes during the final delayed click, after initial verification.
 f.spray.scrollIntoView=()=>{f.paint.className='';f.paint.attrs['data-state']='unchecked';};
 await finish(f,'change');assert.strictEqual(f.spray.clicks||0,0,'Late selection change submitted Spray');

 f=fixture();f.helper.startGuideAction('disable','Residential District','Purple',true);
 f.spray.scrollIntoView=()=>{f.spray.disabled=true;};
 await finish(f,'disable');assert.strictEqual(f.spray.clicks||0,0,'Button disabled during click delay was clicked');

 f=fixture();const input=new f.El('Purple');input.tagName='INPUT';input.attrs.type='radio';input.checked=true;
 assert.strictEqual(f.window.__test.isSelectedElement(input),true,'Native checked radio was not recognized');
 input.checked=false;input.attrs['data-state']='unchecked';
 assert.strictEqual(f.window.__test.isSelectedElement(input),false,'Unchecked state was mistaken for checked');
 input.attrs['data-state']='unselected';
 assert.strictEqual(f.window.__test.isSelectedElement(input),false,'Unselected state was mistaken for selected');

 f=fixture();new f.El('Spray');f.helper.startGuideAction('ambiguous','Residential District','Purple',true);
 await finish(f,'ambiguous');assert.strictEqual(f.spray.clicks||0,0,'Ambiguous Spray control was clicked');

 f=fixture();const previous=f.helper;
 vm.runInContext(source,f.context);
 assert.strictEqual(f.window.__TornCommanderGraffiti,previous,'Re-injection reset command locks');
 console.log('GRAFFITI TEST PASSED: native acknowledgement/results, one-click, cooldown, configure, cancellation, final selection/disabled guards, native radios, ambiguous controls and injection lock.');
})().catch(e=>{console.error(e);process.exitCode=1;});
