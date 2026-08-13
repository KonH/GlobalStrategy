# Plan: Bot `warProsecute` Feature (Child E)

## Spec

Source: `Docs/Specs/26_08_13_09_bot-war-prosecute/spec.md` (Child E of umbrella `Docs/Specs/26_08_01_09_bot-war-features/`, issue #83).

Ship `warProsecute` so bots play **`sell_arms` only** on the preferred side of an **active war** when the expected dual-side natural-peace profit after the arms-driven win-% lift is positive, then **wait for natural peace** — never Ultimatum / Surrender.

Acceptance (condensed):
- Playable `sell_arms` + gold policy + positive dual-side EV from raising win % → propose/play on preferred side; resolution = natural peace.
- Force cards disabled/absent or present → feature never plays/depends on `force_war_win` / `force_war_loss`.
- Non-positive EV or better rival proposal → skip / lose Child B arbitration.
- Destroy scored only as EV side effect.
- v1 centers on `sell_arms`; peacetime-only pre-buff without an active war is out of v1 acceptance.

**Verified shipped economics (do not rebalance):**
| Item | Value |
|------|-------|
| `sell_arms` gold | **175** (`action_config.json`) |
| Opinion gate | military advisor **≥ 80** (no `isInWar`) |
| Effect | `sell_arms_damage_bonus_effect` → `troops_damage_bonus_percent` **+30.0**, decay **2.0**/month |
| Force flag | `featureFlags.enableForceWarCards: false` |
| Peace control | winner **+0.5** / loser **−1.0** of own control |
| Province transfer | **50–80%** of eligible occupied loser provinces |

**Depends on:** Child A observation + Child B cadence/arbitration. **Eval:** Child F. **Shared EV:** Child D natural-peace profit helper.

## Goal

Register an `IBotFeature` `warProsecute` that, each Child B decision cycle, scans playable `sell_arms` on at-war acting countries, estimates the **Δ** in dual-side natural-peace profit from the post-play win-% lift (via `WarWinChanceEstimator` + pending sell_arms damage stacking), proposes the best positive candidate into B’s Δorg_score arbitration pool under shared gold-reserve/dip policy, and never emits force-resolve plays.

## Approach

### 1. Feature surface (`/implement-bot-feature` carve-out)

| Piece | Location |
|-------|----------|
| `WarProsecuteFeature` | `src/Game.Bots/WarProsecuteFeature.cs` — `FeatureId = "warProsecute"` |
| Registry | `BotFeatureRegistry.CreateDefault` — register factory like `control` |
| Parameters | `minGoldReserve` (default align with baseline/control conceptual reserve), `minProfitDelta` (default `0`), optional `minWinPercentAfter` / `minWinPercentLift` (defaults `0` / `0` unless evals need floors), `allowHighEvGoldDip` (default true), optional `goldToScoreWeight` (default small positive so gold spoils/cost enter the same scalar as control×CountryScore EV) |
| Eval scaffold | Optional thin `Docs/BotFeatures/warProsecute/` stub; **Child F** owns locked `endDate` 1920 / `hoursPerTick: 4` / `seedCount: 20` / control-only twin / `targetActions: ["sell_arms"]` |

Feature **never** matches `force_war_win` / `force_war_loss`. No branches on `enableForceWarCards` required for correctness when cards are absent; if present later, still ignore.

### 2. Child B contract (consume, do not redefine)

Assume B ships proposal/arbitration roughly as:
- Feature proposes zero-or-one (or a small set of) scored play candidates with `EstimatedDeltaOrgScore` (or equivalent) + play command fields (`ActionId`, `CountryId`, `SlotIndex`, `TargetCountryId`).
- Orchestrator picks global best Δ; at most one strategic play per `botDecisionIntervalHours` interval.
- Discard / draw intent scoring live in B — prosecute only supplies proposals and, if B’s draw scorer asks features for intent weights, a positive weight for `sell_arms` when an active profitable war exists.

If B’s final API differs, adapt call sites; do not fork a second arbitration path inside E.

### 3. When to consider `sell_arms` (v1)

For each `BotCountryView` in `obs.Countries`:
1. `IsAtWar` (Child A) and `WarOpponentCountryId` non-empty when needed for EV.
2. Hand contains `ActionId == "sell_arms"` with `IsPlayable`.
3. Gold: if `obs.Gold - 175 >= minGoldReserve` → OK; else only if `allowHighEvGoldDip` and estimated profit Δ clearly compensates (Δ ≥ cost mapped via `goldToScoreWeight` / `minProfitDelta` — exact inequality locked at implement time to “dip only when post-dip EV still > 0 and beats reserve-respecting alternatives”).
4. Soft preferred-side bias (umbrella): prefer selling arms on the side where **enemy org control is higher** or **bot has no control on the enemy** — implement as a soft additive bonus on the arbitration score (small fraction of Δ), not a hard filter. Prefer the acting country that is the bot’s preferred winner when both sides have playable `sell_arms`.

Peacetime `sell_arms` with `IsAtWar == false`: **do not propose in v1** (card remains legal for players / other features; prosecute acceptance is wartime).

### 4. Win-% lift (critical estimator note)

`WarWinChanceEstimator.EstimateAttackerWinPercent(..., pendingAttackerDamageBonusPercent, pendingAttackerDurabilityBonusPercent)` today applies **revenge-style replace** on `RevengeWarBonusQuery` — **wrong** for sell_arms, which **adds** `initialValue` (30) onto `troops_damage_bonus_percent` and is already folded into live `damage` via `DamageCollector`.

**Do this instead:**

1. **Consume Child D’s** numeric `WarWinChanceEstimator` overload (6 combat stats + optional pending defaults **0**). Do **not** add a second overload shape in E. Call with `pending=0`; scale `damage_S'` externally for sell_arms additive stack. Never pass sell_arms initial as `pendingAttackerDamageBonusPercent`.

2. Observation (Child A) exposes live `Recruits` / `Damage` / `Durability` / **`TroopsDamageBonusPercent`**. E must **not** extend `BotObservation.Build`.

3. When scoring a play on preferred side `S` vs opponent `O`:
   - `factor = (100 + bonus_S + sellArmsInitial) / (100 + bonus_S)` with `sellArmsInitial` from `EffectConfig` (`sell_arms_damage_bonus_effect.InitialValue`, do not hardcode 30 in feature logic beyond tests).
   - `damage_S' = damage_S * factor`.
   - Map attacker/defender from war roles (prefer progress sign / war participant kind from observation: if Child A only exposes `OwnWarProgress`, treat progress `> 0` as attacker-favored for that country as attacker; if A later exposes attacker id, prefer that). Compute:
     - `p_before` = attacker win % from live stats (D overload, pending=0).
     - `p_after` = same with preferred side’s damage replaced by `damage_S'`.
   - Preferred-side win probability: if preferred is attacker, `p_pref = p_attacker`; if defender, `p_pref = 100 - p_attacker`.

### 5. Dual-side natural-peace profit EV (reuse Child D)

**Reuse D’s `WarPeaceOrgScoreEstimator` only** (same type, fractions, mid-band **0.65**, `goldToOrgScore` default **0.001**). Do not add `WarNaturalPeaceProfitEstimator`.

**Scalar:**
```
EV(p) = p/100 * Outcome(preferredWins) + (1 - p/100) * Outcome(preferredLoses)
prosecuteDelta = EV(p_after) - EV(p_before) - goldCost * goldToOrgScore
```

Propose only if `prosecuteDelta > minProfitDelta` (and win-% gates if configured). Set `TargetBiasMultiplier` for soft preferred-side bias (same B field as D). **Do not** add revenge’s +20% weight here (that is declare-only).

Destroy is never a separate mode — only inside `Outcome(...)`.

### 6. Command emission

On win of arbitration (or, if implementing before B merges, temporary direct `Tick` play of the single best candidate):  
`sink.PlayCountryCard("sell_arms", countryId, slotIndex, targetCountryId)` — `TargetCountryId` unused for sell_arms today; pass through card view value.

After play: **no** follow-up force resolve; natural peace systems remain authoritative.

### 7. Explicit non-goals

- Force cards; peacetime pre-buff without active war; config rebalance; Child F locked eval knobs; hunting destroy; `decrease_enemy_control`; observation Build changes; new win-% overload shapes.

## Agent Steps

- [ ] **Confirm prereqs** — Child A fields (including `TroopsDamageBonusPercent`), Child B proposal/arbitration API, and Child D `WarPeaceOrgScoreEstimator` + win-% overload are available (implement order **A → B → D → E**).

- [ ] **Consume D win-% overload** — Call numeric `EstimateAttackerWinPercent` with pending=0; scale damage externally for sell_arms stack; do not extend Systems in E.

- [ ] **Consume D peace profit helper** — Call `WarPeaceOrgScoreEstimator` for `EV(p)` / `prosecuteDelta`; do not add a second estimator type.

- [ ] **Implement `WarProsecuteFeature`** — Scan at-war playable `sell_arms`; compute win-% lift + dual-side Δ; apply gold reserve/dip; soft preferred-side bias via `TargetBiasMultiplier`; propose best candidate to B. Hard-exclude force action ids.

- [ ] **Register feature** — `BotFeatureRegistry.CreateDefault`; parameters with documented defaults; do not enable in root `botFeatures` by default unless umbrella/profile work says otherwise (default profile stays `control`-only; war profiles / evals enable `warProsecute`).

- [ ] **Wire effect initial value** — Resolve `sell_arms` → effect ids → `CountryResourceModifierEffectParams.InitialValue` from configs available to bots (thread `EffectConfig` into feature ctor via registry/session the same way other bot config is passed, or pass `sellArmsDamageBonusPercent` as a constructed parameter from host). Prefer config-driven over magic `30`.

- [ ] **Tests** — see Tests below.

- [ ] **Build** — After any `src/` change: `/dotnet-build Release` (and `dotnet test` for touched suites).

- [ ] **Eval handoff** — Leave Child F to author `Docs/BotFeatures/warProsecute/eval_config.json` with `targetActions: ["sell_arms"]` (not force ids) and locked horizon knobs; optional empty scaffold only if needed for registry smoke.

## User Steps

### 1. None

None for this child — pure `src/` bot feature + estimator/observation helper work; no Unity Editor scene, prefab, or visual inspection. Child F eval runs are harness/CLI, not Editor.

## Tests

Prefer `src/Game.Tests/` pure C# (existing bot / estimator / peace patterns).

- **`WarWinChanceEstimator` stats overload** — equal strengths → 50; recruits 0 → 1; damage scale up on attacker raises percent; scaling defender damage lowers attacker percent.
- **Sell-arms stacking factor** — given `bonus=0`, factor with +30 matches `damage * 1.3`; given `bonus=30`, factor is `(160/130)`, not revenge-replace; prove world pending-revenge API is **not** used in prosecute path tests.
- **`WarProsecuteFeature` proposes sell_arms when EV positive** — synthetic observation: at war, playable `sell_arms`, skewed control/occupation so arms lift yields `prosecuteDelta > 0` → proposes/plays `sell_arms` on preferred side.
- **Skips non-positive EV** — same setup with dual-side control that makes net EV ≤ 0 → no propose/play.
- **Ignores force cards** — hand containing only `force_war_win` / `force_war_loss` (if injected) → no play from this feature.
- **Peacetime skip** — playable `sell_arms` but `IsAtWar == false` → no v1 propose.
- **Gold dip** — below `minGoldReserve` but high EV + dip enabled → may propose; dip disabled → skip.
- **Destroy side effect** — loser near empty provinces / high occupied count → EV includes destroy term (assert delta differs vs destroy-ignored baseline in helper unit tests).
- **Registry** — `CreateDefault` can construct `warProsecute`; unknown id still fails fast.
- **Shared helper parity** — where Child D exists, one test file or shared facts ensure declare/prosecute use the same peace control fractions and transfer midpoint.

## Constitution Check

Checked against `Docs/Constitution.md`.

- *Unity 6 + URP only.* No rendering / Unity asset changes.
- *ECS for all game logic in `src/`.* Peace/war resolution stays in existing systems; feature only estimates from observation and emits sink commands. Estimator overload remains pure in `Game.Systems`.
- *VContainer sole DI.* No new Unity/VContainer services; registry factory construction only.
- *UI Toolkit only.* No UI work.
- *Plan before implement / Spec before plan.* This paired `spec.md` + `plan.md` is the planning artifact. Bot-feature carve-out covers `IBotFeature` + registry + `Docs/BotFeatures/`; **estimator overload + `TroopsDamageBonusPercent` observation field are outside the carve-out and are explicitly planned here** before code.
- *File organisation.* Spec+plan under `Docs/Specs/26_08_13_09_bot-war-prosecute/` (not legacy `Docs/Plans/`).
- *One `.asmdef` per `Assets/Scripts/` feature folder.* No `Assets/Scripts` changes.
- *C# code style.* Tabs, braces always, `_` private prefix — match `Game.Bots` / `Game.Systems`.

**Conflicts:** None found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
