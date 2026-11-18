using System.Text.Json;
namespace TornCommander;
public sealed record SlotReceipt(string Id,DateTime At,decimal Bet,decimal Won);
public sealed class SlotPending {
 public string Token{get;set;}="";public DateTime StartedUtc{get;set;}public int BeforeTokens{get;set;}
}
public sealed class SlotsBatch {
 public string Profile{get;set;}="";public int Owner{get;set;}public decimal Bet{get;set;}=1000;
 public int Requested{get;set;}=100;public int Limit{get;set;}public bool AllTokens{get;set;}
 public int StartTokens{get;set;}public int LastTokens{get;set;}
 public decimal StopLoss{get;set;}public decimal ProfitTarget{get;set;}
 public string Status{get;set;}="Paused";public string Note{get;set;}="";
 public bool PauseRequested{get;set;}public bool StopRequested{get;set;}
 public DateTime StartedUtc{get;set;}public DateTime? EndedUtc{get;set;}
 public string ConfirmedToken{get;set;}="";
 public SlotPending? Pending{get;set;}public List<string> KnownIds{get;set;}=new();
 public List<SlotReceipt> Receipts{get;set;}=new();
 public decimal Staked=>Receipts.Sum(x=>x.Bet);public decimal Won=>Receipts.Sum(x=>x.Won);public decimal Net=>Won-Staked;
}
public sealed record SlotsAccount(int Owner,int Tokens,decimal Wallet);
public sealed class SlotsBoard {
 public bool Ok{get;set;}public bool Ready{get;set;}public bool Retryable{get;set;}
 public int? Owner{get;set;}public int? Tokens{get;set;}public string Note{get;set;}="";
}
public sealed class SlotsDispatch {
 public bool Dispatched{get;set;}public string Note{get;set;}="";
}
internal sealed class SlotsRuntime {
 public required Func<bool> Current{get;init;}
 public required Func<Task<SlotsAccount>> Account{get;init;}
 public required Func<Task<SlotsBoard>> Board{get;init;}
 public required Func<SlotPending,Task<SlotsDispatch>> Dispatch{get;init;}
 public required Func<Task<List<SlotReceipt>>> Receipts{get;init;}
 public required Func<string,Task> Acknowledge{get;init;}
 public required Action Save{get;init;}
 public Func<DateTime> Now{get;init;}=()=>DateTime.UtcNow;
 public Func<int,Task> Delay{get;init;}=ms=>Task.Delay(ms);
}
internal static class SlotsData {
 public static readonly decimal[] Bets=[10,100,1000,10000,100000,1000000,10000000];
 public static int Limit(decimal bet,int requested,int available,bool all){
  if(!Bets.Contains(bet)||requested is <1 or >10000||available<0)throw new InvalidDataException("Choose a listed denomination and 1–10,000 spins.");
  return Math.Min(available,all?10000:requested);
 }
 public static decimal MoneyField(JsonElement request,string field){
  if(request.ValueKind!=JsonValueKind.Object||!request.TryGetProperty(field,out var value)||value.ValueKind!=JsonValueKind.Number||
   !value.TryGetDecimal(out var amount)||amount<0||amount>1000000000000m||decimal.Round(amount,2)!=amount)
   throw new InvalidDataException("Enter a valid "+field+" amount (0 turns off an optional money limit). No bet placed.");
  return amount;
 }
 public static int CountField(JsonElement request){
  if(request.ValueKind!=JsonValueKind.Object||!request.TryGetProperty("count",out var value)||value.ValueKind!=JsonValueKind.Number||
   !value.TryGetInt32(out var count)||count is <1 or >10000)
   throw new InvalidDataException("Choose 1–10,000 spins. No bet placed.");
  return count;
 }
 public static SlotsAccount Account(string json){
  using var d=JsonDocument.Parse(json);var r=d.RootElement;
  JsonElement Field(string section,string name){
   if(r.ValueKind!=JsonValueKind.Object||!r.TryGetProperty(section,out var part)||part.ValueKind!=JsonValueKind.Object||
    !part.TryGetProperty(name,out var value)||value.ValueKind!=JsonValueKind.Number)
    throw new InvalidDataException("Torn did not supply "+section+"."+name+". Check this profile's API permissions and refresh before starting Slots. No bet placed.");
   return value;
  }
  if(!Field("profile","id").TryGetInt32(out var owner)||!Field("casino","tokens").TryGetInt32(out var tokens)||
   !Field("money","wallet").TryGetDecimal(out var wallet))throw new InvalidDataException("Torn returned an unreadable casino snapshot. No bet placed.");
  if(owner<=0||tokens<0||wallet<0||wallet>1000000000000000m)throw new InvalidDataException("Torn returned an invalid casino account snapshot.");
  return new(owner,tokens,wallet);
 }
 public static List<string> Baseline(string json){
  using var d=JsonDocument.Parse(json);return d.RootElement.GetProperty("log").EnumerateArray().Select(x=>x.GetProperty("id").GetString()??"").Where(x=>x.Length is >0 and <=200).Distinct().ToList();
 }
 public static List<SlotReceipt> ReadReceipts(string json,DateTime since){
  using var d=JsonDocument.Parse(json);var rows=new List<SlotReceipt>();
  long cutoff=new DateTimeOffset(since).ToUnixTimeSeconds();
  foreach(var row in d.RootElement.GetProperty("log").EnumerateArray()){
   long stamp=row.GetProperty("timestamp").GetInt64();if(stamp<cutoff)continue;
   var details=row.GetProperty("details");string title=details.GetProperty("title").GetString()??"";
   bool win=title.Equals("Casino slots win",StringComparison.OrdinalIgnoreCase),lose=title.Equals("Casino slots lose",StringComparison.OrdinalIgnoreCase);
   if(!win&&!lose)continue;
   if(details.TryGetProperty("id",out var kind)&&kind.GetInt32()!=(win?8300:8301))throw new InvalidDataException("Slot receipt type and title disagree. No further spin authorized.");
   string id=row.GetProperty("id").GetString()??"";var data=row.GetProperty("data");
   if(id.Length is <1 or >200||!data.TryGetProperty("bet_amount",out var b)||!b.TryGetDecimal(out var bet)||!Bets.Contains(bet))
    throw new InvalidDataException("Slot receipt has an unsupported stake format. No further spin authorized.");
   decimal won=0;
   if(win&&(!data.TryGetProperty("won_amount",out var w)||!w.TryGetDecimal(out won)||won<=0||won>1000000000000000m||decimal.Truncate(won)!=won))
    throw new InvalidDataException("Winning slot receipt has no verified payout. No further spin authorized.");
   rows.Add(new(id,DateTimeOffset.FromUnixTimeSeconds(stamp).UtcDateTime,bet,won));
  }
  return rows.DistinctBy(x=>x.Id).ToList();
 }
 public static SlotReceipt? Find(SlotsBatch batch,IEnumerable<SlotReceipt> receipts){
  var pending=batch.Pending??throw new InvalidDataException("No pending spin to verify.");
  var rows=receipts.Where(x=>!batch.KnownIds.Contains(x.Id)&&x.At>=DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(pending.StartedUtc).ToUnixTimeSeconds()).UtcDateTime).DistinctBy(x=>x.Id).ToList();
  if(rows.Count>1)throw new InvalidDataException("More than one new slot result appeared. Stop manual spins and review; no additional bet will be placed.");
  var row=rows.SingleOrDefault();if(row!=null&&row.Bet!=batch.Bet)throw new InvalidDataException("The slot receipt uses a different denomination. Batch paused.");
  return row;
 }
}
internal static class SlotsRunner {
 public static async Task Run(SlotsBatch b,SlotsRuntime r){
  void Save(string status,string note){b.Status=status;b.Note=note;r.Save();}
  void Finish(string note){b.EndedUtc=r.Now();Save(b.StopRequested?"Stopped":"Complete",note);}
  try{
   if(b.Owner<=0||!SlotsData.Bets.Contains(b.Bet)||b.Limit is <1 or >10000)throw new InvalidDataException("Saved slots batch settings are invalid.");
   while(true){
    // A pending click is always reconciled before considering any new click,
    // including after Pause, Stop, profile changes and application restarts.
    if(b.Pending!=null){
     var until=r.Now().AddSeconds(90);bool found=false;
     for(int attempt=0;attempt<30&&r.Now()<=until;attempt++){
      var receipt=SlotsData.Find(b,await r.Receipts());
      if(receipt!=null){
       string token=b.Pending.Token;b.ConfirmedToken=token;b.LastTokens=Math.Max(0,b.Pending.BeforeTokens-1);b.Receipts.Add(receipt);b.KnownIds.Add(receipt.Id);b.Pending=null;
       Save("Running",$"Confirmed {b.Receipts.Count}/{b.Limit} spins.");
       try{await r.Acknowledge(token);}catch{} // The durable API receipt is authoritative.
       found=true;break;
      }
      Save(b.StopRequested?"Stopping":b.PauseRequested?"Pausing":"Verifying","Waiting for Torn's slot receipt. No second spin will be clicked.");
      await r.Delay(2500);
     }
     if(!found){Save("Needs review","The attempted spin is still unverified. Resume will check its receipt; it will never re-click that bet.");return;}
    }
    if(b.StopRequested){Finish("Stopped. Only verified spins are included in the totals.");return;}
    if(b.Receipts.Count>=b.Limit){Finish("Batch complete. All scheduled spins have verified receipts.");return;}
    if(b.StopLoss>0&&b.Net<=-b.StopLoss){Finish("Stop-loss reached.");return;}
    if(b.ProfitTarget>0&&b.Net>=b.ProfitTarget){Finish("Profit target reached.");return;}
    if(b.PauseRequested||!r.Current()){Save("Paused","Paused before the next bet. Keep the owning profile and Slots tab open to resume.");return;}
    var account=await r.Account();
    if(account.Owner!=b.Owner)throw new InvalidDataException("Torn API key belongs to a different player. No next spin authorized.");
    b.LastTokens=account.Tokens;
    if(b.PauseRequested||b.StopRequested||!r.Current())continue;
    if(account.Tokens==0){Finish("No casino tokens remain. No refill was purchased.");return;}
    if(account.Wallet<b.Bet){Finish("Not enough cash on hand for the fixed denomination. No money was withdrawn.");return;}
    SlotsBoard? board=null;var readyUntil=r.Now().AddSeconds(35);
    for(int check=0;check<100&&r.Now()<readyUntil;check++){
     if(b.PauseRequested||b.StopRequested||!r.Current())break;
     board=await r.Board();
     if(board.Ok&&board.Owner==b.Owner&&board.Ready)break;
     if(!board.Retryable&&!board.Ok)throw new InvalidDataException(board.Note);
     await r.Delay(400);
    }
    if(b.PauseRequested||b.StopRequested||!r.Current())continue;
    if(board?.Ok!=true||!board.Ready)throw new InvalidDataException(board?.Note??"Slots did not become ready. No bet placed.");
    if(board.Owner!=b.Owner)throw new InvalidDataException("The browser account changed. No bet placed.");
    if(board.Tokens is null)throw new InvalidDataException("The visible token count could not be verified.");
    if(board.Tokens<=0){Finish("No casino tokens remain on the game page.");return;}
    b.Pending=new(){Token=Guid.NewGuid().ToString("N"),StartedUtc=r.Now(),BeforeTokens=board.Tokens.Value};
    Save("Dispatching","Placing one fixed-denomination spin. Its result must settle before another.");
    var click=await r.Dispatch(b.Pending);
    if(!click.Dispatched){b.Pending=null;Save("Paused",click.Note.Length>0?click.Note:"Slots changed before the click. No bet was placed.");return;}
    Save("Verifying","One spin dispatched. Waiting for its official receipt.");
    await r.Delay(1200);
   }
  }catch(Exception ex){
   b.Status=b.Pending!=null?"Needs review":"Paused";
   b.Note=ex is InvalidDataException?ex.Message:"Slots verification or local saving failed. Check API access/connection and resume; no bet was retried.";
   try{r.Save();}catch{}
  }
 }
}
