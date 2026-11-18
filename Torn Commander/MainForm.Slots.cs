using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private bool _slotsBusy;
 private SlotsBatch? _slotsRunningBatch;
 private static bool IsSlotsPage(BrowserTab? tab){
  var u=tab?.WebView.Source;return u!=null&&u.Scheme=="https"&&TornRoutes.IsTornHost(u.Host)&&
   (u.AbsolutePath=="/loader.php"||u.AbsolutePath=="/page.php")&&u.Query.Split('&','?').Contains("sid=slots");
 }
 private async Task HandleSlots(string action,JsonElement request){
  string profile=ReadWorkerString(request,"profileId");
  if(profile!=CurrentProfileId)return;
  try{
   if(action=="slotsOpen"){if(!IsSlotsPage(Active()))await AddBrowserTabAsync(TornRoutes.Slots);return;}
   var active=_casino.SingleOrDefault(x=>x.ProfileId==profile&&x.Active);
   if(action=="slotsReset"){
    if(active?.Slots is not {} reset||reset.Profile!=profile)throw new InvalidDataException("No active Slots batch belongs to this account.");
    reset.ResetRequested=true;reset.StopRequested=true;reset.PauseRequested=true;
    if(ReferenceEquals(reset,_slotsRunningBatch)){reset.Status="Resetting";reset.Note="Stopping this batch. Waiting only for its current operation to return; no more receipt polling or bets.";}
    else SlotsRunner.Reset(reset,DateTime.UtcNow);
    SaveSlots(active);return;
   }
   if(action=="slotsPause"){
    if(active?.Slots is not {} paused)throw new InvalidDataException("No active Slots batch.");
    paused.PauseRequested=true;paused.Status=_slotsBusy?"Pausing":"Paused";paused.Note="Pause requested. Any pending spin will be checked first.";
    SaveSlots(active);return;
   }
   if(action=="slotsStop"&&active?.Slots is {} stopped){
    stopped.StopRequested=true;stopped.Note="Stop requested. Any pending spin will be checked first.";
    SaveSlots(active);if(_slotsBusy)return;
   }
   if(_slotsBusy)throw new InvalidDataException("A batch is starting or settling. Wait for its current result.");
   _slotsBusy=true;
   try{
    var key=CredentialManager.LoadApiKey(profile);
    if(string.IsNullOrWhiteSpace(key))throw new InvalidDataException("Save this account's Torn API key in Settings. Slots batches need access to personal logs (Full access).");
    if(action=="slotsStart"){
     if(active!=null)throw new InvalidDataException("Finish or stop the existing casino session first.");
     var bet=SlotsData.MoneyField(request,"bet");var loss=SlotsData.MoneyField(request,"stopLoss");var target=SlotsData.MoneyField(request,"target");
     int count=SlotsData.CountField(request);
     bool all=false;if(request.TryGetProperty("all",out var a)){
      if(a.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Choose whether to use all starting tokens. No bet placed.");
      all=a.GetBoolean();
     }
     SlotsData.Limit(bet,count,0,all); // Validate the command before making API requests.
     var account=SlotsData.Account(await _api.FetchEndpointAsync("user?selections=profile,casino,money",key,fresh:true));
     // Permission/schema preflight must succeed before creating a runnable batch or clicking a bet.
     var baseline=SlotsData.Baseline(await _api.FetchEndpointAsync("user/log?log=8300,8301&limit=100",key,fresh:true));
     if(CurrentProfileId!=profile)throw new InvalidDataException("Account changed during preflight. No bet placed.");
     int limit=SlotsData.Limit(bet,count,account.Tokens,all);
     if(limit==0||account.Wallet<bet)throw new InvalidDataException("Not enough tokens or cash on hand. No refill or withdrawal will be made.");
     active=new CasinoSession{ProfileId=profile,Game="Slots",Date=DateTime.UtcNow,StartBankroll=account.Wallet,EndBankroll=account.Wallet,Active=true,
      Slots=new(){Profile=profile,Owner=account.Owner,Bet=bet,Requested=count,Limit=limit,AllTokens=all,StartTokens=account.Tokens,LastTokens=account.Tokens,
       StopLoss=loss,ProfitTarget=target,KnownIds=baseline,StartedUtc=DateTime.UtcNow,Status="Starting"}};
     _casino.Add(active);SaveSlots(active);
    }else if(action!="slotsResume"&&action!="slotsStop")throw new InvalidDataException("Unknown Slots action.");
    if(active?.Slots is not {} batch||batch.Profile!=profile)throw new InvalidDataException("No active Slots batch belongs to this account.");
    if(action=="slotsResume"){batch.PauseRequested=false;batch.Note="Resuming: any unfinished result is checked before another bet.";}
    // Reconcile pending receipts even if the game tab is gone. Navigation is only needed for a new click.
    if(batch.Pending==null&&!batch.StopRequested&&(action=="slotsStart"||!IsSlotsPage(Active())))await AddBrowserTabAsync(TornRoutes.Slots);
    var tab=Active();string name=CurrentProfile.Name;
    bool Current()=>!IsDisposed&&profile==CurrentProfileId&&tab!=null&&tab==Active()&&_browserTabObjects.Contains(tab)&&_casino.Contains(active)&&active.Active;
    string code=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"BundledScripts","Torn_Slots_Batch.js"));
    async Task<T> Browser<T>(string operation,string token=""){
     if(tab==null)throw new InvalidDataException("Open Slots in the main game browser first.");
     var payload=JsonSerializer.Serialize(new{action=operation,owner=batch.Owner,name,bet=batch.Bet,token});
     var raw=await tab.ExecuteScriptAsync(code.Replace("__SLOTS_REQUEST__",payload));
     return JsonSerializer.Deserialize<T>(raw??"null",new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Slots page did not respond. No repeated click was attempted.");
    }
    _slotsRunningBatch=batch;
    await SlotsRunner.Run(batch,new(){
     Current=Current,
     Account=async()=>SlotsData.Account(await _api.FetchEndpointAsync("user?selections=profile,casino,money",key,fresh:true)),
     Board=async()=>{
      // Re-acknowledge the durable receipt after a transient page/identity failure.
      if(batch.ConfirmedToken.Length>0)await Browser<SlotsBoard>("ack",batch.ConfirmedToken);
      return await Browser<SlotsBoard>("inspect");
     },
     Dispatch=async pending=>{
      if(!Current())return new(){Dispatched=false,Note="Selected browser or account changed. No click."};
      return await Browser<SlotsDispatch>("spin",pending.Token);
     },
     Progress=pending=>Browser<SlotsProgress>("progress",pending.Token),
     Receipts=async()=>{
      var since=batch.Pending?.StartedUtc??batch.StartedUtc;
      var json=await _api.FetchEndpointAsync("user/log?log=8300,8301&limit=100&from="+new DateTimeOffset(since).ToUnixTimeSeconds(),key,fresh:true);
      return SlotsData.ReadReceipts(json,since);
     },
     Acknowledge=async token=>{await Browser<SlotsBoard>("ack",token);},
     Save=()=>SaveSlots(active)
    });
   }finally{_slotsRunningBatch=null;_slotsBusy=false;RebindCasino();UpdateModernDashboard();}
  }catch(Exception ex){
   _casinoNotes[profile]=ex is InvalidDataException or InvalidOperationException?ex.Message:"Slots could not start or verify its result. Check this profile's Full API access and connection. No bet was retried.";
   FlashStatus(_casinoNotes[profile]);
  }finally{UpdateModernDashboard();}
 }
 private void SaveSlots(CasinoSession session){
  var b=session.Slots??throw new InvalidDataException("Missing slots batch.");
  session.Active=b.EndedUtc==null;session.EndedUtc=b.EndedUtc;session.Notes=b.Note;
  Storage.SaveAtomic(Storage.CasinoPath,_casino);
  _casinoNotes[session.ProfileId]=b.Status+": "+b.Note;UpdateModernDashboard();
 }
}
