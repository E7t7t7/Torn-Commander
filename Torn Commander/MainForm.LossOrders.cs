using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm
{
    private readonly HashSet<string> _closingLossOrders=new();
    private object LossLedgerDashboard(){var totals=LossSettlement.Totals(_lossOrders);return new{
        delivered=totals.Delivered,cash=totals.Cash,xanax=totals.Xanax,unrecorded=totals.Unrecorded,active=totals.Active,totalOrders=totals.TotalOrders,
        rows=_lossOrders.OrderByDescending(x=>x.ClosedUtc??x.CreatedAt.ToUniversalTime()).Take(100).Select(x=>new{id=x.Id,buyer=x.BuyerName,delivered=x.CompletedTotal,requested=x.RequestedLosses,status=x.Status,cash=x.CashReceived,xanax=x.XanaxReceived,created=x.CreatedAt,closed=x.ClosedUtc}).ToList()
    };}
    private async Task SettleDashboardLossOrderAsync(JsonElement request,bool close){
        if(ReadWorkerString(request,"profileId")!=CurrentProfileId)throw new InvalidDataException("Profile changed. Review the selected order.");
        string id=ReadWorkerString(request,"orderId");
        var order=_lossOrders.FirstOrDefault(x=>x.Id==id)??throw new InvalidDataException("Order no longer exists.");
        if(SelectedLossOrder()?.Id!=id)throw new InvalidDataException("Selected order changed; review it before saving.");
        long? cash=request.TryGetProperty("cash",out var c)&&c.ValueKind!=JsonValueKind.Null?c.GetInt64():null;
        int? xanax=request.TryGetProperty("xanax",out var x)&&x.ValueKind!=JsonValueKind.Null?x.GetInt32():null;
        if(cash is <0 or >1000000000000000||xanax is <0 or >1000000)throw new InvalidDataException("Check the cash and whole Xanax payment totals.");
        if(!_closingLossOrders.Add(id))throw new InvalidOperationException("This order is already being saved.");
        try{
            if(close){
                foreach(var accountA in new[]{true,false}){
                    var worker=accountA?_lossWorkerA:_lossWorkerB;
                    if(worker!=null&&_medicalSessions.TryGetValue(worker,out var medical)&&!medical.Leveling&&medical.Order==id)
                        await StopLossWorkerAsync(accountA);
                }
                if(order.CompletedTotal!=request.GetProperty("expectedDelivered").GetInt32())throw new InvalidDataException("Delivered count changed while stopping. Workers are stopped; review the current count and close again.");
            }
            LossSettlement.Save(order,close,ReadWorkerString(request,"outcome")=="cancel",cash,xanax,SaveLossOrders);
            RebindLossOrders();SelectLossOrder(id);UpdateModernDashboard();
            FlashStatus(close?"Order closed at "+order.CompletedTotal+"/"+order.RequestedLosses+". Delivered losses and payout history retained.":"Actual payout totals saved. Saving again replaces these totals; it does not add another payment.");
        }finally{_closingLossOrders.Remove(id);}
    }
    private void CreateDashboardLossOrder(JsonElement request)
    {
        if(request.GetProperty("profileId").GetString()!=CurrentProfileId)throw new InvalidDataException("Profile changed. Review the order and try again.");
        var receipt=request.GetProperty("receipt").GetString()??"";
        if(!Guid.TryParseExact(receipt,"N",out _))throw new InvalidDataException("Invalid order receipt.");
        if(_lossOrders.Any(x=>x.Id==receipt)){SelectLossOrder(receipt);UpdateModernDashboard();return;}
        var target=request.GetProperty("target").GetString()?.Trim()??"";
        var buyer=request.GetProperty("buyer").GetString()?.Trim()??"";
        var count=request.GetProperty("requested").GetInt32();var pay=request.GetProperty("pay").GetInt64();
        var a=request.GetProperty("accountA").GetString()??"";var b=request.GetProperty("accountB").GetString()??"";
        if(target.Length>12||!long.TryParse(target,out var id)||id<=0||!target.All(char.IsDigit)||buyer.Length>120||count<1||count>1000000||pay<0||pay>1000000000000)
            throw new InvalidDataException("Check the numeric player ID, count, buyer and pay per loss.");
        if(!_settings.Profiles.Any(x=>x.Id==a)||(!string.IsNullOrEmpty(b)&&!_settings.Profiles.Any(x=>x.Id==b)))throw new InvalidDataException("Select saved account profiles.");
        if(a==b)throw new InvalidDataException("Choose different profiles for A and B, or leave B unused for a solo order.");
        var order=new LossOrder{Id=receipt,TargetPlayerId=target,BuyerName=buyer.Length==0?target:buyer,RequestedLosses=count,PayPerLoss=pay,ProfileAId=a,ProfileBId=b};
        var next=new List<LossOrder>(_lossOrders);next.Insert(0,order);
        Storage.SaveAtomic(Storage.LossOrdersPath,next);_lossOrders=next;
        RebindLossOrders();SelectLossOrder(order.Id);UpdateModernDashboard();FlashStatus("Order created. Total losses are shared across the assigned accounts.");
    }
    private void SelectDashboardLossOrder(JsonElement request)
    {
        var id=request.GetProperty("orderId").GetString()??"";
        if(!_lossOrders.Any(x=>x.Id==id))throw new InvalidDataException("Order no longer exists.");
        SelectLossOrder(id);UpdateModernDashboard();
    }
    private void DeleteDashboardLossOrder(JsonElement request)
    {
        if(_lossWorkerA?.IsArmed==true||_lossWorkerB?.IsArmed==true)throw new InvalidDataException("Stop the workers before deleting an order.");
        SelectDashboardLossOrder(request);
        var order=SelectedLossOrder();if(order is null)return;
        if(LossSettlement.HasHistory(order))throw new InvalidOperationException("This order has delivery/payment history. Use Cancel remaining losses or Close at delivered total to preserve your statistics.");
        if(MessageBox.Show($"Delete loss order for {order.BuyerName}?", "Loss Desk", MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        var next=_lossOrders.Where(x=>x.Id!=order.Id).ToList();Storage.SaveAtomic(Storage.LossOrdersPath,next);_lossOrders=next;
        RebindLossOrders();UpdateModernDashboard();
    }
}
