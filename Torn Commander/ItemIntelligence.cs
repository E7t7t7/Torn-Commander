namespace TornCommander;
internal sealed class ItemWatch
{
    public int ItemId {get;set;}=361;
    public string Location {get;set;}="Switzerland";
    public string Name {get;set;}="Neumune Tablet";
    public bool BackInStock {get;set;}=true;
    public int? BelowQuantity {get;set;}
    public int? AboveQuantity {get;set;}
    public long? PriceBelow {get;set;}
    public long? PriceAbove {get;set;}
    public bool ForecastAlerts {get;set;}
}
internal sealed class ItemIntelligenceSettings
{
    public bool Monitor {get;set;}
    public int PollMinutes {get;set;}=5;
    public int RetentionDays {get;set;}=90;
    public bool MarketQuotes {get;set;}
    public bool UseTornIntel {get;set;}
    public List<ItemWatch> Watches {get;set;}=new(){new()};
}
internal sealed record RestockEvent(DateTime FromUtc,DateTime ToUtc);
internal sealed record RestockEstimate(string State,DateTime? FromUtc,DateTime? ToUtc,string Confidence,int Events,int Intervals,int Evaluations,double? MeanAbsoluteErrorMinutes,double? WindowOverlapPercent,IReadOnlyList<RestockEvent> History);
internal static class RestockEstimator
{
    public static RestockEstimate Analyze(IEnumerable<ItemObservation> input,DateTime now)
    {
        var rows=input.Where(x=>x.Kind=="foreign-shop"&&x.ObservedUtc<=now&&x.Quantity>=0).OrderBy(x=>x.ObservedUtc).ToList();
        if(rows.Select(x=>(x.Source,x.Location,x.ItemId)).Distinct().Count()>1)throw new InvalidDataException("Estimate requires one source, item and location.");
        var events=new List<RestockEvent>();var segments=new List<List<RestockEvent>>{new()};
        for(int i=1;i<rows.Count;i++){
            if(rows[i].ObservedUtc-rows[i-1].ObservedUtc>TimeSpan.FromMinutes(20)){segments.Add(new());continue;}
            if(rows[i-1].Quantity==0&&rows[i].Quantity>0){var e=new RestockEvent(rows[i-1].ObservedUtc,rows[i].ObservedUtc);events.Add(e);segments[^1].Add(e);}
        }
        var intervals=new List<double>();int evaluations=0,hits=0;double errors=0;
        foreach(var segment in segments){var samples=new List<double>();for(int i=1;i<segment.Count;i++){
            var gap=(Mid(segment[i])-Mid(segment[i-1])).TotalMinutes;
            if(samples.Count>=4){var window=Window(samples,segment[i-1]);evaluations++;errors+=Math.Abs((Mid(segment[i])-(window.Item1+(window.Item2-window.Item1)/2)).TotalMinutes);if(segment[i].ToUtc>=window.Item1&&segment[i].FromUtc<=window.Item2)hits++;}
            samples.Add(gap);
        }if(ReferenceEquals(segment,segments[^1]))intervals=samples;}
        string state="Insufficient history";DateTime? from=null,to=null;string confidence="Insufficient";
        if(rows.Count>0&&now-rows[^1].ObservedUtc>TimeSpan.FromMinutes(20))state="Stale observations — estimate withheld";
        else if(rows.Count>0&&rows[^1].Quantity>0)state="Observed in stock";
        else if(intervals.Count>=4&&segments[^1].Count>0){var window=Window(intervals,segments[^1][^1]);from=window.Item1;to=window.Item2;state=to<now?"Window passed — awaiting new evidence":"Estimated window";confidence=intervals.Count>=10&&evaluations>=5&&hits/(double)evaluations>=.7?"Moderate":"Low";}
        return new(state,from,to,confidence,events.Count,intervals.Count,evaluations,evaluations>0?Math.Round(errors/evaluations,1):null,evaluations>0?Math.Round(100d*hits/evaluations,1):null,events.TakeLast(30).ToList());
    }
    private static DateTime Mid(RestockEvent e)=>e.FromUtc+(e.ToUtc-e.FromUtc)/2;
    private static (DateTime,DateTime) Window(List<double> samples,RestockEvent last){var s=samples.OrderBy(x=>x).ToArray();double low=s[(int)Math.Floor((s.Length-1)*.1)],high=s[(int)Math.Ceiling((s.Length-1)*.9)];double margin=Math.Max(5,(high-low)*.25);return(last.FromUtc.AddMinutes(Math.Max(1,low-margin)),last.ToUtc.AddMinutes(high+margin));}
}
internal static class ItemAlertRules
{
    public static List<string> Crossings(ItemWatch w,ItemObservation? before,ItemObservation after,DateTime now)
    {
        var result=new List<string>();if(before is null||before.Source!=after.Source||before.Location!=after.Location||before.ItemId!=after.ItemId||after.ObservedUtc<=before.ObservedUtc||now-after.ObservedUtc>TimeSpan.FromMinutes(20))return result;
        if(w.BackInStock&&before.Quantity==0&&after.Quantity>0)result.Add("Back in stock: "+after.Quantity);
        if(w.BelowQuantity is int low&&before.Quantity>=low&&after.Quantity<low)result.Add("Stock fell below "+low);
        if(w.AboveQuantity is int high&&before.Quantity<=high&&after.Quantity>high)result.Add("Stock rose above "+high);
        if(w.PriceBelow is long cheap&&before.MarketValue>=cheap&&after.MarketValue<cheap)result.Add("Market value fell below $"+cheap);
        if(w.PriceAbove is long dear&&before.MarketValue<=dear&&after.MarketValue>dear)result.Add("Market value rose above $"+dear);
        return result;
    }
}
