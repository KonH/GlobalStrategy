# Ralph Stats

## Goal

To test different approaches for implementation - interactive session and autonomous work.

## Stats

| Stage | Price ($) | Acceptance |
| ----- | --------- | ---------- |
| [Specify + Plan + Deps](https://github.com/KonH/GlobalStrategy/tree/feature/province-population-spec) | 7.11 | PASS |
| [Manual Sample 1](https://github.com/KonH/GlobalStrategy/tree/feature/province-population-manual-sample-1) | 9.17 | PASS |
| [Manual Sample 2](https://github.com/KonH/GlobalStrategy/tree/feature/province-population-manual-sample-2) | 7.25 | PASS |
| [Manual Sample 3](https://github.com/KonH/GlobalStrategy/tree/feature/province-population-manual-sample-3) | 5.97 | PASS |
| **Manual Avg** | **7.46** | **PASS** |
| [Ralph Sample 1](https://github.com/KonH/GlobalStrategy/tree/feature/province-population-ralph-sample-1) | 33.33 | PASS |
| [Ralph Sample 2](https://github.com/KonH/GlobalStrategy/tree/feature/province-population-ralph-sample-2) | 54.14 | PASS |
| [Ralph Sample 3](https://github.com/KonH/GlobalStrategy/tree/feature/province-population-ralph-sample-3) | 76.31 | PASS |
| **Ralph Avg** | **54.59** | **PASS** |

## Comparison

Each of the 6 implementations was independently reviewed against the shared plan (diffed against the `feature/province-population-spec` baseline), checking correctness, test coverage, and code style.

| Branch | Price ($) | Review Score | Notable |
| ----- | --------- | ------------ | ------- |
| Manual Sample 3 | 5.97 | 10/10 | No bugs, no deviations; cleanest history (2 commits) |
| Manual Sample 2 | 7.25 | 9.5/10 | No bugs; verified full 186/186 test suite passes |
| Manual Sample 1 | 9.17 | 9/10 | No bugs; minor STJ-null nitpick only |
| Ralph Sample 1 | 33.33 | 9/10 | No bugs; noisy 40-commit history (stalled iterations) |
| Ralph Sample 2 | 54.14 | 9/10 | No bugs; 2 cosmetic test-naming deviations |
| Ralph Sample 3 | 76.31 | 9.5/10 | No bugs; 2 cosmetic test-naming deviations |

**Verdict:** Manual Sample 3 is the best implementation, and not close on any axis — it is the only one to score a clean 10/10 (every reviewer found the same zero-bug outcome across all six branches, including correct handling of the trickiest part of the spec: sampling density pre-simplify but computing final population from post-simplify geometry) and it is also the cheapest of all six.

All six implementations turned out near-identical in quality — correct, fully-tested implementations of the same plan, which tracks given a detailed plan.md was the shared input to each. The real delta is cost, not quality: the autonomous Ralph loop runs spent 7-13x more money to arrive at the same place as the manual runs (Manual Avg $7.46 vs Ralph Avg $54.59), with Ralph Sample 3 ($76.31) being the most expensive run of all six for a 9.5/10 that Manual Sample 3 matched-and-beat for $5.97.

### Why Ralph costs so much more than manual (7-13x)

Investigated via `scripts/ralph.py` and the `.ralph/activity.md` journals committed on each Ralph branch:

- **Every iteration is a brand-new process with zero memory.** `ralph.py` invokes `claude -p <prompt>` fresh for each loop iteration, not one continuous session. A manual interactive session builds context once (CLAUDE.md, the ~25 rule files, relevant source) and reuses it turn-to-turn via prompt caching. Ralph tears that down and rebuilds it from scratch dozens of times.
- **A hard floor on iteration count.** The harness enforces `MaxIterations >= task_count * 1.5`. These PRDs had 17-19 tasks, guaranteeing at least 26-29 full fresh-context invocations regardless of how trivial most tasks were (e.g. "add one enum case" still pays the full re-orientation cost).
- **The journal grows and gets re-read every iteration.** `.ralph/activity.md` is append-only and read for continuity each iteration. Ralph Sample 1's journal ballooned to 982 lines (vs ~300 for the other two) from 16 consecutive stalled iterations, each appending its own paragraph. One logged API call from iteration 14 shows 377,731 cache-read + 79,834 cache-creation tokens just to re-establish context for a no-op environment check.
- **Extra phases.** `/create-prd` and `/complete-prd` are themselves full separate `claude -p` invocations bolted onto the loop.

### Why the 3 Ralph runs differ from each other ($33 -> $54 -> $76)

Cost turned out to be inversely correlated with commit/iteration count, not proportional to it:

| Run | Commits | Cost | Driver |
| --- | ------- | ---- | ------ |
| Ralph Sample 1 | 38 | $33.33 | Most iterations, but mostly cheap ones |
| Ralph Sample 2 | 25 | $54.14 | Moderate, hit its own blocker |
| Ralph Sample 3 | 22 | $76.31 | Fewest iterations, but expensive ones |

- **Ralph Sample 1** stalled for 16 consecutive iterations (no Unity Editor connected, Node.js missing) and even hit a mid-run 429 "monthly spend limit" error. A stalled iteration just re-checks environment state and writes one journal line — from the logged data, that costs only ~$0.3-0.5 each. Lots of cheap junk iterations didn't blow up the total.
- **Ralph Sample 2** hit its own blocker (missing .NET 8 runtime, resolved 2 iterations later) — moderate friction, no Unity-heavy work.
- **Ralph Sample 3** is the outlier: its `/create-prd` run happened to promote the plan's two "User Steps" (manual Play-mode sanity checks) into real automated PRD tasks — something Sample 1 and Sample 2's PRDs left for a human and never touched. Executing them meant entering Play mode via Unity MCP and doing live ECS reflection from scratch: discovering the snapshot's JSON shape, learning `OwnerType` serializes as an int, hitting `record struct` property-vs-field reflection failures, resolving explicit-interface method names, hunting for the right assembly name — all trial-and-error inside two single "task" iterations. That's far more tool-calls/turns per iteration than a `dotnet build` gate, and it's why the run with the fewest iterations and cleanest history ended up the most expensive.

**Bottom line:** the cost spread across the three Ralph runs is mostly noise — how much the loop got stuck waiting on local environment availability (Unity/Node.js), and whether the auto-generated PRD happened to expand scope into live-Editor verification — not a difference in how much real work got done (all three scored 9-9.5/10). The structural 7x gap vs. manual sessions is the real signal: fresh-context-per-iteration is fundamentally more expensive than one continuous session, regardless of which Ralph run you pick.
