using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TornCommander;

public sealed partial class MainForm
{
    private bool _updatingCommander;

    // Compare against the real compiled assembly, never a hard-coded past release.
    private static Version RunningCommanderVersion => new Version(
        typeof(MainForm).Assembly.GetName().Version?.Major ?? 0,
        typeof(MainForm).Assembly.GetName().Version?.Minor ?? 0,
        Math.Max(0, typeof(MainForm).Assembly.GetName().Version?.Build ?? 0));

    private void SaveUpdateRepository(string input)
    {
        input = input.Trim();
        if (input.Length != 0 && !Regex.IsMatch(input, @"^[A-Za-z0-9_.-]{1,60}/[A-Za-z0-9_.-]{1,100}$"))
        {
            MessageBox.Show("Enter a public GitHub repository as owner/repository, for example ExampleOwner/TornCommander, or leave blank.",
                "Update source", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _settings.UpdateGithubRepo = input;
        Storage.SaveSettings(_settings);
        FlashStatus(input.Length == 0 ? "GitHub update source cleared." : "GitHub update source: " + input);
        UpdateModernDashboard();
    }

    private async Task CheckCommanderGithubUpdateAsync()
    {
        if (_updatingCommander) return;
        var repo = (_settings.UpdateGithubRepo ?? "").Trim();
        if (string.IsNullOrWhiteSpace(repo)) repo = "E7t7t7/Torn-Commander";
        if (!Regex.IsMatch(repo, @"^[A-Za-z0-9_.-]{1,60}/[A-Za-z0-9_.-]{1,100}$"))
        {
            MessageBox.Show("First set a PUBLIC GitHub Releases repository (owner/repository) in Settings.",
                "Commander updater", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _updatingCommander = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TornCommander-Updater/0.6.8");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.GetAsync("https://api.github.com/repos/" + repo + "/releases/latest");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                MessageBox.Show("This repository has no published public Releases yet. The update channel is configured correctly; check again after a Windows build has been published.",
                    "No published releases", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tv) ? tv.GetString() ?? "" : "";
            var versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var remote))
                throw new InvalidDataException("Release tag must be a version such as v0.6.5.");
            if (remote <= RunningCommanderVersion)
            {
                MessageBox.Show("Commander v" + RunningCommanderVersion.ToString(3) + " is already up-to-date for this repository. Latest release: " + tag,
                    "No update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub release has no downloadable assets.");
            JsonElement? selected = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Equals("Torn_Commander_v" + versionText + ".zip", StringComparison.OrdinalIgnoreCase))
                { selected = asset; break; }
            }
            if (!selected.HasValue) throw new InvalidDataException("Release must include Torn_Commander_vX.Y.Z.zip as an asset, not GitHub's generic source ZIP.");
            var chosen = selected.Value;
            var url = chosen.GetProperty("browser_download_url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith("/" + repo + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release asset URL is outside the configured GitHub repository.");
            var choice = MessageBox.Show($"Install Torn Commander {tag} from {repo}?\n\nThe current app will close, existing local profile data will remain in %LOCALAPPDATA%, and an installer backup will be kept.\n\nOnly install releases you trust.",
                "Commander update available", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes) return;
            var staging = Path.Combine(Path.GetTempPath(), "TornCommanderUpdates");
            Directory.CreateDirectory(staging);
            var target = Path.Combine(staging, "tc-" + Guid.NewGuid().ToString("N") + ".zip");
            using (var result = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
            {
                result.EnsureSuccessStatusCode();
                await using var file = new FileStream(target, FileMode.CreateNew);
                await using var stream = await result.Content.ReadAsStreamAsync();
                var buffer = new byte[64 * 1024];
                long total = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer)) != 0)
                {
                    total += read;
                    if (total > 80L * 1024 * 1024) throw new InvalidDataException("Release ZIP is too large (>80 MB).");
                    await file.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            if (chosen.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String &&
                (dg.GetString() ?? "").StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var expected = (dg.GetString() ?? "")[7..].Trim();
                await using var file = File.OpenRead(target);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded ZIP checksum did not match the GitHub Release asset.");
            }
            BeginCommanderUpdate(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show("GitHub update check failed:\n" + ex.Message, "Commander updater", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { _updatingCommander = false; }
    }

    private void PickCommanderZipUpdate()
    {
        using var picker = new OpenFileDialog
        {
            Title = "Select a Torn Commander update ZIP",
            Filter = "Torn Commander release ZIP (*.zip)|*.zip",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { BeginCommanderUpdate(picker.FileName); }
        catch (Exception ex) { MessageBox.Show("Update package rejected:\n" + ex.Message, "Commander updater", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void BeginCommanderUpdate(string sourceZip)
    {
        // Reject archives with traversal, duplicate/collision paths, missing source
        // project, executable payloads, excessive decompressed size or a wrong prefix.
        string? version = null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var zip = ZipFile.OpenRead(sourceZip))
        {
            if (zip.Entries.Count > 500) throw new InvalidDataException("Too many archive entries.");
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith("Torn Commander/", StringComparison.Ordinal) ||
                    name.Contains("../", StringComparison.Ordinal) || name.StartsWith("/", StringComparison.Ordinal) ||
                    name.Contains(":", StringComparison.Ordinal) ||
                    name.Split('/').Any(x => x == ".." || x == "."))
                    throw new InvalidDataException("Unexpected ZIP path: " + name);
                if (!names.Add(name)) throw new InvalidDataException("Duplicate ZIP entry: " + name);
                // ZIP Unix symlinks are refused; only plain files and directories.
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("ZIP contains a symbolic link.");
                expanded += entry.Length;
                if (expanded > 200L * 1024 * 1024) throw new InvalidDataException("ZIP expands beyond 200 MB.");
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("This updater accepts Commander source releases, not arbitrary prebuilt executables.");
            }
            if (!names.Contains("Torn Commander/TornCommander.csproj") ||
                !names.Contains("Torn Commander/REBUILD_AND_LAUNCH.bat") ||
                !names.Contains("Torn Commander/VERSION.txt") ||
                !names.Contains("Torn Commander/CommanderUpdater.cs"))
                throw new InvalidDataException("Missing required Commander update components.");
            using var sr = new StreamReader(zip.GetEntry("Torn Commander/VERSION.txt")!.Open());
            version = sr.ReadToEnd().Trim();
            if (!Version.TryParse(version, out var targetVersion) ||
                targetVersion <= RunningCommanderVersion)
                throw new InvalidDataException("Archive is not newer than the running Commander v" + RunningCommanderVersion.ToString(3) + ".");
        }
        if (MessageBox.Show($"Ready to install v{version}. Commander will close, back up source files, replace them and rebuild. Saved account data is not touched. Continue?",
            "Install update", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var project = Path.GetFullPath(Path.Combine(exeDir, ".."));
        var script = Path.Combine(project, "APPLY_COMMANDER_UPDATE.ps1");
        if (!File.Exists(Path.Combine(project, "TornCommander.csproj")) || !File.Exists(script))
            throw new InvalidOperationException("Cannot locate the installed Commander source folder. Open the app built from its source folder before updating.");
        var tempScript = Path.Combine(Path.GetTempPath(), "tc-updater-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.Copy(script, tempScript);
        var tempZip = Path.Combine(Path.GetTempPath(), "tc-update-" + Guid.NewGuid().ToString("N") + ".zip");
        File.Copy(sourceZip, tempZip);
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = false };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", tempScript,
            "-ZipPath", tempZip, "-ProjectRoot", project, "-WaitPid", Environment.ProcessId.ToString() })
            psi.ArgumentList.Add(arg);
        if (Process.Start(psi) is null) throw new IOException("Unable to start update helper.");
        Application.Exit();
    }
}
