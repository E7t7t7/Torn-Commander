namespace TornCommander;

internal sealed record CompanyLayoutOption(string Id, string Name, string Reason, bool Suggested,
    string CountsText, Dictionary<long, int> Counts, int WorkingScore, int Underqualified, int? TrainerWorkingFit);
internal sealed record CompanyLayoutReport(string Stage, string Note, string Selected,
    List<CompanyLayoutOption> Options, List<string> Checks);

internal static class CompanyStaffLayouts
{
    // These are explicit Commander trial policies, not published game optima.
    // Role functions: https://wiki.torn.com/wiki/Company#Special_Positions
    // Candle roles: https://wiki.torn.com/wiki/Candle_Shop
    public static CompanyLayoutReport Build(CompanyObservation? company, CompanyTeamScan? scan)
    {
        var options = new List<CompanyLayoutOption>(); var checks = new List<string>();
        var selected = scan?.PlannedCounts == null ? "Current role counts" : scan.PlanSource;
        const string note = "Trial layouts for the scanned team, not proven revenue optima. Core production and sales are protected; support roles cost a worker slot. Working-stat scores compare fit only, not income. Apply positions manually and compare the next complete daily report. A new scan resets the local plan.";
        CompanyLayoutReport Stop(string message) => new("PLAN UNAVAILABLE", message, selected, options, checks);
        if (company == null || scan == null || scan.CompanyId != company.CompanyId) return Stop("Scan your company and staff first.");
        if (company.CompanyTypeId != 8) return Stop("Guided layouts currently cover Candle Shops. Other company types retain the custom role-count builder; their business roles need separate policies.");
        int n = scan.Employees.Count;
        if (!scan.EmployeesLoaded || n == 0 || company.Employees != n || scan.Employees.Any(x => x.Stats == null))
            return Stop("A complete company employee scan with working stats is needed. Missing employees or private stats cannot be treated as zero.");
        if (n < 4 || n > 10) return Stop("These Candle Shop trial layouts support 4–10 scanned employees. Use custom counts outside that range.");
        if (scan.Jobs.Select(x => x.Id).Distinct().Count() != scan.Jobs.Count)
            return Stop("Job catalogue contains duplicated IDs; scan again.");
        CompanyJobScan? Job(string name, string? ability = null) => scan.Jobs.FirstOrDefault(x =>
            x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            (name == "Quality Control" && x.Name.Equals("Quality Controller", StringComparison.OrdinalIgnoreCase)) ||
            (ability != null && x.Ability.Equals(ability, StringComparison.OrdinalIgnoreCase)));
        var chandler = Job("Chandler"); var sales = Job("Salesperson"); var cleaner = Job("Cleaner", "Cleaner");
        var manager = Job("Quality Control", "Manager"); var trainer = Job("Trainer", "Trainer"); var bookkeeper = Job("Bookkeeper", "Secretary");
        if (chandler?.Required == null || sales?.Required == null || cleaner?.Required == null || manager?.Required == null)
            return Stop("The Candle Shop role catalogue is incomplete. Chandler, Salesperson, Cleaner and Quality Control requirements are needed.");
        if (scan.Employees.Any(e => !scan.Jobs.Any(j => j.Id == e.PositionId && j.Required != null)))
            return Stop("Some current positions could not be matched to the live job catalogue. Scan again before comparing plans.");
        bool recovering = company.Environment.HasValue && company.Environment < 90;
        bool losingCash = company.AfterAdvertising < 0 || company.FullCostMargin < 0;
        bool small = n < 7;
        string suggested = recovering && n >= 7 ? "environment" : losingCash || small ? "cash" : "growth";

        void Add(string id, string title, string reason, int cleaners, CompanyJobScan? extra)
        {
            if (extra != null && extra.Required == null) return;
            int core = n - cleaners - 1 - (extra == null ? 0 : 1);
            if (core < 2) return;
            // Keep core roles near an even split (40–60%) as an explicit trial policy.
            // Search both possible assignments and the allowed split using actual stats.
            int lo = Math.Max(1, (int)Math.Ceiling(core * .4)), hi = Math.Min(core - 1, (int)Math.Floor(core * .6));
            if (lo > hi) { lo = core / 2; hi = (core + 1) / 2; }
            Dictionary<long, int>? best = null; int bestFit = int.MinValue, bestMoves = int.MaxValue, under = 0; int? bestTrainerFit = null;
            for (int production = lo; production <= hi; production++)
            {
                var counts = scan.Jobs.ToDictionary(x => x.Id, _ => 0);
                counts[chandler.Id] += production; counts[sales.Id] += core - production;
                counts[cleaner.Id] += cleaners; counts[manager.Id]++;
                if (extra != null) counts[extra.Id]++;
                var slots = scan.Jobs.SelectMany(j => Enumerable.Repeat(j, counts[j.Id])).ToList();
                var assignment = CompanyStaffAdvisor.Allocate(scan.Employees, slots);
                int score = 0, moves = 0, gaps = 0; int? trainerFit = null;
                for (int i = 0; i < n; i++)
                {
                    var e = scan.Employees[i]; var target = slots[assignment[i]];
                    int fit = CompanyStaffAdvisor.Fit(e.Stats!, target.Required!);
                    score += fit;
                    if (target.Ability.Equals("Trainer", StringComparison.OrdinalIgnoreCase)) trainerFit = fit;
                    if (target.Id != e.PositionId) moves++;
                    var a = e.Stats!; var r = target.Required!;
                    if (a.ManualLabor < r.ManualLabor || a.Intelligence < r.Intelligence || a.Endurance < r.Endurance) gaps++;
                }
                if (score > bestFit || (score == bestFit && moves < bestMoves))
                { best = counts; bestFit = score; bestMoves = moves; under = gaps; bestTrainerFit = trainerFit; }
            }
            if (best == null) return;
            options.Add(new(id, title, reason, suggested == id,
                string.Join(" · ", scan.Jobs.Where(j => best[j.Id] > 0).Select(j => best[j.Id] + " " + j.Name)),
                best, bestFit, under, bestTrainerFit));
        }
        Add("cash", "Core business first",
            "One Cleaner and one Quality Control; remaining staff make and sell candles. No dedicated Trainer or Bookkeeper. A lean comparison when cash is tight or staff count is small.", 1, null);
        if (!small && trainer != null)
            Add("growth", "Cash and staff growth",
                "Adds one Trainer to the core layout. This sacrifices one core worker to support future staff development; verify actual extra trains and revenue in Torn.", 1, trainer);
        if (!small && bookkeeper != null)
            Add("visibility", "Measure employee contribution",
                "Adds one Bookkeeper so Torn can expose individual employee contribution values. Use those observations to review staffing; this is an information tradeoff, not a forecast of higher revenue.", 1, bookkeeper);
        if (!small)
            Add("environment", "Test extra cleaning coverage",
                "Two Cleaners and one Quality Control; remaining staff cover making and selling. This tests an environment problem at the cost of a core worker; compare the next daily report.", 2, null);
        if (options.Count == 0) return Stop("No complete staffing layout could be built from this scan.");
        var growth = options.FirstOrDefault(x => x.Id == "growth");
        bool weakTrainer = growth?.TrainerWorkingFit is < 50;
        if (suggested == "growth" && weakTrainer) suggested = "cash";
        for (int i = 0; i < options.Count; i++) options[i] = options[i] with { Suggested = options[i].Id == suggested };
        if (!options.Any(x => x.Suggested))
        {
            int fallback = options.FindIndex(x => x.Id == "cash");
            options[fallback] = options[fallback] with { Suggested = true };
        }
        checks.Add("The 40–60% core-role split, one baseline Cleaner/Quality Control, 90% environment trigger and 50-point Trainer working-fit screen are Commander trial rules, not Torn requirements or an industry optimum.");
        if (weakTrainer) checks.Add("The growth layout's assigned Trainer has only " + growth!.TrainerWorkingFit + " modeled working-stat points. Core business is the safer default while this role is weak. Other modifiers can change actual Trainer effectiveness; check the actual extra trains before keeping that slot.");
        if (recovering) checks.Add("Environment is below the planner's 90% review threshold. Try extra cleaning only after checking staff-room suitability; extra cleaners may not solve an undersized room.");
        else if (!company.Environment.HasValue) checks.Add("Environment is unknown. Extra cleaning cannot be judged from this scan; inspect the company in Torn.");
        if (losingCash) checks.Add("Recorded spending exceeds income. A staffing layout alone will not repair overspending: review advertising, wages and supplies.");
        var choice = options.First(x => x.Suggested);
        if (choice.Underqualified > 0) checks.Add(choice.Underqualified + " employees in the suggested layout are below at least one recommended stat. The named plan shows their training gaps; low stats do not exclude them.");
        if (choice.WorkingScore < n * 45) checks.Add("Working-stat fit is weak across this team. Treat support coverage as a trial; prioritize training or suitable recruits rather than expecting role swaps alone to fix efficiency.");
        if (company.Rating is <= 2) checks.Add("At this early star rating, stabilize staffing and compare complete daily outcomes before spending on further expansion. Stars do not replace actual employee stats in the calculation.");
        checks.Add("Record income, customers, health, supplies and role counts for the next complete reporting day. Change one staffing trial at a time; do not compare a partial day with a completed day.");
        return new((company.Rating?.ToString() ?? "Unknown") + "-star Candle Shop · " + n + " scanned staff", note, selected, options, checks);
    }
}
