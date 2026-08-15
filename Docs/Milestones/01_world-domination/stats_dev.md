# Dev Stats

Specs with `Docs/Specs/<dir>/` timestamp between 2026-04-02 and 2026-08-15 (inclusive), aggregated from each spec's `usage.csv`.

**Coverage gap:** 29/88 specs in range have **no `implement`-stage row at all** (the backfill's multi-PR implement-detection heuristic produced false positives and was disabled, so those rows were logged as unverified suggestions instead of written - see the backfill commits' own messages). Implementation is where most real coding work - and most non-Claude-tool usage - happens, so every provider/model/cost figure below is skewed toward whichever tool authored `spec.md`/`plan.md` and **undercounts actual tool usage, especially for cursor/codex.** Treat provider/model shares and cost totals as directional, not exact, until those rows are backfilled.

_No previous milestone to compare against - this is the first milestone._

## Per-Spec Data

| Spec | Rows | Providers | Models | Cost ($) | Input tok | Cached tok | Output tok | Spec tok | Plan tok | Diff lines |
|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|
| 26_06_13_21_dev-map-autoload | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 770 | 799 | 98 |
| 26_06_15_09_version-label | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 634 | 1443 | 164 |
| 26_06_15_10_org-lens-selection-panel | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 716 | 1887 | 262 |
| 26_06_15_11_character-opinion | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 1566 | 2746 | 529 |
| 26_06_20_00_country-action-cards | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 3262 | 12570 | 3691 |
| 26_06_20_23_card-config-refactor | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 3185 | 5422 | 1884 |
| 26_06_22_11_animated-value-barriers | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 1755 | 3247 | 1708 |
| 26_06_22_23_visual-state-action-refactor | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 2490 | 3908 | 1708 |
| 26_06_23_14_unified-action-pipeline | 3 | claude | claude-sonnet-4-6, claude-sonnet-5 | n/a | 0 | 0 | 0 | 2276 | 6859 | 765 |
| 26_06_28_15_org-country-flags | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 912 | 2122 | 4553 |
| 26_07_05_13_fly-text-notifications | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 1184 | 7415 | 954 |
| 26_07_10_18_province-division | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 2092 | 6625 | 86567 |
| 26_07_11_09_province-map-lens | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 1762 | 5746 | 735 |
| 26_07_11_10_province-ownership | 3 | claude | claude-sonnet-4-6 | n/a | 0 | 0 | 0 | 2247 | 4842 | 1201 |
| 26_07_13_17_province-population | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 1948 | 4802 | 34317 |
| 26_07_14_09_country-scoring | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 1635 | 5904 | 327 |
| 26_07_16_09_org-scoring | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 1705 | 3676 | 447 |
| 26_07_16_14_bot-feature-eval-harness | 3 | claude | claude-fable-5, claude-sonnet-5 | n/a | 0 | 0 | 0 | 5954 | 12253 | 4962 |
| 26_07_16_14_bot-org-api | 3 | claude | claude-fable-5, claude-sonnet-5 | n/a | 0 | 0 | 0 | 4757 | 11676 | 4962 |
| 26_07_16_14_multi-org-headless-simulation | 3 | claude | claude-fable-5, claude-sonnet-5 | n/a | 0 | 0 | 0 | 3746 | 10223 | 4962 |
| 26_07_16_15_score-component-composition | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | n/a | 3671 | 447 |
| 26_07_17_06_bot-opponent-unity | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 7479 | 10683 | 1653 |
| 26_07_18_07_action-log-ui | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 3953 | 17632 | 1061 |
| 26_07_18_15_recruits-resource | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 2037 | 4462 | 505 |
| 26_07_18_17_resource-collector-pipeline | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 2584 | 7576 | 505 |
| 26_07_18_18_benchmarkdotnet-perf-harness | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 3723 | 12254 | 447 |
| 26_07_18_19_leaderboards-ui | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 2210 | 4300 | 1251 |
| 26_07_18_19_province-occupation | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 1911 | 5643 | 1254 |
| 26_07_21_08_visualstate-update-optimization | 15 | claude | claude-sonnet-5 | 5.7824 | 518 | 14322065 | 98948 | 2516 | 5936 | n/a |
| 26_07_21_18_resources-visual-update | 3 | claude, codex | claude-sonnet-5, gpt-5.6-sol | n/a | 0 | 0 | 0 | 1238 | 4206 | 816 |
| 26_07_22_08_province-info-panel | 15 | claude | claude-sonnet-5 | 26.0895 | 7145 | 75070667 | 236458 | 1967 | 7261 | 0 |
| 26_07_22_11_win-lose-logic | 2 | claude, codex | claude-sonnet-5, gpt-5.6-sol | n/a | 0 | 0 | 0 | 1885 | 3450 | 90 |
| 26_07_22_16_end-game-window-goal-hint | 12 | claude | claude-sonnet-5 | 4.9915 | 468 | 11947223 | 93730 | 4054 | 10034 | n/a |
| 26_07_22_17_spec-dev-stats | 3 | claude | claude-sonnet-5 | 38.3304 | 530 | 120637761 | 142502 | 2803 | 9317 | 0 |
| 26_07_23_06_country-relations | 2 | claude | claude-sonnet-5 | 6.5559 | 169 | 16047351 | 116080 | 2977 | 6674 | 1492 |
| 26_07_23_06_fly-text-mechanism | 2 | claude | <synthetic>, claude-sonnet-5 | n/a | 88 | 6548323 | 79854 | 1554 | 5476 | 301 |
| 26_07_23_15_standalone-web-client | 5 | claude | claude-sonnet-5, fable 5 | 35.7686 | 11493 | 128771728 | 562119 | 5315 | 4618 | 12623 |
| 26_07_24_10_country-relation-cards | 2 | claude | claude-sonnet-5 | 10.8606 | 16919 | 28795020 | 144759 | 4433 | 10438 | 7632 |
| 26_07_24_10_friends-rivals-panel | 2 | claude | claude-sonnet-5 | 3.4568 | 118 | 9672045 | 36986 | 2014 | 3856 | 7418 |
| 26_07_24_13_stop-friendship-rivalry-cards | 3 | claude | claude-sonnet-5 | 7.0328 | 316 | 18046178 | 107868 | 6064 | 7980 | 1598 |
| 26_07_25_06_war-mechanics-core | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 3476 | 3044 | 416 |
| 26_07_29_00_character-hints | 1 | claude | claude-sonnet-5 | 8.1649 | 355 | 21747293 | 109309 | 2586 | 5128 | 500 |
| 26_07_29_00_decrease-enemy-control-card | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 4060 | 6680 | 679 |
| 26_07_29_10_session-limit-detection | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 570 | 296 | 562 |
| 26_07_29_13_auto-ai-provider-routing | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 1309 | 1454 | 548 |
| 26_07_29_16_damage-durability-at-war | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2799 | 5932 | 1675 |
| 26_07_29_16_peace-resolution | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 3328 | 3933 | 23379 |
| 26_07_29_16_sell-arms-card | 2 | claude, codex | claude-sonnet-5, gpt-5.6-sol | n/a | 0 | 0 | 0 | 4585 | 2853 | 68 |
| 26_07_29_16_war-icons | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 2632 | 4535 | 340 |
| 26_07_29_16_war-progress | 2 | codex | gpt-5, gpt-5.6-sol | n/a | 0 | 0 | 0 | 4739 | 5840 | 373 |
| 26_07_29_20_revenge-card | 3 | claude, cursor | claude-sonnet-5, cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 3336 | 6691 | 1512 |
| 26_07_29_21_declare-war-card | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 5467 | 4402 | 99 |
| 26_07_29_21_ultimatum-surrender-cards | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 4265 | 9338 | 496 |
| 26_07_30_12_debug-card-availability | 1 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2970 | n/a | 97 |
| 26_07_30_13_war-progress-window | 3 | claude, cursor | claude-sonnet-5, cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2083 | 3491 | 2060 |
| 26_07_31_19_war-result-window | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 3112 | 7267 | 580 |
| 26_07_31_23_prevent-double-automation | 3 | claude, cursor | claude-sonnet-5, cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 1810 | 5884 | 242 |
| 26_08_01_09_bot-war-features | 1 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 173 |
| 26_08_02_13_drop-discovery-concept | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2676 | 2729 | 3213 |
| 26_08_02_13_score-count-goal | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 2647 | 4678 | 364 |
| 26_08_02_14_prevent-cross-instance-reclaim | 1 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 3012 | n/a | 75 |
| 26_08_02_16_solid-in-progress-harness | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2080 | 3926 | 670 |
| 26_08_03_08_player-org-feature-flag | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 1347 | 2638 | 102 |
| 26_08_03_13_goals-window | 3 | claude, cursor | claude-sonnet-5, cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 1908 | 3844 | 1229 |
| 26_08_03_13_secret-advisor-flag | 3 | claude, cursor | claude-sonnet-5, cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 1575 | 3232 | 89 |
| 26_08_03_13_war-result-preview | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2882 | 2678 | 413 |
| 26_08_04_17_card-cooldown | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 4699 | 8443 | 822 |
| 26_08_04_17_country-org-borders | 3 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 1708 | 7615 | 195 |
| 26_08_04_17_sell-arms-peacetime | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2046 | 2054 | 248 |
| 26_08_04_17_small-ui-improvements | 3 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 2528 | 2295 | 419 |
| 26_08_06_10_card-deck-rework | 2 | claude, codex | claude-sonnet-5, gpt-5.6-sol | n/a | 0 | 0 | 0 | 7004 | 2698 | 114 |
| 26_08_06_10_card-ui-discard-rework | 2 | claude, codex | claude-sonnet-5, gpt-5.6-sol | n/a | 0 | 0 | 0 | 6428 | 2710 | 114 |
| 26_08_07_08_country-destroy-logic | 3 | claude, cursor | claude-sonnet-5, cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 1865 | 4260 | 822 |
| 26_08_07_08_country-destroy-ui | 3 | claude, cursor | claude-sonnet-5, cursor-grok-4.5-high | n/a | 0 | 0 | 0 | 1787 | 3439 | 501 |
| 26_08_07_13_short-term-tasks | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 284 |
| 26_08_07_19_add-more-countries | 3 | claude | claude-sonnet-5 | 8.7424 | 425 | 25045704 | 81829 | 3213 | 8602 | 18231 |
| 26_08_08_16_card-draw-logic | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 3738 | 3468 | 121 |
| 26_08_08_16_card-draw-ui | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 3627 | 5794 | 146 |
| 26_08_08_18_tutorial | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 422 |
| 26_08_08_20_black-hand-org | 1 | claude | claude-sonnet-5 | 4.6181 | 209 | 13130093 | 45230 | 1614 | 4027 | 20216 |
| 26_08_08_23_country-targeted-relation-cards | 2 | claude | claude-sonnet-5 | n/a | 0 | 0 | 0 | 5017 | 3945 | 96 |
| 26_08_11_09_org-destroy-logic | 1 | claude | claude-sonnet-5 | 2.8338 | 66 | 4641707 | 96070 | n/a | n/a | 91 |
| 26_08_13_09_bot-war-declare | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 1478 |
| 26_08_13_09_bot-war-eval | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 1478 |
| 26_08_13_09_bot-war-infra | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 1478 |
| 26_08_13_09_bot-war-observation | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 1478 |
| 26_08_13_09_bot-war-prosecute | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 1478 |
| 26_08_13_09_bot-war-unlock | 2 | cursor | cursor-grok-4.5-high | n/a | 0 | 0 | 0 | n/a | n/a | 1478 |

## Meta Stats

- **Specs count:** 88 (no previous milestone)
- **Specs missing an `implement` row:** 29 (no previous milestone) (see Coverage gap note above)
- **Total rows:** 258 (62 carry `cost_usd`)
- **Total cost (priced rows only):** $163.23 (no previous milestone)
- **Total tokens:** input 38,819 (no previous milestone), cached 494,423,158 (no previous milestone), output 1,951,742 (no previous milestone)
- **Provider share (by row count):**
  - claude: 193 (74.8%)
  - cursor: 58 (22.5%)
  - codex: 7 (2.7%)
- **Model share (by row count):**
  - claude-sonnet-5: 143 (55.4%)
  - cursor-grok-4.5-high: 58 (22.5%)
  - claude-sonnet-4-6: 41 (15.9%)
  - claude-fable-5: 6 (2.3%)
  - gpt-5.6-sol: 6 (2.3%)
  - fable 5: 2 (0.8%)
  - <synthetic>: 1 (0.4%)
  - gpt-5: 1 (0.4%)
- **Spec size (tokens):** min 570, max 7479, avg 2881.1 (no previous milestone) (n=77)
- **Plan size (tokens):** min 296, max 17632, avg 5519.4 (no previous milestone) (n=76)
- **Diff lines (final, per spec):** min 0, max 86567, avg 3320.4 (no previous milestone) (n=86)
- **Avg cost per stage (priced rows only):**
  - implement: $12.7454 (no previous milestone)/spec (total $127.45)
  - plan: $4.3400 (no previous milestone)/spec (total $21.70)
  - spec: $4.6912 (no previous milestone)/spec (total $14.07)

<!-- milestone-stats-dev: {"diff_lines": {"avg": 3320.3837209302324, "max": 86567, "min": 0, "n": 86}, "model_rows": {"<synthetic>": 1, "claude-fable-5": 6, "claude-sonnet-4-6": 41, "claude-sonnet-5": 143, "cursor-grok-4.5-high": 58, "fable 5": 2, "gpt-5": 1, "gpt-5.6-sol": 6}, "plan_size_tokens": {"avg": 5519.4078947368425, "max": 17632, "min": 296, "n": 76}, "priced_rows": 62, "provider_rows": {"claude": 193, "codex": 7, "cursor": 58}, "spec_size_tokens": {"avg": 2881.051948051948, "max": 7479, "min": 570, "n": 77}, "specs_count": 88, "specs_missing_implement": 29, "stage_avg_cost_usd": {"implement": 12.7454, "plan": 4.34, "spec": 4.6912}, "stage_total_cost_usd": {"implement": 127.4542, "plan": 21.7, "spec": 14.0736}, "total_cached_input_tokens": 494423158, "total_cost_usd": 163.2278, "total_input_tokens": 38819, "total_output_tokens": 1951742, "total_rows": 258} -->
