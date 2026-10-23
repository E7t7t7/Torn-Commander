using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm
{
    private WebView2 _modernDashboard = null!;
    private TabControl _classicTabs = null!;
    private TabControl _companionShell = null!;
    private bool _modernDashboardReady;
    private TabControl _lossWorkerDeck = null!;
    private Panel _lossWorkerPanel = null!;
    private SplitContainer _lossWorkerSplit = null!;
    private bool _lossWorkerSized;
    private string _lastGraffitiActionStatus = "Idle: use Run Once for one spray, or Open Only to navigate.";

    private Control BuildModernDashboardHost()
    {
        var host = new SplitContainer { Dock = DockStyle.Fill, BackColor = Color.FromArgb(55,87,110), Orientation = Orientation.Horizontal, SplitterWidth = 8, Panel1MinSize = 0, Panel2MinSize = 0 };
        _lossWorkerSplit = host;
        _modernDashboard = new WebView2 { Dock = DockStyle.Fill, BackColor = Bg };
        host.Panel1.Controls.Add(_modernDashboard);
        _lossWorkerPanel = new Panel { Dock = DockStyle.Fill, Height = 380, Visible = false, BackColor = PanelBg };
        _lossWorkerDeck = new TabControl { Dock = DockStyle.Fill };
        var workerBar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = PanelBg };
        workerBar.Controls.Add(new Label { Text = "Loss workers — drag divider to resize", Dock = DockStyle.Fill, ForeColor = TextMain, TextAlign = ContentAlignment.MiddleLeft });
        var hide = MakeButton("Hide ×", (_, _) => HideLossWorkerBrowsers(), 85);
        hide.Dock = DockStyle.Right; workerBar.Controls.Add(hide);
        var close = MakeButton("Stop & Close", async (_, _) => {
            try { await StopBothLossWorkersAsync(); HideLossWorkerBrowsers(); }
            catch (Exception ex) { FlashStatus("Workers could not all be stopped: " + ex.Message); }
        }, 115);
        close.Dock = DockStyle.Right; workerBar.Controls.Add(close);
        var expand = MakeButton("Expand / Restore", (_, _) => _lossWorkerSplit.Panel1Collapsed = !_lossWorkerSplit.Panel1Collapsed, 130);
        expand.Dock = DockStyle.Right; workerBar.Controls.Add(expand);
        _lossWorkerPanel.Controls.Add(_lossWorkerDeck);
        _lossWorkerPanel.Controls.Add(workerBar);
        host.Panel2.Controls.Add(_lossWorkerPanel);
        host.Panel2Collapsed = true;
        return host;
    }

    private async Task InitializeModernDashboardAsync()
    {
        if (_webEnv is null || _modernDashboard is null) return;
        try
        {
            await _modernDashboard.EnsureCoreWebView2Async(_webEnv);
            _modernDashboard.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _modernDashboard.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _modernDashboard.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _modernDashboard.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            _modernDashboard.CoreWebView2.WebMessageReceived += ModernDashboard_WebMessageReceived;
            _modernDashboard.NavigationCompleted += (_, _) =>
            {
                _modernDashboardReady = true;
                UpdateModernDashboard();
            };
            _modernDashboard.NavigateToString(ModernDashboardHtml());
        }
        catch (Exception ex)
        {
            _modernDashboardReady = false;
            FlashStatus("Modern dashboard could not initialize: " + ex.Message);
        }
    }

    private async void ModernDashboard_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var cmd = root.TryGetProperty("cmd", out var c) ? c.GetString() ?? "" : "";
            switch (cmd)
            {
                case "levelStart": case "levelPause": case "levelResume": case "levelStop": await LevelingWorkerControlAsync(cmd,root); break;
                case "levelScoutKey": case "levelScoutSave": case "levelScoutSearch": case "levelScoutScan": case "levelLoadBaldr": case "levelSave": case "levelSources": case "levelMedical": case "levelReadInventory": case "levelSkip": case "levelRound": case "levelResult": case "levelCheck": case "levelOpen":
                    await HandleLevelingAsync(cmd,root); break;
                case "progressionGoals": SaveProgressionGoals(root); break;
                case "progressionDone": MarkProgressionTask(root); break;
                case "openGym": NavigateActive(TornRoutes.Gym); break;
                case "openEducation": NavigateActive(TornRoutes.Education); break;
                case "fullSync": await RefreshFullAsync(true); break;
                case "fastSync": await RefreshFastAsync(true); break;
                case "runGuide": await ExecuteRecommendedActionAsync(false, dashboardRunOnce: true); break;
                case "openGuide": await ExecuteRecommendedActionAsync(true); break;
                case "graffitiDebug": await CopyGraffitiDebugAsync(); break;
                case "openCrimes": NavigateActive(TornRoutes.Crimes); break;
                case "openTravel": NavigateActive("https://www.torn.com/travelagency.php"); break;
                case "travelRefresh": await RefreshTravelForecastAsync(true); break;
                case "lossShowWorkers": ShowLossWorkerBrowser(_lossWorkerDeck.SelectedIndex != 1); break;
                case "lossHideWorkers": HideLossWorkerBrowsers(); break;
                case "lossCloseWorkers": await StopBothLossWorkersAsync(); HideLossWorkerBrowsers(); break;
                case "itemSettings": SaveItemSettings(root); break;
                case "itemWatch": SaveItemWatch(root); break;
                case "itemRefresh": await PollItemIntelligenceAsync(); break;
                case "itemImport": ImportItemHistory(); break;
                case "itemExport": await ExportItemHistoryAsync(); break;
                case "itemMarketExport": await ExportMarketHistoryAsync(); break;
                case "intelCredential": SaveIntelCredential(root); break;
                case "intelCopyLogin": CopyIntelLogin(root); break;
                case "intelSignIn": await AddBrowserTabAsync("https://torn-intel.com/login"); break;
                case "intelClientRequest": await AddBrowserTabAsync("https://torn-intel.com/contact"); break;
                case "lossCreate": CreateDashboardLossOrder(root); break;
                case "lossSelect": SelectDashboardLossOrder(root); break;
                case "lossOrderPause": SelectDashboardLossOrder(root); ToggleLossPause(); UpdateModernDashboard(); break;
                case "lossDelete": DeleteDashboardLossOrder(root); break;
                case "lossResumeA": await ResumeLossWorkerAsync(true); UpdateModernDashboard(); break;
                case "lossResumeB": await ResumeLossWorkerAsync(false); UpdateModernDashboard(); break;
                case "lossTiming": await SaveLossAttackTimingAsync(root.GetProperty("milliseconds").GetInt32()); break;
                case "factionScan": await SyncFactionIntelligenceAsync(); break;
                case "factionFocus": SetFactionFocus(root); break;
                case "factionTask": SetFactionTask(root); break;
                case "factionCopy": CopyFactionReport(); break;
                case "openFaction": NavigateActive("https://www.torn.com/factions.php"); break;
                case "openMissions": NavigateActive("https://www.torn.com/loader.php?sid=missions"); break;
                case "companyAuto": SetCompanyAutoCollection(root); break;
                case "companyCopyReport": CopyCompanyReport(); break;
                case "factionActivity": SetFactionActivity(root); break;
                case "warScan": await ScanWarPayoutAsync(root); break;
                case "warPlan": SaveWarPayout(root); break;
                case "warCsv": ExportWarPayout(); break;
                case "companyCsv": ExportCompanyCsv(); break;
                case "stocksCsv": ExportStocksCsv(); break;
                case "openIntel": await AddBrowserTabAsync("https://torn-intel.com/dashboard/activity"); break;
                case "stocksScan": await ScanStocksAsync(); break;
                case "stockTarget": SaveStockTarget(root); break;
                case "openStocks": await AddBrowserTabAsync("https://www.torn.com/page.php?sid=stocks"); break;
                case "companyMarket": await ScanCompanyMarketAsync(); break;
                case "companyHistory": SelectCompanyHistory(root); break;
                case "companyScenario": CalculateCompanyScenario(root); break;
                case "companySync": await SyncCompanyIntelligenceAsync(); break;
                case "openCompany": NavigateActive("https://www.torn.com/companies.php"); break;
                case "companyStaffPlan": SaveCompanyStaffPlan(root); break;
                case "companyCosts": SaveCompanyCosts(root); break;
                case "travelArrivalScan": await ScanTravelShopAsync(true); break;
                case "travelArrivalReload": await RefreshTravelShopPageAsync(); break;
                case "travelCopyPlan": CopyTravelPlanForChat(); break;
                case "travelStatusCheck":
                    await RefreshFastAsync(true);
                    await ProbeTravelPageAsync();
                    UpdateModernDashboard();
                    break;
                case "setTheme":
                    if (root.TryGetProperty("theme", out var themeValue) && themeValue.ValueKind == JsonValueKind.String)
                    {
                        var selected = themeValue.GetString() ?? "";
                        if (selected is "midnight" or "obsidian" or "emerald" or "violet" or "ember" or "intel")
                        {
                            _settings.UiTheme = selected;
                            Storage.SaveSettings(_settings);
                            UpdateModernDashboard();
                        }
                    }
                    break;
                case "saveUpdateRepo":
                    if (root.TryGetProperty("repo", out var repoInput) && repoInput.ValueKind == JsonValueKind.String)
                        SaveUpdateRepository(repoInput.GetString() ?? "");
                    break;
                case "updateFromZip": PickCommanderZipUpdate(); break;
                case "checkGithubUpdate": await CheckCommanderGithubUpdateAsync(); break;
                case "restoreCommanderVersion":
                    if (root.TryGetProperty("version", out var restoreVersion) &&
                        restoreVersion.ValueKind == JsonValueKind.String)
                        RestoreSavedCommanderVersion(restoreVersion.GetString() ?? "");
                    break;
                case "travelRehab": SetTravelRehabFirst(root); await RefreshFastAsync(false); TickTravelState(); UpdateModernDashboard(); break;
                case "travelBuildPlan":
                    ApplyTravelSettingsFromDashboard(root);
                    await RefreshFastAsync(false);
                    TickTravelState();
                    RecalculateStoredTravelRows();
                    UpdateModernDashboard();
                    break;
                case "switchProfile":
                    if (root.TryGetProperty("profileId", out var pid) && pid.ValueKind == JsonValueKind.String)
                        await SwitchProfileAsync(pid.GetString() ?? "");
                    UpdateModernDashboard();
                    break;
                case "profileAdd":
                    await AddProfileAsync();
                    UpdateModernDashboard();
                    break;
                case "profileRename":
                    RenameCurrentProfile();
                    UpdateModernDashboard();
                    break;
                case "profileDelete":
                    await DeleteCurrentProfileAsync();
                    UpdateModernDashboard();
                    break;
                case "scriptOpen": await OpenScriptToolAsync(root.GetProperty("name").GetString() ?? ""); break;
                case "openRacing": NavigateActive(TornRoutes.Racing); break;
                case "tradeExport": ExportTradeResults(); break;
                case "tradeRecordSale": RecordCompletedTrade(root); break;
                case "companyAdTrial": SetCompanyAdTrial(root); break;
                case "tradeScan": await ScanTradingAsync(root); break;
                case "tradeOpen": NavigateActive(TornRoutes.ItemMarketFor((int)GoalNumber(root,"itemId",1,int.MaxValue))); break;
                case "cartScan": await ScanShoppingCartAsync(); UpdateModernDashboard(); break;
                case "cartCopy": CopyShoppingCartForChat(); break;
                case "saveMedicalSettings": await SaveMedicalSettingsAsync(root); break;
                case "lossStartA": await StartLossAutoCycleAsync(true); UpdateModernDashboard(); break;
                case "lossStartBoth": await StartBothLossWorkersAsync(); UpdateModernDashboard(); break;
                case "lossStartB": await StartLossAutoCycleAsync(false); UpdateModernDashboard(); break;
                case "lossPauseBoth": await PauseBothLossWorkersAsync(); UpdateModernDashboard(); break;
                case "lossStopBoth": await StopBothLossWorkersAsync(); UpdateModernDashboard(); break;
                case "openLossX": await AddBrowserTabAsync("https://www.torn.com/"); break;
                case "classic":
                    var page = root.TryGetProperty("page", out var p) ? p.GetString() ?? "Coach" : "Coach";
                    SelectClassicPage(page);
                    break;
                case "travelSettings":
                    ApplyTravelSettingsFromDashboard(root);
                    await RefreshTravelForecastAsync(false);
                    break;
                case "refresh": UpdateModernDashboard(); break;
            }
        }
        catch (Exception ex)
        {
            FlashStatus("Dashboard action failed: " + ex.Message);
        }
    }

    private void ApplyTravelSettingsFromDashboard(JsonElement root)
    {
        if (root.TryGetProperty("capacity", out var cap) && cap.TryGetInt32(out var n)) _settings.TravelCapacity = Math.Clamp(n, 1, 1000);
        if (root.TryGetProperty("flightType", out var ft) && ft.ValueKind == JsonValueKind.String)
        {
            var s = ft.GetString() ?? "Airstrip";
            if (new[] { "Standard", "Airstrip", "Private", "Business" }.Contains(s)) _settings.TravelFlightType = s;
        }
        if (root.TryGetProperty("marketTax", out var mt) && (mt.ValueKind is JsonValueKind.True or JsonValueKind.False)) _settings.TravelApplyMarketTax = mt.GetBoolean();
        if (root.TryGetProperty("horizon", out var h) && h.TryGetInt32(out var hours)) _settings.TravelForecastHorizonHours = Math.Clamp(hours, 6, 72);
        if (root.TryGetProperty("budget", out var budget) && budget.TryGetInt64(out var b))
            _settings.TravelCashBudget = Math.Clamp(b, 0, 1000000000L);
        if (root.TryGetProperty("book", out var book) && (book.ValueKind is JsonValueKind.True or JsonValueKind.False))
            _settings.TravelMailingBook = book.GetBoolean();
        if (root.TryGetProperty("stockOnly", out var stockOnly) && (stockOnly.ValueKind is JsonValueKind.True or JsonValueKind.False))
            _settings.TravelInStockOnly = stockOnly.GetBoolean();
        if (root.TryGetProperty("confidence", out var confidence) && confidence.TryGetDouble(out var conf))
            _settings.TravelMinConfidence = Math.Clamp(conf / 100.0, 0, 1);
        if (root.TryGetProperty("includeLow", out var low) && (low.ValueKind is JsonValueKind.True or JsonValueKind.False))
            _settings.TravelIncludeLowConfidence = low.GetBoolean();
        if (root.TryGetProperty("autoMonitor", out var am) && am.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.TravelAutoMonitor = am.GetBoolean();
        if (root.TryGetProperty("monitorMinutes", out var mm) && mm.TryGetInt32(out var interval))
            _settings.TravelMonitorIntervalMinutes = interval is 10 or 15 or 30 or 60 ? interval : 15;
        if (root.TryGetProperty("transferMinutes", out var tm) && tm.TryGetInt32(out var transfer))
            _settings.TravelTransferMinutes = Math.Clamp(transfer, 0, 120);
        if (root.TryGetProperty("planNow", out var pn) && pn.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.TravelPlanStartNow = pn.GetBoolean();
        if (root.TryGetProperty("pauseUntilHome", out var pu) && pu.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.TravelPauseUntilHome = pu.GetBoolean();
        if (root.TryGetProperty("showRouteMap", out var map) && map.ValueKind is JsonValueKind.True or JsonValueKind.False)
            _settings.TravelShowRouteMap = map.GetBoolean();
        if (root.TryGetProperty("planStart", out var ps) && ps.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(ps.GetString(), out var parsed))
            _settings.TravelPlanCustomStartUtc = parsed.UtcDateTime;
        Storage.SaveSettings(_settings);
        if (_travelBudget is not null) _travelBudget.Value = Math.Clamp(_settings.TravelCashBudget, (long)_travelBudget.Minimum, (long)_travelBudget.Maximum);
        if (_travelBook is not null) _travelBook.Checked = _settings.TravelMailingBook;
        if (_travelStockOnly is not null) _travelStockOnly.Checked = _settings.TravelInStockOnly;
        if (_travelConfidence is not null) _travelConfidence.Value = Math.Clamp((decimal)(_settings.TravelMinConfidence * 100.0), _travelConfidence.Minimum, _travelConfidence.Maximum);
        if (_travelIncludeLow is not null) _travelIncludeLow.Checked = _settings.TravelIncludeLowConfidence;
        if (_travelCapacity is not null) _travelCapacity.Value = Math.Clamp(_settings.TravelCapacity, (int)_travelCapacity.Minimum, (int)_travelCapacity.Maximum);
        if (_travelFlightType is not null) _travelFlightType.SelectedItem = _settings.TravelFlightType;
        if (_travelMarketTax is not null) _travelMarketTax.Checked = _settings.TravelApplyMarketTax;
        if (_travelHorizon is not null) _travelHorizon.Value = Math.Clamp(_settings.TravelForecastHorizonHours, (int)_travelHorizon.Minimum, (int)_travelHorizon.Maximum);
    }

    private void SelectClassicPage(string title)
    {
        if (_companionShell is not null && _companionShell.TabPages.Count > 1) _companionShell.SelectedIndex = 1;
        if (_classicTabs is null) return;
        for (var i = 0; i < _classicTabs.TabPages.Count; i++)
        {
            if (_classicTabs.TabPages[i].Text.Equals(title, StringComparison.OrdinalIgnoreCase))
            {
                _classicTabs.SelectedIndex = i;
                return;
            }
        }
    }

    private void UpdateModernDashboard()
    {
        if (!_modernDashboardReady || _modernDashboard?.CoreWebView2 is null) return;
        try
        {
            var cartEstimate = _shoppingScans.Values.Sum(x => x.PlannedCost);
            var cartNeed = _shoppingTargets.Sum(x => x.RemainingQuantity);
            var cartItems = _shoppingTargets.Count(x => x.RemainingQuantity > 0);
            var activeLoss = SelectedLossOrder();
            var meritCandidates = _snapshot.Awards
                .Where(x => !x.Achieved && x.AvailableForCurrentCrimeVersion && !x.ProgressConflict)
                .OrderByDescending(MeritPriorityScore)
                .Take(7)
                .Select(x => new
                {
                    name = x.Name,
                    progress = x.ProgressPercent is double pct ? $"{pct:0.#}%" : "unknown",
                    requirement = x.Description,
                    advice = x.Advice,
                    effort = x.Effort
                }).ToList();
            var crimes = _snapshot.Crimes.OrderByDescending(x => x.Skill).Take(8).Select(x => new
            {
                name = x.Name,
                skill = Math.Round(x.Skill, 2),
                nerve = x.NerveSpent,
                success = Math.Round(x.SuccessRate, 1)
            }).ToList();
            var travelCandidates=TravelIntelligenceCandidates();
            var travel = travelCandidates.Take(140).Select(x => new
            {
                item = x.ItemName,
                country = x.Country,
                category = x.Category,
                stock = x.Stock,
                status = x.StockStatus,
                isStale = x.IsStale || (x.Source.StartsWith("Commander evidence / ") ? x.StockUpdatedAtUtc<DateTime.UtcNow.AddMinutes(-30) : _travelForecast.RetrievedAtUtc<DateTime.UtcNow.AddMinutes(-30)),
                units = x.Units,
                cash = x.CashRequired,
                trip = x.TripProfit,
                roi = Math.Round(x.RoiPercent, 1),
                flight = x.FlightMinutesOneWay,
                profit = x.ProfitPerItem,
                profitHr = Math.Round(x.ProfitPerHour),
                restock = x.RestockWindowStartUtc.HasValue?x.RestockWindowStartUtc.Value.ToLocalTime().ToString("ddd h:mm tt")+" – "+x.RestockWindowEndUtc?.ToLocalTime().ToString("h:mm tt"):x.RestockAtUtc?.ToLocalTime().ToString("ddd h:mm tt"),
                leave = x.SuggestedDepartureUtc?.ToLocalTime().ToString("ddd h:mm tt"),
                arrival = x.ArrivalAtUtc?.ToLocalTime().ToString("ddd h:mm tt"),
                stockAgeMinutes = x.StockUpdatedAtUtc.HasValue ? (int)Math.Max(0, (DateTime.UtcNow - x.StockUpdatedAtUtc.Value).TotalMinutes) : (int?)null,
                priceAgeMinutes = x.PricingUpdatedAtUtc.HasValue ? (int)Math.Max(0, (DateTime.UtcNow - x.PricingUpdatedAtUtc.Value).TotalMinutes) : (int?)null,
                confidence = x.ConfidenceLabel,
                confidenceValue = x.Confidence,
                source = x.Source,
                note = x.Note,
                priceKnown = x.MarketValue > 0
            }).ToList();
            var itinerary = CurrentTravelPlan();
            var arrival = CurrentLandingAdvice();
            var hasKey = !string.IsNullOrWhiteSpace(CredentialManager.LoadApiKey(CurrentProfileId));

            var payload = new
            {
                type = "state",
                data = new
                {
                    version = "0.6.69",
                    buildPath = Environment.ProcessPath ?? AppContext.BaseDirectory,
                    profiles = _settings.Profiles.Select(x => new { id = x.Id, name = x.Name }).ToList(),
                    profileLimit = MaxSavedProfiles,
                    profileId = CurrentProfileId,
                    updateGithubRepo = _settings.UpdateGithubRepo,
                    rollbackVersions = GetSavedCommanderVersions()
                        .Select(x => new { version = x.Version,
                            label = (x.Parsed < RunningCommanderVersion ? "Revert to v" : "Switch to v") + x.Version })
                        .ToList(),
                    theme = _settings.UiTheme,
                    warPayout = WarPayoutData(),
                    stocks = new { report = StockIntelligence.Report(GetStockJournal()), scanning = _stockScanning },
                    company = CompanyDashboardData(),
                    faction = FactionDashboardData(),
                    progression = ProgressionDashboardData(),
                    apiActivity = new { syncing = _syncing, queued = TornRequestCoordinator.Shared.Queued, cooldownSeconds = TornRequestCoordinator.Shared.CooldownSeconds, cooldownUntil = TornRequestCoordinator.Shared.CooldownUntil.ToString("O") },
                    apiConnected = hasKey,
                    apiErrors = _snapshot.Errors.Count,
                    profile = CurrentProfile.Name,
                    player = _snapshot.PlayerName ?? CurrentProfile.Name,
                    playerId = _snapshot.PlayerId,
                    level = _snapshot.Level,
                    nerve = new { current = _snapshot.CurrentNerve, max = _snapshot.MaxNerve, nnb = _recommendation.EstimatedNaturalNerve },
                    energy = new { current = _snapshot.CurrentEnergy, max = _snapshot.MaxEnergy },
                    wallet = _snapshot.Wallet,
                    phase = _recommendation.Phase,
                    headline = _recommendation.Headline,
                    why = _recommendation.Why,
                    nerveAdvice = _recommendation.NerveAdvice,
                    graffitiStatus = _lastGraffitiActionStatus,
                    guideBusy = _guideActionBusy,
                    guide = _recommendation.GuidedAction is null ? null : new
                    {
                        crime = _recommendation.GuidedAction.Crime,
                        summary = _recommendation.GuidedAction.Summary,
                        target = _recommendation.GuidedAction.Target,
                        option = _recommendation.GuidedAction.Option,
                        mode = NormalizeGuideActionMode(_settings.GuideActionMode)
                    },
                    cart = new { items = cartItems, remaining = cartNeed, estimate = cartEstimate, ignoreLimits = _settings.ShoppingIgnoreLimits },
                    itemIntelligence = ItemIntelligenceDashboard(),
                    lossOrders = _lossOrders.Select(x => new { id=x.Id, buyer=x.BuyerName, target=x.TargetPlayerId, requested=x.RequestedLosses, completed=x.CompletedTotal, status=x.Status }).ToList(),
                    loss = activeLoss is null ? null : new
                    {
                        id = activeLoss.Id,
                        buyer = activeLoss.BuyerName,
                        target = activeLoss.TargetPlayerId,
                        completed = activeLoss.CompletedTotal,
                        requested = activeLoss.RequestedLosses,
                        remaining = activeLoss.Remaining,
                        gross = activeLoss.Gross,
                        status = activeLoss.Status
                    },
                    merits = new { earned = _snapshot.Awards.Count(x => x.Achieved), total = _snapshot.Awards.Count, available = _snapshot.MeritAvailable, rows = meritCandidates },
                    crimes,
                    trading = new { entries = _trades.Count(x=>x.ProfileId==CurrentProfileId), profit = _trades.Where(x=>x.ProfileId==CurrentProfileId).Sum(x => x.ProfitAfterFees??0), unknownFees = _trades.Count(x=>x.ProfileId==CurrentProfileId&&!x.SellingFees.HasValue), lastReceipt = _trades.LastOrDefault(x=>x.ProfileId==CurrentProfileId)?.Id, advice=TradingDashboardData() },
                    scripts = ScriptToolsDashboardData(),
                    casino = new { sessions = _casino.Count, net = _casino.Sum(x => x.ProfitLoss) },
                    medical=MedicalDashboard(),
                    leveling=LevelingDashboard(),
                    lossAttackDelayMs = Math.Clamp(_settings.LossAttackDelayMs, 500, 5000),
                    workers = new { a = _lossWorkerA?.LastStatus ?? "Not initialized", b = _lossWorkerB?.LastStatus ?? "Not initialized" },
                    travel = new
                    {
                        source = _travelForecast.Source,
                        monitor = _settings.TravelAutoMonitor,
                        monitorMinutes = Math.Clamp(_settings.TravelMonitorIntervalMinutes, 10, 60),
                        nextPollAt = _settings.TravelAutoMonitor && _travelLastAttemptUtc != DateTime.MinValue
                            ? _travelLastAttemptUtc.AddMinutes(Math.Clamp(_settings.TravelMonitorIntervalMinutes, 10, 60)).ToLocalTime().ToString("g") : null,
                        lastPollAt = _travelLastAttemptUtc == DateTime.MinValue ? null : _travelLastAttemptUtc.ToLocalTime().ToString("g"),
                        refreshing = _travelRefreshing,
                        rehabFirst = TravelRehabFirst,
                        rehabStay = _settings.TravelRehabStayMinutes,
                        planNow = _settings.TravelPlanStartNow,
                        planCustomStart = _settings.TravelPlanCustomStartUtc == DateTime.MinValue ? null : _settings.TravelPlanCustomStartUtc.ToString("O"),
                        transferMinutes = _settings.TravelTransferMinutes,
                        showRouteMap = _settings.TravelShowRouteMap,
                        situation = new
                        {
                            state = _travelSituation.State,
                            description = _travelSituation.Description,
                            source = _travelSituation.Source,
                            takeoffAt = _travelSituation.DepartUtc?.ToLocalTime().ToString("ddd h:mm tt"),
                            landingAt = _travelSituation.ArrivalUtc?.ToLocalTime().ToString("ddd h:mm tt"),
                            returnAt = _travelSituation.ReturnUtc?.ToLocalTime().ToString("ddd h:mm tt"),
                            lastCheckedAt = _travelSituation.UpdatedUtc == DateTime.MinValue ? null : _travelSituation.UpdatedUtc.ToLocalTime().ToString("h:mm tt")
                        },
                        pauseUntilHome = _settings.TravelPauseUntilHome,
                        waitAdvice = TravelWaitData(),
                        preview = TravelPreviewEngine.Build(travelCandidates, _settings,
                            DateTime.UtcNow, _travelForecast.Error, _travelForecast.Diagnostics),
                        arrival = new {
                            status = arrival.Status, action = arrival.Action, country = arrival.Country,
                            cash = arrival.Cash, slots = arrival.SlotsLeft, message = arrival.Message,
                            nextCheck = arrival.NextCheck,
                            recentChanges = arrival.RecentChanges,
                            basket = arrival.BasketSummary,
                            basketProfit = arrival.BasketNet,
                            basketUnits = arrival.BasketUnits,
                            seen = arrival.CheckedUtc?.ToLocalTime().ToString("ddd h:mm tt"),
                            choices = arrival.Choices.Select(x => new {
                                item = x.Item, stock = x.Stock, units = x.Units, cost = x.Cost,
                                profit = x.NetProfit, profitUnit = x.ProfitPerUnit,
                                restock = x.RestockText, evidence = x.RestockEvidence,
                                priceEvidence = x.PriceEvidence, estimate = x.RestockIsEstimate,
                                observedChange = x.ObservedChange
                            }).ToList()
                        },
                        itinerary = new
                        {
                            start = itinerary.StartUtc.ToLocalTime().ToString("ddd MMM d, h:mm tt"),
                            end = itinerary.EndUtc.ToLocalTime().ToString("ddd MMM d, h:mm tt"),
                            profit = itinerary.EstimatedProfit,
                            score = itinerary.RiskWeightedScore,
                            projected = itinerary.ProjectedLegCount,
                            note = itinerary.Note,
                            legs = itinerary.Legs.Select(x => new {
                                kind = x.Kind, number = x.Number, country = x.Country, item = x.Item, units = x.Units,
                                departure = x.DepartUtc.ToLocalTime().ToString("ddd h:mm tt"),
                                arrival = x.ArriveUtc.ToLocalTime().ToString("ddd h:mm tt"),
                                back = x.ReturnUtc.ToLocalTime().ToString("ddd h:mm tt"),
                                cash = x.CashRequired, profit = x.EstimatedProfit,
                                riskAdjusted = x.RiskAdjustedProfit,
                                availability = x.Availability, confidence = Math.Round(x.PlanningConfidence * 100)
                            }).ToList()
                        },
                        updated = _travelForecast.RetrievedAtUtc == DateTime.MinValue ? null : _travelForecast.RetrievedAtUtc.ToLocalTime().ToString("g"),
                        ageMinutes = _travelForecast.RetrievedAtUtc == DateTime.MinValue ? (int?)null : (int)Math.Max(0, (DateTime.UtcNow - _travelForecast.RetrievedAtUtc).TotalMinutes),
                        warning = travelCandidates.Any(x => x.IsStale),
                        error = _travelForecast.Error,
                        hasRows = travelCandidates.Count > 0,
                        hasError = !string.IsNullOrWhiteSpace(_travelForecast.Error),
                        capacity = _settings.TravelCapacity,
                        flightType = _settings.TravelFlightType,
                        marketTax = _settings.TravelApplyMarketTax,
                        horizon = _settings.TravelForecastHorizonHours,
                        budget = _settings.TravelCashBudget,
                        book = _settings.TravelMailingBook,
                        stockOnly = _settings.TravelInStockOnly,
                        confidence = Math.Round(_settings.TravelMinConfidence * 100),
                        includeLow = _settings.TravelIncludeLowConfidence,
                        rows = travel
                    }
                }
            };
            _modernDashboard.CoreWebView2.PostWebMessageAsJson(DashboardJson.Serialize(payload));
        }
        catch { }
    }

    private static string ModernDashboardHtml() => """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Torn Commander</title>
<style>
:root{color-scheme:dark;--bg:#0a0e15;--side:#0d131c;--panel:#121b29;--panel2:#162233;--line:#253448;--line2:#31465d;--text:#e9f2fc;--muted:#9badc0;--blue:#69b9ff;--green:#69d8a4;--amber:#f5c276;--red:#f08490;--shadow:0 12px 30px #0003}
*{box-sizing:border-box}html,body{margin:0;min-width:0;height:100%;font:13px/1.48 'Segoe UI',Inter,system-ui,sans-serif;background:var(--bg);color:var(--text)}button,input,select{font:inherit}button{cursor:pointer}button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid var(--blue);outline-offset:2px}a{color:var(--blue)}.shell{height:100vh;display:grid;grid-template-columns:162px minmax(0,1fr);overflow:hidden}.side{padding:19px 10px 12px;background:var(--side);border-right:1px solid var(--line);display:flex;flex-direction:column;gap:3px;min-width:0;overflow-y:auto}.brand{padding:5px 10px 20px;font-weight:850;font-size:14px;letter-spacing:.11em;line-height:1.28}.brand small{font-size:10px;display:block;margin-top:5px;letter-spacing:.16em;color:var(--green)}.nav{display:flex;align-items:center;gap:8px;width:100%;border:1px solid transparent;background:transparent;border-radius:9px;color:var(--muted);text-align:left;padding:9px 11px;font-weight:650;white-space:nowrap}.nav:hover{background:#192538;color:var(--text)}.nav.active{background:linear-gradient(90deg,#204064,#16283e);color:#fff;border-color:#294768}.nav i{width:15px;text-align:center;font-style:normal;font-size:14px}.navitems{display:contents}.navtoggle{display:none}.sidefoot{margin-top:auto;font-size:10px;color:#71879b;padding:16px 9px 3px}.content{min-width:0;height:100vh;overflow:auto;background:radial-gradient(ellipse 65% 20% at 90% 0%,#17304855,transparent 80%),var(--bg);padding:16px 20px 30px}.bar{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-bottom:17px;flex-wrap:wrap;padding-bottom:14px;border-bottom:1px solid var(--line)}.barleft,.bar-actions{display:flex;align-items:center;flex-wrap:wrap;gap:8px}.selectmini{max-width:180px;min-width:118px;padding:8px 28px 8px 10px;border-radius:9px;border:1px solid var(--line2);background:#131e2e;color:var(--text)}.eyebrow{font-size:10px;font-weight:750;color:var(--muted);text-transform:uppercase;letter-spacing:.11em}.bigtitle{font-size:23px;font-weight:800;letter-spacing:-.035em;line-height:1.2}.sub{font-size:12px;color:var(--muted);line-height:1.55}.top{display:flex;align-items:center;justify-content:space-between;gap:10px;flex-wrap:wrap;margin-bottom:16px}.actions{display:flex;align-items:center;gap:7px;flex-wrap:wrap}.btn{border:1px solid #344b66;background:#1b2a3d;border-radius:9px;color:#e9f4ff;padding:8px 11px;font-weight:680;line-height:1.25}.btn:hover{background:#253951;border-color:#588bb6}.btn.primary{background:#256fb8;border-color:#388ed4;color:white}.btn.primary:hover{background:#3084d3}.btn.good{background:#18523e;border-color:#2b8a62}.btn.warn{background:#62451e;border-color:#927035}.btn.small{padding:6px 9px;font-size:11px}.pill{display:inline-flex;align-items:center;gap:5px;border:1px solid #34516d;background:#1b3047;border-radius:50px;padding:3px 9px;white-space:nowrap;color:#c0ddf6;font-size:11px;font-weight:700}.pill.good{border-color:#256147;background:#17392d;color:#a5efc5}.pill.warn{border-color:#79613c;background:#44311d;color:#f9cf8d}.pill.bad{border-color:#83454e;background:#49232d;color:#ffb3bc}.pill.muted{border-color:var(--line);background:#182433;color:var(--muted)}.grid{display:grid;grid-template-columns:repeat(12,minmax(0,1fr));gap:11px}.span3{grid-column:span 3}.span4{grid-column:span 4}.span5{grid-column:span 5}.span6{grid-column:span 6}.span7{grid-column:span 7}.span8{grid-column:span 8}.span12{grid-column:span 12}.card{background:linear-gradient(180deg,#151f2edb,#121a27ed);border:1px solid var(--line);border-radius:13px;padding:15px;box-shadow:var(--shadow);min-width:0}.hero{background:radial-gradient(circle at 90% 0%,#2b659a45,transparent 43%),linear-gradient(155deg,#182b41,#121c2a)}.section{font-size:14px;font-weight:770;margin-bottom:8px}.metric{font-size:23px;font-weight:800;letter-spacing:-.03em;font-variant-numeric:tabular-nums;overflow-wrap:anywhere}.metric.s{font-size:20px}.muted{color:var(--muted)}.money{font-variant-numeric:tabular-nums}.notice{border-radius:10px;background:#26231d;border:1px solid #59472f;color:#ecd5a9;padding:10px 12px;font-size:12px;line-height:1.5}.notice.info{background:#162436;border-color:#345273;color:#b9dbf4}.notice.good{background:#153328;border-color:#306a50;color:#ace2c4}.minirow{display:flex;align-items:center;justify-content:space-between;gap:10px;padding:10px;background:#101b2a;border:1px solid #233347;border-radius:9px}.list{display:flex;flex-direction:column;gap:8px}.page{display:none}.page.active{display:block}.field{min-width:0;display:flex;flex-direction:column;gap:4px}.field>label{font-size:11px;color:var(--muted);font-weight:650}.formgrid>label>input,.field input[type=number],.field input[type=text],.field input[type=datetime-local],.field select{width:100%;min-width:0;background:#0e1724;border:1px solid #334963;border-radius:9px;color:var(--text);padding:9px}.formgrid{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:10px}.options{display:flex;align-items:center;gap:14px;flex-wrap:wrap;margin-top:11px}.toggle{display:flex;align-items:center;gap:6px;color:var(--muted);font-size:12px}.toggle input{accent-color:#3295e7}.travelbar{display:flex;flex-wrap:wrap;align-items:center;gap:8px;margin:13px 0}.travelbar .field{flex:1 1 130px}.travelbar .field.search{flex:2 1 160px}.opportunity{border:1px solid #26394e;border-radius:11px;padding:12px;background:#101a28;display:grid;grid-template-columns:minmax(0,1.5fr) minmax(115px,1fr);gap:8px}.opportunity:hover{border-color:#3b5772}.opportunity .name{font-weight:780;font-size:14px;overflow-wrap:anywhere}.opportunity .right{text-align:right;min-width:0}.opportunity .moneybig{font-weight:800;font-size:17px;color:#b8ebd1}.opportunity .details{grid-column:1/-1;border-top:1px solid #243348;padding-top:8px;display:flex;flex-wrap:wrap;gap:6px 15px;color:var(--muted);font-size:11px}.opportunity .details strong{color:#dae7f6;font-weight:690}.empty{padding:18px 10px;color:var(--muted);text-align:center;border:1px dashed #3c4e63;border-radius:10px}.smallgrid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px}.tleft{min-width:0}.tleft .sub{margin-top:4px}.divider{height:1px;background:var(--line);margin:12px 0}.twocol{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}.footerline{padding:15px 0;color:#7990a4;font-size:11px}.mobileNav{display:none;margin-bottom:12px;padding:8px 10px;border-radius:10px;border:1px solid var(--line);background:#121c2b}.mobileNav label{color:var(--muted);font-size:10px;font-weight:800;letter-spacing:.1em;white-space:nowrap}.mobileNav select{background:#18283d;border:1px solid #34516d;color:var(--text);border-radius:8px;padding:8px 10px;flex:1;min-width:0}.top>div{min-width:0}.top .actions{flex-wrap:wrap}.page{min-width:0}.statusline{font-size:11px;color:var(--muted);margin-top:6px;overflow-wrap:anywhere;word-break:break-word}.barleft .btn{white-space:nowrap}.barleft .eyebrow{flex:0 0 auto}.profile-list{display:flex;flex-direction:column;gap:7px;margin-top:11px}.profile-entry{display:flex;align-items:center;gap:10px;justify-content:space-between;padding:10px 12px;background:#101b29;border:1px solid var(--line);border-radius:9px;overflow-wrap:anywhere}.profile-entry.active{border-color:#47749d;background:#172a40}.profile-entry .small{font-size:11px;color:var(--muted)}.sub,.notice,.minirow,.details,.field>label{overflow-wrap:anywhere}.barleft{min-width:0}.bar-actions{justify-content:flex-end}.itin-step{padding:12px;border:1px solid var(--line);border-radius:11px;background:#111e2d;display:grid;grid-template-columns:32px minmax(0,1fr);gap:12px;min-width:0}.itin-index{display:grid;place-items:center;background:#223d5b;border-radius:9px;font-weight:850;color:#bfe3ff;height:32px}.itin-route{font-size:14px;font-weight:780;overflow-wrap:anywhere}.itin-meta{display:flex;gap:6px 16px;flex-wrap:wrap;margin-top:8px;font-size:12px;color:var(--muted)}.itin-meta strong{color:var(--text)}.itin-forecast{border-left:3px solid var(--amber)}.itin-times{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;margin-top:9px}.itin-times>div{min-width:0;background:#0c1723;border:1px solid #25364b;border-radius:8px;padding:8px}.itin-times .eyebrow{font-size:9px}.itin-times strong{font-size:12px;display:block;margin-top:2px;overflow-wrap:anywhere}.travelmeta{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px;margin-top:10px}.statbox{padding:10px 11px;border:1px solid #25384d;border-radius:10px;background:#101b29}.statbox .k{font-size:10px;text-transform:uppercase;letter-spacing:.1em;color:var(--muted);font-weight:700}.statbox .v{font-size:13px;font-weight:750;margin-top:4px;overflow-wrap:anywhere}
.travel-state{margin-top:12px;padding:14px 16px;background:linear-gradient(110deg,#143047,#0e1e2e);border:1px solid #375877;border-radius:12px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}.travel-state .section{margin:4px 0}.travel-state.flight{border-color:#b78a43;background:linear-gradient(110deg,#392d1e,#182331)}.travel-state.hold{border-color:#66546c;background:#211c30}.travel-state.ready{border-color:#307459}.formgrid{display:grid;gap:12px;grid-template-columns:repeat(auto-fit,minmax(156px,1fr))}.formgrid>label{display:flex;flex-direction:column;gap:6px;min-width:0}.formgrid input,.formgrid select{width:100%;min-width:0}.formgrid>label.toggle{flex-direction:row;align-items:center}.formgrid>label.toggle input{width:auto}.card.hero .grid .span4{min-width:0}@media(min-width:481px) and (max-width:1050px){.content{font-size:14px}.content .sub{font-size:12.5px}.content .btn{padding:9px 11px}.content .card{border-radius:12px}#itineraryCard .grid .span4{grid-column:span 4}#itineraryCard .grid .card{padding:11px}#itineraryCard .metric.s{font-size:18px}#itineraryCard .grid .sub{font-size:11px}}
@media(max-width:1050px){.shell{grid-template-columns:1fr;grid-template-rows:auto minmax(0,1fr)}.side{display:block;padding:7px 10px;overflow:visible;border:0;border-bottom:1px solid var(--line)}.navitems{display:none;grid-template-columns:repeat(auto-fit,minmax(112px,1fr));gap:4px;padding-top:8px}.side.expanded .navitems{display:grid}.brand,.sidefoot{display:none}.navtoggle{display:flex;align-items:center;gap:9px;width:100%;border:1px solid var(--line);border-radius:8px;background:#132035;color:var(--text);padding:9px 11px;text-align:left;font-weight:700}.navtoggle span{margin-left:auto;color:var(--blue);font-weight:600}.nav{width:100%;min-width:0;padding:8px 10px}.content{height:auto;min-height:0;padding:14px}.span3{grid-column:span 6}.span4,.span5,.span6,.span7,.span8{grid-column:span 12}}
@media(max-width:540px){.mobileNav{flex-wrap:wrap}.mobileNav select{width:100%}.bigtitle{font-size:19px}.span3{grid-column:span 6}.card{padding:12px}.content{padding:11px}.btn{padding:8px 9px}.bar{align-items:flex-start}.barleft,.bar-actions{width:100%}.barleft{gap:6px}.eyebrow{width:100%}.twocol{grid-template-columns:1fr}.opportunity{grid-template-columns:minmax(0,1fr)}.opportunity .right{text-align:left}.bar-actions{width:100%;justify-content:flex-start}.selectmini{max-width:none;flex:1 1 180px}.metric{font-size:20px}}
@media(max-width:420px){.itin-times{grid-template-columns:1fr}.span3{grid-column:span 12}.smallgrid{grid-template-columns:1fr}}

html[data-theme="intel"]{--bg:#09090f;--side:#101017;--panel:#13131e;--panel2:#1a1b29;--line:#292a3b;--line2:#3b4059;--text:#f2f5fc;--muted:#a3a7bc;--blue:#60c7fa;--green:#2ed5b0;--amber:#f5c56b;--red:#fa718c;--theme-action:#227ead;--theme-action-hover:#2999cd;--theme-active:#173446;--theme-nav-hover:#1b2432;--theme-field:#0e101b;--theme-card:#151521;--theme-surface:#141927;--theme-hero:#162738;--theme-glow:#27c6da18}
html[data-theme="intel"] .card{box-shadow:0 12px 32px #0004;border-radius:15px}
html[data-theme="intel"] .hero{border-top:2px solid #2acbb5}
html[data-theme="intel"] .nav.active{box-shadow:inset 3px 0 #50c4f4}
html[data-theme="intel"] .brand small{color:#38d6b0}
html[data-theme="intel"] .bigtitle{letter-spacing:-.025em}
.activity-heatmap{display:grid;grid-template-columns:repeat(24,minmax(5px,1fr));gap:2px;margin:12px 0}.activity-cell{height:14px;border:1px solid var(--line);border-radius:2px;background:var(--panel2)}
.stock-chart{width:100%;height:90px;display:block;margin:10px 0}.stock-table{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(420px,100%),1fr));gap:12px}.stock-target{border-color:var(--green)}.stock-label{font-size:12px;font-weight:700;color:var(--muted)}
/* Commander palettes: one local setting, CSS variables for all modern sections. */
html[data-theme="obsidian"]{--bg:#080b10;--side:#0b1017;--panel:#151b24;--panel2:#1c2631;--line:#2c3645;--line2:#40516a;--text:#eef4fd;--muted:#aab8c8;--blue:#b6d9fa;--green:#80e6c0;--amber:#e6c08a;--red:#f095a1;--theme-action:#536d8c;--theme-action-hover:#647f9d;--theme-active:#263a4e;--theme-nav-hover:#202a38;--theme-field:#111923;--theme-card:#151b24;--theme-surface:#151f2b;--theme-hero:#202e3d;--theme-glow:#7098bf1c}
html[data-theme="emerald"]{--bg:#081510;--side:#0b1b14;--panel:#10241c;--panel2:#193329;--line:#295246;--line2:#3c7462;--text:#e2f9eb;--muted:#a6c8b5;--blue:#65dcb0;--green:#7ae6a5;--amber:#f0c576;--red:#f78f97;--theme-action:#17694b;--theme-action-hover:#208a62;--theme-active:#1a4936;--theme-nav-hover:#1c3b2e;--theme-field:#0c1e17;--theme-card:#11261c;--theme-surface:#142a21;--theme-hero:#1a4030;--theme-glow:#49ce9b1b}
html[data-theme="violet"]{--bg:#100d1a;--side:#171126;--panel:#20192d;--panel2:#2a213e;--line:#473653;--line2:#654e79;--text:#f7edff;--muted:#c6b3d4;--blue:#c2a0fa;--green:#8de6c6;--amber:#f2c68a;--red:#ffa2b7;--theme-action:#67419a;--theme-action-hover:#8055b8;--theme-active:#3b2859;--theme-nav-hover:#332443;--theme-field:#1a1427;--theme-card:#21192e;--theme-surface:#281d39;--theme-hero:#3b2859;--theme-glow:#a476ef1a}
html[data-theme="ember"]{--bg:#17100e;--side:#201510;--panel:#2b1f1a;--panel2:#362820;--line:#5a4034;--line2:#805b49;--text:#ffefe4;--muted:#d3b5a5;--blue:#ffaa74;--green:#9adca2;--amber:#ffd18b;--red:#f3938c;--theme-action:#a4522c;--theme-action-hover:#c16a3c;--theme-active:#5b3625;--theme-nav-hover:#452c22;--theme-field:#231813;--theme-card:#2b1f1a;--theme-surface:#32241d;--theme-hero:#533427;--theme-glow:#f4a06416}
html:not([data-theme]),html[data-theme="midnight"]{--theme-action:#256fb8;--theme-action-hover:#3084d3;--theme-active:#204064;--theme-nav-hover:#192538;--theme-field:#0e1724;--theme-card:#151f2e;--theme-surface:#182b41;--theme-hero:#1b3550;--theme-glow:#17304855}
html[data-theme]{background:var(--bg)}
html[data-theme] .side{background:var(--side)}
html[data-theme] .content{background:radial-gradient(ellipse 68% 25% at 92% 0%,var(--theme-glow),transparent 82%),var(--bg)}
html[data-theme] .card{background:linear-gradient(165deg,var(--theme-card),var(--panel));border-color:var(--line)}
html[data-theme] .card.hero{background:radial-gradient(circle at 90% 0%,var(--theme-glow),transparent 55%),linear-gradient(145deg,var(--theme-hero),var(--panel))}
html[data-theme] .nav:hover{background:var(--theme-nav-hover)}
html[data-theme] .nav.active{background:var(--theme-active);border-color:var(--line2);color:var(--text)}
html[data-theme] .btn{background:var(--panel2);border-color:var(--line2);color:var(--text)}
html[data-theme] .btn:hover{background:var(--theme-nav-hover);border-color:var(--blue)}
html[data-theme] .btn.primary{background:var(--theme-action);border-color:var(--blue);color:#fff}
html[data-theme] .btn.primary:hover{background:var(--theme-action-hover)}
html[data-theme] .btn.good{background:#18523e;border-color:var(--green)}
html[data-theme] .btn.warn{background:#62451e;border-color:var(--amber)}
html[data-theme] .pill:not(.good):not(.warn):not(.bad){background:var(--panel2);color:var(--muted);border-color:var(--line2)}
html[data-theme] .selectmini,html[data-theme] .mobileNav,html[data-theme] .mobileNav select,html[data-theme] .field input,html[data-theme] .field select{background:var(--theme-field);color:var(--text);border-color:var(--line2)}
html[data-theme] .profile-entry,html[data-theme] .opportunity,html[data-theme] .itin-step,html[data-theme] .minirow,html[data-theme] .statbox,html[data-theme] .itin-times>div{background:var(--theme-surface);border-color:var(--line)}
html[data-theme] .profile-entry.active{background:var(--theme-active);border-color:var(--blue)}
html[data-theme] .itin-index{background:var(--theme-active);color:var(--text)}
html[data-theme] .opportunity .moneybig{color:var(--green)}
html[data-theme] .toggle input{accent-color:var(--blue)}
/* Responsive, keyboard-focusable theme tiles; no wide fixed-width controls. */
.theme-options{display:grid;grid-template-columns:repeat(auto-fit,minmax(118px,1fr));gap:9px;margin-top:12px}
.theme-tile{background:var(--panel2);border:1px solid var(--line2);border-radius:11px;padding:10px;display:flex;flex-direction:column;gap:7px;color:var(--text);text-align:left;min-width:0;min-height:100px;transition:border-color .14s,transform .14s}
.theme-tile:hover{border-color:var(--blue);transform:translateY(-1px)}
.theme-tile[aria-pressed="true"]{border:2px solid var(--blue);padding:9px;box-shadow:0 0 0 2px var(--theme-glow)}
.theme-sample{height:31px;border-radius:7px;display:flex;gap:5px;align-items:center;padding:7px;background:var(--sample-bg);border:1px solid var(--sample-line)}
.theme-sample>i{display:block;height:17px;flex:1;border-radius:4px;background:var(--sample-panel)}
.theme-sample>i:last-child{flex:0 0 23%;background:var(--sample-accent)}
.theme-name{font-weight:760;font-size:12px}.theme-caption{font-size:10px;color:var(--muted);line-height:1.35}
.theme-tile[data-choice="midnight"]{--sample-bg:#0a0e15;--sample-panel:#162233;--sample-accent:#69b9ff;--sample-line:#253448}
.theme-tile[data-choice="obsidian"]{--sample-bg:#080b10;--sample-panel:#1c2631;--sample-accent:#b6d9fa;--sample-line:#40516a}
.theme-tile[data-choice="emerald"]{--sample-bg:#081510;--sample-panel:#193329;--sample-accent:#65dcb0;--sample-line:#3c7462}
.theme-tile[data-choice="violet"]{--sample-bg:#100d1a;--sample-panel:#2a213e;--sample-accent:#c2a0fa;--sample-line:#654e79}
.theme-tile[data-choice="intel"]{--sample-bg:#09090f;--sample-panel:#151521;--sample-accent:#60c7fa;--sample-line:#292a3b}
.theme-tile[data-choice="ember"]{--sample-bg:#17100e;--sample-panel:#362820;--sample-accent:#ffaa74;--sample-line:#805b49}
@media(max-width:360px){.theme-options{grid-template-columns:repeat(2,minmax(0,1fr))}}
.itin-line-map{margin:10px 0;padding:13px;background:var(--surface,#0c1624);border:1px solid var(--border,#2e4156);border-radius:12px;min-width:0;overflow:hidden}.itin-line-map[hidden]{display:none}.itin-track{display:flex;align-items:center;gap:0;overflow-x:auto;padding:16px 4px 12px;min-width:0;scrollbar-width:thin}.itin-node{appearance:none;background:var(--panel,#132136);border:1px solid var(--border,#36516e);border-radius:9px;color:var(--text,#e0ebf4);padding:8px 10px;flex:0 0 auto;max-width:160px;min-width:75px;font:inherit;text-align:center;cursor:pointer}.itin-node:focus-visible{outline:2px solid var(--accent,#3c8ccd)}.itin-node span{display:block;font-size:10px;color:var(--muted,#aab8c8)}.itin-node.low{border-style:dashed}.itin-line{height:2px;background:var(--accent,#3b83bb);min-width:21px;width:31px;flex:0 0 auto;position:relative}.itin-line:after{content:'';position:absolute;right:0;top:-4px;border-left:6px solid var(--accent,#3b83bb);border-top:5px solid transparent;border-bottom:5px solid transparent}.itin-step{scroll-margin-top:24px}
.adviser-fields{margin:10px 0 15px;align-items:end}.adviser-fields>label{font-size:12px;color:var(--muted)}
.adviser-fields input:not([type=checkbox]),.adviser-fields select,.formgrid input:not([type=checkbox]){background:var(--theme-field,#131e2e);color:var(--text);border:1px solid var(--line2);border-radius:8px;padding:8px;min-height:36px}
.adviser-fields .btn{align-self:end;min-height:36px}.adviser-fields>label.toggle{min-height:36px}
/* Rich dashboard layout: original vector art, charts and quieter supporting detail. */
:root{--shadow:0 8px 30px #0002}
.content{padding-top:20px}.bar{position:relative}.brand{letter-spacing:.14em}.brand small{color:var(--blue)}
.page>.top{padding:8px 0 14px;border-bottom:1px solid var(--line);margin-bottom:20px}
.bigtitle{font-size:27px;letter-spacing:-.04em}.page>.top .sub{margin-top:5px}
.card{padding:19px;border-radius:15px}.grid{gap:14px}.metric{font-size:29px;line-height:1.3;margin:5px 0}.metric.s{font-size:24px}
.card .eyebrow{font-size:10px;letter-spacing:.13em}.section{font-size:16px}
.card:has(>.metric){position:relative;overflow:hidden}.card:has(>.metric):after{content:'';position:absolute;width:70px;height:70px;border-radius:50%;right:-15px;top:-15px;background:var(--blue);opacity:.055;pointer-events:none}
.btn{min-height:34px;transition:background .16s,border-color .16s,box-shadow .16s}.btn.primary{box-shadow:0 3px 14px #38bdf815}
.nav.active i{color:var(--blue)}.nav{padding-top:10px;padding-bottom:10px}.minirow{border-color:transparent;background:var(--theme-surface);padding:12px}.list{gap:10px}
.city-hero{position:relative;isolation:isolate;overflow:hidden;min-height:248px;margin-bottom:22px;padding:30px;border:1px solid #32526b;border-radius:20px;background:radial-gradient(ellipse at 85% 0%,#1b628d55,transparent 65%),linear-gradient(115deg,#102130,#10121f);box-shadow:0 18px 45px #0003}
.city-art{position:absolute;right:-30px;bottom:0;width:76%;height:100%;z-index:-1;opacity:.8;mask-image:linear-gradient(90deg,transparent,#000 48%)}
.city-copy{max-width:570px}.city-copy h1{font-size:clamp(26px,3.5vw,43px);line-height:1.12;letter-spacing:-.055em;margin:15px 0 12px;color:#f1f8ff}
.city-copy h1 em{font-style:normal;color:#7edbf4}.city-copy p{max-width:390px;font-size:13px;color:#a9becf;line-height:1.6;margin-bottom:19px}.city-caption{position:absolute;right:20px;bottom:17px;font-size:8px;color:#7aacc5;letter-spacing:.2em}
.status-dot{display:inline-block;width:6px;height:6px;background:#43d6b0;border-radius:50%;margin-right:6px;box-shadow:0 0 12px #43d6b055}
.insight-head{display:flex;justify-content:space-between;gap:12px;align-items:flex-start;flex-wrap:wrap;margin-bottom:14px}
.chart-range{display:flex;gap:3px;padding:3px;border:1px solid var(--line);border-radius:9px;background:var(--theme-field)}
.chart-range button{border:0;background:none;border-radius:6px;color:var(--muted);font:inherit;font-size:11px;padding:5px 10px}.chart-range button[aria-pressed=true]{background:var(--theme-active);color:var(--text)}
.insight-chart{min-height:205px;min-width:0;overflow-x:auto}.insight-chart svg{min-width:460px;width:100%;height:auto;min-height:150px;display:block}.chart-grid{stroke:var(--line);stroke-dasharray:3 5;stroke-width:1}.chart-axis{fill:var(--muted);font-size:16px}.chart-legend{display:flex;gap:16px;flex-wrap:wrap;color:var(--muted);font-size:11px;margin-top:8px}.chart-legend i{width:8px;height:8px;border-radius:50%;display:inline-block;margin-right:5px}
.chart-empty{min-height:180px;display:grid;place-content:center;text-align:center;border:1px dashed var(--line2);border-radius:10px;color:var(--muted);font-size:12px;padding:20px}
.rehab-strip{display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:16px;padding:16px;margin-bottom:20px;background:linear-gradient(120deg,#18534b33,var(--panel));border:1px solid #327669;border-radius:13px}.rehab-strip strong{display:block;font-size:16px;margin:4px 0}.rehab-strip .sub{display:block}.rehab-leg{border-left:3px solid var(--green)}
@media(prefers-reduced-motion:reduce){.btn,.theme-tile{transition:none}}
@media(max-width:650px){.city-hero{padding:22px;min-height:240px}.city-art{width:125%;opacity:.35}.city-caption{display:none}.card{padding:15px}.bigtitle{font-size:24px}.chart-range button{padding:5px 8px}}

.route-bars{display:flex;flex-direction:column;gap:13px;margin-top:17px}.route-bar-label{display:flex;justify-content:space-between;gap:10px;font-size:12px;margin-bottom:5px}.route-bar-track{height:6px;background:var(--panel2);border-radius:5px;overflow:hidden}.route-bar-fill{height:100%;background:linear-gradient(90deg,var(--blue),var(--green));border-radius:5px}
.loss-meter{height:7px;background:var(--panel2);border-radius:8px;overflow:hidden;margin:12px 0}.loss-meter>span{height:100%;display:block;background:linear-gradient(90deg,var(--blue),var(--green));border-radius:8px}
#losses label{display:grid;gap:6px;margin-bottom:12px;color:var(--muted)}
#losses input,#losses select{box-sizing:border-box;width:100%;padding:10px 12px;border:1px solid #34485d;border-radius:9px;background:#101c28;color:#e8f4ff;font:inherit}
#lossNew:not([open]){display:none}
#items input:not([type="checkbox"]),#items select,#iiClientKey,#iiLoginKey{width:100%;box-sizing:border-box;padding:9px;border:1px solid #34485d;border-radius:8px;background:#101c28;color:#e8f4ff} #items label{display:block;margin:8px 0} #items svg{max-height:180px} #items .actions{align-items:center} [data-company-panel][hidden]{display:none!important} #company .metric.s{font-size:clamp(20px,2.2vw,28px)} #company input,#company select{box-sizing:border-box;max-width:100%;padding:8px;border-radius:8px;border:1px solid #34485d;background:#101c28;color:#e8f4ff} #company label{display:grid;gap:6px;margin:8px 0} #company label.sub{display:inline-flex;align-items:center} #company input:not([type="checkbox"]),#company select{width:100%}
#smartMedicalPanel input[type="checkbox"]{width:16px;height:16px;display:inline-block;margin:0 7px 0 0;vertical-align:middle}
#smartMedicalPanel .adviser-fields{grid-template-columns:repeat(auto-fit,minmax(min(230px,100%),1fr));align-items:start}
#smartMedicalPanel label:has(input[type="checkbox"]){display:flex;align-items:center;gap:8px}
#medItemSettings .card{padding:12px;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}
#medItemSettings .card>label:first-child{grid-column:1/-1}
#leveling label{display:flex;flex-direction:column;gap:6px;min-width:0;margin:10px 0;color:var(--muted);font-size:12px}
#leveling input,#leveling select,#leveling textarea{width:100%;min-width:0;padding:9px 11px;border:1px solid var(--line2);border-radius:8px;background:var(--theme-field,var(--bg));color:var(--text);font:inherit}
#leveling textarea{resize:vertical;min-height:100px;line-height:1.5}
#leveling .twocol{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr));gap:12px}
#leveling .actions{margin-top:12px}#leveling .btn:disabled{opacity:.45;cursor:default}
#levelNote{font-weight:650;margin:10px 0}#levelRecovery{padding:10px 0;border-top:1px solid var(--line)}
</style>
</head>
<body>
<div class="shell">
<aside class="side" id="navSide" aria-label="Sections">
 <button class="navtoggle" id="navToggle" type="button" aria-expanded="false" aria-controls="navItems">☰ &nbsp; Sections <span id="navCurrent">Overview</span></button>
 <div id="navItems" class="navitems">
 <div class="brand">TORN<br>COMMANDER<small>CONTROL CENTER</small></div>
 <button class="nav active" data-page="dashboard"><i>◈</i>Overview</button>
 <button class="nav" data-page="items"><i>◎</i>Item Intel</button>
 <button class="nav" data-page="travel"><i>✈</i>Travel</button>
 <button class="nav" data-page="crimes"><i>◇</i>Crimes</button>
 <button class="nav" data-page="merits"><i>✦</i>Merits</button>
 <button class="nav" data-page="cart"><i>▤</i>Cart</button>
 <button class="nav" data-page="leveling"><i>⇧</i>Leveling</button>
 <button class="nav" data-page="losses"><i>⊞</i>Loss Desk</button>
 <button class="nav" data-page="trading"><i>⇄</i>Trading</button>
 <button class="nav" data-page="intel"><i>◉</i>Intel Tools</button>
 <button class="nav" data-page="war"><i>⚔</i>War Payouts</button>
 <button class="nav" data-page="stocks"><i>↗</i>Stocks</button>
 <button class="nav" data-page="faction"><i>⚐</i>Faction</button>
 <button class="nav" data-page="company"><i>▦</i>Company</button>
 <button class="nav" data-page="racing"><i>⚑</i>Racing</button>
 <button class="nav" data-page="casino"><i>♠</i>Casino</button>
 <button class="nav" data-page="scripts"><i>⚙</i>Scripts</button>
 <button class="nav" data-page="settings"><i>☷</i>Settings</button>
 <div class="sidefoot">Independent companion • v<span id="ver">0.6.69</span></div>
</div>
</aside>
<div class="content">
 <header class="bar">
  <div class="barleft"><span class="eyebrow">ACTIVE PROFILE</span><select id="profileSelect" class="selectmini" aria-label="Active Torn profile"></select><button class="btn small" id="profileAdd" title="Add a separate browser login" onclick="send('profileAdd')">+ Add</button><button class="btn small" title="Rename selected profile" onclick="send('profileRename')">Rename</button><button class="btn small" id="profileDelete" title="Remove this Commander profile (not the Torn game account)" onclick="send('profileDelete')">Remove</button><span id="apiBadge" class="pill muted">API not connected</span></div>
  <div class="bar-actions"><span id="buildBadge" class="pill muted">Build v0.6.69</span><button class="btn small" id="fastSyncButton" onclick="send('fastSync')">Fast Sync</button><button class="btn small primary" id="fullSyncButton" onclick="send('fullSync')">Full Sync</button></div>
 </header>
 <div class="notice" id="dashboardRenderWarning" hidden role="status"></div>
 <section class="page active" id="dashboard">
  <div class="city-hero"><svg class="city-art" viewBox="0 0 900 220" aria-hidden="true"><defs><linearGradient id="citySky" x2="0" y2="1"><stop stop-color="#38bdf8" stop-opacity=".13"/><stop offset="1" stop-color="#38bdf8" stop-opacity="0"/></linearGradient><pattern id="cityWindows" width="16" height="19" patternUnits="userSpaceOnUse"><rect x="3" y="4" width="3" height="5" fill="#79d8f2" opacity=".4"/></pattern></defs><ellipse cx="650" cy="110" rx="210" ry="140" fill="url(#citySky)"/><path d="M0 220V165h65v-35h45v90h20V105h70v115h25V65h60v155h20V130h50v90h25V25h65v195h30V100h70v120h25V55h45v165h20V135h70v85h30V90h60v130h35V155h75v65Z" fill="#102c42"/><path d="M0 220V165h65v-35h45v90h20V105h70v115h25V65h60v155h20V130h50v90h25V25h65v195h30V100h70v120h25V55h45v165h20V135h70v85h30V90h60v130h35V155h75v65Z" fill="url(#cityWindows)"/><path d="M411 25V0M572 55V22M0 219H900" stroke="#61c8ef" opacity=".5"/></svg><div class="city-copy"><div class="eyebrow"><span class="status-dot"></span> YOUR TORN, IN FOCUS</div><h1>Build your edge.<br><em>Make your next move count.</em></h1><p>Your money, your people, your progress — one place to see the bigger picture.</p><div class="actions"><button class="btn primary" onclick="page('travel')">Find my next trip <span>↗</span></button><button class="btn" onclick="page('company')">Company insights</button></div></div><span class="city-caption">TORN COMMANDER / OPERATIONS</span></div>

  <div class="top"><div><div class="eyebrow">Your command center</div><div class="bigtitle" id="player">Torn Commander</div><div class="sub" id="profileMeta">Account information appears after sync</div></div><span class="pill good">Local dashboard</span></div>
  <div class="grid">
   <div class="card span3"><div class="eyebrow">Nerve</div><div class="metric" id="nerve">—</div><div class="sub" id="nnb">NNB —</div></div>
   <div class="card span3"><div class="eyebrow">Energy</div><div class="metric" id="energy">—</div><div class="sub">Current / maximum</div></div>
   <div class="card span3"><div class="eyebrow">Cash</div><div class="metric s money" id="wallet">—</div><div class="sub">API wallet</div></div>
   <div class="card span3"><div class="eyebrow">Awards</div><div class="metric" id="merits">—</div><div class="sub" id="meritAvailable">—</div></div>
   <div class="card span12 hero" id="progressionCard">
    <div class="eyebrow">Your daily path</div><div class="section">Cash while growing</div>
    <div class="sub" id="progressionNote">Sync to build your plan.</div>
    <div class="statusline" id="progressionMetrics"></div>
    <div class="list" id="progressionSteps" style="margin-top:12px"></div>
    <details style="margin-top:12px"><summary>Adjust this profile's goals</summary>
     <div class="formgrid" style="margin:12px 0">
      <label>Training share of current energy (%)<input id="goalTraining" type="number" min="0" max="100" value="70"></label>
      <label>Cash reserve ($)<input id="goalReserve" type="number" min="0" max="1000000000000" value="100000"></label>
      <label>Daily recorded trading-margin target ($)<input id="goalTarget" type="number" min="1" max="1000000000000" value="500000"></label>
     </div><button class="btn small" onclick="saveProgressionGoals()">Save goals</button>
    </details>
   </div>
   <div class="card span8 hero"><div class="eyebrow" style="color:var(--blue)" id="phase">Crime Coach</div><div class="section" id="headline" style="margin-top:8px;font-size:19px">Run Full Sync to build your plan</div><div class="sub" id="why"></div><div class="notice info" style="margin:12px 0" id="guide">No guided action yet.</div><div class="notice info" style="margin:10px 0" id="graffitiStatus" aria-live="polite">Idle</div><div class="actions"><button class="btn primary" id="runGuideButton" onclick="send('runGuide')">Run Once</button><button class="btn" onclick="send('openGuide')">Open Only</button><button class="btn" onclick="send('graffitiDebug')">Copy Graffiti Debug</button><button class="btn" onclick="classic('Coach')">Detailed Coach</button></div></div>
   <div class="card span4"><div class="section">At a glance</div><div class="list"><div class="minirow"><b>Shopping</b><span class="sub" id="cartMini">—</span><button class="btn small" onclick="page('cart')">View</button></div><div class="minirow"><b>Loss Desk</b><span class="sub" id="lossMini">—</span><button class="btn small" onclick="page('losses')">View</button></div><div class="minirow"><b>Travel</b><span class="sub" id="travelMini">—</span><button class="btn small primary" onclick="page('travel')">View</button></div></div></div>
  </div>
 </section>
 <section class="page" id="travel">
  <div class="top"><div><div class="eyebrow">Independent foreign-stock intelligence</div><div class="bigtitle">Travel Planner</div><div class="sub">Find profitable runs, verify stock age, and plan when to leave</div></div><div class="actions"><button class="btn primary" id="refreshTravel" onclick="saveTravel()">Save + Refresh</button><button class="btn" onclick="send('openTravel')">Torn Travel Agency</button><button class="btn" onclick="classic('Travel')">Detailed View</button></div></div>
  <div id="travelWarning" class="notice info" style="margin-bottom:12px">Public data may be delayed. Confirm foreign stock in Torn before committing to a flight.</div>
  <div class="travelmeta" style="margin-bottom:11px"><div class="statbox"><div class="k">Feed status</div><div class="v" id="travelFeedStatus">Waiting for refresh</div></div><div class="statbox"><div class="k">Data source</div><div class="v" id="travelFeedSource">Not loaded</div></div><div class="statbox"><div class="k">What Commander is using</div><div class="v" id="travelFeedMode">No travel data loaded yet</div></div></div>
  <div class="card">
   <div class="section">Your flight setup</div>
   <div class="formgrid">
    <div class="field"><label for="tCapacity">Carry capacity</label><input type="number" min="1" max="1000" id="tCapacity" value="29"></div>
    <div class="field"><label for="tFlight">Flight method</label><select id="tFlight"><option>Standard</option><option>Airstrip</option><option>Private</option><option>Business</option></select></div>
    <div class="field"><label for="tBudget">Cash budget $ (0 = no limit)</label><input type="number" min="0" max="1000000000" step="10000" id="tBudget" value="0"></div>
    <div class="field"><label for="tHorizon">Forecast window</label><select id="tHorizon"><option value="12">12 hours</option><option value="24">24 hours</option><option value="48">48 hours</option><option value="72">72 hours</option></select></div>
    <div class="field"><label for="tConfidence">Min prediction confidence %</label><input type="number" min="0" max="100" step="5" id="tConfidence" value="35"></div>
   </div>
   <div class="options"><label class="toggle"><input id="tTax" type="checkbox"> Item Market fee (5%)</label><label class="toggle"><input id="tBook" type="checkbox"> Mailing book (-25%)</label><label class="toggle"><input id="tLow" type="checkbox"> Include low-confidence estimates</label><label class="toggle"><input id="tStockOnly" type="checkbox"> In-stock only</label></div>
   <div id="travelSource" class="statusline">Awaiting feed</div>
  </div>
  <div class="card hero" id="landingAdvisor" style="margin-top:11px">
   <div class="top"><div><div class="eyebrow">After landing · actual Torn shelves</div><div class="section" style="margin:0">Buy now, wait for restock, or return?</div><div class="sub">Reads only the currently visible overseas shop. Never purchases anything automatically.</div></div><div class="actions"><button class="btn primary" onclick="send('travelArrivalScan')">I've Landed — Scan Shop</button><button class="btn" onclick="send('travelArrivalScan')">When Will It Restock?</button><button class="btn" onclick="send('travelArrivalReload')">Refresh Torn Shop</button></div></div>
   <div class="travelmeta" style="margin:8px 0 12px"><div class="statbox"><div class="k">Decision</div><div class="v" id="arrivalDecision">Awaiting shop scan</div></div><div class="statbox"><div class="k">Cash at shop</div><div class="v" id="arrivalCash">—</div></div><div class="statbox"><div class="k">Carrying slots left</div><div class="v" id="arrivalSlots">—</div></div></div>
   <div class="notice info" id="arrivalAdvice">Open the Torn foreign shop and press Scan Shop to compare the actual stock on arrival.</div>
   <div class="statusline" id="arrivalNextCheck"></div>
   <div class="notice info" id="arrivalChanges" style="margin-top:9px">Restocks detected during this visit will appear here.</div>
   <div class="statusline" id="arrivalBasket"></div>
   <div class="list" id="arrivalItems" style="margin-top:9px"></div>
   <div class="footerline">Restock times are estimates, not guaranteed shop timers. If the source lacks enough observations, Commander shows only a suggested recheck time. Prices are indicative, not guaranteed sale values.</div>
  </div>
  <div class="travel-state" id="travelStatusBox"><div><div class="eyebrow">Live travel position</div><div class="section" id="travelStatusLabel">Awaiting account sync</div><div class="sub" id="travelStatusDetails">Commander checks your own Torn travel status and re-plans when it changes.</div></div><button class="btn small" onclick="send('travelStatusCheck')">Recheck Status</button></div>
  <div class="card hero" style="margin-top:11px" id="itineraryCard">
   <div class="rehab-strip"><div><span class="eyebrow">Health before profit</span><strong>Switzerland, then the money runs</strong><span class="sub" id="rehabStatus">Add a rehab visit before your trading route.</span></div><div class="actions"><label class="field"><span class="sub">Minutes at rehab</span><input id="rehabStay" type="number" min="1" max="120" value="10" style="width:90px"></label><button class="btn primary" id="rehabFirstButton" onclick="send('travelRehab',{profileId:S.profileId,enabled:true,stayMinutes:Number($('rehabStay').value)})">Switzerland rehab first</button><button class="btn" id="rehabDoneButton" onclick="send('travelRehab',{profileId:S.profileId,enabled:false})">Rehab done / remove stop</button></div></div>
   <div class="top"><div><div class="eyebrow">Rolling schedule · starts and returns in Torn</div><div class="bigtitle" style="font-size:19px">Best modeled 24-hour itinerary</div><div class="sub" id="itineraryPeriod">Choose a departure time to build your route</div></div><div class="actions"><button class="btn primary" onclick="saveTravel(false)">Rebuild Plan</button><button class="btn" onclick="send('travelCopyPlan')">Copy 24h Plan</button></div></div>
   <div class="formgrid" style="margin-top:12px">
    <label class="toggle" style="align-self:end;padding-bottom:10px"><input type="checkbox" id="tPlanNow" checked> Plan from when I'm free in Torn</label>
    <label class="toggle" style="align-self:end;padding-bottom:10px"><input type="checkbox" id="tPauseUntilHome"> Pause recommendations while I do my own trip</label>
    <div class="field" id="tCustomStartWrap"><label for="tPlanStart">Custom departure (your PC time)</label><input type="datetime-local" id="tPlanStart"></div>
    <div class="field"><label for="tTransfer">Shop stay / turnaround time (minutes)</label><input id="tTransfer" type="number" min="0" max="120" value="10"></div>
   </div>
   <div class="divider"></div>
   <div class="formgrid" style="align-items:end">
    <label class="toggle" style="padding-bottom:10px"><input id="tAutoMonitor" type="checkbox" checked> Auto-monitor live travel feeds</label>
    <div class="field"><label for="tMonitorEvery">Check every</label><select id="tMonitorEvery"><option value="10">10 minutes</option><option value="15">15 minutes</option><option value="30">30 minutes</option><option value="60">60 minutes</option></select></div>
    <div class="statusline" id="travelNextCheck">Checks while Commander is running</div>
   </div>
   <div class="grid" style="margin-top:13px"><div class="card span4"><div class="eyebrow">Scenario upside (unverified)</div><div class="metric s money" id="itinProfit">—</div><div class="sub">Assumes every modeled purchase succeeds</div></div><div class="card span4"><div class="eyebrow">Planned round trips</div><div class="metric s" id="itinTrips">—</div><div class="sub">All completed inside 24 hours</div></div><div class="card span4"><div class="eyebrow">Risk-weighted planning score</div><div class="metric s money" id="itinScore">—</div><div class="sub">Ranking heuristic, not expected cash</div></div></div>
   <div class="notice info" id="itinDisclaimer" style="margin:12px 0">Later trips rely on uncertain stock. Commander does not book flights or purchase items.</div>
   <label class="toggle" style="margin-bottom:9px"><input type="checkbox" id="tShowRouteMap" onchange="saveTravel(false)"> Show route-line map (schematic)</label>
   <div id="itineraryMap" class="itin-line-map" hidden><div class="eyebrow">Route sequence · schematic, not geographical scale</div><div id="itineraryMapTrack" class="itin-track"></div><div class="statusline">Tap a destination to see its trip details. All times are local to this computer.</div></div>
   <div class="list" id="itineraryRows"><div class="empty">Load travel data to generate a 24-hour itinerary.</div></div>
  <div class="card" id="travelPreviewPanel" style="margin-top:12px">
   <div class="top"><div><div class="eyebrow">After-return strategy · no departure time assumed</div><div class="section" style="margin:0">Candidate routes and feed diagnosis</div></div><span class="pill muted" id="travelPreviewCount">Waiting for travel feed</span></div>
   <div class="notice info" id="travelPreviewDiagnostic">The planner will explain its available data here, even while you're away from Torn.</div>
   <div class="list" id="travelPreviewRows" style="margin-top:11px"><div class="empty">No route comparisons loaded yet.</div></div>
   <div class="statusline">This panel is a conditional preview, not a 24-hour departure schedule. Verify remaining flight time, your cash, and shop inventory before traveling. Modeled resale values can change.</div>
  </div>

  </div>
  <div class="grid" style="margin-top:11px"><div class="card span4"><div class="eyebrow">Best estimated net / trip</div><div class="metric s money" id="bestTrip">—</div><div class="sub" id="bestTripItem">—</div></div><div class="card span4"><div class="eyebrow">Best estimated net / hour</div><div class="metric s money" id="bestHour">—</div><div class="sub">Round-trip flight time, excluding time on the ground</div></div><div class="card span4"><div class="eyebrow">Stock-aware results</div><div class="metric s" id="travelCount">—</div><div class="sub" id="travelCountDetail">Current and forecast routes</div></div></div>
  <div class="travelbar">
   <div class="field search"><label for="tSearch">Find item</label><input id="tSearch" type="text" placeholder="Search flowers, plushies, Xanax..."></div>
   <div class="field"><label for="tCountry">Destination</label><select id="tCountry"><option value="">All countries</option></select></div>
   <div class="field"><label for="tCategory">Category</label><select id="tCategory"><option value="">All categories</option></select></div>
   <div class="field"><label for="tStatus">Availability</label><select id="tStatus"><option value="">All opportunities</option><option value="In stock">Reported in stock</option><option value="Predicted">Forecast restocks</option><option value="Stale">Stale / unverified</option></select></div>
  </div>
  <div class="card" style="margin:14px 0"><div class="eyebrow">COMPARE YOUR OPTIONS</div><div class="section">Top routes by modeled profit per hour</div><div class="sub">Current route estimates, not a history chart. Stock and selling prices can change.</div><div id="travelOpportunityChart" class="route-bars"></div><details class="card" style="margin:12px 0"><summary>Wait for restock or buy an alternative?</summary><div id="travelWaitAdvice" class="rows">Sync your location and collect item history to compare.</div></details></div>
  <div class="card"><div class="top"><div class="section" style="margin:0">Ranked travel opportunities</div><span class="pill muted" id="resultCount">0 results</span></div><div class="list" id="travelRows"><div class="empty">Refresh to load travel opportunities.</div></div><div class="footerline">Profit estimates assume selling at the quoted market value. Flight times are approximate; standard airfare is included, but the cost of business tickets, purchasing time, rehab fees, market slippage and travel risk are not.</div></div>
 </section>
 <section class="page" id="crimes"><div class="top"><div><div class="eyebrow">Crimes 2.0 / Natural Nerve Bar</div><div class="bigtitle">Crime Coach</div></div><div class="actions"><button class="btn primary" id="crimeRunGuideButton" onclick="send('runGuide')">Guided Action Once</button><button class="btn" onclick="send('openCrimes')">Open Crimes</button><button class="btn" onclick="classic('Crimes')">Crime Statistics</button></div></div><div class="grid"><div class="card span5"><div class="section">Next step</div><div class="notice info" id="crimeGuide">Sync for a recommendation</div><div class="statusline" id="crimeActionStatus" aria-live="polite">Idle</div><div class="actions" style="margin-top:10px"><button class="btn small" onclick="send('graffitiDebug')">Copy Graffiti Debug</button></div></div><div class="card span7"><div class="section">Highest crime skills</div><div class="list" id="crimeRows"></div></div></div></section>
 <section class="page" id="merits"><div class="top"><div><div class="eyebrow">Verified progress only</div><div class="bigtitle">Merit Hunter</div></div><div class="actions"><button class="btn primary" onclick="classic('Merits')">Open Full Merit Hunter</button></div></div><div class="card"><div class="list" id="meritRows"></div></div></section>
 <section class="page" id="cart"><div class="top"><div><div class="eyebrow">Item Market / bulk buying</div><div class="bigtitle">Shopping Cart</div></div><div class="actions"><button class="btn primary" onclick="send('cartScan')">Scan Cart</button><button class="btn" onclick="send('cartCopy')">Copy For Faction Chat</button><button class="btn" onclick="classic('Cart')">Full Cart</button></div></div><div class="grid"><div class="card span4"><div class="eyebrow">Items remaining</div><div class="metric" id="cartItems">—</div></div><div class="card span4"><div class="eyebrow">Units remaining</div><div class="metric" id="cartRemain">—</div></div><div class="card span4"><div class="eyebrow">Market estimate</div><div class="metric s money" id="cartEstimate">—</div><div class="sub" id="cartLimits">—</div></div></div><div class="notice info" style="margin-top:12px">Review each prepared purchase before confirming. Existing limits and the explicit NUKE MY PILE switch remain in Classic Tools.</div></section>
 <section class="page" id="items"><div class="top"><div><div class="eyebrow">Observed stock · local history · independent estimates</div><div class="bigtitle">Item Intelligence</div></div><div class="actions"><button class="btn primary" onclick="send('itemRefresh')">Refresh stocks</button><button class="btn" onclick="send('itemImport')">Import CSV</button><button class="btn" onclick="send('itemExport')">Export stock history</button><button class="btn" onclick="send('itemMarketExport')">Export market asks</button></div></div>
 <div class="notice" id="iiStatus">No observations yet. Refresh stocks to begin.</div>
 <div class="card" style="margin-top:12px"><div class="section">Collection while Commander is open</div><div class="actions"><label><input id="iiMonitor" type="checkbox"> Monitor watchlist</label><label>Minutes<input id="iiMinutes" type="number" min="1" max="60" value="5"></label><label>History days<input id="iiRetention" type="number" min="7" max="365" value="90"></label><label><input id="iiIntel" type="checkbox"> Use approved Torn Intel stock API</label><label><input id="iiMarketQuotes" type="checkbox"> Sample official market asks (up to 5 items/poll)</label><button class="btn" onclick="saveItemCollection()">Save collection</button></div><div class="sub">YATA public observations remain available as an independent source. Torn Intel requires an approved application key in Settings. Missing items and missing timestamps are never treated as zero stock.</div></div>
 <details class="card" style="margin-top:12px"><summary>Add or edit a watched item</summary><label>Find an item<input id="iiSearch" placeholder="Name, ID or destination" oninput="renderItemCatalog(S.itemIntelligence)"></label><label>Observed catalog<select id="iiCatalog" onchange="loadItemWatch()"></select></label><div class="twocol"><label><input id="iiBack" type="checkbox" checked> Alert when back in stock</label><label><input id="iiForecast" type="checkbox"> Alert near a window or on material change</label><label>Stock below<input id="iiBelow" type="number" min="0" placeholder="Off"></label><label>Stock above<input id="iiAbove" type="number" min="0" placeholder="Off"></label><label>Price below ($)<input id="iiPriceBelow" type="number" min="0" placeholder="Off / provider value required"></label><label>Price above ($)<input id="iiPriceAbove" type="number" min="0" placeholder="Off"></label></div><button class="btn primary" onclick="saveItemWatch(false)">Save watch</button><button class="btn" onclick="saveItemWatch(true)">Remove watch</button><div class="sub">Price thresholds use the lowest returned official asking price when market sampling is enabled; otherwise they use a provider market value if available. Asking prices are not completed sales. Neumune Tablet #361 is sold in Switzerland. Foreign-shop availability differs from item-market listings, bazaars, city shops and crime rewards. This catalog covers reported foreign shops; it does not claim those other inventories.</div></details>
 <div id="iiCards" style="margin-top:12px"></div><details class="card" style="margin-top:12px"><summary>Sources, freshness and imports</summary><div id="iiHealth"></div><p class="sub">CSV columns: source,location,itemId,name,observedUtc,quantity,cost; optional marketValue. Use ISO timestamps with Z or an explicit offset. Imports are kept under a separate CSV source. Windows notifications require Commander to remain running and Windows notification settings to allow them.</p></details></section>
 <section class="page" id="leveling">
 <div class="top"><div><div class="eyebrow">Your target list · your account</div><div class="bigtitle">Leveling</div></div><span class="pill">Target queue + live combat</span></div>
 <div class="card"><p>Work down a list of leveling targets automatically, or use the manual checks below. Automatic mode attacks with your selected equipped weapon, chooses Leave after a win and verifies the official result before advancing. Equip a fighting weapon instead of the Loss Desk Pillow, and choose <b>Leave</b> after a win.</p><div class="actions"><label>Baldr list<select id="levelBaldrGroup"><option value="">All lists</option><option selected>Baldr's List 1</option><option>Baldr's List 2</option><option>Baldr's List 3</option><option>Baldr's Extra List 1</option><option>Baldr's Extra List 2</option><option>Baldr's Extra List 3</option><option>Baldr's DOMINO List</option></select></label><button class="btn primary" onclick="loadBaldrList()">Load Baldr’s list</button><button class="btn" onclick="levelAction('levelSources',{source:'baldr'})">Open guide</button><button class="btn" onclick="levelAction('levelSources',{source:'ff'})">Open FFScouter targets</button></div><p class="sub">Paste player IDs (one per line), Torn profile links, ID-first CSV, or FFScouter targets JSON. FFScouter requests use only the separate key you explicitly save below. Source listings and battle-stat estimates do not guarantee a win.</p>
 <details class="card" open><summary>Find targets this account can handle</summary><p class="sub">Default: target estimates at most 50% of your own total stats. With 300 stats that means a 150-stat cap. This is a conservative filter, not a win guarantee: weapon, armour, life and stat balance still matter. Unknown or stale strength is excluded from fights. Baldr listed totals remain historical reference only.</p><div class="actions"><label>Registered FFScouter key for this account<input id="levelScoutKey" type="password" autocomplete="off"></label><button class="btn" onclick="saveScouterKey()">Save key locally</button><button class="btn" onclick="levelAction('levelScoutKey',{remove:true})">Forget key</button><span id="levelScoutConnection" class="sub"></span></div><div class="actions"><label>Target strength cap (% of yours)<input id="levelStatRatio" type="number" min="5" max="100" value="50"></label><label>Maximum Fair Fight<input id="levelMaxFF" type="number" min="1" max="3" step="0.1" value="2"></label><label>Maximum estimate age (days)<input id="levelEstimateDays" type="number" min="1" max="365" value="30"></label><label><input id="levelInactive" type="checkbox" checked>Inactive for at least 14 days</label><button class="btn" onclick="saveScoutFilters()">Apply strength filters</button></div><div class="actions"><button class="btn primary" onclick="levelAction('levelScoutSearch')">Find FFScouter targets</button><button class="btn" onclick="levelAction('levelScoutScan')">Scan strength of entire loaded list</button></div><p id="levelScoutSummary" class="sub"></p><p class="sub">Apply filters before searching. Scans retrieve the latest known estimates, not exact live stats. Current Torn activity and availability are rechecked before each fight. FFScouter Fair Fight is relative to the supplied key owner; use a key for this account. The independent strength cap always uses this selected account and its existing Torn key. The FFScouter key is sent only to FFScouter and stays in Windows Credential Manager.</p></details>
 <div id="levelBaldrSource" class="sub"></div><label>Target list (optional manual import)<textarea id="levelTargets" rows="5" maxlength="200000" placeholder="1234567&#10;https://www.torn.com/profiles.php?XID=2345678"></textarea></label>
 <div class="twocol"><label>Keep energy<input id="levelReserve" type="number" min="0" max="10000" value="0"></label><label>Session attack limit (0 = no limit)<input id="levelLimit" type="number" min="0" max="10000" value="0"></label><label>Heal before fighting<select id="levelLife"><option value="25">At least 25% life</option><option value="50">At least 50% life</option><option value="75">At least 75% life</option><option value="100" selected>Full life (100%)</option></select></label></div>
 <div class="twocol"><label>Fighting weapon<select id="levelWeapon"><option value="weapon_primary">Equipped primary</option><option value="weapon_secondary">Equipped secondary</option><option value="weapon_melee">Equipped melee</option></select></label><label>Attack interval (ms)<input id="levelDelay" type="number" min="500" max="5000" value="1000"></label></div><label style="flex-direction:row;align-items:center"><input id="levelAutoHeal" type="checkbox" style="width:auto"> Allow automatic personal medical use for leveling</label><p class="sub">Save first, then Start. Automatic healing uses the saved item permissions and reserves for this account; drugs remain excluded. Without healing permission it pauses with a recommendation. An unavailable/empty weapon pauses instead of switching equipment.</p>
 <details><summary>Personal medical quantities for manual recovery estimates</summary><div class="twocol"><label>Small FAK<input id="levelQty68" type="number" min="0" value="0"></label><label>FAK<input id="levelQty67" type="number" min="0" value="0"></label><label>Morphine<input id="levelQty66" type="number" min="0" value="0"></label></div><p class="sub">Enter current quantities or open Medical and read its visible counts below. The planner respects this account’s Smart Medical item permissions, bonuses, quantity reserves and cooldown reserve. Drugs and blood bags are excluded from this guided planner. Refresh/recalculate after each item use.</p></details>
 <button class="btn primary" onclick="saveLeveling()">Save list / settings</button></div>
 <div class="card" style="margin-top:12px"><div id="levelSummary" class="sub">Import a target list to begin.</div><div id="levelWorker" class="sub">Worker not started</div><div class="actions"><button class="btn primary" onclick="levelAction('levelStart')">Start automatic leveling</button><button class="btn" onclick="levelAction('levelPause')">Pause leveling</button><button class="btn" onclick="levelAction('levelResume')">Resume leveling</button><button class="btn" onclick="levelAction('levelStop')">Stop leveling</button><button class="btn" onclick="send('lossShowWorkers')">Show fight window</button></div><div id="levelNote" role="status"></div><div id="levelRecovery" class="sub"></div><div class="actions"><button class="btn primary" onclick="levelAction('levelCheck')">Check next target</button><button id="levelOpen" class="btn" onclick="levelAction('levelOpen')">Open checked target</button><button class="btn" onclick="levelAction('levelResult')">Check fight result</button><button class="btn" onclick="levelAction('levelMedical')">Open Items / Medical</button><button class="btn" onclick="levelAction('levelReadInventory')">Read visible medical counts</button><button class="btn" onclick="levelAction('levelSkip')">Skip current target</button><button class="btn" onclick="levelAction('levelRound')">New pass through list</button></div><p class="sub">Live API data can be cached. Attack results count only after the official log identifies your account, this target and a new completed attack. Energy estimate assumes the usual 25 energy per attack; no refills or drugs are used.</p></div><label>Show targets<select id="levelScoutView" onchange="renderLeveling(levelingLastData)"><option value="all">All targets with exclusion reasons</option><option value="within">Within strength limit</option></select></label><div id="levelRows" style="margin-top:12px"></div>
 </section>
 <section class="page" id="losses">
 <div class="top"><div><div class="eyebrow">Orders and workers in one place</div><div class="bigtitle">Loss Desk</div></div><button class="btn primary" onclick="$('lossNew').open=!$('lossNew').open">+ New order</button></div>
 <details class="card" id="lossNew"><summary>Create an order</summary><div class="twocol" style="margin-top:12px">
 <label>Buyer / contract<input id="lossBuyer" maxlength="120" placeholder="Buyer name"></label><label>Target player ID<input id="lossTarget" inputmode="numeric" placeholder="Numeric Torn ID"></label>
 <label>Total losses (shared by A and B)<input id="lossCount" type="number" min="1" max="1000000" value="20"></label><label>Pay per loss ($)<input id="lossPrice" type="number" min="0" max="1000000000000" value="350000"></label>
 <label>Account A<select id="lossAccountA"></select></label><label>Account B (optional)<select id="lossAccountB"></select></label></div>
 <div class="sub">The count is combined, not per account. Use Start A for a solo order, or assign two different accounts and Start Both.</div><button class="btn primary" onclick="createLossOrder()">Create order</button></details>
 <div class="card" style="margin-top:12px"><label>Selected order<select id="lossOrderSelect" onchange="send('lossSelect',{orderId:this.value})"></select></label><div id="lossCard" style="margin-top:14px">No active order</div></div>
 <div class="actions" style="margin:12px 0"><button class="btn good" onclick="send('lossStartA')">Start A</button><button class="btn good" onclick="send('lossStartB')">Start B</button><button class="btn primary" onclick="send('lossStartBoth')">Start Both</button><button class="btn warn" onclick="send('lossPauseBoth')">Pause</button><button class="btn" onclick="send('lossStopBoth')">Stop</button></div>
 <div class="twocol"><div class="card"><div class="eyebrow">Account A</div><div class="sub" id="workerA">Not initialized</div><button class="btn small" onclick="send('lossResumeA')">Resume A</button></div><div class="card"><div class="eyebrow">Account B</div><div class="sub" id="workerB">Not initialized</div><button class="btn small" onclick="send('lossResumeB')">Resume B</button></div></div>
 <div class="actions" style="margin-top:12px"><button class="btn" onclick="send('lossShowWorkers')">Show fight windows</button><button class="btn" onclick="send('lossHideWorkers')">Hide</button><button class="btn" onclick="send('lossCloseWorkers')">Stop &amp; close</button></div><div class="sub">Drag the divider above the fight window to resize it. Use Expand / Restore for more room.</div>
 <details class="card" style="margin-top:12px" id="smartMedicalPanel"><summary>Smart Medical Management</summary><div class="notice" style="margin-top:12px"><b>Drug use is permanently disabled.</b> No Xanax, Opium, drug cooldown or faction-armory supplies can enter this module.</div>
 <label>Medical settings for account<select id="medProfile" onchange="loadMedicalSettings()"></select></label><div class="formgrid adviser-fields"><label><input id="medEnabled" type="checkbox"> Enable optimizer</label><label>Consumption mode<select id="medMode"><option value="recommend">Recommend only — never consume</option><option value="automatic">Automatic — I permit healing</option></select></label><label><input id="medDetect" type="checkbox" onchange="$('medBonus').disabled=this.checked;$('medMaximum').disabled=this.checked"> Detect bonuses from account perks</label><label>Manual total effectiveness bonus (%)<input id="medBonus" type="number" min="0" max="50" step="0.1" value="0"></label><label>Manual cooldown maximum (minutes)<input id="medMaximum" type="number" min="360" max="540" value="360"></label><label>Keep cooldown capacity (minutes)<input id="medReserve" type="number" min="0" max="539" value="0"></label><label><input id="medResume" type="checkbox"> Resume automatically when cooldown permits</label><label><input id="medBlood" type="checkbox"> Permit O-negative bags only (verified IV eligibility required)</label></div>
 <div class="sub">Minimum medical cooldown is the fixed objective. Manual values are user supplied, not API verified; they apply when detection is off. Stop the selected profile's worker before changing settings. Personal inventory is scanned before the first attack and each heal. Unknown or stale state pauses safely.</div><div id="medItemSettings" class="twocol" style="margin-top:12px"></div><button class="btn primary" onclick="saveMedicalSettings()">Save medical permissions for this account</button><div class="sub">Blood type is not exposed by the verified profile schema. Other filled blood types, empty bags and irradiated bags are excluded. O-negative is universally compatible but still requires the blood-use education perk.</div></details>
 <div id="medicalSessions" class="twocol" style="margin-top:12px"></div>
 <details class="card" style="margin-top:12px"><summary>Timing and order tools</summary><label>Pillow delay (ms)<input id="lossAttackDelay" type="number" min="500" max="5000" step="100" value="1900"></label><button class="btn" onclick="saveLossTiming()">Apply timing</button><div class="sub">500–5000 ms between attempts; Torn loading can make turns slower.</div><div class="actions" style="margin-top:12px"><button class="btn" onclick="send('lossOrderPause',{orderId:$('lossOrderSelect').value})">Pause / resume order</button><button class="btn" onclick="send('lossDelete',{orderId:$('lossOrderSelect').value})">Delete selected order…</button><button class="btn" onclick="classic('Losses')">Advanced manual tools</button></div><div class="notice" style="margin-top:12px">Workers require verified Torn page states. Review Torn's scripting rules before using automated actions.</div></details>
 </section>
 <section class="page" id="trading"><div class="top"><div><div class="eyebrow">Trade ledger & market tools</div><div class="bigtitle">Trading</div></div><div class="actions"><button class="btn primary" onclick="classic('Money')">Open Trading Ledger</button><button class="btn" onclick="send('tradeExport')">Export Completed Trades</button><button class="btn" onclick="classic('Cart')">Open Market Cart</button></div></div><div class="grid"><div class="card span6"><div class="eyebrow">Recorded trades</div><div class="metric" id="tradeCount">—</div></div><div class="card span6"><div class="eyebrow">Known profit after selling fees</div><div class="metric s money" id="tradeProfit">—</div><div class="sub" id="tradeFeeCoverage"></div></div></div><div class="card" style="margin-top:12px"><div class="section">Live trading scanner &amp; adviser</div><div class="sub">Scan up to eight exact item names or IDs. Candidates depend on visible resale gaps; listings do not prove demand or future sale prices.</div>
<div class="formgrid adviser-fields"><label>Items / IDs (up to 8)<input id="tradeItems" placeholder="Beer, First Aid Kit" aria-label="Watchlist item names or IDs"></label><label>Total cash, including reserve ($)<input id="tradeBudget" type="number" min="0" placeholder="Total budget" aria-label="Total capital budget"></label><label>Keep cash reserve ($)<input id="tradeReserve" type="number" min="0" placeholder="Cash reserve" aria-label="Cash reserve"></label><label>Selling fee (%)<input id="tradeFee" type="number" min="0" max="25" step="0.1" value="5" aria-label="Selling fee percent"></label><label>Minimum net return (%)<input id="tradeRoi" type="number" min="0" max="1000" step="0.1" value="5" aria-label="Minimum return percent"></label><label>Your cost each ($; one item)<input id="tradeCostBasis" type="number" min="0" placeholder="Cost each (one item only)" aria-label="Cost per owned item"></label><label>Price-drop stress test (%)<input id="tradeStress" type="number" min="0" max="50" step="0.1" value="3"></label><label>Max capital in one item (%)<input id="tradeExposure" type="number" min="1" max="100" value="50"></label><label>Minimum cash profit ($)<input id="tradeMinimumProfit" type="number" min="0" value="1000"></label><button class="btn primary" id="tradeScanButton" onclick="scanTrading()">Scan &amp; advise</button></div>
<div class="sub">Fee %: ordinary Item Market 5%; Bazaar 0%. Set your actual selling fee. Return % is the minimum net return on purchase cost. Cost each is optional for sell advice on one item.</div>
<div id="tradeScanNote" class="notice info">Ready to scan.</div><div id="tradeCapitalNote" class="sub"></div><div id="tradeAdviceRows"></div>
<div class="sub">Quote expiry: 10 minutes. All candidate costs share one capital budget. Candidates must also survive your entered price-drop scenario and per-item cash limit; these checks do not establish buyer demand. No purchases or sales are performed. <a href="https://wiki.torn.com/wiki/Item_Market" target="_blank">Market fees</a> / <a href="https://wiki.torn.com/wiki/Bazaar" target="_blank">Bazaar fees</a></div></div><div class="card" style="margin-top:14px"><div class="eyebrow">COUNT CASH YOU ACTUALLY KEPT</div><div class="section">Record a completed sale</div><div class="sub">Enter a sale you have already completed. Actual selling fees are a total dollar amount, not a percentage. No purchase or listing is made.</div><div class="formgrid adviser-fields" id="completedTradeForm"><label>Item<input id="completedItem" maxlength="120"></label><label>Quantity sold<input id="completedQuantity" type="number" min="1" value="1"></label><label>Buy price each ($)<input id="completedBuy" type="number" min="0" step="0.01"></label><label>Actual sell price each ($)<input id="completedSell" type="number" min="0" step="0.01"></label><label>Total actual selling fees ($)<input id="completedFees" type="number" min="0" step="0.01" placeholder="0 if genuinely fee-free"></label></div><button class="btn primary" onclick="recordCompletedTrade()">Save Completed Sale</button><div class="statusline">These records stay on this PC. Older entries with unknown fees are excluded from the known-net total.</div></div></section>
 <section class="page" id="racing"><div class="top"><div><div class="eyebrow">Racing journal and tools</div><div class="bigtitle">Racing</div></div><div class="actions"><button class="btn primary" onclick="classic('Racing')">Racing Tools</button><button class="btn" onclick="send('openRacing')">Open Torn Racing</button></div></div><div class="card"><div class="section">Racing operations</div><div class="sub">Use the classic racing panel to manage your data, tips and current helper scripts. The Torn browser uses the correct page.php route.</div></div></section>
 <section class="page" id="casino"><div class="top"><div><div class="eyebrow">Session-based casino tools</div><div class="bigtitle">Casino</div></div><div class="actions"><button class="btn primary" onclick="classic('Casino')">Open Casino Tools</button></div></div><div class="grid"><div class="card span6"><div class="eyebrow">Tracked sessions</div><div class="metric" id="casinoSessions">—</div></div><div class="card span6"><div class="eyebrow">Recorded net P/L</div><div class="metric s money" id="casinoNet">—</div></div></div><div class="card" style="margin-top:12px"><div class="section">Hand adviser</div><div class="sub">Enter the current hand; press its button for fresh advice. No bets are placed.</div>
<div class="section">Blackjack</div><div class="formgrid adviser-fields"><label>Your blackjack cards<input id="bjCards" placeholder="Your ranks: A 7"></label><label>Dealer up-card<input id="bjDealer" placeholder="Dealer rank: 6"></label><label class="toggle"><input id="bjDouble" type="checkbox" checked>Double available</label><label class="toggle"><input id="bjSplit" type="checkbox" checked>Split available</label><label class="toggle"><input id="bjSurrender" type="checkbox">Surrender available</label><button class="btn" onclick="casinoAdvice('blackjack')">Get move</button><button class="btn" onclick="send('scriptOpen',{name:'Torn_Blackjack_Assist.user.js'})">Open live Blackjack helper</button></div>
<div class="section">Poker</div><div class="sub">Cards include suits: c, d, h, s. Enter the pot before your call and opponents still in the hand.</div><div class="formgrid adviser-fields"><label>Your two poker cards<input id="pkHole" placeholder="Hole: As Kh"></label><label>Community cards<input id="pkBoard" placeholder="Board: Qh Jh 10h"></label><label>Opponents in the hand<input id="pkOpponents" type="number" min="1" max="8" value="1" aria-label="Opponents"></label><label>Pot before your call ($)<input id="pkPot" type="number" min="0" placeholder="Pot before your call"></label><label>Cost to call ($)<input id="pkCall" type="number" min="0" placeholder="Cost to call"></label><button class="btn" onclick="casinoAdvice('poker')">Compare call / fold</button></div>
<div class="section">High-Low</div><div class="formgrid adviser-fields"><label>Current High-Low rank<input id="hlCard" placeholder="Current rank"></label><label>Displayed growth (%)<input id="hlGrowth" type="number" value="15" aria-label="Displayed growth percent"></label><label>Available cash-out<select id="hlCash"><option value="50">50% cash-out available</option><option value="100">100% cash-out available</option></select></label><button class="btn" onclick="casinoAdvice('highlow')">Compare choices</button></div>
<div class="section">Other bets - expected value</div><div class="sub">For roulette, dice or other known payouts, enter the actual chance and net win multiplier.</div><div class="formgrid adviser-fields"><label>Win chance (%)<input id="evChance" type="number" placeholder="Win chance %"></label><label>Net win multiplier<input id="evPayout" type="number" placeholder="Net win multiplier"></label><label>Stake ($)<input id="evStake" type="number" placeholder="Stake"></label><button class="btn" onclick="casinoAdvice('expected')">Evaluate bet</button></div>
<div id="casinoAdvice" class="notice info" role="status">Choose a hand above.</div><div class="sub">Rules: <a href="https://wiki.torn.com/wiki/Black_Jack" target="_blank">Torn Blackjack</a> / <a href="https://wiki.torn.com/wiki/Poker" target="_blank">Poker</a> / <a href="https://wiki.torn.com/wiki/High-Low" target="_blank">High-Low</a></div></div><div class="notice info" style="margin-top:12px">Assistance and session tracking only. No unattended gambling runs are started from this dashboard.</div></section>
 <section class="page" id="scripts"><div class="top"><div><div class="eyebrow">Native userscript engine</div><div class="bigtitle">Scripts & Helpers</div></div><div class="actions"><button class="btn primary" onclick="classic('Tools')">Manage Userscripts</button></div></div><div class="card"><div class="section">Installed assistants</div><div class="sub">Open a helper below to enable it and load its Torn page in Commander. Script menu commands appear in the Commander helpers menu on the game page. Manage Userscripts installs and updates tools.</div><div id="scriptToolsList" class="statusline">Loading helpers…</div></div></section>
 <section class="page" id="faction">
  <div class="top"><div><div class="eyebrow">Position-aware faction helper</div><div class="bigtitle">Faction Command</div><div class="sub">Membership, growth, organized crimes, wars and your local mission checklist</div></div><div class="actions"><button class="btn primary" id="faScan" onclick="send('factionScan')">Scan Faction</button><button class="btn" id="faCopy" onclick="send('factionCopy')">Copy Faction Report</button><button class="btn" onclick="send('openFaction')">Open Torn Faction</button></div></div>
  <div class="notice info" id="faStatus">Scan your faction to begin.</div>
  <div class="grid" style="margin-top:12px"><div class="card span6"><div class="eyebrow">FACTION</div><div class="metric s" id="faName">Not scanned</div><div class="sub" id="faMeta"></div></div><div class="card span6"><div class="eyebrow">YOUR POSITION</div><div class="metric s" id="faRole">Unknown</div><div class="sub" id="faPermissions"></div></div></div>
  <div class="notice" id="faWarnings" style="margin-top:12px"></div>
  <div style="display:flex;flex-direction:column;gap:12px;margin-top:12px">
   <div class="card" id="faPersonalPanel"><div class="eyebrow">YOUR PARTICIPATION</div><div id="faPersonal" class="rows"></div></div>
   <div class="card" id="faLeadershipPanel"><div class="eyebrow" id="faPriorityTitle">Management and growth priorities</div><div class="sub">All panels remain available. Your verified position determines which priorities appear first; access still depends on your saved API key.</div><div class="formgrid" style="margin:12px 0"><label>Faction focus<select id="faGoal"><option value="balanced">Balanced growth</option><option value="war">War and chains</option><option value="crimes">Organized crimes</option></select></label><label>Review inactivity after (days)<input id="faIdle" type="number" min="1" max="30" value="3"></label></div><button class="btn" onclick="saveFactionFocus()">Save Local Focus</button><div id="faAdvice" class="rows" style="margin-top:12px"></div><div class="sub" id="faGrowth"></div></div>
   <div class="card" id="faOperationsPanel"><div class="eyebrow">CHAINS AND WARS — SAVED SNAPSHOT</div><div id="faChain" class="section"></div><div class="sub">Refresh in Torn before acting. This is not a live chain monitor or an assessment of member battle stats.</div><div id="faWars" class="rows" style="margin-top:12px"></div></div>
  </div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">MEMBER ACTIVITY AND CRIME PARTICIPATION</div><div class="sub">Candidates are unassigned, recently active members marked Okay, excluding the Recruit title. Check actual eligibility and role suitability in Torn; level does not reveal battle stats.</div><div id="faMembers" class="rows" style="margin-top:12px"></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">ORGANIZED CRIMES</div><div class="sub">Assigned-slot CPR belongs to that participant. Empty-slot CPR is for the scanning account only; it cannot rank other members for that slot.</div><div id="faCrimes" class="rows" style="margin-top:12px"></div></div>
  <div class="twocol" style="margin-top:14px"><div class="card"><div class="insight-head"><div><div class="eyebrow">FACTION MOMENTUM</div><div class="section">Respect &amp; membership</div></div><div class="chart-range" id="factionChartRange"><button onclick="setChartRange('faction',7)" data-days="7" aria-pressed="false">7d</button><button onclick="setChartRange('faction',30)" data-days="30" aria-pressed="true">30d</button><button onclick="setChartRange('faction',90)" data-days="90" aria-pressed="false">90d</button></div></div><div id="factionRespectChart" class="insight-chart"></div><div id="factionMemberChart" class="insight-chart"></div></div><div class="card"><div class="eyebrow">WHEN YOUR PEOPLE ARE HERE</div><div class="section">Hourly activity</div><div class="sub">Average observed members per sampled hour, last 30 days. Unknown status is excluded; gaps mean no collection.</div><div id="factionActivityChart" class="insight-chart"></div></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">LOCAL ACTIVITY HISTORY</div><label class="toggle"><input type="checkbox" id="faActivityEnabled" onchange="send('factionActivity',{profileId:S.profileId,enabled:this.checked})"> Record this profile's faction every 5 minutes while Commander is open and this profile is active</label><div id="faActivitySummary" class="sub"></div><div class="sub">Percentages describe observed online + idle samples, not time played. Blank cells mean no observations. Data stays on this PC; collection stops when Commander closes or you switch profiles.</div><div id="faHeatmap" class="activity-heatmap"></div><div id="faActivityMembers" class="rows"></div></div>
  <details class="card" style="margin-top:12px"><summary>Installed upgrades and growth context</summary><div class="sub">These are current levels, not next-level costs or an automatically optimized spending plan. Reported respect is not a verified unspent budget.</div><div id="faUpgrades" class="rows" style="margin-top:12px"></div></details>
  <div class="card" style="margin-top:12px"><div class="eyebrow">MISSION AND LEADERSHIP CHECKLIST — LOCAL ONLY</div><div class="sub">Track your agent mission objectives or faction jobs here. Tasks are manually entered, scoped to this profile and faction; nothing is sent to members or marked complete in Torn.</div><div class="formgrid" style="margin:12px 0"><label>Mission or leadership task<input id="faTaskTitle" type="text" maxlength="240" placeholder="e.g., Confirm weekend chain coverage"></label></div><div class="actions"><button class="btn" id="faTaskAdd" onclick="addFactionTask()">Add Local Task</button><button class="btn" onclick="send('openMissions')">Open Torn Missions</button></div><div id="faTasks" class="rows" style="margin-top:12px"></div></div>
 </section>
 <section class="page" id="war"><div class="top"><div><div class="eyebrow">Ranked war reporting</div><div class="bigtitle">War Payout Planner</div><div class="sub">Read your faction's report and preview a cash split</div></div><button class="btn" onclick="send('warCsv')">Export Payout CSV</button></div><div class="notice info">This calculates proposed payouts only. No funds are sent, no faction bank balance is reserved, and no one is marked paid. The budget is your entered amount after deciding how to value rewards and deduct expenses.</div><div class="card" style="margin-top:12px"><div class="formgrid"><label>Ranked war ID<input id="warId" type="number" min="1" step="1"></label></div><button class="btn primary" id="warScan" style="margin-top:10px" onclick="send('warScan',{profileId:S.profileId,warId:Number($('warId').value)})">Scan War Report</button><div id="warSummary" class="statusline"></div></div><div class="card" style="margin-top:12px" id="warSettings"><div class="formgrid"><label>Cash budget<input id="warBudget" type="number" min="0" max="1000000000000" step="1"></label><label>Attacks weight (%)<input id="warAttackPercent" type="number" min="0" max="100" step="1"></label><label>Minimum attacks<input id="warMinimum" type="number" min="0" step="1"></label><label>Per-person cap (blank = none)<input id="warCap" type="number" min="0" step="1"></label></div><div class="sub" style="margin-top:10px">The remaining percentage uses the report's war score. This is not raw respect or every attack made during the war. Members below the minimum receive zero; capped amounts are redistributed among the remaining weighted members. Unused money stays unallocated.</div><button class="btn primary" style="margin-top:10px" id="warCalculate" onclick="saveWarPlan()">Calculate and Save Preview</button></div><div class="card" style="margin-top:12px"><div class="eyebrow">DISTRIBUTION PREVIEW</div><div class="section">Where the budget goes</div><div id="warPayoutChart" class="route-bars"></div></div><div id="warRows" class="rows" style="margin-top:12px"></div></section>
 <section class="page" id="intel"><div class="top"><div><div class="eyebrow">Tools and integrations</div><div class="bigtitle">Intel Workspace</div><div class="sub">Commander tools alongside your Torn Intel account</div></div><button class="btn primary" onclick="send('openIntel')">Open Torn Intel Inside Commander</button></div><div class="notice info">Torn Intel opens in a browser tab inside this application using the active profile's browser session. Sign in there yourself. Commander never inserts your API key, imports the site's database or bypasses its subscription levels. Its features and privacy terms remain the site's responsibility.</div><div class="grid" style="margin-top:12px"><div class="card span6"><div class="section">Available directly in Commander</div><div class="sub">Travel planning and shop scans; company staff review, costs and star standings; faction roster, crimes and leadership advice; ranked-war payout previews and CSV reports; stock holdings, price history, benefit value scenarios and scan-time price targets; optional sampled faction activity history.</div></div><div class="card span6"><div class="section">Available through your Torn Intel account</div><div class="sub">Its shared faction tools, historical reports, war tools, spy integrations, alliances, chat, alerts, stock and travel dashboards. Some require its Pro or Platinum access. Opening the website does not make them native Commander features.</div></div><div class="card span12"><div class="section">Still requires separate development or services</div><div class="sub">Armory audits, upgrade-cost simulation, recruitment search, levelling lists, personal-stat time series, stock historical backfill, shared bank requests and cross-device notifications. A global player index and shared real-time chat need a hosted service; existing site history and forecast models are not included in Commander.</div></div></div></section>
 <section class="page" id="stocks"><div class="top"><div><div class="eyebrow">Torn Stock Exchange</div><div class="bigtitle">Stock Portfolio</div><div class="sub">Your holdings, local price history and benefit-block planning</div></div><div class="actions"><button class="btn primary" id="stockScan" onclick="send('stocksScan')">Scan Stocks</button><button class="btn" onclick="send('stocksCsv')">Export CSV</button><button class="btn" onclick="send('openStocks')">Open Torn Stocks</button></div></div><div class="notice info" id="stockStatus">Scan to begin.</div><div class="card hero" style="margin:12px 0"><div class="eyebrow">Portfolio market value</div><div class="metric" id="stockValue">Unknown</div><div class="sub">P/L deducts Torn's 0.1% selling fee. Benefit payouts and past realized gains are excluded. Quotes and targets update when you scan; there are no background or phone alerts.</div></div><div class="formgrid" style="margin:12px 0"><label>Find stock<input id="stockSearch" type="text" placeholder="Name or symbol" oninput="renderStocks(S.stocks,S.profileId)"></label></div><div class="sub">Charts show recorded hourly samples from the last 30 days, not complete market history. Gaps mean Commander did not scan. Payout value and interval are your assumptions for one first-tier payout; the yield is not a price forecast.</div><div id="stockRows" class="stock-table" style="margin-top:12px"></div></section>
 <section class="page" id="company">
  <div class="top">
   <div><div class="eyebrow">Local company intelligence</div><div class="bigtitle">Company Growth Advisor</div><div class="sub">Daily outcomes, advertising experiments and business trends — read-only recommendations</div></div>
   <div class="actions"><button class="btn primary" id="coSync" onclick="send('companySync')">Sync Company</button><button class="btn" onclick="send('companyCsv')">Export CSV</button><button class="btn" id="coCopyReport" onclick="send('companyCopyReport')" title="Copies company and employee stats, costs and recent results for you to share. Excludes API keys and browser sessions.">Copy Company Report</button><button class="btn" onclick="send('openCompany')">Open Torn Company</button><label class="sub"><input type="checkbox" id="coAuto" onchange="send('companyAuto',{enabled:this.checked})"> Daily sync while open</label></div>
  </div>
  <div class="actions" style="margin:12px 0"><button class="btn" onclick="setCompanyTab('overview')">Overview</button><button class="btn" onclick="setCompanyTab('staff')">Staff &amp; positions</button><button class="btn" onclick="setCompanyTab('intelligence')">History &amp; scenarios</button><button class="btn" onclick="setCompanyTab('accounting')">Costs &amp; records</button></div>
  <div data-company-panel="overview">
  <div class="grid" style="margin-top:12px">
   <div class="card span6"><div class="eyebrow">CURRENT COMPANY</div><div class="metric s" id="coName">No company synced</div><div class="sub" id="coMeta">Star rating and employees appear after sync</div></div>
   <div class="card span3"><div class="eyebrow">REPORTED DAILY INCOME</div><div class="metric s" id="coIncome">—</div><div class="sub">Gross income, not net profit</div></div>
   <div class="card span3"><div class="eyebrow">DAILY CUSTOMERS</div><div class="metric s" id="coCustomers">—</div><div class="sub">API-reported daily customers</div></div>
   <div class="card span4"><div class="eyebrow">DAILY ADVERTISING</div><div class="metric s" id="coAdvertising">—</div><div class="sub">Requires permitted key or manual entry</div></div>
   <div class="card span4"><div class="eyebrow">INCOME AFTER ADS</div><div class="metric s" id="coAfterAds">—</div><div class="sub">Excludes payroll, stock and other expenses</div></div>
   <div class="card span4"><div class="eyebrow">ACCOUNTED MARGIN</div><div class="metric s" id="coNet">—</div><div class="sub">Only when all entered costs are known; not audited profit</div></div>
  </div>
  <div class="card hero" style="margin-bottom:12px"><div class="eyebrow">Business health from your observations</div><div class="section" id="coBusinessState">No observation</div><div class="sub" id="coBusinessNote">Sync Company to begin.</div><details style="margin-top:10px"><summary>Cost breakdown</summary><div class="statusline" id="coBusinessMetrics"></div></details></div>
  <details class="card" style="margin-bottom:12px"><summary>Sunday star outlook</summary><div class="actions"><button class="btn" id="coMarketScan" onclick="send('companyMarket')">Scan Company Standings</button></div><div id="coMarketSummary" class="sub">Scan listings to compare weekly sales with companies of your type.</div><div class="sub">Promotion chance: unknown. Hidden star unlocks depend on lifetime income; there is no confirmed fixed wait at each star. The percentages below measure companies you outsell, not your chance of promotion. Lowest earners may be declining businesses.</div><div id="coMarketRows" class="rows"></div><a href="https://www.torn.com/forums.php?a=0&amp;b=0&amp;f=19&amp;p=threads&amp;t=16590771" target="_blank">Torn administrator explanation</a></details>
  <div class="card" style="margin:14px 0"><div class="insight-head"><div><div class="eyebrow">YOUR BUSINESS, OVER TIME</div><div class="section">Income &amp; costs</div><div class="sub">Daily observations. Missing costs leave gaps; they are never treated as zero.</div></div><div class="chart-range" id="companyChartRange"><button onclick="setChartRange('company',7)" data-days="7" aria-pressed="false">7d</button><button onclick="setChartRange('company',30)" data-days="30" aria-pressed="true">30d</button><button onclick="setChartRange('company',90)" data-days="90" aria-pressed="false">90d</button></div></div><div id="companyTrendChart" class="insight-chart"></div></div>
  <div class="card" style="margin-bottom:14px"><div class="eyebrow">GROWTH WITH A SPENDING LIMIT</div><div class="section">Advertising trial</div><div class="sub">Track a 7-day trial against the preceding week's recorded results at the baseline budget. This does not change Torn's advertising setting. Enter the actual daily ad cost when saving company costs.</div><div class="formgrid adviser-fields"><label>Test advertising ($/day)<input id="adTrialBudget" type="number" min="0" value="700000"></label><label>Acceptable daily loss ($)<input id="adTrialAllowance" type="number" min="0" value="300000"></label></div><div class="actions"><button class="btn primary" id="adTrialStart" onclick="startAdTrial(false)">Start 7-Day Trial</button><button class="btn" id="adTrialStop" onclick="startAdTrial(true)">Clear Trial</button></div><div id="adTrialReport" style="margin-top:12px"></div></div>
  </div>
  <div class="notice info" id="coStatus">No history yet. Sync a saved Torn API key to start daily collection.</div>

  <div data-company-panel="staff" hidden>
  <div class="card" style="margin-top:12px"><div class="eyebrow">BUSINESS SNAPSHOT</div><div class="section" id="coHealth">Sync Company to load company health.</div><div class="sub" id="coScan">No staff scan yet.</div><div class="notice info" id="coScanWarnings"></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">YOUR NEXT STEPS</div><div id="coActions" class="rows"></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">REVIEW YOUR NEW COMPANY — THEN PREVIEW A DRAFT</div><div class="section" id="coLayoutStage">Scan Company &amp; Staff first</div><div class="sub" id="coLayoutNote"></div><div class="statusline" id="coLayoutSelected"></div><div id="coLayouts" class="rows" style="margin-top:12px"></div><div id="coLayoutChecks" class="rows" style="margin-top:12px"></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">STAFF POSITION ADVISOR</div><div class="section" id="coTeamState">Not scanned</div><div class="sub" id="coTeamNote"></div><div class="statusline" id="coTeamScore"></div><div id="coTeamRows" class="rows" style="margin-top:12px"></div></div>
  <details class="card" style="margin-top:12px"><summary>Advanced: compare your own job counts</summary><div class="sub">You choose the job counts here. This comparison does not prove that those counts make business sense. Keep the total equal to your staff count. Use the starter draft for key-job coverage; a fresh scan returns to review mode.</div><div id="coJobs" class="rows" style="margin-top:12px"></div><div class="actions"><button class="btn primary" onclick="saveCompanyStaffPlan(false)">Compare These Counts</button><button class="btn" onclick="saveCompanyStaffPlan(true)">Review Current Positions</button></div></details>
  <details class="card" style="margin-top:12px" open><summary>Stock and pricing checks</summary><div class="sub">Reported stock and order quantities; these are not daily supply costs.</div><div id="coStockRows" class="rows" style="margin-top:12px"></div></details>
  </div>
  <div data-company-panel="accounting" hidden>
  <div class="card" style="margin-top:12px"><div class="eyebrow">ADVERTISING EXPERIMENT ADVISOR</div>
   <div class="section" id="coAdviceState">Building baseline</div>
   <div class="sub" id="coAdvice">A recommendation appears after enough comparable daily observations.</div>
   <div class="statusline" id="coDays">0 recorded days</div>
   <div class="statusline">Observational comparisons cannot isolate advertising from competition, staffing, price or inventory changes. Commander never changes your Torn advertising budget.</div>
  </div>
  <div class="card" style="margin-top:12px">
   <div class="eyebrow">OPTIONAL ACCOUNTING</div><div class="section">Today's company costs</div>
   <div class="sub">Enter costs for the UTC date shown below. Blank = unknown, not zero. Sync the company first; these figures stay on this PC and never go to Torn.</div>
   <div class="statusline" id="coDate">No dated record yet</div>
   <div class="formgrid" style="margin:12px 0">
    <label>Advertising $/day<input id="coAd" type="number" min="0" step="1" placeholder="Unknown" /></label>
    <label>Payroll $/day<input id="coPay" type="number" min="0" step="1" placeholder="Unknown" /></label>
    <label>Stock/supplies $/day<input id="coStock" type="number" min="0" step="1" placeholder="Unknown" /></label>
    <label>Context / changes<input id="coNotes" type="text" maxlength="240" placeholder="e.g., changed prices or staffing" /></label>
   </div>
   <div class="actions"><button class="btn primary" id="coSave" onclick="saveCompanyCosts()">Save Today's Costs</button></div>
  </div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">DAILY HISTORY</div><div class="section">Most recent 90 observations</div><div class="sub">One snapshot per UTC day. Re-syncing the same day updates it; it does not create extra performance samples.</div><div id="coHistory" style="margin-top:12px" class="rows"><div class="empty">No company data yet.</div></div></div>
  </div><div data-company-panel="intelligence" hidden><div class="card"><div class="section">Saved company history</div><label>Company available in this account's saved history<select id="coHistoryCompany" onchange="send('companyHistory',{profileId:S.profileId,companyId:Number(this.value)})"></select></label><div class="sub" id="coHistoryScope"></div></div>
 <div class="card" style="margin-top:12px"><div class="section">Compare a financial scenario</div><div class="twocol"><label>Advertising ($/day)<input id="coScenarioAds" type="number" min="0" placeholder="Use recorded amount"></label><label>Total wages ($/day)<input id="coScenarioPay" type="number" min="0" placeholder="Use recorded amount"></label><label>Stock/supplies ($/day)<input id="coScenarioStock" type="number" min="0" placeholder="Unknown unless recorded"></label><label>Your assumed revenue change (%)<input id="coScenarioRevenue" type="number" min="-100" max="1000" value="0"></label></div><button class="btn primary" onclick="calculateCompanyScenario()">Compare scenario</button><div class="sub">Blank costs use the recorded amount; missing costs stay unknown. Revenue change is your assumption, not a forecast. Nothing is changed in Torn.</div><div id="coScenarioResult" style="margin-top:12px"></div></div>
 <details class="card" style="margin-top:12px" open><summary>Advertising under comparable staff and star counts</summary><div class="sub">Completed UTC days only, grouped by budget, staff and stars. At least 3 days in a group are needed for a repeatable comparison. Differences are observed associations, not proof that advertising caused sales.</div><div id="coCohorts"></div></details>
 <details class="card" style="margin-top:12px" open><summary>Employee development and wages</summary><div class="sub">Changes between saved scans. Wages and effectiveness do not prove individual revenue contribution. Role coverage still matters.</div><div id="coEmployeeHistory"></div></details>
 <details class="card" style="margin-top:12px"><summary>Inactivity penalties by scan hour</summary><div class="sub">These are API-reported inactivity penalties when scans were taken, not continuous employee online monitoring. Missing hours stay empty.</div><div id="coPenaltyHeatmap" class="activity-grid"></div></details></div>
</section>
 <section class="page" id="settings"><div class="top"><div><div class="eyebrow">Profiles, privacy and sync</div><div class="bigtitle">Settings</div></div><div class="actions"><button class="btn primary" onclick="classic('Settings')">Open Full Settings</button></div></div><div class="card" id="themeSettingsCard"><div class="eyebrow">Appearance</div><div class="section">Commander color theme</div><div class="sub">Choose a dark palette. Changes preview immediately and save to this Windows installation; account credentials and game pages are unaffected.</div><div class="theme-options" role="group" aria-label="Color theme"><button type="button" class="theme-tile" data-choice="intel" aria-pressed="false" onclick="chooseTheme('intel')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Intel Night</span><span class="theme-caption">Charcoal, cyan &amp; teal</span></button><button type="button" class="theme-tile" data-choice="midnight" aria-pressed="true" onclick="chooseTheme('midnight')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Midnight Blue</span><span class="theme-caption">Original Commander</span></button><button type="button" class="theme-tile" data-choice="obsidian" aria-pressed="false" onclick="chooseTheme('obsidian')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Obsidian</span><span class="theme-caption">Graphite &amp; ice</span></button><button type="button" class="theme-tile" data-choice="emerald" aria-pressed="false" onclick="chooseTheme('emerald')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Emerald</span><span class="theme-caption">Forest &amp; mint</span></button><button type="button" class="theme-tile" data-choice="violet" aria-pressed="false" onclick="chooseTheme('violet')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Amethyst</span><span class="theme-caption">Purple &amp; lavender</span></button><button type="button" class="theme-tile" data-choice="ember" aria-pressed="false" onclick="chooseTheme('ember')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Ember</span><span class="theme-caption">Copper &amp; charcoal</span></button></div><div class="statusline" id="themeStatus" role="status">Midnight Blue selected. Saved locally.</div></div><div class="card"><div class="section">Manage account profiles</div><div class="sub">Each saved profile has its own Torn login, cookies, and API key. New profiles switch immediately to a clean sign-in. Removing a profile never deletes the actual Torn game account.</div><div class="actions" style="margin-top:12px"><button class="btn primary" onclick="send('profileAdd')">+ Add Profile</button><button class="btn" onclick="send('profileRename')">Rename Selected</button><button class="btn" onclick="send('profileDelete')">Remove Selected</button></div><div class="statusline" style="margin-top:10px" id="profilesSummary">Loading profiles…</div><div class="profile-list" id="profilesList"></div><div class="divider"></div><div class="sub">Up to 10 saved profiles in this build; the main browsing window uses one at a time. Loss Desk still has two simultaneous worker slots, A and B. WebView2 has no fixed documented account count: RAM and active browser processes determine performance. One Torn game account per human player. Deleted profile browser files are retained as a local backup, but its Commander API key is removed.</div><div class="divider"></div><div class="sub">API keys stay in Windows Credential Manager. Travel stock data is public and does not require your Torn API key.</div><div class="divider"></div><div class="eyebrow">ACTUAL EXECUTABLE IN USE</div><div id="actualBuildPath" class="statusline">Waiting for app path</div><button class="btn small" onclick="copyPath()" style="margin-top:9px">Copy running EXE path</button></div><div class="card hero" style="margin-top:12px"><div class="eyebrow">In-app updates</div><div class="section">Update Torn Commander</div><div class="sub">No more merging ZIPs into old folders. Select a trusted Commander release ZIP, or configure a public GitHub Releases repository to check and install newer versions. The app closes and rebuilds; your local account data stays separate.</div><div class="actions" style="margin:12px 0"><button class="btn primary" onclick="send('updateFromZip')">Install from ZIP…</button><button class="btn" onclick="send('checkGithubUpdate')">Check GitHub for Update</button></div><div class="field" style="margin:12px 0"><label for="rollbackVersion">Saved versions (rollback)</label>
<select id="rollbackVersion" aria-label="Select a saved Commander version" style="width:100%;max-width:340px"><option value="">No local version backups yet</option></select></div>
<div class="actions" style="margin-bottom:12px"><button class="btn" id="restoreVersionBtn" onclick="restoreCommander()" disabled>Revert / Switch to Selected Version</button></div>
<div class="statusline" id="rollbackHelp">The updater saves up to five compact source backups on this computer. No additional old app folders are intentionally kept. The currently running version is never listed as a rollback option.</div><div class="field"><label for="githubRepo">Public GitHub repository (owner/repo)</label><input type="text" id="githubRepo" placeholder="your-github-name/TornCommander"></div><button class="btn small" style="margin-top:9px" onclick="send('saveUpdateRepo',{repo:$('githubRepo').value})">Save Repository</button><div class="statusline">GitHub mode requires a published Release containing Torn_Commander_vX.Y.Z.zip. The first upgrade to this updater still requires one manual extraction. Only install releases you trust.</div></div><details class="card" style="margin-top:12px"><summary>Torn Intel connections</summary><div class="notice" id="iiConnection">Not checked</div><p class="sub">Approved client keys access documented foreign-stock feeds only. They are application credentials, do not identify a player, and grant no faction/company permissions. A website login key is separate. Native private-history sync is unavailable without a documented export; normal website sign-in remains in the embedded browser.</p><label>Approved application key<input id="iiClientKey" type="password" autocomplete="off" placeholder="Stored only in Windows Credential Manager"></label><button class="btn" onclick="saveIntelKey(true,false)">Save / replace client key</button><button class="btn" onclick="saveIntelKey(true,true)">Disconnect client key</button><button class="btn" onclick="send('intelClientRequest')">Request approved access</button><label>Separate website login key (local storage only)<input id="iiLoginKey" type="password" autocomplete="off"></label><button class="btn" onclick="saveIntelKey(false,false)">Save login key locally</button><button class="btn" onclick="saveIntelKey(false,true)">Forget local login key</button><button class="btn" onclick="send('intelCopyLogin',{profileId:S.profileId})">Copy saved login key</button><button class="btn" onclick="send('intelSignIn')">Open normal website sign-in</button><p class="sub">Saved login keys are not automatically transmitted or treated as native API approval. Use the website's normal login to connect its browser session. Website account identity and permissions are shown by the website; native stock-client access has no associated player.</p></details><details class="card" style="margin-top:12px"><summary>Data-source diagnostics</summary><div id="iiDatasetSources"></div><p class="sub">Private account datasets stay in the current local profile. Torn Intel's public stock client key does not grant private faction or company access.</p></details></section>
</div>
</div>
<script>
/*CASINO_COACH*/
'use strict';
let S={};const $=id=>document.getElementById(id);const money=n=>n==null?'—':'$'+Number(n).toLocaleString('en-US',{maximumFractionDigits:0});
const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function send(cmd,extra={}){window.chrome?.webview?.postMessage({cmd,...extra})}
const commanderThemes={midnight:'Midnight Blue',obsidian:'Obsidian',emerald:'Emerald',violet:'Amethyst',ember:'Ember',intel:'Intel Night'};
function applyTheme(theme){
 const selected=Object.prototype.hasOwnProperty.call(commanderThemes,theme)?theme:'midnight';
 document.documentElement.dataset.theme=selected;
 document.querySelectorAll('.theme-tile').forEach(el=>el.setAttribute('aria-pressed',String(el.dataset.choice===selected)));
 const status=$('themeStatus');if(status)status.textContent=commanderThemes[selected]+' selected. Saved locally.';
 return selected;
}
function chooseTheme(theme){
 const selected=applyTheme(theme);
 $('themeStatus').textContent=commanderThemes[selected]+' selected. Saving locally…';
 send('setTheme',{theme:selected});
}

function classic(p){send('classic',{page:p})}
function copyPath(){const p=S.buildPath||'';if(p){navigator.clipboard?.writeText(p).catch(()=>{});}}
function restoreCommander(){const v=$('rollbackVersion').value;if(v)send('restoreCommanderVersion',{version:v});}
function page(p){if(!$(p))return;document.querySelectorAll('.page').forEach(e=>e.classList.toggle('active',e.id===p));document.querySelectorAll('.nav[data-page]').forEach(e=>e.classList.toggle('active',e.dataset.page===p));$('navCurrent').textContent=document.querySelector('.nav.active')?.textContent?.trim()||p;$('navSide').classList.remove('expanded');$('navToggle').setAttribute('aria-expanded','false');document.querySelector('.content').scrollTop=0;if($('mobilePageSelect'))$('mobilePageSelect').value=p;}
document.querySelectorAll('.nav[data-page]').forEach(e=>e.addEventListener('click',()=>page(e.dataset.page)));
$('navToggle').addEventListener('click',()=>{const open=$('navSide').classList.toggle('expanded');$('navToggle').setAttribute('aria-expanded',String(open))});
$('profileSelect').addEventListener('change',e=>{if(e.target.value!==S.profileId)send('switchProfile',{profileId:e.target.value})});
$('profilesList').addEventListener('click',e=>{const b=e.target.closest('button[data-profile-id]');if(b&&b.dataset.profileId!==S.profileId)send('switchProfile',{profileId:b.dataset.profileId})});
function num(id,min,max){return Math.max(min,Math.min(max,Number($(id).value)||0))}
function saveTravel(refresh=true){const custom=$('tPlanStart').value;const parsed=custom?new Date(custom):null;const start=parsed&&!Number.isNaN(parsed.getTime())?parsed.toISOString():null;send(refresh?'travelSettings':'travelBuildPlan',{capacity:num('tCapacity',1,1000),flightType:$('tFlight').value,marketTax:$('tTax').checked,horizon:num('tHorizon',6,72),budget:num('tBudget',0,1000000000),book:$('tBook').checked,stockOnly:$('tStockOnly').checked,confidence:num('tConfidence',0,100),includeLow:$('tLow').checked,autoMonitor:$('tAutoMonitor').checked,monitorMinutes:num('tMonitorEvery',10,60),transferMinutes:num('tTransfer',0,120),planNow:$('tPlanNow').checked,pauseUntilHome:$('tPauseUntilHome').checked,showRouteMap:$('tShowRouteMap').checked,planStart:start})}
$('tPlanNow').addEventListener('change',()=>{if(!$('tPlanNow').checked&&!$('tPlanStart').value){const d=new Date(Date.now()+5*60000);$('tPlanStart').value=[d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-')+'T'+String(d.getHours()).padStart(2,'0')+':'+String(d.getMinutes()).padStart(2,'0')}$('tCustomStartWrap').style.display=$('tPlanNow').checked?'none':'flex';});
['tAutoMonitor','tMonitorEvery'].forEach(id=>$(id).addEventListener('change',()=>saveTravel(false)));
function renderArrival(t){const a=t.arrival||{};const observed=!!a.seen;$('arrivalDecision').textContent=a.action||'Awaiting shop scan';$('arrivalCash').textContent=observed?money(a.cash):'—';$('arrivalSlots').textContent=observed?String(a.slots??'—'):'—';$('arrivalAdvice').textContent=a.message||'Open the foreign shop and press Scan Shop.';$('arrivalNextCheck').textContent=(observed?'Observed '+a.seen+' · '+(a.country||'Foreign shop')+'. ':'')+(a.nextCheck||'');const changes=(a.recentChanges||[]);$('arrivalChanges').textContent=changes.length?'Observed replenishment (interval between scans): '+changes.join(' · '):'No stock increases observed yet. The live shelf is scanned approximately every 20 seconds while the Torn shop is open.';$('arrivalBasket').textContent=a.basketUnits>0?'Potential mixed load: '+a.basket+' · '+money(a.basketProfit)+' indicative net':'No affordable, positively valued mixed load verified.';$('arrivalItems').innerHTML=(a.choices||[]).slice(0,15).map(x=>`<div class="minirow" style="flex-wrap:wrap;align-items:flex-start"><div style="flex:1 1 170px"><b>${esc(x.item)}</b>${x.observedChange?'<span class="pill good" style="margin-left:6px">Restocked</span>':''}<div class="sub">Visible stock: ${x.stock} · affordable: ${x.units} · ${esc(x.priceEvidence)}</div><div class="statusline">${esc(x.observedChange||x.restock)} · ${esc(x.evidence)}</div></div><div style="text-align:right;min-width:100px">${x.profit>0?money(x.profit)+' net':'Profit unknown / unavailable'}</div></div>`).join('')||'<div class="empty">No shop rows scanned yet. This section will populate after you open the foreign shop and scan.</div>';}

function renderRouteLine(legs,enabled){const outer=$('itineraryMap'),track=$('itineraryMapTrack');outer.hidden=!enabled;if(!enabled)return;if(!legs.length){track.innerHTML='<div class="empty">No supported route to display yet.</div>';return}let pieces=['<div class="itin-node"><b>Torn</b><span>Start</span></div>'];for(let i=0;i<legs.length;i++){const x=legs[i];pieces.push('<span class="itin-line"></span>');pieces.push(`<button class="itin-node ${x.confidence<35?'low':''}" data-trip="${i+1}" title="Jump to trip ${i+1}"><b>${esc(x.country)}</b><span>Arrive ${esc(x.arrival)} · ${x.confidence}% heuristic</span></button>`);pieces.push('<span class="itin-line"></span>');pieces.push(`<button class="itin-node" data-trip="${i+1}" title="Trip ${i+1} back in Torn"><b>Torn</b><span>Return ${esc(x.back)}</span></button>`)}track.innerHTML=pieces.join('');track.querySelectorAll('[data-trip]').forEach(el=>el.addEventListener('click',()=>document.getElementById('itin-step-'+el.dataset.trip)?.scrollIntoView({behavior:'smooth',block:'nearest'})))}

function renderTravelPreview(report,travel,legs){
 report=report||{};
 const rows=report.routes||[];
 const situation=(travel?.situation?.state)||'unknown';
 const noDeparture=situation!=='home';
 const status=noDeparture?'Route preview while away from Torn':'Route alternatives / diagnostics';
 $('travelPreviewCount').textContent=status+' · '+rows.length+' options';
 const diagnostics=report.guidance||'Waiting for provider data.';
 const counts='Feed: '+(report.feedRows??0)+' · Priced: '+(report.pricedRows??0)+
  ' · Positive margin: '+(report.positiveMarginRows??0)+
  ' · Recent reported stock: '+(report.freshStockRows??0);
 $('travelPreviewDiagnostic').textContent=counts+' — '+diagnostics;
 $('travelPreviewRows').innerHTML=rows.length?rows.map((x,i)=>
  '<div class="minirow" style="align-items:flex-start;gap:12px;flex-wrap:wrap">'+
  '<div style="flex:1 1 210px"><b>'+esc(x.country)+' — '+esc(x.item)+'</b>'+
  '<div class="sub">'+esc(x.stockEvidence)+'</div>'+
  '<div class="sub">'+esc(x.source||'Source unknown')+' · '+Number(x.oneWayMinutes||0)+' min each way</div></div>'+
  '<div style="text-align:right;min-width:130px"><div><b>'+money(x.scenarioProfit)+'</b> possible net / trip</div>'+
  '<div class="sub">'+money(x.profitPerHour)+'/hour modeled · '+Number(x.units||0)+' items</div>'+
  '<div class="sub">Purchase cost '+money(x.buyCost)+'</div></div></div>').join(''):
  '<div class="empty">No defensible route comparison yet. '+esc(diagnostics)+'</div>';
 if(!legs.length && travel?.showRouteMap){
  const track=$('itineraryMapTrack');
  if(rows.length){
   const segments=['<div class="itin-node"><b>Torn</b><span>After actual return (time unknown)</span></div>'];
   rows.slice(0,3).forEach((x,i)=>{
    segments.push('<span class="itin-line"></span>');
    segments.push('<div class="itin-node low"><b>'+esc(x.country)+'</b><span>'+esc(x.item)+' · preview only</span></div>');
    segments.push('<span class="itin-line"></span>');
    segments.push('<div class="itin-node"><b>Torn</b><span>Return time not scheduled</span></div>');
   });
   track.innerHTML=segments.join('');
  }else track.innerHTML='<div class="empty">No supported route yet. '+esc(diagnostics)+'</div>';
 }
}
function rehabLegHtml(x){return '<div class="itin-step rehab-leg"><div class="itin-index">✚</div><div><div class="itin-route">Switzerland · Rehab first</div><div class="sub">Treatment cost varies. Bring cash and mark Rehab done after your visit.</div><div class="itin-times"><div><div class="eyebrow">Take off</div><strong>'+esc(x.departure)+'</strong></div><div><div class="eyebrow">Arrive / start rehab</div><strong>'+esc(x.arrival)+'</strong></div><div><div class="eyebrow">Back in Torn</div><strong>'+esc(x.back)+'</strong></div></div><div class="statusline">'+esc(x.availability)+'</div></div></div>';}
function renderItinerary(t){$('rehabStatus').textContent=t.rehabFirst?'Rehab is first in this profile’s route. Clear it after treatment.':'Add a rehab visit before your trading route.';$('rehabFirstButton').disabled=!!t.rehabFirst;$('rehabDoneButton').disabled=!t.rehabFirst;if(document.activeElement!==$('rehabStay'))$('rehabStay').value=t.rehabStay??10;const situation=t.situation||{};const label=({home:'In Torn — ready to depart',returning:'In flight — returning to Torn',outbound:'In flight — outbound',abroad:'Abroad — conditional return plan',unavailable:'Travel temporarily unavailable',unknown:'Location not yet confirmed'})[situation.state]||'Location not yet confirmed';$('travelStatusLabel').textContent=label;$('travelStatusDetails').textContent=(situation.description||'Run Fast Sync or open Torn Travel')+(situation.takeoffAt?' · Took off: '+situation.takeoffAt:'')+(situation.landingAt?' · Landing: '+situation.landingAt:'')+(situation.lastCheckedAt?' · Checked '+situation.lastCheckedAt:'');$('travelStatusBox').className='travel-state '+(situation.state==='home'?'ready':situation.state==='returning'?'flight':'hold');const itin=t.itinerary||{};const legs=itin.legs||[];const previewOnly=situation.state!=="home"&&!legs.length;$('itineraryPeriod').textContent=previewOnly?'Time window begins after your confirmed return to Torn':itin.start&&itin.end?`${itin.start} → ${itin.end}`:'24-hour planning window';$('itinProfit').textContent=legs.length?money(itin.profit):'—';$('itinTrips').textContent=legs.length?String(legs.length):'0';$('itinScore').textContent=legs.length?money(itin.score):'—';$('itinDisclaimer').textContent=(itin.note||'Plans are estimates. Always verify stock before flying.')+(itin.projected?' · '+itin.projected+' speculative leg(s).':'');$('tShowRouteMap').checked=!!t.showRouteMap;renderRouteLine(legs,!!t.showRouteMap);renderTravelPreview(t.preview,t,legs);$('itineraryRows').innerHTML=legs.length?legs.map(x=>x.kind==="rehab"?rehabLegHtml(x):`<div id="itin-step-${x.number}" class="itin-step ${x.availability?.startsWith('Modeled')?'itin-forecast':''}"><div class="itin-index">${x.number}</div><div><div class="itin-route">Torn → ${esc(x.country)} → Torn</div><div class="sub">Buy ${Number(x.units).toLocaleString()} × ${esc(x.item)} · ${money(x.profit)} possible net if stock survives · ${money(x.riskAdjusted)} risk-weighted score</div><div class="itin-times"><div><div class="eyebrow">Take off</div><strong>${esc(x.departure)}</strong></div><div><div class="eyebrow">Arrive abroad</div><strong>${esc(x.arrival)}</strong></div><div><div class="eyebrow">Back in Torn</div><strong>${esc(x.back)}</strong></div></div><div class="itin-meta"><span>Cash required <strong>${money(x.cash)}</strong></span><span>${esc(x.availability)} · heuristic confidence <strong>${x.confidence}%</strong></span></div></div></div>`).join(''):'<div class="empty">'+esc(itin.note||'No plan yet. Refresh stock to get started.')+'</div>';const active=!!t.monitor;$('travelNextCheck').textContent=active?(t.refreshing?'Refreshing feeds now…':t.nextPollAt?'Next feed check: '+t.nextPollAt:'Auto-monitor starts when Commander is ready'):'Auto-monitor off — use Save + Refresh manually';}

function setFilters(rows){for(const [id,values,placeholder] of [['tCountry',new Set(rows.map(x=>x.country).filter(Boolean)),'All countries'],['tCategory',new Set(rows.map(x=>x.category).filter(Boolean)),'All categories']]){const select=$(id),last=select.value;select.replaceChildren(new Option(placeholder,''));[...values].sort((a,b)=>a.localeCompare(b)).forEach(v=>select.add(new Option(v,v)));select.value=[...select.options].some(o=>o.value===last)?last:'';}}
function badge(x){if(x.isStale||x.status==='Stale')return '<span class="pill bad">Stale data</span>';if(x.status==='In stock')return '<span class="pill good">In stock*</span>';if(x.status==='Predicted')return '<span class="pill warn">Predicted '+esc(x.confidence)+'</span>';return '<span class="pill muted">Stock unverified</span>'}
function renderTravelRows(){const t=S.travel||{},rows=t.rows||[];const q=$('tSearch').value.trim().toLowerCase(),country=$('tCountry').value,cat=$('tCategory').value,status=$('tStatus').value;const filtered=rows.filter(x=>(!q||[x.item,x.country,x.category].join(' ').toLowerCase().includes(q))&&(!country||x.country===country)&&(!cat||x.category===cat)&&(!status||(status==='Stale'?(x.isStale||x.status==='Unverified'):x.status===status&&!x.isStale)));const priced=filtered.filter(x=>x.priceKnown&&Number(x.profitHr)>0);const best=priced.length?priced.reduce((a,b)=>b.profitHr>a.profitHr?b:a):null;$('resultCount').textContent=filtered.length+' of '+rows.length+' results';$('travelCount').textContent=rows.length.toLocaleString();$('travelCountDetail').textContent=rows.filter(x=>x.status==='In stock'&&!x.isStale).length+' in stock • '+rows.filter(x=>!x.priceKnown).length+' missing market price • '+rows.filter(x=>x.status==='Predicted').length+' predicted';$('bestHour').textContent=best&&best.priceKnown?money(best.profitHr)+'/hr':'—';const biggest=priced.reduce((a,b)=>(!a||b.trip>a.trip)?b:a,null);$('bestTrip').textContent=biggest?money(biggest.trip):'—';$('bestTripItem').textContent=biggest?(biggest.item+' · '+biggest.country):'No matching route';
const emptyMsg=t.hasError&&!rows.length?'The live travel provider is unavailable and Commander has no saved stock history yet. Refresh again later, or open Torn Travel Agency and build local observations over time.':t.hasError?'No routes match the current filters. Live provider data is unavailable, so Commander is only using cached/local observations right now.':(!rows.length?'No travel data yet. Press Save + Refresh to load opportunities.':'No matching viable travel opportunities. Try a different filter, budget, flight method, or refresh the feed.');$('travelRows').innerHTML=filtered.length?filtered.slice(0,70).map(x=>`<article class="opportunity"><div class="tleft"><div class="name">${esc(x.item)}</div><div class="sub">${esc(x.country)} · ${esc(x.category||'Item')} · ${esc(x.source)}</div><div style="margin-top:7px">${badge(x)}</div></div><div class="right"><div class="moneybig money">${x.priceKnown?money(x.profitHr)+'/hr':'Price unknown'}</div><div class="sub">${x.priceKnown?money(x.trip)+' net / trip · '+Number(x.roi).toFixed(1)+'% ROI':'No market valuation — stock only'}</div></div><div class="details"><span><strong>${Number(x.units||0).toLocaleString()}</strong> units planned</span><span>Cash <strong>${money(x.cash)}</strong></span><span>${x.flight}m flight each way</span><span>Stock <strong>${x.stock==null?'unknown':Number(x.stock).toLocaleString()}</strong></span>${x.stockAgeMinutes!=null?`<span>Stock age <strong>${x.stockAgeMinutes}m</strong></span>`:''}${x.priceAgeMinutes!=null?`<span>Valuation retrieved <strong>${x.priceAgeMinutes}m ago</strong></span>`:''}${x.restock?`<span>Restock est. <strong>${esc(x.restock)}</strong></span>`:''}${x.leave?`<span>Fly out ~ <strong>${esc(x.leave)}</strong></span>`:''}${x.note?`<span title="${esc(x.note)}">${esc(x.note)}</span>`:''}</div></article>`).join('')+(filtered.length>70?`<div class="statusline">Showing first 70 matching routes. Use filters to narrow the list.</div>`:''):`<div class="empty">${esc(emptyMsg)}</div>`;}
['tSearch','tCountry','tCategory','tStatus'].forEach(id=>$(id).addEventListener(id==='tSearch'?'input':'change',renderTravelRows));

const medItems=[{id:68,name:'Small FAK'},{id:67,name:'FAK'},{id:66,name:'Morphine'},{id:739,name:'O-negative blood bag'}];
let medProfileIdentity='',medConfigIdentity='',medicalState=null;
function loadMedicalSettings(){
 const row=medicalState?.profiles?.find(x=>x.id===$('medProfile').value);if(!row)return;const s=row.settings||{};medConfigIdentity=JSON.stringify([row.id,s]);
 $('medEnabled').checked=s.enabled!==false;$('medMode').value=s.automatic?'automatic':'recommend';$('medDetect').checked=s.autoDetect!==false;$('medBonus').disabled=$('medDetect').checked;$('medMaximum').disabled=$('medDetect').checked;$('medBonus').value=s.manualBonusPercent??0;$('medMaximum').value=s.manualMaximumMinutes??360;$('medReserve').value=s.reserveMinutes??0;$('medResume').checked=!!s.autoResume;$('medBlood').checked=!!s.bloodBags;
 $('medItemSettings').innerHTML=medItems.map(x=>'<div class="card"><label><input type="checkbox" id="medAllow'+x.id+'" '+(s.permissions?.[x.id]?'checked':'')+'> '+esc(x.name)+'</label><label>Keep at least<input id="medKeep'+x.id+'" type="number" min="0" value="'+(Number(s.reserves?.[x.id])||0)+'"></label><label>Tie-break price each ($; 0 = pharmacy/reference cost)<input id="medPrice'+x.id+'" type="number" min="0" value="'+(Number(s.prices?.[x.id])||0)+'"></label></div>').join('');
}
function saveMedicalSettings(){const permissions={},reserves={},prices={};for(const x of medItems){permissions[x.id]=$('medAllow'+x.id).checked;reserves[x.id]=Number($('medKeep'+x.id).value);const cost=Number($('medPrice'+x.id).value);if(cost>0)prices[x.id]=cost;}send('saveMedicalSettings',{profileId:$('medProfile').value,settings:{enabled:$('medEnabled').checked,automatic:$('medMode').value==='automatic',autoDetect:$('medDetect').checked,manualBonusPercent:Number($('medBonus').value),manualMaximumMinutes:Number($('medMaximum').value),reserveMinutes:Number($('medReserve').value),autoResume:$('medResume').checked,bloodBags:$('medBlood').checked,permissions,reserves,prices}});}
function renderSmartMedical(data){if(!data)return;medicalState=data;
 const profiles=JSON.stringify((data.profiles||[]).map(x=>[x.id,x.name]));if(profiles!==medProfileIdentity){medProfileIdentity=profiles;const before=$('medProfile').value;$('medProfile').replaceChildren(...(data.profiles||[]).map(p=>new Option(p.name,p.id)));if((data.profiles||[]).some(x=>x.id===before))$('medProfile').value=before;}
 const chosen=(data.profiles||[]).find(x=>x.id===$('medProfile').value);if(chosen&&JSON.stringify([chosen.id,chosen.settings])!==medConfigIdentity)loadMedicalSettings();
 const mins=n=>n==null?'Unknown':(Math.max(0,Number(n))/60).toFixed(1)+'m';
 $('medicalSessions').innerHTML=(data.sessions||[]).map(s=>'<div class="card"><div class="section">Account '+esc(s.slot)+' · '+esc((data.profiles||[]).find(p=>p.id===s.profile)?.name||s.profile)+'</div><div class="metric s">'+mins(s.cooldownSeconds)+' / '+mins(s.maximumSeconds)+'</div><div class="sub">Medical cooldown · Hospital remaining '+mins(s.hospitalSeconds)+' · Bonus '+(s.bonus??'unknown')+'%</div><div class="sub">'+esc(s.verification)+'</div><div class="sub">Observed '+esc(s.observed?new Date(s.observed).toLocaleTimeString():'not yet')+' · '+esc(s.note)+'</div><div>Next: '+esc(s.next||'No recommendation')+' · Complete planned heal '+mins(s.plannedCooldownSeconds)+'</div><div class="sub">Personal supplies: '+medItems.map(x=>x.name+' '+(s.inventory?.[x.id]??'unknown')).map(esc).join(' · ')+'</div><div class="sub">Session losses '+s.losses+' · Items used '+s.itemsUsed+' · Medical cooldown consumed '+mins(s.cooldownUsed)+' · Saved vs regular FAKs '+(s.savedSeconds<0?'−'+mins(-s.savedSeconds)+' (extra cost)':mins(s.savedSeconds))+'</div><div class="sub">Estimated remaining heals/losses: '+(s.estimatedHeals==null?'Collect a hospital observation first':Math.floor(s.estimatedHeals))+' · based on supplies and last observed hospital duration; energy, future hospital lengths and elapsed cooldown can change this.</div></div>').join('')||'<div class="empty">Start a worker to scan personal medical supplies. Automatic consumption is off until explicitly saved for that account.</div>';
}
let levelingIdentity='';
function levelAction(cmd,extra={}){send(cmd,{profileId:S.profileId,...extra});}
function saveScouterKey(){const key=$('levelScoutKey').value;levelAction('levelScoutKey',{key});$('levelScoutKey').value='';}
function saveScoutFilters(){levelAction('levelScoutSave',{ratio:Number($('levelStatRatio').value)/100,maxff:Number($('levelMaxFF').value),days:Number($('levelEstimateDays').value),inactive:$('levelInactive').checked});}
function loadBaldrList(){levelAction('levelLoadBaldr',{group:$('levelBaldrGroup').value});}
function saveLeveling(){const inventory={};for(const id of [68,67,66])inventory[id]=Number($('levelQty'+id).value);levelAction('levelSave',{targets:$('levelTargets').value,reserve:Number($('levelReserve').value),lifePercent:Number($('levelLife').value),limit:Number($('levelLimit').value),weapon:$('levelWeapon').value,delay:Number($('levelDelay').value),autoHeal:$('levelAutoHeal').checked,inventory});}
let levelingLastData=null;
function renderLeveling(data){
 levelingLastData=data;if(!data)return;const s=data.session||{},a=data.account,identity=JSON.stringify([data.profileId,s.energyReserve,s.lifePercent,s.attackLimit,s.weaponSlot,s.attackDelayMs,s.autoHeal,s.inventory,s.maxStatRatio,s.maxFairFight,s.maxEstimateDays,s.inactiveOnly,s.targets?.map(t=>t.id)]);
 if(identity!==levelingIdentity){if(renderLeveling.profile!==data.profileId)$('levelScoutView').value='all';levelingIdentity=identity;renderLeveling.profile=data.profileId;$('levelScoutKey').value='';$('levelStatRatio').value=(s.maxStatRatio??0.5)*100;$('levelMaxFF').value=s.maxFairFight??2;$('levelEstimateDays').value=s.maxEstimateDays??30;$('levelInactive').checked=s.inactiveOnly??true;$('levelTargets').value='';$('levelReserve').value=s.energyReserve??0;$('levelLife').value=s.lifePercent??100;$('levelLimit').value=s.attackLimit??0;$('levelWeapon').value=s.weaponSlot||'weapon_primary';$('levelDelay').value=s.attackDelayMs??1000;$('levelAutoHeal').checked=!!s.autoHeal;for(const id of [68,67,66])$('levelQty'+id).value=s.inventory?.[id]??0;}
 $('levelScoutConnection').textContent=data.scouterConfigured?'Key saved for this local account':'No FFScouter key saved';$('levelScoutSummary').textContent=s.ownStats?'Your total stats: '+Number(s.ownStats).toLocaleString()+' | Target cap: '+Math.floor(s.ownStats*(s.maxStatRatio??0.5)).toLocaleString()+' | Own stats read '+new Date(s.ownStatsUtc).toLocaleString():'Scan/search to read your own battle stats. Strength checks are required before fights.';
 const groups=JSON.stringify(s.baldrGroups||[]);if(s.baldrGroups?.length&&renderLeveling.groups!==groups){const selected=$('levelBaldrGroup').value;$('levelBaldrGroup').replaceChildren(new Option('All lists',''),...s.baldrGroups.map(x=>new Option(x,x)));$('levelBaldrGroup').value=s.baldrGroups.includes(selected)?selected:'';renderLeveling.groups=groups;}
 $('levelBaldrSource').textContent=s.baldrLoadedUtc?'Baldr source: OranWeb/tc-baldrs-levelling-list/data.json · Loaded '+new Date(s.baldrLoadedUtc).toLocaleString()+' · Historical list stats; availability checked separately.':'';
 $('levelSummary').textContent=(s.wins||0)+' verified Leave wins · '+(s.otherResults||0)+' other results · '+(a?'Energy '+a.energy+' · Life '+a.life+'/'+a.maximum+' · About '+data.estimatedAttacks+' attacks above reserve · '+(data.stale?'STALE — check again':'Checked '+new Date(a.at).toLocaleTimeString()):'Check next to read this account');
 $('levelWorker').textContent='Worker: '+(data.worker||'Not started')+(data.medical?' · Medical items used '+data.medical.itemsUsed+' · Cooldown spent '+(data.medical.cooldownUsed/60).toFixed(1)+'m':'');$('levelNote').textContent=s.note||'';const recovery=a?.recovery;const names={68:'Small FAK',67:'FAK',66:'Morphine'};const counts={};for(const id of recovery?.items||[])counts[id]=(counts[id]||0)+1;
 $('levelRecovery').textContent=recovery?recovery.state+': '+recovery.reason+' '+Object.entries(counts).map(([id,n])=>n+' × '+names[id]).join(' + ')+(recovery.cooldownSeconds?' · '+(recovery.cooldownSeconds/60)+'m total medical cooldown':'')+' · Supply counts '+(s.inventoryUtc?new Date(s.inventoryUtc).toLocaleTimeString():'not entered'):'Recovery plan appears after an account check.';
 $('levelOpen').disabled=!!data.stale||!s.pendingTarget||!!s.openedUtc||a?.recovery?.state!=='Ready';
 $('levelRows').innerHTML=(s.targets||[]).filter(t=>$('levelScoutView').value!=='within'||(t.estimatedStats>0&&s.ownStats>0&&t.estimatedStats<=s.ownStats*(s.maxStatRatio??0.5)&&(t.fairFight==null||t.fairFight<=(s.maxFairFight??2))&&t.estimateUtc&&Date.now()-Date.parse(t.estimateUtc)<=(s.maxEstimateDays??30)*86400000&&Date.parse(t.estimateUtc)<=Date.now()+300000&&t.scannedUtc&&Date.now()-Date.parse(t.scannedUtc)<=86400000)).map((t,i)=>'<div class="card" style="margin-bottom:8px"><b>'+esc((i+1)+'. '+(t.name||'Player')+' ['+t.id+']')+'</b><div class="sub">'+esc(t.status||'Not checked')+' | '+esc(t.suitability||'Strength scan required')+(t.estimatedStats!=null?' | Estimated stats '+Number(t.estimatedStats).toLocaleString()+' | '+esc(t.estimateSource||'FFScouter')+' | Estimate dated '+esc(t.estimateUtc?new Date(t.estimateUtc).toLocaleString():'unknown'):'')+(t.source?' · '+esc(t.source):'')+(t.listedBattleStats!=null?' · Listed battle stats '+Number(t.listedBattleStats).toLocaleString()+' (historical)':'')+' · Level '+(Number(t.level)||'?')+(t.fairFight!=null?' · FF estimate for supplied key '+esc(t.fairFight):'')+' · '+esc(t.checkedUtc?new Date(t.checkedUtc).toLocaleTimeString():'No live status yet')+'</div>'+(t.id===s.pendingTarget?'<span class="pill">'+(s.openedUtc?'Awaiting fight result':'Next target')+'</span>':'')+'</div>').join('')||'<div class="empty">No targets imported for this account.</div>';
}
function saveLossTiming(){
 const milliseconds=Number($('lossAttackDelay').value);
 if(!Number.isSafeInteger(milliseconds)||milliseconds<500||milliseconds>5000){alert('Choose a Pillow delay from 500 to 5000 milliseconds.');return}
 send('lossTiming',{milliseconds});
}
const chartRanges={company:30,faction:30};
function setChartRange(key,days){chartRanges[key]=days;if(key==='company')drawCompanyChart(S.company);else drawFactionCharts(S.faction);}
function rangeRows(rows,key,timeField){const days=chartRanges[key]||30,cutoff=Date.now()-days*86400000;return (rows||[]).filter(x=>Date.parse(x[timeField])>=cutoff).sort((a,b)=>Date.parse(a[timeField])-Date.parse(b[timeField]));}
function chartLegend(series){return '<div class="chart-legend">'+series.map(s=>'<span><i style="background:'+s.color+'"></i>'+esc(s.label)+'</span>').join('')+'</div>';}
function insightChart(rows,timeField,series,moneyAxis=false,gapHours=36){
 const data=(rows||[]).filter(x=>Number.isFinite(Date.parse(x[timeField])));
 const values=data.flatMap(x=>series.filter(s=>x[s.key]!=null&&Number.isFinite(Number(x[s.key]))).map(s=>Number(x[s.key])));
 if(!values.length)return '<div class="chart-empty">Your story starts with the first scan.<br>Recorded data will appear here as it builds.</div>';
 const times=data.map(x=>Date.parse(x[timeField])),minT=Math.min(...times),maxT=Math.max(...times),spanT=maxT-minT||86400000;
 let lo=Math.min(...values),hi=Math.max(...values);if(lo===hi){lo-=Math.max(1,Math.abs(lo)*.05);hi+=Math.max(1,Math.abs(hi)*.05);}const span=hi-lo;
 const X=t=>60+(t-minT)/spanT*770,Y=v=>195-(v-lo)/span*170;
 const compact=n=>moneyAxis?(n<0?'−':'')+'$'+(Math.abs(n)>=1e6?(Math.abs(n)/1e6).toFixed(1)+'m':Math.abs(n)>=1000?(Math.abs(n)/1000).toFixed(0)+'k':Math.abs(n).toFixed(0)):Math.abs(n)>=1000?(n/1000).toFixed(1)+'k':Number(n.toFixed(1));
 let html='<svg viewBox="0 0 860 235" role="img" aria-label="'+esc(series.map(s=>s.label).join(', ')+' over recorded time')+'">';
 for(let i=0;i<=4;i++){const v=lo+span*i/4,y=Y(v);html+='<line class="chart-grid" x1="60" x2="830" y1="'+y+'" y2="'+y+'"/><text class="chart-axis" x="50" y="'+(y+3)+'" text-anchor="end">'+compact(v)+'</text>';}
 for(const line of series){let last=null,path='';for(const x of data){const value=x[line.key],time=Date.parse(x[timeField]);if(value==null||!Number.isFinite(Number(value))){last=null;continue;}const px=X(time),py=Y(Number(value));path+=(last!=null&&time-last<=gapHours*3600000?'L':'M')+px.toFixed(1)+','+py.toFixed(1);last=time;html+='<circle cx="'+px+'" cy="'+py+'" r="3" fill="'+line.color+'" tabindex="0"><title>'+esc(x[timeField]+' · '+line.label+': '+(moneyAxis?money(value):Number(value).toLocaleString()))+'</title></circle>';}html+='<path d="'+path+'" fill="none" stroke="'+line.color+'" stroke-width="2.5" stroke-linejoin="round"/>';}
 for(const t of [minT,maxT])html+='<text class="chart-axis" x="'+X(t)+'" y="225" text-anchor="'+(t===minT?'start':'end')+'">'+new Date(t).toISOString().slice(0,10)+'</text>';
 return html+'</svg>'+chartLegend(series);
}
function markChartRange(key){const parent=$(key+'ChartRange');if(parent)parent.querySelectorAll?.('button').forEach(b=>b.setAttribute('aria-pressed',String(Number(b.dataset.days)===chartRanges[key])));}
function drawCompanyChart(c){markChartRange('company');$('companyTrendChart').innerHTML=insightChart(rangeRows(c?.rows,'company','day'),'day',[{key:'income',label:'Income',color:'#60c7fa'},{key:'ad',label:'Advertising',color:'#be9cff'},{key:'net',label:'Accounted margin',color:'#2ed5b0'}],true);}
function drawFactionCharts(f){markChartRange('faction');const rows=rangeRows(f?.history,'faction','day');$('factionRespectChart').innerHTML=insightChart(rows,'day',[{key:'respect',label:'Respect',color:'#60c7fa'}]);$('factionMemberChart').innerHTML=insightChart(rows,'day',[{key:'members',label:'Members',color:'#be9cff'}]);$('factionActivityChart').innerHTML=insightChart(f?.activity?.timeline,'at',[{key:'online',label:'Online',color:'#2ed5b0'},{key:'idle',label:'Idle',color:'#f5c56b'},{key:'offline',label:'Offline',color:'#fa718c'}],false,2);}
function drawTravelChart(t){const rows=(t?.rows||[]).filter(x=>Number.isFinite(Number(x.profitHr))&&Number(x.profitHr)>0).sort((a,b)=>b.profitHr-a.profitHr).slice(0,6),max=rows[0]?.profitHr||1;$('travelOpportunityChart').innerHTML=rows.map(x=>'<div><div class="route-bar-label"><span>'+esc(x.country)+' · '+esc(x.item)+'</span><strong>'+money(x.profitHr)+'/hr</strong></div><div class="route-bar-track"><div class="route-bar-fill" style="width:'+(Number(x.profitHr)/max*100).toFixed(1)+'%"></div></div></div>').join('')||'<div class="chart-empty">Refresh travel data to compare routes.</div>';}
let companyHistoryIdentity='',companyScenarioIdentity='';
function setCompanyTab(key){document.querySelectorAll('[data-company-panel]').forEach(el=>el.hidden=el.dataset.companyPanel!==key)}
function calculateCompanyScenario(){const n=id=>$(id).value.trim()===''?null:Number($(id).value);send('companyScenario',{profileId:S.profileId,companyId:Number($('coHistoryCompany').value),ads:n('coScenarioAds'),payroll:n('coScenarioPay'),supplies:n('coScenarioStock'),revenueChange:Number($('coScenarioRevenue').value)})}
function renderCompanyHistory(data,scenario,profile){if(!data)return;
 const key=JSON.stringify([profile,data.companies]);if(key!==companyHistoryIdentity){companyHistoryIdentity=key;$('coHistoryCompany').replaceChildren(...(data.companies||[]).map(x=>new Option(x.name+' #'+x.id,x.id)));}
 $('coHistoryCompany').value=data.selected??'';
 const identity=profile+'|'+data.selected;if(identity!==companyScenarioIdentity){companyScenarioIdentity=identity;for(const id of ['coScenarioAds','coScenarioPay','coScenarioStock'])$(id).value='';$('coScenarioRevenue').value=0;}
 $('coHistoryScope').textContent=(data.selected===data.liveCompany?'Current company. ':'Historical company view; current API scans still use the account’s live company. ')+(data.scans??0)+' saved staff scans.';
 const value=x=>x==null?'Unknown':money(x),result=scenario||data.baseline;
 $('coScenarioResult').innerHTML=result?'<div class="statusline">Scenario revenue '+value(result.revenue)+' · costs: ads '+value(result.advertising)+', payroll '+value(result.payroll)+', supplies '+value(result.supplies)+'</div><div class="metric s">Scenario net '+value(result.net)+'</div><div class="sub">Extra revenue needed to break even: '+value(result.extraRevenueNeeded)+' · Funds at that daily loss: '+(result.runwayDays??'—')+' days</div><div class="sub">'+esc(result.note)+'</div>':'<div class="empty">Sync actual company results first.</div>';
 $('coCohorts').innerHTML=(data.cohorts||[]).map(x=>'<div class="minirow"><b>'+money(x.budget)+' ads · '+x.staff+' staff · '+x.stars+' stars</b><div class="sub">'+x.days+' days · average revenue '+money(x.averageIncome)+' · after ads '+money(x.averageAfterAds)+' · '+(x.eligible?'enough samples to review':'collect more days')+'</div></div>').join('')||'<div class="empty">No comparable completed-day records yet.</div>';
 const delta=x=>x==null?'not measured':Number(x).toLocaleString();
 $('coEmployeeHistory').innerHTML=(data.staff||[]).map(x=>'<div class="minirow"><b>'+esc(x.name)+' · '+esc(x.position)+'</b><div class="sub">Observed stat change: MAN '+delta(x.manualDelta)+' · INT '+delta(x.intelligenceDelta)+' · END '+delta(x.enduranceDelta)+'</div><div class="sub">Daily wage '+value(x.wage)+' · effectiveness '+(x.effectiveness??'unknown')+' · inactivity penalty '+(x.inactivity??'unknown')+'</div></div>').join('')||'<div class="empty">Employee history begins with authorized company scans.</div>';
 const bins=new Map((data.heatmap||[]).map(x=>[x.day+'|'+x.hour,x]));$('coPenaltyHeatmap').innerHTML=Array.from({length:168},(_,i)=>{const x=bins.get(Math.floor(i/24)+'|'+i%24);return '<div class="activity-cell" title="'+(x?x.samples+' employee readings · average penalty '+x.averagePenalty:'No scan evidence')+'" style="'+(x?'background:rgba(236,156,81,'+Math.min(.9,.15+x.averagePenalty/100)+')':'')+'"></div>'}).join('');
}
function renderTravelWait(t){const w=t?.waitAdvice;$('travelWaitAdvice').innerHTML=w?.options?.length?'<div class="sub">'+esc(w.location)+' · comparison at '+esc(new Date(w.at).toLocaleString())+'</div>'+w.options.map(x=>'<div class="minirow"><b>'+esc(x.kind)+' · '+esc(x.item)+'</b><div class="sub">Wait '+x.waitMinutes+' min · future time '+x.futureMinutes+' min · conditional margin '+money(x.conditionalProfit)+' · '+money(x.conditionalProfitPerHour)+'/hr</div><div class="sub">'+esc(x.source)+' · '+esc(x.evidence)+'</div></div>').join('')+'<div class="sub">'+esc(w.note)+'</div>':'<div class="empty">Sync your current destination and collect fresh stock reports. Available alternatives appear when priced; a wait comparison needs a supported restock window.</div>';}
let factionFormIdentity='';
function saveFactionFocus(){
 const days=Number($('faIdle').value);
 if(!Number.isSafeInteger(days)||days<1||days>30){alert('Choose an inactivity review threshold from 1 to 30 days.');return}
 send('factionFocus',{profileId:S.profileId,goal:$('faGoal').value,inactivityDays:days});
}
function addFactionTask(){
 const title=$('faTaskTitle').value.trim();if(!title){alert('Enter a mission or leadership task.');return}
 send('factionTask',{profileId:S.profileId,factionId:S.faction?.id,title});
 $('faTaskTitle').value='';
}
function factionTaskAction(id,done,remove){
 send('factionTask',{profileId:S.profileId,factionId:S.faction?.id,id,done,remove});
}
function renderFaction(f,profile){
 drawFactionCharts(f);
 const activity=f?.activity||{};
 $('faActivityEnabled').checked=!!activity.enabled;
 $('faActivitySummary').textContent=Number(activity.hours||0)+' recorded hours in the last 30 days. Rows run Sunday–Saturday; columns 00–23 UTC.';
 $('faHeatmap').innerHTML=(activity.heatmap||[]).map(x=>'<div class="activity-cell" title="'+['Sun','Mon','Tue','Wed','Thu','Fri','Sat'][x.day]+' '+Number(x.hour)+':00 UTC · '+(x.samples?Number(x.activePercent)+'% active across '+Number(x.samples)+' member samples':'No samples')+'" style="'+(x.samples?'background:rgba(46,213,176,'+(0.12+Number(x.activePercent)/100*0.8)+')':'')+'"></div>').join('');
 $('faActivityMembers').innerHTML=(activity.members||[]).map(m=>'<div class="minirow"><strong>'+esc(m.name)+'</strong><span>'+Number(m.activePercent)+'% active · '+Number(m.online)+' online / '+Number(m.idle)+' idle / '+Number(m.offline)+' offline samples</span></div>').join('');
 f=f||{};const num=x=>x==null?'Unknown':Number(x).toLocaleString();
 const when=x=>x?new Date(Number(x)*1000).toISOString().replace('T',' ').slice(0,16)+' UTC':'Unknown';
 const line=x=>'<div class="minirow"><div class="sub">'+esc(x)+'</div></div>';
 $('faStatus').textContent=(f.status||'Scan your faction to begin.')+(f.at?' · '+f.at+' · '+num(f.ageMinutes)+' minutes old':'');
 $('faName').textContent=f.name||'Not scanned';
 $('faMeta').textContent='Rank '+(f.rank||'Unknown')+' · Members '+num(f.count)+' / '+num(f.capacity)+' · Reported respect '+num(f.respect);
 $('faRole').textContent=(f.position||'Unknown position')+' · '+(f.role||'Unknown')+' priorities';
 $('faPermissions').textContent=f.abilitiesLoaded?'Position abilities: '+((f.abilities||[]).join(', ')||'None returned'):'Position permissions not exposed. Leader/co-leader identity can still be verified; custom officer titles are not guessed.';
 $('faWarnings').textContent=(f.warnings||[]).join(' | ');
 $('faWarnings').hidden=!(f.warnings||[]).length;
 $('faScan').disabled=!!f.scanning;$('faScan').textContent=f.scanning?'Scanning…':'Scan Faction';
 $('faCopy').disabled=!f.id||!!f.scanning;$('faTaskAdd').disabled=!f.id;
 const isMember=!['Leader','Co-leader','Officer'].includes(f.role);
 $('faPersonalPanel').style.order=isMember?'-3':'1';
 $('faLeadershipPanel').style.order=isMember?'1':'-3';
 $('faOperationsPanel').style.order=f.role==='Officer'?'-2':'0';
 $('faPriorityTitle').textContent=f.role==='Officer'?'Coordination and growth priorities':'Management and growth priorities';
 $('faPersonal').innerHTML=(f.personal||[]).map(line).join('')||line('Scan the faction for your personal participation.');
 $('faAdvice').innerHTML=(f.advice||[]).map(line).join('')||line('Scan the faction to build a data-based action list.');
 $('faGrowth').textContent=f.previousDay?'Compared with '+f.previousDay+': respect change '+num(f.respectChange)+' · member change '+num(f.membersChange)+'. Observed change, not a forecast.':'Collect another UTC day to compare membership and respect.';
 const identity=String(profile)+'|'+String(f.id||0)+'|'+(f.goal||'balanced')+'|'+String(f.inactivityDays||3);
 if(identity!==factionFormIdentity){factionFormIdentity=identity;$('faGoal').value=f.goal||'balanced';$('faIdle').value=f.inactivityDays||3;$('faTaskTitle').value='';}
 const chain=f.chain||{};
 $('faChain').textContent=!chain.loaded?'Chain data unavailable':chain.count==null?'Chain count unknown':chain.count===0?'No active chain recorded at scan':'Recorded chain '+num(chain.count)+' / '+num(chain.max)+' · '+(chain.remaining==null?'Deadline unavailable':chain.remaining<=0?'Saved hit deadline passed; refresh now':num(chain.remaining)+' seconds to the saved hit deadline');
 $('faWars').innerHTML=(f.wars||[]).map(w=>'<div class="minirow" style="display:block"><b>'+esc(w.kind)+' #'+esc(w.id)+'</b><div class="sub">Start '+when(w.start)+' · End '+when(w.end)+'</div><div class="sub">'+esc((w.sides||[]).map(s=>s.name+' (score '+num(s.score)+')').join(' vs '))+'</div></div>').join('')||line(f.warsLoaded?'No wars returned in this snapshot.':'War data unavailable.');
 const members=[...(f.members||[])].sort((a,b)=>Number(b.isMe)-Number(a.isMe)||Number(b.needsReview)-Number(a.needsReview)||(b.idleDays??-1)-(a.idleDays??-1));
 $('faMembers').innerHTML=members.map(m=>'<div class="minirow" style="display:block"><b>'+esc(m.name)+(m.isMe?' · You':'')+'</b><div class="sub">'+esc(m.position)+' · Level '+num(m.level)+' · '+esc(m.status||'Unknown status')+' · '+esc(m.activity||'Unknown presence')+'</div><div class="sub">Last action '+(m.idleDays==null?'Unknown':Number(m.idleDays).toFixed(1)+' days ago')+' · In organized crime: '+(m.inCrime==null?'Unknown':m.inCrime?'Yes':'No')+(m.needsReview?' · Inactivity review':'')+(m.candidate?' · Candidate to review for an open crime slot':'')+'</div></div>').join('')||line(f.membersLoaded?'No members returned.':'Member roster unavailable.');
 $('faCrimes').innerHTML=(f.crimes||[]).map(c=>'<div class="minirow" style="display:block"><b>'+esc(c.name)+' #'+esc(c.id)+'</b><div class="sub">'+esc(c.status)+' · Difficulty '+num(c.difficulty)+' · Ready '+when(c.readyAt)+' · Expires '+when(c.expiresAt)+'</div>'+
 (c.slots||[]).map(x=>'<div class="sub" style="margin-top:8px">'+esc(x.position)+' — '+esc(x.name)+(x.isMe?' · You':'')+' · '+(x.userId?'Assigned member CPR ':'Your CPR for open slot ')+num(x.passRate)+' · Planning progress '+num(x.progress)+' · Required item '+(x.itemAvailable==null?'not reported / not required':x.itemAvailable?'available':'MISSING')+'</div>').join('')+'</div>').join('')||line(f.crimesLoaded?'No available organized crimes returned.':'Organized crimes unavailable: check faction API access on this key.');
 $('faUpgrades').innerHTML=(f.upgrades||[]).map(u=>'<div class="minirow" style="display:block"><b>'+esc(u.name)+' · Level '+num(u.level)+'</b><div class="sub">'+esc(u.branch)+' · '+esc(u.ability)+'</div></div>').join('')||line(f.upgradesLoaded?'No upgrades returned.':'Upgrade details unavailable for this key.');
 $('faTasks').innerHTML=(f.tasks||[]).map(t=>'<div class="minirow"><label style="flex:1"><input type="checkbox" data-faction-task="'+esc(t.id)+'" '+(t.done?'checked ':'')+'onchange="factionTaskAction(this.dataset.factionTask,this.checked,false)"> '+esc(t.title)+'</label><button class="btn small" data-faction-task="'+esc(t.id)+'" onclick="factionTaskAction(this.dataset.factionTask,false,true)">Remove</button></div>').join('')||line('No local mission or leadership tasks yet.');
}
let coRecordIdentity='';
let coPlanIdentity='',coPlanCompany=0,coPlanToken='';
let coStaffIdentity='';
function previewCompanyLayout(scenarioId){
 send('companyStaffPlan',{companyId:coPlanCompany,scanToken:coPlanToken,scenarioId});
}
function saveCompanyStaffPlan(reset){
 const counts=Array.from(document.querySelectorAll('[data-company-role]')).map(x=>({positionId:Number(x.dataset.companyRole),count:Number(x.value)}));
 if(!reset&&counts.some(x=>!Number.isSafeInteger(x.count)||x.count<0||x.count>200)){alert('Role counts must be whole numbers from 0 to 200.');return}
 send('companyStaffPlan',{companyId:coPlanCompany,scanToken:coPlanToken,counts,reset});
}
function saveCompanyCosts(){
 const parse=id=>{
   const s=$(id).value.trim();
   if(s==='')return null;
   if(!/^\d+$/.test(s))return NaN;
   const n=Number(s);
   return Number.isSafeInteger(n)&&n>=0?n:NaN;
 };
 const values={advertising:parse('coAd'),payroll:parse('coPay'),stock:parse('coStock'),notes:$('coNotes').value.slice(0,240)};
 if([values.advertising,values.payroll,values.stock].some(x=>Number.isNaN(x))){
   $('coStatus').textContent='Costs must be whole nonnegative numbers or left blank.';
   return;
 }
 send('companyCosts',values);
}
let warFormIdentity='';
function saveWarPlan(){
 const cap=$('warCap').value.trim();
 send('warPlan',{profileId:S.profileId,warId:S.warPayout?.warId,budget:Number($('warBudget').value),attackPercent:Number($('warAttackPercent').value),minimumAttacks:Number($('warMinimum').value),cap:cap===''?null:Number(cap)});
}
function renderWar(w,profile){
 w=w||{};
 const identity=profile+'|'+(w.warId||0),changed=identity!==warFormIdentity;warFormIdentity=identity;
 $('warScan').disabled=!!w.scanning;$('warCalculate').disabled=!!w.scanning||!w.warId;
 $('warSummary').textContent=w.warId?'War '+w.warId+' · Faction '+w.factionId+' · '+(w.end?'Completed report':'Report not finalized')+' · Unallocated '+money(w.unallocated):'Enter a ranked war ID. The report must include your current faction.';
 if(changed)$('warId').value=w.warId||'';
 if(changed||!document.activeElement?.closest?.('#warSettings')){
 $('warBudget').value=w.budget??0;$('warAttackPercent').value=w.attackPercent??50;$('warMinimum').value=w.minimumAttacks??0;$('warCap').value=w.cap??'';
 }
 const payoutRows=(w.rows||[]).filter(x=>x.amount>0).slice(0,8),payoutMax=payoutRows[0]?.amount||1;
 $('warPayoutChart').innerHTML=payoutRows.map(x=>'<div><div class="route-bar-label"><span>'+esc(x.name)+'</span><strong>'+money(x.amount)+'</strong></div><div class="route-bar-track"><div class="route-bar-fill" style="width:'+(Number(x.amount)/payoutMax*100).toFixed(1)+'%"></div></div></div>').join('')||'<div class="sub">Calculate a payout preview to compare allocations.</div>';
 $('warRows').innerHTML=(w.rows||[]).map(x=>'<div class="minirow"><div><strong>'+esc(x.name)+' ['+Number(x.id)+']</strong><div class="sub">'+Number(x.attacks)+' attacks · '+Number(x.score)+' war score'+(x.eligible?'':' · Below minimum')+'</div></div><strong>'+money(x.amount)+'</strong></div>').join('');
}
function saveStockTarget(id){
 const value=suffix=>{const v=$('st'+id+suffix).value.trim();return v===''?null:Number(v)};
 send('stockTarget',{profileId:S.profileId,id,buy:value('buy'),sell:value('sell'),payout:value('payout'),days:Number($('st'+id+'days').value)});
}
function stockChart(points){return insightChart(points,'at',[{key:'price',label:'Recorded share price',color:'#60c7fa'}],true,2);}
let stockFormProfile="";
function renderStocks(data,profile){
 const switched=stockFormProfile!==profile;stockFormProfile=profile;
 if(switched)$('stockSearch').value="";
 const r=data?.report||{};
 $('stockScan').disabled=!!data?.scanning;
 $('stockStatus').textContent=(r.status||'Scan to begin.')+' · '+(r.at||'Never')+(r.stale?' · Quotes may be old':'');
 $('stockValue').textContent=r.holdingsKnown?money(r.value):'Holdings unavailable';
 const search=$('stockSearch').value.toLowerCase();
 if(!switched&&document.activeElement?.closest?.('#stockRows'))return;
 $('stockRows').innerHTML=(r.rows||[]).filter(q=>(q.name+' '+q.symbol).toLowerCase().includes(search)).map(q=>{
 const id=Number(q.id),t=q.target||{},price=n=>n==null?'Unknown':'$'+Number(n).toLocaleString('en-US',{minimumFractionDigits:2,maximumFractionDigits:2});
 return '<article class="card '+(q.signal!=='Watching'?'stock-target':'')+'"><div class="eyebrow">'+esc(q.symbol)+'</div><div class="section">'+esc(q.name)+'</div><div class="metric">'+price(q.price)+'</div><div class="sub">Shares: '+(q.shares==null?'unknown':Number(q.shares).toLocaleString())+' · Net P/L: '+money(q.netProfit)+'</div>'+stockChart(q.history||[])+'<div class="sub">Observed range: '+price(q.low)+' – '+price(q.high)+' · '+Number(q.samples)+' hourly samples</div><div class="divider"></div><div class="sub">'+esc(q.benefit)+'</div><div class="statusline">First block: '+money(q.blockCost)+' · Still needed: '+(q.shares==null?'unknown':money(q.remainingCost))+(q.available?' · Benefit ready':'')+'</div><details style="margin-top:10px"><summary>Targets and benefit value</summary><div class="formgrid" style="margin-top:10px">'+[['buy','Buy target',t.buy],['sell','Sell target',t.sell],['payout','One payout value',t.payout],['days','Payout interval (days)',t.days??7]].map(([key,label,value])=>'<label>'+label+'<input id="st'+id+key+'" type="number" min="'+(key==='days'?1:0)+'" step="'+(key==='days'?1:'0.01')+'" value="'+(value??'')+'"></label>').join('')+'</div><button class="btn small" style="margin-top:8px" onclick="saveStockTarget('+id+')">Save Targets</button><div class="statusline">'+esc(q.signal)+' · First-block annual payout yield: '+(q.annualPayoutYield==null?'unknown / passive benefit':Number(q.annualPayoutYield).toFixed(1)+'%')+'</div></details></article>';
 }).join('')||'<div class="empty">No matching stocks. Scan Stocks to load prices.</div>';
}
function startAdTrial(stop){send('companyAdTrial',{profileId:S.profileId,companyId:S.company?.id,stop,budget:Number($('adTrialBudget').value),allowedDailyLoss:Number($('adTrialAllowance').value)});}
let adTrialFormProfile="";
function renderAdTrial(c,profile){
 if(profile!==undefined&&adTrialFormProfile!==profile){adTrialFormProfile=profile;$('adTrialBudget').value=c?.adTrial?.budget??700000;$('adTrialAllowance').value=c?.adTrial?.allowedDailyLoss??300000;}
 const r=c?.adTrial||{};
 $('adTrialStart').disabled=!!r.active||!c?.id;
 $('adTrialStop').disabled=!r.active;
 $('adTrialReport').innerHTML=r.active?'<div class="section">'+esc(r.state)+'</div><div class="sub">'+esc(r.start)+' → '+esc(r.end)+' · Test '+money(r.budget)+'/day · Allowance '+money(r.allowedDailyLoss)+'/day</div><div class="smallgrid" style="margin:12px 0"><div class="statbox">Baseline: '+Number(r.baselineDays)+' days<br>Sales/day '+money(r.revenueBefore)+'</div><div class="statbox">Test: '+Number(r.testDays)+' days<br>Sales/day '+money(r.revenueAfter)+'</div><div class="statbox">Extra sales/day<br>'+money(r.extraSales)+'</div><div class="statbox">Change after ads/day<br>'+money(r.afterAdsChange)+'</div></div><div class="sub">Known average daily loss: '+money(r.knownLoss)+(r.costsComplete?'':' (minimum; some costs unknown)')+' · Largest known daily loss: '+money(r.peakKnownLoss)+' · Extra revenue per extra advertising dollar: '+(r.extraRevenuePerAdDollar==null?'unknown':Number(r.extraRevenuePerAdDollar).toFixed(2))+'</div><div class="notice '+(r.overAllowance?'':'info')+'" style="margin-top:10px">'+esc(r.note)+'</div><div class="statusline">'+esc(r.disclaimer)+'</div>':'<div class="sub">No trial active. A growth subsidy can be intentional; the comparison measures whether the extra spending is producing extra business.</div>';
}
let completedTradeProfile='',completedTradeReceipt='';
function recordCompletedTrade(){
 const read=id=>{const text=$(id).value.trim();return text===''?NaN:Number(text)};
 if(!completedTradeReceipt)completedTradeReceipt=typeof crypto.randomUUID==='function'?crypto.randomUUID():'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g,c=>{const r=crypto.getRandomValues(new Uint8Array(1))[0]&15;return (c==='x'?r:(r&3|8)).toString(16)});
 const request={profileId:S.profileId,receipt:completedTradeReceipt,item:$('completedItem').value,quantity:read('completedQuantity'),buy:read('completedBuy'),sell:read('completedSell'),fees:read('completedFees')};
 if(!request.item.trim()||Object.values(request).some(x=>typeof x==='number'&&!Number.isFinite(x))){alert('Enter the completed quantity, actual buy/sell prices and total actual fees. Enter 0 only if there were no fees.');return;}
 send('tradeRecordSale',request);
}
function renderTradeResults(t,profile){
 if(completedTradeProfile!==profile||t?.lastReceipt===completedTradeReceipt){
 completedTradeProfile=profile;completedTradeReceipt='';for(const id of ['completedItem','completedBuy','completedSell','completedFees'])$(id).value='';$('completedQuantity').value=1;
 }
 $('tradeFeeCoverage').textContent=Number(t?.unknownFees||0)+' older records have unknown fees and are excluded.';
}
function renderCompany(c,profile){
 renderCompanyHistory(c?.historySuite,c?.scenario,profile);
 drawCompanyChart(c);renderAdTrial(c,profile);
 c=c||{};
 $('coStatus').textContent=c.status||'Company history not loaded.';
 const market=c.market||{};
 $('coMarketScan').disabled=!!c.syncing;
 $('coMarketSummary').textContent=market.available?('Your weekly sales: '+(market.weeklyIncome==null?'unknown':money(market.weeklyIncome))+' · Scanned '+market.scannedUtc+(market.stale?' · OLD DATA: scan again':'')+' · API listings may be cached. Sales can change before Sunday.'):'Scan listings to compare weekly sales with companies of your type.';
 $('coMarketRows').innerHTML=(market.rows||[]).filter(r=>r.count>0||r.next).map(r=>'<div class="row"><strong>'+Number(r.stars)+' stars'+(r.next?' — next star':'')+'</strong><div>'+Number(r.count)+' other companies · You outsell '+(r.beatenPercent==null?'unknown':Number(r.beatenPercent)+'%')+'</div><div class="sub">Lowest weekly sales: '+(r.lowest==null?'unknown':money(r.lowest))+' · Middle company: '+(r.median==null?'unknown':money(r.median))+'</div></div>').join('');

 $('coName').textContent=c.name||'No company synced';
 $('coMeta').textContent=(c.type||'')+' · Stars: '+(c.rating??'—')+' · Employees: '+(c.staff??'—')+' · Funds: '+(c.health?.funds==null?'Unknown':money(c.health.funds));
 $('coIncome').textContent=c.income==null?'—':money(c.income);
 $('coCustomers').textContent=c.customers==null?'—':Number(c.customers).toLocaleString();
 $('coAdvertising').textContent=c.ad==null?'Unknown':money(c.ad);
 $('coAfterAds').textContent=c.afterAd==null?'Unknown':money(c.afterAd);
 $('coNet').textContent=c.net==null?'Unknown':money(c.net);
 const b=c.business||{};
 $('coBusinessState').textContent=b.state||'No observation';
 $('coBusinessNote').textContent=b.note||'Sync Company to begin.';
 $('coBusinessMetrics').textContent='Recorded costs: '+(b.knownCosts==null?'unknown':money(b.knownCosts))+
  (b.completeCosts?'':' (minimum; some costs missing)')+' · Revenue/customer: '+(b.revenuePerCustomer==null?'unknown':money(b.revenuePerCustomer))+
  ' · Ads/customer: '+(b.advertisingPerCustomer==null?'unknown':money(b.advertisingPerCustomer))+
  ' · Customers to cover recorded costs at current average revenue: '+(b.breakEvenCustomers??'unknown')+
  ' (scenario, not a sales forecast) · Income vs previous recorded day: '+(b.incomeChange==null?'unknown':money(b.incomeChange));
 const health=c.health||{},scan=c.scan||{},team=c.team||{};
 const known=x=>x==null?'Unknown':Number(x).toLocaleString();
 const stats=x=>x?'MAN '+known(x.manualLabor)+' / INT '+known(x.intelligence)+' / END '+known(x.endurance):'Working stats unavailable';
 $('coHealth').textContent='Efficiency '+known(health.efficiency)+'% · Environment '+known(health.environment)+'% · Popularity '+known(health.popularity)+'%';
 $('coScan').textContent=(scan.at?'Scanned '+scan.at:'No staff scan yet')+' · Capacity '+known(c.staff)+' / '+known(health.capacity)+' · Trains '+known(health.trains)+' · Funds '+(health.funds==null?'Unknown':money(health.funds))+' · Scanned wages/day '+(scan.wageTotal==null?'Unknown':money(scan.wageTotal));
 $('coScanWarnings').textContent=(scan.warnings||[]).join(' | ');
 $('coScanWarnings').hidden=!(scan.warnings||[]).length;
 $('coActions').innerHTML=(team.actions||[]).map((x,i)=>'<div class="minirow"><div><b>Step '+(i+1)+'</b><div class="sub">'+esc(x)+'</div></div></div>').join('')||'<div class="empty">Sync Company for a progression plan based on current company data.</div>';
 const layouts=c.layouts||{};
 $('coLayoutStage').textContent=layouts.stage||'Scan Company & Staff first';
 $('coLayoutNote').textContent=layouts.note||'Guided layouts use your company type and actual staff count.';
 $('coLayoutSelected').textContent='Named assignments below use: '+(layouts.selected||'Current role counts');
 $('coLayouts').innerHTML=(layouts.options||[]).map(x=>'<div class="minirow" style="display:block"><b>'+esc(x.name)+' · Unvalidated draft'+'</b><div class="sub">'+esc(x.reason)+'</div><div>'+esc(x.countsText)+'</div><div class="sub">Calculated job-fit total '+known(x.workingScore)+' · '+known(x.underqualified)+' below recommended stats.'+(x.trainerWorkingFit==null?'':' Trainer working-stat fit: '+known(x.trainerWorkingFit)+'.')+' This is not an income estimate.</div><button class="btn primary" data-company-layout="'+esc(x.id)+'" onclick="previewCompanyLayout(this.dataset.companyLayout)"'+(c.syncing?' disabled':'')+'>Preview Staff Placements</button></div>').join('')||'<div class="empty">No guided layout available for this scan. The custom role-count builder remains available below.</div>';
 $('coLayoutChecks').innerHTML=(layouts.checks||[]).map(x=>'<div class="minirow"><div class="sub">'+esc(x)+'</div></div>').join('');
 $('coTeamState').textContent=team.state||'NOT SCANNED';
 $('coTeamNote').textContent=team.note||'Sync Company to scan employees and their job requirements.';
 $('coTeamScore').textContent=String(team.state||'').includes('REVIEW ONLY')?'These are your current jobs, not an endorsement of this setup. Preview a draft before moving people.':String(team.state||'').startsWith('STARTER DRAFT')?'Key-job coverage comes first in this draft. Everyone stays included, even with very low stats. The total score can fall; it does not decide which setup earns more.':team.before==null?'A complete staff list and working stats are needed. Low stats are allowed; missing stats are different.':'Custom comparison: calculated job-fit total '+known(team.before)+' → '+known(team.after)+'. A higher total does not prove this job mix is better for the company.';
 const staffIdentity=String(profile)+'|'+String(c.id||0)+'|'+JSON.stringify(team.rows||[]);
 if(staffIdentity!==coStaffIdentity){
  coStaffIdentity=staffIdentity;
  $('coTeamRows').innerHTML=(team.rows||[]).map(x=>{
   const explanation=x.explanation||{};
   return '<div class="minirow" style="display:block"><b>'+esc(x.name)+' ['+esc(x.id)+']</b><div>'+(String(team.state||'').includes('REVIEW ONLY')?'Current position: '+esc(x.current):esc(x.current)+' → '+esc(x.recommended))+'</div><div class="sub">'+esc(explanation.summary||'')+'</div><div class="sub">'+esc(stats(x.stats))+'</div><div class="sub">Modeled work-stat score '+known(x.before)+' → '+known(x.after)+'</div><div class="sub">'+esc(x.nextStep)+'</div>'+
   (x.before!=null&&x.actualWorking!=null&&Math.abs(x.before-x.actualWorking)>=5?'<div class="sub">Verify score mismatch — see details before moving.</div>':'')+
   '<details style="margin-top:10px"><summary class="btn" style="display:list-item;cursor:pointer">Why this position? — Show details</summary><div style="margin-top:10px"><b>'+esc(explanation.summary||'Detailed reasoning requires a fresh company scan.')+'</b>'+
   (explanation.reasons||[]).map(reason=>'<p class="sub">'+esc(reason)+'</p>').join('')+
   '<div class="sub">Recommended role requirements: '+esc(stats(x.required))+'</div>'+
   (explanation.statComparison||[]).map(reason=>'<div class="sub">'+esc(reason)+'</div>').join('')+
   '<p class="sub">Torn reported work-stat score '+known(x.actualWorking)+' · Total effectiveness '+known(x.actualTotal)+'</p>'+
   '<div class="sub">Wage/day '+(x.wage==null?'Unknown':money(x.wage))+' · API contribution estimate '+(x.contributionValue==null?'Not exposed by this scan':money(x.contributionValue))+' (reporting period unspecified)</div>'+
   '<p class="notice">'+esc(explanation.reliability||'Verify the latest scan before acting on a projection.')+'</p></div></details></div>';
  }).join('')||'<div class="empty">No employees returned. Sync with company employee-detail access to load working stats.</div>';
 }
 const planIdentity=String(profile)+'|'+String(c.id||0)+'|'+(scan.token||'')+'|'+JSON.stringify((scan.jobs||[]).map(x=>[x.id,x.count]));
 if(planIdentity!==coPlanIdentity){
  coPlanIdentity=planIdentity;coPlanCompany=c.id||0;coPlanToken=scan.token||'';
  $('coJobs').innerHTML=(scan.jobs||[]).map(x=>'<div class="minirow" style="display:block"><label><b>'+esc(x.name)+'</b> slots <input type="number" min="0" max="200" step="1" data-company-role="'+esc(x.id)+'" value="'+esc(x.count)+'" style="max-width:90px"></label><div class="sub">'+esc(stats(x.required))+' · Role ability: '+esc(x.ability||'None reported')+'</div><div class="sub">'+esc(x.description||'')+'</div></div>').join('')||'<div class="empty">Job requirements unavailable.</div>';
 }
 $('coStockRows').innerHTML=(scan.stock||[]).map(x=>'<div class="minirow" style="display:block"><b>'+esc(x.name)+'</b><div class="sub">In stock '+known(x.inStock)+' · On order '+known(x.onOrder)+' · Unit cost '+(x.cost==null?'Unknown':money(x.cost))+' · Selling price '+(x.price==null?'Unknown':money(x.price))+' · Unit margin before wages/ads '+(x.cost==null||x.price==null?'Unknown':money(x.price-x.cost))+(x.cost!=null&&x.price!=null&&x.price<x.cost?' — selling below reported unit cost; review pricing':'')+'</div></div>').join('')||'<div class="empty">'+(scan.stockLoaded?'No stocked products returned for this company.':'Stock details unavailable. Check company stock API permission.')+'</div>';
 $('coAdviceState').textContent=c.state||'NO BASELINE';
 $('coAdvice').textContent=c.advice||'Collect daily observations first.';
 $('coDays').textContent=(c.samples||0)+' daily records with recorded advertising · Suggested experimental level: '+(c.suggested?money(c.suggested):'Not established');
 $('coAuto').checked=!!c.autoCollect;
 $('coSync').disabled=!!c.syncing;
 $('coSync').textContent=c.syncing?'Scanning…':'Scan Company & Staff';
 $('coSave').disabled=!c.day;
 $('coCopyReport').disabled=!c.day||!!c.syncing;
 $('coDate').textContent=c.day?'Editing UTC date '+c.day:'Sync Company before entering costs';
 const identity=String(profile)+'|'+String(c.id||0)+'|'+(c.day||'');
 if(identity!==coRecordIdentity){
   coRecordIdentity=identity;
   for(const [id,value] of [['coAd',c.ad],['coPay',c.payroll],['coStock',c.stock]]){
     $(id).value=value==null?'':String(value);
   }
   $('coNotes').value=c.notes||'';
 }
 const rows=c.rows||[];
 $('coHistory').innerHTML=rows.length?rows.map(x=>
   '<div class="minirow" style="gap:12px;flex-wrap:wrap"><div style="flex:1 1 160px"><b>'+esc(x.day)+'</b><div class="sub">'+(x.customers==null?'unknown':Number(x.customers).toLocaleString())+' customers · '+(x.staff??'unknown')+' staff · '+(x.rating??'unknown')+' stars</div>'+
   (x.notes?'<div class="sub">'+esc(x.notes)+'</div>':'')+'</div><div style="text-align:right">'+
   '<div>Income '+(x.income==null?'unknown':money(x.income))+'</div><div class="sub">Ads '+(x.ad==null?'unknown':money(x.ad))+' · After ads '+(x.afterAd==null?'unknown':money(x.afterAd))+'</div>'+
   '<div class="sub">Accounted margin '+(x.net==null?'unknown':money(x.net))+'</div></div></div>').join('')
   :'<div class="empty">Sync Company to start storing dated observations locally.</div>';
}
let progressionIdentity='';
function saveProgressionGoals(){
 const values=['goalTraining','goalReserve','goalTarget'].map(id=>Number($(id).value));
 if(values.some(x=>!Number.isSafeInteger(x))||values[0]<0||values[0]>100||values[1]<0||values[1]>1e12||values[2]<1||values[2]>1e12){alert('Enter a training share from 0 to 100, a nonnegative reserve, and a positive trading target.');return}
 send('progressionGoals',{training:values[0],reserve:values[1],target:values[2]});
}
function renderProgression(p,profile){
 p=p||{};const r=p.report||{},g=p.goals||{};
 $('progressionNote').textContent=r.note||'Sync to build your plan.';
 $('progressionMetrics').textContent=(r.day||'Today')+' · Known net trading margin '+money(r.recordedMargin||0)+' / '+money(r.target||0)+
  ' across '+Number(r.trades||0)+' entries (fees excluded) · Wallet above reserve: '+(r.spendableCash==null?'needs sync':money(r.spendableCash))+
  (r.unknownFeeTrades?' · '+r.unknownFeeTrades+' trade(s) excluded until selling fees are recorded.':'')+
  (r.legacyTradesExcluded?' · '+r.legacyTradesExcluded+' unassigned older trade(s) excluded.':'');
 const actions={training:['openGym','Open gym'],education:['openEducation','Open education'],nerve:['openCrimes','Open crimes']};
 $('progressionSteps').innerHTML=(r.steps||[]).map(x=>{
  const action=actions[x.id];const button=action?'<button class="btn small" data-cmd="'+esc(action[0])+'" onclick="send(this.dataset.cmd)">'+action[1]+'</button>':'<button class="btn small" onclick="page(\'travel\')">View travel</button><button class="btn small" onclick="classic(\'Trading\')">Log trade</button>';
  return '<div class="minirow" style="align-items:flex-start;flex-wrap:wrap"><label style="flex:1 1 220px"><input type="checkbox" data-task="'+esc(x.id)+'" '+(x.done?'checked':'')+' onchange="send(\'progressionDone\',{id:this.dataset.task,done:this.checked})"> <b>'+esc(x.title)+'</b><div class="sub">'+esc(x.detail)+'</div></label><div class="actions">'+button+'</div></div>';
 }).join('');
 const identity=profile+'|'+r.day;
 if(identity!==progressionIdentity){progressionIdentity=identity;for(const [id,value] of [['goalTraining',g.training],['goalReserve',g.reserve],['goalTarget',g.target]])$(id).value=value??'';}
}
function renderApiActivity(d){
 const a=d?.apiActivity||{};
 const seconds=Math.max(0,Math.ceil((Date.parse(a.cooldownUntil||'')-Date.now())/1000)||0);
 const cooling=seconds>0, busy=!!a.syncing;
 $('fastSyncButton').disabled=busy||cooling;$('fullSyncButton').disabled=busy||cooling;
 $('apiBadge').textContent=cooling?'API cooldown '+seconds+'s':busy?'API sync in progress':d?.apiConnected?(d.apiErrors?'API partial':'API configured'):'API key needed';
 $('apiBadge').className='pill '+(cooling||!d?.apiConnected||d.apiErrors?'warn':busy?'muted':'good');
 $('apiBadge').title='Commander paces Torn API reads; '+Number(a.queued||0)+' waiting at last refresh.';
}
setInterval(()=>renderApiActivity(S),1000);
let casinoAdviceGeneration=0;
document.addEventListener('input',event=>{
 if(event.target?.closest('#casino')){casinoAdviceGeneration++;$('casinoAdvice').textContent='Hand changed. Press an adviser button to recalculate.';}
});
async function casinoAdvice(game){
 const generation=++casinoAdviceGeneration,profile=S.profileId;
 const val=id=>$(id).value,num=id=>Number(val(id));
 try{
  let result;
  if(game==='blackjack')result=CasinoCoach.blackjack(val('bjCards'),val('bjDealer'),$('bjDouble').checked,$('bjSplit').checked,$('bjSurrender').checked);
  else if(game==='highlow')result=CasinoCoach.highlow(val('hlCard'),num('hlGrowth'),num('hlCash'));
  else if(game==='expected'){if(!val('evChance')||!val('evPayout')||!val('evStake'))throw Error('Enter chance, net win multiplier and stake.');result=CasinoCoach.expected(num('evChance'),num('evPayout'),num('evStake'));}
  else{if(!val('pkPot')||!val('pkCall'))throw Error('Enter pot and cost to call (0 is allowed).');$('casinoAdvice').textContent='Calculating sampled poker equity...';result=await CasinoCoach.poker(val('pkHole'),val('pkBoard'),num('pkOpponents'),num('pkPot'),num('pkCall'),1200,()=>generation!==casinoAdviceGeneration||profile!==S.profileId);}
  if(generation===casinoAdviceGeneration&&profile===S.profileId)$('casinoAdvice').textContent=result;
 }catch(error){if(generation===casinoAdviceGeneration&&profile===S.profileId)$('casinoAdvice').textContent=error.message}
}
let tradeFormIdentity='';
function scanTrading(){
 const value=id=>Number($(id).value);
 const request={profileId:S.profileId,stress:value('tradeStress'),exposure:value('tradeExposure'),minimumProfit:value('tradeMinimumProfit'),items:$('tradeItems').value,budget:value('tradeBudget'),reserve:value('tradeReserve'),fee:value('tradeFee'),roi:value('tradeRoi'),costBasis:value('tradeCostBasis')};
 if(!request.items.trim()||Object.values(request).some(x=>typeof x==='number'&&!Number.isFinite(x))){alert('Enter item names and valid numeric limits.');return;}
 send('tradeScan',request);
}
function renderTrading(r,profile){
 if(!r)return;
 const identity=profile+'|'+r.settings;
 if(tradeFormIdentity!==identity){tradeFormIdentity=identity;for(const [id,value] of [['tradeItems',r.items],['tradeBudget',r.budget],['tradeReserve',r.reserve],['tradeFee',r.fee],['tradeRoi',r.roi],['tradeCostBasis',r.costBasis],['tradeStress',r.stress??3],['tradeExposure',r.exposure??50],['tradeMinimumProfit',r.minimumProfit??1000]])$(id).value=value;}
 $('tradeScanButton').disabled=!!r.scanning;$('tradeScanNote').textContent=r.note||'Ready';$('tradeCapitalNote').textContent=r.capitalNote||'';
 $('tradeAdviceRows').innerHTML=(r.rows||[]).map(x=>'<div class="card" style="margin-top:10px"><div class="section">'+esc(x.name)+' - '+esc(x.decision)+'</div><div class="sub">Observed '+esc(new Date(x.observedUtc).toLocaleString())+' · lowest '+money(x.lowest)+'</div><div class="sub">ROI ceiling before stress '+money(x.buyCeiling)+' each · target ask '+money(x.sellTarget)+' · '+Number(x.quantity||0).toLocaleString()+' returned cheaper units · cost '+money(x.cost)+' · conditional net '+money(x.estimatedNet)+' ('+Number(x.roi||0).toFixed(1)+'%)</div><div class="sub">'+(x.riskAnalyzed?('Stress-case net '+money(x.stressNet)+' · Break-even resale '+money(x.breakEvenSell)+' each · Cash exposure '+Number(x.exposurePercent||0).toFixed(1)+'% · '):'')+esc(x.reason)+'</div><div class="sub">'+esc(x.trend)+'</div><div class="sub">'+esc(x.sellAdvice)+'</div><button class="btn small" data-trade-item="'+Number(x.itemId)+'">Open item market</button></div>').join('')||'<div class="empty">No observations yet. Run a scan.</div>';
 for(const button of $('tradeAdviceRows').querySelectorAll('[data-trade-item]'))button.onclick=()=>send('tradeOpen',{itemId:Number(button.dataset.tradeItem)});
}
function renderScriptTools(items){
 $('scriptToolsList').replaceChildren();
 for(const item of items||[]){
  const row=document.createElement('div');row.className='minirow';
  const label=document.createElement('span');label.textContent=item.name+' · '+(item.enabled?'Enabled on matching pages':'Disabled');
  const button=document.createElement('button');button.className='btn small';button.textContent='Open helper';
  button.onclick=()=>send('scriptOpen',{name:item.name});
  row.append(label,button);$('scriptToolsList').appendChild(row);
 }
 if(!(items||[]).length)$('scriptToolsList').textContent='No installed helpers. Open Manage Userscripts to install them.';
}
let itemConfigIdentity='',itemProfile='',itemCatalogIdentity='';
function saveItemCollection(){send('itemSettings',{profileId:S.profileId,market:$('iiMarketQuotes').checked,monitor:$('iiMonitor').checked,intel:$('iiIntel').checked,minutes:Number($('iiMinutes').value),retention:Number($('iiRetention').value)})}
function saveIntelKey(client,remove){const field=$(client?'iiClientKey':'iiLoginKey');send('intelCredential',{profileId:S.profileId,client,remove,key:field.value});field.value=''}
function renderItemCatalog(data){const list=(data?.catalog||[]).filter(x=>[x.name,x.id,x.location].join(' ').toLowerCase().includes($('iiSearch').value.toLowerCase())),el=$('iiCatalog'),prior=el.value;el.replaceChildren(...list.map(x=>new Option(x.name+' #'+x.id+' · '+x.location,x.id+'|'+x.location)));if(list.some(x=>x.id+'|'+x.location===prior))el.value=prior;}
function loadItemWatch(){const key=$('iiCatalog').value,w=(S.itemIntelligence?.cards||[]).map(x=>x.watch).find(x=>x.itemId+'|'+x.location===key);$('iiBack').checked=w?.backInStock??true;$('iiForecast').checked=w?.forecastAlerts??false;for(const [id,k] of [['iiBelow','belowQuantity'],['iiAbove','aboveQuantity'],['iiPriceBelow','priceBelow'],['iiPriceAbove','priceAbove']])$(id).value=w?.[k]??'';}
function saveItemWatch(remove){const entry=(S.itemIntelligence?.catalog||[]).find(x=>x.id+'|'+x.location===$('iiCatalog').value);if(!entry)return;const num=id=>$(id).value.trim()===''?null:Number($(id).value);send('itemWatch',{profileId:S.profileId,itemId:entry.id,location:entry.location,name:entry.name,remove,back:$('iiBack').checked,forecast:$('iiForecast').checked,below:num('iiBelow'),above:num('iiAbove'),priceBelow:num('iiPriceBelow'),priceAbove:num('iiPriceAbove')});}
function renderWatchMarket(m){if(!m?.latest)return '<div class="sub">No official market-price sample yet. Enable market sampling or scan this item in Trading.</div>';const q=m.latest;return '<details open><summary>Official item-market asks</summary><div class="sub">Lowest returned ask: '+(q.lowestReturnedAsk==null?'Unknown / no returned offers':money(q.lowestReturnedAsk))+(Date.now()-Date.parse(q.observedUtc)>1800000?' (stale sample)':'')+' · Returned quantity '+q.returnedQuantity+' · Observed '+esc(new Date(q.observedUtc).toLocaleString())+'</div><div class="sub">This is separate from foreign-shop stock and is not completed-sales demand or a complete bazaar comparison.</div>'+insightChart(m.points||[],'at',[{key:'price',label:'Lowest returned ask',color:'#87aaff'}],true,.5)+'</details>';}
function renderItemIntelligence(data,profile){if(!data)return;
 if(itemProfile!==profile){itemProfile=profile;itemConfigIdentity='';itemCatalogIdentity='';$('iiClientKey').value='';$('iiLoginKey').value='';$('iiSearch').value='';}
 const config=JSON.stringify([data.monitor,data.minutes,data.retention,data.useIntel,data.marketQuotes]);if(config!==itemConfigIdentity){itemConfigIdentity=config;$('iiMonitor').checked=!!data.monitor;$('iiMarketQuotes').checked=!!data.marketQuotes;$('iiIntel').checked=!!data.useIntel;$('iiMinutes').value=data.minutes??5;$('iiRetention').value=data.retention??90;}
 const cat=JSON.stringify(data.catalog);if(cat!==itemCatalogIdentity){itemCatalogIdentity=cat;renderItemCatalog(data);loadItemWatch();}
 $('iiStatus').textContent=(data.status||'No history yet')+' · '+(data.count??0)+' saved observations';
 $('iiConnection').textContent='Approved client key: '+(data.clientSaved?'saved locally':'not configured')+' · separate login key: '+(data.loginSaved?'saved locally':'not configured')+'. Stock status and last successful sync are listed in Item Intelligence → Sources.';
 $('iiDatasetSources').innerHTML=(data.diagnostics||[]).map(x=>'<div class="minirow"><b>'+esc(x.dataset)+'</b><div class="sub">'+esc(x.source)+' · '+esc(x.scope)+' · '+esc(x.lastObserved?new Date(x.lastObserved).toLocaleString():'No dataset-wide timestamp')+'</div><div class="sub">'+esc(x.detail)+'</div></div>').join('');
 $('iiHealth').innerHTML=(data.health||[]).map(h=>'<div class="card"><b>'+esc(h.source)+' · '+esc(h.status)+'</b><div class="sub">Last successful fetch: '+esc(h.lastSuccessUtc?new Date(h.lastSuccessUtc).toLocaleString():'never')+' · Next eligible attempt: '+esc(h.nextAttemptUtc?new Date(h.nextAttemptUtc).toLocaleString():'on refresh')+'</div><div class="sub">'+esc(h.detail)+'</div></div>').join('');
 $('iiCards').innerHTML=(data.cards||[]).map(c=>'<div class="card" style="margin-bottom:12px"><div class="section">'+esc(c.watch.name)+' #'+c.watch.itemId+' · '+esc(c.watch.location)+'</div>'+renderWatchMarket(c.market)+(c.providers?.length?c.providers.map(p=>{const x=p.observation,f=p.forecast;return '<details open><summary>'+esc(x.source)+' · '+(p.stale?'STALE':'Observed')+' · '+x.quantity+' available</summary><div class="sub">Reported '+esc(new Date(x.observedUtc).toLocaleString())+' · Collected '+esc(new Date(x.collectedUtc).toLocaleString())+' · Buy '+money(x.cost)+' · Market '+(x.marketValue?money(x.marketValue):'unknown')+'</div><div class="sub">'+esc(f.state)+' · '+esc(f.confidence)+' confidence · '+f.events+' observed restocks · '+f.intervals+' usable intervals</div>'+(f.fromUtc?'<div>'+esc(new Date(f.fromUtc).toLocaleString())+' – '+esc(new Date(f.toUtc).toLocaleString())+'</div>':'')+'<div class="sub">Held-out comparisons: '+f.evaluations+' · midpoint MAE '+(f.meanAbsoluteErrorMinutes??'—')+' min · observed-window overlap '+(f.windowOverlapPercent??'—')+'%</div>'+insightChart(p.points||[],'at',[{key:'quantity',label:'Observed stock',color:'#54d6c1'}],false,0.34)+((p.points||[]).some(v=>v.price!=null)?insightChart(p.points,'at',[{key:'price',label:'Reported market value',color:'#72aaff'}],true,0.34):'<div class="sub">No market-price history from this source.</div>')+'<details><summary>Restock evidence</summary>'+f.history.map(e=>'<div class="sub">Between '+esc(new Date(e.fromUtc).toLocaleString())+' and '+esc(new Date(e.toUtc).toLocaleString())+'</div>').join('')+'</details></details>';}).join(''):'<div class="empty">No timestamped stock reports for this item yet. Refresh or import actual observations. No estimate has been invented.</div>')+'</div>').join('')||'<div class="empty">No watched items. Add one from the catalog.</div>';
}
let lossEditorIdentity='',lossOrdersIdentity='',lossCreateReceipt='';
function createLossOrder(){
 const target=$('lossTarget').value.trim(),requested=Number($('lossCount').value),pay=Number($('lossPrice').value);
 if(!/^\d{1,12}$/.test(target)||Number(target)<=0||!Number.isSafeInteger(requested)||requested<1||requested>1000000||!Number.isSafeInteger(pay)||pay<0||pay>1000000000000||!$('lossAccountA').value){alert('Enter a numeric player ID, positive whole loss count, pay per loss, and Account A.');return;}
 if(!lossCreateReceipt)lossCreateReceipt=Array.from(crypto.getRandomValues(new Uint8Array(16)),v=>v.toString(16).padStart(2,'0')).join('');
 send('lossCreate',{receipt:lossCreateReceipt,profileId:S.profileId,buyer:$('lossBuyer').value,target,requested,pay,accountA:$('lossAccountA').value,accountB:$('lossAccountB').value});
}
function renderLossOrders(d){
 const profiles=d.profiles||[],key=JSON.stringify(profiles);
 if(key!==lossEditorIdentity){lossEditorIdentity=key;for(const id of ['lossAccountA','lossAccountB']){const el=$(id),prior=el.value;el.replaceChildren(...(id==='lossAccountB'?[new Option('Not used — solo order','')]:[]),...profiles.map(p=>new Option(p.name,p.id)));el.value=profiles.some(p=>p.id===prior)?prior:(id==='lossAccountA'?d.profileId:'');}}
 const rows=d.lossOrders||[],identity=JSON.stringify(rows);
 if(identity!==lossOrdersIdentity){lossOrdersIdentity=identity;$('lossOrderSelect').replaceChildren(...(rows.length?rows.map(x=>new Option(x.buyer+' · '+x.completed+'/'+x.requested+' · '+x.status,x.id)):[new Option('No orders yet','')]));}
 $('lossOrderSelect').value=d.loss?.id||'';
 if(lossCreateReceipt&&rows.some(x=>x.id===lossCreateReceipt)){lossCreateReceipt='';$('lossNew').open=false;$('lossBuyer').value='';$('lossTarget').value='';}
}
function renderLossStatus(d){
 if(d.medical)renderSmartMedical(d.medical);if(d.leveling)renderLeveling(d.leveling);const delay=d.lossAttackDelayMs;if(Number.isInteger(delay)&&delay>=500&&delay<=5000&&renderLossStatus.timingValue!==delay&&document.activeElement!==$('lossAttackDelay')){$('lossAttackDelay').value=delay;renderLossStatus.timingValue=delay;}$('workerA').textContent=d.workers?.a||'Not initialized';$('workerB').textContent=d.workers?.b||'Not initialized';$('lossCard').innerHTML=d.loss?`<div class="section">${esc(d.loss.buyer)} · target ${esc(d.loss.target)}</div><div class="metric">${d.loss.completed} / ${d.loss.requested} confirmed losses</div><div class="loss-meter" role="progressbar" aria-label="Confirmed losses" aria-valuemin="0" aria-valuemax="${Number(d.loss.requested)||0}" aria-valuenow="${Number(d.loss.completed)||0}"><span style="width:${Math.max(0,Math.min(100,Number(d.loss.completed)/Math.max(1,Number(d.loss.requested))*100))}%"></span></div><div class="sub">${d.loss.remaining} remaining · ${money(d.loss.gross)} tracked · ${esc(d.loss.status)}</div>`:'<div class="empty">No order selected. Use New order above to get started.</div>';}
function renderSection(name,fn){
 try{fn()}catch(error){console.error(name+' dashboard failed',error);$('dashboardRenderWarning').hidden=false;$('dashboardRenderWarning').textContent=name+' panel could not update. Profile and update controls remain available.';}
}
function render(d){if(!d)return;const previous=S.profileId;S=d;
if(previous&&previous!==d.profileId){casinoAdviceGeneration++;for(const id of ['bjCards','bjDealer','pkHole','pkBoard','pkPot','pkCall','hlCard','evChance','evPayout','evStake'])$(id).value='';$('casinoAdvice').textContent='Profile changed. Enter the current hand.';}
$('dashboardRenderWarning').hidden=true;applyTheme(d.theme||"midnight");$('ver').textContent=d.version||'0.6.69';$('buildBadge').textContent='Build v'+(d.version||'?');$('buildBadge').title=d.buildPath||'';$('actualBuildPath').textContent=d.buildPath||'Unknown';$('profileSelect').replaceChildren(...(d.profiles||[]).map(p=>new Option(p.name,p.id)));$('profileSelect').value=d.profileId;$('profileAdd').disabled=(d.profiles||[]).length>=(d.profileLimit||10);$('profileDelete').disabled=(d.profiles||[]).length<=1;if(document.activeElement!==$('githubRepo'))$('githubRepo').value=d.updateGithubRepo||'';const savedVersions=d.rollbackVersions||[];
const restorePicker=$('rollbackVersion');
const lastSelection=restorePicker.value;
restorePicker.replaceChildren(...(savedVersions.length?savedVersions.map(x=>new Option(x.label,x.version)):[new Option('No local version backups yet','')]));
if(savedVersions.some(x=>x.version===lastSelection))restorePicker.value=lastSelection;
$('restoreVersionBtn').disabled=savedVersions.length===0;
$('rollbackHelp').textContent=savedVersions.length?
  'Saved locally: '+savedVersions.length+' version(s). Your active installation folder uses the version number. Older source ZIPs live outside it in LOCALAPPDATA.' :
  'No rollback history yet. Your first update using v0.6.69 will save the version you are leaving.';
$('profilesSummary').textContent=`${(d.profiles||[]).length} of ${d.profileLimit||10} saved profiles • 1 main browser active • 2 Loss Desk worker slots`;$('profilesList').innerHTML=(d.profiles||[]).map(p=>`<div class="profile-entry ${p.id===d.profileId?'active':''}"><div><b>${esc(p.name)}</b><div class="small">${esc(p.id)}</div></div>${p.id===d.profileId?'<span class="pill good">Active</span>':'<button class="btn small" data-profile-id="'+esc(p.id)+'">Switch</button>'}</div>`).join('');renderApiActivity(d);$('player').textContent=(d.player||d.profile||'Torn Commander')+(d.playerId?' ['+d.playerId+']':'');$('profileMeta').textContent='Profile: '+(d.profile||'—')+' · Level '+(d.level??'—');$('nerve').textContent=(d.nerve?.current??'—')+' / '+(d.nerve?.max??'—');$('nnb').textContent='Estimated natural nerve: '+(d.nerve?.nnb??'unknown');$('energy').textContent=(d.energy?.current??'—')+' / '+(d.energy?.max??'—');$('wallet').textContent=money(d.wallet);$('merits').textContent=(d.merits?.earned??0)+' / '+(d.merits?.total??0);$('meritAvailable').textContent=(d.merits?.available??0)+' points available';$('phase').textContent=d.phase||'Crime Coach';$('headline').textContent=d.headline||'Run Full Sync';$('why').textContent=d.why||'';$('guide').textContent=d.guide?'Recommended: '+d.guide.summary+' · saved Classic Coach mode: '+d.guide.mode+' (dashboard Run Once is always an explicit single-action request)':'No guided action yet';$('graffitiStatus').textContent=d.graffitiStatus||'Idle';$('crimeActionStatus').textContent=d.graffitiStatus||'Idle';$('runGuideButton').disabled=!!d.guideBusy||!d.guide||d.guide.crime!=='Graffiti';$('crimeRunGuideButton').disabled=$('runGuideButton').disabled;$('runGuideButton').title=d.guide?.crime==='Graffiti'?'Attempt one Graffiti Spray after verifying both selections':'One-click action not available for this recommendation';$('cartMini').textContent=(d.cart?.items??0)+' items · '+(d.cart?.remaining??0)+' units';$('lossMini').textContent=d.loss?(d.loss.completed+'/'+d.loss.requested+' · '+d.loss.status):'No active order';$('travelMini').textContent=(d.travel?.rows?.length??0)+' routes';
$('crimeGuide').textContent=d.guide?.summary||d.headline||'Sync to receive advice';$('crimeRows').innerHTML=(d.crimes?.length?d.crimes.map(x=>`<div class="minirow"><div><b>${esc(x.name)}</b><div class="sub">${Number(x.skill).toFixed(1)} skill · ${Number(x.nerve||0).toLocaleString()} nerve</div></div><span class="pill">${x.success}% success</span></div>`).join(''):'<div class="empty">Full Sync to load crimes.</div>');$('meritRows').innerHTML=(d.merits?.rows?.length?d.merits.rows.map(x=>`<div class="minirow"><div><b>${esc(x.name)}</b><div class="sub">${esc(x.advice)}</div></div><span class="pill">${esc(x.progress)}</span></div>`).join(''):'<div class="empty">Full Sync to load merit progress.</div>');$('cartItems').textContent=d.cart?.items??0;$('cartRemain').textContent=Number(d.cart?.remaining??0).toLocaleString();$('cartEstimate').textContent=money(d.cart?.estimate);$('cartLimits').textContent=d.cart?.ignoreLimits?'NUKE MY PILE enabled':'Spending limits active';$('workerA').textContent=d.workers?.a||'Not initialized';$('workerB').textContent=d.workers?.b||'Not initialized';
renderSection('Leveling',()=>renderLeveling(d.leveling));renderLossStatus(d);renderSection('Loss orders',()=>renderLossOrders(d));renderSection('Item intelligence',()=>renderItemIntelligence(d.itemIntelligence,d.profileId));
renderTradeResults(d.trading,d.profileId);$('tradeCount').textContent=d.trading?.entries??0;$('tradeProfit').textContent=money(d.trading?.profit);$('casinoSessions').textContent=d.casino?.sessions??0;$('casinoNet').textContent=money(d.casino?.net);
const t=d.travel||{};for(const [id,value] of [['tCapacity',t.capacity??29],['tFlight',t.flightType||'Airstrip'],['tBudget',t.budget??0],['tHorizon',String(t.horizon??24)],['tConfidence',t.confidence??35]]){if(document.activeElement!==$(id))$(id).value=value;}$('tTax').checked=!!t.marketTax;$('tBook').checked=!!t.book;$('tStockOnly').checked=!!t.stockOnly;$('tLow').checked=!!t.includeLow;$('tAutoMonitor').checked=!!t.monitor;$('tMonitorEvery').value=String(t.monitorMinutes??15);if(document.activeElement!==$('tTransfer'))$('tTransfer').value=t.transferMinutes??10;$('tPlanNow').checked=t.planNow!==false;$('tPauseUntilHome').checked=!!t.pauseUntilHome;$('tCustomStartWrap').style.display=t.planNow===false?'flex':'none';if(t.planCustomStart&&document.activeElement!==$('tPlanStart')){const date=new Date(t.planCustomStart);if(!Number.isNaN(date.getTime())){$('tPlanStart').value=[date.getFullYear(),String(date.getMonth()+1).padStart(2,'0'),String(date.getDate()).padStart(2,'0')].join('-')+'T'+String(date.getHours()).padStart(2,'0')+':'+String(date.getMinutes()).padStart(2,'0')}}renderItinerary(t);renderArrival(t);drawTravelChart(t);
$('travelSource').textContent=(t.source||'Not loaded')+(t.updated?' · Updated '+t.updated:'')+(t.ageMinutes!=null?' · '+t.ageMinutes+' min ago':'');let feedStatus='Ready';let feedMode='Using live feed results.';let warn='Forecasts are estimates, not guaranteed restocks. Reported stock can sell out before arrival.';let warnClass='notice info';if(t.hasError){feedStatus='Partial provider outage';feedMode=t.hasRows?(String(t.source||'').includes('YATA')?'Using YATA backup stock; primary forecast service unavailable.':'Using saved observations or partial live data.'):'No cached travel results are available yet.';warn='Live travel provider issue: '+(t.error||'Unknown error')+' Commander will preserve older results when it can, but you should verify manually before flying.';warnClass='notice';}else if(t.warning||t.ageMinutes>30){feedStatus='Stale data';feedMode='Showing older prices or stock snapshots.';warn='Some prices or stock snapshots are stale. Verify abroad before spending.';warnClass='notice';}$('travelFeedStatus').textContent=feedStatus;$('travelFeedSource').textContent=(t.source||'Not loaded')+(t.updated?' · Updated '+t.updated:'')+(t.ageMinutes!=null?' · '+t.ageMinutes+' min ago':'');$('travelFeedMode').textContent=feedMode;$('travelWarning').textContent=warn;$('travelWarning').className=warnClass;setFilters(t.rows||[]);renderTravelRows();renderTravelWait(d.travel);renderSection('Trading',()=>renderTrading(d.trading?.advice,d.profileId));renderSection('Helpers',()=>renderScriptTools(d.scripts));renderSection('Faction',()=>renderFaction(d.faction,d.profileId));renderSection('War payouts',()=>renderWar(d.warPayout,d.profileId));renderSection('Stocks',()=>renderStocks(d.stocks,d.profileId));renderSection('Company',()=>renderCompany(d.company,d.profileId));renderSection('Progression',()=>renderProgression(d.progression,d.profileId));}
if(window.chrome?.webview){window.chrome.webview.addEventListener('message',e=>{if(e.data?.type==='state')render(e.data.data);else if(e.data?.type==='lossStatus'){Object.assign(S,e.data.data);renderLossStatus(e.data.data)}});send('refresh')}else{$('travelSource').textContent='Preview mode: Commander backend not connected.'}
</script>
</body>
</html>
""".Replace("/*CASINO_COACH*/", CasinoCoachScript.Code);
}
