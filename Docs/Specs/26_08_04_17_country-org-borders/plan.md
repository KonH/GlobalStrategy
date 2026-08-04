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

1. **Geometric segment-to-neighbor attribution** — for a given province ring segment (edge `i -> i+1`), which other province (if any) shares that edge (exactly or approximately). This is purely a function of static polygon geometry + authoritative `NeighborProvinceIds`, computed **once** when province geometry loads.
2. **Boundary-vs-internal classification** — given a segment's neighbor province (from #1) and the *current* lens + ownership/org-control state, should this segment render a line right now. This must be **re-evaluated** on lens change and on ownership/org-control change, but needs no new geometry work — only owner/org lookups.

Splitting this way lets #1 be a one-time, pure-geometry computation and #2 be a cheap, frequently-re-run, pure-data-lookup computation — both implementable as plain C# with no `UnityEngine` dependency, so both are directly unit-testable (the plan's answer to "no Unity Play/Edit Mode test framework in use" for this kind of code — see **Tests**).

### 1. Geometric segment-to-neighbor attribution — `Core.Map.BorderSegmentIndex` (new)

New file `src/Core.Map/Map/BorderSegmentIndex.cs`, class `GS.Core.Map.BorderSegmentIndex` (pure C#, only depends on the existing `Vector2d`/`Ring`/`Polygon` types already in `Core.Map`):

```csharp
public static class BorderSegmentIndex {
    // ringsByProvinceId: provinceId -> one Vector2d[] per outer ring the province is built from
    // (parallels how MapMeshBuilder.BuildBorderMesh iterates feature.Polygons[i].Rings[0]).
    // neighborProvinceIdsByProvinceId: authoritative adjacency from ProvinceEntry.NeighborProvinceIds /
    // ProvinceTopology (required — exact edge-key matching alone misses ~31% of cross-country pairs
    // after per-country mapshaper simplify in provinces_1880.json).
    // Returns provinceId -> per-ring array of neighbor provinceId per segment ("" = no neighbor / map edge).
    public static Dictionary<string, string[][]> BuildNeighborMap(
        IReadOnlyDictionary<string, IReadOnlyList<Vector2d[]>> ringsByProvinceId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> neighborProvinceIdsByProvinceId,
        double epsilon = 0.01,
        double proximityThreshold = 0.05);
}
```

**Algorithm (two-pass):**

1. **Exact quantized edge-key fast path.** For every ring segment across every province (edge = `ring[i] -> ring[(i+1) % ring.Length]`), quantize both endpoints to an integer grid (`(long)Math.Round(x / epsilon)`, `(long)Math.Round(y / epsilon)`) and build an order-independent edge key (sort the two quantized endpoint pairs so `A->B` and `B->A` collide). Group `(provinceId, ringIndex, segmentIndex)` occurrences by edge key. A key with exactly 2 distinct owning provinces that are listed as neighbors of each other in `neighborProvinceIdsByProvinceId` attributes those segments to each other. A key with 1 (or with non-neighbor co-occupants) leaves those segments unattributed for pass 2. A key with >2 (data anomaly, e.g. a T-junction) picks the first other *listed neighbor* deterministically (`StringComparer.Ordinal`) rather than throwing.

2. **Proximity fallback for remaining segments** (required for cross-country borders). Exact edge-key matching recovers ~99.9% of same-country adjacencies but only ~69% of cross-country `neighborProvinceIds` pairs on real `provinces_1880.json` data, because cross-country provinces are generated/simplified per country and often touch without coincident vertices. For each still-unattributed segment, take the segment midpoint and assign the nearest candidate neighbor from `neighborProvinceIdsByProvinceId[provinceId]` whose own ring has a point-to-segment (or midpoint-to-segment) distance below `proximityThreshold` world units. Segments with no candidate within threshold stay `""` (map/coastline edge).

`epsilon = 0.01` and `proximityThreshold = 0.05` operate on the same **world-space** coordinates used for rendering (see below), i.e. after `CoordinateConverter.Scale = 3`. Tune only if tests against real cross-country fixtures show under/over-attribution; do not loosen enough to attribute a segment across a real gap between non-touching provinces.

**Known limitation (accepted, not solved by this plan):** classification runs on each province's own post-antimeridian-unwrap world vertices (see below), not raw lon/lat. Two neighboring provinces whose rings independently unwrap/re-center differently near ±180° longitude could fail both passes on the affected segment, falling back to "no neighbor" (map edge). This degrades to a missing border line on a rare antimeridian-adjacent edge, not an incorrect one — acceptable given the existing per-province independent unwrap already has this same theoretical edge case for rendering today.

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
  - `Render(...)` gains a first pass, before the existing per-feature GameObject loop: for every feature, for every `polygon` in `feature.Polygons`, call `MapMeshBuilder.ProjectRingVertices(polygon.Rings[0])` and cast each `Vector2` to a `Vector2d` (double), collecting `Dictionary<string, IReadOnlyList<Vector2d[]>> ringsByProvinceId` keyed by `feature.Name` (the provinceId). Also build `Dictionary<string, IReadOnlyList<string>> neighborProvinceIdsByProvinceId` from `provinceConfig` entries' `NeighborProvinceIds`. Call `BorderSegmentIndex.BuildNeighborMap(ringsByProvinceId, neighborProvinceIdsByProvinceId)` once for the whole render pass. Also build and cache `Dictionary<string,string> _countryIdByProvinceId` from `provinceConfig` (needed later so `RebuildCountryOrgBorders` can resolve *any* province's static-seed fallback owner, not just the one currently being visited).
  - In the existing per-feature loop, after `go.AddComponent<ProvinceIdentifier>().SetProvince(...)`, call `identifier.SetSegmentNeighbors(neighborMap.TryGetValue(provinceId, out var n) ? n : Array.Empty<string[]>())`. Also create a second border child (parallel to the existing `_Border` child): `<provinceId>_CountryOrgBorder`, tagged `CountryOrgBorderRendererMarker`, `MeshFilter` initially empty, `MeshRenderer.material = _countryOrgBorderMaterialTemplate`, `enabled = false`.
  - New public method `RebuildCountryOrgBorders(MapLens lens, IReadOnlyDictionary<string,string> ownerByProvinceId, IReadOnlyDictionary<string,string> topOrgIdByCountryId, IReadOnlyCollection<string> visibleProvinceIds)`: for each `go` in `_featureObjects`, resolve `identifier = go.GetComponent<ProvinceIdentifier>()`; find its `_CountryOrgBorder` child's `MeshFilter`/`MeshRenderer`. If `identifier.ProvinceId` is not in `visibleProvinceIds`, disable the renderer and continue (matches existing `inWorld` gating). Otherwise resolve `ownerId` (province's own owner, same fallback rule as `MapLensApplier.ResolveOwner`), build a `bool[][] mask` sized to `identifier.SegmentNeighborProvinceIds`' shape: for each ring `r`, segment `i`, `neighborId = identifier.SegmentNeighborProvinceIds[r][i]`; `mask[r][i] = neighborId != "" && BorderClassifier.ShouldRenderBoundary(ownerId, ResolveOwner(neighborId, ownerByProvinceId), lens, topOrgIdByCountryId)` where `ResolveOwner(pid, dict) => dict.TryGetValue(pid, v) ? v : _countryIdByProvinceId[pid]`. Build the mesh via `MapMeshBuilder.BuildBorderMesh(identifier.Feature.Polygons, _countryOrgBorderWidth, mask)`. **Before assigning**, if `meshFilter.sharedMesh != null` then `Object.Destroy(meshFilter.sharedMesh)` (runtime meshes only — avoids leaking one Mesh per province per `ApplyLens`). Then assign to the child's `MeshFilter.mesh`, enable the renderer (skip/disable and clear mesh if the built mesh is `null`, i.e. no boundary segments on this province this frame).
  - New public method `DisableCountryOrgBorders()`: disable every `_CountryOrgBorder` child renderer (no rebuild) — used for Province/Geographic lenses. Leaving meshes cached while disabled is fine; Destroy on next rebuild still covers memory.

- **`Assets/Scripts/Unity/Map/MapController.cs`**:
  - Expose both wrap instances' province renderers, e.g. `public IEnumerable<ProvinceRenderer> ProvinceRenderers` yielding `_current` and `_forward`'s `ProvinceRenderer` when non-null (keep existing `ActiveProvinceRenderer` unchanged for click/camera callers).

- **`Assets/Scripts/Unity/Map/MapLensApplier.cs`**:
  - `ApplyLens` iterates **every** non-null renderer from `_mapController.ProvinceRenderers` (not only `ActiveProvinceRenderer`). Today's fill/hatch/Province-border loop already only updates `_current`; after an antimeridian wrap swap the newly active map can show stale fills/borders until the next `PropertyChanged`. Applying the same lens pass (fills + Province borders + new country/org borders) to both wrap copies closes that hole for this feature and for the existing fill path in the same change — shared ownership/org lookups, per-renderer mesh/child updates.
  - While iterating each renderer's features, also collect `visibleProvinceIds` (provinceIds where `inWorld` was true) into a `HashSet<string>`, and reuse the already-resolved `ownerId` per province to build `ownerByProvinceIdResolved` (a `Dictionary<string,string>` covering every visited province, pre-resolved through the existing fallback) — both needed by the new step below, and both were already being computed per-province, just not retained. Ownership lookup is shared across both wrap copies; rebuild borders per renderer.
  - After each renderer's feature loop (or once after collecting shared owner state, then per renderer): if `lens == MapLens.Political || lens == MapLens.Org`, build `topOrgIdByCountryId` (new small private helper `Dictionary<string,string> BuildTopOrgLookup()` iterating `_state.OrgMap.Entries`, keyed by `CountryId -> TopOrgId`, skipping empty `TopOrgId`s) and call `provinceRenderer.RebuildCountryOrgBorders(lens, ownerByProvinceIdResolved, topOrgIdByCountryId, visibleProvinceIds)`. Otherwise call `provinceRenderer.DisableCountryOrgBorders()`.
  - Skip calling `RebuildCountryOrgBorders` when `ApplyLens` was triggered only by `HandleProvinceOccupationChanged` (occupation does not affect country/org borders) — still run the existing fill/hatch/Province-border loop for that handler, but call `DisableCountryOrgBorders` / leave country-org borders untouched rather than full mesh rebuild. Simplest clean approach: pass a `bool rebuildCountryOrgBorders` into `ApplyLens` (true from lens/ownership/org/world-countries handlers; false from occupation handler). When false and lens is Political/Org, leave existing `_CountryOrgBorder` meshes as-is (do not Destroy/rebuild); when false and lens is Province/Geographic, still call `DisableCountryOrgBorders()` so they stay off.
  - No change needed to the existing `showBorders`/`SetBorderRenderersEnabled` line — that continues to gate only the original `_Border`/`ProvinceBorderRendererMarker` child for Province lens, untouched by this feature.
  - The existing `HandleProvinceOwnershipChanged`/`HandleOrgMapChanged`/`HandleLensChanged`/`HandleWorldCountriesChanged` handlers already call `ApplyLens` on every relevant change — no new subscriptions needed; the new Political/Org border rebuild happens as part of those `ApplyLens` calls (with `rebuildCountryOrgBorders: true`), satisfying the "border lines update live" acceptance criterion the same way fill colors already do.

### 4. Rebuild-cost note (accepted, not optimized in this pass)

`RebuildCountryOrgBorders` rebuilds one `Mesh` per visible province on every qualifying `ApplyLens` call while Political/Org lens is active (lens switch, ownership change, org-control change, world-countries change) — not per frame, and not on occupation-only changes. The dataset has ~5492 provinces across ~154 countries (not "low hundreds"); the visible subset can still be large once many countries are in-world. Accept full rebuild for this pass with these mitigations already in the plan: (a) Destroy old runtime meshes before assign; (b) skip border rebuild on occupation-only `ApplyLens`. If profiling later shows a hotspot, dirty-province rebuild is a candidate for `/optimize-performance`, not something this plan needs to solve.

### 5. Architecture-doc update

`.claude/rules/unity/map_system.md` currently states province `_Border` children are enabled only in Province lens and that Political/Org/Geographic stay border-free. Update that bullet to: Province lens still uses full-ring `_Border` / `ProvinceBorderRendererMarker`; Political/Org use selective `_CountryOrgBorder` / `CountryOrgBorderRendererMarker` from live ownership/org state; Geographic still has no borders.

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
  - Two adjacent unit-square provinces sharing one edge (reversed point order, as real ring winding would produce) → the shared segment resolves to each other's provinceId on both sides (exact path).
  - A province with one edge on nobody else's ring → that segment resolves to `""` (map edge / no neighbor).
  - Coordinates within `epsilon` but not bit-identical (simulates float round-trip through `CoordinateConverter.ToWorld`) still match on the exact path.
  - Coordinates further apart than `epsilon` do not match on the exact path alone.
  - Two provinces listed as neighbors whose shared boundary endpoints are offset by more than `epsilon` but whose edges pass within `proximityThreshold` → still attribute the segment (guards the cross-country approximate-touch case that exact keys miss).
  - Optional but preferred: a small fixture excerpt (a few real cross-country neighbor pairs from `provinces_1880` + `neighborProvinceIds`) asserting near-complete segment attribution under the documented thresholds — CI must not green-light an exact-only implementation that fails international borders.
  - This is the **first** test coverage for any `Core.Map` class — `src/Game.Tests/Game.Tests.csproj` needs a new `<ProjectReference Include="../Core.Map/Core.Map.csproj" />` (safe: `Game.Main.csproj` already references `Core.Map` today, so no circular-reference risk).

- **`src/Game.Tests/BorderClassifierTests.cs`** (new) — exercises `GS.Game.Systems.BorderClassifier.ShouldRenderBoundary` directly (no Unity, `Game.Systems` already referenced by `Game.Tests.csproj`):
  - Political lens, different owners → `true`; same owner → `false`.
  - Org lens, both owners' countries have a `TopOrgId` and they differ → `true`; same top org → `false`.
  - Org lens, one or both owners' countries missing from `topOrgIdByCountryId` → falls back to owner-id comparison (both the "different owners, no org data" → `true` and "same owner, no org data" → `false` cases).
  - Geographic/Province lens values are not expected to reach this method at all (`MapLensApplier` only calls `RebuildCountryOrgBorders` for Political/Org) — no test case needed for those, since `MapLensApplier`'s own branch, not `BorderClassifier`, is what prevents that call.

No `src/Core.Map`-side or `src/Game.Systems`-side test infrastructure changes beyond adding the two files above and the one `ProjectReference`.

## Section 1 — Agent Steps

- [ ] **Add `BorderSegmentIndex` to Core.Map** — new file `src/Core.Map/Map/BorderSegmentIndex.cs`, implementing `BuildNeighborMap` as described in Approach §1 (exact edge-key fast path + `NeighborProvinceIds`-constrained proximity fallback).
- [ ] **Add `BorderClassifier` to Game.Systems** — new file `src/Game.Systems/BorderClassifier.cs`, implementing `ShouldRenderBoundary` as described in Approach §2.
- [ ] **Add Core.Map project reference to Game.Tests** — edit `src/Game.Tests/Game.Tests.csproj`, add `<ProjectReference Include="../Core.Map/Core.Map.csproj" />`.
- [ ] **Add `BorderSegmentIndexTests`** — new `src/Game.Tests/BorderSegmentIndexTests.cs` per the **Tests** section cases above (including the proximity / near-miss case).
- [ ] **Add `BorderClassifierTests`** — new `src/Game.Tests/BorderClassifierTests.cs` per the **Tests** section cases above.
- [ ] **Run `src` test suite** — via the `dotnet-test` skill, confirm the two new test files (and the full existing suite) pass before moving to Unity-side work.
- [ ] **Rebuild Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release`, confirm `Core.Map.dll` and `Game.Systems.dll` land in `Assets/Plugins/Core/` with updated timestamps.
- [ ] **Expose `MapMeshBuilder.ProjectRingVertices`** — rename the existing private `UnwrapAndProjectRing` to `public static Vector2[] ProjectRingVertices(Ring ring)`, update its one existing call site (`AppendRingMesh`) and the existing `BuildBorderMesh`'s call site to the new name.
- [ ] **Add masked `MapMeshBuilder.BuildBorderMesh` overload** — new `public static Mesh BuildBorderMesh(IReadOnlyList<Polygon> polygons, float width, IReadOnlyList<bool[]> ringSegmentMasks)`, threading an optional `bool[] segmentMask` through `AppendBorderRingMesh` to skip masked-out segments; existing 2-argument overload unchanged.
- [ ] **Add `CountryOrgBorderRendererMarker`** — new `Assets/Scripts/Unity/Map/CountryOrgBorderRendererMarker.cs`, mirrors `ProvinceBorderRendererMarker.cs`.
- [ ] **Extend `ProvinceIdentifier`** — add `SegmentNeighborProvinceIds` (`string[][]`) and `SetSegmentNeighbors(string[][])`.
- [ ] **Extend `ProvinceRenderer`** — add `_countryOrgBorderMaterialTemplate`/`_countryOrgBorderWidth` serialized fields; add the neighbor-map build pass (rings + `NeighborProvinceIds`) and `_countryIdByProvinceId` cache to `Render(...)`; create the `_CountryOrgBorder` child per province; add `RebuildCountryOrgBorders(...)` (with runtime-mesh Destroy before assign) and `DisableCountryOrgBorders()` public methods, per Approach §3.
- [ ] **Extend `MapController`** — expose `ProvinceRenderers` yielding both wrap copies' `ProvinceRenderer`s.
- [ ] **Extend `MapLensApplier`** — apply lens to every `ProvinceRenderers` entry; collect `visibleProvinceIds`/`ownerByProvinceIdResolved`; add `BuildTopOrgLookup()`; branch to call `RebuildCountryOrgBorders(...)` (Political/Org, when `rebuildCountryOrgBorders`) or `DisableCountryOrgBorders()` (Province/Geographic); skip country/org border rebuild on occupation-only triggers, per Approach §3.
- [ ] **Update `.claude/rules/unity/map_system.md`** — replace “country lenses stay border-free” with: Province lens still uses full-ring `_Border` / `ProvinceBorderRendererMarker`; Political/Org use selective `_CountryOrgBorder` / `CountryOrgBorderRendererMarker` from live ownership/org state; Geographic still has no borders.
- [ ] **Smoke test in Play Mode** — blocked on User Step 1 (material + prefab assignment). Once that is done: enter Play mode, switch to Political lens, confirm border lines appear only along different-owner province edges (thicker/distinct color from Province lens's border) and not along same-owner or map-edge segments; switch to Org lens, confirm border lines follow top-org boundaries and fall back to owner comparison where `OrgMap.Entries` has no entry for a side; switch to Province lens, confirm the existing full-ring `_Border` still renders exactly as before and no `_CountryOrgBorder` lines show; switch to Geographic lens, confirm no borders at all; trigger a runtime ownership change (e.g. `DebugChangeProvinceOwnerCommand`) while Political/Org lens is active and confirm border lines update immediately; pan across the antimeridian wrap and confirm both wrap copies show consistent country/org borders; check console for compile/runtime errors. If Unity Editor/MCP is unavailable in the automation host, leave this step unchecked and note it in the handoff.

## Section 2 — User Steps

### 1. Create and assign the country/org border material (required before smoke test)
Create `Assets/Materials/CountryOrgBorderMaterial.mat`: Shader `Universal Render Pipeline/Unlit`, a bold, fully-opaque color distinct from Province lens's translucent near-black `ProvinceBorderMaterial` (e.g. a dark red `RGBA(0.65, 0.10, 0.10, 1.0)`) — the goal is for it to visibly read as a "bigger feature" per the spec, not blend in with existing borders. Assign it to `ProvinceRenderer`'s new `_countryOrgBorderMaterialTemplate` field on the `Map` prefab (`Assets/Prefabs/Map/Map.prefab`), and confirm `_countryOrgBorderWidth` is `1.2f` (or your preferred starting width). Do this before asking the agent to run the Play Mode smoke test.

### 2. Visually tune border width and color
After the agent's Play Mode smoke test, enter Play mode yourself, compare Political/Org lens borders side-by-side against Province lens's borders at default and zoomed-out camera positions, and adjust `_countryOrgBorderWidth` and/or the material color/alpha on the `Map` prefab until the country/org borders clearly read as a distinct, bigger visual feature without overwhelming the map at typical zoom levels.

### 3. Verify antimeridian edge case visually
Pan the map to countries whose provinces sit near ±180° longitude (if any exist in the current province dataset) while Political or Org lens is active, and confirm no obviously wrong/missing border segments appear there. Per the Approach's noted limitation, an occasional missing (not incorrect) border segment at an antimeridian-adjacent province edge is expected and acceptable for this pass; if it's visually disruptive, flag it as a follow-up rather than blocking this feature.

Use the implement skill to start working on the plan or request changes.
