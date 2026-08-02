# Plan: Drop Discovery Concept

## Spec

Full spec: `Docs/Specs/26_08_02_13_drop-discovery-concept/spec.md` (issue #111).

Remove the discovery fog layer entirely: every world/`IsAvailable` country is implicitly known for every participating org at start; there is no discover-country card, progressive reveal, discovery ECS, discovery game-log, or discovery debug cheat. Unavailable countries stay hidden on the map via a playable/world-country visibility gate. Default bot feature renames `discoverAndControl` → `control` (control-only). No save migration for pre-change discovery saves.

Owner-resolved decisions (encoded; do not re-ask): full ECS/UI removal; unavailable stay hidden; no save compat; bot id `control`; move `Docs/BotFeatures/discoverAndControl/` → `control/`; remove Discover All debug/calibration push; strip game-log Discovery + locales + card art in the same change; delete empty Illuminati/Masons `orgPools` entries with no replacement org card.

## Goal

Delete discovery end-to-end (components, systems, config, UI, bots, debug, tests, docs) and replace map visibility gating so available countries render and unavailable ones remain hidden, with default bots still able to play control cards across all world countries.

## Approach

**ECS / simulation — delete discovery.** Remove `DiscoveredCountry` (`src/Game.Components/DiscoveredCountry.cs`), `DiscoverCountryEffect` (in `ResourceChangeEffect.cs`), `DiscoveryApplied` (`GameLogEffects.cs`), `DiscoverCountrySystem`, `InitSystem.DiscoverInitialCountries` + call site, `CreateActionEffectSystem`’s `DiscoverCountryEffectParams` branch, and cleanup sweeps for `DiscoverCountryEffect` / `DiscoveryApplied`. Drop `GameLogic`’s `DiscoverCountrySystem.Update` call and `ApplyDebugDiscoverAllCountries`. No migration / version bump for old discovery saves — loaders simply no longer expect those components.

**Config / locale / art.** Delete `discover_country` action + Illuminati/Masons `orgPools` entries from `action_config.json` (leave `orgPools: []` or omit emptied entries — no replacement card). Delete `discover_country` / `DiscoverCountry` from `effect_config.json`. Remove `DiscoverCountryEffectParams` and the converter arm in `EffectConfig.cs`. Strip locale keys (`action.discover_country.*`, `effect.discover_country.*`, `hud.discovery.confirmation`, `game_log.discovered_format`) from `en.asset` / `ru.asset`. Remove the `discover_country` row from `ActionVisualConfig.asset`; if its front-image texture GUID is unused elsewhere, delete that texture asset too.

**VisualState / map gate.** Delete `DiscoveredCountriesState` / `RecentlyDiscovered`, `VisualStateConverter.UpdateDiscoveredCountries`, and Discovery game-log emission (`GameLogEntryKind.Discovery`, `DiscoveryApplied` scan). Strip `CardPlayAnimator` discovery pan / fly-text / `ClearRecentlyDiscovered`. Mirror the game-log Discovery strip in `src/Game.WebClient/Components/ActionsLog.razor` (same enum/locale as Unity). Prefer an explicit **`WorldCountriesState`** (HashSet of world/`IsAvailable` country ids from `GetCountryIds`) that only `Set`s when the key set changes; wire `MapLensApplier` to `Contains` that set and subscribe to its `PropertyChanged` (same role as today’s `DiscoveredCountries` subscription). Do **not** gate on raw `CountryScore` without a key-set-aware refresh — `UpdateCountryScore` currently runs after ownership/occupation, so relying on those handlers alone can leave the Political map blank after the first VisualState populate. Update `.claude/rules/unity/map_system.md` accordingly.

**Bots.** Rename `DiscoverAndControlFeature` → `ControlFeature` (`Id = "control"`): drop `TryPlayDiscover`, `discoveredCountriesAvailableControl`, and any discover-first ordering; `Tick` only tries control plays. Update `BotFeatureRegistry`, `game_settings.json`, `GameSettings` defaults. In `BotObservation` / `IBotObservation` / `BotViews`: remove `DiscoveredCountryIds`, `DiscoversCountry`, discovery filtering on countries/hands/characters/control, and `DiscoverCountryEffectParams` classification. Move `Docs/BotFeatures/discoverAndControl/` → `Docs/BotFeatures/control/`; rewrite `eval_config.json` for id `control` with empty parameters (drop discovery parameter search); treat prior eval history as obsolete for the new id (reset or clearly mark superseded — implementer choice, prefer clean `eval_history` for `control`).

**Debug / calibration.** Delete `DebugDiscoverAllCountriesCommand`, HUD button / `PushDiscoverAllCountriesCommand` in `HUDDocument`, `GameLogic` apply path, `CalibrationRunner` push, and references in `.claude/skills/end-game-score-calibration/SKILL.md`. CommandAccessor regenerates from remaining `ICommand` types.

**Tests / benchmarks.** Delete discovery-dedicated suites (`DiscoveryPerOrgTests`, `SavableDiscoveryTests`); rename/rewrite `DiscoverAndControlFeatureTests` → `ControlFeatureTests`. Strip discovery seeding/assertions from `BotSessionTests`, `BotCommandSinkTests`, `MultiOrgInitTests`, `UnifiedPipelineTests`, `GameLogStateTests`, `BaselineCardPlayTests`, `BotObservationTests`, `VisualStateChangeNotificationTests`, `SaveLoadRoundTripTests`, `MultiOrgTestSupport` effect fixtures, and `DictionaryAndSetVisualStateSetBenchmarks`’ `DiscoveredCountriesState_*` cases. Retarget any harness that used `discover_country` as a sample action id (e.g. `EvalCommandAssertionTests`) to a still-shipped action. Retarget `LocaleAssetParserTests.unquotes_double_quoted_value` off `game_log.discovered_format` to another remaining double-quoted `en.asset` value.

## Steps

### Section 1 — Agent Steps

- [ ] **Delete discovery ECS surface** — remove `DiscoveredCountry.cs`; remove `DiscoverCountryEffect` / `DiscoveryApplied`; delete `DiscoverCountrySystem.cs`; strip `CreateActionEffectSystem` / `CleanupActionEffectsSystem` / `CleanupEffectNotificationsSystem` discovery branches; remove `InitSystem.DiscoverInitialCountries` + call; remove `GameLogic` discovery system call + `ApplyDebugDiscoverAllCountries` + debug-command read.
- [ ] **Config + types** — delete discover action/effect JSON; delete Illuminati/Masons `orgPools` entries; remove `DiscoverCountryEffectParams` + converter arm; strip locales; remove `ActionVisualConfig` entry (+ orphaned texture if unused).
- [ ] **VisualState / presentation** — delete `DiscoveredCountriesState` and converter/update/game-log Discovery path; remove `GameLogEntryKind.Discovery` + `GameLogLineFormatter.BuildDiscoveryLine` + Unity `ActionLogView` branch **and** web `ActionsLog.razor` Discovery arm/`BuildDiscoveryLine`; strip `CardPlayAnimator` discovery UX.
- [ ] **MapLensApplier world-country gate** — add `WorldCountriesState` (world/`IsAvailable` ids from `GetCountryIds`, set-on-key-change only); replace `IsCountryDiscovered` / `DiscoveredCountries` subscription with `Contains` + `PropertyChanged` so first VisualState populate repaints Political/Org lenses; update `map_system.md`.
- [ ] **Bot rename to `control`** — `ControlFeature`, registry, settings defaults/`game_settings.json`; strip observation discovery fields/filters/`DiscoversCountry`; move `Docs/BotFeatures/` folder and rewrite eval config.
- [ ] **Debug / calibration cleanup** — delete command type, HUD wiring, CalibrationRunner push, end-game calibration skill mention.
- [ ] **Tests + benchmarks** — delete or rewrite suites listed in Approach; remove all `DiscoveredCountry` seeding; ensure `ControlFeature` coverage (control-only play, no discover path); update benchmarks that referenced `DiscoveredCountriesState`.
- [ ] **Build + test** — `dotnet-build` / `dotnet-test` green; no remaining references to discovery types/ids except historical Docs/Specs (leave old specs alone).

### Section 2 — User Steps

1. **Map visibility smoke in Editor** — Enter Play on `Map` with a normal org selection. Confirm every `IsAvailable` country’s provinces are visible under Political/Org/Province lenses at start (no fog waiting on discovery), and that provinces belonging only to `IsAvailable: false` countries remain hidden. Confirm org hand has no Discover Country card and the debug HUD has no Discover All Countries button.

## Tests

- Delete: `DiscoveryPerOrgTests`, `SavableDiscoveryTests`.
- Rewrite/rename: `DiscoverAndControlFeatureTests` → control-only cases (play control when eligible; no discover card path; ignore removed params).
- Update: `BotSessionTests` (no discover-count / `DiscoverAndControlFeature.Id`), `BotCommandSinkTests` (replace Discover/`DiscoveredCountry` assertion with a non-discovery dual-play check, e.g. two distinct spend/control outcomes), `MultiOrgInitTests` (no HQ-only discovery assert — assert no `DiscoveredCountry` archetypes / or simply drop that assert), `UnifiedPipelineTests` / `GameLogStateTests` (no `DiscoveryApplied` / `GameLogEntryKind.Discovery`), `BaselineCardPlayTests` / `BotObservationTests` (no discovery seed; all world countries visible to observation; no `DiscoversCountry`), `VisualStateChangeNotificationTests` (drop `DiscoveredCountriesState` cases), `SaveLoadRoundTripTests` (drop discovery round-trip), `MultiOrgTestSupport` (remove discover effect fixture; keep a playable org sample action for determinism/sink tests), benchmarks `DiscoveredCountriesState_*`, `LocaleAssetParserTests` (new double-quoted locale sample).
- Retarget sample action ids in eval assertion tests away from `discover_country`.
- No new save-migration tests (owner: no compatibility).

## Constitution Check

No conflicts found — plan aligns with all principles.

Detail, principle by principle (`Docs/Constitution.md`):

- **Rendering (URP)** — not applicable; no render-pipeline change (map visibility toggles existing MeshRenderers only).
- **Game Logic (ECS in `src/`)** — satisfied: discovery removal and remaining gates stay in `src/` systems/components; MonoBehaviours stay presentation glue (`MapLensApplier`, HUD, animator).
- **VContainer** — satisfied: no new service location patterns; only delete debug/HUD wiring.
- **UI Toolkit** — satisfied: HUD/debug button and action-log branches are UI Toolkit / existing views; no Canvas/uGUI.
- **Planning / Specification / File Organisation** — satisfied: spec exists; this plan lives at `Docs/Specs/26_08_02_13_drop-discovery-concept/plan.md`.
- **Assembly Structure / C# Code Style** — satisfied: no new asmdefs; edits follow existing style.

**Plan-review fixes applied:** (1) map gate uses explicit `WorldCountriesState` + PropertyChanged (avoids blank Political map on first populate). (2) web `ActionsLog.razor` Discovery strip included. (3) `BotCommandSinkTests` + `LocaleAssetParserTests` added to test inventory.

**Accepted residual risks:** empty org pools (bots = country cards only); pre-change saves unsupported; bot eval history reset under `Docs/BotFeatures/control/` is implementer discretion.

Use the implement skill to start working on the plan or request changes.
