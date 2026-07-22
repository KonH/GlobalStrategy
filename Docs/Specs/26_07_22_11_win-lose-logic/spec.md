# Spec: Win / Lose Logic

## Feature Intent

As a player, I want the game to recognize when an organization achieves the configured control objective, declare the winner and losers, and stop the completed game, so that control-focused play has a definitive and observable outcome.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A game is in progress with the configured objective of either 80% of total control across all available countries or full control in 15 available countries.
  - No participating organization has reached either threshold => the game finishes an update => the game remains in progress, no winner or loser is assigned, and normal updates continue.
  - One participating organization reaches at least 80% of total control => the game finishes the update that reached the threshold => that organization is the sole winner and every other participating organization is a loser.
  - One participating organization reaches full control in at least 15 distinct countries => the game finishes the update that reached the threshold => that organization is the sole winner and every other participating organization is a loser.
  - An organization reaches exactly 80% total control or exactly 15 fully controlled countries => the game evaluates the objective => equality counts as meeting the relevant threshold.
  - An organization is below both thresholds => the game evaluates the objective => concentrated control or partial control in additional countries does not complete the game.
- Control is evaluated for a participating organization.
  - The organization has multiple control contributions in one country => the game evaluates its progress => those contributions are summed for that country without including another organization's control.
  - The game contains countries where the organization has no control => the game evaluates its total-control share => every available country's full control capacity still contributes to the total capacity.
  - The game contains no available countries => the game evaluates the total-control objective => the objective fails safely and no winner is declared from an undefined ratio.
- More than one participating organization meets the objective in the same update.
  - The game selects the winner => exactly one winner is chosen in the same stable order on every run => every other participating organization is a loser.
- The game has not completed.
  - A consumer reads the result => completion is false, the result is `InProgress`, and no winner identity is present.
- The game completes.
  - The player organization is the winner => the final state is published => completion is true, the winner identity is present, and the player result is `Win`.
  - Another organization is the winner => the final state is published => completion is true, the winner identity is present, and the player result is `Lose`.
  - The update that reaches the objective contains other state changes => completion is finalized => all changes from that update are included in one final published state.
  - Another update or gameplay command is submitted => the game receives it => time, resources, control, actions, logs, and all other gameplay state remain frozen, and the winner and loser assignments do not change.
  - A consumer reads the result repeatedly => the completed state is returned => the completion flag, winner identity, player result, and organization outcomes remain unchanged.
- The completion objective is changed in configuration.
  - A new game is started => its objective is evaluated => the configured thresholds and configured composition are used instead of fixed update-loop values.

## Tech Notes

- Configured objective and threshold behavior:
  - Extend `src/Game.Configs/GameSettings.cs` and `Assets/Configs/game_settings.json` with one composite completion-condition definition using explicit OR semantics, a total-control threshold of `0.8`, and a full-control-country threshold of `15`.
  - Represent leaf conditions behind a shared completion-condition contract/base abstraction. Reuse `ExpressionNode` comparison/composition behavior from `src/Game.Configs/ExpressionNode.cs` only where its inputs and semantics match; do not force aggregate world queries into the action-specific `ExpressionContext`.
- Organization progress and edge-case behavior:
  - Treat ECS `Organization` entities created by `InitSystem.ResolveParticipatingOrgs` as the participating organizations and ECS `Country` entities as the available countries; config entries not instantiated in the world do not contribute.
  - Aggregate all `ControlEffect.Value` entries by `(OrgId, CountryId)`. Extend or reuse `OrgMetrics.GetControlByCountry`/`GetTotalControl` so both leaf conditions share the same totals.
  - Use `GameSettings.MaxControlPool` as each country's capacity and full-control threshold. Compute total capacity as available-country count multiplied by that value, include zero-control countries in the denominator, treat control at or above the pool size as full, and return false when total capacity is zero.
- Winner and loser assignment:
  - Evaluate every participating organization after the update's gameplay mutations. Preserve the organization order produced during initialization as the deterministic tie-break and record a single winner plus an outcome for every other organization.
  - Store terminal state in ECS game logic, rather than in a MonoBehaviour, so Unity and headless callers observe the same result and repeated evaluation cannot replace it.
- Published result and update freeze:
  - Add a game-result enum with `InProgress`, `Win`, and `Lose`, plus a completion-state model carrying `IsCompleted`, the winner organization ID, and organization outcomes.
  - Expose the player-facing result through `src/Game.Main/VisualState.cs` and populate it from `VisualStateConverter` using the current player organization.
  - In `GameLogic.Update`, evaluate completion after all state-changing systems and commands for the tick, then run the final `VisualStateConverter.Update`. On later calls, return before simulation and command processing so the terminal world and visual state remain unchanged.
- Verification coverage:
  - Add focused headless tests for both thresholds and equality, OR composition, multi-effect aggregation, zero-country handling, non-qualifying states, deterministic simultaneous qualifiers, player win/lose projection, final-state publication, and the post-completion update/command freeze.

## Out of Scope

- A victory/defeat modal, end-game screen, animation, sound, notification, localization, or any other new presentation beyond exposing the completion flag, winner identity, and result value for consumers.
- Additional completion-condition types, alternative threshold presets, multiple independently configured victory rules, campaign-specific objectives, draws, shared victories, or post-game tie-break UI.
- Changing the control-pool rules, control-effect application/clamping, country availability, discovery, organization participation, country/org scoring, or resource calculations.
- Continuing simulation in a post-game mode, restarting/rematching from the completed game instance, or accepting gameplay commands after completion.
- Redesigning action-condition configuration or expression semantics beyond sharing an existing abstraction/evaluator where it is behaviorally compatible with completion conditions.
