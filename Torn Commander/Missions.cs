using System.Text.RegularExpressions;
namespace TornCommander;
public sealed class MissionCard {
 public string Id{get;set;}="";public string Title{get;set;}="";public string Objective{get;set;}="";public string Text{get;set;}="";
 public string ItemName{get;set;}="";public int ItemId{get;set;}
 public string Signature{get;set;}="";public int[] Targets{get;set;}=[];
 public bool Active{get;set;}
 public bool CanAccept{get;set;}public bool CanComplete{get;set;}public bool Visible{get;set;}
 public string Key=>Id+"|"+Title+"|"+string.Join(",",Targets.Order())+"|"+ItemId+"|"+ItemName;
}
public sealed class MissionBoard {
 public bool Ok{get;set;}public int Owner{get;set;}public string Note{get;set;}="";public List<MissionCard> Cards{get;set;}=new();
}
public sealed class MissionPlan {
 public bool Supported{get;set;}public string Action{get;set;}="leave";public string Reason{get;set;}="";public int[] Steps{get;set;}=[];
 public bool AttackOnly{get;set;}public string Kind{get;set;}="manual";public int EstimatedEnergy=>Steps.Length*25;
}
public sealed record MissionAttack(long Id,string Contract,int Target,string Result,DateTime At);
public sealed record PendingMissionFight(int Target,DateTime Opened,string Claim,string Contract,int Index,string Action,bool AttackOnly);
public sealed class MissionJournal {
 public Dictionary<string,int> Progress{get;set;}=new();public Dictionary<string,int[]> Plans{get;set;}=new();
 public PendingMissionFight? PendingFight{get;set;}
 public MissionOptions Options{get;set;}=new();
 public DateTime? ScannedUtc{get;set;}public List<MissionCard> Cards{get;set;}=new();public List<MissionAttack> Attacks{get;set;}=new();
 public string Note{get;set;}="Open Missions, then Scan current missions.";
 public string Contract{get;set;}="";public string RunId{get;set;}="";public int CompletedSteps{get;set;}
 public string PendingAction{get;set;}="";public string PendingContract{get;set;}="";public string PendingKind{get;set;}="";
 public List<string> Deliveries{get;set;}=new();
 public List<string> RewardRequests{get;set;}=new();
}
public sealed class MissionOptions {
 public string Weapon{get;set;}="weapon_primary";public int Delay{get;set;}=1000;public int Reserve{get;set;}=0;
 public int MaximumAttacks{get;set;}=10;public int LifePercent{get;set;}=50;
 public bool AllowItemSend{get;set;}
 public bool Heal{get;set;}public bool ClaimRewards{get;set;}=true;
 public void Validate(){if(Weapon is not ("weapon_primary" or "weapon_secondary" or "weapon_melee")||Delay is <500 or >5000||Reserve is <0 or >10000||MaximumAttacks is <1 or >100||LifePercent is <1 or >100)throw new InvalidDataException("Review mission weapon, delay, energy reserve, attack budget and life target.");}
}
public static class MissionPlanner {
 private static string Name(string value)=>Regex.Replace(value.ToLowerInvariant().Trim().TrimEnd('*','!'),@"\s+"," ");
 public static MissionPlan Plan(MissionCard c){
  var ids=c.Targets.Where(x=>x>0).Distinct().ToArray();var name=Name(c.Title);string text=c.Objective;
  if(c.CanComplete)return new(){Supported=true,Kind="claim",Reason="Torn shows Complete for this contract. No fight is needed."};
  if(Regex.IsMatch(text,@"\b(send|deliver|parcel|gift|mail)\b",RegexOptions.IgnoreCase)){
   bool simpleSend=Regex.IsMatch(text,@"^send\s+(?:(?:1|one|a|an)\s+)?",RegexOptions.IgnoreCase)&&!Regex.IsMatch(text,@"\b(parcel|wrap|gift|box|then|after|weapon|buy|some|any|two|three)\b",RegexOptions.IgnoreCase)&&(!Regex.Match(text,@"^send\s+(\d+)\b",RegexOptions.IgnoreCase).Success||Regex.Match(text,@"^send\s+(\d+)\b",RegexOptions.IgnoreCase).Groups[1].Value=="1")&&ids.Length==1&&c.ItemId>0&&c.ItemName.Length is >0 and <=100;
   return new(){Supported=simpleSend,Kind="delivery",Action="send",Reason=simpleSend?"Send one exact linked item to the one linked recipient. Requires item-transfer permission and a verifiable native Send form.":"Item delivery needs manual preparation: no single exact linked item/recipient or the objective includes special steps. Open inventory and follow the mission text."};
  }
  var simple=new HashSet<string>{"forgotten bills","guardian","lack of awareness","loyal customer","massacrist","peak experience","scammer","sending a message","sleep aid","big tub of muscle","immovable object","party tricks","hare, meet tortoise","against the odds","drug problem","hands off","tree huggers","undercutters","wrath of duke","show some muscle","battering ram","rolling in it","charity work","family ties","unwanted attention"};
  if(!simple.Contains(name))return new(){Reason="This contract has additional or unrecognized requirements. Read its objective; automatic fighting is disabled."};
  if(ids.Length==0)return new(){Reason="No exact target links were found in the mission. Open the contract and scan again."};
  if(ids.Length>20)return new(){Reason="Too many target links to identify a bounded mission plan."};
  if(Regex.IsMatch(text,@"\b(only|without|equip|wear|mask|grenade|critical|injur|interrogat|kiss|secrete|spouse|abroad|travel|minutes|no fewer|finishing|first attempt|body part)\w*\b",RegexOptions.IgnoreCase))
   return new(){Reason="The current objective contains special restrictions. Complete those steps manually; Commander will not ignore them."};
  string action=name is "rolling in it" or "charity work"?"mug":name is "family ties" or "unwanted attention"?"hospitalize":"leave";
  bool attack=name is "show some muscle" or "battering ram";int repeats=name is "battering ram" or "family ties"?3:1;
  if(repeats>1&&ids.Length!=1)return new(){Reason="The repeated-attack contract does not have one unambiguous target."};
  var steps=Enumerable.Range(0,repeats).SelectMany(_=>ids).ToArray();
  return new(){Supported=true,Kind="combat",Action=action,AttackOnly=attack,Steps=steps,Reason=attack?"Attack the exact linked target; victory is not required. Commander still verifies the completed attack.":action=="mug"?"Defeat each linked target and choose Mug. Torn's mission page must confirm the objective; a failed/empty mug is not assumed successful.":action=="hospitalize"?"Defeat the linked target(s) and choose Hospitalize.":"Defeat the exact linked target(s), then Leave. Mission completion is checked separately on Torn."};
 }
 public static MissionCard? Next(IEnumerable<MissionCard> cards,ISet<string> seen,int remainingAttacks,bool allowItems)=>cards.FirstOrDefault(c=>{
  if(seen.Contains(c.Key))return false;if(c.CanComplete)return true;if(remainingAttacks<=0)return false;var p=Plan(c);return p.Supported&&(p.Kind=="combat"||p.Kind=="delivery"&&allowItems);
 });
 public static bool Matches(MissionPlan plan,string result)=>plan.AttackOnly||plan.Action=="mug"&&result=="Mugged"||plan.Action=="hospitalize"&&(result=="Hospitalized"||result=="Hospitalised")||plan.Action=="leave"&&result=="Attacked";
}
