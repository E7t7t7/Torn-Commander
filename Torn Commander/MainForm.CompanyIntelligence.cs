using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm
{
    private CompanyJournal? _companyJournal;
    private string _companyJournalProfileId = "";
    private bool _companySyncing;
    private string CompanyJournalPath()
    {
        var safe = new string(CurrentProfileId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (safe.Length == 0) safe = "main";
        return Path.Combine(Storage.Root, "CompanyIntelligence", safe + ".json");
    }
    private CompanyJournal GetCompanyJournal()
    {
        if (_companyJournal == null || _companyJournalProfileId != CurrentProfileId)
        {
            _companyJournalProfileId = CurrentProfileId;
            _companyJournal = Storage.Load(CompanyJournalPath(), new CompanyJournal());
            _companyJournal.Days ??= new();
        }
        return _companyJournal;
    }
    private void SaveCompanyJournal() => Storage.Save(CompanyJournalPath(), GetCompanyJournal());
    private async Task SyncCompanyIntelligenceAsync()
    {
        if (_companySyncing) return;
        var key = CredentialManager.LoadApiKey(CurrentProfileId);
        if (string.IsNullOrWhiteSpace(key))
        {
            GetCompanyJournal().Status = "No API key for this profile. Save your Torn key in Settings first.";
            UpdateModernDashboard();
            return;
        }
        var profileId = CurrentProfileId;
        _companySyncing = true;
        try
        {
            var api = new TornApiClient();
            var json = await api.FetchEndpointAsync("company/profile", key);
            // Ignore a completed request if a different Torn profile is now active.
            if (profileId != CurrentProfileId) return;
            var observation = CompanyIntelligence.ReadProfile(json);
            var journal = GetCompanyJournal();
            CompanyIntelligence.Upsert(journal, observation);
            journal.Status = $"Last company sync: {DateTime.Now:g}. Daily API values are observations, not guaranteed final daily accounting.";
            SaveCompanyJournal();
            FlashStatus("Company snapshot saved: " + observation.CompanyName);
        }
        catch (Exception ex)
        {
            if (profileId == CurrentProfileId)
                GetCompanyJournal().Status = "Sync failed: " + ex.Message;
        }
        finally { _companySyncing = false; UpdateModernDashboard(); }
    }
    private static long? CompanyCost(JsonElement request, string field, long? prior)
    {
        if (!request.TryGetProperty(field, out var x)) return prior;
        if (x.ValueKind == JsonValueKind.Null) return null;
        if (x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out var n) && n >= 0 && n <= 100000000000L)
            return n;
        throw new InvalidDataException(field + " must be a nonnegative number or blank.");
    }
    private void SaveCompanyCosts(JsonElement request)
    {
        var journal = GetCompanyJournal();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var current = journal.Days.FirstOrDefault(x => x.CompanyId == journal.ActiveCompanyId && x.DayUtc == today);
        if (current == null)
        {
            journal.Status = "Sync Company first: costs must attach to a dated, identified company observation.";
            UpdateModernDashboard();
            return;
        }
        try
        {
            current.DailyAdvertising = CompanyCost(request, "advertising", current.DailyAdvertising);
            current.DailyPayroll = CompanyCost(request, "payroll", current.DailyPayroll);
            current.DailyStockCost = CompanyCost(request, "stock", current.DailyStockCost);
            if (request.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.String)
            {
                var n = (notes.GetString() ?? "").Trim();
                current.Notes = n[..Math.Min(n.Length, 240)];
            }
            journal.Status = "Manual company costs saved locally. No Torn company settings were changed.";
            SaveCompanyJournal();
        }
        catch (Exception ex) { journal.Status = "Costs not saved: " + ex.Message; }
        UpdateModernDashboard();
    }

    private object CompanyDashboardData()
    {
        var journal = GetCompanyJournal();
        var days = journal.Days.Where(x => x.CompanyId == journal.ActiveCompanyId)
            .OrderByDescending(x => x.DayUtc, StringComparer.Ordinal).Take(90).ToList();
        var latest = days.FirstOrDefault();
        var advice = CompanyIntelligence.Analyze(days);
        return new
        {
            name = latest?.CompanyName ?? "No company synced",
            type = latest?.CompanyType ?? "",
            id = journal.ActiveCompanyId,
            rating = latest?.Rating,
            staff = latest?.Employees,
            income = latest?.DailyIncome,
            customers = latest?.DailyCustomers,
            ad = latest?.DailyAdvertising,
            payroll = latest?.DailyPayroll,
            stock = latest?.DailyStockCost,
            afterAd = latest?.AfterAdvertising,
            net = latest?.FullCostMargin,
            notes = latest?.Notes ?? "",
            day = latest?.DayUtc ?? "",
            status = journal.Status,
            state = advice.State,
            advice = advice.Message,
            suggested = advice.SuggestedBudget,
            samples = advice.SampleDays,
            syncing = _companySyncing,
            rows = days.Take(30).Select(x => new {
                day = x.DayUtc, income = x.DailyIncome, customers = x.DailyCustomers,
                ad = x.DailyAdvertising, payroll = x.DailyPayroll, stock = x.DailyStockCost,
                afterAd = x.AfterAdvertising, net = x.FullCostMargin,
                staff = x.Employees, rating = x.Rating, notes = x.Notes
            }).ToList()
        };
    }
}
