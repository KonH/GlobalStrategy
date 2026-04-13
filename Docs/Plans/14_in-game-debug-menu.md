# 14 — In-Game Debug Menu

## Goal

Move the ECS Viewer debug action from the Unity Editor menu bar into a foldable in-game debug panel embedded in the Map scene HUD. Simultaneously, restyle the existing game menu button: move it to the right corner of the top bar and replace its text with a hamburger icon.

## Current State

- **Debug entry point:** `Assets/Scripts/Editor/EcsViewer/EcsViewerMenu.cs` — `[MenuItem("Game/ECS Viewer/Open")]` opens the ECS Viewer URL. Editor-only; not accessible in Play mode without the menu bar.
- **Menu button:** `btn-menu` in `HUD.uxml`, centered at the top, text `"Menu"` (localized via `hud.menu`). Wired in `HUDDocument.Start()` to call `_gameMenu.Show()`.
- **Top-center panel:** `top-center-panel` in `HUD.uss`, `justify-content: center`, houses the menu button.

## Approach

1. **Relocate the menu button** to the right side of the top bar (no text, hamburger icon `≡`).
2. **Add a foldable debug panel** to the top-center area of the HUD. Toggle button is always visible; content expands/collapses on click.
3. **Debug panel content** — initially one action: "Open ECS Viewer". Clicking it calls `EcsViewerBridge.CurrentUrl` and opens the browser URL, exactly as the old `[MenuItem]` did.
4. **Inject `EcsViewerBridge`** into `HUDDocument` via VContainer so the debug panel can query the URL without static access where possible. (Since `EcsViewerBridge.CurrentUrl` is already a `static` property, a direct static call is acceptable.)
5. Keep `EcsViewerMenu.cs` as-is (Editor menu item is a harmless convenience, removing it is optional).

## Steps

### 1 — HUD.uxml: restructure top bar

- Wrap the `time-panel` instance and `btn-menu` in a new `top-right-panel` container (`position: absolute; top: 0; right: 0; flex-direction: column`). The time panel stacks on top, the menu button below it.
- Remove the old `.time-panel` absolute positioning from `HUD.uss` (the wrapper now handles placement).
- Set `btn-menu` text to `"≡"`, no localization key needed; apply class `hud-hamburger-button`.
- Keep `top-center-panel` for the debug toggle only (`justify-content: center`). Add `btn-debug-toggle` button with class `hud-debug-toggle`.
- Add `debug-panel` `VisualElement` inside `hud-root`, `position: absolute`, top-center (below the toggle bar), containing a `btn-ecs-viewer` button. Hidden by default (`display: none`).

### 2 — HUD.uss: new styles

- `.hud-hamburger-button` — same visual theme as current `.hud-menu-button` but without text padding; fixed width square, font-size 20px.
- `.hud-debug-toggle` — similar style, positioned in center.
- `.debug-panel` — absolute, top offset (below the toggle bar), centered horizontally, `background-color: rgb(238, 222, 180)`, border, padding, `display: none` initial state.
- `.debug-panel-button` — consistent brown-theme button style.

### 3 — HUDDocument.cs: wire debug panel

- Add fields: `Button _btnDebugToggle`, `VisualElement _debugPanel`, `Button _btnEcsViewer`, `bool _debugPanelOpen`.
- In `Start()`:
  - Query `btn-debug-toggle`, `debug-panel`, `btn-ecs-viewer`.
  - `_btnDebugToggle.clicked += ToggleDebugPanel`.
  - `_btnEcsViewer.clicked += OpenEcsViewer`.
- `ToggleDebugPanel()`: flip `_debugPanelOpen`, set `_debugPanel.style.display`.
- `OpenEcsViewer()`: read `EcsViewerBridge.CurrentUrl`; if null log warning, else `Application.OpenURL(url)`.
- Remove `_btnMenu.text = _loc.Get("hud.menu")` from `HandleLocaleChanged` (button is now icon-only, no localization key needed).
- Update `Start()` to no longer set `_btnMenu.text`.

### 4 — Localization: remove unused key

- The `hud.menu` key is no longer displayed. Leave the key in `en.asset` / `ru.asset` for safety (harmless), or remove — removing is cleaner but optional.

### 5 — Verify

- Enter Play mode in Map scene.
- Confirm hamburger button (`≡`) appears at top-right below the time panel, opens game menu.
- Confirm debug toggle button appears at top-center.
- Click toggle — debug panel expands with "ECS Viewer" button.
- Click again — panel collapses.
- Click "ECS Viewer" — browser opens ECS Viewer URL (requires EcsViewerBridge running).
- Click "ECS Viewer" when not in proper play state — warning logged, no crash.

---

Use `/implement` to start working on the plan or request changes.
