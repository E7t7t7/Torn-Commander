namespace TornCommander;
public sealed partial class MainForm
{
    private object ScriptToolsDashboardData()
    {
        Directory.CreateDirectory(Storage.UserScriptsPath);
        return Directory.EnumerateFiles(Storage.UserScriptsPath).Where(Storage.IsSupportedScriptFile)
            .Select(path => new { name = Path.GetFileName(path), enabled = _settings.EnabledScripts.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase) }).ToList();
    }
    private async Task OpenScriptToolAsync(string name)
    {
        if (Path.GetFileName(name) != name || !File.Exists(Path.Combine(Storage.UserScriptsPath, name)))
            throw new InvalidOperationException("Install this helper in Manage Userscripts first.");
        if (!_settings.EnabledScripts.Contains(name, StringComparer.OrdinalIgnoreCase))
            await SetScriptEnabled(name, true);
        var route = name.Contains("Blackjack", StringComparison.OrdinalIgnoreCase) ? "https://www.torn.com/page.php?sid=blackjack"
            : name.Contains("Racing", StringComparison.OrdinalIgnoreCase) ? TornRoutes.Racing
            : name.Contains("Crack", StringComparison.OrdinalIgnoreCase) || name.Contains("Crime", StringComparison.OrdinalIgnoreCase) || name.Contains("Graffiti", StringComparison.OrdinalIgnoreCase) ? TornRoutes.Crimes
            : name.Contains("Cart", StringComparison.OrdinalIgnoreCase) ? "https://www.torn.com/page.php?sid=ItemMarket"
            : TornRoutes.Home;
        await AddBrowserTabAsync(route);
    }
}
