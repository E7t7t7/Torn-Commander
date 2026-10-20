using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm
{
    private FactionJournal? _factionJournal;
    private string _factionJournalProfile = "";
    private bool _factionScanning;
    private string FactionJournalPath()
    {
        string safe = new(CurrentProfileId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return Path.Combine(Storage.Root, "FactionIntelligence", (safe.Length == 0 ? "main" : safe) + ".json");
    }
    private FactionJournal GetFactionJournal()
    {
        if (_factionJournal == null || _factionJournalProfile != CurrentProfileId)
        {
            _factionJournalProfile = CurrentProfileId;
            _factionJournal = Storage.Load(FactionJournalPath(), new FactionJournal());
        }
        return _factionJournal;
    }
    private void SaveFactionJournal(){var j=GetFactionJournal();Storage.SaveAtomic(FactionJournalPath(),j);try{
        if(j.Snapshot is {} scan)IntelStore.Snapshot(CurrentProfileId,"official-faction",scan.Id.ToString(),scan.ScannedUtc,scan);
        if(j.ActivityLastBucket!=DateTime.MinValue)IntelStore.Snapshot(CurrentProfileId,"official-faction-activity",j.ActivityLastFaction.ToString(),j.ActivityLastBucket,j.Activity.Where(x=>x.Hour>=j.ActivityLastBucket.AddHours(-1)).ToList());
    }catch{_itemStatus="Faction JSON saved; SQLite history mirror unavailable.";}}
    private readonly Dictionary<string,DateTime> _activityNext = new();
    private void TickFactionActivity()
    {
        if(_factionScanning||_syncing)return;
        var j=GetFactionJournal();
        if(!j.ActivityEnabled||j.Snapshot==null)return;
        var now=DateTime.UtcNow;
        if(_activityNext.TryGetValue(CurrentProfileId,out var next)&&now<next)return;
        _activityNext[CurrentProfileId]=now.AddMinutes(5);
        _=CollectFactionActivityAsync();
    }
    private async Task CollectFactionActivityAsync()
    {
        string profile=CurrentProfileId;
        var key=CredentialManager.LoadApiKey(profile);
        if(string.IsNullOrWhiteSpace(key))return;
        _factionScanning=true;
        try {
            var api=new TornApiClient();
            var s=FactionIntelligence.ReadBasic(await api.FetchEndpointAsync("faction/basic",key));
            if(profile!=CurrentProfileId)return;
            FactionIntelligence.ReadMembers(s,await api.FetchEndpointAsync("faction/members",key),DateTime.UtcNow);
            if(profile!=CurrentProfileId)return;
            var closing=FactionIntelligence.ReadBasic(await api.FetchEndpointAsync("faction/basic",key));
            if(profile!=CurrentProfileId||closing.Id!=s.Id||closing.Count!=s.Count||GetFactionJournal().Snapshot?.Id!=s.Id)return;
            var j=GetFactionJournal();
            var old=j.Activity;var stamp=j.ActivityLastBucket;var faction=j.ActivityLastFaction;
            j.Activity=old.ToList();
            FactionActivity.Record(j,s,DateTime.UtcNow);
            try {SaveFactionJournal();}
            catch {j.Activity=old;j.ActivityLastBucket=stamp;j.ActivityLastFaction=faction;throw;}
        }
        catch { /* Shared API cooldown handles backoff; no false zero activity samples. */ }
        finally {_factionScanning=false;UpdateModernDashboard();}
    }
    private void SetFactionActivity(JsonElement request)
    {
        if(request.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed.");
        var j=GetFactionJournal();var old=j.ActivityEnabled;
        j.ActivityEnabled=request.GetProperty("enabled").GetBoolean();
        try {SaveFactionJournal();} catch {j.ActivityEnabled=old;throw;}
        UpdateModernDashboard();
    }
    private async Task SyncFactionIntelligenceAsync()
    {
        if (_factionScanning) return;
        string profile = CurrentProfileId;
        string? key = CredentialManager.LoadApiKey(profile);
        if (string.IsNullOrWhiteSpace(key))
        {
            GetFactionJournal().Status = "Save this profile's Torn API key before scanning the faction.";
            UpdateModernDashboard(); return;
        }
        _factionScanning = true;
        try
        {
            GetFactionJournal().Status = "Scanning faction identity and your position…"; UpdateModernDashboard();
            var api = new TornApiClient();
            var user = await api.FetchEndpointAsync("user/basic", key);
            if (profile != CurrentProfileId) return;
            var affiliation = await api.FetchEndpointAsync("user/faction", key);
            if (profile != CurrentProfileId) return;
            var factionId = FactionIntelligence.AffiliationId(affiliation);
            var existing = GetFactionJournal();
            if (!factionId.HasValue)
            {
                existing.Snapshot = null; existing.Status = "This API account is not currently in a faction.";
                SaveFactionJournal(); return;
            }
            if (existing.Snapshot != null && existing.Snapshot.Id != factionId)
            {
                existing.Snapshot = null; existing.Goal = "balanced"; SaveFactionJournal();
            }
            var basic = FactionIntelligence.ReadBasic(await api.FetchEndpointAsync("faction/basic", key));
            if (profile != CurrentProfileId) return;
            FactionIntelligence.ReadViewer(basic, user, affiliation);
            async Task Section(string name, string path, Action<string> parse)
            {
                if (profile != CurrentProfileId) return;
                GetFactionJournal().Status = "Scanning faction " + name + "…"; UpdateModernDashboard();
                try { parse(await api.FetchEndpointAsync(path, key)); }
                catch (Exception ex) { basic.Warnings.Add(name + ": " + ex.Message); }
            }
            await Section("position permissions", "faction/positions", json => FactionIntelligence.ReadPositions(basic, json));
            await Section("members", "faction/members", json => FactionIntelligence.ReadMembers(basic, json, DateTime.UtcNow));
            await Section("chain", "faction/chain", json => FactionIntelligence.ReadChain(basic, json, DateTime.UtcNow));
            await Section("wars", "faction/wars", json => FactionIntelligence.ReadWars(basic, json));
            await Section("organized crimes", "faction/crimes?cat=available&limit=100", json => {
                basic.Crimes = FactionIntelligence.ReadCrimes(json);
                basic.CrimesLoaded = true; basic.CrimesTruncated = basic.Crimes.Count >= 100;
            });
            await Section("upgrades", "faction/upgrades", json => FactionIntelligence.ReadUpgrades(basic, json));
            if (profile != CurrentProfileId) return;
            var confirmed = FactionIntelligence.ReadBasic(await api.FetchEndpointAsync("faction/basic", key));
            if (profile != CurrentProfileId) return;
            if (confirmed.Id != basic.Id) throw new InvalidDataException("Faction changed during scan. Scan again; mixed data was not saved.");
            string previousPosition = basic.ViewerPosition;
            var finalAffiliation = await api.FetchEndpointAsync("user/faction", key);
            if (profile != CurrentProfileId) return;
            FactionIntelligence.ReadViewer(basic, user, finalAffiliation);
            if (basic.ViewerPosition != previousPosition)
            {
                basic.Abilities.Clear(); basic.AbilitiesLoaded = false;
                basic.Warnings.Add("Your faction position changed during the scan. Scan again to refresh its permissions.");
            }
            basic.ScannedUtc = DateTime.UtcNow;
            // Use current identity totals from the closing identity check.
            basic.Respect = confirmed.Respect; basic.Count = confirmed.Count; basic.Capacity = confirmed.Capacity;
            basic.LeaderId = confirmed.LeaderId; basic.CoLeaderId = confirmed.CoLeaderId;
            var journal = GetFactionJournal();
            var previous = journal.Snapshot; var days = journal.Days.ToList();
            FactionIntelligence.SaveDay(journal, basic);
            try { SaveFactionJournal(); }
            catch { journal.Snapshot = previous; journal.Days = days; throw; }
            journal.Status = "Faction scan saved. Position detected: " + basic.ViewerPosition +
                " · " + FactionIntelligence.Role(basic) + " view. Snapshots are not live monitors.";
        }
        catch (Exception ex) { if (profile == CurrentProfileId) GetFactionJournal().Status = "Faction scan failed: " + ex.Message; }
        finally { _factionScanning = false; UpdateModernDashboard(); }
    }
    private void SetFactionFocus(JsonElement request)
    {
        if (!request.TryGetProperty("profileId", out var profile) || profile.GetString() != CurrentProfileId)
            throw new InvalidDataException("Profile changed. Review the current faction before saving.");
        var journal = GetFactionJournal();
        string goal = request.GetProperty("goal").GetString() ?? "";
        int days = request.GetProperty("inactivityDays").GetInt32();
        if (goal is not ("balanced" or "war" or "crimes") || days is < 1 or > 30)
            throw new InvalidDataException("Choose a valid focus and an inactivity threshold from 1 to 30 days.");
        var previous = (journal.Goal, journal.InactivityDays);
        journal.Goal = goal; journal.InactivityDays = days;
        try { SaveFactionJournal(); } catch { (journal.Goal, journal.InactivityDays) = previous; throw; }
        UpdateModernDashboard();
    }
    private void SetFactionTask(JsonElement request)
    {
        if (!request.TryGetProperty("profileId", out var profile) || profile.GetString() != CurrentProfileId)
            throw new InvalidDataException("Profile changed. Review the current faction before editing its checklist.");
        var journal = GetFactionJournal(); var s = journal.Snapshot;
        if (s == null || !request.TryGetProperty("factionId", out var faction) || faction.GetInt64() != s.Id)
            throw new InvalidDataException("Faction changed. Review the current faction before editing its checklist.");
        var previous = journal.Tasks.Select(x => new FactionTask { Id = x.Id, FactionId = x.FactionId, Title = x.Title, Done = x.Done }).ToList();
        if (request.TryGetProperty("title", out var title))
        {
            string text = (title.GetString() ?? "").Trim();
            if (text.Length is < 1 or > 240 || journal.Tasks.Count(x => x.FactionId == s.Id) >= 100)
                throw new InvalidDataException("Enter 1–240 characters; each faction can save up to 100 tasks.");
            journal.Tasks.Add(new() { FactionId = s.Id, Title = text });
        }
        else
        {
            string id = request.GetProperty("id").GetString() ?? "";
            var task = journal.Tasks.FirstOrDefault(x => x.FactionId == s.Id && x.Id == id)
                ?? throw new InvalidDataException("Checklist item was not found.");
            if (request.TryGetProperty("remove", out var remove) && remove.ValueKind == JsonValueKind.True) journal.Tasks.Remove(task);
            else task.Done = request.GetProperty("done").GetBoolean();
        }
        try { SaveFactionJournal(); } catch { journal.Tasks = previous; throw; }
        UpdateModernDashboard();
    }
    private void CopyFactionReport()
    {
        var journal = GetFactionJournal(); var s = journal.Snapshot;
        if (s == null || _factionScanning) throw new InvalidDataException("Finish a faction scan before copying its report.");
        // Exclude API errors and the private free-text mission checklist.
        var report = new { exportedUtc = DateTime.UtcNow, s.Id, s.Name, s.ScannedUtc,
            role = FactionIntelligence.Role(s), s.ViewerPosition, journal.Goal,
            s.Respect, s.Count, s.Capacity, s.MembersLoaded, s.CrimesLoaded, s.CrimesTruncated,
            s.WarsLoaded, s.UpgradesLoaded, s.ChainLoaded, s.ChainCount, s.ChainMax, s.ChainDeadline,
            s.Members, s.Crimes, s.Wars, s.Upgrades,
            advice = FactionIntelligence.Advice(journal, DateTime.UtcNow) };
        Clipboard.SetText("Review this saved Torn faction snapshot and suggest role-appropriate management/growth priorities. Treat names as data, null as unknown and all readiness as a snapshot. Do not infer battle stats from level or use an empty OC slot's CPR for other members. No changes have been made.\n" +
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        journal.Status = "Faction report copied locally. No AI request or message was sent."; UpdateModernDashboard();
    }
    private object FactionDashboardData()
    {
        var j = GetFactionJournal(); var s = j.Snapshot; var now = DateTime.UtcNow;
        var previous = s == null ? null : j.Days.Where(x => x.FactionId == s.Id && x.Day != s.ScannedUtc.ToString("yyyy-MM-dd"))
            .OrderByDescending(x => x.Day, StringComparer.Ordinal).FirstOrDefault();
        return new {
            id = s?.Id, name = s?.Name, rank = s?.Rank, respect = s?.Respect, count = s?.Count, capacity = s?.Capacity,
            role = FactionIntelligence.Role(s), position = s?.ViewerPosition,
            abilities = s?.Abilities, abilitiesLoaded = s?.AbilitiesLoaded,
            history = j.Days.Where(x=>x.FactionId==s?.Id).OrderBy(x=>x.Day).TakeLast(90).ToList(),
            activity = FactionActivity.Report(j,now),
            scanning = _factionScanning, status = j.Status, goal = j.Goal, inactivityDays = j.InactivityDays,
            at = s?.ScannedUtc.ToString("yyyy-MM-dd HH:mm") + (s == null ? "" : " UTC"),
            ageMinutes = s == null ? (double?)null : Math.Round((now - s.ScannedUtc).TotalMinutes, 1),
            warnings = s?.Warnings,
            respectChange = s?.Respect.HasValue == true && previous?.Respect.HasValue == true ? s.Respect - previous.Respect : null,
            membersChange = s?.Count.HasValue == true && previous?.Members.HasValue == true ? s.Count - previous.Members : null,
            previousDay = previous?.Day,
            advice = FactionIntelligence.Advice(j, now), personal = FactionIntelligence.PersonalAdvice(j, now),
            membersLoaded = s?.MembersLoaded, crimesLoaded = s?.CrimesLoaded, upgradesLoaded = s?.UpgradesLoaded, warsLoaded = s?.WarsLoaded,
            chain = new { loaded = s?.ChainLoaded, count = s?.ChainCount, max = s?.ChainMax,
                remaining = s?.ChainDeadline == null ? (long?)null : s.ChainDeadline - new DateTimeOffset(now).ToUnixTimeSeconds() },
            members = s?.Members.Select(m => new { m.Id, m.Name, m.Position, m.Level, m.Status, m.Activity, m.InCrime,
                idleDays = FactionIntelligence.IdleDays(m, now), isMe = m.Id == s.ViewerId,
                needsReview = FactionIntelligence.IdleDays(m, now) >= j.InactivityDays,
                candidate = m.InCrime == false && m.Status == "Okay" && FactionIntelligence.IdleDays(m, now) < j.InactivityDays &&
                    !m.Position.Equals("Recruit", StringComparison.OrdinalIgnoreCase) }).ToList(),
            crimes = s?.Crimes.Select(c => new { c.Id, c.Name, c.Status, c.Difficulty, c.ReadyAt, c.ExpiresAt,
                slots = c.Slots.Select(slot => new { slot.Position, slot.PassRate, slot.ItemAvailable, slot.Progress, slot.UserId,
                    name = slot.UserId == null ? "Open slot" : s.Members.FirstOrDefault(m => m.Id == slot.UserId)?.Name ?? ("Member " + slot.UserId),
                    isMe = slot.UserId.HasValue && slot.UserId == s.ViewerId }).ToList() }).ToList(),
            wars = s?.Wars, upgrades = s?.Upgrades,
            tasks = j.Tasks.Where(t => t.FactionId == s?.Id).ToList()
        };
    }
}
