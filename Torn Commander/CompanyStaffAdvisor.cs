using System.Text.Json;

namespace TornCommander;

// Read-only models for the official v2 company and company-type endpoints.
// Contract: https://www.torn.com/swagger/openapi.json
internal sealed record CompanyWorkStats(long ManualLabor, long Intelligence, long Endurance);
internal sealed class CompanyEmployeeScan
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Position { get; set; } = "";
    public long PositionId { get; set; }
    public CompanyWorkStats? Stats { get; set; }
    public int? WorkingEffectiveness { get; set; }
    public int? TotalEffectiveness { get; set; }
    public int? Addiction { get; set; }
    public int? Inactivity { get; set; }
    public int? SettledIn { get; set; }
    public long? Wage { get; set; }
    public long? ContributionValue { get; set; }
}
internal sealed class CompanyJobScan
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Ability { get; set; } = "";
    public string Description { get; set; } = "";
    public CompanyWorkStats? Required { get; set; }
}
internal sealed class CompanyStockScan
{
    public string Name { get; set; } = "";
    public long? Cost { get; set; }
    public long? Price { get; set; }
    public long? InStock { get; set; }
    public long? OnOrder { get; set; }
}
internal sealed class CompanyTeamScan
{
    public long CompanyId { get; set; }
    public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;
    public bool EmployeesLoaded { get; set; }
    public bool StockLoaded { get; set; }
    public List<CompanyEmployeeScan> Employees { get; set; } = new();
    public List<CompanyJobScan> Jobs { get; set; } = new();
    public List<CompanyStockScan> Stock { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<long, int>? PlannedCounts { get; set; }
    public string PlanSource { get; set; } = "Current role counts";
}
internal sealed record CompanyAssignment(long Id, string Name, string Current, string Recommended,
    CompanyWorkStats? Stats, CompanyWorkStats? Required, int? Before, int? After,
    int? ActualWorking, int? ActualTotal, string NextStep, long? Wage, long? ContributionValue);
internal sealed record CompanyTeamReport(string State, string Note, int? Before, int? After,
    List<CompanyAssignment> Rows, List<string> Actions);

internal static class CompanyStaffAdvisor
{
    private static JsonElement Field(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    private static string Text(JsonElement e, string name) =>
        Field(e, name).ValueKind == JsonValueKind.String ? Field(e, name).GetString() ?? "" : "";
    private static long? Number(JsonElement e, string name, bool signed = false)
    {
        var v = Field(e, name);
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && (signed || n >= 0) ? n : null;
    }
    private static int? Integer(JsonElement e, string name, bool signed = false)
    {
        var n = Number(e, name, signed);
        return n is >= int.MinValue and <= int.MaxValue ? (int?)n.Value : null;
    }
    private static CompanyWorkStats? Stats(JsonElement e)
    {
        var m = Number(e, "manual_labor"); var i = Number(e, "intelligence"); var d = Number(e, "endurance");
        return m.HasValue && i.HasValue && d.HasValue ? new(m.Value, i.Value, d.Value) : null;
    }
    public static List<CompanyEmployeeScan> ReadEmployees(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = Field(doc.RootElement, "employees");
        if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Employee list unavailable. Check company API access.");
        if (list.GetArrayLength() > 200) throw new InvalidDataException("Unexpected employee count; scan rejected.");
        var result = list.EnumerateArray().Select(e => {
            var eff = Field(e, "effectiveness"); var pos = Field(e, "position");
            return new CompanyEmployeeScan {
                Id = Number(e, "id") ?? 0, Name = Text(e, "name"), Position = Text(pos, "name"),
                PositionId = Number(pos, "id") ?? 0, Stats = Stats(Field(e, "stats")),
                WorkingEffectiveness = Integer(eff, "working_stats"), TotalEffectiveness = Integer(eff, "total", true),
                Addiction = Integer(eff, "addiction", true), Inactivity = Integer(eff, "inactivity", true),
                SettledIn = Integer(eff, "settled_in"), Wage = Number(e, "wage"), ContributionValue = Number(e, "value", true)
            };
        }).ToList();
        if (result.Any(x => x.Id <= 0) || result.Select(x => x.Id).Distinct().Count() != result.Count)
            throw new InvalidDataException("Employee IDs missing or duplicated; scan rejected.");
        return result;
    }
    public static List<CompanyJobScan> ReadJobs(string json, long companyType)
    {
        using var doc = JsonDocument.Parse(json);
        var types = Field(doc.RootElement, "companies");
        if (types.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Company job catalogue unavailable.");
        var type = types.EnumerateArray().FirstOrDefault(x => Number(x, "id") == companyType);
        var positions = Field(type, "positions");
        if (positions.ValueKind != JsonValueKind.Array) throw new InvalidDataException("No jobs returned for this company type.");
        var jobs = positions.EnumerateArray().Select(e => new CompanyJobScan {
            Id = Number(e, "id") ?? 0, Name = Text(e, "name"), Ability = Text(e, "ability"), Description = Text(e, "description"),
            Required = Stats(Field(Field(e, "working_stats"), "required"))
        }).ToList();
        if (jobs.Any(x => x.Id <= 0) || jobs.Select(x => x.Id).Distinct().Count() != jobs.Count)
            throw new InvalidDataException("Invalid job catalogue; allocation withheld.");
        return jobs;
    }
    public static List<CompanyStockScan> ReadStock(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var stock = Field(doc.RootElement, "stock");
        if (stock.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Stock detail unavailable for this key.");
        return stock.EnumerateArray().Select(e => new CompanyStockScan {
            Name = Text(e, "name"), Cost = Number(e, "cost"), Price = Number(e, "price"),
            InStock = Number(e, "in_stock"), OnOrder = Number(e, "on_order")
        }).ToList();
    }
    // Official wiki employee effectiveness formula, applied to each required stat.
    // https://wiki.torn.com/wiki/Company#Employee_Effectiveness
    // Zero requirements contribute nothing; zero actual stats never enter log(0).
    public static int Fit(CompanyWorkStats actual, CompanyWorkStats required)
    {
        static int Part(long value, long need)
        {
            if (need <= 0 || value <= 0) return 0;
            double ratio = (double)value / need;
            return (int)Math.Floor(Math.Min(45, 45 * ratio) + Math.Max(0, 5 * Math.Log2(ratio)));
        }
        return Part(actual.ManualLabor, required.ManualLabor) +
            Part(actual.Intelligence, required.Intelligence) + Part(actual.Endurance, required.Endurance);
    }
    // Hungarian minimum-cost assignment; slot counts are the CURRENT job mix.
    // A secondary cost favours keeping existing assignments when total fit ties.
    public static int[] Allocate(IReadOnlyList<CompanyEmployeeScan> staff, IReadOnlyList<CompanyJobScan> slots)
    {
        int n = staff.Count;
        if (n != slots.Count || n > 200 || staff.Any(x => x.Stats == null) || slots.Any(x => x.Required == null))
            throw new InvalidDataException("Allocation requires one known job slot per employee and complete working stats.");
        var u = new long[n + 1]; var v = new long[n + 1]; var p = new int[n + 1]; var way = new int[n + 1];
        for (int i = 1; i <= n; i++)
        {
            p[0] = i; int j0 = 0;
            var min = Enumerable.Repeat(long.MaxValue, n + 1).ToArray(); var used = new bool[n + 1];
            do
            {
                used[j0] = true; int i0 = p[j0], j1 = 0; long delta = long.MaxValue;
                for (int j = 1; j <= n; j++) if (!used[j])
                {
                    long cost = -Fit(staff[i0 - 1].Stats!, slots[j - 1].Required!) * 1000L +
                        (staff[i0 - 1].PositionId == slots[j - 1].Id ? 0 : 1);
                    long cur = cost - u[i0] - v[j];
                    if (cur < min[j]) { min[j] = cur; way[j] = j0; }
                    if (min[j] < delta) { delta = min[j]; j1 = j; }
                }
                for (int j = 0; j <= n; j++)
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else min[j] -= delta;
                j0 = j1;
            } while (p[j0] != 0);
            do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
        }
        var assignment = new int[n];
        for (int j = 1; j <= n; j++) assignment[p[j] - 1] = j - 1;
        return assignment;
    }
    private static string Gap(CompanyEmployeeScan employee, CompanyJobScan? job)
    {
        if (employee.Stats == null) return "Working stats unavailable: use a key with company employee-detail access, then scan again.";
        if (job?.Required == null) return "Job requirements unavailable; keep the current role until a complete scan.";
        var a = employee.Stats; var r = job.Required;
        var gaps = new[] { ("MAN", r.ManualLabor - a.ManualLabor), ("INT", r.Intelligence - a.Intelligence), ("END", r.Endurance - a.Endurance) }
            .Where(x => x.Item2 > 0).Select(x => x.Item1 + " +" + x.Item2.ToString("N0")).ToList();
        var message = gaps.Count > 0 ? "Recommended-stat gaps: " + string.Join(", ", gaps) + ". Prioritize these working stats."
            : "Meets recommended working stats. Maintain activity and monitor effectiveness penalties.";
        if (employee.Addiction < 0) message += " Addiction penalty: " + employee.Addiction + "; discuss recovery with this employee.";
        if (employee.Inactivity < 0) message += " Inactivity penalty: " + employee.Inactivity + "; check their availability.";
        return message;
    }
    public static Dictionary<long, int> ReadCounts(JsonElement request, CompanyTeamScan scan)
    {
        var rows = Field(request, "counts");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != scan.Jobs.Count)
            throw new InvalidDataException("Enter a slot count for every available role.");
        var counts = new Dictionary<long, int>();
        foreach (var row in rows.EnumerateArray())
        {
            var id = Number(row, "positionId"); var count = Integer(row, "count");
            if (!id.HasValue || !scan.Jobs.Any(x => x.Id == id) || !count.HasValue || count > 200 || !counts.TryAdd(id.Value, count.Value))
                throw new InvalidDataException("Role counts must be whole numbers from 0 to 200 with no duplicated roles.");
        }
        if (!scan.EmployeesLoaded || counts.Values.Sum() != scan.Employees.Count)
            throw new InvalidDataException("The total number of role slots must equal the scanned employee count.");
        return counts;
    }
    public static CompanyTeamReport Report(CompanyObservation? company, CompanyTeamScan? scan)
    {
        var actions = new List<string>();
        if (company == null) return new("NOT SCANNED", "Sync Company to scan the business and its staff.", null, null, new(), actions);
        if (company.AfterAdvertising < 0) actions.Add("Review advertising first: it exceeds reported income by " + (-company.AfterAdvertising.Value).ToString("C0") + " before wages and supplies. Set an affordable test budget and record the next daily result.");
        if (company.Efficiency < 100) actions.Add("Efficiency is " + company.Efficiency + "%. Review staff fit, inactivity and addiction penalties before expanding.");
        if (company.Environment < 100) actions.Add("Environment is " + company.Environment + "%. Review cleaning coverage and staff-room suitability in Torn.");
        if (company.Popularity < 100) actions.Add("Popularity is " + company.Popularity + "%. Check product availability and prices before increasing advertising.");
        if (company.Trains > 0) actions.Add(company.Trains + " company trains available. Use the recommended-role gaps below to choose working-stat training priorities.");
        if (company.FullCostMargin == null) actions.Add("Complete today's wages and stock/supply costs to see an accounted margin before committing cash to upgrades.");
        if (scan == null || scan.CompanyId != company.CompanyId)
            return new("NOT SCANNED", "No staff scan for this company. Sync Company to load staff and requirements.", null, null, new(), actions);
        if (scan.StockLoaded)
        {
            foreach (var item in scan.Stock.Where(x => x.InStock == 0))
                actions.Add(item.Name + ": no stock. " + (item.OnOrder > 0 ? item.OnOrder + " on order; check delivery before spending more." : "Check demand and an affordable replenishment order."));
            foreach (var item in scan.Stock.Where(x => x.Price.HasValue && x.Cost.HasValue && x.Price < x.Cost))
                actions.Add(item.Name + ": selling below unit cost. Review the price before scaling sales.");
        }
        var staff = scan.Employees;
        var slots = staff.Select(e => scan.Jobs.FirstOrDefault(j => j.Id == e.PositionId)).ToList();
        bool complete = scan.EmployeesLoaded && staff.Count > 0 && company.Employees == staff.Count &&
            staff.All(e => e.Stats != null) && slots.All(j => j?.Required != null);
        var targetSlots = scan.PlannedCounts == null ? slots.Where(x => x != null).Select(x => x!).ToList() :
            scan.Jobs.SelectMany(j => Enumerable.Repeat(j, Math.Clamp(scan.PlannedCounts.GetValueOrDefault(j.Id), 0, 200))).ToList();
        complete = complete && targetSlots.Count == staff.Count && targetSlots.All(x => x.Required != null);
        var assignment = complete ? Allocate(staff, targetSlots) : null;
        var rows = staff.Select((e, i) => {
            var current = slots[i]; var target = assignment == null ? current : targetSlots[assignment[i]];
            return new CompanyAssignment(e.Id, e.Name, e.Position, assignment == null ? "Plan unavailable" : target!.Name,
                e.Stats, target?.Required, e.Stats != null && current?.Required != null ? Fit(e.Stats, current.Required) : null,
                assignment != null ? Fit(e.Stats!, target!.Required!) : null, e.WorkingEffectiveness, e.TotalEffectiveness, Gap(e, target), e.Wage, e.ContributionValue);
        }).ToList();
        int? before = complete ? rows.Sum(x => x.Before!.Value) : null;
        int? after = complete ? rows.Sum(x => x.After!.Value) : null;
        if (complete)
            actions.Add(after > before ? "Apply the proposed staff swaps manually, preserving the listed number of each role. Review the next company report before making further changes."
                : "The proposed staffing mix has no modeled working-stat gain over the current allocation. Compare role coverage, training and effectiveness penalties before changing roles.");
        if (rows.Any(x => x.Before.HasValue && x.ActualWorking.HasValue && x.Before != x.ActualWorking))
            actions.Add("Some modeled current scores differ from Torn's reported working-stat effectiveness. Treat swaps as estimates and verify the role requirements and next report.");
        if (actions.Count == 0) actions.Add("Record daily results and keep staffing stable to establish a useful baseline before changing budgets or capacity.");
        return new(complete ? "TEAM PLAN READY" : "INCOMPLETE SCAN",
            "Best combined working-stat fit for the selected job counts (current staffing mix by default); includes underqualified staff. Changing role counts is a scenario: preserve the sales and support coverage your business needs. This does not choose a profitable role mix or predict profit. Other effectiveness modifiers and support-role effects are excluded. No positions are changed automatically.",
            before, after, rows, actions);
    }
}
