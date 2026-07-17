# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-17 — Run discoverAndControl eval batch (attempt 4, passed)

Task: "Run the discoverAndControl eval batch ... and iterate until it beats the
feature-disabled baseline."

**Starting state (uncommitted, from a prior interrupted iteration):** attempts 1-3 in
`eval_history.json` all failed with `commandOnPass: false` and every candidate score
*bit-for-bit identical* to baseline (delta 0 across all 6 threshold values and all 10
seeds) — a strong signal the feature was never actually taking any action, not that the
threshold grid was wrong. A prior iteration had already root-caused this and left an
uncommitted fix in the working tree:
- `src/Core.Configs.IO/ActionEffectDefinitionListConverter.cs` (new, untracked) — a
  System.Text.Json converter for `EffectConfig.Effects`. Without it, `FileConfig<T>`
  (which uses System.Text.Json) silently ignores the Newtonsoft `[JsonConverter]`
  attribute on that field, so every effect deserializes as the base
  `ActionEffectDefinition` type. `BotObservation.ClassifyCard`'s `is
  DiscoverCountryEffectParams` / `is ControlChangeEffectParams` checks were therefore
  always false, so `DiscoverAndControlFeature` never found a discover/control card to
  play — never emitted anything, never changed the score.
- `src/Core.Configs.IO/FileConfig.cs` — wired the new converter into the shared
  `JsonSerializerOptions`.
- `src/Core.Configs.IO/Core.Configs.IO.csproj` — added a `ProjectReference` to
  `Game.Configs` (needed for the effect-params types the converter deserializes into).
- `Docs/BotFeatures/discoverAndControl/eval_config.json` — `candidateFeatures` was
  `null` (eval defaults to using the *bot's default profile*, which per
  `BotSession.DefaultProfile` already enables `discoverAndControl` unparameterized —
  so attempts 1-2 were unknowingly evaluating the *default-params* feature, not the
  grid); fixed to explicitly declare `discoverAndControl` enabled with `{}` params so
  `ApplyParameters` can override `discoveredCountriesAvailableControl` per grid point.

None of this was journaled by whichever iteration made it, so I verified it myself
before trusting it: `dotnet build src/GlobalStrategy.Core.sln -c Release` → 0
errors/warnings, all plugin DLLs rebuilt into `Assets/Plugins/Core/`.

**What I did this iteration:** ran the actual gate,
`dotnet run --project src/Game.Evals -- --feature discoverAndControl`. Attempt 4
passed: `Eval batch passed for feature 'discoverAndControl': winner=0
meanDelta=10947.676 improved=True`. Full grid in
`Docs/BotFeatures/discoverAndControl/eval_summary.md`:
`discoveredCountriesAvailableControl=0` and `=1` tie for the best mean delta
(+10947.676, min +7550 / max +14345 across 10 seeds); score improvement shrinks
monotonically as the threshold rises (2 → +1473, 3 → +1247, 5 → +879, 8 → +544).
Winner index 0 (`=0`) — i.e. controlling already-discovered countries before
discovering new ones wins the most, at least under this eval's opponent
(`baselineCardPlay`) and 5-year horizon. `commandOnPass` and `commandOffPass` both
true for every parameter set, confirming emission attribution now works correctly
(baseline arm has zero `discoverAndControl` emissions, candidate arm has at least
one for every set).

Marked this task's `"passes": true`. `Docs/BotFeatures/discoverAndControl/eval_history.json`
and `eval_summary.md` were rewritten by the run itself (`EvalPersistence`) — no manual
edits needed. Deleted `.tmp/dotnet-build.log` and `.tmp/eval-run.log` after reading
them (gitignored scratch logs); `.tmp/evals/discoverAndControl/attempt_4/*` raw run
output is also gitignored and left in place.

**Next iteration (task 4, "Adopt the winning threshold as the eval config's candidate
default"):** the latest passing entry's winner is parameter set index 0, i.e.
`discoveredCountriesAvailableControl: 0`. Pin that value into
`Docs/BotFeatures/discoverAndControl/eval_config.json`'s `candidateFeatures` entry for
`discoverAndControl` (parameters: `{ "discoveredCountriesAvailableControl": 0 }`), per
the task's own instructions — do **not** change the C# default (stays
`double.MaxValue` / discover-first), this task only pins the eval config's default so
future eval runs start from the validated threshold instead of the empty-`{}` grid
placeholder.
