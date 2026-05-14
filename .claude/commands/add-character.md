Add one or more entries to `Docs/Characters/character_roster.md`.

Accepts any combination of:
- A new country block (country name + all 4 roles)
- A new role block inside an existing country (Ruler / General / Baron / Secret Advisor)
- A new character inside an existing role block

## Arguments

`$ARGUMENTS` may be free-form. Examples:
- `Ottoman Empire – Ruler` — pick and add a new historically appropriate Ruler for that country
- `Brazil – General` — choose a fitting general and fill in all details
- `Ottoman Empire – Ruler: Mehmed V` — add a specific named character
- `Brazil – new role: Admiral` — add a new role block to an existing country
- `Venezuela` — add a full new country block

**Name is always optional.** When a name is not supplied, choose the most historically significant and fitting real person for that country and role who is not already in the roster.

If `$ARGUMENTS` is empty, ask the user what to add before proceeding.

## Steps

1. **Parse intent** from `$ARGUMENTS`:
   - Identify target country, role (if given), and character name (if given — may be absent)
   - If country or role is ambiguous, ask one clarifying question before continuing

2. **Read the roster** at `Docs/Characters/character_roster.md` to understand current structure, find existing characters for that country/role, and determine the insertion point

3. **Select and research the character** using your knowledge (no web search needed unless explicitly asked):
   - If a name was given: use that person
   - If no name was given: choose the best real historical figure for that country + role who does not already appear in the roster; prefer figures active around 1880 but accept 1700–1900
   - Produce: full name, life dates, brief contextual note, portrait prompt
   - For a new role block: define the role and produce 3 characters the same way
   - For a new country: produce all 4 standard roles (Ruler, General, Baron, Secret Advisor) × 3 characters each

4. **Portrait prompt format** — every character must have one:
   - Oil painting style, circa 1880 unless dates dictate otherwise
   - Include: age/decade, attire (specific to role and nationality), expression, background setting
   - Example: *Portrait prompt: Oil painting of a ...*

5. **Character block format**:
   ```
   - **Full Name** (birth–death) — brief contextual note
     - *Portrait prompt: Oil painting of ...*
   ```

6. **Insertion rules**:
   - New country: append at the end of the file, following the `---` separator pattern
   - New role in existing country: append after the last existing role block for that country
   - New character in existing role: append after the last character in that role block
   - Maintain the heading hierarchy: `## Country`, `### Role`, bullet list of characters

7. **Write the change** using the Edit tool (append or insert, never rewrite the whole file)

8. **Confirm** by stating exactly what was added (country / role / character name)
