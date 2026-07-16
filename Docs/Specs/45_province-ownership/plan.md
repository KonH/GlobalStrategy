# Plan: Province Ownership

## Spec

Source: `Docs/Specs/45_province-ownership/spec.md`.

**Intent.** Give each province a mutable, persisted country owner. A country's territory becomes the aggregate of the provinces that currently point to it (no longer read from `CountryEntry.MainMapFeatureIds`/`SecondaryMapFeatureIds`). A debug-menu cheat reassigns the selected province (in the Province map lens) to the player org's `HqCountryId`, going through the command pipeline. This turns province ownership into real runtime state that rendering and country-level logic both react to, laying groundwork for future non-cheat ownership changes without building them yet.

**Key acceptance criteria (design targets):**
- Owner is read from mutable runtime state seeded at startup from `province_config.json`'s `countryId` — not from static config after startup.
- Country territory/area is derived by aggregating provinces by current owner.
- Country-level lenses (Political / Org / Geographic) render from province ownership, reusing `ProvinceRenderer`'s per-province meshes coloured by each province's current owner — **without** province borders (unchanged look). `MapRenderer`'s `mapFeatureId → CountryEntry.FindByFeatureId → fill mesh` path and the runtime consumption of the two feature-id lists for rendering/area are removed as dead code. The Python generation pipeline is untouched.
- Province lens keeps its spec-44 gray border outline exactly as before.
- Debug cheat available only when the Province lens is active **and** a province is selected; disabled otherwise. It pushes an `ICommand` (à la `DebugImproveOpinionCommand`), not a direct state mutation.
- Target country comes from the already-known `PlayerOrganization.HqCountryId` on `VisualState`; thread it if a layer lacks it, but do not invent org-selection logic.
- After a reassignment: province immediately recolours (Province lens); old owner's aggregate shrinks and new owner's grows (country lenses); every "does X own Y / what is X's territory" query reflects the change with no stale cache.
- Underlying model: a generic runtime component holding a province's owner (field style of `ControlEffect`), plus a generic change signal firing `(oldOwnerId, newOwnerId)` using the codebase's `INotifyPropertyChanged` idiom (as `SaveResultState`), not a new event mechanism. Only the cheat needs to fire it in this pass; no consumers beyond this feature's own rendering/logic-update needs.
- Ownership is `[Savable]`; survives save/reload (not recomputed from static config on load).
- `ControlEffect`/`ControlSystem` (org influence) left completely untouched — orthogonal concept.
- Update `.claude/rules/unity/map_system.md`, `.claude/rules/unity/province_config_generator.md`, and any other rule file whose text describes country area/ownership as fixed static config or the removed feature-based path.

**Out of scope:** war/annexation/AI/conquest, any `ControlEffect`/`ControlSystem` change, the province generation pipeline, cascading effects on the old owner beyond the derived aggregate, wiring any signal consumer beyond this feature's needs, save-migration for pre-feature saves, and any new HUD surface beyond observing the cheat.

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* The owner component (`ProvinceOwnership`), seeding, mutation, aggregation, and selection state all live in `src/` ECS components/systems. The rendering/click/debug-button changes are presentation and input glue in MonoBehaviours only.
- *VContainer sole DI.* `ProvinceConfig` is already a registered instance in `GameLifetimeScope`; this plan only threads it into the already-registered `GameLogicContext`. No `new` singletons, no `FindObjectOfType`, no static mutable singletons.
- *UI Toolkit only.* The cheat button reuses the existing UXML debug panel + `HUDDocument` binding pattern.
- *URP only.* No render-pipeline, shader, or material-pipeline changes; `ProvinceRenderer`'s existing material templates are reused.
- *One `.asmdef` per feature folder.* All new C# files land in existing feature folders — `src/Game.Components/`, `src/Game.Systems/`, `src/Game.Commands/`, `src/Game.Main/` (and their Unity-side consumers in `Assets/Scripts/Unity/Map` and `.../UI`). No new folder or assembly is introduced.
- *C# style.* Tabs, `{}` always, `_`-prefixed private members, no redundant modifiers — matching surrounding files.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case `[Fact]` names; harness pattern in `InitSystemTests.cs` — `StaticConfig<T>`, `MemoryStorage`, `CapturingSerializer`, `BuildLogic`).

- **New `src/Game.Tests/ProvinceOwnershipTests.cs`:**
  - `seed_creates_one_ownership_entity_per_province_from_config` — after first `GameLogic.Update`, one `ProvinceOwnership` per `ProvinceEntry`, `OwnerId == ProvinceEntry.CountryId`.
  - `change_owner_updates_owner_field` — `ProvinceOwnershipSystem.ChangeOwner` sets the new owner and returns the old owner.
  - `change_owner_to_same_owner_is_noop` — returns `changed == false`, no signal-worthy change.
  - `change_owner_unknown_province_is_noop`.
  - `get_provinces_by_owner_reflects_reassignment` — aggregation moves the province from old to new owner immediately (no stale cache); old owner's set shrinks, new owner's grows.
  - `get_owner_returns_current_runtime_owner`.
- **Extend `src/Game.Tests/SaveLoadRoundTripTests.cs`** (or add `ProvinceOwnershipSaveTests.cs` if it keeps that file focused): reassign a province, `SaveSystem.BuildSnapshot` → `LoadSystem.Apply`, assert the reassigned `OwnerId` survives and `IsInitialized` presence prevents re-seed from config on load.
- **Extend `src/Game.Tests/InitSystemTests.cs`:** add a `ProvinceConfig` to `BuildLogic`'s context and assert `CountEntities<ProvinceOwnership>` matches the province count after init and does not double after a second `Update`.
- **Command pipeline test** (in `ProvinceOwnershipTests.cs`): push `DebugChangeProvinceOwnerCommand` via `logic.Commands`, `Update`, assert the province's owner changed — proving the generated `ReadDebugChangeProvinceOwnerCommand()` accessor and `GameLogic` wiring work end to end.
- `src/Game.Tests/ProvinceConfigTests.cs` stays as-is (static config shape is unchanged).

Run: `dotnet test src/GlobalStrategy.Core.sln` (with `dangerouslyDisableSandbox: true`).

## Section 1 — Agent Steps

- [x] **Add the `ProvinceOwnership` runtime component** — Create `src/Game.Components/ProvinceOwnership.cs`: `[Savable] public struct ProvinceOwnership { public string ProvinceId; public string OwnerId; }` (string-id field style of `ControlEffect`). `[Savable]` makes `SaveSystem`/`LoadSystem` persist it automatically via reflection — no serializer changes needed.

- [x] **Add the `ProvinceOwnershipSystem`** — Create `src/Game.Systems/ProvinceOwnershipSystem.cs` (static, mirroring `ControlSystem`'s archetype-iteration style):
  - `Seed(World world, ProvinceConfig config)` — one entity per `ProvinceEntry` with `ProvinceOwnership { ProvinceId, OwnerId = entry.CountryId }`.
  - `(bool Changed, string OldOwnerId) ChangeOwner(World world, string provinceId, string newOwnerId)` — plain nested `foreach (Archetype arch in world.GetMatchingArchetypes(...))` over `ProvinceOwnership`, matching `ControlSystem.ApplyChangeControl`'s style (direct array-index field assignment, no lambda involved); return `(false, "")` if not found or owner unchanged, else set `OwnerId` and return `(true, oldOwner)`. Bump a static/module-level version counter (see next step) whenever a change is actually applied.
  - `string GetOwner(IReadOnlyWorld world, string provinceId)` and `Dictionary<string, List<string>> GetProvincesByOwner(IReadOnlyWorld world)` — the derived country-territory aggregation for renderer, tests, and any future consumer (no cached copy; recomputed from live components each call).
  - `int Version` — a monotonically-incrementing counter bumped by `Seed` and by `ChangeOwner` on an actual change, so consumers (see `UpdateProvinceOwnership` below) can detect "did ownership change since I last looked" without rebuilding every frame.

- [x] **Thread `ProvinceConfig` into game logic** — Add `IConfigSource<ProvinceConfig> Province { get; }` to `src/Game.Main/GameLogicContext.cs` as a trailing optional ctor param defaulting to an `EmptyProvinceConfig` (mirroring `EmptyCharacterConfig`), so existing test/context constructors keep compiling. Reuse the *same* loaded instance rather than deserializing twice: in `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, pass the existing `new TextAssetConfig<GS.Game.Configs.ProvinceConfig>(_provinceConfigAsset)` into the `GameLogicContext` constructor, then replace the current separate `builder.RegisterInstance(provinceConfig)` call (used today for `MapLoader`/`ProvinceRenderer`) with `builder.Register(c => c.Resolve<GameLogic>().ProvinceConfig, Lifetime.Singleton)` — expose `ProvinceConfig` as a public property on `GameLogic` backed by `context.Province.Load()`, matching the existing `ResourceConfig`/`CharacterConfig` pattern in `vcontainer.md` exactly. This avoids the two-deserialization divergence risk that pattern warns about.

- [x] **Seed province ownership at init** — In `src/Game.Main/InitSystem.Run`, after the country/resource loop, call `ProvinceOwnershipSystem.Seed(world, context.Province.Load())`. Seeding is gated by the existing `IsInitialized` guard, so it runs once for a new game and is skipped after load (where `[Savable]` province entities come from the snapshot). Config `countryId`s are already validated against `CountryConfig` at build time per `config_validation.md` — no extra runtime cross-check.

- [x] **Add the generic owner-change signal + ownership state to `VisualState`** — In `src/Game.Main/VisualState.cs` add `ProvinceOwnershipState : INotifyPropertyChanged` (idiom of `SaveResultState`/`OrgMapState`): exposes `IReadOnlyDictionary<string,string> OwnerByProvinceId`, plus last-change fields `RecentProvinceId` / `RecentOldOwnerId` / `RecentNewOwnerId` (the `OwnerChanged(oldOwnerId, newOwnerId)` payload, modelled like `DiscoveredCountriesState.RecentlyDiscovered`). Its `Set(...)` fires `PropertyChanged`. Add the property to the `VisualState` aggregate.

- [x] **Populate ownership state each tick** — In `src/Game.Main/VisualStateConverter.cs` add `UpdateProvinceOwnership(world)` (called from `Update`) that compares `ProvinceOwnershipSystem.Version` against the last-seen version cached on the converter, and only rebuilds the `provinceId → ownerId` map (and the recent-change delta, same previous-vs-current diff technique as `UpdateDiscoveredCountries`) when the version has advanced — avoiding an unconditional ~1200-entry (per `province_config.json`) rebuild every unpaused frame. This makes both the province→owner map and the `(old,new)` change signal available to presentation with no stale cache.

- [x] **Make country territory a derived aggregation (consumers)** — Point rendering and any territory query at `ProvinceOwnershipSystem.GetProvincesByOwner` / `VisualState.ProvinceOwnership.OwnerByProvinceId` instead of `CountryEntry.MainMapFeatureIds`/`SecondaryMapFeatureIds`. (The two feature-id lists remain on `CountryEntry` because `InitSystem.BuildProximityMap`/`ComputeMinDistance` and the Python pipeline still use them for proximity/geometry — only their **rendering/area** consumption is removed.)

- [x] **Rework country-lens rendering onto province data** — In `Assets/Scripts/Unity/Map/MapLensApplier.cs`, drive `ActiveProvinceRenderer` for **all** lenses:
  - For every province `go`, resolve its runtime owner from `VisualState.ProvinceOwnership.OwnerByProvinceId[go.name]` (fall back to `ProvinceIdentifier.CountryId` only if absent), gate visibility by discovery of that owner, and set the fill colour: Province & Political → owner's `CountryVisualConfig` colour; Org → owner's top-org colour from `OrgMap`; Geographic → transparent.
  - Enable border child renderers **only** when `lens == Province` (keeps the spec-44 outline exclusive to that lens; country lenses stay border-free).
  - Subscribe to `VisualState.ProvinceOwnership.PropertyChanged` (alongside the existing `MapLens`/`OrgMap`/`DiscoveredCountries` handlers) and re-apply the current lens so a reassignment recolours immediately.
  - Delete the `ActiveRenderer`/`MapRenderer.FeatureObjects` fill branch and the `FindByFeatureId`-based `GetPoliticalColor`/`GetOrgColor` helpers.

- [x] **Remove `MapRenderer`'s feature path as dead code** — Delete `Assets/Scripts/Unity/Map/MapRenderer.cs` and its only collaborator `Assets/Scripts/Unity/Map/FeatureIdentifier.cs` (both `.cs` + `.meta` via Bash `rm`, per project convention). Update `Map.cs` (drop `_renderer`/`Renderer`, `Render` call, and the `MapRenderer` param of `Initialize`), `MapController.cs` (drop `ActiveRenderer`), and `MapLoader.cs` (drop the `MapRenderer` wiring). `MapRenderer`/`ActiveRenderer`/`FeatureObjects` have two more real consumers beyond the Map scene that must be migrated in the same step or compilation breaks:
  - `Assets/Scripts/Unity/Map/SelectOrgMapFilter.cs` (used in `CountrySelection.unity` via `SelectCountryLifetimeScope`, which shares `Map.prefab`) — swap its `MapRenderer`/`ActiveRenderer`/`FeatureObjects` usage for `ActiveProvinceRenderer.FeatureObjects` + `ProvinceIdentifier.CountryId` (static seed id is fine here — `SelectCountryLifetimeScope` has no ECS `World`).
  - `MapCameraController.PanToCountry` (reads `_mapController.ActiveRenderer.FeatureObjects`, called from `CardPlayAnimator.cs` on country discovery in the Game scene) — same swap onto `ActiveProvinceRenderer`/`ProvinceIdentifier.CountryId`.

  Remove the `MapRenderer` MonoBehaviour + its mesh child from `Assets/Prefabs/Map/Map.prefab` and any dangling reference in `Assets/Scenes/Map.unity`. Then `refresh_unity` + `read_console(types=["error"])` to confirm a clean compile across all scenes that use `Map.prefab` (Map, CountrySelection, Game).

- [x] **Wire province selection through `MapClickHandler`** — Add `SelectProvinceCommand { public string ProvinceId; }` to `src/Game.Commands/` (auto-generates a `ReadSelectProvinceCommand()` accessor). Add a transient (not `[Savable]`) singleton `ProvinceSelection { public string ProvinceId; }` component in `src/Game.Components/`. In `GameLogic.Update`, drain `SelectProvinceCommand` and set/create the `ProvinceSelection` singleton; add `SelectedProvinceState : INotifyPropertyChanged` (`IsValid`, `ProvinceId`) to `VisualState`, populated by a new `VisualStateConverter.UpdateSelectedProvince`. In `MapClickHandler.HandleProvinceClick`, replace the `// TODO` with `_commands.Push(new SelectProvinceCommand { ProvinceId = provinceId })`. In the **non-province** click branch, hit-test `ActiveProvinceRenderer` (the country renderer is gone), resolve the clicked province's runtime owner from `VisualState.ProvinceOwnership`, and push `SelectCountryCommand(ownerId)` — removing the `MapRenderer.FindFeatureAt` + `FindByFeatureId` usage there.

- [x] **Add the debug reassign command + system wiring** — Add `DebugChangeProvinceOwnerCommand { public string ProvinceId; public string NewOwnerId; }` to `src/Game.Commands/` (following `DebugImproveOpinionCommand`). In `GameLogic.Update`, drain it (next to the other `Debug*` drains) and call `ProvinceOwnershipSystem.ChangeOwner(_world, cmd.ProvinceId, cmd.NewOwnerId)` — the next `VisualStateConverter.Update` picks up the new map + change delta.

- [x] **Add the debug-menu cheat button** — In `Assets/Scripts/Unity/UI/HUDDocument.cs`, add a "Reassign Province to My HQ" button to the debug panel (same `gs-btn gs-btn--small debug-panel-button` styling as `Improve Opinion`). On `PointerUp`/click it pushes `DebugChangeProvinceOwnerCommand { ProvinceId = _state.SelectedProvince.ProvinceId, NewOwnerId = _state.PlayerOrganization.HqCountryId }`. Add a `RefreshProvinceCheatButton()` (modelled on `RefreshControlDebugRow`) that shows/enables it only when `_state.MapLens.Lens == MapLens.Province && _state.SelectedProvince.IsValid`, and subscribe it to `_state.MapLens.PropertyChanged` and `_state.SelectedProvince.PropertyChanged` in `OnEnable`/`OnDisable`. Guard the push against empty `HqCountryId`/`ProvinceId`.

- [x] **Persistence** — No serializer work beyond the `[Savable]` attribute on `ProvinceOwnership` (step 1): `SaveSystem.BuildSnapshot`/`LoadSystem.Apply` discover it by reflection. Confirm the round-trip test (Tests section) passes. Note in the doc updates that pre-feature saves (created before this component existed) load with no province entities and are not re-seeded — an accepted out-of-scope limitation.

- [x] **Update rule docs** — In `.claude/rules/unity/map_system.md`: replace the `MapRenderer`/`FeatureIdentifier`/`FindByFeatureId`-for-rendering guidance with the new model (all lenses render via `ProvinceRenderer`; per-province colour comes from runtime `ProvinceOwnership` in `VisualState`; clicks hit-test provinces and resolve the runtime owner; country territory is the province-ownership aggregate). In `.claude/rules/unity/province_config_generator.md`: clarify that `province_config.json`'s `countryId` is now **seed** data for mutable runtime ownership rather than the permanent owner, while the generation pipeline itself is unchanged. Scan `.claude/rules/*` for any other text describing country area/ownership as fixed static config (e.g. `config_validation.md`'s foreign-key wording is still accurate — leave it) and correct only what the new model makes inaccurate.

- [x] **Add/extend tests** — Implement the Tests section above; run `dotnet test src/GlobalStrategy.Core.sln`.

## Section 2 — User Steps

### 1. Rebuild the Core DLLs and let Unity import

After the agent's `src/` changes compile, run `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up the new `ProvinceOwnership`, `ProvinceOwnershipSystem`, and commands, then let Unity finish its domain reload (`read_console` should be error-free).

### 2. Verify the debug button gating in the Editor

Enter Play mode, open the debug panel. Confirm the "Reassign Province to My HQ" button is hidden/disabled outside the Province lens and while no province is selected, and becomes available only after selecting a province in the Province lens.

### 3. Confirm Province vs. country-lens rendering side-by-side

Switch between Province and Political/Org/Geographic lenses. Confirm: the Province lens still shows the gray province borders (spec 44) and country lenses show no province borders and look identical to today; country fills match the owning country's colour.

### 4. Exercise the cheat and observe the aggregate change

Select a province owned by a neighbour, trigger the cheat, and confirm: under the Province lens the province immediately recolours to the player HQ country; under Political/Org lenses the old owner's territory visibly shrinks and the HQ country's grows — with no lens re-toggle needed.

### 5. Save/reload round-trip in the Editor

After a reassignment, save via the in-game menu, reload the save, and confirm the reassigned province still shows the new owner (both lenses) rather than reverting to the config `countryId`.

Use /implement to start working on the plan or request changes.
