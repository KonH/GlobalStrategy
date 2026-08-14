# Plan: Bot War Infrastructure (Child B)

## Spec

Source: `Docs/Specs/26_08_13_09_bot-war-infra/spec.md` (thin child of umbrella `Docs/Specs/26_08_01_09_bot-war-features/`, issue #83 Child B).

**Intent.** Replace once-per-calendar-day strategic gating and control-only draw/discard with a shared **`botDecisionIntervalHours` (default 4)** decision cycle: draw and/or discard (0+ unbounded until useful or blocked), then **at most one** strategic play chosen by **global best estimated Δorg_score** among feature proposals (unlocks in the same pool; revenge soft-weighted `Δ × 1.20`). Ship shared discard + intent-aware 1-of-3 draw scoring so Children C–E can propose scored war plays without owning cadence.

**Hard dependency / sibling.** Child A (`Docs/Specs/26_08_13_09_bot-war-observation/`) supplies CountryScore / org_score / war / occupation fields for **real** Δorg_score. This plan defines proposal + arbitration contracts so A fields plug in; if A is not merged yet, features use documented fallback estimates. Do **not** implement war features C/D/E here.

**Key acceptance criteria (design targets):**
- Root `Assets/Configs/game_settings.json` + `GameSettings.BotDecisionIntervalHours` default **4**; Bot gated on elapsed in-game hours, not `.Date`.
- One open cycle per interval: acquisition (shared draw scorer) ↔ discard (shared helper, unbounded) ↔ **≤1** play from arbitration; same-tick play when no mid-pipeline wait is required.
- Features **propose** scored plays; Bot arbitrates and emits the single winner through the existing sink.
- `ControlFeature` no longer owns discard exclusively — discard moves to orchestrator + shared helper (Control may call the helper only if retained as a thin wrapper; prefer orchestrator-owned).
- Revenge soft weight lives in B’s arbitration helper even before Child D ships.

**Out of scope:** C/D/E feature logic, F eval packages, force cards, observation Build field work (A), action-economy retunes.

## Goal

Ship bot **infra only** in `src/Game.Bots` (+ `GameSettings` / root `game_settings.json` wiring): interval cadence, `IBotFeature` proposal collection, Δorg_score arbitration (with revenge `× 1.20`), shared discard helper, shared draw intent scorer, and migrate `ControlFeature` / `BaselineCardPlayFeature` + existing day-gating / acquisition tests — so later war features only register proposals into an already-correct cycle.

## Approach

### 1. Config: `botDecisionIntervalHours`

**`Assets/Configs/game_settings.json`** — add a root-level key beside the existing bot knobs (`botActionLogRetentionCap`, `maxControlPool`, `botFeatures`), not nested under `botFeatures` or `featureFlags`:

```json
"botDecisionIntervalHours": 4,
```

**`src/Game.Configs/GameSettings.cs`** — add:

```csharp
public double BotDecisionIntervalHours { get; set; } = 4;
```

Validate at use sites / Bot construction: value must be `> 0` (throw or clamp with fail-fast — prefer fail-fast `InvalidOperationException` when ≤ 0, matching other GameSettings validators’ spirit).

**Wiring:** `BotSession.AttachBot` already reads `_logic.BotFeatures` / `MaxControlPool` from `GameLogic`. Pass `_logic.GameSettings.BotDecisionIntervalHours` (and optionally `DiscardGoldCost` only if the helper needs an affordability pre-check — today discard affordability is enforced by `DiscardCardSystem`; keep that posture unless tests require an early gold gate) into `Bot`’s constructor as an optional/required parameter so headless, Unity, and eval hosts pick up the JSON value without a second config path.

### 2. Decision-cycle state machine (`Bot.cs`)

**Replace** `_lastActedDate` (calendar-day compare) with interval state:

| Field | Role |
|-------|------|
| `_lastCycleCompletedAt` (`DateTime?`) | Timestamp when the last cycle finished (play, or exhausted discard/blocked with nothing useful). Next cycle opens when `(currentDate - completed).TotalHours >= botDecisionIntervalHours`, or immediately if null. |
| `_cycleOpen` (`bool`) | True while mid-cycle work may span frames (pending draw/receive after discard, stalled acquisition fallthrough, etc.). |
| `_pendingAcquisitionSince` | Keep stall detection; re-base from **hours** not calendar days (see below). |

**Remove** the early-out that treats “already acted today” as done. New early-out:

1. If **no** open cycle **and** interval not elapsed → return without rebuilding observation (preserve today’s perf skip when idle).
2. If interval elapsed → open cycle (`_cycleOpen = true`).
3. If open cycle → always rebuild observation and continue the phase machine.

**Per `ExecuteDecisionTick` while `_cycleOpen`:**

```
BeginDecisionPhase + Build observation
→ TryAcquireCountryCard (shared draw scorer)
    if acquisition emitted a command and not stalled → return (wait for apply; cycle stays open)
→ CollectProposals from every feature into one list
→ best = BotPlayArbitration.SelectBest(proposals)  // raw Δ; revenge applies ×1.20 inside
→ if best is suitable (arbitration score > 0):
      emit exactly one Play* via sink; CurrentFeatureId = winner.FeatureId for emission log
      complete cycle (_cycleOpen=false, _lastCycleCompletedAt=currentDate); return
→ else if BotDiscardHelper.TryDiscard(...):
      // unbounded: leave cycle open; next tick may acquire into freed capacity / re-score
      return (or loop discard+re-collect on same tick only if observation remains valid —
               prefer return-after-one-discard when sink commands need Update to apply)
→ else:
      // blocked: cannot improve hand / no positive proposal
      complete cycle; return
```

**Same-tick play OK:** when there is no pending acquisition work and a suitable proposal exists before any discard, collect → play → complete on **that** tick. Multi-frame only when draw/receive/discard commands must hit `logic.Update` before the next observation is truthful (same constraint as today’s acquisition early-return).

**Acquisition stall:** replace `AcquisitionStallDayLimit = 3` days with hours, e.g. `botDecisionIntervalHours * 3` (default 12h) **or** fixed `72` hours to preserve the old “~3 days” magnitude. Prefer **`Math.Max(botDecisionIntervalHours * 3, 24)`** so short intervals still stall-fallthrough within a day. On stall, fall through to proposals/discard instead of freezing strategic play (preserve today’s intent).

**Emission / `CurrentFeatureId`:** today set around `feature.Tick`. After arbitration, set `CurrentFeatureId` to the **winning proposal’s** `FeatureId` for the single play emit (and clear in `finally`), so `BotSession`’s action log still attributes correctly. Discards and draw/receive are host/orchestrator actions — keep `CurrentFeatureId == ""` for those (matches today’s acquisition path).

### 3. Feature contract: proposals instead of immediate Tick plays

Today `IBotFeature.Tick` mutates via sink directly; profile order and “first feature that plays” dominate. Child B needs **scoring coexistence**.

**Change `IBotFeature` to:**

```csharp
public interface IBotFeature {
	string FeatureId { get; }
	// Append zero or more scored play candidates. Must not Play*/Discard* on the sink.
	void CollectProposals(IBotObservation observation, IList<BotPlayProposal> proposals, Random rng);
}
```

**Delete `Tick`** (update all implementers + test doubles). This is authorized by this plan (infra outside the `/implement-bot-feature` carve-out). Document on the interface: features only propose; Bot arbitrates and plays at most one per cycle.

**`BotPlayProposal`** (new sealed DTO in `Game.Bots`):

```csharp
public sealed class BotPlayProposal {
	public string FeatureId = "";
	public string ActionId = "";
	public string CountryId = "";          // "" => org card → PlayOrgCard
	public string TargetCountryId = "";
	public int SlotIndex;
	public double EstimatedDeltaOrgScore;  // raw expected Δorg_score BEFORE revenge / target bias
	public bool ApplyRevengeSoftWeight;    // true for declare_revenge_war candidates (Child D)
	public double TargetBiasMultiplier = 1.0; // soft enemy-higher / no-bot-control bias (Child D; C/E may use)
}
```

**`BotPlayArbitration`** (static helper owned by B):

```csharp
public static class BotPlayArbitration {
	public const double RevengeSoftWeight = 1.20; // umbrella lock; Child D may retune later via owner

	public static double ArbitrationScore(BotPlayProposal p) {
		double m = p.TargetBiasMultiplier > 0 ? p.TargetBiasMultiplier : 1.0;
		double raw = p.EstimatedDeltaOrgScore * m;
		return p.ApplyRevengeSoftWeight ? raw * RevengeSoftWeight : raw;
	}

	// Suitable = ArbitrationScore > 0. Ties: FeatureId ordinal, then ActionId, CountryId,
	// TargetCountryId, SlotIndex — all StringComparer.Ordinal / numeric — for determinism.
	// Hard gate for war features still uses raw EstimatedDeltaOrgScore > 0 before proposing;
	// TargetBiasMultiplier / revenge weight affect ranking only via ArbitrationScore.
	public static BotPlayProposal? SelectBest(IReadOnlyList<BotPlayProposal> proposals);
}
```

Child D only sets `ApplyRevengeSoftWeight = true` on revenge proposals and fills `TargetBiasMultiplier`; B already ranks them. No war feature code in this plan.

**Shared war profit helper (contract only; body in D):** Children C/D/E must use one type name — `WarPeaceOrgScoreEstimator` in `Game.Bots`. **D implements** the full dual-side helper; **C and E only call it**. C must not land a temporary `WarPathProfitEstimator` fork. Implement order after B: **D first** (helper + win% overload + `warDeclare`), then **C and E in parallel**, then F packages.

### 4. Child A plug-in contract (estimates)

Arbitration consumes **`EstimatedDeltaOrgScore` numbers features compute**. Real inputs come from Child A. Until A merges, implement **fallback** estimators and name the expected observation fields:

| Consumer | Prefers from A (when present) | Fallback until A |
|----------|-------------------------------|------------------|
| `ControlFeature` | `BotCountryView.CountryScore` (and control-gain magnitude if exposed); Δ ≈ `(controlDelta/100) * CountryScore` − gold opportunity if modeled | Any playable `RaisesControl` card: positive proxy e.g. `MaxControlPool - MyControl` (or `1.0` minimum) so control still proposes and wins when alone |
| Future war features | Wars, rivals, occupation counts, org_score, destroy, combat inputs per Child A spec | Out of scope here — they must not ship without A |

Do **not** implement Child A `BotObservation.Build` fields in this plan. Optionally use reflection-free duck typing only via **additive properties once A lands**; until then Control’s fallback stays compile-clean against today’s `IBotObservation` / `BotCountryView`. When A adds `CountryScore`, a follow-up one-liner in Control (same PR if A already merged, else tiny follow-up) switches to the real formula — call that out in Agent Steps as “if A fields exist on the branch, use them; else fallback.”

**Suitable play:** `ArbitrationScore > 0` (umbrella: net Δorg_score > 0). Non-positive proposals are ignored by `SelectBest`.

### 5. Migrate `ControlFeature` / `BaselineCardPlayFeature`

**`ControlFeature`:**
- Implement `CollectProposals`: scan countries/hands as today’s `TryPlayControl` does; for each playable `RaisesControl` card append a `BotPlayProposal` with fallback/real Δ; do **not** call sink.
- **Remove** in-feature discard from the decision path. Delete `TryDiscardForBetterHand` **or** reduce it to a one-line wrapper over `BotDiscardHelper` only if some unit test still calls it — prefer deletion and cover discard via Bot/orchestrator tests.
- Keep `_maxControlPool` skip for full countries.

**`BaselineCardPlayFeature`:**
- `CollectProposals`: first eligible card under `minGoldReserve` as today, but emit one proposal with a small positive Δ proxy (e.g. `1.0`) or gold-aware heuristic — enough that baseline still acts when it is the only feature. Do not play via sink inside CollectProposals.
- Preserve scan order only as tie-breaker input (SlotIndex / country ordinal already in proposal fields); arbitration is the play gate.

**Test doubles** (`ScriptedPlayFeature`, throwing feature, etc.): switch to `CollectProposals` that append a proposal **or**, for orchestrator exception tests, throw from `CollectProposals`. Playing moves to Bot after selection — scripted tests that counted sink plays from Tick must assert after Bot runs arbitration (Bot will play the scripted proposal).

### 6. Shared discard helper

New `BotDiscardHelper` (static) in `src/Game.Bots/`:

```csharp
public static class BotDiscardHelper {
	// When CountryHandCount >= CountryHandCapacity, pick one physical card (collapse
	// per-country duplicate views by SlotIndex) with the lowest value score and
	// sink.DiscardCountryCard(...). Returns true if a discard was emitted.
	public static bool TryDiscardForBetterHand(
		IBotObservation obs,
		IBotCommandSink sink,
		Func<BotCardView, IBotObservation, double>? valueScore = null);
}
```

**Default value function** (when `valueScore` is null): prefer discarding cards that are **not** control-usable / raises-control / known war-intent action ids; among equals, lowest `SlotIndex` (preserves today’s ControlFeature deterministic collapse). Shared draw/intent tables should feed the same notion of “valuable” so discard and draw stay coherent.

**Orchestrator ownership:** Bot’s cycle calls this helper when no suitable proposal exists. Unbounded within the open cycle across ticks until a suitable proposal appears or discard is blocked (hand not full, no cards, or system rejects unaffordable discard — treat failed/no-op discard as blocked and complete the cycle to avoid infinite open cycles).

**Gold:** do not invent a new reserve check here; keep relying on `DiscardCardSystem` + `GameSettings.DiscardGoldCost` unless an existing test needs an early affordability short-circuit (`obs.Gold < DiscardGoldCost` → blocked). If early-check is added, thread `DiscardGoldCost` from GameSettings into Bot.

### 7. Shared draw intent scorer

Replace private `Bot.GetChoicePriority` with `BotDrawIntentScorer`:

```csharp
public static class BotDrawIntentScorer {
	// Higher is better. Used by Bot.TryAcquireCountryCard over CountryCardDrawChoices.
	public static double ScoreChoice(BotCardDrawChoiceView choice, IBotObservation obs);
}
```

**Ranking (locked extension of today’s order, not a product re-open):**
1. Strong control intent: `IsControlUsable` (highest band).
2. `RaisesControl`.
3. Known war / unlock action ids that C–E will own (`make_rival`, `improve_diplomacy_advisor_opinion`, `improve_military_advisor_opinion`, `improve_ruler_opinion`, `declare_war`, `declare_revenge_war`, `sell_arms`) — mid band so they beat generic playable junk when present in the 1-of-3 offer **even before those features register**, matching “intent-aware” acquisition. Include diplomacy opinion: Child C plays it when diplomacy &lt; 30 blocks `make_rival`.
4. Other `IsPlayable`.
5. Fallback.

Within a band, prefer higher estimated usefulness if cheap signals exist (e.g. `IsControlUsable` already); else lower `ChoiceIndex` for determinism (today’s tie-break).

Optional later extension (document only): `IBotDrawIntentContributor` on features — **not required for B** if the static war-id table covers acquisition intent for C–E.

### 8. What deliberately does **not** change

- `IBotCommandSink` whitelist / draw-receive-discard-play methods.
- `BotFeatureRegistry` registration shape (still factories → `IBotFeature`); no war feature registrations.
- Child A observation Build implementation.
- Eval configs / `hoursPerTick` locks (Child F).
- Unity MonoBehaviours, VContainer registrations, UI.

## Steps

### Agent Steps

- [ ] **Config surface** — Add `botDecisionIntervalHours: 4` to root `Assets/Configs/game_settings.json`; add `GameSettings.BotDecisionIntervalHours` default `4`; ensure JSON round-trip tests / `StringConfigParityTests` still pass (update expected property presence if any test snapshots full settings).

- [ ] **Proposal + arbitration types** — Add `BotPlayProposal.cs`, `BotPlayArbitration.cs` with `RevengeSoftWeight = 1.20`, `ArbitrationScore`, deterministic `SelectBest` (score `> 0` only).

- [ ] **Change `IBotFeature`** — Replace `Tick` with `CollectProposals(...)`; update `ControlFeature`, `BaselineCardPlayFeature`, and every test double in `src/Game.Tests`.

- [ ] **Shared helpers** — Add `BotDiscardHelper` and `BotDrawIntentScorer`; delete control-only `GetChoicePriority`; move ControlFeature discard to orchestrator (delete or thin-wrap private discard).

- [ ] **Rewrite `Bot.ExecuteDecisionTick`** — Interval gate + open-cycle state machine per Approach §2; wire `botDecisionIntervalHours` from constructor; acquisition uses draw scorer; after acquisition, collect → arbitrate → play-or-discard-or-complete; set `CurrentFeatureId` only around the winning play emit.

- [ ] **`BotSession` / call sites** — Pass `GameSettings.BotDecisionIntervalHours` into `new Bot(...)`; fix any other `new Bot(` test/helpers that need the new parameter (optional arg with default `4` is acceptable to limit churn).

- [ ] **Migrate Control / Baseline proposals** — Control: RaisesControl proposals with A-aware Δ when `CountryScore` exists, else fallback proxy; no sink plays/discards. Baseline: reserve-gated single proposal. Confirm default `control`-only profile still plays control cards under arbitration.

- [ ] **Update gating / acquisition tests** — Retarget `BotDayGatingTests` to interval hours (rename file/class OK): repeated calls within `< 4h` emit ≤1 strategic play; after advancing `≥ botDecisionIntervalHours` may act again; pre-init safety preserved. Update `BotCardAcquisitionTests` for new draw ranking (war-id mid-band). Add arbitration / discard-helper / revenge-weight unit tests (see Tests).

- [ ] **Run test suite** — `dotnet test` on `src/GlobalStrategy.Core.sln` (or project `dotnet-test` skill); fix fallout in Control/Baseline/orchestrator/emission tests.

- [ ] **Release build** — Run `/dotnet-build Release` (or `dotnet-build` skill) after `src/` changes; fix compile errors.

### User Steps

### 1. None

None — this child is `src/` + root `Assets/Configs/game_settings.json` only; no Unity Editor scene/asset work or visual inspection is required.

## Tests

- **`BotPlayArbitration` (new):** revenge proposal with raw Δ ties or loses to ordinary declare on raw Δ but wins after `× 1.20`; non-positive scores excluded; deterministic tie-break on equal scores.
- **`BotDiscardHelper` (new):** full hand → discards lowest-value / SlotIndex-stable card; non-full hand → no discard; duplicate per-country views collapse to one physical slot.
- **`BotDrawIntentScorer` / acquisition:** control-usable still beats raises-control; a `declare_war` / `sell_arms` / `make_rival` choice beats generic playable non-control when control-usable is absent; ChoiceIndex tie-break stable.
- **Interval gating (replace day tests):** multiple `ExecuteDecisionTick` within the same interval → at most one strategic play; after time advance ≥ configured hours → may play again; acquisition mid-cycle does not complete the cycle until play/block; unbounded discard leaves cycle open across ticks until useful or blocked.
- **Orchestrator:** two stub features proposing different Δ → only higher arbitration score is played; exactly one play command per completed cycle; `CurrentFeatureId` on emission matches winner; discard then later play still ≤1 play for that cycle.
- **ControlFeature:** CollectProposals only (no discard/play side effects); with only control enabled, Bot still plays a RaisesControl card when available.
- **Baseline:** minGoldReserve still suppresses proposals when gold too low.
- **Regression:** `BotOrchestratorTests` exception wrapping uses `CollectProposals`; determinism / session tests stay green.
- Full suite: `dotnet test src/GlobalStrategy.Core.sln`, then `/dotnet-build Release`.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts — plan aligns with all principles.**

- *Unity 6 + URP only.* No rendering/camera/shader work.
- *ECS for all game logic in `src/`.* Cadence/scoring live in `Game.Bots` orchestrator; mutation still only via existing command sink → ECS systems. No MonoBehaviour game logic.
- *VContainer sole DI.* No new Unity registrations; `BotSession` continues plain construction from `GameLogic` settings (same pattern as today).
- *UI Toolkit only.* No UI changes.
- *Plan before implement / Spec before plan.* Thin child `spec.md` accompanies this plan; umbrella specify already locked product decisions. **Not** using `/implement-bot-feature` — orchestrator / `IBotFeature` contract / config are outside that carve-out and correctly use `/specify`+`/plan`.
- *File organisation.* Lives at `Docs/Specs/26_08_13_09_bot-war-infra/` (timestamped folder, not legacy `Docs/Plans/`).
- *One `.asmdef` per `Assets/Scripts/` feature.* Untouched.
- *C# code style.* Tabs, `_` privates, braces always — match surrounding `Game.Bots` files.

**Carve-out note:** Changing `IBotFeature` from `Tick` → `CollectProposals` is an intentional, plan-authorized extension-contract evolution so Children C–E can compete by score; it is **not** sneaked in under the bot-feature carve-out.

Use the implement skill to start working on the plan or request changes.
