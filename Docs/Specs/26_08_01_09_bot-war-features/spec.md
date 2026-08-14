# Spec: Bot War Features

## Feature Intent

As a developer building AI-controlled organizations, I want bots to declare wars and prosecute them toward **own `org_score` profit** — using shipped war/diplomacy cards (`make_rival`, opinion unlocks, `declare_war`, `sell_arms`, `declare_revenge_war`) and **natural peace** outcomes (province ownership / occupation transfers, gold spoils, winner/loser control shifts, possible country destroy) — so that bots treat war as a scored lever against the best-scoring rival rather than ignoring the war card suite.

**v1 resolution path:** bots raise win chance via `sell_arms`, then **wait for natural peace** — they do **not** play Ultimatum / Surrender (`force_war_win` / `force_war_loss`). Those cards are currently disabled (`featureFlags.enableForceWarCards: false`); InitSystem skips creating them when the flag is false. Force-resolve may return later if the flag is turned on — **out of scope for this umbrella’s first ship**.

**Profit model:** expected Δorg_score must include **enemy control decrease, occupation / province-transfer EV, and gold change** — not only `(control/100)×CountryScore` on the preferred side.

**Cadence:** every **`botDecisionIntervalHours` (default 4)** in-game hours the bot may **draw and/or discard (0+ cards), then play at most one strategic card** (play may land on the same tick or a later frame if logic flow requires). This replaces once-per-calendar-day strategic ticks and the prior “acquisition every update / Tick on interval” split.

This is an **umbrella discussion / decomposition** issue (#83). War gameplay dependencies (#69, #70, #72, #73, #74, #76) are closed. Post–feature-cut main also shipped card-draw 1-of-3 (#154), country-targeted `make_rival` (#158), country destroy (#142), and replaced `discoverAndControl` with **`control`**. No `plan.md` and no implementation belong in this specify step — only product behaviour, resolved decisions, and the phased issue split. After this umbrella is approved, child issues are filed and `/plan` starts on **child A**.

## Resolved Decisions (owner 2026-08-02 + 2026-08-09 + 2026-08-12)

| Topic | Decision |
|-------|----------|
| Packaging | **Separated features**; `control` and war features compete via **scoring** (war sometimes wins, sometimes control does) |
| Objective | Maximize own **`org_score`**; prefer outcomes that hurt the **best-scoring rival org** |
| Scoring arbitration | On the same decision cycle, pick the **global best estimated Δorg_score** among `control` + war + unlock candidates; unlocks sit in the **same scoring pool** (no separate pre-pass); **at most one** strategic card play per interval |
| Declare threshold | Prefer actions with best expected **Δorg_score**; prefer high win % via shared **`WarWinChanceEstimator`** |
| Dual-side profit | Declare / prosecute require **net Δorg_score > 0** after both sides’ peace control shifts (+50% winner / −100% loser), province/occupation transfers, destroy EV, and gold |
| Target preference | Prefer wars against countries where **enemy control is higher** OR **no bot control** is present — implement as a **soft scoring bias** (still allow declare if net Δorg_score wins) |
| Profit EV inputs | Must include **enemy control decrease, occupation, and gold change** (not control×CountryScore alone) |
| Occupation depth | Observation exposes **full occupied-province counts per country** for both war sides (v1) |
| Unlocking | **In scope:** `make_rival` when creating a rival to war makes sense; also play **military advisor / ruler opinion** cards to unlock related war cards |
| In-war policy | **`sell_arms` first** (prosecute via arms); then **wait for natural peace**. No force-resolve in v1 |
| Force cards | **IGNORE** `force_war_win` / `force_war_loss` for this umbrella’s first ship (`enableForceWarCards` defaults false; cards absent from world when disabled) |
| Revenge | Only when still **profitable**; packaged **inside `warDeclare`** with a **soft additive priority bonus** vs ordinary `declare_war`: arbitration score uses **+20% of that revenge candidate’s estimated Δorg_score** (i.e. `score = Δ × 1.20` for revenge; ordinary declare uses raw Δ). Magnitude chosen here; tunable at child-D `/plan` if evals need it |
| Country destroy | Scored **side effect** in the profit model (not a dedicated hunt-destroy mode) |
| `decrease_enemy_control` | Stays with **control / baseline**, not war features |
| Config economics | Follow **shipped** `action_config.json` / `effect_config.json` (do not restore stale issue-text numbers) |
| Partial visibility | May prosecute with only one side known enough to act under card gates |
| Issue boundary | **Split into separate issues** after this decompose/specify — child split **A–F approved** |
| Naming | `warUnlock`, `warDeclare`, `warProsecute` (camelCase like `control`) |
| Shared discard | **Common shared discard helper** (orchestrator / shared util) — not only inside `ControlFeature`; discards per interval are **unbounded** (0+, until a suitable play exists or gold/hand constraints stop further discards) |
| Draw / intent | **Shared intent/value scorer** for 1-of-3 draw, called from Bot acquisition (replaces / extends control-only `GetChoicePriority`) |
| Decision cadence | Every **`botDecisionIntervalHours` (default 4)** the bot may **draw and/or discard (0+ cards), then play at most one** strategic card (same tick OK when world state already allows; defer to a later tick/frame only when command/apply ordering requires it). Lives inside **child B**. Setting: root `game_settings.json` key `botDecisionIntervalHours: 4` |
| Gold policy | Share **`minGoldReserve` with control**; **allow dipping** below reserve only for clearly high-EV expensive war plays (`declare_war` / `sell_arms`) when expected Δorg_score compensates |
| Eval opponents | **Primary merge gate = control-only twin** (same seeds; candidate = war features + `control`; twin = `control` only). Secondary passive / `baselineCardPlay` **not** required for merge. Mirror war bots later |
| Eval horizon | Explicit **`endDate` required** (null → ConsoleRunner `StartYear + 5`, not a long scenario). Locked: **`endDate` `1920-01-01`**, **`hoursPerTick: 4`** (matches decision interval), **`seedCount: 20`** |
| Eval success bar | First ship requires **both**: (a) sometimes plays war cards in eval **and** (b) beats control-only twin on `org_score` |

No open product ambiguities remain for this umbrella; remaining work is owner **approval of this specify**, then filing child issues and `/plan` on child A.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`.

### Umbrella behaviour (once child issues ship)

- A multi-org session where the candidate org can afford war-related plays and holds control skew favoring one side of a profitable rivalry (enemy control higher on the preferred target, or no bot control there).
  - Over subsequent decision cycles (default **every 4 in-game hours**: draw/discard then at most one play), the bot unlocks rivalry/opinion when needed, declares when expected Δorg_score and win % justify it, sells arms on the preferred side, and **waits for natural peace** (no Ultimatum/Surrender) => own `org_score` improves, and evals satisfy **both** success bars: war cards are sometimes played **and** the candidate beats a control-only twin on `org_score`.
- Edge: war cards are playable but expected Δorg_score is non-positive after dual-side accounting (control shifts, occupation, destroy EV, gold).
  - The bot skips those plays => no “play war because IsPlayable” behaviour from war features.
- Edge: hand is full and no feature finds a suitable play.
  - A **shared** discard path can discard low-value cards (paying `discardGoldCost`) unbounded within the interval until a suitable play appears or gold/hand constraints stop further discards => discard is not owned exclusively by `control`.
- Edge: a 1-of-3 country-card draw offer is pending on a decision cycle.
  - The bot picks the choice that best fits active intentions via the **shared intent/value scorer** (war unlock/declare/prosecute vs control), not control-only priority => war-capable profiles can acquire war cards on purpose.
- Edge: bot holds meaningful control on **both** war participants.
  - Declare / prosecute decisions use a net profit model that includes peace control shifts on **both** countries, occupation/province moves, destroy EV, and gold => dual-side wars are not treated as single-side skew alone.
- Edge: natural peace can empty a country's provinces.
  - Country destroy (`IsDestroyed`, control cleared, relations removed) is considered in profit reasoning when it hurts the lead rival more than it hurts the bot => destroy is an allowed scored side effect, not a hunt mode and not ignored.
- Edge: `featureFlags.enableForceWarCards` is false (shipped default) so `force_war_win` / `force_war_loss` entities are not created.
  - War features never depend on those cards for v1 behaviour => first ship works with force cards absent from the world.
- Edge: both `declare_revenge_war` and ordinary `declare_war` are score-positive on the same interval.
  - Arbitration ranks revenge with **+20% soft weight** on its estimated Δorg_score vs raw Δ for ordinary declare => rematches are preferred when still profitable, without a hard “always revenge” rule.

### Child issue A — Bot observation extensions (prerequisite; not `/implement-bot-feature`)

- Seat-useful war / relation / score / occupation signals needed for score-aware war are missing from `IBotObservation` today (wars, rivals, progress, occupation, CountryScore / org_score contributions, destroyed flag, combat inputs for win %).
  - Observation is extended so a bot can, for countries it can act on, know war participation + opponent when knowable, own-side progress, rival targets, score-facing signals, **full per-country occupied-province counts** for both war sides, and destroyed state => war features can estimate Δorg_score (including occupation + enemy control decrease + gold) and call `WarWinChanceEstimator` without privileged foresight.
  - `TargetCountryId` is **already** on `BotCardView` / draw choices for relation and revenge cards => do not re-specify that field; only fill remaining gaps.
- Any `IBotObservation` / views / `BotObservation.Build` change requires its own `/specify` + `/plan` => Constitution Planning Discipline is honored.

### Child issue B — Bot infrastructure (interval, scoring arbitration, shared discard, intent-aware draw)

- Strategic play is still gated once per calendar day via `_lastActedDate` in `Bot.cs`, and acquisition can run every update while pending.
  - Cadence becomes: every **`botDecisionIntervalHours` (default 4)** the bot may **draw and/or discard (0+ unbounded until useful or blocked), then play at most one** strategic card (same tick OK when state allows; later frame only when ordering requires) => bots are not limited to one strategic play per calendar day, and draw/discard are part of the same interval cycle (not “acquisition every update”).
- Multiple features (`control`, `warUnlock`, `warDeclare`, `warProsecute`, …) can each propose a play on the same decision cycle.
  - The bot selects among proposals by **global best estimated Δorg_score** (unlocks included in the same pool; revenge uses +20% soft weight) rather than fixed profile order alone => control and war coexist by scoring; **at most one** play wins the interval.
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

- Bot has playable `declare_war` and/or `declare_revenge_war` with a named rival, can afford it under gold policy, expected net Δorg_score is positive after dual-side accounting (including occupation + enemy control decrease + gold), and `WarWinChanceEstimator` win % is acceptable under the chosen threshold; soft target preference favors enemy-higher-control / no-bot-control countries.
  - Bot plays that card => war starts (or rematch) with shipped costs/effects.
  - Multiple declare instances => picks highest expected Δorg_score (preferring higher win % when deltas are close), not first-in-hand.
  - When both `declare_revenge_war` and ordinary `declare_war` are profitable candidates, revenge gets a **soft +20% Δorg_score weight** over ordinary declare => rematches are preferred when still score-positive.
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
  - `targetActions` lists the action ids that feature owns (`sell_arms` for prosecute — **not** force cards); parameters cover reserves / profit / win-% thresholds.
  - Horizon: **`endDate` `1920-01-01`**, **`hoursPerTick: 4`**, **`seedCount: 20`**; decision cadence follows `botDecisionIntervalHours` (default 4). Note: ~40 in-game years at 4h steps is much heavier than control’s 5y/24h/10-seed defaults — child-F `/plan` should size `timeoutSeconds` / harness budget accordingly, without changing these locked knobs unless evals prove infeasible.
  - Gate metric remains paired-seed **`org_score`**.
  - Success requires **(a)** war-related actions appear in eval play traces **and** **(b)** candidate beats **control-only twin** on `org_score` (primary merge gate; secondary opponents not required).

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
- Secondary eval opponents (passive / `baselineCardPlay` / mirror war) as merge gates.
- Changing locked eval knobs (`hoursPerTick: 4`, `seedCount: 20`, `endDate` 1920) without a new owner decision.

## Tech Notes

### Standing anchors (post–feature-cut main)

- **Bot tick:** `Bot.ExecuteDecisionTick` (`src/Game.Bots/Bot.cs`) — country-card **acquisition** (draw/receive) can currently run every update while pending; **strategic** `feature.Tick` is gated **once per calendar day** via `_lastActedDate`. Owner requires: every **`botDecisionIntervalHours` (default 4)** the bot may **draw and/or discard (0+ unbounded), then play at most one** card (same tick OK when state allows; later frame only when ordering requires). Root key in `game_settings.json`.
- **Features:** `IBotFeature.Tick(IBotObservation, IBotCommandSink, Random)`. Registry default: `baselineCardPlay`, `control` only (`BotFeatureRegistry.CreateDefault`). Default profile in `game_settings.json` enables **`control`** only. Discovery / `discoverAndControl` are gone.
- **Observation today:** gold, hands, draw choices (`CountryCardDrawChoices` with `TargetCountryId`, `IsControlUsable`, `RaisesControl`, `IsPlayable`), countries with control breakdown and character opinions. **`TargetCountryId` present.** Missing: wars, relations, progress, occupation counts, CountryScore/org_score, `IsDestroyed`, war-card kind flags, win-chance inputs. `ControlWarSnapshot` is built inside `BotObservation.Build` for playability only — not exposed.
- **Draw selection today:** `Bot.GetChoicePriority` prefers control-usable → raises-control → playable → else — **war-intent unaware**. Owner requires a shared intent/value scorer called from acquisition.
- **Discard today:** `IBotCommandSink.DiscardCountryCard` exists; **only** `ControlFeature.TryDiscardForBetterHand` calls it when hand full and no control play. Cost: `GameSettings.DiscardGoldCost` (50). Owner requires a **common shared** discard helper on the 4h cycle with unbounded discards per interval until useful or blocked.
- **Commands:** `PlayCountryCard` / `PlayOrgCard` / `DiscardCountryCard` / draw+receive — war cards are ordinary country cards.
- **Win % helper (shipped):** `WarWinChanceEstimator.EstimateAttackerWinPercent` in `src/Game.Systems/WarWinChanceEstimator.cs` (recruits × damage / enemy durability; pending sell_arms/revenge bonuses). Natural home for declare/prosecute heuristics.
- **War domain:** `Wars` (`DeclareWar`, `ResolveWar`, progress, peace-by-chance). Peace control shifts from `game_settings.json`: winner `peaceWinnerControlIncreaseFraction` **0.5**, loser `peaceLoserControlDecreaseFraction` **1.0**. Destroy: `CountryDestroySystem.TryDestroyIfNoProvinces` → `IsDestroyed`, clear control. Province transfer band **50–80%** of eligible occupied loser provinces.
- **Occupation:** `ProvinceOccupationSystem` exists in domain (seeded in `GameLogic`; used by battles / visual state). Observation must expose **full per-country occupied-province counts** for both war sides for v1 EV.
- **Score:** `OrgScoreCollector` — Σ `(control/100) × CountryScore`. Destroy / province transfer / occupation / control ± / gold are the war→score path inputs for profit EV.
- **Force flag:** `featureFlags.enableForceWarCards` defaults **false** in `game_settings.json`. `InitSystem.CreateCountryActionEntities` skips creating `force_war_win` / `force_war_loss` when false.
- **Gold reserve today:** `minGoldReserve` is a parameter on `BaselineCardPlayFeature`; `ControlFeature` currently ignores parameters / has no reserve check. War features should still **share the conceptual `minGoldReserve` policy with control** (introduce/align as needed at plan time), with high-EV dip allowed for expensive war plays.
- **Eval endDate fact:** in bot-feature eval, `endDate: null` does **not** mean “run forever / scenario default” — ConsoleRunner substitutes **`StartYear + 5`** (1880 → `1885-01-01`). Longer war evals **must** set an explicit endDate; owner chose **1920**.
- **Eval cost note:** `hoursPerTick: 4` × ~40 years × `seedCount: 20` is roughly an order of magnitude heavier than control’s defaults; child F must raise harness timeouts / budgets rather than silently thinning the locked knobs.
- **Shipped action ids & gold (verify at plan time against `action_config.json`):**

| ActionId | UI | Gold | Notable gates | v1 war-feature ownership |
|----------|-----|------|---------------|--------------------------|
| `make_rival` | Make rival | 75 | diplomacy opinion ≥30; named target | `warUnlock` |
| `improve_military_advisor_opinion` | Improve mil opinion | 30 | control ≥10 | `warUnlock` |
| `improve_ruler_opinion` | Improve ruler opinion | 50 | control ≥20 | `warUnlock` |
| `declare_war` | Declare war | 150 | `targetRulerOrMilitaryOpinion` ≥50; rival; neither at war | `warDeclare` |
| `sell_arms` | Sell arms | 175 | mil opinion ≥80; peacetime OK | `warProsecute` |
| `declare_revenge_war` | Revenge | 125 | control ≥20; mil ≥25; warFree; revengeEligible | `warDeclare` (+20% soft Δ weight vs ordinary declare) |
| `force_war_win` | Ultimatum | 300 | control ≥10; mil ≥50; in war; own progress ≥50 | **Out of scope (v1)** — disabled by flag |
| `force_war_loss` | Surrender | 25 | control ≥20; mil ≥80; in war; own progress ≤0 | **Out of scope (v1)** — disabled by flag |
| `decrease_enemy_control` | Decrease enemy control | 250 | enemy control > mine | **control / baseline** (not war) |

- **Prior specs:** `Docs/Specs/26_07_16_14_bot-org-api/`, bot-feature-eval harness, war card specs under `Docs/Specs/26_07_29_*`, country-targeted relation cards `26_08_08_23_*`, country-destroy, card-draw rework. Bot feature docs: only `Docs/BotFeatures/control/` remains (`eval_config.json`: 10 seeds, `hoursPerTick` 24, `endDate` null, opponents `baselineCardPlay`).

### Approved issue split (after this umbrella is approved)

| Child | Scope | Authority |
|-------|--------|-----------|
| **A** Observation extensions (war/relation/progress/**full occupation counts**/score/destroy + estimator inputs) | `/specify`+`/plan` |
| **B** Decision interval (`botDecisionIntervalHours` **inside B**) + shared discard (unbounded per interval) + intent-aware draw scoring + cross-feature Δorg_score arbitration (**at most one** play; revenge +20% soft weight); 4h cycle = draw/discard then play | `/specify`+`/plan` |
| **C** `warUnlock` (`make_rival`, opinion cards) | `/implement-bot-feature` after A–B |
| **D** `warDeclare` (`declare_war` + `declare_revenge_war` with **+20% soft priority weight**; dual-side profit; soft target bias) | `/implement-bot-feature` after A–B |
| **E** `warProsecute` (**`sell_arms` only** for v1; dual-side profit; destroy as EV side effect; natural peace — **no force cards**) | `/implement-bot-feature` after A–B |
| **F** Eval configs under `Docs/BotFeatures/<id>/` (`endDate` 1920-01-01; `hoursPerTick: 4`; `seedCount: 20`; success = war plays + beat control-only twin) | `/implement-bot-feature` configs carve-out |

Parent #83 stays the umbrella until children are filed; do not implement the whole stack in one PR.

**Child plans (2026-08-13):** written under `Docs/Specs/26_08_13_09_bot-war-{observation,infra,unlock,declare,prosecute,eval}/` (spec+plan each). Cross-plan locks: shared `WarPeaceOrgScoreEstimator` owned by **D**; `TroopsDamageBonusPercent` on Child A; B `BotPlayProposal.TargetBiasMultiplier`; implement order **A → B → D → C∥E → F**.

Next step after plan approval: file GitHub children A–F (or reuse these folders as the child issues’ specs), then `/implement` on **child A** first.

### Stale vs prior revision of this spec

- Locked eval sim step: **`hoursPerTick: 4`**, **`seedCount: 20`**, **`endDate` `1920-01-01`** (owner chose 4h over cheaper 24h).
- Locked decision-cycle details: **at most one** strategic play per interval; **unbounded** discards until useful or blocked; **same-tick play OK** when state allows.
- Locked revenge priority: **soft +20%** of revenge candidate’s estimated Δorg_score (agent-chosen magnitude).
- Ambiguities section removed — no open product clarifications remain for this umbrella.
- Prior locks unchanged: control-only twin gate, gold dip policy, 4h draw/discard-then-play, full occupation counts, A–F split, soft enemy-control bias, force cards out of v1, success bar (a)+(b).
