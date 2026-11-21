using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private bool _saleBusy;private string _saleProfile="";private CancellationTokenSource? _saleCancel;
 private readonly Dictionary<string,(DateTime At,string Html)> _saleCache=new();
 private SaleJournal Sales(string profile)=>IntelStore.Load(profile,"inventory-sales",new SaleJournal());
 private void SaveSales(string profile,SaleJournal j){IntelStore.Save(profile,"inventory-sales",j);UpdateModernDashboard();}
 private object SalesDashboard(){
  if(_saleBusy&&_saleProfile!=CurrentProfileId)_saleCancel?.Cancel();
  var j=Sales(CurrentProfileId);var result=InventorySales.Compare(j,DateTime.UtcNow);
  return new{profileId=CurrentProfileId,busy=_saleBusy,note=j.Note,j.InventoryUtc,j.ExcludedRows,j.PartialInventory,j.MinimumRating,j.MaxTraders,j.OnlineOnly,
   rows=j.Inventory.Select(x=>new{x.Id,x.Name,x.Amount,selected=j.Selected.GetValueOrDefault(x.Id)}),single=result.Single,split=result.Split,candidates=result.Candidates,
   extra=result.Single!=null&&result.Split?.Missing.Length==0?result.Split.Total-result.Single.Total:(decimal?)null};
 }
 private async Task HandleInventorySales(string action,JsonElement request){
  string profile=ReadWorkerString(request,"profileId");if(profile!=CurrentProfileId)return;
  try{
   if(action=="saleStop"){if(_saleProfile==profile)_saleCancel?.Cancel();return;}
   if(action=="saleCopy"){
    var saved=Sales(profile);var comparison=InventorySales.Compare(saved,DateTime.UtcNow);
    bool one=ReadWorkerString(request,"plan")=="single";var plan=one?comparison.Single:comparison.Split;
    if(plan==null)throw new InvalidDataException("Scan a selected item pool before copying its sale plan.");
    var lines=new List<string>{"Torn Exchange quoted sale plan — "+DateTime.Now.ToString("g"),"Advertised offers only. Confirm current price/quantity with each buyer."};
    foreach(var buyer in plan.Lines.GroupBy(x=>x.Trader)){
     lines.Add(buyer.First().TraderName+" ["+buyer.Key+"] — $"+buyer.Sum(x=>x.Total).ToString("N0"));
     foreach(var item in buyer)lines.Add("  "+item.Quantity.ToString("N0")+" x "+item.ItemName+" @ $"+item.UnitPrice.ToString("N0")+" = $"+item.Total.ToString("N0"));
     lines.Add(buyer.First().Url);
    }
    lines.Add("Quoted total: $"+plan.Total.ToString("N0")+(plan.Missing.Length>0?" — PARTIAL: "+plan.Missing.Length+" unquoted item types":""));
    Clipboard.SetText(string.Join(Environment.NewLine,lines));FlashStatus("Trader-by-trader sale plan copied.");return;
   }
   if(action=="saleSource"){
    int trader=request.GetProperty("trader").GetInt32();var quote=Sales(profile).Quotes.Where(x=>x.Trader==trader&&DateTime.UtcNow-x.Retrieved<TimeSpan.FromMinutes(10)).OrderByDescending(x=>x.Retrieved).FirstOrDefault();
    if(quote==null||!Uri.TryCreate(quote.Url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="www.tornexchange.com"||!uri.AbsolutePath.StartsWith("/prices/"))throw new InvalidDataException("Scan this trader's public price list first.");
    await AddBrowserTabAsync(uri.AbsoluteUri);return;
   }
   if(action=="saleOpen"){
    int trader=request.GetProperty("trader").GetInt32();var j=Sales(profile);
    if(!j.Quotes.Any(x=>x.Trader==trader&&DateTime.UtcNow-x.Retrieved<TimeSpan.FromMinutes(10)))throw new InvalidDataException("Re-scan this trader's quote first.");
    await AddBrowserTabAsync("https://www.torn.com/trade.php#step=start&userID="+trader);return;
   }
   if(_saleBusy)throw new InvalidDataException("An inventory/buyer scan is already running.");
   _saleBusy=true;_saleProfile=profile;using var cancel=new CancellationTokenSource();_saleCancel=cancel;var ct=cancel.Token;
   try{
    var j=Sales(profile);
    if(action=="saleInventory"){
     var key=CredentialManager.LoadApiKey(profile)??throw new InvalidDataException("Save this profile's Torn API key first.");
     using var owner=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,ct,fresh:true));int player=owner.RootElement.GetProperty("profile").GetProperty("id").GetInt32();
     if(j.Owner!=0&&j.Owner!=player)j=new();j.Owner=player;
     var rows=new List<SaleInventory>();int excluded=0,count=0;
     for(int offset=0;offset<5000;offset+=250){
      ct.ThrowIfCancellationRequested();if(profile!=CurrentProfileId)throw new OperationCanceledException();
      var page=InventorySales.Inventory(await _api.FetchEndpointAsync("user/inventory?limit=250&offset="+offset,key,ct,fresh:true));
      rows.AddRange(page.Rows);excluded+=page.Excluded;count=page.Count;if(count<250)break;
     }
     j.Inventory=rows.GroupBy(x=>x.Id).Select(g=>new SaleInventory(g.Key,g.First().Name,g.Sum(x=>x.Amount))).OrderBy(x=>x.Name).ToList();
     j.ExcludedRows=excluded;j.PartialInventory=count==250;j.InventoryUtc=DateTime.UtcNow;
     j.Selected=j.Selected.Where(x=>j.Inventory.Any(y=>y.Id==x.Key)).ToDictionary(x=>x.Key,x=>Math.Min(x.Value,j.Inventory.Single(y=>y.Id==x.Key).Amount));
     j.Note="Inventory loaded. Equipped, unique-stat and faction-owned rows are excluded from generic item quotes. Select up to 20 item types and quantities."+(j.PartialInventory?" Inventory scan hit its 5,000-row cap.":"");SaveSales(profile,j);return;
    }
    if(action!="saleCompare")throw new InvalidDataException("Unknown inventory sale action.");
    if(j.InventoryUtc==null||DateTime.UtcNow-j.InventoryUtc>TimeSpan.FromMinutes(15))throw new InvalidDataException("Scan inventory first; the snapshot must be less than 15 minutes old.");
    var selected=request.GetProperty("pool").EnumerateArray().Select(x=>(Id:x.GetProperty("id").GetInt32(),Quantity:x.GetProperty("quantity").GetInt64())).Where(x=>x.Quantity>0).ToList();
    if(selected.Count is <1 or >20||selected.Select(x=>x.Id).Distinct().Count()!=selected.Count||selected.Any(x=>!j.Inventory.Any(y=>y.Id==x.Id&&x.Quantity<=y.Amount)))throw new InvalidDataException("Choose 1–20 distinct inventory item types, within owned quantities.");
    j.MinimumRating=request.GetProperty("rating").GetInt32();j.MaxTraders=request.GetProperty("maxTraders").GetInt32();j.OnlineOnly=request.GetProperty("online").GetBoolean();
    if(j.MinimumRating is <-100000 or >1000000||j.MaxTraders is <0 or >5)throw new InvalidDataException("Use a valid minimum rating and either unlimited or 1–5 traders.");
    j.Selected=selected.ToDictionary(x=>x.Id,x=>x.Quantity);j.Note="Reading public Torn Exchange listings; no API key, inventory quantities or trade submissions are sent.";SaveSales(profile,j);
    using var handler=new HttpClientHandler{UseCookies=false,AllowAutoRedirect=false};using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(25)};http.DefaultRequestHeaders.UserAgent.ParseAdd("TornCommander/0.6.116 (public price comparison)");
    async Task<(string Html,DateTime At)> Public(string url){
     if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="www.tornexchange.com"||!(uri.AbsolutePath=="/listings"||uri.AbsolutePath.StartsWith("/prices/")))throw new InvalidDataException("Unsupported public quote URL.");
     if(_saleCache.TryGetValue(url,out var cached)&&DateTime.UtcNow-cached.At<TimeSpan.FromMinutes(5))return(cached.Html,cached.At);
     ct.ThrowIfCancellationRequested();if(profile!=CurrentProfileId)throw new OperationCanceledException();
     await Task.Delay(2200,ct);var response=await http.GetAsync(uri,ct);if(!response.IsSuccessStatusCode)throw new InvalidDataException("Torn Exchange unavailable ("+(int)response.StatusCode+"). Saved quotes were kept; no bypass attempted.");
     string html=await response.Content.ReadAsStringAsync(ct);if(html.Length>4000000)throw new InvalidDataException("Public quote page is too large.");
     var at=DateTime.UtcNow;_saleCache[url]=(at,html);return(html,at);
    }
    foreach(var item in j.Inventory.Where(x=>j.Selected.ContainsKey(x.Id))){
     for(int page=1;page<=2;page++){
      var html=await Public("https://www.tornexchange.com/listings?model_name_contains="+Uri.EscapeDataString(item.Name)+"&order_by=-effective_price&page="+page);
      var quotes=InventorySales.Listings(html.Html,item.Id,html.At);if(page==1)j.Quotes.RemoveAll(x=>x.Item==item.Id);j.Quotes.AddRange(quotes);if(quotes.Count<20)break;
     }
     j.Note="Read quotes for "+item.Name+". Checking candidate buyers' complete public lists next.";SaveSales(profile,j);
    }
    var candidates=j.Quotes.Where(x=>j.Selected.ContainsKey(x.Item)&&x.Trader!=j.Owner&&x.Rating>=j.MinimumRating&&(!j.OnlineOnly||x.Status=="Online"))
     .GroupBy(x=>x.Trader).OrderByDescending(g=>g.Select(x=>x.Item).Distinct().Count()).ThenByDescending(g=>g.Sum(x=>(decimal)x.Price*j.Selected[x.Item])).Take(24).Select(g=>g.First()).ToList();
    foreach(var buyer in candidates){
     var html=await Public(buyer.Url);var filled=InventorySales.PriceList(html.Html,buyer,j.Selected.Keys.ToHashSet(),html.At);
     foreach(var quote in filled){if(j.Quotes.Any(x=>x.Item==quote.Item&&x.Trader==quote.Trader&&x.Retrieved>quote.Retrieved))continue;j.Quotes.RemoveAll(x=>x.Item==quote.Item&&x.Trader==quote.Trader);j.Quotes.Add(quote);}
     SaveSales(profile,j);
    }
    j.Note="Comparison complete. Best found among up to 40 public listings per item and 24 candidate buyer price lists. Quotes are advertised, not guaranteed purchases; confirm quantity, final price and buyer before trading. Highest return mode uses the best eligible quote for every item; capped splits search a shortlist and may miss alternatives. No items or money transferred.";SaveSales(profile,j);
   }finally{_saleBusy=false;_saleCancel=null;UpdateModernDashboard();}
  }catch(Exception ex){var j=Sales(profile);j.Note=ex is OperationCanceledException?"Scan stopped. Partial quotes retained with their timestamps.":ex is InvalidDataException?ex.Message:"Inventory/buyer scan failed. Check API permissions or the public site; saved data was kept.";SaveSales(profile,j);}
 }
}
