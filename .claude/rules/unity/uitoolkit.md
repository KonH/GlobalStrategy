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
