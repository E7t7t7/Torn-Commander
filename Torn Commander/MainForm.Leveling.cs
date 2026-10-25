using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly Dictionary<string,LevelingSession> _leveling=new();
 private readonly Dictionary<string,LevelingAccount> _levelingAccounts=new();
 private bool _levelingBusy;
 private readonly Dictionary<string,object> _levelScoutFeedback=new();
 private sealed record LevelingAccount(int Player,string Name,int Energy,int Life,int Maximum,string Status,MedicalObservation Medical,LevelingHealPlan Recovery,DateTime At);
 private LevelingSession LevelingFor(string profile){if(!_leveling.TryGetValue(profile,out var s)){s=IntelStore.Load(profile,"leveling-queue",new LevelingSession());_leveling[profile]=s;}return s;}
 private void SaveLeveling(string profile)=>IntelStore.Save(profile,"leveling-queue",LevelingFor(profile));
 private object LevelingDashboard(){var s=LevelingFor(CurrentProfileId);_levelingAccounts.TryGetValue(CurrentProfileId,out var a);var worker=_levelingRuns.LastOrDefault(x=>x.Key.ProfileId==CurrentProfileId).Key;MedicalSession? medical=null;if(worker!=null)_medicalSessions.TryGetValue(worker,out medical);return new{scouterFeedback=_levelScoutFeedback.GetValueOrDefault(CurrentProfileId),scouterConfigured=!string.IsNullOrWhiteSpace(CredentialManager.LoadScouterKey(CurrentProfileId)),profileId=CurrentProfileId,session=s,account=a,worker=_levelingRuns.LastOrDefault(x=>x.Key.ProfileId==CurrentProfileId).Value?.Status??"Not started",medical=medical==null?null:new{itemsUsed=medical.ItemsUsed,cooldownUsed=medical.CooldownUsed},estimatedAttacks=a==null?(int?)null:Math.Max(0,(a.Energy-s.EnergyReserve)/25),stale=a==null||DateTime.UtcNow-a.At>TimeSpan.FromSeconds(45)};}
 private async Task<LevelingAccount> ReadLevelingAccountAsync(string profile){
  var key=CredentialManager.LoadApiKey(profile);if(string.IsNullOrWhiteSpace(key))throw new InvalidDataException("Save this account's Torn API key first.");
  var s=LevelingFor(profile);
  using var pdoc=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key));var p=pdoc.RootElement.GetProperty("profile");int player=p.GetProperty("id").GetInt32();
  using var bdoc=JsonDocument.Parse(await _api.FetchEndpointAsync("user/bars",key));var bars=bdoc.RootElement.GetProperty("bars");
  int energy=bars.GetProperty("energy").GetProperty("current").GetInt32(),life=bars.GetProperty("life").GetProperty("current").GetInt32(),maximum=bars.GetProperty("life").GetProperty("maximum").GetInt32();
  using var cdoc=JsonDocument.Parse(await _api.FetchEndpointAsync("user/cooldowns",key));int cooldown=cdoc.RootElement.GetProperty("cooldowns").GetProperty("medical").GetInt32();
  var cfg=MedicalSettings(profile);string perks=cfg.AutoDetect?await _api.FetchEndpointAsync("user/perks",key):"{}";var mods=SmartMedicalOptimizer.Modifiers(perks,cfg);
  var status=p.GetProperty("status");string state=status.GetProperty("state").GetString()??"Unknown";DateTime until=state=="Hospital"?DateTimeOffset.FromUnixTimeSeconds(status.GetProperty("until").GetInt64()).UtcDateTime:DateTime.UtcNow;
  var now=DateTime.UtcNow;var medical=new MedicalObservation(profile,player,now,until,cooldown,mods.Maximum,mods.Bonus,false,mods.Status,s.Inventory);
  var recovery=LevelingPlanner.Recovery(medical,cfg,life,maximum,s.LifePercent,now);
  var account=new LevelingAccount(player,p.GetProperty("name").GetString()??"",energy,life,maximum,state,medical,recovery,now);_levelingAccounts[profile]=account;return account;
 }
 private async Task HandleLevelingAsync(string action,JsonElement root){
  string profile=ReadWorkerString(root,"profileId");if(profile!=CurrentProfileId||!_settings.Profiles.Any(p=>p.Id==profile))throw new InvalidDataException("The selected account changed; refresh this page.");
  if(action is "levelScoutKey" or "levelScoutSave"){await SaveLevelingScoutSettingsAsync(action,root,profile);return;}
  if(action!="levelSources"&&_levelingRuns.Any(x=>x.Key.ProfileId==profile&&x.Value.Active))throw new InvalidOperationException("Stop automatic leveling before editing the queue or using manual session controls.");
  if(_levelingBusy)throw new InvalidOperationException("A leveling check is already running.");_levelingBusy=true;
  var s=LevelingFor(profile);
  try{
   if(action.StartsWith("levelScout")){await LevelingScoutActionAsync(action,root,profile);
   }else if(action=="levelLoadBaldr"){
    if(s.OpenedUtc!=null)throw new InvalidOperationException("Check or skip the pending fight before replacing its target list.");
    var data=await BaldrListProvider.LoadAsync(ReadWorkerString(root,"group"),_levelingAccounts.GetValueOrDefault(profile)?.Player??0);
    if(CurrentProfileId!=profile)throw new InvalidOperationException("Account changed during download. The existing queue was kept.");
    s.Targets=data.Targets;s.PendingTarget=0;s.BaldrGroups=data.Groups;s.BaldrSource=BaldrListProvider.SourceUrl;s.BaldrLoadedUtc=data.FetchedUtc;
    s.Note="Loaded "+data.Targets.Count+" distinct targets from Baldr's list, in source order. Listed stats are historical reference values; Commander checks current availability before fighting.";
   }else if(action=="levelSave"){
    int reserve=root.GetProperty("reserve").GetInt32(),percent=root.GetProperty("lifePercent").GetInt32(),limit=root.GetProperty("limit").GetInt32();
    if(reserve is <0 or >10000||percent is <1 or >100||limit is <0 or >10000)throw new InvalidDataException("Invalid reserve, life target or attack limit.");
    var inventory=new Dictionary<int,int>();foreach(int id in new[]{68,67,66}){int q=root.GetProperty("inventory").GetProperty(id.ToString()).GetInt32();if(q is <0 or >100000000)throw new InvalidDataException("Invalid medical quantity.");inventory[id]=q;}
    string text=ReadWorkerString(root,"targets");List<LevelingTarget>? imported=string.IsNullOrWhiteSpace(text)?null:LevelingPlanner.Import(text,_levelingAccounts.GetValueOrDefault(profile)?.Player??0);
    if(imported!=null&&s.OpenedUtc!=null)throw new InvalidOperationException("Check the pending fight result or skip it before replacing the list.");
    if(_levelingRuns.Any(x=>x.Key.ProfileId==profile&&x.Value.Active))throw new InvalidOperationException("Stop the leveling worker before changing its settings.");
    string weapon=ReadWorkerString(root,"weapon");if(weapon is not ("weapon_primary" or "weapon_secondary" or "weapon_melee"))throw new InvalidDataException("Select a supported equipped weapon slot.");
    int delay=root.GetProperty("delay").GetInt32();if(delay is <500 or >5000)throw new InvalidDataException("Attack delay must be 500–5000 ms.");
    s.AutoHeal=root.GetProperty("autoHeal").GetBoolean();s.WeaponSlot=weapon;s.AttackDelayMs=delay;
    s.EnergyReserve=reserve;s.LifePercent=percent;s.AttackLimit=limit;s.Inventory=inventory;s.InventoryUtc=DateTime.UtcNow;
    if(imported!=null){s.Targets=imported;s.PendingTarget=0;s.BaldrSource="";s.BaldrLoadedUtc=null;}
    _levelingAccounts.Remove(profile);s.Note="Saved for this account. Supply quantities are user-entered estimates; refresh/recalculate after each medical use.";
   }else if(action=="levelSources"){
    await AddBrowserTabAsync(ReadWorkerString(root,"source")=="ff"?"https://ffscouter.com/target-finder":"https://www.torn.com/forums.php#/p=threads&f=61&t=16034448");return;
   }else if(action=="levelReadInventory"){
    var a=await ReadLevelingAccountAsync(profile);var web=Active()?.WebView;
    if(web?.CoreWebView2==null||web.Source?.Host!="www.torn.com"||web.Source.AbsolutePath!="/item.php")throw new InvalidOperationException("Manually open Items / Medical in the main browser first.");
    string result=await web.CoreWebView2.ExecuteScriptAsync("""
    JSON.stringify((()=>{
     const visible=e=>e.getClientRects().length&&getComputedStyle(e).display!=='none';
     const named=new Set();
     const inChat=e=>{for(let n=e;n;n=n.parentElement){const id=n.getAttribute?.('id')||'';if(id.startsWith('chat_panel_button:')||id.startsWith('chat_panel:'))return true;}return false;};
     for(const link of document.querySelectorAll('a[href*="profiles.php"]')){
      if(inChat(link)||![link.getAttribute('aria-label'),link.getAttribute('title'),link.textContent].some(v=>/^(?:name|username)\s*:/i.test((v||'').replace(/\s+/g,' ').trim())))continue;
      try{const u=new URL(link.href);const id=Number(u.searchParams.get('XID'));if(u.protocol==='https:'&&(u.hostname==='www.torn.com'||u.hostname==='torn.com')&&Number.isSafeInteger(id)&&id>0)named.add(id);}catch{}
     }

     for(const label of document.querySelectorAll('span,strong,b,div,dt,td,p,li,dd,label,th,section')){
      if(inChat(label))continue;
      const text=label.textContent.replace(/\s+/g,' ').trim();if(text.length>300||!/^(?:name|username)\s*:/i.test(text))continue;
      let scope=label;for(let depth=0;scope&&depth<3;depth++,scope=scope.parentElement){
       const text=scope.textContent.replace(/\s+/g,' ').trim();if(text.length>300||!/^(?:name|username)\s*:/i.test(text))break;
       const ids=[...new Set([...scope.querySelectorAll('a[href*="profiles.php"]')].map(a=>{try{const u=new URL(a.href);return u.protocol==='https:'&&(u.hostname==='www.torn.com'||u.hostname==='torn.com')?Number(u.searchParams.get('XID')):0;}catch{return 0;}}).filter(n=>Number.isSafeInteger(n)&&n>0))];
       if(ids.length===1){named.add(ids[0]);break;}
      }
     }
     const ids=[...named];const inventory=[];
     for(const [id,name] of [[68,'small first aid kit'],[67,'first aid kit'],[66,'morphine']]){
      const leaves=[...document.querySelectorAll('span.name,span,strong')].filter(e=>visible(e)&&!e.children.length&&e.textContent.trim().toLowerCase()===name);
      const rows=[...new Set(leaves.map(e=>e.closest('li')).filter(Boolean))];if(rows.length!==1)continue;
      const row=rows[0],text=row.textContent.replace(/,/g,'').replace(/\s+/g,' ').trim();
      if(/faction|armory|borrowed/i.test(text)||row.getAttribute('data-faction-owned')==='true')continue;
      const m=text.match(/(?:^|\s)x\s*(\d+)(?:\s|$)/i);const raw=row.getAttribute('data-quantity')??row.getAttribute('data-qty');
      const quantity=raw!==null?Number(raw):m?Number(m[1]):NaN;if(Number.isSafeInteger(quantity)&&quantity>=0)inventory.push({id,quantity});
     }return{owner:ids.length===1?ids[0]:null,inventory};
    })())
    """);
    using var d=JsonDocument.Parse(JsonSerializer.Deserialize<string>(result)??"{}");
    if(!d.RootElement.GetProperty("owner").TryGetInt32(out int owner)||owner!=a.Player||CurrentProfileId!=profile)throw new InvalidDataException("Visible Medical page account could not be verified.");
    var inventory=d.RootElement.GetProperty("inventory").EnumerateArray().ToDictionary(x=>x.GetProperty("id").GetInt32(),x=>x.GetProperty("quantity").GetInt32());
    if(inventory.Count==0)throw new InvalidDataException("No unambiguous personal medical quantities are visible. Open the Medical category or enter quantities manually.");
    s.Inventory=inventory;s.InventoryUtc=DateTime.UtcNow;
    var medical=a.Medical with{Inventory=inventory};_levelingAccounts[profile]=a with{Medical=medical,Recovery=LevelingPlanner.Recovery(medical,MedicalSettings(profile),a.Life,a.Maximum,s.LifePercent,DateTime.UtcNow)};
    s.Note="Read visible personal medical quantities. Unseen items are unavailable to the planner until you enter them. Use medicines manually, one at a time, then read again.";
   }else if(action=="levelMedical"){
    EnsureLevelingProfileIdle(profile);NavigateActive("https://www.torn.com/item.php");s.Note="Open Medical and use the recommended item manually. Update the quantities and check again after each use.";
   }else if(action=="levelSkip"){
    var target=s.Targets.FirstOrDefault(t=>t.Id==s.PendingTarget);if(target!=null){target.Finished=true;target.Status="Skipped by you";}s.PendingTarget=0;s.OpenedUtc=null;s.Note="Skipped without counting a win. Check next to continue.";
   }else if(action=="levelRound"){
    if(s.OpenedUtc!=null)throw new InvalidOperationException("Check or skip the pending fight first.");foreach(var t in s.Targets){t.Finished=false;t.CheckedUtc=null;t.Status="Not checked";}s.PendingTarget=0;s.Note="New pass through the list. Session totals and energy reserve retained.";
   }else if(action=="levelResult"){
    if(s.PendingTarget<=0||s.OpenedUtc==null)throw new InvalidOperationException("Open a queued target first.");
    var a=await ReadLevelingAccountAsync(profile);string key=CredentialManager.LoadApiKey(profile)!;
    using var attacks=JsonDocument.Parse(await _api.FetchEndpointAsync("user/attacks?filters=outgoing&limit=100&sort=desc&from="+new DateTimeOffset(s.OpenedUtc.Value).ToUnixTimeSeconds(),key));
    var receipt=LevelingPlanner.FindReceipt(attacks.RootElement.GetRawText(),a.Player,s.PendingTarget,s.OpenedUtc.Value,s.AttackIds);
    if(receipt==null){s.Note="No new completed attack found for this target yet. Torn may cache the log; check again after it updates. Nothing counted.";}
    else {var r=receipt.Value;s.AttackIds.Add(r.Id);if(r.Result=="Attacked")s.Wins++;else s.OtherResults++;var target=s.Targets.First(t=>t.Id==s.PendingTarget);target.Finished=true;target.Status="Verified: "+r.Result;s.PendingTarget=0;s.OpenedUtc=null;s.Note=r.Result=="Attacked"?"Verified win with Leave. Check next when ready.":"Verified result: "+r.Result+". Review equipment/life before continuing; only Leave results count as leveling wins.";}
   }else if(action=="levelCheck"||action=="levelOpen"){
    EnsureLevelingProfileIdle(profile);
    if(s.OpenedUtc!=null)throw new InvalidOperationException("Check the pending attack result before opening another target.");
    var a=await ReadLevelingAccountAsync(profile);await RefreshLevelingStrengthAsync(profile);
    if(a.Status!="Okay"){s.Note="Account status: "+a.Status+". Recover or return to Torn before fighting.";s.PendingTarget=0;}
    else if(a.Energy-s.EnergyReserve<25){s.Note="Energy reserve reached: fewer than 25 spendable energy. No target opened.";s.PendingTarget=0;}
    else if(s.AttackLimit>0&&s.Wins+s.OtherResults>=s.AttackLimit){s.Note="Session attack limit reached.";s.PendingTarget=0;}
    else if(a.Recovery.State!="Ready"){s.Note=a.Recovery.Reason;s.PendingTarget=0;}
    else {
     string key=CredentialManager.LoadApiKey(profile)!;var candidates=(action=="levelOpen"?s.Targets.Where(t=>t.Id==s.PendingTarget):s.Targets.Where(t=>!t.Finished&&(t.CheckedUtc==null||t.Status=="Okay"||DateTime.UtcNow-t.CheckedUtc>TimeSpan.FromMinutes(1)))).ToList();
     s.PendingTarget=0;int scanned=0;
     foreach(var target in candidates.ToList()){
      if(scanned>=10)break;
      if(target.Id==a.Player){target.Finished=true;target.Status="Own account excluded";continue;}
      target.Suitability=LevelingScouting.Suitability(target,s,DateTime.UtcNow);if(!LevelingScouting.StrengthEligible(target,s,DateTime.UtcNow))continue;
      scanned++;
      using var d=JsonDocument.Parse(await _api.FetchEndpointAsync("user/"+target.Id+"/profile",key));var p=d.RootElement.GetProperty("profile");if(p.GetProperty("id").GetInt32()!=target.Id)throw new InvalidDataException("Target identity mismatch.");
      target.Name=p.GetProperty("name").GetString()??"";target.Level=p.GetProperty("level").GetInt32();target.Status=p.GetProperty("status").GetProperty("state").GetString()??"Unknown";target.CheckedUtc=DateTime.UtcNow;target.LastActionUtc=LevelingScouting.Time(p.GetProperty("last_action"),"timestamp");target.Suitability=LevelingScouting.Suitability(target,s,DateTime.UtcNow);
      if(!LevelingScouting.Eligible(target,s,DateTime.UtcNow))continue;
      if(target.Status=="Okay"){
       s.PendingTarget=target.Id;s.Note="Ready: "+target.Name+". Equip a proper fighting weapon, complete the fight yourself and choose Leave. Listed targets and estimates do not guarantee a win.";
       if(action=="levelOpen"){
        if(CurrentProfileId!=profile)throw new InvalidOperationException("Account changed during the check.");
        if(DateTime.UtcNow-a.At>TimeSpan.FromSeconds(45))throw new InvalidOperationException("Account check expired; check again before opening the target.");
        s.OpenedUtc=DateTime.UtcNow;NavigateActive("https://www.torn.com/page.php?sid=attack&user2ID="+target.Id);
       }break;
      }
      if(scanned>=10){s.Note="Checked ten unavailable targets. Check next again to continue without flooding the API.";break;}
     }
     if(s.PendingTarget==0&&scanned<10)s.Note="No eligible target found. Scan strength estimates, review filters, or wait for availability to change.";
    }
   }
   SaveLeveling(profile);
  }catch(Exception ex){s.Note=action=="levelLoadBaldr"?"Baldr list load failed; your existing queue was kept. "+(ex is InvalidDataException?ex.Message:"Check the connection and try again."):s.Note;SaveLeveling(profile);throw;}
  finally{_levelingBusy=false;UpdateModernDashboard();}
 }
 private void EnsureLevelingProfileIdle(string profile){if(new[]{_lossWorkerA,_lossWorkerB}.Any(w=>w?.IsArmed==true&&w.ProfileId==profile))throw new InvalidOperationException("Stop this account's Loss Desk worker before using Leveling.");}
}
