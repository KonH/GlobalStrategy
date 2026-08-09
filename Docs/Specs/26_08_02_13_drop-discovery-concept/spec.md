# Spec: Drop Discovery Concept

## Feature Intent

As a player (and as every participating organization), I want every available country on the map already known at game start with no discover-country card, progressive reveal, or related bot behaviour, so that the game no longer has a discovery fog layer and country play begins fully unlocked for all participating orgs.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A new game starts with one or more participating organizations and a set of available countries (countries that are part of the playable map for this scenario).
  - The session initializes => every participating organization already knows every available country; there is no undiscovered-country state for any of them.
  - The player views the map at the start of the session => every available country’s territory is visible under the normal map lenses (no fog / blank provinces that wait on discovery).
  - The player opens an available country that is not that org’s HQ => country actions, control, characters, and other existing country gameplay for that country are available on the same terms as for already-known countries today — discovery is not a gate.
- The player inspects the organization hand / org action deck during a normal session.
  - Hand and deck evaluation runs => no “Discover Country” (or equivalent discover-country) card exists in any org’s pool, deck, or hand for any participating org.
  - The player looks for a way to discover more countries mid-game => there is no card, effect, confirmation fly-text, camera pan-to-newly-discovered-country, or game-log “discovered” line produced by discovery gameplay, because discovery gameplay no longer exists.
- A bot-driven participating organization plays through a normal session with default bot settings.
  - The bot takes turns => default feature id is `control` (renamed from `discoverAndControl`); it never prioritizes or plays a discover-country action (that card no longer exists) and plays control/country cards across all available countries without a discovery gate.
- Mid-game after start (any month, any org).
  - Time advances / cards are played => no new country becomes “discovered” as a distinct gameplay event; the set of known available countries does not grow over time because it was already complete at start.
- Debug / calibration tooling that previously force-discovered every country for the viewed org.
  - A developer opens the debug panel or a headless calibration path that used “Discover All Countries” => the Discover All Countries command, HUD button, and calibration push are gone; headless calibration no longer depends on force-discover because all available countries are already playable without a discovery gate.
- An older save that was written when discovery still existed (some countries discovered, some not).
  - The save is loaded under the new rules => no compatibility is required: discovery components/state are gone and pre-change saves are not supported or migrated for discovery. New sessions simply have no discovery concept.

## Tech Notes

Maps each product-facing behaviour above to its concrete implementation — specific files, classes, methods, commands, state paths.

- **Vocabulary — “enabled / available countries”:**
  - Owner prompt says “all enabled countries discovered at start.” Country config has no `Enabled` flag; playable countries are those with `CountryEntry.IsAvailable == true` (`src/Game.Configs/CountryConfig.cs`). `InitSystem.Run` only creates `Country` ECS entities for `IsAvailable` entries (`src/Game.Main/InitSystem.cs` ~lines 26–32). Country decks/hands are likewise only built for `IsAvailable` countries (~lines 660–661). Spec “available countries” = `IsAvailable` countries already in the world. Countries with `IsAvailable: false` are not in the ECS world and remain out of scope (unchanged).
  - Bot feature settings use a separate `Enabled` bool (`BotFeatureSetting.Enabled` / `game_settings.json` `botFeatures[].enabled`) — do not confuse with country availability.

- **Start-of-game: all available countries known (implicitly) for every participating org:**
  - Owner decision: **full removal (B)** of the discovery concept. Delete `InitSystem.DiscoverInitialCountries` and its call site; do **not** emit any `DiscoveredCountry` entities. “Known” is implicit for every world `Country` (i.e. every `IsAvailable` country).
  - Consumers that previously consulted discovery must stop gating on it (map, bots, UI).

- **Remove discover-country card, effect, progressive discovery, and ECS discovery entirely:**
  - Config: delete `actionId: "discover_country"` from `Assets/Configs/action_config.json` (action row + both Illuminati/Masons `orgPools` entries — owner: **delete empty pool entries**, no replacement org card). Delete `effectId: "discover_country"` / `effectType: "DiscoverCountry"` from `Assets/Configs/effect_config.json`. Remove matching art entry in `Assets/Configs/ActionVisualConfig.asset` (owner: cleanup card-related art with the rest).
  - Config types: remove `DiscoverCountryEffectParams` and `"DiscoverCountry"` arm of `ActionEffectDefinitionListConverter` in `src/Game.Configs/EffectConfig.cs`.
  - Effect pipeline: remove `CreateActionEffectSystem` branch for `DiscoverCountryEffect`; delete `DiscoverCountrySystem` and its `GameLogic` orchestration call; remove `CleanupActionEffectsSystem` / `CleanupEffectNotificationsSystem` sweeps for discovery types.
  - Components to delete: `DiscoverCountryEffect`, `DiscoveryApplied`, and `[Savable] DiscoveredCountry` (`src/Game.Components/DiscoveredCountry.cs`) entirely.
  - Locale keys to strip in the same change: `action.discover_country.*`, `effect.discover_country.*`, `hud.discovery.confirmation`, `game_log.discovered_format` in `Assets/Localization/en.asset` / `ru.asset`.

- **Map / presentation: available countries visible; unavailable stay hidden:**
  - Owner: unavailable (`IsAvailable: false`) map features must **still not be visible**. After deleting discovery, `MapLensApplier` must stop consulting `DiscoveredCountries` and instead gate visibility on whether the owner country exists in world / playable set (same net effect as today: available → visible, unavailable → hidden).
  - Remove `CardPlayAnimator` discovery pan / `hud.discovery.confirmation` fly-text path.
  - Delete `VisualState.DiscoveredCountriesState` / `RecentlyDiscovered` and `VisualStateConverter.UpdateDiscoveredCountries` (and any discovery-related change notifications).

- **Game log:**
  - Owner: strip in the same change — remove `GameLogEntryKind.Discovery`, discovery formatters (`GameLogLineFormatter.BuildDiscoveryLine` / `ActionLogView` branches), and emission from `VisualStateConverter`. No dormant enum/locale for old logs.

- **Bots:**
  - Owner: rename `discoverAndControl` → **`control`** (control-only successor, no discover parameters / discover play path). Rename class/id/registry registration; update `Assets/Configs/game_settings.json` and `GameSettings` defaults; move/rename `Docs/BotFeatures/discoverAndControl/` → `Docs/BotFeatures/control/` (owner Q4: follow Q3 rename).
  - `BotObservation.Build` must stop filtering countries/hands/characters/control on discovery; drop `DiscoveredCountryIds` and `DiscoversCountry` classification with the effect type.

- **Debug “Discover All Countries” / calibration:**
  - Owner: **remove entirely** — delete `DebugDiscoverAllCountriesCommand`, HUD button, `GameLogic.ApplyDebugDiscoverAllCountries`, and CalibrationRunner / end-game calibration skill references that push discover-all.

- **Tests expected to change (non-exhaustive):**
  - `DiscoveryPerOrgTests`, `SavableDiscoveryTests`, `DiscoverAndControlFeatureTests`, `BotSessionTests` (discover-count assertions), `MultiOrgInitTests` (HQ-only initial discovery), `UnifiedPipelineTests` / `GameLogStateTests` (discovery applied), `BaselineCardPlayTests` / bot observation tests that seed `DiscoveredCountry`, `VisualStateChangeNotificationTests` (`DiscoveredCountriesState`), `SaveLoadRoundTripTests` discovery round-trip.

- **What discovery currently does *not* gate (for planner awareness):**
  - Country ECS entities, country card decks/hands, and character seeding already run for all `IsAvailable` countries regardless of discovery (`InitSystem`). Discovery primarily gates map visibility, bot observation surface, and the discover card’s progressive reveal — not deck creation. After this feature, those remaining gates must align with “everything available is known.”

## Out of Scope

- Changing which countries are `IsAvailable` in country config, or expanding the playable country set beyond current availability rules.
- Rebalancing gold costs, hand sizes, control pool caps, or non-discovery cards (sphere of pressure, opinion cards, war cards, etc.) except where removing the discover card leaves an empty org pool that must be cleaned up or replaced for decks to remain valid.
- Designing a brand-new bot strategy beyond “no discovery + still play existing non-discovery cards” — any deeper AI redesign is separate work; only the minimum replacement for `discoverAndControl` so default bots remain functional is in scope.
- Map art, fog shader replacements, or new “exploration” flavour — this is removal, not a redesign of fog of war.
- Multiplayer / per-client asymmetric knowledge beyond the existing per-org model; once discovery is dropped, orgs simply do not differ by discovery set.
- Reworking save format versioning infrastructure beyond whatever is required for the chosen save-compatibility decision in Ambiguities.

## Ambiguities

Resolved by owner on issue #111 (2026-08-02):

0. **Removal depth** — **B, full**: delete discovery from ECS/UI entirely; “known” is implicit for every world `Country`.
1. **Map presentation** — Unavailable countries remain **not visible**; stop consulting discovery and gate on playable/world countries instead.
2. **Save compatibility** — **None**: no migration or support for pre-change discovery saves.
3. **Default bot replacement** — Rename `discoverAndControl` → **`control`** (control-only; drop discover params/path).
4. **`Docs/BotFeatures/discoverAndControl/`** — Follow Q3: rename/move to `control` (not leave orphaned under old id).
5. **Debug Discover All** — **Remove** command, HUD button, and calibration push entirely.
6. **Game log / locale / art cleanup** — Strip discovery enum/formatters/locale keys **and** card-related art in the same change.
7. **Empty org pools** — **Delete** the Illuminati/Masons `orgPools` entries; no replacement org card in this feature.
