using System.Text;
using System.Text.Json;
namespace TornCommander;

public sealed partial class MainForm
{
    private WarPayoutJournal? _warPayout;
    private string _warPayoutProfile="";
    private bool _warPayoutScanning;
    private string WarPayoutPath(){
        string safe=new(CurrentProfileId.Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_').ToArray());
        return Path.Combine(Storage.Root,"WarPayouts",(safe.Length==0?"main":safe)+".json");
    }
    private WarPayoutJournal GetWarPayout(){
        if(_warPayout==null||_warPayoutProfile!=CurrentProfileId){
            _warPayoutProfile=CurrentProfileId;_warPayout=Storage.Load(WarPayoutPath(),new WarPayoutJournal());
        }
        return _warPayout;
    }
    private async Task ScanWarPayoutAsync(JsonElement request){
        if(_warPayoutScanning)return;
        string profile=CurrentProfileId;
        if(request.GetProperty("profileId").GetString()!=profile)throw new InvalidDataException("Profile changed.");
        long id=request.GetProperty("warId").GetInt64();
        if(id<=0)throw new InvalidDataException("Enter a positive ranked war ID.");
        var key=CredentialManager.LoadApiKey(profile);
        if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("Save this profile's Torn API key first.");
        _warPayoutScanning=true;UpdateModernDashboard();
        try {
            var api=new TornApiClient();
            var faction=FactionIntelligence.ReadBasic(await api.FetchEndpointAsync("faction/basic",key));
            if(profile!=CurrentProfileId)return;
            var report=WarPayouts.Read(await api.FetchEndpointAsync($"faction/{id}/rankedwarreport",key),id,faction.Id);
            if(profile!=CurrentProfileId)return;
            var closing=FactionIntelligence.ReadBasic(await api.FetchEndpointAsync("faction/basic",key));
            if(profile!=CurrentProfileId)return;
            if(closing.Id!=faction.Id)throw new InvalidDataException("Faction changed during scan.");
            Storage.SaveAtomic(WarPayoutPath(),report);_warPayout=report;
        } finally {_warPayoutScanning=false;UpdateModernDashboard();}
    }
    private void SaveWarPayout(JsonElement request){
        var j=GetWarPayout();
        if(_warPayoutScanning||request.GetProperty("profileId").GetString()!=CurrentProfileId||
           request.GetProperty("warId").GetInt64()!=j.WarId||j.WarId<=0)
            throw new InvalidDataException("Scan or profile changed. Review the report.");
        var next=new WarPayoutJournal {WarId=j.WarId,FactionId=j.FactionId,End=j.End,Members=j.Members,ScannedUtc=j.ScannedUtc,
            Budget=request.GetProperty("budget").GetInt64(),AttackPercent=request.GetProperty("attackPercent").GetInt32(),
            MinimumAttacks=request.GetProperty("minimumAttacks").GetInt32(),
            Cap=request.GetProperty("cap").ValueKind==JsonValueKind.Null?null:request.GetProperty("cap").GetInt64()};
        WarPayouts.Calculate(next);
        Storage.SaveAtomic(WarPayoutPath(),next);_warPayout=next;UpdateModernDashboard();
    }
    private object WarPayoutData(){
        var j=GetWarPayout();
        var rows=WarPayouts.Calculate(j);
        return new {warId=j.WarId,factionId=j.FactionId,end=j.End,budget=j.Budget,attackPercent=j.AttackPercent,
            minimumAttacks=j.MinimumAttacks,cap=j.Cap,rows,unallocated=j.Budget-rows.Sum(x=>x.Amount),
            scanning=_warPayoutScanning};
    }
    private void SaveCsvReport(string name,IEnumerable<string> rows){
        using var dialog=new SaveFileDialog {Filter="CSV spreadsheet (*.csv)|*.csv",FileName=name+".csv",OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        File.WriteAllText(dialog.FileName,string.Join("\r\n",rows)+"\r\n",new UTF8Encoding(true));
        FlashStatus("CSV report saved.");
    }
    private void ExportWarPayout(){
        var j=GetWarPayout();
        if(j.WarId<=0)throw new InvalidOperationException("Scan a ranked war first.");
        SaveCsvReport("Commander_War_"+j.WarId,
            new[]{ReportCsv.Row("War ID","Faction ID","Player ID","Name","Attacks","War score","Proposed payout","Eligible")}
            .Concat(WarPayouts.Calculate(j).Select(x=>ReportCsv.Row(j.WarId,j.FactionId,x.Id,x.Name,x.Attacks,x.Score,x.Amount,x.Eligible))));
    }
    private void ExportCompanyCsv(){
        var j=GetCompanyJournal();
        SaveCsvReport("Commander_Company_"+j.ActiveCompanyId,
            new[]{ReportCsv.Row("UTC date","Company ID","Income","Advertising","Payroll","Stock cost","Accounted margin")}
            .Concat(j.Days.Where(x=>x.CompanyId==j.ActiveCompanyId).OrderBy(x=>x.DayUtc)
                .Select(x=>ReportCsv.Row(x.DayUtc,x.CompanyId,x.DailyIncome,x.DailyAdvertising,x.DailyPayroll,x.DailyStockCost,x.FullCostMargin))));
    }
    private void ExportStocksCsv(){
        var j=GetStockJournal();
        SaveCsvReport("Commander_Stocks_"+DateTime.UtcNow.ToString("yyyyMMdd"),
            new[]{ReportCsv.Row("Scanned UTC","Stock ID","Symbol","Name","Price","Shares","Cost basis","Net unrealized P/L after 0.1% fee")}
            .Concat(j.Quotes.Select(q=>{
                var h=j.Holdings?.FirstOrDefault(x=>x.Id==q.Id);
                return ReportCsv.Row(j.ScannedUtc.ToString("O"),q.Id,q.Symbol,q.Name,q.Price,
                    j.Holdings==null?null:h?.Shares??0,h?.Cost,StockIntelligence.NetProfit(h,q.Price));
            })));
    }
}
