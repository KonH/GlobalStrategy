# Spec: Card Config Refactor

## Feature Intent

As a game designer, I want a single unified action card config with a composable success-rate expression tree, structured resource conditions and costs, and a separate visual config, so that org and country action cards share one consistent schema that is extensible without touching C# models for balance or effect changes.

## Acceptance Criteria

### 1. Single unified settings config replaces both JSON files

- **Given** the game loads, **When** it reads card settings, **Then** it reads exactly one file (`Assets/Configs/action_config.json`) that covers both org actions and country actions.
- **Given** the old `country_action_config.json` existed, **When** the refactor ships, **Then** that file is deleted and all its action definitions appear in the unified file under a common `actions` array with a `ownerType` discriminator (e.g. `"org"` or `"country"`).
- **Given** the unified file, **When** the runtime loads it, **Then** `orgPools` and `defaults` (org/country hand sizes) are present at the top level, exactly as before — character defaults are removed.
- **Given** an action definition, **When** its `ownerType` is `"country"`, **Then** the country-specific fields (`targetRole`, `deckCopies`, `cooldownDays`) are present; org-specific fields (`effectIds`) are present when `ownerType` is `"org"`.

Representative unified schema excerpt:
```json
{
  "defaults": [
    { "ownerType": "org",     "handSize": 1 },
    { "ownerType": "country", "handSize": 0 }
  ],
  "actions": [
    {
      "actionId": "sphere_of_pressure",
      "ownerType": "country",
      "nameKey": "action.sphere_of_pressure.name",
      "descKey": "action.sphere_of_pressure.desc",
      "targetRole": "",
      "deckCopies": 3,
      "cooldownDays": 30,
      "conditions": [{ "type": "ResourceCondition", "resourceId": "influence", "minValue": 0 }],
      "cost": [],
      "successRate": { "type": "value", "members": [0.5] },
      "effectIds": ["sphere_of_pressure_effect"]
    },
    {
      "actionId": "discover_country",
      "ownerType": "org",
      "nameKey": "action.discover_country.name",
      "descKey": "action.discover_country.desc",
      "cost": [],
      "successRate": { "type": "value", "members": [0.75] },
      "effectIds": ["discover_country"]
    }
  ],
  "orgPools": [
    { "orgId": "Illuminati", "actionIds": ["discover_country"] }
  ]
}
```

### 2. `preDealtToHand` removed; initial hand computed from game state

- **Given** a new game starts for the first time, **When** the card system initialises the country hand, **Then** it evaluates each card's `resourceCondition` against current game state and deals any card whose condition passes, rather than reading a `preDealtToHand` flag.
- **Given** `preDealtToHand` was `true` on `sphere_of_pressure` (which has `influenceThreshold: 0`), **When** the new logic runs with a `resourceCondition` of `{ "resourceId": "influence", "minValue": 0 }`, **Then** `sphere_of_pressure` is dealt to the opening hand because the condition is satisfied at game start (influence ≥ 0).
- **Given** a card with `resourceCondition: { "resourceId": "influence", "minValue": 10 }`, **When** starting influence is 0, **Then** the card is NOT dealt to the opening hand.

### 3. `prices` / `goldCost` replaced by `cost`; gold removed

- **Given** an action definition, **When** it has no resource cost, **Then** `cost` is omitted or is an empty array `[]`.
- **Given** an action definition, **When** it has a non-gold resource cost, **Then** `cost` is an array of `{ "resourceId": "<id>", "amount": <n> }` entries.
- **Given** old entries that had `goldCost` or `prices` with `resourceId: "gold"`, **When** the refactor ships, **Then** those fields are absent; any gameplay cost is re-expressed in non-gold resources or omitted if gold was the only cost and gold is no longer in the economy.
- **Given** the `cost` array, **When** a `resourceId` appears in it, **Then** it must be a valid id from `resource_config.json`; the field is open-ended with no closed enum of valid values.

### 4. `influenceThreshold` replaced by DSL-expression `conditions` array

- **Given** an action definition, **When** it has play conditions, **Then** they are expressed as a `conditions` array of boolean DSL expressions using the same node format (`type`/`members`) as `successRate`, e.g.:
  ```json
  "conditions": [
    {
      "type": "gte",
      "members": [
        { "type": "influence" },
        { "type": "value", "members": [10] }
      ]
    }
  ]
  ```
- **Given** a condition expression, **When** evaluated, **Then** it uses the same evaluation context (scope) as `successRate` — `influence()` resolves to the player org's influence in the target country; `opinion()` resolves to the relevant opinion value.
- **Given** a condition expression, **When** all entries in `conditions` evaluate to `true`, **Then** the card is playable.
- **Given** any condition entry that evaluates to `false`, **Then** the card is not playable (all conditions are AND-ed).
- **Given** an action with no play conditions, **When** `conditions` is omitted or empty, **Then** the card is always playable.
- **Given** the DSL node type set, **When** authoring conditions, **Then** the following boolean comparison ops are available: `gte` (`>=`), `lte` (`<=`), `gt` (`>`), `lt` (`<`), `eq` (`==`); their `members` are two float-returning DSL nodes.
- **Given** the C# model, **When** deserialized, **Then** `ActionDefinition` has a `List<ExpressionNode> Conditions` field; each node is evaluated as a boolean using the same `ExpressionNode` type used for `successRate`.

### 5. `cooldownMonths` replaced by `cooldownDays`

- **Given** an action that previously had `cooldownMonths: 2`, **When** converted, **Then** the action has `cooldownDays: 60` (1 month = 30 days conversion).
- **Given** `cooldownDays: 0` or the field absent, **When** the action is played, **Then** no cooldown is applied.
- **Given** the in-game timer runs in days, **When** a cooldown is active, **Then** the cooldown counter decrements in days, matching the game clock's native unit.

### 6. Flat success-rate fields replaced by expression-tree `successRate`

- **Given** the old `successRateBase: 0.3, successRateInfluenceDivisor: 2`, **When** converted to the new schema, **Then** the action has:
  ```json
  "successRate": {
    "type": "add",
    "members": [
      { "type": "value", "members": [0.3] },
      { "type": "div", "members": [
        { "type": "influence" },
        { "type": "value", "members": [2] }
      ]}
    ]
  }
  ```
- **Given** `successRateInfluenceDivisor: 0` (divisor unused), **When** converted, **Then** the expression is simply `{ "type": "value", "members": [0.5] }` — no `add` or `div` wrapper.
- **Given** an expression tree at runtime, **When** evaluated, **Then** `value` returns the literal number in `members[0]`; `add` returns the sum of all `members`; `sub` returns `members[0] - members[1]`; `mul` returns the product of all `members`; `div` returns `members[0] / members[1]` (denominator ≠ 0); `clamp` returns `members[0]` clamped between `members[1]` and `members[2]`; `influence` returns the player's current influence in the target country; `opinion` returns the current opinion value for the relevant relationship.
- **Given** `div` with a zero denominator, **When** evaluated, **Then** the system returns 0 (safe default) rather than throwing.
- **Given** the C# model, **When** deserialized, **Then** a polymorphic `SuccessRateNode` (or equivalent discriminated-union-style class) is produced from the JSON using the `type` field as the discriminator and `members` as the child nodes or literal arguments.
- **Given** the supported op set, **When** authoring expressions, **Then** the following ops are available in this iteration: `value`, `add`, `sub`, `mul`, `div`, `clamp`, `influence`, `opinion`.

### 7. Influence / opinion outcomes move to effect definitions

- **Given** an action that previously carried `influenceOnSuccess: 10`, `opinionModifierSourceId: "letter_of_commendation"`, `opinionModifierValue: 50`, `opinionModifierChangeValue: -1`, **When** the refactor ships, **Then** those fields are absent from the action definition.
- **Given** those outcomes are now needed, **When** the action succeeds, **Then** they are driven by entries in `effectIds` that reference extended effect definitions in `effect_config.json`.
- **Given** an effect definition of type `InfluenceChange`, **When** applied, **Then** it carries typed parameters specifying the influence delta (exact C# class design is a plan-phase concern).
- **Given** an effect definition of type `OpinionModifier`, **When** applied, **Then** it carries typed parameters for `sourceId`, `initialValue`, and `decayPerMonth` (exact C# class design is a plan-phase concern).
- **Given** an effect definition of type `DiscoverCountry`, **When** applied, **Then** it carries typed parameters for country-selection weighting (e.g. `minCountryChance`) — see criterion 8 for details.
- **Given** `EffectConfig.cs` today, **When** extended, **Then** `ActionEffectDefinition` uses typed subclasses per `effectType` (not a freeform dictionary). Every effect type that carries parameters gets its own typed C# class. Effect types needed in this iteration: at minimum `DiscoverCountry`, `InfluenceChange`, `OpinionModifier`.

### 8. `minCountryChance` lives in `DiscoverCountry` effect parameters

- **Given** the org action `discover_country` previously had `minCountryChance: 0.01` as a bare float on the action definition, **When** the refactor ships, **Then** that field is absent from the action definition entirely.
- **Given** the `DiscoverCountry` effect type, **When** its parameters are deserialized, **Then** `minCountryChance` (and any influence/opinion weighting for country selection) is a typed field on the `DiscoverCountryEffectParams` C# class (or equivalent), not on `ActionDefinition`.
- **Given** the action definition for `discover_country`, **When** reading the JSON, **Then** there is no `countryTargeting` block on the action; all targeting logic is encapsulated inside the effect definition referenced via `effectIds`.

### 9. Visual config is the existing `ActionVisualConfig` ScriptableObject

- **Given** the game loads visual data for cards, **When** it resolves card visuals, **Then** it uses the existing Unity ScriptableObject `ActionVisualConfig` (at `Assets/Scripts/Unity/Common/ActionVisualConfig.cs`), which stores `Sprite frontImage` and `Sprite backImage` per `actionId` in its `ActionVisualEntry` list.
- **Given** an `actionId` that has no entry in `ActionVisualConfig`, **When** a card is displayed, **Then** the UI falls back to a default visual (no exception thrown).
- **Given** `action_config.json`, **When** the refactor ships, **Then** the `rarity` field remains on action definitions in that file (it may be used by logic-layer systems in the future).
- **Given** `ActionVisualConfig`, **When** the refactor ships, **Then** no changes are required to it; Sprite references continue to be assigned in the Unity Inspector as before.

### 10. C# models updated to match new schema

- **Given** the new unified config, **When** deserialized by `ActionConfig.cs`, **Then** the old `ActionPrice` / `Prices` list is replaced by a `Cost` list using the same `{ ResourceId, Amount }` shape; `ActionDefinition` gains `OwnerType`, a `List<ExpressionNode> Conditions` field (same node type as `successRate`), `CooldownDays`, and `SuccessRateNode` fields; `MinCountryChance` is removed from `ActionDefinition` and lives instead in the `DiscoverCountryEffectParams` (or equivalent) typed class in the effect config.
- **Given** `CountryActionConfig.cs` and `CountryActionDefinition`, **When** the refactor ships, **Then** those classes are deleted (country actions use `ActionDefinition` with `ownerType: "country"`).
- **Given** the unified `ActionConfig`, **When** the `Find(actionId)` helper is called, **Then** it searches the single `actions` list regardless of owner type.
- **Given** existing consumers of `CountryActionConfig` in `Game.Main`, **When** the refactor ships, **Then** they are updated to resolve country actions from the unified `ActionConfig` instead.

## Out of Scope

- Changes to the map system, ECS world tick, or save/load format beyond what is needed to wire the new config fields.
- Adding new card actions or new effects beyond what is required to replicate existing behaviour under the new schema.
- UI changes to the card display beyond wiring up the new visual config source.
- Multiplayer or network serialization of the config.
- Character owner type — character defaults are removed and no character action cards are introduced in this refactor.
