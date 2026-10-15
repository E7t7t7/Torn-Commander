using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm
{
    private CompanyJournal? _companyJournal;
    private string _companyJournalProfileId = "";
    private bool _companySyncing;
    private readonly Dictionary<string, DateTime> _companyNextAttempt = new();
    private void SetCompanyAutoCollection(JsonElement request)
    {
        if (!request.TryGetProperty("enabled", out var enabled) || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        var journal = GetCompanyJournal();
        var previous = journal.AutoCollectEnabled;
        journal.AutoCollectEnabled = enabled.GetBoolean();
        try { SaveCompanyJournal(); }
        catch { journal.AutoCollectEnabled = previous; throw; }
        UpdateModernDashboard();
    }
    private void TickCompanyCollection()
    {
        if (_companySyncing) return;
        var journal = GetCompanyJournal();
        var now = DateTime.UtcNow;
        if (!CompanyIntelligence.CollectionDue(journal, now)) return;
        if (_companyNextAttempt.TryGetValue(CurrentProfileId, out var next) && now < next) return;
        _companyNextAttempt[CurrentProfileId] = now.AddMinutes(10);
        _ = SyncCompanyIntelligenceAsync();
    }
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
    private void SaveCompanyJournal() => Storage.SaveAtomic(CompanyJournalPath(), GetCompanyJournal());
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
        UpdateModernDashboard();
        try
        {
            var api = new TornApiClient();
            var json = await api.FetchEndpointAsync("company/profile", key);
            // Ignore a completed request if a different Torn profile is now active.
            if (profileId != CurrentProfileId) return;
            var observation = CompanyIntelligence.ReadProfile(json);
            var team = new CompanyTeamScan { CompanyId = observation.CompanyId };
            // Optional sections fail independently. Never substitute a previous company's team.
            async Task ReadSection(string label, string path, Action<string> read)
            {
                if (profileId != CurrentProfileId) return;
                GetCompanyJournal().Status = "Scanning " + label.ToLowerInvariant() + "…";
                UpdateModernDashboard();
                try { read(await api.FetchEndpointAsync(path, key)); }
                catch (Exception ex) { team.Warnings.Add(label + ": " + ex.Message); }
            }
            await ReadSection("Employees", "company/employees", body => {
                team.Employees = CompanyStaffAdvisor.ReadEmployees(body); team.EmployeesLoaded = true;
            });
            if (observation.CompanyTypeId > 0)
                await ReadSection("Job requirements", "torn/" + observation.CompanyTypeId + "/companies",
                    body => team.Jobs = CompanyStaffAdvisor.ReadJobs(body, observation.CompanyTypeId));
            else team.Warnings.Add("Company type ID missing; job requirements unavailable.");
            await ReadSection("Stock", "company/stock", body => {
                team.Stock = CompanyStaffAdvisor.ReadStock(body); team.StockLoaded = true;
            });
            if (profileId != CurrentProfileId) return;
            // The employee endpoint is account-relative. Confirm company identity after the scan.
            var confirmed = CompanyIntelligence.ReadProfile(await api.FetchEndpointAsync("company/profile", key));
            if (profileId != CurrentProfileId) return;
            if (confirmed.CompanyId != observation.CompanyId || confirmed.CompanyTypeId != observation.CompanyTypeId)
                throw new InvalidDataException("Your company changed during the scan. Sync again; no mixed-company data was saved.");
            team.ScannedUtc = DateTime.UtcNow;
            var journal = GetCompanyJournal();
            var previousDays = journal.Days.ToList();
            var previousCompany = journal.ActiveCompanyId;
            var previousTeam = journal.Team;
            var previousStatus = journal.Status;
            CompanyIntelligence.Upsert(journal, confirmed);
            journal.Team = team;
            journal.Status = $"Last company scan: {DateTime.Now:g}. Daily API values are observations, not final accounts.";
            try { SaveCompanyJournal(); }
            catch
            {
                journal.Days = previousDays; journal.ActiveCompanyId = previousCompany;
                journal.Team = previousTeam; journal.Status = previousStatus; throw;
            }
            FlashStatus("Company snapshot saved: " + observation.CompanyName);
        }
        catch (Exception ex)
        {
            if (profileId == CurrentProfileId)
                GetCompanyJournal().Status = "Sync failed: " + ex.Message;
        }
        finally { _companySyncing = false; UpdateModernDashboard(); }
    }
    private void SaveCompanyStaffPlan(JsonElement request)
    {
        var journal = GetCompanyJournal();
        var team = journal.Team;
        if (team == null || team.CompanyId != journal.ActiveCompanyId || _companySyncing ||
            !request.TryGetProperty("companyId", out var company) || !company.TryGetInt64(out var id) || id != team.CompanyId ||
            !request.TryGetProperty("scanToken", out var token) || token.GetString() != team.ScannedUtc.ToString("O"))
            throw new InvalidDataException("The company scan changed. Review the current scan before rebuilding the plan.");
        var previous = team.PlannedCounts;
        var previousSource = team.PlanSource;
        var previousPolicy = team.PlanPolicy;
        var previousVersion = team.PlanVersion;
        string policy = "total-fit";
        Dictionary<long, int>? counts;
        string source;
        if (request.TryGetProperty("scenarioId", out var scenario))
        {
            if (scenario.ValueKind != JsonValueKind.String) throw new InvalidDataException("Choose a valid staffing layout.");
            var latest = journal.Days.Where(x => x.CompanyId == team.CompanyId)
                .OrderByDescending(x => x.DayUtc, StringComparer.Ordinal).FirstOrDefault();
            var option = CompanyStaffLayouts.Build(latest, team).Options.FirstOrDefault(x => x.Id == scenario.GetString())
                ?? throw new InvalidDataException("That layout is unavailable for the current scan. Scan again and choose an available plan.");
            counts = new Dictionary<long, int>(option.Counts);
            source = option.Name; policy = option.Policy;
        }
        else
        {
            bool resetting = request.TryGetProperty("reset", out var reset) && reset.ValueKind == JsonValueKind.True;
            counts = resetting ? null : CompanyStaffAdvisor.ReadCounts(request, team);
            source = resetting ? "Current role counts" : "Custom role-count scenario";
        }
        team.PlannedCounts = counts; team.PlanSource = source; team.PlanPolicy = policy; team.PlanVersion = CompanyStaffLayouts.PlanVersion;
        try { SaveCompanyJournal(); }
        catch { team.PlannedCounts = previous; team.PlanSource = previousSource; team.PlanPolicy = previousPolicy; team.PlanVersion = previousVersion; throw; }
        journal.Status = "Local staffing scenario saved. No Torn positions were changed.";
        UpdateModernDashboard();
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
            // Validate every field before changing any part of the saved observation.
            var advertising = CompanyIntelligence.CompanyCost(request, "advertising", current.DailyAdvertising);
            var payroll = CompanyIntelligence.CompanyCost(request, "payroll", current.DailyPayroll);
            var stock = CompanyIntelligence.CompanyCost(request, "stock", current.DailyStockCost);
            var note = current.Notes;
            if (request.TryGetProperty("notes", out var notes))
            {
                if (notes.ValueKind != JsonValueKind.String) throw new InvalidDataException("Notes must be text.");
                var n = (notes.GetString() ?? "").Trim();
                note = n[..Math.Min(n.Length, 240)];
            }
            var old = (current.DailyAdvertising, current.DailyPayroll, current.DailyStockCost, current.Notes);
            current.DailyAdvertising = advertising; current.DailyPayroll = payroll; current.DailyStockCost = stock; current.Notes = note;
            try { SaveCompanyJournal(); }
            catch { (current.DailyAdvertising, current.DailyPayroll, current.DailyStockCost, current.Notes) = old; throw; }
            journal.Status = "Manual company costs saved locally. No Torn company settings were changed.";
        }
        catch (Exception ex) { journal.Status = "Costs not saved: " + ex.Message; }
        UpdateModernDashboard();
    }

    private void CopyCompanyReport()
    {
        if (_companySyncing)
        {
            FlashStatus("Wait for the company scan to finish before copying its report.");
            return;
        }
        var report = CompanyReportExport.Build(GetCompanyJournal(), DateTime.UtcNow);
        Clipboard.SetText(report);
        GetCompanyJournal().Status = "Company report copied. Paste it into your chosen chat for advice. No automatic AI request was made.";
        UpdateModernDashboard();
        FlashStatus("Company report copied to clipboard.");
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
            business = CompanyIntelligence.BusinessReport(days),
            health = new { efficiency = latest?.Efficiency, environment = latest?.Environment,
                popularity = latest?.Popularity, trains = latest?.Trains, capacity = latest?.Capacity, funds = latest?.Funds },
            team = CompanyStaffAdvisor.Report(latest, journal.Team),
            layouts = CompanyStaffLayouts.Build(latest, journal.Team),
            scan = journal.Team?.CompanyId == journal.ActiveCompanyId ? new {
                at = journal.Team.ScannedUtc.ToString("yyyy-MM-dd HH:mm") + " UTC",
                warnings = journal.Team.Warnings.Concat(
                    DateTime.UtcNow - journal.Team.ScannedUtc > TimeSpan.FromHours(24)
                        ? new[] { "This staff scan is over 24 hours old. Scan again before acting on its staffing plan." } : Array.Empty<string>()).ToList(),
                employeesLoaded = journal.Team.EmployeesLoaded,
                stockLoaded = journal.Team.StockLoaded,
                wageTotal = journal.Team.EmployeesLoaded && journal.Team.Employees.Count > 0 &&
                    latest?.Employees == journal.Team.Employees.Count &&
                    journal.Team.Employees.All(x => x.Wage.HasValue)
                        ? (long?)journal.Team.Employees.Sum(x => x.Wage!.Value) : null,
                stock = journal.Team.Stock,
                token = journal.Team.ScannedUtc.ToString("O"),
                jobs = journal.Team.Jobs.Select(j => new { j.Id, j.Name, j.Ability, j.Description, j.Required,
                    count = journal.Team.PlannedCounts?.GetValueOrDefault(j.Id) ??
                        journal.Team.Employees.Count(e => e.PositionId == j.Id) }).ToList()
            } : null,
            suggested = advice.SuggestedBudget,
            samples = advice.SampleDays,
            syncing = _companySyncing,
            autoCollect = journal.AutoCollectEnabled,
            rows = days.Take(30).Select(x => new {
                day = x.DayUtc, income = x.DailyIncome, customers = x.DailyCustomers,
                ad = x.DailyAdvertising, payroll = x.DailyPayroll, stock = x.DailyStockCost,
                afterAd = x.AfterAdvertising, net = x.FullCostMargin,
                staff = x.Employees, rating = x.Rating, notes = x.Notes
            }).ToList()
        };
    }
}
