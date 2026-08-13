# Spec: Bot War Eval Packages (Child F)

## Feature Intent

As a developer shipping bot war features (`warUnlock`, `warDeclare`, `warProsecute`), I want committed eval packages under `Docs/BotFeatures/<featureId>/` (plus a shared full-stack package) that mirror `Docs/BotFeatures/control/`, with **locked** long-horizon knobs and a **control-only twin** merge gate, so that each war feature’s `/implement-bot-feature` loop (and the umbrella #83 first-ship bar) proves **both** that war-related actions appear in play traces **and** that the candidate beats a control-only twin on paired-seed `org_score`.

This is **Child F** of umbrella `Docs/Specs/26_08_01_09_bot-war-features/` (issue #83). It depends on Children C/D/E existing to produce meaningful war emissions, and on Child B’s decision cadence (`botDecisionIntervalHours`, default 4) aligning with eval `hoursPerTick: 4`. Observation (A) and infra (B) are prerequisites for the features under eval, not for writing configs.

**Eval endDate lock (critical):** `endDate: null` in bot-feature eval does **not** mean “scenario default / run long.” `Game.Evals` substitutes `StartYear + 5` (`Program.DefaultEndDate` → e.g. `1885-01-01` when `startYear` is 1880). War packages **must** set explicit `"endDate": "1920-01-01"`.

## Resolved Decisions (from umbrella #83)

| Topic | Decision |
|-------|----------|
| Feature folders | `Docs/BotFeatures/warUnlock/`, `warDeclare/`, `warProsecute/` (+ shared `warStack` package) |
| Horizon knobs (locked) | `endDate: "1920-01-01"`, `hoursPerTick: 4`, `seedCount: 20` — do **not** thin these if runs are heavy |
| Heavy-run response | Raise `timeoutSeconds` / harness budgets only |
| Primary merge gate | **Control-only twin**: same seeds; candidate = war feature(s) + `control`; twin = `control` only |
| Secondary opponents | Passive / `baselineCardPlay` / mirror-war **not** required for merge |
| Success bar | **(a)** war-related owned `targetActions` appear in candidate emission logs **and** **(b)** candidate **beats** twin on mean paired `org_score` delta (`mean(d_i) > 0`) |
| `targetActions` | Owned action ids only; `sell_arms` for prosecute — **never** `force_war_win` / `force_war_loss` |
| Gate metric | Paired-seed final `org_score` (existing harness) |
| Config mirror | Same JSON shape as `Docs/BotFeatures/control/eval_config.json` |

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`.

### Packages & locked knobs

- Child F is implemented and C/D/E feature ids are registered.
  - `Docs/BotFeatures/warUnlock/eval_config.json`, `warDeclare/eval_config.json`, and `warProsecute/eval_config.json` exist, camelCase, mirroring control’s schema => each package is runnable via `dotnet run --project src/Game.Evals -- --feature <featureId>`.
- Any of those configs is loaded.
  - Effective knobs are **`endDate: "1920-01-01"`**, **`hoursPerTick: 4`**, **`seedCount: 20`** => never `endDate: null` (which would silently become StartYear+5).
- A fourth shared package folder `Docs/BotFeatures/warStack/` holds an eval config for the full candidate stack vs the same twin => umbrella first-ship can prove the combined war profile without inventing a fake registered `featureId` (invoked with `--feature` set to one registered war id + `--eval-config` pointing at the shared file — see plan).

### Control-only twin & score bar (b)

- Candidate profile includes `control` plus the war feature(s) under test (and upstream war features needed for that feature to fire).
  - Baseline/twin arm is **`control` only** (all non-`control` candidate features disabled) => primary merge gate matches the umbrella, not “flip only `--feature` while leaving other war features on.”
- A completed paired batch for a war package.
  - Must-have score gate requires **`mean(d_i) > 0`** (beat twin), not merely the harness default non-regression `mean(d_i) ≥ −ε` => success bar (b) is enforceable by the CLI exit code.

### Command / play-trace bar (a)

- Candidate arm finishes without timeout across the seed set.
  - Command-on assertion passes for `--feature`: ≥1 emission attributed to that featureId matching its `targetActions` => war cards actually played.
- Twin arm (control only).
  - Command-off: zero emissions attributed to `--feature` => feature-flag gating remains sound.

`targetActions` ownership (v1):

| Package | `targetActions` |
|---------|-----------------|
| `warUnlock` | `make_rival`, `improve_military_advisor_opinion`, `improve_ruler_opinion` |
| `warDeclare` | `declare_war`, `declare_revenge_war` |
| `warProsecute` | `sell_arms` |
| `warStack` (shared) | Union of the above (no force cards) |

### Candidate feature stacks

- `warUnlock` eval candidate => `[control, warUnlock]`.
- `warDeclare` eval candidate => `[control, warUnlock, warDeclare]` (unlock upstream so declare can fire; twin still strips **all** war features).
- `warProsecute` eval candidate => `[control, warUnlock, warDeclare, warProsecute]`.
- `warStack` shared candidate => same as prosecute stack (full war + control).

### Opponents & parameters

- Non-candidate orgs use **`control` only** (enabled) as `opponentFeatures` => secondary `baselineCardPlay` is not the merge opponent.
- `parameterSearch` may declare grids for feature knobs (reserves / profit / win-% thresholds) once C/D/E expose them; `maxTotalRuns` stays high enough for `seedCount × (1 + paramSets)` without thinning locked knobs. Empty / null search is valid for a first land of configs.

### Heavy-run harness budgets

- A single headless run at 4h ticks from 1880→1920 (~40y) is ~order-of-magnitude heavier than control’s 5y/24h defaults.
  - Each war eval config sets a raised `timeoutSeconds` (plan-sized; further raises allowed) => runs fail with actionable timeout diagnostics rather than silently shrinking `seedCount` / `hoursPerTick` / `endDate`.
- Harness / CLI changes needed for twin mode, improve score gate, and budgets are covered by this spec’s plan (outside the bot-feature carve-out for `IBotFeature` code, but authorized here for `src/Game.Evals` + tests).

### Dependencies

- Eval **configs** may land before C/D/E code; eval **batches** that must pass for merge require the corresponding feature(s) registered and Child A/B behaviour available so war plays are reachable.
- Force cards remain out of scope: no `targetActions` entry and no dependence on `enableForceWarCards`.

## Out of Scope

- Implementing `warUnlock` / `warDeclare` / `warProsecute` feature logic (Children C/D/E).
- Observation extensions (A) or bot infra interval/arbitration/discard/draw (B), except consuming their shipped behaviour in evals.
- Changing locked knobs (`endDate` 1920, `hoursPerTick: 4`, `seedCount: 20`) without a new owner decision.
- Secondary merge gates (passive, `baselineCardPlay`, mirror-war opponents).
- Force-card eval coverage (`force_war_win` / `force_war_loss`).
- CI wiring of eval batches.
- Thinning seeds/horizon to fit default 300s timeouts.
- Unity / `Assets/` changes.

## Tech Notes

- Mirror file: `Docs/BotFeatures/control/eval_config.json` (`seedCount: 10`, `hoursPerTick: 24`, `endDate: null`, `timeoutSeconds: 300`, opponents `baselineCardPlay`).
- Harness today (`src/Game.Evals`): `BuildBaselineFeatures` flips only `--feature`; score must-have is `mean ≥ −ε`; `endDate: null` → `StartYear + 5`; defaults unfit for Child F without extensions + config overrides.
- Standing harness spec/plan: `Docs/Specs/26_07_16_14_bot-feature-eval-harness/`.
- Umbrella: `Docs/Specs/26_08_01_09_bot-war-features/spec.md` (Child F + eval locks).
