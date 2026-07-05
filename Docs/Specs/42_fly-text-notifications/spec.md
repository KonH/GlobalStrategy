# Spec: Fly Text Notifications

## Feature Intent

As a player, I want brief, non-blocking floating text notifications to appear on screen when the game confirms an outcome of an action (e.g. a manual save completing), so that I get lightweight feedback without an interruption that requires dismissal or blocks further interaction.

## Acceptance Criteria

- **Given** the player is in the main menu, in-game HUD, or the in-game pause (game) menu **When** a fly text notification is requested with a localization key and optional format parameters **Then** the resolved, localized text appears on screen as the front-most element, rendered on a dedicated top-most UI layer (its own `PanelSettings`/`UIDocument`, sort order above the existing Modal layer) so it always draws above all other currently visible UI content in that scene.
- **Given** a fly text notification is triggered **When** it appears **Then** it plays a short scale-up entrance animation (starts smaller than target size and grows to full size) before settling.
- **Given** a fly text notification has finished its entrance animation **When** its display/hold duration elapses **Then** it animates by simultaneously moving downward, scaling down, and fading its alpha to zero, then is removed from the visual tree.
- **Given** the animation timing is unspecified by earlier discussion **When** implementing the entrance/hold/exit phases **Then** use fast timing as the default: ~0.2s scale-up, ~1.5s hold, ~0.5s exit (move+scale+fade), ~2.2s total — adjustable later without a spec change since these are implementation constants, not behavioral contracts.
- **Given** a fly text notification is currently animating or visible **When** the player clicks or otherwise interacts with world/UI elements underneath it **Then** the notification does not block or intercept that input — its root element and all children have `PickingMode.Ignore` applied recursively.
- **Given** the notification service is requested from any scene context (main menu via `StaticGameLogic`, or in-game via full `GameLogic`) **When** a caller injects and calls it **Then** each scene's lifetime scope (`MainMenuLifetimeScope`, `GameLifetimeScope`) registers and owns its own instance of the service, exposing the same public API/interface in both scopes so callers do not need scope-specific logic. Notifications do not persist across scene transitions — an in-flight notification is discarded when the owning scene unloads.
- **Given** the localization key supplied to the notification includes optional parameter values **When** the text is resolved **Then** the notification service calls the existing `ILocalization.Get(key)` unchanged, then applies `string.Format` (or equivalent) itself using the supplied parameters — no change to the `ILocalization` interface is required.
- **Given** a fly text notification is triggered while another is still visible or animating **When** the new request is made **Then** it is queued and shown after the current one finishes its full animation (entrance, hold, exit) — only one fly text is ever visible at a time per scene.
- **Given** the player performs a manual save from the game menu **When** the save completes successfully **Then** a fly text notification is shown using a save-confirmation localization key (e.g. `game_menu.save.confirmation` or equivalent), wired through the game menu's existing manual-save confirmation flow.
- **Given** the active locale changes while a fly text notification is queued or visible **When** the locale change happens **Then** the currently visible/animating notification finishes displaying its originally-resolved text (not re-localized in place); only notifications resolved after the change use the new locale. This must not crash or throw.

## Out of Scope

- The manual save button/logic itself (already implemented) — this spec only covers wiring a notification call into the existing confirmation flow, not building or changing the save flow.
- Any notification types other than short-lived, non-interactive fly text (no persistent toasts, no notification history/log, no click-to-dismiss).
- Sound/haptic feedback accompanying the notification.
- Notifications triggered from any flow other than the manual save confirmation (this spec establishes the mechanism and its first caller only).
- Localization authoring workflow changes (adding new keys to `en.asset`/`ru.asset` follows the existing process; this spec does not change that process).
- Persisting or queuing notifications across scene loads — a scene transition discards any pending/visible notification for that scene's instance.
- Stacking multiple simultaneously-visible notifications — only queuing (one at a time) is supported.
