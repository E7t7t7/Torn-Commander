NEW CONTROLS (v0.6.34)
- Loss Desk: Start A runs A only; Start B runs B only; Start Both needs two distinct profiles. Either solo slot can complete the full requested total.
- Scripts & Helpers: Open helper enables an installed script and opens its Torn page. On the game page, use Commander helpers for script commands such as Open Loss X-change and its API-key setup.
- Casino: enter the visible hand for move/equity advice. Poker assumes random opponents and needs pot/call amounts; it is not an opponent-reading solver.
- Trading: enter up to eight items, one total budget, cash reserve, selling fee and minimum return. Use one item plus cost each for sell advice. Candidates are conditional on returned quotes and a buyer, not guaranteed profit.
- Helper installation refreshes open tabs. Use the selected account profile's own script registration/settings.
- Update in Settings using Check GitHub for Update. Read the release changelog and test one small workflow first.

TORN COMMANDER v0.6.5 - BUILD FIX
=================================
This hotfix resolves the CS1061 errors in MainForm.Privacy.cs reported on v0.6.4.
Extract to a clean folder and run REBUILD_AND_LAUNCH.bat. Confirm Build v0.6.5.
Existing profiles, sessions, and API credentials are saved separately in %LOCALAPPDATA% and Windows Credential Manager.
Windows build verification still needs to be performed on a Windows computer.

TORN COMMANDER v0.6.2 - PROPER WINDOWS T ICON SHORTCUT
======================================================
The BAT file is only a build/start helper and can show the generic CMD icon.
1. Run REBUILD_AND_LAUNCH.bat once in the extracted Torn Commander folder.
2. After the app launches, close the app.
3. Run CREATE_DESKTOP_SHORTCUT.bat (new in v0.6.2).
4. Open your Windows Desktop's new Torn Commander shortcut with the dark T icon.
It targets dist\TornCommander.exe directly, without displaying a CMD window.
If you pinned the old BAT shortcut to the taskbar, unpin it and pin the new one.
Moving/deleting the extracted source folder breaks the shortcut. Run the shortcut
creator again after relocating the folder. All app profiles/data remain under
%LOCALAPPDATA%\TornCommander. No login data is touched by this process.

TORN COMMANDER v0.6.1 — RELEASE NOTES

Start clean: close all TornCommander.exe processes, extract this ZIP into a new folder, run REBUILD_AND_LAUNCH.bat.
Check Build v0.6.1 and Settings -> actual executable path before testing.
Torn logins, app data, API keys and account profiles are under %LOCALAPPDATA%\TornCommander and are NOT deleted by extracting a release.
The darker T icon is bundled. Windows may cache old taskbar/shortcut icons; launch the newly built EXE if a shortcut still shows the previous icon.

Graffiti: Open & Configure first. Run Once sends no more than one click and stops if district or paint cannot be proven. No blind retries.
If STOPPED: open the actual Graffiti page and use Copy Graffiti Debug to capture control information; don't send a raw API key, cookies or account credentials.

VERSION v0.5.5 GRAFFITI FIX NOTES
- CLOSE all Torn Commander instances, extract into a clean folder, run REBUILD_AND_LAUNCH.bat.
- Check Build v0.6.0 and the Settings EXE path before testing.
- For Graffiti test with Coach > Open & Configure first (not Run Once).
- The paint overlay now shows CHECKING, VERIFIED, or STOPPED with a reason.
- If STOPPED or the wrong controls are highlighted, open the Graffiti page then use
  Classic Tools > Coach > Copy Graffiti Debug, and paste its diagnostic JSON into ChatGPT.
- That diagnostic contains matching control metadata, not your API key or full page text.
- Avoid enabling third-party scripts that also automate Graffiti during the controlled test.

TORN COMMANDER v0.5.3 — READ FIRST (2026-10-09)

1. Close every existing Torn Commander window, including older ones.
2. Extract this ZIP to ONE new, empty folder. Do not merge with old project folders.
3. Run REBUILD_AND_LAUNCH.bat from inside the Torn Commander folder.
4. Check for Build v0.6.0 in the dashboard and v0.6.0 in the window title.
5. Settings > ACTUAL EXECUTABLE IN USE shows the precise running EXE path.
6. Travel: Save + Refresh. DroqsDB may return HTTP 530, so YATA public
   export is attempted as an independent backup. Stock-only rows without a
   market valuation are shown as PRICE UNKNOWN; net profit is never invented.
7. Graffiti: first use OPEN ONLY (not Run Once) to confirm the selected
   target and paint; if DOM markers are ambiguous Commander stops without Spray.
8. Paint overlay resizes as viewport changes and can be collapsed. Debug captures
   identifiable candidate controls if selection remains unverified.

Persistent profiles, cookies, settings, and credentials live in %%LOCALAPPDATA%%\TornCommander
and Windows Credential Manager, not in this ZIP. The clean rebuild does not erase them.

IMPORTANT v0.5.3: This ZIP contains SOURCE, not a prebuilt Windows EXE.
Extract into a NEW EMPTY FOLDER, open the INNER "Torn Commander" folder, and run "Torn Commander.bat" THERE.
The launcher now prints the exact source folder and VERSION.txt it is using.
In the actual app, check the new Build v0.6.0 badge and Settings -> ACTUAL EXECUTABLE IN USE.
An already-open v0.5.1 app does not update itself when you extract the ZIP; exit old Commander windows completely.
%LOCALAPPDATA%\TornCommander user sessions and saved data are unaffected by extracting the ZIP.
Graffiti v1.5.0 chooses at most one district and one paint control, verifies both, and stops on uncertainty. If it stops, use the paint overlay's Debug button to capture Torn's current UI selectors; do not attempt a Spray until the selections can be verified.

v0.5.3 SOURCE RELEASE — BUILD AND VERIFY
- Modern dashboard: profile switcher, sync status, all 11 sections, responsive cards.
- Travel: live public stock + published forecast, local learning, stock-age/freshness,
  budget/flight/fee/ROI/profit comparisons, predicted departure, item filters.
- Start with the BAT in this folder; this is a full source replacement, not a delta.
- See Guides/TRAVEL_PREDICTION.txt for calculations and limits.
- This ZIP is source; Windows compilation remains a required user test.

Torn Commander v0.5.1

TORN COMMANDER v0.5.1 — MODERN DASHBOARD + FREE TRAVEL FORECAST

v0.5.1 keeps every v0.4.2 feature but changes the default companion experience to a modern local WebView2 dashboard inspired by clean travel-planner layouts rather than traditional WinForms utility panels. The original detailed controls are still available under CLASSIC TOOLS.

MODERN COMMANDER UI
- New responsive local HTML/CSS dashboard rendered inside Commander; no external login or hosted dashboard required.
- Left navigation: Dashboard, Travel, Crimes, Merits, Cart, Losses, Scripts/Tools and Settings.
- Live cards for nerve/NNB, energy, wallet, merits, coach recommendation, cart, active loss order and travel forecast.
- Buttons call the existing C# backend directly, so the new UI is a front end around the same Commander logic rather than a separate web app.
- Classic detailed tabs are preserved as a fallback while individual modules continue moving to the new UI.

TRAVEL / STOCK PREDICTION
- Added a free Travel module with capacity, flight type, market-fee and forecast-horizon settings.
- Commander does NOT unlock, scrape or copy TornTravel premium prediction data.
- Primary forecast source is the documented DroqsDB public API intended for third-party tools/integrations.
- Commander also stores foreign-stock snapshots locally and learns recurring restock intervals as a fallback.
- Local predictions are confidence-labelled and remain 'learning' until enough actual restock events have been observed.
- Travel history and forecast cache are included in Commander backups; Torn API keys remain excluded.


TORN COMMANDER v0.4.2 — INTEGRATED LOSS AUTO CYCLE

v0.4.2 fixes the v0.4.1 false Loss Desk sanity-check failure and ports the prior Loss Desk attack/heal cycle directly into Commander.

LOSS AUTO CYCLE
- The Losses tab now contains Account A and Account B embedded WebView2 workers.
- Each worker uses the assigned Commander profile's isolated Torn cookies/session; normal browser extensions are not required.
- START AUTO A/B runs the prior loss-selling cycle: target claim, JOIN FIGHT, Pillow, visible loss result, First Aid Kit recovery, repeat.
- Shared target lease/claim coordination prevents Account A and B from attacking the same target simultaneously and prevents combined progress from exceeding the requested order.
- Final recovery is verified on the attack page before Commander marks the order complete.
- Pause/Resume/Stop are per-account, plus PAUSE BOTH.
- Ambiguous or changed Torn UI pauses safely rather than clicking arbitrary controls.
- After an app restart, a previous running state is left paused until you explicitly Resume.
- IMPORTANT: Auto Cycle performs repeated Torn browser actions automatically and may violate Torn's scripting rules. Use at your own risk.


TORN COMMANDER v0.3.0 — MERIT HUNTER

v0.3.0 adds a live Merit Hunter. Full Sync now reads your achieved honors/medals plus Torn's current award and merit-upgrade catalogs, ranks unearned awards by estimated effort, estimates progress from API stats where defensible, and recommends where to spend new merit points for NNB Growth / Easiest First / Max Nerve modes.

The Merits tab includes search, Show Earned, Guide Next, Open Awards, and Copy Top 10. Exact unearned progress is not available for every Torn award, so Commander explicitly labels estimates and unknowns.

TORN COMMANDER v0.4.1 — GUIDED ACTION STRICT-SELECTION FIX

v0.4.1 fixes Graffiti guided actions so the selected target and paint must match the Coach recommendation before Commander submits a one-click attempt. It also retains the direct Crimes 2.0 route and refuses unrelated navigation controls.

Coach includes a GUIDED ACTION block. Choose Advice only, Open & Configure, or One-click single action. During the current NNB foundation phase the live coach recommends Graffiti / Residential / Purple. Open & Configure selects the setup and leaves the final Spray click to you. One-click single action performs one supported attempt for each manual press and does not loop or repeat. If Commander cannot identify the live Torn controls safely, it stops without submitting and tells you in the status bar.

The Graffiti paint overlay remains charge-aware for informational suggestions, but Guided Action no longer substitutes an already-active can for the Coach recommendation. The guided action uses the exact requested paint color.

BASE / QUICK START NOTES (still apply)
======================================

QUICK START
-----------
1. Run Torn Commander.bat.
2. Settings -> Create Full Access key. Approve the new key on Torn.
3. Copy the NEW 16-character key, then Settings -> Paste + Save.
4. Click Verify Full Access, then Full Sync.
5. The key is saved in Windows Credential Manager and reused automatically.

USERSCRIPTS
-----------
Settings -> Local Userscripts -> Import userscript accepts .user.js, .js, and .txt.
A normal userscript text file beginning with // ==UserScript== can be imported directly.
Commander copies it into %LOCALAPPDATA%\TornCommander\Scripts, enables it, and honors @match/@include rules.
Reload/open a new matching browser tab after importing.
Torn Blackjack Assist v2.1 is bundled and enabled by default in this build.

WINDOW SIZE
-----------
Commander no longer forces maximized startup. It remembers the last saved size/position on close.

ORIGINAL v0.2 DOCUMENTATION
---------------------------
TORN COMMANDER v0.2.1
Windows 11 x64 • Edge/WebView2 desktop companion

THIS BUILD IS A CLEAN-UP / FUNCTIONALITY PASS
==============================================
v0.2.1 is intentionally a larger revision rather than another patch on the original static dashboard.

Main changes based on real testing:
- Browser starts with ONE Torn tab by default instead of restoring a wall of tabs.
- Browser tabs now have a visible × close control.
- Ctrl+W closes the selected browser tab.
- Middle-click a browser tab to close it.
- Right-click a browser tab for Close / Close other tabs / Close all tabs / Open in normal Edge.
- Correct current Torn page.php routes are used for Crimes and Racing.
- Education now uses page.php?sid=education.
- Bazaar Directory now uses page.php?sid=bazaar.
- Any old top-level Torn loader.php?sid=... link entered/restored is automatically rewritten to page.php?sid=...
- The static NNB wall-of-text has been replaced by a LIVE COACH generated from your Torn API data.
- Full Sync reads perks, merits, education, work stats and every available Crimes 2.0 category, then rebuilds your plan.
- When company employee data exposes it, Full Sync auto-detects your live drug-addiction effectiveness penalty; the saved manual value is only the fallback.
- Live progression tracker shows tracked crime nerve, attempts, critical fails, current priority crime and the API progression bonus.
- Fast Sync updates volatile account data such as bars/refills/profile without doing a large crime crawl.
- The coach follows the Torn page you are viewing and shows context-sensitive advice.
- Your -23% addiction / Level 15 rehab plan is built into the education coach.
- Tools, loss-selling launchers, whitelist, custom links and local helper scripts remain available.
- API key remains securely saved in Windows Credential Manager and is automatically reused.

FIRST RUN / UPGRADE
===================
1. Extract the entire "Torn Commander" folder to your preferred location:

   C:\Users\<WindowsUser>\Desktop\Torn Commander

2. Run:

   Torn Commander.bat

3. If dist\TornCommander.exe does not exist, the launcher builds it.
   It will install the .NET 8 SDK through winget if the SDK is missing.

4. If you previously used v0.1.x, your app data stays in:

   %LOCALAPPDATA%\TornCommander

   That includes the persistent WebView2 login profile, settings, ledgers and scripts.
   Your saved Torn API key remains in Windows Credential Manager.

5. v0.2 defaults to NOT restoring all old tabs. If you want session restore later:
   Settings -> Restore browser tabs at startup.

LIVE COACH
==========
The right-side Coach is designed to replace repeatedly reopening a written guide.

FAST SYNC
- profile / level
- nerve / energy bars
- daily refill state
- racing skill when exposed
- job/company/faction basics

FULL SYNC
- everything in Fast Sync
- perks
- merits
- education
- organized crime status
- work stats / money / racing records when allowed by the key
- Torn Crimes 2.0 catalog
- your per-crime Crime Skill, nerve spent, attempts, failures / critical failures, progression data where exposed

The coach then recalculates:
- working NNB estimate
- current NNB phase
- main crime to prioritize
- crimes to delay / use only safely
- education / rehab priority
- refill / nerve advice
- faction / merit / OC reminders
- per-crime "Plan" column

The NNB estimate is still an estimate when Torn does not expose enough information to subtract every flat max-nerve bonus. Settings includes an NNB override if you know the true value.

CURRENT ACCOUNT LOGIC
=====================
The default fallback addiction setting remains -23%. Commander prefers API-detected company addiction data when it is available.

Before Level 15:
- do not build the plan around long education courses that are likely to be interrupted by severe addiction
- continue safe account / NNB progression

At Level 15 while still severely addicted:
- rehab becomes the education priority
- after addiction is controlled, resume the Psychology / Cracking education plan

NNB phases used by the coach:
- below ~35 NNB: Graffiti foundation + passive Card Skimming
- ~35-39: Hustling + safe Disposal bridge
- ~40-59: Cracking becomes the main CE engine + safe Disposal
- 60+: hardest advanced crime mix you can perform safely; avoid turning any one crime into outdated forever-advice

BROWSER / TABS
==============
Toolbar:
- ← back
- → forward
- ⟳ reload
- Torn = home
- address bar
- + = new Torn tab
- × = close current tab
- quick-route chooser
- AI = copy live account context + open ChatGPT

Keyboard:
- Ctrl+T = new Torn tab
- Ctrl+W = close active browser tab
- Ctrl+L = focus address bar

Browser tab mouse controls:
- click × = close
- middle click = close
- right click = tab menu

ROUTE HANDLING
==============
Torn has migrated many SID-driven pages away from direct loader.php navigation.
Commander v0.2 treats page.php as the canonical top-level SID route and rewrites old loader.php links automatically.

Known quick routes include:
- Crimes:    https://www.torn.com/page.php?sid=crimes
- Racing:    https://www.torn.com/page.php?sid=racing
- Education: https://www.torn.com/page.php?sid=education#/step=main
- Bazaar:    https://www.torn.com/page.php?sid=bazaar

Other older direct Torn pages are left alone when they are still valid instead of guessing a replacement.

API KEY
=======
Settings -> Torn API Key

- Paste the key once.
- Save.
- It is stored in Windows Credential Manager under TornCommander:TornApiKey.
- It auto-loads every future launch on that Windows account.
- It is NOT stored in settings.json.
- It is NOT included in exported backups.
- Test verifies the key against current API data.
- Forget removes it from Credential Manager.

Because a key was previously pasted into a chat during development, create a fresh Torn-Commander-specific key and revoke the chat-shared key once you are satisfied the app works.

TOOLS / PLUG-INS / WHITELIST
=============================
The Tools page includes launch points for:
- TornTools
- Lazy Crimes
- Simple Cracking Helper
- torn.report
- YATA
- Torn API Playground

You can also:
- add arbitrary Custom Links
- whitelist domains you decide are needed
- Allow once / Always allow / Open in Edge / Block an unknown site when navigating
- inject plain local JavaScript helper scripts into newly opened tabs
- load an unpacked Chromium extension into the WebView2 profile

The whitelist bypasses COMMANDER'S navigation filter only. It does not bypass authentication, CAPTCHAs, Cloudflare, Torn security, or Torn rules.

LOSS SELLING
============
Losses contains launchers for several community loss-selling services plus a payout calculator.
Rates change, so verify the live order/contract before committing.
Commander does not automatically claim orders or perform attacks.

MONEY / TRADING
===============
The local ledger records:
- item
- quantity
- buy price
- target/actual sell price
- cost
- profit
- ROI
- notes

Gameplay and purchase actions remain manual.

RACING
======
The app syncs racing skill when the API provides it and keeps local track/car notes.
The quick route uses the current page.php Racing route.
Race entry remains manual.

CASINO
======
Casino is a bankroll/session tracker only.
There is no auto-betting or unattended gambling.

AI CONTEXT
==========
Press AI / Ask AI.
Commander builds a clipboard packet from your current account state, including:
- level
- nerve
- NNB estimate
- current addiction value / source
- live NNB phase
- crime progression
- company/faction
- racing skill / wallet when available

It then opens ChatGPT. Paste the context into the chat so advice is based on the current account rather than an old static guide.

DATA / BACKUP
=============
Local data folder:
%LOCALAPPDATA%\TornCommander

Settings -> Export backup creates a ZIP on the Windows Desktop.
The API key is deliberately excluded.

TORN AUTOMATION BOUNDARY
========================
Most Commander features remain manual or single-action helpers. v0.4.2 adds an explicit opt-in Loss Auto Cycle that repeatedly performs the configured attack/Pillow/heal workflow inside the two embedded loss-worker browsers. It pauses on verification screens and ambiguous UI instead of attempting to bypass security.

Commander still does not:
- auto-race
- run unattended market buy/sell loops
- auto-gamble
- bypass CAPTCHA/security or Cloudflare verification
- continue a previous auto-cycle silently after an app restart

The API dashboard reads documented Torn API data. Loss Auto Cycle is a separate opt-in browser-automation mode and may violate Torn's scripting rules.

IF SOMETHING BREAKS
===================
Send the exact console/build output or a screenshot of the failing page/control.
For a bad browser session, RESET_BROWSER_PROFILE.bat clears only Commander's WebView2 browser profile; it does not delete your API credential or local ledgers.


GRAFFITI PAINT HELPER
---------------------
Torn Commander bundles Torn_Graffiti_Paint_Helper.user.js. On the Graffiti crime page it attempts to read the remaining charge of each spray-paint color from the already-loaded Torn page/client data and displays the values together in one overlay. It does not click or submit Torn actions. If Torn changes its page structure and a value is missing, use the helper's Debug button and paste the copied diagnostics into ChatGPT so the parser can be updated.


SHOPPING CART / BULK BUY (v0.3.5)
----------------------------------
Open the Cart tab. For the requested paint restock workflow, choose the quantity per color (default 1,000),
press Load paint cart, then Scan / Build Cart. Commander reads Torn's current item catalog and live Item Market
price tiers, calculates the cheapest plan, and shows budget/reserve warnings.

Prepare Next opens and prepares the next planned item. Buy Next Once attempts one purchase only per press and
never loops through sellers/items. Copy cart for chat exports the remaining shopping list to your clipboard in
a clean format for Torn faction/private chats. If the live Item Market DOM no longer matches Commander's safe
selectors, it stops without guessing. Press Market debug and send the copied output so the market helper can be updated.


V0.4 ACCOUNT PROFILES / LOSS DESK
--------------------------------
Use the profile selector at the top-right of the browser toolbar to switch between accounts. Each profile has separate Torn cookies/login state and a separate saved API key. The original main profile keeps the pre-v0.4 browser data location so your existing login should survive the upgrade.

Loss X-change is NOT bundled because its author forbids redistribution. Install/update it from Losses or Tools; Commander downloads it directly from the author's Greasy Fork URL. Each Torn account must configure the Loss X-change Limited API key inside that account's own script UI.

The Loss Desk supports shared A/B order tracking. NEXT A / NEXT B remains the manual workflow. v0.4.2 also adds embedded Account A/B Auto Cycle workers that can run the old attack → Pillow → loss-result → FAK recovery cycle without Edge/Chrome extensions. Shared target leases prevent both workers from attacking the same buyer at the same time, and one-loss claims prevent double-counting or overshooting the order.


VERSION v0.5.4 NOTES
---------------------
Travel uses the saved Torn API key only against Torn API endpoints for item catalog market_value (estimated) and a small number of missing Item Market price listings. Prices are indicative, not guaranteed selling revenue. Public travel sources may still fail; stock-only rows remain useful without invented profit.
To ensure you are running the new source: close all Commander windows and run REBUILD_AND_LAUNCH.bat. Check the dashboard Build v0.6.0 badge and the running EXE path in Settings.


v0.5.7 Graffiti action behavior:
- MODERN DASHBOARD > Run Once is an explicit single-action request, independent of the persisted Classic Coach mode.
- It never spams or retries selection clicks. Run Once stops if either district or paint cannot be positively verified.
- Open Only navigates to Torn without clicking an action.
- The persistent Graffiti status is shown on the dashboard and Crime Coach.
- Use Copy Graffiti Debug when status reports STOPPED, then share only the relevant control diagnostics.
- A CLICK_SENT status confirms one click was requested; it is NOT proof Torn accepted the action.
