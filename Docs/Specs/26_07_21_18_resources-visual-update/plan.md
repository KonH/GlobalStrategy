# Plan: Resources Visual Update

## Spec

Source: `Docs/Specs/26_07_21_18_resources-visual-update/spec.md`.

**Intent.** Limit the shared resource summaries used by the selected-country panel and both player-organization views to a config-ordered whitelist, give every displayed resource a localized definition and recognizable image, and render the visible subset on one horizontal line. Resource initialization is reorganized around config-declared seed targets, while preserving the existing resource values, effects, ownership, persistence, and update behavior.

## Goal

Make `ResourcesView` a presentation-catalog consumer: the config determines which resource IDs are eligible, their order, localization keys, and icon key; the current owner's `CountryResourcesState` determines which of those eligible resources are actually present. Country summaries will therefore show `gold`, `country_population`, and `country_score`, while organization summaries show `gold` and `org_score`.

The implementation must also separate resource catalog entries by owner type. Today every `ResourceConfig.Resources` entry is automatically seeded onto every country, so adding the requested definitions naively would duplicate collector-owned country resources and incorrectly add `org_score` to countries. Each static resource definition will instead declare a `SeedTarget` (`Character`, `Province`, `Country`, or `Org`), and initialization will consult that target wherever the resource has a config-backed initialization path. Target-specific configs remain authoritative for actual starting values and collector setup.

## Approach

### 1. Make resource initialization target-aware

Extend `src/Game.Configs/ResourceConfig.cs`:

- Add `DisplayWhitelist`, a `List<string>` whose order is the display order.
- Add a `ResourceSeedTarget` enum with exactly `Character`, `Province`, `Country`, and `Org`.
- Add `SeedTarget` to `ResourceDefinition`, defaulting to `Country` for backward compatibility with existing programmatic configs and the current gold behavior.
- Add a target-filtered lookup/enumeration helper so initialization code does not repeat target predicates.
- Keep `FindResource` as the catalog lookup used by tooltips and icon selection.

Serialize `seedTarget` as the readable enum name in JSON. Add `JsonStringEnumConverter` to the shared System.Text.Json options in `src/Core.Configs.IO/FileConfig.cs`; Unity's Newtonsoft loader already accepts named enum values. Verify both loaders so the headless and Unity config paths interpret the same file identically.

Update `src/Game.Main/InitSystem.cs` to use the target-filtered definitions in each config-backed initialization path:

- `Country`: create `gold`, `country_population`, `country_score`, and `recruits` only for available countries. `CountryEntry.InitialResources` continues to override configured defaults. Refactor the three collector-backed helpers to attach their existing effects/collectors without creating a second `Resource`.
- `Province`: create `population` only for provinces, taking its value from `ProvinceEntry.Population`, then attach the existing monthly population-growth collector effect.
- `Org`: create `org_score` only for participating organizations, then attach the existing instant/daily score collector effects.
- `Character`: create the four configured skill resources for both country and organization characters, while continuing to take random ranges and fallback values from `CharacterConfig`/`CharacterEntry`. Share this target-aware skill-resource helper between both character creation paths.

Two dynamic/specialized cases remain explicit and are documented in code: organization `gold` keeps using `OrganizationEntry.InitialGold` (the gold definition's `Country` target describes its generic config seeding and default country effects), and runtime `opinion_<orgId>` resources remain action/load-created because their IDs cannot be enumerated statically in `resource_config.json`.

Fail fast with the resource ID and target when a statically configured target/resource pairing has no supported initialization strategy. This prevents a new target entry from being silently ignored or seeded with the wrong owner/value source.

### 2. Populate the resource presentation config

Update `Assets/Configs/resource_config.json` with:

- `displayWhitelist` in this exact order: `gold`, `country_population`, `country_score`, `org_score`.
- Complete definitions for all four displayed IDs.
- Explicit `seedTarget` values for every static initialized resource: `Country` for `gold`, `country_population`, `country_score`, and `recruits`; `Province` for `population`; `Org` for `org_score`; and `Character` for `power`, `charm`, `stinginess`, and `intrigue`.
- Stable icon keys: `coin`, `country-population`, `country-score`, and `org-score`.
- Existing gold defaults/effects unchanged. Collector-backed definitions use zero defaults and no generic default effects; their current target-specific initialization paths attach the same collector effects as before.
- Character skill IDs remain aligned with `CharacterConfig`; their localization, icons, and random ranges stay authoritative there, while their resource definitions supply the target metadata used by initialization.

Add `resource.<id>.name` and `resource.<id>.description` entries for the three new resources to both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`. Keep the existing gold strings unchanged.

### 3. Filter and order the shared resource view

Update `Assets/Scripts/Unity/UI/ResourcesView.cs` so `Refresh`:

1. Clears the container and returns for an invalid state as it does now.
2. Iterates `ResourceConfig.DisplayWhitelist`, not the runtime list.
3. Finds the matching `ResourceStateEntry` in the current state and omits the whitelist entry when absent.
4. Builds each item in whitelist order, applying a common `resource-icon` class and `resource-icon--<configured icon key>` when a definition and icon key exist.
5. Continues rendering the numeric value and registering the existing tooltip even if the definition or optional image is missing.

Retain the existing gold integer formatting and other-resource whole-number display. Use `ResourceDefinitions.Gold` instead of adding another gold ID literal.

When building the tooltip, keep all effect, instant, and control-income detail behavior. Add the configured localized resource description immediately below the existing localized resource-name header when the definition has a description key; a missing definition continues to fall back to the raw ID without throwing.

No caller-specific filter is needed in `CountryInfoView`, `PlayerOrgView`, or `OrgInfoDocument`: each already passes an owner-specific `CountryResourcesState` to the same `ResourcesView`, and the absent-entry rule naturally selects the appropriate subset.

### 4. Add generated resource images and shared horizontal styling

Generate three square transparent-background PNG images under `Assets/UI/Icons/`:

- `resource-country-population.png`: a compact crowd/population symbol.
- `resource-country-score.png`: a country-focused achievement/score symbol.
- `resource-org-score.png`: an organization/network achievement symbol visually distinct from country score.

Use a consistent strategy-game HUD style, strong silhouettes, and minimal fine detail so each remains legible at 22 px. Do not replace or regenerate `Assets/UI/Icons/coin.svg`.

Import the new PNGs as single UI sprites with alpha, mipmaps disabled, clamp wrapping, and their generated `.meta` files committed. Update `Assets/UI/Shared/SharedStyles.uss`:

- Add `.resources-container` as a single non-wrapping horizontal flex row with centered items and spacing between items.
- Move common 22 px sizing/scaling to `.resource-icon`.
- Keep `.resource-icon--coin` mapped to the existing coin SVG and add one background-image class per generated PNG.
- Remove vertical-only row spacing from `.resource-row`; use container `gap` so the first item remains aligned with the container edge.

Because the three affected UXML documents already use `resources-container`, import `SharedStyles.uss`, and instantiate `ResourcesView`, no UXML, scene, prefab, or DI changes are required.

## Steps

### Agent Steps

- [x] **Add presentation-order and target metadata** — update `src/Game.Configs/ResourceConfig.cs` with `DisplayWhitelist`, `ResourceSeedTarget`, backward-compatible `ResourceDefinition.SeedTarget`, and target-filtered lookup; enable named-enum loading in `src/Core.Configs.IO/FileConfig.cs`.
- [x] **Route config-backed initialization by target** — update `src/Game.Main/InitSystem.cs` so country, province, organization, and character resource paths consume only their target's definitions, reuse existing target-specific value sources/collectors, and never create duplicate resources; retain explicit organization-gold and dynamic-opinion exceptions.
- [x] **Configure static resource targets and the display catalog** — update `Assets/Configs/resource_config.json` with the exact whitelist, complete displayed definitions, icon keys, and explicit targets for gold, population, country population/score, recruits, org score, and character skills while preserving current values/effects.
- [x] **Add localized names and descriptions** — update `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` for `country_population`, `country_score`, and `org_score`.
- [x] **Implement whitelist filtering, ordering, and icon selection** — update `Assets/Scripts/Unity/UI/ResourcesView.cs`, including absent-entry omission, missing-image fallback, localized resource descriptions, and preserved tooltip details.
- [x] **Generate and import the three resource images** — add the three transparent PNGs and Unity metadata under `Assets/UI/Icons/`; keep the existing coin SVG.
- [x] **Render resource items as one row** — update `Assets/UI/Shared/SharedStyles.uss` with the horizontal container and icon classes.
- [x] **Add initialization regression tests** — extend `src/Game.Tests/InitSystemTests.cs` (or a focused adjacent test file) to assert target-correct ownership and singular creation across countries, provinces, organizations, and both character sources; preserve organization-gold values, target-specific collector effects, and default `SeedTarget == Country` compatibility.
- [x] **Run core verification** — run the focused initialization tests, then `dotnet test src/GlobalStrategy.Core.sln`.
- [x] **Run Unity verification** — refresh/import in Unity, confirm the new sprites and USS URLs resolve, and read the console for compile/import/UXML/USS errors.
- [ ] **Perform visual verification** — in Play mode inspect all three affected views in both locales and at representative HUD widths; verify resource subset/order, single-line layout, icon legibility, live refresh, missing-entry behavior, and tooltip content.

### User Steps

1. In the Unity Editor, open the `Map` scene and enter Play mode. Select a country and confirm the resource line shows `gold`, `country_population`, and `country_score` in that order. Confirm the compact player-organization HUD and organization information overlay show `gold` followed by `org_score`, with no `recruits`, province `population`, or wrong-owner score.
2. Visually confirm the three new images are distinct and recognizable at their displayed size, the existing gold coin is unchanged, and every affected summary remains one horizontal line without overlap or clipping at the supported game-window widths.
3. Hover every displayed item and confirm its localized name and description appear, while gold retains its income/effect details. Change between English and Russian and confirm the views refresh without duplicates or stale tooltip text.
4. Exercise a resource change (or advance simulation time) and confirm values update without reordering. If practical, use a state where one whitelisted resource is absent and confirm it is omitted cleanly rather than rendered as a zero placeholder or empty icon.

## Tests

### Automated

- Named `seedTarget` values deserialize identically through the headless System.Text.Json config path and Unity's Newtonsoft config path.
- Each configured static definition is created only for its declared target: country resources never appear on provinces/orgs/characters, province population remains province-owned, org score remains org-owned, and character skills remain character-owned.
- `country_population`, `country_score`, `recruits`, `population`, and `org_score` each remain singular per applicable owner and retain their existing collector effects; no generic-plus-specialized duplicate is created.
- Organization gold retains `OrganizationEntry.InitialGold` and its existing effect shape even though the shared gold catalog entry is country-targeted; dynamic opinion resources remain unaffected.
- Omitting `seedTarget` from an in-memory or deserialized legacy definition retains the current country-seeding behavior.
- Unsupported static target/resource pairings fail with a contextual error instead of being silently ignored.
- Run the full `src/GlobalStrategy.Core.sln` suite to guard resource initialization, collectors, save/load, and visual-state projections.

### Unity / visual

- Unity imports all three PNGs as UI sprites and reports no C#, UXML, USS, or missing-asset errors.
- Selected country: `gold`, `country_population`, `country_score`, in config order.
- Player organization in both views: `gold`, `org_score`, in config order.
- Absent whitelist entries are omitted; unrelated runtime resources never render.
- Items stay on one horizontal line with distinct spacing and legible 22 px images.
- Existing live resource and locale refresh paths rebuild cleanly without duplicate/stale elements.
- Tooltips preserve effect/control-income details and show localized resource names/descriptions.

## Verification Notes

- Target-aware initialization regression tests pass 7/7; the broader focused initialization/configuration/character set passes 45/45.
- The full core run passes 378 tests and has one reproducible unrelated failure in `DiscoverAndControlFeatureTests.plays_control_card_over_discover_card_once_threshold_is_met`.
- Unity refresh, sprite import, compilation, and targeted console checks pass. Manual Play-mode verification remains a user step.

## Constitution Check

Checked against `Docs/Constitution.md`; no violations found.

- **Rendering:** No render-pipeline changes; the feature uses existing UI Toolkit sprite/background-image support under URP.
- **Game logic:** No calculation, balance, ownership, or update rule moves into Unity. Initialization dispatch becomes config-target-aware in `src/`, while country/province/organization/character configs and the existing collector paths remain authoritative for values and effects.
- **Dependency injection:** Existing injected `ResourceConfig`, localization, state, and tooltip dependencies are reused. No singleton, service locator, or new composition-root wiring is introduced.
- **UI:** All presentation changes remain in the existing UI Toolkit `ResourcesView` and shared USS; no Canvas/uGUI assets are added.
- **Planning discipline:** This plan follows the approved specification and precedes implementation.
- **File organization / assemblies:** Files stay in existing `Game.Configs`, `Core.Configs.IO`, `Game.Main`, `Game.Tests`, `Assets/Scripts/Unity/UI`, `Assets/UI/Icons`, localization, config, and shared UI folders; no new feature assembly or cross-folder asmdef is needed.
- **C# style:** Implementation will use tabs, same-line braces, `_`-prefixed private fields, and braced control flow.

Use the issue approval checkpoint before implementation, or request plan clarifications on issue #41.

## Automation Notes

The `full-env-headless` pass skips these plan steps because they require a Unity Editor, Unity MCP, image generation, asset import, or visual inspection:

- [ ] **Add localized names and descriptions** — update `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` for `country_population`, `country_score`, and `org_score`.
- [ ] **Generate and import the three resource images** — add the three transparent PNGs and Unity metadata under `Assets/UI/Icons/`; keep the existing coin SVG.
- [ ] **Render resource items as one row** — update `Assets/UI/Shared/SharedStyles.uss` with the horizontal container and icon classes.
- [ ] **Run Unity verification** — refresh/import in Unity, confirm the new sprites and USS URLs resolve, and read the console for compile/import/UXML/USS errors.
- [ ] **Perform visual verification** — in Play mode inspect all three affected views in both locales and at representative HUD widths; verify resource subset/order, single-line layout, icon legibility, live refresh, missing-entry behavior, and tooltip content.
