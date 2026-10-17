$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderTrading'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'TradingAdvisor.cs') $project
$program=@'
using TornCommander;
using System.Text.Json;
void Check(bool ok,string why){if(!ok)throw new Exception(why);}
var now=DateTime.UtcNow;
Check(TradingAdvisor.AvailableCapital(1000,200,null)==800&&TradingAdvisor.AvailableCapital(1000,200,500)==300&&TradingAdvisor.AvailableCapital(100,200,null)==0,"Reserve must reduce manual and wallet-limited capital.");

TradeQuote Quote(int id,long cheap=80,int amount=2,long next=110)=>new(){ItemId=id,Name="Item "+id,ObservedUtc=now,Reference=110,Tiers=new(){new(){Price=cheap,Quantity=amount},new(){Price=next,Quantity=100}}};
var q=Quote(1);
var r=TradingAdvisor.Build(new[]{q},1000,5,10,0,now).Single();
Check(r.Decision=="Candidate"&&r.SellTarget==109&&r.BuyCeiling==93&&r.Cost==160&&r.EstimatedNet==46,"Fee/ROI/tier math wrong.");
r=TradingAdvisor.Build(new[]{q},100,5,10,0,now).Single();
Check(r.Decision=="Watch"&&r.Reason.Contains("cannot clear"),"Budget left cheap competition.");
var rows=TradingAdvisor.Build(new[]{q,Quote(2)},200,5,10,0,now);
Check(rows.Count(x=>x.Decision=="Candidate")==1&&rows.Where(x=>x.Decision=="Candidate").Sum(x=>x.Cost)<=200,"Shared budget overspent.");
Check(TradingAdvisor.Build(new[]{Quote(1,106)},1000,5,5,0,now).Single().Decision=="Watch","Fee-negative flip recommended.");
q=Quote(1);q.ObservedUtc=now.AddMinutes(-11);
Check(TradingAdvisor.Build(new[]{q},1000,0,0,0,now).Single().Decision=="Rescan","Stale opportunity recommended.");
q.ObservedUtc=now.AddMinutes(2);
Check(TradingAdvisor.Build(new[]{q},1000,0,0,0,now).Single().Decision=="Rescan","Future timestamp trusted.");
q=Quote(1);q.Tiers.RemoveAt(1);
Check(TradingAdvisor.Build(new[]{q},1000,0,0,0,now).Single().Decision=="Watch","Fake spread.");
q=Quote(1);q.Reference=70;
Check(TradingAdvisor.Build(new[]{q},1000,5,5,0,now).Single().Decision=="Watch","Reference cap ignored.");
q=Quote(1);
Check(TradingAdvisor.Build(new[]{q},1000,5,5,60,now).Single().SellAdvice.Contains("clears"),"Profitable sell not explained.");
Check(TradingAdvisor.Build(new[]{q},1000,5,5,120,now).Single().SellAdvice.Contains("misses"),"Losing sell promoted.");
var stress=TradingAdvisor.Build(new[]{Quote(1,98,2,110)},1000,5,0,0,now,10).Single();
Check(stress.Decision=="Watch"&&stress.StressNet<0&&stress.Reason.Contains("Fragile"),"Fragile spread was promoted.");
var limited=TradingAdvisor.Build(new[]{Quote(1)},200,5,10,0,now,0,50).Single();
Check(limited.Decision=="Watch"&&limited.Reason.Contains("per-item"),"Per-item capital limit ignored.");
Check(TradingAdvisor.Build(new[]{Quote(1)},1000,5,10,0,now,0,100,100).Single().Decision=="Watch","Minimum cash profit ignored.");
var robust=TradingAdvisor.Build(new[]{Quote(1)},1000,5,10,0,now,3).Single();
Check(robust.StressNet>0&&robust.BreakEvenSell==85&&robust.ExposurePercent==16,"Break-even / stress / exposure math wrong.");
var old=Quote(1,90);old.ObservedUtc=now.AddHours(-1);
Check(TradingAdvisor.Build(new[]{old,q},1000,5,5,0,now).Single().Trend.Contains("down"),"Trend lost.");
var a=new TradingJournal{Items="one",Observations=new(){q}};
var b=new TradingJournal();
Check(b.Observations.Count==0&&b.Items!=a.Items,"Shared journals.");
var reload=JsonSerializer.Deserialize<TradingJournal>(JsonSerializer.Serialize(a))!;
Check(reload.Observations.Count==1&&reload.Items=="one","Persistence lost history.");
Console.WriteLine("TRADING ADVISER PASSED: fee rounding, return target, clearing competition, shared capital, stale/future quotes, reference cap, sell cost basis, trend and separate journals.");
'@
Set-Content (Join-Path $project 'Program.cs') $program -Encoding UTF8
Set-Content (Join-Path $project 'Fixture.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'Fixture.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Trading model regression failed.'}
