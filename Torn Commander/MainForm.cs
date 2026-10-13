using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TornCommander;

public sealed partial class MainForm : Form
{
    private readonly Color Bg = Color.FromArgb(15, 17, 21);
    private readonly Color PanelBg = Color.FromArgb(24, 27, 33);
    private readonly Color FieldBg = Color.FromArgb(34, 38, 46);
    private readonly Color Accent = Color.FromArgb(92, 169, 255);
    private readonly Color Good = Color.FromArgb(91, 191, 125);
    private readonly Color Warn = Color.FromArgb(235, 177, 74);
    private readonly Color Bad = Color.FromArgb(232, 98, 98);
    private readonly Color TextMain = Color.FromArgb(235, 238, 243);
    private readonly Color TextMuted = Color.FromArgb(165, 173, 184);

    private readonly TornApiClient _api = new();
    private AppSettings _settings = Storage.LoadSettings();
    private TornSnapshot _snapshot = new();
    private RecommendationResult _recommendation = new();
    private CoreWebView2Environment? _webEnv;
    private System.Windows.Forms.Timer? _fastTimer;
    private System.Windows.Forms.Timer? _fullTimer;
    private System.Windows.Forms.Timer? _uiTimer;
    private bool _syncing;

    private readonly TabControl _browserTabs = new();
    private readonly List<BrowserTab> _browserTabObjects = new();
    private readonly TextBox _address = new();
    private readonly Label _status = new();
    private readonly ComboBox _quickRoute = new();
    private readonly ComboBox _profileSwitchCombo = new();
    private readonly ContextMenuStrip _tabMenu = new();
    private SplitContainer _mainSplit = null!;
    private bool _splitterInitialized;
    private int _contextTabIndex = -1;

    private Label _profileLabel = null!;
    private Label _barsLabel = null!;
    private Label _capLabel = null!;
    private Label _apiStatusLabel = null!;
    private Label _coachPhase = null!;
    private Label _coachHeadline = null!;
    private Label _coachWhy = null!;
    private Label _coachNerve = null!;
    private Label _coachMetrics = null!;
    private Label _coachEducation = null!;
    private Label _coachAccount = null!;
    private Label _pageHint = null!;
    private RichTextBox _coachChecklist = null!;
    private Label _guideActionLabel = null!;
    private Button _guideActionButton = null!;
    private Button _guideOpenButton = null!;
    private ComboBox _guideActionModeCombo = null!;
    private ComboBox _guideActionModeSettingsCombo = null!;
    private DataGridView _crimeGrid = null!;
    private Label _crimeSyncLabel = null!;
    private Label _meritSummaryLabel = null!;
    private Label _meritSpendLabel = null!;
    private Label _coachMerits = null!;
    private DataGridView _meritGrid = null!;
    private ComboBox _meritModeCombo = null!;
    private TextBox _meritSearchBox = null!;
    private CheckBox _meritShowAchieved = null!;
    private CheckBox _meritShowUnavailable = null!;
    private List<MeritAward> _visibleMeritAwards = new();
    private Label _moneyApiLabel = null!;
    private Label _racingSkillLabel = null!;

    private readonly BindingSource _tradeSource = new();
    private List<NnbMilestone> _milestones = new();
    private List<TradeEntry> _trades = new();
    private List<LossOrder> _lossOrders = new();
    private DataGridView _lossGrid = null!;
    private ComboBox _lossProfileACombo = null!;
    private ComboBox _lossProfileBCombo = null!;
    private TextBox _lossTargetBox = null!;
    private TextBox _lossBuyerBox = null!;
    private NumericUpDown _lossRequested = null!;
    private NumericUpDown _lossPay = null!;
    private DataGridView _tradeGrid = null!;

    private readonly BindingSource _casinoSource = new();
    private List<CasinoSession> _casino = new();
    private DataGridView _casinoGrid = null!;

    private TextBox _racingNotes = null!;
    private TextBox _apiKeyBox = null!;
    private CheckBox _autoRefreshCheck = null!;
    private CheckBox _restoreTabsCheck = null!;
    private CheckBox _rememberWindowCheck = null!;
    private NumericUpDown _fastRefreshSeconds = null!;
    private NumericUpDown _fullRefreshSeconds = null!;
    private NumericUpDown _addictionPercent = null!;
    private NumericUpDown _nnbOverride = null!;
    private CheckedListBox _scriptsList = null!;
    private ListBox _whitelistList = null!;
    private TextBox _whitelistDomainBox = null!;
    private CheckBox _allowHelperSitesCheck = null!;
    private ListBox _customLinksList = null!;

    public MainForm()
    {
        Text = "Torn Commander v0.6.33";
        MinimumSize = new Size(850, 600);
        Size = new Size(1500, 900);
        BackColor = Bg;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;
        TryApplyAppIcon();

        Program.TraceStartup("MainForm constructor: seeding bundled scripts.");
        var bundled = Storage.SeedBundledScripts();
        Program.TraceStartup("MainForm constructor: bundled scripts checked.");
        const int bundledScriptsGeneration = 9;
        if (!_settings.BundledScriptsInitialized || _settings.BundledScriptsGeneration < bundledScriptsGeneration)
        {
            foreach (var name in bundled)
                if (!_settings.EnabledScripts.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _settings.EnabledScripts.Add(name);
            _settings.BundledScriptsInitialized = true;
            _settings.BundledScriptsGeneration = bundledScriptsGeneration;
            Storage.SaveSettings(_settings);
        }

        EnsureProfiles();
        ApplySavedWindowPlacement();

        _milestones = Storage.Load(Storage.MilestonesPath, new List<NnbMilestone>());
        _trades = Storage.Load(Storage.TradesPath, new List<TradeEntry>());
        _lossOrders = Storage.Load(Storage.LossOrdersPath, new List<LossOrder>());
        _casino = Storage.Load(Storage.CasinoPath, new List<CasinoSession>());

        Program.TraceStartup("MainForm constructor: building controls.");
        BuildUi();
        Program.TraceStartup("MainForm constructor: controls built.");
        Shown += async (_, _) =>
        {
            Program.TraceStartup("Main window Shown event reached.");
            try
            {
                ApplyCompanionPaneWidth();
                Program.TraceStartup("Window layout applied; starting WebView initialization.");
                await InitializeAsync();
                Program.TraceStartup("WebView initialization task returned.");
                // Ask once before changing any old, potentially user-customized
                // example profile labels. This never touches cookies or credentials.
                ReviewLegacyExampleProfileLabels();
            }
            catch (Exception ex)
            {
                Program.TraceStartup("Shown event error: " + ex.GetType().Name + ": " + ex.Message);
                MessageBox.Show("Startup failed. Look at %LOCALAPPDATA%\\TornCommander\\startup-diagnostic.log.\n\n" + ex.Message,
                    "Torn Commander startup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        FormClosing += (_, _) => PersistOnClose();
    }


    private void TryApplyAppIcon()
    {
        try
        {
            var direct = Path.Combine(AppContext.BaseDirectory, "Assets", "TornCommander_T_Icon.ico");
            if (File.Exists(direct))
            {
                Icon = new Icon(direct);
                return;
            }

            var embedded = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "TornCommander_T_Icon.ico");
            if (File.Exists(embedded))
            {
                Icon = new Icon(embedded);
            }
        }
        catch
        {
            // If the custom icon fails to load, Windows will fall back to the EXE icon.
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.W))
        {
            CloseBrowserTab(_browserTabs.SelectedIndex);
            return true;
        }
        if (keyData == (Keys.Control | Keys.T))
        {
            _ = AddBrowserTabAsync(TornRoutes.Home);
            return true;
        }
        if (keyData == (Keys.Control | Keys.L))
        {
            _address.Focus();
            _address.SelectAll();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private string CurrentProfileId => _settings.CurrentProfileId;

    private AppProfile CurrentProfile
        => _settings.Profiles.FirstOrDefault(x => x.Id.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase))
           ?? _settings.Profiles.First();

    private void EnsureProfiles()
    {
        if (_settings.Profiles is null || _settings.Profiles.Count == 0)
            _settings.Profiles = new List<AppProfile> { new() { Id = "main", Name = "Player 1" } };

        foreach (var profile in _settings.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N")[..8];
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = profile.Id;
            profile.OpenTabs ??= new List<string>();
        }

        if (!_settings.Profiles.Any(x => x.Id.Equals(_settings.CurrentProfileId, StringComparison.OrdinalIgnoreCase)))
            _settings.CurrentProfileId = _settings.Profiles[0].Id;

        // Carry the pre-profile saved tabs into the main profile the first time this build runs.
        var main = _settings.Profiles.FirstOrDefault(x => x.Id.Equals("main", StringComparison.OrdinalIgnoreCase));
        if (main is not null && (main.OpenTabs is null || main.OpenTabs.Count == 0) && _settings.OpenTabs.Count > 0)
            main.OpenTabs = _settings.OpenTabs.ToList();

        Storage.SaveSettings(_settings);
    }

    private void RefreshProfileSelector()
    {
        if (_profileSwitchCombo is null) return;
        var selectedId = CurrentProfileId;
        _profileSwitchCombo.BeginUpdate();
        try
        {
            _profileSwitchCombo.Items.Clear();
            foreach (var profile in _settings.Profiles)
                _profileSwitchCombo.Items.Add($"{profile.Name} [{profile.Id}]");
            var idx = _settings.Profiles.FindIndex(x => x.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (_profileSwitchCombo.Items.Count > 0) _profileSwitchCombo.SelectedIndex = Math.Max(0, idx);
        }
        finally { _profileSwitchCombo.EndUpdate(); }
    }

    private async Task SwitchSelectedProfileAsync()
    {
        var idx = _profileSwitchCombo.SelectedIndex;
        if (idx < 0 || idx >= _settings.Profiles.Count) return;
        await SwitchProfileAsync(_settings.Profiles[idx].Id);
    }

    private async Task SwitchProfileAsync(string profileId)
    {
        var target = _settings.Profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (target is null || target.Id.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase)) return;

        _apiSyncCancellation?.Cancel();
        await CancelPendingGraffitiAsync();
        SaveCurrentProfileTabs();
        FlashStatus($"Switching to {target.Name}…");
        CloseAllTabs();
        _snapshot = new TornSnapshot();
        _recommendation = new RecommendationResult();
        _travelSituation = new();
        _travelStateLastProbe = DateTime.MinValue;
        _settings.CurrentProfileId = target.Id;
        ClearTravelState();
        ClearTravelArrival();
        _settings.ApiFullAccessVerified = false;
        Storage.SaveSettings(_settings);
        RefreshProfileSelector();
        Text = $"Torn Commander v0.6.33 — {target.Name}";

        if (_apiKeyBox is not null) _apiKeyBox.Text = CredentialManager.LoadApiKey(CurrentProfileId) ?? "";
        _webEnv = await GetWebEnvironmentAsync(CurrentProfileId);

        var urls = _settings.RestoreTabsOnStartup && target.OpenTabs.Count > 0
            ? target.OpenTabs.Select(TornRoutes.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList()
            : new List<string> { TornRoutes.Home };
        foreach (var url in urls) await AddBrowserTabAsync(url);

        UpdateAllUi();
        if (!string.IsNullOrWhiteSpace(CredentialManager.LoadApiKey(CurrentProfileId)))
            await RefreshFullAsync(false);
        else
            _apiStatusLabel.Text = $"API: no saved key for {target.Name}. Save one in Settings.";

        FlashStatus($"Profile: {target.Name}");
    }

    private void SaveCurrentProfileTabs()
    {
        var profile = _settings.Profiles.FirstOrDefault(x => x.Id.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return;
        profile.OpenTabs = _browserTabObjects
            .Select(x => x.WebView.Source?.ToString() ?? x.InitialUrl)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(TornRoutes.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        if (profile.OpenTabs.Count == 0) profile.OpenTabs.Add(TornRoutes.Home);
    }

    // UI safety limit: stored profiles do not imply simultaneous browser sessions.
    // WebView2 has no documented fixed count; limit the list to keep management usable.
    private const int MaxSavedProfiles = 10;

    private void RefreshLossProfileCombos()
    {
        if (_lossProfileACombo is null || _lossProfileBCombo is null) return;
        // Prefer identity to a raw index; indices change after profile deletion.
        static string SelectedId(ComboBox combo)
        {
            var text = combo.SelectedItem?.ToString() ?? "";
            var open = text.LastIndexOf('[');
            return open >= 0 && text.EndsWith("]", StringComparison.Ordinal) ? text[(open + 1)..^1] : "";
        }
        var aId = SelectedId(_lossProfileACombo);
        var bId = SelectedId(_lossProfileBCombo);
        foreach (var combo in new[] { _lossProfileACombo, _lossProfileBCombo })
        {
            combo.Items.Clear();
            foreach (var profile in _settings.Profiles) combo.Items.Add($"{profile.Name} [{profile.Id}]");
        }
        if (_lossProfileACombo.Items.Count > 0)
            _lossProfileACombo.SelectedIndex = Math.Max(0, _settings.Profiles.FindIndex(x => x.Id.Equals(aId, StringComparison.OrdinalIgnoreCase)));
        if (_lossProfileBCombo.Items.Count > 0)
            _lossProfileBCombo.SelectedIndex = Math.Max(0, _settings.Profiles.FindIndex(x => x.Id.Equals(bId, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task AddProfileAsync()
    {
        if (_settings.Profiles.Count >= MaxSavedProfiles)
        {
            MessageBox.Show($"Commander currently supports up to {MaxSavedProfiles} saved profiles. This is an app safety limit, not a Torn login limit. Remove an unused profile to add another.", "Profile limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var name = Prompt("Add Torn browser profile", "Profile display name (one real player's Torn account)", "New profile")?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Length > 40)
        {
            MessageBox.Show("Use a profile name of 40 characters or fewer.", "Profile name");
            return;
        }
        if (_settings.Profiles.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A profile with that display name already exists. Choose a distinct name.", "Profile name");
            return;
        }
        // Never reuse a name-based ID: deleted profiles can leave live login cookies
        // in their browser backup folder. A new identity must always start clean.
        var id = "acct-" + Guid.NewGuid().ToString("N")[..12];
        while (_settings.Profiles.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            id = "acct-" + Guid.NewGuid().ToString("N")[..12];
        _settings.Profiles.Add(new AppProfile { Id = id, Name = name });
        Storage.SaveSettings(_settings);
        RefreshProfileSelector();
        RefreshLossProfileCombos();
        await SwitchProfileAsync(id);
        UpdateModernDashboard();
        FlashStatus($"New isolated profile '{name}' ready. Sign into Torn in this browser; add its API key in Settings if needed.");
    }

    private void RenameCurrentProfile()
    {
        var name = Prompt("Rename Commander profile", "Profile display name", CurrentProfile.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Length > 40)
        {
            MessageBox.Show("Use a profile name of 40 characters or fewer.", "Profile name");
            return;
        }
        if (_settings.Profiles.Any(x => !x.Id.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A profile with that display name already exists.", "Profile name");
            return;
        }
        CurrentProfile.Name = name;
        Storage.SaveSettings(_settings);
        RefreshProfileSelector();
        RefreshLossProfileCombos();
        Text = $"Torn Commander v0.6.33 — {name}";
        UpdateModernDashboard();
        FlashStatus($"Profile renamed to {name}.");
    }

    private async Task DeleteCurrentProfileAsync()
    {
        if (_settings.Profiles.Count <= 1)
        {
            MessageBox.Show("Commander must keep at least one profile.", "Profiles");
            return;
        }
        var doomed = CurrentProfile;
        var attachedOrders = _lossOrders.Count(x => !x.Status.Equals("Complete", StringComparison.OrdinalIgnoreCase) &&
            (x.ProfileAId.Equals(doomed.Id, StringComparison.OrdinalIgnoreCase) || x.ProfileBId.Equals(doomed.Id, StringComparison.OrdinalIgnoreCase)));
        if (attachedOrders > 0)
        {
            MessageBox.Show($"'{doomed.Name}' is still assigned to {attachedOrders} unfinished Loss Desk order(s). Complete or reassign those orders before removing this profile. Nothing was changed.",
                "Profile in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var confirm = $"Remove Commander profile '{doomed.Name}'?\n\nThis does NOT delete the Torn game account.\nThe saved Commander API key will be removed from Windows Credential Manager.\n\nBrowser cookies and local profile data are deliberately kept as a backup. They will not automatically be reused for newly created profiles.\n\nAny initialized Loss Desk worker using this profile will be stopped and closed.";
        if (MessageBox.Show(confirm, "Remove Commander profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        // Profile removal must never leave an orphan worker clicking Torn.
        if (_lossWorkerA is not null && _lossWorkerA.ProfileId.Equals(doomed.Id, StringComparison.OrdinalIgnoreCase))
        {
            await _lossWorkerA.StopAsync();
            ReleaseClaimsForWorker(_lossWorkerA);
            _lossWorkerA.Dispose();
            _lossWorkerA = null;
            _lossWorkerHostA?.Controls.Clear();
            _lossWorkerHostA?.Controls.Add(WorkerPlaceholder("Account A worker not initialized."));
        }
        if (_lossWorkerB is not null && _lossWorkerB.ProfileId.Equals(doomed.Id, StringComparison.OrdinalIgnoreCase))
        {
            await _lossWorkerB.StopAsync();
            ReleaseClaimsForWorker(_lossWorkerB);
            _lossWorkerB.Dispose();
            _lossWorkerB = null;
            _lossWorkerHostB?.Controls.Clear();
            _lossWorkerHostB?.Controls.Add(WorkerPlaceholder("Account B worker not initialized."));
        }
        CloseAllTabs();
        CredentialManager.ForgetApiKey(doomed.Id);
        var fallback = _settings.Profiles.First(x => !x.Id.Equals(doomed.Id, StringComparison.OrdinalIgnoreCase));
        _settings.Profiles.Remove(doomed);
        _settings.CurrentProfileId = fallback.Id;
        _settings.ApiFullAccessVerified = false;
        Storage.SaveSettings(_settings);
        RefreshProfileSelector();
        RefreshLossProfileCombos();

        _snapshot = new TornSnapshot();
        _recommendation = new RecommendationResult();
        _travelSituation = new();
        _travelStateLastProbe = DateTime.MinValue;
        _webEnv = await GetWebEnvironmentAsync(CurrentProfileId);
        var urls = _settings.RestoreTabsOnStartup && fallback.OpenTabs.Count > 0
            ? fallback.OpenTabs.Select(TornRoutes.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList()
            : new List<string> { TornRoutes.Home };
        foreach (var url in urls) await AddBrowserTabAsync(url);
        if (_apiKeyBox is not null) _apiKeyBox.Text = CredentialManager.LoadApiKey(CurrentProfileId) ?? "";
        Text = $"Torn Commander v0.6.33 — {fallback.Name}";
        UpdateAllUi();
        if (!string.IsNullOrWhiteSpace(CredentialManager.LoadApiKey(CurrentProfileId)))
            await RefreshFullAsync(false);
        else if (_apiStatusLabel is not null)
            _apiStatusLabel.Text = $"API: no saved key for {fallback.Name}. Save one in Settings.";
        UpdateModernDashboard();
        FlashStatus($"Removed Commander profile '{doomed.Name}'. Local browser backup retained; now using {fallback.Name}.");
    }

    private void ApplySavedWindowPlacement()
    {
        if (!_settings.RememberWindowPlacement || !_settings.WindowPlacementSaved)
        {
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Normal;
            return;
        }

        var width = Math.Max(MinimumSize.Width, _settings.WindowWidth);
        var height = Math.Max(MinimumSize.Height, _settings.WindowHeight);
        var desired = new Rectangle(_settings.WindowX, _settings.WindowY, width, height);
        var visible = Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(desired));
        if (visible)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = desired;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(width, height);
        }

        WindowState = _settings.WindowMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
    }

    private void BuildUi()
    {
        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(55, 60, 70),
            SplitterWidth = 7,
            FixedPanel = FixedPanel.None,
            IsSplitterFixed = false
        };

        Controls.Add(_mainSplit);
        _mainSplit.Panel1.Controls.Add(BuildBrowserArea());
        _mainSplit.Panel2.Controls.Add(BuildCompanionTabs());

        // Do not set SplitterDistance while the form is still being constructed.
        // WinForms can report a temporary/zero SplitContainer width here, which can
        // throw an ArgumentOutOfRangeException before the window ever appears.
        // Apply the saved width from the Shown event once layout has a real size.
        _mainSplit.SplitterMoved += (_, _) =>
        {
            if (_splitterInitialized && _mainSplit.ClientSize.Width > 0)
                _settings.CompanionPaneWidth = Math.Max(_mainSplit.Panel2MinSize, _mainSplit.ClientSize.Width - _mainSplit.SplitterDistance - _mainSplit.SplitterWidth);
            ResizeAllCompanionFlows();
        };
        _mainSplit.DoubleClick += (_, _) =>
        {
            var target = Math.Min(520, Math.Max(_mainSplit.Panel2MinSize, _mainSplit.ClientSize.Width - _mainSplit.Panel1MinSize - _mainSplit.SplitterWidth));
            _mainSplit.SplitterDistance = Math.Max(_mainSplit.Panel1MinSize, _mainSplit.ClientSize.Width - target - _mainSplit.SplitterWidth);
            ResizeAllCompanionFlows();
        };
        _mainSplit.Panel2.SizeChanged += (_, _) => ResizeAllCompanionFlows();
    }

    private Control BuildBrowserArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Bg,
            Padding = new Padding(5)
        };
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 13,
            RowCount = 1,
            BackColor = PanelBg,
            Padding = new Padding(4, 4, 4, 3)
        };
        foreach (var w in new[] { 36, 36, 36, 54 }) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var w in new[] { 42, 40, 40, 150, 48, 42, 132, 62 }) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, w));

        toolbar.Controls.Add(MakeButton("←", (_, _) => { if (Active()?.WebView.CanGoBack == true) Active()!.WebView.GoBack(); }, 34), 0, 0);
        toolbar.Controls.Add(MakeButton("→", (_, _) => { if (Active()?.WebView.CanGoForward == true) Active()!.WebView.GoForward(); }, 34), 1, 0);
        toolbar.Controls.Add(MakeButton("⟳", (_, _) => Active()?.WebView.Reload(), 34), 2, 0);
        toolbar.Controls.Add(MakeButton("Torn", (_, _) => NavigateActive(TornRoutes.Home), 50), 3, 0);

        _address.Dock = DockStyle.Fill;
        _address.BackColor = FieldBg;
        _address.ForeColor = TextMain;
        _address.BorderStyle = BorderStyle.FixedSingle;
        _address.Margin = new Padding(4, 3, 4, 3);
        _address.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            GoAddress();
            e.SuppressKeyPress = true;
        };
        toolbar.Controls.Add(_address, 4, 0);
        toolbar.Controls.Add(MakeButton("Go", (_, _) => GoAddress(), 38), 5, 0);
        toolbar.Controls.Add(MakeButton("+", async (_, _) => await AddBrowserTabAsync(TornRoutes.Home), 36), 6, 0);
        toolbar.Controls.Add(MakeButton("×", (_, _) => CloseBrowserTab(_browserTabs.SelectedIndex), 36), 7, 0);

        _quickRoute.Dock = DockStyle.Fill;
        _quickRoute.DropDownStyle = ComboBoxStyle.DropDownList;
        _quickRoute.BackColor = FieldBg;
        _quickRoute.ForeColor = TextMain;
        _quickRoute.Margin = new Padding(3, 3, 3, 3);
        _quickRoute.Items.AddRange(new object[]
        {
            "Crimes", "Travel", "Racing", "Education", "Faction", "Company", "Gym", "Items", "Bazaar", "Casino",
            "ChatGPT", "Google sign-in", "Discord", "torn.report", "YATA"
        });
        _quickRoute.SelectedIndex = 0;
        toolbar.Controls.Add(_quickRoute, 8, 0);
        toolbar.Controls.Add(MakeButton("Open", (_, _) => OpenQuickRoute(), 44), 9, 0);
        toolbar.Controls.Add(MakeButton("AI", (_, _) => OpenAiWithContext(), 38), 10, 0);

        _profileSwitchCombo.Dock = DockStyle.Fill;
        _profileSwitchCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileSwitchCombo.BackColor = FieldBg;
        _profileSwitchCombo.ForeColor = TextMain;
        _profileSwitchCombo.Margin = new Padding(3);
        RefreshProfileSelector();
        toolbar.Controls.Add(_profileSwitchCombo, 11, 0);
        toolbar.Controls.Add(MakeButton("Switch", async (_, _) => await SwitchSelectedProfileAsync(), 58), 12, 0);

        _browserTabs.Dock = DockStyle.Fill;
        _browserTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _browserTabs.SizeMode = TabSizeMode.Fixed;
        _browserTabs.ItemSize = new Size(168, 28);
        _browserTabs.Padding = new Point(16, 4);
        _browserTabs.ShowToolTips = true;
        _browserTabs.DrawItem += DrawBrowserTab;
        _browserTabs.SelectedIndexChanged += (_, _) => SyncAddressFromActive();
        _browserTabs.MouseDown += BrowserTabsMouseDown;

        _tabMenu.BackColor = PanelBg;
        _tabMenu.ForeColor = TextMain;
        _tabMenu.Items.Add("Close", null, (_, _) => CloseBrowserTab(_contextTabIndex));
        _tabMenu.Items.Add("Close other tabs", null, (_, _) => CloseOtherTabs(_contextTabIndex));
        _tabMenu.Items.Add("Close all tabs", null, (_, _) => CloseAllTabs());
        _tabMenu.Items.Add(new ToolStripSeparator());
        _tabMenu.Items.Add("Open in normal Edge", null, (_, _) => OpenTabInEdge(_contextTabIndex));

        _status.Dock = DockStyle.Fill;
        _status.BackColor = PanelBg;
        _status.ForeColor = TextMuted;
        _status.Text = "Ready — Ctrl+T new tab • Ctrl+W close tab • Ctrl+L address";
        _status.Padding = new Padding(6, 2, 0, 0);

        area.Controls.Add(toolbar, 0, 0);
        area.Controls.Add(_browserTabs, 0, 1);
        area.Controls.Add(_status, 0, 2);
        return area;
    }

    private Control BuildCompanionTabs()
    {
        _companionShell = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(130, 30),
            SizeMode = TabSizeMode.Fixed
        };
        _companionShell.DrawItem += DrawDarkTab;
        _companionShell.TabPages.Add(MakeTab("COMMANDER", BuildModernDashboardHost()));

        _classicTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(78, 29),
            SizeMode = TabSizeMode.Fixed,
            Multiline = true
        };
        _classicTabs.DrawItem += DrawDarkTab;
        _classicTabs.TabPages.Add(MakeTab("Coach", BuildCoach()));
        _classicTabs.TabPages.Add(MakeTab("Travel", BuildTravel()));
        _classicTabs.TabPages.Add(MakeTab("Crimes", BuildCrimes()));
        _classicTabs.TabPages.Add(MakeTab("Merits", BuildMerits()));
        _classicTabs.TabPages.Add(MakeTab("Money", BuildMoney()));
        _classicTabs.TabPages.Add(MakeTab("Cart", BuildShoppingCart()));
        _classicTabs.TabPages.Add(MakeTab("Losses", BuildLossSelling()));
        _classicTabs.TabPages.Add(MakeTab("Tools", BuildTools()));
        _classicTabs.TabPages.Add(MakeTab("Racing", BuildRacing()));
        _classicTabs.TabPages.Add(MakeTab("Casino", BuildCasino()));
        _classicTabs.TabPages.Add(MakeTab("Settings", BuildSettings()));
        _companionShell.TabPages.Add(MakeTab("CLASSIC TOOLS", _classicTabs));
        return _companionShell;
    }

    private TabPage MakeTab(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = PanelBg, ForeColor = TextMain, Padding = new Padding(4) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private Control BuildCoach()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("LIVE ACCOUNT COACH"));
        p.Controls.Add(Muted("This replaces the static guide. Full Sync reads your current Crimes 2.0 stats/perks and rebuilds the recommendation automatically."));

        _profileLabel = BigLabel("Profile: waiting for API…");
        _barsLabel = BigLabel("Nerve: waiting for API…");
        _capLabel = BigLabel("Nerve cap: —");
        _apiStatusLabel = Muted("API: not synced yet.");
        p.Controls.Add(_profileLabel);
        p.Controls.Add(_barsLabel);
        p.Controls.Add(_capLabel);
        p.Controls.Add(_apiStatusLabel);

        var syncRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        syncRow.Controls.Add(MakeButton("Fast Sync", async (_, _) => await RefreshFastAsync(true), 92));
        syncRow.Controls.Add(MakeButton("Full Sync", async (_, _) => await RefreshFullAsync(true), 92));
        syncRow.Controls.Add(MakeButton("Open Crimes", (_, _) => NavigateActive(TornRoutes.Crimes), 100));
        syncRow.Controls.Add(MakeButton("Ask AI", (_, _) => OpenAiWithContext(), 76));
        syncRow.Controls.Add(MakeButton("Record NNB", (_, _) => RecordNnbMilestone(), 92));
        syncRow.Controls.Add(MakeButton("NNB history", (_, _) => ShowNnbHistory(), 92));
        p.Controls.Add(syncRow);

        p.Controls.Add(Separator());
        _coachPhase = Heading("Phase: waiting for API");
        _coachHeadline = BigLabel("Save an API key in Settings, then press Full Sync.");
        _coachHeadline.ForeColor = Good;
        _coachWhy = Body("The recommendation will be generated from your actual NNB estimate, crime skills, progression bonuses, perks, refills and account state.");
        p.Controls.Add(_coachPhase);
        p.Controls.Add(_coachHeadline);
        p.Controls.Add(_coachWhy);

        _pageHint = Muted("Page-aware hint: open a Torn page and this area will follow what you're doing.");
        p.Controls.Add(_pageHint);

        p.Controls.Add(Heading("GUIDED ACTION"));
        _guideActionLabel = Body("Run Full Sync to generate a guided action from your current progression phase.");
        p.Controls.Add(_guideActionLabel);
        var guideActionRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        _guideActionModeCombo = new ComboBox
        {
            Width = 175,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = FieldBg,
            ForeColor = TextMain
        };
        _guideActionModeCombo.Items.AddRange(new object[] { "Advice only", "Open & Configure", "One-click single action" });
        _guideActionModeCombo.SelectedItem = NormalizeGuideActionMode(_settings.GuideActionMode);
        _guideActionModeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_guideActionModeCombo.SelectedItem is string mode)
            {
                _settings.GuideActionMode = mode;
                if (_guideActionModeSettingsCombo is not null && _guideActionModeSettingsCombo.SelectedItem?.ToString() != mode)
                    _guideActionModeSettingsCombo.SelectedItem = mode;
                Storage.SaveSettings(_settings);
                UpdateGuidedActionUi();
            }
        };
        guideActionRow.Controls.Add(_guideActionModeCombo);
        _guideActionButton = MakeButton("Do Recommended", async (_, _) => await ExecuteRecommendedActionAsync(false), 132);
        _guideOpenButton = MakeButton("Open Page", async (_, _) => await ExecuteRecommendedActionAsync(true), 92);
        guideActionRow.Controls.Add(_guideActionButton);
        guideActionRow.Controls.Add(_guideOpenButton);
        guideActionRow.Controls.Add(MakeButton("Copy Graffiti Debug", async (_, _) => await CopyGraffitiDebugAsync(), 145));
        p.Controls.Add(guideActionRow);
        p.Controls.Add(Muted("Open & Configure selects the recommended setup and leaves the final Torn action to you. One-click single action performs exactly one supported crime action per button press; there is no loop or unattended repeat."));

        p.Controls.Add(Heading("NERVE / NNB"));
        _coachNerve = Body("—");
        p.Controls.Add(_coachNerve);

        p.Controls.Add(Heading("LIVE PROGRESSION TRACKER"));
        _coachMetrics = Body("Full Sync will populate nerve spent, critical fails, current priority crime and API progression bonus.");
        p.Controls.Add(_coachMetrics);

        p.Controls.Add(Heading("EDUCATION / REHAB"));
        _coachEducation = Body("—");
        p.Controls.Add(_coachEducation);

        p.Controls.Add(Heading("ACCOUNT BONUSES"));
        _coachAccount = Body("—");
        p.Controls.Add(_coachAccount);

        p.Controls.Add(Heading("MERIT HUNTER"));
        _coachMerits = Body("Full Sync will scan Torn's current honor/medal catalogs against your account and rank easy unearned merits.");
        p.Controls.Add(_coachMerits);

        p.Controls.Add(Heading("NEXT ACTIONS"));
        _coachChecklist = new RichTextBox
        {
            Width = 475,
            Height = 180,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = FieldBg,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 9.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };
        p.Controls.Add(_coachChecklist);
        return p;
    }

    private Control BuildCrimes()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("CRIMES 2.0 — LIVE PROGRESSION"));
        p.Controls.Add(Muted("Pulled from Torn API v2 /torn/crimes + /user/{crimeId}/crimes. The Plan column changes with your NNB phase."));
        _crimeSyncLabel = Muted("No full crime sync yet.");
        p.Controls.Add(_crimeSyncLabel);

        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Full Sync", async (_, _) => await RefreshFullAsync(true), 90));
        row.Controls.Add(MakeButton("Open Crimes", (_, _) => NavigateActive(TornRoutes.Crimes), 100));
        row.Controls.Add(MakeButton("torn.report", async (_, _) => await AddBrowserTabAsync("https://torn.report/nerve"), 95));
        p.Controls.Add(row);

        _crimeGrid = MakeGrid(475, 485);
        _crimeGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        _crimeGrid.CellDoubleClick += (_, _) => NavigateActive(TornRoutes.Crimes);
        p.Controls.Add(_crimeGrid);
        p.Controls.Add(Muted("Double-click any row to open the Crimes page. Critical failures matter more than chasing a high unlocked crime too early."));
        return p;
    }

    private Control BuildMerits()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("MERIT HUNTER — LIVE ACCOUNT SCAN"));
        p.Controls.Add(Muted("Full Sync compares Torn's current honors + medals catalog with what your account has already earned. Commander ranks the remaining awards by estimated effort. Exact progress is shown only when the API exposes enough data; everything else is labeled as an estimate/requirement."));

        _meritSummaryLabel = BigLabel("Run Full Sync to scan awards.");
        _meritSpendLabel = Body("Merit spend advice will appear here.");
        _meritSpendLabel.ForeColor = Good;
        p.Controls.Add(_meritSummaryLabel);
        p.Controls.Add(_meritSpendLabel);

        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Full Sync", async (_, _) => await RefreshFullAsync(true), 90));
        row.Controls.Add(MakeButton("Open Awards", (_, _) => NavigateActive(TornRoutes.Awards), 100));
        row.Controls.Add(MakeButton("Guide Next", (_, _) => GuideNextMerit(), 92));
        row.Controls.Add(MakeButton("Explain Selected", (_, _) => ExplainSelectedMerit(), 116));
        row.Controls.Add(MakeButton("Copy Top 10", (_, _) => CopyMeritPlan(), 96));
        p.Controls.Add(row);

        var filters = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        filters.Controls.Add(new Label { Text = "Priority:", AutoSize = true, ForeColor = TextMain, Margin = new Padding(3, 8, 3, 3) });
        _meritModeCombo = new ComboBox
        {
            Width = 145,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = FieldBg,
            ForeColor = TextMain
        };
        _meritModeCombo.Items.AddRange(new object[] { "NNB Growth", "Easiest First", "Max Nerve" });
        _meritModeCombo.SelectedItem = new[] { "NNB Growth", "Easiest First", "Max Nerve" }.Contains(_settings.MeritHunterMode) ? _settings.MeritHunterMode : "NNB Growth";
        _meritModeCombo.SelectedIndexChanged += (_, _) =>
        {
            _settings.MeritHunterMode = _meritModeCombo.SelectedItem?.ToString() ?? "NNB Growth";
            Storage.SaveSettings(_settings);
            RebindMerits();
        };
        filters.Controls.Add(_meritModeCombo);

        filters.Controls.Add(new Label { Text = "Search:", AutoSize = true, ForeColor = TextMain, Margin = new Padding(10, 8, 3, 3) });
        _meritSearchBox = DarkTextBox();
        _meritSearchBox.Width = 145;
        _meritSearchBox.TextChanged += (_, _) => RebindMerits();
        filters.Controls.Add(_meritSearchBox);

        _meritShowAchieved = DarkCheck("Show earned", false);
        _meritShowAchieved.CheckedChanged += (_, _) => RebindMerits();
        filters.Controls.Add(_meritShowAchieved);
        _meritShowUnavailable = DarkCheck("Show legacy/unavailable", false);
        _meritShowUnavailable.CheckedChanged += (_, _) => RebindMerits();
        filters.Controls.Add(_meritShowUnavailable);
        p.Controls.Add(filters);

        _meritGrid = MakeGrid(475, 510);
        _meritGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _meritGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _meritGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _meritGrid.CellDoubleClick += (_, _) => OpenSelectedMerit();
        p.Controls.Add(_meritGrid);
        p.Controls.Add(Muted("Double-click an award to open the relevant Torn page. 'Requirement' is Torn's live catalog text. 'WhatToDo' translates it into plain-English steps when Commander knows the mechanic. Crimes-version awards are filtered using Torn's live crimes_version field; v1 crime medals are hidden from a Crimes 2.0 account unless you explicitly show legacy/unavailable. Progress is only shown when Commander can map the requirement to an exact API counter."));
        return p;
    }

    private Control BuildMoney()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("MONEY / TRADING"));
        _moneyApiLabel = BigLabel("Wallet: waiting for API permission…");
        p.Controls.Add(_moneyApiLabel);
        p.Controls.Add(Muted("Ledger first; automation stays manual. Use this to prove whether a flip is actually profitable."));

        var inputs = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = 475, BackColor = PanelBg };
        var item = DarkTextBox();
        var qty = Num(1, 1, 1_000_000);
        var buy = MoneyNum();
        var sell = MoneyNum();
        var note = DarkTextBox();
        AddLabeled(inputs, "Item", item);
        AddLabeled(inputs, "Quantity", qty);
        AddLabeled(inputs, "Buy each", buy);
        AddLabeled(inputs, "Sell each", sell);
        AddLabeled(inputs, "Notes", note);
        p.Controls.Add(inputs);

        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Add", (_, _) =>
        {
            _trades.Add(new TradeEntry
            {
                ProfileId = CurrentProfileId,
                Item = item.Text.Trim(), Quantity = (int)qty.Value, BuyEach = buy.Value,
                SellEach = sell.Value, Notes = note.Text.Trim()
            });
            Storage.Save(Storage.TradesPath, _trades);
            RebindTrades();
        }, 70));
        row.Controls.Add(MakeButton("Delete", (_, _) => DeleteSelectedTrade(), 74));
        row.Controls.Add(MakeButton("Bazaar", (_, _) => NavigateActive(TornRoutes.Bazaar), 74));
        row.Controls.Add(MakeButton("Items", (_, _) => NavigateActive(TornRoutes.Items), 68));
        p.Controls.Add(row);

        _tradeGrid = MakeGrid(475, 330);
        p.Controls.Add(_tradeGrid);
        RebindTrades();
        return p;
    }

    private Control BuildLossSelling()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("LOSS DESK — TWO ACCOUNT CONTROL"));
        p.Controls.Add(Muted("Commander keeps both Torn accounts isolated in separate profiles and coordinates the order in one window. Use the manual NEXT controls when you want one-action-at-a-time control, or the integrated Auto Cycle workers below for the old Loss Desk attack/heal loop."));

        p.Controls.Add(Heading("LOSS X-CHANGE"));
        p.Controls.Add(Body("Loss X-change is installed directly from the author's Greasy Fork build because its license forbids redistribution. Its API key/settings stay inside the current browser profile, so Account A and Account B can each have their own registration."));
        AddRemoteScriptTool(p, "Loss X-change", "Loss_X-change.user.js", "Contract browser, contributions and invoices inside Torn.", "https://update.greasyfork.org/scripts/595106/Loss%20X-change.user.js", "https://greasyfork.org/en/scripts/595106-loss-x-change");
        var xrow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        xrow.Controls.Add(MakeButton("Open Torn", (_, _) => NavigateActive(TornRoutes.Home), 82));
        xrow.Controls.Add(MakeButton("Open Loss X-change page", async (_, _) => await AddBrowserTabAsync("https://greasyfork.org/en/scripts/595106-loss-x-change"), 160));
        p.Controls.Add(xrow);

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("MANUAL / DIRECT LOSS ORDER"));
        p.Controls.Add(Muted("Create an order here for faction/private buyers or a contract you want to track yourself. Account A/B can work the same order without double-counting."));

        var inputs = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = 475, BackColor = PanelBg };
        _lossBuyerBox = DarkTextBox();
        _lossBuyerBox.PlaceholderText = "Buyer / contract name";
        _lossTargetBox = DarkTextBox();
        _lossTargetBox.PlaceholderText = "Target Torn ID";
        _lossRequested = Num(25, 1, 100000);
        _lossPay = MoneyNum(); _lossPay.Value = 350000;
        _lossProfileACombo = ProfileCombo();
        _lossProfileBCombo = ProfileCombo();
        if (_lossProfileBCombo.Items.Count > 1) _lossProfileBCombo.SelectedIndex = 1;
        AddLabeled(inputs, "Buyer / contract", _lossBuyerBox);
        AddLabeled(inputs, "Target player ID", _lossTargetBox);
        AddLabeled(inputs, "Requested losses", _lossRequested);
        AddLabeled(inputs, "Pay per loss", _lossPay);
        AddLabeled(inputs, "Account A", _lossProfileACombo);
        AddLabeled(inputs, "Account B", _lossProfileBCombo);
        p.Controls.Add(inputs);

        var createRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        createRow.Controls.Add(MakeButton("Create order", (_, _) => CreateLossOrder(), 100));
        createRow.Controls.Add(MakeButton("NEXT A", async (_, _) => await NextLossActionAsync(true), 82));
        createRow.Controls.Add(MakeButton("NEXT B", async (_, _) => await NextLossActionAsync(false), 82));
        createRow.Controls.Add(MakeButton("+1 A confirmed", (_, _) => AddLossProgress(true), 112));
        createRow.Controls.Add(MakeButton("+1 B confirmed", (_, _) => AddLossProgress(false), 112));
        p.Controls.Add(createRow);

        var controlRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        controlRow.Controls.Add(MakeButton("Heal A / Items", async (_, _) => await OpenLossItemsAsync(true), 105));
        controlRow.Controls.Add(MakeButton("Heal B / Items", async (_, _) => await OpenLossItemsAsync(false), 105));
        controlRow.Controls.Add(MakeButton("Pause / Resume", (_, _) => ToggleLossPause(), 105));
        controlRow.Controls.Add(MakeButton("FINISH + COMPLETE", (_, _) => CompleteLossOrder(), 135));
        controlRow.Controls.Add(MakeButton("Delete order", (_, _) => DeleteLossOrder(), 95));
        p.Controls.Add(controlRow);

        _lossGrid = MakeGrid(475, 265);
        p.Controls.Add(_lossGrid);
        RebindLossOrders();
        p.Controls.Add(Muted("NEXT A/B remains the manual workflow. Auto Cycle below uses separate embedded Commander WebView2 workers for Account A/B, so both profiles can stay alive without Edge/Chrome extensions."));

        BuildLossAutoCycleSection(p);

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("OTHER LOSS SELLING SERVICES"));
        AddService(p, "Hive Network", "Discord seller workflow / community loss marketplace.", "https://discord.gg/DZeRxenMHf", "https://www.torn.com/forums.php?p=threads&t=16480506");
        AddService(p, "24/7 Services", "Loss-selling marketplace; rates can change.", "https://discord.gg/fExMtG3vKW", "https://www.torn.com/forums.php?p=threads&t=16547903");
        AddService(p, "Nebula Services", "Established contract network; verify current provider rates.", "https://discord.gg/U5QdUz3QD6", "https://www.torn.com/forums.php?p=threads&t=16406219");
        AddService(p, "Phenix Co.", "Losses / escapes / bounties and other contracts.", "https://discord.gg/phenixco", "https://www.torn.com/forums.php?p=threads&t=16560135");
        return p;
    }

    private ComboBox ProfileCombo()
    {
        var c = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = FieldBg, ForeColor = TextMain };
        foreach (var profile in _settings.Profiles) c.Items.Add($"{profile.Name} [{profile.Id}]");
        if (c.Items.Count > 0) c.SelectedIndex = 0;
        return c;
    }

    private string ProfileIdFromCombo(ComboBox combo)
    {
        var idx = combo.SelectedIndex;
        return idx >= 0 && idx < _settings.Profiles.Count ? _settings.Profiles[idx].Id : CurrentProfileId;
    }

    private void CreateLossOrder()
    {
        var target = _lossTargetBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target) || !target.All(char.IsDigit))
        {
            MessageBox.Show("Enter the target's numeric Torn ID.", "Loss Desk");
            return;
        }
        var order = new LossOrder
        {
            TargetPlayerId = target,
            BuyerName = string.IsNullOrWhiteSpace(_lossBuyerBox.Text) ? target : _lossBuyerBox.Text.Trim(),
            RequestedLosses = (int)_lossRequested.Value,
            PayPerLoss = (long)_lossPay.Value,
            ProfileAId = ProfileIdFromCombo(_lossProfileACombo),
            ProfileBId = ProfileIdFromCombo(_lossProfileBCombo)
        };
        _lossOrders.Insert(0, order);
        SaveLossOrders();
        RebindLossOrders();
        FlashStatus($"Loss order {order.Id} created: {order.RequestedLosses} losses.");
    }

    private LossOrder? SelectedLossOrder()
    {
        if (_lossGrid?.SelectedRows.Count > 0)
        {
            var id = _lossGrid.SelectedRows[0].Cells["Id"]?.Value?.ToString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                var hit = _lossOrders.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
        }
        return _lossOrders.FirstOrDefault(x => x.Status != "Complete");
    }

    private async Task NextLossActionAsync(bool accountA)
    {
        var order = SelectedLossOrder();
        if (order is null) { MessageBox.Show("Create or select an order first.", "Loss Desk"); return; }
        if (order.Status == "Paused") { MessageBox.Show("This order is paused. Resume it first.", "Loss Desk"); return; }
        if (order.Remaining <= 0) { MessageBox.Show("Requested losses are already complete. Use FINISH + COMPLETE.", "Loss Desk"); return; }
        var profileId = accountA ? order.ProfileAId : order.ProfileBId;
        if (!profileId.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase)) await SwitchProfileAsync(profileId);
        var next = accountA ? order.NextA : order.NextB;
        if (next.Equals("Heal", StringComparison.OrdinalIgnoreCase))
        {
            NavigateActive(TornRoutes.Items);
            if (accountA) order.NextA = "Attack"; else order.NextB = "Attack";
            SaveLossOrders(); RebindLossOrders();
            FlashStatus($"{(accountA ? "A" : "B")}: Items opened for manual FAK/heal. After healing, NEXT {(accountA ? "A" : "B")} returns to the target.");
            return;
        }
        NavigateActive($"https://www.torn.com/profiles.php?XID={order.TargetPlayerId}");
        FlashStatus($"{(accountA ? "A" : "B")}: target opened. Make ONE deliberate loss, confirm Torn's result, then record +1.");
    }

    private async Task OpenLossItemsAsync(bool accountA)
    {
        var order = SelectedLossOrder();
        var profileId = order is null ? CurrentProfileId : (accountA ? order.ProfileAId : order.ProfileBId);
        if (!profileId.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase)) await SwitchProfileAsync(profileId);
        NavigateActive(TornRoutes.Items);
        FlashStatus($"{(accountA ? "A" : "B")}: Items opened for manual healing/recovery.");
    }

    private void AddLossProgress(bool accountA)
    {
        var order = SelectedLossOrder();
        if (order is null) return;
        if (order.Status == "Paused") { FlashStatus("Order is paused."); return; }
        if (order.Remaining <= 0) { FlashStatus("Order is already at the requested total."); return; }
        if (accountA) { order.CompletedA++; order.NextA = "Heal"; }
        else { order.CompletedB++; order.NextB = "Heal"; }
        if (order.Remaining == 0) order.Status = "Ready to finish";
        SaveLossOrders();
        RebindLossOrders();
        FlashStatus($"Recorded {(accountA ? "A" : "B")} loss. {order.CompletedTotal}/{order.RequestedLosses} complete.");
    }

    private void ToggleLossPause()
    {
        var order = SelectedLossOrder();
        if (order is null) return;
        order.Status = order.Status == "Paused" ? (order.Remaining == 0 ? "Ready to finish" : "Active") : "Paused";
        SaveLossOrders(); RebindLossOrders();
    }

    private void CompleteLossOrder()
    {
        var order = SelectedLossOrder();
        if (order is null) return;
        order.Status = "Complete";
        SaveLossOrders(); RebindLossOrders();
        FlashStatus($"Order {order.Id} completed. Gross tracked: ${order.Gross:N0}.");
    }

    private void DeleteLossOrder()
    {
        var order = SelectedLossOrder();
        if (order is null) return;
        if (MessageBox.Show($"Delete loss order {order.Id}?", "Loss Desk", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _lossOrders.Remove(order); SaveLossOrders(); RebindLossOrders();
    }

    private void SaveLossOrders() => Storage.SaveAtomic(Storage.LossOrdersPath, _lossOrders);

    private void RebindLossOrders()
    {
        if (_lossGrid is null) return;
        _lossGrid.DataSource = null;
        _lossGrid.DataSource = _lossOrders.Select(x => new
        {
            x.Id,
            Buyer = x.BuyerName,
            Target = x.TargetPlayerId,
            Progress = $"{x.CompletedTotal}/{x.RequestedLosses}",
            A = x.CompletedA,
            NextA = x.NextA,
            B = x.CompletedB,
            NextB = x.NextB,
            Remaining = x.Remaining,
            Pay = x.PayPerLoss,
            Gross = x.Gross,
            x.Status
        }).ToList();
        if (_lossGrid.Columns.Count > 0) _lossGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
    }

    private void AddService(FlowLayoutPanel p, string name, string description, string discord, string forum)
    {
        p.Controls.Add(Heading(name));
        p.Controls.Add(Body(description));
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Discord", async (_, _) => await AddBrowserTabAsync(discord), 78));
        row.Controls.Add(MakeButton("Forum", async (_, _) => await AddBrowserTabAsync(forum), 72));
        p.Controls.Add(row);
    }

    private Control BuildTools()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("COMMANDER TOOLS"));
        p.Controls.Add(Muted("This page now separates tools that are actually running inside Commander from external dashboards. Installed scripts execute in Torn tabs through Commander's built-in userscript runner; you do not need Greasemonkey for these."));

        p.Controls.Add(Heading("BUILT-IN / ACTIVE MODULES"));
        AddManagedLocalTool(p, "Graffiti Paint + Guided Action", "Torn_Graffiti_Paint_Helper.user.js", "Paint charge display, recommendation and guided one-action setup.");
        AddManagedLocalTool(p, "Blackjack Assist", "Torn_Blackjack_Assist.user.js", "Basic-strategy overlay from the userscript you supplied.");

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("ONE-CLICK USERSCRIPT INSTALLERS"));
        p.Controls.Add(Muted("Install/Update downloads the current script directly from the author's Greasy Fork update URL into Commander's script manager, then enables it. No separate browser extension is required. Scripts with common GM_* calls use Commander's compatibility shims."));
        AddRemoteScriptTool(p, "Lazy Crimes", "Lazy_Crimes.user.js", "Crimes 2.0 quality-of-life controls.", "https://update.greasyfork.org/scripts/540604/Lazy%20Crimes.user.js", "https://greasyfork.org/en/scripts/540604-lazy-crimes");
        AddRemoteScriptTool(p, "Torn Racing Guide", "Torn_Racing_Guide.user.js", "Track/car overlay and racing progression helper.", "https://update.greasyfork.org/scripts/569449/Torn%20Racing%20Guide.user.js", "https://greasyfork.org/en/scripts/569449-torn-racing-guide");
        AddRemoteScriptTool(p, "Simple Cracking Helper", "Torn_Cracking_Helper.user.js", "Cracking password helper for your later NNB phase.", "https://update.greasyfork.org/scripts/544469/torn-crack.user.js", "https://greasyfork.org/en/scripts/544469-torn-crack");
        AddRemoteScriptTool(p, "Loss X-change", "Loss_X-change.user.js", "Loss-selling contracts, contributions and invoices. Downloaded from the author on demand; not redistributed in Commander.", "https://update.greasyfork.org/scripts/595106/Loss%20X-change.user.js", "https://greasyfork.org/en/scripts/595106-loss-x-change");

        var packRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        packRow.Controls.Add(MakeButton("Install / update recommended pack", async (_, _) => await InstallRecommendedToolPackAsync(), 220));
        packRow.Controls.Add(MakeButton("Open scripts folder", (_, _) => OpenFolder(Storage.UserScriptsPath), 135));
        packRow.Controls.Add(MakeButton("Load unpacked extension", async (_, _) => await LoadExtensionAsync(), 170));
        p.Controls.Add(packRow);

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("EXTERNAL DASHBOARDS"));
        p.Controls.Add(Muted("These are websites/services, not installed plug-ins, so Commander opens them as tabs instead of pretending they are local modules."));
        AddExternalDashboard(p, "torn.report", "Nerve/crime history and chain tracking.", "https://torn.report/nerve");
        AddExternalDashboard(p, "YATA", "Faction/account helper suite.", "https://yata.yt/");
        AddExternalDashboard(p, "Torn API Playground", "Inspect the exact v2 endpoint data Commander can use.", "https://api-playground.torn.com/");
        AddExternalDashboard(p, "TornTools", "TornTools is a browser extension rather than a normal userscript. Use Load unpacked extension if you have an unpacked copy; the forum button opens its current project page.", "https://www.torn.com/forums.php?p=threads&t=16243863");

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("CUSTOM LINKS"));
        p.Controls.Add(Muted("Add any helper/service you decide you need. Opening an unknown domain will still offer Allow once / Always allow / Edge / Block."));
        _customLinksList = new ListBox { Width = 475, Height = 130, BackColor = FieldBg, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        p.Controls.Add(_customLinksList);
        var customRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        customRow.Controls.Add(MakeButton("Add link", (_, _) => AddCustomLink(), 78));
        customRow.Controls.Add(MakeButton("Open", async (_, _) => await OpenSelectedCustomLinkAsync(), 70));
        customRow.Controls.Add(MakeButton("Delete", (_, _) => DeleteSelectedCustomLink(), 70));
        p.Controls.Add(customRow);
        RefreshCustomLinks();
        return p;
    }

    private void AddManagedLocalTool(FlowLayoutPanel p, string displayName, string fileName, string description)
    {
        p.Controls.Add(Heading(displayName));
        var status = new Label { AutoSize = true, ForeColor = TextMuted, BackColor = PanelBg };
        void RefreshStatus()
        {
            var installed = File.Exists(Path.Combine(Storage.UserScriptsPath, fileName));
            var enabled = _settings.EnabledScripts.Contains(fileName, StringComparer.OrdinalIgnoreCase);
            status.Text = installed ? (enabled ? "● Installed + enabled" : "○ Installed, disabled") : "× Missing";
            status.ForeColor = installed && enabled ? Good : (installed ? Warn : Bad);
        }
        p.Controls.Add(Body(description));
        p.Controls.Add(status);
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Enable", async (_, _) => { await SetScriptEnabled(fileName, true); RefreshStatus(); }, 72));
        row.Controls.Add(MakeButton("Disable", async (_, _) => { await SetScriptEnabled(fileName, false); RefreshStatus(); }, 72));
        row.Controls.Add(MakeButton("Open folder", (_, _) => OpenFolder(Storage.UserScriptsPath), 90));
        p.Controls.Add(row);
        RefreshStatus();
    }

    private void AddRemoteScriptTool(FlowLayoutPanel p, string displayName, string fileName, string description, string downloadUrl, string projectUrl)
    {
        p.Controls.Add(Heading(displayName));
        p.Controls.Add(Body(description));
        var status = new Label { AutoSize = true, ForeColor = TextMuted, BackColor = PanelBg };
        void RefreshStatus()
        {
            var installed = File.Exists(Path.Combine(Storage.UserScriptsPath, fileName));
            var enabled = _settings.EnabledScripts.Contains(fileName, StringComparer.OrdinalIgnoreCase);
            status.Text = installed ? (enabled ? "● Installed + enabled" : "○ Installed, disabled") : "Not installed";
            status.ForeColor = installed && enabled ? Good : (installed ? Warn : TextMuted);
        }
        p.Controls.Add(status);
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Install / Update", async (_, _) => { await InstallRemoteUserscriptAsync(displayName, fileName, downloadUrl); RefreshStatus(); }, 112));
        row.Controls.Add(MakeButton("Enable", async (_, _) => { await SetScriptEnabled(fileName, true); RefreshStatus(); }, 68));
        row.Controls.Add(MakeButton("Disable", async (_, _) => { await SetScriptEnabled(fileName, false); RefreshStatus(); }, 70));
        row.Controls.Add(MakeButton("Project", async (_, _) => await AddBrowserTabAsync(projectUrl), 72));
        p.Controls.Add(row);
        RefreshStatus();
    }

    private void AddExternalDashboard(FlowLayoutPanel p, string name, string description, string url)
    {
        p.Controls.Add(Heading(name));
        p.Controls.Add(Body(description));
        p.Controls.Add(MakeButton("Open dashboard", async (_, _) => await AddBrowserTabAsync(url), 110));
    }

    private async Task SetScriptEnabled(string fileName, bool enabled)
    {
        _settings.EnabledScripts.RemoveAll(x => x.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (enabled && File.Exists(Path.Combine(Storage.UserScriptsPath, fileName)))
            _settings.EnabledScripts.Add(fileName);
        Storage.SaveSettings(_settings);
        RefreshScriptsList();
        await ApplyScriptsToOpenTabsAsync();
        FlashStatus($"{fileName}: {(enabled ? "enabled" : "disabled")}. Open Torn tabs refreshed.");
    }

    private async Task ApplyScriptsToOpenTabsAsync()
    {
        foreach (var tab in _browserTabObjects.ToList())
        {
            try { await tab.ApplyUserscriptsAsync(); }
            catch (Exception ex) { FlashStatus("Could not refresh helper scripts: " + ex.Message); }
        }
    }

    private async Task<bool> InstallRemoteUserscriptAsync(string displayName, string fileName, string downloadUrl, bool quiet = false)
    {
        try
        {
            FlashStatus($"Downloading {displayName} from its author…");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TornCommander/0.6.33");
            var script = await http.GetStringAsync(downloadUrl);
            if (!script.Contains("==UserScript==", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded file did not look like a userscript.");
            Directory.CreateDirectory(Storage.UserScriptsPath);
            await File.WriteAllTextAsync(Path.Combine(Storage.UserScriptsPath, fileName), script);
            await SetScriptEnabled(fileName, true);
            if (!quiet) MessageBox.Show($"{displayName} was installed/updated and enabled. Matching open tabs have been refreshed. Look for the helper on its Torn page.", "Commander Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            FlashStatus($"Installed {displayName}.");
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet) MessageBox.Show($"Could not install {displayName}:\n\n{ex.Message}", "Commander Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            FlashStatus($"Install failed: {displayName} — {ex.Message}");
            return false;
        }
    }

    private async Task InstallRecommendedToolPackAsync()
    {
        var results = new List<string>();
        if (await InstallRemoteUserscriptAsync("Lazy Crimes", "Lazy_Crimes.user.js", "https://update.greasyfork.org/scripts/540604/Lazy%20Crimes.user.js", true)) results.Add("Lazy Crimes");
        if (await InstallRemoteUserscriptAsync("Torn Racing Guide", "Torn_Racing_Guide.user.js", "https://update.greasyfork.org/scripts/569449/Torn%20Racing%20Guide.user.js", true)) results.Add("Torn Racing Guide");
        if (await InstallRemoteUserscriptAsync("Simple Cracking Helper", "Torn_Cracking_Helper.user.js", "https://update.greasyfork.org/scripts/544469/torn-crack.user.js", true)) results.Add("Simple Cracking Helper");
        if (await InstallRemoteUserscriptAsync("Loss X-change", "Loss_X-change.user.js", "https://update.greasyfork.org/scripts/595106/Loss%20X-change.user.js", true)) results.Add("Loss X-change");
        RefreshScriptsList();
        MessageBox.Show(results.Count > 0
            ? "Installed/updated and enabled:\n\n• " + string.Join("\n• ", results) + "\n\nReload matching Torn tabs to activate them."
            : "No scripts were installed. Check your internet connection and try the individual Install / Update buttons.",
            "Commander Recommended Pack", MessageBoxButtons.OK, results.Count > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private Control BuildRacing()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("RACING COMMAND"));
        _racingSkillLabel = BigLabel("Racing skill: waiting for API…");
        p.Controls.Add(_racingSkillLabel);
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Open Racing", (_, _) => NavigateActive(TornRoutes.Racing), 100));
        row.Controls.Add(MakeButton("Full Sync", async (_, _) => await RefreshFullAsync(true), 90));
        p.Controls.Add(row);

        p.Controls.Add(Heading("TRACK / CAR NOTES"));
        _racingNotes = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 475,
            Height = 380,
            BackColor = FieldBg,
            ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle,
            Text = Storage.Load(Storage.RacingPath, new RacingNote()).Notes
        };
        p.Controls.Add(_racingNotes);
        p.Controls.Add(MakeButton("Save notes", (_, _) =>
        {
            Storage.Save(Storage.RacingPath, new RacingNote { Notes = _racingNotes.Text });
            FlashStatus("Racing notes saved.");
        }, 100));
        p.Controls.Add(Muted("The API module already syncs racing skill. Future builds can expand this into per-track/car performance without automating race entry."));
        return p;
    }

    private Control BuildCasino()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("CASINO TRACKER"));
        p.Controls.Add(Muted("Session tracking/calculators only. No auto-betting or unattended gambling."));
        var inputs = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = 475, BackColor = PanelBg };
        var game = DarkTextBox("Blackjack");
        var start = MoneyNum();
        var end = MoneyNum();
        var note = DarkTextBox();
        AddLabeled(inputs, "Game", game);
        AddLabeled(inputs, "Starting bankroll", start);
        AddLabeled(inputs, "Ending bankroll", end);
        AddLabeled(inputs, "Notes", note);
        p.Controls.Add(inputs);

        var row = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        row.Controls.Add(MakeButton("Add session", (_, _) =>
        {
            _casino.Add(new CasinoSession { Game = game.Text.Trim(), StartBankroll = start.Value, EndBankroll = end.Value, Notes = note.Text.Trim() });
            Storage.Save(Storage.CasinoPath, _casino);
            RebindCasino();
        }, 100));
        row.Controls.Add(MakeButton("Delete", (_, _) => DeleteSelectedCasino(), 72));
        row.Controls.Add(MakeButton("Open Casino", (_, _) => NavigateActive(TornRoutes.Casino), 100));
        p.Controls.Add(row);

        _casinoGrid = MakeGrid(475, 350);
        p.Controls.Add(_casinoGrid);
        RebindCasino();
        p.Controls.Add(Muted("Set the session bankroll before opening a table. Log the result instead of trying to win back a loss because the graph annoyed you."));
        return p;
    }

    private Control BuildSettings()
    {
        var p = MakeFlow();
        p.Controls.Add(Heading("SETTINGS"));
        p.Controls.Add(Muted("Layout tip: drag the vertical divider between Torn and Commander to make either side wider. Double-click the divider to reset it. The width is remembered when you close Commander."));

        p.Controls.Add(Heading("ACCOUNT PROFILES"));
        p.Controls.Add(Body("Each Commander profile has its own Torn login/cookies, WebView2 browser storage and saved Torn API key. Use the profile selector in the top toolbar to jump between accounts without Edge/Chrome fighting each other."));
        var profileRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        profileRow.Controls.Add(MakeButton("Add profile", async (_, _) => await AddProfileAsync(), 95));
        profileRow.Controls.Add(MakeButton("Rename current", (_, _) => RenameCurrentProfile(), 112));
        profileRow.Controls.Add(MakeButton("Delete current", async (_, _) => await DeleteCurrentProfileAsync(), 108));
        profileRow.Controls.Add(MakeButton("Open current home", (_, _) => NavigateActive(TornRoutes.Home), 125));
        p.Controls.Add(profileRow);
        p.Controls.Add(Muted("Current profile: " + CurrentProfile.Name + " [" + CurrentProfileId + "]. Loss X-change and other userscript storage also stays isolated because each profile has separate browser data."));

        p.Controls.Add(Heading("TORN API KEY — CURRENT PROFILE"));
        p.Controls.Add(Muted("Commander saves a separate Full Access Torn API key for each profile. Torn's API is read-only. Enter each account key once; Windows Credential Manager reuses the correct key whenever you switch profiles."));
        _apiKeyBox = DarkTextBox();
        _apiKeyBox.UseSystemPasswordChar = true;
        _apiKeyBox.Width = 360;
        _apiKeyBox.Text = CredentialManager.LoadApiKey(CurrentProfileId) ?? "";
        p.Controls.Add(_apiKeyBox);
        var apiRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        apiRow.Controls.Add(MakeButton("Create Full Access key", async (_, _) => await AddBrowserTabAsync(TornRoutes.FullAccessApiKey), 150));
        apiRow.Controls.Add(MakeButton("Paste + Save", (_, _) => PasteAndSaveApiKey(), 105));
        apiRow.Controls.Add(MakeButton("Save", (_, _) => SaveApiKey(), 65));
        apiRow.Controls.Add(MakeButton("Verify Full Access", async (_, _) => await VerifyFullAccessAsync(), 125));
        apiRow.Controls.Add(MakeButton("Show/Hide", (_, _) => _apiKeyBox.UseSystemPasswordChar = !_apiKeyBox.UseSystemPasswordChar, 90));
        apiRow.Controls.Add(MakeButton("Forget", (_, _) => ForgetApiKey(), 70));
        p.Controls.Add(apiRow);
        p.Controls.Add(Muted("How: click Create Full Access key → approve it in Torn → copy the new 16-character key → click Paste + Save → Full Sync. Existing restricted keys cannot be upgraded in place; replace the saved key."));

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("LIVE COACH"));
        var settingsGrid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Width = 475, BackColor = PanelBg };
        _addictionPercent = Num(_settings.ManualAddictionPercent, -100, 0);
        _nnbOverride = Num(_settings.ManualNaturalNerveOverride, 0, 200);
        _fastRefreshSeconds = Num(_settings.FastRefreshSeconds, 60, 3600);
        _fullRefreshSeconds = Num(_settings.FullRefreshSeconds, 300, 7200);
        _autoRefreshCheck = DarkCheck("Auto-refresh API", _settings.AutoRefreshApi);
        _restoreTabsCheck = DarkCheck("Restore browser tabs at startup", _settings.RestoreTabsOnStartup);
        _rememberWindowCheck = DarkCheck("Remember window size and position", _settings.RememberWindowPlacement);
        AddLabeled(settingsGrid, "Drug addiction % (fallback)", _addictionPercent);
        AddLabeled(settingsGrid, "NNB override (0 = auto)", _nnbOverride);
        AddLabeled(settingsGrid, "Fast sync seconds", _fastRefreshSeconds);
        AddLabeled(settingsGrid, "Full sync seconds", _fullRefreshSeconds);
        _guideActionModeSettingsCombo = new ComboBox
        {
            Width = 175,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = FieldBg,
            ForeColor = TextMain
        };
        _guideActionModeSettingsCombo.Items.AddRange(new object[] { "Advice only", "Open & Configure", "One-click single action" });
        _guideActionModeSettingsCombo.SelectedItem = NormalizeGuideActionMode(_settings.GuideActionMode);
        _guideActionModeSettingsCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_guideActionModeSettingsCombo.SelectedItem is string mode)
            {
                _settings.GuideActionMode = mode;
                if (_guideActionModeCombo is not null && _guideActionModeCombo.SelectedItem?.ToString() != mode)
                    _guideActionModeCombo.SelectedItem = mode;
            }
        };
        AddLabeled(settingsGrid, "Guide action mode", _guideActionModeSettingsCombo);
        settingsGrid.Controls.Add(_autoRefreshCheck, 1, settingsGrid.RowCount++);
        settingsGrid.Controls.Add(_restoreTabsCheck, 1, settingsGrid.RowCount++);
        settingsGrid.Controls.Add(_rememberWindowCheck, 1, settingsGrid.RowCount++);
        p.Controls.Add(settingsGrid);
        p.Controls.Add(MakeButton("Save settings", (_, _) => SaveSettingsFromUi(), 110));
        p.Controls.Add(Muted("Fast sync = bars/refill/profile. Full sync = perks, merits, education, work stats and all Crimes 2.0 statistics. If company employee data exposes your addiction penalty, Commander auto-detects it; this field is the fallback. NNB override is there if bonus parsing is ambiguous."));

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("WHITELIST"));
        _allowHelperSitesCheck = DarkCheck("Allow built-in helper sites", _settings.AllowHelperSites);
        p.Controls.Add(_allowHelperSitesCheck);
        _whitelistDomainBox = DarkTextBox();
        _whitelistDomainBox.Width = 300;
        _whitelistDomainBox.PlaceholderText = "example.com";
        var whitelistInput = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        whitelistInput.Controls.Add(_whitelistDomainBox);
        whitelistInput.Controls.Add(MakeButton("Add", (_, _) => AddWhitelistDomainFromUi(), 60));
        whitelistInput.Controls.Add(MakeButton("Remove", (_, _) => RemoveSelectedWhitelistDomain(), 72));
        p.Controls.Add(whitelistInput);
        _whitelistList = new ListBox { Width = 475, Height = 115, BackColor = FieldBg, ForeColor = TextMain, BorderStyle = BorderStyle.FixedSingle };
        p.Controls.Add(_whitelistList);
        p.Controls.Add(Muted("Whitelist means 'allow this site inside Commander.' It cannot bypass site security, Torn rules, CAPTCHAs or authentication restrictions."));

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("LOCAL USERSCRIPTS"));
        p.Controls.Add(Muted("Use Import userscript for .user.js, .js, or even a .txt file containing a userscript header. Commander copies it into its persistent Scripts folder and can enable it immediately. @match/@include metadata is honored; @grant none scripts are the most compatible."));
        _scriptsList = new CheckedListBox
        {
            Width = 475,
            Height = 130,
            BackColor = FieldBg,
            ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true
        };
        p.Controls.Add(_scriptsList);
        var scriptRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        scriptRow.Controls.Add(MakeButton("Import userscript", (_, _) => ImportUserscript(), 120));
        scriptRow.Controls.Add(MakeButton("Refresh", (_, _) => RefreshScriptsList(), 75));
        scriptRow.Controls.Add(MakeButton("Scripts folder", (_, _) => OpenFolder(Storage.UserScriptsPath), 110));
        scriptRow.Controls.Add(MakeButton("Save enabled", (_, _) => SaveEnabledScripts(), 105));
        p.Controls.Add(scriptRow);

        p.Controls.Add(Separator());
        p.Controls.Add(Heading("DATA / MAINTENANCE"));
        var dataRow = new FlowLayoutPanel { AutoSize = true, BackColor = PanelBg, WrapContents = true };
        dataRow.Controls.Add(MakeButton("Data folder", (_, _) => OpenFolder(Storage.Root), 95));
        dataRow.Controls.Add(MakeButton("Export backup", (_, _) => ExportBackup(), 105));
        dataRow.Controls.Add(MakeButton("Copy diagnostics", (_, _) => CopyDiagnostics(), 115));
        dataRow.Controls.Add(MakeButton("Load extension", async (_, _) => await LoadExtensionAsync(), 110));
        p.Controls.Add(dataRow);

        RefreshWhitelistList();
        RefreshScriptsList();
        return p;
    }

    private async Task InitializeAsync()
    {
        Program.TraceStartup("InitializeAsync started.");
        try
        {
            Text = $"Torn Commander v0.6.33 — {CurrentProfile.Name}";
            FlashStatus($"Starting browser profile: {CurrentProfile.Name}…");
            Program.TraceStartup("Creating WebView2 environment.");
            _webEnv = await GetWebEnvironmentAsync(CurrentProfileId);
            Program.TraceStartup("WebView2 environment created; loading dashboard.");
            await InitializeModernDashboardAsync();
            Program.TraceStartup("Dashboard setup completed.");

            // v0.2 deliberately starts clean unless the user explicitly asks to restore tabs.
            // Every saved Torn loader.php route is normalized to the current page.php format.
            var profileTabs = CurrentProfile.OpenTabs.Count > 0 ? CurrentProfile.OpenTabs : _settings.OpenTabs;
            var urls = _settings.RestoreTabsOnStartup
                ? profileTabs
                    .Where(u => Uri.TryCreate(TornRoutes.Normalize(u), UriKind.Absolute, out _))
                    .Select(TornRoutes.Normalize)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList()
                : new List<string> { TornRoutes.Home };

            if (urls.Count == 0) urls.Add(TornRoutes.Home);
            foreach (var url in urls)
                await AddBrowserTabAsync(url);

            StartTimers();
            UpdateAllUi();
            Program.TraceStartup("Tabs loaded and UI updated.");
            if (_travelForecast.RetrievedAtUtc == DateTime.MinValue || DateTime.UtcNow - _travelForecast.RetrievedAtUtc > TimeSpan.FromMinutes(20))
                _ = RefreshTravelForecastAsync(false);

            if (!string.IsNullOrWhiteSpace(CredentialManager.LoadApiKey(CurrentProfileId)))
            {
                await RefreshFastAsync(false);
                // Do one full sync shortly after startup so the coach becomes account-aware.
                await RefreshFullAsync(false);
            }
            else
            {
                _apiStatusLabel.Text = "API: key not saved yet — Settings → Torn API Key.";
            }

            Program.TraceStartup("Initialization ready.");
            FlashStatus("Ready — Ctrl+T new tab • Ctrl+W close tab • Ctrl+L address");
        }
        catch (Exception ex)
        {
            Program.TraceStartup("InitializeAsync failed: " + ex.GetType().Name + ": " + ex.Message);
            MessageBox.Show(
                "Torn Commander could not initialize the Edge WebView2 browser.\n\n" +
                "Windows 11 normally includes the runtime with Microsoft Edge. Update Edge/WebView2 and try again.\n\n" +
                ex.Message,
                "Torn Commander startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StartTimers()
    {
        _fastTimer?.Stop();
        _fastTimer?.Dispose();
        _fullTimer?.Stop();
        _fullTimer?.Dispose();
        _uiTimer?.Stop();
        _uiTimer?.Dispose();

        _fastTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(60, _settings.FastRefreshSeconds) * 1000,
            Enabled = _settings.AutoRefreshApi
        };
        _fastTimer.Tick += async (_, _) => await RefreshFastAsync(false);

        _fullTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(300, _settings.FullRefreshSeconds) * 1000,
            Enabled = _settings.AutoRefreshApi
        };
        _fullTimer.Tick += async (_, _) => await RefreshFullAsync(false);

        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true };
        _uiTimer.Tick += (_, _) =>
        {
            UpdateCapTimer();
            UpdatePageHint();
            TickTravelMonitor();
            TickCompanyCollection();
        };
    }

    private async Task AddBrowserTabAsync(string url)
    {
        if (_webEnv is null) return;

        var normalized = TornRoutes.Normalize(url);
        BrowserTab? bt = null;
        bt = new BrowserTab(
            normalized,
            _settings,
            u => BeginInvoke(new Action(async () => await AddBrowserTabAsync(u))),
            s => BeginInvoke(new Action(() => HandleBrowserMessage(s))),
            _ => BeginInvoke(new Action(() =>
            {
                if (bt is not null && Active() == bt)
                {
                    SyncAddressFromActive();
                    UpdatePageHint();
                }
            })));

        _browserTabObjects.Add(bt);
        _browserTabs.TabPages.Add(bt.Page);
        _browserTabs.SelectedTab = bt.Page;
        SyncAddressFromActive();

        try
        {
            await bt.InitializeAsync(_webEnv);
        }
        catch (Exception ex)
        {
            FlashStatus("Tab failed to initialize: " + ex.Message);
            MessageBox.Show(ex.Message, "Browser tab error");
        }
    }

    private BrowserTab? Active()
    {
        if (_browserTabs.SelectedTab is null) return null;
        return _browserTabObjects.FirstOrDefault(x => x.Page == _browserTabs.SelectedTab);
    }

    private void SyncAddressFromActive()
    {
        var active = Active();
        var url = active?.WebView.Source?.ToString();
        if (string.IsNullOrWhiteSpace(url)) url = active?.InitialUrl;
        _address.Text = url ?? "";
        UpdatePageHint();
    }

    private void GoAddress()
    {
        var text = _address.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        NavigateActive(TornRoutes.Normalize(text));
    }

    private void NavigateActive(string url)
    {
        var normalized = TornRoutes.Normalize(url);
        var active = Active();
        if (active is null)
        {
            _ = AddBrowserTabAsync(normalized);
            return;
        }
        try
        {
            active.WebView.Source = new Uri(normalized);
        }
        catch
        {
            FlashStatus("Invalid address.");
        }
    }

    private void OpenQuickRoute()
    {
        var name = _quickRoute.SelectedItem?.ToString() ?? "Crimes";
        var url = name switch
        {
            "Crimes" => TornRoutes.Crimes,
            "Travel" => "https://www.torn.com/travelagency.php",
            "Racing" => TornRoutes.Racing,
            "Education" => TornRoutes.Education,
            "Faction" => TornRoutes.Faction,
            "Company" => TornRoutes.Company,
            "Gym" => TornRoutes.Gym,
            "Items" => TornRoutes.Items,
            "Bazaar" => TornRoutes.Bazaar,
            "Casino" => TornRoutes.Casino,
            "ChatGPT" => "https://chatgpt.com/",
            "Google sign-in" => "https://accounts.google.com/",
            "Discord" => "https://discord.com/app",
            "torn.report" => "https://torn.report/nerve",
            "YATA" => "https://yata.yt/",
            _ => TornRoutes.Home
        };

        // Keep navigation predictable: game pages reuse the current tab; helper/auth pages open a new one.
        if (TornRoutes.IsTornHost(new Uri(url).Host)) NavigateActive(url);
        else _ = AddBrowserTabAsync(url);
    }

    private void CloseBrowserTab(int index)
    {
        if (index < 0 || index >= _browserTabs.TabPages.Count) return;
        var page = _browserTabs.TabPages[index];
        var obj = _browserTabObjects.FirstOrDefault(x => x.Page == page);

        _browserTabs.TabPages.Remove(page);
        if (obj is not null)
        {
            _browserTabObjects.Remove(obj);
            obj.Dispose();
        }
        else
        {
            page.Dispose();
        }

        if (_browserTabs.TabPages.Count == 0)
        {
            _address.Text = "";
            FlashStatus("No browser tabs open. Press + or Ctrl+T for a new tab.");
        }
        else
        {
            _browserTabs.SelectedIndex = Math.Clamp(index - 1, 0, _browserTabs.TabPages.Count - 1);
            SyncAddressFromActive();
        }
    }

    private void CloseOtherTabs(int keepIndex)
    {
        if (keepIndex < 0 || keepIndex >= _browserTabs.TabPages.Count) return;
        for (var i = _browserTabs.TabPages.Count - 1; i >= 0; i--)
            if (i != keepIndex) CloseBrowserTab(i);
    }

    private void CloseAllTabs()
    {
        for (var i = _browserTabs.TabPages.Count - 1; i >= 0; i--)
            CloseBrowserTab(i);
    }

    private void OpenTabInEdge(int index)
    {
        if (index < 0 || index >= _browserTabs.TabPages.Count) return;
        var obj = _browserTabObjects.FirstOrDefault(x => x.Page == _browserTabs.TabPages[index]);
        var url = obj?.WebView.Source?.ToString() ?? obj?.InitialUrl ?? TornRoutes.Home;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { FlashStatus(ex.Message); }
    }

    private Rectangle GetCloseRect(int index)
    {
        var r = _browserTabs.GetTabRect(index);
        return new Rectangle(r.Right - 22, r.Top + 6, 16, Math.Max(12, r.Height - 12));
    }

    private void DrawBrowserTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _browserTabs.TabPages.Count) return;
        var rect = _browserTabs.GetTabRect(e.Index);
        var selected = e.Index == _browserTabs.SelectedIndex;
        using var bg = new SolidBrush(selected ? Color.FromArgb(46, 51, 61) : Color.FromArgb(28, 31, 37));
        e.Graphics.FillRectangle(bg, rect);

        var closeRect = GetCloseRect(e.Index);
        var textRect = new Rectangle(rect.X + 8, rect.Y + 2, Math.Max(10, rect.Width - 34), rect.Height - 4);
        TextRenderer.DrawText(e.Graphics, _browserTabs.TabPages[e.Index].Text, Font, textRect,
            selected ? Color.White : TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var xFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "×", xFont, closeRect,
            selected ? Color.White : TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void BrowserTabsMouseDown(object? sender, MouseEventArgs e)
    {
        for (var i = 0; i < _browserTabs.TabPages.Count; i++)
        {
            var tabRect = _browserTabs.GetTabRect(i);
            if (!tabRect.Contains(e.Location)) continue;

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && GetCloseRect(i).Contains(e.Location)))
            {
                CloseBrowserTab(i);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _contextTabIndex = i;
                _browserTabs.SelectedIndex = i;
                _tabMenu.Show(_browserTabs, e.Location);
                return;
            }
            break;
        }
    }

    private CancellationTokenSource? _apiSyncCancellation;

    private async Task RefreshFastAsync(bool showErrors)
    {
        if (_syncing) return;
        var key = CredentialManager.LoadApiKey(CurrentProfileId);
        if (string.IsNullOrWhiteSpace(key))
        {
            _apiStatusLabel.Text = "API: no saved key. Settings → Torn API Key.";
            return;
        }

        var syncProfile = CurrentProfileId;
        using var syncCancellation = new CancellationTokenSource();
        _apiSyncCancellation = syncCancellation;
        _syncing = true;
        _apiStatusLabel.Text = "API: fast sync — paced requests…";
        UpdateModernDashboard();
        try
        {
            var fresh = await _api.FetchFastSnapshotAsync(key, syncCancellation.Token);
            if (syncProfile != CurrentProfileId) return;
            MergeFastSnapshot(fresh);
            UpdateAllUi();
            _apiStatusLabel.Text = fresh.Errors.Count == 0
                ? $"API fast sync OK — {fresh.FastRetrievedAt:G}"
                : $"Fast sync partial — {fresh.Errors.Count} warning(s).";
            if (showErrors && fresh.Errors.Count > 0)
                ShowApiWarnings(fresh.Errors, "Fast API sync");
        }
        catch (Exception ex)
        {
            if (syncProfile != CurrentProfileId || syncCancellation.IsCancellationRequested) return;
            _apiStatusLabel.Text = "API fast sync failed: " + ex.Message;
            if (showErrors) MessageBox.Show(ex.Message, "Torn API fast sync");
        }
        finally
        {
            if (ReferenceEquals(_apiSyncCancellation, syncCancellation)) _apiSyncCancellation = null;
            _syncing = false;
            UpdateModernDashboard();
        }
    }

    private async Task RefreshFullAsync(bool showErrors)
    {
        if (_syncing) return;
        var key = CredentialManager.LoadApiKey(CurrentProfileId);
        if (string.IsNullOrWhiteSpace(key))
        {
            _apiStatusLabel.Text = "API: no saved key. Settings → Torn API Key.";
            if (showErrors) MessageBox.Show("Save your Torn API key in Settings first.");
            return;
        }

        var syncProfile = CurrentProfileId;
        using var syncCancellation = new CancellationTokenSource();
        _apiSyncCancellation = syncCancellation;
        _syncing = true;
        _apiStatusLabel.Text = "API: full sync — reading perks, education, Crimes 2.0 and awards…";
        FlashStatus("Full API sync running with paced requests…");
        UpdateModernDashboard();
        try
        {
            var fresh = await _api.FetchFullSnapshotAsync(key, syncCancellation.Token);
            if (syncProfile != CurrentProfileId) return;
            if (fresh.Errors.Any(x => x.Contains("Torn API cooldown", StringComparison.Ordinal)))
            {
                MergeFastSnapshot(fresh);
                _snapshot.Errors = fresh.Errors;
                UpdateAllUi();
                _apiStatusLabel.Text = "API cooldown — last successful full data retained.";
                if (showErrors) ShowApiWarnings(fresh.Errors, "Torn API cooldown");
                return;
            }
            _snapshot = fresh;
            UpdateAllUi();
            _apiStatusLabel.Text = _snapshot.Errors.Count == 0
                ? $"API full sync OK — {_snapshot.FullRetrievedAt:G}"
                : $"Full sync complete with {_snapshot.Errors.Count} warning(s) — {_snapshot.FullRetrievedAt:G}";
            _crimeSyncLabel.Text = _snapshot.Crimes.Count > 0
                ? $"{_snapshot.Crimes.Count} crime categories synced at {_snapshot.FullRetrievedAt:t}. Coach plan rebuilt automatically."
                : "Crime data was not available with the current key/endpoint response.";
            FlashStatus("Full API sync complete; live coach updated.");

            if (showErrors && _snapshot.Errors.Count > 0)
                ShowApiWarnings(_snapshot.Errors, "Full API sync");
        }
        catch (Exception ex)
        {
            if (syncProfile != CurrentProfileId || syncCancellation.IsCancellationRequested) return;
            _apiStatusLabel.Text = "API full sync failed: " + ex.Message;
            if (showErrors) MessageBox.Show(ex.Message, "Torn API full sync");
        }
        finally
        {
            if (ReferenceEquals(_apiSyncCancellation, syncCancellation)) _apiSyncCancellation = null;
            _syncing = false;
            UpdateModernDashboard();
        }
    }

    private static bool IsAccessLevelError(string error)
        => error.Contains("Access level of this key is not high enough", StringComparison.OrdinalIgnoreCase)
           || error.Contains("\"code\":16", StringComparison.OrdinalIgnoreCase);

    private void ShowApiWarnings(IReadOnlyCollection<string> errors, string title)
    {
        var cooldown = errors.FirstOrDefault(x => x.Contains("Torn API cooldown", StringComparison.Ordinal));
        if (cooldown is not null)
        {
            MessageBox.Show("Torn temporarily limited API requests. Commander has stopped new requests and will allow them after the cooldown. Other tools using your account share Torn's allowance.\n\nLast successful data is retained. Wait for the API cooldown badge to clear before syncing again.", "Torn API cooldown");
            return;
        }
        var restricted = errors.Where(IsAccessLevelError).ToList();
        if (restricted.Count > 0)
        {
            var other = errors.Where(e => !IsAccessLevelError(e)).Take(5).ToList();
            string message;
            if (_settings.ApiFullAccessVerified)
            {
                _apiStatusLabel.Text = $"Full Access key verified; {restricted.Count} endpoint(s) still require additional Torn role/entity permission.";
                message = "Your Full Access key passed Commander's verification, but Torn still rejected one or more endpoints with code 16. That can happen when a faction/company endpoint also requires in-game role permissions that an API key cannot grant. Commander will keep using every section that is available.";
            }
            else
            {
                _apiStatusLabel.Text = $"API key restricted — {restricted.Count} section(s) blocked. Settings → Create Full Access key.";
                message = "Your saved Torn API key is too restricted for Torn Commander's live guide. That is why crime skills, perks, job/faction data, money, racing and other sections may look blank or vague.\n\n"
                        + "Fix it: Settings → Create Full Access key → approve in Torn → copy the NEW key → Paste + Save → Verify Full Access → Full Sync.\n\n"
                        + "The old key cannot be upgraded in place.";
            }
            if (other.Count > 0)
                message += "\n\nOther warnings still present:\n• " + string.Join("\n• ", other);
            MessageBox.Show(message, title + " — API permissions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(string.Join("\n\n", errors.Take(12)), title + " warnings");
    }

    private void MergeFastSnapshot(TornSnapshot fresh)
    {
        SnapshotMerge.MergeFast(_snapshot, fresh);
        if (!fresh.RawJson.ContainsKey("basic") && !fresh.RawJson.ContainsKey("travel")) ClearTravelState();
    }

    private void UpdateAllUi()
    {
        _recommendation = RecommendationEngine.Build(_snapshot, _settings);

        var name = _snapshot.PlayerName ?? "Waiting for API";
        var id = _snapshot.PlayerId?.ToString() ?? "—";
        var lvl = _snapshot.Level?.ToString() ?? "—";
        _profileLabel.Text = $"{name} [{id}]  •  Level {lvl}";

        if (_snapshot.CurrentNerve is int cur && _snapshot.MaxNerve is int max)
        {
            var nnbText = _recommendation.EstimatedNaturalNerve is int nnb ? $" • working NNB ≈ {nnb}" : "";
            _barsLabel.Text = $"Nerve: {cur}/{max}{nnbText}";
        }
        else
        {
            _barsLabel.Text = "Nerve: waiting for bars permission…";
        }

        _coachPhase.Text = "Phase: " + _recommendation.Phase;
        _coachHeadline.Text = _recommendation.Headline;
        _coachWhy.Text = _recommendation.Why;
        _coachNerve.Text = _recommendation.NerveAdvice;
        _coachMetrics.Text = BuildLiveProgressionSummary();
        _coachEducation.Text = _recommendation.EducationAdvice;
        _coachAccount.Text = _recommendation.AccountAdvice;
        _coachChecklist.Text = _recommendation.Checklist.Count == 0
            ? "• No urgent checklist items detected. Keep spending nerve safely and sync periodically."
            : string.Join(Environment.NewLine, _recommendation.Checklist.Select(x => "• " + x));
        UpdateGuidedActionUi();

        if (_snapshot.Wallet is long wallet)
            _moneyApiLabel.Text = $"Wallet: ${wallet:N0}";
        else
            _moneyApiLabel.Text = "Wallet: unavailable with current API permissions.";

        _racingSkillLabel.Text = _snapshot.RacingSkill is double rs
            ? $"Racing skill: {rs:0.####}"
            : "Racing skill: unavailable / not parsed yet.";

        if (_snapshot.Crimes.Count > 0)
            _crimeSyncLabel.Text = $"{_snapshot.Crimes.Count} crime categories synced • last full sync {_snapshot.FullRetrievedAt:G}";

        RebindCrimes();
        RebindMerits();
        if (_coachMerits is not null) _coachMerits.Text = BuildMeritCoachSummary();
        UpdateCapTimer();
        UpdatePageHint();
        UpdateModernDashboard();
    }

    private void RebindCrimes()
    {
        if (_crimeGrid is null) return;
        var nnb = _recommendation.EstimatedNaturalNerve;
        var rows = _snapshot.Crimes
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                Crime = c.Name,
                Skill = Math.Round(c.Skill, 2),
                NerveSpent = c.NerveSpent,
                Success = c.Attempts > 0 ? $"{c.SuccessRate:0.0}%" : "—",
                Crits = c.CriticalFail,
                Bonus = c.ProgressionBonus == 0 ? "—" : $"+{c.ProgressionBonus}%",
                Enhancer = string.IsNullOrWhiteSpace(c.Enhancer) ? "—" : c.Enhancer,
                Plan = RecommendationEngine.PlanForCrime(c, nnb)
            })
            .ToList();
        _crimeGrid.DataSource = rows;
    }

    private int MeritPriorityScore(MeritAward award)
    {
        if (!award.AvailableForCurrentCrimeVersion || award.ProgressConflict) return award.Achieved ? 0 : 1;
        var score = award.EaseScore;
        var mode = _settings.MeritHunterMode;
        if (mode.Equals("NNB Growth", StringComparison.OrdinalIgnoreCase))
        {
            if (award.Category.Equals("Crime", StringComparison.OrdinalIgnoreCase)) score += 8;
            var text = (award.Name + " " + award.Description).ToLowerInvariant();
            if (text.Contains("nerve") || text.Contains("crime")) score += 5;
        }
        return Math.Clamp(score, 0, 120);
    }

    private void RebindMerits()
    {
        if (_meritGrid is null || _meritSummaryLabel is null || _meritSpendLabel is null) return;
        if (_snapshot.Awards.Count == 0)
        {
            _visibleMeritAwards = new();
            _meritGrid.DataSource = null;
            _meritSummaryLabel.Text = _snapshot.FullRetrievedAt == DateTime.MinValue
                ? "Run Full Sync to scan honors and medals."
                : "Award catalog unavailable. Check API warnings / Full Access permissions.";
            _meritSpendLabel.Text = MeritHunter.SpendAdvice(_snapshot, _settings.MeritHunterMode);
            return;
        }

        var query = _meritSearchBox?.Text?.Trim() ?? "";
        var source = _snapshot.Awards.AsEnumerable();
        if (!(_meritShowAchieved?.Checked ?? false)) source = source.Where(x => !x.Achieved);
        if (!(_meritShowUnavailable?.Checked ?? false)) source = source.Where(x => x.Achieved || x.AvailableForCurrentCrimeVersion);
        if (!string.IsNullOrWhiteSpace(query))
            source = source.Where(x => (x.Name + " " + x.Description + " " + x.Category + " " + x.AwardType + " " + x.Availability)
                .Contains(query, StringComparison.OrdinalIgnoreCase));

        _visibleMeritAwards = source
            .OrderByDescending(MeritPriorityScore)
            .ThenByDescending(x => x.ProgressPercent ?? -1)
            .ThenBy(x => x.Name)
            .ToList();

        var rank = 0;
        _meritGrid.DataSource = _visibleMeritAwards.Select(a => new
        {
            Rank = a.Achieved ? "✓" : (++rank).ToString(),
            Award = a.Name,
            Kind = string.IsNullOrWhiteSpace(a.CrimesVersion) ? $"{a.AwardType} / {a.Category}" : $"{a.AwardType} / {a.Category} / {a.CrimesVersion}",
            Status = a.Achieved ? "EARNED" : !a.AvailableForCurrentCrimeVersion ? "UNAVAILABLE" : a.ProgressConflict ? "VERIFY" : "ACTIVE",
            Progress = a.Achieved ? "100% — DONE"
                : !a.AvailableForCurrentCrimeVersion ? a.Availability
                : a.ProgressConflict && a.Current is long conflictCur && a.Target is int conflictTarget ? $"CHECK: {conflictCur:N0} >= {conflictTarget:N0}; not awarded"
                : a.ProgressPercent is double pct && a.Current is long cur && a.Target is int target ? $"{pct:0.#}%  ({cur:N0}/{target:N0})"
                : a.Target is int targetOnly ? $"Target {targetOnly:N0} • progress unknown" : "Progress not exposed",
            Effort = a.Achieved ? "DONE" : a.Effort,
            Requirement = string.IsNullOrWhiteSpace(a.Description) ? "Torn did not return requirement text" : a.Description,
            WhatToDo = a.Advice
        }).ToList();

        var achieved = _snapshot.Awards.Count(x => x.Achieved);
        var total = _snapshot.Awards.Count;
        var easy = _snapshot.Awards.Count(x => !x.Achieved && x.AvailableForCurrentCrimeVersion && !x.ProgressConflict && MeritPriorityScore(x) >= 75);
        _meritSummaryLabel.Text = $"Awards earned: {achieved:N0}/{total:N0} scanned • crime system: {(_snapshot.CrimeVersion.Length == 0 ? "unknown" : _snapshot.CrimeVersion)} • {easy:N0} high-priority active candidates • unspent merits: {_snapshot.MeritAvailable:N0}";
        _meritSpendLabel.Text = MeritHunter.SpendAdvice(_snapshot, _settings.MeritHunterMode);
    }

    private MeritAward? SelectedMeritAward()
    {
        var i = _meritGrid?.CurrentRow?.Index ?? -1;
        return i >= 0 && i < _visibleMeritAwards.Count ? _visibleMeritAwards[i] : null;
    }

    private void OpenSelectedMerit()
    {
        var award = SelectedMeritAward();
        if (award is null) return;
        NavigateActive(award.Route);
        FlashStatus($"Merit Hunter: {award.Name} — {award.Description}");
    }

    private void ExplainSelectedMerit()
    {
        var award = SelectedMeritAward();
        if (award is null)
        {
            MessageBox.Show("Select an award row first.", "Merit Hunter");
            return;
        }

        var progress = award.Achieved
            ? "Earned: YES"
            : !award.AvailableForCurrentCrimeVersion
                ? $"Availability: {award.Availability}"
                : award.ProgressConflict
                    ? $"Progress check: {award.ProgressSource}"
                    : award.ProgressPercent is double pct
                        ? $"Tracked progress: {pct:0.#}% — {award.ProgressSource}"
                        : $"Progress: unknown — {award.ProgressSource}";

        MessageBox.Show(
            $"{award.Name}\n\nTORN REQUIREMENT\n{award.Description}\n\nWHAT YOU ACTUALLY DO\n{award.Advice}\n\n{progress}\nPriority: {award.Effort} • score {MeritPriorityScore(award)}",
            "Merit Hunter — How to earn it");
    }

    private void GuideNextMerit()
    {
        var award = _visibleMeritAwards.FirstOrDefault(x => !x.Achieved && x.AvailableForCurrentCrimeVersion && !x.ProgressConflict)
                    ?? _snapshot.Awards.Where(x => !x.Achieved && x.AvailableForCurrentCrimeVersion && !x.ProgressConflict).OrderByDescending(MeritPriorityScore).FirstOrDefault();
        if (award is null)
        {
            MessageBox.Show("No unearned award candidates are loaded. Run Full Sync first.", "Merit Hunter");
            return;
        }

        NavigateActive(award.Route);
        var progress = award.ProgressPercent is double pct ? $"\nTracked progress: {pct:0.#}% ({award.ProgressSource})" : "\nProgress: not safely exposed/derived from the current API data.";
        MessageBox.Show($"NEXT MERIT: {award.Name}\n\nTORN REQUIREMENT\n{award.Description}\n\nWHAT TO DO\n{award.Advice}\n\nPriority: {award.Effort} • score {MeritPriorityScore(award)}{progress}\n\nCommander opened the most relevant Torn page. Complete the game action manually, then Full Sync to confirm the award.", "Merit Hunter");
    }

    private void CopyMeritPlan()
    {
        var rows = _snapshot.Awards.Where(x => !x.Achieved && x.AvailableForCurrentCrimeVersion && !x.ProgressConflict)
            .OrderByDescending(MeritPriorityScore).ThenBy(x => x.Name).Take(10).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show("Run Full Sync first.", "Merit Hunter");
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("TORN COMMANDER — TOP 10 MERIT HUNT");
        sb.AppendLine("Mode: " + _settings.MeritHunterMode);
        sb.AppendLine(MeritHunter.SpendAdvice(_snapshot, _settings.MeritHunterMode));
        sb.AppendLine();
        var i = 1;
        foreach (var a in rows)
        {
            sb.AppendLine($"{i++}. {a.Name} [{a.AwardType}] — {a.Effort} / score {MeritPriorityScore(a)}");
            sb.AppendLine("   Requirement: " + a.Description);
            sb.AppendLine("   What to do: " + a.Advice);
            if (a.ProgressPercent is double pct) sb.AppendLine($"   Tracked progress: {pct:0.#}% — {a.ProgressSource}");
        }
        Clipboard.SetText(sb.ToString());
        FlashStatus("Top 10 Merit Hunter plan copied to clipboard.");
    }

    private string BuildMeritCoachSummary()
    {
        if (_snapshot.Awards.Count == 0) return "Run Full Sync to scan Torn's honor/medal catalog and your achieved awards.";
        var next = _snapshot.Awards.Where(x => !x.Achieved && x.AvailableForCurrentCrimeVersion && !x.ProgressConflict).OrderByDescending(MeritPriorityScore).ThenBy(x => x.Name).FirstOrDefault();
        var easy = _snapshot.Awards.Count(x => !x.Achieved && x.AvailableForCurrentCrimeVersion && !x.ProgressConflict && MeritPriorityScore(x) >= 75);
        if (next is null) return $"Merit scan loaded {_snapshot.Awards.Count:N0} awards; no unearned candidate was detected.";
        var progress = next.ProgressPercent is double pct ? $" • {pct:0.#}% tracked" : "";
        return $"{easy:N0} high-priority merit candidates detected. Next: {next.Name} ({next.Effort}){progress}. {MeritHunter.SpendAdvice(_snapshot, _settings.MeritHunterMode)}";
    }

    private string BuildLiveProgressionSummary()
    {
        if (_snapshot.Crimes.Count == 0)
        {
            if (_snapshot.Errors.Any(IsAccessLevelError))
                return "Crime tracker is blocked by the current restricted API key. Settings → Create Full Access key → Paste + Save → Verify Full Access → Full Sync.";
            return "Run Full Sync to load per-crime Crimes 2.0 statistics.";
        }

        var nnb = _recommendation.EstimatedNaturalNerve;
        var priorityName = nnb is null || nnb < 35 ? "graffiti" : nnb < 40 ? "hustling" : "cracking";
        var priority = _snapshot.Crimes.FirstOrDefault(x => x.Name.Contains(priorityName, StringComparison.OrdinalIgnoreCase));
        var totalNerve = _snapshot.Crimes.Sum(x => x.NerveSpent);
        var totalAttempts = _snapshot.Crimes.Sum(x => x.Attempts);
        var totalCrits = _snapshot.Crimes.Sum(x => x.CriticalFail);
        var critRate = totalAttempts > 0 ? totalCrits * 100.0 / totalAttempts : 0;

        var sb = new StringBuilder();
        sb.Append($"Tracked crime nerve: {totalNerve:N0} • attempts: {totalAttempts:N0} • critical fails: {totalCrits:N0}");
        if (totalAttempts > 0) sb.Append($" ({critRate:0.000}% overall)");
        if (priority is not null)
        {
            sb.AppendLine();
            sb.Append($"Current priority: {priority.Name} • CS {priority.Skill:0.##} • {priority.NerveSpent:N0} nerve spent");
            if (priority.ProgressionBonus > 0) sb.Append($" • API progression bonus +{priority.ProgressionBonus}%");
            if (priority.Attempts > 0) sb.Append($" • success {priority.SuccessRate:0.0}%");
        }

        if (_snapshot.DetectedAddictionPercent is int addiction)
            sb.Append($"{Environment.NewLine}Addiction auto-detected: {addiction}% via {_snapshot.AddictionSource ?? "API"}.");
        else
            sb.Append($"{Environment.NewLine}Addiction: using manual fallback {_settings.ManualAddictionPercent}% until API data can detect it.");

        return sb.ToString();
    }

    private void UpdateCapTimer()
    {
        if (_snapshot.CurrentNerve is not int baseCurrent || _snapshot.MaxNerve is not int max || max <= 0)
        {
            _capLabel.Text = "Nerve cap: waiting for API…";
            return;
        }

        var at = _snapshot.FastRetrievedAt == DateTime.MinValue ? DateTime.Now : _snapshot.FastRetrievedAt;
        var elapsed = DateTime.Now - at;
        var regen = Math.Max(0, (int)Math.Floor(elapsed.TotalMinutes / 5.0));
        var current = Math.Min(max, baseCurrent + regen);
        var missing = Math.Max(0, max - current);

        if (missing == 0)
        {
            _capLabel.Text = "Nerve cap: CAPPED — spend it; natural regen is being wasted.";
            _capLabel.ForeColor = Warn;
            return;
        }

        var minutesIntoTick = Math.Max(0, elapsed.TotalMinutes % 5.0);
        var minutes = Math.Max(0, missing * 5.0 - minutesIntoTick);
        var capAt = DateTime.Now.AddMinutes(minutes);
        var span = TimeSpan.FromMinutes(minutes);
        var hours = (int)span.TotalHours;
        _capLabel.Text = $"Nerve cap: ~{hours}h {span.Minutes}m • around {capAt:t} • estimated {current}/{max}";
        _capLabel.ForeColor = missing <= 6 ? Warn : TextMain;
    }

    private void UpdatePageHint()
    {
        if (_pageHint is null) return;
        var url = Active()?.WebView.Source?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            _pageHint.Text = "Page-aware hint: no Torn page open.";
            return;
        }

        var lower = url.ToLowerInvariant();
        string hint;
        if (lower.Contains("page.php") && lower.Contains("sid=crimes"))
            hint = _recommendation.CrimeAdvice;
        else if (lower.Contains("page.php") && lower.Contains("sid=racing"))
            hint = _snapshot.RacingSkill is double rs
                ? $"Racing page detected • API racing skill {rs:0.####}. Log car/track observations under Racing while you progress."
                : "Racing page detected • run Full Sync to pull your current racing skill.";
        else if (lower.Contains("education"))
            hint = _recommendation.EducationAdvice;
        else if (lower.Contains("bazaar") || lower.Contains("item.php") || lower.Contains("market"))
            hint = "Trading page detected • record buy/sell numbers in Money so profit and ROI are based on your real cost basis.";
        else if (lower.Contains("casino"))
            hint = "Casino page detected • set a bankroll/session limit first and log the ending result. Commander will not auto-bet.";
        else if (lower.Contains("faction"))
            hint = "Faction page detected • for NNB, check Criminality/Crime Progression, nerve/Voracity bonuses, and regular OC2.0 participation.";
        else if (lower.Contains("compan") || lower.Contains("job"))
            hint = "Company/job page detected • compare job-point nerve and progression perks against your current NNB phase before changing jobs.";
        else
            hint = _recommendation.Headline;

        _pageHint.Text = "Page-aware hint: " + hint;
    }

    private static string NormalizeGuideActionMode(string? mode)
    {
        return mode switch
        {
            "Advice only" => "Advice only",
            "One-click single action" => "One-click single action",
            _ => "Open & Configure"
        };
    }

    private void UpdateGuidedActionUi()
    {
        if (_guideActionLabel is null || _guideActionButton is null || _guideOpenButton is null) return;
        var action = _recommendation.GuidedAction;
        var mode = NormalizeGuideActionMode(_settings.GuideActionMode);
        if (_guideActionModeCombo is not null && _guideActionModeCombo.SelectedItem?.ToString() != mode)
            _guideActionModeCombo.SelectedItem = mode;
        if (_guideActionModeSettingsCombo is not null && _guideActionModeSettingsCombo.SelectedItem?.ToString() != mode)
            _guideActionModeSettingsCombo.SelectedItem = mode;

        if (action is null)
        {
            _guideActionLabel.Text = "No guided action is available yet. Run Full Sync first.";
            _guideActionButton.Enabled = false;
            _guideOpenButton.Enabled = false;
            return;
        }

        _guideActionLabel.Text = $"Recommended now: {action.Summary}\nMode: {mode}";
        _guideOpenButton.Text = $"Open {action.Crime}";
        _guideOpenButton.Enabled = true;

        if (mode == "Advice only")
        {
            _guideActionButton.Text = "Advice only";
            _guideActionButton.Enabled = false;
        }
        else if (mode == "One-click single action" && action.SupportsRunOnce)
        {
            _guideActionButton.Text = "Run Recommended Once";
            _guideActionButton.Enabled = true;
        }
        else if (action.SupportsConfigure)
        {
            _guideActionButton.Text = "Open & Configure";
            _guideActionButton.Enabled = true;
        }
        else
        {
            _guideActionButton.Text = $"Open {action.Crime}";
            _guideActionButton.Enabled = true;
        }
    }

    private async Task CopyGraffitiDebugAsync()
    {
        var tab = Active();
        if (tab is null)
        {
            FlashStatus("Open Torn Crimes > Graffiti first, then copy diagnostics.");
            return;
        }
        var raw = await tab.ExecuteScriptAsync("(() => { const h=window.__TornCommanderGraffiti; return h && h.debugJson ? h.debugJson() : 'Graffiti helper not active on this page'; })()");
        if (string.IsNullOrWhiteSpace(raw))
        {
            FlashStatus("Graffiti diagnostics are unavailable on the current Torn tab.");
            return;
        }
        try
        {
            using var parsed = JsonDocument.Parse(raw);
            var report = parsed.RootElement.ValueKind == JsonValueKind.String ? parsed.RootElement.GetString() ?? "" : parsed.RootElement.ToString();
            if (string.IsNullOrWhiteSpace(report)) report = "Empty Graffiti diagnostic response";
            if (report.StartsWith("Graffiti helper not active", StringComparison.OrdinalIgnoreCase))
            {
                FlashStatus("Open the Graffiti crime page and reload its tab; the diagnostic helper is not running here.");
                return;
            }
            Clipboard.SetText(report);
            FlashStatus("Graffiti selection diagnostics copied. Paste into ChatGPT so we can map the actual Torn controls.");
        }
        catch (Exception ex)
        {
            FlashStatus("Could not copy Graffiti diagnostics: " + ex.Message);
        }
    }

    private async Task ExecuteRecommendedActionCoreAsync(bool openOnly, bool dashboardRunOnce, string actionProfile)
    {
        var action = _recommendation.GuidedAction;
        if (action is null)
        {
            FlashStatus("Run Full Sync first so Commander can generate a recommended action.");
            return;
        }

        var mode = NormalizeGuideActionMode(_settings.GuideActionMode);
        // The modern dashboard has a distinct, explicitly clicked Run Once button.
        // Unlike the Classic Coach button, it MUST NOT be silently downgraded
        // by the persisted default mode (Open & Configure). Unsupported crimes
        // remain open-only; no unsupported click automation is attempted.
        if (openOnly || (!dashboardRunOnce && mode == "Advice only") || !action.SupportsConfigure)
        {
            NavigateActive(action.Url);
            FlashStatus($"Opened {action.Crime}." + (action.SupportsConfigure ? " Guided mode is advice/open only." : " Configure the safe target manually; automation for this crime is not enabled yet."));
            return;
        }

        var tab = await EnsureActionPageAsync(action.Url);
        if (tab is null)
        {
            if (!_lastGraffitiActionStatus.StartsWith("STOPPED:", StringComparison.OrdinalIgnoreCase))
                FlashStatus("Could not open the recommended Torn page. No action submitted.");
            return;
        }

        if (actionProfile != CurrentProfileId) return;
        if (action.Crime.Equals("Graffiti", StringComparison.OrdinalIgnoreCase))
        {
            var runOnce = action.SupportsRunOnce && (dashboardRunOnce || mode == "One-click single action");
            _lastGraffitiActionStatus = runOnce
                ? "CHECKING: Run Once requested — verifying district, paint and Spray button before a single click."
                : "CHECKING: Open & Configure requested — no automatic Spray.";
            UpdateModernDashboard();
            // The Torn Crimes screen is a SPA. Route-ready != script-ready, and a
            // helper registered on document creation may be absent after switching
            // its script on without reloading an existing tab. Never spray until
            // the current page exposes the specific Commander guide API.
            var helperReady = false;
            const string helperName = "Torn_Graffiti_Paint_Helper.user.js";
            var helperEnabled = _settings.EnabledScripts.Contains(helperName, StringComparer.OrdinalIgnoreCase);
            if (!helperEnabled)
            {
                _lastGraffitiActionStatus = "STOPPED: Graffiti helper is disabled. Enable it in Classic Tools > Scripts, then retry.";
                UpdateModernDashboard();
                FlashStatus(_lastGraffitiActionStatus);
                return;
            }
            var attemptedInjection = false;
            for (var i = 0; i < 60; i++)
            {
                if (actionProfile != CurrentProfileId) return;
                var probe = await tab.ExecuteScriptAsync("typeof window.__TornCommanderGraffiti?.startGuideAction === 'function'");
                if (probe == "true") { helperReady = true; break; }
                // Recover a helper that was enabled after this WebView tab loaded,
                // without repeatedly adding scripts or clicking page controls.
                if (!attemptedInjection && i >= 10)
                {
                    attemptedInjection = true;
                    var path = Path.Combine(Storage.UserScriptsPath, helperName);
                    if (File.Exists(path))
                    {
                        try
                        {
                            var helperSource = await File.ReadAllTextAsync(path);
                            await tab.ExecuteScriptAsync(helperSource);
                        }
                        catch (Exception ex)
                        {
                            Program.TraceStartup("Graffiti helper injection failed: " + ex.Message);
                        }
                    }
                }
                await Task.Delay(200);
            }
            if (!helperReady)
            {
                var route = await tab.ExecuteScriptAsync("location.pathname + location.search + location.hash");
                _lastGraffitiActionStatus = "STOPPED: Graffiti helper unavailable after waiting 12 seconds. Current page: "
                    + (route is null ? "unknown" : route.Length > 160 ? route[..160] : route)
                    + ". Open the Graffiti tab, reload it, and check Scripts.";
                UpdateModernDashboard();
                FlashStatus(_lastGraffitiActionStatus);
                return;
            }
            if (actionProfile != CurrentProfileId) return;
            FlashStatus("Graffiti: verifying selections for " + (runOnce ? "one Spray attempt" : "configuration only") + ".");
            var result = await ExecuteGraffitiCommandAsync(tab, action.Target, action.Option, runOnce, actionProfile);
            if (actionProfile != CurrentProfileId) return;
            if (string.IsNullOrWhiteSpace(result) || result == "null" || result.Contains("helper-missing", StringComparison.OrdinalIgnoreCase))
            {
                _lastGraffitiActionStatus = "STOPPED: Graffiti helper returned no result. Reload Crimes, then use Copy Graffiti Debug.";
                UpdateModernDashboard();
                FlashStatus(_lastGraffitiActionStatus);
                return;
            }
            // Maintain the helper's detailed STOPPED/CLICK_SENT event as authoritative.
            // This fallback is used only when the WebView message bridge was unavailable.
            if (_lastGraffitiActionStatus.StartsWith("CHECKING", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var doc = JsonDocument.Parse(result);
                    var root = doc.RootElement;
                    var ok = root.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
                    var reason = root.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() : null;
                    var clicked = root.TryGetProperty("submitted", out var submitted) && submitted.ValueKind == JsonValueKind.True;
                    _lastGraffitiActionStatus = clicked ? "CLICK_SENT: A single Spray click was sent; verify Torn's result."
                        : ok ? "VERIFIED: Selections configured; Spray not submitted."
                        : "STOPPED: " + (reason ?? "Selections could not be verified.");
                    UpdateModernDashboard();
                }
                catch { /* The event bridge may already have delivered a more detailed result. */ }
            }
            return;
        }

        NavigateActive(action.Url);
        FlashStatus($"Opened {action.Crime}. Single-action configuration for this crime will be added only after its live Torn controls are mapped.");
    }

    private async Task<BrowserTab?> EnsureActionPageAsync(string url)
    {
        var normalized = TornRoutes.Normalize(url);
        var tab = Active();
        if (tab is null)
        {
            await AddBrowserTabAsync(normalized);
            tab = Active();
        }
        else
        {
            var current = tab.WebView.Source?.ToString() ?? "";
            if (!current.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                NavigateActive(normalized);
        }

        if (tab is null) return null;
        // A generic Crimes page can report document.readyState=complete before
        // Torn has switched to the Graffiti hash route. Do not mistake that for
        // readiness: wait for the requested actual route inside this same tab.
        const string routeCheck = "(() => location.hostname.endsWith('torn.com') && "
            + "new URLSearchParams(location.search).get('sid') === 'crimes' && "
            + "/^#\\/?graffiti(?:$|[/?])/i.test(location.hash) && "
            + "(document.readyState === 'complete' || document.readyState === 'interactive'))()";
        for (var i = 0; i < 75; i++)
        {
            if (tab.WebView.IsDisposed || tab.WebView.CoreWebView2 is null) return null;
            var atRoute = await tab.ExecuteScriptAsync(routeCheck);
            if (atRoute == "true")
            {
                await Task.Delay(300); // allow Crimes SPA state to render
                return tab;
            }
            await Task.Delay(200);
        }
        _lastGraffitiActionStatus = "STOPPED: Graffiti page did not become ready (15-second timeout). No controls clicked. Check the Torn browser for loading or Cloudflare errors.";
        UpdateModernDashboard();
        FlashStatus(_lastGraffitiActionStatus);
        return null; // Never run on an unrelated/incomplete page.
    }

    private void PasteAndSaveApiKey()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                MessageBox.Show("Clipboard does not contain text. Copy the new Torn API key first.", "Torn Commander");
                return;
            }
            var key = Clipboard.GetText().Trim();
            if (key.Length != 16)
            {
                MessageBox.Show("The clipboard text is not a 16-character Torn API key.", "Torn Commander");
                return;
            }
            _apiKeyBox.Text = key;
            SaveApiKey();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not paste API key");
        }
    }

    private async Task VerifyFullAccessAsync()
    {
        try
        {
            var key = _apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(key)) key = CredentialManager.LoadApiKey(CurrentProfileId) ?? "";
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Create/paste/save a Torn API key first.");

            // These private selections were among the ones rejected by the user's old restricted key.
            // Successful reads are a practical check that the replacement key has the access Commander needs.
            await _api.FetchEndpointAsync("user/money", key);
            await _api.FetchEndpointAsync("user/casino", key);
            await _api.FetchEndpointAsync("user/perks", key);
            _settings.ApiFullAccessVerified = true;
            Storage.SaveSettings(_settings);
            MessageBox.Show("Full Access check passed. Commander can read the private account sections used by the live guide.\n\nRun Full Sync now.", "API access OK");
            FlashStatus("Full Access API key verified.");
        }
        catch (Exception ex)
        {
            var msg = ex.Message.Contains("code\":16", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Access level of this key is not high enough", StringComparison.OrdinalIgnoreCase)
                ? "This key is still restricted. In Settings click Create Full Access key, approve it in Torn, copy the NEW key, then Paste + Save. The old key cannot be upgraded in place."
                : ex.Message;
            MessageBox.Show(msg, "Full Access check failed");
        }
    }

    private void SaveApiKey()
    {
        try
        {
            var key = _apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("Paste the Torn API key first.", "Torn Commander");
                return;
            }
            CredentialManager.SaveApiKey(key, CurrentProfileId);
            _settings.ApiFullAccessVerified = false;
            Storage.SaveSettings(_settings);
            _apiKeyBox.Text = key;
            _apiStatusLabel.Text = "API key saved securely in Windows Credential Manager.";
            FlashStatus("API key saved. It will auto-load on future launches.");
            _ = RefreshFullAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save API key");
        }
    }

    private async Task TestApiKeyAsync()
    {
        try
        {
            var key = _apiKeyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(key)) key = CredentialManager.LoadApiKey(CurrentProfileId) ?? "";
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter or save a key first.");

            var result = await _api.FetchFastSnapshotAsync(key);
            var sb = new StringBuilder();
            sb.AppendLine("Torn API responded.");
            sb.AppendLine();
            sb.AppendLine($"Profile: {result.PlayerName ?? "—"} [{result.PlayerId?.ToString() ?? "—"}]");
            sb.AppendLine($"Level: {result.Level?.ToString() ?? "—"}");
            sb.AppendLine($"Nerve: {result.CurrentNerve?.ToString() ?? "—"}/{result.MaxNerve?.ToString() ?? "—"}");
            if (result.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Some requested selections were unavailable:");
                foreach (var err in result.Errors) sb.AppendLine("• " + err);
            }
            MessageBox.Show(sb.ToString(), "API test");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "API test failed");
        }
    }

    private void ForgetApiKey()
    {
        if (MessageBox.Show("Forget the saved Torn API key on this Windows account?", "Forget API key", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        CredentialManager.ForgetApiKey(CurrentProfileId);
        _settings.ApiFullAccessVerified = false;
        Storage.SaveSettings(_settings);
        _apiKeyBox.Text = "";
        _apiStatusLabel.Text = "API key forgotten.";
        FlashStatus("Saved API key removed from Windows Credential Manager.");
    }

    private void SaveSettingsFromUi()
    {
        _settings.ManualAddictionPercent = (int)_addictionPercent.Value;
        _settings.ManualNaturalNerveOverride = (int)_nnbOverride.Value;
        _settings.FastRefreshSeconds = (int)_fastRefreshSeconds.Value;
        _settings.FullRefreshSeconds = (int)_fullRefreshSeconds.Value;
        _settings.AutoRefreshApi = _autoRefreshCheck.Checked;
        _settings.RestoreTabsOnStartup = _restoreTabsCheck.Checked;
        _settings.RememberWindowPlacement = _rememberWindowCheck.Checked;
        _settings.AllowHelperSites = _allowHelperSitesCheck.Checked;
        _settings.GuideActionMode = NormalizeGuideActionMode(_guideActionModeCombo?.SelectedItem?.ToString() ?? _settings.GuideActionMode);
        Storage.SaveSettings(_settings);
        StartTimers();
        UpdateAllUi();
        FlashStatus("Settings saved; live coach recalculated.");
    }

    private void RefreshWhitelistList()
    {
        if (_whitelistList is null) return;
        _whitelistList.Items.Clear();
        foreach (var domain in _settings.UserWhitelistDomains
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x))
            _whitelistList.Items.Add(domain);
    }

    private void AddWhitelistDomainFromUi()
    {
        var raw = _whitelistDomainBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        var domain = NormalizeDomainInput(raw);
        if (string.IsNullOrWhiteSpace(domain))
        {
            MessageBox.Show("Enter a domain such as example.com or a full https:// URL.", "Whitelist");
            return;
        }
        if (!_settings.UserWhitelistDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            _settings.UserWhitelistDomains.Add(domain);
        _settings.UserWhitelistDomains = _settings.UserWhitelistDomains
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        Storage.SaveSettings(_settings);
        _whitelistDomainBox.Clear();
        RefreshWhitelistList();
        FlashStatus("Whitelisted: " + domain);
    }

    private static string NormalizeDomainInput(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return "";
            raw = uri.Host;
        }
        raw = raw.Trim().Trim('.').ToLowerInvariant();
        if (raw.StartsWith("*.")) raw = raw[2..];
        if (raw.Length == 0 || raw.Contains('/') || raw.Contains(' ') || !raw.Contains('.')) return "";
        return raw;
    }

    private void RemoveSelectedWhitelistDomain()
    {
        if (_whitelistList.SelectedItem is not string domain) return;
        _settings.UserWhitelistDomains.RemoveAll(x => x.Equals(domain, StringComparison.OrdinalIgnoreCase));
        Storage.SaveSettings(_settings);
        RefreshWhitelistList();
        FlashStatus("Removed from whitelist: " + domain);
    }

    private void ImportUserscript()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import Torn userscript",
            Filter = "Userscripts (*.user.js;*.js;*.txt)|*.user.js;*.js;*.txt|JavaScript (*.js)|*.js|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            var hasHeader = text.Contains("// ==UserScript==", StringComparison.OrdinalIgnoreCase);
            if (!hasHeader)
            {
                var confirm = MessageBox.Show(
                    "This file does not contain a userscript header. If imported, it will be treated as raw JavaScript and may run on every page. Import anyway?",
                    "Raw JavaScript", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            Directory.CreateDirectory(Storage.UserScriptsPath);
            var sourceName = Path.GetFileName(dialog.FileName);
            var targetName = sourceName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(sourceName) + ".user.js"
                : sourceName;
            targetName = string.Join("_", targetName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(targetName)) targetName = "imported.user.js";

            var target = Path.Combine(Storage.UserScriptsPath, targetName);
            if (File.Exists(target))
            {
                var answer = MessageBox.Show($"{targetName} already exists. Replace it?", "Import userscript", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }
            File.WriteAllText(target, text);
            if (!_settings.EnabledScripts.Contains(targetName, StringComparer.OrdinalIgnoreCase))
                _settings.EnabledScripts.Add(targetName);
            Storage.SaveSettings(_settings);
            RefreshScriptsList();
            MessageBox.Show(
                $"Imported and enabled:\n{targetName}\n\nReload/open a new matching browser tab for it to run. @match/@include rules are honored by Commander.",
                "Userscript imported");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Userscript import failed");
        }
    }

    private void RefreshScriptsList()
    {
        if (_scriptsList is null) return;
        Directory.CreateDirectory(Storage.UserScriptsPath);
        _scriptsList.Items.Clear();
        foreach (var path in Directory.EnumerateFiles(Storage.UserScriptsPath, "*", SearchOption.TopDirectoryOnly)
                     .Where(Storage.IsSupportedScriptFile)
                     .OrderBy(x => Path.GetFileName(x)))
        {
            var name = Path.GetFileName(path);
            var i = _scriptsList.Items.Add(name);
            _scriptsList.SetItemChecked(i, _settings.EnabledScripts.Contains(name, StringComparer.OrdinalIgnoreCase));
        }
    }

    private async void SaveEnabledScripts()
    {
        _settings.EnabledScripts = _scriptsList.CheckedItems.Cast<string>().ToList();
        Storage.SaveSettings(_settings);
        await ApplyScriptsToOpenTabsAsync();
        MessageBox.Show("Enabled userscripts saved and open tabs refreshed.", "Torn Commander");
    }

    private async Task LoadExtensionAsync()
    {
        var active = Active();
        if (active?.WebView.CoreWebView2 is null)
        {
            MessageBox.Show("Open a browser tab first.");
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose an unpacked Chromium extension folder containing manifest.json",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        if (!File.Exists(Path.Combine(dialog.SelectedPath, "manifest.json")))
        {
            MessageBox.Show("That folder does not contain manifest.json.", "Extension manager");
            return;
        }

        try
        {
            await active.WebView.CoreWebView2.Profile.AddBrowserExtensionAsync(dialog.SelectedPath);
            MessageBox.Show("Extension loaded into Torn Commander's persistent WebView2 profile.\n\nWebView2 does not support every Chrome/Edge extension API, so test the extension before relying on it.", "Extension loaded");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not load extension");
        }
    }

    private void AddCustomLink()
    {
        var name = Prompt("Custom helper", "Display name:", "My Torn helper");
        if (string.IsNullOrWhiteSpace(name)) return;
        var url = Prompt("Custom helper", "URL:", "https://");
        if (string.IsNullOrWhiteSpace(url)) return;
        url = TornRoutes.Normalize(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            MessageBox.Show("That URL is invalid.");
            return;
        }
        _settings.CustomLinks.Add(new CustomLink { Name = name.Trim(), Url = uri.ToString() });
        Storage.SaveSettings(_settings);
        RefreshCustomLinks();
    }

    private void RefreshCustomLinks()
    {
        if (_customLinksList is null) return;
        _customLinksList.Items.Clear();
        foreach (var link in _settings.CustomLinks)
            _customLinksList.Items.Add($"{link.Name}  —  {link.Url}");
    }

    private async Task OpenSelectedCustomLinkAsync()
    {
        var i = _customLinksList.SelectedIndex;
        if (i < 0 || i >= _settings.CustomLinks.Count) return;
        await AddBrowserTabAsync(_settings.CustomLinks[i].Url);
    }

    private void DeleteSelectedCustomLink()
    {
        var i = _customLinksList.SelectedIndex;
        if (i < 0 || i >= _settings.CustomLinks.Count) return;
        _settings.CustomLinks.RemoveAt(i);
        Storage.SaveSettings(_settings);
        RefreshCustomLinks();
    }

    private void RebindTrades()
    {
        if (_tradeGrid is null) return;
        _tradeSource.DataSource = null;
        _tradeSource.DataSource = _trades.Select(x => new
        {
            x.Date,
            x.Item,
            Qty = x.Quantity,
            Buy = x.BuyEach,
            Sell = x.SellEach,
            x.Cost,
            x.Profit,
            ROI = Math.Round(x.Roi, 2),
            x.Notes
        }).ToList();
        _tradeGrid.DataSource = _tradeSource;
    }

    private void DeleteSelectedTrade()
    {
        var i = _tradeGrid.CurrentRow?.Index ?? -1;
        if (i < 0 || i >= _trades.Count) return;
        _trades.RemoveAt(i);
        Storage.Save(Storage.TradesPath, _trades);
        RebindTrades();
    }

    private void RebindCasino()
    {
        if (_casinoGrid is null) return;
        _casinoSource.DataSource = null;
        _casinoSource.DataSource = _casino.Select(x => new
        {
            x.Date,
            x.Game,
            Start = x.StartBankroll,
            End = x.EndBankroll,
            P_L = x.ProfitLoss,
            x.Notes
        }).ToList();
        _casinoGrid.DataSource = _casinoSource;
    }

    private void DeleteSelectedCasino()
    {
        var i = _casinoGrid.CurrentRow?.Index ?? -1;
        if (i < 0 || i >= _casino.Count) return;
        _casino.RemoveAt(i);
        Storage.Save(Storage.CasinoPath, _casino);
        RebindCasino();
    }

    private void RecordNnbMilestone()
    {
        var suggested = _recommendation.EstimatedNaturalNerve?.ToString() ?? "";
        var input = Prompt("Record NNB milestone", "Confirmed natural nerve (15, 20, 25, ...):", suggested);
        if (!int.TryParse(input, out var nnb) || nnb < 15 || (nnb - 15) % 5 != 0)
        {
            if (input is not null) MessageBox.Show("Natural nerve should be 15 + a multiple of 5.", "NNB milestone");
            return;
        }

        var totalNerve = _snapshot.Crimes.Sum(x => x.NerveSpent);
        var priorTotal = _milestones.OrderBy(x => x.Date).LastOrDefault()?.TotalCrimeNerveSpent ?? 0;
        var mainCrimes = string.Join(", ", _snapshot.Crimes
            .OrderByDescending(x => x.NerveSpent)
            .Take(3)
            .Where(x => x.NerveSpent > 0)
            .Select(x => x.Name));

        _milestones.Add(new NnbMilestone
        {
            Date = DateTime.Now,
            NaturalNerve = nnb,
            DisplayedMaxNerve = _snapshot.MaxNerve ?? 0,
            TotalCrimeNerveSpent = totalNerve,
            NerveSpentSincePrior = Math.Max(0, totalNerve - priorTotal),
            MainCrimes = mainCrimes,
            Notes = $"Recorded from Torn Commander v0.6.33; addiction {(_snapshot.DetectedAddictionPercent ?? _settings.ManualAddictionPercent)}% ({(_snapshot.DetectedAddictionPercent is not null ? "API" : "fallback")})"
        });
        _milestones = _milestones.OrderBy(x => x.Date).ToList();
        Storage.Save(Storage.MilestonesPath, _milestones);
        _settings.ManualNaturalNerveOverride = nnb;
        _nnbOverride.Value = Math.Clamp(nnb, (int)_nnbOverride.Minimum, (int)_nnbOverride.Maximum);
        Storage.SaveSettings(_settings);
        UpdateAllUi();
        FlashStatus($"Recorded confirmed {nnb} NNB milestone.");
    }

    private void ShowNnbHistory()
    {
        if (_milestones.Count == 0)
        {
            MessageBox.Show("No confirmed NNB milestones recorded yet.\n\nUse Record NNB when you confirm a natural nerve jump.", "NNB history");
            return;
        }

        var sb = new StringBuilder();
        foreach (var m in _milestones.OrderBy(x => x.Date))
        {
            sb.AppendLine($"{m.Date:g}  •  NNB {m.NaturalNerve}  •  displayed max {m.DisplayedMaxNerve}");
            if (m.NerveSpentSincePrior > 0) sb.AppendLine($"   Crime nerve since prior record: {m.NerveSpentSincePrior:N0}");
            if (!string.IsNullOrWhiteSpace(m.MainCrimes)) sb.AppendLine($"   Main crimes: {m.MainCrimes}");
        }
        MessageBox.Show(sb.ToString(), "NNB milestone history");
    }

    private void CopyDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TORN COMMANDER v0.3.0 DIAGNOSTICS");
        sb.AppendLine($"Windows: {Environment.OSVersion}");
        sb.AppendLine($"Runtime: {Environment.Version}");
        sb.AppendLine($"Browser tabs: {_browserTabs.TabPages.Count}");
        sb.AppendLine($"API key saved: {!string.IsNullOrWhiteSpace(CredentialManager.LoadApiKey(CurrentProfileId))}");
        sb.AppendLine($"Last fast sync: {(_snapshot.FastRetrievedAt == DateTime.MinValue ? "never" : _snapshot.FastRetrievedAt.ToString("O"))}");
        sb.AppendLine($"Last full sync: {(_snapshot.FullRetrievedAt == DateTime.MinValue ? "never" : _snapshot.FullRetrievedAt.ToString("O"))}");
        sb.AppendLine($"API warnings: {_snapshot.Errors.Count}");
        sb.AppendLine($"Detected level: {_snapshot.Level?.ToString() ?? "—"}");
        sb.AppendLine($"Nerve: {_snapshot.CurrentNerve?.ToString() ?? "—"}/{_snapshot.MaxNerve?.ToString() ?? "—"}");
        sb.AppendLine($"Estimated NNB: {_recommendation.EstimatedNaturalNerve?.ToString() ?? "—"}");
        sb.AppendLine($"Crime rows: {_snapshot.Crimes.Count}");
        sb.AppendLine($"Awards scanned: {_snapshot.Awards.Count}; achieved: {_snapshot.Awards.Count(x => x.Achieved)}; unspent merits: {_snapshot.MeritAvailable}");
        sb.AppendLine("Open tabs:");
        foreach (var tab in _browserTabObjects)
            sb.AppendLine("- " + (tab.WebView.Source?.ToString() ?? tab.InitialUrl));
        if (_snapshot.Errors.Count > 0)
        {
            sb.AppendLine("API warnings:");
            foreach (var error in _snapshot.Errors) sb.AppendLine("- " + error);
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            FlashStatus("Diagnostics copied to clipboard. API key value is never included.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy diagnostics");
        }
    }

    private void ExportBackup()
    {
        try
        {
            var path = Storage.ExportBackup();
            MessageBox.Show("Backup created (API key intentionally excluded):\n\n" + path, "Torn Commander backup");
            FlashStatus("Backup exported: " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Backup failed");
        }
    }

    private void OpenAiWithContext()
    {
        var text = BuildAiContext();
        try
        {
            Clipboard.SetText(text);
            FlashStatus("Live account context copied. Paste it into the ChatGPT tab.");
        }
        catch { }
        _ = AddBrowserTabAsync("https://chatgpt.com/");
    }

    private string BuildAiContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("TORN COMMANDER LIVE CONTEXT");
        sb.AppendLine("Goal: maximize long-term natural nerve while also progressing money, racing, trading, company/faction and general account efficiency. Keep Torn actions manual.");
        sb.AppendLine($"Profile: {_snapshot.PlayerName ?? "unknown"} [{_snapshot.PlayerId?.ToString() ?? "—"}], level {_snapshot.Level?.ToString() ?? "—"}");
        var activeAddiction = _snapshot.DetectedAddictionPercent ?? _settings.ManualAddictionPercent;
        sb.AppendLine($"Current addiction used by coach: {activeAddiction}% ({(_snapshot.DetectedAddictionPercent is not null ? "API-detected" : "manual fallback")})");
        if (_snapshot.CurrentNerve is int cur && _snapshot.MaxNerve is int max)
            sb.AppendLine($"Nerve: {cur}/{max}");
        if (_recommendation.EstimatedNaturalNerve is int nnb)
            sb.AppendLine($"Working estimated natural nerve: {nnb}");
        sb.AppendLine($"Current coach phase: {_recommendation.Phase}");
        sb.AppendLine($"Coach priority: {_recommendation.Headline}");
        sb.AppendLine($"Education/rehab: {_recommendation.EducationAdvice}");
        if (!string.IsNullOrWhiteSpace(_snapshot.CompanyName)) sb.AppendLine("Company: " + _snapshot.CompanyName);
        if (!string.IsNullOrWhiteSpace(_snapshot.FactionName)) sb.AppendLine("Faction: " + _snapshot.FactionName);
        if (_snapshot.RacingSkill is double rs) sb.AppendLine($"Racing skill: {rs:0.####}");
        if (_snapshot.Wallet is long wallet) sb.AppendLine($"Wallet: ${wallet:N0}");
        if (_snapshot.ManualLabor is int man && _snapshot.Intelligence is int intel && _snapshot.Endurance is int end)
            sb.AppendLine($"Work stats: MAN {man:N0}, INT {intel:N0}, END {end:N0}");
        sb.AppendLine($"OC2.0 active: {(_snapshot.InOrganizedCrime == true ? "yes" : _snapshot.InOrganizedCrime == false ? "no" : "unknown")}");
        if (_snapshot.Crimes.Count > 0)
        {
            sb.AppendLine("Crime progress:");
            foreach (var c in _snapshot.Crimes.OrderBy(c => c.Id))
                sb.AppendLine($"- {c.Name}: CS {c.Skill:0.##}, nerve spent {c.NerveSpent:N0}, attempts {c.Attempts:N0}, success {c.SuccessRate:0.0}%, crits {c.CriticalFail}, plan: {RecommendationEngine.PlanForCrime(c, _recommendation.EstimatedNaturalNerve)}");
        }
        sb.AppendLine("Question/context: I am asking from the Torn Commander desktop client; use the current 2026 Torn/Crimes 2.0 mechanics and update advice from the live account state above.");
        return sb.ToString();
    }

    private void PersistOnClose()
    {
        try
        {
            _settings.ManualAddictionPercent = (int)_addictionPercent.Value;
            _settings.ManualNaturalNerveOverride = (int)_nnbOverride.Value;
            _settings.AutoRefreshApi = _autoRefreshCheck.Checked;
            _settings.RestoreTabsOnStartup = _restoreTabsCheck.Checked;
            _settings.RememberWindowPlacement = _rememberWindowCheck.Checked;
            _settings.AllowHelperSites = _allowHelperSitesCheck.Checked;
            _settings.GuideActionMode = NormalizeGuideActionMode(_guideActionModeCombo?.SelectedItem?.ToString() ?? _settings.GuideActionMode);
            _settings.FastRefreshSeconds = (int)_fastRefreshSeconds.Value;
            _settings.FullRefreshSeconds = (int)_fullRefreshSeconds.Value;
            if (_mainSplit is not null && _mainSplit.ClientSize.Width > 0)
                _settings.CompanionPaneWidth = Math.Max(_mainSplit.Panel2MinSize, _mainSplit.ClientSize.Width - _mainSplit.SplitterDistance - _mainSplit.SplitterWidth);

            if (_settings.RememberWindowPlacement)
            {
                var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (bounds.Width >= MinimumSize.Width && bounds.Height >= MinimumSize.Height)
                {
                    _settings.WindowX = bounds.X;
                    _settings.WindowY = bounds.Y;
                    _settings.WindowWidth = bounds.Width;
                    _settings.WindowHeight = bounds.Height;
                    _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
                    _settings.WindowPlacementSaved = true;
                }
            }

            SaveCurrentProfileTabs();
            _settings.OpenTabs = CurrentProfile.OpenTabs.ToList(); // compatibility with older backups
            Storage.SaveSettings(_settings);
            if (_racingNotes is not null)
                Storage.Save(Storage.RacingPath, new RacingNote { Notes = _racingNotes.Text });
        }
        catch { }

        DisposeLossWorkers();

        foreach (var tab in _browserTabObjects.ToList())
        {
            try { tab.Dispose(); } catch { }
        }
    }

    private static string? Prompt(string title, string label, string initial = "")
    {
        using var f = new Form
        {
            Text = title,
            Width = 520,
            Height = 170,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            BackColor = Color.FromArgb(24, 27, 33),
            ForeColor = Color.White
        };
        var l = new Label { Text = label, Left = 16, Top = 16, Width = 470, ForeColor = Color.White };
        var t = new TextBox { Text = initial, Left = 16, Top = 43, Width = 470, BackColor = Color.FromArgb(34, 38, 46), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var ok = new Button { Text = "OK", Left = 326, Top = 80, Width = 76, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 410, Top = 80, Width = 76, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { l, t, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? t.Text : null;
    }

    private FlowLayoutPanel MakeFlow()
    {
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = PanelBg,
            Padding = new Padding(12)
        };
        p.SizeChanged += (_, _) => ResizeFlowChildren(p);
        p.ControlAdded += (_, _) => ResizeFlowChildren(p);
        return p;
    }

    private void ApplyCompanionPaneWidth()
    {
        if (_mainSplit is null || _mainSplit.IsDisposed) return;

        var width = _mainSplit.ClientSize.Width;
        if (width <= 0) return;

        // IMPORTANT: SplitContainer validates Panel1MinSize / Panel2MinSize immediately.
        // During form construction its width can still be 0, so assigning those values in
        // the object initializer can crash before the window is shown. Configure them here
        // only after layout has a real width, and establish a valid splitter position first.
        const int requestedBrowserMin = 420;
        const int requestedCompanionMin = 260;
        var splitter = _mainSplit.SplitterWidth;

        var browserMin = Math.Min(requestedBrowserMin, Math.Max(0, width - splitter));
        var companionMin = Math.Min(requestedCompanionMin, Math.Max(0, width - browserMin - splitter));
        var minDistance = browserMin;
        var maxDistance = width - companionMin - splitter;
        if (maxDistance < minDistance) return;

        var maxCompanion = Math.Max(companionMin, Math.Min(900, width - browserMin - splitter));
        var desiredCompanion = Math.Clamp(_settings.CompanionPaneWidth, companionMin, maxCompanion);
        var desiredDistance = Math.Clamp(width - desiredCompanion - splitter, minDistance, maxDistance);

        // Keep constraints at zero while positioning, then apply the real limits.
        _mainSplit.Panel1MinSize = 0;
        _mainSplit.Panel2MinSize = 0;
        _mainSplit.SplitterDistance = desiredDistance;
        _mainSplit.Panel1MinSize = browserMin;
        _mainSplit.Panel2MinSize = companionMin;

        _splitterInitialized = true;
        ResizeAllCompanionFlows();
    }

    private void ResizeAllCompanionFlows()
    {
        if (_mainSplit is null) return;
        foreach (var flow in FindControls<FlowLayoutPanel>(_mainSplit.Panel2))
            if (flow.FlowDirection == FlowDirection.TopDown && !flow.WrapContents)
                ResizeFlowChildren(flow);
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            if (c is T hit) yield return hit;
            foreach (var nested in FindControls<T>(c)) yield return nested;
        }
    }

    private void ResizeFlowChildren(FlowLayoutPanel p)
    {
        if (p.IsDisposed || p.ClientSize.Width <= 80) return;
        var usable = Math.Max(240, p.ClientSize.Width - p.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 10);

        p.SuspendLayout();
        try
        {
            foreach (Control c in p.Controls)
            {
                // Text should wrap to the pane instead of creating a horizontal scrollbar.
                if (c is Label label)
                {
                    label.MaximumSize = new Size(usable, 0);
                    continue;
                }

                if (c is Panel separator && separator.Height <= 3)
                {
                    separator.Width = usable;
                    continue;
                }

                if (c is FlowLayoutPanel row)
                {
                    row.WrapContents = true;
                    row.MaximumSize = new Size(usable, 0);
                    row.Width = usable;
                    continue;
                }

                if (c is TableLayoutPanel table)
                {
                    table.Width = usable;
                    table.MaximumSize = new Size(usable, 0);
                    continue;
                }

                if (c is DataGridView || c is RichTextBox || c is ListBox || c is CheckedListBox)
                {
                    c.Width = usable;
                    continue;
                }

                // Long standalone text boxes (API key, notes, etc.) can use the pane.
                if (c is TextBoxBase && c.Width >= 280)
                    c.Width = usable;
            }
        }
        finally
        {
            p.ResumeLayout(true);
        }
    }

    private Button MakeButton(string text, EventHandler click, int width = 90)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = FieldBg,
            ForeColor = TextMain,
            Cursor = Cursors.Hand,
            Margin = new Padding(3)
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(60, 65, 74);
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 54, 65);
        b.Click += click;
        return b;
    }

    private Label Heading(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = Accent,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            MaximumSize = new Size(1200, 0),
            Margin = new Padding(0, 8, 0, 5)
        };

    private Label BigLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            ForeColor = TextMain,
            Font = new Font("Segoe UI Semibold", 10.5f),
            Margin = new Padding(0, 5, 0, 5)
        };

    private Label Body(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, 4, 0, 8)
        };

    private Label Muted(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            ForeColor = TextMuted,
            Margin = new Padding(0, 3, 0, 8)
        };

    private Control Separator()
        => new Panel { Width = 1, Height = 1, BackColor = Color.FromArgb(55, 60, 70), Margin = new Padding(0, 10, 0, 10) };

    private NumericUpDown Num(decimal value, decimal min, decimal max)
        => new()
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 150,
            BackColor = FieldBg,
            ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle
        };

    private NumericUpDown MoneyNum()
        => new()
        {
            Minimum = 0,
            Maximum = 1_000_000_000_000m,
            DecimalPlaces = 0,
            ThousandsSeparator = true,
            Width = 175,
            BackColor = FieldBg,
            ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle
        };

    private TextBox DarkTextBox(string text = "")
        => new()
        {
            Text = text,
            Width = 245,
            BackColor = FieldBg,
            ForeColor = TextMain,
            BorderStyle = BorderStyle.FixedSingle
        };

    private CheckBox DarkCheck(string text, bool value)
        => new()
        {
            Text = text,
            Checked = value,
            AutoSize = true,
            ForeColor = TextMain,
            BackColor = PanelBg,
            Margin = new Padding(3, 6, 3, 6)
        };

    private void AddLabeled(TableLayoutPanel grid, string label, Control control)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = TextMain,
            MaximumSize = new Size(220, 0),
            Margin = new Padding(0, 6, 8, 6)
        }, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private DataGridView MakeGrid(int width = 475, int height = 300)
    {
        var g = new DataGridView
        {
            Width = width,
            Height = height,
            BackgroundColor = FieldBg,
            ForeColor = TextMain,
            GridColor = Color.FromArgb(60, 65, 74),
            BorderStyle = BorderStyle.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false
        };
        g.DefaultCellStyle.BackColor = FieldBg;
        g.DefaultCellStyle.ForeColor = TextMain;
        g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(55, 78, 105);
        g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(42, 46, 55);
        g.ColumnHeadersDefaultCellStyle.ForeColor = TextMain;
        g.EnableHeadersVisualStyles = false;
        return g;
    }

    private void DrawDarkTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0) return;
        var rect = tabs.GetTabRect(e.Index);
        var selected = e.Index == tabs.SelectedIndex;
        using var bg = new SolidBrush(selected ? Color.FromArgb(45, 50, 60) : Color.FromArgb(29, 32, 38));
        e.Graphics.FillRectangle(bg, rect);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, Font, rect,
            selected ? Color.White : TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Open folder"); }
    }

    private void FlashStatus(string text)
    {
        if (_status is null) return;
        _status.Text = text;
    }
}
