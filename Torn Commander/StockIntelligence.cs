using System.Text.Json;
namespace TornCommander;

internal sealed class StockQuote
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "";
    public decimal Price { get; set; }
    public long Requirement { get; set; }
    public string Benefit { get; set; } = "";
    public bool Passive { get; set; }
}
internal sealed record StockHolding(long Id, long Shares, decimal? Cost, int Increment, bool Available);
internal sealed record StockPricePoint(DateTime At, long Id, decimal Price);
internal sealed class StockTarget
{
    public decimal? Buy { get; set; }
    public decimal? Sell { get; set; }
    public decimal? Payout { get; set; }
    public int Days { get; set; } = 7;
}
internal sealed class StockJournal
{
    public DateTime ScannedUtc { get; set; }
    public List<StockQuote> Quotes { get; set; } = new();
    public List<StockHolding>? Holdings { get; set; }
    public List<StockPricePoint> History { get; set; } = new();
    public Dictionary<long, StockTarget> Targets { get; set; } = new();
    public string Status { get; set; } = "Scan the Torn Stock Exchange to begin.";
}
internal static class StockIntelligence
{
    public static List<StockQuote> ReadQuotes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<StockQuote>();
        foreach(var x in doc.RootElement.GetProperty("stocks").EnumerateArray())
        {
            var b=x.GetProperty("bonus");
            var q=new StockQuote {Id=x.GetProperty("id").GetInt64(),Name=x.GetProperty("name").GetString()??"",
                Symbol=x.GetProperty("acronym").GetString()??"",Price=x.GetProperty("market").GetProperty("price").GetDecimal(),
                Requirement=b.GetProperty("requirement").GetInt64(), Benefit=b.GetProperty("description").GetString()??"",
                Passive=b.GetProperty("passive").GetBoolean()};
            if(q.Id<=0 || q.Price<=0 || q.Requirement<=0 || list.Any(y=>y.Id==q.Id)) throw new InvalidDataException("Invalid stock quote.");
            list.Add(q);
        }
        if(list.Count==0) throw new InvalidDataException("Empty stock exchange response.");
        return list;
    }
    public static List<StockHolding> ReadHoldings(string json)
    {
        using var doc=JsonDocument.Parse(json);
        var list=new List<StockHolding>();
        foreach(var x in doc.RootElement.GetProperty("stocks").EnumerateArray())
        {
            long id=x.GetProperty("id").GetInt64(),shares=x.GetProperty("shares").GetInt64(),sum=0;
            decimal cost=0;
            foreach(var t in x.GetProperty("transactions").EnumerateArray()) {
                long n=t.GetProperty("shares").GetInt64(); decimal price=t.GetProperty("price").GetDecimal();
                if(n<0||price<0) throw new InvalidDataException("Invalid stock position.");
                sum=checked(sum+n);cost+=n*price;
            }
            if(id<=0||shares<0||list.Any(y=>y.Id==id)) throw new InvalidDataException("Invalid stock holding.");
            var bonus=x.GetProperty("bonus");
            list.Add(new(id,shares,sum==shares?cost:null,bonus.GetProperty("increment").GetInt32(),bonus.GetProperty("available").GetBoolean()));
        }
        return list;
    }
    public static decimal? NetProfit(StockHolding? h, decimal price) => h?.Cost is decimal cost ? h.Shares*price*0.999m-cost : null;
    public static void Record(StockJournal j, DateTime now)
    {
        var hour=new DateTime(now.Year,now.Month,now.Day,now.Hour,0,0,DateTimeKind.Utc);
        j.History.RemoveAll(x=>x.At<now.AddDays(-30)||x.At>now.AddMinutes(5));
        foreach(var q in j.Quotes) {
            j.History.RemoveAll(x=>x.Id==q.Id&&x.At==hour);
            j.History.Add(new(hour,q.Id,q.Price));
        }
    }
    public static object Report(StockJournal j)
    {
        return new { at=j.ScannedUtc==default?"Never":j.ScannedUtc.ToString("yyyy-MM-dd HH:mm")+" UTC",
            stale=j.ScannedUtc==default||DateTime.UtcNow-j.ScannedUtc>TimeSpan.FromMinutes(15),status=j.Status,
            holdingsKnown=j.Holdings!=null,
            value=j.Holdings==null||j.Holdings.Any(h=>!j.Quotes.Any(q=>q.Id==h.Id))?(decimal?)null:j.Holdings.Sum(h=>j.Quotes.FirstOrDefault(q=>q.Id==h.Id)?.Price*h.Shares??0),
            rows=j.Quotes.Select(q=>{
                var h=j.Holdings?.FirstOrDefault(x=>x.Id==q.Id);
                var history=j.History.Where(x=>x.Id==q.Id).OrderBy(x=>x.At).ToList();
                var target=j.Targets.GetValueOrDefault(q.Id)??new();
                long remaining=Math.Max(0,q.Requirement-(h?.Shares??0));
                return new { id=q.Id,name=q.Name,symbol=q.Symbol,price=q.Price,benefit=q.Benefit,passive=q.Passive,
                    shares=j.Holdings==null?(long?)null:h?.Shares??0,cost=h?.Cost,netProfit=NetProfit(h,q.Price),
                    blockCost=q.Requirement*q.Price,remainingCost=remaining*q.Price,increment=h?.Increment,available=h?.Available,
                    low=history.Count==0?(decimal?)null:history.Min(x=>x.Price),high=history.Count==0?(decimal?)null:history.Max(x=>x.Price),
                    samples=history.Count,history=history.Select(x=>new {at=x.At.ToString("O"),price=x.Price}).ToList(),
                    target,signal=target.Buy.HasValue&&q.Price<=target.Buy?"BUY TARGET REACHED":target.Sell.HasValue&&q.Price>=target.Sell?"SELL TARGET REACHED":"Watching",
                    annualPayoutYield=!q.Passive&&target.Payout.HasValue&&target.Days>0?target.Payout*365m/target.Days/(q.Requirement*q.Price)*100m:null };
            }).ToList() };
    }
}
