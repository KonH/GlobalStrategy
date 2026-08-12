# Plan: Country-Targeted Make Friend / Make Rival Cards

## Spec

As a player, I want "Make Friend" and "Make Rival" to each name the specific other country they will affect (e.g. "Make France rival!"), instead of the game silently rolling a random target after gold is committed.

Key acceptance criteria (see `spec.md` for full detail):
- No single generic "Make Friend"/"Make Rival" card — a distinct card instance exists per `IsAvailable` target country (~19 instances each in the shipping country roster), mirroring `stop_friendship`/`stop_rivalry`'s per-target instance model.
- An instance naming a country the selected country already has a Friend/Rival relation with is shown unplayable; it becomes playable again once that relation clears, with no instance created/destroyed.
- An instance naming the selected country itself is always unplayable, regardless of opinion/relation state.
- The existing 30+ opinion gate, unchanged gold cost, cooldown, and control-pool checks are untouched and apply per instance.
- Playing a valid instance always succeeds and sets the relation with the exact named country only — no roll, no proximity/random pick.
- Card face text reads "Make France friend!"/"Make Germany rival!" via the existing `{0}` locale template.
- `enableFriendsRelation = false` still suppresses all `make_friend` instances for that org; `make_rival` is unaffected.

## Approach

**This is a self-contained follow-up to the already-shipped `stop_friendship`/`stop_rivalry` feature (`Docs/Specs/26_07_24_13_stop-friendship-rivalry-cards/`) — all of that feature's plumbing (`RelationCardTarget`, the `{0}` display templating in `CountryActionsView.ComposeFaceData`/`VisualStateConverter.BuildEntry`, `PlayCardActionCommand.TargetCountryId` end-to-end disambiguation through `InitActionFromPlayCardSystem`/`CardPlayAnimator`/`CardTransitionView`, the opinion-gate wiring in all three condition evaluators) is confirmed already live on `main` and requires zero changes here.** Verified directly against source, not inferred from that plan's own text.

**Correcting two Tech Notes inaccuracies found during research before implementing:**
1. `SetCountryRelationEffect` is extended in place inside `src/Game.Components/CountryRelation.cs` (where it actually already lives, alongside `CountryRelation` itself) — not in a new `SetCountryRelationEffect.cs` file as the spec's Tech Notes suggested.
2. `SetCountryRelationSystem.Update` keeps its current inline `world.Destroy(entity)` for its own marker after processing — it does **not** switch to the `CleanupActionEffectsSystem`-next-tick pattern `ClearCountryRelationEffect` uses. The two pathways were never actually symmetric today (`ClearCountryRelationSystem` leaves its marker for `CleanupActionEffectsSystem` to strip a tick later; `SetCountryRelationSystem` already destroys inline); only the candidate-search/coin-flip is being removed here, not the destroy timing.

**Genuine gap found during research, not mentioned in the spec's Tech Notes:** `src/Game.Tests/GameLogStateTests.cs::relation_produces_exactly_one_entry_with_target_and_kind_and_no_extra_on_a_passive_tick` plays a bare, targetless `make_friend` card and depends on today's single-entity-per-org creation model to get a deterministic relation outcome (its own code comment notes the assertion is only deterministic because exactly one candidate country exists in that fixture). This test is rewritten as part of this plan (see Tests) to target a specific `make_friend` instance explicitly, since after this change two independent per-target instances exist and compete for the hand slot instead of one guaranteed entity.

**Load `countryConfig` once, not twice.** `InitSystem.Run` already loads `countryConfig` near its own top before calling `CreateCountryActionEntities` a few lines later. Rather than following the spec's literal suggestion to reload it inside `CreateCountryActionEntities`, this plan threads the already-loaded `countryConfig` through as a new parameter — avoiding a redundant JSON deserialization every game start.

Everything else in the spec's Tech Notes — `InitSystem.CreateCountryActionEntities`'s existing structure/gates, `RelationCardTarget`'s existing shape, `CountryActionConditionContext.Build`'s self-target gap (confirmed real, exact mechanism as described), `CreateActionEffectSystem`'s `ClearCountryRelationEffectParams` branch shape to mirror, `CountryRelations.GetSuitableRelationCandidates` becoming genuinely dead code (confirmed via repo-wide grep), `DrawCardSystem`'s per-entity weight semantics, the generic `{0}` templating requiring zero UI code changes, and `BotObservation.cs` requiring zero changes — was verified accurate against the current source and is implemented as described below.

## Steps

### Agent Steps

- [ ] **Self-targeting guard in `CountryActionConditionContext.Build`** (`src/Game.Systems/CountryActionConditionContext.cs`, relation-value loop) — compute `bool sameCountry = !string.IsNullOrEmpty(relationTargetCountryId) && countryId == relationTargetCountryId;` right after `relationTargetCountryId` is resolved, and fold it into the existing relation-value loop's guard condition so `none`/`friend`/`rival` are all hard `0.0` for a self-targeted card, regardless of `CountryRelations.GetRelation`'s own equal-id `null` special-case. Add a covering case to `src/Game.Tests/CountryActionConditionContextTests.cs` (e.g. `build_treats_self_target_as_no_relation_of_any_kind`): build a `RelationCardTarget{TargetCountryId="Prussia"}` card and call `Build` with `countryId="Prussia"`, asserting the `"none"` relation value is `0`.

- [ ] **Add `TargetCountryId` to `SetCountryRelationEffect`** — in `src/Game.Components/CountryRelation.cs` (not a new file — see Approach), add `public string TargetCountryId;` to the existing `SetCountryRelationEffect` struct, matching `ClearCountryRelationEffect`'s shape.

- [ ] **`CreateActionEffectSystem`'s `SetCountryRelationEffectParams` branch reads the fixed target** (`src/Game.Systems/CreateActionEffectSystem.cs`) — add `&& world.Has<RelationCardTarget>(entity)` to the branch guard (mirroring the `ClearCountryRelationEffectParams` branch immediately below it), and populate `TargetCountryId = world.Get<RelationCardTarget>(entity).TargetCountryId` on the created `SetCountryRelationEffect` marker.

- [ ] **Simplify `SetCountryRelationSystem`** (`src/Game.Systems/SetCountryRelationSystem.cs`) — delete `ResolveRelation` and `PickByProximity` entirely. Replace `Update`'s body with a direct-set loop reading `TargetCountryId` off each marker: `relations.SetRelation(world, countryId, targetCountryId, kind)`, emit `RelationSetApplied { OrgId, CountryId, TargetCountryId, Kind }`, then `world.Destroy(entity)` (unchanged from today — see Approach). Drop the now-unused `proximityEntity`/`rng` parameters — new signature `Update(World world, CountryRelations relations)`, matching `ClearCountryRelationSystem.Update`'s signature exactly.

- [ ] **Update the sole call site** — `src/Game.Main/GameLogic.cs` (around line 275), change `SetCountryRelationSystem.Update(_world, _relations, _proximityEntity, _rng);` to `SetCountryRelationSystem.Update(_world, _relations);`.

- [ ] **Remove `CountryRelations.GetSuitableRelationCandidates`** (`src/Game.Systems/CountryRelations.cs`) — confirmed dead once `SetCountryRelationSystem` no longer calls it (repo-wide grep found no other caller). Delete the method.

- [ ] **Rewrite `src/Game.Tests/SetCountryRelationSystemTests.cs`** — remove the proximity-weighting (`proximity_weighting_favors_the_nearer_candidate_over_many_runs`) and candidate-search tests (`excludes_source_country_and_countries_already_related`, `no_suitable_candidate_is_a_safe_noop`), which no longer apply. Rewrite `AddMarker` to also set `TargetCountryId`, update the two remaining tests (`resolves_a_relation_of_the_requested_kind_and_destroys_the_marker`, `emits_relation_set_applied_matching_the_resolved_pair`) to pass an explicit target and call `SetCountryRelationSystem.Update(world, _relations)` (no `proximityEntity`/`rng` args). Add a case confirming the relation lands on the exact named target even when other unrelated candidate countries exist (replacing the old "candidate pool" coverage with "no candidate pool at all — direct set").

- [ ] **Extend `InitSystem.CreateCountryActionEntities`** — add a `CountryConfig countryConfig` parameter (threaded from `Run`, which already has it loaded — pass it into the existing call site instead of re-loading). Inside the per-`def` loop, branch `make_friend`/`make_rival` before the existing generic skip condition: keep the existing `def.DeckCopies <= 0`, `enableFriendsRelation` (make_friend only), and `TargetRole`/`availableTargetRoles` gates (evaluated once per org, same as today), then loop `foreach (var targetEntry in countryConfig.Countries)` where `targetEntry.IsAvailable`, creating one entity per target: `GameAction{ActionId}` + `OrgContext{OrgId}` + `CardOwnerType(CardOwnerKind.Country)` + `RelationCardTarget{TargetCountryId=targetEntry.CountryId, Kind=Friend|Rival}` — no `CountryContext`, no self-exclusion at creation time (handled entirely by the condition-context guard added above). Fall through to the existing generic single-entity branch unchanged for every other `def.ActionId`.

- [ ] **`Assets/Configs/action_config.json`** — change `make_friend.deckCopies` from `9` to `1` and `make_rival.deckCopies` from `15` to `1` (matching the established `stop_friendship`/`stop_rivalry`/`declare_war` per-instance convention — this mechanically shifts aggregate draw weight from a fixed 9/15 to "however many eligible-target instances exist," ~19 each; called out to the owner as an acknowledged side effect per the spec's Out of Scope). No other field changes — `conditions`, `cost`, `cooldownDays`, `targetRole`, `effectIds` stay exactly as they are today.

- [ ] **Locale keys** (use the `localization` skill for a real Russian translation, not a placeholder) — in both `Assets/Localization/en.asset` and `ru.asset`:
  - `action.make_friend.name`: `Make Friend` → `Make {0} friend!`
  - `action.make_rival.name`: `Make Rival` → `Make {0} rival!`
  - `action.make_friend.desc` / `action.make_rival.desc` stay unchanged (`Mark new country as friend.` / `Mark new country as rival.`).

- [ ] **`src/Game.Tests/InitSystemTests.cs`** — add a new test asserting the per-target creation shape directly: with a small fixed `CountryConfig` (e.g. 3 available countries) and `make_friend`/`make_rival` in the action config, assert exactly `N-1` `RelationCardTarget`-bearing entities exist per org per action id (one per other available country), and that `enableFriendsRelation = false` still yields zero `make_friend` entities of any kind. Existing tests referencing `make_friend`/`make_rival` — verified compatible with the new model, since they only assert "found in hand" via boolean flags, not entity counts — need no changes.

- [ ] **Rewrite `GameLogStateTests.cs::relation_produces_exactly_one_entry_with_target_and_kind_and_no_extra_on_a_passive_tick`** — this test currently plays a targetless `make_friend` and relies on the old single-candidate-pool determinism (see Approach). Rewrite to build/force a specific `RelationCardTarget`-bearing `make_friend` instance into hand (e.g. via `DrawCardSystem.ForceDrawCard` with an explicit `targetCountryId`, or direct entity construction with `CardInHand` + `RelationCardTarget`), push `PlayCardActionCommand { ..., TargetCountryId = <chosen target> }`, and assert the `RelationSetApplied` game-log entry names that exact target — removing the now-inapplicable "candidate pool of exactly one" code comment.

- [ ] **Add coverage confirming the new guard on `SetCountryRelationEffectParams`** — alongside `GameLogStateTests.cs`'s relation coverage (or wherever most directly exercises this system), confirm a play of a `make_friend`/`make_rival`-shaped entity with no `RelationCardTarget` (not reachable in production after this change, but defends the new guard) creates no `SetCountryRelationEffect` marker at all.

- [ ] **Run `/dotnet-build Release`** (or the `dotnet-build` skill) and fix any compile errors before finishing, per project workflow — this feature touches only `src/`.

### User Steps

### 1. None

None — this feature requires no Unity Editor scene/asset work; all changes are code (`src/`), config (`Assets/Configs/action_config.json`), and locale (`Assets/Localization/en.asset`, `ru.asset`) files, consistent with the existing `stop_friendship`/`stop_rivalry` precedent.

## Tests

- **`src/Game.Tests/CountryActionConditionContextTests.cs`**: new case for the self-targeting guard — `RelationCardTarget.TargetCountryId == countryId` must yield `0` for `none`/`friend`/`rival` alike, not just fall through to `MatchesCondition`'s existing self-safe behavior.
- **`src/Game.Tests/SetCountryRelationSystemTests.cs`**: full rewrite per Agent Steps — drop proximity/candidate-search cases, add direct-set-to-named-target cases, update all `Update(...)` call sites to the two-argument signature.
- **`src/Game.Tests/InitSystemTests.cs`**: new test asserting per-target `RelationCardTarget` entity creation count and shape for `make_friend`/`make_rival` (one per `IsAvailable` country, gated by `enableFriendsRelation`/`TargetRole`/`DeckCopies` exactly as today); existing four `make_friend`/`make_rival`-referencing tests verified to need no changes.
- **`src/Game.Tests/GameLogStateTests.cs`**: rewrite `relation_produces_exactly_one_entry_with_target_and_kind_and_no_extra_on_a_passive_tick` to target a specific instance explicitly (see Agent Steps) instead of relying on removed single-candidate-pool determinism.
- **New coverage for `CreateActionEffectSystem`'s `SetCountryRelationEffectParams` guard**: confirm no marker is created without a `RelationCardTarget` present.
- **Locale parity**: covered by whatever existing test asserts every `nameKey`/`descKey` in `action_config.json` has both `en`/`ru` entries — no new test needed beyond adding the keys themselves.
- Full suite: `dotnet test` on `src/GlobalStrategy.Core.sln` must stay green, then `/dotnet-build Release` per project workflow.

## Constitution Check

No conflicts. This is a self-contained ECS/config/locale change:
- **ECS-only game logic in `src/`**: all behavior changes are in `src/Game.Systems`/`src/Game.Main`/`src/Game.Components`; no MonoBehaviour logic added.
- **VContainer DI**: untouched — no new services, no new registrations.
- **UI Toolkit only**: no UI code changes at all — `CountryActionsView.ComposeFaceData`, `VisualStateConverter.BuildEntry`, and the full `CardPlayAnimator`/`CardTransitionView` play-animation pipeline already handle `RelationCardTarget`-bearing cards generically (verified against source, confirmed working for `stop_friendship`/`stop_rivalry` today).
- **One plan file per feature under `Docs/Specs/`**: this plan lives at `Docs/Specs/26_08_08_23_country-targeted-relation-cards/plan.md`, alongside the already-approved `spec.md`.
- **Spec before plan**: satisfied — `spec.md` already exists and precedes this plan.

No new `.asmdef`, no new DI registration, no ScriptableObject types, no Canvas/UGUI. The two deliberate corrections to the spec's literal Tech Notes (extending `SetCountryRelationEffect` in `CountryRelation.cs` rather than a new file, and keeping `SetCountryRelationSystem`'s inline marker-destroy rather than adopting `ClearCountryRelationEffect`'s leave-for-cleanup pattern) are factual corrections against verified source, not deviations from settled design intent — flagged in Approach for visibility, no owner sign-off needed beyond this plan surfacing them.

Use the implement skill to start working on the plan or request changes.
