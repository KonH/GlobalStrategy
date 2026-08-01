# Clarification Questions in Issue / PR Comments

When automation (or an agent working an issue/PR) needs owner decisions, put the questions in the **handoff comment itself** — not only as a mention that questions exist elsewhere (spec Ambiguities, plan notes, chat).

## Required format

1. **Always write the questions in the comment.** Do not say “see Ambiguities in the spec” or “open clarifications remain” without listing them.
2. **Number them `0`–`9` (then continue `10+` if needed)** so the owner can answer with short replies like `0: yes`, `3: FIFO`, `7: ignore`.
3. **Show the full question text** for each item — not a short paraphrase. Include enough context that the owner can decide without opening another file. Assumed defaults may follow the question in parentheses.

## Example

```markdown
**Clarification questions** (reply with `N: answer`, then remove `ai-need-attention` to resume):

0. Exact definition of “player has influence” — any player-org control > 0 in a participant country, or must the country also be discovered, or does HQ country alone count? (assumed: control > 0)
1. Sibling `WarResultWindow` vs extending `WarProgressWindow` in place? (recommended: sibling)
2. Should the result window still show the final progress slider, effects list, side stats, and battles list, or only header chrome plus the new winner label and results block?
```

## Anti-patterns

- Listing only abbreviated titles (“influence definition”, “FIFO vs merge”) without the full question
- Saying questions exist in `spec.md` without repeating them in the comment
- Using bullets without stable numbers the owner can cite
