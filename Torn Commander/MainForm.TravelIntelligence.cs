namespace TornCommander;
public sealed partial class MainForm
{
    private List<TravelForecastItem>? _evidenceTravel;
    private DateTime _evidenceTravelAt;
    private string _evidenceTravelProfile="";
    private string _evidenceTravelConfig="";
    private List<TravelForecastItem> TravelIntelligenceCandidates()
    {
        var config=string.Join("|",_settings.TravelCapacity,_settings.TravelFlightType,_settings.TravelApplyMarketTax,_settings.TravelCashBudget,_settings.TravelMailingBook);
        if(config==_evidenceTravelConfig&&_evidenceTravel!=null&&_evidenceTravelProfile==CurrentProfileId&&DateTime.UtcNow-_evidenceTravelAt<TimeSpan.FromSeconds(15))return _evidenceTravel;
        _evidenceTravelConfig=config;_evidenceTravelAt=DateTime.UtcNow;_evidenceTravelProfile=CurrentProfileId;var all=_travelForecast.Items.Where(x=>x.Source!="Commander local learning").ToList();
        try{
            var settings=ItemSettings();var now=DateTime.UtcNow;
            foreach(var observation in IntelStore.Latest().Where(x=>x.Kind=="foreign-shop"&&now-x.ObservedUtc<=TimeSpan.FromMinutes(30)).GroupBy(x=>(x.Location,x.ItemId)).Select(g=>g.OrderByDescending(x=>x.ObservedUtc).First())){
                var prior=_travelForecast.Items.Where(x=>x.ItemId==observation.ItemId&&x.MarketValue>0&&x.PricingUpdatedAtUtc>=now.AddHours(-3)).OrderByDescending(x=>x.PricingUpdatedAtUtc).FirstOrDefault();
                var item=new TravelForecastItem{ItemId=observation.ItemId,ItemName=observation.Name,Country=observation.Location,Stock=observation.Quantity,BuyPrice=observation.Cost,MarketValue=observation.MarketValue??prior?.MarketValue??0,StockUpdatedAtUtc=observation.ObservedUtc,PricingUpdatedAtUtc=observation.MarketValue.HasValue?observation.CollectedUtc:prior?.PricingUpdatedAtUtc,Source="Commander evidence / "+observation.Source,Note="Timestamped foreign-shop observation; verify current shelf. Market values are indicative."};
                if(item.Stock==0&&settings.Watches.Any(w=>w.ItemId==item.ItemId&&w.Location==item.Country)){
                    var estimate=RestockEstimator.Analyze(IntelStore.Read(item.ItemId,item.Country,settings.RetentionDays).Where(x=>x.Source==observation.Source),now);
                    if(estimate.FromUtc.HasValue&&estimate.ToUtc>=now){item.RestockWindowStartUtc=estimate.FromUtc;item.RestockWindowEndUtc=estimate.ToUtc;item.RestockAtUtc=estimate.ToUtc;item.Confidence=estimate.Confidence=="Moderate"?.65:.4;item.ConfidenceLabel=estimate.Confidence+" evidence";item.Note="Independent observed-cycle window; sequence planning conservatively uses its end. Planning weight is not a success probability. Restock quantity is unknown.";}
                }
                all.Add(CalculateTravelOpportunity(item));
            }
        }catch{/* Existing travel feeds remain usable when the local history is unavailable. */}
        return _evidenceTravel=all.GroupBy(x=>(x.Country.ToLowerInvariant(),x.ItemId,x.ItemId>0?"":x.ItemName.ToLowerInvariant())).Select(g=>g.OrderByDescending(x=>x.StockUpdatedAtUtc??DateTime.MinValue).ThenByDescending(x=>x.MarketValue>0).First()).OrderByDescending(x=>x.ProfitPerHour).ToList();
    }
    private object TravelWaitData(){var now=DateTime.UtcNow;DateTime? ready=_travelSituation.State=="outbound"?_travelSituation.ArrivalUtc:now;bool eligible=_travelSituation.State is "outbound" or "abroad";return new{state=_travelSituation.State,location=_travelSituation.Destination,at=ready,options=eligible&&ready.HasValue?TravelWaitAdvisor.Compare(TravelIntelligenceCandidates(),_travelSituation.Destination,ready.Value>now?ready.Value:now,now):new List<TravelWaitOption>(),note="Conditional choices from current observations. Already-spent outbound time is excluded; ticket consumables, mugging risk and selling liquidity are not priced. Recheck after landing."};}
}
