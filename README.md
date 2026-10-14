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

**Current source version:** v0.6.36. [Latest published release](https://github.com/E7t7t7/Torn-Commander/releases/latest). GitHub Actions builds on Windows, validates the tracked source, and publishes the matching `Torn_Commander_vX.Y.Z.zip` asset whenever a new version is pushed to `main`. The app's Settings â†’ Update Commander â†’ Check GitHub for Update button uses that release channel.

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

Travel planning reads actual departed_at/arrival_at from the documented Torn v2 user/travel endpoint. Rebuild Plan refreshes personal timing. While outbound or abroad, the plan estimates returning immediately after the configured shop stay and clearly states the assumption; starting the real return flight replaces it with the actual landing ETA. Custom departures are in PC local time. API reference: https://www.torn.com/swagger.php.

Company business health now highlights recorded spending above reported income before a seven-day baseline exists. It shows per-customer figures and prior-day income changes, with incomplete costs and reporting-period uncertainty disclosed. The customer-count calculation is an accounting scenario, not predicted demand.

Guided graffiti now acknowledges commands synchronously and polls their completion by ID. A profile change cancels pending commands; the page also expires requests after 25 seconds. Run Once submits at most one verified Spray click per request, with no automatic retry. Selection ambiguity or changed/disabled controls stop submission. Live Torn layout and crime outcomes still require local verification.

Loss workers retry missing Commander replies after ten seconds, up to three requests. Lost commit acknowledgements replay the same claim against persistent per-order receipts; they do not start a new fight or add a second loss. Exhausted retries pause with the pending visible loss retained; Resume reconciles that same claim. New commits still require an authorized live lease. Orders are written atomically and failed saves restore the prior counts. No actual Torn fight or item-use outcome is inferred from a sent click.

Commander paces Torn API requests across native modules and the userscript HTTP bridge through a shared process queue (one request start per 1.25 seconds). A Torn error 5 or HTTP 429 pauses sends for at least 65 seconds and respects a longer Retry-After. The dashboard counts down and retains prior successful sync data. Other applications and separately running Commander processes still consume the account allowance; this queue does not control their traffic. Full Sync takes longer because its reads are paced. Official API limit documentation: https://www.torn.com/api.html.

Home now includes Cash while growing: profile-specific training allocation, cash reserve, daily recorded trading-margin target and a local daily checklist. Default targets are editable planning choices, not an enforced energy/spending limit. No training, enrolment, flight or purchase is performed by this panel. It uses existing synced bars, wallet, education and travel observations; stale evidence is labelled. New trade records receive the active profile ID, while unassigned older trades stay unchanged and are excluded from the profile daily total. Check-ins reset on the PC local date and retain fourteen days. Travel requirement source: https://wiki.torn.com/wiki/Travel; education schema: https://www.torn.com/swagger.php.
