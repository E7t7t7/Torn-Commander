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
}

public sealed class TravelItineraryPlan
{
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public long EstimatedProfit { get; set; }
    public double RiskWeightedScore { get; set; }
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
                (x.Stock is > 0 || x.RestockAtUtc.HasValue))
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
                    var observed = item.Stock is > 0;
                    // Do not build an itinerary from already-obsolete stock reports.
                    var stockAge = item.StockUpdatedAtUtc.HasValue
                        ? (nowUtc - item.StockUpdatedAtUtc.Value).TotalMinutes : double.PositiveInfinity;
                    if (observed && (stockAge < -10 || stockAge > 180)) continue;
                    if (!observed && (!item.RestockAtUtc.HasValue || item.Confidence < settings.TravelMinConfidence ||
                        arrive < item.RestockAtUtc.Value.AddMinutes(-10) ||
                        arrive > item.RestockAtUtc.Value.AddMinutes(75))) continue;
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
                    var elapsed = (arrive - nowUtc).TotalMinutes;
                    var freshnessAtArrival = observed && item.StockUpdatedAtUtc.HasValue &&
                        (arrive - item.StockUpdatedAtUtc.Value).TotalMinutes <= 45;
                    var certainty = freshnessAtArrival ? 0.85 : observed
                        ? Math.Max(0.22, 0.65 - Math.Max(0, elapsed - 45) / 2400.0)
                        : Math.Clamp(item.Confidence, 0.2, 0.85) * 0.55;
                    // Late-day opportunities remain visible, but are penalized for uncertainty.
                    if (observed && elapsed > 180) certainty *= 0.75;
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
                        Availability = freshnessAtArrival ? "Recent report (verify)" : "Projected — verify again",
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
        plan.Note = plan.Legs.Count == 0
            ? "No complete profitable round trip fits the chosen 24-hour window. Try another flight method or await fresh stock."
            : "Best modeled sequence found (beam-search estimate, not guaranteed maximum). Later legs assume stock remains available; recheck before every departure.";
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
