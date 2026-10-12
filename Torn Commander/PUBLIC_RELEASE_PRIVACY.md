# Privacy and local data — Torn Commander

Torn Commander does **not** bundle or synchronize player logins across installations.

- Settings and profile display names: `%LOCALAPPDATA%\TornCommander\settings.json`
- Each profile's WebView2 cookies/cache: `%LOCALAPPDATA%\TornCommander\WebView2Profile` (legacy main) or `Profiles\<profile-id>\WebView2Profile`
- API credentials: Windows Credential Manager, keyed by the local profile ID
- Local ledgers, travel observations and user scripts: `%LOCALAPPDATA%\TornCommander`

Fresh installs use neutral profile names. Existing profiles are intentionally
preserved during updates. Previous releases included example names/IDs; v0.6.5
removes them from public source defaults. An optional one-time migration renames
example *labels* locally without changing the profile IDs, cookies or credentials.

**Do not publish or attach:** personal backup ZIPs, Torn API keys, `settings.json`,
WebView2 folders, browser session databases, user scripts containing secrets,
logs with usernames, personal screenshots or travel/order ledgers.

A GitHub repository is **not** a profile sync service. Publishing a source release
never uploads `%LOCALAPPDATA%` by design.
