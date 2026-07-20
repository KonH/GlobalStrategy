# Spec: Leaderboards UI

## Feature Intent

As a player, I want a dedicated fullscreen leaderboard window reachable from the map HUD, so that I can compare organizations and countries by their current score without leaving the main map view or relying on debug/inspector data.

This feature is a UI surface over existing and in-flight score data. It must account for the resource-collector pipeline work in `Docs/Specs/26_07_18_17_resource-collector-pipeline/`, where country score is being moved from a composed `Score` component on `Country` entities to a generic `Resource{ResourceId="country_score"}` read through `CountryScoreSystem.GetScore`, while organization score remains a composed `Score` on `Organization` entities recomputed by `OrgScoreSystem`.

## Dependency / Coordination Note

The user specifically called out changes in `claude/resource-collector-pipeline-qehxfi` when considering score sources. No matching local branch is available in this checkout, but the local resource-collector-pipeline spec/plan already describes the relevant storage change:

- Organization scores remain available via `Organization + Score` composition for this feature's score source.
- Country scores must be read through the stable public query `CountryScoreSystem.GetScore(world, countryId)` rather than by directly requiring `Country + Score`, because country score is migrating to the `country_score` resource.
- The leaderboard UI should be written against those stable source contracts so it works before and after the resource collector pipeline lands, and so it does not couple the UI to country score's storage details.

## Acceptance Criteria

### Map HUD entry point

- **Given** the map UI/HUD is visible **When** the feature is implemented **Then** a new button is present in the map UI that opens the leaderboard window.
- **Given** the leaderboard window is closed **When** the player activates the leaderboard button **Then** the fullscreen leaderboard window opens without changing the current map selection, active map lens, time controls, or game simulation state.
- **Given** the leaderboard window is already open **When** the player activates the leaderboard button again **Then** the existing window remains open and focused rather than creating a duplicate instance.

### Fullscreen window shell

- **Given** the leaderboard window is open **When** it is rendered **Then** it occupies the fullscreen/modal UI layer used by existing fullscreen windows in the project and visually blocks interaction with the map beneath it.
- **Given** the leaderboard window is open **When** the player looks at the top of the window **Then** a header reading `Leaderboard` is visible.
- **Given** the leaderboard window is open **When** the player activates the close `X` button in the header **Then** the window closes and the map UI underneath is interactive again.
- **Given** the leaderboard window is closed and reopened **When** it renders again **Then** it starts on the same default tab every time unless a project-wide window convention already preserves tab state for other windows; if such a convention exists, follow that convention consistently.

### Tabs

- **Given** the leaderboard window is open **When** it renders **Then** it contains exactly two top-level tabs: `Organizations` and `Countries`.
- **Given** the `Organizations` tab is selected **When** the content area renders **Then** it shows organization leaderboard rows sorted by organization score in descending order.
- **Given** the `Countries` tab is selected **When** the content area renders **Then** it shows country leaderboard rows sorted by country score in descending order.
- **Given** two or more entries have identical scores **When** their rows are sorted **Then** ties are ordered deterministically by localized/display name ascending, and then by stable id ascending if names also match.
- **Given** the player switches tabs **When** the new tab becomes active **Then** the scroll position resets to the top of that tab's current ordered list, unless an existing tabbed-window convention in the project preserves per-tab scroll positions; if such a convention exists, follow it consistently.

### Scrollable item list

- **Given** the selected tab has more entries than fit vertically **When** the content area renders **Then** the list is vertically scrollable.
- **Given** the selected tab has no entries available **When** the content area renders **Then** an empty-state message is shown instead of a blank panel; suggested text is `No leaderboard entries available`.
- **Given** leaderboard data changes while the window is open **When** the UI receives the next normal visual-state/data refresh **Then** the visible list updates in place without requiring the player to close and reopen the window.
- **Given** an update changes row order while the player has the list scrolled **When** the list refreshes **Then** the UI preserves the currently selected tab and should preserve scroll offset where practical, while still ensuring row content and place numbers are correct after sorting.

### Item view

Each row in either tab has the same visual structure:

`place_number flag name score`

- **Given** a row is rendered **When** the entry is first in the sorted list **Then** its `place_number` displays `1`; subsequent rows display consecutive place numbers after sorting.
- **Given** an organization row is rendered **When** its flag is shown **Then** the flag uses the organization's existing flag/emblem asset and fallback behavior from the shared flag/org image asset pipeline.
- **Given** a country row is rendered **When** its flag is shown **Then** the flag uses the country's existing flag asset and fallback behavior from the shared flag/org image asset pipeline.
- **Given** a row is rendered **When** its name is shown **Then** it uses the display/localized name already used elsewhere in the UI for that organization or country, not a raw internal id except as an explicit fallback.
- **Given** a row is rendered **When** its score is shown **Then** the value is formatted consistently for both tabs, with large values readable and no unnecessary trailing decimal noise.
- **Given** a row's name is too long for the available width **When** it renders **Then** the row remains aligned and readable by truncating or wrapping according to the project's existing list-row convention; the score must remain visible.

### Score sources

- **Given** the `Organizations` tab builds its entries **When** it reads organization score **Then** it reads the current organization `Score` value from the same source used by `OrgScoreSystem`/visual state for organization score, preserving organization-score behavior from the org-scoring feature.
- **Given** the `Countries` tab builds its entries **When** it reads country score **Then** it uses the stable `CountryScoreSystem.GetScore(world, countryId)` query or an equivalent visual-state field backed by that query, not a direct `Country + Score` archetype scan.
- **Given** the resource-collector pipeline has landed **When** the `Countries` tab updates **Then** it displays the `country_score` resource-backed value through the stable country-score query and does not regress to zero because `Country` no longer carries a composed `Score` component.
- **Given** the resource-collector pipeline has not landed yet **When** the `Countries` tab updates **Then** it still works through the same stable country-score query, which currently reads the pre-pipeline country score storage.

### Interaction and non-goals

- **Given** a leaderboard row is visible **When** the player clicks it **Then** no navigation/action behavior is required for this feature unless an existing shared list-row component requires selection styling by default.
- **Given** the leaderboard is open **When** game time advances, bots act, province ownership changes, or scores update **Then** the open window reflects the updated scores through the normal refresh path; this feature does not introduce a separate score recomputation loop.

## Out of Scope

- New scoring formulas, score balancing, or score persistence changes.
- Any new score source beyond the current organization score and country score systems.
- Row click-to-focus behavior, search/filter controls, pagination, medals, historical trend charts, or score breakdown tooltips.
- Replacing existing flag asset pipelines or creating new flag artwork.
- Implementing or changing the resource collector pipeline itself; this feature only consumes the stable score source contracts that pipeline exposes.
- Localization key design beyond the literal labels named here (`Leaderboard`, `Organizations`, `Countries`, empty-state text); `/plan` should map these strings into the project's localization conventions if the surrounding UI is localized.
