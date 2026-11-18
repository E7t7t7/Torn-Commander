using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly Dictionary<string,BrowserTab> _missionTabs=new();
 private readonly Dictionary<string,BrowserTab> _missionItemTabs=new();
 private sealed class MissionItemResult {public bool Ok{get;set;}public string Phase{get;set;}="";public string Note{get;set;}="";}
 private readonly Dictionary<LossAutoCycleWorker,MissionRun> _missionRuns=new();
 private bool _missionBusy,_missionStop,_missionBatchBusy;private string _missionBatchProfile="";
 private long _missionGeneration;private string _missionJobProfile="";
 private sealed class MissionRun {
  public string Id="mission-"+Guid.NewGuid().ToString("N"),Profile="",Contract="",Claim="",LastClaim="",Status="Starting";public int Player,Index,StartIndex;
  public DateTime Opened;public bool Active=true,Busy;public MissionPlan Plan=new();public MissionOptions Options=new();
  public TaskCompletionSource<bool> Finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
 }
 private MissionJournal MissionsFor(string profile)=>IntelStore.Load(profile,"missions",new MissionJournal());
 private void SaveMissions(string profile,MissionJournal j){IntelStore.Save(profile,"missions",j);UpdateModernDashboard();}
 private object MissionDashboard(){
  var j=MissionsFor(CurrentProfileId);var run=_missionRuns.LastOrDefault(x=>x.Key.ProfileId==CurrentProfileId&&x.Value.Active);
  return new{profileId=CurrentProfileId,busy=_missionBusy||_missionBatchBusy,running=run.Key!=null,status=run.Value?.Status??j.Note,j.ScannedUtc,j.PendingAction,pendingFight=j.PendingFight!=null,options=j.Options,
   verifiedAttacks=j.Attacks.Count,rewardRequests=j.RewardRequests.Count,rows=j.Cards.Select(c=>new{card=c,plan=MissionPlanner.Plan(c)})};
 }
 private async Task<T> MissionBrowser<T>(string profile,object request){
  if(profile!=CurrentProfileId||!_missionTabs.TryGetValue(profile,out var tab)||!_browserTabObjects.Contains(tab))throw new InvalidDataException("Open this account's Missions page and scan again.");
  if(Active()!=tab){_browserTabs.SelectedTab=tab.Page;await Task.Delay(150);}
  var code=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"BundledScripts","Torn_Missions.js"));
  var raw=await tab.ExecuteScriptAsync(code.Replace("__MISSION_REQUEST__",JsonSerializer.Serialize(request)));
  return JsonSerializer.Deserialize<T>(raw??"null",new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Missions page did not respond.");
 }
 private async Task<(int Player,string Name)> MissionOwner(string profile){
  var key=CredentialManager.LoadApiKey(profile)??throw new InvalidDataException("Save this account's Torn API key first.");
  using var d=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,fresh:true));var p=d.RootElement.GetProperty("profile");
  return(p.GetProperty("id").GetInt32(),p.GetProperty("name").GetString()??"");
 }
 private async Task<MissionBoard> ReadMissionBoard(string profile,int player){
  var board=await MissionBrowser<MissionBoard>(profile,new{action="scan",owner=player});
  if(!board.Ok||board.Owner!=player)throw new InvalidDataException(board.Note.Length>0?board.Note:"Mission account did not match.");
  var j=MissionsFor(profile);j.Cards=board.Cards;j.ScannedUtc=DateTime.UtcNow;j.Note=board.Note;SaveMissions(profile,j);return board;
 }
 private async Task<MissionCard> SelectMission(string profile,int player,MissionCard card){
  var result=await MissionBrowser<MissionBoard>(profile,new{action="select",owner=player,id=card.Id,title=card.Title});
  if(!result.Ok)throw new InvalidDataException(result.Note);
  await Task.Delay(400);var board=await ReadMissionBoard(profile,player);
  return board.Cards.SingleOrDefault(c=>c.Id==card.Id&&c.Title==card.Title)??throw new InvalidDataException("Selected mission changed. Scan again.");
 }
 private async Task MissionNativeAction(string profile,int player,MissionCard card,string kind){
  var j=MissionsFor(profile);if(j.PendingFight!=null)throw new InvalidDataException("Scan the previous mission attack receipt before accepting or claiming another contract.");if(j.PendingAction!="")throw new InvalidDataException("A mission action is unverified. Check the game, then use Clear interrupted action before issuing another.");
  if(_missionStop&&_missionBusy)throw new InvalidDataException("Mission session stopped.");
  card=await SelectMission(profile,player,card);
  if(kind=="accept"&&!card.CanAccept||kind=="complete"&&!card.CanComplete)throw new InvalidDataException("The mission no longer has that action available.");
  if(_missionStop&&_missionBusy)throw new InvalidDataException("Mission session stopped.");
  j=MissionsFor(profile);j.PendingAction=Guid.NewGuid().ToString("N");j.PendingContract=card.Key;j.PendingKind=kind;SaveMissions(profile,j);
  var result=await MissionBrowser<MissionBoard>(profile,new{action=kind,owner=player,id=card.Id,title=card.Title,signature=card.Signature,token=j.PendingAction});
  if(!result.Ok){j.PendingAction="";j.Note=result.Note;SaveMissions(profile,j);throw new InvalidDataException(result.Note);}
  for(int i=0;i<12;i++){
   await Task.Delay(500);var board=await ReadMissionBoard(profile,player);var next=board.Cards.FirstOrDefault(x=>x.Id==card.Id&&x.Title==card.Title);
   bool accepted=kind=="accept"&&next?.Active==true&&!next.CanAccept;
   bool claimed=kind=="complete"&&(next==null||!next.CanComplete&&(next.Text.Contains("completed",StringComparison.OrdinalIgnoreCase)||next.CanAccept));
   if(!accepted&&!claimed)continue;
   j=MissionsFor(profile);j.PendingAction="";j.PendingContract="";j.PendingKind="";
   if(kind=="complete"){j.RewardRequests.Add(card.Key);j.Note="Native reward claim sent; the completed contract changed or left the page. Confirm credits/cash in Torn.";}
   else {j.Contract=card.Key;j.RunId=Guid.NewGuid().ToString("N");j.CompletedSteps=0;j.Progress[card.Key]=0;j.Plans.Remove(card.Key);j.Deliveries.RemoveAll(x=>x==card.Key);j.Note="Mission acceptance verified on Torn.";}
   SaveMissions(profile,j);return;
  }
  throw new InvalidDataException("The mission action was sent but its page result is not verified. No repeated click. Check Torn before clearing the interrupted action.");
 }
 private async Task HandleMissions(string action,JsonElement request,bool fromBatch=false){
  string profile=ReadWorkerString(request,"profileId");if(profile!=CurrentProfileId)return;
  long generation=_missionGeneration;
  try{
   var current=_missionRuns.LastOrDefault(x=>x.Key.ProfileId==profile&&x.Value.Active);
   if(action=="missionPause"){if(current.Key!=null){await current.Key.PauseAsync();current.Value.Status="Paused by you";}return;}
   if(action=="missionResume"){if(current.Key!=null){await current.Key.ResumeAsync();current.Value.Status="Resuming the same mission fight";}return;}
   if(action=="missionStop"){
    if(_missionBatchBusy&&_missionBatchProfile!=profile)return;
    if(_missionBusy&&_missionJobProfile!=profile)return;
    _missionGeneration++;_missionStop=true;if(current.Key!=null){current.Value.Active=false;await current.Key.StopAsync();current.Key.MarkFinished();current.Value.Finished.TrySetResult(false);}
    var stopped=MissionsFor(profile);stopped.Note="Stopped. Verified attack history retained; no automatic restart.";SaveMissions(profile,stopped);return;
   }
   if(action=="missionOpen"){await AddBrowserTabAsync(TornRoutes.Missions);if(Active() is {} t)_missionTabs[profile]=t;return;}
   if(action=="missionInventory"){await MissionInventoryTab(profile);return;}
   if(action=="missionClear"){
    if(_missionBusy||_missionBatchBusy)throw new InvalidDataException("Stop the mission session first.");
    var j=MissionsFor(profile);j.PendingAction="";j.PendingContract="";j.PendingKind="";j.Note="Interrupted action cleared by you. Re-scan Torn before acting again.";SaveMissions(profile,j);return;
   }
   if(action=="missionBatch"){await RunMissionBatch(profile,request);return;}
   if(_missionBatchBusy&&!fromBatch&&action is "missionRun" or "missionAccept" or "missionClaim" or "missionClear")throw new InvalidDataException("A mission batch is running. Stop it before starting another action.");
   var owner=await MissionOwner(profile);
   if(action=="missionScan"){
    var active=Active();var u=active?.WebView.Source;
    if(u!=null&&TornRoutes.IsTornHost(u.Host)&&u.Query.Split('&','?').Contains("sid=missions"))_missionTabs[profile]=active!;
    await ReconcileMissionFight(profile,owner.Player);await ReadMissionBoard(profile,owner.Player);return;
   }
   if(_missionBusy)throw new InvalidDataException("A mission session is already running. Pause or stop it first.");
   if(generation!=_missionGeneration)return;
   _missionBusy=true;_missionStop=false;_missionJobProfile=profile;
   try{
    var board=await ReadMissionBoard(profile,owner.Player);string id=ReadWorkerString(request,"id");
    var card=board.Cards.SingleOrDefault(x=>x.Id==id)??throw new InvalidDataException("Select a currently scanned mission.");
    if(action=="missionAccept"){await MissionNativeAction(profile,owner.Player,card,"accept");return;}
    if(action=="missionClaim"){await MissionNativeAction(profile,owner.Player,card,"complete");return;}
    if(action!="missionRun")throw new InvalidDataException("Unknown mission action.");
    var options=JsonSerializer.Deserialize<MissionOptions>(request.GetProperty("options").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new();options.Validate();var savedOptions=MissionsFor(profile);savedOptions.Options=options;SaveMissions(profile,savedOptions);
    if(MissionsFor(profile).PendingFight!=null)throw new InvalidDataException("A previous mission fight is unverified. Scan its receipt first.");
    var plan=MissionPlanner.Plan(card);if(!plan.Supported)throw new InvalidDataException(plan.Reason);
    if(card.CanComplete){await MissionNativeAction(profile,owner.Player,card,"complete");return;}
    card=await SelectMission(profile,owner.Player,card);
    if(card.CanAccept){await MissionNativeAction(profile,owner.Player,card,"accept");card=(await ReadMissionBoard(profile,owner.Player)).Cards.Single(x=>x.Id==id);}
    if(!card.Active)throw new InvalidDataException("An active accepted mission could not be verified. Select its contract in Torn and scan again.");
    plan=MissionPlanner.Plan(card);
    if(plan.Kind=="delivery"&&plan.Supported){await MissionDelivery(profile,owner.Player,card,options);return;}
    if(!plan.Supported||plan.Steps.Length==0)throw new InvalidDataException(plan.Reason);
    if(plan.Steps.Any(x=>x==owner.Player))throw new InvalidDataException("The mission target list includes this account. No fight authorized.");
    EnsureLevelingProfileIdle(profile);
    bool slotA=_lossWorkerA?.IsArmed!=true;if(!slotA&&_lossWorkerB?.IsArmed==true)throw new InvalidDataException("Free a worker window first.");
    if(_missionStop||profile!=CurrentProfileId)return;
    var journal=MissionsFor(profile);if(journal.PendingFight!=null)throw new InvalidDataException("A previous mission attack is unverified. Use Scan current missions to check its receipt before another fight.");if(journal.PendingAction!="")throw new InvalidDataException("Check the unverified mission action before fighting.");
    if(journal.Contract!=card.Key){journal.Contract=card.Key;journal.CompletedSteps=journal.Progress.GetValueOrDefault(card.Key);journal.RunId=Guid.NewGuid().ToString("N");SaveMissions(profile,journal);}
    if(journal.Plans.TryGetValue(card.Key,out var savedPlan)&&savedPlan.Distinct().Order().SequenceEqual(card.Targets.Distinct().Order()))plan.Steps=savedPlan;
    else{journal.Plans[card.Key]=plan.Steps;SaveMissions(profile,journal);}
    if(journal.CompletedSteps>=plan.Steps.Length)throw new InvalidDataException("Planned attacks are already verified. Re-scan and complete the objective; no extra fights will be issued.");
    var worker=await InitializeLossWorkerAsync(slotA,profile);
    var run=new MissionRun{Profile=profile,Player=owner.Player,Contract=card.Key,Plan=plan,Options=options,Index=journal.CompletedSteps,StartIndex=journal.CompletedSteps};
    _missionRuns[worker]=run;_medicalSessions[worker]=new MedicalSession{Order=run.Id,Player=owner.Player,Leveling=true,Mission=true};
    await worker.StartAsync(new{mode="level",mission=true,finishAction=plan.Action,profileId=profile,slot=worker.Slot,orderId=run.Id,targetId=plan.Steps[run.Index].ToString(),selfName=owner.Name,playerId=owner.Player,smartMedical=true,medicalSession=_medicalSessions[worker].Id,weaponSlot=options.Weapon,attackDelayMs=options.Delay,maxKits=8});
    UpdateModernDashboard();bool success=await run.Finished.Task;
    if(!success||_missionStop||profile!=CurrentProfileId)return;
    var after=await ReadMissionBoard(profile,owner.Player);var finished=after.Cards.FirstOrDefault(x=>x.Id==card.Id&&x.Title==card.Title);
    journal=MissionsFor(profile);
    if(finished?.CanComplete==true&&options.ClaimRewards)await MissionNativeAction(profile,owner.Player,finished,"complete");
    else{journal.Note=finished?.CanComplete==true?"Objective complete on Torn. Press Claim reward.":"Planned attacks verified. Torn has not exposed Complete yet; scan again. No additional attacks issued.";SaveMissions(profile,journal);}
   }finally{_missionBusy=false;_missionJobProfile="";UpdateModernDashboard();}
  }catch(Exception ex){foreach(var orphan in _missionRuns.Where(x=>x.Key.ProfileId==profile&&x.Value.Active&&!x.Key.IsArmed)){orphan.Value.Active=false;orphan.Value.Finished.TrySetResult(false);}var j=MissionsFor(profile);j.Note=ex is InvalidDataException or InvalidOperationException?ex.Message:"Mission action paused after a provider or connection error. No uncertain action was retried.";SaveMissions(profile,j);}
  finally{UpdateModernDashboard();}
 }
 private async Task RunMissionBatch(string profile,JsonElement request){
  if(_missionBusy||_missionBatchBusy)throw new InvalidDataException("A mission session is already running.");
  var options=JsonSerializer.Deserialize<MissionOptions>(request.GetProperty("options").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new();options.Validate();
  if(!options.ClaimRewards)throw new InvalidDataException("Enable reward claims for a supported-missions batch, or run one contract separately.");
  _missionBatchBusy=true;_missionBatchProfile=profile;_missionStop=false;long generation=_missionGeneration;
  var seen=new HashSet<string>();int remaining=options.MaximumAttacks,originalBudget=options.MaximumAttacks;
  try{
   for(int contract=0;contract<5&&!_missionStop&&profile==CurrentProfileId&&generation==_missionGeneration;contract++){
    var owner=await MissionOwner(profile);var board=await ReadMissionBoard(profile,owner.Player);var j=MissionsFor(profile);
    if(j.PendingAction!=""||j.PendingFight!=null)throw new InvalidDataException("Reconcile the pending mission action/fight before starting a batch.");
    var next=MissionPlanner.Next(board.Cards,seen,remaining,options.AllowItemSend);if(next==null){j.Note="Batch finished: no further supported contracts within your remaining attack budget. Special tasks remain manual.";SaveMissions(profile,j);return;}
    if(_missionStop||profile!=CurrentProfileId||generation!=_missionGeneration)return;
    seen.Add(next.Key);int before=j.Attacks.Count;options.MaximumAttacks=Math.Max(1,remaining);
    var command=JsonSerializer.SerializeToElement(new{profileId=profile,id=next.Id,options});
    await HandleMissions(next.CanComplete?"missionClaim":"missionRun",command,true);
    j=MissionsFor(profile);remaining=Math.Max(0,remaining-(j.Attacks.Count-before));
    if(_missionStop||profile!=CurrentProfileId||generation!=_missionGeneration||j.PendingAction!=""||j.PendingFight!=null)return;
    var after=await ReadMissionBoard(profile,owner.Player);var still=after.Cards.FirstOrDefault(c=>c.Key==next.Key);
    if(still!=null&&!still.CanAccept){j=MissionsFor(profile);j.Note="Batch paused: the last contract is still present. Check its objective or reward status before another mission.";SaveMissions(profile,j);return;}
   }
  }finally{_missionBatchBusy=false;_missionBatchProfile="";var saved=MissionsFor(profile);saved.Options.MaximumAttacks=originalBudget;SaveMissions(profile,saved);UpdateModernDashboard();}
 }
 private async Task<BrowserTab> MissionInventoryTab(string profile){
  if(profile!=CurrentProfileId)throw new InvalidDataException("Account changed.");
  if(_missionItemTabs.TryGetValue(profile,out var existing)&&_browserTabObjects.Contains(existing)){_browserTabs.SelectedTab=existing.Page;return existing;}
  await AddBrowserTabAsync(TornRoutes.Items);var tab=Active()??throw new InvalidDataException("Items browser unavailable.");_missionItemTabs[profile]=tab;return tab;
 }
 private async Task MissionDelivery(string profile,int player,MissionCard card,MissionOptions options){
  if(!options.AllowItemSend)throw new InvalidDataException("Enable exact mission item sends before running this delivery. No item sent.");
  var j=MissionsFor(profile);
  if(j.Deliveries.Contains(card.Key))throw new InvalidDataException("A delivery for this contract is already verified. Re-scan the objective; it will not be sent twice.");
  if(j.PendingAction!="")throw new InvalidDataException("Review the interrupted mission action before another transfer.");
  if(card.Targets.Length!=1||card.Targets[0]==player||card.ItemId<=0||card.ItemName=="")throw new InvalidDataException("Exact mission item or recipient is missing.");
  var tab=await MissionInventoryTab(profile);string code=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"BundledScripts","Torn_Mission_Item.js"));
  string token=Guid.NewGuid().ToString("N");j.PendingAction=token;j.PendingContract=card.Key;j.PendingKind="delivery";SaveMissions(profile,j);
  bool sent=false;var issued=new HashSet<string>();
  async Task<MissionItemResult> Browser(string action){
   if(_missionStop||profile!=CurrentProfileId||!_browserTabObjects.Contains(tab))throw new InvalidDataException("Mission delivery stopped or browser/account changed.");
   if(Active()!=tab){_browserTabs.SelectedTab=tab.Page;await Task.Delay(150);}
   var request=new{action,owner=player,recipient=card.Targets[0],itemId=card.ItemId,itemName=card.ItemName,token};
   var raw=await tab.ExecuteScriptAsync(code.Replace("__MISSION_ITEM_REQUEST__",JsonSerializer.Serialize(request)));
   return JsonSerializer.Deserialize<MissionItemResult>(raw??"null",new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Item page did not respond.");
  }
  try{
   await Task.Delay(1000);
   for(int step=0;step<24;step++){
    var state=await Browser("inspect");if(!state.Ok)throw new InvalidDataException(state.Note);
    if(state.Phase=="sent"){
     j=MissionsFor(profile);j.Deliveries.Add(card.Key);j.PendingAction="";j.PendingKind="";j.PendingContract="";j.Note="One item delivery verified by the native receipt.";SaveMissions(profile,j);
     var after=(await ReadMissionBoard(profile,player)).Cards.FirstOrDefault(c=>c.Id==card.Id&&c.Title==card.Title);
     if(after?.CanComplete==true&&options.ClaimRewards)await MissionNativeAction(profile,player,after,"complete");
     return;
    }
    if(new[]{"expand","openSend","fill","send"}.Contains(state.Phase)&&issued.Add(state.Phase)){
     if(state.Phase=="send"){
      var fresh=(await ReadMissionBoard(profile,player)).Cards.SingleOrDefault(c=>c.Id==card.Id&&c.Title==card.Title);
      if(fresh==null||fresh.Signature!=card.Signature||!fresh.Active||fresh.CanComplete)throw new InvalidDataException("Mission item/recipient objective changed before Send. No transfer.");
      j=MissionsFor(profile);j.PendingKind="delivery-sent";SaveMissions(profile,j);sent=true;
     }
     var action=await Browser(state.Phase);if(!action.Ok)throw new InvalidDataException(action.Note);
    }
    await Task.Delay(500);
   }
   throw new InvalidDataException(sent?"Item Send was dispatched but its native receipt is unverified. Do not send again; check Torn.":"The native item or recipient controls could not be verified. Open the exact item's Send form manually, then retry. No item sent.");
  }catch{
   if(!sent){j=MissionsFor(profile);j.PendingAction="";j.PendingKind="";j.PendingContract="";SaveMissions(profile,j);}
   throw;
  }
 }
 private async Task ReconcileMissionFight(string profile,int player){
  var j=MissionsFor(profile);if(j.PendingFight is not {} pending)return;
  if(_missionRuns.Any(x=>x.Key.ProfileId==profile&&x.Value.Active))return;
  var key=CredentialManager.LoadApiKey(profile)??throw new InvalidDataException("Missing account API key.");
  var json=await _api.FetchEndpointAsync("user/attacks?filters=outgoing&limit=100&sort=desc&from="+new DateTimeOffset(pending.Opened).ToUnixTimeSeconds(),key,fresh:true);
  var receipt=LevelingPlanner.FindReceipt(json,player,pending.Target,pending.Opened,j.Attacks.Select(x=>x.Id));
  if(receipt==null){j.Note="Previous mission attack remains unverified. No new fight will start.";SaveMissions(profile,j);return;}
  var r=receipt.Value;j.Attacks.Add(new(r.Id,pending.Contract,pending.Target,r.Result,DateTime.UtcNow));
  if(MissionPlanner.Matches(new MissionPlan{Action=pending.Action,AttackOnly=pending.AttackOnly},r.Result)){
   j.Progress[pending.Contract]=Math.Max(j.Progress.GetValueOrDefault(pending.Contract),pending.Index+1);
   if(j.Contract==pending.Contract)j.CompletedSteps=Math.Max(j.CompletedSteps,pending.Index+1);
  }
  j.PendingFight=null;j.Note="Recovered previous mission receipt: "+r.Result+". Scan the mission objective before continuing.";SaveMissions(profile,j);
 }
 private async Task HandleMissionWorker(LossAutoCycleWorker worker,JsonElement request){
  if(!_missionRuns.TryGetValue(worker,out var run)||!run.Active||!worker.IsArmed||ReadWorkerString(request,"profileId")!=worker.ProfileId||ReadWorkerString(request,"orderId")!=run.Id||!ReferenceEquals(worker,worker.Slot=="A"?_lossWorkerA:_lossWorkerB))return;
  string type=ReadWorkerString(request,"type"),rid=ReadWorkerString(request,"requestId"),claim=ReadWorkerString(request,"claimId");
  if(type=="tc-level-status"){run.Status=ReadWorkerString(request,"stage")+": "+ReadWorkerString(request,"status");return;}
  if(type=="tc-level-abandon")return;
  if(type=="tc-level-finished"){run.Active=false;worker.MarkFinished();run.Finished.TrySetResult(false);return;}
  if(type=="tc-level-skip"){run.Status="Mission target is unavailable or mission activation was not recognized. No replacement target selected.";await worker.PauseAsync();UpdateModernDashboard();return;}
  if(run.Busy)return;run.Busy=true;
  try{
   var j=MissionsFor(run.Profile);var key=CredentialManager.LoadApiKey(run.Profile)??throw new InvalidDataException("Missing account API key.");
   if(type=="tc-level-claim"){
    if(_missionStop||run.Profile!=CurrentProfileId)throw new InvalidDataException("Mission session stopped or account changed.");
    if(run.Claim!=""){await worker.ReplyLevelingAsync(new{kind="claim",requestId=rid,ok=true,claimId=run.Claim,targetId=run.Plan.Steps[run.Index]});return;}
    if(run.Index>=run.Plan.Steps.Length||run.Index-run.StartIndex>=run.Options.MaximumAttacks)throw new InvalidDataException("Mission attack budget exhausted.");
    using var d=JsonDocument.Parse(await _api.FetchEndpointAsync("user?selections=profile,bars",key,fresh:true));var p=d.RootElement.GetProperty("profile");var bars=d.RootElement.GetProperty("bars");
    if(p.GetProperty("id").GetInt32()!=run.Player)throw new InvalidDataException("API account changed. No fight authorized.");
    if(bars.GetProperty("energy").GetProperty("current").GetInt32()-run.Options.Reserve<25)throw new InvalidDataException("Mission energy reserve reached.");
    var state=p.GetProperty("status").GetProperty("state").GetString();
    if(state=="Hospital"||state=="Okay"&&bars.GetProperty("life").GetProperty("current").GetInt32()<bars.GetProperty("life").GetProperty("maximum").GetInt32()*run.Options.LifePercent/100d){await worker.ReplyLevelingAsync(new{kind="heal",requestId=rid});return;}
    if(state!="Okay")throw new InvalidDataException("This account is not ready to fight.");
    if(_missionStop||run.Profile!=CurrentProfileId||!run.Active)return;
    run.Opened=DateTime.UtcNow;run.Claim=Guid.NewGuid().ToString("N");
    j.PendingFight=new(run.Plan.Steps[run.Index],run.Opened,run.Claim,run.Contract,run.Index,run.Plan.Action,run.Plan.AttackOnly);SaveMissions(run.Profile,j);
    await worker.ReplyLevelingAsync(new{kind="claim",requestId=rid,ok=true,claimId=run.Claim,targetId=run.Plan.Steps[run.Index],message="Exact accepted mission target authorized."});
   }else if(type=="tc-level-commit"){
    if(claim==run.LastClaim&&claim!=""){await worker.ReplyLevelingAsync(new{kind="commit",claimId=claim,remaining=run.Plan.Steps.Length-run.Index,message="Mission receipt already recorded."});return;}
    if(claim==""||claim!=run.Claim)throw new InvalidDataException("No matching mission attack lease.");
    var json=await _api.FetchEndpointAsync("user/attacks?filters=outgoing&limit=100&sort=desc&from="+new DateTimeOffset(run.Opened).ToUnixTimeSeconds(),key,fresh:true);
    var receipt=LevelingPlanner.FindReceipt(json,run.Player,run.Plan.Steps[run.Index],run.Opened,j.Attacks.Select(x=>x.Id));
    if(receipt==null){await worker.ReplyLevelingAsync(new{kind="pending",claimId=claim,message="Waiting for the exact mission attack receipt. No new attack."});return;}
    var r=receipt.Value;j.PendingFight=null;j.Attacks.Add(new(r.Id,run.Contract,run.Plan.Steps[run.Index],r.Result,DateTime.UtcNow));run.LastClaim=claim;run.Claim="";
    if(!MissionPlanner.Matches(run.Plan,r.Result)){j.Note="Verified "+r.Result+". Mission requires "+run.Plan.Action+"; stopped for review.";SaveMissions(run.Profile,j);run.Active=false;await worker.StopAsync();worker.MarkFinished();run.Finished.TrySetResult(false);return;}
    run.Index++;j.CompletedSteps=run.Index;j.Progress[run.Contract]=run.Index;j.Note="Verified mission attack "+run.Index+"/"+run.Plan.Steps.Length+" ("+r.Result+").";SaveMissions(run.Profile,j);
    if(run.Index>=run.Plan.Steps.Length){run.Active=false;await worker.StopAsync();worker.MarkFinished();run.Finished.TrySetResult(true);return;}
    await worker.ReplyLevelingAsync(new{kind="commit",claimId=claim,remaining=run.Plan.Steps.Length-run.Index,message=j.Note});
   }
  }catch(Exception ex){run.Status=ex is InvalidDataException?ex.Message:"Mission verification failed; check API access or connection.";await worker.ReplyLevelingAsync(new{kind="paused",requestId=rid,claimId=claim,message=run.Status});var j=MissionsFor(run.Profile);j.Note=run.Status;SaveMissions(run.Profile,j);}
  finally{run.Busy=false;UpdateModernDashboard();}
 }
}
