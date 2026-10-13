namespace TornCommander;
public static class SnapshotMerge
{
    public static void MergeFast(TornSnapshot current, TornSnapshot fresh)
    {
        current.PlayerName = fresh.PlayerName ?? current.PlayerName;
        current.PlayerId = fresh.PlayerId ?? current.PlayerId;
        current.Level = fresh.Level ?? current.Level;
        current.CurrentNerve = fresh.CurrentNerve ?? current.CurrentNerve;
        current.MaxNerve = fresh.MaxNerve ?? current.MaxNerve;
        current.CurrentEnergy = fresh.CurrentEnergy ?? current.CurrentEnergy;
        current.MaxEnergy = fresh.MaxEnergy ?? current.MaxEnergy;
        current.RacingSkill = fresh.RacingSkill ?? current.RacingSkill;
        current.NerveRefillAvailable = fresh.NerveRefillAvailable ?? current.NerveRefillAvailable;
        current.JobName = fresh.JobName ?? current.JobName;
        current.CompanyName = fresh.CompanyName ?? current.CompanyName;
        current.FactionName = fresh.FactionName ?? current.FactionName;
        if (fresh.RawJson.Count > 0) current.FastRetrievedAt = fresh.FastRetrievedAt;
        // Keep role/access warnings from a previous full sync, but clear stale
        // transient errors when a subsequent fast sync succeeds.
        current.Errors = fresh.Errors.Concat(current.Errors.Where(e =>
            e.Contains("Access level of this key is not high enough", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("\"code\":16", StringComparison.OrdinalIgnoreCase))).Distinct().ToList();
        foreach (var name in new[] { "basic", "travel" })
            if (!fresh.RawJson.ContainsKey(name)) current.RawJson.Remove(name);
        foreach (var pair in fresh.RawJson) current.RawJson[pair.Key] = pair.Value;
    }
}
