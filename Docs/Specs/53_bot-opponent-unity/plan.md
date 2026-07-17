# Plan: Bot Opponent in Unity + Save-File Action Log

## Spec

Full spec: `Docs/Specs/53_bot-opponent-unity/spec.md`. Condensed summary:

Wire the existing headless `src/Game.Bots` infrastructure (spec 51) so the org the player didn't pick in `CountrySelection` becomes a minimal AI opponent — in the live Unity `Map` scene, and (as a byproduct of the chosen design) in `ConsoleRunner` — and persist every card it actually plays into the save file via a new `[Savable]` `BotActionLog` singleton so a loaded save exposes the bot's real decision history.

Acceptance criteria (condensed):
- `GameLifetimeScope` sets `GameLogicContext.ParticipatingOrganizationIds` to both orgs from `organizations.json`, but only when `initialOrgId` is non-empty (it is `""` whenever the `Map` scene is entered directly without going through `CountrySelection` and no save auto-loads — a real dev-testing path; passing an empty id through `ParticipatingOrganizationIds` makes `InitSystem` throw). This lets `InitSystem` seed a full org for the bot exactly as spec 50 already does for any participating org.
- **Which org(s) are bots is domain state, not a Unity-side heuristic.** `InitSystem`'s existing one-time (`IsInitialized`-guarded) init block marks every participating org whose id isn't `context.InitialOrganizationId` with a new `[Savable]` `BotControlled` marker component, composed onto its `Organization` entity — mirroring the existing `IsDiscovered`-onto-`Country` precedent. This generalizes correctly to N−1 bot orgs for N participating orgs, not just "the first non-player one." On load, `BotControlled` is restored as-is (it's `[Savable]`) and `InitSystem`'s guard prevents recomputation — "explicitly known from save or first init phase," never guessed by `GameLifetimeScope`.
- **No MonoBehaviour hosts the bot.** `Game.Bots` already has a `ProjectReference` to `Game.Main` (`BotCommandSink` consumes `GameLogic`'s `IWriteOnlyCommandAccessor`/`IGameLogger`), so `GameLogic` cannot reference `Game.Bots` back — a circular project reference is a hard build error. Bot construction/ticking instead lives in a new plain C# wrapper, `BotSession` (`src/Game.Bots`), which holds a `GameLogic` plus a keyed-by-org-id collection of `Bot`s. `BotSession.Update(deltaTime)` runs each currently-attached bot's decision phase, calls the wrapped `GameLogic.Update(deltaTime)`, then queries the world for `Organization + BotControlled` and lazily attaches a `Bot` for any newly-seen bot-controlled org (default profile: `discoverAndControl` only) — self-discovering, told nothing by the host. All of a bot's dependencies (features, `BotCommandSink`, seeded `Random`, the `Bot` itself) are private state built once per discovered org — an isolated composition unit, nothing exposed.
- In Unity, `GameLifetimeScope` registers `BotSession` as a VContainer singleton via the forwarded-property pattern (`builder.Register(c => BotSession.Create(c.Resolve<GameLogic>(), rngSeed: ...), Lifetime.Singleton)`) — **no org id or profile passed in at all**. `GameLoopRunner` takes `BotSession` instead of `GameLogic` directly and calls `_botSession.Update(Time.deltaTime)` in `Tick()`, behind the exact same existing `if (_pauseToken.IsPaused) return;` guard — no duplicated pause logic, no second `Update()` method to race against anything.
- `HeadlessRunner` keeps its existing explicit `--bot <path>` mechanism unchanged — it declares org+profile pairs directly via CLI, with no dependency on the `BotControlled` marker at all, and attaches them eagerly at `BotSession.Create` time via an optional `explicitProfiles` parameter. Being host-agnostic, `BotSession` is adopted by `HeadlessRunner` too (refactored to drop its own inline bot-construction code), so bot support isn't headless-specific — though this plan only wires Unity's `Map` scene and `HeadlessRunner`; ConsoleRunner's interactive mode stays unwired (spec 51's exclusion). `--hours-per-tick` (spec 52's eval harness depends on it for simulation-granularity control, unrelated to bots) is left untouched — day-gating inside `Bot` already governs bot frequency regardless of tick resolution.
- `discoverAndControl` (new `IBotFeature`, registered in `BotFeatureRegistry` alongside `baselineCardPlay`) plays **at most one card per tick**: (1) first playable `DiscoverCountryEffectParams` card in `OrgHand` (`SlotIndex` order), else (2) first playable `ControlChangeEffectParams` (`Amount > 0`) card across discovered countries (ordinal `countryId` order, then `SlotIndex`). No RNG use, no gold-reserve floor, no candidate comparison. `BotObservation`/`BotCardView` gain the minimum effect-classification data (computed once, shared, sourced from `ActionConfig`/`EffectConfig`) that the feature consumes rather than re-deriving.
- Day-boundary gating (at most one real decision per simulated in-game day) lives **entirely inside `Bot`** (`src/Game.Bots`) — `GameLogic`, its systems, and `BotSession` stay unaware of it, so every caller (Unity, `HeadlessRunner`) can call at any cadence ≥ once/day with identical results.
- `Bot`'s constructor still requires a seeded `Random` (`BotRng.Create`); `BotSession.Create` supplies a fresh, unpersisted per-session seed since `discoverAndControl` consumes no randomness.
- Bot action log: a single `[Savable]` singleton `BotActionLog { string[] Entries }` in `src/Game.Components`, created unconditionally by `InitSystem` (empty, regardless of whether a bot is attached), resolved via `GameLogic.RefreshSingletonEntities()` (never cached across save/load). A new `GameLogic.RecordBotAction(orgId, featureId, actionId, countryId)` method hides the entity lookup and does the append/FIFO-trim; it is called from `BotSession`'s `BotCommandSink` emission callback (the same mechanism `HeadlessRunner.cs:89` already uses for its own results-JSON log), which is the only place with both mutable-`World` access (via `GameLogic`) and the emitted action's data. Records are delimiter-encoded (`date\x1EorgId\x1EfeatureId\x1EactionId\x1EcountryId`, `\x1E` distinct from the `\x1F` `SaveSystem`/`LoadSystem` already use to join `string[]`), oldest-first, no separate ordering key. `[Savable]`-omission does **not** apply — this history is not config-derivable.
- Retention cap: a configurable `game_settings.json` value (proposed default 500), FIFO eviction trimming the array's front on overflow.
- Observability: the existing ECS Viewer already shows the singleton automatically — no new UI panel is built (explicit out-of-scope item).

Explicitly resolved / not to be re-litigated: no UI indicator for which org is AI-controlled; RNG seed persistence across save/load is deferred (fresh seed per session only); log entries are written only on an actual play, never as a per-tick heartbeat; retention cap is global (one bot org exists today); log storage is one singleton component, not one entity per action; bot hosting is a plain C# `BotSession`, not a MonoBehaviour, for the circular-reference reason above; which org(s) are bots is `InitSystem`-decided domain state (`BotControlled` marker), not a Unity-computed heuristic; `--hours-per-tick` stays as-is.

## Goal

Make the `Map` scene run a second, bot-controlled org alongside the player using only already-built `src/Game.Bots` infrastructure — via a reusable, host-agnostic `BotSession` wrapper rather than Unity-specific glue — and give every bot play a permanent, save-persisted record for post-hoc debugging, without adding any bot intelligence beyond "discover, then raise control" or any new UI.

## Approach

1. **Unblock packaging**: `src/Game.Bots` currently has no `OutputPath`, so its DLL never reaches `Assets/Plugins/Core/` and is invisible to Unity. Add the standard `OutputPath` block (per `.claude/rules/unity/plugins.md`) and rebuild.
2. **Data layer first**: add `BotActionLog` (`src/Game.Components`), the retention-cap config field (`src/Game.Configs`/`Assets/Configs/game_settings.json`), `InitSystem` unconditional creation, and `GameLogic.RecordBotAction` — all headless-testable in `src/Game.Tests` before touching `Assets/`. This layer is unaffected by the hosting-mechanism decision below.
3. **Bot-assembly changes**: extend `Bot`/`BotObservation`/`BotViews` with day-gating and effect classification, add `DiscoverAndControlFeature`, register it. Keep `Bot.ExecuteDecisionTick(world, actionConfig)`'s public signature byte-for-byte identical by adding the new `EffectConfig` dependency as an **optional trailing constructor parameter** on `Bot` — keeps all five existing `new Bot(...)` call sites and all eleven existing `BotObservation.Build(...)` call sites in `src/Game.Tests` compiling unchanged.
4. **Bot-org assignment is domain state, decided once**: rather than a Unity-side "first non-player org" heuristic (which silently stops generalizing past two orgs), `InitSystem`'s existing one-time init block marks every participating org that isn't `context.InitialOrganizationId` with a new `[Savable]` `BotControlled` marker component on its `Organization` entity — decided once at first init, restored as-is on load, never recomputed.
5. **`BotSession`, not a MonoBehaviour**: because `Game.Bots` already references `Game.Main` (a `ProjectReference`, not just a `using`), `GameLogic` cannot reference `Game.Bots` back — bot hosting cannot live inside the `GameLogic` class itself without a circular project reference. Add `BotSession` as a new plain C# class in `src/Game.Bots` that wraps a `GameLogic`, privately owns its bots keyed by org id, and discovers which orgs to drive by querying `Organization + BotControlled` each `Update()` call — no org id or profile is pushed into it from outside. Refactor `HeadlessRunner` to use it too (its existing explicit `--bot` profiles are attached eagerly, unaffected by the marker mechanism), then wire it into Unity: `GameLoopRunner` calls `BotSession.Update` instead of `GameLogic.Update`, and `GameLifetimeScope` registers `BotSession` as a VContainer singleton built from `GameLogic` alone. No new scene GameObjects, no MCP scene edits — `BotSession` is a container registration, not a Component.
6. Tests accompany every `src/` change per project convention; a handful of manual Unity Editor checks close out anything that can't be asserted headlessly (visible bot play in Play mode, ECS Viewer visibility, save/load round trip through the real save UI).

## Section 1 — Agent Steps

- [x] **Package `Game.Bots` into `Assets/Plugins/Core`** — Add the `Release`-configuration `OutputPath`/`AppendTargetFrameworkToOutputPath` `PropertyGroup` to `src/Game.Bots/Game.Bots.csproj`, matching every other Unity-consumed `netstandard2.1` project (see `src/Game.Main/Game.Main.csproj` for the exact block shape). Run `dotnet build src/GlobalStrategy.Core.sln -c Release` and confirm `Assets/Plugins/Core/Game.Bots.dll` (+ `.deps.json`) now exists.

- [x] **Add `BotActionLog` component** — New file `src/Game.Components/BotActionLog.cs`: `[Savable] public struct BotActionLog { public string[] Entries; }`, following the exact shape of `Character.NamePartKeys` (existing `string[]` `[Savable]` field precedent — no `SaveSystem`/`LoadSystem` changes needed).

- [x] **Add retention-cap config field** — `src/Game.Configs/GameSettings.cs`: add `public int BotActionLogRetentionCap { get; set; } = 500;`. `Assets/Configs/game_settings.json`: add `"botActionLogRetentionCap": 500`.

- [x] **`InitSystem` creates `BotActionLog` unconditionally** — In `src/Game.Main/InitSystem.cs`'s `Run(...)`, next to the existing `gameTimeEntity`/`localeEntity`/`settingsEntity` singleton creation block, add: `int botActionLogEntity = world.Create(); world.Add(botActionLogEntity, new BotActionLog { Entries = Array.Empty<string>() });`. Unconditional — created regardless of whether any bot org ends up participating this session.

- [x] **`GameLogic.RecordBotAction`** — In `src/Game.Main/GameLogic.cs`: add field `int _botActionLogEntity = -1;` and a cached `int _botActionLogRetentionCap;` (loaded in the constructor next to `_countryScoreCoefficient`, from `settings.BotActionLogRetentionCap`). Add `_botActionLogEntity = FindEntityWith<BotActionLog>();` to `RefreshSingletonEntities()`. Add a new public method:
  ```csharp
  public void RecordBotAction(string orgId, string featureId, string actionId, string countryId) {
      if (_botActionLogEntity < 0) { return; }
      DateTime date = _gameTimeEntity >= 0 ? _world.Get<GameTime>(_gameTimeEntity).CurrentTime : default;
      string record = string.Join("\x1E", new[] {
          date.ToString("O", System.Globalization.CultureInfo.InvariantCulture), orgId, featureId, actionId, countryId
      });
      ref BotActionLog log = ref _world.Get<BotActionLog>(_botActionLogEntity);
      var existing = log.Entries ?? Array.Empty<string>();
      var appended = new string[existing.Length + 1];
      Array.Copy(existing, appended, existing.Length);
      appended[existing.Length] = record;
      if (appended.Length > _botActionLogRetentionCap) {
          int overflow = appended.Length - _botActionLogRetentionCap;
          var trimmed = new string[_botActionLogRetentionCap];
          Array.Copy(appended, overflow, trimmed, 0, _botActionLogRetentionCap);
          appended = trimmed;
      }
      log.Entries = appended;
  }
  ```
  This is the only code path anything outside `GameLogic` needs to call — `BotSession`'s emission callback calls this and nothing else touches `BotActionLog`.

- [x] **Extract shared "read current date" helper in `BotObservation`** — In `src/Game.Bots/BotObservation.cs`, factor the existing `GameTime` archetype scan (lines building `currentDate` at the top of `Build`) into `internal static DateTime ReadCurrentDate(IReadOnlyWorld world)`, and call it both from `Build()` and (next step) from `Bot`. Keeps "one place that knows how to read the game date" per the codebase's existing single-shared-evaluation convention (spec 51 precedent).

- [x] **Day-boundary gating inside `Bot`** — In `src/Game.Bots/Bot.cs`: add field `DateTime? _lastActedDate;`. At the top of `ExecuteDecisionTick`, before `_sink.BeginDecisionPhase()`: read `DateTime currentDate = BotObservation.ReadCurrentDate(world);`; if `_lastActedDate.HasValue && currentDate.Date == _lastActedDate.Value.Date`, return immediately (no sink call, no observation build, no feature invocation). Otherwise set `_lastActedDate = currentDate;` and proceed as today. This makes `ExecuteDecisionTick` safe to call at any cadence ≥ once/day — `HeadlessRunner`'s existing once-per-day call site and every existing `src/Game.Tests` call site (all of which already tick exactly once per simulated day) are unaffected.

- [x] **Thread `EffectConfig` into `Bot` without breaking its public call-site contract** — Add an **optional trailing** constructor parameter: `public Bot(string orgId, IReadOnlyList<IBotFeature> features, Random rng, BotCommandSink sink, EffectConfig? effectConfig = null)`. Store `_effectConfig = effectConfig ?? new EffectConfig();`. Change the internal `BotObservation.Build(world, actionConfig, OrgId)` call to `BotObservation.Build(world, actionConfig, OrgId, _effectConfig)`. `Bot.ExecuteDecisionTick(IReadOnlyWorld, ActionConfig)`'s own signature is untouched.

- [x] **Effect classification on `BotCardView`** — In `src/Game.Bots/BotViews.cs`, add two fields to `BotCardView`: `public bool DiscoversCountry;` and `public bool RaisesControl;`. In `src/Game.Bots/BotObservation.cs`, add an optional trailing parameter to `Build`: `EffectConfig? effectConfig = null` (defaults keep all 11 existing test call sites compiling). Add a private static helper next to the existing per-card loop:
  ```csharp
  static (bool discoversCountry, bool raisesControl) ClassifyCard(ActionDefinition? def, EffectConfig effectConfig) {
      bool discovers = false, raises = false;
      if (def != null) {
          foreach (var effectId in def.EffectIds) {
              var effect = effectConfig.Find(effectId);
              if (effect is DiscoverCountryEffectParams) { discovers = true; }
              if (effect is ControlChangeEffectParams ccp && ccp.Amount > 0) { raises = true; }
          }
      }
      return (discovers, raises);
  }
  ```
  Call it once per card (the loop already resolves `def = actionConfig.Find(actionId)`), passing `effectConfig ?? new EffectConfig()`, and set the two new fields on both the org-hand and country-hand `BotCardView` construction sites. This is the single shared classification point — `DiscoverAndControlFeature` must only read `card.DiscoversCountry`/`card.RaisesControl`, never call `EffectConfig`/`ActionConfig` itself.

- [x] **`DiscoverAndControlFeature`** — New file `src/Game.Bots/DiscoverAndControlFeature.cs`:
  ```csharp
  public sealed class DiscoverAndControlFeature : IBotFeature {
      public const string Id = "discoverAndControl";
      public string FeatureId => Id;

      public void Tick(IBotObservation obs, IBotCommandSink sink, Random rng) {
          foreach (var card in obs.OrgHand) {
              if (card.IsPlayable && card.DiscoversCountry) {
                  sink.PlayOrgCard(card.ActionId);
                  return;
              }
          }
          foreach (var country in obs.Countries) {
              foreach (var card in country.Hand) {
                  if (card.IsPlayable && card.RaisesControl) {
                      sink.PlayCountryCard(card.ActionId, card.CountryId);
                      return;
                  }
              }
          }
      }
  }
  ```
  Relies on `IBotObservation.OrgHand` already being `SlotIndex`-sorted and `Countries`/`country.Hand` already being ordinal-`countryId`/`SlotIndex`-sorted by `BotObservation.Build` — no re-sorting needed here. `rng` intentionally unused.

- [x] **Register the new feature** — `src/Game.Bots/BotFeatureRegistry.cs`, in `CreateDefault()`: `registry.Register(DiscoverAndControlFeature.Id, parameters => new DiscoverAndControlFeature());` (alongside, not replacing, `baselineCardPlay`).

- [x] **`BotControlled` marker component** — New file `src/Game.Components/BotControlled.cs`: `[Savable] public struct BotControlled { }`, an empty marker mirroring `IsDiscovered`'s role. In `src/Game.Main/InitSystem.cs`'s `Run(...)`, inside the existing `foreach (var orgEntry in participating)` loop (the block that already creates each org's `Organization`/gold/`ControlEffect` entities), add: `if (orgEntry.OrganizationId != context.InitialOrganizationId) { world.Add(orgEntity, new BotControlled()); }`. This runs exactly once per session (the whole `Run` method is `IsInitialized`-guarded) and correctly marks *every* non-player participating org, not just one — the fix for "breaks past two bots." On load, `LoadSystem.Apply` restores `BotControlled` from the save automatically (it's `[Savable]`), and `InitSystem.Update`'s guard prevents `Run` from executing again, so the marking is never recomputed post-load.

- [x] **`BotSession`** — New file `src/Game.Bots/BotSession.cs`. This is the sole place bot construction/ticking is composed, usable by any host (Unity, `ConsoleRunner`). Unity gives it no org/profile info at all — it discovers bot-controlled orgs from the world itself; `HeadlessRunner` keeps declaring profiles explicitly via its existing CLI mechanism, attached eagerly:
  ```csharp
  public delegate void BotActionObserver(string orgId, string featureId, string actionId, string countryId);

  public sealed class BotSession {
      readonly GameLogic _logic;
      readonly int? _rngSeed;
      readonly BotFeatureRegistry _registry;
      readonly IGameLogger? _logger;
      readonly BotActionObserver? _onAction;
      readonly Dictionary<string, Bot> _botsByOrgId = new();

      public GameLogic Logic => _logic;

      public static BotSession Create(
              GameLogic logic,
              int? rngSeed,
              IReadOnlyList<BotProfile>? explicitProfiles = null,
              BotFeatureRegistry? registry = null,
              IGameLogger? logger = null,
              BotActionObserver? onAction = null) {
          var session = new BotSession(logic, rngSeed, registry ?? BotFeatureRegistry.CreateDefault(), logger, onAction);
          if (explicitProfiles != null) {
              foreach (var profile in explicitProfiles) {
                  session.AttachBot(profile.OrgId, profile);
              }
          }
          return session;
      }

      BotSession(GameLogic logic, int? rngSeed, BotFeatureRegistry registry, IGameLogger? logger, BotActionObserver? onAction) {
          _logic = logic;
          _rngSeed = rngSeed;
          _registry = registry;
          _logger = logger;
          _onAction = onAction;
      }

      public void Update(float deltaTime) {
          foreach (var bot in _botsByOrgId.Values) {
              bot.ExecuteDecisionTick(_logic.World, _logic.ActionConfig);
          }
          _logic.Update(deltaTime);
          SyncBotsFromWorld();
      }

      void SyncBotsFromWorld() {
          int[] required = { TypeId<Organization>.Value, TypeId<BotControlled>.Value };
          foreach (var arch in _logic.World.GetMatchingArchetypes(required, null)) {
              var orgs = arch.GetColumn<Organization>();
              for (int i = 0; i < arch.Count; i++) {
                  string orgId = orgs[i].OrganizationId;
                  if (!_botsByOrgId.ContainsKey(orgId)) {
                      AttachBot(orgId, DefaultProfile(orgId));
                  }
              }
          }
      }

      void AttachBot(string orgId, BotProfile profile) {
          var features = new List<IBotFeature>();
          foreach (var featureSetting in profile.Features) {
              if (featureSetting.Enabled) {
                  features.Add(_registry.Create(featureSetting.FeatureId, featureSetting.Parameters));
              }
          }
          var rng = BotRng.Create(_rngSeed, orgId);
          Bot bot = null!;
          BotEmissionCallback callback = (actionId, countryId) => {
              string featureId = bot.CurrentFeatureId;
              _logic.RecordBotAction(orgId, featureId, actionId, countryId);
              _onAction?.Invoke(orgId, featureId, actionId, countryId);
          };
          var sink = new BotCommandSink(orgId, _logic.Commands, _logger, callback);
          bot = new Bot(orgId, features, rng, sink, _logic.EffectConfig);
          _botsByOrgId[orgId] = bot;
      }

      static BotProfile DefaultProfile(string orgId) => new BotProfile {
          OrgId = orgId,
          Features = new List<BotFeatureSetting> {
              new BotFeatureSetting { FeatureId = DiscoverAndControlFeature.Id, Enabled = true, Parameters = new Dictionary<string, double>() }
          }
      };
  }
  ```
  `SyncBotsFromWorld` runs after `_logic.Update` (not before) so a freshly-`InitSystem`-marked or freshly-loaded org is picked up by the time `Update()` returns, ready for the *next* call's decision phase — a one-frame lag on first attach that `Bot`'s own day-gating (starting from `_lastActedDate == null`) makes harmless. With no `BotControlled`-marked orgs (and no `explicitProfiles`), `_botsByOrgId` stays empty and `Update` degenerates to a pure `_logic.Update(deltaTime)` call — additive, non-breaking. All per-bot state is private to `BotSession`, satisfying "isolated composition, deps accessible by the bot only."

- [x] **Refactor `HeadlessRunner` to use `BotSession`** — `src/Game.ConsoleRunner/HeadlessRunner.cs`: replace the manual bot-construction loop (current lines ~59–103: building `features`, `rng`, `sink`, `bot` per profile) with a single `var botSession = BotSession.Create(logic, options.Seed, explicitProfiles: profiles, registry: registry, logger: logger, onAction: (orgId, featureId, actionId, countryId) => { emissionLogByOrgId[orgId].Add(new BotEmission { FeatureId = featureId, ActionId = actionId, CountryId = countryId, Date = logic.VisualState.Time.CurrentTime.ToString("yyyy-MM-dd"), Tick = emissionTick }); });` call — `explicitProfiles` attaches all of them immediately, matching today's eager behavior exactly, with no dependency on `BotControlled` (`HeadlessRunner` already knows its participating orgs from CLI args, independent of the new marker mechanism). Keep the existing loop over `profiles` that builds `botsResult`/`featureResults` (JSON-shape metadata, unrelated to bot execution) and pre-populates `emissionLogByOrgId[orgId] = new List<BotEmission>()` per profiled org. Replace both tick sites — the pre-loop baseline (`foreach (var bot in bots) { bot.ExecuteDecisionTick(...); } logic.Update(0f);`) and the main loop body (same pair) — with `botSession.Update(0f);` and `botSession.Update(deltaTime);` respectively. Output shape (`SimulationResult`, `BotEmission` entries, `BotProfileResult`) is unchanged — this is an internal de-duplication verified by the existing eval-harness/headless tests still passing and a fixed-seed results-JSON diff at the default `--hours-per-tick 24` showing no unexpected changes. `--hours-per-tick` itself is untouched — see the Tests section for the note on why day-gating is a deliberate, documented behavior narrowing at sub-daily tick resolution, not a regression to chase.

- [x] **Update `GameLoopRunner`** — `Assets/Scripts/Unity/DI/GameLoopRunner.cs`: change the constructor parameter from `GameLogic logic` to `BotSession botSession`, storing `_botSession`. In `Start()`, replace `_logic.LoadState(saveName)`/etc. calls with `_botSession.Logic.LoadState(saveName)` (same for the auto-load-latest-save path). In `Tick()`, replace `_logic.Update(Time.deltaTime);` with `_botSession.Update(Time.deltaTime);` — the existing `if (_pauseToken.IsPaused) return;` guard is untouched and now covers bot ticking automatically.

- [x] **Wire `GameLifetimeScope`** — `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, in `Configure`:
  - Load `organizationConfig = new TextAssetConfig<OrganizationConfig>(_organizationsConfigAsset).Load();` (mirrors the existing `domainCountryConfig` load two lines above).
  - Compute `participatingOrgIds`: if `!string.IsNullOrEmpty(initialOrgId)` **and** `organizationConfig.Organizations.Count >= 2`, `participatingOrgIds = [initialOrgId, ...every other org id in config order]`; otherwise `participatingOrgIds = null` (preserves today's behavior exactly — including the empty-`initialOrgId` direct-launch-on-Map-scene case, which must not pass an empty id through to `InitSystem`). No `botOrgId`/profile computation of any kind — that decision now lives entirely in `InitSystem`/`BotSession`.
  - Pass `participatingOrganizationIds: participatingOrgIds` into the existing `new GameLogicContext(...)` call (constructor already accepts this parameter — currently just never supplied here).
  - Register `BotSession` in place of resolving `GameLogic` directly wherever it was previously injected for ticking: `builder.Register(c => BotSession.Create(c.Resolve<GameLogic>(), rngSeed: unchecked((int)DateTime.UtcNow.Ticks), logger: new UnityGameLogger()), Lifetime.Singleton);` — the forwarded-property registration pattern (`.claude/rules/unity/vcontainer.md`), with no `explicitProfiles` argument, so `BotSession` discovers bot orgs from the `BotControlled` marker at runtime. The seed is explicitly not persisted, per the spec's deferred-RNG-persistence decision.
  - No scene/GameObject changes: `BotSession` is a container registration, not a Component, so no MCP `manage_gameobject` step is needed.

- [x] **Assembly reference for `Game.Bots.dll`** — After the packaging step's rebuild, once Unity has imported `Assets/Plugins/Core/Game.Bots.dll` and generated its `.meta` (its GUID is not knowable before that import), refresh Unity and check `read_console(types=["error"])`. If `GS.Game.Bots` types are unresolved in `GS.Unity.DI` (now referenced from `GameLifetimeScope.cs` and `GameLoopRunner.cs`), add `"GUID:<Game.Bots.dll's generated guid>"` to `Assets/Scripts/Unity/DI/GS.Unity.DI.asmdef`'s `references` array, following the existing `Game.Main.dll` precedent (`GUID:c07e33f0394f63c4cb1851b66e1c137e` is already listed there for the same reason). If no error appears, the plugin's "Auto Referenced" import default already covers it and this sub-step is a no-op.

- [x] **`src/Game.Tests` additions** (see Tests section below for the full list) — write alongside each corresponding `src/` change above, not as a separate pass at the end.

## Section 2 — User Steps

### 1. Verify the bot visibly acts in a live Play-mode session

Enter Play mode on the `Map` scene with an org selected in `CountrySelection` (so `organizations.json`'s two orgs both participate). Let time run at normal or fast speed for a few in-game days. Confirm via the ECS Viewer (see step 2) or console logging that the non-player org's hand size decreases / control appears in a country it didn't start with — i.e. the bot is actually playing cards, not just idling. If nothing happens after several in-game days, check `read_console` for a `BotFeatureException` and re-check the `discoverAndControl` card-classification wiring (Step "Effect classification on `BotCardView`" above) against the shipped `discover_country`/`sphere_of_pressure`-style action IDs in `Assets/Configs/action_config.json` and `Assets/Configs/effect_config.json`.

### 2. Confirm `BotActionLog` is visible in the ECS Viewer

Open the in-game debug panel's ECS Viewer (per `Docs/Plans/14_in-game-debug-menu.md`) during the same Play-mode session and locate the `BotActionLog` singleton entity. Confirm `Entries` grows (one delimiter-encoded string per bot play) as the bot acts, with no dedicated viewer code needed — this is the acceptance criterion's "no bot-log-specific code required" claim, verify it holds in practice.

### 3. Confirm save/load round-trip in the Editor

With a non-empty `BotActionLog` (from steps 1–2), trigger a manual save via the existing in-game save UI, then load that save (fresh session or in-session load). Confirm in the ECS Viewer that `Entries` is restored intact and in order, and that any further bot plays after the load append after the restored entries rather than replacing or duplicating them.

### 4. Confirm pause correctness

While paused, watch the ECS Viewer's `BotActionLog`/hand-size state for the bot org across several real-time seconds — confirm nothing changes (no observation built, no entries logged) until unpaused. Since `GameLoopRunner.Tick()`'s existing pause guard now wraps `BotSession.Update` directly (the same single check that already gated `GameLogic.Update`), this should hold by construction — this step is a sanity check, not a search for a separate bot-specific pause bug.

## Tests

All in `src/Game.Tests`, xunit, following existing file/naming conventions (`MultiOrgTestSupport`, `BaselineCardPlayTests`, `SaveLoadRoundTripTests` as direct precedent):

- **`DiscoverAndControlFeatureTests.cs`** (new file, modeled on `BaselineCardPlayTests.cs`):
  - Plays `discover_country`-shaped org card when playable and no country control card is eligible yet (priority order 1).
  - Once a country is discovered with a playable positive `ControlChangeEffectParams` card, plays that (priority order 2), skipping any playable-but-non-qualifying card (e.g. an opinion-modifier card in the same hand) — proves `baselineCardPlay`'s over-eager "any playable card" behavior does not leak in.
  - Plays at most one card per tick.
  - Divergence test: `discoverAndControl` bot vs. passive-org baseline over a simulated period with the same seed — asserts differing end metrics (`OrgMetrics.GetTotalControl`/`GetGold`), mirroring `BaselineCardPlayTests.baseline_bot_changes_metrics_relative_to_passive_run`.
  - Feature-flag gating: profile with `discoverAndControl` disabled yields a run identical to passive, same seed (mirrors `disabled_feature_yields_run_identical_to_passive`).
  - Determinism: two identical seeded runs with an attached `discoverAndControl` bot produce identical end state (extend/parallel `BotDeterminismTests.cs`'s pattern with `DiscoverAndControlFeature` instead of `BaselineCardPlayFeature`).

- **`BotObservationTests.cs` additions**: `BotCardView.DiscoversCountry`/`RaisesControl` are correctly set for a `DiscoverCountryEffectParams` card, a positive `ControlChangeEffectParams` card, a negative/zero-`Amount` `ControlChangeEffectParams` card (must **not** set `RaisesControl`), and a card with unrelated/no effects (both flags false).

- **`BotDayGatingTests.cs`** (new file): `Bot.ExecuteDecisionTick` called multiple times within the same simulated day emits at most one card total; called again after the in-game day advances, it may emit again. Cover the pre-init call (no `GameTime` entity yet) not throwing and not permanently blocking the first real decision.

- **`BotSessionTests.cs`** (new file):
  - `BotSession.Create` with no `explicitProfiles` and no `BotControlled`-marked orgs in the world: `Update(deltaTime)` behaves identically to calling `logic.Update(deltaTime)` directly (no bots constructed, no exceptions, no `BotActionLog` growth).
  - World-discovery mode: after a `GameLogic` with a `BotControlled`-marked org completes its first `Update(0f)` (triggering `InitSystem`), a second `BotSession.Update` call attaches and ticks a bot for that org — confirms the one-call discovery lag is bounded and harmless, and that the default attached profile is `discoverAndControl`-only.
  - Explicit-profile mode (`HeadlessRunner`'s path): `BotSession.Create(logic, seed, explicitProfiles: [...])` attaches immediately, before the first `Update()` call, matching today's eager `HeadlessRunner` behavior.
  - `Update` runs each attached bot's decision phase before `GameLogic.Update` processes commands in that same call — a played card's effects are visible immediately after one `Update` call (mirrors the existing `BotDeterminismTests`/`HeadlessRunner` tick-contract assumption, now exercised through `BotSession`).
  - `onAction` observer fires exactly once per real (non-duplicate) play, with the correct `orgId`/`featureId`/`actionId`/`countryId`, and does not fire for a day-gated no-op tick or a tick where the feature finds nothing eligible.
  - Two `BotSession`s built from two different `GameLogic` instances (each with a `BotControlled`-marked org) do not share bot state — confirms per-session isolation.

- **`InitSystemTests.cs` additions** (or a new `BotControlledTests.cs`, matching whichever convention that file already follows):
  - With three or more participating orgs (`GameLogicContext.ParticipatingOrganizationIds`), `InitSystem` marks every org except `InitialOrganizationId` with `BotControlled` — the direct test that the fix generalizes past two orgs/one bot, not just "the first non-player org."
  - The player's own org (`InitialOrganizationId`) never receives `BotControlled`.
  - After a save/load round trip, `BotControlled` markers are present on exactly the same orgs as before saving, and `InitSystem`'s one-time block does not re-run (no duplicate/changed markers) — extends the existing `SaveLoadRoundTripTests.cs` pattern.

- **`SaveLoadRoundTripTests.cs` addition**: `round_trip_preserves_bot_action_log_entries_and_order` — build a world with a `BotActionLog` singleton holding several delimiter-encoded entries (including one whose `countryId` field is empty, mirroring an org-card play), snapshot, restore, assert `Entries` matches exactly in order.

- **`BotActionLogTests.cs`** (new file, exercises `GameLogic.RecordBotAction` directly, modeled on `GameLogicOrgTests.cs`/`InitSystemTests.cs`):
  - A fresh `GameLogic` (post `Update(0f)`) has a `BotActionLog` singleton with empty `Entries` — proves `InitSystem` creates it unconditionally even with zero bot orgs attached.
  - `RecordBotAction` appends one correctly-delimited record (`date\x1EorgId\x1EfeatureId\x1EactionId\x1EcountryId`) and increments `Entries.Length` by exactly one per call.
  - Retention cap: with `BotActionLogRetentionCap` set low (e.g. 3) via a bespoke `GameSettings`, calling `RecordBotAction` more times than the cap trims from the front (FIFO), leaving the newest N entries in original relative order.
  - After a save/load cycle, `RecordBotAction` continues appending after the restored entries (not resetting, not overwriting) — exercises `RefreshSingletonEntities()` re-resolving `_botActionLogEntity` post-load.

- **`BotActionLogEncodingTests.cs`** (new file): the delimiter-safety assumption is a real constraint, not just documentation —
  - Unit test: a record built from field values containing no `\x1E` round-trips through `SaveSystem`/`LoadSystem` byte-for-byte.
  - Config-sanity test (per `.claude/rules/config_validation.md`'s spirit): every `organizationId` in `organizations.json`, every `actionId` in `action_config.json`, every `countryId` in `country_config.json`, and every registered `BotFeatureRegistry` feature id contains no `\x1E` character — guards the "field values never contain `\x1E` in practice" assumption the spec calls out as worth testing.

- **`HeadlessRunner` refactor regression check**: after replacing its inline bot-construction with `BotSession`, run the full `src/Game.Tests` suite plus a fixed-seed headless smoke run at the **default** `--hours-per-tick 24` (`dotnet run --project src/Game.ConsoleRunner -- --headless --seed <n> --bot <profile> --output ...`) before and after the refactor, diffing the resulting JSON — `SimulationResult`/`BotEmissions`/`Orgs`/`Timeline` must match exactly at this cadence (pure internal de-duplication, not a behavior change).
- **Day-gating is a deliberate behavior change at sub-daily tick resolution, not a regression to hide**: before this plan, `Bot.ExecuteDecisionTick` had no day-based limit at all, so a headless run with `--hours-per-tick < 24` could previously emit multiple bot actions per simulated day. After the day-gating step, that's capped at one/day regardless of tick granularity. This is intentional (day-gating is required for Unity's per-frame cadence and applies uniformly to every caller, per the spec), but it is a real output change for any `--hours-per-tick < 24` run — including future spec-52 eval-harness runs at finer resolution. No code changes to `--hours-per-tick` itself; this is a documentation note so the behavior change isn't mistaken for a bug during the regression check above (which is deliberately pinned to the default cadence, where output is unaffected).

- **Compile check**: confirm all pre-existing `new Bot(...)` (5 call sites) and `BotObservation.Build(...)` (11 call sites) in `src/Game.Tests` and `src/Game.ConsoleRunner` still compile unchanged against the new optional trailing parameters — verified by running the full `src/Game.Tests` suite (`dotnet test src/GlobalStrategy.Core.sln`) after the `Bot`/`BotObservation` signature changes.

Run the full suite (`dotnet test src/GlobalStrategy.Core.sln`) before moving to the Unity-side steps — all of the above must be green first, since the Unity host is a thin, logic-free consumer of code this test pass already proves correct.

## Constitution Check

- **ECS for all game logic (`src/`)** — satisfied. `GameLoopRunner`/`GameLifetimeScope` (MonoBehaviour/composition-root territory) do construction/wiring/ticking only; the actual "which card to play" decision lives entirely in `DiscoverAndControlFeature` (`src/Game.Bots`), day-gating lives entirely in `Bot` (`src/Game.Bots`), *which org is a bot* is decided entirely in `InitSystem` (`src/Game.Main`), and session composition/discovery lives entirely in `BotSession` (`src/Game.Bots`) — no domain logic of any kind sits in `Assets/Scripts/`. `GameLifetimeScope` no longer computes an org id or profile at all. The log append/trim logic lives in `GameLogic.RecordBotAction` (`src/Game.Main`); `BotSession`'s emission callback is a one-line forwarding call.
- **VContainer is the sole DI mechanism** — satisfied without qualification. `BotSession` is registered as a VContainer singleton in `GameLifetimeScope` via the forwarded-property pattern and resolved by `GameLoopRunner` through the container, exactly like any other cross-cutting dependency. `BotSession.Create`'s own internals (`Bot`, `BotCommandSink`, `BotFeatureRegistry`, seeded `Random`) are intentionally *not* container registrations — they are per-bot composition-local values, privately constructed once inside a static factory and never exposed outside the resulting `BotSession` instance, which is itself what the container manages. This resolves the earlier draft's flagged judgment call: there is no more `new`-inside-a-MonoBehaviour pattern to defend, since `BotSession` is the container-registered singleton and its internals are legitimately private implementation detail, not services other code needs to resolve.
- **UI Toolkit only** — satisfied; no UI of any kind is added (explicit spec out-of-scope item).
- **Planning discipline** — this plan itself is the required artifact; spec 53 was approved before this plan was written, per `.claude/commands/plan.md`'s ordering.
- **Specification discipline** — satisfied; this is feature work and it went through `/specify` already (`Docs/Specs/53_bot-opponent-unity/spec.md`).
- **File organisation** — this plan lives at `Docs/Specs/53_bot-opponent-unity/plan.md`, alongside its spec, index 53 unchanged.
- **Assembly structure (one `.asmdef` per feature folder under `Assets/Scripts/`)** — satisfied; no new `Assets/Scripts/` folder is created. `GameLifetimeScope`/`GameLoopRunner` edits stay in the existing `Assets/Scripts/Unity/DI/` folder (`GS.Unity.DI.asmdef`) — and no new Unity-side type is introduced at all, since `BotSession` lives entirely in `src/Game.Bots`.
- **C# code style** — tabs, `_`-prefixed private members, always-braced control flow, no redundant access modifiers apply to every new/edited file above; the implementer must follow `.claude/rules/csharp/code_style.md` throughout (the code sketches in this plan are illustrative of structure/logic, not final formatting).
- **Previously flagged risk, now resolved:** the earlier draft of this plan used a `BotHostComponent` MonoBehaviour and flagged an unresolved risk about its `Update()` ordering relative to `GameLoopRunner.Tick()` (a VContainer `ITickable`) within the same frame. That risk no longer applies: there is exactly one per-frame tick call site (`GameLoopRunner.Tick()`), which now drives `BotSession.Update()` directly — no second `Update()` method exists to race against it.

Use /implement to start working on the plan or request changes.
