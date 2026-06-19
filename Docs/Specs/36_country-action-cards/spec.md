# Spec: Country Action Cards

## Feature Intent

As a player, I want to play action cards targeting a selected country — improving influence and building relations with its characters — so that engaging with individual countries has a meaningful, card-driven gameplay loop beyond passively accumulating influence.

## Acceptance Criteria

### Hand Cap and Initial State

- **Given** the player's organisation is initialised **When** the action system sets up country-action hands **Then** the country-action hand for each country is capped at 3 cards (`handSize = 3`).

- **Given** a country has no prior interaction history **When** the player first selects it **Then** the hand contains exactly one card: "Sphere of Pressure" (the Add Influence card), placed in slot 0; the remaining two slots are empty.

- **Given** the player selects a country they have previously interacted with **When** the hand panel is displayed **Then** it shows whatever cards are currently in that country's hand (persisted across selections).

---

### Card: Add Influence — "Sphere of Pressure"

- **Given** the player opens country actions **When** "Sphere of Pressure" is in hand **Then** it is displayed with success rate 50%, cost 200 gold, and the description from the Card Proposals section.

- **Given** the country influence pool is full (total claimed influence across all orgs ≥ 100) **When** "Sphere of Pressure" is evaluated **Then** it is shown as unplayable (greyed out) and the condition label reads "No influence pool space remaining"; it is playable whenever at least 1 pool point is unclaimed.

- **Given** the country influence pool has at least 1 unclaimed point and the player has 200+ gold **When** the player plays "Sphere of Pressure" **Then** a success roll is made at 50%; on success, 10 influence is added to the player's org in that country (excess beyond the pool cap of 100 is silently discarded; the play is still considered successful); on failure, no influence is added; in both cases the card leaves the hand.

- **Given** "Sphere of Pressure" has been played **When** the cooldown timer starts **Then** another copy of this card cannot re-enter the hand for 1 calendar month; the deck contains 3 copies total, so subsequent draws are possible once the cooldown expires.

---

### Card: Improve Advisor Relations — "Letter of Commendation"

- **Given** there is at least one advisor-role character in the selected country **When** the action system builds the deck for that country **Then** one distinct named card exists per advisor role (Foreign Minister card, Finance Minister card, Military Advisor card, Interior Minister card, etc.), each carrying the advisor's `characterId` as its target and its own name and description; each advisor card contributes 3 copies to the deck.

- **Given** the player's org has fewer than 10 influence in a country **When** the draw system evaluates advisor cards for that country **Then** none of them are eligible to enter the hand; they remain in the deck.

- **Given** the player's org has 10+ influence in a country **When** a hand slot becomes free (after playing a card) **Then** an advisor card (for any advisor in that country) may be drawn at random into the hand if one is available in the deck.

- **Given** an advisor card (e.g. "Diplomatic Dispatch", "Treasury Commission") is in hand **When** displayed **Then** it shows its own distinct card name and description (from Card Proposals), the targeted advisor's name in a secondary label, the dynamic success rate formula `30% + influence/2` evaluated at the current influence value, cost 50 gold.

- **Given** the player plays an advisor card with 40 influence **When** the success roll is made **Then** the effective success rate is 50% (30% base + 20% from 40÷2); on success: an `OpinionModifier { SourceId = "letter_of_commendation", Value = 50, ChangeValue = -1 }` is added to the targeted advisor (lasting ~50 months) and, if the country influence pool is not yet full (at least 1 unclaimed point remains), 1 influence is added to the player's org in that country (excess beyond cap is silently discarded); on failure: neither effect applies; in both cases the card leaves the hand.

- **Given** an advisor card has been played **When** cooldown starts **Then** no copy of that specific advisor's card can re-enter the hand for 2 calendar months.

---

### Card: Improve Ruler Relations — "Royal Audience"

- **Given** a country has a ruler-role character **When** the deck for that country is built **Then** "Royal Audience" cards targeting that ruler are included (3 copies in the deck).

- **Given** the player's org has fewer than 20 influence in a country **When** the draw system evaluates "Royal Audience" cards **Then** none are eligible to enter the hand.

- **Given** the player's org has 20+ influence in a country **When** a hand slot becomes free **Then** a "Royal Audience" card may be drawn into the hand if one is available in the deck.

- **Given** "Royal Audience" is in hand **When** displayed **Then** it shows the dynamic success rate `20% + influence/3` evaluated at current influence, cost 100 gold, and the description from Card Proposals.

- **Given** the player plays "Royal Audience" with 30 influence **When** the success roll is made **Then** the effective rate is 30% (20% base + 10% from 30÷3); on success: an `OpinionModifier { SourceId = "royal_audience", Value = 25, ChangeValue = -1 }` is added to the targeted ruler (lasting ~25 months) and, if the country influence pool is not yet full (at least 1 unclaimed point remains), 2 influence is added to the player's org in that country (excess beyond cap is silently discarded); on failure: neither effect applies; in both cases the card leaves the hand.

- **Given** "Royal Audience" has been played **When** cooldown starts **Then** no copy for the same ruler can re-enter the hand for 3 calendar months.

---

### Post-Play Flow

- **Given** any country action card is played **When** the play resolves (success or failure) **Then** the system immediately re-evaluates which cards in the deck are eligible for the current influence level and draws one at random from the eligible subset into the vacated hand slot if any eligible card exists.

- **Given** a card is drawn after play **When** influence has risen due to the just-played card **Then** the new influence value (post-effect) is used for eligibility checks, not the pre-play value.

- **Given** a card's cooldown has not yet expired **When** the draw system runs **Then** that card (all copies for the same advisor/ruler target if applicable) is skipped and the next eligible card is considered.

---

### Cooldown Display

- **Given** a card is on cooldown **When** it appears in hand (greyed out) or in the deck pile **Then** a secondary text row appears below the card name on a semi-transparent background showing the remaining duration.

- **Given** the remaining cooldown duration is 365+ days **When** the cooldown label is rendered **Then** it displays "N year(s)" (e.g. "1 year", "2 years").

- **Given** the remaining cooldown duration is 30–364 days **When** the cooldown label is rendered **Then** it displays "N month(s)" (e.g. "4 months", "1 month").

- **Given** the remaining cooldown duration is 2–29 days **When** the cooldown label is rendered **Then** it displays "N days" (e.g. "27 days", "2 days").

- **Given** the remaining cooldown duration is exactly 1 day **When** the cooldown label is rendered **Then** it displays "1 day".

- **Given** the remaining cooldown duration is less than 1 calendar day **When** the cooldown label is rendered **Then** it displays "less than a day".

---

### Dynamic Success Rate Tooltip

- **Given** a card has a dynamic success rate (Letter of Commendation or Royal Audience) **When** the player hovers over the success rate percentage **Then** a tooltip appears showing the breakdown, e.g. "50% = 30% base + 20% from 40 influence".

- **Given** the influence value changes (e.g. after playing another card) **When** the tooltip is re-opened **Then** it reflects the updated influence and recalculated total.

- **Given** a card has a fixed success rate (Sphere of Pressure) **When** the player hovers over the rate **Then** no breakdown tooltip is shown (the value is static and self-explanatory).

---

### Country Selection UI — Actions Button

- **Given** the player selects a country **When** the country info panel is displayed **Then** an "Actions" button appears in the country panel alongside the existing controls, mirroring the same button that exists in the player org view.

- **Given** the player clicks the "Actions" button in the country panel **When** the panel opens **Then** it shows the three-slot hand and deck pile for country-targeted cards, using the same visual structure as the org actions panel.

- **Given** the player has not yet selected a country **When** no country is selected **Then** the "Actions" button is not visible.

---

### Bug Fix: Mutual Exclusion of Cards and Characters in Org Panel

- **Given** the org info panel is open with the Characters slide visible **When** the player clicks the "Actions" button **Then** the Characters slide closes (`_charsOpen = false`) before the Actions slide opens (`_actionsOpen = true`); both are never simultaneously open.

- **Given** the org info panel is open with the Actions slide visible **When** the player clicks the "Characters" button **Then** the Actions slide closes (`_actionsOpen = false`) before the Characters slide opens (`_charsOpen = true`); both are never simultaneously open.

- **Given** either slide is already open **When** the player clicks its own button again **Then** the slide toggles closed (existing toggle behaviour is preserved).

---

## Card Proposals

### Add Influence — "Sphere of Pressure"

> *Dispatch your agents to consolidate the organisation's foothold. A show of coordinated strength may tip the balance of local loyalties.*

- **Flavour note:** Evokes covert economic and political manoeuvring characteristic of 19th-century great-power competition.

---

### Improve Advisor Relations — Per-Advisor Cards

Each advisor role has its own distinct card name and description. All share the same mechanical profile (cost 50 gold, `30% + influence/2` success rate, `OpinionModifier` on success, +1 influence if pool not full). The targeted advisor's name is shown in a secondary label on the card face.

---

#### Foreign Minister — "Diplomatic Dispatch"

> *A private communiqué delivered to the foreign minister's desk — cordial, well-argued, and impossible to ignore. Goodwill at the ministry opens corridors that force alone cannot.*

- **Flavour note:** Reflects 19th-century reliance on personal correspondence and the foreign minister's central role in managing great-power relations.

---

#### Finance Minister — "Treasury Commission"

> *A formal letter of recognition from the organisation, acknowledging the minister's fiscal acumen and proposing closer financial cooperation. Self-interest dressed as flattery.*

- **Flavour note:** Evokes the era's intertwined relationship between private capital and state finance ministries.

---

#### Military Advisor — "Campaign Commendation"

> *A letter extolling the general's past campaigns, delivered through discreet channels. Professional admiration is a currency soldiers understand.*

- **Flavour note:** Captures 19th-century military culture where honour and reputation were as valuable as rank.

---

#### Secret Advisor — "Shadow Accord"

> *A whispered arrangement, conveyed through cut-outs and sealed with mutual benefit. Some alliances are better left unwritten.*

- **Flavour note:** Reflects the era's hidden networks of spies, brokers, and confidential agents operating behind the facade of official diplomacy.

---

### Improve Ruler Relations — "Royal Audience"

> *Securing a private audience with the sovereign is a rare honour. The right words, delivered face to face, may sow the seeds of a lasting alliance.*

- **Flavour note:** Captures the personal diplomacy of 19th-century statecraft, where direct ruler access was a coveted instrument of influence. The targeted ruler's name is shown in a secondary label on the card face.

---

## Out of Scope

- Action cards targeting countries where the player's org has no presence — "Sphere of Pressure" is bootstrapped into the initial hand regardless of pool state, giving the player an entry point.
- Opponent AI organisations using country action cards.
- Card artwork generation (placeholder art is acceptable for initial delivery).
- Negative-outcome cards or cards that reduce another org's influence.
- Country characters other than advisors and rulers being targeted by these cards.
- An "undo" or cancel mechanic after a card play begins.
- Any cooldown mechanic for the initial "Sphere of Pressure" card that is pre-dealt at game start (the cooldown only applies after it is played and a copy re-enters the hand from the deck).
- Saving/loading of per-country action-card state (addressed in a later persistence feature if needed).

