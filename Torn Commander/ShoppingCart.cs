using System.Text.Json;
using System.Text.RegularExpressions;

namespace TornCommander;

public sealed partial class MainForm
{
    private readonly List<ShoppingTarget> _shoppingTargets = Storage.Load(Storage.ShoppingCartPath, new List<ShoppingTarget>());
    private readonly Dictionary<int, MarketScanResult> _shoppingScans = new();
    private readonly List<MarketCatalogItem> _shoppingCatalog = new();

    private DataGridView _shoppingGrid = null!;
    private Label _shoppingSummary = null!;
    private ComboBox _shoppingItemName = null!;
    private NumericUpDown _shoppingItemQty = null!;
    private NumericUpDown _paintRestockQty = null!;
    private NumericUpDown _shoppingMaxUnitPrice = null!;
    private NumericUpDown _shoppingMaxSpend = null!;
    private NumericUpDown _shoppingReserve = null!;
    private CheckBox _shoppingIgnoreLimits = null!;
    private Label _shoppingLimitExplainer = null!;

    private ShoppingTarget? _lastCartPurchaseTarget;
    private int _lastCartPurchaseQty;
    private long _lastCartPurchaseCost;
    private int _cartCommanderPurchaseItemId;
    private DateTime _cartSuppressBrowserPurchaseUntil = DateTime.MinValue;

    private Control BuildShoppingCart()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("SHOPPING CART / BULK BUY"));
        p.Controls.Add(Muted("Build a cart from Torn's live Item Market data. Commander can prepare the next purchase or submit exactly one market purchase per button press. It will never loop purchases in the background."));

        p.Controls.Add(Heading("QUICK PRESET — GRAFFITI PAINT"));
        var paintRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        paintRow.Controls.Add(new Label { Text = "Each color:", AutoSize = true, ForeColor = TextMain, Margin = new Padding(3, 8, 3, 3) });
        _paintRestockQty = Num(1000, 1, 1_000_000);
        _paintRestockQty.Width = 105;
        paintRow.Controls.Add(_paintRestockQty);
        paintRow.Controls.Add(MakeButton("Load paint cart", (_, _) => LoadPaintCartPreset(), 120));
        p.Controls.Add(paintRow);
        p.Controls.Add(Muted("Adds Black, Blue, Green, Orange, Pink, Purple, Red and White Spray Paint. You can type short names such as “green paint” or “purple”; exact punctuation is no longer required."));

        p.Controls.Add(Heading("ADD ANY ITEM"));
        var addRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };

        _shoppingItemName = new ComboBox
        {
            Width = 285,
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.CustomSource,
            BackColor = FieldBg,
            ForeColor = TextMain,
            FlatStyle = FlatStyle.Flat
        };
        _shoppingItemName.DropDown += async (_, _) => await EnsureShoppingCatalogAsync(false);

        _shoppingItemQty = Num(100, 1, 1_000_000);
        _shoppingItemQty.Width = 100;

        addRow.Controls.Add(_shoppingItemName);
        addRow.Controls.Add(_shoppingItemQty);
        addRow.Controls.Add(MakeButton("Add / Find", async (_, _) => await AddShoppingTargetAsync(), 92));
        addRow.Controls.Add(MakeButton("Load item list", async (_, _) => await EnsureShoppingCatalogAsync(true), 102));
        addRow.Controls.Add(MakeButton("Remove", (_, _) => RemoveSelectedShoppingTarget(), 72));
        p.Controls.Add(addRow);
        p.Controls.Add(Muted("Paste or type a partial name. Commander resolves against Torn's live item catalog and ignores punctuation/case. If a pasted row contains extra columns, it tries to extract the actual item name instead of treating the whole paste as the name."));

        p.Controls.Add(Heading("BUY LIMITS"));
        _shoppingLimitExplainer = Muted("These are Commander safety limits only. They do not change Torn's market. Set a value to $0 for unlimited, or enable NUKE MY PILE to ignore all Commander spending limits.");
        p.Controls.Add(_shoppingLimitExplainer);

        var limits = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Width = 620,
            BackColor = PanelBg,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        _shoppingMaxUnitPrice = MoneyNum();
        _shoppingMaxSpend = MoneyNum();
        _shoppingReserve = MoneyNum();

        _shoppingMaxUnitPrice.Value = Math.Clamp((decimal)_settings.ShoppingMaxUnitPrice, _shoppingMaxUnitPrice.Minimum, _shoppingMaxUnitPrice.Maximum);
        _shoppingMaxSpend.Value = Math.Clamp((decimal)_settings.ShoppingMaxSpend, _shoppingMaxSpend.Minimum, _shoppingMaxSpend.Maximum);
        _shoppingReserve.Value = Math.Clamp((decimal)_settings.ShoppingCashReserve, _shoppingReserve.Minimum, _shoppingReserve.Maximum);

        AddLabeled(limits, "Max price per unit ($0 = unlimited)", _shoppingMaxUnitPrice);
        AddLabeled(limits, "Max total cart spend ($0 = unlimited)", _shoppingMaxSpend);
        AddLabeled(limits, "Minimum wallet to keep ($0 = spend to $0)", _shoppingReserve);
        p.Controls.Add(limits);

        _shoppingIgnoreLimits = DarkCheck("NUKE MY PILE — ignore ALL Commander price/spend/reserve limits for this cart", _settings.ShoppingIgnoreLimits);
        _shoppingIgnoreLimits.ForeColor = Color.FromArgb(255, 196, 80);
        _shoppingIgnoreLimits.CheckedChanged += (_, _) =>
        {
            if (_shoppingIgnoreLimits.Checked && !_settings.ShoppingIgnoreLimits)
            {
                var result = MessageBox.Show(
                    "NUKE MY PILE disables Commander's max-unit-price, max-cart-spend and cash-reserve safeguards.\n\nIt does NOT auto-buy the whole cart and it does not bypass Torn. One Buy Next press still submits at most one market purchase.\n\nEnable it?",
                    "Shopping Cart — ignore limits",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    _shoppingIgnoreLimits.Checked = false;
                    return;
                }
            }

            SaveShoppingLimitSettings();
            RebindShoppingCart();
        };
        p.Controls.Add(_shoppingIgnoreLimits);

        void SaveLimitValue(object? _, EventArgs __)
        {
            SaveShoppingLimitSettings();
            RebindShoppingCart();
        }

        _shoppingMaxUnitPrice.ValueChanged += SaveLimitValue;
        _shoppingMaxSpend.ValueChanged += SaveLimitValue;
        _shoppingReserve.ValueChanged += SaveLimitValue;

        var actions = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        actions.Controls.Add(MakeButton("Scan / Build Cart", async (_, _) => await ScanShoppingCartAsync(), 125));
        actions.Controls.Add(MakeButton("Prepare Next", async (_, _) => await PrepareNextCartPurchaseAsync(false), 100));
        actions.Controls.Add(MakeButton("Buy Next Once", async (_, _) => await PrepareNextCartPurchaseAsync(true), 108));
        actions.Controls.Add(MakeButton("Record selected bought", (_, _) => RecordSelectedBoughtFallback(), 142));
        actions.Controls.Add(MakeButton("Copy cart for chat", (_, _) => CopyShoppingCartForChat(), 118));
        actions.Controls.Add(MakeButton("Undo last", (_, _) => UndoLastCartPurchase(), 82));
        actions.Controls.Add(MakeButton("Market debug", async (_, _) => await CopyMarketDebugAsync(), 96));
        p.Controls.Add(actions);

        _shoppingSummary = BigLabel("Load a preset or add an item, then Scan / Build Cart.");
        p.Controls.Add(_shoppingSummary);

        _shoppingGrid = MakeGrid(620, 430);
        _shoppingGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _shoppingGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _shoppingGrid.MultiSelect = false;
        _shoppingGrid.CellDoubleClick += (_, _) =>
        {
            var target = SelectedShoppingTarget();
            if (target is not null) _shoppingItemName.Text = target.ItemName;
        };
        p.Controls.Add(_shoppingGrid);

        p.Controls.Add(Muted("Prepare Next opens the correct market item, opens the matching buy dialog and prefills the planned quantity without confirming. Buy Next Once does the same and submits one purchase. Manual purchases made in Torn Commander are auto-detected when possible; “Record selected bought” is the fallback if Torn changes its UI."));
        p.Controls.Add(Muted("Important: Torn's inventory API is cached by category, so Commander tracks purchases from the live browser instead of pretending an inventory API refresh is instant."));

        RebindShoppingCart();
        return p;
    }

    private void SaveShoppingLimitSettings()
    {
        if (_shoppingMaxUnitPrice is null || _shoppingMaxSpend is null || _shoppingReserve is null || _shoppingIgnoreLimits is null)
            return;

        _settings.ShoppingMaxUnitPrice = (long)_shoppingMaxUnitPrice.Value;
        _settings.ShoppingMaxSpend = (long)_shoppingMaxSpend.Value;
        _settings.ShoppingCashReserve = (long)_shoppingReserve.Value;
        _settings.ShoppingIgnoreLimits = _shoppingIgnoreLimits.Checked;
        Storage.SaveSettings(_settings);
    }

    private void LoadPaintCartPreset()
    {
        var qty = (int)_paintRestockQty.Value;
        var colors = new[] { "Black", "Blue", "Green", "Orange", "Pink", "Purple", "Red", "White" };

        _shoppingTargets.RemoveAll(x => NormalizeItemText(x.ItemName).Contains("spraypaint", StringComparison.OrdinalIgnoreCase));
        foreach (var color in colors)
            _shoppingTargets.Add(new ShoppingTarget { ItemName = $"Spray Paint : {color}", DesiredQuantity = qty });

        _shoppingScans.Clear();
        SaveShoppingCart();
        RebindShoppingCart();
        FlashStatus($"Paint cart loaded: {qty:N0} of each color.");
    }

    private async Task EnsureShoppingCatalogAsync(bool showStatus)
    {
        if (_shoppingCatalog.Count > 0)
        {
            PopulateShoppingItemPicker();
            if (showStatus) FlashStatus($"{_shoppingCatalog.Count:N0} Torn items loaded for search.");
            return;
        }

        var key = CredentialManager.LoadApiKey(CurrentProfileId);
        if (string.IsNullOrWhiteSpace(key))
        {
            if (showStatus) MessageBox.Show("Save your Torn API key in Settings first.", "Shopping Cart");
            return;
        }

        try
        {
            if (showStatus) FlashStatus("Loading Torn item catalog…");
            var catalog = await _api.FetchItemCatalogAsync(key);
            _shoppingCatalog.Clear();
            _shoppingCatalog.AddRange(catalog);
            PopulateShoppingItemPicker();
            if (showStatus) FlashStatus($"{_shoppingCatalog.Count:N0} Torn items loaded. Type any part of a name.");
        }
        catch (Exception ex)
        {
            if (showStatus) MessageBox.Show(ex.Message, "Shopping Cart item catalog");
        }
    }

    private void PopulateShoppingItemPicker()
    {
        if (_shoppingItemName is null || _shoppingCatalog.Count == 0) return;

        var prior = _shoppingItemName.Text;
        var names = _shoppingCatalog.Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

        _shoppingItemName.BeginUpdate();
        _shoppingItemName.Items.Clear();
        _shoppingItemName.Items.AddRange(names.Cast<object>().ToArray());
        _shoppingItemName.EndUpdate();

        var source = new AutoCompleteStringCollection();
        source.AddRange(names);
        _shoppingItemName.AutoCompleteCustomSource = source;
        _shoppingItemName.Text = prior;
        _shoppingItemName.SelectionStart = _shoppingItemName.Text.Length;
    }

    private async Task AddShoppingTargetAsync()
    {
        var raw = _shoppingItemName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            MessageBox.Show("Enter or paste an item name first.", "Shopping Cart");
            return;
        }

        await EnsureShoppingCatalogAsync(false);

        var resolved = _shoppingCatalog.Count > 0 ? ResolveCatalogItem(_shoppingCatalog, new ShoppingTarget { ItemName = raw }) : null;
        if (resolved is null)
        {
            var candidates = FindCatalogCandidates(_shoppingCatalog, raw, 8);
            var candidateText = candidates.Count == 0
                ? "No close item names were found."
                : "Closest matches:\n\n" + string.Join("\n", candidates.Select(x => "• " + x.Name));

            MessageBox.Show(
                $"Commander could not confidently resolve:\n\n{raw}\n\n{candidateText}\n\nTry a shorter name such as “green paint”, or choose the exact item from the dropdown.",
                "Shopping Cart — item not resolved");
            return;
        }

        var existing = _shoppingTargets.FirstOrDefault(x => x.ItemId == resolved.Id || x.ItemName.Equals(resolved.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.ItemId = resolved.Id;
            existing.ItemName = resolved.Name;
            existing.DesiredQuantity += (int)_shoppingItemQty.Value;
        }
        else
        {
            _shoppingTargets.Add(new ShoppingTarget
            {
                ItemId = resolved.Id,
                ItemName = resolved.Name,
                DesiredQuantity = (int)_shoppingItemQty.Value
            });
        }

        _shoppingItemName.Text = "";
        SaveShoppingCart();
        RebindShoppingCart();
        FlashStatus($"Added {(int)_shoppingItemQty.Value:N0} × {resolved.Name}.");
    }

    private ShoppingTarget? SelectedShoppingTarget()
    {
        if (_shoppingGrid?.CurrentRow is null) return _shoppingTargets.FirstOrDefault(x => x.RemainingQuantity > 0);
        var name = _shoppingGrid.CurrentRow.Cells["Item"]?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(name)) return _shoppingTargets.FirstOrDefault(x => x.RemainingQuantity > 0);
        return _shoppingTargets.FirstOrDefault(x => x.ItemName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void RemoveSelectedShoppingTarget()
    {
        var target = SelectedShoppingTarget();
        if (target is null) return;
        _shoppingTargets.Remove(target);
        if (target.ItemId > 0) _shoppingScans.Remove(target.ItemId);
        SaveShoppingCart();
        RebindShoppingCart();
    }

    private void SaveShoppingCart() => Storage.Save(Storage.ShoppingCartPath, _shoppingTargets);

    private void CopyShoppingCartForChat()
    {
        var remaining = _shoppingTargets
            .Where(x => x.RemainingQuantity > 0)
            .OrderBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (remaining.Count == 0)
        {
            MessageBox.Show("There is nothing left in the cart to ask for.", "Shopping Cart");
            return;
        }

        var lines = new List<string>
        {
            "Looking for these before I market buy:"
        };

        foreach (var target in remaining)
            lines.Add($"{target.ItemName} x{target.RemainingQuantity:N0}");

        lines.Add("");
        lines.Add($"Total wanted: {remaining.Sum(x => x.RemainingQuantity):N0} items");

        var marketEstimate = remaining.Sum(target =>
        {
            if (target.ItemId <= 0 || !_shoppingScans.TryGetValue(target.ItemId, out var scan)) return 0L;
            return scan.PlannedCost;
        });

        if (marketEstimate > 0)
            lines.Add($"Current market estimate: ${marketEstimate:N0}");

        lines.Add("If you have any, send me the quantity + price. Thanks.");

        var text = string.Join(Environment.NewLine, lines);
        try
        {
            Clipboard.SetText(text);
            FlashStatus($"Copied {remaining.Count:N0} cart line(s) for Torn chat.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Commander could not copy the cart to your clipboard.\n\n" + ex.Message, "Shopping Cart — copy");
        }
    }

    private async Task ScanShoppingCartAsync()
    {
        var key = CredentialManager.LoadApiKey(CurrentProfileId);
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("Save your Torn API key in Settings first.", "Shopping Cart");
            return;
        }

        if (_shoppingTargets.Count == 0)
        {
            MessageBox.Show("Your cart is empty. Load the Paint preset or add an item first.", "Shopping Cart");
            return;
        }

        var maxUnit = _shoppingIgnoreLimits.Checked ? 0L : (long)_shoppingMaxUnitPrice.Value;
        _shoppingSummary.Text = "Reading Torn item catalog and live market listings…";
        FlashStatus("Shopping cart market scan running…");

        try
        {
            var catalog = await _api.FetchItemCatalogAsync(key);
            _shoppingCatalog.Clear();
            _shoppingCatalog.AddRange(catalog);
            PopulateShoppingItemPicker();
            _shoppingScans.Clear();

            var unresolved = new List<string>();
            foreach (var target in _shoppingTargets)
            {
                if (target.RemainingQuantity <= 0) continue;

                var resolved = ResolveCatalogItem(catalog, target);
                if (resolved is null)
                {
                    target.ItemId = 0;
                    unresolved.Add(target.ItemName);
                    continue;
                }

                target.ItemId = resolved.Id;
                target.ItemName = resolved.Name;
            }
            SaveShoppingCart();

            using var gate = new SemaphoreSlim(4);
            var scanTasks = _shoppingTargets
                .Where(x => x.ItemId > 0 && x.RemainingQuantity > 0)
                .Select(async target =>
                {
                    await gate.WaitAsync();
                    try { return await _api.FetchItemMarketAsync(target.ItemId, target.RemainingQuantity, maxUnit, key); }
                    finally { gate.Release(); }
                }).ToArray();

            var scans = await Task.WhenAll(scanTasks);
            foreach (var scan in scans) _shoppingScans[scan.ItemId] = scan;
            RebindShoppingCart();

            if (unresolved.Count > 0)
                FlashStatus($"Cart built, but {unresolved.Count} item name(s) still need resolving.");
            else
                FlashStatus("Shopping cart built from live Item Market listings.");
        }
        catch (Exception ex)
        {
            _shoppingSummary.Text = "Market scan failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Shopping Cart market scan");
        }
    }

    private static MarketCatalogItem? ResolveCatalogItem(List<MarketCatalogItem> catalog, ShoppingTarget target)
    {
        if (target.ItemId > 0)
        {
            var byId = catalog.FirstOrDefault(x => x.Id == target.ItemId);
            if (byId is not null) return byId;
        }

        var raw = ExtractLikelyItemName(target.ItemName);
        var norm = NormalizeItemText(raw);
        if (string.IsNullOrWhiteSpace(norm)) return null;

        var exact = catalog.Where(x => NormalizeItemText(x.Name) == norm).ToList();
        if (exact.Count == 1) return exact[0];

        var scored = catalog
            .Select(x => new { Item = x, Score = ScoreCatalogMatch(raw, x.Name) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Name.Length)
            .ToList();

        if (scored.Count == 0) return null;
        if (scored.Count == 1) return scored[0].Item;

        var first = scored[0];
        var second = scored[1];

        // Accept a clear winner. Otherwise force the user to choose from the dropdown instead of guessing.
        return first.Score >= 85 && first.Score - second.Score >= 8 ? first.Item : null;
    }

    private static List<MarketCatalogItem> FindCatalogCandidates(List<MarketCatalogItem> catalog, string raw, int count)
        => catalog.Select(x => new { Item = x, Score = ScoreCatalogMatch(raw, x.Name) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Name.Length)
            .Take(count)
            .Select(x => x.Item)
            .ToList();

    private static int ScoreCatalogMatch(string queryRaw, string itemName)
    {
        var query = NormalizeItemText(ExtractLikelyItemName(queryRaw));
        var item = NormalizeItemText(itemName);
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(item)) return 0;
        if (query == item) return 100;
        if (item.StartsWith(query, StringComparison.Ordinal)) return 95;
        if (item.Contains(query, StringComparison.Ordinal)) return 90;

        var queryTokens = TokenizeItemText(queryRaw);
        var itemTokens = TokenizeItemText(itemName);

        if (queryTokens.Count == 0) return 0;
        var matched = queryTokens.Count(q => itemTokens.Contains(q));
        if (matched == queryTokens.Count)
        {
            var bonus = itemTokens.Count == queryTokens.Count ? 8 : 0;
            return 80 + bonus;
        }

        // Paint aliases: "purple", "purple paint", "green spray" etc.
        if (itemTokens.Contains("paint") && itemTokens.Contains("spray"))
        {
            var colors = new[] { "black", "blue", "green", "orange", "pink", "purple", "red", "white" };
            var color = colors.FirstOrDefault(c => queryTokens.Contains(c));
            if (color is not null && itemTokens.Contains(color)) return 92;
        }

        return matched * 60 / queryTokens.Count;
    }

    private static string ExtractLikelyItemName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var line = raw.Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? raw.Trim();

        if (line.Contains('\t'))
            line = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? line;

        // Remove obvious trailing market totals if a whole copied row was pasted.
        line = Regex.Replace(line, @"\s+\$[\d,]+(?:\.\d+)?(?:\s.*)?$", "", RegexOptions.CultureInvariant);
        return line.Trim();
    }

    private static string NormalizeItemText(string value)
        => Regex.Replace((value ?? "").ToLowerInvariant(), @"[^a-z0-9]+", "");

    private static HashSet<string> TokenizeItemText(string value)
        => Regex.Matches((value ?? "").ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(x => x.Length > 0 && !x.All(char.IsDigit))
            .ToHashSet(StringComparer.Ordinal);

    private void RebindShoppingCart()
    {
        if (_shoppingGrid is null) return;

        var rows = _shoppingTargets.Select(target =>
        {
            _shoppingScans.TryGetValue(target.ItemId, out var scan);
            var status = target.RemainingQuantity <= 0
                ? "Complete"
                : scan?.Status ?? (target.ItemId == 0 ? "Needs scan / unresolved" : "Needs scan");

            return new
            {
                Item = target.ItemName,
                Want = target.DesiredQuantity,
                Bought = target.PurchasedQuantity,
                Need = target.RemainingQuantity,
                Available = scan?.AvailableQuantity ?? 0,
                Best = scan?.BestPrice ?? 0,
                Avg = Math.Round(scan?.AveragePrice ?? 0m, 0),
                EstCost = scan?.PlannedCost ?? 0,
                Status = status
            };
        }).ToList();

        _shoppingGrid.DataSource = rows;
        foreach (DataGridViewColumn c in _shoppingGrid.Columns)
            c.SortMode = DataGridViewColumnSortMode.NotSortable;

        var planned = _shoppingScans.Values.Sum(x => x.PlannedCost);
        var plannedQty = _shoppingScans.Values.Sum(x => x.PlannedQuantity);
        var purchased = _shoppingTargets.Sum(x => x.PurchasedCost);
        var wallet = _snapshot.Wallet;
        var reserve = (long)_shoppingReserve.Value;
        var maxSpend = (long)_shoppingMaxSpend.Value;

        var budgetNotes = new List<string>();
        if (_shoppingIgnoreLimits.Checked)
        {
            budgetNotes.Add("NUKE MY PILE ON — Commander limits ignored");
        }
        else
        {
            if (maxSpend > 0 && purchased + planned > maxSpend)
                budgetNotes.Add($"OVER cart cap by ${(purchased + planned - maxSpend):N0}");
            if (wallet is not null && wallet.Value - planned < reserve)
                budgetNotes.Add($"would dip below ${reserve:N0} reserve");
        }

        _shoppingSummary.Text =
            $"Planned now: {plannedQty:N0} units • ${planned:N0} • recorded bought: ${purchased:N0}" +
            (wallet is not null ? $" • wallet ${wallet.Value:N0}" : "") +
            (budgetNotes.Count > 0 ? " • " + string.Join(" • ", budgetNotes) : "");
    }

    private async Task PrepareNextCartPurchaseAsync(bool submit)
    {
        var target = SelectedShoppingTarget() ?? _shoppingTargets.FirstOrDefault(x => x.RemainingQuantity > 0);
        if (target is null)
        {
            FlashStatus("Shopping cart is complete or empty.");
            return;
        }

        if (target.ItemId <= 0 || !_shoppingScans.TryGetValue(target.ItemId, out var scan) || scan.Plan.Count == 0)
        {
            await ScanShoppingCartAsync();
            if (target.ItemId <= 0 || !_shoppingScans.TryGetValue(target.ItemId, out scan) || scan.Plan.Count == 0)
            {
                MessageBox.Show("Commander could not build a purchasable plan for this item. Check the item name, price cap and live listings.", "Shopping Cart");
                return;
            }
        }

        var next = scan.Plan[0];
        if (!CanAffordCartStep(next, out var reason))
        {
            MessageBox.Show(reason, "Shopping Cart limit");
            return;
        }

        NavigateActive(TornRoutes.ItemMarketFor(target.ItemId));
        FlashStatus($"Opening {target.ItemName} market…");

        if (!await WaitForMarketPageAsync(target.ItemId))
        {
            MessageBox.Show("The Item Market page did not finish loading in time. Try Prepare Next again.", "Shopping Cart");
            return;
        }

        var active = Active();
        if (active is null) return;

        var success = false;
        string? finalMessage = null;

        if (submit)
        {
            _cartCommanderPurchaseItemId = target.ItemId;
            _cartSuppressBrowserPurchaseUntil = DateTime.Now.AddSeconds(5);
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var raw = await active.ExecuteScriptAsync(BuildMarketActionScript(target, next, submit));
            var immediate = ParseScriptObject(raw);
            var ok = ScriptBool(immediate, "ok");
            finalMessage = ScriptString(immediate, "message");

            if (ok)
            {
                success = true;
                break;
            }

            await Task.Delay(450);
        }

        if (!success)
        {
            _cartCommanderPurchaseItemId = 0;
            MessageBox.Show(finalMessage ?? "Commander could not find the planned market listing after waiting for the live page. Nothing was purchased. Use Market debug and send me the copied output.", "Shopping Cart");
            return;
        }

        FlashStatus(finalMessage ?? (submit ? "Purchase flow started…" : "Opening purchase dialog…"));

        await Task.Delay(submit ? 1700 : 900);

        var stateRaw = await active.ExecuteScriptAsync("window.__tcCartLast || null");
        var state = ParseScriptObject(stateRaw);

        if (!submit)
        {
            var prepared = ScriptBool(state, "prepared");
            var detail = ScriptString(state, "message");
            if (!prepared)
            {
                MessageBox.Show((detail ?? "Commander opened the market but could not safely prepare the quantity dialog.") + "\n\nNothing was purchased.", "Shopping Cart");
                return;
            }

            FlashStatus(detail ?? $"Prepared {next.Quantity:N0} × {target.ItemName}. Review and click Torn's final Buy button.");
            return;
        }

        var submitted = ScriptBool(state, "submitted");
        if (!submitted)
        {
            _cartCommanderPurchaseItemId = 0;
            var detail = ScriptString(state, "message") ?? "Purchase confirmation was not detected.";
            MessageBox.Show(detail + "\n\nNothing was added to Commander's bought count. You can finish the purchase manually; Commander will try to detect that final manual purchase.", "Shopping Cart");
            return;
        }

        _lastCartPurchaseTarget = target;
        _lastCartPurchaseQty = next.Quantity;
        _lastCartPurchaseCost = next.Cost;

        target.PurchasedQuantity = Math.Min(target.DesiredQuantity, target.PurchasedQuantity + next.Quantity);
        target.PurchasedCost += next.Cost;

        scan.Plan.RemoveAt(0);
        SaveShoppingCart();
        RebindShoppingCart();

        _cartCommanderPurchaseItemId = 0;
        FlashStatus($"Submitted one market purchase: {next.Quantity:N0} × {target.ItemName} @ ${next.Price:N0}.");
    }

    private bool CanAffordCartStep(MarketPurchasePlan next, out string reason)
    {
        if (_shoppingIgnoreLimits.Checked)
        {
            reason = "";
            return true;
        }

        var already = _shoppingTargets.Sum(x => x.PurchasedCost);
        var maxSpend = (long)_shoppingMaxSpend.Value;
        var reserve = (long)_shoppingReserve.Value;
        var maxUnit = (long)_shoppingMaxUnitPrice.Value;

        if (maxUnit > 0 && next.Price > maxUnit)
        {
            reason = $"This listing is ${next.Price:N0} each, above your ${maxUnit:N0} max-unit-price limit.\n\nSet Max price per unit to $0 for unlimited, raise it, or enable NUKE MY PILE.";
            return false;
        }

        if (maxSpend > 0 && already + next.Cost > maxSpend)
        {
            reason = $"This purchase would exceed your ${maxSpend:N0} max total cart spend.\n\nSet Max total cart spend to $0 for unlimited, raise it, or enable NUKE MY PILE.";
            return false;
        }

        if (_snapshot.Wallet is long wallet && wallet - next.Cost < reserve)
        {
            reason = $"This purchase would leave about ${(wallet - next.Cost):N0}, below your ${reserve:N0} minimum-wallet reserve.\n\nSet Minimum wallet to keep to $0 if you are willing to spend down to $0, or enable NUKE MY PILE.";
            return false;
        }

        reason = "";
        return true;
    }

    private async Task<bool> WaitForMarketPageAsync(int itemId)
    {
        var active = Active();
        if (active is null) return false;

        for (var i = 0; i < 22; i++)
        {
            await Task.Delay(350);
            var raw = await active.ExecuteScriptAsync(
                $"location.href.includes('itemID={itemId}') && document.readyState !== 'loading' && document.body && document.body.innerText.length > 100");
            if (raw?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true) return true;
        }

        return false;
    }

    private static string BuildMarketActionScript(ShoppingTarget target, MarketPurchasePlan plan, bool submit)
    {
        var nameJson = JsonSerializer.Serialize(target.ItemName);
        var submitJs = submit ? "true" : "false";

        return $$"""
(() => {
  const expectedName = {{nameJson}};
  const expectedPrice = {{plan.Price}};
  const wantedQty = {{plan.Quantity}};
  const shouldSubmit = {{submitJs}};

  window.__tcCartLast = { submitted:false, prepared:false, message:'Looking for planned market listing' };

  const visible = el => !!(el && (el.offsetWidth || el.offsetHeight || el.getClientRects().length));
  const text = el => String(el?.innerText || el?.textContent || '').replace(/\s+/g,' ').trim();
  const fire = el => {
    el.dispatchEvent(new Event('input',{bubbles:true}));
    el.dispatchEvent(new Event('change',{bubbles:true}));
  };
  const moneyValues = s => [...String(s||'').matchAll(/\$?\s*([\d,]+)/g)]
    .map(m => Number(m[1].replace(/,/g,'')))
    .filter(n => Number.isFinite(n));
  const hasExpectedPrice = el => moneyValues(text(el)).includes(expectedPrice);

  const clickables = [...document.querySelectorAll('button,a,[role="button"]')].filter(visible);
  const buyButtons = clickables.filter(b => /\b(buy|purchase)\b/i.test(text(b)) && !/cancel|close|back/i.test(text(b)));

  let row = null;
  let buy = null;

  for (const b of buyButtons) {
    let n = b;
    for (let depth=0; n && depth<10; depth++, n=n.parentElement) {
      const t = text(n);
      if (hasExpectedPrice(n) && t.length < 3500) {
        row = n;
        buy = b;
        break;
      }
    }
    if (row) break;
  }

  if (!row || !buy) {
    const priceNodes = [...document.querySelectorAll('div,span,li,tr,td')].filter(visible).filter(hasExpectedPrice);
    for (const node of priceNodes) {
      let n = node;
      for (let depth=0; n && depth<8; depth++, n=n.parentElement) {
        const candidate = [...n.querySelectorAll('button,a,[role="button"]')].filter(visible)
          .find(b => /\b(buy|purchase)\b/i.test(text(b)) && !/cancel|close|back/i.test(text(b)));
        if (candidate && text(n).length < 3500) {
          row = n;
          buy = candidate;
          break;
        }
      }
      if (row) break;
    }
  }

  if (!row || !buy)
    return {ok:false,message:`Waiting for a Buy listing at $${expectedPrice.toLocaleString()}`};

  row.style.outline='3px solid #5ca9ff';
  row.scrollIntoView({block:'center',behavior:'smooth'});

  const rowInputs=[...row.querySelectorAll('input[type="number"],input[inputmode="numeric"],input[type="text"]')].filter(visible);
  const rowQty=rowInputs.find(i => /qty|quantity|amount/i.test((i.name||'')+' '+(i.placeholder||'')+' '+(i.getAttribute('aria-label')||''))) || null;
  if (rowQty) {
    const max=Number(rowQty.max)||wantedQty;
    const desired=Math.max(1,Math.min(wantedQty,max));
    const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')?.set;
    if (setter) setter.call(rowQty,String(desired)); else rowQty.value=String(desired);
    fire(rowQty);
    rowQty.focus();
    rowQty.select?.();

    if (!shouldSubmit) {
      window.__tcCartLast={submitted:false,prepared:true,message:`Prepared ${wantedQty.toLocaleString()} × ${expectedName} in the highlighted market row. Review and press Torn's Buy button.`};
      return {ok:true,message:`Prepared ${wantedQty.toLocaleString()} × ${expectedName} in the highlighted market row`};
    }
  }

  buy.click();
  window.__tcCartLast = { submitted:false, prepared:false, message:'Opened purchase dialog' };

  setTimeout(() => {
    const dialogs=[...document.querySelectorAll('[role="dialog"],.modal,.dialog,[class*="modal"],[class*="dialog"],[class*="confirm"]')].filter(visible);
    const dialog=dialogs.find(d => /\b(buy|purchase|confirm)\b/i.test(text(d))) || dialogs[0] || null;

    if (!dialog) {
      if (shouldSubmit && (!document.contains(buy) || buy.disabled)) {
        window.__tcCartLast={submitted:true,prepared:true,message:'Single market purchase appears to have submitted directly'};
      } else {
        window.__tcCartLast={submitted:false,prepared:false,message:'Matched the listing, but Torn did not expose a purchase dialog Commander can safely fill'};
      }
      return;
    }

    const qInputs=[...dialog.querySelectorAll('input[type="number"],input[inputmode="numeric"],input[type="text"]')].filter(visible);
    const q=qInputs.find(i => /qty|quantity|amount/i.test((i.name||'')+' '+(i.placeholder||'')+' '+(i.getAttribute('aria-label')||''))) || qInputs[0] || null;

    if (q) {
      const max=Number(q.max)||wantedQty;
      const desired=Math.max(1,Math.min(wantedQty,max));
      const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')?.set;
      if (setter) setter.call(q,String(desired)); else q.value=String(desired);
      fire(q);
      q.focus();
      q.select?.();
    }

    window.__tcCartLast={submitted:false,prepared:true,message:`Prepared ${wantedQty.toLocaleString()} × ${expectedName} @ $${expectedPrice.toLocaleString()}. Review the Torn dialog.`};

    if (!shouldSubmit) return;

    const confirms=[...dialog.querySelectorAll('button,[role="button"],input[type="submit"]')].filter(visible)
      .filter(b => /\b(buy|purchase|confirm)\b/i.test(text(b) || b.value || '') && !/cancel|close|back/i.test(text(b) || b.value || ''));

    const confirm=confirms[confirms.length-1] || null;
    if (!confirm) {
      window.__tcCartLast={submitted:false,prepared:true,message:'Quantity prepared, but Commander refused to guess the final confirmation button'};
      return;
    }

    confirm.click();
    window.__tcCartLast={submitted:true,prepared:true,message:`Submitted ${wantedQty} × ${expectedName} @ $${expectedPrice}`};
  }, 450);

  return {ok:true,message:`Matched $${expectedPrice.toLocaleString()} listing and opened its purchase flow`};
})()
""";
    }

    private void RecordSelectedBoughtFallback()
    {
        var target = SelectedShoppingTarget();
        if (target is null)
        {
            MessageBox.Show("Select a cart row first.", "Shopping Cart");
            return;
        }

        var remaining = Math.Max(1, target.RemainingQuantity);
        using var form = new Form
        {
            Text = "Record manual purchase",
            Width = 360,
            Height = 190,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Bg,
            ForeColor = TextMain
        };

        var label = new Label
        {
            Text = $"{target.ItemName}\nHow many did you just buy?",
            AutoSize = true,
            ForeColor = TextMain,
            Location = new Point(18, 18)
        };

        var qty = Num(Math.Min(remaining, 1), 1, Math.Max(remaining, 1_000_000));
        qty.Location = new Point(18, 65);
        qty.Width = 140;

        var ok = new Button { Text = "Record", DialogResult = DialogResult.OK, Width = 90, Location = new Point(165, 100) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Location = new Point(260, 100) };

        form.Controls.Add(label);
        form.Controls.Add(qty);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog(this) != DialogResult.OK) return;

        ApplyDetectedCartPurchase(target.ItemId, (int)qty.Value, 0, "manual fallback");
    }

    private void HandleBrowserMessage(string message)
    {
        if (TryHandleShoppingCartBrowserMessage(message)) return;
        // Keep the result visible in the dashboard; a Torn route/status update
        // can overwrite the tiny native status bar immediately afterward.
        if (message.StartsWith("Graffiti [", StringComparison.Ordinal))
        {
            _lastGraffitiActionStatus = message.Length > 650 ? message[..650] : message;
            UpdateModernDashboard();
        }
        FlashStatus(message);
    }

    private bool TryHandleShoppingCartBrowserMessage(string message)
    {
        const string prefix = "TC_CART_PURCHASE|";
        if (!message.StartsWith(prefix, StringComparison.Ordinal)) return false;

        try
        {
            var json = message[prefix.Length..];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var itemId = root.TryGetProperty("itemId", out var idEl) && idEl.TryGetInt32(out var id) ? id : 0;
            var quantity = root.TryGetProperty("quantity", out var qEl) && qEl.TryGetInt32(out var q) ? Math.Max(1, q) : 1;
            var unitPrice = root.TryGetProperty("unitPrice", out var pEl) && pEl.TryGetInt64(out var p) ? Math.Max(0, p) : 0;

            if (itemId <= 0) return true;

            if (itemId == _cartCommanderPurchaseItemId && DateTime.Now <= _cartSuppressBrowserPurchaseUntil)
                return true;

            ApplyDetectedCartPurchase(itemId, quantity, unitPrice, "browser");
        }
        catch
        {
            // Never let a helper message break the browser.
        }

        return true;
    }

    private void ApplyDetectedCartPurchase(int itemId, int quantity, long unitPrice, string source)
    {
        var target = _shoppingTargets.FirstOrDefault(x => x.ItemId == itemId);
        if (target is null)
        {
            FlashStatus("Market purchase detected, but that item is not in the active Commander cart.");
            return;
        }

        var applied = Math.Min(Math.Max(1, quantity), Math.Max(1, target.RemainingQuantity));
        if (target.RemainingQuantity <= 0) return;

        _lastCartPurchaseTarget = target;
        _lastCartPurchaseQty = applied;
        _lastCartPurchaseCost = unitPrice > 0 ? unitPrice * (long)applied : 0;

        target.PurchasedQuantity = Math.Min(target.DesiredQuantity, target.PurchasedQuantity + applied);
        if (unitPrice > 0) target.PurchasedCost += unitPrice * (long)applied;

        _shoppingScans.Remove(itemId);
        SaveShoppingCart();
        if (_shoppingGrid is not null) RebindShoppingCart();

        FlashStatus($"Cart updated from {source}: +{applied:N0} {target.ItemName}" + (unitPrice > 0 ? $" @ ${unitPrice:N0}" : "") + ".");
    }

    private static JsonElement? ParseScriptObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static bool ScriptBool(JsonElement? obj, string name)
    {
        if (obj is not JsonElement el || el.ValueKind != JsonValueKind.Object) return false;
        return el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static string? ScriptString(JsonElement? obj, string name)
    {
        if (obj is not JsonElement el || el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private void UndoLastCartPurchase()
    {
        if (_lastCartPurchaseTarget is null || _lastCartPurchaseQty <= 0) return;

        _lastCartPurchaseTarget.PurchasedQuantity = Math.Max(0, _lastCartPurchaseTarget.PurchasedQuantity - _lastCartPurchaseQty);
        _lastCartPurchaseTarget.PurchasedCost = Math.Max(0, _lastCartPurchaseTarget.PurchasedCost - _lastCartPurchaseCost);

        if (_lastCartPurchaseTarget.ItemId > 0)
            _shoppingScans.Remove(_lastCartPurchaseTarget.ItemId);

        _lastCartPurchaseTarget = null;
        _lastCartPurchaseQty = 0;
        _lastCartPurchaseCost = 0;

        SaveShoppingCart();
        RebindShoppingCart();
        FlashStatus("Last recorded cart purchase undone. Rescan before continuing.");
    }

    private async Task CopyMarketDebugAsync()
    {
        var active = Active();
        if (active is null) return;

        var raw = await active.ExecuteScriptAsync("""
(() => ({
  url:location.href,
  bodyText:(document.body?.innerText||'').slice(0,5000),
  dialogs:[...document.querySelectorAll('[role="dialog"],.modal,.dialog,[class*="modal"],[class*="dialog"]')]
    .filter(e=>e.offsetParent!==null).slice(0,20).map(e=>({text:(e.innerText||e.textContent||'').trim().slice(0,1000),cls:String(e.className||'').slice(0,200)})),
  buttons:[...document.querySelectorAll('button,a,[role="button"]')]
    .filter(e=>e.offsetParent!==null).slice(0,120).map(e=>({text:(e.innerText||e.textContent||'').trim().slice(0,160),cls:String(e.className||'').slice(0,200)})),
  inputs:[...document.querySelectorAll('input')]
    .filter(e=>e.offsetParent!==null).slice(0,80).map(e=>({type:e.type,name:e.name,placeholder:e.placeholder,aria:e.getAttribute('aria-label'),value:e.value,max:e.max}))
}))()
""");

        if (string.IsNullOrWhiteSpace(raw)) return;

        try
        {
            Clipboard.SetText(raw);
            FlashStatus("Market debug copied to clipboard.");
        }
        catch
        {
        }
    }
}
