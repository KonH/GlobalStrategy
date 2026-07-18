# UI Toolkit Architecture

The project uses Unity UI Toolkit exclusively. Do not add Canvas, UGUI, or uGUI components.

## Layer Model

In practice, every UI surface in every scene (`GameHUD`, `GameMenuUI`, `SettingsWindowUI`, `MainMenuUI`, `LoadWindowUI`, `FlyTextUI`, etc.) shares a single `PanelSettings` asset:

```
Assets/UI/HUD/          → HUDPanelSettings.asset  — the only PanelSettings wired into any scene
Assets/UI/Overlay/      → OverlayPanelSettings.asset — exists on disk but unused; not referenced by any scene
```

There is no `ModalPanelSettings.asset`. Layering between documents sharing `HUDPanelSettings.asset` is controlled entirely via `UIDocument.sortingOrder` — higher values draw on top. Most existing documents use `sortingOrder: 0`. `FlyTextNotifierDocument._topMostSortingOrder` (default `1000`, serialized field) is applied in `Awake()` so the fly-text layer renders above everything else by default.

If a future UI surface needs to render above fly text, pick a `sortingOrder` higher than `1000` — don't rely on scene-authoring discretion.

Use `manage_ui create_panel_settings` to create additional PanelSettings assets if a genuinely separate render target is ever needed.

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

### Incremental diff Refresh — accumulating/animating lists

The full-rebuild `Refresh()` above is wrong for a list where entries must independently animate in and out (e.g. a scrolling log). Instead, key each rendered element by a stable identity (e.g. a monotonic `SequenceId`) in a `Dictionary<long, VisualElement>`, diff the new state against that dictionary, and only touch what changed: new ids get a fresh element with a short fade-in, ids no longer present get a longer fade-out before removal, everything else is left alone. Fade transitions are driven per-element via `IStyle.transitionDuration` plus `element.schedule.Execute(...).ExecuteLater(...)` to remove the element only after its fade-out finishes. See `ActionLogView` (`Assets/Scripts/Unity/UI/ActionLogView.cs`) for a concrete example.

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

**Template USS must be explicitly imported into the parent document for C#-created elements.** When a UXML template is embedded via `<ui:Instance>`, the template's own `<ui:Style>` declarations apply to its static UXML elements. C# code that creates VisualElements and adds them to containers *inside* the template instance still resolves classes against the parent document's loaded stylesheets — not the template's. Fix: import the template's USS into the parent UXML:

```xml
<!-- In parent.uxml, alongside other <ui:Style> declarations: -->
<ui:Style src="project://database/Assets/UI/Feature/Template.uss"/>
```

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

## Known Event Bugs (Unity 6000.4.1f1)

### Button.clicked and ClickEvent silently fail

`Button.clicked` does not reliably fire in Unity 6000.4.1f1 even when all conditions are met:
- `PointerDownEvent` reaches the button ✓
- `PointerCapture` fires ✓
- `PointerUpEvent` reaches the button with `ContainsPoint = true` ✓

The same applies to `ClickEvent` on plain `VisualElement`s.

**Workaround — use `PointerUpEvent` with a manual bounds check everywhere:**

```csharp
element.RegisterCallback<PointerUpEvent>(e => {
    if (e.button == 0 && element.ContainsPoint(e.localPosition)) {
        DoAction();
    }
});
```

Do **not** use `Button.clicked` or `ClickEvent` for any interactive element in this project.

### PickingMode.Ignore is not recursive

Setting `panel.pickingMode = PickingMode.Ignore` on a container does **not** propagate to its children. Children retain `PickingMode.Position` and can still intercept pointer events, blocking elements underneath (e.g. tooltip panels blocking card Play buttons).

**Fix:** apply `PickingMode.Ignore` recursively after building dynamic content:

```csharp
static void SetPickingIgnoreRecursive(VisualElement element) {
    element.pickingMode = PickingMode.Ignore;
    foreach (var child in element.Children())
        SetPickingIgnoreRecursive(child);
}
```

## Animated State Transitions — SuppressRefresh Pattern

When a UI animation spans a state change (e.g. a card play sequence that hides a card, pushes a command, waits for a result, then animates a new card in), reactive `Refresh()` calls triggered by `PropertyChanged` will rebuild the UI mid-animation and reset any opacity/position changes already applied.

**Pattern:** expose a `SuppressRefresh` flag on the view class and guard `Refresh()` with it.

```csharp
public bool SuppressRefresh { get; set; }

public void Refresh(SomeState state) {
    if (SuppressRefresh) { return; }
    // ... rebuild logic ...
}
```

**In the animation coroutine:**

```csharp
// Before pushing any state-changing command:
if (_view != null) { _view.SuppressRefresh = true; }
_commands.Push(new PlayActionCommand { ... });

// ... animation steps ...

// At the point where you need the UI to rebuild with new state (one frame only):
if (_view != null) { _view.SuppressRefresh = false; }
yield return null;   // one Refresh() runs here
if (_view != null) { _view.SuppressRefresh = true; }

// ... animate the new element ...

// Always reset unconditionally at the end, outside any conditional blocks:
if (_view != null) { _view.SuppressRefresh = false; }
```

**Critical:** reset to `false` outside any `if (newElement != null)` block — otherwise a failed element lookup leaves suppression permanently on.

## Layout Gotchas

### align-items: stretch needs a defined container width

In a `flex-direction: column` container, `align-items: stretch` makes children fill the container's cross-axis (width). If the container has no explicit width, the result is unreliable — children may not all end up the same width.

**Fix:** add `min-width` to the container to give `stretch` a concrete value to work with.

### align-self on a child overrides the parent's align-items

If any child has `align-self` set — including via a shared stylesheet — it overrides `align-items: stretch` from the parent. Check for leftover `align-self: flex-end` or similar on elements being moved into a new layout container.

### gap shorthand may not parse in UXML inline styles

`gap: 80px` in a UXML `style="..."` attribute can be silently ignored. Prefer `margin-right`/`margin-left` on individual children, or declare the gap in a USS class.

### Absolute-only children give wrapper zero layout height

If every child of a wrapper has `position: Absolute`, the wrapper has no layout height (only explicit `height`/`min-height` applies). Keep at least one **relative-positioned** child to establish the wrapper's natural height.

## Closeable Overlay Slides

### CSS hiding: use `display: none`, not opacity/translate

`opacity: 0` and `translate` only visually hide an element — it stays in the layout and intercepts pointer events. `display: none` fully removes the element from the picking tree. For slides that must not block clicks when closed, use `display: none` in the base CSS class and `display: flex` in the `--open` modifier.

If opacity/translate is used for the animation, `PickingMode` must be managed in code (see below).

### Recursive PickingMode + post-Refresh re-apply

Two traps when toggling `PickingMode` on a slide container:
- `PickingMode.Ignore` on a container is **not recursive** — child elements keep `PickingMode.Position` and still intercept events.
- `Refresh()` typically calls `.Clear()` and recreates child elements; new elements get default `PickingMode.Position`, silently undoing any `Ignore` set on the container.

Correct pattern:

```csharp
static void SetPickingModeRecursive(VisualElement el, PickingMode mode) {
    el.pickingMode = mode;
    foreach (var child in el.Children()) { SetPickingModeRecursive(child, mode); }
}

void SetSlideOpen(bool open) {
    if (open) {
        slide.AddToClassList("slide--open");
        SetPickingModeRecursive(slide, PickingMode.Position);
    } else {
        slide.RemoveFromClassList("slide--open");
        SetPickingModeRecursive(slide, PickingMode.Ignore);
        tooltip?.HideAll();  // see Tooltip cleanup below
    }
}

// After each Refresh() that rebuilds the slide's children:
view.Refresh(state);
if (!_slideOpen) { SetPickingModeRecursive(slide, PickingMode.Ignore); }
```

### Tooltip cleanup

`PointerLeaveEvent` does not fire when a slide hides via opacity/translate (the element stays in the tree). Open tooltips persist at their last screen position. Call `tooltip.HideAll()` when closing the slide and add `HideAll()` as a public method on `TooltipSystem` if it doesn't exist yet.
