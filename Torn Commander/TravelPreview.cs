namespace TornCommander;

// Informational route comparison only; NEVER a scheduled departure, stock
// guarantee, or claim that the player is currently free to fly from Torn.
public sealed class TravelPreviewRoute
{
    public string Country { get; set; } = "";
    public string Item { get; set; } = "";
    public string Source { get; set; } = "";
    public string StockEvidence { get; set; } = "";
    public int? Stock { get; set; }
    public int? StockAgeMinutes { get; set; }
    public int OneWayMinutes { get; set; }
    public int Units { get; set; }
    public long BuyCost { get; set; }
    public long ScenarioProfit { get; set; }
    public long ProfitPerItem { get; set; }
    public double ProfitPerHour { get; set; }
    public bool StockVerifiedNow { get; set; } // Deliberately always false: third party observations are not verification
}

public sealed class TravelPreviewReport
{
    public int FeedRows { get; set; }
    public int PricedRows { get; set; }
    public int UnpricedRows { get; set; }
    public int PositiveMarginRows { get; set; }
    public int FreshStockRows { get; set; }
    public int ForecastRows { get; set; }
    public int StaleRows { get; set; }
    public int AffordableRows { get; set; }
    public string Guidance { get; set; } = "";
    public List<TravelPreviewRoute> Routes { get; set; } = new();
}

public static class TravelPreviewEngine
{
    public static TravelPreviewReport Build(IEnumerable<TravelForecastItem> source, AppSettings settings,
        DateTime nowUtc, string? feedError = null)
    {
        var rows = source?.ToList() ?? new List<TravelForecastItem>();
        var report = new TravelPreviewReport
        {
            FeedRows = rows.Count,
            PricedRows = rows.Count(x => x.MarketValue > 0 && x.BuyPrice > 0),
            UnpricedRows = rows.Count(x => x.MarketValue <= 0 || x.BuyPrice <= 0),
            PositiveMarginRows = rows.Count(x => x.MarketValue > 0 && x.BuyPrice > 0 && x.ProfitPerItem > 0),
            StaleRows = rows.Count(x => x.IsStale),
            ForecastRows = rows.Count(x => x.RestockAtUtc.HasValue),
            FreshStockRows = rows.Count(x => x.Stock is > 0 && x.StockUpdatedAtUtc.HasValue &&
                !x.IsStale && (nowUtc - x.StockUpdatedAtUtc.Value).TotalMinutes is >= -10 and <= 30)
        };
        var candidates = rows.Where(x => x.ItemId > 0 && x.FlightMinutesOneWay > 0 &&
                x.BuyPrice > 0 && x.MarketValue > 0 && x.ProfitPerItem > 0 &&
                x.TripProfit > 0 && x.Units > 0)
            .OrderByDescending(x => x.ProfitPerHour)
            .ThenByDescending(x => x.TripProfit)
            .ToList();
        report.AffordableRows = candidates.Count;
        report.Routes = candidates.Take(5).Select(x =>
        {
            var age = x.StockUpdatedAtUtc.HasValue
                ? (int?)Math.Max(0, (nowUtc - x.StockUpdatedAtUtc.Value).TotalMinutes) : null;
            string evidence;
            if (x.Stock is > 0 && x.StockUpdatedAtUtc.HasValue && !x.IsStale &&
                age.HasValue && age.Value <= 30)
                evidence = $"Third-party shelf report: {x.Stock:N0} at {age.Value}m ago; can sell out before arrival";
            else if (x.RestockAtUtc > nowUtc)
                evidence = "Modeled restock (unconfirmed) — check shelf before purchasing";
            else if (x.Stock is > 0 && x.IsStale)
                evidence = "Stale shelf observation — not evidence of current availability";
            else if (x.Stock is > 0)
                evidence = "Old or undated stock report — verify before flying";
            else
                evidence = "No confirmed available stock at destination";
            return new TravelPreviewRoute
            {
                Country = x.Country, Item = x.ItemName, Source = x.Source,
                StockEvidence = evidence, Stock = x.Stock, StockAgeMinutes = age,
                OneWayMinutes = x.FlightMinutesOneWay, Units = x.Units,
                BuyCost = x.CashRequired, ScenarioProfit = x.TripProfit,
                ProfitPerItem = x.ProfitPerItem, ProfitPerHour = x.ProfitPerHour
            };
        }).ToList();
        if (report.FeedRows == 0)
            report.Guidance = "No stock observations have loaded. Use Save + Refresh, then inspect the travel feed error. This is a data outage, not a finding that all routes are unprofitable.";
        else if (report.PricedRows == 0)
            report.Guidance = $"Loaded {report.FeedRows} stock records, but none has both purchase and market prices. Income estimates cannot be produced until price enrichment succeeds.";
        else if (report.Routes.Count == 0)
            report.Guidance = $"Loaded {report.FeedRows} records ({report.PricedRows} priced, {report.PositiveMarginRows} with positive unit margin), but none meets current carrying-capacity, budget and trip-profit conditions. Try a new feed check or review filters and flight settings.";
        else
            report.Guidance = $"Showing {report.Routes.Count} candidate routes from {report.FeedRows} feed records; {report.FreshStockRows} have stock observed in the last 30 minutes. These are conditional comparisons, NOT timed bookings or guaranteed profit.";
        if (!string.IsNullOrWhiteSpace(feedError))
            report.Guidance += " Live feed warning: " + (feedError.Length > 180 ? feedError[..180] + "…" : feedError);
        return report;
    }
}
