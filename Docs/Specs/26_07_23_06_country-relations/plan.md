# Plan: Country-to-Country Relations

## Spec summary

Source: `Docs/Specs/26_07_23_06_country-relations/spec.md`.

Countries hold a bidirectional friend/rival stance toward each other, many-to-many, mutually exclusive per pair (a pair is friend, rival, or neither — never both). Available countries are seeded with 1-3 historical friends and 1-3 historical rivals at game start (`startYear: 1880`). The relation can be changed at runtime only through a debug tool on the existing "Selected country" debug menu (dropdown + Set friend / Set rival / Clear relation). Relation data persists across save/load and is exposed through `VisualState` for the currently selected country. Visual markers, gameplay effects, and any player-facing (non-debug) way to change relations are out of scope.

## Goal

Add a pairwise `CountryRelation` ECS component, a plain query/mutation helper (not a system, since both `InitSystem` and `GameLogic` need to call it directly), historical seeding from extended `CountryEntry` config data, two debug commands wired the same way as the existing province-occupation debug commands, a `VisualState` projection for the selected country, and a debug UI section reusing the existing "Selected country" debug menu. Along the way, fix a pre-existing architecture gap this feature would otherwise repeat: introduce a shared, zero-dependency `Game.Common` project for cross-project enums, and migrate the existing `MapLens` enum into it instead of leaving it defined inside `Game.Commands`.

## Approach

### 1. New `Game.Common` project — shared data types, and a fix for the `MapLens` precedent

- `Game.Commands` currently has zero project references, and its one existing enum (`MapLens`, in `ChangeLensCommand.cs`) is defined locally there and reused downstream by `Game.Main`. That precedent is what this feature would otherwise have copied for `RelationKind` (as a `bool IsFriend` workaround) — instead, fix the underlying gap: add `src/Game.Common/Game.Common.csproj` (netstandard2.1, same `Nullable`/`LangVersion`/Release-`OutputPath`-to-`Assets/Plugins/Core/` boilerplate as every other `src/` library, **zero** `ProjectReference`s — the most foundational project in the solution, below even `Game.Components`).
- Move `MapLens` out of `src/Game.Commands/ChangeLensCommand.cs` into `src/Game.Common/MapLens.cs` under namespace `GS.Game.Common`. `ChangeLensCommand` keeps just the command struct and gets a `using GS.Game.Common;`.
- Add `src/Game.Common/RelationKind.cs`: `public enum RelationKind { Friend, Rival }`, namespace `GS.Game.Common`. This is the type both the new component (§2) and the new debug command (§6) share, instead of duplicating it or routing around it with a `bool`.
- Add `Game.Common` to `src/GlobalStrategy.Core.sln`. Add `<ProjectReference Include="../Game.Common/Game.Common.csproj" />` to every `.csproj` whose code now names `MapLens` or `RelationKind` directly: `Game.Commands` (both enums' commands), `Game.Components` (§2), `Game.Systems` (§3), `Game.Main` (`VisualState.cs`'s `MapLensState.Lens` and the debug-command wiring in `GameLogic.cs`), and `Game.Tests` (asserts against `RelationKind.Friend`/`.Rival`) — matching this codebase's existing convention of every project explicitly listing each project it directly consumes, rather than relying on transitive `ProjectReference` propagation.
- Unity side: the three asmdefs whose scripts reference `GS.Game.Commands.MapLens` today — `Assets/Scripts/Unity/UI/GS.Unity.UI.asmdef` (`HUDDocument.cs`, `LensSwitcherView.cs`), `Assets/Scripts/Unity/Map/GS.Unity.Map.asmdef` (`MapClickHandler.cs`, `MapLensApplier.cs`), `Assets/Scripts/Unity/DI/GS.Unity.DI.asmdef` (`GameLifetimeScope.cs`) — need a new GUID reference to the built `Assets/Plugins/Core/Game.Common.dll.meta` (create that `.meta` with a fresh GUID directly, the same way `Assets/Plugins/Core/Game.Bots.dll.meta` was authored by hand rather than via the Editor), and each of those 5 files needs `using GS.Game.Common;` added for `MapLens`. This is a `dotnet build` + file-edit change, not a Unity-Editor-only action.

### 2. `CountryRelation` component (using the shared `RelationKind`)

- Add `src/Game.Components/CountryRelation.cs` with `[Savable] public struct CountryRelation { RelationKind Kind; string LeftCountryId; string RightCountryId; }` (using `GS.Game.Common.RelationKind` from §1 — no enum redefined here), mirroring the shape of `src/Game.Components/OrganizationGameOutcome.cs` (component next to/using its result enum). One entity per related pair — never a list composed onto `Country` — so a pair's relation is a single independently creatable/removable/queryable record, the same shape as `src/Game.Components/DiscoveredCountry.cs` (one entity per (org, country) pair).
- Add `typeof(CountryRelation)` to `ExpectedSavable` in `src/Game.Tests/SavableDiscoveryTests.cs`. No companion "not savable" version-counter component is needed (see §6 for why).

### 3. `CountryRelations` helper (deliberately not `CountryRelationSystem`)

- Add `src/Game.Systems/CountryRelations.cs` as a plain static helper class — **not** suffixed `System` — in the same family as the existing non-system helpers `OrgMetrics`/`ResourceQuery` in that folder. This is required by `.claude/rules/unity/ecs_patterns.md`'s "no system-to-system calls" rule: `GameLogic.Update` (the top-level orchestrator) calls it for debug commands, which is always fine, but keeping its logic in a non-system-named class also keeps the door open for any future system to call it as a plain helper without violating the rule.
- Methods, all static and `World`/`IReadOnlyWorld`-based, mirroring `src/Game.Systems/ProvinceOccupationSystem.cs`'s shape:
  - `SetRelation(World world, string countryIdA, string countryIdB, RelationKind kind) : bool` — returns `false` and does nothing if `countryIdA == countryIdB` (self-relation rejection). Otherwise removes any existing `CountryRelation` entity for that unordered pair (in either direction) via the same lookup as `RemoveRelation`, creates a new entity with the given `kind`, and returns `true`. This makes a friend/rival replacement a remove-then-add rather than two entities disagreeing.
  - `RemoveRelation(World world, string countryIdA, string countryIdB) : bool` — scans all `CountryRelation` entities, destroys (`world.Destroy(entity)`) the first one matching `(LeftCountryId, RightCountryId) == (a, b)` or `(b, a)`, returns whether one was found.
  - `GetRelation(IReadOnlyWorld world, string countryIdA, string countryIdB) : RelationKind?` — same direction-agnostic match, returns the kind or `null`.
  - `GetRelationsByCountryId(IReadOnlyWorld world, string countryId) : (List<string> Friends, List<string> Rivals)` — scans all `CountryRelation` entities where `countryId` appears on either side, returning the other side's ID bucketed by kind.
  - A private `Matches(CountryRelation, a, b)` helper used by all four public methods so the "check both directions" logic exists exactly once.

### 4. Historical seeding stays inside `InitSystem`, not delegated to `CountryRelations`

- Extend `CountryEntry` in `src/Game.Configs/CountryConfig.cs` with `public List<string> HistoricalFriends { get; set; } = new List<string>();` and `public List<string> HistoricalRivals { get; set; } = new List<string>();` (default empty, matching the record's existing optional-field style).
- Route both new fields through `ApplyPreservedFields` in `src/Game.Configs.Loader/Program.cs` (alongside `IsAvailable`/`InitialResources`) so regenerating `country_config.json` from GeoJSON does not wipe hand-authored relations.
- In `src/Game.Main/InitSystem.cs`, add a private `SeedCountryRelations(World world, CountryConfig config)` called from `Run()` right after the existing `Country`-entity creation loop. This follows the precedent set by every other `InitSystem` sub-step (`CreateCountryResourceEntities`, `CreateProvinceResourceEntities`, `DiscoverInitialCountries`): raw `world.Create()`/`world.Add()` calls made directly inside `InitSystem`, not a call into `CountryRelations` or any other external helper — `InitSystem.Run`'s own comment block already documents that it "only creates raw entity/component data, never call[s] into another system," and the codebase's actual precedent extends that to any external helper, not just system entry points.
- Algorithm: build the set of available country IDs first. Then iterate every available `CountryEntry`'s `HistoricalFriends`/`HistoricalRivals` in config file order; for each listed `otherId`, skip it if it equals the entry's own ID or isn't in the available set; canonicalize the pair via ordinal string comparison (`(min, max)`); track seen canonical pairs in a `HashSet<(string, string)>` and skip (no-op) if the pair was already created. This gives:
  - **One-sided listing** (only one country's config entry lists the pair): still creates exactly one entity, satisfied on the first occurrence encountered in file order.
  - **Conflicting listing** (one side lists it as friend, the other as rival): deterministic **first-declared-wins** — whichever entry is iterated first (config file order, which is stable) creates the entity with its kind; the later, conflicting declaration is skipped as an already-seen pair. This is the single consistent resolution rule required by the spec's acceptance criteria, documented here and in a code comment at the skip site.
  - **Unavailable target**: silently skipped, consistent with `.claude/rules/config_validation.md`'s existing silent-mismatch convention for country-ID foreign keys.

### 5. Populate historical relations in `Assets/Configs/country_config.json`

For each of the 20 `isAvailable: true` countries, add 1-3 `historicalFriends`/`historicalRivals` entries (only referencing other available countries), grounded in documented circa-1880 alliances/rivalries. Declared symmetrically on both sides in the JSON for readability (the §4 dedup logic collapses each pair to one entity regardless):

| Country | Friends | Rivals |
|---|---|---|
| Argentina | United States | Kingdom of Brazil |
| Austria-Hungary | Germany, Russian Empire | Italy, Ottoman Empire |
| Belgium | United Kingdom, France | Netherlands |
| Egypt | Ottoman Empire | Ethiopia |
| Ethiopia | Portugal | Egypt |
| France | Belgium, Spain | Germany, United Kingdom |
| Germany | Austria-Hungary, Russian Empire, Imperial Japan | France, Portugal |
| Imperial Japan | Germany | Manchu Empire |
| Italy | United Kingdom | Austria-Hungary |
| Kingdom of Brazil | United States | Argentina |
| Manchu Empire | Netherlands | Russian Empire, Imperial Japan |
| Netherlands | Sweden-Norway, Manchu Empire | Belgium |
| Ottoman Empire | Egypt | Russian Empire, Austria-Hungary, Persia |
| Persia | Russian Empire | Ottoman Empire |
| Portugal | United Kingdom, Spain, Ethiopia | Germany |
| Russian Empire | Germany, Austria-Hungary, Persia | Ottoman Empire, Manchu Empire, Sweden-Norway |
| Spain | France, Portugal | United States |
| Sweden-Norway | Netherlands | Russian Empire |
| United Kingdom | Portugal, Belgium, Italy | France |
| United States | Kingdom of Brazil, Argentina | Spain |

Rationale (for the commit/PR description, not the JSON itself): Dual Alliance and League of the Three Emperors (Germany-Austria-Hungary-Russia friendships), Anglo-Portuguese Alliance and UK's guarantee of Belgian neutrality, Franco-Prussian War aftermath and colonial friction (France-Germany, France-UK rivals), the Great Game and Congress of Berlin fallout (UK/Austria-Hungary/Russia vs. Ottoman Empire), Italian irredentism (Italy-Austria-Hungary), the 1879 Persian Cossack Brigade (Persia-Russia friendship) against the older Ottoman-Persian rivalry, the 1879 Ryukyu dispute and 1880-81 Ili crisis (Japan/Russia vs. Manchu Empire), the 1875-76 Egyptian-Ethiopian War, the 1830 Belgian secession from the Netherlands, and general New World cordiality (US-Brazil, US-Argentina) against Spain's Cuba tensions and Argentina-Brazil regional rivalry.

### 6. Debug commands and `GameLogic` wiring — no version counter needed

- Add `src/Game.Commands/DebugSetCountryRelationCommand.cs` (`string CountryIdA; string CountryIdB; RelationKind Kind;`) and `src/Game.Commands/DebugClearCountryRelationCommand.cs` (`string CountryIdA; string CountryIdB;`), following the plain-data-struct shape of `DebugSetProvinceOccupationCommand`/`DebugClearProvinceOccupationCommand`. `Kind` is `GS.Game.Common.RelationKind` directly (via the new `Game.Commands → Game.Common` reference from §1) — no `bool` workaround and no duplicated enum.
- In `src/Game.Main/GameLogic.cs`'s `Update`, add two `foreach` blocks alongside the existing `ReadDebugSetProvinceOccupationCommand`/`ReadDebugClearProvinceOccupationCommand` loops:
  ```csharp
  foreach (var cmd in _commandAccessor.ReadDebugSetCountryRelationCommand().AsSpan()) {
      CountryRelations.SetRelation(_world, cmd.CountryIdA, cmd.CountryIdB, cmd.Kind);
  }
  foreach (var cmd in _commandAccessor.ReadDebugClearCountryRelationCommand().AsSpan()) {
      CountryRelations.RemoveRelation(_world, cmd.CountryIdA, cmd.CountryIdB);
  }
  ```
  Unlike the province-occupation/ownership commands, this does **not** also call `VisualState.Set(...)` directly in the loop — those two do it only to publish one-shot "recent change" fields (`RecentProvinceId`/`RecentOldOwnerId`/...) that this feature has no equivalent of. Relation debug mutations are picked up by the unconditional `_visualStateConverter.Update(...)` call at the end of the same `Update()` tick (§7), exactly the same way `ApplyDebugImproveOpinion`/`ApplyDebugChangeGold`/character-cycle debug mutations already work with no direct `VisualState` touch in their command loops.
- No `CountryRelationsVersion` singleton/dirty-counter component is added. `Country`-scoped `VisualState` data (`CountryActionsState` via `UpdateCountryActions`) is already recomputed unconditionally every tick rather than version-gated — it's cheap because it's bounded by one selected country's data, not the whole map. Relations are the same shape (bounded by ~20 countries), so `UpdateCountryRelations` (§7) follows that existing sibling pattern instead of the `ProvinceOccupationVersion`-style gating the spec's Tech Notes suggested; this is a plan-time simplification, not a behavior change — the acceptance criterion ("relation portion of `VisualState` is not recomputed when unchanged") is about internal cost, not externally observable output, and an unconditional recompute produces identical results.

### 7. `VisualState` exposure on `SelectedCountry`

- Add `CountryRelationsState : INotifyPropertyChanged` to `src/Game.Main/VisualState.cs` with `IReadOnlyList<string> Friends`/`IReadOnlyList<string> Rivals` (default `Array.Empty<string>()`) and a `Set(...)`-replaces-not-appends method, following the exact style of the other small list-state classes in that file.
- Add `public CountryRelationsState Relations { get; } = new CountryRelationsState();` to `SelectedCountryState`, alongside its existing `Resources`/`Control`/`Characters`/`CountryActions` sub-states.
- In `src/Game.Main/VisualStateConverter.cs`, add `UpdateCountryRelations(IReadOnlyWorld world)`: if `!_state.SelectedCountry.IsValid`, set both lists empty and return; otherwise call `CountryRelations.GetRelationsByCountryId(world, _state.SelectedCountry.CountryId)` and `Set(...)` the result. Call it unconditionally from the converter's `Update` method next to the existing `UpdateCountryActions(world, gameTimeEntity);` line.

### 8. Debug UI — extend the existing "Selected country" debug menu

- In `Assets/UI/HUD/HUD.uxml`, add a sibling container inside the existing `selected-country-debug-menu` block (next to `character-debug-container`): `<ui:VisualElement name="relation-debug-container" style="flex-direction: column;" />`. This is a plain UXML text edit, not a Unity-Editor-only action.
- In `Assets/Scripts/Unity/UI/HUDDocument.cs`, add fields `VisualElement _relationDebugContainer; DropdownField _relationCountryDropdown; Button _btnSetCountryFriend; Button _btnSetCountryRival; Button _btnClearCountryRelation; readonly List<string> _relationDropdownCountryIds = new();`.
- Add `BuildRelationDebugUi()`, called from `Awake` alongside the existing `BuildProvinceDebugUi();` call: query `relation-debug-container`, create the dropdown (`debug-panel-button` class, `RegisterValueChangedCallback(_ => RefreshRelationActionButtons())`) and the three buttons (`gs-btn`, `gs-btn--small`, `debug-panel-button` classes), wiring `PushSetCountryRelationCommand(RelationKind.Friend)` / `PushSetCountryRelationCommand(RelationKind.Rival)` / `PushClearCountryRelationCommand` via `PointerUpEvent` handlers per `.claude/rules/unity/uitoolkit.md`'s documented `Button.clicked` bug workaround (the existing province-debug buttons use plain `Button(Action)` construction, which internally still relies on `clicked` — confirm during implementation whether that known bug affects these buttons in practice, since the existing province debug buttons apparently work; use the `PointerUpEvent` workaround if not).
- Add `RebuildRelationCountryDropdown()`: clears `_relationDropdownCountryIds`, iterates `_countryConfig.Countries` where `IsAvailable && CountryId != _state.SelectedCountry.CountryId`, builds a label via the existing `GetCountryDisplayName` helper suffixed with `" (Friend)"`/`" (Rival)"` when that country ID is in `_state.SelectedCountry.Relations.Friends`/`.Rivals`, sets `choices`/`index`, then calls `RefreshRelationActionButtons()`.
- Add `RefreshRelationActionButtons()`: resolves the dropdown's currently selected country ID (mirroring `GetSelectedProvinceDropdownCountryId`), enables "Set friend"/"Set rival" whenever a target is selected, and enables "Clear relation" only when that target is currently a friend or rival of the selected country.
- Subscribe `_state.SelectedCountry.Relations.PropertyChanged` in `OnEnable` (unsubscribe in `OnDisable`), handler calls `RebuildRelationCountryDropdown()` — this covers both "selected country changed" (already triggers `HandleCountryChanged` → add a `RebuildRelationCountryDropdown()` call there too, since a country switch changes `Relations.Friends`/`.Rivals` contents even before the next converter pass settles) and "a relation changed for the currently selected country" (the `Relations` sub-state's own `PropertyChanged` fires after `UpdateCountryRelations` runs).
- Also call `RebuildRelationCountryDropdown()` once after `BuildRelationDebugUi()` during `Awake`, alongside the existing `RefreshSelectedCountryCharacterDebugButtons()`/`RefreshSelectedProvinceDebugMenu()` initial-sync calls.

## Steps

### Agent Steps

- [x] Add `src/Game.Common/Game.Common.csproj` (zero references); move `MapLens` into `src/Game.Common/MapLens.cs`; add `src/Game.Common/RelationKind.cs`; add the project to `GlobalStrategy.Core.sln` and to every `.csproj` that names either enum (`Game.Commands`, `Game.Components`, `Game.Systems`, `Game.Main`, `Game.Tests`).
- [ ] Update `ChangeLensCommand.cs` and the 5 Unity scripts that reference `MapLens` (`HUDDocument.cs`, `LensSwitcherView.cs`, `MapClickHandler.cs`, `MapLensApplier.cs`, `GameLifetimeScope.cs`) to `using GS.Game.Common;`. Add a hand-authored `Assets/Plugins/Core/Game.Common.dll.meta` (fresh GUID, same format as `Game.Bots.dll.meta`) and add that GUID to the `references` array of `GS.Unity.UI.asmdef`, `GS.Unity.Map.asmdef`, and `GS.Unity.DI.asmdef`. — **Deferred: needs live Unity Editor MCP.** `ChangeLensCommand.cs` itself was already updated (it lives in `src/`); the 5 Unity scripts, the `.dll.meta`, and the 3 asmdef edits are outstanding.
- [x] Add `CountryRelation` (using `GS.Game.Common.RelationKind`) in `src/Game.Components/`; add `typeof(CountryRelation)` to `SavableDiscoveryTests.ExpectedSavable`.
- [x] Add the `CountryRelations` static helper (`SetRelation`/`RemoveRelation`/`GetRelation`/`GetRelationsByCountryId`) in `src/Game.Systems/`.
- [x] Extend `CountryEntry` with `HistoricalFriends`/`HistoricalRivals`; route both through `ApplyPreservedFields` in `src/Game.Configs.Loader/Program.cs`.
- [x] Add `SeedCountryRelations` to `InitSystem.cs` (raw entity creation, dedup + first-declared-wins conflict resolution, unavailable-target skip) and call it from `Run()`.
- [x] Populate `Assets/Configs/country_config.json` with the historical friend/rival table above for all 20 available countries.
- [x] Add `DebugSetCountryRelationCommand`/`DebugClearCountryRelationCommand` in `src/Game.Commands/`; wire both in `GameLogic.Update`.
- [x] Add `CountryRelationsState` to `VisualState.cs` (`SelectedCountry.Relations`); add `UpdateCountryRelations` to `VisualStateConverter.cs` and call it unconditionally alongside `UpdateCountryActions`.
- [ ] Add `relation-debug-container` to `HUD.uxml`; add `BuildRelationDebugUi`/`RebuildRelationCountryDropdown`/`RefreshRelationActionButtons`/command-push methods to `HUDDocument.cs`; wire the `Relations.PropertyChanged` subscription and the `HandleCountryChanged` refresh call. — **Deferred: needs live Unity Editor MCP.**
- [x] Add `src/Game.Tests/CountryRelationsTests.cs` (see Tests); extend `SavableDiscoveryTests` and `InitSystemTests`/config-loader preservation tests as listed below.
- [x] Run `dotnet test src/GlobalStrategy.Core.sln`, then `dotnet build src/GlobalStrategy.Core.sln -c Release` to refresh the Unity-consumed assemblies under `Assets/Plugins/Core/`.

### User Steps

- [ ] After the Core DLL rebuild, let Unity finish domain reload and confirm no console errors from the `HUD.uxml`/`HUDDocument.cs` changes.
- [ ] Enter Play mode, select a country with configured historical relations (e.g. Germany), open the debug panel → "Selected country" menu, and confirm the relation dropdown lists every other available country annotated `(Friend)`/`(Rival)` matching the seeded table (e.g. Austria-Hungary, Russian Empire, Imperial Japan show `(Friend)`; France, Portugal show `(Rival)`).
- [ ] Pick an unrelated country in the dropdown, click "Set friend", and confirm the label updates to `(Friend)` and "Clear relation" becomes enabled; repeat with "Set rival" on a currently-friend entry and confirm it flips to `(Rival)` (exclusivity replacement).
- [ ] Click "Clear relation" and confirm the label reverts to no annotation and the button disables itself again.
- [ ] Switch the selected country to the just-changed target country and confirm its own dropdown/annotations reflect the same relation from that side (bidirectionality).
- [ ] Save, reload, reselect the same country, and confirm the debug-tool-created relation persisted.

## Tests

- Add `src/Game.Tests/CountryRelationsTests.cs` using the `BuildLogic(...)` + in-memory config style from `ProvinceOccupationTests.cs`. Cover: `SetRelation` creates a bidirectionally-queryable friend relation; `SetRelation` with the opposite kind on an existing pair replaces it (old kind no longer reported, `GetRelation` returns exactly one kind); `RemoveRelation` clears a pair from both directions; `SetRelation`/`GetRelation` reject/ignore `countryIdA == countryIdB`; `GetRelationsByCountryId` returns independent friend/rival lists across several unrelated pairs without cross-contamination.
- Extend `src/Game.Tests/InitSystemTests.cs` (or add `CountryRelationSeedingTests.cs`) for: a relation listed on only one side of a pair is seeded and queryable from both sides; a historical entry naming a country that isn't `IsAvailable` seeds no relation and doesn't affect other seeding; a pair declared as friend by one entry and rival by the other resolves deterministically to the first-declared kind (assert via a config with entries in a known list order) and creates exactly one `CountryRelation` entity for that pair.
- Extend the `CountryConfig`/loader-preservation tests (`src/Game.Tests/LoaderCountryPreservationTests.cs`) to cover `HistoricalFriends`/`HistoricalRivals` being preserved through `ApplyPreservedFields` the same way `IsAvailable`/`InitialResources` already are.
- Add debug-command coverage (in `CountryRelationsTests.cs` or alongside it) pushing `DebugSetCountryRelationCommand`/`DebugClearCountryRelationCommand` through a built `GameLogic`, asserting both the ECS relation state and `VisualState.SelectedCountry.Relations.Friends`/`.Rivals` reflect the change after `Update`, including the friend↔rival replacement case end-to-end.
- Extend `src/Game.Tests/SavableDiscoveryTests.cs` for `CountryRelation`, and extend `src/Game.Tests/SaveLoadRoundTripTests.cs` (or a focused test) for a runtime-created relation surviving a save/load round trip.
- Run `dotnet test src/GlobalStrategy.Core.sln`, then `dotnet build src/GlobalStrategy.Core.sln -c Release` so all dependent tests pass and the tracked Unity-consumed DLLs are refreshed.

## Constitution Check

- **ECS game logic:** The relation data, its mutation, and its historical seeding live in `src/Game.Components`, `src/Game.Systems`, and `InitSystem`/`GameLogic`. `HUDDocument`/`VisualStateConverter` only read and project decided ECS state; no MonoBehaviour owns relation rules.
- **Dependency injection:** No new global service or mutable static singleton. `HUDDocument` continues to use its existing injected `VisualState`/`IWriteOnlyCommandAccessor`/`CountryConfig`.
- **UI:** Pure UI Toolkit — a new `DropdownField`/`Button`s added to the existing UXML-authored, VContainer-injected `HUDDocument`. No Canvas/UGUI.
- **Planning/spec discipline:** This plan implements the approved `spec.md` in the same `Docs/Specs/26_07_23_06_country-relations/` folder before source changes.
- **Assembly/file organization:** One new `src/` project, `Game.Common`, is introduced deliberately (§1) — it holds only shared, dependency-free enums (`MapLens`, `RelationKind`) consumed by multiple existing projects, which is exactly the kind of foundational, zero-reference project the solution's dependency graph otherwise lacks (today `Game.Commands` and `Game.Components` both sit at the bottom of the graph with zero references each, unable to share a type without one depending on the other). No new Unity `.asmdef`/feature folder is introduced — the 3 existing asmdefs that need it (`GS.Unity.UI`, `GS.Unity.Map`, `GS.Unity.DI`) just gain one more DLL reference, same as any other `Assets/Plugins/Core/*.dll`.
- **C# style:** Tabs, same-line braces, `_`-prefixed private fields, no redundant access modifiers, fail-fast where applicable (self-relation rejection returns a value rather than throwing, matching `ProvinceOccupationSystem`'s no-op-on-invalid-input style for debug-tool inputs).

Use the implement skill to start working on the plan or request changes.
