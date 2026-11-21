using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly Dictionary<string,CargoPreferences> _cargoPrefs=new();private bool _cargoBusy;private string _cargoProfile="";private CancellationTokenSource? _cargoCancel;
 private CargoPreferences CargoPrefs(string? profile=null){profile??=CurrentProfileId;if(!_cargoPrefs.TryGetValue(profile,out var c))_cargoPrefs[profile]=c=IntelStore.Load(profile,"travel-cargo",new CargoPreferences());return c;}
 private List<CargoShelf> CargoShelves(){
  var types=CargoPrefs().Types;
  return _travelForecast.Items.Where(x=>x.ItemId>0&&x.BuyPrice>0&&x.StockUpdatedAtUtc.HasValue&&!CargoPrefs().Excluded.ContainsKey(x.ItemId))
   .GroupBy(x=>(x.Country,x.ItemId)).Select(g=>g.OrderByDescending(x=>x.StockUpdatedAtUtc).First())
   .Select(x=>new CargoShelf(x.ItemId,x.ItemName,x.Country,x.BuyPrice,types.GetValueOrDefault(x.ItemId)??"",x.StockUpdatedAtUtc!.Value,x.Source)).ToList();
 }
 private List<OutboundCargo> OutboundQuotes(){
  var p=CargoPrefs();if(!p.Enabled)return new();
  return CargoShelves().Select(s=>{
   if(!p.Books.TryGetValue(s.Item,out var book))return null;
   long fare=_settings.TravelFlightType=="Standard"?CountryRoute(s.Country).Item2:0;
   return TravelCargoMath.Quote(s,book,_settings.TravelCapacity,_settings.TravelCashBudget,fare,DateTime.UtcNow);
  }).Where(x=>x!=null).Cast<OutboundCargo>().OrderByDescending(x=>x.Margin).ToList();
 }
 private object CargoDashboard(){
  if(_cargoBusy&&_cargoProfile!=CurrentProfileId)_cargoCancel?.Cancel();
  var p=CargoPrefs();var items=_travelForecast.Items.Where(x=>x.ItemId>0).GroupBy(x=>x.ItemId).Select(g=>new{id=g.Key,name=g.First().ItemName}).OrderBy(x=>x.name);
  return new{profileId=CurrentProfileId,busy=_cargoBusy,p.Enabled,p.Note,excluded=p.Excluded.Select(x=>new{id=x.Key,name=x.Value}),items,quotes=OutboundQuotes().Take(100),priced=p.Books.Count,
   total=items.Count(),noteRule="Only goods listed in a country's observed shop inventory are treated as accepted there. Black Market buys at the observed shop price; other shops at 75%. Quotes can change before arrival. Per-item outbound quantity is conservatively capped at 100."};
 }
 private async Task HandleTravelCargo(string action,JsonElement request){
  string profile=ReadWorkerString(request,"profileId");if(profile!=CurrentProfileId)return;var prefs=CargoPrefs(profile);
  try{
   if(action=="cargoStop"){if(_cargoProfile==profile)_cargoCancel?.Cancel();return;}
   if(action=="cargoExclude"){
    int id=request.GetProperty("itemId").GetInt32();bool exclude=request.GetProperty("exclude").GetBoolean();
    if(id<=0)throw new InvalidDataException("Select a valid item.");
    if(exclude){var item=_travelForecast.Items.FirstOrDefault(x=>x.ItemId==id)??throw new InvalidDataException("Refresh travel stocks before excluding this item.");prefs.Excluded[id]=item.ItemName;}else prefs.Excluded.Remove(id);
    IntelStore.Save(profile,"travel-cargo",prefs);_evidenceTravel=null;RebindTravel();UpdateModernDashboard();return;
   }
   if(action=="cargoEnable"){prefs.Enabled=request.GetProperty("enabled").GetBoolean();IntelStore.Save(profile,"travel-cargo",prefs);_evidenceTravel=null;UpdateModernDashboard();return;}
   if(action!="cargoScan"||_cargoBusy)return;
   _cargoBusy=true;_cargoProfile=profile;using var cancel=new CancellationTokenSource();_cargoCancel=cancel;var ct=cancel.Token;
   try{
    var key=CredentialManager.LoadApiKey(profile)??throw new InvalidDataException("Save this account's Torn API key to scan actual purchase listings.");
    var catalog=await _api.FetchItemCatalogAsync(key,ct);prefs.Types=catalog.ToDictionary(x=>x.Id,x=>x.Type);
    var ids=CargoShelves().Where(x=>DateTime.UtcNow-x.ShopAt<TimeSpan.FromMinutes(30)).Select(x=>x.Item).Distinct().Take(150).ToList();
    if(ids.Count==0)throw new InvalidDataException("No fresh accepted-item/shop-price rows. Refresh travel stocks first.");
    int scanned=0;
    foreach(int id in ids){
     ct.ThrowIfCancellationRequested();if(profile!=CurrentProfileId)throw new OperationCanceledException();
     if(!prefs.Books.TryGetValue(id,out var cached)||DateTime.UtcNow-cached.At>TimeSpan.FromMinutes(15)){
      var quote=await _api.FetchItemMarketAsync(id,Math.Min(100,_settings.TravelCapacity),0,key,ct);
      prefs.Books[id]=new CargoBook{At=DateTime.UtcNow,Tiers=quote.Tiers.Where(t=>t.Price>0&&t.Amount>0).Select(t=>new CargoTier(t.Price,t.Amount)).ToList()};
     }
     prefs.Note="Scanned "+(++scanned)+"/"+ids.Count+" distinct outbound item purchase books. Current quantities are not reserved.";
     IntelStore.Save(profile,"travel-cargo",prefs);_evidenceTravel=null;UpdateModernDashboard();
    }
    prefs.Note="Outbound scan complete: "+scanned+" item types, current listing depth versus observed country buyback prices. Combined first-trip profit uses both cargo directions; future departures do not reuse stale outbound quotes.";
    IntelStore.Save(profile,"travel-cargo",prefs);
   }finally{_cargoBusy=false;_cargoCancel=null;_evidenceTravel=null;UpdateModernDashboard();}
  }catch(Exception ex){prefs.Note=ex is OperationCanceledException?"Outbound scan stopped; partial books kept with original timestamps.":ex is InvalidDataException?ex.Message:"Outbound scan failed. Check API permissions/rate limits; partial books kept. No purchases or travel booked.";IntelStore.Save(profile,"travel-cargo",prefs);_evidenceTravel=null;UpdateModernDashboard();}
 }
 private List<TravelForecastItem> ApplyCargo(List<TravelForecastItem> imports){
  var all=imports.Where(x=>!CargoPrefs().Excluded.ContainsKey(x.ItemId)).Select(x=>JsonSerializer.Deserialize<TravelForecastItem>(JsonSerializer.Serialize(x))!).Select(CalculateTravelOpportunity).ToList();
  var quotes=OutboundQuotes();var best=quotes.GroupBy(x=>x.Country,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.Margin).First(),StringComparer.OrdinalIgnoreCase);
  foreach(var item in all){
   if(!best.TryGetValue(item.Country,out var outcargo)||item.BuyPrice<=0||item.MarketValue<=0)continue;
   long fare=_settings.TravelFlightType=="Standard"?CountryRoute(item.Country).Item2:0;
   int stock=item.Stock is >0?item.Stock.Value:item.RestockAtUtc.HasValue?_settings.TravelCapacity:0;
   var pair=TravelCargoMath.Pair(outcargo,item.BuyPrice,item.ProfitPerItem,stock,_settings.TravelCapacity,_settings.TravelCashBudget,fare);
   item.Units=pair.ReturnUnits;item.CashRequired=pair.StartingCash;item.TripProfit=pair.Net;item.RoiPercent=pair.Roi;
   item.ProfitPerHour=pair.Net*60d/Math.Max(1,item.FlightMinutesOneWay*2);
   item.OutboundItem=outcargo.Name;item.OutboundUnits=outcargo.Units;item.OutboundCost=outcargo.Purchase;item.OutboundRevenue=outcargo.Revenue;item.OutboundQuoteUtc=outcargo.MarketAt;
   item.Note+=" | Outbound "+outcargo.Units+"x "+outcargo.Name+": buy $"+outcargo.Purchase.ToString("N0")+", observed overseas proceeds $"+outcargo.Revenue.ToString("N0")+". Combined net includes return cargo and standard fares once. Sell outbound goods before loading return cargo.";
  }
  foreach(var cargo in quotes){
   var row=new TravelForecastItem{Direction="export",ItemId=cargo.Item,ItemName="Outbound: "+cargo.Name,Country=cargo.Country,Category="Outbound cargo",Stock=cargo.Units,
    BuyPrice=(cargo.Purchase+cargo.Units-1)/cargo.Units,MarketValue=cargo.Revenue/cargo.Units,StockUpdatedAtUtc=cargo.MarketAt,PricingUpdatedAtUtc=cargo.MarketAt,Source="Torn market → "+cargo.Source,
    Note=cargo.Rule+"; shop observed "+cargo.ShopAt.ToString("u")+". Outbound-only route; no inbound purchase assumed. Market listing quantities are not reserved.",OutboundQuoteUtc=cargo.MarketAt,OutboundItem=cargo.Name,OutboundUnits=cargo.Units,OutboundCost=cargo.Purchase,OutboundRevenue=cargo.Revenue};
   CalculateTravelOpportunity(row);long fare=_settings.TravelFlightType=="Standard"?2*CountryRoute(cargo.Country).Item2:0;
   row.Units=cargo.Units;row.CashRequired=cargo.Purchase+fare/2+Math.Max(0,fare/2-cargo.Revenue);row.TripProfit=cargo.Margin-fare;row.ProfitPerItem=cargo.Margin/cargo.Units;
   row.ProfitPerHour=row.TripProfit*60d/Math.Max(1,row.FlightMinutesOneWay*2);row.RoiPercent=100d*row.TripProfit/Math.Max(1,cargo.Purchase);
   all.Add(row);
  }
  return all.OrderByDescending(x=>x.ProfitPerHour).ToList();
 }
}
