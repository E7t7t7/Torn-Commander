namespace TornCommander;
public sealed class ProgressionJournal
{
    public int TrainingPercent { get; set; } = 70;
    public long CashReserve { get; set; } = 100000;
    public long DailyTradingTarget { get; set; } = 500000;
    public Dictionary<string, HashSet<string>> Done { get; set; } = new();
    public HashSet<string> ForDay(DateTime localDay)
    {
        Done ??= new();
        var key = localDay.ToString("yyyy-MM-dd");
        if (!Done.TryGetValue(key, out var day) || day is null) Done[key] = day = new();
        return day;
    }
}
public sealed record ProgressionStep(string Id, string Title, string Detail, int Priority, bool Done);
public sealed class ProgressionReport
{
    public string Day { get; set; } = "";
    public string Note { get; set; } = "";
    public int? TrainingEnergy { get; set; }
    public int? FlexibleEnergy { get; set; }
    public long? SpendableCash { get; set; }
    public decimal RecordedMargin { get; set; }
    public long Target { get; set; }
    public int Trades { get; set; }
    public int UnknownFeeTrades { get; set; }
    public int LegacyTradesExcluded { get; set; }
    public List<ProgressionStep> Steps { get; set; } = new();
}
public static class ProgressionAdvisor
{
    public static readonly string[] TaskIds = ["training","education","cash","nerve"];
    public static ProgressionReport Build(TornSnapshot s, RecommendationResult coach, ProgressionJournal goals,
        string profile, TravelSituation situation, IEnumerable<TradeEntry> trades, IEnumerable<TravelForecastItem> routes, DateTime nowUtc, DateTime localDay)
    {
        var report = new ProgressionReport { Day = localDay.ToString("yyyy-MM-dd"), Target = Math.Max(1, goals.DailyTradingTarget),
            Note = "Cash while growing: train your planned energy first, keep education running, then build income through travel or trading. Checked tasks are your own records, not verified game actions." };
        var done = goals.ForDay(localDay);
        void Add(string id,string title,string detail,int priority) => report.Steps.Add(new(id,title,detail,priority,done.Contains(id)));
        var fastAge = s.FastRetrievedAt == DateTime.MinValue ? double.PositiveInfinity : (nowUtc-s.FastRetrievedAt.ToUniversalTime()).TotalMinutes;
        var fullAge = s.FullRetrievedAt == DateTime.MinValue ? double.PositiveInfinity : (nowUtc-s.FullRetrievedAt.ToUniversalTime()).TotalMinutes;
        var barsFresh = s.RawJson.ContainsKey("bars") && fastAge is >= -1 and <= 10 && !s.Errors.Any(x=>x.StartsWith("bars:",StringComparison.Ordinal));
        if (barsFresh && s.CurrentEnergy is >= 0)
        {
            report.TrainingEnergy = (int)Math.Ceiling(s.CurrentEnergy.Value * Math.Clamp(goals.TrainingPercent,0,100) / 100m);
            report.FlexibleEnergy = s.CurrentEnergy.Value - report.TrainingEnergy;
        }
        var home = situation.State == "home";
        Add("training",home ? "Train before selling more losses" : "Plan training after returning to Torn",
            report.TrainingEnergy is { } energy
            ? $"Plan {energy} of your last-synced {s.CurrentEnergy} energy for the gym; {report.FlexibleEnergy} remains flexible. This is a planning allocation: train it before starting losses."
            : "Energy is missing or stale. Fast Sync before choosing a training allocation; no energy amount is assumed.",
            home && barsFresh && s.MaxEnergy is > 0 && s.CurrentEnergy >= s.MaxEnergy ? 0 : 20);
        var eduKnown = s.EducationDataKnown && fullAge is >= -1 and <= 30 && !s.Errors.Any(x=>x.StartsWith("education:",StringComparison.Ordinal));
        var edu = !eduKnown ? "Education status needs a successful Full Sync; missing data does not mean no course." :
            s.CurrentEducationName is null ? "No course was reported at the last sync. Open Education and choose the next course for your goals." :
            s.EducationEndsUtc <= nowUtc ? "The recorded course end time has passed. Full Sync to confirm completion and check your next course." :
            s.EducationEndsUtc is { } end ? $"{s.CurrentEducationName} was recorded running until {end.ToLocalTime():g}. Keep it moving; confirm the course in Torn." :
            $"{s.CurrentEducationName} is recorded, but its completion time is unknown.";
        Add("education","Keep education moving",edu,eduKnown && (s.CurrentEducationName is null || s.EducationEndsUtc<=nowUtc) ? 5 : 30);
        var todays = trades.Where(x=>(x.Date.Kind==DateTimeKind.Utc ? x.Date.ToLocalTime().Date : x.Date.Date)==localDay.Date && x.Quantity>0 && x.BuyEach>=0 && x.SellEach>0).ToList();
        var owned = todays.Where(x=>string.Equals(x.ProfileId,profile,StringComparison.OrdinalIgnoreCase)).ToList();
        report.RecordedMargin = owned.Sum(x=>x.ProfitAfterFees??0);
        report.UnknownFeeTrades=owned.Count(x=>!x.SellingFees.HasValue);
        report.Trades=owned.Count;report.LegacyTradesExcluded=todays.Count(x=>string.IsNullOrWhiteSpace(x.ProfileId));
        var cashFresh = s.RawJson.ContainsKey("money") && fullAge is >= -1 and <= 30 && s.Wallet is >= 0 &&
            !s.Errors.Any(x=>x.StartsWith("money:",StringComparison.Ordinal));
        if(cashFresh) report.SpendableCash=Math.Max(0,s.Wallet!.Value-Math.Max(0,goals.CashReserve));
        string cash;
        if (situation.State is "outbound" or "returning") cash="Follow the current flight before considering another trip. Use Travel Planner for landing and return timing.";
        else if (situation.State=="abroad") cash="Check the actual shop after landing, then compare available stock with your cash and return plan.";
        else if (!home) cash="Your location is not confirmed or prevents travel. Sync status before planning a departure.";
        else if (s.Level is null) cash="Player level is unknown. Sync before choosing a travel income route.";
        else if (s.Level<15) cash="Travel unlocks at level 15. Build toward that access and log completed trades while keeping your training plan.";
        else if (report.SpendableCash is null) cash="Wallet data is missing or stale. Full Sync before choosing an affordable buying budget.";
        else if (report.SpendableCash==0) cash="The last wallet balance does not exceed your cash reserve. Do not plan stock purchases against protected cash.";
        else
        {
            var route=routes.Where(x=>x.BuyPrice>0 && x.MarketValue>0 && x.ProfitPerItem>0 && !x.IsStale &&
                    x.Stock is > 0 && x.StockUpdatedAtUtc.HasValue && (nowUtc-x.StockUpdatedAtUtc.Value).TotalMinutes is >= -10 and <= 30 &&
                    x.PricingUpdatedAtUtc.HasValue && (nowUtc-x.PricingUpdatedAtUtc.Value).TotalMinutes is >= -10 and <= 720 &&
                    x.CashRequired>0 && x.CashRequired<=report.SpendableCash)
                .OrderByDescending(x=>x.ProfitPerHour).FirstOrDefault();
            cash=route is null ? "No fresh, affordable route supports a purchase suggestion. Refresh Travel and compare your reserve-adjusted budget before committing." :
                $"Compare {route.Country} / {route.ItemName}: modeled {route.Units} units, cost USD {route.CashRequired:N0}, possible trip margin USD {route.TripProfit:N0}. Quotes and stock can change; verify before flying or buying.";
        }
        Add("cash","Build income beyond loss selling",cash,10);
        Add("nerve","Spend nerve with the crime coach",
            barsFresh && s.CurrentNerve.HasValue ? $"Last-synced nerve: {s.CurrentNerve}/{s.MaxNerve}. {coach.NerveAdvice}" :
            "Nerve data is missing or stale. Sync before following the crime coach.",home && barsFresh && s.MaxNerve is > 0 && s.CurrentNerve>=s.MaxNerve ? 1 : 40);
        report.Steps=report.Steps.OrderBy(x=>x.Done).ThenBy(x=>x.Priority).ToList();
        return report;
    }
}
