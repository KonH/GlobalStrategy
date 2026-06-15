# Plan: Character Opinion

## Spec
Each country character carries an opinion score toward each organization. Opinion is stored as a base integer plus a list of time-decaying modifiers; the effective value is clamped to [-100, +100]. A debug "Improve Opinion" button in the HUD character-debug-container applies a +50 modifier (decaying by -1/month) to every country character of the selected country, for the player's organization. The opinion score is surfaced in the character card UI: the character's name label moves from the info column to an overlay at the bottom of the portrait, and a signed opinion counter (color-coded positive/negative) appears after the role label.

## Goal
Add a visible, manipulable opinion relationship between country characters and the player's organization, with monthly decay and a cheat debug action.

## Approach
All game logic lives in the ECS layer (`src/`). A new `CharacterOpinion` component (marked `[Savable]`) is attached to every country character entity at initialization. A new `OpinionSystem` handles monthly modifier decay. The command/accessor/GameLogic wiring mirrors the existing debug command pattern. The Unity UI layer reads opinion from `CharacterStateEntry.Opinion` (added to `VisualState`) rather than accessing ECS directly.

## Section 1 — Agent Steps

- [x] **Add `OpinionModifier` struct** — Create `src/Game.Components/OpinionModifier.cs` with `public struct OpinionModifier { public string SourceId; public int Value; public int ChangeValue; }`. No `[Savable]` attribute (it is a nested type inside `CharacterOpinion`).

- [x] **Add `CharacterOpinion` component** — Create `src/Game.Components/CharacterOpinion.cs`:
  ```
  [Savable]
  public struct CharacterOpinion {
      public Dictionary<string, int> BaseOpinionPerOrg;
      public Dictionary<string, List<OpinionModifier>> ModifiersPerOrg;
  }
  ```
  Initialize dictionaries to non-null in the struct or at creation time.

- [x] **Add `DebugImproveOpinionCommand` command** — Create `src/Game.Commands/DebugImproveOpinionCommand.cs` with `public struct DebugImproveOpinionCommand : ICommand { public string CountryId; public string OrgId; }`.

- [x] **Extend `CommandAccessor`** — The generated partial in `src/Game.Main/CommandAccessor.cs` follows the pattern of `ReadDebugCycleCharacterCommand()`. Add `ReadDebugImproveOpinionCommand()` returning `ReadCommands<DebugImproveOpinionCommand>` by appending the typed buffer via the source generator (or by hand if the generator requires it). Verify the pattern by reading how `ReadDebugCycleCharacterCommand` is exposed in `CommandAccessor.g.cs` if it exists, and mirror it exactly.

- [x] **Add `CharacterOpinion` to character entities in `InitSystem`** — In `src/Game.Main/InitSystem.cs`, inside `CreateCharacterEntities()`, after `world.Add(charEntity, new Character { ... })`, add:
  ```csharp
  world.Add(charEntity, new CharacterOpinion {
      BaseOpinionPerOrg = new Dictionary<string, int>(),
      ModifiersPerOrg   = new Dictionary<string, List<OpinionModifier>>()
  });
  ```
  Country characters only (not org characters in `CreateOrgSlots`).

- [x] **Create `OpinionSystem`** — Create `src/Game.Systems/OpinionSystem.cs` as a `public static class` in `namespace GS.Game.Systems`. Implement `public static void Update(World world, DateTime previousTime, DateTime currentTime)`. On month boundary (`previousTime.Month != currentTime.Month || previousTime.Year != currentTime.Year`): iterate all archetypes with `TypeId<CharacterOpinion>.Value`, get `ref CharacterOpinion opinion`, for each org key in `ModifiersPerOrg` iterate the modifier list, apply `modifier.Value += modifier.ChangeValue`, then remove modifiers where `Value == 0`. Use index-based loop iterating backwards to safely remove elements.

- [x] **Add `CharacterStateEntry.Opinion` to `VisualState`** — In `src/Game.Main/VisualState.cs` (class `CharacterStateEntry`, lines 117–128): add `public int Opinion { get; }` property and extend the constructor to accept `int opinion` as a final parameter, storing it in `Opinion`.

- [x] **Update `VisualStateConverter.UpdateCharacters`** — In `src/Game.Main/VisualStateConverter.cs`, `UpdateCharacters()`: read the player org ID from the `Organization` component in world (same approach as `UpdateOrgMap` or `UpdatePlayerOrganization`). During the archetype loop over `TypeId<Character>.Value`, also collect `CharacterOpinion` components by character ID into a `Dictionary<string, CharacterOpinion>`. In the `entries` build loop, compute effective opinion:
  ```csharp
  int effective = 0;
  if (opinionMap.TryGetValue(charId, out var op)) {
      int base_ = op.BaseOpinionPerOrg.TryGetValue(orgId, out var b) ? b : 0;
      int mods  = 0;
      if (op.ModifiersPerOrg.TryGetValue(orgId, out var list))
          foreach (var m in list) mods += m.Value;
      effective = Math.Clamp(base_ + mods, -100, 100);
  }
  ```
  Pass `effective` as the `opinion` argument to `new CharacterStateEntry(...)`.

- [x] **Call `OpinionSystem.Update` in `GameLogic.Update`** — In `src/Game.Main/GameLogic.cs`, after `InfluenceSystem.Update(_world, _previousTime, currentTime);` (line 65), add:
  ```csharp
  OpinionSystem.Update(_world, _previousTime, currentTime);
  ```

- [x] **Handle `DebugImproveOpinionCommand` in `GameLogic.Update`** — In `src/Game.Main/GameLogic.cs`, after the existing debug character command handlers (after line ~93), add:
  ```csharp
  foreach (var cmd in _commandAccessor.ReadDebugImproveOpinionCommand().AsSpan()) {
      ApplyDebugImproveOpinion(cmd.CountryId, cmd.OrgId);
  }
  ```
  Add private method `ApplyDebugImproveOpinion(string countryId, string orgId)`: iterate archetypes with both `TypeId<Character>.Value` and `TypeId<CharacterOpinion>.Value`; for each entity where `character.CountryId == countryId`, get `ref CharacterOpinion opinion`, ensure `ModifiersPerOrg[orgId]` list exists, add `new OpinionModifier { SourceId = "cheat_improve_opinion", Value = 50, ChangeValue = -1 }`.

- [x] **Add "Improve Opinion" button in `HUDDocument`** — In `Assets/Scripts/Unity/UI/HUDDocument.cs`, inside the `character-debug-container` population block (after line 137 where drop buttons are added), add a single button per country role loop — OR add one global button outside the role loop. Per the spec, the button applies the command to all characters in the selected country, so one button is sufficient. Add it after the role loop:
  ```csharp
  var improveOpinionBtn = new Button(() => PushImproveOpinionCommand(_state?.SelectedCountry?.CountryId ?? ""));
  improveOpinionBtn.text = "Improve Opinion";
  improveOpinionBtn.AddToClassList("gs-btn");
  improveOpinionBtn.AddToClassList("gs-btn--small");
  improveOpinionBtn.AddToClassList("debug-panel-button");
  characterDebugContainer.Add(improveOpinionBtn);
  ```
  Add private method `PushImproveOpinionCommand(string countryId)`: if `string.IsNullOrEmpty(countryId)` return; resolve player org ID from `_state.PlayerOrganization.OrgId` (if invalid, return); push `new DebugImproveOpinionCommand { CountryId = countryId, OrgId = orgId }`.

- [x] **Update `CharactersView.BuildCharacterCard` — name overlay** — In `Assets/Scripts/Unity/UI/CharactersView.cs`, move the `nameLabel` construction and `info.Add(nameLabel)` call: instead of adding the name to `info`, create an overlay `VisualElement` inside `portrait`, position it at the bottom using `position: Absolute` (via class), add the name label to it, and add the overlay to `portrait`. Concretely: after building `portrait`, add:
  ```csharp
  var nameOverlay = new VisualElement();
  nameOverlay.AddToClassList("char-name-overlay");
  var nameLabel = new Label(string.Join(" ", nameParts));
  nameLabel.AddToClassList("char-name");
  nameOverlay.Add(nameLabel);
  portrait.Add(nameOverlay);
  ```
  Remove the original `nameLabel` construction and `info.Add(nameLabel)` lines.

- [x] **Update `CharactersView.BuildCharacterCard` — opinion label** — After `info.Add(roleLabel)`, insert:
  ```csharp
  string opinionText = entry.Opinion >= 0 ? $"+{entry.Opinion}" : $"{entry.Opinion}";
  var opinionLabel = new Label(opinionText);
  opinionLabel.AddToClassList("char-opinion");
  opinionLabel.AddToClassList(entry.Opinion < 0 ? "gs-color-negative" : "gs-color-positive");
  info.Add(opinionLabel);
  ```

- [x] **Add CSS for name overlay and opinion label to `SharedStyles.uss`** — In `Assets/UI/Shared/SharedStyles.uss`, inside the `/* ===== Compact country character card ===== */` section, add after `.char-portrait-area { ... }`:
  ```css
  .char-name-overlay {
      position: absolute;
      bottom: 0;
      left: 0;
      right: 0;
      background-color: rgba(14, 26, 46, 0.75);
      padding: 3px 5px;
  }

  .char-opinion {
      font-size: 14px;
      -unity-font-style: bold;
      margin-top: 2px;
  }
  ```
  Note: `position: absolute` on a child of `.char-portrait-area` requires `.char-portrait-area` to remain `position: relative` (its default). Verify this is not overridden.

- [x] **Build and verify** — Run `dotnet build src/GlobalStrategy.Core.sln -c Release` to confirm the `src/` layer compiles without errors. Then in Unity (via `refresh_unity`) check `read_console` for compilation errors in the Unity scripts.

## Section 2 — User Steps

### 1. Verify in Play mode
Enter Play mode, select a country, open the debug panel, press "Improve Opinion". The character cards should show a `+50` opinion counter in green. Advance time by one month and verify the counter drops to `+49`.

## Tests

Add to `src/Game.Tests/CharacterVisualStateTests.cs`:
- `character_state_entries_have_opinion_field` — after init and country select, each `CharacterStateEntry.Opinion` is 0 (no modifiers yet).
- `opinion_included_in_visual_state_after_cheat_command` — push `DebugImproveOpinionCommand` for the selected country's org, call `Update(0f)`, assert each entry's `Opinion == 50`.

Add a new `src/Game.Tests/OpinionSystemTests.cs`:
- `opinion_modifier_decays_by_one_per_month` — create a world, add a `CharacterOpinion` entity with a modifier `{ Value=3, ChangeValue=-1 }`, call `OpinionSystem.Update` across a month boundary, assert `Value == 2`.
- `opinion_modifier_removed_when_reaches_zero` — start with `Value=1, ChangeValue=-1`, decay one month, assert the modifier list is empty.
- `effective_opinion_clamped_to_minus100_plus100` — set `BaseOpinionPerOrg = 80`, add modifier `Value=50`, assert effective = 100.
- `effective_opinion_clamped_negative` — set `BaseOpinionPerOrg = -80`, add modifier `Value=-50`, assert effective = -100.

## Constitution Check
No conflicts found — plan aligns with all principles. All game logic resides in `src/` ECS layer with no Unity references. VContainer remains the sole DI mechanism. UI uses UI Toolkit exclusively. CharacterOpinion is `[Savable]` as required by the spec; the modifier list is embedded in the component and serialized with it.

Use /implement to start working on the plan or request changes.
