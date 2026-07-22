# Spec: Province Info Panel

## Feature Intent

As a player exploring the map in the Province lens, I want a selected-province info panel that shows the province's name, its owning country (and, when occupied, the occupier alongside it), and the province's resources, so that I can inspect province-level detail the same way I can already inspect country-level detail via the existing selected-country panel — closing the `// TODO` left by `Docs/Specs/26_07_11_09_province-map-lens/spec.md` for where province-selection UI will eventually hook in.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- Panel visibility
  - Player clicks a province while the Province lens is active => the province info panel opens, showing that province
  - No province is selected, or the selection is cleared (e.g. clicking open water) => the panel is hidden
  - Player switches the active map lens away from Province => the panel is hidden (the selection itself is kept, only the panel hides)

- Panel content, while visible
  - Renders its header => shows the province's localized name (never a raw province id)
  - Resolves the current owner => shows the owner's flag and localized country name
  - Builds the resources list => shows every resource the province currently has, unfiltered (today: just population; see "Out of Scope")
  - Province currently has zero resources => the resources section is simply empty, no placeholder text

- Occupation display
  - Province has no occupier, or the occupier is the same country as the owner => only the owner row is shown, full color, no occupant row
  - Province has an occupier different from its owner => owner row shown grayed out/semi-transparent, occupier row shown full-color, both side by side in one row

- Click-to-select
  - Player clicks the owner row => selects that country and switches the map lens off Province so the country's own info panel becomes visible
  - Player clicks the occupant row => same as above, but selects the occupier's country

- Live refresh
  - Owner or occupier changes while the panel is visible (e.g. via existing debug cheats) => the panel's rows update immediately, no stale flag/name left showing

## Tech Notes

Maps each product-facing behaviour above to its concrete implementation.

- Player clicks a province while the Province lens is active:
  - `MapClickHandler.HandleProvinceClick` pushes `SelectProvinceCommand { ProvinceId = provinceId }`
  - `GameLogic.ApplySelectProvince` processes it, setting `VisualState.SelectedProvince.IsValid = true`
- No province selected / selection cleared:
  - `SelectProvinceCommand { ProvinceId = "" }` (e.g. clicking open water) sets `VisualState.SelectedProvince.IsValid = false`
  - Panel hides via `style.display = DisplayStyle.None`, the same pattern `CountryInfoView.Refresh` already uses for `SelectedCountryState`
- Player switches lens away from Province:
  - Mirrors `HUDDocument.RefreshCountryViews`'s existing `isProvinceLens` branch, which already force-hides the country-info panel while the Province lens is active; this feature adds the inverse hide for the new panel in the non-Province-lens branches
  - `VisualState.SelectedProvince` itself is not mutated — only the panel's visual surface is hidden
- Header:
  - Text = `_loc.Get($"province_name.{provinceId}")`, per the locale key convention in `.claude/rules/unity/province_config_generator.md`
- Owner resolution:
  - Reads `VisualState.ProvinceOwnership.OwnerByProvinceId[provinceId]` (runtime-ownership-first rule, `.claude/rules/unity/map_system.md`)
  - Flag: `CountryVisualConfig.Find(ownerId)?.flag`; name: `_loc.Get($"country_name.{ownerId}")` — same construction `CountryInfoView`/`OrgLensCountryView` already use
- Resources list:
  - Reuses `CountryInfoView`'s `ResourcesView` generic resource-row rendering
  - Sourced from ECS `Resource`/`ResourceOwner` entities with `OwnerId == provinceId` — today only `population` (`ResourceOwner.OwnerType.Province`, seeded by `InitSystem`, otherwise consumed only by `CountryPopulationCollector`'s country-level aggregation)
  - No limiting/prioritization UX — that curation is deferred to GitHub issue #41
  - Empty state mirrors `ResourcesView.Refresh`'s existing empty-state behavior (empty container, no placeholder)
- Occupation rules:
  - `VisualState.ProvinceOccupation.OccupierByProvinceId` has no entry, or its entry equals the owner id => treated as unoccupied (same rule established in `Docs/Specs/26_07_18_19_province-occupation/spec.md`)
  - When occupied: owner chip gets the semi-transparent/gray treatment (same category as the `opacity: 0.5` disabled-state pattern already used elsewhere in `Assets/UI/Shared/SharedStyles.uss`); occupier chip is full-opacity, built from `CountryVisualConfig.Find(occupierId)?.flag` / `_loc.Get($"country_name.{occupierId}")`
  - Both chips are laid out side-by-side in a single horizontal row, not stacked
- Click-to-select (owner and occupant rows alike):
  - Implemented via `PointerUpEvent` + a manual `ContainsPoint` check (per the documented Unity 6000.4.1f1 `Button.clicked`/`ClickEvent` bug — never those APIs)
  - Pushes `SelectCountryCommand(id)` (the same command `MapClickHandler` already pushes for non-Province-lens country clicks) plus a lens-change command switching `MapLens.Lens` away from `Province` (e.g. to `Political`), both in the same frame, per the action-before-pause command-ordering convention (`.claude/rules/unity/game_loop_integration.md`)
  - The occupant row is independently clickable, regardless of the owner row's grayed-out treatment
- Live refresh:
  - Panel subscribes to `VisualState.ProvinceOwnership.PropertyChanged` and `VisualState.ProvinceOccupation.PropertyChanged` — the same events `MapLensApplier` already reacts to for map recoloring

## Out of Scope

- Any curation, filtering, capping, or prioritization of which province resources are displayed — every resource entity currently owned by the province is shown unfiltered; the follow-up limiting/update work is tracked separately by GitHub issue #41 and explicitly deferred.
- Introducing any new province-scoped resource types, collectors, or `ResourceEffect`s beyond what already exists in the ECS today (currently only `population` is keyed by `ResourceOwner.OwnerType.Province`). This feature only displays whatever province-owned resource state already exists; it does not add new ones.
- Province-level control, characters, or actions display — the new panel covers only header/owner/occupier/resources as requested; it does not add equivalents of `CountryInfoView`'s control row, characters slide, or actions slide.
- Any change to province occupation semantics, the existing `DebugSetProvinceOccupierCommand`/toggle cheat, or `ProvinceOccupationSystem` — this feature only reads `VisualState.ProvinceOccupation` for display.
- Any change to `MapClickHandler`'s existing per-lens click routing (`SelectProvinceCommand` in the Province lens, `SelectCountryCommand` elsewhere) beyond the new click-to-select interaction added inside the panel's owner/occupant rows themselves.
- Any change to the province geometry/config generation pipeline (`scripts/generate_provinces.py`, `ProvinceProcessor`, `province_config.json`, `provinces_1880.json`).
- Hover-triggered or tooltip-only province previews — this is a click-to-select panel, matching the existing click-to-select country panel, not a hover preview.
- Multi-province selection, comparison views, or any change to `SelectedProvinceState` supporting more than one concurrently selected province.
- Any change to the existing debug-only "Selected province" menu in `HUDDocument.cs` (`RefreshSelectedProvinceDebugMenu` and its cheat buttons) — that remains a separate, developer-facing surface untouched by this feature.
