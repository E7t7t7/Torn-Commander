using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm{
 private readonly Dictionary<string,CancellationTokenSource> _gearScans=new();
 private readonly Dictionary<string,string> _gearProgress=new();
 private GearJournal GearFor(string profile)=>IntelStore.Load(profile,"gear",new GearJournal());
 private object GearDashboard(){
  var j=GearFor(CurrentProfileId);var now=DateTime.UtcNow;
  return new{profileId=CurrentProfileId,busy=_gearScans.ContainsKey(CurrentProfileId),note=_gearProgress.GetValueOrDefault(CurrentProfileId,j.Note),j.Settings,j.ScannedUtc,j.TypesScanned,j.BazaarsScanned,j.Warnings,
   offers=j.Offers.OrderByDescending(o=>o.StatsKnown&&o.BonusKnown).ThenBy(o=>o.Price).Take(200).ToList(),
   builds=j.Builds,stale=j.Builds.Any(b=>b.Items.Any(o=>!GearOptimizer.Fresh(o,now)))||(j.ScannedUtc.HasValue&&now-j.ScannedUtc.Value>TimeSpan.FromMinutes(75)),
   unknown=j.Offers.Count(o=>!o.StatsKnown||!o.BonusKnown),quoted=j.Offers.Count,
   oldest=j.Offers.Select(o=>(DateTime?)o.ObservedUtc).Min()};
 }
 private async Task HandleGear(string action,JsonElement r){
  string profile=ReadWorkerString(r,"profileId");if(profile!=CurrentProfileId)throw new InvalidOperationException("Selected account changed. Review Gear for this profile.");
  if(action=="gearCancel"){if(_gearScans.TryGetValue(profile,out var running))running.Cancel();return;}
  if(action=="gearOpen"){
   var j=GearFor(profile);var key=ReadWorkerString(r,"key");var offer=j.Offers.FirstOrDefault(o=>o.Key==key)??throw new InvalidDataException("Listing no longer exists in this saved scan.");
   await AddBrowserTabAsync(offer.Url);return;
  }
  if(_gearScans.ContainsKey(profile))return;
  var s=new GearSettings{Budget=GoalNumber(r,"budget",1,1000000000000L),Reserve=GoalNumber(r,"reserve",0,1000000000000L),Scope=ReadWorkerString(r,"scope"),Bias=ReadWorkerString(r,"bias"),
   Depth=r.GetProperty("depth").GetInt32(),DiscoverBazaars=r.GetProperty("discover").GetBoolean(),AllowBonus=r.GetProperty("bonus").GetBoolean(),Sellers=ReadWorkerString(r,"sellers"),Items=ReadWorkerString(r,"items")};
  GearOptimizer.Validate(s);
  var sellers=new List<int>();foreach(var text in s.Sellers.Split([',',';',' '],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)){
   if(!int.TryParse(text,out var id)||id<=0)throw new InvalidDataException("Enter positive bazaar owner IDs separated by commas.");if(!sellers.Contains(id))sellers.Add(id);
  }
  if(sellers.Count>8||s.Items.Length>2000)throw new InvalidDataException("Use at most eight bazaar owners and a short extra-item list.");
  string apiKey=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this profile's Torn API key in Settings first.");
  using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(6));_gearScans[profile]=cancel;
  void Progress(string note){_gearProgress[profile]=note;if(profile==CurrentProfileId)UpdateModernDashboard();}
  var journal=new GearJournal{Settings=s};
  try{
   var ct=cancel.Token;Progress("Loading the official weapon and armour catalog…");
   var weapons=await _api.FetchEndpointAsync("torn/items?cat=Weapon",apiKey,ct);
   var armor=await _api.FetchEndpointAsync("torn/items?cat=Armor",apiKey,ct);
   var catalog=GearOptimizer.Catalog(weapons).Concat(GearOptimizer.Catalog(armor)).ToList();
   var shortlist=GearOptimizer.Shortlist(catalog,s);if(shortlist.Count==0)throw new InvalidDataException("No supported gear categories were returned.");
   int done=0;
   foreach(var item in shortlist){
    ct.ThrowIfCancellationRequested();Progress("Item Market "+(++done)+"/"+shortlist.Count+": "+item.Name+" · API requests are paced; quotes may be globally cached.");
    try{var json=await _api.FetchEndpointAsync("market/"+item.Id+"/itemmarket?limit=100&offset=0",apiKey,ct);journal.Offers.AddRange(GearOptimizer.Market(json,item,DateTime.UtcNow).Where(o=>o.Price<=s.Budget-s.Reserve));journal.TypesScanned++;}
    catch(OperationCanceledException){throw;}catch{journal.Warnings.Add(item.Name+": market request failed or access/rate limit prevented this item scan.");if(TornRequestCoordinator.Shared.CooldownSeconds>0)break;}
   }
   if(s.DiscoverBazaars&&TornRequestCoordinator.Shared.CooldownSeconds==0){
    foreach(var cat in new[]{"Defensive",s.Scope=="melee"?"Melee":"Primary"}){
     try{Progress("Finding public "+cat+" bazaar directory entries…");var directory=await _api.FetchEndpointAsync("market/bazaar?cat="+cat,apiKey,ct);
      foreach(var id in GearOptimizer.Directory(directory).Take(2))if(!sellers.Contains(id)&&sellers.Count<8)sellers.Add(id);
     }catch(OperationCanceledException){throw;}catch{journal.Warnings.Add("Bazaar directory unavailable for "+cat+". Your entered sellers can still be checked.");}
    }
   }
   foreach(var seller in sellers){
    ct.ThrowIfCancellationRequested();if(TornRequestCoordinator.Shared.CooldownSeconds>0){journal.Warnings.Add("Bazaar scanning stopped during Torn's rate-limit cooldown.");break;}
    Progress("Checking bazaar "+(journal.BazaarsScanned+1)+"/"+sellers.Count+"…");
    try{var json=await _api.FetchBazaarGearAsync(seller,apiKey,ct);journal.Offers.AddRange(GearOptimizer.Bazaar(json,catalog.Where(x=>GearOptimizer.Allowed(x,s)).ToList(),seller,DateTime.UtcNow).Where(o=>o.Price<=s.Budget-s.Reserve));journal.BazaarsScanned++;}
    catch(OperationCanceledException){throw;}catch{journal.Warnings.Add("A selected bazaar could not be read. It may be closed, unavailable or unsupported.");}
   }
   var missing=journal.Offers.Where(o=>!o.StatsKnown&&o.Uid>0).Select(o=>o.Uid).Distinct().Take(100).ToArray();
   foreach(var batch in missing.Chunk(25)){
    ct.ThrowIfCancellationRequested();if(TornRequestCoordinator.Shared.CooldownSeconds>0)break;
    try{Progress("Verifying individual equipment stats…");var json=await _api.FetchEndpointAsync("torn/"+string.Join(",",batch)+"/itemstats",apiKey,ct);GearOptimizer.ApplyStats(json,journal.Offers);}
    catch(OperationCanceledException){throw;}catch{journal.Warnings.Add("Some individual stats could not be verified; those offers remain review-only.");}
   }
   ct.ThrowIfCancellationRequested();Progress("Comparing affordable combinations…");
   journal.Builds=await Task.Run(()=>GearOptimizer.Build(journal.Offers,s,DateTime.UtcNow),ct);ct.ThrowIfCancellationRequested();
   journal.ScannedUtc=DateTime.UtcNow;
   journal.Note="Compared "+journal.TypesScanned+" item types and "+journal.BazaarsScanned+" selected/directory bazaars. Best found among this bounded scan, not every listing in Torn. Buying and equipping remain manual.";
   if(journal.BazaarsScanned>0&&!journal.Offers.Any(o=>o.Seller>0))journal.Warnings.Add("The checked bazaars returned no usable gear offers. Directory membership does not guarantee current stock.");
   journal.Warnings.Add("Market API quotes can be cached for about an hour. Check the price, UID, stats and availability in Torn before buying.");
   journal.Warnings.Add("Scores are base-stat/coverage proxies. Your battle stats, weapon experience, merits, ammunition/reloads, set bonuses and item bonus effects are not simulated.");
   journal.Warnings.Add("Armour coverage is compared conservatively by body part. Overlapping protection is not added; slot classification is inferred from item names/coverage.");
   if(journal.Offers.Count==0)throw new InvalidDataException("No affordable listings were returned. Increase the budget, add item names/sellers or try later; previous results are preserved.");
   IntelStore.Save(profile,"gear",journal);_gearProgress[profile]=journal.Note;
  }catch(OperationCanceledException){_gearProgress[profile]="Scan cancelled or timed out. Your previous completed scan is preserved.";}
  catch(Exception ex){_gearProgress[profile]=ex is InvalidDataException or InvalidOperationException?ex.Message:"Gear scan failed. Saved results were preserved; check API access and try later.";}
  finally{_gearScans.Remove(profile);if(profile==CurrentProfileId)UpdateModernDashboard();}
 }
}
