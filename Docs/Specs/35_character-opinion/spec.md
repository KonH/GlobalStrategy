# Spec: Character Opinion

## Feature Intent

As a player, I want to see each country character's opinion of my organization and be able to improve that opinion via a cheat action, so that I have a visible relationship metric I can manipulate during play.

## Acceptance Criteria

### Data Model

- **Given** the game initialises **When** a country character entity is created **Then** it receives a `CharacterOpinion` component with a `Dictionary<string, int> BaseOpinionPerOrg` (empty, all orgs default to 0) and a `Dictionary<string, List<OpinionModifier>> ModifiersPerOrg` (empty); this component is `[Savable]`.

- **Given** any code reads effective opinion for a character toward a specific org **When** it sums the per-org `BaseOpinion` plus the `Value` of all per-org `OpinionModifier` entries on that character **Then** the result is clamped to the range `[-100, +100]` before being returned or stored in visual state.

### Modifier Time-Decay

- **Given** a `CharacterOpinion` component has one or more `OpinionModifier` entries **When** a calendar month elapses **Then** each modifier's `Value` is updated by its `ChangeValue` (e.g. a positive modifier with `Value = 50, ChangeValue = -1` becomes `Value = 49`; a negative modifier with `Value = -50, ChangeValue = +1` becomes `Value = -49`).

- **Given** an `OpinionModifier` entry reaches `Value = 0` after applying `ChangeValue` **When** the monthly decay tick runs **Then** that modifier is removed from the list so it does not accumulate as a zero-value dead entry.

- **Given** a character has multiple `OpinionModifier` entries **When** monthly decay runs **Then** every entry decays independently; the effective opinion is still the clamped sum of all remaining entries plus `BaseOpinion`.

- **Given** the `OpinionModifier` concept is a re-usable struct **Then** it must carry at minimum: `string SourceId` (identifies what added it, e.g. `"cheat_improve_opinion"`), `int Value` (current remaining magnitude), and `int ChangeValue` (the per-month delta applied toward zero: `+1` when `Value` was initially negative, `-1` when positive). Multiple modifiers with the same `SourceId` are allowed to coexist (stacking).

### Cheat Action — Improve Opinion

- **Given** a country is selected and the player is in the debug panel **When** the player presses the "Improve Opinion" cheat button in the existing `character-debug-container` row (alongside Next/Drop) **Then** a command is pushed that adds a new `OpinionModifier { SourceId = "cheat_improve_opinion", Value = 50, ChangeValue = -1 }` to the player org's modifier list for every country character whose `CountryId` matches the selected country.

- **Given** the "Improve Opinion" button is pressed while no country is selected (`SelectedCountryState.IsValid == false`) **When** the command is evaluated **Then** nothing happens (the button should be greyed out or the command is a no-op).

- **Given** the same "Improve Opinion" cheat is applied a second time to the same country's characters **Then** a second `OpinionModifier` (Value = 50) is added on top of any existing ones; the effective opinion is clamped at `+100` at read time, but the raw modifier value is not changed.

- **Given** each subsequent monthly tick after the cheat is applied **When** the decay system runs **Then** each cheat modifier loses 1 point per month until it reaches 0 and is removed; the visual opinion counter updates accordingly.

### UI — Character Card Layout Changes

- **Given** the character card is rendered in `CharactersView.BuildCharacterCard` **When** it is built **Then** the character name label is repositioned to overlay the bottom portion of the portrait (`char-portrait-area`) rather than appearing in the info column above the role label — the portrait area retains its existing height.

- **Given** the character card is rendered **When** it is built **Then** an opinion counter label is inserted in the `char-info` column after the role label and before the skills (`char-stats`) block.

- **Given** the effective opinion for this character toward the player's org is known **When** the opinion counter label is rendered **Then** it displays the integer value with an explicit sign (`+50`, `-20`, `0`) and no decimal fraction.

- **Given** the effective opinion value is negative **When** the label is rendered **Then** it uses a red tint CSS class (e.g. `gs-color-negative`); for zero or positive values it uses a green tint CSS class (e.g. `gs-color-positive`).

- **Given** the player changes the selected country **When** `CharactersView.Refresh` is called **Then** each character card's opinion counter reflects the current effective opinion of that character toward the player's org, using the player org ID from `PlayerOrganizationState`.

### Visual State

- **Given** the ECS world is updated **When** `VisualStateConverter.UpdateCharacters` runs **Then** the opinion value for each displayed character (clamped effective opinion toward the player org) is included in `CharacterStateEntry` so the UI can render it without querying ECS directly.

## Out of Scope

- Opinion of org characters (master/agent) toward other orgs — this feature covers country characters only.
- Non-cheat gameplay mechanics that raise or lower opinion (actions, events, control).
- Separate per-org opinion tracking — only opinion toward a single org (the player org) is surfaced in the UI; the data model may store it keyed by org but the UI only renders the player org value.
- Persisting modifier `SourceId` strings in a config file — `"cheat_improve_opinion"` is a hardcoded constant.
- Showing opinion in the org character panel (`OrgCharactersView`) — only the country character card (`CharactersView`) is changed.
- Tooltip or hover explanation for the opinion counter.

## Decisions

- Opinion is stored **per-org** (`Dictionary<string, ...>` keyed by org ID) for future-proofing; UI only surfaces the player org value.
- "Improve Opinion" cheat button goes in the **existing `character-debug-container` row** alongside Next/Drop.
- `OpinionModifier` carries a `ChangeValue` field (`+1` or `-1`); decay applies `ChangeValue` each month and removes the modifier when it reaches `0` — negative modifiers decay upward toward 0 symmetrically.
