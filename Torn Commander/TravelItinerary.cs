namespace TornCommander;

// Rolling 24-hour model starting in Torn. No travel is booked, and future stock
// is a scenario based on public observations rather than a guarantee.
public sealed class TravelItineraryLeg
{
    public int Number { get; set; }
    public string Country { get; set; } = "";
    public string Item { get; set; } = "";
    public int Units { get; set; }
    public DateTime DepartUtc { get; set; }
    public DateTime ArriveUtc { get; set; }
    public DateTime ReturnUtc { get; set; }
    public long CashRequired { get; set; }
    public long EstimatedProfit { get; set; }
    public string Availability { get; set; } = "Projected";
    public double PlanningConfidence { get; set; }
    public long RiskAdjustedProfit { get; set; }
}

public sealed class TravelItineraryPlan
{
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public long EstimatedProfit { get; set; }
    public double RiskWeightedScore { get; set; }
    public int ProjectedLegCount { get; set; }
    public string Note { get; set; } = "";
    public List<TravelItineraryLeg> Legs { get; set; } = new();
}

public static class TravelItineraryEngine
{
    private const int HorizonMinutes = 24 * 60;
    private const int SlotMinutes = 15;
    private const int Beam = 14;
    private sealed class State
    {
        public int Slot;
        public double Score;
        public long Profit;
        public List<TravelItineraryLeg> Legs = new();
        public Dictionary<string, int> Used = new(StringComparer.Ordinal);
    }

    // Beam search maximizes the sum of risk-adjusted trip profits across the
    // whole horizon, not the profit/hour of just the first departure.
    public static TravelItineraryPlan Build(IEnumerable<TravelForecastItem> source, AppSettings settings,
        DateTime startUtc, DateTime nowUtc, DateTime? horizonEndUtc = null)
    {
        startUtc = startUtc.Kind == DateTimeKind.Utc ? startUtc : startUtc.ToUniversalTime();
        var plan = new TravelItineraryPlan
        {
            GeneratedUtc = nowUtc, StartUtc = startUtc, EndUtc = horizonEndUtc ?? startUtc.AddHours(24)
        };
        if (startUtc < nowUtc.AddMinutes(-2) || startUtc > nowUtc.AddHours(48))
        {
            plan.Note = "The departure time must be now or within the next 48 hours.";
            return plan;
        }
        // A flight already in progress consumes part of the user's next 24 hours.
        // Never plan trips that would return after the rolling deadline.
        var availableMinutes = Math.Clamp((int)(plan.EndUtc - startUtc).TotalMinutes, 0, HorizonMinutes);
        if (availableMinutes < 20) { plan.Note = "Too little time remains for another round trip today."; return plan; }
        var candidates = source.Where(x => x.ItemId > 0 && x.FlightMinutesOneWay > 0 &&
                x.ProfitPerItem > 0 && x.BuyPrice > 0 && x.MarketValue > 0 &&
                // Old stock is not an inventory guarantee and must have a real timestamp.
                ((x.Stock is > 0 && x.StockUpdatedAtUtc.HasValue) || x.RestockAtUtc.HasValue))
            .Where(x => !settings.TravelInStockOnly || x.Stock is > 0)
            .OrderByDescending(x => x.ProfitPerHour)
            .ThenByDescending(x => x.TripProfit)
            .Take(32).ToList();
        if (candidates.Count == 0)
        {
            plan.Note = "No profitable, price-known routes are available. Refresh the stock feeds; unknown prices are never fabricated.";
            return plan;
        }
        var states = Enumerable.Range(0, availableMinutes / SlotMinutes + 1)
            .Select(_ => new List<State>()).ToArray();
        states[0].Add(new State());
        State best = states[0][0];
        var transfer = Math.Clamp(settings.TravelTransferMinutes, 0, 120);
        for (var slot = 0; slot < states.Length; slot++)
        {
            foreach (var state in states[slot].ToList())
            {
                if (state.Score > best.Score) best = state;
                if (slot + 1 < states.Length) Offer(states[slot + 1], state, slot + 1);
                var depart = startUtc.AddMinutes(slot * SlotMinutes);
                foreach (var item in candidates)
                {
                    var roundtrip = item.FlightMinutesOneWay * 2;
                    var returnMinute = slot * SlotMinutes + roundtrip;
                    if (returnMinute > availableMinutes) continue;
                    var arrive = depart.AddMinutes(item.FlightMinutesOneWay);
                    var stockAge = item.StockUpdatedAtUtc.HasValue
                        ? (nowUtc - item.StockUpdatedAtUtc.Value).TotalMinutes : double.PositiveInfinity;
                    // One observation can support a near-term candidate, not a whole day of
                    // repeated trips. All later legs need separate restock timing evidence.
                    var observed = item.Stock is > 0 && state.Legs.Count == 0 &&
                        stockAge >= -10 && stockAge <= 60;
                    var restockProjection = item.RestockAtUtc.HasValue &&
                        item.Confidence >= settings.TravelMinConfidence &&
                        arrive >= item.RestockAtUtc.Value.AddMinutes(-10) &&
                        arrive <= item.RestockAtUtc.Value.AddMinutes(60);
                    if (!observed && !restockProjection) continue;
                    // No model may reuse a static shelf reading as a new replenishment.
                    if (observed && arrive < nowUtc) continue;
                    var key = item.Country.ToUpperInvariant() + ":" + item.ItemId;
                    var used = state.Used.TryGetValue(key, out var count) ? count : 0;
                    var totalAvailable = observed ? Math.Max(0, item.Stock!.Value) : settings.TravelCapacity;
                    var remaining = totalAvailable - used;
                    if (remaining <= 0) continue;
                    var funds = settings.TravelCashBudget > 0 ? settings.TravelCashBudget / item.BuyPrice : 1000;
                    var units = (int)Math.Clamp(Math.Min(Math.Min((long)settings.TravelCapacity, funds), remaining), 0, 1000);
                    if (units == 0) continue;
                    var standardFare = settings.TravelFlightType == "Standard" ?
                        Math.Max(0L, item.ProfitPerItem * item.Units - item.TripProfit) : 0L;
                    var profit = item.ProfitPerItem * units - standardFare;
                    if (profit <= 0) continue;
                    // Time-to-arrival reduces observed-stock confidence sharply. A full
                    // airplane trip can outlast a shelf snapshot; never label it certain.
                    var minutesSinceReportAtArrival = item.StockUpdatedAtUtc.HasValue
                        ? Math.Max(0, (arrive - item.StockUpdatedAtUtc.Value).TotalMinutes) : double.PositiveInfinity;
                    var certainty = observed
                        ? Math.Clamp(0.75 * Math.Exp(-minutesSinceReportAtArrival / 150.0), 0.08, 0.72)
                        : Math.Clamp(item.Confidence, 0, 1) * 0.40;
                    // Model confidence is a ranking heuristic, not a calibrated probability.
                    if (!double.IsFinite(certainty)) continue;
                    var next = new State
                    {
                        Score = state.Score + profit * certainty,
                        Profit = state.Profit + profit,
                        Used = new Dictionary<string, int>(state.Used, StringComparer.Ordinal),
                        Legs = new List<TravelItineraryLeg>(state.Legs)
                    };
                    next.Used[key] = used + units;
                    next.Legs.Add(new TravelItineraryLeg
                    {
                        Number = next.Legs.Count + 1, Country = item.Country, Item = item.ItemName,
                        Units = units, DepartUtc = depart, ArriveUtc = arrive,
                        ReturnUtc = depart.AddMinutes(roundtrip), CashRequired = units * item.BuyPrice,
                        EstimatedProfit = profit,
                        RiskAdjustedProfit = (long)Math.Round(profit * certainty),
                        Availability = observed ? "Stock observed earlier — verify on arrival" : "Modeled restock — speculative",
                        PlanningConfidence = certainty
                    });
                    var nextMinutes = returnMinute + transfer;
                    // Departure after turnaround is rounded up to the next 15-minute slot.
                    next.Slot = (int)Math.Ceiling(nextMinutes / (double)SlotMinutes);
                    if (next.Score > best.Score) best = next;
                    if (next.Slot < states.Length) Offer(states[next.Slot], next, next.Slot);
                }
            }
        }
        plan.Legs = best.Legs;
        plan.EstimatedProfit = best.Profit;
        plan.RiskWeightedScore = Math.Round(best.Score);
        plan.ProjectedLegCount = plan.Legs.Count(x => x.Availability.StartsWith("Modeled", StringComparison.Ordinal));
        plan.Note = plan.Legs.Count == 0
            ? "No supported profitable sequence right now. A stale shelf is not evidence of future stock; wait for fresh observations or a learned restock window."
            : "Scenario upside is NOT a profit forecast. The risk-weighted value is a conservative ranking heuristic, not a calibrated expected payout. Confirm stock, selling prices and cash before each trip."
              + (settings.TravelCashBudget == 0 ? " Cash budget is unlimited in settings: confirm you can actually afford these purchases." : "")
              + (plan.ProjectedLegCount > 0 ? " Later trips use speculative replenishment timing." : "");
        return plan;
    }

    private static void Offer(List<State> bucket, State candidate, int slot)
    {
        // Avoid identical schedules crowding out distinct alternatives.
        var signature = string.Join("|", candidate.Legs.Select(x => x.Country + ":" + x.Item + ":" + x.DepartUtc.Ticks));
        if (bucket.Any(x => x.Legs.Count == candidate.Legs.Count &&
                string.Join("|", x.Legs.Select(y => y.Country + ":" + y.Item + ":" + y.DepartUtc.Ticks)) == signature))
            return;
        var copy = new State { Slot = slot, Score = candidate.Score, Profit = candidate.Profit,
            Used = candidate.Used, Legs = candidate.Legs };
        bucket.Add(copy);
        bucket.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (bucket.Count > Beam) bucket.RemoveRange(Beam, bucket.Count - Beam);
    }
}
