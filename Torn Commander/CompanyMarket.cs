using System.Text.Json;
namespace TornCommander;

internal sealed record CompanyMarketEntry(long Id, int Stars, long WeeklyIncome);
internal sealed class CompanyMarketScan
{
    public long CompanyId { get; set; }
    public long TypeId { get; set; }
    public DateTime ScannedUtc { get; set; }
    public List<CompanyMarketEntry> Companies { get; set; } = new();
}
internal static class CompanyMarket
{
    public static List<CompanyMarketEntry> ReadPage(string json, long typeId)
    {
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement.GetProperty("companies");
        if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Company listing is missing.");
        var rows = new List<CompanyMarketEntry>();
        foreach (var x in list.EnumerateArray())
        {
            if (x.GetProperty("type").GetProperty("id").GetInt64() != typeId)
                throw new InvalidDataException("Company listing contains a different company type.");
            long id = x.GetProperty("id").GetInt64(), income = x.GetProperty("income").GetProperty("weekly").GetInt64();
            int stars = x.GetProperty("rating").GetInt32();
            if (id <= 0 || income < 0 || stars < 0 || stars > 10) throw new InvalidDataException("Invalid listing values.");
            rows.Add(new(id, stars, income));
        }
        return rows;
    }
    public static object Report(CompanyObservation? own, CompanyMarketScan? scan)
    {
        if (own == null || scan == null || scan.CompanyId != own.CompanyId || scan.TypeId != own.CompanyTypeId)
            return new { available = false };
        var peers = scan.Companies.Where(x => x.Id != own.CompanyId).ToList();
        return new {
            available = true,
            scannedUtc = scan.ScannedUtc.ToString("yyyy-MM-dd HH:mm") + " UTC",
            stale = DateTime.UtcNow - scan.ScannedUtc > TimeSpan.FromHours(24),
            weeklyIncome = own.WeeklyIncome,
            rows = Enumerable.Range(0,11).Select(star => {
                var group = peers.Where(x => x.Stars == star).OrderBy(x => x.WeeklyIncome).ToList();
                int beat = own.WeeklyIncome.HasValue ? group.Count(x => x.WeeklyIncome < own.WeeklyIncome.Value) : 0;
                return new { stars = star, count = group.Count,
                    lowest = group.Count > 0 ? (long?)group[0].WeeklyIncome : null,
                    median = group.Count > 0 ? (long?)group[(group.Count-1)/2].WeeklyIncome : null,
                    beatenPercent = group.Count > 0 && own.WeeklyIncome.HasValue ? (double?)Math.Round(100.0*beat/group.Count,1) : null,
                    next = own.Rating.HasValue && star == own.Rating.Value+1 };
            }).ToList()
        };
    }
}
