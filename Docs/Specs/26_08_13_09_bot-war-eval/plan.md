# Plan: Bot War Eval Packages (Child F)

## Spec

Source: `Docs/Specs/26_08_13_09_bot-war-eval/spec.md` (Child F of umbrella `Docs/Specs/26_08_01_09_bot-war-features/`, issue #83).

**Intent.** Ship eval packages for `warUnlock`, `warDeclare`, and `warProsecute` (plus a shared `warStack` config) under `Docs/BotFeatures/`, with locked long-horizon knobs and a control-only twin merge gate, so war-feature loops prove both war plays in traces and a positive paired `org_score` mean delta vs control-only.

**Binding locks (umbrella + this spec):**
- `endDate: "1920-01-01"` (never `null` — null ⇒ ConsoleRunner/Evals `StartYear + 5` ≈ 1885).
- `hoursPerTick: 4`, `seedCount: 20`.
- Primary twin: candidate = war feature(s) + `control`; baseline = `control` only.
- Success: (a) owned `targetActions` emitted on candidate under `--feature` attribution **and** (b) `mean(d_i) > 0` vs twin.
- Raise `timeoutSeconds` / budgets; do **not** thin locked knobs.
- `sell_arms` only for prosecute `targetActions` — no force cards.

**Out of scope:** C/D/E feature logic; A/B infra beyond consuming it; secondary opponents as merge gates; CI; Unity/`Assets/`; changing locked knobs without owner decision.

## Goal

1. Extend `src/Game.Evals` so war configs can declare **control-only twin** baseline construction and an **improve** must-have score gate (`mean(d_i) > 0`), with unit tests.
2. Commit `Docs/BotFeatures/{warUnlock,warDeclare,warProsecute,warStack}/eval_config.json` with locked knobs, raised timeouts, correct `candidateFeatures` / `opponentFeatures` / `targetActions`.
3. Leave C/D/E implementation and live passing batches to those children / `/implement-bot-feature`, using these configs (and not overwriting locked fields with skill defaults).

## Approach

### 1. Cost model & timeout sizing (do not thin knobs)

| Knob | Control today | War (locked) |
|------|---------------|--------------|
| `endDate` | `null` → ~1885 (5y) | **`1920-01-01`** (~40y from 1880) |
| `hoursPerTick` | 24 | **4** |
| `seedCount` | 10 | **20** |
| Approx ticks/run | ~1.8e3 | ~8.8e4 (~**48×**) |
| Arms (no search) | 20 runs | **40** runs |

**`timeoutSeconds` (per headless run):** start at **`14400`** (4h wall). If batches still exit `endReason=timeout` / exit 3, raise further (e.g. 21600 / 28800) in the same config field only — never reduce `seedCount`, `hoursPerTick`, or `endDate`.

**`maxTotalRuns`:** keep **`200`**. No-search batches use 40 runs. Parameter search must satisfy `20 × (1 + paramSetCount) ≤ 200` ⇒ at most **9** parameter sets; prefer `maxCandidates ≤ 4` on first grids.

### 2. Harness extensions (`src/Game.Evals`) — authorized by this plan

`/implement-bot-feature` may not touch `Game.Evals`; Child F’s `/implement` (or a dedicated harness PR before feature loops) lands these additive fields. Defaults preserve existing control / baselineCardPlay behaviour.

**2a. `baselineMode` (string, default `"featureOff"`)**

| Value | Baseline construction |
|-------|----------------------|
| `"featureOff"` | Today’s `BuildBaselineFeatures`: flip only `--feature` to `enabled: false` |
| `"controlOnly"` | Clone candidate list; keep `control` enabled; set **every other** feature entry `enabled: false` |

War packages set `"baselineMode": "controlOnly"`. Validation: candidate must include enabled `control`; otherwise exit 2.

Extend `EvalConfig.BuildBaselineFeatures` (or sibling `BuildControlOnlyBaseline`) and wire in `Program.cs` where baseline features are built today.

**2b. `scoreGate` (string, default `"nonRegression"`)**

| Value | Must-have |
|-------|-----------|
| `"nonRegression"` | `GateEvaluator.ScoreGatePasses(mean, ε)` — `mean ≥ −ε` (unchanged) |
| `"improve"` | `GateEvaluator.ImprovedFlag(mean)` — `mean > 0` |

War packages set `"scoreGate": "improve"`. Still compute/report ε and non-regression stats in history for diagnosis; only the must-have verdict changes.

**2c. Persistence / CLI**

- Record effective `baselineMode` and `scoreGate` in attempt history / summary.
- Stderr on gate failure names which score mode failed.
- No change to emission attribution contract for per-feature packages.

### 3. Eval config packages (mirror control schema)

Create directories + `eval_config.json` (empty `eval_history.json` `[]` and a one-line `eval_summary.md` stub optional; history may stay uncreated until first run — match control’s existing trio if convenient).

**Common fields (all war packages):**

```json
{
	"candidateOrgId": null,
	"opponentFeatures": [ { "featureId": "control", "enabled": true, "parameters": {} } ],
	"seedCount": 20,
	"baseSeed": 1880,
	"endDate": "1920-01-01",
	"hoursPerTick": 4,
	"timeoutSeconds": 14400,
	"epsilonRelative": 0.02,
	"epsilonAbsolute": 0,
	"maxTotalRuns": 200,
	"baselineMode": "controlOnly",
	"scoreGate": "improve",
	"parameterSearch": null
}
```

**Per-package differences:**

| Folder | `candidateFeatures` (enabled) | `targetActions` |
|--------|-------------------------------|-----------------|
| `warUnlock` | `control`, `warUnlock` | `make_rival`, `improve_diplomacy_advisor_opinion`, `improve_military_advisor_opinion`, `improve_ruler_opinion` |
| `warDeclare` | `control`, `warUnlock`, `warDeclare` | `declare_war`, `declare_revenge_war` |
| `warProsecute` | `control`, `warUnlock`, `warDeclare`, `warProsecute` | `sell_arms` |
| `warStack` | same as prosecute | union of unlock + declare + prosecute actions (no force ids; include diplomacy opinion) |

Parameters on feature entries: `{}` initially; when C/D/E expose tunables (`minGoldReserve`, profit / win-% thresholds, revenge weight if tunable), pin defaults here and optionally add a small `parameterSearch` grid under the run cap.

**Invoking `warStack`:** not a registered `featureId`. Run:

```
dotnet run --project src/Game.Evals -- --feature warProsecute --eval-config Docs/BotFeatures/warStack/eval_config.json
```

Command-on then asserts `warProsecute` / `sell_arms`. Umbrella bar (a) for unlock/declare remains proven by the per-feature package runs. Document this in `warStack/eval_summary.md`. Do **not** register a dummy `warStack` feature.

**Opponents:** `control` only (not `baselineCardPlay`) — secondary opponents are not merge gates.

### 4. Interaction with `/implement-bot-feature` (C/D/E)

- Child F configs are the source of truth for war eval knobs. When scaffolding C/D/E, **copy/adapt these files** — do not reset to skill defaults (`endDate: null`, `hoursPerTick: 24`, `seedCount: 10`, `timeoutSeconds: 300`, opponents `baselineCardPlay`).
- Feature logic stays under the bot-feature carve-out; harness fields above stay under this plan.
- Passing eval batches for merge require A/B shipped behaviour and the feature under test registered.

### 5. Tests (`src/Game.Tests`)

Extend synthetic eval tests (no full 40y sims in unit tests):

- `controlOnly` baseline disables all non-`control` entries; leaves `control` enabled; preserves parameters on disabled entries.
- `featureOff` path unchanged (existing `EvalBatchTests` case still passes).
- `scoreGate: "improve"` treats `mean == 0` / negative as fail; positive as pass; `nonRegression` still uses ε boundary cases in `EvalGateTests`.
- Validation: `baselineMode: "controlOnly"` without enabled `control` in candidate → treated as config error (unit-test the helper / mirror Program validation if extracted).

### 6. Explicit non-goals in implement

- No change to ConsoleRunner `DefaultEndDate` semantics (document only).
- No thinning of locked knobs; no force-card targets; no CI pipeline; no Unity assets.
- No implementing war `IBotFeature` classes in this plan’s scope.

## Agent Steps

- [ ] **Add `baselineMode` + `scoreGate` to `EvalConfig`** — Deserialize new optional strings with defaults `"featureOff"` / `"nonRegression"`. Implement `BuildControlOnlyBaseline` (or parameterized `BuildBaselineFeatures`). Validate control-only candidate includes enabled `control`.
- [ ] **Wire `Program.cs` gate + baseline selection** — Choose baseline builder from `baselineMode`; choose score must-have from `scoreGate` (`ImprovedFlag` vs `ScoreGatePasses`). Persist effective modes in history records / summary text.
- [ ] **Unit tests for twin + improve gate** — Extend `EvalBatchTests` / `EvalGateTests` (and Program validation tests if present) per Approach §5.
- [ ] **Write `Docs/BotFeatures/warUnlock/eval_config.json`** — Locked knobs; candidate `[control, warUnlock]`; unlock `targetActions`; `baselineMode` / `scoreGate` / opponents / `timeoutSeconds` per Approach §3.
- [ ] **Write `Docs/BotFeatures/warDeclare/eval_config.json`** — Candidate stack includes `warUnlock` + `warDeclare`; declare `targetActions`.
- [ ] **Write `Docs/BotFeatures/warProsecute/eval_config.json`** — Full upstream stack; `targetActions: ["sell_arms"]` only.
- [ ] **Write `Docs/BotFeatures/warStack/eval_config.json` + short `eval_summary.md`** — Full stack + union `targetActions`; document CLI invocation via `--feature warProsecute --eval-config …`.
- [ ] **Optional history stubs** — `eval_history.json` as `[]` and minimal `eval_summary.md` for the three feature folders (match `control` layout) so folders are reviewable before first attempt.
- [ ] **Run focused eval unit tests + Release build** — `dotnet-test` for `Game.Tests` filter on eval gate/batch tests; `/dotnet-build Release` after any `src/` change.
- [ ] **Do not run full 40y×20-seed war batches in this plan** unless C/D/E are already registered and A/B landed — smoke is optional; merge proof for war features belongs to those children’s eval loops using these configs.

## User Steps

None required for Child F itself (no Unity Editor / asset work). Owner may later run long eval batches on a machine with sufficient wall-clock budget after C/D/E land:

```
dotnet run --project src/Game.Evals -- --feature warUnlock
dotnet run --project src/Game.Evals -- --feature warDeclare
dotnet run --project src/Game.Evals -- --feature warProsecute
dotnet run --project src/Game.Evals -- --feature warProsecute --eval-config Docs/BotFeatures/warStack/eval_config.json
```

If runs timeout, raise `timeoutSeconds` in the relevant `eval_config.json` only.

## Tests

1. **Synthetic harness unit tests** (required) — control-only baseline construction; improve vs nonRegression score gate; defaults unchanged for omitted fields; control-only without `control` fails validation.
2. **No full-simulation unit tests** for 1920 horizon (prohibitively slow) — rely on existing HeadlessRunner + emission assertion coverage.
3. **Manual / loop acceptance** (post C/D/E) — CLI exit 0 on each package with (a) command-on for owned actions and (b) positive mean delta vs control-only twin; twin arm command-off clean for `--feature`.

## Constitution Check

- **Rendering (URP only):** No rendering/shader/camera work. No conflict.
- **Game Logic (ECS in `src/`, no state in MonoBehaviours):** No domain rules in MonoBehaviours; eval harness remains net8.0 `Game.Evals` + existing ConsoleRunner. No conflict.
- **Dependency Injection (VContainer sole DI):** No new Unity services or static mutable singletons. No conflict.
- **UI (UI Toolkit only):** No UI. No conflict.
- **Planning Discipline:** This plan precedes harness + config implement. Bot-feature *logic* remains under `/implement-bot-feature` carve-out; **`src/Game.Evals` changes are outside that carve-out and are authorized by this plan.** Eval JSON under `Docs/BotFeatures/` is the carve-out surface for configs/history. No conflict.
- **Specification Discipline:** Spec in this folder accompanies the plan. No conflict.
- **File Organisation:** `Docs/Specs/26_08_13_09_bot-war-eval/{spec,plan}.md`. No conflict.
- **Assembly Structure:** No new asmdefs; `Game.Evals` already exists. No conflict.
- **C# Code Style:** Harness/test edits follow project conventions (tabs, `_` prefix, braces). No conflict.

**Conflicts:** none.

## Dependency note

| Depends on | Why |
|------------|-----|
| Child A/B (shipped behaviour) | War plays reachable in sim |
| Child C/D/E (registered features) | Unknown `featureId` fails CLI; emissions need implementations |
| Standing harness `26_07_16_14_bot-feature-eval-harness` | Batch/gate/emission contracts |

Configs + harness tweaks may land before C/D/E; **passing** war evals cannot.

Use the implement skill to start working on the plan or request changes.
