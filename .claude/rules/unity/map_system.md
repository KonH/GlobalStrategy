# Map System Architecture

## Feature GO naming

`MapRenderer` names each feature GameObject with its `mapFeatureId` (from `MapFeatureConfig`).
When resolving a clicked feature's id, use `go.name` — not `FeatureIdentifier.FeatureId`, which holds the raw `geoJsonId` and won't match `CountryConfig` lookups.

## Country ID casing

`CountryConfig` uses `PascalCase_With_Underscores` for `countryId` (e.g. `Russian_Empire`, `Ottoman_Empire`). Locale keys mirror the same casing (`country_name.Russian_Empire`). Never assume lowercase — a mismatch silently falls through to a "key not found" warning at runtime.

## Accessing the active MapRenderer

Map prefabs are instantiated at runtime in `MapController.Start`, after all `Awake` calls have run.
`FindObjectOfType<MapRenderer>()` in `Awake` will always return null.

Components that need the active renderer must hold a serialized reference to `MapController` and call `MapController.ActiveRenderer` per-frame — the controller tracks `_current` vs `_forward` and returns the correct one.
