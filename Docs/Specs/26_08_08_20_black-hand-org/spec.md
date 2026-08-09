# Spec: Black Hand Organization

## Feature Intent

As a player, I want a third selectable secret organization — **Black Hand** (`OrganizationId`: `BlackHand`), headquartered in Serbia — with its own starting gold, org visual/flag, localized display name, and a full master/agent character pool, so that org choice and multi-org bot play expand beyond Illuminati and Masons without new game systems.

**InitialGold decision:** `750.0` — below both existing orgs (Illuminati `1000.0`, Masons `1500.0`), keeping Black Hand as the leaner Serbian cell while remaining playable with the same `InitialAgentSlots: 3` pattern. Character portraits are part of feature completeness but ship in a deferred Stage 2 after the org is playable.

## Acceptance Criteria

### Stage 1 — Initial implementation (first `/implement` pass)

Org config, localization, org visual/flag, character pool + name keys, and full participation. Portraits may be missing; UI already leaves the portrait area empty when `FindPortrait` returns null (`CharactersView` / `OrgCharactersView`).

- **Given** `Assets/Configs/organizations.json` **When** Black Hand is added **Then** it has `OrganizationId` `BlackHand`, `DisplayName` `Black Hand`, `HqCountryId` `Serbia`, `InitialGold` `750.0`, and `InitialAgentSlots` `3` (same slot count as Illuminati/Masons).

- **Given** English and Russian localization assets **When** the org is shown in UI **Then** `organization_name.BlackHand` resolves to a proper display name in both locales (EN: "Black Hand"; RU via localization skill at implement time — real translation, not an English placeholder).

- **Given** `OrgVisualConfig.asset` and `Assets/Textures/Flags/Orgs/BlackHand.png` **When** Black Hand appears in SelectOrg / OrgInfo / related org UI **Then** it has a wired `OrgVisualEntry` (`orgId` `BlackHand`, distinct color, flag sprite) sourced via the flag-assets / `ORG_FLAGS` Wikimedia pipeline (`scripts/utils/download_flags.py`), using a suitable Black Hand or related historical emblem chosen at plan/implement time.

- **Given** `Assets/Configs/character_config.json` → `orgPools` **When** Black Hand is configured **Then** it matches the Illuminati/Masons pattern: 3 master candidates (charm ~65–95) and 6 agent candidates (intrigue ~48–90), with ids `blackhand_master_N` / `blackhand_agent_N`.

- **Given** the Black Hand character pool **When** characters are created **Then** they use these decided identities and name-part locale keys (new `character.name.part.*` keys in `en.asset` / `ru.asset` as needed):

  | characterId | namePartKeys | role / skills |
  |---|---|---|
  | `blackhand_master_1` | `character.name.part.dragutin`, `character.name.part.dimitrijevic` | master / charm ~70–95 |
  | `blackhand_master_2` | `character.name.part.vojislav`, `character.name.part.tankosic` | master / charm ~65–90 |
  | `blackhand_master_3` | `character.name.part.bogdan`, `character.name.part.radenkovic` | master / charm ~68–92 |
  | `blackhand_agent_1` | `character.name.part.gavrilo`, `character.name.part.princip` | agent / intrigue ~60–90 |
  | `blackhand_agent_2` | `character.name.part.nedeljko`, `character.name.part.cabrinovic` | agent / intrigue ~55–85 |
  | `blackhand_agent_3` | `character.name.part.trifko`, `character.name.part.grabez` | agent / intrigue ~50–80 |
  | `blackhand_agent_4` | `character.name.part.danilo`, `character.name.part.ilic` | agent / intrigue ~58–88 |
  | `blackhand_agent_5` | `character.name.part.veljko`, `character.name.part.cubrilovic` | agent / intrigue ~52–82 |
  | `blackhand_agent_6` | `character.name.part.muhamed`, `character.name.part.mehmedbasic` | agent / intrigue ~48–78 |

- **Given** CountrySelection / `SelectOrgLogic` (iterates orgs from `organizations.json`) **When** the player opens org selection **Then** Black Hand appears as a selectable org alongside Illuminati and Masons (no special-case SelectOrg code required beyond configs + visuals + locale).

- **Given** a Unity session where the player picks Black Hand **When** the Map session starts with all orgs from `organizations.json` participating **Then** Black Hand is the player org (HQ Serbia, gold 750) and Illuminati + Masons are `BotControlled` (existing N−1 bot generalization from `26_07_17_06_bot-opponent-unity`).

- **Given** a Unity session where the player picks Illuminati or Masons **When** the Map session starts with all three orgs participating **Then** Black Hand is `BotControlled` like any other non-player participating org — no bot UX redesign.

- **Given** Stage 1 ships without CharacterVisualConfig portrait entries for the nine `blackhand_*` ids **When** those characters are shown in CharactersView / OrgCharactersView **Then** the portrait area renders with an empty background (existing null-sprite behaviour) and names/roles still display correctly — acceptable for Stage 1.

### Stage 2 — Character portraits (deferred; required for feature completeness)

Not part of the first `/implement` pass. Gate Stage 1 first; run Stage 2 after Stage 1 lands (same issue/spec until portraits are done). Stage 1 alone does not close the feature as complete.

- **Given** the nine Black Hand `characterId`s from Stage 1 **When** Stage 2 runs **Then** portraits are generated via the project image-generation / ComfyUI pipeline (`image-generation` skill / `scripts/utils/generate_image.py` or batch equivalent) with prompts appropriate to early-20th-century Serbian nationalist-revolutionary / Balkan milieu.

- **Given** generated portrait sprites **When** wired into `CharacterVisualConfig.asset` **Then** each `blackhand_master_N` and `blackhand_agent_N` has a portrait entry mapping `characterId` → sprite, matching how existing Illuminati/Masons org characters are wired.

- **Given** Stage 2 is complete **When** Black Hand characters appear in CharactersView / OrgCharactersView **Then** each shows its generated portrait (no empty portrait area for those ids).

## Out of Scope

- New game mechanics, actions, cards, goals, or org-specific rules beyond config-driven participation
- Rebalancing Illuminati or Masons `InitialGold` / slots / pools
- Bot AI redesign, bot UI indicators, or changes to `BotControlled` selection logic beyond confirming Black Hand participates in the existing N−1 pattern
- New country or province content (Serbia already exists)
- Redesign of SelectOrg / CountrySelection UX
- Portrait generation in Stage 1
- Changing skill-range conventions for existing orgs
