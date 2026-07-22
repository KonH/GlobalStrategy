# Character Portrait Recipe

- Output: `Assets/Textures/Characters/{characterId}.png`
- Size: `512x512`
- Prompt template:

  ```text
  portrait of {name}, {regional style} {role description},
  19th century, historical oil painting style,
  formal attire, serious dignified expression,
  bust portrait, dark background, highly detailed, realistic painting
  ```

Regional style examples: Argentine, Latin American, Spanish heritage; Japanese, East Asian, Meiji era; Ethiopian, East African; British, Victorian era.

Role descriptions: ruler — statesman, ruler, head of state; military — military general, military officer; diplomacy — diplomat, foreign minister; economic — financier, economist, businessman; secret — politician, statesman, advisor.

Read `character_config.json` and `Assets/Localization/en.asset` (`character.name.part.*`) for character names and country pools.
