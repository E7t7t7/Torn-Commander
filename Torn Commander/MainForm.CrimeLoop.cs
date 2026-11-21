using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly Dictionary<string,CrimeLoopJournal> _crimeJournals=new();
 private bool _crimeLoopBusy,_crimeLoopStop;private string _crimeLoopProfile="";
 private readonly Dictionary<string,BrowserTab> _crimeLoopTabs=new();
 private CrimeReading? _crimeLoopReading;
 private CrimeLoopJournal CrimeJournal(string profile){
  if(!_crimeJournals.TryGetValue(profile,out var j)){j=IntelStore.Load(profile,"crime-loop",new CrimeLoopJournal());if(j.State=="running")j.State="paused";_crimeJournals[profile]=j;}return j;
 }
 private void SaveCrime(string profile,CrimeLoopJournal j){IntelStore.Save(profile,"crime-loop",j);UpdateModernDashboard();}
 private object CrimeLoopDashboard(){
  var j=CrimeJournal(CurrentProfileId);var rows=j.History.Where(x=>x.Crime==j.Options.Crime).ToList();
  return new{profileId=CurrentProfileId,busy=_crimeLoopBusy,state=j.State,note=j.Note,options=j.Options,pending=j.Pending.Length>0,
   actions=j.SessionActions,nerveSpent=j.SessionNerve,nerve=j.AvailableNerve,cash=rows.Sum(x=>x.Cash),
   rows=rows.TakeLast(20).Reverse(),
   evidence="Recommendations rank this account's recent observed skill gain or cash rewards per nerve after three samples. With no history: closest ready burglary target, or gather/demo/win at your thresholds. Cash excludes unpriced loot and is not net profit; confidence is not an exact success chance."};
 }
 private async Task<T> CrimeJs<T>(BrowserTab tab,string expression){
  var raw=await tab.ExecuteScriptAsync(expression);
  return JsonSerializer.Deserialize<T>(raw??"null",new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Crime page did not respond.");
 }
 private async Task HandleCrimeLoop(string action,JsonElement request){
  string profile=ReadWorkerString(request,"profileId");if(profile!=CurrentProfileId)return;var j=CrimeJournal(profile);
  try{
   if(action=="crimePause"||action=="crimeStop"){_crimeLoopStop=true;j.State=action=="crimeStop"?"stopped":"paused";j.Note="Stopping after the pending result, if any. No next action will start.";SaveCrime(profile,j);return;}
   if(action=="crimeClear"){
    if(_crimeLoopBusy)throw new InvalidDataException("Pause and wait for the current action first.");
    if(_crimeLoopTabs.TryGetValue(profile,out var recoveryTab)&&_browserTabObjects.Contains(recoveryTab))await recoveryTab.ExecuteScriptAsync("window.__tcCrimeLoop?.acknowledge("+JsonSerializer.Serialize(j.Pending)+")");
    j.Pending="";j.PendingChoice=null;j.State="paused";j.Note="Interrupted action cleared after your review. A fresh session is required.";SaveCrime(profile,j);return;
   }
   if(_crimeLoopBusy)throw new InvalidDataException("A crime session is already checking an action.");
   var options=request.TryGetProperty("options",out var o)?JsonSerializer.Deserialize<CrimeLoopOptions>(o.GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??j.Options:j.Options;options.Validate();
   if(j.Pending.Length>0&&action!="crimeResume")throw new InvalidDataException("A previous action is unverified. Resume to check its receipt or review Torn before clearing it.");
   if(action=="crimeResume")options=j.Options;
   if(j.Options.Crime!=options.Crime)j.LastSkill=null;j.Options=options;
   if(action=="crimeOpen"){NavigateActive(TornRoutes.Crimes+"#/"+options.Crime);j.Note="Open a property or the selected game, then Scan controls.";SaveCrime(profile,j);return;}
   var tab=Active()??throw new InvalidDataException("Open this crime in the game browser.");
   if(action=="crimeResume"&&j.Pending.Length>0&&(!_crimeLoopTabs.TryGetValue(profile,out var pendingTab)||pendingTab!=tab))throw new InvalidDataException("Open the same crime tab to check its pending receipt, or review and clear the interrupted action.");
   _crimeLoopTabs[profile]=tab;_crimeLoopBusy=true;_crimeLoopProfile=profile;_crimeLoopStop=false;
   try{
    var key=CredentialManager.LoadApiKey(profile)??throw new InvalidDataException("Save this account's Torn API key first.");
    using var owner=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,fresh:true));j.Player=owner.RootElement.GetProperty("profile").GetProperty("id").GetInt32();
    string code=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"BundledScripts","Torn_Crime_Loop.js"));
    await tab.ExecuteScriptAsync(code);
    if(j.Pending.Length>0)await VerifyCrimeResult(profile,tab,j);
    using var bars=JsonDocument.Parse(await _api.FetchEndpointAsync("user/bars",key,fresh:true));j.AvailableNerve=bars.RootElement.GetProperty("bars").GetProperty("nerve").GetProperty("current").GetInt32();
    _crimeLoopReading=await CrimeJs<CrimeReading>(tab,"window.__tcCrimeLoop.read("+JsonSerializer.Serialize(options.Crime)+","+j.Player+")");
    if(!_crimeLoopReading.Ok)throw new InvalidDataException(_crimeLoopReading.Note);
    if(action=="crimeScan"){j.Note=_crimeLoopReading.Note+" "+_crimeLoopReading.Choices.Count+" candidate action(s).";SaveCrime(profile,j);return;}
    if(action=="crimeStart"){j.SessionActions=0;j.SessionNerve=0;j.LastSkill=null;}
    j.State="running";
    while(!_crimeLoopStop&&profile==CurrentProfileId&&Active()==tab){
     _crimeLoopReading=await CrimeJs<CrimeReading>(tab,"window.__tcCrimeLoop.read("+JsonSerializer.Serialize(options.Crime)+","+j.Player+")");
     if(!_crimeLoopReading.Ok)throw new InvalidDataException(_crimeLoopReading.Note);
     var next=CrimeLoopPlanner.Next(_crimeLoopReading,options,j.History,j.SessionNerve,j.SessionActions,j.AvailableNerve);
     if(next==null){j.State="complete";j.Note="Stopped at your nerve reserve, budget/action limit, or no eligible visible target. Open another target/game and scan if needed.";break;}
     j.Pending=Guid.NewGuid().ToString("N");j.PendingChoice=next;j.Note="Next: "+next.Action+" — "+next.Target+" ("+next.Cost+" nerve). Waiting for one result.";SaveCrime(profile,j);
     var ack=await CrimeJs<CrimeReceipt>(tab,"window.__tcCrimeLoop.start("+JsonSerializer.Serialize(j.Pending)+","+JsonSerializer.Serialize(options.Crime)+","+j.Player+","+JsonSerializer.Serialize(next.Key)+")");
     if(!ack.Ok)throw new InvalidDataException(ack.Note);
     await VerifyCrimeResult(profile,tab,j);
     if(j.State=="paused")break;
     await Task.Delay(650);
    }
    if(j.State=="running"){j.State="paused";j.Note="Paused. The next action has not started.";}
   }finally{_crimeLoopBusy=false;SaveCrime(profile,j);}
  }catch(Exception ex){j.State="paused";j.Note=ex is InvalidDataException?ex.Message:"Crime check failed. Check API access or page loading. No pending action was retried.";SaveCrime(profile,j);}
 }
 private async Task VerifyCrimeResult(string profile,BrowserTab tab,CrimeLoopJournal j){
  var until=DateTime.UtcNow.AddSeconds(25);
  while(DateTime.UtcNow<until){
   if(profile!=CurrentProfileId||Active()!=tab)throw new InvalidDataException("Account or game tab changed. Pending crime was not repeated.");
   var receipt=await CrimeJs<CrimeReceipt>(tab,"window.__tcCrimeLoop?.get("+JsonSerializer.Serialize(j.Pending)+")");
   if(receipt.Done){
    CrimeLoopPlanner.Record(j,receipt);j.Note=receipt.Note;
    if(receipt.Critical){j.State="paused";j.Note="Torn reports a critical/blocked result. Review the game before resuming.";}
    SaveCrime(profile,j);return;
   }
   await Task.Delay(200);
  }
  throw new InvalidDataException("Crime result was not verified within 25 seconds. No repeat click. Resume checks the same pending receipt; review Torn before clearing it.");
 }
}
