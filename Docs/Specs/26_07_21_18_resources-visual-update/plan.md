# Plan: Resources Visual Update

## Spec

Source: `Docs/Specs/26_07_21_18_resources-visual-update/spec.md`.

**Intent.** Limit the shared resource summaries used by the selected-country panel and both player-organization views to a config-ordered whitelist, give every displayed resource a localized definition and recognizable image, and render the visible subset on one horizontal line without changing resource calculation, ownership, persistence, or update behavior.

## Goal

Make `ResourcesView` a presentation-catalog consumer: the config determines which resource IDs are eligible, their order, localization keys, and icon key; the current owner's `CountryResourcesState` determines which of those eligible resources are actually present. Country summaries will therefore show `gold`, `country_population`, and `country_score`, while organization summaries show `gold` and `org_score`.

The implementation must also separate presentation catalog entries from country initialization. Today every `ResourceConfig.Resources` entry is automatically seeded onto every country, so adding the three requested definitions naively would duplicate collector-owned country resources and incorrectly add `org_score` to countries. A small explicit seeding flag preserves existing runtime ownership while allowing the config to describe presentation-only resources.

## Approach

### 1. Separate presentation definitions from country seeding

Extend `src/Game.Configs/ResourceConfig.cs`:

- Add `DisplayWhitelist`, a `List<string>` whose order is the display order.
- Add `SeedForCountries` to `ResourceDefinition`, defaulting to `true` for backward compatibility with existing programmatic configs and the current gold behavior.
- Keep `FindResource` as the catalog lookup used by tooltips and icon selection.

Update `src/Game.Main/InitSystem.cs` so `CreateResourceEntities` skips definitions whose `SeedForCountries` value is `false`. This is a preservation guard, not a new gameplay rule: collector-owned `country_population` and `country_score` continue to be created by `CreateCollectorDrivenCountryResource`, and `org_score` continues to be created only by `CreateOrgScoreEntities`.

### 2. Populate the resource presentation config

Update `Assets/Configs/resource_config.json` with:

- `displayWhitelist` in this exact order: `gold`, `country_population`, `country_score`, `org_score`.
- Complete definitions for all four IDs.
- `seedForCountries: true` for `gold`; `false` for `country_population`, `country_score`, and `org_score`.
- Stable icon keys: `coin`, `country-population`, `country-score`, and `org-score`.
- Existing gold defaults/effects unchanged; presentation-only definitions have no default effects and do not participate in initialization.

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

- [ ] **Add presentation-order and seed metadata** — update `src/Game.Configs/ResourceConfig.cs` with `DisplayWhitelist` and backward-compatible `ResourceDefinition.SeedForCountries`.
- [ ] **Protect runtime resource ownership** — update `src/Game.Main/InitSystem.cs` to seed only definitions enabled for country initialization.
- [ ] **Configure the four displayed resources** — update `Assets/Configs/resource_config.json` with the exact whitelist, complete definitions, icon keys, and seed flags while preserving gold's current defaults/effects.
- [ ] **Add localized names and descriptions** — update `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` for `country_population`, `country_score`, and `org_score`.
- [ ] **Implement whitelist filtering, ordering, and icon selection** — update `Assets/Scripts/Unity/UI/ResourcesView.cs`, including absent-entry omission, missing-image fallback, localized resource descriptions, and preserved tooltip details.
- [ ] **Generate and import the three resource images** — add the three transparent PNGs and Unity metadata under `Assets/UI/Icons/`; keep the existing coin SVG.
- [ ] **Render resource items as one row** — update `Assets/UI/Shared/SharedStyles.uss` with the horizontal container and icon classes.
- [ ] **Add initialization regression tests** — extend `src/Game.Tests/InitSystemTests.cs` (or a focused adjacent test file) to assert that presentation-only definitions do not create duplicate country resources, countries do not receive `org_score`, organization `org_score` remains singular, and default `SeedForCountries == true` preserves programmatic-config compatibility.
- [ ] **Run core verification** — run the focused initialization tests, then `dotnet test src/GlobalStrategy.sln`.
- [ ] **Run Unity verification** — refresh/import in Unity, confirm the new sprites and USS URLs resolve, and read the console for compile/import/UXML/USS errors.
- [ ] **Perform visual verification** — in Play mode inspect all three affected views in both locales and at representative HUD widths; verify resource subset/order, single-line layout, icon legibility, live refresh, missing-entry behavior, and tooltip content.

### User Steps

1. In the Unity Editor, open the `Map` scene and enter Play mode. Select a country and confirm the resource line shows `gold`, `country_population`, and `country_score` in that order. Confirm the compact player-organization HUD and organization information overlay show `gold` followed by `org_score`, with no `recruits`, province `population`, or wrong-owner score.
2. Visually confirm the three new images are distinct and recognizable at their displayed size, the existing gold coin is unchanged, and every affected summary remains one horizontal line without overlap or clipping at the supported game-window widths.
3. Hover every displayed item and confirm its localized name and description appear, while gold retains its income/effect details. Change between English and Russian and confirm the views refresh without duplicates or stale tooltip text.
4. Exercise a resource change (or advance simulation time) and confirm values update without reordering. If practical, use a state where one whitelisted resource is absent and confirm it is omitted cleanly rather than rendered as a zero placeholder or empty icon.

## Tests

### Automated

- A resource definition with `SeedForCountries == false` is not created by the generic country initializer.
- Adding presentation definitions for `country_population` and `country_score` leaves exactly one collector-owned resource of each ID per country.
- Adding the `org_score` presentation definition creates no country-owned `org_score`; participating organizations still receive exactly one org-owned `org_score` through the existing path.
- Omitting `seedForCountries` from an in-memory or deserialized legacy definition retains the current `true` behavior.
- Full `src/GlobalStrategy.sln` tests remain green, guarding resource initialization, collectors, save/load, and visual-state projections.

### Unity / visual

- Unity imports all three PNGs as UI sprites and reports no C#, UXML, USS, or missing-asset errors.
- Selected country: `gold`, `country_population`, `country_score`, in config order.
- Player organization in both views: `gold`, `org_score`, in config order.
- Absent whitelist entries are omitted; unrelated runtime resources never render.
- Items stay on one horizontal line with distinct spacing and legible 22 px images.
- Existing live resource and locale refresh paths rebuild cleanly without duplicate/stale elements.
- Tooltips preserve effect/control-income details and show localized resource names/descriptions.

## Constitution Check

Checked against `Docs/Constitution.md`; no violations found.

- **Rendering:** No render-pipeline changes; the feature uses existing UI Toolkit sprite/background-image support under URP.
- **Game logic:** No calculation, balance, ownership, or update rule moves into Unity. The only `src/` behavior change prevents presentation-only catalog entries from entering generic country initialization; collector and organization creation paths remain authoritative.
- **Dependency injection:** Existing injected `ResourceConfig`, localization, state, and tooltip dependencies are reused. No singleton, service locator, or new composition-root wiring is introduced.
- **UI:** All presentation changes remain in the existing UI Toolkit `ResourcesView` and shared USS; no Canvas/uGUI assets are added.
- **Planning discipline:** This plan follows the approved specification and precedes implementation.
- **File organization / assemblies:** Files stay in existing `Game.Configs`, `Game.Main`, `Game.Tests`, `Assets/Scripts/Unity/UI`, `Assets/UI/Icons`, localization, config, and shared UI folders; no new feature assembly or cross-folder asmdef is needed.
- **C# style:** Implementation will use tabs, same-line braces, `_`-prefixed private fields, and braced control flow.

Use the issue approval checkpoint before implementation, or request plan clarifications on issue #41.
