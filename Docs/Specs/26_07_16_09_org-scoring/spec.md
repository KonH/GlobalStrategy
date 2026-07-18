# Spec: Org Scoring

## Feature Intent

As a developer building toward the bot-controlled-organization initiative, I want each organization's score computed as the sum, over all countries, of the org's control fraction in that country multiplied by that country's population-derived `CountryScore`, so that grip on a high-population country is worth proportionally more than grip on a micro-state, and org performance becomes a comparable value — a prerequisite for evaluating bot efficiency once multi-org and eval-harness work lands, not the other way around.

This feature is deliberately self-contained and buildable against the current single-org codebase: it is a pure derived query over whatever `Organization`/`ControlEffect`/`CountryScore` state currently exists, with no dependency on multiple orgs, a headless runner, or any bot infrastructure. A future multi-org/eval-harness feature depends on this one — you cannot judge bot efficiency without a score to compare orgs by — never the reverse.

## Dependency

This feature depends on the already-merged `Docs/Specs/26_07_14_09_country-scoring/spec.md`/`CountryScoreSystem` (PR #16, on `main`): `CountryScore = countryScoreCoefficient × sum(population of provinces the country currently owns)`, recomputed at month boundaries plus forced recomputes at init/load. This feature reads `CountryScoreSystem`'s already-computed values; it does not re-derive population aggregation or introduce a second scoring coefficient.

It also depends on `Docs/Specs/26_07_16_15_score-component-composition/plan.md` (the `Country + Score` composition refactor) as an implementation-detail prerequisite — this feature's efficient single-pass read of all country scores should query whatever shape `CountryScoreSystem` stores them in at implementation time, so implementing plan 26_07_16_15_score-component-composition first avoids writing throwaway code against the pre-refactor `CountryScore { CountryId, Value }` shape.

It has **no dependency on multi-org support, a headless runner, or any bot-org infrastructure.** It computes a score for whatever `Organization` entities currently exist in the world (today exactly one); if/when a future multi-org feature creates more, this feature's logic requires no changes, since it is parameterized by `orgId` and reads world state fresh on every call.

## Acceptance Criteria

- **Given** an org with one or more `ControlEffect` entities across any number of countries, and each of those countries' already-computed `CountryScore` **When** the org's score is computed **Then** it equals `Σ over countries of (the org's ControlEffect total in that country ÷ 100) × that country's CountryScore.Value` — the org's control fraction in each country (its share of the 100-point per-country control cap enforced by `ControlSystem`) weighted by that country's population-derived score.
- **Given** an org with multiple separate `ControlEffect` entities in the same country (e.g. a base effect and a permanent effect) **When** the org's control fraction in that country is computed **Then** those entities' `Value`s are summed first, then the combined total is divided by 100 and multiplied by that country's `CountryScore` — not weighted separately per effect.
- **Given** an org with zero `ControlEffect` entities anywhere **When** its score is computed **Then** the result is `0.0`, not an error or a missing value.
- **Given** an org that holds control only in countries whose `CountryScore.Value` is `0` (e.g. countries with no population-contributing provinces, or not yet scored) **When** its score is computed **Then** the result is `0.0` for that contribution — a nonzero control fraction times a zero country score contributes nothing, and the overall function still returns cleanly rather than throwing.
- **Given** this feature's formula **Then** it lives in exactly one derived, non-`[Savable]` query — `OrgScore.GetScore(IReadOnlyWorld world, string orgId)` in `src/Game.Systems`, alongside `CountryScoreSystem` — that materializes no new ECS component and is the **only** place in the codebase that knows the org-score formula. Any future consumer (a headless runner, an eval harness, a UI) treats the result as an opaque comparable `double` rather than re-deriving it.
- **Given** this query reads world state directly on every call **Then** it is deterministic — a pure function of the world's current `ControlEffect` and `CountryScore` state, with no hidden mutable state, no randomness, and no wall-clock dependency.
- **Given** the query needs to read every country's score to compute even one org's total **Then** the implementation reads all country scores in a single pass (e.g. into a local `countryId -> value` lookup) rather than calling a per-country linear-scan lookup once per country the org holds control in, to avoid quadratic-in-country-count behavior.
- **Given** a future multi-org, headless-runner, or eval-harness feature needs per-org score **When** that feature is designed **Then** it calls `OrgScore.GetScore(world, orgId)` directly — this feature's public API is the intended integration point, so no changes to this feature should be required merely because a consumer feature is later built on top of it.

## Out of Scope

- **An ECS `OrgScore` component, `VisualState` exposure, or any in-game/UI display of org score.** This feature is a derived query only; materializing a component or showing score in-game is future work, if ever pursued, and must follow spec 26_07_14_09_country-scoring's `CountryScore` patterns rather than this feature's.
- Any change to `ControlEffect`, `ControlSystem`, `CountryScoreSystem`, or `ResourceSystem` — this feature only reads their already-computed/existing values.
- A second scoring coefficient. The only tunable in this formula is `countryScoreCoefficient` (already defined for country scoring); org score does not introduce its own coefficient.
- Any consumer of the score — a headless simulation runner and its results JSON, a bot/eval harness, a UI leaderboard, win/loss conditions, or bot/AI decision-making. This feature only computes and exposes `OrgScore.GetScore`; consumption is a separate, deliberately deferred future feature that depends on this one.
- Multi-org world initialization, seeded RNG, or a headless console runner — those belong to a future multi-org feature that will consume this one's `OrgScore.GetScore`, not the reverse.
- The bot decision logic, eval harness, paired-seed statistics, or parameter/genetic search of any future bot-org initiative.
- Per-country breakdown of an org's score — only a single aggregate `double` per org is in scope.
- Historical score tracking (a score-over-time timeline/sample history) — a future consumer's responsibility to sample `OrgScore.GetScore` over time, not this feature's to provide.
