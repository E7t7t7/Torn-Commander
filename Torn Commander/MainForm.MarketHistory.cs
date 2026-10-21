using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm
{
    private readonly Dictionary<string,int> _marketWatchCursor=new();
    private async Task CollectWatchMarketAsync(string profile,ItemIntelligenceSettings settings)
    {
        if(!settings.MarketQuotes)return;
        var now=DateTime.UtcNow;const string source="Official Torn item market";
        var health=IntelStore.Load(profile,"provider:"+source,new ProviderHealth(source,"Not checked",null,null,""));
        if(health.NextAttemptUtc>now)return;
        var key=CredentialManager.LoadApiKey(profile);
        if(string.IsNullOrWhiteSpace(key)){IntelStore.Save(profile,"provider:"+source,new ProviderHealth(source,"Torn API key needed",health.LastSuccessUtc,now.AddMinutes(5),"Save this profile's Torn API key; no third-party key is required."));return;}
        var watches=settings.Watches.DistinctBy(x=>x.ItemId).ToList();if(watches.Count==0)return;
        int cursor=_marketWatchCursor.GetValueOrDefault(profile)%watches.Count,count=Math.Min(5,watches.Count);int success=0;
        for(int index=0;index<count;index++){
            var watch=watches[(cursor+index)%watches.Count];
            try{
                var scan=await _api.FetchItemMarketAsync(watch.ItemId,1,0,key);
                if(scan.Status=="No item market data"||scan.ItemId!=watch.ItemId)throw new InvalidDataException("Market item mismatch");
                var listings=scan.Tiers.Where(x=>x.Price>0&&x.Amount>0).ToList();
                var quote=new MarketHistoryPoint(watch.ItemId,watch.Name,DateTime.UtcNow,listings.Count>0?listings.Min(x=>x.Price):null,listings.Sum(x=>(long)x.Amount));
                var before=IntelStore.Snapshots(profile,"official-item-market",watch.ItemId.ToString(),1).Select(x=>JsonSerializer.Deserialize<MarketHistoryPoint>(x)).FirstOrDefault();
                IntelStore.Snapshot(profile,"official-item-market",watch.ItemId.ToString(),quote.ObservedUtc,quote);success++;
                if(profile==CurrentProfileId)foreach(var alert in settings.Watches.Where(w=>w.ItemId==watch.ItemId).SelectMany(w=>MarketPriceAlerts.Crossings(w,before,quote,DateTime.UtcNow)).Distinct()){
                    _itemNotifier??=new NotifyIcon{Icon=SystemIcons.Information,Visible=true,Text="Torn Commander item intelligence"};_itemNotifier.ShowBalloonTip(8000,watch.Name,alert+". Asking prices are not completed sales.",ToolTipIcon.Info);
                }
            }catch{
                IntelStore.Save(profile,"provider:"+source,new ProviderHealth(source,"Read failed",health.LastSuccessUtc,DateTime.UtcNow.AddMinutes(10),"Check item-market permission or shared API cooldown; existing price history is retained."));break;
            }
        }
        IntelStore.PruneSnapshots(profile,"official-item-market",settings.RetentionDays);
        _marketWatchCursor[profile]=(cursor+count)%watches.Count;
        if(success==count)IntelStore.Save(profile,"provider:"+source,new ProviderHealth(source,"Available",DateTime.UtcNow,DateTime.UtcNow.AddMinutes(Math.Max(1,settings.PollMinutes)),"Up to five watched item IDs per poll, rotating through the list. Prices are the lowest returned asks, not executed trades or a complete bazaar scan."));
    }
    private object WatchMarketData(int item){var points=IntelStore.Snapshots(CurrentProfileId,"official-item-market",item.ToString(),200).Select(x=>JsonSerializer.Deserialize<MarketHistoryPoint>(x)).Where(x=>x!=null).Cast<MarketHistoryPoint>().OrderBy(x=>x.ObservedUtc).ToList();return new{latest=points.LastOrDefault(),points=points.Select(x=>new{at=x.ObservedUtc,price=x.LowestReturnedAsk,quantity=x.ReturnedQuantity}).ToList()};}
    private Task ExportMarketHistoryAsync(){var profile=CurrentProfileId;return ExportIntelligenceCsvAsync("commander-market-asks",new[]{ReportCsv.Row("source","itemId","name","observedUtc","lowestReturnedAsk","returnedQuantity")}.Concat(IntelStore.ExportSnapshots(profile,"official-item-market").Select(json=>JsonSerializer.Deserialize<MarketHistoryPoint>(json)).Where(x=>x!=null).Select(x=>ReportCsv.Row(x!.Source,x.ItemId,x.Name,x.ObservedUtc.ToString("O"),x.LowestReturnedAsk,x.ReturnedQuantity))));}
    private void MirrorTradingQuotes(string profile,IEnumerable<TradeQuote> quotes){try{foreach(var q in quotes){var tiers=q.Tiers.Where(x=>x.Price>0&&x.Quantity>0).ToList();IntelStore.Snapshot(profile,"official-item-market",q.ItemId.ToString(),q.ObservedUtc,new MarketHistoryPoint(q.ItemId,q.Name,q.ObservedUtc,tiers.Count>0?tiers.Min(x=>x.Price):null,tiers.Sum(x=>(long)x.Quantity)));}IntelStore.PruneSnapshots(profile,"official-item-market",ItemSettings(profile).RetentionDays);if(profile==CurrentProfileId)InvalidateItemDashboard();}catch{_tradeScanNote="Trading scan saved; long-term SQLite history mirror unavailable.";}}
}
