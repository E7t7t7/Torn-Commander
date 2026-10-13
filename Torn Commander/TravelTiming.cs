using System.Text.Json;
using System.Text.RegularExpressions;

namespace TornCommander;

public sealed class TravelSituation
{
    public string State { get; set; } = "unknown";
    public string Description { get; set; } = "Travel status not yet synced";
    public string Destination { get; set; } = "";
    public DateTime? DepartUtc { get; set; }
    public DateTime? ArrivalUtc { get; set; }
    public DateTime? ReturnUtc => State == "returning" ? ArrivalUtc : null;
    public int? FlightMinutes { get; set; }
    public string Source { get; set; } = "Torn API";
    public DateTime UpdatedUtc { get; set; } = DateTime.MinValue;
}

public sealed record TravelPlanAnchor(DateTime? ReadyUtc, string Note, bool Conditional);

public static class TravelTiming
{
    private static string Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";
    private static DateTime? Timestamp(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var x) || x.ValueKind != JsonValueKind.Number || !x.TryGetInt64(out var value) || value <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime; } catch (ArgumentOutOfRangeException) { return null; }
    }
    public static TravelSituation Parse(string? basicJson, string? travelJson, DateTime observedUtc, DateTime nowUtc)
    {
        var result = new TravelSituation { UpdatedUtc = observedUtc };
        if (!string.IsNullOrWhiteSpace(basicJson))
        {
            using var doc = JsonDocument.Parse(basicJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("profile", out var profile)) root = profile;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var status))
            {
                var state = Text(status, "state");
                var description = Text(status, "description");
                var text = (state + " " + description).ToLowerInvariant();
                result.Description = description.Length > 0 ? description : state;
                result.State = text.Contains("hospital") || text.Contains("jail") ? "unavailable" :
                    text.Contains("travel") ? (Regex.IsMatch(description, @"\bto\s+Torn\b", RegexOptions.IgnoreCase) ? "returning" : "outbound") :
                    text.Contains("abroad") || text.Contains("overseas") ? "abroad" :
                    state.Equals("Okay", StringComparison.OrdinalIgnoreCase) ? "home" : "unknown";
            }
        }
        if (!string.IsNullOrWhiteSpace(travelJson))
        {
            using var doc = JsonDocument.Parse(travelJson);
            if (doc.RootElement.TryGetProperty("travel", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                result.Destination = Text(t, "destination");
                result.DepartUtc = Timestamp(t, "departed_at");
                result.ArrivalUtc = Timestamp(t, "arrival_at");
                // time_left is relative to the API observation, never to each UI redraw.
                if (result.ArrivalUtc is null && t.TryGetProperty("time_left", out var left) && left.ValueKind == JsonValueKind.Number && left.TryGetInt32(out var seconds) && seconds is > 0 and <= 43200)
                    result.ArrivalUtc = observedUtc.AddSeconds(seconds);
                if (result.DepartUtc.HasValue && result.ArrivalUtc.HasValue)
                {
                    var duration = (result.ArrivalUtc.Value - result.DepartUtc.Value).TotalMinutes;
                    if (duration is > 0 and <= 720) result.FlightMinutes = (int)Math.Ceiling(duration);
                }
                if (result.State != "unavailable" && result.Destination.Length > 0 && result.ArrivalUtc > observedUtc && result.ArrivalUtc <= observedUtc.AddHours(12))
                {
                    var home = result.Destination.Equals("Torn", StringComparison.OrdinalIgnoreCase);
                    result.State = result.ArrivalUtc > nowUtc ? (home ? "returning" : "outbound") : (home ? "home" : "abroad");
                    result.Description = result.ArrivalUtc > nowUtc ? "Flying to " + result.Destination : "Arrived in " + result.Destination + " (awaiting next API sync)";
                }
            }
        }
        return result;
    }

    public static TravelPlanAnchor Anchor(TravelSituation situation, DateTime nowUtc, int transferMinutes, IEnumerable<TravelForecastItem> routes)
    {
        var transfer = Math.Clamp(transferMinutes, 0, 120);
        if (situation.State == "home") return new(nowUtc, "Starting from your current location in Torn.", false);
        if (situation.State == "returning" && situation.ArrivalUtc.HasValue)
            return new((situation.ArrivalUtc > nowUtc ? situation.ArrivalUtc.Value : nowUtc).AddMinutes(transfer),
                "First departure follows your actual landing in Torn plus turnaround.", false);
        if (situation.State is "outbound" or "abroad")
        {
            var minutes = situation.FlightMinutes ?? routes.Where(x => x.Country.Equals(situation.Destination, StringComparison.OrdinalIgnoreCase) && x.FlightMinutesOneWay > 0)
                .Select(x => (int?)x.FlightMinutesOneWay).FirstOrDefault();
            if (!minutes.HasValue || (situation.State == "outbound" && !situation.ArrivalUtc.HasValue))
                return new(null, "Flight timing is incomplete. Run Fast Sync to read takeoff and landing, or choose a custom departure after your return.", true);
            var landing = situation.State == "outbound" && situation.ArrivalUtc > nowUtc ? situation.ArrivalUtc.Value : nowUtc;
            return new(landing.AddMinutes(transfer + minutes.Value + transfer),
                $"Conditional return: assumes {transfer} minutes abroad, a {minutes} minute return flight, then {transfer} minutes in Torn. Rebuild after starting your return flight for an actual ETA.", true);
        }
        return new(null, situation.State == "unavailable" ? "Current Torn status prevents travel." : "Travel location is unknown. Run Fast Sync or choose a custom departure.", false);
    }
}
