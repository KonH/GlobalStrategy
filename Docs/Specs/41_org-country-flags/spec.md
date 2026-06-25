# Spec: Org and Country Flags

## Feature Intent

As a player, I want to see the real flag of each country and a representative icon for each organization displayed to the left of their names throughout the UI, so that I can identify countries and orgs at a glance without having to read the text.

## Acceptance Criteria

- **Given** flag image files exist in `Assets/Textures/Flags/Countries/` **When** the game loads **Then** each country's flag is displayed as a small inline image to the left of the country name label in:
  - The HUD `CountryInfo` panel (`CountryInfo.uxml`)
  - The `SelectCountry` modal (`SelectCountry.uxml`)
  - The `OrgLensCountryInfo` panel (`OrgLensCountryInfo.uxml`)

- **Given** org icon/flag image files exist in `Assets/Textures/Flags/Orgs/` **When** the game loads **Then** each org's flag/icon is displayed as a small inline image to the left of the org name label in:
  - The HUD `OrgInfo` panel (`OrgInfo.uxml`)
  - The player org display (`PlayerOrgView`)
  - The org lens country view (`OrgLensCountryView`) showing the top influencing org
  - The `SelectOrg` document (`SelectOrgDocument`)

- **Given** a country has no flag asset wired in `CountryVisualConfig` **When** that country is displayed **Then** the name label shows without any image (no broken icon, no error)

- **Given** an org has no flag asset wired in `OrgVisualConfig` **When** that org is displayed **Then** the name label shows without any image

- **Given** a flag is displayed next to a name **When** rendered **Then** the flag image is uniformly sized (height matches the line-height of the adjacent text, width proportional), vertically centered with the name label, and does not overflow its container

- **Given** `CountryVisualEntry` and `OrgVisualEntry` structs **When** a designer opens the ScriptableObject in the Unity Inspector **Then** each entry has a `Flag` Sprite field that can be assigned in the Inspector

- **Given** a Python/shell download script **When** run against the list of active country IDs from `country_config.json` **Then** it fetches a flag image for each available country and places it in `Assets/Textures/Flags/Countries/<countryId>.png` (or `.svg`)

- **Given** org flag images **When** downloaded or authored **Then** they are placed in `Assets/Textures/Flags/Orgs/<orgId>.png` (or `.svg`)

## Out of Scope

- Animated or waving flag effects
- Flags in tooltips (influence tooltip inside `CountryInfoView` that lists orgs by name)
- Map-layer rendering (flags on the map itself)
- Localization of flag assets (same image used for all locales)
- Historical accuracy beyond "plausible 19th-century flag for the entity" — exact historical correctness is not a hard requirement
- Automatic flag discovery at runtime; all flags must be pre-assigned in the visual config ScriptableObjects

## Ambiguities

- [NEEDS CLARIFICATION: The game is set in the 19th century. Should country flags be the historical flags of that era (e.g., Confederate states, pre-unified Germany), or modern flags? Historical flags require custom sourcing; modern flags can be fetched from a standard library like `flagcdn.com` or Wikimedia Commons.]
- [NEEDS CLARIFICATION: Organizations are fictional entities (secret societies, trade guilds, etc.). Do they have real-world flag analogues, or should their icons be the same symbolic SVG icons already used in the UI (e.g., `dagger.svg` for a spy org)? If so, "org flag" may simply mean "reuse the existing action/org icon" rather than a separate image set.]
- [NEEDS CLARIFICATION: The influence tooltip inside `CountryInfoView` also lists org names inline — should flags appear there too, or is that explicitly out of scope?]
- [NEEDS CLARIFICATION: Preferred image format — PNG sprites (like character portraits in `Assets/Textures/Characters/`) or SVG VectorImages (like icons in `Assets/UI/Icons/`)? SVG requires the `UIToolkitVectorImage` import setting and `fill="currentColor"` must be replaced. PNG is simpler but lower quality at small sizes.]
- [NEEDS CLARIFICATION: Display size — what pixel height should the flag render at next to the name label? (e.g., 20 px, 24 px, 32 px)]
