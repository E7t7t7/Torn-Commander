# Slots batches

In **Casino → Slots**, choose a denomination, enter **100**, then press **Start batch**. The defaults are **$1,000 × 100 = $100,000 staked before winnings**. Start caps the count at the token balance it reads; it does not promise 100 tokens exist.

Keep the selected account's Slots tab open and Commander running. The runner places one ordinary native spin, verifies its official result, then proceeds. No clicks are needed between successful spins. A Full-access Torn API key saved for that Commander profile is required for personal logs. The first API checks must succeed before any spin.

Use **Pause** to pause after the pending result settles, **Resume / check pending result** to continue, or **Stop batch** to end early after checking a pending result. Restarting Commander never restarts betting automatically. An uncertain bet remains pending and is not clicked a second time.

The finished record shows confirmed spins, total stake, gross winnings and net winnings minus stake. These figures use slot receipts, not unrelated wallet changes. Old manually tracked casino sessions remain unchanged. An interrupted record can have one pending spin whose stake/payout is deliberately excluded until verified.

Optional stop-loss/profit limits are evaluated after a spin settles. The last spin can exceed a limit. Stake never increases. Zero means the corresponding limit is off. All-tokens captures only the initial balance (maximum 10,000); it never refills.

No refill purchases, withdrawals, direct game-network calls, animation acceleration or outcome manipulation are implemented. The helper only clicks a verified native denomination control. Switching accounts, hiding the game or failed verification pauses new bets. Play Slots on only this device while the batch runs: concurrent manual/phone spins can produce ambiguous receipts and stop the batch.

## Data and verification

- [Official Torn Slots documentation](https://wiki.torn.com/wiki/Slots): supported denominations and token mechanics.
- [Official Torn API schema](https://www.torn.com/swagger/openapi.json): user profile/casino/money, personal logs and public log types. Read-only checks confirmed combined profile/casino/money roots, and log IDs 8300 (win) / 8301 (lose).
- [Primary Tornscope casino normalization implementation](https://github.com/Cyxno/tornscope/blob/main/packages/database/src/normalizers/casino.ts) and [its recorded schema tests](https://github.com/Cyxno/tornscope/blob/main/packages/database/tests/activity-normalization.test.ts): receipt data uses bet_amount and winning won_amount. Commander has its own parser and refuses unsupported shapes.
- Native controls are identified by denomination, visible token count and three reels. The runner does not infer payouts from reel images. It does not modify the game's requests, speed or random outcomes.

Tests use mocked API responses and local browser pages. Full 100-spin simulated runs, interruption recovery, exact accounting, identity boundaries and responsive controls are checked. No real tokens were consumed during development. First live use still needs to confirm that Torn's current rendered controls and receipt payloads match the documented/published forms. A discrepancy pauses instead of spending more tokens.

Storage is the existing local casino ledger; keys stay in Windows Credential Manager. Pending operation IDs and receipts persist per account. Nothing runs while Commander is closed.
