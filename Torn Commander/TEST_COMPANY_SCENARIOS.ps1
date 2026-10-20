$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderScenarios'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($name in @('CompanyScenarios.cs','TravelWaitAdvisor.cs')){Copy-Item (Join-Path $PSScriptRoot $name) $project}
$program=@'
using TornCommander;
using System.Text.Json;
static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
var current=new CompanyFinance(1,"2026-10-01",1000000,400000,200000,200000,9,1,1000000);
var scenario=CompanyScenarios.Compare(current,700000,null,null,20);
Assert(scenario.Revenue==1200000&&scenario.Net==100000&&scenario.ExtraRevenueNeeded==100000,"scenario arithmetic");
Assert(CompanyScenarios.Compare(current,700000,null,null,0).RunwayDays==10,"funds runway");
Assert(CompanyScenarios.Compare(current with{Supplies=null},700000,null,null,0).Net==null,"unknown supplies treated as zero");
try{CompanyScenarios.Compare(current,-1,null,null,0);throw new Exception("negative ad accepted");}catch(InvalidDataException){}
var groups=CompanyScenarios.AdvertisingCohorts(new[]{current,current with{Day="2026-10-02"},current with{Day="2026-10-03"},current with{CompanyId=2,Day="2026-10-04"},current with{Day=DateTime.UtcNow.ToString("yyyy-MM-dd")}});
using(var doc=JsonDocument.Parse(JsonSerializer.Serialize(groups))){Assert(doc.RootElement.GetArrayLength()==2,"company cohorts mixed");Assert(doc.RootElement[0].GetProperty("days").GetInt32()==3,"today included in closed-day cohort");}
var now=DateTime.UtcNow;
var available=new TravelForecastItem{Country="Hawaii",ItemName="Alternative",Source="Fixture",Stock=10,StockUpdatedAtUtc=now,Units=10,ProfitPerItem=100,TripProfit=1000,FlightMinutesOneWay=20};
var wait=new TravelForecastItem{Country="Hawaii",ItemName="Watched",Source="Fixture",Stock=0,StockUpdatedAtUtc=now,Units=10,ProfitPerItem=200,TripProfit=2000,FlightMinutesOneWay=20,RestockWindowEndUtc=now.AddMinutes(60)};
var choices=TravelWaitAdvisor.Compare(new[]{available,wait},"Hawaii",now,now);
Assert(choices.Count==2&&choices[0].Item=="Alternative","waiting ignored opportunity cost");
Assert(choices[1].WaitMinutes==60&&choices[1].FutureMinutes==80,"wait window end not used");
Assert(TravelWaitAdvisor.Compare(new[]{available with{IsStale=true}},"Hawaii",now,now).Count==0,"stale choice");
Assert(TravelWaitAdvisor.Compare(new[]{wait with{RestockWindowEndUtc=now.AddMinutes(-1)}},"Hawaii",now,now).Count==0,"expired wait forecast");
Console.WriteLine("COMPANY / TRAVEL SCENARIOS PASSED: unknown-cost handling, arithmetic, runway, closed-day/company cohorts, wait opportunity cost and stale/expired rejection.");
namespace TornCommander{
internal record TravelForecastItem{public string Country{get;init;}="";public string ItemName{get;init;}="";public string Source{get;init;}="";public bool IsStale{get;init;}public DateTime? StockUpdatedAtUtc{get;init;}public long ProfitPerItem{get;init;}public int Units{get;init;}public int? Stock{get;init;}public DateTime? RestockWindowEndUtc{get;init;}public int FlightMinutesOneWay{get;init;}public long TripProfit{get;init;}}
}
'@
[IO.File]::WriteAllText((Join-Path $project 'Program.cs'),$program)
[IO.File]::WriteAllText((Join-Path $project 'Tests.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>')
dotnet run --project (Join-Path $project 'Tests.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Company/travel scenarios failed'}
