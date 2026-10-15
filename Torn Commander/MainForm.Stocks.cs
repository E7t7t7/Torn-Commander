using System.Text.Json;
namespace TornCommander;

public sealed partial class MainForm
{
    private StockJournal? _stockJournal;
    private string _stockProfile="";
    private bool _stockScanning;
    private string StockJournalPath()
    {
        string safe=new(CurrentProfileId.Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_').ToArray());
        return Path.Combine(Storage.Root,"Stocks",(safe.Length==0?"main":safe)+".json");
    }
    private StockJournal GetStockJournal()
    {
        if(_stockJournal==null||_stockProfile!=CurrentProfileId) {
            _stockProfile=CurrentProfileId;
            _stockJournal=Storage.Load(StockJournalPath(),new StockJournal());
        }
        return _stockJournal;
    }
    private async Task ScanStocksAsync()
    {
        if(_stockScanning) return;
        string profile=CurrentProfileId;
        var key=CredentialManager.LoadApiKey(profile);
        if(string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Save this profile's Torn API key first.");
        _stockScanning=true; UpdateModernDashboard();
        try {
            var api=new TornApiClient();
            var quotes=StockIntelligence.ReadQuotes(await api.FetchEndpointAsync("torn/stocks",key));
            if(profile!=CurrentProfileId)return;
            List<StockHolding>? holdings=null; string warning="";
            try { holdings=StockIntelligence.ReadHoldings(await api.FetchEndpointAsync("user/stocks",key)); }
            catch {warning=" Holdings unavailable; check key access. Previous holdings are not presented as current.";}
            if(profile!=CurrentProfileId)return;
            var old=GetStockJournal();
            var fresh=new StockJournal {Quotes=quotes,Holdings=holdings,Targets=old.Targets,
                History=old.History.ToList(),ScannedUtc=DateTime.UtcNow,Status="Stock scan saved."+warning};
            StockIntelligence.Record(fresh,fresh.ScannedUtc);
            Storage.SaveAtomic(StockJournalPath(),fresh);
            _stockJournal=fresh;
        }
        catch {if(profile==CurrentProfileId)GetStockJournal().Status="Stock scan failed. Saved quotes may be old; retry after the API cooldown.";}
        finally {_stockScanning=false;UpdateModernDashboard();}
    }
    private void SaveStockTarget(JsonElement request)
    {
        if(request.GetProperty("profileId").GetString()!=CurrentProfileId||_stockScanning)
            throw new InvalidDataException("Profile changed or scan in progress; review the current stocks.");
        long id=request.GetProperty("id").GetInt64();
        var journal=GetStockJournal();
        if(!journal.Quotes.Any(x=>x.Id==id))throw new InvalidDataException("Scan this stock first.");
        decimal? Price(string field) {
            var v=request.GetProperty(field);
            if(v.ValueKind==JsonValueKind.Null)return null;
            if(v.ValueKind!=JsonValueKind.Number||!v.TryGetDecimal(out var n)||n<0||n>1000000000000m)
                throw new InvalidDataException("Use a nonnegative price or leave blank.");
            return n;
        }
        var target=new StockTarget {Buy=Price("buy"),Sell=Price("sell"),Payout=Price("payout"),Days=request.GetProperty("days").GetInt32()};
        if(target.Days<1||target.Days>365)throw new InvalidDataException("Payout interval must be 1–365 days.");
        var old=journal.Targets.GetValueOrDefault(id);journal.Targets[id]=target;
        try {Storage.SaveAtomic(StockJournalPath(),journal);}
        catch {if(old==null)journal.Targets.Remove(id);else journal.Targets[id]=old;throw;}
        UpdateModernDashboard();
    }
}
