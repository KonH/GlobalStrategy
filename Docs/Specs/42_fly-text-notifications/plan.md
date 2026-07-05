# Plan: Fly Text Notifications

## Spec

As a player, brief non-blocking floating text notifications should appear when the game confirms an outcome (e.g. a manual save completing), giving lightweight feedback without requiring dismissal or blocking further interaction.

Acceptance criteria:
- Notification text (resolved via `ILocalization.Get(key)` + `string.Format`) renders on a dedicated top-most UI layer — its own `PanelSettings`/`UIDocument`, sort order above the existing Modal layer — front-most in main menu, HUD, and game menu contexts.
- Entrance: scale-up from smaller-than-target to full size (~0.2s).
- Hold at full size/opacity (~1.5s).
- Exit: simultaneous move-down + scale-down + fade-to-zero-alpha (~0.5s), then removed from the visual tree. Total ~2.2s; these are tunable constants, not contracts.
- Fully click-through: root element and all children get `PickingMode.Ignore` applied recursively (not automatically recursive in this Unity version).
- Each scene's lifetime scope (`MainMenuLifetimeScope`, `GameLifetimeScope`) registers and owns its own instance of the notification service behind one shared interface; no cross-scene persistence — an in-flight notification is discarded on scene unload.
- Only one notification visible at a time; further requests queue and play strictly after the current one's full animation (entrance+hold+exit) completes.
- First caller: manual save confirmation from the game menu, using a save-confirmation localization key (e.g. `game_menu.save.confirmation`). Per plan review, this is driven by an actual success/failure signal from `GameLogic` (new `VisualState.SaveResult`), not fired optimistically before the save runs — a failed save shows an error fly text (`game_menu.save.error`, exception type name only) instead of a false "saved" confirmation.
- Locale change mid-notification does not re-localize the currently visible notification (already-resolved text is kept); only later-resolved notifications use the new locale. Must not crash or throw.

Out of scope: the save button/logic itself, other notification types (toasts, history, click-to-dismiss), sound/haptics, non-save trigger sites, localization authoring workflow, cross-scene persistence, stacking multiple visible notifications.

## Approach

### New UI layer

Investigation finding (verified against scene YAML, not just docs): the documented one-`PanelSettings`-per-z-layer model was never actually completed. Every UI surface in every scene — `GameHUD`, `GameMenuUI`, `SettingsWindowUI`, `MainMenuUI`, `LoadWindowUI` — is wired to the same `Assets/UI/HUD/HUDPanelSettings.asset` (guid `a52ac28cceb58ba4db172389975ccca7`) with `UIDocument.m_SortingOrder: 0`. `Assets/UI/Overlay/OverlayPanelSettings.asset` exists on disk but is referenced by zero scenes — it's dead. There is no `ModalPanelSettings.asset` at all. So "one PanelSettings per layer" is aspirational; the real, load-bearing pattern already proven throughout this project is: **one shared PanelSettings, layering controlled by `UIDocument.sortingOrder` among documents on that panel.**

Revised decision: **do not create a new PanelSettings asset.** Attach FlyText's `UIDocument` to the existing `HUDPanelSettings.asset`, the same one every other UI surface already uses, and rely on `sortingOrder` to guarantee it draws on top. This reuses a mechanism this codebase has already exercised (documents sharing a panel), instead of introducing a second Panel/render-target whose cross-panel compositing order has never been used or verified here.

To keep the sorting order correct without per-scene manual tuning (and without `FindObjectsOfType`, which the constitution forbids), `FlyTextNotifierDocument` enforces its own sorting order in code:

```csharp
public const int TopMostSortingOrder = 1000;

void Awake() {
    GetComponent<UIDocument>().sortingOrder = TopMostSortingOrder;
    ...
}
```

`1000` is chosen as a documented ceiling far above the `0` every other current document uses — self-enforcing every time the component initializes, so a future scene wiring the GameObject in the Inspector can't accidentally leave it at the default and end up behind other content. If a future UI surface legitimately needs to render above FlyText, it must use a higher constant and that decision gets made explicitly in code, not by leaving values at scene-authoring discretion.

`Assets/UI/FlyText/` still gets its own UXML/USS (layer content, not a layer settings asset):
- `FlyText.uxml` — a single root container (e.g. `fly-text-root`) holding one `Label`/text container that the service shows/hides/repositions; no buttons, no interactive controls.
- `FlyText.uss` — imports `Assets/UI/Shared/SharedStyles.uss` first, then layer-local rules (typography reuses `.gs-content`/`.gs-header` style tokens rather than redefining color/font).

A `FlyTextUI` GameObject (`UIDocument` referencing `HUDPanelSettings.asset` + `FlyText.uxml`, plus `FlyTextNotifierDocument`) must be added to each scene that can show notifications: `MainMenu.unity` (driven by `MainMenuLifetimeScope`) and `Map.unity` (driven by `GameLifetimeScope`). Since Unity Editor is MCP-connected for this project, this is done via `manage_gameobject`/`manage_components` as an Agent Step, not a manual User Step — see Steps below.

Correction from plan review: `CountrySelection.unity` is actually driven by its own `SelectCountryLifetimeScope` (verified by reading the scene YAML and `Assets/Scripts/Unity/DI/SelectCountryLifetimeScope.cs`), not `MainMenuLifetimeScope` as first assumed. Since the spec's only caller lives in the Map/game-menu flow and `CountrySelection.unity` is out of scope for any trigger, it is left unwired for this pass — wiring a `FlyTextUI` instance into that scene without also registering `IFlyTextNotifier` in `SelectCountryLifetimeScope` would leave the MonoBehaviour un-injected (`_loc` null) and dead. If country-selection screens need fly text later, that requires its own registration step in `SelectCountryLifetimeScope`, added explicitly.

### Documentation update

`.claude/rules/unity/uitoolkit.md`'s "Layer Model" section currently documents three separate PanelSettings assets (HUD/Overlay/Modal) with distinct sort orders as the architecture. This plan corrects that section to describe what's actually true and now further reinforced by this feature: only `HUDPanelSettings.asset` is wired into any scene; `OverlayPanelSettings.asset` is unused dead weight; layering between UI surfaces sharing that one panel is controlled via `UIDocument.sortingOrder`, and `FlyTextNotifierDocument.TopMostSortingOrder` (1000) is the current ceiling value. This doc update is an Agent Step, included in this plan since it directly documents a fact this feature's implementation depends on and would otherwise leave the rules file actively misleading.

### Assembly placement

The notifier is UI-Toolkit-specific (owns a `UIDocument`, builds/animates a `VisualElement`) and depends on `ILocalization`, which already lives in `GS.Unity.UI`. Per the one-`.asmdef`-per-feature-folder rule, a brand-new folder would need its own asmdef that in turn references `GS.Unity.UI` for `ILocalization` — an extra assembly with no independent reuse value, since nothing outside UI-layer code will ever consume `IFlyTextNotifier` directly except other UI documents (`GameMenuDocument` is itself in `GS.Unity.UI`). Decision: **implement inside `Assets/Scripts/Unity/UI/` (assembly `GS.Unity.UI`)**, alongside `ILocalization`/`CustomLocalization` and the other `XxxDocument` classes. No new asmdef is needed; `GS.Unity.UI` already references VContainer (`GUID:b0214a6008ed146ff8f122a6a9c2f6cc`) and the Input System/Common assemblies it needs are already wired.

### Interface and service

```
GS.Unity.UI/IFlyTextNotifier.cs
    public interface IFlyTextNotifier {
        void Notify(string localizationKey, params object[] args);
    }

GS.Unity.UI/FlyTextNotifierDocument.cs   (MonoBehaviour, [RequireComponent(typeof(UIDocument))])
    - [Inject] Construct(ILocalization loc)
    - Implements IFlyTextNotifier and VContainer.Unity.ITickable
    - Owns: Queue<PendingNotification> (already-resolved-and-formatted string), current-phase state machine
    - Awake(): cache UIDocument via GetComponent<UIDocument>() only, and set sortingOrder — matches every other XxxDocument in this codebase (GameMenuDocument, LoadWindowDocument, SettingsWindowDocument, MainMenuDocument, SelectOrgDocument all defer rootVisualElement access to Start()).
    - Start(): query root VisualElement + Label from _doc.rootVisualElement; hide root (display: none) here — NOT in Awake(). Correction from plan review: UIDocument populates rootVisualElement in its own OnEnable(), which runs after every component's Awake() in the scene has already executed. Querying it in Awake() would hit a null root and throw on scene load; every existing XxxDocument in this codebase avoids this by deferring to Start(), and FlyTextNotifierDocument must follow the same pattern.
    - Notify(key, args): resolves text immediately via _loc.Get(key) + string.Format (per spec: locale changes later must not affect this already-resolved string) and enqueues it — resolution happens at call time, not at display time, satisfying the "keeps its already-resolved text" requirement trivially (nothing to re-resolve later).
    - Tick(deltaTime supplied by VContainer's ITickable / driven off Time.deltaTime since ITickable does not pass dt directly — see Timing note below): advances an elapsed-time counter through Idle -> Entrance -> Hold -> Exit -> Idle(next item) states, applying style changes each tick.
```

`Notify` is registered per-scope behind `IFlyTextNotifier`:
```csharp
// GameLifetimeScope.Configure and MainMenuLifetimeScope.Configure, both:
builder.RegisterComponentInHierarchy<FlyTextNotifierDocument>();
builder.Register<IFlyTextNotifier>(c => c.Resolve<FlyTextNotifierDocument>(), Lifetime.Singleton);
```
`RegisterComponentInHierarchy` already finds the single scene instance and VContainer resolves the same instance for both the concrete type and the interface registration — no `new`, no `FindObjectOfType`, no static singleton, satisfying the DI constitution rule. Because each scope's `Configure` runs independently per scene load, each scene gets its own instance with no persisted state — satisfying the "no cross-scene persistence" criterion for free (the old scene's `FlyTextNotifierDocument` and its queue are destroyed with the scene).

Callers (e.g. `GameMenuDocument`) inject `IFlyTextNotifier` via the existing `[Inject] void Construct(...)` method-injection pattern and call `_flyText.Notify("game_menu.save.confirmation")`.

### Animation technique

Decision: **direct per-frame style manipulation inside `Tick()`**, not `experimental.animation.Start(...)`. Reasons:
- The exit phase animates three properties simultaneously (translate, scale, opacity) with a single shared elapsed-time driver and a single completion callback (remove from tree, dequeue next) — a hand-rolled elapsed-time state machine makes that one code path, whereas `experimental.animation` would need three independently-started animations plus a way to know when the slowest one finishes.
- `experimental.animation` easing/callback API is less predictable across Unity point releases (marked experimental); direct style writes are simple `float` lerps the project already has precedent for conceptually (though not code-shared) in `AnimatableInt`/`AnimatableDouble` barrier lerps — same "accumulate elapsed time, compute normalized t, lerp, write" shape, just against `VisualElement.style` instead of a numeric field.
- Only one element is ever animating at a time (single-notification-at-a-time queue), so there is no need for a reusable/parallel tween pool.

`Tick()` implementation sketch:
```csharp
void Tick() {
    float dt = Time.deltaTime;
    switch (_phase) {
        case Phase.Idle:
            if (_queue.Count > 0) { StartEntrance(_queue.Dequeue()); }
            break;
        case Phase.Entrance:
            _elapsed += dt;
            float t = Mathf.Clamp01(_elapsed / EntranceDuration);
            float scale = Mathf.Lerp(EntranceStartScale, 1f, t);
            _root.style.scale = new Scale(new Vector3(scale, scale, 1f));
            if (t >= 1f) { _phase = Phase.Hold; _elapsed = 0f; }
            break;
        case Phase.Hold:
            _elapsed += dt;
            if (_elapsed >= HoldDuration) { _phase = Phase.Exit; _elapsed = 0f; }
            break;
        case Phase.Exit:
            _elapsed += dt;
            float et = Mathf.Clamp01(_elapsed / ExitDuration);
            _root.style.translate = new Translate(0, Mathf.Lerp(0, ExitMoveDownPx, et), 0);
            float exitScale = Mathf.Lerp(1f, ExitEndScale, et);
            _root.style.scale = new Scale(new Vector3(exitScale, exitScale, 1f));
            _root.style.opacity = Mathf.Lerp(1f, 0f, et);
            if (et >= 1f) { HideAndReset(); _phase = Phase.Idle; }
            break;
    }
}
```
`ITickable.Tick()` has no `deltaTime` parameter in VContainer.Unity — it is called once per `Update()` from the VContainer-managed player loop, so reading `UnityEngine.Time.deltaTime` inside `Tick()` is the correct and only way to get per-frame delta (consistent with how other `MonoBehaviour.Update()`-based views in this codebase read `Time.deltaTime`/`Time.time` directly, e.g. `game_loop_integration.md`'s coroutine timeout pattern).

Register via `builder.RegisterEntryPoint<FlyTextNotifierDocument>()`... — but `FlyTextNotifierDocument` is also a `MonoBehaviour` resolved via `RegisterComponentInHierarchy`. VContainer supports both: `RegisterComponentInHierarchy<T>()` finds the scene instance, and if `T` implements `ITickable` it must additionally be registered as an entry point so VContainer's dispatcher calls `Tick()`. Concretely:
```csharp
builder.RegisterComponentInHierarchy<FlyTextNotifierDocument>().As<IFlyTextNotifier, ITickable>();
```
(VContainer's fluent `.As<...>()` on a component registration exposes the same instance under multiple contracts — avoids double-resolving/double-instancing that a separate `RegisterEntryPoint` call would otherwise not fit, since the entry point must be the *same* component instance already living in the scene, not a container-constructed one.)

### Screen position

Spec does not pin an exact screen position. Decision: **top-center of the screen**, anchored via USS (`position: absolute; top: 5%; left: 50%; translate: -50% 0;` as the resting position, with the *animated* `translate` in `Tick()` added on top for the exit move-down — apply as a combined value each frame, e.g. compute the resting `-50%` X offset once in USS and only touch the Y `translate` component procedurally, or resolve both offsets in C# using `Length.Percent` so a single style write does not clobber the centering). This avoids any dependency on a trigger element's `worldBound`/`GeometryChangedEvent`, unlike the tooltip-positioning pattern — fly text has no anchor element, so that gotcha does not apply here. Flagging this as a plan-level implementation decision, not a spec violation, per the task brief.

### Click-through

After building/showing the root each time (`Awake` and whenever swapping in the resolved text does not recreate the tree — the element is built once and reused, text/visibility toggled), recursively apply `PickingMode.Ignore`:
```csharp
static void SetPickingIgnoreRecursive(VisualElement element) {
    element.pickingMode = PickingMode.Ignore;
    foreach (var child in element.Children()) { SetPickingIgnoreRecursive(child); }
}
```
Call once in `Start()`, right after querying the root (see Awake()/Start() correction above) — since the tree is static (one root + one label, never rebuilt with `.Clear()`), there is no risk of `Refresh()` silently reintroducing `PickingMode.Position` on new children (unlike the closeable-overlay-slide gotcha, which applies to dynamically rebuilt content).

### Localization / formatting

`FlyTextNotifierDocument.Notify(key, args)` calls `_loc.Get(key)` then `string.Format(resolved, args)` (no-op format when `args` is empty) and stores only the final string in the queue — never the key. This satisfies both the "no `ILocalization` change" and "locale change mid-notification keeps already-resolved text" criteria without any extra bookkeeping: the string is fixed at `Notify()` call time, so a `SetLocale` call afterward literally cannot affect it (nothing re-reads `_loc` for already-queued/visible items). Guard `string.Format` with a try/catch or an args-length check only if `args` is null — must not throw (spec: "must not crash").

### First usage — manual save confirmation

Found the exact wiring point: `Assets/Scripts/Unity/UI/GameMenuDocument.cs`, method `OnSave()` (lines 90–92):
```csharp
void OnSave() {
    _commands?.Push(new SaveGameCommand());
}
```
`SaveGameCommand` is processed synchronously within the same `GameLogic.Update()` tick that reads the command buffer (`src/Game.Main/GameLogic.cs` lines 88–95 call `SaveGame(isAutoSave)` directly). `SaveGame` (lines 136–145) has no try/catch around `_context.Storage.Write(...)` today — any I/O exception propagates uncaught, and there is no success/failure signal exposed to Unity/VisualState.

**Correction from plan review:** the spec's acceptance criterion is "when the save completes successfully" — firing the notification unconditionally right after pushing the command (before `GameLogic.Update()` has even run this frame) would show "Game saved" even if the write throws or silently no-ops (`Storage`/`Serializer` null). This plan adds a minimal completion signal so the notification reflects actual outcome, following the same `INotifyPropertyChanged`-on-`VisualState` pattern already used throughout `src/Game.Main/VisualState.cs` (e.g. `SelectedCountryState.Set(...)`, which unconditionally raises `PropertyChanged` on every call — the same shape needed here so repeated saves each produce a fresh signal):

```csharp
// src/Game.Main/VisualState.cs — new state class, registered as a VisualState property
public class SaveResultState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Success { get; private set; }
    public string? ErrorType { get; private set; }

    public void Set(bool success, string? errorType) {
        Success = success;
        ErrorType = errorType;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
// on VisualState: public SaveResultState SaveResult { get; } = new SaveResultState();
```

```csharp
// src/Game.Main/GameLogic.cs — SaveGame wrapped in try/catch, reports outcome
void SaveGame(bool isAutoSave) {
    if (_context.Storage == null || _context.Serializer == null) {
        return;
    }
    try {
        var snapshot = SaveSystem.BuildSnapshot(_world);
        string fileName = isAutoSave ? $"autosave_{snapshot.Header.OrganizationId}" : snapshot.Header.SaveName;
        _context.Storage.Write(
            $"Saves/{fileName}.json",
            _context.Serializer.Serialize(snapshot));
        if (!isAutoSave) {
            VisualState.SaveResult.Set(true, null);
        }
    } catch (Exception ex) {
        if (!isAutoSave) {
            VisualState.SaveResult.Set(false, ex.GetType().Name);
        }
    }
}
```
The `!isAutoSave` guard keeps autosave (which already has its own "one autosave per session" flow, out of scope here) from spamming fly text — only manual saves raise `SaveResult`, matching the spec's manual-save-only first caller. Only `ex.GetType().Name` (e.g. `"IOException"`) is surfaced, per explicit instruction — never the exception message or stack trace, avoiding any risk of leaking a raw file-system path or internal detail into player-facing UI text.

`GameMenuDocument` subscribes to `SaveResult.PropertyChanged` the same way it already subscribes to `Locale.PropertyChanged` (`OnEnable`/`OnDisable`):
```csharp
void OnEnable() {
    _visualState.Locale.PropertyChanged += HandleLocaleChanged;
    _visualState.SaveResult.PropertyChanged += HandleSaveResultChanged;
}
void OnDisable() {
    _visualState.Locale.PropertyChanged -= HandleLocaleChanged;
    _visualState.SaveResult.PropertyChanged -= HandleSaveResultChanged;
}
void HandleSaveResultChanged(object sender, PropertyChangedEventArgs e) {
    var result = _visualState.SaveResult;
    if (result.Success) {
        _flyText?.Notify("game_menu.save.confirmation");
    } else {
        _flyText?.Notify("game_menu.save.error", result.ErrorType);
    }
}
```
`OnSave()` itself no longer calls `Notify` directly — it only pushes the command; the notification is now driven entirely by the `SaveResult` signal, which fires within the same or next `GameLogic.Update()` tick (no long-timeout polling coroutine needed, unlike the discover-action async pattern in `game_loop_integration.md`, since this result is available essentially immediately).

`GameMenuDocument` gains a `[Inject]`-constructed `IFlyTextNotifier _flyText` field alongside its existing injected dependencies. Two new locale keys must be added to `Assets/Localization/en.asset` and `ru.asset`:
- `game_menu.save.confirmation` — e.g. `"Game saved."` / `"Игра сохранена."`
- `game_menu.save.error` — e.g. `"Error happens while saving game: {0}"` / Russian equivalent, with `{0}` substituted by `result.ErrorType` via the notifier's existing `string.Format` step.

This follows the existing localization authoring process untouched, per spec's out-of-scope note; only the two new keys are added.

### Queueing semantics recap

`Notify()` always enqueues (never overwrites/coalesces); `Tick()`'s `Idle` phase dequeues the next item only after the previous item's `Exit` phase fully completes — this directly satisfies "queued, one at a time, after the current one finishes its full animation."

## Steps

### Agent Steps
- [ ] **Add `SaveResultState` to `VisualState`** — `src/Game.Main/VisualState.cs`; new `INotifyPropertyChanged` class (`Success`, `ErrorType`, `Set(...)` unconditionally raising `PropertyChanged`, matching the existing state-class pattern) plus a `SaveResult` property on `VisualState`.
- [ ] **Wrap `GameLogic.SaveGame` in try/catch and report outcome** — `src/Game.Main/GameLogic.cs`, lines 136–145; on success call `VisualState.SaveResult.Set(true, null)`, on exception call `VisualState.SaveResult.Set(false, ex.GetType().Name)`; guard both calls with `!isAutoSave` so autosave does not trigger fly text.
- [ ] **Create `IFlyTextNotifier` interface** — `Assets/Scripts/Unity/UI/IFlyTextNotifier.cs`, single `Notify(string key, params object[] args)` method.
- [ ] **Create `FlyTextNotifierDocument` MonoBehaviour** — `Assets/Scripts/Unity/UI/FlyTextNotifierDocument.cs`; implements `IFlyTextNotifier` and `VContainer.Unity.ITickable`; owns queue + phase state machine + style-based animation per the Approach section; `[RequireComponent(typeof(UIDocument))]`; `[Inject] void Construct(ILocalization loc)`; `Awake()` caches `UIDocument` and sets `sortingOrder = TopMostSortingOrder` (const `1000`); `Start()` queries `rootVisualElement`/`Label`, hides root, applies recursive `PickingMode.Ignore` — not `Awake()` (see Approach correction).
- [ ] **Write `FlyText.uxml`** — `Assets/UI/FlyText/FlyText.uxml`; root `VisualElement` (`fly-text-root`) with `<ui:Style>` importing `SharedStyles.uss` then `FlyText.uss`; one child `Label` (`fly-text-label`) using `.gs-content`/`.gs-header` shared classes for typography.
- [ ] **Write `FlyText.uss`** — `Assets/UI/FlyText/FlyText.uss`; layout-only rules: `position: absolute; top: 5%; left: 50%;` resting transform; no color/font redefinition (reuse shared classes).
- [ ] **Register `IFlyTextNotifier` in `GameLifetimeScope`** — `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`; add `builder.RegisterComponentInHierarchy<FlyTextNotifierDocument>().As<IFlyTextNotifier, ITickable>();` alongside the other `RegisterComponentInHierarchy` calls.
- [ ] **Register `IFlyTextNotifier` in `MainMenuLifetimeScope`** — `Assets/Scripts/Unity/DI/MainMenuLifetimeScope.cs`; same registration line.
- [ ] **Wire `GameMenuDocument`** — `Assets/Scripts/Unity/UI/GameMenuDocument.cs`; add `IFlyTextNotifier _flyText` field, extend `Construct(...)` signature, subscribe/unsubscribe `SaveResult.PropertyChanged` in `OnEnable`/`OnDisable`, add `HandleSaveResultChanged` calling `_flyText.Notify("game_menu.save.confirmation")` on success or `_flyText.Notify("game_menu.save.error", result.ErrorType)` on failure. `OnSave()` no longer calls `Notify` directly.
- [ ] **Add locale keys** — `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`; keys `game_menu.save.confirmation` ("Game saved." / "Игра сохранена.") and `game_menu.save.error` ("Error happens while saving game: {0}" / Russian equivalent).
- [ ] **Compile check** — after all script/UXML/USS changes, `refresh_unity` then `read_console(types=["error"])` to confirm no compile errors before adding scene instances.
- [ ] **Add `FlyTextUI` GameObject to each playable scene via MCP** — in `MainMenu.unity` and `Map.unity` (not `CountrySelection.unity` — see Approach correction), use `manage_gameobject` (create) + `manage_components` (add `UIDocument` referencing `HUDPanelSettings.asset` + `FlyText.uxml` as `sourceAsset`, add `FlyTextNotifierDocument`), then `manage_scene` (save). Verify via `read_console` after each scene save.
- [ ] **Update `.claude/rules/unity/uitoolkit.md` Layer Model section** — correct it to state that only `HUDPanelSettings.asset` is actually wired into any scene, `OverlayPanelSettings.asset` is unused, and layering within the shared panel is controlled via `UIDocument.sortingOrder`, with `FlyTextNotifierDocument.TopMostSortingOrder` (1000) as the current ceiling.

### User Steps
### 1. Visually verify animation timing and position
Enter Play mode in the `Map` scene, open the game menu, click Save, and visually confirm: notification appears top-center, scales up smoothly, holds, then moves down/shrinks/fades out over roughly the documented durations, and does not block clicks on menu buttons underneath it while animating. Repeat once in `MainMenu` scene if a menu-level trigger is added later (not required for this spec's first caller, but confirms the shared layer renders correctly in both scope types) — for this spec, only the Map/game-menu path needs verification since that is the only wired caller.

### 2. Verify the error path
Temporarily force a save failure (e.g. rename/lock the `Saves/` folder, or point `Storage` at an invalid path in a debug build) and confirm the game menu shows the error fly text with an exception type name rather than crashing or hanging. Revert the temporary change afterward — no permanent test hook for induced failure is part of this plan.

## Tests

This plan touches `src/Game.Main/GameLogic.cs` and `src/Game.Main/VisualState.cs`. Add/update:
- A unit test asserting `GameLogic.SaveGame` (manual, non-auto save) sets `VisualState.SaveResult.Success == true` and raises `PropertyChanged` on a normal write.
- A unit test asserting that when `Storage.Write` throws, `VisualState.SaveResult.Success == false`, `ErrorType` equals the thrown exception's type name, and the exception does not propagate out of `GameLogic.Update()` (a fake/mock `IStorage` that throws on `Write` is sufficient — no real file I/O needed).
- A unit test asserting autosave (`isAutoSave: true`) does NOT raise `SaveResult.PropertyChanged`, confirming the `!isAutoSave` guard.

## Constitution Check

No conflicts found — plan aligns with all principles:

- **ECS for all game logic, living in `src/`:** The fly-text queue/phase/animation state is pure UI presentation timing (how long a label stays on screen and its transform/opacity), not simulation state — it has no effect on save data, ECS components, or any system in `src/`. It is explicitly analogous to other presentation-only MonoBehaviours already in the project (`CardPlayAnimator`, `TimeView`) that hold transient animation state outside ECS. No game rule or domain data is computed or stored in `FlyTextNotifierDocument`. The one piece of this plan that does touch `src/` — `SaveResultState`/`GameLogic.SaveGame`'s try/catch — is consistent with this principle: it reports an outcome of existing game logic (the save operation itself, already in `src/`), it doesn't add new simulation rules, and it follows the same `VisualState`/`INotifyPropertyChanged` pattern already used to surface all other game state to Unity.
- **VContainer is the sole DI mechanism:** `FlyTextNotifierDocument` is registered via `RegisterComponentInHierarchy` + `.As<...>()` in both lifetime scopes; no `new`, no `FindObjectOfType`, no static mutable singleton is used anywhere in this plan.
- **UI Toolkit only:** The new layer is a `UIDocument`/`PanelSettings`/UXML/USS layer, consistent with every other UI surface in the project; no Canvas/uGUI is introduced.
- **One `.asmdef` per feature folder:** No new folder/asmdef is created; the notifier lives in the existing `Assets/Scripts/Unity/UI/` folder and its existing `GS.Unity.UI.asmdef`, which already carries the required VContainer/Common references.
- **Spec before plan / plan before implement:** This plan follows the already-approved `Docs/Specs/42_fly-text-notifications/spec.md`.
- **C# code style:** All sketched code uses tabs, `_`-prefixed private fields, same-line opening braces, and omits redundant access modifiers, consistent with `.claude/rules/csharp/code_style.md`.

Use /implement to start working on the plan or request changes.
