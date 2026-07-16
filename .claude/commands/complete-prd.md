Finish a Ralph loop run: commit any remaining changes and open a pull request.

Arguments: `$ARGUMENTS` — either the spec index (e.g. `45`), or `bot:<featureId>` for a bot-feature run (e.g. `bot:opinionTargeting`).

## Steps

1. **Resolve the spec folder.**
   - Spec-index form: `Docs/Specs/<index>_<name>/`.
   - `bot:<featureId>` form: the standing spec `Docs/Specs/52_bot-feature-eval-harness/` (bot features have no per-feature spec/plan — see the Constitution's bot-feature carve-out).

   Read `.ralph/prd.md` and `.ralph/activity.md`. Determine run status: how many tasks have `"passes": true` out of the total, and which are `"category": "unity-manual"`.
2. Run `git status` (never `-uall`) and `git diff`. If uncommitted changes remain, stage them and create a commit using the **/commit** skill (it handles branch rules and the version bump). If the working tree is clean, still run the /commit version-bump flow only if no commit exists on this branch yet; otherwise proceed.
3. Create the pull request using the **/pr** skill. In addition to the /pr skill's standard body format, append a section:

   ```
   ## Ralph run
   - Spec: Docs/Specs/<index>_<name>/
   - Tasks passed: X/N
   - Manual Editor verification needed: <list of unity-manual tasks, or "none">
   - Unfinished tasks / blockers: <from activity.md, or "none">
   ```

   Unchecked test-plan items must include every `unity-manual` task.

   **`bot:<featureId>` form only** — follow the Ralph run section with an `## Eval verdict` section sourced from `Docs/BotFeatures/<featureId>/eval_history.json` (latest entry) and `eval_summary.md`:

   ```
   ## Eval verdict
   - Gates: score=<pass/fail>, command-on=<pass/fail>, command-off=<pass/fail>
   - Mean delta: <mean> (median <median>) vs epsilon <epsilon>
   - Improved (mean > 0): <yes/no>
   - Winning parameters: <winner's parameter set, or "no search">
   - Attempts: <count>
   - Details: Docs/BotFeatures/<featureId>/
   ```

4. Report the PR URL.

## Rules

- Do not flip any `"passes"` flags here — this skill reports state, it does not verify tasks.
- If the loop left blockers in `.ralph/activity.md`, quote them in the PR body verbatim rather than paraphrasing them away.
- For `bot:<featureId>` runs: never fabricate the eval verdict section — read it from the committed `eval_history.json`/`eval_summary.md`; if those files are missing or empty, say so rather than inventing numbers.
