$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderLeveling'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('LevelingQueue.cs','SmartMedicalOptimizer.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$program=@'
using TornCommander;
static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
var ids=LevelingPlanner.Import("https://www.torn.com/profiles.php?XID=123\n123\n456,Some player\n789",789);
Check(ids.Select(t=>t.Id).SequenceEqual(new[]{123,456}),"ordered import/dedup/self filter");
Check(LevelingPlanner.Import("""{"targets":[{"player_id":45,"name":"Fixture","level":70,"fair_fight":1.8}]}""")[0].FairFight==1.8,"FFScouter response import");
foreach(string invalid in new[]{"Level 99, stats 123456","https://example.com/profiles.php?XID=123"}){try{LevelingPlanner.Import(invalid);throw new Exception("invalid import");}catch(InvalidDataException){}}
var now=DateTime.UtcNow;
var o=new MedicalObservation("A",1,now,now,0,21600,0,false,"fixture",new(){{68,200},{67,200},{66,200},{206,200}});
var cfg=new SmartMedicalSettings();
var full=LevelingPlanner.Recovery(o,cfg,0,100,100,now);
Check(full.State=="Heal first"&&full.TargetLife==100&&full.CooldownSeconds==8100,"full-life minimum cooldown");
Check(full.Items.All(SmartMedicalOptimizer.AllowedId)&&!full.Items.Contains(206)&&!full.Items.Contains(739),"drug/blood entered recovery");
Check(LevelingPlanner.Recovery(o,cfg,90,100,100,now).Items.SequenceEqual(new[]{67}),"small life deficit");
Check(LevelingPlanner.Recovery(o,cfg,100,100,100,now).State=="Ready","unnecessary healing");
Check(LevelingPlanner.Recovery(o with{HospitalUntilUtc=now.AddMinutes(66)},cfg,100,100,100,now).Items.SequenceEqual(new[]{66}),"hospital plus life");
Check(LevelingPlanner.Recovery(o with{CooldownSeconds=21599},cfg,95,100,100,now).Items.SequenceEqual(new[]{68}),"final-item overshoot");
Check(LevelingPlanner.Recovery(o with{MaximumSeconds=32400,CooldownSeconds=32399},cfg,95,100,100,now).State=="Heal first","nine-hour cap");
Check(LevelingPlanner.Recovery(o with{CooldownSeconds=21600},cfg,50,100,100,now).State=="Cooldown","exhausted cooldown");
Check(LevelingPlanner.Recovery(o with{ObservedUtc=now.AddMinutes(-1)},cfg,0,100,100,now).State=="Unverified","stale life data");
Check(LevelingPlanner.Recovery(o with{Inventory=new()},cfg,0,100,100,now).Items.Length==0,"empty stock");
cfg.Reserves[66]=200;Check(!LevelingPlanner.Recovery(o,cfg,0,100,100,now).Items.Contains(66),"Morphine reserve");cfg.Reserves.Clear();
cfg.ReserveMinutes=350;Check(LevelingPlanner.Recovery(o,cfg,0,100,100,now).Items.Length==0,"cooldown reserve");cfg.ReserveMinutes=0;
Check(LevelingPlanner.Recovery(o with{BonusPercent=50},cfg,0,100,100,now).CooldownSeconds<full.CooldownSeconds,"effectiveness bonus");
var random=new Random(620);
for(int trial=0;trial<100;trial++){
 int small=random.Next(7),fak=random.Next(7),morph=random.Next(7),life=random.Next(101),hospital=random.Next(121),cd=random.Next(18000,21700);
 var observation=o with{Inventory=new(){{68,small},{67,fak},{66,morph}},HospitalUntilUtc=now.AddMinutes(hospital),CooldownSeconds=cd};
 var actual=LevelingPlanner.Recovery(observation,cfg,life,100,100,now);int best=int.MaxValue;
 for(int a=0;a<=small;a++)for(int b=0;b<=fak;b++)for(int c=0;c<=morph;c++){
  int cost=a*600+b*900+c*1200,last=c>0?1200:b>0?900:a>0?600:0;
  if(a*20+b*40+c*70>=hospital&&a*5+b*10+c*15>=100-life&&cd<21600&&cost-last<21600-cd)best=Math.Min(best,cost);
 }
 Check(best==int.MaxValue?actual.Items.Length==0:actual.CooldownSeconds==best,"hospital/life oracle "+trial);
}
var unix=new DateTimeOffset(now).ToUnixTimeSeconds();
string Attack(int actor,int defender,long started,long ended,string result,int id=91)=>$$"""{"attacks":[{"id":{{id}},"started":{{started}},"ended":{{ended}},"attacker":{"id":{{actor}}},"defender":{"id":{{defender}}},"result":"{{result}}"}]}""";
Check(LevelingPlanner.FindReceipt(Attack(1,2,unix,unix+5,"Attacked"),1,2,now,Array.Empty<long>())?.Result=="Attacked","valid Leave result");
Check(LevelingPlanner.FindReceipt(Attack(3,2,unix,unix+5,"Attacked"),1,2,now,Array.Empty<long>())==null,"wrong attacker");
Check(LevelingPlanner.FindReceipt(Attack(1,3,unix,unix+5,"Attacked"),1,2,now,Array.Empty<long>())==null,"wrong target");
Check(LevelingPlanner.FindReceipt(Attack(1,2,unix-10,unix+5,"Attacked"),1,2,now,Array.Empty<long>())==null,"old fight");
Check(LevelingPlanner.FindReceipt(Attack(1,2,unix,unix+5,"Attacked"),1,2,now,new long[]{91})==null,"duplicate receipt");
Check(LevelingPlanner.FindReceipt(Attack(1,2,unix,0,"None"),1,2,now,Array.Empty<long>())==null,"unfinished fight");
Console.WriteLine("LEVELING PASSED: imports, full-life/hospital optimization, exhaustive oracle, bonuses, reserves, cooldowns, drug exclusion and replay-safe account/target results.");
'@
[IO.File]::WriteAllText((Join-Path $project 'Program.cs'),$program)
[IO.File]::WriteAllText((Join-Path $project 'Test.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
dotnet run --project (Join-Path $project 'Test.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Leveling tests failed'}
