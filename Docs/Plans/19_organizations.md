# Plan 19: Organizations

## Goal

Introduce an Organizations layer: the player controls an organization (not a country directly). Organizations have a name, HQ country, and starting gold. The SelectCountry scene becomes SelectOrganization — showing only HQ countries on the map and displaying org info. The in-game HUD shows org name and org resources. Save file names use the org ID.

## Approach

- Add `OrganizationConfig` (src + JSON asset) with one entry: Illuminati, HQ = Great_Britain.
- Add `Organization` ECS component (savable) placed on a dedicated org entity in the ECS world.
- Rename `SelectCountryLogic` → `SelectOrgLogic`: filters selectable countries to HQ only; maps HQ country → org for VisualState.
- Rename `SelectCountryDocument` → `SelectOrgDocument`: displays org name + initial gold; passes org ID via `SceneTransitionArgs` when starting game.
- Hide non-HQ country GOs in the SelectOrg scene via `SelectOrgMapFilter` MonoBehaviour.
- Extend `GameLogicContext` with org config source + initial org ID.
- `GameLogic` creates org entity (`Organization` component + gold resource entity keyed to org ID).
- `VisualState` gains `PlayerOrganizationState`; `VisualStateConverter` populates it and sources `PlayerResources` from org entity.
- HUD `PlayerCountryView` → `PlayerOrgView`: shows org name + org resources.
- `SaveHeader` adds `OrganizationId`; save name becomes `{orgId}_{date}`.

## Steps

### 1. OrganizationConfig (src)

**`src/Game.Configs/OrganizationConfig.cs`** (new):
```csharp
public class OrganizationConfig {
    public List<OrganizationEntry> Organizations { get; set; } = new();
    public OrganizationEntry? FindByHqCountry(string countryId) { ... }
    public OrganizationEntry? FindById(string orgId) { ... }
}
public class OrganizationEntry {
    public string OrganizationId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string HqCountryId { get; set; } = "";
    public double InitialGold { get; set; }
}
```

### 2. Organization JSON asset

**`Assets/StreamingAssets/Configs/organizations.json`** (new):
```json
{
  "Organizations": [
    {
      "OrganizationId": "Illuminati",
      "DisplayName": "Illuminati",
      "HqCountryId": "Great_Britain",
      "InitialGold": 1000.0
    }
  ]
}
```

Add `_organizationsConfigAsset` TextAsset field to `GameLifetimeScope` and `SelectCountryLifetimeScope`.

### 3. ResourceOwner — rename CountryId → OwnerId

In **`src/Game.Components/ResourceOwner.cs`** (existing), rename the `CountryId` field to `OwnerId`. This makes the component generic enough to hold both country IDs and org IDs without semantic confusion. Update all call sites:
- `GameLogic.CreateResourceEntities` — `new ResourceOwner(entry.CountryId)` → `new ResourceOwner { OwnerId = entry.CountryId }`
- `VisualStateConverter.BuildResources` — compare `owners[i].OwnerId`
- `SaveSystem.BuildSnapshot` — if it queries `ResourceOwner` by field name, no change needed (the JSON key is the C# field name; old saves used `CountryId` — add a migration note in `LoadSystem` that old saves without this field are unsupported).

### 4. Organization ECS component (new)

**`src/Game.Components/Organization.cs`** (new):
```csharp
[Savable]
public struct Organization {
    public string OrganizationId;
    public string DisplayName;
}
```

`GameLogic` sets both fields from the config entry at entity creation. `VisualStateConverter` reads `DisplayName` directly from the component — no config dependency in the converter.

### 5. VisualState — PlayerOrganizationState

Add to **`src/Game.Main/VisualState.cs`**:
```csharp
public class PlayerOrganizationState : INotifyPropertyChanged {
    public bool IsValid { get; private set; }
    public string OrgId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public void Set(bool isValid, string orgId, string displayName) { ... }
}
```

Add `PlayerOrganizationState PlayerOrganization { get; }` to `VisualState`.

Also add `SelectedOrganizationState` (same shape + `double InitialGold`) for use in SelectOrg scene.
Add `SelectedOrganizationState SelectedOrganization { get; }` to `VisualState`.

Scoping note: `SelectOrgLogic` and `GameLogic` each create their own `VisualState` instance. `SelectedOrganization` is only populated in the SelectOrg scene (by `SelectOrgLogic`); `PlayerOrganization` is only populated in the Game scene (by `GameLogic`). No conflict.

### 6. SelectOrgLogic (replaces SelectCountryLogic)

Rename `src/Game.Main/SelectCountryLogic.cs` → `SelectOrgLogic.cs`. Class rename: `SelectOrgLogic`.

Changes:
- Constructor additionally takes `IConfigSource<OrganizationConfig>`.
- Builds `Dictionary<string, OrganizationEntry> _hqToOrg` from org config.
- Exposes `IReadOnlyList<string> HqCountryIds`.
- `Update()`: passes `ReadSelectCountryCommand()` to `SelectCountrySystem` as-is — no command filtering needed because `SelectOrgMapFilter` deactivates all non-HQ GOs, making them physically unclickable.
- `UpdateVisualState()`: when a country is selected, look up its org entry and call `VisualState.SelectedOrganization.Set(...)`.

### 7. SceneTransitionArgs + SceneLoader

**`Assets/Scripts/Unity/Common/SceneTransitionArgs.cs`**: Add `public static string OrganizationId;` field. Clear it in `Clear()`.

**`Assets/Scripts/Unity/Common/SceneLoader.cs`**: Add `organizationId` parameter to `LoadGame`:
```csharp
public void LoadGame(string saveName = null, string playerCountryId = null, string organizationId = null) {
    SceneTransitionArgs.Clear();
    SceneTransitionArgs.SaveNameToLoad = saveName;
    SceneTransitionArgs.InitialPlayerCountry = playerCountryId;
    SceneTransitionArgs.OrganizationId = organizationId;
    SceneManager.LoadScene("Map");
}
```
`SelectOrgDocument.OnStartGame` calls `_sceneLoader.LoadGame(playerCountryId: hqCountryId, organizationId: orgId)` — it does NOT pre-set `SceneTransitionArgs` fields directly, letting LoadGame do it after Clear.

### 8. SelectOrgDocument (replaces SelectCountryDocument)

Rename `Assets/Scripts/Unity/UI/SelectCountryDocument.cs` → `SelectOrgDocument.cs`.

Changes:
- Inject `SelectOrgLogic` (instead of `SelectCountryLogic`).
- Subscribe to `VisualState.SelectedOrganization.PropertyChanged`.
- `RefreshUI()`: show org `DisplayName` + initial gold (localize label: `"select_org.gold"`).
- `OnStartGame()`: call `_sceneLoader.LoadGame(playerCountryId: hqCountryId, organizationId: orgId)` — do NOT set `SceneTransitionArgs` directly; let `LoadGame` handle the clear-then-set order (see step 7).

Localization keys — in `Assets/Localization/en.asset` and `ru.asset`:
- Remove: `select_country.hint`, `select_country.start`, `select_country.back`
- Add: `select_org.hint`, `select_org.start`, `select_org.back`, `select_org.gold`
- Add: `organization_name.Illuminati` (display name for the first org)

### 9. SelectOrgMapFilter (new MonoBehaviour)

**`Assets/Scripts/Unity/Map/SelectOrgMapFilter.cs`** (new):
- Before writing this class, read `Assets/Scripts/Unity/Map/MapController.cs` and `MapRenderer.cs` to find the correct method/property for iterating feature GameObjects at runtime.
- Inject `SelectOrgLogic` and the Unity-side `CountryConfig` (ScriptableObject, already in scope).
- In `Start()` (after map loads via `MapController.ActiveRenderer`):
  1. Build a set of **HQ feature IDs**: for each `HqCountryId` in `SelectOrgLogic.HqCountryIds`, look it up in `CountryConfig.Entries` and collect all `mainMapFeatureIds` (and `secondaryMapFeatureIds`) into a `HashSet<string>`.
  2. Iterate all feature GOs via the method found in step above; call `go.SetActive(false)` for any GO whose `go.name` is not in the HQ feature ID set.
- Register in `SelectCountryLifetimeScope` via `RegisterComponentInHierarchy`.

### 10. SelectCountryLifetimeScope → wires SelectOrgLogic

Update **`Assets/Scripts/Unity/DI/SelectCountryLifetimeScope.cs`**:
- Add `[SerializeField] TextAsset _organizationsConfigAsset;`.
- Register `SelectOrgLogic` (passing both country + org config sources).
- Register `SelectOrgMapFilter` via `RegisterComponentInHierarchy`.
- Wire `IWriteOnlyCommandAccessor` through `SelectOrgLogic.Commands`.

### 11. GameLogicContext — add org fields

**`src/Game.Main/GameLogicContext.cs`**:
- Add `IConfigSource<OrganizationConfig> Organization { get; }`.
- Add `string InitialOrganizationId { get; }`.
- Extend constructor accordingly.

### 12. GameLogic — create org entity

**`src/Game.Main/GameLogic.cs`**:
- Load org config; find entry matching `context.InitialOrganizationId`.
- `_orgEntity = _world.Create(); _world.Add(_orgEntity, new Organization { OrganizationId = entry.OrganizationId, DisplayName = entry.DisplayName });`
- Create gold resource entity: `ResourceOwner { OwnerId = entry.OrganizationId }` + `Resource { ResourceId = "gold", Value = entry.InitialGold }`.
- Store `_orgEntity` field.
- Pass `_orgEntity` to `_visualStateConverter.Update(...)`.
- `CreateResourceEntities` still runs for every country — country resources remain as a separate concept, used by `CountryInfoView` when the player clicks on any country map. Do not remove them.

### 13. VisualStateConverter — org state + org resources

**`src/Game.Main/VisualStateConverter.cs`**:
- `Update(...)` signature gains `int orgEntity`.
- Add `UpdatePlayerOrganization(world, orgEntity)`:
  - Reads `Organization` component from `orgEntity`.
  - Calls `_state.PlayerOrganization.Set(true, org.OrganizationId, org.DisplayName)`.
  - No config dependency — `DisplayName` is stored on the component (see step 4).
- Change `UpdateResources`: replace `string playerCountryId = _state.PlayerCountry.IsValid ? _state.PlayerCountry.CountryId : "";` with `string playerOrgId = _state.PlayerOrganization.IsValid ? _state.PlayerOrganization.OrgId : "";`. Pass `playerOrgId` to `BuildResources` for `_state.PlayerResources`. Selected-country resources stay unchanged (still keyed by `_state.SelectedCountry.CountryId`).

### 14. HUD — PlayerOrgView (replaces PlayerCountryView)

Rename `Assets/Scripts/Unity/UI/PlayerCountryView.cs` → `PlayerOrgView.cs`.

Changes:
- `Refresh(PlayerOrganizationState orgState, CountryResourcesState resources)` (instead of PlayerCountryState).
- Show `orgState.DisplayName` in the name label (no localization lookup needed — DisplayName is already human-readable).

Update `HUDDocument`:
- Construct `PlayerOrgView` (new name, same element query `"player-country"`).
- Subscribe to `_state.PlayerOrganization.PropertyChanged` (instead of `PlayerCountry`).

### 15. SaveHeader — org name in save

**`src/Game.Main/WorldSnapshot.cs`**:
- Replace `string PlayerCountryId { get; set; }` with `string OrganizationId { get; set; }` in `SaveHeader`. Old saves are not supported.

**`src/Game.Main/SaveSystem.cs`**:
- In `BuildSnapshot`: query org entity for `Organization` component to get `orgId`.
- Remove the player-country query that currently sets `playerCountryId`; replace with org query.
- Set `Header.OrganizationId = orgId`.
- Save name: `$"{orgId}_{gameDate:yyyy-MM-dd}"`.

**`src/Game.Main/SaveFileManager.cs`** (if it reads `Header.PlayerCountryId`): update to use `Header.OrganizationId`.

### 16. GameLifetimeScope — wire org config + ID

**`Assets/Scripts/Unity/DI/GameLifetimeScope.cs`**:
- Add `[SerializeField] TextAsset _organizationsConfigAsset;`.
- Read `string initialOrgId = SceneTransitionArgs.OrganizationId;` — no fallback; if null the org entity creation will fail visibly, which is correct since normal flow always sets it via `LoadGame`.
- Pass org config source + `initialOrgId` to `GameLogicContext`.

## Tests

In `Game.Tests`:

- **OrganizationConfigTests**: loading JSON produces correct OrganizationEntry; `FindByHqCountry` and `FindById` return correct entries and null for unknowns.
- **SelectOrgLogicTests**: clicking a non-HQ country does not change `SelectedOrganization`; clicking an HQ country sets correct org name and gold.
- **GameLogicOrgTests**: after GameLogic construction, org entity exists with correct `Organization.OrganizationId`; gold resource entity has `ResourceOwner == orgId` and correct initial value.
- **SaveSystemOrgTests**: `BuildSnapshot` sets `Header.OrganizationId` to the org ID and uses it in `Header.SaveName`.

Use /implement to start working on the plan or request changes.
