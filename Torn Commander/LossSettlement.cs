namespace TornCommander;
public sealed record LossSalesTotals(long Delivered,long Cash,long Xanax,int Unrecorded,int Active,int TotalOrders,long QuotedCash);
public static class LossSettlement {
 public static bool PaymentKnown(LossOrder o)=>o.SourceService=="FFScouter"?o.RemotePaidCash.HasValue:o.CashReceived.HasValue||o.XanaxReceived.HasValue;
 public static LossSalesTotals Totals(IEnumerable<LossOrder> orders){
  var rows=orders.ToList();return new(rows.Sum(x=>(long)x.CompletedTotal),rows.Sum(x=>x.SourceService=="FFScouter"?(x.RemotePaidCash??0):(x.CashReceived??0)),rows.Sum(x=>(long)(x.XanaxReceived??0)),rows.Count(x=>x.CompletedTotal>0&&!PaymentKnown(x)),rows.Count(x=>!x.IsClosed),rows.Count,rows.Sum(x=>x.Gross));
 }

 public static void Save(LossOrder order,bool close,bool cancel,long? cash,int? xanax,Action persist){
  if(cash is <0 or >1000000000000000||xanax is <0 or >1000000)throw new InvalidDataException("Enter nonnegative cash and whole Xanax quantities within the displayed limits.");
  bool payment=cash.HasValue||xanax.HasValue;
  if(!close&&!payment)throw new InvalidDataException("Enter actual payment totals (zero is allowed), or close the order without recording payment.");
  var before=(order.Status,order.NextA,order.NextB,order.ClosedUtc,order.CashReceived,order.XanaxReceived,order.PayoutUpdatedUtc);
  if(close&&!order.IsClosed){
   order.ClosedUtc=DateTime.UtcNow;
   order.Status=cancel?"Cancelled":order.Remaining>0?"Closed partial":"Complete";
   order.NextA="Done";order.NextB="Done";
  }
  if(payment){order.CashReceived=cash??0;order.XanaxReceived=xanax??0;order.PayoutUpdatedUtc=DateTime.UtcNow;}
  try{persist();}catch{
   (order.Status,order.NextA,order.NextB,order.ClosedUtc,order.CashReceived,order.XanaxReceived,order.PayoutUpdatedUtc)=before;throw;
  }
 }
 public static bool HasHistory(LossOrder order)=>order.CompletedTotal>0||order.CashReceived.HasValue||order.XanaxReceived.HasValue||order.ClosedUtc.HasValue;
}
