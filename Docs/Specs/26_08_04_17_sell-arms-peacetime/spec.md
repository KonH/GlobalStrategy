# Spec: Sell Arms During Peacetime

## Feature Intent

As a player, I want to draw and play Sell Arms for a country that is at peace (when military-advisor opinion is high enough), so that the temporary troop-damage bonus can raise Declare War / Revenge win-chance previews and strengthen any war that starts while the bonus is still decaying.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- The selected country is at peace (not participating in any war) and the playing organization's opinion with that country's military advisor is at least 80.
  - A free hand slot is filled through the ordinary country-card draw => Sell Arms may be drawn into that slot the same way other eligible country cards are.
  - A playable Sell Arms copy is in hand and the player has enough gold for its cost => the player can play it successfully; the country's troop-damage bonus rises by 10 percentage points and the gold cost is paid; the card leaves the hand and the vacated slot refills through the ordinary draw flow.
- The selected country is at peace and military-advisor opinion is below 80 (or no military advisor / opinion can be resolved).
  - The deck/draw system evaluates a free hand slot for that country => Sell Arms is not eligible to be drawn.
  - A held Sell Arms copy is shown in the Actions panel => it is unplayable for the ordinary insufficient-opinion reason (not a wartime / war-ended reason).
- The selected country is currently at war and military-advisor opinion is at least 80.
  - Draw eligibility and playability for Sell Arms remain available as today when the opinion gate is met; removing the wartime-only restriction does not remove wartime play.
- A Sell Arms copy is already in hand when war state changes.
  - The country enters or leaves a war while opinion stays at least 80 => the copy stays in hand and remains playable; war state alone no longer blocks or unblocks this card.
  - The country leaves a war while opinion stays below 80 => the copy stays in hand and remains unplayable for the opinion reason only.
- The selected country is at peace, Sell Arms has just been played, and a Declare War or Revenge card naming a potential opponent is in that country's hand.
  - The player opens the Actions panel (or it refreshes after the play) => the win-probability badge on that Declare War / Revenge card updates to reflect the stronger estimated combat strength from the new damage bonus.
  - Months pass and the bonus decays while the Declare War / Revenge card remains in hand => the badge updates again to match the remaining bonus.
- A country has an active remaining Sell Arms damage bonus (from a peacetime or wartime play) and then enters a new war before the bonus fully decays.
  - Battles in that war resolve => the country's troop damage uses the remaining bonus for as long as it has not yet decayed to zero; starting the war does not clear or reset the bonus.
  - The war later ends while some bonus remains => the leftover bonus continues its ordinary monthly decay (unchanged existing behavior).
- Cost, stacking, and decay rules for Sell Arms are otherwise unchanged from the live card.
  - Playing Sell Arms still costs 200 gold and applies only the troop-damage bonus effect (no separate gold grant from the card).
  - Multiple plays for the same country still stack additively with independent 1-point-per-month decay contributions, and the bonus never goes negative.

## Tech Notes

- **Remove wartime gate only on `sell_arms`** in `Assets/Configs/action_config.json`:
  - Delete the `gte(isInWar, 1)` condition from the `sell_arms` action entry.
  - Keep `gte(opinion, 80)` with `targetRole: "military_advisor"`.
  - Keep live cost `[{ "resourceId": "gold", "amount": 200.0 }]` and `effectIds: ["sell_arms_damage_bonus_effect"]` unchanged. Do not reintroduce `sell_arms_gold_grant_effect` or empty-cost / +300 gold grant behavior from the original sell-arms spec.
  - Leave `isInWar` on other cards (`ultimatum`, `surrender`, etc.) untouched.

- **Draw / playability / held-card reasons reuse existing evaluators:**
  - `CountryActionConditionContext.Build`, `DrawCardSystem.DrawCountryCards`, `ActionPlayability.Evaluate`, and `VisualStateConverter.BuildEntry` already evaluate `sell_arms` conditions from config; removing the war condition is sufficient for peacetime draw and play.
  - `VisualStateConverter`'s `war_ended` mapping (failed expression containing `isInWar` → `action.country.unplayable.war_ended`) stops applying to Sell Arms because that condition is gone. The reason string and mapping remain for other wartime-gated country cards.
  - Opinion failures continue to surface through the existing insufficient-opinion unplayable path.

- **Bonus resource and combat / preview coupling (no second formula):**
  - Effect remains `sell_arms_damage_bonus_effect` in `Assets/Configs/effect_config.json`: `CountryResourceModifier` on `troops_damage_bonus_percent`, `initialValue: 10.0`, `decayPerMonth: 1.0`, applied by `CreateActionEffectSystem`.
  - `DamageCollector.Compute` already multiplies country damage by `(1 + troops_damage_bonus_percent / 100)` (and revenge bonus separately). Collectors refresh live `damage` for peacetime countries as well as wartime ones.
  - `WarWinChanceEstimator` (war-result-preview) reads live `damage` / `durability` resources, which already include applied Sell Arms bonuses per the locked "live combat resources" decision in `Docs/Specs/26_08_03_13_war-result-preview/spec.md`. Peacetime play → bonus → collector refresh → Declare War / Revenge badge update without a Sell-Arms-specific preview branch.
  - Real wars consume the same live damage resources via `WarBattleSystem` / `WarProgressSnapshot`, so a war started while remaining monthly decay is active automatically applies the leftover bonus.

- **Localization:**
  - Live `action.sell_arms.desc` is already peacetime-neutral ("Spend gold to boost this country's troop damage.") and does not claim the country must be at war. No copy change is required for this feature unless product later wants explicit peacetime / future-war wording.
  - No new unplayable-reason key is required.

- **Tests to update (implementation follow-through, not product behavior):**
  - Config / playability / draw / visual-state tests that currently assert Sell Arms requires an active war or reports `war_ended` after peace must be revised to match peacetime eligibility with the opinion gate only (`StringConfigParityTests`, `ActionPlayabilityTests`, `DrawCardSystemTests`, `VisualStateConverterCountryActionsOpinionGateTests`, and related Sell Arms coverage).
  - Prefer adding coverage that peacetime play raises live damage and that win-chance projection / a subsequent war sees the remaining bonus through existing collectors rather than inventing a parallel path.

## Out of Scope

- Changing military-advisor opinion threshold, gold cost, bonus magnitude, monthly decay rate, stacking rules, or deck copy count.
- Reintroducing an organization gold grant on Sell Arms play.
- Changing `DamageCollector`, `WarWinChanceEstimator` formulas, or war battle resolution beyond relying on their existing live-resource reads.
- New UI chrome, tooltips, or badges on the Sell Arms card itself explaining that the bonus will affect future wars or preview percents (Declare War / Revenge badges already reflect live damage).
- Changing Ultimatum, Surrender, or any other card that still requires `isInWar`.
- Removing the shared `isInWar` expression field or the `war_ended` unplayable reason used by other cards.
- Dedicated Action Log entries, bonus HUD widgets, or card artwork changes.
- Bot strategy changes for when to value peacetime Sell Arms (shared mechanical playability remains).

## Ambiguities

None. Assumed defaults from the issue and live config:

- Military-advisor opinion ≥ 80 remains required in peacetime; only `isInWar` is removed.
- Gold cost stays 200; effect list stays damage-bonus only.
- Stacking and monthly decay are unchanged.
- Existing localization needs no wartime-wording update; no extra peacetime UI hint is required.
