# Spec: Resources Visual Update

## Feature Intent

As a player, I want the resource summaries for the selected country and my organization to show only the small set of values that helps me understand their current state, with a recognizable image for every value and a compact single-row layout, so that newly introduced internal resources do not clutter these high-visibility HUD and information views.

The resource display is config-driven. Resource configuration defines both the resources available for presentation and an ordered display whitelist; the shared resource view filters its runtime state through that whitelist instead of rendering every resource entry it receives. Because countries and organizations own different resources, the same whitelist naturally produces the relevant subset for each owner type.

## Acceptance Criteria

- **Given** the resource configuration **When** it is loaded **Then** it contains an ordered display whitelist with exactly `gold`, `country_population`, `country_score`, and `org_score`; changing that list in config changes which resource IDs are eligible for the selected/player resource summaries without requiring a code change.
- **Given** a selected-country resource state that includes `gold`, `country_population`, `country_score`, and any other runtime resources (for example `recruits`) **When** the selected-country panel refreshes **Then** it shows only `gold`, `country_population`, and `country_score`, in whitelist order.
- **Given** a player-organization resource state that includes `gold`, `org_score`, and any other runtime resources **When** either the compact player-organization HUD summary or the player-organization information overlay refreshes **Then** it shows only `gold` and `org_score`, in whitelist order.
- **Given** a whitelisted resource is absent from the current owner state **When** a resource summary refreshes **Then** that resource is omitted cleanly; the view does not show a zero-value placeholder, error, or empty icon solely because the ID exists in the whitelist.
- **Given** the resource configuration **When** this feature is complete **Then** it includes complete definitions for `gold`, `country_population`, `country_score`, and `org_score`, including stable resource IDs and English/Russian localization keys for each displayed resource's name and description, so existing resource tooltips do not fall back to raw IDs.
- **Given** gold already has an existing coin image **When** resource image assets are prepared **Then** the existing gold image is retained, while new, distinct image assets are generated and imported for `country_population`, `country_score`, and `org_score`; each new image clearly represents its resource and remains recognizable at the resource row's displayed icon size.
- **Given** a visible whitelisted resource **When** its row is built **Then** its configured/generated image is shown beside its formatted numeric value; a missing optional image fails gracefully without preventing the value or other resources from rendering.
- **Given** any affected selected/player resource summary contains two or more visible resources **When** it is rendered **Then** all visible resource items appear on one horizontal line in whitelist order, rather than stacking one item per line; spacing keeps adjacent icon/value pairs visually distinct.
- **Given** a displayed resource item **When** the player hovers it **Then** the existing resource tooltip behavior remains available, using the resource's configured localized name and description and preserving applicable effect and income details.
- **Given** the resource state changes or the locale changes **When** the existing views refresh **Then** filtering, numeric values, images, ordering, and localized tooltip content update without duplicating resource items or retaining stale items from the previous state.

## Affected Views

- Selected-country information panel (`CountryInfoView`).
- Compact player-organization HUD summary (`PlayerOrgView`, backed by the currently named `PlayerCountry` UXML document).
- Player-organization information overlay (`OrgInfoDocument`).

## Out of Scope

- Changing how `gold`, `country_population`, `country_score`, or `org_score` is calculated, seeded, updated, saved, or exposed in visual state.
- Displaying non-whitelisted runtime resources such as province `population` or country `recruits` in these selected/player summaries.
- Adding resources to org-lens country summaries, leaderboards, action costs, tooltips outside the existing resource-item tooltip flow, or any other UI that does not currently instantiate the shared `ResourcesView`.
- Replacing or regenerating the existing gold coin image.
- Redesigning resource effect/income tooltip contents or numeric formatting beyond what is needed to keep the newly displayed resources readable.
- Creating new gameplay mechanics, balancing values, or resource-generation rules.
