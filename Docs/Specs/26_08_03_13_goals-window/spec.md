# Spec: Goals Window

## Feature Intent

As a player, I want a new Goals window that lists organizations alongside their scores and lets me select one to see its progress toward each win goal as a labeled progress bar, so that I can track how close any organization is to winning without leaving the HUD.

## Acceptance Criteria

- **Given** the HUD is visible during a game **When** the player looks at the HUD button row **Then** a new "Goals" button is present immediately to the right of the existing "Leaderboard" button.
- **Given** the HUD is visible **When** the player presses (pointer-up) the "Goals" button **Then** the Goals window opens on top of the HUD, matching the Leaderboard window's modal behavior (blocks other modal-triggering input while open).
- **Given** the Goals window is open **When** the player views its layout **Then** it shows two columns: a left column listing organizations with their scores (same visual presentation and sort order as the Leaderboard window's org list), and a right info panel reserved for goal progress.
- **Given** the Goals window has just opened and no organization has been selected yet **When** the player looks at the right info panel **Then** it shows a clear empty state (no goal bars) rather than blank space or stale data, and no row in the left list appears selected.
- **Given** the Goals window is open with its org list populated **When** the player presses (pointer-up) an organization row **Then** that row becomes visually marked as selected (mirroring the existing tab-active-class-toggle idiom) and the right info panel refreshes to show that organization's goal progress.
- **Given** an organization is selected **When** the right info panel renders each goal **Then** every goal is shown as one row with the goal's description text anchored to the left and a progress bar anchored to the right, and the progress bar renders in the form "description [ N/M ]" where "[ ]" is the filled/unfilled bar track and N/M are the current and target values for that goal.
- **Given** an organization is selected and one of its goals has zero progress **When** the info panel renders that goal's bar **Then** the bar shows a fully unfilled track with N equal to the organization's current value (which may be 0) and M equal to the goal's target value.
- **Given** an organization is selected and one of its goals is partially met **When** the info panel renders that goal's bar **Then** the fill width reflects the current-value-to-target-value ratio and N/M display the current and target values exactly.
- **Given** an organization is selected and one of its goals is fully met or exceeded **When** the info panel renders that goal's bar **Then** the bar shows as fully filled (capped at 100% fill even if the underlying value exceeds the target).
- **Given** the player has selected organization A and then selects organization B **When** the selection changes **Then** the right info panel fully replaces A's goal bars with B's goal bars (no stale bars from the previous selection remain), and only B's row shows as selected.
- **Given** the Goals window is open **When** the player closes it (same close affordance as the Leaderboard window) **Then** the window hides and the HUD's modal-open state is cleared, matching Leaderboard's close behavior.
- **Given** the Goals window was closed while an organization was selected **When** the player reopens the Goals window **Then** the window's selection either persists to the same organization or resets to the no-selection empty state (exact behavior is an implementation choice, but it must be one of these two well-defined states — not an inconsistent or partial one).

## Out of Scope

- Any change to the Leaderboard window itself (its layout, tabs, or data) beyond serving as a visual precedent for the Goals window's left column.
- A live progress indicator anywhere other than inside this new Goals window (e.g. no HUD-persistent goal meter, no change to the existing static pre-game win-conditions hint on the organization-selection screen).
- New gameplay mechanics, new win conditions, or changes to how any existing goal/score/completion value is computed.
- Historical or trend data for goal progress (e.g. graphs, deltas over time) — only current-value-vs-target snapshots are shown.
- Sound, animation, or VFX polish beyond what the Leaderboard/War-progress windows already establish as precedent.
- Filtering, searching, or sorting controls for the org list beyond the existing score-sorted order already used by Leaderboard.
- Localized string content itself (final `en`/`ru` copy) — this spec only establishes that the window needs player-facing text; actual key naming and translation is a later step.

## Ambiguities

- [NEEDS CLARIFICATION: What does "goal" mean in this feature? The issue's only concrete data model precedent in the codebase is the existing three-way OR-combined completion/win condition (`total_control >= 0.8`, `full_control_countries >= 15`, `score_goal >= 275592`), which today only has a *static, pre-game* hint and no live per-org progress readout. Should the Goals window show one progress bar per existing win condition (e.g. "Control 80% of the World [ current% / 80% ]", "Full control of countries [ current / 15 ]", "Score goal [ current / 275592 ]"), or is this a request for a new, separate per-org objective/quest system unrelated to the win conditions? This is a fork in scope, not a minor detail, and the acceptance criteria above are written to be agnostic to the answer (they say "each goal" without naming specific goals).]
- [NEEDS CLARIFICATION: Should the left-column org list include every organization defined in the current game, or only organizations still actively participating (e.g. excluding ones already eliminated/defeated)? Leaderboard's existing behavior may or may not be the intended precedent here.]
- [NEEDS CLARIFICATION: The issue mentions only an "org list" for Goals, while Leaderboard has both an Orgs tab and a Countries tab. Should Goals also get a parallel Countries view, or is Goals org-only by design?]
- [NEEDS CLARIFICATION: Is the Goals window available at all times during an in-progress game, or restricted to certain phases (e.g. hidden pre-game, or disabled after the game has already ended and the end-game window is showing)?]
- [NEEDS CLARIFICATION: If a goal's target value M is itself dynamic per organization or per game configuration (as opposed to a single shared constant), does the info panel need to reflect a per-org-specific M, or is M always the same shared target across all organizations?]
