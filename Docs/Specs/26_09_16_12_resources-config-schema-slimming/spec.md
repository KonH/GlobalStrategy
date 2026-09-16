# Spec: Resource Config Schema Slimming

## Feature Intent

As a game designer maintaining the resource configuration, I want each resource entry — and each recurring effect attached to it — to carry only the data that is genuinely unique to it, with display name, description, and icon derived from its own identifier by a fixed naming convention, so that adding or renaming a resource takes one entry instead of several parallel bookkeeping fields that can silently drift out of sync.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition.

- A resource is defined in the resource configuration
  - designer inspects the entry => the entry contains no separate fields naming the resource's display text or its icon; those are derived from the resource identifier
  - designer inspects the entry => the entry still carries the resource's behavioural data (where it is seeded, its starting value, its bounds, whether its history is recorded, and its default recurring effects)
  - designer writes an entry in the old shape with display-text or icon fields => those fields are simply ignored; there is no compatibility path, no per-entry override, and no dedicated validation machinery to detect them

- A recurring effect is defined alongside a resource
  - designer inspects the effect => it carries no display-text fields; its name and description are derived from the effect identifier by the same convention used for resources
  - player opens the resource breakdown tooltip => each contributing effect shows its derived translated name and description
  - player views the war progress screen => each listed effect shows its derived translated name and description
  - an effect has no translated text => the raw effect identifier is shown in its place; display is never blocked

- A resource that is shown to the player and has translated text and artwork available
  - player views the resource display => the resource shows the same name, description, and icon it showed before this change
  - player hovers the resource => the tooltip shows the same name and description text it showed before this change
  - player views a task reward that references this resource => the reward shows the resource's translated display name, as before

- A resource that is shown to the player but is missing translated text or artwork
  - resource has no translated text => the raw internal identifier is shown in its place, exactly as today; display is never blocked
  - resource has no matching artwork => the icon renders blank in a correctly-sized slot rather than as a broken or missing image
  - developer builds the project => the missing text and the missing artwork are each reported once, as a build-time failure naming the gap, never as a repeated warning during play

- A resource that is never shown to the player (internal bookkeeping values such as war progress, war initiative, combat bonus percentages, character skills, and the combat damage and durability values)
  - developer starts the game => no missing-text or missing-artwork report is produced for it, because the report is scoped to the resources the game already lists as player-visible
  - designer inspects its entry => the entry needs no extra bookkeeping beyond its behavioural data, and no marker declaring it hidden
  - designer inspects the translated text catalogue => text entries that existed only for these now-never-displayed resources are removed, since nothing reads them

- A resource icon is shown anywhere in the game (the live resource display, the developer component gallery, and the war result summary)
  - developer compares the currency resource between the live display and the component gallery => both show the same icon, because both derive it the same way; the pre-change disagreement between them is gone by construction
  - developer audits every place an icon is referenced, including places that pick an icon directly rather than through the configuration => all of them follow the new naming convention and none is left pointing at an old name
  - designer replaces a resource's artwork => only the artwork asset changes; no configuration entry needs editing

- A resource identifier contains more than one word
  - the display layer derives its icon name => the identifier is used verbatim, with no case or separator transformation of any kind; the identifier's own spelling is the single spelling used for its text keys, its icon name, and its artwork file
  - the presentation layer's own naming style differs from the identifier's => the identifier still wins; this is a deliberate, documented exception so that no rule has to be applied when moving between the two
  - existing artwork that does not already match the identifier => it is renamed outright in this change, with every reference updated; no duplicate under the old name is kept

- The province-scoped population resource is displayed
  - player views it => it shows its own translated name and description, and its own artwork, rather than borrowing the country-level population resource's
  - developer checks the translated text catalogue => the new entries exist in both supported languages with a real translation, even though the English text duplicates the country-level wording

- A designer adds a brand-new resource
  - designer adds the entry and supplies translated text and artwork under the naming convention => the resource displays correctly with no further configuration
  - designer adds the entry without supplying text or artwork => the game still runs and the resource behaves correctly; the name falls back to the raw identifier, the icon renders blank, and both gaps are named by a failing build-time check

## Success Criteria

Measurable, technology-agnostic outcomes.

- A resource entry in the configuration has at most the behavioural fields plus its identifier; no entry restates display text or artwork naming, and neither does any effect attached to it.
- Every resource and effect currently visible to the player shows the same name, description, and icon before and after the change — verified across the resource display, tooltips, the resource breakdown, task rewards, the war progress screen, the war result screen, and the component gallery.
- The currency resource shows the same icon in the live display and in the component gallery, where previously they differed.
- No icon reference anywhere in the game still uses a pre-change name, including references that select an icon directly instead of through the configuration.
- Renaming or replacing a resource's artwork requires changing exactly one thing (the artwork asset) and zero configuration edits.
- A player-visible resource added without text or artwork is caught by a check that runs on every build, naming the specific gap, and no repeated per-frame warnings are introduced by this change.
- Every newly added translated text entry exists in both supported languages with a real translation, not a copy of the English text.
- No translated text entry remains in the catalogue that nothing reads.

## Icon Unification Options

Three options were considered for unifying how resource icons are presented. All three produce the same end result for the player; they differ in how much artwork has to exist before a resource can be displayed.

**Decided — Option B: one icon per resource, named after the resource identifier, with a graceful fallback.**
Each resource's icon is found by its identifier alone, spelled exactly as the identifier is spelled — no case or separator transformation, even where the presentation layer's own naming style differs. A resource with no matching artwork renders a blank, correctly-sized icon slot rather than a broken image, and the gap is caught once by a check that runs on every build. The check is scoped by the game's existing list of player-visible resources, so internal resources cost nothing and never appear in it. This gives the trustworthy report that Option C aimed for without adding any field back into the configuration. Where two resources legitimately share a picture, the artwork is duplicated under each derived name.

**Rejected — Option A: artwork required for every displayed resource, no fallback.** Rejected because it makes a missing artwork asset a hard blocker on adding or testing a new resource, with no benefit over B once the build-time coverage check exists.

**Rejected — Option C: Option B plus an explicit per-resource "never displayed" marker.** Rejected because the existing player-visible resource list already expresses exactly what the marker would, and adding the field would reintroduce presentation data into the configuration this feature exists to slim down.

## Out of Scope

- Changing which resources are visible to the player, or the order they appear in.
- Changing resource or effect behaviour: seeding, starting values, bounds, history recording, effect values, pay cadence, or how effects are computed.
- Redesigning the visual style of the resource display, tooltips, or chips.
- Creating new artwork for resources that have none today — including the combat damage and durability values, which are not player-visible and are deliberately left without artwork.
- Supporting configurations written in the pre-change shape; the configuration ships with the game and is edited in-repo, so this is a clean break.
- Any change to other configuration files that happen to share a similar shape.
