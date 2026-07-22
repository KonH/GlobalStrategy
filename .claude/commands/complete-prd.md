Finish a Ralph loop run: commit any remaining changes and open a pull request.

Arguments: `$ARGUMENTS` — either the spec index (e.g. `45`), `bot:<featureId>` for a bot-feature run (e.g. `bot:opinionTargeting`), or `perf:<target>` for a performance-optimization run (e.g. `perf:CountryPopulationCollector`).

## Steps

1. **Resolve the spec folder.**
   - Spec-index form: `Docs/Specs/<index>_<name>/`.
   - `bot:<featureId>` form: the standing spec `Docs/Specs/52_bot-feature-eval-harness/` (bot features have no per-feature spec/plan — see the Constitution's bot-feature carve-out).
   - `perf:<target>` form: the standing spec `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/` (perf-optimization attempts have no per-target spec/plan — see the Constitution's performance-optimization carve-out).

   Read `.ralph/prd.md` and `.ralph/activity.md`. Determine run status: how many tasks have `"passes": true` out of the total, and which are `"category": "unity-manual"`.
2. Run `git status` (never `-uall`) and `git diff`. If uncommitted changes remain, stage them and create a commit using the **/commit** skill (it handles branch rules and the version bump). If the working tree is clean, still run the /commit version-bump flow only if no commit exists on this branch yet; otherwise proceed.
3. Create the pull request using the **/pr** skill. Create it as a draft when any PRD task is unfinished or the activity journal records a blocker; create it ready for review only when every task passes and no blocker remains. In addition to the /pr skill's standard body format, append a section:

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

   **`perf:<target>` form only** — follow the Ralph run section with a `## Benchmark verdict` section sourced from `Docs/Benchmarks/history.json` (latest entry/entries for the target) and `Docs/Benchmarks/summary.md`:

   ```
   ## Benchmark verdict
   | Benchmark | Baseline mean | Current mean | % change | Verdict | Allocated bytes |
   |---|---|---|---|---|---|
   | <name> | <baseline ns> | <current ns> | <%> | <pass/fail> | <bytes> |
   - Attempts: <count>
   - Details: Docs/Benchmarks/
   ```

4. After the pull request exists, clear the completed run artifacts: replace `.ralph/prd.md` with a short `# Ralph PRD` header stating that no run is active, and reset `.ralph/activity.md` to its standard header only (`# Ralph Activity Journal`, its intro line, and `---`). Stage and commit this cleanup as a **separate follow-up commit** using the **/commit** skill, then push it to the same pull-request branch. Do not combine this artifact cleanup with the implementation commit or the PR-creation commit.
5. Report the PR URL and the follow-up cleanup commit.

## Rules

- Do not flip any `"passes"` flags here — this skill reports state, it does not verify tasks.
- Do not refuse to create a PR merely because the Ralph run is incomplete. A partial PR preserves verified progress and makes the remaining tasks and blockers reviewable.
- If the loop left blockers in `.ralph/activity.md`, quote them in the PR body verbatim rather than paraphrasing them away.
- For `bot:<featureId>` runs: never fabricate the eval verdict section — read it from the committed `eval_history.json`/`eval_summary.md`; if those files are missing or empty, say so rather than inventing numbers.
- For `perf:<target>` runs: never fabricate the benchmark verdict section — read it from the committed `Docs/Benchmarks/history.json`/`summary.md`; if those files are missing the target's entries, say so rather than inventing numbers.
