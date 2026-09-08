# Spec: UI Animation Foundation

## Feature Intent

As a player, I want existing UI animations to remain smooth, correctly timed, and safely interruptible through game pauses and HUD rebuilds, so that card flows, tutorial guidance, and action-log feedback remain visually reliable.

As a UI maintainer, I want imperative `VisualElement` interpolation to use one small project-owned abstraction while declarative state transitions remain in USS, so that the current animation code is consistent without adding a general-purpose tween dependency prematurely.

## Acceptance Criteria

### Shared imperative interpolation

- An imperative UI animation starts with a positive duration.
  - Each update advances from actual unscaled elapsed time rather than assuming a fixed frame or scheduler interval.
  - Normalized progress remains clamped from 0 through 1 and the successful final update writes the exact requested endpoint.
  - A caller can select an easing function; linear interpolation remains available for behavior-preserving migrations.
- An imperative UI animation starts with a zero or negative duration => the endpoint is applied immediately and the operation completes without waiting for another frame.
- An animation is cancelled while it is running.
  - Interpolation stops promptly and performs no later writes.
  - Expected cancellation is handled as normal flow termination rather than being logged as an animation failure.
  - The owning flow's existing cleanup remains responsible for restoring hidden real elements, removing temporary copies, and releasing modal/pause ownership.
- The animated `VisualElement` becomes detached, stale, or is replaced during a HUD reload => interpolation terminates safely and does not mutate the detached element on later frames.
- Card transition and card draw code require imperative position or scale interpolation => both delegate normalized progress and lifetime handling to one small helper in the Unity UI layer rather than owning separate per-frame loops.
- The helper remains narrowly scoped to the interpolation currently needed by the project; it does not become a timeline, sequence editor, global animation service, or replacement for UniTask orchestration.

### Card transition and draw behavior preservation

- An organization or country card-play transition runs => its current sequence, duration, source and destination bounds, card face, temporary-copy behavior, input suppression, result wait, value barriers, and cleanup behavior remain unchanged from the player's perspective.
- A country-card draw, paid discard, return, receipt, flip, or hover animation runs => its current ordering, duration, destination, flip behavior, hover scale, selection locking, modal ownership, and pause ownership remain unchanged from the player's perspective.
- A card flow is interrupted by cancellation, HUD disable, destruction, or UI reload.
  - Its active interpolation receives cancellation.
  - No temporary card remains visible after cleanup.
  - Any real card hidden for the transition is restored.
  - `SuppressRefresh`, presentation-busy state, modal ownership, and flow-owned pause state are released by the existing owning flow.
- A hover-in is superseded by hover-out, or the pointer moves between draw choices quickly => the latest hover generation remains authoritative and an older animation cannot overwrite the newer scale.
- A card copy travels across the HUD => its stable absolute anchor is established once and UI Toolkit transform properties are used for per-frame movement where practical, avoiding repeated layout-coordinate writes while preserving correct final `worldBound` behavior for subsequent steps.

### Tutorial highlight timing

- A tutorial task identifies a valid highlight target => the arrow preserves its current side-selection rules, orientation, bounce curve, 14-pixel travel amplitude, and 1.4-second cycle.
- Scheduled highlight callbacks arrive late or at uneven intervals => animation phase advances using actual unscaled elapsed time, so callback delay does not proportionally slow the bounce cycle.
- The highlight is hidden and later shown again => phase and timestamp state reset without a large first-frame jump, and the schedule does not continue mutating a hidden arrow.
- The highlighted target moves while the arrow is active => the existing repeated target-bound resolution and positioning continue to track it.

### Native transition lifecycle

- An action-log entry is added => its existing identity-keyed incremental rendering and 0.25-second opacity fade-in remain unchanged.
- An action-log entry leaves projected state => its 0.6-second fade-out completes before the label is removed from the visual tree.
- An action-log fade-out ends or is cancelled => removal is driven by the filtered opacity transition lifecycle for that label, not by a separately calculated duration timer; registered callbacks are removed during cleanup.
- A different transition property or descendant raises a transition event => it cannot remove the action-log label accidentally.
- Existing panel slides, overlays, and other declarative class/style transitions => they remain native USS transitions and are not routed through the imperative helper.

### Existing animated display values

- Gold, control, or opinion changes use an `AnimatableDouble` or `AnimatableInt` barrier => existing hold offsets, integer counting, parallel release, cancellation, property-change notifications, and game-state separation remain unchanged.
- The UI animation foundation is introduced => no third-party tween package, new gameplay dependency, or Unity dependency is added to `src/` assemblies.

### Verification

- The implementation compiles with the project's Unity assemblies and introduces no Unity Console errors.
- Gallery or live-Editor verification covers organization/country card travel, draw deal/flip/hover/return/receipt, paused-game timing, interruption or HUD rebuild cleanup, tutorial-arrow timing, and action-log fade removal.
- Existing gameplay, state, and non-Unity test suites remain unchanged unless a test needs a behavior-preserving update for the new helper boundary.

## Tech Notes

### Helper boundary

- Add an internal project-owned helper under `Assets/Scripts/Unity/UI/`, in the existing `GS.Unity.UI` assembly. UniTask is already referenced by that assembly.
- Prefer one normalized-progress core that accepts duration, cancellation, easing, and an update callback, with only the narrowly required `VisualElement` position/translate and scale adapters around it.
- Use `Time.unscaledDeltaTime` or an equivalent unscaled elapsed-time source. Zero-duration completion, exact endpoint assignment, cancellation, and element attachment checks belong in the shared implementation rather than each caller.
- Do not put this helper under `src/`: it is Unity UI presentation infrastructure and depends on `VisualElement`/Unity timing.

### Card integrations

- Migrate the manual loops in `CardTransitionView.AnimateCard`, `CardDrawView.AnimateScaleAsync`, `AnimateHorizontalScaleAsync`, and `AnimatePositionAsync` to the shared helper.
- `CardDrawView` already receives a flow `CancellationToken`; forward it while preserving `HoverGeneration` as an additional supersession condition.
- `CardTransitionView` currently has no cancellation token and uses scaled delta time. Thread a flow token through `Show`/`ShowCountry` and the `CardPlayAnimator` sequence, cancel it on disable/destruction/UI replacement, and treat expected `OperationCanceledException` as cleanup.
- For travel transforms, set the absolute anchor once, interpolate `style.translate`, then bake/reset the successful endpoint if later `worldBound` reads require it. Preserve layout and overlay coordinate conversion.
- Do not replace the higher-level UniTask orchestration in `CardPlayAnimator` or `CardDrawAnimator`: command waits, modal locks, pause ownership, state refresh suppression, sequencing, and authoritative-result checks are outside the tween helper.

### Tutorial and action log

- `TutorialHighlightView` may keep its scheduled callback because it must continuously resolve and follow a moving target. Derive phase delta from an unscaled timestamp rather than `AnimIntervalMs / CycleDurationMs`, and reset the timestamp in `Hide`/restart paths.
- In `ActionLogView`, register filtered `TransitionEndEvent` and `TransitionCancelEvent` callbacks before starting fade-out, and unregister both when the label is removed. The short scheduled fade-in trigger may remain because it establishes distinct initial and target style states; it is not being used to guess completion.

### Dependency threshold

- Do not add PrimeTween, DOTween, LitMotion, or another tween library in this change.
- Reconsider PrimeTween separately if the project later gains materially more animated screens, overlapping/staggered sequences, reusable punch/shake effects, or designer-authored animation requirements that would make a dedicated library smaller than the growing project-owned abstraction.

## Out of Scope

- Adding or evaluating a third-party tween package as part of implementation.
- Replacing native USS transitions or the `AnimationBarrierInt`/`AnimationBarrierDouble` display-value system.
- Migrating `FlyTextNotifierDocument`, every scheduled callback, or every existing style transition to the new helper.
- Changing animation timings, easing appearance, card flow ordering, tutorial-arrow visuals, or action-log presentation.
- Adding new animations, sound, particles, haptics, shakes, punches, or designer tooling.
- Changing gameplay commands, authoritative state, pause semantics, save data, ECS systems, or Web client behavior.
- Building a general-purpose tween engine, global registry, inspector authoring system, or reusable sequence graph.

## Resolved Decisions

The owner approved the hybrid approach before specification:

1. Keep simple state-driven animations in native USS transitions.
2. Keep animated game-display barriers project-owned and independent of UI tween infrastructure.
3. Introduce only a small UniTask-based custom helper for duplicated imperative `VisualElement` interpolation.
4. Use unscaled elapsed time, cancellation, easing support, exact endpoints, and safe detach behavior in that helper.
5. Prefer UI Toolkit transforms over per-frame layout-coordinate writes where practical.
6. Correct tutorial timing and action-log completion handling in the same focused cleanup.
7. Add no tween library now; PrimeTween is the preferred option to reconsider only if animation volume or authoring complexity grows materially.
