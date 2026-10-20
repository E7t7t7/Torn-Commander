using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm
{
    private readonly Dictionary<string,long> _companyHistoryView=new();
    private static CompanyFinance Finance(CompanyObservation x)=>new(x.CompanyId,x.DayUtc,x.DailyIncome,x.DailyAdvertising,x.DailyPayroll,x.DailyStockCost,x.Employees,x.Rating,x.Funds);
    private void SelectCompanyHistory(JsonElement request){if(request.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed.");long id=request.GetProperty("companyId").GetInt64();if(!GetCompanyJournal().Days.Any(x=>x.CompanyId==id))throw new InvalidDataException("No authorized saved history for that company.");_companyHistoryView[CurrentProfileId]=id;UpdateModernDashboard();}
    private object CompanyHistoryData(CompanyJournal journal){try{
        long selected=_companyHistoryView.GetValueOrDefault(CurrentProfileId,journal.ActiveCompanyId);var companies=journal.Days.GroupBy(x=>x.CompanyId).Select(g=>new{id=g.Key,name=g.OrderBy(x=>x.ObservedUtc).Last().CompanyName}).ToList();
        var days=journal.Days.Where(x=>x.CompanyId==selected).OrderBy(x=>x.DayUtc,StringComparer.Ordinal).ToList();var latest=days.LastOrDefault();
        var teams=IntelStore.Snapshots(CurrentProfileId,"official-company-team",selected.ToString(),120).Select(x=>JsonSerializer.Deserialize<CompanyTeamScan>(x)).Where(x=>x!=null).Cast<CompanyTeamScan>().OrderBy(x=>x.ScannedUtc).ToList();
        if(journal.Team is {} current&&current.CompanyId==selected&&!teams.Any(x=>x.ScannedUtc==current.ScannedUtc))teams.Add(current);
        teams=teams.OrderBy(x=>x.ScannedUtc).ToList();
        var newest=teams.LastOrDefault();var oldest=teams.FirstOrDefault();
        var staff=(newest?.Employees??new()).Select(e=>{var old=oldest?.Employees.FirstOrDefault(x=>x.Id==e.Id);bool measured=old?.Stats!=null&&e.Stats!=null&&teams.Count>=2;return new{id=e.Id,name=e.Name,position=e.Position,wage=e.Wage,effectiveness=e.TotalEffectiveness,inactivity=e.Inactivity,manualDelta=measured?e.Stats!.ManualLabor-old!.Stats!.ManualLabor:(long?)null,intelligenceDelta=measured?e.Stats!.Intelligence-old!.Stats!.Intelligence:(long?)null,enduranceDelta=measured?e.Stats!.Endurance-old!.Stats!.Endurance:(long?)null,firstAt=oldest?.ScannedUtc,lastAt=newest?.ScannedUtc};}).ToList();
        var heat=teams.SelectMany(t=>t.Employees.Where(e=>e.Inactivity.HasValue).Select(e=>new{day=(int)t.ScannedUtc.DayOfWeek,hour=t.ScannedUtc.Hour,penalty=e.Inactivity!.Value})).GroupBy(x=>(x.day,x.hour)).Select(g=>new{day=g.Key.day,hour=g.Key.hour,samples=g.Count(),averagePenalty=Math.Round(g.Average(x=>Math.Abs(x.penalty)),1)}).ToList();
        return new{selected,companies,current=latest,liveCompany=journal.ActiveCompanyId,cohorts=CompanyScenarios.AdvertisingCohorts(days.Select(Finance)),staff,heatmap=heat,scans=teams.Count,stock=newest?.Stock,baseline=latest==null?null:CompanyScenarios.Compare(Finance(latest),null,null,null,0)};
    }catch{return new{selected=journal.ActiveCompanyId,companies=Array.Empty<object>(),staff=Array.Empty<object>(),heatmap=Array.Empty<object>(),note="Saved history view unavailable; current company tools remain available."};}}
    private object? _companyScenarioResult;
    private string _companyScenarioProfile="";
    private long _companyScenarioId;
    private void CalculateCompanyScenario(JsonElement request){
        if(request.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed.");long company=request.GetProperty("companyId").GetInt64();var day=GetCompanyJournal().Days.Where(x=>x.CompanyId==company).OrderBy(x=>x.DayUtc,StringComparer.Ordinal).LastOrDefault()??throw new InvalidDataException("Sync company history first.");
        long? Read(string key){var x=request.GetProperty(key);if(x.ValueKind==JsonValueKind.Null)return null;var n=x.GetInt64();if(n<0||n>1000000000000)throw new InvalidDataException("Scenario costs must be nonnegative.");return n;}
        _companyScenarioResult=CompanyScenarios.Compare(Finance(day),Read("ads"),Read("payroll"),Read("supplies"),request.GetProperty("revenueChange").GetDouble());_companyScenarioProfile=CurrentProfileId;_companyScenarioId=company;UpdateModernDashboard();
    }
}
