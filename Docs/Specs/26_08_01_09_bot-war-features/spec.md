# Spec: Bot War Features

## Feature Intent

As a developer building AI-controlled organizations, I want bots to declare wars and prosecute them toward org-score profit — using the existing war cards (`declare_war`, `sell_arms`, `ultimatum`, `surrender`, `revenge`) and peace-resolution outcomes (province ownership, gold spoils, winner/loser control shifts) — so that bots treat war as a scored lever (control × country population score for self, and relative harm to rivals) rather than ignoring the war card suite that players already have.

This is a **discussion / decomposition first** issue (#83). Dependencies #69 (war core), #70 (`declare_war`), #72 (`sell_arms`), #73 (`ultimatum`/`surrender`), #74 (peace resolution), and #76 (`revenge`) are closed and implemented. No plan.md and no implementation belong in this step — only product behaviour, phased change boundaries, and every clarification the owner must answer before planning.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

Acceptance criteria below are **provisional defaults pending Ambiguities**. They describe desired bot/org behaviour in product language once clarifications resolve; they do not invent new war mechanics beyond the shipped cards and peace resolution.

### Phase 1 — Observation & card classification (prerequisite surface; not a pure bot feature)

- Seat-visible war / relation / score signals that a human player can already infer from discovered countries, country panels, and playable war cards are missing from the bot observation today.
  - Proposed default (pending clarification): the observation is extended so a bot can, for each discovered country it can see, know whether that country is at war, who the opponent is (when both sides are discovered / otherwise seat-visible), that country's own-side war progress, and which rivals it currently has => bots can reason about declare/prosecute/resolve without privileged foresight.
  - Proposed default: each in-hand country card exposes its war-card kind (or equivalent flags) and, for relation-targeted instances (`declare_war`, and any other `RelationCardTarget` cards), the named `TargetCountryId` => bots can distinguish "declare war on Spain" from "declare war on Portugal" and from non-war cards without parsing action ids alone.
  - Proposed default: enough score-facing signal is exposed to estimate war profit under player-visible parity (e.g. per-discovered-country `CountryScore` / population contribution and/or the bot's own current org score contribution breakdown) => bots can prefer wars that raise their score (and optionally hurt rivals) rather than declaring blindly.
  - A war or relation involves only undiscovered countries for this org => those facts remain hidden, matching player seat visibility; no bot-only foresight of undiscovered wars, rivalries, occupation, or scores.
- Phase 1 is explicitly **not** shippable via `/implement-bot-feature` alone.
  - Any change to `IBotObservation` / views / `BotObservation.Build` (and any supporting query helpers needed for parity) requires its own `/specify` + `/plan` before feature logic => Constitution Planning Discipline is honored.

### Phase 2 — War declaration / revenge behaviour

- Proposed default (pending clarification): a bot has a playable `declare_war` instance naming a rival, can afford it under its gold-reserve policy, and a provisional profit heuristic says the war is "worth it" (e.g. the bot holds meaningful control on the intended winner side and/or expects peace outcomes to raise own score vs leave it flat/worse).
  - The bot plays that `declare_war` for the naming country => a war starts between that country and the named rival, same as a player playing the card (100 gold, rivals, ruler/military ≥50 opinion, neither side already at war).
  - Multiple playable declare instances exist => it picks the one the profit heuristic ranks highest (provisional: highest expected own-score delta), not arbitrary first-in-hand order, unless the owner prefers a simpler first-playable policy.
- Proposed default: a bot has a playable `revenge` instance (eligible after a prior loss involving that pairing per `revengeEligible`), can afford it, and the same profit heuristic still says redeclaring is worthwhile.
  - The bot plays `revenge` => war is redeclared with the card's existing damage/durability attacker bonuses (50 gold, control/opinion/`warFree`/`revengeEligible` gates unchanged).
  - Profit heuristic says revenge is no longer worthwhile => the bot does **not** play revenge solely because it is eligible (provisional; owner may override to "always avenge").
- Prerequisites for declare (`make_rival`, opinion-raising advisor cards) are **not** assumed solved by this feature under the provisional default.
  - Rivalry / opinion unlock cards are absent or unplayable => this feature does nothing to unlock declare; it only acts when `declare_war` / `revenge` is already `IsPlayable` (provisional — see Ambiguities on whether unlocking is in scope).

### Phase 3 — War prosecution / resolution behaviour

- Proposed default: a bot holds a playable `sell_arms` on a country in an active war where the bot wants that side to do better (or wants the card's economic effect), and gold policy allows it.
  - The bot may play `sell_arms` => the country's temporary damage bonus rises per the shipped card; gold moves per the **shipped** `action_config.json` / effects (note: current shipped row costs 200 gold and applies the damage-bonus effect only — the original issue text's "+300 gold grant / zero cost" may have drifted; behaviour must follow shipped config unless the owner rebalances first).
- Proposed default: a bot holds a playable `ultimatum` on a country whose win would raise the bot's expected org score (typically: bot has more control on that country than on the opponent, so winner +5% control boost and province/score gains land on the preferred side).
  - The bot plays `ultimatum` when progress/opinion/control gates already make it playable and forcing the win is better than waiting for natural peace => selected country wins immediately; peace resolution applies province transfer from occupied loser provinces, gold spoils, winner orgs +5% control, loser orgs −10% control.
- Proposed default: a bot holds a playable `surrender` on a country whose **loss** would raise the bot's expected org score (e.g. bot has substantially more control on the opposing side).
  - The bot plays `surrender` in that intentional-loss case => selected country loses; peace resolution favors the opponent side the bot prefers.
  - Surrender would hurt the bot's score more than helping => the bot does not play surrender merely because it is playable.
- Proposed default policy between prolonging vs forcing resolution.
  - `sell_arms` is preferred while the preferred side's progress is still climbing toward a favorable band and gold/opinion allow it; `ultimatum`/`surrender` are preferred once playable and the expected score delta of forcing peace exceeds the value of waiting / further arms sales (all thresholds tunable; exact cutovers are owner decisions).

### Phase 4 — Feature arbitration / coexistence with `discoverAndControl`

- Proposed default: war features and `discoverAndControl` coexist on one org profile, ordered so war takes priority when a high-value war play is available, otherwise discovery/control proceeds.
  - On a game-day tick, a profitable war declare/prosecute/resolve play is available under the feature's rules and gold reserve => the war feature emits that play and other features that tick later see the sink already used for that day's war intent (exact "at most one play per day vs multiple features emitting" interaction must match `Bot.ExecuteDecisionTick`'s once-per-day loop and sink duplicate rules — see Tech Notes).
  - No war play qualifies => `discoverAndControl` (and/or `baselineCardPlay`) may act as today.
  - Gold is scarce relative to declare (100) / sell_arms (shipped 200) / ultimatum (300) / surrender (500) / revenge (50) => a shared or war-specific `minGoldReserve` (provisional parameter) prevents war spends from starving discovery/control entirely.

### Phase 5 — Eval config

- Proposed default: once behaviour features exist, each registered war-related feature id gets a `Docs/BotFeatures/<featureId>/` eval package.
  - `eval_config.json` lists `targetActions` covering the war action ids the feature is meant to play (`declare_war`, `sell_arms`, `ultimatum`, `surrender`, `revenge` as applicable) and parameters for reserves / profit thresholds / priority weights.
  - Eval duration accounts for war timescales (wars span months; default ~5 game years may be insufficient — provisional: lengthen `endDate` / seed settings so declare→progress→peace can complete often enough for score deltas to appear).
  - Paired-seed gate continues to use org score (`org_score` / the OrgScore formula: Σ controlFraction × CountryScore) as the comparison metric.

### Cross-cutting happy path (once phases 1–4 are decided)

- A multi-org headless (or Unity bot) session where the candidate org has discovered two rival countries, holds control skew favoring one side, and has a playable `declare_war`.
  - Over subsequent game-days the bot declares when profitable, optionally sells arms on the preferred side, and forces resolution with ultimatum or surrender when that side's win/loss is score-positive => end org score is higher than an otherwise identical bot that never plays war cards (eval gate subject to noise/epsilon).
- Edge: war cards are playable but expected score delta is non-positive under the chosen objective.
  - The bot skips those plays => no "play war cards because they are IsPlayable" behaviour from the war feature(s).
- Edge: only one side of a war is discovered.
  - Proposed default: the bot may still play cards on the discovered side using seat-visible progress/control there, but must not invent opponent control/score it cannot see.

## Tech Notes

Maps each product-facing behaviour above to its concrete implementation — specific files, classes, methods, commands, state paths. Structured as the same phased decomposition the owner can approve or reject piece-by-piece.

### Standing architecture anchors

- **Bot tick contract:** `IBotFeature.Tick(IBotObservation, IBotCommandSink, Random)` in `src/Game.Bots/IBotFeature.cs`. `Bot.ExecuteDecisionTick` (`src/Game.Bots/Bot.cs`) builds one `BotObservation` per org per **game-day** and runs enabled features in profile order. Features today: `baselineCardPlay`, `discoverAndControl` (`BotFeatureRegistry.CreateDefault`).
- **Observation today:** `IBotObservation` / `BotObservation` / `BotViews` expose gold, org hand, discovered countries, per-country control breakdown, country hands with `IsPlayable` / `DiscoversCountry` / `RaisesControl`, and character opinions. **Missing today:** war state, relations/rivals, war progress, occupation, CountryScore/population, org score breakdown, `TargetCountryId` on cards, war-card kind flags.
- **Commands:** `IBotCommandSink.PlayCountryCard` / `PlayOrgCard` only — war cards are ordinary country cards; no new sink methods are required for play itself.
- **War domain:** `src/Game.Systems/Wars.cs` (`IsInWar`, `DeclareWar`, `ResolveWar`, `GetOwnWarProgress`, …). `ResolveWar` already runs peace outcomes (province transfer, occupation clear, gold spoils, control ±, revenge eligibility via `RevengeEligibilityQuery.OnWarResolved`).
- **Score:** org score formula is Σ over countries of `(org control in country / 100) × CountryScore` — implemented as `OrgScoreCollector` (`src/Game.Systems/OrgScoreCollector.cs`) writing the `org_score` resource (historical query name `OrgScore.GetScore` from `Docs/Specs/26_07_16_09_org-scoring/`). Peace resolution changes province ownership → CountryScore, control shares, and gold; that is the primary war→score pathway.
- **War action ids** (shipped in `Assets/Configs/action_config.json`): `declare_war` (100g), `sell_arms` (200g cost in current file; damage bonus effect), `ultimatum` (300g), `surrender` (500g), `revenge` (50g, gated by `revengeEligible` among others). Prerequisite diplomacy: `make_rival` (50g) and opinion cards — separate action ids.
- **Authority boundary:** `.claude/commands/implement-bot-feature.md` + Constitution bot-feature carve-out — pure `IBotFeature` + `Docs/BotFeatures/<id>/` + registry only. **Any** `IBotObservation` / sink / systems / Assets / eval-harness change needs `/specify`+`/plan`. Phase 1 is almost certainly outside the carve-out; phases 2–3 may use the carve-out **only after** observation is sufficient (or if the owner accepts a weaker IsPlayable-only heuristic — Ambiguities).
- **Prior specs:** `Docs/Specs/26_07_16_14_bot-org-api/`, `Docs/Specs/26_07_16_14_bot-feature-eval-harness/`, `Docs/Specs/26_07_25_06_war-mechanics-core/`, war card specs under `Docs/Specs/26_07_29_*` (`declare-war`, `sell-arms`, `ultimatum-surrender`, `peace-resolution`, `revenge-card`).

### Phase 1 — Observation & card classification extensions

- **Expose seat-visible wars for discovered countries:**
  - Extend `BotCountryView` / `IBotObservation` (and `BotObservation.Build`) to surface at least: whether the country is in a war; opponent country id when knowable under discovery filters; own-side war progress via `Wars.GetOwnWarProgress`; optionally attacker/defender kind.
  - Source of truth: `War` / `WarParticipant` / `WarProgress` components + `Wars` helpers — not a second war model.
- **Expose rivals (and optionally friends) for discovered countries:**
  - Read existing country-relation state the player panel already shows for discovered countries; do not invent private relation foresight.
- **Expose per-card targeting / war-card classification:**
  - Extend `BotCardView` with `TargetCountryId` (from `RelationCardTarget` when present — required for multi-rival `declare_war` instances) and either explicit flags (`IsDeclareWar`, `IsSellArms`, …) or a small war-card kind enum derived from `ActionId` / effect classification (same spirit as today's `DiscoversCountry` / `RaisesControl` via `ClassifyCard`).
- **Expose score-facing signals (player-visible parity):**
  - Candidates: per-discovered-country `CountryScore` (and/or population), and/or the bot's own `org_score` plus a per-country contribution breakdown computable from control × CountryScore. Do **not** expose other orgs' gold or undiscovered-country scores.
- **Tests:** information-hiding tests in `src/Game.Tests` (extend `BotObservationTests` patterns from bot-org-api) asserting undiscovered wars/relations/scores stay hidden and discovered seat-visible facts appear deterministically ordered.
- **Plan gate:** this phase needs its own approved plan (or a split prerequisite issue) before phase 2–3 feature code. It is **not** `/implement-bot-feature` authority.

### Phase 2 — War declaration / revenge feature(s)

- **Feature shape (pending Ambiguities #1):** either one `warProfit` feature covering declare+prosecute, or split `declareWar` / `prosecuteWar` (names illustrative). Register in `BotFeatureRegistry.CreateDefault`.
- **Decision inputs:** observation war/rival/score fields from phase 1; playable `declare_war` / `revenge` cards with `TargetCountryId`; gold + `minGoldReserve`-style parameter; control shares on attacker vs defender (and CountryScore weights) for the profit heuristic.
- **Emit:** `sink.PlayCountryCard(actionId, countryId)` only when `IsPlayable` is true — never bypass conditions; invalid plays still discard the card like a player mistake.
- **Out of this phase unless owner expands scope:** playing `make_rival` / opinion cards to unlock declare (Ambiguities #4).
- **Unit tests:** synthetic `IBotObservation` fixtures asserting declare/revenge play vs skip under profit/gold gates (`src/Game.Tests`).

### Phase 3 — War prosecution / resolution feature(s)

- **Same feature or sibling feature** as phase 2 (owner choice): play `sell_arms`, `ultimatum`, `surrender` when playable and score heuristic agrees.
- **Ultimatum vs surrender:** compare bot control (× CountryScore if available) on selected country vs opponent; prefer forcing the side where the bot's post-peace expected score is higher (winner +5% / loser −10% and province/score shifts from `Wars.ResolveWar` / peace resolution).
- **sell_arms vs force resolve:** parameterize preference (e.g. progress thresholds, expected delta of waiting vs paying 300/500 to force). Follow **shipped** sell_arms economics (current config: 200g cost + damage bonus; verify against `action_config.json` / `effect_config.json` at plan time — do not reintroduce a removed +300 gold grant without an explicit balance decision).
- **Register / test** like phase 2.

### Phase 4 — Arbitration / coexistence

- **Ordering:** profile feature list order in `BotProfile` / session construction — document recommended order (e.g. war features before `discoverAndControl`) once Ambiguities #8 is answered.
- **Gold:** shared `minGoldReserve` vs war-specific reserve parameters; declare/sell_arms/ultimatum/surrender/revenge costs as listed above.
- **Once-per-day:** `Bot.cs` already limits decision ticks to once per calendar date; clarify whether multiple features may each emit a play in one day or whether war features should no-op if another feature already played (sink duplicate / BeginDecisionPhase behaviour — confirm against `BotCommandSink` at plan time).
- **No change to `discoverAndControl` internals** unless arbitration requires a shared coordinator (prefer profile ordering + parameters over rewriting discovery).

### Phase 5 — Eval config

- **Path:** `Docs/BotFeatures/<featureId>/eval_config.json` (+ history after runs), mirroring `Docs/BotFeatures/discoverAndControl/`.
- **Fields of interest:** `targetActions` for the war action ids; `parameterSearch` over reserves / profit thresholds / priority weights; longer `endDate` / more seeds if wars rarely complete in the default window; opponent features likely `discoverAndControl` or `baselineCardPlay`.
- **Gate metric:** paired-seed org score comparison via the existing eval harness (`src/Game.Evals`, `OrgScoreCollector` / `org_score`).
- **Carve-out note:** adding eval configs for a pure feature is in `/implement-bot-feature` authority; changing the harness itself is not.

### Mapping summary (AC → code)

- **Phase 1 observation gaps:** `IBotObservation.cs`, `BotObservation.cs`, `BotViews.cs`; queries in `Wars.cs`, relation helpers, `ResourceQuery` / CountryScore, `RelationCardTarget`, `RevengeEligibilityQuery` (only if seat-visible).
- **Phases 2–3 behaviour:** new `*Feature.cs` under `src/Game.Bots/`, registry entry, focused tests.
- **Phase 4 coexistence:** profile JSON feature order + parameters; possibly docs under `Docs/BotFeatures/`.
- **Phase 5 eval:** `Docs/BotFeatures/<id>/eval_config.json`.
- **Unchanged gameplay:** war card conditions/effects, peace resolution math, max control pool — bots consume them, they do not rebalance them in this issue.

## Out of Scope

- Writing `plan.md` or any implementation in this specify step (owner discussion / clarification only).
- New war gameplay mechanics, new action ids, rebalancing card costs/effects, or changing peace-resolution formulas — unless the owner explicitly opens a balance follow-up (sell_arms gold drift called out in Ambiguities).
- Bot-only privileged information (undiscovered countries' wars, other orgs' hands/gold, hidden occupation maps beyond seat visibility).
- Teaching bots to play non-war cards except insofar as Ambiguities #4 expands scope to rivalry/opinion unlocks.
- Unity UI for bot war decisions, Action Log changes, or player-facing tutorials about bot strategy.
- Replacing `discoverAndControl` or `baselineCardPlay`; arbitration only.
- Multi-country / allied wars (game still two participants per war).
- Using `/implement-bot-feature` to sneak observation or systems changes past Planning Discipline.

## Ambiguities

- [NEEDS CLARIFICATION: Feature packaging — single `warProfit` (or similar) feature that both declares and prosecutes, vs multiple features (e.g. `declareWar` + `prosecuteWar`, optionally separate `revenge`)? If multiple, what is the required profile order among them and relative to `discoverAndControl`?]
- [NEEDS CLARIFICATION: Profit objective — should the bot maximize its own `org_score`, maximize own−bestRival (or own−sumOthers) relative score, maximize gold as a co-equal objective, or a weighted mix? If relative, which rival set (all other participating orgs, only orgs with control on the war's loser, a named rival org)?]
- [NEEDS CLARIFICATION: When is declaring war "profitable enough"? Absolute expected score delta threshold? Ratio vs gold spent? Only when the bot's control on the intended winner side exceeds a minimum (and/or exceeds enemy control on that side)? Prefer wars where the bot's control×CountryScore on one side dominates the other? Should low-CountryScore border wars be deprioritized even if control-skewed?]
- [NEEDS CLARIFICATION: Prerequisite unlocking — should this issue's feature(s) also play `make_rival` and opinion-raising advisor cards to unlock `declare_war`, or assume other features / future work provide rivalry and opinion? If in scope, how aggressive should unlocking be relative to spending gold on discovery/control?]
- [NEEDS CLARIFICATION: During an active war, preferred policy among `sell_arms` (shipped: pay 200g for decaying +10% damage on that country) vs forcing resolution with `ultimatum` (300g, force selected country win at own progress ≥50) vs `surrender` (500g, force selected country lose at own progress ≥0)? Always arms-first until force cards are playable? Force immediately when playable if score-positive? Cap arms plays per war?]
- [NEEDS CLARIFICATION: When is intentional losing correct? Confirm the provisional rule: play `surrender` on a country the bot controls only when the bot expects higher post-peace score from the opponent winning (more control×score on the other side), and never surrender a country that is the bot's primary score engine. Any hard veto (e.g. never surrender HQ country / never surrender if MyControl ≥ X)?]
- [NEEDS CLARIFICATION: Revenge policy — always redeclare when `revenge` is playable after a loss, or only when the profit heuristic still says the rematch is worthwhile? If the first war was a bad bet, should revenge be suppressed even when eligible?]
- [NEEDS CLARIFICATION: Interaction with `discoverAndControl` — hard priority (war feature always ticks first and takes the day when any war play qualifies), soft priority (only when expected score delta exceeds a threshold), gold-partitioned (N% reserve for discovery), or mutually exclusive profile modes (war profile vs discover profile)? Should war ever interrupt an org that still has zero/few discoveries?]
- [NEEDS CLARIFICATION: Observation surface depth — full seat-visible war/relation/progress/score API (larger change, smarter bots), vs minimal change (only `IsPlayable` + `ActionId` heuristics, maybe `TargetCountryId`, without exposing war progress)? The weak path might fit closer to `/implement-bot-feature` but cannot implement progress-aware ultimatum timing or score-aware declare. Which depth is in scope for #83?]
- [NEEDS CLARIFICATION: Eval length / seeds — default discoverAndControl-style ~5 years / 10 seeds: is that enough for declare→monthly progress/decay→peace chance or card-forced peace to show score signal? Prefer longer `endDate`, more seeds, war-forcing opponent setups, or seeded scenarios that start mid-rivalry?]
- [NEEDS CLARIFICATION: Minimum control to play war cards — should bots only declare/prosecute/resolve in countries where `MyControl` meets a minimum (card gates already require control for ultimatum ≥10 / surrender ≥20 / revenge ≥20; declare_war has no control gate today)? Add a stricter bot-side floor (e.g. never declare below 15 control on the declaring country)?]
- [NEEDS CLARIFICATION: Multi-org hurt target — when relative scoring matters, should the bot preferentially hurt a specific rival org (largest score threat, largest control on the enemy side, fixed opponent in eval), or treat "all others" symmetrically?]
- [NEEDS CLARIFICATION: Gold reserve / affordability policy given current shipped costs — `declare_war` 100, `sell_arms` 200, `ultimatum` 300, `surrender` 500, `revenge` 50 (and `make_rival` 50 if unlocking is in scope). One shared `minGoldReserve`? Per-action ceilings? Keep a declare budget separate from an ultimatum budget? Ever go into near-zero gold for a high-EV ultimatum?]
- [NEEDS CLARIFICATION: Phase 1 observation work — is it in-scope for issue #83 (one issue, sequenced plans), or should observation extensions be a separate prerequisite issue/spec that #83 depends on before any war feature logic? Owner prompt asked to decompose carefully first; confirm the issue boundary.]
- [NEEDS CLARIFICATION: sell_arms economics drift — owner prompt describes sell_arms as "+300 gold to org" during war; shipped `Assets/Configs/action_config.json` currently lists cost 200 gold and only `sell_arms_damage_bonus_effect` (no gold-grant effect in `effect_config.json`). Should bot policy assume the shipped net cost (−200g + damage help), restore the original +300g grant design before bot work, or treat gold from sell_arms as out of the profit model?]
- [NEEDS CLARIFICATION: Natural peace vs card-forced peace — bots may wait for monthly peace-chance resolution once progress is in the win/lose band instead of paying for ultimatum/surrender. Is waiting allowed/preferred when progress is already extreme, or should bots always force when the card is playable and score-positive?]
- [NEEDS CLARIFICATION: Partial visibility — if the bot discovers only one participant in a war, may it still sell arms / ultimatum / surrender on that side using only local control and own-side progress, or must both sides be discovered before any prosecution?]
- [NEEDS CLARIFICATION: Occupation / province foresight — peace province transfers depend on occupied loser provinces. Should phase 1 expose seat-visible occupation info for discovered countries so bots can estimate transfer EV, or is control×CountryScore skew alone enough for v1?]
- [NEEDS CLARIFICATION: At-most-one play — today features typically emit at most one play per tick and Bot runs all features once per game-day. Should a war feature be hard-capped to one play/day even if declare and sell_arms are both attractive, and can discoverAndControl still play on the same day after a war feature no-ops (or after it plays)?]
- [NEEDS CLARIFICATION: Eval opponent design — against passive orgs, `baselineCardPlay`, or a second war-capable bot? Mirror matches (both sides war features) change relative-score dynamics; confirm the first eval's opponentFeatures.]
- [NEEDS CLARIFICATION: Naming — preferred `featureId`(s) for registry / `Docs/BotFeatures/` (e.g. `warProfit`, `declareWar`, `prosecuteWar`)? camelCase per existing `discoverAndControl` / `baselineCardPlay`.]
- [NEEDS CLARIFICATION: Success bar for the first ship — "bots sometimes play war cards in eval," "bots beat discoverAndControl-only twin on org_score by epsilon," or "bots demonstrably win wars on the side they control"? Pick the gate narrative before planning.]
