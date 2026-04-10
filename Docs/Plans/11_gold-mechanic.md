# 11 — Gold Mechanic (Generic Resource System)

## Goal

Add a generic `double`-based resource system using ECS components. Gold is the first resource — no `Gold`-specific type exists. Config defines resources and their effect text keys. Countries own resource entities; resource entities own effect entities. Effects have a `PayType` (Instant or Monthly). Both the player panel and the selected-country panel show a gold counter with tooltip.

## Architecture

### ECS entity structure

```
Country entity      [Country { CountryId }]
Resource entity     [ResourceOwner { CountryId }, Resource { ResourceId, double Value }]
Effect entity       [ResourceOwner { CountryId }, ResourceLink { ResourceId }, ResourceEffect { EffectId, double Value, PayType }]
```

- One resource entity per (country × resource)
- One effect entity per (country × resource × active effect)
- Instant effects are removed after application; Monthly effects persist

### Config structure (`Game.Configs/ResourceConfig.cs`)

```
ResourceConfig
  List<ResourceDefinition> Resources
    string ResourceId         // e.g. "gold"
    string NameKey            // e.g. "resource.gold.name"
    string DescriptionKey     // e.g. "resource.gold.description"
    double DefaultInitialValue // used if CountryEntry has no override
    List<EffectDefinition> DefaultEffects  // applied to every country on init
      string EffectId         // e.g. "base_income"
      string NameKey          // e.g. "effect.base_income.name"
      string DescriptionKey   // e.g. "effect.base_income.description"
      double Value            // e.g. 1.0
      PayType PayType         // Monthly
```

`CountryEntry` gains:
```
List<CountryResourceInit> InitialResources   // optional per-country overrides
  string ResourceId
  double Value
```

Initial gold values by design intent (set in JSON): major powers 150, medium 100, minor 50. Falls back to `DefaultInitialValue = 100` if not set.

### PayType enum (`Game.Components/PayType.cs`)

```csharp
public enum PayType { Instant, Monthly }
```

## Steps

### 1 — New ECS components (`src/Game.Components/`)

- `ResourceOwner.cs` — `record struct ResourceOwner(string CountryId)`
- `Resource.cs` — `struct Resource { string ResourceId; double Value; }`
- `ResourceLink.cs` — `record struct ResourceLink(string ResourceId)`
- `ResourceEffect.cs` — `struct ResourceEffect { string EffectId; double Value; PayType PayType; }`
- `PayType.cs` — `enum PayType { Instant, Monthly }`

### 2 — ResourceConfig (`src/Game.Configs/ResourceConfig.cs`)

New config class with the structure described above. Add a `ResourceConfig.json` data file (populated with gold resource + base_income effect).

### 3 — GameLogicContext & GameLogic init

- `GameLogicContext` gains `IConfigSource<ResourceConfig> Resource`
- In `GameLogic` constructor, after creating country entities:
  - Load `ResourceConfig`
  - For each country × each `ResourceDefinition`:
    - Determine initial value (CountryEntry override or `DefaultInitialValue`)
    - Create resource entity: `ResourceOwner(countryId)` + `Resource { ResourceId, Value }`
    - For each `DefaultEffect` on the resource:
      - Create effect entity: `ResourceOwner(countryId)` + `ResourceLink(resourceId)` + `ResourceEffect { EffectId, Value, PayType }`

### 4 — ResourceSystem (`src/Game.Systems/ResourceSystem.cs`)

```
ResourceSystem.Update(world, previousTime, currentTime)
```

**Instant effects** (every frame):
- Query all effect entities with `PayType.Instant`
- For each: find matching resource entity (same CountryId + ResourceId), add value to `Resource.Value`
- Collect effect entities to remove; remove after iteration

**Monthly effects** (at month boundary: `previousTime.Month != currentTime.Month`):
- Query all effect entities with `PayType.Monthly`
- For each: find matching resource entity, add value to `Resource.Value`

`GameLogic.Update` tracks `_previousTime`, passes it to `ResourceSystem` before updating `_gameTimeEntity`.

### 5 — Visual state (`src/Game.Main/`)

**New types (`ResourcesState.cs`):**
```
EffectStateEntry    { string EffectId; double Value; PayType PayType }
ResourceStateEntry  { string ResourceId; double Value; IReadOnlyList<EffectStateEntry> Effects }
CountryResourcesState : INotifyPropertyChanged
  string CountryId
  bool IsValid
  IReadOnlyList<ResourceStateEntry> Resources
  void Set(bool isValid, string countryId, List<ResourceStateEntry> resources)
```

**`VisualState`** — add:
```csharp
public CountryResourcesState PlayerResources { get; } = new CountryResourcesState();
public CountryResourcesState SelectedResources { get; } = new CountryResourcesState();
```

**`VisualStateConverter`** — add `UpdateResources(world)`:
- For player country: query resource entities where `ResourceOwner.CountryId == playerCountryId`; for each, query matching effects
- For selected country: same for selected countryId
- Call `_state.PlayerResources.Set(...)` and `_state.SelectedResources.Set(...)`

### 6 — Reusable tooltip system (Unity, `Assets/Scripts/Unity/UI/`)

**`TooltipController.cs`** — MonoBehaviour, takes the tooltip overlay `VisualElement`:
```
RegisterTooltip(VisualElement trigger, Func<VisualElement> buildContent)
```
- `MouseEnterEvent` on trigger → build content, set panel position near pointer, `display: Flex`
- `MouseLeaveEvent` → `display: None`
- Single shared `VisualElement` panel, content replaced each time

### 7 — ResourcesView (Unity, `Assets/Scripts/Unity/UI/ResourcesView.cs`)

Plain C# view class, not MonoBehaviour:
```
ResourcesView(VisualElement root, ILocalization loc, ResourceConfig config, TooltipController tooltip)
Refresh(CountryResourcesState state)
```

- Displays one label per resource (`💰 {value:F0}` for gold until an icon asset exists)
- Net monthly = sum of effects with `PayType.Monthly`
- Net positive → USS class `resource-positive` (green), negative → `resource-negative` (red), zero → default
- On hover over resource label: tooltip lists each effect
  - Row: localized effect name (`config.FindEffect(effectId).NameKey`) + value formatted as `+1.0/month`
  - On hover over an effect row: nested tooltip showing localized description key
- `ResourceConfig` passed in for key lookup; `ILocalization` resolves text

### 8 — Wire into existing views

**`PlayerCountryView`** — gains a `ResourcesView` instance; `Refresh` also calls `resourcesView.Refresh(...)`

**`CountryInfoView`** — same, uses `SelectedResources` state

**`HUDDocument`**:
- Instantiate `TooltipController` with `root.Q("tooltip-overlay")`
- Pass it into `PlayerCountryView` and `CountryInfoView` constructors (or new `ResourcesView` instance)
- Subscribe `_state.PlayerResources.PropertyChanged` and `_state.SelectedResources.PropertyChanged`

### 9 — UXML & USS changes

**`PlayerCountry.uxml`** — add `<ui:VisualElement name="resources-container" />`

**`CountryInfo.uxml`** — add `<ui:VisualElement name="resources-container" />`

**`HUD.uxml`** — add tooltip overlay as last child of `hud-root`:
```xml
<ui:VisualElement name="tooltip-overlay" class="tooltip-overlay" />
```

**`HUD.uss`** — `.tooltip-overlay { position: absolute; background-color: rgba(0,0,0,0.85); padding: 6px; display: none; }`

**`PlayerCountry.uss` / `CountryInfo.uss`** — add `.resource-positive { color: #4CAF50; }` `.resource-negative { color: #F44336; }`

### 10 — Localization keys

Add to locale JSON files (en/ru):
- `resource.gold.name`, `resource.gold.description`
- `effect.base_income.name`, `effect.base_income.description`

### 11 — Build & verify

- `dotnet build src/GlobalStrategy.Core.sln -c Release`
- Refresh Unity, check console errors
- Run in Editor: gold counter appears in both panels, increments monthly, tooltip shows on hover with effect list and description
