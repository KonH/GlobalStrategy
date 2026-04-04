# Plan: Map Prototype Improvements

## Goal

1. Add mouse-wheel zoom to the map camera with configurable min/max limits.
2. Replace the current single-map infinite scroll with a smooth dual-instance approach: a `Map` prefab wraps both the mesh layer and the image overlay; a new `MapController` keeps two instances (Current + Forward) positioned so the seam is never visible.

---

## Current State

- `MapCameraController` — keyboard + right-drag pan, X-wrap only, no zoom.
- `MapLoader` — entry point; holds refs to `MapRenderer` and `MapImageOverlay`; calls both directly; wired in scene.
- `MapRenderer` — MonoBehaviour on `MapRoot`; spawns one child `GameObject` per feature.
- `MapImageOverlay` — MonoBehaviour on a separate `ImageOverlay` quad.
- No prefabs exist.

---

## Approach

### 1. Mouse-wheel zoom

Add scroll handling to `MapCameraController`:
- Read `Mouse.current.scroll.ReadValue().y` each frame.
- Adjust `Camera.orthographicSize` by a configurable `_zoomSpeed` (e.g. `10f`), clamped to `[_minZoom, _maxZoom]` (e.g. `20f`–`200f`).

### 2. Map prefab + Map component

Create `Assets/Prefabs/Map/Map.prefab` with this hierarchy:

```
Map (root)          ← Map component
├── MapRoot         ← MapRenderer component
└── ImageOverlay    ← MapImageOverlay component (Quad)
```

`Map` is a new MonoBehaviour on the prefab root:
- `[SerializeField] MapRenderer _renderer` — assigned in prefab
- `[SerializeField] MapImageOverlay _overlay` — assigned in prefab
- `void Initialize(List<MapFeature> features, Texture2D texture)` — calls `_renderer.Render` + `_overlay.Setup`

`Map` is the only public surface — callers never reach `MapRenderer` or `MapImageOverlay` directly.

### 3. MapLoader

`MapLoader` keeps the asset refs and the prefab ref. Single public method `Load()`:
- Instantiates the `Map` prefab
- Gets the `Map` component
- Calls `map.Initialize(_features, _mapTexture)` (parses GeoJSON internally if not yet cached)
- Returns the `Map` instance

Fields:
```
[SerializeField] Map _mapPrefab
[SerializeField] TextAsset _geoJsonAsset
[SerializeField] Texture2D _mapTexture
List<MapFeature> _features   // parsed once, reused across Load() calls
```

No `Start()` — loading is triggered on demand by `MapController`.

### 4. MapController

New MonoBehaviour in `GS.Unity.Map`. Owns the two-instance scroll logic. Only refs needed: `MapLoader` and the camera transform.

Fields:
```
[SerializeField] MapLoader _loader
[SerializeField] Camera _camera
Map _current, _forward
```

Lifecycle:
- `Start` — call `_loader.Load()` twice; position `_current` at `x = 0`, `_forward` at `x = +MapWidth`.
- `Update` — after camera moves, update Forward position to stay in the direction of travel; swap Current↔Forward and shift camera when `|cameraX| > MapWidth / 2`.

Positioning rule:
- `_current` always at `x = 0`.
- Forward slot: `cameraX >= 0` → `x = +MapWidth`; `cameraX < 0` → `x = -MapWidth`.
- Swap trigger: `|cameraX| > MapWidth / 2` → swap refs, shift camera by `∓MapWidth`, re-evaluate Forward slot.

---

## Module Assignment

| File | Assembly |
|---|---|
| `Map.cs` | `GS.Unity.Map` |
| `MapLoader.cs` (refactored) | `GS.Unity.Map` |
| `MapController.cs` | `GS.Unity.Map` |
| `MapCameraController.cs` (zoom added) | `GS.Prototype.MapControls` |

---

## Steps

### Step 1 — Zoom
1. Edit `MapCameraController`: add `_zoomSpeed`, `_minZoom`, `_maxZoom` fields; add `HandleZoom()` called from `Update`.

### Step 2 — Map component
1. Create `Assets/Scripts/Unity/Map/Map.cs` with serialized refs to `MapRenderer` + `MapImageOverlay` and `Initialize`.

### Step 3 — MapLoader refactor
1. Rewrite `MapLoader`: keep asset refs + prefab ref; add `Load()` method; remove `Start()`.

### Step 4 — MapController
1. Create `Assets/Scripts/Unity/Map/MapController.cs` with dual-instance positioning logic.

### Step 5 — Map prefab
1. Create `Map` prefab from the existing `MapRoot` + `ImageOverlay` objects via MCP (`manage_gameobject` + `manage_prefabs`).
2. Attach `Map` component; wire `_renderer` and `_overlay` in the prefab.
3. Save as `Assets/Prefabs/Map/Map.prefab`.

### Step 6 — Scene wiring
1. Remove old `MapLoader` wiring from scene.
2. Add `MapController`; assign `_loader` and `_camera`.
3. Assign prefab + asset refs on `MapLoader`.
4. Save scene.

---

## Out of Scope

- Y-axis wrapping / vertical scroll limits.
- More than two map instances.
- Zoom-to-cursor (zoom stays camera-centered for now).
- Antimeridian mesh splitting.
