namespace TornCommander;
internal sealed record MarketHistoryPoint(int ItemId,string Name,DateTime ObservedUtc,long? LowestReturnedAsk,long ReturnedQuantity,string Source="Official Torn item market");
internal static class MarketPriceAlerts
{
    public static List<string> Crossings(ItemWatch watch,MarketHistoryPoint? before,MarketHistoryPoint after,DateTime now){
        var result=new List<string>();if(before is null||before.ItemId!=after.ItemId||before.Source!=after.Source||!before.LowestReturnedAsk.HasValue||!after.LowestReturnedAsk.HasValue||after.ObservedUtc<=before.ObservedUtc||now-after.ObservedUtc>TimeSpan.FromMinutes(20)||after.ObservedUtc>now.AddMinutes(2)||after.ObservedUtc-before.ObservedUtc>TimeSpan.FromMinutes(20))return result;
        if(watch.PriceBelow is long low&&before.LowestReturnedAsk>=low&&after.LowestReturnedAsk<low)result.Add("Lowest returned market ask fell below $"+low);
        if(watch.PriceAbove is long high&&before.LowestReturnedAsk<=high&&after.LowestReturnedAsk>high)result.Add("Lowest returned market ask rose above $"+high);
        return result;
    }
}
