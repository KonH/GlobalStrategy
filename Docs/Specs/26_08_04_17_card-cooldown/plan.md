# Plan: Card Cooldown

## Spec

(Verbatim summary of `spec.md` — see that file for the full text.)

**Feature intent:** As a player (and as a bot-controlled organisation), playing a country action card puts every card of that same type on a shared cooldown for the playing organisation, so the org cannot spam-repeat the same kind of action (e.g. "Declare War") back-to-back regardless of target, hand slot, or fresh draw.

**Acceptance criteria:**
- A successful country-card play (org-owned cards, `ownerType: "org"`, are out of scope) starts a cooldown for that card's `ActionId`, scoped to the playing org, starting immediately on success.
- Cooldown duration is one configurable global default (initially 7 days), not tuned per card type.
- While on cooldown, every instance of that `ActionId` for that org — different target, a copy already in hand, or a freshly drawn copy — is unplayable, "same type" meaning same `ActionId` not same target instance.
- A freed hand slot may still draw a new copy of a cooldown'd type; it shows unplayable with the cooldown indicator immediately (`DrawCardSystem` needs no cooldown-aware skip).
- While on cooldown, the card face shows a radial, semi-transparent progress overlay (elapsed/remaining) paired with a numeric remaining-time label (reusing the "N days"/"N months"/"N years" formatting from the original country-action-cards feature).
- When the cooldown fully elapses, the card (and every other instance of that type for that org) becomes playable again on the next evaluation, with the overlay/label disappearing — no manual action required.
- Cooldown is strictly per `(OrgId, ActionId)` — one org playing a card never affects another org's copies of the same type.
- Bot card selection never picks a card on cooldown for its own org, enforced through the same shared `ActionPlayability.Evaluate`/`BotObservation.IsPlayable` gate used by the player UI and server-side validation — no bot-specific heuristic.
- A play attempted while on cooldown is rejected exactly like any other failed-gate play (no cost deducted, no effect applied, card stays where it was).

**Out of scope:** per-card-type cooldown tuning; any change to action success/failure rolls (none exist); cross-org cooldown sharing; reviving the old per-instance `ActionCooldown` design; a required shader/material decision being fixed here (this plan makes that call); Debug Card Availability panel changes; new `IBotFeature`/bot-specific cooldown code; multiplayer sync; **org-owned cards** (`UpdateOrgActions`/`OrgActionsView.cs` untouched, no tracking entities created for org plays).

## Verification against current codebase

Read in full and cross-checked against `spec.md`'s Tech Notes:
- `src/Game.Main/GameLogic.cs`: `CheckActionConditionSystem.Update` is at line 247, `ActionSucceededSystem.Update` at line 249, `CreateActionEffectSystem.Update` starts at line 251 — matches spec's line references (spec said "currently line 249" for `ActionSucceededSystem`, confirmed correct).
- `ActionPlayability.Evaluate`, `CheckActionConditionSystem.Update`, `BotObservation.Build` (cooldown-relevant call at line 130, `currentDate` already resolved at line 71 via `ReadCurrentDate`) all match the spec's description.
- `VisualStateConverter.BuildEntry`/`UpdateCountryActions` (`src/Game.Main/VisualStateConverter.cs`) confirmed: does not call `ActionPlayability.Evaluate`, has its own `poolFull` special case at lines 678–685, and `UpdateCountryActions` already resolves `currentTime` (lines 577–579) but does not yet pass it into `BuildEntry`.
- `CountryActionsView.cs` (lines 73–97) confirmed: `UnplayableReason` switch with no `on_cooldown` branch yet.
- `ActionCardBuilder.cs` confirmed: only rectangular chrome + one precedent overlay (`BuildWarWinChanceBadge`, a small label layered on `action-card-art`); no radial/circular UI element anywhere in the codebase.
- No `cooldown`-related config/component exists anywhere in `src/` or `Assets/Configs/action_config.json` today (grep confirms) — this is fresh ground, matching the spec's own note.
- The `.action-card*` USS classes live in `Assets/UI/Overlay/OrgInfo/OrgActions.uss`, imported into `Assets/UI/HUD/CountryInfo/CountryInfo.uxml` (which is where `CountryActionsView`'s `hand-container` actually lives) per the USS-scope rule in `.claude/rules/unity/uitoolkit.md` — new overlay classes go in `OrgActions.uss`, not a new file.
- The original "N days"/"N months"/"N years" formatting was found in the removed commit `3ba012c` (`FormatCooldown`, plain hardcoded English strings — never localized, no locale keys were ever added for it). Reusing it means reusing that exact bucketing logic as a small static C# helper; it stays plain, non-localized text like the original — only the new `action.country.unplayable.on_cooldown` reason key gets EN+RU localization treatment, per spec's own scoping of what needs the `localization` skill.
- `src/Game.Tests/ActionPlayabilityTests.cs` has ~40 direct calls to `ActionPlayability.Evaluate(...)` and several `CheckActionConditionSystem.Update(world, config)` calls with no `currentTime`/cooldown awareness. To avoid rewriting all of them, `currentTime` is added as a **trailing optional parameter** (`DateTime currentTime = default`) on both methods — `default(DateTime)` is a valid C# optional-parameter value, and since none of those tests ever create an `ActionCooldownState` tracking entity, `ActionCooldownQuery.IsOnCooldown` returns `false` regardless of the `default` value passed, so all existing tests keep passing unchanged.

## Goal

Add a shared `(OrgId, ActionId)` cooldown gate for country-owned action cards: a configurable global duration, a persisted tracking entity per pair, a single shared playability check consumed by both server-side validation and bots, and a country-actions-panel UI that shows a radial semi-transparent cooldown overlay plus a numeric remaining-time label on affected cards.

## Approach

1. **Config**: one new flat scalar `GameSettings.CardCooldownDays` (default 7), mirrored in `Assets/Configs/game_settings.json`.
2. **State**: one new `[Savable]` component `ActionCooldownState { OrgId, ActionId, EndTime }` living on a dedicated tracking entity per `(OrgId, ActionId)` pair that has been played at least once — composed the same way `CardDeck` is, not attached to the played card entity (that would reproduce the old per-instance bug).
3. **Query**: one new pure-query static class `ActionCooldownQuery` (`IsOnCooldown`, `GetRemaining`), same shape as `ControlQuery`/`ResourceQuery`.
4. **Write path**: one new system `ApplyActionCooldownSystem`, scanning the same `GameAction + ActionSucceeded + OrgContext + CardUse` archetype `CreateActionEffectSystem` already scans, filtering to `ActionDefinition.OwnerType == "country"` only, creating/updating the tracking entity's `EndTime`. Wired into `GameLogic.Update` right after `ActionSucceededSystem.Update` (line 249) and before `CreateActionEffectSystem.Update` (line 251).
5. **Gate**: `ActionCooldownQuery.IsOnCooldown` folded into `ActionPlayability.Evaluate` alongside the existing `CanAfford` call, via a new trailing optional `DateTime currentTime = default` parameter threaded through `Evaluate`'s two call sites (`CheckActionConditionSystem.Update`, `BotObservation.Build`).
6. **UI data**: `VisualStateConverter.BuildEntry`/`UpdateCountryActions` gain their own `ActionCooldownQuery.GetRemaining` check (since `BuildEntry` doesn't call `Evaluate` at all — it re-implements condition evaluation), producing a new `"on_cooldown"` unplayable reason plus `CooldownRemainingDays`/`CooldownFractionRemaining` on `ActionCardEntry`.
7. **UI chrome**: `CountryActionsView.cs` gets an `"on_cooldown"` reason branch + new locale key; `ActionCardBuilder.cs` gains a radial cooldown overlay, rendered as a **runtime-regenerated, cached `Texture2D` pie-mask** (see "Radial overlay approach" below) assigned via `VisualElement.style.backgroundImage`, plus the reused numeric remaining-time label.

## Radial overlay approach — decision

Three options were on the table (per spec's Tech Notes). Recommendation: **runtime-regenerated `Texture2D` pie-slice mask**, cached by rounded percent bucket.

- **Rejected — custom shader + RenderTexture.** Most visually correct, but the project has zero existing shader precedent and ships to WebGL. `.claude/rules/unity/webgl.md` documents that `Shader.Find` silently returns null in WebGL builds unless the material is registered in Player Settings → Preloaded Assets or referenced by a scene object — a real, easy-to-miss failure mode this repo's automation cannot verify itself (no Editor access to check Preloaded Assets or take a WebGL build). Introducing the project's first-ever custom shader for one small UI element is disproportionate risk for the payoff.
- **Rejected — two rotating semi-disc `VisualElement`s.** Avoids shaders entirely and is a well-known USS trick, but getting the two half-circle masks, `transform-origin`, and rotation angles pixel-correct needs iterative visual tuning in the Unity Editor — which this environment cannot do. It also only approximates a true radial wipe rather than reproducing it exactly.
- **Chosen — runtime-regenerated `Texture2D`.** No shader/material asset at all (sidesteps the WebGL `Shader.Find`/Preloaded-Assets pitfall entirely — a plain `Texture2D` needs no preloading), URP-agnostic by construction, and fully expressible as ordinary C# the agent can write and reason about without Editor round-trips. Cost is bounded: cooldown only ever affects rendered **hand** cards (deck cards render as a collapsed pile, not individually — `BuildDeckPile`), hand size is capped at `actionConfig.GetHandSize("country")` (currently 5, confirmed in `Assets/Configs/action_config.json`), and the mask only depends on the *rounded remaining fraction*, not per-card identity — so a small `Dictionary<int, Texture2D>` cache keyed by `Mathf.RoundToInt(fraction * 100)` (0–100 buckets) means at most 101 textures ever get generated for the lifetime of the process, each generated once, not per frame.

Semantics: the overlay visualizes **remaining** cooldown as a shrinking dark pie wedge (sweeping away as time passes, like a common ability-cooldown indicator) — filled for `angle < fractionRemaining * 360`, where `fractionRemaining = remaining.TotalDays / GameSettings.CardCooldownDays` (clamped 0..1). It disappears (`display: none`) once cooldown ends, matching the existing "no reason to reserve layout space when absent" guidance in `.claude/rules/unity/uitoolkit.md`.

## Section 1 — Agent Steps

- [x] **Add `GameSettings.CardCooldownDays`** — `src/Game.Configs/GameSettings.cs`: add `public double CardCooldownDays { get; set; } = 7;` alongside the other flat scalar settings (e.g. near `PeaceGoldPerMonth`). Add `"cardCooldownDays": 7` to `Assets/Configs/game_settings.json` (camelCase JSON policy auto-maps it, confirmed via `Game.Configs.Loader/Program.cs`'s `PropertyNamingPolicy.CamelCase`).

- [x] **Add `ActionCooldownState` component** — new file `src/Game.Components/ActionCooldownState.cs`:
  ```csharp
  using System;

  namespace GS.Game.Components {
      [Savable]
      public struct ActionCooldownState {
          public string OrgId;
          public string ActionId;
          public DateTime EndTime;
      }
  }
  ```
  `[Savable]` because `EndTime` is genuine runtime state set at play time, not derivable from config (per the `[Savable]` omission rule in `.claude/rules/unity/ecs_patterns.md`). One entity per `(OrgId, ActionId)` pair, created lazily the first time that pair is played — mirrors `CardDeck`'s shape.

- [x] **Add `ActionCooldownQuery`** — new file `src/Game.Systems/ActionCooldownQuery.cs`, a plain non-system helper (same shape as `ControlQuery`):
  - `GetRemaining(IReadOnlyWorld world, string orgId, string actionId, DateTime currentTime) -> TimeSpan?` — scans `ActionCooldownState` entities, returns `null` if no tracking entity for the pair or if `EndTime <= currentTime`, otherwise `EndTime - currentTime`.
  - `IsOnCooldown(IReadOnlyWorld world, string orgId, string actionId, DateTime currentTime) -> bool` — `GetRemaining(...).HasValue`.

- [x] **Add `ApplyActionCooldownSystem`** — new file `src/Game.Systems/ApplyActionCooldownSystem.cs`:
  - `Update(World world, DateTime currentTime, GameSettings settings, ActionConfig actionConfig)`.
  - Query shape: `{ GameAction, ActionSucceeded, OrgContext, CardUse }` — the same archetype `CreateActionEffectSystem.Update` already scans (collect `(actionId, orgId)` pairs into a list first, same two-pass collect-then-mutate style used elsewhere in this codebase to avoid mutating while iterating archetypes).
  - For each pair: look up `actionConfig.Find(actionId)`; **skip if `def == null` or `def.OwnerType != "country"`** (explicitly filters out org-owned actions per spec — must not silently start gating them).
  - `EndTime = currentTime.AddDays(settings.CardCooldownDays)`.
  - Find an existing `ActionCooldownState` tracking entity matching `(OrgId, ActionId)` (linear scan over `ActionCooldownState`-tagged entities); if found, update its `EndTime` via `ref` mutation (`world.Get<ActionCooldownState>(entity).EndTime = endTime`); otherwise `world.Create()` + `world.Add(entity, new ActionCooldownState { OrgId = orgId, ActionId = actionId, EndTime = endTime })`.

- [x] **Wire `ApplyActionCooldownSystem` into the game loop** — `src/Game.Main/GameLogic.cs`: call `ApplyActionCooldownSystem.Update(_world, currentTime, GameSettings, _actionConfig);` immediately after `ActionSucceededSystem.Update(_world, _actionConfig);` (line 249) and before `CreateActionEffectSystem.Update(...)` (line 251) — both consume the same one-tick `ActionSucceeded`/`CardUse` marker lifetime that `CleanupActionEffectsSystem.Update` sweeps at the top of the *next* tick, so this placement matches every other post-play system's ordering assumption.

- [x] **Thread `currentTime` through `ActionPlayability.Evaluate` and add the cooldown gate** — `src/Game.Systems/ActionPlayability.cs`:
  - Add a trailing optional parameter `DateTime currentTime = default` to `Evaluate`'s signature (after `hqCountryByOrgId`).
  - Add, alongside the existing `CanAfford` check: `if (ActionCooldownQuery.IsOnCooldown(world, orgId, actionId, currentTime)) { return false; }`.
  - No owner-type branching needed inside `Evaluate` itself — this is safe by construction because `ApplyActionCooldownSystem` never creates a tracking entity for an org-owned `actionId`, so `IsOnCooldown` is always `false` for org actions regardless of `currentTime`. (Confirms the spec's Tech Notes point that this needs verifying, not leaving implicit — verified true given step above.)

- [x] **Thread `currentTime` through `CheckActionConditionSystem.Update`** — `src/Game.Systems/CheckActionConditionSystem.cs`: add a trailing optional parameter `DateTime currentTime = default`, pass it through to the `ActionPlayability.Evaluate(...)` call inside. Update `src/Game.Main/GameLogic.cs`'s call site (line 247) to pass the already-in-scope `currentTime` explicitly: `CheckActionConditionSystem.Update(_world, _actionConfig, _hqCountryByOrgId, currentTime);`.

- [x] **Pass `currentDate` into the bot's playability check** — `src/Game.Bots/BotObservation.cs`: at the `ActionPlayability.Evaluate(world, actionConfig, entity, actionId, orgId, countryId)` call (line 130), add the already-resolved `currentDate` (from `ReadCurrentDate`, line 71) as the trailing argument: `ActionPlayability.Evaluate(world, actionConfig, entity, actionId, orgId, countryId, currentTime: currentDate)`. This is the one shared-function edit that covers both player-facing server-side validation and `BotCardView.IsPlayable` (consumed by `BaselineCardPlayFeature.TryPlay`) — no bot-specific code needed, satisfying the Constitution's bot-feature carve-out boundary (this is not a new `IBotFeature`, just a shared gate becoming stricter).

- [x] **Add cooldown-aware fields to `ActionCardEntry`** — `src/Game.Main/VisualState.cs`: add two nullable properties, `double? CooldownRemainingDays` and `double? CooldownFractionRemaining` (0 = about to end, 1 = just started), with matching optional constructor parameters (default `null`) appended after `warWinChancePercent`.

- [x] **Update `StateEquality.ActionCardEntryEquals`** — `src/Game.Main/StateEquality.cs`: add `&& a.CooldownRemainingDays == b.CooldownRemainingDays && a.CooldownFractionRemaining == b.CooldownFractionRemaining` so cooldown countdown changes correctly trigger a `CountryActionsState` refresh.

- [x] **Add `CardCooldownDays` to `VisualStateConverter`'s constructor** — `src/Game.Main/VisualStateConverter.cs`: add a trailing constructor parameter `double cardCooldownDays = 7`, store as `readonly double _cardCooldownDays`. Update the construction call site in `src/Game.Main/GameLogic.cs` (around line 79–81) to pass `settings.CardCooldownDays`.

- [x] **Add the cooldown check to `BuildEntry`** — `src/Game.Main/VisualStateConverter.cs`:
  - Add a `DateTime currentTime` parameter to `BuildEntry` (already computed in `UpdateCountryActions`, lines 577–579); update both call sites (lines 615, 628) to pass it.
  - Inside `BuildEntry`, alongside the existing `poolFull` special-case (lines 678–685): `TimeSpan? remaining = ActionCooldownQuery.GetRemaining(world, orgId, actionId, currentTime); bool onCooldown = remaining.HasValue;`
  - Extend `isUnplayable = conditionFailed || poolFull || onCooldown;` and `unplayableReason` to check `onCooldown` (map to `"on_cooldown"`), keeping the existing `poolFull`/`conditionFailed` precedence ahead of it since those are rarer to co-occur with an active cooldown and this preserves current test expectations for the other reasons.
  - Compute `double? cooldownRemainingDays = onCooldown ? System.Math.Ceiling(remaining!.Value.TotalDays) : (double?)null;` (whole days, matching `FormatCooldownRemaining`'s own day-level granularity) and `double? cooldownFractionRemaining = onCooldown && _cardCooldownDays > 0 ? System.Math.Round(System.Math.Clamp(remaining!.Value.TotalDays / _cardCooldownDays, 0.0, 1.0), 2) : (double?)null;` (rounded to the same percent-bucket granularity `GetOrCreateCooldownTexture`'s cache key uses), and pass both into the new `ActionCardEntry(...)` constructor call. Rounding here (rather than passing raw continuously-decreasing doubles) keeps `StateEquality.ActionCardEntryEquals` stable between ticks where the visible label/overlay wouldn't actually change — `TimeSystem` advances `GameTime.CurrentTime` in whole-hour steps on almost every tick while unpaused, and unrounded values would fail equality (and trigger a full hand-panel rebuild via `CountryActionsState.Set` → `HUDDocument.HandleCountryActionsChanged` → `CountryActionsView.Refresh`) on nearly every tick for the entire time a card is on cooldown.

- [x] **Add the reused remaining-time formatter** — new small static helper (place in `Assets/Scripts/Unity/UI/ActionCardBuilder.cs` as a `static string FormatCooldownRemaining(double? remainingDays)` method, since it's UI-presentation-only and consumed only from that file/`CountryActionsView.cs`), reproducing the original `FormatCooldown` bucketing from the removed `3ba012c` commit (plain, non-localized text, matching the original — only the `on_cooldown` *reason* label gets localization, not this numeric string):
  ```csharp
  static string FormatCooldownRemaining(double? remainingDays) {
      if (!remainingDays.HasValue || remainingDays.Value <= 0) { return ""; }
      int days = (int)remainingDays.Value;
      if (days >= 365) { return $"{days / 365} year(s)"; }
      if (days >= 30) { return $"{days / 30} month(s)"; }
      if (days >= 2) { return $"{days} days"; }
      if (days == 1) { return "1 day"; }
      return "less than a day";
  }
  ```

- [x] **Add the radial cooldown overlay to `ActionCardBuilder`** — `Assets/Scripts/Unity/UI/ActionCardBuilder.cs`:
  - Add a `double? cooldownFractionRemaining = null` **and** `double? cooldownRemainingDays = null` parameter pair to `Build`, `PopulateSlot`, and the internal `Populate` method (threaded through together, matching the existing `warWinChancePercent` optional-parameter pattern), so `Populate` can forward both into `BuildCooldownOverlay(fractionRemaining, remainingDays)`.
  - Inside `Populate`, after `artEl` is built and appended (mirroring where `BuildWarWinChanceBadge` is added): if `cooldownFractionRemaining.HasValue`, call a new `BuildCooldownOverlay(cooldownFractionRemaining.Value)` and add its result on top of `container` (added last, after `body`, so it visually covers the whole card face, not just the art thumbnail — matches the issue's "overlay is shown on the card face" wording more literally than confining it to the 130px art strip).
  - `BuildCooldownOverlay(double fractionRemaining)` builds a container `VisualElement` with class `action-card-cooldown-overlay` (`position: absolute`, full card bounds, `display: flex` since it's only added when present — no need for a hidden/visible toggle class, absence of the element itself is the "hidden" state per the acceptance criterion "the radial overlay ... disappear[s]"), containing:
    - a child `VisualElement` with class `action-card-cooldown-radial` whose `style.backgroundImage` is set to `GetOrCreateCooldownTexture(fractionRemaining)`.
    - a `Label` with class `action-card-cooldown-label`, text from `FormatCooldownRemaining(...)` (caller passes the days value alongside the fraction — adjust the parameter list to carry both, e.g. `BuildCooldownOverlay(double fractionRemaining, double? remainingDays)`).
  - `GetOrCreateCooldownTexture(double fractionRemaining)`: a `static readonly Dictionary<int, Texture2D>` cache keyed by `Mathf.RoundToInt(Mathf.Clamp01((float)fractionRemaining) * 100)`. On cache miss, generate a new `Texture2D` (e.g. 128×128, `TextureFormat.RGBA32`, `filterMode = FilterMode.Bilinear`), iterate pixels, compute each pixel's angle from image center (0° = 12 o'clock, clockwise) and radius; set alpha ~0.6 black (or a themed dark tone consistent with `.action-card--unavailable`'s dim treatment) where `angle/360 < fractionRemaining` and the pixel is inside the circle radius, fully transparent elsewhere; `Apply()` once, cache, and return.

- [x] **Add the new USS classes** — `Assets/UI/Overlay/OrgInfo/OrgActions.uss` (this is the file that already owns every other `.action-card*` class and is imported into `CountryInfo.uxml`, per the USS-scope rule in `.claude/rules/unity/uitoolkit.md` — do not create a new stylesheet):
  ```css
  .action-card-cooldown-overlay {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      align-items: center;
      justify-content: center;
  }

  .action-card-cooldown-radial {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
  }

  .action-card-cooldown-label {
      color: rgb(240, 232, 208);
      font-size: 16px;
      -unity-font-style: bold;
      -unity-text-align: middle-center;
      background-color: rgba(0, 0, 0, 0.35);
      padding: 2px 8px;
      border-radius: 4px;
  }
  ```
  (Exact colours/sizes are a starting point — flagged for visual confirmation in Section 2, since this environment cannot preview UI Toolkit rendering.)

- [x] **Wire the `on_cooldown` branch into `CountryActionsView`** — `Assets/Scripts/Unity/UI/CountryActionsView.cs`:
  - In the `card.UnplayableReason` switch (lines 74–93), add: `"on_cooldown" => _loc.Get("action.country.unplayable.on_cooldown"),`.
  - In `BuildHandCard`, always pass `card.CooldownFractionRemaining`/`card.CooldownRemainingDays` into `ActionCardBuilder.Build(...)`'s new parameters, regardless of which string `UnplayableReason` currently holds — `CooldownFractionRemaining` is populated by `BuildEntry` whenever the card is actually on cooldown, independent of reason precedence (a card can simultaneously be on cooldown and fail an unrelated condition, in which case `UnplayableReason` resolves to the other reason but the card is still on cooldown), so the overlay must key off `CooldownFractionRemaining.HasValue` directly, not off `UnplayableReason == "on_cooldown"`.

- [x] **Add the `on_cooldown` locale key (EN + real RU)** — use the `localization` skill to add `action.country.unplayable.on_cooldown` to `Assets/Localization/en.asset` (short practical value, matching the existing terse style of sibling keys, e.g. `On cooldown`) and a real Russian translation to `ru.asset` — not an English placeholder, per `.claude/rules/unity/localization.md`.

- [x] **Add/update unit tests** — see Tests section below for the full list; place ECS-level tests in `src/Game.Tests` alongside `ActionPlayabilityTests.cs`.

## Section 2 — User Steps

### 1. Visual sign-off on the radial overlay

The generated `Texture2D` pie-mask's exact look (colour, opacity, sweep direction, size relative to the card) cannot be previewed by this environment — there is no Unity Editor access. After the agent's implementation, open the Unity Editor, put an org card on cooldown (e.g. via a debug play or the existing debug card commands), select the country whose card is on cooldown, and visually confirm:
- the overlay renders as a semi-transparent dark radial wipe over the card face (not a solid block, not a rectangle),
- it visibly shrinks tick-by-tick as the game clock advances,
- it fully disappears the instant the cooldown ends (no stale 1-frame flash), and
- the numeric "N days"/"N months"/"N years" label is legible against both light and dark parts of the card art.

Adjust the USS values (`action-card-cooldown-overlay`/`-radial`/`-label` in `Assets/UI/Overlay/OrgInfo/OrgActions.uss`) and the texture's alpha/colour choice in `ActionCardBuilder.cs` directly in-Editor via Play Mode iteration or the UI Builder as needed — this is expected polish, not a sign of a wrong approach.

### 2. Confirm hand-slot draw behaviour visually

Verify the "freed hand slot draws a new copy of a cooldown'd type, shown unplayable immediately" acceptance criterion end-to-end in Play Mode: play a country card, let its hand slot fill with a fresh copy (may require advancing several turns depending on deck composition), and confirm the freshly drawn copy immediately shows the cooldown overlay rather than appearing briefly playable.

### 3. Confirm bot behaviour is unaffected beyond the intended gate

Optionally, run a short bot-vs-bot session (or check `Docs/BotFeatures/` eval history if already wired for a relevant scenario) to confirm bots stop repeat-playing a card type they've just played, without introducing any new bot log noise or errors — this is a sanity check on the shared-gate change, not a new bot feature, so no new eval config is required.

## Tests

Follow the existing `dotnet test` convention in `src/Game.Tests`, matching `ActionPlayabilityTests.cs`'s style (`BuildActionConfig()` helper, `AddCard`/`AddGold`/`AddControl` helpers, direct `World` construction, `Assert.True`/`Assert.False`).

- **New `src/Game.Tests/ActionCooldownQueryTests.cs`** (mirrors `ControlQueryTests.cs`'s shape):
  - `IsOnCooldown` returns `false` when no tracking entity exists for the `(orgId, actionId)` pair.
  - `IsOnCooldown` returns `true` when a tracking entity's `EndTime` is after `currentTime`, `false` when `EndTime <= currentTime`.
  - `GetRemaining` returns the correct `TimeSpan` when on cooldown, `null` otherwise.
  - Two different `(orgId, actionId)` tracking entities don't cross-contaminate lookups (same org, different action; same action, different org).

- **New `src/Game.Tests/ApplyActionCooldownSystemTests.cs`**:
  - A country-owned card's successful play (`ActionSucceeded` + `CardUse` + matching `GameAction`/`OrgContext`) creates a new `ActionCooldownState` entity with `EndTime == currentTime.AddDays(settings.CardCooldownDays)`.
  - Playing the same `(orgId, actionId)` pair again **updates** the existing tracking entity's `EndTime` rather than creating a duplicate entity.
  - An org-owned card's successful play (`ownerType: "org"`) creates **no** tracking entity — regression guard for the explicit out-of-scope filter.
  - A different target's card entity sharing the same `ActionId` (e.g. `declare_war` vs. Spain and vs. Portugal) both feed the same single tracking entity, keyed by `ActionId` alone, not by entity/target.
  - Two different orgs playing the same `ActionId` produce two independent tracking entities.

- **Extend `src/Game.Tests/ActionPlayabilityTests.cs`**:
  - New test: a country card is playable when no cooldown tracking entity exists, and becomes unplayable once one is added with a future `EndTime`, and playable again once `currentTime` passes `EndTime` — call `ActionPlayability.Evaluate(..., currentTime: someTime)` directly with a manually-seeded `ActionCooldownState` entity (no need to run the full pipeline).
  - New test: an org-owned card (`org_card` in the existing `BuildActionConfig()` fixture) stays playable even with an `ActionCooldownState` entity seeded for the same `orgId`+a country `actionId` — confirms no accidental cross-action leakage (this is really exercising `ActionCooldownQuery`'s exact-match behaviour end-to-end through `Evaluate`).
  - New test mirroring `evaluate_verdict_matches_pipeline_action_valid_outcome`'s pattern: a cooldown'd card's `Evaluate(...)` result matches the full `RunPipeline(...)` outcome (extend `RunPipeline` to accept/pass a `currentTime`, or add a cooldown-aware sibling helper) — keeps the "shared gate" property covered exactly the way non-cooldown gates already are.
  - All ~40 existing calls to `ActionPlayability.Evaluate(...)`/`CheckActionConditionSystem.Update(...)` are left untouched (relying on the trailing optional `currentTime = default` parameter) — run the full existing suite to confirm no regressions from the new parameter/gate.

- **New `src/Game.Tests/VisualStateConverterActionCooldownTests.cs`** (or extend an existing `VisualStateConverter`-focused test file if one already covers `BuildEntry`/`UpdateCountryActions` — check for one before creating a new file):
  - A country card entity backed by an on-cooldown `ActionCooldownState` produces an `ActionCardEntry` with `IsUnplayable == true`, `UnplayableReason == "on_cooldown"`, `CooldownRemainingDays` matching the tracking entity's remaining `TimeSpan`, and `CooldownFractionRemaining` in `[0, 1]`.
  - A card with no cooldown tracking entity produces `CooldownRemainingDays == null` and `CooldownFractionRemaining == null`.
  - `CooldownFractionRemaining` at the instant of play (`remaining == GameSettings.CardCooldownDays`) is `1.0`; just before expiry it approaches `0.0`.

- **Bot regression coverage**: extend or add a `src/Game.Bots`-adjacent test (check existing `BotObservation`-focused tests, if any, under `src/Game.Tests`, before adding a new file) confirming `BotObservation.Build(...)`'s resulting `BotCardView.IsPlayable` is `false` for a country card on cooldown for that org, and unaffected for a different org holding the same `ActionId`.

- Run the full suite via the `dotnet-test` skill before considering the plan implemented — this project's convention per `CLAUDE.md`/skill index, not raw `dotnet test` shell invocations.

## Constitution Check

No conflicts found — plan aligns with all principles:
- **Rendering (URP-only):** the radial overlay is a plain `Texture2D` assigned to a UI Toolkit `VisualElement.style.backgroundImage` — no shader, material, or camera-stack work of any kind, so URP-only is trivially satisfied (this was in fact the deciding factor against the shader-based overlay option).
- **Game Logic (ECS-only, in `src/`):** all new simulation state (`ActionCooldownState`), the write system (`ApplyActionCooldownSystem`), and the read helper (`ActionCooldownQuery`) live in `src/Game.Components`/`src/Game.Systems`; the only Unity-side (`Assets/Scripts/Unity/UI`) changes are pure presentation (overlay rendering, label text, locale lookup) driven by data already computed in `src/Game.Main/VisualStateConverter.cs`.
- **DI (VContainer-only):** no new singleton services, no `FindObjectOfType`, no static mutable game state — the only new "cache" is a `static readonly Dictionary<int, Texture2D>` inside `ActionCardBuilder` for the radial mask, which is immutable-shape presentation-asset memoization (comparable in spirit to Unity's own texture/font caches), not application state requiring DI.
- **UI (UI Toolkit-only):** the new overlay is built entirely from `VisualElement`/`Label` plus USS, no Canvas/UGUI.
- **Planning discipline:** this plan itself satisfies "plan before implement"; the feature is not a bot feature or a performance-optimization attempt, so neither carve-out applies and the full plan is required (as done here).
- **Specification discipline:** `spec.md` already exists and was approved before this plan, satisfying "spec before plan for feature work."
- **File organisation:** this plan lives at `Docs/Specs/26_08_04_17_card-cooldown/plan.md`, matching the required `Docs/Specs/<YY_MM_DD_HH>_<name>/` layout alongside the existing `spec.md`.
- **Assembly structure:** no new feature folder under `Assets/Scripts/` is introduced — all UI changes land in the existing `Assets/Scripts/Unity/UI` assembly (`GS.Unity.UI.asmdef`), and all ECS changes land in the existing `src/Game.Components`/`src/Game.Systems` projects.
- **C# code style:** all new/edited code in this plan follows tabs, `_`-prefixed private members, always-braces control flow, and no redundant access modifiers, matching the conventions already visible in `ControlQuery.cs`/`ActionPlayability.cs`/`CardDeck.cs`.

Use the implement skill to start working on the plan or request changes.
