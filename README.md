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

A Commander release uses the exact asset name `Torn_Commander_vX.Y.Z.zip` and must contain a single `Torn Commander/` source folder. Extract into a permanent local folder and run `REBUILD_AND_LAUNCH.bat` once on Windows with the .NET 8 SDK. The in-app updater (Settings → Update Commander) checks published Releases from this repository; it does **not** read the source branch or synchronize accounts.

There is not yet a GitHub Release until the source is uploaded, validated on Windows, and a version is published. The release workflow included here is a starting point for that process.

## Source organization

The current source ZIP is maintained separately while the repository is initialized. The checked-in project should live inside `Torn Commander/` with exactly one `TornCommander.csproj`. Keep all personal state outside the project tree.

This project is not affiliated with Torn City or any third-party travel-data service. Do not redistribute unlicensed third-party userscript sources.

## Build and publishing

When the sanitized project has been added and committed, CI will validate and build on Windows. Pushing a `vX.Y.Z` tag, or manually running the release workflow for a version, can generate a release ZIP from **tracked source files only**. Verify the build, privacy scan and release asset before telling users to update.
