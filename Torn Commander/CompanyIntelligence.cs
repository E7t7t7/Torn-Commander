using System.Text.Json;

namespace TornCommander;

internal sealed class CompanyObservation
{
    public DateTime ObservedUtc { get; set; } = DateTime.UtcNow;
    public string DayUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public long CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public string CompanyType { get; set; } = "";
    public long CompanyTypeId { get; set; }
    public int? Efficiency { get; set; }
    public int? Environment { get; set; }
    public int? Popularity { get; set; }
    public int? Trains { get; set; }
    public int? Capacity { get; set; }
    public long? Funds { get; set; }
    public int? Rating { get; set; }
    public int? Employees { get; set; }
    public long? WeeklyIncome { get; set; }
    public long? DailyIncome { get; set; }
    public int? DailyCustomers { get; set; }
    public long? DailyAdvertising { get; set; }
    public long? DailyPayroll { get; set; }
    public long? DailyStockCost { get; set; }
    public string Notes { get; set; } = "";
    public bool FromApi { get; set; }
    public long? FullCostMargin => DailyAdvertising.HasValue && DailyPayroll.HasValue && DailyStockCost.HasValue
        ? DailyIncome - DailyAdvertising.Value - DailyPayroll.Value - DailyStockCost.Value : null;
    public long? AfterAdvertising => DailyAdvertising.HasValue ? DailyIncome - DailyAdvertising.Value : null;
}
internal sealed class CompanyJournal
{
    public CompanyAdTrial? AdTrial { get; set; }
    public bool AutoCollectEnabled { get; set; }
    public long ActiveCompanyId { get; set; }
    public string Status { get; set; } = "Connect a Torn API key and sync your company to begin collecting daily results.";
    public List<CompanyObservation> Days { get; set; } = new();
    public CompanyMarketScan? Market { get; set; }
    public CompanyTeamScan? Team { get; set; }
}
internal sealed record CompanyRecommendation(string State, string Message, long SuggestedBudget, int SampleDays);
internal static class CompanyIntelligence
{
    public static long? CompanyCost(JsonElement request, string field, long? prior)
    {
        if (!request.TryGetProperty(field, out var x)) return prior;
        if (x.ValueKind == JsonValueKind.Null) return null;
        if (x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out var n) && n >= 0 && n <= 100000000000L)
            return n;
        throw new InvalidDataException(field + " must be a nonnegative number or blank.");
    }
    public static bool CollectionDue(CompanyJournal journal, DateTime nowUtc) =>
        journal.AutoCollectEnabled && !journal.Days.Any(x => x.FromApi && x.CompanyId == journal.ActiveCompanyId && x.DayUtc == nowUtc.ToString("yyyy-MM-dd"));

    private static long Long(JsonElement x, string field)
    {
        if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty(field, out var y)) return 0;
        return y.ValueKind == JsonValueKind.Number && y.TryGetInt64(out var n) ? n : 0;
    }
    private static long? OptionalLong(JsonElement x, string field) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(field, out var y) &&
        y.ValueKind == JsonValueKind.Number && y.TryGetInt64(out var n) && n >= 0 ? n : null;
    private static int? OptionalInt(JsonElement x, string field)
    {
        var value = OptionalLong(x, field);
        return value is >= 0 and <= int.MaxValue ? (int?)value.Value : null;
    }
    private static string Text(JsonElement x, string field) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(field, out var y) &&
        y.ValueKind == JsonValueKind.String ? y.GetString() ?? "" : "";

    public static CompanyObservation ReadProfile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("profile", out var p) || p.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Company API response has no profile object.");
        var id = Long(p, "id");
        if (id <= 0) throw new InvalidDataException("Company ID missing: no record was saved.");
        return new CompanyObservation
        {
            CompanyId = id,
            CompanyName = Text(p, "name"),
            CompanyType = p.TryGetProperty("type", out var t) ? Text(t, "name") : "",
            CompanyTypeId = p.TryGetProperty("type", out var type) ? Long(type, "id") : 0,
            Efficiency = OptionalInt(p, "efficiency"), Environment = OptionalInt(p, "environment"),
            Popularity = OptionalInt(p, "popularity"), Trains = OptionalInt(p, "trains"),
            Funds = OptionalLong(p, "funds"),
            Capacity = p.TryGetProperty("employees", out var capacity) ? OptionalInt(capacity, "capacity") : null,
            Rating = OptionalInt(p, "rating"),
            Employees = p.TryGetProperty("employees", out var e) ? OptionalInt(e, "hired") : null,
            WeeklyIncome = p.TryGetProperty("income", out var weekly) ? OptionalLong(weekly, "weekly") : null,
            DailyIncome = p.TryGetProperty("income", out var income) ? OptionalLong(income, "daily") : null,
            DailyCustomers = p.TryGetProperty("customers", out var cust) ? OptionalInt(cust, "daily") : null,
            DailyAdvertising = OptionalLong(p, "advertisement_budget"),
            FromApi = true,
            ObservedUtc = DateTime.UtcNow,
            DayUtc = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };
    }

    public static void Upsert(CompanyJournal journal, CompanyObservation fresh)
    {
        var previous = journal.Days.FirstOrDefault(x => x.CompanyId == fresh.CompanyId && x.DayUtc == fresh.DayUtc);
        if (previous != null)
        {
            fresh.DailyAdvertising ??= previous.DailyAdvertising;
            fresh.DailyPayroll = previous.DailyPayroll;
            fresh.DailyStockCost = previous.DailyStockCost;
            fresh.Notes = previous.Notes;
            journal.Days.Remove(previous);
        }
        journal.Days.Add(fresh);
        journal.ActiveCompanyId = fresh.CompanyId;
        journal.Days = journal.Days.OrderByDescending(x => x.DayUtc, StringComparer.Ordinal).Take(365).ToList();
    }

    public static CompanyBusinessReport BusinessReport(IReadOnlyList<CompanyObservation> days)
    {
        var latest = days.OrderByDescending(x => x.DayUtc, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null) return new("No observation", "Sync Company to begin.", null, null, null, null, null, null, false);
        var knownCosts = (latest.DailyAdvertising ?? 0) + (latest.DailyPayroll ?? 0) + (latest.DailyStockCost ?? 0);
        var complete = latest.DailyAdvertising.HasValue && latest.DailyPayroll.HasValue && latest.DailyStockCost.HasValue;
        var revenuePerCustomer = latest.DailyIncome.HasValue && latest.DailyCustomers is > 0
            ? (decimal?)latest.DailyIncome.Value / latest.DailyCustomers.Value : null;
        var adsPerCustomer = latest.DailyAdvertising.HasValue && latest.DailyCustomers is > 0
            ? (decimal?)latest.DailyAdvertising.Value / latest.DailyCustomers.Value : null;
        var previous = days.Where(x => x.CompanyId == latest.CompanyId && x.DayUtc != latest.DayUtc && x.DailyIncome.HasValue)
            .OrderByDescending(x => x.DayUtc, StringComparer.Ordinal).FirstOrDefault();
        long? incomeChange = latest.DailyIncome.HasValue && previous is not null
            ? latest.DailyIncome.Value - previous.DailyIncome!.Value : null;
        long? balance = latest.DailyIncome.HasValue ? latest.DailyIncome.Value - knownCosts : null;
        var state = balance < 0 ? "SPENDING EXCEEDS INCOME" : !complete ? "COSTS INCOMPLETE" : "ACCOUNTED MARGIN";
        var note = balance < 0
            ? $"Reported income is USD {-balance:N0} below recorded costs before any missing expenses. Verify matching reporting periods and review spending before treating the current budget as sustainable."
            : complete ? "All three entered costs are included. This is an observed accounting balance, not a prediction of profit."
            : "Enter missing payroll and supplies to assess the business; blank costs remain unknown.";
        if (revenuePerCustomer is > 0 && adsPerCustomer > revenuePerCustomer)
            note += " Advertising per customer exceeds reported revenue per customer.";
        if (previous is not null)
            note += $" Income comparison uses {previous.DayUtc}; staffing was " + (latest.Employees.HasValue && previous.Employees.HasValue
                ? latest.Employees == previous.Employees ? "unchanged." : "different, so advertising alone cannot explain the change."
                : "not fully recorded.");
        return new(state, note, knownCosts, balance, revenuePerCustomer, adsPerCustomer,
            revenuePerCustomer is > 0 ? (long?)Math.Ceiling(knownCosts / revenuePerCustomer.Value) : null, incomeChange, complete);
    }

    public static CompanyRecommendation Analyze(IReadOnlyList<CompanyObservation> days)
    {
        var points = days.Where(x => x.FromApi && x.DailyAdvertising.HasValue && x.DailyIncome.HasValue)
            .OrderBy(x => x.DayUtc, StringComparer.Ordinal).ToList();
        if (points.Count == 0)
            return new("NO BASELINE", "Sync a Limited or Full Access company profile, or enter advertising manually. No budget recommendation yet.", 0, 0);
        var latest = points[^1];
        var currentBudget = latest.DailyAdvertising ?? 0;
        if (latest.DailyIncome < currentBudget)
            return new("SPENDING ALERT", $"Reported income is USD {currentBudget - latest.DailyIncome:N0} below advertising alone. Check that both values cover the same reporting period and review affordability. The current budget is not a recommended spending level; collect comparable complete days before optimizing it.", 0, points.Count);
        if (points.Count < 7)
            return new("BASELINE", $"Collected {points.Count}/7 daily records with advertising costs. Hold the budget steady while building history.", currentBudget, points.Count);
        if (points.Any(x => !x.Employees.HasValue))
            return new("INCOMPLETE", "Employee counts are missing; collect comparable staffing observations before judging advertising budgets.", currentBudget, points.Count);
        var step = Math.Max(100L, (long)Math.Round(Math.Max(1L, currentBudget) * 0.10));
        var groups = points.GroupBy(x => (long)Math.Round((double)x.DailyAdvertising!.Value / step) * step)
            .Where(g => g.Count() >= 3).Select(g => new {
                Budget = g.Key,
                Count = g.Count(),
                Mean = g.Average(x => x.DailyIncome!.Value - x.DailyAdvertising!.Value),
                StaffSpread = g.Max(x => x.Employees) - g.Min(x => x.Employees)
            }).OrderByDescending(g => g.Mean).ToList();
        if (groups.Count < 2)
            return new("TEST NEXT", $"Collected {points.Count} records but fewer than 3 results at two distinct budget levels. Test roughly 10% above or below the current daily budget for at least 3 complete reporting days, holding staffing and prices steady.", currentBudget, points.Count);
        if (groups.Any(g => g.StaffSpread > 1))
            return new("CONFOUNDED", "Employee count changed within an advertising test group. Keep staffing stable before attributing a revenue change to advertising.", currentBudget, points.Count);
        var best = groups[0];
        var near = groups.OrderBy(g => Math.Abs(g.Budget - currentBudget)).First();
        var delta = best.Mean - near.Mean;
        if (delta < Math.Max(500.0, latest.DailyIncome!.Value * 0.02))
            return new("NO CLEAR WINNER", "Compared repeated budgets; observed income after advertising differs too little to claim an improvement. Keep collecting evidence.", currentBudget, points.Count);
        return new("EXPERIMENTAL", $"A budget near USD {best.Budget:N0}/day showed roughly USD {delta:N0}/day more income after ads than the closest observed budget ({best.Count} vs {near.Count} days). This does not prove causation or true net profit. Run a controlled test before changing long-term spending.", best.Budget, points.Count);
    }
}


internal sealed record CompanyBusinessReport(string State, string Note, long? KnownCosts, long? Balance,
    decimal? RevenuePerCustomer, decimal? AdvertisingPerCustomer, long? BreakEvenCustomers, long? IncomeChange, bool CompleteCosts);
