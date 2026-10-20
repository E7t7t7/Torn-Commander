namespace TornCommander;
internal sealed record TravelWaitOption(string Item,string Source,string Kind,double WaitMinutes,double FutureMinutes,long ConditionalProfit,double ConditionalProfitPerHour,string Evidence);
internal static class TravelWaitAdvisor
{
    public static List<TravelWaitOption> Compare(IEnumerable<TravelForecastItem> rows,string location,DateTime readyUtc,DateTime now)
    {
        var result=new List<TravelWaitOption>();
        foreach(var x in rows.Where(x=>x.Country.Equals(location,StringComparison.OrdinalIgnoreCase)&&!x.IsStale&&x.StockUpdatedAtUtc.HasValue&&now-x.StockUpdatedAtUtc.Value<=TimeSpan.FromMinutes(30)&&x.ProfitPerItem>0&&x.Units>0)){
            bool observed=x.Stock is >0;double wait=0;
            if(!observed){if(!x.RestockWindowEndUtc.HasValue||x.RestockWindowEndUtc<readyUtc)continue;wait=Math.Max(0,(x.RestockWindowEndUtc.Value-readyUtc).TotalMinutes);}
            var future=wait+x.FlightMinutesOneWay;if(future<=0)continue;
            var remainingFare=Math.Max(0,x.ProfitPerItem*x.Units-x.TripProfit)/2;var profit=x.ProfitPerItem*x.Units-remainingFare;
            result.Add(new(x.ItemName,x.Source,observed?"Buy available alternative":"Wait scenario",Math.Round(wait,1),Math.Round(future,1),profit,Math.Round(profit*60/future),observed?"Current report only; verify shelf before buying.":"Uses the end of an estimated window and assumes a full planned basket becomes available. Quantity and demand are not guaranteed."));
        }
        return result.OrderByDescending(x=>x.ConditionalProfitPerHour).ToList();
    }
}
