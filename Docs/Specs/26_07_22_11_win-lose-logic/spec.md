# Spec: Win / Lose Logic

## Feature Intent

As a player, I want the game to recognize when one participating organization has achieved the configured control objective, declare that organization the winner and every other participating organization a loser, and freeze the completed game, so that control-focused play has a definitive and observable outcome instead of continuing indefinitely.

Game completion is config-driven. The initial configuration contains one composite completion condition that succeeds when either of two control objectives succeeds: an organization owns at least 80% of all control capacity across the countries participating in the game, or it has full control in at least 15 countries. The completion model is structured as composable conditions, following the same evaluate-and-compose concept already used by action conditions where the aggregate game-state inputs allow that logic to be shared safely.

## Definitions

- A **participating organization** is an organization instantiated for the current game session, including both the player organization and bot-controlled organizations.
- An **available country** is a country instantiated in the current game world. Config entries excluded from initialization do not contribute control capacity or country counts.
- An organization's **control in a country** is the sum of all current `ControlEffect` values for that organization and country, including base and permanent control.
- **Full control** means the organization's control in that country is at least the configured control-pool capacity (`MaxControlPool`, currently `100`).
- **Total control capacity** is the number of available countries multiplied by `MaxControlPool`. An organization's total-control share is its summed control across all available countries divided by that capacity; countries where it has no control still contribute their full capacity to the denominator.
- The configured composite condition uses **OR** semantics: satisfying either the total-control condition or the full-control-country-count condition completes the game.

## Acceptance Criteria

### Completion configuration and condition evaluation

- **Given** the game settings configuration **When** it is loaded **Then** it defines one composite game-completion condition whose alternatives are a total-control threshold of `80%` and a full-control-country threshold of `15`; these threshold values and their OR composition are data-driven rather than hard-coded in the update loop.
- **Given** game-completion conditions are represented in code **When** the composite condition is evaluated **Then** leaf conditions share a common completion-condition contract/base abstraction and the composite evaluates child conditions with explicit OR semantics; existing action-expression evaluation is reused where its context and semantics are compatible, without duplicating equivalent comparison/composition behavior merely for game completion.
- **Given** an organization has aggregate control equal to or greater than 80% of total control capacity across all available countries **When** completion is evaluated **Then** its total-control condition succeeds, including exact equality at the configured threshold.
- **Given** an organization has aggregate control below 80% of total control capacity **When** completion is evaluated **Then** its total-control condition fails, even if that control is concentrated in a small number of countries.
- **Given** an organization has control equal to `MaxControlPool` in at least 15 distinct available countries **When** completion is evaluated **Then** its full-control condition succeeds, including exactly 15 countries.
- **Given** an organization has full control in fewer than 15 countries **When** completion is evaluated **Then** its full-control condition fails, regardless of its control in the remaining countries unless the separate total-control condition succeeds.
- **Given** an organization has multiple `ControlEffect` entries for the same country **When** either leaf condition is evaluated **Then** all of that organization's entries for that country are summed once into the country's control total; effects belonging to other organizations never contribute to its result.
- **Given** there are no available countries **When** the total-control condition is evaluated **Then** it fails safely rather than treating an undefined zero-capacity ratio as completion.

### Winner and loser calculation

- **Given** no participating organization satisfies the configured composite condition **When** a game update completes **Then** the game remains in progress, no organization is marked winner or loser, and normal updates continue.
- **Given** a gameplay update changes control so that one participating organization first satisfies either configured alternative **When** completion is evaluated at the end of that update's game-state mutations **Then** that organization is recorded as the sole winner and every other participating organization is recorded as a loser during the same update.
- **Given** more than one participating organization satisfies the composite condition during the same evaluation **When** the winner is selected **Then** exactly one winner is chosen deterministically using the stable participating-organization order established for the session, and all remaining organizations are losers.
- **Given** a winner has already been recorded **When** completion is queried or `Update` is called again **Then** the winner and loser assignments are immutable; later world state or commands cannot replace the winner.

### Completed-game state and update freeze

- **Given** a game has not completed **When** its completion state is read **Then** it exposes an explicit `IsCompleted = false` indication and a non-terminal result value (for example `InProgress`), with no winner organization ID.
- **Given** the player organization's session completes with that organization as the winner **When** the final visual/game state is published **Then** it exposes `IsCompleted = true`, the winner organization ID, and a player-facing result enum value of `Win`.
- **Given** the player organization's session completes with another organization as the winner **When** the final visual/game state is published **Then** it exposes `IsCompleted = true`, the winner organization ID, and a player-facing result enum value of `Lose`.
- **Given** a completion condition becomes true during an update **When** that update finishes **Then** all state mutations that caused the result are included, winner/loser state is calculated, and one final visual-state conversion publishes the terminal result.
- **Given** the terminal result has been published **When** subsequent `GameLogic.Update` calls occur **Then** the simulation no longer advances time, updates resources or control, processes actions or gameplay commands, changes game-log/gameplay state, or recalculates the winner; the final result remains readable by Unity and headless callers.
- **Given** the same world state and completion configuration **When** the game is run repeatedly **Then** completion timing, winner selection, loser assignments, and exposed result values are deterministic.

## Out of Scope

- A victory/defeat modal, end-game screen, animation, sound, notification, localization, or any other new presentation beyond exposing the completion flag, winner identity, and result value for consumers.
- Additional completion-condition types, alternative threshold presets, multiple independently configured victory rules, campaign-specific objectives, draws, shared victories, or post-game tie-break UI.
- Changing the control-pool rules, control-effect application/clamping, country availability, discovery, organization participation, country/org scoring, or resource calculations.
- Continuing simulation in a post-game mode, restarting/rematching from the completed game instance, or accepting gameplay commands after completion.
- Redesigning action-condition configuration or expression semantics beyond sharing an existing abstraction/evaluator where it is behaviorally compatible with completion conditions.
