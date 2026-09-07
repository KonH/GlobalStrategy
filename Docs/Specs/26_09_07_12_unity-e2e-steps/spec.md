# Spec: End-to-End Play Step Sequences for Agent Verification

## Feature Intent

As a coding agent working on this game (and as the developer supervising that agent), I want to drive the game through a real play session step by step — reaching the map through the normal start-a-new-game and continue-a-saved-game routes, issuing game commands, operating on-screen controls, and collecting screenshots, console output and game-state snapshots as I go — so that I can verify and debug my own changes against the running game instead of asking a human to reproduce every flow by hand.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- An agent is doing verification or debugging work, and the editor is open on the project with no play session running
  - the agent decides a run is needed => it starts one on its own initiative, with no confirmation prompt and without the developer having to ask for a run in that conversation; the run is recorded as agent-initiated from its first step
  - the agent starts a run while a play session is already active => the run refuses to start and reports that a session is in progress, rather than interrupting whatever the human is doing
  - the human interrupts or stops the session mid-run => the run ends, the evidence gathered so far is preserved and reported as an incomplete run, and nothing is left half-configured

- The agent wants to reach the map by starting a new game
  - it runs the new-game sequence => the session opens on the main menu, moves to the org selection screen, selects the requested playable org, confirms, and arrives at the map with that org active
  - it runs the new-game sequence without naming an org => a default org is chosen, and the report states which org was used
  - it names an org that is not offered on the selection screen => the run stops at that step with a message naming the requested org and listing what was available

- The agent wants to reach the map from an existing saved game
  - it runs the load-save sequence => the session opens on the main menu, chooses the load/continue route, loads the requested save, and arrives at the map with that save's state in play
  - it runs the load-save sequence without naming a save => the most recent save belonging to the run is used, and the report states which one
  - it runs the load-save sequence when the run has no save to load yet => the run produces one itself as a setup step, by playing a short new-game sequence and saving it, then continues; the flow is never blocked waiting on a human
  - it names a save that the run does not have and cannot produce => the run stops at that step with a message saying so, instead of silently starting a new game

- Any run touches saved games
  - the run starts => it works only on an isolated set of saved games created for that run; the developer's own saved games are never read or written
  - the run ends => the saves it created are discarded, and the report lists which saves the run created and used

- A run has reached the map and is in a live session
  - the agent issues a game command by name with arguments => the command reaches the running simulation exactly as if a developer had typed it, and the step records whether the command was accepted and any message it returned
  - the agent issues an unknown command or bad arguments => the step fails with the rejection message, and the run's failure policy decides whether to continue or stop
  - the agent interacts with an on-screen control (press a button, choose from a list, toggle a panel, enter a value) => the interaction is delivered through the same path a human's input would take, so the game reacts exactly as it does for a player
  - the agent refers to a control that carries a stable name => that name identifies the control, and keeps working across visual redesigns of the screen
  - the agent refers to a control on a screen that has not been given stable names yet => the control's visible label identifies it instead, so scripts can be written before every screen is annotated
  - the agent targets a control that is not present or not currently interactive => the step fails with a message describing what was looked for and what the screen actually offered
  - the agent asks the session to wait for a condition (a screen to appear, a control to become available, simulation time to advance) => the step completes as soon as the condition holds, or fails once its time budget is exhausted

- Runs always use the game's default language
  - a run starts => the game is presented in the default language regardless of the developer's own preference, so reports name on-screen text consistently and an agent never reasons about translated labels
  - the developer's language preference was changed by the run => it is restored when the run ends

- Evidence is collected as a run proceeds
  - a step completes => a screenshot of the game view at that moment is captured and attached to that step
  - a step completes => any console output produced during that step (including warnings and errors) is captured and attached to that step
  - a step completes => a snapshot of a fixed core set of game state is captured and attached to that step: the current screen, the active org, the in-game date, whether the game is paused, and the current country/province selection
  - a step needs more than the core set => it can ask for additional state to be captured alongside it, without changing what every other step records
  - the agent asks for evidence on demand mid-run => the same kinds of evidence can be captured at an arbitrary point without advancing the sequence
  - a run finishes for any reason => all evidence for that run is stored together in a folder for that run alone, kept outside version control, so one run's artifacts never mix with another's and no report is ever committed alongside a change
  - runs accumulate over time => older run folders are pruned automatically, without the developer having to clean up

- The flow being verified is a known, repeatable one
  - the agent writes the flow down as a reusable step script and runs it by name => the same sequence executes the same way on later runs, without the agent restating the steps
  - the agent runs a stored script with different inputs (a different org, a different save, different command arguments) => the script runs with those inputs, without a copy of the script being made
  - the flow is one of the standard routes into the game (new game to map, load save to map) => it lives in a small committed library of standard flows that is reviewed and versioned like the rest of the project
  - the agent writes a one-off sequence while debugging => it goes in an unversioned scratch area, usable immediately and never reviewed or committed
  - a stored script references a step or screen that no longer exists => the run reports the problem against the offending step in the script, naming the script and the step position

- The agent is debugging something it cannot script in advance
  - the agent starts an interactive session and submits a single step => that step executes, its evidence is returned, and the session stays open on the state the step left behind
  - the agent submits further steps one at a time => each builds on the state left by the previous one, so the agent can look at the result before deciding what to do next
  - the agent ends the interactive session => it closes down the same way a scripted run does, and produces the same kind of report
  - the agent stops submitting steps and never returns => after an idle period the session closes itself, cleans up, and reports as an abandoned run, so no play session is ever left running

- A run has finished (whether it passed, failed, or was cut short)
  - the agent reads the run report => it finds, in one place: what was asked for, each step in order with its outcome and duration, the failure reason if any, and pointers to that step's screenshot, console output and state snapshot
  - the agent reads the run report => it can tell, without opening the editor, whether the run reached the map, how far through the sequence it got, and which step ended it
  - errors appeared in the console during the run => they are called out prominently in the report summary, not only buried in the per-step output

- Something goes wrong during a run
  - a step exceeds its allowed time => that step is failed with a timeout, evidence is captured for the moment of the timeout, and the run does not hang indefinitely
  - a step expects to take unusually long (for example while waiting for simulation time to pass) => it may raise its own time budget above the default, and only that step is affected
  - the run exceeds the whole-run cap => the run is ended with a timeout naming the step it was on; this cap always applies, no matter how individual steps raised their own budgets
  - errors appear in the console => by default they are reported and the run continues; a script that opts into strict handling instead fails the run on the first console error
  - the game throws an error or stops responding => the run ends with that failure recorded, rather than continuing to send steps into a broken session
  - a step fails => the failure policy declared by the script decides whether remaining steps are skipped or attempted, defaulting to stopping at the first failure; the report says which policy applied

- A run ends for any reason at all, including a crash or an interruption
  - the run finishes => the play session is stopped and the editor is returned to the state it was in before the run started, with no leftover play session, altered scene or changed setting
  - the run is repeated => it starts from the same known state as the first time, without manual cleanup between runs

- The three supported coding agents need to use this
  - a developer works with any one of the supported agents (Claude, Codex, or Cursor) => that agent can start, drive, and read the results of a run using instructions it already knows how to invoke, with no per-agent difference in what the runs can do
  - the reusable, project-independent portion of those instructions is changed => it is maintained once in the shared agent-tooling repositories kept alongside this project, on their main lines, and every agent picks the change up from there
  - the parts that are specific to this game (its screens, its orgs, its saves, its commands, its stored step scripts) => they live in this project's own repository as thin wrappers over the shared instructions, so project detail never leaks into the shared side
  - the shared instructions change without the project wrappers changing => existing runs keep working, and any incompatibility is reported as a clear message rather than a broken run

## Success Criteria

- An agent can take a change it just made and, unaided by a human and without asking permission first, reach the map through both supported routes and report on what it saw.
- Reaching the map via either route completes in under two minutes of wall-clock time on a developer machine.
- Every step of every run produces a screenshot, its console output, and the core game-state snapshot — no step in a completed run is missing evidence.
- A run report is enough on its own to tell whether a run succeeded and, if not, which step ended it and why — verified by a reader who never opens the editor.
- Running the same stored script twice on unchanged code produces the same sequence of step outcomes both times.
- Every run, including one ended by failure, timeout, interruption or idleness, leaves the editor in the same state it was found in and leaves no play session running — measured by repeating a run immediately with no manual cleanup and getting the same result.
- No developer save is read or modified by any run, and every save a run creates is gone once the run ends.
- No run continues past the whole-run cap; a hung or abandoned session is always reported as a timeout rather than requiring the developer to notice and intervene.
- Run artifacts never appear in a change under review, and the artifact folder does not grow without bound across repeated runs.
- All three supported agents can run the same stored script and produce equivalent reports.
- An agent can add a one-off scratch script and run it without committing anything, and a developer can add a standard flow to the committed library without touching the shared agent-tooling repositories.

## Out of Scope

- Running sequences without an editor window, or as part of an automated build or continuous-integration pipeline.
- Running sequences against a packaged, shipped build of the game.
- Pass/fail assertions declared inside a step (a step reports what happened; judging it is the reader's job).
- Video or continuous screen recording of a run.
- Performance measurement, profiling, or frame-timing capture.
- Comparing screenshots against reference images, or any other automated visual-difference check.
- Verifying the game in any language other than the default one.
- Replaying or converting a run into a regression test suite that runs unattended on a schedule.
- Retaining run artifacts long-term, or reviewing them as part of a change.
- Driving flows that do not exist in the game yet, or adding new screens/controls purely so a run can reach them.
- Annotating every existing screen with stable control names up front — that is expected follow-on work, and visible labels cover the gap meanwhile.
- Multiple concurrent runs against the same editor.
- Any change to how the game itself plays for a human.
