using System.Text.Json;
using System.Text.RegularExpressions;

namespace TornCommander;

public sealed partial class MainForm
{
    private TravelSituation _travelSituation = new();
    private DateTime _travelStateLastProbe = DateTime.MinValue;
    private bool _travelStateProbing;
    private string _travelStateProfileId = "";

    private void ClearTravelState()
    {
        _travelSituation = new TravelSituation();
        _travelApiObservationUtc = DateTime.MinValue;
        _travelStateLastProbe = DateTime.MinValue;
        _travelStateProfileId = CurrentProfileId;
    }

    private DateTime _travelApiObservationUtc = DateTime.MinValue;
    private void UpdateTravelStatusFromApi()
    {
        var observed = _snapshot.FastRetrievedAt == DateTime.MinValue ? DateTime.MinValue : _snapshot.FastRetrievedAt.ToUniversalTime();
        if (observed == DateTime.MinValue || observed == _travelApiObservationUtc) return;
        _travelApiObservationUtc = observed;
        _snapshot.RawJson.TryGetValue("basic", out var basic);
        _snapshot.RawJson.TryGetValue("travel", out var travel);
        try { _travelSituation = TravelTiming.Parse(basic, travel, observed, DateTime.UtcNow); _travelStateProfileId = CurrentProfileId; }
        catch { /* Incomplete API responses never invent a location. */ }
    }

    private async Task ProbeTravelPageAsync()
    {
        if (_travelStateProbing || _travelStateProfileId != CurrentProfileId) return;
        var tab = Active();
        var url = tab?.WebView.Source?.ToString() ?? "";
        // Observe only the current profile's actual Torn travel view, not private
        // chat or unrelated browser pages. Do not store page text or cookies.
        if (tab is null || !url.StartsWith("https://www.torn.com/", StringComparison.OrdinalIgnoreCase) ||
            !(url.Contains("sid=travel", StringComparison.OrdinalIgnoreCase) ||
              url.Contains("travelagency.php", StringComparison.OrdinalIgnoreCase))) return;
        _travelStateProbing = true;
        var profile = CurrentProfileId;
        try
        {
            var raw = await tab.ExecuteScriptAsync("""(() => {const t=(document.body?.innerText||'').slice(0,12000);const m=t.match(/(?:Remaining\s+Flight\s+Time|Flight\s+Time\s+Remaining)\s*[-:]?\s*(?:(\d{1,2}):)?(\d{1,2}):(\d{2})/i);const r=t.match(/([A-Za-z ]{2,32})\s+to\s+Torn\.?\s*Remaining\s+Flight\s+Time/i);const outbound=/\bTorn\s+to\s+[A-Za-z ]{2,32}\.?\s*Remaining\s+Flight\s+Time/i.test(t);const travel=/\bTraveling\b/i.test(t)&&/Remaining\s+Flight\s+Time/i.test(t);return {returning:!!r,outbound:outbound,traveling:travel,remaining:m?((Number(m[1]||0)*3600)+(Number(m[2]||0)*60)+Number(m[3])):null};})()""");
            if (profile != CurrentProfileId || string.IsNullOrWhiteSpace(raw)) return;
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var returning = root.TryGetProperty("returning", out var ret) && ret.ValueKind == JsonValueKind.True;
            var outbound = root.TryGetProperty("outbound", out var outboundEl) && outboundEl.ValueKind == JsonValueKind.True;
            if (!returning && !outbound) return;
            if (!root.TryGetProperty("remaining", out var rem) || !rem.TryGetInt32(out var seconds) || seconds <= 0 || seconds > 12 * 3600) return;
            if (_travelSituation.State != (returning ? "returning" : "outbound")) _travelSituation.DepartUtc = null;
            _travelSituation.State = returning ? "returning" : "outbound";
            if (returning) _travelSituation.Destination = "Torn";
            _travelSituation.Description = returning ? "Returning to Torn (flight timer detected)" : "Flying away from Torn (flight timer detected)";
            _travelSituation.ArrivalUtc = DateTime.UtcNow.AddSeconds(seconds);
            _travelSituation.Source = "Torn travel page";
            _travelSituation.UpdatedUtc = DateTime.UtcNow;
            _travelStateProfileId = profile;
            UpdateModernDashboard();
        }
        catch { /* Page render/Cloudflare errors preserve API status. */ }
        finally { _travelStateProbing = false; }
    }

    private void TickTravelState()
    {
        if (_travelStateProfileId != CurrentProfileId) ClearTravelState();
        UpdateTravelStatusFromApi();
        if (_travelSituation.ArrivalUtc <= DateTime.UtcNow && _travelSituation.State is "returning" or "outbound")
        {
            _travelSituation.State = _travelSituation.State == "returning" ? "home" : "abroad";
            _travelSituation.Description = "Flight timer completed; awaiting next API sync.";
        }
        if (DateTime.UtcNow - _travelStateLastProbe >= TimeSpan.FromSeconds(30))
        {
            _travelStateLastProbe = DateTime.UtcNow;
            _ = ProbeTravelPageAsync();
        }
    }

    private TravelItineraryPlan BuildContextAwareTravelPlan()
    {
        var now = DateTime.UtcNow;
        if (_settings.TravelPlanStartNow && _settings.TravelPauseUntilHome)
            return TravelHold(now, "Paused for your own flight. Uncheck 'Pause plan for my trip' when ready.");
        if (_settings.TravelPlanStartNow)
        {
            UpdateTravelStatusFromApi();
            var anchor = TravelTiming.Anchor(_travelSituation, now, _settings.TravelTransferMinutes, _travelForecast.Items);
            if (!anchor.ReadyUtc.HasValue) return TravelHold(now, anchor.Note);
            var plan = TravelItineraryEngine.Build(_travelForecast.Items, _settings, anchor.ReadyUtc.Value, now, now.AddHours(24));
            plan.Note = anchor.Note + " " + plan.Note;
            return plan;
        }
        return TravelItineraryEngine.Build(_travelForecast.Items, _settings, TravelPlanStartUtc, now,
            _settings.TravelPlanStartNow ? now.AddHours(24) : null);
    }

    private static TravelItineraryPlan TravelHold(DateTime now, string note) => new()
    {
        GeneratedUtc = now, StartUtc = now, EndUtc = now.AddHours(24), Note = note
    };
}
