# Plan

## Spec faithful summary

Make Serbia, Bulgaria, Bosnia_Herzegovina, Montenegro, Romania, and Greece playable, spawned countries — visible/colored on the map, ownable, and populated with their own characters — matching the 20 countries already available. All 6 already exist end-to-end in the data pipeline (map feature, `CountryEntry`, provinces, `CountryVisualConfig` color slot, EN/RU `country_name.*`/`province_name.*` locale) except that `CountryEntry.IsAvailable` is `false`, `character_config.json` has no `countryPools` entry for them, `Docs/Characters/character_roster.md` has no narrative section for them, and no flag PNG/Sprite/GUID exists. No C#/engine code changes are needed — `InitSystem`, `VisualStateConverter`, `CountryVisualConfig`, and `ProvinceRenderer`'s gating already generically support any country whose config flips to available.

**Acceptance criteria:**
- New game session: all 6 spawn as `Country` entities with unchanged `initialResources`; their provinces render fill/border in all 4 map lenses (Province, Political, Org, Geographic); each has seeded `historicalFriends`/`historicalRivals` per the confirmed mapping.
- Country screen: each of the 6 shows exactly 3 named characters per role (15 total: `ruler`, `military_advisor`, `diplomacy_advisor`, `economic_advisor`, `secret_advisor`), all resolving via locale with no "key not found" warnings, skill ranges matching the existing per-role convention.
- The `.claude/rules/config_validation.md` pools-vs-`isAvailable` cross-check reports zero mismatches.
- Flags: either the full pipeline completes, or the script-side `COUNTRY_FLAGS` entries exist and the Unity-side import/wiring is called out as outstanding (this environment has no Unity MCP, so it will be the latter).
- Portraits: none generated (explicitly deferred); a documented recipe listing every new `characterId` exists for the follow-up pass.

**Confirmed ambiguity answers (all "assumed" defaults, no changes):**
- 0: populate `historicalFriends`/`historicalRivals` per the spec's proposed 1880s-Balkans mapping.
- 1: use real historical c.1880 figures for character names.
- 2: add `COUNTRY_FLAGS` entries and attempt the Wikimedia download now; defer only Unity-side Sprite import/GUID-wiring.
- 3: mirror the existing 3 Rulers/3 Generals/3 Barons/3 Secret Advisors structure for all 6 countries, using best-available real or plausible period figures for thin-material roles (Bosnia_Herzegovina's post-1878 Austro-Hungarian administration era, Montenegro's small-principality "Baron"/financier role).

## Goal

Flip the 6 countries to available with correct relations, give each a complete 15-character pool with real c.1880 figures and locale-backed names, add their narrative roster sections, add their flag download entries and run the download, and document the portrait-generation recipe for the deferred follow-up — leaving only Unity-Editor-only steps (Sprite import, GUID wiring, in-game visual verification, actual portrait generation) as explicit User Steps.

## Approach

This is pure data/config/asset-text content work, no `src/` changes. All target files are either JSON (`country_config.json`, `character_config.json`), plain-text ScriptableObject YAML (`en.asset`, `ru.asset`, `CountryVisualConfig.asset` — read-only reference here, no edit needed since flag GUID wiring is Unity-only), Markdown (`character_roster.md`), or a Python script (`download_flags.py`). Work proceeds country-by-country-but-batched-by-artifact so each artifact type is edited once across all 6 countries, in the fixed order **Serbia, Bulgaria, Bosnia_Herzegovina, Montenegro, Romania, Greece** (spec's listed order) for consistency across all files.

Character selection uses real historical figures active around 1880 (or up to a few decades earlier when a country's specific-era roster would otherwise be too thin, matching the existing roster's tolerance — e.g. Ottoman Empire's Abdulaziz d.1876). For `character_config.json`, the schema/skill-range convention exactly mirrors an existing pool (`Ottoman_Empire`'s, confirmed structure: `ruler` gets all 4 skills 20–80, each advisor role gets its one mapped skill 30–90; `characterId` pattern `<countryid_snake>_<role_short>_<n>` with `mil`/`dip`/`eco`/`sec`/`ruler` short forms). `namePartKeys` are built and de-duplicated against existing `character.name.part.*` en.asset entries following the `add-character-config` skill's algorithm (strip diacritics, lowercase, hyphens→underscores, reuse a key if its EN value matches, mint a new key otherwise); real Russian translations (not placeholders) are produced via the `localization` skill for every newly-minted key.

Flags: add `COUNTRY_FLAGS` entries to `scripts/utils/download_flags.py` with best-effort era-accurate (c.1878–1890) Wikimedia Commons filenames, verify each with `check_flags.py` before the real download, adjust to a fallback/alternate title if `NOT FOUND`, then run `download_flags.py`. This produces PNGs under `Assets/Textures/Flags/Countries/` but cannot import them as Sprites or wire `CountryVisualConfig.asset` (`flag: {fileID: 0}` today for all 6) — that needs Unity MCP, unavailable here, so it is a User Step.

Portraits are out of scope for this feature; the plan documents the exact recipe (paths, prompt template, regional/role modifiers) and the full list of 90 new `characterId`s so the follow-up pass can build `.tmp/images.json` and run `generate_images_batch.py` directly.

## Section 1 — Agent Steps

- [x] **Flip availability + seed relations in `Assets/Configs/country_config.json`** — for each of the 6 entries (`Serbia`, `Bulgaria`, `Bosnia_Herzegovina`, `Montenegro`, `Romania`, `Greece`), set `"isAvailable": true` and populate `historicalFriends`/`historicalRivals` per the confirmed mapping: Serbia — rival `["Ottoman_Empire"]`, friend `["Austria_Hungary"]`; Bulgaria — rival `["Ottoman_Empire"]`, friend `["Russian_Empire"]`; Bosnia_Herzegovina — rival `["Ottoman_Empire"]`, friend `["Austria_Hungary"]`; Montenegro — rival `["Ottoman_Empire"]`, friend `[]`; Romania — rival `["Ottoman_Empire"]`, friend `["Austria_Hungary"]`; Greece — rival `["Ottoman_Empire"]`, friend `[]`. Leave `mainMapFeatureIds`, `secondaryMapFeatureIds`, `initialResources`, `baseDamage`, `baseDurability` untouched (all preserved per spec Out of Scope).

- [x] **Bootstrap all 6 `countryPools` in `Assets/Configs/character_config.json`** — add one pool per country (schema/skill-ranges mirroring `Ottoman_Empire`'s existing pool), 3 characters × 5 roles each, using these real c.1880-era historical figures (name — one-line role fit). `characterId` pattern: `<snake_countryid>_<role_short>_<n>` (`ruler`, `mil`, `dip`, `eco`, `sec`).

  **Serbia** (`serbia_*`):
  - ruler: Miloš Obrenović I — founding Prince of modern Serbia; Mihailo Obrenović III — reforming Prince, assassinated 1868; Milan I Obrenović — Prince 1868–1882, King 1882–1889, ruler at the 1880s "present".
  - military_advisor: Kosta Protić — Serbian-Turkish War (1876–78) commander, later regent; Jovan Belimarković — general and regent; Đura Horvatović — Serbian-Turkish War general.
  - diplomacy_advisor: Jovan Ristić — regent and lead Serbian negotiator at the 1878 Congress of Berlin; Čedomilj Mijatović — foreign minister and diplomat; Milutin Garašanin — statesman and foreign-policy figure.
  - economic_advisor: Kosta Cukić — finance minister; Vladimir Jovanović — economist and reformist politician; Đorđe Vajfert — industrialist and banker.
  - secret_advisor: Nikola Pašić — Radical Party founder, 1880s political agitator/exile; Pera Todorović — Radical journalist and conspirator; Adam Bogosavljević — peasant-movement agitator.

  **Bulgaria** (`bulgaria_*`):
  - ruler: Alexander of Battenberg — first Prince of Bulgaria 1879–1886; Ferdinand I of Bulgaria — Prince from 1887; Stefan Stambolov — regent and de facto head of state during the 1886–87 interregnum (thin-material stand-in per Ambiguity 3, not reused elsewhere).
  - military_advisor: Sava Mutkurov — general, hero of the 1885 Serbo-Bulgarian War; Racho Petrov — general and war minister; Danail Nikolaev — Bulgaria's first war minister and army organiser.
  - diplomacy_advisor: Marin Drinov — historian-statesman, first PM under the 1886 regency; Grigor Nachovich — diplomat, negotiated the 1885 unification with Eastern Rumelia; Konstantin Stoilov — statesman and later PM active in 1880s diplomacy.
  - economic_advisor: Ivan Evstratiev Geshov — banker, founder of Bulgarian financial institutions; Dragan Tsankov — politician-economist, multiple-time PM; Todor Ikonomov — statesman and economic administrator.
  - secret_advisor: Zahari Stoyanov — revolutionary who organized the 1885 unification conspiracy; Panayot Volov — April Uprising conspirator; Dimitar Rizov — diplomat and secret political agent.

  **Bosnia_Herzegovina** (`bosnia_herzegovina_*`) — thin post-1878 material per Ambiguity 3; "ruler" role filled by the Austro-Hungarian military/civil governors who administered the province:
  - ruler: Josip Filipović — first Austro-Hungarian military governor of Bosnia, 1878–1881; Hermann Dahlen von Orlaburg — governor 1881–1882; Benjámin Kállay — Joint Finance Minister and civil administrator/governor 1882–1903.
  - military_advisor: Salih Vilajetović ("Hadži Lojo") — leader of the 1878 Sarajevo resistance to the Austro-Hungarian occupation; Anton von Mollinary — Austro-Hungarian general who commanded the 1878 Bosnia campaign; Stevan Jovanović — Bosnian Serb officer in Habsburg service.
  - diplomacy_advisor: Mustaj-beg Fadilpašić — first mayor of Sarajevo under Austro-Hungarian rule; Mehmed-beg Kapetanović Ljubušak — Bosniak politician and writer engaging with the new administration; Nikola Mandić — Croat lawyer/politician, later senior Bosnian administrator.
  - economic_advisor: Kosta Hörmann — administrator of Bosnia's state monopolies and antiquities/finance under Kállay; Coloman (Kálmán) Thallóczy — head of the Bosnian section of the Joint Finance Ministry; Anto Šola — Sarajevo Croat merchant-family patriarch, representative of the local trading class.
  - secret_advisor: Mićo Ljubibratić — Herzegovinian insurgent leader, active resistance figure; Gligor Jeftanović — Sarajevo Serb merchant who covertly organized Serb cultural-political circles under Habsburg surveillance; Ibrahim-beg Bašagić — Bosniak notable engaged in discreet political organizing.

  **Montenegro** (`montenegro_*`):
  - ruler: Petar II Petrović-Njegoš — Prince-Bishop 1830–1851, celebrated poet-ruler; Danilo I Petrović-Njegoš — Prince 1852–1860, assassinated; Nikola I Petrović-Njegoš — Prince from 1860 (later King), ruler at the 1880s "present".
  - military_advisor: Mašo Vrbica — general and war minister; Petar Vukotić — voivode and military commander; Marko Miljanov — celebrated warrior and voivode of the Kuči tribe.
  - diplomacy_advisor: Gavro Vuković — foreign minister and Montenegro's key negotiator at the 1878 Congress of Berlin; Ilija Plamenac — statesman and prime minister; Jovan Sundečić — poet and court secretary handling correspondence.
  - economic_advisor (thin material per Ambiguity 3 — court/administrative officials): Božo Petrović-Njegoš — statesman/serdar who oversaw court and administrative finances; Lazar Mijušković — senator and later PM involved in state finances; Simo Popović — historian and state secretary handling administrative-economic matters.
  - secret_advisor: Stanko Radonjić — voivode known for court intrigue against Prince Danilo I; Peko Pavlović — voivode who organized covert support for the 1875 Herzegovina uprising; Novica Cerović — senator involved in covertly coordinating the Herzegovina uprising.

  **Romania** (`romania_*`):
  - ruler: Alexandru Ioan Cuza — first Domnitor of the United Principalities, 1859–1866; Carol I — Domnitor from 1866, King from 1881; Lascăr Catargiu — led the 1866 provisional government (locotenența domnească) between Cuza and Carol I.
  - military_advisor: Gheorghe Manu — war minister and commander in the 1877 War of Independence; Alexandru Cernat — chief of staff, hero of Grivița/Plevna; Mihail Cerchez — general at the Siege of Plevna.
  - diplomacy_advisor: Mihail Kogălniceanu — statesman who negotiated recognition of Romanian independence; Ion C. Brătianu — PM, architect of the 1877 independence and the secret 1883 Triple Alliance treaty; Titu Maiorescu — statesman active in 1880s foreign affairs.
  - economic_advisor: Ion Ghica — multi-time PM and economic reformer; Petre S. Aurelian — economist and statistician, "father of Romanian economic sciences"; Dionisie Pop Marțian — Romania's first professional statistician-economist.
  - secret_advisor: C.A. Rosetti — radical journalist and influential behind-the-scenes politician; Vasile Boerescu — jurist-diplomat involved in secret independence-recognition negotiations; Eugeniu Carada — discreet financial architect behind the founding of the National Bank of Romania and Brătianu's political machine.

  **Greece** (`greece_*`):
  - ruler: Otto of Greece — first King, 1832–1862; George I of Greece — King from 1863, ruler at the 1880s "present"; Dimitrios Voulgaris — led the provisional government after Otto's 1862 ouster.
  - military_advisor: Konstantinos Sapountzakis — army chief of staff who organized the 1881 annexation of Thessaly; Timoleon Vassos — career officer active through the 1880s, later led the 1897 Crete intervention; Panos Koronaios — general and politician active across the mid-to-late 19th century.
  - diplomacy_advisor: Charilaos Trikoupis — reforming PM and diplomat; Theodoros Deligiannis — rival nationalist PM and diplomat; Alexandros Koumoundouros — elder statesman, multiple-time PM active in Balkan diplomacy.
  - economic_advisor: Andreas Syngros — banker, "father of modern Greek banking," major financier; Georgios Stavros — founder and first governor of the National Bank of Greece; Stefanos Skouloudis — banker and later PM, prominent 1870s–80s financier.
  - secret_advisor: Epameinondas Deligeorgis — multi-time PM known for political maneuvering; Alexandros Rangavis — diplomat-scholar who conducted discreet territorial-claims negotiations; Konstantinos Kanaris — naval hero and multi-time PM/minister associated with covert operations.

  For each character, build `namePartKeys` following the *real* pool precedent rather than a strict per-word split: compound titles, regnal numerals, and multi-word surnames into a single hyphenated namePart (e.g. `character.name.part.abdul_hamid_ii` → `Value: Abdul-Hamid-II`, matching `Ottoman_Empire`'s existing entries), and prefer dropping middle names rather than giving them their own key (matching `Argentina`'s `julio`+`roca` precedent, which drops "Argentino") unless the middle name is load-bearing for disambiguation. Reuse an existing `character.name.part.<nkey>` key if its stored EN value already matches, otherwise mark the part as needing a new key (feeds the next step). Skills: `ruler` → `power`/`charm`/`stinginess`/`intrigue` all `20–80`; `military_advisor` → `power` `30–90`; `diplomacy_advisor` → `charm` `30–90`; `economic_advisor` → `stinginess` `30–90`; `secret_advisor` → `intrigue` `30–90`.

- [x] **Add new `character.name.part.*` locale keys** — for every name part identified in the previous step that has no existing matching key, append a new `character.name.part.<nkey>` entry to `Assets/Localization/en.asset` (English value) and a real (not placeholder/machine) Russian translation to `Assets/Localization/ru.asset`, using the `localization` skill so the Haiku subagent produces the Russian text in the same pass. Follow the existing YAML block format (`- Key: ...` / `Value: ...`, Cyrillic values written as raw UTF-8 text, matching the existing `character.name.part.*` entries in `ru.asset` — e.g. `character.name.part.sultan` → `Value: Султан`. The `\uXXXX`-escape format seen elsewhere in the file belongs only to the separate, auto-generated `province_name.*` namespace and must not be used here).

- [x] **Add narrative roster sections to `Docs/Characters/character_roster.md`** — using the `add-character` skill's format/structure, append one `## <DisplayName>` section per country (in order: `## Serbia`, `## Bulgaria`, `## Bosnia-Herzegovina`, `## Montenegro`, `## Romania`, `## Greece` — `displayName` values from `country_config.json`, note the hyphenated `Bosnia-Herzegovina` spelling), each with `### Ruler` / `### General` / `### Baron` / `### Secret Advisor` sub-sections listing the same 3 figures chosen per role above (Ruler↔`ruler`, General↔`military_advisor`, Baron↔`economic_advisor`, Secret Advisor↔`secret_advisor`; note the diplomacy_advisor figures are config-only, matching the existing roster's 4-role/5-config-role convention — check one existing country's roster section against its config pool to confirm this asymmetry before writing, and mirror it exactly). Each character entry gets life dates, a one-line contextual note, and an era-appropriate (Balkan/Southern European, oil-painting-style, c.1880 unless dates dictate otherwise) portrait prompt, following the exact bullet format used by existing entries (e.g. Ottoman Empire's section). Append after the file's last existing section, preceded by a `---` separator per the skill's insertion rule; use `Edit` to append, never rewrite the whole file.

- [x] **Add flag download entries and run the download** — add a `COUNTRY_FLAGS` entry to `scripts/utils/download_flags.py` for each of the 6 countries with an era-accurate (c.1878–1890) Wikimedia Commons filename:
  ```python
  "Serbia":              "File:Flag_of_Serbia_(1882-1918).svg",
  "Bulgaria":             "File:Flag_of_Bulgaria_(1878-1944).svg",
  "Bosnia_Herzegovina":   "File:Flag_of_Bosnia_and_Herzegovina_(1878-1918).svg",
  "Montenegro":           "File:Flag_of_Montenegro_(1876).svg",
  "Romania":              "File:Flag_of_Romania.svg",
  "Greece":               "File:Flag_of_Greece_(1822-1978).svg",
  ```
  These filenames are best-guess and unverified — before downloading, run `.venv\Scripts\python.exe scripts\utils\check_flags.py "File:...svg"` for each (per the `flag-assets` skill) and if `NOT FOUND`, search Wikimedia Commons for the correct title and/or add a `COUNTRY_FLAGS_FALLBACK` entry (Bosnia_Herzegovina and Montenegro are the most likely to need a fallback — Bosnia had no official flag under Austro-Hungarian administration, and Montenegro's 1880s state flag has several Commons variants). Then run `.venv\Scripts\python.exe scripts\utils\download_flags.py` and confirm `Verified N/N files OK` for the 6 new entries. This produces `Assets/Textures/Flags/Countries/<countryId>.png` but does **not** import them as Sprites or wire `CountryVisualConfig.asset` — that is Unity-only (see User Steps). If network access is unavailable in this environment, leave the `COUNTRY_FLAGS` entries in place and state in the completion report that the download step itself could not run.

- [x] **Write the portrait-generation recipe for the deferred follow-up pass** — add a short note (in the plan's completion report or a `Docs/Characters/` note referenced from the roster entries) documenting the `image-generation` skill's recipe for the 90 new characters: output path `Assets/Textures/Characters/PortraitCard/{characterId}.png`, size `512x512`, prompt template `portrait of {name}, {regional style} {role description}, 19th century, historical oil painting style, formal attire, serious dignified expression, bust portrait, dark background, highly detailed, realistic painting`, regional style modifier `Balkan, Southern European, 19th century` (adjust per-country flavor if desired: Serbian/South Slavic, Bulgarian/South Slavic-Ottoman, Bosnian/South Slavic-Ottoman mixed heritage, Montenegrin/South Slavic mountain principality, Romanian/Latin-Balkan, Greek/Hellenic), and role descriptions (`ruler` → statesman, ruler, head of state; `military_advisor` → military general, military officer; `diplomacy_advisor` → diplomat, foreign minister; `economic_advisor` → financier, economist, businessman; `secret_advisor` → politician, statesman, advisor). List all 90 new `characterId`s (the 15 per country named in the bootstrap step above) as the exact set the follow-up pass must build into `.tmp/images.json` and run through `generate_images_batch.py`. No images are generated by this step.

- [x] **Run the completion gate** — run the `.claude/rules/config_validation.md` Python cross-check (pools vs. `isAvailable`) via the `.tmp/run.py` pattern (write → `scripts/run.ps1` → delete) and confirm both `Pools not in available countries` and `Available countries without pools` print empty sets. In the same script, also collect every `namePartKeys` string referenced anywhere in `character_config.json` and confirm each one exists as a `Key:` in both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset` — print any missing keys and treat a non-empty list as a gate failure, since no `src`-side test and no Unity Editor is available in this environment to catch a "key not found" warning otherwise. Then run `dotnet-test` (skill) against `src/GlobalStrategy.Core.sln` and confirm the full suite passes, paying particular attention to `StringConfigParityTests.cs` (loads the real `country_config.json`/`character_config.json` files and checks `FileConfig` vs `StringConfig` structural parity — will catch any JSON schema mistake in the edits above) — `CharacterInitTests.cs` and `CountryRelationSeedingTests.cs` use synthetic in-memory configs unrelated to the real file content and need no changes. No `src/` files are touched by this feature, so the CLAUDE.md "after any change under `src/`, run `/dotnet-build Release`" rule does not apply — `dotnet-test` alone is the gate here, matching the spec's own Validation tech note. Report results (pass/fail, and the flag-download outcome) in the final summary.

## Section 2 — User Steps

### 1. Import the 6 flag Sprites and wire them into `CountryVisualConfig.asset`

Once the flag PNGs exist at `Assets/Textures/Flags/Countries/Serbia.png`, `Bulgaria.png`, `Bosnia_Herzegovina.png`, `Montenegro.png`, `Romania.png`, `Greece.png` (produced by the Agent Steps' download step), open the project in the Unity Editor with MCP available and, for each of the 6, follow the `flag-assets` skill's steps 6–9: import the PNG as a Sprite (`manage_texture(action="set_import_settings", ..., as_sprite=true)`), read its asset GUID (`manage_asset(action="get_info", ...)`), find that country's 0-based index in `CountryVisualConfig.asset`'s `Entries` list (re-read the asset file at the time — do not assume the indices seen during planning still hold), and patch `Entries.Array.data[<index>].flag` to `{guid: "<guid>"}` via `manage_scriptable_object`. Commit the new texture assets and `.meta` files together with the config change.

### 2. Generate the 90 new character portraits

Using the recipe documented in the Agent Steps (output path, prompt template, regional/role modifiers) and the full `characterId` list, build `.tmp/images.json` and run `scripts/utils/generate_images_batch.py` against a locally running ComfyUI + FLUX instance (`setup-comfy-ui` skill can start it), per the `image-generation` skill. This is explicitly deferred by the spec to a separate follow-up pass — do this only when that pass is scheduled, not as part of landing this feature.

### 3. In-Editor Play Mode verification

Start a new game session in the Unity Editor (or an equivalent debug build) and confirm: all 6 countries spawn as `Country` entities with their existing `initialResources` unchanged; their provinces render fill/border correctly in all 4 map lenses (Province, Political, Org, Geographic); each country's screen shows exactly 15 named characters (3 per role) with no "key not found" warnings in the console; each country's seeded relations (friend/rival) show correctly in the relations UI; and, once User Step 1 is done, each country's flag renders in the map tooltip/relations UI/character screens.

## Tests

- **Config cross-validation gate (explicit spec requirement):** run the `.claude/rules/config_validation.md` Python script — both `Pools not in available countries` and `Available countries without pools` must print as empty sets after the `country_config.json`/`character_config.json` edits.
- **Locale-key parity gate (added by plan review — no existing test covers this):** every `namePartKeys` string referenced in `character_config.json` must resolve to a `Key:` present in both `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`; this is the only automated check standing in for the Unity Play Mode "key not found" warning this environment cannot run.
- **`dotnet-test` gate:** run the full `src/Game.Tests` suite via the `dotnet-test` skill.
  - `StringConfigParityTests.cs` loads the real `Assets/Configs/country_config.json` and `Assets/Configs/character_config.json` via both `FileConfig` and `StringConfig` and asserts structural parity (counts, first-entry field values) between the two loaders — this will fail loudly on any JSON syntax or schema mistake introduced while editing these files, even though it does not assert a fixed total count.
  - `CharacterInitTests.cs` and `CountryRelationSeedingTests.cs` build their own synthetic in-memory `CountryConfig`/`CharacterConfig` fixtures and do not read the real config files, so they need no changes and are unaffected by this feature's data additions.
  - No other test file in `src/Game.Tests/` hardcodes a country or character count tied to the real config content (confirmed by search).
- No `dotnet-build Release` is required — this feature makes zero `src/` changes, so the CLAUDE.md "after any change under `src/`" build rule is out of scope; `dotnet-test` is the sufficient completion gate here, matching the spec's own Tech Notes ("a `dotnet-build`/`dotnet-test` pass if any config-loader tests assert on country/character counts").
- Manual verification of in-game spawning/rendering/roster/relations/flags is Unity-Editor-only and is captured as User Step 3, not an automated test.

## Constitution Check

No conflicts found — plan aligns with all principles.

- **Rendering (URP only):** no rendering code/shader/material changes.
- **Game Logic (ECS in `src/`):** no ECS/system/domain code changes; `InitSystem` already generically handles any `IsAvailable` country.
- **Dependency Injection (VContainer only):** no DI changes.
- **UI (UI Toolkit only):** no UI code/UXML/USS changes.
- **Planning Discipline — plan before implement:** this plan itself satisfies the requirement; no code/asset changes have been made ahead of it.
- **Specification Discipline — spec before plan:** `Docs/Specs/26_08_07_19_add-more-countries/spec.md` already exists, was approved, and all its Ambiguities were confirmed by the owner before this plan was written.
- **File Organisation:** this plan is written to `Docs/Specs/26_08_07_19_add-more-countries/plan.md`, sibling to the existing `spec.md`, per the required `Docs/Specs/<YY_MM_DD_HH>_<name>/` convention.
- **Assembly Structure / C# code style:** not applicable — no `.asmdef` or C# files are touched.

Use the implement skill to start working on the plan or request changes.

## Completion Report

All Section 1 Agent Steps are complete and verified:

- `country_config.json`: all 6 countries flipped to `isAvailable: true` with the confirmed relations mapping (Serbia/Bulgaria/Bosnia_Herzegovina/Romania each have one friend + `Ottoman_Empire` rival; Montenegro/Greece have `Ottoman_Empire` rival only, no friend — per Ambiguity 0).
- `character_config.json`: all 6 `countryPools` bootstrapped, 15 characters each (90 total), 3 per role × 5 roles, skill ranges matching the `ruler`/`military_advisor`/`diplomacy_advisor`/`economic_advisor`/`secret_advisor` convention. No duplicate `characterId`s against the existing 20 pools.
- `en.asset`/`ru.asset`: every `namePartKeys` string referenced by the 6 new pools resolves to a `Key:` in both files (verified by script — zero missing). `ru.asset` values are raw UTF-8 Cyrillic, matching the existing `character.name.part.*` convention (not `\uXXXX` escapes).
- `Docs/Characters/character_roster.md`: `## Serbia`, `## Bulgaria`, `## Bosnia-Herzegovina`, `## Montenegro`, `## Romania`, `## Greece` sections added, each with Ruler/General/Baron/Secret Advisor sub-sections and a per-character portrait prompt.
- Flags: `COUNTRY_FLAGS` entries added to `scripts/utils/download_flags.py` for all 6; the download itself ran successfully — all 6 PNGs exist at `Assets/Textures/Flags/Countries/<id>.png` and are valid images (verified via PNG header/dimension check: Serbia 330×220, Bulgaria 330×198, Bosnia_Herzegovina 330×180, Montenegro 330×264, Romania 330×220, Greece 330×220). Only the Unity-side Sprite import + `CountryVisualConfig.asset` GUID wiring remains (User Step 1 — no Unity MCP in this environment).

**Completion gate results:**
- Config cross-validation (`.claude/rules/config_validation.md`): `Pools not in available countries` = `{}`, `Available countries without pools` = `{}`.
- Locale-key parity check: every `namePartKeys` string in `character_config.json` resolves to a `Key:` in both `en.asset` and `ru.asset` — zero missing.
- `dotnet-test` (`src/GlobalStrategy.Core.sln`, Debug): all 4 suites pass — `ECS.Tests` 34/34, `ECS.Viewer.Tests` 16/16, `Game.Tests` 876/876 (including `StringConfigParityTests`), `Game.WebClient.Tests` 89/89. Zero failures.

**Portrait-generation recipe (deferred to the follow-up pass, per the issue) — full 90-`characterId` list:**

Output path `Assets/Textures/Characters/PortraitCard/{characterId}.png`, 512×512, prompt template `portrait of {name}, {regional style} {role description}, 19th century, historical oil painting style, formal attire, serious dignified expression, bust portrait, dark background, highly detailed, realistic painting` (per-character bespoke prompts are already written into each `character_roster.md` entry and can be used directly instead of the generic template). Regional style: Serbian/South Slavic, Bulgarian/South Slavic-Ottoman, Bosnian/South Slavic-Ottoman mixed heritage, Montenegrin/South Slavic mountain principality, Romanian/Latin-Balkan, Greek/Hellenic. Role descriptions: `ruler` → statesman, ruler, head of state; `military_advisor` → military general, military officer; `diplomacy_advisor` → diplomat, foreign minister; `economic_advisor` → financier, economist, businessman; `secret_advisor` → politician, statesman, advisor.

- **Serbia**: `serbia_ruler_1`, `serbia_ruler_2`, `serbia_ruler_3`, `serbia_mil_1`, `serbia_mil_2`, `serbia_mil_3`, `serbia_dip_1`, `serbia_dip_2`, `serbia_dip_3`, `serbia_eco_1`, `serbia_eco_2`, `serbia_eco_3`, `serbia_sec_1`, `serbia_sec_2`, `serbia_sec_3`
- **Bulgaria**: `bulgaria_ruler_1`, `bulgaria_ruler_2`, `bulgaria_ruler_3`, `bulgaria_mil_1`, `bulgaria_mil_2`, `bulgaria_mil_3`, `bulgaria_dip_1`, `bulgaria_dip_2`, `bulgaria_dip_3`, `bulgaria_eco_1`, `bulgaria_eco_2`, `bulgaria_eco_3`, `bulgaria_sec_1`, `bulgaria_sec_2`, `bulgaria_sec_3`
- **Bosnia_Herzegovina**: `bosnia_herzegovina_ruler_1`, `bosnia_herzegovina_ruler_2`, `bosnia_herzegovina_ruler_3`, `bosnia_herzegovina_mil_1`, `bosnia_herzegovina_mil_2`, `bosnia_herzegovina_mil_3`, `bosnia_herzegovina_dip_1`, `bosnia_herzegovina_dip_2`, `bosnia_herzegovina_dip_3`, `bosnia_herzegovina_eco_1`, `bosnia_herzegovina_eco_2`, `bosnia_herzegovina_eco_3`, `bosnia_herzegovina_sec_1`, `bosnia_herzegovina_sec_2`, `bosnia_herzegovina_sec_3`
- **Montenegro**: `montenegro_ruler_1`, `montenegro_ruler_2`, `montenegro_ruler_3`, `montenegro_mil_1`, `montenegro_mil_2`, `montenegro_mil_3`, `montenegro_dip_1`, `montenegro_dip_2`, `montenegro_dip_3`, `montenegro_eco_1`, `montenegro_eco_2`, `montenegro_eco_3`, `montenegro_sec_1`, `montenegro_sec_2`, `montenegro_sec_3`
- **Romania**: `romania_ruler_1`, `romania_ruler_2`, `romania_ruler_3`, `romania_mil_1`, `romania_mil_2`, `romania_mil_3`, `romania_dip_1`, `romania_dip_2`, `romania_dip_3`, `romania_eco_1`, `romania_eco_2`, `romania_eco_3`, `romania_sec_1`, `romania_sec_2`, `romania_sec_3`
- **Greece**: `greece_ruler_1`, `greece_ruler_2`, `greece_ruler_3`, `greece_mil_1`, `greece_mil_2`, `greece_mil_3`, `greece_dip_1`, `greece_dip_2`, `greece_dip_3`, `greece_eco_1`, `greece_eco_2`, `greece_eco_3`, `greece_sec_1`, `greece_sec_2`, `greece_sec_3`

**Remaining (Section 2 User Steps — Unity Editor / MCP / ComfyUI required, unavailable in this automation environment):**
1. Import the 6 flag PNGs as Sprites and wire their GUIDs into `CountryVisualConfig.asset`.
2. Generate the 90 character portraits (recipe above) via the `image-generation` skill once ComfyUI is available — explicitly deferred by the issue to a separate pass regardless.
3. In-Editor Play Mode verification of spawning/rendering/roster/relations/flags.
