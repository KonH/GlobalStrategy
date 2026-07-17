# Plan: Bot Feature Eval Harness & /implement-bot-feature Skill

## Spec

Source: `Docs/Specs/52_bot-feature-eval-harness/spec.md`.

**Intent.** Part 3 of 3 of the bot-org initiative: an evaluation harness that measures whether a new bot feature helps (or at least does not hurt) an org's score under paired-seed headless competition, plus an `/implement-bot-feature` skill that autonomously implements a described bot feature, runs the evals, iterates on failure within a bounded Ralph loop, and ends with a human-reviewable PR — objective, reproducible evidence instead of per-feature hand-written specs and plans.

**Resolved decisions (2026-07-15, binding):**
- **Org scoring is population-weighted from day one, and is delivered entirely by `Docs/Specs/49_org-scoring/` (`main`), not by this plan.** Org score = Σ over countries of (org control fraction × `CountryScore`), where the control fraction is the org's `ControlEffect` total in that country ÷ 100 (the per-country control cap enforced by `ControlSystem`), and `CountryScore` is spec 47's `coefficient × sum(population of owned provinces)`. Raw-control-sum scoring was rejected. The formula lives in exactly one derived, non-`[Savable]` query — `OrgScore.GetScore(IReadOnlyWorld, string orgId)` in `src/Game.Systems` — **already implemented by spec 49**, extracted there as a standalone, multi-org-independent prerequisite once it became clear scoring needs to exist before bot efficiency can be evaluated, not after. This plan consumes `OrgScore.GetScore` and surfaces it additively as a `score` field on every per-org entry in the results JSON (end metrics and every monthly timeline sample); it does not implement the query itself. All gate/statistics code treats scores as opaque comparable numbers.
- **Constitution carve-out.** `Docs/Constitution.md`'s Planning Discipline section is amended as part of this feature with an explicit exception for `/implement-bot-feature`-driven bot features (PRD + eval history as the planning artifact under this standing spec). The exact wording is proposed in §13 of this plan.

**Key acceptance criteria (design targets):**
- Paired-seed batches: candidate arm (feature `F` enabled) vs baseline arm (identical profile, `F` flipped off) over the same deterministically-derived seed set; opponents fixed (`baselineCardPlay`) and identical in both arms; baselines always run fresh in-batch, never reused across attempts/commits.
- Must-have score gate: `mean(d_i) ≥ −ε` over per-seed deltas `d_i = candidateScore(seed_i) − baselineScore(seed_i)`, with `ε = epsilonRelative × mean(baselineScore)` (default `0.02`) and an `epsilonAbsolute` floor (default `0`) for zero-baseline cases. Median/min/max/stddev/per-seed table/improved-worsened-unchanged counts are reported, never gated on. `mean(d_i) > 0` is recorded as the nice-to-have "improved" outcome. No t-tests/CIs.
- Command assertions via a new additive per-org **bot emission log** in the results JSON (`featureId`, `actionId`, `countryId`, game date — attribution by `featureId`, no wall-clock): candidate arm must contain ≥1 emission attributed to `F` (and matching `targetActions` when declared); baseline arm must contain **zero** emissions attributed to `F`.
- Parameter search: grid (full cartesian product) or random (`maxCandidates` sets, RNG seeded from `searchSeed`); baseline arm run once per batch and shared; hard cap on total runs (default 200) rejected at validation time; winner = passing set with highest mean delta, first-in-generation-order tie-break; batch passes iff ≥1 set passes; winning parameters recorded in the eval config and adopted as the feature's registration defaults; internal API shaped "parameter set in → paired delta statistics out" so a GA can slot in later. No-parameters features skip search entirely.
- `src/Game.Evals` is a new net8.0 console CLI + library (never emitted to `Assets/Plugins/Core/`, no Unity change), invoking `HeadlessRunner` in-process; exit code is the verdict (0 iff all must-have gates pass) so it serves directly as a Ralph task `gate`; unknown `featureId` fails fast before any run; any failing run (non-zero/bot exception/timeout end reason/malformed results) fails the whole batch with a diagnostic naming seed, arm, and parameter set.
- Persistence: raw per-run results under gitignored `.tmp/evals/<featureId>/attempt_<n>/…`; committed append-only `Docs/BotFeatures/<featureId>/eval_history.json` (per-attempt gates, ε, per-seed scores/deltas, stats, parameter table + winner, effective config, raw-dir pointer) plus rewritten human-readable `eval_summary.md`. History is the fresh-context loop's memory; earlier attempts are never rewritten.
- `/implement-bot-feature` skill at `.claude/commands/implement-bot-feature.md`: derives a camelCase `featureId`, creates a dedicated `ralph/…` branch, writes the eval config, writes `.ralph/prd.md` **directly** (no per-feature `/specify`/`/plan`) with honest gates (the eval CLI's exit code — never fabricated always-green gates), bounds eval attempts (default 5, independent of the driver's `MaxIterations`), iterates by reading `eval_history.json` + `.ralph/activity.md`, finishes via `/commit` + PR carrying the eval verdict, **never merges**, reports failure with full history when the budget is exhausted, and stops + directs to `/specify`+`/plan` for anything outside the bot-feature surface.
- Ralph integration: reuse `.ralph/PROMPT.md`/`prd.md`/`activity.md` conventions; `/create-prd` unchanged; a driver path exists for skill-written PRDs that skips spec-folder resolution and the `/create-prd` phase while preserving the clean-tree check, dedicated branch, bounded iterations, metrics CSV, and skip-PR-on-incomplete; `/complete-prd`-style finishing extended with the eval summary.
- Unit tests for all statistics/gate/assertion/search/failure-propagation logic on synthetic data in `src/Game.Tests` — no full simulations in unit tests.

**Out of scope:** genetic/advanced optimizers (API boundary only), any ECS `OrgScore` component or `VisualState`/UI exposure, significance testing, multi-feature interaction evals, self-merging, any `Assets/` change (Core DLL refresh from rebuilding `src/` excepted), non-additive changes to parts 1–2's contracts, cross-attempt baseline caching, CI integration, replacing `/create-prd`.

## Hard dependencies — the plan cannot start before ALL of these land

1. `Docs/Specs/50_multi-org-headless-simulation/` (spec + plan) — multi-org init, seeded RNG, headless ConsoleRunner (`HeadlessOptions`/`HeadlessRunner`), deterministic camelCase results JSON (`SimulationResult` DTOs), and `src/Game.Systems/OrgMetrics.cs` (`GetTotalControl`/`GetGold`/`GetControlByCountry` over `IReadOnlyWorld`).
2. `Docs/Specs/51_bot-org-api/` — `IBotObservation`, `IBotCommandSink` (whitelisted card play, sink-stamped `OrgId`, duplicate suppression), `BotProfile`/`BotFeatureSetting` DTOs in netstandard2.1 `src/Game.Bots`, `--bot` runner attachment with per-tick decision phases in participating-org order, the `featureId` registry with fail-fast on unknown ids, per-org results-JSON bot config, and the `baselineCardPlay` built-in feature. (Plan 51 was authored in parallel; this plan relies on spec 51 only and defines its own additions — §4 — as minimal additive hooks that do not constrain plan 51's internals.)
3. `Docs/Specs/49_org-scoring/` (`main`, merged independently of this initiative) — `src/Game.Systems/OrgScore.cs`, `public static double GetScore(IReadOnlyWorld world, string orgId)`, the population-weighted formula described in the Resolved Decisions above. Transitively built on the already-merged `Docs/Specs/47_country-scoring/` (`CountryScoreSystem`, `countryScoreCoefficient`) and `Docs/Specs/46_province-population/` — this plan does not depend on 46/47 directly, only on spec 49's already-delivered `OrgScore.GetScore`, and must not re-derive or duplicate that aggregation.

## Goal

Add the single population-weighted org-score query (`OrgScore.GetScore`) to `src/Game.Systems` and surface it as an additive `score` field in the headless results JSON (end metrics + every monthly timeline sample); extend the results JSON with a deterministic per-org bot emission log fed by a minimal sink hook; build the `src/Game.Evals` net8.0 CLI/library that runs paired-seed candidate-vs-baseline batches in-process (with optional grid/random parameter search under a run cap), evaluates the explicit-ε paired-mean score gate plus both command assertions, and persists raw runs to `.tmp/evals/…` and committed attempt history to `Docs/BotFeatures/<featureId>/`; add the `/implement-bot-feature` skill that scaffolds a bot-feature branch, eval config, and directly-written Ralph PRD with the eval CLI as an honest gate; add a `-BotFeature` mode to `scripts/ralph.ps1` (plus a matching `/complete-prd` argument form) so skill-written PRDs loop under the existing safeguards; and amend `Docs/Constitution.md` with the user-approved carve-out. All simulation-side code lives in `src/`; unit tests on synthetic data in `src/Game.Tests` plus one end-to-end smoke batch are the acceptance gates.

## Approach

### 1. Dependency ordering and merge preflight

Implementation starts only after two independent chains have merged: (a) `main`'s `46 → 47 → 49` (province population → country scoring → org scoring — org scoring depends only on 47, not on multi-org) and (b) this branch's `50 → 51` (multi-org → bot org API). The two chains have no ordering constraint relative to each other; both must simply be complete before this plan starts. The first implementation step verifies the concrete symbols this plan builds on: `OrgScore.GetScore` (49 — **not** `CountryScoreSystem.GetScore`/`CountryScore` directly, see §2), `OrgMetrics.GetControlByCountry`, `HeadlessRunner`/`HeadlessOptions`/`SimulationResult` (50), `src/Game.Bots` with the sink/profile/registry types (51), and the `baselineCardPlay` feature. If any is missing or shaped differently, stop and reconcile before writing code — do not code around a drifted dependency.

### 2. `OrgScore` query — already delivered by spec 49, not built here

An earlier draft of this plan built `src/Game.Systems/OrgScore.cs` itself. **That is no longer this plan's job.** `Docs/Specs/49_org-scoring/` (`main`) already delivers exactly this:

```csharp
namespace GS.Game.Systems {
	public static class OrgScore {
		public static double GetScore(IReadOnlyWorld world, string orgId);
	}
}
```

— a derived query with no ECS component, nothing `[Savable]`, no `VisualState` exposure, reading already-computed `CountryScore` values plus the org's `ControlEffect`s in a single pass (avoiding the O(countries²) trap of calling a per-country linear scan once per country), returning `0.0` for zero control or control-only-in-zero-score-countries, never throwing. This plan's dependency preflight (§1) confirms this exact symbol exists with this exact signature; if spec 49 shaped it differently by the time this plan is implemented (e.g. a different namespace or an extra parameter), stop and reconcile rather than silently adapting around a drift. This plan must **not** duplicate spec 49's `ControlEffect`/`CountryScore` aggregation logic — it only calls `OrgScore.GetScore`.

### 3. Results JSON `score` field (`src/Game.ConsoleRunner`)

Plan 50's per-org DTO (`SimulationResult`'s org entry, used by both end metrics and timeline samples) gains `public double Score { get; set; }` → serialized `score` (camelCase policy already in place). `HeadlessRunner` computes it via `OrgScore.GetScore(logic.World, orgId)` (spec 49's query, per §2) at exactly the existing sampling moments: the t0 baseline sample, each month-boundary sample taken after `logic.Update` (valid: `CountryScoreSystem.Update` recomputes inside that same `Update`, after population growth), plus the end-metrics block. Note on t0: plan 50's t0 sample is taken before the first `logic.Update`, i.e. before `InitSystem` (and spec 47's init-time `Recompute`) has run — at that moment `OrgScore.GetScore` legitimately returns `0.0` for every org (no `CountryScore` entities, no control), matching the t0 control/gold zeros. This is accepted, not fixed: gates read end metrics only, and the t0 sampling moment is part 1's contract. Verify the actual sampling moment at dependency preflight; if 50 landed t0 *after* the first `Update`, the init-time `Recompute` makes t0 scores non-zero — either way, no change to part 1. Additive only — part 1/2-shaped consumers ignore the extra field; determinism is preserved (score derives only from world state). If `HeadlessRunner` does not already expose the world read-only, add an `IReadOnlyWorld` accessor on `GameLogic` usage it already has (plan 50's runner reads world state for `OrgMetrics`; reuse the same access path).

### 4. Bot emission log — hook contract and schema

**Hook (additive to spec 51's surface, not a change to it).** The bot-facing `IBotCommandSink` interface is untouched. The sink *implementation* gains one optional constructor parameter:

```csharp
// src/Game.Bots — netstandard2.1, no JSON dependency:
public delegate void BotEmissionCallback(string actionId, string countryId);
```

The sink invokes the callback synchronously for every **accepted** play — i.e. after duplicate suppression, at the moment the `PlayCardActionCommand` is actually pushed (`countryId` = `""` for org cards). Suppressed duplicates are not logged: the log records commands actually emitted. `null` callback (Unity host, no-log runs) = no-op — spec 51's contract is unchanged for every existing caller.

**Attribution.** Spec 51 fixes that features are ticked one at a time — but per plan 51 that loop lives in `Bot.ExecuteDecisionTick` (`src/Game.Bots`), not in the ConsoleRunner. `Bot` therefore gains one additive member: `public string CurrentFeatureId { get; private set; }`, set to `feature.FeatureId` immediately before each `feature.Tick(...)` and reset to `""` after the loop. The ConsoleRunner's emission-callback closure captures the `Bot` instance and stamps each emission with `{ orgId, bot.CurrentFeatureId, actionId, countryId, gameDate, tick }` (`gameDate` = `yyyy-MM-dd` from `GameTime.CurrentTime`; `tick` = the runner's tick counter). No wall-clock anywhere. The callback fires synchronously inside `Tick`, so `CurrentFeatureId` can never mis-attribute across features.

**Schema (additive).** `SimulationResult` gains a top-level `botEmissions` array, per-org in participating-list order, entries in chronological emission order:

```json
"botEmissions": [
	{
		"orgId": "Illuminati",
		"emissions": [
			{ "featureId": "baselineCardPlay", "actionId": "discover_country", "countryId": "", "date": "1880-02-14", "tick": 44 }
		]
	}
]
```

Runs without bots omit the property (or emit `[]`) — part 1/2-shaped output remains valid. Collection lives in a small `BotEmissionLog` class in `Game.ConsoleRunner` (net8.0). Determinism: same seed/config/profiles ⇒ element-wise identical logs (bot decisions are deterministic per spec 51's gate; date/tick derive from the sim).

### 5. `src/Game.Evals` project

New net8.0 project (console CLI + library), added to `GlobalStrategy.Core.sln`, never emitted to `Assets/Plugins/Core/`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net8.0</TargetFramework>
		<Nullable>enable</Nullable>
		<LangVersion>latestMajor</LangVersion>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="../Game.ConsoleRunner/Game.ConsoleRunner.csproj" />
	</ItemGroup>
</Project>
```

(`Game.Bots`, `Game.Systems`, `Game.Main` arrive transitively through `Game.ConsoleRunner`, which `Game.Tests` already references per plan 50 — referencing a net8.0 exe from net8.0 projects is supported. `System.Text.Json` is fine here, as in ConsoleRunner.)

**CLI surface:**

```
dotnet run --project src/Game.Evals -- --feature <featureId> [--eval-config <path>] [--out-dir <path>]
```

- `--feature` (required): the `featureId` under evaluation. Validated against the `src/Game.Bots` registry **before any run**; unknown → exit 2 with a stderr diagnostic (matching spec 51's fail-fast posture).
- `--eval-config` (optional): path to the eval config JSON. Default: `Docs/BotFeatures/<featureId>/eval_config.json`. An explicitly-passed path that does not exist → exit 2. The default path missing → proceed with all defaults and a stderr note (a new feature can be evaluated with no config at all).
- `--out-dir` (optional): raw-results root for this attempt. Default: `.tmp/evals/<featureId>/attempt_<n>` where `n` = (max attempt in `eval_history.json`) + 1, or `1`.

**In-process invocation.** `Game.Evals` builds one `HeadlessOptions` request per run (seed, orgs, config dir, end date, hours per tick, timeout, bot profile paths) and calls the same `HeadlessRunner` entry `Program.Main` uses — in-process, no per-run process spawn. Plans 50/51 as written have the runner consume profile *file paths* and write the results JSON itself, so the expected shape is: `Game.Evals` writes the effective profile JSONs into `<attempt>/profiles/` (needed anyway, for auditability), passes their paths, points `--output`-equivalent at the per-run file in the attempt dir, and reads the written `SimulationResult` back. If landing 50/51 exposed (or trivially permits adding) an additive `HeadlessOptions → SimulationResult` in-memory entry, prefer it — but that refactor is this plan's to make, additively, not a precondition on parts 1–2.

**Files:**

| File | Responsibility |
|---|---|
| `Program.cs` | CLI parse, validation, wiring, exit code |
| `EvalConfig.cs` | config DTOs + defaults + `Load(path)` + validation |
| `SeedDerivation.cs` | `Seeds(baseSeed, count)` → `baseSeed + i` for `i ∈ [0, count)` (documented, trivial, deterministic) |
| `ParameterSearch.cs` | `IEnumerable<ParameterSet>` from config: grid/random enumerators + run-cap validation |
| `BatchRunner.cs` | arm/seed orchestration over an injected `Func<RunRequest, RunOutcome>` (real = HeadlessRunner; tests = synthetic) |
| `GateEvaluator.cs` | `DeltaStatistics` (mean, median, min, max, stddev, per-seed table, improved/worsened/unchanged) + ε gate |
| `EmissionAssertions.cs` | command-on / command-off checks over emission logs, `targetActions` matching |
| `EvalPersistence.cs` | raw run files, `eval_history.json` append, `eval_summary.md` rewrite |

The `BatchRunner` delegate seam is the "parameter set in → paired delta statistics out" boundary the spec requires for a future GA: the enumerator (`ParameterSearch`) is swappable without touching evaluation, gates, or persistence.

**Exit-code contract** (`0` iff all must-have gates pass; designed to be a Ralph task `gate` verbatim):

| Code | Meaning |
|---|---|
| `0` | pass — score gate + command-on + command-off all hold (for ≥1 parameter set when search is active) |
| `1` | gates evaluated and failed — failing gate(s) named on stderr (`score gate`, `command assertion (on)`, `command assertion (off)`, per parameter set) |
| `2` | validation error before any run — unknown `featureId`, malformed/missing explicit config, run cap exceeded, candidate org not in `organizations.json`, or an explicitly-declared `candidateFeatures` that omits `<featureId>` or declares it `enabled: false` (the candidate arm would be indistinguishable from the baseline arm) |
| `3` | run failure mid-batch — non-zero/in-process exception, `endReason == "timeout"`, or malformed results; stderr names the seed, arm, and (when applicable) parameter set index |

`eval_history.json`/`eval_summary.md` are written for outcomes `0` and `1` (a completed, evaluated batch — pass or fail — is iteration memory); `2`/`3` write nothing to the committed history (nothing comparable was measured) and rely on stderr + `.ralph/activity.md`.

### 6. Eval config JSON schema (`Docs/BotFeatures/<featureId>/eval_config.json`)

CamelCase, all fields optional (defaults in comments):

```json
{
	"candidateOrgId": null,
	"opponentFeatures": [ { "featureId": "baselineCardPlay", "enabled": true, "parameters": {} } ],
	"candidateFeatures": null,
	"seedCount": 10,
	"baseSeed": 1880,
	"endDate": null,
	"hoursPerTick": 24,
	"timeoutSeconds": 300,
	"epsilonRelative": 0.02,
	"epsilonAbsolute": 0,
	"maxTotalRuns": 200,
	"targetActions": [],
	"parameterSearch": null
}
```

- `candidateOrgId` default: first org in `organizations.json` (config order). Participating orgs: all orgs in `organizations.json`, matching part 1's default.
- `candidateFeatures` default: `[ baselineCardPlay enabled (default parameters), <featureId> enabled (its registered default parameters) ]`; deduplicated if `<featureId>` **is** `baselineCardPlay`. The baseline arm is byte-identical except `<featureId>`'s `enabled` flipped to `false`.
- `opponentFeatures`: the profile every non-candidate org gets — identical in both arms and across all seeds.
- `endDate` default: 5 game years from the configured start (`game_settings.json` start year 1880 → `"1885-01-01"`); `hoursPerTick`/`timeoutSeconds` pass through to the runner (subject to its own `[1, 672]` guard).
- `parameterSearch` shape (per the spec):

```json
"parameterSearch": {
	"mode": "grid",
	"parameters": {
		"minGoldReserve": { "values": [0, 50, 100] },
		"aggression":     { "min": 0.1, "max": 0.9, "step": 0.2 }
	},
	"maxCandidates": 20,
	"searchSeed": 7
}
```

The CLI resolves every default and records the **effective** config (fully expanded) in the history entry, so an attempt is reproducible from its history record alone.

### 7. Batch orchestration, seeds, parameter search

- **Seeds:** `seed_i = baseSeed + i`, `i ∈ [0, seedCount)`.
- **Arms:** the baseline arm runs once per batch (feature-off is parameter-independent) — `seedCount` runs; each parameter set gets a candidate arm over the same seeds. Total runs = `seedCount × (1 + paramSetCount)`; validated against `maxTotalRuns` (default 200) **before** any run starts (exit 2 on violation). No search → `paramSetCount = 1` (the config's declared/default parameters; no degenerate search machinery).
- **Grid:** cartesian product over parameter names in **ordinal name order**, each parameter's values in declared order (`{min,max,step}` expands to `min, min+step, … ≤ max` before enumeration). Generation order is the deterministic tie-break order.
- **Random:** `maxCandidates` sets sampled with `new Random(searchSeed)` (never unseeded), each parameter drawn uniformly from its expanded value list, parameters consumed in ordinal name order (pins RNG stream). Duplicates allowed (documented; the cap bounds cost either way).
- **Run order:** baseline seeds ascending, then parameter sets in generation order × seeds ascending — fixed for reproducible logs, though results are order-independent.
- **Failure propagation:** any run failing (per §5's exit-3 causes) aborts the batch immediately with the seed/arm/parameter-set diagnostic; a partial batch never produces a verdict.

### 8. Gates and statistics

Per parameter set `p`, with per-seed final scores from the candidate arm `c_i` and shared baseline arm `b_i` (the `score` field of each run's end metrics — the timeline `score` samples are persisted for diagnosis but never gated on):

- `d_i = c_i − b_i`; `ε = Math.Max(epsilonRelative × mean(b), epsilonAbsolute)` — the `max` makes `epsilonAbsolute` an explicit floor and covers `mean(b) == 0` exactly as the spec requires (scores are non-negative by construction: control ≥ 0, `CountryScore` ≥ 0).
- **Must-have score gate:** `mean(d) ≥ −ε`. Boundary is inclusive.
- **Must-have command-on (per candidate arm/parameter set):** ≥1 emission with `featureId == F` across the set's runs; when `targetActions` is non-empty, ≥1 such emission with `actionId ∈ targetActions`. Violation = "feature never acted" failure even if the score gate passed.
- **Must-have command-off (batch-level, baseline arm):** zero emissions with `featureId == F` in any baseline run. Violation = feature-flag gating bug (spec 51 forbids instantiating a disabled feature); fails the batch.
- **Reported, never gated:** median, min, max, sample standard deviation, per-seed delta table, counts of improved (`d_i > 0`) / worsened (`d_i < 0`) / unchanged (`d_i == 0`).
- **Nice-to-have:** `improved = mean(d) > 0`, recorded in the attempt summary and PR body.
- **Winner:** among passing sets, highest `mean(d)`; tie → first in generation order. Batch passes iff ≥1 set passes.
- No t-tests, confidence intervals, or significance machinery — this is the entire v1 statistical contract.

### 9. Results persistence & iteration memory

- **Raw (gitignored, never committed):** `.tmp/evals/<featureId>/attempt_<n>/baseline_seed<seed>.json`, `candidate_seed<seed>.json` (no search) or `candidate_p<k>_seed<seed>.json` (`k` = generation index), plus `profiles/` (the effective profile JSONs per arm) — full part 1/2/3-shaped results including score timelines and emission logs, for trajectory-level diagnosis.
- **Committed `Docs/BotFeatures/<featureId>/eval_history.json`:** append-only JSON array; one entry per completed batch:

```json
{
	"attempt": 3,
	"date": "2026-07-15",
	"verdict": { "pass": false, "scoreGate": false, "commandOn": true, "commandOff": true },
	"improved": false,
	"epsilon": 1.84,
	"effectiveConfig": { "…fully expanded §6 config…" },
	"parameterSets": [
		{
			"index": 0,
			"parameters": { "minGoldReserve": 50 },
			"scoreGatePass": false,
			"commandOnPass": true,
			"stats": { "mean": -2.1, "median": -1.7, "min": -6.0, "max": 0.4, "stdDev": 1.9, "improved": 2, "worsened": 7, "unchanged": 1 },
			"perSeed": [ { "seed": 1880, "baselineScore": 91.2, "candidateScore": 89.6, "delta": -1.6 } ]
		}
	],
	"winner": null,
	"rawRunDir": ".tmp/evals/<featureId>/attempt_3"
}
```

  Earlier entries are never rewritten or deleted (`attempt` strictly increases). The `date` is a human-facing calendar date in the committed doc, not part of any determinism contract (results JSON stays wall-clock-free).
- **Committed `Docs/BotFeatures/<featureId>/eval_summary.md`:** rewritten each attempt — latest verdict per gate, ε, stats table, parameter-set comparison table + winner, improved flag, raw-dir pointer, attempt count. This pair is what a fresh-context Ralph iteration (and the human reviewer) reads; that is why it is committed with the feature rather than left in `.tmp/`.

### 10. `/implement-bot-feature` skill (`.claude/commands/implement-bot-feature.md`)

New command file; `$ARGUMENTS` = natural-language feature description. The workflow it encodes:

1. **Authority statement (top of file):** this skill is the sanctioned autonomous path for **bot features only** — `IBotFeature` implementations in `src/Game.Bots`, their registrations, their `Docs/BotFeatures/<featureId>/` eval configs/history, and the `.ralph` PRD — with `Docs/Specs/52_bot-feature-eval-harness/` (spec + this plan) as the standing spec/plan and the per-feature PRD as the planning artifact (per the Constitution's Planning Discipline carve-out, §13). Any change outside that surface — game systems, observation facade, sink whitelist, runner, `Game.Evals` itself, Unity assets — is out of authority: **stop and direct the user to `/specify` + `/plan`.**
2. **Derive `featureId`:** camelCase per spec 51's naming (e.g. "target countries where our opinion is highest" → `opinionTargeting`). If already registered in `src/Game.Bots`, stop and report — no silent overwrite.
3. **Branch:** clean tree required; create/switch to `ralph/bot_<featureId>` (the same name the driver resolves, §11).
4. **Write `Docs/BotFeatures/<featureId>/eval_config.json`** (§6 schema): infer `targetActions` (action ids from `Assets/Configs/action_config.json` the description implies) and `parameterSearch` ranges from any tunables the description names; keep everything else at defaults unless the description says otherwise.
5. **Write `.ralph/prd.md` directly** (no `/specify`, no `/plan`, no `/create-prd`), using the existing task shape `{category, description, steps, gate, passes}`, and reset `.ralph/activity.md` to its header. Tasks, in order:
   1. Implement `<FeatureId>Feature : IBotFeature` in `src/Game.Bots` behind its flag with the declared parameters, plus focused unit tests of its decision logic on synthetic observations — gate: `dotnet test src/GlobalStrategy.Core.sln`.
   2. Register the `featureId` in the registry — gate: `dotnet test src/GlobalStrategy.Core.sln`.
   3. Run the eval batch — gate: `dotnet run --project src/Game.Evals -- --feature <featureId>` (the CLI's exit code **is** the verdict; never a fabricated always-green gate). When scaffolding, the skill records the current attempt count from `eval_history.json` (0 for a new feature) into this task's `steps` as `baseAttempt`. Task `steps` encode the improvement loop: on failure, read `Docs/BotFeatures/<featureId>/eval_history.json` and `.ralph/activity.md`, pick **one** concrete improvement (logic change, different targeting, adjusted parameter ranges in the eval config), journal it, and re-run; if `eval_history.json` holds **baseAttempt + 5** or more attempts and the gate still fails, journal budget exhaustion, leave `"passes": false`, and end the iteration — the 5-attempt budget (attempts made by *this* run, history stays append-only across runs) lives here in the PRD (and is restated in the skill), independent of the driver's `MaxIterations`.
   4. Adopt the winning parameters (only when parameter search was active and a winner exists): update the feature's default parameter values in its `src/Game.Bots` registration and pin them in `eval_config.json` — gate: `dotnet test src/GlobalStrategy.Core.sln`.
6. **Commit the scaffolding** (eval config + PRD + activity reset) via the `/commit` skill's rules, then print the driver command for the user to run in a terminal: `.\scripts\ralph.ps1 -BotFeature <featureId>`. Deliberate plan decision: the skill does **not** spawn the loop itself — `ralph.ps1` launches fresh-context `claude -p` processes and is designed to be driven from the shell, exactly as the spec-driven flow already works.
7. **Finish/failure semantics (stated in the skill, executed by the driver's phases):** all gates pass → `/commit` + PR via the `/complete-prd bot:<featureId>` flow (§12), PR body carrying the eval verdict; the skill/loop **never merges** — human review gates merge, because evals cannot judge metric-gaming or gameplay feel. Budget exhausted → no "done" PR; commit the committed-artifact state reached on the branch, report which gates failed on which attempts (from `eval_history.json`), and leave branch/history/raw `.tmp` results for a human; nothing is force-reverted.

### 11. Ralph driver path: `scripts/ralph.ps1 -BotFeature <featureId>`

A **mode on the existing script** (not a sibling script — the loop/metrics/permission machinery is shared and must not fork):

- Params: `[int]$SpecIndex` loses `Mandatory`; new `[string]$BotFeature`. Validation: exactly one of `-SpecIndex`/`-BotFeature` must be given.
- Bot mode differences:
  - **No spec-folder resolution and no `plan.md` requirement** — instead require `.ralph/prd.md` to exist and contain at least one `"passes": false` task (it was written by `/implement-bot-feature`); `-SkipCreatePrd` semantics are implicit and the `/create-prd` phase is skipped entirely (`/create-prd` itself is unchanged, still spec-driven).
  - Branch: `ralph/bot_<featureId>` (same clean-tree check and create-or-switch logic as today).
  - Metrics: `.ralph/metrics_bot_<featureId>.csv` (same columns, gitignored).
  - Phase 3: `/complete-prd bot:<featureId>` — still only when the loop succeeded (`complete_promise`/`all_tasks_passed`); the skip-PR-on-incomplete rule is preserved verbatim.
  - Incomplete path (loop did not finish): the existing "To create one anyway, run:" hint must print the bot form — `claude -p "/complete-prd bot:<featureId>"` — and additionally name the failure report the spec requires: `Docs/BotFeatures/<featureId>/eval_summary.md` + `eval_history.json` (which gates failed on which attempts) and `.ralph/activity.md`. These committed files ARE the failure report; the driver only has to point at them, since every loop iteration already committed them.
- Shared change for both modes: add `"Bash(dotnet run:*)"` / `"PowerShell(dotnet run *)"` to `$loopTools` so the eval gate can execute inside loop iterations without a permission stall (spec-driven runs are unaffected — it is an allowlist superset).
- Everything else — `MaxIterations` bound, `PROMPT.md` iteration contract, `acceptEdits` permission mode, totals — is untouched.

### 12. `/complete-prd` extension (`.claude/commands/complete-prd.md`)

`$ARGUMENTS` accepts either a spec index (existing behaviour, unchanged) **or** `bot:<featureId>`. In the bot form:

- The "spec folder" for the report is the standing `Docs/Specs/52_bot-feature-eval-harness/`.
- The PR body's Ralph section is followed by an `## Eval verdict` section sourced from `Docs/BotFeatures/<featureId>/eval_history.json` + `eval_summary.md`: gate outcomes (score / command-on / command-off), mean and median per-seed delta vs the ε used, whether the nice-to-have improvement (`mean > 0`) was achieved, winning parameters (or "no search"), attempt count, and a link to `Docs/BotFeatures/<featureId>/`.
- All existing rules stay: `/commit` skill for leftovers, no `passes` flips, blockers quoted verbatim.

### 13. Constitution amendment (`Docs/Constitution.md`, Planning Discipline)

Append one bullet under **Planning Discipline**, immediately after the existing "Plan before implement" bullet — exact proposed wording:

> - **Bot-feature carve-out.** Bot features implemented via `/implement-bot-feature` — `IBotFeature` implementations in `src/Game.Bots`, their registrations, and their `Docs/BotFeatures/` eval configs and history — use the skill's directly-written PRD plus the committed eval history as their planning artifact, under the standing spec/plan pair `Docs/Specs/52_bot-feature-eval-harness/`. Everything outside that surface still requires its own approved plan.

This keeps "Plan before implement" literally true (the standing plan is this document; the per-feature planning artifact is the PRD + eval history) and was explicitly user-approved on 2026-07-15 per the spec's Resolved Decisions.

### 14. What deliberately does NOT change

- `src/Game.Bots`' bot-facing surface (`IBotFeature`, `IBotObservation`, `IBotCommandSink` interface, profile schema, whitelist) — the only additions are the optional emission callback on the sink *implementation* and the read-only `Bot.CurrentFeatureId` host-facing property (§4); nothing bot-facing changes.
- Parts 1–2's results JSON fields — `score` and `botEmissions` are purely additive.
- `/create-prd`, `.ralph/PROMPT.md`, `.ralph/prd.md` template — unchanged; only the driver and `/complete-prd` gain the bot mode.
- Anything under `Assets/` — no scenes, prefabs, scripts, asmdefs, configs, `VisualState`; the only side effect is the usual Core DLL refresh from rebuilding `src/` (the `OrgScore` addition to `Game.Systems`). `Game.Evals`, like ConsoleRunner, never ships to `Assets/Plugins/Core/`.
- Save system, `VisualState`, Unity runtime behaviour — org score has no ECS component and no in-game exposure (explicitly out of scope).

## Steps

### Agent Steps

- [x] **Dependency preflight** — Verify all three hard dependencies have merged and the exact symbols exist: `OrgScore.GetScore` (49 — org scoring, delivered independently of multi-org), `OrgMetrics`/`HeadlessRunner`/`HeadlessOptions`/`SimulationResult` (50 — multi-org), `src/Game.Bots` sink/profile/registry + `baselineCardPlay` (51 — bot org API). Any mismatch → stop and reconcile with the user before coding.

- [x] **Confirm `OrgScore.GetScore` already exists** — per §2, this is delivered by `Docs/Specs/49_org-scoring/` on `main`, not built by this plan. Verify `src/Game.Systems/OrgScore.cs`'s `GetScore(IReadOnlyWorld, string orgId)` signature matches what §2/§3 assume before wiring the results-JSON `score` field to it; do not create a second implementation.

- [x] **Surface `score` in results JSON** — Extend the per-org DTO in `src/Game.ConsoleRunner/SimulationResult.cs` with `Score`; compute via `OrgScore.GetScore` in `HeadlessRunner` at the t0 sample, every month-boundary sample, and the end-metrics block (§3).

- [x] **Add the emission hook and log** — `src/Game.Bots`: add `BotEmissionCallback` and the optional sink-implementation constructor parameter invoked on every accepted (post-duplicate-suppression) play (§4). `src/Game.ConsoleRunner`: `BotEmissionLog` collection in the bot host loop stamping `orgId`/current `featureId`/game date/tick; additive `botEmissions` section in `SimulationResult`, omitted for bot-less runs.

- [x] **Create `src/Game.Evals` (project + config + seeds)** — New csproj per §5, added to `GlobalStrategy.Core.sln`; `EvalConfig.cs` DTOs with §6 defaults/validation; `SeedDerivation.cs` (`baseSeed + i`).

- [x] **Parameter search and batch orchestration** — `ParameterSearch.cs` (grid cartesian product in ordinal-name/declared-value order, `{min,max,step}` expansion, seeded random sampling, `maxTotalRuns` pre-validation) and `BatchRunner.cs` (baseline-once-per-batch, candidate arm per set, fixed run order, fail-fast propagation with seed/arm/set diagnostics, injected run delegate for testability) per §7.

- [x] **Gates, assertions, statistics** — `GateEvaluator.cs` (`DeltaStatistics`, `ε = max(epsilonRelative × mean(b), epsilonAbsolute)`, inclusive `mean(d) ≥ −ε` gate, improved flag) and `EmissionAssertions.cs` (command-on per set incl. `targetActions`, command-off batch-level) per §8.

- [x] **Persistence and CLI** — `EvalPersistence.cs` (raw run files + `profiles/` under the attempt dir, append-only `eval_history.json`, rewritten `eval_summary.md` per §9) and `Program.cs` (CLI surface, default-config resolution, attempt numbering, in-process `HeadlessRunner` invocation, §5 exit-code contract with named-gate stderr diagnostics).

- [x] **Unit tests** — Implement the Tests section below (`Game.Tests` gains a `ProjectReference` to `Game.Evals`); run `dotnet test src/GlobalStrategy.Core.sln` until green.

- [x] **Write the `/implement-bot-feature` skill** — `.claude/commands/implement-bot-feature.md` encoding the full §10 workflow: authority boundary, `featureId` derivation, `ralph/bot_<featureId>` branch, eval-config authoring, direct PRD writing with the four honest-gated tasks and the 5-attempt budget, `/commit` scaffolding commit, driver hand-off, never-merge and failure-reporting rules.

- [x] **Add the `-BotFeature` driver mode** — Edit `scripts/ralph.ps1` per §11: parameter validation (exactly one of `-SpecIndex`/`-BotFeature`), skip spec resolution + `/create-prd` in bot mode (require an existing PRD with open tasks), `ralph/bot_<featureId>` branch, `metrics_bot_<featureId>.csv`, phase-3 `/complete-prd bot:<featureId>`, and add `dotnet run` to `$loopTools`; spec-index behaviour byte-for-byte preserved.

- [x] **Extend `/complete-prd`** — `.claude/commands/complete-prd.md`: accept `bot:<featureId>`, standing-spec resolution, `## Eval verdict` PR-body section per §12.

- [x] **Amend the Constitution** — Append the §13 bullet verbatim to `Docs/Constitution.md`'s Planning Discipline section.

- [x] **Release build** — `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up the changed `Game.Systems` (and any `Game.Bots` refresh); confirm `Game.Evals`/`Game.ConsoleRunner` outputs do not land in `Assets/Plugins/Core/`.

- [x] **End-to-end smoke batch** — Write a tiny throwaway eval config (per `temp_scripts.md`, under `.tmp/`): `candidateFeatures = [ { "featureId": "baselineCardPlay", "enabled": true } ]`, `seedCount: 3`, `endDate: "1881-01-01"`, no search. Run `dotnet run --project src/Game.Evals -- --feature baselineCardPlay --eval-config .tmp/smoke_eval_config.json`. Verify: exit code reflects the gates (candidate = baseline bot acting vs passive baseline arm — expect command-on satisfied, command-off satisfied, score gate evaluated on real paired deltas); 6 raw result files + profiles under `.tmp/evals/baselineCardPlay/attempt_1/`, each with `score` fields and a candidate-arm `botEmissions` log; `Docs/BotFeatures/baselineCardPlay/eval_history.json` gained exactly one entry and `eval_summary.md` matches it; re-run and confirm attempt 2 appends without rewriting attempt 1 and per-run raw results are byte-identical for metric values. Then verify the unknown-feature path: `--feature doesNotExist` exits 2 before any run. Delete the throwaway config **and** delete `Docs/BotFeatures/baselineCardPlay/` before committing — these entries are smoke artifacts of a temporary config, not real eval memory; removing the whole directory pre-commit is the sanctioned exception to the append-only rule (which governs committed history only).

### User Steps

### 1. Confirm a clean Unity import

Let Unity reload the rebuilt `Assets/Plugins/Core/*.dll` and check `read_console(types=["error"])` — no Unity-side source or asset changed, so only the DLL refresh should be visible.

### 2. Approve the Constitution amendment text

Review the new Planning Discipline bullet in `Docs/Constitution.md` (§13 wording) — it is a governance change and should be read, not skimmed.

### 3. Dry-run the skill path end to end

Run `/implement-bot-feature` with a simple description (e.g. a variant card-usage rule), let it scaffold branch/config/PRD, then run `.\scripts\ralph.ps1 -BotFeature <featureId>` from a terminal and review the resulting PR's eval verdict section before deciding on merge.

## Tests

Test project: `src/Game.Tests/` (xunit, snake_case `[Fact]` names). All harness tests run on synthetic data via `BatchRunner`'s injected run delegate and plain DTOs — **no full simulations in unit tests**; the smoke batch in Steps is the only real-sim check.

- **`src/Game.Tests/OrgScoreTests.cs`** — already exists, added by `Docs/Specs/49_org-scoring/plan.md` (`org_score_is_control_fraction_times_country_score_summed`, `org_with_no_control_anywhere_scores_zero`, `control_in_zero_score_countries_scores_zero`, `multiple_control_effects_in_one_country_sum_before_weighting`, plus a composition-invariant check). This plan does not duplicate that coverage — its own tests below assume `OrgScore.GetScore`'s correctness is already established and focus on this plan's own additions (the results-JSON `score` field, the gates, the harness, the skill).

- **New `src/Game.Tests/EvalGateTests.cs`:**
  - `mean_delta_exactly_at_negative_epsilon_passes` and `mean_delta_below_negative_epsilon_fails` — the inclusive ε boundary.
  - `epsilon_scales_with_baseline_mean` — same deltas pass with a large baseline mean and fail with a small one under the same `epsilonRelative`.
  - `epsilon_absolute_floor_applies_when_baseline_mean_is_zero` — `mean(b) = 0`, `epsilonAbsolute = 0.5` → gate is `mean(d) ≥ −0.5`.
  - `improved_flag_true_only_when_mean_delta_positive` — nice-to-have never required: a passing-but-negative mean reports `improved == false`.
  - `reported_statistics_match_synthetic_deltas` — median/min/max/stddev and improved/worsened/unchanged counts over a hand-computed delta set.

- **New `src/Game.Tests/EvalCommandAssertionTests.cs`:**
  - `candidate_arm_without_feature_emission_fails_with_never_acted` — score gate passing, zero emissions attributed to `F` → set fails.
  - `emission_from_other_feature_with_same_action_id_does_not_satisfy_candidate_assertion` — `baselineCardPlay` emitting the same `actionId` does not count for `F` (attribution soundness).
  - `target_actions_require_a_matching_attributed_emission` — `F` emitted, but no emission's `actionId` is in `targetActions` → fails; a matching one passes.
  - `baseline_arm_with_feature_emission_fails_batch` — any `F`-attributed emission in any baseline run → command-off violation.

- **New `src/Game.Tests/EvalParameterSearchTests.cs`:**
  - `grid_enumerates_full_cartesian_product_in_deterministic_order` — ordinal parameter-name order × declared value order.
  - `min_max_step_range_expands_to_inclusive_value_list` — `{min:0.1, max:0.9, step:0.2}` → `[0.1, 0.3, 0.5, 0.7, 0.9]`.
  - `random_mode_with_same_search_seed_reproduces_identical_sets` and `random_mode_respects_max_candidates`.
  - `run_cap_rejects_oversized_batch_before_any_run` — `seedCount × (1 + sets) > maxTotalRuns` → validation error, injected runner never invoked.
  - `winner_is_passing_set_with_highest_mean_delta` and `winner_tie_breaks_to_first_in_generation_order`.
  - `batch_passes_when_at_least_one_set_passes` and `no_declared_parameters_skips_search_with_single_candidate_arm`.

- **New `src/Game.Tests/EvalBatchTests.cs`:**
  - `seeds_derive_from_base_seed_deterministically` — `baseSeed + i` sequence.
  - `baseline_arm_runs_once_per_batch_and_is_shared_across_parameter_sets` — injected runner counts baseline invocations = `seedCount` regardless of set count.
  - `failing_run_fails_batch_naming_seed_arm_and_parameter_set` — injected failure on one candidate run → batch failure diagnostic carries all three; no pass verdict from a partial batch.
  - `baseline_and_candidate_arms_differ_only_in_the_feature_enabled_flag` — profile construction check over the built `BotProfile` DTOs.

- **New `src/Game.Tests/BotEmissionLogTests.cs`:**
  - `sink_callback_fires_only_for_accepted_plays` — duplicate-suppressed second play produces no emission entry.
  - `emissions_are_stamped_with_current_feature_org_date_and_tick` — host-loop stamping via the closure contract of §4.
  - `identical_decision_sequences_produce_element_wise_identical_logs` — two synthetic host runs over the same scripted decisions → identical entry lists; entries contain only game date/tick, no wall-clock fields.

Run: `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts — with the note that this plan itself introduces one amendment, by explicit user approval.**

- *ECS for all game logic in `src/`.* `OrgScore` (spec 49's derived query in `src/Game.Systems`, consumed but not redefined here); the emission hook lives in `src/Game.Bots`/`src/Game.ConsoleRunner`; the harness is net8.0 tooling in `src/Game.Evals`. No MonoBehaviour, no Unity-side logic, no ECS component added (org score is deliberately query-only per spec 49/this spec's out-of-scope list).
- *VContainer sole DI.* No Unity-side registrations touched; nothing new is resolved in Unity at all.
- *UI Toolkit only / URP only.* No UI, rendering, or `Assets/` change of any kind (Core DLL refresh excepted).
- *Planning Discipline — plan before implement.* This plan implements the approved spec 52. **Amendment introduced by this plan:** the §13 carve-out bullet, resolved with the user on 2026-07-15 and required by the spec itself, is added so `/implement-bot-feature`-driven bot features (PRD + eval history under this standing spec/plan) keep the written principle literally true. Until that amendment lands (it lands as part of this very feature), no bot feature is implemented via the skill.
- *Specification Discipline.* Spec 52 preceded this plan; per-feature bot work is covered by the standing spec + the new carve-out rather than per-feature specs — exactly the arrangement the spec's Resolved Decisions mandate.
- *File Organisation.* This plan lives at `Docs/Specs/52_bot-feature-eval-harness/plan.md`, paired with its spec under the shared index. `Docs/BotFeatures/<featureId>/` is a new, distinct documentation home for per-feature eval artifacts — it does not collide with the `Docs/Specs`/`Docs/Plans` index rule, which governs spec/plan pairs only.
- *One `.asmdef` per feature folder under `Assets/Scripts/`.* No `Assets/Scripts` folder or asmdef is created or modified; new `src/` code lands in existing projects plus the new `Game.Evals` csproj (net8.0, sln-only, never in Plugins — same posture as `Game.ConsoleRunner`/`Game.Configs.Loader`).
- *C# code style.* Tabs, braces always, `_`-prefixed privates, no redundant access modifiers throughout the new code, matching the surrounding `Game.Systems`/`Game.Tests` files.

Use /implement to start working on the plan or request changes.
