# Spec: Bot War Features

## Feature Intent

As a developer building AI-controlled organizations, I want bots to declare wars and prosecute them toward **own `org_score` profit** — using shipped war/diplomacy cards (`make_rival`, opinion unlocks, `declare_war`, `sell_arms`, `force_war_win` / Ultimatum, `force_war_loss` / Surrender, `declare_revenge_war`) and peace outcomes (province ownership, gold spoils, winner/loser control shifts, possible country destroy) — so that bots treat war as a scored lever against the best-scoring rival rather than ignoring the war card suite.

This is an **umbrella discussion / decomposition** issue (#83). War gameplay dependencies (#69, #70, #72, #73, #74, #76) are closed. Post–feature-cut main also shipped card-draw 1-of-3 (#154), country-targeted `make_rival` (#158), country destroy (#142), and replaced `discoverAndControl` with **`control`**. No `plan.md` and no implementation belong in this specify step — only product behaviour, resolved decisions, phased issue split, and remaining clarifications before planning.

## Resolved Decisions (owner 2026-08-02 + 2026-08-09)

| Topic | Decision |
|-------|----------|
| Packaging | **Separated features**; `control` and war features compete via **scoring** (war sometimes wins, sometimes control does) |
| Objective | Maximize own **`org_score`** |
| Declare threshold | Prefer actions with best expected **Δorg_score**; prefer high win % via shared **`WarWinChanceEstimator`** |
| Unlocking | **In scope:** `make_rival` when creating a rival to war makes sense; also play **military advisor / ruler opinion** cards to unlock related war cards |
| In-war policy | **`sell_arms` first** (raises win chance); then force resolve when score-positive |
| Surrender | Playing `force_war_loss` on an **opponent country** (so the bot's preferred side wins) is intended |
| Revenge | Only when still **profitable** |
| Hurt focus | Prefer outcomes that hurt the **best-scoring rival org** |
| Config economics | Follow **shipped** `action_config.json` / `effect_config.json` (do not restore stale issue-text numbers) |
| Partial visibility | May prosecute with only one side known enough to act under card gates |
| Issue boundary | **Split into separate issues** after this decompose/specify |
| Naming | Agent chooses: `warUnlock`, `warDeclare`, `warProsecute` (camelCase like `control`) |
| Draw / discard | 1-of-3 draw must score by **intent**; discard must be a **common** mechanism (not only inside `control`) |
| Dual-side control | Profit calc **must** account for bot control on **both** war participants |
| Country destroy | Valid strategic lever to reduce enemy org scores (in addition to reducing score via enemy-controlled country damage) |

Still open / “discuss or explain” items are listed under **Ambiguities**.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`. Criteria below incorporate resolved decisions; remaining thresholds sit in Ambiguities.

### Umbrella behaviour (once child issues ship)

- A multi-org session where the candidate org can afford war-related plays and holds control skew favoring one side of a profitable rivalry.
  - Over subsequent ticks the bot unlocks rivalry/opinion when needed, declares when expected Δorg_score and win % justify it, sells arms on the preferred side first, and forces win/loss when that improves score (including intentional loss on the weaker side) => own `org_score` improves relative to an otherwise identical bot that never plays war cards (exact eval gate in Ambiguities).
- Edge: war cards are playable but expected Δorg_score is non-positive (including after dual-side control accounting).
  - The bot skips those plays => no “play war because IsPlayable” behaviour from war features.
- Edge: hand is full and no feature finds a suitable play.
  - A **shared** discard path can discard a low-value card (paying `discardGoldCost`) so a new draw offer can appear => discard is not owned exclusively by `control`.
- Edge: a 1-of-3 country-card draw offer is pending.
  - The bot picks the choice that best fits active intentions (war unlock/declare/prosecute vs control), not control-only priority => war-capable profiles can acquire war cards on purpose.
- Edge: bot holds meaningful control on **both** war participants.
  - Declare / prosecute / force-resolve decisions use a net profit model that includes peace control shifts on **both** countries, province/score moves, and gold => dual-side wars are not treated as single-side skew alone.
- Edge: peace (natural or forced) can empty a country's provinces.
  - Country destroy (`IsDestroyed`, control cleared, relations removed) is considered in profit reasoning when it hurts the lead rival more than it hurts the bot => destroy is an allowed strategic outcome, not ignored.

### Child issue A — Bot observation extensions (prerequisite; not `/implement-bot-feature`)

- Seat-useful war / relation / score signals needed for score-aware war are missing from `IBotObservation` today (wars, rivals, progress, CountryScore / org_score contributions, destroyed flag, combat inputs for win %).
  - Observation is extended so a bot can, for countries it can act on, know war participation + opponent when knowable, own-side progress, rival targets, score-facing signals, and destroyed state => war features can estimate Δorg_score and call `WarWinChanceEstimator` without privileged foresight.
  - `TargetCountryId` is **already** on `BotCardView` / draw choices for relation and revenge cards => do not re-specify that field; only fill remaining gaps.
- Any `IBotObservation` / views / `BotObservation.Build` change requires its own `/specify` + `/plan` => Constitution Planning Discipline is honored.

### Child issue B — Bot infrastructure (scoring arbitration, shared discard, intent-aware draw)

- Multiple features (`control`, `warUnlock`, `warDeclare`, `warProsecute`, …) can each propose a play on the same strategic tick.
  - The bot selects among proposals by **estimated Δorg_score** (or an approved soft threshold) rather than fixed profile order alone => control and war coexist by scoring (resolved decision).
- Hand full, no suitable play from any feature.
  - A common discard helper runs with a **feature-neutral** (or shared weighted) value function => `ControlFeature.TryDiscardForBetterHand` is not the only discard path.
- Pending 1-of-3 draw choices while war features are enabled.
  - Draw selection uses shared intent/value scoring (not only `IsControlUsable` → `RaisesControl` → `IsPlayable`) => war cards can win the offer when they better serve score.

### Child issue C — `warUnlock` feature

- Bot has playable `make_rival` naming a country that would unlock a profitable future declare (or improves war options), and gold/opinion gates allow it.
  - Bot plays `make_rival` for that target => rivalry exists for a subsequent declare path.
- Bot cannot yet play `declare_war` / `sell_arms` / force cards because opinion is below card conditions, but has playable `improve_military_advisor_opinion` and/or `improve_ruler_opinion` (and diplomacy opinion where needed for `make_rival`).
  - Bot plays those unlock opinion cards when the expected path to a profitable war beat spending the gold elsewhere => complicated multi-step unlock flow is supported.
- Unlock play would not lead toward a profitable war / is dominated by a better scored control or war play.
  - Bot skips unlock => no blind opinion grinding.

### Child issue D — `warDeclare` feature

- Bot has playable `declare_war` (or `declare_revenge_war`) with a named rival, can afford it under gold policy, expected Δorg_score is positive after dual-side accounting, and `WarWinChanceEstimator` win % is acceptable under the chosen threshold.
  - Bot plays that card => war starts (or rematch) with shipped costs/effects.
  - Multiple declare instances => picks highest expected Δorg_score (preferring higher win % when deltas are close), not first-in-hand.
- Revenge is eligible but rematch is not profitable.
  - Bot does **not** play `declare_revenge_war` solely because it is eligible.

### Child issue E — `warProsecute` feature

- Active war where the bot wants its preferred side stronger; `sell_arms` is playable and gold policy allows it.
  - Bot prefers `sell_arms` before forcing resolution => temporary damage bonus raises estimated win % via the shared estimator path.
- `force_war_win` is playable on a country whose win raises expected org score (typically preferred side).
  - Bot may play Ultimatum when forcing beats waiting / further arms under the chosen policy => selected country wins; peace resolution applies.
- `force_war_loss` is playable on a country whose **loss** raises expected org score (e.g. play on the opponent country, or on the bot's weaker side to limit losses).
  - Bot may play Surrender in that intentional-loss / loss-limiting case => peace favors the preferred side.
- Natural peace vs force still has open policy knobs (Ambiguities) but both cards remain legal tools to limit losses in a specific country.

### Child issue F — Eval packages

- Each registered war-related feature id gets `Docs/BotFeatures/<featureId>/` eval config mirroring `Docs/BotFeatures/control/`.
  - `targetActions` lists the action ids that feature owns; parameters cover reserves / profit / win-% thresholds; horizon/seeds/opponents suffice for declare→battles→peace/destroy signal (exact settings in Ambiguities).
  - Gate metric remains paired-seed **`org_score`**.

## Out of Scope

- Writing `plan.md` or any implementation in this specify step.
- New war gameplay mechanics, new action ids, or rebalancing card costs/effects / peace formulas (bots consume shipped config).
- Bot-only privileged information beyond what player-facing systems already expose for that org.
- Replacing `control` or `baselineCardPlay`; coexistence via scoring + shared infra only.
- Multi-country / allied wars (game remains two participants per war).
- Using `/implement-bot-feature` to sneak observation, sink, systems, or Assets changes past Planning Discipline.
- Teaching bots non-war non-unlock cards except as needed for the shared discard/draw value function and control coexistence.

## Tech Notes

### Standing anchors (post–feature-cut main)

- **Bot tick:** `Bot.ExecuteDecisionTick` (`src/Game.Bots/Bot.cs`) — country-card **acquisition** (draw/receive) can run every update while pending; **strategic** `feature.Tick` is gated **once per calendar day** via `_lastActedDate`. There is **no** `botDecisionIntervalHours` today (see Ambiguities — owner answer 19 vs current code).
- **Features:** `IBotFeature.Tick(IBotObservation, IBotCommandSink, Random)`. Registry default: `baselineCardPlay`, `control` only (`BotFeatureRegistry.CreateDefault`). Default profile in `game_settings.json` enables **`control`** only. Discovery / `discoverAndControl` are gone.
- **Observation today:** gold, hands, draw choices (`CountryCardDrawChoices` with `TargetCountryId`, `IsControlUsable`, `RaisesControl`, `IsPlayable`), countries with control breakdown and character opinions. **`TargetCountryId` present.** Missing: wars, relations, progress, occupation, CountryScore/org_score, `IsDestroyed`, war-card kind flags, win-chance inputs.
- **Draw selection today:** `Bot.GetChoicePriority` prefers control-usable → raises-control → playable → else — **war-intent unaware**.
- **Discard today:** `IBotCommandSink.DiscardCountryCard` exists; **only** `ControlFeature.TryDiscardForBetterHand` calls it when hand full and no control play. Cost: `GameSettings.DiscardGoldCost` (50).
- **Commands:** `PlayCountryCard` / `PlayOrgCard` / `DiscardCountryCard` / draw+receive — war cards are ordinary country cards.
- **Win % helper (shipped):** `WarWinChanceEstimator.EstimateAttackerWinPercent` in `src/Game.Systems/WarWinChanceEstimator.cs` (recruits × damage / enemy durability; pending sell_arms/revenge bonuses). Natural home for declare/prosecute heuristics.
- **War domain:** `Wars` (`DeclareWar`, `ResolveWar`, progress, peace-by-chance). Peace control shifts from `game_settings.json`: winner `peaceWinnerControlIncreaseFraction` **0.5**, loser `peaceLoserControlDecreaseFraction` **1.0**. Destroy: `CountryDestroySystem.TryDestroyIfNoProvinces` → `IsDestroyed`, clear control.
- **Score:** `OrgScoreCollector` — Σ `(control/100) × CountryScore`. Destroy / province transfer / control ± are the war→score path.
- **Shipped action ids & gold (verify at plan time against `action_config.json`):**

| ActionId | UI | Gold | Notable gates |
|----------|-----|------|---------------|
| `make_rival` | Make rival | 75 | diplomacy opinion ≥30; named target |
| `improve_military_advisor_opinion` | Improve mil opinion | 30 | control ≥10 |
| `improve_ruler_opinion` | Improve ruler opinion | 50 | control ≥20 |
| `declare_war` | Declare war | 150 | `targetRulerOrMilitaryOpinion` ≥50; rival; neither at war |
| `sell_arms` | Sell arms | 175 | mil opinion ≥80; peacetime OK |
| `force_war_win` | Ultimatum | 300 | control ≥10; mil ≥50; in war; own progress ≥50 |
| `force_war_loss` | Surrender | 25 | control ≥20; mil ≥80; in war; own progress ≤0 |
| `declare_revenge_war` | Revenge | 125 | control ≥20; mil ≥25; warFree; revengeEligible |
| `decrease_enemy_control` | Decrease enemy control | 250 | enemy control > mine (ownership TBD — Ambiguities) |

- **Prior specs:** `Docs/Specs/26_07_16_14_bot-org-api/`, bot-feature-eval harness, war card specs under `Docs/Specs/26_07_29_*`, country-targeted relation cards `26_08_08_23_*`, country-destroy, card-draw rework. Bot feature docs: only `Docs/BotFeatures/control/` remains.

### Proposed issue split (after this umbrella is approved)

| Child | Scope | Authority |
|-------|--------|-----------|
| **A** Observation extensions (war/relation/progress/score/destroy + estimator inputs) | `/specify`+`/plan` |
| **B** Shared discard + intent-aware draw scoring + cross-feature Δorg_score arbitration | `/specify`+`/plan` |
| **C** `warUnlock` (`make_rival`, opinion cards) | `/implement-bot-feature` after A–B |
| **D** `warDeclare` (`declare_war`, `declare_revenge_war` + estimator) | `/implement-bot-feature` after A–B |
| **E** `warProsecute` (`sell_arms`, `force_war_win`, `force_war_loss`; dual-side profit; optional destroy EV) | `/implement-bot-feature` after A–B |
| **F** Eval configs under `Docs/BotFeatures/<id>/` | `/implement-bot-feature` configs carve-out |

Parent #83 stays the umbrella until children are filed; do not implement the whole stack in one PR.

### Stale vs prior revision of this spec

- `discoverAndControl` → **`control`**; discovery language removed.
- Action costs/ids updated (`force_war_win` / `force_war_loss`, not old ultimatum/surrender ids; costs no longer 100/200/500…).
- `WarWinChanceEstimator` already exists (was “will use later”).
- `TargetCountryId` already on observation.
- Card-draw rework, country-targeted `make_rival`, country destroy, shared discard, feature arbitration, dual-side control are first-class.

## Ambiguities

- [NEEDS CLARIFICATION: Feature scoring arbitration — when `control` and a war feature both have a playable, affordable candidate on the same strategic tick, should the bot compare estimated Δorg_score for each candidate and pick the global best (single strategic play that day), or use a soft threshold (war only if Δ ≥ X)? Should unlock actions (`make_rival`, opinion cards) sit in the same scoring pool or run as a separate pre-pass before scored plays?]
- [NEEDS CLARIFICATION: Shared discard — when the hand is full and no feature found a suitable play, should a common bot discard helper (orchestrator or shared util) discard the lowest-value card by a shared scoring function, and should that function be feature-neutral with weights for control + war + unlock rather than control-only?]
- [NEEDS CLARIFICATION: Card-draw 1-of-3 — should `Bot.GetChoicePriority` be replaced/extended so choices are scored by active intentions (prefer `declare_war` / `make_rival` / high CountryScore+win% targets when war features are enabled; prefer control cards when control wins arbitration)? Who owns that scorer — `Bot` acquisition phase or a shared helper used by all features?]
- [NEEDS CLARIFICATION: Bot decision cadence — you answered “not per day, per hours configured in config,” but `Bot.cs` still gates strategic `feature.Tick` once per calendar day and there is no bot-decision-interval setting (eval `hoursPerTick` only sizes the simulation step / war battle rounds). Keep one strategic play per day, add something like `botDecisionIntervalHours` in `game_settings.json`, or did “hours” only mean simulation/`hoursPerTick`?]
- [NEEDS CLARIFICATION: Dual-side control profit — when the bot holds meaningful control on both war participants, must declare/prosecute/surrender require net Δorg_score > 0 after both sides’ peace control shifts (+50% winner / −100% loser), province transfers, and gold? Any hard veto when the bot is top controller on both sides unless net score still wins?]
- [NEEDS CLARIFICATION: Country destroy as strategy — should the bot actively pursue wars/peace that eliminate a country (zero provinces → `IsDestroyed`) when that maximally hurts the lead rival, or treat destroy only as a passive side effect of otherwise score-optimal wars? Should `decrease_enemy_control` live in the war feature set or stay with control/baseline?]
- [NEEDS CLARIFICATION: Natural peace vs forced resolve — when progress is already in the peace-chance band and `force_war_win` / `force_war_loss` are playable, should the bot wait for free natural peace unless forcing improves expected score (or saves future loss), and is cheap `force_war_loss` (25g) primarily a loss-limiting tool on the bot’s weaker side?]
- [NEEDS CLARIFICATION: Eval length/seeds — for war features, what `endDate` / seed count / `hoursPerTick` should evals use so declare → battles → peace/destroy completes often enough? Is control’s ~5y / 10 seeds / 24h insufficient by default?]
- [NEEDS CLARIFICATION: Gold policy — should war features share one `minGoldReserve` with control, use per-action budgets (declare vs ultimatum vs discard-at-50g), or allow spending down for high-EV force-resolve plays?]
- [NEEDS CLARIFICATION: Occupation EV — for v1 profit estimates, is control × CountryScore skew enough, or should observation expose occupied province counts on countries for transfer EV?]
- [NEEDS CLARIFICATION: Eval opponents — first evals against `control`-only, `baselineCardPlay`, passive orgs, or mirror war bots?]
- [NEEDS CLARIFICATION: Success bar for first ship — (a) sometimes plays war cards in eval, (b) beats control-only twin on org_score by ε, or (c) wins wars on the side it favors — which metric blocks merge?]
