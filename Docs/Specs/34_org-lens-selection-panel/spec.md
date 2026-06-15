# Spec: Org Lens Selection Panel

## Feature Intent

As a player in Organization lens mode, I want the bottom selection panel to show the dominant organization in the country I click, so that I can quickly understand who controls each territory without switching lens or hunting through the influence list.

## Acceptance Criteria

- **Given** the map is in `MapLens.Political` or `MapLens.Geographic` and the player clicks a country **When** the bottom panel renders **Then** it shows the existing `CountryInfoView` content (country name, resources, characters, influence breakdown) — no change from current behaviour.

- **Given** the map is in `MapLens.Org` and the player clicks a country that has a `TopOrgId` entry in `OrgMapState.Entries` **When** the bottom panel renders **Then** the `CountryInfoView` is hidden and a new `OrgLensCountryView` is shown in its place, displaying:
  - The dominant org's display name, resolved from `SelectedOrganizationState.DisplayName` (the org-click flow already populates this)

- **Given** the map is in `MapLens.Org` and the player clicks a country that has **no entry** in `OrgMapState.Entries` (no organization holds any influence there) **When** the bottom panel renders **Then** the `OrgLensCountryView` shows the country (via `SelectedCountryState.CountryId`) with a "no dominant organization" indicator; the influence breakdown list is empty.

- **Given** the map is in `MapLens.Org` and **no country is selected** (`SelectedCountryState.IsValid == false`) **When** the bottom panel renders **Then** the `OrgLensCountryView` is hidden (same behaviour as `CountryInfoView` when nothing is selected).

- **Given** the player is viewing the `OrgLensCountryView` and then switches the lens away from `MapLens.Org` **When** `MapLensState.PropertyChanged` fires **Then** the panel reverts to `CountryInfoView` (re-showing whatever `SelectedCountryState` currently holds).

- **Given** the player is in `MapLens.Org` with a country selected and `OrgMapState` updates (e.g. tick advances influence) **When** `OrgMapState.PropertyChanged` fires **Then** `OrgLensCountryView` refreshes with the latest `TopOrgId` and `InfluenceRatio` without requiring the player to re-click.

## Out of Scope

- Clicking on an organization entry in the `OrgLensCountryView` to open the full `OrgInfoDocument` overlay — navigation to org detail is handled elsewhere.
- Showing org resources, gold income, or character roster in this panel — those belong to the `OrgInfoDocument`.
- Changing how the `MapLens.Political` / `MapLens.Geographic` bottom panel works in any way.
- Adding or modifying any ECS systems, `VisualStateConverter`, or command processing — the required data (`OrgMapState`, `CountryInfluenceState`, `SelectedOrganizationState`) is already populated by existing systems.
- Persisting the selected-in-org-lens state across scene loads.

