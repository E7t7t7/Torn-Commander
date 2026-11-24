using System.Text.RegularExpressions;
namespace TornCommander;
public sealed record GorillaVendor(int Player,string Name,int Delivered,bool Removed);
public sealed record GorillaContract(string Id,string Buyer,int Target,string State,int Requested,int Verified,int Remaining,long Pay,List<GorillaVendor> Vendors){
 public bool Active=>Regex.IsMatch(State,@"^[^\p{L}\p{N}]*In Progress\s*$",RegexOptions.IgnoreCase);
}
public static class GorillaContracts {
 public const string Service="Gorilla Mercenary Services";
 public const string Invite="https://discord.gg/ZDfnmBzKR";
 public static GorillaContract Parse(string raw){
  if(string.IsNullOrWhiteSpace(raw)||raw.Length>30000)throw new InvalidDataException("Paste the full Live Contract Progress card (up to 30,000 characters).");
  string text=raw.Replace("\r\n","\n").Replace('\r','\n').Replace("**","").Replace("__","").Replace("\u200b","");
  string Field(string name){
   var m=Regex.Match(text,@"(?:^|\n)[ \t]*"+Regex.Escape(name)+@"[ \t]*:?[ \t]*(?:\n[ \t]*)?([^\n]+)",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
   return m.Success?m.Groups[1].Value.Trim():"";
  }
  int Number(string name){string s=Field(name).Replace(",","");if(!int.TryParse(s,out int n)||n<0||n>1000000)throw new InvalidDataException("Missing or invalid "+name+". Copy the whole progress card.");return n;}
  if(Regex.Matches(text,@"(?:^|\n)[ \t]*Contract ID[ \t]*:?",RegexOptions.IgnoreCase).Count!=1)throw new InvalidDataException("Copy exactly one Live Contract Progress card.");
  string id=Field("Contract ID");if(!Regex.IsMatch(id,@"^losses-\d{1,12}$",RegexOptions.IgnoreCase))throw new InvalidDataException("Expected a Gorilla losses-N contract ID.");
  if(!Field("Contract Type").Equals("Buy Losses",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Only Buy Losses contracts are supported.");
  string patron=Field("Patron / Target");var target=Regex.Match(patron,@"^(.+?)\s*\[(\d+)\]\s*$");
  if(!target.Success||!int.TryParse(target.Groups[2].Value,out int player)||player<=0||target.Groups[1].Value.Length>120)throw new InvalidDataException("Patron / Target must include its Torn player ID in brackets.");
  int requested=Number("Requested Losses"),verified=Number("Verified Losses"),remaining=Number("Remaining Losses");
  if(requested<1||verified>requested||requested-verified!=remaining)throw new InvalidDataException("Contract totals disagree. Copy its latest complete progress card.");
  string payout=Field("Payout Per Loss").Replace("$","").Replace(",","").Trim();
  if(!long.TryParse(payout,out long pay)||pay is <1 or >1000000000000)throw new InvalidDataException("Missing cash payout per loss.");
  var vendors=new List<GorillaVendor>();
  foreach(Match m in Regex.Matches(text,@"(?:^|\n)[ \t]*(.+?)\[(\d+)\][ \t]*[\u2013\u2014-][ \t]*(\d+)[ \t]*loss(?:\(es\)|es|\(s\)|s)?[ \t]*(\(removed\))?",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100))){
   if(int.TryParse(m.Groups[2].Value,out int who)&&who>0&&int.TryParse(m.Groups[3].Value,out int count)&&count>=0)vendors.Add(new(who,m.Groups[1].Value.Trim(),count,m.Groups[4].Success));
  }
  if(vendors.GroupBy(x=>x.Player).Any(g=>g.Count()>1))throw new InvalidDataException("Duplicate vendor identities in copied text; copy only one current contract card.");
  string state=Field("Status");if(state.Length is <1 or >100)throw new InvalidDataException("Contract status is missing.");
  return new(id.ToLowerInvariant(),target.Groups[1].Value.Trim(),player,state,requested,verified,remaining,pay,vendors);
 }
 public static string MessageUrl(string raw){
  if(string.IsNullOrWhiteSpace(raw))return "";
  if(!Uri.TryCreate(raw.Trim(),UriKind.Absolute,out var u)||u.Scheme!="https"||u.Host!="discord.com"||u.UserInfo.Length>0||!u.IsDefaultPort||u.Query.Length>0||u.Fragment.Length>0||
   !Regex.IsMatch(u.AbsolutePath,@"^/channels/\d{15,22}/\d{15,22}(?:/\d{15,22})?/?$"))
   throw new InvalidDataException("Use Copy Message Link from the Gorilla channel, or leave the link blank.");
  return u.AbsoluteUri;
 }
 public static void ValidateImport(GorillaContract c,int player,int count,bool joined){
  if(player<=0||player==c.Target)throw new InvalidDataException("The selling account must be different from the target.");
  if(!c.Active||c.Remaining<1)throw new InvalidDataException("The copied contract is not in progress or has no remaining losses.");
  if(!joined)throw new InvalidDataException("Join Contract in Discord first, then confirm you joined as the selected Torn account.");
  if(c.Vendors.Count>0&&!c.Vendors.Any(v=>v.Player==player&&!v.Removed))throw new InvalidDataException("This account is absent from the active copied vendor list. Join with the correct Discord/Torn account and copy the refreshed card.");
  if(count<1||count>c.Remaining)throw new InvalidDataException("Choose a positive batch no larger than the shared remaining count.");
 }
 public static string? RunBlock(string assigned,string profile,bool joined,DateTime? copied,DateTime now){
  if(assigned!=profile||!joined)return "This Gorilla order belongs to another profile or lacks your Discord join confirmation.";
  if(!copied.HasValue||copied>now.AddMinutes(2)||now-copied.Value>TimeSpan.FromMinutes(15))return "Gorilla contract copy is over 15 minutes old. Stop this worker, refresh the card in Discord and re-import it before continuing.";
  return null;
 }
}
