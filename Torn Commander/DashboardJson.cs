using System.Text.Json;
namespace TornCommander;
public static class DashboardJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
}
