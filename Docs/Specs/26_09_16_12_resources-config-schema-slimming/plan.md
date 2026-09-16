# Plan: Resource Config Schema Slimming

## Goal

Remove `nameKey`, `descriptionKey` and `icon` from `ResourceDefinition`, and `nameKey` / `descriptionKey` from the resource-attached `EffectDefinition`, deriving all three from the entity's own identifier by a fixed convention. Rename resource artwork to match the derived convention, unify every icon reference (config-driven, gallery, and hardcoded), and add build-time coverage tests for missing text/artwork scoped to `displayWhitelist`.

## Spec

Source: `Docs/Specs/26_09_16_12_resources-config-schema-slimming/spec.md` (approved).

**Intent.** A resource entry — and each recurring effect attached to it — carries only data genuinely unique to it. Display name, description and icon are derived from the identifier by a fixed naming convention, so adding or renaming a resource means editing one entry instead of several parallel bookkeeping fields that can drift.

**Acceptance criteria (verbatim summary).**

- *A resource is defined in the resource configuration* — the entry contains no display-text or icon fields (they are derived from the identifier); it still carries behavioural data (seed target, starting value, bounds, history recording, default effects); an entry written in the old shape simply has those fields ignored — no compatibility path, no per-entry override, no dedicated validation machinery.
- *A recurring effect is defined alongside a resource* — it carries no display-text fields; its name/description derive from the effect identifier by the same convention; the resource breakdown tooltip shows each contributing effect's derived translated name and description; the war progress screen shows each listed effect's derived name and description *(see **Spec correction** below — this bullet does not apply to `ResourceConfig.EffectDefinition`)*; an effect with no translated text shows its raw identifier and is listed in the startup missing-text report, never blocking display.
- *A player-visible resource with text and artwork available* — resource display, hover tooltip, and task-reward label all show exactly the same name/description/icon as before this change.
- *A player-visible resource missing text or artwork* — missing text falls back to the raw identifier exactly as today; missing artwork renders a blank, correctly-sized icon slot rather than a broken image; each gap is reported once, never as a repeated per-frame warning *(implemented as a build-time test rather than a runtime report — see Approach)*.
- *A never-displayed resource (war progress, war initiative, combat bonus percentages, character skills, damage, durability)* — produces no missing-text/artwork report (the report is scoped by `displayWhitelist`); its entry needs no extra bookkeeping and no "hidden" marker; translated text entries that existed only for these resources are removed.
- *A resource icon shown anywhere (live display, developer component gallery, war result summary)* — the currency resource shows the same icon in the live display and the gallery, because both derive it identically; every icon reference, including ones that pick an icon directly rather than through config, follows the new convention with no old names left; replacing a resource's artwork changes only the artwork asset.
- *A multi-word resource identifier* — the identifier is used verbatim for its text keys, its icon name, and its artwork file, with no case or separator transformation; where the presentation layer's own naming style differs, the identifier still wins, as a deliberate documented exception; artwork that does not already match is renamed outright with every reference updated, with no duplicate kept under the old name.
- *The province-scoped `population` resource* — shows its own translated name/description and its own artwork rather than borrowing the country-level resource's; the new entries exist in both languages with a real translation even though the English text duplicates the country-level wording.
- *A designer adds a brand-new resource* — supplying text and artwork under the convention is enough for correct display with no further configuration; omitting them still runs correctly, falling back to the raw identifier and a blank icon, with both gaps caught by the coverage tests.

**Success criteria.** No entry restates display text or artwork naming; every currently-visible resource and effect looks identical before and after; currency icon matches between live display and gallery; no pre-change icon name survives anywhere; artwork replacement is a one-asset change; coverage of missing text/artwork is reported once for whitelisted resources with no per-frame spam; every new locale key exists in both languages with a real translation; no unread locale entry remains.

**Out of scope.** Changing which resources are visible or their order; changing any resource/effect behaviour; redesigning visual style; creating new artwork for resources that have none (including damage/durability); supporting the pre-change config shape; changing other config files with a similar shape.

## Approach

**Spec correction — the war progress screen is not in scope.** The spec's acceptance criteria state that effect display text appears on the war progress screen. That is incorrect for this feature. `Assets/Scripts/Unity/UI/WarProgressLayoutBinder.cs` (~lines 338-356) renders `ActionEffectDefinition` from `src/Game.Configs/EffectConfig.cs`, which is backed by a *different* config file (`Assets/Configs/effects.json`) and is an entirely separate type from `ResourceConfig.EffectDefinition`. `ActionEffectDefinition` keeps its own display fields and is explicitly **out of scope** here. The only consumer of `ResourceConfig.EffectDefinition`'s display text is `Assets/Scripts/Unity/UI/ResourcesView.cs` (lines 185-191 and 226-232), i.e. the resource breakdown tooltip. The scope is therefore narrower than the spec implies: the war-progress bullet is satisfied vacuously, not by any code change.

**Second correction — `resource-country-control.png` is in use and must not be touched.** It is referenced by `Assets/UI/HUD/CountryInfo/CountryInfo.uss:11` through a GUID-bearing `url(... ?fileID=2800000&guid=21487500afe0051408fd79abd94154e3&type=3#resource-country-control)`. It is not a resource-row icon and is not part of the rename map.

**Naming convention.** One pure helper, `ResourceDisplayNaming`, lives in `src/Game.Configs` next to `ResourceConfig` so it is covered by `dotnet test` and consumed identically from Unity:

- `NameKey(resourceId)` → `resource.{resourceId}.name`
- `DescriptionKey(resourceId)` → `resource.{resourceId}.description`
- `EffectNameKey(effectId)` → `effect.{effectId}.name`
- `EffectDescriptionKey(effectId)` → `effect.{effectId}.description`
- `IconClass(resourceId)` → `resource-icon--{resourceId}`

**No identifier transformation anywhere (decided: snake_case everywhere).** The resource id is used verbatim in its locale keys, its icon class, and its artwork filename. There is no `_` → `-` conversion and no `IconName` helper — the id's own spelling is the only spelling. This keeps locale keys matching the existing `resource.country_population.name` entries and leaves resource ids, `ResourceDefinitions.cs`, `game_settings.json`'s `resourceIdUpdateOrder`, `effects.json`, save files, and all test literals completely untouched.

**Accepted inconsistency (deliberate, must be documented in the stylesheet).** The six `.resource-icon--*` classes will be the only USS selectors in the project containing an underscore — verified by grepping every `.uss` file for an underscored class (zero hits today). Likewise the new artwork filenames use underscores while the neighbouring `skill-power.png` / `skill-charm.png` in `Assets/Textures/Icons/` stay hyphenated. This was chosen over kebab-casing the ids because resource ids are `[Savable]` (`Resource.ResourceId`, `ResourceLink.ResourceId`), appear in four config files and the console runner, and would still sit beside snake/Pascal country ids (`country_name.Austria_Hungary`) — so kebab ids would move the seam rather than remove it. A comment in `SharedStyles.uss` must state this explicitly so the underscores do not read as a typo.

**Behaviour neutrality check.** All five current `nameKey`/`descriptionKey` pairs in `resources.json` already equal `resource.{resourceId}.{name|description}` except `population`, which deliberately borrows `resource.country_population.*` — the spec requires it to get its own keys. The single `EffectDefinition` in the whole file (`base_income` under `gold`) already uses `effect.base_income.name` / `.description`, so dropping its two fields is a no-op. Icons change name (`coin` → `gold`, `country-recruits` → `recruits`, etc.) but resolve to the same images after the art rename.

**Old fields are simply dropped — no validation machinery.** `FileConfig<T>` / `StringConfig<T>` call `JsonConvert.DeserializeObject` with default settings, i.e. `MissingMemberHandling.Ignore`, so once the properties are gone from the type a stale `nameKey` / `descriptionKey` / `icon` in the JSON is silently ignored. That is the intended behaviour: the config ships with the game and is edited in-repo, so there is no untrusted old-shape file to guard against. No `[JsonExtensionData]` sink, no `[OnDeserialized]` validator, no loader changes.

**Missing artwork renders blank by construction.** `ResourceChipBuilder.Bind` always applies `resource-chip-icon` (fixed `22px × 22px`, `flex-shrink: 0`, from `Assets/UI/Components/Components.uss:35-40`) before the variant class. A derived `resource-icon--x` class with no USS rule simply contributes no `background-image`, so the slot stays correctly sized and empty — no code change needed for that criterion.

**The "startup report" is a static test, not runtime machinery.** The spec asks for a single report of missing text/artwork with no per-frame spam. The originally planned approach — a Unity-side `ResourceDisplayReport` probing `resolvedStyle.backgroundImage` on throwaway `VisualElement`s — is unsound: an unresolved style and a genuinely absent one are both `default(Background)`, so it would emit false "missing artwork" warnings on cold start, which is worse than no report. There is no runtime report. Instead both halves become deterministic `dotnet test` facts over the `displayWhitelist`:

- **Text coverage** — every whitelisted id's derived `resource.{id}.name` / `.description` exists in *both* `en.asset` and `ru.asset` (and `resource.damage.*` / `resource.durability.*` are gone from both).
- **Icon coverage** — every whitelisted id has both an artwork file at `Assets/Textures/Icons/ResourceRow/{id}.png` and a matching `.resource-icon--{id}` rule in `Assets/UI/Shared/SharedStyles.uss`.

This is strictly better than the runtime probe: it runs in CI, cannot produce a false positive, needs no Unity runtime or Play-mode step, and fails at the moment a designer adds a whitelisted resource without art or text. Consequently **no `ResourceDisplayReport` class is created and `HUDDocument` is not modified at all.** Effect-text coverage stays out of the test (effect ids are not whitelist-scoped); effects rely on the raw-id fallback below.

**No per-frame warning spam.** Two changes to `CustomLocalization`: (a) call sites that look up *derived* keys (`ResourcesView`, `PlayerTasksView`) use a new `ILocalization.Has(string key)` guard instead of calling `Get()` blindly, so a legitimately absent key never warns from the render loop; (b) `Get()` keeps a `HashSet<string> _warned` so any remaining missing key warns at most once. **`_warned` must be cleared in `SetLocale`** — `_active` is swapped there, and a key present in `en` but missing in `ru` is the single most likely gap; a session-wide set would suppress exactly the warning worth having. `Has` reuses the same linear scan over `_active.Entries` and logs nothing.

## Technical Mapping

- **Entry contains no display-text/icon fields; still carries behavioural data**:
  - `src/Game.Configs/ResourceConfig.cs` — delete `ResourceDefinition.NameKey`, `.DescriptionKey`, `.Icon` (lines 27-29) and `EffectDefinition.NameKey`, `.DescriptionKey` (lines 54-55). Keep `ResourceId`, `SeedTarget`, `DefaultInitialValue`, `RecordHistory`, `MinValue`, `MaxValue`, `DefaultEffects`, `FindEffect`; keep `EffectId`, `Value`, `PayType`, `CollectorId`.
  - `Assets/Configs/resources.json` — strip `nameKey`/`descriptionKey`/`icon` from `gold`, `country_population`, `country_score`, `recruits`, `population`, `org_score`, `damage`, `durability`, and from the `base_income` effect under `gold`. 15 resource entries and `displayWhitelist` are otherwise unchanged.

- **Old-shape fields ignored, no compatibility path, no override**:
  - No code. Deleting the properties is sufficient — Newtonsoft's default `MissingMemberHandling.Ignore` drops any leftover `nameKey`/`descriptionKey`/`icon` in the JSON. No new imports, no validator, no loader change.

- **Effect name/description derived; tooltip shows derived translated text; missing text falls back to raw id**:
  - `src/Game.Configs/ResourceDisplayNaming.cs` (new) — `EffectNameKey` / `EffectDescriptionKey`.
  - `Assets/Scripts/Unity/UI/ResourcesView.cs` `BuildMonthlyEffectList` (lines 185-191) and `BuildInstantEffectList` (lines 226-232) — replace `effectDef.NameKey` / `effectDef.DescriptionKey` with `ResourceDisplayNaming.EffectNameKey(effect.EffectId)` / `EffectDescriptionKey(...)`, resolved through `Has`-guarded lookup falling back to `effect.EffectId` for the name and `null` for the description.
    - **Keep the `resDef?.FindEffect(effect.EffectId) != null` gate.** Today only effects declared in `resources.json` — i.e. `base_income` alone — resolve translated text; every other id appearing in `ResourceStateEntry.Effects` falls through to the raw id. Dropping the gate would start resolving the 13 `effect.*.name` keys that belong to `effects.json` / `ActionEffectDefinition`, changing visible tooltip text and breaking the "identical before and after" success criterion. The gate becomes a *presence* check only — the key itself is always derived, never read off the definition.
    - **`EffectDescriptionKey` derives `.description`, which is the minority spelling.** `base_income` is the only effect using `effect.{id}.description`; all 13 `effects.json` effects use `effect.{id}.desc` (`en.asset` lines 2157-2341). `.description` is correct for `ResourceConfig.EffectDefinition`, but state in the helper's XML doc comment that it is scoped to resource-attached effects and must not be reused for `ActionEffectDefinition`, or the next caller will derive the wrong key.

- **Resource display / tooltip / task reward show the same text and icon as before**:
  - `Assets/Scripts/Unity/UI/ResourcesView.cs:40-43` — `iconClass` becomes `ResourceDisplayNaming.IconClass(resource.ResourceId)`, unconditional (no `resourceDefinition != null` icon guard).
  - `Assets/Scripts/Unity/UI/ResourcesView.cs:85-88` — tooltip header/description from `ResourceDisplayNaming.NameKey/DescriptionKey(resource.ResourceId)` via the `Has`-guarded path; header falls back to the raw id, description is omitted when absent (same as today's empty-`DescriptionKey` behaviour).
  - `Assets/Scripts/Unity/UI/PlayerTasksView.cs:100` — replace `_resourceConfig.FindResource(reward.ResourceId)?.NameKey ?? reward.ResourceId` with `ResourceDisplayNaming.NameKey(reward.ResourceId)` plus the same fallback-to-raw-id behaviour. If `_resourceConfig` becomes unused in that file, drop the field and its constructor argument (and update `HUDDocument.cs:228`).

- **Missing artwork renders blank in a correctly-sized slot**:
  - No code change. `Assets/Scripts/Unity/UI/Components/ResourceChipBuilder.cs` `Bind` already re-applies `resource-chip-icon` before the variant class, and `Assets/UI/Components/Components.uss:35-40` fixes the box at 22×22.

- **Single report of missing text/artwork; no repeated per-frame warnings**:
  - Coverage reporting is a `dotnet test` fact, not runtime code — see the Tests section. No `ResourceDisplayReport` class, no `HUDDocument` change.
  - `Assets/Scripts/Unity/UI/ILocalization.cs` — add `bool Has(string key);` (one implementer, `CustomLocalization`; the unrelated `src/Game.WebClient/Services/ILocalization.cs` is a different interface and is untouched).
  - `Assets/Scripts/Unity/UI/CustomLocalization.cs` — implement `Has` (same linear scan, no logging); add a `HashSet<string> _warned` so `Get()`'s existing `Debug.LogWarning` (line 32) fires at most once per key, and **clear `_warned` in `SetLocale`** so an `en`-only key can still warn after switching to `ru`.

- **Never-displayed resources produce no report and need no marker; their orphan locale entries are removed**:
  - Report iteration is driven by `config.DisplayWhitelist` for both text and artwork, so `damage`, `durability`, `war_progress`, `war_initiative`, `troops_damage_bonus_percent`, `power`, `charm`, `stinginess`, `intrigue` are never probed.
  - `Assets/Localization/en.asset` and `ru.asset` lines 489-496 — remove `resource.damage.name/.description` and `resource.durability.name/.description`. **Verified by grep across `Assets/` and `src/`:** the only occurrences of those four keys are the two locale assets, `Assets/Configs/resources.json`, and the historical `Docs/Specs/26_07_29_16_damage-durability-at-war/plan.md`. No C# code reads them, so removal is safe.

- **Every icon reference follows the new convention; gallery and live display agree; artwork replacement is a one-asset change**:
  - Rename map (`resourceId` → derived icon name → new filename, moving both `.png` and `.png.meta` to preserve the GUID):
    | resourceId | derived icon | old file | new file |
    |---|---|---|---|
    | `gold` | `gold` | `coin.png` | `gold.png` |
    | `country_population` | `country_population` | `resource-country-population.png` | `country_population.png` |
    | `recruits` | `recruits` | `resource-country-recruits.png` | `recruits.png` |
    | `country_score` | `country_score` | `resource-country-score.png` | `country_score.png` |
    | `org_score` | `org_score` | `resource-org-score.png` | `org_score.png` |
    | `population` | `population` | *(duplicate of `country_population.png`)* | `population.png` |
  - `Assets/UI/Shared/SharedStyles.uss:285-303` — rename the five `.resource-icon--*` selectors to `--gold`, `--country_population`, `--recruits`, `--country_score`, `--org_score` and repoint each `url("project://database/Assets/Textures/Icons/ResourceRow/<new>.png")`; add a sixth rule `.resource-icon--population` pointing at `population.png`. These URLs are **plain path form with no `?fileID=&guid=` query** — keep them that way; do *not* convert them into the query form used by the `character-skill-icon--*` rules in the same file.
  - `Assets/UI/Shared/SharedStyles.uss:276-279` — update the explanatory comment: the standalone reference is now `.resource-icon--gold` (used by `WarResultWindowView`), the set has six variants, and **state explicitly that the underscores are intentional** — these classes are generated verbatim from `[Savable]` resource ids, so they deliberately break the project's otherwise-universal kebab-case class convention. Without that note the next reader will "fix" them.
  - `Assets/Scripts/Unity/UI/WarResultWindowView.cs:157` — replace the hardcoded `icon.AddToClassList("resource-icon--coin")` with `ResourceDisplayNaming.IconClass(ResourceDefinitions.Gold)`.
  - `Assets/Scripts/Unity/Gallery/ResourceChipGalleryBlock.cs:8-10,29` — change `_resources` from the icon-name list `{ "coin", "country-population", "country-recruits", "country-score", "org-score" }` to the *resource ids* from `ResourceConfig.DisplayWhitelist`, and build the class via `ResourceDisplayNaming.IconClass(resourceId)`. This removes the `gold`/`coin` divergence by construction.
    - **`GalleryDocument` has no parsed `ResourceConfig`.** It holds `[SerializeField] TextAsset _resourceConfigAsset` (line 29) and hands that raw `TextAsset` to every block that needs resource data — `HudResourcesGalleryBlock`, `ProvinceInfoGalleryBlock`, `OrgActionsGalleryBlock` and five others all take it and parse it themselves via `HudConfigLoader.LoadResourceConfig`. Follow that convention: change line 246 to `new ResourceChipGalleryBlock(_loc, _resourceConfigAsset)` and parse inside the block. Do **not** add a parsed `_resourceConfig` field to `GalleryDocument` — nothing else there uses one.
    - **`_resources` is currently `static readonly` and backs `protected override IReadOnlyList<string> InstanceChoices`.** Config-derived data cannot live in a static initialiser, so it becomes a readonly *instance* field populated in the constructor. `HudConfigLoader.LoadResourceConfig` returns `null` for an unwired `TextAsset`, so fall back to a hardcoded id list (`gold`, `country_population`, `recruits`, `country_score`, `org_score`, `population`) when the config is null — the other gallery blocks fail open the same way (see the `IsCountryAvailable` comment in `HudPanelGalleryBlocks.cs:38-45`). Without the fallback an unwired asset silently empties the block's instance dropdown, which is the same class of silent-wrong-render this change exists to fix.

- **Identifiers are used verbatim; old artwork names are renamed outright with no duplicates**:
  - No transformation helper exists, so there is nothing to test or get wrong. The rename map above leaves no file under an old name. `resources.json` no longer records icon names at all, so `recruits` correctly loses the historical `country-` prefix and `coin` becomes `gold`.

- **`population` gets its own text and artwork**:
  - `Assets/Localization/en.asset` + `ru.asset` — add `resource.population.name` / `resource.population.description`, English text duplicating the country-level wording, Russian a **real translation** produced via the `localization` skill (English placeholders in `ru.asset` are forbidden by `.claude/rules/unity/localization.md`).
  - `Assets/Textures/Icons/ResourceRow/population.png` — byte copy of the country-population artwork, with a `.meta` cloned from the source and given a fresh 32-hex GUID (the USS reference is path-based, so only the path must match).

- **New resource with text+art works with no further config; without them it still runs**:
  - Guaranteed by the derived-naming helper plus the blank-slot and raw-id fallbacks above; covered by the startup-report test cases.

## Steps

### Agent Steps

- [ ] **Add `ResourceDisplayNaming`** — new `src/Game.Configs/ResourceDisplayNaming.cs` with `NameKey`, `DescriptionKey`, `EffectNameKey`, `EffectDescriptionKey`, `IconClass`; all pure string interpolation over the id verbatim, no transformation. Static class, tabs, no redundant modifiers.
- [ ] **Slim the schema** — remove the five properties from `src/Game.Configs/ResourceConfig.cs`. Nothing else; leftover JSON fields are ignored by the deserializer's default behaviour.
- [ ] **Strip the config** — remove `nameKey`/`descriptionKey`/`icon` from all eight resource entries and the one `base_income` effect in `Assets/Configs/resources.json`; leave `displayWhitelist` and all behavioural fields untouched.
- [ ] **Update `src/` tests** — add `src/Game.Tests/ResourceDisplayNamingTests.cs`; add the coverage facts per the Tests section. Confirm `src/Game.ConsoleRunner/WarSim/WarScenarioRunner.cs:265-277`, `BotObservationTests`, `BaselineCardPlayTests`, `BotActionLogTests`, `CharacterInitTests`, `CharacterVisualStateTests`, `ControlFeatureTests`, `DamageDurability*Tests` still compile — grep confirms none of them set the removed fields, so no edits are expected.
- [ ] **Rename and add artwork** — `git mv` each `.png` **and** its `.png.meta` per the rename map (preserves GUIDs); add `population.png` + a fresh-GUID `.meta`. Do **not** touch `resource-country-control.png`, which `Assets/UI/HUD/CountryInfo/CountryInfo.uss:11` references by GUID.
- [ ] **Update USS** — rewrite the five `.resource-icon--*` rules in `Assets/UI/Shared/SharedStyles.uss:285-303`, add `.resource-icon--population`, and refresh the comment block at lines 276-279. Keep the plain `project://database/...png` URL form.
- [ ] **Update Unity consumers** — `ResourcesView.cs` (icon class, tooltip header/description, both effect lists), `PlayerTasksView.cs:100`, `WarResultWindowView.cs:157`, `ResourceChipGalleryBlock.cs` (+ its construction in `GalleryDocument.cs:246`).
- [ ] **Add localization existence probe and de-spam** — `bool Has(string key)` on `ILocalization` + `CustomLocalization`; `_warned` set around the existing `Get()` warning, cleared in `SetLocale`. No `ResourceDisplayReport`, no `HUDDocument` change.
- [ ] **Add coverage tests** — the text- and icon-coverage facts described in the Tests section, replacing the runtime report.
- [ ] **Locale keys via the `localization` skill** — add `resource.population.name` / `.description` (EN + real RU), remove `resource.damage.*` and `resource.durability.*` from both `en.asset` and `ru.asset`.
- [ ] **Compile and test** — `dotnet test src/GlobalStrategy.Core.sln` (Debug) for the `src/` changes, then finish with `/dotnet-build Release` so `Assets/Plugins/Core/` is refreshed for Unity. Do not commit the DLLs. Restore `Assets/UI/Fonts/*.asset` if Unity dirties them.

### User Steps

### 1. Verify the asset renames imported cleanly in the Editor

UnityMCP is not connected this session, so the `.png`/`.meta` moves are done on disk. Open the Editor once so it re-imports `Assets/Textures/Icons/ResourceRow/`, then confirm: no "missing texture" or broken-reference errors in the Console, `population.png` imported with the same importer settings as `country_population.png`, and no leftover `.meta` orphans.

### 2. Visually confirm the HUD, war result window and gallery

In Play mode, check the resource row shows the same **five** icons and the same tooltip text as before. It is five, not six: `population` is `seedTarget: "Province"`, and `VisualStateConverter.BuildResources` (lines 476-501) filters by `ResourceOwner.OwnerId == countryId`, so the province-scoped `population` never reaches the country HUD row despite being on `displayWhitelist`. Do not treat its absence as a regression.

Then hover `gold` and expand the monthly breakdown to confirm `base_income` still shows its translated name and description, and that no *other* effect in that breakdown gained translated text it did not have before (that would mean the `FindEffect` gate was dropped). Open a war result window and confirm the gold header icon is present (now `resource-icon--gold`). In the component gallery, open "Atom: ResourceChip" and confirm it lists six resource ids with the currency chip showing the coin artwork instead of rendering iconless, then open "HUD: Resources" and confirm the `population` chip shows the new `population.png` — that block is where `population` artwork and text are actually exercised.

## Tests

- `src/Game.Tests/ResourceDisplayNamingTests.cs` (new) — every helper embeds the id verbatim with no transformation: `NameKey("country_population")` → `resource.country_population.name`, `IconClass("country_population")` → `resource-icon--country_population`, `EffectNameKey("base_income")` → `effect.base_income.name`. One regression fact asserting the underscore survives (guards against a future "tidy-up" reintroducing a conversion).
- `src/Game.Tests/ResourceConfigTests.cs` — existing three facts still pass unchanged (none use the removed fields). No new facts needed; there is no rejection behaviour to assert.
- `src/Game.Tests/StringConfigParityTests.cs` — `resources_parity` keeps passing unchanged; it asserts only `FileConfig<T>`-vs-`StringConfig<T>` deserialization parity and touches nothing removed here. **Do not add the locale fact here** — `Game.Tests` has no Unity-YAML reader; its `.csproj` references neither `Game.WebClient` nor `Game.WebClient.LocaleTool`, so hosting it there means hand-rolling a second parser or inverting a project dependency.
- `src/Game.WebClient.Tests/ResourceLocaleParityTests.cs` (new) — the **text-coverage** fact. That project already references `Game.WebClient.LocaleTool` (whose `LocaleAssetParser.Parse` is the project's tested Unity-YAML locale reader) and reaches `Game.Configs` through `Game.WebClient`. Loads `resources.json` via `FileConfig<ResourceConfig>` and both locale assets via `LocaleAssetParser.Parse`; asserts every `displayWhitelist` id's derived `resource.{id}.name` and `.description` exist in **both** locales (the regression guard for the newly added `resource.population.*`), and that `resource.damage.*` / `resource.durability.*` are absent from both. Reuse the existing `FindRepoRootConfigPath` walk-up pattern to locate the asset files.
- `src/Game.WebClient.Tests/ResourceIconCoverageTests.cs` (new) — the **icon-coverage** fact, replacing the abandoned runtime probe. For every `displayWhitelist` id assert both that `Assets/Textures/Icons/ResourceRow/{id}.png` exists on disk and that `Assets/UI/Shared/SharedStyles.uss` contains a `.resource-icon--{id}` selector (plain text scan is sufficient — these rules are hand-authored one per line). This catches a whitelisted resource added without art at `dotnet test` time, deterministically and with no Unity runtime. Same repo-root walk-up as above.
- Full `dotnet test src/GlobalStrategy.Core.sln` — regression guard for `WarScenarioRunner` and the seven test files that construct `ResourceDefinition` inline.
- Unity-side behaviour that remains unautomated: the blank icon slot and gallery/live icon parity, covered by the User Steps below.

## Constitution Check

No conflicts found — plan aligns with all principles.

- *Game Logic (ECS in `src/`)* — `ResourceDisplayNaming` is a pure naming helper in `src/Game.Configs`, not a MonoBehaviour. No new Unity-side runtime class is introduced at all: the coverage check is a test, and the only Unity edits are to existing view/localization glue.
- *Dependency Injection* — no new singletons; `ResourceConfig` continues to flow through the existing VContainer registration, and `ResourceChipGalleryBlock` receives it via its constructor from `GalleryDocument`, which already resolves it.
- *UI Toolkit only* — all UI work is USS/`VisualElement`; no Canvas or uGUI introduced.
- *Assembly Structure* — no new assemblies; new files land in the existing `GS.Unity.UI` and `Game.Configs` assemblies.
- *File Organisation / Planning & Specification Discipline* — spec and plan live together under `Docs/Specs/26_09_16_12_resources-config-schema-slimming/`.
- *C# Code Style* — tabs, `_`-prefixed private members, braces always, no redundant access modifiers; the validator fails fast with a descriptive, contextual message per the style rule.

Use the implement skill to start working on the plan or request changes.
