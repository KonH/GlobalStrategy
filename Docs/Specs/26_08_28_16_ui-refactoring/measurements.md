# Phase 2 measurements — cold-panel pull model

Method: Unity Editor Profiler, `GameLoop.UpdateLogic` and `GameLoop.UpdateVisualState` markers,
one representative frame, a comparable late-game world (~154 countries), every window closed.
Recorded reference baseline (from the spec, not re-measured here): `GameLoop.UpdateLogic`
8 ms / 40 KB vs `GameLoop.UpdateVisualState` 4 ms / 200 KB.

**Status: pending.** Capturing these numbers requires Unity Editor Play mode and the Profiler,
which is a User Step (`Docs/Specs/26_08_28_16_ui-refactoring/plan.md` User Steps 1 and 2), not
something this agent can do — per `.claude/rules/unity/mcp_usage.md` the agent must not self-test
in Play mode. No numbers are fabricated below; the tables are placeholders for the user's readings.

## Before (User Step 1, captured before phase 2 code changes)

| World size | `GameLoop.UpdateLogic` time | `GameLoop.UpdateLogic` alloc | `GameLoop.UpdateVisualState` time | `GameLoop.UpdateVisualState` alloc |
|---|---|---|---|---|
| _pending_ | _pending_ | _pending_ | _pending_ | _pending_ |

## After (User Step 2, captured with phase 2 code changes in place)

| World size | `GameLoop.UpdateLogic` time | `GameLoop.UpdateLogic` alloc | `GameLoop.UpdateVisualState` time | `GameLoop.UpdateVisualState` alloc |
|---|---|---|---|---|
| _pending_ | _pending_ | _pending_ | _pending_ | _pending_ |

## What changed in the code (informs what to expect, not a substitute for the reading)

With every window closed, `VisualStateConverter.Update` no longer calls `UpdateLeaderboards`
(scans every org + all countries and sorts both), `UpdateGoals`, `UpdateSelectedWar`, or
`UpdateDebugOrgCardAvailability` every tick. Those four projections are now pull-only: the
owning window (or, for the three debug-only leaderboard readers, the HUD's debug panel) calls
the extracted `LeaderboardProjector` / `GoalsProjector` / `SelectedWarProjector` /
`DebugOrgCardAvailabilityProjector` directly, on open, right after any command the window
pushes, and otherwise on a 250 ms `PullRefreshTimer` cadence while the window stays open
(skipped while the game is paused). Report the before/after numbers here once captured.
