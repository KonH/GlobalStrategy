# Spec: Unified Action Pipeline

## Feature Intent

As a developer, I want org-action and country-action execution unified into a single ECS component pipeline, so that action processing is consistent, testable, and extensible without maintaining two divergent code paths.

## Acceptance Criteria

### Command unification

- **Given** the game receives a request to play an org-scoped card **When** `PlayCardActionCommand(actionId, orgId, countryId: null)` is pushed **Then** it replaces the former `PlayActionCommand` and the pipeline handles it correctly
- **Given** the game receives a request to play a country-scoped card **When** `PlayCardActionCommand(actionId, orgId, countryId)` is pushed (countryId present) **Then** it replaces the former `PlayCountryActionCommand` and the pipeline handles it correctly
- **Given** `PlayActionCommand` and `PlayCountryActionCommand` existed **When** the refactor is complete **Then** both command types are removed from the codebase

### ECS component model

- **Given** a card entity for an org-scoped action **When** `InitActionFromPlayCardSystem` runs **Then** the entity has `Action(actionId)`, `OrgContext(orgId)`, and `CardUse` components; no `CountryContext` is present
- **Given** a card entity for a country-scoped action **When** `InitActionFromPlayCardSystem` runs **Then** the entity has `Action(actionId)`, `OrgContext(orgId)`, `CountryContext(countryId)`, and `CardUse` components
- **Given** the `InHand` component existed **When** the refactor is complete **Then** it is replaced by `CardInHand` with identical semantics
- **Given** `ActionCard` and `CountryActionCard` components existed **When** the refactor is complete **Then** both are removed; the card entity with context components replaces them
- **Given** a deck entity **When** it exists **Then** it carries `CardDeck(orgId, countryId?)` where `countryId` is present only for country-scoped decks; there is exactly one deck entity per `(orgId)` for org-scoped decks and exactly one per `(orgId, countryId)` for country-scoped decks
- **Given** a hand entity **When** it exists **Then** it carries `CardHand(handSize)`

### System pipeline ordering

- **Given** the game loop starts a new frame **When** `CleanupActionEffectsSystem` runs **Then** all action-execution components from the previous frame are removed: `Action`, `ActionValid`, `ActionSucceeded`, `ActionFailed`, `CardUse`, `DiscoverCountryEffect`, and `ResourceChange` components originating from actions
- **Given** a `PlayCardActionCommand` is queued **When** `InitActionFromPlayCardSystem` runs **Then** it locates the card entity matching `(actionId, orgId[, countryId])` that has `CardInHand` and adds the appropriate context components
- **Given** a card entity has `Action + OrgContext [+ CountryContext]` **When** `CheckActionConditionSystem` runs **Then** `ActionValid` is added only if all conditions are met and the cost is affordable
- **Given** a card entity has `Action + ActionValid` **When** `DeductActionCostSystem` runs **Then** resources are deducted directly in ECS and `ResourceChange` entities (negative amounts) are created for each cost, for use by visual animation
- **Given** a card entity has `Action + ActionValid` **When** `ActionSucceededSystem` runs **Then** `ActionSucceeded` is added unconditionally; `ActionFailed` is defined and kept as a dead path reserved for future use but is never added by the current implementation
- **Given** a card entity has `Action + ActionSucceeded` **When** `CreateActionEffectSystem` runs **Then** `DiscoverCountryEffect` and/or `ResourceChange` entities are created per the action's `effectIds` config; for opinion-change effects the action config entry is a resource-change type targeting the opinion resource by `resourceId = "opinion_{orgId}"`, and role-to-character resolution happens inside `CreateActionEffectSystem`
- **Given** a `DiscoverCountryEffect` entity exists **When** `DiscoverCountrySystem` runs **Then** a random undiscovered country weighted by proximity is marked discovered
- **Given** a card entity has `CardUse + (ActionSucceeded | ActionFailed) + CardInHand` **When** `RemoveCardFromHandSystem` runs **Then** `CardInHand` is removed and `CardDiscard` is added; no further removal of `CardDiscard` occurs in this system
- **Given** a card entity has `CardDiscard` **When** `CheckHandSizeSystem` runs **Then** it finds the matching deck entity (`CardDeck + OrgContext [+ CountryContext]`) and adds `CardDraw(1)` to it
- **Given** a deck entity has `CardDraw(N)` **When** `DrawCardSystem` runs **Then** N cards without `CardInHand` are selected via shuffled draw and `CardInHand` is added to each; `CardDraw` is then removed from the deck entity
- **Given** a card entity has `CardDiscard` **When** `CleanupCardDiscardSystem` runs **Then** the `CardDiscard` component is removed from that entity
- **Given** the systems above **When** each frame runs **Then** they execute in the order: Cleanup → Init → CheckCondition → DeductCost → ActionSucceeded → CreateEffects → DiscoverCountry → RemoveFromHand → CheckHandSize → DrawCard → CleanupCardDiscard

### Effect components (no IEffect interface)

- **Given** the `IEffect` interface and its implementors (`ResourceChange` class, `InfluenceAdded`, `CharacterOpinionChange`) existed **When** the refactor is complete **Then** all are removed; the new `ResourceChange` is a plain ECS component struct with no interface, distinct from the old class of the same name
- **Given** a `DiscoverCountryEffect(effectId)` component **When** the discovery system reads it **Then** no interface dispatch occurs; the system queries the component type directly
- **Given** a `ResourceChange(effectId, resourceId, ownerId, amount)` component **When** the resource system reads it **Then** it is a plain data component carrying the actual mutation, with no virtual methods

### Persistent ResourceEffect with MaxTotal

- **Given** a `ResourceEffect` entity **When** `MaxTotal > 0` **Then** the effect stops applying once `|AccumulatedTotal| >= MaxTotal`
- **Given** `CreateActionEffectSystem` creates a monthly decay `ResourceEffect` for an opinion resource **When** it is created **Then** `MaxTotal` equals the absolute value of the initial opinion grant and the `amount` is negative, draining the opinion back to zero over time
- **Given** a `ResourceEffect` with `MaxTotal == 0` **When** it is processed **Then** it behaves identically to the current unbounded monthly effect (no change to existing non-opinion resources)

### Character opinion as resource

- **Given** a character's opinion toward an org **When** it is stored **Then** it lives as a `Resource` entity with `ResourceOwner(characterId, OwnerType.Character)` and `resourceId = "opinion_{orgId}"`
- **Given** `CharacterOpinion` and `OpinionModifier` components existed **When** the refactor is complete **Then** both are removed; all opinion state is accessed via the resource model

### Visual state

- **Given** `VisualState.LastAction` existed **When** the refactor is complete **Then** it is replaced by `VisualState.LastFrameEffects` of type `VisualEffectCollection`
- **Given** the pipeline has run for a frame **When** `VisualStateConverter` executes **Then** `LastFrameEffects` is populated from all `ResourceChange` and `DiscoverCountryEffect` entities present after the pipeline; each `ResourceChange` entity maps to a `VisualResourceChangeEffect` (a visual-layer object implementing `IVisualEffect`, not an ECS component) which drives UI animation (e.g. gold deduction animation)
- **Given** `LastFrameEffects` is populated **When** UI animation code needs effects for a specific played card **Then** it calls `LastFrameEffects.GetEffectsByActionId(actionId)` and filters by the played card's `actionId`
- **Given** a card-play animation is running **When** the card is displayed in the card view **Then** no text is shown on the card and no text animation plays; there is no roll result display
- **Given** a card in hand is displayed **When** the card view renders **Then** no success rate, influence base, influence bonus, or dynamic-rate indicator is shown; `CountryActionCardEntry` has `SuccessRate`, `IsRateDynamic`, `InfluenceBase`, `InfluenceBonus` fields removed.

### ActionSystem removal

- **Given** `ActionSystem.ProcessPlayAction()` and `ActionSystem.ProcessPlayCountryAction()` existed **When** the refactor is complete **Then** the `ActionSystem` static class and `ActionResult` struct are fully removed; no callers remain

## Out of Scope

- Changes to any Unity MonoBehaviour or UI layer beyond updating callers of removed command types and `LastAction` → `LastFrameEffects`
- Adding new action types or effect types beyond those listed above
- Changing the condition evaluation logic (only the invocation site changes)
- Save/load migration for persisted game state (component renames must handle deserialization compatibility separately)
- Performance optimisation of the ECS query layer
- Success-rate evaluation and RNG rolls — `ActionSucceededSystem` always produces `ActionSucceeded` in this implementation
