using System.Net.Http.Headers;
using System.Text.Json;

namespace TornCommander;

internal sealed class TornApiClient
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.torn.com/v2/"),
        Timeout = TimeSpan.FromSeconds(25)
    };

    public TornApiClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TornCommander/0.6.7 (desktop companion)");
    }

    private async Task<string> GetAsync(string relative, string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relative);
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", key);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {Trim(body, 500)}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (TryGet(doc.RootElement, "error", out var err) && err.ValueKind != JsonValueKind.Null)
                throw new HttpRequestException("Torn API: " + err.ToString());
        }
        catch (JsonException)
        {
            throw new HttpRequestException("Torn API returned non-JSON data.");
        }

        return body;
    }

    public async Task<TornSnapshot> FetchFastSnapshotAsync(string key, CancellationToken ct = default)
    {
        var s = new TornSnapshot();
        var requests = new (string Name, string Path)[]
        {
            ("basic", "user/basic"),
            ("bars", "user/bars"),
            ("refills", "user/refills"),
            ("skills", "user/skills"),
            ("job", "user/job"),
            ("faction", "user/faction")
        };

        var results = await Task.WhenAll(requests.Select(x => TryFetchAsync(x.Name, x.Path, key, ct)));
        ApplyFetchResults(s, results);
        s.FastRetrievedAt = DateTime.Now;
        ParseCommon(s);
        return s;
    }

    public async Task<TornSnapshot> FetchFullSnapshotAsync(string key, CancellationToken ct = default)
    {
        var s = await FetchFastSnapshotAsync(key, ct);
        var requests = new (string Name, string Path)[]
        {
            ("perks", "user/perks"),
            ("merits", "user/merits"),
            ("education", "user/education"),
            ("organizedcrime", "user/organizedcrime"),
            ("workstats", "user/workstats"),
            ("money", "user/money"),
            ("racingrecords", "user/racingrecords"),
            ("enlistedcars", "user/enlistedcars"),
            ("casino", "user/casino"),
            ("honors", "user/honors"),
            ("medals", "user/medals"),
            ("personalstats", "user/personalstats?cat=all"),
            ("torn_honors", "torn/honors"),
            ("torn_medals", "torn/medals"),
            ("torn_merits", "torn/merits"),
            ("torn_crimes", "torn/crimes")
        };

        var results = await Task.WhenAll(requests.Select(x => TryFetchAsync(x.Name, x.Path, key, ct)));
        ApplyFetchResults(s, results);

        await FetchCrimeProgressAsync(s, key, ct);

        // Directors/employees can expose their live company addiction effectiveness through
        // /company/employees when the saved key has sufficient access. This is best-effort:
        // a restricted key should never make the rest of Torn Commander fail.
        if (s.PlayerId is not null && !string.IsNullOrWhiteSpace(s.CompanyName))
        {
            var companyEmployees = await TryFetchAsync("companyemployees", "company/employees", key, ct);
            if (companyEmployees.Json is not null)
                s.RawJson[companyEmployees.Name] = companyEmployees.Json;
        }

        s.FullRetrievedAt = DateTime.Now;
        ParseCommon(s);
        ParsePerks(s);
        ParseMerits(s);
        ParseEducation(s);
        ParseOrganizedCrime(s);
        ParseWorkStats(s);
        ParseCompanyAddiction(s);
        MeritHunter.Build(s);
        return s;
    }

    public async Task<string> FetchEndpointAsync(string path, string key, CancellationToken ct = default)
    {
        var clean = path.Trim();
        if (clean.StartsWith("https://api.torn.com/v2/", StringComparison.OrdinalIgnoreCase))
            clean = clean["https://api.torn.com/v2/".Length..];
        if (clean.StartsWith("v2/", StringComparison.OrdinalIgnoreCase)) clean = clean[3..];
        return await GetAsync(clean.TrimStart('/'), key, ct);
    }

    public async Task<List<MarketCatalogItem>> FetchItemCatalogAsync(string key, CancellationToken ct = default)
    {
        var json = await GetAsync("torn/items", key, ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<MarketCatalogItem>();
        if (!TryGet(doc.RootElement, "items", out var items)) return list;

        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
                AddCatalogItem(list, item, null);
        }
        else if (items.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in items.EnumerateObject())
                AddCatalogItem(list, prop.Value, int.TryParse(prop.Name, out var id) ? id : null);
        }

        return list.Where(x => x.Id > 0 && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Id).Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<MarketScanResult> FetchItemMarketAsync(int itemId, int requestedQuantity, long maxUnitPrice, string key, CancellationToken ct = default)
    {
        var json = await GetAsync($"market/{itemId}/itemmarket", key, ct);
        using var doc = JsonDocument.Parse(json);
        var result = new MarketScanResult { ItemId = itemId, RequestedQuantity = Math.Max(0, requestedQuantity) };

        if (!TryGet(doc.RootElement, "itemmarket", out var market) || market.ValueKind != JsonValueKind.Object)
        {
            result.Status = "No item market data";
            return result;
        }

        if (TryGet(market, "item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            result.ItemId = DirectInt(item, "id") ?? itemId;
            result.ItemName = DirectString(item, "name") ?? $"Item #{itemId}";
        }
        else result.ItemName = $"Item #{itemId}";

        if (TryGet(market, "listings", out var listings) && listings.ValueKind == JsonValueKind.Array)
        {
            foreach (var listing in listings.EnumerateArray())
            {
                var price = FindLong(listing, "price") ?? 0;
                var amount = FindInt(listing, "amount") ?? FindInt(listing, "quantity") ?? 0;
                if (price <= 0 || amount <= 0) continue;
                result.Tiers.Add(new MarketPriceTier { Price = price, Amount = amount });
            }
        }

        result.Tiers = result.Tiers.OrderBy(x => x.Price).ThenByDescending(x => x.Amount).ToList();
        var remaining = result.RequestedQuantity;
        foreach (var tier in result.Tiers)
        {
            if (remaining <= 0) break;
            if (maxUnitPrice > 0 && tier.Price > maxUnitPrice) break;
            var take = Math.Min(remaining, tier.Amount);
            if (take <= 0) continue;
            result.Plan.Add(new MarketPurchasePlan { Price = tier.Price, Quantity = take });
            remaining -= take;
        }

        if (result.RequestedQuantity <= 0) result.Status = "Complete";
        else if (result.Tiers.Count == 0) result.Status = "No listings";
        else if (result.PlannedQuantity >= result.RequestedQuantity) result.Status = "Ready";
        else if (maxUnitPrice > 0 && result.Tiers.Any(x => x.Price > maxUnitPrice)) result.Status = $"Partial — price cap ${maxUnitPrice:N0}";
        else result.Status = $"Partial — {result.PlannedQuantity:N0}/{result.RequestedQuantity:N0} available";

        return result;
    }

    private static void AddCatalogItem(List<MarketCatalogItem> list, JsonElement item, int? fallbackId)
    {
        if (item.ValueKind != JsonValueKind.Object) return;
        var id = DirectInt(item, "id") ?? fallbackId ?? 0;
        var name = DirectString(item, "name") ?? "";
        var type = DirectString(item, "type") ?? DirectString(item, "type_name") ?? "";
        if (id > 0 && !string.IsNullOrWhiteSpace(name))
            list.Add(new MarketCatalogItem { Id = id, Name = name, Type = type,
                MarketValue = FindLong(item, "market_value") ?? FindLong(item, "marketValue") ?? 0 });
    }

    private async Task<(string Name, string? Json, string? Error)> TryFetchAsync(
        string name, string path, string key, CancellationToken ct)
    {
        try
        {
            return (name, await GetAsync(path, key, ct), null);
        }
        catch (Exception ex)
        {
            return (name, null, ex.Message);
        }
    }

    private static void ApplyFetchResults(
        TornSnapshot s,
        IEnumerable<(string Name, string? Json, string? Error)> results)
    {
        foreach (var result in results)
        {
            if (result.Json is not null) s.RawJson[result.Name] = result.Json;
            if (result.Error is not null) s.Errors.Add($"{result.Name}: {result.Error}");
        }
    }

    private async Task FetchCrimeProgressAsync(TornSnapshot s, string key, CancellationToken ct)
    {
        string catalogJson;
        try
        {
            catalogJson = await GetAsync("torn/crimes", key, ct);
            s.RawJson["torn_crimes"] = catalogJson;
        }
        catch (Exception ex)
        {
            s.Errors.Add("crime catalog: " + ex.Message);
            return;
        }

        var catalog = ParseCrimeCatalog(catalogJson);
        // Keep the full sync responsive without hammering Torn's API. Four concurrent
        // per-crime reads is enough to cut waiting time while staying comfortably small.
        using var gate = new SemaphoreSlim(4);
        var tasks = catalog.Select(async crime =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var json = await GetAsync($"user/{crime.Id}/crimes", key, ct);
                ApplyCrimeStats(crime, json);
                return (Crime: crime, Json: json, Error: (string?)null);
            }
            catch (Exception ex)
            {
                return (Crime: crime, Json: (string?)null, Error: ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        foreach (var result in results.OrderBy(x => x.Crime.Id))
        {
            if (result.Json is not null) s.RawJson[$"crime_{result.Crime.Id}"] = result.Json;
            if (result.Error is not null) s.Errors.Add($"crime {result.Crime.Name}: {result.Error}");
            s.Crimes.Add(result.Crime);
        }
    }

    private static List<CrimeProgress> ParseCrimeCatalog(string json)
    {
        var list = new List<CrimeProgress>();
        using var doc = JsonDocument.Parse(json);
        if (!TryGet(doc.RootElement, "crimes", out var crimes) || crimes.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var c in crimes.EnumerateArray())
        {
            list.Add(new CrimeProgress
            {
                Id = DirectInt(c, "id") ?? 0,
                Name = DirectString(c, "name") ?? "Unknown crime",
                Category = DirectString(c, "category_name") ?? "",
                Enhancer = DirectString(c, "enhancer_name") ?? ""
            });
        }
        return list.Where(x => x.Id > 0).OrderBy(x => x.Id).ToList();
    }

    private static void ApplyCrimeStats(CrimeProgress row, string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!TryGet(doc.RootElement, "crimes", out var c) || c.ValueKind != JsonValueKind.Object) return;

        row.NerveSpent = DirectInt(c, "nerve_spent") ?? 0;
        row.Skill = DirectDouble(c, "skill") ?? 0;
        row.ProgressionBonus = DirectInt(c, "progression_bonus") ?? 0;
        if (TryGet(c, "attempts", out var attempts) && attempts.ValueKind == JsonValueKind.Object)
        {
            row.Attempts = DirectInt(attempts, "total") ?? 0;
            row.Success = DirectInt(attempts, "success") ?? 0;
            row.Fail = DirectInt(attempts, "fail") ?? 0;
            row.CriticalFail = DirectInt(attempts, "critical_fail") ?? 0;
        }
    }

    private static void ParseCommon(TornSnapshot s)
    {
        if (s.RawJson.TryGetValue("basic", out var basic))
        {
            using var doc = JsonDocument.Parse(basic);
            s.Level = FindInt(doc.RootElement, "level");
            s.PlayerId = FindInt(doc.RootElement, "id") ?? FindInt(doc.RootElement, "player_id");
            s.PlayerName = FindString(doc.RootElement, "name");
        }

        if (s.RawJson.TryGetValue("bars", out var bars))
        {
            using var doc = JsonDocument.Parse(bars);
            if (FindObject(doc.RootElement, "nerve") is JsonElement nerve)
            {
                s.CurrentNerve = DirectInt(nerve, "current");
                s.MaxNerve = DirectInt(nerve, "maximum") ?? DirectInt(nerve, "max");
            }
            if (FindObject(doc.RootElement, "energy") is JsonElement energy)
            {
                s.CurrentEnergy = DirectInt(energy, "current");
                s.MaxEnergy = DirectInt(energy, "maximum") ?? DirectInt(energy, "max");
            }
        }

        if (s.RawJson.TryGetValue("refills", out var refills))
        {
            using var doc = JsonDocument.Parse(refills);
            if (FindObject(doc.RootElement, "refills") is JsonElement r)
                s.NerveRefillAvailable = DirectBool(r, "nerve");
        }

        if (s.RawJson.TryGetValue("skills", out var skills))
        {
            using var doc = JsonDocument.Parse(skills);
            foreach (var e in Enumerate(doc.RootElement))
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = DirectString(e, "name") ?? DirectString(e, "slug");
                if (name is null || !name.Contains("racing", StringComparison.OrdinalIgnoreCase)) continue;
                s.RacingSkill = DirectDouble(e, "level") ?? DirectDouble(e, "value");
                if (s.RacingSkill is not null) break;
            }
        }

        if (s.RawJson.TryGetValue("job", out var jobJson))
        {
            using var doc = JsonDocument.Parse(jobJson);
            if (TryGet(doc.RootElement, "job", out var job) && job.ValueKind == JsonValueKind.Object)
            {
                var type = DirectString(job, "type") ?? "";
                var name = DirectString(job, "name");
                if (type.Equals("company", StringComparison.OrdinalIgnoreCase) || TryGet(job, "rating", out _))
                    s.CompanyName = name;
                else
                    s.JobName = name;
            }
        }

        if (s.RawJson.TryGetValue("faction", out var factionJson))
        {
            using var doc = JsonDocument.Parse(factionJson);
            if (TryGet(doc.RootElement, "faction", out var faction) && faction.ValueKind == JsonValueKind.Object)
                s.FactionName = DirectString(faction, "name");
        }

        if (s.RawJson.TryGetValue("money", out var moneyJson))
        {
            using var doc = JsonDocument.Parse(moneyJson);
            s.Wallet = FindLong(doc.RootElement, "wallet");
        }
    }

    private static void ParsePerks(TornSnapshot s)
    {
        if (!s.RawJson.TryGetValue("perks", out var json)) return;
        using var doc = JsonDocument.Parse(json);
        if (!TryGet(doc.RootElement, "perks", out var p) || p.ValueKind != JsonValueKind.Object) return;
        s.FactionPerks = ReadStringArray(p, "faction");
        s.JobPerks = ReadStringArray(p, "job");
        s.PropertyPerks = ReadStringArray(p, "property");
        s.EducationPerks = ReadStringArray(p, "education");
        s.EnhancerPerks = ReadStringArray(p, "enhancer");
        s.BookPerks = ReadStringArray(p, "book");
        s.StockPerks = ReadStringArray(p, "stock");
        s.MeritPerks = ReadStringArray(p, "merit");
    }

    private static void ParseMerits(TornSnapshot s)
    {
        if (!s.RawJson.TryGetValue("merits", out var json)) return;
        using var doc = JsonDocument.Parse(json);
        if (!TryGet(doc.RootElement, "merits", out var m) || m.ValueKind != JsonValueKind.Object) return;
        s.MeritAvailable = DirectInt(m, "available") ?? 0;
        s.MeritUsed = DirectInt(m, "used") ?? 0;
    }

    private static void ParseEducation(TornSnapshot s)
    {
        if (!s.RawJson.TryGetValue("education", out var json)) return;
        using var doc = JsonDocument.Parse(json);
        if (!TryGet(doc.RootElement, "education", out var ed) || ed.ValueKind != JsonValueKind.Object) return;

        if (TryGet(ed, "complete", out var complete) && complete.ValueKind == JsonValueKind.Array)
            s.CompletedEducationIds = complete.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
                .Select(x => x.GetInt32()).ToList();

        if (TryGet(ed, "current", out var current) && current.ValueKind == JsonValueKind.Object)
        {
            var id = DirectInt(current, "id");
            var until = DirectInt(current, "until");
            if (id is not null) s.CurrentEducationName = $"Education #{id}";
            if (until is not null)
                s.CurrentEducationTimeLeft = Math.Max(0, until.Value - (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
    }

    private static void ParseOrganizedCrime(TornSnapshot s)
    {
        if (!s.RawJson.TryGetValue("organizedcrime", out var json)) return;
        using var doc = JsonDocument.Parse(json);
        foreach (var propName in new[] { "organizedCrime", "organizedcrime", "organized_crime" })
        {
            if (!TryGet(doc.RootElement, propName, out var oc)) continue;

            if (oc.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                s.InOrganizedCrime = false;
                return;
            }

            // Torn v2 uses an object with error code 27 when the user is not currently
            // assigned to an organized crime. Do not mistake that error object for an OC.
            if (oc.ValueKind == JsonValueKind.Object
                && (TryGet(oc, "code", out _) || TryGet(oc, "error", out _)))
            {
                s.InOrganizedCrime = false;
                return;
            }

            s.InOrganizedCrime = oc.ValueKind == JsonValueKind.Object || oc.ValueKind == JsonValueKind.Array;
            return;
        }
        s.InOrganizedCrime = false;
    }

    private static void ParseWorkStats(TornSnapshot s)
    {
        if (!s.RawJson.TryGetValue("workstats", out var json)) return;
        using var doc = JsonDocument.Parse(json);
        if (!TryGet(doc.RootElement, "workstats", out var work) || work.ValueKind != JsonValueKind.Object) return;
        s.ManualLabor = DirectInt(work, "manual_labor");
        s.Intelligence = DirectInt(work, "intelligence");
        s.Endurance = DirectInt(work, "endurance");
    }

    private static void ParseCompanyAddiction(TornSnapshot s)
    {
        if (s.PlayerId is not int playerId) return;
        if (!s.RawJson.TryGetValue("companyemployees", out var json)) return;

        using var doc = JsonDocument.Parse(json);
        if (!TryGet(doc.RootElement, "employees", out var employees) || employees.ValueKind != JsonValueKind.Array) return;

        foreach (var employee in employees.EnumerateArray())
        {
            if (employee.ValueKind != JsonValueKind.Object || DirectInt(employee, "id") != playerId) continue;
            if (!TryGet(employee, "effectiveness", out var eff) || eff.ValueKind != JsonValueKind.Object) return;
            var addiction = DirectInt(eff, "addiction");
            if (addiction is null) return;
            s.DetectedAddictionPercent = addiction.Value;
            s.AddictionSource = "company effectiveness API";
            return;
        }
    }

    private static List<string> ReadStringArray(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var arr) || arr.ValueKind != JsonValueKind.Array) return new();
        return arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement root)
    {
        yield return root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
                foreach (var x in Enumerate(p.Value)) yield return x;
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in root.EnumerateArray())
                foreach (var x in Enumerate(a)) yield return x;
        }
    }

    private static JsonElement? FindObject(JsonElement root, string property)
    {
        foreach (var e in Enumerate(root))
            if (e.ValueKind == JsonValueKind.Object && TryGet(e, property, out var v) && v.ValueKind == JsonValueKind.Object)
                return v;
        return null;
    }

    private static int? FindInt(JsonElement root, string property)
    {
        foreach (var e in Enumerate(root))
            if (e.ValueKind == JsonValueKind.Object && DirectInt(e, property) is int v) return v;
        return null;
    }

    private static long? FindLong(JsonElement root, string property)
    {
        foreach (var e in Enumerate(root))
        {
            if (e.ValueKind != JsonValueKind.Object || !TryGet(e, property, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out n)) return n;
        }
        return null;
    }

    private static string? FindString(JsonElement root, string property)
    {
        foreach (var e in Enumerate(root))
            if (e.ValueKind == JsonValueKind.Object && DirectString(e, property) is string v) return v;
        return null;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string? DirectString(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
    }

    private static int? DirectInt(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out i)) return i;
        return null;
    }

    private static double? DirectDouble(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out d)) return d;
        return null;
    }

    private static bool? DirectBool(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)) return b;
        return null;
    }

    private static string Trim(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
