using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm
{
    private TravelLandingSnapshot? _arrivalSnapshot;
    private TravelLandingAdvice _arrivalAdvice = new();
    private string _arrivalProfileId = "";
    private DateTime _arrivalLastScanUtc = DateTime.MinValue;
    private bool _arrivalScanning;
    private readonly List<TravelStockChange> _arrivalStockChanges = new();
    private string _arrivalChangeProfile = "";

    private void ClearTravelArrival()
    {
        _arrivalSnapshot = null;
        _arrivalAdvice = new TravelLandingAdvice();
        _arrivalProfileId = CurrentProfileId;
        _arrivalLastScanUtc = DateTime.MinValue;
        _arrivalStockChanges.Clear();
        _arrivalChangeProfile = CurrentProfileId;
    }

    private async Task ScanTravelShopAsync(bool explicitRequest = false)
    {
        if (_arrivalScanning) return;
        if (_arrivalProfileId != CurrentProfileId) ClearTravelArrival();
        var tab = Active();
        var url = tab?.WebView.Source?.ToString() ?? "";
        if (tab is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("www.torn.com", StringComparison.OrdinalIgnoreCase) ||
            !((uri.AbsolutePath.EndsWith("page.php", StringComparison.OrdinalIgnoreCase) && uri.Query.Contains("sid=travel", StringComparison.OrdinalIgnoreCase)) ||
              uri.AbsolutePath.EndsWith("travelagency.php", StringComparison.OrdinalIgnoreCase)))
        {
            if (explicitRequest) { _arrivalAdvice = new TravelLandingAdvice { Action = "Open overseas shop", Message = "Choose the Torn travel/foreign shop browser tab first, then press Scan Shop. No other browser page was read." }; UpdateModernDashboard(); }
            return;
        }
        _arrivalScanning = true;
        var profile = CurrentProfileId;
        try
        {
            // Only visible shop table fields and the public page's cash/capacity banner are read.
            // No chat messages, account name, cookies or login tokens are collected.
            var raw = await tab.ExecuteScriptAsync("""
              (() => {
              const out={country:'',cash:null,purchased:null,capacity:null,items:[]};
              const banner=[...document.querySelectorAll('div,span,p')].map(e=>e.textContent?.replace(/\s+/g,' ').trim()||'')
                .find(t=>t.length<260 && /You are in .+ and have \$[\d,]+/i.test(t) && /purchased \d+\s*\/\s*\d+/i.test(t));
              if(!banner)return {error:'Foreign shop banner not found. Open the overseas shop while abroad.'};
              const m=banner.match(/You are in (.+?) and have \$([\d,]+).*?purchased (\d+)\s*\/\s*(\d+)/i);
              if(!m)return {error:'Shop banner did not contain a reliable balance/capacity.'};
              out.country=m[1].trim();out.cash=Number(m[2].replace(/,/g,''));out.purchased=Number(m[3]);out.capacity=Number(m[4]);
              for(const table of document.querySelectorAll('table')){
                const heads=[...table.querySelectorAll('thead th')].map(e=>e.textContent?.trim().toLowerCase()||'');
                const ix={name:heads.indexOf('name'),cost:heads.indexOf('cost'),stock:heads.indexOf('stock')};
                if(Object.values(ix).some(v=>v<0))continue;
                for(const tr of table.querySelectorAll('tbody tr')){
                  const cells=[...tr.querySelectorAll(':scope > td')];
                  if(cells.length<=Math.max(...Object.values(ix)))continue;
                  const name=cells[ix.name].textContent?.replace(/\s+/g,' ').trim()||'';
                  const costText=cells[ix.cost].textContent?.trim()||'';
                  const stockText=cells[ix.stock].textContent?.trim()||'';
                  const money=costText.match(/\$([\d,]+)/),qty=stockText.match(/^([\d,]+)/);
                  if(!name || !money || !qty)continue;
                  const cost=Number(money[1].replace(/,/g,'')),stock=Number(qty[1].replace(/,/g,''));
                  if(Number.isFinite(cost)&&Number.isFinite(stock)&&cost>=0&&stock>=0)
                    out.items.push({name,stock,buyPrice:cost});
                }
              }
              return out;
              })()
              """);
            if (profile != CurrentProfileId) return;
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (explicitRequest) _arrivalAdvice = new TravelLandingAdvice { Status = "Page not ready", Action = "Scan again", Message = error.GetString() ?? "Shop not ready" };
                UpdateModernDashboard(); return;
            }
            var snapshot = JsonSerializer.Deserialize<TravelLandingSnapshot>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Country) || snapshot.Capacity <= 0) return;
            snapshot.SeenUtc = DateTime.UtcNow;
            if (_arrivalChangeProfile != profile)
            {
                _arrivalStockChanges.Clear();
                _arrivalChangeProfile = profile;
            }
            if (_arrivalSnapshot is not null && _arrivalSnapshot.Country.Equals(snapshot.Country, StringComparison.OrdinalIgnoreCase))
            {
                var oldStock = _arrivalSnapshot.Items.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Stock), StringComparer.OrdinalIgnoreCase);
                foreach (var item in snapshot.Items)
                {
                    if (!oldStock.TryGetValue(item.Name, out var was) || item.Stock <= was) continue;
                    // Replenishment is *observed* between two scans, not at a known server instant.
                    _arrivalStockChanges.Add(new TravelStockChange {
                        Country = snapshot.Country, Item = item.Name, Before = was, After = item.Stock,
                        PreviousScanUtc = _arrivalSnapshot.SeenUtc, DetectedUtc = snapshot.SeenUtc
                    });
                }
            }
            else _arrivalStockChanges.Clear();
            _arrivalStockChanges.RemoveAll(x => x.DetectedUtc < snapshot.SeenUtc.AddMinutes(-15));
            _arrivalSnapshot = snapshot;
            _arrivalLastScanUtc = snapshot.SeenUtc;
            _arrivalAdvice = TravelArrivalAdvisor.Evaluate(snapshot, _travelForecast.Items, _travelHistory,
                _settings.TravelApplyMarketTax, snapshot.SeenUtc, _arrivalStockChanges);
            UpdateModernDashboard();
            if (explicitRequest) FlashStatus("Foreign shop scanned: " + _arrivalAdvice.Action);
            else if (_arrivalStockChanges.Any(x => x.DetectedUtc == snapshot.SeenUtc))
                FlashStatus("Foreign stock replenished! " + string.Join(", ", _arrivalStockChanges
                    .Where(x => x.DetectedUtc == snapshot.SeenUtc).Select(x => x.Item).Take(3)));
        }
        catch (Exception ex)
        {
            if (explicitRequest) { _arrivalAdvice = new TravelLandingAdvice { Action = "Scan failed", Message = "Could not read the shop safely: " + ex.Message }; UpdateModernDashboard(); }
        }
        finally { _arrivalScanning = false; }
    }

    private async Task RefreshTravelShopPageAsync()
    {
        var tab = Active();
        var url = tab?.WebView.Source?.ToString() ?? "";
        if (tab is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("www.torn.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.EndsWith("page.php", StringComparison.OrdinalIgnoreCase) ||
            !uri.Query.Contains("sid=travel", StringComparison.OrdinalIgnoreCase))
        {
            FlashStatus("Open your foreign Torn travel shop tab before refreshing.");
            return;
        }
        tab.WebView.Reload();
        await Task.Delay(2200);
        await ScanTravelShopAsync(true);
    }

    private void TickTravelArrival()
    {
        if (_arrivalProfileId != CurrentProfileId) ClearTravelArrival();
        if (_travelSituation.State == "home" || _travelSituation.State == "returning" || _travelSituation.State == "outbound")
        {
            if (_arrivalSnapshot is not null) ClearTravelArrival();
            return;
        }
        if (!_arrivalScanning && DateTime.UtcNow - _arrivalLastScanUtc > TimeSpan.FromSeconds(20))
        {
            _arrivalLastScanUtc = DateTime.UtcNow;
            _ = ScanTravelShopAsync();
        }
    }

    private TravelLandingAdvice CurrentLandingAdvice()
    {
        if (_arrivalProfileId != CurrentProfileId) ClearTravelArrival();
        if (_arrivalSnapshot is not null)
            _arrivalAdvice = TravelArrivalAdvisor.Evaluate(_arrivalSnapshot, _travelForecast.Items,
                _travelHistory, _settings.TravelApplyMarketTax, DateTime.UtcNow, _arrivalStockChanges);
        return _arrivalAdvice;
    }
}
