using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm
{
    private ProgressionJournal? _progressionJournal;
    private string _progressionProfile = "";
    private string ProgressionPath()
    {
        var safe = string.Concat(CurrentProfileId.Where(x => char.IsLetterOrDigit(x) || x is '-' or '_'));
        if (safe.Length == 0) safe = "main";
        return Path.Combine(Storage.Root, "Progression", safe + ".json");
    }
    private ProgressionJournal GetProgressionJournal()
    {
        if (_progressionJournal is null || _progressionProfile != CurrentProfileId)
        {
            _progressionProfile = CurrentProfileId;
            _progressionJournal = Storage.Load(ProgressionPath(), new ProgressionJournal());
        }
        return _progressionJournal;
    }
    private void UpdateProgression(Action<ProgressionJournal> edit)
    {
        var copy = JsonSerializer.Deserialize<ProgressionJournal>(JsonSerializer.Serialize(GetProgressionJournal()))!;
        edit(copy);
        var cutoff = DateTime.Now.AddDays(-14).ToString("yyyy-MM-dd");
        copy.Done = copy.Done.Where(x => string.CompareOrdinal(x.Key, cutoff) >= 0).ToDictionary(x => x.Key, x => x.Value);
        Storage.SaveAtomic(ProgressionPath(), copy);
        _progressionJournal = copy;
        UpdateModernDashboard();
    }
    private static long GoalNumber(JsonElement request, string name, long min, long max)
    {
        if (!request.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var number) || number < min || number > max)
            throw new InvalidDataException(name + " is outside its allowed range.");
        return number;
    }
    private void SaveProgressionGoals(JsonElement request)
    {
        var training = (int)GoalNumber(request, "training", 0, 100);
        var reserve = GoalNumber(request, "reserve", 0, 1000000000000L);
        var target = GoalNumber(request, "target", 1, 1000000000000L);
        UpdateProgression(x => { x.TrainingPercent = training; x.CashReserve = reserve; x.DailyTradingTarget = target; });
        FlashStatus("Cash and growth goals saved for " + CurrentProfile.Name + ".");
    }
    private void MarkProgressionTask(JsonElement request)
    {
        if (!request.TryGetProperty("id",out var idValue) || idValue.ValueKind != JsonValueKind.String ||
            !request.TryGetProperty("done",out var done) || done.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        var id = idValue.GetString() ?? "";
        if (!ProgressionAdvisor.TaskIds.Contains(id,StringComparer.Ordinal)) return;
        UpdateProgression(x => { var day=x.ForDay(DateTime.Now); if(done.GetBoolean()) day.Add(id); else day.Remove(id); });
    }
    private object ProgressionDashboardData()
    {
        UpdateTravelStatusFromApi();
        var journal=GetProgressionJournal();
        return new {
            goals = new { training = journal.TrainingPercent, reserve = journal.CashReserve, target = journal.DailyTradingTarget },
            report = ProgressionAdvisor.Build(_snapshot, _recommendation, journal, CurrentProfileId, _travelSituation,
                _trades, _travelForecast.Items, DateTime.UtcNow, DateTime.Now)
        };
    }
}
