using System.Text.Json;
namespace TornCommander;
public static class MedicalSnapshotReader{
 public static async Task<string> ReadIdentityAsync(Func<string,CancellationToken,Task<string>> fetch,CancellationToken ct){
  string json=await fetch("user/profile",ct);using var d=JsonDocument.Parse(json);var p=d.RootElement.GetProperty("profile");
  if(p.GetProperty("id").GetInt32()<=0||string.IsNullOrWhiteSpace(p.GetProperty("name").GetString()))throw new InvalidDataException("Account identity is unavailable.");
  return json;
 }

 public static async Task<string> ReadAsync(Func<string,CancellationToken,Task<string>> fetch,int expectedPlayer,bool perks,CancellationToken ct){
  string json=await fetch("user?selections=profile,cooldowns"+(perks?",perks":""),ct);
  using var d=JsonDocument.Parse(json);var r=d.RootElement;
  if(r.GetProperty("profile").GetProperty("id").GetInt32()!=expectedPlayer)throw new InvalidDataException("API account changed; no medical action allowed.");
  if(r.GetProperty("cooldowns").GetProperty("medical").GetInt32()<0)throw new InvalidDataException("Invalid medical cooldown.");
  if(perks&&r.GetProperty("perks").ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Medical perks unavailable.");
  return json;
 }
}
