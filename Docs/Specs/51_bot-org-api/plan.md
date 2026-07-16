# Plan: Bot Org API

## Spec

Source: `Docs/Specs/51_bot-org-api/spec.md`.

**Intent.** Part 2 of 3 of the bot-org initiative: a minimal, extensible bot API — a read-only per-org observation facade, a whitelisted org-scoped command sink, a feature-flagged `BotProfile` configuration, and headless-runner attachment — so bots observe exactly what a player in their seat would see and act through exactly the pipeline the player uses, with every behaviour gated behind a feature flag that part 3's eval harness can flip and tune.

**Hard dependency.** This plan builds directly on `Docs/Specs/50_multi-org-headless-simulation/` (spec + plan). Spec 50 is approved but **not yet implemented** — **this plan cannot start until 50's implementation lands.** It consumes 50's concrete artifacts as designed there: `GameLogicContext.RngSeed`/`ParticipatingOrganizationIds`, per-org `DiscoveredCountry { OrgId, CountryId }` entities, the `OrgMetrics` static query helper (`GetTotalControl`/`GetGold`/`GetControlByCountry` over `IReadOnlyWorld`), `HeadlessOptions`/`HeadlessRunner`/`SimulationResult` in `src/Game.ConsoleRunner/`, the camelCase results JSON schema (with its `parameters` object), the tick contract (`deltaTime = hoursPerTick / multipliers[0]`, one game day per tick by default, month-skip guard), the view-org convention (`initialOrganizationId` = first participating org in headless), and the `Game.Tests` → `Game.ConsoleRunner` project reference plan 50 adds.

**Key acceptance criteria (design targets):**
- **Observation facade.** Bots never touch `World`, `GameLogic`, or `VisualState`. Each bot gets a read-only per-org observation (`IBotObservation`) built over `IReadOnlyWorld` + configs, exposing only seat-visible state: game date, own gold, own org hand (with `ActionId`, `SlotIndex`, cost, playability), own `ActionOwner.HandSize`, own discovered countries, own control per country + total (consistent with `OrgMetrics`), own org character slots. Per discovered country: own country-card hand with playability, the full per-org control breakdown + total used control (public information), resident characters with their opinion of the observing org only. Undiscovered countries are entirely absent — including other orgs' control there. Nothing leaks: other orgs' hands/decks, own deck contents/order, other orgs' gold/resources/discovered sets/slots, or any mutable `World`/entity handles. All collections come back in a deterministic, documented order (hands by `SlotIndex`; countries/breakdowns ordinally by id).
- **Single shared playability evaluation.** Playability is computed exactly as `CheckActionConditionSystem` would judge it this tick (`ExpressionContext { Control = org's control in the card's country }`, 0 for org cards; every condition non-zero; every `ActionCost` affordable). This MUST be one shared static helper (`ActionPlayability.Evaluate(...)` in `src/Game.Systems`) extracted from `CheckActionConditionSystem`/`DeductActionCostSystem` and called by both the pipeline systems and the facade — no duplicated logic anywhere; the refactor is behaviour-identical and regression-protected.
- **Command sink.** Bots' only mutation channel is `IBotCommandSink` bound to their org, exposing exactly `PlayOrgCard(string actionId)` and `PlayCountryCard(string actionId, string countryId)`, pushing `PlayCardActionCommand` with `OrgId` **stamped by the sink**. No generic `Push<T>`, no `OrgId` parameter anywhere on the bot-facing surface — the forbidden set (all pause/time/locale/lens/save/select/control/debug commands enumerated in the spec) is unexpressable by construction. A duplicate play (same `ActionId` + `CountryId`) within one decision phase is ignored with an `IGameLogger` warning, guarding `InitActionFromPlayCardSystem`'s duplicate-play `InvalidOperationException`; distinct plays in one tick stay allowed. Bot-emitted plays flow through the unmodified pipeline and produce identical world outcomes to direct pushes.
- **BotProfile.** camelCase JSON declaring `orgId` + `features` (`featureId`, `enabled`, flat numeric `parameters` map). DTOs (`BotProfile`, `BotFeatureSetting`) are plain classes in the netstandard2.1 bot assembly with no JSON-library dependency; deserialization happens only in net8.0 ConsoleRunner via `System.Text.Json`. Unknown `featureId`, malformed JSON, or missing file fails fast (non-zero exit, stderr) before any simulation tick. Disabled/absent features are not instantiated; a zero-enabled-feature profile yields a passive org identical to a part-1 run. Adding a feature = implement `IBotFeature` + register its id, nothing else.
- **Runner integration & determinism.** Repeatable `--bot <path>` in headless mode; each profile drives one participating org; validation failures (org not participating, duplicate org) exit non-zero. Tick contract: (1) each bot's decision phase runs in participating-org order, (2) `logic.Update(deltaTime)` consumes all emitted commands in that same tick — so a bot's observation at tick N reflects the world after tick N−1. Each bot gets a seeded `Random` derived deterministically (documented, cross-platform-stable) from the session seed + org id; no unseeded randomness or wall-clock anywhere in bot code. Same seed + configs + orgs + profiles ⇒ identical results JSON (paired-run xunit test + smoke diff). A bot/feature exception aborts the run non-zero with org + feature named. Results JSON `parameters` gains the attached bots' effective configuration; no-bot runs keep the part-1 shape.
- **Baseline feature.** `baselineCardPlay` (parameter `minGoldReserve`, default 0): at most one card per tick — scan own org hand in `SlotIndex` order, then discovered countries' hands in ordinal `countryId` order (each by `SlotIndex`), play the first playable card whose total gold cost leaves gold ≥ `minGoldReserve`; else do nothing. No randomness. Verified: it observably acts vs a passive paired run; a high `minGoldReserve` prevents all plays; disabled flag ⇒ run identical to passive.

**Out of scope:** eval harness / scoring / `/implement-bot-feature` skill (part 3); any `Assets/` change (the bot assembly is netstandard2.1 for future Unity use but is **not** emitted to `Assets/Plugins/Core/` — its csproj gets **no** Release `OutputPath`); new org-scoped commands beyond card play (no character hiring); non-trivial bot intelligence; bots in interactive runner mode; save/load of bot state (nothing bot-related is `[Savable]`); security sandboxing beyond structural API guarantees; performance work beyond not regressing part 1's loop.

## Goal

Create a new netstandard2.1 `src/Game.Bots` project containing the bot-facing contracts (`IBotObservation`, `IBotCommandSink`, `IBotFeature`), their implementations (`BotObservation` snapshot builder, `BotCommandSink` with sink-stamped `OrgId` and per-tick duplicate guard, `Bot` orchestrator, `BotFeatureRegistry`, `BaselineCardPlayFeature`, `BotRng` seed derivation) and the JSON-free `BotProfile`/`BotFeatureSetting` DTOs; extract the condition + affordability logic of `CheckActionConditionSystem`/`DeductActionCostSystem` into a single behaviour-identical `ActionPlayability` helper in `src/Game.Systems` that both the pipeline and the facade call; and wire bot attachment into the part-50 headless runner — repeatable `--bot <path>` parsing/validation in `HeadlessOptions`, `System.Text.Json` profile loading in ConsoleRunner only, a decision phase before each `logic.Update` in participating-org order, and a compatible `parameters.bots` extension of the results JSON — with determinism, information-hiding, sink-enforcement, and baseline-behaviour xunit tests in `src/Game.Tests` as the acceptance gate.

## Approach

### 1. Shared playability evaluation (`src/Game.Systems/ActionPlayability.cs`)

New static class holding the **only** definition of "playable", assembled by moving the existing private helpers out of the pipeline systems verbatim:

```csharp
namespace GS.Game.Systems {
	public static class ActionPlayability {
		// The single source of truth for "playable": definition exists,
		// all Conditions evaluate non-zero with Control = org's control in countryId
		// ("" / null => 0, matching org cards), and every ActionCost is affordable.
		public static bool Evaluate(IReadOnlyWorld world, ActionConfig config, string actionId, string orgId, string? countryId);

		public static int GetOrgControl(IReadOnlyWorld world, string orgId, string? countryId);
		public static bool CanAfford(IReadOnlyWorld world, string orgId, List<ActionCost> costs);
		// First (ResourceOwner, Resource) entity matching owner+resource, or -1 —
		// preserves the existing first-match-wins semantics of both systems.
		public static int FindResourceEntity(IReadOnlyWorld world, string ownerId, string resourceId);
	}
}
```

`Evaluate` body is exactly today's `CheckActionConditionSystem` per-entity logic (lines 34–48): `config.Find(actionId)` null ⇒ `false`; `GetOrgControl` (moved verbatim from lines 56–70, parameter type widened to `IReadOnlyWorld` — `World : IReadOnlyWorld`, and `GetMatchingArchetypes`/`GetColumn` are on the read-only surface, so the move is mechanical); `new ExpressionContext { Control = orgControl }`; loop `def.Conditions` with `ExpressionNode.Evaluate(cond, ctx) == 0.0` short-circuit; `CanAfford` (moved verbatim from lines 72–77, its `HasResource` re-expressed as `FindResourceEntity(...) >= 0 && world.Get<Resource>(entity).Value >= amount` — identical semantics including the existing "first matching entity decides, no continued search" behaviour).

**Callers after the refactor:**
- `CheckActionConditionSystem.Update` — the archetype-collection scaffolding (gather `(entity, actionId, orgId)`, build `entityCountry`, add `ActionValid`) is unchanged; the per-entity check body becomes `if (ActionPlayability.Evaluate(world, config, actionId, orgId, countryId)) { toAdd.Add(entity); }`. Its private `GetOrgControl`/`CanAfford`/`HasResource` are deleted (moved, not copied).
- `DeductActionCostSystem.DeductResource` — replaced by `int entity = ActionPlayability.FindResourceEntity(world, ownerId, resourceId); if (entity >= 0) { ref Resource r = ref world.Get<Resource>(entity); r.Value -= amount; }` so the resource-locating logic exists once. The cost-loop and `ResourceChange` emission stay in the system (they are writes, not evaluation).
- `BotObservation` (§3) — calls `Evaluate` per card when building views.

**Behaviour-identity protection:** (a) the moved code is verbatim (no reordering of the three checks, same short-circuits, same first-match resource lookup); (b) new equivalence tests assert `Evaluate`'s verdict matches the pipeline's `ActionValid`/`ActionFailed` outcome across a scenario matrix (see Tests); (c) plan 50's `MultiOrgGameplayTests` (full card pipeline: cost deduction, hand removal, redraw, failure path) run unchanged over the refactored systems.

### 2. New project `src/Game.Bots` (netstandard2.1)

**csproj** — mirrors `Game.Systems.csproj` (netstandard2.1, `Nullable` enable, `LangVersion` latestMajor) but **deliberately without** the Release `OutputPath` block: per the spec, the bot assembly is not yet emitted to `Assets/Plugins/Core/` (enabling that later is a one-line addition). No `System.Text.Json`, no NuGet packages. References:

```xml
<ProjectReference Include="../ECS.Core/ECS.Core.csproj" />        <!-- IReadOnlyWorld, TypeId, Archetype -->
<ProjectReference Include="../Game.Components/Game.Components.csproj" />
<ProjectReference Include="../Game.Configs/Game.Configs.csproj" /> <!-- ActionConfig, ActionCost -->
<ProjectReference Include="../Game.Commands/Game.Commands.csproj" /><!-- PlayCardActionCommand -->
<ProjectReference Include="../Game.Systems/Game.Systems.csproj" />  <!-- ActionPlayability, OrgMetrics -->
<ProjectReference Include="../Game.Main/Game.Main.csproj" />        <!-- IWriteOnlyCommandAccessor, IGameLogger -->
```

Justification for implementations living in `Game.Bots` (not `Game.Main`/`Game.Systems`): the facade and sink are bot-hosting concerns, not simulation systems — keeping them here keeps `Game.Systems` a pure pipeline (only the shared `ActionPlayability` lands there, because that logic is owned by the pipeline and merely *consumed* by the facade), keeps `Game.Main` free of bot types, and gives part 3 one assembly to grow features in. The `Game.Main` reference is needed solely for `IWriteOnlyCommandAccessor` and `IGameLogger`; duplicating those interfaces would be worse than the reference. Compile-time access to `GameLogic` through that reference is acceptable — the spec's guarantee is structural on the bot-facing interfaces, not a sandbox, and features are first-party reviewed code. Register the project in `GlobalStrategy.Core.sln`.

**Bot-facing contracts:**

```csharp
public interface IBotObservation {
	string OrgId { get; }
	DateTime CurrentDate { get; }                              // GameTime.CurrentTime
	double Gold { get; }                                       // == OrgMetrics.GetGold
	int OrgHandSize { get; }                                   // ActionOwner.HandSize for the org
	int TotalControl { get; }                                  // == OrgMetrics.GetTotalControl
	IReadOnlyList<BotCardView> OrgHand { get; }                // SlotIndex ascending
	IReadOnlyList<string> DiscoveredCountryIds { get; }        // ordinal ascending
	IReadOnlyList<BotCharacterSlotView> CharacterSlots { get; }// (RoleId ordinal, SlotIndex)
	IReadOnlyList<BotCountryView> Countries { get; }           // discovered only, ordinal by CountryId
	BotCountryView? GetCountry(string countryId);              // null if not discovered
}

public interface IBotCommandSink {
	void PlayOrgCard(string actionId);
	void PlayCountryCard(string actionId, string countryId);
}

public interface IBotFeature {
	string FeatureId { get; }
	void Tick(IBotObservation observation, IBotCommandSink sink, Random rng);
}
```

`IBotFeature`'s code docs state explicitly (per the spec): this single interface + a registry entry is the entire extension contract that part 3's `/implement-bot-feature` skill targets — no facade, sink, runner-loop, or cross-feature change is ever needed for a new feature.

**View DTOs** (plain sealed classes in `src/Game.Bots/BotViews.cs`, immutable after build, no entity ids, no `World` handles):

```csharp
public sealed class BotCardView {          // one card in a hand
	public string ActionId;  public int SlotIndex;  public string CountryId;  // "" for org cards
	public IReadOnlyList<BotCostView> Cost;                                   // from ActionDefinition.Cost
	public double GoldCost;                                                   // sum of Cost where ResourceId == "gold"
	public bool IsPlayable;                                                   // ActionPlayability.Evaluate at build time
}
public sealed class BotCostView { public string ResourceId; public double Amount; }
public sealed class BotCountryView {
	public string CountryId;
	public int MyControl;  public int TotalControl;                           // TotalControl = all orgs' sum (used control)
	public IReadOnlyList<BotControlShare> ControlByOrg;                       // ordinal by OrgId — public info per spec
	public IReadOnlyList<BotCardView> Hand;                                   // SlotIndex ascending
	public IReadOnlyList<BotCountryCharacterView> Characters;                 // ordinal by CharacterId
}
public sealed class BotControlShare { public string OrgId; public int Control; }
public sealed class BotCharacterSlotView { public string RoleId; public int SlotIndex; public bool IsAvailable; public string CharacterId; }
public sealed class BotCountryCharacterView { public string CharacterId; public string RoleId; public double OpinionOfMyOrg; } // opinion_{myOrg} resource; 0 when absent
```

### 3. Observation implementation (`src/Game.Bots/BotObservation.cs`)

`sealed class BotObservation : IBotObservation` with a static factory:

```csharp
public static BotObservation Build(IReadOnlyWorld world, ActionConfig actionConfig, string orgId)
```

Built **fresh at each decision tick** as an immutable snapshot (spec permits per-tick rebuild; no caching in this part). Build passes, all via `GetMatchingArchetypes` scans in the established system style:

1. **Date** — scan for the `GameTime` archetype (single entity), read `CurrentTime`.
2. **Gold / total control / per-country own control** — delegate to `OrgMetrics.GetGold`, `GetTotalControl`, `GetControlByCountry` (part 50), keeping facade numbers definitionally consistent with the results JSON.
3. **Discovered set** — `DiscoveredCountry` entities with `OrgId == orgId` → `SortedSet<string>(StringComparer.Ordinal)` of `CountryId`s.
4. **Control breakdown** — one pass over `ControlEffect` archetypes accumulating `country → (org → sum)`; only countries in the discovered set are materialized into `BotCountryView.ControlByOrg` (ordinal by `OrgId`) + `TotalControl`; everything about undiscovered countries is dropped on the floor — they are absent from `Countries`, `GetCountry` returns null.
5. **Hands** — `ActionCard` entities with `OwnerId == orgId` + `InHand`: no `CountryContext` ⇒ org hand; with `CountryContext` (and `OrgContext.OrgId == orgId`) ⇒ that country's hand, **only if the country is discovered** (per-country decks exist for all countries since part 50's init, but a player cannot open an undiscovered country's panel, so those hands are hidden). Each card resolves `ActionDefinition` via `actionConfig.Find` for `Cost`/`GoldCost` and `IsPlayable = ActionPlayability.Evaluate(world, actionConfig, actionId, orgId, countryIdOrNull)`. Hands sorted by `SlotIndex`.
6. **Org character slots** — `CharacterSlot` entities with `OwnerId == orgId`, sorted `(RoleId ordinal, SlotIndex)`.
7. **Resident characters** — `Character` entities with `CountryId == C` for each discovered `C` (`CharacterId`, `RoleId`), sorted ordinally by `CharacterId`; opinion = the `Resource { ResourceId = $"opinion_{orgId}" }` owned by that `CharacterId` (`ResourceOwner`, `OwnerType.Character`), `0` when the resource entity does not exist. Only the observing org's opinion resource is read — other orgs' opinions never enter the snapshot.

Deterministic ordering is enforced by explicit sorts (`StringComparer.Ordinal`, `SlotIndex`) after every scan — never by relying on archetype/dictionary iteration order. The class performs no writes (`IReadOnlyWorld` only) and holds no reference to the world after `Build` returns (everything is copied into the view DTOs), so a stale observation can never read a mutated world.

**Staleness note (documented in code):** playability is evaluated at build time, i.e. against the post-(N−1)-tick world. For the first play of a tick this is exactly what `CheckActionConditionSystem` will compute; for additional plays in the same tick the pipeline re-validates and an unaffordable play fails and discards, as it would for a player (spec-accepted).

### 4. Command sink (`src/Game.Bots/BotCommandSink.cs`)

```csharp
public sealed class BotCommandSink : IBotCommandSink {
	readonly string _orgId;
	readonly IWriteOnlyCommandAccessor _commands;
	readonly IGameLogger? _logger;
	readonly HashSet<(string actionId, string countryId)> _playedThisPhase = new();

	public BotCommandSink(string orgId, IWriteOnlyCommandAccessor commands, IGameLogger? logger) { ... }

	public void PlayOrgCard(string actionId) => TryEmit(actionId, "");
	public void PlayCountryCard(string actionId, string countryId) => TryEmit(actionId, countryId);

	public void BeginDecisionPhase() => _playedThisPhase.Clear();   // host-facing, not on IBotCommandSink

	void TryEmit(string actionId, string countryId) {
		if (!_playedThisPhase.Add((actionId, countryId))) {
			_logger?.LogInfo($"[BotCommandSink] warning: duplicate play ignored org={_orgId} action={actionId} country={countryId}");
			return;
		}
		_commands.Push(new PlayCardActionCommand { ActionId = actionId, OrgId = _orgId, CountryId = countryId });
	}
}
```

- **Whitelist by construction:** `IBotCommandSink` has exactly the two typed methods; the `IWriteOnlyCommandAccessor` is a private field of the concrete class, never surfaced. No generic push, no `OrgId` parameter anywhere — every command carries the sink's bound `_orgId`. Every forbidden command type in the spec's enumerated set is structurally unexpressable.
- **Duplicate guard:** the `(actionId, countryId)` set spans one decision phase (org card key uses `""`, matching `PlayCardActionCommand.CountryId` semantics in `InitActionFromPlayCardSystem`); `BeginDecisionPhase()` clears it and is called by the `Bot` orchestrator each tick. Duplicates are dropped with a warning; distinct plays pass through and the pipeline re-validates each. `IGameLogger` has no dedicated warning level (`LogError`/`LogInfo`/`LogDebug`), so the warning is `LogInfo` with an explicit `warning:` prefix — noted here as a deliberate choice, not an oversight.
- **Part-3 emission hook (designed in, not implemented):** every accepted play funnels through the single `TryEmit` seam. Part 3's play-log can attach by adding an optional `Action<string actionId, string countryId>` callback (or listener interface) constructor parameter invoked inside `TryEmit` — no `IBotCommandSink` surface change, no call-site changes beyond the host's constructor call.

### 5. Profile DTOs, registry, orchestrator, RNG (`src/Game.Bots/`)

**`BotProfile.cs`** — plain DTOs, zero JSON dependency (deserialized in ConsoleRunner via `System.Text.Json` with `PropertyNameCaseInsensitive = true`, so camelCase JSON binds to these PascalCase properties with no attributes; a future Unity host builds them via Newtonsoft with the same shape):

```csharp
public class BotProfile {
	public string OrgId { get; set; } = "";
	public List<BotFeatureSetting> Features { get; set; } = new();
}
public class BotFeatureSetting {
	public string FeatureId { get; set; } = "";
	public bool Enabled { get; set; }
	public Dictionary<string, double> Parameters { get; set; } = new();   // flat numeric map, per spec
}
```

`Parameters` is numbers-only in this part — exactly the mechanically-mutable shape part 3 needs; widening to other primitives later is a DTO-local change.

**`BotFeatureRegistry.cs`** — instance class (no static mutable state, per constitution spirit):

```csharp
public sealed class BotFeatureRegistry {
	public void Register(string featureId, Func<IReadOnlyDictionary<string, double>, IBotFeature> factory);
	public IBotFeature Create(string featureId, IReadOnlyDictionary<string, double> parameters); // unknown id => InvalidOperationException naming the id
	public bool IsRegistered(string featureId);
	public static BotFeatureRegistry CreateDefault();  // registers "baselineCardPlay"
}
```

**Validation decision:** every `featureId` in a profile is validated against the registry **regardless of `enabled`** (catches typos in dormant entries), but only `enabled: true` settings are instantiated. This satisfies both the unknown-id fail-fast and the not-instantiated-when-disabled criteria.

**`Bot.cs`** — per-bot orchestrator owning the tick contract's phase 1; keeping it in `Game.Bots` (not the runner) means a future Unity host reuses the same loop:

```csharp
public sealed class Bot {
	public string OrgId { get; }
	public Bot(string orgId, IReadOnlyList<IBotFeature> features, Random rng, BotCommandSink sink);
	public void ExecuteDecisionTick(IReadOnlyWorld world, ActionConfig actionConfig) {
		_sink.BeginDecisionPhase();
		var observation = BotObservation.Build(world, actionConfig, OrgId);
		foreach (var feature in _features) {
			try { feature.Tick(observation, _sink, _rng); }
			catch (Exception ex) { throw new BotFeatureException(OrgId, feature.FeatureId, ex); }
		}
	}
}
public sealed class BotFeatureException : Exception { public string OrgId { get; } public string FeatureId { get; } ... }
```

Each bot has its own features, observation, sink, and RNG — no shared mutable state between bots (spec criterion); the only shared object is the write-only command accessor behind each sink, which is the intended shared channel.

**`BotRng.cs`** — the documented, cross-platform-stable seed derivation:

```csharp
public static class BotRng {
	// FNV-1a 32-bit over the org id's UTF-16 code units:
	// hash = 2166136261; foreach (char c in orgId) { hash ^= c; hash *= 16777619; }
	// Deterministic across processes, platforms, and .NET versions —
	// string.GetHashCode is per-process-randomized and MUST NOT be used.
	public static int DeriveSeed(int sessionSeed, string orgId) => sessionSeed ^ unchecked((int)Fnv1a32(orgId));
	public static Random Create(int sessionSeed, string orgId) => new Random(DeriveSeed(sessionSeed, orgId));
}
```

XOR with a stable hash gives distinct-per-org, seed-sensitive streams; the exact algorithm is pinned in code docs and by a fixed-value regression test. Bot code never calls `new Random()`, `Guid.NewGuid()`, `DateTime.Now/UtcNow`, or environment state — stated in `IBotFeature` docs and enforceable by review (part 3's skill will restate it).

### 6. Baseline feature (`src/Game.Bots/BaselineCardPlayFeature.cs`)

```csharp
public sealed class BaselineCardPlayFeature : IBotFeature {
	public const string Id = "baselineCardPlay";
	readonly double _minGoldReserve;   // parameters["minGoldReserve"], default 0
	public string FeatureId => Id;
	public void Tick(IBotObservation obs, IBotCommandSink sink, Random rng) { ... }
}
```

`Tick` (fully deterministic, `rng` unused): enumerate `obs.OrgHand` in `SlotIndex` order, then `obs.Countries` (already ordinal by `CountryId`) each hand in `SlotIndex` order; the first card with `IsPlayable && obs.Gold - card.GoldCost >= _minGoldReserve` is played (`PlayOrgCard` when `CountryId == ""`, else `PlayCountryCard`) and the method returns — at most one card per tick; if none qualifies, do nothing. Registered in `BotFeatureRegistry.CreateDefault()`. This exercises the "first discovered country" targeting path from the initiative brief because the ordinal-first discovered country's hand is scanned first after the org hand.

### 7. Runner integration (`src/Game.ConsoleRunner/`)

All net8.0-only code (CLI + JSON) stays here; `Game.ConsoleRunner.csproj` gains `<ProjectReference Include="../Game.Bots/Game.Bots.csproj" />`.

**`HeadlessOptions.cs` (extend part 50's parser)** — repeatable `--bot <path>` collected into `public IReadOnlyList<string> BotProfilePaths` (empty by default). Validation additions: `--bot` is only valid together with `--headless` (bots are headless-only per the spec) — otherwise `ArgumentException` like the rest of 50's validation.

**`BotProfileLoader.cs` (new)** — ConsoleRunner-only JSON boundary:

```csharp
public static class BotProfileLoader {
	// Throws (descriptive message) on: missing file, malformed JSON, empty orgId,
	// null/absent features treated as empty list. System.Text.Json,
	// PropertyNameCaseInsensitive = true, binding directly to GS.Game.Bots.BotProfile.
	public static BotProfile Load(string path);
}
```

**`HeadlessRunner.cs` (extend)** — profile loading and validation (steps 1–2) happen after options parsing and before `GameLogic` construction; bot construction (step 3) requires the constructed `logic` (its `Commands` accessor) and runs after construction but before the first tick. Either way every failure surfaces before any simulation tick (fail-fast per spec):
1. Load every profile via `BotProfileLoader.Load` (file/JSON errors → caught in `Program.Main` → stderr, exit 1, matching 50's posture).
2. Validate: each `profile.OrgId` ∈ the resolved participating-org list; no two profiles share an org; every `featureId` is `IsRegistered` in `BotFeatureRegistry.CreateDefault()` (enabled or not, per §5). Any failure → descriptive exception → stderr, exit non-zero, before any simulation tick.
3. Build bots **in participating-org order** (order profiles by their org's index in the participating list — pins decision-phase order and results ordering regardless of CLI argument order): per profile, `features` = enabled settings → `registry.Create(featureId, parameters)`; `rng = BotRng.Create(options.Seed, orgId)`; `sink = new BotCommandSink(orgId, logic.Commands, logger)`; `new Bot(...)`. Orgs without a profile get no bot — passive, exactly part 1.
4. Loop body becomes the spec's fixed tick contract:
   ```csharp
   foreach (var bot in bots) { bot.ExecuteDecisionTick(logic.World, logic.ActionConfig); }   // phase 1, org order
   logic.Update(deltaTime);                                                                  // phase 2: consumes all phase-1 commands, then Clear()
   ```
   (`GameLogic.Update` drains every buffer within the call and ends with `_commandAccessor.Clear()` — so phase-1 commands take effect during this same tick, and tick N's observations reflect the post-(N−1) world. `logic.World` is passed as `IReadOnlyWorld`; `logic.ActionConfig` is the already-public config property.) Bots tick once per `Update`, every tick — including tick 0, where the observation is empty until `InitSystem` has run inside the first `Update`; `BotObservation.Build` on an uninitialized world returns empty collections and `Gold == 0`, so features simply do nothing on that tick (documented; deterministic).
5. `BotFeatureException` (and any bot exception) propagates out of the loop to `Program.Main`'s existing catch → stderr message including org id and feature id → exit non-zero. No swallowing.

**`SimulationResult.cs` (extend)** — `parameters` gains a `bots` property, `null` (and `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`) when no bots so no-bot output stays byte-identical to part 1's shape:

```json
"parameters": {
	"...": "part-50 fields unchanged",
	"bots": [
		{
			"orgId": "Illuminati",
			"features": [ { "featureId": "baselineCardPlay", "enabled": true, "parameters": { "minGoldReserve": 0 } } ]
		}
	]
}
```

Content is the **effective** configuration echoed from the parsed profiles (all declared features with their `enabled` flags and parameter values — self-describing results, exactly what part 3's paired-run comparisons need), ordered by participating-org order, feature order as declared. No wall-clock data anywhere.

### 8. Test project wiring

`src/Game.Tests/Game.Tests.csproj` gains `<ProjectReference Include="../Game.Bots/Game.Bots.csproj" />` (the `Game.ConsoleRunner` reference already arrives with plan 50). New bot tests reuse/extend the multi-org `BuildLogic`-style harness plan 50 introduces (two orgs, countries, an `ActionConfig` with org + country pools, discover/control effects, monthly income) — extended here with per-card costs/conditions rich enough to drive playability on and off.

### 9. What deliberately does NOT change

- `InitActionFromPlayCardSystem`, `ActionSucceededSystem`, `CreateActionEffectSystem`, `RemoveCardFromHandSystem`, `DrawCardSystem`, `CheckHandSizeSystem` — untouched; bot plays ride the identical pipeline (the duplicate-play `InvalidOperationException` stays as-is; the sink guards it from outside, per spec).
- `GameLogic` / `GameLogicContext` / `VisualState` / `VisualStateConverter` — untouched; the decision phase lives entirely in the runner and `Game.Bots`.
- `Game.Commands` — no new command types; the whitelist ships with card play only.
- Save system — nothing bot-related is `[Savable]`; `SavableDiscoveryTests.ExpectedSavable` needs no change.
- Anything under `Assets/` — zero changes; `Game.Bots` has no Plugins `OutputPath`, so even `dotnet build -c Release` leaves `Assets/Plugins/Core/` with only the part-50 refresh of existing DLLs (`Game.Systems.dll` changes because `ActionPlayability` lives there).
- Interactive ConsoleRunner mode — unchanged; `--bot` is rejected outside `--headless`.

## Steps

### Agent Steps

- [x] **Verify spec 50 is implemented** — confirm `Docs/Specs/50_multi-org-headless-simulation/plan.md`'s artifacts exist on the branch (`GameLogicContext.RngSeed`/`ParticipatingOrganizationIds`, `DiscoveredCountry`, `OrgMetrics`, `HeadlessOptions`/`HeadlessRunner`/`SimulationResult`, multi-org tests). **If they do not, stop — this plan cannot start before 50 lands.**

- [x] **Extract `ActionPlayability`** — new `src/Game.Systems/ActionPlayability.cs` with `Evaluate`/`GetOrgControl`/`CanAfford`/`FindResourceEntity` per Approach §1 (verbatim moves, `IReadOnlyWorld` parameters); rewrite `CheckActionConditionSystem.Update`'s per-entity check to call `Evaluate` and delete its private helpers; rewrite `DeductActionCostSystem.DeductResource` over `FindResourceEntity` + `ref` mutation. No behavioural change.

- [x] **Create `src/Game.Bots` project** — `Game.Bots.csproj` (netstandard2.1, **no Release OutputPath**, references per Approach §2), add to `GlobalStrategy.Core.sln`.

- [x] **Bot contracts and views** — `IBotObservation.cs`, `IBotCommandSink.cs`, `IBotFeature.cs` (with the extension-contract code docs), `BotViews.cs` (all view DTO classes) per Approach §2.

- [x] **Observation implementation** — `BotObservation.cs` static `Build` snapshot per Approach §3: `OrgMetrics`-backed numbers, discovered-set filtering, hidden undiscovered countries, `ActionPlayability.Evaluate` per card, explicit ordinal/`SlotIndex` sorts, no retained world reference.

- [x] **Command sink** — `BotCommandSink.cs` per Approach §4: two whitelisted methods, sink-stamped `OrgId`, `BeginDecisionPhase` duplicate-guard reset, single `TryEmit` funnel (part-3 hook seam), `LogInfo` warning on duplicates.

- [x] **Profiles, registry, orchestrator, RNG, baseline** — `BotProfile.cs`, `BotFeatureRegistry.cs` (+`CreateDefault`), `Bot.cs` + `BotFeatureException`, `BotRng.cs` (documented FNV-1a derivation), `BaselineCardPlayFeature.cs` per Approach §5–6.

- [x] **Runner: options + profile loading** — extend `src/Game.ConsoleRunner/HeadlessOptions.cs` with repeatable `--bot <path>` (headless-only validation); new `BotProfileLoader.cs` (`System.Text.Json`, case-insensitive, descriptive failures); add the `Game.Bots` project reference to `Game.ConsoleRunner.csproj`.

- [x] **Runner: attachment + tick contract + results** — extend `HeadlessRunner.cs` per Approach §7: pre-tick load/validate (participating-org membership, duplicate-org, registry check on all featureIds), bots built in participating-org order with derived RNG seeds, decision phase before each `logic.Update`, exceptions surfaced to stderr with org+feature and non-zero exit; extend `SimulationResult.cs` with the `WhenWritingNull` `parameters.bots` echo of effective profiles.

- [x] **Tests** — implement the Tests section below, including the `Game.Bots` reference in `Game.Tests.csproj` and extending the shared multi-org test builder with cost/condition-bearing action configs.

- [x] **Run the test suite** — `dotnet test src/GlobalStrategy.Core.sln`; the playability-equivalence, information-hiding, sink, determinism, and baseline tests are the acceptance gate.

- [x] **Release build** — `dotnet build src/GlobalStrategy.Core.sln -c Release`; verify `Assets/Plugins/Core/` contains **no** `Game.Bots.dll` (only the existing DLLs refreshed) — the not-yet-in-Plugins criterion.

- [x] **Headless smoke run with a bot** — write `.tmp/bot_illuminati.json` (`orgId: "Illuminati"`, `baselineCardPlay` enabled, `minGoldReserve: 0`); from the repo root run twice:
  `dotnet run --project src/Game.ConsoleRunner -c Release -- --headless --seed 42 --output .tmp/bot_run_a.json --end-date 1881-01-01 --bot .tmp/bot_illuminati.json` (and `..._b.json`) — verify exit 0, `parameters.bots` present, and the two files diff byte-identical on metrics; run the same seed **without** `--bot` and verify Illuminati's metrics diverge from the bot run (the bot demonstrably acts) while the file keeps part-1 shape (no `bots` key); run once with a profile naming `orgId: "DoesNotExist"` and once with `featureId: "nope"` — both must exit non-zero with clear stderr before simulating.

### User Steps

### 1. Review the bot API surface

`src/`-only work — no Unity action required. Optionally skim `IBotObservation`/`IBotCommandSink`/`IBotFeature` and the smoke-run results JSON to confirm the extension contract and self-describing output match what part 3's harness should consume. (If Unity is open, letting it reload the refreshed `Assets/Plugins/Core/` DLLs from the Release build and checking `read_console(types=["error"])` is the only side effect to eyeball.)

## Tests

Test project: `src/Game.Tests/` (xunit, snake_case `[Fact]` names, `StaticConfig<T>`/`BuildLogic` harness style per `GameLogicOrgTests.cs`, extended multi-org builder from plan 50 with cost/condition-bearing `ActionConfig`).

- **New `src/Game.Tests/ActionPlayabilityTests.cs`** (shared-evaluation behaviour-identity regression):
  - `evaluate_verdict_matches_pipeline_action_valid_outcome` — matrix of scenarios (conditions met/unmet via a control-threshold condition, affordable/unaffordable cost, org card with no country, country card, unknown `actionId`): for each, assert `ActionPlayability.Evaluate` equals whether the pipeline marks the played card `ActionSucceeded` (vs `ActionFailed`) — the two can never drift because both call the same method, and this test proves the extraction preserved the pipeline's judgement.
  - `unaffordable_play_still_discards_card_and_deducts_nothing` — the honest-contract corollary: a play `Evaluate` calls unplayable goes through the pipeline, fails, is discarded, and costs nothing (refactored `DeductActionCostSystem` untouched behaviour).
  - `deduct_uses_same_resource_entity_lookup_as_affordability` — with two resource entities for different owners, cost is deducted from exactly the org's entity found by `FindResourceEntity`.
  - Plus: plan 50's `MultiOrgGameplayTests` run unchanged over the refactored systems (no edits to those tests — their passing is part of the identity guarantee).

- **New `src/Game.Tests/BotObservationTests.cs`:**
  - `observation_hides_other_orgs_private_state` — two-org world where org B holds org+country cards, has gold, and has discovered an extra country: org A's observation contains none of B's cards, no B gold anywhere, no B slot views, and B's extra country is absent from A's `Countries`/`DiscoveredCountryIds`/`GetCountry` (spec's information-hiding test).
  - `undiscovered_country_control_breakdown_is_hidden` — B has `ControlEffect` in a country A has not discovered; A's observation exposes no view of it at all.
  - `discovered_country_shows_full_public_control_breakdown` — A's view of a discovered country lists both orgs' control sums and the total, matching the player panel semantics.
  - `card_playability_matches_pipeline_validation` — a playable and an unplayable card in A's hands: `IsPlayable` equals the pipeline outcome for each (facade ↔ system consistency through the shared method).
  - `observation_metrics_match_org_metrics` — `Gold`/`TotalControl`/per-country `MyControl` equal `OrgMetrics.GetGold`/`GetTotalControl`/`GetControlByCountry`.
  - `observation_collections_are_deterministically_ordered` — build the observation twice (and for two bots) over the same world; hands are `SlotIndex`-ascending, countries/breakdowns/characters ordinal; both reads element-wise identical.
  - `resident_characters_expose_only_own_org_opinion` — a character with `opinion_A` and `opinion_B` resources: A's view carries the `opinion_A` value; absent opinion reads 0.

- **New `src/Game.Tests/BotCommandSinkTests.cs`:**
  - `sink_stamps_org_id_on_all_commands` — plays via A's sink; every `PlayCardActionCommand` read back from the accessor has `OrgId == "A"` (and there is no API path to say otherwise).
  - `bot_emitted_play_produces_identical_outcome_to_direct_push` — two identical worlds: one plays via the sink, one pushes `PlayCardActionCommand` directly; after `Update`, gold, hands, effects, and control are equal (no side channel, no skipped validation).
  - `duplicate_play_in_same_phase_is_ignored_and_logged` — same `(actionId, countryId)` twice in one phase: exactly one command in the buffer, a capturing `IGameLogger` recorded the warning, and `Update` completes without `InvalidOperationException`.
  - `distinct_plays_in_same_phase_are_all_emitted` — two different cards in one phase both reach the buffer; pipeline re-validates each.
  - `begin_decision_phase_resets_duplicate_guard` — the same play in two consecutive phases emits twice.
  - `sink_interface_exposes_only_whitelisted_members` — reflection over `typeof(IBotCommandSink)`: exactly `PlayOrgCard(string)` and `PlayCountryCard(string, string)`, no generic methods (structural-enforcement regression against future widening by accident).

- **New `src/Game.Tests/BotOrchestratorTests.cs`** (spec's bot-exception abort criterion):
  - `throwing_feature_surfaces_bot_feature_exception_naming_org_and_feature` — a stub `IBotFeature` whose `Tick` throws: `Bot.ExecuteDecisionTick` throws `BotFeatureException` carrying the bot's `OrgId` and the feature's `FeatureId` (both also present in the message), with the original exception as `InnerException` — the testable seam behind the runner's non-zero-exit/stderr path in Approach §7.5, which no other test or smoke step exercises.

- **New `src/Game.Tests/BotDeterminismTests.cs`** (paired-run gate):
  - `same_seed_and_profiles_produce_identical_end_state_and_timeline` — two `GameLogic` instances, same seed/configs/orgs, each with a `baselineCardPlay` bot on org A driven through the exact runner tick contract (decision phase then `Update`) for ≥13 simulated months: identical per-org `OrgMetrics.GetTotalControl`/`GetGold`/`GetControlByCountry`, game date, hand contents, and element-wise identical monthly samples.
  - `bot_seed_derivation_is_stable` — `BotRng.DeriveSeed` returns pinned expected values for fixed `(seed, orgId)` inputs (cross-platform/process stability guard; breaks loudly if anyone swaps in `string.GetHashCode`).
  - `different_orgs_get_different_derived_seeds` — same session seed, two org ids → distinct seeds.

- **New `src/Game.Tests/BaselineCardPlayTests.cs`:**
  - `baseline_bot_changes_metrics_relative_to_passive_run` — paired same-seed runs, one with the baseline bot: the bot emits at least one successful play and its org's metrics diverge from the passive run (spec's "demonstrably acts" criterion).
  - `baseline_plays_at_most_one_card_per_tick` — multiple playable cards; exactly one command per decision phase.
  - `baseline_scans_org_hand_then_countries_in_documented_order` — playable cards in the org hand and in two discovered countries: the org-hand `SlotIndex`-first card is chosen; with the org hand unplayable, the ordinal-first country's `SlotIndex`-first card is chosen.
  - `min_gold_reserve_above_available_gold_prevents_all_plays` — reserve higher than gold ever reaches: zero commands over the whole run (tunability contract).
  - `disabled_feature_yields_run_identical_to_passive` — profile with `enabled: false`: end state and timeline identical to the same-seed no-bot run (flag-off regression); likewise a zero-feature profile.

- **New `src/Game.Tests/BotProfileTests.cs`** (uses the `Game.ConsoleRunner` reference from plan 50):
  - `profile_json_deserializes_camel_case_into_dtos` — the spec's sample JSON → `BotProfile` with `OrgId`, feature flags, and numeric parameters intact.
  - `missing_file_and_malformed_json_fail_with_descriptive_error` — `BotProfileLoader.Load` throws for both.
  - `unknown_feature_id_fails_fast_even_when_disabled` — registry validation rejects a bogus `featureId` regardless of `enabled` (documented decision in §5).
  - `profile_org_not_in_participating_set_fails_fast` and `duplicate_profiles_for_same_org_fail_fast` — runner-side validation throws before any tick.
  - `bot_flag_is_repeatable_and_requires_headless` — `HeadlessOptions.Parse` collects multiple `--bot` paths; `--bot` without `--headless` → `ArgumentException`.
  - `results_parameters_include_effective_bot_config` and `results_without_bots_omit_bot_section` — serialized `SimulationResult` carries the `bots` echo when bots attached, and no `bots` key at all otherwise (part-1 shape preserved).

Run: `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts found — plan aligns with all principles.**

- *Unity 6 + URP only.* No rendering, shader, camera, or any Unity-facing change at all.
- *ECS for all game logic, living in `src/`.* Everything lands in `src/` (`Game.Systems`, new `Game.Bots`, `Game.ConsoleRunner`, `Game.Tests`); the one game-logic touch (`ActionPlayability` extraction) is a behaviour-identical refactor inside the existing ECS pipeline; bots read via `IReadOnlyWorld` and mutate only through the command pipeline. No MonoBehaviour or `Assets/Scripts` change.
- *VContainer is the sole DI mechanism.* No Unity-side registrations touched. Within `src/`, no static mutable singletons are introduced — `BotFeatureRegistry` is an instance the runner composes, `ActionPlayability`/`BotRng` are pure static functions, and the ConsoleRunner composes plain objects in `Main` as it already does (outside the Unity composition root by design).
- *UI Toolkit only.* No UI added or modified; `VisualState` untouched.
- *Plan before implement / Spec before plan.* This plan implements the approved `Docs/Specs/51_bot-org-api/spec.md`; no code precedes it, and implementation is additionally gated on plan 50's landed implementation (first agent step).
- *File organisation.* Plan lives at `Docs/Specs/51_bot-org-api/plan.md`, paired with its spec under shared index 51 (between 50 and 52). Originally indexed 49 (between 48 and 50); reindexed to make room for `Docs/Specs/48_score-component-composition.md`/`Docs/Specs/49_org-scoring/` on `main`.
- *One `.asmdef` per feature folder under `Assets/Scripts/`.* No `Assets/Scripts` folders or asmdefs created or modified. The new `src/Game.Bots` csproj follows the existing `src/` project conventions, deliberately **without** the Plugins `OutputPath` (spec requirement), so `Assets/Plugins/Core/` gains no new DLL.
- *C# code style.* All new/edited code uses tabs, braces always, `_`-prefixed privates, no redundant access modifiers — matching the surrounding files quoted throughout this plan.

Use /implement to start working on the plan or request changes.
