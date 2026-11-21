using System.Text.Json;
namespace TornCommander;
public sealed class FinanceEntry {
 public string Id {get;set;}=Guid.NewGuid().ToString("N");
 public DateTime At {get;set;}=DateTime.UtcNow;
 public string Category {get;set;}="Other";
 public string Kind {get;set;}="review";
 public decimal Amount {get;set;}
 public string Currency {get;set;}="cash";
 public string Note {get;set;}="";
 public string Source {get;set;}="manual";
 public string Direction {get;set;}="";
}
public sealed class FinanceBalance {
 public DateTime At {get;set;}
 public long? Company {get;set;}
 public long Wallet {get;set;} public long Vault {get;set;} public long Cayman {get;set;}
 public long Bank {get;set;} public long BankProfit {get;set;} public long BankUntil {get;set;}
 public long? Faction {get;set;} public long? Networth {get;set;}
 public decimal Accessible=> (decimal)Wallet+Vault;
 public decimal CashSavings=>Accessible+Cayman+Bank;
}
public sealed class FinanceJournal {
 public int Player {get;set;}
 public long Goal {get;set;}=500000000;
 public long Reserve {get;set;}=10000000;
 public long DailyIncome {get;set;}=20000000;
 public long DailySpending {get;set;}
 public bool Monitor {get;set;}
 public List<FinanceEntry> Entries {get;set;}=new();
 public List<FinanceBalance> Balances {get;set;}=new();
 public DateTime? LastSync {get;set;}
 public DateTime? LastLogSync {get;set;}public string BalanceStatus{get;set;}="Not synced";public string LogStatus{get;set;}="History not scanned";public int LogRowsScanned{get;set;}public int LogPages{get;set;}public int LogCategories{get;set;}public int PartialCategories{get;set;}
 public string Note {get;set;}="Sync balances and recent logs, then review uncategorized receipts. History starts with real observations.";
}
public sealed record FinanceSummary(decimal Income,decimal Spending,decimal Net,decimal XanaxIncome,decimal XanaxSpending,int Review,decimal Savings,decimal Remaining,decimal Percent,decimal? ScenarioDays,decimal? ObservedDaily);
public static class FinanceData {
 public static readonly string[] Categories=["Loss sales","Item sales","Trading","Bank interest","Company","Job","Stocks","Travel","Crimes","Training / drugs","Property","Transfers","Other"];
 public static void Validate(FinanceEntry e,DateTime now) {
  if(e.Amount<=0||e.Amount>1000000000000m||decimal.Truncate(e.Amount)!=e.Amount||e.At>now.AddMinutes(2)||e.At<new DateTime(2004,1,1,0,0,0,DateTimeKind.Utc)||
   e.Kind is not ("income" or "expense" or "transfer" or "review")||e.Currency is not ("cash" or "xanax")||!Categories.Contains(e.Category)||e.Note.Length>240)
   throw new InvalidDataException("Use a positive whole amount, valid date, category and currency.");
 }
 public static FinanceBalance Balance(string json,DateTime at) {
  using var doc=JsonDocument.Parse(json);var r=doc.RootElement;var m=r.GetProperty("money");
  long N(JsonElement x,string k)=>x.GetProperty(k).GetInt64();
  var b=new FinanceBalance{At=at,Wallet=N(m,"wallet"),Vault=N(m,"vault"),Cayman=N(m,"cayman_bank")};
  if(m.TryGetProperty("company",out var co)&&co.ValueKind==JsonValueKind.Number)b.Company=co.GetInt64();
  if(m.TryGetProperty("city_bank",out var bank)&&bank.ValueKind==JsonValueKind.Object){b.Bank=N(bank,"amount");b.BankProfit=N(bank,"profit");b.BankUntil=N(bank,"until");}
  if(m.TryGetProperty("faction",out var f)&&f.ValueKind==JsonValueKind.Object)b.Faction=N(f,"money");
  if(r.TryGetProperty("networth",out var nw))b.Networth=N(nw,"total");
  if(b.Wallet<0||b.Vault<0||b.Cayman<0||b.Bank<0||b.BankProfit<0)throw new InvalidDataException("Negative balance received; previous observation preserved.");
  return b;
 }
 // The official log data is dynamic. Ambiguous receipts require review, never silently become profit.
 public static List<FinanceEntry> Logs(string json,DateTime now) {
  using var doc=JsonDocument.Parse(json);var list=new List<FinanceEntry>();
  foreach(var row in doc.RootElement.GetProperty("log").EnumerateArray()){
   try{
    var data=row.GetProperty("data");string title=row.GetProperty("details").GetProperty("title").GetString()??"",t=title.ToLowerInvariant();
    string id=row.GetProperty("id").GetString()??"";if(id.Length is <1 or >200)continue;
    var at=DateTimeOffset.FromUnixTimeSeconds(row.GetProperty("timestamp").GetInt64()).UtcDateTime;
    decimal? Amount(params string[] names){foreach(string name in names)if(data.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDecimal(out var n)&&n>0&&n<=1000000000000m&&decimal.Truncate(n)==n)return n;return null;}
    void Add(decimal? amount,string kind,string category,string direction,string suffix=""){
     if(!amount.HasValue)return;var e=new FinanceEntry{Id="torn:"+id+suffix,At=at,Amount=amount.Value,Kind=kind,Category=category,Direction=direction,Source="Torn log",Note=title.Length>240?title[..240]:title};
     Validate(e,now);list.Add(e);
    }
    switch(t){
     case "item market buy":case "item market buy (old)":case "bazaar buy":case "bazaar buy (legacy)":
      Add(Amount("cost_total","money"),"expense","Trading","out");break;
     case "bazaar sell":case "bazaar sell (legacy)":
      Add(Amount("cost_total","money_received","money"),"income","Item sales","in");break;
     case "item market sell":case "item market sell (old)":
      // Prefer an explicit received amount. Otherwise fees are unknown and need review.
      var received=Amount("money_received");Add(received??Amount("cost_total","money"),received.HasValue?"income":"review","Item sales","in");break;
     case "job pay":case "job wage":case "job special gain money":
      Add(Amount("money","pay","money_gained"),"income","Job","in");break;
     case "bank interest":case "offshore bank interest":
      Add(Amount("interest","money"),"income","Bank interest","in");break;
     case "vault deposit":case "bank invest":case "bank investment":case "offshore bank deposit":case "cayman bank deposit":case "faction deposit money":case "faction deposit":case "company deposit":
      Add(Amount("money","money_deposited","deposited","amount"),"transfer","Transfers","out");break;
     case "vault withdraw":case "bank withdraw":case "offshore bank withdraw":case "cayman bank withdraw":case "faction withdraw":case "company withdraw":
      Add(Amount("money","money_withdrawn","withdrawn","amount"),"transfer","Transfers","in");break;
     case "money receive":case "faction give money receive":case "faction payout money receive":
      Add(Amount("money","money_given","money_received"),"review","Other","in");break;
     case "money send":case "faction give money send":case "faction payout money send":
      Add(Amount("money","money_given","money_sent"),"review","Other","out");break;
     case "faction payout money balance receive":case "faction payout money balance send":
      // Faction book credit is not wallet cash; do not import it as received money.
      break;
     case "property upkeep":Add(Amount("cost","money","upkeep"),"expense","Property","out");break;
     case "crime success money gain":case "crime success money gain (new)":
      Add(Amount("money_gained","money"),"income","Crimes","in");break;
     case "crime success money paid":case "crime critical fail money loss":case "crime fail money loss":
      Add(Amount("money_lost","money_paid","money"),"expense","Crimes","out");break;
     case "casino slots win":
      Add(Amount("bet_amount"),"expense","Other","out",":stake");Add(Amount("won_amount"),"income","Other","in",":payout");break;
     case "casino slots lose":Add(Amount("bet_amount"),"expense","Other","out",":stake");break;
     default:Add(Amount("money"),"review","Other","");break;
    }
   }catch(Exception ex) when(ex is InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException or FormatException){
    // A new/invalid log shape must not discard the rest of the financial scan.
   }
  }return list;
 }
 public static string? PreviousLogPage(string json){
  using var d=JsonDocument.Parse(json);
  if(!d.RootElement.TryGetProperty("_metadata",out var m)||!m.TryGetProperty("links",out var links)||!links.TryGetProperty("prev",out var p)||p.ValueKind!=JsonValueKind.String)return null;
  if(!Uri.TryCreate(p.GetString(),UriKind.Absolute,out var u)||u.Scheme!="https"||u.Host!="api.torn.com"||u.AbsolutePath!="/v2/user/log"||u.Fragment.Length>0)throw new InvalidDataException("Untrusted financial pagination link.");
  foreach(string field in u.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)){
   var pair=field.Split('=',2);if(pair.Length!=2||pair[0] is not ("cat" or "limit" or "from" or "to" or "nanostamp" or "timestamp" or "sort")||
    !System.Text.RegularExpressions.Regex.IsMatch(Uri.UnescapeDataString(pair[1]),@"^[0-9.,\-A-Za-z]+$"))throw new InvalidDataException("Unsupported financial pagination parameters.");
  }
  return "user/log"+u.Query;
 }
 public static List<(int Id,string Name)> FinancialCategories(string json){
  string[] names=["Money","Money outgoing","Money incoming","Item market","Bazaars","Bank","Vault","Offshore bank","Company","Job","Trades","Property","Upkeep","Property rental","Stocks","Crime success","Crime critical failure","Slots","Blackjack","Roulette","High-low","Keno","Craps","Bookie"];
  using var d=JsonDocument.Parse(json);return d.RootElement.GetProperty("logcategories").EnumerateArray().Where(x=>names.Contains(x.GetProperty("title").GetString(),StringComparer.OrdinalIgnoreCase))
   .Select(x=>(Id:x.GetProperty("id").GetInt32(),Name:x.GetProperty("title").GetString()??"")).Where(x=>x.Id>0).OrderBy(x=>Array.FindIndex(names,n=>n.Equals(x.Name,StringComparison.OrdinalIgnoreCase))).ToList();
 }
 public static void Merge(FinanceJournal journal,IEnumerable<FinanceEntry> rows) {
  var known=journal.Entries.Select(e=>e.Id).ToHashSet(StringComparer.Ordinal);
  foreach(var e in rows)if(known.Add(e.Id))journal.Entries.Add(e);
 }
 public static FinanceSummary Summary(FinanceJournal j,DateTime now) {
  var entries=j.Entries.Where(e=>e.At>=now.Date.AddDays(-29)&&e.At<=now).ToList();
  decimal Sum(string kind,string currency)=>entries.Where(e=>e.Kind==kind&&e.Currency==currency).Sum(e=>e.Amount);
  decimal savings=Math.Max(0,(j.Balances.OrderByDescending(b=>b.At).FirstOrDefault()?.CashSavings??0)-j.Reserve);
  decimal remaining=Math.Max(0,j.Goal-savings),rate=j.DailyIncome-j.DailySpending;
  var daily=j.Balances.Where(b=>b.At<=now&&b.At>=now.AddDays(-30)).GroupBy(b=>b.At.Date).Select(g=>g.MaxBy(b=>b.At)!).OrderBy(b=>b.At).ToList();
  decimal? observed=daily.Count>=3&&(daily[^1].At-daily[0].At).TotalDays>=2?
   (daily[^1].CashSavings-daily[0].CashSavings)/(decimal)(daily[^1].At-daily[0].At).TotalDays:null;
  return new(Sum("income","cash"),Sum("expense","cash"),Sum("income","cash")-Sum("expense","cash"),Sum("income","xanax"),Sum("expense","xanax"),
   entries.Count(e=>e.Kind=="review"),savings,remaining,Math.Min(100,savings/Math.Max(1,j.Goal)*100),remaining==0?0:rate>0?Math.Ceiling(remaining/rate):null,observed);
 }
 public static List<string> Advice(FinanceJournal j,FinanceSummary s,DateTime now) {
  var advice=new List<string>();
  if(j.Balances.Count==0)advice.Add("First step: sync balances. Net worth includes unsold assets; it is not your Private Island buying budget.");
  if(s.Review>0)advice.Add($"Review {s.Review} cash logs before trusting the income total. A money receipt could be a sale, gift, loan or transfer.");
  advice.Add($"Your scenario is {j.DailyIncome:N0} income minus {j.DailySpending:N0} spending each day. Include medical supplies, Xanax, refills, rent, wages and advertising; gross loss payouts are not take-home profit.");
  if(s.Spending>0) {
   var biggest=j.Entries.Where(e=>e.Kind=="expense"&&e.Currency=="cash"&&e.At>=now.Date.AddDays(-29)&&e.At<=now).GroupBy(e=>e.Category).OrderByDescending(g=>g.Sum(e=>e.Amount)).First();
   advice.Add($"Largest recorded expense: {biggest.Key}, USD {biggest.Sum(e=>e.Amount):N0} in 30 days. Cutting a repeat USD 1m/day saves USD 30m over 30 days; protect training and education before trimming growth spending.");
  }
  var largest=j.Entries.Where(e=>e.Kind=="income"&&e.Currency=="cash"&&e.At>=now.Date.AddDays(-29)&&e.At<=now).GroupBy(e=>e.Category).OrderByDescending(g=>g.Sum(e=>e.Amount)).FirstOrDefault();
  if(largest!=null&&s.Income>0)advice.Add($"Largest recorded income source: {largest.Key}, {largest.Sum(e=>e.Amount)/s.Income*100:N0}% of classified cash receipts. Test a second source before increasing reliance on one activity.");
  var b=j.Balances.MaxBy(b=>b.At);
  if(b?.Bank>0)advice.Add($"Bank: USD {b.Bank:N0} principal with USD {b.BankProfit:N0} reported interest at maturity. Principal is savings, not income. Available after {DateTimeOffset.FromUnixTimeSeconds(b.BankUntil):u}; compare this lock-up with your purchase date.");
  advice.Add("Next income stream: compare fresh Travel profit/hour with Trading net margins after fees. Start with a small repeatable route or trade, record its actual costs, and scale only after several profitable completions.");
  advice.Add("Loss selling consumes energy that could grow battle stats. Keep your planned gym share and education active; use the Progression tab to balance cash now with stronger future earning options.");
  advice.Add("Before buying the island, compare a current rental against the full purchase + upgrades + upkeep budget. Keep the reserve and trading capital outside your goal; do not empty income-producing capital just to reach the headline price.");
  if(s.ObservedDaily.HasValue)advice.Add($"Observed cash-savings change: USD {s.ObservedDaily:N0}/day. This includes asset sales/purchases and transfers outside tracked balances; it is not a verified profit rate.");
  return advice;
 }
}
