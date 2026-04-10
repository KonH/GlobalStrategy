# Plan: Player Country

## Goal

Introduce a "player country" concept: one country entity carries a `Player` component. Show the player country name in the top-left corner (smaller than the selected-country block). When a non-player country is selected, show a "Select" button that transfers the `Player` component to the selected country. Default player country: Russian Empire (`russian_empire`).

---

## Approach

Follow the existing ECS command pipeline:

1. New `Player` component on a country entity (ECS side in `src/`).
2. New `SelectPlayerCountryCommand` + `SelectPlayerCountrySystem` (mirrors `SelectCountrySystem`).
3. `VisualState` gains a `PlayerCountryState` (mirrors `SelectedCountryState`).
4. `VisualStateConverter` reads the entity that has `Player` and populates `PlayerCountryState`.
5. Unity UI: new `PlayerCountryView` in top-left; `CountryInfoView` shows a "Select" button when selected ≠ player.
6. `HUDDocument` wires the new state and button command.
7. UXML/USS: add `player-country` template + layout class.

---

## Steps

### 1. ECS — `Game.Components`

- Add `src/Game.Components/Player.cs`:
  ```csharp
  namespace GS.Game.Components {
      public struct Player { }
  }
  ```

### 2. ECS — `Game.Commands`

- Add `src/Game.Commands/SelectPlayerCountryCommand.cs`:
  ```csharp
  namespace GS.Game.Commands {
      public record struct SelectPlayerCountryCommand(string CountryId) : ICommand;
  }
  ```

### 3. ECS — `Game.Systems`

- Add `src/Game.Systems/SelectPlayerCountrySystem.cs` (same pattern as `SelectCountrySystem`):
  moves `Player` component from the current player entity to the target entity.

### 4. ECS — `Game.Main / GameLogic`

- In `GameLogic` constructor, after creating country entities, find the one with `CountryId == "russian_empire"` and `Add(_world, entity, new Player())`.
- In `GameLogic.Update`, call `SelectPlayerCountrySystem.Update(...)` with the new command read.

### 5. ECS — `Game.Main / VisualState`

- Add `PlayerCountryState` class (same shape as `SelectedCountryState`) to `VisualState.cs`.
- Add `PlayerCountry` property to `VisualState`.

### 6. ECS — `Game.Main / VisualStateConverter`

- Add `UpdatePlayerCountry(world)`: query for archetype `[Country, Player]`, call `_state.PlayerCountry.Set(true, countryId)` or `Set(false, "")`.
- Call it from `Update`.

### 7. Rebuild DLL

- `dotnet build src/GlobalStrategy.Core.sln -c Release`

### 8. Unity UI — UXML

- Add new template file `Assets/UI/HUD/PlayerCountry/PlayerCountry.uxml`:
  - `Label` named `player-country-name`
  - `Button` named `select-player-button` (hidden when player country is selected)
- Register template in `HUD.uxml`; add `<ui:Instance>` with class `player-country-panel`.

### 9. Unity UI — USS

- `Assets/UI/HUD/PlayerCountry/PlayerCountry.uss`: style `player-country-name` at ~24px (half the selected-country font size).
- In `HUD.uss`: position `.player-country-panel` top-left (absolute, top: 0, left: 0).

### 10. Unity UI — `PlayerCountryView.cs`

- New `Assets/Scripts/Unity/UI/PlayerCountryView.cs`:
  - Constructor takes root `VisualElement`.
  - `Refresh(PlayerCountryState state)`:
    - Show/hide root based on `state.IsValid`.
    - Set label text from localization key.

### 11. Unity UI — `CountryInfoView.cs`

- Add `Button` named `select-player-button` to `CountryInfo.uxml`.
- In `CountryInfoView`:
  - Query the button in constructor.
  - `Refresh(SelectedCountryState selected, PlayerCountryState player)`:
    - Show button only when `selected.IsValid && selected.CountryId != player.CountryId`.
  - Expose `OnSelectClicked` action wired to the button.

### 12. Unity UI — `HUDDocument.cs`

- Instantiate `PlayerCountryView` from `root.Q("player-country")`.
- Subscribe/unsubscribe `_state.PlayerCountry.PropertyChanged`.
- On any country or player state change: call `_playerCountryView.Refresh(_state.PlayerCountry)` and `_countryInfo.Refresh(_state.SelectedCountry, _state.PlayerCountry)`.
- Wire `_countryInfo.OnSelectClicked` → `_commands.Push(new SelectPlayerCountryCommand(_state.SelectedCountry.CountryId))`.

### 13. Localization

- Add `country_name.russian_empire` key to all locale JSON files (if not already present).
