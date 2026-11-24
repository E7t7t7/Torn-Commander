using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private sealed record GorillaDraft(string Nonce,GorillaContract Contract,string Link,int Player,int Energy,DateTime Copied);
 private readonly Dictionary<string,GorillaDraft> _gorillaDrafts=new();
 private readonly Dictionary<string,string> _gorillaNotes=new();private bool _gorillaBusy;
 private object GorillaDashboard(){
  _gorillaDrafts.TryGetValue(CurrentProfileId,out var d);
  return new{profileId=CurrentProfileId,busy=_gorillaBusy,note=_gorillaNotes.GetValueOrDefault(CurrentProfileId)??"Join in Gorilla's Discord, copy Live Contract Progress, then preview it here. Importing never presses Join or reserves losses.",
   draft=d==null?null:new{nonce=d.Nonce,contract=d.Contract,player=d.Player,energy=d.Energy,maximum=Math.Min(d.Contract.Remaining,Math.Max(0,d.Energy/25)),copied=d.Copied,link=d.Link},
   orders=_lossOrders.Where(o=>o.SourceService==GorillaContracts.Service&&o.RemoteProfileId==CurrentProfileId).OrderByDescending(o=>o.CreatedAt).Take(20).Select(o=>new{o.Id,contract=o.DiscordContractId,buyer=o.BuyerName,target=o.TargetPlayerId,o.RequestedLosses,delivered=o.CompletedTotal,o.PayPerLoss,o.Status,closed=o.IsClosed,link=o.DiscordMessageUrl,copied=o.DiscordCopiedUtc,sharedRemaining=o.DiscordSharedRemaining})};
 }
 private async Task HandleGorilla(string action,JsonElement r){
  string profile=ReadWorkerString(r,"profileId");if(profile!=CurrentProfileId)throw new InvalidDataException("Profile changed; review this account's Gorilla contract.");
  try{
   if(action=="gorillaOpen"){
    string link=GorillaContracts.MessageUrl(ReadWorkerString(r,"link"));await AddBrowserTabAsync(link.Length>0?link:GorillaContracts.Invite);return;
   }
   if(_gorillaBusy)return;_gorillaBusy=true;UpdateModernDashboard();
   try{
    var key=CredentialManager.LoadApiKey(profile)??throw new InvalidDataException("Save this selling account's Torn API key first.");
    if(action=="gorillaPreview"){
     var c=GorillaContracts.Parse(ReadWorkerString(r,"text"));string link=GorillaContracts.MessageUrl(ReadWorkerString(r,"link"));
     using var owner=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,fresh:true));int player=owner.RootElement.GetProperty("profile").GetProperty("id").GetInt32();
     using var bars=JsonDocument.Parse(await _api.FetchEndpointAsync("user/bars",key,fresh:true));int energy=bars.RootElement.GetProperty("bars").GetProperty("energy").GetProperty("current").GetInt32();
     if(profile!=CurrentProfileId)return;
     _gorillaDrafts[profile]=new(Guid.NewGuid().ToString("N"),c,link,player,energy,DateTime.UtcNow);
     _gorillaNotes[profile]="Preview only. "+c.Remaining+" losses remain across ALL vendors, not reserved for you. Confirm your Discord join and choose a bounded personal batch.";return;
    }
    if(action!="gorillaImport")throw new InvalidDataException("Unknown Gorilla action.");
    if(!_gorillaDrafts.TryGetValue(profile,out var draft)||ReadWorkerString(r,"nonce")!=draft.Nonce||DateTime.UtcNow-draft.Copied>TimeSpan.FromMinutes(5))throw new InvalidDataException("Preview expired. Copy the current Discord card and preview again.");
    using var check=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,fresh:true));if(profile!=CurrentProfileId)return;
    if(check.RootElement.GetProperty("profile").GetProperty("id").GetInt32()!=draft.Player)throw new InvalidDataException("The key owner changed after preview.");
    int count=r.GetProperty("count").GetInt32();bool joined=r.GetProperty("joined").GetBoolean();GorillaContracts.ValidateImport(draft.Contract,draft.Player,count,joined);
    var existing=_lossOrders.FirstOrDefault(o=>o.SourceService==GorillaContracts.Service&&o.RemoteProfileId==profile&&o.DiscordContractId==draft.Contract.Id&&!o.IsClosed);
    if(existing!=null&&new[]{_lossWorkerA,_lossWorkerB}.Any(w=>w?.IsArmed==true&&_medicalSessions.TryGetValue(w,out var m)&&m.Order==existing.Id))throw new InvalidDataException("Stop the existing Gorilla worker before refreshing its contract copy; progress will be kept.");
    var order=existing==null?new LossOrder{Id=Guid.NewGuid().ToString("N"),SourceService=GorillaContracts.Service,RemoteOrderNumber=draft.Contract.Id,RemoteProfileId=profile,RemoteOwner=draft.Player,DiscordContractId=draft.Contract.Id,ProfileAId=profile,ProfileBId="",TargetPlayerId=draft.Contract.Target.ToString(),BuyerName=draft.Contract.Buyer,PayPerLoss=draft.Contract.Pay}:JsonSerializer.Deserialize<LossOrder>(JsonSerializer.Serialize(existing))!;
    if(order.TargetPlayerId!=draft.Contract.Target.ToString()||order.PayPerLoss!=draft.Contract.Pay)throw new InvalidDataException("Target or price differs from the existing order. Close that order at its delivered count before importing a new batch.");
    order.RequestedLosses=order.CompletedTotal+count;order.DiscordMessageUrl=draft.Link;order.DiscordJoined=joined;order.DiscordCopiedUtc=draft.Copied;order.DiscordSharedRemaining=draft.Contract.Remaining;
    order.Notes="Manually joined Gorilla Discord contract "+draft.Contract.Id+". Shared progress is not this account's delivery count. Cash payment remains unconfirmed until recorded. Open Discord to Leave Contract when unable to continue.";
    var next=_lossOrders.Where(o=>o.Id!=order.Id).ToList();next.Insert(0,order);Storage.SaveAtomic(Storage.LossOrdersPath,next);_lossOrders=next;
    _gorillaDrafts.Remove(profile);RebindLossOrders();SelectLossOrder(order.Id);RefreshLossLedger();
    _gorillaNotes[profile]="Local batch ready: "+count+" losses at $"+order.PayPerLoss.ToString("N0")+". Press Start A below. Join/Leave stays in Discord; this import did not reserve anything remotely.";
   }finally{_gorillaBusy=false;UpdateModernDashboard();}
  }catch(Exception ex){_gorillaNotes[profile]=ex is InvalidDataException?ex.Message:"Gorilla import failed. Check API access or local storage. No Discord action or fight was performed.";UpdateModernDashboard();}
 }
}
