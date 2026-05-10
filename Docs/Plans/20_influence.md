# 20 — Influence Feature

## Goal

Organizations hold influence stakes in countries. Influence draws a share of that country's monthly gold income proportional to the stake. The only way to change influence during gameplay is via debug buttons. Influence is visible in both the country panel and the org income tooltip.

---

## Approach

**ECS model (mirrors ResourceEffect pattern):**
- `InfluenceEffect` component stores one influence source per entity: `OrgId`, `CountryId`, `Value` (int), `EffectId`
- Multiple entities per org-country pair: one `"base_{orgId}"` entity (immutable, created at startup from config), one optional `"permanent_{orgId}_{countryId}"` entity (debug cheat)
- `InfluenceSystem` aggregates entities per org-country pair at month boundaries and directly transfers gold

**Income calculation:**
- Country base monthly income = sum of all Monthly positive `ResourceEffect` values for that country's gold
- Org gain = `(orgInfluence / 100.0) * countryBaseMonthlyGold`, rounded to two decimals
- Gold is mutated directly: `orgGoldResource += gain`, `countryGoldResource -= gain`
- No dynamic `ResourceEffect` entities for influence gold — avoids circular dependency and keeps the resource effect list clean. Influence income is carried in separate state (see Visual State below)

**Pool constraint:**
- Country influence pool = 100 (constant)
- Debug delta = ±5; clamped so `orgTotal ≥ 0` and `sum-across-all-orgs ≤ 100`

**Icon:**
- Unicode `★` (U+2605) is outside ASCII; WebGL with LiberationSans will render it blank
- Use a plain text abbreviation label (`[Inf]`) in the interim; a texture icon can replace it later without layout changes

---

## Steps

### 1. ECS Components (`src/Game.Components/`)

1.1 **`InfluenceEffect.cs`**
```csharp
[Savable]
public struct InfluenceEffect {
    public string OrgId;
    public string CountryId;
    public int Value;
    public string EffectId;
}
```

1.2 **`ChangeInfluenceCommand.cs`** (`src/Game.Commands/`)
```csharp
public struct ChangeInfluenceCommand {
    public string OrgId;
    public string CountryId;
    public int Delta;
}
```

1.3 **Verify code generator pickup:** After adding `ChangeInfluenceCommand.cs`, rebuild the solution and confirm that `ReadChangeInfluenceCommand()` appears in the generated `CommandAccessor` partial. If the generator uses an explicit registration list rather than auto-discovery, add the new command there before proceeding.

---

### 2. Config (`src/Game.Configs/OrganizationConfig.cs`)

Add `BaseInfluence` to `OrganizationEntry` (default `10`):
```csharp
public int BaseInfluence { get; set; } = 10;
```

---

### 3. GameLogic initialisation (`src/Game.Main/GameLogic.cs`)

After creating the org entity, create the base influence entity in the org's HQ country:
```csharp
int influenceEntity = _world.Create();
_world.Add(influenceEntity, new InfluenceEffect {
    OrgId   = orgEntry.OrganizationId,
    CountryId = orgEntry.HqCountryId,
    Value   = orgEntry.BaseInfluence,
    EffectId = $"base_{orgEntry.OrganizationId}"
});
```

---

### 4. InfluenceSystem (`src/Game.Systems/InfluenceSystem.cs`)

Static class; signature matches the existing system pattern:
```csharp
public static void Update(World world, DateTime previous, DateTime current)
```

Logic (only runs at a month boundary):
1. Collect all `InfluenceEffect` entities; group by `CountryId → (OrgId → totalValue)`
2. For each country with influencers:
   a. Sum all Monthly positive `ResourceEffect` gold values for that country → `countryBaseIncome`
   b. For each org: `gain = (orgInfluence / 100.0) * countryBaseIncome`
   c. Find the country's gold `Resource` entity by `ResourceOwner(countryId)` + `Resource.ResourceId == "gold"` → deduct `gain`
   d. Find the org's gold `Resource` entity by `ResourceOwner(orgId)` → add `gain`

Call in `GameLogic.Update()` immediately after `ResourceSystem.Update()`:
```csharp
InfluenceSystem.Update(_world, _previousTime, currentTime);
```

---

### 5. ChangeInfluenceCommand handling (`src/Game.Main/GameLogic.cs`)

In `Update()`, after influence system, process commands:

```csharp
foreach (var cmd in _commandAccessor.ReadChangeInfluenceCommand()) {
    ApplyChangeInfluence(cmd.OrgId, cmd.CountryId, cmd.Delta);
}
```

`ApplyChangeInfluence`:
1. Compute `otherOrgsTotal` = sum of all `InfluenceEffect.Value` for `countryId` where `OrgId != orgId`
2. Find entity with `EffectId = $"permanent_{orgId}_{countryId}"` (may not exist)
3. `existing` = entity's current `Value`, or 0 if no entity
4. `newVal = Clamp(existing + delta, 0, 100 - otherOrgsTotal)`
5. Dispatch:
   - Entity exists, `newVal == 0` → destroy entity
   - Entity exists, `newVal > 0` → update `Value`
   - No entity, `newVal > 0` → create new entity with that `Value`
   - No entity, `newVal == 0` → no-op

---

### 6. Visual State (`src/Game.Main/VisualState.cs` + `ResourcesState.cs`)

**New types:**

```csharp
public class OrgInfluenceEntry {
    public string OrgId { get; }
    public string DisplayName { get; }
    public int Influence { get; }
    public double EstimatedMonthlyGold { get; }
}

public class CountryInfluenceState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public int UsedInfluence { get; private set; }
    public int PoolSize => 100;
    public IReadOnlyList<OrgInfluenceEntry> OrgEntries { get; private set; }
    public void Set(int used, List<OrgInfluenceEntry> entries) { ... }
}

public class InfluenceIncomeEntry {
    public string CountryId { get; }
    public double MonthlyGold { get; }
}
```

**Extend `CountryResourcesState`:**  
Add `IReadOnlyList<InfluenceIncomeEntry> InfluenceIncomes` (empty by default). Use an optional parameter with a null-coalescing default so existing callers don't break:
```csharp
public IReadOnlyList<InfluenceIncomeEntry> InfluenceIncomes { get; private set; } = Array.Empty<InfluenceIncomeEntry>();

public void Set(bool isValid, string countryId, List<ResourceStateEntry> resources,
                List<InfluenceIncomeEntry>? influenceIncomes = null) {
    // ...
    InfluenceIncomes = influenceIncomes ?? Array.Empty<InfluenceIncomeEntry>();
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
```
All existing `Set()` call sites remain valid without changes.

**Extend `VisualState`:**  
Add `CountryInfluenceState SelectedInfluence { get; } = new CountryInfluenceState();`

---

### 7. VisualStateConverter (`src/Game.Main/VisualStateConverter.cs`)

7.1 Add `UpdateSelectedInfluence(world)`:
- Query all `InfluenceEffect` entities for `selectedCountryId`; group by org
- Compute country base income (same logic as InfluenceSystem step 4-a)
- Build `OrgInfluenceEntry` list sorted by `Influence` descending (including estimated gold)
- Call `_state.SelectedInfluence.Set(...)`

7.2 Extend `BuildResources()` when building resources for the player org:
- Query all `InfluenceEffect` entities where `OrgId == orgId`; group by `CountryId`
- For each country, compute `countryBaseIncome` and `gain`
- Set `InfluenceIncomes` on the returned `CountryResourcesState`

7.3 Call `UpdateSelectedInfluence(world)` inside `Update()`.

---

### 8. Debug Buttons (Unity)

**`Assets/UI/HUD/HUD.uxml`:**  
Add inside `debug-panel`:
```xml
<ui:VisualElement name="influence-debug-row" style="display: none;">
    <ui:Button name="btn-influence-plus" text="Influence+" />
    <ui:Button name="btn-influence-minus" text="Influence-" />
</ui:VisualElement>
```

**`Assets/Scripts/Unity/UI/HUDDocument.cs`:**
- Add field `VisualElement _influenceDebugRow`
- In `Start()`: query `influence-debug-row`, `btn-influence-plus`, `btn-influence-minus`; wire button clicks
- On each button click: push `ChangeInfluenceCommand { OrgId = playerOrgId, CountryId = selectedCountryId, Delta = ±5 }`
- Add `RefreshInfluenceDebugRow()` helper:
  ```csharp
  void RefreshInfluenceDebugRow() {
      if (_influenceDebugRow == null) return;
      _influenceDebugRow.style.display =
          _state.SelectedCountry.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
  }
  ```
- Call `RefreshInfluenceDebugRow()` from both `HandleCountryChanged()` and `OnEnable()` (for initial state)

---

### 9. Selected Country — Influence UI (Unity)

9.1 **`Assets/UI/HUD/CountryInfo/CountryInfo.uxml`:** Add influence row below gold row:
```xml
<ui:VisualElement name="influence-row">
    <ui:Label name="influence-icon" text="[Inf]" />
    <ui:Label name="influence-label" />
</ui:VisualElement>
```

9.2 **`Assets/Scripts/Unity/UI/CountryInfoView.cs`:**
- Query `influence-label`, register tooltip trigger on `influence-row`
- In `Refresh()`, update label: `"Influence: {used}/{pool}"` when `SelectedInfluence.IsValid`; hide otherwise
- Subscribe to `state.SelectedInfluence.PropertyChanged` in `HUDDocument.OnEnable/OnDisable`

9.3 **Influence tooltip** (built in `CountryInfoView`):
- Header: `"Country Influence"`
- One row per `OrgInfluenceEntry` (sorted descending by influence): `"{DisplayName}: {Influence}"`
- Each org row is an inner trigger; hover shows org influence details:

**Org influence inner tooltip:**
```
Influence: {total}
  Base: +{base}
  Permanent effect: +{permanent}   (omit if 0)
Leads to:
  Income +{estimatedGold:F0}/month
```

9.4 HUDDocument subscribes to `_state.SelectedInfluence.PropertyChanged` alongside other state events; calls `RefreshCountryViews()`.

---

### 10. Org Income Tooltip (Unity)

**`Assets/Scripts/Unity/UI/ResourcesView.cs`** — extend `BuildResourceTooltip()` for the gold resource:

After rendering standard monthly effect rows, if `state.InfluenceIncomes` is non-empty:
1. Sum all entries → `influenceTotal`
2. Add a summary outer row `"+{influenceTotal:F1}/month"` with classes `tooltip-effect-name`, `tooltip-effect-positive`, `tooltip-inner-trigger`
3. Register an inner trigger on that row that opens a per-country breakdown:
   - Header matching the summary text
   - One row per `InfluenceIncomeEntry`: `"Influence ({CountryDisplayName}): +{MonthlyGold:F1}/month"` (green)
   - Country display name resolved as `_loc.Get($"country_name.{entry.CountryId}")`

This follows the same `RegisterInnerTrigger` pattern as the existing `BuildMonthlyEffectList` calls.

`ResourcesView.Refresh(CountryResourcesState state)` already receives the state object; `state.InfluenceIncomes` is accessed there directly.

---

### 11. Select Org View (Unity)

11.0 **`SelectOrgLifetimeScope.cs`** (or whichever `LifetimeScope` registers `SelectOrgLogic`):
- Add `[SerializeField] TextAsset _resourceConfigAsset` field; wire it in the Inspector
- Load and pass to the `SelectOrgLogic` constructor:
  ```csharp
  var resourceConfig = new TextAssetConfig<ResourceConfig>(_resourceConfigAsset).Load();
  builder.Register(_ => new SelectOrgLogic(countryConfig, orgConfig, resourceConfig), Lifetime.Singleton);
  ```

11.1 **`src/Game.Main/SelectOrgLogic.cs`**:
- Add constructor parameter `ResourceConfig resourceConfig`; store as `_resourceConfig`
- Expose a method `double ComputeBaseInfluenceIncome(string orgId)`:
  - Find org entry; compute `hqCountryBaseIncome` = sum of Monthly positive gold `DefaultEffects` from `_resourceConfig`
  - Return `(orgEntry.BaseInfluence / 100.0) * hqCountryBaseIncome`

11.2 **`SelectOrgDocument.cs`** — extend `RefreshUI()`:
- When org is valid, display base influence: `"{loc[select_org.base_influence]} {baseInfluence}/100"`
- Display: `"{loc[select_org.estimated_income]} +{income:F0}/month"` using `_logic.ComputeBaseInfluenceIncome(orgId)`

11.3 Add two labels to `Assets/UI/SelectOrg/SelectOrg.uxml`: `influence-label`, `estimated-income-label`.

---

### 12. Localization

Add to `Assets/Localization/en.asset` and `ru.asset`:

| Key | EN | RU |
|---|---|---|
| `hud.country_influence` | `Influence` | `Влияние` |
| `hud.influence_tooltip_title` | `Country Influence` | `Влияние в стране` |
| `hud.influence_tooltip_base` | `Base:` | `База:` |
| `hud.influence_tooltip_permanent` | `Permanent effect:` | `Постоянный эффект:` |
| `hud.influence_tooltip_leads_to` | `Leads to:` | `Приводит к:` |
| `hud.influence_tooltip_income` | `Income` | `Доход` |
| `select_org.base_influence` | `Base Influence:` | `Базовое влияние:` |
| `select_org.estimated_income` | `Estimated Income:` | `Ожидаемый доход:` |

---

## Tests

New file: `src/Game.Tests/InfluenceSystemTests.cs`

- **Base entity created:** After `GameLogic` init, one `InfluenceEffect` entity exists for the org's HQ country with `Value == 10`
- **Gold transfer at month boundary:** Org with 20% influence in a country earning 1000/month receives 200; country gold is reduced by 200
- **Zero influence:** Org with 0% influence receives nothing; country income unchanged
- **Multiple orgs:** Two orgs with 20% and 30% influence both receive correct proportional amounts; country deducted sum
- **ChangeInfluenceCommand — add:** Permanent entity created/updated; value reflects delta
- **ChangeInfluenceCommand — pool cap:** Cannot exceed `100 - otherOrgsInfluence`; clamped silently
- **ChangeInfluenceCommand — floor:** Cannot go below 0; clamped silently
- **Base effect immutable:** `ChangeInfluenceCommand` creates a separate permanent entity; base entity untouched

---

Use /implement to start working on the plan or request changes.
