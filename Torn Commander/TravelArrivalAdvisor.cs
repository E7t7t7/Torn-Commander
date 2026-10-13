namespace TornCommander;

// The adviser is advisory. Purchase execution is deliberately never automated.
public sealed class TravelLandingItem
{
    public string Name { get; set; } = "";
    public int Stock { get; set; }
    public long BuyPrice { get; set; }
}

public sealed class TravelLandingSnapshot
{
    public string Country { get; set; } = "";
    public long Cash { get; set; }
    public int Purchased { get; set; }
    public int Capacity { get; set; }
    public DateTime SeenUtc { get; set; }
    public List<TravelLandingItem> Items { get; set; } = new();
}

public sealed class TravelLandingChoice
{
    public string Item { get; set; } = "";
    public int Stock { get; set; }
    public int Units { get; set; }
    public long Cost { get; set; }
    public long NetProfit { get; set; }
    public long ProfitPerUnit { get; set; }
    public string PriceEvidence { get; set; } = "Unknown market price";
    public string RestockText { get; set; } = "No supported restock ETA";
    public string RestockEvidence { get; set; } = "Unknown";
    public int? RestockMinutes { get; set; }
    public bool RestockIsEstimate { get; set; }
    public int? PreviousStock { get; set; }
    public string? ObservedChange { get; set; }
}

public sealed class TravelLandingAdvice
{
    public string Status { get; set; } = "Not scanned";
    public string Action { get; set; } = "Scan shop";
    public string Country { get; set; } = "";
    public long Cash { get; set; }
    public int SlotsLeft { get; set; }
    public string Message { get; set; } = "Open the overseas shop and scan its actual shelf.";
    public string NextCheck { get; set; } = "";
    public DateTime? CheckedUtc { get; set; }
    public List<TravelLandingChoice> Choices { get; set; } = new();
    public List<string> RecentChanges { get; set; } = new();
    public string BasketSummary { get; set; } = "";
    public long BasketNet { get; set; }
    public int BasketUnits { get; set; }
}

public sealed class TravelStockChange
{
    public string Country { get; set; } = "";
    public string Item { get; set; } = "";
    public int Before { get; set; }
    public int After { get; set; }
    public DateTime PreviousScanUtc { get; set; }
    public DateTime DetectedUtc { get; set; }
}

public static class TravelArrivalAdvisor
{
    public static TravelLandingAdvice Evaluate(TravelLandingSnapshot snapshot,
        IEnumerable<TravelForecastItem> quotes, IEnumerable<TravelObservation> history,
        bool applyMarketFee, DateTime nowUtc, IEnumerable<TravelStockChange>? observedChanges = null)
    {
        var result = new TravelLandingAdvice {
            Country = snapshot.Country, Cash = snapshot.Cash,
            SlotsLeft = Math.Max(0, snapshot.Capacity - snapshot.Purchased),
            CheckedUtc = snapshot.SeenUtc,
            Status = "Observed shop"
        };
        var relevant = quotes.Where(x => x.Country.Equals(snapshot.Country, StringComparison.OrdinalIgnoreCase)).ToList();
        var recent = (observedChanges ?? Enumerable.Empty<TravelStockChange>())
            .Where(x => x.Country.Equals(snapshot.Country, StringComparison.OrdinalIgnoreCase)
                && x.DetectedUtc <= nowUtc && x.DetectedUtc > nowUtc.AddMinutes(-15))
            .OrderByDescending(x => x.DetectedUtc).ToList();
        result.RecentChanges = recent.Take(8).Select(x =>
            $"{x.Item}: {x.Before} → {x.After} seen between {x.PreviousScanUtc.ToLocalTime():h:mm:ss tt} and {x.DetectedUtc.ToLocalTime():h:mm:ss tt}")
            .ToList();
        foreach (var row in snapshot.Items.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            var matching = relevant.Where(x => x.ItemName.Equals(row.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var quote = matching.Where(x => x.MarketValue > 0 && x.PricingUpdatedAtUtc.HasValue &&
                    (nowUtc - x.PricingUpdatedAtUtc.Value).TotalMinutes is >= -10 and <= 720)
                .OrderByDescending(x => x.PricingUpdatedAtUtc).FirstOrDefault();
            var forecastQuote = matching.Where(x => x.RestockAtUtc > nowUtc && !x.IsStale)
                .OrderByDescending(x => x.Confidence).FirstOrDefault();
            var unitNet = quote is { MarketValue: > 0 }
                ? (long)Math.Floor(quote.MarketValue * (applyMarketFee ? 0.95m : 1m)) - row.BuyPrice
                : 0L;
            var available = Math.Max(0, Math.Min(result.SlotsLeft, row.Stock));
            var affordable = row.BuyPrice > 0 ? (int)Math.Min(available, Math.Max(0, snapshot.Cash) / row.BuyPrice) : 0;
            var choice = new TravelLandingChoice {
                Item = row.Name, Stock = row.Stock, Units = affordable, Cost = affordable * row.BuyPrice,
                ProfitPerUnit = unitNet,
                NetProfit = unitNet > 0 ? affordable * unitNet : 0,
                PriceEvidence = quote is { MarketValue: > 0 }
                    ? $"Indicative market value ${quote.MarketValue:N0}, not a guaranteed sale"
                    : "No timestamped market valuation within 12 hours; profit unknown"
            };
            var change = recent.FirstOrDefault(x => x.Item.Equals(row.Name, StringComparison.OrdinalIgnoreCase)
                && x.After == row.Stock);
            if (change is not null)
            {
                choice.PreviousStock = change.Before;
                choice.ObservedChange = $"Observed {change.Before} → {change.After} between {change.PreviousScanUtc.ToLocalTime():h:mm:ss tt} and {change.DetectedUtc.ToLocalTime():h:mm:ss tt}";
            }
            // Only forecast items with a valid future timestamp count as a timed forecast.
            if (row.Stock <= 0 && forecastQuote?.RestockAtUtc is DateTime forecast &&
                forecast > nowUtc && forecast <= nowUtc.AddHours(3) &&
                forecastQuote.Confidence >= 0.40 && !forecastQuote.IsStale)
            {
                choice.RestockMinutes = Math.Max(1, (int)Math.Ceiling((forecast - nowUtc).TotalMinutes));
                choice.RestockText = $"Around {forecast.ToLocalTime():h:mm tt} (estimate, ± unknown)";
                choice.RestockEvidence = $"{forecastQuote.Source}; confidence {forecastQuote.Confidence:P0}";
                choice.RestockIsEstimate = true;
            }
            else if (row.Stock <= 0)
            {
                // Twenty-second scans detect changes to Torn's visible shop.
                // This is a *check interval*, not a claimed restock cycle.
                choice.RestockText = "Unknown restock time; monitoring the visible shop";
                choice.RestockEvidence = "Next scan ~20 seconds; use Refresh Torn Shop if Torn's table has not updated";
            }
            else
            {
                choice.RestockText = "Currently shown in Torn";
                choice.RestockEvidence = "Observed shelf; it can sell out";
            }
            result.Choices.Add(choice);
        }
        result.Choices = result.Choices
            .OrderByDescending(x => x.NetProfit)
            .ThenByDescending(x => x.ProfitPerUnit)
            .ThenByDescending(x => x.Stock)
            .Take(30).ToList();
        // Fill capacity across multiple items, rather than assuming a single item
        // can supply the entire plane. Only consider observed stock and positive,
        // non-guaranteed price estimates; never count unknown-value items as profit.
        var basketCash = Math.Max(0, snapshot.Cash);
        var basketSlots = result.SlotsLeft;
        var parts = new List<string>();
        foreach (var candidate in result.Choices.Where(x => x.Stock > 0 && x.ProfitPerUnit > 0 && x.Cost >= 0)
                     .OrderByDescending(x => x.ProfitPerUnit).ThenByDescending(x => x.Stock))
        {
            if (basketSlots <= 0) break;
            var itemPrice = snapshot.Items.FirstOrDefault(x => x.Name.Equals(candidate.Item, StringComparison.OrdinalIgnoreCase))?.BuyPrice ?? 0;
            if (itemPrice <= 0) continue;
            var quantity = (int)Math.Min((long)Math.Min(basketSlots, candidate.Stock), basketCash / itemPrice);
            if (quantity <= 0) continue;
            basketCash -= quantity * itemPrice;
            basketSlots -= quantity;
            result.BasketUnits += quantity;
            result.BasketNet += candidate.ProfitPerUnit * quantity;
            parts.Add($"{quantity} × {candidate.Item}");
        }
        result.BasketSummary = parts.Count > 0 ? string.Join(" + ", parts) : "No affordable, positively valued mix available";
        var winner = result.Choices.FirstOrDefault(x => x.Units > 0 && x.NetProfit > 0);
        long PredictedNet(TravelLandingChoice choice)
        {
            var price = snapshot.Items.FirstOrDefault(x => x.Name.Equals(choice.Item, StringComparison.OrdinalIgnoreCase))?.BuyPrice ?? 0;
            return price > 0 ? Math.Min((long)result.SlotsLeft, Math.Max(0, snapshot.Cash) / price) * choice.ProfitPerUnit : 0;
        }
        var predicted = result.Choices.Where(x => x.Stock == 0 && x.RestockMinutes is > 0 && x.ProfitPerUnit > 0 && PredictedNet(x) > 0)
            .OrderByDescending(PredictedNet).FirstOrDefault();
        if (result.SlotsLeft == 0)
        {
            result.Action = "Return home";
            result.Message = "All carrying slots are filled. Review purchases and return when ready.";
        }
        else if (snapshot.Cash <= 0)
        {
            result.Action = "No cash abroad";
            result.Message = "Torn shows $0 available at this shop. Restocks may appear, but you cannot purchase them without spendable cash abroad. Commander will still show stock changes and alternative items.";
        }
        else if (winner is not null && predicted is not null &&
            predicted.RestockMinutes <= 20 &&
            PredictedNet(predicted) > result.BasketNet * 1.4)
        {
            result.Action = "Consider waiting";
            result.Message = $"The available mixed load has about ${result.BasketNet:N0} estimated net, but a {predicted.Item} restock is estimated in ~{predicted.RestockMinutes}m and might pay more. Neither stock nor timing is guaranteed.";
        }
        else if (winner is not null)
        {
            result.Action = "Buy available stock";
            result.Message = $"Suggested mixed load: {result.BasketSummary} (~${result.BasketNet:N0} indicative net). Calculated only from observed stock and available cash; verify prices before buying.";
        }
        else if (predicted is not null)
        {
            result.Action = "Consider waiting";
            result.Message = $"Nothing profitable is affordable right now. {predicted.Item} is estimated to restock in ~{predicted.RestockMinutes}m, but there is no guaranteed time or quantity.";
        }
        else
        {
            result.Action = "Return or watch stock";
            result.Message = "No profitable, affordable purchase could be verified from the currently visible stock and available price quotes. Wait only if you are comfortable with an unknown restock time.";
        }
        if (snapshot.Items.Count == 0)
        {
            result.Action = "Open overseas shop";
            result.Message = "No readable stock rows were found. Open the foreign shop page and press Scan Again; no purchases have been attempted.";
        }
        var checkAt = nowUtc.AddSeconds(20);
        result.NextCheck = $"Passive visible-shop scan about every 20 seconds while abroad and the Torn shop is open. If Torn hasn't updated its table, press Refresh Torn Shop. Next scan ~{checkAt.ToLocalTime():h:mm:ss tt}. No guaranteed restock countdown.";
        return result;
    }
}
