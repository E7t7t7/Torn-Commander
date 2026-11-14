using System.Text.Json;
namespace TornCommander;
internal sealed class GearItem {
 public int Id{get;set;}public string Name{get;set;}="";public string Slot{get;set;}="";public string Category{get;set;}="";
 public long Reference{get;set;}public decimal BaseDamage{get;set;}public decimal BaseAccuracy{get;set;}public decimal BaseArmor{get;set;}
 public Dictionary<string,decimal> Coverage{get;set;}=new();public string Requirement{get;set;}="";
}
internal sealed class GearOffer {
 public string Key{get;set;}="";public int ItemId{get;set;}public string Name{get;set;}="";public string Slot{get;set;}="";public string Category{get;set;}="";
 public long Uid{get;set;}public long Price{get;set;}public int Seller{get;set;}public string Source{get;set;}="Item Market";
 public DateTime ObservedUtc{get;set;}public DateTime CollectedUtc{get;set;}public bool TimestampKnown{get;set;}
 public decimal? Damage{get;set;}public decimal? Accuracy{get;set;}public decimal? Armor{get;set;}public decimal? Quality{get;set;}
 public Dictionary<string,decimal> Coverage{get;set;}=new();public List<string> Bonuses{get;set;}=new();
 public bool BonusKnown{get;set;}
 public string Requirement{get;set;}="";public string Url=>Seller>0?$"https://www.torn.com/bazaar.php?userId={Seller}":$"https://www.torn.com/page.php?sid=ItemMarket#/market/view=search&itemID={ItemId}";
 public string Identifier=>Uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
 public bool StatsKnown=>Uid>0&&(Slot=="weapon"?Damage is >0&&Accuracy is >0 and <=100:Armor is >0 and <=100&&Coverage.Count>0);
}
internal sealed class GearSettings {
 public long Budget{get;set;}=10000000;public long Reserve{get;set;}=0;
 public string Scope{get;set;}="balanced";public string Bias{get;set;}="balanced";
 public int Depth{get;set;}=18;public bool DiscoverBazaars{get;set;}=true;public string Sellers{get;set;}="";
 public bool AllowBonus{get;set;}public string Items{get;set;}="";
}
internal sealed class GearJournal {
 public GearSettings Settings{get;set;}=new();public List<GearOffer> Offers{get;set;}=new();
 public DateTime? ScannedUtc{get;set;}public string Note{get;set;}="Set a budget and scan to compare real weapon and armour listings.";
 public List<GearBuild> Builds{get;set;}=new();
 public int TypesScanned{get;set;}public int BazaarsScanned{get;set;}public List<string> Warnings{get;set;}=new();
}
internal sealed record GearBuild(string Name,long Cost,decimal Score,List<GearOffer> Items,List<string> Missing);
internal static class GearOptimizer {
 public static readonly string[] Parts=["Head","Throat","Heart","Chest","Stomach","Arm","Hand","Groin","Leg","Foot"];
 private static JsonElement Field(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(name,out var v)?v:default;
 private static string Text(JsonElement e,string name)=>Field(e,name).ValueKind==JsonValueKind.String?Field(e,name).GetString()??"":"";
 private static long Int(JsonElement e,string name){var v=Field(e,name);return v.ValueKind==JsonValueKind.Number&&v.TryGetInt64(out var n)?n:0;}
 private static decimal? Num(JsonElement e,string name){var v=Field(e,name);return v.ValueKind==JsonValueKind.Number&&v.TryGetDecimal(out var n)&&n>=0&&n<=1000?n:null;}
 public static List<GearItem> Catalog(string json){
  using var d=JsonDocument.Parse(json);var rows=new List<GearItem>();
  foreach(var e in d.RootElement.GetProperty("items").EnumerateArray()){
   var detail=Field(e,"details");if(detail.ValueKind!=JsonValueKind.Object||Field(e,"is_tradable").ValueKind==JsonValueKind.False)continue;
   var item=new GearItem{Id=(int)Int(e,"id"),Name=Text(e,"name"),Requirement=Text(e,"requirement"),Reference=Int(Field(e,"value"),"market_price")};
   var stats=Field(detail,"base_stats");item.BaseDamage=Num(stats,"damage")??0;item.BaseAccuracy=Num(stats,"accuracy")??0;item.BaseArmor=Num(stats,"armor")??0;
   if(Text(e,"type")=="Weapon"){item.Category=Text(detail,"category");if(item.Category=="Temporary")continue;item.Slot="weapon";}
   else if(Text(e,"type")=="Armor"){
    var coverage=Field(detail,"coverage");if(coverage.ValueKind==JsonValueKind.Array)foreach(var c in coverage.EnumerateArray()){var value=Num(c,"value");if(value is >=0 and <=100)item.Coverage[Text(c,"name")]=value.Value;}
    var name=item.Name.ToLowerInvariant();
    item.Slot=name.Contains("glove")?"hands":name.Contains("boot")?"feet":name.Contains("pant")||name.Contains("trouser")?"legs":
     name.Contains("helmet")||name.Contains("mask")||name.Contains("respirator")||name.Contains("visage")?"head":
     item.Coverage.GetValueOrDefault("Chest")+item.Coverage.GetValueOrDefault("Stomach")>0?"body":
     item.Coverage.GetValueOrDefault("Head")>0?"head":item.Coverage.GetValueOrDefault("Foot")>0?"feet":item.Coverage.GetValueOrDefault("Hand")>0?"hands":item.Coverage.GetValueOrDefault("Leg")>0?"legs":"";
   }
   if(item.Id>0&&item.Slot!="")rows.Add(item);
  }return rows;
 }
 public static void Validate(GearSettings s){
  if(s.Budget<1||s.Budget>1000000000000L||s.Reserve<0||s.Reserve>=s.Budget||s.Depth is not (18 or 42)||s.Scope is not ("balanced" or "melee" or "primary" or "weapon" or "armor")||s.Bias is not ("balanced" or "accuracy" or "damage"))throw new InvalidDataException("Use a positive budget larger than your reserve and a supported scan/build mode.");
 }
 public static string[] Slots(GearSettings s)=>s.Scope=="weapon"?["weapon"]:s.Scope=="armor"?["head","body","hands","legs","feet"]:["weapon","head","body","hands","legs","feet"];
 public static bool Allowed(GearItem item,GearSettings s)=>Slots(s).Contains(item.Slot)&&(item.Slot!="weapon"||s.Scope!="melee"&&s.Scope!="primary"||item.Category.Equals(s.Scope,StringComparison.OrdinalIgnoreCase));
 public static List<GearItem> Shortlist(IEnumerable<GearItem> catalog,GearSettings s){
  Validate(s);var all=catalog.Where(x=>Allowed(x,s)).ToList();
  if(!string.IsNullOrWhiteSpace(s.Items)){
   var terms=s.Items.Split([',',';','\n'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
   if(terms.Length>42)throw new InvalidDataException("Use at most 42 extra item names or IDs.");
   foreach(var term in terms)if(!all.Any(x=>x.Id.ToString()==term||x.Name.Equals(term,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Unknown or incompatible gear item: "+term+". Use an exact name or item ID.");
  }
  var groups=new List<List<GearItem>>();
  foreach(var slot in Slots(s)){
   var pool=all.Where(x=>x.Slot==slot).ToList();
   decimal Power(GearItem x)=>slot=="weapon"?x.BaseDamage*x.BaseAccuracy/100m:x.BaseArmor*x.Coverage.GetValueOrDefault("Full Body")/100m;
   var selected=new List<GearItem>();var extra=s.Items.Split([',',';','\n'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
   selected.AddRange(pool.Where(x=>extra.Any(t=>x.Id.ToString()==t||x.Name.Equals(t,StringComparison.OrdinalIgnoreCase))));
   var affordable=pool.Where(x=>x.Reference>0&&x.Reference<=s.Budget-s.Reserve).ToList();if(affordable.Count==0)affordable=pool;
   int each=Math.Max(3,s.Depth/Slots(s).Length);
   foreach(var stream in new[]{affordable.OrderByDescending(x=>Power(x)).ToList(),affordable.OrderByDescending(x=>Power(x)/Math.Max(1,x.Reference)).ToList(),pool.OrderBy(x=>x.Reference<=0?long.MaxValue:x.Reference).ToList()}){
    if(stream.Count>0&&selected.All(x=>x.Id!=stream[0].Id))selected.Add(stream[0]);
   }
   foreach(var x in affordable.OrderByDescending(Power))if(selected.Count<each&&selected.All(y=>y.Id!=x.Id))selected.Add(x);
   groups.Add(selected);
  }
  // Round-robin preserves coverage when extra requested weapons exceed a slot quota.
  var result=new List<GearItem>();
  for(int index=0;result.Count<s.Depth&&groups.Any(g=>g.Count>index);index++)
   foreach(var group in groups)if(index<group.Count&&result.Count<s.Depth&&result.All(x=>x.Id!=group[index].Id))result.Add(group[index]);
  return result;
 }
 private static GearOffer Offer(GearItem item,long uid,long price,string source,int seller,DateTime at,DateTime collected,bool known)=>new(){
  Key=source+":"+item.Id+":"+uid+":"+seller+":"+price,ItemId=item.Id,Uid=uid,Price=price,Name=item.Name,Slot=item.Slot,Category=item.Category,Source=source,Seller=seller,ObservedUtc=at,CollectedUtc=collected,TimestampKnown=known,Coverage=new(item.Coverage),Requirement=item.Requirement};
 private static void Stats(GearOffer offer,JsonElement stats){
  offer.Damage=Num(stats,"damage");offer.Accuracy=Num(stats,"accuracy");offer.Armor=Num(stats,"armor");offer.Quality=Num(stats,"quality");
 }
 public static List<GearOffer> Market(string json,GearItem item,DateTime now){
  using var d=JsonDocument.Parse(json);var market=Field(d.RootElement,"itemmarket");var listedId=Int(Field(market,"item"),"id");if(listedId>0&&listedId!=item.Id)throw new InvalidDataException("Market response belongs to a different item.");var listings=Field(market,"listings");var rows=new List<GearOffer>();if(listings.ValueKind!=JsonValueKind.Array)return rows;
  long stamp=Int(market,"cache_timestamp");DateTime at=stamp>0?DateTimeOffset.FromUnixTimeSeconds(stamp).UtcDateTime:now;
  foreach(var row in listings.EnumerateArray()){
   long price=Int(row,"price");if(price<=0||price>1000000000000L||Int(row,"amount")<=0)continue;
   var details=Field(row,"item_details");var offer=Offer(item,Int(details,"uid"),price,"Item Market",0,at,now,stamp>0);Stats(offer,Field(details,"stats"));
   var bonuses=Field(details,"bonuses");offer.BonusKnown=bonuses.ValueKind==JsonValueKind.Array;if(offer.BonusKnown)offer.Bonuses=bonuses.EnumerateArray().Select(b=>(Text(b,"title")!=""?Text(b,"title"):Text(b,"name")!=""?Text(b,"name"):"Bonus")+" "+(Num(b,"value")?.ToString()??"")).ToList();
   rows.Add(offer);
  }return rows;
 }
 public static List<int> Directory(string json){using var d=JsonDocument.Parse(json);var rows=Field(Field(d.RootElement,"bazaar"),"specialized");return rows.ValueKind==JsonValueKind.Array?rows.EnumerateArray().Where(x=>Field(x,"is_open").ValueKind==JsonValueKind.True).Select(x=>(int)Int(x,"id")).Where(x=>x>0).Distinct().ToList():new();}
 public static List<GearOffer> Bazaar(string json,IReadOnlyList<GearItem> catalog,int seller,DateTime now){
  using var d=JsonDocument.Parse(json);var rows=new List<GearOffer>();var list=Field(d.RootElement,"bazaar");if(list.ValueKind!=JsonValueKind.Array)return rows;
  foreach(var row in list.EnumerateArray()){
   int id=(int)Math.Max(Int(row,"ID"),Int(row,"id"));var item=catalog.FirstOrDefault(x=>x.Id==id);long price=Int(row,"price");
   if(item==null||price<=0||price>1000000000000L||Int(row,"quantity")<=0)continue;
   var detail=Field(row,"item_details");long uid=Math.Max(Int(row,"UID"),Math.Max(Int(row,"uid"),Int(detail,"uid")));
   var offer=Offer(item,uid,price,"Bazaar",seller,now,now,false);Stats(offer,Field(detail,"stats").ValueKind==JsonValueKind.Object?Field(detail,"stats"):Field(row,"stats"));
   // Legacy bazaar summaries do not reliably expose individual rarity/bonuses: never infer them from generic item names.
   var bonuses=Field(detail,"bonuses");if(bonuses.ValueKind!=JsonValueKind.Array)bonuses=Field(row,"bonuses");
   offer.BonusKnown=bonuses.ValueKind==JsonValueKind.Array;
   if(offer.BonusKnown)offer.Bonuses=bonuses.EnumerateArray().Select(b=>Text(b,"title")!=""?Text(b,"title"):"Bonus").ToList();
   rows.Add(offer);
  }return rows;
 }
 public static void ApplyStats(string json,IEnumerable<GearOffer> offers){
  using var d=JsonDocument.Parse(json);var pool=offers.ToList();
  foreach(var row in d.RootElement.GetProperty("itemdetails").EnumerateArray()){
   long uid=Int(row,"uid");int id=(int)Int(row,"id");var stats=Field(row,"stats");if(stats.ValueKind!=JsonValueKind.Array)continue;
   foreach(var offer in pool.Where(x=>x.Uid==uid&&x.ItemId==id)){
    foreach(var stat in stats.EnumerateArray()){
     string name=Text(stat,"name").ToLowerInvariant();var v=Num(stat,"value");if(v==null)continue;
     if(name=="damage")offer.Damage=v;else if(name=="accuracy")offer.Accuracy=v;else if(name=="armor")offer.Armor=v;else if(name=="quality")offer.Quality=v;
    }
   }
  }
 }
 public static bool Fresh(GearOffer offer,DateTime now)=>offer.ObservedUtc<=now.AddMinutes(2)&&offer.ObservedUtc>=now.AddMinutes(-75)&&offer.CollectedUtc>=now.AddMinutes(-75)&&offer.CollectedUtc<=now.AddMinutes(2);
 private static decimal Weapon(GearOffer o,string bias)=>o.Damage.HasValue&&o.Accuracy.HasValue?
  bias=="accuracy"?(o.Damage.Value*o.Accuracy.Value*o.Accuracy.Value/10000m):bias=="damage"?(o.Damage.Value*o.Damage.Value*o.Accuracy.Value/10000m):o.Damage.Value*o.Accuracy.Value/100m:0;
 public static decimal Score(IEnumerable<GearOffer> offers,GearSettings s){
  var rows=offers.ToList();decimal offense=rows.Where(x=>x.Slot=="weapon").Select(x=>Weapon(x,s.Bias)).DefaultIfEmpty(0).Max();
  // Conservative coverage proxy: use the best rating*coverage per part, never add overlapping protection.
  decimal defense=Parts.Average(part=>rows.Where(x=>x.Slot!="weapon").Select(x=>(x.Armor??0)*x.Coverage.GetValueOrDefault(part)/100m).DefaultIfEmpty(0).Max());
  return s.Scope=="weapon"?offense:s.Scope=="armor"?defense:(offense+defense)/2m;
 }
 public static List<GearBuild> Build(IEnumerable<GearOffer> offers,GearSettings settings,DateTime now){
  Validate(settings);long limit=settings.Budget-settings.Reserve;
  var pool=offers.Where(o=>o.Price>0&&o.Price<=limit&&o.StatsKnown&&Fresh(o,now)&&(settings.AllowBonus||o.BonusKnown&&o.Bonuses.Count==0)&&Slots(settings).Contains(o.Slot)&&(o.Slot!="weapon"||settings.Scope is not ("melee" or "primary")||o.Category.Equals(settings.Scope,StringComparison.OrdinalIgnoreCase)))
   .GroupBy(o=>o.Uid).Select(g=>g.OrderBy(o=>o.Price).ThenByDescending(o=>o.ObservedUtc).First()).ToList();
  var states=new List<GearBuild>{new("Best found",0,0,new(),new())};
  foreach(var slot in Slots(settings)){
   var options=pool.Where(x=>x.Slot==slot).ToList();
   // Bounded search keeps strong, cheap and price-diverse choices; it is not a proof of a global market optimum.
   options=options.OrderByDescending(x=>Score([x],settings)).Take(20).Concat(options.OrderBy(x=>x.Price).Take(12)).Concat(options.OrderByDescending(x=>Score([x],settings)/x.Price).Take(12)).DistinctBy(x=>x.Key).ToList();
   var next=new List<GearBuild>();
   foreach(var state in states){
    next.Add(new("Best found",state.Cost,state.Score,state.Items.ToList(),state.Missing.Concat([slot]).ToList()));
    foreach(var item in options)if(item.Price<=limit-state.Cost&&state.Items.All(i=>i.Uid!=item.Uid)){
     var selected=state.Items.Concat([item]).ToList();next.Add(new("Best found",state.Cost+item.Price,Score(selected,settings),selected,state.Missing.ToList()));
    }
   }
   // Keep budget-diverse states so early expensive pieces do not crowd out a complete affordable set.
   states=next.OrderBy(x=>x.Missing.Count).ThenByDescending(x=>x.Score).ThenBy(x=>x.Cost).Take(1200)
    .Concat(next.GroupBy(x=>Math.Min(100,x.Cost/(Math.Max(1,limit/100)))).SelectMany(g=>g.OrderBy(x=>x.Missing.Count).ThenByDescending(x=>x.Score).Take(8))).DistinctBy(x=>string.Join(",",x.Items.Select(i=>i.Key))).ToList();
  }
  var complete=states.Where(x=>x.Missing.Count==0).ToList();var usable=complete.Count>0?complete:states;
  var result=new List<GearBuild>();foreach(var pick in usable.Where(x=>x.Items.Count>0).OrderBy(x=>x.Missing.Count).ThenByDescending(x=>x.Score).ThenBy(x=>x.Cost)){
   if(result.All(r=>string.Join(",",r.Items.Select(x=>x.Key))!=string.Join(",",pick.Items.Select(x=>x.Key))))result.Add(pick with{Name=result.Count==0?"Best found":"Alternative "+result.Count});
   if(result.Count==3)break;
  }
  return result;
 }
}
