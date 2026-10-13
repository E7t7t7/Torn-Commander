using System.Net;
using System.Text.Json;
namespace TornCommander;

public sealed record TornApiResponse(int Status, string Reason, string Body, string Headers, string FinalUrl);
public static class TornApiTransport
{
    public static Task<TornApiResponse> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct = default,
        TornRequestCoordinator? coordinator = null)
    {
        var queue = coordinator ?? TornRequestCoordinator.Shared;
        return queue.RunAsync(async token =>
        {
            using var response = await http.SendAsync(request, token);
            var body = await response.Content.ReadAsStringAsync(token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || IsRateLimit(body))
            {
                var retry = response.Headers.RetryAfter;
                var wait = retry?.Delta ?? (retry?.Date is { } date ? date - queue.UtcNow : (TimeSpan?)null);
                throw queue.RateLimited(wait);
            }
            return new TornApiResponse((int)response.StatusCode, response.ReasonPhrase ?? "", body,
                string.Join("\n", response.Headers.Concat(response.Content.Headers).Select(h => h.Key + ": " + string.Join(", ", h.Value))),
                response.RequestMessage?.RequestUri?.ToString() ?? request.RequestUri?.ToString() ?? "");
        }, ct);
    }
    public static bool IsRateLimit(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error",out var error) &&
                error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code",out var code) &&
                code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number) && number == 5;
        }
        catch (JsonException) { return false; }
    }
}
