# Plan: Ultimatum / Surrender Cards

## Spec

As a player, playing "Ultimatum" (300 gold) or "Surrender" (500 gold) on a country currently
locked in a war forces that war to a decisive, immediate, always-succeeding end — Ultimatum makes
the selected country the winner, Surrender makes it the loser — once the playing org holds enough
Control and military-advisor Opinion in that country and the country's own war progress is
trending the required way. See `spec.md` for full detail.

Key acceptance criteria:
- Not currently in a war => neither card is eligible to draw; an already-in-hand copy whose war
  ends by any means becomes unplayable (never removed/discarded).
- In a war, and `control >= 10`, `opinion(military_advisor) >= 50`, `own war progress >= 50` =>
  Ultimatum is drawable/playable at 300 gold; `control >= 20`, `opinion >= 80`, `own progress >= 0`
  => Surrender is drawable/playable at 500 gold. Either threshold dropping (or the war ending)
  while a copy is in hand makes it unplayable, not removed. Both cards can be simultaneously
  eligible and are fully independent.
- Playing either card always succeeds (no roll): the country's current war ends immediately with
  it as winner (Ultimatum) or loser (Surrender); the card leaves hand and a replacement is drawn as
  usual; no other consequence (no gold/control/relation change).
- Neither card ever shows a cooldown label.
- One Action Log entry appears reporting which country won and which lost.
- "Own" war progress is the selected country's signed reading of `WarProgress.Value`: the
  attacker's own progress is `Value` directly, the defender's is `-Value`.

## Approach

**Overall shape: this is the same "single static config row per card, no dynamic instance, inline
effect dispatch" pattern as `decrease_enemy_control`** (`Docs/Specs/26_07_29_00_decrease-enemy-control-card/plan.md`),
not the per-relation dynamic-instance pattern used by `stop_friendship`/`stop_rivalry`
(`RelationCardTarget`/`RelationCardSyncSystem`) — correct, since a country can be in at most one
war at a time (`Wars.IsInWar`'s existing guard), so a plain numeric-conditions-gated static row
always means "the war this country is currently in." Confirmed no dynamic instance, no marker
entity, no new system, no `GameLogic.cs` orchestration change is needed anywhere in this feature —
verified below, call-site by call-site.

**Everything the spec's Tech Notes names was re-verified against the current tree; two real gaps
were found that the Tech Notes did not call out precisely enough to implement directly, both
detailed below. Everything else in the Tech Notes matches current source exactly (line numbers
cited per-file below) and is implemented as written.**

### Already-satisfied precedent (no-op, confirmed against real source)

- `ActionPlayability.Evaluate` **already has** the `entity`-aware signature the spec's Tech Notes
  describes as a prerequisite (`src/Game.Systems/ActionPlayability.cs:8`:
  `Evaluate(IReadOnlyWorld world, ActionConfig config, int entity, string actionId, string orgId, string? countryId)`),
  landed by the `stop_friendship`/`stop_rivalry` feature. No signature change needed.
- `Game.Bots/BotObservation.cs:136` **already** calls `ActionPlayability.Evaluate(world, actionConfig, entity, actionId, orgId, countryId)`
  with a real `entity` (from `arch.Entities[i]` at line 118). The spec's "Bot AI mechanical update
  only" bullet is therefore **already fully satisfied, a true no-op** — there is no bot call site
  left to touch. (Existing `BotObservationTests.cs` call sites already pass an entity too; no test
  change needed there either.)
- `CharacterQuery.cs` already exists as a plain non-system helper
  (`src/Game.Systems/CharacterQuery.cs`, `GetTargetCharacterByCountryAndRole(IReadOnlyWorld world, string countryId, string targetRole)`) —
  confirms the no-system-to-system-call shape `Wars.cs`'s new `ResolveWar`/`GetOwnWarProgress`
  will also follow.
- `ControlQuery.cs`, `EnemyControlDrainEffectParams`, and the `ActionEffectDefinitionListConverter`
  switch registration pattern (`src/Game.Configs/EffectConfig.cs`) already exist from the
  `decrease_enemy_control` feature and are reused/mirrored directly, not re-derived.
- `GameSettings.AttackerWarProgressDecayPerMonth` (`src/Game.Configs/GameSettings.cs:14`) and
  `Assets/Configs/game_settings.json`'s `"attackerWarProgressDecayPerMonth"` already exist from
  `26_07_25_06_war-mechanics-core` — untouched by this feature.
- `Wars.IsInWar`/`Wars.DeclareWar`/`Wars.StopWar` (`src/Game.Systems/Wars.cs`) and `WarSystem.Update`
  (`src/Game.Systems/WarSystem.cs`) already exist and are already wired into `GameLogic.cs`
  (`WarSystem.Update` call, `ReadDebugDeclareWarCommand`/`ReadDebugStopWarCommand` loops) — this
  feature adds `Wars.ResolveWar`/`Wars.GetOwnWarProgress` as new methods in the same file, called
  only from `CreateActionEffectSystem.Update`, never from `GameLogic.cs` directly. **This feature
  requires zero changes to `GameLogic.cs`** — every system/method it touches is either already
  invoked every tick (`CreateActionEffectSystem.Update`, `CleanupEffectNotificationsSystem.UpdateActionEffects`,
  `VisualStateConverter`'s update pipeline) or is a plain helper called from inside one of those.

### Gap 1 (found during re-verification, not called out precisely enough by the spec) — role-aware Opinion must move *inside* the per-candidate loop at two of the four call sites, not just swap the literal string

The spec's Tech Notes describe the fix as "resolve the character via `def.TargetRole` instead of
the literal `"diplomacy_advisor"`" at each of four call sites. That is correct and sufficient for
two of them, but **silently wrong for the other two**, because those two currently compute
`Opinion` **once per `(orgId, countryId)` before iterating multiple different cards' `def`s**, not
per-card:

- `ActionPlayability.Evaluate` (`src/Game.Systems/ActionPlayability.cs:8-44`) evaluates exactly one
  card entity per call, so swapping `"diplomacy_advisor"` (line 20) for `def.TargetRole` is a
  correct, sufficient, one-line fix.
- `VisualStateConverter.BuildEntry` (`src/Game.Main/VisualStateConverter.cs:608-655`) also
  evaluates exactly one card entity per call (called once per hand/deck entity from
  `UpdateCountryActions`, lines 585/598), so the same one-line swap (line 615) is correct there too.
- **`DrawCardSystem.DrawCountryCards` (`src/Game.Systems/DrawCardSystem.cs:91-155`) computes a
  single `ctx.Opinion` once (lines 94-95) before looping over every candidate card entity in that
  country's deck** (lines 107-126, the same loop that already sets `ctx.RelationStillExists`
  per-candidate at lines 117-119). Once `ultimatum`/`surrender` (`TargetRole = "military_advisor"`)
  exist alongside `stop_friendship`/`make_friend` (`TargetRole = "diplomacy_advisor"`) in the same
  country's deck, a single pre-loop `Opinion` value computed from one hardcoded (or even one
  `def.TargetRole`-derived, since there is no single "the" `def` at that point) role is wrong for
  every other role's cards in the same pass. **Fix: move the character/opinion resolution inside
  the per-candidate loop**, resolving `def.TargetRole` per `def` (the loop already resolves `def`
  at line 114) exactly the same way `RelationStillExists` is already resolved per-candidate.
- **`InitSystem.CreateCountryActionEntities` (`src/Game.Main/InitSystem.cs:593-701`) has the
  identical bug**: `opinion` is computed once (lines 664-667) before the
  `foreach (var (e, actionId) in createdEntities)` loop (lines 670-687) that evaluates every
  country card's initial-hand eligibility for one org — again, one role cannot serve every card's
  `def.TargetRole` in that loop. **Fix: move the character/opinion resolution inside that loop**,
  per-`d.TargetRole` (the loop already resolves `d` at line 671).

Both fixes are behavior-preserving for every existing card (identical to today when only
`diplomacy_advisor`-targeted and untargeted cards exist in a deck — the only card mix this
regresses without the fix), and are the first real exercise of a country deck holding
simultaneously-eligible cards keyed to two different `TargetRole`s. No extra caching is added (a
per-candidate character lookup is a plain linear scan, same as every other query in these files) —
consistent with the codebase's existing non-memoized query style.

### Gap 2 (found during re-verification, a real, currently-invisible bug in the prior feature this one must not repeat) — the Action Log must be wired end-to-end for `WarResolvedApplied`, not just the component + cleanup

The spec's Tech Notes describe `WarResolvedApplied` by direct analogy to `RelationClearedApplied`
("following the same 'separate sibling entity, not attached to the effect component' convention
`DiscoverCountrySystem`/`SetCountryRelationSystem`/`ClearCountryRelationSystem` already use... New
`GameLogLineFormatter` branch + locale key"). **Verified against the real current tree:
`RelationClearedApplied` (`src/Game.Components/RelationClearedApplied.cs`, emitted by
`ClearCountryRelationSystem.cs`) is only ever asserted at the raw-ECS-component level
(`src/Game.Tests/ClearCountryRelationSystemTests.cs`) and swept by
`CleanupEffectNotificationsSystem.UpdateActionEffects` (`src/Game.Systems/CleanupEffectNotificationsSystem.cs:22`)
— it is never read anywhere in `VisualStateConverter.UpdateGameLog`
(`src/Game.Main/VisualStateConverter.cs:822-921`), there is no `GameLogEntryKind` value for it, no
`GameLogLineFormatter` method exists for it, and `ActionLogView`'s `Kind switch`
(`Assets/Scripts/Unity/UI/ActionLogView.cs:74-80`) has no case for it.** Playing `stop_friendship`/
`stop_rivalry` today produces zero Action Log line for the relation being cleared — a real,
pre-existing, silent gap in that feature, not something this plan needs to fix (out of scope: this
plan only touches war-resolution), but this feature's own Acceptance Criteria explicitly require
"One entry appears reporting the war's outcome" in the Action Log, so **this plan must not follow
`RelationClearedApplied`'s incomplete precedent** — it wires the full chain:

1. `src/Game.Components/WarResolvedApplied.cs` (new, not `[Savable]`, same-tick notification like
   `RelationClearedApplied`): `{ string WarId; string WinnerCountryId; string LoserCountryId; }`.
2. `src/Main/VisualState.cs:459-465`'s `GameLogEntryKind` enum: add `WarResolved`.
3. **No new fields needed on `GameLogEntry`** (`src/Game.Main/VisualState.cs:467-497`) — it already
   carries `CountryId` and `TargetCountryId` (used today for `Relation`'s
   country/target-country pair), which are reused as winner/loser respectively; `StateEquality.GameLogEntryEquals`
   (`src/Game.Main/StateEquality.cs:112-125`) already compares both generically, so no equality-code
   change is needed either.
4. `VisualStateConverter.UpdateGameLog` (`src/Game.Main/VisualStateConverter.cs:822-921`): add a new
   loop over `WarResolvedApplied` (mirroring the `RelationSetApplied` loop at lines 886-895) that
   adds `new GameLogEntry(0, GameLogEntryKind.WarResolved, "", applied[i].WinnerCountryId, "", "",
   Array.Empty<string>(), 0, 0, false, applied[i].LoserCountryId, default)`. **Deliberate
   consequence, flagged for visibility, matching the spec's own literal `WarResolvedApplied` shape
   (`{ WarId, WinnerCountryId, LoserCountryId }`, no `OrgId`):** unlike `Control`/`Opinion`/`Relation`/`Discovery`,
   this entry carries no acting-org id, so it is never filtered by `_gameLogIncludePlayerActions`
   (`VisualStateConverter.cs:22,831,867,880,891,903`) — it always shows regardless of who played the
   card. This is accepted as-is (no `OrgId` was asked for and none is a natural fit — the "acting
   org" concept is less central to a war-outcome announcement than to a Control/Opinion delta), but
   is called out here since it is a real behavioral difference from every sibling entry kind.
5. `GameLogLineFormatter.BuildWarResolvedLine` (`Assets/Scripts/Unity/UI/GameLogLineFormatter.cs`,
   new method, same shape as `BuildRelationLine`): wraps `entry.CountryId` (winner) and
   `entry.TargetCountryId` (loser) via `country_name.*` + `WrapColored`, and formats via
   `string.Format(loc.Get("game_log.war_resolved_format"), winnerName, loserName)`.
6. `ActionLogView`'s `Kind switch` (`Assets/Scripts/Unity/UI/ActionLogView.cs:74-80`): add
   `GameLogEntryKind.WarResolved => GameLogLineFormatter.BuildWarResolvedLine(entry, _loc, _countryVisualConfig, _orgVisualConfig),`.
7. `CleanupEffectNotificationsSystem.UpdateActionEffects` (`src/Game.Systems/CleanupEffectNotificationsSystem.cs:17-23`):
   add `RemoveComponent<WarResolvedApplied>(world);` alongside the existing five.

### `Wars.cs` additions — `ResolveWar` and `GetOwnWarProgress`, sharing extracted private helpers with the existing `StopWar`

`src/Game.Systems/Wars.cs` (104 lines, re-read in full) currently has three public methods
(`IsInWar`, `DeclareWar`, `StopWar`) with no private helpers — `StopWar` (lines 54-101) inlines its
own two-pass scan (find `warId` for the country, then collect+destroy every `WarParticipant`/`War`
entity sharing that `warId`). Per the spec's own suggestion, this plan extracts the shared shape so
`ResolveWar` does not duplicate it a third time:

- `static bool TryGetWarId(IReadOnlyWorld world, string countryId, out string warId, out WarParticipantKind kind)` —
  the scan `StopWar`'s first loop (lines 55-66) already does, widened to also return the
  participant's `Kind` (needed by `GetOwnWarProgress`).
- `static void DestroyWarAndParticipants(World world, string warId)` — `StopWar`'s existing
  collect-then-destroy body (lines 71-98), extracted verbatim.
- `static string? FindOpponentCountryId(IReadOnlyWorld world, string warId, string countryId)` — new,
  small scan over `WarParticipant` for the same `warId` with a different `CountryId`.
- `static double FindWarProgressValue(IReadOnlyWorld world, string warId)` — new, scans the `War`
  archetype (which already carries `WarProgress` composed on the same entity, per
  `26_07_25_06_war-mechanics-core`'s Tech Notes) for the matching `WarId`, returns `0` if not found
  (defensive; unreachable in practice since `TryGetWarId` already found a live war for this
  country).

```csharp
public static bool StopWar(World world, string countryId) {
    if (!TryGetWarId(world, countryId, out string warId, out _)) { return false; }
    DestroyWarAndParticipants(world, warId);
    return true;
}

public static bool ResolveWar(World world, string countryId, WarOutcome outcomeForCountry) {
    if (!TryGetWarId(world, countryId, out string warId, out _)) { return false; }
    string? opponentCountryId = FindOpponentCountryId(world, warId, countryId);
    if (opponentCountryId == null) { return false; } // defensive — invariant (exactly 2 participants) guarantees this is found
    string winnerCountryId = outcomeForCountry == WarOutcome.Win ? countryId : opponentCountryId;
    string loserCountryId  = outcomeForCountry == WarOutcome.Win ? opponentCountryId : countryId;
    DestroyWarAndParticipants(world, warId);
    int ge = world.Create();
    world.Add(ge, new WarResolvedApplied { WarId = warId, WinnerCountryId = winnerCountryId, LoserCountryId = loserCountryId });
    return true;
}

public static double GetOwnWarProgress(IReadOnlyWorld world, string countryId) {
    if (!TryGetWarId(world, countryId, out string warId, out WarParticipantKind kind)) { return 0; }
    double rawValue = FindWarProgressValue(world, warId);
    return kind == WarParticipantKind.Attacker ? rawValue : -rawValue;
}
```

`ResolveWar` reads the opponent and computes winner/loser *before* calling
`DestroyWarAndParticipants`, since the participant entities (which carry `CountryId`) are destroyed
by that call. Matches the spec's exact `WarOutcome`-relative-to-`countryId` semantics.

### New DSL nodes — `isInWar`/`warProgress`, populated at all four condition-evaluation call sites

`src/Game.Configs/ExpressionNode.cs`'s `ExpressionContext` (lines 5-11) already holds
`Control`/`TotalCountryControl`/`Opinion`/`HasSuitableRelationTarget`/`RelationStillExists`; add
`IsInWar`/`WarProgress` (both `double`) alongside them, and `case "isInWar": return ctx.IsInWar;` /
`case "warProgress": return ctx.WarProgress;` in `Evaluate`'s switch (alongside the existing
`"relationStillExists"` case at lines 65-67). Populate both at all four call sites — once per
`(orgId, countryId)` (not per-candidate; unlike `Opinion`, these two do not vary by `def.TargetRole`):
`ctx.IsInWar = Wars.IsInWar(world, countryId) ? 1.0 : 0.0; ctx.WarProgress = Wars.GetOwnWarProgress(world, countryId);`
(`GetOwnWarProgress` already returns `0` when not in a war, matching the spec's stated default).

### Unplayable-reason wiring

`VisualStateConverter.BuildEntry`'s `failedReason` switch (`src/Game.Main/VisualStateConverter.cs:637-645`)
already switches on `cond.Members[0].Type` directly for `opinion`/`hasSuitableRelationTarget`/`relationStillExists`
(no nested-tree search needed, unlike `totalCountryControl`'s `ContainsExpressionType` special-case,
which exists only because that node is nested inside a `sub(...)` — `isInWar` appears as a plain
top-level `gte(isInWar, ...)` member, so it needs no such special-casing). Add
`"isInWar" => "not_at_war",` as a new arm. Per the spec, `control`/`opinion`/`warProgress` threshold
failures deliberately fall through to the existing generic `_ => "insufficient_control"` bucket —
**verified consequence, not a bug**: since `ultimatum`/`surrender`'s `Conditions` list also always
contains a `gte(control, ...)` entry, `CountryActionsView.cs`'s existing
`ExtractConditionThreshold(def, "control")` fallback (used by the `_` case) will always find and
display *that* card's control threshold even when the actual failing condition was `warProgress` —
an accepted, spec-endorsed imprecision (the spec's own words: "exact wiring is a plan-time detail"),
not something this plan improves further. `CountryActionsView.cs`'s reason-text switch
(`Assets/Scripts/Unity/UI/CountryActionsView.cs:74-85`): add
`"not_at_war" => _loc.Get("action.country.unplayable.not_at_war"),`.

### Config rows and placeholder art

`action_config.json`/`effect_config.json` new rows follow the spec's Tech Notes exactly (re-verified
against `decrease_enemy_control`'s real, current JSON shape — `Assets/Configs/action_config.json:192-216`,
`Assets/Configs/effect_config.json:78-89` — both match). `ActionVisualConfig.asset` reuses
`letter_of_commendation_military_advisor`'s existing `frontImage` guid/fileID
(`-4234567890123456789` / `d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9`, confirmed present at
`Assets/Configs/ActionVisualConfig.asset:29-30`) as placeholder art for both new cards — the only
existing entry with a military/advisor-flavored image, same reuse-an-existing-guid convention the
`decrease_enemy_control`/`stop_friendship` entries already established.

## Steps

### Agent Steps

- [ ] **Add `WarOutcome` enum** — new `src/Game.Common/WarOutcome.cs`: `namespace GS.Game.Common { public enum WarOutcome { Win, Lose } }`, sibling to `WarParticipantKind.cs`.

- [ ] **Add `WarResolvedApplied` component** — new `src/Game.Components/WarResolvedApplied.cs`, not `[Savable]` (same-tick Game Log notification, comment the omission like `RelationClearedApplied.cs`): `public struct WarResolvedApplied { public string WarId; public string WinnerCountryId; public string LoserCountryId; }`.

- [ ] **Extend `Wars.cs`** (`src/Game.Systems/Wars.cs`) — extract `TryGetWarId`/`DestroyWarAndParticipants` from `StopWar`'s existing body (behavior-preserving refactor — existing `WarsTests.cs` cases for `StopWar` must keep passing unchanged), add `FindOpponentCountryId`, `FindWarProgressValue`, and the two new public methods `ResolveWar(World world, string countryId, WarOutcome outcomeForCountry) : bool` and `GetOwnWarProgress(IReadOnlyWorld world, string countryId) : double`, exactly per the Approach section's code shape above.

- [ ] **Add `isInWar`/`warProgress` DSL nodes** — In `src/Game.Configs/ExpressionNode.cs`: add `IsInWar`/`WarProgress` (`double`) to `ExpressionContext`, and `case "isInWar": return ctx.IsInWar;` / `case "warProgress": return ctx.WarProgress;` to `Evaluate`.

- [ ] **Make Opinion resolution role-aware at all four condition-evaluation call sites, fixing the per-candidate-loop gap at two of them, and populate `IsInWar`/`WarProgress` at all four**:
  - `src/Game.Systems/ActionPlayability.cs::Evaluate` (line 20): replace the hardcoded `"diplomacy_advisor"` with `def.TargetRole`. Add `ctx.IsInWar`/`ctx.WarProgress` (once per call, alongside the existing `Control`/`Opinion` block).
  - `src/Game.Main/VisualStateConverter.cs::BuildEntry` (line 615): same one-line swap to `def.TargetRole`; add `IsInWar`/`WarProgress` via `Wars.IsInWar`/`Wars.GetOwnWarProgress`.
  - `src/Game.Systems/DrawCardSystem.cs::DrawCountryCards` (lines 91-155): move the character/opinion resolution (currently lines 94-95, hardcoded) from before the per-candidate loop to inside it (alongside the existing per-candidate `ctx.RelationStillExists` assignment at lines 117-119), keyed by each candidate's own `def.TargetRole`. Add `ctx.IsInWar`/`ctx.WarProgress` once before the loop (country-level, not per-candidate).
  - `src/Game.Main/InitSystem.cs::CreateCountryActionEntities` (lines 593-701): move the character/opinion resolution (currently lines 664-667, hardcoded, before the `foreach (var (e, actionId) in createdEntities)` loop) to inside that loop, keyed by each `d.TargetRole`. Add `IsInWar`/`WarProgress` once before the loop.

- [ ] **New effect type: `ResolveWarEffectParams`** — In `src/Game.Configs/EffectConfig.cs`: add `public class ResolveWarEffectParams : ActionEffectDefinition { public WarOutcome Outcome { get; set; } }` (needs `using GS.Game.Common;`, already present in this file), and register `case "ResolveWar": item = obj.ToObject<ResolveWarEffectParams>(serializer)!; break;` in `ActionEffectDefinitionListConverter`'s switch, alongside the existing six cases.

- [ ] **Dispatch the effect in `CreateActionEffectSystem`** — In `src/Game.Systems/CreateActionEffectSystem.cs`'s `foreach (var effectId in def.EffectIds)` dispatch (after the `EnemyControlDrainEffectParams` branch, currently ending at line 133): add `else if (effectDef is ResolveWarEffectParams resolveWarParams && !string.IsNullOrEmpty(countryId)) { Wars.ResolveWar(world, countryId, resolveWarParams.Outcome); }`.

- [ ] **Unplayable-reason plumbing** — `VisualStateConverter.BuildEntry`'s `failedReason` switch (lines 640-645): add `"isInWar" => "not_at_war",`. `CountryActionsView.cs`'s reason-text switch (lines 74-85): add `"not_at_war" => _loc.Get("action.country.unplayable.not_at_war"),`.

- [ ] **Wire `WarResolvedApplied` end-to-end into the Action Log** (see Approach Gap 2 — do not stop at the component):
  - `GameLogEntryKind` (`src/Game.Main/VisualState.cs:459-465`): add `WarResolved`.
  - `VisualStateConverter.UpdateGameLog` (`src/Game.Main/VisualStateConverter.cs:822-921`): add a loop over `WarResolvedApplied`, appending `new GameLogEntry(0, GameLogEntryKind.WarResolved, "", applied[i].WinnerCountryId, "", "", Array.Empty<string>(), 0, 0, false, applied[i].LoserCountryId, default)` per entity — no `_gameLogIncludePlayerActions` filter (no `OrgId` on this event, per spec).
  - `GameLogLineFormatter.BuildWarResolvedLine` (`Assets/Scripts/Unity/UI/GameLogLineFormatter.cs`, new method): `string.Format(loc.Get("game_log.war_resolved_format"), winnerName, loserName)` with both names `WrapColored` via `country_name.*` + `CountryVisualConfig`.
  - `ActionLogView`'s `Kind switch` (`Assets/Scripts/Unity/UI/ActionLogView.cs:74-80`): add the `WarResolved` case.
  - `CleanupEffectNotificationsSystem.UpdateActionEffects` (`src/Game.Systems/CleanupEffectNotificationsSystem.cs:17-23`): add `RemoveComponent<WarResolvedApplied>(world);`.

- [ ] **`Assets/Configs/action_config.json`: two new action rows** — append `ultimatum` and `surrender`, both `ownerType: "country"`, `rarity: "Standard"`, `targetRole: "military_advisor"`, `deckCopies: 3`, no roll field (none exists on `ActionDefinition`):
  ```json
  {
    "actionId": "ultimatum",
    "ownerType": "country",
    "rarity": "Standard",
    "nameKey": "action.ultimatum.name",
    "descKey": "action.ultimatum.desc",
    "targetRole": "military_advisor",
    "deckCopies": 3,
    "conditions": [
      { "type": "gte", "members": [ { "type": "control" }, { "type": "value", "value": 10 } ] },
      { "type": "gte", "members": [ { "type": "opinion" }, { "type": "value", "value": 50 } ] },
      { "type": "gte", "members": [ { "type": "isInWar" }, { "type": "value", "value": 1 } ] },
      { "type": "gte", "members": [ { "type": "warProgress" }, { "type": "value", "value": 50 } ] }
    ],
    "cost": [{ "resourceId": "gold", "amount": 300.0 }],
    "effectIds": ["ultimatum_effect"]
  },
  {
    "actionId": "surrender",
    "ownerType": "country",
    "rarity": "Standard",
    "nameKey": "action.surrender.name",
    "descKey": "action.surrender.desc",
    "targetRole": "military_advisor",
    "deckCopies": 3,
    "conditions": [
      { "type": "gte", "members": [ { "type": "control" }, { "type": "value", "value": 20 } ] },
      { "type": "gte", "members": [ { "type": "opinion" }, { "type": "value", "value": 80 } ] },
      { "type": "gte", "members": [ { "type": "isInWar" }, { "type": "value", "value": 1 } ] },
      { "type": "gte", "members": [ { "type": "warProgress" }, { "type": "value", "value": 0 } ] }
    ],
    "cost": [{ "resourceId": "gold", "amount": 500.0 }],
    "effectIds": ["surrender_effect"]
  }
  ```

- [ ] **`Assets/Configs/effect_config.json`: two new effect rows** — append:
  ```json
  {
    "effectId": "ultimatum_effect",
    "effectType": "ResolveWar",
    "nameKey": "effect.ultimatum_effect.name",
    "descKey": "effect.ultimatum_effect.desc",
    "outcome": "Win"
  },
  {
    "effectId": "surrender_effect",
    "effectType": "ResolveWar",
    "nameKey": "effect.surrender_effect.name",
    "descKey": "effect.surrender_effect.desc",
    "outcome": "Lose"
  }
  ```

- [ ] **New `ActionVisualConfig` entries** — `Assets/Configs/ActionVisualConfig.asset` (plain YAML, direct edit): append, reusing `letter_of_commendation_military_advisor`'s `frontImage` guid/fileID as placeholder art:
  ```yaml
  - actionId: ultimatum
    frontImage: {fileID: -4234567890123456789, guid: d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9, type: 3}
    backImage: {fileID: 0}
  - actionId: surrender
    frontImage: {fileID: -4234567890123456789, guid: d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9, type: 3}
    backImage: {fileID: 0}
  ```

- [ ] **Locale keys** — Add to both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` (use the `localization` skill for real Russian translations, per `.claude/rules/unity/localization.md`; keep `.desc` short/practical per the Card/Action Description Text rule, not flavor text):
  - `action.ultimatum.name` → `"Ultimatum"`
  - `action.ultimatum.desc` → `"Force this country to win its current war."`
  - `action.surrender.name` → `"Surrender"`
  - `action.surrender.desc` → `"Force this country to lose its current war."`
  - `effect.ultimatum_effect.name` / `.desc` — short name/desc for the win-resolution effect.
  - `effect.surrender_effect.name` / `.desc` — short name/desc for the loss-resolution effect.
  - `game_log.war_resolved_format` → `"{0} won the war against {1}"`
  - `action.country.unplayable.not_at_war` → `"This country is not currently at war"`
  - Russian equivalents (real translations) for all eight.

### User Steps

### 1. None

None — every change in this plan is a code, JSON config, or asset-YAML/locale-asset file edit
(including the `ActionVisualConfig.asset` placeholder-art entries, which are plain YAML edits, not
Inspector operations), exactly like `26_07_29_00_decrease-enemy-control-card`'s plan found for its
own single-config-row card. No Unity Editor scene/asset work, visual inspection, or other hands-on
Unity step is required. Optional, non-blocking: confirm in the Editor that the reused
`letter_of_commendation_military_advisor` placeholder art renders correctly on both new cards once
implemented, and that the new Action Log line reads correctly (no literal `{0}`/`{1}` left
un-interpolated).

## Tests

- **`src/Game.Tests/WarsTests.cs`** (extend): after the `TryGetWarId`/`DestroyWarAndParticipants`
  refactor, all existing `StopWar`-related cases must keep passing unchanged (behavior-preserving
  extraction — add an explicit note/assert if useful, but no new case is needed purely for the
  refactor). Add:
  - `resolve_war_with_win_outcome_makes_named_country_the_winner_and_hard_deletes_the_war` — declare
    a war, call `ResolveWar(world, countryId, WarOutcome.Win)`, assert it returns `true`, the
    `War`/`WarProgress`/both `WarParticipant` entities are gone (`CountEntities<T>` all `0`),
    `Wars.IsInWar` is `false` for both sides, and a `WarResolvedApplied` entity exists with
    `WinnerCountryId == countryId` and `LoserCountryId == opponentId`.
  - `resolve_war_with_lose_outcome_makes_named_country_the_loser` — same, `WarOutcome.Lose`,
    assert `LoserCountryId == countryId`.
  - `resolve_war_on_country_not_in_any_war_is_a_no_op_returning_false` — mirrors
    `stop_war_on_country_not_in_any_war_is_a_no_op`; assert no `WarResolvedApplied` is created.
  - `get_own_war_progress_returns_value_directly_for_the_attacker` — declare a war, mutate
    `WarProgress.Value` directly (e.g. `-30`), assert `GetOwnWarProgress(world, attackerId) == -30`.
  - `get_own_war_progress_returns_negated_value_for_the_defender` — same war/value, assert
    `GetOwnWarProgress(world, defenderId) == 30`.
  - `get_own_war_progress_returns_zero_when_not_in_any_war`.

- **`src/Game.Tests/ExpressionNodeTests.cs`**: extend the `Ctx(...)` helper (currently
  `control`/`totalCountryControl`/`opinion`/`hasSuitableRelationTarget`/`relationStillExists`) with
  `isInWar`/`warProgress` parameters, mirroring the sibling `decrease_enemy_control` plan's own
  `totalCountryControl` addition; add round-trip cases for both new node types plus `gte`
  composition (e.g. `gte(warProgress, 50)` true/false at the boundary).

- **`src/Game.Tests/ActionPlayabilityTests.cs`**: add `ultimatum`/`surrender`-shaped
  `ActionDefinition`s (`TargetRole = "military_advisor"`, the four-condition list, 300/500 gold) and
  cases:
  - unplayable when not in any war (`isInWar` gate).
  - playable once in a war with `control`/`opinion`/`warProgress` all meeting Ultimatum's
    thresholds and gold affordable; unplayable if any one of the three numeric thresholds is one
    below its gate.
  - a defender-perspective case demonstrating the signed-progress convention: seed a war where the
    tested country is the **defender** with `WarProgress.Value` at a level whose *negation* clears
    Ultimatum's `>= 50` gate (e.g. `Value = -60`) — playable; the same raw `Value` with the tested
    country as **attacker** — not playable (own progress is `-60`, well under `50`), directly
    exercising the spec's documented "Ultimatum realistically defender-only today" consequence.
  - both `ultimatum` and `surrender` simultaneously playable for the same seeded country/thresholds
    (control 25, opinion 90, progress 60), confirming independence.
  - extend an existing helper (`AddDiplomacyAdvisor`, `src/Game.Tests/ActionPlayabilityTests.cs:107-118`)
    to accept a `roleId` parameter (or add a sibling `AddMilitaryAdvisor`) so opinion can be seeded
    against the military advisor specifically.
  - a mixed-role regression case seeding **both** a `diplomacy_advisor`-gated card and an `ultimatum`
    instance for the same country with two *different* opinion values on the two advisors, asserting
    each card's own-role opinion gate is evaluated independently (this is the direct regression
    guard for the Approach's Gap 1 fix, at the `ActionPlayability.Evaluate` call site specifically —
    this call site was already correct even before the fix, since it evaluates one card at a time,
    but the test still documents the expected independent-role behavior this feature introduces).

- **`src/Game.Tests/DrawCardSystemTests.cs`**: this is the call site Gap 1's fix is actually
  necessary for. Add a case seeding one `stop_friendship`-shaped deck candidate (or any
  `diplomacy_advisor`-gated card) and one `ultimatum`-shaped deck candidate in the *same* country
  deck, with the diplomacy advisor's opinion high enough for the first card but the military
  advisor's opinion too low for `ultimatum` (or vice versa) — assert only the card whose own role's
  opinion gate is actually satisfied is drawn, proving `DrawCountryCards` resolves `Opinion`
  per-candidate-role rather than once for the whole deck pass. Add straightforward
  drawable/not-drawable cases for `ultimatum` alone gated on `isInWar`/`control`/`opinion`/`warProgress`.

- **`src/Game.Tests/InitSystemTests.cs`**: same mixed-role regression as above, but for
  `CreateCountryActionEntities`'s initial-hand-fill pass — seed two participating orgs, a country
  with both a diplomacy advisor and a military advisor character, and confirm each card's own-role
  gate is independently honored when populating the initial hand.

- **`src/Game.Tests/GameLogStateTests.cs`** (extend, following the existing `relation_produces_exactly_one_entry...`
  fixture pattern at lines 152-175): add an `ultimatum`/`surrender`-shaped `ActionConfig`/`EffectConfig`
  pair (`ResolveWarEffectParams { Outcome = WarOutcome.Win }` / `{ Outcome = WarOutcome.Lose }`).
  Seed a war via `Wars.DeclareWar` directly on `logic.World` before the first `logic.Update(0f)`,
  seed a military-advisor character + opinion resource satisfying the thresholds, put the card in
  hand via `PutCountryCardInHand` (`GameLogStateTests.cs:256-279`), then push
  `PlayCardActionCommand`. Assert:
  - exactly one `GameLogEntryKind.WarResolved` entry, `CountryId` (winner) and `TargetCountryId`
    (loser) matching the card's outcome.
  - `Wars.IsInWar` is `false` for both former participants afterward.
  - a passive tick afterward produces no additional entry (confirms
    `CleanupEffectNotificationsSystem` sweeps `WarResolvedApplied` correctly, mirroring the existing
    `relation_produces_exactly_one_entry...` test's own passive-tick assertion).
  - a `surrender` case confirming winner/loser are swapped relative to `ultimatum`'s.

- **Not automatable — `GameLogLineFormatter.BuildWarResolvedLine` and `CountryActionsView.cs`'s
  `"not_at_war"` reason-text case**: both are Unity-side `Assets/Scripts/Unity/UI/` classes with no
  existing xUnit harness reach (same pre-existing status `GameLogLineFormatter.cs` already has, per
  `26_07_29_00_decrease-enemy-control-card/plan.md`'s own Tests section). Verify manually in the
  Editor (play Ultimatum/Surrender, confirm the Action Log line reads correctly with no literal
  `{0}`/`{1}`; put a copy in hand for a not-currently-at-war country and confirm the unplayable
  reason text renders) or via `/code-review` inspection of the diff — do not attempt to add a
  `Game.Tests` entry for either.

- **Run the full test suite** — use the `dotnet-test` skill against `src/GlobalStrategy.Core.sln`
  after implementation, confirming all new/updated cases pass with no regressions elsewhere
  (particularly `WarsTests.cs`'s pre-existing `StopWar` cases, which must be unaffected by the
  `TryGetWarId`/`DestroyWarAndParticipants` extraction).

## Constitution Check

No conflicts found — plan aligns with all principles:
- **ECS for all game logic, living in `src/`** — all new state (`WarOutcome`, `WarResolvedApplied`)
  and behavior (`Wars.ResolveWar`/`GetOwnWarProgress`, the `ResolveWarEffectParams` dispatch branch,
  the DSL node additions) lives under `src/`; the only `Assets/Scripts/Unity/` changes are pure
  presentation (a new `GameLogLineFormatter` method, an `ActionLogView` switch case, a
  `CountryActionsView` reason-text case) — no game state or domain rule lives in a MonoBehaviour.
- **VContainer is the sole DI mechanism** — no new service registration; `Wars`/`CreateActionEffectSystem`
  remain static helpers/systems called directly, same as every existing sibling.
- **UI Toolkit only** — no Canvas/UGUI touched; `ActionLogView`/`CountryActionsView` already use UI
  Toolkit exclusively.
- **Plan before implement** — this plan is the gate; no code/asset changes are made until it is
  approved.
- **Spec before plan for feature work** — `spec.md` already exists and was approved before this plan.
- **File organisation** — this plan lives at
  `Docs/Specs/26_07_29_21_ultimatum-surrender-cards/plan.md`, alongside its `spec.md`.
- **One asmdef per feature folder** — no new Unity `Assets/Scripts/` feature folder is introduced;
  every touched Unity file already lives in an existing asmdef-covered folder (`GS.Unity.UI`).
- **C# code style** — new files/methods use tabs, brace-on-same-line, `_`-prefixed private fields,
  no redundant access modifiers, matching `Wars.cs`/`RelationClearedApplied.cs`/`ControlQuery.cs`
  precedent throughout.

Use the implement skill to start working on the plan or request changes.
