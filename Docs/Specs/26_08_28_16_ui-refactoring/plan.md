# Plan: UI Refactoring — Component Decomposition, Gallery Coverage, PanelRenderer Migration, Cold-Panel Pull Model

## Spec

Source: `Docs/Specs/26_08_28_16_ui-refactoring/spec.md` (approved; all owner clarifications folded in — do not re-open). Companion options study: `analysis.md` in the same folder. The spec's **Tech Notes** are authoritative for the component inventory, the decomposition tables, the PanelRenderer facts and the 7-phase **Sequencing**; this plan executes that order and does not invent a different one.

**Intent.** Every UI element and window previewable and restylable in the Gallery scene, assembled from small reusable components named after what they *are*; the UI moved off the soft-deprecated `UIDocument`; the debug tooling out of the shipping HUD; and the frame budget spent on the simulation and on what is actually on screen rather than on projecting leaderboards behind closed windows.

**Acceptance criteria (summary — full text in `spec.md` §Acceptance Criteria, nine `Precondition => Action => Outcome` groups).**
1. Gallery: one collapsible block per element with real localized text/flags/art and the game's own stylesheets, with instance + state dropdowns per block, independent per block, working with no game, no ECS world, no save.
2. Live edit: saving a UXML/USS/C# file re-renders within a second with every expansion, selection and scroll position preserved — in the Gallery *and* in the running game.
3. Code navigability: one class per component, per panel, per window; debug tooling is its own surface/folder/assembly/markup; the 1764-line document is gone.
4. Reuse: exactly one component per reusable element, one piece of markup, one place its styling lives, editable in UI Builder.
5. Lists: leaderboard, goals, end-game comparison, war battles and save list stay smooth in a 154-country world, scroll position preserved.
6. Cold panels: with every window closed, the projection half of the loop is measurably cheaper than the recorded 4 ms / 200 KB baseline (Editor Profiler, `GameLoop.UpdateVisualState`, comparable world, numbers written into this spec folder); windows are correct on open and stay current; nothing edge-triggered is missed.
7. Controls: every button responds on the first click, disabled controls do nothing, panels over the map swallow their clicks, HUD flags render at full quality.
8. Migration: every surface renders, takes pointer input and layers as before; no deprecation warning left; stylesheet live-edit rebinds without per-frame checking code.
9. Debug tooling is excludable later as one self-contained unit; the docs agree with each other and with the code on pointer-over-UI and click handling.

**Out of Scope** (spec §Out of Scope, verbatim topics): option E data binding; deleting `VisualState`; any visual redesign (except the accepted `RankRow` unification); new gameplay/windows/locale keys; benchmarks; player-build performance measurement; **introducing Unity test assemblies**; adding the Gallery scene to `EditorBuildSettings`; `parentUI` panel composition.

## Goal

Land the spec's six agreed workstreams (A gallery, B components, C `ListView`, D tokens, F fixes, G pull model) plus the debug extraction and the `PanelRenderer` migration as seven independently shippable phases, keeping every projection rule inside `src/` and under `dotnet test`, and keeping the Gallery block for a surface as the regression check that must exist *before* that surface is refactored.

## Approach

**Phase order is the spec's**: 1 F+D → 2 G → 3 components → 4 `ListView` → 5 debug extraction → 6 PanelRenderer go/no-go → 7 panel/window decomposition with the migration riding along. Each phase compiles, plays and ships on its own; nothing later is a prerequisite for anything earlier.

**Verification model.** The project has **zero Unity test assemblies** and adding them is Out of Scope, so only `src/` changes get automated coverage (phase 2, plus the small `src/` touch in phase 5). Everything Unity-side is verified by: (a) the surface's Gallery block, landed *before* the refactor and compared after; (b) `refresh_unity` + `read_console(types=["error"])` after every script/asset change; (c) the manual User Steps. Per `.claude/rules/unity/mcp_usage.md` the agent never self-tests in Play mode — Play-mode judgement is a User Step.

**Pull model shape (phase 2).** Each cold projection becomes a `public static Project(IReadOnlyWorld, …) -> DTO` in `src/Game.Main`, shaped like the existing `GoalsProjector` / `SelectedWarProjector` / `WarIconsProjector`. The Unity side decides only *when*: on open, immediately after any command the window itself pushes, and otherwise on a 250 ms wall-clock accumulator ticked from the owning document's existing `Update`, skipped entirely while the game is paused. No projection rule moves Unity-side.

**Component shape (phase 3).** One C# builder class + one `.uxml` per component under `Assets/UI/Components/<Name>/`, instantiated via `VisualTreeAsset.Instantiate()`, styled by the single shared `Assets/UI/Components/Components.uss` (one `<ui:Style>` import per hosting document, per the spec's stated reason). Components go into plain subfolders of the existing `Assets/Scripts/Unity/UI/` — **no new asmdefs** except the debug assembly in phase 5.

**Migration shape (phases 6–7).** `PanelRenderer.rootVisualElement` is `internal`; the only public route is `RegisterUIReloadCallback` / `UnregisterUIReloadCallback`. Every migrated MonoBehaviour inverts from "pull the root in `Awake`/`OnEnable`" to "receive the root in the reload callback", which also deletes the Gallery's per-frame `panel != null` polling. `sortingOrder` becomes `int` — no fractional sorting orders after the migration.

## Agent Steps

### Phase 1 — Standalone fixes (F) + USS design tokens (D)

- [ ] **Add the `.OnClick()` extension** — new `Assets/Scripts/Unity/UI/VisualElementClickExtensions.cs` in `GS.Unity.UI`: button 0, `ContainsPoint`, and **`enabledInHierarchy`, not `enabledSelf`** (the spec's draft used `enabledSelf`, which does not see a disabled ancestor; criterion 7 requires disabled controls to be inert *consistently, on every surface*, and this helper becomes the project-wide click rule). Tabs + braces per `.claude/rules/csharp/code_style.md`.
- [ ] **Migrate the 22 `.clicked` sites** — `HUDDocument.cs` (6), `SettingsWindowDocument.cs` (11), `TimeView.cs` (4), `LoadWindowDocument.cs` (1); counts verified against the current tree.
- [ ] **Collapse the 58 hand-rolled `PointerUpEvent` sites** — across 23 files; the several debug sites that also hand-roll an `enabledSelf` check lose that duplication.
- [ ] **Fix pointer-over-UI in `HUDDocument`** — replace `HUDDocument.cs:737`'s `EventSystem.current…IsPointerOverGameObject()` with `UIPointerState.IsPointerOverUI`, which is a **method taking a screen position**, not a property: the call is `_pointerState.IsPointerOverUI(mouse.position.ReadValue())`. Drop `using UnityEngine.EventSystems;` (`HUDDocument.cs:5`).
- [ ] **Raise the dynamic-atlas sub-texture cap** — `Assets/UI/HUD/HUDPanelSettings.asset` `m_DynamicAtlasSettings.m_MaxSubTextureSize: 64` → `128` so the 128×128 flags are no longer excluded (confirmed at the cited line).
- [ ] **Import `SharedStyles.uss` into `WarIcons.uxml`** — add `<ui:Style src="project://database/Assets/UI/Shared/SharedStyles.uss"/>` before the local style; it is the only UXML missing it while using `gs-btn`.
- [ ] **Remove dead binding metadata** — delete `data-source-type="GS.Main.VisualState, Game.Main"` from `hud-root` in `Assets/UI/HUD/HUD.uxml:18`.
- [ ] **Declare `--gs-*` tokens** — on `:root` in `Assets/UI/Shared/SharedStyles.uss` (583 lines today), mirroring the token names in `Design/01_prototype/design-final.html`; values capture the existing look exactly, no redesign.
- [ ] **Repoint the literals to tokens** — the 184 colour/font literals across 23 stylesheets; shared classes (`gs-panel`, `gs-btn*`, `gs-title/header/label/content/hint`, `gs-color-*`, `gs-bg-*`, `gs-border-*`) keep their names and start reading tokens; per-feature USS keeps its layout-only rule; remove `Gallery.uss`'s hand-copied `.gs-header` font/colour duplication for the foldout toggle and its "keep in step manually" comment.
- [ ] **Reconcile the rules docs** — `.claude/rules/unity/uitoolkit.md:160-173` currently mandates `EventSystem.current.IsPointerOverGameObject()` while `.claude/rules/unity/localization.md` §"Click Blocking for Modal Dialogs" says that is unreliable; make both say `ModalState` / `UIPointerState`, and restate the click rule as "call `.OnClick()`".
- [ ] **Refresh and check** — `refresh_unity` + `read_console(types=["error"])`.

### Phase 2 — Cold-panel pull model (G)

- [ ] **Extract `LeaderboardProjector` into `src/Game.Main`** — lift `VisualStateConverter.UpdateLeaderboards` (`VisualStateConverter.cs:1021`) plus `SortAndAssignPlaces` / `GetCountryDisplayName` into a `public static` projector returning the org + country entry lists; drop the call from the per-tick `Update` (`:97`).
- [ ] **Give `GoalsProjector` a pull entry point** — `UpdateGoals` (`:1051`) already delegates to `GoalsProjector.Build`; expose the leaf/pool construction the converter holds so a caller can project without the converter, then drop the per-tick call (`:98`).
- [ ] **Pull `SelectedWar`** — `UpdateSelectedWar` (`:326`) into `SelectedWarProjector`'s existing shape; `SelectedWar.PendingWarId` has no world backing and **stays** in `VisualState`.
- [ ] **Pull debug card availability** — `UpdateDebugOrgCardAvailability` (`:721`) into a `DebugOrgCardAvailabilityProjector`; the four `DebugOrgCardVisibility` flag gates wired from `HUDDocument.cs:258` go away with it.
- [ ] **Add the refresh-cadence helper** — a small plain C# `PullRefreshTimer` in `GS.Unity.UI`: project on open, project after any command the window pushes, otherwise accumulate wall-clock and re-project every 250 ms, skipping entirely while paused.
- [ ] **Convert the four window documents that read pulled state** — `LeaderboardWindowDocument`, `GoalsWindowDocument` (needs **both** `LeaderboardProjector` and `GoalsProjector` — `GoalsWindowView.Refresh(LeaderboardState, GoalsState)`), `WarProgressWindowDocument`, `EndGameWindowDocument`: call the projector(s) from `Show()`/the open path and from the timer in `Update`, pass the DTO straight to the view, and delete **only** the subscriptions to sub-states that are actually pulled.
- [ ] **Keep the still-pushed subscriptions on those same documents** — `EndGameWindowDocument` subscribes to `GameCompletion` (`:67`), `Leaderboard` (`:68`), `PlayerOrganization` (`:69`) and `Locale` (`:70`); only `Leaderboard` is pulled. `GameCompletion` is what *opens* the window (`:96`, `:112`) and `Locale` drives re-render on language change. Deleting a document's subscribe/unsubscribe pair wholesale would break both — remove subscriptions one by one, not by the pair.
- [ ] **Leave `WarResultWindowDocument` on push entirely** — it reads only the `WarResults` queue (`:92`, `:125`, `:184`, `:201`, `:229`) plus `_state.Time`, which are edge-triggered carve-outs this phase must not touch. There is nothing to pull; its subscriptions stay exactly as they are.
- [ ] **Repoint `HUDDocument`'s three debug-only leaderboard readers** — `GetOrgDisplayName` (`:1018`), `RebuildControlOrgDropdown` (`:1034`) and `GetOpponentOrgId` (`:1594`) are the *only* HUD consumers of `VisualState.Leaderboard`, and all three are debug-panel-only; make them pull via `LeaderboardProjector` behind the existing `_debugPanelOpen` flag and delete the subscription at `HUDDocument.cs:604`. Without this, phase 2 cannot take the leaderboard off the per-tick path.
- [ ] **Fix the `Game.Benchmarks` compile break before deleting `VisualState.Leaderboard`** — `src/Game.Benchmarks/ListVisualStateSetBenchmarks.cs:126` does `_leaderboard = visualState.Leaderboard;`, and `Game.Benchmarks` is in `GlobalStrategy.Core.sln` (line 50), so both the mandatory `dotnet test` and `/dotnet-build Release` would fail. Retarget that fixture to the `LeaderboardProjector` DTO. This is a **compile fix to a harness, not new benchmark work** — it does not reopen the Out-of-Scope benchmark item, and no baseline is re-measured.
- [ ] **Delete the now-dead state and diffing** — the pulled sub-states and their `StateEquality` members. **Do not touch** the still-pushed `GameCompletion`, `PlayerOrganization`, `Locale` and `Time` sub-states on the converted windows, nor the edge-triggered observations (`UpdateGameLog` `:1134`, `UpdateLastFrameEffects` `:112`, `UpdateProvinceOwnership` `:957`, the `WarResults` / `CountryDestroyedResults` / `OrgDestroyedResults` queues) or the world-unbacked state (`MapLens`, `SelectedWar.PendingWarId`, `SaveResult`, `Animatable*` + `AnimationBarrier*`). `VisualState` shrinks; it does not disappear.
- [ ] **Test and build** — new/retargeted projector tests (see Tests), then `dotnet test src/GlobalStrategy.Core.sln`, then `/dotnet-build Release` (required after any `src/` change — it refreshes `Assets/Plugins/Core/`).
- [ ] **Record the measurement** — write the before/after `GameLoop.UpdateLogic` / `GameLoop.UpdateVisualState` time and allocation numbers the user captures (User Steps 1 and 2) into `Docs/Specs/26_08_28_16_ui-refactoring/measurements.md`, stating the world size and that both readings used the Editor Profiler. Do not cite `Docs/Benchmarks/baseline.json`.

### Phase 3 — Component inventory (B, delivered as the component tier)

- [ ] **Give the Gallery its own DI entry point** — add `GalleryLifetimeScope` in `Assets/Scripts/Unity/Gallery/` (assembly `GS.Unity.Gallery`), **not** in `GS.Unity.DI`: the scope must name `GalleryDocument` and the block types, so putting it in `GS.Unity.DI` would need `GS.Unity.DI` → `GS.Unity.Gallery` → `GS.Unity.DI`, a cycle Unity rejects. Add a `ProjectLifetimeScope` GameObject to `Assets/Scenes/Gallery.unity` and set the new scope's `parentReference.TypeName` to `GS.Unity.DI.ProjectLifetimeScope` — this is the actual `MainMenuLifetimeScope` precedent (`MainMenu.unity:201`), which does *not* register `ILocalization` itself but inherits it from the project scope. `GalleryLifetimeScope` therefore registers only the gallery-specific configs currently held as `[SerializeField]`; `ILocalization`, `SettingsStorage` and `IPersistentStorage` come from the parent. `GalleryDocument` stops `new`-ing `CustomLocalization`, `SettingsStorage` and `PersistentStorage` (`GalleryDocument.cs:77`) and receives them by `[Inject]`; every block resolves through the scope. This must land **before** the block count grows, so the `new` pattern is not copied 40 more times. `GS.Unity.Gallery.asmdef` gains only the VContainer GUID `b0214a6008ed146ff8f122a6a9c2f6cc`.
- [ ] **Move the Gallery's first build out of `OnEnable`** — VContainer injects during the scope's `Awake`/`Build`, which can run *after* `GalleryDocument.OnEnable`, so the injected `ILocalization` may still be null there. Follow `HUDDocument`: guard `OnEnable` on the injected field being non-null and do the first `Bind()` from `Start()`.
- [ ] **Split the Gallery host first** — `GalleryDocument` becomes a host owning an ordered list of blocks, building one `ui:Foldout` per block; each block owns its own two `DropdownField`s and its own state switch. Move `FitDropdownToWidestChoice` (`GalleryDocument.cs:314`) to the host, and replace the three per-block serialized fields (`GalleryDocument.cs:57-59`) with a serialized per-block-id record so expansion + both selections survive domain reload. Do not let `GalleryDocument` grow into a second `HUDDocument`.
- [ ] **Create the component home** — `Assets/UI/Components/` plus `Assets/UI/Components/Components.uss` (one shared sheet, component classes only, tokens only — no new colour/font literals); add its single `<ui:Style>` import to `HUD.uxml` and to each hosting window UXML.
- [ ] **Atoms** — `FlagBadge`, `ResourceChip`, `StatChip`, `ProgressBar`; each gets a C# builder, a `.uxml`, entries in `Components.uss` and a Gallery block; repoint the six flag classes (`entity-flag`, `relations-flag`, `goals-row-flag`, `leaderboard-row-flag`, `war-result-province-flag`, `war-icon-flag`) and the resource/stat/progress classes named in the spec's table (all verified present at the cited stylesheets).
- [ ] **Rows** — `RankRow`, `EffectRow`, `BattleRow`, `ProvinceTransferRow`, `RequirementRow`; same treatment. These are the `makeItem` targets phase 4 depends on, so they must land here.
- [ ] **Unify the Goals and Leaderboard row styling** — `RankRow` replaces both `goals-row` (`GoalsWindow.uss`) and `leaderboard-row` (`LeaderboardWindow.uss` + `EndGameWindow.uss`); a small visual change to one or both is the spec's one accepted exception to "no redesign".
- [ ] **Cards** — `ActionCard`, `CharacterCard` (unifying `char-card` / `org-char-card` with an `--empty` variant), `TaskCard`. `ActionCard` absorbs **both** copies of `ComposeFaceData`: the private one in `CountryActionsView` and `GalleryDocument.ComposeFace` (`GalleryDocument.cs:255`). Do not add a third copy for any new block.
- [ ] **Composites** — `TooltipBody`, `HandContainer`, `DrawSlot`, `FlagNameHeader`; `TooltipBody` replaces the hand-built tooltip content in all six views (`CharactersView`, `CountryInfoView`, `OrgCharactersView`, `ResourcesView`, `WarIconsView`, `WarProgressLayoutBinder`).
- [ ] **Widen access where a block needs it** — prefer `public` over `InternalsVisibleTo` per `.claude/rules/csharp/code_style.md`, as already done for `ActionConditionText` (`CountryActionsView.cs:507`).
- [ ] **Leave `ActionLogView` alone** — its identity-keyed incremental diff with per-element fade transitions is documented in `.claude/rules/unity/uitoolkit.md`; extracting its rows would break it. Panel and window *shells* are likewise not components.
- [ ] **Refresh and check** — `refresh_unity` + `read_console(types=["error"])` after each component batch.

### Phase 4 — `ListView` virtualization (C)

- [ ] **Leaderboard list** — `LeaderboardWindowView.cs:33` (`leaderboard-list`): `ScrollView` + `Clear()`/rebuild → `ListView` with `makeItem` returning a `RankRow` and `bindItem` applying the row data.
- [ ] **End-game lists** — `EndGameWindowView.cs:24,26` (`end-game-leaderboard-list`, `end-game-comparison-list`), both on `RankRow`.
- [ ] **War lists** — `WarProgressLayoutBinder.cs:70-72` (`attacker-effects-list`, `defender-effects-list`, `battles-list`), shared by `WarProgressWindow.uxml` and `WarResultWindow.uxml`; `EffectRow` and `BattleRow`.
- [ ] **Save list** — `LoadWindowDocument.cs:34` (`save-list`).
- [ ] **Goals lists** — `GoalsWindowView` `goals-org-list` and `goals-progress-list` (`:39-42`, `RefreshProgressPanel`) are `.Clear()`-rebuilt plain `VisualElement`s, not `ScrollView`s, and were missing from this phase despite criterion 5 and User Step 8 both naming goals. Convert to `ListView` on `RankRow` and `ProgressBar` + `RequirementRow`, keeping the row click that drives `SelectOrg`. If the org count makes virtualization pointless, record that decision here and strike "goals" from criterion 5 and User Step 8 rather than leaving it silently unimplemented.
- [ ] **Confirm the compounding with phase 2** — the rebuild trigger is now the coarse 250 ms refresh, not every tick. Scroll-position survival across a refresh on each list surface is Play-mode judgement: hand it to User Step 8 and **do not self-test** (`.claude/rules/unity/mcp_usage.md`).

### Phase 5 — Debug UI extraction

- [ ] **Create the assembly** — `Assets/Scripts/Unity/DebugTools/` with `GS.Unity.DebugTools.asmdef` per `.claude/rules/unity/asmdef.md` (GUID references, `autoReferenced: true`). Name it `DebugTools`, not `Debug`: a `GS.Unity.Debug` namespace would shadow `UnityEngine.Debug` inside its own files.
- [ ] **Move the debug members out of `HUDDocument`** — the ~36 members the spec enumerates (`BuildProvinceDebugUi` :400 … `PushImproveOpinionCommand` :1731 — line references verified) plus `DebugCardAvailabilityView.cs` and the ~30 `_btnDebug*` / `_*DebugMenu` / `_*RawGoldLabel` fields.
- [ ] **Move the markup** — the `debug-panel` → `debug-panel-scroll` subtree from `Assets/UI/HUD/HUD.uxml` (spec cites lines 43-112; the subtree currently opens at line 42 and closes at ~113) into `Assets/UI/Debug/Debug.uxml`, with `HUD.uss`'s debug-only classes (`debug-condition-label`, `debug-panel-scroll`, `debug-panel-inner`, `debug-panel-menu-toggle`, …) into `Assets/UI/Debug/Debug.uss`.
- [ ] **Add the debug UI surface to the scene** — via MCP (`manage_gameobject` / `manage_components` / `manage_scene(save)`) on `Assets/Scenes/Map.unity`, sharing `HUDPanelSettings` with an explicit `sortingOrder` below `FlyTextNotifierDocument`'s 1000; register the new document in `GameLifetimeScope` with `RegisterComponentInHierarchy`.
- [ ] **Wire the assembly references both ways** — remove `GS.Unity.EcsViewer` from `GS.Unity.UI.asmdef` and the `using GS.Unity.EcsViewer;` from `HUDDocument.cs:12`. `GS.Unity.DebugTools.asmdef` takes `GS.Unity.UI` (`31616c5c35fcc3c418ca03ade2c0cfb9`), `GS.Unity.EcsViewer` (`41cd3871486c6f74198dee3483866912`), `GS.Unity.Common` (`7e5a37e68b84aeb48bf5de2cbe39a94e`) and VContainer (`b0214a6008ed146ff8f122a6a9c2f6cc`). `GS.Unity.DI.asmdef` gains `GS.Unity.DebugTools` — **required** by the `GameLifetimeScope` registration in the step above, and easy to miss — as does `GS.Unity.Gallery.asmdef`. Keep the direction one-way: nothing in `GS.Unity.UI` may reference `GS.Unity.DebugTools`.
- [ ] **Gallery blocks for the debug surfaces** — debug panel, province / relation / control-org / character sub-menus, `DebugCardAvailabilityView`, FPS counter.
- [ ] **Confirm nothing changed about shipping** — the debug UI is still present in builds; only its excludability changed. If any `src/` file was touched (e.g. `DebugOrgCardVisibility`), run `/dotnet-build Release`.

### Phase 6 — PanelRenderer, step one (go/no-go)

- [ ] **Migrate the Gallery scene only** — swap the `UIDocument` on the Gallery's UI GameObject for `PanelRenderer`, keeping the screen-space `HUDPanelSettings` (`m_RenderMode: 0`; migration does **not** move the UI into world space).
- [ ] **Invert `GalleryDocument` to the reload callback** — `RegisterUIReloadCallback` / `UnregisterUIReloadCallback` instead of `rootVisualElement`, and **delete** the per-frame `_cardDropdown.panel != null` polling in `GalleryDocument.Update()` (`GalleryDocument.cs:87-96`) that the callback replaces.
- [ ] **Add a throwaway second surface for the layering check** — `Gallery.unity` has exactly one UI GameObject today, so User Step 4(c) is unanswerable as it stands. Add a second GameObject with a `PanelRenderer`, the same `HUDPanelSettings`, a one-element UXML overlapping the gallery root, and `sortingOrder: 1`. It exists only to make check (c) answerable and is deleted once the go/no-go is recorded.
- [ ] **Hand over for the go/no-go check** — the four checks (renders; foldouts + dropdowns take pointer input; a second overlapping surface layers by `sortingOrder`; editing `Gallery.uss` rebinds through the callback with the polling gone) can only be judged in Play mode: User Step 4.
- [ ] **If the check fails: stop and record** — revert the Gallery scene and `GalleryDocument` to `UIDocument`, write exactly what failed (which of the four, with console output and the SRP-culling behaviour observed, since `PanelRenderer` is a `Renderer` and `UIDocument` is not) into `Docs/Specs/26_08_28_16_ui-refactoring/panelrenderer-findings.md`, and drop the migration from phase 7 — phase 7's decomposition then proceeds unchanged on `UIDocument`, since everything else in the spec is independent of the migration. **Also drop, explicitly:** phase 7's `sortingOrder` audit and its "no `UIDocument` left / no deprecation warning" final sweep, both of which become impossible. Record in `panelrenderer-findings.md` that acceptance criterion group 8 (all rows) and criterion group 2's "does the same while the *game* is running" row are then **not met**, and that they need an owner decision — do not close them silently or let the phase read as if nothing was lost.

### Phase 7 — Panel and window decomposition (migration riding along)

Per surface, strictly in this order: **land its Gallery block → split into document + view → convert that same class to `RegisterUIReloadCallback`.** The Gallery block is the only regression check available, and doing the split and the migration in one pass avoids rewriting all 18 classes twice.

- [ ] **HUD panel binders** — one binder per HUD panel, each owning only its own subscribe/refresh pair, replacing the ~30 pairs centralised in `HUDDocument.OnEnable` (:572) / `OnDisable` (:627); `HUDDocument` shrinks to a composition root that constructs binders and routes `Refresh`.
- [ ] **HUD panel Gallery blocks** — `CountryInfoView`, `ProvinceInfoView`, `ResourcesView`, `PlayerOrgView`, `PlayerTasksView`, `TimeView`, `LensSwitcherView`, `OrgLensCountryView`, `ActionLogView`, `WarIconsView`, `TutorialHighlightView`, `TooltipController` content, `FlyTextNotifierDocument` text.
- [ ] **Hand/deck and animation blocks** — `CountryActionsView` hand row, `OrgActionsView`, `CardDrawView`, `CardTransitionView`, and static frames of `CardPlayAnimator` / `CardDrawAnimator`; plus `CharactersView` / `OrgCharactersView` slots and the `OrgInfoDocument` overlay.
- [ ] **The seven windows that already have a view** — `LeaderboardWindow`, `GoalsWindow`, `WarProgressWindow` (+ the `WarProgressLayout` subtree), `WarResultWindow`, `EndGameWindow`, `CountryDestroyedWindow`, `OrgDestroyedWindow`: Gallery block, then migrate. No view extraction needed.
- [ ] **The six view-less documents** — `MainMenuDocument`, `GameMenuDocument`, `SettingsWindowDocument`, `LoadWindowDocument`, `SelectOrgDocument`, `OrgInfoDocument`: extract a plain `Refresh(state)` view **first** (their Gallery block is impossible without it), then block, split and migrate. These come after the seven above.
- [ ] **Remaining migration targets** — `FlyTextNotifierDocument`, `CardPlayAnimator`, `CardDrawAnimator` (plus `HUDDocument` and `GalleryDocument` already covered), completing all 18.
- [ ] **`sortingOrder` audit** — `PanelRenderer` inherits `Renderer.sortingOrder` (`int`) where `UIDocument.sortingOrder` was `float`. Only `FlyTextNotifierDocument._topMostSortingOrder` (1000) and the windows' explicit constants (e.g. `LeaderboardWindowDocument.SortingOrder = 500`) are non-zero today, so nothing relies on fractional ordering — record the standing constraint that none may be introduced, and update `.claude/rules/unity/uitoolkit.md` §"Layer Model" to say `PanelRenderer` + `int`.
- [ ] **Final sweep** — no `UIDocument` left in any scene, no deprecation warning in the editor, `refresh_unity` + `read_console` clean.

## User Steps

These require Unity Editor Play mode, the Profiler, or human visual judgement — the agent must not self-test in Play mode (`.claude/rules/unity/mcp_usage.md`).

### 1. Capture the phase-2 baseline (before phase 2 starts)

Open a comparable late-game world (~154 countries) with **every window closed**, attach the Editor Profiler, and record one representative frame's time and allocation for the `GameLoop.UpdateLogic` and `GameLoop.UpdateVisualState` markers. The recorded reference is 8 ms / 40 KB and 4 ms / 200 KB. Report the numbers and the world size back so they can be written into `measurements.md`.

### 2. Re-measure after phase 2

Same world, same window-closed condition, same markers, same Editor Profiler. Report the numbers. The acceptance bar is a measured reduction against step 1's reading, not a millisecond target — and not a BenchmarkDotNet number.

### 3. Confirm the atlas and click fixes (after phase 1)

Play the game and confirm HUD country flags render at full quality (the 128×128 flags now fit the dynamic atlas), that every button still responds on the first click across HUD, windows, settings and time controls, that disabled controls do nothing, and that clicking a panel over the map does not also move the map.

### 4. PanelRenderer go/no-go (phase 6, blocking)

Open `Assets/Scenes/Gallery.unity` and press Play. Judge four things and report each pass/fail: (a) the UI renders at all; (b) the foldouts and dropdowns take pointer input; (c) a second overlapping surface layers correctly by `sortingOrder`; (d) editing `Gallery.uss` while playing rebinds through the reload callback with the polling code deleted, preserving expansion, selections and scroll. **If any fails, phase 6 stops and reverts** and the migration drops out of phase 7 — say which failed and paste any console output.

### 5. Gallery block confirmation (phases 3, 5 and 7, per batch)

For each batch of new Gallery blocks: press Play in the Gallery scene, expand every new block, step both dropdowns, and confirm the element looks the same as it does in the running game — including localized text, flags and art. Expand several blocks at once and confirm their selections stay independent. This is the regression check that stands in for the missing Unity tests, so it must happen *before* the matching surface is refactored.

### 6. Live-edit loop confirmation

With the Gallery playing, save a change to a component `.uxml`, then to `Components.uss`, then to a `--gs-*` token in `SharedStyles.uss`, then let a C# recompile run. Each time, confirm the change appears within about a second and that every expansion, dropdown selection and scroll position is exactly where it was. Repeat one stylesheet edit while the *game* is running.

### 7. Debug surface confirmation (after phase 5)

In `Assets/Scenes/Map.unity`, confirm the debug panel appears as its own surface, layers correctly, and that every tool still works exactly as before: province, relation, control-org and character sub-menus, gold buttons, force-destroy, card draw/discard, the FPS counter and the ECS viewer button. Confirm the shipping HUD looks unchanged.

### 8. Full parity play-through (after phases 4, 5 and 7)

Play through a war, a country's destruction, an org's destruction, a card play and a province changing hands. Confirm every log line, fly-text, result window and animation still appears exactly as before, that fly-text still renders above everything, that opening leaderboard / goals / war-progress / end-game shows correct current data immediately and stays current, and that the lists (leaderboard, goals, comparison, battles, saves) scroll smoothly without losing position.

## Tests

**What is actually testable.** Only `src/` is covered — `src/Game.Tests` has 144 test files, 15 projector-specific, against **zero** Unity test assemblies, and introducing them is Out of Scope. So phase 2 (and any `src/` touch in phase 5) is the only automated-test surface; phases 1, 3, 4, 6 and 7 have no unit tests at all, by design, and their regression check is the Gallery block landed before the refactor plus the User Steps above.

- **`LeaderboardProjector`** — retarget the existing `VisualStateConverterLeaderboardTests` to the extracted projector rather than deleting it; keep its ordering, tie-breaking and place-assignment coverage and add a large-world (154-country) case.
- **`GoalsProjector`** — extend `GoalsProjectorTests` to cover the new pull entry point including the leaf/pool construction that moves out of the converter's constructor.
- **`SelectedWarProjector`** — extend `SelectedWarProjectorTests` for the pull entry point; assert `PendingWarId` is untouched by it.
- **`DebugOrgCardAvailabilityProjector`** — new tests mirroring the removed `DebugOrgCardVisibility`-gated behaviour in `VisualStateConverterCountryActionsVisibilityTests`' style.
- **Per-tick regression** — a test asserting that one `VisualStateConverter.Update` with no window open no longer touches leaderboard/goals/selected-war/debug-availability state, and that the edge-triggered paths (`UpdateGameLog`, `UpdateLastFrameEffects`, `UpdateProvinceOwnership`, the three result queues) still fire on the same tick they did before — this is the "nothing between two frames is missed" criterion.
- **`StateEquality`** — update for the removed members; keep the animation-barrier-sensitive comparisons (`ResourceStateEntryEquals`) intact.
- **Regression suite** — `dotnet test src/GlobalStrategy.Core.sln` must be green before phase 2 is considered done; all 15 projector-specific files stay green.
- **Build** — `/dotnet-build Release` after every phase that touches `src/` (phase 2 certainly; phase 5 if `DebugOrgCardVisibility` moves), because Release is what refreshes `Assets/Plugins/Core/`.
- **Not a test** — `Docs/Benchmarks/baseline.json` / `VisualStateConverterBenchmarks.Update` reads ~100× cheaper than the Unity Profiler for the same projection; it must not be used to size or verify phase 2, and investigating the gap is Out of Scope.

## Spec corrections applied

Everything else in the spec was verified against the tree and holds exactly — the 1764-line `HUDDocument`, the 55 files / 11418 lines, all the `HUDDocument` and `VisualStateConverter` line references, the 22 `.clicked` (6/11/4/1 across the four named files) and 58 `PointerUpEvent` sites, `HUD.uxml:18`, `HUDPanelSettings` `m_MaxSubTextureSize: 64`, `WarIcons.uxml`'s missing import, the Gallery's absence from `EditorBuildSettings`, all five `ListView` target line references, and every USS class in the component inventory at the stylesheet the spec names.

Six things differed. All six have been **corrected in `spec.md`** with the owner's approval, so spec and plan now agree — recorded here only as a change log:

1. **`EndGameComparison` is already pull-shaped.** The spec listed it among the things to move off the per-tick path, but `EndGameComparisonProjector.Build` is already called on demand from `EndGameWindowView.cs:131`, fed by `GameSettings.EndGameComparisons` config — it never runs per tick. What the end-game window actually costs per tick is `UpdateLeaderboards`, which it consumes. The projector is left alone.
2. **`ActionsPanelOpen` is wired from `CountryInfoView.cs:223`**, not `CountryActionsView.cs:223` — right line, wrong filename.
3. **`HUDDocument`'s only `VisualState.Leaderboard` consumers are debug-only** (`GetOrgDisplayName`, `RebuildControlOrgDropdown`, `GetOpponentOrgId`, subscription at `:604`). The leaderboard cannot leave the per-tick path until they pull, so phase 2 carries a dedicated step for them, ahead of the phase-5 debug extraction. This was absent from the spec's call-site list and has been added.
4. **`Assets/UI/Modal/WarProgressLayout/` contains only `WarProgressLayout.uss`** — there is no `WarProgressLayout.uxml`; the layout is authored inline in both `WarProgressWindow.uxml` and `WarResultWindow.uxml`. Its phase-7 Gallery block previews the shared subtree via `WarProgressLayoutBinder`, not a template.

5. **The Gallery is no longer DI-free.** `spec.md` §Gallery completeness said the prototype "is deliberately DI-free — no `GalleryLifetimeScope`", and `analysis.md` §Decisions repeated "no lifetime scope". The owner's constitution check restored the DI principle here, so phase 3 adds a `GalleryLifetimeScope`. Both documents now say the DI-free part is superseded while the "no `VisualState`, no `GameLogic`" part stands (that half is criterion group 1 and is permanent).
6. **`.OnClick()` uses `enabledInHierarchy`, not `enabledSelf`.** The spec's draft implementation checked `enabledSelf`, which does not see a disabled *ancestor*. Criterion 7 requires disabled controls to be inert "consistently, on every surface", and this helper becomes the project-wide click rule for all future code. Latent rather than live today — all 14 current `SetEnabled` sites target buttons directly — but a one-word fix. Corrected in the spec's code block.

Also applied: the new debug assembly is named **`GS.Unity.DebugTools`** (folder `Assets/Scripts/Unity/DebugTools/`), not `Debug` — a `GS.Unity.Debug` namespace would shadow `UnityEngine.Debug` at every call site with `using UnityEngine;`. The markup folder stays `Assets/UI/Debug/`, which has no such collision.

Note: the spec's phase-7 sentence "The five view-less documents (`MainMenuDocument`, `GameMenuDocument`, `SettingsWindowDocument`, `LoadWindowDocument`, `SelectOrgDocument`, plus `OrgInfoDocument`)" lists six; this matches its own Tech Notes ("five windows … `OrgInfoDocument` is the same shape") and is read here as six surfaces. Left as written.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts found — plan aligns with all principles.**

Three points of tension were raised against the original draft and all three are now resolved, two of them by owner-approved amendments to the constitution itself:

1. **VContainer is the sole DI mechanism** — *resolved in the plan.* The original draft kept the Gallery's DI-free `new` of `CustomLocalization` / `SettingsStorage` / `PersistentStorage` (`GalleryDocument.cs:77`) as a documented carve-out. That carve-out is withdrawn: phase 3's first step gives the Gallery a real `GalleryLifetimeScope`, following the existing `MainMenuLifetimeScope` / `SelectCountryLifetimeScope` precedent, and every block resolves through it. No exception to the principle is now claimed, and the step is sequenced before the block count grows so the `new` pattern is not copied 40 more times.
2. **ECS for all game logic, living in `src/`** — *resolved by constitution amendment.* `Docs/Constitution.md` §Game Logic now carries "Projection scheduling is presentation, not logic": deciding *when* a projection runs may live in a MonoBehaviour, while the projection stays a pure `Project(IReadOnlyWorld, ...) -> DTO` in `src/` under `dotnet test`, and pull-on-demand is preferred over per-tick push for anything a closed surface does not display. Phase 2 is exactly that shape, and the plan's per-tick regression test enforces that no rule moved Unity-side.
3. **One `.asmdef` per feature folder under `Assets/Scripts/`** — *resolved by constitution amendment.* §Assembly Structure now states that the required boundary is the feature level `Assets/Scripts/<Tier>/<Feature>/`, and that deeper nesting is organisation, not a new assembly — a judgement call on subfolder size and scope. Phase 3's plain subfolders inside `Assets/Scripts/Unity/UI/` are therefore compliant by the rule rather than by exception, and the one genuinely new feature, the debug tooling, still gets its own folder and its own `GS.Unity.DebugTools.asmdef` in phase 5.

Aligned with no tension: **Rendering** (URP untouched; the `PanelRenderer` SRP-culling question is an explicit go/no-go check, not an RP change), **UI Toolkit only** (UXML/USS + MonoBehaviour/View pairs throughout; no Canvas or uGUI), **Plan before implement** and **Spec before plan** (approved `spec.md`, this plan, colocated), **File organisation** (`Docs/Specs/26_08_28_16_ui-refactoring/`), and **C# code style** (tabs, `_`-prefixed private members, braces always, no redundant access modifiers — including the `.OnClick()` extension and every new component builder).

Use the implement skill to start working on the plan or request changes.
