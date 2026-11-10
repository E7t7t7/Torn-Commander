using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly HashSet<string> _financeBusy=new();
 private readonly Dictionary<string,DateTime> _financeNext=new();
 private FinanceJournal FinanceFor(string profile)=>IntelStore.Load(profile,"finance",new FinanceJournal());
 private object FinanceDashboard(){
  var j=FinanceFor(CurrentProfileId);var now=DateTime.UtcNow;var s=FinanceData.Summary(j,now);
  var owned=_lossOrders.Where(o=>o.SourceService=="FFScouter"?o.RemoteProfileId==CurrentProfileId:o.ProfileAId==CurrentProfileId&&string.IsNullOrEmpty(o.ProfileBId)).ToList();
  var payments=LossSettlement.Totals(owned);
  return new{profileId=CurrentProfileId,j.Goal,j.Reserve,j.DailyIncome,j.DailySpending,j.Monitor,j.Note,j.LastSync,busy=_financeBusy.Contains(CurrentProfileId),
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
  UpdateModernDashboard();
  try {
   var key=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this profile's Torn API key in Settings first.");
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(65));
   using var owner=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,deadline.Token,fresh:true));
   int player=owner.RootElement.GetProperty("profile").GetProperty("id").GetInt32();
   var j=FinanceFor(profile);if(j.Player!=0&&j.Player!=player)throw new InvalidDataException("The Torn key belongs to another account. Finance history was not changed.");
   j.Player=player;
   var json=await _api.FetchEndpointAsync("user/money",key,deadline.Token,fresh:true);
   var b=FinanceData.Balance(json,DateTime.UtcNow);
   string note="Balances saved. ";var imported=new List<FinanceEntry>();
   try{using var nw=JsonDocument.Parse(await _api.FetchEndpointAsync("user/networth",key,deadline.Token));b.Networth=nw.RootElement.GetProperty("networth").GetProperty("total").GetInt64();}catch{note+="Net worth unavailable. ";}
   j.Balances.RemoveAll(x=>x.At.Date==b.At.Date);j.Balances.Add(b);
   j.Balances=j.Balances.OrderByDescending(x=>x.At).Take(730).ToList();
   try{
    var logs=await _api.FetchEndpointAsync("user/log?limit=100",key,deadline.Token);
    imported=FinanceData.Logs(logs,DateTime.UtcNow);
    note+="Latest 100 logs inspected. Only explicit money fields imported; ambiguous receipts need review. Older or unsupported logs may be missing.";
   }catch{note+="Logs unavailable (full Torn key required), timed out, or unsupported. Use manual entries for missing income/spending. No transaction history was invented.";}
   var current=FinanceFor(profile);
   if(current.Player!=0&&current.Player!=player)throw new InvalidDataException("Finance owner changed while syncing.");
   current.Player=player;current.Balances=j.Balances;FinanceData.Merge(current,imported);
   current.LastSync=DateTime.UtcNow;current.Note=note;IntelStore.Save(profile,"finance",current);
  }catch(Exception ex){var j=FinanceFor(profile);j.Note=ex is InvalidOperationException or InvalidDataException?ex.Message:"Finance sync failed or API permission/rate limit blocked it. Saved history preserved; try later.";IntelStore.Save(profile,"finance",j);}
  finally{_financeBusy.Remove(profile);_financeNext[profile]=DateTime.UtcNow.AddMinutes(15);UpdateModernDashboard();}
 }
 private async Task HandleFinance(string action,JsonElement r){
  string profile=ReadWorkerString(r,"profileId");if(profile!=CurrentProfileId)throw new InvalidOperationException("Profile changed; review this account first.");
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
