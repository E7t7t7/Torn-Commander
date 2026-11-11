using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
namespace TornCommander;
internal sealed class CompanyTrainRecord {
 public string Id{get;set;}="";public long Employee{get;set;}public string Name{get;set;}="";
 public DateTime At{get;set;}public int Count{get;set;}=1;
}
internal sealed class CompanyTrainChoice {
 public long Employee{get;set;}public string Name{get;set;}="";public string Reason{get;set;}="";
 public int ReceivedAtPlan{get;set;}public string Role{get;set;}="";
}
internal sealed class CompanyTrainingJournal {
 public List<CompanyTrainRecord> Records{get;set;}=new();
 public List<CompanyTrainChoice> Plan{get;set;}=new();public string PlanDay{get;set;}="";
 public string Mode{get;set;}="rotation";public DateTime? LastSync{get;set;}
 public DateTime? PlanCreatedUtc{get;set;}public int? Available{get;set;}public DateTime? BudgetObservedUtc{get;set;}
 public int Unreadable{get;set;}public string Note{get;set;}="Scan staff, choose today's group, train them in Torn, then refresh the training receipts.";
}
internal static class CompanyTraining {
 public static int Total(CompanyTrainingJournal j,long id)=>j.Records.Where(x=>x.Employee==id).Sum(x=>x.Count);
 public static int Today(CompanyTrainingJournal j,long id,DateTime now)=>j.Records.Where(x=>x.Employee==id&&x.At.Date==now.Date).Sum(x=>x.Count);
 public static List<CompanyTrainChoice> Choose(CompanyTrainingJournal journal,CompanyTeamScan team,string mode,int count,DateTime now,Random random){
  if(mode is not ("rotation" or "efficient")||count is <1 or >1000||!team.EmployeesLoaded||team.Employees.Count==0)throw new InvalidDataException("Scan the employee list and choose a valid training mode/count.");
  if(journal.PlanDay==now.ToString("yyyy-MM-dd")&&journal.Plan.Count>0)
   throw new InvalidOperationException("Today's group is already saved. Use it or explicitly clear its untrained choices before choosing again.");
  var candidates=team.Employees.Where(x=>x.Id>0&&Today(journal,x.Id,now)==0).Select(e=>{
   var job=team.Jobs.FirstOrDefault(x=>x.Id==e.PositionId);
   return new{employee=e,tie=random.NextDouble(),score=Priority(e,job),total=Total(journal,e.Id),last=journal.Records.Where(r=>r.Employee==e.Id).MaxBy(r=>r.At)?.At.Date??DateTime.MinValue};
  }).ToList();
  if(mode=="efficient")candidates=candidates.Where(x=>x.score.Gain>=0).ToList();
  var ordered=mode=="rotation"?candidates.OrderBy(x=>x.total).ThenBy(x=>x.last).ThenBy(x=>x.tie):candidates.OrderByDescending(x=>x.score.Gain).ThenBy(x=>x.total).ThenBy(x=>x.tie);
  if(candidates.Count==0)throw new InvalidDataException(mode=="efficient"?"No staff have a supported role/stat calculation. Scan again or use fair rotation.":"Every current employee already has a recorded train today.");
  return ordered.Take(count).Select(x=>new CompanyTrainChoice{Employee=x.employee.Id,Name=x.employee.Name,Role=x.employee.Position,ReceivedAtPlan=x.total,
   Reason=mode=="rotation"?$"{x.total} trains recorded so far. Picked from staff with the fewest trains; people waiting longest came first and ties were shuffled.":x.score.Reason}).ToList();
 }
 private static (decimal Gain,string Reason) Priority(CompanyEmployeeScan e,CompanyJobScan? job){
  if(e.Stats==null||job?.Required==null)return(-1,"Current stats or job requirements are missing. Scan staff before using this recommendation.");
  var values=new[]{("MAN",job.Required.ManualLabor,e.Stats.ManualLabor),("INT",job.Required.Intelligence,e.Stats.Intelligence),("END",job.Required.Endurance,e.Stats.Endurance)}.Where(x=>x.Item2>0).OrderByDescending(x=>x.Item2).ToList();
  if(values.Count<2||values[0].Item2==values[1].Item2)return(-1,"The role's main and secondary training stats are unclear. No gain estimate was guessed.");
  var first=values[0];var second=values[1];
  var after=new CompanyWorkStats(e.Stats.ManualLabor+(first.Item1=="MAN"?50:second.Item1=="MAN"?25:0),e.Stats.Intelligence+(first.Item1=="INT"?50:second.Item1=="INT"?25:0),e.Stats.Endurance+(first.Item1=="END"?50:second.Item1=="END"?25:0));
  int before=CompanyStaffAdvisor.Fit(e.Stats,job.Required),next=CompanyStaffAdvisor.Fit(after,job.Required);
  decimal gap=Math.Max(0,(decimal)(first.Item2-first.Item3)/first.Item2)+Math.Max(0,(decimal)(second.Item2-second.Item3)/second.Item2);
  string StatName(string key)=>key=="MAN"?"manual labor":key=="INT"?"intelligence":"endurance";
  string reason=$"A train in {e.Position} is expected to add 50 {StatName(first.Item1)} and 25 {StatName(second.Item1)}. ";
  reason+=next>before?$"That is one of the clearest near-term improvements for this job.":"It helps the role's working stats, although one train may be too small to change the displayed job-fit score.";
  if(e.Addiction<0||e.Inactivity<0)reason+=" Training will not remove addiction or inactivity penalties.";
  return((next-before)*1000m+gap,reason+" This ranks stat growth, not a promised increase in sales.");
 }
 public static (List<CompanyTrainRecord> Records,int Unknown) ReadNews(string json,DateTime now){
  using var doc=JsonDocument.Parse(json);var rows=new List<CompanyTrainRecord>();int unknown=0;
  foreach(var n in doc.RootElement.GetProperty("news").EnumerateArray()){
   string text=n.GetProperty("text").GetString()??"";
   // Only a named target AFTER 'trained' identifies the recipient; never use the director's first profile link.
   var target=Regex.Match(text,@"\btrained\s+(?:<[^>]+>\s*)*<a\b[^>]*href\s*=\s*[""']?[^>""']*profiles\.php\?[^>""']*(?:XID|userID)=(\d+)[^>]*>(.*?)</a>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
   if(!target.Success)target=Regex.Match(text,@"<a\b[^>]*href\s*=\s*[""']?[^>""']*profiles\.php\?[^>""']*(?:XID|userID)=(\d+)[^>]*>(.*?)</a>\s*(?:</?[^>]+>\s*)*(?:has\s+been|was|is\s+being)\s+trained\b",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
   if(!target.Success||!long.TryParse(target.Groups[1].Value,out var id)||id<=0){unknown++;continue;}
   string plain=WebUtility.HtmlDecode(Regex.Replace(text,"<[^>]+>"," "));
   if(Regex.IsMatch(plain,@"\b(?:[2-9]|[1-9]\d+)\s+(?:times|trains)\b",RegexOptions.IgnoreCase)){unknown++;continue;}
   var at=DateTimeOffset.FromUnixTimeSeconds(n.GetProperty("timestamp").GetInt64()).UtcDateTime;
   if(at>now.AddMinutes(2)){unknown++;continue;}
   string source=n.GetProperty("id").GetString()??"";if(source.Length is <1 or >180){unknown++;continue;}
   rows.Add(new(){Id="news:"+source,Employee=id,Name=WebUtility.HtmlDecode(Regex.Replace(target.Groups[2].Value,"<[^>]+>","")).Trim(),At=at});
  }return(rows,unknown);
 }
 public static int Merge(CompanyTrainingJournal journal,IEnumerable<CompanyTrainRecord> rows){
  int added=0;var ids=journal.Records.Select(x=>x.Id).ToHashSet();
  foreach(var row in rows)if(ids.Add(row.Id)){journal.Records.Add(row);added++;}return added;
 }
}
