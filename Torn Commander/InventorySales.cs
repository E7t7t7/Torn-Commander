using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TornCommander;
public sealed record SaleInventory(int Id,string Name,long Amount);
public sealed record BuyerQuote(int Item,int Trader,string Name,long Price,int? Rating,string Status,string Url,DateTime Retrieved);
public sealed record SaleLine(int Item,string ItemName,long Quantity,int Trader,string TraderName,long UnitPrice,string Url){public decimal Total=>(decimal)Quantity*UnitPrice;}
public sealed class SalePlan {public List<SaleLine> Lines{get;set;}=new();public int[] Missing{get;set;}=[];public decimal Total=>Lines.Sum(x=>x.Total);public int Traders=>Lines.Select(x=>x.Trader).Distinct().Count();}
public sealed class SaleJournal {
 public int Owner{get;set;}public DateTime? InventoryUtc{get;set;}public List<SaleInventory> Inventory{get;set;}=new();
 public Dictionary<int,long> Selected{get;set;}=new();public List<BuyerQuote> Quotes{get;set;}=new();
 public int MinimumRating{get;set;}=0;public int MaxTraders{get;set;}=3;public bool OnlineOnly{get;set;}
 public string Note{get;set;}="Scan inventory, choose a pool, then compare buyers.";public int ExcludedRows{get;set;}public bool PartialInventory{get;set;}
}
public static class InventorySales {
 private static readonly TimeSpan RegexTimeout=TimeSpan.FromSeconds(2);
 static Match M(string text,string pattern)=>Regex.Match(text,pattern,RegexOptions.IgnoreCase|RegexOptions.Singleline,RegexTimeout);
 static string Clean(string text)=>Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]+>"," ",RegexOptions.Singleline,RegexTimeout)),@"\s+"," ",RegexOptions.None,RegexTimeout).Trim();
 static long? Money(string text){var m=M(text,@"\$([0-9][0-9,]*)");return m.Success&&long.TryParse(m.Groups[1].Value.Replace(",",""),out var v)&&v is >0 and <=1000000000000?v:null;}
 public static (List<SaleInventory> Rows,int Excluded,int Count) Inventory(string json){
  using var d=JsonDocument.Parse(json);var rows=d.RootElement.GetProperty("inventory").GetProperty("items");var found=new List<SaleInventory>();int excluded=0;
  foreach(var x in rows.EnumerateArray()){
   int id=x.GetProperty("id").GetInt32();long amount=x.GetProperty("amount").GetInt64();string name=x.GetProperty("name").GetString()??"";
   if(x.GetProperty("equipped").GetBoolean()||x.GetProperty("faction_owned").GetBoolean()||x.GetProperty("uid").ValueKind!=JsonValueKind.Null){excluded++;continue;}
   if(id<=0||amount<0||amount>1000000000000L||name.Length is <1 or >200)throw new InvalidDataException("Invalid inventory row.");
   if(amount>0)found.Add(new(id,name,amount));
  }
  return(found.GroupBy(x=>x.Id).Select(g=>new SaleInventory(g.Key,g.First().Name,checked(g.Sum(x=>x.Amount)))).ToList(),excluded,rows.GetArrayLength());
 }
 public static List<BuyerQuote> Listings(string html,int item,DateTime now){
  if(html.Length>4000000||M(html,@"<title>\s*(?:just a moment|access denied|security check)").Success)throw new InvalidDataException("Torn Exchange needs manual browser attention; no bypass attempted.");
  if(!html.Contains("listing-container",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The public listings layout is unavailable. Open Torn Exchange to check; no quote inferred.");
  var cards=Regex.Split(html,@"<div\b[^>]*class\s*=\s*[""'][^""']*\blisting-card\b[^""']*[""'][^>]*>",RegexOptions.IgnoreCase,RegexTimeout).Skip(1);
  var quotes=new List<BuyerQuote>();
  foreach(var c in cards){
   var im=M(c,@"images/items/(\d+)/");var owner=M(c,@"href=[""']https://www\.torn\.com/profiles\.php\?XID=(\d+)[""'][^>]*>(.*?)</a>");
   var price=M(c,@"listing-price[^>]*>\s*<strong>\s*\$([0-9,]+)\s*</strong>");var url=M(c,@"href=[""'](/prices/[^""']+)[""']");
   if(!im.Success||!int.TryParse(im.Groups[1].Value,out int id)||id!=item||!owner.Success||!int.TryParse(owner.Groups[1].Value,out int trader)||trader<=0||!url.Success||!price.Success)continue;
   long? value=Money("$"+price.Groups[1].Value);if(value==null)continue;
   var rating=M(c,@"text-end[^>]*>\s*(-?[0-9,]+)\s*<i");int? votes=rating.Success&&int.TryParse(rating.Groups[1].Value.Replace(",",""),out int v)?v:null;
   var status=M(c,@"fa-circle\s+text-(success|warning|secondary)");string state=status.Groups[1].Value.ToLowerInvariant() switch{"success"=>"Online","warning"=>"Idle","secondary"=>"Offline",_=>"Unknown"};
   string name=Regex.Replace(Clean(owner.Groups[2].Value),@"\s*\[\d+\]\s*$","").Trim();
   var uri=new Uri(new Uri("https://www.tornexchange.com"),WebUtility.HtmlDecode(url.Groups[1].Value));if(uri.Host!="www.tornexchange.com"||!uri.AbsolutePath.StartsWith("/prices/"))continue;
   quotes.Add(new(item,trader,name,value.Value,votes,state,uri.AbsoluteUri,now));
  }
  return quotes.DistinctBy(x=>(x.Item,x.Trader)).ToList();
 }
 public static List<BuyerQuote> PriceList(string html,BuyerQuote owner,ISet<int> pool,DateTime now){
  var trade=M(html,@"https://www\.torn\.com/trade\.php#step=start(?:&amp;|&)userID=(\d+)");
  if(!trade.Success||!int.TryParse(trade.Groups[1].Value,out int trader)||trader!=owner.Trader)return new();
  var result=new List<BuyerQuote>();
  foreach(Match row in Regex.Matches(html,@"<tr\b.*?</tr>",RegexOptions.Singleline|RegexOptions.IgnoreCase,RegexTimeout)){
   var image=M(row.Value,@"images/items/(\d+)/");if(!image.Success||!int.TryParse(image.Groups[1].Value,out int id)||!pool.Contains(id))continue;
   var values=Regex.Matches(row.Value,@"\$[0-9][0-9,]*",RegexOptions.None,RegexTimeout).Select(x=>Money(x.Value)).Where(x=>x!=null).Distinct().ToList();
   if(values.Count==1)result.Add(owner with{Item=id,Price=values[0]!.Value,Retrieved=now});
  }
  return result.DistinctBy(x=>x.Item).ToList();
 }
 public static (SalePlan? Single,SalePlan? Split,int Candidates) Compare(SaleJournal j,DateTime now){
  if(j.Selected.Count==0)return(null,null,0);
  var quotes=j.Quotes.Where(q=>q.Trader!=j.Owner&&q.Price>0&&q.Retrieved<=now.AddMinutes(1)&&now-q.Retrieved<=TimeSpan.FromMinutes(10)&&q.Rating.HasValue&&q.Rating>=j.MinimumRating&&(!j.OnlineOnly||q.Status=="Online")&&j.Selected.ContainsKey(q.Item)).GroupBy(q=>(q.Item,q.Trader)).Select(g=>g.OrderByDescending(q=>q.Retrieved).First()).ToList();
  var traders=quotes.GroupBy(x=>x.Trader).OrderByDescending(g=>g.Select(x=>x.Item).Distinct().Count()).ThenByDescending(g=>g.Sum(x=>(decimal)x.Price*j.Selected[x.Item])).Take(12).Select(g=>g.Key).ToArray();
  var bits=traders.Select((id,index)=>(id,index)).ToDictionary(x=>x.id,x=>1<<x.index);
  var byItem=quotes.Where(x=>bits.ContainsKey(x.Trader)).GroupBy(x=>x.Item).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.Price).ThenByDescending(x=>x.Rating).ToList());
  SalePlan Build(int mask){
   var plan=new SalePlan();
   foreach(var p in j.Selected){
    var q=byItem.GetValueOrDefault(p.Key)?.FirstOrDefault(x=>(bits[x.Trader]&mask)!=0);if(q==null)continue;
    var stock=j.Inventory.SingleOrDefault(x=>x.Id==p.Key);if(stock==null||p.Value<=0||p.Value>stock.Amount)throw new InvalidDataException("Selected inventory changed. Re-scan before comparing.");
    plan.Lines.Add(new(p.Key,stock.Name,p.Value,q.Trader,q.Name,q.Price,q.Url));
   }
   plan.Missing=j.Selected.Keys.Except(plan.Lines.Select(x=>x.Item)).ToArray();return plan;
  }
  SalePlan? single=null,split=null;
  for(int mask=1;mask<(1<<traders.Length);mask++){
   int n=System.Numerics.BitOperations.PopCount((uint)mask);if(n>Math.Clamp(j.MaxTraders,1,5))continue;
   var plan=Build(mask);
   if(n==1&&plan.Missing.Length==0&&(single==null||plan.Total>single.Total))single=plan;
   if(split==null||plan.Missing.Length<split.Missing.Length||plan.Missing.Length==split.Missing.Length&&(plan.Total>split.Total||plan.Total==split.Total&&plan.Traders<split.Traders))split=plan;
  }
  return(single,split,traders.Length);
 }
}
