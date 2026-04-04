# UI Implementation Rules

## Prefab Structure

Feature UI prefabs live under `Assets/Prefabs/UI/[Feature]/` (e.g. `Assets/Prefabs/UI/Map/`).
Base/common prefabs live under `Assets/Prefabs/UI/Common/`, organized by type:
- `Common/Panels/` — background panels
- `Common/Layout/` — layout groups (vertical, horizontal)
- `Common/Text/` — text holder prefabs

## Base Prefab Usage

Build feature prefabs by composing common prefabs as nested instances — do not duplicate components. Example structure:

```
CountryInfoPanel          ← feature prefab root (RectTransform only)
  ├── PanelBackground     ← from Common/Panels/Panel.prefab
  └── VerticalLayout      ← from Common/Layout/VerticalLayout.prefab
        └── CountryNameHolder  ← from Common/Text/HeaderTextHolder.prefab
```

- Root has **no Image or CanvasRenderer** — add visual components only when needed via script or explicit design decision
- Children are nested prefab instances, not copies — modifications to the base prefab propagate

## Root RectTransform

The prefab root uses full stretch so the container controls sizing:

```
anchorMin: (0, 0)
anchorMax: (1, 1)
anchoredPosition: (0, 0)
sizeDelta: (0, 0)
pivot: (0.5, 0.5)
```

## Instantiating in Scene

When placing a UI prefab into a scene container, zero out the RectTransform on the instance — same values as above. The container (e.g. `BottomPanelContainer`) is responsible for actual layout and sizing; the instance must carry no offsets or size overrides.

## UI Script Structure

Split UI logic into two components on the prefab root:

**View** (e.g. `CountryInfoPanel`) — display only:
- Holds serialized refs to child components (TMP text, images, etc.)
- Exposes simple methods: `Present(...)`, `Hide()`
- No knowledge of game data or other systems

**Controller** (e.g. `CountryInfoPanelController`) — state and wiring:
- Holds a serialized ref to the View component
- Hides the panel by default in `Awake`: `_panel.Hide()`
- Exposes a public handler method (e.g. `HandleSelectionChanged(CountryEntry entry)`) that callers invoke directly
- Checks data for null — null means "no selection", calls `Hide()`; otherwise calls `Present(...)`

**Caller** (e.g. `MapClickHandler`) — drives the controller:
- Holds `[SerializeField] CountryInfoPanelController _panelController`
- Calls the handler method directly: `_panelController?.HandleSelectionChanged(entry)`
- No delegate subscription needed for simple one-to-one connections
