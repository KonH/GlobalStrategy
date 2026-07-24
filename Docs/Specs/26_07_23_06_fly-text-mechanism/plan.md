# Plan: Fly Text Mechanism (Common)

## Spec

Every lightweight outcome confirmation (country discovery, save result, delete-all-saves result, opinion change, control change) should use one consistent floating-text notification mechanism, so every confirmation looks and behaves the same and a new call site is a single call, not new UI/animation code.

Acceptance criteria:
- Renders on the existing dedicated top-most fly-text layer (`FlyTextNotifierDocument`, `HUDPanelSettings.asset`, `sortingOrder = 1000`) in every scene, same guarantee the save-result notification already has.
- Visual becomes fade-in → hold → fade-out only (opacity, no scale, no translate) — the country-discovery look — replacing the current scale-up/move-down/scale-down/fade shape. This is the one fly-text visual going forward, used by all five call sites.
- Queues one at a time, uniformly across all five call sites (unchanged queueing semantics, now including discovery).
- Localization-key + format-args resolution (`ILocalization.Get(key)` + `string.Format`, resolved at call time) is unchanged for discovery/save-result/delete-all-saves.
- A new rich-text path renders pre-built `<b>`/`<color>` markup correctly for opinion/control call sites, without affecting plain-text call sites.
- Each scene's lifetime scope keeps registering its own instance behind the shared interface; no cross-scene persistence.
- A future call site needs exactly one call into the shared notifier and zero new UI/animation/layer code.
- Country discovery migrates off `CardPlayAnimator`'s bespoke `"fly-text"` Label/hand-rolled fade loop onto the shared mechanism, gaining a real localization key (replacing the hardcoded `"Discovered: {name}!"` string).
- Save result keeps its trigger/content, only the visual changes.
- Delete-all-saves gets its first feedback ever: an "Operation completed"-equivalent localized message.
- Newly-added `Control`/`Opinion` `GameLogEntry` items each show a fly text reusing the exact highlighted rich-text string `ActionLogView.BuildControlLine`/`BuildOpinionLine` already build — not re-derived.

Out of scope (per spec): exact timing constants beyond what's decided below are not a behavioral contract; no new `PanelSettings`/`UIDocument`; no error-path feedback for delete-all-saves; no fly text for `Discovery`-via-log-entry or `NewCharacter` log kinds; no burst coalescing/rate-limiting; `ActionLogView`'s own log panel rendering is unchanged; no sound/haptics/click-to-dismiss/history; no stacking of simultaneously-visible notifications.

## Approach

### Investigation summary

Two independent fly-text code paths exist today:

1. **`IFlyTextNotifier`/`FlyTextNotifierDocument`** (`Assets/Scripts/Unity/UI/`) — the save-result mechanism. Queued (`Queue<string>`), locale-safe (resolves `_loc.Get(key)` + `string.Format` at `Notify()` call time), registered per-scene in `GameLifetimeScope`/`MainMenuLifetimeScope` behind `IFlyTextNotifier`, rendered on `FlyTextUI` (`HUDPanelSettings.asset`, `sortingOrder = 1000`). Current visual: scale-up entrance (0.5→1, 0.2s) → hold (1.5s) → move-down/scale-down/fade exit (0.5s). `FlyText.uxml`'s `Label` uses `gs-content gs-header fly-text-label` classes; `FlyText.uss`'s `.fly-text-root` centers full-screen, `.fly-text-label` hardcodes 72px/white/shadow (a pre-existing violation of the "no color/font redefinition in per-feature USS" rule — not something this plan needs to fix beyond replacing it with the discovery look below). Only caller today: `GameMenuDocument.HandleSaveResultChanged` → `Notify("game_menu.save.confirmation")` / `Notify("game_menu.save.error", errorType)`.
2. **Bespoke discovery fade**, entirely inside `CardPlayAnimator.PlaySequence` (`Assets/Scripts/Unity/UI/CardPlayAnimator.cs`, lines ~144, ~235-267). Queries a `Label` named `"fly-text"` that lives directly in `Assets/UI/HUD/HUD.uxml` (line 59, class `gs-title`, `#fly-text` override in `Assets/UI/HUD/HUD.uss` lines 175-180 adds `font-size: 46px; text-shadow: 3px 3px 0 rgba(0,0,0,0.9); -unity-text-outline-width: 1px; -unity-text-outline-color: rgb(0,0,0);`), positioned `left: 50%; top: 40%;` with a runtime `translate(-50%, 0)`. Sets `flyText.text = $"Discovered: {localizedName}!"` — hardcoded, not a localization key — then hand-rolls opacity fade-in (0.5s) → hold (2s) → fade-out (0.5s) via a raw `while` loop. No queueing (only one card-play can be in flight), no dedicated top-most layer (rides on the HUD document itself).

Decision: mechanism #1 (`IFlyTextNotifier`) is extended and becomes the only implementation; mechanism #2 is retired and discovery becomes a caller of #1. #1's animation shape is replaced with #2's fade-only shape and #2's exact typography/position (`gs-title` + the `#fly-text` override's font-size/shadow/outline, `top: 40%; left: 50%;`), since that is literally "the discovery visual" the feature asks for — not a generic redesign.

The `Control`/`Opinion` rich-text call sites reuse `ActionLogView.BuildControlLine`/`BuildOpinionLine` (`Assets/Scripts/Unity/UI/ActionLogView.cs`, lines 96-112), which already build fully-localized, `<b>`/`<color>`-highlighted strings from a `GameLogEntry` (`src/Game.Main/VisualState.cs` lines 380-421) via a `WrapColored` helper. These are extracted into a shared static class so both `ActionLogView` and the new trigger point call the same code, per the spec's "reused as-is, not re-derived."

Delete-all-saves (`SettingsWindowDocument.DeleteAllSaves()`, `Assets/Scripts/Unity/UI/SettingsWindowDocument.cs` line 117) calls `_saveFileManager?.DeleteAllSaves()` synchronously today with zero feedback; per spec's out-of-scope note, this plan adds only the one success-path notification, no error signal.

Everything in this plan lives under `Assets/Scripts/Unity/UI/`, `Assets/UI/FlyText/`, `Assets/UI/HUD/`, and `Assets/Localization/` — no `src/` (`Game.Main`/`Game.Tests`) changes at all, since none of the five call sites need new domain/ECS state: `DiscoveredCountries.RecentlyDiscovered`, `SaveResultState`, and `GameLogState` already exist and are unchanged; delete-all-saves fires optimistically right after the existing synchronous call returns, matching the spec's decision not to add error handling.

### `IFlyTextNotifier` gains a raw/rich-text entry point

```csharp
// Assets/Scripts/Unity/UI/IFlyTextNotifier.cs
public interface IFlyTextNotifier {
	void Notify(string localizationKey, params object[] args);
	void NotifyRaw(string text);
}
```
`NotifyRaw` enqueues `text` verbatim — no `_loc.Get`, no `string.Format` — used by callers that already hold a fully-resolved (and possibly rich-text) string, i.e. the opinion/control log-entry call sites. `Notify` is unchanged. Both funnel into the same `Queue<string>` (already-resolved display strings), since the only thing that differs between plain and rich content is whether the Label's `enableRichText` output shows literal tags or renders them — nothing about the queue/phase machinery needs to know which kind an item is.

### `FlyTextNotifierDocument` — fade-only visual + rich text

```csharp
// Assets/Scripts/Unity/UI/FlyTextNotifierDocument.cs
[SerializeField] float _fadeInDuration = 0.5f;
[SerializeField] float _holdDuration = 2.0f;
[SerializeField] float _fadeOutDuration = 0.5f;
// _entranceDuration/_exitDuration/_entranceStartScale/_exitEndScale/_exitMoveDownPx removed — no scale/translate left in this component.

enum Phase { Idle, FadeIn, Hold, FadeOut }

void Start() {
	_root = _doc.rootVisualElement.Q<VisualElement>("fly-text-root");
	_label = _root.Q<Label>("fly-text-label");
	_label.enableRichText = true; // new — lets NotifyRaw's <b>/<color> markup render
	...
}

public void NotifyRaw(string text) {
	Debug.Log($"[FlyText] NotifyRaw: text=\"{text}\", queueCountBefore={_queue.Count}");
	_queue.Enqueue(text);
}

void Update() {
	...
	switch (_phase) {
		case Phase.FadeIn: {
			_elapsed += dt;
			float t = Mathf.Clamp01(_elapsed / _fadeInDuration);
			_root.style.opacity = t;
			if (t >= 1f) { _phase = Phase.Hold; _elapsed = 0f; }
			break;
		}
		case Phase.Hold:
			_elapsed += dt;
			if (_elapsed >= _holdDuration) { _phase = Phase.FadeOut; _elapsed = 0f; }
			break;
		case Phase.FadeOut: {
			_elapsed += dt;
			float t = Mathf.Clamp01(_elapsed / _fadeOutDuration);
			_root.style.opacity = 1f - t;
			if (t >= 1f) { HideAndReset(); _phase = Phase.Idle; }
			break;
		}
	}
}

void StartFadeIn(string text) {
	_label.text = text;
	_root.style.opacity = 0f;
	_root.style.display = DisplayStyle.Flex;
	_phase = Phase.FadeIn;
	_elapsed = 0f;
}

void HideAndReset() {
	_root.style.display = DisplayStyle.None;
	_root.style.opacity = 1f;
}
```
`SetPickingIgnoreRecursive`, the DI registration (`RegisterComponentInHierarchy<FlyTextNotifierDocument>().As<IFlyTextNotifier>()` in both scopes), and the queueing (`Idle` dequeues only after a full fade cycle) are unchanged — only the phase shape and constants change; `NotifyRaw` is additive.

### UXML/USS — typography and position match discovery exactly

`Assets/UI/FlyText/FlyText.uxml`: Label classes become `gs-title fly-text-label` (was `gs-content gs-header fly-text-label`) — `gs-title` supplies the base typography discovery already used; `fly-text-label` keeps carrying only the size/shadow/outline delta below, mirroring how `#fly-text` sat on top of `gs-title` in the old HUD-local element.

`Assets/UI/FlyText/FlyText.uss`:
```css
.fly-text-root {
	position: absolute;
	top: 40%;
	left: 50%;
	translate: -50% 0;
}

.fly-text-label {
	font-size: 46px;
	text-shadow: 3px 3px 0 rgba(0, 0, 0, 0.9);
	-unity-text-outline-width: 1px;
	-unity-text-outline-color: rgb(0, 0, 0);
}
```
This replaces the current full-screen-center `.fly-text-root` and the 72px/white/shadow `.fly-text-label` with the exact position and override discovery's `#fly-text` rule used in `HUD.uss` — the concrete reading of "use discovery visual."

### Retire the bespoke discovery code path

`Assets/UI/HUD/HUD.uxml`: delete line 59, `<ui:Label name="fly-text" .../>` — no longer queried by anything once `CardPlayAnimator` is migrated.

`Assets/UI/HUD/HUD.uss`: delete the `#fly-text { ... }` rule (lines 175-180) — its content is now `FlyText.uss`'s `.fly-text-label`, above.

`Assets/Scripts/Unity/UI/CardPlayAnimator.cs`: add `IFlyTextNotifier flyText` to `Construct(...)`, store `_flyText`. Remove the `var flyText = root.Q<Label>("fly-text");` lookup (line 144) and the entire hand-rolled fade block (lines 239-266). Replace with:
```csharp
if (success && !string.IsNullOrEmpty(discoveredCountryId)) {
	_cameraController?.PanToCountry(discoveredCountryId);
	await UniTask.Delay(1000);
	string localizedName = _loc.Get($"country_name.{discoveredCountryId}");
	if (string.IsNullOrEmpty(localizedName) || localizedName == $"country_name.{discoveredCountryId}") {
		localizedName = discoveredCountryId.Replace("_", " ");
	}
	_flyText?.Notify("hud.discovery.confirmation", localizedName);
}
```
The camera pan and 1s delay before showing the notification are unchanged; only the display mechanism changes.

### Delete-all-saves

`Assets/Scripts/Unity/UI/SettingsWindowDocument.cs`: add `IFlyTextNotifier flyText` to `Construct(...)`, store `_flyText`.
```csharp
void DeleteAllSaves() {
	_saveFileManager?.DeleteAllSaves();
	_flyText?.Notify("settings.delete_saves.confirmation");
}
```
`SettingsWindowDocument` is already registered in both `GameLifetimeScope` and `MainMenuLifetimeScope`, both of which already register `FlyTextNotifierDocument` behind `IFlyTextNotifier` — no new DI registration needed in either scope.

### Opinion/control change effects — shared line-formatting helper

Extract `ActionLogView`'s rich-text builders into a new static class so the fly-text trigger and the log panel call identical code:

```csharp
// Assets/Scripts/Unity/UI/GameLogLineFormatter.cs
static class GameLogLineFormatter {
	public static string BuildDiscoveryLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) { ... }
	public static string BuildControlLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) { ... }
	public static string BuildOpinionLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) { ... }
	public static string BuildNewCharacterLine(GameLogEntry entry, ILocalization loc, CountryVisualConfig countryVisualConfig, OrgVisualConfig orgVisualConfig) { ... }
	static string WrapColored(string text, Color? color) { ... }
	static string FormatNumber(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
```
Bodies are moved verbatim from `ActionLogView` (no logic change). `ActionLogView.BuildLabel` becomes:
```csharp
string text = entry.Kind switch {
	GameLogEntryKind.Discovery => GameLogLineFormatter.BuildDiscoveryLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
	GameLogEntryKind.Control => GameLogLineFormatter.BuildControlLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
	GameLogEntryKind.Opinion => GameLogLineFormatter.BuildOpinionLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
	GameLogEntryKind.NewCharacter => GameLogLineFormatter.BuildNewCharacterLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),
	_ => ""
};
```
(`ActionLogView`'s own private copies of these methods are deleted.)

`HUDDocument` (`Assets/Scripts/Unity/UI/HUDDocument.cs`) already owns the `GameLogState` subscription (`OnEnable` line 301, `HandleGameLogChanged` line 565) and already constructs `ActionLogView`. It gains `IFlyTextNotifier flyText` in `Construct(...)` and a watermark field:
```csharp
long _lastNotifiedLogSequenceId = -1;
```
In `OnEnable()`, right after the existing initial `_actionLog?.Refresh(_state.GameLog);` call (line 309), initialize the watermark to the highest `SequenceId` already present — so entries from a loaded save (or any already-displayed entries) never fire a fly text retroactively, only entries added after this point do:
```csharp
_lastNotifiedLogSequenceId = HighestSequenceId(_state.GameLog);
```
`HandleGameLogChanged` gains a second call alongside the existing `_actionLog?.Refresh(...)`:
```csharp
void HandleGameLogChanged(object sender, PropertyChangedEventArgs e) {
	_actionLog?.Refresh(_state.GameLog);
	NotifyNewLogEntries();
}

void NotifyNewLogEntries() {
	if (_flyText == null) { return; }
	long maxSeen = _lastNotifiedLogSequenceId;
	foreach (var entry in _state.GameLog.Entries) {
		if (entry.SequenceId <= _lastNotifiedLogSequenceId) { continue; }
		if (entry.Kind == GameLogEntryKind.Control) {
			_flyText.NotifyRaw(GameLogLineFormatter.BuildControlLine(entry, _loc, _countryVisualConfig, _orgVisualConfig));
		} else if (entry.Kind == GameLogEntryKind.Opinion) {
			_flyText.NotifyRaw(GameLogLineFormatter.BuildOpinionLine(entry, _loc, _countryVisualConfig, _orgVisualConfig));
		}
		if (entry.SequenceId > maxSeen) { maxSeen = entry.SequenceId; }
	}
	_lastNotifiedLogSequenceId = maxSeen;
}

static long HighestSequenceId(GameLogState state) {
	long max = -1;
	foreach (var entry in state.Entries) {
		if (entry.SequenceId > max) { max = entry.SequenceId; }
	}
	return max;
}
```
`Discovery`/`NewCharacter` entries are intentionally skipped here — discovery already has its own dedicated call site with different wording (see above), and `NewCharacter` has no fly text per the spec's out-of-scope list. Per spec, bursts (e.g. one action producing both a `Control` and an `Opinion` entry in the same tick) simply queue two fly texts in sequence — no coalescing is implemented.

### Localization keys

Add to `Assets/Localization/en.asset` and `ru.asset`:
- `hud.discovery.confirmation` — e.g. `"Discovered: {0}!"` / Russian equivalent (near the existing `hud.actions` key).
- `settings.delete_saves.confirmation` — e.g. `"Operation completed."` / Russian equivalent (near the existing `settings.delete_saves` key).

## Steps

### Agent Steps
- [x] **Extract `GameLogLineFormatter`** — new `Assets/Scripts/Unity/UI/GameLogLineFormatter.cs`; move `BuildDiscoveryLine`/`BuildControlLine`/`BuildOpinionLine`/`BuildNewCharacterLine`/`WrapColored`/`FormatNumber` out of `ActionLogView.cs` unchanged in logic; update `ActionLogView.BuildLabel` to call the new static methods.
- [x] **Add `NotifyRaw` to `IFlyTextNotifier`** — `Assets/Scripts/Unity/UI/IFlyTextNotifier.cs`.
- [x] **Rework `FlyTextNotifierDocument` to fade-only + rich text** — `Assets/Scripts/Unity/UI/FlyTextNotifierDocument.cs`; replace Entrance/Exit scale/move phases with FadeIn/Hold/FadeOut opacity-only phases (`_fadeInDuration=0.5f`, `_holdDuration=2.0f`, `_fadeOutDuration=0.5f`); remove now-unused scale/move fields; set `_label.enableRichText = true` in `Start()`; implement `NotifyRaw`.
- [x] **Update `FlyText.uxml`** — Label classes `gs-content gs-header fly-text-label` → `gs-title fly-text-label`.
- [x] **Update `FlyText.uss`** — `.fly-text-root` → `top: 40%; left: 50%; translate: -50% 0;`; `.fly-text-label` → `font-size: 46px; text-shadow: 3px 3px 0 rgba(0,0,0,0.9); -unity-text-outline-width: 1px; -unity-text-outline-color: rgb(0,0,0);`.
- [x] **Migrate discovery in `CardPlayAnimator`** — add `IFlyTextNotifier flyText` to `Construct(...)`; remove the `"fly-text"` Label lookup and hand-rolled fade block; replace with a single `_flyText?.Notify("hud.discovery.confirmation", localizedName);` call.
- [x] **Remove the bespoke `fly-text` element** — delete the `<ui:Label name="fly-text" .../>` line from `Assets/UI/HUD/HUD.uxml`; delete the `#fly-text { ... }` rule from `Assets/UI/HUD/HUD.uss`.
- [x] **Wire delete-all-saves** — `Assets/Scripts/Unity/UI/SettingsWindowDocument.cs`; add `IFlyTextNotifier flyText` to `Construct(...)`; call `_flyText?.Notify("settings.delete_saves.confirmation");` in `DeleteAllSaves()` after the existing `_saveFileManager?.DeleteAllSaves();`.
- [x] **Wire opinion/control fly text in `HUDDocument`** — add `IFlyTextNotifier flyText` to `Construct(...)`; add `_lastNotifiedLogSequenceId` field, initialized in `OnEnable()` right after the existing initial `_actionLog?.Refresh(_state.GameLog);` call; extend `HandleGameLogChanged` to call a new `NotifyNewLogEntries()` that scans for `Control`/`Opinion` entries newer than the watermark and calls `_flyText.NotifyRaw(GameLogLineFormatter.BuildControlLine/BuildOpinionLine(...))`.
- [x] **Add locale keys** — `Assets/Localization/en.asset` and `ru.asset`; `hud.discovery.confirmation` and `settings.delete_saves.confirmation`.
- [x] **Compile check** — after all script/UXML/USS changes, `refresh_unity` then `read_console(types=["error"])`.

### User Steps
### 1. Visually verify the shared fade-only visual across all five call sites (Map scene)
Enter Play mode in `Map.unity`. Trigger, in turn: a country-discovery card action, a manual save (success), an action that changes control, an action that changes opinion, and (via the in-game settings window) delete-all-saves. Confirm every one now shows the same fade-in → hold → fade-out look (no scale, no move), at the same position/typography (matches the old discovery look: centered, ~40% from top, bold with dark shadow+outline). Confirm the opinion/control fly texts render their `<b>`/color highlights as actual styled text, not literal tag characters. If possible, trigger an action that changes both control and opinion in the same tick and confirm both fly texts queue and play one after another rather than overlapping.

### 2. Verify delete-all-saves from the main menu
Enter Play mode in `MainMenu.unity`, open Settings, delete all saves, and confirm the same fly text appears there too (`SettingsWindowDocument` and `FlyTextNotifierDocument` are both registered in `MainMenuLifetimeScope`).

### 3. Verify a forced save error still displays correctly
Repeat the existing save-error verification (e.g. temporarily make the save path unwritable) and confirm `game_menu.save.error` still shows with the new fade-only visual and does not crash or hang.

## Tests

This plan makes no changes under `src/` (`Game.Main`/`Game.Tests`) — all five call sites reuse existing, unchanged domain state (`DiscoveredCountries.RecentlyDiscovered`, `SaveResultState`, `GameLogState`), and delete-all-saves adds only a UI-side notification call with no new success/failure signal. No new or updated `Game.Tests` unit tests are required; verification is the Unity Play-mode checks in User Steps above, consistent with this project's existing pattern of only unit-testing `src/` logic.

## Constitution Check

No conflicts found:

- **ECS for all game logic, living in `src/`:** This plan touches only `Assets/Scripts/Unity/UI/`, `Assets/UI/FlyText/`, `Assets/UI/HUD/`, and `Assets/Localization/` — no `src/` changes at all. No new simulation/domain state is introduced; all five call sites read existing `VisualState` data.
- **VContainer is the sole DI mechanism:** `IFlyTextNotifier` is already registered via `RegisterComponentInHierarchy<FlyTextNotifierDocument>().As<IFlyTextNotifier>()` in both `GameLifetimeScope` and `MainMenuLifetimeScope`; the new consumers (`CardPlayAnimator`, `SettingsWindowDocument`, `HUDDocument`) all receive it through their existing `[Inject] void Construct(...)` methods — no `new`, no `FindObjectOfType`, no new registrations needed.
- **UI Toolkit only:** All changes are UXML/USS edits and MonoBehaviour/plain-C#-helper code; no Canvas/UGUI.
- **One `.asmdef` per feature folder:** `GameLogLineFormatter.cs` is added to the existing `Assets/Scripts/Unity/UI/` folder (`GS.Unity.UI.asmdef`); no new folder or assembly.
- **Spec before plan:** This plan follows the approved `Docs/Specs/26_07_23_06_fly-text-mechanism/spec.md`.
- **C# code style:** Sketched code uses tabs, `_`-prefixed private fields, same-line braces, no redundant access modifiers.

Use /implement to start working on the plan or request changes.
