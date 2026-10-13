# Commander takeover audit and v0.6.19 verification

Baseline: main e34dfa7793d09cfa7d1fc5887b6a5af0a4d7d771, version 0.6.18, inspected October 12, 2026 Eastern.

- Travel preview is wired from the C# forecast into dashboard state/renderTravelPreview. Existing empty/unpriced/stale/fresh checks pass. Live travel/shop behavior is not claimed verified.
- The old updater changed the desktop link, but not the separate taskbar pin. The stable launcher/migration address that verified gap.
- Cleanup relied on queued folders rather than discovering older installations. Maintenance now discovers exact version folders and requires readable, version-matching complete source archives before cleanup.
- TEST_UPDATER_STAGING used real LOCALAPPDATA despite describing itself as isolated. Child updater processes and rollback assertions now use the same fixture data root.
- Windows .NET 8.0.425 build/publish, PowerShell parsing, existing shortcut/updater/travel checks and new maintenance regression are release gates.
- Tests use real .lnk files, launcher compilation and ZIP/folder operations in temporary fixtures. They do not launch Torn or access real keys/account data.
- Existing compiler warnings about WindowsBase/WebView2, nullable shop parsing and DPI remain; baseline had no build errors.
- User confirmed last-used v0.6.18. Live taskbar pin clicks and in-app upgrade still need user validation.
