# Spec: Small UI Improvements

## Feature Intent

As a player, I want a batch of small HUD/overlay polish fixes — pause menu drawing above the HUD, card-play finishing without wrongly resuming a paused game, an action log that does not steal taps or overlap the selected country/org bar and that has a readable gray backdrop, and flying cards that match static card size — so that everyday UI interactions feel consistent and do not fight each other.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- The in-game pause menu is open (Esc or the HUD menu button) while the HUD is visible.
  - The player looks at the screen => the pause menu (dimmed backdrop and menu panel) draws fully on top of the HUD; no HUD chrome (time controls, action log, country/org bars, buttons) appears over the pause menu.
  - The player can click Resume / Save / Exit => those pause-menu controls remain clickable and are not blocked by HUD elements underneath or on top.
- The simulation is already paused (player paused via time controls or an equivalent pause) and the player plays a card from the hand.
  - The card-play animation finishes => the simulation stays paused; finishing the play must not resume time on its own.
- The simulation is running (not paused) and the player plays a card from the hand.
  - The card-play animation finishes => the simulation resumes as it does today after a play that this feature itself paused for the animation (no change to the “play while running → temporary pause → resume” happy path).
- The action log panel is visible on the HUD.
  - The player taps / clicks the map or other HUD controls through the action-log area => those taps pass through; the action log never intercepts pointer input.
  - The player has a country or org selection panel open at the bottom => the action log’s visible area sits entirely above that panel with a clear gap; the log does not overlap or sit under the selected country/org bar.
  - The player looks at the action log => the log area has a semi-transparent gray background behind its entries (readable over the map, not a fully opaque slab).
- The player plays a card and watches the flying/transition card animation.
  - The flying card is on screen => its width and height match the static hand / overlay card size (same footprint as the card it flies from / to); it must not appear taller or differently sized than the static UI card.

## Tech Notes

Maps each product-facing behaviour above to its concrete implementation — specific files, classes, methods, commands, state paths.

- Pause menu above HUD (`UIDocument.sortingOrder`):
  - Layering among documents that share `Assets/UI/HUD/HUDPanelSettings.asset` is controlled by `UIDocument.sortingOrder` (see `.claude/rules/unity/uitoolkit.md`). Higher values draw on top. `FlyTextNotifierDocument` uses `_topMostSortingOrder` default `1000`; `EndGameWindowDocument` uses `_sortingOrder` default `1100`.
  - Today both `GameMenuUI` and `GameHUD` in `Assets/Scenes/Map.unity` serialize `m_SortingOrder: 0`. `GameMenuDocument` (`Assets/Scripts/Unity/UI/GameMenuDocument.cs`) never assigns `sortingOrder` in `Awake`/`Start`, unlike peers that set an explicit const (`LeaderboardWindowDocument` `500`, `GoalsWindowDocument` `505`, `WarProgressWindowDocument` / `WarResultWindowDocument` `510`).
  - With equal sort orders, draw order falls back to scene/document registration order; `GameHUD` appears later in `Map.unity` than `GameMenuUI`, so the HUD paints over the pause menu — matching the reported bug.
  - Fix: in `GameMenuDocument.Awake` (after caching `UIDocument`), assign `_doc.sortingOrder` to an explicit const **above all modal windows** (Leaderboard `500` / Goals `505` / War `510`) and **just below fly-text (`1000`)**, still below end-game (`1100`). Locked value: **`990`**. Follow the same “explicit sortingOrder, not scene-authoring order” comment pattern as `GoalsWindowDocument`. Prefer a const rather than relying on scene YAML alone.
  - Pause menu chrome remains `Assets/UI/Modal/GameMenu/GameMenu.uxml` + `.uss` (`.gs-blackfade` + `.gs-panel`); no new PanelSettings asset (Constitution: UI Toolkit only; layer model uses shared `HUDPanelSettings`).

- Do not unpause on card-play completion when the game was already paused:
  - Bug site: `CardPlayAnimator` (`Assets/Scripts/Unity/UI/CardPlayAnimator.cs`). Both `PlaySequence` and `PlayCountrySequence` always `_commands.Push(new PauseCommand())` at start and always `_commands.Push(new UnpauseCommand())` at end (org path ~lines 137/231; country path ~256/352), with no pause-ownership flag.
  - `TimeSystem` (`src/Game.Systems/TimeSystem.cs`) sets `GameTime.IsPaused` from any `PauseCommand` / `UnpauseCommand`; a trailing `UnpauseCommand` clears pause even if the player had paused before the play.
  - Correct pattern already in-repo: `WarResultWindowDocument` tracks `_issuedPause`, only pushes `PauseCommand` when `!_state.Time.IsPaused` (and the feature should pause), and only pushes `UnpauseCommand` when `_issuedPause` is true on close.
  - Apply the same ownership model to `CardPlayAnimator`: before pushing pause at sequence start, record whether this sequence claimed pause (e.g. `_issuedPause = !_state.Time.IsPaused`, then push `PauseCommand` only if claiming — or always push pause but only unpause if claimed). At sequence end, push `UnpauseCommand` **only if** this sequence issued/owns the pause; always clear `ModalState.IsModalOpen` as today.
  - Read pause via `VisualState.Time.IsPaused` (`src/Game.Main/TimeState.cs`), already injected on the animator.
  - Do not change `GameMenuDocument.Hide()`’s unconditional `UnpauseCommand` in this feature (separate behaviour).

- Action log: pass-through picks, raise above bottom bar, gray backdrop:
  - View: `ActionLogView` (`Assets/Scripts/Unity/UI/ActionLogView.cs`), built from `HUDDocument` (`_actionLog = new ActionLogView(...)`). Template: `Assets/UI/HUD/ActionLog/ActionLog.uxml` + `ActionLog.uss`; instance `.action-log-panel` in `Assets/UI/HUD/HUD.uxml` / layout in `HUD.uss`.
  - **Tap blocking:** UXML sets `picking-mode="Ignore"` on `action-log-root` and `action-log-content`, but `.claude/rules/unity/uitoolkit.md` documents that `PickingMode.Ignore` is **not recursive**. `BuildLabel` creates `Label` entries with default `PickingMode.Position`, so entries still steal clicks. Fix: after creating each entry (and on the `.action-log-panel` / root if needed), apply recursive `PickingMode.Ignore` (same helper pattern as `CardTransitionView.SetPickingIgnoreRecursive` / `OrgInfoDocument` / `CountryInfoView`). Re-apply after refresh diffs that add new labels.
  - **Vertical clearance:** `ActionLogView` uses fixed `BottomReservedOffsetPx = 160f` (`_root.style.bottom`). Original action-log plan treated 160 as a “representative closed-state height” of the bottom bar so the log does not jump when selection opens/closes (`Docs/Specs/26_07_18_07_action-log-ui/`). Raise to **`280f`** (matches `map-controls-panel`’s `bottom: 280px`) so the log clears the selected country/org panel. Keep the fixed bottom anchor (log does not jump when selection opens/closes); no dynamic measurement.
  - Top/right sizing (`TopGapPx`, `WidthMultiplier`, `RightPx`, `RepositionAndResize` against `.top-right-panel`) stays as today unless changing them is required to keep layout coherent after the bottom raise.
  - **Semi-transparent gray background:** add a panel background on `.action-log-panel` and/or `.action-log-root` in USS. Locked fill: **`rgba(0, 0, 0, 0.35)`**. Keep entry text legibility (`action-log-entry` white + shadow). Per UI kit rules, put color on the panel backdrop in feature USS — do not invent a second PanelSettings.

- Flying card size matches static card:
  - Static hand/overlay cards use `.action-card` in `Assets/UI/Overlay/OrgInfo/OrgActions.uss`: `width: 240px; height: 300px`. Built via `ActionCardBuilder` / `OrgActionsView` / `CountryActionsView`.
  - Flying copy: `CardTransitionView.PlaceAndAnimate` (`Assets/Scripts/Unity/UI/CardTransitionView.cs`) hardcodes `_cardCopy.style.width = 240f` and `_cardCopy.style.height = 320f` — **20px taller** than the static card. That is the size mismatch.
  - Mid-animation destination `card-test-card` in `HUD.uxml` uses class `action-card` with inline `width: 240px` only (height from USS → 300).
  - Fix: hard-match the transition card to static `.action-card` **`240×300`** (change `PlaceAndAnimate` height from `320f` → `300f`). Do not lerp width/height from source/destination `worldBound`. Keep `SetPickingIgnoreRecursive` on the copy. No change to animation timing (`Show` / `ShowCountry` durations in `CardPlayAnimator`) required for size alone.
  - Constitution: presentation-only change in Unity UI Toolkit code under `Assets/Scripts/Unity/UI/`; no ECS / game-logic changes.

## Out of Scope

- Redesigning pause-menu layout, adding Settings to the pause menu, or changing Resume/Save/Exit behaviour beyond z-order.
- Changing `GameMenuDocument.Hide()`’s always-unpause behaviour, or war-result / other modal pause-ownership systems except as a pattern reference for card play.
- New action-log line types, config (`gameLog`), animation timings, or making log entries clickable/navigable.
- Dynamic action-log follow of open character/actions slides above the bottom bar (only clearance vs the main selected country/org bar is required unless clarified).
- Resizing all card art/typography; only align flying vs static outer card dimensions.
- Introducing a separate `PanelSettings` asset or Canvas/uGUI layers.
- Web client / Razor UI parity for these Unity HUD bugs.

## Resolved Clarifications

Owner answers on #134 (locked for planning):

0. Pause-menu `sortingOrder`: above all windows, just below fly text → **`990`** (FlyText `1000`, EndGame `1100`).
1. Action-log bottom reserve: **`280px`** (fixed; no dynamic anchoring).
2. Action-log backdrop: assumed default accepted → **`rgba(0, 0, 0, 0.35)`**.
3. Flying-card size: **hard-match `240×300`** (no worldBound lerp).
