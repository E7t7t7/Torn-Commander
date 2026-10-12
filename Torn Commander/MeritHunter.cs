using System.Text.Json;
using System.Text.RegularExpressions;

namespace TornCommander;

internal static class MeritHunter
{
    private static readonly HashSet<string> KnownEasy = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pocket Money", "Pious", "Sanity", "Historian", "Church Mouse", "Lean",
        "Double Dragon", "Domino Effect", "Clotted", "Gone Fishing", "Lovestruck",
        "Zero Liability", "Flatline"
    };

    private static readonly string[] EasyWords =
    {
        "donate", "read", "visit", "use ", "equip", "invest", "bank", "church",
        "hospitalize yourself", "get mugged", "assisted attack", "one hit", "one-hit",
        "recover a card skimmer", "displaying the", "married couple"
    };

    private static readonly string[] HardWords =
    {
        "ranked war", "territory war", "competition", "elimination", "1000 days",
        "year", "years", "billion", "trillion", "10,000", "10000", "100,000", "100000"
    };

    public static void Build(TornSnapshot s)
    {
        s.Awards.Clear();
        s.MeritUpgrades.Clear();

        var achievedHonors = ParseAchievedIds(s.RawJson.GetValueOrDefault("honors"));
        var achievedMedals = ParseAchievedIds(s.RawJson.GetValueOrDefault("medals"));
        var personalStatsJson = s.RawJson.GetValueOrDefault("personalstats");
        var stats = ParseNumericLeaves(personalStatsJson);
        s.PersonalStats = stats;
        s.CrimeVersion = NormalizeCrimeVersion(DetectCrimeVersion(personalStatsJson));
        if (string.IsNullOrWhiteSpace(s.CrimeVersion) && s.Crimes.Count > 0) s.CrimeVersion = "v2";

        AddCatalog(s, "torn_honors", "honors", "Honor", achievedHonors, stats);
        AddCatalog(s, "torn_medals", "medals", "Medal", achievedMedals, stats);
        ParseMeritUpgrades(s);

        s.Awards = s.Awards
            .OrderByDescending(x => x.Achieved)
            .ThenByDescending(x => x.EaseScore)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public static string SpendAdvice(TornSnapshot s, string mode)
    {
        var crime = FindUpgrade(s, "crime progression", "crime experience", "crime");
        var nerve = FindUpgrade(s, "nerve bar", "nerve");
        var available = s.MeritAvailable;
        var prefix = available > 0 ? $"You have {available} unspent merit point{(available == 1 ? "" : "s")}. " : "No unspent merit points right now. ";

        if (mode.Equals("Max Nerve", StringComparison.OrdinalIgnoreCase))
        {
            if (nerve is not null && nerve.Level < Math.Max(1, nerve.MaxLevel))
                return prefix + $"Max Nerve mode: next spend → {nerve.Name} ({nerve.Level}/{nerve.MaxLevel}). This raises displayed max nerve/refill size, not hidden NNB.";
            if (crime is not null && crime.Level < Math.Max(1, crime.MaxLevel))
                return prefix + $"Nerve Bar appears capped; next long-term NNB spend → {crime.Name} ({crime.Level}/{crime.MaxLevel}).";
        }
        else
        {
            if (crime is not null && crime.Level < Math.Max(1, crime.MaxLevel))
                return prefix + $"Best long-term NNB spend → {crime.Name} ({crime.Level}/{crime.MaxLevel}) until capped, then Nerve Bar.";
            if (nerve is not null && nerve.Level < Math.Max(1, nerve.MaxLevel))
                return prefix + $"Crime Progression appears capped; next spend → {nerve.Name} ({nerve.Level}/{nerve.MaxLevel}) for more nerve capacity/refill value.";
        }

        return prefix + "Commander could not confidently map the Crime Progression / Nerve Bar upgrade names from Torn's current merit catalog. Check the Merits page before spending.";
    }

    public static string RouteFor(MeritAward award)
    {
        if (award.Name.Equals("The Socialist", StringComparison.OrdinalIgnoreCase)) return TornRoutes.Points;
        var t = (award.Name + " " + award.Description + " " + award.Category).ToLowerInvariant();
        if (t.Contains("refer") || t.Contains("referral")) return TornRoutes.Recruit;
        if (t.Contains("graffiti") || t.Contains("crime") || t.Contains("skimmer") || t.Contains("pickpocket") || t.Contains("burgl")) return TornRoutes.Crimes;
        if (t.Contains("race") || t.Contains("racing")) return TornRoutes.Racing;
        if (t.Contains("casino") || t.Contains("blackjack") || t.Contains("slot") || t.Contains("poker")) return TornRoutes.Casino;
        if (t.Contains("gym") || t.Contains("strength") || t.Contains("speed") || t.Contains("defense") || t.Contains("defence") || t.Contains("dexterity")) return TornRoutes.Gym;
        if (t.Contains("education") || t.Contains("course")) return TornRoutes.Education;
        if (t.Contains("item") || t.Contains("bazaar") || t.Contains("market")) return TornRoutes.Items;
        return TornRoutes.Awards;
    }

    private static MeritUpgradeState? FindUpgrade(TornSnapshot s, params string[] fragments)
        => s.MeritUpgrades.FirstOrDefault(x => fragments.Any(f => x.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));

    private static void AddCatalog(TornSnapshot s, string rawKey, string rootName, string awardType, HashSet<int> achieved, Dictionary<string, long> stats)
    {
        if (!s.RawJson.TryGetValue(rawKey, out var json) || string.IsNullOrWhiteSpace(json)) return;
        using var doc = JsonDocument.Parse(json);
        if (!TryFindArray(doc.RootElement, rootName, out var arr)) return;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = ReadInt(item, "id") ?? 0;
            if (id <= 0) continue;
            var name = ReadString(item, "name") ?? $"{awardType} #{id}";
            var description = ReadString(item, "description") ?? ReadString(item, "requirement") ?? "";
            var type = ReadString(item, "type") ?? ReadString(item, "category") ?? "";
            var rarity = ReadString(item, "rarity") ?? "";
            var crimesVersion = NormalizeCrimeVersion(ReadString(item, "crimes_version") ?? "");
            var typeId = ReadNestedInt(item, "type", "id");
            // Honor type id 1 is a default honor bar, not an award the player needs to hunt for.
            if (awardType == "Honor" && typeId == 1) continue;
            var earned = achieved.Contains(id);

            var award = new MeritAward
            {
                Id = id,
                AwardType = awardType,
                Name = name,
                Description = description,
                Category = InferCategory(name + " " + description + " " + type),
                Rarity = rarity,
                CrimesVersion = crimesVersion,
                Achieved = earned
            };

            ApplyAvailability(s, award);
            EstimateProgress(s, award, stats);
            award.Advice = BuildAdvice(s, award);
            award.EaseScore = ScoreEase(s, award);
            award.Effort = !award.AvailableForCurrentCrimeVersion ? "UNAVAILABLE"
                : award.ProgressConflict ? "VERIFY"
                : EffortLabel(award.EaseScore, award.Achieved);
            award.Route = RouteFor(award);
            s.Awards.Add(award);
        }
    }

    private static void ParseMeritUpgrades(TornSnapshot s)
    {
        var catalog = new Dictionary<int, MeritUpgradeState>();
        if (s.RawJson.TryGetValue("torn_merits", out var catalogJson))
        {
            using var doc = JsonDocument.Parse(catalogJson);
            if (TryFindArray(doc.RootElement, "merits", out var arr))
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var id = ReadInt(item, "id") ?? 0;
                    if (id <= 0) continue;
                    catalog[id] = new MeritUpgradeState
                    {
                        Id = id,
                        Name = ReadString(item, "name") ?? $"Merit #{id}",
                        Description = ReadString(item, "description") ?? "",
                        MaxLevel = ReadInt(item, "max_level") ?? ReadInt(item, "maximum") ?? 10
                    };
                }
            }
        }

        if (s.RawJson.TryGetValue("merits", out var userJson))
        {
            using var doc = JsonDocument.Parse(userJson);
            if (TryFindArray(doc.RootElement, "upgrades", out var upgrades))
            {
                foreach (var item in upgrades.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var id = ReadInt(item, "id") ?? ReadInt(item, "merit_id") ?? 0;
                    if (id <= 0) continue;
                    if (!catalog.TryGetValue(id, out var u))
                        u = new MeritUpgradeState { Id = id, Name = ReadString(item, "name") ?? $"Merit #{id}", MaxLevel = 10 };
                    u.Level = ReadInt(item, "level") ?? ReadInt(item, "value") ?? 0;
                    catalog[id] = u;
                }
            }
        }

        s.MeritUpgrades = catalog.Values.OrderBy(x => x.Name).ToList();
    }

    private static HashSet<int> ParseAchievedIds(string? json)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(json)) return set;
        using var doc = JsonDocument.Parse(json);
        foreach (var e in Enumerate(doc.RootElement))
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var id = ReadInt(e, "id") ?? ReadInt(e, "honor_id") ?? ReadInt(e, "medal_id");
            if (id is > 0) set.Add(id.Value);
        }
        return set;
    }

    private static Dictionary<string, long> ParseNumericLeaves(string? json)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        using var doc = JsonDocument.Parse(json);
        Flatten(doc.RootElement, "", result);
        return result;
    }

    private static void Flatten(JsonElement e, string path, Dictionary<string, long> result)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
                Flatten(p.Value, string.IsNullOrEmpty(path) ? p.Name : path + "." + p.Name, result);
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var x in e.EnumerateArray()) Flatten(x, path + "[" + i++ + "]", result);
        }
        else if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n))
        {
            result[path] = n;
        }
    }

    private static void EstimateProgress(TornSnapshot s, MeritAward a, Dictionary<string, long> stats)
    {
        a.ProgressConflict = false;
        if (a.Achieved)
        {
            a.ProgressPercent = 100;
            a.ProgressSource = "API achieved";
            return;
        }

        if (!a.AvailableForCurrentCrimeVersion)
        {
            a.ProgressPercent = null;
            a.Current = null;
            a.Target = ExtractLikelyTarget(a.Description);
            a.ProgressSource = a.Availability;
            return;
        }

        var text = (a.Name + " " + a.Description).ToLowerInvariant();

        // Referral / legacy-social honors frequently contain phrases like "level 10".
        // That level belongs to the referred player, NOT the key owner's account.
        if (IsReferralOrExternalLevelAward(text))
        {
            a.Target = a.Name.Equals("Social Butterfly", StringComparison.OrdinalIgnoreCase) ? 3
                : a.Name.Equals("Pyramid Scheme", StringComparison.OrdinalIgnoreCase) ? 1
                : null;
            a.Current = null;
            a.ProgressPercent = null;
            a.ProgressSource = "Exact incomplete referral/legacy progress is not exposed by the current API.";
            return;
        }

        var target = ExtractLikelyTarget(a.Description);
        long? current = null;
        string source = "";

        if (IsOwnLevelAward(a) && s.Level is int level)
        {
            current = level;
            source = "API account level";
        }
        else if ((text.Contains("education") || text.Contains("course")) && target is not null
                 && Regex.IsMatch(a.Description, @"(?i)(complete|finish|pass).*\d+.*(education|course)|\d+.*courses?"))
        {
            current = s.CompletedEducationIds.Count;
            source = "API completed education count";
        }
        else
        {
            var crime = s.Crimes.FirstOrDefault(c => text.Contains(c.Name.ToLowerInvariant()));
            if (crime is not null)
            {
                if (text.Contains("skill"))
                {
                    current = (long)Math.Floor(crime.Skill);
                    source = $"API {crime.Name} skill";
                }
                else if (text.Contains("successful") || text.Contains("success") || text.Contains("offense") || text.Contains("offence") || text.Contains("commit"))
                {
                    current = crime.Success;
                    source = $"API {crime.Name} successes";
                }
            }
        }

        // Only use a personal-stat value when the requirement maps to one exact, known Torn stat.
        // Never fuzzy-match generic words like "crime" or "attack" again: that caused false
        // 1,024/1,000 Murder and 126/50 Defends estimates in v0.3.2.
        if (current is null && TryResolveExactPersonalStat(a.Description, stats, out var exactValue, out var exactSource))
        {
            current = exactValue;
            source = exactSource;
        }

        // Crimes 2.0 category medals have exact counters in personalstats.crimes.offenses.*.
        if (current is null && TryResolveCrimeCategoryStat(a, stats, out var crimeValue, out var crimeSource))
        {
            current = crimeValue;
            source = crimeSource;
        }

        a.Target = target;
        a.Current = current;
        if (target is > 0 && current is not null)
        {
            if (current.Value >= target.Value)
            {
                // If Torn's achieved-award endpoint says this is NOT earned but a counter appears over target,
                // the mapping or trigger semantics are suspect. Never show fake 99.9% completion.
                a.ProgressPercent = null;
                a.ProgressConflict = true;
                a.ProgressSource = $"VERIFY — {source} reports {current.Value:N0}, which is at/above target {target.Value:N0}, but Torn did not return this award as achieved.";
            }
            else
            {
                a.ProgressPercent = Math.Clamp(current.Value * 100.0 / target.Value, 0, 100);
                a.ProgressSource = source;
            }
        }
        else
        {
            a.ProgressSource = "Requirement known; exact incomplete progress is not safely derivable from the current API data.";
        }
    }

    private static void ApplyAvailability(TornSnapshot s, MeritAward a)
    {
        a.AvailableForCurrentCrimeVersion = true;
        a.Availability = "Available";

        if (string.IsNullOrWhiteSpace(a.CrimesVersion) || string.IsNullOrWhiteSpace(s.CrimeVersion)) return;
        if (a.CrimesVersion.Equals(s.CrimeVersion, StringComparison.OrdinalIgnoreCase)) return;

        a.AvailableForCurrentCrimeVersion = false;
        a.Availability = $"LEGACY / wrong crime version — award is {a.CrimesVersion}, your account is {s.CrimeVersion}.";
    }

    private static bool TryResolveCrimeCategoryStat(MeritAward a, Dictionary<string, long> stats, out long value, out string source)
    {
        value = 0;
        source = "";
        if (!a.CrimesVersion.Equals("v2", StringComparison.OrdinalIgnoreCase)) return false;

        var t = (a.Name + " " + a.Description).ToLowerInvariant();
        string? suffix = null;
        if (t.Contains("criminal offense") || t.Contains("criminal offence")) suffix = "crimes.offenses.total";
        else if (t.Contains("vandalism")) suffix = "crimes.offenses.vandalism";
        else if (t.Contains("counterfeit")) suffix = "crimes.offenses.counterfeiting";
        else if (t.Contains("illicit service")) suffix = "crimes.offenses.illicit_services";
        else if (t.Contains("cybercrime")) suffix = "crimes.offenses.cybercrime";
        else if (t.Contains("extortion")) suffix = "crimes.offenses.extortion";
        else if (t.Contains("illegal production")) suffix = "crimes.offenses.illegal_production";
        else if (Regex.IsMatch(t, @"\bfraud\b")) suffix = "crimes.offenses.fraud";
        else if (Regex.IsMatch(t, @"\btheft\b")) suffix = "crimes.offenses.theft";

        if (suffix is null) return false;
        var found = FindExactStat(stats, suffix);
        if (found is null) return false;
        value = found.Value.Value;
        source = "Exact API Crimes 2.0 category counter: " + suffix;
        return true;
    }

    private static bool TryResolveExactPersonalStat(string description, Dictionary<string, long> stats, out long value, out string source)
    {
        value = 0;
        source = "";
        var d = description.Trim().ToLowerInvariant();
        string[]? suffixes = null;

        if (Regex.IsMatch(d, @"^win\s+[\d,]+\s+attacks?\b"))
            suffixes = new[] { "attacking.attacks.won", "attackswon" };
        else if (Regex.IsMatch(d, @"^win\s+[\d,]+\s+defends?\b") || Regex.IsMatch(d, @"defend\s+against\s+[\d,]+\s+attacks?"))
            suffixes = new[] { "attacking.defends.won", "defendswon" };
        else if (Regex.IsMatch(d, @"deal\s+[\d,]+\s+critical hits?"))
            suffixes = new[] { "attacking.hits.critical", "attackcriticalhits" };
        else if (Regex.IsMatch(d, @"fire\s+[\d,]+\s+rounds?"))
            suffixes = new[] { "attacking.ammunition.total", "roundsfired" };
        else if (Regex.IsMatch(d, @"collect\s+[\d,]+\s+bount"))
            suffixes = new[] { "bounties.collected.amount", "bountiescollected" };
        else if (Regex.IsMatch(d, @"bust\s+[\d,]+\s+people"))
            suffixes = new[] { "jail.busts.success", "busts" };
        else if (Regex.IsMatch(d, @"travel\s+abroad\s+[\d,]+\s+times?"))
            suffixes = new[] { "travel.total", "traveltimes" };
        else if (Regex.IsMatch(d, @"use\s+[\d,]+\s+medical items?"))
            suffixes = new[] { "hospital.medical_items_used", "medicalitemsused" };
        else if (Regex.IsMatch(d, @"perform\s+[\d,]+\s+revives?|revive\s+[\d,]+"))
            suffixes = new[] { "hospital.reviving.revives", "revives" };
        else if (Regex.IsMatch(d, @"win\s+[\d,]+\s+races?"))
            suffixes = new[] { "racing.wins", "raceswon" };

        if (suffixes is null) return false;
        var found = FindExactStat(stats, suffixes);
        if (found is null) return false;
        value = found.Value.Value;
        source = "Exact API personal stat: " + found.Value.Key.Split('.').Last();
        return true;
    }

    private static (string Key, long Value)? FindExactStat(Dictionary<string, long> stats, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            var n = Normalize(suffix);
            foreach (var pair in stats)
            {
                var key = Normalize(pair.Key);
                if (key.EndsWith(n, StringComparison.OrdinalIgnoreCase) || key.Equals(n, StringComparison.OrdinalIgnoreCase))
                    return (pair.Key, pair.Value);
            }
        }
        return null;
    }

    private static string DetectCrimeVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FindCrimeVersion(doc.RootElement) ?? "";
        }
        catch { return ""; }
    }

    private static string? FindCrimeVersion(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.Equals("crimes", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var cp in p.Value.EnumerateObject())
                        if (cp.Name.Equals("version", StringComparison.OrdinalIgnoreCase) && cp.Value.ValueKind == JsonValueKind.String)
                            return cp.Value.GetString();
                }
                var nested = FindCrimeVersion(p.Value);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in root.EnumerateArray())
            {
                var nested = FindCrimeVersion(e);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private static string NormalizeCrimeVersion(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return "";
        if (v.Contains("2")) return "v2";
        if (v.Contains("1")) return "v1";
        return v;
    }

    private static bool IsReferralOrExternalLevelAward(string text)
        => text.Contains("refer") || text.Contains("referral") || text.Contains("facebook torn")
           || text.Contains("torn lite") || text.Contains("your referral") || text.Contains("friend reaches level")
           || text.Contains("player reaches level");

    private static bool IsOwnLevelAward(MeritAward a)
    {
        var d = a.Description.Trim().ToLowerInvariant();
        if (d.Contains("facebook torn") || d.Contains("torn lite") || d.Contains("refer") || d.Contains("referral")) return false;
        return Regex.IsMatch(d, @"^(reach|achieve|attain)\s+(?:a\s+)?level\s+\d+")
               || Regex.IsMatch(d, @"^reach\s+level\s+\d+");
    }

    private static int? ExtractLikelyTarget(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var matches = Regex.Matches(description, @"(?<![A-Za-z])([0-9][0-9,]*)");
        if (matches.Count == 0) return null;
        var values = new List<int>();
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Groups[1].Value.Replace(",", ""), out var n) && n > 0)
                values.Add(n);
        }
        return values.Count == 0 ? null : values.Max();
    }

    private static int ScoreEase(TornSnapshot s, MeritAward a)
    {
        if (a.Achieved) return 0;
        if (!a.AvailableForCurrentCrimeVersion) return 0;
        if (a.ProgressConflict) return 12;
        var text = (a.Name + " " + a.Description + " " + a.Category).ToLowerInvariant();
        if (text.Contains("no longer obtainable") || text.Contains("legacy") || text.Contains("default honor") || text.Contains("default honour")) return 1;

        // These are valid awards, but they are not "do it in five minutes" tasks.
        // Do not let small numbers in their descriptions make them jump to the top.
        if (text.Contains("refer") || text.Contains("referral")) return 24;
        if (a.Name.Equals("The Socialist", StringComparison.OrdinalIgnoreCase))
            return (s.Level ?? 0) < 20 ? 8 : 58;

        var score = 45;
        if (KnownEasy.Contains(a.Name)) score = 96;
        if (EasyWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))) score += 18;
        if (HardWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase))) score -= 25;

        if (a.ProgressPercent is double progress)
        {
            score = Math.Max(score, 40 + (int)Math.Round(progress * 0.55));
            if (progress >= 95) score += 8;
            else if (progress >= 80) score += 5;
        }

        if (a.Target is int target)
        {
            score += target switch
            {
                <= 1 => 20,
                <= 5 => 16,
                <= 10 => 12,
                <= 50 => 8,
                <= 100 => 4,
                >= 100000 => -18,
                >= 10000 => -12,
                >= 1000 => -6,
                _ => 0
            };
        }

        if (a.Category is "Crime" or "Account" or "Money") score += 3;
        return Math.Clamp(score, 1, 100);
    }

    private static string EffortLabel(int score, bool achieved)
    {
        if (achieved) return "DONE";
        if (score >= 90) return "DO NOW";
        if (score >= 75) return "EASY";
        if (score >= 60) return "SOON";
        if (score >= 45) return "NORMAL";
        if (score >= 20) return "LONG";
        return "LOW PRIORITY";
    }

    private static string BuildAdvice(TornSnapshot s, MeritAward a)
    {
        if (a.Achieved) return "Already earned.";
        if (!a.AvailableForCurrentCrimeVersion)
            return $"NOT IN YOUR ACTIVE MERIT QUEUE: {a.Availability} This is a legacy/other crime-version award and Commander will not tell you to grind it.";
        if (a.ProgressConflict)
            return $"VERIFY, DO NOT TRUST THIS COUNTER YET: {a.ProgressSource} Commander removed it from the easy/do-now queue instead of pretending it is 99.9%.";

        if (a.Name.Equals("Zero Liability", StringComparison.OrdinalIgnoreCase))
            return "CARD SKIMMING: let ONE placed skimmer accumulate at least 250 card details, then Recover that skimmer. The 250 must be on the same skimmer; do not recover it early.";

        if (a.Name.Equals("Social Butterfly", StringComparison.OrdinalIgnoreCase))
            return "REFERRALS: personally refer 3 players and have all 3 reach level 10. Your own level does not count toward this award.";

        if (a.Name.Equals("Pyramid Scheme", StringComparison.OrdinalIgnoreCase))
            return "REFERRAL CHAIN: one of YOUR referrals must refer another player, and that second-generation referral must reach level 10. Your own level does not count.";

        if (a.Name.Equals("The Socialist", StringComparison.OrdinalIgnoreCase))
        {
            if ((s.Level ?? 0) < 20)
                return $"LOCKED FOR NOW: the old Torn Lite/Facebook route is retired. At level 20, buy the 'The Socialist' honor from the Points Building for 600 points. Current level: {s.Level ?? 0}.";
            return "POINTS BUILDING: the old Torn Lite/Facebook route is retired. Buy the 'The Socialist' honor for 600 points; purchasing the honor awards its merit.";
        }

        if (a.ProgressPercent is double pct && a.Current is long cur && a.Target is int target)
            return $"Requirement: {a.Description}  Tracked progress: {pct:0.#}% ({cur:N0}/{target:N0}) from {a.ProgressSource}.";

        if (!string.IsNullOrWhiteSpace(a.Description))
            return $"HOW TO EARN: {a.Description}";

        return "Requirement text was not returned by Torn's catalog. Open Awards and inspect this award before spending resources on it.";
    }

    private static string InferCategory(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("crime") || t.Contains("graffiti") || t.Contains("skimmer") || t.Contains("pickpocket") || t.Contains("burgl")) return "Crime";
        if (t.Contains("attack") || t.Contains("weapon") || t.Contains("mug") || t.Contains("hospital")) return "Combat";
        if (t.Contains("race")) return "Racing";
        if (t.Contains("casino") || t.Contains("blackjack") || t.Contains("poker") || t.Contains("slot")) return "Casino";
        if (t.Contains("travel") || t.Contains("country") || t.Contains("flight")) return "Travel";
        if (t.Contains("money") || t.Contains("bank") || t.Contains("donat") || t.Contains("networth")) return "Money";
        if (t.Contains("education") || t.Contains("course")) return "Education";
        if (t.Contains("gym") || t.Contains("strength") || t.Contains("speed") || t.Contains("defen") || t.Contains("dexterity")) return "Battle stats";
        if (t.Contains("company") || t.Contains("job") || t.Contains("work")) return "Work";
        return "Account";
    }

    private static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");

    private static bool TryFindArray(JsonElement root, string name, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                {
                    array = p.Value;
                    return true;
                }
                if (TryFindArray(p.Value, name, out array)) return true;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in root.EnumerateArray()) if (TryFindArray(x, name, out array)) return true;
        }
        array = default;
        return false;
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement root)
    {
        yield return root;
        if (root.ValueKind == JsonValueKind.Object)
            foreach (var p in root.EnumerateObject()) foreach (var x in Enumerate(p.Value)) yield return x;
        else if (root.ValueKind == JsonValueKind.Array)
            foreach (var e in root.EnumerateArray()) foreach (var x in Enumerate(e)) yield return x;
    }

    private static int? ReadNestedInt(JsonElement obj, string objectName, string valueName)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.Equals(objectName, StringComparison.OrdinalIgnoreCase) || p.Value.ValueKind != JsonValueKind.Object) continue;
            return ReadInt(p.Value, valueName);
        }
        return null;
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        return null;
    }

    private static int? ReadInt(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)) return n;
            if (p.Value.ValueKind == JsonValueKind.String && int.TryParse(p.Value.GetString(), out n)) return n;
        }
        return null;
    }
}
