# Plan: Dev Map Auto-Load

## Spec

As a developer, I want the Map scene to automatically load the latest save when entered directly from the Unity Editor (without navigating through MainMenu), so I can iterate on Map-scene features without repeating the MainMenu → Resume flow on every Play press. Auto-load triggers only when both `SceneTransitionArgs.SaveNameToLoad` and `SceneTransitionArgs.InitialPlayerCountry` are null and at least one save exists; otherwise the game behaves exactly as today. A console log identifies the save name when auto-load runs. No `#if UNITY_EDITOR` guard — the runtime null-check is the sufficient isolation.

## Goal

Inject `SaveFileManager` into `GameLoopRunner` and add a fallback auto-load branch in `Start()` that fires only when no explicit transition args are set.

## Approach

Add `SaveFileManager` as a constructor parameter to `GameLoopRunner` — VContainer already has it registered as a singleton, so no scope changes are needed. In `Start()`, after the existing `SaveNameToLoad` branch, add an `else if` that checks `InitialPlayerCountry` is also null, calls `SaveFileManager.GetLastSave()`, and loads the result if non-null. A `Debug.Log` call records the auto-loaded save name.

## Steps

### Agent Steps

- [x] **Step 0 — Add Game.Main asmdef reference** — Open `Assets/Scripts/Unity/DI/GS.Unity.DI.asmdef` and add `"GUID:c07e33f0394f63c4cb1851b66e1c137e"` to the `references` array (this is `Game.Main.dll`). Call `refresh_unity`, then `read_console(types=["error"])` to confirm it imports cleanly before touching any `.cs` files.

- [x] **Step 1 — Inject SaveFileManager into GameLoopRunner** — Add `SaveFileManager saveFileManager` as a third constructor parameter in `GameLoopRunner`. Store it in a `readonly` field `_saveFileManager`.

- [x] **Step 2 — Add auto-load branch in Start()** — After the existing `if (saveName != null)` block, add:
  ```
  else if (SceneTransitionArgs.InitialPlayerCountry == null) {
      var latest = _saveFileManager.GetLastSave();
      if (latest != null) {
          Debug.Log($"[DevAutoLoad] No transition args set — auto-loading save: {latest.SaveName}");
          _logic.LoadState(latest.SaveName);
      }
  }
  ```
  This leaves the `InitialPlayerCountry` (new-game) path untouched and adds no behaviour in production navigation flows.

- [x] **Step 3 — Refresh Unity and verify compilation** — Call `refresh_unity`, then `read_console(types=["error"])` to confirm no compile errors. (Step 0 already has its own refresh+console cycle for the asmdef change; this step covers the `.cs` edit.)

### User Steps

None required — no scene wiring, no Inspector changes, no new assets.

## Constitution Check

No conflicts found — plan aligns with all principles.

- Single file changed (`GameLoopRunner.cs`), pure C# entry-point class, no MonoBehaviour.
- `SaveFileManager` injected via constructor, consistent with VContainer rules for `IStartable`/`ITickable` classes.
- No `FindObjectOfType`, no new static singletons, no `new` for singleton services.
- No `src/` changes needed; logic stays in the Unity-side DI layer where it belongs.

Use /implement to start working on the plan or request changes.
