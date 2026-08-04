# Plan: Country/Org Borders in Political and Org Lenses

## Spec

Today, Political and Org lenses render no borders at all (`MapLensApplier.ApplyLens`: `bool showBorders = lens == MapLens.Province;`), while Province lens renders every province's full ring outline via `ProvinceRenderer`'s per-province `_Border` child mesh (`MapMeshBuilder.BuildBorderMesh(feature.Polygons, _borderWidth)`, tagged `ProvinceBorderRendererMarker`).

This feature adds selective country/org boundary lines to Political and Org lenses:

- **Political lens**: a border line renders along a province-pair's shared edge only when the two provinces' current runtime owners differ (`VisualState.ProvinceOwnership.OwnerByProvinceId`, falling back to `ProvinceIdentifier.CountryId` only when a province is absent from that map). Same-owner shared edges render no line. A province edge with no neighbor (map/coastline edge) renders no line either (unchanged — no new coastline borders).
- **Org lens**: same idea, but compared at the owning-country's top-org granularity, via `VisualState.OrgMap.Entries` (`CountryId -> TopOrgId`, populated by `VisualStateConverter.UpdateOrgMap`). If either side's owning country has no `TopOrgId` entry (no controlling org), that edge falls back to the Political-lens comparison (owner-id difference) instead of being suppressed or forced.
- **Province lens**: unchanged — every province still renders its full existing `_Border` ring outline, including internal same-owner/same-org edges.
- **Geographic lens**: unchanged — no borders.
- Runtime ownership/org-control changes (war outcome, debug command, `ControlEffect` accumulation) while Political/Org lens is active must update the border lines live, the same way fill colors already update via `MapLensApplier`'s existing `PropertyChanged` subscriptions.
- Double-rendering a shared edge (once from each adjacent province's own ring) is accepted, matching the Province-lens precedent — no deduplication.
- The new country/org border lines must look visually distinct/bigger than Province lens's `_Border` (`ProvinceBorderMaterial`, `_borderWidth = 0.5f`, near-black translucent) — not the same material/width reused unchanged.

The central technical gap: `NeighborProvinceIds` (`ProvinceEntry`) and `ProvinceTopology` only know province-*pair* adjacency, not which specific ring *segment* of a province borders which specific neighbor. `MapMeshBuilder.BuildBorderMesh` currently walks a ring as one undifferentiated sequence of segments. This segment-to-neighbor attribution does not exist today and must be derived at Unity runtime (not precomputed in `scripts/utils/generate_provinces.py`), because province ownership is mutable runtime state.

## Goal

Let players read country/org political structure from the map at a glance in Political/Org lens via boundary-only border lines, computed from live ownership/control state, without touching the province data-generation pipeline or Province/Geographic lens behavior.

## Approach

### Why this is two separable problems

1. **Geometric segment-to-neighbor attribution** — for a given province ring segment (edge `i -> i+1`), which other province (if any) shares that exact edge. This is purely a function of static polygon geometry (`provinces_1880.json`), computed **once** when province geometry loads.
2. **Boundary-vs-internal classification** — given a segment's neighbor province (from #1) and the *current* lens + ownership/org-control state, should this segment render a line right now. This must be **re-evaluated** on lens change and on ownership/org-control change, but needs no new geometry work — only owner/org lookups.

Splitting this way lets #1 be a one-time, pure-geometry computation and #2 be a cheap, frequently-re-run, pure-data-lookup computation — both implementable as plain C# with no `UnityEngine` dependency, so both are directly unit-testable (the plan's answer to "no Unity Play/Edit Mode test framework in use" for this kind of code — see **Tests**).

### 1. Geometric segment-to-neighbor attribution — `Core.Map.BorderSegmentIndex` (new)

New file `src/Core.Map/Map/BorderSegmentIndex.cs`, class `GS.Core.Map.BorderSegmentIndex` (pure C#, only depends on the existing `Vector2d`/`Ring`/`Polygon` types already in `Core.Map`):

```csharp
public static class BorderSegmentIndex {
    // ringsByProvinceId: provinceId -> one Vector2d[] per outer ring the province is built from
    // (parallels how MapMeshBuilder.BuildBorderMesh iterates feature.Polygons[i].Rings[0]).
    // Returns provinceId -> per-ring array of neighbor provinceId per segment ("" = no neighbor / map edge).
    public static Dictionary<string, string[][]> BuildNeighborMap(
        IReadOnlyDictionary<string, IReadOnlyList<Vector2d[]>> ringsByProvinceId,
        double epsilon = 0.01);
}
```

Algorithm: for every ring segment across every province (edge = `ring[i] -> ring[(i+1) % ring.Length]`), quantize both endpoints to an integer grid (`(long)Math.Round(x / epsilon)`, `(long)Math.Round(y / epsilon)`) and build an order-independent edge key (sort the two quantized endpoint pairs so `A->B` and `B->A` collide). Group `(provinceId, ringIndex, segmentIndex)` occurrences by edge key. A key with exactly 2 distinct owning provinces means those two provinces are neighbors along that segment; a key with 1 means a map/coastline edge (no neighbor — mirrors the existing "no coastline borders" acceptance criterion for Political/Org lens). A key with >2 (data anomaly, e.g. a T-junction) picks the first other province deterministically (sorted `StringComparer.Ordinal`) rather than throwing, since this is a rendering concern, not a data-integrity guarantee to fail fast on.

`epsilon = 0.01` operates on the same **world-space** coordinates used for rendering (see below), i.e. after `CoordinateConverter.Scale = 3`, so `0.01` world units ≈ `0.0033°` — comfortably above float round-trip error, comfortably below real vertex spacing after `generate_provinces.py`'s 10% mapshaper simplification.

**Known limitation (accepted, not solved by this plan):** classification runs on each province's own post-antimeridian-unwrap world vertices (see below), not raw lon/lat. Two neighboring provinces whose rings independently unwrap/re-center differently near ±180° longitude could fail to match on the affected segment, falling back to "no neighbor" (map edge) for that one segment. This degrades to a missing border line on a rare antimeridian-adjacent edge, not an incorrect one — acceptable given the existing per-province independent unwrap already has this same theoretical edge case for rendering today.

### 2. Boundary-vs-internal classification — `Game.Systems.BorderClassifier` (new)

New file `src/Game.Systems/BorderClassifier.cs`, class `GS.Game.Systems.BorderClassifier` (pure C#, only depends on `GS.Game.Common.MapLens`, already referenced by `Game.Systems.csproj`):

```csharp
public static class BorderClassifier {
    public static bool ShouldRenderBoundary(
        string ownerIdA, string ownerIdB, MapLens lens,
        IReadOnlyDictionary<string, string> topOrgIdByCountryId) {
        if (lens == MapLens.Org) {
            string orgA = topOrgIdByCountryId.TryGetValue(ownerIdA, out var a) ? a : "";
            string orgB = topOrgIdByCountryId.TryGetValue(ownerIdB, out var b) ? b : "";
            if (orgA != "" && orgB != "") {
                return orgA != orgB;
            }
            // fall through to Political-style comparison when either side's org is unknown
        }
        return ownerIdA != ownerIdB;
    }
}
```

This directly encodes the spec's resolved Org-lens fallback rule. `Game.Systems` already has no dependency on `Game.Main` (confirmed via `Game.Systems.csproj`), so this introduces no circular reference; `Game.Systems.dll` is already built to `Assets/Plugins/Core/` and already auto-referenced by Unity assemblies (no `precompiledReferences`/asmdef change needed — `MapLensApplier.cs` already consumes other plugin-DLL namespaces the same way, e.g. `GS.Game.Configs`).

### 3. Unity-side wiring

- **`Assets/Scripts/Unity/Map/MapMeshBuilder.cs`**:
  - Rename/expose the existing private `UnwrapAndProjectRing(Ring) -> Vector2[]` as `public static Vector2[] ProjectRingVertices(Ring ring)` (same body — this is the method that already does antimeridian unwrap + re-center + subsample + `CoordinateConverter.ToWorld` projection). `ProvinceRenderer` needs to call this directly to build the classification input.
  - Add an overload `public static Mesh BuildBorderMesh(IReadOnlyList<Polygon> polygons, float width, IReadOnlyList<bool[]> ringSegmentMasks)`. Internally the same as the existing `BuildBorderMesh(polygons, width)`, except `AppendBorderRingMesh` gains an optional `bool[] segmentMask` parameter — when segment `i`'s mask entry is `false`, that segment's quad is skipped (no vertices/triangles emitted for it). The existing 2-argument `BuildBorderMesh` is unchanged (still used by Province lens's full-ring `_Border` mesh).

- **`Assets/Scripts/Unity/Map/ProvinceIdentifier.cs`**: add `internal string[][] SegmentNeighborProvinceIds { get; private set; }` and `internal void SetSegmentNeighbors(string[][] neighbors)`, set once at build time (parallels the existing `SetProvince` pattern).

- **`Assets/Scripts/Unity/Map/CountryOrgBorderRendererMarker.cs`** (new, mirrors `ProvinceBorderRendererMarker.cs`): `[DisallowMultipleComponent] public class CountryOrgBorderRendererMarker : MonoBehaviour { }`.

- **`Assets/Scripts/Unity/Map/ProvinceRenderer.cs`**:
  - New serialized fields: `[SerializeField] Material _countryOrgBorderMaterialTemplate;` and `[SerializeField] float _countryOrgBorderWidth = 1.2f; // ~2.4x Province lens's 0.5f, per spec's "bigger feature" requirement`.
  - `Render(...)` gains a first pass, before the existing per-feature GameObject loop: for every feature, for every `polygon` in `feature.Polygons`, call `MapMeshBuilder.ProjectRingVertices(polygon.Rings[0])` and cast each `Vector2` to a `Vector2d` (double), collecting `Dictionary<string, IReadOnlyList<Vector2d[]>> ringsByProvinceId` keyed by `feature.Name` (the provinceId). Call `BorderSegmentIndex.BuildNeighborMap(ringsByProvinceId)` once for the whole render pass. Also build and cache `Dictionary<string,string> _countryIdByProvinceId` from `provinceConfig` (needed later so `RebuildCountryOrgBorders` can resolve *any* province's static-seed fallback owner, not just the one currently being visited).
  - In the existing per-feature loop, after `go.AddComponent<ProvinceIdentifier>().SetProvince(...)`, call `identifier.SetSegmentNeighbors(neighborMap.TryGetValue(provinceId, out var n) ? n : Array.Empty<string[]>())`. Also create a second border child (parallel to the existing `_Border` child): `<provinceId>_CountryOrgBorder`, tagged `CountryOrgBorderRendererMarker`, `MeshFilter` initially empty, `MeshRenderer.material = _countryOrgBorderMaterialTemplate`, `enabled = false`.
  - New public method `RebuildCountryOrgBorders(MapLens lens, IReadOnlyDictionary<string,string> ownerByProvinceId, IReadOnlyDictionary<string,string> topOrgIdByCountryId, IReadOnlyCollection<string> visibleProvinceIds)`: for each `go` in `_featureObjects`, resolve `identifier = go.GetComponent<ProvinceIdentifier>()`; find its `_CountryOrgBorder` child's `MeshFilter`/`MeshRenderer`. If `identifier.ProvinceId` is not in `visibleProvinceIds`, disable the renderer and continue (matches existing `inWorld` gating). Otherwise resolve `ownerId` (province's own owner, same fallback rule as `MapLensApplier.ResolveOwner`), build a `bool[][] mask` sized to `identifier.SegmentNeighborProvinceIds`' shape: for each ring `r`, segment `i`, `neighborId = identifier.SegmentNeighborProvinceIds[r][i]`; `mask[r][i] = neighborId != "" && BorderClassifier.ShouldRenderBoundary(ownerId, ResolveOwner(neighborId, ownerByProvinceId), lens, topOrgIdByCountryId)` where `ResolveOwner(pid, dict) => dict.TryGetValue(pid, v) ? v : _countryIdByProvinceId[pid]`. Build the mesh via `MapMeshBuilder.BuildBorderMesh(identifier.Feature.Polygons, _countryOrgBorderWidth, mask)`, assign to the child's `MeshFilter.mesh`, enable the renderer (skip/disable if the built mesh is `null`, i.e. no boundary segments on this province this frame).
  - New public method `DisableCountryOrgBorders()`: disable every `_CountryOrgBorder` child renderer (no rebuild) — used for Province/Geographic lenses.

- **`Assets/Scripts/Unity/Map/MapLensApplier.cs`**:
  - Existing per-province loop is unchanged in structure; while iterating, also collect `visibleProvinceIds` (provinceIds where `inWorld` was true) into a `HashSet<string>`, and reuse the already-resolved `ownerId` per province to build `ownerByProvinceIdResolved` (a `Dictionary<string,string>` covering every visited province, pre-resolved through the existing fallback) — both needed by the new step below, and both were already being computed per-province, just not retained.
  - After the loop, branch on lens: if `lens == MapLens.Political || lens == MapLens.Org`, build `topOrgIdByCountryId` (new small private helper `Dictionary<string,string> BuildTopOrgLookup()` iterating `_state.OrgMap.Entries`, keyed by `CountryId -> TopOrgId`, skipping empty `TopOrgId`s) and call `provinceRenderer.RebuildCountryOrgBorders(lens, ownerByProvinceIdResolved, topOrgIdByCountryId, visibleProvinceIds)`. Otherwise call `provinceRenderer.DisableCountryOrgBorders()`.
  - No change needed to the existing `showBorders`/`SetBorderRenderersEnabled` line — that continues to gate only the original `_Border`/`ProvinceBorderRendererMarker` child for Province lens, untouched by this feature.
  - The existing `HandleProvinceOwnershipChanged`/`HandleOrgMapChanged`/`HandleLensChanged` handlers already call `ApplyLens(_state.MapLens.Lens)` on every relevant change — no new subscriptions needed; the new Political/Org border rebuild happens automatically as part of every `ApplyLens` call, satisfying the "border lines update live" acceptance criterion the same way fill colors already do.

### 4. Rebuild-cost note (accepted, not optimized in this pass)

`RebuildCountryOrgBorders` rebuilds one `Mesh` per visible province on every `ApplyLens` call while Political/Org lens is active (lens switch, ownership change, org-control change) — not per frame. Given province counts are in the low hundreds and this mirrors the existing pattern of recomputing fill colors for every province on every such event, this is accepted as-is; if it becomes a measured hotspot, it is a candidate for the project's `/optimize-performance` carve-out later, not something this plan needs to solve.

## Constitution Check

No conflicts found — plan aligns with all principles.

- **Rendering**: `CountryOrgBorderMaterial` is a new URP Unlit material (Shader: `Universal Render Pipeline/Unlit`), no Built-in RP shaders introduced — matches the existing `ProvinceBorderMaterial` pattern.
- **Game Logic**: `BorderSegmentIndex` (Core.Map) and `BorderClassifier` (Game.Systems) are pure geometry/lookup helper functions with no mutable game state and no ECS involvement — they read already-resolved ownership/org data and derive a rendering decision, the same category as the existing `ProvinceTopology` (adjacency queries) and `GeoJsonParser` (geometry parsing) classes that already live in `src/` as plain C# utilities without being ECS systems. `ProvinceRenderer`/`MapLensApplier` remain presentation/input glue reading `VisualState`, unchanged in kind from their current role.
- **Dependency Injection**: no new singletons, no `FindObjectOfType`, no static mutable state. New `Material`/`float` fields on `ProvinceRenderer` are `[SerializeField]` inspector data, consistent with existing fields (`_materialTemplate`, `_borderMaterialTemplate`, `_borderWidth`).
- **UI**: no UI Toolkit/Canvas/UGUI changes — this is a map-rendering-only feature, no new UI surface.
- **Planning Discipline**: this document is the required plan before implementation begins.
- **Specification Discipline**: `Docs/Specs/26_08_04_17_country-org-borders/spec.md` already exists and is this plan's source.
- **File Organisation**: plan saved at `Docs/Specs/26_08_04_17_country-org-borders/plan.md`, matching convention.
- **Assembly Structure**: no new `.asmdef` files. New Unity files (`CountryOrgBorderRendererMarker.cs`) live in the existing `Assets/Scripts/Unity/Map/` folder already covered by `GS.Unity.Map.asmdef`. New `src/` files stay inside their existing `Core.Map`/`Game.Systems` projects and reach Unity via the already-established precompiled-DLL Plugin mechanism (`.claude/rules/unity/plugins.md`) — no asmdef edits required, matching how `GeoJsonParser`/`ProvinceTopology` are already consumed today.
- **C# Code Style**: all new/edited code uses tabs, `_`-prefixed private members, always-braces control flow, no redundant access modifiers, consistent with the surrounding files.

## Tests

This project has no Unity Play Mode/Edit Mode C# test framework in active use for presentation-layer code (confirmed by the `26_07_11_09_province-map-lens` plan's precedent) — `ProvinceRenderer`, `MapLensApplier`, and `MapMeshBuilder` changes remain unverified by automated tests, same as today, and are covered instead by the Play Mode smoke-test step below.

However, this plan deliberately extracts the two pieces of new *logic* (as opposed to Unity mesh/GameObject plumbing) into pure C# classes in `src/`, specifically so they can be unit-tested without Unity:

- **`src/Game.Tests/BorderSegmentIndexTests.cs`** (new) — exercises `GS.Core.Map.BorderSegmentIndex.BuildNeighborMap` directly with small synthetic `Vector2d[]` rings (no Unity, no GeoJSON parsing needed):
  - Two adjacent unit-square provinces sharing one edge (reversed point order, as real ring winding would produce) → the shared segment resolves to each other's provinceId on both sides.
  - A province with one edge on nobody else's ring → that segment resolves to `""` (map edge / no neighbor).
  - Coordinates within `epsilon` but not bit-identical (simulates float round-trip through `CoordinateConverter.ToWorld`) still match.
  - Coordinates further apart than `epsilon` do not match (regression guard against overly-loose quantization).
  - This is the **first** test coverage for any `Core.Map` class — `src/Game.Tests/Game.Tests.csproj` needs a new `<ProjectReference Include="../Core.Map/Core.Map.csproj" />` (safe: `Game.Main.csproj` already references `Core.Map` today, so no circular-reference risk).

- **`src/Game.Tests/BorderClassifierTests.cs`** (new) — exercises `GS.Game.Systems.BorderClassifier.ShouldRenderBoundary` directly (no Unity, `Game.Systems` already referenced by `Game.Tests.csproj`):
  - Political lens, different owners → `true`; same owner → `false`.
  - Org lens, both owners' countries have a `TopOrgId` and they differ → `true`; same top org → `false`.
  - Org lens, one or both owners' countries missing from `topOrgIdByCountryId` → falls back to owner-id comparison (both the "different owners, no org data" → `true` and "same owner, no org data" → `false` cases).
  - Geographic/Province lens values are not expected to reach this method at all (`MapLensApplier` only calls `RebuildCountryOrgBorders` for Political/Org) — no test case needed for those, since `MapLensApplier`'s own branch, not `BorderClassifier`, is what prevents that call.

No `src/Core.Map`-side or `src/Game.Systems`-side test infrastructure changes beyond adding the two files above and the one `ProjectReference`.

## Section 1 — Agent Steps

- [ ] **Add `BorderSegmentIndex` to Core.Map** — new file `src/Core.Map/Map/BorderSegmentIndex.cs`, implementing `BuildNeighborMap` as described in Approach §1 (grid-quantized, order-independent edge-key matching over `Vector2d[]` rings).
- [ ] **Add `BorderClassifier` to Game.Systems** — new file `src/Game.Systems/BorderClassifier.cs`, implementing `ShouldRenderBoundary` as described in Approach §2.
- [ ] **Add Core.Map project reference to Game.Tests** — edit `src/Game.Tests/Game.Tests.csproj`, add `<ProjectReference Include="../Core.Map/Core.Map.csproj" />`.
- [ ] **Add `BorderSegmentIndexTests`** — new `src/Game.Tests/BorderSegmentIndexTests.cs` per the **Tests** section cases above.
- [ ] **Add `BorderClassifierTests`** — new `src/Game.Tests/BorderClassifierTests.cs` per the **Tests** section cases above.
- [ ] **Run `src` test suite** — via the `dotnet-test` skill, confirm the two new test files (and the full existing suite) pass before moving to Unity-side work.
- [ ] **Rebuild Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release`, confirm `Core.Map.dll` and `Game.Systems.dll` land in `Assets/Plugins/Core/` with updated timestamps.
- [ ] **Expose `MapMeshBuilder.ProjectRingVertices`** — rename the existing private `UnwrapAndProjectRing` to `public static Vector2[] ProjectRingVertices(Ring ring)`, update its one existing call site (`AppendRingMesh`) and the existing `BuildBorderMesh`'s call site to the new name.
- [ ] **Add masked `MapMeshBuilder.BuildBorderMesh` overload** — new `public static Mesh BuildBorderMesh(IReadOnlyList<Polygon> polygons, float width, IReadOnlyList<bool[]> ringSegmentMasks)`, threading an optional `bool[] segmentMask` through `AppendBorderRingMesh` to skip masked-out segments; existing 2-argument overload unchanged.
- [ ] **Add `CountryOrgBorderRendererMarker`** — new `Assets/Scripts/Unity/Map/CountryOrgBorderRendererMarker.cs`, mirrors `ProvinceBorderRendererMarker.cs`.
- [ ] **Extend `ProvinceIdentifier`** — add `SegmentNeighborProvinceIds` (`string[][]`) and `SetSegmentNeighbors(string[][])`.
- [ ] **Extend `ProvinceRenderer`** — add `_countryOrgBorderMaterialTemplate`/`_countryOrgBorderWidth` serialized fields; add the neighbor-map build pass and `_countryIdByProvinceId` cache to `Render(...)`; create the `_CountryOrgBorder` child per province; add `RebuildCountryOrgBorders(...)` and `DisableCountryOrgBorders()` public methods, per Approach §3.
- [ ] **Extend `MapLensApplier`** — collect `visibleProvinceIds`/`ownerByProvinceIdResolved` in the existing per-province loop; add `BuildTopOrgLookup()`; branch after the loop to call `RebuildCountryOrgBorders(...)` (Political/Org) or `DisableCountryOrgBorders()` (Province/Geographic), per Approach §3.
- [ ] **Smoke test in Play Mode** — enter Play mode, switch to Political lens, confirm border lines appear only along different-owner province edges (thicker/distinct color from Province lens's border) and not along same-owner or map-edge segments; switch to Org lens, confirm border lines follow top-org boundaries and fall back to owner comparison where `OrgMap.Entries` has no entry for a side; switch to Province lens, confirm the existing full-ring `_Border` still renders exactly as before and no `_CountryOrgBorder` lines show; switch to Geographic lens, confirm no borders at all; trigger a runtime ownership change (e.g. `DebugChangeProvinceOwnerCommand`) while Political/Org lens is active and confirm border lines update immediately; check `read_console(types=["error"])` for compile/runtime errors.

## Section 2 — User Steps

### 1. Create the country/org border material
Create `Assets/Materials/CountryOrgBorderMaterial.mat`: Shader `Universal Render Pipeline/Unlit`, a bold, fully-opaque color distinct from Province lens's translucent near-black `ProvinceBorderMaterial` (e.g. a dark red `RGBA(0.65, 0.10, 0.10, 1.0)`) — the goal is for it to visibly read as a "bigger feature" per the spec, not blend in with existing borders. Assign it to `ProvinceRenderer`'s new `_countryOrgBorderMaterialTemplate` field on the `Map` prefab (`Assets/Prefabs/Map/Map.prefab`).

### 2. Visually tune border width and color
`_countryOrgBorderWidth` starts at `1.2f` (roughly 2.4x Province lens's `0.5f`) as a first guess matching the existing world-scale (`CoordinateConverter.Scale = 3`). After the agent's Play Mode smoke test, enter Play mode yourself, compare Political/Org lens borders side-by-side against Province lens's borders at default and zoomed-out camera positions, and adjust `_countryOrgBorderWidth` and/or the material color/alpha on the `Map` prefab until the country/org borders clearly read as a distinct, bigger visual feature without overwhelming the map at typical zoom levels.

### 3. Verify antimeridian edge case visually
Pan the map to countries whose provinces sit near ±180° longitude (if any exist in the current province dataset) while Political or Org lens is active, and confirm no obviously wrong/missing border segments appear there. Per the Approach's noted limitation, an occasional missing (not incorrect) border segment at an antimeridian-adjacent province edge is expected and acceptable for this pass; if it's visually disruptive, flag it as a follow-up rather than blocking this feature.

Use the implement skill to start working on the plan or request changes.
