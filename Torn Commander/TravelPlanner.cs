using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm
{
    private TravelForecastSnapshot _travelForecast = Storage.Load(Storage.TravelForecastCachePath, new TravelForecastSnapshot());
    private List<TravelObservation> _travelHistory = Storage.Load(Storage.TravelHistoryPath, new List<TravelObservation>());
    private DataGridView _travelGrid = null!;
    private Label _travelSummary = null!;
    private NumericUpDown _travelCapacity = null!;
    private ComboBox _travelFlightType = null!;
    private CheckBox _travelMarketTax = null!;
    private NumericUpDown _travelHorizon = null!;
    private NumericUpDown _travelConfidence = null!;
    private CheckBox _travelIncludeLow = null!;
    private bool _travelRefreshing;
    private DateTime _travelLastAttemptUtc = DateTime.MinValue;
    private DateTime _travelLastPlanMinuteUtc = DateTime.MinValue;

    private DateTime TravelPlanStartUtc => _settings.TravelPlanStartNow ||
        _settings.TravelPlanCustomStartUtc == DateTime.MinValue ? DateTime.UtcNow :
        _settings.TravelPlanCustomStartUtc.ToUniversalTime();

    private TravelItineraryPlan CurrentTravelPlan() =>
        BuildContextAwareTravelPlan();

    private void CopyTravelPlanForChat()
    {
        var plan = CurrentTravelPlan();
        var lines = new List<string> {
            $"Torn Commander 24h flight plan ({plan.StartUtc.ToLocalTime():g} to {plan.EndUtc.ToLocalTime():g})",
            "Starts/returns to Torn between all trips. Public stock may change."
        };
        foreach (var leg in plan.Legs)
            lines.Add($"#{leg.Number} {leg.DepartUtc.ToLocalTime():ddd h:mm tt} Torn -> {leg.Country} " +
                $"(arrive {leg.ArriveUtc.ToLocalTime():h:mm tt}) -> Torn by {leg.ReturnUtc.ToLocalTime():h:mm tt}; " +
                $"{leg.Units}x {leg.Item}, budget ${leg.CashRequired:N0}, estimated net ${leg.EstimatedProfit:N0}, {leg.Availability}");
        lines.Add($"24-hour modeled net: ${plan.EstimatedProfit:N0}. {plan.Note}");
        try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); FlashStatus("24-hour travel itinerary copied."); }
        catch (Exception ex) { FlashStatus("Could not copy travel plan: " + ex.Message); }
    }

    private void TickTravelMonitor()
    {
        var now = DateTime.UtcNow;
        if (_travelLastPlanMinuteUtc == DateTime.MinValue || now - _travelLastPlanMinuteUtc >= TimeSpan.FromMinutes(1))
        {
            _travelLastPlanMinuteUtc = now;
            TickTravelState();
            UpdateModernDashboard(); // rolling next 24h window, even between feed polls
        }
        if (!_settings.TravelAutoMonitor || _travelRefreshing || _webEnv is null) return;
        var minutes = Math.Clamp(_settings.TravelMonitorIntervalMinutes, 10, 60);
        if (_travelLastAttemptUtc == DateTime.MinValue || now - _travelLastAttemptUtc >= TimeSpan.FromMinutes(minutes))
            _ = RefreshTravelForecastAsync(false);
    }

    // Short-lived market quotes are indicative listing prices, NOT guaranteed sell proceeds.
    // This cache stays in memory; credentials are never written into travel-history files.
    private readonly Dictionary<int, (long Price, DateTime CheckedUtc)> _travelMarketQuotes = new();
    private readonly Dictionary<int, long> _travelCatalogMarketValues = new();
    private DateTime _travelCatalogCheckedUtc = DateTime.MinValue;
    private NumericUpDown _travelBudget = null!;
    private CheckBox _travelBook = null!;
    private CheckBox _travelStockOnly = null!;

    private Control BuildTravel()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("TRAVEL / FOREIGN STOCK"));
        p.Controls.Add(Muted("Free travel planner and foreign-stock forecast. Commander does not unlock or scrape TornTravel premium data. It uses documented public integration data when available and keeps its own local observation history as a fallback learning source."));

        _travelSummary = BigLabel("Travel forecast has not been refreshed yet.");
        p.Controls.Add(_travelSummary);

        var settings = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = 610, BackColor = PanelBg };
        _travelCapacity = Num(Math.Clamp(_settings.TravelCapacity, 1, 1000), 1, 1000);
        _travelFlightType = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = FieldBg, ForeColor = TextMain };
        _travelFlightType.Items.AddRange(new object[] { "Standard", "Airstrip", "Private", "Business" });
        _travelFlightType.SelectedItem = new[] { "Standard", "Airstrip", "Private", "Business" }.Contains(_settings.TravelFlightType) ? _settings.TravelFlightType : "Airstrip";
        _travelMarketTax = DarkCheck("Apply 5% Item Market fee", _settings.TravelApplyMarketTax);
        _travelHorizon = Num(Math.Clamp(_settings.TravelForecastHorizonHours, 6, 72), 6, 72);
        _travelConfidence = Num((decimal)Math.Clamp(_settings.TravelMinConfidence * 100.0, 0, 100), 0, 100);
        _travelIncludeLow = DarkCheck("Include low-confidence predictions", _settings.TravelIncludeLowConfidence);
        AddLabeled(settings, "Carry capacity", _travelCapacity);
        AddLabeled(settings, "Flight type", _travelFlightType);
        AddLabeled(settings, "Forecast horizon (hours)", _travelHorizon);
        AddLabeled(settings, "Minimum confidence %", _travelConfidence);
        settings.Controls.Add(_travelMarketTax, 1, settings.RowCount++);
        settings.Controls.Add(_travelIncludeLow, 1, settings.RowCount++);
        _travelBudget = Num((decimal)Math.Clamp(_settings.TravelCashBudget, 0, 1000000000L), 0, 1000000000);
        _travelBook = DarkCheck("Mailing Yourself Abroad book (-25% flight time)", _settings.TravelMailingBook);
        _travelStockOnly = DarkCheck("Only currently in-stock opportunities", _settings.TravelInStockOnly);
        AddLabeled(settings, "Cash budget (0 = unlimited)", _travelBudget);
        settings.Controls.Add(_travelBook, 1, settings.RowCount++);
        settings.Controls.Add(_travelStockOnly, 1, settings.RowCount++);
        p.Controls.Add(settings);

        var actions = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        actions.Controls.Add(MakeButton("Refresh forecast", async (_, _) => await RefreshTravelForecastAsync(true), 118));
        actions.Controls.Add(MakeButton("Save settings", (_, _) => SaveTravelSettings(), 104));
        actions.Controls.Add(MakeButton("Open Travel", (_, _) => NavigateActive("https://www.torn.com/travelagency.php"), 92));
        actions.Controls.Add(MakeButton("Open Items", (_, _) => NavigateActive(TornRoutes.Items), 82));
        p.Controls.Add(actions);

        _travelGrid = MakeGrid(610, 500);
        _travelGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _travelGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _travelGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        p.Controls.Add(_travelGrid);
        p.Controls.Add(Muted("Source priority: DroqsDB public feed, then YATA foreign-stock export and optional public market prices, then Commander local learning. Predictions are estimates, not guaranteed restocks. Commander labels low-confidence or locally learned estimates instead of pretending they are exact."));
        // Cached snapshots contain earlier derived economics; align them with the current
        // settings immediately on startup, without changing their original retrieval time.
        RecalculateStoredTravelRows();
        return p;
    }

    private void SaveTravelSettings()
    {
        if (_travelCapacity is not null) _settings.TravelCapacity = (int)_travelCapacity.Value;
        if (_travelFlightType is not null) _settings.TravelFlightType = _travelFlightType.SelectedItem?.ToString() ?? "Airstrip";
        if (_travelMarketTax is not null) _settings.TravelApplyMarketTax = _travelMarketTax.Checked;
        if (_travelHorizon is not null) _settings.TravelForecastHorizonHours = (int)_travelHorizon.Value;
        if (_travelConfidence is not null) _settings.TravelMinConfidence = (double)_travelConfidence.Value / 100.0;
        if (_travelIncludeLow is not null) _settings.TravelIncludeLowConfidence = _travelIncludeLow.Checked;
        if (_travelBudget is not null) _settings.TravelCashBudget = (long)_travelBudget.Value;
        if (_travelBook is not null) _settings.TravelMailingBook = _travelBook.Checked;
        if (_travelStockOnly is not null) _settings.TravelInStockOnly = _travelStockOnly.Checked;
        Storage.SaveSettings(_settings);
        // Recompute quote economics when flight method/capacity/budget changes, even
        // when public APIs are unreachable. Never mark cached stock as freshly fetched.
        RecalculateStoredTravelRows();
        UpdateModernDashboard();
        FlashStatus("Travel settings saved.");
    }

    private void RecalculateStoredTravelRows()
    {
        foreach (var item in _travelForecast.Items)
            CalculateTravelOpportunity(item);
        _travelForecast.Items = _travelForecast.Items
            .OrderByDescending(x => x.MarketValue > 0 && x.ProfitPerHour > 0)
            .ThenByDescending(x => x.StockStatus == "In stock")
            .ThenByDescending(x => x.ProfitPerHour).ToList();
        RebindTravel();
    }

    private async Task RefreshTravelForecastAsync(bool showErrors)
    {
        if (_travelRefreshing) return;
        _travelRefreshing = true;
        _travelLastAttemptUtc = DateTime.UtcNow;
        SaveTravelSettings();
        if (_travelSummary is not null) _travelSummary.Text = "Refreshing public stock, prices and forecast...";
        try
        {
            var errors = new List<string>();
            // Fetch independent public endpoints concurrently; a slow endpoint should not
            // serialize two 25-second waits on a travel-screen refresh.
            var stockTask = CapturePublicTravelSnapshotAsync();
            var forecastTask = FetchPublicTravelForecastAsync();
            var stock = new List<TravelForecastItem>();
            try { stock = await stockTask; }
            catch (Exception ex)
            {
                errors.Add(DescribeTravelFetchError("DroqsDB stock feed", ex));
                try
                {
                    stock = await CaptureYataTravelSnapshotAsync();
                    // A failed primary feed is worth disclosing, but YATA stock is usable.
                }
                catch (Exception yataError) { errors.Add(DescribeTravelFetchError("YATA backup stock feed", yataError)); }
            }

            TravelForecastSnapshot? remote = null;
            try { remote = await forecastTask; }
            catch (Exception ex) { errors.Add(DescribeTravelFetchError("Forecast feed", ex)); }

            // Current stock is valuable even when the prediction endpoint is empty.
            // Use the user's existing Torn API connection for a small, rate-limited set of
            // missing market quotes. The API key goes only to api.torn.com via TornApiClient.
            if (stock.Count > 0 && stock.Any(x => x.MarketValue <= 0))
                await EnrichMissingTravelPricesAsync(stock);

            var candidates = stock.Concat(remote?.Items ?? Enumerable.Empty<TravelForecastItem>()).ToList();
            if (candidates.Count == 0)
            {
                var local = BuildLocalTravelForecast();
                candidates.AddRange(local.Items);
            }
            if (candidates.Count > 0)
            {
                var enriched = candidates.Select(CalculateTravelOpportunity)
                    .Where(x => (x.Units > 0 && x.ProfitPerHour > 0) || (x.Stock is > 0 && x.BuyPrice > 0 && x.MarketValue <= 0))
                    .Where(x => !_settings.TravelInStockOnly || x.StockStatus == "In stock")
                    .Where(x => (x.MarketValue <= 0 && x.Stock is > 0) || x.StockStatus == "In stock" || _settings.TravelIncludeLowConfidence || x.Confidence >= _settings.TravelMinConfidence)
                    // Deduplicate a current-stock row and a forecast for the same item/destination.
                    .GroupBy(x => (x.Country.ToUpperInvariant(), x.ItemId, x.ItemName.ToUpperInvariant()))
                    .Select(g => g.OrderByDescending(x => x.StockStatus == "In stock" && !x.IsStale)
                                  .ThenByDescending(x => x.MarketValue > 0)
                                  .ThenByDescending(x => x.ProfitPerHour).First())
                    // Display profitable, priced routes first; stock-only rows are still useful
                    // but must never outrank an actually computable opportunity.
                    .OrderByDescending(x => x.MarketValue > 0 && x.ProfitPerHour > 0)
                    .ThenByDescending(x => x.StockStatus == "In stock" && !x.IsStale)
                    .ThenByDescending(x => x.ProfitPerHour)
                    .Take(140).ToList();
                _travelForecast = new TravelForecastSnapshot
                {
                    RetrievedAtUtc = DateTime.UtcNow,
                    Source = stock.Count > 0 ? stock[0].Source + (remote is null ? "" : " + forecast") : remote?.Source ?? "Commander local learning",
                    Items = enriched,
                    Error = ShortTravelErrorSummary(errors)
                };
            }
            else
            {
                // Do not throw away a saved snapshot during an outage. Preserve its real timestamp,
                // mark it stale, and never imply an old cached price is newly refreshed.
                _travelForecast.Error = errors.Count > 0 ? ShortTravelErrorSummary(errors) : "No public or locally learned travel opportunities yet.";
                foreach (var item in _travelForecast.Items) item.IsStale = true;
                RecalculateStoredTravelRows();
            }
            Storage.Save(Storage.TravelForecastCachePath, _travelForecast);
            RebindTravel();
            UpdateModernDashboard();
            if (showErrors && _travelForecast.Items.Count == 0 && errors.Count > 0)
                MessageBox.Show("Travel data could not be refreshed. Your previous saved observations were preserved if available.\n\n" + string.Join("\n", errors), "Travel Planner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _travelRefreshing = false;
            UpdateModernDashboard(); // clear the in-progress indicator even after a failed request
        }
    }

    // Price enrichment is opportunistic: only a few quote requests per refresh, with a
    // hard overall timeout and independent failure for each item. No buying or selling.
    private async Task EnrichMissingTravelPricesAsync(List<TravelForecastItem> rows)
    {
        var now = DateTime.UtcNow;
        var ids = rows.Where(x => x.MarketValue <= 0 && x.ItemId > 0 && x.Stock is > 0)
            .GroupBy(x => x.ItemId).OrderByDescending(g => g.Sum(x => x.Stock ?? 0))
            .Select(g => g.Key).ToList();
        if (ids.Count == 0) return;

        // Reuse 15-minute quotes, and never store the API key in this cache.
        foreach (var id in ids)
            if (_travelMarketQuotes.TryGetValue(id, out var quote) && quote.Price > 0 &&
                now - quote.CheckedUtc <= TimeSpan.FromMinutes(15))
                ApplyMarketQuote(rows, id, quote.Price, quote.CheckedUtc, "Torn Item Market lowest listing (indicative)");

        var key = CredentialManager.LoadApiKey(CurrentProfileId);
        if (string.IsNullOrWhiteSpace(key)) return;

        // One official Torn catalog request can cover many foreign items at once.
        // The returned market_value is an estimate, not a firm listing or a sale guarantee.
        if (now - _travelCatalogCheckedUtc >= TimeSpan.FromMinutes(20))
        {
            try
            {
                using var catalogTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var catalog = await _api.FetchItemCatalogAsync(key, catalogTimeout.Token);
                _travelCatalogMarketValues.Clear();
                foreach (var c in catalog.Where(x => x.MarketValue > 0))
                    _travelCatalogMarketValues[c.Id] = c.MarketValue;
                _travelCatalogCheckedUtc = DateTime.UtcNow;
            }
            catch (Exception) { /* Catalog unavailable; try individual listings below. */ }
        }
        foreach (var id in ids)
            if (_travelCatalogMarketValues.TryGetValue(id, out var price) && price > 0)
                ApplyMarketQuote(rows, id, price, _travelCatalogCheckedUtc,
                    "Torn catalog market-value estimate (not live sale price)");

        // When the catalog fails or has gaps, query a small number of actual listings.
        // Requests are bounded to avoid hammering Torn or slowing refresh indefinitely.
        var missing = ids.Where(id => rows.Any(x => x.ItemId == id && x.MarketValue <= 0)).Take(6).ToList();
        if (missing.Count == 0) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(14));
        using var parallel = new SemaphoreSlim(2);
        var jobs = missing.Select(async id =>
        {
            var entered = false;
            try
            {
                await parallel.WaitAsync(timeout.Token);
                entered = true;
                var quote = await _api.FetchItemMarketAsync(id, 1, 0, key, timeout.Token);
                if (quote.BestPrice <= 0) return;
                var checkedAt = DateTime.UtcNow;
                _travelMarketQuotes[id] = (quote.BestPrice, checkedAt);
                ApplyMarketQuote(rows, id, quote.BestPrice, checkedAt,
                    "Torn Item Market lowest listing (indicative; not guaranteed proceeds)");
            }
            catch (OperationCanceledException) { /* Time budget reached. */ }
            catch (Exception) { /* Keep the stock row unpriced. */ }
            finally { if (entered) parallel.Release(); }
        }).ToList();
        await Task.WhenAll(jobs);
    }

    private static void ApplyMarketQuote(List<TravelForecastItem> items, int id, long price, DateTime checkedUtc, string note)
    {
        foreach (var item in items.Where(x => x.ItemId == id && x.MarketValue <= 0))
        {
            item.MarketValue = price;
            item.PricingUpdatedAtUtc = checkedUtc;
            // Keep stock's own freshness separate from price retrieval time.
            item.Note = string.IsNullOrWhiteSpace(item.Note) ? note : item.Note + " | " + note;
        }
    }

    private static void ConfigurePublicTravelClient(HttpClient http, string referrer = "https://droqsdb.com/")
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36 TornCommander/0.5.9");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        http.DefaultRequestHeaders.Referrer = new Uri(referrer);
    }

    private static string DescribeTravelFetchError(string label, Exception ex)
    {
        if (ex is TaskCanceledException)
            return label + " timed out while contacting the public provider.";
        if (ex is HttpRequestException hex)
        {
            var code = hex.StatusCode is not null ? ((int)hex.StatusCode.Value).ToString() : null;
            if (code == "530")
                return label + " is temporarily unavailable or rejecting automated requests (HTTP 530).";
            if (!string.IsNullOrWhiteSpace(code))
                return label + " failed with HTTP " + code + ".";
            return label + " could not be reached.";
        }
        if (ex is InvalidDataException)
            return label + " returned an unexpected response format.";
        return label + " failed: " + ex.Message;
    }

    private static string ShortTravelErrorSummary(IEnumerable<string> errors)
    {
        var list = errors.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (list.Count == 0) return "";
        if (list.Count == 1) return list[0];
        return string.Join(" ", list);
    }

    private async Task<TravelForecastSnapshot> FetchPublicTravelForecastAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        ConfigurePublicTravelClient(http);
        var flight = Uri.EscapeDataString(_settings.TravelFlightType.ToLowerInvariant());
        var url = $"https://droqsdb.com/api/public/v1/daily-forecast?limit=40&horizonHours={Math.Clamp(_settings.TravelForecastHorizonHours, 6, 72)}&minConfidence={_settings.TravelMinConfidence:0.##}&flightType={flight}&capacity={Math.Clamp(_settings.TravelCapacity, 1, 1000)}&includeLowConfidence={_settings.TravelIncludeLowConfidence.ToString().ToLowerInvariant()}";
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!TryGetArray(root, out var items, "items", "forecasts", "results"))
            throw new InvalidDataException("Public forecast response did not contain an items list.");

        var result = new TravelForecastSnapshot
        {
            RetrievedAtUtc = DateTime.UtcNow,
            Source = "DroqsDB public forecast API"
        };

        foreach (var row in items.EnumerateArray())
        {
            var item = ParsePublicForecastRow(row);
            if (string.IsNullOrWhiteSpace(item.ItemName) || string.IsNullOrWhiteSpace(item.Country)) continue;
            if (!_settings.TravelIncludeLowConfidence && item.Confidence < _settings.TravelMinConfidence) continue;
            result.Items.Add(item);
        }

        result.Items = result.Items
            .OrderByDescending(x => x.ProfitPerHour)
            .ThenByDescending(x => x.Confidence)
            .Take(60)
            .ToList();
        return result;
    }

    private TravelForecastItem ParsePublicForecastRow(JsonElement row)
    {
        var item = new TravelForecastItem
        {
            ItemId = GetInt(row, "itemId", "id") ?? 0,
            ItemName = GetString(row, "itemName", "name", "item") ?? "",
            Country = GetString(row, "country", "destination", "countryName") ?? "",
            Category = GetString(row, "category", "type") ?? "",
            Stock = GetInt(row, "stock", "quantity", "currentStock"),
            StockUpdatedAtUtc = GetDateTime(row, "stockUpdatedAt", "lastUpdated", "stockAsOf"),
            PricingUpdatedAtUtc = GetDateTime(row, "pricingUpdatedAt", "marketUpdatedAt"),
            BuyPrice = GetLong(row, "buyPrice", "cost", "purchasePrice") ?? 0,
            MarketValue = GetLong(row, "marketValue", "sellPrice", "marketPrice") ?? 0,
            ProfitPerItem = GetLong(row, "profitPerItem", "profit") ?? 0,
            Source = "DroqsDB public forecast"
        };

        var ppm = GetDouble(row, "profitPerMinute", "profitPerMin");
        var pph = GetDouble(row, "profitPerHour", "profitPerHr");
        item.ProfitPerHour = pph ?? (ppm is double m ? m * 60.0 : 0.0);
        item.RestockAtUtc = GetDateTime(row, "restockAt", "estimatedRestockAt", "nextRestockAt");
        item.Confidence = NormalizeConfidence(GetDouble(row, "confidence", "restockConfidence") ?? 0);

        if (TryGetArray(row, out var windows, "flyOutWindows", "windows", "departureWindows"))
        {
            foreach (var w in windows.EnumerateArray())
            {
                item.SuggestedDepartureUtc ??= GetDateTime(w, "departureAt", "leaveAt", "flyOutAt");
                item.ArrivalAtUtc ??= GetDateTime(w, "arrivalAt");
                item.RestockAtUtc ??= GetDateTime(w, "restockAt", "estimatedRestockAt");
                var wc = GetDouble(w, "confidence");
                if (wc is not null) item.Confidence = Math.Max(item.Confidence, NormalizeConfidence(wc.Value));
                item.Note = GetString(w, "status", "availability", "safety", "message") ?? item.Note;
                break;
            }
        }

        // The provider may offer a restock ETA in minutes rather than an absolute timestamp.
        if (item.RestockAtUtc is null && GetDouble(row, "estimatedRestockMinutes") is double mins && mins >= 0 && mins <= 4320)
            item.RestockAtUtc = DateTime.UtcNow.AddMinutes(mins);
        item.ConfidenceLabel = item.Confidence >= 0.75 ? "High" : item.Confidence >= 0.45 ? "Medium" : item.Confidence > 0 ? "Low" : "Unrated";
        if (item.ProfitPerItem == 0 && item.MarketValue > 0 && item.BuyPrice > 0)
        {
            var sell = _settings.TravelApplyMarketTax ? (long)Math.Floor(item.MarketValue * 0.95) : item.MarketValue;
            item.ProfitPerItem = sell - item.BuyPrice;
        }
        return item;
    }

    private async Task<List<TravelForecastItem>> CapturePublicTravelSnapshotAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        ConfigurePublicTravelClient(http);
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://droqsdb.com/api/public/v1/export"));
        if (!TryGetArray(doc.RootElement, out var countries, "countries", "data"))
            throw new InvalidDataException("Public export did not include a countries array.");
        var result = new List<TravelForecastItem>();
        var now = DateTime.UtcNow;
        foreach (var country in countries.EnumerateArray())
        {
            var countryName = GetString(country, "country", "name") ?? "";
            var countryUpdated = GetDateTime(country, "lastUpdated", "stockUpdatedAt", "updatedAt");
            if (!TryGetArray(country, out var rows, "items", "stocks")) continue;
            foreach (var row in rows.EnumerateArray())
            {
                var id = GetInt(row, "itemId", "id") ?? 0;
                var name = GetString(row, "itemName", "name") ?? "";
                if (id <= 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(countryName)) continue;
                var quantity = GetInt(row, "stock", "quantity", "currentStock") ?? 0;
                var buy = GetLong(row, "buyPrice", "cost") ?? 0;
                var sell = GetLong(row, "marketValue", "marketPrice") ?? 0;
                var lastStock = GetDateTime(row, "stockUpdatedAt", "lastUpdated", "updatedAt") ?? countryUpdated;
                var lastPrice = GetDateTime(row, "pricingUpdatedAt", "marketUpdatedAt");
                if (quantity > 0 && buy > 0 && sell > 0)
                {
                    result.Add(new TravelForecastItem
                    {
                        ItemId = id, ItemName = name, Country = countryName,
                        Category = GetString(row, "category", "type") ?? "",
                        Stock = quantity, BuyPrice = buy, MarketValue = sell,
                        StockUpdatedAtUtc = lastStock, PricingUpdatedAtUtc = lastPrice,
                        Source = "DroqsDB public stock", Note = "Observed stock; not a reservation or guarantee"
                    });
                }
                var existing = _travelHistory.LastOrDefault(x => x.ItemId == id && x.Country.Equals(countryName, StringComparison.OrdinalIgnoreCase));
                // Don't build a false high-frequency restock history out of polling the same quantity.
                if (existing is not null && now - existing.TimestampUtc < TimeSpan.FromMinutes(10) && existing.Quantity == quantity) continue;
                _travelHistory.Add(new TravelObservation
                {
                    TimestampUtc = now, Country = countryName, ItemId = id,
                    ItemName = name, Category = GetString(row, "category", "type") ?? "",
                    Quantity = quantity, BuyPrice = buy, MarketValue = sell
                });
            }
        }
        var cutoff = now.AddDays(-21);
        _travelHistory = _travelHistory.Where(x => x.TimestampUtc >= cutoff && x.TimestampUtc <= now.AddMinutes(5))
            .OrderBy(x => x.TimestampUtc).TakeLast(30000).ToList();
        Storage.Save(Storage.TravelHistoryPath, _travelHistory);
        return result;
    }

    private async Task<List<TravelForecastItem>> CaptureYataTravelSnapshotAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        ConfigurePublicTravelClient(http, "https://yata.yt/");
        // YATA publishes community-sourced country stocks; prices may be absent.
        // We never infer a market price from item cost or claim a profit without one.
        var yataTask = http.GetStringAsync("https://yata.yt/api/v1/travel/export/");
        var catalogTask = http.GetStringAsync("https://weav3r.dev/api/marketplace");
        var prices = new Dictionary<int, long>();
        try
        {
            using var catalog = JsonDocument.Parse(await catalogTask);
            if (TryGetArray(catalog.RootElement, out var entries, "items"))
            {
                foreach (var row in entries.EnumerateArray())
                {
                    var id = GetInt(row, "item_id", "id");
                    var price = GetLong(row, "market_price", "marketPrice");
                    if (id is > 0 && price is > 0) prices[id.Value] = price.Value;
                }
            }
        }
        catch { /* Market-price source optional; make unknown prices explicit. */ }
        using var doc = JsonDocument.Parse(await yataTask);
        if (!doc.RootElement.TryGetProperty("stocks", out var countries) || countries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("YATA did not return the expected stocks object.");
        var now = DateTime.UtcNow;
        var result = new List<TravelForecastItem>();
        foreach (var country in countries.EnumerateObject())
        {
            string name = country.Name.ToLowerInvariant() switch
            {
                "mex" => "Mexico", "cay" => "Cayman Islands", "can" => "Canada",
                "haw" => "Hawaii", "uni" or "uk" => "United Kingdom",
                "arg" => "Argentina", "swi" => "Switzerland", "jap" => "Japan",
                "chi" => "China", "uae" => "United Arab Emirates", "sou" => "South Africa",
                _ => ""
            };
            if (string.IsNullOrWhiteSpace(name) || country.Value.ValueKind != JsonValueKind.Object) continue;
            var block = country.Value;
            DateTime? updated = null;
            if (GetLong(block, "update") is long unix && unix > 1500000000 && unix < 2200000000)
                updated = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            if (!TryGetArray(block, out var rows, "stocks", "items")) continue;
            foreach (var row in rows.EnumerateArray())
            {
                var id = GetInt(row, "id", "itemId") ?? 0;
                var itemName = GetString(row, "name", "itemName") ?? "";
                var qty = GetInt(row, "quantity", "stock") ?? 0;
                var buy = GetLong(row, "cost", "buyPrice") ?? 0;
                if (id <= 0 || string.IsNullOrWhiteSpace(itemName) || qty <= 0 || buy <= 0) continue;
                var hasExternalPrice = prices.TryGetValue(id, out var market) && market > 0;
                DateTime? priceTimestamp = hasExternalPrice ? now : null;
                if (!hasExternalPrice)
                {
                    var prior = _travelHistory.Where(x => x.ItemId == id && x.MarketValue > 0)
                        .OrderByDescending(x => x.TimestampUtc).FirstOrDefault();
                    if (prior is not null && now - prior.TimestampUtc <= TimeSpan.FromHours(3))
                    {
                        market = prior.MarketValue;
                        priceTimestamp = prior.TimestampUtc;
                    }
                    else market = 0;
                }
                var item = new TravelForecastItem
                {
                    ItemId = id, ItemName = itemName, Country = name, Stock = qty,
                    BuyPrice = buy, MarketValue = market, StockUpdatedAtUtc = updated,
                    PricingUpdatedAtUtc = priceTimestamp,
                    Source = "YATA community stock",
                    Note = market > 0 ? (hasExternalPrice ? "Community stock + external indicative market quote" :
                        "Community stock + recent historical indicative market quote") :
                        "Stock only: market price unavailable; no profit estimate"
                };
                // Missing or old timestamp must not be represented as fresh observed stock.
                item.IsStale = !updated.HasValue || now - updated.Value > TimeSpan.FromMinutes(30);
                result.Add(item);
                // Only learn from fresh timestamped observations, never from a stale poll.
                if (updated.HasValue && now - updated.Value <= TimeSpan.FromMinutes(30))
                {
                    var priorPoint = _travelHistory.LastOrDefault(x => x.ItemId == id && x.Country.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (priorPoint is null || (updated.Value > priorPoint.TimestampUtc.AddMinutes(5) && priorPoint.Quantity != qty))
                        _travelHistory.Add(new TravelObservation {TimestampUtc = updated.Value, Country = name, ItemId = id, ItemName = itemName, Quantity = qty, BuyPrice = buy, MarketValue = market});
                }
            }
        }
        var cutoff = now.AddDays(-21);
        _travelHistory = _travelHistory.Where(x => x.TimestampUtc >= cutoff).OrderBy(x => x.TimestampUtc).TakeLast(30000).ToList();
        Storage.Save(Storage.TravelHistoryPath, _travelHistory);
        return result;
    }

    // Official Torn wiki flight table (minutes, without the 25% book bonus).
    // "Private" means the WLT benefit; business tickets are not priced as cash here.
    private static (int StandardMinutes, int StandardFare) CountryRoute(string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized switch
        {
            "mexico" or "mex" => (24, 6500),
            "caymanislands" or "caymans" or "cay" => (33, 10000),
            "canada" or "can" => (39, 9000),
            "hawaii" or "haw" => (127, 11000),
            "unitedkingdom" or "uk" or "england" => (151, 18000),
            "argentina" or "arg" => (158, 21000),
            "switzerland" or "swi" => (166, 27000),
            "japan" or "jap" => (213, 32000),
            "china" or "chi" => (229, 35000),
            "unitedarabemirates" or "uae" => (257, 32000),
            "southafrica" or "sou" or "sa" => (281, 40000),
            _ => (0, 0)
        };
    }

    private TravelForecastItem CalculateTravelOpportunity(TravelForecastItem item)
    {
        // Economics are derived fields. Reset before every recompute so a missing quote or
        // reduced budget can never retain profit from the last settings/profile state.
        item.ProfitPerItem = 0;
        item.ProfitPerHour = 0;
        item.TripProfit = 0;
        item.RoiPercent = 0;
        item.Units = 0;
        item.CashRequired = 0;
        item.FlightMinutesOneWay = 0;
        var (standardMinutes, standardFare) = CountryRoute(item.Country);
        if (standardMinutes <= 0 || item.BuyPrice <= 0) return item;
        var factor = _settings.TravelFlightType switch
        {
            "Airstrip" => 0.70,
            "Private" => 0.50,
            "Business" => 0.30,
            _ => 1.0
        };
        if (_settings.TravelMailingBook) factor *= 0.75;
        item.FlightMinutesOneWay = Math.Max(1, (int)Math.Round(standardMinutes * factor, MidpointRounding.AwayFromZero));
        var now = DateTime.UtcNow;
        item.IsStale = item.IsStale ||
            (item.Stock is > 0 && !item.StockUpdatedAtUtc.HasValue) ||
            (item.StockUpdatedAtUtc.HasValue && (now - item.StockUpdatedAtUtc.Value).TotalMinutes > 30) ||
            (item.PricingUpdatedAtUtc.HasValue && (now - item.PricingUpdatedAtUtc.Value).TotalHours > 12) ||
            (item.RestockAtUtc.HasValue && item.RestockAtUtc.Value < now.AddHours(-1));
        var live = item.Stock is > 0 && !item.IsStale;
        var predicted = !live && (item.RestockAtUtc.HasValue || item.SuggestedDepartureUtc.HasValue);
        item.StockStatus = item.IsStale ? "Stale" : live ? "In stock" : predicted ? "Predicted" : "Unverified";
        var budget = _settings.TravelCashBudget;
        var maxBudgetUnits = budget > 0 ? (int)Math.Min(1000L, budget / item.BuyPrice) : 1000;
        var stockLimit = item.Stock is > 0 ? item.Stock.Value : predicted ? 1000 : 0;
        item.Units = Math.Clamp(Math.Min(Math.Min(_settings.TravelCapacity, maxBudgetUnits), stockLimit), 0, 1000);
        item.CashRequired = item.Units * item.BuyPrice;
        if (item.Units == 0 || item.MarketValue <= 0) return item;
        var sell = _settings.TravelApplyMarketTax ? (long)Math.Floor(item.MarketValue * 0.95m) : item.MarketValue;
        item.ProfitPerItem = sell - item.BuyPrice;
        item.CashRequired = item.Units * item.BuyPrice;
        // Standard cash airfare assumed on each leg. Airstrip/WLT are free; business tickets
        // are excluded from cash net and clearly disclosed in the UI.
        var fare = _settings.TravelFlightType == "Standard" ? 2L * standardFare : 0L;
        item.TripProfit = item.ProfitPerItem * item.Units - fare;
        item.RoiPercent = item.CashRequired <= 0 ? 0 : 100.0 * item.TripProfit / item.CashRequired;
        var roundTripMinutes = Math.Max(1, 2 * item.FlightMinutesOneWay);
        if (predicted && item.RestockAtUtc > now)
        {
            var leave = item.RestockAtUtc.Value.AddMinutes(-item.FlightMinutesOneWay - 5);
            item.SuggestedDepartureUtc ??= leave > now ? leave : now;
            item.ArrivalAtUtc ??= item.SuggestedDepartureUtc.Value.AddMinutes(item.FlightMinutesOneWay);
        }
        // Waiting until a future departure also ties up this opportunity's time.
        var waitMinutes = predicted && item.SuggestedDepartureUtc.HasValue
            ? Math.Max(0.0, (item.SuggestedDepartureUtc.Value - now).TotalMinutes) : 0.0;
        item.ProfitPerHour = item.TripProfit * 60.0 / (roundTripMinutes + waitMinutes);
        return item;
    }

    private TravelForecastSnapshot BuildLocalTravelForecast()
    {
        var result = new TravelForecastSnapshot
        {
            RetrievedAtUtc = DateTime.UtcNow,
            Source = "Commander local learning"
        };

        foreach (var group in _travelHistory.GroupBy(x => (x.Country.ToLowerInvariant(), x.ItemId)))
        {
            var rows = group.OrderBy(x => x.TimestampUtc).ToList();
            if (rows.Count < 4 || DateTime.UtcNow - rows[^1].TimestampUtc > TimeSpan.FromHours(24)) continue;
            var restocks = new List<DateTime>();
            for (var i = 1; i < rows.Count; i++)
            {
                var before = rows[i - 1];
                var after = rows[i];
                if ((before.Quantity <= 0 && after.Quantity > 0) || (after.Quantity > before.Quantity * 1.5 && after.Quantity - before.Quantity >= 5))
                    if (restocks.Count == 0 || after.TimestampUtc - restocks[^1] > TimeSpan.FromMinutes(5)) restocks.Add(after.TimestampUtc);
            }
            if (restocks.Count < 3) continue; // two intervals are the minimum for local recurrence
            var intervals = new List<double>();
            for (var i = 1; i < restocks.Count; i++) intervals.Add((restocks[i] - restocks[i - 1]).TotalMinutes);
            if (intervals.Count == 0) continue;
            // Do not treat missed observation intervals as high-confidence clockwork.
            var mean = intervals.Average();
            if (mean < 10 || mean > 24 * 60) continue;
            var variance = intervals.Select(x => Math.Pow(x - mean, 2)).Average();
            var cv = Math.Sqrt(variance) / mean;
            var confidence = Math.Clamp((restocks.Count - 1) / 9.0 * (1.0 - Math.Min(0.85, cv)), 0.05, 0.70);
            var next = restocks[^1].AddMinutes(mean);
            while (next < DateTime.UtcNow.AddMinutes(-5)) next = next.AddMinutes(mean);
            if (next > DateTime.UtcNow.AddHours(Math.Clamp(_settings.TravelForecastHorizonHours, 6, 72))) continue;
            var latest = rows[^1];
            var sell = _settings.TravelApplyMarketTax ? (long)Math.Floor(latest.MarketValue * 0.95) : latest.MarketValue;
            result.Items.Add(new TravelForecastItem
            {
                ItemId = latest.ItemId,
                ItemName = latest.ItemName,
                Country = latest.Country,
                Category = latest.Category,
                Stock = latest.Quantity,
                BuyPrice = latest.BuyPrice,
                MarketValue = latest.MarketValue,
                ProfitPerItem = sell > 0 && latest.BuyPrice > 0 ? sell - latest.BuyPrice : 0,
                RestockAtUtc = next,
                StockUpdatedAtUtc = latest.TimestampUtc,
                Confidence = confidence,
                ConfidenceLabel = confidence >= 0.65 ? "Medium" : "Low",
                Source = "Commander local learning",
                Note = $"Learned from {restocks.Count} observed restocks; avg cycle ~{mean:0} min"
            });
        }

        result.Items = result.Items.OrderByDescending(x => x.Confidence).ThenBy(x => x.RestockAtUtc).Take(60).ToList();
        return result;
    }

    private void RebindTravel()
    {
        if (_travelGrid is null || _travelSummary is null) return;
        var age = _travelForecast.RetrievedAtUtc == DateTime.MinValue ? "never" : ToAge(DateTime.UtcNow - _travelForecast.RetrievedAtUtc);
        _travelSummary.Text = $"{_travelForecast.Source} • {_travelForecast.Items.Count:N0} ranked routes • last good refresh {age} ago" + (string.IsNullOrWhiteSpace(_travelForecast.Error) ? "" : " • partial feed / cached data");
        _travelGrid.DataSource = _travelForecast.Items.Select(x => new
        {
            Item = x.ItemName,
            x.Country,
            x.StockStatus,
            Stock = x.Stock?.ToString("N0") ?? "?",
            x.Units,
            Cash = x.CashRequired > 0 ? $"${x.CashRequired:N0}" : "—",
            TripProfit = x.TripProfit != 0 ? $"${x.TripProfit:N0}" : "—",
            ROI = x.RoiPercent != 0 ? $"{x.RoiPercent:0.#}%" : "—",
            Flight = x.FlightMinutesOneWay > 0 ? $"{x.FlightMinutesOneWay}m each way" : "—",
            Buy = x.BuyPrice > 0 ? $"${x.BuyPrice:N0}" : "—",
            ProfitEach = x.ProfitPerItem != 0 ? $"${x.ProfitPerItem:N0}" : "—",
            ProfitHr = x.ProfitPerHour != 0 ? $"${x.ProfitPerHour:N0}/hr" : "—",
            Restock = FormatLocalTime(x.RestockAtUtc),
            Leave = FormatLocalTime(x.SuggestedDepartureUtc),
            Confidence = x.ConfidenceLabel + (x.Confidence > 0 ? $" {x.Confidence:P0}" : ""),
            x.Source,
            x.Note
        }).ToList();
    }

    private static string FormatLocalTime(DateTime? utc)
    {
        if (utc is null) return "—";
        var t = utc.Value.Kind == DateTimeKind.Utc ? utc.Value.ToLocalTime() : utc.Value;
        return t.ToString("ddd h:mm tt");
    }

    private static string ToAge(TimeSpan age)
    {
        if (age.TotalSeconds < 90) return $"{Math.Max(0, age.TotalSeconds):0}s";
        if (age.TotalMinutes < 90) return $"{age.TotalMinutes:0}m";
        return $"{age.TotalHours:0.#}h";
    }

    private static double NormalizeConfidence(double v) => v > 1.0 ? Math.Clamp(v / 100.0, 0, 1) : Math.Clamp(v, 0, 1);

    private static bool TryGetArray(JsonElement obj, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array) return true;
        array = default;
        return false;
    }

    private static JsonElement? GetNestedObject(JsonElement e, params string[] names)
    {
        foreach (var name in names)
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object) return v;
        return null;
    }

    private static string? GetString(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind == JsonValueKind.Number) return v.ToString();
            if (v.ValueKind == JsonValueKind.Object)
            {
                var nested = GetString(v, "name", "label", "title");
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        var itemObj = GetNestedObject(e, "item");
        return itemObj is JsonElement io ? GetString(io, names) ?? GetString(io, "name") : null;
    }

    private static int? GetInt(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n)) return n;
        }
        var itemObj = GetNestedObject(e, "item");
        return itemObj is JsonElement io ? GetInt(io, names) : null;
    }

    private static long? GetLong(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (long)d;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out n)) return n;
        }
        return null;
    }

    private static double? GetDouble(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out d)) return d;
        }
        return null;
    }

    private static DateTime? GetDateTime(JsonElement e, params string[] names)
    {
        foreach (var name in names)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)) return dt.UtcDateTime;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var unix))
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime; } catch { }
            }
        }
        return null;
    }
}
