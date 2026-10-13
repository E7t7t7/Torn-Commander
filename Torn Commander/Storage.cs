using System.IO.Compression;
using System.Text.Json;

namespace TornCommander;

internal static class Storage
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TornCommander");

    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public static string TradesPath => Path.Combine(Root, "trades.json");
    public static string MilestonesPath => Path.Combine(Root, "nnb_milestones.json");
    public static string CasinoPath => Path.Combine(Root, "casino_sessions.json");
    public static string RacingPath => Path.Combine(Root, "racing_notes.json");
    public static string ShoppingCartPath => Path.Combine(Root, "shopping_cart.json");
    public static string LossOrdersPath => Path.Combine(Root, "loss_orders.json");
    public static string TravelHistoryPath => Path.Combine(Root, "travel_stock_history.json");
    public static string TravelForecastCachePath => Path.Combine(Root, "travel_forecast_cache.json");
    public static string BrowserDataPath => BrowserDataPathFor("main");
    public static string BrowserDataPathFor(string profileId)
    {
        var safe = string.Concat((profileId ?? "main").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safe)) safe = "main";
        // Preserve the pre-profile browser session for the original/main account.
        if (safe.Equals("main", StringComparison.OrdinalIgnoreCase)) return Path.Combine(Root, "WebView2Profile");
        return Path.Combine(Root, "Profiles", safe, "WebView2Profile");
    }
    public static string UserScriptsPath => Path.Combine(Root, "Scripts");
    public static string BundledScriptsPath => Path.Combine(AppContext.BaseDirectory, "BundledScripts");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    static Storage()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(UserScriptsPath);
    }

    public static T Load<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            var text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(text, Options) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static void SaveAtomic<T>(string path, T value) => AtomicJsonFile.Save(path, value, Options);

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }

    public static AppSettings LoadSettings() => Load(SettingsPath, new AppSettings());
    public static void SaveSettings(AppSettings settings) => Save(SettingsPath, settings);

    public static List<string> SeedBundledScripts()
    {
        var names = new List<string>();
        try
        {
            Directory.CreateDirectory(UserScriptsPath);
            if (!Directory.Exists(BundledScriptsPath)) return names;

            foreach (var source in Directory.EnumerateFiles(BundledScriptsPath, "*", SearchOption.TopDirectoryOnly)
                         .Where(IsSupportedScriptFile)
                         .Where(path => !Path.GetFileName(path).Equals("Torn_Loss_AutoCycle.js", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(source);
                var destination = Path.Combine(UserScriptsPath, name);
                File.Copy(source, destination, overwrite: true);
                names.Add(name);
            }
        }
        catch
        {
            // A bundled helper must never stop Commander from launching.
        }
        return names;
    }

    public static bool IsSupportedScriptFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExportBackup()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var outPath = Path.Combine(desktop, $"TornCommander_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);

        foreach (var file in new[] { SettingsPath, TradesPath, MilestonesPath, CasinoPath, RacingPath, ShoppingCartPath, LossOrdersPath, TravelHistoryPath, TravelForecastCachePath })
        {
            if (File.Exists(file))
                zip.CreateEntryFromFile(file, Path.GetFileName(file));
        }

        if (Directory.Exists(UserScriptsPath))
        {
            foreach (var script in Directory.GetFiles(UserScriptsPath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(UserScriptsPath, script);
                zip.CreateEntryFromFile(script, Path.Combine("Scripts", rel));
            }
        }

        // API key is intentionally NOT exported; it lives in Windows Credential Manager.
        return outPath;
    }
}
