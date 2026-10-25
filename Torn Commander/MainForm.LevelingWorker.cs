using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private sealed class LevelingRun {
  public string Id="level-"+Guid.NewGuid().ToString("N"),Claim="",LastCommitted="";
  public int Player,Target,LastRemaining;public DateTime Opened,LastStatus;public bool Active=true,Busy,LastStop;
  public string LastMessage="",Status="Starting automatic leveling";
 }
 private readonly Dictionary<LossAutoCycleWorker,LevelingRun> _levelingRuns=new();
 private bool WorkerAutomaticMedical(LossAutoCycleWorker worker,MedicalSession s,SmartMedicalSettings cfg)=>s.Leveling?LevelingFor(worker.ProfileId).AutoHeal:cfg.Automatic;
 private MedicalPlan WorkerMedicalPlan(LossAutoCycleWorker worker,MedicalSession session,MedicalObservation o,SmartMedicalSettings cfg){
  if(!session.Leveling)return SmartMedicalOptimizer.Plan(o,cfg,DateTime.UtcNow);
  var p=LevelingPlanner.Recovery(o,cfg,o.Life,o.MaximumLife,LevelingFor(worker.ProfileId).LifePercent,DateTime.UtcNow);
  DateTime? available=p.State=="Cooldown"?DateTime.UtcNow.AddSeconds(Math.Max(1,o.CooldownSeconds-(o.MaximumSeconds-cfg.ReserveMinutes*60)+1)):null;
  if(p.State=="Supplies / capacity"&&o.CooldownSeconds>0){var later=LevelingPlanner.Recovery(o with{CooldownSeconds=0},cfg,o.Life,o.MaximumLife,LevelingFor(worker.ProfileId).LifePercent,DateTime.UtcNow);if(later.Items.Length>0){int needed=cfg.ReserveMinutes>0?later.CooldownSeconds:later.CooldownSeconds-later.Items.Max(id=>SmartMedicalOptimizer.Item(id).CooldownSeconds)+1;available=DateTime.UtcNow.AddSeconds(Math.Max(1,needed-(o.MaximumSeconds-cfg.ReserveMinutes*60-o.CooldownSeconds)));p=p with{State="Cooldown",Reason="Waiting for enough medical capacity to reach the configured life target. Drugs remain excluded."};}}
  return new(p.State=="Ready"?"Recovered":p.State=="Heal first"?"Ready":p.State,p.Reason,p.Items,p.CooldownSeconds,Math.Max(0,(int)(o.HospitalUntilUtc-DateTime.UtcNow).TotalSeconds),0,available);
 }
 private async Task LevelingWorkerControlAsync(string action,JsonElement request){
  string profile=ReadWorkerString(request,"profileId");if(profile!=CurrentProfileId)throw new InvalidOperationException("Account changed; refresh Leveling first.");
  var existing=_levelingRuns.LastOrDefault(x=>x.Key.ProfileId==profile&&x.Value.Active);
  if(action!="levelStart"){
   if(existing.Key==null)return;
   if(action=="levelStop"){await existing.Key.StopAsync();existing.Value.Active=false;existing.Value.Claim="";existing.Value.Status="Stopped";LevelingFor(profile).PendingTarget=0;LevelingFor(profile).OpenedUtc=null;SaveLeveling(profile);}
   else if(action=="levelPause")await existing.Key.PauseAsync();
   else if(action=="levelResume")await existing.Key.ResumeAsync();
   UpdateModernDashboard();return;
  }
  if(_levelingBusy||_lossStartBusy)throw new InvalidOperationException("Another start/check is in progress.");
  _levelingBusy=true;
  try{
   EnsureLevelingProfileIdle(profile);var cfg=LevelingFor(profile);
   if(cfg.Targets.All(x=>x.Finished))throw new InvalidOperationException("Import targets or start a new pass first.");
   if(cfg.OpenedUtc!=null)throw new InvalidOperationException("Check or skip the pending manual fight first.");
   if(cfg.WeaponSlot is not ("weapon_primary" or "weapon_secondary" or "weapon_melee"))throw new InvalidDataException("Invalid fighting weapon slot.");
   bool slotA=_lossWorkerA?.IsArmed!=true;if(!slotA&&_lossWorkerB?.IsArmed==true)throw new InvalidOperationException("Stop a worker to free a fight window first.");
   var account=await ReadLevelingAccountAsync(profile);await RefreshLevelingStrengthAsync(profile);
   if(!cfg.Targets.Any(t=>!t.Finished&&LevelingScouting.StrengthEligible(t,cfg,DateTime.UtcNow)))throw new InvalidDataException("No targets have usable strength estimates within your limit. Scan the list or find FFScouter targets first.");
   var worker=await InitializeLossWorkerAsync(slotA,profile);var run=new LevelingRun{Player=account.Player};
   _levelingRuns[worker]=run;_medicalSessions[worker]=new MedicalSession{Order=run.Id,Player=account.Player,Leveling=true};
   await worker.StartAsync(new{mode="level",profileId=profile,slot=worker.Slot,orderId=run.Id,targetId=cfg.Targets.First(x=>!x.Finished).Id.ToString(),selfName=account.Name,playerId=account.Player,smartMedical=true,medicalSession=_medicalSessions[worker].Id,weaponSlot=cfg.WeaponSlot,attackDelayMs=cfg.AttackDelayMs,maxKits=8});
   cfg.Note="Automatic leveling started. The worker verifies its own weapon, finishes wins with Leave, and verifies official receipts before advancing.";SaveLeveling(profile);UpdateModernDashboard();
  }finally{_levelingBusy=false;}
 }
 private bool LevelingRunContext(LossAutoCycleWorker worker,LevelingRun run,JsonElement request)=>worker.IsArmed&&run.Active&&ReferenceEquals(worker,worker.Slot=="A"?_lossWorkerA:_lossWorkerB)&&ReadWorkerString(request,"profileId")==worker.ProfileId&&ReadWorkerString(request,"orderId")==run.Id;
 private async Task HandleLevelingWorkerAsync(LossAutoCycleWorker worker,JsonElement request){
  if(!_levelingRuns.TryGetValue(worker,out var run)||!LevelingRunContext(worker,run,request))return;
  string type=ReadWorkerString(request,"type"),rid=ReadWorkerString(request,"requestId"),claim=ReadWorkerString(request,"claimId");
  if(type=="tc-level-status"){run.Status=ReadWorkerString(request,"stage")+": "+ReadWorkerString(request,"status");if(DateTime.UtcNow-run.LastStatus>TimeSpan.FromSeconds(1)){run.LastStatus=DateTime.UtcNow;UpdateLossDashboardStatus();}return;}
  if(type=="tc-level-abandon"){if(claim==run.Claim)run.Claim="";return;}
  if(type=="tc-level-skip"){
   if(claim==run.Claim&&claim!=""){var session=LevelingFor(worker.ProfileId);var target=session.Targets.FirstOrDefault(x=>x.Id==run.Target);if(target!=null){target.Finished=true;target.Status="Skipped: became unavailable on attack page";}run.Claim="";session.PendingTarget=0;session.OpenedUtc=null;SaveLeveling(worker.ProfileId);}return;
  }
  if(type=="tc-level-finished"){run.Active=false;worker.MarkFinished();UpdateModernDashboard();return;}
  if(run.Busy)return;run.Busy=true;
  try{
   var session=LevelingFor(worker.ProfileId);string key=CredentialManager.LoadApiKey(worker.ProfileId)??throw new InvalidDataException("Missing API key.");
   if(type=="tc-level-claim"){
    if(run.Claim!=""){await worker.ReplyLevelingAsync(new{kind="claim",requestId=rid,ok=true,claimId=run.Claim,targetId=run.Target,message="Continuing the same leveling fight."});return;}
    if(session.OwnStatsUtc==null||DateTime.UtcNow-session.OwnStatsUtc>TimeSpan.FromMinutes(10))await RefreshLevelingStrengthAsync(worker.ProfileId);
    int checks=0;
    foreach(var target in session.Targets.Where(x=>!x.Finished).ToList()){
     if(checks>=10)break;
     if(!LevelingScouting.CanProbe(target,DateTime.UtcNow))continue;
     if(target.Id==run.Player){target.Finished=true;target.Status="Own account excluded";continue;}
     target.Suitability=LevelingScouting.Suitability(target,session,DateTime.UtcNow);if(!LevelingScouting.StrengthEligible(target,session,DateTime.UtcNow))continue;
     checks++;
     using var doc=JsonDocument.Parse(await _api.FetchEndpointAsync("user/"+target.Id+"/profile",key));var t=doc.RootElement.GetProperty("profile");
     if(t.GetProperty("id").GetInt32()!=target.Id)throw new InvalidDataException("Target identity mismatch.");
     target.Status=t.GetProperty("status").GetProperty("state").GetString()??"Unknown";target.UnavailableUntilUtc=LevelingScouting.Time(t.GetProperty("status"),"until");target.Name=t.GetProperty("name").GetString()??"";target.Level=t.GetProperty("level").GetInt32();target.CheckedUtc=DateTime.UtcNow;
     target.LastActionUtc=LevelingScouting.Time(t.GetProperty("last_action"),"timestamp");target.Suitability=LevelingScouting.Suitability(target,session,DateTime.UtcNow);
     if(!LevelingRunContext(worker,run,request))return;
     await worker.ReplyLevelingAsync(new{kind="scan",requestId=rid,message="Checked "+session.Targets.Count(x=>x.Finished)+"/"+session.Targets.Count+" targets. "+target.Name+": "+target.Status+". Looking for an available target."});
     if(!LevelingScouting.Eligible(target,session,DateTime.UtcNow)){target.Finished=true;target.Status="Skipped: "+target.Suitability;continue;}
     if(target.Status=="Okay"){
    using var self=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key));var p=self.RootElement.GetProperty("profile");
    if(p.GetProperty("id").GetInt32()!=run.Player)throw new InvalidDataException("API account changed; leveling paused.");
    var state=p.GetProperty("status").GetProperty("state").GetString();int life=p.GetProperty("life").GetProperty("current").GetInt32(),maximum=p.GetProperty("life").GetProperty("maximum").GetInt32();
    using var bars=JsonDocument.Parse(await _api.FetchEndpointAsync("user/bars",key));int energy=bars.RootElement.GetProperty("bars").GetProperty("energy").GetProperty("current").GetInt32();
    if(!LevelingRunContext(worker,run,request))return;
    if(_levelingAccounts.TryGetValue(worker.ProfileId,out var currentAccount))_levelingAccounts[worker.ProfileId]=currentAccount with{Energy=energy,Life=life,Maximum=maximum,Status=state??"Unknown",At=DateTime.UtcNow};
    if(energy-session.EnergyReserve<25||session.AttackLimit>0&&session.Wins+session.OtherResults>=session.AttackLimit){await worker.ReplyLevelingAsync(new{kind="claim",requestId=rid,ok=false,message="Complete: energy reserve or attack limit reached."});return;}
    if(state=="Hospital"||state=="Okay"&&life<(int)Math.Ceiling(maximum*session.LifePercent/100d)){await worker.ReplyLevelingAsync(new{kind="heal",requestId=rid});return;}
    if(state!="Okay")throw new InvalidDataException("Cannot level while account is "+state+".");
      run.Target=target.Id;run.Claim=Guid.NewGuid().ToString("N");run.Opened=DateTime.UtcNow;session.PendingTarget=target.Id;session.OpenedUtc=run.Opened;SaveLeveling(worker.ProfileId);
      await worker.ReplyLevelingAsync(new{kind="claim",requestId=rid,ok=true,claimId=run.Claim,targetId=run.Target,message="Leveling target verified. Opening fight."});return;
     }
     target.Finished=true;target.Status="Skipped: "+target.Status;
     if(checks>=10)break;
    }
    SaveLeveling(worker.ProfileId);await worker.ReplyLevelingAsync(new{kind="claim",requestId=rid,ok=false,message=checks>=10&&session.Targets.Any(x=>LevelingScouting.CanProbe(x,DateTime.UtcNow)&&LevelingScouting.StrengthEligible(x,session,DateTime.UtcNow))?"Checking the next batch of eligible targets.":"Complete: no eligible targets remain in this pass. Review estimates or filters before starting another pass."});
   }else if(type=="tc-level-commit"){
    if(claim==run.LastCommitted&&claim!=""){await worker.ReplyLevelingAsync(new{kind="commit",claimId=claim,remaining=run.LastRemaining,message=run.LastMessage,stop=run.LastStop});return;}
    if(claim==""||claim!=run.Claim)throw new InvalidDataException("No matching leveling fight reservation.");
    string json=await _api.FetchEndpointAsync("user/attacks?filters=outgoing&limit=100&sort=desc&from="+new DateTimeOffset(run.Opened).ToUnixTimeSeconds(),key);
    if(!LevelingRunContext(worker,run,request))return;
    var result=LevelingPlanner.FindReceipt(json,run.Player,run.Target,run.Opened,session.AttackIds);
    if(result==null){await worker.ReplyLevelingAsync(new{kind="pending",claimId=claim,message="Waiting for Torn's official attack log. No new attack will start."});return;}
    var receipt=result.Value;session.AttackIds.Add(receipt.Id);bool won=receipt.Result=="Attacked";if(won)session.Wins++;else session.OtherResults++;
    var target=session.Targets.First(x=>x.Id==run.Target);target.Finished=true;target.Status="Verified: "+receipt.Result;session.PendingTarget=0;session.OpenedUtc=null;
    run.LastCommitted=claim;run.Claim="";run.LastStop=!won;run.LastRemaining=won?session.Targets.Count(x=>!x.Finished):0;run.LastMessage=won?"Verified Leave win.":"Stopped after "+receipt.Result+". Review target difficulty and weapon before restarting.";session.Note=run.LastMessage;SaveLeveling(worker.ProfileId);
    await worker.ReplyLevelingAsync(new{kind="commit",claimId=claim,remaining=run.LastRemaining,message=run.LastMessage,stop=run.LastStop});UpdateModernDashboard();
   }
  }catch(Exception ex){LevelingFor(worker.ProfileId).Note=ex is InvalidDataException?ex.Message:"Leveling API check failed. Review API access or rate-limit status; no new attack authorized.";await worker.ReplyLevelingAsync(new{kind="paused",message=LevelingFor(worker.ProfileId).Note});}
  finally{run.Busy=false;}
 }
}
