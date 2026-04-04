# Map System Architecture

## Feature GO naming

`MapRenderer` names each feature GameObject with its `mapFeatureId` (from `MapFeatureConfig`).
When resolving a clicked feature's id, use `go.name` — not `FeatureIdentifier.FeatureId`, which holds the raw `geoJsonId` and won't match `CountryConfig` lookups.

## Accessing the active MapRenderer

Map prefabs are instantiated at runtime in `MapController.Start`, after all `Awake` calls have run.
`FindObjectOfType<MapRenderer>()` in `Awake` will always return null.

Components that need the active renderer must hold a serialized reference to `MapController` and call `MapController.ActiveRenderer` per-frame — the controller tracks `_current` vs `_forward` and returns the correct one.
