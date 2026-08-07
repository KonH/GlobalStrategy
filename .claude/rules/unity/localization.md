---
paths:
  - "Assets/Localization/**"
  - "Assets/Scripts/Unity/UI/**"
---

# Localization

## Architecture

Two layers:
- **`ILocalization`** (Unity side, `GS.Unity.UI`) — `CustomLocalization` wraps Unity ScriptableObject assets. Project-level singleton, persists across scenes. Use `_loc.Get(key)` and `_loc.SetLocale(locale)`.
- **ECS `Locale` component** (game side) — the source of truth for the running game. `VisualStateConverter.UpdateLocale()` syncs ECS → `VisualState.Locale`. In pre-game menus, `StaticGameLogic` does the same directly.

## Pre-game Menus (StaticGameLogic)

Pre-game scenes (MainMenu, SelectCountry) use `StaticGameLogic` instead of full `GameLogic`:
- Holds its own `CommandAccessor` + `VisualState`
- On each `Tick()` (via `StaticGameLoopRunner : ITickable`): reads `ChangeLocaleCommand` queue, calls `VisualState.Locale.Set(locale)`, clears the buffer
- Initialized with `ILocalization.CurrentLocale` so it starts at the correct locale (persisted across scenes)
- Registered in `MainMenuLifetimeScope`; commands and VisualState resolved from it

## UI Localization Pattern

Each document that shows text:
1. Injects `VisualState _state` and `ILocalization _loc`
2. In `OnEnable`: `_state.Locale.PropertyChanged += HandleLocaleChanged`
3. In `OnDisable`: unsubscribes
4. Handler: call `_loc.SetLocale(_state.Locale.Locale)` first, then `RefreshTexts()`
5. In `Start()`: call `RefreshTexts()` directly — do NOT rely on PropertyChanged for initial sync, it fires in `StaticGameLogic`/`GameLogic` constructor before MonoBehaviour subscriptions

Only one document per scene should call `_loc.SetLocale()` (the always-visible one, e.g. `MainMenuDocument`, `HUDDocument`). Other documents only call `RefreshTexts()` in their handler.

## Locale Key Naming

All keys are in `Assets/Localization/en.asset` and `ru.asset`.

Namespacing convention:
- `menu.*` — main menu buttons/labels
- `select_country.*` — country selection UI
- `load.*` — load game window
- `settings.*` — settings window
- `game_menu.*` — in-game pause menu
- `hud.*` — HUD elements
- `country_name.{CountryId}` — country names (163 countries)
- `resource.{id}.name/description` — resource names
- `effect.{id}.name/description` — effect names

## Card/Action Description Text (`action.*.desc`, `effect.*.desc`)

Keep `desc` values short, practical, one plain sentence — e.g. `Mark new country as friend.`, `We're not friends anymore.` Do **not** write flavor/narrative text (no "A pointed dismissal, delivered through official channels..."). This applies even to plans/specs that propose placeholder description text — treat any such text as non-binding and write the short practical form instead when implementing.

## Adding New Locale Keys

Do not add the same English text to `ru.asset` as a "placeholder Russian
translation" — that pattern is deprecated. Use the `localization` skill,
which spawns a lightweight Haiku subagent to produce a real Russian
translation for every new key in the same change that introduces it.

## Click Blocking for Modal Dialogs

`EventSystem.IsPointerOverGameObject()` does not reliably detect UI Toolkit panels with the new Input System (Unity 6). Use `ModalState` instead:

- `GS.Unity.Common.ModalState` — injected via VContainer (`builder.Register<ModalState>(Lifetime.Singleton)` in `GameLifetimeScope`/`SelectCountryLifetimeScope`, scoped to the scene). A modal calls `Lock(this)` on open and `Unlock(this)` on close (owner tracked via weak reference, so a forgotten `Unlock` can't pin the lock open forever); `IsLocked()` returns true while any owner is still alive
- `GameMenuDocument.Show()` calls `_modalState.Lock(this)`; `Hide()` calls `_modalState.Unlock(this)`
- `MapClickHandler.Update()` returns early when `_modalState.IsLocked()` is true
- `GS.Unity.Map.asmdef` must reference `GS.Unity.Common` (GUID `7e5a37e68b84aeb48bf5de2cbe39a94e`)
