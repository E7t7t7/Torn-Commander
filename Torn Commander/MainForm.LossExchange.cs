using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private sealed record PendingLossExchange(string Profile,string Nonce,LossExchangeListing Listing,BrowserTab Tab);
 private readonly Dictionary<string,PendingLossExchange> _lossExchangeChoices=new();
 private object? LossExchangeChoice()=>_lossExchangeChoices.TryGetValue(CurrentProfileId,out var choice)?new{nonce=choice.Nonce,buyer=choice.Listing.Buyer,target=choice.Listing.Target,remaining=choice.Listing.Remaining,pay=choice.Listing.Pay}:null;
 private async Task ReplyLossExchangeAsync(PendingLossExchange choice,bool ok,string message,string state="done"){
  if(choice.Profile!=CurrentProfileId||!_browserTabObjects.Contains(choice.Tab))return;
  try{await choice.Tab.WebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('tc-lossx-import-result',{detail:"+DashboardJson.Serialize(new{nonce=choice.Nonce,ok,message,state})+"}))");}catch{}
 }
 private async Task ImportLossExchangeAsync(BrowserTab tab,string profile,JsonElement request){
  string nonce=ReadWorkerString(request,"nonce");PendingLossExchange? choice=null;
  try{
   if(nonce.Length!=32||!nonce.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid import receipt. Refresh the Torn page.");
   if(profile!=CurrentProfileId||!_browserTabObjects.Contains(tab))throw new InvalidOperationException("Account changed. Reopen Browse contracts in the current profile.");
   var listing=LossExchangeListing.Parse(request);choice=new(profile,nonce,listing,tab);
   if(_lossExchangeChoices.TryGetValue(profile,out var previous))await ReplyLossExchangeAsync(previous,false,"Selection replaced by another listing.");
   _lossExchangeChoices[profile]=choice;if(_companionShell.TabPages.Count>0)_companionShell.SelectedIndex=0;UpdateModernDashboard();
   if(_modernDashboard?.CoreWebView2!=null)await _modernDashboard.CoreWebView2.ExecuteScriptAsync("page('losses');document.getElementById('lossXImport')?.scrollIntoView({block:'center'})");
   await ReplyLossExchangeAsync(choice,false,"Choose the loss count and account(s) in Loss Desk, then press Create order.","choose");
   FlashStatus("Loss X-change listing received. Finish the highlighted choice in Loss Desk.");
  }catch(Exception ex){
   var message=ex is InvalidDataException or InvalidOperationException?ex.Message:"Could not receive the listing. Refresh the Torn page and try again.";
   if(choice!=null)await ReplyLossExchangeAsync(choice,false,message);else try{await tab.WebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('tc-lossx-import-result',{detail:"+DashboardJson.Serialize(new{nonce,ok=false,message})+"}))");}catch{}
   FlashStatus(message);
  }
 }
 private async Task ConfirmLossExchangeAsync(string action,JsonElement request){
  string profile=ReadWorkerString(request,"profileId"),nonce=ReadWorkerString(request,"nonce");
  if(profile!=CurrentProfileId||!_lossExchangeChoices.TryGetValue(profile,out var choice)||choice.Nonce!=nonce)throw new InvalidOperationException("This listing choice expired or the account changed. Press Claim in Commander again.");
  if(action=="lossXCancel"){
   _lossExchangeChoices.Remove(profile);UpdateModernDashboard();await ReplyLossExchangeAsync(choice,false,"Cancelled; no order created.");return;
  }
  try{
   string a=ReadWorkerString(request,"accountA"),b=ReadWorkerString(request,"accountB");
   if(!_settings.Profiles.Any(p=>p.Id==a)||b!=""&&!_settings.Profiles.Any(p=>p.Id==b))throw new InvalidDataException("Choose saved Commander accounts.");
   int count=request.GetProperty("count").GetInt32();var order=choice.Listing.Create(count,a,b);
   // Repeated browser clicks/confirmations must select the existing matching account assignment.
   var existing=_lossOrders.FirstOrDefault(o=>!o.IsClosed&&o.SourceService=="Loss X-change"&&o.TargetPlayerId==order.TargetPlayerId&&o.PayPerLoss==order.PayPerLoss&&o.ProfileAId==a&&o.ProfileBId==b);
   if(existing!=null)order=existing;
   else{var next=new List<LossOrder>(_lossOrders);next.Insert(0,order);Storage.SaveAtomic(Storage.LossOrdersPath,next);_lossOrders=next;}
   _lossExchangeChoices.Remove(profile);RebindLossOrders();SelectLossOrder(order.Id);UpdateModernDashboard();RefreshLossLedger();
   await ReplyLossExchangeAsync(choice,true,"Order ready in Loss Desk. Press Start A or Start Both to run it.");
   FlashStatus("Loss X-change order ready: "+order.BuyerName+". Start A / Start Both from Loss Desk.");
  }catch(Exception ex){
   var message=ex is InvalidDataException or InvalidOperationException?ex.Message:"Order could not be saved. The choice is retained; retry after checking local storage.";
   FlashStatus(message);await ReplyLossExchangeAsync(choice,false,message,"choose");
  }
 }
}
