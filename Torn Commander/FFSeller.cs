using System.Net;
using System.Text;
using System.Text.Json;
namespace TornCommander;
public sealed class FFSellerClaim {
 public long Id{get;set;} public string OrderNumber{get;set;}="";public int Target{get;set;}public string TargetName{get;set;}="";
 public string State{get;set;}="";public int Claimed{get;set;}public int Delivered{get;set;}public long Price{get;set;}public long Earnings{get;set;}
 public bool EarningsKnown{get;set;}
 public long ClaimedAt{get;set;}public long ExpiresAt{get;set;}public long IdleDeadline{get;set;}public long PaidAt{get;set;}
 public string PaymentStatus{get;set;}="";public bool Review{get;set;}public List<long> AttackIds{get;set;}=new();
 public bool Paid=>EarningsKnown&&!Review&&(PaymentStatus=="resolved"||PaidAt>0);
 public bool Live(DateTime now)=>State=="active"&&ExpiresAt>new DateTimeOffset(now).ToUnixTimeSeconds()&&(IdleDeadline==0||IdleDeadline>new DateTimeOffset(now).ToUnixTimeSeconds())&&Delivered<Claimed;
 public long Deadline=>IdleDeadline>0?Math.Min(ExpiresAt,IdleDeadline):ExpiresAt;
}
public sealed record FFSellerContract(string OrderNumber,int Available,long Price);
public sealed class FFSellerState {
 public int Player{get;set;}public string KeyHash{get;set;}="";public DateTime OwnerChecked{get;set;}
 public DateTime ArmedAt{get;set;}
 public bool Enabled{get;set;}public bool AutoRun{get;set;}public bool AutoFinish{get;set;}public int SessionLimit{get;set;}=0;public bool? LimitCustomized{get;set;}
 public int Cap{get;set;} public DateTime LastSync{get;set;} public string Note{get;set;}="Connect this account's seller key.";
 public List<FFSellerContract> Contracts{get;set;}=new();public Dictionary<long,FFSellerClaim> Claims{get;set;}=new();
 public HashSet<long> PendingKnownIds{get;set;}=new();
 public HashSet<long> AutoHandled{get;set;}=new();
 public string Pending{get;set;}="";public string PendingOrder{get;set;}="";public long PendingClaim{get;set;}public DateTime PendingAt{get;set;}
}
public static class FFSellerData {
 public static long Number(JsonElement row,string name,long fallback=0){if(!row.TryGetProperty(name,out var x)||x.ValueKind==JsonValueKind.Null)return fallback;if(x.ValueKind==JsonValueKind.Number&&x.TryGetInt64(out var n)&&n>=0)return n;throw new InvalidDataException("Invalid seller field: "+name);}
 public static int Int(JsonElement row,string name,int fallback=0){long n=Number(row,name,fallback);if(n>int.MaxValue)throw new InvalidDataException("Seller integer is out of range.");return (int)n;}
 public static string Text(JsonElement row,string name,string fallback="")=>row.TryGetProperty(name,out var x)&&x.ValueKind==JsonValueKind.String?x.GetString()??fallback:fallback;
 public static FFSellerClaim Merge(JsonElement row,FFSellerClaim? previous=null){
  var c=previous==null?new FFSellerClaim():JsonSerializer.Deserialize<FFSellerClaim>(JsonSerializer.Serialize(previous))!;
  long id=Number(row,"id",c.Id);if(id<=0||id>9007199254740991||c.Id>0&&id!=c.Id)throw new InvalidDataException("Claim identity changed.");
  c.Id=id;c.OrderNumber=Text(row,"order_number",c.OrderNumber);c.Target=Int(row,"target_player_id",c.Target);
  c.TargetName=Text(row,"target_player_name",c.TargetName);c.State=Text(row,"state",c.State);
  c.Claimed=Int(row,"slots_claimed",c.Claimed);c.Delivered=Int(row,"slots_delivered",c.Delivered);
  if(row.TryGetProperty("earnings",out _))c.EarningsKnown=true;
  c.Price=Number(row,"price_per_loss",c.Price);c.Earnings=Number(row,"earnings",c.Earnings);
  c.ClaimedAt=Number(row,"claimed_at",c.ClaimedAt);c.ExpiresAt=Number(row,"expires_at",c.ExpiresAt);
  c.IdleDeadline=row.TryGetProperty("idle_deadline_at",out _)?Number(row,"idle_deadline_at"):c.IdleDeadline;
  c.PaidAt=row.TryGetProperty("paid_at",out _)?Number(row,"paid_at"):c.PaidAt;
  c.PaymentStatus=row.TryGetProperty("payment_status",out _)?Text(row,"payment_status"):c.PaymentStatus;
  if(row.TryGetProperty("payment_review_required",out var review))c.Review=review.ValueKind==JsonValueKind.True;
  if(row.TryGetProperty("hits",out var hits)&&hits.ValueKind==JsonValueKind.Array)c.AttackIds=c.AttackIds.Concat(hits.EnumerateArray().Select(x=>Number(x,"attack_id")).Where(x=>x>0)).Distinct().ToList();
  if(c.Target<0||c.Claimed is <0 or >1000000||c.Delivered<0||c.Delivered>c.Claimed||c.OrderNumber.Length>100||c.Price>1000000000000||c.Earnings>1000000000000000)throw new InvalidDataException("Seller claim values are inconsistent.");
  return c;
 }
 public static (List<FFSellerContract> Contracts,int Cap) Contracts(string json){
  using var d=JsonDocument.Parse(json);var root=d.RootElement;
  int cap=Int(root,"max_claim_slots");
  if(cap<0||cap>1000000)throw new InvalidDataException("Invalid personal claim cap.");
  var rows=root.GetProperty("contracts").EnumerateArray().Select(x=>new FFSellerContract(Text(x,"order_number"),Int(x,"slots_available"),Number(x,"price_per_loss"))).ToList();
  if(rows.Any(x=>string.IsNullOrEmpty(x.OrderNumber)||x.OrderNumber.Length>100||x.Available is <0 or >1000000||x.Price>1000000000000))throw new InvalidDataException("Invalid seller contract list.");
  return(rows,cap);
 }
 public static void MergeClaims(FFSellerState state,string json){
  using var d=JsonDocument.Parse(json);var updated=new Dictionary<long,FFSellerClaim>(state.Claims);
  foreach(var row in d.RootElement.GetProperty("claims").EnumerateArray()){
   long id=Number(row,"id");var claim=Merge(row,updated.GetValueOrDefault(id));updated[id]=claim;
  }
  state.Claims=updated; // Never delete history merely because the API's seven-day window moved.
 }
 public static bool CanExecute(LossOrder o,FFSellerState s,string profile,DateTime now)=>o.SourceService!="FFScouter"||(o.RemoteProfileId==profile&&o.RemoteOwner==s.Player&&o.RemoteState=="active"&&!o.IsClosed&&o.RemoteDeadline>new DateTimeOffset(now).ToUnixTimeSeconds()&&o.RemoteObservedUtc>now.AddSeconds(-75));
 public static FFSellerClaim? AutoCandidate(FFSellerState s,DateTime now)=>s.Enabled&&s.AutoRun&&s.ArmedAt!=default&&s.Pending==""?s.Claims.Values.FirstOrDefault(c=>c.Live(now)&&c.ClaimedAt>=new DateTimeOffset(s.ArmedAt).ToUnixTimeSeconds()&&!s.AutoHandled.Contains(c.Id)):null;
 public static void VerifyOwner(int seller,int torn){if(seller<=0||seller!=torn)throw new InvalidDataException("Seller key belongs to another account.");}
 public static bool MigrateLimit(FFSellerState s){
  if(s.LimitCustomized.HasValue)return false;
  if(s.SessionLimit==10)s.SessionLimit=0;
  s.LimitCustomized=s.SessionLimit>0;return true;
 }
 public static int ReservationLimit(FFSellerState s,int available)=>Math.Max(0,Math.Min(available,Math.Min(s.Cap,s.SessionLimit>0?s.SessionLimit:s.Cap)));
 public static int ClaimCount(FFSellerState state,string order,int requested,bool max,DateTime now){
  if(state.Pending!="")throw new InvalidOperationException("An earlier request needs reconciliation; do not submit it again.");
  if(state.Claims.Values.Any(c=>c.State=="active"&&c.ExpiresAt>new DateTimeOffset(now).ToUnixTimeSeconds()))throw new InvalidOperationException("One active seller claim is permitted. Complete the existing claim first.");
  var c=state.Contracts.SingleOrDefault(c=>c.OrderNumber==order)??throw new InvalidDataException("Refresh this contract.");
  int limit=ReservationLimit(state,c.Available);int count=max?limit:requested;
  if(count<1||count>limit)throw new InvalidDataException("Count exceeds available slots, personal cap or your session limit.");
  return count;
 }
 public static int EnergyClaimCount(FFSellerState s,string order,int energy,DateTime now){
  if(energy<25)throw new InvalidDataException("At least 25 current energy is needed to reserve one loss.");
  return Math.Min(energy/25,ClaimCount(s,order,0,true,now));
 }
 public static object Totals(FFSellerState state){
  var rows=state.Claims.Values.ToList();
  return new{paid=rows.Where(x=>x.Paid).Sum(x=>x.Earnings),outstanding=rows.Where(x=>!x.Paid).Sum(x=>x.Earnings),earned=rows.Sum(x=>x.Earnings),losses=rows.Sum(x=>(long)x.Delivered),finished=rows.Count(x=>x.State is "fulfilled" or "expired"),available=state.Contracts.Count,active=rows.Count(x=>x.Live(DateTime.UtcNow)),pending=rows.Count(x=>!x.Paid&&x.Earnings>0)};
 }
 public static string KeyHash(string key)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
public sealed class FFSellerException:Exception {
 public bool Uncertain{get;}public FFSellerException(string message,bool uncertain=false):base(message){Uncertain=uncertain;}
}
public sealed class FFSellerClient {
 private static readonly SemaphoreSlim Gate=new(1,1);private static DateTime Next,Cooling;
 private readonly HttpClient http;private readonly Func<DateTime> clock;private readonly Func<TimeSpan,Task> delay;
 public FFSellerClient(HttpClient? http=null,Func<DateTime>? clock=null,Func<TimeSpan,Task>? delay=null){
  this.http=http??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(25),MaxResponseContentBufferSize=2097152};
  this.clock=clock??(()=>DateTime.UtcNow);this.delay=delay??(d=>Task.Delay(d));
 }
 public async Task<string> Send(string key,string route,object? body=null){
  if(!System.Text.RegularExpressions.Regex.IsMatch(key,"^[A-Za-z0-9]{16}$"))throw new FFSellerException("A registered 16-character seller API key is required.");
  if(!System.Text.RegularExpressions.Regex.IsMatch(route,@"^(check-key|account/balance|losses/seller/(contracts|claims|claim|orders/[A-Za-z0-9_-]+|claims/[0-9]+/complete))$"))throw new FFSellerException("Unsupported seller route.");
  bool write=body!=null;if(write&&route!="losses/seller/claim"&&!route.EndsWith("/complete"))throw new FFSellerException("Unsupported mutation.");
  await Gate.WaitAsync();
  try{
   var now=clock();if(Cooling>now)throw new FFSellerException("FFScouter rate-limit wait. Refresh later; no mutation repeated.");
   if(Next>now)await delay(Next-now);Next=clock().AddSeconds(3.1);
   using var request=new HttpRequestMessage(write?HttpMethod.Post:HttpMethod.Get,"https://ffscouter.com/api/v1/"+route+"?key="+Uri.EscapeDataString(key));
   if(write)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
   HttpResponseMessage response;
   try{response=await http.SendAsync(request);}catch{throw new FFSellerException(write?"Reservation/completion response interrupted. Refresh claims to reconcile before retrying.":"FFScouter connection failed. Saved history remains available.",write);}
   using(response){
    if(response.StatusCode==HttpStatusCode.TooManyRequests){Cooling=clock().AddSeconds(60);throw new FFSellerException("FFScouter rate limit reached. Requests paused for at least a minute.",write);}
    string text;try{text=await response.Content.ReadAsStringAsync();}catch{throw new FFSellerException("Response interrupted. Refresh claims before retrying.",write);}
    JsonDocument doc;try{doc=JsonDocument.Parse(text);}catch{throw new FFSellerException("FFScouter returned unreadable data; refresh before retrying a mutation.",write);}
    using(doc){
     var root=doc.RootElement;
     if(response.StatusCode==HttpStatusCode.TooManyRequests){Cooling=clock().AddSeconds(Math.Max(60,FFSellerData.Number(root,"retry_after_seconds")));throw new FFSellerException("FFScouter rate limit reached. Requests paused for at least a minute.",write);}
     if(!response.IsSuccessStatusCode||root.TryGetProperty("error",out _))throw new FFSellerException("FFScouter rejected the request (HTTP "+(int)response.StatusCode+", code "+FFSellerData.Number(root,"code")+").",write&&(int)response.StatusCode>=500);
     return text;
    }
   }
  }finally{Gate.Release();}
 }
}

public static class FFSellerOperations {
 public static async Task Execute(FFSellerState s,Func<string,object?,Task<string>> send,Action persist,string type,string order,int count,long claimId=0){
  if(s.Pending!="")throw new InvalidOperationException("Reconcile the previous request before another reservation/completion.");
  if(type is not ("claim" or "complete"))throw new InvalidDataException("Unknown seller mutation.");
  if(type=="claim")count=FFSellerData.ClaimCount(s,order,count,false,DateTime.UtcNow);
  else if(claimId<=0||!s.Claims.ContainsKey(claimId))throw new InvalidDataException("Unknown owning seller claim.");
  s.PendingKnownIds=s.Claims.Keys.ToHashSet();s.Pending=type;s.PendingOrder=order;s.PendingClaim=claimId;s.PendingAt=DateTime.UtcNow;
  persist(); // If this fails, no POST is sent.
  try{
   var json=await send(type=="claim"?"losses/seller/claim":"losses/seller/claims/"+claimId+"/complete",type=="claim"?new{order_number=order,slots=count}:(object)new{});
   using var doc=JsonDocument.Parse(json);var row=doc.RootElement.GetProperty("claim");
   long id=FFSellerData.Number(row,"id");if(type=="complete"&&id!=claimId)throw new InvalidDataException("Completion returned another claim ID.");
   var merged=FFSellerData.Merge(row,s.Claims.GetValueOrDefault(id));
   if(type=="claim"){
    if(s.PendingKnownIds.Contains(id)||merged.Target<=0||merged.Claimed<=0||merged.Claimed>count||merged.OrderNumber!=order||merged.ExpiresAt<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new InvalidDataException("Reservation response incomplete. Refresh to reconcile; do not repeat it.");
    merged.State=merged.State==""?"active":merged.State;merged.ClaimedAt=merged.ClaimedAt>0?merged.ClaimedAt:new DateTimeOffset(s.PendingAt).ToUnixTimeSeconds();merged.Price=s.Contracts.First(x=>x.OrderNumber==order).Price;
   }
   s.Claims[id]=merged;s.LastSync=DateTime.UtcNow;s.Pending="";
   try{persist();}catch{s.Pending=type;throw;} // Remote success + local failure still blocks resubmission.
  }catch(FFSellerException ex)when(!ex.Uncertain){s.Pending="";persist();throw;}
 }
}

public sealed record FFSellerBatchClaim(string Profile,long ClaimId,int Count);
public sealed record FFSellerBatchResult(List<FFSellerBatchClaim> Confirmed,string FailedProfile,string Error);
public static class FFSellerBatch{
 public static (int First,int Second) Plan(FFSellerState a,FFSellerState b,string order,int wantedA,int wantedB,DateTime now){
  if(a.Player<=0||b.Player<=0||a.Player==b.Player)throw new InvalidDataException("Choose two different verified seller accounts.");
  int first=Math.Min(wantedA,FFSellerData.ClaimCount(a,order,0,true,now));
  int second=Math.Min(wantedB,FFSellerData.ClaimCount(b,order,0,true,now));
  int available=Math.Min(a.Contracts.Single(x=>x.OrderNumber==order).Available,b.Contracts.Single(x=>x.OrderNumber==order).Available);
  second=Math.Min(second,Math.Max(0,available-first));
  if(first<=0||second<=0)throw new InvalidDataException("Not enough energy/slots for a claim on both accounts. Reduce the first count or reserve separately.");
  return(first,second);
 }
 public static async Task<FFSellerBatchResult> Reserve(string profileA,string profileB,FFSellerState a,FFSellerState b,string order,int wantedA,int wantedB,Func<Task<FFSellerState>> refreshSecond,Func<string,int,Task<(long Id,int Count)>> reserve){
  if(profileA==profileB)throw new InvalidDataException("Choose different local profiles.");
  var plan=Plan(a,b,order,wantedA,wantedB,DateTime.UtcNow);int ownerB=b.Player;
  var confirmed=new List<FFSellerBatchClaim>();string step=profileA;
  try{
   var first=await reserve(profileA,plan.First);
   if(first.Id<=0||first.Count<=0||first.Count>plan.First)throw new InvalidDataException("First claim response is unconfirmed. Review the provider before retrying.");
   confirmed.Add(new(profileA,first.Id,first.Count));step=profileB;
   var fresh=await refreshSecond();FFSellerData.VerifyOwner(ownerB,fresh.Player);
   int count=Math.Min(plan.Second,FFSellerData.ClaimCount(fresh,order,0,true,DateTime.UtcNow));
   var second=await reserve(profileB,count);
   if(second.Id<=0||second.Count<=0||second.Count>count)throw new InvalidDataException("Second claim response is unconfirmed. Review the provider before retrying.");
   confirmed.Add(new(profileB,second.Id,second.Count));
   return new(confirmed,"","");
  }catch(Exception ex){return new(confirmed,step,ex is InvalidOperationException or InvalidDataException or FFSellerException?ex.Message:"Request failed. Refresh this account before retrying; no automatic repeat.");}
 }
}
