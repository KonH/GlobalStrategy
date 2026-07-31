# Spec: War Result Window

## Feature Intent

As a player, I want a war-result window and configurable event notifications when a war peaces out — pausing and showing the outcome when I have influence in a participant country, and always recording the resolution in the action log — so that I can see spoils, control changes, and conquered provinces without missing wars that matter to my organization.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A war peaces with a winner and loser, and the player has influence in at least one of the two participant countries (player org control in that country is strictly greater than zero).
  - Peace resolution completes => the game pauses and the war result window opens above the map and HUD.
  - The window is shown => its layout matches the war progress window (header "`<Attacker>` - `<Defender>` War", progress slider, effects list, side stats, battles list), plus the additions below.
  - The window is shown => a gold-colored label under the header reads "`<WinnerCountryName>` won!".
  - The window is shown => a results block lists gold taken and how that gold was distributed to organizations (and any country remainder), control that changed, and the provinces taken.
  - No gold was transferred => the gold section still appears and shows that nothing was taken / distributed (zero or empty distribution, not a crash or missing section).
  - No provinces were transferred => the provinces list is empty (or an explicit empty state) rather than inventing province rows.
  - No org control shifted => the control-changed section shows that nothing changed (empty / zeros) rather than omitting the section silently in a confusing way.
- A war peaces with a winner and loser, and the player has influence in neither participant country.
  - Peace resolution completes => the war result window does not open and the game is not paused by this feature.
  - Peace resolution completes => an action-log entry for the war resolution is still written.
- A war peaces with a winner and loser (regardless of player influence).
  - Peace resolution completes => an action-log entry for the war resolution is always written (short winner/loser line; not a full spoils dump).
- The war result window is open.
  - The player clicks the close button => the window closes, map and HUD interaction is restored, and the simulation resumes if this feature paused it.
- Several wars peace in the same tick and more than one passes the influence gate.
  - Peace resolutions complete => result windows open one after another in FIFO order; the player dismisses each before the next; the sim stays paused until the last owned result window closes.
- Event notification configuration exists for event types (including war resolved).
  - A designer opens the notification config => each listed event type has independent flags for pause game, show window, and write to action log, all defaulting to enabled, plus condition expressions (existing conditions system) that gate those behaviours.
  - War resolved fires with its defaults => pause and show-window still obey the player-influence condition (control > 0 in any participant); write-to-action-log still fires unconditionally for war resolved (condition always true / empty).
  - In a future config, an event type could disable show-window or pause while leaving write-to-action-log on => the config shape allows that divergence without a schema rewrite (actual non-default combinations beyond war resolved’s gates are not required to ship in this feature).
- Debug stop-war ends a war at progress exactly `0` (no winner).
  - Resolution completes => no war-result window and no war-resolved notification path is required for this case (ignored for this feature).

## Tech Notes

- Same layout as war progress + winner label + results block:
  - Reuse the war progress presentation patterns from `Assets/UI/Modal/WarProgressWindow/WarProgressWindow.uxml` + `.uss`, `Assets/Scripts/Unity/UI/WarProgressWindowDocument.cs`, and `WarProgressWindowView.cs` (header via `hud.war.title_format`, `[-100, 100]` dual-fill progress track, effects columns, side stats, battles `ScrollView`, `ModalState`, sorting order ~`510`). Maximum reuse of shared view helpers / USS / projection shapes; do not fork styling needlessly.
  - Sibling `WarResultWindow` document/view/UXML under `Assets/UI/Modal/WarResultWindow/` (not extending the live progress window in place): peace destroys the war entity, so `SelectedWarState` / `SelectedWarProjector` cannot keep projecting a live war after `Wars.DestroyWar`. A frozen result snapshot must drive the UI. Sharing USS utility classes (`.gs-color-attacker` / `.gs-color-defender`, panel chrome) and extracting shared bind helpers from `WarProgressWindowView` where practical is expected.
  - Progress slider, effects list, side stats, and battles list all still appear on the result window (frozen from the pre-destroy snapshot) — not chrome/header alone.
  - Add a gold-styled winner label under the header (new locale key, e.g. `war_result.winner_format` → "`{0} won!`") with a dedicated USS class for gold text color — not the attacker/defender red/blue utilities. “Gold label” means gold-colored “won!” text, not a gold-amount label.
  - Add a results section under (or after) the shared progress layout: gold taken + per-org distribution (and country remainder if any), control deltas per org/country side, and transferred province ids resolved through existing `province_name.{ProvinceId}` localization.
- Results snapshot must be captured before the war is destroyed:
  - `Wars.ResolvePeace` in `src/Game.Systems/Wars.cs` currently: transfers provinces → clears occupation → `TransferGoldSpoils` → `ApplyControlShifts` → `DestroyWar` → creates `WarResolvedApplied { WinnerCountryId, LoserCountryId }` only (`src/Game.Components/GameLogEffects.cs`).
  - Enrich `WarResolvedApplied` (or add a sibling one-shot snapshot component created in the same tick) with: attacker/defender country ids, final progress, winner/loser, total gold taken, per-recipient gold distribution (`OwnerType` + owner id + amount), per-org control deltas (country id, org id, signed delta, optional post-shift total), transferred province id list, and frozen progress-history / side-stats / battle rows needed for the progress-layout sections.
  - Refactor `TransferOccupiedProvinces`, `TransferGoldSpoils` / `CollectGoldFromSide` / `PayoutGoldToSide`, and `ApplyControlShifts` / winner-boost / loser-cut helpers so they return or accumulate the amounts and ids applied (today they mutate silently). Capture attacker/defender/progress/declaredAt and any battle/history snapshot **before** `DestroyWar`.
  - Keep cleanup on the existing `CleanupEffectNotificationsSystem.UpdateWarResolved` path (`GameLogic` sweeps last tick’s `WarResolvedApplied` before the next peace chance pass).
- Action log unconditionally for war resolved (short line):
  - `GameLogEntryKind.WarResolved`, `VisualStateConverter.UpdateGameLog` conversion of `WarResolvedApplied`, `GameLogLineFormatter.BuildWarResolvedLine`, and locale `game_log.war_resolved_format` already exist and do not filter by player org (unlike `WarDeclaredApplied` / relation entries gated by `GameLogSettings.IncludePlayerActions`).
  - Keep the existing short winner/loser action-log line; do not expand it to dump spoils/control/provinces (window carries detail).
  - Under the new notification config, war resolved’s write-to-action-log flag stays enabled by default and must remain independent of the influence gate used for pause/window. Prefer routing log emission through the notification dispatcher so config can later turn logging off, without changing the product rule that war resolved always logs in v1 defaults.
- Player influence gate for pause + window:
  - Player org is `VisualState.PlayerOrganization` / the player org entity.
  - “Has influence” = `ControlQuery.GetOrgControlInCountry(world, playerOrgId, countryId) > 0` in either war participant country.
  - Gate evaluation uses the existing conditions / `ExpressionNode` system (not a hard-coded one-off boolean beside the config flags). Notification config entries carry condition expressions for pause / show-window / write-action-log (or a shared condition plus per-behaviour flags — plan chooses the shape), evaluated against a context that can express player control in participants. Empty / always-true condition for write-action-log on war resolved; influence condition for pause and show-window.
  - Gate evaluation belongs with the notification / presentation path that reacts to `WarResolvedApplied` (Unity UI / visual-state consumer), not inside `Wars.ResolvePeace` domain mutation — ECS still always emits the enriched event; UI/dispatcher decides pause/window/log per config + conditions.
- Pause / modal behaviour:
  - On show: push `PauseCommand` via `IWriteOnlyCommandAccessor` and set `ModalState.IsModalOpen` (same pattern as card play / game menu in `HUDDocument` / `CardPlayAnimator` / `GameMenuDocument`). War progress currently sets modal state but does **not** auto-pause; war result **does** pause when the influence gate passes.
  - On close: clear modal ownership, hide the document, and push `UnpauseCommand` if this feature issued the pause (mirror card-play / menu unpause; do not leave the sim stuck paused).
- New event notification mechanics (config-driven + conditions):
  - Add a config list (on `GameSettings` / `Assets/Configs/game_settings.json`, or a dedicated settings object referenced from there) of event-type entries, each with: event type id (e.g. `"war_resolved"`), `Pause` / `ShowWindow` / `WriteActionLog` booleans defaulting to `true`, and condition expression(s) using the existing `ExpressionNode` conditions system.
  - Introduce a small notification dispatcher (Unity presentation side, fed by projected `*Applied` events / visual state) that, for each fired configured event type: if `WriteActionLog` and its condition passes → ensure game-log entry path runs; if `ShowWindow` and its condition passes → open the matching window; if `Pause` and its condition (and show path) applies → push `PauseCommand`.
  - For war resolved v1 defaults: `ShowWindow` and `Pause` conditions encode player influence in any participant (`control > 0`); `WriteActionLog` condition is always true. Do not leave influence as a hard-coded special case outside the conditions system.
  - Register `WarResultWindowDocument` in `GameLifetimeScope` like `WarProgressWindowDocument`; HUD or a notification host opens it from the dispatcher rather than from war-icon clicks.
- Multiple resolutions / ordering:
  - If several wars resolve in one tick, each emits its own `WarResolvedApplied` (or snapshot). Queue result windows FIFO when influence conditions pass so the player dismisses one before the next; each queued open that requires pause keeps the sim paused until the last owned result window closes.
- Debug `StopWar` at progress `0`:
  - Out of scope for this feature — no requirement to emit `WarResolvedApplied` or open a window for the no-winner path (current behaviour may remain).
- Read-only UI boundary:
  - Result window only displays the snapshot; it issues no war/stop/battle commands — only close → unpause / clear modal.
- Constitution:
  - Peace outcome math and snapshot emission stay in `src/` ECS; UI Toolkit + VContainer only; no Canvas/UGUI.

## Out of Scope

- Per-event-type behavioural divergence beyond war resolved’s influence-gated window/pause vs always-on action log (e.g. shipping other event types as log-only UI). The config shape must allow it later; implementing and tuning other event types’ windows is not part of this feature.
- Changing peace-resolution math (province transfer fraction, gold `D × G`, control ± fractions) — only surface those outcomes.
- Changing country relations on peace.
- Multi-country / allied wars beyond the current two-participant model.
- Animation, audio, or fly-text beyond opening the modal and pausing.
- Redesigning the existing war progress HUD icon flow (`WarProgressWindow` click-to-open for active wars).
- Expanding the action-log line itself to dump full spoils/control/province detail (window carries detail; log keeps the short winner/loser format).
- Debug `StopWar` at progress `0` (no winner) — ignore for this feature.
