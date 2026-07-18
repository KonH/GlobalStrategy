# Spec: Action Log UI

## Feature Intent

As a player, I want a persistent, scrolling log of important game-logic events (discoveries, control gains, opinion gains, new character appointments) visible on the HUD at all times, so that I can follow the consequences of my own and rival organizations' actions without having to hunt for them across country/org panels.

## Research Note: Judgment Call on Action Coverage

The user asked for exactly four log line formats and explicitly asked to double-check whether other currently-implemented important actions deserve a line too. Investigation of the current game-logic systems (`src/Game.Systems/`, `src/Game.Main/GameLogic.cs`, `Assets/Configs/action_config.json`, `Assets/Configs/effect_config.json`) found:

- **Discovery** (`DiscoverCountrySystem`) — fully live today via real (non-debug) gameplay, for both player and bot orgs.
- **Control** (`CreateActionEffectSystem` → `ControlEffect`, aggregated into `CountryControlState` totals by `ControlSystem`) — fully live today via real card actions (e.g. `letter_commendation_control`, `royal_audience_control`). Both the per-action delta and the resulting running total per org per country are derivable at the point the effect is applied.
- **Opinion** (`CreateActionEffectSystem` → `OpinionModifierEffectParams` → `ResourceChange`/`CharacterStateEntry.Opinion`) — also fully live today via real card actions (e.g. `letter_commendation_opinion`, `royal_audience_opinion`), contrary to a first-pass assumption that only the debug "Improve Opinion" cheat touches opinion. Both the per-action delta and the character's resulting effective opinion value are derivable at the point the effect is applied.
- **New character in role** (`Character`/`CharacterSlot` creation) — genuinely has no non-debug trigger today; the only code paths that create a new role-holding `Character` are `ApplyDebugCycleCharacter`/`ApplyDebugDropCharacter` (both debug-only commands) plus one-time `InitSystem` seeding at game start.

Beyond the four requested formats, other implemented systems were checked and deliberately excluded (see Out of Scope): resource/gold income (`ResourceSystem`) changes every tick and would flood the log; `CountryScoreSystem`/`OrgScore` recompute continuously rather than firing discrete events; `ProvinceOwnershipSystem` currently only changes via a debug cheat (`DebugChangeProvinceOwnerCommand`), mirroring the same "debug-only" situation as character roles and not requested by the user.

**Decision:** all four requested formats stay in scope, driven by a uniform mechanism — the log observes the underlying state (discovery set, control totals, opinion totals, role-slot occupants) and emits a line whenever that state changes in the "increased/appeared" direction, regardless of whether the change was produced by a full gameplay system or (for character roles today) a debug command. This keeps the log correct today and requires no additional wiring later when a real character-appointment system ships — it will "just work" through the same observation mechanism.

The fourth format's `<Country> / <Org>` notation is contextual, not a literal two-slot line: exactly one of the two renders, chosen by which kind of role the character occupies — `<Country>` for a country-government role (`CountryId` set, `OrgId` empty), `<Org>` for an org role (`OrgId` set, `CountryId` empty). This resolves cleanly against the data model (see Line formats below); there is no remaining structural gap.

## Acceptance Criteria

### Line formats

- **Given** an organization discovers a country for the first time **When** the action log next refreshes **Then** a new line reading `<OrganizationDisplayName> discovered <CountryDisplayName>` appears at the bottom of the log.
- **Given** an organization's aggregate control total in a country increases **When** the action log next refreshes **Then** a new line reading `<OrganizationDisplayName> increased control in <CountryDisplayName> by +<DELTA> (<TOTAL>)` appears, where `<DELTA>` is the amount just added and `<TOTAL>` is the organization's new resulting control total in that country, both formatted per the number-formatting rule below.
- **Given** a character's effective opinion of an organization increases **When** the action log next refreshes **Then** a new line reading `<OrganizationDisplayName> increased <CharacterRoleDisplayName> <CharacterDisplayName> opinion in <CountryDisplayName> by +<DELTA> (<TOTAL>)` appears, where `<CountryDisplayName>` is the character's home country, `<CharacterRoleDisplayName>` is the character's role, `<DELTA>` is the amount just added, and `<TOTAL>` is the character's new resulting opinion value for that organization, both formatted per the number-formatting rule below.
- **Given** a new character becomes the occupant of a country-government role slot (`CountryId` set, `OrgId` empty — e.g. Ruler, General) **When** the action log next refreshes **Then** a new line reading `New <CharacterRoleDisplayName> in <CountryDisplayName> - <CharacterDisplayName>` appears (the org segment is omitted entirely — no slash, no placeholder — since no organization is involved).
- **Given** a new character becomes the occupant of an org role slot (`OrgId` set, `CountryId` empty — e.g. Master, Agent) **When** the action log next refreshes **Then** a new line reading `New <CharacterRoleDisplayName> in <OrganizationDisplayName> - <CharacterDisplayName>` appears (the country segment is omitted entirely — no slash, no placeholder — since no country is involved).
- **Given** the underlying triggering condition for a line format does not currently occur outside a debug command (true today only for the fourth format) **When** that debug command fires during play or QA **Then** the corresponding log line still appears, since the log observes state changes rather than the specific command that caused them.

### Number formatting

- **Given** a log line contains a numeric value (a delta or a resulting total; a Control/Opinion line contains both) **When** it is rendered **Then** the value is formatted with exactly one digit after the decimal point (e.g. `+3.5`, `35.0`), never as a bare integer (`+3`) and never with more than one decimal digit (`+3.50`), matching the project's existing `:F1`-style convention for fractional game values.

### Name/role styling within a line

- **Given** a log line contains a country or organization display name **When** it is rendered **Then** that name is bold and colored using that entity's existing per-country/per-org "related font color" (the exact source property — e.g. the color already used elsewhere for country/org name text — is an implementation detail for `/plan`, not specified here).
- **Given** a log line contains a character role display name **When** it is rendered **Then** the role name is bold, in the default white text color (no special role color).
- **Given** a log line contains any other text (character display names, connector words like "discovered"/"increased"/"in"/"by") **When** it is rendered **Then** that text uses the panel's default style: white text with a black shadow/outline for legibility over the map, matching every other HUD line's default treatment. Character display names are not bolded or colored unless covered by another rule above. The parenthesized total in Control/Opinion lines is also unstyled default text (the styling rules apply only to names/roles, not to any numeric segment).

### Panel placement and sizing

- **Given** the HUD is displayed **When** the action log panel is rendered **Then** it shares the project's existing `HUDPanelSettings.asset` PanelSettings layer (per `.claude/rules/unity/uitoolkit.md`) rather than introducing a new PanelSettings asset, and uses a `sortingOrder` that keeps it below the fly-text layer (`sortingOrder < 1000`) while remaining visible alongside other HUD panels — it is not a modal and does not block map/world clicks.
- **Given** the top-right UI block (`.top-right-panel`, holding time/speed controls) renders at its current height **When** the action log panel is positioned **Then** the panel's top edge sits immediately below the top-right block's actual rendered bottom edge (tracking its live height, not a fixed pixel offset — the top-right block has no fixed height in the existing USS), with a small consistent gap matching other HUD panel spacing.
- **Given** the bottom-bar panel (`.country-info-panel`, `.org-lens-country-info-panel`, or the org-lens equivalent) toggles between visible and `display: none` as selection changes **When** the action log panel is positioned **Then** its bottom edge is anchored at the fixed offset where that bottom-bar panel's top edge would sit *if it were currently shown* — this offset does not change when the bottom-bar panel opens or closes, so the action log panel never shifts position as a result of selecting/deselecting a country or org. The exact reserved offset value (e.g. a fixed pixel value representative of the bottom-bar panel's typical rendered height) is an implementation detail for `/plan`.
- **Given** the top-right block's rendered width is `W` **When** the action log panel is sized **Then** its width is `1.5 × W`, its right edge is anchored at `right: 6px` (flush with `.top-right-panel`'s own right offset, for visual alignment), and its left edge is therefore positioned at `(right screen edge − 6px) − 1.5W`.
- **Given** the action log panel contains more entries than fit in its available vertical space **When** it is rendered **Then** content is bottom-aligned within the panel: the newest entry sits at the bottom of the visible block, the log grows upward as entries accumulate, and the oldest entries scroll off the top (no longer visible) rather than pushing the newest entry out of view.
- **Given** a log line's text is wider than the panel's fixed width **When** it is rendered **Then** the line wraps to multiple visual lines within that width — no truncation (e.g. ellipsis) and no horizontal scrolling.

### Entry animation

- **Given** a new log line is appended to the panel **When** it first appears **Then** it plays a short fade-in animation (from transparent to fully opaque) rather than appearing instantly, consistent with the panel's bottom-aligned/grows-upward layout — the new line settles at the bottom of the visible block as it fades in.
- **Given** the log has reached its configured maximum entry count (`gameLog.maxLogEntries`) **When** a new entry is appended **Then** the oldest visible entry is removed via a longer fade-out animation (longer duration than the new-entry fade-in) rather than disappearing instantly, so the eviction reads as a deliberate "aging out" rather than the new-entry animation's "arriving" beat.
- **Given** the fade-in and fade-out animations are not given exact timing by the user **When** implementing them **Then** use short/fast defaults consistent with the project's existing fly-text timing conventions (`Docs/Specs/42_fly-text-notifications/spec.md`) — e.g. fade-in noticeably shorter than fade-out — as implementation constants, not a behavioral contract; adjustable later without a spec change.

### `gameLog` config block

- **Given** the game settings config **When** it is loaded **Then** it contains a `gameLog` block (a new top-level settings section, alongside existing `GameSettings` properties) with two entries: `includePlayerActions` (bool) and `maxLogEntries` (int).
- **Given** `gameLog.includePlayerActions` is not explicitly set **When** the game loads settings **Then** it defaults to enabled (`true`), so player-organization entries appear in the log by default alongside AI/bot-organization entries.
- **Given** `gameLog.includePlayerActions` is set to `false` **When** an event is generated whose acting organization's ID matches the current player's organization ID **Then** that entry is suppressed from the log (not queued, not shown even briefly).
- **Given** `gameLog.includePlayerActions` is set to `false` **When** an event is generated whose acting organization is any AI/bot organization (i.e. its org ID does not match the player's org ID) **Then** that entry appears in the log unaffected by the flag.
- **Given** the fourth line format's country-role variant (a character with no owning organization, e.g. a country's Ruler appointed via country-pool cycling rather than an org action) **When** the includePlayerActions flag is evaluated **Then** it is treated as a non-player (unaffected) entry, since there is no acting organization to compare against the player's org ID.
- **Given** `gameLog.maxLogEntries` defines the maximum number of entries the log retains **When** appending a new entry would exceed that cap **Then** the oldest entry is dropped (per the fade-out animation above) so the log never holds more than `maxLogEntries` entries at once. The default value is `12`.

### Log-type-proposal skill (process artifact)

- **Given** a contributor or agent later adds a new important game-logic action that should be reflected in the action log **When** they invoke the log-type-proposal skill/command (following this repo's `.claude/commands/*.md` convention) **Then** it walks them through defining the new log line format, the locale keys it needs, and its wiring point (which existing state/event it should observe), consistent with the conventions established by this spec — without requiring them to re-derive those conventions from scratch.
- **Given** the log-type-proposal skill is invoked **When** it completes **Then** it produces a written definition of the new log line type (format string, styling rules, triggering condition) suitable for a human or `/plan` to act on next — it is a documentation/definition aid, not a code generator, and does not itself implement the new log line.

## Out of Scope

- **Resource/gold income, country score, and org score changes** as log line types — these recompute continuously/every tick rather than firing as discrete named events, and would flood the log; not requested by the user.
- **Province ownership changes** as a log line type — today only changes via a debug cheat (`DebugChangeProvinceOwnerCommand`), matches no format the user specified, and is left for a future spec if ever made a real gameplay action.
- **Log persistence across save/load.** The action log's entry history is not written to the save file and is not restored on load — it is presentation-only state, following the same "not `[Savable]`, rebuilt/derived rather than persisted" convention already used elsewhere in `VisualState` (e.g. `DiscoveredCountriesState`). On load, the log starts empty for that session and must not replay any historical event from the save as a flood of log lines the moment it is loaded — the exact mechanism that guarantees this is an implementation detail for `/plan`.
- **Log entry interactivity** — clicking a log entry to navigate to the relevant country/org/character panel is not part of this feature.
- **Sound effects** accompanying new log entries.
- **Notification/toast behavior** — this is explicitly not the fly-text system (`Docs/Specs/42_fly-text-notifications/`); the action log never disappears on its own and does not use the fly-text queue or its `sortingOrder: 1000+` layer.
- **Historical/off-screen entry search or filtering** (e.g. filtering the log by org or action type) — the log is a simple chronological scroll-off list only.
- **`gameLog` config entries for anything other than `includePlayerActions` and `maxLogEntries`** — this spec introduces the `gameLog` block itself (previously absent from `GameSettings`) but scopes its content to only these two entries; other potential future flags are not designed here.

