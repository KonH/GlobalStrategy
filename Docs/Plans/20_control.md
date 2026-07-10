# 20 — Control Feature

## Goal

Organizations hold control stakes in countries. Control draws a share of that country's monthly gold income proportional to the stake. The only way to change control during gameplay is via debug buttons. Control is visible in both the country panel and the org income tooltip.

---

## Approach

**ECS model (mirrors ResourceEffect pattern):**
- `ControlEffect` component stores one control source per entity: `OrgId`, `CountryId`, `Value` (int), `EffectId`
- Multiple entities per org-country pair: one `"base_{orgId}"` entity (immutable, created at startup from config), one optional `"permanent_{orgId}_{countryId}"` entity (debug cheat)
- `ControlSystem` aggregates entities per org-country pair at month boundaries and directly transfers gold

**Income calculation:**
- Country base monthly income = sum of all Monthly positive `ResourceEffect` values for that country's gold
- Org gain = `(orgControl / 100.0) * countryBaseMonthlyGold`, rounded to two decimals
- Gold is mutated directly: `orgGoldResource += gain`, `countryGoldResource -= gain`
- No dynamic `ResourceEffect` entities for control gold — avoids circular dependency and keeps the resource effect list clean. Control income is carried in separate state (see Visual State below)

**Pool constraint:**
- Country control pool = 100 (constant)
- Debug delta = ±5; clamped so `orgTotal ≥ 0` and `sum-across-all-orgs ≤ 100`

**Icon:**
- Unicode `★` (U+2605) is outside ASCII; WebGL with LiberationSans will render it blank
- Use a plain text abbreviation label (`[Inf]`) in the interim; a texture icon can replace it later without layout changes

---

## Steps

### 1. ECS Components (`src/Game.Components/`)

1.1 **`ControlEffect.cs`**
```csharp
[Savable]
public struct ControlEffect {
    public string OrgId;
    public string CountryId;
    public int Value;
    public string EffectId;
}
```

1.2 **`ChangeControlCommand.cs`** (`src/Game.Commands/`)
```csharp
public struct ChangeControlCommand {
    public string OrgId;
    public string CountryId;
    public int Delta;
}
```

1.3 **Verify code generator pickup:** After adding `ChangeControlCommand.cs`, rebuild the solution and confirm that `ReadChangeControlCommand()` appears in the generated `CommandAccessor` partial. If the generator uses an explicit registration list rather than auto-discovery, add the new command there before proceeding.

---

### 2. Config (`src/Game.Configs/OrganizationConfig.cs`)

Add `BaseControl` to `OrganizationEntry` (default `10`):
```csharp
public int BaseControl { get; set; } = 10;
```

---

### 3. GameLogic initialisation (`src/Game.Main/GameLogic.cs`)

After creating the org entity, create the base control entity in the org's HQ country:
```csharp
int controlEntity = _world.Create();
_world.Add(controlEntity, new ControlEffect {
    OrgId   = orgEntry.OrganizationId,
    CountryId = orgEntry.HqCountryId,
    Value   = orgEntry.BaseControl,
    EffectId = $"base_{orgEntry.OrganizationId}"
});
```

---

### 4. ControlSystem (`src/Game.Systems/ControlSystem.cs`)

Static class; signature matches the existing system pattern:
```csharp
public static void Update(World world, DateTime previous, DateTime current)
```

Logic (only runs at a month boundary):
1. Collect all `ControlEffect` entities; group by `CountryId → (OrgId → totalValue)`
2. For each country with controlrs:
   a. Sum all Monthly positive `ResourceEffect` gold values for that country → `countryBaseIncome`
   b. For each org: `gain = (orgControl / 100.0) * countryBaseIncome`
   c. Find the country's gold `Resource` entity by `ResourceOwner(countryId)` + `Resource.ResourceId == "gold"` → deduct `gain`
   d. Find the org's gold `Resource` entity by `ResourceOwner(orgId)` → add `gain`

Call in `GameLogic.Update()` immediately after `ResourceSystem.Update()`:
```csharp
ControlSystem.Update(_world, _previousTime, currentTime);
```

---

### 5. ChangeControlCommand handling (`src/Game.Main/GameLogic.cs`)

In `Update()`, after control system, process commands:

```csharp
foreach (var cmd in _commandAccessor.ReadChangeControlCommand()) {
    ApplyChangeControl(cmd.OrgId, cmd.CountryId, cmd.Delta);
}
```

`ApplyChangeControl`:
1. Compute `otherOrgsTotal` = sum of all `ControlEffect.Value` for `countryId` where `OrgId != orgId`
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
public class OrgControlEntry {
    public string OrgId { get; }
    public string DisplayName { get; }
    public int Control { get; }
    public double EstimatedMonthlyGold { get; }
}

public class CountryControlState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public int UsedControl { get; private set; }
    public int PoolSize => 100;
    public IReadOnlyList<OrgControlEntry> OrgEntries { get; private set; }
    public void Set(int used, List<OrgControlEntry> entries) { ... }
}

public class ControlIncomeEntry {
    public string CountryId { get; }
    public double MonthlyGold { get; }
}
```

**Extend `CountryResourcesState`:**  
Add `IReadOnlyList<ControlIncomeEntry> ControlIncomes` (empty by default). Use an optional parameter with a null-coalescing default so existing callers don't break:
```csharp
public IReadOnlyList<ControlIncomeEntry> ControlIncomes { get; private set; } = Array.Empty<ControlIncomeEntry>();

public void Set(bool isValid, string countryId, List<ResourceStateEntry> resources,
                List<ControlIncomeEntry>? controlIncomes = null) {
    // ...
    ControlIncomes = controlIncomes ?? Array.Empty<ControlIncomeEntry>();
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
```
All existing `Set()` call sites remain valid without changes.

**Extend `VisualState`:**  
Add `CountryControlState SelectedControl { get; } = new CountryControlState();`

---

### 7. VisualStateConverter (`src/Game.Main/VisualStateConverter.cs`)

7.1 Add `UpdateSelectedControl(world)`:
- Query all `ControlEffect` entities for `selectedCountryId`; group by org
- Compute country base income (same logic as ControlSystem step 4-a)
- Build `OrgControlEntry` list sorted by `Control` descending (including estimated gold)
- Call `_state.SelectedControl.Set(...)`

7.2 Extend `BuildResources()` when building resources for the player org:
- Query all `ControlEffect` entities where `OrgId == orgId`; group by `CountryId`
- For each country, compute `countryBaseIncome` and `gain`
- Set `ControlIncomes` on the returned `CountryResourcesState`

7.3 Call `UpdateSelectedControl(world)` inside `Update()`.

---

### 8. Debug Buttons (Unity)

**`Assets/UI/HUD/HUD.uxml`:**  
Add inside `debug-panel`:
```xml
<ui:VisualElement name="control-debug-row" style="display: none;">
    <ui:Button name="btn-control-plus" text="Control+" />
    <ui:Button name="btn-control-minus" text="Control-" />
</ui:VisualElement>
```

**`Assets/Scripts/Unity/UI/HUDDocument.cs`:**
- Add field `VisualElement _controlDebugRow`
- In `Start()`: query `control-debug-row`, `btn-control-plus`, `btn-control-minus`; wire button clicks
- On each button click: push `ChangeControlCommand { OrgId = playerOrgId, CountryId = selectedCountryId, Delta = ±5 }`
- Add `RefreshControlDebugRow()` helper:
  ```csharp
  void RefreshControlDebugRow() {
      if (_controlDebugRow == null) return;
      _controlDebugRow.style.display =
          _state.SelectedCountry.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
  }
  ```
- Call `RefreshControlDebugRow()` from both `HandleCountryChanged()` and `OnEnable()` (for initial state)

---

### 9. Selected Country — Control UI (Unity)

9.1 **`Assets/UI/HUD/CountryInfo/CountryInfo.uxml`:** Add control row below gold row:
```xml
<ui:VisualElement name="control-row">
    <ui:Label name="control-icon" text="[Inf]" />
    <ui:Label name="control-label" />
</ui:VisualElement>
```

9.2 **`Assets/Scripts/Unity/UI/CountryInfoView.cs`:**
- Query `control-label`, register tooltip trigger on `control-row`
- In `Refresh()`, update label: `"Control: {used}/{pool}"` when `SelectedControl.IsValid`; hide otherwise
- Subscribe to `state.SelectedControl.PropertyChanged` in `HUDDocument.OnEnable/OnDisable`

9.3 **Control tooltip** (built in `CountryInfoView`):
- Header: `"Country Control"`
- One row per `OrgControlEntry` (sorted descending by control): `"{DisplayName}: {Control}"`
- Each org row is an inner trigger; hover shows org control details:

**Org control inner tooltip:**
```
Control: {total}
  Base: +{base}
  Permanent effect: +{permanent}   (omit if 0)
Leads to:
  Income +{estimatedGold:F0}/month
```

9.4 HUDDocument subscribes to `_state.SelectedControl.PropertyChanged` alongside other state events; calls `RefreshCountryViews()`.

---

### 10. Org Income Tooltip (Unity)

**`Assets/Scripts/Unity/UI/ResourcesView.cs`** — extend `BuildResourceTooltip()` for the gold resource:

After rendering standard monthly effect rows, if `state.ControlIncomes` is non-empty:
1. Sum all entries → `controlTotal`
2. Add a summary outer row `"+{controlTotal:F1}/month"` with classes `tooltip-effect-name`, `tooltip-effect-positive`, `tooltip-inner-trigger`
3. Register an inner trigger on that row that opens a per-country breakdown:
   - Header matching the summary text
   - One row per `ControlIncomeEntry`: `"Control ({CountryDisplayName}): +{MonthlyGold:F1}/month"` (green)
   - Country display name resolved as `_loc.Get($"country_name.{entry.CountryId}")`

This follows the same `RegisterInnerTrigger` pattern as the existing `BuildMonthlyEffectList` calls.

`ResourcesView.Refresh(CountryResourcesState state)` already receives the state object; `state.ControlIncomes` is accessed there directly.

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
- Expose a method `double ComputeBaseControlIncome(string orgId)`:
  - Find org entry; compute `hqCountryBaseIncome` = sum of Monthly positive gold `DefaultEffects` from `_resourceConfig`
  - Return `(orgEntry.BaseControl / 100.0) * hqCountryBaseIncome`

11.2 **`SelectOrgDocument.cs`** — extend `RefreshUI()`:
- When org is valid, display base control: `"{loc[select_org.base_control]} {baseControl}/100"`
- Display: `"{loc[select_org.estimated_income]} +{income:F0}/month"` using `_logic.ComputeBaseControlIncome(orgId)`

11.3 Add two labels to `Assets/UI/SelectOrg/SelectOrg.uxml`: `control-label`, `estimated-income-label`.

---

### 12. Localization

Add to `Assets/Localization/en.asset` and `ru.asset`:

| Key | EN | RU |
|---|---|---|
| `hud.country_control` | `Control` | `Влияние` |
| `hud.control_tooltip_title` | `Country Control` | `Влияние в стране` |
| `hud.control_tooltip_base` | `Base:` | `База:` |
| `hud.control_tooltip_permanent` | `Permanent effect:` | `Постоянный эффект:` |
| `hud.control_tooltip_leads_to` | `Leads to:` | `Приводит к:` |
| `hud.control_tooltip_income` | `Income` | `Доход` |
| `select_org.base_control` | `Base Control:` | `Базовое влияние:` |
| `select_org.estimated_income` | `Estimated Income:` | `Ожидаемый доход:` |

---

## Tests

New file: `src/Game.Tests/ControlSystemTests.cs`

- **Base entity created:** After `GameLogic` init, one `ControlEffect` entity exists for the org's HQ country with `Value == 10`
- **Gold transfer at month boundary:** Org with 20% control in a country earning 1000/month receives 200; country gold is reduced by 200
- **Zero control:** Org with 0% control receives nothing; country income unchanged
- **Multiple orgs:** Two orgs with 20% and 30% control both receive correct proportional amounts; country deducted sum
- **ChangeControlCommand — add:** Permanent entity created/updated; value reflects delta
- **ChangeControlCommand — pool cap:** Cannot exceed `100 - otherOrgsControl`; clamped silently
- **ChangeControlCommand — floor:** Cannot go below 0; clamped silently
- **Base effect immutable:** `ChangeControlCommand` creates a separate permanent entity; base entity untouched

---

Use /implement to start working on the plan or request changes.
