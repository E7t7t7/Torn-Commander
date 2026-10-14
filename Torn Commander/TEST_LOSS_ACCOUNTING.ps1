$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderLossAccounting'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('LossAccounting.cs','AtomicJsonFile.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$models=Get-Content (Join-Path $PSScriptRoot 'Models.cs') -Raw
$loss=[regex]::Match($models,'(?s)public sealed class LossOrder\s*\{.*?(?=\r?\npublic sealed class TornSnapshot)').Value
if(!$loss){throw 'Production LossOrder model not found.'}
Set-Content -LiteralPath (Join-Path $project 'LossOrder.cs') -Value ("namespace TornCommander;"+[Environment]::NewLine+$loss) -Encoding UTF8
$prog=@'
using System.Text.Json;
using TornCommander;
void Check(bool value,string message){if(!value)throw new Exception(message);}
var path=Path.Combine(args[0],"orders.json");
var order=new LossOrder{Id="test",RequestedLosses=2,ProfileAId="one",ProfileBId="two",PayPerLoss=500000};
int saves=0;
void Save(){saves++;AtomicJsonFile.Save(path,order);}
var first=LossAccounting.Commit(order,"claim1","one","A",Save);
Check(first.Ok && order.CompletedA==1 && order.Gross==500000 && saves==1,"First commit failed.");
var replay=LossAccounting.Commit(order,"claim1","one","A",Save);
Check(replay.Ok && replay.Duplicate && order.CompletedTotal==1 && saves==1,"Retry counted twice.");
Check(LossAccounting.Replay(order,"claim1","two","B")?.Ok==false,"Receipt crossed profiles.");
var reloaded=JsonSerializer.Deserialize<LossOrder>(File.ReadAllText(path))!;
Check(LossAccounting.Replay(reloaded,"claim1","one","A")?.Duplicate==true,"Receipt lost across restart.");
var second=LossAccounting.Commit(order,"claim2","two","B",Save);
Check(second.Ok && order.CompletedB==1 && order.CompletedTotal==2 && order.Status=="Complete" && order.NextA=="Done" && order.NextB=="Done","Second worker accounting wrong.");
Check(!LossAccounting.Commit(order,"extra","one","A",Save).Ok && order.CompletedTotal==2,"Quota over-counted.");
Check(LossAccounting.Commit(order,"claim2","two","B",Save).Duplicate,"Final loss retry failed.");
var prior=File.ReadAllText(path);
order=new LossOrder{RequestedLosses=2,ProfileAId="one"};
using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)){
 try{LossAccounting.Commit(order,"failed","one","A",Save);throw new Exception("Expected failed save.");}
 catch(Exception ex) when (ex is IOException or UnauthorizedAccessException){}
}
Check(order.CompletedTotal==0 && order.CommitReceipts.Count==0 && order.Status=="Active","Failed save changed count.");
Check(File.ReadAllText(path)==prior,"Failed replacement damaged persisted order.");
Check(!Directory.EnumerateFiles(args[0],"*.tmp").Any(),"Temporary file leaked.");
foreach(var slot in new[]{"A","B"}){
 var solo=new LossOrder{RequestedLosses=3,ProfileAId="solo",ProfileBId="solo"};
 for(int i=0;i<3;i++) Check(LossAccounting.Commit(solo,"solo"+slot+i,"solo",slot,()=>{}).Ok,"Solo loss rejected.");
 Check(solo.CompletedTotal==3 && solo.Remaining==0 && (slot=="A"?solo.CompletedA:solo.CompletedB)==3,"Solo total split or lost.");
}
var legacy=JsonSerializer.Deserialize<LossOrder>("""{"RequestedLosses":4,"CompletedA":2}""")!;
Check(legacy.CommitReceipts.Count==0 && legacy.CompletedA==2,"Old order migration changed count.");
var delivered=new LossOrder{RequestedLosses=14,CompletedB=14,NextA="Attack",NextB="Heal",Status="Ready to finish"};
Check(LossAccounting.NormalizeCompletion(delivered)&&delivered.Status=="Complete"&&delivered.NextA=="Done"&&delivered.NextB=="Done"&&delivered.CompletedTotal==14,"Legacy fulfilled order not reconciled.");
Check(!LossAccounting.NormalizeCompletion(delivered),"Completion reconciliation not idempotent.");
var partial=new LossOrder{RequestedLosses=14,CompletedB=13};
Check(!LossAccounting.NormalizeCompletion(partial)&&partial.Status=="Active"&&partial.CompletedTotal==13,"Partial order marked complete.");
var finalRollback=new LossOrder{RequestedLosses=1,ProfileAId="one"};
try{LossAccounting.Commit(finalRollback,"final-fail","one","A",()=>throw new IOException("fixture"));throw new Exception("Expected completion save failure.");}catch(IOException){}
Check(finalRollback.Status=="Active"&&finalRollback.CompletedTotal==0&&finalRollback.NextA=="Attack"&&finalRollback.CommitReceipts.Count==0,"Failed final commit left an order fulfilled.");
Console.WriteLine("LOSS ACCOUNTING PASSED: receipt replay, profiles, restart, quota, failed-save rollback, atomic preservation and legacy orders.");
'@
Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $prog -Encoding UTF8
Set-Content -LiteralPath (Join-Path $project 'LossTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'LossTest.csproj') -c Release -- $project
if($LASTEXITCODE -ne 0){throw 'Loss accounting regression failed.'}
