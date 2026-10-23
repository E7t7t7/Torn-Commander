using System.Text.Json;
using System.Globalization;
namespace TornCommander;
public sealed partial class MainForm {
 private async Task RefreshLevelingStrengthAsync(string profile){
  var key=CredentialManager.LoadApiKey(profile)??throw new InvalidDataException("Missing Torn key.");
  using var doc=JsonDocument.Parse(await _api.FetchEndpointAsync("user/battlestats",key));
  double total=doc.RootElement.GetProperty("battlestats").GetProperty("total").GetDouble();
  if(!double.IsFinite(total)||total<=0)throw new InvalidDataException("Your battle stats could not be verified. A Limited key with battle stats access is required.");
  var s=LevelingFor(profile);s.OwnStats=total;s.OwnStatsUtc=DateTime.UtcNow;
 }
 private async Task<string> ScouterKeyAsync(string profile,int player){
  string key=CredentialManager.LoadScouterKey(profile)??throw new InvalidDataException("Save a registered FFScouter key for this account first.");
  using var doc=JsonDocument.Parse(await FFScouterProvider.GetAsync(key,"check-key",""));
  if(!doc.RootElement.TryGetProperty("is_registered",out var registered)||registered.ValueKind!=JsonValueKind.True)throw new InvalidDataException("Register this key with FFScouter first.");
  if(doc.RootElement.TryGetProperty("policy_update_required",out var policy)&&policy.ValueKind==JsonValueKind.True)throw new InvalidDataException("FFScouter requires a policy update. Review it on their website before using this key.");
  return key;
 }
 private async Task ScanLevelingTargetsAsync(string profile,List<LevelingTarget> targets,string key){
  for(int i=0;i<targets.Count;i+=205){
   if(CurrentProfileId!=profile)throw new InvalidDataException("Account changed; scan stopped.");
   var batch=targets.Skip(i).Take(205).ToList();
   string json=await FFScouterProvider.GetAsync(key,"get-stats","targets="+string.Join(",",batch.Select(x=>x.Id)));
   LevelingScouting.ApplyStats(batch,json,DateTime.UtcNow);
   var s=LevelingFor(profile);foreach(var t in batch)t.Suitability=LevelingScouting.Suitability(t,s,DateTime.UtcNow);
   s.Note="Scanned "+Math.Min(i+205,targets.Count)+"/"+targets.Count+" strength estimates. Live status is checked before each fight.";UpdateModernDashboard();
  }
 }
 private async Task LevelingScoutActionAsync(string action,JsonElement root,string profile){
  var s=LevelingFor(profile);
  if(s.OpenedUtc!=null)throw new InvalidDataException("Resolve the pending fight before changing scouting settings or lists.");
  if(action=="levelScoutKey"){
   if(root.TryGetProperty("remove",out var remove)&&remove.GetBoolean())CredentialManager.ForgetScouterKey(profile);
   else CredentialManager.SaveScouterKey(profile,ReadWorkerString(root,"key"));
   s.Note="FFScouter credential updated locally for this account.";return;
  }
  if(action=="levelScoutSave"){
   double ratio=root.GetProperty("ratio").GetDouble(),ff=root.GetProperty("maxff").GetDouble();int days=root.GetProperty("days").GetInt32();
   if(!double.IsFinite(ratio)||ratio is <0.05 or >1||!double.IsFinite(ff)||ff is <1 or >3||days is <1 or >365)throw new InvalidDataException("Choose 5–100% target strength, FF 1–3 and estimate age 1–365 days.");
   s.MaxStatRatio=ratio;s.MaxFairFight=ff;s.MaxEstimateDays=days;s.InactiveOnly=root.GetProperty("inactive").GetBoolean();
   foreach(var t in s.Targets)t.Suitability=LevelingScouting.Suitability(t,s,DateTime.UtcNow);
   s.Note="Strength filters saved. Unknown, stale and over-limit targets cannot be opened automatically.";return;
  }
  var a=await ReadLevelingAccountAsync(profile);await RefreshLevelingStrengthAsync(profile);
  string key=await ScouterKeyAsync(profile,a.Player);
  if(action=="levelScoutSearch"){
   string query="minlevel=1&maxlevel=100&minff=1&maxff="+s.MaxFairFight.ToString(CultureInfo.InvariantCulture)+"&inactiveonly="+(s.InactiveOnly?"1":"0")+"&limit=50";
   var targets=LevelingScouting.SearchResults(await FFScouterProvider.GetAsync(key,"get-targets",query),a.Player);
   await ScanLevelingTargetsAsync(profile,targets,key);
   if(CurrentProfileId!=profile)throw new InvalidDataException("Account changed; existing queue kept.");
   s.Targets=targets.OrderByDescending(x=>LevelingScouting.Eligible(x,s,DateTime.UtcNow)).ThenByDescending(x=>x.Level).ToList();s.PendingTarget=0;s.BaldrSource="";s.BaldrLoadedUtc=null;
  }else await ScanLevelingTargetsAsync(profile,s.Targets,key);
  foreach(var t in s.Targets)t.Suitability=LevelingScouting.Suitability(t,s,DateTime.UtcNow);
  s.Note=s.Targets.Count(t=>LevelingScouting.StrengthEligible(t,s,DateTime.UtcNow))+" / "+s.Targets.Count+" targets within your strength limit (live activity still checked before fighting). Your total: "+s.OwnStats.ToString("N0")+"; target cap: "+(s.OwnStats*s.MaxStatRatio).ToString("N0")+". Estimates cannot guarantee a win; equipment and stat distribution matter.";
 }
}
