using System.Text.RegularExpressions;

namespace TornCommander;

internal static class RecommendationEngine
{
    public static RecommendationResult Build(TornSnapshot s, AppSettings settings)
    {
        var r = new RecommendationResult();
        var restrictedApi = !settings.ApiFullAccessVerified && s.Errors.Any(IsAccessLevelError);
        var perksRestricted = s.Errors.Any(e => e.StartsWith("perks:", StringComparison.OrdinalIgnoreCase) && IsAccessLevelError(e));
        var parsedBonus = ParseMaxNerveBonuses(s);
        r.ParsedBonusNerve = parsedBonus > 0 ? parsedBonus : null;
        r.EstimatedNaturalNerve = EstimateNaturalNerve(s.MaxNerve, parsedBonus, settings.ManualNaturalNerveOverride);

        var nnb = r.EstimatedNaturalNerve;
        var graffiti = Crime(s, "graffiti");
        var skimming = Crime(s, "card skimming");
        var hustling = Crime(s, "hustling");
        var disposal = Crime(s, "disposal");
        var cracking = Crime(s, "cracking");

        if (s.PlayerName is null && s.MaxNerve is null)
        {
            r.Headline = "Connect the API to activate the live coach";
            r.Phase = "API not synced";
            r.Why = "The coach updates from your bars, perks, merits, education and Crimes 2.0 statistics.";
            r.Checklist.Add("Settings → save/test API key");
            r.Checklist.Add("Press Full Sync once");
            return r;
        }

        if (nnb is null || nnb < 35)
        {
            r.Phase = "NNB foundation";
            r.Headline = graffiti is not null && graffiti.Skill < 100
                ? $"Graffiti is still your main NNB grind — CS {graffiti.Skill:0.##} → 100"
                : "Stay on very safe CE until 35 natural nerve";
            r.Why = "At this CE bracket, protecting success rate and crime chain is more valuable than forcing advanced crimes and eating critical failures.";
            r.NextUnlock = "35 NNB → Hustling + safe Disposal";
            r.CrimeAdvice = "Main: Graffiti. Background: keep Card Skimming working. Do not grind Burglary, Arson, Forgery or Scamming just because they are unlocked.";
            r.GuidedAction = new GuideActionRecommendation
            {
                Crime = "Graffiti",
                Target = "Residential District",
                Option = "Purple",
                Url = TornRoutes.Graffiti,
                Summary = "Graffiti • Residential District • Purple paint (strict guided selection)",
                SupportsConfigure = true,
                SupportsRunOnce = true
            };
        }
        else if (nnb < 40)
        {
            r.Phase = "Hustling bridge";
            r.Headline = $"Shift the main grind toward Hustling{SkillSuffix(hustling)} + safe Disposal";
            r.Why = "You are in the bridge range where cheap Hustling successes can build/protect chain while Disposal adds stronger CE when safe jobs are available.";
            r.NextUnlock = "~40 NNB → serious Cracking";
            r.CrimeAdvice = "Use Hustling as the default burn, take safe Disposal opportunities, and keep Card Skimming running. Avoid riskier options that generate critical fails.";
            r.GuidedAction = new GuideActionRecommendation
            {
                Crime = "Hustling", Url = TornRoutes.Crimes, Summary = "Open Hustling — use the safest recommended hustle", SupportsConfigure = false, SupportsRunOnce = false
            };
        }
        else if (nnb < 60)
        {
            r.Phase = "Cracking engine";
            r.Headline = $"Cracking becomes the main CE engine{SkillSuffix(cracking)}";
            r.Why = "At roughly 40+ NNB, Cracking is one of the strongest consistent CE routes when your education, enhancer and chain are in place.";
            r.NextUnlock = "60+ NNB → broader advanced safe mix";
            r.CrimeAdvice = "Main: Cracking. Secondary: safe Disposal. Use cheap safe successes to recover chain after failures. Backfill crime skill when it supports merits/OC roles, not at the expense of CE safety.";
            r.GuidedAction = new GuideActionRecommendation
            {
                Crime = "Cracking", Url = TornRoutes.Crimes, Summary = "Open Cracking — use the current safest high-CE target", SupportsConfigure = false, SupportsRunOnce = false
            };
        }
        else
        {
            r.Phase = "Advanced CE farming";
            r.Headline = "Farm the hardest crime you can perform safely — not one fixed forever-crime";
            r.Why = "At high NNB, throughput, progression multipliers and avoiding critical failures matter more than following an old single-crime recipe.";
            r.NextUnlock = "Next +5 NNB milestone";
            r.CrimeAdvice = "Favor safe high-value Cracking / Disposal and add advanced Scamming, Forgery or Arson only when your success profile supports it. Keep measuring critical-fail rate.";
            r.GuidedAction = new GuideActionRecommendation
            {
                Crime = "Cracking", Url = TornRoutes.Crimes, Summary = "Open your current safest advanced CE option", SupportsConfigure = false, SupportsRunOnce = false
            };
        }

        var addiction = s.DetectedAddictionPercent ?? settings.ManualAddictionPercent;
        var addictionSource = s.DetectedAddictionPercent is not null ? "live API" : "manual fallback";
        if ((s.Level ?? 0) < 15 && addiction <= -10)
        {
            r.EducationAdvice = $"Addiction is {addiction}% ({addictionSource}). Until Level 15, avoid committing to long courses you are likely to be kicked from. Hit 15 → rehab → resume Psychology/Cracking education.";
            r.Checklist.Add("Reach Level 15, then rehab before the education push");
        }
        else if ((s.Level ?? 0) >= 15 && addiction <= -10)
        {
            r.EducationAdvice = $"Level {s.Level}: rehab is now the education priority. Clear the {addiction}% addiction ({addictionSource}), then restart the long-term education plan.";
            r.Checklist.Add("Rehab now; then resume education");
        }
        else
        {
            r.EducationAdvice = HasProgressionEducation(s)
                ? "Crime-progression education perk detected. Keep the Cracking Computer Science chain moving when applicable."
                : "Prioritize the Psychology path for the permanent crime progression bonus, then finish the useful Cracking Computer Science courses.";
            if (!HasProgressionEducation(s)) r.Checklist.Add("Work toward the Psychology crime-progression education perk");
        }

        r.NerveAdvice = BuildNerveAdvice(s, nnb);
        r.AccountAdvice = BuildAccountAdvice(s);

        if (restrictedApi)
            r.Checklist.Insert(0, "API DATA INCOMPLETE: replace the restricted key with a Full Access key in Settings, then Full Sync");

        if (!HasCrimeProgressionMerit(s))
            r.Checklist.Add(perksRestricted ? "Crime Progression merit status is unknown until the API key is fixed" : "Prioritize Crime Progression merits toward 10/10");
        if (!HasFactionCrimeProgression(s))
            r.Checklist.Add(perksRestricted ? "Faction Criminality → Crimes bonus is unknown until perk data is available" : "Verify your faction's Criminality → Crimes progression bonus");
        if (s.InOrganizedCrime == false) r.Checklist.Add("Get into an OC2.0 slot for no-nerve CE");
        if (s.NerveRefillAvailable == true) r.Checklist.Add("Daily nerve refill is available — drain the bar before using it");

        var priority = PriorityCrimeName(nnb);
        var priorityCrime = Crime(s, priority);
        if (priorityCrime is not null && !string.IsNullOrWhiteSpace(priorityCrime.Enhancer)
            && !s.EnhancerPerks.Any(x => x.Contains(priorityCrime.Enhancer, StringComparison.OrdinalIgnoreCase)))
            r.Checklist.Add($"Get/hold the {priorityCrime.Enhancer} enhancer for {priorityCrime.Name}");

        if (s.MaxNerve is int max && nnb is int natural)
        {
            var bonusText = parsedBonus > 0 ? $"parsed +{parsedBonus} max-nerve bonus" : "bonus amount not fully parsed";
            r.Why += $" Current working estimate: {natural} NNB from {max} displayed max ({bonusText}).";
        }

        return r;
    }

    public static string PlanForCrime(CrimeProgress c, int? nnb)
    {
        var name = c.Name.ToLowerInvariant();
        if (name.Contains("graffiti"))
            return nnb is null || nnb < 35 ? "MAIN — safest foundation CE" : c.Skill < 100 ? "Finish / chain builder" : "Chain builder / backfill";
        if (name.Contains("card skimming")) return "PASSIVE — keep skimmers running";
        if (name.Contains("hustling")) return nnb is >= 35 and < 40 ? "MAIN bridge crime" : nnb is >= 40 ? "Cheap chain recovery" : "Wait for ~35 NNB";
        if (name.Contains("disposal")) return nnb is >= 35 ? "Use safe jobs" : "Wait for ~35–40 NNB";
        if (name.Contains("cracking")) return nnb is >= 40 ? "MAIN CE engine" : "Wait for ~40 NNB";
        if (name.Contains("burglary")) return "Loot/merits later; can hurt chain";
        if (name.Contains("arson")) return nnb is >= 60 ? "Advanced safe mix" : "Too early for NNB grind";
        if (name.Contains("forgery") || name.Contains("scamming")) return nnb is >= 60 ? "Advanced — only when safe" : "Later; avoid crits";
        if (name.Contains("search") || name.Contains("bootlegging") || name.Contains("shoplifting") || name.Contains("pickpocket"))
            return "Backfill merits/uniques; not current NNB main";
        return "Use when safe / for progression goals";
    }

    private static CrimeProgress? Crime(TornSnapshot s, string name)
        => s.Crimes.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string SkillSuffix(CrimeProgress? c)
        => c is null ? "" : $" (CS {c.Skill:0.##})";

    private static string PriorityCrimeName(int? nnb)
        => nnb is null || nnb < 35 ? "graffiti" : nnb < 40 ? "hustling" : "cracking";

    private static string BuildNerveAdvice(TornSnapshot s, int? nnb)
    {
        var parts = new List<string>();
        if (s.CurrentNerve is int cur && s.MaxNerve is int max)
            parts.Add($"Nerve {cur}/{max}; avoid sitting capped.");
        if (s.NerveRefillAvailable == true) parts.Add("Your daily nerve refill is available.");
        else if (s.NerveRefillAvailable == false) parts.Add("Daily nerve refill already used/unavailable.");

        var totalNerve = s.Crimes.Sum(x => x.NerveSpent);
        var totalCrits = s.Crimes.Sum(x => x.CriticalFail);
        if (totalNerve > 0) parts.Add($"API tracks {totalNerve:N0} total Crimes 2.0 nerve spent and {totalCrits:N0} critical fail(s).");

        var priority = Crime(s, PriorityCrimeName(nnb));
        if (priority is not null && priority.ProgressionBonus > 0)
            parts.Add($"{priority.Name} currently reports +{priority.ProgressionBonus}% progression bonus.");

        parts.Add("Use alcohol/extra nerve when you can spend it immediately so natural regen is not blocked above cap.");
        return string.Join(" ", parts);
    }

    private static string BuildAccountAdvice(TornSnapshot s)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.CompanyName)) bits.Add($"Company: {s.CompanyName}.");
        else if (!string.IsNullOrWhiteSpace(s.JobName)) bits.Add($"Job: {s.JobName}.");
        if (!string.IsNullOrWhiteSpace(s.FactionName)) bits.Add($"Faction: {s.FactionName}.");
        if (s.InOrganizedCrime == true) bits.Add("OC2.0 detected.");
        else if (s.InOrganizedCrime == false) bits.Add("No active OC2.0 assignment detected.");
        if (s.DetectedAddictionPercent is int addiction) bits.Add($"Addiction: {addiction}% (auto-detected).");
        if (s.ManualLabor is int man && s.Intelligence is int intel && s.Endurance is int end)
        {
            bits.Add($"Work stats: MAN {man:N0} / INT {intel:N0} / END {end:N0}.");
            var judgeReady = man >= 6000 && intel >= 9000 && end >= 15000;
            bits.Add(judgeReady ? "Federal Judge work-stat threshold met." : "Federal Judge permanent crime perk remains a future work-stat goal.");
        }
        if (s.MeritAvailable > 0) bits.Add($"{s.MeritAvailable} unspent merit(s) detected.");
        return bits.Count == 0 ? "Account-side bonuses will appear here as the API permissions expose them." : string.Join(" ", bits);
    }

    private static bool HasProgressionEducation(TornSnapshot s)
        => s.EducationPerks.Any(x => ContainsCrimeProgression(x));

    private static bool HasCrimeProgressionMerit(TornSnapshot s)
        => s.MeritPerks.Any(x => ContainsCrimeProgression(x));

    private static bool HasFactionCrimeProgression(TornSnapshot s)
        => s.FactionPerks.Any(x => ContainsCrimeProgression(x));

    private static bool ContainsCrimeProgression(string text)
        => text.Contains("crime", StringComparison.OrdinalIgnoreCase)
           && (text.Contains("progress", StringComparison.OrdinalIgnoreCase)
               || text.Contains("experience", StringComparison.OrdinalIgnoreCase)
               || text.Contains("skill", StringComparison.OrdinalIgnoreCase));

    private static bool IsAccessLevelError(string error)
        => error.Contains("Access level of this key is not high enough", StringComparison.OrdinalIgnoreCase)
           || error.Contains("\"code\":16", StringComparison.OrdinalIgnoreCase);

    private static int? EstimateNaturalNerve(int? maxNerve, int parsedBonus, int manualOverride)
    {
        if (manualOverride >= 15 && (manualOverride - 15) % 5 == 0) return manualOverride;
        if (maxNerve is null) return null;

        var candidate = maxNerve.Value - parsedBonus;
        if (candidate >= 15 && (candidate - 15) % 5 == 0) return candidate;

        // Fallback inference only: natural nerve is a 15 + 5n value. Prefer the highest
        // possible natural value not exceeding displayed max and clearly label it as estimate.
        var inferred = maxNerve.Value;
        while (inferred >= 15 && (inferred - 15) % 5 != 0) inferred--;
        return inferred >= 15 ? inferred : null;
    }

    private static int ParseMaxNerveBonuses(TornSnapshot s)
    {
        var all = s.FactionPerks
            .Concat(s.JobPerks)
            .Concat(s.PropertyPerks)
            .Concat(s.EducationPerks)
            .Concat(s.EnhancerPerks)
            .Concat(s.MeritPerks)
            .Concat(s.BookPerks)
            .Concat(s.StockPerks);

        var total = 0;
        foreach (var text in all)
        {
            if (!text.Contains("nerve", StringComparison.OrdinalIgnoreCase)) continue;
            if (!text.Contains("maximum", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("max nerve", StringComparison.OrdinalIgnoreCase)
                && !text.Contains("nerve bar", StringComparison.OrdinalIgnoreCase)) continue;

            var matches = Regex.Matches(text, @"(?<!\d)(\d{1,3})(?!\d)");
            if (matches.Count == 0) continue;
            // Perk descriptions normally contain one integer for the amount. Use the last
            // integer because strings such as 'stage 10: +40 maximum nerve' put amount last.
            if (int.TryParse(matches[^1].Groups[1].Value, out var value) && value <= 100)
                total += value;
        }
        return total;
    }
}
