using System.Text.Json;

namespace TornCommander;

internal sealed class FactionMemberScan
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Position { get; set; } = "";
    public int? Level { get; set; }
    public int? DaysInFaction { get; set; }
    public long? LastAction { get; set; }
    public string Activity { get; set; } = "";
    public string Status { get; set; } = "";
    public bool? InCrime { get; set; }
}
internal sealed record FactionCrimeSlotScan(string Position, long? UserId, int? PassRate, bool? ItemAvailable, double? Progress);
internal sealed record FactionCrimeScan(long Id, string Name, string Status, int? Difficulty,
    long? ReadyAt, long? ExpiresAt, List<FactionCrimeSlotScan> Slots);
internal sealed record FactionWarSide(long Id, string Name, long? Score);
internal sealed record FactionWarScan(long Id, string Kind, long? Start, long? End, List<FactionWarSide> Sides);
internal sealed record FactionUpgradeScan(string Branch, string Name, string Ability, int? Level);
internal sealed class FactionSnapshot
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Rank { get; set; } = "";
    public long? LeaderId { get; set; }
    public long? CoLeaderId { get; set; }
    public long? ViewerId { get; set; }
    public string ViewerPosition { get; set; } = "";
    public bool AbilitiesLoaded { get; set; }
    public List<string> Abilities { get; set; } = new();
    public long? Respect { get; set; }
    public int? Count { get; set; }
    public int? Capacity { get; set; }
    public bool? Enlisted { get; set; }
    public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? MembersUtc { get; set; }
    public bool MembersLoaded { get; set; }
    public bool ChainLoaded { get; set; }
    public bool WarsLoaded { get; set; }
    public bool CrimesLoaded { get; set; }
    public bool UpgradesLoaded { get; set; }
    public bool CrimesTruncated { get; set; }
    public long? ChainCount { get; set; }
    public long? ChainMax { get; set; }
    public long? ChainDeadline { get; set; }
    public List<FactionMemberScan> Members { get; set; } = new();
    public List<FactionCrimeScan> Crimes { get; set; } = new();
    public List<FactionWarScan> Wars { get; set; } = new();
    public List<FactionUpgradeScan> Upgrades { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
internal sealed record FactionDay(string Day, long FactionId, long? Respect, int? Members);
internal sealed class FactionTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long FactionId { get; set; }
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}
internal sealed class FactionJournal
{
    public FactionSnapshot? Snapshot { get; set; }
    public string Goal { get; set; } = "balanced";
    public int InactivityDays { get; set; } = 3;
    public List<FactionDay> Days { get; set; } = new();
    public List<FactionTask> Tasks { get; set; } = new();
    public string Status { get; set; } = "Scan your faction to load its leadership overview.";
}
internal static class FactionIntelligence
{
    private static JsonElement Field(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v : default;
    private static string Text(JsonElement e, string n) => Field(e, n).ValueKind == JsonValueKind.String ? Field(e, n).GetString() ?? "" : "";
    private static long? Num(JsonElement e, string n)
    {
        var v = Field(e, n);
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var value) && value >= 0 ? value : null;
    }
    private static int? Int(JsonElement e, string n) => Num(e, n) is >= 0 and <= int.MaxValue ? (int?)Num(e, n)!.Value : null;
    private static bool? Bool(JsonElement e, string n) => Field(e, n).ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
    private static List<JsonElement> Array(JsonElement root, string n)
    {
        var a = Field(root, n);
        if (a.ValueKind != JsonValueKind.Array) throw new InvalidDataException(n + " section unavailable for this API response.");
        return a.EnumerateArray().ToList();
    }
    public static FactionSnapshot ReadBasic(string json)
    {
        using var doc = JsonDocument.Parse(json); var b = Field(doc.RootElement, "basic");
        var id = Num(b, "id");
        if (id is not > 0) throw new InvalidDataException("No faction identity returned. Check membership and API access.");
        return new() { Id = id.Value, Name = Text(b, "name"), Rank = Text(Field(b, "rank"), "name"),
            LeaderId = Num(b, "leader_id"), CoLeaderId = Num(b, "co_leader_id"),
            Respect = Num(b, "respect"), Count = Int(b, "members"), Capacity = Int(b, "capacity"), Enlisted = Bool(b, "is_enlisted") };
    }
    public static long? AffiliationId(string json)
    {
        using var doc = JsonDocument.Parse(json); var f = Field(doc.RootElement, "faction");
        if (f.ValueKind == JsonValueKind.Null) return null;
        var id = Num(f, "id");
        if (id is not > 0) throw new InvalidDataException("Account faction membership was not returned.");
        return id;
    }
    public static void ReadViewer(FactionSnapshot s, string userJson, string factionJson)
    {
        using var user = JsonDocument.Parse(userJson); using var faction = JsonDocument.Parse(factionJson);
        var member = Field(faction.RootElement, "faction");
        if (Num(member, "id") != s.Id) throw new InvalidDataException("Account faction identity changed during scan.");
        s.ViewerId = Num(Field(user.RootElement, "profile"), "id");
        if (s.ViewerId is not > 0) throw new InvalidDataException("Account identity unavailable; role detection withheld.");
        s.ViewerPosition = Text(member, "position");
    }
    public static void ReadPositions(FactionSnapshot s, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var position = Array(doc.RootElement, "positions").FirstOrDefault(x =>
            Text(x, "name").Equals(s.ViewerPosition, StringComparison.OrdinalIgnoreCase));
        if (position.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Your position was not matched to the faction permission list.");
        s.Abilities = Array(position, "abilities").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
        s.AbilitiesLoaded = true;
    }
    public static string Role(FactionSnapshot? s)
    {
        if (s?.ViewerId is not > 0) return "Unknown";
        if (s.ViewerId == s.LeaderId) return "Leader";
        if (s.ViewerId == s.CoLeaderId) return "Co-leader";
        var officer = new[] { "Organised Crimes", "War Management", "Upgrade Management", "Application Management",
            "Kick Members", "Money Giving", "Balance Adjustment", "Newsletter Sending", "Announcement Changes",
            "Item Giving", "Points Giving", "Forum Management" };
        return s.AbilitiesLoaded && s.Abilities.Any(x => officer.Contains(x, StringComparer.OrdinalIgnoreCase))
            ? "Officer" : "Member";
    }
    public static List<string> PersonalAdvice(FactionJournal journal, DateTime now)
    {
        var s = journal.Snapshot; var result = new List<string>();
        if (s == null) return new() { "Scan your faction to detect your own position and participation." };
        var me = s.Members.FirstOrDefault(x => x.Id == s.ViewerId);
        if (me != null)
        {
            result.Add("Your position: " + s.ViewerPosition + ". Current roster status: " + (string.IsNullOrEmpty(me.Status) ? "unknown" : me.Status) + ".");
            if (me.InCrime == false) result.Add("You are not marked as assigned to an organized crime. Review recruiting slots and your own CPR, items and availability in Torn.");
            else if (me.InCrime == true) result.Add("You are assigned to an organized crime. Review your listed slot progress and required item before planning travel or faction events.");
        }
        else result.Add("Your roster entry is unavailable; personal participation could not be verified.");
        foreach (var crime in s.Crimes.Where(c => c.Slots.Any(x => x.UserId == s.ViewerId)))
            result.Add("Your organized crime: " + crime.Name + " (" + crime.Status + "). Review the slot details below; a ready time alone does not guarantee all participants are ready.");
        if (!s.AbilitiesLoaded && Role(s) == "Member")
            result.Add("Position permissions were not exposed. Your reported title is shown, but officer responsibilities cannot be inferred from a custom rank name. All available panels remain accessible.");
        result.Add("Use the local mission checklist for your agent objectives or leadership tasks. Personal mission-agent objectives are not imported by this faction scan.");
        return result;
    }

    public static void ReadMembers(FactionSnapshot s, string json, DateTime now)
    {
        using var doc = JsonDocument.Parse(json);
        var members = Array(doc.RootElement, "members").Select(m => new FactionMemberScan {
            Id = Num(m, "id") ?? 0, Name = Text(m, "name"), Position = Text(m, "position"),
            Level = Int(m, "level"), DaysInFaction = Int(m, "days_in_faction"),
            LastAction = Num(Field(m, "last_action"), "timestamp"), Activity = Text(Field(m, "last_action"), "status"),
            Status = Text(Field(m, "status"), "state"), InCrime = Bool(m, "is_in_oc")
        }).ToList();
        if (members.Count > 200 || members.Any(x => x.Id <= 0) || members.Select(x => x.Id).Distinct().Count() != members.Count)
            throw new InvalidDataException("Invalid or duplicated faction member IDs.");
        s.Members = members; s.MembersLoaded = true; s.MembersUtc = now;
    }
    public static void ReadChain(FactionSnapshot s, string json, DateTime now)
    {
        using var doc = JsonDocument.Parse(json); var chain = Field(doc.RootElement, "chain");
        if (chain.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Chain section unavailable.");
        s.ChainCount = Num(chain, "current"); s.ChainMax = Num(chain, "max");
        var timeout = Num(chain, "timeout");
        s.ChainDeadline = timeout is >= 0 and <= 86400 && s.ChainCount > 0
            ? new DateTimeOffset(now).ToUnixTimeSeconds() + timeout : null;
        s.ChainLoaded = true;
    }
    public static void ReadWars(FactionSnapshot s, string json)
    {
        using var doc = JsonDocument.Parse(json); var wars = Field(doc.RootElement, "wars");
        if (wars.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Wars section unavailable.");
        var rows = new List<FactionWarScan>();
        void Add(JsonElement w, string kind) {
            if (Num(w, "war_id") is not > 0) return;
            var sides = Array(w, "factions").Select(x => new FactionWarSide(Num(x, "id") ?? 0, Text(x, "name"), Num(x, "score"))).ToList();
            rows.Add(new(Num(w, "war_id")!.Value, kind, Num(w, "start"), Num(w, "end"), sides));
        }
        var ranked = Field(wars, "ranked"); if (ranked.ValueKind == JsonValueKind.Object) Add(ranked, "Ranked war");
        foreach (var w in Array(wars, "raids")) Add(w, "Raid");
        foreach (var w in Array(wars, "territory")) Add(w, "Territory war");
        s.Wars = rows; s.WarsLoaded = true;
    }
    public static List<FactionCrimeScan> ReadCrimes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var crimes = Array(doc.RootElement, "crimes").Select(c => new FactionCrimeScan(
            Num(c, "id") ?? 0, Text(c, "name"), Text(c, "status"), Int(c, "difficulty"), Num(c, "ready_at"), Num(c, "expired_at"),
            Array(c, "slots").Select(slot => {
                var u = Field(slot, "user"); var progress = Field(u, "progress");
                return new FactionCrimeSlotScan(Text(slot, "position"),
                    Num(u, "id"), Int(slot, "checkpoint_pass_rate"), Bool(Field(slot, "item_requirement"), "is_available"),
                    progress.ValueKind == JsonValueKind.Number && progress.TryGetDouble(out var n) && double.IsFinite(n) ? (double?)n : null);
            }).ToList())).ToList();
        if (crimes.Any(x => x.Id <= 0 || x.Slots.Any(slot => slot.UserId == 0)) ||
            crimes.Select(x => x.Id).Distinct().Count() != crimes.Count)
            throw new InvalidDataException("Invalid or duplicated organized crime identities.");
        return crimes;
    }
    public static void ReadUpgrades(FactionSnapshot s, string json)
    {
        using var doc = JsonDocument.Parse(json); var upgrades = Field(doc.RootElement, "upgrades");
        if (upgrades.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Upgrade section unavailable.");
        var rows = new List<FactionUpgradeScan>();
        void Add(JsonElement branch, string name) {
            foreach (var u in Array(branch, "upgrades"))
                rows.Add(new(name, Text(u, "name"), Text(u, "ability"), Int(u, "level")));
        }
        Add(Field(upgrades, "core"), "Core");
        foreach (var group in new[] { "peace", "war" })
            foreach (var b in Array(upgrades, group)) Add(b, group + " / " + Text(b, "name"));
        s.Upgrades = rows; s.UpgradesLoaded = true;
    }
    public static double? IdleDays(FactionMemberScan m, DateTime now)
    {
        if (!m.LastAction.HasValue || m.LastAction <= 0) return null;
        var seconds = new DateTimeOffset(now).ToUnixTimeSeconds() - m.LastAction.Value;
        return seconds < -300 ? null : Math.Max(0, seconds) / 86400.0;
    }
    public static void SaveDay(FactionJournal journal, FactionSnapshot fresh)
    {
        string day = fresh.ScannedUtc.ToString("yyyy-MM-dd");
        journal.Days.RemoveAll(x => x.FactionId == fresh.Id && x.Day == day);
        journal.Days.Add(new(day, fresh.Id, fresh.Respect, fresh.Count));
        journal.Days = journal.Days.OrderByDescending(x => x.Day, StringComparer.Ordinal).Take(365).ToList();
        journal.Snapshot = fresh;
    }
    public static List<string> Advice(FactionJournal journal, DateTime now)
    {
        var s = journal.Snapshot; var result = new List<string>();
        if (s == null) return new() { "Scan the faction first. Leadership advice uses the data this key can actually access." };
        double age = (now - s.ScannedUtc).TotalMinutes;
        if (age > 15) result.Add("This faction scan is over 15 minutes old. Refresh before acting on chain, war or member-presence information.");
        if (s.ChainCount > 0)
            result.Add("A chain of " + s.ChainCount + " hits was recorded. Refresh in Torn and appoint coverage before the next hit deadline; this saved scan is not a live chain monitor.");
        long unix = new DateTimeOffset(now).ToUnixTimeSeconds();
        if (s.WarsLoaded && s.Wars.Any(x => x.End == null || x.End == 0 || x.End > unix))
            result.Add("Review the listed active/upcoming wars: confirm attendance, medical supplies and agreed leadership roles. Member level and online status do not establish combat strength.");
        if (s.MembersLoaded && s.Count != s.Members.Count) result.Add("The member count and roster disagree. Refresh before drawing whole-faction participation conclusions.");
        var inactive = s.Members.Where(m => IdleDays(m, now) >= journal.InactivityDays).ToList();
        if (s.MembersLoaded && inactive.Count > 0)
            result.Add(inactive.Count + " members exceed your " + journal.InactivityDays + "-day inactivity review threshold. Check availability and agreed expectations; do not automatically remove them.");
        if (s.Capacity.HasValue && s.Count.HasValue && s.Capacity > s.Count)
            result.Add((s.Capacity - s.Count) + " open member places. Recruit for your faction's actual schedule and goals before buying more capacity.");
        if (s.CrimesLoaded)
        {
            int vacancies = s.Crimes.Sum(c => c.Slots.Count(x => x.UserId == null));
            if (vacancies > 0) result.Add(vacancies + " open slots in the returned organized crimes. Review the unassigned roster and slot requirements; empty-slot CPR belongs to the requesting account, not every possible recruit.");
            int missingItems = s.Crimes.Sum(c => c.Slots.Count(x => x.UserId != null && x.ItemAvailable == false));
            if (missingItems > 0) result.Add(missingItems + " assigned crime slots report a missing item. Coordinate the item before assuming planning can proceed.");
            int ready = s.Crimes.Count(c => c.Status == "Planning" && c.ReadyAt.HasValue && c.ReadyAt > 0 && c.ReadyAt <= unix);
            if (ready > 0) result.Add(ready + " planning crimes have reached the returned ready time. Check every slot's progress, required items and current member status in Torn; the timestamp alone does not guarantee readiness.");
            if (s.CrimesTruncated) result.Add("Organized crimes are a capped partial list. Do not interpret missing crimes as absent.");
        }
        else result.Add("Organized-crime details are unavailable. Leader/co-leader status alone does not ensure the saved key has faction API access.");
        result.Add(journal.Goal switch {
            "war" => "War focus: agree attendance windows and supply limits, review current wars and chain coverage, then assess outcomes before expanding commitments.",
            "crimes" => "Crime focus: fill suitable recruiting slots, remove item/planning blockers, and track completed outcomes before increasing difficulty. This scan cannot rank every member's unjoined-slot success chance.",
            _ => "Growth focus: establish reliable participation and organized-crime routines, then review installed perks with members' training/crime goals. Reported respect is not a verified spare upgrade budget."
        });
        if (s.UpgradesLoaded) result.Add("Review the installed upgrade list against your chosen focus. The API levels shown are current; next-level costs, unlocks and the best spending order are not inferred.");
        return result;
    }
}
