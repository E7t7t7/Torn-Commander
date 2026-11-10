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


## v0.6.92 responsiveness and worker controls
Active claims show the attached worker and its status, with Pause and Resume beside Open target. Pause does not require a seller refresh or release the remote claim. Resume validates the same claim/session and refuses expired claims or an outstanding native medical check. If a later Pause occurs while Resume is waiting, that queued Resume is cancelled.

Start uses the fresh saved claim or that claim's detail endpoint instead of refreshing both full seller lists every time. An existing refresh is shared when fresh state is needed. A known target opens directly without another seller API request. The claim-card DOM remains intact on unchanged heartbeats.

Worker startup formerly requested seven fast-sync datasets just to obtain player identity; one fresh user/profile response now suffices. Medical profile/cooldown/perk reads now use the documented v2 multi-selection route (user?selections=profile,cooldowns,perks, with perks only when due). One request occupies one global API queue slot. Its total queue/request wait is cancelled after 35 seconds so the browser's 45-second wait does not expire first. Global provider/API pacing is preserved.

These savings are verified in production-service mock tests, not a live timing benchmark. Identity, state and receipt checks are preserved. A slow/rate-limited API can still pause the worker; Resume rechecks rather than repeating medicine blindly.


## v0.6.94–v0.6.95 reservation choices
The default cap follows the provider's reported allowance; zero in the optional lower-limit field means automatic. Legacy unmarked ten-loss defaults migrate to automatic. Old deliberate ten-loss limits cannot be distinguished from that default; explicitly save ten again if desired.

Reserve using current energy reads the selected player's fresh profile and bars, verifies the player against the connected seller owner, and takes the minimum of floor(energy / 25), provider allowance, available slots and any lower override. It shows a confirmation before the POST; future refills are not included. 775 energy with a 30-loss allowance yields at most 30.

Reserve on both accounts opens a native chooser for two independently connected seller profiles. It defaults to energy per account or accepts a count per account. Both accounts are checked before either claim is posted. The preview accounts for shared slots; the second account is refreshed after the first succeeds and its approved count can be reduced if fewer slots remain.

These are two sequential remote reservations, not one dual-owner claim or an atomic server transaction. Confirmed first-account claims remain saved if the second fails or times out. No automatic resubmission or rollback is performed; inspect the per-account result before trying again. Existing explicitly armed auto-run settings still apply independently.


## v0.6.97 local batch execution
The user explicitly requested removing per-fight API confirmation waits. New FFScouter workers therefore use the existing visible-loss result detector and persist a provisional local receipt with owning worker, lease ID and fight timestamps. A successfully started attack alone never counts. Thirty claimed slots can run from thirty visible losses without per-fight Torn attack-log or FFScouter receipt requests. The existing official-receipt path remains available for ambiguous terminal screens; no visible result means no guessed loss.

A fixed batch baseline plus durable distinct local receipt IDs bounds further attacks, independently of delayed provider counters. Provider summaries and detailed hit IDs reconcile in background and at batch end. Local observations do not become confirmed earnings or payments. Exact unambiguous timestamp matches may link receipts to documented detailed hits; ambiguous matches remain unlinked. Use one execution source for a claim: phone claims can run at home, but do not also attack the same claim manually from the phone during a desktop batch.

The public OpenAPI document was rechecked at https://ffscouter.com/openapi/spec.yaml on 2026-11-10. Summary claim rows do not contain hit IDs; detailed order claim rows contain hits with attack_id, started_at and ended_at. The previous wait required every local ID in that delayed hit list and caused repeated stalls. New per-fight authorization checks only the already verified owning claim, local quota, pending mutation guard and actual expiry; background refresh can still end an expired/completed remote claim.
