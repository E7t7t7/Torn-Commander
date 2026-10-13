using System.Text.Json;
namespace TornCommander;
public sealed record EducationObservation(bool HasCurrent, int? CourseId, DateTime? EndsUtc, List<int> Completed);
public static class EducationReader
{
    public static EducationObservation? Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("education", out var ed) || ed.ValueKind != JsonValueKind.Object ||
            !ed.TryGetProperty("current", out var current)) return null;
        var completed = new List<int>();
        if (ed.TryGetProperty("complete", out var complete) && complete.ValueKind == JsonValueKind.Array)
            foreach (var x in complete.EnumerateArray())
                if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var id) && id > 0) completed.Add(id);
        if (current.ValueKind == JsonValueKind.Null) return new(false, null, null, completed);
        if (current.ValueKind != JsonValueKind.Object) return null;
        int? course = current.TryGetProperty("id", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var number) && number > 0 ? number : null;
        DateTime? until = null;
        if (current.TryGetProperty("until", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetInt64(out var stamp) && stamp > 0)
            try { until = DateTimeOffset.FromUnixTimeSeconds(stamp).UtcDateTime; } catch (ArgumentOutOfRangeException) { }
        return new(true, course, until, completed);
    }
}
