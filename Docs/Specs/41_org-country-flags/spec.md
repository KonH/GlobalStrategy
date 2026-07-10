# Spec: Org and Country Flags

## Feature Intent

As a player, I want to see the historical flag of each country and a representative image for each organization displayed to the left of their names throughout the UI, so that I can identify countries and orgs at a glance without having to read the text.

## Acceptance Criteria

- **Given** flag PNG files exist in `Assets/Textures/Flags/Countries/` **When** the game loads **Then** each country's historical (19th-century era) flag is displayed as a 64×64 px inline image to the left of the country name label in:
  - The HUD `CountryInfo` panel (`CountryInfo.uxml`)
  - The `SelectCountry` modal (`SelectCountry.uxml`)
  - The `OrgLensCountryInfo` panel (`OrgLensCountryInfo.uxml`)

- **Given** org image PNG files exist in `Assets/Textures/Flags/Orgs/` **When** the game loads **Then** each org's representative image (sourced from real-world historical or mythological references, e.g. the Illuminati eye symbol) is displayed as a 64×64 px inline image to the left of the org name label in:
  - The HUD `OrgInfo` panel (`OrgInfo.uxml`)
  - The player org display (`PlayerOrgView`)
  - The org lens country view (`OrgLensCountryView`) showing the top influencing org
  - The `SelectOrg` document (`SelectOrgDocument`)
  - The control tooltip inside `CountryInfoView` (inline org-name list)

- **Given** a country has no flag asset wired in `CountryVisualConfig` **When** that country is displayed **Then** the name label shows without any image (no broken icon, no error)

- **Given** an org has no image asset wired in `OrgVisualConfig` **When** that org is displayed **Then** the name label shows without any image (no broken icon, no error)

- **Given** a flag is displayed next to a name **When** rendered **Then** the image renders at exactly 64×64 px, is vertically centered with the name label, and does not overflow its container; source images may be larger and have varying aspect ratios, displayed with `scale-mode: scale-to-fit`

- **Given** `CountryVisualEntry` and `OrgVisualEntry` structs **When** a designer opens the ScriptableObject in the Unity Inspector **Then** each entry has a `Flag` Sprite field that can be assigned in the Inspector

- **Given** a Python download script **When** run against the list of available country IDs from `country_config.json` **Then** it fetches a historical PNG flag for each country and saves it to `Assets/Textures/Flags/Countries/<countryId>.png`

- **Given** org images **When** downloaded from public internet sources **Then** they are saved to `Assets/Textures/Flags/Orgs/<orgId>.png`

## Out of Scope

- Animated or waving flag effects
- Map-layer rendering (flags on the map itself)
- Localization of flag assets (same image used for all locales)
- SVG format — all flag/org assets are PNG
- Automatic flag discovery at runtime; all flags must be pre-assigned in the visual config ScriptableObjects

## Design Notes

- **Flag style:** historical era-accurate flags (19th century), not modern flags; sourced from Wikimedia Commons or equivalent public domain sources
- **Org images:** real-world sourced PNG images for known historical/mythological organizations (e.g. Illuminati eye-in-pyramid symbol); downloaded from public internet sources
- **Format:** PNG only, sourced at native resolution (larger than 64×64 is fine; Unity will scale down)
- **Display size:** 64×64 px unified render size; aspect ratio differences in source images are handled by `scale-mode: scale-to-fit` in USS — this value may be tuned later
- **Control tooltip:** org flags also appear in the inline org-name list inside the `CountryInfo` control tooltip
