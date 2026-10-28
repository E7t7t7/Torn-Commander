using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm
{
    private sealed class MedicalSession
    {
        public string Id=Guid.NewGuid().ToString("N"),Order="",Note="Waiting for a hospital observation",ModifierNote="Not scanned",Pending="";
        public int Player,LossesAtStart,ItemsUsed,CooldownUsed,Saved,EpisodeBaseline,EpisodeCost,LastHospitalSeconds,LastHealCost,VerifyAttempts;
        public DateTime PendingUntil,LastRequest,PerksAt;
        public string Perks="";
        public MedicalObservation? Observation;
        public MedicalPlan? Plan;
        public MedicalItem? Item;
        public bool Busy,Issued,Verified,Leveling;
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
        using var profile=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,fresh:true));var p=profile.RootElement.GetProperty("profile");
        if(p.GetProperty("id").GetInt32()!=session.Player)throw new InvalidDataException("API account changed; no medical action allowed.");
        var status=p.GetProperty("status");var state=status.GetProperty("state").GetString();DateTime until=DateTime.UtcNow;
        if(string.Equals(state,"Hospital",StringComparison.OrdinalIgnoreCase)){
            until=DateTimeOffset.FromUnixTimeSeconds(status.GetProperty("until").GetInt64()).UtcDateTime;
            var description=status.ToString();if(description.Contains("radiation",StringComparison.OrdinalIgnoreCase)||description.Contains("poison",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Nonstandard hospitalization; review manually. No medical or drug fallback used.");
        }else if(!string.Equals(state,"Okay",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Account is not in a verified Hospital/Okay state.");
        using var cooldown=JsonDocument.Parse(await _api.FetchEndpointAsync("user/cooldowns",key,fresh:true));int seconds=cooldown.RootElement.GetProperty("cooldowns").GetProperty("medical").GetInt32();
        var settings=MedicalSettings(worker.ProfileId);
        if(settings.AutoDetect&&DateTime.UtcNow-session.PerksAt>TimeSpan.FromMinutes(5)){
            session.Perks=await _api.FetchEndpointAsync("user/perks",key);session.PerksAt=DateTime.UtcNow;
        }
        var mods=SmartMedicalOptimizer.Modifiers(session.Perks,settings);session.ModifierNote=mods.Status;
        // Manual mode still cannot assert blood eligibility; auto detection must prove it.
        var observed=new MedicalObservation(worker.ProfileId,session.Player,DateTime.UtcNow,until,seconds,mods.Maximum,mods.Bonus,mods.Iv,mods.Status,inventory,session.Leveling?p.GetProperty("life").GetProperty("current").GetInt32():0,session.Leveling?p.GetProperty("life").GetProperty("maximum").GetInt32():0);
        if(session.Leveling&&_levelingAccounts.TryGetValue(worker.ProfileId,out var account))_levelingAccounts[worker.ProfileId]=account with{Life=observed.Life,Maximum=observed.MaximumLife,Status=state??"Unknown",Medical=observed,Recovery=LevelingPlanner.Recovery(observed,settings,observed.Life,observed.MaximumLife,LevelingFor(worker.ProfileId).LifePercent,DateTime.UtcNow),At=DateTime.UtcNow};
        return observed;
    }
    private bool MedicalContext(LossAutoCycleWorker worker,MedicalSession s,JsonElement request)=>worker.IsArmed&&ReferenceEquals(worker,worker.Slot=="A"?_lossWorkerA:_lossWorkerB)&&(s.Leveling?(_levelingRuns.TryGetValue(worker,out var run)&&run.Active&&run.Id==s.Order):_lossOrders.Any(o=>o.Id==s.Order&&o.Status!="Paused"&&(worker.Slot=="A"?o.ProfileAId:o.ProfileBId)==worker.ProfileId))&&ReadWorkerString(request,"medicalSession")==s.Id&&ReadWorkerString(request,"profileId")==worker.ProfileId&&ReadWorkerString(request,"orderId")==s.Order&&request.TryGetProperty("browserPlayerId",out var p)&&p.TryGetInt32(out int id)&&id==s.Player;
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
                // Authorization is not proof of a click. Recover a pre-dispatch interruption
                // only when the matching browser operation and fresh API evidence prove no use.
                bool unresolved=!string.IsNullOrEmpty(s.Pending)&&s.Issued&&!s.Verified;
                if(unresolved&&(s.Item is null||s.Observation is null||ReadWorkerString(request,"operationToken")!=s.Pending||!request.TryGetProperty("medicalDispatched",out var dispatched)||dispatched.ValueKind!=JsonValueKind.False))throw new InvalidDataException("The previous healing operation still needs verification. Resume that operation before another attack; if its outcome cannot be verified, stop and review. Your healing permissions are enabled.");
                if(DateTime.UtcNow-s.LastRequest<TimeSpan.FromSeconds(3))throw new InvalidDataException("Medical refresh requested too quickly; pause and review.");s.LastRequest=DateTime.UtcNow;
                var observation=request.TryGetProperty("continueVerifiedMedical",out var continuation)&&continuation.ValueKind==JsonValueKind.True&&s.Verified&&s.Observation is not null&&DateTime.UtcNow-s.Observation.ObservedUtc<=TimeSpan.FromSeconds(8)
                    ?s.Observation with{Inventory=inventory}
                    :await ReadMedicalObservationAsync(worker,s,inventory);
                if(!MedicalContext(worker,s,request))return;
                if(unresolved&&!SmartMedicalOptimizer.VerifyUnused(s.Observation!,observation,s.Item!.Id,DateTime.UtcNow))throw new InvalidDataException("Could not prove the previous healing permission was unused. No second item authorized; stop and review the item count and cooldown.");
                s.Observation=observation;s.Plan=WorkerMedicalPlan(worker,s,observation,settings);s.Note=s.Plan.Reason;
                if(s.EpisodeBaseline==0&&s.Plan.State=="Ready"){s.EpisodeBaseline=s.Plan.FakBaselineSeconds;s.LastHospitalSeconds=s.Plan.HospitalSeconds;}
                s.Pending="";s.Issued=false;s.Verified=false;s.VerifyAttempts=0;
                if(!settings.Enabled&&s.Plan.State!="Recovered")throw new InvalidDataException("Smart Medical Management is disabled. Heal manually; no automatic fallback is permitted.");
                if(s.Plan.State!="Ready"){
                    s.Item=null;if(s.Plan.State=="Recovered"){s.EpisodeBaseline=0;s.EpisodeCost=0;}
                    await worker.ReplyMedicalAsync(rid,new{state=s.Plan.State,reason=s.Plan.Reason,waitUntil=s.Plan.AvailableUtc,autoResume=settings.Enabled&&WorkerAutomaticMedical(worker,s,settings)&&(s.Leveling?LevelingFor(worker.ProfileId).AutoResumeMedical:settings.AutoResume)});return;
                }
                s.Item=SmartMedicalOptimizer.Item(s.Plan.Items[0]);s.Before=inventory;s.Pending=Guid.NewGuid().ToString("N");s.PendingUntil=DateTime.UtcNow.AddSeconds(40);
                await worker.ReplyMedicalAsync(rid,new{state="Ready",reason=s.Plan.Reason,token=s.Pending,itemId=s.Item.Id,name=s.Item.Name,quantity=inventory.GetValueOrDefault(s.Item.Id),expires=s.PendingUntil,automatic=settings.Enabled&&WorkerAutomaticMedical(worker,s,settings),medicalSession=s.Id});
            }else if(type=="tc-medical-authorize"){
                if(!settings.Enabled||!WorkerAutomaticMedical(worker,s,settings))throw new InvalidDataException("Automatic healing is disabled for this account. Save Automatic mode in its medical settings.");
                if(s.Item is null||s.Observation is null||ReadWorkerString(request,"token")!=s.Pending)throw new InvalidDataException("Healing request does not match this account's pending operation; no item authorized.");
                if(s.Issued)throw new InvalidDataException("This healing operation was already authorized. Verify its outcome before requesting another use; your saved permissions are enabled.");
                if(DateTime.UtcNow>s.PendingUntil){s.Pending="";await worker.ReplyMedicalAsync(rid,new{state="Replan",reason="The unused healing plan expired; refreshing hospital and cooldown."});return;}
                if(inventory.GetValueOrDefault(s.Item.Id)!=s.Before.GetValueOrDefault(s.Item.Id)){s.Pending="";await worker.ReplyMedicalAsync(rid,new{state="Replan",reason="Personal inventory changed before dispatch; recalculating."});return;}
                // The plan's API snapshot is still fresh while opening the item accordion.
                // Avoid repeating two queued API reads immediately before the same dispatch.
                // Inventory and browser identity were checked again above; after-use verification
                // always obtains a new API snapshot.
                var fresh=DateTime.UtcNow-s.Observation.ObservedUtc<=TimeSpan.FromSeconds(10)
                    ?s.Observation with{Inventory=inventory}
                    :await ReadMedicalObservationAsync(worker,s,inventory);
                var plan=WorkerMedicalPlan(worker,s,fresh,settings);
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
                if(!SmartMedicalOptimizer.VerifyUse(s.Observation,after,s.Item.Id,DateTime.UtcNow)){
                    double elapsed=(after.ObservedUtc-s.Observation.ObservedUtc).TotalSeconds;
                    var verification=new{itemId=s.Item.Id,attempt=s.VerifyAttempts+1,beforeObserved=s.Observation.ObservedUtc,afterObserved=after.ObservedUtc,beforeQuantity=s.Before.GetValueOrDefault(s.Item.Id),afterQuantity=inventory.GetValueOrDefault(s.Item.Id),beforeCooldown=s.Observation.CooldownSeconds,afterCooldown=after.CooldownSeconds,expectedCooldownCost=s.Item.CooldownSeconds,minimumExpectedCooldown=Math.Max(0,s.Observation.CooldownSeconds+s.Item.CooldownSeconds-elapsed),maximumExpectedCooldown=Math.Max(0,s.Observation.CooldownSeconds-elapsed)+s.Item.CooldownSeconds,observedCooldownDelta=after.CooldownSeconds-Math.Max(0,s.Observation.CooldownSeconds-elapsed),beforeHospitalUntil=s.Observation.HospitalUntilUtc,afterHospitalUntil=after.HospitalUntilUtc,beforeLife=s.Observation.Life,afterLife=after.Life,maximumLife=after.MaximumLife,bonus=s.Observation.BonusPercent};
                    try{IntelStore.Snapshot(worker.ProfileId,"medical-verification",s.Id,DateTime.UtcNow,verification);}catch{}
                    if(++s.VerifyAttempts<=6){await worker.ReplyMedicalAsync(rid,new{state="PendingVerification",token=s.Pending,verification,retryAfterMs=s.VerifyAttempts<=2?2500:8000,reason="Item count decreased once. Waiting for Torn's cooldown/hospital snapshot; no further item click."});return;}
                    s.Note="Medical receipt still disagrees after six refreshes: cooldown "+s.Observation.CooldownSeconds+" -> "+after.CooldownSeconds+" seconds, expected item cost "+s.Item.CooldownSeconds+". Copy worker diagnostic. No repeat use.";
                    await worker.ReplyMedicalAsync(rid,new{state="Paused",verification,reason=s.Note});return;
                }
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
        sessions=_medicalSessions.Where(x=>x.Key.IsArmed&&!x.Value.Leveling).Select(pair=>{var w=pair.Key;var s=pair.Value;var o=s.Observation;var order=_lossOrders.FirstOrDefault(x=>x.Id==s.Order);int losses=order==null?0:Math.Max(0,(w.Slot=="A"?order.CompletedA:order.CompletedB)-s.LossesAtStart);int hospital=o==null?0:Math.Max(0,(int)(o.HospitalUntilUtc-DateTime.UtcNow).TotalSeconds);int cd=o==null?0:Math.Max(0,o.CooldownSeconds-(int)(DateTime.UtcNow-o.ObservedUtc).TotalSeconds);
            var cfg=MedicalSettings(w.ProfileId);
            var permitted=o?.Inventory.Where(x=>SmartMedicalOptimizer.AllowedId(x.Key)&&x.Value>cfg.Reserves.GetValueOrDefault(x.Key)&&cfg.Permissions.GetValueOrDefault(x.Key)&&(x.Key!=739||(cfg.BloodBags&&o.IvVerified))).ToList();
            double supplyHealing=permitted?.Sum(x=>Math.Max(0,x.Value-cfg.Reserves.GetValueOrDefault(x.Key))*SmartMedicalOptimizer.Item(x.Key).HospitalSeconds*(1+o!.BonusPercent/100))??0;
            int extra=cfg.ReserveMinutes>0?0:(permitted?.Select(x=>SmartMedicalOptimizer.Item(x.Key).CooldownSeconds).DefaultIfEmpty(0).Max()??0)-1;
            double? estimate=o==null||s.LastHospitalSeconds<=0||s.LastHealCost<=0?null:Math.Min(Math.Max(0,(o.MaximumSeconds-cfg.ReserveMinutes*60-cd+extra)/s.LastHealCost),supplyHealing/s.LastHospitalSeconds);
            return new{slot=w.Slot,profile=w.ProfileId,session=s.Id,note=s.Note,verification=s.ModifierNote,observed=o?.ObservedUtc,hospitalSeconds=o==null?(int?)null:hospital,cooldownSeconds=o==null?(int?)null:cd,maximumSeconds=o?.MaximumSeconds,bonus=o?.BonusPercent,inventory=o?.Inventory,next=s.Item?.Name,plannedCooldownSeconds=s.Plan?.CooldownSeconds,losses,itemsUsed=s.ItemsUsed,cooldownUsed=s.CooldownUsed,savedSeconds=s.Saved,estimatedHeals=estimate};}).ToList()
    };
}
