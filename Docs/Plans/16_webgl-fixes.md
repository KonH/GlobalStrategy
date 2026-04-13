# Plan: WebGL Build Fixes

## Goal

Make the game run correctly in WebGL: fix config loading, hide unsupported debug features, and solve the map texture size limit.

---

## Issue 1 — Config loading crashes on WebGL

### Root cause

`StreamingAssetsConfig<T>` uses `File.ReadAllText`, which is not supported in WebGL. WebGL serves streaming assets over HTTP and requires `UnityWebRequest`. The path is also constructed incorrectly in the hosted build (the `DirectoryNotFoundException` in the error log shows the path being mangled).

### Fix

Replace streaming-assets config loading with `TextAsset` references — the same pattern already used for GeoJSON in `MapLoader`. The DI composition root (`GameLifetimeScope`) will hold serialized `TextAsset` fields for each config file and pass them to a new `TextAssetConfig<TConfig>` implementation of `IConfigSource<TConfig>`.

**Steps:**

1. Add `Assets/Scripts/Unity/DI/TextAssetConfig.cs` — a new `IConfigSource<TConfig>` that takes a `TextAsset` and deserializes its `.text` with `JsonConvert.DeserializeObject<T>`.
2. In `GameLifetimeScope`, add five `[SerializeField] TextAsset` fields (one per config file: `_geoJsonConfig`, `_mapEntryConfig`, `_countryConfig`, `_gameSettings`, `_resourceConfig`).
3. Replace the five `new StreamingAssetsConfig<T>(ConfigPath(...))` calls with `new TextAssetConfig<T>(_fieldName)`.
4. In the Unity Editor, assign each TextAsset field in the Inspector to the corresponding JSON file from `Assets/StreamingAssets/Configs/`.
5. Delete `StreamingAssetsConfig.cs` (and its `.meta`) — it is now unused.

> Note: The GeoJSON TextAsset is already wired to `MapLoader._geoJsonAsset`, so it does not need a second reference in `GameLifetimeScope`. The `GameLogicContext` receives the GeoJSON through the config (`GeoJsonConfig`), so that field in the scope is new.

---

## Issue 2 — ECS Viewer button shown in WebGL builds

### Root cause

`HUDDocument.Start` always wires `_btnEcsViewer` click handler. `EcsViewerBridge` already guards its own startup with `#if !UNITY_WEBGL`, so `CurrentUrl` will always be null in WebGL — but the button remains visible.

### Fix

In `HUDDocument.Start`, hide `_btnEcsViewer` at runtime when running on WebGL:

```csharp
#if UNITY_WEBGL
_btnEcsViewer.style.display = DisplayStyle.None;
#endif
```

This uses a compile-time constant so there is zero overhead on non-WebGL platforms and the button is simply absent from the layout on WebGL.

---

## Issue 3 — Map texture too large for WebGL

### Root cause

The source texture `Assets/Map/NE1_LR_LC_SR_W_DR.jpg` is 16200×8100 px. Unity's WebGL platform importer setting caps it at 2048 px (set in the `.meta` file), so the entire world map is downscaled to 2048×1024 — heavily degraded.

### Approach

Split the 16200×8100 image into an 8×4 grid of tiles (each ≤ 2048×2025 px). Each tile is a separate texture asset within Unity's WebGL limit. `MapImageOverlay` is extended to accept a tile grid and assemble them as a grid of quads.

### Steps

**A. Protect the original texture**

Move `Assets/Map/NE1_LR_LC_SR_W_DR.jpg` (and its `.meta`) outside the Unity project to a sibling folder (e.g. `../MapSource/NE1_LR_LC_SR_W_DR.jpg`). This keeps it accessible for re-slicing without being imported by Unity at all.

**B. Create Editor tool `Assets/Scripts/Editor/Map/MapTextureSplitter.cs`**

- Menu item: `Game/Map/Split Map Texture`
- Reads a configurable source path (default `../MapSource/NE1_LR_LC_SR_W_DR.jpg`) or prompts a file picker
- Splits into an 8-column × 4-row grid → 32 PNG tiles, output to `Assets/Map/Tiles/tile_r{row}_c{col}.png`
- After writing all files, calls `AssetDatabase.Refresh()`
- Sets each tile's TextureImporter: `textureType = Default`, `isReadable = false`, `maxTextureSize = 2048`, no mip maps, `filterMode = Bilinear`, `npotScale = None`
- After import, finds `Map.prefab` and calls a second helper to assign tiles to `MapImageOverlay` (see C)

**C. Extend `MapImageOverlay`**

Replace the single `Texture2D` setup method with a tile-grid setup:

```csharp
// New signature
public void Setup(Texture2D[] tiles, int cols, int rows)
```

- Creates one child `GameObject` with `MeshFilter` + `MeshRenderer` per tile
- Each quad is sized `(MapWidth/cols) × (MapHeight/rows)` and placed at the correct offset
- Each uses `Unlit/Texture` material with the corresponding tile texture
- The existing `Setup(Texture2D texture)` overload is kept for Editor/non-WebGL convenience (single full-res texture → 1×1 grid)

**D. Update `MapLoader`**

- Replace `[SerializeField] Texture2D _mapTexture` with `[SerializeField] Texture2D[] _mapTiles` and `[SerializeField] int _tileCols`, `[SerializeField] int _tileRows`
- Update `Map.Initialize` signature and call accordingly
- Keep backward-compat: if `_mapTiles` length == 1, treat as single full-texture case

**E. Wire in Editor**

The Editor splitter (step B) assigns the 32 tiles into `MapLoader._mapTiles` in the scene/prefab and sets `_tileCols=8`, `_tileRows=4`. This keeps wiring automated so it does not need to be done by hand.

---

## Shader null error

The `ArgumentNullException: shader` in `MapRenderer.Awake` (`Shader.Find("Sprites/Default")`) occurs because that shader is not included in the WebGL build's shader variant collection. Fix: change the shader name to `"Unlit/Color"` (guaranteed to be included in all builds), which provides the same solid-colour rendering.

---

## File summary

| File | Action |
|---|---|
| `Assets/Scripts/Unity/DI/TextAssetConfig.cs` | New — TextAsset-based config loader |
| `Assets/Scripts/Unity/DI/StreamingAssetsConfig.cs` | Delete |
| `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` | Add 5× TextAsset fields, swap config ctor calls |
| `Assets/Scripts/Unity/UI/HUDDocument.cs` | Hide ECS viewer button under `#if UNITY_WEBGL` |
| `Assets/Scripts/Unity/Map/MapImageOverlay.cs` | Add tile-grid Setup overload |
| `Assets/Scripts/Unity/Map/MapLoader.cs` | Use tile array; update Map.Initialize call |
| `Assets/Scripts/Unity/Map/Map.cs` | Update Initialize signature to pass tiles |
| `Assets/Scripts/Unity/Map/MapRenderer.cs` | Fix shader name to `"Unlit/Color"` |
| `Assets/Scripts/Editor/Map/MapTextureSplitter.cs` | New — Editor menu item to split texture |
| `Assets/Scripts/Editor/Map/GS.Editor.Map.asmdef` | New — Editor-only asmdef |
| `../MapSource/NE1_LR_LC_SR_W_DR.jpg` | Move from `Assets/Map/` |
| `Assets/Map/NE1_LR_LC_SR_W_DR.jpg` | Delete after move |
| `Assets/Map/Tiles/tile_r*_c*.png` | Generated by the splitter tool |

Use /implement to start working on the plan or request changes.
