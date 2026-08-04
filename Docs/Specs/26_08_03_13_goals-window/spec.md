# Spec: Goals Window

## Feature Intent

As a player, I want a new Goals window that lists organizations alongside their scores and lets me select one to see its live progress toward each of the game's win conditions, so that I can track how close any organization is to winning without leaving the HUD.

## Acceptance Criteria

- The HUD is visible during a game
  - Player looks at the HUD button row => a new "Goals" button is present immediately to the right of the existing "Leaderboard" button
  - Player presses (pointer-up) the "Goals" button => the Goals window opens on top of the HUD, matching the Leaderboard window's modal behavior (blocks other modal-triggering input while open), with a "Goals" header and an "X" close button at the top-right, matching the Leaderboard window's chrome
- The Goals window has just opened
  - Player views its layout => it shows two columns: a left column listing every organization in the game with its score (same visual presentation and sort order as the Leaderboard window's org list, with no filtering of eliminated/defeated organizations), and a right info panel showing goal progress
  - Player looks at the left list and right info panel => the player's own organization's row is selected by default (mirroring the existing tab-active-class-toggle idiom) and the right info panel already shows that organization's goal progress — no empty/no-selection state is ever shown
- The Goals window is open with its org list populated
  - Player presses (pointer-up) an organization row => that row becomes visually marked as selected (mirroring the existing tab-active-class-toggle idiom) and the right info panel refreshes to show that organization's goal progress
- An organization is selected
  - Right info panel renders each goal => one row per existing win condition (control 80% of the world, fully control 15 countries, reach the score goal), each shown with the goal's description text anchored to the left and a progress bar anchored to the right, rendered as "description [ N/M ]" where "[ ]" is the filled/unfilled bar track and N/M are that organization's current value and the goal's shared target value (the same constant target for every organization, taken from the existing completion-condition config)
  - One of the selected organization's goals has zero progress => the bar shows a fully unfilled track with N equal to the organization's current value (which may be 0) and M equal to the goal's target value
  - One of the selected organization's goals is partially met => the fill width reflects the current-value-to-target-value ratio and N/M display the current and target values exactly
  - One of the selected organization's goals is fully met or exceeded => the bar shows as fully filled (capped at 100% fill even if the underlying value exceeds the target)
- The player has selected organization A and then selects organization B
  - Selection changes => the right info panel fully replaces A's goal bars with B's goal bars (no stale bars from the previous selection remain), and only B's row shows as selected
- The Goals window is open
  - Player closes it (same close affordance as the Leaderboard window) => the window hides and the HUD's modal-open state is cleared, matching Leaderboard's close behavior
  - Player reopens the window after closing it with a different organization selected => the selection resets to the player's own organization again, same as the first-open default (consistent, predictable re-entry state rather than persisting the last pick)
- The Goals window is open at any point during an in-progress game (mirroring when the Leaderboard window itself is available, with no additional phase restriction)

## Tech Notes

- HUD button: `Assets/UI/HUD/HUD.uxml`, add a sibling `<ui:Button name="btn-goals" .../>` next to the existing `btn-leaderboard` inside `top-left-panel`; wire it in `Assets/Scripts/Unity/UI/HUDDocument.cs` the same way `_btnLeaderboard` is wired (`clicked` -> `_goalsWindow?.Show()`).
- Window shell: new `Assets/UI/Modal/GoalsWindow/GoalsWindow.uxml` + `.uss`, a new `GoalsWindowDocument.cs` (`[RequireComponent(typeof(UIDocument))]`) and plain `GoalsWindowView.cs`, mirroring `LeaderboardWindowDocument.cs` / `LeaderboardWindowView.cs`: `Show()`/`Hide()` toggling `GS.Unity.Common.ModalState.IsModalOpen` and `_root.style.display`, a `btn-close` wired via `PointerUpEvent` + `ContainsPoint` (not `Button.clicked`, per the known Unity 6000.4.1f1 click-event bug), and its own `SortingOrder` const distinct from Leaderboard's `500`.
- Left column: reuse `LeaderboardState.Organizations` (`IReadOnlyList<LeaderboardEntryState>` from `GS.Main.VisualState.Leaderboard`) and the same dynamic per-row `CreateRow`-style construction as `LeaderboardWindowView`; add a selected-row style class following the `.leaderboard-tab--active` toggle idiom (e.g. `.goals-row--selected`, applied via `EnableInClassList`).
- Right panel data: new per-organization goal progress needs a live-value computation the codebase doesn't expose today — `WinConditionHintProjector` (`src/Game.Main/WinConditionHintProjector.cs`) only flattens the static config thresholds, with no per-org current value. Add a new projector (e.g. `GoalsProjector` in `src/Game.Main/`) that, per organization, computes current values using the same logic already implemented in `src/Game.Systems/TotalControlCondition.cs`, `FullControlCondition.cs`, and `ScoreGoalCondition.cs` (extract a reusable "current value" accessor from each condition rather than duplicating the math), and pairs each with its shared target from `game_settings.json`'s completion-condition config (`total_control` 0.8, `full_control_countries` 15, `score_goal` — verify the exact configured value in `Assets/Configs/game_settings.json` at implementation time, since it differs from the number quoted in the original issue). Expose the result as a new `GoalsState` (per-org list of `{ Description, Current, Target }`) on `VisualState`.
- Default selection: read `VisualState.PlayerOrganization.OrgId` (`src/Game.Main/VisualState.cs`) to preselect the player's own organization on open and on every reopen.
- Localization: add `goals.title`, `hud.goals` (and any other new player-facing key this window needs) to both `Assets/Localization/en.asset` and `ru.asset`, using the `localization` skill for real Russian translations rather than English placeholders.

## Out of Scope

- Any change to the Leaderboard window itself (its layout, tabs, or data) beyond serving as a visual precedent for the Goals window's left column.
- A Countries tab/view in the Goals window — it is organization-only.
- A live progress indicator anywhere other than inside this new Goals window (e.g. no HUD-persistent goal meter, no change to the existing static pre-game win-conditions hint on the organization-selection screen or in `HUDDocument`'s win-condition labels).
- New gameplay mechanics, new win conditions, or changes to how any existing goal/score/completion value is computed — only a new read-only view onto existing values.
- Per-organization or per-game-configuration variance in a goal's target value `M` — targets are shared constants across all organizations, matching today's completion-condition config.
- Historical or trend data for goal progress (e.g. graphs, deltas over time) — only current-value-vs-target snapshots are shown.
- Sound, animation, or VFX polish beyond what the Leaderboard/War-progress windows already establish as precedent.
- Filtering, searching, or sorting controls for the org list beyond the existing score-sorted order already used by Leaderboard.
