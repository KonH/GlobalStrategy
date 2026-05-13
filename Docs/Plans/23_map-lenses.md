# Goal
Add a Map Lenses feature that lets the player switch between three visual overlays on the map — Geographic (bare map, no highlights), Political (default country colours from `CountryVisualConfig`), and Org (countries tinted by the player's org influence level). A collapsible icon-button switcher sits in the bottom-left corner of the HUD, above the country-info panel. The design must be extensible so future lenses can be added with minimal friction.

# Approach
A new `MapLens` enum and `MapLensState : INotifyPropertyChanged` are added to `src/Game.Main/VisualState.cs` and wired into `VisualState`. A `ChangeLensCommand` in `src/Game.Commands/` is queued from the UI, processed in `GameLogic.Update`, and reflected back through `VisualStateConverter`. On the Unity side, `MapLensApplier` (a new `ITickable`) reads `VisualState.MapLens` each frame and recolours the active `MapRenderer`'s feature objects according to the active lens; the Political lens delegates to `CountryVisualConfig` as before, the Geographic lens clears all tints to a neutral grey, and the Org lens colours countries by the player's influence share (dark-to-bright gradient). `LensSwitcherView` (plain C#) manages the collapse/expand toggle in UXML, and `HUDDocument` wires it up with injection.

# Steps

1. **Domain — enum and state (`src/Game.Main/VisualState.cs`)**
   - Add `public enum MapLens { Political, Geographic, Org }` (in the `GS.Main` namespace, same file or a dedicated `MapLens.cs`).
   - Add `MapLensState : INotifyPropertyChanged` with `Lens` property and `Set(MapLens)` method (fires `PropertyChanged` only on change).
   - Add `public MapLensState MapLens { get; } = new MapLensState();` to `VisualState`.

2. **Domain — command (`src/Game.Commands/ChangeLensCommand.cs`)**
   - Add `public struct ChangeLensCommand : ICommand { public MapLens Lens; }`.
   - No manual registration needed. The source generator (`Game.SourceGenerators`) auto-generates the buffer field and `ReadChangeLensCommand()` accessor in `CommandAccessor.g.cs` once the struct implements `ICommand`. Just add the struct and rebuild.

3. **Domain — process command in `GameLogic` (`src/Game.Main/GameLogic.cs`)**
   - In `Update`, after the other command reads, read `_commandAccessor.ReadChangeLensCommand()` and update a stored `_currentLens` field, then call `VisualState.MapLens.Set(_currentLens)` (or delegate to `VisualStateConverter`).

4. **Domain — converter update (`src/Game.Main/VisualStateConverter.cs`)**
   - Add `UpdateMapLens` step that copies the current lens value from wherever it is stored to `_state.MapLens.Set(...)`. Alternatively the lens can be set directly in `GameLogic.Update` without going through the converter — choose the simplest path (direct set is fine since there is no ECS component for it).

5. **Rebuild DLLs**
   - Run `dotnet build src/GlobalStrategy.Core.sln -c Release` to push updated DLLs to `Assets/Plugins/Core/`.

6. **Unity — `MapLensApplier` (`Assets/Scripts/Unity/Map/MapLensApplier.cs`)**
   - New `MonoBehaviour` (not `ITickable`). Inject via `[Inject] void Construct(VisualState state, MapController mapController, CountryVisualConfig visualConfig, GS.Game.Configs.CountryConfig countryConfig)`.
   - Register with `builder.RegisterComponentInHierarchy<MapLensApplier>()` in `GameLifetimeScope` — it must exist as a GameObject in the scene hierarchy.
   - Update `GS.Unity.Map.asmdef`: add `precompiledReferences` for `Game.Main.dll` and `Game.Configs.dll` (or confirm they are auto-referenced via DLL import settings in `Assets/Plugins/Core/`).
   - Drive recolouring by subscribing to `VisualState.MapLens.PropertyChanged` in `OnEnable`; unsubscribe in `OnDisable`; apply immediately on enable.
   - In the handler: `var renderer = _mapController.ActiveRenderer; if (renderer == null) return;` — then iterate `renderer.FeatureObjects` and recolour via each feature's cached `MeshRenderer` material (access the `Material` via `meshRenderer.sharedMaterial` or the reference cached at render time — do **not** use `.material` which creates a new instance per call):
     - **Political**: look up `CountryVisualConfig.Find(go.name)`, use its colour at alpha 0.5; fall back to grey.
     - **Geographic**: set all features to `new Color(0.5f, 0.5f, 0.5f, 0.5f)` (uniform grey, no highlights).
     - **Org**: use `OrgMapState.Entries` (see step 7) to tint countries from a dark neutral to a saturated org colour based on influence ratio.

7. **Domain — per-country org colour data for Org lens (`src/Game.Main/VisualState.cs`)**
   - Add `OrgCountryEntry` with `CountryId` and `InfluenceRatio` (0–1 float).
   - Add `OrgMapState : INotifyPropertyChanged` with `IReadOnlyList<OrgCountryEntry> Entries` and a `Set(List<OrgCountryEntry>)` method.
   - Add `public OrgMapState OrgMap { get; } = new OrgMapState();` to `VisualState`.
   - Populate in `VisualStateConverter`: iterate countries and compute `playerInfluence / PoolSize` for the player org.
   - Rebuild DLLs again after this step.

8. **SVG icons — three lens icons**
   - Copy three icons from Bootstrap Icons to `Assets/UI/Icons/`:
     - `globe2.svg` — Geographic lens (already added per git status)
     - `map-fill.svg` (or `flag-fill.svg`) — Political lens
     - `eye-fill.svg` (or similar) — Org lens
   - Replace `fill="currentColor"` with `fill="#FFFFFF"` in each SVG.
   - Import with `manage_asset(action="import", path="...", properties={"generatedAssetType": "UIToolkitVectorImage"})`.
   - Verify no `currentColor` warnings in console.

9. **UXML — lens switcher template (`Assets/UI/HUD/LensSwitcher/LensSwitcher.uxml`)**
   - Root `VisualElement name="lens-switcher"`.
   - Child `Button name="lens-current-btn"` with icon slot (`VisualElement name="lens-current-icon"`).
   - Child `VisualElement name="lens-expand-panel"` (hidden by default, `display: none`) containing one `Button` per lens (`name="lens-btn-political"`, `"lens-btn-geographic"`, `"lens-btn-org"`), each with an icon child and a data-attribute for tooltip text.
   - Reference `SharedStyles.uss` and `LensSwitcher.uss`.

10. **USS — lens switcher styles (`Assets/UI/HUD/LensSwitcher/LensSwitcher.uss`)**
    - Layout only: icon button size (40 × 40 px), gap between buttons, expand-panel flex-direction column.
    - Active lens button gets `.lens-btn--active` (highlighted border using `gs-border-primary` tint).
    - No colour/typography overrides — use existing shared classes (`gs-btn`, `gs-btn--small`).

11. **USS — position in HUD (`Assets/UI/HUD/HUD.uss`)**
    - Add `.lens-switcher-panel` rule: `position: absolute; bottom: <fixed-px-offset>; left: 8px;`.
    - Use a hard-coded pixel value only — **do not use `calc()`**, it is not supported in UI Toolkit on Unity 6000.4.1f1 and silently fails. Measure or approximate the country-info panel height at design time and use a fixed `bottom` offset. Tune after layout is visible.

12. **UXML — wire template into HUD (`Assets/UI/HUD/HUD.uxml`)**
    - Add `<ui:Template name="LensSwitcher" src="...LensSwitcher.uxml"/>`.
    - Add `<ui:Instance template="LensSwitcher" name="lens-switcher" class="lens-switcher-panel"/>` inside `hud-root`, before the `country-info` instance.

13. **Unity — `LensSwitcherView` (`Assets/Scripts/Unity/UI/LensSwitcherView.cs`)**
    - Plain C# class (not MonoBehaviour).
    - Constructor: receives root `VisualElement`; queries `lens-current-btn`, `lens-expand-panel`, and the three lens buttons.
    - `Refresh(MapLens activeLens)`: update icon on `lens-current-btn`, toggle `.lens-btn--active` class. Do **not** reset `_isExpanded` here — a `Refresh()` call triggered by a lens-change state update must not collapse the panel mid-interaction.
    - The expand/collapse flag (`_isExpanded`) is local to the view (transient UI state). Toggle on `lens-current-btn.clicked`.
    - On each lens button click: invoke `Action<MapLens> OnLensSelected` callback (passed in constructor), then collapse (`_isExpanded = false`; hide expand panel).
    - Tooltip: register `PointerEnterEvent` on each lens button; show a plain label tooltip using `TooltipSystem` (passed into constructor) with the localized lens name.

14. **Unity — wire into `HUDDocument` (`Assets/Scripts/Unity/UI/HUDDocument.cs`)**
    - Add `LensSwitcherView _lensSwitcher;` field.
    - In `Awake`, after querying other views: `_lensSwitcher = new LensSwitcherView(root.Q("lens-switcher"), _tooltip, OnLensSelected);`.
    - Add `OnLensSelected(MapLens lens)` that calls `_commands.Push(new ChangeLensCommand { Lens = lens });`.
    - In `OnEnable`: subscribe `_state.MapLens.PropertyChanged += HandleLensChanged`; call `_lensSwitcher.Refresh(_state.MapLens.Lens)`.
    - In `OnDisable`: unsubscribe.
    - Add `HandleLensChanged` handler that calls `_lensSwitcher.Refresh(...)`.

15. **DI wiring (`Assets/Scripts/Unity/DI/GameLifetimeScope.cs`)**
    - Add `builder.RegisterComponentInHierarchy<MapLensApplier>();` — `MapLensApplier` is a MonoBehaviour in the scene hierarchy; do **not** use `RegisterEntryPoint` (that creates a separate pure-C# instance and skips scene injection).
    - Add `builder.RegisterComponentInHierarchy<HUDDocument>();` if not already registered (currently wired implicitly — verify).

16. **Localization keys (`Assets/Localization/en.asset`, `ru.asset`)**
    - Add `hud.lens.political` → "Political" / "Политическая"
    - Add `hud.lens.geographic` → "Geographic" / "Географическая"
    - Add `hud.lens.org` → "Organizations" / "Организации"

17. **Colour palette improvement for `CountryVisualConfig`**
    - Open `CountryVisualConfig` ScriptableObject in the Unity Editor (or edit the YAML directly).
    - Replace the current palette with a set of visually distinct, saturated hues spaced evenly around the colour wheel (e.g. 8–12 hues at saturation ~0.7, value ~0.75, alpha 0.6 in play-mode).
    - Suggested hues (HSV): 0°, 30°, 60°, 120°, 160°, 210°, 260°, 310° — avoids greens-only clustering and improves map readability.
    - Re-assign colours to countries in the config after picking the palette.

18. **Smoke-test**
    - Enter Play Mode; verify Political lens shows coloured countries, Geographic shows uniform grey, Org tints countries by influence.
    - Verify switcher collapses after selecting a lens and the active-lens button is highlighted.
    - Verify tooltips appear on hover.
    - Verify new country colours are visually distinct on the map.

Use `/implement` to start working on the plan or request changes.
