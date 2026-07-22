Autonomously implement a bot feature, run its eval batch, iterate on failure within a bounded budget, and scaffold a Ralph loop run that ends in a human-reviewable PR.

Arguments: `$ARGUMENTS` — a natural-language description of the bot mechanic (e.g. a card-usage rule or a target-country selection strategy).

## Authority statement

This skill is the sanctioned autonomous path for **bot features only**: `IBotFeature` implementations in `src/Game.Bots`, their registrations in `src/Game.Bots/BotFeatureRegistry.cs`, their `Docs/BotFeatures/<featureId>/` eval configs and history, and the `.ralph` PRD that drives their implementation loop. `Docs/Specs/52_bot-feature-eval-harness/` (spec + plan) is the standing spec/plan for this surface, and the per-feature PRD plus its committed eval history is the planning artifact — per the Constitution's Planning Discipline bot-feature carve-out.

**Any change outside that surface is out of authority.** If the description implies touching game systems, the observation facade (`IBotObservation`), the command sink whitelist (`IBotCommandSink`), the headless runner, `src/Game.Evals` itself, or any `Assets/` asset — **stop and direct the user to `/specify` + `/plan`.** Do not attempt to route around this by squeezing the change into a "feature."

## Steps

1. **Derive `featureId`.** camelCase, matching spec 51's naming convention (e.g. "target countries where our opinion is highest" → `opinionTargeting`). Check `src/Game.Bots/BotFeatureRegistry.cs` — if the id is already registered, **stop and report**; do not silently overwrite an existing feature.

2. **Branch.** Require a clean working tree (`git status`, never `-uall`). Create or switch to `ralph/bot_<featureId>` (the exact branch name `scripts/automation/claude/ralph.ps1 -BotFeature <featureId>` resolves to).

3. **Write the eval config** — `Docs/BotFeatures/<featureId>/eval_config.json`:
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
   - Leave every field at its default unless the description says otherwise.
   - `targetActions`: infer action ids from `Assets/Configs/action_config.json` that the description implies the feature should play (e.g. a "prioritize discovery" description → the discovery action's id). Leave `[]` if nothing concrete is named.
   - `parameterSearch`: only populate if the description names a tunable (e.g. "with a configurable minimum gold reserve") — declare a `grid` or `random` search over that parameter's plausible range. Otherwise leave `null` (no search).

4. **Write `.ralph/prd.md` directly** — no `/specify`, no `/plan`, no `/create-prd`. Reset `.ralph/activity.md` to its header only (`# Ralph Activity Journal` + intro line + `---`). Use the standard task shape `{ "category", "description", "steps", "gate", "passes" }`. Four tasks, in order:

   1. **Implement the feature.**
      - `category`: `"bot-feature"`
      - Implement `<FeatureId>Feature : IBotFeature` in `src/Game.Bots` (file `src/Game.Bots/<FeatureId>Feature.cs`), reading its declared parameters the way `BaselineCardPlayFeature` does (`parameters.TryGetValue("name", out var v) ? v : default`), behind its `enabled` flag (an unregistered/disabled feature must never be instantiated — see spec 51).
      - Add focused unit tests of its decision logic on synthetic observations to `src/Game.Tests`.
      - `gate`: `dotnet test src/GlobalStrategy.Core.sln`

   2. **Register the feature.**
      - `category`: `"bot-feature"`
      - Add one line to `BotFeatureRegistry.CreateDefault()`: `registry.Register(<FeatureId>Feature.Id, parameters => new <FeatureId>Feature(parameters));`
      - `gate`: `dotnet test src/GlobalStrategy.Core.sln`

   3. **Run the eval batch and iterate.**
      - `category`: `"bot-feature-eval"`
      - `gate`: `dotnet run --project src/Game.Evals -- --feature <featureId>` — the CLI's exit code **is** the verdict. Never fabricate an always-green gate.
      - `steps` (encodes the improvement loop — write this literally into the task, filling in `<featureId>` and the current `baseAttempt`, i.e. the attempt count already in `Docs/BotFeatures/<featureId>/eval_history.json` before this run starts, `0` for a brand-new feature):
        1. Run the gate command.
        2. If it passes (exit 0): mark this task's `passes: true` and stop iterating.
        3. If it fails: read `Docs/BotFeatures/<featureId>/eval_history.json` (latest entry) and `.ralph/activity.md`. Pick **one** concrete improvement — a logic change, different targeting, or an adjusted parameter range in `eval_config.json` — journal the change and why in `.ralph/activity.md`, then re-run the gate.
        4. If `Docs/BotFeatures/<featureId>/eval_history.json` has reached `baseAttempt + 5` or more attempts and the gate still fails: journal budget exhaustion in `.ralph/activity.md`, leave `passes: false`, and end the iteration — do not keep retrying past the budget. This 5-attempt cap is independent of the Ralph driver's own `-MaxIterations`.

   4. **Adopt winning parameters** (only include this task if `parameterSearch` was populated in step 3):
      - `category`: `"bot-feature"`
      - Update the feature's default parameter handling in its `src/Game.Bots` registration to the winning set recorded in the latest passing `eval_history.json` entry, and pin those values in `eval_config.json`'s `candidateFeatures` parameters.
      - `gate`: `dotnet test src/GlobalStrategy.Core.sln`

5. **Commit the scaffolding.** Stage the eval config, `.ralph/prd.md`, and the reset `.ralph/activity.md`, and commit via the **/commit** skill's rules.

6. **Hand off to the driver.** Print exactly this for the user to run in a terminal — this skill does **not** spawn the loop itself:
   ```
   .\scripts\automation\claude\ralph.ps1 -BotFeature <featureId>
   ```

## Finish / failure semantics

Executed by the driver's phases (`scripts/automation/claude/ralph.ps1 -BotFeature <featureId>` → `scripts/automation/claude/ralph.py`), not by this skill directly:

- **All gates pass:** the driver runs `/commit` + `/complete-prd bot:<featureId>`, opening a PR whose body carries the eval verdict (gate outcomes, mean/median delta vs ε, improved flag, winning parameters, attempt count). The loop **never merges** — human review gates merge, because evals cannot judge metric-gaming or gameplay feel.
- **Budget exhausted / loop incomplete:** no "done" PR is opened. The committed-artifact state reached on the branch stays as-is; the failure report is `Docs/BotFeatures/<featureId>/eval_summary.md` + `eval_history.json` (which gates failed on which attempts) and `.ralph/activity.md`. Nothing is force-reverted. A human can run `claude -p "/complete-prd bot:<featureId>"` manually if they want a PR anyway.
