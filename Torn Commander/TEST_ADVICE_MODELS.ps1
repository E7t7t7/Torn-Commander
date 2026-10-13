# Synthetic model tests; no credentials, browser sessions or real user data.
$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderAdviceTest'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('CompanyIntelligence.cs','TravelArrivalAdvisor.cs')){
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project
}
$prog=@'
using System.Text.Json;
using TornCommander;
void Check(bool value,string message){if(!value)throw new Exception(message);}
var now=new DateTime(2026,10,13,0,0,0,DateTimeKind.Utc);
var missing=CompanyIntelligence.ReadProfile("""{"profile":{"id":1}}""");
Check(missing.DailyIncome is null && missing.Employees is null && missing.FullCostMargin is null,"Missing API data must remain unknown.");
var zero=CompanyIntelligence.ReadProfile("""{"profile":{"id":1,"income":{"daily":0},"employees":{"hired":0},"advertisement_budget":0}}""");
Check(zero.DailyIncome==0 && zero.Employees==0 && zero.AfterAdvertising==0,"Real zero is not missing.");
using(var invalid=JsonDocument.Parse("""{"advertising":200,"payroll":-1}""")){
 Check(CompanyIntelligence.CompanyCost(invalid.RootElement,"advertising",100)==200,"Valid cost rejected.");
 try{CompanyIntelligence.CompanyCost(invalid.RootElement,"payroll",100);throw new Exception("Negative cost accepted.");}
 catch(InvalidDataException){}
}
var journal=new CompanyJournal{ActiveCompanyId=1};
Check(!CompanyIntelligence.CollectionDue(journal,now),"Collection must default off.");
journal.AutoCollectEnabled=true;
Check(CompanyIntelligence.CollectionDue(journal,now),"Opted-in empty history must collect.");
zero.DayUtc=now.ToString("yyyy-MM-dd");zero.DailyPayroll=20;zero.DailyStockCost=30;zero.Notes="keep";
CompanyIntelligence.Upsert(journal,zero);
Check(!CompanyIntelligence.CollectionDue(journal,now) && CompanyIntelligence.CollectionDue(journal,now.AddDays(1)),"Daily schedule failed.");
missing.DayUtc=zero.DayUtc;
CompanyIntelligence.Upsert(journal,missing);
Check(journal.Days.Count==1 && missing.DailyAdvertising==0 && missing.DailyPayroll==20 && missing.Notes=="keep","Repeated sync lost manual costs or duplicated day.");
Check(CompanyIntelligence.Analyze(journal.Days).SampleDays==0,"Unknown income counted as zero in baseline.");
var days=Enumerable.Range(1,7).Select(i=>new CompanyObservation{FromApi=true,DayUtc="2026-10-"+i.ToString("00"),DailyIncome=1000,DailyAdvertising=100}).ToList();
Check(CompanyIntelligence.Analyze(days).State=="INCOMPLETE","Missing staffing should block budget inference.");
var shop=new TravelLandingSnapshot{Country="Test",Cash=100,Capacity=4,SeenUtc=now,Items=new(){
 new(){Name="A",Stock=2,BuyPrice=10},new(){Name="B",Stock=2,BuyPrice=10},new(){Name="C",Stock=0,BuyPrice=1000}}};
var quotes=new List<TravelForecastItem>{
 new(){Country="Test",ItemName="A",MarketValue=30,PricingUpdatedAtUtc=now},
 new(){Country="Test",ItemName="B",MarketValue=25,PricingUpdatedAtUtc=now},
 new(){Country="Test",ItemName="C",MarketValue=2000,PricingUpdatedAtUtc=now,RestockAtUtc=now.AddMinutes(5),Confidence=.8}};
TravelLandingAdvice Eval()=>TravelArrivalAdvisor.Evaluate(shop,quotes,Array.Empty<TravelObservation>(),false,now);
var advice=Eval();
Check(advice.BasketUnits==4 && advice.BasketNet==70 && advice.Action=="Buy available stock","Unaffordable predicted stock incorrectly outranked affordable mixed basket.");
shop.Cash=0;Check(Eval().Action=="No cash abroad" && Eval().BasketUnits==0,"Zero cash advice failed.");
shop.Cash=100;
quotes[0].PricingUpdatedAtUtc=now.AddHours(-13);quotes[1].PricingUpdatedAtUtc=null;
Check(Eval().BasketNet==0,"Stale or undated resale quote counted as profit.");
quotes[0].PricingUpdatedAtUtc=now.AddMinutes(11);
Check(Eval().BasketNet==0,"Future quote counted as profit.");
quotes[0].PricingUpdatedAtUtc=now;shop.Items[0].Stock=0;
Check(Eval().Choices.First(x=>x.Item=="A").RestockMinutes is null,"Invented a restock ETA.");
shop.Items[2].BuyPrice=10;quotes[2].MarketValue=50;
Check(Eval().Action=="Consider waiting","Affordable supported forecast was lost.");
Console.WriteLine("COMPANY / ARRIVAL TEST PASSED: unknown values, costs, daily schedule, history, quote age, mixed budgets, forecast limits.");
namespace TornCommander{
 public sealed class TravelObservation{}
 public sealed class TravelForecastItem{
  public string Country{get;set;}="";
  public string ItemName{get;set;}="";
  public string Source{get;set;}="";
  public long MarketValue{get;set;}
  public DateTime? PricingUpdatedAtUtc{get;set;}
  public DateTime? RestockAtUtc{get;set;}
  public bool IsStale{get;set;}
  public double Confidence{get;set;}
 }
}
'@
Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $prog -Encoding UTF8
Set-Content -LiteralPath (Join-Path $project 'AdviceTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'AdviceTest.csproj') --configuration Release
if($LASTEXITCODE -ne 0){throw 'Company/arrival model regression failed.'}
