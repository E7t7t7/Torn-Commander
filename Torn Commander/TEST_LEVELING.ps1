$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderLeveling'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('LevelingQueue.cs','SmartMedicalOptimizer.cs','BaldrListProvider.cs','LevelingScouting.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$program=@'
using TornCommander;
static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
var ids=LevelingPlanner.Import("https://www.torn.com/profiles.php?XID=123\n123\n456,Some player\n789",789);
Check(ids.Select(t=>t.Id).SequenceEqual(new[]{123,456}),"ordered import/dedup/self filter");
Check(LevelingPlanner.Import("""{"targets":[{"player_id":45,"name":"Fixture","level":70,"fair_fight":1.8}]}""")[0].FairFight==1.8,"FFScouter response import");
foreach(string invalid in new[]{"Level 99, stats 123456","https://example.com/profiles.php?XID=123"}){try{LevelingPlanner.Import(invalid);throw new Exception("invalid import");}catch(InvalidDataException){}}
var baldr="""{"Baldr's List 1":[{"id":"123","name":"First","lvl":"35","total":"1,234"},{"id":"456","name":"Second","lvl":"31","total":"640"}],"Baldr's Extra List 1":[{"id":"123","name":"Duplicate","lvl":"35","total":"1234"},{"id":"789","name":"Third","lvl":"20","total":"unknown"}]}""";
var listed=BaldrListProvider.Parse(baldr,"",0,DateTime.UtcNow);
Check(listed.Targets.Select(t=>t.Id).SequenceEqual(new[]{123,456,789}),"Baldr order/dedup");
Check(listed.Targets[0].ListedBattleStats==1234&&listed.Targets[2].ListedBattleStats==null,"Baldr historical stats parse");
Check(listed.Targets[0].Source=="Baldr's List 1"&&listed.Groups.Count==2,"Baldr group attribution");
Check(BaldrListProvider.Parse(baldr,"Baldr's Extra List 1",123,DateTime.UtcNow).Targets.Single().Id==789,"Baldr group/self filtering");
foreach(var invalid in new[]{"[]","{}",baldr.Replace("\"123\"","\"invalid\"")}){
 try{BaldrListProvider.Parse(invalid,"",0,DateTime.UtcNow);throw new Exception("invalid Baldr data accepted");}catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException){}
}
try{BaldrListProvider.Parse(baldr,"Missing list",0,DateTime.UtcNow);throw new Exception("missing group accepted");}catch(InvalidDataException){}
var actualFixture=Environment.GetEnvironmentVariable("BALDR_LIST_TEST_FIXTURE");
if(!string.IsNullOrEmpty(actualFixture)){var actual=BaldrListProvider.Parse(File.ReadAllText(actualFixture),"",0,DateTime.UtcNow);Check(actual.Targets.Count==382&&actual.Groups.Count==7,"current public Baldr data fixture");Console.WriteLine("PUBLIC BALDR DATA PASSED: 382 unique targets across seven lists.");}
Check(LevelingScouting.SearchResults(FFScouterProvider.DecodeResponse("get-targets",404,"""{"code":17,"error":"No targets found matching the specified criteria"}"""),0).Count==0,"documented no-targets response is an empty result");
try{FFScouterProvider.DecodeResponse("check-key",404,"""{"code":17,"error":"not a search"}""");throw new Exception("key failure treated as empty search");}catch(InvalidDataException){}
var scoutNow=DateTime.UtcNow;
var scoutSession=new LevelingSession{OwnStats=300,OwnStatsUtc=scoutNow,MaxStatRatio=0.5};
LevelingTarget weak=new(){Id=123,EstimatedStats=100,EstimateUtc=scoutNow,ScannedUtc=scoutNow,LastActionUtc=scoutNow.AddDays(-30)};
Check(LevelingScouting.Eligible(weak,scoutSession,scoutNow),"100-stat inactive target accepted for 300-stat account");
weak.Name="Test Dummy #2";Check(!LevelingScouting.StrengthEligible(weak,scoutSession,scoutNow),"mission NPC blocked");weak.Name="Player";
weak.UnavailableUntilUtc=scoutNow.AddMinutes(5);Check(!LevelingScouting.CanProbe(weak,scoutNow),"known hospitalized target skipped");Check(LevelingScouting.CanProbe(weak,scoutNow.AddMinutes(5)),"expired hospital timer rechecked");weak.UnavailableUntilUtc=null;Check(LevelingScouting.CanProbe(weak,scoutNow),"unknown availability checked");weak.Finished=true;Check(!LevelingScouting.CanProbe(weak,scoutNow),"finished target not repeated");weak.Finished=false;
weak.Finished=true;weak.Status="Skipped: Hospital";weak.UnavailableUntilUtc=scoutNow.AddSeconds(-1);weak.CheckedUtc=scoutNow;Check(!LevelingScouting.CanProbe(weak,scoutNow.AddSeconds(29)),"cached hospital backoff");Check(LevelingScouting.CanProbe(weak,scoutNow.AddSeconds(30)),"expired hospital retried in same pass");weak.Status="Leave";Check(!LevelingScouting.CanProbe(weak,scoutNow.AddMinutes(1)),"completed win never retried");weak.Finished=false;weak.Status="Not checked";weak.UnavailableUntilUtc=null;weak.CheckedUtc=null;
weak.EstimatedStats=1000;Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow),"1000-stat target blocked");
weak.EstimatedStats=null;Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow),"unknown stats blocked");
weak.EstimatedStats=150;Check(LevelingScouting.Eligible(weak,scoutSession,scoutNow),"inclusive strength cap");
weak.LastActionUtc=scoutNow;Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow)&&LevelingScouting.StrengthEligible(weak,scoutSession,scoutNow),"active account excluded");
weak.LastActionUtc=null;Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow),"unknown activity excluded");
weak.LastActionUtc=scoutNow.AddDays(-30);weak.EstimateUtc=scoutNow.AddDays(-31);Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow),"stale estimate excluded");
weak.EstimateUtc=scoutNow.AddDays(1);Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow),"future estimate excluded");
weak.EstimateUtc=scoutNow;scoutSession.OwnStatsUtc=scoutNow.AddHours(-1);Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow),"stale own stats blocked");
scoutSession.OwnStatsUtc=scoutNow;weak.ScannedUtc=scoutNow.AddDays(-2);Check(!LevelingScouting.Eligible(weak,scoutSession,scoutNow),"old scan blocked");
LevelingScouting.ApplyStats(new[]{weak},"""[{"player_id":123,"bs_estimate":120,"fair_fight":1.2,"last_updated":1700000000,"source":"bss"}]""",scoutNow);
Check(weak.EstimatedStats==120&&weak.FairFight==1.2&&weak.EstimateUtc==DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime,"FFScouter parsing");
LevelingScouting.ApplyStats(new[]{weak},"""[{"player_id":456,"bs_estimate":10}]""",scoutNow);Check(weak.EstimatedStats==null,"missing result clears old estimate");
var searched=LevelingScouting.SearchResults("""{"parameters":{"key":"not-persisted"},"targets":[{"player_id":123,"name":"Fixture","level":25,"last_action":1700000000},{"player_id":123},{"player_id":456}]}""",456);
Check(searched.Count==1&&searched[0].Id==123&&searched[0].LastActionUtc!=null,"search dedupe/self/activity");
Check(!System.Text.Json.JsonSerializer.Serialize(searched).Contains("not-persisted"),"key echo not persisted");
Console.WriteLine("STRENGTH SCOUTING PASSED: low-stat limits, stale/unknown data, activity, parsing, credential echo exclusion.");
Check(new LevelingSession().AutoResumeMedical,"new leveling cooldown waits enabled");
Check(!System.Text.Json.JsonSerializer.Deserialize<LevelingSession>("""{"AutoResumeMedical":false}""")!.AutoResumeMedical,"saved no-resume choice retained");
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
Check(LevelingPlanner.ContinueAfter("Assist")&&LevelingPlanner.ContinueAfter("Interrupted")&&!LevelingPlanner.ContinueAfter("Lost")&&!LevelingPlanner.ContinueAfter("Stalemate"),"interrupted results continue, losses stop");
Check(LevelingPlanner.CanAdvanceUnverified(true,3,1,1,2,2,"Okay","Hospital"),"ended fight corroborated without a win");
Check(!LevelingPlanner.CanAdvanceUnverified(false,3,1,1,2,2,"Okay","Hospital"),"hospital alone not ended evidence");
Check(!LevelingPlanner.CanAdvanceUnverified(true,2,1,1,2,2,"Okay","Hospital"),"cached receipt grace");
Check(!LevelingPlanner.CanAdvanceUnverified(true,3,1,3,2,2,"Okay","Hospital"),"wrong own account");
Check(!LevelingPlanner.CanAdvanceUnverified(true,3,1,1,2,3,"Okay","Hospital"),"wrong opponent");
Check(!LevelingPlanner.CanAdvanceUnverified(true,3,1,1,2,2,"Okay","Okay"),"active fight cannot be abandoned");
Check(!LevelingPlanner.CanAdvanceUnverified(true,3,1,1,2,2,"Unknown","Hospital"),"unknown own state rejected");
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
