using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TornCommander;

internal sealed class BrowserTab : IDisposable
{
    public TabPage Page { get; }
    public WebView2 WebView { get; }
    public string InitialUrl { get; }

    private readonly AppSettings _settings;
    private readonly Action<string> _openNewTab;
    private readonly Action<string> _status;
    private readonly Action<string> _locationChanged;
    private readonly HashSet<string> _allowOnceHosts = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private readonly CancellationTokenSource _bridgeCancellation = new();

    public BrowserTab(
        string url,
        AppSettings settings,
        Action<string> openNewTab,
        Action<string> status,
        Action<string> locationChanged)
    {
        InitialUrl = TornRoutes.Normalize(url);
        _settings = settings;
        _openNewTab = openNewTab;
        _status = status;
        _locationChanged = locationChanged;

        Page = new TabPage("New tab")
        {
            BackColor = Color.FromArgb(24, 24, 27),
            Padding = new Padding(0)
        };
        WebView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(24, 24, 27)
        };
        Page.Controls.Add(WebView);
    }

    public async Task InitializeAsync(CoreWebView2Environment env)
    {
        await WebView.EnsureCoreWebView2Async(env);
        var core = WebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsWebMessageEnabled = true;

        core.WebMessageReceived += async (_, e) =>
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(message)) return;
                if (await TryHandleCommanderBridgeAsync(core, message)) return;
                _status(message);
            }
            catch { }
        };

        core.DocumentTitleChanged += (_, _) => UpdateTitle();
        core.SourceChanged += (_, _) => NotifyLocation();
        core.HistoryChanged += (_, _) => NotifyLocation();

        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            {
                e.Cancel = true;
                return;
            }

            var fixedUri = TornRoutes.FixLegacyTornRoute(uri);
            if (!fixedUri.Equals(uri))
            {
                e.Cancel = true;
                _status($"Updated old Torn route → {fixedUri.PathAndQuery}");
                core.Navigate(fixedUri.ToString());
                return;
            }

            if (IsAllowed(uri))
            {
                _status(uri.Host);
                return;
            }

            e.Cancel = true;
            var choice = NavigationPrompt.Show(Page.FindForm(), uri);
            switch (choice)
            {
                case NavigationChoice.AllowOnce:
                    _allowOnceHosts.Add(uri.Host);
                    core.Navigate(uri.ToString());
                    break;
                case NavigationChoice.AlwaysAllow:
                    AddUserWhitelist(uri.Host);
                    core.Navigate(uri.ToString());
                    break;
                case NavigationChoice.OpenExternal:
                    Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
                    break;
                default:
                    _status($"Blocked: {uri.Host}");
                    break;
            }
        };

        core.NavigationCompleted += (_, e) =>
        {
            NotifyLocation();
            if (!e.IsSuccess)
                _status($"Page load failed: {e.WebErrorStatus}");
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(e.Uri))
                _openNewTab(TornRoutes.Normalize(e.Uri));
        };

        await InjectLocalScriptsAsync();
        core.Navigate(InitialUrl);
    }

    public async Task<string?> ExecuteScriptAsync(string script)
    {
        if (_disposed || WebView.CoreWebView2 is null) return null;
        try { return await WebView.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception ex)
        {
            _status("Commander script failed: " + ex.Message);
            return null;
        }
    }

    private async Task<bool> TryHandleCommanderBridgeAsync(CoreWebView2 core, string message)
    {
        if (!message.StartsWith("{", StringComparison.Ordinal)) return false;
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "tc-gm-xhr") return false;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            var url = root.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            var method = root.TryGetProperty("method", out var methodEl) ? methodEl.GetString() ?? "GET" : "GET";
            var data = root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String ? dataEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return true;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var req = new HttpRequestMessage(new HttpMethod(method), uri);
            if (data is not null) req.Content = new StringContent(data, System.Text.Encoding.UTF8);
            if (root.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
            {
                foreach (var h in headers.EnumerateObject())
                {
                    var value = h.Value.ValueKind == JsonValueKind.String ? h.Value.GetString() ?? "" : h.Value.ToString();
                    if (!req.Headers.TryAddWithoutValidation(h.Name, value) && req.Content is not null)
                        req.Content.Headers.TryAddWithoutValidation(h.Name, value);
                }
            }

            object payload;
            try
            {
                if (uri.Scheme == "https" && uri.Host.Equals("api.torn.com", StringComparison.OrdinalIgnoreCase))
                {
                    var packet = await TornApiTransport.SendAsync(http, req, _bridgeCancellation.Token);
                    payload = new { status = packet.Status, statusText = packet.Reason, responseText = packet.Body,
                        response = packet.Body, finalUrl = packet.FinalUrl, readyState = 4, responseHeaders = packet.Headers };
                }
                else
                {
                    using var res = await http.SendAsync(req, _bridgeCancellation.Token);
                    var text = await res.Content.ReadAsStringAsync(_bridgeCancellation.Token);
                    var responseHeaders = string.Join("\n", res.Headers.Concat(res.Content.Headers).Select(h => h.Key + ": " + string.Join(", ", h.Value)));
                    payload = new
                    {
                        status = (int)res.StatusCode,
                        statusText = res.ReasonPhrase ?? "",
                        responseText = text,
                        response = text,
                        finalUrl = res.RequestMessage?.RequestUri?.ToString() ?? url,
                        readyState = 4,
                        responseHeaders
                    };
                }
            }
            catch (Exception ex)
            {
                payload = new { status = 0, statusText = "", responseText = "", response = "", finalUrl = url, readyState = 4, responseHeaders = "", error = ex.Message };
            }

            if (_disposed) return true;
            var idJson = JsonSerializer.Serialize(id);
            var payloadJson = JsonSerializer.Serialize(payload);
            await core.ExecuteScriptAsync($"window.__tcGmXhrResolve && window.__tcGmXhrResolve({idJson}, {payloadJson});");
            return true;
        }
        catch { return true; }
    }

    private void UpdateTitle()
    {
        if (WebView.CoreWebView2 is null) return;
        var title = WebView.CoreWebView2.DocumentTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            try { title = new Uri(WebView.Source?.ToString() ?? TornRoutes.Home).Host; }
            catch { title = "Torn"; }
        }
        Page.Text = Shorten(title, 20);
        Page.ToolTipText = title;
    }

    private void NotifyLocation()
    {
        var url = WebView.Source?.ToString();
        if (!string.IsNullOrWhiteSpace(url))
            _locationChanged(url);
    }

    private async Task InjectLocalScriptsAsync()
    {
        Directory.CreateDirectory(Storage.UserScriptsPath);
        foreach (var name in _settings.EnabledScripts.ToList())
        {
            try
            {
                var path = Path.Combine(Storage.UserScriptsPath, name);
                if (!File.Exists(path) || !Storage.IsSupportedScriptFile(path)) continue;
                var script = await File.ReadAllTextAsync(path);
                var payload = WrapUserscriptForMetadata(script, name);
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(payload);
            }
            catch (Exception ex)
            {
                // A broken helper must never prevent the browser from opening.
                _status($"Userscript skipped: {name} ({ex.Message})");
            }
        }
    }

    private static string WrapUserscriptForMetadata(string script, string name)
    {
        // WebView2 can inject JavaScript directly, but it is not a full Greasemonkey runtime.
        // Honor the most important userscript metadata ourselves so a @match script does not
        // accidentally run on every Torn/Google/helper page in Commander. @grant none scripts
        // are the most compatible; scripts requiring GM_* APIs may still need a real extension.
        var matches = ReadMetadata(script, "@match")
            .Concat(ReadMetadata(script, "@include"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var excludes = ReadMetadata(script, "@exclude-match")
            .Concat(ReadMetadata(script, "@exclude"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0 && excludes.Count == 0) return script;

        var matchRegexes = matches.Select(GlobToRegex).ToList();
        var excludeRegexes = excludes.Select(GlobToRegex).ToList();
        var matchJson = JsonSerializer.Serialize(matchRegexes);
        var excludeJson = JsonSerializer.Serialize(excludeRegexes);
        var nameJson = JsonSerializer.Serialize(name);

        var gmCompat = @"
const GM_addStyle = (css) => { const s=document.createElement('style'); s.textContent=css; (document.head||document.documentElement).appendChild(s); return s; };
const GM_getValue = (k,d=null) => { try { const v=localStorage.getItem('__tc_gm_'+__tc_name+'_'+k); return v===null?d:JSON.parse(v); } catch { return d; } };
const GM_setValue = (k,v) => { try { localStorage.setItem('__tc_gm_'+__tc_name+'_'+k, JSON.stringify(v)); } catch {} };
const GM_deleteValue = (k) => { try { localStorage.removeItem('__tc_gm_'+__tc_name+'_'+k); } catch {} };
const GM_listValues = () => { try { const p='__tc_gm_'+__tc_name+'_'; return Object.keys(localStorage).filter(k=>k.startsWith(p)).map(k=>k.slice(p.length)); } catch { return []; } };
const GM_openInTab = (u) => { window.open(u, '_blank'); };
if (!window.__tcGmXhrPending) window.__tcGmXhrPending = {};
if (!window.__tcGmXhrResolve) window.__tcGmXhrResolve = (id,res) => { const d=window.__tcGmXhrPending[id]; if(!d) return; delete window.__tcGmXhrPending[id]; try { if(res && res.status >= 200 && res.status < 400) d.onload?.(res); else d.onerror?.(res||{status:0,readyState:4}); d.onreadystatechange?.(res||{status:0,readyState:4}); } catch {} };
const GM_xmlhttpRequest = (d) => {
  const id='tcxhr_'+Date.now()+'_'+Math.random().toString(36).slice(2);
  if (window.chrome && window.chrome.webview) {
    window.__tcGmXhrPending[id]=d;
    window.chrome.webview.postMessage(JSON.stringify({type:'tc-gm-xhr',id,method:d.method||'GET',url:d.url,headers:d.headers||{},data:d.data||null,timeout:d.timeout||30000,anonymous:!!d.anonymous}));
    return {abort:()=>{delete window.__tcGmXhrPending[id];}};
  }
  const ctl=new AbortController();
  fetch(d.url,{method:d.method||'GET',headers:d.headers||{},body:d.data,signal:ctl.signal,credentials:d.anonymous?'omit':'same-origin'})
    .then(async r=>{ const t=await r.text(); const o={status:r.status,statusText:r.statusText,responseText:t,response:t,finalUrl:r.url,readyState:4,responseHeaders:[...r.headers].map(x=>x.join(': ')).join('\n')}; d.onload?.(o); d.onreadystatechange?.(o); })
    .catch(e=>d.onerror?.({error:String(e),status:0,readyState:4}));
  return {abort:()=>ctl.abort()};
};
const GM_registerMenuCommand = (label,fn) => 0;
const GM = { addStyle:GM_addStyle, getValue:async(k,d)=>GM_getValue(k,d), setValue:async(k,v)=>GM_setValue(k,v), deleteValue:async(k)=>GM_deleteValue(k), listValues:async()=>GM_listValues(), xmlHttpRequest:GM_xmlhttpRequest, openInTab:GM_openInTab, registerMenuCommand:GM_registerMenuCommand };
";

        return "(() => {\n"
             + $"const __tc_name = {nameJson};\n"
             + $"const __tc_matches = {matchJson};\n"
             + $"const __tc_excludes = {excludeJson};\n"
             + "const __tc_url = location.href;\n"
             + "const __tc_test = (p) => { try { return new RegExp(p, 'i').test(__tc_url); } catch { return false; } };\n"
             + "if ((__tc_matches.length === 0 || __tc_matches.some(__tc_test)) && !__tc_excludes.some(__tc_test)) {\n"
             + gmCompat + "\n"
             + "try {\n" + script + "\n} catch (e) { console.error('Torn Commander userscript failed:', __tc_name, e); }\n"
             + "}\n})();";
    }

    private static IEnumerable<string> ReadMetadata(string script, string key)
    {
        foreach (var raw in script.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("//", StringComparison.Ordinal)) continue;
            line = line[2..].Trim();
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Length > key.Length && !char.IsWhiteSpace(line[key.Length])) continue;
            var value = line[key.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }
    }

    private static string GlobToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern.Trim()).Replace("\\*", ".*");
        return "^" + escaped + "$";
    }

    private bool IsAllowed(Uri uri)
    {
        if (uri.Scheme is "about" or "data" or "blob") return true;
        if (uri.Scheme is not ("http" or "https")) return false;

        var host = uri.Host.ToLowerInvariant();
        if (_allowOnceHosts.Contains(host)) return true;
        if (TornRoutes.IsTornHost(host)) return true;

        foreach (var authDomain in new[]
        {
            "google.com", "gstatic.com", "googleusercontent.com",
            "accounts.google.com", "discord.com", "discord.gg", "discordapp.com",
            "cloudflare.com", "challenges.cloudflare.com"
        })
            if (DomainMatches(host, authDomain)) return true;

        if (_settings.UserWhitelistDomains.Any(d => DomainMatches(host, NormalizeDomain(d))))
            return true;

        if (!_settings.AllowHelperSites) return false;

        foreach (var helperDomain in new[]
        {
            "torn.report", "yata.yt", "chatgpt.com", "openai.com",
            "greasyfork.org", "github.com", "githubusercontent.com",
            "tornlabs.de", "chromewebstore.google.com", "microsoftedge.microsoft.com",
            "api-playground.torn.com"
        })
            if (DomainMatches(host, helperDomain)) return true;

        return false;
    }

    private void AddUserWhitelist(string host)
    {
        var domain = NormalizeDomain(host);
        if (string.IsNullOrWhiteSpace(domain)) return;
        if (!_settings.UserWhitelistDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
        {
            _settings.UserWhitelistDomains.Add(domain);
            _settings.UserWhitelistDomains = _settings.UserWhitelistDomains
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
            Storage.SaveSettings(_settings);
        }
        _status($"Whitelisted: {domain}");
    }

    private static string NormalizeDomain(string value)
    {
        var s = value.Trim().ToLowerInvariant();
        if (s.StartsWith("http://") || s.StartsWith("https://"))
        {
            if (Uri.TryCreate(s, UriKind.Absolute, out var u)) s = u.Host;
        }
        s = s.Trim('.');
        if (s.StartsWith("*.")) s = s[2..];
        return s;
    }

    private static bool DomainMatches(string host, string domain)
        => !string.IsNullOrWhiteSpace(domain)
           && (host.Equals(domain, StringComparison.OrdinalIgnoreCase)
               || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    private static string Shorten(string s, int length)
        => s.Length <= length ? s : s[..Math.Max(1, length - 1)] + "…";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bridgeCancellation.Cancel();
        _bridgeCancellation.Dispose();
        try { WebView.Dispose(); } catch { }
        try { Page.Dispose(); } catch { }
    }
}
