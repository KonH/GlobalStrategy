# Spec: Make More Countries Available (Serbia, Bulgaria, Bosnia-Herzegovina, Montenegro, Romania, Greece)

## Feature Intent

As a player, I want Serbia, Bulgaria, Bosnia_Herzegovina, Montenegro, Romania, and Greece to be
playable, spawned countries in-game — visible and colored on the map, ownable, and populated with
their own characters — so that the Balkan region of the 1880 world stops appearing empty/unavailable
and becomes part of the active game world like the 20 countries that are already available.

## Background (already true today — no rediscovery needed)

All 6 countries already exist end-to-end in the data pipeline, just switched off:

- Each has its own raw feature in `Assets/Map/world_1880.json` (self-owned, not merged into
  `Ottoman_Empire`/`Austria_Hungary`), its own `CountryEntry` in `Assets/Configs/country_config.json`,
  its own generated provinces in `Assets/Configs/province_config.json` / `provinces_1880.json`, its own
  slot in `Assets/Configs/CountryVisualConfig.asset` (color assigned, flag empty), and complete EN/RU
  `country_name.*` / `province_name.*` locale entries.
- The only thing gating a country from actually spawning is `CountryEntry.IsAvailable`
  (`src/Game.Configs/CountryConfig.cs`): `InitSystem.Run` (`src/Game.Main/InitSystem.cs`) only creates
  the ECS `Country` entity — and only that entity gets resources, characters, and relations — when
  `IsAvailable == true`. Map rendering visibility (`VisualState.WorldCountries.CountryIds`) is itself
  derived from which countries actually spawned, per `.claude/rules/unity/map_system.md`.
- Current entries for all 6 (identical shape, `isAvailable: false`, empty relations, default combat
  stats, e.g. Serbia):
  ```json
  {
    "countryId": "Serbia",
    "displayName": "Serbia",
    "mainMapFeatureIds": ["Serbia"],
    "secondaryMapFeatureIds": [],
    "initialResources": [{ "resourceId": "gold", "value": 75 }],
    "isAvailable": false,
    "historicalFriends": [],
    "historicalRivals": [],
    "baseDamage": 40,
    "baseDurability": 40
  }
  ```
  (Bulgaria/Bosnia_Herzegovina/Montenegro: gold 50; Romania/Greece: gold 75. All 6: baseDamage 40,
  baseDurability 40 — the config-wide defaults.)
- `Assets/Configs/character_config.json`'s `countryPools` currently has entries for exactly 20
  countries, and that set is identical to the 20 `isAvailable: true` countries — country availability
  and character-pool presence are a checked invariant
  (`.claude/rules/config_validation.md`'s pools-vs-available cross-check). None of the 6 candidates
  have a pool. Each existing country's pool is 3 characters × 5 roles (`ruler`, `military_advisor`,
  `diplomacy_advisor`, `economic_advisor`, `secret_advisor` — see `character_config.json`'s `roles`)
  = 15 characters, each with `namePartKeys` referencing `character.name.part.*` locale entries (no
  literal names in config) and a per-role skill range (ruler: all 4 skills 20-80; each advisor: its
  one skill 30-90).
- `Docs/Characters/character_roster.md` has a `## <Country>` narrative section per available country
  (3 Rulers / 3 Generals / 3 Barons / 3 Secret Advisors, each a real historical c.1880 figure with a
  portrait prompt) — none of the 6 candidates have a section.
- Flag PNGs (`Assets/Textures/Flags/Countries/<id>.png`) do not exist for any of the 6; the
  `CountryVisualConfig.asset` slot exists but `flag` is an empty `{fileID: 0}` reference. The
  `flag-assets` skill's pipeline (`scripts/utils/download_flags.py` Wikimedia download → Unity Sprite
  import → GUID wired into `CountryVisualConfig.asset` via MCP) is unrun for all 6.
- Character portrait images (`Assets/Textures/Characters/PortraitCard/<characterId>.png`) require the
  local ComfyUI + FLUX pipeline (`image-generation` skill) — per the issue, actual generation is
  explicitly deferred to a separate follow-up pass; this feature only needs to produce the
  characters/names the later pass will generate portraits for, plus documented generation steps.

## Acceptance Criteria

- A new game session starts
  - Serbia, Bulgaria, Bosnia_Herzegovina, Montenegro, Romania, and Greece each spawn as an in-game
    `Country` entity with their existing `initialResources` (unchanged), exactly like the 20 countries
    available today
  - Each of the 6 countries' provinces render with fill/border in every map lens (Province, Political,
    Org, Geographic), instead of staying hidden
  - Each of the 6 countries has a seeded `historicalFriends`/`historicalRivals` relations state
    (values TBD per Ambiguity 0 below)
- Player opens a country screen for one of the 6 countries and looks at its roster
  - Exactly 3 named characters appear per role (15 total: ruler, military_advisor, diplomacy_advisor,
    economic_advisor, secret_advisor), each resolving to a real display name via locale — no
    "key not found" warnings
  - Each character's skill range matches the existing per-role convention (ruler: power/charm/
    stinginess/intrigue 20-80; each advisor: its one mapped skill 30-90)
- Someone runs the `.claude/rules/config_validation.md` pools-vs-available cross-check after this
  feature lands
  - The check reports zero countries in `isAvailable` without a pool, and zero pools without a
    matching `isAvailable` entry
- Player views a country's flag (map tooltip / relations UI / character screens) for one of the 6
  - Either the flag renders correctly (full pipeline completed), or — if Unity/MCP or network access
    isn't available in the environment doing the work — the script-side `COUNTRY_FLAGS` entries exist
    and the remaining Unity-side import/wiring step is called out as outstanding in the handoff
- Player looks for character portraits for one of the 6 countries' new characters
  - No portraits are generated by this feature (explicitly deferred); a documented recipe (paths,
    prompts, regional/role style modifiers) exists for the follow-up pass to use, listing every new
    `characterId` that needs a portrait

## Tech Notes

- `country_config.json` change: flip `isAvailable` to `true` for the 6 `countryId`s listed above, and
  set `historicalFriends`/`historicalRivals` per Ambiguity 0. No other fields on these entries need to
  change (`initialResources`, `baseDamage`, `baseDurability` stay as-is — see Out of Scope).
- `character_config.json` change: add one `countryPools[]` entry per country, 3 characters × 5 roles,
  following the exact schema/skill-range convention of an existing pool (e.g. `Ottoman_Empire`'s).
  `characterId` convention: `<countryId lowercase>_<role short>_<n>` (existing short forms: `ruler`,
  `mil`, `dip`, `eco`, `sec`), matching e.g. `ottoman_empire_ruler_1`.
- New `character.name.part.*` locale keys (EN + `en.asset`, then real Russian translation in
  `ru.asset` per the `localization` skill — not machine/placeholder) for every new name part
  referenced by the 6 countries' `namePartKeys`. Character names should reuse real, meaningful c.1880
  historical figures per country (matching the existing roster convention for all 20 available
  countries today) — see Ambiguity 1.
- `Docs/Characters/character_roster.md`: add one `## <DisplayName>` section per country (use each
  country's `displayName` from `country_config.json`, e.g. `Bosnia-Herzegovina`), same
  Ruler/General/Baron/Secret Advisor sub-structure and portrait-prompt format as existing entries. Use
  the `add-character` skill for this narrative layer; use the `add-character-config` skill for the
  `character_config.json` + locale wiring (note: that skill requires the countryId to already exist in
  `countryPools`, so the pool block itself needs to be bootstrapped for a new country rather than
  incrementally added by that skill alone).
- Flag assets: add a `COUNTRY_FLAGS` entry per country to `scripts/utils/download_flags.py` (era-
  accurate ~1878-1890 flag, matching the existing entries' historical-accuracy convention, e.g.
  `Ottoman_Empire: "File:Flag_of_the_Ottoman_Empire.svg"`), per the `flag-assets` skill. Downloading
  (`download_flags.py`) is a plain Python/network step with no Unity dependency; importing as a Sprite
  and wiring the GUID into `CountryVisualConfig.asset` needs Unity MCP — call this out as a remaining
  step if MCP isn't available where this is implemented (see `.claude/commands/handle-issue.md`
  "Environment limits").
- Character portraits: no image generation in this feature. Document (in the plan or the roster
  entries themselves) the `image-generation` skill's recipe — output path
  `Assets/Textures/Characters/PortraitCard/{characterId}.png`, 512x512, prompt template, and the
  regional-style / role-description modifiers to use for these 6 countries (Southern
  European/Balkan region flavor) — so the follow-up pass can run
  `generate_images_batch.py` directly off a `.tmp/images.json` built from the new characters.
- Validation: after config edits, run the `.claude/rules/config_validation.md` Python cross-check
  (pools vs. `isAvailable`) as an explicit completion gate, plus a `dotnet-build`/`dotnet-test` pass if
  any config-loader tests assert on country/character counts.
- No C#/engine code changes are anticipated — `InitSystem`, `VisualStateConverter`,
  `CountryVisualConfig`, and `ProvinceRenderer`'s gating already generically support any country whose
  config flips to available; this is pure data/config/asset content work.

## Out of Scope

- Any new C#/engine code or Unity system/prefab/scene changes.
- Re-running the province generation pipeline (`scripts/utils/generate_provinces.py`) — provinces for
  all 6 countries are already generated and present in `province_config.json`/`provinces_1880.json`.
- Re-running the GeoJSON→`country_config.json` loader (`src/Game.Configs.Loader`) — no `_colonialParents`
  or geometry changes are needed; these 6 are already independent top-level features.
- Rebalancing `initialResources`, `baseDamage`, or `baseDurability` for the 6 countries — they keep
  their existing preserved values.
- Actually generating character portrait images — explicitly deferred by the issue to a later pass;
  this feature only prepares the characters and documents the generation steps.
- Adding these countries to any org (`organizations.json` `HqCountryId`) unless the owner asks for it.
- Any gameplay/balance tuning specific to these 6 countries beyond what any newly-available country
  already receives by default.

## Ambiguities

(reply with `N: answer`, then remove `ai-need-attention` to resume)

0. Should `historicalFriends`/`historicalRivals` be populated for the 6 countries with plausible
   1880s-Balkans relations, or left as empty lists (`[]`) like today, treating relations as strictly
   out of scope for this feature? If populated, is the following mapping acceptable — Serbia:
   rival `Ottoman_Empire`, friend `Austria_Hungary` (1881 Serbian-Austrian secret alignment); Bulgaria:
   rival `Ottoman_Empire`, friend `Russian_Empire` (Russian-sponsored independence); Montenegro: rival
   `Ottoman_Empire`; Romania: rival `Ottoman_Empire`, friend `Austria_Hungary` (1883 secret alliance);
   Greece: rival `Ottoman_Empire`; Bosnia_Herzegovina: rival `Ottoman_Empire`, friend `Austria_Hungary`
   (1878 Berlin Congress occupation/administration)? (assumed: populate with the mapping above)
1. Should the new characters' `namePartKeys` reference real historical c.1880 figures from each
   country's actual history (matching the existing roster convention used for all 20 currently-
   available countries, e.g. Argentina's Julio Argentino Roca), or fully invented period-flavored
   names instead? (assumed: real historical figures, for consistency with existing content)
2. Should this feature also add the `COUNTRY_FLAGS` script entries and attempt the Wikimedia download
   step of the flag pipeline now (script/network-only, no Unity dependency), leaving only the Unity
   Sprite-import + `CountryVisualConfig.asset` GUID-wiring step outstanding if Unity MCP isn't
   available in the implementing environment — or should flags be deferred entirely, alongside
   character portraits, to the same later pass? (assumed: add the script entries and attempt the
   download now; defer only the Unity-side wiring if MCP is unavailable)
3. Is mirroring the exact existing roster sub-structure (3 Rulers/monarchs, 3 Generals/military
   figures, 3 Barons/financiers, 3 Secret Advisors — matching the 5 `character_config.json` roles)
   sufficient for all 6 countries, or should any country get a different flavor split (e.g. Montenegro
   as a small principality might have thinner "Baron"/financier history to draw on)? (assumed: mirror
   the existing structure for all 6; use best-available real or plausible period figures where a role
   has thin historical material, e.g. court officials/merchants for Montenegro's "Baron" role)
