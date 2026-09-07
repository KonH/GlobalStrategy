# Plan: End-to-End Play Step Sequences for Agent Verification

## Spec

Source: `Docs/Specs/26_09_07_12_unity-e2e-steps/spec.md` (approved).

**Intent.** A coding agent (and the developer supervising it) drives the game through a real play session step by step — reaching the map through the normal new-game and continue-a-saved-game routes, issuing game commands, operating on-screen controls, and collecting screenshots, console output and game-state snapshots as it goes — so it can verify and debug its own changes against the running game instead of asking a human to reproduce every flow by hand.

**Acceptance criteria, by group.**

1. *Starting a run.* The agent starts a run on its own initiative, no confirmation prompt, recorded as agent-initiated from step 1. A run refuses to start while a play session is already active. A human interrupt mid-run ends the run, preserves the evidence gathered so far, reports an incomplete run, and leaves nothing half-configured.
2. *New game to map.* Main menu → org selection → select requested org → confirm → map with that org active. No org named ⇒ a default is chosen and named in the report. Org not offered ⇒ stop at that step, naming the request and listing what was available.
3. *Load save to map.* Main menu → load/continue route → load requested save → map with that save's state. No save named ⇒ the run's most recent save, named in the report. No save yet ⇒ the run produces one itself by playing a short new-game sequence and saving, never blocking on a human. Named save that cannot be produced ⇒ stop with a message, never silently start a new game.
4. *Saves.* A run works only on an isolated set of saves created for it; the developer's saves are never read or written. At run end the saves it created are discarded and the report lists which saves it created and used.
5. *Live session.* Issue a game command by name with arguments, reaching the simulation exactly as if a developer had typed it, recording acceptance and any returned message; unknown command/bad arguments fail the step with the rejection message and the failure policy decides continue-or-stop. Interact with an on-screen control (press a button, choose from a list, toggle a panel, enter a value) delivered through the same path a human's input would take. A control with a stable name is addressed by that name, surviving visual redesigns; a control on an un-annotated screen is addressed by its visible label. A missing or non-interactive target fails with a message describing what was looked for and what the screen actually offered. A wait step (screen appears, control becomes available, simulation time advances) completes as soon as the condition holds or fails when its time budget is exhausted.
6. *Language.* Runs always present the game in the default language regardless of the developer's preference; a preference changed by the run is restored at run end.
7. *Evidence.* Every completed step attaches a screenshot, that step's console output (warnings and errors included), and a core state snapshot: current screen, active org, in-game date, pause state, current country/province selection. A step may request extra state without changing what every other step records. Evidence can also be captured on demand mid-run without advancing the sequence. All of a run's evidence lands in one folder for that run alone, kept out of version control; older run folders are pruned automatically.
8. *Step scripts.* A flow written down as a reusable script runs by name and executes identically later. A stored script runs with different inputs (org, save, command arguments) without being copied. Standard routes into the game live in a small committed, reviewed library; one-off debugging sequences live in an unversioned scratch area. A script referencing a step or screen that no longer exists reports the problem against the offending step, naming the script and the step position.
9. *Interactive mode.* Start a session, submit one step, get its evidence back, session stays on the state that step left. Further steps build on the previous state. Ending the session closes down like a scripted run and produces the same kind of report. Abandoning it closes itself after an idle period, cleans up, and reports as an abandoned run.
10. *Run report.* One place holding: what was asked for, each step in order with outcome and duration, the failure reason if any, and pointers to that step's screenshot, console output and state snapshot. A reader who never opens the editor can tell whether the run reached the map, how far it got, and which step ended it. Console errors are called out prominently in the summary, not only buried per-step.
11. *Failures.* Step over its time budget ⇒ failed with a timeout, evidence captured at the moment of timeout, no indefinite hang. A step may raise its own budget above the default, affecting only itself. The whole-run cap always applies regardless of per-step budgets and names the step it was on. Console errors are reported and the run continues by default; a script may opt into strict handling and fail on the first console error. A game error or unresponsive session ends the run with that failure recorded. A failed step consults the script's failure policy (default: stop at first failure) and the report says which policy applied.
12. *Cleanup.* However a run ends — including crash or interruption — the play session is stopped and the editor is returned to its pre-run state: no leftover play session, altered scene, or changed setting. A repeat run starts from the same known state with no manual cleanup.
13. *Three agents.* Claude, Codex and Cursor can each start, drive and read a run through instructions they already know how to invoke, with no per-agent difference in capability. The reusable, project-independent portion is maintained once in the shared agent-tooling repositories on their main lines; the game-specific parts (screens, orgs, saves, commands, stored scripts) live in this repo as thin wrappers. A shared-instruction change without a wrapper change keeps existing runs working, and any incompatibility is reported as a clear message rather than a broken run.

**Out of scope.** Headless/CI/batch runs, packaged builds, in-step assertions, video capture, performance measurement, screenshot diffing, non-default languages, scheduled regression suites, long-term artifact retention, driving flows that do not exist yet, blanket annotation of every screen with stable names, concurrent runs against one editor, and any change to how the game plays for a human.

## Goal

Deliver a project-side **E2E step runner** driving the Unity Editor in Play mode: step-script/report/sequencing logic and its JSON serialization in a new `src/Game.E2E` (netstandard2.1, `Assets/Plugins/Core/`, under `dotnet test`); a runtime driver assembly `Assets/Scripts/Unity/E2E/` that receives game services from an `E2ESessionBridge` registered in each scene scope, delivers interactions as **device-level Input System events**, and captures per-step evidence; an Editor-side watcher assembly `Assets/Scripts/Editor/E2E/` owning Play-mode lifecycle, isolation, restoration and stale-lock recovery; a **file-handshake transport** under a gitignored `.e2e/` that all three agents drive identically without holding an MCP connection; a committed flow library at `Docs/E2E/flows/`; the terminal command pipeline extracted from `src/Game.WebClient/Terminal/` into a Unity-consumable `src/Game.Commands.Text`; and the skills split across `../ClaudeTools`, `../CodexTools` and this repo, with `.claude/rules/unity/mcp_usage.md` amended to permit runner-driven Play mode. `com.unity.ui.test-framework` is **optional** and adopted only if Step 0 justifies it (§Approach 3).

## Approach

### 1. Layering

| Layer | Home | Responsibility |
|---|---|---|
| Core logic + artifact I/O | `src/Game.E2E/` (netstandard2.1 → `Assets/Plugins/Core/`) | `StepScript`/`RunRequest`/`RunReport` DTOs, script validation, parameter substitution, failure-policy + timeout-budget sequencing, markdown report rendering, and JSON (de)serialization of every artifact it owns. No Unity. Newtonsoft.Json 13.0.3 with `<ExcludeAssets>runtime;native</ExcludeAssets>`, exactly as `src/Game.Configs/Game.Configs.csproj` does. |
| Terminal pipeline | `src/Game.Commands.Text/` (netstandard2.1 → `Assets/Plugins/Core/`) | Extracted `CommandRegistry`/`TerminalParser`/`ValueCoercion`/`CommandExecutor` — one source of truth for text → `ICommand` → `Push`, shared by the web terminal and the runner. |
| Runtime driver | `Assets/Scripts/Unity/E2E/` (`GS.Unity.E2E.asmdef`) | Play-mode host: element resolution, input injection, command execution, evidence capture, per-step handshake, `E2ESessionBridge`. |
| Editor control | `Assets/Scripts/Editor/E2E/` (`GS.Editor.E2E.asmdef`, `includePlatforms: ["Editor"]`, `autoReferenced: false`) | Request watcher, active-session refusal, stale-lock recovery, isolation setup/teardown, Play-mode enter/exit, editor-state restore, artifact pruning, cap watchdog. |
| Transport | `.e2e/` (gitignored) | Requests, run folders, interactive inbox/outbox, `current_run.json` lock. |
| Flow library | `Docs/E2E/flows/*.json` (committed) | Standard routes, reviewed and versioned. Precedent: `Docs/BotFeatures/<id>/eval_config.json`. |

**Why `Game.E2E` owns JSON.** Every artifact it defines is JSON, and the Tests section requires reading the committed `Docs/E2E/flows/*.json` from disk. If the tests parsed with a different library than the runner, a committed flow could pass the suite and still break the runner. One serializer, one contract. The `ExcludeAssets` form keeps Unity's `com.unity.nuget.newtonsoft-json` as the runtime provider and keeps a conflicting `Newtonsoft.Json.dll` out of `Assets/Plugins/Core/`; field names are camelCase per `.claude/rules/unity/plugins.md`.

### 2. Transport — file handshake, not a live MCP connection

The connected MCP server (`com.coplaydev.unity-mcp`) has no screenshot tool and no input-simulation tool, and neither Codex nor Cursor is guaranteed to have it at all. Every one of the three agents can, however, read and write files. So the agent-facing surface is a **filesystem request/response handshake**, which also survives the agent disconnecting mid-run:

```
.e2e/
  requests/<runId>.json         agent drops a RunRequest here to start a run
  current_run.json              active-run lock + pointer + start timestamp
  runs/<runId>/
    request.json                the request as accepted
    editor_state.json           pre-run editor state, written before anything is changed
    report.json / report.md     machine + human report
    steps/<nnn>_screenshot.png
    steps/<nnn>_console.log
    steps/<nnn>_state.json
    inbox/step_<n>.json         interactive: agent writes the next step here
    outbox/step_<n>.json        interactive: runner writes that step's result here
  scratch/<name>.json           unversioned one-off scripts
```

`E2ERunWatcher` (`Assets/Scripts/Editor/E2E/`, `[InitializeOnLoad]`) polls `.e2e/requests/` from `EditorApplication.update`, throttled to ~4 Hz, and does nothing while `EditorApplication.isCompiling` or `isUpdating`. `[InitializeOnLoad]` re-runs after every domain reload, including entering and leaving Play mode, so the watcher re-arms itself; **no runner state lives in a static** — everything that must survive a domain reload lives in the `.e2e/` files.

**A domain reload during a run kills the run, so the run must end first.** The `isCompiling` guard only gates *accepting* a request. A multi-minute run plus anyone saving a `.cs` file makes Unity recompile and reload the domain while in Play mode, destroying `E2ERunnerHost`'s coroutines and every non-serialized field on it — "no state in a static" does not help, because MonoBehaviour instance state and coroutines die too, and the run would silently stop advancing while the lock still claims it is active. `E2ERunWatcher` therefore subscribes `AssemblyReloadEvents.beforeAssemblyReload`: if a run is active and unfinished, it finalizes the report as `interrupted (assembly reload)` and runs the normal teardown before the reload proceeds, so the cause is named in the report rather than left as a mystery hang. The skills state plainly that **no source file may be edited while a run is in progress**.

Claude may additionally nudge the watcher via `execute_code`, but nothing in the design requires it; the file drop alone is sufficient and is what the skills instruct.

`RunRequest.protocolVersion` is compared against `E2EProtocol.Version` in `src/Game.E2E`. On mismatch the watcher writes a report with `outcome: "protocol-mismatch"` naming both versions and runs nothing — this is AC group 13's "clear message rather than a broken run".

### 3. Interaction fidelity — the design tension, resolved

`com.unity.ui.test-framework` dispatches UI Toolkit events straight to the panel and is documented as a testing package. The spec requires a live driven session with an interactive step mode. Verified facts that decide this:

- All four scenes (`MainMenu`, `CountrySelection`, `Map`, `Gallery`) already contain an `EventSystem`, and `com.unity.inputsystem` 1.19.0 ships `InputSystem/Plugins/InputForUI/InputSystemProvider.cs`, which routes Input System device events into UI Toolkit runtime panels.
- `InputSystem.QueueStateEvent<TState>(InputDevice, TState, double)` (`InputSystem.cs:2602`) and `InputSystem.Update()` (`:2800`) are **public runtime API**, and `MouseState` (`Devices/Mouse.cs:16`) with `WithButton` (`:115`) is public. None of this needs a test assembly.
- Most of this project's click sites go through `VisualElementClickExtensions.OnClick`, which listens for `PointerUpEvent` with `evt.button == 0`, `enabledInHierarchy`, and `ContainsPoint(evt.localPosition)` — because `Button.clicked`/`ClickEvent` were found broken on this Unity 6 line (`.claude/rules/unity/uitoolkit.md`). A device-level mouse event produces exactly that `PointerUpEvent`.
- **`save-list` rows are the exception.** `LoadWindowView.MakeRow` builds its Load/Delete buttons with `new Button(Action)` — the `Clickable` manipulator, not `.OnClick()`. Since the project's own rule says `Button.clicked` is unreliable here, Step 0 must confirm a device-injected press/release actually fires a row button before `load_save_to_map.json` is relied on. If it does not, the row click falls back to the panel path and the report records that.

**Chosen option: (c) hybrid, with device-level injection as the primary path.**

- **Primary — device-level.** `E2EInputDriver` computes the target's screen point from `element.worldBound`, queues a `MouseState` move, then press, then release via `InputSystem.QueueStateEvent` + `InputSystem.Update()`, one frame apart. Keyboard entry for text fields uses the same mechanism on `Keyboard.current` plus `InputSystem.QueueTextEvent` for characters. This is genuinely "the same path a human's input would take": OS device event → Input System → `InputForUI` → `EventSystem` → UI Toolkit panel → the element's own `PointerUpEvent` handler, exercising `UIPointerState`/`ModalState` click-blocking, pointer capture, hover/enter-leave ordering and `sortingOrder` hit-testing along the way.
- **Fallback — hand-rolled panel dispatch, by default.** A `panel.visualTree.SendEvent(PointerDownEvent/PointerUpEvent)` pair plus a `yield return null` settle helper is the default fallback, implemented directly in `E2EInputDriver`. It takes no package dependency and is a few dozen lines.
- **`com.unity.ui.test-framework` is optional.** §3 already establishes that nothing on the primary path depends on it and that the fallback is small. It is therefore adopted only if Step 0 shows *both* that it has a release compatible with Unity 6000.5.5f1 *and* that its simulation API is callable from a non-test assembly — **and** the owner wants it (User Step 1). Otherwise the plan proceeds unchanged with the hand-rolled fallback and the package is never added.
- **Where the fallback falls short, honestly.** Panel-level dispatch bypasses the Input System, the `EventSystem`, and therefore `UIPointerState`/`ModalState` guards and cross-panel `sortingOrder` hit-testing. A regression in click-blocking or modal gating would be invisible to it. Mitigation: every step records `inputPath: "device" | "panel"` in its state snapshot and the report summary counts panel-path steps, so a reader knows exactly which interactions were lower-fidelity. The runner only falls back when the device path cannot address the target, and it says so in the step outcome rather than silently downgrading.

**Supporting detail — the Editor input routing knob, which is not `backgroundBehavior`.** `backgroundBehavior` is the *player-side* setting. What decides whether injected pointer/keyboard events reach player code in the Editor is `InputSettings.editorInputBehaviorInPlayMode`, whose default `PointersAndKeyboardsRespectGameViewFocus` routes pointer and keyboard input to the editor and away from player code whenever no Game view is focused — exactly the agent-driven case, in which the entire primary path would silently deliver zero events. For the duration of a run the runner therefore sets **both** `editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView` and `backgroundBehavior = IgnoreFocus`, records **both** prior values in `editor_state.json`, and restores both at teardown. Step 0 confirmed a device-injected `PointerUpEvent` reaches a button with the Game view unfocused, for both `.OnClick()` and `Clickable`-manipulator buttons — see the Spike result under Step 0.

**A third setting, and it is not per-run.** Step 0 measured `Application.runInBackground == false` throttling the player loop to roughly 97 frames per 88 s while the Editor is unfocused — enough that `save-list` rows never realized and every `waitFor` step would exhaust its budget waiting for frames that are not running. **Owner decision: set `PlayerSettings.runInBackground = true` globally** (Step 5), rather than having the runner toggle it per run. That is one committed change to `ProjectSettings.asset`, removes a restore path from the runner entirely, and makes ordinary development nicer since the game keeps running when the Editor loses focus. It does ship — built players will also run in the background, which is harmless for a turn-paced strategy game. Only the two *input* settings stay per-run and get recorded in `editor_state.json`.

**Two stale version references.** `CLAUDE.md` and `.claude/rules/unity/uitoolkit.md` both still say Unity 6000.4.1f1; `ProjectSettings/ProjectVersion.txt` says **6000.5.5f1**. Step 10 corrects both files.

### 4. Isolation — one redirect covers saves, settings and language

`PersistentStorage` (`Assets/Scripts/Unity/Save/PersistentStorage.cs`) hardcodes `Application.persistentDataPath` and is `new`'d at **four** sites — `GameLifetimeScope.cs:36`, `MainMenuLifetimeScope.cs:17`, `ProjectLifetimeScope.cs:14` and `SelectCountryLifetimeScope.cs:58` (which also registers `SaveFileManager`). CountrySelection sits on the new-game route, so missing it would let a run read and write the developer's real `Saves/`. Add a constructor overload `PersistentStorage(string root)`; **all four** scopes construct it as `new PersistentStorage(E2ERunContext.StorageRootOrDefault())`, where `E2ERunContext` (`GS.Unity.E2E`) reads `.e2e/current_run.json` once per domain load and returns `Application.persistentDataPath` when no run is active. Normal play is byte-for-byte unchanged.

Redirecting the root buys three acceptance-criteria groups at once:

- **Group 4 (saves).** The run's saves live under `.e2e/runs/<runId>/persistent/Saves/`. `SaveFileManager.ListSaves()` sees only them; the developer's saves are never read or written. Teardown deletes the whole `persistent/` subtree and the report lists what was created and used (recorded per-step, not derived from the deleted folder).
- **Group 6 (language).** `SettingsStorage` reads `settings.json` from the same root, so the developer's preference is never touched — "restored at run end" holds trivially. `SettingsData.Locale` defaults to `""`, so the runner **seeds** `settings.json` in the isolated root at run start with the default locale from `LocalizationConfig`, rather than relying on an empty-string fallback. The seeded file uses camelCase keys, which Newtonsoft matches case-insensitively against `SettingsStorage`'s private `SettingsData`. The report records the locale used.
- **Determinism (groups 8, 12).** `SettingsData.TutorialsEnabled` defaults to `true` and `CompletedTutorialIds` is empty, so a fresh isolated root would pop tutorial overlays into every run and derail control resolution. The seeded `settings.json` therefore sets `tutorialsEnabled: false` by default, overridable per script via `RunRequest.settings`.

One more behaviour to respect: `GameLoopRunner.Start()` has a DevAutoLoad path that loads the latest save when no `SceneTransitionArgs` are set. With an empty isolated root there is no save to auto-load, so a new-game run starts clean — this is relied on, and asserted by a step in the smoke run.

### 5. Run lifecycle and editor restoration (groups 1, 11, 12)

`E2ERunWatcher` accepts a request only when **all** hold, else it writes a refusal report and consumes the request:

- `!EditorApplication.isPlaying` and no `.e2e/current_run.json` (AC "refuses while a session is in progress" — never interrupts a human).
- No open scene is dirty (`EditorSceneManager`), so restoration cannot destroy unsaved work.
- `RunRequest.protocolVersion` matches.

On accept it records `.e2e/runs/<runId>/editor_state.json` (active scene path, prior `editorInputBehaviorInPlayMode`, prior `backgroundBehavior`) **before changing anything**, prunes old run folders (keep newest 20, pruned at **start** so the last run survives for reading), opens `MainMenu.unity`, writes `.e2e/current_run.json` with a start timestamp, and sets `EditorApplication.isPlaying = true`.

`E2ERunnerBootstrap` (`GS.Unity.E2E`, `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`) reads `.e2e/current_run.json`; absent ⇒ returns immediately, so there is zero runtime cost to normal play. Present ⇒ creates the `DontDestroyOnLoad` `E2ERunnerHost`.

Teardown is driven by `EditorApplication.playModeStateChanged`, so it runs for every **in-process** exit — a human pressing Stop, an assembly reload (§2), a normal finish: finalize the report (marking `interrupted` if steps remain), delete `persistent/`, restore both input settings and the previously open scene, delete `.e2e/current_run.json`. Because the report is written incrementally after every step, an abrupt exit still leaves every completed step's evidence intact (group 1's "evidence gathered so far is preserved").

**Stale-lock recovery covers what teardown cannot.** `playModeStateChanged` does *not* fire when the Editor process dies, and the spec demands restoration "for any reason at all, including a crash". A stale `.e2e/current_run.json` would otherwise keep every subsequent *normal* play session redirected to a dead isolated save root and leave `InputSystem.settings` mutated project-wide. So `E2ERunWatcher`'s `[InitializeOnLoad]` static constructor runs recovery **before** it begins polling: if `.e2e/current_run.json` exists while `!EditorApplication.isPlaying`, it finalizes that run's report as `crashed`, deletes its `persistent/`, restores input settings and scene from its `editor_state.json`, and deletes the lock. Because `editor_state.json` is written before any state is changed, it is always the authority on what to restore.

Budgets: per-step default 30 s, overridable per step (`timeoutSeconds`); whole-run cap default 300 s, enforced by the host regardless of per-step budgets and naming the step it was on; interactive idle timeout default 120 s since the last inbox file, ending the run as `abandoned`.

**The cap's honest limit.** If game logic hangs the main thread, the host's coroutine never resumes — and `EditorApplication.update` runs on that same thread, so an Editor-side watchdog is frozen too. Two mitigations and one plain admission:

- Belt and braces: `E2ERunWatcher` checks the lock's start timestamp on every `EditorApplication.update` and force-exits Play mode once the cap is exceeded by more than 30 s. This covers a host that stopped advancing for a recoverable reason — a lost coroutine, a stuck wait — without the session having ended.
- A **hard** main-thread hang defeats both, because nothing on that thread runs. The developer must kill the Editor; on the next load, stale-lock recovery finalizes the run as `crashed`. The skills say this outright rather than promising a guarantee the design cannot make.

### 6. Control addressing (group 5)

`E2EElementResolver` searches every live `UIDocument` panel in descending `sortingOrder`:

1. `root.Q(name: <name>)` — stable name first. The screens the standard flows touch already have them: `btn-play`, `btn-load`, `btn-resume`, `btn-settings` (`Assets/UI/Modal/MainMenu/MainMenu.uxml`), `btn-start`, `btn-back` (`Assets/UI/Modal/SelectCountry/SelectCountry.uxml`), `save-list`, `btn-back` (`Assets/UI/Modal/LoadWindow/LoadWindow.uxml`). No blanket annotation is needed and none is done.
2. Visible-label fallback — case-insensitive exact match on `Button.text`/`Label.text`, then contains, across the same panels.
3. **Indexed** `ListView` rows: `{ "name": "save-list", "row": { "index": N } }`. Label matching is not available for these rows and must not be attempted — `LoadWindowView.BindRow` writes only `SaveFileInfo.OrganizationId` and `GameDate` into a row, never `SaveName` (which is what `{{save}}` names), and both row buttons carry the shared localized labels `load.btn_load` / `load.btn_delete`, identical on every row, so a label match would hit Delete as readily as Load. `save-list` is also `virtualization-method="FixedHeight"` with `selection-type="None"`, so an off-screen row has no realized `VisualElement` at all. `E2ESelectRowStep` therefore resolves `{{save}}` to an index via `SaveFileManager.ListSaves()` — the same ordering `LoadWindowView` binds — calls `ScrollToItem(index)`, settles one frame, then clicks that row's **first** `Button` descendant (Load), never matching by text. An index `ListSaves()` does not contain fails the step, listing the save names it did return.

A candidate must be `enabledInHierarchy`, not `display: none`, and have a non-zero `worldBound`. On failure the step reports what was looked for plus the names and labels actually present on the topmost panel (capped at 40 entries) — AC "describing what was looked for and what the screen actually offered".

Screen point: for these screen-space-overlay panels, `screen = panelPoint * (Screen.width / panel.visualTree.layout.width)` with a Y flip. The resolver **round-trips the computed point back through `RuntimePanelUtils.ScreenToPanel` and fails the step if it does not land inside the element**, so a scaling assumption can never silently mis-click.

### 7. Services, commands and the session bridge (group 5)

**`E2ESessionBridge` is how the host reaches game services.** The host is created before any scene loads and outlives every scene, so it cannot itself be container-managed — but "resolve from the active `LifetimeScope`" has no mechanism that is both available and permitted: `LifetimeScope.Find<T>()` is constrained to a concrete `LifetimeScope` subtype, those subtypes live in `GS.Unity.DI` (which §Steps 5–6 make reference `GS.Unity.E2E`, so a reverse reference would be a hard assembly cycle), and `FindObjectsByType<LifetimeScope>()` is forbidden by the Constitution. There are also two root scopes per scene, so "the active scope" is ambiguous anyway.

Instead, each of the three game scopes — `GameLifetimeScope`, `MainMenuLifetimeScope`, `SelectCountryLifetimeScope` — adds one line, `builder.RegisterEntryPoint<E2ESessionBridge>()`. `E2ESessionBridge` (`GS.Unity.E2E`, constructor-injected with `VisualState`, `IWriteOnlyCommandAccessor`, `SaveFileManager`, `ILocalization`) publishes itself to the host on `Start` and clears itself on `Dispose`. The host therefore always holds the current scene's services, resolved through the container, and holds nothing at all after a scene unloads. `Assets/Scripts/Unity/DI/GS.Unity.DI.asmdef` gains the `GS.Unity.E2E` reference; `GS.Unity.E2E` never references `GS.Unity.DI`. The Gallery scene registers no bridge and is deliberately not drivable — no flow touches it.

**Commands — extract, do not reimplement.** `src/Game.WebClient/Terminal/{CommandRegistry,TerminalParser,ValueCoercion,CommandExecutor}.cs` already implement text → `ICommand` → `Push`, with tests in `src/Game.WebClient.Tests`. `Game.WebClient` is a Blazor WASM project and is not among the DLLs Unity consumes, so the code moves to a new **`src/Game.Commands.Text`** (netstandard2.1, `Assets/Plugins/Core/` output, references `Game.Commands` + `Game.Main`, no `System.Text.Json`), namespace `GS.Game.Commands.Text`. `Game.WebClient` gains a `ProjectReference` to it; `Terminal/Suggestions/`, `Program.cs` and `Components/Terminal.razor` update their `using`. Suggestion providers stay in `Game.WebClient` — they depend on web-side config plumbing and the runner does not need Tab completion.

`.claude/skills/add-terminal-command/SKILL.md` is updated: the reflection contract now lives in `src/Game.Commands.Text/`, and it now serves the Unity runner as well as the web terminal, so its invariants are load-bearing in two places. Its checklist is otherwise unchanged.

`E2ECommandStep` takes `IWriteOnlyCommandAccessor` from `E2ESessionBridge` and calls `CommandExecutor.Execute(line, accessor)`, recording `ExecutionResult.Success` and `Message` verbatim — literally the same code path a developer typing into the web terminal takes.

### 8. Evidence (group 7)

- **Screenshot.** Factor a reusable `ScreenCaptureUtil` into `GS.Unity.Common` (runtime, already referenced everywhere) with **two entry points over one shared encode/write helper**, because a static `[MenuItem]` has no MonoBehaviour to run a coroutine on: `IEnumerator CaptureTo(string path)` for the runner (`yield return new WaitForEndOfFrame()`, then the shared helper) and `void CaptureImmediate(string path)` for the menu item. Both use `ScreenCapture.CaptureScreenshotAsTexture()` → `EncodeToPNG` → `File.WriteAllBytes` → `Destroy(texture)`, which is deterministic and writes to an arbitrary path, unlike `ScreenCapture.CaptureScreenshot`'s fire-and-forget. `Assets/Scripts/Editor/Utils/ScreenshotCapture.cs` keeps its `GS/Screenshot %#s` shortcut and its `validate = true` Play-mode gate, calling `CaptureImmediate`.
- **Console.** `E2EConsoleCollector` subscribes `Application.logMessageReceived` for the run's lifetime and buffers `(type, condition, stackTrace)` per step, flushed to `steps/<nnn>_console.log`. Error/exception counts roll up into the report summary.
- **Core state.** `E2EStateSnapshot` reads the active scene name plus the visible modal document (current screen), and takes `VisualState` from `E2ESessionBridge` for `PlayerOrganization.OrgId`, `Time.CurrentTime`, `Time.IsPaused`, `SelectedCountry.CountryId`, `SelectedProvince` — every field the spec's fixed core set names is already on `VisualState`. Extra state is opt-in per step via `"extraState": ["gold", "actionLog", ...]`, resolved by a small named-projection table; it never changes the core set.
- **On demand.** A `capture` step kind produces all three without advancing the sequence, and in interactive mode an inbox file with `{"kind":"capture"}` does the same.

### 9. Step kinds and the flow library (groups 2, 3, 8)

Primitives, all validated in `src/Game.E2E`: `click`, `selectOrg`, `selectRow`, `setValue`, `command`, `waitFor` (screen / control / gameDate / pause), `capture`, `sleepFrames`. The standard routes are ordinary scripts over these primitives with `{{org}}` / `{{save}}` placeholders substituted from `RunRequest.inputs`, so "run a stored script with different inputs without copying it" needs no new machinery.

**`selectOrg` exists because orgs are chosen by a map click, not a UI control.** `Assets/UI/Modal/SelectCountry/SelectCountry.uxml` contains only info labels plus `btn-start`/`btn-back` — there is no org list and no per-org control, so `E2EElementResolver` has nothing to address and cannot enumerate offered orgs from the UI. An org becomes selected only when `MapClickHandler` pushes `SelectCountryCommand(ownerId)` for that org's HQ country (`MapClickHandler.cs:100`), which `SelectOrgLogic` maps back to an org through its `_hqToOrg` table (`SelectOrgLogic.cs:46`); `SelectOrgDocument` keeps `btn-start` at `SetEnabled(false)` until `SelectedOrganization.IsValid`. So `E2ESelectOrgStep` (`Assets/Scripts/Unity/E2E/Steps/`) resolves `{{org}}` to its `HqCountryId` from `OrganizationConfig.Organizations`, pushes `SelectCountryCommand { CountryId = <hq> }` through the `IWriteOnlyCommandAccessor` that `SelectCountryLifetimeScope` registers (via `SelectOrgLogic.Commands`) — the same command a human's map click produces — then waits for `VisualState.SelectedOrganization.IsValid` and for `btn-start.enabledSelf`. An unknown org fails **before any command is pushed**, naming the request and listing `Organizations`' ids.

- `Docs/E2E/flows/new_game_to_map.json` — `click btn-play` → `waitFor` the org screen → `selectOrg {{org}}` (default: first entry in `organizations.json`, named in the report) → `click btn-start` → `waitFor` the map with that org active.
- `Docs/E2E/flows/load_save_to_map.json` — `click btn-load` → `waitFor save-list` → `selectRow` at the index resolved from `{{save}}` (§6.3) → `waitFor` the map. With `{{save}}` unset it uses the run's most recent save. With **no** run-owned save yet it first executes `new_game_to_map.json` and a `SaveGame` command as a setup step, then continues — the run never blocks on a human. A named save that neither exists nor can be produced fails the step rather than silently starting a new game.

Scratch scripts live at `.e2e/scratch/<name>.json`, are usable immediately, and are gitignored. A script referencing an unknown step kind or an unresolvable control fails validation (unknown kind) or the step (unresolvable control) with the script name and the zero-based step index — group 8's last bullet.

Failure policy: `RunRequest.failurePolicy` ∈ `stopOnFirstFailure` (default) | `continueOnFailure`; `RunRequest.consoleErrors` ∈ `report` (default) | `strict`. Both are echoed in the report summary so a reader knows which applied.

### 10. Skills split (group 13)

**`../ClaudeTools` (separate repo, its own commit on `main`)** — `plugins/cc/skills/unity-e2e-run/SKILL.md`, project-independent: the handshake protocol (RunRequest fields, run-folder layout, report shape, `protocolVersion`), how to start a run, how to recognise and respect a refusal, how to poll for `report.json`, how to submit and read interactive steps, idle/abandonment rules, how to read outcomes, the no-source-edits-during-a-run rule (§2), and the honest statement of the hard-hang limit (§5). No paths, screens, orgs or command names from this game.

**`../CodexTools` (separate repo, its own commit on `main`)** — `plugins/cd/skills/unity-e2e-run/SKILL.md`, the same content adapted to Codex conventions. Bump `plugins/cd/.codex-plugin/plugin.json` `version` `0.2.0` → `0.3.0` (the Claude manifest carries no version field and needs no change).

**This repo** — `.claude/skills/unity-e2e-run/SKILL.md` delegates to `cc:unity-e2e-run`, and `.agents/skills/unity-e2e-run/SKILL.md` to `cd:unity-e2e-run`, following the `dotnet-build` wrapper pattern. Each adds only the project specifics: the `.e2e/` and `Docs/E2E/flows/` paths, the two standard flow names, the scene/screen names, where org ids come from (`Assets/Configs/organizations.json`), that commands are the `src/Game.Commands` set, and the explicit statement that starting a run needs no confirmation.

**Cursor** — a `.cursor/commands/unity-e2e-run.md` slash command pointing at `.claude/skills/unity-e2e-run/SKILL.md`, and **no `.cursor/skills/` wrapper**. The existing `cursor-meeting-*` wrappers exist solely because meetings need a Cursor-specific session identity; this feature has no Cursor-only override — the file handshake is identical for all three agents — so a wrapper would be pure duplication. This matches the documented default that Cursor uses the Claude skill directly.

`CLAUDE.md`'s Configuration Index gains an entry in the existing style.

### 11. Documentation changes

`.claude/rules/unity/mcp_usage.md`'s "Do not self-test in Play mode" section is rewritten (not deleted). It keeps its two standing cautions — do not hand-roll ad-hoc synthetic input events, and never interrupt an active human session — and adds that agent-initiated Play-mode runs **through this feature's runner** are permitted for verification and debugging work with no per-run confirmation, because the runner delivers device-level input through the real input stack, refuses to start while a session is active, and restores the editor afterwards. Ad-hoc `manage_editor(action="play")` and hand-dispatched `PointerDownEvent`/`PointerUpEvent` remain discouraged.

`CLAUDE.md` and `.claude/rules/unity/uitoolkit.md` have their stale `6000.4.1f1` references corrected to `6000.5.5f1` (§3).

`.gitignore` gains `.e2e/`. `Docs/E2E/flows/` is committed and explicitly not ignored.

## Technical Mapping

- **Group 1 — starting, refusing, interruption:**
  - `Assets/Scripts/Editor/E2E/E2ERunWatcher.cs` — `[InitializeOnLoad]`, stale-lock recovery then throttled `EditorApplication.update` polling of `.e2e/requests/`; accept guard on `EditorApplication.isPlaying`, `.e2e/current_run.json`, scene dirtiness, `protocolVersion`; writes a refusal report and consumes the request otherwise; `AssemblyReloadEvents.beforeAssemblyReload` finalizes an active run as `interrupted (assembly reload)`.
  - `RunReport.Initiator` in `src/Game.E2E/RunReport.cs`, set from `RunRequest.Initiator` (`agent` default) and stamped on step 1.
  - `Assets/Scripts/Editor/E2E/E2ERunTeardown.cs` — `EditorApplication.playModeStateChanged` handler finalizing an `interrupted` report; report written incrementally after each step so partial evidence survives.

- **Group 2 — new game to map:**
  - `Docs/E2E/flows/new_game_to_map.json`; control `btn-play` (`Assets/UI/Modal/MainMenu/MainMenu.uxml`) and `btn-start` (`Assets/UI/Modal/SelectCountry/SelectCountry.uxml`).
  - Org selection is **not** a control click: `Assets/Scripts/Unity/E2E/Steps/E2ESelectOrgStep.cs` resolves `{{org}}` → `HqCountryId` from `OrganizationConfig.Organizations`, pushes `SelectCountryCommand { CountryId = <hq> }` through `SelectCountryLifetimeScope`'s `IWriteOnlyCommandAccessor` (the same command `MapClickHandler.cs:100` issues), then waits for `VisualState.SelectedOrganization.IsValid` and `btn-start.enabledSelf`.
  - Default org = first `OrganizationConfig.Organizations` entry from `Assets/Configs/organizations.json`, recorded in `RunReport.ResolvedInputs`.
  - Not-offered org: `E2ESelectOrgStep` fails before pushing any command, naming the requested org and listing `Organizations`' ids.
  - Arrival assertion: `VisualState.PlayerOrganization.OrgId` equals the requested org, via `E2EStateSnapshot`.

- **Group 3 — load save to map:**
  - `Docs/E2E/flows/load_save_to_map.json`; controls `btn-load`, `save-list`, `btn-back` (`Assets/UI/Modal/LoadWindow/LoadWindow.uxml`).
  - `Assets/Scripts/Unity/E2E/Steps/E2ESelectRowStep.cs` — resolves `{{save}}` to an index via `SaveFileManager.ListSaves()` (the ordering `LoadWindowView` binds), `ScrollToItem(index)`, one settle frame, then clicks the row's first `Button` descendant. Never matches by row text (`LoadWindowView.BindRow` writes only `OrganizationId`/`GameDate`; both row buttons share localized labels). An index `ListSaves()` lacks fails the step, listing the names it returned.
  - `SaveFileManager` taken from `E2ESessionBridge`.
  - Self-provisioning setup step in `Assets/Scripts/Unity/E2E/Steps/E2ELoadSaveStep.cs`: no run-owned save ⇒ run `new_game_to_map.json` then push `SaveGame`.
  - Unavailable named save ⇒ step failure, never a silent new game.

- **Group 4 — save isolation and discard:**
  - `PersistentStorage(string root)` overload in `Assets/Scripts/Unity/Save/PersistentStorage.cs`; **four** call sites switch to `E2ERunContext.StorageRootOrDefault()` — `GameLifetimeScope.cs:36`, `MainMenuLifetimeScope.cs:17`, `ProjectLifetimeScope.cs:14`, `SelectCountryLifetimeScope.cs:58`.
  - `Assets/Scripts/Unity/E2E/E2ERunContext.cs` — reads `.e2e/current_run.json` once per domain load; returns `Application.persistentDataPath` when no run is active.
  - Root: `.e2e/runs/<runId>/persistent/`; teardown and stale-lock recovery both delete it; `RunReport.SavesCreated` / `SavesUsed` recorded per-step.

- **Group 5 — live session:**
  - Services: `Assets/Scripts/Unity/E2E/E2ESessionBridge.cs`, registered via `builder.RegisterEntryPoint<E2ESessionBridge>()` in `GameLifetimeScope`, `MainMenuLifetimeScope`, `SelectCountryLifetimeScope`; publishes on `Start`, clears on `Dispose`.
  - Commands: `src/Game.Commands.Text/CommandExecutor.cs` (extracted) invoked by `Assets/Scripts/Unity/E2E/Steps/E2ECommandStep.cs` against the bridge's `IWriteOnlyCommandAccessor`; `ExecutionResult.Success`/`Message` recorded verbatim.
  - Interaction: `Assets/Scripts/Unity/E2E/E2EInputDriver.cs` — `InputSystem.QueueStateEvent(Mouse.current, new MouseState{...}.WithButton(MouseButton.Left, ...))` + `InputSystem.Update()`, plus `Keyboard.current` / `QueueTextEvent` for `setValue`; hand-rolled `SendEvent(PointerDownEvent/PointerUpEvent)` fallback. For the run's duration **both** `InputSystem.settings.editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView` (the knob that actually decides Editor routing) and `backgroundBehavior = IgnoreFocus` are set, with both prior values recorded in `editor_state.json` and restored at teardown.
  - Addressing: `Assets/Scripts/Unity/E2E/E2EElementResolver.cs` — name → visible label → indexed `ListView` row; `RuntimePanelUtils.ScreenToPanel` round-trip check; failure message lists present names/labels. Traversal goes through `element.hierarchy[i]`, **not** `element[i]`: Step 0 found `ListView` redirects its `contentContainer`, so the content-children walk reports 1 child and zero buttons even with every row realized, while the hierarchy walk reports 65 descendants and 14 buttons.
  - Waits: `Assets/Scripts/Unity/E2E/Steps/E2EWaitStep.cs` over screen / control / `VisualState.Time.CurrentTime` / `VisualState.Time.IsPaused`, bounded by the step budget.

- **Group 6 — default language:**
  - Seeded `settings.json` written into the isolated root by `Assets/Scripts/Editor/E2E/E2ERunIsolation.cs` with the default locale from `LocalizationConfig` and `tutorialsEnabled: false`, camelCase keys. `LocalizationConfig` is a `ScriptableObject` in `GS.Unity.UI` with no fixed path, located via `AssetDatabase.FindAssets("t:LocalizationConfig")`.
  - The developer's own `settings.json` under `Application.persistentDataPath` is never opened; `RunReport.Locale` records what was used.

- **Group 7 — evidence:**
  - `Assets/Scripts/Unity/Common/ScreenCaptureUtil.cs` (new) — `IEnumerator CaptureTo(string)` for the runner and `void CaptureImmediate(string)` for the menu item over one shared encode/write helper; `Assets/Scripts/Editor/Utils/ScreenshotCapture.cs` calls `CaptureImmediate`, keeping its shortcut and Play-mode validate gate.
  - `Assets/Scripts/Unity/E2E/E2EConsoleCollector.cs` — `Application.logMessageReceived`, buffered per step.
  - `Assets/Scripts/Unity/E2E/E2EStateSnapshot.cs` — scene + visible modal, and from `E2ESessionBridge`'s `VisualState`: `PlayerOrganization.OrgId`, `Time.CurrentTime`, `Time.IsPaused`, `SelectedCountry.CountryId`, `SelectedProvince`; `extraState` named-projection table.
  - `capture` step kind; artifacts under `.e2e/runs/<runId>/steps/`; pruning to the newest 20 run folders at run start in `E2ERunWatcher`.

- **Group 8 — step scripts:**
  - `src/Game.E2E/StepScript.cs` (DTOs), `StepScriptValidator.cs` (unknown kind / missing target / bad placeholder → error carrying script name + step index), `ParameterSubstitution.cs` (`{{...}}` from `RunRequest.Inputs`), `StepScriptSerializer.cs` (the one parser both runner and tests use).
  - Committed: `Docs/E2E/flows/new_game_to_map.json`, `Docs/E2E/flows/load_save_to_map.json`. Scratch: `.e2e/scratch/*.json` (gitignored).

- **Group 9 — interactive mode:**
  - `Assets/Scripts/Unity/E2E/E2EInteractiveSession.cs` — polls `.e2e/runs/<runId>/inbox/step_<n>.json`, executes, writes `outbox/step_<n>.json`, leaves the session on the resulting state.
  - `{"kind":"end"}` closes down through the same teardown as a scripted run; idle beyond `idleTimeoutSeconds` (default 120) ends the run as `abandoned`.

- **Group 10 — run report:**
  - `src/Game.E2E/RunReport.cs` + `RunReportRenderer.cs` (markdown) + `RunReportSerializer.cs`. Summary carries: request echo, outcome, `ReachedMap`, steps completed/total, ending step index + reason, failure policy applied, console-error count with the first errors quoted, `SavesCreated`/`SavesUsed`, locale, and the count of `inputPath: "panel"` steps.
  - Per step: index, kind, target, outcome, duration, and relative paths to its screenshot/console/state files. Written to `report.json` + `report.md` incrementally.

- **Group 11 — failures and timeouts:**
  - `src/Game.E2E/StepSequencer.cs` — pure state machine over per-step budgets (default 30 s, per-step `timeoutSeconds` override), the whole-run cap (default 300 s), `failurePolicy`, and `consoleErrors` strict/report. Fully unit-tested without Unity.
  - `Assets/Scripts/Unity/E2E/E2ERunnerHost.cs` drives it, captures evidence at the moment of a timeout, and ends the run on an unhandled exception surfaced by `E2EConsoleCollector`.
  - Watchdog: `E2ERunWatcher` compares the lock's start timestamp on `EditorApplication.update` and force-exits Play mode at cap + 30 s, covering a host that stopped advancing. A hard main-thread hang freezes that check too and is documented as needing a manual Editor kill, after which stale-lock recovery reports the run as `crashed`.

- **Group 12 — cleanup and repeatability:**
  - `Assets/Scripts/Editor/E2E/E2ERunTeardown.cs` on `playModeStateChanged` (every in-process exit): finalize report, delete `persistent/`, restore `editorInputBehaviorInPlayMode`, `backgroundBehavior` and the scene recorded in `editor_state.json`, delete `.e2e/current_run.json`.
  - `E2ERunWatcher`'s `[InitializeOnLoad]` stale-lock recovery covers process death, which `playModeStateChanged` cannot: finalize as `crashed`, delete `persistent/`, restore from `editor_state.json` (written before any state change, so always authoritative), clear the lock.
  - Dirty-scene refusal in `E2ERunWatcher` guarantees restoration can never lose unsaved work.

- **Group 13 — three agents:**
  - `../ClaudeTools/plugins/cc/skills/unity-e2e-run/SKILL.md`; `../CodexTools/plugins/cd/skills/unity-e2e-run/SKILL.md` + `plugin.json` version bump — separate repos, separate commits on their own `main` branches.
  - `.claude/skills/unity-e2e-run/SKILL.md`, `.agents/skills/unity-e2e-run/SKILL.md`, `.cursor/commands/unity-e2e-run.md` (no `.cursor/skills/` wrapper — justified in Approach §10).
  - `src/Game.E2E/E2EProtocol.cs` `Version` constant + mismatch report path.
  - `CLAUDE.md` Configuration Index entry.

## Tests

`src/Game.E2E` is deliberately shaped so everything decision-bearing is pure C# under `dotnet test`. New tests in `src/Game.Tests/` (which gains a `ProjectReference` to `Game.E2E` and `Game.Commands.Text`), xunit, snake_case `[Fact]` names:

- **`E2EStepScriptTests.cs`** — valid script round-trips through `StepScriptSerializer`; unknown step kind fails validation naming the script and zero-based step index; a step missing its required target fails the same way; `{{org}}`/`{{save}}` substitute from `RunRequest.Inputs`; an unresolved placeholder is a validation error, not a literal `{{org}}` sent to the runner; **the two committed `Docs/E2E/flows/*.json` files parse and validate, read from disk through the same serializer the runner uses**, so a broken committed flow fails the suite.
- **`E2EStepSequencerTests.cs`** — `stopOnFirstFailure` skips remaining steps and records the policy; `continueOnFailure` attempts them all; a per-step `timeoutSeconds` override applies to that step only and leaves the default intact for the next; the whole-run cap fires even when every per-step budget is unexhausted, naming the step in progress; `consoleErrors: strict` fails the run on the first error while `report` continues; a step that times out is recorded as a timeout failure with an evidence-capture request emitted.
- **`E2ERunReportTests.cs`** — a report with a mid-sequence failure renders a summary carrying outcome, `ReachedMap`, steps completed/total, ending step index and reason, and the applied failure policy; console errors appear in the summary and not only per-step; every step entry carries relative screenshot/console/state paths; an interrupted run renders as incomplete with the completed steps intact; `RunReportSerializer` round-trips a report without losing per-step evidence paths.
- **`E2EProtocolTests.cs`** — a request whose `protocolVersion` differs produces a `protocol-mismatch` outcome naming both versions; a matching version does not.
- **Moved from `src/Game.WebClient.Tests/` to `src/Game.Tests/`** — `CommandRegistryTests`, `TerminalParserTests`, `ValueCoercionTests`, `CommandExecutorTests`, retargeted to the `GS.Game.Commands.Text` namespace. They move with the code so the extracted project owns its own coverage. `SuggestionEngineTests` and `SuggestionValueResolverTests` stay in `Game.WebClient.Tests` (the suggestion layer stays in the web client) and are updated only for the new `using`.

Not unit-testable here, covered by Step 8's shakedown and the User Steps instead: input injection reaching a real panel, screenshot capture, element resolution against live UXML, org selection via `SelectCountryCommand`, `ListView` row realization, Play-mode lifecycle and editor restoration.

Run with the `dotnet-test` skill: `dotnet test src/GlobalStrategy.Core.sln`.

## Steps

### Agent Steps

- [x] **Step 0 — Input-path spike** — Prove the **primary** path first, since everything depends on it. Write a throwaway Editor script under `.tmp/` that enters Play mode on `MainMenu.unity`, sets `InputSystem.settings.editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView` and `backgroundBehavior = IgnoreFocus`, and injects a device-level click at `btn-play`'s screen point **with the Game view unfocused** — confirm its `.OnClick()` handler fires. Then repeat against a `save-list` row's Load button, which `LoadWindowView.MakeRow` builds with `new Button(Action)` (the `Clickable` manipulator, not `.OnClick()`), and confirm that fires too. Record both results, plus the restored settings, in this plan under a `### Spike result` heading. **Only then** evaluate `com.unity.ui.test-framework`: check whether it has a release compatible with Unity 6000.5.5f1 and whether its simulation API is callable from a non-test assembly; record the answer. Do not add the package — that is User Step 1's call. If the primary path fails for either button, stop and report before writing any runner code.

### Spike result

Run 2026-09-07 against Unity **6000.5.5f1**, driven over MCP with the Game view unfocused throughout. Editor restored afterwards (Map scene reopened and clean, both input settings back to their originals, no play session, `git status` clean).

**Primary device-level path: PASS, for both button kinds.**

| Target | Construction | Injected click result |
|---|---|---|
| `btn-play` (MainMenu) | `.OnClick()` extension (`PointerUpEvent`) | MainMenu → **CountrySelection** |
| `btn-load` (MainMenu) | `.OnClick()` extension | LoadWindow opened (`LoadWindowUI` panel 0x0 → 1920x1080) |
| `save-list` row Load | `new Button(Action)` — `Clickable` manipulator | save loaded → **Map** |

Method: `InputSystem.QueueStateEvent(Mouse.current, MouseState{position}.WithButton(Left, …))` + `InputSystem.Update()` for move → press → release. Panel scale was exactly 1.0 (panel 1920x1080 = `Screen`), and the `screen = panelPoint * scale` with Y-flip formula in §6 produced correct hits every time.

**Prerequisite settings, confirmed by measurement.**

- `InputSettings.editorInputBehaviorInPlayMode` was at its default **`PointersAndKeyboardsRespectGameViewFocus`**, which routes pointer input away from player code when the Game view is unfocused. Setting it to `AllDeviceInputAlwaysGoesToGameView` is what makes the whole path work — `backgroundBehavior` alone would not have. The enum's three values are `PointersAndKeyboardsRespectGameViewFocus`, `AllDevicesRespectGameViewFocus`, `AllDeviceInputAlwaysGoesToGameView`.
- `backgroundBehavior` original value on this machine: `ResetAndDisableNonBackgroundDevices`.
- `InputSystem.QueueStateEvent`, `QueueTextEvent` and `Update()` are all public; `MouseState`/`WithButton` are public. No test assembly required.

**Two findings the plan did not anticipate — both need folding in.**

1. **`Application.runInBackground` is `false`, and it throttles the player loop to a crawl while the Editor is unfocused.** Measured ~97 frames across ~88 s of wall clock; with it set to `true` the same gap produced ~1,150 frames. Every `waitFor` step would otherwise burn its budget waiting for frames that are not running, and the `save-list` rows never realized until frames flowed. This is a **third** setting the runner must handle, alongside the two input settings. `PlayerSettings.runInBackground` is still `false` — the spike set only the runtime `Application.runInBackground`, which resets on play-mode exit.
2. **`ListView` redirects its `contentContainer`, so the element resolver must walk `hierarchy`, not the content children.** Walking `element[i]` from `save-list` reports `childCount == 1` and zero buttons even with all 7 rows realized and laid out; walking `element.hierarchy[i]` reports 65 descendants and 14 buttons. §6's resolver and `E2ESelectRowStep` must use `hierarchy` traversal.

**Review concern 4 confirmed against the live UI.** All 7 rows realized at `fixedItemHeight` 64 under `FixedHeight` virtualization. Every row renders `Illuminati` plus a date (`1880-01-02`, `1881-06-01`, …) — the save name appears nowhere — and every row's two buttons are labelled exactly `Load` and `Delete`. Label matching is therefore unusable on `save-list`, exactly as the indexed-row scheme assumes.

**`com.unity.ui.test-framework`: a compatible release exists — version `6.5.0`**, offered by the registry for this editor ("UIToolkit Test framework for running Edit mode and Play mode tests in Unity"). Whether its simulation API is callable from a non-test assembly is **not** answered: determining it requires installing the package, which is User Step 1's call. Not installed; `Packages/manifest.json` untouched. Given the primary path passed for both button kinds, nothing in the plan depends on this answer.

- [ ] **Step 1 — Extract `src/Game.Commands.Text`** — New netstandard2.1 csproj with the `Release` → `../../Assets/Plugins/Core/` output block per `.claude/rules/unity/plugins.md`; references `Game.Commands` + `Game.Main`; added to `src/GlobalStrategy.Core.sln`. Move `CommandRegistry.cs`, `TerminalParser.cs`, `ValueCoercion.cs`, `CommandExecutor.cs` out of `src/Game.WebClient/Terminal/` into it under namespace `GS.Game.Commands.Text`. Add a `ProjectReference` from `Game.WebClient` and update `using` in `Terminal/Suggestions/*.cs`, `Program.cs`, `Components/Terminal.razor`. Move the four test files to `src/Game.Tests/` and add the `Game.Commands.Text` `ProjectReference` there; fix the remaining `using` in `Game.WebClient.Tests`. Verify: `dotnet test src/GlobalStrategy.Core.sln` green.

- [ ] **Step 2 — `src/Game.E2E` core** — New netstandard2.1 csproj (same Plugins output block; `Newtonsoft.Json` 13.0.3 with `<ExcludeAssets>runtime;native</ExcludeAssets>` copied from `src/Game.Configs/Game.Configs.csproj`; added to the sln) with `E2EProtocol.cs`, `RunRequest.cs`, `StepScript.cs`, `StepScriptValidator.cs`, `StepScriptSerializer.cs`, `ParameterSubstitution.cs`, `StepSequencer.cs`, `RunReport.cs`, `RunReportRenderer.cs`, `RunReportSerializer.cs`. Add the `Game.E2E` `ProjectReference` to `src/Game.Tests`. Verify: build green, and a `Release` build puts **no** `Newtonsoft.Json.dll` into `Assets/Plugins/Core/`.

- [ ] **Step 3 — Flow library + `src/Game.E2E` tests** — Write `Docs/E2E/flows/new_game_to_map.json` and `Docs/E2E/flows/load_save_to_map.json` **first**, per Approach §9, using the `selectOrg` primitive and indexed `selectRow`, with the verified control names (`btn-play`, `btn-start`, `btn-load`, `save-list`). Then implement `E2EStepScriptTests`, `E2EStepSequencerTests`, `E2ERunReportTests`, `E2EProtocolTests` per the Tests section, including the on-disk parse-and-validate test against those two real files. Verify: `dotnet test src/GlobalStrategy.Core.sln` green — and green at the end of every step from here on.

- [ ] **Step 4 — Release build + Unity import** — `/dotnet-build Release` so `Assets/Plugins/Core/` picks up `Game.Commands.Text.dll` and `Game.E2E.dll`; confirm both `.dll`s and their `.meta` files land there and that no `Newtonsoft.Json.dll` appeared. Then `refresh_unity` + `read_console(types=["error"])`.

- [ ] **Step 5 — Isolation plumbing** — Add the `PersistentStorage(string root)` overload (`Assets/Scripts/Unity/Save/PersistentStorage.cs`). Create `Assets/Scripts/Unity/E2E/` with `GS.Unity.E2E.asmdef` (references `GS.Unity.Common`, `GS.Unity.Save`, `GS.Unity.UI`, VContainer `GUID:b0214a6008ed146ff8f122a6a9c2f6cc`, Input System `GUID:75469ad4d38634e559750d17036d5f7c`; template per `.claude/rules/unity/asmdef.md`) and `E2ERunContext.cs`. Switch **all four** scopes — `GameLifetimeScope.cs:36`, `MainMenuLifetimeScope.cs:17`, `ProjectLifetimeScope.cs:14`, `SelectCountryLifetimeScope.cs:58` — to `new PersistentStorage(E2ERunContext.StorageRootOrDefault())`. Add `ScreenCaptureUtil.cs` to `Assets/Scripts/Unity/Common/` with both `IEnumerator CaptureTo(string)` and `void CaptureImmediate(string)` over one shared encode/write helper, and make `Assets/Scripts/Editor/Utils/ScreenshotCapture.cs` call `CaptureImmediate` (add the `GS.Unity.Common` reference to `GS.Editor.Utils.asmdef`, and while editing that file set its `autoReferenced` to `false` per the asmdef rule's Editor-only exception — approved separately; the Release build and `read_console` will surface any assembly that was silently relying on the auto-reference). Set `PlayerSettings.runInBackground = true` (`ProjectSettings/ProjectSettings.asset`) — owner-approved in Step 0; without it the player loop crawls while the Editor is unfocused and every wait step times out. `refresh_unity` + `read_console(types=["error"])`.

- [ ] **Step 6 — Runtime driver** — Under `Assets/Scripts/Unity/E2E/`: `E2ESessionBridge.cs`, `E2ERunnerBootstrap.cs` (`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, no-op when no run is active), `E2ERunnerHost.cs`, `E2EElementResolver.cs`, `E2EInputDriver.cs` (device path + hand-rolled panel fallback), `E2EConsoleCollector.cs`, `E2EStateSnapshot.cs`, `E2EInteractiveSession.cs`, and `Steps/{E2EClickStep,E2ESelectOrgStep,E2ESelectRowStep,E2ESetValueStep,E2ECommandStep,E2EWaitStep,E2ECaptureStep,E2ELoadSaveStep}.cs`. Register the bridge with one `builder.RegisterEntryPoint<E2ESessionBridge>()` line in `GameLifetimeScope`, `MainMenuLifetimeScope` and `SelectCountryLifetimeScope`, and add the `GS.Unity.E2E` reference to `Assets/Scripts/Unity/DI/GS.Unity.DI.asmdef` — the dependency runs DI → E2E only; `GS.Unity.E2E` must never reference `GS.Unity.DI`. No `FindObjectOfType`/`FindObjectsByType` anywhere. `refresh_unity` + `read_console(types=["error"])`.

- [ ] **Step 7 — Editor control** — `Assets/Scripts/Editor/E2E/GS.Editor.E2E.asmdef` (`includePlatforms: ["Editor"]`, `autoReferenced: false`, references `GS.Unity.E2E`, `GS.Unity.Common` and `GS.Unity.UI` `GUID:31616c5c35fcc3c418ca03ade2c0cfb9` — needed for `LocalizationConfig`) with: `E2ERunWatcher.cs` (stale-lock recovery in the `[InitializeOnLoad]` static ctor, then polling, accept guards, protocol check, pruning, scene open, Play-mode enter, `AssemblyReloadEvents.beforeAssemblyReload` handling, and the cap + 30 s watchdog); `E2ERunIsolation.cs` (isolated root, seeded camelCase `settings.json` with default locale + `tutorialsEnabled: false`, `LocalizationConfig` located via `AssetDatabase.FindAssets("t:LocalizationConfig")`); `E2ERunTeardown.cs` (`playModeStateChanged`: finalize, delete `persistent/`, restore scene + **both** `editorInputBehaviorInPlayMode` and `backgroundBehavior`, clear the lock). `editor_state.json` is written before any state change. Add `.e2e/` to `.gitignore`. `refresh_unity` + `read_console(types=["error"])`.

- [ ] **Step 8 — Flow library shakedown** — Execute both committed flows end to end against the real screens and correct whatever they reject: wait conditions, settle frames, `selectOrg` HQ resolution, `selectRow` index resolution and `ScrollToItem` behaviour. Re-run `dotnet test src/GlobalStrategy.Core.sln` after any flow edit so the on-disk validation test stays green.

- [ ] **Step 9 — Shared skills (two separate sibling-repo commits)** — In `../ClaudeTools`: add `plugins/cc/skills/unity-e2e-run/SKILL.md` (frontmatter `name`, `description`), commit on its `main`. In `../CodexTools`: add `plugins/cd/skills/unity-e2e-run/SKILL.md` and bump `plugins/cd/.codex-plugin/plugin.json` `version` to `0.3.0`, commit on its `main`. These are two commits in two repos, separate from this repo's commit. Content is project-independent per Approach §10 — including the no-source-edits-during-a-run rule and the honest hard-hang limit — with no paths, screens, orgs or command names from this game.

- [ ] **Step 10 — Project wrappers and docs** — `.claude/skills/unity-e2e-run/SKILL.md` (delegates to `cc:unity-e2e-run`, `dotnet-build` wrapper pattern), `.agents/skills/unity-e2e-run/SKILL.md` (delegates to `cd:unity-e2e-run`), `.cursor/commands/unity-e2e-run.md` (points at the Claude skill; no `.cursor/skills/` wrapper). Rewrite the "Do not self-test in Play mode" section of `.claude/rules/unity/mcp_usage.md` per Approach §11. Update `.claude/skills/add-terminal-command/SKILL.md` for the `src/Game.Commands.Text/` move and its second consumer. **Correct the stale Unity version** `6000.4.1f1` → `6000.5.5f1` in `CLAUDE.md` and `.claude/rules/unity/uitoolkit.md`. Add the `CLAUDE.md` Configuration Index entry.

- [ ] **Step 11 — Final build and self-run** — `/dotnet-build Release`; `dotnet test src/GlobalStrategy.Core.sln` green; `refresh_unity` + `read_console(types=["error"])`. Grep-check that no `new PersistentStorage()` without a root argument remains anywhere under `Assets/`. Then drive the feature with itself: drop a `RunRequest` for `new_game_to_map.json` into `.e2e/requests/`, wait for `report.json`, and verify — the run reached the map under two minutes; every step has a screenshot, console log and state snapshot; the report names the org used; `.e2e/runs/<runId>/persistent/` is gone; the pre-run scene is reopened and not dirty; both input settings are back to their recorded values; no play session is left running. Repeat immediately with no manual cleanup and confirm an identical sequence of step outcomes. Then run `load_save_to_map.json` with no save present and confirm it self-provisions one. Then run a request while Play mode is already active and confirm the refusal report. Then simulate a crash: while a run is active, delete nothing but force-quit the Editor, reopen, and confirm stale-lock recovery finalizes the run as `crashed` and restores everything. Confirm `git status` shows no `.e2e/` entries.

### User Steps

### 1. Decide whether you want `com.unity.ui.test-framework` at all (optional)

Step 0 reports whether the package has a Unity 6000.5.5f1-compatible release and whether its API is callable outside a test assembly. Nothing in the plan needs it — the primary path is device-level Input System injection and the fallback is a hand-rolled `SendEvent` pair — so this is a genuinely optional call, not a gate. Say yes only if you want the package's query/settling helpers as a permanent project dependency; saying nothing means it is never added.

### 2. Watch one full run in the Editor

With the Editor open and the Game view visible, let the agent start a `new_game_to_map.json` run and watch it: confirm the clicks land on the real buttons (not just that the report says so), that the org gets selected and `btn-start` enables, that no tutorial overlay appears, that the language is the default one, and that the editor returns to the scene you had open. This is the only check that the *device-level* input path genuinely behaves like a human's — the automated verification can only confirm the game reacted, not that it reacted for the right reason.

### 3. Confirm your own saves are untouched

Before and after a run, check `Application.persistentDataPath`'s `Saves/` folder and `settings.json` (Windows: `%USERPROFILE%\AppData\LocalLow\<company>\<product>\`). Neither should change in any way — this is the one acceptance criterion whose failure would cost you real data, and the fourth `PersistentStorage` site in `SelectCountryLifetimeScope` (on the new-game route, and easy to miss) is exactly the kind of thing worth verifying by hand once rather than trusting the isolation code.

### 4. Press Stop mid-run

Start a run and hit Stop in the Editor partway through. Confirm the report is finalized as interrupted with the completed steps' evidence intact, the isolated save folder is gone, your scene is restored, both input settings are back, and the next run starts cleanly with no manual cleanup.

## Constitution Check

Checked against `Docs/Constitution.md`. **Two genuine tensions and one governance amendment, each with a resolution; no principle is quietly designed around.**

1. **"VContainer is the sole DI mechanism … no static mutable singletons outside the container."** `E2ERunnerHost` is created by `[RuntimeInitializeOnLoadMethod]` before any scene (and therefore any `LifetimeScope`) exists, and must survive scene loads — `ProjectLifetimeScope` is placed per-scene in all four scenes, so there is no cross-scene container to register it in. An earlier draft resolved this as "the host resolves services from the active `LifetimeScope`", but that has no mechanism that is both available and permitted: `LifetimeScope.Find<T>()` is constrained to a concrete `LifetimeScope` subtype; those subtypes live in `GS.Unity.DI`, which Steps 5–6 make reference `GS.Unity.E2E`, so a reverse reference would be a hard assembly cycle; `FindObjectsByType<LifetimeScope>()` is forbidden by this very principle; and with two root scopes per scene "the active scope" is ambiguous regardless. **Resolution (owner-approved, reversing the earlier draft on this evidence):** each of the three game scopes registers `builder.RegisterEntryPoint<E2ESessionBridge>()` — one line each — and `E2ESessionBridge` (constructor-injected with `VisualState`, `IWriteOnlyCommandAccessor`, `SaveFileManager`, `ILocalization`) publishes itself to the host on `Start` and clears itself on `Dispose`. Every game service the runner touches is therefore resolved through the container by ordinary VContainer constructor injection, the host holds nothing after a scene unloads, and no `FindObjectOfType`/`FindObjectsByType` appears anywhere. The host itself remains outside the container, which is unavoidable given it predates every scope; it is a driver, not a game service. `E2ERunContext` holds one immutable value (the storage root) read from `.e2e/current_run.json` once per domain load — not a mutable singleton, and following the precedent already set by `GS.Unity.Common.SceneTransitionArgs`.

2. **"ECS for all game logic, living in `src/`."** The step runner is tooling, not game logic — it holds no game state and encodes no domain rules — so on a literal reading there is no conflict. But the spirit ("nothing decision-bearing hides in a MonoBehaviour") does apply. **Resolution:** all decision-bearing logic — script validation, parameter substitution, failure policy, timeout budgets, report rendering, protocol versioning, and the JSON contract for every artifact — lives in `src/Game.E2E` under `dotnet test`, and the Unity assemblies contain only Unity-specific effects (input injection, element resolution, capture, Play-mode lifecycle). This mirrors how `src/Game.Evals` was handled for the bot harness.

3. **Amendment required to `.claude/rules/unity/mcp_usage.md`.** Its "Do not self-test in Play mode" section forbids exactly what this feature enables. This is a rule file rather than a Constitution principle, so it is not a Constitution violation — but it is a governance change and is called out here rather than buried in Step 10. The spec settled that agent-initiated play sessions are allowed for verification and debugging with no per-run confirmation; the rewrite keeps both surrounding cautions intact (no ad-hoc hand-dispatched input events; never interrupt an active human session).

Aligned with no tension:

- *URP only.* No rendering change. `ScreenCapture.CaptureScreenshotAsTexture` is pipeline-agnostic.
- *UI Toolkit only.* No Canvas/UGUI is added. The runner reads existing UI Toolkit panels; the `EventSystem` objects it relies on already exist in all four scenes.
- *One `.asmdef` per feature folder under `Assets/Scripts/`.* Two new feature folders, two new asmdefs: `Assets/Scripts/Unity/E2E/GS.Unity.E2E.asmdef` and `Assets/Scripts/Editor/E2E/GS.Editor.E2E.asmdef` (Editor-only, `autoReferenced: false` per the rule's exception). The new `GS.Unity.DI` → `GS.Unity.E2E` reference is one-directional and introduces no cycle.
- *Plan before implement / spec before plan.* Spec 26_09_07_12 precedes this plan; both live under `Docs/Specs/26_09_07_12_unity-e2e-steps/`.
- *File Organisation.* Plan filed under the dated spec folder. `Docs/E2E/flows/` is a new committed home for machine-read flow JSON, following the `Docs/BotFeatures/<id>/eval_config.json` precedent; it does not collide with the dated `Docs/Specs` convention.
- *C# code style.* Tabs, `_`-prefixed privates, braces always, no redundant access modifiers, `[SerializeField]` inline — throughout all new `src/` and `Assets/` code.

One pre-existing deviation noticed in passing and **not** introduced by this plan: `Assets/Scripts/Editor/Utils/GS.Editor.Utils.asmdef` has `autoReferenced: true` despite being Editor-only, which the asmdef rule's exception says should be `false`. Step 5 already edits that assembly's references, so **the repo owner approved correcting it there** rather than deferring it to a separate change.

Use the implement skill to start working on the plan or request changes.
