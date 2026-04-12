# 13 — Save/Load Feature

## Goal

Implement a complete save/load system: JSON world-state snapshots, auto-save, a main menu scene, a country-selection scene, an in-game pause menu, shared settings and load windows.

---

## Architecture Overview

```
Game.Configs:    GameSettings += DefaultLocale, AutoSaveInterval
Game.Components: AppSettings (singleton ECS), TriggerSave (event tag)
                 [Savable] attribute marks components included in snapshots
Game.Commands:   SaveGameCommand
Game.Main:       WorldSnapshot, EntitySnapshot, SaveHeader
                 IPersistentStorage, ISnapshotSerializer
                 SaveSystem, LoadSystem, AutoSaveSystem
                 SaveFileManager, SelectCountryLogic
Unity:           PersistentStorage (IPersistentStorage via System.IO)
                 NewtonsoftSnapshotSerializer (ISnapshotSerializer via Newtonsoft.Json)
                 SceneTransitionArgs (static)
                 Scenes: MainMenu, SelectCountry (new) + Game (existing)
                 UI: MainMenu, SelectCountry, LoadWindow, SettingsWindow, GameMenu
```

Flow:
```
MainMenu → [Play]   → SelectCountry → [Start] → Game (new game, player country set)
MainMenu → [Load]   → LoadWindow   → [Pick]  → Game (load save)
MainMenu → [Resume] → Game (load last save)
Game     → [Esc / Menu button] → GameMenu (auto-pauses) → [Main Menu] → MainMenu
```

---

## Steps

### Core (`src/`)

#### 1. `Game.Configs` — extend `GameSettings`

Add to `GameSettings.cs`:
```csharp
public string DefaultLocale { get; set; } = "en";
public string AutoSaveInterval { get; set; } = "monthly"; // "daily" | "monthly" | "yearly"
```

Update `Assets/StreamingAssets/Configs/game_settings.json` with new fields.

---

#### 2. `Game.Components` — `[Savable]` attribute + `AppSettings` + `TriggerSave`

`SavableAttribute.cs`:
```csharp
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public class SavableAttribute : Attribute { }
```

Any component decorated with `[Savable]` is automatically included in saves — no other registration needed. Add the attribute to all persistent components: `GameTime`, `Country`, `IsSelected`, `Player`, `Locale`, `AppSettings`, `Resource`, `ResourceOwner`, `ResourceLink`, `ResourceEffect`.

`AppSettings.cs`:
```csharp
public enum AutoSaveInterval { Daily, Monthly, Yearly }
[Savable]
public struct AppSettings {
    public string Locale;
    public AutoSaveInterval AutoSaveInterval;
}
```

`TriggerSave.cs` — empty tag component (no `[Savable]`; ephemeral, never persisted).

---

#### 3. `Game.Commands` — `SaveGameCommand`

```csharp
public record struct SaveGameCommand() : ICommand;
```

Loading is not a mid-loop command — it runs before `GameLogic` starts ticking (via `GameLogic.LoadState`).

---

#### 4. `Game.Main` — `WorldSnapshot` + `EntitySnapshot` + `SaveHeader`

No component-specific fields. The snapshot captures every entity that has at least one `[Savable]` component, and every `[Savable]` component on that entity.

`WorldSnapshot.cs`:
```csharp
public class WorldSnapshot {
    public SaveHeader Header { get; set; } = new();
    public List<EntitySnapshot> Entities { get; set; } = new();
}

public class SaveHeader {
    public string SaveName { get; set; } = "";      // used as filename stem
    public string PlayerCountryId { get; set; } = ""; // for save list display
    public DateTime GameDate { get; set; }             // for save list display
    public DateTime SavedAt { get; set; }
}

public class EntitySnapshot {
    // Key: component type's full name (e.g. "GS.Game.Components.Country")
    // Value: component field values; serializer handles primitive/string/DateTime/enum
    public Dictionary<string, Dictionary<string, object?>> Components { get; set; } = new();
}
```

Adding a new savable component requires only `[Savable]` on the struct — no changes to save/load code.

---

#### 5. `Game.Main` — `IPersistentStorage` + `ISnapshotSerializer`

```csharp
public interface IPersistentStorage {
    void Write(string relativePath, string content);
    string Read(string relativePath);
    bool Exists(string relativePath);
    void Delete(string relativePath);
    IReadOnlyList<string> List(string relativeDir);
}

public interface ISnapshotSerializer {
    string Serialize(WorldSnapshot snapshot);
    WorldSnapshot Deserialize(string json);
}
```

`ISnapshotSerializer` lives in `Game.Main`; the Unity implementation uses Newtonsoft.Json (`NewtonsoftSnapshotSerializer`). This keeps `Game.Main` free of any JSON library dependency.

---

#### 6. `Game.Main` — `SaveSystem` + `LoadSystem`

**`SaveSystem`** (static):

```
BuildSnapshot(World world) → WorldSnapshot
```
1. Scan `Game.Components` assembly for all types with `[Savable]` attribute.
2. Iterate all live entities in `world`.
3. For each entity: check which `[Savable]` types are present via `world.Has<T>(entity)`.
4. For each present component: read via `world.Get<T>(entity)`, reflect all public fields into `Dictionary<string, object?>`.
5. If entity has any savable components, add `EntitySnapshot` to list.
6. Build `SaveHeader` by finding the entity with `Player` → its `Country.CountryId`, and the entity with `GameTime` → its `CurrentTime`.
7. Save name format: `{playerCountryId}_{gameDate:yyyy-MM-dd}`.

Caller (e.g. `GameLogic`): `storage.Write($"Saves/{name}.json", serializer.Serialize(snapshot))`.

**`LoadSystem`** (static):

```
Apply(WorldSnapshot snapshot, World world)
```
1. `world.DestroyAll()` — clear existing state.
2. Scan `Game.Components` assembly to build a `Dictionary<string, Type>` keyed by full type name.
3. For each `EntitySnapshot`: create entity, then for each component entry use reflection to instantiate the struct and set its fields, then `world.Add(entity, component)` via reflection.

After `LoadSystem.Apply` returns, `GameLogic` re-queries to refresh its singleton entity IDs:
```csharp
void RefreshSingletonEntities() {
    _gameTimeEntity = FindEntityWith<GameTime>();
    _localeEntity   = FindEntityWith<Locale>();
    _settingsEntity = FindEntityWith<AppSettings>();
}
```

---

#### 7. `Game.Main` — `AutoSaveSystem`

Static `Update(World, int settingsEntity, int gameTimeEntity, DateTime previousTime, IWriteOnlyCommandAccessor)`:
- Read `AppSettings.AutoSaveInterval` and compare `previousTime` vs current `GameTime.CurrentTime` for day/month/year boundary crossing.
- On boundary: push `SaveGameCommand`.

---

#### 8. `Game.Main` — extend `GameLogic`

`GameLogicContext` gains:
- `IPersistentStorage Storage { get; }`
- `ISnapshotSerializer Serializer { get; }`
- `string InitialPlayerCountryId { get; }` (default `"Russian_Empire"`; overridden when loading from SelectCountry)

Constructor:
- Create `_settingsEntity` with `AppSettings` (from `GameSettings` config).
- Assign initial `Player` component based on `context.InitialPlayerCountryId`.

`Update`:
- After `TimeSystem.Update`, call `AutoSaveSystem.Update(...)`.
- Handle `SaveGameCommand`:
  ```csharp
  var snapshot = SaveSystem.BuildSnapshot(_world);
  _context.Storage.Write($"Saves/{snapshot.Header.SaveName}.json",
      _context.Serializer.Serialize(snapshot));
  ```

New method:
```csharp
public void LoadState(string saveName) {
    var json = _context.Storage.Read($"Saves/{saveName}.json");
    var snapshot = _context.Serializer.Deserialize(json);
    LoadSystem.Apply(snapshot, _world);
    RefreshSingletonEntities();
}
```

---

#### 9. `Game.Main` — `SelectCountryLogic`

Slim class for the country-selection screen — no `GameTime`, no resources, no locale:

```csharp
public class SelectCountryLogic {
    public VisualState VisualState { get; }
    public IWriteOnlyCommandAccessor Commands { get; }
    public SelectCountryLogic(IConfigSource<CountryConfig> countryConfig) { ... }
    public void Update() { } // no deltaTime; pure selection logic
}
```

Creates country entities, runs `SelectCountrySystem` and `SelectPlayerCountrySystem`. No player is assigned by default (no `Player` component on any entity until the user clicks "Start").

---

#### 10. `Game.Main` — `SaveFileManager`

```csharp
public class SaveFileInfo {
    public string SaveName { get; set; } = "";
    public string PlayerCountryId { get; set; } = "";
    public DateTime GameDate { get; set; }
    public DateTime SavedAt { get; set; }
}

public class SaveFileManager {
    public SaveFileManager(IPersistentStorage storage, ISnapshotSerializer serializer) { ... }
    public IReadOnlyList<SaveFileInfo> ListSaves() { ... }
    public void DeleteSave(string saveName) { ... }
    public SaveFileInfo? GetLastSave() { ... }
}
```

Listing: reads `Saves/*.json`, deserializes only `SaveHeader` from each file (using `serializer`), orders by `SavedAt` desc. File content drives metadata — no filename parsing needed.

`LoadSave` is not on `SaveFileManager`; loading is done directly by `GameLogic.LoadState(saveName)` which reads and deserializes the full snapshot.

---

#### 11. Rebuild DLLs

```
dotnet build src/GlobalStrategy.Core.sln -c Release
```

Verify no compilation errors before Unity work.

---

### Unity

#### 12. `PersistentStorage.cs` + `NewtonsoftSnapshotSerializer.cs`

`PersistentStorage.cs` — plain C# in `Assets/Scripts/Unity/Save/`:
```csharp
class PersistentStorage : IPersistentStorage {
    readonly string _root = Application.persistentDataPath;
    // Write/Read/Exists/Delete/List using System.IO
}
```

`NewtonsoftSnapshotSerializer.cs` — in same folder:
```csharp
class NewtonsoftSnapshotSerializer : ISnapshotSerializer {
    public string Serialize(WorldSnapshot snapshot) => JsonConvert.SerializeObject(snapshot, Formatting.Indented);
    public WorldSnapshot Deserialize(string json) => JsonConvert.DeserializeObject<WorldSnapshot>(json)!;
}
```

Both registered in `GameLifetimeScope` as singletons.

---

#### 13. `SceneTransitionArgs.cs`

Static class (no MonoBehaviour needed) carrying arguments across scene loads:

```csharp
static class SceneTransitionArgs {
    public static string? SaveNameToLoad;      // set before LoadGame
    public static string? InitialPlayerCountry; // set from SelectCountry
}
```

---

#### 14. `SceneLoader.cs`

Static helpers: `LoadMainMenu()`, `LoadSelectCountry()`, `LoadGame(string? saveName = null, string? playerCountryId = null)` — sets `SceneTransitionArgs`, then calls `SceneManager.LoadScene`.

---

#### 15. Update `GameLifetimeScope`

- Register `PersistentStorage` and `NewtonsoftSnapshotSerializer` as singletons.
- Register `SaveFileManager` as singleton (inject both above).
- Pass `PersistentStorage` and `NewtonsoftSnapshotSerializer` into `GameLogicContext`.
- Set `GameLogicContext.InitialPlayerCountryId` from `SceneTransitionArgs.InitialPlayerCountry` if present.
- In `IStartable.Start`: if `SceneTransitionArgs.SaveNameToLoad != null`, call `gameLogic.LoadState(saveName)`.

---

#### 16. New scenes

- `Assets/Scenes/Menu/MainMenu.unity`
- `Assets/Scenes/Menu/SelectCountry.unity`

Register both in `ProjectSettings/EditorBuildSettings.asset`.

---

#### 17. MainMenu scene

**No ECS.** `MainMenuDocument` MonoBehaviour injected with `SaveFileManager`.

**Background:** `MapScrollBackground.cs` — simple component panning camera very slowly over the world map. Non-interactive (no click handler, no EventSystem interaction).

**UXML:** `Assets/UI/Modal/MainMenu/MainMenu.uxml`
```
blackfade overlay
vertical menu:
  Play button
  Resume button   (hidden when no saves)
  Load button     (hidden when no saves)
  Settings button
  Exit button
```

**`MainMenuDocument.cs`:**
- `Start`: query `saveFileManager`, show/hide Resume + Load based on `GetLastSave() != null`.
- Play → `SceneLoader.LoadSelectCountry()`
- Resume → `SceneLoader.LoadGame(saveFileManager.GetLastSave()!.SaveName)`
- Load → show `LoadWindowDocument` (additive overlay or toggle display)
- Settings → show `SettingsWindowDocument`
- Exit → `Application.Quit()`

---

#### 18. `LoadWindow` UI

**UXML:** `Assets/UI/Modal/LoadWindow/LoadWindow.uxml` — `ScrollView` list + Back button.

**`LoadWindowDocument.cs`:**
- On show: rebuild list from `saveFileManager.ListSaves()`.
- Each row: country name + game date + Load button + Delete button.
- Load → `SceneLoader.LoadGame(saveName)`.
- Delete → `saveFileManager.DeleteSave(saveName)`, rebuild list.
- Back → hide window.

---

#### 19. `SettingsWindow` UI

**UXML:** `Assets/UI/Modal/SettingsWindow/SettingsWindow.uxml`
- Language: EN / RU buttons (or `RadioButtonGroup`)
- Auto-save: Daily / Monthly / Yearly
- Back button

**`SettingsWindowDocument.cs`:**
- Reads current values from `AppSettings` (injected `VisualState` if in-game, or from saved config if on main menu).
- On language change: push `ChangeLocaleCommand` (if in-game) or write `settings.json` directly (if on main menu).
- On auto-save change: update `AppSettings` component via new `ChangeAutoSaveIntervalCommand` or direct write.
- This window is shared between MainMenu and GameMenu; wired differently in each context.

---

#### 20. SelectCountry scene

**Context:** `SelectCountryLifetimeScope` — registers `SelectCountryLogic` (manual tick from document), map rendering setup.

**UXML:** `Assets/UI/Modal/SelectCountry/SelectCountry.uxml`
- Country name label (empty until selection)
- "Start Game" button (disabled until `VisualState.SelectedCountry.IsValid`)
- Back button

**`SelectCountryDocument.cs`:**
- Each `Update`: calls `selectCountryLogic.Update()`.
- Subscribes to `VisualState.SelectedCountry.PropertyChanged`: update label, enable/disable Start.
- No time controls displayed.
- Start → `SceneLoader.LoadGame(playerCountryId: state.SelectedCountry.CountryId)`.
- Back → `SceneLoader.LoadMainMenu()`.

`SelectCountryLifetimeScope` registers `SelectCountryLogic` and the same map / locale config sources as `GameLifetimeScope`.

---

#### 21. GameMenu

**Trigger:** `Escape` key in `GameInputHandler` or dedicated Menu button in HUD. On open: `commands.Push(new PauseCommand())`.

**UXML:** `Assets/UI/Modal/GameMenu/GameMenu.uxml`
- Blackfade background
- Resume, Save, Settings, Main Menu buttons

**`GameMenuDocument.cs`:**
- Resume → push `UnpauseCommand`, hide menu.
- Save → push `SaveGameCommand`.
- Settings → show `SettingsWindowDocument` (remains paused).
- Main Menu → `SceneLoader.LoadMainMenu()`.

---

#### 22. Time controls in SelectCountry

The `Time` template is **not** included in `SelectCountry.uxml`. Time controls remain game-scene-only. The `TimeInputHandler` is also absent from `SelectCountryLifetimeScope`.

---

#### 23. In-game "Select" (transfer player country) button

Remains in the Game scene `CountryInfoView` as before. The SelectCountry scene's "Start Game" button is separate and distinct.

---

## Tests

### `Game.Tests` additions

- **`AutoSaveSystemTests`** — daily/monthly/yearly boundary detection; command pushed on crossing; not pushed mid-period; no double-push per frame.
- **`SaveLoadRoundTripTests`** — populate world with `[Savable]` components → `SaveSystem.BuildSnapshot` → `LoadSystem.Apply` on fresh world → assert all entities and component field values match. Also verify non-`[Savable]` components (e.g. `TriggerSave`) are absent from snapshot.
- **`SavableDiscoveryTests`** — assert that every expected persistent component type in `Game.Components` carries `[Savable]`; guard against forgetting the attribute on new components.
- **`SaveFileManagerTests`** — mock `IPersistentStorage` + `ISnapshotSerializer`; verify list ordering by `SavedAt`, delete removes file, `GetLastSave` returns most recent.
