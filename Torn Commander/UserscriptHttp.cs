using System.Text;
using System.Text.Json;
namespace TornCommander;
internal static class UserscriptHttp
{
 public static HttpRequestMessage Create(string method,Uri uri,string? data,JsonElement headers)
 {
  var request=new HttpRequestMessage(new HttpMethod(method),uri);
  if(data is not null)request.Content=new StringContent(data,Encoding.UTF8);
  if(headers.ValueKind==JsonValueKind.Object){
   foreach(var header in headers.EnumerateObject()){
    var value=header.Value.ValueKind==JsonValueKind.String?header.Value.GetString()??"":header.Value.ToString();
    if(request.Headers.TryAddWithoutValidation(header.Name,value))continue;
    if(request.Content is null)continue;
    // StringContent already sets text/plain; replace it rather than silently appending a rejected second Content-Type.
    request.Content.Headers.Remove(header.Name);
    request.Content.Headers.TryAddWithoutValidation(header.Name,value);
   }
  }
  return request;
 }
}
