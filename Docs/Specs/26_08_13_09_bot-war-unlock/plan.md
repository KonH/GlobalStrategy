# Plan: Bot `warUnlock` Feature (Child C)

## Spec

Source: `Docs/Specs/26_08_13_09_bot-war-unlock/spec.md` (Child C of umbrella `Docs/Specs/26_08_01_09_bot-war-features/`, issue #83).

Add a new **`warUnlock`** `IBotFeature` that proposes scored plays of shipped unlock cards so bots can open rivalry and opinion gates toward profitable `declare_war` / `sell_arms` paths — competing via estimated Δorg_score in Child B’s shared arbitration pool, never grinding unlocks that are not on a profitable path and never dipping gold reserve for unlock costs.

**Hard dependencies (must land first):**
- **Child A** — `Docs/Specs/26_08_13_09_bot-war-observation/` (rivals, CountryScore / OrgScores, war/occupation/combat inputs).
- **Child B** — `Docs/Specs/26_08_13_09_bot-war-infra/` (`botDecisionIntervalHours` cadence; proposal + global best-Δorg_score arbitration; shared discard / intent-aware draw). This plan **assumes** B’s landed proposal surface (names may match B’s `plan.md`; align at implement time).

**Not this plan:** Child D/E play logic; Child F eval knobs / twin gate / timeout budgets (register `warUnlock` only).

**Key acceptance (from child + umbrella):**
- Playable `make_rival` toward a profitable future declare wins the interval when its path EV beats other proposals.
- Playable mil / ruler opinion cards (and diplomacy opinion when needed for `make_rival`) unlock declare / sell_arms gates when path EV wins arbitration.
- Non-positive / dominated unlocks are skipped — no blind opinion grinding.

**Verified shipped economics** (`Assets/Configs/action_config.json` + `effect_config.json`, 2026-08-13):

| ActionId | Gold | Notable gates | Effect |
|----------|------|---------------|--------|
| `make_rival` | **75** | diplomacy opinion ≥30; relation `none`→rival; named `TargetCountryId` | `make_rival_effect` (Rival) |
| `improve_military_advisor_opinion` | **30** | control ≥10 | OpinionModifier +50 (`letter_of_commendation`) |
| `improve_ruler_opinion` | **50** | control ≥20 | OpinionModifier +25 (`royal_audience`) |
| `improve_diplomacy_advisor_opinion` | **30** | control ≥10 | OpinionModifier +50 — **in Child C scope** when diplomacy &lt;30 blocks `make_rival` (umbrella acceptance text), even though the ownership table row lists only the three primary unlock ids |

Downstream gates this feature unlocks toward (owned by D/E, not played here): `declare_war` 150g (`targetRulerOrMilitaryOpinion` ≥50 = max(ruler, mil), rival, neither at war); `sell_arms` 175g (mil opinion ≥80).

## Goal

Ship `WarUnlockFeature` (`featureId` `warUnlock`) that, on each Child B decision cycle, proposes at most one best unlock play — `make_rival` or a necessary opinion improver — scored as discounted path EV toward a profitable war, so arbitration can pick it over or under `control` / later war features; register it in `BotFeatureRegistry`; cover decision logic with synthetic-observation unit tests; leave eval package contents to Child F.

## Approach

### 1. Implement path and surface boundaries

- **Intended implement path after A–B:** `/implement-bot-feature` (scaffold PRD + registry + feature file + unit tests). This formal `spec.md`/`plan.md` pair is the owner-requested planning artifact **in addition to** the Constitution bot-feature carve-out (standing harness under `Docs/Specs/26_07_16_14_bot-feature-eval-harness/`).
- **In authority for the skill:** `src/Game.Bots/WarUnlockFeature.cs`, `BotFeatureRegistry` registration, focused `Game.Tests` for feature logic, and a stub mention that `Docs/BotFeatures/warUnlock/` will exist (F owns contents).
- **Out of authority / do not sneak in:** `IBotObservation` / Build changes (A); Bot orchestrator cadence / arbitration / discard / draw scorer (B); `Assets/` economics; Unity scenes.

If A–B are not merged when implement starts, **stop** — do not re-implement observation or infra inside this feature.

### 2. Child B contract assumptions (align names to landed B plan)

Assume B exposes approximately:

1. A **proposal** type carrying at least: `FeatureId`, card identity (`ActionId`, `CountryId`, `SlotIndex`, `TargetCountryId`), and `EstimatedDeltaOrgScore` (raw Δ used for ranking; revenge ×1.20 weight is B/D — unused by unlock).
2. A feature participation mode where features **collect proposals** for the interval instead of calling `PlayCountryCard` immediately inside a free-for-all `Tick` (today’s `ControlFeature` / `BaselineCardPlayFeature` direct-play pattern is migrated by B). `WarUnlockFeature` must **only propose**; the orchestrator applies the single winner via `IBotCommandSink`.
3. Shared **discard** and **draw** helpers owned by B — `warUnlock` does **not** discard and does **not** select 1-of-3 offers.
4. Shared **war peace Δorg_score estimator** — consume **D’s** `WarPeaceOrgScoreEstimator` only (no C-private `WarPathProfitEstimator` fork). If C would land before D, block on a D-owned stub PR first.

Do not invent a second arbitration pass inside `warUnlock`.

### 3. `WarUnlockFeature` behaviour

```csharp
// src/Game.Bots/WarUnlockFeature.cs
public sealed class WarUnlockFeature : IBotFeature {
	public const string Id = "warUnlock";
	// parameters: minGoldReserve (default 0), unlockPathDiscount (default 0.4),
	//             unlockStepDecay (default 0.7)
}
```

**Owned action ids** (match only these when scanning hands):

- Primary (umbrella ownership table): `make_rival`, `improve_military_advisor_opinion`, `improve_ruler_opinion`
- Path-enabler (umbrella Child C acceptance): `improve_diplomacy_advisor_opinion` when diplomacy opinion is the blocker for a profitable `make_rival`

Never propose `declare_war` / `declare_revenge_war` / `sell_arms` / force / control cards.

**Scan order (deterministic):** countries ordinal by `CountryId`; within a country, hand by `SlotIndex`. Among valid unlock candidates, keep the single best by path score (then stable tie-break: lower `SlotIndex`, then ordinal `ActionId`, then ordinal `TargetCountryId`).

**Gold policy:** share conceptual `minGoldReserve` with control/baseline (`parameters.TryGetValue("minGoldReserve", …)` like `BaselineCardPlayFeature`). Unlock plays **must not** dip below reserve (`obs.Gold - card.GoldCost < minGoldReserve` ⇒ skip). Umbrella “high-EV dip” is **only** for `declare_war` / `sell_arms` (D/E).

**Playability:** trust `BotCardView.IsPlayable` (shared `ActionPlayability`); do not re-implement condition trees. Still read opinions / rivals from Child A fields to decide whether an unlock is **necessary** on a path (see §4).

### 4. Path EV scoring (unlock Δorg_score)

Unlock cards do not immediately change `org_score`. Their arbitration score is a **discounted path value** toward an estimated profitable war outcome:

\[
\text{score} = \text{unlockPathDiscount} \times (\text{unlockStepDecay})^{k-1} \times \widehat{\Delta}_{\text{war}} - \text{goldSpentFraction}
\]

Where:

- \(\widehat{\Delta}_{\text{war}}\) = expected own Δorg_score from the **terminal war path** this unlock advances (declare when peacetime + rivalry path; or `sell_arms`→natural peace when already at war on a preferred side), using the shared profit estimator (enemy control decrease, occupation/province-transfer EV, destroy EV, gold — per umbrella). Soft target bias (prefer enemy-higher-control / no-bot-control) lives inside that estimator / declare scoring, not as a hard unlock filter.
- \(k\) = number of **still-required** unlock steps on that path including this card (e.g. diplomacy improve then `make_rival` then declare-ready ⇒ `make_rival` has \(k=2\) if diplomacy already OK and declare opinion still short after rivalry… count only remaining gates this proposal clears toward the next war card). Prefer the **shortest** remaining gate chain that yields \(\widehat{\Delta}_{\text{war}} > 0\).
- `goldSpentFraction` (optional small term, default 0 or `goldCost / max(1, OrgScore scale)`): keep tiny so expensive unlocks are slightly disfavored when Δ is equal — do not let it dominate path EV. Prefer parameter `unlockGoldPenaltyWeight` default `0` in v1 unless unit tests need a knob; **do not** invent a second competing heuristic beyond discount × war Δ.

**Necessity rules (skip / do not propose):**

| Card | Propose only when |
|------|-------------------|
| `make_rival` | Target not already rival; neither side at war (declare path); \(\widehat{\Delta}_{\text{war}}\) for acting country vs `TargetCountryId` **after** rivalry would be **&gt; 0**; card `IsPlayable`. |
| `improve_diplomacy_advisor_opinion` | Diplomacy opinion &lt; 30 on acting country **and** a profitable `make_rival` path exists for some target (hand may or may not hold `make_rival` yet — still unlock so a future draw can use it; if no positive war target exists under current observation, skip). |
| `improve_military_advisor_opinion` / `improve_ruler_opinion` | Clears or advances a **binding** gate: (a) peacetime declare path where `max(ruler, mil) &lt; 50` and improving this role raises the max (or is the deficient side), with positive post-rivalry war Δ for some rival / make-rival target; **or** (b) active war where preferred-side `sell_arms` path has mil &lt; 80 and positive prosecute Δ. If opinion already meets the relevant threshold, skip. |

**Blind-grind ban:** if \(\widehat{\Delta}_{\text{war}} \le 0\) for every reachable terminal path this unlock could serve, **do not propose**. Domination by control / declare / prosecute is Child B arbitration’s job once a proposal exists.

**Parameters (feature `parameters` map):**

| Key | Default | Role |
|-----|---------|------|
| `minGoldReserve` | `0` | Same semantics as baseline; unlocks never dip |
| `unlockPathDiscount` | `0.4` | Scales terminal war Δ into unlock score (tunable in F/eval later) |
| `unlockStepDecay` | `0.7` | Per extra remaining unlock step |

### 5. Registration and profiles

- `BotFeatureRegistry.CreateDefault`: `registry.Register(WarUnlockFeature.Id, parameters => new WarUnlockFeature(parameters));`
- Do **not** enable `warUnlock` in the default `game_settings.json` bot profile (ships `control` only today) — war-capable profiles / eval candidates are Child F / scenario wiring.
- Feature id string: **`warUnlock`** (camelCase, umbrella naming).

### 6. Shared estimator touchpoint (C vs D/E)

- Call D’s `WarPeaceOrgScoreEstimator` for unlock path value (same dual-side fractions / mid-band / gold bridge as declare/prosecute). Implement order: **A → B → D → C∥E → F**.
- v1 inside C may use a **conservative** estimate (control-shift fractions from `GameSettings` peace winner/loser constants + CountryScore × control shares + gold cost of expected declare/sell_arms) sufficient to distinguish profitable vs non-profitable targets; D/E refine occupation/destroy fidelity **in the same helper**, not a fork.
- Call `WarWinChanceEstimator` only via numeric inputs from Child A country views (or the pure overload D may add) when win-% gates path value; unlock itself does not require a win-% threshold beyond “path Δ &gt; 0”, but may fold win% into \(\widehat{\Delta}\) the same way D will.

### 7. Eval package (Child F only)

- Register awareness: F creates `Docs/BotFeatures/warUnlock/` with locked knobs (`endDate` 1920-01-01, `hoursPerTick: 4`, `seedCount: 20`, control-only twin, `targetActions` listing unlock action ids).
- This plan does **not** specify twin composition, timeouts, or success-bar harness changes — only that `warUnlock` is a registered feature id F will package.
- If `/implement-bot-feature` scaffolds a default `eval_config.json`, treat it as temporary; **F’s plan wins** on locked fields.

## Agent Steps

- [ ] **Confirm A+B merged** — Child A observation fields and Child B proposal/arbitration/cadence APIs are available on the implement branch; align type/method names to B’s landed `plan.md` (do not re-spec infra here).
- [ ] **Add `WarUnlockFeature`** — `src/Game.Bots/WarUnlockFeature.cs` with `Id = "warUnlock"`, parameter reads (`minGoldReserve`, `unlockPathDiscount`, `unlockStepDecay`), deterministic hand scan, necessity + path-EV scoring per Approach §3–§4, emitting **one** best proposal into B’s pool (no direct sink play; no discard).
- [ ] **Consume D’s profit helper** — call `WarPeaceOrgScoreEstimator` (Child D) for unlock path Δ; do not add a second estimator type.
- [ ] **Register** — one line in `BotFeatureRegistry.CreateDefault` for `WarUnlockFeature`.
- [ ] **Unit tests** — `src/Game.Tests/WarUnlockFeatureTests.cs` (or equivalent) on synthetic `IBotObservation` doubles / builders per Tests below.
- [ ] **Eval stub note only** — do not implement Child F’s locked eval package here; if the implement skill scaffolds `Docs/BotFeatures/warUnlock/`, leave a short comment in the PR that F owns final knobs.
- [ ] **Run** `dotnet test` on `src/GlobalStrategy.Core.sln`, then `/dotnet-build Release` after any `src/` change.

## User Steps

### 1. None

None — `src/Game.Bots` + `Game.Tests` (+ optional docs stub) only; no Unity Editor scene, prefab, or visual inspection steps.

## Tests

Focused unit tests in `src/Game.Tests` with synthetic observations (no full 40-year eval here — that is F):

- **`war_unlock_proposes_make_rival_when_path_delta_positive`** — playable `make_rival` targeting country T; estimator returns Δ &gt; 0; proposal action/target/slot match; score = `unlockPathDiscount × Δ` (k=1).
- **`war_unlock_skips_make_rival_when_path_delta_non_positive`** — same card but Δ ≤ 0 ⇒ no proposal (blind-grind ban).
- **`war_unlock_proposes_diplomacy_opinion_when_below_make_rival_gate`** — diplomacy &lt; 30, profitable rival target exists, playable `improve_diplomacy_advisor_opinion` ⇒ proposed; diplomacy ≥ 30 ⇒ not proposed for that reason.
- **`war_unlock_proposes_mil_or_ruler_opinion_to_clear_declare_gate`** — `max(ruler,mil) &lt; 50`, positive war path, playable improver that advances the max ⇒ proposed; already ≥ 50 ⇒ skip opinion grind.
- **`war_unlock_proposes_mil_opinion_for_sell_arms_gate_when_at_war`** — active war, mil &lt; 80, positive prosecute path Δ, playable `improve_military_advisor_opinion` ⇒ proposed.
- **`war_unlock_respects_min_gold_reserve`** — affordable vs playability but `gold - cost < minGoldReserve` ⇒ no proposal (no dip).
- **`war_unlock_picks_best_candidate_deterministically`** — two positive unlock candidates; higher path score wins; equal scores → SlotIndex / ActionId / TargetCountryId ordinal tie-break.
- **`war_unlock_ignores_non_owned_action_ids`** — `declare_war` / `sell_arms` / control-raising cards in hand never appear as unlock proposals.
- **Regression:** existing bot registry / session tests still construct defaults; new id is registered and creatable.

Full suite green + Release build per workflow. Eval success bars (war cards played + beat control-only twin) are **Child F**, not a gate of this plan.

## Constitution Check

Checked against `Docs/Constitution.md`.

- *Unity 6 + URP only.* No rendering / Unity asset work.
- *ECS for all game logic in `src/`.* Feature is pure decision code over `IBotObservation` + sink/proposals; no MonoBehaviour simulation.
- *VContainer sole DI.* No new Unity container registrations; registry factory pattern unchanged.
- *UI Toolkit only.* No UI.
- *Plan before implement / Spec before plan.* Satisfied by this folder’s `spec.md` + `plan.md`. **Bot-feature carve-out** still applies to `/implement-bot-feature` (PRD + eval history as planning artifact under the standing harness spec) — this formal plan is an **additional** owner-requested artifact for Child C under the umbrella split, not a conflict with the carve-out. Observation/orchestrator changes remain outside the carve-out and stay on A/B plans.
- *File organisation.* Spec+plan under `Docs/Specs/26_08_13_09_bot-war-unlock/` (not legacy `Docs/Plans/`).
- *One `.asmdef` per Assets feature folder.* No `Assets/Scripts` changes.
- *C# code style.* Tabs, `_` private members, braces always — match `ControlFeature` / `BaselineCardPlayFeature`.

**Conflicts:** none that block the plan. The only tension is procedural: Constitution allows bot features to skip a formal Specs plan via the carve-out, but the owner explicitly requested this formal Child C plan anyway — followed as requested; implementers should still use `/implement-bot-feature` after A–B rather than ad-hoc edits.

Use the implement skill to start working on the plan or request changes.
