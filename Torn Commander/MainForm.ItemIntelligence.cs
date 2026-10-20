using System.Globalization;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
namespace TornCommander;
public sealed partial class MainForm
{
    private IntelligenceStore? _intelligence;
    private bool _itemPolling;
    private DateTime _itemNextUtc;
    private readonly Dictionary<string,int> _itemFailures=new();
    private string _itemStatus="Enable monitoring or refresh to start collecting real stock observations.";
    private NotifyIcon? _itemNotifier;
    private readonly Dictionary<string,string> _itemAlertSeen=new();
    private static DateTime _intelKeyNextUtc;
    private IntelligenceStore IntelStore=>_intelligence??=new IntelligenceStore(Path.Combine(Storage.Root,"Intelligence","history.sqlite"));
    private ItemIntelligenceSettings ItemSettings(string? profile=null)=>IntelStore.Load(profile??CurrentProfileId,"item-settings",new ItemIntelligenceSettings());
    private void TickItemMonitor(){if(_itemPolling||DateTime.UtcNow<_itemNextUtc)return;try{if(ItemSettings().Monitor){_itemNextUtc=DateTime.UtcNow.AddMinutes(5);_=PollItemIntelligenceAsync();}}catch{_itemNextUtc=DateTime.UtcNow.AddMinutes(10);}}
    private void SaveItemSettings(JsonElement r){
        if(r.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed; review settings.");
        var s=ItemSettings();s.Monitor=r.GetProperty("monitor").GetBoolean();s.UseTornIntel=r.GetProperty("intel").GetBoolean();s.PollMinutes=Math.Clamp(r.GetProperty("minutes").GetInt32(),1,60);s.RetentionDays=Math.Clamp(r.GetProperty("retention").GetInt32(),7,365);
        IntelStore.Save(CurrentProfileId,"item-settings",s);_itemNextUtc=DateTime.MinValue;InvalidateItemDashboard();UpdateModernDashboard();
    }
    private void SaveIntelCredential(JsonElement r){
        if(r.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed.");
        bool client=r.GetProperty("client").GetBoolean();bool remove=r.GetProperty("remove").GetBoolean();
        if(remove)CredentialManager.ForgetIntelKey(CurrentProfileId,client);else CredentialManager.SaveIntelKey(CurrentProfileId,r.GetProperty("key").GetString()??"",client);
        if(client){IntelStore.Save(CurrentProfileId,"provider:Torn Intel",new ProviderHealth("Torn Intel","Not checked",null,null,"Approved application key required; not a player login."));_itemNextUtc=DateTime.MinValue;}
        FlashStatus(remove?"Credential disconnected on this Windows profile.":"Credential saved in Windows Credential Manager. No key is included in reports.");InvalidateItemDashboard();UpdateModernDashboard();
    }
    private void SaveItemWatch(JsonElement r){
        if(r.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed.");
        int id=r.GetProperty("itemId").GetInt32();string loc=r.GetProperty("location").GetString()??"";if(id<=0||loc.Length is <1 or >80)throw new InvalidDataException("Choose an observed item and location.");
        var s=ItemSettings();s.Watches.RemoveAll(x=>x.ItemId==id&&x.Location==loc);
        if(!r.GetProperty("remove").GetBoolean()){
            long? Optional(string key,long limit){if(!r.TryGetProperty(key,out var x)||x.ValueKind==JsonValueKind.Null)return null;var n=x.GetInt64();if(n<0||n>limit)throw new InvalidDataException("Invalid alert threshold.");return n;}
            s.Watches.Add(new(){ItemId=id,Location=loc,Name=(r.GetProperty("name").GetString()??"")[..Math.Min(120,(r.GetProperty("name").GetString()??"").Length)],BackInStock=r.GetProperty("back").GetBoolean(),BelowQuantity=(int?)Optional("below",int.MaxValue),AboveQuantity=(int?)Optional("above",int.MaxValue),PriceBelow=Optional("priceBelow",1000000000000),PriceAbove=Optional("priceAbove",1000000000000),ForecastAlerts=r.GetProperty("forecast").GetBoolean()});
        }
        if(s.Watches.Count>100)throw new InvalidDataException("Watchlist limit is 100 items.");IntelStore.Save(CurrentProfileId,"item-settings",s);InvalidateItemDashboard();UpdateModernDashboard();
    }
    private async Task PollItemIntelligenceAsync(){
        if(_itemPolling)return;_itemPolling=true;var profile=CurrentProfileId;try{
            var settings=ItemSettings(profile);var sources=settings.UseTornIntel?new[]{"Torn Intel","YATA"}:new[]{"YATA"};bool success=false;
            foreach(var source in sources){
                var health=IntelStore.Load(profile,"provider:"+source,new ProviderHealth(source,"Not checked",null,null,""));
                if(health.Status=="Approval required"||health.NextAttemptUtc>DateTime.UtcNow)continue;
                if(source=="Torn Intel"&&DateTime.UtcNow<_intelKeyNextUtc)continue;
                try{
                    if(source=="Torn Intel")_intelKeyNextUtc=DateTime.UtcNow.AddMinutes(1);
                    var provider=new ForeignStockProvider(source,source=="Torn Intel"?CredentialManager.LoadIntelKey(profile,true):null);
                    var rows=await provider.FetchAsync();var before=IntelStore.Latest();
                    int added=IntelStore.Add(rows,DateTime.UtcNow);var now=DateTime.UtcNow;
                    IntelStore.Save(profile,"provider:"+source,new ProviderHealth(source,"Available",now,now.AddMinutes(source=="Torn Intel"?Math.Max(1,settings.PollMinutes):Math.Max(1,settings.PollMinutes)),"Stock report times are shown separately from fetch time."));
                    _itemFailures[profile+source]=0;success=true;
                    if(profile==CurrentProfileId){NotifyItemChanges(settings,before,rows);_itemStatus=source+": "+added+" new timestamped observations saved.";}
                }catch(Exception ex){
                    int failures=_itemFailures.GetValueOrDefault(profile+source)+1;_itemFailures[profile+source]=failures;
                    var failure=ex as ProviderFailure;var next=failure?.RetryUtc??DateTime.UtcNow.AddMinutes(Math.Min(60,Math.Pow(2,Math.Min(failures,6))));
                    string status=failure?.Message??(ex is InvalidDataException or JsonException?"Invalid provider response":"Connection failed");
                    IntelStore.Save(profile,"provider:"+source,new ProviderHealth(source,status,health.LastSuccessUtc,next,status=="Approval required"?"Save an approved client key to retry. Website login keys do not grant stock API access.":"Saved history remains available; background retries use backoff."));
                    if(profile==CurrentProfileId)_itemStatus=source+": "+status+". Cached history retained.";
                }
            }
            if(success)IntelStore.Prune(_settings.Profiles.Select(x=>ItemSettings(x.Id).RetentionDays).DefaultIfEmpty(settings.RetentionDays).Max());
            _itemNextUtc=DateTime.UtcNow.AddMinutes(Math.Clamp(settings.PollMinutes,1,60));
        }catch{if(profile==CurrentProfileId)_itemStatus="History database unavailable. Existing Commander modules remain usable.";_itemNextUtc=DateTime.UtcNow.AddMinutes(10);}
        finally{_itemPolling=false;if(profile==CurrentProfileId)InvalidateItemDashboard();UpdateModernDashboard();}
    }
    private void NotifyItemChanges(ItemIntelligenceSettings s,IEnumerable<ItemObservation> before,IEnumerable<ItemObservation> after){
        var saved=IntelStore.Load(CurrentProfileId,"item-alert-receipts",new Dictionary<string,string>());
        foreach(var pair in saved)_itemAlertSeen[pair.Key]=pair.Value;
        foreach(var w in s.Watches)foreach(var item in after.Where(x=>x.ItemId==w.ItemId&&x.Location==w.Location)){
            var old=before.Where(x=>x.Source==item.Source&&x.Location==item.Location&&x.ItemId==item.ItemId).OrderBy(x=>x.ObservedUtc).LastOrDefault();
            var alerts=ItemAlertRules.Crossings(w,old,item,DateTime.UtcNow);
            if(w.ForecastAlerts&&item.Quantity==0){var estimate=RestockEstimator.Analyze(IntelStore.Read(item.ItemId,item.Location,s.RetentionDays).Where(x=>x.Source==item.Source).Append(item).DistinctBy(x=>x.ObservedUtc),DateTime.UtcNow);if(estimate.FromUtc is DateTime start&&estimate.ToUtc>=DateTime.UtcNow&&start<=DateTime.UtcNow.AddMinutes(15))alerts.Add("Estimated restock window approaching ("+estimate.Confidence+" confidence)");}
            foreach(var alert in alerts){string key=CurrentProfileId+item.Source+item.Location+w.ItemId+alert;string stamp=item.ObservedUtc.ToString("O");if(_itemAlertSeen.TryGetValue(key,out var seen)&&(seen==stamp||(alert.StartsWith("Estimated")&&DateTime.TryParse(seen,out var last)&&DateTime.UtcNow-last.ToUniversalTime()<TimeSpan.FromHours(1))))continue;_itemAlertSeen[key]=stamp;
                _itemNotifier??=new NotifyIcon{Icon=SystemIcons.Information,Visible=true,Text="Torn Commander item intelligence"};_itemNotifier.ShowBalloonTip(8000,w.Name+" · "+w.Location,alert,ToolTipIcon.Info);
            }
        }
        IntelStore.Save(CurrentProfileId,"item-alert-receipts",_itemAlertSeen.Where(x=>x.Key.StartsWith(CurrentProfileId)).TakeLast(1000).ToDictionary(x=>x.Key,x=>x.Value));
    }
    private void ImportItemHistory(){
        using var dialog=new OpenFileDialog{Filter="Stock history CSV|*.csv",Title="Import timestamped stock observations"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        if(new FileInfo(dialog.FileName).Length>20000000)throw new InvalidDataException("CSV exceeds 20 MB.");
        using var parser=new TextFieldParser(dialog.FileName){TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true};parser.SetDelimiters(",");var columns=parser.ReadFields()??Array.Empty<string>();var names=columns.Select((name,index)=>(name,index)).ToDictionary(x=>x.name.Trim(),x=>x.index,StringComparer.OrdinalIgnoreCase);
        foreach(var key in new[]{"source","location","itemId","name","observedUtc","quantity","cost"})if(!names.ContainsKey(key))throw new InvalidDataException("CSV requires source,location,itemId,name,observedUtc,quantity,cost; optional marketValue.");
        var rows=new List<ItemObservation>();while(!parser.EndOfData){var f=parser.ReadFields()!;string Get(string key)=>names.TryGetValue(key,out var i)&&i<f.Length?f[i]:"";var text=Get("observedUtc");if(!(text.EndsWith('Z')||System.Text.RegularExpressions.Regex.IsMatch(text,@"[+-]\d{2}:\d{2}$"))||!DateTimeOffset.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.None,out var observed))throw new InvalidDataException("Each timestamp needs UTC Z or an explicit offset.");
            if(!int.TryParse(Get("itemId"),out var id)||!int.TryParse(Get("quantity"),out var qty)||!long.TryParse(Get("cost"),out var cost))throw new InvalidDataException("Invalid numeric CSV field.");long? market=null;if(Get("marketValue").Length>0){if(!long.TryParse(Get("marketValue"),out var m))throw new InvalidDataException("Invalid market value.");market=m;}
            rows.Add(new("CSV:"+Get("source"),Get("location"),id,Get("name"),observed.UtcDateTime,DateTime.UtcNow,qty,cost,market));if(rows.Count>50000)throw new InvalidDataException("CSV exceeds 50000 rows.");}
        int added=IntelStore.Add(rows,DateTime.UtcNow);_itemStatus="Imported "+added+" actual observations. Import sources remain separate from live providers.";InvalidateItemDashboard();UpdateModernDashboard();
    }
    private void ExportItemHistory(){var rows=IntelStore.Read();SaveCsvReport("commander-item-history",new[]{ReportCsv.Row("source","location","itemId","name","observedUtc","quantity","cost","marketValue")}.Concat(rows.Select(x=>ReportCsv.Row(x.Source,x.Location,x.ItemId,x.Name,x.ObservedUtc.ToString("O"),x.Quantity,x.Cost,x.MarketValue))));}
    private object? _itemDashboardCache;
    private string _itemDashboardProfile="";
    private DateTime _itemDashboardAt;
    private void InvalidateItemDashboard()=>_itemDashboardCache=null;
    private object ItemIntelligenceDashboard(){if(_itemDashboardCache!=null&&_itemDashboardProfile==CurrentProfileId&&DateTime.UtcNow-_itemDashboardAt<TimeSpan.FromSeconds(15))return _itemDashboardCache;_itemDashboardProfile=CurrentProfileId;_itemDashboardAt=DateTime.UtcNow;return _itemDashboardCache=BuildItemIntelligenceDashboard();}
    private object BuildItemIntelligenceDashboard(){try{
        var s=ItemSettings();var latest=IntelStore.Latest();var rows=s.Watches.SelectMany(w=>IntelStore.Read(w.ItemId,w.Location,s.RetentionDays)).ToList();
        var catalog=latest.GroupBy(x=>(x.Location,x.ItemId)).Select(g=>g.OrderByDescending(x=>x.ObservedUtc).First()).OrderBy(x=>x.Name).Take(1000).Select(x=>new{id=x.ItemId,name=x.Name,location=x.Location}).ToList();
        if(!catalog.Any(x=>x.id==361&&x.location=="Switzerland"))catalog.Add(new{id=361,name="Neumune Tablet",location="Switzerland"});
        var cards=s.Watches.Select(w=>new{watch=w,providers=latest.Where(x=>x.ItemId==w.ItemId&&x.Location==w.Location).Select(x=>new{observation=x,stale=DateTime.UtcNow-x.ObservedUtc>TimeSpan.FromMinutes(20),forecast=RestockEstimator.Analyze(rows.Where(y=>y.Source==x.Source&&y.Location==x.Location&&y.ItemId==x.ItemId),DateTime.UtcNow),points=rows.Where(y=>y.Source==x.Source&&y.Location==x.Location&&y.ItemId==x.ItemId).TakeLast(100).Select(y=>new{at=y.ObservedUtc,quantity=y.Quantity,price=y.MarketValue}).ToList()}).ToList()}).ToList();
        return new{status=_itemStatus,monitor=s.Monitor,minutes=s.PollMinutes,retention=s.RetentionDays,useIntel=s.UseTornIntel,polling=_itemPolling,catalog,cards,count=IntelStore.Count(),health=new[]{"YATA","Torn Intel"}.Select(id=>IntelStore.Load(CurrentProfileId,"provider:"+id,new ProviderHealth(id,"Not checked",null,null,""))).ToList(),clientSaved=!string.IsNullOrWhiteSpace(CredentialManager.LoadIntelKey(CurrentProfileId,true)),loginSaved=!string.IsNullOrWhiteSpace(CredentialManager.LoadIntelKey(CurrentProfileId,false))};
        }catch{return new{status="Intelligence storage is unavailable. Existing modules are unaffected.",cards=Array.Empty<object>(),catalog=Array.Empty<object>(),health=Array.Empty<object>()};}}
}
