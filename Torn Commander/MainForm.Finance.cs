using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly HashSet<string> _financeBusy=new();
 private readonly Dictionary<string,CancellationTokenSource> _financeCancel=new();
 private readonly Dictionary<string,DateTime> _financeNext=new();
 private FinanceJournal FinanceFor(string profile)=>IntelStore.Load(profile,"finance",new FinanceJournal());
 private object FinanceDashboard(){
  var j=FinanceFor(CurrentProfileId);var now=DateTime.UtcNow;var s=FinanceData.Summary(j,now);
  var owned=_lossOrders.Where(o=>o.SourceService=="FFScouter"?o.RemoteProfileId==CurrentProfileId:o.ProfileAId==CurrentProfileId&&string.IsNullOrEmpty(o.ProfileBId)).ToList();
  var payments=LossSettlement.Totals(owned);
  return new{profileId=CurrentProfileId,j.Goal,j.Reserve,j.DailyIncome,j.DailySpending,j.Monitor,j.Note,j.LastSync,j.LastLogSync,j.BalanceStatus,j.LogStatus,j.LogRowsScanned,j.LogPages,j.LogCategories,j.PartialCategories,busy=_financeBusy.Contains(CurrentProfileId),
   summary=s,balance=j.Balances.MaxBy(b=>b.At),history=j.Balances.OrderBy(b=>b.At).Select(b=>new{at=b.At,cash=b.CashSavings,networth=b.Networth}).ToList(),
   entries=j.Entries.OrderByDescending(e=>e.At).Take(500).ToList(),advice=FinanceData.Advice(j,s,now),
   losses=new{payments.Cash,payments.Xanax,payments.Delivered,sharedExcluded=_lossOrders.Count(o=>(o.ProfileAId==CurrentProfileId||o.ProfileBId==CurrentProfileId)&&!string.IsNullOrEmpty(o.ProfileBId))},
   trades=_trades.Where(t=>t.ProfileId==CurrentProfileId&&t.ProfitAfterFees.HasValue&&t.Date.ToUniversalTime()>=now.AddDays(-30)).Sum(t=>t.ProfitAfterFees??0)};
 }
 private void TickFinance(){
  var p=CurrentProfileId;if(_financeBusy.Contains(p)||_financeNext.GetValueOrDefault(p)>DateTime.UtcNow)return;
  _financeNext[p]=DateTime.UtcNow.AddMinutes(15);
  try{if(FinanceFor(p).Monitor)_=SyncFinance(p);}catch{}
 }
 private async Task SyncFinance(string profile){
  if(!_financeBusy.Add(profile))return;
  using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(180));_financeCancel[profile]=deadline;
  UpdateModernDashboard();
  int player=0;string stage="Account";
  void Save(Action<FinanceJournal> update){
   var j=FinanceFor(profile);if(j.Player!=0&&j.Player!=player)throw new InvalidDataException("Finance owner changed. Previous history preserved.");
   j.Player=player;update(j);IntelStore.Save(profile,"finance",j);UpdateModernDashboard();
  }
  try{
   var key=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this profile's Torn API key in Settings first.");
   using var owner=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,deadline.Token,fresh:true));
   player=owner.RootElement.GetProperty("profile").GetProperty("id").GetInt32();
   var previous=FinanceFor(profile);if(previous.Player!=0&&previous.Player!=player)throw new InvalidDataException("The Torn key belongs to another account. Finance history was not changed.");
   stage="Balances";
   try{
    var b=FinanceData.Balance(await _api.FetchEndpointAsync("user/money",key,deadline.Token,fresh:true),DateTime.UtcNow);
    Save(j=>{j.Balances.RemoveAll(x=>x.At.Date==b.At.Date);j.Balances.Add(b);j.Balances=j.Balances.OrderByDescending(x=>x.At).Take(730).ToList();j.LastSync=b.At;j.BalanceStatus="Cash balances received from Torn";j.Note="Cash balances saved. Scanning financial categories next; current cash is already available below.";});
   }catch(Exception ex) when(ex is not OperationCanceledException){
    Save(j=>{j.BalanceStatus="Cash balance unavailable: "+FinanceFailure(ex);j.Note=j.BalanceStatus;});
   }
   try{
    using var worth=JsonDocument.Parse(await _api.FetchEndpointAsync("user/networth",key,deadline.Token));
    long total=worth.RootElement.GetProperty("networth").GetProperty("total").GetInt64();
    if(total>=0)Save(j=>{var balance=j.Balances.MaxBy(x=>x.At);if(balance!=null)balance.Networth=total;});
   }catch(Exception ex) when(ex is not OperationCanceledException){/* Optional assets never block verified cash. */}
   stage="History";
   Save(j=>{j.LogRowsScanned=0;j.LogPages=0;j.LogCategories=0;j.PartialCategories=0;j.LogStatus="Finding official financial log categories…";});
   var categories=FinanceData.FinancialCategories(await _api.FetchEndpointAsync("torn/logcategories",key,deadline.Token));
   if(categories.Count==0)throw new InvalidDataException("Official financial log categories could not be identified.");
   long from=new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-29)).ToUnixTimeSeconds();
   int scanned=0,pages=0,finished=0,partial=0,added=0;
   foreach(var category in categories){
    string? path="user/log?cat="+category.Id+"&limit=100&from="+from;var visited=new HashSet<string>();
    for(int page=0;page<2&&path!=null;page++){
     deadline.Token.ThrowIfCancellationRequested();
     if(!visited.Add(path))throw new InvalidDataException("Financial pagination repeated; scan stopped without repeating a page.");
     string json=await _api.FetchEndpointAsync(path,key,deadline.Token,fresh:true);
     using var d=JsonDocument.Parse(json);scanned+=d.RootElement.GetProperty("log").GetArrayLength();pages++;
     var imported=FinanceData.Logs(json,DateTime.UtcNow);string? next=d.RootElement.GetProperty("log").GetArrayLength()==0?null:FinanceData.PreviousLogPage(json);
     Save(j=>{int before=j.Entries.Count;FinanceData.Merge(j,imported);added+=j.Entries.Count-before;j.LogRowsScanned=scanned;j.LogPages=pages;j.LogStatus="Scanning "+category.Name+" · "+scanned+" source rows checked · "+added+" new cash entries. Nonfinancial fights do not crowd out this scan.";});
     path=next;
    }
    if(path!=null)partial++;finished++;
    Save(j=>{j.LogCategories=finished;j.PartialCategories=partial;});
   }
   Save(j=>{j.LastLogSync=DateTime.UtcNow;j.LogStatus="Scanned "+finished+" financial categories, "+pages+" pages and "+scanned+" rows for the last 30 days; "+added+" new cash entries. "+(partial>0?partial+" busy categories exceeded the 200-row/category limit; totals are partial.":"Returned category pages reached the requested date boundary.")+" Unsupported fields and other cash sources may still be missing. Review unclassified receipts; totals are not audited profit.";j.Note=j.BalanceStatus+". "+j.LogStatus;});
  }catch(Exception ex){
   var j=FinanceFor(profile);
   string detail=ex is OperationCanceledException?"Scan stopped or timed out; balances and imported pages already saved.":FinanceFailure(ex);
   if(stage=="History")j.LogStatus="Financial history unavailable/partial: "+detail+" Full access is required for user logs.";
   else if(stage=="Balances")j.BalanceStatus=detail;
   j.Note=stage+": "+detail+" Saved balances and history were preserved.";IntelStore.Save(profile,"finance",j);
  }finally{_financeCancel.Remove(profile);_financeBusy.Remove(profile);_financeNext[profile]=DateTime.UtcNow.AddMinutes(15);UpdateModernDashboard();}
 }
 private static string FinanceFailure(Exception ex){
  if(ex is InvalidDataException or InvalidOperationException)return ex.Message;
  var match=System.Text.RegularExpressions.Regex.Match(ex.Message,"[\"']?code[\"']?\\s*:\\s*(\\d+)");
  return match.Success?"Torn API error "+match.Groups[1].Value+". Check this profile's key permissions and API cooldown.":"API request or response failed. Check the connection and key permissions; try again after the API queue clears.";
 }
 private async Task HandleFinance(string action,JsonElement r){
  string profile=ReadWorkerString(r,"profileId");if(profile!=CurrentProfileId)throw new InvalidOperationException("Profile changed; review this account first.");
  if(action=="financeStop"){if(_financeCancel.TryGetValue(profile,out var cancel))cancel.Cancel();return;}
  if(action=="financeSync"){await SyncFinance(profile);return;}
  var j=FinanceFor(profile);
  if(action=="financeSettings"){
   j.Goal=GoalNumber(r,"goal",1,1000000000000L);j.Reserve=GoalNumber(r,"reserve",0,1000000000000L);
   j.DailyIncome=GoalNumber(r,"income",0,1000000000000L);j.DailySpending=GoalNumber(r,"spending",0,1000000000000L);
   j.Monitor=r.GetProperty("monitor").GetBoolean();_financeNext[profile]=DateTime.UtcNow.AddMinutes(15);
  }else if(action=="financeEntry"){
   var e=new FinanceEntry{Id=ReadWorkerString(r,"id"),At=DateTime.Parse(ReadWorkerString(r,"at"),null,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal),
    Category=ReadWorkerString(r,"category"),Kind=ReadWorkerString(r,"kind"),Currency=ReadWorkerString(r,"currency"),Amount=r.GetProperty("amount").GetDecimal(),Note=ReadWorkerString(r,"note")};
   if(e.Id.Length is <1 or >220)throw new InvalidDataException("Missing entry identity.");
   FinanceData.Validate(e,DateTime.UtcNow);
   var existing=j.Entries.FirstOrDefault(x=>x.Id==e.Id);
   if(existing!=null){e.Source=existing.Source;e.At=existing.At;e.Amount=existing.Amount;e.Currency=existing.Currency;}
   else if(e.Id.StartsWith("torn:",StringComparison.Ordinal))throw new InvalidDataException("Unknown imported log.");
   j.Entries.RemoveAll(x=>x.Id==e.Id);j.Entries.Add(e);
  }else if(action=="financeDelete"){
   var id=ReadWorkerString(r,"id");if(j.Entries.Any(e=>e.Id==id&&e.Source!="manual"))throw new InvalidOperationException("Imported logs remain as an audit trail. Mark transfers instead of deleting.");
   j.Entries.RemoveAll(e=>e.Id==id);
  }else if(action=="financeExport"){
   using var dialog=new SaveFileDialog{Filter="JSON finance export|*.json",FileName="Commander-finance.json"};
   if(dialog.ShowDialog(this)==DialogResult.OK)File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(j,new JsonSerializerOptions{WriteIndented=true}));
   return;
  }
  IntelStore.Save(profile,"finance",j);UpdateModernDashboard();
 }
}
