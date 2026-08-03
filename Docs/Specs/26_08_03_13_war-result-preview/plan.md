# Plan: War Result Preview

## Spec

Source: `Docs/Specs/26_08_03_13_war-result-preview/spec.md` (owner clarifications locked).

As a player viewing a Declare War or Revenge country action card, show a win-probability badge (1–99%) on the card front before starting the war, so combat strength (recruits × damage vs enemy durability) is readable at a glance from the attacker country’s perspective.

Acceptance criteria (condensed):
- **Declare War / Revenge hand fronts** — badge on the art area, immediately below the header, right-aligned; integer percent in `[1, 99]`; higher = more likely attacker wins.
- **Other country actions** — no badge (Make Friend, Sell Arms, Ultimatum, etc.).
- **Live refresh** — percent rebinds when recruits / damage / durability change; not frozen at draw.
- **Equal strengths** — show `50%`.
- **Deck pile** — face-down backs need no badge.
- **Draw / play transitions** — animated front copies carry the same badge + current percent as the hand card.
- **Unplayable copies** — badge still shown; dims with existing `.action-card--unavailable` opacity (no badge-only override).

Out of scope: real war / peace math changes, Monte-Carlo, org/character cards, bots consuming the percent, redesigning card chrome beyond the badge.

## Goal

Project an optional `WarWinChancePercent` on country `ActionCardEntry` for `declare_war` / `revenge`, estimate it with a pure ECS helper mirroring live combat inputs (plus pending revenge bonuses), and render a green/yellow/red circle badge on every country card front rebuild path (hand + transitions).

## Approach

### Formula (locked)

```
ε = 1.0   // preview-only floor; real Strike throws if durability <= 0
sideStrength = recruits × damage / max(enemyDurability, ε)
winFraction  = attackerStrength / (attackerStrength + defenderStrength)
percent      = clamp(round(winFraction × 100), 1, 99)
```

Edge cases:
- Attacker recruits `== 0` → return **1** immediately (even if defender also 0). Short-circuit before the ratio.
- Equal positive strengths → **50**.
- Attacker recruits `> 0` but both strengths somehow `0` after the formula → treat as **50**.
- Percent never `0` or `100` — always clamp to `[1, 99]`.

Inputs (locked):
- Recruits / damage / durability = live resources via `ResourceQuery.GetValue` (`ResourceDefinitions.Recruits` / `Damage` / `Durability`). Missing → `0` (existing `ResourceQuery` default). Live damage already includes Sell Arms / `TroopsDamageBonusPercent` and any active `RevengeWarBonus` (DamageCollector / DurabilityCollector stacking).
- Revenge preview: pending percents from `DeclareRevengeWarEffectParams` model the bonus **as if the war had already started**. Play-time does `RevengeWarBonusQuery.RemoveForCountry` then adds the config bonus — preview must mirror that **replace**, not stack and not “skip if live present”:
  - When pending percent `> 0`: `effective = live / (1 + liveRevengePercent/100) * (1 + pendingPercent/100)` for attacker damage and durability (`RevengeWarBonusQuery.GetBonusPercent`; live factor `0` → no-op divide).
  - When pending is `0` (`declare_war`): use live resources unchanged (residual revenge still applies in combat).
  Prefer: VisualStateConverter resolves pending percents from EffectConfig and passes them into the estimator; estimator owns the replace math via `RevengeWarBonusQuery` and stays free of the action→effect graph.

### Layers

| Layer | Change |
|---|---|
| `Game.Systems` | New static `WarWinChanceEstimator.EstimateAttackerWinPercent(world, attackerId, defenderId, pendingAttackerDamageBonusPercent = 0, pendingAttackerDurabilityBonusPercent = 0) → int` |
| `Game.Main` | `ActionCardEntry.WarWinChancePercent` (`int?`, null = no badge); `StateEquality.ActionCardEntryEquals`; `VisualStateConverter.BuildEntry` calls estimator only for `declare_war` / `revenge` when `targetCountryId` non-empty. Thread `EffectConfig` into converter ctor (GameLogic already loads it) to resolve revenge pending bonuses from the action’s effect ids. |
| Unity UI | `ActionCardBuilder.Populate` / `Build` / `PopulateSlot` optional `int? warWinChancePercent`; Label inside `.action-card-art`, classes `.action-card-war-win-chance` + `--low/--mid/--high`. Bind from `CountryActionsView.BuildHandCard`. Thread through `CardTransitionView.ShowCountry` and `CardPlayAnimator.PopulateCountryTestCard` + all `ShowCountry` call sites. |
| USS | New styles in `Assets/UI/Overlay/OrgInfo/OrgActions.uss` — do **not** overload legacy `.action-card-success-pct`. |

### UI badge (assumed defaults — pending plan approval)

- Circle via `border-radius: 50%`; white bold text (Cinzel-Bold SDF already used on cards) + `-unity-text-outline-width/color` (same pattern as FlyText).
- Color bands by percent: **1–33** `--low` (red), **34–66** `--mid` (yellow), **67–99** `--high` (green).
- Absolute top-right inside `.action-card-art` (visually under the header bar).
- Text: `{n}%` (numeric only — no new locale key required).
- Unplayable: inherit parent `.action-card--unavailable` opacity `0.55`; no separate badge opacity.

### Transition threading

`PlayCountrySequence` today only receives `actionId` / `targetCountryId` / `clickedCard` — not an `ActionCardEntry`. At the **start** of the sequence (before `PlayCardActionCommand`), look up the matching hand entry in `_state.SelectedCountry.CountryActions.Hand` by `actionId` + `targetCountryId` and **capture** `WarWinChancePercent` into a local. Pass that captured value into `PopulateCountryTestCard` and both played-card `ShowCountry(...)` calls (hand→test, test→deck). For the replacement-draw transition, read percent from the new hand `ActionCardEntry` the same way `newActionId` / `newTargetCountryId` are read today.

## Agent Steps

- [x] **Add `WarWinChanceEstimator`** — `src/Game.Systems/WarWinChanceEstimator.cs`: pure static helper implementing the locked formula + edge cases; when a pending percent `> 0`, apply **replace** on attacker damage/durability (divide out live `RevengeWarBonusQuery` factor, then multiply pending); when pending is `0`, use live resources as-is.

- [x] **Extend `ActionCardEntry` + equality** — `src/Game.Main/VisualState.cs`: add `int? WarWinChancePercent` (default null); extend ctor. `StateEquality.ActionCardEntryEquals`: include the new field so hand rebinds when the estimate changes.

- [x] **Wire `EffectConfig` into `VisualStateConverter`** — Add optional `EffectConfig?` ctor param; pass `EffectConfig` / `_effectConfig` from `GameLogic` (already loaded). Existing test `new VisualStateConverter(...)` call sites keep compiling with the default `null` (revenge pending stays `0, 0` in those tests unless they pass a config).

- [x] **Project percent in `VisualStateConverter.BuildEntry`** — When `actionId` is `declare_war` or `revenge` and `targetCountryId` is non-empty, call `WarWinChanceEstimator` with attacker = selected `countryId`, defender = `targetCountryId`. For `revenge`, resolve pending bonuses by walking `_actionConfig.Find(actionId).EffectIds` → `_effectConfig.Find` → first `DeclareRevengeWarEffectParams` (do not hardcode effect id); for `declare_war`, pass `0, 0`. All other actions leave `WarWinChancePercent` null. Deck backs need not render a badge even if a deck `ActionCardEntry` carries a percent.

- [x] **Extend `ActionCardBuilder`** — Optional `int? warWinChancePercent` on `Build` / `PopulateSlot` / `Populate`. When present, add a `Label` as sibling of `.action-card-art-image` inside `.action-card-art` with text `{n}%`, base class `.action-card-war-win-chance`, and band modifier from the locked bands. When null, omit the label.

- [x] **Add USS for the badge** — `Assets/UI/Overlay/OrgInfo/OrgActions.uss`: circle, absolute top-right, Cinzel-Bold, white + outline, `--low/--mid/--high` backgrounds. Leave `.action-card-success-pct` unused/untouched.

- [x] **Bind hand cards** — `CountryActionsView.BuildHandCard`: pass `card.WarWinChancePercent` into `ActionCardBuilder.Build`.

- [x] **Thread transitions** — Extend `CardTransitionView.ShowCountry` and `CardPlayAnimator.PopulateCountryTestCard` (+ all country `ShowCountry` call sites) with `int? warWinChancePercent`. In `PlayCountrySequence`, capture the playing card’s percent from hand **before** `PlayCardActionCommand`, pass it through play→test / test→deck; pass the new hand entry’s percent on deck→hand draw.

- [x] **Add / update tests** — see Tests below.

## User Steps

### 1. Visual check in Unity Editor

Open a play session, select a country with Declare War and/or Revenge in hand (playable and unplayable). Confirm badge placement (art, top-right under header), band colors, unplayable dimming via card opacity, absence on other action types and on the face-down deck pile, and that draw/play transition fronts keep the badge. Spot-check that equal-ish forces show ~50% and a zero-recruit attacker shows 1%.

## Tests

- **New `WarWinChanceEstimatorTests.cs`** (`src/Game.Tests/`):
  - Equal strengths → `50`.
  - Attacker recruits `0` → `1` (including both sides at 0 recruits).
  - Strong attacker (high recruits × damage vs weak defender) → high percent in the green band; clamp never exceeds `99` or drops below `1`.
  - Revenge pending bonuses shift percent upward vs the same live inputs with `0, 0` pending.
  - Live residual `RevengeWarBonus` (e.g. decayed 5%/2.5%) + pending 10%/5% matches replace (same as stripping residual then applying pending) — not stack, not “ignore pending because live exists”.
  - Missing country resources (`ResourceQuery` → 0) behave safely (no throw; follow edge-case rules).
  - Both strengths `0` with attacker recruits `> 0` → `50`.
- **`ActionCardEntry` / equality** — if any existing VisualState / StateEquality tests construct `ActionCardEntry`, update ctors; add a focused equality case that differing `WarWinChancePercent` is not equal (so UI refresh is covered). Prefer pure C#; no Unity EditMode UI tests unless an `ActionCardBuilder` test pattern already exists (it does not today).

## Constitution Check

No conflicts found — plan aligns with all principles.

- **ECS for game logic:** estimation lives in `src/Game.Systems`; UI only reads projected `VisualState`.
- **UI Toolkit only:** badge is USS + UI Toolkit `Label` via existing builder; no Canvas/UGUI.
- **VContainer:** no new services or mutable singletons; EffectConfig threaded through existing GameLogic → VisualStateConverter construction.
- **Spec before plan / Docs/Specs organisation:** this plan sits beside the approved spec in `Docs/Specs/26_08_03_13_war-result-preview/`.
- **C# style:** new helper follows project conventions (tabs, braces, static pure API).

Use the implement skill to start working on the plan or request changes.
