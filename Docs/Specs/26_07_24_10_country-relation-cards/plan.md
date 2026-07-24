# Plan: Country Relation Cards

## Spec summary

Source: `Docs/Specs/26_07_24_10_country-relation-cards/spec.md`.

Two new player-facing country action cards, "Make Friend" and "Make Rival": 50 gold cost, `deckCopies: 3`, gated by (a) the player-org's opinion with the selected country's `diplomacy_advisor` character being ≥30 and (b) at least one other available country existing that is currently neither friend nor rival of the selected country ("suitable candidate"). On play, the system auto-picks a candidate (50% proximity-weighted anchored on the selected country, 50% uniform-random, among all available countries regardless of per-org discovery state) and calls `CountryRelations.SetRelation` to set a Friend/Rival relation. Both cards always succeed once played — there is no roll of any kind. A project-wide cooldown-display cleanup (two dead artifacts) is bundled in as a small side task.

**Correction to the spec's Tech Notes, verified directly from source during this planning pass:** there is no success-rate/roll mechanism anywhere in the codebase, for any card, not just these two. `src/Game.Configs/ActionConfig.cs`'s `ActionDefinition` has no rate field at all, and the play pipeline (`src/Game.Main/GameLogic.cs` lines 199-207: `InitActionFromPlayCardSystem` → `CheckActionConditionSystem` → `DeductActionCostSystem` → `ActionSucceededSystem` → `CreateActionEffectSystem` → `DiscoverCountrySystem` → `RemoveCardFromHandSystem` → `CheckHandSizeSystem` → `DrawCardSystem`) unconditionally promotes every `ActionValid` card to `ActionSucceeded` with no RNG involved (`src/Game.Systems/ActionSucceededSystem.cs`). "Always succeeds" is simply how every card already works; these two cards need no special no-roll handling, just ordinary `conditions`/`cost`/`effectIds` entries like `sphere_of_pressure`/`letter_of_commendation_*`/`royal_audience`.

## Goal

Add `make_friend`/`make_rival` as ordinary country action cards, close a real pre-existing gap where the DSL's `opinion` context field is never populated by any of the four places that build an `ExpressionContext`, add a new `hasSuitableRelationTarget` DSL node, add a `SetCountryRelation` effect type resolved through the existing marker-component/system pattern (mirroring `DiscoverCountryEffect`/`DiscoverCountrySystem`), and perform the two trivial cooldown-artifact cleanups. No new UI code is needed for card rendering — `CountryActionsView`/`VisualStateConverter.UpdateCountryActions` already render any `ownerType: "country"` config entry generically — but a small, honest UI-reason-code fix is needed so the unplayable label doesn't show a nonsensical "Requires 0 control" for these two opinion/candidate-gated cards.

## Approach

### 1. Four call sites build an `ExpressionContext`, not two — all four need `Opinion` and the new `HasSuitableRelationTarget` wired

`src/Game.Configs/ExpressionNode.cs` already declares `ExpressionContext.Opinion` (line 7) and `ExpressionNode.Evaluate`'s `case "opinion": return ctx.Opinion;` (lines 53-55) — both correct, just fed `0.0` everywhere because nothing sets `ctx.Opinion`. Direct grep of every `new ExpressionContext` construction site turns up **four** places, not the two named in the spec's Tech Notes:

1. `src/Game.Systems/ActionPlayability.cs::Evaluate`, line 13 — the actual play-time gate (called by `CheckActionConditionSystem.Update`, `src/Game.Systems/CheckActionConditionSystem.cs` line 34) and the general-purpose playability query.
2. `src/Game.Systems/DrawCardSystem.cs::DrawCountryCards`, line 93 — draw-into-hand eligibility.
3. `src/Game.Main/InitSystem.cs::CreateCountryActionEntities`, line 664 — initial-hand-population eligibility at game start.
4. `src/Game.Main/VisualStateConverter.cs::BuildEntry`, line 614 — drives `ActionCardEntry.IsUnplayable`/`.UnplayableReason` for both hand and deck display in `CountryActionsView`.

Missing site 4 is not cosmetic: without wiring `ctx.Opinion`/`ctx.HasSuitableRelationTarget` here, `BuildEntry` would keep evaluating both new conditions against the default `0.0`, so the UI would show "Make Friend"/"Make Rival" as permanently unplayable even once the real opinion/candidate gates are satisfied — contradicting `ActionPlayability.Evaluate`'s real gate state used by the actual play pipeline. All four sites must resolve the diplomacy advisor and candidate count identically, so this is done via two new shared helpers (§2), called unconditionally at each site exactly the way `Control` is already always precomputed there regardless of whether a given card's conditions reference it.

### 2. Extract `GetTargetCharacterByCountryAndRole` into a new `src/Game.Systems/CharacterQuery.cs`

`src/Game.Systems/CreateActionEffectSystem.cs`'s private `static string GetTargetCharacterByCountryAndRole(World world, string countryId, string targetRole)` (lines 134-147) is a plain query helper (iterates `Character` entities, no system-entry-point call), so extracting it does not violate `.claude/rules/unity/ecs_patterns.md`'s no-system-to-system-calls rule. Home: a new `src/Game.Systems/CharacterQuery.cs`, alongside `ResourceQuery.cs` — not `CountryRelations.cs` (that file is relation-specific) and not a method added onto `CreateActionEffectSystem` itself (that would keep it system-owned, discouraging reuse). Signature: `public static string GetTargetCharacterByCountryAndRole(IReadOnlyWorld world, string countryId, string targetRole)` (relaxed from `World` to `IReadOnlyWorld` since it's read-only and `World` already satisfies that interface everywhere it's called). Delete the private copy in `CreateActionEffectSystem.cs` and update its one call site (line 73, inside the `OpinionModifierEffectParams` branch) to `CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, def.TargetRole)`.

### 3. `CountryRelations.cs` gains `GetSuitableRelationCandidates` (single source of truth for both the DSL gate and the resolution system)

Add to `src/Game.Systems/CountryRelations.cs`:
- `public static List<string> GetSuitableRelationCandidates(IReadOnlyWorld world, string countryId)` — iterates `Country` entities (`TypeId<Country>.Value`, the same archetype `DiscoverCountrySystem` iterates for its candidate pool at lines 46-54 — note `InitSystem.Run` only ever creates a `Country` entity for `IsAvailable` countries, line 27, so "all `Country` entities" already **is** "all available countries"; no `CountryConfig`/`IsAvailable` re-check or extra parameter is needed at any of the four call sites), excluding `countryId` itself, keeping only candidates where `GetRelation(world, countryId, candidateId)` returns `null`.
- `public static bool HasSuitableRelationTarget(IReadOnlyWorld world, string countryId) => GetSuitableRelationCandidates(world, countryId).Count > 0;` — thin wrapper; bounded by ~20 countries so recomputing the full list just to check "any" is cheap, consistent with the sibling `CountryRelations`/`VisualStateConverter` "unconditional recompute is fine at this scale" precedent from `Docs/Specs/26_07_23_06_country-relations/plan.md` §6.

`SetCountryRelationSystem` (§6) reuses `GetSuitableRelationCandidates` directly for its candidate pool, so the "is there a suitable candidate" gate and the "which candidates can be picked" resolution can never disagree.

### 4. New `hasSuitableRelationTarget` DSL node

`src/Game.Configs/ExpressionNode.cs`: add `public double HasSuitableRelationTarget { get; set; }` to `ExpressionContext` (alongside `Control`/`Opinion`) and a `case "hasSuitableRelationTarget": return ctx.HasSuitableRelationTarget;` branch to `Evaluate`, following the exact shape of the existing `control`/`opinion` cases.

### 5. Wire `Opinion` and `HasSuitableRelationTarget` at all four sites (§1)

At each of the four sites, right where `ctx.Control` is already being set, add (unconditionally, for every country-card evaluation, matching how `Control` is already always precomputed regardless of whether the specific card's conditions reference it):
```csharp
string diplomacyCharId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "diplomacy_advisor");
double opinion = string.IsNullOrEmpty(diplomacyCharId) ? 0.0 : ResourceQuery.GetValue(world, diplomacyCharId, $"opinion_{orgId}");
double hasSuitableTarget = CountryRelations.HasSuitableRelationTarget(world, countryId) ? 1.0 : 0.0;
var ctx = new ExpressionContext { Control = orgControl, Opinion = opinion, HasSuitableRelationTarget = hasSuitableTarget };
```
- `ActionPlayability.cs::Evaluate` (line 13): `countryId` is already a parameter (nullable — guard with `!string.IsNullOrEmpty(countryId)`, defaulting `opinion`/`hasSuitableTarget` to `0.0` when null/empty, i.e. org-card evaluation is unaffected).
- `DrawCardSystem.cs::DrawCountryCards` (line 93): `countryId` is already a parameter, always non-empty in this method (only called for country decks).
- `InitSystem.cs::CreateCountryActionEntities` (line 664): `entry.CountryId` is already in scope from the enclosing loop.
- `VisualStateConverter.cs::BuildEntry` (line 614): `countryId`/`orgId` are already in scope from `UpdateCountryActions`'s caller-level locals — `BuildEntry`'s signature needs `orgId`/`countryId` added as parameters (currently only receives `actionId, slotIndex, isInHand, orgControl, usedTotal`); update its two call sites at lines 585 and 598 accordingly.

Both `InitSystem.cs` and `VisualStateConverter.cs` already have `using GS.Game.Systems;` (confirmed by direct read) — no new using directive is needed at either site.

### 6. `SetCountryRelationEffectParams` effect type

`src/Game.Configs/EffectConfig.cs`: add `public class SetCountryRelationEffectParams : ActionEffectDefinition { public RelationKind Kind { get; set; } }` (needs `using GS.Game.Common;`), and a `case "SetCountryRelation": item = obj.ToObject<SetCountryRelationEffectParams>(serializer)!; break;` branch in `ActionEffectDefinitionListConverter.ReadJson`, alongside the existing `DiscoverCountry`/`ControlChange`/`OpinionModifier` cases. Newtonsoft deserializes a JSON string (`"Friend"`/`"Rival"`) directly into the `RelationKind` enum with no extra converter needed (its default enum handling matches by name).

**This is only sufficient for the Unity Editor/Player path.** A second, independently-maintained System.Text.Json converter also exists (`src/Core.Configs.IO/ActionEffectDefinitionListConverter.cs`), used by `Game.ConsoleRunner` and `Game.WebClient` to load the same `EffectConfig` type. Left unaddressed, `make_friend`/`make_rival` would silently no-op for those two hosts (gold deducted, card leaves hand, but `SetCountryRelationSystem` never sees a marker because the effect deserializes as the untyped base `ActionEffectDefinition`) — this exact gap was caught in plan review. Rather than patch the second converter to keep two implementations in permanent lockstep, §14 below removes it entirely by migrating `Core.Configs.IO` off System.Text.Json onto Newtonsoft, so this one converter is the only one that ever needs updating for a new effect type again.

Two new entries in `Assets/Configs/effect_config.json`:
```json
{
  "effectId": "make_friend_effect",
  "effectType": "SetCountryRelation",
  "nameKey": "effect.make_friend_effect.name",
  "descKey": "effect.make_friend_effect.desc",
  "kind": "Friend"
},
{
  "effectId": "make_rival_effect",
  "effectType": "SetCountryRelation",
  "nameKey": "effect.make_rival_effect.name",
  "descKey": "effect.make_rival_effect.desc",
  "kind": "Rival"
}
```

### 7. `make_friend`/`make_rival` entries in `action_config.json`

Following the exact shape every existing entry uses (`sphere_of_pressure`/`letter_of_commendation_*`/`royal_audience` — verified current schema: `actionId, ownerType, rarity, nameKey, descKey, targetRole, deckCopies, conditions, cost, effectIds`, no rate/cooldown field exists on any entry):
```json
{
  "actionId": "make_friend",
  "ownerType": "country",
  "rarity": "Standard",
  "nameKey": "action.make_friend.name",
  "descKey": "action.make_friend.desc",
  "targetRole": "diplomacy_advisor",
  "deckCopies": 3,
  "conditions": [
    { "type": "gte", "members": [ { "type": "opinion" }, { "type": "value", "value": 30 } ] },
    { "type": "gte", "members": [ { "type": "hasSuitableRelationTarget" }, { "type": "value", "value": 1 } ] }
  ],
  "cost": [{ "resourceId": "gold", "amount": 50.0 }],
  "effectIds": ["make_friend_effect"]
},
{
  "actionId": "make_rival",
  "ownerType": "country",
  "rarity": "Standard",
  "nameKey": "action.make_rival.name",
  "descKey": "action.make_rival.desc",
  "targetRole": "diplomacy_advisor",
  "deckCopies": 3,
  "conditions": [
    { "type": "gte", "members": [ { "type": "opinion" }, { "type": "value", "value": 30 } ] },
    { "type": "gte", "members": [ { "type": "hasSuitableRelationTarget" }, { "type": "value", "value": 1 } ] }
  ],
  "cost": [{ "resourceId": "gold", "amount": 50.0 }],
  "effectIds": ["make_rival_effect"]
}
```
`targetRole: "diplomacy_advisor"` is reused here purely so `CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, def.TargetRole)` resolves the right character for the **opinion gate lookup** — it is not used to target the `SetCountryRelation` effect itself (that effect only needs `orgId`/`countryId`/`Kind`, no character). This reuse of `TargetRole` for a different purpose than `OpinionModifierEffectParams` (which reads it to target the character the opinion effect is *applied to*) is safe because both readers key off the same `(countryId, targetRole)` → character resolution; no conflict. No new character role is needed — `Assets/Configs/character_config.json` already defines `roleId: "diplomacy_advisor"` (line 51) with existing country pools assigning it (e.g. line 216 onward, one per country).

`Assets/Configs/character_config.json`'s country IDs already match `country_config.json` per the existing convention (`.claude/rules/config_validation.md`) — no cross-validation needed since no new country-ID references are introduced by this feature.

### 8. `SetCountryRelationEffect` marker component + `CreateActionEffectSystem` dispatch

Add to `src/Game.Components/CountryRelation.cs` (same file as `CountryRelation`, the natural home — component files in this codebase already group related structs, e.g. `GameLogEffects.cs`, `ResourceChangeEffect.cs`):
```csharp
public struct SetCountryRelationEffect {
    public string EffectId;
    public string OrgId;
    public string CountryId;
    public RelationKind Kind;
}
```
Not `[Savable]` — same-tick transient marker consumed and destroyed by `SetCountryRelationSystem` in the same `GameLogic.Update` tick it's created, exactly like the existing `DiscoverCountryEffect` (also not `[Savable]`, also not listed in `SavableDiscoveryTests`'s `ExpectedSavable`/`ExpectedNotSavable`).

`src/Game.Systems/CreateActionEffectSystem.cs`: extend the `if (effectDef is X) {...} else if (effectDef is Y) {...}` chain (lines 39-103) with a new branch mirroring the `DiscoverCountryEffect` marker creation at lines 40-41:
```csharp
} else if (effectDef is SetCountryRelationEffectParams relationParams && !string.IsNullOrEmpty(countryId)) {
    int e = world.Create();
    world.Add(e, new SetCountryRelationEffect { EffectId = effectId, OrgId = orgId, CountryId = countryId, Kind = relationParams.Kind });
}
```

### 9. New `src/Game.Systems/SetCountryRelationSystem.cs`

Mirrors `DiscoverCountrySystem`'s (`src/Game.Systems/DiscoverCountrySystem.cs`, 104 lines) two-level shape — collect-all-markers then per-marker resolve — but anchored on the **selected country** (`CountryId` on the marker) rather than an org HQ, so no `hqCountryByOrgId` dictionary parameter is needed:

```csharp
public static class SetCountryRelationSystem {
    public static void Update(World world, int proximityEntity, Random rng) {
        int[] required = { TypeId<SetCountryRelationEffect>.Value };
        var toProcess = new List<(int entity, string orgId, string countryId, RelationKind kind)>();
        foreach (var arch in world.GetMatchingArchetypes(required, null)) {
            SetCountryRelationEffect[] effects = arch.GetColumn<SetCountryRelationEffect>();
            for (int i = 0; i < arch.Count; i++) {
                toProcess.Add((arch.Entities[i], effects[i].OrgId, effects[i].CountryId, effects[i].Kind));
            }
        }
        if (toProcess.Count == 0) { return; }

        ProximityMapData pm = default;
        bool hasPm = proximityEntity >= 0;
        if (hasPm) { pm = world.Get<ProximityMapData>(proximityEntity); }

        foreach (var (entity, orgId, countryId, kind) in toProcess) {
            ResolveRelation(world, rng, orgId, countryId, kind, hasPm, pm);
            world.Destroy(entity);
        }
    }
    // ResolveRelation: builds candidates via CountryRelations.GetSuitableRelationCandidates(world, countryId);
    // no-ops (still destroys the marker) if empty (state may have shifted since the play-time gate passed —
    // no crash, same "no-op on stale/invalid state" spirit as CountryRelations.SetRelation's self-relation guard).
    // Otherwise: 50/50 coin flip (rng.NextDouble() < 0.5) between (a) an inverse-distance-weighted pick
    // identical to DiscoverCountrySystem's algorithm at lines 62-93 (same 1/distance weighting + minChance
    // floor, anchored on countryId instead of an org HQ, falling back to uniform pick if !hasPm or
    // pm.Distances is null) and (b) a uniform pick via rng.Next(candidates.Count); then
    // CountryRelations.SetRelation(world, countryId, chosen, kind); then emits RelationSetApplied (§10);
    // then the marker entity is destroyed by the caller loop above.
}
```

Wire it into `src/Game.Main/GameLogic.cs`'s `Update`, right after the existing `DiscoverCountrySystem.Update` call (line 204):
```csharp
DiscoverCountrySystem.Update(_world, _proximityEntity, _rng, _hqCountryByOrgId);
SetCountryRelationSystem.Update(_world, _proximityEntity, _rng);
RemoveCardFromHandSystem.Update(_world);
```
`_proximityEntity` and `_rng` are both existing fields already in scope at this call site (`src/Game.Main/GameLogic.cs` lines 19/26) — no new fields needed on `GameLogic`.

### 10. Game Log: `RelationSetApplied`

Add to `src/Game.Components/GameLogEffects.cs` (needs `using GS.Game.Common;` added to the file):
```csharp
public struct RelationSetApplied {
    public string OrgId;
    public string CountryId;
    public string TargetCountryId;
    public RelationKind Kind;
}
```
Emitted by `SetCountryRelationSystem` as a separate sibling entity right after `CountryRelations.SetRelation` succeeds, following the exact "separate sibling entity, not attached to the effect component" convention `DiscoverCountrySystem` uses for `DiscoveryApplied` (`DiscoverCountrySystem.cs` lines 96-100) — for `Docs/Specs/26_07_18_07_action-log-ui/` consumption (out of scope to wire a display line for it in this plan; the log-type-registration skill `propose-log-type` is the follow-up path if a rendered line is wanted, and is explicitly out of scope per the spec's own "gameplay effect of a relation" out-of-scope note not covering game-log plumbing either way — this plan only emits the raw event entity, matching the existing `ControlEffectApplied`/`OpinionEffectApplied`/`DiscoveryApplied` precedent of "system emits the event; log-UI consumption is a separate concern").

### 11. UI reason-code precision (small, necessary fix — not new UI wiring)

`VisualStateConverter.cs::BuildEntry` (lines 608-625) currently derives `unplayableReason` as either `"pool_full"` (hardcoded to `sphere_of_pressure`) or a blanket `"insufficient_control"` whenever **any** condition fails — harmless today because every existing card's conditions happen to only ever gate on `control`. For "Make Friend"/"Make Rival", whose conditions gate on `opinion`/`hasSuitableRelationTarget`, leaving this as-is would make `CountryActionsView.ExtractMinControl` (`Assets/Scripts/Unity/UI/CountryActionsView.cs` lines 138-147, which specifically searches for a `gte(control, value)` node and returns `0` if none is found) format a misleading "Requires 0 control" label. Fix, scoped narrowly:
- In `BuildEntry`, when a condition fails, inspect `cond.Members[0].Type` (every condition in this codebase is shaped `gte(field, value)`) to classify the reason: `"control"` → keep existing `"insufficient_control"`; `"opinion"` → new `"insufficient_opinion"`; `"hasSuitableRelationTarget"` → new `"no_suitable_target"`. `poolFull`'s existing precedence (checked before iterating conditions) is unchanged.
- `CountryActionsView.cs`'s reason-resolution branch (lines 66-74) gains two more cases mirroring the existing ones: `"insufficient_opinion"` → format `action.country.unplayable.insufficient_opinion` with the threshold (generalize `ExtractMinControl` to `ExtractConditionThreshold(def, fieldType)` accepting the field name to search for, reusable for both `"control"` and `"opinion"`); `"no_suitable_target"` → `action.country.unplayable.no_suitable_target` with no numeric argument.
- New locale keys (§12): `action.country.unplayable.insufficient_opinion` → `"Requires {0} opinion with the diplomacy advisor"`; `action.country.unplayable.no_suitable_target` → `"No suitable target country available"`.

This is the only `Assets/Scripts/Unity/UI/*.cs` change this feature needs — everything else in `CountryActionsView`/`ActionCardBuilder` already renders any `ownerType: "country"` config entry generically (confirmed by reading the current file: name/desc/cost/art/click-handling are all driven off `ActionConfig.Find(card.ActionId)` and `ActionVisualConfig.FindFront(card.ActionId)`, with no card-type-specific branches beyond the `pool_full`/`insufficient_control` reason check and the `sphere_of_pressure`-specific pool-full flag). No new UXML/USS/view class is needed.

### 12. Locale keys and card art

`Assets/Localization/en.asset` / `ru.asset` (same placeholder-English-text-for-Russian convention as prior action-card locale additions):
```
action.make_friend.name → "Cordial Accord"
action.make_friend.desc → "A treaty of friendship, sealed with warm words and mutual advantage — the first step toward a lasting partnership."
action.make_rival.name → "Declaration of Rivalry"
action.make_rival.desc → "A pointed rebuke, delivered through official channels — from this day, cooperation gives way to competition."
action.country.unplayable.insufficient_opinion → "Requires {0} opinion with the diplomacy advisor"
action.country.unplayable.no_suitable_target → "No suitable target country available"
```
(Names/flavour text per the spec's non-binding placeholder suggestions.)

`Assets/Configs/ActionVisualConfig.asset`: two new entries (`make_friend`, `make_rival`) following the existing `entries:` pattern, `backImage: {fileID: 0}` (falls back to `defaultBackImage`). Card art: generate via the `image-generation` skill / `generate-image` skill following the same 256x384, 19th-century-oil-painting-style convention as existing cards (e.g. `sphere_of_pressure.png` — check `Assets/Textures/Actions/` for the current file/`.meta` pattern before generating, since `.meta` sprite-slice format must match exactly for `ActionVisualConfig`'s `{fileID, guid, type: 3}` references to resolve).

### 13. Cooldown mechanic removal — two trivial, already-scoped cleanups

Re-verified directly, both still present exactly as the spec describes:
- `Assets/UI/HUD/CountryInfo/CountryInfo.uss` lines 72-86: delete the `/* Hand-only overlays — not shared with test/flying cards */` comment plus the `.action-card-cooldown-overlay` (lines 73-80) and `.action-card-cooldown-label` (lines 82-86) blocks. Leave the adjacent `.action-card-unplayable-reason` block (lines 88-93) untouched — it's still in active use (§11 adds more reason codes that reuse this same class).
- `README.md` line 17: drop `"cooldowns, "` from `"...per-country action card hands with costs, success rolls, cooldowns, and deck-building rules..."`, leaving `"...costs, success rolls, and deck-building rules..."`. The "success rolls" phrase stays accurate — it describes the other existing cards (`sphere_of_pressure`/`letter_of_commendation_*`/`royal_audience`), which is technically also inaccurate per this plan's own finding (§ Spec summary — no roll exists for *any* card), but rewriting that phrase is explicitly out of scope for this feature (only the cooldown wording is called out by the spec) and is left as-is to keep this plan's diff minimal and focused.

No `ActionDefinition` field, component, system, or test needs removing anywhere — nothing else references cooldowns.

### 14. `Core.Configs.IO`: migrate off System.Text.Json onto Newtonsoft, deleting the duplicate converter

**Root cause, confirmed by direct source read:** `.claude/rules/unity/plugins.md` documents that Unity cannot load System.Text.Json v8 at all ("conflicts with Unity's bundled version and causes a load error"), which is why `Core.Configs.IO` is deliberately excluded from `Assets/Plugins/Core` and instead consumed only by `Game.ConsoleRunner`/`Game.WebClient` (both standalone .NET 8 executables outside Unity). This is a sound reason for `Core.Configs.IO` to exist as a separate assembly — but there is no equally sound reason for it to use a *different JSON library* than the Newtonsoft-based `TextAssetConfig<T>` (`Assets/Scripts/Unity/DI/TextAssetConfig.cs`, plain `JsonConvert.DeserializeObject<TConfig>(_asset.text)`, no custom options) that Unity uses to load the exact same `Game.Configs` POCOs (`GeoJsonConfig`, `MapEntryConfig`, `CountryConfig`, `GameSettings`, `ResourceConfig`, `OrganizationConfig`, `CharacterConfig`, `ActionConfig`, `EffectConfig`, `ProvinceConfig` — confirmed via every `FileConfig<T>`/`StringConfig<T>` instantiation site across the codebase). That mismatch is what forces `EffectConfig`'s polymorphic `Effects` list to carry two independently-maintained converters (Newtonsoft in `Game.Configs`, System.Text.Json in `Core.Configs.IO`) for one C# type — the exact duplication plan review flagged, and the only place in the codebase where this duplication actually exists (confirmed by scoping: `Game.Evals`, `Game.Benchmarks`, `ECS.Viewer.Server`'s websocket wire protocol, `Game.Configs.Loader`'s GeoJSON tool, and `Game.WebClient`'s own locale-dictionary loading also use `System.Text.Json`, but none of them parse a type Unity also parses, so there is no duplication to remove there — migrating those would be unrelated churn, and is explicitly out of scope for this plan per user direction).

Change, scoped to `Core.Configs.IO` only:
- `src/Core.Configs.IO/FileConfig.cs`: replace `System.Text.Json.JsonSerializer.Deserialize<TConfig>(json, ConfigJsonOptions.Value)` with `Newtonsoft.Json.JsonConvert.DeserializeObject<TConfig>(json)` — no custom `JsonSerializerSettings`, matching `TextAssetConfig<T>` exactly for true cross-host parity. Update the `using` directives accordingly.
- `src/Core.Configs.IO/StringConfig.cs`: same change.
- Delete `src/Core.Configs.IO/ActionEffectDefinitionListConverter.cs` and `src/Core.Configs.IO/ConfigJsonOptions.cs` entirely — no longer needed. `EffectConfig.Effects`'s own `[JsonConverter(typeof(ActionEffectDefinitionListConverter))]` attribute (the Newtonsoft one already in `Game.Configs/EffectConfig.cs`, updated in §6) is picked up automatically by `JsonConvert.DeserializeObject`, so `SetCountryRelation` (and any future effect type) only ever needs adding in one place from here on.
- `Core.Configs.IO.csproj`: remove the `<PackageReference Include="System.Text.Json" Version="8.0.5" />` item, add `<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />` (a normal full reference, not the `ExcludeAssets="runtime;native"` form `Game.Configs.csproj` uses — that exclusion exists specifically because Unity supplies its own Newtonsoft runtime; `Core.Configs.IO` is never loaded by Unity, so it needs the real DLL, matching how `Game.ConsoleRunner.csproj`/`Game.WebClient.csproj` already reference it directly today for the same reason). `Game.Tests` picks this up transitively via its existing `Game.ConsoleRunner` project reference, same as it does today for the System.Text.Json path being replaced.
- No enum-typed field in any `Game.Configs` POCO currently relies on `ConfigJsonOptions`'s `JsonStringEnumConverter()` — grepped and confirmed the only enum property anywhere in `Game.Configs` is the new `SetCountryRelationEffectParams.Kind` this feature introduces (§6), which Newtonsoft already handles by default (confirmed for the Unity path). `PropertyNameCaseInsensitive = true` is likewise redundant to state explicitly — Newtonsoft's default `DefaultContractResolver` already matches property names case-insensitively (`.claude/rules/unity/plugins.md`). So dropping both options changes no observed behavior.
- `src/Game.Tests/StringConfigParityTests.cs` continues to exercise `FileConfig<T>` vs `StringConfig<T>` parity unchanged — both now go through the same `JsonConvert.DeserializeObject` call, so the test still asserts something real (previously it asserted two System.Text.Json call sites agreed with each other, which is weaker).

This migration is intentionally scoped to `Core.Configs.IO` only, per explicit user direction — `Game.Evals`, `Game.Benchmarks`, `ECS.Viewer.Server`, `Game.Configs.Loader`, and `Game.WebClient`'s own JS-interop/locale-dictionary System.Text.Json usage are all private to their own standalone tools (none share a type with the Unity-loaded side), so there is no duplication to remove there, and migrating them would be unrelated risk for no benefit. (Blazor WebAssembly's own `IJSRuntime` JS-interop marshaling is additionally hard-wired to System.Text.Json internally and could not be migrated regardless.)

## Steps

### Agent Steps

- [ ] Add `src/Game.Systems/CharacterQuery.cs` with the extracted, now-`public`/`IReadOnlyWorld`-based `GetTargetCharacterByCountryAndRole`; remove the private copy from `CreateActionEffectSystem.cs` and update its one call site.
- [ ] Add `GetSuitableRelationCandidates`/`HasSuitableRelationTarget` to `src/Game.Systems/CountryRelations.cs`.
- [ ] Add `ExpressionContext.HasSuitableRelationTarget` and the `"hasSuitableRelationTarget"` case to `src/Game.Configs/ExpressionNode.cs`.
- [ ] Wire `ctx.Opinion`/`ctx.HasSuitableRelationTarget` at all four sites: `ActionPlayability.cs::Evaluate`, `DrawCardSystem.cs::DrawCountryCards`, `InitSystem.cs::CreateCountryActionEntities`, `VisualStateConverter.cs::BuildEntry` (including `BuildEntry`'s new `orgId`/`countryId` parameters and both call-site updates).
- [ ] Add `SetCountryRelationEffectParams` to `src/Game.Configs/EffectConfig.cs` plus its `ActionEffectDefinitionListConverter` case.
- [ ] Migrate `Core.Configs.IO` off System.Text.Json onto Newtonsoft (§14): update `FileConfig.cs`/`StringConfig.cs` to use `JsonConvert.DeserializeObject`; delete `ActionEffectDefinitionListConverter.cs` and `ConfigJsonOptions.cs`; update `Core.Configs.IO.csproj`'s package reference from `System.Text.Json` to `Newtonsoft.Json` 13.0.3.
- [ ] Add `make_friend_effect`/`make_rival_effect` to `Assets/Configs/effect_config.json`.
- [ ] Add `make_friend`/`make_rival` to `Assets/Configs/action_config.json`.
- [ ] Add `SetCountryRelationEffect` struct to `src/Game.Components/CountryRelation.cs`; add the `CreateActionEffectSystem` dispatch branch.
- [ ] Add `src/Game.Systems/SetCountryRelationSystem.cs`; wire `SetCountryRelationSystem.Update(_world, _proximityEntity, _rng);` into `GameLogic.Update` right after the existing `DiscoverCountrySystem.Update` call.
- [ ] Add `RelationSetApplied` to `src/Game.Components/GameLogEffects.cs`; emit it from `SetCountryRelationSystem` after each successful `SetRelation` call.
- [ ] Apply the `BuildEntry`/`CountryActionsView` reason-code precision fix (§11): new `"insufficient_opinion"`/`"no_suitable_target"` reason codes, generalized threshold-extraction helper, matching locale keys.
- [ ] Add the 6 new locale keys to `Assets/Localization/en.asset` and `ru.asset`.
- [ ] Generate card art for `make_friend`/`make_rival` (256x384, matching existing style) via the image-generation workflow; write matching `.meta` sprite-slice files; add both entries to `Assets/Configs/ActionVisualConfig.asset`.
- [ ] Delete the two dead CSS blocks (`Assets/UI/HUD/CountryInfo/CountryInfo.uss` lines 72-86) and drop "cooldowns," from `README.md` line 17.
- [ ] Add/extend tests per the Tests section below.
- [ ] Run `dotnet test src/GlobalStrategy.Core.sln`, then `dotnet build src/GlobalStrategy.Core.sln -c Release` to refresh the Unity-consumed assemblies under `Assets/Plugins/Core/`.

### User Steps

Since this feature reuses the existing generic country-card UI path with no new UXML/asmdef/scene wiring, and no new Inspector-serialized field is introduced anywhere (`ActionVisualConfig.asset` is a data-only asset edited directly, not a new field to wire up), this section is verification-only:

### 1. Verify the two new cards render and behave correctly in Play mode

After the Core DLL rebuild and Unity domain reload finishes with no console errors: enter Play mode, select a country whose diplomacy advisor's opinion is below 30, open its Actions panel, and confirm "Make Friend"/"Make Rival" either don't appear in hand or show as unplayable with "Requires 30 opinion with the diplomacy advisor" (not "Requires 0 control"). Use debug tooling (existing opinion-raising debug action, or the debug relation menu to temporarily clear the country's other relations) to push opinion to ≥30 with at least one suitable candidate available, and confirm a copy becomes playable. Play it, confirm 50 gold is deducted, the card leaves the hand, a new card is drawn into the vacated slot, and the selected country's relation list (via the existing "Selected country" debug menu from `Docs/Specs/26_07_23_06_country-relations/plan.md`) shows a new Friend/Rival entry for some other country. Repeat a few times to sanity-check the pick isn't always the same candidate (rough 50/50 proximity/uniform behavior, not exactly verifiable by eye but should visibly vary). Finally, confirm no card anywhere in the game shows a cooldown label or a "next available in..." state.

## Tests

- `src/Game.Tests/ExpressionNodeTests.cs`: add cases for the new `"hasSuitableRelationTarget"` node (returns `ctx.HasSuitableRelationTarget`), mirroring the existing `control_node_returns_context_control` test.
- `src/Game.Tests/ActionPlayabilityTests.cs`: extend `BuildActionConfig`-style setup with an opinion-gated country card; add cases for opinion below/at/above 30 (using a seeded `Resource{ResourceId="opinion_{orgId}"}` owned by a `Character` entity with `RoleId="diplomacy_advisor"`), and for `hasSuitableRelationTarget` present/absent (seed `CountryRelation` entities to exhaust all candidates, then add one back). Assert `ActionPlayability.Evaluate` results match a `CheckActionConditionSystem`-driven pipeline run, same pattern as `evaluate_verdict_matches_pipeline_action_valid_outcome`.
- Extend `src/Game.Tests/StringConfigParityTests.cs`: add an `effect_config_parity`-style case that loads the real `Assets/Configs/effect_config.json` via `FileConfig<EffectConfig>` and asserts `Find("make_friend_effect")`/`Find("make_rival_effect")` deserialize as `SetCountryRelationEffectParams` with the correct `Kind` — this is what actually exercises the post-migration Newtonsoft path `Game.ConsoleRunner`/`Game.WebClient` use, closing the exact gap plan review found (the old System.Text.Json converter's own bugs would have passed silently since `StringConfigParityTests` only compared `FileConfig` against `StringConfig`, which shared the same converter).
- After the `Core.Configs.IO` migration (§14): run the existing `StringConfigParityTests` suite in full and confirm every config type still parses identically through `FileConfig<T>`/`StringConfig<T>`; spot-check that `Game.ConsoleRunner`/`Game.Evals`/`Game.Benchmarks` (which reference `Core.Configs.IO` transitively) still build and their own existing tests still pass, since removing `ConfigJsonOptions`/the deleted converter must not be referenced anywhere else.
- New `src/Game.Tests/SetCountryRelationSystemTests.cs` (or extend `CountryRelationsTests.cs`): cover — playing `make_friend`/`make_rival` end-to-end through `GameLogic` (via `UnifiedPipelineTests`'s `BuildLogic`/`StaticConfig` pattern) always resolves a relation with no roll involved; the resolved relation is `Friend`/`Rival` matching the played card; the pick excludes the source country itself and any country already friend/rival with it; with a seeded `Random`, force both the proximity branch (`rng.NextDouble() < 0.5` first call) and the uniform branch deterministically and assert each picks from the correct pool (proximity branch weighted via a small `ProximityMapData.Distances` fixture, matching `DiscoveryPerOrgTests.each_org_anchors_to_its_own_hq_country`'s style); `RelationSetApplied` is emitted with the correct `OrgId`/`CountryId`/`TargetCountryId`/`Kind`; the `SetCountryRelationEffect` marker is destroyed after processing; a no-candidate scenario is a safe no-op (marker destroyed, no relation created, no game-log event).
- Extend `src/Game.Tests/DiscoveryPerOrgTests.cs`-adjacent coverage or add to the new test file: draw-eligibility (`DrawCardSystem.DrawCountryCards`) respects both new gates — below-opinion and no-suitable-candidate scenarios keep the card out of hand; both-satisfied lets it draw.
- Extend `src/Game.Tests/InitSystemTests.cs`: initial-hand population never includes `make_friend`/`make_rival` at game start (opinion always starts at 0), and does not throw/misbehave now that `ExpressionContext.Opinion`/`.HasSuitableRelationTarget` are wired into `CreateCountryActionEntities`.
- Extend `src/Game.Tests/VisualStateConverterSelectedProvinceTests.cs`-adjacent coverage (or add a focused test) for `VisualStateConverter.BuildEntry`'s new reason codes: an opinion-gated card below threshold reports `"insufficient_opinion"`; a candidate-gated card with no suitable target reports `"no_suitable_target"`; existing control-gated cards (`letter_of_commendation_*`/`royal_audience`) still report `"insufficient_control"` unchanged (non-interference check).
- Non-interference check: an existing test (or a new small one) confirms `Control` still resolves correctly for cards whose conditions don't reference `Opinion`/`HasSuitableRelationTarget` at all (e.g. `letter_of_commendation_*` playability unaffected by the new wiring in `ActionPlayability`/`DrawCardSystem`/`InitSystem`/`VisualStateConverter`).
- Run `dotnet test src/GlobalStrategy.Core.sln`, then `dotnet build src/GlobalStrategy.Core.sln -c Release` so all dependent tests pass and the tracked Unity-consumed DLLs are refreshed.

## Constitution Check

- **Rendering (URP only):** Not touched by this feature — no shader/material/camera changes.
- **ECS for all game logic:** All new game-state logic (`SetCountryRelationEffect`, `SetCountryRelationSystem`, the DSL node, the opinion/candidate wiring) lives in `src/Game.Components`/`src/Game.Configs`/`src/Game.Systems`/`src/Game.Main`. `VisualStateConverter`/`CountryActionsView` only read and project already-decided ECS state (plus the narrowly-scoped reason-code classification in §11, which reads `def.Conditions` structure, not game state, to pick a locale key — no new game rule is decided in the UI layer).
- **VContainer sole DI mechanism:** No new service, no `new` for a singleton, no `FindObjectOfType`. `SetCountryRelationSystem` is a static class called only from `GameLogic.Update`, the existing top-level orchestrator — consistent with `DiscoverCountrySystem`'s registration-free wiring.
- **UI Toolkit only:** No Canvas/UGUI. The one UI code change (§11) is a plain C# reason-code branch inside the existing `CountryActionsView`/`VisualStateConverter` pair; no new UXML/USS/view class.
- **Planning discipline (plan before implement):** This plan follows the approved `spec.md` in this same `Docs/Specs/26_07_24_10_country-relation-cards/` folder.
- **Specification discipline (spec before plan for feature work):** Satisfied — `spec.md` already exists and was approved before this plan.
- **File organisation (`Docs/Specs/<timestamp>_<name>/`):** This plan is `Docs/Specs/26_07_24_10_country-relation-cards/plan.md`, alongside its `spec.md`, per convention.
- **One `.asmdef` per feature folder:** No new Unity feature folder or `.asmdef` is introduced — all new `src/` files land in existing projects (`Game.Components`, `Game.Configs`, `Game.Systems`, `Game.Main`), and the one Unity-side edit (`VisualStateConverter.cs`'s already-Unity-side counterpart is actually `src/Game.Main`, not `Assets/Scripts/Unity/`) plus `CountryActionsView.cs`/`ActionCardBuilder.cs` edits land in the existing `GS.Unity.UI` folder/asmdef.
- **C# code style:** Tabs, `_`-prefixed private fields, braces always, no redundant access modifiers — followed throughout every new/edited file described above; no deviations identified.
- **`Core.Configs.IO` migration (§14) scope check:** This is a technical/infra change bundled into a feature plan at explicit user direction rather than requiring its own spec (`Docs/Constitution.md`'s "spec before plan for feature work" carve-out already permits purely technical tasks to skip straight to `/plan`; here it rides along with an already-approved feature spec instead). It touches no Unity asmdef, no MonoBehaviour, no DI registration — only two `Core.Configs.IO` files' internals and its `.csproj` package reference, consistent with every other principle above.

No constitution violations identified for this feature.

Use the implement skill to start working on the plan or request changes.
