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

**Current source version:** v0.6.77. [Latest published release](https://github.com/E7t7t7/Torn-Commander/releases/latest). GitHub Actions builds on Windows, validates the tracked source, and publishes the matching `Torn_Commander_vX.Y.Z.zip` asset whenever a new version is pushed to `main`. The app's Settings â†’ Update Commander â†’ Check GitHub for Update button uses that release channel.

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


### Company scan and staffing adviser (v0.6.37)

Open Company and choose **Scan Company & Staff**. With permitted company API access, Commander reads company health, employees' working stats and effectiveness, the company type's role requirements, and stock/prices. Public-only responses leave private stats unknown and disable a complete team allocation.

The scan reviews current jobs without endorsing them. Preview the corrected new-company starter draft for key-role coverage, or explicitly compare your own counts under **Advanced: compare your own job counts**. All employees remain included, even below recommended stats. Counts must total the scanned staff count, and a new scan resets the local preview. No Torn positions or other company settings are changed.

Working-stat effectiveness uses the [official Company wiki formula](https://wiki.torn.com/wiki/Company#Employee_Effectiveness). At exact recommended stats for a normal two-stat role the modeled score is 90. The optimizer excludes other effectiveness modifiers and cannot forecast sales, support-role effects, star growth or profit. Torn's actual reported scores are displayed separately. Preserve appropriate sales and support coverage when trying another role mix. Account data follows the [official v2 API schema](https://www.torn.com/swagger/openapi.json).

The next-steps panel identifies observed advertising shortfalls, health issues, training gaps, missing accounting, empty shelves and below-cost pricing. API wages are shown separately from manually entered accounting. Inventory quantities and order costs are not treated as daily supplies spending. Each profile stores its own dated scan locally; scans older than 24 hours are flagged.


### New-company Candle Shop review and draft (v0.6.43)

A scan now shows **Current setup — review only**. It does not endorse the current mix or recommend moves automatically. Old generated suggestions are marked for rebuilding; the app does not undo positions you already changed in Torn.

The **New-company starter draft** keeps all 4–10 scanned staff, including new players and zero-stat fillers. It reserves one Chandler, one Quality Control and one Cleaner as a simple coverage assumption; remaining employees fill Salesperson places. This is an unvalidated starting arrangement, not a proven profitable mix. There is no fixed near-even Chandler split, automatic second Cleaner, dedicated Trainer or Bookkeeper.

Placement protects the weakest key-job coverage first, then balances adequate key-job coverage and the other roles. It cannot improve its objective merely by dumping the weakest employee into a demanding key job to free a stronger person for an easier job. Full recommended stats are not required. Scores above the normal recommended-stat baseline do not justify sacrificing another key job's coverage.

**Preview Staff Placements** shows named draft assignments with plain-language purposes. The draft explains weak roles and keeps everyone included; it does not recommend firing fillers. **Advanced: compare your own job counts** remains a separate explicit comparison that maximizes total working-stat fit for the counts you choose. That total is not a business-value or revenue objective.

Check complete daily outcomes and the actual Environment/staff-room situation before deciding whether a different role mix helps. Guided draft counts remain assumptions and are never labeled a validated recommendation.

### Optional company report for chat advice (v0.6.39)

Choose **Company → Copy Company Report** after scanning. Paste the clipboard text into your chosen chat to request a researched review of the actual staff and selected plan. The report includes company/staff names, working stats, assignments, costs, stock and up to 30 daily observations. It excludes API keys, browser sessions, free-text notes, request-error text and other companies' records.

Copying is entirely local. Commander does not send this report, call an AI service or consume AI credits. Sharing it in a chat is your separate action, under that service's normal limits. The app continues to scan and calculate independently.


### Position explanations (v0.6.40)

Each employee card has **Why this position? — Show details**. Expand it to see the selected role counts, individual and team score tradeoffs, best individual fit among included roles, recommended-stat coverage, role function and reliability limits. An individual can lose modeled points while the team gains; the explanation states that directly.

For explicit custom-count comparisons with up to 25 staff, Commander also calculates the best assignment if that person stays in their current role. It shows the exact team-score cost, an equally good alternative, or that the selected counts have no slot for the old role. These are calculated explanations, without an AI service or usage charges.

A current modeled/reported working-score difference of at least five points is flagged for verification, not attributed to a confirmed cause. Proposed scores exclude other effectiveness modifiers and support-role effects. Open explanations remain open during unchanged dashboard updates.


### Position-aware Faction Command (v0.6.41)

Open **Faction → Scan Faction**, or use a user-requested **Full Sync**, to read your account identity, faction position, roster, chain, wars, available organized crimes and installed upgrades. Manual Full Sync includes the faction scan; background account refreshes do not repeatedly poll all faction endpoints.

The top priorities adapt automatically. Leaders/co-leaders are verified by account ID against faction leadership IDs. Officers are recognized from position management abilities when the API exposes them; an impressive custom title or ordinary armory access does not establish officer permissions. Members see personal participation first. All panels remain available in every view, while the saved key's actual permissions determine which data can be read.

Choose a local growth/war/crime focus and inactivity review threshold. Inspect member availability, open crime slots, assigned-member CPR, reported missing items, ready times, installed perks and observed membership/respect changes. Empty-slot CPR belongs to the requesting account and cannot rank other members. Available crimes are capped at 100 and marked partial when the cap is reached. No member battle stats or optimal upgrade purchase order are fabricated.

Chain deadlines and war/presence data are saved snapshots, not a live monitor. Refresh in Torn before acting. Personal mission-agent objectives are not imported: the local mission/leadership checklist is manually entered and scoped to the profile and faction. **Copy Faction Report** copies selected saved data locally, excluding API error details and free-text tasks. No messages, purchases, upgrades, crime launches or AI requests are performed.

Endpoint contracts follow the [official v2 schema](https://www.torn.com/swagger/openapi.json). See the official [Faction guide](https://wiki.torn.com/wiki/Faction) and [Organized Crime 2.0 guide](https://wiki.torn.com/wiki/Organized_Crime_2.0) for game mechanics.


### Loss recovery, timing and fulfillment (v0.6.42)

Workers now expand a verified collapsed First Aid Kit entry before looking for its Use control. They wait at most 15 seconds after opening it, then pause with a diagnostic rather than repeatedly toggling the item or guessing another medicine.

In **Loss Desk**, set **Pillow attack delay (milliseconds)** and choose **Apply Attack Timing**. The allowed range is 500–5000 ms; the default remains 1900 ms. The same setting is available in the Full Loss Desk. It applies to running workers and future starts. Actual turns may be slower while Torn loads or disables controls.

Local contracts become **Complete** as soon as their confirmed count reaches the requested quota; Next A/B then show **Done**. Final healing continues independently in the worker and does not hold up contract fulfillment. Older fully delivered orders are corrected on load without adding any losses. Partial orders are not automatically completed. This is local order tracking, not an automatic external Loss Exchange settlement.


The docked worker panel has **Hide ×** and **Stop & Close** controls. Hide reclaims screen space while preserving worker state; Stop & Close first stops both workers. Use **Loss Desk → Show Workers** to reopen the A/B tabs. The same actions are available beside Full Loss Desk.

### Company star outlook (v0.6.44)
Company → Scan Company Standings reads paginated public company listings for your company type using the existing paced API connection. Each star level shows its lowest and middle weekly sales and the percentage of peers you strictly outsell. Ties do not count as beaten. These are revenue comparisons, not calibrated promotion odds. Hidden lifetime-income experience gates prevent a reliable Sunday probability or fixed waiting-time prediction. Scans are manual, profile/company scoped, and marked old after 24 hours. API data may be cached; a changing or incomplete scan is rejected. Source: https://www.torn.com/forums.php?a=0&b=0&f=19&p=threads&t=16590771

### Intel tools, stock portfolio and activity history (v0.6.45)
- Settings → Appearance → Intel Night selects the new charcoal/cyan/teal palette.
- Stocks → Scan Stocks reads official Torn prices and your holdings. P/L deducts the 0.1% sale fee and is unknown if returned transaction shares do not establish a complete cost basis.
- Hourly sampled charts/ranges build as you scan. Price targets are checked against scanned prices only. Payout yield uses your own one-first-tier-payout value and day interval; it does not model price changes or passive benefits.
- Faction → Local Activity History optionally records every five minutes, only while the app is open on that profile. Percentages count observed online/idle/offline samples, not hours played. Collection ignores incomplete or changed-faction rosters, uses shared API pacing and retains 30 days.
- Intel Tools opens Torn Intel in an ordinary in-app browser tab. The site requires its own login and any relevant subscriptions. Commander does not insert credentials or copy its private data/model.
- [Feature audit](INTEL_FEATURE_AUDIT.md) tracks what remains; this is not full native parity.

### Ranked-war payout previews and CSV reports (v0.6.46)
War Payouts → enter the ranked war ID → Scan War Report. The report must include the API account's current faction. Set a cash budget, attacks weight (remaining weight uses the report's war score), minimum attacks and optional per-person cap. Calculate and Save Preview never transfers money. Whole-dollar rounding is deterministic; unused money is shown separately. Historical participants remain in the report even if they later leave the faction.

Export CSV is available for war payout previews, Company history and Stocks. Unknown values remain blank; formula-like text is escaped for spreadsheets. Exports contain the selected profile's report only.

### Visual dashboards and rehab-first planning (v0.6.47)
Company now places its summary cards first and charts recorded income, advertising and accounted margin over 7/30/90 days. Faction charts respect, membership and sampled hourly activity. Stocks use labelled time-series graphs; Travel, War Payouts and Loss Desk gain data-driven comparisons/progress. Unknown values remain gaps, not zero. The original vector city header and layout polish apply across palettes; Intel Night remains available in Settings.

Travel → Switzerland rehab first reserves a rehab trip before trading. Adjust minutes at rehab, then mark Rehab done/remove stop after treatment. The requirement is saved per profile. If already headed to Switzerland, the current landing time is used instead of scheduling a duplicate outbound flight. Existing location/timing checks, custom starts and the rolling 24-hour deadline still apply. Rehab fees and airfare are excluded from the profit calculation; the stop is planning only. Flight-time reference: https://wiki.torn.com/wiki/Switzerland .

### Trading margin controls and advertising trials (v0.6.48)
Trading → Scan & advise now accepts a downside price-drop assumption, a maximum share of analysis capital in one item, and a minimum cash-profit threshold. New defaults are 3%, 50% and $1,000; they are user-adjustable controls, not empirically optimal values. Candidate spreads must survive the chosen stress case. Asking prices still do not establish completed sales or liquidity. Total cash includes the reserve; the reserve is excluded even without fresh wallet data.

Record a completed sale with actual quantity, buy/sell prices and total actual selling fees. Duplicate submission receipts are ignored. Known after-fee profit feeds the trading total and daily progression margin. Legacy trades with unknown fees are preserved and excluded from these known-net totals. The classic ledger distinguishes gross margin, fees and net, and is filtered to the active profile. Completed trades can be exported as formula-safe CSV.

Company → Advertising trial records a seven-day experiment definition without changing Torn settings. Enter actual daily advertising costs separately. It compares the prior week's recorded days at the baseline budget with test days at the chosen budget, excluding today's snapshots. It shows extra revenue, extra advertising, the after-ad difference and known losses against your allowance. Incomplete costs and changing staffing/stars are identified. These are observational comparisons, not causal proof or promotion odds.

### Item Intelligence and historical foundation (v0.6.54)
Native foreign-item watchlists, timestamped SQLite observations, source health, background collection, CSV import/export and desktop stock/threshold alerts. Default watch: Neumune Tablet #361, Switzerland. The independent estimator requires multiple continuously observed zero-to-positive cycles, withholds stale forecasts, and reports walk-forward midpoint errors and observation-window overlap. Quantities from different providers never become one synthetic history.
YATA public stock works without a key; Torn Intel's documented stock API requires an approved application key requested through its contact page. Website login credentials are stored separately in Credential Manager and do not grant native private-history synchronization. Website sign-in remains a normal embedded browser workflow. No paid predictions, hidden APIs or browser-extension impersonation are used.
The SQLite database lives in LOCALAPPDATA/TornCommander/Intelligence alongside retained JSON journals, outside versioned installations. Company scans, staff snapshots and faction collection mirror into profile-scoped history. See INTELLIGENCE_ROADMAP.md for the audit, implemented boundaries and remaining stages.

### Company scenarios and travel evidence (v0.6.56)
Companies now have Overview, Staff, History & Scenarios, and Costs & Records views. Compare recorded advertising groups with matching staff/star counts, inspect measured employee development, and preview hypothetical costs/revenue/funds runway without changing Torn. Private snapshots remain profile-scoped. A historical company selector is a saved-data view, not authority to scan that company.
Travel and sequence plans consume fresh SQLite stock evidence. Supported local forecasts retain their full interval and use its end conservatively; waiting comparisons show time opportunity cost and conditional basket profit. The previous legacy point-cycle extrapolator is retired. Existing JSON observations remain intact. See Guides/ITEM_INTELLIGENCE.txt for supported sources, imports and interpretation.

### Item-market history and source diagnostics (v0.6.57)
Item Intelligence can optionally sample official Torn item-market asks for watched items, rotating up to five IDs per poll through the shared API queue. Saved samples produce local price charts and threshold-crossing alerts. These are returned asking prices, not completed sales, a complete bazaar scan, or a promised profit. Empty offers remain unknown. Settings includes a source/freshness overview. Stock and profile-scoped market history export to CSV without the on-screen history limit. The v0.6.56 Pillow and fight-resume fixes are retained.


### Smart Medical Management (v0.6.60)
Loss Desk now plans bounded combinations of personal Small FAKs, FAKs and Morphine to minimize medical cooldown, then item count and configured cost/scarcity. Automatic consumption is off by default: stop the relevant worker, open Loss Desk > Smart Medical Management, select the account, choose Automatic and save permissions before restarting. Each account has independent permissions, reserves, modifiers and statistics. Drug IDs and faction supplies are excluded. Optional blood support is limited to universal O-negative bags with API-verified IV eligibility. See Guides/SMART_MEDICAL.txt for verified API fields, limitations and recovery behavior.

### Leveling and direct Baldr imports (v0.6.64)
**Sections → Leveling → Load Baldr’s list** downloads the public `data.json` from [OranWeb/tc-baldrs-levelling-list](https://github.com/OranWeb/tc-baldrs-levelling-list). Choose a named group or All lists. Published order is retained and duplicate/self IDs are removed. Source and load time are shown; listed battle stats are historical reference values. No Torn credentials go to GitHub. Failed downloads or validation preserve the existing queue. FFScouter JSON and manual imports remain supported.

Save a fighting weapon slot (primary, secondary or melee), attack interval, energy reserve, optional session attack limit and a 25/50/75/100% life target. Enable the separate automatic-medical checkbox if desired, then **Start automatic leveling**. The existing embedded worker checks targets, performs repeated weapon turns, finishes with Leave, waits for an official matching attack receipt, heals to the configured life target and advances. It stops when energy/reserve/limit/list conditions are reached and pauses on ambiguous controls, empty/unavailable equipment or unexpected outcomes. A Pillow is not accepted as the leveling weapon. Stop before editing the queue/settings.

Automatic medical planning uses fresh worker-visible personal stocks and the account’s saved Smart Medical item permissions/reserves/bonuses. Drugs, faction supplies and blood bags are excluded from the leveling planner. Item use requires one-use account/session authorization and verified quantity, cooldown and life changes. Cached medical/attack responses trigger bounded verification waits without repeating a consumed item or starting another fight. Pause/Resume retains pending operations. Manual checks and result verification remain available when automation is stopped.

Target lists and FF estimates do not guarantee wins, and exact XP is not fabricated. Queues and counters stay profile-local and separate from loss contracts. Source links/imports do not imply access to private FFScouter endpoints. Read [Torn’s scripting rules](https://www.torn.com/rules.php) before using game-interaction automation. See `Guides/LEVELING.txt` for setup and limitations. No real account fights or medical items are consumed during automated development tests.
