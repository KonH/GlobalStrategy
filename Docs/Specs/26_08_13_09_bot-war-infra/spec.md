# Spec: Bot War Infrastructure (Child B)

## Feature Intent

As a developer enabling score-competing bot features (`control` + future `warUnlock` / `warDeclare` / `warProsecute`), I want shared bot **decision cadence**, **cross-feature Δorg_score arbitration**, a **common discard helper**, and an **intent-aware 1-of-3 draw scorer**, so war and control coexist by scoring on a 4-hour cycle instead of once-per-calendar-day strategic ticks and control-only acquisition/discard paths.

This is **Child B** of umbrella `Docs/Specs/26_08_01_09_bot-war-features/` (issue #83). It is **not** an `/implement-bot-feature` carve-out — orchestrator / shared helpers / `IBotFeature` contract / `game_settings.json` changes require `/specify` + `/plan` (this folder).

**Locked from umbrella (owner 2026-08-02 + 2026-08-09 + 2026-08-12):**
- Root `game_settings.json` key `botDecisionIntervalHours` **default 4**.
- Every interval: **draw and/or discard (0+ unbounded until useful or blocked), then at most one** strategic play; same-tick play OK when state allows.
- Arbitration: **global best estimated Δorg_score** among `control` + war + unlock proposals (unlocks in the same pool).
- Revenge candidates: arbitration score `Δ × 1.20` (soft weight; helper owned here even if revenge feature ships in Child D).
- Shared discard helper (not only inside `ControlFeature`).
- Shared intent/value scorer for 1-of-3 draw (replaces/extends control-only `GetChoicePriority`).

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`.

- Strategic play is still gated once per calendar day via `_lastActedDate` in `Bot.cs`, and acquisition can run every update while pending.
  - Cadence becomes: every **`botDecisionIntervalHours` (default 4)** the bot may **draw and/or discard (0+ unbounded until useful or blocked), then play at most one** strategic card (same tick OK when state allows; later frame only when command/apply ordering requires) => bots are not limited to one strategic play per calendar day, and draw/discard are part of the same interval cycle.
- Multiple features (`control`, and later `warUnlock` / `warDeclare` / `warProsecute`) can each propose a play on the same decision cycle.
  - The bot selects among proposals by **global best estimated Δorg_score** (unlocks included in the same pool; revenge uses +20% soft weight) rather than fixed profile order alone => control and war coexist by scoring; **at most one** play wins the interval.
- Hand full, no suitable play from any feature.
  - A common discard helper runs with a **feature-neutral** (or shared weighted) value function => `ControlFeature.TryDiscardForBetterHand` is not the only discard path; `ControlFeature` moves to / calls the shared helper (or stops owning discard entirely once the orchestrator does).
- Pending 1-of-3 draw choices while war-capable cards appear in the offer.
  - Draw selection uses the shared intent/value scorer called from Bot acquisition (not only `IsControlUsable` → `RaisesControl` → `IsPlayable`) => war cards can win the offer when they better serve score.

## Out of Scope

- Children C–E war feature implementations (`warUnlock` / `warDeclare` / `warProsecute`) — they only **consume** this infra’s proposal / scoring / cadence contracts.
- Child A observation field implementation — B defines arbitration / proposal interfaces so A’s score / war / occupation fields can plug into Δorg_score estimates; stub or fallback estimates are allowed until A merges.
- Child F eval packages.
- Playing `force_war_win` / `force_war_loss`.
- New war gameplay, action ids, or config economics rebalance.
- Secondary eval opponents / changing locked eval knobs (Child F).

## Parent / Dependency

- **Umbrella:** `Docs/Specs/26_08_01_09_bot-war-features/spec.md` (Child B + Resolved Decisions + Tech Notes).
- **Sibling Child A:** `Docs/Specs/26_08_13_09_bot-war-observation/` — real Δorg_score inputs (CountryScore, occupation, wars, etc.). B may land with fallback estimates; war features need A.
- **Downstream:** C–E propose scored plays into B’s arbitration pool; F assumes `botDecisionIntervalHours` cadence.
- **Standing API:** `Docs/Specs/26_07_16_14_bot-org-api/` — extend orchestrator / feature contract; keep sink whitelist and information-hiding.
