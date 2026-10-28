$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderMedical'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SmartMedicalOptimizer.cs') -Destination $project
$program=@'
using TornCommander;
static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
var now=DateTime.UtcNow;var settings=new SmartMedicalSettings();
MedicalObservation Obs(int minutes,int cooldown=0,int maximum=21600,double bonus=0,Dictionary<int,int>? inventory=null)=>new("A",123,now,now.AddMinutes(minutes),cooldown,maximum,bonus,false,"fixture",inventory??new(){{68,50},{67,50},{66,50},{739,50}});
foreach(var test in new[]{(18,600,68),(38,900,67),(66,1200,66),(75,1800,0)}){
 var p=SmartMedicalOptimizer.Plan(Obs(test.Item1),settings,now);Assert(p.State=="Ready"&&p.CooldownSeconds==test.Item2,"hospital "+test.Item1);if(test.Item3!=0)Assert(p.Items.SequenceEqual(new[]{test.Item3}),"wrong medicine");
}
// Large supplies must not expand the search or change a strictly better cooldown choice.
var watch=System.Diagnostics.Stopwatch.StartNew();
for(int i=0;i<100;i++){
 var abundant=SmartMedicalOptimizer.Plan(Obs(66,inventory:new(){{68,1000000},{67,1000000},{66,5}}),settings,now);
 Assert(abundant.Items.SequenceEqual(new[]{66})&&abundant.CooldownSeconds==1200,"large inventory changed optimum");
}
watch.Stop();Assert(watch.Elapsed<TimeSpan.FromSeconds(5),"short hospital planning scales with stockpile");
Assert(!settings.Automatic&&settings.Permissions[68]&&settings.Permissions[67]&&settings.Permissions[66]&&!settings.BloodBags,"unsafe defaults");
foreach(var bonus in new[]{0d,10d,20d,30d,50d}){var p=SmartMedicalOptimizer.Plan(Obs(27,bonus:bonus),settings,now);Assert(p.CooldownSeconds==(bonus==50?600:900),"bonus combination");}
Assert(SmartMedicalOptimizer.Plan(Obs(18,21599),settings,now).State=="Ready","final-item overshoot forbidden");
Assert(SmartMedicalOptimizer.Plan(Obs(66,32399,32400),settings,now).State=="Ready","nine-hour final item");
Assert(SmartMedicalOptimizer.Plan(Obs(75,21599),settings,now).State=="Cooldown","ineligible second overshoot item");
var blocked=SmartMedicalOptimizer.Plan(Obs(18,21600),settings,now);Assert(blocked.State=="Cooldown"&&blocked.AvailableUtc>now,"exhausted cooldown");
Assert(SmartMedicalOptimizer.Plan(Obs(18,32400,32400),settings,now).State=="Cooldown","nine-hour exhausted");
settings.ReserveMinutes=10;Assert(SmartMedicalOptimizer.Plan(Obs(18,21000),settings,now).State=="Cooldown","cooldown reserve");settings.ReserveMinutes=0;
Assert(SmartMedicalOptimizer.Plan(Obs(38,inventory:new(){{68,2}}),settings,now).Items.Length==2,"bounded combination");
Assert(SmartMedicalOptimizer.Plan(Obs(38,inventory:new(){{68,1}}),settings,now).State=="Supplies","missing supplies");
settings.Reserves[66]=50;Assert(!SmartMedicalOptimizer.Plan(Obs(66),settings,now).Items.Contains(66),"item reserve");settings.Reserves.Clear();
Assert(SmartMedicalOptimizer.Plan(Obs(18,inventory:new()),settings,now).State=="Supplies","empty inventory");
Assert(SmartMedicalOptimizer.Plan(Obs(18) with{ObservedUtc=now.AddMinutes(-2)},settings,now).State=="Unverified","stale hospitalization");
Assert(SmartMedicalOptimizer.Plan(Obs(18) with{ObservedUtc=now.AddMinutes(2)},settings,now).State=="Unverified","future observation");
settings.BloodBags=true;settings.Permissions[739]=true;
Assert(!SmartMedicalOptimizer.Plan(Obs(75),settings,now).Items.Contains(739),"unverified blood eligibility");
var blood=SmartMedicalOptimizer.Plan(Obs(75) with{IvVerified=true},settings,now);Assert(blood.Items.SequenceEqual(new[]{739}),"equal cooldown fewer items");
settings.BloodBags=false;settings.Prices[66]=1000000;Assert(SmartMedicalOptimizer.Plan(Obs(75),settings,now).Items.All(x=>x==67),"equal cooldown cost tie");settings.Prices.Clear();
foreach(var id in new[]{206,197,738,731,0,-1,9999})Assert(!SmartMedicalOptimizer.AllowedId(id),"drug/unknown/non-universal bag entered allowlist");
try{new SmartMedicalSettings{Permissions=new(){{206,true}}}.Validate();throw new Exception("drug permission accepted");}catch(InvalidDataException){}
var perks="""{"perks":{"education":["10% Medical item effectiveness","10% Medical item effectiveness","Withdraw and deliver blood"],"faction":["30% Medical item effectiveness","3 hours of medical cooldown"],"job":[]}}""";
var mods=SmartMedicalOptimizer.Modifiers(perks,settings);Assert(mods.Bonus==50&&mods.Maximum==32400&&mods.Iv,"API modifiers");
settings.AutoDetect=false;settings.ManualBonusPercent=20;settings.ManualMaximumMinutes=540;mods=SmartMedicalOptimizer.Modifiers("{}",settings);Assert(mods.Bonus==20&&mods.Maximum==32400&&!mods.Iv,"manual verification boundary");
var before=Obs(38,1000,inventory:new(){{67,2}});var after=before with{ObservedUtc=now.AddSeconds(5),HospitalUntilUtc=now,CooldownSeconds=1895,Inventory=new(){{67,1}}};
Assert(SmartMedicalOptimizer.VerifyUse(before,after,67,now.AddSeconds(5)),"valid usage");
Assert(!SmartMedicalOptimizer.VerifyUse(before,after with{Inventory=new(){{67,2}}},67,now.AddSeconds(5)),"failed use accepted");
Assert(!SmartMedicalOptimizer.VerifyUse(before,after with{ProfileId="B"},67,now.AddSeconds(5)),"cross-account verification");
Assert(!SmartMedicalOptimizer.VerifyUse(before,after with{CooldownSeconds=1000},67,now.AddSeconds(5)),"unverified cooldown");
Assert(!SmartMedicalOptimizer.VerifyUse(before,after with{HospitalUntilUtc=before.HospitalUntilUtc},67,now.AddSeconds(5)),"unverified hospital reduction");
Assert(!SmartMedicalOptimizer.VerifyUse(before,after,206,now.AddSeconds(5)),"drug verification accepted");
var zeroBefore=before with{CooldownSeconds=0};
Assert(SmartMedicalOptimizer.VerifyUse(zeroBefore,after with{ObservedUtc=now.AddSeconds(70.5),CooldownSeconds=851},67,now.AddSeconds(70.5)),"live zero-cooldown FAK receipt: 851 seconds after delayed verification");
Assert(SmartMedicalOptimizer.VerifyUse(zeroBefore with{CooldownSeconds=10},after with{ObservedUtc=now.AddSeconds(70),CooldownSeconds=850},67,now.AddSeconds(70)),"old cooldown expiring before item use");
Assert(!SmartMedicalOptimizer.VerifyUse(zeroBefore,after with{ObservedUtc=now.AddSeconds(70),CooldownSeconds=0},67,now.AddSeconds(70)),"missing new cooldown accepted");
Assert(!SmartMedicalOptimizer.VerifyUse(zeroBefore,after with{ObservedUtc=now.AddSeconds(70),CooldownSeconds=1500},67,now.AddSeconds(70)),"excess cooldown accepted");
Assert(!SmartMedicalOptimizer.VerifyUse(zeroBefore,after with{ObservedUtc=now.AddSeconds(70),CooldownSeconds=851,Inventory=new(){{67,2}}},67,now.AddSeconds(70)),"decay fix accepted unconsumed item");

Assert(SmartMedicalOptimizer.VerifyUse(before,after with{ObservedUtc=now.AddSeconds(110),CooldownSeconds=1790},67,now.AddSeconds(110)),"delayed receipt should verify without authorizing another item");
Assert(!SmartMedicalOptimizer.VerifyUse(before,after with{ObservedUtc=now.AddSeconds(181),CooldownSeconds=1719},67,now.AddSeconds(181)),"unbounded old receipt accepted");

var unused=before with{ObservedUtc=now.AddSeconds(5),CooldownSeconds=995};
Assert(SmartMedicalOptimizer.VerifyUnused(before,unused,67,now.AddSeconds(5)),"unused authorization not recoverable");
Assert(!SmartMedicalOptimizer.VerifyUnused(before,after,67,now.AddSeconds(5)),"consumed item reauthorized");
Assert(SmartMedicalOptimizer.VerifyUnused(before,unused with{Inventory=new Dictionary<int,int>{{67,0}}},67,now.AddSeconds(5),true),"pre-click depletion cannot replan");
Assert(!SmartMedicalOptimizer.VerifyUnused(before,after,67,now.AddSeconds(5),true),"consumed item cannot be cancelled");
Assert(!SmartMedicalOptimizer.VerifyUnused(before,unused with{Life=unused.Life+1},67,now.AddSeconds(5),true),"life change cannot cancel issued operation");

Assert(!SmartMedicalOptimizer.VerifyUnused(before,unused with{ProfileId="B"},67,now.AddSeconds(5)),"cross-account unused authorization");
Assert(!SmartMedicalOptimizer.VerifyUnused(before,unused with{CooldownSeconds=1500},67,now.AddSeconds(5)),"changed cooldown treated as unused");
Assert(!SmartMedicalOptimizer.VerifyUnused(before,unused with{HospitalUntilUtc=now},67,now.AddSeconds(5)),"changed hospital treated as unused");
Assert(!SmartMedicalOptimizer.VerifyUnused(before,unused,67,now.AddMinutes(2)),"stale pending authorization reused");
Assert(!SmartMedicalOptimizer.VerifyUnused(before,unused,206,now.AddSeconds(5)),"drug unused authorization");
var levelingBefore=before with{HospitalUntilUtc=now,Life=10,MaximumLife=100};
var levelingAfter=after with{HospitalUntilUtc=now,Life=20,MaximumLife=100};
Assert(SmartMedicalOptimizer.VerifyUse(levelingBefore,levelingAfter,67,now.AddSeconds(5)),"leveling life gain verification");
Assert(!SmartMedicalOptimizer.VerifyUse(levelingBefore,levelingAfter with{Life=10},67,now.AddSeconds(5)),"unchanged life accepted after full-heal medicine");
var random=new Random(901);settings=new SmartMedicalSettings();
for(int trial=0;trial<120;trial++){
 int a=random.Next(5),b=random.Next(5),c=random.Next(5),hospital=random.Next(1,160),cd=random.Next(19000,21700);var o=Obs(hospital,cd,inventory:new(){{68,a},{67,b},{66,c}});var actual=SmartMedicalOptimizer.Plan(o,settings,now);int expected=int.MaxValue;
 for(int x=0;x<=a;x++)for(int y=0;y<=b;y++)for(int z=0;z<=c;z++){
 int cost=x*600+y*900+z*1200,last=z>0?1200:y>0?900:x>0?600:0;
 if(x*20+y*40+z*70>=hospital&&cd<21600&&cd+cost-last<21600)expected=Math.Min(expected,cost);
 }
 Assert((expected==int.MaxValue&&actual.State!="Ready")||(actual.State=="Ready"&&actual.CooldownSeconds==expected),"DP oracle mismatch "+trial);
}
Console.WriteLine("SMART MEDICAL PASSED: durations, bonuses, bounded DP oracle, six/nine-hour overshoot, reserves, supplies, ties, blood gating, stale state, verification, account isolation and drug exclusion.");
'@
[IO.File]::WriteAllText((Join-Path $project 'Program.cs'),$program)
[IO.File]::WriteAllText((Join-Path $project 'Test.csproj'),'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
dotnet run --project (Join-Path $project 'Test.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw 'Smart Medical tests failed'}
