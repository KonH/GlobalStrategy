# Map Config Generator Rules

## Colonial territory handling

Territories that have their own `PARTOF` value in GeoJSON (e.g. "Gold Coast (GB)", "Senegal (FR)") become separate `CountryEntry` objects in `CountryConfig`. Adding them to `_predefinedColors` only fixes the display color — `FindByFeatureId` still returns the territory entry, not the colonial power's.

The correct approach is `_colonialParents`: a dictionary mapping territory `countryId` → parent `countryId`. After the main build loop, a merge pass moves all territory `mapFeatureId`s into the parent's `secondaryMapFeatureIds` and removes the territory entry. This way `FindByFeatureId` returns the colonial power for those features.
