using System.Text.Json;
using System.Globalization;
namespace TornCommander;
internal static class LevelingScouting {
 public static string Suitability(LevelingTarget t,LevelingSession s,DateTime now,bool checkActivity=true){
  if(System.Text.RegularExpressions.Regex.IsMatch(t.Name,@"^Test Dummy #\d+$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return "Mission-only NPC - excluded";
  if(!double.IsFinite(s.OwnStats)||s.OwnStats<=0||s.OwnStatsUtc==null||now-s.OwnStatsUtc>TimeSpan.FromMinutes(15)||s.OwnStatsUtc>now.AddMinutes(1))return "Refresh your battle stats";
  if(!double.IsFinite(s.MaxStatRatio)||s.MaxStatRatio is <=0 or >1||s.MaxEstimateDays is <1 or >365)return "Invalid strength filter";
  if(t.EstimatedStats is not double stats||!double.IsFinite(stats)||stats<=0||t.EstimateUtc==null||t.ScannedUtc==null)return "Unknown strength — excluded";
  if(t.EstimateUtc>now.AddMinutes(5)||now-t.EstimateUtc>TimeSpan.FromDays(s.MaxEstimateDays)||now-t.ScannedUtc>TimeSpan.FromHours(24)||t.ScannedUtc>now.AddMinutes(1))return "Stale estimate — rescan required";
  if(t.FairFight is double ff&&(!double.IsFinite(ff)||ff>s.MaxFairFight))return "Above Fair Fight limit for supplied key";
  if(stats>s.OwnStats*s.MaxStatRatio)return "Too strong for your limit";
  if(checkActivity&&s.InactiveOnly&&(t.LastActionUtc==null||t.LastActionUtc>now||now-t.LastActionUtc<TimeSpan.FromDays(14)))return "Active or activity unknown — excluded";
  return "Within strength limit (estimate)";
 }
 public static DateTime? HospitalRetryAt(LevelingTarget t)=>t.Status=="Skipped: Hospital"&&t.UnavailableUntilUtc is DateTime until?new DateTime(Math.Max(until.Ticks,(t.CheckedUtc??DateTime.MinValue).AddSeconds(30).Ticks),DateTimeKind.Utc):null;
 public static bool CanProbe(LevelingTarget t,DateTime now)=>HospitalRetryAt(t) is DateTime retry?retry<=now:!t.Finished&&t.UnavailableUntilUtc.GetValueOrDefault(DateTime.MinValue)<=now;
 public static bool StrengthEligible(LevelingTarget t,LevelingSession s,DateTime now)=>Suitability(t,s,now,false)=="Within strength limit (estimate)";
 public static bool Eligible(LevelingTarget t,LevelingSession s,DateTime now)=>Suitability(t,s,now)=="Within strength limit (estimate)";
 public static double? Number(JsonElement x,string key)=>x.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)&&double.IsFinite(n)?n:null;
 public static DateTime? Time(JsonElement x,string key){var n=Number(x,key);if(n is null or <=0||n>253402300799)return null;return DateTimeOffset.FromUnixTimeSeconds((long)n.Value).UtcDateTime;}
 public static void ApplyStats(IEnumerable<LevelingTarget> targets,string json,DateTime now){
  using var d=JsonDocument.Parse(json);if(d.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("FFScouter returned an unsupported stats response.");
  var rows=d.RootElement.EnumerateArray().Where(x=>Number(x,"player_id") is double id&&id>0&&id<=int.MaxValue&&id==Math.Truncate(id)).GroupBy(x=>(int)Number(x,"player_id")!.Value).Where(x=>x.Count()==1).ToDictionary(x=>x.Key,x=>x.Single());
  foreach(var t in targets){t.EstimatedStats=null;t.EstimateUtc=null;t.FairFight=null;t.EstimateSource="FFScouter";t.ScannedUtc=now;
   if(!rows.TryGetValue(t.Id,out var row))continue;
   t.EstimatedStats=Number(row,"bs_estimate");t.EstimateUtc=Time(row,"last_updated");t.FairFight=Number(row,"fair_fight");
   if(row.TryGetProperty("source",out var source)&&source.ValueKind==JsonValueKind.String)t.EstimateSource="FFScouter / "+source.GetString();
  }
 }
 public static List<LevelingTarget> SearchResults(string json,int self){
  using var d=JsonDocument.Parse(json);var rows=d.RootElement.GetProperty("targets");if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>50)throw new InvalidDataException("Invalid FFScouter target results.");
  return rows.EnumerateArray().Where(x=>Number(x,"player_id") is double id&&id>0&&id<=int.MaxValue&&id==Math.Truncate(id)).Select(x=>new LevelingTarget{Id=(int)Number(x,"player_id")!.Value,Name=x.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?n.GetString()??"":"",Level=(int)(Number(x,"level")??0),Source="FFScouter target search",LastActionUtc=Time(x,"last_action")}).Where(x=>x.Id!=self).DistinctBy(x=>x.Id).ToList();
 }
}
internal static class FFScouterProvider {
 private static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25),MaxResponseContentBufferSize=2097152};
 private static readonly SemaphoreSlim Gate=new(1,1);private static DateTime Next=DateTime.MinValue;
 public static string DecodeResponse(string route,int status,string body){
  using var doc=JsonDocument.Parse(body);
  var root=doc.RootElement;
  int? code=root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("code",out var v)&&v.TryGetInt32(out int n)?n:null;
  if(route=="get-targets"&&status==404&&code==17)return "{\"targets\":[]}";
  if(status is <200 or >=300)throw new InvalidDataException("FFScouter request failed (HTTP "+status+(code.HasValue?", provider code "+code:"")+"). Check the connection, registration or provider limits.");
  if(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("error",out _))throw new InvalidDataException("FFScouter could not provide this data. Check registration, permissions or service limits.");
  return body;
 }
 public static async Task<string> GetAsync(string key,string route,string query){
  if(key.Length!=16||!key.All(char.IsLetterOrDigit)||route is not ("get-stats" or "get-targets" or "check-key"))throw new InvalidDataException("Use a registered 16-character FFScouter key.");
  await Gate.WaitAsync();try{
   var wait=Next-DateTime.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait);
   using var request=new HttpRequestMessage(HttpMethod.Get,"https://ffscouter.com/api/v1/"+route+"?key="+Uri.EscapeDataString(key)+"&"+query);
   request.Headers.UserAgent.ParseAdd("TornCommander-Scouting/1.0");
   HttpResponseMessage response;try{response=await Http.SendAsync(request);}catch{throw new InvalidDataException("FFScouter connection failed. Your queue was kept; try again later.");}
   using(response){Next=DateTime.UtcNow.AddSeconds(3);
    if((int)response.StatusCode==429){Next=DateTime.UtcNow.AddSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds??60,3,3600));throw new InvalidDataException("FFScouter rate limit reached. Wait before rescanning.");}
    var body=await response.Content.ReadAsStringAsync();
    return DecodeResponse(route,(int)response.StatusCode,body);
   }
  }finally{Gate.Release();}
 }
}
