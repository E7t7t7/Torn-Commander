$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderItemIntelligence'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('IntelligenceStore.cs','ItemIntelligence.cs','IntelligenceProviders.cs','MarketHistory.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$program=@'
using TornCommander;
static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
var now=DateTime.UtcNow;var start=now.AddMinutes(-385);
ItemObservation Row(int minute,int quantity,string source="Fixture")=>new(source,"Switzerland",361,"Neumune Tablet",start.AddMinutes(minute),now,quantity,900000,1400000);
var history=Enumerable.Range(0,78).Select(i=>Row(i*5,i*5%60<30?0:200)).ToList();
var forecast=RestockEstimator.Analyze(history,now);
Assert(forecast.Events==6&&forecast.Intervals==5,"restock evidence count");
Assert(forecast.FromUtc<forecast.ToUtc&&forecast.Evaluations==1&&forecast.MeanAbsoluteErrorMinutes<=3,"walk-forward evaluation");
Assert(RestockEstimator.Analyze(history.Take(5),now).FromUtc==null,"manufactured forecast");
Assert(RestockEstimator.Analyze(history,now.AddHours(2)).FromUtc==null,"stale forecast");
Assert(RestockEstimator.Analyze(history.Where(x=>x.ObservedUtc<now.AddHours(-1)).Append(Row(385,0)),now).FromUtc==null,"long-gap cycles");
try{RestockEstimator.Analyze(history.Append(Row(385,0,"Other")),now);throw new Exception("mixed sources accepted");}catch(InvalidDataException){}
var watch=new ItemWatch{PriceBelow=100};
var oldQuote=new MarketHistoryPoint(361,"Fixture",now.AddMinutes(-5),110,10);
var newQuote=new MarketHistoryPoint(361,"Fixture",now,90,10);
Assert(MarketPriceAlerts.Crossings(watch,oldQuote,newQuote,now).Count==1,"asking-price crossing");
Assert(MarketPriceAlerts.Crossings(watch,oldQuote,newQuote with{LowestReturnedAsk=null},now).Count==0,"unknown ask alert");
Assert(MarketPriceAlerts.Crossings(watch,oldQuote,newQuote,now.AddHours(1)).Count==0,"stale ask alert");
Assert(MarketPriceAlerts.Crossings(watch,oldQuote with{ObservedUtc=now.AddHours(-2)},newQuote,now).Count==0,"unobserved price gap alert");
Assert(MarketPriceAlerts.Crossings(watch,oldQuote,newQuote with{ObservedUtc=now.AddHours(1)},now).Count==0,"future ask alert");
Assert(ItemAlertRules.Crossings(watch,Row(375,0),Row(380,5),now).Count==1,"restock crossing");
Assert(ItemAlertRules.Crossings(watch,null,Row(380,5),now).Count==0,"startup alert");
Assert(ItemAlertRules.Crossings(watch,Row(375,0),Row(380,5),now.AddHours(1)).Count==0,"stale alert");
var unix=new DateTimeOffset(now.AddMinutes(-2)).ToUnixTimeSeconds();
var json="""{"stocks":{"swi":{"update":UNIX,"stocks":[{"id":361,"name":"Neumune Tablet","quantity":0,"cost":900000},{"id":999,"name":"Missing quantity","cost":1}]}}}""".Replace("UNIX",unix.ToString());
var parsed=ForeignStockProvider.Parse(json,"YATA",now);Assert(parsed.Count==1&&parsed[0].Quantity==0&&parsed[0].MarketValue==null,"quantity/price semantics");
var dir=Path.Combine(Path.GetTempPath(),"CommanderIntelDb"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
var db=new IntelligenceStore(Path.Combine(dir,"history.sqlite"));Assert(db.Add(history,now)==history.Count,"SQLite batch");Assert(db.Add(history,now)==0,"duplicate source times");
db.Save("A","settings",new ItemIntelligenceSettings{Monitor=true});Assert(!db.Load("B","settings",new ItemIntelligenceSettings()).Monitor,"profile settings leaked");
db.Snapshot("A","company","1",now,new{Value="fixture-only"});Assert(db.Snapshots("B","company","1").Count==0,"private snapshot leaked");
try{db.Add(new[]{Row(386,1,"Rollback"),Row(387,-1,"Rollback")},now);throw new Exception("invalid batch accepted");}catch(InvalidDataException){}
Assert(!db.Read().Any(x=>x.Source=="Rollback"),"partial import persisted");
Assert(new IntelligenceStore(Path.Combine(dir,"history.sqlite")).Read(361).Count==history.Count,"restart persistence");
db.Snapshot("A","official-item-market","361",now.AddDays(-10),oldQuote);
db.Snapshot("B","official-item-market","361",now.AddDays(-10),oldQuote);
db.Snapshot("A","company","1",now.AddDays(-10),new{Value="retain"});
db.Snapshot("A","official-item-market","361",now,newQuote);
Assert(db.ExportSnapshots("A","official-item-market").Count()==2,"market export truncated");
db.PruneSnapshots("A","official-item-market",7);
Assert(db.ExportSnapshots("A","official-item-market").Count()==1,"market retention");
Assert(db.ExportSnapshots("B","official-item-market").Count()==1,"retention crossed profiles");
Assert(db.ExportSnapshots("A","company").Count()==2,"retention crossed datasets");
Assert(db.ExportItems().Count()==history.Count,"stock export lost rows");
Console.WriteLine("ITEM INTELLIGENCE PASSED: windows, walk-forward error, source/gap/staleness isolation, alerts, provider fixtures, SQLite dedupe/rollback/profile isolation and persistence.");
'@
[IO.File]::WriteAllText((Join-Path $project 'Program.cs'),$program)
[IO.File]::WriteAllText((Join-Path $project 'Test.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.32" /></ItemGroup></Project>')
dotnet run --project (Join-Path $project 'Test.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Item intelligence tests failed'}
