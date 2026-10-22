using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm
{
    private sealed class MedicalSession
    {
        public string Id=Guid.NewGuid().ToString("N"),Order="",Note="Waiting for a hospital observation",ModifierNote="Not scanned",Pending="";
        public int Player,LossesAtStart,ItemsUsed,CooldownUsed,Saved,EpisodeBaseline,EpisodeCost,LastHospitalSeconds,LastHealCost;
        public DateTime PendingUntil,LastRequest,PerksAt;
        public string Perks="";
        public MedicalObservation? Observation;
        public MedicalPlan? Plan;
        public MedicalItem? Item;
        public bool Busy,Issued,Verified;
        public Dictionary<int,int> Before=new();
    }
    private readonly Dictionary<LossAutoCycleWorker,MedicalSession> _medicalSessions=new();
    private SmartMedicalSettings MedicalSettings(string profile)=>_settings.SmartMedicalProfiles.GetValueOrDefault(profile)??new();
    private string BeginMedicalSession(LossAutoCycleWorker worker,int player,string order){
        var o=_lossOrders.First(x=>x.Id==order);var session=new MedicalSession{Player=player,Order=order,LossesAtStart=worker.Slot=="A"?o.CompletedA:o.CompletedB};
        _medicalSessions[worker]=session;return session.Id;
    }
    private async Task SaveMedicalSettingsAsync(JsonElement request){
        string profile=request.GetProperty("profileId").GetString()??"";
        if(!_settings.Profiles.Any(x=>x.Id==profile))throw new InvalidDataException("Unknown profile.");
        if(new[]{_lossWorkerA,_lossWorkerB}.Any(w=>w?.IsArmed==true&&w.ProfileId==profile))throw new InvalidOperationException("Stop this profile's loss worker before changing medical permissions; then save and restart.");
        var settings=JsonSerializer.Deserialize<SmartMedicalSettings>(request.GetProperty("settings").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Missing medical settings.");settings.Validate();
        _settings.SmartMedicalProfiles[profile]=settings;Storage.SaveSettings(_settings);UpdateModernDashboard();
        await Task.CompletedTask;FlashStatus("Medical settings saved for this profile. Drugs and faction supplies remain permanently disabled.");
    }
    private async Task<MedicalObservation> ReadMedicalObservationAsync(LossAutoCycleWorker worker,MedicalSession session,Dictionary<int,int> inventory){
        var key=CredentialManager.LoadApiKey(worker.ProfileId);if(string.IsNullOrWhiteSpace(key))throw new InvalidDataException("This profile needs a Torn API key.");
        using var profile=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key));var p=profile.RootElement.GetProperty("profile");
        if(p.GetProperty("id").GetInt32()!=session.Player)throw new InvalidDataException("API account changed; no medical action allowed.");
        var status=p.GetProperty("status");var state=status.GetProperty("state").GetString();DateTime until=DateTime.UtcNow;
        if(string.Equals(state,"Hospital",StringComparison.OrdinalIgnoreCase)){
            until=DateTimeOffset.FromUnixTimeSeconds(status.GetProperty("until").GetInt64()).UtcDateTime;
            var description=status.ToString();if(description.Contains("radiation",StringComparison.OrdinalIgnoreCase)||description.Contains("poison",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Nonstandard hospitalization; review manually. No medical or drug fallback used.");
        }else if(!string.Equals(state,"Okay",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Account is not in a verified Hospital/Okay state.");
        using var cooldown=JsonDocument.Parse(await _api.FetchEndpointAsync("user/cooldowns",key));int seconds=cooldown.RootElement.GetProperty("cooldowns").GetProperty("medical").GetInt32();
        var settings=MedicalSettings(worker.ProfileId);
        if(settings.AutoDetect&&DateTime.UtcNow-session.PerksAt>TimeSpan.FromMinutes(5)){
            session.Perks=await _api.FetchEndpointAsync("user/perks",key);session.PerksAt=DateTime.UtcNow;
        }
        var mods=SmartMedicalOptimizer.Modifiers(session.Perks,settings);session.ModifierNote=mods.Status;
        // Manual mode still cannot assert blood eligibility; auto detection must prove it.
        return new(worker.ProfileId,session.Player,DateTime.UtcNow,until,seconds,mods.Maximum,mods.Bonus,mods.Iv,mods.Status,inventory);
    }
    private bool MedicalContext(LossAutoCycleWorker worker,MedicalSession s,JsonElement request)=>worker.IsArmed&&ReferenceEquals(worker,worker.Slot=="A"?_lossWorkerA:_lossWorkerB)&&_lossOrders.Any(o=>o.Id==s.Order&&o.Status!="Paused"&&(worker.Slot=="A"?o.ProfileAId:o.ProfileBId)==worker.ProfileId)&&ReadWorkerString(request,"medicalSession")==s.Id&&ReadWorkerString(request,"profileId")==worker.ProfileId&&ReadWorkerString(request,"orderId")==s.Order&&request.TryGetProperty("browserPlayerId",out var p)&&p.TryGetInt32(out int id)&&id==s.Player;
    private static Dictionary<int,int> ReadMedicalInventory(JsonElement r){
        var result=new Dictionary<int,int>();foreach(var row in r.GetProperty("inventory").EnumerateArray()){
            int id=row.GetProperty("id").GetInt32(),q=row.GetProperty("quantity").GetInt32();
            if(!SmartMedicalOptimizer.AllowedId(id)||q<0||q>100000000||!result.TryAdd(id,q))throw new InvalidDataException("Invalid or non-allowlisted personal medical inventory.");
        }return result;
    }
    private async Task HandleMedicalMessageAsync(LossAutoCycleWorker worker,JsonElement request){
        string type=ReadWorkerString(request,"type"),rid=ReadWorkerString(request,"requestId");
        if(!_medicalSessions.TryGetValue(worker,out var s)||!MedicalContext(worker,s,request)){
            await worker.ReplyMedicalAsync(rid,new{state="Paused",reason="Account/session identity could not be verified. No item authorized."});return;
        }
        if(s.Busy){await worker.ReplyMedicalAsync(rid,new{state="Paused",reason="A medical verification is already running. No additional item was authorized."});return;}s.Busy=true;
        try{
            var settings=MedicalSettings(worker.ProfileId);settings.Validate();
            var inventory=ReadMedicalInventory(request);
            if(type=="tc-medical-plan"){
                if(!string.IsNullOrEmpty(s.Pending)&&s.Issued&&!s.Verified)throw new InvalidDataException("A prior item dispatch is unverified; do not retry consumption. Stop and review the worker diagnostic.");
                if(DateTime.UtcNow-s.LastRequest<TimeSpan.FromSeconds(3))throw new InvalidDataException("Medical refresh requested too quickly; pause and review.");s.LastRequest=DateTime.UtcNow;
                var observation=await ReadMedicalObservationAsync(worker,s,inventory);
                if(!MedicalContext(worker,s,request))return;
                s.Observation=observation;s.Plan=SmartMedicalOptimizer.Plan(observation,settings,DateTime.UtcNow);s.Note=s.Plan.Reason;
                if(s.EpisodeBaseline==0&&s.Plan.State=="Ready"){s.EpisodeBaseline=s.Plan.FakBaselineSeconds;s.LastHospitalSeconds=s.Plan.HospitalSeconds;}
                s.Pending="";s.Issued=false;s.Verified=false;
                if(!settings.Enabled&&s.Plan.State!="Recovered")throw new InvalidDataException("Smart Medical Management is disabled. Heal manually; no automatic fallback is permitted.");
                if(s.Plan.State!="Ready"){
                    s.Item=null;if(s.Plan.State=="Recovered"){s.EpisodeBaseline=0;s.EpisodeCost=0;}
                    await worker.ReplyMedicalAsync(rid,new{state=s.Plan.State,reason=s.Plan.Reason,waitUntil=s.Plan.AvailableUtc,autoResume=settings.Enabled&&settings.Automatic&&settings.AutoResume});return;
                }
                s.Item=SmartMedicalOptimizer.Item(s.Plan.Items[0]);s.Before=inventory;s.Pending=Guid.NewGuid().ToString("N");s.PendingUntil=DateTime.UtcNow.AddSeconds(40);
                await worker.ReplyMedicalAsync(rid,new{state="Ready",reason=s.Plan.Reason,token=s.Pending,itemId=s.Item.Id,name=s.Item.Name,quantity=inventory.GetValueOrDefault(s.Item.Id),expires=s.PendingUntil,automatic=settings.Enabled&&settings.Automatic,medicalSession=s.Id});
            }else if(type=="tc-medical-authorize"){
                if(!settings.Enabled||!settings.Automatic||s.Item is null||s.Observation is null||ReadWorkerString(request,"token")!=s.Pending||s.Issued||DateTime.UtcNow>s.PendingUntil)throw new InvalidDataException("Medical permission expired or is recommendation-only.");
                if(inventory.GetValueOrDefault(s.Item.Id)!=s.Before.GetValueOrDefault(s.Item.Id)){s.Pending="";await worker.ReplyMedicalAsync(rid,new{state="Replan",reason="Personal inventory changed before dispatch; recalculating."});return;}
                // The plan's API snapshot is still fresh while opening the item accordion.
                // Avoid repeating two queued API reads immediately before the same dispatch.
                // Inventory and browser identity were checked again above; after-use verification
                // always obtains a new API snapshot.
                var fresh=DateTime.UtcNow-s.Observation.ObservedUtc<=TimeSpan.FromSeconds(10)
                    ?s.Observation with{Inventory=inventory}
                    :await ReadMedicalObservationAsync(worker,s,inventory);
                var plan=SmartMedicalOptimizer.Plan(fresh,settings,DateTime.UtcNow);
                if(!MedicalContext(worker,s,request))return;
                if(plan.State!="Ready"||plan.Items.FirstOrDefault()!=s.Item.Id){s.Pending="";await worker.ReplyMedicalAsync(rid,new{state="Replan",reason="Hospital/cooldown changed before dispatch; recalculating."});return;}
                s.Observation=fresh;s.Issued=true;s.PendingUntil=DateTime.UtcNow.AddSeconds(30);
                await worker.ReplyMedicalAsync(rid,new{state="Authorized",token=s.Pending,itemId=s.Item.Id,expires=s.PendingUntil});
            }else if(type=="tc-medical-verify"){
                if(s.Item is null||s.Observation is null||ReadWorkerString(request,"token")!=s.Pending||!s.Issued)throw new InvalidDataException("No matching issued medical operation.");
                if(s.Verified){await worker.ReplyMedicalAsync(rid,new{state="Verified",token=s.Pending});return;}
                if(inventory.GetValueOrDefault(s.Item.Id)!=s.Before.GetValueOrDefault(s.Item.Id)-1)throw new InvalidDataException("Medical quantity did not decrease by exactly one; consumption remains unverified.");
                if(!inventory.ContainsKey(s.Item.Id))inventory[s.Item.Id]=0;
                var after=await ReadMedicalObservationAsync(worker,s,inventory);if(!MedicalContext(worker,s,request))return;
                if(!SmartMedicalOptimizer.VerifyUse(s.Observation,after,s.Item.Id,DateTime.UtcNow))throw new InvalidDataException("Item/cooldown/hospital changes disagree (or API is stale). Paused; never repeat the item blindly.");
                s.Verified=true;s.ItemsUsed++;s.CooldownUsed+=s.Item.CooldownSeconds;s.EpisodeCost+=s.Item.CooldownSeconds;s.Observation=after;
                if(after.HospitalUntilUtc<=DateTime.UtcNow){s.Saved+=s.EpisodeBaseline-s.EpisodeCost;s.LastHealCost=s.EpisodeCost;s.EpisodeBaseline=0;s.EpisodeCost=0;}
                s.Note="Verified "+s.Item.Name+"; replanning from observed state.";
                try{IntelStore.Snapshot(worker.ProfileId,"medical-session",s.Id,DateTime.UtcNow,new{session=s.Id,slot=worker.Slot,itemId=s.Item.Id,used=s.ItemsUsed,cooldownSeconds=s.CooldownUsed,savedSeconds=s.Saved,verified=true});}catch{s.Note+=" Local audit storage unavailable; live verification succeeded.";}
                await worker.ReplyMedicalAsync(rid,new{state="Verified",token=s.Pending});
            }
        }catch(Exception ex){s.Note=ex is InvalidDataException?ex.Message:"Medical read/verification failed. Check API permissions and visible inventory; no item retry or drug fallback.";try{IntelStore.Snapshot(worker.ProfileId,"medical-failure",s.Id,DateTime.UtcNow,new{session=s.Id,slot=worker.Slot,stage=type,reason=s.Note,verified=false});}catch{}await worker.ReplyMedicalAsync(rid,new{state="Paused",reason=s.Note});}
        finally{s.Busy=false;UpdateLossDashboardStatus();}
    }
    private object MedicalDashboard()=>new{
        profiles=_settings.Profiles.Select(p=>new{id=p.Id,name=p.Name,settings=MedicalSettings(p.Id)}).ToList(),
        sessions=_medicalSessions.Where(x=>x.Key.IsArmed).Select(pair=>{var w=pair.Key;var s=pair.Value;var o=s.Observation;var order=_lossOrders.FirstOrDefault(x=>x.Id==s.Order);int losses=order==null?0:Math.Max(0,(w.Slot=="A"?order.CompletedA:order.CompletedB)-s.LossesAtStart);int hospital=o==null?0:Math.Max(0,(int)(o.HospitalUntilUtc-DateTime.UtcNow).TotalSeconds);int cd=o==null?0:Math.Max(0,o.CooldownSeconds-(int)(DateTime.UtcNow-o.ObservedUtc).TotalSeconds);
            var cfg=MedicalSettings(w.ProfileId);
            var permitted=o?.Inventory.Where(x=>SmartMedicalOptimizer.AllowedId(x.Key)&&x.Value>cfg.Reserves.GetValueOrDefault(x.Key)&&cfg.Permissions.GetValueOrDefault(x.Key)&&(x.Key!=739||(cfg.BloodBags&&o.IvVerified))).ToList();
            double supplyHealing=permitted?.Sum(x=>Math.Max(0,x.Value-cfg.Reserves.GetValueOrDefault(x.Key))*SmartMedicalOptimizer.Item(x.Key).HospitalSeconds*(1+o!.BonusPercent/100))??0;
            int extra=cfg.ReserveMinutes>0?0:(permitted?.Select(x=>SmartMedicalOptimizer.Item(x.Key).CooldownSeconds).DefaultIfEmpty(0).Max()??0)-1;
            double? estimate=o==null||s.LastHospitalSeconds<=0||s.LastHealCost<=0?null:Math.Min(Math.Max(0,(o.MaximumSeconds-cfg.ReserveMinutes*60-cd+extra)/s.LastHealCost),supplyHealing/s.LastHospitalSeconds);
            return new{slot=w.Slot,profile=w.ProfileId,session=s.Id,note=s.Note,verification=s.ModifierNote,observed=o?.ObservedUtc,hospitalSeconds=o==null?(int?)null:hospital,cooldownSeconds=o==null?(int?)null:cd,maximumSeconds=o?.MaximumSeconds,bonus=o?.BonusPercent,inventory=o?.Inventory,next=s.Item?.Name,plannedCooldownSeconds=s.Plan?.CooldownSeconds,losses,itemsUsed=s.ItemsUsed,cooldownUsed=s.CooldownUsed,savedSeconds=s.Saved,estimatedHeals=estimate};}).ToList()
    };
}
