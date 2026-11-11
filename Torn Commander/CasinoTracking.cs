namespace TornCommander;
public static class CasinoTracking {
 public static readonly string[] Games=["Blackjack","Poker","High-Low","Other"];
 public static void Money(decimal value){if(value<0||value>1000000000000m||decimal.Round(value,2)!=value)throw new InvalidDataException("Enter a nonnegative amount with at most two decimal places.");}
 public static CasinoSession Start(string profile,string id,string game,decimal bankroll,decimal stopLoss,decimal target,DateTime now){
  foreach(var v in new[]{bankroll,stopLoss,target})Money(v);
  if(string.IsNullOrWhiteSpace(profile)||id.Length is <1 or >80||!Games.Contains(game)||bankroll<=0)throw new InvalidDataException("Select a game and positive starting table bankroll.");
  var s=new CasinoSession{Id=id,ProfileId=profile,Date=now,Game=game,StartBankroll=bankroll,EndBankroll=bankroll,StopLoss=stopLoss,ProfitTarget=target,Active=true};
  s.Readings.Add(new(){At=now,Bankroll=bankroll,Net=0});return s;
 }
 public static void Update(CasinoSession s,decimal bankroll,decimal deposited,decimal withdrawn,DateTime now){
  foreach(var v in new[]{bankroll,deposited,withdrawn})Money(v);
  if(!s.Active)throw new InvalidOperationException("This session has ended. Start a new session.");
  // Funding figures are lifetime session totals, not deltas: replaying the same update cannot add money twice.
  if(deposited<s.Deposits||withdrawn<s.Withdrawals)throw new InvalidDataException("Total added/cashed out cannot decrease. Use the current session totals.");
  s.EndBankroll=bankroll;s.Deposits=deposited;s.Withdrawals=withdrawn;
  if(s.LimitReached==""){
   if(s.StopLoss>0&&s.ProfitLoss<=-s.StopLoss)s.LimitReached="Stop-loss reached";
   else if(s.ProfitTarget>0&&s.ProfitLoss>=s.ProfitTarget)s.LimitReached="Profit target reached";
   if(s.LimitReached!="")s.LimitReachedUtc=now;
  }
  if(s.Readings.Count==0||s.Readings[^1].Bankroll!=bankroll||s.Readings[^1].Net!=s.ProfitLoss)
   s.Readings.Add(new(){At=now,Bankroll=bankroll,Net=s.ProfitLoss});
  s.Readings=s.Readings.TakeLast(500).ToList();
 }
 public static object View(CasinoSession s)=>new{s.Id,s.Date,s.Game,s.Active,s.StartBankroll,s.EndBankroll,s.Deposits,s.Withdrawals,s.ProfitLoss,s.StopLoss,s.ProfitTarget,s.LimitReached,s.LimitReachedUtc,s.EndedUtc,s.Notes,points=s.Readings};
}
