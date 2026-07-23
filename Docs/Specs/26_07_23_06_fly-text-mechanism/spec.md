# Spec: Fly Text Mechanism (Common)

## Feature Intent

As a player, I want every lightweight outcome confirmation in the game (country discovery, save result, delete-all-saves result, opinion change, control change) to use one consistent floating-text notification mechanism, so that every confirmation looks and behaves the same and a new confirmation type can be added later with a single call, not new UI or animation code.

## Acceptance Criteria

### Common mechanism

- **Given** any of the five call sites below requests a fly text notification **When** it is shown **Then** it renders on the existing dedicated top-most fly-text layer (`FlyTextNotifierDocument`, currently on `HUDPanelSettings.asset` with `sortingOrder = 1000`) so it is always the front-most element in that scene — the same guarantee the save-result notification already has today.
- **Given** the shared mechanism today plays a scale-up entrance and a move-down/scale-down/fade exit (the save-result look) **When** this spec's visual is implemented **Then** it is replaced with a plain fade-in → hold → fade-out shape (opacity only, no scale, no translate) matching how country-discovery's notification looks today. This new visual becomes the one and only fly text look going forward, used by all five call sites. Exact durations are an implementation detail for the plan, not a behavioral contract here.
- **Given** a fly text notification is requested while another is visible or animating **When** the new request is made **Then** it queues and plays only after the current one fully finishes — unchanged from today's save-result queueing, now applying uniformly to all five call sites (including discovery, which has no queueing today because only one card-play can be in flight at a time).
- **Given** a fly text notification's content is a localization key plus optional format args (discovery, save result, delete-all-saves) **When** it is resolved **Then** it behaves exactly as today: `ILocalization.Get(key)` + `string.Format`, resolved at call time so a later locale change does not affect an already-queued/visible item.
- **Given** a fly text notification's content is a pre-built rich-text string (opinion/control change effect) **When** it is shown **Then** `<b>`/`<color>` markup renders as highlighted text, not literal tag characters — the notifier gains a rich-text-capable path alongside its existing plain-text path. Plain-text call sites are unaffected and never have their resolved text interpreted as markup.
- **Given** the notification service is requested from any scene context **When** a caller injects and calls it **Then** it behaves exactly as today: each scene's lifetime scope registers its own instance behind the shared notifier interface, with no cross-scene persistence.
- **Given** a future feature needs another fly-text call site **When** a developer wires it up **Then** it requires exactly one call into the shared notifier (the localization-key form or the rich-text form) and zero new UI, animation, or layer code — the same bar `game_menu.save.confirmation` already meets today.

### Per call site

- **Given** a player successfully discovers a country via a card action **When** the discovery completes **Then** a fly text notification is shown through the shared mechanism — not `CardPlayAnimator`'s private `"fly-text"` `Label` and hand-rolled fade loop — using a localization key formatted with the discovered country's localized name. This replaces today's hardcoded, non-localized `"Discovered: {name}!"` string and retires the bespoke code path entirely.
- **Given** a player performs a manual save **When** it completes (success or failure) **Then** the existing `game_menu.save.confirmation` / `game_menu.save.error` fly text continues to fire on the same trigger with the same content; only its visual changes to the new shared fade-only look.
- **Given** a player deletes all saves from the settings window **When** the delete operation completes **Then** a fly text notification is shown with an "Operation completed"-equivalent localized message — the first feedback of any kind for this action, which gives none today.
- **Given** a game-log entry of kind `Control` is newly added to `GameLogState` **When** it appears **Then** a fly text notification is shown using the same highlighted rich-text string `ActionLogView.BuildControlLine` already produces for that entry (org/country name colored per their configured visual config) — reused as-is, not re-derived or simplified.
- **Given** a game-log entry of kind `Opinion` is newly added to `GameLogState` **When** it appears **Then** a fly text notification is shown using the same highlighted rich-text string `ActionLogView.BuildOpinionLine` already produces for that entry (org/country name colored, role name bold) — reused as-is.

## Out of Scope

- Exact animation timing constants (fade-in/hold/fade-out durations) and exact DI/trigger wiring code — implementation detail for the plan.
- Any new `PanelSettings`/`UIDocument` asset — the existing top-most fly-text layer is reused unchanged in ownership/registration structure.
- Error/failure feedback for delete-all-saves — this spec covers only the single "Operation completed" success message the user asked for; `SaveFileManager.DeleteAllSaves` gains no new error signaling here.
- Fly text for the remaining `GameLogEntryKind` (`Discovery` is handled via its own dedicated call site above with different wording; `NewCharacter`) — not requested, no fly text added for it.
- Rate limiting, coalescing, or batching when several log entries (e.g. simultaneous opinion + control changes from one action) queue several fly texts in quick succession — each entry queues and plays independently in order; UX tuning for bursts is a future concern, not solved here.
- Any change to `ActionLogView`'s own scrolling log panel — it keeps building and rendering entries exactly as today; this spec only reuses its already-built strings.
- Sound/haptic feedback, click-to-dismiss, notification history — same permanent exclusions as the original fly-text-notifications spec.
- Stacking multiple simultaneously-visible notifications — only queuing (one at a time) is supported.
