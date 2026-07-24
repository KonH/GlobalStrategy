# Spec: Friends & Rivals Panel in Selected Country View

## Feature Intent

As a player, I want to see the selected country's friends and rivals as flag rows in the country info panel, so that I can quickly understand its diplomatic relationships and jump to a related country by clicking its flag.

## Acceptance Criteria

- Player selects a country in Political-lens view (the country info panel is open)
  - The panel shows a new block anchored to the right of the main country info and just before the Characters/Actions buttons => the block displays a "Friends" header followed by a row of that country's friend flags, and a "Rivals" header followed by a row of that country's rival flags
  - The selected country has no friends => the "Friends" header and row are hidden entirely (no empty header shown)
  - The selected country has no rivals => the "Rivals" header and row are hidden entirely, independently of whether Friends is shown
  - The selected country has neither friends nor rivals => the whole friends/rivals block takes no visible space
- Player hovers a flag in the Friends or Rivals row
  - A tooltip appears showing that country's localized display name
- Player clicks a flag in the Friends or Rivals row
  - That country becomes the newly selected country and the panel refreshes to show its info, resources, and its own friends/rivals
  - If a Characters or Actions sub-panel was open, it closes as part of switching to the newly selected country (matching existing behavior when selection changes)
- Player changes the selected country (via map click, flag click, or any other selection path) while the panel is visible
  - The Friends/Rivals block updates to the newly selected country's data — no stale flags from the previously selected country remain visible

## Tech Notes

- **Data source**: no backend changes needed. `SelectedCountryState.Relations` (`src/Game.Main/VisualState.cs`) exposes `CountryRelationsState.Friends` / `.Rivals` (`IReadOnlyList<string>` of country IDs), already populated by `VisualStateConverter`. Access via `_state.SelectedCountry.Relations.Friends` / `.Rivals` in `HUDDocument`.

- **Panel anchoring (block position)**:
  - UXML: `Assets/UI/HUD/CountryInfo/CountryInfo.uxml`. Add a new sibling `VisualElement` (e.g. `name="relations-block"`) inside `country-bar`, positioned between the existing `country-main-block` and `country-toggle-block` elements (lines 15–26). `country-bar` is already `flex-direction: row; justify-content: space-between` (`CountryInfo.uss` `.country-bar`), so this sibling naturally renders to the right of the main info block and to the left of the Characters/Actions buttons.
  - Inside `relations-block`: two sub-rows, e.g. `friends-row-block` (Label header `friends-header` + `VisualElement` flag container `friends-flags`) and `rivals-row-block` (Label header `rivals-header` + `VisualElement` flag container `rivals-flags`), each on its own line per the "header, next line: flags" requirement (`flex-direction: column` on each sub-block).

- **View/binding wiring**: extend `CountryInfoView` (`Assets/Scripts/Unity/UI/CountryInfoView.cs`), not a new view class — it already owns `Refresh(SelectedCountryState selected, ...)` and the `_lastCountryId` change-detection block (lines 100–104), which already triggers `SetCharsOpen(false)` / `SetActionsOpen(false)` on country change — the new block's own selection-change behavior (closing an open sub-panel) piggybacks on this same existing block, no new logic needed there.
  - Query the new elements in the constructor (`root.Q(...)`), mirroring existing `_flagElement`/`_controlRow` field pattern.
  - In `Refresh`, after the existing `selected.IsValid` handling, build/update the Friends and Rivals flag rows from `selected.Relations.Friends` / `.Rivals`. Toggle `friends-row-block`/`rivals-row-block` `style.display` to `DisplayStyle.None` when the respective list is empty, following the exact `hasChars`/`hasActions` precedent at lines 106–114.
  - Rebuild each flag row's children from the current list each `Refresh` call (`Clear()` then repopulate) since list membership can change with the selected country — no incremental diffing needed here (unlike `ActionLogView`'s animated list case), since flags don't need enter/exit animation per this spec.

- **Per-flag element (rendering, tooltip, click)**: for each country ID in `Friends`/`Rivals`, create a small `VisualElement` flag chip, following the flag-rendering pattern already used in `CountryInfoView.BuildControlTooltip` (lines 209–224) and `ProvinceInfoView`'s country chip:
  - Background image: `_countryVisualConfig?.Find(countryId)?.flag`, applied via `StyleBackground`, matching `Refresh`'s existing flag-setting logic at lines 89–97.
  - New USS class (e.g. `.relations-flag`) in `CountryInfo.uss`, sized smaller than the existing `.entity-flag` (64×64px, too large for a multi-flag row) — around 28–32px square, similar scale to `.control-icon` (22px). Use `gap` (not `margin-left`) on the flag row container (`friends-flags`/`rivals-flags`) for spacing between flags, per the documented USS gotcha in `.claude/rules/unity/uitoolkit.md`.
  - Each flag element is individually clickable (unlike the single-chip `ProvinceInfoView` pattern), so each flag itself must NOT be `PickingMode.Ignore` — instead each flag registers its own `PointerUpEvent` handler with a manual `ContainsPoint` check (never `Button.clicked`/`ClickEvent` — documented Unity 6000.4.1f1 bug in `.claude/rules/unity/uitoolkit.md`), invoking a new `Action<string>` C# event on `CountryInfoView` (e.g. `OnRelatedCountryFlagClicked`).
  - Tooltip: register each flag element individually via `TooltipSystem.RegisterTrigger(flagEl, $"relation-{countryId}-{index}", ctx => ...tooltip content..., new HashSet<string>())`, building a simple `Label` with `_loc.Get($"country_name.{countryId}")` text (existing `country_name.{CountryId}` convention per `.claude/rules/unity/localization.md`). Use a unique trigger key per flag element (not per country ID alone) in case the same country could theoretically appear once per list.

- **Click → select country wiring**: in `HUDDocument.cs`, subscribe to the new `CountryInfoView.OnRelatedCountryFlagClicked` event next to the existing `_countryInfo.OnSubPanelOpened` / `OnCountryActionCardClicked` subscriptions (`Start()`, ~line 118-120). Handler pushes only `_commands.Push(new SelectCountryCommand(countryId));` — no `ChangeLensCommand` needed (unlike `HandleProvinceInfoCountryRowClicked` at lines 407–413), since `CountryInfoView` is already the Political-lens-only panel (see `HUDDocument` ~line 380-392, `RefreshCountryInfoView`'s `isOrgLens` branch).

- **Localization**: add new `hud.*` keys for the headers — `hud.friends` ("Friends") and `hud.rivals` ("Rivals") — to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, per `.claude/rules/unity/localization.md`'s `hud.*` namespace convention. Set header `Label.text` via `_loc.Get("hud.friends")` / `_loc.Get("hud.rivals")` in `Refresh`.

## Out of Scope

- Any changes to `VisualState`, ECS components, or `VisualStateConverter` — the friends/rivals data model is already fully implemented (see `Docs/Specs/26_07_23_06_country-relations/`).
- Any UI for editing/creating relations — relation changes remain debug-menu-only.
- Adding an equivalent friends/rivals block to `OrgLensCountryInfo` (the org-lens selected-country panel) — this spec covers only the Political-lens `CountryInfoView` panel named in the feature request.
- Animated enter/exit transitions for individual flags when the friends/rivals list changes (e.g. relation gained/lost while the country stays selected) — rows are simply rebuilt on refresh.
- Showing relation type/strength/duration or any relation metadata beyond the flag + name — only presence in `Friends`/`Rivals` is surfaced.

## Ambiguities

- [NEEDS CLARIFICATION: should clicking a friend/rival flag for a country that is *not the player's own* still be selectable the same way as any other country (no restriction), or should some countries be non-clickable/non-selectable in this context? Assumed no restriction — same `SelectCountryCommand` path used everywhere else in the HUD — but not explicitly confirmed by the feature request.]
