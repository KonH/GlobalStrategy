# Spec: Bot War Features

## Feature Intent

As a developer building AI-controlled organizations, I want bots to declare wars and prosecute them toward **own `org_score` profit** — using shipped war/diplomacy cards (`make_rival`, opinion unlocks, `declare_war`, `sell_arms`, `declare_revenge_war`) and **natural peace** outcomes (province ownership / occupation transfers, gold spoils, winner/loser control shifts, possible country destroy) — so that bots treat war as a scored lever against the best-scoring rival rather than ignoring the war card suite.

**v1 resolution path:** bots raise win chance via `sell_arms`, then **wait for natural peace** — they do **not** play Ultimatum / Surrender (`force_war_win` / `force_war_loss`). Those cards are currently disabled (`featureFlags.enableForceWarCards: false`); InitSystem skips creating them when the flag is false. Force-resolve may return later if the flag is turned on — **out of scope for this umbrella’s first ship**.

**Profit model:** expected Δorg_score must include **enemy control decrease, occupation / province-transfer EV, and gold change** — not only `(control/100)×CountryScore` on the preferred side.

**Cadence:** strategic bot decisions run on a **configurable hour interval** (proposed `botDecisionIntervalHours`, **default 4**), not once per calendar day. `hoursPerTick` remains the simulation step size only.

This is an **umbrella discussion / decomposition** issue (#83). War gameplay dependencies (#69, #70, #72, #73, #74, #76) are closed. Post–feature-cut main also shipped card-draw 1-of-3 (#154), country-targeted `make_rival` (#158), country destroy (#142), and replaced `discoverAndControl` with **`control`**. No `plan.md` and no implementation belong in this specify step — only product behaviour, resolved decisions, phased issue split, and remaining clarifications before planning.

## Resolved Decisions (owner 2026-08-02 + 2026-08-09 + 2026-08-12)

| Topic | Decision |
|-------|----------|
| Packaging | **Separated features**; `control` and war features compete via **scoring** (war sometimes wins, sometimes control does) |
| Objective | Maximize own **`org_score`**; prefer outcomes that hurt the **best-scoring rival org** |
| Scoring arbitration | On the same strategic tick, pick the **global best estimated Δorg_score** among `control` + war + unlock candidates; unlocks sit in the **same scoring pool** (no separate pre-pass) |
| Declare threshold | Prefer actions with best expected **Δorg_score**; prefer high win % via shared **`WarWinChanceEstimator`** |
| Dual-side profit | Declare / prosecute require **net Δorg_score > 0** after both sides’ peace control shifts (+50% winner / −100% loser), province/occupation transfers, destroy EV, and gold |
| Target preference | **General rule:** prefer wars against countries where **enemy control is higher** OR **no bot control** is present (exact hard-filter vs soft bias still open — Ambiguities) |
| Profit EV inputs | Must include **enemy control decrease, occupation, and gold change** (not control×CountryScore alone) |
| Unlocking | **In scope:** `make_rival` when creating a rival to war makes sense; also play **military advisor / ruler opinion** cards to unlock related war cards |
| In-war policy | **`sell_arms` first** (prosecute via arms); then **wait for natural peace**. No force-resolve in v1 |
| Force cards | **IGNORE** `force_war_win` / `force_war_loss` for this umbrella’s first ship (`enableForceWarCards` defaults false; cards absent from world when disabled) |
| Revenge | Only when still **profitable** |
| Country destroy | Scored **side effect** in the profit model (not a dedicated hunt-destroy mode) |
| `decrease_enemy_control` | Stays with **control / baseline**, not war features |
| Config economics | Follow **shipped** `action_config.json` / `effect_config.json` (do not restore stale issue-text numbers) |
| Partial visibility | May prosecute with only one side known enough to act under card gates |
| Issue boundary | **Split into separate issues** after this decompose/specify |
| Naming | `warUnlock`, `warDeclare`, `warProsecute` (camelCase like `control`) |
| Shared discard | **Common shared discard helper** (orchestrator / shared util) — not only inside `ControlFeature` |
| Draw / intent | **Shared intent/value scorer** for 1-of-3 draw, called from Bot acquisition (replaces / extends control-only `GetChoicePriority`) |
| Decision cadence | Change strategic tick from once-per-calendar-day to **per-hour interval configured in config, default 4 hours** (`botDecisionIntervalHours` or similar). Exact setting placement + acquisition vs Tick split still open (Ambiguities) |
| Gold policy | Share **`minGoldReserve` with control**. Force-resolve carve-out is obsolete; remaining nuance (strict reserve vs high-EV dip for expensive `declare_war` / `sell_arms`) in Ambiguities — **proposed default:** shared reserve, allow dipping only when expected Δorg_score for those expensive war plays clearly justifies it |
| Eval cadence / horizon | War evals **lengthen `endDate` / seed count** vs control defaults; decision cadence follows the 4h interval; `hoursPerTick` stays the simulation step |
| Eval success bar | First ship requires **both**: (a) sometimes plays war cards in eval **and** (b) beats control-only twin on `org_score` |
| Eval opponents | **Proposed primary gate:** control-only twin (confirm in Ambiguities); passive / `baselineCardPlay` optional secondary |

Still-open items that need owner confirmation before `/plan` are listed under **Ambiguities** only.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`. Criteria below incorporate resolved decisions; remaining knobs sit in Ambiguities.

### Umbrella behaviour (once child issues ship)

- A multi-org session where the candidate org can afford war-related plays and holds control skew favoring one side of a profitable rivalry (enemy control higher on the preferred target, or no bot control there).
  - Over subsequent strategic ticks (default **every 4 in-game hours** once configured), the bot unlocks rivalry/opinion when needed, declares when expected Δorg_score and win % justify it, sells arms on the preferred side, and **waits for natural peace** (no Ultimatum/Surrender) => own `org_score` improves, and evals satisfy **both** success bars: war cards are sometimes played **and** the candidate beats a control-only twin on `org_score`.
- Edge: war cards are playable but expected Δorg_score is non-positive after dual-side accounting (control shifts, occupation, destroy EV, gold).
  - The bot skips those plays => no “play war because IsPlayable” behaviour from war features.
- Edge: hand is full and no feature finds a suitable play.
  - A **shared** discard path can discard a low-value card (paying `discardGoldCost`) so a new draw offer can appear => discard is not owned exclusively by `control`.
- Edge: a 1-of-3 country-card draw offer is pending.
  - The bot picks the choice that best fits active intentions via the **shared intent/value scorer** (war unlock/declare/prosecute vs control), not control-only priority => war-capable profiles can acquire war cards on purpose.
- Edge: bot holds meaningful control on **both** war participants.
  - Declare / prosecute decisions use a net profit model that includes peace control shifts on **both** countries, occupation/province moves, destroy EV, and gold => dual-side wars are not treated as single-side skew alone.
- Edge: natural peace can empty a country's provinces.
  - Country destroy (`IsDestroyed`, control cleared, relations removed) is considered in profit reasoning when it hurts the lead rival more than it hurts the bot => destroy is an allowed scored side effect, not a hunt mode and not ignored.
- Edge: `featureFlags.enableForceWarCards` is false (shipped default) so `force_war_win` / `force_war_loss` entities are not created.
  - War features never depend on those cards for v1 behaviour => first ship works with force cards absent from the world.

### Child issue A — Bot observation extensions (prerequisite; not `/implement-bot-feature`)

- Seat-useful war / relation / score / occupation signals needed for score-aware war are missing from `IBotObservation` today (wars, rivals, progress, occupation, CountryScore / org_score contributions, destroyed flag, combat inputs for win %).
  - Observation is extended so a bot can, for countries it can act on, know war participation + opponent when knowable, own-side progress, rival targets, score-facing signals, occupation signals (depth in Ambiguities), and destroyed state => war features can estimate Δorg_score (including occupation + enemy control decrease + gold) and call `WarWinChanceEstimator` without privileged foresight.
  - `TargetCountryId` is **already** on `BotCardView` / draw choices for relation and revenge cards => do not re-specify that field; only fill remaining gaps.
- Any `IBotObservation` / views / `BotObservation.Build` change requires its own `/specify` + `/plan` => Constitution Planning Discipline is honored.

### Child issue B — Bot infrastructure (interval, scoring arbitration, shared discard, intent-aware draw)

- Strategic `feature.Tick` is still gated once per calendar day via `_lastActedDate` in `Bot.cs`.
  - Cadence becomes a configurable hour interval (**default 4** hours) so war progress / arms / declare chains can act within a sim year at a useful rate => bots are not limited to one strategic play per calendar day.
- Multiple features (`control`, `warUnlock`, `warDeclare`, `warProsecute`, …) can each propose a play on the same strategic tick.
  - The bot selects among proposals by **global best estimated Δorg_score** (unlocks included in the same pool) rather than fixed profile order alone => control and war coexist by scoring.
- Hand full, no suitable play from any feature.
  - A common discard helper runs with a **feature-neutral** (or shared weighted) value function => `ControlFeature.TryDiscardForBetterHand` is not the only discard path.
- Pending 1-of-3 draw choices while war features are enabled.
  - Draw selection uses the shared intent/value scorer called from Bot acquisition (not only `IsControlUsable` → `RaisesControl` → `IsPlayable`) => war cards can win the offer when they better serve score.

### Child issue C — `warUnlock` feature

- Bot has playable `make_rival` naming a country that would unlock a profitable future declare (or improves war options), and gold/opinion gates allow it.
  - Bot plays `make_rival` for that target when that candidate wins global Δorg_score arbitration => rivalry exists for a subsequent declare path.
- Bot cannot yet play `declare_war` / `sell_arms` because opinion is below card conditions, but has playable `improve_military_advisor_opinion` and/or `improve_ruler_opinion` (and diplomacy opinion where needed for `make_rival`).
  - Bot plays those unlock opinion cards when the expected path to a profitable war beats spending the gold elsewhere in the shared scoring pool => complicated multi-step unlock → declare / sell_arms flow is supported.
- Unlock play would not lead toward a profitable war / is dominated by a better scored control or war play.
  - Bot skips unlock => no blind opinion grinding.

### Child issue D — `warDeclare` feature

- Bot has playable `declare_war` (and/or `declare_revenge_war` if kept here — Ambiguities) with a named rival, can afford it under gold policy, expected net Δorg_score is positive after dual-side accounting (including occupation + enemy control decrease + gold), and `WarWinChanceEstimator` win % is acceptable under the chosen threshold; target preference favors enemy-higher-control / no-bot-control countries.
  - Bot plays that card => war starts (or rematch) with shipped costs/effects.
  - Multiple declare instances => picks highest expected Δorg_score (preferring higher win % when deltas are close), not first-in-hand.
- Revenge is eligible but rematch is not profitable.
  - Bot does **not** play `declare_revenge_war` solely because it is eligible.

### Child issue E — `warProsecute` feature (`sell_arms` focus)

- Active war where the bot wants its preferred side stronger; `sell_arms` is playable and gold policy allows it; expected Δorg_score from raising win % (via arms → natural peace) is positive after dual-side accounting.
  - Bot plays `sell_arms` on the preferred side to raise estimated win % via the shared estimator path, then relies on **natural peace** for resolution => temporary damage bonus improves win chance without force cards.
- Force cards are disabled / absent.
  - `warProsecute` does **not** play or depend on `force_war_win` / `force_war_loss` for v1 => first ship is correct under `enableForceWarCards: false`.
- Other in-war non-force tools that later prove score-relevant may join this feature; v1 acceptance centers on **`sell_arms`**.

### Child issue F — Eval packages

- Each registered war-related feature id gets `Docs/BotFeatures/<featureId>/` eval config mirroring `Docs/BotFeatures/control/` (`seedCount: 10`, `hoursPerTick: 24`, `endDate: null` today; opponentFeatures currently `baselineCardPlay` in that file).
  - `targetActions` lists the action ids that feature owns (`sell_arms` for prosecute — **not** force cards); parameters cover reserves / profit / win-% thresholds; horizon/seeds lengthen vs control so declare → battles → natural peace / destroy can complete under the 4h decision interval (exact settings in Ambiguities).
  - Gate metric remains paired-seed **`org_score`**.
  - Success requires **(a)** war-related actions appear in eval play traces **and** **(b)** candidate beats **control-only twin** on `org_score` (proposed primary opponent; confirm Ambiguities).

## Out of Scope

- Writing `plan.md` or any implementation in this specify step.
- Bot play of **`force_war_win` / `force_war_loss` (Ultimatum / Surrender)** for this umbrella’s first ship — even if `enableForceWarCards` is later turned on; that is a separate follow-up.
- New war gameplay mechanics, new action ids, or rebalancing card costs/effects / peace formulas (bots consume shipped config).
- Bot-only privileged information beyond what player-facing systems already expose for that org.
- Replacing `control` or `baselineCardPlay`; coexistence via scoring + shared infra only.
- Multi-country / allied wars (game remains two participants per war).
- Dedicated hunt-destroy mode (destroy is EV side effect only).
- Moving `decrease_enemy_control` into war features (stays control/baseline).
- Using `/implement-bot-feature` to sneak observation, sink, systems, or Assets changes past Planning Discipline.
- Teaching bots non-war non-unlock cards except as needed for the shared discard/draw value function and control coexistence.

## Tech Notes

### Standing anchors (post–feature-cut main)

- **Bot tick:** `Bot.ExecuteDecisionTick` (`src/Game.Bots/Bot.cs`) — country-card **acquisition** (draw/receive) can run every update while pending; **strategic** `feature.Tick` is gated **once per calendar day** via `_lastActedDate`. Owner requires changing this to a **configurable hour interval, default 4** (`botDecisionIntervalHours` or similar in `game_settings.json`). Exact key placement and whether acquisition stays every-update while Tick uses the interval: Ambiguities.
- **Features:** `IBotFeature.Tick(IBotObservation, IBotCommandSink, Random)`. Registry default: `baselineCardPlay`, `control` only (`BotFeatureRegistry.CreateDefault`). Default profile in `game_settings.json` enables **`control`** only. Discovery / `discoverAndControl` are gone.
- **Observation today:** gold, hands, draw choices (`CountryCardDrawChoices` with `TargetCountryId`, `IsControlUsable`, `RaisesControl`, `IsPlayable`), countries with control breakdown and character opinions. **`TargetCountryId` present.** Missing: wars, relations, progress, occupation, CountryScore/org_score, `IsDestroyed`, war-card kind flags, win-chance inputs.
- **Draw selection today:** `Bot.GetChoicePriority` prefers control-usable → raises-control → playable → else — **war-intent unaware**. Owner requires a shared intent/value scorer called from acquisition.
- **Discard today:** `IBotCommandSink.DiscardCountryCard` exists; **only** `ControlFeature.TryDiscardForBetterHand` calls it when hand full and no control play. Cost: `GameSettings.DiscardGoldCost` (50). Owner requires a **common shared** discard helper.
- **Commands:** `PlayCountryCard` / `PlayOrgCard` / `DiscardCountryCard` / draw+receive — war cards are ordinary country cards.
- **Win % helper (shipped):** `WarWinChanceEstimator.EstimateAttackerWinPercent` in `src/Game.Systems/WarWinChanceEstimator.cs` (recruits × damage / enemy durability; pending sell_arms/revenge bonuses). Natural home for declare/prosecute heuristics.
- **War domain:** `Wars` (`DeclareWar`, `ResolveWar`, progress, peace-by-chance). Peace control shifts from `game_settings.json`: winner `peaceWinnerControlIncreaseFraction` **0.5**, loser `peaceLoserControlDecreaseFraction` **1.0**. Destroy: `CountryDestroySystem.TryDestroyIfNoProvinces` → `IsDestroyed`, clear control.
- **Occupation:** `ProvinceOccupationSystem` exists in domain (seeded in `GameLogic`; used by battles / visual state). Observation does not yet expose occupation for bot EV — depth TBD (Ambiguities).
- **Score:** `OrgScoreCollector` — Σ `(control/100) × CountryScore`. Destroy / province transfer / occupation / control ± / gold are the war→score path inputs for profit EV.
- **Force flag:** `featureFlags.enableForceWarCards` defaults **false** in `game_settings.json`. `InitSystem.CreateCountryActionEntities` skips creating `force_war_win` / `force_war_loss` when false.
- **Gold reserve today:** `minGoldReserve` is a parameter on `BaselineCardPlayFeature`; `ControlFeature` currently ignores parameters / has no reserve check. War features should still **share the conceptual `minGoldReserve` policy with control** (introduce/align as needed at plan time).
- **Shipped action ids & gold (verify at plan time against `action_config.json`):**

| ActionId | UI | Gold | Notable gates | v1 war-feature ownership |
|----------|-----|------|---------------|--------------------------|
| `make_rival` | Make rival | 75 | diplomacy opinion ≥30; named target | `warUnlock` |
| `improve_military_advisor_opinion` | Improve mil opinion | 30 | control ≥10 | `warUnlock` |
| `improve_ruler_opinion` | Improve ruler opinion | 50 | control ≥20 | `warUnlock` |
| `declare_war` | Declare war | 150 | `targetRulerOrMilitaryOpinion` ≥50; rival; neither at war | `warDeclare` |
| `sell_arms` | Sell arms | 175 | mil opinion ≥80; peacetime OK | `warProsecute` |
| `declare_revenge_war` | Revenge | 125 | control ≥20; mil ≥25; warFree; revengeEligible | `warDeclare` (or separate — Ambiguities) |
| `force_war_win` | Ultimatum | 300 | control ≥10; mil ≥50; in war; own progress ≥50 | **Out of scope (v1)** — disabled by flag |
| `force_war_loss` | Surrender | 25 | control ≥20; mil ≥80; in war; own progress ≤0 | **Out of scope (v1)** — disabled by flag |
| `decrease_enemy_control` | Decrease enemy control | 250 | enemy control > mine | **control / baseline** (not war) |

- **Prior specs:** `Docs/Specs/26_07_16_14_bot-org-api/`, bot-feature-eval harness, war card specs under `Docs/Specs/26_07_29_*`, country-targeted relation cards `26_08_08_23_*`, country-destroy, card-draw rework. Bot feature docs: only `Docs/BotFeatures/control/` remains (`eval_config.json`: 10 seeds, `hoursPerTick` 24, `endDate` null, opponents `baselineCardPlay`).

### Proposed issue split (after this umbrella is approved)

| Child | Scope | Authority |
|-------|--------|-----------|
| **A** Observation extensions (war/relation/progress/occupation/score/destroy + estimator inputs) | `/specify`+`/plan` |
| **B** Decision interval (`botDecisionIntervalHours`) + shared discard + intent-aware draw scoring + cross-feature Δorg_score arbitration | `/specify`+`/plan` |
| **C** `warUnlock` (`make_rival`, opinion cards) | `/implement-bot-feature` after A–B |
| **D** `warDeclare` (`declare_war`, optionally `declare_revenge_war` + estimator; dual-side profit; target preference) | `/implement-bot-feature` after A–B |
| **E** `warProsecute` (**`sell_arms` only** for v1; dual-side profit; destroy as EV side effect; natural peace — **no force cards**) | `/implement-bot-feature` after A–B |
| **F** Eval configs under `Docs/BotFeatures/<id>/` (lengthened horizon; success = war plays + beat control-only twin) | `/implement-bot-feature` configs carve-out |

Parent #83 stays the umbrella until children are filed; do not implement the whole stack in one PR. Whether `botDecisionIntervalHours` stays inside **B** or a tiny separate issue is still open (Ambiguities).

### Stale vs prior revision of this spec

- `discoverAndControl` → **`control`**; discovery language removed.
- Force cards (`force_war_win` / `force_war_loss`) removed from v1 feature scope / acceptance — disabled by `enableForceWarCards: false`.
- In-war policy is **sell_arms → natural peace**, not force-resolve.
- Strategic cadence: **configurable hours (default 4)**, not once per calendar day.
- Profit EV must include occupation + enemy control decrease + gold; destroy is scored side effect; `decrease_enemy_control` stays with control/baseline.
- Scoring arbitration: global best Δorg_score including unlocks; shared discard + shared draw scorer locked.
- Success bar locked: war cards played in eval **and** beat control-only twin on org_score.
- `WarWinChanceEstimator` already exists; `TargetCountryId` already on observation; `ProvinceOccupationSystem` exists in domain.
- Action costs/ids remain shipped values (`sell_arms` 175, `declare_war` 150, etc.).

## Ambiguities

- [NEEDS CLARIFICATION: Eval opponents — recommend **primary gate = control-only twin** (same seeds, candidate enables war features + control; twin enables only `control`). Tradeoff: directly measures the success bar “(b) beat control-only twin,” and isolates war feature value vs the shipped default profile. Optional secondary: passive orgs / `baselineCardPlay` to check that war bots still function against weaker opponents (cheaper signal, but does not prove war beats control). Mirror war bots are a later stress test, not the first-ship gate. Confirm primary = control-only twin, and whether secondary passive/baseline runs are required for merge?]
- [NEEDS CLARIFICATION: Gold policy nuance (force carve-out gone) — lock **shared `minGoldReserve` with control**. Confirm remaining rule: (i) **strict shared reserve only** (never spend below reserve for declare/sell_arms), or (ii) **allow dipping below reserve** only for clearly high-EV expensive war plays (`declare_war` / `sell_arms`) when expected Δorg_score clearly compensates? Spec’s proposed default is (ii).]
- [NEEDS CLARIFICATION: `botDecisionIntervalHours` — confirm default **4**, setting name, and location (`game_settings.json` root vs nested under `botFeatures` / shared bot params). Also confirm: country-card **acquisition** (draw/receive) still runs every update while pending, while only strategic `feature.Tick` uses the hour interval?]
- [NEEDS CLARIFICATION: Eval horizon — with 4h strategic cadence and natural-peace resolution (no force), propose war evals use a **longer horizon and more seeds** than control’s current `eval_config.json` (10 seeds, `hoursPerTick` 24, `endDate` null ≈ scenario default). Concrete proposal for confirm: **`seedCount: 20`**, explicit **`endDate` ~10 in-game years** from start (e.g. startYear 1880 → end ~1890-01-01), keep **`hoursPerTick: 24`** as simulation step. Confirm or substitute numbers.]
- [NEEDS CLARIFICATION: Observation occupation depth — for v1 profit EV, expose **full occupied-province counts per country for both war sides**, or a cheaper **summary occupied-score proxy** (e.g. aggregate occupied CountryScore fraction) sufficient for declare/prosecute estimates?]
- [NEEDS CLARIFICATION: Child-issue split approval — confirm A observation, B infra (interval + arbitration + draw + discard), C `warUnlock`, D `warDeclare`, E `warProsecute` (`sell_arms` only), F eval — and whether `botDecisionIntervalHours` lives inside **B** or as a tiny separate issue?]
- [NEEDS CLARIFICATION: Revenge packaging — does `declare_revenge_war` stay inside **`warDeclare`** (same profit + estimator gates), or get a separate child feature/issue?]
- [NEEDS CLARIFICATION: Prefer-war-where-enemy-control-higher rule — implement as a **hard filter** (e.g. skip declare when bot control ≥ enemy on the target country, or on both participants) or as a **soft scoring bias** (still allow declare if net Δorg_score wins)?]
