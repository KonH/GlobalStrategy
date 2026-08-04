# Plan: Small UI Improvements

## Spec

Source: `Docs/Specs/26_08_04_17_small-ui-improvements/spec.md` (owner clarifications locked).

As a player, batch four HUD/overlay polish fixes so everyday UI interactions stay consistent: pause menu above the HUD, card-play that does not resume an already-paused game, an action log that passes taps through / clears the bottom selection bar / has a readable gray backdrop, and flying cards that match static card size.

Acceptance criteria (condensed):
- **Pause menu above HUD** — Esc / HUD menu: dimmed backdrop + panel draw fully on top of HUD chrome; Resume / Save / Exit remain clickable.
- **Card-play pause ownership** — play while already paused → stay paused after animation; play while running → temporary pause then resume (unchanged happy path).
- **Action log** — never intercepts pointer input; fixed bottom clearance above selected country/org bar; semi-transparent gray backdrop behind entries.
- **Flying card size** — transition card footprint matches static `.action-card` **240×300**.

Out of scope: pause-menu redesign/Settings, `GameMenuDocument.Hide()` always-unpause, new log line types / dynamic log anchoring, card art/typography resize beyond outer dimensions, new PanelSettings / Canvas, web-client parity.

## Goal

Apply four presentation-only Unity UI Toolkit fixes under `Assets/Scripts/Unity/UI/` (+ USS) using the locked constants: `GameMenuDocument` sortingOrder **990**, action-log bottom reserve **280f**, backdrop **`rgba(0, 0, 0, 0.35)`**, flying card **240×300**.

## Approach

Four independent presentation fixes. No ECS / command / VisualState shape changes. No VContainer registration changes. No scene YAML edits for sortingOrder (code const, same pattern as peers).

### 1. Pause menu above HUD

| Item | Detail |
|---|---|
| File | `Assets/Scripts/Unity/UI/GameMenuDocument.cs` |
| Bug | `_doc.sortingOrder` never set; scene `m_SortingOrder: 0` ties with `GameHUD`, so HUD paints over the menu |
| Fix | In `Awake`, after caching `UIDocument`, assign `_doc.sortingOrder` from `const int SortingOrder = 990` |
| Comment | Same “explicit sortingOrder, not scene-authoring order” pattern as `GoalsWindowDocument` (note: above modals 500–510, just below FlyText 1000, below EndGame 1100) |
| Unchanged | UXML/USS (`GameMenu.uxml` / `.gs-blackfade` / `.gs-panel`); shared `HUDPanelSettings`; `Hide()` unpause |

### 2. Card-play pause ownership

| Item | Detail |
|---|---|
| File | `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` |
| Bug | `PlaySequence` (~137 / ~231) and `PlayCountrySequence` (~256 / ~352) always `PauseCommand` then always `UnpauseCommand` |
| Pattern | `WarResultWindowDocument` `_issuedPause` — pause only when not already paused; unpause only when this feature issued pause |
| Fix | At each sequence start (after `ModalState.IsModalOpen = true`, before / with the pause push): `bool issuedPause = !_state.Time.IsPaused;` then push `PauseCommand` only if `issuedPause`. At end: always clear `ModalState.IsModalOpen`; push `UnpauseCommand` only if `issuedPause`. Local bool per sequence is fine (`_isPlaying` already prevents concurrent sequences). `_state` is already injected |
| Unchanged | `GameMenuDocument.Hide()` unconditional `UnpauseCommand`; animation timings; `PlayCardActionCommand` push order relative to pause when pause is issued (keep “action before pause so both process in the same tick” when pausing) |

### 3. Action log: pass-through, clearance, backdrop

| Item | Detail |
|---|---|
| View | `Assets/Scripts/Unity/UI/ActionLogView.cs` |
| USS | `Assets/UI/HUD/ActionLog/ActionLog.uss` and/or `Assets/UI/HUD/HUD.uss` `.action-log-panel` |
| Tap blocking | `PickingMode.Ignore` is not recursive (uitoolkit.md). The `action-log` Instance (`.action-log-panel` / `ActionLogView._root`) has no `picking-mode="Ignore"` in `HUD.uxml` and defaults to `Position`, so it blocks the whole strip even when children Ignore. In the constructor, call `SetPickingIgnoreRecursive(_root)` (same helper pattern as `CardTransitionView` / `FlyTextNotifierDocument`). In `BuildLabel`, apply `SetPickingIgnoreRecursive(label)` (not only `label.pickingMode = Ignore`). Re-apply for every label `Refresh` adds. |
| Bottom clearance | `BottomReservedOffsetPx` **160f → 280f** (fixed; no dynamic measurement). Top/right sizing unchanged |
| Backdrop | Panel background **`rgba(0, 0, 0, 0.35)`** on **`.action-log-panel` only** (`HUD.uss`). Do **not** also set it on `.action-log-root` — nested fills stack and exceed the locked 0.35 alpha. Keep entry white + shadow. No second PanelSettings |

### 4. Flying card size

| Item | Detail |
|---|---|
| File | `Assets/Scripts/Unity/UI/CardTransitionView.cs` `PlaceAndAnimate` |
| Bug | width `240f`, height `320f` vs static `.action-card` **240×300** |
| Fix | height **320f → 300f**; width stays `240f`. No worldBound size lerp. Keep `SetPickingIgnoreRecursive`. No animation-duration changes |

## Agent Steps

- [ ] **Raise pause-menu sortingOrder** — `GameMenuDocument.Awake`: after `_doc = GetComponent<UIDocument>()`, set `_doc.sortingOrder` from `const int SortingOrder = 990` with a GoalsWindow-style comment (above modals 500–510, just below FlyText 1000, below EndGame 1100). Do not change `Hide()` unpause or menu UXML/USS.

- [ ] **Card-play pause ownership** — In `CardPlayAnimator.PlaySequence` and `PlayCountrySequence`: at start `bool issuedPause = !_state.Time.IsPaused;`; push `PauseCommand` only if `issuedPause` (when pausing, keep existing “action command then pause” same-tick ordering). At end always clear `ModalState.IsModalOpen`; push `UnpauseCommand` only if `issuedPause`. Do not change `GameMenuDocument.Hide()`.

- [ ] **Action-log pick-through + bottom reserve** — `ActionLogView`: change `BottomReservedOffsetPx` to **280f**. In the constructor, `SetPickingIgnoreRecursive(_root)` so the Instance panel itself pass-throughs. In `BuildLabel`, `SetPickingIgnoreRecursive(label)` for every new entry (Ignore is not recursive; re-apply on each `Refresh` add).

- [ ] **Action-log gray backdrop** — Add `background-color: rgba(0, 0, 0, 0.35);` to `.action-log-panel` in `HUD.uss` only (not also `.action-log-root`). Keep `.action-log-entry` white + shadow legibility. No new PanelSettings.

- [ ] **Flying card hard-match 240×300** — `CardTransitionView.PlaceAndAnimate`: change `_cardCopy.style.height` from `320f` to `300f`. Width stays `240f`. No worldBound size lerp; keep `SetPickingIgnoreRecursive`.

## User Steps

### 1. Pause menu above HUD (Unity Editor)

Play Map, open the pause menu (Esc or HUD menu). Confirm the dimmed backdrop and menu panel draw fully above HUD chrome (time controls, action log, country/org bars, buttons). Confirm Resume / Save / Exit are clickable and not blocked.

### 2. Card-play pause ownership (Unity Editor)

With time already paused, play a hand card (org and country if both available). After the animation finishes, confirm the simulation stays paused. With time running, play a card and confirm the temporary pause → resume happy path still works.

### 3. Action log (Unity Editor)

With the action log visible, click/tap through the log area onto the map or other HUD controls — confirm clicks pass through. Open a country or org selection at the bottom and confirm the log sits entirely above that bar with a clear gap (no overlap). Confirm a semi-transparent gray backdrop behind log entries over the map.

### 4. Flying card size (Unity Editor)

Play a card and watch the transition copy. Confirm the flying card’s outer width/height match the static hand/overlay card (**240×300**), not taller than the static card.

## Tests

- No new pure-C# unit tests required: these are presentation-only UI Toolkit MonoBehaviour/view/USS changes. There is no existing EditMode pattern covering `CardPlayAnimator`, `ActionLogView`, `GameMenuDocument`, or `CardTransitionView` size/picking.
- Optional: if any existing test constructs related state for pause commands, leave as-is (no command/TimeSystem API changes).
- Primary verification is the Unity Editor User Steps above.

## Constitution Check

No conflicts found — plan aligns with all principles.

- **UI Toolkit only:** sortingOrder, USS backdrop, picking modes, and view layout; no Canvas/UGUI; shared `HUDPanelSettings` only.
- **ECS for game logic:** presentation under `Assets/Scripts/Unity/UI/`; pause ownership only gates existing `PauseCommand` / `UnpauseCommand` pushes — no simulation/domain rule moves into MonoBehaviours.
- **VContainer:** no new services, registrations, or mutable singletons; `_state` / `_commands` already injected on `CardPlayAnimator`.
- **Spec before plan / Docs/Specs organisation:** this plan sits beside the approved spec in `Docs/Specs/26_08_04_17_small-ui-improvements/`.
- **Plan before implement:** plan written before any code or asset changes.
- **C# style:** follow existing file conventions (tabs, `_` private members, braces).

Use the implement skill to start working on the plan or request changes.
