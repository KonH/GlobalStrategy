---
paths:
  - "Assets/Scripts/**/Map/**"
---

# Map System Architecture

## Rendering model — all lenses render via ProvinceRenderer

`MapRenderer`/`FeatureIdentifier` (the old `mapFeatureId → CountryEntry.FindByFeatureId → fill mesh` path) have been removed as dead code. All four map lenses (`Province`, `Political`, `Org`, `Geographic`) render from `ProvinceRenderer`'s per-province meshes (`MapController.ActiveProvinceRenderer.FeatureObjects`), each carrying a `ProvinceIdentifier` component.

- Per-province fill colour is resolved from the **runtime** `VisualState.ProvinceOwnership.OwnerByProvinceId[go.name]` map (`go.name` == `provinceId`), falling back to the province's static seed `ProvinceIdentifier.CountryId` only if the province is absent from that map.
- Province lens still uses full-ring `_Border` / `ProvinceBorderRendererMarker` children. Political and Org lenses use selective `_CountryOrgBorder` / `CountryOrgBorderRendererMarker` lines derived from live ownership/org state (segments whose neighboring province has a different owner, or different top org with Political-style fallback when org data is missing). Geographic stays border-free.
- `ProvinceRenderer.RebuildCountryOrgBorders` masking fails **open**: a ring segment `BorderSegmentIndex` couldn't attribute to any neighbor (`neighborId == ""`, e.g. `provinces_1880.json` geometry that touches a real neighbor but is too coarse/drifted for the exact+proximity match, or a genuine coastline) always draws. Only segments with a positively-identified, confirmed-same-owner (or same-top-org) neighbor are suppressed. Trade-off accepted deliberately: hiding by default silently dropped real country/org boundaries on attribution gaps (e.g. Évora/Bragança vs Spain); the cost is that true coastlines now also draw a Political/Org border line (country outline look), which the original `26_08_04_17_country-org-borders` spec had called for suppressing.
- Province visibility is gated by `VisualState.WorldCountries.CountryIds` (`WorldCountriesState`): a province whose owner country is **not** in that set (unavailable / not spawned into the world) stays hidden — fill, borders, and occupation hatch all off. Available world countries are visible.
- `MapLensApplier` subscribes to `VisualState.ProvinceOwnership.PropertyChanged` (alongside `MapLens`/`OrgMap`/`WorldCountries`) and re-applies the current lens so a runtime ownership change recolours immediately.

## Country territory is a derived aggregation

Country territory/area is the aggregate of provinces whose current runtime owner matches that country — via `ProvinceOwnershipSystem.GetProvincesByOwner` / `VisualState.ProvinceOwnership.OwnerByProvinceId` — **not** `CountryEntry.MainMapFeatureIds`/`SecondaryMapFeatureIds`. Those two feature-id lists remain on `CountryEntry` only for `InitSystem.BuildProximityMap`/`ComputeMinDistance` (proximity/distance) and the Python province-generation pipeline; they are no longer consumed for rendering or area.

## Country ID casing

`CountryConfig` uses `PascalCase_With_Underscores` for `countryId` (e.g. `Russian_Empire`, `Ottoman_Empire`). Locale keys mirror the same casing (`country_name.Russian_Empire`). Never assume lowercase — a mismatch silently falls through to a "key not found" warning at runtime.

## Province ID vs country ID — always resolve runtime owner before domain lookups

A province GameObject's `go.name` is its `provinceId` (e.g. `Russian_Empire__moscow`), not a `countryId`. Any code that needs the current owning country (visual config, control, resources) must resolve it via `VisualState.ProvinceOwnership.OwnerByProvinceId[go.name]`, falling back to `ProvinceIdentifier.CountryId` (the static seed id) only if the province isn't present in that map (e.g. pre-game scenes with no ECS `World`, such as `CountrySelection.unity`'s `SelectOrgMapFilter`, which has no runtime ownership to consult and legitimately uses the static seed id).

Visual state from ECS (e.g. `OrgMap`, control) uses domain `countryId` — not `mapFeatureId` or `provinceId`.

## Clicks always hit-test provinces

`MapClickHandler` hit-tests `MapController.ActiveProvinceRenderer.FindFeatureAt(...)` for every lens. In the `Province` lens, a hit pushes `SelectProvinceCommand`. In all other lenses, a hit resolves the clicked province's runtime owner (same fallback rule above) and pushes `SelectCountryCommand(ownerId)`.

## Accessing the active ProvinceRenderer

Map prefabs are instantiated at runtime in `MapController.Start`, after all `Awake` calls have run.
`FindObjectOfType<ProvinceRenderer>()` in `Awake` will always return null.

Components that need the active renderer must hold a serialized reference to `MapController` and call `MapController.ActiveProvinceRenderer` per-frame — the controller tracks `_current` vs `_forward` and returns the correct one.

## Any new map input handler must gate against UI, not just poll hardware state

`MapCameraController` (zoom/pan) and `MapClickHandler` (clicks) all read `Mouse`/`Touchscreen` input directly every frame — this bypasses UI Toolkit's event system entirely, so nothing stops that input from *also* driving the camera while the player is interacting with a UI panel on top of it. This has recurred as a bug more than once (scrolling/dragging a list inside an open window also zoomed/panned the map underneath).

Two checks, both required for any new hardware-polling input handler in this folder:
- `GS.Unity.Common.ModalState.IsModalOpen` — cheap bool, true while any full modal window is open.
- `GS.Unity.Common.UIPointerState.IsPointerOverUI(screenPosition)` — real UI Toolkit hit-test (`IPanel.Pick` via `RuntimePanelUtils.ScreenToPanel`) against the shared runtime panel (set once in `HUDDocument.Awake()`, since the whole project shares one `PanelSettings`). Use this even for **non-modal** UI (HUD panels, tooltips) — `ModalState` alone only covers full modal windows.

**Never use `EventSystem.current.IsPointerOverGameObject()`** for this — it does not reliably detect UI Toolkit panels under the New Input System in this Unity version (see `.claude/rules/unity/uitoolkit.md`'s "Click Blocking for Modal Dialogs").

For a **drag gesture** (e.g. map pan-by-drag), only gate the *start* of the gesture — check once at the down→held transition, the same way `MapClickHandler.BeginPress` does. Don't re-check every frame while already dragging: if the cursor crosses over a UI element mid-drag, a continuous check would abruptly cut the gesture short mid-motion.
