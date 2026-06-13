# Spec: Dev Map Auto-Load

## Feature Intent

As a developer, I want the Map scene to automatically load the latest save when entered directly from the Unity Editor (without navigating through MainMenu), so that I can iterate on Map-scene features without repeating the MainMenu → Resume flow on every Play press.

## Acceptance Criteria

- **Given** the Map scene is opened directly in the Unity Editor (Play pressed from the Map scene) **and** `SceneTransitionArgs.SaveNameToLoad` is null **and** `SceneTransitionArgs.InitialPlayerCountry` is null **and** at least one save file exists **When** `GameLoopRunner.Start()` runs **Then** the most recent save (as returned by `SaveFileManager.GetLastSave()`) is loaded automatically, equivalent to calling `_logic.LoadState(latestSave.SaveName)`.

- **Given** the Map scene is opened directly in the Unity Editor **and** no save files exist **When** `GameLoopRunner.Start()` runs **Then** the game starts in its default new-game state (no load is attempted), identical to current behaviour.

- **Given** the Map scene is reached via the normal MainMenu → Resume flow (i.e. `SceneTransitionArgs.SaveNameToLoad` is set) **When** `GameLoopRunner.Start()` runs **Then** the explicitly requested save is loaded and the auto-load logic is not triggered.

- **Given** the Map scene is reached via MainMenu → CountrySelection → Map (new game flow, i.e. `SceneTransitionArgs.InitialPlayerCountry` is set) **When** `GameLoopRunner.Start()` runs **Then** a new game is started as normal and auto-load is not triggered.

- **Given** the auto-load path executes **When** the latest save is loaded **Then** a log message is written to the Unity console identifying the save name that was auto-loaded, to make the behaviour discoverable during development.

- **Given** the feature ships in a production build **Then** there is no behavioural difference from the current code — auto-load only activates when both `SaveNameToLoad` and `InitialPlayerCountry` are null, which cannot occur through normal user navigation.

## Out of Scope

- Any UI indication to the player that an auto-load occurred (this is a dev tool, not a user-facing flow).
- Automatic scene-switching — the developer must already have the Map scene open; this feature does not load the Map scene automatically from within other scenes.
- Handling corrupted saves with a special fallback: `SaveFileManager.GetLastSave()` already silently skips corrupted files during `ListSaves()`; no additional recovery logic is needed here.
- A toggle or Editor preference to disable auto-load — the null-check on `SceneTransitionArgs` fields is the natural "off switch" for production and normal editor navigation.

## Ambiguities

- [NEEDS CLARIFICATION: Should the `#if UNITY_EDITOR` preprocessor guard be used to make the auto-load strictly compile-out of production builds, or is the runtime null-check on `SceneTransitionArgs` sufficient isolation? The runtime guard is simpler but leaves dead code in builds; the preprocessor guard is more explicit but adds conditional compilation.]
