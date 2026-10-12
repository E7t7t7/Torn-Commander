using System.Text.Json;

namespace TornCommander;

internal sealed class CompanyObservation
{
    public DateTime ObservedUtc { get; set; } = DateTime.UtcNow;
    public string DayUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public long CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public string CompanyType { get; set; } = "";
    public int Rating { get; set; }
    public int Employees { get; set; }
    public long DailyIncome { get; set; }
    public int DailyCustomers { get; set; }
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
    public long ActiveCompanyId { get; set; }
    public string Status { get; set; } = "Connect a Torn API key and sync your company to begin collecting daily results.";
    public List<CompanyObservation> Days { get; set; } = new();
}
internal sealed record CompanyRecommendation(string State, string Message, long SuggestedBudget, int SampleDays);
internal static class CompanyIntelligence
{
    private static long Long(JsonElement x, string field)
    {
        if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty(field, out var y)) return 0;
        return y.ValueKind == JsonValueKind.Number && y.TryGetInt64(out var n) ? n : 0;
    }
    private static long? OptionalLong(JsonElement x, string field) =>
        x.ValueKind == JsonValueKind.Object && x.TryGetProperty(field, out var y) &&
        y.ValueKind == JsonValueKind.Number && y.TryGetInt64(out var n) ? n : null;
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
            Rating = (int)Long(p, "rating"),
            Employees = p.TryGetProperty("employees", out var e) ? (int)Long(e, "hired") : 0,
            DailyIncome = p.TryGetProperty("income", out var income) ? Long(income, "daily") : 0,
            DailyCustomers = p.TryGetProperty("customers", out var cust) ? (int)Long(cust, "daily") : 0,
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

    public static CompanyRecommendation Analyze(IReadOnlyList<CompanyObservation> days)
    {
        var points = days.Where(x => x.FromApi && x.DailyAdvertising.HasValue)
            .OrderBy(x => x.DayUtc, StringComparer.Ordinal).ToList();
        if (points.Count == 0)
            return new("NO BASELINE", "Sync a Limited or Full Access company profile, or enter advertising manually. No budget recommendation yet.", 0, 0);
        var latest = points[^1];
        var currentBudget = latest.DailyAdvertising ?? 0;
        if (points.Count < 7)
            return new("BASELINE", $"Collected {points.Count}/7 daily records with advertising costs. Hold the budget steady while building history.", currentBudget, points.Count);
        var step = Math.Max(100L, (long)Math.Round(Math.Max(1L, currentBudget) * 0.10));
        var groups = points.GroupBy(x => (long)Math.Round((double)x.DailyAdvertising!.Value / step) * step)
            .Where(g => g.Count() >= 3).Select(g => new {
                Budget = g.Key,
                Count = g.Count(),
                Mean = g.Average(x => x.DailyIncome - x.DailyAdvertising!.Value),
                StaffSpread = g.Max(x => x.Employees) - g.Min(x => x.Employees)
            }).OrderByDescending(g => g.Mean).ToList();
        if (groups.Count < 2)
            return new("TEST NEXT", $"Collected {points.Count} records but fewer than 3 results at two distinct budget levels. Test roughly 10% above or below the current daily budget for at least 3 complete reporting days, holding staffing and prices steady.", currentBudget, points.Count);
        if (groups.Any(g => g.StaffSpread > 1))
            return new("CONFOUNDED", "Employee count changed within an advertising test group. Keep staffing stable before attributing a revenue change to advertising.", currentBudget, points.Count);
        var best = groups[0];
        var near = groups.OrderBy(g => Math.Abs(g.Budget - currentBudget)).First();
        var delta = best.Mean - near.Mean;
        if (delta < Math.Max(500.0, latest.DailyIncome * 0.02))
            return new("NO CLEAR WINNER", "Compared repeated budgets; observed income after advertising differs too little to claim an improvement. Keep collecting evidence.", currentBudget, points.Count);
        return new("EXPERIMENTAL", $"A budget near USD {best.Budget:N0}/day showed roughly USD {delta:N0}/day more income after ads than the closest observed budget ({best.Count} vs {near.Count} days). This does not prove causation or true net profit. Run a controlled test before changing long-term spending.", best.Budget, points.Count);
    }
}
