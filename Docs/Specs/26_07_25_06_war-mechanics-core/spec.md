# Spec: War Mechanics Core

## Feature Intent

As a developer/tester driving the game through the debug terminal, I want a backend war state (an attacker and a defender country locked into a war, with a bounded, monthly-decaying progress value) that I can force-create and force-end unconditionally, so that later slices (natural declaration, natural progress, peace resolution, allied wars) have a real war/warParticipant/warProgress data model and a monthly tick already in place to build on.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- Two countries, A and B, are neither currently in a war (with each other or anyone else).
  - The tester issues a declare-war debug command naming A as attacker and B as defender => a war is created with A as attacker and B as defender, its progress starts at 0, and both A and B are now considered "in a war".
  - The tester issues the same declare-war command again, still naming A and B, before either stops their war => the command has no effect — a country already in a war cannot be put into a second one, so no second war is created and the existing war/progress are untouched.
  - The two countries named are the same country => the command has no effect — a country cannot be at war with itself.
- Country A is already in a war (as either attacker or defender, against any opponent) and country C is not in a war.
  - The tester issues a declare-war command naming A as attacker (or defender) and C as the other side => the command has no effect; the check is purely "is this country currently in *any* war", independent of which side it would take in the new one.
- Two countries are related as rivals, or are not related at all, or are friends.
  - The tester issues the declare-war debug command for that pair => the command still succeeds (subject only to the not-already-in-war check above) — the debug command does not check or require a Rival relation between the two countries. Whether two countries were already rivals, friends, or unrelated has no bearing on whether the debug command works.
- A war between an attacker and a defender exists with progress at some value.
  - A month boundary passes in the simulation (the in-game month advances) => the war's progress decreases by the configured attacker-progress-decay amount, clamped so it never goes below -100.
  - The war's progress is already at or below -100 and another month boundary passes => progress stays at -100; it does not decrease further.
  - No month boundary has passed yet since the war was declared (still within the same in-game month) => progress is unchanged.
- A country is currently a participant (attacker or defender) in a war.
  - The tester issues a stop-war debug command naming that country => that war ends: the war and its progress are removed, and both the attacker and the defender are no longer considered "in a war" (both are immediately free to be declared into a new war).
- A country is not currently a participant in any war.
  - The tester issues a stop-war debug command naming that country => the command has no effect; there is nothing to stop.

## Tech Notes

- **Data model** (new file `src/Game.Components/War.cs`, all `[Savable]` — this is live domain state, not derived/recomputable, matching the precedent set by `src/Game.Components/CountryRelation.cs`):
  - `War { string WarId }` — one entity per active war.
  - `WarProgress { double Value }` — composed directly onto the same entity as `War` (per `.claude/rules/unity/ecs_patterns.md`'s "composition over parallel lookup entities" rule: `WarProgress` belongs to the war that already exists as an entity, so it is added onto that entity rather than living on a second entity keyed by a duplicated `WarId`). Range is enforced by `WarSystem` (see below), not by the component itself. Initial value `0` at war creation.
  - `WarParticipant { string WarId, WarParticipantKind Kind, string CountryId }` — exactly two of these are created per war (one `Attacker`, one `Defender`), each referencing the war's `WarId` by value (this one *is* a genuine multi-row relation — a war's two sides are two distinct entities, not a single composed value — so a parallel, `WarId`-keyed entity is the correct shape here, unlike `WarProgress` above). This is the literal `warParticipant(kind, warId, countryId)` shape named in the issue.
  - New enum `WarParticipantKind { Attacker, Defender }` in `src/Game.Common/WarParticipantKind.cs`, sibling to `src/Game.Common/RelationKind.cs` (same rationale: a small enum consumed by a `[Savable]` component but not itself requiring persistence attributes).
  - No `WarVersion`/dirty-counter component is added (unlike `CountryRelationsVersion`) — there is no UI or `VisualStateConverter` consumer of war state in this slice (UI is explicitly out of scope), so there is nothing to invalidate a cache for. Add one in a future slice if/when a UI consumer needs change detection.
- **`src/Game.Systems/WarSystem.cs`** (new static class, same shape/style as `src/Game.Systems/CountryRelations.cs`):
  - `IsInWar(IReadOnlyWorld world, string countryId) : bool` — scans `WarParticipant` archetypes for any entity whose `CountryId` matches, regardless of `Kind`. Used both by `DeclareWar`'s guard and available as a general query.
  - `DeclareWar(World world, string attackerCountryId, string defenderCountryId, DateTime currentTime) : bool` — returns `false` (no-op) if `attackerCountryId == defenderCountryId`, or if `IsInWar` is true for either id (mirrors `CountryRelations.SetRelation`'s equal-id guard and `RemoveRelation`'s scan-and-match style). Otherwise: creates a new entity with `War { WarId }` + `WarProgress { Value = 0 }`, where `WarId = $"war_{attackerCountryId}_{defenderCountryId}_{currentTime.Ticks}"` (deterministic, following the `EffectId` construction convention in `src/Game.Systems/CreateActionEffectSystem.cs`, e.g. `$"control_{orgId}_{countryId}_{currentTime.Ticks}"`); creates two more entities: `WarParticipant { WarId, Kind = Attacker, CountryId = attackerCountryId }` and `WarParticipant { WarId, Kind = Defender, CountryId = defenderCountryId }`. Returns `true`.
  - `StopWar(World world, string countryId) : bool` — scans `WarParticipant` archetypes for an entity matching `countryId`; if none found, returns `false`. If found, reads its `WarId`, then destroys: the `War`+`WarProgress` entity with that `WarId`, and both `WarParticipant` entities (attacker and defender) with that `WarId`. Returns `true`. This is a hard delete, not a resolution — no outcome/victor is computed or recorded anywhere, consistent with peace resolution being out of scope.
  - `Update(World world, DateTime previousTime, DateTime currentTime, double decayPerMonth)` — follows `src/Game.Systems/ControlSystem.cs`'s exact month-boundary style: computes `isMonthBoundary = previousTime.Month != currentTime.Month || previousTime.Year != currentTime.Year` and returns immediately if false. Otherwise, for every entity carrying `WarProgress`, applies `Value = Math.Max(-100, Value - decayPerMonth)` (clamped floor only — decay always pushes progress down regardless of current sign, per "attacker side loses part of progress each month"; there is no corresponding monthly increase in this slice, so an untouched war's progress will monotonically fall to -100 and stay there — expected for this core slice since no other progress-moving mechanic exists yet). No upper clamp is exercised by this system (nothing in this slice increases progress), but the component's documented range is `[-100, 100]` per the issue, so future progress-increasing code must clamp at `+100` the same way.
  - No method in `WarSystem` calls another system's entry point, and nothing outside the top-level orchestrator calls `WarSystem`'s methods — consistent with the no-system-to-system-calls rule.
- **Config** (`attacker progress decay ... initial value 2.5`):
  - `Assets/Configs/game_settings.json`: add `"attackerWarProgressDecayPerMonth": 2.5` as a new top-level key, sibling to `"recruitsMonthlyIncreasePercent"`.
  - `src/Game.Configs/GameSettings.cs`: add `public double AttackerWarProgressDecayPerMonth { get; set; } = 2.5;`, sibling to `RecruitsMonthlyIncreasePercent`.
- **Debug commands** (new files in `src/Game.Commands/`, following the `[CountryId]`-annotated field pattern of `DebugImproveOpinionCommand`/`DebugChangeGoldCommand` — not the older, unannotated `DebugSetCountryRelationCommand`/`DebugClearCountryRelationCommand`, so the web-client debug terminal's tab-completion works for these new commands; see the `add-terminal-command` skill):
  - `DebugDeclareWarCommand : ICommand { [CountryId] public string AttackerCountryId; [CountryId] public string DefenderCountryId; }`
  - `DebugStopWarCommand : ICommand { [CountryId] public string CountryId; }`
  - Both are picked up automatically by `src/Game.SourceGenerators/CommandGenerator.cs` (any `struct : ICommand` under `GS.Game.Commands` gets a generated `ReadDebugDeclareWarCommand()`/`ReadDebugStopWarCommand()` on `CommandAccessor` — no manual accessor wiring needed).
- **`src/Game.Main/GameLogic.cs` wiring:**
  - Add `WarSystem.Update(_world, _previousTime, currentTime, GameSettings.AttackerWarProgressDecayPerMonth);` immediately after the existing `ControlSystem.Update(_world, _previousTime, currentTime);` call (line 113) — same tick position, same `previousTime`/`currentTime` args, same reasoning (a month-boundary-gated system driven by the tick's already-computed `currentTime`).
  - Add two new command loops next to the existing relation command loops (after the `ReadDebugClearCountryRelationCommand` loop, around line 190-192):
    ```csharp
    foreach (var cmd in _commandAccessor.ReadDebugDeclareWarCommand().AsSpan()) {
        WarSystem.DeclareWar(_world, cmd.AttackerCountryId, cmd.DefenderCountryId, currentTime);
    }
    foreach (var cmd in _commandAccessor.ReadDebugStopWarCommand().AsSpan()) {
        WarSystem.StopWar(_world, cmd.CountryId);
    }
    ```
    Both call `WarSystem` directly from the orchestrator (no wrapping private `ApplyDebug...` method needed), matching how `DebugSetCountryRelationCommand`/`DebugClearCountryRelationCommand` call `CountryRelations.SetRelation`/`RemoveRelation` directly rather than through a private helper — this is the more analogous precedent since war, like relations, is a pairwise-entity model rather than a single-resource mutation (contrast with `ApplyDebugChangeGold`/`ApplyDebugImproveOpinion`, which wrap resource mutations in a private method).
  - Both command return values (`bool`) are discarded at the call site, exactly like the existing `RemoveRelation` call in the `ReadDebugClearCountryRelationCommand` loop — no success/failure feedback channel (Game Log entry, console log, etc.) is added; the tester observes outcomes via save/inspection state, matching every other existing debug command's silent-success/silent-no-op convention.
- **Rivals-only rule placement:** the "war can be declared naturally only to rivals" rule from the issue is *not* implemented anywhere in this slice. It is documented here only as the intended constraint for the future, out-of-scope *natural* declaration flow (which would presumably call something like `CountryRelations.GetRelation(world, attackerId, defenderId) == RelationKind.Rival` before calling `WarSystem.DeclareWar`). `DebugDeclareWarCommand`'s handler performs no relation check of any kind — it is intentionally unconditional except for the not-already-in-war guard, per the issue's explicit "(unconditional, check only current not-in-war status)" instruction.

## Out of Scope

- Any UI — no debug menu buttons/dropdowns/HUD surface of any kind. The only surface this feature adds is the two `ICommand` types plus their `GameLogic` wiring, reachable through the existing web-client debug terminal.
- Natural (non-debug) war declaration between rivals — the "only rivals" rule is documented in Tech Notes as the future natural-flow constraint but is not enforced by any code in this slice.
- Natural war progress changes of any kind (combat outcomes, events, actions) — the only thing that moves `WarProgress.Value` in this slice is the monthly attacker decay.
- Peace resolution — no negotiated end, no victory/defeat determination, and no consequence of `WarProgress` reaching -100 or +100. The stop-war debug command ends a war's existence with a hard delete; it computes no outcome.
- Allies / multi-country wars — every war created in this slice has exactly one attacker and one defender (two `WarParticipant` entities total); a country can be in at most one war at a time; nothing in this slice supports more than two participants per war.

## Ambiguities

None — the issue text plus existing `CountryRelation`/`ControlSystem`/`DebugSetCountryRelationCommand` precedent resolve every modeling decision needed for this slice (war entity shape, progress composition, decay direction/clamping, config placement, command shape, and stop-war semantics), as documented with their rationale in Tech Notes above.
