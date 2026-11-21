using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm{
 private bool _trainingBusy;
 private CompanyTrainingJournal TrainingFor(long company,string? profile=null)=>IntelStore.Load(profile??CurrentProfileId,"company-training:"+company,new CompanyTrainingJournal());
 private object TrainingDashboard(CompanyJournal company){
  var j=TrainingFor(company.ActiveCompanyId);var team=company.Team?.CompanyId==company.ActiveCompanyId?company.Team:null;var now=DateTime.UtcNow;
  var people=(team?.Employees.Select(e=>new{e.Id,e.Name,e.Position})??[]).ToList();
  foreach(var former in j.Records.GroupBy(x=>x.Employee).Where(g=>people.All(p=>p.Id!=g.Key)))people.Add(new{Id=former.Key,Name=former.OrderByDescending(x=>x.At).First().Name,Position="Former employee / not in latest scan"});
  return new{profileId=CurrentProfileId,companyId=company.ActiveCompanyId,busy=_trainingBusy,j.Note,j.LastSync,j.Unreadable,j.PlanDay,j.Mode,
   day=now.ToString("yyyy-MM-dd"),available=j.Available,budgetAt=j.BudgetObservedUtc,requested=j.Requested>0?j.Requested:(int?)null,unassigned=j.Available.HasValue?CompanyTraining.FreeChoices(j,j.Available.Value,now):(int?)null,
   plan=j.Plan.Select(x=>new{x.Employee,x.Name,x.Role,x.Reason,count=x.Count,completed=CompanyTraining.Completed(j,x),remaining=CompanyTraining.Remaining(j,x),done=CompanyTraining.Done(j,x)}).ToList(),
   rows=people.Select(e=>new{e.Id,e.Name,e.Position,total=CompanyTraining.Total(j,e.Id),today=CompanyTraining.Today(j,e.Id,now),
    week=j.Records.Where(x=>x.Employee==e.Id&&x.At>=now.AddDays(-7)).Sum(x=>x.Count),last=j.Records.Where(x=>x.Employee==e.Id).MaxBy(x=>x.At)?.At}).OrderByDescending(x=>x.total).ToList(),
   events=j.Records.OrderByDescending(x=>x.At).Take(100).ToList()};
 }
 private async Task HandleTraining(string action,JsonElement r){
  var profile=ReadWorkerString(r,"profileId");var company=GetCompanyJournal();long id=r.GetProperty("companyId").GetInt64();
  if(profile!=CurrentProfileId||id<=0||id!=company.ActiveCompanyId)throw new InvalidOperationException("Scan the selected account's company first.");
  if(_trainingBusy)return;_trainingBusy=true;UpdateModernDashboard();
  try{
   var j=TrainingFor(id);
   if(action=="trainingOpen"){await AddBrowserTabAsync("https://www.torn.com/companies.php?step=your&type=3#/option=employees");return;}
   if(action=="trainingFocus"){
    if(j.PlanDay!=DateTime.UtcNow.ToString("yyyy-MM-dd"))throw new InvalidOperationException("Choose today’s group first; the saved group belongs to another day.");
    var employee=r.GetProperty("employee").GetInt64();if(!j.Plan.Any(x=>x.Employee==employee))throw new InvalidDataException("Choose a saved employee in today's group.");
    var tab=Active();var url=tab?.WebView.Source;
    if(url==null||url.Scheme!="https"||!TornRoutes.IsTornHost(url.Host)||url.AbsolutePath!="/companies.php")throw new InvalidOperationException("Open your company Employees page first.");
    string code=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"BundledScripts","Torn_Company_Training.js"));
    var result=await tab!.ExecuteScriptAsync(code.Replace("__EMPLOYEE_ID__",employee.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    if(profile!=CurrentProfileId||tab!=Active())return;
    using var data=JsonDocument.Parse(result??"null");j.Note=data.RootElement.GetProperty("note").GetString()??"Open the employee's Train button manually.";
   }else if(action is "trainingPlan" or "trainingAdd"){
    var team=company.Team;if(team==null||team.CompanyId!=id||DateTime.UtcNow-team.ScannedUtc>TimeSpan.FromHours(24))throw new InvalidDataException("Scan Company & Staff first; the staff list is missing or over a day old.");
    int count=r.TryGetProperty("count",out var requestedCount)?requestedCount.GetInt32():1;bool all=r.TryGetProperty("useAll",out var useAll)&&useAll.ValueKind==JsonValueKind.True;string mode=ReadWorkerString(r,"mode");
    var key=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this account's Torn API key first.");
    var fresh=CompanyIntelligence.ReadProfile(await _api.FetchEndpointAsync("company/profile",key,fresh:true));
    if(profile!=CurrentProfileId)return;if(fresh.CompanyId!=id)throw new InvalidDataException("Your company changed. Scan again.");
    if(fresh.Trains is not { } available)throw new InvalidDataException("Available trains are not exposed for this account. Director access is needed.");
    if(j.LastSync==null||DateTime.UtcNow-j.LastSync.Value>TimeSpan.FromMinutes(5)){
     string news=await _api.FetchEndpointAsync("company/news?cat=training&limit=100&sort=desc",key,fresh:true);
     var check=CompanyIntelligence.ReadProfile(await _api.FetchEndpointAsync("company/profile",key,fresh:true));
     if(profile!=CurrentProfileId)return;if(check.CompanyId!=id)throw new InvalidDataException("Company changed while checking trains. Scan again.");
     var receipts=CompanyTraining.ReadNews(news,DateTime.UtcNow);CompanyTraining.Merge(j,receipts.Records);j.LastSync=DateTime.UtcNow;j.Unreadable=receipts.Unknown;
     available=check.Trains??throw new InvalidDataException("Available trains could not be verified.");
    }

    bool append=action=="trainingAdd";int free=append?CompanyTraining.FreeChoices(j,available,DateTime.UtcNow):available;
    if(all)count=free;
    if(count<1)throw new InvalidDataException("No unassigned trains remain. Refresh receipts after training your pending choices.");
    if(count>free)throw new InvalidDataException("Only "+free+" trains are unassigned. Existing untrained choices already reserve the rest; refresh receipts after training them.");
    var chosen=CompanyTraining.Choose(j,team,mode,count,DateTime.UtcNow,Random.Shared,append);
    j.Requested=append?Math.Max(j.Requested,j.Plan.Sum(x=>x.Count)+count):count;
    if(append)CompanyTraining.AddChoices(j,chosen);else{j.Plan=chosen;j.PlanDay=DateTime.UtcNow.ToString("yyyy-MM-dd");j.PlanCreatedUtc=DateTime.UtcNow;}
    j.Mode=mode;j.Available=available;j.BudgetObservedUtc=DateTime.UtcNow;
    j.Note=CompanyTraining.Summary(j,count,chosen.Sum(x=>x.Count),append);
   }else if(action=="trainingClear"){
    if(MessageBox.Show(this,"Clear today's untrained choices? Training receipts and totals will be kept.","Choose a different group",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
    foreach(var choice in j.Plan)choice.Count=CompanyTraining.Completed(j,choice);
    j.Plan=j.Plan.Where(x=>x.Count>0).ToList();if(j.Plan.Count==0)j.PlanDay="";j.Requested=j.Plan.Sum(x=>x.Count);j.Note="Untrained choices cleared. Confirmed people remain in the group and history.";
   }else if(action=="trainingSync"){
    var key=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this account's Torn API key first.");
    var fresh=CompanyIntelligence.ReadProfile(await _api.FetchEndpointAsync("company/profile",key,fresh:true));
    if(profile!=CurrentProfileId)return;if(fresh.CompanyId!=id)throw new InvalidDataException("Company identity changed; no training records imported.");
    string json=await _api.FetchEndpointAsync("company/news?cat=training&limit=100&sort=desc",key,fresh:true);
    var confirmed=CompanyIntelligence.ReadProfile(await _api.FetchEndpointAsync("company/profile",key,fresh:true));
    if(profile!=CurrentProfileId)return;if(confirmed.CompanyId!=id)throw new InvalidDataException("Company changed while importing; records were not mixed.");
    var rows=CompanyTraining.ReadNews(json,DateTime.UtcNow);int added=CompanyTraining.Merge(j,rows.Records);
    j.LastSync=DateTime.UtcNow;j.Unreadable=rows.Unknown;j.Available=confirmed.Trains;j.BudgetObservedUtc=DateTime.UtcNow;
    j.Note="Imported "+added+" new training receipts. Latest 100 training news entries checked; older history is retained once imported. "+rows.Unknown+" entries could not be identified safely.";
   }
   IntelStore.Save(profile,"company-training:"+id,j);
  }catch(Exception ex){var j=TrainingFor(id,profile);j.Note=ex is InvalidDataException or InvalidOperationException?ex.Message:"Training action failed or API access is unavailable. Saved history was preserved; no one was trained by Commander.";IntelStore.Save(profile,"company-training:"+id,j);}
  finally{_trainingBusy=false;UpdateModernDashboard();}
 }
}
