# Torn Commander

A community Windows desktop companion for Torn City built with C# / .NET 8, WinForms and Edge WebView2.

## Privacy by design

**Each installation is independent.** Source releases do not include accounts, cookies, API keys, browser databases, personal ledgers, or other player data.

- Local settings and profile names: `%LOCALAPPDATA%\TornCommander\settings.json`
- Browser storage: `%LOCALAPPDATA%\TornCommander\WebView2Profile` (legacy main profile) or `Profiles\<profile-id>\WebView2Profile` underneath the same local folder
- Torn API credentials: Windows Credential Manager (separate entry per profile)
- Personal transactions, travel observations and helpers: local application data

Fresh installations start with **Profile 1** and **Profile 2**. Customize names and sign in to Torn on your own computer. Earlier builds contained example player names; v0.6.4 replaces those examples and offers a label-only migration on upgrade.

**Never commit or upload:** `settings.json`, `Profiles/`, `WebView2Profile/`, `TornCommander_Backup_*.zip`, API keys, screenshots containing user data, or local runtime logs.

## Install and update

A Commander release uses the exact asset name `Torn_Commander_vX.Y.Z.zip` and must contain a single `Torn Commander/` source folder. Extract into a permanent local folder and run `REBUILD_AND_LAUNCH.bat` once on Windows with the .NET 8 SDK. The in-app updater (Settings â†’ Update Commander) checks published Releases from this repository; it does **not** read the source branch or synchronize accounts.

**Current source version:** v0.6.21. [Latest published release](https://github.com/E7t7t7/Torn-Commander/releases/latest). GitHub Actions builds on Windows, validates the tracked source, and publishes the matching `Torn_Commander_vX.Y.Z.zip` asset whenever a new version is pushed to `main`. The app's Settings â†’ Update Commander â†’ Check GitHub for Update button uses that release channel.

### Stable shortcuts and saved versions

v0.6.19 migrates matching Commander desktop/taskbar shortcuts to a stable launcher under %LOCALAPPDATA%\TornCommander\Launcher. Its active-version pointer changes on update/rollback, so the pin no longer depends on a removed version folder. Startup also handles the first update installed by an older updater. Unrelated shortcuts are not changed; Explorer is notified without restarting it or changing pin order. Windows taskbar caching still requires a real pin-click check.

Older source versions are verified and archived as Torn_Commander_vX.Y.Z.zip beside the version folders and registered in the per-user rollback library. Starting v0.6.20, application startup requests complete compaction: only the active version remains unzipped after every unused older folder has a verified archive. The helper also supports retaining an immediate recovery folder when complete compaction is not requested. Running, locked, linked or private-data folders are retained. Rollback rebuilds the source ZIP with .NET 8 and preserves WebView2 sessions and credentials.

Updater/maintenance tests use fake installations, test shortcut directories and an isolated application-data root. They never use the real player's rollback vault.

## Source organization

The public privacy-cleaned source is tracked in `Torn Commander/` with exactly one `TornCommander.csproj`. Keep all personal state outside the project tree. GitHub Actions packages tracked source only.

This project is not affiliated with Torn City or any third-party travel-data service. Do not redistribute unlicensed third-party userscript sources.

## Build and publishing

CI validates and builds on Windows whenever Commander source is pushed to `main`. If all checks pass, it publishes a new GitHub Release based on `VERSION.txt`, with a matching versioned source ZIP. Existing published version tags are never overwritten; bump `VERSION.txt` for new releases. This checks compilation, **not** live game behavior.

Company Growth Advisor offers per-profile Daily sync while open (off by default). It collects one API observation per UTC day, retries failures no sooner than ten minutes, and does not change company settings. Manual Sync can refresh an incomplete daily observation. Missing financial/staff data stays unknown; advertising comparisons exclude missing income and require known staffing. Arrival advice uses timestamped market valuations within twelve hours and compares affordable forecast loads against the observed mixed basket. Travel diagnostics explain exclusions from the raw feed before deduplication.
