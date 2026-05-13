# UI Toolkit Architecture

The project uses Unity UI Toolkit exclusively. Do not add Canvas, UGUI, or uGUI components.

## Layer Model

One `UIDocument` per z-layer — not one per panel. Each layer has its own `PanelSettings` asset:

```
Assets/UI/HUD/          → HUDPanelSettings.asset  (sortOrder: 0)  — in-game HUD
Assets/UI/Overlay/      → OverlayPanelSettings.asset (sortOrder: 1) — contextual panels
Assets/UI/Modal/        → ModalPanelSettings.asset   (sortOrder: 2) — menus, popups
```

Use `manage_ui create_panel_settings` to create PanelSettings assets.

## UXML Composition

Each layer's root UXML composes sub-panels via templates:

```xml
<ui:Template src="project://database/Assets/UI/HUD/CountryInfo/CountryInfo.uxml" name="CountryInfo" />
<ui:VisualElement name="hud-root">
    <ui:Instance template="CountryInfo" name="country-info" class="country-info-panel" />
</ui:VisualElement>
```

- Layout/position styles (bottom bar, absolute positioning) go in the **layer USS** targeting the instance class
- Typography and internal element styles go in the **template's own USS**
- Always use `<ui:Style>` with the `ui:` prefix — bare `<Style>` breaks UI Builder

## State Layer

- `VisualState` (from `GS.Main`) is a pure C# object injected via VContainer into binding MonoBehaviours
- State objects implement `INotifyPropertyChanged` — use `PropertyChanged` event (not custom `Action<T>`)
- Binding subscribes in `OnEnable`, unsubscribes in `OnDisable`, calls `Refresh` immediately on enable for sync

## Binding Structure

Split into two parts per layer:

**Binding MonoBehaviour** (e.g. `HUDDocument`) — one per UIDocument:
- Gets `UIDocument` in `Awake`, queries named root elements, instantiates view objects
- Injects `VisualState` via `[Inject] void Construct(VisualState state)`
- Subscribes to state events and calls `view.Refresh(state.SubState)`

**View class** (e.g. `CountryInfoView`) — plain C#, not a MonoBehaviour:
- Constructor receives the root `VisualElement` (the template container), queries child elements
- Exposes a single `Refresh(SubState state)` method — no knowledge of events or injection
- Controls visibility via `_root.style.display`

```csharp
class CountryInfoView {
    readonly VisualElement _root;
    readonly Label _name;

    public CountryInfoView(VisualElement root) {
        _root = root;
        _name = root.Q<Label>("country-name");
    }

    public void Refresh(SelectedCountryState state) {
        _root.style.display = state.IsValid ? DisplayStyle.Flex : DisplayStyle.None;
        _name.text = state.CountryId ?? string.Empty;
    }
}
```

## Tabs and Windows

Active tab / open window state lives in `VisualState`, driven by commands — not in the UI script:

```csharp
public class SidebarState : INotifyPropertyChanged {
    public SidebarTab ActiveTab { get; private set; }
    public void SetTab(SidebarTab tab) { ActiveTab = tab; PropertyChanged?.Invoke(...); }
}
```

Binding toggles `display: none/flex` on tab root elements in response to state changes.

## Folder Structure

```
Assets/UI/HUD/
  HUDPanelSettings.asset
  HUD.uxml              ← root UXML, composes templates
  HUD.uss               ← layer-level layout (position, size of panels)
  CountryInfo/
    CountryInfo.uxml    ← template
    CountryInfo.uss     ← internal element styles only

Assets/Scripts/Unity/UI/
  GS.Unity.UI.asmdef   ← references VContainer
  HUDDocument.cs        ← binding MonoBehaviour
  CountryInfoView.cs    ← plain view class
```

## Shared UI Kit

All shared visual styles live in `Assets/UI/Shared/SharedStyles.uss`. Every UXML file must reference it before the local USS:

```xml
<ui:Style src="project://database/Assets/UI/Shared/SharedStyles.uss"/>
<ui:Style src="project://database/Assets/UI/HUD/HUD.uss"/>
```

### Class catalogue

**Colour utilities** (single-property, use for one-off overrides):
- `.gs-bg-panel` / `.gs-bg-button` / `.gs-bg-button-hover` / `.gs-bg-button-active` / `.gs-bg-tooltip`
- `.gs-border-primary` / `.gs-border-muted`
- `.gs-color-dark` / `.gs-color-mid` / `.gs-color-hint` / `.gs-color-positive` / `.gs-color-negative` / `.gs-color-light`

**Panel:** `.gs-panel` — beige bg, brown border (2 px), 6 px radius, column flex, centered items

**Overlays:** `.gs-modal-root` — absolute full-screen center-center flex with semi-transparent bg; `.gs-blackfade` — absolute full-screen 40 % dark overlay

**Text:**
- `.gs-title` — 42 px, dark brown, bold, centered (large modal title)
- `.gs-header` — 36 px, dark brown, bold (section header)
- `.gs-label` — 20 px, medium brown, normal (form label)
- `.gs-content` — 18 px, medium brown (body text)
- `.gs-hint` — 16 px, lighter brown, italic (hints)

**Buttons:** `.gs-btn` — tan bg, brown border, dark bold text, 4 px radius + `:hover` state
- `.gs-btn--primary` — 60 px tall, 30 px font, bottom margin (full menu button)
- `.gs-btn--secondary` — lighter tan tone
- `.gs-btn--small` — 20 px font (row actions, time controls)
- `.gs-btn--destructive` — red-tinted bg (delete actions)
- `.gs-btn--active` — dark brown bg, light text (active speed button)

**Toggles:** `.gs-toggle-on` / `.gs-toggle-off` — darker/standard states for exclusive-option buttons

### Usage rules

- New components must use shared classes for all visual styling (colour, typography, border).
- Per-feature USS contains **only** layout overrides: position, width, height, margin, padding — never colour or font repetition.
- Dynamic elements created in C# must call `AddToClassList("gs-btn")` etc. the same way UXML does.
- To add a new shared style: add it to `SharedStyles.uss`, document it in this section, then use it in UXML/C#.

## Blocking map/world clicks through UI panels

Any MonoBehaviour that reads raw mouse input (e.g. `Mouse.current.leftButton`) must guard against clicks landing on UI panels:

```csharp
using UnityEngine.EventSystems;

void Update() {
    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
    // ... process world click
}
```

UI Toolkit registers with the EventSystem in Unity 6, so `IsPointerOverGameObject()` returns `true` whenever the pointer is over any panel element with default `PickingMode.Position`. Empty transparent areas of the HUD root do not block clicks.

## USS scope for dynamically created elements

USS classes on VisualElements created in C# are resolved against the USS files loaded in the **document** where the element is actually placed — not where the C# code that builds it lives.

Example: tooltip content is built in `ResourcesView` (which lives inside the `PlayerCountry` template), but the tooltip overlay `VisualElement` is a child of `hud-root` in `HUD.uxml`. Therefore tooltip classes (`tooltip-header`, `tooltip-effect-positive`, etc.) must be defined in `HUD.uss`, not in `PlayerCountry.uss`.

Rule: put a USS class in the stylesheet of the **document that owns the container element**, regardless of which C# class creates the child elements.

## USS / C# limitations in Unity 6000.4.1f1

**`border-style: dashed` is not supported.** Neither the USS shorthand (`border-style: dashed`), per-side USS properties (`border-top-style: dashed`), nor the C# `IStyle` API (no `borderTopStyle` property, no `BorderStyle` enum) are implemented. There is no way to achieve dashed borders in UI Toolkit on this version.

**Use `gap` not `margin-left` for button row spacing.** `margin-left: Xpx` on all children of a flex row shifts the *first* child too, offsetting the entire row from the container edge. Use `gap: Xpx` on the container — it only inserts space *between* items.

**Use `opacity: 0` to hide while keeping layout space.** `DisplayStyle.None` removes the element from layout flow, causing siblings to reflow. `Visibility.Hidden` is supposed to preserve space but can be unreliable. `style.opacity = 0` is the most reliable way to make an element invisible while preserving its layout footprint.

## Tooltip Positioning

`worldBound` on a newly added `VisualElement` is zero — the panel has no layout yet. Never read `panel.worldBound.height` immediately after adding it to compute final position.

Pattern: set an initial position, then register a `GeometryChangedEvent` callback to adjust after layout:

```csharp
void PositionNear(VisualElement panel, VisualElement trigger) {
    panel.style.left = trigger.worldBound.xMin;
    panel.style.top  = trigger.worldBound.yMax + 4;
    panel.RegisterCallback<GeometryChangedEvent>(_ => AdjustPosition(panel, trigger));
}

void AdjustPosition(VisualElement panel, VisualElement trigger) {
    var screen = _hudRoot.worldBound;
    var t = trigger.worldBound;
    var p = panel.worldBound;

    float top  = t.yMax + 4;
    if (top + p.height > screen.yMax) top = t.yMin - p.height - 4;
    top = Mathf.Max(top, screen.yMin);

    float left = t.xMin;
    if (left + p.width > screen.xMax) left = screen.xMax - p.width;
    left = Mathf.Max(left, screen.xMin);

    panel.style.left = left;
    panel.style.top  = top;
}
```

`GeometryChangedEvent` fires every time the element is re-laid-out — the callback is idempotent here (it just clamps), so re-firing is harmless.

## 2-Column Chip Grid

`flex-basis: 50%` with `flex-wrap: wrap` and `justify-content: flex-start` on a flex container creates a reliable 2-per-row grid in UI Toolkit:

```css
.chips-container {
    flex-direction: row;
    flex-wrap: wrap;
    justify-content: flex-start;
    width: 100%;
}

.chip {
    flex-direction: row;
    align-items: center;
    justify-content: center;   /* centers chip content within its 50% slot */
    flex-basis: 50%;
    padding: 3px 2px;
}
```

Behaviour:
- **4 chips** → 2 rows of 2 (each chip takes exactly half the row)
- **1 chip** → sits in the left slot; the right slot is visually empty (no placeholder needed)
- `justify-content: flex-start` on the container keeps single chips left-aligned rather than centered in the row
- `justify-content: center` on each chip centers the icon+label within its 50% slot

Do **not** use `margin` on chips — it breaks the 50% calculation. Use `padding` instead.
