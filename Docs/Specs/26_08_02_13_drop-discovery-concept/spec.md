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
  - The bot takes turns => it never prioritizes or plays a discover-country action; bot behaviour that existed only to alternate discovery vs control is gone or replaced by non-discovery behaviour so bots still play meaningful country/control cards without depending on progressive discovery.
- Mid-game after start (any month, any org).
  - Time advances / cards are played => no new country becomes “discovered” as a distinct gameplay event; the set of known available countries does not grow over time because it was already complete at start.
- Debug / calibration tooling that previously force-discovered every country for the viewed org.
  - A developer opens the debug panel or a headless calibration path that used “Discover All Countries” => that cheat is either removed as redundant or becomes a no-op that does not change gameplay state, because all available countries are already known; headless calibration that depended on discovering all countries still reaches a fully-known map without relying on progressive discovery.
- An older save that was written when discovery still existed (some countries discovered, some not).
  - The save is loaded under the new rules => [NEEDS CLARIFICATION: see Ambiguities] — behaviour for partial-discovery saves must be decided before implementation; until clarified, the product requirement for *new* sessions is full knowledge at start only.

## Tech Notes

Maps each product-facing behaviour above to its concrete implementation — specific files, classes, methods, commands, state paths.

- **Vocabulary — “enabled / available countries”:**
  - Owner prompt says “all enabled countries discovered at start.” Country config has no `Enabled` flag; playable countries are those with `CountryEntry.IsAvailable == true` (`src/Game.Configs/CountryConfig.cs`). `InitSystem.Run` only creates `Country` ECS entities for `IsAvailable` entries (`src/Game.Main/InitSystem.cs` ~lines 26–32). Country decks/hands are likewise only built for `IsAvailable` countries (~lines 660–661). Spec “available countries” = `IsAvailable` countries already in the world. Countries with `IsAvailable: false` are not in the ECS world and remain out of scope (unchanged).
  - Bot feature settings use a separate `Enabled` bool (`BotFeatureSetting.Enabled` / `game_settings.json` `botFeatures[].enabled`) — do not confuse with country availability.

- **Start-of-game: all available countries known for every participating org:**
  - Today `InitSystem.DiscoverInitialCountries` (`src/Game.Main/InitSystem.cs` ~736–759) creates one `DiscoveredCountry { OrgId, CountryId }` entity per org for that org’s `HqCountryId` only.
  - Required behaviour: for each participating org × each world `Country` id (i.e. every available country), the org must treat that country as known from the first tick — either by expanding `DiscoverInitialCountries` to emit a full cross-product of `DiscoveredCountry` entities, or by removing the discovery gate entirely so consumers no longer consult `DiscoveredCountry` (see Ambiguities on removal depth).
  - Call site remains `InitSystem.Run` after `CreateCountryActionEntities` (~line 122).

- **Remove discover-country card, effect, and progressive discovery:**
  - Config: delete / stop shipping `actionId: "discover_country"` from `Assets/Configs/action_config.json` (action row + both `orgPools` entries that currently list only `discover_country` for Illuminati/Masons). Delete `effectId: "discover_country"` / `effectType: "DiscoverCountry"` from `Assets/Configs/effect_config.json`. Remove matching art entry in `Assets/Configs/ActionVisualConfig.asset`.
  - Config types: `DiscoverCountryEffectParams` and `"DiscoverCountry"` arm of `ActionEffectDefinitionListConverter` in `src/Game.Configs/EffectConfig.cs`.
  - Effect pipeline: `CreateActionEffectSystem` branch that creates `DiscoverCountryEffect` (`src/Game.Systems/CreateActionEffectSystem.cs` ~50–52); `DiscoverCountrySystem.Update` (`src/Game.Systems/DiscoverCountrySystem.cs`) and its orchestration call in `GameLogic` (`src/Game.Main/GameLogic.cs` ~264); `CleanupActionEffectsSystem` sweep of `DiscoverCountryEffect`; `CleanupEffectNotificationsSystem` sweep of `DiscoveryApplied`.
  - Components: `DiscoverCountryEffect` (`src/Game.Components/ResourceChangeEffect.cs`), `DiscoveryApplied` (`src/Game.Components/GameLogEffects.cs`). Fate of `[Savable] DiscoveredCountry` (`src/Game.Components/DiscoveredCountry.cs`) is the removal-depth ambiguity below.
  - Locale keys to retire with the concept (or leave orphaned only if localization cleanup is deferred): `action.discover_country.*`, `effect.discover_country.*`, `hud.discovery.confirmation`, `game_log.discovered_format` in `Assets/Localization/en.asset` / `ru.asset`.

- **Map / presentation: no undiscovered fog:**
  - `MapLensApplier` (`Assets/Scripts/Unity/Map/MapLensApplier.cs`) currently disables fill/border renderers when `!IsCountryDiscovered(ownerId)`, reading `VisualState.DiscoveredCountries.CountryIds`. After this feature, available countries must always render under normal lens rules (fills/borders on). Implementation either always-true discovery sets in visual state, or delete the discovery check / `DiscoveredCountries` subscription.
  - `CardPlayAnimator` (`Assets/Scripts/Unity/UI/CardPlayAnimator.cs`) pans to `DiscoveredCountries.RecentlyDiscovered` and shows `hud.discovery.confirmation` fly text — remove that discovery success path.
  - `VisualState.DiscoveredCountriesState` / `RecentlyDiscovered` (`src/Game.Main/VisualState.cs`) and `VisualStateConverter.UpdateDiscoveredCountries` (`src/Game.Main/VisualStateConverter.cs` ~482–504) — remove or reduce to always-full sets with no “recently discovered” delta tracking.

- **Game log:**
  - `VisualStateConverter` emits `GameLogEntryKind.Discovery` from `DiscoveryApplied` (~907–913). `ActionLogView` / `GameLogLineFormatter.BuildDiscoveryLine` format those lines. With no progressive discovery, new discovery log lines must not appear. Whether `GameLogEntryKind.Discovery` enum value and formatter stay for old-save display is under Ambiguities / save compatibility.

- **Bots:**
  - `DiscoverAndControlFeature` (`src/Game.Bots/DiscoverAndControlFeature.cs`, id `"discoverAndControl"`) prefers discover vs control via `discoveredCountriesAvailableControl` and `BotCardView.DiscoversCountry`. Registered in `BotFeatureRegistry.CreateDefault`; enabled by default in `Assets/Configs/game_settings.json` `botFeatures` and mirrored defaults in `src/Game.Configs/GameSettings.cs`.
  - `BotObservation.Build` (`src/Game.Bots/BotObservation.cs`) gates countries, country hands, characters, and control shares on the org’s `DiscoveredCountry` set; exposes `DiscoveredCountryIds` and classifies cards via `DiscoverCountryEffectParams` → `DiscoversCountry`.
  - Product requirement: remove discover-related bot logic. Concretely that means retiring or replacing `discoverAndControl` (config entry, registry registration, `Docs/BotFeatures/discoverAndControl/*` eval artifacts), dropping `DiscoversCountry` classification once the effect type is gone, and ensuring bots can see/play all available countries without a discovery gate (either full initial `DiscoveredCountry` sets or observation no longer filters on discovery). Default bot profile must still play useful control/country cards — typically fall back to `baselineCardPlay` or a control-only successor feature (clarification if a new feature id is required).

- **Debug “Discover All Countries” / calibration:**
  - Command: `DebugDiscoverAllCountriesCommand` (`src/Game.Commands/DebugDiscoverAllCountriesCommand.cs`).
  - Applied by `GameLogic.ApplyDebugDiscoverAllCountries` (`src/Game.Main/GameLogic.cs` ~644+) for the view org; UI button in `HUDDocument` debug panel; headless use in `src/Game.ConsoleRunner/CalibrationRunner.cs` and end-game calibration skill docs. After full-at-start discovery, remove the button/command or make apply a no-op; update calibration so it no longer depends on force-discover to unlock the map.

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

- [NEEDS CLARIFICATION: **Removal depth for `DiscoveredCountry`** — Prefer (A) keep the `[Savable] DiscoveredCountry` component and related visual-state set, but initialize the full org×available-country cross-product at start and delete only progressive discovery (card/system/bot discover path), or (B) delete the discovery concept from ECS/UI entirely (`DiscoveredCountry`, `DiscoveredCountriesState`, map discovery checks, bot `DiscoveredCountryIds`) so “known” is implicit for every `Country` in the world?]
- [NEEDS CLARIFICATION: **Map presentation after removal** — With discovery gone, should unavailable (`IsAvailable: false`) map features that never had `Country` entities continue to render as they do today when not in `DiscoveredCountries`, or must the map applier stop consulting discovery and only use ownership/lens data for countries that exist in world state?]
- [NEEDS CLARIFICATION: **Save compatibility** — On load of a pre-change save with a partial per-org discovery set, should the game (A) auto-complete discovery for all available countries for every org on load, (B) leave historical `DiscoveredCountry` entities as-is (breaking the “no undiscovered state” rule for old saves only), (C) reject/migrate the save with an explicit version bump, or (D) something else?]
- [NEEDS CLARIFICATION: **Default bot replacement** — After removing `discoverAndControl`, should default `game_settings.json` `botFeatures` switch to enabled `baselineCardPlay` only, keep a renamed control-focused feature with no discover parameters, or require a new minimal feature id before this ships?]
- [NEEDS CLARIFICATION: **`Docs/BotFeatures/discoverAndControl/`** — Archive/delete the eval config/summary/history as obsolete with this feature, or leave historical eval docs in place unused?]
- [NEEDS CLARIFICATION: **Debug Discover All** — Remove `DebugDiscoverAllCountriesCommand`, HUD button, and CalibrationRunner push entirely, or keep a no-op command for script compatibility?]
- [NEEDS CLARIFICATION: **Game log / locale cleanup** — Strip `GameLogEntryKind.Discovery`, discovery formatters, and locale keys in the same change, or leave dormant enum/locale entries for older log display?]
- [NEEDS CLARIFICATION: **Empty org pools** — After removing `discover_country`, Illuminati/Masons `orgPools` in `action_config.json` become empty; should those pool entries be deleted (orgs use only shared/default org actions if any remain), or must a replacement org-level card be introduced in this same feature so org hands are non-empty?]
