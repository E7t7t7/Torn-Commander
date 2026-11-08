# FFScouter integration and phone handoff

## Audit and reused architecture
Inspected Commander main v0.6.88 and FFContractor main v0.8.2 before changes. Commander already has isolated WebView2 profiles, A/B loss workers, official Torn attack receipts, the medical optimizer, local loss orders, mixed cash/Xanax settlements, modern dashboard messages, Credential Manager, local IntelStore history, and GitHub releases/updater. These remain the implementation foundation; no competing worker or ledger was introduced. FFContractor keeps its lightweight v0.7 plain-DOM launcher lifecycle and v0.8 drag/viewport recovery.

## Data source and architecture
Official FFScouter OpenAPI v1.20.0 was inspected on 2026-11-08:
- https://ffscouter.com/openapi
- https://ffscouter.com/api-docs/guidance
- https://ffscouter.com/sell-losses

The documented seller contracts/claims/order-detail/claim/complete endpoints are used with the user's own registered key. The documented account/balance player_id verifies credential ownership; check-key alone does not identify a player. Keys use the documented query parameter over HTTPS and are never logged. This is separate from Torn Intel and the FFScouter battle-stat estimator credential.

FFSeller.cs separates parsing, bounded reservations, persistent mutation guards and native HTTP transport from MainForm.FFSeller.cs orchestration. LossOrder stores the remote provider, owning player/profile, claim ID, order number, target player ID, deadlines and receipt IDs. Claims and payments merge into existing orders. A reduced API row cannot replace its target with a claim/order ID. Saved history is not deleted when the API stops returning it.

FFScouter permits one active seller claim, first delivery within five minutes, at most five minutes between deliveries and fifteen minutes overall. Claim Max sends an actual positive bounded count, never a special zero. API rejection remains authoritative. No automatic registration or policy acceptance is performed. The connection UI links the service rules and requires explicit consent.

## Setup and phone-to-home execution
1. Update Commander to v0.6.89 and FFContractor to v0.9.0 using their existing update channels.
2. Select the intended Commander account. Save its separate Torn API key, sign its browser in, and configure its existing medical permissions.
3. Loss Desk > FFScouter seller: enter that same player's registered FFScouter seller key and connect. Windows Credential Manager stores it independently; the player must match the profile's Torn API identity.
4. For home execution, enable polling and **Run NEW phone/API claims**, then save BEFORE making the next phone claim. Existing claims are deliberately marked handled when arming; use their Start button explicitly.
5. Claim from FFContractor on the phone. The authenticated seller API shares the recent claim, not phone-local files. Within approximately 30 seconds plus service/network delays, an awake, running Commander can discover it and start its owning A/B worker.
6. Optional auto-completion releases the claim only when all reserved losses are confirmed by FFScouter and local official receipts reconcile. Otherwise use Complete claim explicitly.

Each claim gets one durable automatic start attempt; a paused, stopped, failed or restarted session does not silently restart. Review status and use Start explicitly after correcting a problem. Both workers can handle independent claims for different players; a single seller claim is never split across players. Do not attack the same claim manually on the phone while its home worker is running. There is no cross-device exclusive lock provided by the seller API.

No PC service runs while Commander is closed or asleep. Medical permission, energy, authentication, deadlines and existing worker safety checks still apply. Issued but unverified medicine remains paused for review before reusing the worker. Drug cooldown remains untouched.

## Accounting and history
The eight seller figures are Total Paid, Outstanding Payouts, Total Earned, Losses Completed, Contracts Finished, Available Contracts, Active Claims and Payments Pending. Earned amounts come from provider earnings, not requested-count quotes. Confirmed payments require authoritative resolved/paid status and no review flag. Messages are supporting references only. Remote paid cash is not added to a manually recorded duplicate. Other providers retain manual cash and Xanax totals without speculative conversion.

FFContractor and Commander retain their own older local history. Recent remote claims synchronize through FFScouter's seven-day window; older phone history does not automatically appear on Windows. The phone exports an explicit schema_version=1/provider=FFScouter/owner_player_id/claims/receipts document without credentials; Commander import of that export is not implemented in this release. No cloud synchronization is claimed.

A partial or interrupted POST persists a request guard before network transmission. Refresh reconciles by actual claim/order identity, excluding already-known IDs. If the outcome remains unknown, inspect official claims and explicitly clear the guard after a successful refresh; clearing does not undo a successful reservation. No mutation is blindly retried.

## Validation
Provider tests execute production parsing, ownership, reservation/completion and native HTTP code with fake responses. Cases cover reduced schemas, expiry, caps, durable intent failure, interrupted/restarted operations, target routing, profile credential isolation, attack deduplication and authoritative payout states. Dashboard tests exercise eight cards, account clearing, draft retention and arming payloads. Worker fixtures require official receipts on every external loss and stop new fights at expiry, alongside existing Loss X-change, medical and loss regressions.

Mobile tests simulate DOM mounting/remounting, visible viewports, drag persistence, target retention, bounded claims, stale/active guards, interrupted restart and TornPDA/Commander transport. Simulations do not establish real iOS injection or live claim execution. No live reservations, completions, attacks or medicine were used for validation. Existing WindowsBase/DPI build warnings are unchanged.

## Maintenance
Commander and FFContractor remain distinct repositories and release channels. No credentials, browser sessions, personal profiles or payout histories are committed. Torn Intel, travel, graffiti, stock/item intelligence, profile switching, updater and rollback modules are retained.
