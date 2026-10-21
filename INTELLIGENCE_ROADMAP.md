# Intelligence expansion audit — 2026-10-20
Baseline: v0.6.53 / 5095426. This document tracks implemented work, not full Torn Intel parity.

## Existing architecture and preservation
.NET 8 x64 WinForms, embedded Edge WebView2, raw HTML/JS dashboard with C# message handlers. Credentials are per Windows user and Commander profile in Credential Manager. Existing atomic JSON journals remain readable. GitHub source ZIP releases rebuild through an external updater, retain rollback ZIPs, and maintain the stable shortcut. Do not replace these with a new app.

| Module | Existing functional coverage | Main gap |
|---|---|---|
| Profiles/browser | Isolated cookies, credentials, profile switching | Connection diagnostics for additional providers |
| Travel/sequence | Foreign feeds, cached prices, actual-flight timing, 24h sequences, rehab-first, arrival scans | Independently evaluated item forecasts, wait-versus-alternative comparisons |
| Loss Desk | Unified order creation, dual shared accounting, healing, energy handoff, resizable workers | Live game layout can still change; keep diagnostics |
| Graffiti/merits/progression | Guided helpers, local recommendations, current-account goals | Broader supported progression histories |
| Market/cart/bazaar | Listings scans, fee-aware trade candidates, actual-fee ledger, cart helpers | Persistent comparable market quotes and alert coverage |
| Company | Daily journal, finances with unknown-cost handling, staff layouts and explanations, advertising trial, standings | Staff history, multi-company drill-down, comparable scenarios |
| Faction/war | Permission-aware roster, opt-in 5-minute activity, heatmaps, war reports and payout CSV | Armory/bank/territory history, arbitrary faction comparisons |
| Stocks | Holdings, sampled prices, targets, block and payout scenarios | Complete exchange historical backfill and tier valuation |
| UI/updater | Themes, charts, sidebar, optional classic tools, rollback | Unified item intelligence and source diagnostics |

## Documented Torn Intel boundary
Sources: https://torn-intel.com/developers and https://torn-intel.com/openapi.json (reviewed 2026-10-20).
Approved application key: GET /api/v1/public/foreign-stock; /foreign-stock/history; /api/v1/foreign-stock/travel-table. Default key quota is one request/minute, shared scheduling must be conservative. Send X-Torn-Intel-Key. Authentication failures 401/403 require user action, not repeated retry. Honor Retry-After on 429. No pretending to be a browser extension.
The website login uses a separate Torn API key/session. Public stock access does not identify a player or authorize private faction/company exports. The documented API does not expose restock predictions. Website login is not permission to extract undocumented private APIs. Native account-history sync remains blocked until an authorized documented export or schema is available. CSV import accepts actual timestamped observations; origin is recorded.
An approved client key must be requested at https://torn-intel.com/contact. No request has been sent on the user's behalf.

## Prioritized architecture
1. Add a versioned SQLite intelligence database alongside existing JSON. Store source observation time separately from collection time, profile scope, deduplication and provider health. Do not fabricate a timestamp for an old feed.
2. Foreign-stock provider interface: existing public feeds, approved-key Torn Intel, typed CSV imports, local saved history. Background scheduler with backoff and auth circuit break. Independent per-source forecasts, never merge disagreeing providers into invented transitions.
3. Item catalog/watchlist: Neumune Tablet #361 in Switzerland; generic foreign catalog from feeds, other acquisition types only where supported. Unknown stock stays unknown, not zero.
4. Conservative event-window estimator: observed zero-to-positive intervals, exclude large gaps, learn only from adequate cycles, retain held-out errors and window coverage. No exact countdown promises.
5. Company and faction snapshots enter the same historical architecture, retaining legacy journals as compatibility sources. Add measured company scenarios and travel decisions from the same evidence.
6. UI modules use consistent cards, source freshness, empty/error states and detailed assumptions. No hosted-global-index features claimed without a provider.

## Remaining scope
This is a multi-stage expansion. Full armory/banking/territory/alliance/recruitment/global indexing and paid-provider parity remain unimplemented until explicitly listed as completed in the changelog. New local history cannot backfill time when Commander was not collecting. No arbitrary star-up percentages or company profit gains will be invented.

## Completed stages in this development run
- v0.6.54: SQLite foundation, public/approved stock providers, native item watchlists, conservative restock windows with evaluation, CSV stock history, source diagnostics and desktop alerts; company/faction mirrors retained alongside JSON.
- v0.6.55: company history/scenarios and internal navigation, employee deltas/scan-penalty heatmap, comparable advertising groups, integration with travel/sequence plans and waiting alternatives; legacy unconstrained point extrapolation retired.
These releases do not implement every feature in the master directive. Remaining items include provider-authorized private-history import schemas, complete market/auction backfills, native armory/banking/territory/alliance/recruitment tools, arbitrary faction comparisons, and remote Discord notifications. No blockers have been worked around through private endpoints.

- v0.6.56: urgent Pillow slot resolution and paused-fight resume repair, with repeated-hit solo/dual regression fixtures.
- v0.6.57: opt-in official market-ask sampling/history, crossing alerts, manual-trade mirroring, complete streaming CSV exports and source diagnostics. Full sales-demand/auction/bazaar history remains outside this stage.
