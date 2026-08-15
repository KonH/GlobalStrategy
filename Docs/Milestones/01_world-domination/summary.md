# 1. World Domination
## 2026-04-02 - 2026-08-15 - 4 months, 13 days (136 days total)

See [`stats_code.md`](stats_code.md) and [`stats_dev.md`](stats_dev.md) for full data.

## Dev Notes

- `.cs`: 71,667 LoC across 641 files.
- Codebase stands at 598,354 LoC across 1120 tracked files; `.json` is the largest share (438,652 LoC).
- No previous milestone recorded yet, so this snapshot is the new baseline.
- **Coverage gap:** 29/88 specs have no `implement`-stage row (unbackfilled), so provider/model/cost figures below undercount real tool usage - see stats_dev.md.
- 88 specs shipped in range, 258 usage rows total.
- $163.23 total priced cost across 62/258 rows with pricing data (rest are backfilled/unpriced).
- Primary provider: claude (74.8% of rows).
- No previous milestone recorded yet, so this snapshot is the new baseline.

## Release Notes

1. **Playable core loop** — pick a secret organization (Masons, Illuminati, or Black Hand) and compete to dominate 1880s Europe through action cards for control, diplomacy, and war.
2. **Full world map** with real provinces spanning all 26 playable countries — occupy territory and shift borders as control changes hands.
3. **Complete war system** — declare war, fight multi-stage battles, and negotiate peace.
4. **Rivalries** — build and break rival relationships with dedicated cards.
5. **AI bot opponents** that play the full game (diplomacy, war, card economy) via a dedicated bot API and eval harness.
6. **Goals & win conditions** — live progress tracking, end-game comparison scores, and a leaderboard.
7. **Guided tutorial** for new players.
8. **Full English + Russian localization**.
9. **Playable in-browser** via WebGL (Unity Play) with persistent saves.
10. **Built with heavy AI-assisted development** — Claude, Codex, and Cursor for coding (via the Spec Kit planning framework), ChatGPT for UI, card, and event art, ComfyUI for character portraits.

<!-- milestone-meta: {"end_date": "2026-08-15", "major": "1", "name": "World Domination", "start_date": "2026-04-02"} -->
