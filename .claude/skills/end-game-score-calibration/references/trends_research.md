# Trends research — nine end-game comparison identities

Research date: **2026-07-24**.

## Methodology note: Google Trends substitution

The plan (`Docs/Specs/26_07_22_16_end-game-window-goal-hint/plan.md`, Approach Step 9) calls for
ranking these nine identities by worldwide Google Trends popularity. Direct access to
`trends.google.com` (both the interactive explore UI and its underlying widget/API endpoints)
returned `HTTP 429 Too Many Requests` to every fetch attempt made from this environment — Google
Trends is a JS-rendered SPA that blocks automated/headless fetches, and no interactive browser was
available to work around this.

Per explicit user decision (asked mid-session, given this hard blocker), this research instead
uses the **Wikimedia pageviews API** (`wikimedia.org/api/rest_v1/metrics/pageviews/...`) as a
documented, publicly citable, dated proxy for worldwide public-interest popularity. This is a
deliberate substitution for Google Trends, not an attempt to reproduce it — recorded here so a
future rerun (if Google Trends becomes reachable) can be compared against or replace this data.

## Query parameters (recorded per the spec's requirement)

- **Geography scope:** Wikimedia pageviews are not geo-filtered by this query — `all-access`
  aggregates desktop/mobile-web/mobile-app traffic to the **English Wikipedia** edition
  (`en.wikipedia`) from all countries. This is a proxy for worldwide *English-language* interest,
  not literally all-language worldwide interest (the nearest available substitute for Trends'
  "Worldwide" + default-language scope).
- **Metric:** monthly pageview counts (`all-agents`, i.e. including both user and bot/spider
  traffic — Wikimedia does not offer a "human-only worldwide" breakdown at this granularity; bot
  traffic is assumed roughly proportional across articles and so does not bias the *relative*
  ranking).
- **Time window:** 13 monthly buckets, **2025-07-01 through 2026-07-01** inclusive (the most
  recent full year available at research time).
- **Term-vs-topic choice:** one canonical English Wikipedia article per identity (a "topic", in
  Trends terminology — the article that best matches the folklore concept as commonly searched,
  not a disambiguation page). Exact article titles are recorded in the table below so the query is
  reproducible.
- **Normalization / shared-anchor method:** Wikipedia pageviews (unlike Google Trends' 0–100
  per-query relative index) are **absolute counts already on a single shared unit** (views per
  article per month, same wiki, same time window, same access/agent filters) — so, unlike raw
  Trends numbers from separate single-term queries, these totals are directly comparable to each
  other with no additional normalization step required. This sidesteps the exact problem the spec
  flags about combining incomparable relative Trends samples.

## Raw data (sum of the 13 monthly buckets, English Wikipedia, all-access/all-agents)

| Rank (least → most popular) | Comparison identity | `comparisonElementId` | Wikipedia article | 13-month pageview total |
|---|---|---|---|---|
| 1 (least popular) | Committee of 300 | `CommitteeOf300` | [Committee of 300](https://en.wikipedia.org/wiki/Committee_of_300) | 142,221 |
| 2 | Trilateral Commission | `TrilateralCommission` | [Trilateral Commission](https://en.wikipedia.org/wiki/Trilateral_Commission) | 288,370 |
| 3 | Bilderberg Group | `BilderbergGroup` | [Bilderberg Meeting](https://en.wikipedia.org/wiki/Bilderberg_Meeting) | 368,521 |
| 4 | Deep State | `DeepState` | [Deep state](https://en.wikipedia.org/wiki/Deep_state) | 383,201 |
| 5 | Reptilians | `Reptilians` | [Reptilian conspiracy theory](https://en.wikipedia.org/wiki/Reptilian_conspiracy_theory) | 698,811 |
| 6 | Skull and Bones | `SkullAndBones` | [Skull and Bones](https://en.wikipedia.org/wiki/Skull_and_Bones) | 721,554 |
| 7 | New World Order | `NewWorldOrder` | [New World Order (conspiracy theory)](https://en.wikipedia.org/wiki/New_World_Order_(conspiracy_theory)) | 1,092,453 |
| 8 | Knights Templar | `KnightsTemplar` | [Knights Templar](https://en.wikipedia.org/wiki/Knights_Templar) | 1,269,346 |
| 9 (most popular) | Bohemian Grove | `BohemianGrove` | [Bohemian Grove](https://en.wikipedia.org/wiki/Bohemian_Grove) | 1,320,625 |

Data pulled via:

```
curl "https://wikimedia.org/api/rest_v1/metrics/pageviews/per-article/en.wikipedia/all-access/all-agents/<Article_Title>/monthly/2025070100/2026070100"
```

for each `<Article_Title>` listed above, summing the `views` field across all 13 returned items.

## Selection rationale

All nine are widely known "secretly controls the world" folklore subjects distinct from this
game's two playable organizations (`Illuminati`, `Masons` — see `Assets/Configs/organizations.json`),
so the comparison block never duplicates a name the player can already play as. Real-world ethnic
or religious groups that are targets of antisemitic or similar bigoted "world control" conspiracy
tropes (e.g. claims about the Rothschild family or "Zionist Occupation Government") were
deliberately excluded from consideration — every identity selected here is either a secret-society
mythology (Skull and Bones, Bilderberg Group, Trilateral Commission, Committee of 300, Bohemian
Grove, Knights Templar), a genre-conspiracy concept (Reptilians, New World Order, Deep State), or
otherwise not a real ethnic/religious population.

**Folklore framing requirement:** every description of these identities — here and in the shipped
`end_game.comparison.*` localization strings (Step 16) — must read as recounting a claim/legend/
folklore belief, never as asserting the claim is true (e.g. "folklore claims the Committee of 300
secretly rules the world," not "the Committee of 300 rules the world").

## Rank → threshold mapping

The ascending rank order above (least → most popular) feeds directly into
`Docs/Specs/26_07_22_16_end-game-window-goal-hint/plan.md`'s Step 9/10 requirement: least popular
gets the lowest calibrated threshold (`factor(0) = 0.05`), most popular gets the highest
(`factor(8) = 1.20`), per `calibration_results.md`'s threshold table. See that file's `i` column
for the exact score assigned to each rank position.
