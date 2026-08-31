# Spec: UI Refactoring — Component Decomposition, Gallery Coverage, PanelRenderer Migration, and the Cold-Panel Pull Model

Companion to `analysis.md` in this folder. That document is the pre-spec options study; this
document turns its agreed verdicts — **A** (gallery), **B** (UXML templates), **C** (ListView),
**D** (USS tokens), **F** (standalone fixes), **G** (pull model) — plus the three additional
requests below into acceptance criteria. Option **E** (presentation-model data binding) keeps its
"skip for now" verdict and appears only under Out of Scope.

The three additional requests, verbatim:

> - split all documents, UXMLs / USS to more dedicated classes depends on usage - panels, cards, hand/deck presentation etc
> - migrate to panel renderer
> - add all elements including windows to gallery

All clarification questions raised against the first draft of this spec have been answered by the
owner; the decisions are folded into the sections below and no open questions remain.

## Feature Intent

As a developer working on this game's UI, I want every element and every window previewable and
restylable in a dedicated Gallery scene, assembled from small reusable components named after what
they *are* rather than after the screen that first needed them, so that a visual change costs one
save instead of a restart plus a navigation sequence.

As the same developer, I want the UI to sit on Unity's supported rendering component rather than
the one the editor now tells us is frozen, and I want the debug tooling out of the shipping HUD.

As a player, I want the game to spend its frame budget on the simulation and on what is actually on
screen, not on recomputing leaderboards behind a closed window.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies
to several rows.

- **A developer opens `Assets/Scenes/Gallery.unity` and presses Play, with no game running, no save
  loaded and no country selected**
  - scrolls the page => sees one collapsible block per UI element, each titled with the element's
    name, covering every reusable component, every HUD panel, every overlay, every window/modal, and
    every debug-only surface the game can show
  - expands any block => the element renders with real localized text, real flags and real art,
    styled by the same stylesheets the running game uses, so what the Gallery shows is what the
    game shows
  - picks an entry from a block's first dropdown => the element re-renders for that specific
    instance (that action id, that country, that org, that war, that character)
  - picks an entry from a block's second dropdown => the element re-renders in that named state
    (unaffordable, on cooldown, empty hand, empty slot, at war, destroyed, victory, defeat, …)
  - expands several blocks at once => each keeps its own instance and state selection independently
    of the others
  - has no game, no ECS world, no bots and no save present at any point => every block still
    renders, because each block builds its own display data by hand

- **A developer is editing a UXML or USS file while the Gallery scene is playing**
  - saves the file => the changed element re-renders with the new styling within a second, and every
    expanded block, dropdown selection and scroll position is exactly where it was
  - edits a C# file and lets the editor recompile => the same is true across the domain reload
  - changes a value in the shared palette block => every element in every block picks the change up,
    without any per-file edit
  - does the same while the *game* is running => live-edit rebinding behaves the same way on every
    UI surface, not only in the Gallery

- **A developer is looking for the code behind a piece of UI**
  - opens the class named after a component (a flag badge, a rank row, a character card, a tooltip
    body) => finds one implementation of that thing, used everywhere it appears
  - opens the class named after a panel or a window => finds that surface's wiring and nothing else;
    no file mixes a shipping HUD panel with a debug tool, or one window with another
  - looks for the in-HUD debug tooling (province/relation/control-org/character tools, gold buttons,
    force-destroy buttons, card draw/discard buttons, FPS counter, ECS viewer button) => finds it as
    its own separate UI surface, with its own folder, its own assembly and its own markup, not
    inside the shipping HUD
  - opens any UI class => finding a given handler does not require a search; the 1764-line document
    is gone

- **A developer needs an element that already exists somewhere else — a flag badge, a leaderboard
  row, a character card, a resource chip, a tooltip body**
  - looks for it => finds exactly one component for it, with one piece of markup and one place its
    styling is defined
  - drops it into a new screen => it looks and behaves the same as everywhere else it appears, with
    no copied construction code
  - opens its markup in UI Builder => sees the element laid out and can edit it visually, rather
    than reading C# that constructs it element by element

- **A player has the leaderboard window open in a large late-game world (154 countries)**
  - watches the list while the game ticks => the list stays smooth and scroll position is not lost,
    because only the visible rows exist and only changed rows are touched
  - scrolls the list => rows appear without a visible rebuild hitch
  - the same holds for the goals list, the end-game comparison list, the war battles list and the
    save list

- **A player is playing with every window closed**
  - lets the game run => the per-frame cost and allocation of the projection half of the loop is
    measurably lower than the recorded baseline of 4 ms / 200 KB, measured the same way (Editor
    Profiler, `GameLoop.UpdateVisualState` marker, a comparable world), with before/after numbers
    written into this spec folder
  - opens the leaderboard, goals, war-progress or end-game window => it shows correct, current data
    immediately on open, and stays current while it remains open
  - closes the window again => the game stops paying for that window's data
  - plays through a war, a country's destruction, an org's destruction, a card play and a province
    changing hands => every log line, fly-text, result window and animation still appears exactly as
    before; nothing that happens between two frames is missed

- **A player or developer interacts with any UI control anywhere in the game**
  - clicks any button, on any surface => it responds on the first click, with no dead controls
  - clicks a disabled control => nothing happens, consistently, on every surface
  - clicks a panel that sits over the map => the map does not also receive that click
  - looks at a country flag in the HUD => it renders at full quality rather than being excluded from
    the UI atlas

- **The game has been migrated off the UI rendering component the editor flags as frozen**
  - plays the game start to finish => every UI surface renders, takes pointer input, and layers in
    the same order as before, including fly-text staying above everything
  - opens the scenes in the editor => no UI object still carries the deprecation warning
  - edits a stylesheet while playing => every surface rebinds correctly on its own, without any
    per-frame checking code anyone has to remember to write

- **The team later decides to ship a build without the debug tooling**
  - excludes it => the debug UI drops out as one self-contained unit, without touching a single
    shipping HUD file, because it is its own surface with its own assembly and its own markup
  - ships a build with it still present => the debug tooling works exactly as it does today; this
    feature separates it, it does not change whether it ships

- **The project's own documentation is consulted about pointer-over-UI detection or about click
  handling**
  - reads the UI Toolkit rules and the localization rules => both say the same thing, the code
    matches what they say, and the click rule names a single call every site uses

## Tech Notes

### Component decomposition — the definitive inventory (criteria groups 3–4; user bullet 1)

Decomposition is by **meaning**, not by size. There is no line-count target. The unit is *one small
reusable component per UI block*: one C# builder class, one `.uxml`, instantiated via
`VisualTreeAsset.Instantiate()`, living under `Assets/UI/Components/<Name>/`, and each gets its own
Gallery block. This is also how analysis option **B** (UXML template extraction) is delivered — the
223 C#-built elements collapse into these components rather than into ad-hoc per-screen templates.

**Atoms**

| Component | Replaces | Used by |
|---|---|---|
| `FlagBadge` | `entity-flag` (`SharedStyles.uss`), `relations-flag` (`CountryInfo.uss`), `goals-row-flag` (`GoalsWindow.uss`), `leaderboard-row-flag` (`LeaderboardWindow.uss`), `war-result-province-flag` (`WarResultWindow.uss`), `war-icon-flag` (`WarIcons.uss`) | CountryInfo, Goals, Leaderboard, EndGame, WarResult, WarIcons, CountryActions |
| `ResourceChip` | `resource-icon` + `resource-label` + `resource-row` (`SharedStyles.uss`) | Resources, WarResult |
| `StatChip` | `char-stat-chip` + `char-stat-icon` (`SharedStyles.uss`) | Characters, OrgCharacters |
| `ProgressBar` | `goals-progress-track` / `goals-progress-fill` (`GoalsWindow.uss`) | Goals, WarProgress |

**Rows** — these are the `ListView` `makeItem` targets for the virtualization workstream below.

| Component | Replaces | Used by |
|---|---|---|
| `RankRow` | `leaderboard-row` place/flag/name/score (`LeaderboardWindow.uss`, `EndGameWindow.uss`), identical in shape to `goals-row` (`GoalsWindow.uss`) | Leaderboard, EndGame, Goals |
| `EffectRow` | `war-progress-effect-row` (`WarProgressLayout.uss`), `tooltip-effect-row` (`SharedStyles.uss`), positive/negative variants | WarProgress, WarResult, tooltips |
| `BattleRow` | `war-progress-battle-row` (`WarProgressLayout.uss`) | WarProgress, WarResult |
| `ProvinceTransferRow` | `war-result-province-row` (`WarResultWindow.uss`) — flag → arrow → flag → name | WarResult |
| `RequirementRow` | `ActionConditionText` output + `debug-condition-label` (`HUD.uss`) | CountryActions, DebugCardAvailability, Gallery |

**Cards**

| Component | Replaces | Used by |
|---|---|---|
| `ActionCard` | `ActionCardBuilder` plus the currently duplicated `ComposeFaceData` | CountryActions, OrgActions, CardDraw, Gallery |
| `CharacterCard` | `char-card` and `org-char-card` (`SharedStyles.uss`) unified, with an `--empty` variant | Characters, OrgCharacters |
| `TaskCard` | `task-item` / `task-header` / `task-body` / `task-reward-row` (`PlayerTasks.uss`) | PlayerTasks |

**Composites**

| Component | Replaces | Used by |
|---|---|---|
| `TooltipBody` | `tooltip-header` / `tooltip-description` / `tooltip-effect-*` (`SharedStyles.uss`) | six views build this by hand today: `CharactersView`, `CountryInfoView`, `OrgCharactersView`, `ResourcesView`, `WarIconsView`, `WarProgressLayoutBinder` |
| `HandContainer` | `hand-cards-grid`, `action-deck-wrapper`, `card-lift-wrapper`, deck controls (`OrgActions.uss`) | CountryActions, OrgActions |
| `DrawSlot` | `card-draw-slot`, `card-draw-card-face` / `-back` / `-copy` (`HUD.uss`) | CardDraw |
| `FlagNameHeader` | `flag-name-row` (`SharedStyles.uss`) | CountryInfo, CountryActions |

**Explicitly not components**

- `ActionLogView` — it uses the documented identity-keyed incremental diff with per-element fade
  transitions (`.claude/rules/unity/uitoolkit.md` §"Incremental diff Refresh — accumulating/animating
  lists"). Extracting its rows into a shared component would break that diff. Leave it alone.
- Panel and window **shells**. Each stays one document + one view; only their *contents* become
  components.

**Two accepted consequences**

1. `RankRow` unifies Goals rows and Leaderboard rows. They are the same shape but styled separately
   today (`GoalsWindow.uss` vs `LeaderboardWindow.uss`). A small visual change to one or both is
   possible and is accepted.
2. **All components share one stylesheet: `Assets/UI/Components/Components.uss`** — not one `.uss`
   per component. Reason: per `.claude/rules/unity/uitoolkit.md` §"USS scope for dynamically created
   elements", a class used by a C#-created element resolves against the stylesheets loaded by the
   *hosting document*, and a template's own USS must be explicitly imported into the parent UXML.
   Sixteen separate sheets would mean sixteen `<ui:Style>` import lines in every hosting document
   (`HUD.uxml` already carries five such imports for exactly this reason). One shared sheet is one
   import line.

### Panel and window decomposition (criteria group 3)

- **Primary target:** `Assets/Scripts/Unity/UI/HUDDocument.cs`, 1764 lines, ~115 fields, a
  24-parameter `[Inject] void Construct(...)` at line 117. `Assets/Scripts/Unity/UI/` holds 55 files
  / 11418 lines; next largest are `CountryActionsView.cs` 548, `CardDrawAnimator.cs` 512,
  `WarProgressLayoutBinder.cs` 507, `ActionCardBuilder.cs` 461, `CardDrawView.cs` 457,
  `CardPlayAnimator.cs` 423, `CountryInfoView.cs` 385, `DebugCardAvailabilityView.cs` 380,
  `ResourcesView.cs` 321.
- One binder per HUD panel, each owning only its own subscribe/refresh pair, replacing the ~30
  subscribe/unsubscribe pairs centralised in `HUDDocument.OnEnable` (:572) / `OnDisable` (:627).
  `HUDDocument` shrinks to a composition root that constructs panel binders and routes `Refresh`.
- **Five windows have no view class at all** — every line of their logic lives in the MonoBehaviour:
  `MainMenuDocument`, `GameMenuDocument`, `SettingsWindowDocument`, `LoadWindowDocument`,
  `SelectOrgDocument`. `OrgInfoDocument` is the same shape. Extracting a plain `Refresh(state)` view
  from each is a **prerequisite** for their Gallery blocks. Seven windows already have one and need
  no extraction: `LeaderboardWindowView`, `GoalsWindowView`, `WarProgressWindowView`,
  `WarResultWindowView`, `EndGameWindowView`, `CountryDestroyedWindowView`, `OrgDestroyedWindowView`.
- **Asmdef granularity:** plain subfolders inside the existing `GS.Unity.UI` assembly
  (`Assets/Scripts/Unity/UI/GS.Unity.UI.asmdef`) — *no* new asmdefs for panels, cards, rows or
  windows. The one exception is the debug UI, below. This satisfies the Constitution's "one
  `.asmdef` per feature folder under `Assets/Scripts/`" without fragmenting compilation.
- Behaviour parity is the bar: this workstream moves code, it does not change what the UI does
  (`RankRow`'s styling unification excepted). There are **zero Unity test assemblies**, so parity is
  verified by Gallery blocks plus manual play — which is why every surface gets its Gallery block
  *before* it is split (see Sequencing).

### Debug UI extraction (criteria groups 3 and 9)

The in-HUD debug system leaves the shipping HUD **entirely**: its own feature folder under
`Assets/Scripts/Unity/`, its own `.asmdef`, its own UI surface object and its own markup. This also
removes `GS.Unity.EcsViewer` from the shipping HUD's reference set.

**This feature does not change whether the debug UI ships.** It is present in builds today (its
markup lives in `HUD.uxml:43-112`) and stays present afterwards. What changes is that excluding it
later becomes a build-configuration decision on one self-contained assembly, rather than a code
change threaded through the HUD — so that call can be made separately, on its own merits.

~204 lines of `HUDDocument.cs` mention debug. The members that move:
`BuildProvinceDebugUi` (:400), `BuildRelationDebugUi` (:443), `BuildControlOrgDebugUi` (:471),
`ToggleDebugPanel` (:507), `ToggleFpsDisplay` (:512), `SetFpsEnabled` (:516),
`UpdateFpsCounter` (:530), `RegisterDebugMenuToggle` (:543), `OpenEcsViewer` (:563),
`PushDebugDrawCountryCardCommand` (:903), `PushDebugDiscardCountryCardCommand` (:918),
`RefreshSelectedCountryDebugName` (:1006), `RefreshSelectedOrgDebugName` (:1013),
`RebuildControlOrgDropdown` (:1034), `RefreshControlOrgDebugList` (:1051),
`PushControlOrgCommand` (:1069), `PushChangeGoldCommand` (:1084),
`PushSelectedOrgChangeGoldCommand` (:1097), `PushForceOrgDestroyCommand` (:1105),
`RefreshSelectedCountryCharacterDebugButtons` (:1119),
`RefreshSelectedOrgDebugMenuAvailability` (:1135), `RefreshSelectedProvinceDebugMenu` (:1421),
`RebuildProvinceCountryDropdown` (:1446), `RefreshProvinceActionButtons` (:1464),
`RebuildRelationCountryDropdown` (:1482), `RefreshRelationActionButtons` (:1511),
`PushChangeProvinceOwnerCommand` (:1541), `PushSetProvinceOccupationCommand` (:1549),
`PushClearProvinceOccupationCommand` (:1557), `PushSetCountryRelationCommand` (:1564),
`PushClearCountryRelationCommand` (:1572), `PushForceCompletionCondition` (:1607),
`RebuildOrgCharDebugButtons` (:1641), `PushCycleCharacter` (:1721), `PushDropCharacter` (:1726),
`PushImproveOpinionCommand` (:1731) — plus `DebugCardAvailabilityView.cs` and the ~30 `_btnDebug*` /
`_*DebugMenu` / `_*RawGoldLabel` fields.

Markup: `Assets/UI/HUD/HUD.uxml:43-112` (the `debug-panel-scroll` subtree) moves out with them, into
`Assets/UI/Debug/`. `HUD.uss`'s debug-only classes (`debug-condition-label`, `debug-panel-scroll`, …)
move with it.

The debug UI is in Gallery scope: the debug panel, its province / relation / control-org / character
sub-menus, `DebugCardAvailabilityView` and the FPS counter each get a block.

### Gallery completeness (criteria groups 1–2; analysis option A, user bullet 3)

- Scene and assets already exist: `Assets/Scenes/Gallery.unity`,
  `Assets/Scripts/Unity/Gallery/{GalleryDocument.cs, GS.Unity.Gallery.asmdef}`,
  `Assets/UI/Gallery/{Gallery.uxml, Gallery.uss}`. The prototype covers exactly one element (the
  action card, 7 states × 16 action ids) and is deliberately DI-free — no `GalleryLifetimeScope`,
  no `VisualState`, no `GameLogic`.
- `Assets/Scenes/Gallery.unity` is **not** in `ProjectSettings/EditorBuildSettings.asset`
  (`m_Scenes` lists MainMenu, CountrySelection, Map only). Keep it out.
- **Do not grow `GalleryDocument.cs` into a second `HUDDocument`.** Split it into one small block
  class per element plus a host: `GalleryDocument` owns an ordered list of blocks and builds one
  `ui:Foldout` per block; each block owns its own state switch and its own two `DropdownField`s. The
  per-block serialized selection currently held as `_selectedActionId` / `_selectedStateIndex` /
  `_cardBlockExpanded` (`GalleryDocument.cs:57-59`, `[SerializeField, HideInInspector]`) becomes a
  serialized per-block-id record on the host so it survives domain reload.
- **Live-edit rebinding is host infrastructure, never a per-block responsibility.** Today it is
  `GalleryDocument.Update()` polling `_cardDropdown.panel != null` (`GalleryDocument.cs:87-96`) and
  re-running `Bind()`. The PanelRenderer migration **deletes this polling** — see below.
- `FitDropdownToWidestChoice` (`GalleryDocument.cs:314`) is host infrastructure too; move it out of
  the card block.
- **Blocks to build** — the 16 components in the inventory above, then the panel and window shells,
  then the debug surfaces:
  - *HUD panels:* `CountryInfoView`, `ProvinceInfoView`, `ResourcesView`, `PlayerOrgView`,
    `PlayerTasksView`, `TimeView`, `LensSwitcherView`, `OrgLensCountryView`, `ActionLogView`,
    `WarIconsView`, `TutorialHighlightView`, `TooltipController` content, `FlyTextNotifierDocument`
    text.
  - *Hand/deck and animation:* `CountryActionsView` hand row, `OrgActionsView`, `CardDrawView`,
    `CardTransitionView`, and static frames of `CardPlayAnimator` / `CardDrawAnimator`.
  - *Characters:* `CharactersView`, `OrgCharactersView` slots. *Overlay:* `OrgInfoDocument`.
  - *Windows:* MainMenu, SelectCountry (`SelectOrgDocument`), LoadWindow, SettingsWindow, GameMenu,
    LeaderboardWindow, GoalsWindow, WarProgressWindow (+ `WarProgressLayout`), WarResultWindow,
    CountryDestroyedWindow, OrgDestroyedWindow, EndGameWindow.
  - *Debug:* debug panel, province / relation / control-org / character sub-menus,
    `DebugCardAvailabilityView`, FPS counter.
- `ComposeFaceData` is currently duplicated between `CountryActionsView` and
  `GalleryDocument.ComposeFace` (~25 lines, flagged in `analysis.md` §A). The `ActionCard` component
  absorbs both — do not add a third copy for any new block.
- `ActionConditionText` in `CountryActionsView.cs` was already widened `internal` → `public` for the
  prototype; expect a small number of similar widenings, and prefer `public` over
  `InternalsVisibleTo` per `.claude/rules/csharp/code_style.md`.
- Gallery-only helper types stay in `GS.Unity.Gallery`, which already references `GS.Unity.UI`,
  `GS.Unity.Common`, `GS.Unity.Map`, `GS.Unity.Save`; it gains a reference to the new debug assembly.

### PanelRenderer migration (criteria group 8; user bullet 2)

**The decision is a full migration, not a pilot.** The Unity editor now warns on `UIDocument`:

> Consider migrating to Panel Renderer, the updated UI rendering component which provides more
> robust functionality. The UI Document component will continue to be available but no longer
> receive new features.

`UIDocument` is soft-deprecated. All 18 binding MonoBehaviours migrate.

**Verified against this Unity version (reflection plus a live editor probe) — treat as fact:**

- `UnityEngine.UIElements.PanelRenderer` exists: assembly `UnityEngine.UIElementsModule`,
  `sealed class PanelRenderer : Renderer, IPanelComponent`. `UIDocument` also implements
  `IPanelComponent`.
- **`PanelRenderer` is not "the world-space path".** `PanelRenderMode { ScreenSpaceOverlay,
  WorldSpace }` is a property of **`PanelSettings`**, not of the component.
  `Assets/UI/HUD/HUDPanelSettings.asset` is `ScreenSpaceOverlay` (`m_RenderMode: 0`), and *both*
  `UIDocument` and `PanelRenderer` derive `isWorldSpace` from it. Migration does **not** mean moving
  the UI into world space.
- `UIDocument` already carries the same modern surface: `parentUI`, `position`, `pivot`,
  `pivotReferenceSize`, `worldSpaceSize`, `worldSpaceSizeMode`, `isWorldSpace`, `runtimePanel`. The
  two components are far closer than their names suggest.
- **Live probe** (throwaway `HideFlags.HideAndDontSave` GameObject, destroyed in the same call):
  `AddComponent<PanelRenderer>()` plus assigning the screen-space `HUDPanelSettings` and
  `Gallery.uxml` raised no exception; read back `worldSpaceSizeMode = Fixed`,
  `worldSpaceSize = (1920, 1080)`, `sortingOrder = 0`.
- **The one real API difference, and the whole migration:** `PanelRenderer.rootVisualElement` exists
  but is **`internal`** (typed `PanelRendererRootElement : TemplateContainer`), as is
  `referenceProvider` (`VisualElementReferenceProvider`). Public code cannot reach the tree the way
  this project does today. The only public route is `RegisterUIReloadCallback` —
  `UIReloadCallback.Invoke(PanelRenderer panelRenderer, VisualElement rootElement)`, with a
  `VersionedUIReloadCallback` overload; `UnregisterUIReloadCallback` undoes it.
  `UIDocument.rootVisualElement` stays public.
- **18 MonoBehaviours pull the root in `Awake`/`OnEnable` and must invert to receiving it in a
  callback:** `HUDDocument`, `OrgInfoDocument`, `MainMenuDocument`, `GameMenuDocument`,
  `SettingsWindowDocument`, `LoadWindowDocument`, `SelectOrgDocument`, `LeaderboardWindowDocument`,
  `GoalsWindowDocument`, `WarProgressWindowDocument`, `WarResultWindowDocument`,
  `EndGameWindowDocument`, `CountryDestroyedWindowDocument`, `OrgDestroyedWindowDocument`,
  `FlyTextNotifierDocument`, `CardPlayAnimator`, `CardDrawAnimator`, `GalleryDocument`.
- **`sortingOrder` changes type.** `UIDocument.sortingOrder` is `float`; `PanelRenderer` uses the
  inherited `Renderer.sortingOrder` (`int`) to satisfy `IPanelComponent.sortingOrder`. Today only
  `FlyTextNotifierDocument._topMostSortingOrder` (1000, applied in `Awake`) differs from 0, so
  nothing relies on fractional ordering — but this is a standing constraint: **no fractional
  sorting orders after the migration.**
- **`PanelRenderer` has first-class live-reload plumbing** — `IPanelComponent.HandleLiveReload`,
  `OnLiveReloadOptionChanged`, `m_LiveReloadVisualTreeAssetTracker`. The reload callback **deletes
  the Gallery's per-frame `_cardDropdown.panel != null` polling** in `GalleryDocument.Update()` and
  makes live-edit rebinding correct on *every* surface, instead of something each block author has
  to remember. `analysis.md` §A calls that rebinding "what makes the loop fast, and it is not free" —
  the migration makes it free.
- **`parentUI` + `firstChildInsertIndex` + `PanelComponentList m_ChildrenContent`** give real panel
  composition across GameObjects, a primitive the project has no equivalent for today. Not required
  by this feature, but it is what the modal/overlay layering could eventually use instead of
  hand-tuned sorting orders.

**Not verifiable outside play mode — the Gallery is the go/no-go check:**

Whether a screen-space `PanelRenderer` actually renders, takes pointer input, and layers by
`sortingOrder` cannot be established by reflection. It is a `Renderer`, so SRP culling applies to it
in a way it does not to `UIDocument`.

**Migration order:**

1. Migrate `Assets/Scenes/Gallery.unity` first, as step one of the migration phase. Play it and
   verify: it renders; the dropdowns and foldouts take pointer input; a second overlapping surface
   layers correctly by `sortingOrder`; editing `Gallery.uss` rebinds through the reload callback with
   the polling code deleted.
2. **If that check passes**, carry all 18 surfaces **in the same pass as the decomposition work** —
   those classes are being rewritten anyway, and migrating them twice is the wasteful option.
3. **If it fails**, stop at the Gallery, revert it, and record in this folder exactly what failed.
   Everything else in this spec is independent of the migration.

### List virtualization (criteria group 5; analysis option C)

- Replace `ScrollView` + `Clear()`/rebuild with `ListView` at: `LeaderboardWindowView.cs:33`
  (`leaderboard-list`), `EndGameWindowView.cs:24,26` (`end-game-leaderboard-list`,
  `end-game-comparison-list`), `WarProgressLayoutBinder.cs:70-72` (`attacker-effects-list`,
  `defender-effects-list`, `battles-list`, shared by `WarProgressWindow.uxml` and
  `WarResultWindow.uxml`), `LoadWindowDocument.cs:34` (`save-list`).
- Worst case today, from `analysis.md` §C: 154 country rows × ~4 elements rebuilt per tick while the
  leaderboard is open.
- `makeItem` returns an instance of the matching row component (`RankRow`, `EffectRow`, `BattleRow`),
  which is why the component inventory is a prerequisite; `bindItem` applies the row's data.
- With the pull model below, the rebuild trigger becomes the coarse refresh rather than every tick —
  the two workstreams compound.
- `ActionLogView` is not a `ListView` candidate; see the component inventory's exclusions.

### USS design tokens (criteria group 2, palette row; analysis option D)

- Declare `--gs-*` custom properties on `:root` in `Assets/UI/Shared/SharedStyles.uss`, mirroring the
  token names in `Design/01_prototype/design-final.html` so prototype and Unity share one vocabulary.
- 184 colour/font literals across 23 stylesheets today; the project uses zero USS variables.
- Existing shared classes (`gs-panel`, `gs-btn*`, `gs-title/header/label/content/hint`, `gs-color-*`,
  `gs-bg-*`, `gs-border-*`) keep their names and start reading tokens — the class catalogue in
  `.claude/rules/unity/uitoolkit.md` stays valid, and per-feature USS keeps its layout-only rule.
- `Assets/UI/Components/Components.uss` consumes the same tokens; it defines component classes only,
  never new colour or font literals.
- Gallery-local literals count too: `Gallery.uss` currently repeats `.gs-header`'s font/colour by
  hand for the foldout toggle text (a foldout title is a `Toggle` text element, not a `Label`), with
  a comment saying to keep the two in step manually. Tokens remove that duplication.

### Standalone fixes (criteria group 7 and the documentation group; analysis option F — all six)

1. `HUDDocument.cs:737` — `if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())`
   inside `Update()`'s org-panel outside-click handling. Replace with `UIPointerState.IsPointerOverUI`,
   which every other site already uses. `using UnityEngine.EventSystems;` (`HUDDocument.cs:5`) goes
   with it.
2. `Assets/UI/HUD/HUDPanelSettings.asset:41` — `m_MaxSubTextureSize: 64` under
   `m_DynamicAtlasSettings` excludes all 128×128 flags from the dynamic atlas. Raise it (128 or
   above) and confirm atlas behaviour.
3. **All 22 `.clicked` sites migrate, and a helper is added.** No such helper exists in the project
   today; the 58 sites already on `PointerUpEvent` hand-roll the pattern, and several debug sites
   additionally hand-roll an `enabledSelf` check. Add one extension in `GS.Unity.UI`:

   ```csharp
   public static class VisualElementClickExtensions {
   	public static void OnClick(this VisualElement element, Action handler) {
   		element.RegisterCallback<PointerUpEvent>(evt => {
   			if (evt.button != 0 || !element.enabledSelf) {
   				return;
   			}
   			if (!element.ContainsPoint(evt.localPosition)) {
   				return;
   			}
   			handler();
   		});
   	}
   }
   ```

   It collapses all 80 sites — the 22 `.clicked` plus the 58 hand-rolled — and makes the
   `.claude/rules/unity/uitoolkit.md` rule enforceable as "call `.OnClick()`" instead of "remember
   this pattern". The 22 `.clicked` sites: `HUDDocument.cs` (6 — game menu, leaderboard, goals,
   `ToggleDebugPanel`, `OpenEcsViewer`, `ToggleFpsDisplay`), `SettingsWindowDocument.cs` (11 — two
   locales, three auto-save intervals, two tutorial toggles, delete-all-saves, reset-tutorials,
   reset-defaults, hide), `TimeView.cs` (4 — pause toggle plus three speeds),
   `LoadWindowDocument.cs` (1 — hide).
4. `Assets/UI/HUD/WarIcons/WarIcons.uxml` imports only `WarIcons.uss` yet uses `gs-btn`; it is the
   only UXML not importing `SharedStyles.uss`. Add
   `<ui:Style src="project://database/Assets/UI/Shared/SharedStyles.uss"/>` before the local style.
5. `Assets/UI/HUD/HUD.uxml:18` — `data-source-type="GS.Main.VisualState, Game.Main"` on `hud-root`
   is dead metadata from the abandoned 2026-04-07 binding attempt. Remove it. (It would force
   reflection property bags for a `src/` type under IL2CPP/WebGL if ever revived — see
   `analysis.md` constraining fact #1.)
6. `.claude/rules/unity/uitoolkit.md:160-173` tells readers to use
   `EventSystem.current.IsPointerOverGameObject()` for blocking map/world clicks;
   `.claude/rules/unity/localization.md` §"Click Blocking for Modal Dialogs" says that does not
   reliably detect UI Toolkit panels with the new Input System and mandates `ModalState` /
   `UIPointerState`. Reconcile to one answer, matching fix 1. Update the same file's click rule to
   name `.OnClick()` from fix 3.

### Pull model for cold panels (criteria group 6; analysis option G — agreed)

- **Hard constraint: the projection functions stay in `src/`.** `src/Game.Tests` has 144 test files,
  15 of them projector-specific (`VisualStateConverterLeaderboardTests`, `GoalsProjectorTests`,
  `SelectedWarProjectorTests`, …), against zero Unity test assemblies. Moving projection logic
  Unity-side would take the trickiest rules out of `dotnet test`. Each cold projection becomes a
  public `Project(IReadOnlyWorld, …) -> DTO` in `src/Game.Main`, in the shape of the existing
  `GoalsProjector` / `SelectedWarProjector` / `EndGameComparisonProjector` / `WarIconsProjector` /
  `WinConditionHintProjector` / `CharacterCardHintProjector` files.
- **Move off the per-tick path:** `VisualStateConverter.UpdateLeaderboards`
  (`VisualStateConverter.cs:1021` — allocates two lists over every org plus all 154 countries and
  sorts both, every tick, with the window closed), `UpdateGoals` (:1051), `UpdateSelectedWar` (:326),
  `UpdateDebugOrgCardAvailability` (:721), and the EndGameComparison projection. Call sites: the
  owning document's open path plus a refresh while it stays open — `LeaderboardWindowDocument`,
  `GoalsWindowDocument`, `WarProgressWindowDocument`, `WarResultWindowDocument`,
  `EndGameWindowDocument`, and the extracted debug card-availability UI.
- **Refresh cadence (decided, not open):** re-project immediately when the window opens; immediately
  after any command the window itself pushes; and otherwise on a wall-clock accumulator of 250 ms
  while it stays open, ticked from the owning document's existing `Update`. Skip the scheduled
  re-projection entirely while the game is paused, since nothing it reads can have changed.
- Access is already available Unity-side: `GameLogic.World`, `IReadOnlyWorld`, `Resources`,
  `Relations` are public today.
- Each moved panel also deletes its `StateEquality` diffing and its subscribe/unsubscribe pair — the
  diff machinery exists only to suppress notifications a pull model never raises.
- The existing laziness hints (`CountryActionsVisibility.ActionsPanelOpen` wired from
  `CountryActionsView.cs:223`, `DebugOrgCardVisibility`'s four flags wired from `HUDDocument.cs:258`)
  are the flag-per-panel predecessor of this; where a panel converts to pull, its flag gate goes away
  with it.
- **Must NOT be pulled — edge-triggered observations.** `UpdateGameLog`
  (`VisualStateConverter.cs:1134`) reads `ControlEffectApplied` / `OpinionEffectApplied` /
  `RoleChangeApplied` marker entities that `CleanupEffectNotificationsSystem` destroys on the next
  tick; `UpdateLastFrameEffects` (:112) reads `ResourceChange` the same way; `WarResults`,
  `CountryDestroyedResults`, `OrgDestroyedResults` are `Enqueue`/`AcknowledgeCurrent` queues;
  `ProvinceOwnership.Recent*` (:957) names the province that just changed hands. These need a
  per-tick observer, and that observer is `VisualState`.
- **Must NOT be pulled — state with no world backing.** `MapLens` is written into `VisualState`
  straight from `ChangeLensCommand` (`GameLogic.cs:191`); likewise `SelectedWar.PendingWarId`,
  `SaveResult`, and the `AnimatableInt` / `AnimatableDouble` + `AnimationBarrierInt` /
  `AnimationBarrierDouble` system, which is a `deltaTime`-ticked interpolation held by the card-play
  animator.
- **`VisualState` shrinks; it does not disappear.**

### Measurement, stated honestly (criteria group 6, first row)

- Recorded baseline, one representative Editor-Profiler frame with no windows open:
  `GameLoop.UpdateLogic` 8 ms / 40 KB vs `GameLoop.UpdateVisualState` **4 ms / 200 KB** — the
  projection is ~1/3 of the loop's time and 5× its allocation while showing nothing new. Both
  markers already exist, from the `GameLogic.Update` split into `UpdateLogic` + `UpdateVisualState`
  wrapped by `GameLoopRunner`.
- The acceptance bar is a **measured, recorded reduction against that same measurement method** —
  Editor Profiler, same markers, a comparable world — written into this spec folder. **No
  millisecond target is promised.**
- **`Docs/Benchmarks/baseline.json` is not a usable proxy.** Its
  `VisualStateConverterBenchmarks.Update` reads 45 µs / 18 KB — roughly two orders of magnitude below
  what Unity measures. Do not size or verify this work from it, and do not gate acceptance on a
  benchmark number.

### Sequencing

This is a large multi-workstream refactor and should not be attempted in one sitting. Phase order,
chosen so each phase is independently shippable and reviewable:

1. **F + D** — standalone fixes (including the `.OnClick()` helper and the 22 migrations) and USS
   tokens. Mechanical, independent, low risk; tokens make every later visual change cheaper.
2. **G** — cold-panel pull model. Independent of everything else, largest measured payoff, and the
   projection functions stay under `dotnet test`. Record the profiler markers before and after.
3. **Component inventory** — build the 16 components (C# builder + `.uxml` + entries in
   `Components.uss`), each with its Gallery block, and repoint their existing callers. This is
   analysis option B, delivered as the component tier.
4. **C** — `ListView` on the five list surfaces, using the row components from phase 3.
5. **Debug extraction** — the largest, cleanest seam: new feature folder, new asmdef, new UI surface,
   `HUD.uxml:43-112` moved to `Assets/UI/Debug/`, Gallery blocks for the debug surfaces.
6. **PanelRenderer, step one** — migrate the Gallery scene and run the go/no-go check (renders, takes
   pointer input, layers by `sortingOrder`, reload callback replaces the polling). Stop and record if
   it fails.
7. **Panel and window decomposition, with the PanelRenderer migration riding along** — for each
   surface, in order: land its Gallery block first (the only regression check available with zero
   Unity test assemblies), then split it into document + view, then convert that same class from
   `UIDocument.rootVisualElement` to `RegisterUIReloadCallback`. Doing the split and the migration in
   one pass avoids rewriting all 18 classes twice. The five view-less documents
   (`MainMenuDocument`, `GameMenuDocument`, `SettingsWindowDocument`, `LoadWindowDocument`,
   `SelectOrgDocument`, plus `OrgInfoDocument`) need their view extracted before their Gallery block
   is possible, so they come after the seven that already have one.

## Out of Scope

- **Option E — presentation-model data binding.** `[CreateProperty]` models,
  `INotifyBindablePropertyChanged`, a Unity-side projector layer, or binding UXML to `VisualState`.
  Verdict in `analysis.md` is "skip for now": the Gallery already delivers the preview payoff E was
  partly wanted for, so E now stands or falls on boilerplate reduction alone, and its three caveats
  stand (reflection property bags for `src/` types under IL2CPP/WebGL; null `PropertyChanged` names
  forcing a diffing projector; runtime binding re-reading its source every frame, giving panel-level
  gating rather than the per-access laziness G delivers).
- **Deleting `VisualState` / the full pull model.** Not on the table until the edge-triggered queues
  and the animation-barrier system have an answer. Revisit after G lands and the profiler is re-read.
- **Any visual redesign.** Colours, spacing, typography and layout stay as they are; tokens capture
  the existing values, they do not change them. The single accepted exception is `RankRow` unifying
  the Goals and Leaderboard row styling.
- **New gameplay, new windows, new locale keys.** No user-facing feature changes.
- **Benchmarks.** No BenchmarkDotNet work in this feature, and no investigation into why
  `Docs/Benchmarks/baseline.json` reads ~100× cheaper than the Unity Profiler for the same
  projection. Making that harness representative stays out of scope.
- **Player-build performance measurement.** The Editor-Profiler comparison is the acceptance
  measurement, so before and after use the same method.
- **Introducing Unity test assemblies.** Worth doing, but a separate decision; this feature must not
  quietly relocate `src/`-tested logic on the assumption that one exists.
- **Adding the Gallery scene to `EditorBuildSettings`.** It stays an editor-only tool.
- **`parentUI` panel composition.** Available after the PanelRenderer migration and noted as the
  eventual replacement for hand-tuned sorting orders, but not adopted here.
