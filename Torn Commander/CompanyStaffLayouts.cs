namespace TornCommander;

internal sealed record CompanyLayoutOption(string Id, string Name, string Reason, bool Suggested,
    string CountsText, Dictionary<long, int> Counts, int WorkingScore, int Underqualified, int? TrainerWorkingFit)
{
    public string Policy { get; init; } = "total-fit";
}
internal sealed record CompanyLayoutReport(string Stage, string Note, string Selected,
    List<CompanyLayoutOption> Options, List<string> Checks);

internal static class CompanyStaffLayouts
{
    public const int PlanVersion = 2;
    public static bool IsLegacyPlan(CompanyTeamScan scan) => scan.PlannedCounts != null && scan.PlanVersion < PlanVersion;
    public static HashSet<long> KeyRoles(IEnumerable<CompanyJobScan> jobs) => jobs.Where(j =>
        j.Name.Equals("Chandler", StringComparison.OrdinalIgnoreCase) ||
        j.Ability.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
        j.Ability.Equals("Cleaner", StringComparison.OrdinalIgnoreCase)).Select(j => j.Id).ToHashSet();
    public static CompanyLayoutReport Build(CompanyObservation? company, CompanyTeamScan? scan)
    {
        var options = new List<CompanyLayoutOption>(); var checks = new List<string>();
        string selected = scan == null || scan.PlannedCounts == null ? "Current positions — review only" :
            IsLegacyPlan(scan) ? "Old planner suggestion — rebuild before using" : scan.PlanSource;
        const string note = "Keep your whole team, including new players with very low stats. No changes are recommended just by scanning. Preview the starter draft to see a simpler arrangement. It is a planning starting point, not a proven best money-making setup.";
        CompanyLayoutReport Stop(string message) => new("REVIEW NEEDED", message, selected, options, checks);
        if (company == null || scan == null || scan.CompanyId != company.CompanyId) return Stop("Scan your company and staff first.");
        if (IsLegacyPlan(scan)) checks.Add("The old planner used unsupported role-count rules. Its saved suggestion is no longer treated as a valid plan. The app has not changed any positions in Torn.");
        if (company.CompanyTypeId != 8) return Stop("The starter draft currently covers Candle Shops. You can still review this company's current positions or explicitly compare custom counts.");
        int n = scan.Employees.Count;
        if (!scan.EmployeesLoaded || company.Employees != n || scan.Employees.Any(x => x.Stats == null))
            return Stop("Some employee stats are missing. That is different from low or zero stats. Scan with employee-detail access before building a whole-team draft.");
        if (n < 4 || n > 10) return Stop("The Candle Shop starter draft covers 4–10 staff. Use the clearly labelled custom comparison outside that range.");
        var chandler = scan.Jobs.FirstOrDefault(x => x.Name.Equals("Chandler", StringComparison.OrdinalIgnoreCase));
        var sales = scan.Jobs.FirstOrDefault(x => x.Name.Equals("Salesperson", StringComparison.OrdinalIgnoreCase));
        var cleaner = scan.Jobs.FirstOrDefault(x => x.Ability.Equals("Cleaner", StringComparison.OrdinalIgnoreCase));
        var manager = scan.Jobs.FirstOrDefault(x => x.Ability.Equals("Manager", StringComparison.OrdinalIgnoreCase));
        if (scan.Jobs.Select(x => x.Id).Distinct().Count() != scan.Jobs.Count ||
            new[] { chandler, sales, cleaner, manager }.Any(x => x?.Required == null) ||
            new[] { chandler!.Id, sales!.Id, cleaner!.Id, manager!.Id }.Distinct().Count() != 4)
            return Stop("The required Candle Shop jobs could not be verified from the live catalogue.");
        var counts = scan.Jobs.ToDictionary(x => x.Id, _ => 0);
        // A simple, explicit COVERAGE assumption, never represented as a revenue optimum.
        // No near-even Chandler split, automatic second Cleaner or dedicated Trainer.
        counts[chandler.Id] = 1; counts[cleaner.Id] = 1; counts[manager.Id] = 1; counts[sales.Id] = n - 3;
        var slots = scan.Jobs.SelectMany(j => Enumerable.Repeat(j, counts[j.Id])).ToList();
        var assigned = CompanyStaffAdvisor.AllocateProtected(scan.Employees, slots, KeyRoles(scan.Jobs));
        int score = scan.Employees.Select((e, i) => CompanyStaffAdvisor.Fit(e.Stats!, slots[assigned[i]].Required!)).Sum();
        int under = scan.Employees.Where((e, i) => {
            var a = e.Stats!; var r = slots[assigned[i]].Required!;
            return a.ManualLabor < r.ManualLabor || a.Intelligence < r.Intelligence || a.Endurance < r.Endurance;
        }).Count();
        options.Add(new("starter-v2", "New-company starter draft — review before applying",
            "Start by covering candle-making, Quality Control and cleaning once each; put the remaining people in Salesperson jobs. Give the best available staff coverage to those key jobs first. Nobody is dropped for low stats. One of each is a starting coverage assumption, not proof that these job counts earn the most.",
            false, string.Join(" · ", scan.Jobs.Where(j => counts[j.Id] > 0).Select(j => counts[j.Id] + " " + j.Name)),
            counts, score, under, null) { Policy = "protect-key-roles" });
        int currentCleaners = scan.Employees.Count(e => e.PositionId == cleaner.Id);
        if (currentCleaners > 1)
            checks.Add("Your current setup has " + currentCleaners + " Cleaners. The scan has not shown that a second cleaner helps. The starter draft uses one; check Environment and the staff room before testing extra cleaning.");
        if (scan.Employees.Any(e => scan.Jobs.Any(j => j.Id == e.PositionId && j.Ability.Equals("Trainer", StringComparison.OrdinalIgnoreCase))))
            checks.Add("A Trainer takes someone away from the other jobs. The starter draft does not reserve that slot. Training your new players still matters; use available company trains and check actual extra trains before testing a dedicated Trainer.");
        if (company.Environment.HasValue && company.Environment < 100)
            checks.Add("Environment is " + company.Environment + "%. Check the first cleaner and staff-room suitability. A low bar alone does not prove you need two cleaners.");
        if (!company.Environment.HasValue) checks.Add("Environment was not exposed by this scan. Do not add a second cleaner on that missing information.");
        if (company.AfterAdvertising < 0 || company.FullCostMargin < 0)
            checks.Add("Recorded spending is above income. Moving employees alone will not fix that; review advertising, wages and supplies too.");
        checks.Add("New players and filler staff stay included, even with zero stats. The draft uses the strongest available coverage for key jobs and leaves general sales places for the rest. It does not require maxed employees or recommend firing anyone.");
        checks.Add("Keep a note of what you change, then compare complete daily reports: income, customers, stock costs and Environment. Do not repeatedly reshuffle people just to raise the displayed score.");
        return new((company.Rating?.ToString() ?? "Unknown") + "-star Candle Shop · " + n + " scanned staff", note, selected, options, checks);
    }
}
