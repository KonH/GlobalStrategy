# UI Toolkit Architecture

The project uses Unity UI Toolkit exclusively. Do not add Canvas, UGUI, or uGUI components.

## State Layer

- `VisualStateHolder` MonoBehaviour holds a `VisualState` instance (plain C# object, root of all UI state)
- `VisualState` owns typed state objects (e.g. `CountryState SelectedCountry`)
- State objects expose a `Set(...)` method that updates all fields atomically and fires `OnChanged`
- Binding components subscribe to `OnChanged` in `OnEnable` / unsubscribe in `OnDisable`

## Assets

- UXML and USS files live under `Assets/UI/[Feature]/`
- `PanelSettings` asset lives alongside the UXML/USS it governs
- Link stylesheets via `<ui:Style src="...">` inside the UXML (use `manage_ui link_stylesheet` or write directly)

## UIDocument Binding Components

- One MonoBehaviour per UIDocument; queries named elements in `Awake` via `rootVisualElement.Q<T>("name")`
- Holds a `[SerializeField] VisualStateHolder _stateHolder` reference
- `Refresh(state)` applies all visual changes — called from `OnEnable` (initial sync) and the `OnChanged` handler

## Callers

- Systems that drive state (e.g. `MapClickHandler`) hold `[SerializeField] VisualStateHolder _stateHolder`
- Call `_stateHolder?.State.SelectedCountry.Set(...)` — no panel controller, no delegate wiring needed
