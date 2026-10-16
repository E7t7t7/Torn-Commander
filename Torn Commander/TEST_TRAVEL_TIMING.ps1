$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderFlightTest'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('TravelRehabPlanner.cs','TravelTiming.cs','TravelItinerary.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$prog=@'
using System.Text.Json;
using TornCommander;
void Check(bool value,string message){if(!value)throw new Exception(message);}
var now=new DateTime(2026,10,13,0,0,0,DateTimeKind.Utc);
long Stamp(DateTime time)=>new DateTimeOffset(time).ToUnixTimeSeconds();
string Flight(string country,DateTime departure,DateTime arrival)=>JsonSerializer.Serialize(new{travel=new{destination=country,departed_at=Stamp(departure),arrival_at=Stamp(arrival),time_left=(int)(arrival-now).TotalSeconds}});
var basic="""{"profile":{"status":{"state":"Traveling","description":"Traveling to Hawaii"}}}""";
var outbound=TravelTiming.Parse(basic,Flight("Hawaii",now.AddMinutes(-60),now.AddMinutes(120)),now,now);
Check(outbound.State=="outbound" && outbound.FlightMinutes==180 && outbound.ArrivalUtc==now.AddMinutes(120),"v2 travel timing failed.");
var anchor=TravelTiming.Anchor(outbound,now,10,Array.Empty<TravelForecastItem>());
Check(anchor.Conditional && anchor.ReadyUtc==now.AddMinutes(320),"Outbound plan ignored landing, stay, return flight or turnaround.");
var returned=TravelTiming.Parse(basic,Flight("Torn",now.AddMinutes(-30),now.AddMinutes(90)),now,now);
Check(returned.State=="returning" && TravelTiming.Anchor(returned,now,10,Array.Empty<TravelForecastItem>()).ReadyUtc==now.AddMinutes(100),"Actual return ETA ignored.");
var arrived=TravelTiming.Parse(basic,Flight("Hawaii",now.AddMinutes(-60),now.AddMinutes(120)),now,now.AddMinutes(130));
Check(arrived.State=="abroad","Finished outbound flight should transition abroad.");
Check(TravelTiming.Anchor(arrived,now.AddMinutes(130),10,Array.Empty<TravelForecastItem>()).ReadyUtc==now.AddMinutes(330),"Abroad conditional return wrong.");
var home=TravelTiming.Parse("""{"profile":{"status":{"state":"Okay","description":"Okay"}}}""",null,now,now);
Check(home.State=="home","Nested basic profile status ignored.");
var jail=TravelTiming.Parse("""{"profile":{"status":{"state":"Jail","description":"In jail"}}}""",Flight("Torn",now,now.AddMinutes(60)),now,now);
Check(TravelTiming.Anchor(jail,now,10,Array.Empty<TravelForecastItem>()).ReadyUtc is null,"Blocked status incorrectly allowed departure.");
var relative="""{"travel":{"destination":"Torn","time_left":600}}""";
var timer=TravelTiming.Parse(null,relative,now,now.AddMinutes(2));
Check(timer.ArrivalUtc==now.AddMinutes(10),"Relative timer drifts with redraw.");
var missing=TravelTiming.Parse(basic,null,now,now);
Check(TravelTiming.Anchor(missing,now,10,Array.Empty<TravelForecastItem>()).ReadyUtc is null,"Missing landing time fabricated.");
var item=new TravelForecastItem{ItemId=1,Country="Mexico",ItemName="Flower",FlightMinutesOneWay=30,BuyPrice=100,MarketValue=300,ProfitPerItem=200,Stock=100,StockUpdatedAtUtc=now,ProfitPerHour=2000,TripProfit=2000,Units=10};
var settings=new AppSettings();
var plan=TravelItineraryEngine.Build(new[]{item},settings,anchor.ReadyUtc!.Value,now,now.AddHours(24));
Check(plan.Legs.Count>0 && plan.Legs[0].DepartUtc>=anchor.ReadyUtc && plan.Legs.All(x=>x.ReturnUtc<=now.AddHours(24)),"Anchored schedule ignores flight or rolling horizon.");
var custom=TravelItineraryEngine.Build(new[]{item},settings,now.AddHours(2),now);
Check(custom.StartUtc==now.AddHours(2) && custom.Legs.All(x=>x.DepartUtc>=now.AddHours(2)),"Custom takeoff ignored.");
var rehab=TravelRehabPlanner.Build(new[]{item},settings,now,now,now.AddHours(24),true,10);
Check(rehab.Legs[0].Kind=="rehab"&&rehab.Legs[0].Country=="Switzerland","Rehab must be the mandatory first stop.");
Check(rehab.Legs[0].ReturnUtc==now.AddMinutes(242)&&rehab.Legs.Skip(1).All(x=>x.DepartUtc>=now.AddMinutes(252)),"Trading overlaps rehab or return turnaround.");
var swissFlight=new TravelSituation{State="outbound",Destination="Switzerland",DepartUtc=now.AddMinutes(-30),ArrivalUtc=now.AddMinutes(86),FlightMinutes=116};
var inFlightRehab=TravelRehabPlanner.Build(Array.Empty<TravelForecastItem>(),settings,now.AddHours(4),now,now.AddHours(24),true,10,swissFlight);
Check(inFlightRehab.Legs.Count==1&&inFlightRehab.Legs[0].ArriveUtc==now.AddMinutes(86)&&inFlightRehab.Legs[0].ReturnUtc==now.AddMinutes(212),"Existing Switzerland flight was duplicated or landing time ignored.");
Check(TravelRehabPlanner.FlightMinutes("Business",true)==38,"Business/book Swiss time wrong.");
Check(TravelRehabPlanner.Build(new[]{item},settings,now.AddDays(-1),now,null,true,10).Legs.Count==0,"Invalid custom rehab start accepted.");
var noRehab=TravelRehabPlanner.Build(new[]{item},settings,now,now,null,false,10);
Check(noRehab.Legs.All(x=>x.Kind!="rehab"),"Disabled rehab still inserted.");
Console.WriteLine("FLIGHT TIMING TEST PASSED: v2 status, outbound, return, abroad, relative timer, blocked/missing data, custom takeoff and rolling horizon.");
namespace TornCommander{
 public sealed class AppSettings{
 public bool TravelMailingBook{get;set;}
 public bool TravelInStockOnly{get;set;}
 public double TravelMinConfidence{get;set;}=.35;
 public int TravelTransferMinutes{get;set;}=10;
 public int TravelCapacity{get;set;}=10;
 public long TravelCashBudget{get;set;}=10000;
 public string TravelFlightType{get;set;}="Airstrip";
 }
 public sealed class TravelForecastItem{
 public int ItemId{get;set;}
 public string Country{get;set;}="";
 public string ItemName{get;set;}="";
 public int FlightMinutesOneWay{get;set;}
 public long ProfitPerItem{get;set;}
 public long BuyPrice{get;set;}
 public long MarketValue{get;set;}
 public int? Stock{get;set;}
 public DateTime? StockUpdatedAtUtc{get;set;}
 public DateTime? RestockAtUtc{get;set;}
 public double ProfitPerHour{get;set;}
 public long TripProfit{get;set;}
 public double Confidence{get;set;}
 public int Units{get;set;}
 }
}
'@
Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $prog -Encoding UTF8
Set-Content -LiteralPath (Join-Path $project 'FlightTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'FlightTest.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Flight timing regression failed.'}
