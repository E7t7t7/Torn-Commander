namespace TornCommander;

public sealed class AppProfile
{
    public string Id { get; set; } = "main";
    public string Name { get; set; } = "Player 1";
    public List<string> OpenTabs { get; set; } = new() { TornRoutes.Home };
}

public sealed class AppSettings
{
    public string CurrentProfileId { get; set; } = "main";
    public List<AppProfile> Profiles { get; set; } = new()
    {
        new AppProfile { Id = "main", Name = "Player 1" }
    };
    public int FastRefreshSeconds { get; set; } = 90;
    public int FullRefreshSeconds { get; set; } = 600;
    public bool AutoRefreshApi { get; set; } = true;
    public bool AllowHelperSites { get; set; } = true;
    public bool RestoreTabsOnStartup { get; set; } = false;
    public bool RememberWindowPlacement { get; set; } = true;
    public bool WindowPlacementSaved { get; set; } = false;
    public int WindowX { get; set; } = 120;
    public int WindowY { get; set; } = 80;
    public int WindowWidth { get; set; } = 1500;
    public int WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; } = false;
    public int CompanionPaneWidth { get; set; } = 520;
    // Per-install flag: existing profile labels were reviewed (never stores credentials).
    public bool LegacyExampleLabelsReviewed { get; set; } = false;
    public bool BundledScriptsInitialized { get; set; } = false;
    public int BundledScriptsGeneration { get; set; } = 0;
    public bool ApiFullAccessVerified { get; set; } = false;
    public string GuideActionMode { get; set; } = "Open & Configure";
    public string MeritHunterMode { get; set; } = "NNB Growth";
    public long ShoppingMaxUnitPrice { get; set; } = 0;
    public long ShoppingMaxSpend { get; set; } = 0;
    public long ShoppingCashReserve { get; set; } = 100000;
    public bool ShoppingIgnoreLimits { get; set; } = false;
    public int TravelCapacity { get; set; } = 29;
    public string TravelFlightType { get; set; } = "Airstrip";
    public bool TravelApplyMarketTax { get; set; } = true;
    public int TravelForecastHorizonHours { get; set; } = 24;
    public double TravelMinConfidence { get; set; } = 0.35;
    public bool TravelIncludeLowConfidence { get; set; } = true;
    public long TravelCashBudget { get; set; } = 0; // 0 = no manual budget limit
    public bool TravelMailingBook { get; set; } = false;
    public bool TravelInStockOnly { get; set; } = false;
    public bool TravelAutoMonitor { get; set; } = true;
    public int TravelMonitorIntervalMinutes { get; set; } = 15;
    public int TravelTransferMinutes { get; set; } = 10;
    public bool TravelPlanStartNow { get; set; } = true;
    public bool TravelPauseUntilHome { get; set; } = false;
    public string UpdateGithubRepo { get; set; } = "E7t7t7/Torn-Commander";
    // Dashboard palette is local to this Windows installation, not carried in release ZIPs.
    public string UiTheme { get; set; } = "midnight";
    public bool TravelShowRouteMap { get; set; } = false;
    public DateTime TravelPlanCustomStartUtc { get; set; } = DateTime.MinValue;
    // Neutral for friends' first installations; established users retain locally saved values.
    public int ManualAddictionPercent { get; set; } = 0;
    public int ManualNaturalNerveOverride { get; set; } = 0;
    public List<string> OpenTabs { get; set; } = new() { TornRoutes.Home };
    public List<string> EnabledScripts { get; set; } = new();
    public List<string> UserWhitelistDomains { get; set; } = new();
    public List<CustomLink> CustomLinks { get; set; } = new();
}

public sealed class CustomLink
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "Custom";
}

public sealed class TradeEntry
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string Item { get; set; } = "";
    public int Quantity { get; set; }
    public decimal BuyEach { get; set; }
    public decimal SellEach { get; set; }
    public string Notes { get; set; } = "";
    public decimal Cost => Quantity * BuyEach;
    public decimal Revenue => Quantity * SellEach;
    public decimal Profit => Revenue - Cost;
    public decimal Roi => Cost == 0 ? 0 : Profit / Cost * 100m;
}

public sealed class NnbMilestone
{
    public DateTime Date { get; set; } = DateTime.Now;
    public int NaturalNerve { get; set; }
    public int DisplayedMaxNerve { get; set; }
    public int NerveSpentSincePrior { get; set; }
    public int TotalCrimeNerveSpent { get; set; }
    public string MainCrimes { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class CasinoSession
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string Game { get; set; } = "Blackjack";
    public decimal StartBankroll { get; set; }
    public decimal EndBankroll { get; set; }
    public string Notes { get; set; } = "";
    public decimal ProfitLoss => EndBankroll - StartBankroll;
}

public sealed class RacingNote
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string Track { get; set; } = "";
    public string Car { get; set; } = "";
    public string Result { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class CrimeProgress
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Enhancer { get; set; } = "";
    public double Skill { get; set; }
    public int NerveSpent { get; set; }
    public int ProgressionBonus { get; set; }
    public int Attempts { get; set; }
    public int Success { get; set; }
    public int Fail { get; set; }
    public int CriticalFail { get; set; }
    public double SuccessRate => Attempts <= 0 ? 0 : Success * 100.0 / Attempts;
}


public sealed class MeritAward
{
    public int Id { get; set; }
    public string AwardType { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string CrimesVersion { get; set; } = "";
    public string Availability { get; set; } = "Available";
    public bool AvailableForCurrentCrimeVersion { get; set; } = true;
    public bool ProgressConflict { get; set; }
    public bool Achieved { get; set; }
    public long? Current { get; set; }
    public int? Target { get; set; }
    public double? ProgressPercent { get; set; }
    public string ProgressSource { get; set; } = "";
    public int EaseScore { get; set; }
    public string Effort { get; set; } = "";
    public string Advice { get; set; } = "";
    public string Route { get; set; } = TornRoutes.Awards;
}

public sealed class MeritUpgradeState
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Level { get; set; }
    public int MaxLevel { get; set; } = 10;
}


public sealed class ShoppingTarget
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int DesiredQuantity { get; set; } = 1;
    public int PurchasedQuantity { get; set; }
    public long PurchasedCost { get; set; }
    public int RemainingQuantity => Math.Max(0, DesiredQuantity - PurchasedQuantity);
}

public sealed class MarketCatalogItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    // Official reference market value: useful as a guide, not a live buy/sell listing.
    public long MarketValue { get; set; }
}

public sealed class MarketPriceTier
{
    public long Price { get; set; }
    public int Amount { get; set; }
}

public sealed class MarketPurchasePlan
{
    public long Price { get; set; }
    public int Quantity { get; set; }
    public long Cost => Price * (long)Quantity;
}

public sealed class MarketScanResult
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public List<MarketPriceTier> Tiers { get; set; } = new();
    public List<MarketPurchasePlan> Plan { get; set; } = new();
    public int RequestedQuantity { get; set; }
    public int PlannedQuantity => Plan.Sum(x => x.Quantity);
    public int AvailableQuantity => Tiers.Sum(x => x.Amount);
    public long BestPrice => Tiers.Count == 0 ? 0 : Tiers.Min(x => x.Price);
    public long PlannedCost => Plan.Sum(x => x.Cost);
    public decimal AveragePrice => PlannedQuantity <= 0 ? 0m : (decimal)PlannedCost / PlannedQuantity;
    public string Status { get; set; } = "Not scanned";
}


public sealed class LossOrder
{
    public List<LossCommitReceipt> CommitReceipts { get; set; } = new();
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string TargetPlayerId { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public int RequestedLosses { get; set; } = 1;
    public int CompletedA { get; set; }
    public int CompletedB { get; set; }
    public long PayPerLoss { get; set; }
    public string ProfileAId { get; set; } = "main";
    public string ProfileBId { get; set; } = "wife";
    public string NextA { get; set; } = "Attack";
    public string NextB { get; set; } = "Attack";
    public string Status { get; set; } = "Active";
    public string Notes { get; set; } = "";
    public int CompletedTotal => CompletedA + CompletedB;
    public int Remaining => Math.Max(0, RequestedLosses - CompletedTotal);
    public long Gross => CompletedTotal * PayPerLoss;
}

public sealed class TornSnapshot
{
    public string? PlayerName { get; set; }
    public int? PlayerId { get; set; }
    public int? Level { get; set; }
    public int? CurrentNerve { get; set; }
    public int? MaxNerve { get; set; }
    public int? CurrentEnergy { get; set; }
    public int? MaxEnergy { get; set; }
    public double? RacingSkill { get; set; }
    public long? Wallet { get; set; }
    public string? JobName { get; set; }
    public string? CompanyName { get; set; }
    public string? FactionName { get; set; }
    public bool? NerveRefillAvailable { get; set; }
    public bool? InOrganizedCrime { get; set; }
    public int? DetectedAddictionPercent { get; set; }
    public string? AddictionSource { get; set; }
    public int? ManualLabor { get; set; }
    public int? Intelligence { get; set; }
    public int? Endurance { get; set; }
    public int MeritAvailable { get; set; }
    public int MeritUsed { get; set; }
    public string CrimeVersion { get; set; } = "";
    public DateTime FastRetrievedAt { get; set; } = DateTime.MinValue;
    public DateTime FullRetrievedAt { get; set; } = DateTime.MinValue;

    public List<string> FactionPerks { get; set; } = new();
    public List<string> JobPerks { get; set; } = new();
    public List<string> PropertyPerks { get; set; } = new();
    public List<string> EducationPerks { get; set; } = new();
    public List<string> EnhancerPerks { get; set; } = new();
    public List<string> MeritPerks { get; set; } = new();
    public List<string> BookPerks { get; set; } = new();
    public List<string> StockPerks { get; set; } = new();
    public List<int> CompletedEducationIds { get; set; } = new();
    public string? CurrentEducationName { get; set; }
    public int? CurrentEducationTimeLeft { get; set; }

    public List<CrimeProgress> Crimes { get; set; } = new();
    public List<MeritAward> Awards { get; set; } = new();
    public List<MeritUpgradeState> MeritUpgrades { get; set; } = new();
    public Dictionary<string, long> PersonalStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RawJson { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; set; } = new();
}

public sealed class GuideActionRecommendation
{
    public string Crime { get; set; } = "";
    public string Target { get; set; } = "";
    public string Option { get; set; } = "";
    public string Url { get; set; } = TornRoutes.Crimes;
    public string Summary { get; set; } = "";
    public bool SupportsConfigure { get; set; }
    public bool SupportsRunOnce { get; set; }
}

public sealed class RecommendationResult
{
    public string Headline { get; set; } = "Waiting for API data";
    public string Phase { get; set; } = "Unknown";
    public string Why { get; set; } = "Save/test your API key, then refresh.";
    public string NextUnlock { get; set; } = "—";
    public string NerveAdvice { get; set; } = "—";
    public string EducationAdvice { get; set; } = "—";
    public string CrimeAdvice { get; set; } = "—";
    public string AccountAdvice { get; set; } = "—";
    public int? EstimatedNaturalNerve { get; set; }
    public int? ParsedBonusNerve { get; set; }
    public List<string> Checklist { get; set; } = new();
    public GuideActionRecommendation? GuidedAction { get; set; }
}

public sealed class TravelObservation
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Country { get; set; } = "";
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Category { get; set; } = "";
    public int Quantity { get; set; }
    public long BuyPrice { get; set; }
    public long MarketValue { get; set; }
}

public sealed class TravelForecastItem
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public string Country { get; set; } = "";
    public string Category { get; set; } = "";
    public int? Stock { get; set; }
    public long BuyPrice { get; set; }
    public long MarketValue { get; set; }
    public long ProfitPerItem { get; set; }
    public double ProfitPerHour { get; set; }
    public int Units { get; set; }
    public long CashRequired { get; set; }
    public long TripProfit { get; set; }
    public double RoiPercent { get; set; }
    public int FlightMinutesOneWay { get; set; }
    public string StockStatus { get; set; } = "Unverified";
    public DateTime? StockUpdatedAtUtc { get; set; }
    public DateTime? PricingUpdatedAtUtc { get; set; }
    public bool IsStale { get; set; }
    public DateTime? RestockAtUtc { get; set; }
    public DateTime? SuggestedDepartureUtc { get; set; }
    public DateTime? ArrivalAtUtc { get; set; }
    public double Confidence { get; set; }
    public string ConfidenceLabel { get; set; } = "Learning";
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class TravelForecastSnapshot
{
    public TravelFeedDiagnostics? Diagnostics { get; set; }
    public DateTime RetrievedAtUtc { get; set; } = DateTime.MinValue;
    public string Source { get; set; } = "Not loaded";
    public string Error { get; set; } = "";
    public List<TravelForecastItem> Items { get; set; } = new();
}

