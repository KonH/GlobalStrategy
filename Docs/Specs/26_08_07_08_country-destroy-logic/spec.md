# Spec: Country Destroy Logic

## Feature Intent

As a player, I want a country that loses control of all its provinces to be recognized as destroyed, so that its control, goal progress, and downstream systems (including the UI) reflect that it is no longer a functioning country on the map.

## Acceptance Criteria

- **Given** a country entity that still owns at least one province (per `ProvinceOwnershipSystem.GetProvincesByOwner`) **When** the destroy check runs **Then** the country is not marked destroyed and no destroy event is raised.
- **Given** a country entity that owns zero provinces (its `CountryId` no longer appears as an `OwnerId` in any `ProvinceOwnership`) **When** the destroy check runs **Then** an `IsDestroyed` flag component is added to that country's entity, and the entity and its other components (e.g. `Country`) are left intact — nothing is deleted.
- **Given** a country transitions from "has provinces" to "has zero provinces" in a given tick **When** the destroy check detects this transition **Then** a one-shot country-destroy event component is created carrying the destroyed country's id, following the same lifecycle as `ActionSucceeded`/`ActionFailed`: not `[Savable]`, created by a system, and swept by the tick-end cleanup pass (either by extending `CleanupActionEffectsSystem`'s list of types or an equivalent same-tick sweep) so it does not persist into the next tick.
- **Given** a country becomes destroyed **When** the destroy logic runs **Then** every `ControlEffect` entity referencing that `CountryId` (for every org) is removed or zeroed, so that querying total control for that country by any org sums to zero afterward.
- **Given** a country is already flagged `IsDestroyed` (from a previous tick) **When** the destroy check runs again and the country still has zero provinces **Then** no duplicate `IsDestroyed` component is added and no duplicate destroy event is raised — the flag and event are idempotent per destruction.
- **Given** a country has been marked `IsDestroyed` **When** `GameCompletionSystem.GetAvailableCountryIds` is evaluated **Then** the destroyed country's id is excluded from the returned set, so it no longer counts as an available/target country for goal evaluation.
- **Given** `TotalControlCondition`, whose required target scales with `AvailableCountryIds.Count` **When** a country becomes destroyed and is excluded from `GetAvailableCountryIds` **Then** its required target recalculates against the smaller available-country pool on the next evaluation.
- **Given** `FullControlCondition`, whose required country count is a fixed target **When** a country becomes destroyed and is excluded from `GetAvailableCountryIds` **Then** the destroyed country is no longer counted toward the org's controlled-country numerator, and no longer counted as part of the eligible denominator used to determine whether the fixed target is still achievable (exact required-count adjustment behavior is flagged under Ambiguities).
- **Given** a country becomes destroyed **When** `VisualStateConverter` next projects ECS state to `VisualState` **Then** the destroyed status (derived from the `IsDestroyed` flag) and a corresponding destroy event/notification are both exposed on `VisualState`, so that a UI layer (out of scope here) can consume them.
- **Given** the destroy check must decide when to run **When** province ownership changes (i.e. `ProvinceOwnershipVersion` is bumped, per `ProvinceOwnershipSystem`'s existing dirty-check convention) **Then** the destroy check re-evaluates all countries whose province count could have changed as a result — running the check off the existing ownership dirty-check rather than unconditionally every tick.
- **Given** a country has zero provinces at game start or load (e.g. from a save file, or a country with no provinces ever assigned) **When** the destroy check first runs **Then** it is treated the same as any other zero-province country and is marked destroyed (no special-casing for "never had provinces" vs. "lost its last province").
- **Given** a country becomes destroyed **When** the destroy logic runs **Then** every `CountryRelation` (friend or rival) referencing that country's id on either side is removed entirely, so the destroyed country no longer appears in any other country's `Friends`/`Rivals` lists.
- **Given** an action card is targeted at a country (directly, or as its `RevengeCardTarget`/relation target) **When** that target country is flagged `IsDestroyed` **Then** the card is unplayable with a new reason (surfaced to `VisualState.ActionCardEntry.UnplayableReason` and localized), distinct from the existing unplayable reasons, indicating the target country no longer exists.

## Out of Scope

- Any UI notification window, popup, or player-facing message informing the player a country was destroyed — covered by a separate spec (part B).
- Any change to how a destroyed country's province ownership is displayed on the map beyond it naturally holding zero provinces (no explicit grey-out/border removal work) — a destroyed country is not removed from `VisualState.WorldCountries.CountryIds`, it simply owns no territory.
- Any mechanic to "un-destroy" or revive a country (e.g. via reconquest) — destruction is permanent for the session; not supported.
- Changes to non-control, non-goal, non-relation country state (e.g. bot AI behavior toward destroyed countries, resource stockpiles) beyond zeroing control and removing relations.
- Writing or updating automated tests (`src/Game.Tests/`) — this is a planning/implementation concern, not spec content.
- Any change to `GameSettings.MaxControlPool` or other config-level constants.

## Resolved Decisions

(Owner clarifications from the issue thread, superseding the original ambiguities.)

- **VisualState event shape:** FIFO acknowledgeable queue, matching `VisualState.WarResults`/`WarResultsState` (`Enqueue`/`TryPeek`/`AcknowledgeCurrent`) — not a single "recent" field.
- **`WorldCountries.CountryIds`:** the destroyed country stays present in the set, flagged destroyed via `IsDestroyed` — not removed. It naturally has no map presence since it owns no provinces.
- **Revival:** not supported. Destruction is permanent for the session; no un-destroy path.
- **`FullControlCondition` target:** auto-adjusts — the fixed required-country-count target is decreased as countries are destroyed, keeping the goal achievable with fewer countries in play (same spirit as `TotalControlCondition`'s existing scaling against `AvailableCountryIds.Count`, but for `FullControlCondition`'s fixed target it means the target itself shrinks).
- **Destroy check scope:** `ProvinceOwnershipSystem.ChangeOwner` already returns `(bool Changed, string OldOwnerId)` for the single province that changed hands in that call — the destroy check should evaluate that specific old-owner country only (it is the only country whose province count could have decreased), not re-scan every country in the world.
- **Zero-provinces-at-start:** not a supported/special-cased scenario; the immediate-destruction rule (last bullet in Acceptance Criteria above) is the only behavior — kept simple.
- **Rivals + cards (owner-flagged "important part"):** rival/friend relations to a destroyed country must be removed entirely (see new Acceptance Criteria bullets above), and cards targeted at a destroyed country become unplayable with a new reason communicating the country no longer exists.
