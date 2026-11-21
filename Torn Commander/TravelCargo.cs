namespace TornCommander;
public sealed record CargoTier(long Price,int Quantity);
public sealed class CargoBook {public DateTime At{get;set;}public List<CargoTier> Tiers{get;set;}=new();}
public sealed class CargoPreferences {
 public Dictionary<int,string> Excluded{get;set;}=new();public Dictionary<int,CargoBook> Books{get;set;}=new();public Dictionary<int,string> Types{get;set;}=new();
 public bool Enabled{get;set;}=true;public string Note{get;set;}="Refresh travel stocks, then scan outbound purchase quotes.";
}
public sealed record CargoShelf(int Item,string Name,string Country,long ShopPrice,string Type,DateTime ShopAt,string Source);
public sealed record OutboundCargo(int Item,string Name,string Country,int Units,long Purchase,long Revenue,DateTime MarketAt,DateTime ShopAt,string Rule,string Source){
 public long Margin=>Revenue-Purchase;
}
public sealed record CargoPair(int ReturnUnits,long ReturnCost,long StartingCash,long Net,double Roi);
public static class TravelCargoMath {
 // Official Contraband list; API item types label these as Other/Medical/etc.
 private static readonly HashSet<string> Contraband=new(StringComparer.OrdinalIgnoreCase){
 "Ambergris Lump","Basalt Point","Bear Gall","Bearer Bond","Chalcedony Point","Chert Point","Counterfeit Manga","Ephedrine Powder","Ergotamine Ampoule","Insulin","Meteorite Fragment","Natural Pearls","Obsidian Point","Pangolin Scales","Patagonian Fossil","Quartz Point","Quartzite Point","Raw Ivory","Safrole Oil","Shark Fin","Tiger Bone Powder","Turtle Shell","Uncut Diamonds","Whale Meat"};
 public static bool BlackMarket(string name,string type)=>type.Equals("Drug",StringComparison.OrdinalIgnoreCase)||Contraband.Contains(name);
 public static OutboundCargo? Quote(CargoShelf shelf,CargoBook book,int capacity,long budget,long outboundFare,DateTime now){
  if(capacity is <1 or >1000||budget<0||outboundFare<0||shelf.ShopPrice<=0||string.IsNullOrWhiteSpace(shelf.Type)||now-shelf.ShopAt>TimeSpan.FromMinutes(30)||shelf.ShopAt>now.AddMinutes(2)||now-book.At>TimeSpan.FromMinutes(15)||book.At>now.AddMinutes(2))return null;
  long sale=BlackMarket(shelf.Name,shelf.Type)?shelf.ShopPrice:(long)decimal.Floor(shelf.ShopPrice*.75m);
  long funds=budget>0?Math.Max(0,budget-outboundFare):long.MaxValue;int left=Math.Min(capacity,100),units=0;long spent=0;
  foreach(var tier in book.Tiers.Where(t=>t.Price>0&&t.Quantity>0).OrderBy(t=>t.Price)){
   if(tier.Price>=sale||left==0)break;int take=(int)Math.Min(Math.Min((long)left,tier.Quantity),funds/tier.Price);
   if(take==0)continue;long cost=checked(take*tier.Price);spent=checked(spent+cost);funds-=cost;units+=take;left-=take;
  }
  return units==0?null:new(shelf.Item,shelf.Name,shelf.Country,units,spent,checked(units*sale),book.At,shelf.ShopAt,BlackMarket(shelf.Name,shelf.Type)?"Black Market: 100% of observed shop price":"General/Arms: 75% of observed shop price",shelf.Source);
 }
 public static CargoPair Pair(OutboundCargo cargo,long importBuy,long importMargin,int stock,int capacity,long budget,long fareEach){
  if(importBuy<=0||stock<0||capacity<1||budget<0||fareEach<0)throw new InvalidDataException("Invalid round-trip inputs.");
  long available=budget==0?long.MaxValue:Math.Max(0,checked(budget-cargo.Purchase+cargo.Revenue-2*fareEach));
  int units=(int)Math.Min(Math.Min((long)capacity,stock),available/importBuy);long cost=checked(units*importBuy);
  long cash=checked(cargo.Purchase+fareEach+Math.Max(0,cost+fareEach-cargo.Revenue));
  long net=checked(cargo.Margin+units*importMargin-2*fareEach);
  return new(units,cost,cash,net,cargo.Purchase+cost==0?0:100d*net/(cargo.Purchase+cost));
 }
}
