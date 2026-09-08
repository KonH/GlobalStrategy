# Plan: UI Animation Foundation

## Spec

Source: `Docs/Specs/26_09_08_14_ui-animation-foundation/spec.md`.

The approved scope is a hybrid animation foundation: retain native USS transitions for declarative class/style changes; retain `AnimatableInt`, `AnimatableDouble`, and their animation barriers as project-owned display-state infrastructure; add one small UniTask-based helper for imperative `VisualElement` interpolation; migrate the duplicated card position/scale loops; correct tutorial-arrow elapsed timing; and remove the action log's duration-based removal guess. No third-party tween package is added.

## Goal

Make the existing UI animations consistently unscaled, cancellation-safe, detach-safe, and exact at their endpoints without changing their visible timings, sequencing, modal/pause ownership, or gameplay behavior. Centralize only normalized frame progression and narrowly related transform interpolation, leaving command/result orchestration in the existing animators.

## Approach

### Current animation and lifecycle inventory

The implementation surface is limited and has clear ownership boundaries:

- `Assets/Scripts/Unity/UI/CardTransitionView.cs` owns a shared temporary card copy, waits on one uncancellable `GeometryChangedEvent`, and advances a linear `left`/`top` loop with `Time.deltaTime`. `Show` and `ShowCountry` have no cancellation token.
- `Assets/Scripts/Unity/UI/CardDrawView.cs` owns separate unscaled loops for uniform scale, horizontal flip scale, and position. Its surrounding `CardDrawAnimator` already supplies a per-flow cancellation token and generation-safe cleanup.
- `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` orchestrates card transitions with uncancellable delays/next-frame waits and a scaled `Time.time` result deadline. `OnDisable` unsubscribes state but does not cancel an active play. `OnUIReload` replaces `_transitionView` while an old async continuation may still be running. Both play sequences also expose `card-test-overlay` and hide its real source card before the first await, so interruption must restore that presentation explicitly.
- `Assets/Scripts/Unity/UI/CardPlayBarriersHolder.cs` waits for released barriers without cancellation. If a card flow is interrupted after release and `CancelAll` removes the barrier from its animatable, that wait can otherwise remain pending because the removed barrier is no longer ticked to completion.
- `Assets/Scripts/Unity/UI/HUDDocument.cs` already cancels and awaits `CardDrawAnimator` across disable/enable, but `OnUIReload` discards the draw animator/view references synchronously without first waiting for old flow cleanup. It also does not coordinate teardown with an active `CardPlayAnimator`.
- `Assets/Scripts/Unity/UI/TutorialHighlightView.cs` schedules approximately every 16 ms but advances phase by a fixed `16 / 1400`, so delayed callbacks stretch the visible cycle.
- `Assets/Scripts/Unity/UI/ActionLogView.cs` uses a native opacity transition but removes an evicted label through `ExecuteLater(FadeOutSeconds * 1000)` rather than the transition's own lifecycle.
- `Assets/Scripts/Unity/Gallery/HandDeckGalleryBlocks.cs` calls the public card-transition API with a zero duration to produce a static preview and must continue landing immediately at the destination.
- `.claude/rules/unity/uitoolkit.md` currently documents the action-log timer-removal pattern. It must be updated with the new lifecycle rule and the narrow hybrid animation boundary or it will instruct future work to recreate the code being removed.

No changes are expected under `src/`, in UXML/USS assets, in localization, or in scene/prefab assets. `FlyTextNotifierDocument`, existing panel-slide USS transitions, `AnimationBarrierInt`/`AnimationBarrierDouble`, and higher-level game-command sequencing stay outside this refactor.

### Add one narrow `UiTween` helper

Create `Assets/Scripts/Unity/UI/UiTween.cs` in the existing `GS.Unity.UI` assembly. It is a static presentation helper, not a DI service, global registry, component, new assembly, or editor-authored animation system.

The core API should accept:

- the animated `VisualElement`;
- duration;
- an `Action<float>` that receives normalized/eased progress;
- the owning `CancellationToken`;
- an optional easing function, with linear as the behavior-preserving default;
- an optional `isCurrent` predicate for non-error supersession such as draw-card hover generations.

The core owns the repeated mechanics:

- reject a null element/update callback with a contextual argument exception;
- check caller cancellation, current-generation status, and panel attachment before every write and after every awaited frame;
- advance with `Time.unscaledDeltaTime`, clamp normalized progress, and apply the exact eased endpoint once on successful completion;
- apply the endpoint synchronously for zero/negative durations when the element is current and attached;
- register for `DetachFromPanelEvent` during a running tween and link detachment to controlled cancellation, then unregister/dispose in `finally` so a stale element cannot receive later writes;
- return normally without applying the endpoint when `isCurrent` reports supersession; propagate `OperationCanceledException` for owner cancellation/detachment so the owning flow's established cleanup path runs.

Keep interpolation adapters minimal. `MoveAsync` establishes `left`/`top` at the source once, animates a pixel `style.translate` delta, then on successful completion bakes `left`/`top` to the destination and resets translate to zero. It never bakes a cancelled/superseded motion. `ScaleAsync`, or an update callback over the normalized core where caller-side cached scale must be updated, continues to write UI Toolkit `style.scale`. This removes per-frame layout-coordinate changes while preserving correct final `worldBound` values for later sequence steps.

Unity will generate `UiTween.cs.meta` on import; keep the sidecar with the implementation. Do not add a test-only runtime API, generalized sequence graph, custom update manager, inspector tooling, or new tween dependency.

### Migrate card draw without changing its flow contract

Replace the bodies of `CardDrawView.AnimateScaleAsync`, `AnimateHorizontalScaleAsync`, and `AnimatePositionAsync` with `UiTween` delegation. Preserve all existing timing constants, sequential `foreach` ordering, face swap at horizontal scale zero, 1.3 hover target, selection lock, overlay coordinate conversion, and temporary-copy cleanup.

`ChoiceCopy.Scale` must be updated from the active tween callback so a reversing hover starts from the actual current scale. Pass a predicate comparing the captured hover generation with `copy.HoverGeneration`; superseded hover work exits normally and cannot write an obsolete endpoint. Flow cancellation/detachment still propagates to `CardDrawAnimator`, whose existing `catch (OperationCanceledException)` and generation-owned `finally` perform cleanup.

Harden `WaitForSlotGeometryAsync` at the same boundary: keep its real-time two-second timeout for an attached element that never receives usable geometry, check cancellation each iteration, and terminate through expected cancellation if the overlay or any tracked slot detaches after animation ownership begins. Do not read stale slot bounds following a HUD reload.

### Make card transitions local-copy and cancellation safe

Thread a required `CancellationToken` through `CardTransitionView.Show`, `ShowCountry`, geometry waiting, and movement. Update every shipping caller explicitly; the Gallery's zero-duration preview passes `CancellationToken.None`.

Do not animate through mutable `_cardCopy`. Each invocation captures its own card copy and linked cancellation source. Starting another transition or calling `Hide` cancels the view-owned active operation before removing its copy. Completion/finalization may clear shared fields only when they still refer to that invocation, so an older continuation cannot move or hide a newer copy.

Replace the current geometry completion source with a cancellation- and detachment-safe wait:

- register filtered geometry and detach callbacks plus caller cancellation before or around adding the copy;
- immediately recheck attachment and nonzero geometry after registration to close the missed-event race;
- use one guarded completion path;
- unregister callbacks and dispose registrations in `finally`;
- retain a bounded real-time timeout with a contextual error for an attached copy that never lays out.

Validate the destination immediately before reading `worldBound`, then move the local copy through `UiTween.MoveAsync`. `Hide` remains idempotent and cancellation is treated as flow interruption, not a logged failure.

### Give card play explicit flow ownership

Add a per-play cancellation/completion context to `CardPlayAnimator`. Both start methods create it only after the existing `_isPlaying` guard and capture the current transition view, the real source element, `card-test-overlay`, `card-test-card`, and the one organization/country actions view whose refresh suppression that flow changes. Capture that view's prior `SuppressRefresh` value rather than assuming it was false. Expose `CancelAndWaitAsync`, matching the serialized lifecycle pattern already used by `CardDrawAnimator`.

Pass the flow token through:

- all `CardTransitionView` calls;
- result polling;
- fixed delays;
- next-frame layout waits;
- `CardPlayBarriersHolder.Animate` waits.

Use unscaled delays and `Time.realtimeSinceStartup` for the bounded result deadline while retaining the current 330 ms polling cadence, 10-second bound, and all visible card durations. Extend `CardPlayBarriersHolder.Animate` with a cancellation token and pass it to each `UniTask.WaitUntil`; do not change hold offsets, release duration, integer counting, or parallel barrier behavior.

Cancel the active play on `OnDisable`, `OnDestroy`, and before `OnUIReload` replaces the transition view. Each sequence catches expected `OperationCanceledException`; its matching `finally` remains the only cleanup owner and must:

- cancel any remaining barriers and pending barrier waits;
- hide only the captured transition view;
- hide/reset the captured `card-test-overlay` and restore the captured test card's opacity when they still belong to the flow's old root, including cancellation during the inbound card transition before the normal overlay-hide point;
- restore the hidden source card if it is still attached, otherwise allow the authoritative refresh to rebuild it;
- restore only the specific actions view captured by this flow to its prior `SuppressRefresh` value; never write suppression through the animator's current view fields after a rebind;
- unlock this flow's modal ownership;
- emit `UnpauseCommand` only if this flow introduced the pause;
- clear `_isPlaying`, dispose/complete only the matching flow context, and raise `OnCardPlayComplete` exactly once.

Do not change action command ordering, authoritative result interpretation, org-card replacement behavior, country-card vacancy behavior, barrier keys/offsets, or animation timings.

### Treat HUD reload as an async teardown boundary

`HUDDocument.OnUIReload` must not null old view/animator references until their asynchronous owners have finished cleanup. Add a reload generation and retain the incoming `rootElement`. Synchronously stop new work first: unsubscribe the old view events and binders, disable draw-offer restoration, and end any resume barrier, while retaining every old view/animator reference needed by cleanup. Then request cancellation from the captured old `CardDrawAnimator` and `CardPlayAnimator`, await both, and only afterward null old references and bind the root for the still-current generation.

An initial reload with no active owners can bind immediately because both cancellation tasks are already complete. If another reload arrives while cancellation is pending, the older continuation must observe the generation mismatch and return without clearing or binding anything. Old flow cleanup must finish against captured old views/sorting/modal ownership before new `CountryActionsView`, `ActionLogView`, or draw/transition objects are exposed to interaction. State notifications cannot reach the detached old views during the wait because their subscriptions were removed synchronously. Preserve the existing `OnDisable`/`ResumeAfterEnableAsync` behavior and authoritative pending-offer restoration after a successful current-generation rebind.

Remove the competing reload ownership from `CardPlayAnimator`: replace its independent `PanelRenderer.RegisterUIReloadCallback` path with an internal root-binding method called by `HUDDocument` only after the old play/draw completions finish for the current generation. `CardPlayAnimator` still cancels itself on disable/destroy, but `HUDDocument` becomes the sole coordinator that installs a new `CardTransitionView` and new actions-view references. This makes callback order irrelevant: an active flow always owns captured old elements, and no new root becomes interactive until its generation wins the async rebind gate.

### Correct tutorial elapsed timing without replacing its scheduler

Keep `TutorialHighlightView` scheduled target lookup and positioning: the arrow must continue following a moving target and selecting the best side every callback. Record an unscaled timestamp such as `Time.realtimeSinceStartupAsDouble`, calculate elapsed seconds since the prior callback, and advance phase by `elapsed / 1.4` instead of `16 / 1400`.

The first callback after starting uses zero elapsed time. Update the stored timestamp even when the configured target is temporarily unresolved so target reattachment does not accumulate an artificial jump. `Hide` pauses the scheduled item and resets both phase and timestamp. Preserve the current bounce functions, rotation, side order, arrow gap/size, amplitude, and cycle duration.

### Remove the action-log completion guess

Keep `ActionLogView`'s identity-keyed incremental diff, immediate dictionary eviction, inline native opacity transition configuration, 0.25-second fade-in, 0.6-second fade-out, and short scheduled trigger that establishes distinct fade-in style states.

Change the rendered-entry value from a bare label to a small entry that also retains the 20 ms fade-in scheduled item. On eviction, pause that pending item before starting fade-out; if the label is detached or its resolved opacity is already at the terminal invisible value, clean it up immediately because no opacity transition can run.

For a visible or partially visible label, register `TransitionRunEvent`, `TransitionEndEvent`, and `TransitionCancelEvent` callbacks before setting opacity to zero. The run event arms this specific fade-out only after the new opacity transition begins. This prevents the `TransitionCancelEvent` raised when an in-progress fade-in is interrupted from being mistaken for cancellation of the new fade-out. Route the armed end/cancel events through one idempotent cleanup closure that:

- accepts only events whose target is that label and whose `stylePropertyNames` contains `opacity`;
- ignores descendant and unrelated-property events;
- ignores end/cancel until this eviction's fade-out has observed its own filtered run event;
- unregisters run/end/cancel/detach callbacks and pauses any retained scheduled item before removing the label;
- tolerates the label or its parent already being detached.

Also register detach cleanup and schedule a next-frame, state-based fallback that removes the entry only if no fade-out run event occurred and the label has already resolved to terminal opacity; this is not a guessed duration. Remove the fade-out `ExecuteLater` duration calculation entirely. Update `.claude/rules/unity/uitoolkit.md` so incremental animated lists require an armed, filtered transition lifecycle plus a no-transition cleanup path instead of duplicated timeouts. Add a concise animation-boundary rule: native USS transitions for declarative state changes, `UiTween` only for awaited imperative transform interpolation, and project-owned animation barriers remain separate display-state infrastructure.

### Automated and live verification strategy

There is currently no Unity test assembly under `Assets/`, and the relevant guarantees depend on real player-loop timing, UI Toolkit panel attachment/detachment, transition event dispatch, and HUD flow cleanup. Do not add a broad Editor-test assembly or expose the internal helper publicly solely for synthetic unit access. Validate through the existing compile, Gallery, and live E2E surfaces; keep the implementation structured so a dedicated UI test assembly can be introduced separately if its broader ownership and execution policy are defined.

Before any Editor, Unity MCP, or E2E work, run the `unity-plugins` skill so the gitignored Core DLLs exist. After each C# batch, refresh Unity, wait for compilation, and read console errors. After Editor work, restore `Assets/UI/Fonts/*.asset` as required by project policy.

Use Gallery previews to verify the duration-zero `CardTransitionView`, the draw-choice surface/rapid hover response, tutorial tracking, and action-log presentation. Use the `unity-e2e-run` workflow with a scratch script based on `new_game_to_map` plus command/click/capture steps to exercise organization/country card play and country draw while running and already paused. Capture post-flow UI state and require no console error, stuck modal lock, hidden real card, or leftover temporary copy. Timing smoothness, transform-bake pops, deliberate UI reload interruption, and transition-end visual removal remain explicit manual Unity User Steps.

## Agent Steps

- [ ] **Add the shared UI interpolation helper** — create `Assets/Scripts/Unity/UI/UiTween.cs` (and retain its Unity-generated `.meta`) with normalized unscaled progression, exact endpoint application, optional easing, optional supersession predicate, caller/detach cancellation, callback cleanup, and narrow translate/scale support described above. Keep it in `GS.Unity.UI`; add no package, DI registration, component, new runtime assembly, or `src/` dependency.
- [ ] **Migrate `CardDrawView`** — replace its three manual frame loops with `UiTween` calls; preserve every duration, sequential phase, face swap, cached `ChoiceCopy.Scale`, overlay coordinate conversion, and selection behavior; forward the existing flow token and use `HoverGeneration` as a supersession predicate; harden slot-geometry waiting against cancellation/detachment without changing the attached-layout timeout.
- [ ] **Harden and migrate `CardTransitionView`** — require cancellation in `Show`/`ShowCountry`; use invocation-local copies and linked ownership; make geometry waiting bounded, callback-clean, cancellation-aware, and detachment-aware; validate destination attachment; replace per-frame `left`/`top` animation with translate-and-bake movement; make `Hide` cancel/remove only its active invocation.
- [ ] **Make card-play orchestration cancellable** — add per-flow cancellation/completion ownership and `CancelAndWaitAsync` to `CardPlayAnimator`; capture the old transition/source/test-overlay/test-card elements plus the exact suppressed actions view and its prior value; pass the token through transitions, delays, polling, next-frame waits, and barrier waits; use unscaled/realtime timing; cancel on disable/destroy and when requested by HUD reload; preserve commands, result handling, timings, modal/pause semantics, barriers, and completion notification while making cleanup invocation-safe and resetting the captured test overlay on every exit.
- [ ] **Cancel barrier waiters with their owning play** — update `CardPlayBarriersHolder.Animate` to accept the flow token and use cancellable `WaitUntil` calls, while leaving all `Animatable*`/`AnimationBarrier*` state and release behavior untouched.
- [ ] **Serialize HUD reload teardown** — make `HUDDocument` the sole root-rebind coordinator: immediately unsubscribe old view events/binders, generation-guard the pending root, cancel/await captured old draw and play owners before nulling/rebinding views, and call a new internal `CardPlayAnimator` root-binding method only for the winning generation. Remove `CardPlayAnimator`'s independent panel-reload subscription, preserve disable/enable resume barriers, and restore authoritative pending draw presentation only after the new root owns the HUD.
- [ ] **Fix tutorial phase progression** — update `TutorialHighlightView` to advance from actual unscaled elapsed timestamps, including first-tick and temporarily missing-target handling, while retaining its scheduler, target tracking, side selection, bounce curve, amplitude, orientation, and hide/reset behavior.
- [ ] **Use action-log transition completion** — retain each label's pending fade-in scheduled item; pause it on eviction; immediately clean detached/already-invisible entries; otherwise arm fade-out with a filtered `TransitionRunEvent` before accepting its filtered one-shot end/cancel callbacks; add detach and next-frame terminal-state cleanup for the no-transition path; delete the fade-out duration timer while retaining incremental identity diffing and existing fade durations.
- [ ] **Update animation guidance** — revise `.claude/rules/unity/uitoolkit.md` to document filtered transition lifecycle completion and the hybrid USS/`UiTween`/animation-barrier boundary, keeping future implementations aligned with this refactor.
- [ ] **Update callers and compile** — pass tokens at every `CardTransitionView`/`CardPlayBarriersHolder` call site, including `HandDeckGalleryBlocks` with `CancellationToken.None`; run `unity-plugins` before the first Unity action; refresh Unity after script changes, wait for compilation, read Console errors, and fix all compiler/import errors; restore `Assets/UI/Fonts/*.asset` after Editor use.
- [ ] **Run regression verification** — run the existing .NET test suite to confirm no gameplay regression; exercise Gallery blocks for transition/draw/tutorial/action-log surfaces; run scratch E2E flows for running and already-paused organization/country card play and draw; interrupt both before and after the test overlay becomes visible; confirm no stale overlay/copy/opacity, wrong-view suppression write, modal lock, pause ownership, or uncancellable task remains. No Release Core rebuild is required unless implementation unexpectedly changes `src/`.

## User Steps

### 1. Verify motion and transform baking in Unity Editor

In Play mode, visually inspect organization-card and country-card travel plus a three-card draw, including rapid pointer movement between choices. Repeat while the game is already paused. Confirm timings and ordering look unchanged, hover reverses from its current scale without snapping, and no visible pop occurs when an arriving card's translate is baked into its final `left`/`top` anchor.

### 2. Verify interruption and HUD reload recovery

While a card is travelling or a draw offer is animating, deliberately reload the UI document or disable/re-enable the `GameHUD` object in the Editor. Confirm temporary cards disappear, any hidden real card returns, the rebuilt HUD is interactive, pending authoritative offers restore correctly, and no modal lock, refresh suppression, presentation-busy flag, sorting-order override, or flow-owned pause remains stuck.

### 3. Verify scheduler hitch and transition completion visually

Watch an active tutorial highlight through an intentional Editor hitch and confirm its cycle catches up from elapsed time without a large reattachment jump. Let an action-log entry age out and confirm it remains through the full visible fade, then disappears exactly when the opacity transition terminates.

## Tests

- Run `dotnet test src/GlobalStrategy.Core.sln` (via the `dotnet-test` skill) as a gameplay/state regression check. No `src/` behavior should change.
- Before Unity compilation or live verification, run the `unity-plugins` skill. Refresh Unity after each C# batch, wait for compilation/import, and read all Console errors.
- Verify `CardTransitionGalleryBlock` still renders its duration-zero card at the exact destination and produces no forgotten-task error.
- Verify the card-draw Gallery surface preserves stable one-/two-/three-card placement and that rapid hover enter/leave does not let an older scale endpoint win.
- Verify `HudTutorialHighlightGalleryBlock` keeps tracking and bouncing with the same orientation/amplitude, and `HudActionLogGalleryBlock` retains its existing styling.
- Run E2E scratch coverage from `new_game_to_map` for organization and country play plus explicit country draw in running and already-paused states. Capture after completion and require a clean Console with usable HUD input and no visible temporary copy.
- Exercise cancellation during inbound card travel, while `card-test-overlay` is visible, during outbound travel, and during draw presentation. Confirm the captured old overlay is hidden/reset, the current root—not a superseded root—owns the rebuilt UI, and pause/modal/specific-view-suppression/hidden-element state is restored.
- Evict an action-log entry before its 20 ms fade-in starts, during fade-in, and after it is fully visible. Confirm the pending fade-in cannot resurrect an evicted label, cancellation of the old fade-in does not skip the new fade-out, and detached/no-transition entries are removed without a duration guess.
- Search after implementation for manual elapsed loops in `CardTransitionView`/`CardDrawView` and for fade-out `ExecuteLater((long)(FadeOutSeconds * 1000))`; both duplicated patterns must be gone. Confirm no tween package was added to `Packages/manifest.json`.
- If any implementation change lands under `src/`, stop treating this as Assets-only: run the mandatory `dotnet-build Release` workflow and do not stage generated `Assets/Plugins/Core` binaries.

## Constitution Check

- **Rendering:** no render-pipeline, material, shader, or camera change; URP remains untouched.
- **Game logic:** all production changes stay in Unity presentation code under `Assets/Scripts/Unity/UI` plus its Gallery caller and documentation. Commands, authoritative state, ECS systems, pause rules, and `src/` animation barriers remain unchanged.
- **Dependency injection:** `UiTween` is a stateless helper, not a singleton service. Existing animator/view ownership and VContainer composition remain; no service locator, static mutable state, or object search is added.
- **UI:** all animation continues through UI Toolkit `VisualElement` styles, transforms, schedules, and transition events. No Canvas/uGUI component or alternative UI system is introduced.
- **Planning discipline:** the owner approved `spec.md` before this plan was written; implementation remains blocked until this plan is reviewed and approved.
- **File organization and assemblies:** the helper stays inside the existing `Assets/Scripts/Unity/UI/` feature assembly; no unnecessary nested/runtime asmdef is introduced. Gallery changes stay in the established Gallery assembly.
- **C# style:** implementation must use tabs, same-line braces, `_` private fields, required control-flow braces, and contextual failures/logging. Expected cancellation is handled explicitly rather than silently swallowing unexpected exceptions.

No constitution violations were found.

Use `/implement` after this plan and its review concerns are approved.
