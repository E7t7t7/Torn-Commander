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
 public int Count{get;set;}=1;
 public int ReceivedAtPlan{get;set;}public DateTime? ChosenUtc{get;set;}public string Role{get;set;}="";
}
internal sealed class CompanyTrainingJournal {
 public List<CompanyTrainRecord> Records{get;set;}=new();
 public List<CompanyTrainChoice> Plan{get;set;}=new();public string PlanDay{get;set;}="";
 public int Requested{get;set;}
 public string Mode{get;set;}="rotation";public DateTime? LastSync{get;set;}
 public DateTime? PlanCreatedUtc{get;set;}public int? Available{get;set;}public DateTime? BudgetObservedUtc{get;set;}
 public int Unreadable{get;set;}public string Note{get;set;}="Scan staff, choose today's group, train them in Torn, then refresh the training receipts.";
}
internal static class CompanyTraining {
 public static int Total(CompanyTrainingJournal j,long id)=>j.Records.Where(x=>x.Employee==id).Sum(x=>x.Count);
 public static int Today(CompanyTrainingJournal j,long id,DateTime now)=>j.Records.Where(x=>x.Employee==id&&x.At.Date==now.Date).Sum(x=>x.Count);
 public static List<CompanyTrainChoice> Choose(CompanyTrainingJournal journal,CompanyTeamScan team,string mode,int count,DateTime now,Random random,bool append=false){
  if(mode is not ("rotation" or "efficient")||count is <1 or >1000||!team.EmployeesLoaded||team.Employees.Count==0)throw new InvalidDataException("Scan the employee list and choose a valid training mode/count.");
  if(append&&(journal.PlanDay!=now.ToString("yyyy-MM-dd")||journal.Plan.Count==0))throw new InvalidOperationException("Choose today’s group before adding people.");
  if(!append&&journal.PlanDay==now.ToString("yyyy-MM-dd")&&journal.Plan.Count>0)
   throw new InvalidOperationException("Today's group is already saved. Use it or explicitly clear its untrained choices before choosing again.");
  var staff=team.Employees.Where(x=>x.Id>0).DistinctBy(x=>x.Id).ToList();
  var assigned=staff.ToDictionary(x=>x.Id,_=>0);
  int Pending(long id)=>append?journal.Plan.Where(p=>p.Employee==id).Sum(p=>Remaining(journal,p)):0;
  int Existing(long id)=>journal.Records.Where(r=>r.Employee==id&&r.At>team.ScannedUtc&&r.At<=now).Sum(r=>r.Count);
  var totals=staff.ToDictionary(e=>e.Id,e=>Total(journal,e.Id));
  var pendingCounts=staff.ToDictionary(e=>e.Id,e=>Pending(e.Id));
  var existingCounts=staff.ToDictionary(e=>e.Id,e=>Existing(e.Id));
  var lastDates=staff.ToDictionary(e=>e.Id,e=>journal.Records.Where(r=>r.Employee==e.Id).MaxBy(r=>r.At)?.At.Date??DateTime.MinValue);
  for(int n=0;n<count;n++){
   var candidates=staff.Select(e=>new{employee=e,tie=random.NextDouble(),
    score=Priority(e,team.Jobs.FirstOrDefault(x=>x.Id==e.PositionId),existingCounts[e.Id]+pendingCounts[e.Id]+assigned[e.Id]),
    total=totals[e.Id]+pendingCounts[e.Id]+assigned[e.Id],
    last=assigned[e.Id]>0?now.Date:lastDates[e.Id]}).ToList();
   var rotation=candidates.OrderBy(x=>x.total).ThenBy(x=>x.last).ThenBy(x=>x.tie);
   var selected=mode=="rotation"?rotation.First():
    candidates.Any(x=>x.score.Gain>=0)?candidates.Where(x=>x.score.Gain>=0).OrderByDescending(x=>x.score.Gain).ThenBy(x=>x.total).ThenBy(x=>x.tie).First():rotation.First();
   assigned[selected.employee.Id]++;
  }
  return staff.Where(e=>assigned[e.Id]>0).Select(e=>{
   int countForPerson=assigned[e.Id],pending=Pending(e.Id),extra=Existing(e.Id);
   var job=team.Jobs.FirstOrDefault(x=>x.Id==e.PositionId);var score=Priority(e,job,extra+pending);
   string reason=mode=="rotation"?
    $"Give {countForPerson} train{(countForPerson==1?"":"s")} to {e.Name}. Extra trains are shared across staff with the fewest recorded or already-planned trains; ties are shuffled.":
    score.Gain<0?$"No reliable current-job growth estimate was available. Give {countForPerson} train{(countForPerson==1?"":"s")} by fair rotation; no gain was guessed.":
    $"Give {countForPerson} train{(countForPerson==1?"":"s")} to {e.Name}. "+score.Reason+
    $" This batch adds an estimated {countForPerson*50} to the main job stat and {countForPerson*25} to its secondary stat. The benefit is recalculated after every proposed train.";
   return new CompanyTrainChoice{Employee=e.Id,Name=e.Name,Role=e.Position,Count=countForPerson,ReceivedAtPlan=Total(journal,e.Id),ChosenUtc=now,Reason=reason};
  }).OrderByDescending(x=>x.Count).ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToList();
 }
 public static int Completed(CompanyTrainingJournal j,CompanyTrainChoice choice){
  var since=choice.ChosenUtc??j.PlanCreatedUtc;if(!since.HasValue)return 0;
  int recent=j.Records.Where(r=>r.Employee==choice.Employee&&r.At>=since.Value.AddSeconds(-2)&&r.At.ToString("yyyy-MM-dd")==j.PlanDay).Sum(r=>r.Count);
  return Math.Clamp(Math.Min(recent,Math.Max(0,Total(j,choice.Employee)-choice.ReceivedAtPlan)),0,choice.Count);
 }
 public static int Remaining(CompanyTrainingJournal j,CompanyTrainChoice choice)=>Math.Max(0,choice.Count-Completed(j,choice));
 public static bool Done(CompanyTrainingJournal j,CompanyTrainChoice choice)=>Remaining(j,choice)==0;
 public static void AddChoices(CompanyTrainingJournal j,IEnumerable<CompanyTrainChoice> choices){
  foreach(var choice in choices){var old=j.Plan.FirstOrDefault(x=>x.Employee==choice.Employee);
   if(old==null)j.Plan.Add(choice);else{old.Count+=choice.Count;old.Reason+=" Additional allocation: "+choice.Reason;}
  }
 }
 public static int FreeChoices(CompanyTrainingJournal j,int available,DateTime now){
  int pending=j.PlanDay==now.ToString("yyyy-MM-dd")?j.Plan.Sum(x=>Remaining(j,x)):0;
  return Math.Max(0,available-pending);
 }
 public static string Summary(CompanyTrainingJournal j,int wanted,int added,bool append){
  int total=j.Plan.Sum(x=>x.Count),people=j.Plan.Select(x=>x.Employee).Distinct().Count();
  string result=(append?$"Added {added} more trains.":$"Allocated {added} of {wanted} requested trains.")+
   $" The saved plan has {total} trains across {people} employees; someone can receive more than one.";
  if(j.Plan.Any(x=>x.Reason.StartsWith("No reliable current-job growth estimate")))result+=" Unknown-stat staff use fair rotation; no growth estimate was guessed.";
  return result+" Follow each employee's train quantity in Torn, then refresh receipts. Recommendations are modeled working-stat growth, not promised sales.";
 }
 private static (decimal Gain,string Reason) Priority(CompanyEmployeeScan e,CompanyJobScan? job,int extra=0){
  if(e.Stats==null||job?.Required==null)return(-1,"Current stats or job requirements are missing.");
  var values=new[]{("MAN",job.Required.ManualLabor,e.Stats.ManualLabor),("INT",job.Required.Intelligence,e.Stats.Intelligence),("END",job.Required.Endurance,e.Stats.Endurance)}.Where(x=>x.Item2>0).OrderByDescending(x=>x.Item2).ToList();
  if(values.Count<2||values[0].Item2==values[1].Item2)return(-1,"The role's main and secondary training stats are unclear.");
  var first=values[0];var second=values[1];
  static double Part(long actual,long required){if(actual<=0)return 0;double ratio=(double)actual/required;return Math.Min(45,45*ratio)+Math.Max(0,5*Math.Log2(ratio));}
  long main=first.Item3+50L*extra,secondary=second.Item3+25L*extra;
  double gain=Part(main+50,first.Item2)-Part(main,first.Item2)+Part(secondary+25,second.Item2)-Part(secondary,second.Item2);
  string Name(string key)=>key=="MAN"?"manual labor":key=="INT"?"intelligence":"endurance";
  string reason=$"In {e.Position}, each train is modeled as 50 {Name(first.Item1)} and 25 {Name(second.Item1)}. ";
  reason+="These working stats help their current job. Other employees may gain less once their requirements are already covered.";
  if(e.Addiction<0||e.Inactivity<0)reason+=" Training will not fix addiction or inactivity penalties.";
  return((decimal)Math.Round(gain,10),reason);
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
   int trainCount=1;
   var counted=Regex.Match(plain,@"(?<![\d,])\b(\d+)\s+(?:times|trains)\b",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
   if(counted.Success&&(!int.TryParse(counted.Groups[1].Value,out trainCount)||trainCount is <1 or >1000)){unknown++;continue;}
   var at=DateTimeOffset.FromUnixTimeSeconds(n.GetProperty("timestamp").GetInt64()).UtcDateTime;
   if(at>now.AddMinutes(2)){unknown++;continue;}
   string source=n.GetProperty("id").GetString()??"";if(source.Length is <1 or >180){unknown++;continue;}
   rows.Add(new(){Id="news:"+source,Count=trainCount,Employee=id,Name=WebUtility.HtmlDecode(Regex.Replace(target.Groups[2].Value,"<[^>]+>","")).Trim(),At=at});
  }return(rows,unknown);
 }
 public static int Merge(CompanyTrainingJournal journal,IEnumerable<CompanyTrainRecord> rows){
  int added=0;var ids=journal.Records.Select(x=>x.Id).ToHashSet();
  foreach(var row in rows)if(ids.Add(row.Id)){journal.Records.Add(row);added++;}return added;
 }
}
