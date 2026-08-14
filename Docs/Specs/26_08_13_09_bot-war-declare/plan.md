# Plan: Bot `warDeclare` Feature (Child D)

## Spec

Source: `Docs/Specs/26_08_13_09_bot-war-declare/spec.md` (Child D of umbrella `Docs/Specs/26_08_01_09_bot-war-features/`, issue #83).

Add `warDeclare` so bots propose `declare_war` / `declare_revenge_war` only when **net expected Δorg_score > 0** after dual-side natural-peace accounting (winner +50% / loser −100% control shifts, 50–80% occupation province-transfer EV, destroy EV, gold), prefer high win % via `WarWinChanceEstimator`, softly bias toward enemy-higher-control / no-bot-control targets, and apply revenge soft weight **`score = Δ × 1.20`** in Child B arbitration — without force cards and without “playable ⇒ play.”

**Depends on:** Child A observation + Child B decision interval / proposal arbitration / shared gold-reserve policy surface. **Eval / merge gate:** Child F (`Docs/BotFeatures/warDeclare/`).

Key acceptance (see `spec.md`):
- Propose best positive-Δ declare (not first-in-hand); skip non-positive Δ even if playable.
- Revenge only when profitable; arbitration uses Δ×1.20 vs raw Δ for ordinary declare.
- Soft target bias; shared `minGoldReserve` with high-EV dip for expensive declares.
- Verified costs: `declare_war` **150g**, `declare_revenge_war` **125g**.

## Goal

Ship `WarDeclareFeature` (`featureId: warDeclare`) in `src/Game.Bots`, registered in `BotFeatureRegistry`, with pure helpers for win % (observation numeric inputs) and dual-side peace Δorg_score EV, unit-tested on synthetic observations — ready for Child B arbitration and Child F eval — without changing war domain systems, action configs, or Unity assets.

## Approach

### 1. Dependency contract (A + B)

**Child A (required fields — see `Docs/Specs/26_08_13_09_bot-war-observation/plan.md`):**
- Per country: `CountryScore`, `MyControl` (existing), control breakdown, `RivalCountryIds`, `IsAtWar` / `WarOpponentCountryId`, `OwnedProvinceCount`, `OccupiedOwnedProvinceCount`, `IsDestroyed`, combat inputs `Recruits` / `Damage` / `Durability`.
- Org: `OrgScore`, public `OrgScores`, `Gold`, country hands with `ActionId`, `IsPlayable`, `GoldCost`, `TargetCountryId`, `SlotIndex`.

**Child B (required orchestration — see `Docs/Specs/26_08_13_09_bot-war-infra/`):**
- Decision cycle collects **scored proposals** from enabled features; picks **global best arbitration score**; plays **at most one** card via sink.
- Revenge soft weight **owned by B**: when a proposal’s action is `declare_revenge_war` (or proposal flag `IsRevenge`), arbitration score = `estimatedDeltaOrgScore × 1.20`; ordinary declare uses raw Δ. D still gates on **raw Δ > 0** before proposing.
- Shared conceptual `minGoldReserve` parameter name (same key as `baselineCardPlay` / control alignment in B).
- `WarDeclareFeature` **must not** call `IBotCommandSink.PlayCountryCard` when B arbitration is active — it only **emits proposals**. If B’s landed API uses a transitional `Tick` that still plays when alone, follow B’s contract exactly; do not bypass the global best-Δ pool.

Do **not** implement A or B inside this child. If A/B are not merged yet, keep D’s helpers + feature compiling against the agreed proposal interface stubs only as far as B’s plan defines — prefer landing D after A+B.

### 2. Pure win-% helper (no `World` in features)

Child A deliberately exposes combat inputs and leaves estimator wrapping to D/E. Add a small pure helper in `src/Game.Systems` (preferred: overload next to existing `WarWinChanceEstimator`) **or** a bot-local pure mirror in `src/Game.Bots` that duplicates the shipped formula:

`strength = recruits × damage / max(enemyDurability, 1)`, win% = round(100 × att/(att+def)), clamp 1..99; apply pending revenge bonuses the same way `WarWinChanceEstimator.EffectiveCombatStat` does (strip live revenge then re-apply pending — for declare-time revenge preview, live revenge is usually 0; pass pending **10** damage / **5** durability from `declare_revenge_war_effect`).

**Preferred:** add

```csharp
WarWinChanceEstimator.EstimateAttackerWinPercent(
	double attackerRecruits, double attackerDamage, double attackerDurability,
	double defenderRecruits, double defenderDamage, double defenderDurability,
	double pendingAttackerDamageBonusPercent = 0,
	double pendingAttackerDurabilityBonusPercent = 0);
```

that shares the existing strength/clamp math (refactor the World overload to call it). This keeps one formula, stays in `Game.Systems`, and lets bots call it from observation numbers without `IReadOnlyWorld`. Covered by extending `WarWinChanceEstimatorTests`.

Constants for revenge pending bonuses: read from effect config only if already injected into bots; otherwise **document as feature parameters** with defaults matching shipped effect (`revengePendingDamageBonusPercent: 10`, `revengePendingDurabilityBonusPercent: 5`) so D stays inside bot-feature surface and does not parse `effect_config.json` from netstandard bots unless a numeric already flows through observation/host. Defaults must match `Assets/Configs/effect_config.json` `declare_revenge_war_effect`.

### 3. Dual-side peace Δorg_score EV (shared model)

Add a focused pure estimator in `src/Game.Bots` (e.g. `WarPeaceOrgScoreEstimator`) usable by D now and E later. Inputs are observation snapshots + assumed attacker win probability `p` in \[0,1\] + card gold cost.

**Peace control shifts (shipped):**
- Winner orgs in winner country: each org’s control increases by ~`peaceWinnerControlIncreaseFraction` (**0.5**) of current, capped by pool / per-org 100 (mirror `Wars.ApplyWinnerControlBoosts` intent; bot EV may use the uncapped desired `round(control × 0.5)` then clamp with a simple room model using `maxControlPool` parameter default **100**).
- Loser orgs in loser country: control cut by `peaceLoserControlDecreaseFraction` (**1.0**) ⇒ effectively wipe loser-side org control in that country.

**Outcome mix:** attacker = declaring country, defender = `TargetCountryId`.

```
EV = p * ScoreDelta(attackerWins) + (1-p) * ScoreDelta(attackerLoses) - goldCostPenalty
```

`ScoreDelta(outcome)` for the **observing org only** (maximize own `org_score`):

1. **Control-shift term:** for each of {winnerCountry, loserCountry}, apply the boost/cut to **bot’s** control;  
   `Δscore += (Δcontrol / 100.0) * CountryScore` (pre-transfer CountryScore is acceptable for v1).
2. **Enemy control decrease (required profit input):** expected Δ to the **lead rival’s** public org_score contribution on the loser (and winner if relevant) from control wipe/boost + province transfer + destroy, using Child A `OrgScores` / `ControlByOrg`. This is a required EV term (umbrella: hurt best-scoring rival), separate from the soft *target bias* multiplier on arbitration ranking.
3. **Occupation / province-transfer EV:** mid-band transfer fraction **0.65** = midpoint of shipped **50–80%**.  
   - Eligible occupied proxy: if defender (or loser) `OccupiedOwnedProvinceCount > 0`, use that; else use `assumedOccupiedShare × OwnedProvinceCount` (parameter, default **0.35**) as declare-time stand-in for eventual occupation depth.  
   - Expected transferred count ≈ `p_outcome_win × 0.65 × eligible`.  
   - Map province moves to score via a simple proportional CountryScore shift:  
     `scorePerProvince ≈ CountryScore / max(OwnedProvinceCount, 1)` on loser; winner gains / loser loses that × transferred count; bot Δ = `(botControlWinner/100)*gain + (botControlLoser/100)*loss` after control shifts for that outcome (order: apply control shifts on pre-transfer scores, then apply transfer score mass — document order in code comments; keep deterministic).
4. **Destroy EV:** if expected remaining loser owned provinces after transfer ≤ 0, treat country as destroyed: bot loses `(botControl/100)*CountryScore` on that country (control cleared). Side effect only — no hunt mode.
5. **Gold (required in the same scalar EV as the hard gate):** include gold change in `estimatedDeltaOrgScore` via a shared bridge parameter `goldToOrgScore` (default **0.001** — same constant locked for C/E). Terms: `−GoldCost * goldToOrgScore` plus expected peace gold spoils `p * (botControlShareOnWinner) * peaceGoldPerMonth * assumedWarMonths * goldToOrgScore` with `peaceGoldPerMonth` default **1000**, `assumedWarMonths` default **6**. Affordability / `minGoldReserve` / high-EV dip remain **additional** gates, not a substitute for gold in EV. Unaffordable plays are non-candidates.

**Hard gate:** propose only if `estimatedDeltaOrgScore > 0` (control + occupation + destroy + rival-hurt + gold terms).

### 4. Soft target bias + win % preference

Parameters (feature `parameters` map):

| Key | Default | Role |
|-----|---------|------|
| `minGoldReserve` | `0` | Shared name with baseline/control; leave gold ≥ reserve after cost unless dip |
| `highEvDipMinDelta` | `0` | Allow dip below reserve when `Δ >= highEvDipMinDelta` **and** `Δ > 0` (umbrella: clearly high-EV; start with any positive Δ; raise in F if needed) |
| `minWinPercent` | `40` | Hard floor: skip if estimator win % &lt; this |
| `targetBiasWeight` | `0.10` | Soft: set `TargetBiasMultiplier = 1 + targetBiasWeight` when target country has (max other-org control &gt; bot control) **OR** bot control == 0; else `1.0` |
| `goldToOrgScore` | `0.001` | Shared with C/E peace EV |
| `revengeArbitrationMultiplier` | `1.20` | Documented for D tests; **B applies** the weight — D must not double-apply |
| `assumedOccupiedShare` | `0.35` | Declare-time occupation proxy |
| `revengePendingDamageBonusPercent` | `10` | Match effect config |
| `revengePendingDurabilityBonusPercent` | `5` | Match effect config |

**Ranking inside D (before B):** among positive-Δ candidates, pick max `estimatedDeltaOrgScore`; if `|Δa−Δb|` ≤ `deltaTieEpsilon` (default **1.0** score units), prefer higher win %; then prefer revenge only via B’s weight (D may break remaining ties by ordinal `ActionId` / `SlotIndex` for determinism).

**Soft bias:** proposal carries raw `EstimatedDeltaOrgScore` (hard gate) and `TargetBiasMultiplier`; B computes `arbitrationScore = EstimatedDeltaOrgScore * TargetBiasMultiplier * (revenge ? 1.20 : 1.0)`. Do not bake bias into Δ and do not double-apply revenge weight.

### 5. Feature behaviour

`WarDeclareFeature : IBotFeature` with `Id = "warDeclare"`.

On CollectProposals / Tick-per-B-contract:
1. Scan all country hands for playable cards where `ActionId` is `declare_war` or `declare_revenge_war`.
2. Resolve defender = `TargetCountryId` (required; skip empty).
3. Skip if either side `IsDestroyed` or already `IsAtWar` (playability should already fail; belt-and-suspenders).
4. Compute win % via pure helper; attacker = card’s `CountryId`, defender = target; pass revenge pending bonuses only for `declare_revenge_war`.
5. Skip if win % &lt; `minWinPercent`.
6. Compute raw `estimatedDeltaOrgScore` via peace EV helper.
7. Skip if `Δ <= 0`.
8. Gold: if `obs.Gold - GoldCost < minGoldReserve` and not (`Δ >= highEvDipMinDelta`), skip.
9. Emit proposal with raw Δ, win %, bias multiplier, action/country/slot/target.

Register in `BotFeatureRegistry.CreateDefault`:  
`registry.Register(WarDeclareFeature.Id, parameters => new WarDeclareFeature(parameters, maxControlPool));`  
(`maxControlPool` threaded like `ControlFeature` if EV clamp needs it; else read from parameters default 100.)

**Do not** add `Docs/BotFeatures/warDeclare/eval_config.json` here — **Child F** owns eval packages (locked horizon / control-only twin / targetActions). D’s implement path uses unit tests as the local gate; optional note in `/implement-bot-feature` PRD that eval is deferred to F.

**Default profile:** do **not** enable `warDeclare` in root `game_settings.json` `botFeatures` in this child (umbrella ships control-only by default; F/eval profiles enable war features). Registration alone is enough.

### 6. Explicit non-goals

- No changes to `Wars`, peace fractions, action/effect JSON costs, force cards, UI, or observation Build (A).
- No `warUnlock` / `sell_arms` logic (C/E).
- No calendar-day gate / discard / draw scorer (B).

## Agent Steps

- [ ] **Confirm A+B landed contracts** — read merged Child A fields and Child B proposal DTO / feature method signatures; adjust call sites below to the landed names (do not re-implement A/B).
- [ ] **Add pure win-% overload** — `WarWinChanceEstimator` numeric overload (or equivalent) + tests proving parity with World-based API on identical inputs (`src/Game.Systems/WarWinChanceEstimator.cs`, `src/Game.Tests/WarWinChanceEstimatorTests.cs`).
- [ ] **Add `WarPeaceOrgScoreEstimator` (name flexible)** — pure dual-side EV helper in `src/Game.Bots` implementing Approach §3; document mid-band **0.65**, fractions **0.5 / 1.0**, destroy side effect; parameters for assumed occupation share / war months.
- [ ] **Implement `WarDeclareFeature`** — `src/Game.Bots/WarDeclareFeature.cs`: scan declare/revenge cards, gold reserve + dip, min win %, raw Δ &gt; 0 gate, soft bias metadata, emit B proposals (no direct sink play under arbitration).
- [ ] **Register feature** — one line in `BotFeatureRegistry.CreateDefault`.
- [ ] **Unit tests** — `src/Game.Tests/WarDeclareFeatureTests.cs` (and estimator tests) per Tests below.
- [ ] **Run `/dotnet-test` then `/dotnet-build Release`** after `src/` changes (project workflow / skills).

## User Steps

### 1. None

None — `src/` bot feature + systems helper + tests only; no Unity Editor scene/asset work. Child F owns eval config authoring under `Docs/BotFeatures/`.

## Tests

- **`war_win_chance_numeric_overload_matches_world_path`** — identical recruits/damage/durability (+ pending bonuses) ⇒ same percent as World/`ResourceQuery` overload.
- **`peace_ev_positive_when_bot_skew_favors_likely_winner`** — synthetic country views: high bot control on preferred attacker, enemy control on defender, high win % ⇒ `Δ > 0`.
- **`peace_ev_non_positive_when_bot_holds_both_sides_symmetrically`** — meaningful bot control on both participants with ~50% win ⇒ Δ ≤ 0 or below gate (dual-side accounting).
- **`declare_skips_non_positive_delta_even_if_playable`** — playable `declare_war` with Δ ≤ 0 ⇒ no proposal.
- **`declare_picks_highest_delta_not_first_slot`** — two playable declares; higher Δ wins (lower SlotIndex loses).
- **`revenge_skipped_when_not_profitable`** — playable `declare_revenge_war` with Δ ≤ 0 ⇒ no proposal.
- **`revenge_proposal_marked_for_b_weight_without_double_multiply`** — profitable revenge proposal carries raw Δ; feature does not pre-multiply by 1.20 (assert proposal Δ equals estimator raw Δ).
- **`soft_bias_metadata_when_enemy_higher_or_no_bot_control`** — target with enemy control &gt; bot / bot control 0 ⇒ bias multiplier `1 + targetBiasWeight`; otherwise `1`.
- **`min_win_percent_floor`** — win % below `minWinPercent` ⇒ skip despite positive Δ stub.
- **`gold_reserve_blocks_unless_high_ev_dip`** — below reserve blocks; with `Δ >= highEvDipMinDelta` allows proposal.
- **Determinism** — same observation ⇒ same chosen proposal (ordinal ties).

Full suite green + Release build.

## Constitution Check

Checked against `Docs/Constitution.md`.

- *Unity 6 + URP only.* No rendering / Unity asset changes.
- *ECS for all game logic in `src/`.* War domain rules stay in `Game.Systems`; bot feature only **estimates** using observation + pure helpers — no MonoBehaviour logic, no duplicate declare/peace mutation path.
- *VContainer sole DI.* No new Unity registrations; registry factory pattern unchanged.
- *UI Toolkit only.* No UI work.
- *Plan before implement / Spec before plan.* This paired `spec.md` + `plan.md` is the planning artifact. Constitution **bot-feature carve-out** still allows `/implement-bot-feature` for `IBotFeature` + registry (+ eval configs, which F owns); Child D deliberately keeps a formal plan because declare profit EV + estimator overload touch shared helpers beyond a trivial feature stub. **No conflict** — carve-out is not violated; observation/infra remain A/B.
- *File organisation.* Lives under `Docs/Specs/26_08_13_09_bot-war-declare/` (not legacy `Docs/Plans/`).
- *One `.asmdef` per Assets feature folder.* No `Assets/Scripts` changes.
- *C# code style.* Tabs, `_` private fields, braces always — match `Game.Bots`.

**Conflicts:** none. Dependency on A+B is sequenced, not constitutional. Numeric `WarWinChanceEstimator` overload is a pure refactor/extension in `Game.Systems` justified by bot no-World rule from Child A — still ECS/`src/`, no Unity DI change.

Use the implement skill to start working on the plan or request changes.
