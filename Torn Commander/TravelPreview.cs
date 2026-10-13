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

public sealed class TravelFeedDiagnostics
{
    public int InputRows { get; set; }
    public int RejectedEconomics { get; set; }
    public int RejectedStockFilter { get; set; }
    public int RejectedConfidence { get; set; }
    public int UnpricedRows { get; set; }
    public int NoUnitsRows { get; set; }
    public int NonPositiveMarginRows { get; set; }

    private static bool EconomicCandidate(TravelForecastItem x) =>
        (x.Units > 0 && x.ProfitPerHour > 0) || (x.Stock is > 0 && x.BuyPrice > 0 && x.MarketValue <= 0);
    private static bool StockCandidate(TravelForecastItem x, AppSettings s) => !s.TravelInStockOnly || x.StockStatus == "In stock";
    private static bool ConfidenceCandidate(TravelForecastItem x, AppSettings s) =>
        (x.MarketValue <= 0 && x.Stock is > 0) || x.StockStatus == "In stock" ||
        s.TravelIncludeLowConfidence || x.Confidence >= s.TravelMinConfidence;
    public static bool Include(TravelForecastItem x, AppSettings s) =>
        EconomicCandidate(x) && StockCandidate(x, s) && ConfidenceCandidate(x, s);
    public static TravelFeedDiagnostics Build(IEnumerable<TravelForecastItem> source, AppSettings s)
    {
        var rows = source.ToList();
        return new() {
            InputRows = rows.Count,
            RejectedEconomics = rows.Count(x => !EconomicCandidate(x)),
            RejectedStockFilter = rows.Count(x => EconomicCandidate(x) && !StockCandidate(x,s)),
            RejectedConfidence = rows.Count(x => EconomicCandidate(x) && StockCandidate(x,s) && !ConfidenceCandidate(x,s)),
            UnpricedRows = rows.Count(x => x.BuyPrice <= 0 || x.MarketValue <= 0),
            NoUnitsRows = rows.Count(x => x.Units <= 0),
            NonPositiveMarginRows = rows.Count(x => x.BuyPrice > 0 && x.MarketValue > 0 && x.ProfitPerItem <= 0)
        };
    }
}

public static class TravelPreviewEngine
{
    public static TravelPreviewReport Build(IEnumerable<TravelForecastItem> source, AppSettings settings,
        DateTime nowUtc, string? feedError = null, TravelFeedDiagnostics? diagnostics = null)
    {
        var rows = source?.ToList() ?? new List<TravelForecastItem>();
        var report = new TravelPreviewReport
        {
            FeedRows = rows.Count,
            PricedRows = rows.Count(x => x.MarketValue > 0 && x.BuyPrice > 0),
            UnpricedRows = rows.Count(x => x.MarketValue <= 0 || x.BuyPrice <= 0),
            PositiveMarginRows = rows.Count(x => x.MarketValue > 0 && x.BuyPrice > 0 && x.ProfitPerItem > 0),
            StaleRows = rows.Count(x => x.IsStale || (x.StockUpdatedAtUtc.HasValue && (nowUtc - x.StockUpdatedAtUtc.Value).TotalMinutes > 30)),
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
            var rawAge = x.StockUpdatedAtUtc.HasValue
                ? (double?)(nowUtc - x.StockUpdatedAtUtc.Value).TotalMinutes : null;
            var age = rawAge is >= -10 ? (int?)Math.Max(0, rawAge.Value) : null;
            string evidence;
            if (x.Stock is > 0 && x.StockUpdatedAtUtc.HasValue && !x.IsStale &&
                rawAge is >= -10 and <= 30)
                evidence = $"Third-party shelf report: {x.Stock:N0} at {age.GetValueOrDefault()}m ago; can sell out before arrival";
            else if (rawAge is < -10)
                evidence = "Future-dated shelf timestamp — cannot establish current availability";
            else if (x.RestockAtUtc > nowUtc)
                evidence = "Modeled restock (unconfirmed) — check shelf before purchasing";
            else if (x.Stock is > 0 && (x.IsStale || rawAge > 30))
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
        if (diagnostics is not null)
            report.Guidance += $" Raw feed: {diagnostics.InputRows} rows; excluded {diagnostics.RejectedEconomics} for quantity/trip economics, {diagnostics.RejectedStockFilter} by in-stock filter, {diagnostics.RejectedConfidence} by confidence filter. Observations: {diagnostics.UnpricedRows} unpriced, {diagnostics.NoUnitsRows} with no purchasable units, {diagnostics.NonPositiveMarginRows} nonpositive unit margins (these observations can overlap). Counts precede deduplication.";
        if (!string.IsNullOrWhiteSpace(feedError))
            report.Guidance += " Live feed warning: " + (feedError.Length > 180 ? feedError[..180] + "…" : feedError);
        return report;
    }
}
