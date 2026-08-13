# Spec: Bot War Observation Extensions (Child A)

## Feature Intent

As a developer building score-aware war bot features (`warUnlock` / `warDeclare` / `warProsecute`), I want `IBotObservation` to expose seat-useful war, relation, progress, occupation, score, destroy, and combat-input signals that are missing today, so those features can estimate Δorg_score (including occupation + enemy control decrease + gold) and call `WarWinChanceEstimator` without privileged foresight or raw `World` access.

This is **Child A** of umbrella `Docs/Specs/26_08_01_09_bot-war-features/` (issue #83). It is a prerequisite for children B–F and is **not** an `/implement-bot-feature` carve-out — observation / view / `BotObservation.Build` changes require `/specify` + `/plan` (this folder).

**Locked from umbrella:** `TargetCountryId` is **already** on `BotCardView` / `BotCardDrawChoiceView` for relation and revenge cards — do **not** re-specify or redesign that field; only fill remaining gaps.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`.

- Seat-useful war / relation / score / occupation signals needed for score-aware war are missing from `IBotObservation` today (wars, rivals, progress, occupation, CountryScore / org_score contributions, destroyed flag, combat inputs for win %).
  - Observation is extended so a bot can, for countries it can act on, know war participation + opponent when knowable, own-side progress, rival targets, score-facing signals, **full per-country occupied-province counts** for both war sides, and destroyed state => war features can estimate Δorg_score (including occupation + enemy control decrease + gold) and call `WarWinChanceEstimator` without privileged foresight.
- `TargetCountryId` is already present on card / draw-choice views for relation and revenge cards.
  - This child does not change that contract; implementers only fill the remaining observation gaps listed above.
- Any `IBotObservation` / views / `BotObservation.Build` change requires its own `/specify` + `/plan`.
  - This spec + paired `plan.md` satisfy Constitution Planning / Specification Discipline for Child A.

## Out of Scope

- Children B–F (decision interval / shared discard / draw scoring / arbitration; `warUnlock`; `warDeclare`; `warProsecute`; eval packages) — they **consume** these fields but are not implemented here.
- Bot play of `force_war_win` / `force_war_loss` or any war-feature `IBotFeature` logic.
- New war gameplay, action ids, peace formulas, or config rebalance.
- Privileged bot-only information beyond seat-visible / public systems (leaderboard org scores, country scores, wars, relations, occupation, combat resources already readable for that seat’s countries).
- Re-specifying or changing `TargetCountryId`.
- `/implement-bot-feature` as a substitute for this observation plan.

## Parent / Dependency

- **Umbrella:** `Docs/Specs/26_08_01_09_bot-war-features/spec.md` (Child A + Tech Notes).
- **Downstream:** B–F need Child A observation fields before they can estimate Δorg_score or win % correctly.
- **Standing API:** `Docs/Specs/26_07_16_14_bot-org-api/` — extend the existing read-only facade; keep information-hiding and deterministic ordering rules.
