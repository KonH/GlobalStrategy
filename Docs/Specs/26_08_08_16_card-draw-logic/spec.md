# Spec: Card Draw Rework — Logic

## Feature Intent

As a player managing my organisation's country-action cards, I want to explicitly draw a small choice of cards and keep one, so that building an eight-card hand is a deliberate decision rather than an automatic refill.

This spec covers issue #153's **spec A** (gameplay commands, draw/receive state, hand rules, and bot use). The sibling UI and animation behavior is specified in `Docs/Specs/26_08_08_16_card-draw-ui/spec.md`.

Terminology note: the issue says "org hand" while the current playable surface is the organisation-owned **country-card** hand (`CardOwnerKind.Country`). The owner confirmed that this feature applies only to that country-card pool. Production's disabled org-card pool (`orgPools: []`) remains unchanged.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

### Empty opening hand and eight-card cap

- A new game starts for any participating organisation.
  - Its country-card deck is created => its country-card hand contains zero cards and there is no active draw offer; no automatic opening deal occurs.
  - The hand is inspected through gameplay state, player UI, or bot observation => its capacity is 8.
- A saved game made before this feature is loaded with country cards already in hand => those cards remain in their saved slots; the new cap is 8 and loading does not silently discard or redraw them.
- The country-card hand already contains 8 cards => a draw command is rejected without consuming RNG or changing the deck, hand, or any existing offer.
- Org-owned cards (`CardOwnerKind.Org`) => their current initialization/cap behavior is unchanged; they remain out of production because `orgPools` is empty.

### Start a draw offer

- The country-card hand has at least one free slot, no offer is active for that organisation, and at least three cards are drawable.
  - A valid draw command is processed => exactly 3 distinct card entities are sampled, ordered as choices 0–2, and marked as one active offer; none is added to the hand yet.
  - Candidates are sampled => the existing `ActionDefinition.DeckCopies` weighted probability is preserved and sampling is without replacement, so one physical card entity cannot occupy two choices.
  - A card currently fails its play requirements, is on cooldown, or is unaffordable => those facts do not exclude it from the offer; draw continues to ignore playability exactly as established by `Docs/Specs/26_08_06_10_card-deck-rework/spec.md`.
  - A card is already in hand, belongs to another organisation/owner kind, is in the active offer, or is transiently excluded by a paid discard => it cannot be sampled.
- Only one or two drawable cards remain.
  - The draw command is processed => all remaining cards are offered in stable choice order and the player can still fill the hand; the feature does not require three candidates and strand a free slot.
- No drawable card remains => the command makes no offer and does not mutate or consume RNG.
- An offer is already active.
  - Any additional draw command for the same organisation's country-card pool => it is rejected without rerolling or replacing the current choices.
- Two different organisations draw during the same simulation => each receives an isolated offer sampled only from its own deck; neither command can consume or resolve the other's cards.
- The same initial world and RNG seed receive the same command sequence => offer contents and choice order are deterministic.

### Receive one offered card

- An active offer exists and the receive-card command names a valid choice index for the same organisation and country-card pool.
  - The command is processed => only the selected card receives `CardInHand`, using the lowest available slot in the 0–7 range.
  - The command completes => offer markers are cleared from every offered card atomically; the unselected cards return immediately to the drawable deck and are not marked as played/paid-discarded.
  - The selected card currently cannot be played in the selected country => it is still received; selection is not a playability check.
- A receive command has a stale/out-of-range choice index, wrong organisation, wrong owner kind, or no corresponding active offer => the hand and offer remain unchanged.
- The same receive command is delivered twice => at most one card enters the hand; the second command is stale and has no effect.
- A malformed/concurrent sequence attempts to receive when capacity is no longer available => the authoritative system never creates a ninth hand card. Offer creation reserves one slot so normal in-scope commands cannot cause this state; the receive system still rechecks the invariant defensively.

### Manual replenishment and paid-discard offers supersede automatic replacement

- A country card is played successfully or fails after being submitted.
  - The card leaves its hand slot => no `CheckHandSizeSystem` replacement draw is scheduled and no new card enters the hand automatically.
  - The hand is below 8 afterward => it stays below 8 until an explicit draw + receive sequence succeeds.
- A valid paid-discard command succeeds.
  - The card leaves its hand slot and is marked discarded => the same update starts the normal up-to-three-card offer for that organisation without requiring a separate draw command.
  - Offer candidates are sampled while the paid-discarded card is still excluded from the drawable deck => that card cannot appear in the offer triggered by its own discard; after the offer is created, normal discard cleanup returns it to the deck.
  - No other drawable card remains => the paid discard still succeeds, no offer is created, and the vacancy remains available for a later explicit draw.
- Debug force-draw/force-discard commands are used => they remain explicit cheats and may bypass the normal offer/cap flow; production commands do not call those paths.

### Pending offers survive interruption

- A game is saved while a draw offer is awaiting selection, then loaded.
  - The same ordered offered card identities and choice indices are restored => loading never rerolls, selects, or returns them silently.
  - The corresponding UI can reconstruct the pending choice directly from projected state as described by the sibling UI spec.
- A dynamic relation changes while a relation-targeted card is offered => the offer remains the same; current playability may update, but card identity and availability for receipt do not.

### Bot draw and selection

- A bot-controlled organisation has a country-card hand below 8 and no active offer.
  - Its card-acquisition phase => it emits the same draw command used by the player; it does not add cards directly or call the debug force-draw path.
- A bot-controlled organisation has an active offer.
  - Its next card-acquisition phase => it emits the same receive-card command used by the player for exactly one offered choice.
  - The bot's shared acquisition selector => it first prefers a control-raising choice usable in at least one country with control-pool capacity, then another control-raising choice, then a currently playable/affordable choice; if none qualifies, the lowest choice index wins deterministically. Control-raising cards are the owner's confirmed meaning of required cards.
  - Acquisition is serviced on successive game updates independently of the once-per-game-day play-action throttle => an empty bot hand can be filled without waiting a day between the draw and receive halves of every choice.
- A bot is resolving or starting an offer => it does not also submit a play-card command in that acquisition step; card play resumes through the existing feature cadence once no acquisition command is needed.
- Multiple bots acquire cards => their command sinks stamp their own org ids and cannot see or resolve another bot's offer.

## Tech Notes

### Existing behavior being replaced

- `src/Game.Main/InitSystem.cs`:
  - `CreateCountryActionEntities` currently adds `CardDraw { Count = handSize }` to deal the opening country hand. Remove this initial draw for the affected pool.
  - `CreateActionEntities` directly inserts org cards into the opening hand; leave it unchanged under the confirmed country-card-only scope.
- `src/Game.Systems/CheckHandSizeSystem.cs` currently converts every post-play/discard vacancy into `CardDraw`. Remove it from the production `GameLogic.Update` pipeline (and delete or narrow it if no test/debug caller still needs it).
- `src/Game.Systems/DrawCardSystem.cs` currently consumes `CardDraw` and adds `CardInHand` immediately. Refactor the production path into offer creation plus explicit receipt while keeping `ForceDrawCard` as a debug-only helper.
- `src/Game.Systems/DiscardCardSystem.cs` and `RemoveCardFromHandSystem` still remove the chosen/played card. A successful paid discard feeds the normal offer-creation path in the same update; ordinary play success/failure does not.
- `Assets/Configs/action_config.json`: change the `country` default `handSize` from 5 to 8. There is currently no second checked-in `action_config.json` mirror.
- `CardHand` is savable, so `GameLogic.LoadState` must reconcile a loaded country deck's saved cap to the current configured country cap (8) while preserving every existing `CardInHand` slot and never dealing cards during load.

### Commands and authoritative offer state

- Add `src/Game.Commands/DrawCardsCommand.cs` and `ReceiveCardCommand.cs`. Commands must carry `[OrgId] string OrgId`; `ReceiveCardCommand` additionally carries `int ChoiceIndex`. Because this draft affects only country cards, no owner-kind field is needed. If scope expands, represent the owner kind without creating a `Game.Commands` → `Game.Components` dependency (for example a validated string value).
- Add savable ECS state under `src/Game.Components`, for example:
  - a deck-level `PendingCardDraw` identifying the active offer and its option count;
  - a card-level `CardDrawChoice { int ChoiceIndex; }` on each offered card.
  Both must be `[Savable]`, since the choice survives save/load.
- Choice index—not action id—is the command identity. Multiple relation cards may share `ActionId` and `TargetCountryId` while differing by primary `CountryContext`, so action/target strings alone are not a safe selector.
- The offer-creation system reuses the weighted, without-replacement candidate selection already in `DrawCardSystem.DrawCards`, excluding `CardInHand`, `CardDrawChoice`, and `CardDiscard`. The receive system locates the deck's active offer, validates the choice, assigns the lowest free slot, then clears the complete offer in one update.
- Preserve the existing `DeckCopies <= 0` exclusion and draw-time requirement bypass.
- Feed successful `DiscardCardResult` values into offer creation from the top-level game-loop orchestration. Create that offer before `CleanupCardDiscardSystem` removes `CardDiscard`, so no additional exclusion component is needed and a paid-discarded card cannot reappear in its own replacement offer.

### System ordering

- `src/Game.Main/GameLogic.cs` must process relation/revenge card synchronization before creating offers so newly valid dynamic card entities participate in the candidate set.
- Process receipt only against an offer that was already authoritative/projected before the receive command. Do not let a client combine an unobserved draw and guessed receive index to select from a same-buffer reroll.
- Remove the current `CheckHandSizeSystem.Update` auto-refill call. Keep `DiscardCardSystem`, offer creation, and `CleanupCardDiscardSystem` ordering explicit so paid discard starts an offer while its discarded card remains excluded, whereas played cards simply return to the deck after cleanup.

### Visual and bot projections

- `src/Game.Main/VisualState.cs` / `VisualStateConverter.UpdateCountryActions`:
  - add an ordered `DrawChoices` collection and `CanStartDraw`/active-offer state to `CountryActionsState`;
  - project offered cards separately from `Hand` and `Deck`;
  - use a dedicated entry such as `CardDrawChoiceEntry { ChoiceIndex, ActionCardEntry }` rather than overloading hand `SlotIndex`;
  - include primary `CountryContext` (or an equivalent stable domain identity) where the UI needs to match the selected choice to its newly assigned hand card.
- Update `StateEquality`, `ActionCardEntryEqualityTests`, visual-state tests, and list-state benchmarks for the new collections/fields.
- `src/Game.Bots/BotObservation.cs`, `IBotObservation.cs`, and bot view models need country-hand count/cap plus ordered offered-choice metadata. Reuse the current `RaisesControl`, cost, playability, relation target, and per-country evaluation rather than inventing a second card-classification path.
- `src/Game.Bots/IBotCommandSink.cs` / `BotCommandSink.cs` add whitelisted draw and receive methods that stamp `_orgId`; update their reflection/duplicate-guard tests.
- Split bot acquisition from `Bot.ExecuteDecisionTick`'s `_lastActedDate` guard or otherwise service it before that guard. Keep draw-choice selection in the shared acquisition path, using the existing `RaisesControl`, playability, affordability, and country-cap observations instead of relying on incidental offer order or mutating cards from a feature.

### Verification surface

- Replace/extend `DrawCardSystemTests` and `InitSystemTests` for: empty start, cap 8, weighted three-choice offers, fewer-than-three offers, no playability filtering, duplicate/stale command safety, org isolation, and deterministic order.
- Update `DiscardCardSystemTests`, `BaselineCardPlayTests`, bot observation/command-sink/determinism tests, and unified pipeline tests for manual replenishment and command-only bot acquisition.
- Add a save/load round-trip test with an unresolved offer and a receive-after-load assertion.

## Out of Scope

- Changing `DeckCopies` weighting, draw-time requirement filtering, action effects, cooldowns, costs, card names, or target-country rules.
- Merging the org-card and country-card pools.
- Re-enabling the currently empty/hidden org-card feature.
- Redesigning paid-discard pricing or its hold gesture; only its successful follow-up changes from direct replacement to the shared choose-one offer.
- Changing debug force-draw behavior into the production choice flow.
- UI layout/animation details, which belong to the sibling UI spec.

## Resolved Owner Decisions

0. Scope is the organisation-owned country-card hand only; disabled org cards remain unchanged.
1. The country-card hand cap becomes 8; no hidden org-card UI or logic is re-enabled.
2. Offers contain up to three cards when fewer than three drawable entities remain.
3. A successful paid discard itself triggers the shared up-to-three offer flow.
4. Bot acquisition treats control-raising cards as required draw choices before playable/affordable and lowest-index fallbacks.
