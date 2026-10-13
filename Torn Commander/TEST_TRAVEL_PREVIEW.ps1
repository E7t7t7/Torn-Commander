# Validate travel preview calculations and compiled dashboard JavaScript on Windows.
$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderTravelPreviewTest'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
$source=Join-Path $PSScriptRoot 'TravelPreview.cs'
Copy-Item -LiteralPath $source -Destination $project
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
route.IsStale=true;
var stale=TravelPreviewEngine.Build(new[]{route},settings,now);
if(stale.Routes.Count!=1 || !stale.Routes[0].StockEvidence.Contains("Stale"))
    throw new Exception("Stale-stock evidence regression.");
Console.WriteLine("TRAVEL PREVIEW ENGINE TEST PASSED: empty/unpriced/stale/fresh and no false guarantee.");
namespace TornCommander {
    public sealed class AppSettings {}
    public sealed class TravelForecastItem {
        public int ItemId {get;set;}
        public string ItemName {get;set;}=""
        public string Country {get;set;}=""
        public string Source {get;set;}=""
        public int? Stock {get;set;}
        public DateTime? StockUpdatedAtUtc {get;set;}
        public DateTime? RestockAtUtc {get;set;}
        public bool IsStale {get;set;}
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
dotnet run --project (Join-Path $project 'PreviewTest.csproj') --configuration Release
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
