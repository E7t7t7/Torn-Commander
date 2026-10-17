namespace TornCommander;
public sealed class TradeQuote
{
 public int ItemId { get; set; }
 public string Name { get; set; } = "";
 public DateTime ObservedUtc { get; set; }
 public long Reference { get; set; }
 public List<TradeTier> Tiers { get; set; } = new();
}
public sealed class TradeTier { public long Price { get; set; } public int Quantity { get; set; } }
public sealed class TradingJournal
{
 public List<int> ActiveItemIds { get; set; } = new();
 public string Items { get; set; } = "Beer, First Aid Kit";
 public long Budget { get; set; } = 1000000;
 public long Reserve { get; set; } = 100000;
 public decimal FeePercent { get; set; } = 5;
 public decimal RoiPercent { get; set; } = 5;
 public decimal StressPercent { get; set; } = 3;
 public decimal MaxExposurePercent { get; set; } = 50;
 public long MinimumProfit { get; set; } = 1000;
 public long CostBasis { get; set; }
 public List<TradeQuote> Observations { get; set; } = new();
}
public sealed class TradeOpportunity
{
 public int ItemId { get; set; }
 public string Name { get; set; } = "";
 public DateTime ObservedUtc { get; set; }
 public string Decision { get; set; } = "";
 public string Reason { get; set; } = "";
 public long Lowest { get; set; }
 public long SellTarget { get; set; }
 public long BuyCeiling { get; set; }
 public long Quantity { get; set; }
 public decimal Cost { get; set; }
 public decimal EstimatedNet { get; set; }
 public decimal Roi { get; set; }
 public string Trend { get; set; } = "";
 public bool RiskAnalyzed { get; set; }
 public decimal StressNet { get; set; }
 public decimal ExposurePercent { get; set; }
 public long BreakEvenSell { get; set; }
 public string SellAdvice { get; set; } = "";
}
public static class TradingAdvisor
{
 public static long AvailableCapital(long total,long reserve,long? wallet)
 {
  if(total<0||reserve<0||wallet<0)throw new ArgumentOutOfRangeException("Capital cannot be negative.");
  long manual=Math.Max(0,total-reserve);
  return wallet.HasValue?Math.Min(manual,Math.Max(0,wallet.Value-reserve)):manual;
 }

 public static List<TradeOpportunity> Build(IEnumerable<TradeQuote> quotes, decimal budget, decimal fee, decimal roi, long costBasis, DateTime now, decimal stressPercent=0, decimal maxExposurePercent=100, decimal minimumProfit=0)
 {
  if(stressPercent<0||stressPercent>50||maxExposurePercent<=0||maxExposurePercent>100||minimumProfit<0)throw new ArgumentOutOfRangeException("Invalid trading risk limits.");
  if(budget<0||fee<0||fee>=100||roi<0||roi>1000||costBasis<0)throw new ArgumentOutOfRangeException("Invalid trading limits.");
  var history=quotes.ToList();
  var result=new List<TradeOpportunity>();
  foreach(var group in history.GroupBy(x=>x.ItemId))
  {
   var q=group.OrderByDescending(x=>x.ObservedUtc).First();
   var r=new TradeOpportunity{ItemId=q.ItemId,Name=q.Name,ObservedUtc=q.ObservedUtc,Decision="Watch",Reason="No usable listings."};result.Add(r);
   var tiers=(q.Tiers??new()).Where(t=>t.Price>0&&t.Quantity>0).GroupBy(t=>t.Price).Select(g=>new {Price=g.Key,Quantity=g.Sum(t=>(long)t.Quantity)}).OrderBy(t=>t.Price).ToList();
   if(tiers.Count==0)continue;
   r.Lowest=tiers[0].Price;
   var older=group.Where(x=>x.ObservedUtc<q.ObservedUtc.AddMinutes(-10)&&x.Tiers?.Count>0).OrderByDescending(x=>x.ObservedUtc).FirstOrDefault();
   if(older is not null){
    var prior=older.Tiers.Where(t=>t.Price>0&&t.Quantity>0).Select(t=>t.Price).DefaultIfEmpty(0).Min();
    r.Trend=prior>0?$"Lowest asking price {(r.Lowest>=prior?"up":"down")} {(100m*(r.Lowest-prior)/prior):0.0}% since {older.ObservedUtc:yyyy-MM-dd HH:mm} UTC; not completed sales.":"No comparable earlier quote.";
   }else r.Trend="No independent earlier quote yet; no timing trend inferred.";
   if(now-q.ObservedUtc>TimeSpan.FromMinutes(10)||q.ObservedUtc>now.AddMinutes(1)){r.Decision="Rescan";r.Reason="Quote is stale or has an invalid future time.";continue;}
   decimal netAt(long price)=>price-Math.Ceiling(price*fee/100);
   if(costBasis>0){
    var list=Math.Max(1,r.Lowest-1);var net=netAt(list);var required=costBasis*(1+roi/100);
    var breakEven=Math.Ceiling(costBasis/(1-fee/100));
    r.SellAdvice=net>=required?$"Listing at {list:C0} clears your entered cost and target return after the entered fee, if it sells. Recheck listings before listing.":
     $"Current undercut price {list:C0} misses your target. Fee-adjusted break-even is about {breakEven:C0}; waiting is not a prediction of recovery.";
   }
   if(tiers.Count<2){r.Reason="Only one distinct asking price returned; no visible resale spread.";continue;}
   r.SellTarget=tiers[1].Price-1;
   if(q.Reference>0)r.SellTarget=Math.Min(r.SellTarget,q.Reference);
   var netSell=netAt(r.SellTarget);
   r.BuyCeiling=(long)Math.Floor(netSell/(1+roi/100));
   var cheap=tiers.Where(t=>t.Price<=r.BuyCeiling).ToList();
   r.Quantity=cheap.Sum(t=>t.Quantity);
   r.Cost=cheap.Sum(t=>t.Price*(decimal)t.Quantity);
   if(r.Quantity<=0||r.Cost<=0){r.Reason="Spread does not cover the entered fee and return target.";continue;}
   r.EstimatedNet=netSell*r.Quantity-r.Cost;r.Roi=r.EstimatedNet/r.Cost*100;
   if(r.EstimatedNet<=0){r.Reason="No positive net margin at the visible target.";continue;}
   var averageCost=r.Cost/r.Quantity;
   r.BreakEvenSell=(long)Math.Ceiling(averageCost/(1-fee/100));
   while(netAt(r.BreakEvenSell)<averageCost)r.BreakEvenSell++;
   r.StressNet=netAt((long)Math.Floor(r.SellTarget*(1-stressPercent/100)))*r.Quantity-r.Cost;
   r.ExposurePercent=budget>0?r.Cost/budget*100:0;
   r.RiskAnalyzed=true;
   if(r.EstimatedNet<minimumProfit){r.Reason="Net margin is below your minimum cash-profit target.";continue;}
   if(stressPercent>0&&r.StressNet<=0){r.Reason=$"A {stressPercent:0.#}% fall from the target asking price removes the margin. Fragile spread: watch rather than commit.";continue;}
   r.Decision="Candidate";r.Reason="Conditional on buying all returned cheaper units and reselling at the target. Buyer demand and unreturned listings are unknown.";
  }
  decimal left=budget;
  foreach(var r in result.Where(r=>r.Decision=="Candidate").OrderByDescending(r=>r.Roi).ThenByDescending(r=>r.EstimatedNet)){
   if(maxExposurePercent<100&&r.Cost>budget*maxExposurePercent/100){r.Decision="Watch";r.Reason="Clearing the cheaper listings exceeds your per-item capital limit. Buying only part would leave competing stock.";continue;}
   if(r.Cost>left){r.Decision="Watch";r.Reason="Budget cannot clear the cheaper returned listings; remaining low-price competition would invalidate this flip.";continue;}
   left-=r.Cost;
  }
  return result.OrderBy(r=>r.Decision=="Candidate"?0:r.Decision=="Watch"?1:2).ThenByDescending(r=>r.EstimatedNet).ToList();
 }
}
