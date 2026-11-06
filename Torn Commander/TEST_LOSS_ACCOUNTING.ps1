$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderLossAccounting'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('LossAccounting.cs','LossExchangeImport.cs','LossSettlement.cs','AtomicJsonFile.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
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
var apiOrder=new LossOrder{RequestedLosses=3,ProfileAId="one"};
Check(LossAccounting.Commit(apiOrder,"api-first","one","A",()=>{},12345).Ok,"verified receipt not saved");
Check(apiOrder.CommitReceipts[0].AttackId==12345,"official attack ID lost");
Check(!LossAccounting.Commit(apiOrder,"api-second","one","A",()=>{},12345).Ok&&apiOrder.CompletedTotal==1,"official attack counted twice");
Check(LossAccounting.Commit(apiOrder,"api-first","one","A",()=>{},12345).Duplicate,"same claim receipt replay failed");
var partialSale=new LossOrder{RequestedLosses=20,CompletedA=3,CompletedB=2,PayPerLoss=300000,ProfileAId="one",ProfileBId="two"};
LossSettlement.Save(partialSale,true,false,1500000,2,()=>{});
Check(partialSale.IsClosed&&partialSale.Status=="Closed partial"&&partialSale.CompletedTotal==5&&partialSale.RequestedLosses==20&&partialSale.Remaining==15,"partial settlement fabricated deliveries");
Check(!LossAccounting.NormalizeCompletion(partialSale)&&partialSale.Status=="Closed partial","partial closure normalized away");
Check(!LossAccounting.Commit(partialSale,"late","one","A",()=>{}).Ok&&partialSale.CompletedTotal==5,"closed order accepted late fight");
LossSettlement.Save(partialSale,false,false,1400000,3,()=>{});
LossSettlement.Save(partialSale,false,false,1400000,3,()=>{});
Check(partialSale.CashReceived==1400000&&partialSale.XanaxReceived==3,"repeated payout save double counted");
var restoredSale=JsonSerializer.Deserialize<LossOrder>(JsonSerializer.Serialize(partialSale))!;
Check(restoredSale.IsClosed&&restoredSale.CompletedTotal==5&&restoredSale.XanaxReceived==3,"settlement lost after restart");
var cancelledSale=new LossOrder{RequestedLosses=20,CompletedA=2};
LossSettlement.Save(cancelledSale,true,true,null,null,()=>{});
Check(cancelledSale.Status=="Cancelled"&&cancelledSale.CompletedTotal==2&&cancelledSale.CashReceived==null,"cancel assumed payout or deleted deliveries");
var legacySale=JsonSerializer.Deserialize<LossOrder>("{\"RequestedLosses\":20,\"CompletedA\":4,\"Status\":\"Active\"}")!;
Check(legacySale.CashReceived==null&&legacySale.XanaxReceived==null&&!legacySale.IsClosed,"legacy payments invented");
var totals=LossSettlement.Totals(new[]{partialSale,cancelledSale,legacySale});
Check(totals.Delivered==11&&totals.Cash==1400000&&totals.Xanax==3&&totals.Unrecorded==2&&totals.Active==1,"sales totals combined quote with payout or lost cancelled work");
LossSettlement.Save(cancelledSale,false,false,0,null,()=>{});
Check(cancelledSale.CashReceived==0&&cancelledSale.XanaxReceived==0&&LossSettlement.HasHistory(cancelledSale),"explicit zero payment mistaken for unknown");
var failureSale=new LossOrder{RequestedLosses=20,CompletedA=5};
try{LossSettlement.Save(failureSale,true,false,123,4,()=>throw new IOException("fixture"));throw new Exception("expected failed settlement");}catch(IOException){}
Check(!failureSale.IsClosed&&failureSale.CashReceived==null&&failureSale.XanaxReceived==null&&failureSale.Status=="Active"&&failureSale.CompletedTotal==5,"failed settlement changed history");
try{LossSettlement.Save(failureSale,false,false,-1,0,()=>{});throw new Exception("negative payout accepted");}catch(InvalidDataException){}
try{LossSettlement.Save(failureSale,false,false,null,null,()=>{});throw new Exception("blank payout accepted as paid");}catch(InvalidDataException){}
var fullSale=new LossOrder{RequestedLosses=5,CompletedA=5,Status="Complete"};
LossSettlement.Save(fullSale,false,false,1000000,0,()=>{});
Check(fullSale.Status=="Complete"&&fullSale.CashReceived==1000000,"completed order cannot record payment");
using(var listingDoc=JsonDocument.Parse("{\"actionType\":\"loss\",\"targetId\":123,\"buyer\":\"Fixture\",\"remaining\":20,\"pay\":300000}")){
 var listing=LossExchangeListing.Parse(listingDoc.RootElement);
 var solo=listing.Create(5,"wife","");var dual=listing.Create(20,"main","wife");
 Check(solo.ProfileAId=="wife"&&solo.ProfileBId==""&&solo.RequestedLosses==5&&solo.PayPerLoss==300000,"solo import incorrect");
 Check(dual.ProfileBId=="wife"&&dual.RequestedLosses==20&&dual.SourceService=="Loss X-change","dual import incorrect");
 Check(listing.Existing(new[]{dual})==dual,"repeat claim duplicates active order");
 LossSettlement.Save(dual,true,true,null,null,()=>{});
 Check(listing.Existing(new[]{dual})==null,"closed order blocks a new listing");
 try{listing.Create(21,"main","wife");throw new Exception("overclaim");}catch(InvalidDataException){}
 try{listing.Create(5,"main","main");throw new Exception("same account");}catch(InvalidDataException){}
}
using(var escapeDoc=JsonDocument.Parse("{\"actionType\":\"escape\"}")){
 try{LossExchangeListing.Parse(escapeDoc.RootElement);throw new Exception("escape imported as loss");}catch(InvalidDataException){}
}
Check(LossSettlement.Totals(new[]{partialSale}).QuotedCash==1500000,"quoted cash lost or treated as actual payment");
Console.WriteLine("LOSS X-CHANGE IMPORT PASSED: validated listing, solo/dual, duplicate claim, quota and escape exclusion.");
Console.WriteLine("LOSS SETTLEMENT PASSED: 5/20 partial closure, cancellation, cash/Xanax totals, corrections, zero/unknown payments, late claims, migration, restart and save rollback.");
Console.WriteLine("LOSS ACCOUNTING PASSED: receipt replay, profiles, restart, quota, failed-save rollback, atomic preservation and legacy orders.");
'@
Set-Content -LiteralPath (Join-Path $project 'Program.cs') -Value $prog -Encoding UTF8
Set-Content -LiteralPath (Join-Path $project 'LossTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'LossTest.csproj') -c Release -- $project
if($LASTEXITCODE -ne 0){throw 'Loss accounting regression failed.'}
