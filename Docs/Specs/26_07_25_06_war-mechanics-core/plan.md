# Plan: War Mechanics Core

## Spec

A tester driving the game through the debug terminal needs a real backend war data
model — an attacker/defender pair locked into a war with a bounded, monthly-decaying
`WarProgress` value — that can be force-created and force-ended unconditionally. This
gives later slices (natural declaration between rivals, natural progress, peace
resolution, allied wars) a concrete `War`/`WarParticipant`/`WarProgress` shape and a
monthly tick to build on.

Acceptance criteria (condensed):
- Declaring a war between two countries that are both currently free of any war
  creates the war with `Attacker`/`Defender` participants and `WarProgress.Value == 0`;
  both countries are now "in a war".
- Re-declaring the same pair (or issuing any declare-war naming a country already in
  *any* war, on either side, against any opponent) is a no-op — the existing war and
  its progress are untouched, and no second war is created.
- Declaring a war between a country and itself is a no-op.
- The debug declare-war command performs no relation check — it succeeds regardless
  of whether the two countries are rivals, friends, or unrelated (that constraint is
  reserved for a future *natural* declaration flow, not this debug command).
- On every in-game month boundary, every active war's `WarProgress.Value` decreases by
  the configured `AttackerWarProgressDecayPerMonth` (default `2.5`), floor-clamped at
  `-100`; progress already at `-100` stays there; no change happens mid-month.
- Issuing stop-war naming a country currently in a war hard-deletes that war (the
  `War`+`WarProgress` entity and both `WarParticipant` entities); both former
  participants are immediately free to be declared into a new war. Stop-war on a
  country not currently in any war is a no-op.

Full detail, including exact data shapes, method signatures, and file paths, lives in
`spec.md`'s Tech Notes — this plan follows them directly rather than re-deriving the
design.

## Goal

Add the backend war data model and its two mutation entry points (declare/stop) plus
the monthly decay system, wired into `GameLogic`, exactly per the spec's Tech Notes —
no UI, no natural-declaration logic, no peace resolution, no allied/multi-country
support.

## Approach

Follow the spec's Tech Notes verbatim:

- **Components** (`src/Game.Components/War.cs`, all `[Savable]`): `War { WarId }`,
  `WarProgress { Value }` composed directly onto the same entity as `War` (composition
  over parallel lookup entities — `WarProgress` has no `WarId` of its own), and
  `WarParticipant { WarId, Kind, CountryId }` as a genuine parallel relation entity
  (two rows per war, one `Attacker` one `Defender`). No `WarVersion` component — no UI
  consumer exists yet to invalidate a cache for.
- **Enum** `WarParticipantKind { Attacker, Defender }` in a new sibling file
  `src/Game.Common/WarParticipantKind.cs`, matching `RelationKind`'s shape.
- **Config**: `AttackerWarProgressDecayPerMonth` (default `2.5`) added to
  `GameSettings.cs` and `game_settings.json` as a sibling of
  `RecruitsMonthlyIncreasePercent`/`recruitsMonthlyIncreasePercent`.
- **`Wars.cs`** (new static helper in `src/Game.Systems/`, non-"System" — same shape as
  `CountryRelations.cs`): `IsInWar`, `DeclareWar`, `StopWar`. Called directly from
  `GameLogic`'s command loops, never from another system.
- **`WarSystem.cs`** (new static system in `src/Game.Systems/`, same shape as
  `ControlSystem.cs`): `Update(world, previousTime, currentTime, decayPerMonth)`,
  month-boundary-gated, applies the floor-clamped decay to every `WarProgress`.
- **Commands**: `DebugDeclareWarCommand`/`DebugStopWarCommand` in `src/Game.Commands/`,
  using `[CountryId]`-annotated fields (the attribute already exists in
  `ParamSuggestion.cs` — no new attribute or `ISuggestionValueProvider` needed per the
  `add-terminal-command` skill, since `CountryId` is not a new id kind). Picked up
  automatically by `CommandGenerator.cs`.
- **`GameLogic.cs` wiring**: `WarSystem.Update(...)` immediately after
  `ControlSystem.Update(...)` (line 113), and two new command loops after the
  `ReadDebugClearCountryRelationCommand` loop (around lines 190-192), calling
  `Wars.DeclareWar`/`Wars.StopWar` directly with discarded `bool` returns — matching
  the existing relation command loops' silent-success/no-op convention.

This is a headless, backend-only (`src/`) change — no Unity Editor, no scene/asset
work, no MonoBehaviour/UI Toolkit surface. All steps are Agent Steps; there is no User
Steps section.

## Agent Steps

- [x] **Add `WarParticipantKind` enum** — create `src/Game.Common/WarParticipantKind.cs` with `namespace GS.Game.Common { public enum WarParticipantKind { Attacker, Defender } }`, sibling to `RelationKind.cs`.
- [x] **Add `War`/`WarProgress`/`WarParticipant` components** — create `src/Game.Components/War.cs` with three `[Savable]` structs: `War { string WarId; }`, `WarProgress { double Value; }` (composed onto the same entity as `War`, initial value `0`, range `[-100, 100]` enforced only by `WarSystem`, not the component), and `WarParticipant { string WarId; WarParticipantKind Kind; string CountryId; }`.
- [x] **Add `AttackerWarProgressDecayPerMonth` config** — add `public double AttackerWarProgressDecayPerMonth { get; set; } = 2.5;` to `src/Game.Configs/GameSettings.cs` sibling to `RecruitsMonthlyIncreasePercent`, and `"attackerWarProgressDecayPerMonth": 2.5` to `Assets/Configs/game_settings.json` sibling to `"recruitsMonthlyIncreasePercent"`.
- [x] **Add `Wars` helper class** — create `src/Game.Systems/Wars.cs` (static class, non-"System") with `IsInWar(IReadOnlyWorld world, string countryId) : bool` (scans `WarParticipant` archetypes for any `CountryId` match, any `Kind`); `DeclareWar(World world, string attackerCountryId, string defenderCountryId, DateTime currentTime) : bool` (returns `false` if `attackerCountryId == defenderCountryId` or `IsInWar` is true for either id; otherwise creates the `War`+`WarProgress` entity with `WarId = $"war_{attackerCountryId}_{defenderCountryId}_{currentTime.Ticks}"` and `Value = 0`, plus two `WarParticipant` entities (`Attacker`, `Defender`), returns `true`); `StopWar(World world, string countryId) : bool` (scans `WarParticipant` for a `CountryId` match, returns `false` if none; otherwise destroys the matching `War`+`WarProgress` entity and both `WarParticipant` entities sharing that `WarId`, returns `true`). None of these call another system's entry point.
- [x] **Add `WarSystem` class** — create `src/Game.Systems/WarSystem.cs` (static class, "System" suffix, single `Update` entry point) with `Update(World world, DateTime previousTime, DateTime currentTime, double decayPerMonth)`: computes `isMonthBoundary` exactly like `ControlSystem.Update`, returns immediately if not a boundary; otherwise for every entity carrying `WarProgress`, sets `Value = Math.Max(-100, Value - decayPerMonth)`.
- [x] **Add `DebugDeclareWarCommand`** — create `src/Game.Commands/DebugDeclareWarCommand.cs`: `public struct DebugDeclareWarCommand : ICommand { [CountryId] public string AttackerCountryId; [CountryId] public string DefenderCountryId; }`.
- [x] **Add `DebugStopWarCommand`** — create `src/Game.Commands/DebugStopWarCommand.cs`: `public struct DebugStopWarCommand : ICommand { [CountryId] public string CountryId; }`.
- [x] **Wire `WarSystem.Update` into `GameLogic.Update`** — in `src/Game.Main/GameLogic.cs`, add `WarSystem.Update(_world, _previousTime, currentTime, GameSettings.AttackerWarProgressDecayPerMonth);` immediately after the `ControlSystem.Update(_world, _previousTime, currentTime);` call.
- [x] **Wire the two new command loops into `GameLogic.Update`** — after the existing `foreach (var cmd in _commandAccessor.ReadDebugClearCountryRelationCommand().AsSpan())` loop, add the two loops from the spec's Tech Notes calling `Wars.DeclareWar(_world, cmd.AttackerCountryId, cmd.DefenderCountryId, currentTime)` and `Wars.StopWar(_world, cmd.CountryId)` directly, discarding the `bool` return, with no wrapping private method.
- [x] **Add `WarsTests.cs`** — create `src/Game.Tests/WarsTests.cs` (mirroring `CountryRelationsTests.cs`'s style/helpers) covering: declare-war creates the war entity with `Value == 0` plus both participants and `IsInWar` becomes true for both sides; re-declaring the same pair while the first war is active is a no-op (existing war/progress untouched, no second war created); declaring a country against itself is a no-op; a country already in *any* war (as either side, against any opponent) blocks a new declare-war naming it on either side; stop-war on a participant hard-deletes the `War`/`WarProgress`/both `WarParticipant` entities and both former participants report `IsInWar == false` afterward; stop-war on a country not in any war is a no-op returning `false`.
- [x] **Add `WarSystemTests.cs`** — create `src/Game.Tests/WarSystemTests.cs` (mirroring `ControlSystemTests.cs`'s month-boundary test style, reusing the same `Jan31`/`Feb1`/`Jan1`/`Jan2` boundary pattern) covering: a month boundary decays `WarProgress.Value` by `decayPerMonth`; decay floor-clamps at `-100` and does not go lower on a subsequent boundary; no month boundary means no change.
- [x] **Add a `GameLogic`-level integration test** — in `src/Game.Tests/WarsTests.cs` (or a new `DebugWarCommandsTests.cs`, matching how `CountryRelationsTests.cs` has `debug_commands_set_and_clear_relation_through_game_logic`), push `DebugDeclareWarCommand` then `DebugStopWarCommand` through `GameLogic.Commands`/`GameLogic.Update` and assert `Wars.IsInWar` transitions correctly, confirming the wiring in `GameLogic.cs` is correct end to end.
- [x] **Run the full test suite** — use the `dotnet-test` skill to run `src/GlobalStrategy.Core.sln`, confirming the new tests pass and `Game.Tests/ParamSuggestionAttributeTests.cs`'s `EveryCommand_DomainIdMember_CarriesSuggestionAttribute` still passes for the two new commands.

## Tests

- `src/Game.Tests/WarsTests.cs` (new) — unit coverage for `Wars.DeclareWar`/`Wars.StopWar`/`Wars.IsInWar`, following `CountryRelationsTests.cs`'s helper/assertion style (plain `World` instances, `CountEntities<T>` helper where useful, plus one `GameLogic`-driven test pushing the two debug commands through `Commands`/`Update`).
- `src/Game.Tests/WarSystemTests.cs` (new) — unit coverage for `WarSystem.Update`'s month-boundary decay and floor-clamp, following `ControlSystemTests.cs`'s `DateTime` boundary-constant style (`Jan31`/`Feb1` crossing vs. `Jan1`/`Jan2` same-month).
- Existing `src/Game.Tests/ParamSuggestionAttributeTests.cs` already covers the new commands automatically (reflection-based) — no changes needed there, just confirm it still passes.
- Run via the `dotnet-test` skill against `src/GlobalStrategy.Core.sln`.

## Constitution Check

No conflicts found — plan aligns with all principles:
- **ECS for all game logic** — all new state (`War`, `WarProgress`, `WarParticipant`) and behavior (`Wars.cs`, `WarSystem.cs`) lives under `src/`; no MonoBehaviour touched.
- **VContainer is the sole DI mechanism** — no new service registration; `Wars`/`WarSystem` are static helpers called directly from `GameLogic`, same as `CountryRelations`/`ControlSystem`.
- **UI Toolkit only** — no UI is added; feature is explicitly backend/debug-terminal-only per the spec's Out of Scope list.
- **Plan before implement** — this plan is the gate; no code changes are made until it is approved.
- **Spec before plan for feature work** — `spec.md` already exists and was approved before this plan was written.
- **File organisation** — this plan file lives at `Docs/Specs/26_07_25_06_war-mechanics-core/plan.md`, matching the existing spec folder.
- **One asmdef per feature folder** — no new Unity `Assets/Scripts/` feature folder is introduced; `src/` assemblies are unaffected by this rule (it governs `Assets/Scripts/`).
- **C# code style** — new files will use tabs, brace-on-same-line, `_`-prefixed private fields, and no redundant access modifiers, matching `CountryRelations.cs`/`ControlSystem.cs`/`CountryRelation.cs` precedent.

Use the implement skill to start working on the plan or request changes.
