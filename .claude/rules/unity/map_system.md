# Map System Architecture

## Feature GO naming

`MapRenderer` names each feature GameObject with its `mapFeatureId` (from `MapFeatureConfig`).
When resolving a clicked feature's id, use `go.name` — not `FeatureIdentifier.FeatureId`, which holds the raw `geoJsonId` and won't match `CountryConfig` lookups.

## Country ID casing

`CountryConfig` uses `PascalCase_With_Underscores` for `countryId` (e.g. `Russian_Empire`, `Ottoman_Empire`). Locale keys mirror the same casing (`country_name.Russian_Empire`). Never assume lowercase — a mismatch silently falls through to a "key not found" warning at runtime.

## mapFeatureId vs countryId — always resolve before domain lookups

`go.name` on a feature GameObject is a `mapFeatureId` (e.g. `Gold_Coast_GB`), **not** a domain `countryId`. Any code that needs to look up domain data (visual config, influence, resources) must resolve the feature ID first:

```csharp
var country = _domainCountryConfig?.FindByFeatureId(mapFeatureId);
string domainId = country != null ? country.CountryId : mapFeatureId;
```

Skipping this step silently fails for secondary features (colonial territories, island groups) which have `mapFeatureId`s distinct from their owning country's `countryId`. The main feature of a country often has the same string as `countryId`, masking the bug until secondary features are encountered.

Visual state from ECS (e.g. `OrgMap`, influence) also uses domain `countryId` — not `mapFeatureId`.

## Accessing the active MapRenderer

Map prefabs are instantiated at runtime in `MapController.Start`, after all `Awake` calls have run.
`FindObjectOfType<MapRenderer>()` in `Awake` will always return null.

Components that need the active renderer must hold a serialized reference to `MapController` and call `MapController.ActiveRenderer` per-frame — the controller tracks `_current` vs `_forward` and returns the correct one.
