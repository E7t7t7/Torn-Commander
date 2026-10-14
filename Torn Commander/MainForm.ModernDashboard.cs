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
    private string _lastGraffitiActionStatus = "Idle: use Run Once for one spray, or Open Only to navigate.";

    private Control BuildModernDashboardHost()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        _modernDashboard = new WebView2 { Dock = DockStyle.Fill, BackColor = Bg };
        host.Controls.Add(_modernDashboard);
        _lossWorkerDeck = new TabControl { Dock = DockStyle.Bottom, Height = 350, Visible = false };
        host.Controls.Add(_lossWorkerDeck);
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
                case "companyAuto": SetCompanyAutoCollection(root); break;
                case "companyCopyReport": CopyCompanyReport(); break;
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
                        if (selected is "midnight" or "obsidian" or "emerald" or "violet" or "ember")
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
                case "tradeScan": await ScanTradingAsync(root); break;
                case "tradeOpen": NavigateActive(TornRoutes.ItemMarketFor((int)GoalNumber(root,"itemId",1,int.MaxValue))); break;
                case "cartScan": await ScanShoppingCartAsync(); UpdateModernDashboard(); break;
                case "cartCopy": CopyShoppingCartForChat(); break;
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
            var activeLoss = _lossOrders.FirstOrDefault(x => x.Status != "Complete");
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
            var travel = _travelForecast.Items.Take(140).Select(x => new
            {
                item = x.ItemName,
                country = x.Country,
                category = x.Category,
                stock = x.Stock,
                status = x.StockStatus,
                isStale = x.IsStale || _travelForecast.RetrievedAtUtc < DateTime.UtcNow.AddMinutes(-30),
                units = x.Units,
                cash = x.CashRequired,
                trip = x.TripProfit,
                roi = Math.Round(x.RoiPercent, 1),
                flight = x.FlightMinutesOneWay,
                profit = x.ProfitPerItem,
                profitHr = Math.Round(x.ProfitPerHour),
                restock = x.RestockAtUtc?.ToLocalTime().ToString("ddd h:mm tt"),
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
                    version = "0.6.40",
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
                    company = CompanyDashboardData(),
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
                    trading = new { entries = _trades.Count(x=>x.ProfileId==CurrentProfileId), profit = _trades.Where(x=>x.ProfileId==CurrentProfileId).Sum(x => x.Profit), advice=TradingDashboardData() },
                    scripts = ScriptToolsDashboardData(),
                    casino = new { sessions = _casino.Count, net = _casino.Sum(x => x.ProfitLoss) },
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
                        preview = TravelPreviewEngine.Build(_travelForecast.Items, _settings,
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
                                number = x.Number, country = x.Country, item = x.Item, units = x.Units,
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
                        warning = _travelForecast.Items.Any(x => x.IsStale),
                        error = _travelForecast.Error,
                        hasRows = _travelForecast.Items.Count > 0,
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
.theme-tile[data-choice="ember"]{--sample-bg:#17100e;--sample-panel:#362820;--sample-accent:#ffaa74;--sample-line:#805b49}
@media(max-width:360px){.theme-options{grid-template-columns:repeat(2,minmax(0,1fr))}}
.itin-line-map{margin:10px 0;padding:13px;background:var(--surface,#0c1624);border:1px solid var(--border,#2e4156);border-radius:12px;min-width:0;overflow:hidden}.itin-line-map[hidden]{display:none}.itin-track{display:flex;align-items:center;gap:0;overflow-x:auto;padding:16px 4px 12px;min-width:0;scrollbar-width:thin}.itin-node{appearance:none;background:var(--panel,#132136);border:1px solid var(--border,#36516e);border-radius:9px;color:var(--text,#e0ebf4);padding:8px 10px;flex:0 0 auto;max-width:160px;min-width:75px;font:inherit;text-align:center;cursor:pointer}.itin-node:focus-visible{outline:2px solid var(--accent,#3c8ccd)}.itin-node span{display:block;font-size:10px;color:var(--muted,#aab8c8)}.itin-node.low{border-style:dashed}.itin-line{height:2px;background:var(--accent,#3b83bb);min-width:21px;width:31px;flex:0 0 auto;position:relative}.itin-line:after{content:'';position:absolute;right:0;top:-4px;border-left:6px solid var(--accent,#3b83bb);border-top:5px solid transparent;border-bottom:5px solid transparent}.itin-step{scroll-margin-top:24px}
.adviser-fields{margin:10px 0 15px;align-items:end}.adviser-fields>label{font-size:12px;color:var(--muted)}
.adviser-fields input:not([type=checkbox]),.adviser-fields select,.formgrid input:not([type=checkbox]){background:var(--theme-field,#131e2e);color:var(--text);border:1px solid var(--line2);border-radius:8px;padding:8px;min-height:36px}
.adviser-fields .btn{align-self:end;min-height:36px}.adviser-fields>label.toggle{min-height:36px}
</style>
</head>
<body>
<div class="shell">
<aside class="side" id="navSide" aria-label="Sections">
 <button class="navtoggle" id="navToggle" type="button" aria-expanded="false" aria-controls="navItems">☰ &nbsp; Sections <span id="navCurrent">Overview</span></button>
 <div id="navItems" class="navitems">
 <div class="brand">TORN<br>COMMANDER<small>CONTROL CENTER</small></div>
 <button class="nav active" data-page="dashboard"><i>◈</i>Overview</button>
 <button class="nav" data-page="travel"><i>✈</i>Travel</button>
 <button class="nav" data-page="crimes"><i>◇</i>Crimes</button>
 <button class="nav" data-page="merits"><i>✦</i>Merits</button>
 <button class="nav" data-page="cart"><i>▤</i>Cart</button>
 <button class="nav" data-page="losses"><i>⊞</i>Loss Desk</button>
 <button class="nav" data-page="trading"><i>⇄</i>Trading</button>
 <button class="nav" data-page="company"><i>▦</i>Company</button>
 <button class="nav" data-page="racing"><i>⚑</i>Racing</button>
 <button class="nav" data-page="casino"><i>♠</i>Casino</button>
 <button class="nav" data-page="scripts"><i>⚙</i>Scripts</button>
 <button class="nav" data-page="settings"><i>☷</i>Settings</button>
 <div class="sidefoot">Independent companion • v<span id="ver">0.6.40</span></div>
</div>
</aside>
<div class="content">
 <header class="bar">
  <div class="barleft"><span class="eyebrow">ACTIVE PROFILE</span><select id="profileSelect" class="selectmini" aria-label="Active Torn profile"></select><button class="btn small" id="profileAdd" title="Add a separate browser login" onclick="send('profileAdd')">+ Add</button><button class="btn small" title="Rename selected profile" onclick="send('profileRename')">Rename</button><button class="btn small" id="profileDelete" title="Remove this Commander profile (not the Torn game account)" onclick="send('profileDelete')">Remove</button><span id="apiBadge" class="pill muted">API not connected</span></div>
  <div class="bar-actions"><span id="buildBadge" class="pill muted">Build v0.6.40</span><button class="btn small" id="fastSyncButton" onclick="send('fastSync')">Fast Sync</button><button class="btn small primary" id="fullSyncButton" onclick="send('fullSync')">Full Sync</button></div>
 </header>
 <div class="notice" id="dashboardRenderWarning" hidden role="status"></div>
 <section class="page active" id="dashboard">
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
  <div class="card"><div class="top"><div class="section" style="margin:0">Ranked travel opportunities</div><span class="pill muted" id="resultCount">0 results</span></div><div class="list" id="travelRows"><div class="empty">Refresh to load travel opportunities.</div></div><div class="footerline">Profit estimates assume selling at the quoted market value. Flight times are approximate; standard airfare is included, but the cost of business tickets, purchasing time, rehab fees, market slippage and travel risk are not.</div></div>
 </section>
 <section class="page" id="crimes"><div class="top"><div><div class="eyebrow">Crimes 2.0 / Natural Nerve Bar</div><div class="bigtitle">Crime Coach</div></div><div class="actions"><button class="btn primary" id="crimeRunGuideButton" onclick="send('runGuide')">Guided Action Once</button><button class="btn" onclick="send('openCrimes')">Open Crimes</button><button class="btn" onclick="classic('Crimes')">Crime Statistics</button></div></div><div class="grid"><div class="card span5"><div class="section">Next step</div><div class="notice info" id="crimeGuide">Sync for a recommendation</div><div class="statusline" id="crimeActionStatus" aria-live="polite">Idle</div><div class="actions" style="margin-top:10px"><button class="btn small" onclick="send('graffitiDebug')">Copy Graffiti Debug</button></div></div><div class="card span7"><div class="section">Highest crime skills</div><div class="list" id="crimeRows"></div></div></div></section>
 <section class="page" id="merits"><div class="top"><div><div class="eyebrow">Verified progress only</div><div class="bigtitle">Merit Hunter</div></div><div class="actions"><button class="btn primary" onclick="classic('Merits')">Open Full Merit Hunter</button></div></div><div class="card"><div class="list" id="meritRows"></div></div></section>
 <section class="page" id="cart"><div class="top"><div><div class="eyebrow">Item Market / bulk buying</div><div class="bigtitle">Shopping Cart</div></div><div class="actions"><button class="btn primary" onclick="send('cartScan')">Scan Cart</button><button class="btn" onclick="send('cartCopy')">Copy For Faction Chat</button><button class="btn" onclick="classic('Cart')">Full Cart</button></div></div><div class="grid"><div class="card span4"><div class="eyebrow">Items remaining</div><div class="metric" id="cartItems">—</div></div><div class="card span4"><div class="eyebrow">Units remaining</div><div class="metric" id="cartRemain">—</div></div><div class="card span4"><div class="eyebrow">Market estimate</div><div class="metric s money" id="cartEstimate">—</div><div class="sub" id="cartLimits">—</div></div></div><div class="notice info" style="margin-top:12px">Review each prepared purchase before confirming. Existing limits and the explicit NUKE MY PILE switch remain in Classic Tools.</div></section>
 <section class="page" id="losses"><div class="top"><div><div class="eyebrow">Two independent profile workers</div><div class="bigtitle">Loss Desk</div></div><div class="actions"><button class="btn primary" onclick="classic('Losses')">Full Loss Desk</button><button class="btn" onclick="send('openLossX')">Open Torn</button></div></div><div class="card" id="lossCard">No active order</div><div class="twocol" style="margin-top:11px"><div class="card"><div class="eyebrow">Worker A</div><div class="sub" id="workerA">Not initialized</div></div><div class="card"><div class="eyebrow">Worker B</div><div class="sub" id="workerB">Not initialized</div></div></div><div class="actions" style="margin-top:12px"><button class="btn good" onclick="send('lossStartA')">Start A</button><button class="btn good" onclick="send('lossStartB')">Start B</button><button class="btn good" onclick="send('lossStartBoth')">Start Both</button><button class="btn warn" onclick="send('lossPauseBoth')">Pause Both</button><button class="btn" onclick="send('lossStopBoth')">Stop Both</button></div><div class="notice" style="margin-top:12px">Workers must only proceed on verified Torn page states. Auto actions may conflict with Torn's scripting rules; configure and test safely in the full Loss Desk before starting.</div></section>
 <section class="page" id="trading"><div class="top"><div><div class="eyebrow">Trade ledger & market tools</div><div class="bigtitle">Trading</div></div><div class="actions"><button class="btn primary" onclick="classic('Money')">Open Trading Ledger</button><button class="btn" onclick="classic('Cart')">Open Market Cart</button></div></div><div class="grid"><div class="card span6"><div class="eyebrow">Recorded trades</div><div class="metric" id="tradeCount">—</div></div><div class="card span6"><div class="eyebrow">Ledger net profit</div><div class="metric s money" id="tradeProfit">—</div></div></div><div class="card" style="margin-top:12px"><div class="section">Live trading scanner &amp; adviser</div><div class="sub">Scan up to eight exact item names or IDs. Candidates depend on visible resale gaps; listings do not prove demand or future sale prices.</div>
<div class="formgrid adviser-fields"><label>Items / IDs (up to 8)<input id="tradeItems" placeholder="Beer, First Aid Kit" aria-label="Watchlist item names or IDs"></label><label>Total analysis budget ($)<input id="tradeBudget" type="number" min="0" placeholder="Total budget" aria-label="Total capital budget"></label><label>Keep cash reserve ($)<input id="tradeReserve" type="number" min="0" placeholder="Cash reserve" aria-label="Cash reserve"></label><label>Selling fee (%)<input id="tradeFee" type="number" min="0" max="25" step="0.1" value="5" aria-label="Selling fee percent"></label><label>Minimum net return (%)<input id="tradeRoi" type="number" min="0" max="1000" step="0.1" value="5" aria-label="Minimum return percent"></label><label>Your cost each ($; one item)<input id="tradeCostBasis" type="number" min="0" placeholder="Cost each (one item only)" aria-label="Cost per owned item"></label><button class="btn primary" id="tradeScanButton" onclick="scanTrading()">Scan &amp; advise</button></div>
<div class="sub">Fee %: ordinary Item Market 5%; Bazaar 0%. Set your actual selling fee. Return % is the minimum net return on purchase cost. Cost each is optional for sell advice on one item.</div>
<div id="tradeScanNote" class="notice info">Ready to scan.</div><div id="tradeCapitalNote" class="sub"></div><div id="tradeAdviceRows"></div>
<div class="sub">Quote expiry: 10 minutes. All candidate costs share one capital budget. No purchases or sales are performed. <a href="https://wiki.torn.com/wiki/Item_Market" target="_blank">Market fees</a> / <a href="https://wiki.torn.com/wiki/Bazaar" target="_blank">Bazaar fees</a></div></div></section>
 <section class="page" id="racing"><div class="top"><div><div class="eyebrow">Racing journal and tools</div><div class="bigtitle">Racing</div></div><div class="actions"><button class="btn primary" onclick="classic('Racing')">Racing Tools</button><button class="btn" onclick="send('openRacing')">Open Torn Racing</button></div></div><div class="card"><div class="section">Racing operations</div><div class="sub">Use the classic racing panel to manage your data, tips and current helper scripts. The Torn browser uses the correct page.php route.</div></div></section>
 <section class="page" id="casino"><div class="top"><div><div class="eyebrow">Session-based casino tools</div><div class="bigtitle">Casino</div></div><div class="actions"><button class="btn primary" onclick="classic('Casino')">Open Casino Tools</button></div></div><div class="grid"><div class="card span6"><div class="eyebrow">Tracked sessions</div><div class="metric" id="casinoSessions">—</div></div><div class="card span6"><div class="eyebrow">Recorded net P/L</div><div class="metric s money" id="casinoNet">—</div></div></div><div class="card" style="margin-top:12px"><div class="section">Hand adviser</div><div class="sub">Enter the current hand; press its button for fresh advice. No bets are placed.</div>
<div class="section">Blackjack</div><div class="formgrid adviser-fields"><label>Your blackjack cards<input id="bjCards" placeholder="Your ranks: A 7"></label><label>Dealer up-card<input id="bjDealer" placeholder="Dealer rank: 6"></label><label class="toggle"><input id="bjDouble" type="checkbox" checked>Double available</label><label class="toggle"><input id="bjSplit" type="checkbox" checked>Split available</label><label class="toggle"><input id="bjSurrender" type="checkbox">Surrender available</label><button class="btn" onclick="casinoAdvice('blackjack')">Get move</button><button class="btn" onclick="send('scriptOpen',{name:'Torn_Blackjack_Assist.user.js'})">Open live Blackjack helper</button></div>
<div class="section">Poker</div><div class="sub">Cards include suits: c, d, h, s. Enter the pot before your call and opponents still in the hand.</div><div class="formgrid adviser-fields"><label>Your two poker cards<input id="pkHole" placeholder="Hole: As Kh"></label><label>Community cards<input id="pkBoard" placeholder="Board: Qh Jh 10h"></label><label>Opponents in the hand<input id="pkOpponents" type="number" min="1" max="8" value="1" aria-label="Opponents"></label><label>Pot before your call ($)<input id="pkPot" type="number" min="0" placeholder="Pot before your call"></label><label>Cost to call ($)<input id="pkCall" type="number" min="0" placeholder="Cost to call"></label><button class="btn" onclick="casinoAdvice('poker')">Compare call / fold</button></div>
<div class="section">High-Low</div><div class="formgrid adviser-fields"><label>Current High-Low rank<input id="hlCard" placeholder="Current rank"></label><label>Displayed growth (%)<input id="hlGrowth" type="number" value="15" aria-label="Displayed growth percent"></label><label>Available cash-out<select id="hlCash"><option value="50">50% cash-out available</option><option value="100">100% cash-out available</option></select></label><button class="btn" onclick="casinoAdvice('highlow')">Compare choices</button></div>
<div class="section">Other bets - expected value</div><div class="sub">For roulette, dice or other known payouts, enter the actual chance and net win multiplier.</div><div class="formgrid adviser-fields"><label>Win chance (%)<input id="evChance" type="number" placeholder="Win chance %"></label><label>Net win multiplier<input id="evPayout" type="number" placeholder="Net win multiplier"></label><label>Stake ($)<input id="evStake" type="number" placeholder="Stake"></label><button class="btn" onclick="casinoAdvice('expected')">Evaluate bet</button></div>
<div id="casinoAdvice" class="notice info" role="status">Choose a hand above.</div><div class="sub">Rules: <a href="https://wiki.torn.com/wiki/Black_Jack" target="_blank">Torn Blackjack</a> / <a href="https://wiki.torn.com/wiki/Poker" target="_blank">Poker</a> / <a href="https://wiki.torn.com/wiki/High-Low" target="_blank">High-Low</a></div></div><div class="notice info" style="margin-top:12px">Assistance and session tracking only. No unattended gambling runs are started from this dashboard.</div></section>
 <section class="page" id="scripts"><div class="top"><div><div class="eyebrow">Native userscript engine</div><div class="bigtitle">Scripts & Helpers</div></div><div class="actions"><button class="btn primary" onclick="classic('Tools')">Manage Userscripts</button></div></div><div class="card"><div class="section">Installed assistants</div><div class="sub">Open a helper below to enable it and load its Torn page in Commander. Script menu commands appear in the Commander helpers menu on the game page. Manage Userscripts installs and updates tools.</div><div id="scriptToolsList" class="statusline">Loading helpers…</div></div></section>
 <section class="page" id="company">
  <div class="top">
   <div><div class="eyebrow">Local company intelligence</div><div class="bigtitle">Company Growth Advisor</div><div class="sub">Daily outcomes, advertising experiments and business trends — read-only recommendations</div></div>
   <div class="actions"><button class="btn primary" id="coSync" onclick="send('companySync')">Sync Company</button><button class="btn" id="coCopyReport" onclick="send('companyCopyReport')" title="Copies company and employee stats, costs and recent results for you to share. Excludes API keys and browser sessions.">Copy Company Report</button><button class="btn" onclick="send('openCompany')">Open Torn Company</button><label class="sub"><input type="checkbox" id="coAuto" onchange="send('companyAuto',{enabled:this.checked})"> Daily sync while open</label></div>
  </div>
  <div class="card hero" style="margin-bottom:12px"><div class="eyebrow">Business health from your observations</div><div class="section" id="coBusinessState">No observation</div><div class="sub" id="coBusinessNote">Sync Company to begin.</div><div class="statusline" id="coBusinessMetrics"></div></div>
  <div class="notice info" id="coStatus">No history yet. Sync a saved Torn API key to start daily collection.</div>
  <div class="grid" style="margin-top:12px">
   <div class="card span6"><div class="eyebrow">CURRENT COMPANY</div><div class="metric s" id="coName">No company synced</div><div class="sub" id="coMeta">Star rating and employees appear after sync</div></div>
   <div class="card span3"><div class="eyebrow">REPORTED DAILY INCOME</div><div class="metric s" id="coIncome">—</div><div class="sub">Gross income, not net profit</div></div>
   <div class="card span3"><div class="eyebrow">DAILY CUSTOMERS</div><div class="metric s" id="coCustomers">—</div><div class="sub">API-reported daily customers</div></div>
   <div class="card span4"><div class="eyebrow">DAILY ADVERTISING</div><div class="metric s" id="coAdvertising">—</div><div class="sub">Requires permitted key or manual entry</div></div>
   <div class="card span4"><div class="eyebrow">INCOME AFTER ADS</div><div class="metric s" id="coAfterAds">—</div><div class="sub">Excludes payroll, stock and other expenses</div></div>
   <div class="card span4"><div class="eyebrow">ACCOUNTED MARGIN</div><div class="metric s" id="coNet">—</div><div class="sub">Only when all entered costs are known; not audited profit</div></div>
  </div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">BUSINESS SNAPSHOT</div><div class="section" id="coHealth">Sync Company to load company health.</div><div class="sub" id="coScan">No staff scan yet.</div><div class="notice info" id="coScanWarnings"></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">YOUR NEXT STEPS</div><div id="coActions" class="rows"></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">CHOOSE A PRACTICAL STAFFING LAYOUT</div><div class="section" id="coLayoutStage">Scan Company &amp; Staff first</div><div class="sub" id="coLayoutNote"></div><div class="statusline" id="coLayoutSelected"></div><div id="coLayouts" class="rows" style="margin-top:12px"></div><div id="coLayoutChecks" class="rows" style="margin-top:12px"></div></div>
  <div class="card" style="margin-top:12px"><div class="eyebrow">STAFF POSITION ADVISOR</div><div class="section" id="coTeamState">Not scanned</div><div class="sub" id="coTeamNote"></div><div class="statusline" id="coTeamScore"></div><div id="coTeamRows" class="rows" style="margin-top:12px"></div></div>
  <details class="card" style="margin-top:12px"><summary>Role counts and requirements: try a staffing scenario</summary><div class="sub">Keep the total equal to your staff count. The default preserves current job counts. Changing counts may affect sales and support coverage; scores measure working-stat fit only. A fresh scan resets this local scenario.</div><div id="coJobs" class="rows" style="margin-top:12px"></div><div class="actions"><button class="btn primary" onclick="saveCompanyStaffPlan(false)">Build Staffing Plan</button><button class="btn" onclick="saveCompanyStaffPlan(true)">Use Current Role Counts</button></div></details>
  <details class="card" style="margin-top:12px" open><summary>Stock and pricing checks</summary><div class="sub">Reported stock and order quantities; these are not daily supply costs.</div><div id="coStockRows" class="rows" style="margin-top:12px"></div></details>
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
  <div class="card" style="margin-top:12px"><div class="eyebrow">DAILY HISTORY</div><div class="section">Most recent 30 observations</div><div class="sub">One snapshot per UTC day. Re-syncing the same day updates it; it does not create extra performance samples.</div><div id="coHistory" style="margin-top:12px" class="rows"><div class="empty">No company data yet.</div></div></div>
 </section>
 <section class="page" id="settings"><div class="top"><div><div class="eyebrow">Profiles, privacy and sync</div><div class="bigtitle">Settings</div></div><div class="actions"><button class="btn primary" onclick="classic('Settings')">Open Full Settings</button></div></div><div class="card" id="themeSettingsCard"><div class="eyebrow">Appearance</div><div class="section">Commander color theme</div><div class="sub">Choose a dark palette. Changes preview immediately and save to this Windows installation; account credentials and game pages are unaffected.</div><div class="theme-options" role="group" aria-label="Color theme"><button type="button" class="theme-tile" data-choice="midnight" aria-pressed="true" onclick="chooseTheme('midnight')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Midnight Blue</span><span class="theme-caption">Original Commander</span></button><button type="button" class="theme-tile" data-choice="obsidian" aria-pressed="false" onclick="chooseTheme('obsidian')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Obsidian</span><span class="theme-caption">Graphite &amp; ice</span></button><button type="button" class="theme-tile" data-choice="emerald" aria-pressed="false" onclick="chooseTheme('emerald')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Emerald</span><span class="theme-caption">Forest &amp; mint</span></button><button type="button" class="theme-tile" data-choice="violet" aria-pressed="false" onclick="chooseTheme('violet')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Amethyst</span><span class="theme-caption">Purple &amp; lavender</span></button><button type="button" class="theme-tile" data-choice="ember" aria-pressed="false" onclick="chooseTheme('ember')"><span class="theme-sample"><i></i><i></i></span><span class="theme-name">Ember</span><span class="theme-caption">Copper &amp; charcoal</span></button></div><div class="statusline" id="themeStatus" role="status">Midnight Blue selected. Saved locally.</div></div><div class="card"><div class="section">Manage account profiles</div><div class="sub">Each saved profile has its own Torn login, cookies, and API key. New profiles switch immediately to a clean sign-in. Removing a profile never deletes the actual Torn game account.</div><div class="actions" style="margin-top:12px"><button class="btn primary" onclick="send('profileAdd')">+ Add Profile</button><button class="btn" onclick="send('profileRename')">Rename Selected</button><button class="btn" onclick="send('profileDelete')">Remove Selected</button></div><div class="statusline" style="margin-top:10px" id="profilesSummary">Loading profiles…</div><div class="profile-list" id="profilesList"></div><div class="divider"></div><div class="sub">Up to 10 saved profiles in this build; the main browsing window uses one at a time. Loss Desk still has two simultaneous worker slots, A and B. WebView2 has no fixed documented account count: RAM and active browser processes determine performance. One Torn game account per human player. Deleted profile browser files are retained as a local backup, but its Commander API key is removed.</div><div class="divider"></div><div class="sub">API keys stay in Windows Credential Manager. Travel stock data is public and does not require your Torn API key.</div><div class="divider"></div><div class="eyebrow">ACTUAL EXECUTABLE IN USE</div><div id="actualBuildPath" class="statusline">Waiting for app path</div><button class="btn small" onclick="copyPath()" style="margin-top:9px">Copy running EXE path</button></div><div class="card hero" style="margin-top:12px"><div class="eyebrow">In-app updates</div><div class="section">Update Torn Commander</div><div class="sub">No more merging ZIPs into old folders. Select a trusted Commander release ZIP, or configure a public GitHub Releases repository to check and install newer versions. The app closes and rebuilds; your local account data stays separate.</div><div class="actions" style="margin:12px 0"><button class="btn primary" onclick="send('updateFromZip')">Install from ZIP…</button><button class="btn" onclick="send('checkGithubUpdate')">Check GitHub for Update</button></div><div class="field" style="margin:12px 0"><label for="rollbackVersion">Saved versions (rollback)</label>
<select id="rollbackVersion" aria-label="Select a saved Commander version" style="width:100%;max-width:340px"><option value="">No local version backups yet</option></select></div>
<div class="actions" style="margin-bottom:12px"><button class="btn" id="restoreVersionBtn" onclick="restoreCommander()" disabled>Revert / Switch to Selected Version</button></div>
<div class="statusline" id="rollbackHelp">The updater saves up to five compact source backups on this computer. No additional old app folders are intentionally kept. The currently running version is never listed as a rollback option.</div><div class="field"><label for="githubRepo">Public GitHub repository (owner/repo)</label><input type="text" id="githubRepo" placeholder="your-github-name/TornCommander"></div><button class="btn small" style="margin-top:9px" onclick="send('saveUpdateRepo',{repo:$('githubRepo').value})">Save Repository</button><div class="statusline">GitHub mode requires a published Release containing Torn_Commander_vX.Y.Z.zip. The first upgrade to this updater still requires one manual extraction. Only install releases you trust.</div></div></section>
</div>
</div>
<script>
/*CASINO_COACH*/
'use strict';
let S={};const $=id=>document.getElementById(id);const money=n=>n==null?'—':'$'+Number(n).toLocaleString('en-US',{maximumFractionDigits:0});
const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function send(cmd,extra={}){window.chrome?.webview?.postMessage({cmd,...extra})}
const commanderThemes={midnight:'Midnight Blue',obsidian:'Obsidian',emerald:'Emerald',violet:'Amethyst',ember:'Ember'};
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
function renderItinerary(t){const situation=t.situation||{};const label=({home:'In Torn — ready to depart',returning:'In flight — returning to Torn',outbound:'In flight — outbound',abroad:'Abroad — conditional return plan',unavailable:'Travel temporarily unavailable',unknown:'Location not yet confirmed'})[situation.state]||'Location not yet confirmed';$('travelStatusLabel').textContent=label;$('travelStatusDetails').textContent=(situation.description||'Run Fast Sync or open Torn Travel')+(situation.takeoffAt?' · Took off: '+situation.takeoffAt:'')+(situation.landingAt?' · Landing: '+situation.landingAt:'')+(situation.lastCheckedAt?' · Checked '+situation.lastCheckedAt:'');$('travelStatusBox').className='travel-state '+(situation.state==='home'?'ready':situation.state==='returning'?'flight':'hold');const itin=t.itinerary||{};const legs=itin.legs||[];const previewOnly=situation.state!=="home"&&!legs.length;$('itineraryPeriod').textContent=previewOnly?'Time window begins after your confirmed return to Torn':itin.start&&itin.end?`${itin.start} → ${itin.end}`:'24-hour planning window';$('itinProfit').textContent=legs.length?money(itin.profit):'—';$('itinTrips').textContent=legs.length?String(legs.length):'0';$('itinScore').textContent=legs.length?money(itin.score):'—';$('itinDisclaimer').textContent=(itin.note||'Plans are estimates. Always verify stock before flying.')+(itin.projected?' · '+itin.projected+' speculative leg(s).':'');$('tShowRouteMap').checked=!!t.showRouteMap;renderRouteLine(legs,!!t.showRouteMap);renderTravelPreview(t.preview,t,legs);$('itineraryRows').innerHTML=legs.length?legs.map(x=>`<div id="itin-step-${x.number}" class="itin-step ${x.availability?.startsWith('Modeled')?'itin-forecast':''}"><div class="itin-index">${x.number}</div><div><div class="itin-route">Torn → ${esc(x.country)} → Torn</div><div class="sub">Buy ${Number(x.units).toLocaleString()} × ${esc(x.item)} · ${money(x.profit)} possible net if stock survives · ${money(x.riskAdjusted)} risk-weighted score</div><div class="itin-times"><div><div class="eyebrow">Take off</div><strong>${esc(x.departure)}</strong></div><div><div class="eyebrow">Arrive abroad</div><strong>${esc(x.arrival)}</strong></div><div><div class="eyebrow">Back in Torn</div><strong>${esc(x.back)}</strong></div></div><div class="itin-meta"><span>Cash required <strong>${money(x.cash)}</strong></span><span>${esc(x.availability)} · heuristic confidence <strong>${x.confidence}%</strong></span></div></div></div>`).join(''):'<div class="empty">'+esc(itin.note||'No plan yet. Refresh stock to get started.')+'</div>';const active=!!t.monitor;$('travelNextCheck').textContent=active?(t.refreshing?'Refreshing feeds now…':t.nextPollAt?'Next feed check: '+t.nextPollAt:'Auto-monitor starts when Commander is ready'):'Auto-monitor off — use Save + Refresh manually';}

function setFilters(rows){for(const [id,values,placeholder] of [['tCountry',new Set(rows.map(x=>x.country).filter(Boolean)),'All countries'],['tCategory',new Set(rows.map(x=>x.category).filter(Boolean)),'All categories']]){const select=$(id),last=select.value;select.replaceChildren(new Option(placeholder,''));[...values].sort((a,b)=>a.localeCompare(b)).forEach(v=>select.add(new Option(v,v)));select.value=[...select.options].some(o=>o.value===last)?last:'';}}
function badge(x){if(x.isStale||x.status==='Stale')return '<span class="pill bad">Stale data</span>';if(x.status==='In stock')return '<span class="pill good">In stock*</span>';if(x.status==='Predicted')return '<span class="pill warn">Predicted '+esc(x.confidence)+'</span>';return '<span class="pill muted">Stock unverified</span>'}
function renderTravelRows(){const t=S.travel||{},rows=t.rows||[];const q=$('tSearch').value.trim().toLowerCase(),country=$('tCountry').value,cat=$('tCategory').value,status=$('tStatus').value;const filtered=rows.filter(x=>(!q||[x.item,x.country,x.category].join(' ').toLowerCase().includes(q))&&(!country||x.country===country)&&(!cat||x.category===cat)&&(!status||(status==='Stale'?(x.isStale||x.status==='Unverified'):x.status===status&&!x.isStale)));const priced=filtered.filter(x=>x.priceKnown&&Number(x.profitHr)>0);const best=priced.length?priced.reduce((a,b)=>b.profitHr>a.profitHr?b:a):null;$('resultCount').textContent=filtered.length+' of '+rows.length+' results';$('travelCount').textContent=rows.length.toLocaleString();$('travelCountDetail').textContent=rows.filter(x=>x.status==='In stock'&&!x.isStale).length+' in stock • '+rows.filter(x=>!x.priceKnown).length+' missing market price • '+rows.filter(x=>x.status==='Predicted').length+' predicted';$('bestHour').textContent=best&&best.priceKnown?money(best.profitHr)+'/hr':'—';const biggest=priced.reduce((a,b)=>(!a||b.trip>a.trip)?b:a,null);$('bestTrip').textContent=biggest?money(biggest.trip):'—';$('bestTripItem').textContent=biggest?(biggest.item+' · '+biggest.country):'No matching route';
const emptyMsg=t.hasError&&!rows.length?'The live travel provider is unavailable and Commander has no saved stock history yet. Refresh again later, or open Torn Travel Agency and build local observations over time.':t.hasError?'No routes match the current filters. Live provider data is unavailable, so Commander is only using cached/local observations right now.':(!rows.length?'No travel data yet. Press Save + Refresh to load opportunities.':'No matching viable travel opportunities. Try a different filter, budget, flight method, or refresh the feed.');$('travelRows').innerHTML=filtered.length?filtered.slice(0,70).map(x=>`<article class="opportunity"><div class="tleft"><div class="name">${esc(x.item)}</div><div class="sub">${esc(x.country)} · ${esc(x.category||'Item')} · ${esc(x.source)}</div><div style="margin-top:7px">${badge(x)}</div></div><div class="right"><div class="moneybig money">${x.priceKnown?money(x.profitHr)+'/hr':'Price unknown'}</div><div class="sub">${x.priceKnown?money(x.trip)+' net / trip · '+Number(x.roi).toFixed(1)+'% ROI':'No market valuation — stock only'}</div></div><div class="details"><span><strong>${Number(x.units||0).toLocaleString()}</strong> units planned</span><span>Cash <strong>${money(x.cash)}</strong></span><span>${x.flight}m flight each way</span><span>Stock <strong>${x.stock==null?'unknown':Number(x.stock).toLocaleString()}</strong></span>${x.stockAgeMinutes!=null?`<span>Stock age <strong>${x.stockAgeMinutes}m</strong></span>`:''}${x.priceAgeMinutes!=null?`<span>Valuation retrieved <strong>${x.priceAgeMinutes}m ago</strong></span>`:''}${x.restock?`<span>Restock est. <strong>${esc(x.restock)}</strong></span>`:''}${x.leave?`<span>Fly out ~ <strong>${esc(x.leave)}</strong></span>`:''}${x.note?`<span title="${esc(x.note)}">${esc(x.note)}</span>`:''}</div></article>`).join('')+(filtered.length>70?`<div class="statusline">Showing first 70 matching routes. Use filters to narrow the list.</div>`:''):`<div class="empty">${esc(emptyMsg)}</div>`;}
['tSearch','tCountry','tCategory','tStatus'].forEach(id=>$(id).addEventListener(id==='tSearch'?'input':'change',renderTravelRows));

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
function renderCompany(c,profile){
 c=c||{};
 $('coStatus').textContent=c.status||'Company history not loaded.';
 $('coName').textContent=c.name||'No company synced';
 $('coMeta').textContent=(c.type||'')+' · Stars: '+(c.rating??'—')+' · Employees: '+(c.staff??'—');
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
 $('coLayouts').innerHTML=(layouts.options||[]).map(x=>'<div class="minirow" style="display:block"><b>'+esc(x.name)+(x.suggested?' · Suggested starting trial':'')+'</b><div class="sub">'+esc(x.reason)+'</div><div>'+esc(x.countsText)+'</div><div class="sub">Combined working-stat fit '+known(x.workingScore)+' · '+known(x.underqualified)+' below recommended stats.'+(x.trainerWorkingFit==null?'':' Trainer working-stat fit: '+known(x.trainerWorkingFit)+'.')+' This is not an income estimate.</div><button class="btn primary" data-company-layout="'+esc(x.id)+'" onclick="previewCompanyLayout(this.dataset.companyLayout)"'+(c.syncing?' disabled':'')+'>Show Named Assignments</button></div>').join('')||'<div class="empty">No guided layout available for this scan. The custom role-count builder remains available below.</div>';
 $('coLayoutChecks').innerHTML=(layouts.checks||[]).map(x=>'<div class="minirow"><div class="sub">'+esc(x)+'</div></div>').join('');
 $('coTeamState').textContent=team.state||'NOT SCANNED';
 $('coTeamNote').textContent=team.note||'Sync Company to scan employees and their job requirements.';
 $('coTeamScore').textContent=team.before==null?'A complete staff list, working stats and current-role requirements are needed for a team allocation.':'Combined modeled working-stat effectiveness: '+known(team.before)+' → '+known(team.after)+' ('+(team.after>=team.before?'+':'')+known(team.after-team.before)+'). Scores exclude other modifiers.';
 const staffIdentity=String(profile)+'|'+String(c.id||0)+'|'+JSON.stringify(team.rows||[]);
 if(staffIdentity!==coStaffIdentity){
  coStaffIdentity=staffIdentity;
  $('coTeamRows').innerHTML=(team.rows||[]).map(x=>{
   const explanation=x.explanation||{};
   return '<div class="minirow" style="display:block"><b>'+esc(x.name)+' ['+esc(x.id)+']</b><div>'+esc(x.current)+' → '+esc(x.recommended)+'</div><div class="sub">'+esc(stats(x.stats))+'</div><div class="sub">Modeled work-stat score '+known(x.before)+' → '+known(x.after)+'</div><div class="sub">'+esc(x.nextStep)+'</div>'+
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
 $('coStockRows').innerHTML=(scan.stock||[]).map(x=>'<div class="minirow" style="display:block"><b>'+esc(x.name)+'</b><div class="sub">In stock '+known(x.inStock)+' · On order '+known(x.onOrder)+' · Unit cost '+(x.cost==null?'Unknown':money(x.cost))+' · Selling price '+(x.price==null?'Unknown':money(x.price))+'</div></div>').join('')||'<div class="empty">'+(scan.stockLoaded?'No stocked products returned for this company.':'Stock details unavailable. Check company stock API permission.')+'</div>';
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
 $('progressionMetrics').textContent=(r.day||'Today')+' · Recorded trading margin '+money(r.recordedMargin||0)+' / '+money(r.target||0)+
  ' across '+Number(r.trades||0)+' entries (fees excluded) · Wallet above reserve: '+(r.spendableCash==null?'needs sync':money(r.spendableCash))+
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
 const request={items:$('tradeItems').value,budget:value('tradeBudget'),reserve:value('tradeReserve'),fee:value('tradeFee'),roi:value('tradeRoi'),costBasis:value('tradeCostBasis')};
 if(!request.items.trim()||Object.values(request).some(x=>typeof x==='number'&&!Number.isFinite(x))){alert('Enter item names and valid numeric limits.');return;}
 send('tradeScan',request);
}
function renderTrading(r,profile){
 if(!r)return;
 const identity=profile+'|'+r.settings;
 if(tradeFormIdentity!==identity){tradeFormIdentity=identity;for(const [id,value] of [['tradeItems',r.items],['tradeBudget',r.budget],['tradeReserve',r.reserve],['tradeFee',r.fee],['tradeRoi',r.roi],['tradeCostBasis',r.costBasis]])$(id).value=value;}
 $('tradeScanButton').disabled=!!r.scanning;$('tradeScanNote').textContent=r.note||'Ready';$('tradeCapitalNote').textContent=r.capitalNote||'';
 $('tradeAdviceRows').innerHTML=(r.rows||[]).map(x=>'<div class="card" style="margin-top:10px"><div class="section">'+esc(x.name)+' - '+esc(x.decision)+'</div><div class="sub">Observed '+esc(new Date(x.observedUtc).toLocaleString())+' · lowest '+money(x.lowest)+'</div><div class="sub">Buy ceiling '+money(x.buyCeiling)+' each · target ask '+money(x.sellTarget)+' · '+Number(x.quantity||0).toLocaleString()+' returned cheaper units · cost '+money(x.cost)+' · conditional net '+money(x.estimatedNet)+' ('+Number(x.roi||0).toFixed(1)+'%)</div><div class="sub">'+esc(x.reason)+'</div><div class="sub">'+esc(x.trend)+'</div><div class="sub">'+esc(x.sellAdvice)+'</div><button class="btn small" data-trade-item="'+Number(x.itemId)+'">Open item market</button></div>').join('')||'<div class="empty">No observations yet. Run a scan.</div>';
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
function renderLossStatus(d){$('workerA').textContent=d.workers?.a||'Not initialized';$('workerB').textContent=d.workers?.b||'Not initialized';$('lossCard').innerHTML=d.loss?`<div class="section">${esc(d.loss.buyer)} · target ${esc(d.loss.target)}</div><div class="metric">${d.loss.completed} / ${d.loss.requested} confirmed losses</div><div class="sub">${d.loss.remaining} remaining · ${money(d.loss.gross)} tracked · ${esc(d.loss.status)}</div>`:'<div class="empty">No active loss order. Create and configure it in the Full Loss Desk.</div>';}
function renderSection(name,fn){
 try{fn()}catch(error){console.error(name+' dashboard failed',error);$('dashboardRenderWarning').hidden=false;$('dashboardRenderWarning').textContent=name+' panel could not update. Profile and update controls remain available.';}
}
function render(d){if(!d)return;const previous=S.profileId;S=d;
if(previous&&previous!==d.profileId){casinoAdviceGeneration++;for(const id of ['bjCards','bjDealer','pkHole','pkBoard','pkPot','pkCall','hlCard','evChance','evPayout','evStake'])$(id).value='';$('casinoAdvice').textContent='Profile changed. Enter the current hand.';}
$('dashboardRenderWarning').hidden=true;applyTheme(d.theme||"midnight");$('ver').textContent=d.version||'0.6.40';$('buildBadge').textContent='Build v'+(d.version||'?');$('buildBadge').title=d.buildPath||'';$('actualBuildPath').textContent=d.buildPath||'Unknown';$('profileSelect').replaceChildren(...(d.profiles||[]).map(p=>new Option(p.name,p.id)));$('profileSelect').value=d.profileId;$('profileAdd').disabled=(d.profiles||[]).length>=(d.profileLimit||10);$('profileDelete').disabled=(d.profiles||[]).length<=1;if(document.activeElement!==$('githubRepo'))$('githubRepo').value=d.updateGithubRepo||'';const savedVersions=d.rollbackVersions||[];
const restorePicker=$('rollbackVersion');
const lastSelection=restorePicker.value;
restorePicker.replaceChildren(...(savedVersions.length?savedVersions.map(x=>new Option(x.label,x.version)):[new Option('No local version backups yet','')]));
if(savedVersions.some(x=>x.version===lastSelection))restorePicker.value=lastSelection;
$('restoreVersionBtn').disabled=savedVersions.length===0;
$('rollbackHelp').textContent=savedVersions.length?
  'Saved locally: '+savedVersions.length+' version(s). Your active installation folder uses the version number. Older source ZIPs live outside it in LOCALAPPDATA.' :
  'No rollback history yet. Your first update using v0.6.40 will save the version you are leaving.';
$('profilesSummary').textContent=`${(d.profiles||[]).length} of ${d.profileLimit||10} saved profiles • 1 main browser active • 2 Loss Desk worker slots`;$('profilesList').innerHTML=(d.profiles||[]).map(p=>`<div class="profile-entry ${p.id===d.profileId?'active':''}"><div><b>${esc(p.name)}</b><div class="small">${esc(p.id)}</div></div>${p.id===d.profileId?'<span class="pill good">Active</span>':'<button class="btn small" data-profile-id="'+esc(p.id)+'">Switch</button>'}</div>`).join('');renderApiActivity(d);$('player').textContent=(d.player||d.profile||'Torn Commander')+(d.playerId?' ['+d.playerId+']':'');$('profileMeta').textContent='Profile: '+(d.profile||'—')+' · Level '+(d.level??'—');$('nerve').textContent=(d.nerve?.current??'—')+' / '+(d.nerve?.max??'—');$('nnb').textContent='Estimated natural nerve: '+(d.nerve?.nnb??'unknown');$('energy').textContent=(d.energy?.current??'—')+' / '+(d.energy?.max??'—');$('wallet').textContent=money(d.wallet);$('merits').textContent=(d.merits?.earned??0)+' / '+(d.merits?.total??0);$('meritAvailable').textContent=(d.merits?.available??0)+' points available';$('phase').textContent=d.phase||'Crime Coach';$('headline').textContent=d.headline||'Run Full Sync';$('why').textContent=d.why||'';$('guide').textContent=d.guide?'Recommended: '+d.guide.summary+' · saved Classic Coach mode: '+d.guide.mode+' (dashboard Run Once is always an explicit single-action request)':'No guided action yet';$('graffitiStatus').textContent=d.graffitiStatus||'Idle';$('crimeActionStatus').textContent=d.graffitiStatus||'Idle';$('runGuideButton').disabled=!!d.guideBusy||!d.guide||d.guide.crime!=='Graffiti';$('crimeRunGuideButton').disabled=$('runGuideButton').disabled;$('runGuideButton').title=d.guide?.crime==='Graffiti'?'Attempt one Graffiti Spray after verifying both selections':'One-click action not available for this recommendation';$('cartMini').textContent=(d.cart?.items??0)+' items · '+(d.cart?.remaining??0)+' units';$('lossMini').textContent=d.loss?(d.loss.completed+'/'+d.loss.requested+' · '+d.loss.status):'No active order';$('travelMini').textContent=(d.travel?.rows?.length??0)+' routes';
$('crimeGuide').textContent=d.guide?.summary||d.headline||'Sync to receive advice';$('crimeRows').innerHTML=(d.crimes?.length?d.crimes.map(x=>`<div class="minirow"><div><b>${esc(x.name)}</b><div class="sub">${Number(x.skill).toFixed(1)} skill · ${Number(x.nerve||0).toLocaleString()} nerve</div></div><span class="pill">${x.success}% success</span></div>`).join(''):'<div class="empty">Full Sync to load crimes.</div>');$('meritRows').innerHTML=(d.merits?.rows?.length?d.merits.rows.map(x=>`<div class="minirow"><div><b>${esc(x.name)}</b><div class="sub">${esc(x.advice)}</div></div><span class="pill">${esc(x.progress)}</span></div>`).join(''):'<div class="empty">Full Sync to load merit progress.</div>');$('cartItems').textContent=d.cart?.items??0;$('cartRemain').textContent=Number(d.cart?.remaining??0).toLocaleString();$('cartEstimate').textContent=money(d.cart?.estimate);$('cartLimits').textContent=d.cart?.ignoreLimits?'NUKE MY PILE enabled':'Spending limits active';$('workerA').textContent=d.workers?.a||'Not initialized';$('workerB').textContent=d.workers?.b||'Not initialized';
renderLossStatus(d);
$('tradeCount').textContent=d.trading?.entries??0;$('tradeProfit').textContent=money(d.trading?.profit);$('casinoSessions').textContent=d.casino?.sessions??0;$('casinoNet').textContent=money(d.casino?.net);
const t=d.travel||{};for(const [id,value] of [['tCapacity',t.capacity??29],['tFlight',t.flightType||'Airstrip'],['tBudget',t.budget??0],['tHorizon',String(t.horizon??24)],['tConfidence',t.confidence??35]]){if(document.activeElement!==$(id))$(id).value=value;}$('tTax').checked=!!t.marketTax;$('tBook').checked=!!t.book;$('tStockOnly').checked=!!t.stockOnly;$('tLow').checked=!!t.includeLow;$('tAutoMonitor').checked=!!t.monitor;$('tMonitorEvery').value=String(t.monitorMinutes??15);if(document.activeElement!==$('tTransfer'))$('tTransfer').value=t.transferMinutes??10;$('tPlanNow').checked=t.planNow!==false;$('tPauseUntilHome').checked=!!t.pauseUntilHome;$('tCustomStartWrap').style.display=t.planNow===false?'flex':'none';if(t.planCustomStart&&document.activeElement!==$('tPlanStart')){const date=new Date(t.planCustomStart);if(!Number.isNaN(date.getTime())){$('tPlanStart').value=[date.getFullYear(),String(date.getMonth()+1).padStart(2,'0'),String(date.getDate()).padStart(2,'0')].join('-')+'T'+String(date.getHours()).padStart(2,'0')+':'+String(date.getMinutes()).padStart(2,'0')}}renderItinerary(t);renderArrival(t);
$('travelSource').textContent=(t.source||'Not loaded')+(t.updated?' · Updated '+t.updated:'')+(t.ageMinutes!=null?' · '+t.ageMinutes+' min ago':'');let feedStatus='Ready';let feedMode='Using live feed results.';let warn='Forecasts are estimates, not guaranteed restocks. Reported stock can sell out before arrival.';let warnClass='notice info';if(t.hasError){feedStatus='Partial provider outage';feedMode=t.hasRows?(String(t.source||'').includes('YATA')?'Using YATA backup stock; primary forecast service unavailable.':'Using saved observations or partial live data.'):'No cached travel results are available yet.';warn='Live travel provider issue: '+(t.error||'Unknown error')+' Commander will preserve older results when it can, but you should verify manually before flying.';warnClass='notice';}else if(t.warning||t.ageMinutes>30){feedStatus='Stale data';feedMode='Showing older prices or stock snapshots.';warn='Some prices or stock snapshots are stale. Verify abroad before spending.';warnClass='notice';}$('travelFeedStatus').textContent=feedStatus;$('travelFeedSource').textContent=(t.source||'Not loaded')+(t.updated?' · Updated '+t.updated:'')+(t.ageMinutes!=null?' · '+t.ageMinutes+' min ago':'');$('travelFeedMode').textContent=feedMode;$('travelWarning').textContent=warn;$('travelWarning').className=warnClass;setFilters(t.rows||[]);renderTravelRows();renderSection('Trading',()=>renderTrading(d.trading?.advice,d.profileId));renderSection('Helpers',()=>renderScriptTools(d.scripts));renderSection('Company',()=>renderCompany(d.company,d.profileId));renderSection('Progression',()=>renderProgression(d.progression,d.profileId));}
if(window.chrome?.webview){window.chrome.webview.addEventListener('message',e=>{if(e.data?.type==='state')render(e.data.data);else if(e.data?.type==='lossStatus'){Object.assign(S,e.data.data);renderLossStatus(e.data.data)}});send('refresh')}else{$('travelSource').textContent='Preview mode: Commander backend not connected.'}
</script>
</body>
</html>
""".Replace("/*CASINO_COACH*/", CasinoCoachScript.Code);
}
