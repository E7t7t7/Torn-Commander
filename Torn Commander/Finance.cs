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
}
public sealed class FinanceBalance {
 public DateTime At {get;set;}
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
   var data=row.GetProperty("data");var title=row.GetProperty("details").GetProperty("title").GetString()??"";
   if(!data.TryGetProperty("money",out var money)||money.ValueKind!=JsonValueKind.Number||!money.TryGetDecimal(out var amount)||amount<=0||amount>1000000000000m)continue;
   string id=row.GetProperty("id").GetString()??"";if(id.Length is <1 or >200)continue;
   var e=new FinanceEntry{Id="torn:"+id,At=DateTimeOffset.FromUnixTimeSeconds(row.GetProperty("timestamp").GetInt64()).UtcDateTime,
    Amount=amount,Source="Torn log",Note=title.Length>240?title[..240]:title};
   switch(title.ToLowerInvariant()){
    case "job pay":case "job wage":e.Kind="income";e.Category="Job";break;
    case "bank interest":e.Kind="income";e.Category="Bank interest";break;
    case "vault deposit":case "vault withdraw":case "bank investment":case "bank withdraw":case "cayman bank deposit":case "cayman bank withdraw":case "faction deposit":case "faction withdraw":
     e.Kind="transfer";e.Category="Transfers";break;
   }
   Validate(e,now);list.Add(e);
  }return list;
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
