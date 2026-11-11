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
   day=now.ToString("yyyy-MM-dd"),available=j.Available,budgetAt=j.BudgetObservedUtc,
   plan=j.Plan.Select(x=>new{x.Employee,x.Name,x.Role,x.Reason,done=j.PlanCreatedUtc.HasValue&&j.Records.Any(r=>r.Employee==x.Employee&&r.At>=j.PlanCreatedUtc.Value.AddSeconds(-2))}).ToList(),
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
   }else if(action=="trainingPlan"){
    var team=company.Team;if(team==null||team.CompanyId!=id||DateTime.UtcNow-team.ScannedUtc>TimeSpan.FromHours(24))throw new InvalidDataException("Scan Company & Staff first; the staff list is missing or over a day old.");
    if(j.LastSync==null||DateTime.UtcNow-j.LastSync.Value>TimeSpan.FromMinutes(5))throw new InvalidOperationException("Refresh training receipts first so today’s existing trains are included in the rotation.");
    int count=r.GetProperty("count").GetInt32();string mode=ReadWorkerString(r,"mode");
    var key=CredentialManager.LoadApiKey(profile)??throw new InvalidOperationException("Save this account's Torn API key first.");
    var fresh=CompanyIntelligence.ReadProfile(await _api.FetchEndpointAsync("company/profile",key,fresh:true));
    if(profile!=CurrentProfileId)return;if(fresh.CompanyId!=id)throw new InvalidDataException("Your company changed. Scan again.");
    if(fresh.Trains is not { } available)throw new InvalidDataException("Available trains are not exposed for this account. Director access is needed.");
    if(count>available)throw new InvalidDataException("Only "+available+" trains were reported available. Reduce today's group.");
    j.Plan=CompanyTraining.Choose(j,team,mode,count,DateTime.UtcNow,Random.Shared);j.PlanDay=DateTime.UtcNow.ToString("yyyy-MM-dd");j.PlanCreatedUtc=DateTime.UtcNow;j.Mode=mode;j.Available=available;j.BudgetObservedUtc=DateTime.UtcNow;
    j.Note=j.Plan.Count==0?"Everyone in the current staff list already has a recorded train today.":"Today's group is saved. Find each person's Train button in Torn, click it yourself, then refresh receipts here.";
   }else if(action=="trainingClear"){
    if(MessageBox.Show(this,"Clear today's untrained choices? Training receipts and totals will be kept.","Choose a different group",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
    j.Plan.Clear();j.PlanDay="";j.Note="Choices cleared. Received trains remain in the history and rotation.";
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
