using System.Text.Json;
namespace TornCommander;
public sealed record LossExchangeListing(int Target,string Buyer,int Remaining,long Pay){
 public static LossExchangeListing Parse(JsonElement root){
  if(root.GetProperty("actionType").GetString()!="loss")throw new InvalidDataException("Only loss contracts can be imported.");
  int target=root.GetProperty("targetId").GetInt32(),remaining=root.GetProperty("remaining").GetInt32();
  long pay=root.GetProperty("pay").GetInt64();string buyer=root.GetProperty("buyer").GetString()?.Trim()??"";
  if(target<=0||remaining is <1 or >1000000||pay is <0 or >1000000000000||buyer.Length>120)throw new InvalidDataException("Contract fields are missing or outside the supported range. Refresh Browse contracts.");
  return new(target,buyer.Length>0?buyer:target.ToString(),remaining,pay);
 }
 public LossOrder Create(int count,string a,string b){
  if(count<1||count>Remaining||string.IsNullOrWhiteSpace(a)||a==b)throw new InvalidDataException("Choose a valid loss count and one or two different accounts.");
  return new(){Id=Guid.NewGuid().ToString("N"),SourceService="Loss X-change",TargetPlayerId=Target.ToString(),BuyerName=Buyer,RequestedLosses=count,PayPerLoss=Pay,ProfileAId=a,ProfileBId=b,Notes="Imported public Loss X-change listing. Local Commander order; no external reservation or payment confirmation."};
 }
 public LossOrder? Existing(IEnumerable<LossOrder> orders)=>orders.FirstOrDefault(x=>!x.IsClosed&&x.SourceService=="Loss X-change"&&x.TargetPlayerId==Target.ToString()&&x.PayPerLoss==Pay);
}
