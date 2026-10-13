$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderApiPacing'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('TornRequestCoordinator.cs','TornApiTransport.cs','SnapshotMerge.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$models=Get-Content (Join-Path $PSScriptRoot 'Models.cs') -Raw
$snapshot=[regex]::Match($models,'(?s)public sealed class TornSnapshot\s*\{.*?(?=\r?\npublic sealed class GuideActionRecommendation)').Value
if(!$snapshot){throw 'Production TornSnapshot not found.'}
Set-Content -LiteralPath (Join-Path $project 'TornSnapshot.cs') -Value ("namespace TornCommander;"+[Environment]::NewLine+$snapshot+[Environment]::NewLine+'public sealed class CrimeProgress {} public sealed class MeritAward {} public sealed class MeritUpgradeState {}') -Encoding UTF8
$prog=@'
using System.Net;
using System.Net.Http.Headers;
using TornCommander;
void Check(bool value,string message){if(!value)throw new Exception(message);}
var clock=new Clock();
Task Delay(TimeSpan span,CancellationToken ct){ct.ThrowIfCancellationRequested();clock.Advance(span);return Task.CompletedTask;}
var queue=new TornRequestCoordinator(clock,Delay);
var times=new List<DateTimeOffset>();
using var handler=new FakeHandler(async req=>{times.Add(clock.GetUtcNow());await Task.Yield();return new(HttpStatusCode.OK){Content=new StringContent("{}")};});
using var http=new HttpClient(handler);
var requests=Enumerable.Range(0,50).Select(async n=>{
 using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.torn.com/v2/user/basic");
 return await TornApiTransport.SendAsync(http,req,coordinator:queue);
}).ToArray();
await Task.WhenAll(requests);
Check(times.Count==50 && times.Zip(times.Skip(1)).All(x=>(x.Second-x.First).TotalMilliseconds>=1250),"Concurrent calls bypassed spacing.");
Check(times.Where(t=>t<times[0].AddMinutes(1)).Count()<=48,"Commander consumed too much of the minute allowance.");
Check(queue.Queued==0,"Queue count leaked.");

var limitClock=new Clock();
var limited=new TornRequestCoordinator(limitClock,(d,ct)=>{limitClock.Advance(d);return Task.CompletedTask;});
int sends=0;
using var rateHttp=new HttpClient(new FakeHandler(req=>{
 sends++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("""{"error":{"code":5,"error":"Too many requests"}}""")});
}));
var blocked=Enumerable.Range(0,12).Select(async n=>{
 using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.torn.com/v2/user/basic");
 try{await TornApiTransport.SendAsync(rateHttp,req,coordinator:limited);return false;}
 catch(TornApiCooldownException){return true;}
}).ToArray();
Check((await Task.WhenAll(blocked)).All(x=>x) && sends==1 && limited.CooldownSeconds==65,"Error 5 did not stop queued network sends.");
Check(!TornApiTransport.IsRateLimit("""{"error":{"code":16}}"""),"Access-level error became a rate limit.");
Check(!TornApiTransport.IsRateLimit("not json"),"Malformed response became a rate limit.");
limitClock.Advance(TimeSpan.FromSeconds(65));
limited.ThrowIfCoolingDown();
Check(limited.CooldownSeconds==0,"Cooldown never cleared.");

var headerClock=new Clock();
var headerQueue=new TornRequestCoordinator(headerClock,(d,ct)=>Task.CompletedTask);
using var tooMany=new HttpClient(new FakeHandler(req=>{
 var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests){Content=new StringContent("slow down")};
 response.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
 return Task.FromResult(response);
}));
using(var request=new HttpRequestMessage(HttpMethod.Get,"https://api.torn.com/v2/user/basic")){
 try{await TornApiTransport.SendAsync(tooMany,request,coordinator:headerQueue);throw new Exception("429 not blocked");}
 catch(TornApiCooldownException){}
}
Check(headerQueue.CooldownSeconds==90,"Retry-After was ignored.");

var cancelQueue=new TornRequestCoordinator(clock,Delay);
var held=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
var active=cancelQueue.RunAsync(ct=>held.Task);
using var cancelled=new CancellationTokenSource();
var pending=cancelQueue.RunAsync(ct=>Task.FromResult(1),cancelled.Token);
cancelled.Cancel();
try{await pending;throw new Exception("Cancelled queue entry sent.");}catch(OperationCanceledException){}
held.SetResult(1);await active;
Check(cancelQueue.Queued==0 && await cancelQueue.RunAsync(ct=>Task.FromResult(2))==2,"Cancellation locked the queue.");
var old=new TornSnapshot{PlayerName="Example",CurrentEnergy=20,FullRetrievedAt=new DateTime(2026,10,12),FastRetrievedAt=new DateTime(2026,10,12)};
old.RawJson["basic"]="old location";old.RawJson["perks"]="saved perks";old.Crimes.Add(new CrimeProgress());
var failed=new TornSnapshot{FastRetrievedAt=new DateTime(2026,10,13)};
failed.Errors.Add("basic: Torn API cooldown");
SnapshotMerge.MergeFast(old,failed);
Check(old.PlayerName=="Example" && old.CurrentEnergy==20 && old.Crimes.Count==1 && old.RawJson["perks"]=="saved perks" &&
 old.FullRetrievedAt==new DateTime(2026,10,12) && old.FastRetrievedAt==new DateTime(2026,10,12),"Blocked sync erased or falsely refreshed existing data.");
Check(!old.RawJson.ContainsKey("basic"),"Failed travel status left stale home evidence.");
var success=new TornSnapshot{CurrentEnergy=0,FastRetrievedAt=new DateTime(2026,10,13)};
success.RawJson["bars"]="new bars";
SnapshotMerge.MergeFast(old,success);
Check(old.CurrentEnergy==0 && old.Errors.Count==0 && old.FastRetrievedAt==success.FastRetrievedAt,"Successful zero-valued data or transient errors merged incorrectly.");
Console.WriteLine("API PACING PASSED: concurrent spacing/headroom, queued error-5 block, cooldown expiry, HTTP429/Retry-After, malformed/access errors and cancellation.");
class Clock:TimeProvider{
 DateTimeOffset value=new(2026,10,13,0,0,0,TimeSpan.Zero);
 public override DateTimeOffset GetUtcNow()=>value;
 public void Advance(TimeSpan span)=>value+=span;
}
class FakeHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);
}
'@
Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $prog -Encoding UTF8
Set-Content -LiteralPath (Join-Path $project 'ApiTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'ApiTest.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'API pacing tests failed.'}
