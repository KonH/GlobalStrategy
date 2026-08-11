# Spec: Org Destroy Logic

## Feature Intent

As a player, I want an org that has lost every practical path back to relevance — no control anywhere, no way to regain control from its hand, no way to even clear space in its hand, and no way war mechanics could still turn things around — to be recognized as destroyed, so that goal/win evaluation and downstream systems (including the UI) reflect it is no longer a functioning participant, and so that a single remaining org can win outright once every other org has fallen.

## Acceptance Criteria

### Destroy conditions

- **Given** an org currently holds control `> 0` in at least one country (`OrgMetrics.GetTotalControl(world, orgId) > 0`) **When** the destroy check runs **Then** the org is not marked destroyed and no destroy event is raised, regardless of the state of any other condition below.
- **Given** an org holds zero total control across every country **When** the destroy check runs **Then** it additionally evaluates all of the following, and the org is marked destroyed only if every one holds simultaneously:
  - **No control card in hand** — none of the org's current `CardInHand` entities resolve (via `ActionConfig.Find` on their `GameAction.ActionId`) to an `ActionDefinition` whose configured effects include a positive `ControlChangeEffectParams` (the same classification `BotObservation.ClassifyRaisesControl` already uses, exposed here through a non-bot-specific domain helper so `Game.Systems` does not depend on `Game.Bots`).
  - **Hand is full** — `handCount >= handSize` for the org's hand, using the same source data as `CountryCardDrawQuery.TryGetStatus` (`HandCount`/`HandSize`).
  - **Every card in hand is unplayable, ignoring cooldown** — for each `CardInHand` entity, `ActionPlayability.Evaluate` produces at least one failing requirement, and that failure is never solely `"on_cooldown"` (a card whose *only* blocker is cooldown does not count as unplayable for this condition, per the issue's "skip cooldown check" instruction).
  - **No money to discard any card** — `OrgMetrics.GetGold(world, orgId) < GameSettings.DiscardGoldCost` (the flat, global discard cost; there is no per-card discard cost today).
  - **War mechanics offer no survival path** — see the dedicated War Guard criterion below.
- **Given** an org becomes destroyed **When** the destroy logic runs **Then** an `IsOrgDestroyed` flag component (`[Savable]`) is added to that org's `Organization` entity, and the entity and its other components (`Organization`, `OrganizationGameOutcome`, etc.) are left intact — nothing is deleted.
- **Given** an org transitions from "has a survival path" to "has none" in a given tick **When** the destroy check detects this transition **Then** a one-shot org-destroy event component (e.g. `OrgDestroyedApplied { OrganizationId }`) is created, not `[Savable]`, and swept at the start of the next tick via the same mechanism used for `CountryDestroyedApplied` (`CleanupEffectNotificationsSystem`, not `CleanupActionEffectsSystem` — the country-destroy-logic implementation deliberately moved off `CleanupActionEffectsSystem` to avoid a same-tick ordering bug where earlier-tick emitters lost their event before `VisualStateConverter` ran).
- **Given** an org is already flagged `IsOrgDestroyed` **When** the destroy check runs again **Then** no duplicate flag or event is produced — the flag and event are idempotent per destruction, mirroring `CountryDestroySystem.TryDestroyIfNoProvinces`'s `Has<IsDestroyed>` guard.

### War guard

- **Given** the destroy check is evaluating an org that would otherwise satisfy every condition above **When** it checks whether war mechanics could still let the org survive **Then** it applies the rule resolved under Ambiguities item 0 below; until that is resolved, the assumed default is: the org is **not** destroyed while its HQ country (`OrganizationConfig.FindById(orgId).HqCountryId`) is currently a war participant (`Wars.IsInWar(hqCountryId)` is true) — a conservative guard, since no current war-resolution path (`peace-resolution`'s control-shift step, `force_war_win`/`force_war_loss`/`declare_revenge_war`) grants control to an org that already holds zero control anywhere, but the org could still in principle declare/win a new war before this check runs again next tick.

### Cleanup and hygiene

- **Given** an org becomes destroyed **When** the destroy logic runs **Then** any residual `ControlEffect` entities for that org (there should be none, since zero total control is itself a precondition for destruction, but this guards against stale/negative entries) are removed, mirroring `ControlQuery.DestroyAllControlInCountry`'s pattern filtered by `OrgId` instead of `CountryId`.
- **Given** an org becomes destroyed **When** the destroy logic runs **Then** its `OrganizationGameOutcome.Result` is set to `Loser` immediately (subject to Ambiguities item 2 below), since a destroyed org cannot subsequently satisfy any win condition.
- **Given** an org is flagged `IsOrgDestroyed` **When** per-tick org processing runs (bot decision-tick, card-play/turn systems) **Then** that org is skipped entirely — it no longer takes actions, mirroring how a destroyed country stops appearing in `GameCompletionSystem.GetAvailableCountryIds`.

### Win/goal system integration

- **Given** an org has been marked `IsOrgDestroyed` **When** `GameCompletionSystem` evaluates participants **Then** the destroyed org's id is excluded from the set of orgs eligible to win (a `GetAvailableOrgIds`-style helper analogous to `GetAvailableCountryIds`, filtering `IsOrgDestroyed`), and excluded from `GoalsProjector.Build`'s per-org loop the same way destroyed countries are excluded from `GetAvailableCountryIds`-driven target math.
- **Given** every org except exactly one has been marked `IsOrgDestroyed` **When** `GameCompletionSystem.Update` runs **Then** a new win condition (e.g. `LastOrgStandingCondition`) is satisfied for the sole remaining non-destroyed org, and that org is declared the winner via the existing single-winner `GameCompletion` flow.
- **Given** the current three-way `AnyCompletionCondition` (`TotalControlCondition` @ `0.8`, `FullControlCondition` @ `15`, `ScoreGoalCondition` @ `270000`) **When** this feature ships **Then** the shipped `completionCondition.members` in `Assets/Configs/game_settings.json` are changed to exactly two members: `ScoreGoalCondition` with its threshold lowered to `50000`, and the new `LastOrgStandingCondition` — `TotalControlCondition` and `FullControlCondition` are removed from the active config (their C# classes may remain in the codebase as unused, generically reusable `ICompletionCondition` implementations; deletion is not required).
- **Given** the player's own org is the one that becomes destroyed **When** the destroy logic runs **Then** the session-level consequence follows the resolution of Ambiguities item 1 below (immediate loss vs. continuing to simulate remaining orgs).

## Out of Scope

- The `OrgDestroyedWindow` notification UI, or any other player-facing message informing the player an org was destroyed — covered by the separate Part-B spec (`26_08_11_09_org-destroy-ui`).
- Any mechanic to "un-destroy" or revive an org — destruction is permanent for the session, mirroring the country-destroy precedent's stance.
- Any change to bot decision-making logic beyond skipping destroyed orgs entirely in per-tick processing (e.g. no new bot heuristics for "avoid getting destroyed").
- Any change to `GameSettings.MaxControlPool`, `DiscardGoldCost`, or `CardCooldownDays` — only the win-condition config members (`completionCondition.members`) change, and only as described above.
- Populating or changing the org-card pool (`CardOwnerType.Org`) itself — it remains whatever state the card-draw-logic feature left it in; this feature only reads whatever hand data exists for the org, generically, without assuming which pool(s) are populated.
- Writing or updating automated tests (`src/Game.Tests/`) — a planning/implementation concern, not spec content.

## Ambiguities

- [NEEDS CLARIFICATION: (0) War-survival guard — is "skip destruction while the org's HQ country is in an active war" (the assumed default above) the right rule, or should it be narrower (e.g. only skip destruction if the org still has a currently-playable war-declaring/resolving card), or something else entirely? Current war-resolution math never grants control to an org that already holds zero control anywhere, so a literal "could this war let the org regain control" check would almost always answer "no" — worth confirming the guard is meant as a conservative safety net rather than a precise survival prediction.]
- [NEEDS CLARIFICATION: (1) If the human player's own org is the one destroyed and at least two other orgs are still active (not the last-standing case), does that immediately end the session as a loss for the player (`VisualState.GameCompletion` → `Result = Lose`, game freezes), or does the game keep simulating the remaining bot orgs until one of them separately satisfies a win condition, with the player simply carrying `OrganizationGameOutcome.Result = Loser` in the background?]
- [NEEDS CLARIFICATION: (2) Should a destroyed org's `OrganizationGameOutcome.Result` flip to `Loser` immediately at the moment of destruction (as assumed above), or should it stay `InProgress` until `GameCompletionSystem`'s normal end-of-game sweep, with destruction only affecting eligibility (via `GetAvailableOrgIds`) in the meantime?]
- [NEEDS CLARIFICATION: (3) "No control card in hand" and "all cards unplayable" — should these checks look only at the org's country-card hand (the only pool with real cards in production today), or be written generically over every `CardOwnerType` pool the org has (country + org), so the logic doesn't need revisiting once/if the org-card pool is populated? The spec above assumes the generic form.]
- [NEEDS CLARIFICATION: (4) Is "stop the destroyed org from taking further actions" (skipping it in bot decision-tick / turn processing, per the Cleanup and hygiene section above) actually in scope for this logic spec, or is it assumed to fall out naturally elsewhere (e.g. because it has zero control and full unplayable hand, a destroyed org would rarely take a meaningful action anyway) and this explicit skip is unnecessary extra work?]
