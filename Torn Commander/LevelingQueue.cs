using System.Text.Json;
using System.Text.RegularExpressions;
namespace TornCommander;
public sealed class LevelingTarget {
 public int Id {get;set;} public string Name {get;set;}=""; public int Level {get;set;}
 public double? EstimatedStats {get;set;} public DateTime? EstimateUtc {get;set;} public DateTime? ScannedUtc {get;set;} public DateTime? LastActionUtc {get;set;} public string EstimateSource {get;set;}=""; public string Suitability {get;set;}="Scan required";
 public string Source {get;set;}=""; public long? ListedBattleStats {get;set;}
 public double? FairFight {get;set;} public string Status {get;set;}="Not checked";
 public DateTime? CheckedUtc {get;set;} public bool Finished {get;set;}
}
public sealed class LevelingSession {
 public double OwnStats {get;set;} public DateTime? OwnStatsUtc {get;set;} public double MaxStatRatio {get;set;}=0.5; public int MaxEstimateDays {get;set;}=30; public double MaxFairFight {get;set;}=2; public bool InactiveOnly {get;set;}=true;
 public string BaldrSource {get;set;}=""; public DateTime? BaldrLoadedUtc {get;set;} public List<string> BaldrGroups {get;set;}=new();
 public List<LevelingTarget> Targets {get;set;}=new(); public int EnergyReserve {get;set;}
 public bool AutoHeal {get;set;} public string WeaponSlot {get;set;}="weapon_primary"; public int AttackDelayMs {get;set;}=1000;
 public int LifePercent {get;set;}=100; public int AttackLimit {get;set;}
 public int Wins {get;set;} public int OtherResults {get;set;} public List<long> AttackIds {get;set;}=new();
 public int PendingTarget {get;set;} public DateTime? OpenedUtc {get;set;}
 public Dictionary<int,int> Inventory {get;set;}=new(){{68,0},{67,0},{66,0}};
 public DateTime? InventoryUtc {get;set;}
 public string Note {get;set;}="Import a target list, then check the next target.";
}
internal sealed record LevelingHealPlan(string State,string Reason,int[] Items,int CooldownSeconds,int TargetLife);
internal static class LevelingPlanner {
 public static List<LevelingTarget> Import(string text,int self=0){
  if(text.Length>200000)throw new InvalidDataException("Target import is too large.");
  var rows=new List<LevelingTarget>();
  if(text.TrimStart().StartsWith("{")||text.TrimStart().StartsWith("[")){
   using var d=JsonDocument.Parse(text);var a=d.RootElement;
   if(a.ValueKind==JsonValueKind.Object)a=a.GetProperty("targets");
   foreach(var x in a.EnumerateArray()){
    int id=x.TryGetProperty("player_id",out var p)?p.GetInt32():x.GetProperty("id").GetInt32();
    rows.Add(new(){Id=id,Name=x.TryGetProperty("name",out p)?p.GetString()??"":"",Level=x.TryGetProperty("level",out p)?p.GetInt32():0,FairFight=x.TryGetProperty("fair_fight",out p)&&p.TryGetDouble(out double f)&&double.IsFinite(f)?f:null});
   }
  }else foreach(var line in text.Split('\n')){
   var url=Regex.Match(line,@"https://(?:www\.)?torn\.com/(?:profiles|page)\.php[^\s<>]*",RegexOptions.IgnoreCase);
   if(url.Success){var m=Regex.Match(url.Value,@"[?&](?:XID|user2ID)=(\d{1,9})(?:\D|$)",RegexOptions.IgnoreCase);if(m.Success)rows.Add(new(){Id=int.Parse(m.Groups[1].Value)});}
   else {var m=Regex.Match(line,@"^\s*(\d{1,9})\s*(?:[,;\t].*)?$");if(m.Success)rows.Add(new(){Id=int.Parse(m.Groups[1].Value)});}
  }
  rows=rows.Where(x=>x.Id>0&&x.Id!=self).DistinctBy(x=>x.Id).ToList();
  if(rows.Count is <1 or >200)throw new InvalidDataException("Import 1–200 distinct target IDs, one per line, Torn profile links, ID-first CSV, or FFScouter targets JSON.");
  return rows;
 }
 public static LevelingHealPlan Recovery(MedicalObservation o,SmartMedicalSettings cfg,int life,int maximum,int percent,DateTime now){
  cfg.Validate();LevelingHealPlan Stop(string state,string reason)=>new(state,reason,Array.Empty<int>(),0,0);
  if(now-o.ObservedUtc>TimeSpan.FromSeconds(45)||o.ObservedUtc>now.AddSeconds(2)||maximum<=0||life<0||life>maximum||percent is <1 or >100||o.MaximumSeconds is <21600 or >32400||o.CooldownSeconds<0||!double.IsFinite(o.BonusPercent)||o.BonusPercent is <0 or >50)return Stop("Unverified","Refresh account life and medical state before planning.");
  int goal=(int)Math.Ceiling(maximum*(percent/100d));int deficit=Math.Max(0,goal-life);
  int hospital=Math.Max(0,(int)Math.Ceiling((o.HospitalUntilUtc-now).TotalSeconds));
  if(hospital>86400)return Stop("Unverified","Review the hospital condition manually.");
  if(deficit==0&&hospital==0)return new("Ready","Life target reached; no medical item needed.",Array.Empty<int>(),0,goal);
  int room=o.MaximumSeconds-cfg.ReserveMinutes*60-o.CooldownSeconds;
  if(room<=0)return Stop("Cooldown","Medical cooldown/reserve prevents another item. Wait for capacity; drugs are excluded.");
  var catalog=SmartMedicalOptimizer.Catalog.Where(x=>x.Id!=739).ToArray();
  double bonus=1+o.BonusPercent/100;
  var quantities=catalog.Select(x=>cfg.Permissions.GetValueOrDefault(x.Id)?Math.Max(0,o.Inventory.GetValueOrDefault(x.Id)-cfg.Reserves.GetValueOrDefault(x.Id)):0).ToArray();
  int Gain(MedicalItem x)=>Math.Max(1,(int)Math.Floor(maximum*(x.Id==68?.05:x.Id==67?.10:.15)*bonus));
  var limits=catalog.Select((x,i)=>Math.Min(quantities[i],Math.Min((room+1200)/x.CooldownSeconds,Math.Max((int)Math.Ceiling(hospital/(x.HospitalSeconds*bonus)),(int)Math.Ceiling((double)deficit/Gain(x)))))).ToArray();
  int[]? best=null;int bestCost=int.MaxValue,bestCount=int.MaxValue;long bestPrice=long.MaxValue;
  for(int a=0;a<=limits[0];a++)for(int b=0;b<=limits[1];b++)for(int c=0;c<=limits[2];c++){
   int[] counts={a,b,c};int cost=0,heal=0,hp=0,last=0,count=a+b+c;long price=0;
   for(int i=0;i<3;i++){var x=catalog[i];cost+=counts[i]*x.CooldownSeconds;heal+=counts[i]*(int)Math.Floor(x.HospitalSeconds*bonus);hp+=counts[i]*Gain(x);price+=counts[i]*cfg.Prices.GetValueOrDefault(x.Id,x.BasePrice);if(counts[i]>0)last=Math.Max(last,x.CooldownSeconds);}
   if(heal<hospital||hp<deficit||count==0||(cfg.ReserveMinutes>0?cost>room:cost-last>=room))continue;
   if(cost<bestCost||(cost==bestCost&&(count<bestCount||(count==bestCount&&price<bestPrice)))){best=counts;bestCost=cost;bestCount=count;bestPrice=price;}
  }
  if(best==null)return Stop("Supplies / capacity","The requested life target and hospital exit do not fit permitted supplies and cooldown. Lower the target or wait; no drug fallback.");
  var items=catalog.SelectMany((x,i)=>Enumerable.Repeat(x.Id,best[i])).OrderBy(id=>SmartMedicalOptimizer.Item(id).CooldownSeconds).ToArray();
  return new("Heal first","Estimated minimum cooldown to leave hospital and reach "+percent+"% life. Use one item manually, then refresh/recalculate; natural recovery and rounding can change the remainder.",items,bestCost,goal);
 }
 public static (long Id,string Result)? FindReceipt(string json,int self,int target,DateTime opened,IEnumerable<long> seen){
  using var d=JsonDocument.Parse(json);long start=new DateTimeOffset(opened).ToUnixTimeSeconds();
  foreach(var a in d.RootElement.GetProperty("attacks").EnumerateArray()){
   if(a.GetProperty("attacker").ValueKind!=JsonValueKind.Object||a.GetProperty("attacker").GetProperty("id").GetInt32()!=self||a.GetProperty("defender").GetProperty("id").GetInt32()!=target||a.GetProperty("started").GetInt64()<start||a.GetProperty("ended").GetInt64()<=0)continue;
   long id=a.GetProperty("id").GetInt64();if(!seen.Contains(id))return(id,a.GetProperty("result").GetString()??"Unknown");
  }return null;
 }
}
