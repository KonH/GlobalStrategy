# Map System Architecture

## Rendering model — all lenses render via ProvinceRenderer

`MapRenderer`/`FeatureIdentifier` (the old `mapFeatureId → CountryEntry.FindByFeatureId → fill mesh` path) have been removed as dead code. All four map lenses (`Province`, `Political`, `Org`, `Geographic`) render from `ProvinceRenderer`'s per-province meshes (`MapController.ActiveProvinceRenderer.FeatureObjects`), each carrying a `ProvinceIdentifier` component.

- Per-province fill colour is resolved from the **runtime** `VisualState.ProvinceOwnership.OwnerByProvinceId[go.name]` map (`go.name` == `provinceId`), falling back to the province's static seed `ProvinceIdentifier.CountryId` only if the province is absent from that map.
- Province border child renderers (the `_Border` child GameObject) are enabled **only** in the `Province` lens — country lenses (`Political`/`Org`/`Geographic`) stay border-free, matching the old country-lens look.
- `MapLensApplier` subscribes to `VisualState.ProvinceOwnership.PropertyChanged` (alongside `MapLens`/`OrgMap`/`DiscoveredCountries`) and re-applies the current lens so a runtime ownership change recolours immediately.

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
