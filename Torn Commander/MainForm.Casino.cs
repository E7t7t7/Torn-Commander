using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly Dictionary<string,string> _casinoNotes=new();
 private string _casinoNote{get=>_casinoNotes.GetValueOrDefault(CurrentProfileId,"Start a session with the bankroll at your table. Update it after hands settle; unrelated wallet changes are not gambling results.");set=>_casinoNotes[CurrentProfileId]=value;}
 private object CasinoDashboard(){
  var rows=_casino.Where(s=>s.ProfileId==CurrentProfileId).OrderByDescending(s=>s.Date).ToList();
  return new{profileId=CurrentProfileId,note=_casinoNote,sessions=rows.Count(x=>!x.Active),net=rows.Where(x=>!x.Active).Sum(x=>x.ProfitLoss),
   active=rows.Where(x=>x.Active).Select(CasinoTracking.View).ToList(),history=rows.Where(x=>!x.Active).Take(100).Select(CasinoTracking.View).ToList(),
   legacy=_casino.Count(x=>string.IsNullOrWhiteSpace(x.ProfileId))};
 }
 private async Task HandleCasino(string action,JsonElement r){
  string profile=ReadWorkerString(r,"profileId");if(profile!=CurrentProfileId)throw new InvalidOperationException("Selected account changed. Review its Casino tab.");
  try{
   if(action=="casinoOpen"){await AddBrowserTabAsync(ReadWorkerString(r,"game")=="Blackjack"?"https://www.torn.com/page.php?sid=blackjack":TornRoutes.Casino);return;}
   if(action=="casinoRead"){await ReadCasinoHand(profile);return;}
   var next=JsonSerializer.Deserialize<List<CasinoSession>>(JsonSerializer.Serialize(_casino))!;
   string id=ReadWorkerString(r,"id");
   decimal N(string name){if(!r.TryGetProperty(name,out var v)||!v.TryGetDecimal(out var number))throw new InvalidDataException("Enter "+name+".");CasinoTracking.Money(number);return number;}
   if(action=="casinoStart"){
    if(next.Any(x=>x.Id==id&&x.ProfileId==profile))return; // A repeated click/late acknowledgement cannot create another session.
    if(next.Any(x=>x.Id==id)||next.Any(x=>x.ProfileId==profile&&x.Active))throw new InvalidOperationException("Finish the active session before starting another.");
    next.Add(CasinoTracking.Start(profile,id,ReadWorkerString(r,"game"),N("bankroll"),N("stopLoss"),N("target"),DateTime.UtcNow));
    _casinoNote="Session started. Enter settled table bankroll after each hand; limits produce alerts and never place bets.";
   }else if(action=="casinoAdopt"){
    if(MessageBox.Show(this,"Assign ALL unassigned legacy casino records to "+CurrentProfile.Name+"? Use this only if they all belong to this account.","Assign old records",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
    foreach(var old in next.Where(x=>string.IsNullOrWhiteSpace(x.ProfileId)))old.ProfileId=profile;
   }else{
    var s=next.SingleOrDefault(x=>x.Id==id&&x.ProfileId==profile)??throw new InvalidDataException("This session does not belong to the selected profile.");
    if(action=="casinoUpdate"||action=="casinoEnd"){
     bool already=s.LimitReached!="";
     CasinoTracking.Update(s,N("bankroll"),N("deposits"),N("withdrawals"),DateTime.UtcNow);
     s.Notes=ReadWorkerString(r,"note");if(s.Notes.Length>240)throw new InvalidDataException("Keep the note within 240 characters.");
     if(action=="casinoEnd"){s.Active=false;s.EndedUtc=DateTime.UtcNow;}
     _casinoNote=s.LimitReached!=""?s.LimitReached+". End play manually in Torn. Commander cannot prevent bets made outside this panel.":"Settled bankroll saved; session cash additions and cash-outs excluded from profit.";
     if(!already&&s.LimitReached!="")FlashStatus(s.LimitReached+" — stop playing and finish the session.");
    }else if(action=="casinoDelete"){
     if(s.Active)throw new InvalidOperationException("End the active session first.");
     if(MessageBox.Show(this,"Delete this saved casino session?","Delete session",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
     next.Remove(s);
    }else throw new InvalidDataException("Unknown casino action.");
   }
   Storage.SaveAtomic(Storage.CasinoPath,next);_casino=next;RebindCasino();
  }catch(Exception ex){_casinoNote=ex is InvalidOperationException or InvalidDataException?ex.Message:"Casino operation failed. Saved records were preserved.";FlashStatus(_casinoNote);}
  finally{UpdateModernDashboard();}
 }
 private async Task ReadCasinoHand(string profile){
  var tab=Active();var uri=tab?.WebView.Source;
  if(tab?.WebView.CoreWebView2==null||uri==null||uri.Scheme!="https"||!TornRoutes.IsTornHost(uri.Host)||
   !(uri.AbsolutePath=="/page.php"||uri.AbsolutePath=="/pda.php")||!uri.Query.Contains("blackjack",StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException("Open Blackjack in the main game browser, then press Read current Blackjack hand.");
  var code=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"BundledScripts","Torn_Casino_Read.js"));
  var raw=await tab.ExecuteScriptAsync(code);
  if(profile!=CurrentProfileId||tab!=Active())return;
  using var d=JsonDocument.Parse(raw??"null");
  if(d.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Blackjack table was not readable.");
  _modernDashboard?.CoreWebView2?.PostWebMessageAsJson(DashboardJson.Serialize(new{type="casinoHand",profileId=profile,data=d.RootElement.Clone()}));
 }
}
