namespace TornCommander;

public sealed class CrimeLoopOptions {
 public string Crime {get;set;}="burglary";
 public string Goal {get;set;}="skill";
 public string Game {get;set;}="Cornhole";
 public int Confidence {get;set;}=70;
 public int Interest {get;set;}=60;
 public int Suspicion {get;set;}=50;
 public int Reserve {get;set;}
 public int NerveBudget {get;set;}=100;
 public int ActionLimit {get;set;}=30;
 public void Validate(){
  if(Crime is not ("burglary" or "hustling")||Goal is not ("skill" or "money")||
   Game is not ("Cornhole" or "Snail Racing" or "Find the Lady" or "Shell Game")||
   Confidence is <0 or >100||Interest is <0 or >100||Suspicion is <0 or >100||
   Reserve is <0 or >10000||NerveBudget is <1 or >2000||ActionLimit is <1 or >200)
   throw new InvalidDataException("Check the crime, thresholds, nerve budget and action limit.");
 }
}
public sealed class CrimeChoice {
 public string Key{get;set;}="";public string Action{get;set;}="";public string Target{get;set;}="";
 public int Cost{get;set;}public int? Confidence{get;set;}public int? Interest{get;set;}public int? Suspicion{get;set;}
 public bool Dangerous{get;set;}
}
public sealed class CrimeReading {
 public bool Ok{get;set;}public string Note{get;set;}="";public int Owner{get;set;}public string Crime{get;set;}="";
 public List<CrimeChoice> Choices{get;set;}=new();
}
public sealed class CrimeReceipt {
 public bool Done{get;set;}public bool Ok{get;set;}public bool Critical{get;set;}
 public string Note{get;set;}="";public string Token{get;set;}="";public string Result{get;set;}="";
 public int? Nerve{get;set;}public double? Skill{get;set;}public long Cash{get;set;}
 public string Signature{get;set;}="";
}
public sealed class CrimeObservation {
 public DateTime At{get;set;}=DateTime.UtcNow;public string Crime{get;set;}="";public string Action{get;set;}="";
 public string Target{get;set;}="";public int Nerve{get;set;}public long Cash{get;set;}public double? SkillGain{get;set;}
 public string Receipt{get;set;}="";
}
public sealed class CrimeLoopJournal {
 public CrimeLoopOptions Options{get;set;}=new();public string Note{get;set;}="Open a supported crime, scan, then start a bounded session.";
 public List<CrimeObservation> History{get;set;}=new();public string Pending{get;set;}="";
 public CrimeChoice? PendingChoice{get;set;}public int Player{get;set;}public int SessionActions{get;set;}public int SessionNerve{get;set;}
 public int AvailableNerve{get;set;}public double? LastSkill{get;set;}public string State{get;set;}="idle";
}
public static class CrimeLoopPlanner {
 public static CrimeChoice? Next(CrimeReading board,CrimeLoopOptions options,IEnumerable<CrimeObservation> history,int spent,int done,int nerve) {
  options.Validate();
  if(!board.Ok||board.Crime!=options.Crime||done>=options.ActionLimit)return null;
  var choices=board.Choices.Where(c=>!c.Dangerous&&c.Cost>0&&
   c.Cost<=options.NerveBudget-spent&&c.Cost<=nerve-options.Reserve).ToList();
  if(options.Crime=="burglary"){
   choices=choices.Where(c=>c.Action=="burglarize"?c.Confidence>=options.Confidence:
    c.Action=="case"?c.Confidence.HasValue&&c.Confidence<options.Confidence:c.Action=="scout").ToList();
   if(choices.Any(c=>c.Action=="burglarize"))choices=choices.Where(c=>c.Action=="burglarize").ToList();
   else if(choices.Any(c=>c.Action=="case"))choices=choices.Where(c=>c.Action=="case").ToList();
  }else{
   bool gameVisible=choices.Any(c=>c.Action is "win" or "demo");
   choices=choices.Where(c=>(c.Action=="gather"&&!gameVisible)||
    c.Target.Equals(options.Game,StringComparison.OrdinalIgnoreCase)&&
    c.Interest.HasValue&&c.Suspicion.HasValue&&c.Suspicion<options.Suspicion&&
    (c.Action=="win"?c.Interest>=options.Interest:c.Action=="demo"&&c.Interest<options.Interest)).ToList();
   if(choices.Any(c=>c.Action!="gather"))choices=choices.Where(c=>c.Action!="gather").ToList();
  }
  double Score(CrimeChoice c){
   var rows=history.Where(x=>x.Crime==options.Crime&&x.Action==c.Action&&x.Target==c.Target).TakeLast(50).ToList();
   // A sample is evidence about this local history, not a universal optimum.
   if(options.Goal=="money"&&rows.Count>=3)return rows.Sum(x=>(double)x.Cash)/Math.Max(1,rows.Sum(x=>x.Nerve));
   var skills=rows.Where(x=>x.SkillGain.HasValue&&x.SkillGain>=0).ToList();
   if(options.Goal=="skill"&&skills.Count>=3)return skills.Sum(x=>x.SkillGain!.Value)/Math.Max(1,skills.Sum(x=>x.Nerve));
   return -1; // no invented rewards or XP
  }
  return choices.OrderByDescending(Score).ThenByDescending(c=>c.Confidence??0).ThenBy(c=>c.Cost).ThenBy(c=>c.Key,StringComparer.Ordinal).FirstOrDefault();
 }
 public static void Record(CrimeLoopJournal j,CrimeReceipt receipt){
  if(!receipt.Done||!receipt.Ok||receipt.Token!=j.Pending||j.PendingChoice==null)throw new InvalidDataException("Crime result does not match the pending action.");
  if(!j.History.Any(x=>x.Receipt==receipt.Token)){
   var c=j.PendingChoice;double? gain=j.LastSkill.HasValue&&receipt.Skill.HasValue?receipt.Skill-j.LastSkill:null;
   j.History.Add(new(){Crime=j.Options.Crime,Action=c.Action,Target=c.Target,Nerve=c.Cost,Cash=receipt.Cash,SkillGain=gain,Receipt=receipt.Token});
   j.SessionActions++;j.SessionNerve+=c.Cost;if(receipt.Nerve.HasValue)j.AvailableNerve=receipt.Nerve.Value;else j.AvailableNerve=Math.Max(0,j.AvailableNerve-c.Cost);
   j.LastSkill=receipt.Skill;
  }
  j.Pending="";j.PendingChoice=null;j.History=j.History.TakeLast(10000).ToList();
 }
}
