# Validate travel preview calculations and compiled dashboard JavaScript on Windows.
$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderTravelPreviewTest'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
$source=Join-Path $PSScriptRoot 'TravelPreview.cs'
Copy-Item -LiteralPath $source -Destination $project
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DashboardJson.cs') -Destination $project
$prog=@'
using System;
using System.Collections.Generic;
using TornCommander;

var now = new DateTime(2026,10,13,0,0,0,DateTimeKind.Utc);
var settings = new AppSettings();
var empty = TravelPreviewEngine.Build(new TravelForecastItem[0], settings, now);
if (empty.FeedRows != 0 || empty.Routes.Count != 0 || !empty.Guidance.Contains("No stock observations"))
    throw new Exception("Empty-feed diagnosis failed.");
var unpriced = new TravelForecastItem { ItemId=1, ItemName="Orchid",Country="Hawaii",BuyPrice=700,
    MarketValue=0,FlightMinutesOneWay=100,Stock=500 };
var missing = TravelPreviewEngine.Build(new[]{unpriced},settings,now);
if (missing.PricedRows != 0 || missing.UnpricedRows != 1 || missing.Routes.Count != 0 ||
    !missing.Guidance.Contains("purchase and market prices"))
    throw new Exception("Missing-price diagnosis failed.");
var route = new TravelForecastItem { ItemId=2, ItemName="Orchid",Country="Hawaii",
    BuyPrice=700,MarketValue=3000,ProfitPerItem=2300,ProfitPerHour=19000,
    TripProfit=9200,Units=4,CashRequired=2800,FlightMinutesOneWay=80,Stock=12,
    StockUpdatedAtUtc=now.AddMinutes(-4),Source="Test provider" };
var preview=TravelPreviewEngine.Build(new[]{route},settings,now);
if(preview.FreshStockRows!=1 || preview.Routes.Count!=1 ||
   preview.Routes[0].OneWayMinutes!=80 ||
   preview.Routes[0].StockVerifiedNow || preview.Routes[0].ScenarioProfit!=9200)
    throw new Exception("Observed-stock preview / no false guarantees regression.");
File.WriteAllText(Path.Combine(args[0],"preview.json"),DashboardJson.Serialize(preview));
route.IsStale=true;
var stale=TravelPreviewEngine.Build(new[]{route},settings,now);
if(stale.Routes.Count!=1 || !stale.Routes[0].StockEvidence.Contains("Stale"))
    throw new Exception("Stale-stock evidence regression.");
route.IsStale=false;
route.StockUpdatedAtUtc=now.AddMinutes(-31);
if(!TravelPreviewEngine.Build(new[]{route},settings,now).Routes[0].StockEvidence.Contains("Stale"))
    throw new Exception("Timestamp aging must override an obsolete cached fresh flag.");
route.StockUpdatedAtUtc=now.AddMinutes(11);
if(!TravelPreviewEngine.Build(new[]{route},settings,now).Routes[0].StockEvidence.Contains("Future-dated"))
    throw new Exception("Future timestamps must not establish shelf availability.");
route.StockStatus="Unknown"; route.Confidence=.1;
settings.TravelInStockOnly=true;
var diag=TravelFeedDiagnostics.Build(new[]{route,unpriced,new TravelForecastItem()},settings);
if(diag.InputRows!=3 || diag.RejectedEconomics!=1 || diag.RejectedStockFilter!=2 || diag.RejectedConfidence!=0)
    throw new Exception("Sequential raw-feed exclusion counts are wrong.");
settings.TravelInStockOnly=false;
if(TravelFeedDiagnostics.Include(route,settings) || !TravelFeedDiagnostics.Include(unpriced,settings))
    throw new Exception("Confidence filter must exclude low-confidence priced routes while retaining unknown prices.");
if(!TravelPreviewEngine.Build(Array.Empty<TravelForecastItem>(),settings,now,null,diag).Guidance.Contains("Raw feed: 3 rows"))
    throw new Exception("Filtered-out input must still be explained.");
Console.WriteLine("TRAVEL PREVIEW ENGINE TEST PASSED: empty/unpriced/stale/fresh and no false guarantee.");
namespace TornCommander {
    public sealed class AppSettings { public bool TravelInStockOnly {get;set;} public bool TravelIncludeLowConfidence {get;set;} public double TravelMinConfidence {get;set;}=0.4; }
    public sealed class TravelForecastItem {
        public int ItemId {get;set;}
        public string ItemName {get;set;}="";
        public string Country {get;set;}="";
        public string Source {get;set;}="";
        public int? Stock {get;set;}
        public DateTime? StockUpdatedAtUtc {get;set;}
        public DateTime? RestockAtUtc {get;set;}
        public bool IsStale {get;set;}
        public string StockStatus {get;set;}="";
        public double Confidence {get;set;}
        public long BuyPrice {get;set;}
        public long MarketValue {get;set;}
        public long ProfitPerItem {get;set;}
        public long CashRequired {get;set;}
        public long TripProfit {get;set;}
        public double ProfitPerHour {get;set;}
        public int FlightMinutesOneWay {get;set;}
        public int Units {get;set;}
    }
}
'@
Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $prog -Encoding UTF8
$proj=@'
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
</Project>
'@
Set-Content -LiteralPath (Join-Path $project 'PreviewTest.csproj') -Value $proj -Encoding UTF8
dotnet run --project (Join-Path $project 'PreviewTest.csproj') --configuration Release -- $project
if($LASTEXITCODE -ne 0){throw 'Travel preview C# regression test failed.'}

$dashboard=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'MainForm.ModernDashboard.cs') -Raw
$start=$dashboard.IndexOf('<script>')
$end=$dashboard.IndexOf('</script>',$start)
if($start -lt 0 -or $end -le $start){throw 'Modern dashboard inline JavaScript not found.'}
$scriptPath=Join-Path $project 'dashboard.js'
[IO.File]::WriteAllText($scriptPath,$dashboard.Substring($start+8,$end-$start-8))
& node --check $scriptPath
if($LASTEXITCODE -ne 0){throw 'Modern dashboard JavaScript syntax invalid.'}
foreach($marker in @('id="travelPreviewRows"','id="travelPreviewDiagnostic"','function renderTravelPreview','renderTravelPreview(t.preview,t,legs)')){
 if(-not $dashboard.Contains($marker)){throw "Missing travel preview dashboard marker: $marker"}
}
Write-Host 'TRAVEL PREVIEW DASHBOARD TEST PASSED: JavaScript syntax and wire-up.'

$renderTest=@'
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const script=fs.readFileSync(process.argv[2],'utf8');
const report=JSON.parse(fs.readFileSync(process.argv[3],'utf8'));
const start=script.indexOf('function renderTravelPreview('),end=script.indexOf('function renderItinerary(',start);
const nodes={};
const context={ $:id=>nodes[id]??=( {} ), esc:s=>String(s??''),money:x=>String(x)};
vm.createContext(context);
vm.runInContext(script.slice(start,end),context);
context.renderTravelPreview(report,{situation:{state:'outbound'}},[]);
assert(nodes.travelPreviewCount.textContent.includes('1 options'),'Serialized routes vanished on render');
assert(nodes.travelPreviewDiagnostic.textContent.includes('Feed: 1'),'Serialized diagnostics vanished on render');
assert(nodes.travelPreviewRows.innerHTML.includes('Orchid'),'Route card is empty');
const apiStart=script.indexOf('function renderApiActivity('),apiEnd=script.indexOf('setInterval(()=>renderApiActivity',apiStart);
vm.runInContext(script.slice(apiStart,apiEnd),context);
context.renderApiActivity({apiConnected:true,apiActivity:{cooldownUntil:new Date(Date.now()+5000).toISOString()}});
assert(nodes.fullSyncButton.disabled && nodes.apiBadge.textContent.includes('API cooldown'),'Cooldown buttons/notice missing');
context.renderApiActivity({apiConnected:true,apiActivity:{cooldownUntil:new Date(Date.now()-5000).toISOString()}});
assert(!nodes.fullSyncButton.disabled,'Cooldown never enables sync again');
context.renderApiActivity({apiConnected:true,apiActivity:{syncing:true}});
assert(nodes.fullSyncButton.disabled,'Sync allows duplicate requests');
console.log('DASHBOARD DATA TEST PASSED: serialized route rendering and API cooldown controls.');
'@
$renderPath=Join-Path $project 'render-test.js'
[IO.File]::WriteAllText($renderPath,$renderTest)
& node $renderPath $scriptPath (Join-Path $project 'preview.json')
if($LASTEXITCODE -ne 0){throw 'Serialized travel preview rendering failed.'}
