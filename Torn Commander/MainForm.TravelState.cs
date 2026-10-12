using System.Text.Json;
using System.Text.RegularExpressions;

namespace TornCommander;

public sealed partial class MainForm
{
    private sealed class TravelSituation
    {
        public string State { get; set; } = "unknown"; // home / returning / outbound / abroad / unknown
        public string Description { get; set; } = "Travel status not yet synced";
        public DateTime? ReturnUtc { get; set; }
        public string Source { get; set; } = "Torn API";
        public DateTime UpdatedUtc { get; set; } = DateTime.MinValue;
    }

    private TravelSituation _travelSituation = new();
    private DateTime _travelStateLastProbe = DateTime.MinValue;
    private bool _travelStateProbing;
    private string _travelStateProfileId = "";

    private void ClearTravelState()
    {
        _travelSituation = new TravelSituation();
        _travelStateLastProbe = DateTime.MinValue;
        _travelStateProfileId = CurrentProfileId;
    }

    private void UpdateTravelStatusFromApi()
    {
        if (!_snapshot.RawJson.TryGetValue("basic", out var raw)) return;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Object) return;
            var state = status.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
            var description = status.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(state)) return;
            var kind = "unknown";
            var text = (state + " " + description).ToLowerInvariant();
            if (text.Contains("travel"))
            {
                kind = Regex.IsMatch(description, @"\bto\s+Torn\b", RegexOptions.IgnoreCase) ? "returning" : "outbound";
            }
            else if (text.Contains("abroad") || text.Contains("overseas")) kind = "abroad";
            else if (text.Contains("hospital") || text.Contains("jail")) kind = "unavailable";
            else if (text.Contains("okay") || text.Contains("idle") || text.Contains("online") || text.Contains("offline")) kind = "home";
            if (kind == "unknown") return;
            var previous = _travelSituation;
            _travelSituation = new TravelSituation
            {
                State = kind,
                Description = string.IsNullOrWhiteSpace(description) ? state : description,
                // An ETA derived from the displayed flight timer remains valid until arrival,
                // but not if the API shows a different travel direction or location.
                ReturnUtc = kind == "returning" && previous.State == "returning" && previous.ReturnUtc > DateTime.UtcNow
                    ? previous.ReturnUtc : null,
                Source = "Torn API",
                UpdatedUtc = DateTime.UtcNow
            };
            _travelStateProfileId = CurrentProfileId;
        }
        catch { /* Partial API data must never invent a home location. */ }
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
            _travelSituation.State = returning ? "returning" : "outbound";
            _travelSituation.Description = returning ? "Returning to Torn (flight timer detected)" : "Flying away from Torn (flight timer detected)";
            _travelSituation.ReturnUtc = returning ? DateTime.UtcNow.AddSeconds(seconds) : null;
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
        if (_settings.TravelPlanStartNow && _travelSituation.State != "home")
        {
            if (_travelSituation.State == "returning" && _travelSituation.ReturnUtc > now)
            {
                var ready = _travelSituation.ReturnUtc.Value.AddMinutes(Math.Clamp(_settings.TravelTransferMinutes, 0, 120));
                var plan = TravelItineraryEngine.Build(_travelForecast.Items, _settings, ready, now, now.AddHours(24));
                plan.Note = "You're flying home. First suggested departure is after your actual return and turnaround. " + plan.Note;
                return plan;
            }
            var reason = _travelSituation.State switch
            {
                "returning" => "Returning to Torn, but the remaining flight time is not verified. Keep the Torn travel tab open for ETA detection.",
                "outbound" => "You're already traveling abroad. Commander will replan after your return; no impossible Torn departure is scheduled.",
                "abroad" => "You're abroad. Commander is waiting for your return before scheduling a departure from Torn.",
                "unavailable" => "Your Torn status currently prevents departure. Commander will replan when you can travel.",
                _ => "Torn travel status is unconfirmed. Run Fast Sync or keep the travel page open before relying on departures."
            };
            return TravelHold(now, reason);
        }
        return TravelItineraryEngine.Build(_travelForecast.Items, _settings, TravelPlanStartUtc, now,
            _settings.TravelPlanStartNow ? now.AddHours(24) : null);
    }

    private static TravelItineraryPlan TravelHold(DateTime now, string note) => new()
    {
        GeneratedUtc = now, StartUtc = now, EndUtc = now.AddHours(24), Note = note
    };
}
