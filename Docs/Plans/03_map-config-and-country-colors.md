# Plan: Map Config, Country Data, and Color Overlay

## Goal

1. ScriptableObject configs for map features and countries, generated from the GeoJSON source.
2. `MapLoader` renders only configured features, named by `mapFeatureId`.
3. `MapClickHandler` logs country info (id, display name, main/secondary role).
4. Country color meshes rendered half-transparent over the map image.

---

## GeoJSON Analysis Summary

- 236 features, 163 unique `PARTOF` values (countries).
- No `feature_NN` generics in this file — filter anyway for robustness.
- 1 non-ASCII name: `Sweden\xffNorway` (encoding corruption).
- 6 colonial features (`NAME != PARTOF`): Iceland, Greenland (→Denmark), Netherlands Indies (→Netherlands), Angola (→Portugal), Algeria FR (→Algeria), Victoria UK (→UK).
- 8 countries have multiple main features with the same NAME (Spain×2, France×2, Germany×2, Russian Empire×2) — split polygons, all main.

---

## Data Models

### `MapFeatureEntry` (`[Serializable]`, in `GS.Unity.Map`)
```
string geoJsonId       // raw NAME from source
string normalizedId    // non-ASCII stripped/replaced
string mapFeatureId    // normalized, Az_ no spaces (used as GO name)
```

### `MapFeatureConfig : ScriptableObject` (`GS.Unity.Map`)
```
List<MapFeatureEntry> Features
MapFeatureEntry Find(string geoJsonId)
```

### `CountryEntry` (`[Serializable]`, in `GS.Unity.Map`)
```
string countryId                   // normalized PARTOF
string displayName                 // PARTOF without non-ASCII chars
List<string> mainMapFeatureIds     // features where NAME == PARTOF
List<string> secondaryMapFeatureIds // features where NAME != PARTOF
Color color
```

### `CountryConfig : ScriptableObject` (`GS.Unity.Map`)
```
List<CountryEntry> Countries
CountryEntry FindByFeatureId(string mapFeatureId)
```

---

## Approach

### Normalization rules
- Replace non-ASCII chars: strip or substitute with closest ASCII equivalent (e.g. `ø→o`, `é→e`, unknown → empty).
- `mapFeatureId`: keep A–Z, a–z, 0–9; replace everything else with `_`; collapse consecutive `_`; trim leading/trailing `_`.

### Main vs secondary
- Feature is **main** if `NAME == PARTOF` in GeoJSON.
- Feature is **secondary** if `NAME != PARTOF` (colonial/territory); its `countryId` is the normalized `PARTOF`.

### Country colors
Predefined for major 1880 powers (approximate geopolitical convention):

| Country | Color |
|---|---|
| United Kingdom of Great Britain and Ireland | `#C8788C` (British pink) |
| France | `#8CB4DC` (French blue) |
| Russian Empire | `#A0C87A` (Russian green) |
| Germany | `#C8B478` (German tan) |
| Ottoman Empire | `#78C0A0` (Ottoman teal) |
| Austria-Hungary | `#E8D878` (Habsburg yellow) |
| United States | `#78A0C8` (US blue) |
| Qing / China | `#C8A078` (Qing orange) |
| Spain | `#C89678` (Spanish orange) |
| Portugal | `#78C878` (Portuguese green) |
| Imperial Japan | `#C878A0` (Japan rose) |
| Italy | `#90C890` (Italian green) |
| Netherlands | `#E8A050` (Dutch orange) |
| Brazil | `#78C8A0` (Brazilian teal) |
| Sweden\*Norway | `#7890C8` (Nordic blue) |

All others: evenly distributed HSV hues (S=0.45, V=0.80), assigned in alphabetical order of `countryId`.

### Color overlay rendering
- `MapRenderer` switches from opaque hash-color to the country color at **alpha = 0.5** using `Unlit/Transparent` shader (or `Sprites/Default`).
- `MapRenderer` needs a reference to `CountryConfig` to look up the color per feature.
- Feature meshes remain at z=0 (in front of `ImageOverlay` at z=1), now semi-transparent.

---

## Module Assignment

| File | Assembly |
|---|---|
| `MapFeatureEntry.cs`, `MapFeatureConfig.cs` | `GS.Unity.Map` |
| `CountryEntry.cs`, `CountryConfig.cs` | `GS.Unity.Map` |
| `MapConfigGenerator.cs` (Editor) | new `GS.Editor.MapConfig` asmdef |
| `GeoJsonParser.cs` (add PartOf) | `GS.Core.Map` |
| `MapFeature.cs` (add PartOf field) | `GS.Core.Map` |
| `MapRenderer.cs` (color lookup) | `GS.Unity.Map` |
| `MapLoader.cs` (config filter) | `GS.Unity.Map` |
| `Map.cs` (pass config refs) | `GS.Unity.Map` |
| `MapClickHandler.cs` (country log) | `GS.Prototype.MapControls` |

---

## Steps

### Step 1 — Core model: add `PartOf`
1. Add `public string PartOf;` to `MapFeature`.
2. Update `GeoJsonParser.ParseFeature` to read `PARTOF` property and set `feature.PartOf`.

### Step 2 — Unity data models
1. Create `MapFeatureEntry.cs` and `MapFeatureConfig.cs` in `Assets/Scripts/Unity/Map/Config/`.
2. Create `CountryEntry.cs` and `CountryConfig.cs` in the same folder.

### Step 3 — Editor generator
1. Create `Assets/Scripts/Editor/MapConfig/` folder with a new `GS.Editor.MapConfig.asmdef` (Editor platform only).
2. Create `MapConfigGenerator.cs`:
   - Menu item `Tools/GlobalStrategy/Generate Map Configs`.
   - Reads `Assets/Map/world_1880.json` via `AssetDatabase`.
   - Normalizes names; skips `feature_\d+` generics.
   - Builds `MapFeatureConfig` asset at `Assets/Configs/MapFeatureConfig.asset`.
   - Builds `CountryConfig` asset at `Assets/Configs/CountryConfig.asset` with predefined + auto colors.
   - Saves with `AssetDatabase.SaveAssets()`.

### Step 4 — MapLoader filter
1. Add `[SerializeField] MapFeatureConfig _mapFeatureConfig` and `[SerializeField] CountryConfig _countryConfig` to `MapLoader`.
2. Pass both configs into `Map.Initialize`.
3. In `Map.Initialize`, pass them to `MapRenderer`.

### Step 5 — MapRenderer color overlay
> **Known issue:** feature meshes are currently invisible in the prototype even without any overlay.
> Diagnose and fix rendering before adding transparency (check that `MapController.Start` is firing,
> the prefab refs are wired, and the camera z-range covers z=0 meshes).

1. Verify meshes are visible with the existing opaque `Unlit/Color` shader before proceeding.
2. Add `CountryConfig` ref parameter to `MapRenderer.Render`.
3. Look up `CountryEntry` by `mapFeatureId`; use `entry.color` with alpha=0.5; fall back to grey if not found.
4. Use `Unlit/Transparent` shader instead of `Unlit/Color`.
5. Use `feature.Name` → `MapFeatureConfig.Find` → `mapFeatureId` for GO name.

### Step 6 — MapClickHandler country log
1. Add `[SerializeField] CountryConfig _countryConfig` to `MapClickHandler` (or find it via scene reference).
2. On click, resolve feature → `CountryEntry`; log: `countryId`, `displayName`, and whether the clicked feature is in `mainMapFeatureIds` or `secondaryMapFeatureIds`.

### Step 7 — Scene wiring
1. Run `Tools/GlobalStrategy/Generate Map Configs` to produce the SO assets.
2. Wire `_mapFeatureConfig` and `_countryConfig` on `MapLoader` in scene.
3. Wire `_countryConfig` on `MapClickHandler`.
4. Save scene.

---

## Out of Scope

- Manual editing of country colors in Inspector (generator overwrites on each run).
- Non-1880 datasets.
- Country border line rendering (separate from fill meshes).
