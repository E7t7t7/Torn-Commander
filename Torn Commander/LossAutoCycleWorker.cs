using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace TornCommander;

internal sealed class LossAutoCycleWorker : IDisposable
{
    private readonly WebView2 _webView = new();
    private readonly Label _status = new();
    private readonly string _slot;
    private readonly string _profileId;
    private readonly string _profileName;
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 250 };
    private bool _pumping;
    private bool _initialized;
    private bool _disposed;

    public Control View { get; }
    public string Slot => _slot;
    public string ProfileId => _profileId;
    public bool IsInitialized => _initialized;
    public bool IsArmed { get; private set; }
    public string LastStatus { get; private set; } = "Not initialized";

    public Func<LossAutoCycleWorker, JsonElement, Task>? MessageHandler { get; set; }

    public LossAutoCycleWorker(string slot, string profileId, string profileName)
    {
        _slot = slot;
        _profileId = profileId;
        _profileName = profileName;
        _heartbeat.Tick += async (_,_) => await PumpAsync();

        var box = new GroupBox
        {
            Width = 475,
            Height = 285,
            Text = $"Account {slot} — {profileName}",
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(24, 27, 33),
            Padding = new Padding(8)
        };

        _status.Dock = DockStyle.Top;
        _status.Height = 42;
        _status.ForeColor = Color.FromArgb(190, 198, 210);
        _status.BackColor = Color.FromArgb(24, 27, 33);
        _status.Text = "Not initialized";
        _status.AutoEllipsis = true;

        _webView.Dock = DockStyle.Fill;
        _webView.BackColor = Color.FromArgb(15, 17, 21);

        box.Controls.Add(_webView);
        box.Controls.Add(_status);
        var diagnostic=new Button { Dock=DockStyle.Top,Height=28,Text="Copy worker diagnostic" };
        diagnostic.Click+=async (_,_)=>{
            try {
                if(_webView.CoreWebView2 is null)return;
                var json=await _webView.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.TornCommanderLossCycle?.diagnostics())");
                var text=JsonSerializer.Deserialize<string>(json);
                if(!string.IsNullOrWhiteSpace(text))Clipboard.SetText(text);
            }catch(Exception ex){SetStatus("Could not copy diagnostic: "+ex.Message);}
        };
        box.Controls.Add(diagnostic);
        View = box;
    }

    public async Task InitializeAsync(CoreWebView2Environment environment)
    {
        ThrowIfDisposed();
        if (_initialized) return;

        await _webView.EnsureCoreWebView2Async(environment);
        var core = _webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 did not initialize.");
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = true;
        core.WebMessageReceived += CoreOnWebMessageReceived;
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
                SetStatus($"Navigation failed: {e.WebErrorStatus}");
        };

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "BundledScripts", "Torn_Loss_AutoCycle.js");
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Integrated loss auto-cycle script is missing.", scriptPath);

        var script = await File.ReadAllTextAsync(scriptPath);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
        // Also install it into the current about:blank document so it is available after a same-document navigation.
        try { await core.ExecuteScriptAsync(script); } catch { }

        _initialized = true;
        _heartbeat.Start();
        SetStatus("Initialized. Opening Torn…");
        _webView.Source = new Uri(TornRoutes.Home);
    }

    public async Task StartAsync(object config)
    {
        await EnsureTornDocumentAsync();
        await InvokeAsync("start", config);
        IsArmed = true;
    }

    public async Task SetAttackDelayAsync(int milliseconds) => await InvokeAsync("setAttackDelay", milliseconds);

    public async Task PauseAsync() => await InvokeNoArgAsync("pause");
    public async Task ResumeAsync() => await InvokeNoArgAsync("resume");
    public async Task StopAsync() { await InvokeNoArgAsync("stop"); IsArmed = false; }
    public async Task ResetAsync() => await InvokeNoArgAsync("reset");

    public async Task ReplyClaimAsync(string requestId, bool ok, string claimId, string message)
    {
        await EnsureReadyAsync();
        var args = string.Join(",", JsonSerializer.Serialize(requestId), ok ? "true" : "false", JsonSerializer.Serialize(claimId), JsonSerializer.Serialize(message));
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.TornCommanderLossCycle && window.TornCommanderLossCycle.onClaim({args});");
    }

    public async Task ReplyCommitAsync(string claimId, bool ok, int remaining, string message)
    {
        await EnsureReadyAsync();
        var args = string.Join(",", JsonSerializer.Serialize(claimId), ok ? "true" : "false", remaining.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonSerializer.Serialize(message));
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.TornCommanderLossCycle && window.TornCommanderLossCycle.onCommit({args});");
    }

    public async Task OpenHomeAsync()
    {
        await EnsureReadyAsync();
        _webView.Source = new Uri(TornRoutes.Home);
    }

    public async Task OpenTargetAsync(string targetId)
    {
        await EnsureReadyAsync();
        _webView.Source = new Uri($"https://www.torn.com/page.php?sid=attack&user2ID={Uri.EscapeDataString(targetId)}");
    }

    public void SetExternalStatus(string text) => SetStatus(text);

    private async void CoreOnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var text = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(text)) return;
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement.Clone();
            if (root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "tc-loss-status")
            {
                var stage = root.TryGetProperty("stage", out var stageEl) ? stageEl.GetString() ?? "" : "";
                var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "" : "";
                SetStatus(string.IsNullOrWhiteSpace(stage) ? status : $"{stage}: {status}");
            }
            if (MessageHandler is not null)
                await MessageHandler(this, root);
        }
        catch (Exception ex)
        {
            SetStatus("Worker message error: " + ex.Message);
        }
    }

    private async Task PumpAsync()
    {
        if(_disposed || !_initialized || _pumping || _webView.CoreWebView2 is null)return;
        _pumping=true;
        try { await _webView.CoreWebView2.ExecuteScriptAsync("(()=>{const w=window.TornCommanderLossCycle;if(w)w.pump();return !!w})()"); }
        catch(Exception ex){SetStatus("Worker heartbeat failed: "+ex.Message);}
        finally{_pumping=false;}
    }

    private async Task EnsureTornDocumentAsync()
    {
        await EnsureReadyAsync();
        var current = _webView.Source?.ToString() ?? "";
        if (!current.StartsWith("https://www.torn.com/", StringComparison.OrdinalIgnoreCase))
            _webView.Source = new Uri(TornRoutes.Home);
        var deadline=DateTime.UtcNow.AddSeconds(30);
        while(DateTime.UtcNow<deadline)
        {
            var ready=await _webView.CoreWebView2.ExecuteScriptAsync("!!(location.hostname==='www.torn.com' && document.readyState!=='loading' && window.TornCommanderLossCycle)");
            if(ready=="true")return;
            await Task.Delay(150);
        }
        throw new InvalidOperationException("Loss worker page did not become ready within 30 seconds. Inspect its worker browser and resolve any login or verification screen.");
    }

    private async Task InvokeNoArgAsync(string fn)
    {
        await EnsureReadyAsync();
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.TornCommanderLossCycle && window.TornCommanderLossCycle.{fn}();");
    }

    private async Task InvokeAsync(string fn, object arg)
    {
        await EnsureReadyAsync();
        var json = JsonSerializer.Serialize(arg);
        var result=await _webView.CoreWebView2.ExecuteScriptAsync($"(()=>{{const w=window.TornCommanderLossCycle;if(!w)return false;w.{fn}({json});return true}})()");
        if(result!="true")throw new InvalidOperationException("Worker script is not ready; no cycle was started.");
    }

    private Task EnsureReadyAsync()
    {
        ThrowIfDisposed();
        if (!_initialized || _webView.CoreWebView2 is null)
            throw new InvalidOperationException($"Loss worker {_slot} is not initialized.");
        return Task.CompletedTask;
    }

    private void SetStatus(string text)
    {
        LastStatus = text;
        if (_status.IsDisposed) return;
        if (_status.InvokeRequired)
        {
            try { _status.BeginInvoke(new Action(() => _status.Text = text)); } catch { }
        }
        else _status.Text = text;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LossAutoCycleWorker));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeat.Stop();
        _heartbeat.Dispose();
        try
        {
            if (_webView.CoreWebView2 is not null)
                _webView.CoreWebView2.WebMessageReceived -= CoreOnWebMessageReceived;
        }
        catch { }
        try { _webView.Dispose(); } catch { }
        try { View.Dispose(); } catch { }
    }
}
