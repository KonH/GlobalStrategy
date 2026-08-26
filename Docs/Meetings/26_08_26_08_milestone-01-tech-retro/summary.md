# Tech retro — 01_world-domination

Ended 13:58 on owner decision (~46 minutes after `started` at 13:12). All seven checklist themes were covered; none were cut for time.

## Roster

- Ivan — Product Manager (owner)
- Bogdan — Junior Engineer | Tooling & Automation
- Miroslav — Architect | ECS Core
- Gleb — Principal Engineer | Unity UI
- Stanislav — Architect | Cross-cutting
- Radomir — Middle Engineer | Map & Province Systems
- Yaroslav — Junior Engineer | Localization

Kicked: Yaroslav (no ack within 60s, never spoke) and Bogdan (no ack within 60s after speaking on theme one). Remaining four plus the owner carried the rest of the meeting.

## Definition of done

Met, with the gaps below named rather than faked.

- What went well / badly: stated from own notes by everyone who remained. Yaroslav contributed nothing; Bogdan only spoke on wins.
- Codebase / maintainability pain: named and ranked.
- Specific files/systems: a must-do short list, not a category dump.
- Provider/model comparison: lived experience recorded; the room refused a fake league table from broken `usage.csv`.
- Productivity ideas: top three plus one explicit policy split.
- This file.

## Theme 1 — What went well

**Majority ranking**

1. The `src/` boundary: game logic compiles and runs without Unity, which is why the console runner, BenchmarkDotNet, bot evals, web client, and tests can share one game.
2. Playable surface (HUD, cards, tutorial, war, goals, leaderboard, two locales, browser) as proof that architecture actually shipped.
3. Automation (skills, GitHub-issue cron, `update-branch` version-bump auto-resolve) as a consequence of (1), not the cause.

**Unresolved split (Gleb):** HUD first, `src/` second, automation third. He would not live with the majority ranking.

## Theme 2 — What went badly

Three shared pains, not a pile of one-offs.

1. **The UI blob.** `HUDDocument` / `VisualState` / `VisualStateConverter` / animation barriers as one cluster. Concrete bugs: stuck gold after barrier races, accordion collapsing the wrong task, tutorial DI dying after a merge, war cards silently not playing.
2. **Broken `usage.csv`.** Two defects, not one (see open questions). Headline provider share (Claude 74.8% / Cursor 22.5% / Codex 2.7%) measures who authored `spec.md`, not who wrote code. 29/88 specs have no implement row; only 62/258 rows carry a cost; six bot-war specs each claim the same 1478 diff lines.
3. **Untested Unity glue.** C# tests green while Unity is wrong: unassigned `Map.prefab` material, fail-open coastline drawing, leftover `EventSystem.current.IsPointerOverGameObject` at `HUDDocument.cs:737`. Three people hit the same hole from three directions.

Retry dollars (province-info-panel 15 rows / $26, plus two other specs making ~$100 of $163 priced) were treated as a symptom of (1), not a fifth complaint. The flat test directory and 455 `new World()` calls were parked here and brought back under maintainability.

## Theme 3 — Codebase & maintainability (priority)

**Consensus**

- Unity **batchmode gate is a merge blocker, not a start blocker**: work on `HUDDocument` may start immediately; it does not land until the gate is green. Gate spec: load the real Map prefab and scenes, assert required serialized references and materials are non-null, ban `EventSystem.current`, run `BorderSegmentIndex` against production province geometry with a cap on unattributed segments. Toy-square C# tests are not this gate.
- Same day: `/plan` **size check at 800 lines**.
- **VisualState splits after panel splits**, not beside them (`VisualStateConverter` sits between; doing state first doubles the merge).
- **Fixtures (`TestWorld`) after the blob**, not the same week as `HUDDocument`.
- **`usage.csv` is parallel work** by whoever is not in the blob. It does not compete with the split. Record it as **backfill plus stop the leak** (implement writes its own row; diffs attributed per spec, not per branch). A backfill that reuses the branch-diff heuristic will reproduce the second defect.
- `GameLogic.cs` is not first.

**Unresolved split (Gleb):** first `HUDDocument` cut is gold and cards, not the debug-surface template. Majority: one- or two-day debug-surface template (`BuildProvinceDebugUi`, `BuildRelationDebugUi`, `BuildControlOrgDebugUi`, `RegisterDebugMenuToggle`, debug `Push` commands) then gold and cards.

## Theme 4 — Specific files (short list)

**Must-do**

1. `Assets/Scripts/Unity/UI/HUDDocument.cs` including the `EventSystem.current` call at line 737.
2. `src/Game.Main/VisualState.cs` and `VisualStateConverter.cs` as one hub, after the panel split.
3. Barrier stack: `src/Game.Main/AnimationBarrierInt.cs`, `AnimationBarrierDouble.cs`, `Assets/Scripts/Unity/UI/CardPlayBarriersHolder.cs`.
4. `src/Core.Map/Map/BorderSegmentIndex.cs` plus `Assets/Prefabs/Map/Map.prefab` (this is the gate, not a rewrite).
5. `src/Game.Tests/TestWorld.cs` **after** the blob (fixtures third).

**Watch** (real, not this milestone's rewrite): `ProvinceRenderer.cs`, `MapLensApplier.cs`, `InitSystem.cs` / `InitSystemTests.cs` (open when adding countries/orgs/provinces), `Wars.cs` (open when adding war phases), `BotObservation.cs` (**becomes must-do the moment VisualState splits**). Tutorial/accordion files are symptoms of `HUDDocument`, not separate projects.

**Unresolved split (Gleb):** drop `TestWorld` from must-do, put `TutorialHighlightView.cs` back; `Map.prefab` is "not my week." Majority kept the five.

## Theme 5 — Providers / models

**Majority (signed)**

- No model league table from this milestone. Codex has 7/258 rows; sonnet-4.6 → sonnet-5 had no A/B; implement rows are missing.
- What actually mattered was whether the task had a **gate**. Bot evals and the benchmark harness produced usable output on whatever model ran them. Failures (unassigned material, leftover `EventSystem`, tutorial DI) failed on every provider because no model passes a gate that does not exist. Expensive specs were **blind**, not hard: the model could not see the panel.
- Observed routing, not capability: Claude took open-ended high-diff work (web client, add-more-countries, black-hand) and specs/plans; Cursor Grok took well-specified follow-ups and surgical patches. Cursor's UI-patch advantage is the **environment** (screenshot, MCP, file already open), not a secret Grok talent Claude lacks.
- Codex 5.6 Luna: one lived example as an **independent reviewer** on a bounded slice (traced `BorderSegmentIndex` → `ProvinceRenderer` → `Map.prefab`). Not an implementation bake-off.
- Next milestone: **same spec shape, same gate, both providers, log both.**

**Unresolved split (Gleb):** Grok wins UI **as a model**, not just because of screenshots. Claude writes a spec and misses the panel.

## Theme 6 — Productivity

**Top three (majority)**

1. **Eyes.** Implement on HUD / map / prefabs cannot claim done without a screenshot, Unity MCP snapshot, or batchmode render — same class of rule as the bot eval batch.
2. **Fast lane.** Written into the constitution: if a change touches at most two files, introduces no new system or config type, and is expected under ~250 diff lines, skip spec and plan; record a one-paragraph intent on the `usage.csv` row. Batch sibling card variants into one spec. Keep the full ceremony for open-ended high-diff work. Gates still apply (batchmode, eval batch, 800-line check). This is cutting paperwork, not safety. Evidence: spec+plan ~$9 vs implement $12.75 on priced rows; 21/86 specs shipped under 250 diff lines (e.g. declare-war-card 9,869 spec/plan tokens for 99 diff lines).
3. **`map-validate` skill.** Isolated regen, deterministic hashes, unknown neighbors, attribution coverage, known cross-country boundary checks, map test slice. Report is an artifact on every map/province PR. Agents stop reading 438k lines of generated JSON.

**Unresolved split (Gleb):** UI implement **must** route to Cursor Grok (specs may stay on Sonnet). That is policy, not preference; do not fold it into eyes.

**How the split gets resolved, not frozen:** run the already-agreed same-spec A/B on **two UI specs, both behind the eyes gate**. If Grok wins on rows and rework, encode routing at the next retro. Until then it is a preference Gleb is free to exercise on tickets he picks up.

## Open questions

- Does a longer spec produce a better outcome, or just more tokens? Unanswered here; the fast lane is a process bet, not that experiment.
- After the eyes gate exists, does Grok still beat Claude on UI? That is the A/B above.
- When VisualState splits, `BotObservation.cs` must not drift. Not scheduled; flagged as a tripwire.

## Actionable takeaways for the next milestone

1. Land the Unity batchmode gate; treat it as a merge blocker.
2. Add the 800-line `/plan` size check the same day.
3. Split `HUDDocument` (majority: debug template then gold/cards; Gleb: gold/cards first). Do not merge until the gate is green.
4. Split VisualState/Converter after panels, not beside them.
5. Fix the barrier stack with the gold label's single owner in mind.
6. Introduce `TestWorld` after the blob; stop adding 456th `new World()`.
7. Repair `usage.csv`: backfill history **and** make implement write its own row with per-spec diffs.
8. Require eyes on UI/Unity-glue implement.
9. Adopt the spec/plan fast lane for small diffs; batch sibling card variants.
10. Add `map-validate` and attach its report to map/province PRs.
11. Run same-spec, same-gate, both-provider A/B (include two UI specs behind eyes) before encoding any model routing policy.
