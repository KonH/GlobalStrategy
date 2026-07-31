# Spec: Sell Arms Card

## Feature Intent

As a player, I want to play a "Sell Arms" card for a country that is currently at war and whose military advisor strongly supports my organization, so that my organization earns gold while that country's troops receive a meaningful but temporary damage boost.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- The selected country is not currently participating in a war.
  - The deck/draw system evaluates a free hand slot for that country => "Sell Arms" is not eligible to be drawn, regardless of military-advisor opinion.
  - A copy is already in hand because the country was at war when the card was drawn, and that war then ends => the copy is shown as unplayable through the ordinary conditions gate; it stays in hand and is not discarded or retargeted merely because the war ended.
  - The country later enters a new war while that copy is still in hand and the opinion requirement is met => the same copy becomes playable again; the card is tied to the selected country, not permanently to the earlier war's `WarId`.
- The selected country is currently participating in a war, as either attacker or defender.
  - The playing organization's opinion with the country's current military advisor is below 80 => "Sell Arms" is not eligible to be drawn into a free hand slot, and an already-held copy is shown as unplayable.
  - Opinion is exactly 80 or higher => the opinion condition passes.
  - No current military advisor can be resolved for the country, or the advisor has no opinion resource for the playing organization => opinion is treated as 0 and the card is ineligible.
  - The active military advisor changes while a copy is in hand => draw eligibility and playability use the new current advisor's opinion, not the previous advisor's value.
- The selected country is currently at war and the playing organization's opinion with its military advisor reaches 80.
  - The condition becomes true while no hand slot is free => no card is pushed into the hand immediately.
  - A hand slot is filled on a later normal draw => "Sell Arms" joins the eligible country-card pool and may be selected through the existing shuffled draw flow.
- A "Sell Arms" copy is in hand while the selected country remains at war and military-advisor opinion remains at least 80.
  - The player plays the card => the play always succeeds, with no success/failure roll and no gold or other resource cost; the selected country's troop-damage bonus increases by 10 percentage points, the playing organization's gold increases by 300, the card leaves the hand, and the vacated slot is refilled through the ordinary draw flow.
  - The selected country is the attacker => the same +10-point damage bonus applies to that country's troops.
  - The selected country is the defender => the same +10-point damage bonus applies to that country's troops; war side does not change the card's result.
  - The opposing country receives no damage bonus, and no organization other than the organization that played the card receives gold.
- A country has just received one "Sell Arms" damage bonus.
  - No in-game month boundary has passed since the play => the bonus remains +10 percentage points.
  - One in-game month boundary passes => the bonus decreases to +9 percentage points.
  - Ten month boundaries pass without another bonus => the bonus reaches 0 and never becomes negative.
  - The country's war ends before the bonus reaches 0 => the remaining bonus continues its configured monthly decay; ending the war does not remove the already-applied effect early.
- Two or more "Sell Arms" copies are played for the same country before earlier bonuses have fully decayed.
  - Each play independently adds +10 percentage points and its own 1-point-per-month decay contribution => the active values stack additively, with no additional cap introduced by this feature.
  - One source reaches the end of its ten-month decay while another remains active => only the exhausted source stops contributing; the other source continues decaying on its own schedule.
- The player views the selected country's action cards.
  - "Sell Arms" uses the existing country-card presentation and has no cooldown label or separate war-target picker.

## Tech Notes

- **Dependency status:** issue #69's war core is present on `main`: `[Savable]` `War`, `WarProgress`, and `WarParticipant` components exist in `src/Game.Components/War.cs`; `Wars.IsInWar(IReadOnlyWorld, countryId)` already answers whether a country is an attacker or defender in any active war; and `Wars.StopWar` deletes both participants plus the shared war entity. This feature reuses that model and does not create a second representation of war state.

- **Static card definition** in `Assets/Configs/action_config.json`:
  - Add `actionId: "sell_arms"`, `ownerType: "country"`, `rarity: "Standard"`, `targetRole: "military_advisor"`, and `deckCopies: 3`, following the ordinary static country-card shape.
  - `cost` is an empty array. The issue specifies a 300-gold payout and no purchase cost; the card therefore grants gold without first deducting any.
  - Add two conditions, both required:
    - `gte(isInWar, 1)`.
    - `gte(opinion, 80)`.
  - Add two effect ids, `sell_arms_damage_bonus_effect` and `sell_arms_gold_grant_effect`. Their order is not load-bearing because they mutate different resources owned by different entities.
  - The three static copies are created for each participating organization/country combination through the existing `InitSystem.CreateCountryActionEntities` flow. No dynamic card-instance component or war-specific sync system is needed: each copy is country-scoped, and both draw and playability checks resolve the country's current war state at evaluation time.

- **Shared country-card condition context — consolidate the four currently duplicated evaluators before adding another field:**
  - Add a plain helper in `Game.Systems`, for example `CountryActionConditionContext.Build(IReadOnlyWorld world, ActionDefinition definition, string orgId, string countryId, int cardEntity = -1) : ExpressionContext`.
  - The helper computes the existing `Control`, `TotalCountryControl`, `HasSuitableRelationTarget`, and per-card `RelationStillExists` fields, plus the role-correct `Opinion` and new `IsInWar` field described below. It is a query helper, not a system entry point, so using it from multiple systems is consistent with `.claude/rules/unity/ecs_patterns.md`'s no-system-to-system-calls rule.
  - Replace the duplicated context construction in:
    - `InitSystem.CreateCountryActionEntities`, when choosing the initial hand.
    - `DrawCardSystem.DrawCountryCards`, when choosing eligible deck entities for a free slot.
    - `ActionPlayability.Evaluate`, used by the actual play pipeline and bots.
    - `VisualStateConverter.BuildEntry`, used to show whether an in-hand card is playable and why it is blocked.
  - The helper must receive the current `ActionDefinition` and card entity because `Opinion` depends on `definition.TargetRole`, while `RelationStillExists` may depend on the specific card entity's `RelationCardTarget`.

- **Military-advisor opinion gate — fix the existing role-selection gap instead of adding a Sell-Arms-only workaround:**
  - All four current evaluators hard-code `"diplomacy_advisor"` when populating `ExpressionContext.Opinion`, even though every `ActionDefinition` already carries `TargetRole`. That happens to work for the existing opinion-gated diplomacy cards but would silently evaluate Sell Arms against the wrong character.
  - The shared context helper resolves `CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, definition.TargetRole)` whenever `TargetRole` is non-empty, then reads `ResourceQuery.GetValue(world, characterId, $"opinion_{orgId}")`; missing role/character/resource yields 0. Cards with an empty `TargetRole` also receive opinion 0, which is harmless because their conditions do not reference it.
  - This is a general correctness fix: existing diplomacy cards retain the same behavior, while future ruler/economic/military-advisor opinion conditions automatically use their declared role.

- **New `isInWar` expression field:**
  - Add `double IsInWar` to `ExpressionContext` in `src/Game.Configs/ExpressionNode.cs` and a `case "isInWar": return ctx.IsInWar;` evaluator branch.
  - `CountryActionConditionContext.Build` sets it to `1.0` when `Wars.IsInWar(world, countryId)` is true and `0.0` otherwise. `Wars` is already a plain domain helper rather than a per-tick system, so this query does not violate the no-system-to-system-calls rule.
  - When `VisualStateConverter.BuildEntry` finds the failed expression contains `isInWar`, it maps the failure to a new `war_ended` unplayable reason. `CountryActionsView` resolves that reason through `action.country.unplayable.war_ended`, so a held card clearly explains why it cannot currently be played. The check is condition-driven only; no code removes the card when the war ends.

- **Troop-damage bonus representation — separate temporary percentage bonus, not a mutation of future base damage:**
  - Add `ResourceDefinitions.TroopsDamageBonusPercent = "troops_damage_bonus_percent"` and a hidden country-seeded row in `Assets/Configs/resource_config.json` with `seedTarget: "Country"`, `defaultInitialValue: 0.0`, and no default effects. Do not add it to `displayWhitelist`; this slice needs simulation state, not another top-bar resource.
  - Store values in percentage points: `10.0` means a `+10%` damage coefficient, and monthly decay `1.0` means one percentage point per month. This matches the repository's existing human-readable percent configuration convention (`RecruitsInitialPercent`, `RecruitsMonthlyIncreasePercent`) and avoids embedding `0.10`/`0.01` conversion assumptions throughout effect code.
  - The future troop/battle damage consumer applies the modifier as `baseDamage * (1.0 + troopsDamageBonusPercent / 100.0)`. Keeping this bonus separate is important: base troop damage is expected to be derived from country config and character skills, so adding the temporary value directly to that base resource would be lost or double-counted whenever the base is recomputed.
  - This feature creates and decays the bonus resource now. Wiring it into battle casualty calculation belongs to the battle/damage consumer that owns that formula; no such consumer exists on `main` yet.

- **New timed country-resource effect:**
  - Add `CountryResourceModifierEffectParams : ActionEffectDefinition` in `src/Game.Configs/EffectConfig.cs` with `string ResourceId`, `double InitialValue`, and `double DecayPerMonth`; register `"CountryResourceModifier"` in `ActionEffectDefinitionListConverter`.
  - Add `sell_arms_damage_bonus_effect` to `Assets/Configs/effect_config.json` with `effectType: "CountryResourceModifier"`, `resourceId: "troops_damage_bonus_percent"`, `initialValue: 10.0`, and `decayPerMonth: 1.0`.
  - In `CreateActionEffectSystem`, when a successful country action resolves this effect:
    - Find the selected country's already-seeded `troops_damage_bonus_percent` resource and add `InitialValue` immediately.
    - Emit a transient `ResourceChange` with owner equal to the country, resource id `troops_damage_bonus_percent`, and amount `+10`, keeping the normal last-frame effect stream accurate even though this hidden resource has no dedicated UI.
    - Create a `[Savable]` monthly `ResourceEffect` owned by that country and linked to the same resource: `Value = -DecayPerMonth`, `PayType = Monthly`, `MaxTotal = InitialValue`, and `ClampToZero = true`. Use a unique deterministic id containing the organization, country, played card entity, and current tick.
  - This reuses the same bounded monthly-decay mechanism as opinion modifiers. Every play creates its own decay effect, which produces the independent stacking behavior in the acceptance criteria. `ResourceSystem.Update` already runs once per tick before the action pipeline, so a card played on a month boundary retains its full +10 until the next month boundary rather than being decayed immediately in the same tick.

- **New organization-resource grant effect:**
  - Add `OrgResourceGrantEffectParams : ActionEffectDefinition` with `string ResourceId` and `double Amount`; register `"OrgResourceGrant"` in the effect converter.
  - Add `sell_arms_gold_grant_effect` to `effect_config.json` with `effectType: "OrgResourceGrant"`, `resourceId: "gold"`, and `amount: 300.0`.
  - `CreateActionEffectSystem` resolves it inline for the successful action's `OrgContext.OrgId`: locate the organization's existing gold resource, add 300 immediately, and emit `ResourceChange { ResourceId = "gold", OwnerId = orgId, Amount = 300 }` so the existing resource animation reflects the payout.
  - No marker component or separate update system is needed for either new effect. Both operations are deterministic synchronous resource mutations using data already available inside `CreateActionEffectSystem`; unlike relation/discovery effects, they require no random selection, proximity data, or top-level orchestration dependency.

- **Always-success behavior and end-of-war timing:**
  - No success-rate field, random roll, or failure effect is added. Once `CheckActionConditionSystem` validates `isInWar >= 1` and `opinion >= 80`, the existing `ActionSucceededSystem` marks the play successful and both effects run.
  - Debug stop-war commands currently run before the action-card pipeline in `GameLogic.Update`. Therefore, if a stop-war command and attempted Sell Arms play arrive in the same tick, the war is already absent when `ActionPlayability.Evaluate` runs: the card is rejected and neither bonus nor gold is granted.

- **Presentation and localization:**
  - Add `action.sell_arms.name` and a short practical `action.sell_arms.desc` (for example, "Boost this country's troop damage and gain gold."), plus concise name/description keys for both effect definitions, to `Assets/Localization/en.asset` and `ru.asset`.
  - Add `action.country.unplayable.war_ended` in both locales. The implementation must use the repository's localization workflow to supply a real Russian translation rather than copying English.
  - Add an `ActionVisualConfig` entry keyed by `actionId: "sell_arms"` and use placeholder existing artwork if no dedicated asset is supplied. Card artwork generation is not part of this specification.
  - The gold change uses the existing `ResourceChange`/`CardPlayAnimator` path. No new Action Log entry is required by the issue; the card's direct resource changes are represented by the ordinary card-resolution/resource feedback.

- **Verification coverage:**
  - `ExpressionNodeTests`: `isInWar` returns the context value and composes correctly with `gte`.
  - Shared context/helper tests: military-advisor opinion is selected from `ActionDefinition.TargetRole`; a high diplomacy-advisor opinion cannot satisfy a military-advisor condition; missing military advisor defaults to 0; active attacker and defender countries both produce `IsInWar == 1`; a non-participant produces 0.
  - `ActionPlayabilityTests`: Sell Arms is playable only when both the war and 80-opinion conditions hold; exact 80 passes; ending the war makes a held copy unplayable without removing it; no gold affordability gate applies.
  - `DrawCardSystemTests`: the card is skipped without war or below 80 opinion and becomes eligible on a later draw when both become true.
  - `VisualStateConverter` country-action tests: a held copy reports `war_ended` after `Wars.StopWar`, and role-specific opinion produces the same verdict as the actual play pipeline.
  - `CreateActionEffectSystem`/feature integration tests: a successful play adds exactly 300 gold only to the playing organization, adds exactly 10 bonus points only to the selected country, creates one bounded monthly decay effect, removes the played card through the normal pipeline, and draws a replacement.
  - `ResourceSystem` integration tests: the bonus remains 10 before a month boundary, becomes 9 after one boundary, reaches 0 after ten, never becomes negative, continues decaying after `Wars.StopWar`, and stacked plays decay independently.

## Out of Scope

- Declaring or ending a war, changing `WarProgress`, choosing an opponent, or changing whether the selected country is attacker or defender.
- The base troop-damage formula, durability, recruits-to-troops conversion, battles, casualties, or any other combat-resolution mechanics. This feature exposes the temporary percentage bonus that those systems will consume.
- Removing an already-applied bonus when a war ends; it continues to decay normally.
- Discarding a held Sell Arms card when a war ends; it remains in hand but is conditions-gated.
- Binding a card copy or bonus to a specific `WarId`; the selected country is the target, and the country's current active-war status is evaluated dynamically.
- A manual war/side/country target picker. The card operates on the country whose Actions panel owns it.
- A cap, replacement rule, or diminishing returns for stacked Sell Arms bonuses.
- Any card cost, success/failure roll, cooldown, or alternative payout recipient.
- A dedicated Action Log event or a new UI panel for the damage bonus.
- Opponent-bot strategy for valuing or timing this card. Existing mechanical playability evaluation remains shared with bots.
- Dedicated card artwork generation.

## Ambiguities

None remain for planning. The issue's unspecified details are resolved from existing project conventions as follows:

- No cost was stated, so the action has an empty cost and grants the full 300 gold.
- "10% coefficient" and "1/month" mean +10 percentage points with one percentage point removed per in-game month, represented as human-readable `10.0` and `1.0` config values.
- The target is the selected country that owns the country-card deck; its attacker/defender role is irrelevant.
- Multiple ordinary deck copies follow the existing additive timed-modifier precedent, so bonuses stack and decay independently.
- War ending affects future draw/play eligibility only; it neither discards the card nor cancels an already-applied bonus because neither behavior was requested.
