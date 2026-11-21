using System.Text.Json;
namespace TornCommander;

public sealed partial class MainForm
{
    private async Task SetPaintOverlayAsync(bool show){
        var tab=Active();var uri=tab?.WebView.Source;
        if(tab==null||uri==null||uri.Scheme!="https"||!TornRoutes.IsTornHost(uri.Host)){FlashStatus("Open Torn in the game browser before changing its paint overlay.");return;}
        string profile=CurrentProfileId;
        var raw=await tab.ExecuteScriptAsync("(()=>{const show="+(show?"true":"false")+";if(window.__TornCommanderGraffiti?.setOverlayVisible)return window.__TornCommanderGraffiti.setOverlayVisible(show);try{localStorage.setItem('tcGraffitiPaintOverlayHiddenV1',show?'0':'1');return {saved:true};}catch{return {saved:false};}})()");
        bool accepted=false;try{using var d=JsonDocument.Parse(raw??"null");accepted=d.RootElement.ValueKind==JsonValueKind.Object&&((d.RootElement.TryGetProperty("visible",out var v)&&v.ValueKind is JsonValueKind.True or JsonValueKind.False&&v.GetBoolean()==show)||(d.RootElement.TryGetProperty("saved",out var saved)&&saved.ValueKind==JsonValueKind.True));}catch{}
        if(!accepted){FlashStatus("Paint visibility could not be saved on this page. Open Graffiti and try again.");return;}
        if(profile==CurrentProfileId)FlashStatus(show?"Paint overlay enabled for this browser profile. Open Graffiti to see it.":"Paint overlay hidden for this browser profile. Restore it from Crimes → Show paint window.");
    }
    private bool _guideActionBusy;
    private BrowserTab? _guideCommandTab;
    private string? _guideCommandId;

    private async Task ExecuteRecommendedActionAsync(bool openOnly, bool dashboardRunOnce = false)
    {
        if (_guideActionBusy) { FlashStatus("A guided command is already running. Wait for its result."); return; }
        var profile = CurrentProfileId;
        _guideActionBusy = true;
        UpdateModernDashboard();
        try { await ExecuteRecommendedActionCoreAsync(openOnly, dashboardRunOnce, profile); }
        catch (Exception ex)
        {
            if (profile == CurrentProfileId) _lastGraffitiActionStatus = "STOPPED: " + ex.Message + ". No retry was sent.";
        }
        finally { _guideActionBusy = false; UpdateModernDashboard(); }
    }

    private async Task CancelPendingGraffitiAsync()
    {
        if (_guideCommandTab is { } tab && _guideCommandId is { } id)
            await tab.ExecuteScriptAsync($"window.__TornCommanderGraffiti?.cancelGuideAction({JsonSerializer.Serialize(id)})");
    }

    private async Task<string?> ExecuteGraffitiCommandAsync(BrowserTab tab, string target, string paint, bool runOnce, string profile)
    {
        var id = Guid.NewGuid().ToString("N");
        _guideCommandTab = tab; _guideCommandId = id;
        var idJson = JsonSerializer.Serialize(id);
        try
        {
            var ack = await tab.ExecuteScriptAsync($"window.__TornCommanderGraffiti.startGuideAction({idJson},{JsonSerializer.Serialize(target)},{JsonSerializer.Serialize(paint)},{(runOnce ? "true" : "false")})");
            if (string.IsNullOrWhiteSpace(ack) || ack == "null")
                return "{\"ok\":false,\"reason\":\"Command acknowledgement unavailable; no retry\"}";
            using (var doc = JsonDocument.Parse(ack))
            {
                if (!doc.RootElement.TryGetProperty("started", out var started) || started.ValueKind != JsonValueKind.True)
                    return ack;
            }
            var deadline = DateTime.UtcNow.AddSeconds(26);
            while (DateTime.UtcNow < deadline)
            {
                if (profile != CurrentProfileId) return null;
                var raw = await tab.ExecuteScriptAsync($"window.__TornCommanderGraffiti?.getGuideAction({idJson})");
                if (string.IsNullOrWhiteSpace(raw) || raw == "null")
                    return "{\"ok\":false,\"reason\":\"Page or helper changed while command was pending; no retry\"}";
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True &&
                    doc.RootElement.TryGetProperty("result", out var result)) return result.GetRawText();
                await Task.Delay(200);
            }
            return "{\"ok\":false,\"reason\":\"Command timed out; pending clicks cancelled, no retry. Check Torn for any result already received\"}";
        }
        finally
        {
            await tab.ExecuteScriptAsync($"window.__TornCommanderGraffiti?.cancelGuideAction({idJson})");
            if (_guideCommandId == id) { _guideCommandId = null; _guideCommandTab = null; }
        }
    }
}
