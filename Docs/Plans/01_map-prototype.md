# Plan: Map Usage Prototype

## Goal

Load historical GeoJSON + raster map image, render a 2D world map in Unity, support looped camera panning, and report the country name on click.

---

## Data

### Sources

- **GeoJSON:** `aourednik/historical-basemaps` GitHub repo — file pattern `world_<year>.geojson`
- **Map image:** Natural Earth raster — file pattern `natural_earth_<resolution>.jpg`

### Local placement

Place both under `Assets/Map/`:
- `Assets/Map/world_1880.geojson` → imported as `TextAsset`
- `Assets/Map/natural_earth_16384.jpg` → imported as `Texture2D`

### StreamingAssets vs Assets

Use `Assets/Map/` (not StreamingAssets):
- Unity compresses the texture automatically
- `TextAsset` requires no platform path boilerplate
- Rebuild is acceptable since data is fixed for the prototype
- Use StreamingAssets only if runtime data-swapping without rebuild becomes needed

---

## Module Structure

```
Assets/Scripts/
├── Core/
│   └── Map/               # asmdef: GS.Core.Map (noEngineReferences: true)
│       ├── GeoJsonParser.cs
│       ├── MapFeature.cs
│       ├── Polygon.cs
│       └── Vector2d.cs
├── Unity/
│   └── Map/               # asmdef: GS.Unity.Map (refs GS.Core.Map)
│       ├── CoordinateConverter.cs
│       ├── MapMeshBuilder.cs
│       ├── MapRenderer.cs
│       ├── MapImageOverlay.cs
│       └── MapLoader.cs
└── Prototype/
    └── MapControls/       # asmdef: GS.Prototype.MapControls (refs GS.Unity.Map, GS.Core.Map)
        ├── MapCameraController.cs
        └── MapClickHandler.cs
```

---

## Approach

Split into three layers:

- **Core (pure C#, no Unity deps)** — GeoJSON parsing and data model; `noEngineReferences: true`
- **Unity integration** — mesh generation, texture loading, scene wiring
- **Prototype controls** — camera movement, click-to-identify

---

## Steps

### 1. Data Import

- Add `world_1880.geojson` and `natural_earth_16384.jpg` to `Assets/Map/`
- Wire as serialized fields (`TextAsset`, `Texture2D`) on `MapLoader`

### 2. Core Layer — `GS.Core.Map`

- `GeoJsonParser.cs` — pure C#; parses FeatureCollection JSON → `List<MapFeature>`
- `MapFeature.cs` — `string Id`, `string Name`, `List<Polygon> Polygons`
- `Polygon.cs` — `List<Ring> Rings` (index 0 = outer, rest = holes); `Ring` = `List<Vector2d>`
- `Vector2d.cs` — `double Lon`, `double Lat` struct; no Unity types

### 3. Unity Integration Layer — `GS.Unity.Map`

- `CoordinateConverter.cs` — equirectangular lon/lat → Unity XY; maps [-180,180] × [-90,90] to configurable world units
- `MapMeshBuilder.cs` — triangulates rings (earcut or Unity's `Triangulator`), builds one `Mesh` per feature
- `MapRenderer.cs` — spawns one `GameObject` per feature (`MeshFilter` + `MeshRenderer`); stores feature reference for click lookup
- `MapImageOverlay.cs` — background `Quad` scaled to map extents, assigned the raster `Texture2D`
- `MapLoader.cs` — MonoBehaviour entry point; reads assets, calls parser, calls builder, populates scene

### 4. Prototype Controls — `GS.Prototype.MapControls`

- `MapCameraController.cs` — orthographic camera; WASD / click-drag panning; X-axis wraps so the map loops horizontally
- `MapClickHandler.cs` — raycasts on mouse click → finds hit `GameObject` → logs feature `Name` / `Id` to console

### 5. Scene Setup

- Scene: `Assets/Scenes/Map/MapPrototype.unity`
- Hierarchy: `MapCamera` (orthographic), `MapRoot` (parent for all feature meshes), `ImageOverlay`
- Register scene in `ProjectSettings/EditorBuildSettings.asset`

---

## Out of Scope

- Province subdivision
- Adjacency graph
- Terrain, economy
- Antimeridian mesh splitting (defer unless artifacts appear)
