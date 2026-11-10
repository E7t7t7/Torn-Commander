using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly FFSellerClient _ffSeller=new();
 private bool _ffBatchBusy;
 private readonly Dictionary<string,FFSellerState> _ffStates=new();
 private readonly HashSet<string> _ffBusy=new();
 private readonly Dictionary<string,DateTime> _ffPollAt=new();
 private readonly Dictionary<string,Task> _ffSyncTasks=new();
 private readonly HashSet<string> _ffControlBusy=new();
 private readonly Dictionary<string,int> _ffControlEpoch=new();
 private readonly Dictionary<string,string> _ffActivity=new();
 private void FFActivity(string profile,string message){_ffActivity[profile]=message;PushFF();}
 private LossAutoCycleWorker? FFWorker(string profile,long claimId)=>new[]{_lossWorkerA,_lossWorkerB}.FirstOrDefault(w=>w?.IsArmed==true&&w.ProfileId==profile&&_medicalSessions.TryGetValue(w,out var m)&&_lossOrders.Any(o=>o.Id==m.Order&&o.SourceService=="FFScouter"&&o.RemoteProfileId==profile&&o.RemoteClaimId==claimId));
 private object? FFWorkerView(string profile,long claimId){var w=FFWorker(profile,claimId);return w==null?null:new{slot=w.Slot,status=w.LastStatus,paused=w.LastStatus.StartsWith("paused:",StringComparison.OrdinalIgnoreCase)};}

 private FFSellerState FFFor(string profile){if(!_ffStates.TryGetValue(profile,out var s)){s=IntelStore.Load(profile,"ff-seller",new FFSellerState());_ffStates[profile]=s;if(FFSellerData.MigrateLimit(s))IntelStore.Save(profile,"ff-seller",s);}return s;}
 private void SaveFF(string profile)=>IntelStore.Save(profile,"ff-seller",FFFor(profile));
 private object FFDashboard(){var s=FFFor(CurrentProfileId);return new{profileId=CurrentProfileId,configured=!string.IsNullOrEmpty(CredentialManager.LoadSellerKey(CurrentProfileId)),busy=_ffBusy.Contains(CurrentProfileId),batchBusy=_ffBatchBusy,activity=_ffActivity.GetValueOrDefault(CurrentProfileId),controlBusy=_ffControlBusy.Contains(CurrentProfileId),s.Player,s.Enabled,s.AutoRun,s.AutoFinish,s.SessionLimit,s.Cap,s.LastSync,s.Note,s.Pending,totals=FFSellerData.Totals(s),contracts=s.Contracts,claims=s.Claims.Values.OrderByDescending(x=>x.ClaimedAt).Take(100).Select(c=>new{c.Id,c.OrderNumber,c.Target,c.State,c.Claimed,c.Delivered,c.Price,c.Earnings,c.PaymentStatus,c.Paid,c.Review,c.ExpiresAt,c.IdleDeadline,live=c.Live(DateTime.UtcNow),worker=FFWorkerView(CurrentProfileId,c.Id)}).ToList()};}
 private void PushFF(bool keySaved=false){if(_modernDashboard?.CoreWebView2!=null)_modernDashboard.CoreWebView2.PostWebMessageAsJson(DashboardJson.Serialize(new{type="ffSeller",data=FFDashboard(),keySaved}));}
 private async Task<string> FFSellerKeyAsync(string profile){
  var s=FFFor(profile);var key=CredentialManager.LoadSellerKey(profile)??throw new InvalidOperationException("Connect this profile's FFScouter seller key first.");
  if(s.Player<=0||s.KeyHash!=FFSellerData.KeyHash(key))throw new InvalidOperationException("Seller credential changed. Reconnect to verify its account.");
  if(DateTime.UtcNow-s.OwnerChecked>TimeSpan.FromMinutes(5)){
   using var owner=JsonDocument.Parse(await _ffSeller.Send(key,"account/balance"));
   if(owner.RootElement.GetProperty("player_id").GetInt32()!=s.Player)throw new InvalidDataException("Seller account changed. Workers remain blocked.");
   s.OwnerChecked=DateTime.UtcNow;
  }
  return key;
 }
 private void LinkFFOrders(string profile){
  var s=FFFor(profile);
  foreach(var c in s.Claims.Values){
   var order=_lossOrders.FirstOrDefault(o=>o.SourceService=="FFScouter"&&o.RemoteOwner==s.Player&&o.RemoteClaimId==c.Id);
   if(order==null){
    order=new LossOrder{Id="ff-"+s.Player+"-"+c.Id,SourceService="FFScouter",RemoteOwner=s.Player,RemoteProfileId=profile,RemoteClaimId=c.Id,ProfileAId=profile,ProfileBId="",Notes="FFScouter seller API claim; one owning account only."};_lossOrders.Insert(0,order);
   }
   if(order.RemoteProfileId!=profile)throw new InvalidDataException("This remote claim is already linked to a different local profile.");
   order.RemoteOrderNumber=c.OrderNumber;order.TargetPlayerId=c.Target>0?c.Target.ToString():"";order.BuyerName="FF "+c.OrderNumber+(c.TargetName!=""?" · "+c.TargetName:"");
   order.RequestedLosses=c.Claimed;order.PayPerLoss=c.Price;order.RemoteDelivered=c.Delivered;order.RemoteAttackIds=c.AttackIds.ToList();
   order.RemoteState=c.State;order.RemoteDeadline=c.Deadline;order.RemoteObservedUtc=s.LastSync;order.RemotePaidCash=c.Paid?c.Earnings:null;
   if(c.ClaimedAt>0)order.CreatedAt=DateTimeOffset.FromUnixTimeSeconds(c.ClaimedAt).UtcDateTime;
   if(c.State!="active"&&!string.IsNullOrEmpty(c.State)&&!order.IsClosed){order.ClosedUtc=DateTime.UtcNow;order.Status="Provider "+c.State;order.NextA=order.NextB="Done";}
  }
  SaveLossOrders();RebindLossOrders();
 }
 private async Task SyncFFCore(string profile){
  if(_ffSyncTasks.TryGetValue(profile,out var existing)){await existing;return;}
  var task=SyncFFCoreImpl(profile);_ffSyncTasks[profile]=task;
  try{await task;}finally{if(_ffSyncTasks.GetValueOrDefault(profile)==task)_ffSyncTasks.Remove(profile);}
 }
 private async Task SyncFFCoreImpl(string profile){
  var s=FFFor(profile);var key=await FFSellerKeyAsync(profile);
  FFActivity(profile,"Refreshing available contracts (provider requests are paced)…");
  var contractJson=await _ffSeller.Send(key,"losses/seller/contracts");
  FFActivity(profile,"Refreshing claim progress and payments…");
  var claimJson=await _ffSeller.Send(key,"losses/seller/claims");
  var contracts=FFSellerData.Contracts(contractJson);FFSellerData.MergeClaims(s,claimJson);s.Contracts=contracts.Contracts;s.Cap=contracts.Cap;s.LastSync=DateTime.UtcNow;
  if(s.Pending=="claim"&&s.Claims.Values.Any(c=>c.OrderNumber==s.PendingOrder&&!s.PendingKnownIds.Contains(c.Id)&&c.ClaimedAt>=new DateTimeOffset(s.PendingAt).ToUnixTimeSeconds()-5))s.Pending="";
  if(s.Pending=="complete"&&s.Claims.TryGetValue(s.PendingClaim,out var complete)&&complete.State!="active")s.Pending="";
  s.Note="Seller data synchronized; older history retained locally.";SaveFF(profile);LinkFFOrders(profile);
  foreach(var worker in new[]{_lossWorkerA,_lossWorkerB}){
   if(worker==null||!_medicalSessions.TryGetValue(worker,out var med)||med.Leveling)continue;
   var order=_lossOrders.FirstOrDefault(o=>o.Id==med.Order&&o.SourceService=="FFScouter"&&o.RemoteProfileId==profile);
   if(order==null)continue;
   await worker.SetExternalDeadlineAsync(order.RemoteDeadline*1000);
   if(worker.IsArmed&&(!FFExecutable(order,profile)||order.IsClosed)){
    if(med.Issued&&!med.Verified){await worker.PauseAsync();s.Note="Ended claim paused with medical verification pending; review before starting another worker.";}
    else await StopLossWorkerAsync(worker.Slot=="A");
   }
  }
 }
 private bool FFExecutable(LossOrder o,string profile)=>FFSellerData.CanExecute(o,FFFor(profile),profile,DateTime.UtcNow);
 private async Task FFDetail(string profile,FFSellerClaim c){
  string key=await FFSellerKeyAsync(profile);
  using var doc=JsonDocument.Parse(await _ffSeller.Send(key,"losses/seller/orders/"+Uri.EscapeDataString(c.OrderNumber)));
  var matches=doc.RootElement.GetProperty("claims").EnumerateArray().Where(x=>FFSellerData.Number(x,"id")==c.Id).ToList();
  if(matches.Count!=1)throw new InvalidDataException("Order details did not identify this seller claim.");
  var state=FFFor(profile);state.Claims[c.Id]=FFSellerData.Merge(matches[0],c);state.LastSync=DateTime.UtcNow;SaveFF(profile);LinkFFOrders(profile);
 }
 private async Task<bool> FFBeforeClaimAsync(LossAutoCycleWorker worker,string orderId){
  var o=_lossOrders.FirstOrDefault(x=>x.Id==orderId);if(o==null||o.SourceService!="FFScouter")return true;
  string profile=worker.ProfileId;if(o.RemoteProfileId!=profile||o.IsClosed||o.RemoteOwner!=FFFor(profile).Player)return false;
  if(!_medicalSessions.TryGetValue(worker,out var med)||med.Player!=o.RemoteOwner)return false;
  var localIds=o.CommitReceipts.Where(x=>x.AttackId>0).Select(x=>x.AttackId).ToList();
  if(FFExecutable(o,profile)&&DateTime.UtcNow-o.RemoteObservedUtc<TimeSpan.FromSeconds(10)&&localIds.All(o.RemoteAttackIds.Contains))return true;
  if(!_ffBusy.Add(profile))return false;
  try{
   var c=FFFor(profile).Claims[o.RemoteClaimId];await FFDetail(profile,c);
   return FFExecutable(o,profile)&&localIds.All(o.RemoteAttackIds.Contains);
  }catch{FFFor(profile).Note="Waiting for FFScouter delivery confirmation. No new attack authorized.";return false;}
  finally{_ffBusy.Remove(profile);PushFF();}
 }
 private async Task FFMutation(string profile,string type,string order,int count,long claimId=0){
  var s=FFFor(profile);string key=await FFSellerKeyAsync(profile);
  await FFSellerOperations.Execute(s,(route,body)=>_ffSeller.Send(key,route,body),()=>SaveFF(profile),type,order,count,claimId);
  LinkFFOrders(profile);
 }

 private async Task StartFFOrder(string profile,FFSellerClaim c,bool unattended=false){
  var o=_lossOrders.Single(x=>x.SourceService=="FFScouter"&&x.RemoteProfileId==profile&&x.RemoteClaimId==c.Id);
  if(!c.Live(DateTime.UtcNow)||!FFExecutable(o,profile)||o.Remaining<=0)throw new InvalidOperationException("Claim is completed, expired, stale, or not ready.");
  if(string.IsNullOrEmpty(CredentialManager.LoadApiKey(profile)))throw new InvalidOperationException("Save the owning profile's Torn API key before running its worker.");
  if(new[]{_lossWorkerA,_lossWorkerB}.Any(w=>w?.IsArmed==true&&w.ProfileId==profile))throw new InvalidOperationException("This profile already has an active worker.");
  bool slotA=_lossWorkerA?.IsArmed!=true;if(!slotA&&_lossWorkerB?.IsArmed==true)throw new InvalidOperationException("Both workers are busy. No remote claim was duplicated.");
  if(_lossStartBusy)throw new InvalidOperationException("A worker is already starting.");
  o.ProfileAId=slotA?profile:"";o.ProfileBId=slotA?"":profile;SaveLossOrders();RebindLossOrders();
  var previous=SelectedLossOrder()?.Id;SelectLossOrder(o.Id);_lossStartBusy=true;
  try{if(!await StartLossWorkerCoreAsync(slotA,unattended))throw new InvalidOperationException("Worker did not start. Review this account and use Start explicitly.");}finally{_lossStartBusy=false;if(previous!=null)SelectLossOrder(previous);}
 }
 private async Task PrepareFFExecution(string profile,long id){
  var s=FFFor(profile);
  bool refreshing=_ffSyncTasks.TryGetValue(profile,out var sync);
  if(refreshing&&DateTime.UtcNow-s.LastSync>TimeSpan.FromSeconds(15))await sync!;
  else if(!refreshing&&_ffBusy.Contains(profile))throw new InvalidOperationException("A seller reservation/completion is running. Wait for its result before starting or resuming.");
  if(s.Pending!="")throw new InvalidOperationException("Reconcile the previous seller request before starting or resuming.");
  var c=s.Claims.GetValueOrDefault(id)??throw new InvalidDataException("Unknown owning claim.");
  if(!c.Live(DateTime.UtcNow))throw new InvalidOperationException("Claim ended or expired. No worker resumed.");
  await FFSellerKeyAsync(profile);
  if(DateTime.UtcNow-s.LastSync>TimeSpan.FromSeconds(15)){FFActivity(profile,"Checking this claim before starting/resuming…");await FFDetail(profile,c);}
 }
 private async Task HandleFFControl(string action,string profile,long id){
  var s=FFFor(profile);
  if(action=="ffPause"){
   _ffControlEpoch[profile]=_ffControlEpoch.GetValueOrDefault(profile)+1;
   var w=FFWorker(profile,id)??throw new InvalidOperationException("No running worker is linked to this claim.");
   await w.PauseAsync();s.Note="Paused this claim's worker. No remote slots released.";PushFF();UpdateLossDashboardStatus();return;
  }
  if(!_ffControlBusy.Add(profile))throw new InvalidOperationException("A worker start/resume is already running.");
  int epoch=_ffControlEpoch.GetValueOrDefault(profile);
  try{
   FFActivity(profile,action=="ffResume"?"Checking this claim before resuming…":"Preparing this account's loss worker…");
   await PrepareFFExecution(profile,id);
   if(epoch!=_ffControlEpoch.GetValueOrDefault(profile)){s.Note="Paused; the queued resume/start was cancelled.";return;}
   if(action=="ffResume"){
    var w=FFWorker(profile,id)??throw new InvalidOperationException("No paused worker remains. Use Start for a stopped worker.");
    var order=_lossOrders.Single(o=>o.SourceService=="FFScouter"&&o.RemoteProfileId==profile&&o.RemoteClaimId==id);
    if(!FFExecutable(order,profile))throw new InvalidOperationException("Claim is no longer executable. No worker resumed.");
    if(_medicalSessions.TryGetValue(w,out var med)&&med.Busy)throw new InvalidOperationException("Medical verification is still running. Wait for its result before resuming; no item will be repeated.");
    if(!await w.ResumeIfAsync(()=>epoch==_ffControlEpoch.GetValueOrDefault(profile)&&ReferenceEquals(FFWorker(profile,id),w)&&FFExecutable(order,profile)))throw new InvalidOperationException("Resume cancelled because this worker or claim changed.");
    s.Note="Resume requested for the existing worker/session; pending medical use is rechecked, never repeated blindly.";
   }else await StartFFOrder(profile,s.Claims[id]);
  }catch(Exception ex){s.Note=ex is InvalidOperationException or InvalidDataException or FFSellerException?ex.Message:"Worker control failed. No new attack or item was authorized.";}
  finally{_ffControlBusy.Remove(profile);_ffActivity.Remove(profile);PushFF();UpdateLossDashboardStatus();}
 }
 private async Task<int> FFEnergy(string profile){
  var key=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this account's Torn API key first.");
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(35));
  using var doc=JsonDocument.Parse(await _api.FetchEndpointAsync("user?selections=profile,bars",key,deadline.Token,fresh:true));
  FFSellerData.VerifyOwner(FFFor(profile).Player,doc.RootElement.GetProperty("profile").GetProperty("id").GetInt32());
  return doc.RootElement.GetProperty("bars").GetProperty("energy").GetProperty("current").GetInt32();
 }
 private async Task HandleFFAction(string action,JsonElement root){
  if(action=="ffState"){PushFF();return;}
  string profile=ReadWorkerString(root,"profileId");if(profile!=CurrentProfileId)throw new InvalidOperationException("Selected profile changed.");
  if(action=="ffClaimBoth"){await ReserveFFBoth(root);return;}
  if(action is "ffPause" or "ffResume" or "ffStart"){await HandleFFControl(action,profile,root.GetProperty("claimId").GetInt64());return;}
  if(action=="ffTarget"){
   var targetClaim=FFFor(profile).Claims.GetValueOrDefault(root.GetProperty("claimId").GetInt64())??throw new InvalidDataException("Unknown claim.");
   if(targetClaim.Target>0){await AddBrowserTabAsync(TornRoutes.Attack(targetClaim.Target));return;}
  }
  if(_ffControlBusy.Contains(profile))throw new InvalidOperationException("A worker start/resume is already running.");
  if(!_ffBusy.Add(profile))throw new InvalidOperationException("Seller refresh is running. Pause and Open target remain available; wait briefly before another seller change.");
  var s=FFFor(profile);bool keySaved=false;
  try{
   if(action=="ffConnect"){
    if(!root.TryGetProperty("consent",out var consent)||!consent.GetBoolean())throw new InvalidOperationException("Review the seller data policy and confirm this account's key.");
    if(s.Pending!="")throw new InvalidOperationException("Resolve the pending request before replacing its credential.");
    string key=ReadWorkerString(root,"key").Trim();
    using var check=JsonDocument.Parse(await _ffSeller.Send(key,"check-key"));
    if(!check.RootElement.GetProperty("is_registered").GetBoolean()||check.RootElement.GetProperty("policy_update_required").GetBoolean())throw new InvalidOperationException("Register/review this key on FFScouter's website first.");
    using var owner=JsonDocument.Parse(await _ffSeller.Send(key,"account/balance"));int player=owner.RootElement.GetProperty("player_id").GetInt32();
    var tornKey=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this profile's separate Torn API key first.");
    using var torn=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",tornKey,fresh:true));
    FFSellerData.VerifyOwner(player,torn.RootElement.GetProperty("profile").GetProperty("id").GetInt32());
    if(s.Player>0&&s.Player!=player||_settings.Profiles.Any(p=>p.Id!=profile&&FFFor(p.Id).Player==player))throw new InvalidOperationException("This seller account is already bound to another profile/history.");
    CredentialManager.SaveSellerKey(profile,key);s.Player=player;s.KeyHash=FFSellerData.KeyHash(key);s.OwnerChecked=DateTime.UtcNow;s.Enabled=true;SaveFF(profile);keySaved=true;await SyncFFCore(profile);
   }else if(action=="ffSettings"){
    bool arm=root.GetProperty("autoRun").GetBoolean();
    if(arm&&(s.Player<=0||string.IsNullOrEmpty(CredentialManager.LoadSellerKey(profile))))throw new InvalidOperationException("Connect and verify the seller account before arming.");
    int limit=root.GetProperty("limit").GetInt32();if(limit is <0 or >1000000)throw new InvalidDataException("Invalid session claim limit.");
    if(arm&&!s.AutoRun){s.ArmedAt=DateTime.UtcNow;foreach(var id in s.Claims.Keys)s.AutoHandled.Add(id);} // Arm only subsequently discovered claims.
    s.AutoRun=arm;s.AutoFinish=root.GetProperty("autoFinish").GetBoolean();s.Enabled=root.GetProperty("enabled").GetBoolean()||arm;s.SessionLimit=limit;s.LimitCustomized=limit>0;s.Note="Settings saved. Home execution applies to NEW claims after arming; existing claims require Start.";SaveFF(profile);
   }else if(action=="ffRefresh"){await SyncFFCore(profile);
   }else if(action=="ffResolve"){
    if(DateTime.UtcNow-s.LastSync>TimeSpan.FromSeconds(45))throw new InvalidOperationException("Refresh claims first.");
    s.Pending="";SaveFF(profile);s.Note="Pending request cleared by you after review. No mutation was retried.";
   }else if(action=="ffForget"){
    foreach(var w in new[]{_lossWorkerA,_lossWorkerB})if(w?.ProfileId==profile&&w.IsArmed)throw new InvalidOperationException("Stop this account's worker before disconnecting.");
    if(s.Pending!="")throw new InvalidOperationException("Reconcile the pending operation before disconnecting.");
    CredentialManager.ForgetSellerKey(profile);s.Enabled=s.AutoRun=s.AutoFinish=false;s.Note="Disconnected locally. History retained; no remote key revoked.";SaveFF(profile);
   }else if(action is "ffClaim" or "ffClaimEnergy"){
    await SyncFFCore(profile);string order=ReadWorkerString(root,"order");int count;string energyNote="";
    if(action=="ffClaimEnergy"){
     FFActivity(profile,"Reading this account's current energy…");
     int energy=await FFEnergy(profile);
     count=FFSellerData.EnergyClaimCount(s,order,energy,DateTime.UtcNow);
     energyNote="Current energy: "+energy+" / 25 = "+(energy/25)+" possible losses; provider, availability and optional limits reduce this to "+count+".\n\n";
    }else count=FFSellerData.ClaimCount(s,order,root.GetProperty("count").GetInt32(),root.GetProperty("max").GetBoolean(),DateTime.UtcNow);
    if(profile!=CurrentProfileId)throw new InvalidOperationException("The selected account changed. No contract reserved; select the account and try again.");
    if(MessageBox.Show(this,energyNote+"Reserve "+count+" losses on "+order+" for this account? The five-minute/15-minute timers start immediately.","FFScouter reservation",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
    await FFMutation(profile,"claim",order,count);await SyncFFCore(profile);
   }else{
    long id=root.GetProperty("claimId").GetInt64();var c=s.Claims.GetValueOrDefault(id)??throw new InvalidDataException("Claim not in this account's saved history.");
    if(action=="ffTarget"){await FFDetail(profile,c);c=s.Claims[id];if(c.Target<=0)throw new InvalidDataException("No Torn target ID available. Claim ID/order number are not attack targets.");await AddBrowserTabAsync(TornRoutes.Attack(c.Target));}
    else if(action=="ffComplete"){
     if(MessageBox.Show(this,"Mark claim "+id+" complete and release unused slots? This does not confirm payment.","FFScouter completion",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
     foreach(var a in new[]{true,false}){var w=a?_lossWorkerA:_lossWorkerB;if(w!=null&&_medicalSessions.TryGetValue(w,out var med)&&_lossOrders.Any(o=>o.Id==med.Order&&o.RemoteProfileId==profile&&o.RemoteClaimId==id))await StopLossWorkerAsync(a);}
     await FFMutation(profile,"complete",c.OrderNumber,0,id);await SyncFFCore(profile);
    }
   }
  }catch(Exception ex){s.Note=ex is InvalidOperationException or InvalidDataException or FFSellerException?ex.Message:"Seller operation failed. Saved orders retained; refresh to reconcile before retrying.";SaveFF(profile);}
  finally{_ffBusy.Remove(profile);_ffActivity.Remove(profile);PushFF(keySaved);RefreshLossLedger();UpdateLossDashboardStatus();}
 }
 private async void TickFFSeller(){
  foreach(var p in _settings.Profiles.ToList()){
   var s=FFFor(p.Id);if(!s.Enabled||_ffControlBusy.Contains(p.Id)||_ffBusy.Contains(p.Id)||_ffPollAt.GetValueOrDefault(p.Id)>DateTime.UtcNow)continue;
   _ffPollAt[p.Id]=DateTime.UtcNow.AddSeconds(30);_ffBusy.Add(p.Id);
   try{
    await SyncFFCore(p.Id);
    if(s.Pending==""&&!_ffControlBusy.Contains(p.Id)){
     var finish=s.Claims.Values.FirstOrDefault(c=>c.State=="active"&&c.Claimed>0&&c.Delivered>=c.Claimed);
     if(s.AutoFinish&&finish!=null){
      await FFDetail(p.Id,finish);var o=_lossOrders.Single(x=>x.RemoteOwner==s.Player&&x.RemoteClaimId==finish.Id&&x.SourceService=="FFScouter");
      if(o.CommitReceipts.Where(x=>x.AttackId>0).All(x=>s.Claims[finish.Id].AttackIds.Contains(x.AttackId)))await FFMutation(p.Id,"complete",finish.OrderNumber,0,finish.Id);
     }
     var ready=FFSellerData.AutoCandidate(s,DateTime.UtcNow);
     if(s.AutoRun&&ready!=null&&!new[]{_lossWorkerA,_lossWorkerB}.Any(w=>w?.IsArmed==true&&w.ProfileId==p.Id)&&!(_lossWorkerA?.IsArmed==true&&_lossWorkerB?.IsArmed==true)){
      s.AutoHandled.Add(ready.Id);SaveFF(p.Id); // Durable single automatic start attempt.
      await StartFFOrder(p.Id,ready,true);
     }
    }
   }catch(Exception ex){s.Note=ex is InvalidOperationException or InvalidDataException or FFSellerException?ex.Message:"Seller sync failed. No new automatic attack authorized.";try{SaveFF(p.Id);}catch{}}
   finally{_ffBusy.Remove(p.Id);_ffActivity.Remove(p.Id);PushFF();RefreshLossLedger();}
   return; // One profile per timer turn; requests remain globally paced.
  }
 }
}
