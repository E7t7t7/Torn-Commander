using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private readonly HashSet<string> _lossTerminalBusy=new();
 private async Task VerifyLossTerminalAsync(LossAutoCycleWorker worker,JsonElement request){
  string claim=ReadWorkerString(request,"claimId"),orderId=ReadWorkerString(request,"orderId");
  if(!worker.IsArmed||ReadWorkerString(request,"profileId")!=worker.ProfileId||ReadWorkerString(request,"slot")!=worker.Slot)return;
  var order=_lossOrders.FirstOrDefault(o=>o.Id==orderId);if(order==null)return;
  var replay=LossAccounting.Replay(order,claim,worker.ProfileId,worker.Slot);
  if(replay!=null){await worker.ReplyLossTerminalAsync(new{kind="verified",claimId=claim,ok=replay.Ok,remaining=replay.Remaining,message=replay.Message});return;}
  LossClaimLease? lease;lock(_lossClaimGate)_lossClaims.TryGetValue(claim,out lease);
  if(lease==null||lease.OrderId!=orderId||lease.ProfileId!=worker.ProfileId||lease.Slot!=worker.Slot||!_medicalSessions.TryGetValue(worker,out var medical))return;
  if(!_lossTerminalBusy.Add(claim))return;
  bool Current(){lock(_lossClaimGate)return worker.IsArmed&&_lossClaims.TryGetValue(claim,out var current)&&ReferenceEquals(current,lease)&&_medicalSessions.TryGetValue(worker,out var session)&&ReferenceEquals(session,medical);}
  try{
   if(!int.TryParse(lease.TargetId,out int target)||!request.TryGetProperty("fightStartedAt",out var started)||!started.TryGetInt64(out long milliseconds))throw new InvalidDataException("Fight start/target missing. Nothing counted.");
   var opened=DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
   if(opened<lease.CreatedAt.AddSeconds(-2)||opened>DateTime.UtcNow.AddSeconds(5))throw new InvalidDataException("Fight timestamp does not match this claim. Nothing counted.");
   var key=CredentialManager.LoadApiKey(worker.ProfileId);if(string.IsNullOrWhiteSpace(key))throw new InvalidDataException("Save this profile's Torn API key to verify the ended fight.");
   using var ownDoc=JsonDocument.Parse(await _api.FetchEndpointAsync("user/profile",key,fresh:true));
   if(!Current())return;
   if(ownDoc.RootElement.GetProperty("profile").GetProperty("id").GetInt32()!=medical.Player)throw new InvalidDataException("API account does not match the fight worker. No loss counted.");
   var json=await _api.FetchEndpointAsync("user/attacks?filters=outgoing&limit=100&sort=desc&from="+new DateTimeOffset(opened).ToUnixTimeSeconds(),key,fresh:true);
   if(!Current())return;
   var seen=_lossOrders.SelectMany(o=>o.CommitReceipts??new()).Where(x=>x.AttackId>0).Select(x=>x.AttackId);
   var receipt=LevelingPlanner.FindReceipt(json,medical.Player,target,opened,seen);
   if(receipt==null){await worker.ReplyLossTerminalAsync(new{kind="pending",claimId=claim,retryAt=DateTimeOffset.UtcNow.AddSeconds(12).ToUnixTimeMilliseconds(),message="Fight ended; waiting for its official attack receipt. No further weapon clicks or loss counted."});return;}
   if(receipt.Value.Result!="Lost")throw new InvalidDataException("Official fight result was "+receipt.Value.Result+". Not a loss; stopped without counting it.");
   var result=CommitLossClaim(worker,orderId,claim,receipt.Value.Id);
   await worker.ReplyLossTerminalAsync(new{kind="verified",claimId=claim,ok=result.Ok,remaining=result.Remaining,message=result.Message});UpdateLossDashboardStatus();
  }catch(Exception ex){
   if(!Current())return;
   bool transient=LevelingApiRecovery.Transient(ex);
   int wait=Math.Max(12,TornRequestCoordinator.Shared.CooldownSeconds+1);
   await worker.ReplyLossTerminalAsync(new{kind=transient?"pending":"paused",claimId=claim,retryAt=DateTimeOffset.UtcNow.AddSeconds(wait).ToUnixTimeMilliseconds(),message=ex is InvalidDataException?ex.Message:LevelingApiRecovery.Describe(ex)});
  }finally{_lossTerminalBusy.Remove(claim);}
 }
}
