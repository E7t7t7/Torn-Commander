using System.Text.Json;
namespace TornCommander;
public sealed partial class MainForm{
 private async Task ReserveFFBoth(JsonElement root){
  if(_ffBatchBusy)throw new InvalidOperationException("A two-account reservation is already open.");
  _ffBatchBusy=true;var held=new List<string>();
  try{
   string order=ReadWorkerString(root,"order");
   var profiles=_settings.Profiles.Where(p=>FFFor(p.Id).Player>0&&!string.IsNullOrEmpty(CredentialManager.LoadSellerKey(p.Id))).ToList();
   if(profiles.Count<2)throw new InvalidOperationException("Connect a separate FFScouter seller key under each of two account profiles first.");
   using var dialog=new Form{Text="Reserve independent claims on two accounts",ClientSize=new Size(570,300),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=PanelBg,ForeColor=TextMain,AutoScaleMode=AutoScaleMode.Dpi};
   var a=new ComboBox{Left=180,Top=55,Width=365,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=FieldBg,ForeColor=TextMain};
   var b=new ComboBox{Left=180,Top=95,Width=365,DropDownStyle=ComboBoxStyle.DropDownList,BackColor=FieldBg,ForeColor=TextMain};
   foreach(var p in profiles){a.Items.Add(p.Name+" ["+p.Id+"]");b.Items.Add(p.Name+" ["+p.Id+"]");}
   a.SelectedIndex=Math.Max(0,profiles.FindIndex(p=>p.Id==CurrentProfileId));b.SelectedIndex=a.SelectedIndex==0?1:0;
   var energy=new CheckBox{Text="Use each account's current energy / 25",Checked=true,Left=16,Top=140,Width=520};
   var count=new NumericUpDown{Left=350,Top=174,Width=130,Minimum=1,Maximum=1000000,Value=1,Enabled=false,BackColor=FieldBg,ForeColor=TextMain};
   energy.CheckedChanged+=(_,_)=>count.Enabled=!energy.Checked;
   var review=new Button{Text="Check both accounts and review…",Left=16,Top=245,Width=330,DialogResult=DialogResult.OK,BackColor=FieldBg,ForeColor=TextMain,FlatStyle=FlatStyle.Flat};
   var cancel=new Button{Text="Cancel",Left=365,Top=245,Width=175,DialogResult=DialogResult.Cancel,BackColor=FieldBg,ForeColor=TextMain,FlatStyle=FlatStyle.Flat};
   dialog.Controls.AddRange(new Control[]{new Label{Text="Contract "+order+" · two real remote reservations",Left=16,Top=15,Width=530,Height=28},new Label{Text="First account",Left=16,Top=58,Width=155},a,new Label{Text="Second account",Left=16,Top=98,Width=155},b,energy,new Label{Text="Or up to this count on each account",Left=16,Top=177,Width=320},count,new Label{Text="One active claim per player. Each needs its own key. The second claim can fail independently; saved auto-run settings still apply.",Left=16,Top=207,Width=530,Height=35},review,cancel});
   dialog.AcceptButton=review;dialog.CancelButton=cancel;
   if(dialog.ShowDialog(this)!=DialogResult.OK)return;
   if(a.SelectedIndex==b.SelectedIndex)throw new InvalidOperationException("Choose two different account profiles.");
   var first=profiles[a.SelectedIndex];var second=profiles[b.SelectedIndex];
   foreach(var p in new[]{first,second}){
    if(_ffControlBusy.Contains(p.Id)||!_ffBusy.Add(p.Id))throw new InvalidOperationException(p.Name+" is busy. Wait for its current operation before reserving on both.");
    held.Add(p.Id);
   }
   await SyncFFCore(first.Id);await SyncFFCore(second.Id);
   int? energyA=null,energyB=null;
   if(energy.Checked){FFActivity(first.Id,"Checking energy for both accounts…");energyA=await FFEnergy(first.Id);energyB=await FFEnergy(second.Id);}
   int wantedA=energyA.HasValue?energyA.Value/25:(int)count.Value,wantedB=energyB.HasValue?energyB.Value/25:(int)count.Value;
   var plan=FFSellerBatch.Plan(FFFor(first.Id),FFFor(second.Id),order,wantedA,wantedB,DateTime.UtcNow);
   string preview=first.Name+": up to "+plan.First+" losses"+(energyA.HasValue?" from "+energyA+" energy":"")+"\n"+second.Name+": up to "+plan.Second+" losses"+(energyB.HasValue?" from "+energyB+" energy":"")+"\n\nReserve these two independent claims? Timers start immediately. If slots decrease, the second count may be lower. A failed/uncertain second request will not undo or repeat the first.";
   if(MessageBox.Show(this,preview,"Confirm two-account reservation",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
   var result=await FFSellerBatch.Reserve(first.Id,second.Id,FFFor(first.Id),FFFor(second.Id),order,plan.First,plan.Second,
    async()=>{await SyncFFCore(second.Id);return FFFor(second.Id);},
    async(profile,quantity)=>{
     var before=FFFor(profile).Claims.Keys.ToHashSet();
     await FFMutation(profile,"claim",order,quantity);
     var actual=FFFor(profile).Claims.Values.Single(c=>!before.Contains(c.Id));
     return(actual.Id,actual.Claimed);
    });
   string summary=string.Join(" · ",result.Confirmed.Select(c=>profiles.First(p=>p.Id==c.Profile).Name+": "+c.Count+" reserved (claim "+c.ClaimId+")"));
   if(result.FailedProfile!="")summary+=(summary.Length>0?" · ":"")+profiles.First(p=>p.Id==result.FailedProfile).Name+": "+result.Error+" No automatic retry or rollback.";
   foreach(var p in new[]{first,second}){FFFor(p.Id).Note=summary;SaveFF(p.Id);}
   FlashStatus(summary);MessageBox.Show(this,summary,"Two-account reservation result");
  }catch(Exception ex){var note=ex is InvalidOperationException or InvalidDataException or FFSellerException?ex.Message:"Two-account reservation failed. Review both accounts before retrying.";FFFor(CurrentProfileId).Note=note;try{SaveFF(CurrentProfileId);}catch{}FlashStatus(note);}
  finally{foreach(var profile in held){_ffBusy.Remove(profile);_ffActivity.Remove(profile);}_ffBatchBusy=false;PushFF();RefreshLossLedger();UpdateLossDashboardStatus();}
 }
}
