using System.Globalization;
using System.Text.Json;
namespace TornCommander;
internal sealed record BaldrListData(List<LevelingTarget> Targets,List<string> Groups,DateTime FetchedUtc);
internal static class BaldrListProvider {
 public const string SourceUrl="https://raw.githubusercontent.com/OranWeb/tc-baldrs-levelling-list/master/data.json";
 private static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20),MaxResponseContentBufferSize=1048576};
 public static async Task<BaldrListData> LoadAsync(string group,int self,CancellationToken ct=default){
  using var request=new HttpRequestMessage(HttpMethod.Get,SourceUrl);request.Headers.UserAgent.ParseAdd("TornCommander-BaldrLoader/1.0");
  using var response=await Http.SendAsync(request,ct);response.EnsureSuccessStatusCode();
  return Parse(await response.Content.ReadAsStringAsync(ct),group,self,DateTime.UtcNow);
 }
 public static BaldrListData Parse(string json,string group,int self,DateTime fetched){
  if(json.Length>1048576)throw new InvalidDataException("Baldr's list exceeds the supported download size.");
  using var doc=JsonDocument.Parse(json);if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Baldr's list has an unsupported format; the existing queue was kept.");
  var groups=doc.RootElement.EnumerateObject().Where(x=>x.Value.ValueKind==JsonValueKind.Array).Select(x=>x.Name).ToList();
  if(groups.Count is <1 or >50||groups.Any(x=>x.Length>120))throw new InvalidDataException("Invalid Baldr list names.");
  if(group!=""&&!groups.Contains(group))throw new InvalidDataException("This Baldr list is no longer present. Choose All lists and reload.");
  var rows=new List<LevelingTarget>();var seen=new HashSet<int>();
  foreach(var list in doc.RootElement.EnumerateObject()){
   if(!groups.Contains(list.Name)||(group!=""&&group!=list.Name))continue;
   foreach(var item in list.Value.EnumerateArray()){
    if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("id",out var value)||!int.TryParse(value.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out int id)||id<=0)throw new InvalidDataException("An invalid target was found; the existing queue was kept.");
    if(id==self||!seen.Add(id))continue;
    string name=item.TryGetProperty("name",out value)?value.ToString():"";if(name.Length>120)throw new InvalidDataException("Invalid target name.");
    int level=0;if(item.TryGetProperty("lvl",out value)&&(!int.TryParse(value.ToString(),out level)||level is <1 or >100))throw new InvalidDataException("Invalid listed level.");
    long? stats=null;if(item.TryGetProperty("total",out value)&&long.TryParse(value.ToString(),NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out long total)&&total>=0)stats=total;
    rows.Add(new(){Id=id,Name=name,Level=level,Source=list.Name,ListedBattleStats=stats});
    if(rows.Count>1000)throw new InvalidDataException("Baldr's list contains too many targets.");
   }
  }
  if(rows.Count==0)throw new InvalidDataException("No targets were found in the selected list.");
  return new(rows,groups,fetched);
 }
}
