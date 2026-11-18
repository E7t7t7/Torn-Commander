# Mission desk

Open **Honors / Missions → Missions**. Press **Open Missions**, select the agent/contract in Torn, then **Scan current missions**. Scanning reads contracts currently loaded for that agent, not every locked or unavailable agent.

Use **Run mission** on one supported card, or **Run supported contracts** to process up to five eligible contracts using one shared attack budget. The default is ten attacks and no nerve/energy refill. The planner shows exact targets and finish choices before running. A completed contract can be claimed without another fight. Unknown/special objectives remain visible with a manual explanation.

Fight settings select the equipped weapon, interval, energy reserve, attack budget and life threshold. Healing requires both the mission checkbox and saved automatic personal Smart Medical permission. All item reserves and cooldown checks still apply; drugs and faction supplies are excluded. No gear is bought, equipped or discarded automatically.

Pause and Resume operate on the same fight. Stop retires the mission run and retains its history. Closing the shared worker through Loss Desk also retires it. After an interruption, Scan checks the outstanding official attack receipt; another mission fight is blocked while it is unknown. Per-contract progress and original target order persist locally. Nothing starts automatically after application restart.

## Delivery and reward verification

A simple delivery is supported only if its objective links one exact item and one recipient. Enable **Allow one exact mission item send**. Open the item delivery page/category if needed. The adapter requires a personal inventory row with the exact item ID/name, an unambiguous native Send form, one-unit quantity, and a resolved recipient profile link matching the objective.

It submits Send once and requires a visible matching item/recipient receipt. Wrong or missing identifiers, unsupported form layouts and unknown receipts stop the transfer. A dispatched but uncertain Send remains pending and is never repeated automatically. **Clear interrupted page action** is for use after checking Torn yourself; it is not proof that an item was returned.

Parcels/gifts, item purchases, codes, messages, equipment restrictions, travel/group NPC attacks and unfamiliar tasks remain manual. Complete those in Torn, then re-scan. The module does not claim universal mission support.

Rewards are requested only through a current verified **Complete** control. Counts label these as claim requests; they do not promise a confirmed cash/credit payment from a disappearing card. Check Torn for balances.

## Sources and tests

Mission mechanics and simple contract requirements: [official Missions guide](https://wiki.torn.com/wiki/Mission) and [official Duke contract list](https://wiki.torn.com/wiki/List_of_Duke_Missions). Native mission containers/title compatibility are documented by the primary TornTools authors: [mission-hints compatibility](https://github.com/Mephiles/torntools_extension/blob/master/src/common/features/mission-hints/COMPATIBILITY.md).

Planner and shared-worker regression tests run without accounts. The browser test is a local fixture using Playwright; set COMMANDER_PLAYWRIGHT to its installed module (or install Playwright normally) and optionally COMMANDER_EDGE to a browser executable, then run TEST_MISSIONS_BROWSER.cjs from the repository root.

The current live mission/Send layout was not inspected through desktop control in this chat. Browser tests verify fixture behavior, not a live account. Strict preconditions deliberately refuse unfamiliar forms. No live accept, fight, healing or transfer was used for testing.
