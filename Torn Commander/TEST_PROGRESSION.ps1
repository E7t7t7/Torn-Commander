$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderProgression'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('Models.cs','TornRoutes.cs','LossAccounting.cs','TravelPreview.cs','TravelTiming.cs','ProgressionAdvisor.cs','EducationObservation.cs','DashboardJson.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$prog=@'
using System.Text.Json;
using TornCommander;
void Check(bool value,string message){if(!value)throw new Exception(message);}
var now=new DateTime(2026,10,13,14,0,0,DateTimeKind.Utc);
var day=now.ToLocalTime();
var s=new TornSnapshot{Level=20,CurrentEnergy=100,MaxEnergy=100,CurrentNerve=50,MaxNerve=50,Wallet=500000,FastRetrievedAt=now,FullRetrievedAt=now,EducationDataKnown=true};
s.RawJson["bars"]="{}";s.RawJson["money"]="{}";s.RawJson["education"]="{}";
var goals=new ProgressionJournal();
var coach=new RecommendationResult{NerveAdvice="Use the crime coach."};
var situation=new TravelSituation{State="home"};
var trades=new[]{new TradeEntry{Date=day,ProfileId="one",Quantity=1,BuyEach=100,SellEach=200,SellingFees=10},new TradeEntry{Date=day,ProfileId="two",Quantity=1,BuyEach=0,SellEach=99999},new TradeEntry{Date=day,Quantity=1,BuyEach=0,SellEach=99999}};
var route=new TravelForecastItem{Country="Mexico",ItemName="Flower",BuyPrice=100,MarketValue=300,Units=3,Stock=3,StockUpdatedAtUtc=now,PricingUpdatedAtUtc=now,ProfitPerItem=200,CashRequired=300,TripProfit=600,ProfitPerHour=600};
ProgressionReport Plan()=>ProgressionAdvisor.Build(s,coach,goals,"one",situation,trades,new[]{route},now,day);
var r=Plan();
Check(r.TrainingEnergy==70 && r.FlexibleEnergy==30 && r.SpendableCash==400000,"Allocation or reserve math failed.");
Check(r.Steps[0].Id=="training","Capped energy should be urgent.");
Check(r.RecordedMargin==90 && r.Trades==1 && r.LegacyTradesExcluded==1,"Income crossed profiles or counted unassigned trades.");
trades[0].SellingFees=null;
Check(Plan().RecordedMargin==0&&Plan().UnknownFeeTrades==1,"Unknown selling fees fabricated as zero.");
trades[0].SellingFees=10;
Check(r.Steps.First(x=>x.Id=="cash").Detail.Contains("Mexico"),"Fresh affordable income route was lost.");
goals.ForDay(day).Add("training");
Check(Plan().Steps[^1].Done,"Completed task not retained.");
Check(goals.ForDay(day.AddDays(1)).Count==0,"Daily tasks did not reset.");
var secondProfile=new ProgressionJournal();
Check(!secondProfile.ForDay(day).Contains("training"),"Journal completion crossed profiles.");
s.FastRetrievedAt=now.AddMinutes(-11);
Check(Plan().TrainingEnergy is null,"Stale energy used for an allocation.");
s.FastRetrievedAt=now;s.EducationDataKnown=false;
Check(Plan().Steps.First(x=>x.Id=="education").Detail.Contains("missing data"),"Missing education treated as no course.");
s.EducationDataKnown=true;s.CurrentEducationName="Education #1";s.EducationEndsUtc=now.AddSeconds(-1);
Check(Plan().Steps.First(x=>x.Id=="education").Detail.Contains("confirm completion"),"Past completion claimed verified completion.");
situation.State="outbound";
Check(Plan().Steps.First(x=>x.Id=="cash").Detail.Contains("current flight"),"Outbound player was told to depart again.");
situation.State="home";goals.CashReserve=600000;
Check(Plan().SpendableCash==0 && Plan().Steps.First(x=>x.Id=="cash").Detail.Contains("protected cash"),"Reserve allowed unaffordable spending.");
goals.CashReserve=100000;route.PricingUpdatedAtUtc=now.AddHours(-13);
Check(!Plan().Steps.First(x=>x.Id=="cash").Detail.Contains("Mexico"),"Stale quote became a cash recommendation.");
var education=EducationReader.Read("""{"education":{"complete":[1,2],"current":{"id":3,"until":1791900000}}}""")!;
Check(education.HasCurrent && education.CourseId==3 && education.Completed.Count==2 && education.EndsUtc==DateTimeOffset.FromUnixTimeSeconds(1791900000).UtcDateTime,"Education API timing parsed incorrectly.");
Check(EducationReader.Read("""{"education":{"complete":[],"current":null}}""")?.HasCurrent==false,"Null current course not recognized.");
Check(EducationReader.Read("""{"education":{"complete":[]}}""") is null,"Incomplete education mistaken for no course.");
File.WriteAllText(Path.Combine(args[0],"progression.json"),DashboardJson.Serialize(new{goals=new{training=70,reserve=100000,target=500000},report=r}));
Console.WriteLine("PROGRESSION PASSED: energy/cash allocation, profiles, priorities, date rollover, stale/missing data, travel state, quote age and education timestamps.");
'@
Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $prog -Encoding UTF8
Set-Content -LiteralPath (Join-Path $project 'ProgressionTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'ProgressionTest.csproj') -c Release -- $project
if($LASTEXITCODE -ne 0){throw 'Progression model test failed.'}
$dashboard=Get-Content (Join-Path $PSScriptRoot 'MainForm.ModernDashboard.cs') -Raw
$from=$dashboard.IndexOf("let progressionIdentity=")
$to=$dashboard.IndexOf('function renderApiActivity(',$from)
$jsPath=Join-Path $project 'progression-render.js'
[IO.File]::WriteAllText($jsPath,$dashboard.Substring($from,$to-$from))
$test=@'
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const nodes={},data=JSON.parse(fs.readFileSync(process.argv[3],'utf8'));
const context={$:id=>nodes[id]??={},esc:x=>String(x??''),money:x=>String(x)};
vm.createContext(context);vm.runInContext(fs.readFileSync(process.argv[2],'utf8'),context);
context.renderProgression(data,'one');
assert(nodes.progressionSteps.innerHTML.includes('Train before selling'),'Daily steps not rendered');
assert(nodes.progressionMetrics.textContent.includes('across 1 entries'),'Account-specific totals missing');
assert.strictEqual(nodes.goalTraining.value,70);
context.renderProgression({goals:{training:90},report:{day:data.report.day,steps:[]}},'two');
assert.strictEqual(nodes.goalTraining.value,90,'Profile switch left the other account goals in the form');
console.log('PROGRESSION DASHBOARD PASSED: serialized daily plan and profile goal switch.');
'@
$testPath=Join-Path $project 'test.js'
[IO.File]::WriteAllText($testPath,$test)
node $testPath $jsPath (Join-Path $project 'progression.json')
if($LASTEXITCODE -ne 0){throw 'Progression render test failed.'}
