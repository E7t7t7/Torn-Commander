using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm
{
 private TradingJournal? _tradingJournal;
 private string _tradingProfile = "";
 private bool _tradeScanBusy;
 private CancellationTokenSource? _tradeScanCancellation;
 private string _tradeScanNote = "Choose items and scan live listings.";
 private static string TradingPath(string profile) => Path.Combine(Storage.Root,"TradingAdvisor",string.Concat(profile.Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_'))+".json");
 private TradingJournal GetTradingJournal()
 {
  if(_tradingJournal is null || _tradingProfile!=CurrentProfileId){
   _tradeScanCancellation?.Cancel();
   _tradingProfile=CurrentProfileId;
   RebindTrades();
   _tradingJournal=Storage.Load(TradingPath(CurrentProfileId),new TradingJournal());
   _tradingJournal.Observations??=new();
   _tradingJournal.ActiveItemIds??=new();
   _tradeScanNote="Choose items and scan live listings.";
  }
  return _tradingJournal;
 }
 private object TradingDashboardData()
 {
  var journal=GetTradingJournal();
  var freshWallet=_snapshot.Wallet is not null && _snapshot.FullRetrievedAt!=DateTime.MinValue &&
      DateTime.UtcNow-_snapshot.FullRetrievedAt.ToUniversalTime()<=TimeSpan.FromMinutes(30);
  var budget=TradingAdvisor.AvailableCapital(journal.Budget,journal.Reserve,freshWallet?_snapshot.Wallet:null);
  return new {
   settings=journal.Items+"|"+journal.Budget+"|"+journal.Reserve+"|"+journal.FeePercent+"|"+journal.RoiPercent+"|"+journal.CostBasis+"|"+journal.StressPercent+"|"+journal.MaxExposurePercent+"|"+journal.MinimumProfit,
   items=journal.Items,budget=journal.Budget,reserve=journal.Reserve,fee=journal.FeePercent,roi=journal.RoiPercent,costBasis=journal.CostBasis,stress=journal.StressPercent,exposure=journal.MaxExposurePercent,minimumProfit=journal.MinimumProfit,
   scanning=_tradeScanBusy,note=_tradeScanNote,
   capitalNote=freshWallet?$"Analysis capital {budget:C0}, capped by the last wallet balance minus reserve.":"Wallet is unavailable or stale; capital is your entered total cash minus reserve. Your actual wallet is unverified.",
   rows=TradingAdvisor.Build(journal.Observations.Where(q=>journal.ActiveItemIds.Contains(q.ItemId)),budget,journal.FeePercent,journal.RoiPercent,journal.CostBasis,DateTime.UtcNow,journal.StressPercent,journal.MaxExposurePercent,journal.MinimumProfit)
  };
 }
 private async Task ScanTradingAsync(JsonElement request)
 {
  if(_tradeScanBusy)return;
  var profile=CurrentProfileId;
  if(request.GetProperty("profileId").GetString()!=profile)throw new InvalidDataException("Profile changed. Review this account before scanning.");
  var original=GetTradingJournal();
  var journal=JsonSerializer.Deserialize<TradingJournal>(JsonSerializer.Serialize(original))!;
  var text=request.TryGetProperty("items",out var itemText)?itemText.GetString()??"":"";
  var names=text.Split([',',';','\n'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  if(names.Count is <1 or >8)throw new InvalidDataException("Enter 1-8 item names or numeric IDs, separated by commas.");
  journal.Budget=GoalNumber(request,"budget",0,1000000000000L);
  journal.Reserve=GoalNumber(request,"reserve",0,1000000000000L);
  journal.CostBasis=GoalNumber(request,"costBasis",0,1000000000000L);
  decimal number(string name,decimal max){
   if(!request.TryGetProperty(name,out var value)||!value.TryGetDecimal(out var n)||n<0||n>max)throw new InvalidDataException("Invalid "+name+".");
   return n;
  }
  journal.StressPercent=number("stress",50);journal.MaxExposurePercent=number("exposure",100);
  if(journal.MaxExposurePercent<=0)throw new InvalidDataException("Per-item capital limit must be above zero.");
  journal.MinimumProfit=GoalNumber(request,"minimumProfit",0,1000000000000L);
  journal.FeePercent=number("fee",25);journal.RoiPercent=number("roi",1000);
  if(names.Count>1&&journal.CostBasis>0)throw new InvalidDataException("Use one item when entering a cost basis for sell advice.");
  var key=CredentialManager.LoadApiKey(profile);
  if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("Save a Torn API key for this profile in Settings first.");
  journal.Items=string.Join(", ",names);
  _tradeScanBusy=true;_tradeScanNote="Scanning through the shared API queue...";
  using var cancellation=new CancellationTokenSource();_tradeScanCancellation=cancellation;
  UpdateModernDashboard();
  try{
   var catalog=await _api.FetchItemCatalogAsync(key,cancellation.Token);
   var resolved=new List<MarketCatalogItem>();
   foreach(var name in names){
    var hits=catalog.Where(x=>x.Id.ToString()==name||x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToList();
    if(hits.Count==0)hits=catalog.Where(x=>x.Name.Contains(name,StringComparison.OrdinalIgnoreCase)).ToList();
    if(hits.Count!=1)throw new InvalidDataException("Item '"+name+"' is missing or ambiguous. Use its exact name or ID.");
    if(resolved.All(x=>x.Id!=hits[0].Id))resolved.Add(hits[0]);
   }
   var observations=new List<TradeQuote>();
   foreach(var item in resolved){
    cancellation.Token.ThrowIfCancellationRequested();
    var scan=await _api.FetchItemMarketAsync(item.Id,1,0,key,cancellation.Token);
    observations.Add(new TradeQuote{ItemId=item.Id,Name=item.Name,Reference=item.MarketValue,ObservedUtc=DateTime.UtcNow,
      Tiers=scan.Tiers.Select(t=>new TradeTier{Price=t.Price,Quantity=t.Amount}).ToList()});
   }
   cancellation.Token.ThrowIfCancellationRequested();
   if(profile!=CurrentProfileId)return;
   journal.ActiveItemIds=resolved.Select(x=>x.Id).ToList();
   journal.Observations.AddRange(observations);
   journal.Observations=journal.Observations.Where(x=>x.ObservedUtc>=DateTime.UtcNow.AddDays(-7))
     .GroupBy(x=>x.ItemId).SelectMany(g=>g.OrderByDescending(x=>x.ObservedUtc).Take(100)).ToList();
   Storage.SaveAtomic(TradingPath(profile),journal);
   _tradingJournal=journal;
   MirrorTradingQuotes(profile,observations);
   _tradeScanNote="Scanned "+observations.Count+" items. Quotes are asking prices, not executed sales. Recheck before committing cash.";
  }catch(OperationCanceledException){if(profile==CurrentProfileId)_tradeScanNote="Scan cancelled. Previous observations retained.";}
  catch(Exception ex){if(profile==CurrentProfileId)_tradeScanNote="Scan failed; previous observations retained. "+ex.Message;}
  finally{
   if(ReferenceEquals(_tradeScanCancellation,cancellation))_tradeScanCancellation=null;
   _tradeScanBusy=false;UpdateModernDashboard();
  }
 }
}
