using System.Text.Json;
namespace TornCommander;

public sealed partial class MainForm
{
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
