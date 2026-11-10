using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly Dictionary<string,JsonElement> _cityScans=new();
 private readonly HashSet<string> _cityActionBusy=new();
 private object CityDashboard()=>new{profileId=CurrentProfileId,scan=_cityScans.TryGetValue(CurrentProfileId,out var value)?(object)value:null};
 private async Task HandleCityAsync(string action,JsonElement root){
  var profile=ReadWorkerString(root,"profileId");if(profile!=CurrentProfileId)throw new InvalidOperationException("Selected account changed. Scan again.");
  if(action=="cityOpen"){await AddBrowserTabAsync(TornRoutes.City);return;}
  var tab=Active();if(tab?.WebView.CoreWebView2==null)throw new InvalidOperationException("Open City Map first.");
  var source=tab.WebView.Source;
  if(source==null||source.Scheme!="https"||!TornRoutes.IsTornHost(source.Host)||source.AbsolutePath!="/city.php")throw new InvalidOperationException("Select the City Map game tab first.");
  var path=Path.Combine(AppContext.BaseDirectory,"BundledScripts","Torn_City_Scanner.js");
  await tab.ExecuteScriptAsync(await File.ReadAllTextAsync(path));
  if(profile!=CurrentProfileId||tab!=Active())return;
  if(action is "cityScan" or "cityCollect"){
   if(!_cityActionBusy.Add(profile)){FlashStatus("A city action is already running. No extra item clicked.");return;}
   string? raw;
   try{
    raw=await tab.ExecuteScriptAsync(action=="cityCollect"?"window.__TornCommanderCity.collectNext("+JsonSerializer.Serialize(ReadWorkerString(root,"ticket"))+")":"window.__TornCommanderCity.scan()");
   }finally{_cityActionBusy.Remove(profile);}
   using var d=JsonDocument.Parse(raw??"null");
   if(profile!=CurrentProfileId||tab!=Active())return;
   if(d.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("City scan did not return a result.");
   _cityScans[profile]=d.RootElement.Clone();FlashStatus(d.RootElement.GetProperty("note").GetString()??"City scanned.");
  }else if(action=="cityFocus"){
   int index=root.GetProperty("index").GetInt32();
   var ok=await tab.ExecuteScriptAsync("window.__TornCommanderCity.focus("+index+")");
   if(ok!="true")FlashStatus("Marker changed or was collected. Scan the current map again.");
  }else if(action=="cityClear"){await tab.ExecuteScriptAsync("window.__TornCommanderCity.clear()");_cityScans.Remove(profile);}
  UpdateModernDashboard();
 }
}
