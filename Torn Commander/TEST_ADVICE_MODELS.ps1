# Synthetic model tests; no credentials, browser sessions or real user data.
$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderAdviceTest'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('WarPayouts.cs','StockIntelligence.cs','FactionActivity.cs','CompanyMarket.cs','CompanyIntelligence.cs','CompanyStaffAdvisor.cs','CompanyStaffLayouts.cs','CompanyReportExport.cs','FactionIntelligence.cs','TravelArrivalAdvisor.cs','DashboardJson.cs')){
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project
}
$prog=@'
using System.Text.Json;
using TornCommander;
void Check(bool value,string message){if(!value)throw new Exception(message);}
var warFixture=new WarPayoutJournal {Budget=100,AttackPercent=100,Members=new(){new(1,"A",1,1),new(2,"B",1,1),new(3,"C",1,1)}};
var paid=WarPayouts.Calculate(warFixture);
Check(paid.Sum(x=>x.Amount)==100&&paid.Single(x=>x.Id==1).Amount==34,"Payout rounding must conserve budget deterministically.");
warFixture.Cap=20;
Check(WarPayouts.Calculate(warFixture).Sum(x=>x.Amount)==60,"Capped unused budget must remain unallocated.");
warFixture.Cap=null;warFixture.MinimumAttacks=2;
Check(WarPayouts.Calculate(warFixture).All(x=>x.Amount==0&&!x.Eligible),"Minimum attacks ignored.");
var payoutRandom=new Random(83);
for(int trial=0;trial<100;trial++){
 var plan=new WarPayoutJournal {Budget=payoutRandom.Next(1,100000),AttackPercent=payoutRandom.Next(101),
    Cap=trial%2==0?payoutRandom.Next(0,10000):null,
    Members=Enumerable.Range(1,10).Select(i=>new WarParticipant(i,"Fixture "+i,payoutRandom.Next(1,100),payoutRandom.Next(1,100))).ToList()};
 var result=WarPayouts.Calculate(plan);
 long expected=Math.Min(plan.Budget,plan.Cap.HasValue?plan.Cap.Value*10:plan.Budget);
 Check(result.Sum(x=>x.Amount)==expected&&result.All(x=>x.Amount>=0&&(!plan.Cap.HasValue||x.Amount<=plan.Cap)),"Random payout conservation/cap invariant failed.");
}
Check(ReportCsv.Cell(" =SUM(A1)")=="\"' =SUM(A1)\""&&ReportCsv.Cell(-20)=="\"-20\"","CSV formula safety / numeric preservation failed.");
try{WarPayouts.Read("""{"rankedwarreport":{"id":1,"end":0,"factions":[{"id":99,"members":[]}]}}""",1,2);throw new Exception("Foreign faction report accepted.");}catch(InvalidDataException){}
var quoteFixture="""{"stocks":[{"id":1,"name":"Fixture Stock","acronym":"FIX","market":{"price":100},"bonus":{"requirement":1000,"description":"Fixture benefit","passive":false}}]}""";
var stockQuotes=StockIntelligence.ReadQuotes(quoteFixture);
var held=StockIntelligence.ReadHoldings("""{"stocks":[{"id":1,"shares":10,"transactions":[{"shares":10,"price":90}],"bonus":{"increment":0,"available":false}}]}""");
Check(StockIntelligence.NetProfit(held[0],100)==99m,"Stock fee / purchase basis wrong.");
var unmatched=StockIntelligence.ReadHoldings("""{"stocks":[{"id":1,"shares":10,"transactions":[{"shares":9,"price":90}],"bonus":{"increment":0,"available":false}}]}""");
Check(unmatched[0].Cost==null,"Incomplete cost basis fabricated.");
var stockJournal=new StockJournal {Quotes=stockQuotes,Holdings=held};
var sampleTime=new DateTime(2026,10,15,12,0,0,DateTimeKind.Utc);
StockIntelligence.Record(stockJournal,sampleTime);
StockIntelligence.Record(stockJournal,sampleTime.AddMinutes(10));
Check(stockJournal.History.Count==1,"Stock hourly dedup failed.");
stockJournal.Targets[1]=new StockTarget{Buy=100,Payout=10,Days=7};
Check(JsonSerializer.Serialize(StockIntelligence.Report(stockJournal)).Contains("BUY TARGET REACHED"),"Stock target threshold wrong.");
var activityJournal=new FactionJournal();
var activitySnapshot=new FactionSnapshot {Id=1,Count=2,MembersLoaded=true,Members=new(){new(){Id=1,Name="Online fixture",Activity="Online"},new(){Id=2,Name="Idle fixture",Activity="Idle"}}};
activityJournal.Snapshot=activitySnapshot;
FactionActivity.Record(activityJournal,activitySnapshot,sampleTime);
FactionActivity.Record(activityJournal,activitySnapshot,sampleTime.AddMinutes(1));
Check(activityJournal.Activity.Count==2&&activityJournal.Activity.Sum(x=>x.Online+x.Idle)==2,"Activity dedup failed.");
activitySnapshot.Members[0].Activity="Offline";
FactionActivity.Record(activityJournal,activitySnapshot,sampleTime.AddMinutes(5));
Check(activityJournal.Activity.Single(x=>x.MemberId==1).Offline==1,"Activity counter failed.");
activityJournal.Snapshot=new FactionSnapshot{Id=2};
using(var activityDoc=JsonDocument.Parse(JsonSerializer.Serialize(FactionActivity.Report(activityJournal,sampleTime.AddMinutes(10)))))
 Check(activityDoc.RootElement.GetProperty("members").GetArrayLength()==0,"Activity leaked between factions.");
var marketOwn = new CompanyObservation {CompanyId=1,CompanyTypeId=8,Rating=1,WeeklyIncome=200};
var marketScan = new CompanyMarketScan {CompanyId=1,TypeId=8,ScannedUtc=DateTime.UtcNow,Companies=new(){new(1,1,200),new(2,2,100),new(3,2,200),new(4,2,300)}};
var marketJson=JsonSerializer.Serialize(CompanyMarket.Report(marketOwn,marketScan));
using(var m=JsonDocument.Parse(marketJson)){
 var next=m.RootElement.GetProperty("rows")[2];
 Check(next.GetProperty("count").GetInt32()==3 && next.GetProperty("beatenPercent").GetDouble()==33.3,"Peer percentage must exclude own company and ties.");
}
marketOwn.CompanyId=99;
Check(JsonSerializer.Serialize(CompanyMarket.Report(marketOwn,marketScan)).Contains("false"),"Market data leaked to another company.");
Check(CompanyMarket.ReadPage("""{"companies":[{"id":2,"type":{"id":8},"rating":2,"income":{"weekly":0}}]}""",8)[0].WeeklyIncome==0,"Listing zero lost.");
try { CompanyMarket.ReadPage("""{"companies":[{"id":2,"type":{"id":9},"rating":2,"income":{"weekly":0}}]}""",8); throw new Exception("Wrong type accepted."); } catch(InvalidDataException){}
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
var loss = new CompanyObservation { CompanyId=1, FromApi=true, DayUtc="2026-10-13", DailyIncome=146325, DailyCustomers=428, DailyAdvertising=400000, Employees=8 };
var report = CompanyIntelligence.BusinessReport(new[]{loss});
Check(report.State=="SPENDING EXCEEDS INCOME" && report.Balance==-253675 && !report.CompleteCosts && report.BreakEvenCustomers==1170,"Observed loss / partial-cost business report wrong.");
Check(CompanyIntelligence.Analyze(new[]{loss}).State=="SPENDING ALERT" && CompanyIntelligence.Analyze(new[]{loss}).SuggestedBudget==0,"Baseline must not endorse loss-making advertising.");
using(var wire=JsonDocument.Parse(DashboardJson.Serialize(report))){
 Check(wire.RootElement.GetProperty("balance").GetInt64()==-253675 && wire.RootElement.GetProperty("state").GetString()=="SPENDING EXCEEDS INCOME","Business data cannot reach dashboard field names.");
}
var prior=new CompanyObservation { CompanyId=1, DayUtc="2026-10-12", DailyIncome=100000, Employees=7 };
Check(CompanyIntelligence.BusinessReport(new[]{prior,loss}).IncomeChange==46325 && CompanyIntelligence.BusinessReport(new[]{prior,loss}).Note.Contains("different"),"Income trend lost staffing context.");
Check(CompanyIntelligence.BusinessReport(new[]{missing}).RevenuePerCustomer is null,"Unknown business revenue fabricated.");
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
// Company API fixtures follow the official v2 schema, including missing access fields.
var profile=CompanyIntelligence.ReadProfile("""{"profile":{"id":7,"name":"Fixture","type":{"id":8,"name":"Candle Shop"},"employees":{"hired":2,"capacity":8},"efficiency":71,"environment":83,"popularity":42,"trains":3,"funds":10000}}""");
Check(profile.CompanyTypeId==8 && profile.Capacity==8 && profile.Efficiency==71 && profile.Trains==3 && profile.Funds==10000,"Extended company health fields lost.");
var employees=CompanyStaffAdvisor.ReadEmployees("""{"employees":[{"id":11,"name":"Low MAN","position":{"id":1,"name":"Manual"},"stats":{"manual_labor":10,"intelligence":80,"endurance":0},"effectiveness":{"working_stats":40,"total":44,"addiction":-3,"inactivity":-2,"settled_in":9},"wage":250},{"id":12,"name":"Low INT","position":{"id":2,"name":"Office"},"stats":{"manual_labor":80,"intelligence":10,"endurance":0}}]}""");
Check(employees.Count==2 && employees[0].Addiction==-3 && employees[1].Wage==null && employees[1].TotalEffectiveness==null,"Employee modifiers or unknown values lost.");
var jobs=CompanyStaffAdvisor.ReadJobs("""{"companies":[{"id":99,"positions":[]},{"id":8,"positions":[{"id":1,"name":"Manual","ability":"Cleaner","working_stats":{"required":{"manual_labor":100,"intelligence":0,"endurance":50}}},{"id":2,"name":"Office","ability":"Manager","working_stats":{"required":{"manual_labor":0,"intelligence":100,"endurance":50}}}]}]}""",8);
Check(jobs.Count==2 && jobs[0].Ability=="Cleaner","Company-type jobs not selected correctly.");
var scan=new CompanyTeamScan {CompanyId=7,EmployeesLoaded=true,Employees=employees,Jobs=jobs,StockLoaded=true,
 Stock=CompanyStaffAdvisor.ReadStock("""{"stock":[{"name":"Candle","cost":10,"price":9,"in_stock":0,"on_order":0}]}""")};
scan.PlannedCounts=new(){{1,1},{2,1}};scan.PlanVersion=2;
var team=CompanyStaffAdvisor.Report(profile,scan);
Check(team.State=="CUSTOM COUNTS — COMPARISON ONLY" && team.After>team.Before && team.Rows[0].Recommended=="Office" && team.Rows[1].Recommended=="Manual","Underqualified staff must be globally swapped to better roles.");
Check(team.Rows.All(x=>x.NextStep.Contains("gaps")) && team.Rows[0].NextStep.Contains("Addiction") &&
 team.Actions.Any(x=>x.Contains("no stock")) && team.Actions.Any(x=>x.Contains("below unit cost")),"Training gaps, staff penalties and stock actions missing.");
Check(CompanyStaffAdvisor.Fit(new(100,50,0),new(100,50,0))==90 &&
 CompanyStaffAdvisor.Fit(new(200,100,0),new(100,50,0))==100 &&
 CompanyStaffAdvisor.Fit(new(0,0,0),new(100,50,0))==0,"Effectiveness formula boundaries wrong.");
using(var wire=JsonDocument.Parse(DashboardJson.Serialize(team))){
 Check(wire.RootElement.GetProperty("rows")[0].GetProperty("stats").GetProperty("manualLabor").GetInt64()==10,"Staff data casing cannot reach dashboard.");
}
using(var counts=JsonDocument.Parse("""{"counts":[{"positionId":1,"count":0},{"positionId":2,"count":2}]}"""))
 scan.PlannedCounts=CompanyStaffAdvisor.ReadCounts(counts.RootElement,scan);
var scenario=CompanyStaffAdvisor.Report(profile,scan);
Check(scenario.Rows.All(x=>x.Recommended=="Office"),"Chosen role counts were ignored.");
foreach(var bad in new[]{"{\"counts\":[{\"positionId\":1,\"count\":1},{\"positionId\":2,\"count\":2}]}",
 "{\"counts\":[{\"positionId\":1,\"count\":-1},{\"positionId\":2,\"count\":3}]}",
 "{\"counts\":[{\"positionId\":1,\"count\":1},{\"positionId\":1,\"count\":1}]}"}){
 using var req=JsonDocument.Parse(bad);
 try{CompanyStaffAdvisor.ReadCounts(req.RootElement,scan);throw new Exception("Invalid staffing counts accepted.");}catch(InvalidDataException){}
}
scan.PlannedCounts=null;
employees[0].Stats=null;
var incomplete=CompanyStaffAdvisor.Report(profile,scan);
Check(incomplete.State=="INCOMPLETE SCAN" && incomplete.Before==null && incomplete.Rows[0].Stats==null && incomplete.Rows.All(x=>x.After==null),"Missing stats must withhold team allocation, not become zero.");
Check(CompanyStaffAdvisor.Report(new CompanyObservation{CompanyId=999},scan).Rows.Count==0,"Previous company's staff leaked into new company.");
employees[0].Stats=new(10,80,0);
profile.Employees=3;
Check(CompanyStaffAdvisor.Report(profile,scan).State=="INCOMPLETE SCAN","Partial staff list must not produce a whole-company allocation.");
profile.Employees=2;
var basic=CompanyStaffAdvisor.ReadEmployees("""{"employees":[{"id":1,"name":"Basic","position":{"id":1,"name":"Role"}}]}""");
Check(basic[0].Stats==null && basic[0].Wage==null,"Basic API response fabricated private employee detail.");
try{CompanyStaffAdvisor.ReadEmployees("""{"employees":[{"id":1},{"id":1}]}""");throw new Exception("Duplicate employee accepted.");}catch(InvalidDataException){}
// Compare optimizer to exhaustive permutations, including ties and zero stats.
var random=new Random(731);
for(int trial=0;trial<25;trial++){
 var roles=Enumerable.Range(1,5).Select(i=>new CompanyJobScan{Id=i,Required=new(random.Next(1,200),random.Next(1,200),0)}).ToList();
 var workers=Enumerable.Range(1,5).Select(i=>new CompanyEmployeeScan{Id=i,PositionId=i,Stats=new(random.Next(0,250),random.Next(0,250),0)}).ToList();
 var chosen=CompanyStaffAdvisor.Allocate(workers,roles);
 long Cost(int worker,int role)=>-CompanyStaffAdvisor.Fit(workers[worker].Stats!,roles[role].Required!)*1000L+(workers[worker].PositionId==roles[role].Id?0:1);
 long Search(int row,int mask){if(row==5)return 0;return Enumerable.Range(0,5).Where(j=>(mask&(1<<j))==0).Min(j=>Cost(row,j)+Search(row+1,mask|(1<<j)));}
 Check(chosen.Distinct().Count()==5 && Enumerable.Range(0,5).Sum(i=>Cost(i,chosen[i]))==Search(0,0),"Global allocation or minimum-movement tie break differs from exhaustive optimum.");
}
// Protected allocation: compare the complete priority tuple to every permutation.
for(int trial=0;trial<15;trial++){
 var roles=Enumerable.Range(1,4).Select(i=>new CompanyJobScan{Id=i,Required=new(random.Next(1,150),random.Next(1,150),0)}).ToList();
 var workers=Enumerable.Range(1,4).Select(i=>new CompanyEmployeeScan{Id=i,PositionId=i,Stats=new(random.Next(0,200),random.Next(0,200),0)}).ToList();
 var keys=new HashSet<long>{1,2};
 var selected=CompanyStaffAdvisor.AllocateProtected(workers,roles,keys);
 (int,int,int,int) Rank(int[] choice){
  var keyFits=Enumerable.Range(0,4).Where(i=>keys.Contains(roles[choice[i]].Id)).Select(i=>CompanyStaffAdvisor.Fit(workers[i].Stats!,roles[choice[i]].Required!)).ToList();
  return (Math.Min(90,keyFits.Min()),keyFits.Sum(x=>Math.Min(90,x)),Enumerable.Range(0,4).Sum(i=>CompanyStaffAdvisor.Fit(workers[i].Stats!,roles[choice[i]].Required!)),-Enumerable.Range(0,4).Count(i=>workers[i].PositionId!=roles[choice[i]].Id));
 }
 var best=(int.MinValue,int.MinValue,int.MinValue,int.MinValue);
 void Search(int row,int mask,int[] choices){
  if(row==4){var rank=Rank(choices);if(rank.CompareTo(best)>0)best=rank;return;}
  for(int j=0;j<4;j++)if((mask&(1<<j))==0){choices[row]=j;Search(row+1,mask|(1<<j),choices);}
 }
 Search(0,0,new int[4]);
 Check(Rank(selected)==best,"Protected key-job allocation differs from exhaustive priority optimum.");
}
// One-star nine-person team, including extremely low-stat roster fillers.
var candleJobs=new List<CompanyJobScan>{
 new(){Id=101,Name="Chandler",Ability="None",Required=new(4500,2250,0)},
 new(){Id=102,Name="Salesperson",Ability="None",Required=new(0,750,1500)},
 new(){Id=103,Name="Cleaner",Ability="Cleaner",Required=new(1000,0,500)},
 new(){Id=104,Name="Quality Controller",Ability="Manager",Required=new(0,1500,3000)},
 new(){Id=105,Name="Trainer",Ability="Trainer",Required=new(0,4500,2250)},
 new(){Id=106,Name="Bookkeeper",Ability="Secretary",Required=new(0,1250,2500)}
};
var candleCompany=new CompanyObservation{CompanyId=17,CompanyTypeId=8,Rating=1,Employees=9,Environment=80,DailyIncome=150000,DailyAdvertising=10000};
CompanyEmployeeScan Worker(long id,string name,long role,long man,long intel,long end)=>new(){Id=id,Name=name,PositionId=role,Position=candleJobs.Single(j=>j.Id==role).Name,Stats=new(man,intel,end)};
var candleScan=new CompanyTeamScan{CompanyId=17,EmployeesLoaded=true,Jobs=candleJobs,Employees=new(){
 Worker(1,"Manual specialist",101,3900,1400,2400),Worker(2,"Low-stat filler",101,6,121,143),
 Worker(3,"New player",105,129,268,389),Worker(4,"Quality fit",104,502,1403,2249),
 Worker(5,"Seller",102,1567,1006,1051),Worker(6,"Seller 2",102,297,868,949),
 Worker(7,"Starter",102,241,589,697),Worker(8,"Manual seller",103,2277,565,1489),
 Worker(9,"Cleaner fit",103,1274,555,816)}};
var layouts=CompanyStaffLayouts.Build(candleCompany,candleScan);
Check(layouts.Stage.Contains("1-star")&&layouts.Stage.Contains("9 scanned")&&layouts.Options.Count==1,"Corrected starter draft missing.");
Check(layouts.Options.All(x=>!x.Suggested),"Unvalidated role counts were endorsed as recommended.");
var layout=layouts.Options.Single();
Check(layout.Counts.Values.Sum()==9&&layout.Counts[101]==1&&layout.Counts[102]==6&&layout.Counts[103]==1&&layout.Counts[104]==1&&layout.Counts[105]==0,"Unsupported cleaners, near-even Chandler split or Trainer returned.");
Check(layouts.Checks.Any(x=>x.Contains("has 2 Cleaners")),"Existing excess cleaning was not called out.");
Check(CompanyStaffAdvisor.Report(candleCompany,candleScan).State=="CURRENT SETUP — REVIEW ONLY","Scanning automatically endorsed a new assignment.");
candleScan.PlannedCounts=layout.Counts;candleScan.PlanPolicy=layout.Policy;candleScan.PlanVersion=2;
var named=CompanyStaffAdvisor.Report(candleCompany,candleScan);
Check(named.State=="STARTER DRAFT — REVIEW BEFORE APPLYING"&&named.Rows.Count==9&&named.After==layout.WorkingScore,"Starter preview differs from named placement.");
Check(named.Rows.Single(x=>x.Id==1).Recommended=="Chandler"&&named.Rows.Single(x=>x.Id==4).Recommended=="Quality Controller","Key jobs were sacrificed to maximize easier-job totals.");
Check(named.Rows.Single(x=>x.Id==2).Recommended=="Salesperson"&&named.Rows.Single(x=>x.Id==3).Recommended=="Salesperson","Filler/new players were dumped into demanding roles.");
Check(named.Rows.Single(x=>x.Id==9).Recommended=="Cleaner","Adequate cleaning coverage wasted a better sales fit.");
Check(named.Rows.Single(x=>x.Id==2).Explanation!.Summary.Contains("included"),"Low-stat inclusion was not explained plainly.");
foreach(var role in candleJobs)Check(named.Rows.Count(x=>x.Recommended==role.Name)==layout.Counts[role.Id],"Named placements violated starter counts.");
candleScan.PlanVersion=0;candleScan.PlanSource="Cash and staff growth";
Check(CompanyStaffAdvisor.Report(candleCompany,candleScan).State=="CURRENT SETUP — REVIEW ONLY"&&CompanyStaffLayouts.Build(candleCompany,candleScan).Selected.Contains("Old planner"),"Old suggestion remained endorsed.");
candleScan.PlannedCounts=null;
for(int size=4;size<=10;size++){
 candleCompany.Employees=size;candleScan.Employees=Enumerable.Range(1,size).Select(i=>Worker(i,"Filler "+i,102,0,0,0)).ToList();
 var sized=CompanyStaffLayouts.Build(candleCompany,candleScan);
 Check(sized.Options.Count==1&&sized.Options[0].Counts.Values.Sum()==size&&sized.Options[0].Underqualified==size,"Zero-stat fillers excluded.");
 candleScan.PlannedCounts=sized.Options[0].Counts;candleScan.PlanPolicy=sized.Options[0].Policy;candleScan.PlanVersion=2;
 Check(CompanyStaffAdvisor.Report(candleCompany,candleScan).Rows.Count==size,"Low-stat team lost employees.");
}
candleCompany.Employees=10;candleScan.Employees[0].Stats=null;
Check(CompanyStaffLayouts.Build(candleCompany,candleScan).Options.Count==0,"Missing stats treated as zero.");
candleScan.Employees[0].Stats=new(0,0,0);candleCompany.CompanyId=99;
Check(CompanyStaffLayouts.Build(candleCompany,candleScan).Options.Count==0,"Draft mixed companies.");
candleCompany.CompanyId=17;candleCompany.CompanyTypeId=7;
Check(CompanyStaffLayouts.Build(candleCompany,candleScan).Options.Count==0,"Candle assumptions applied to another industry.");
var valueFixture=CompanyStaffAdvisor.ReadEmployees("""{"employees":[{"id":44,"value":-20,"wage":0},{"id":45,"value":null}]}""");
Check(valueFixture[0].ContributionValue==-20&&valueFixture[0].Wage==0&&valueFixture[1].ContributionValue==null,"Contribution loss/unknown or zero wage lost.");
// Explanations must disclose personal sacrifices and prove the stay-put alternative.
var reasonScan=new CompanyTeamScan{CompanyId=55,EmployeesLoaded=true,
 Jobs=new(){new(){Id=1,Name="Manual",Required=new(100,0,0)},new(){Id=2,Name="Office",Required=new(0,100,0)}},
 Employees=new(){new(){Id=1,Name="Flexible",Position="Manual",PositionId=1,Stats=new(100,80,0),WorkingEffectiveness=45},
 new(){Id=2,Name="Specialist",Position="Office",PositionId=2,Stats=new(90,0,0),WorkingEffectiveness=98}}};
var reasonCompany=new CompanyObservation{CompanyId=55,Employees=2};
reasonScan.PlannedCounts=new(){{1,1},{2,1}};reasonScan.PlanVersion=2;
var reasonPlan=CompanyStaffAdvisor.Report(reasonCompany,reasonScan);
Check(reasonPlan.Before==45&&reasonPlan.After==76,"Reason fixture scores changed.");
var detail=reasonPlan.Rows[0].Explanation!;
Check(detail.Summary.Contains("loses 9")&&detail.Reasons.Any(x=>x.Contains("falls to 45 (31 below")),"Explanation hid individual loss or failed constrained-stay comparison.");
Check(detail.StatComparison.Any(x=>x.Contains("80% covered"))&&detail.Reasons.Any(x=>x.Contains("Best individual fit")),"Stat ratios or alternative role missing.");
Check(reasonPlan.Rows[1].Explanation!.Reliability.Contains("VERIFY BEFORE MOVING"),"Major Torn score mismatch was hidden.");
reasonScan.PlannedCounts=new(){{1,0},{2,2}};
Check(CompanyStaffAdvisor.Report(reasonCompany,reasonScan).Rows[0].Explanation!.Reasons.Any(x=>x.Contains("zero slots")),"Role excluded by selected counts was not explained.");
reasonScan.Employees[0].Stats=null;
Check(CompanyStaffAdvisor.Report(reasonCompany,reasonScan).Rows[0].Explanation!.Summary.Contains("No complete"),"Incomplete scan invented an explanation for a recommendation.");
Console.WriteLine("COMPANY REASONING TEST PASSED: personal sacrifice, exact constrained alternative, stat coverage, excluded-role reason and score discrepancy warning.");
// Official v2 faction fixtures and permission-aware role prioritization.
var faction=FactionIntelligence.ReadBasic("""{"basic":{"id":71,"name":"Fixture Faction","leader_id":10,"co_leader_id":20,"members":3,"capacity":8,"respect":1200,"rank":{"name":"Silver"},"is_enlisted":null}}""");
FactionIntelligence.ReadViewer(faction,"""{"profile":{"id":10}}""","""{"faction":{"id":71,"position":"Custom Captain"}}""");
Check(FactionIntelligence.Role(faction)=="Leader"&&faction.Enlisted==null,"Leader identity or unknown enlistment lost.");
FactionIntelligence.ReadViewer(faction,"""{"profile":{"id":20}}""","""{"faction":{"id":71,"position":"Second"}}""");
Check(FactionIntelligence.Role(faction)=="Co-leader","Co-leader identity not detected.");
FactionIntelligence.ReadViewer(faction,"""{"profile":{"id":30}}""","""{"faction":{"id":71,"position":"Captain"}}""");
Check(FactionIntelligence.Role(faction)=="Member","A custom rank name granted guessed officer status.");
FactionIntelligence.ReadPositions(faction,"""{"positions":[{"name":"Captain","abilities":["Medical Usage"]}]}""");
Check(FactionIntelligence.Role(faction)=="Member","Ordinary armory access promoted a member to officer.");
FactionIntelligence.ReadPositions(faction,"""{"positions":[{"name":"Captain","abilities":["War Management","Organised Crimes"]}]}""");
Check(FactionIntelligence.Role(faction)=="Officer","Verified position management permissions not recognized.");
try{FactionIntelligence.ReadViewer(faction,"""{"profile":{"id":30}}""","""{"faction":{"id":99,"position":"Leader"}}""");throw new Exception("Mismatched faction accepted.");}catch(InvalidDataException){}
Check(FactionIntelligence.AffiliationId("""{"faction":null}""")==null,"No-faction membership not detected.");
try{FactionIntelligence.AffiliationId("{}");throw new Exception("Missing faction field treated as verified departure.");}catch(InvalidDataException){}
long epoch=new DateTimeOffset(now).ToUnixTimeSeconds();
var memberJson="{\"members\":[{\"id\":30,\"name\":\"Officer\",\"position\":\"Captain\",\"level\":10,\"is_in_oc\":true,\"last_action\":{\"timestamp\":"+epoch+",\"status\":\"Online\"},\"status\":{\"state\":\"Okay\"}},{\"id\":40,\"name\":\"Idle\",\"position\":\"Member\",\"last_action\":{\"timestamp\":"+(epoch-5*86400)+",\"status\":\"Offline\"},\"status\":{\"state\":\"Okay\"},\"is_in_oc\":false},{\"id\":50,\"name\":\"Unknown\",\"position\":\"Recruit\",\"status\":{\"state\":\"Hospital\"}}]}";
FactionIntelligence.ReadMembers(faction,memberJson,now);
Check(faction.Members.Count==3&&faction.Members[2].InCrime==null&&FactionIntelligence.IdleDays(faction.Members[2],now)==null,"Unknown member fields fabricated.");
Check(FactionIntelligence.IdleDays(new FactionMemberScan{LastAction=epoch+3600},now)==null,"Future activity treated as reliable.");
FactionIntelligence.ReadChain(faction,"""{"chain":{"current":15,"max":100,"timeout":90}}""",now);
Check(faction.ChainDeadline==epoch+90,"Chain deadline not anchored to scan time.");
FactionIntelligence.ReadWars(faction,"""{"wars":{"ranked":{"war_id":12,"start":0,"end":null,"factions":[{"id":71,"name":"Home","score":100},{"id":72,"name":"Away","score":90}]},"raids":[],"territory":[]}}""");
Check(faction.WarsLoaded&&faction.Wars.Single().Sides[1].Score==90,"Ranked war payload not parsed.");
faction.Crimes=FactionIntelligence.ReadCrimes("""{"crimes":[{"id":1,"name":"Recruiting Crime","status":"Recruiting","difficulty":1,"slots":[{"position":"Driver","user":null,"checkpoint_pass_rate":70,"item_requirement":null}]},{"id":2,"name":"Planning Crime","status":"Planning","ready_at":1,"slots":[{"position":"Hacker","user":{"id":30,"progress":80},"checkpoint_pass_rate":52,"item_requirement":{"id":67,"is_available":false}}]}]}""");
faction.CrimesLoaded=true;
Check(faction.Crimes[0].Slots[0].UserId==null&&faction.Crimes[0].Slots[0].PassRate==70&&faction.Crimes[1].Slots[0].UserId==30,"Open and assigned CPR contexts mixed.");
FactionIntelligence.ReadUpgrades(faction,"""{"upgrades":{"core":{"upgrades":[{"name":"Chaining","ability":"Chain bonus","level":1}]},"peace":[],"war":[]}}""");
Check(faction.UpgradesLoaded&&faction.Upgrades.Single().Level==1,"Installed upgrade levels missing.");
var factionJournal=new FactionJournal{Snapshot=faction,InactivityDays=3,Goal="crimes"};
faction.ScannedUtc=now;
FactionIntelligence.SaveDay(factionJournal,faction);FactionIntelligence.SaveDay(factionJournal,faction);
Check(factionJournal.Days.Count==1,"Repeated faction scan duplicated daily sample.");
var leadership=FactionIntelligence.Advice(factionJournal,now);
Check(leadership.Any(x=>x.Contains("1 members exceed"))&&leadership.Any(x=>x.Contains("missing item"))&&leadership.Any(x=>x.Contains("requesting account"))&&leadership.Any(x=>x.Contains("timestamp alone")),"Leadership actions missed inactivity, CPR ownership or crime blockers.");
Check(FactionIntelligence.PersonalAdvice(factionJournal,now).Any(x=>x.Contains("Planning Crime")),"Personal assigned crime missing.");
Check(FactionIntelligence.Advice(factionJournal,now.AddMinutes(16)).Any(x=>x.Contains("15 minutes old")),"Stale scan not flagged.");
var restricted=new FactionJournal{Snapshot=new FactionSnapshot{Id=71}};
Check(FactionIntelligence.Advice(restricted,now).Any(x=>x.Contains("details are unavailable")),"Missing permission represented as no crimes.");
using(var wire=JsonDocument.Parse(DashboardJson.Serialize(faction))){
 Check(wire.RootElement.GetProperty("viewerPosition").GetString()=="Captain","Faction role casing not compatible with dashboard.");
}
Console.WriteLine("FACTION TEST PASSED: leader/co-leader identity, verified officer abilities, no title guessing, membership isolation, null fields, scan deadlines, CPR ownership, crime blockers, installed upgrades and daily samples.");
// Export only the active company's allowlisted data; never notes, errors or other profiles.
candleCompany.CompanyTypeId=8;
var exportJournal=new CompanyJournal {ActiveCompanyId=17,Status="PRIVATE_STATUS_SECRET",Team=candleScan,Days=new(){
 new(){CompanyId=17,CompanyName="Fixture Candle",CompanyTypeId=8,DayUtc="2026-10-14",Employees=10,Notes="PRIVATE_NOTE_SECRET",DailyIncome=0},
 new(){CompanyId=999,CompanyName="OTHER_COMPANY_SECRET",DayUtc="2026-10-13",Notes="OTHER_NOTE_SECRET"}
}};
candleScan.Warnings.Add("PRIVATE_ERROR_SECRET");
var exported=CompanyReportExport.Build(exportJournal,now.AddDays(2));
Check(exported.Contains("Fixture Candle")&&!exported.Contains("PRIVATE_")&&!exported.Contains("OTHER_"),"Report leaked free-text/private or another company's data.");
using(var parsed=JsonDocument.Parse(exported[(exported.IndexOf("JSON DATA:")+10)..])){
 Check(parsed.RootElement.GetProperty("accounting").GetProperty("dailyIncome").GetInt64()==0 &&
 parsed.RootElement.GetProperty("accounting").GetProperty("dailyPayroll").ValueKind==JsonValueKind.Null,"Export lost real zero or unknown costs.");
 Check(parsed.RootElement.GetProperty("recentDailyObservations").GetArrayLength()==1,"Export mixed company histories.");
}
candleScan.CompanyId=999;
var isolated=CompanyReportExport.Build(exportJournal,now);
using(var parsed=JsonDocument.Parse(isolated[(isolated.IndexOf("JSON DATA:")+10)..])){
 Check(parsed.RootElement.GetProperty("scan").ValueKind==JsonValueKind.Null&&parsed.RootElement.GetProperty("staffPlan").GetProperty("rows").GetArrayLength()==0,"Mismatched company staff leaked into clipboard report.");
}
try{CompanyReportExport.Build(new CompanyJournal(),now);throw new Exception("Empty export accepted.");}catch(InvalidDataException){}
Console.WriteLine("COMPANY EXPORT TEST PASSED: clipboard allowlist, unknown/zero values, active-company isolation and no unscanned reports.");
Console.WriteLine("COMPANY LAYOUT TEST PASSED: new-company/filler regression, protected key jobs, zero-stat inclusion at 4-10 staff, no automatic excess support, legacy invalidation and review-only scans.");
Console.WriteLine("COMPANY STAFF TEST PASSED: official API fixtures, unknown access fields, underqualified swaps, role-count scenarios, 25 exhaustive optimizer comparisons, company isolation, penalties and stock guidance.");
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
