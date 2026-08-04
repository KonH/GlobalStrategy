# Spec: Country/Org Borders in Political and Org Lenses

## Feature Intent

As a player viewing the map in Political or Org lens, I want to see border lines only along the actual boundaries between countries (Political lens) or between org-controlled territories (Org lens), so that I can read the map's political/organizational structure at a glance without visual noise from every internal province boundary.

## Acceptance Criteria

- Player switches to Political lens
  - Two adjacent provinces belong to different countries (different runtime owners) => a border line renders along the shared edge between them
  - Two adjacent provinces belong to the same country (same runtime owner) => no border line renders along the shared edge between them
  - A province sits at the edge of the playable map with no neighbor on one side => no border line renders along that unmatched edge (unchanged from today: no coastline/map-edge borders are added by this feature)

- Player switches to Org lens
  - Two adjacent provinces' owning countries are controlled by different top orgs => a border line renders along the shared edge between them
  - Two adjacent provinces' owning countries are controlled by the same top org => no border line renders along the shared edge between them

- Player switches to Province lens
  - Existing behavior is unchanged: every province still renders its own full outline, including edges shared with same-country/same-org neighbors

- Player switches to Geographic lens
  - Existing behavior is unchanged: no borders render

- Player changes a province's owner or an org's control of a country while a Political or Org lens is active (e.g. via a war outcome or debug command)
  - The border lines update to reflect the new ownership/control state, consistent with how fill colors already update on such changes

## Tech Notes

- Political lens border filtering:
  - `Assets/Scripts/Unity/Map/MapLensApplier.cs`: currently `bool showBorders = lens == MapLens.Province;` gates all border rendering off for Political/Org. This must change to render Political/Org borders selectively rather than as an all-or-nothing per-lens toggle.
  - Owner comparison source: `VisualState.ProvinceOwnership.OwnerByProvinceId[provinceId]`, falling back to `ProvinceIdentifier.CountryId` only when absent (per existing convention documented in `.claude/rules/unity/map_system.md`).

- Org lens border filtering:
  - Org comparison is at country granularity, not per-province: look up each province's owning country's `TopOrgId` via `VisualState.OrgMap.Entries` (`src/Game.Main/VisualStateConverter.cs::UpdateOrgMap` populates this), matching the existing `MapLensApplier.GetOrgColor` lookup pattern.

- Province/Geographic lens unchanged:
  - `ProvinceRenderer.cs`'s existing per-province `_Border` mesh (built via `MapMeshBuilder.BuildBorderMesh(feature.Polygons, _borderWidth)`, tagged `ProvinceBorderRendererMarker`) continues to render in full for Province lens exactly as today; Geographic lens continues to render no borders.

- Core technical gap to resolve (segment-level boundary detection):
  - `NeighborProvinceIds` (`src/Game.Configs/ProvinceConfig.cs`) and `src/Game.Systems/ProvinceTopology.cs` only expose province-*pair* adjacency (which provinces touch), not per-*segment* geometry (which vertex-pair segment of a province's ring is the shared edge with which specific neighbor).
  - `MapMeshBuilder.BuildBorderMesh` currently walks each polygon ring as one undifferentiated sequence of segments and emits a quad per segment uniformly. Rendering only country/org boundary segments requires attributing each ring segment to the specific neighboring province (or "no neighbor") it borders, then classifying that segment as "boundary" (different owner/org) or "internal" (same owner/org) per lens, per current ownership/control state.
  - This segment-to-neighbor attribution does not exist anywhere in the codebase today and is the central new technical problem this feature introduces.

- Data pipeline vs. runtime decision (flagged for `/plan`, not decided here):
  - Unresolved whether segment-to-neighbor attribution should be precomputed offline (e.g. extending `scripts/utils/generate_provinces.py` to bake segment/neighbor metadata into `provinces_1880.json` or `province_config.json`, alongside the existing `NeighborProvinceIds` generation described in `.claude/rules/unity/province_config_generator.md`) or computed/classified at runtime in `MapMeshBuilder`/`ProvinceRenderer`/`MapLensApplier` from existing polygon + adjacency data.
  - Ownership and org control change at runtime (province owner via gameplay/debug commands, org control via `ControlEffect` accumulation), so whatever segment/neighbor *mapping* is precomputed, the boundary/internal *classification* per segment must still be re-evaluated at runtime (on lens change and on ownership/control change) — precomputation, if used, only needs to cover the static geometry-to-neighbor mapping, not the classification itself.

- Visual styling:
  - Default assumption: reuse `Assets/Materials/ProvinceBorderMaterial.mat` and the existing `_borderWidth` value used by `MapMeshBuilder.BuildBorderMesh`, for consistency with Province lens borders, unless product direction calls for a visually heavier line to read as a "bigger feature" per the issue text.

- Double-rendering of shared boundary segments:
  - Per the Province-lens precedent (`Docs/Specs/26_07_11_09_province-map-lens/spec.md`), double-rendering a shared edge (once from each adjacent province's ring) was explicitly accepted as non-defective. Default assumption here is to keep that precedent for country/org borders too, since the segments being drawn are a strict subset of what Province lens already draws twice. Flagged as an ambiguity below given the research note that isolated boundary lines (vs. full per-province outlines) may make double-rendering more visually noticeable (e.g. z-fighting or double-width appearance at scale).

## Out of Scope

- Changing Province lens's full-ring-per-province border behavior.
- Changing Geographic lens's no-border behavior.
- Distinct border styling per adjacency type beyond what's decided for this feature (e.g. no further differentiation between "country border" and "org border" styling unless specified).
- Map-edge/coastline border lines for provinces with no neighbor on one side.
- New UI (lens switcher, legends, etc.) — this is a rendering-only change to lenses that already exist.

## Ambiguities

- [NEEDS CLARIFICATION: Segment-to-neighbor attribution approach — should the mapping from each province ring segment to its bordering neighbor (or "none") be precomputed in the config-generation pipeline (`scripts/utils/generate_provinces.py` → `provinces_1880.json`/`province_config.json`), or derived/classified purely at runtime from existing polygon and `NeighborProvinceIds` data? This is the hardest open technical question and materially changes the plan's shape (config-gen script changes + regenerated config assets vs. Unity-side-only code changes).]
- [NEEDS CLARIFICATION: For Org lens, how should a country with no controlling org (no `TopOrgId` entry in `VisualState.OrgMap.Entries`) be treated for border purposes against a neighbor that also has no controlling org (border or no border), and against a neighbor that does have a controlling org (border or no border)?]
- [NEEDS CLARIFICATION: Should double-rendering of a shared country/org boundary segment (drawn once from each adjacent province's ring) be accepted as in the Province-lens precedent, or does showing only isolated boundary lines (rather than every province's full outline) make double-rendering visually objectionable enough to require deduplication in this feature?]
- [NEEDS CLARIFICATION: Should the new country/org border lines reuse `ProvinceBorderMaterial`/current border width unchanged, or use different (e.g. thicker/differently colored) styling to visually read as a "bigger feature" as the issue's wording suggests?]
