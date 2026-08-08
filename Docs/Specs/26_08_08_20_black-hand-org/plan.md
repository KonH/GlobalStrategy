# Plan: Black Hand Organization

## Spec

As a player, I want a third selectable secret organization — **Black Hand** (`OrganizationId`: `BlackHand`), headquartered in Serbia — with its own starting gold, org visual/flag, localized display name, and a full master/agent character pool, so that org choice and multi-org bot play expand beyond Illuminati and Masons without new game systems.

**InitialGold:** `750.0` (below Illuminati `1000.0` / Masons `1500.0`); `InitialAgentSlots: 3`. Portraits are Stage 2 (deferred); Stage 1 may leave portrait areas empty via existing null-sprite UI behaviour.

Acceptance criteria (Stage 1 — first `/implement` pass):
- `organizations.json`: `BlackHand`, DisplayName `Black Hand`, `HqCountryId` `Serbia`, `InitialGold` `750.0`, `InitialAgentSlots` `3`.
- Locale: `organization_name.BlackHand` EN "Black Hand" + real RU (localization skill); all new `character.name.part.*` keys from the name table below.
- `OrgVisualConfig` + `Assets/Textures/Flags/Orgs/BlackHand.png`: wired `OrgVisualEntry` with distinct color and flag via `ORG_FLAGS` / flag-assets pipeline.
- `character_config.json` → `orgPools`: 3 masters + 6 agents mirroring Illuminati skill ranges; ids `blackhand_master_N` / `blackhand_agent_N` with decided Serbian name-part keys.
- SelectOrg lists Black Hand from config (no special-case code). Player-as-BlackHand ⇒ Illuminati+Masons `BotControlled`; player-as-Illuminati/Masons ⇒ Black Hand `BotControlled` (existing N−1 pattern). No bot UX redesign.
- Missing `CharacterVisualConfig` portraits for `blackhand_*` is acceptable in Stage 1 (empty portrait background).

Acceptance criteria (Stage 2 — deferred; same issue/spec; required for feature completeness):
- ComfyUI portraits for all nine `blackhand_*` ids → `CharacterVisualConfig` wiring → portraits show in CharactersView / OrgCharactersView.

Out of scope: new mechanics; rebalancing Illuminati/Masons; bot AI/UI redesign; SelectOrg UX redesign; Stage 1 portraits; changing existing org skill-range conventions; full Serbia country character pool / country-content expansion beyond the HQ prerequisites below.

## Goal

Ship Black Hand as a fully selectable third org (config, locale, flag/visual, character pool, N−1 bots) in Stage 1 without portraits; defer ComfyUI portraits + `CharacterVisualConfig` to Stage 2 after Stage 1 lands. Make Serbia a live HQ country (`isAvailable: true` + country flag) so InitSystem / map / scoring treat the HQ like Illuminati/Masons HQs.

## Approach

Config-driven only — no new ECS systems, UI Toolkit screens, or bot features. Mirror Illuminati/Masons patterns.

### Stage 1 (first implement)

1. **Enable Serbia HQ country (blocking)** — Serbia exists in `country_config.json` / provinces but `"isAvailable": false`. `InitSystem` skips unavailable countries (no `Country` entity, no `country_score`, map provinces for that owner stay hidden). Stage 1 **must** set Serbia `"isAvailable": true` so HQ Serbia works. Assumed Stage 1 friends/rivals (editable on owner request): `historicalFriends: ["Russian_Empire"]`, `historicalRivals: ["Austria_Hungary", "Ottoman_Empire"]`. **Assumed Stage 1 default:** no Serbia `countryPools` entry (empty HQ country characters / advisor skills 0) — org masters/agents still work; full Serbia country pool is deferred / out of scope unless owner overrides.

2. **Serbia country flag (required once available)** — Serbia’s `CountryVisualConfig` entry currently has `flag: {fileID: 0}` and is absent from `COUNTRY_FLAGS`. Add Serbia to `COUNTRY_FLAGS` in `scripts/utils/download_flags.py` (primary: `File:Flag_of_Serbia_(1882-1918).svg`; fallback: `File:Flag_of_Serbia.svg`), download to `Assets/Textures/Flags/Countries/Serbia.png`, import as Sprite, wire `CountryVisualConfig` Serbia `flag` (flag-assets country path).

3. **Org config** — Append to `Assets/Configs/organizations.json`:
   ```json
   {
     "OrganizationId": "BlackHand",
     "DisplayName": "Black Hand",
     "HqCountryId": "Serbia",
     "InitialGold": 750.0,
     "InitialAgentSlots": 3
   }
   ```

4. **Character pool** — Append a third `orgPools` entry in `Assets/Configs/character_config.json` copying Illuminati skill ranges exactly (not Masons’ slightly different master_1 charm). JSON `namePartKeys` must be the **full** `character.name.part.*` strings (same as Illuminati):

   | characterId | namePartKeys | skills |
   |---|---|---|
   | `blackhand_master_1` | `character.name.part.dragutin`, `character.name.part.dimitrijevic` | charm 70–95 |
   | `blackhand_master_2` | `character.name.part.vojislav`, `character.name.part.tankosic` | charm 65–90 |
   | `blackhand_master_3` | `character.name.part.bogdan`, `character.name.part.radenkovic` | charm 68–92 |
   | `blackhand_agent_1` | `character.name.part.gavrilo`, `character.name.part.princip` | intrigue 60–90 |
   | `blackhand_agent_2` | `character.name.part.nedeljko`, `character.name.part.cabrinovic` | intrigue 55–85 |
   | `blackhand_agent_3` | `character.name.part.trifko`, `character.name.part.grabez` | intrigue 50–80 |
   | `blackhand_agent_4` | `character.name.part.danilo`, `character.name.part.ilic` | intrigue 58–88 |
   | `blackhand_agent_5` | `character.name.part.veljko`, `character.name.part.cubrilovic` | intrigue 52–82 |
   | `blackhand_agent_6` | `character.name.part.muhamed`, `character.name.part.mehmedbasic` | intrigue 48–78 |

   JSON shape matches Illuminati (`orgId`, `slots.master` / `slots.agent`).

5. **Localization** — Add to `Assets/Localization/en.asset` then real RU via **localization** skill (no English placeholders in `ru.asset`):
   - `organization_name.BlackHand` → EN `Black Hand`
   - Name parts (EN): Dragutin, Dimitrijević, Vojislav, Tankosić, Bogdan, Radenković, Gavrilo, Princip, Nedeljko, Čabrinović, Trifko, Grabež, Danilo, Ilić, Veljko, Čubrilović, Muhamed, Mehmedbašić

6. **Org emblem (locked)** — Primary Wikimedia file: **`File:Black_Hand,_logo.png`** (PNG; Unification or Death emblem). Add `"BlackHand": "File:Black_Hand,_logo.png"` to `ORG_FLAGS` in `scripts/utils/download_flags.py`. Before download, run `check_flags.py`. **Do not** point `ORG_FLAGS` at `File:Seal_of_the_Black_Hand.jpg` — `download_flags.py` requires PNG magic bytes and org downloads have no `ORG_FLAGS_FALLBACK` loop today. Contingency if primary fails: download the seal JPG separately and convert to PNG (Pillow / local convert) into `Assets/Textures/Flags/Orgs/BlackHand.png`, or pick another Commons PNG. Optionally wire `ORG_FLAGS_FALLBACK` later (countries already support fallbacks; orgs do not).

7. **Org visual** — Color (locked, contrasts Illuminati gold `{0.9, 0.75, 0.1}` and Masons blue `{0.1, 0.2, 0.55}`): **`{r: 0.45, g: 0.06, b: 0.08, a: 1}`** (dark crimson). Append `OrgVisualEntry` (`orgId: BlackHand`) to `Assets/Configs/OrgVisualConfig.asset` with that color + flag sprite GUID. Prefer Unity MCP (`manage_texture` as_sprite + `manage_scriptable_object`); else YAML edit + User Steps.

8. **Hardcoded 2-org sweep** — Grep production code for org-count `== 2` / Illuminati+Masons-only assumptions. Known safe: `GameLifetimeScope` / `GameSession` use `Organizations.Count >= 2` and iterate all config orgs. Fix only if a true hardcode blocks a third org. Do **not** redesign bot UX.

9. **Stage 1 skips** — No `CharacterVisualConfig` / portrait PNGs for `blackhand_*`. No Serbia `countryPools` unless owner overrides assumed default.

### Stage 2 (deferred — after Stage 1 approval + land)

1. Generate nine 512×512 portraits via **image-generation** skill → `Assets/Textures/Characters/PortraitCard/{characterId}.png`. Use the skill portrait recipe (bust, dark background, historical oil painting, formal attire) but **replace** the template’s hardcoded `19th century` with **`early 20th century (c. 1910), Serbian nationalist-revolutionary / Balkan`**. Do not paste the skill’s century line unchanged.
2. Import as sprites (MCP) and append nine entries to `Assets/Configs/CharacterVisualConfig.asset` matching `illuminati_*` / `masons_*` pattern.
3. Confirm CharactersView / OrgCharactersView show portraits (no empty area for those ids).

## Agent Steps

### Stage 1

- [x] **Enable Serbia in `country_config.json`** — **Deferred to #147** (owner: "Serbia will be added in #147"). Not done in this PR; HQ `Serbia` remains on BlackHand config and depends on #147 / PR #148 landing.
- [x] **Map + download Serbia country flag** — **Deferred to #147** (same as above).
- [x] **Wire Serbia `CountryVisualConfig` flag** — **Deferred to #147** (same as above).
- [x] **Add BlackHand to `organizations.json`** — Append org entry with `OrganizationId` `BlackHand`, `DisplayName` `Black Hand`, `HqCountryId` `Serbia`, `InitialGold` `750.0`, `InitialAgentSlots` `3` in `Assets/Configs/organizations.json`.
- [x] **Add BlackHand `orgPools` block** — In `Assets/Configs/character_config.json`, append Illuminati-mirrored master×3 / agent×6 pool with the nine `blackhand_*` ids and **full** `character.name.part.*` keys from Approach.
- [x] **Add EN locale keys** — In `Assets/Localization/en.asset`, add `organization_name.BlackHand` and the 18 new `character.name.part.*` keys with the EN values listed in Approach.
- [x] **Add real RU locale keys** — Follow `.claude/skills/localization/SKILL.md`: Haiku subagent translates the new EN keys; write results to `Assets/Localization/ru.asset` (no English placeholders).
- [x] **Map + download org emblem** — Add `BlackHand` → `File:Black_Hand,_logo.png` in `ORG_FLAGS` (`scripts/utils/download_flags.py`). Run `check_flags.py` then `download_flags.py` from project root (`.venv` Python). On primary failure, convert a PNG-capable source into `Assets/Textures/Flags/Orgs/BlackHand.png` (do not leave a JPG as the org asset). Confirm valid PNG header.
- [x] **Wire `OrgVisualConfig` + sprite** — Import `Textures/Flags/Orgs/BlackHand.png` as Sprite (Unity MCP `manage_texture` when Editor available). Append entry `orgId: BlackHand`, color `{r: 0.45, g: 0.06, b: 0.08, a: 1}`, flag GUID. If MCP unavailable, edit asset YAML with placeholder and leave sprite assignment to User Steps.
- [x] **Verify no blocking 2-org hardcodes** — Grep `Assets/Scripts/` and `src/` for assumptions that break a third org; fix only blockers. Confirm SelectOrg / `GameLifetimeScope` participation still iterates all `organizations.json` entries (`Count >= 2`).
- [x] **Add minimal BlackHand / Serbia presence tests** — Extend `StringConfigParityTests` with BlackHand org + pool presence (HQ Serbia, gold 750, 3 masters / 6 agents). Serbia `IsAvailable` assertion omitted here — owned by #147.
- [x] **Run tests + Release build if `src/` changed** — `dotnet-test` for `Game.Tests`. If any `src/` edit, also `/dotnet-build Release` per workflow.

### Stage 2 (deferred — do not run in first implement pass)

- [ ] **Generate nine Black Hand portraits** — Via image-generation / ComfyUI (`generate_images_batch.py` or per-id `generate_image.py`) to `Assets/Textures/Characters/PortraitCard/blackhand_{master,agent}_N.png` at 512×512. Override the skill template century to early-20th-century Serbian revolutionary / Balkan wording.
- [ ] **Wire `CharacterVisualConfig`** — Import portrait sprites; add nine `characterId` → sprite entries in `Assets/Configs/CharacterVisualConfig.asset` beside existing `illuminati_*` / `masons_*` rows.
- [ ] **Confirm portrait display path** — After Unity refresh, ensure FindPortrait resolves for all nine ids (User Steps if visual check needed).

## User Steps

### 1. Country + org flag sprites / visual configs (if Unity MCP unavailable)

If the agent could not import PNGs as Sprites or patch ScriptableObjects via MCP: in Unity Editor, set Texture Type = Sprite on `Assets/Textures/Flags/Countries/Serbia.png` and `Assets/Textures/Flags/Orgs/BlackHand.png`, assign Serbia’s flag on `CountryVisualConfig`, and assign BlackHand’s flag on `OrgVisualConfig` (color already `{0.45, 0.06, 0.08, 1}` if YAML was edited). Save assets.

### 2. Visual check — SelectOrg / session (Stage 1)

In the Editor: open CountrySelection, confirm Black Hand appears with flag, crimson tint, and localized name. Confirm Serbia appears as a live country with a flag on the map. Start a Map session as Black Hand (HQ Serbia provinces visible, gold 750; Illuminati+Masons bots) and once as Illuminati or Masons (Black Hand bot). Confirm CharactersView / OrgCharactersView show Black Hand names with empty portrait backgrounds. Expect empty Serbia country character slots under the assumed Stage 1 default.

### 3. Wikimedia download contingency

If the automation host cannot reach Commons, run from a networked machine at repo root:

```
.venv/bin/python scripts/utils/check_flags.py "File:Flag_of_Serbia_(1882-1918).svg"
.venv/bin/python scripts/utils/check_flags.py "File:Black_Hand,_logo.png"
.venv/bin/python scripts/utils/download_flags.py
```

(Windows: `.venv\Scripts\python.exe` …). Commit `Assets/Textures/Flags/Countries/Serbia.png`, `Assets/Textures/Flags/Orgs/BlackHand.png` (and `.meta` if generated).

### 4. Stage 2 portraits — ComfyUI + CharacterVisualConfig (after Stage 1 lands)

When Stage 2 is authorized: ensure ComfyUI is at `http://127.0.0.1:8188`, run the batch generator with the early-20th-century prompt override, then in Editor (or MCP) import `PortraitCard/blackhand_*.png` as sprites and assign in `CharacterVisualConfig`. Visually confirm all nine portraits appear in CharactersView / OrgCharactersView.

### 5. Stage 2 ComfyUI contingency

If ComfyUI is unavailable on the agent host, generate portraits on a machine with the project ComfyUI+FLUX setup (see image-generation skill), copy PNGs into `Assets/Textures/Characters/PortraitCard/`, then perform User Step 4 wiring/inspection.

## Tests

Minimal config presence — no new simulation systems.

1. **`src/Game.Tests/StringConfigParityTests.cs`** (preferred home):
   - Keep existing `organization_config_parity` / `character_config_parity`.
   - Add a fact (or extend organization/character parity) that loads repo `organizations.json` and asserts BlackHand exists with `HqCountryId == "Serbia"`, `InitialGold == 750.0`, `InitialAgentSlots == 3`.
   - Assert `country_config.json` Serbia entry has `IsAvailable == true` (HQ prerequisite).
   - Assert `character_config.json`’s `FindOrgPool("BlackHand")` is non-null with `Slots["master"].Count == 3` and `Slots["agent"].Count == 6` (and optionally first master id `blackhand_master_1`).
2. **Hardcode sweep** — Document during implement: production paths already use `Count >= 2` + full org list; no test asserting org count == 2 in production config loaders. Fixture tests that hardcode Illuminati+Masons only are fine as synthetic data.
3. **Run** — `dotnet-test` skill on the solution containing `Game.Tests`. Stage 2 needs no new automated tests beyond existing visual/config patterns unless portrait wiring gains a cheap GUID-presence check (optional; skip if awkward).

## Constitution Check

- **Rendering (URP only):** Flag/portrait textures only; no RP/shader changes. No conflict.
- **Game Logic (ECS in `src/`, no state in MonoBehaviours):** Participation stays config → existing Init/BotSession; no MonoBehaviour domain rules. No conflict.
- **Dependency Injection (VContainer sole DI):** No new services or static singletons. No conflict.
- **UI (UI Toolkit only):** Existing SelectOrg / character views; no Canvas/uGUI. No conflict.
- **Planning Discipline:** This plan precedes implement. No conflict.
- **Specification Discipline:** Approved `spec.md` in this folder; clarifications locked yes. No conflict.
- **File Organisation:** Plan at `Docs/Specs/26_08_08_20_black-hand-org/plan.md`. No conflict.
- **Assembly Structure:** No new asmdefs. No conflict.
- **C# Code Style:** Any test/`src` edits follow project conventions. No conflict.

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
