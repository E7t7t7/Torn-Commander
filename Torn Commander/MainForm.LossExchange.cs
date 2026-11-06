using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm {
 private bool _lossImportBusy;
 private async Task ImportLossExchangeAsync(BrowserTab tab,string profile,JsonElement request){
  string nonce=ReadWorkerString(request,"nonce");object response;bool ownsDialog=false;
  try{
   if(_lossImportBusy)throw new InvalidOperationException("Another contract choice is already open.");
   if(profile!=CurrentProfileId||!_browserTabObjects.Contains(tab))throw new InvalidOperationException("Account changed. Reopen Browse contracts in the current profile.");
   var listing=LossExchangeListing.Parse(request);
   var existing=listing.Existing(_lossOrders);
   if(existing!=null){SelectLossOrder(existing.Id);UpdateModernDashboard();response=new{nonce,ok=true,message="Existing order selected: "+existing.Id};}
   else{
    _lossImportBusy=true;ownsDialog=true;
    using var dialog=new Form{Text="Claim in Commander",ClientSize=new Size(520,310),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=PanelBg,ForeColor=TextMain,AutoScaleMode=AutoScaleMode.Dpi};
    var title=new Label{Text=listing.Buyer+" · "+listing.Remaining+" losses available · $"+listing.Pay.ToString("N0")+"/loss quoted",Left=16,Top=16,Width=485,Height=40};
    var note=new Label{Text="Creates a local order. Public availability can change; the exchange does not reserve these losses. Start the worker from Loss Desk when ready.",Left=16,Top=58,Width=485,Height=44};
    var count=new NumericUpDown{BackColor=FieldBg,ForeColor=TextMain,Minimum=1,Maximum=listing.Remaining,Value=listing.Remaining,Left=180,Top=111,Width=140};
    var a=new ComboBox{BackColor=FieldBg,ForeColor=TextMain,Left=180,Top=151,Width=300,DropDownStyle=ComboBoxStyle.DropDownList};
    var b=new ComboBox{BackColor=FieldBg,ForeColor=TextMain,Left=180,Top=191,Width=300,DropDownStyle=ComboBoxStyle.DropDownList};
    foreach(var p in _settings.Profiles){a.Items.Add(p.Name+" ["+p.Id+"]");b.Items.Add(p.Name+" ["+p.Id+"]");}
    a.SelectedIndex=Math.Max(0,_settings.Profiles.FindIndex(p=>p.Id==profile));b.SelectedIndex=_settings.Profiles.Count>1?(a.SelectedIndex==0?1:0):0;
    string mode="";var onlyA=new Button{Text="First account only",Left=16,Top=249,Width=150};
    var onlyB=new Button{Text="Second account only",Left=174,Top=249,Width=150,Enabled=_settings.Profiles.Count>1};
    var both=new Button{Text="Both accounts",Left=332,Top=249,Width=150,Enabled=_settings.Profiles.Count>1};
    void Choose(string selected){if(selected=="both"&&a.SelectedIndex==b.SelectedIndex){MessageBox.Show(dialog,"Choose different accounts for a dual order.");return;}mode=selected;dialog.DialogResult=DialogResult.OK;}
    onlyA.Click+=(_,_)=>Choose("a");onlyB.Click+=(_,_)=>Choose("b");both.Click+=(_,_)=>Choose("both");
    dialog.Controls.AddRange(new Control[]{title,note,new Label{Text="Losses to deliver",Left=16,Top=113,Width=160},count,new Label{Text="First account",Left=16,Top=153,Width=160},a,new Label{Text="Second account",Left=16,Top=193,Width=160},b,onlyA,onlyB,both});
    if(dialog.ShowDialog(this)!=DialogResult.OK){response=new{nonce,ok=false,message="Claim cancelled; no order created."};}
    else{
     if(profile!=CurrentProfileId||!_browserTabObjects.Contains(tab))throw new InvalidOperationException("Account changed during selection; no order created.");
     var order=listing.Create((int)count.Value,_settings.Profiles[mode=="b"?b.SelectedIndex:a.SelectedIndex].Id,mode=="both"?_settings.Profiles[b.SelectedIndex].Id:"");
     var next=new List<LossOrder>(_lossOrders);next.Insert(0,order);Storage.SaveAtomic(Storage.LossOrdersPath,next);_lossOrders=next;
     RebindLossOrders();SelectLossOrder(order.Id);UpdateModernDashboard();RefreshLossLedger();
     if(_modernDashboard?.CoreWebView2!=null)await _modernDashboard.CoreWebView2.ExecuteScriptAsync("page('losses')");
     response=new{nonce,ok=true,message="Order ready in Loss Desk: "+order.Id};
    }
   }
  }catch(Exception ex){response=new{nonce,ok=false,message=ex is InvalidDataException or InvalidOperationException?ex.Message:"Contract import failed. No automatic fight started; refresh and try again."};}
  finally{if(ownsDialog)_lossImportBusy=false;}
  if(profile==CurrentProfileId&&_browserTabObjects.Contains(tab))await tab.WebView.CoreWebView2.ExecuteScriptAsync("window.dispatchEvent(new CustomEvent('tc-lossx-import-result',{detail:"+DashboardJson.Serialize(response)+"}))");
 }
}
