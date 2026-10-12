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
    private bool _initialized;
    private bool _disposed;

    public Control View { get; }
    public string Slot => _slot;
    public string ProfileId => _profileId;
    public bool IsInitialized => _initialized;
    public string LastStatus { get; private set; } = "Not initialized";

    public Func<LossAutoCycleWorker, JsonElement, Task>? MessageHandler { get; set; }

    public LossAutoCycleWorker(string slot, string profileId, string profileName)
    {
        _slot = slot;
        _profileId = profileId;
        _profileName = profileName;

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
        SetStatus("Initialized. Opening Torn…");
        _webView.Source = new Uri(TornRoutes.Home);
    }

    public async Task StartAsync(object config)
    {
        await EnsureTornDocumentAsync();
        await InvokeAsync("start", config);
    }

    public async Task PauseAsync() => await InvokeNoArgAsync("pause");
    public async Task ResumeAsync() => await InvokeNoArgAsync("resume");
    public async Task StopAsync() => await InvokeNoArgAsync("stop");
    public async Task ResetAsync() => await InvokeNoArgAsync("reset");

    public async Task ReplyClaimAsync(string requestId, bool ok, string claimId, string message)
    {
        await EnsureReadyAsync();
        var args = string.Join(",", JsonSerializer.Serialize(requestId), ok ? "true" : "false", JsonSerializer.Serialize(claimId), JsonSerializer.Serialize(message));
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.TornCommanderLossCycle && window.TornCommanderLossCycle.onClaim({args});");
    }

    public async Task ReplyCommitAsync(bool ok, int remaining, string message)
    {
        await EnsureReadyAsync();
        var args = string.Join(",", ok ? "true" : "false", remaining.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonSerializer.Serialize(message));
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

    private async Task EnsureTornDocumentAsync()
    {
        await EnsureReadyAsync();
        var current = _webView.Source?.ToString() ?? "";
        if (current.StartsWith("https://www.torn.com/", StringComparison.OrdinalIgnoreCase)) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
        _webView.CoreWebView2.NavigationCompleted += Handler;
        try
        {
            _webView.Source = new Uri(TornRoutes.Home);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await tcs.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            _webView.CoreWebView2.NavigationCompleted -= Handler;
        }
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
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.TornCommanderLossCycle && window.TornCommanderLossCycle.{fn}({json});");
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
