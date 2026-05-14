Add a new character entry to `Assets/Configs/character_config.json` and matching locale keys to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`.

## Arguments

`$ARGUMENTS` format: `<countryId> <roleId> <EnglishName> | <RussianName>`

Examples:
- `Russia military_advisor Ivan Smirnov | Иван Смирнов`
- `Ottoman_Empire ruler Mehmed V | Мехмед V`
- `Argentina secret_advisor Carlos Tejedor | Карлос Техедор`

Role IDs: `ruler`, `military_advisor`, `diplomacy_advisor`, `economic_advisor`, `secret_advisor`

Country IDs must exactly match entries in `Assets/Configs/country_config.json`.

If `$ARGUMENTS` is empty or incomplete, ask the user for the missing parts before continuing.

## Steps

### 1. Parse arguments

Extract from `$ARGUMENTS`:
- `countryId` — the exact country ID string
- `roleId` — one of the five role IDs above
- `englishName` — full name in English (space-separated words)
- `russianName` — full name in Russian (must have the **same word count** as the English name)

If the Russian name is missing, ask for it before proceeding — word count parity between EN and RU is required.

### 2. Validate inputs

- Read `Assets/Configs/character_config.json`
- Confirm `countryId` exists in `countryPools`
- Confirm `roleId` exists in the country's `slots`
- Confirm EN and RU names have the same number of space-separated words; if not, stop and ask the user to fix the mismatch (suggest hyphenating compound words, e.g. `Crown-Prince` / `Кронпринц`)

### 3. Generate the new characterId

Pattern: `{country_snake}_{role_abbrev}_{n}` where:
- `country_snake` = countryId lowercased, spaces → underscores (e.g. `russian_empire`)
- `role_abbrev` = `ruler` → `ruler`, `military_advisor` → `mil`, `diplomacy_advisor` → `dip`, `economic_advisor` → `eco`, `secret_advisor` → `sec`
- `n` = next integer after the highest existing `_n` suffix for that country+role

### 4. Build namePartKeys

Split both names by spaces. For each English word:
1. Compute `nkey`: strip diacritics, lowercase, hyphens→underscores, remove non-`[a-z0-9_]`, strip edge underscores
2. Search `en.asset` for an existing `character.name.part.{nkey}` entry
   - If found: reuse it (check the stored EN value matches; if it doesn't, mint a suffixed key `{nkey}_2`, `{nkey}_3`, etc. until one is available or matches)
   - If not found: this word needs a new locale entry
3. Collect the list of `character.name.part.{nkey}` keys — this is `namePartKeys`

### 5. Build the config entry

Determine skill ranges from the role:
- `ruler`: all four skills (`power`, `charm`, `stinginess`, `intrigue`) with `minValue: 20, maxValue: 80`
- `military_advisor`: `power` only, min 30 max 90
- `diplomacy_advisor`: `charm` only, min 30 max 90
- `economic_advisor`: `stinginess` only, min 30 max 90
- `secret_advisor`: `intrigue` only, min 30 max 90

Entry shape:
```json
{
  "characterId": "<generated-id>",
  "namePartKeys": ["character.name.part.<key1>", ...],
  "skills": { ... }
}
```

### 6. Write the config

Using `Edit`, append the new character object to the end of the correct `slots.<roleId>` array inside the correct `countryId` pool. Do **not** rewrite the whole file.

### 7. Write the locale entries

For each word that needs a **new** locale key (not already in en.asset):
- Append to `en.asset`:
  ```yaml
    - Key: character.name.part.<nkey>
      Value: <EnglishWord>
  ```
- Append to `ru.asset` (Cyrillic values must use `\uXXXX` unicode escapes, same format as existing entries):
  ```yaml
    - Key: character.name.part.<nkey>
      Value: "<escaped-russian-word>"
  ```

If all words already have existing keys, no locale changes are needed.

Use the `.tmp/run.py` pattern (write → `& ".claude\run.ps1"` → `Remove-Item .tmp\run.py`) for any non-trivial JSON/YAML surgery. For a single new entry where the insertion point is clear, direct `Edit` is fine.

### 8. Confirm

State:
- The new `characterId`
- The `namePartKeys` used (noting which were reused vs newly created)
- Where the entry was inserted in the config
