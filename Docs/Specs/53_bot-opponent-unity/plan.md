# Plan: Bot Opponent in Unity + Save-File Action Log

## Spec

Full spec: `Docs/Specs/53_bot-opponent-unity/spec.md`. Condensed summary:

Wire the existing headless `src/Game.Bots` infrastructure (spec 51) into the live Unity `Map` scene so the org the player didn't pick in `CountrySelection` becomes a minimal AI opponent, and persist every card it actually plays into the save file via a new `[Savable]` `BotActionLog` singleton so a loaded save exposes the bot's real decision history.

Acceptance criteria (condensed):
- `GameLifetimeScope` sets `GameLogicContext.ParticipatingOrganizationIds` to both orgs from `organizations.json` (player's chosen org + the one remaining org) so `InitSystem` seeds a full org for the bot exactly as spec 50 already does for any participating org.
- A new MonoBehaviour host component (`builder.RegisterComponentInHierarchy<T>()`), containing **no decision logic**, constructs exactly one `Bot` for the non-player org from a `BotProfile` whose single feature is a new `discoverAndControl` `IBotFeature`. It resolves `IReadOnlyWorld`/`ActionConfig` from the already-registered `GameLogic` singleton, mirroring `HeadlessRunner.Run`'s `bot.ExecuteDecisionTick(logic.World, logic.ActionConfig)` call exactly. With fewer than two orgs configured, no bot is attached — additive, non-breaking.
- `discoverAndControl` (registered in `BotFeatureRegistry`, alongside `baselineCardPlay`) plays **at most one card per tick**: (1) first playable `DiscoverCountryEffectParams` card in `OrgHand` (`SlotIndex` order), else (2) first playable `ControlChangeEffectParams` (`Amount > 0`) card across discovered countries (ordinal `countryId` order, then `SlotIndex`). No RNG use, no gold-reserve floor, no candidate comparison. `BotObservation`/`BotCardView` gain the minimum effect-classification data (computed once, shared, sourced from `ActionConfig`/`EffectConfig`) that the feature consumes rather than re-deriving.
- The bot host calls `bot.ExecuteDecisionTick` **before** `GameLogic.Update` in the same frame, subject to the exact same `PauseToken.IsPaused` guard `GameLoopRunner.Tick()` already uses. Day-boundary gating (at most one real decision per simulated in-game day) lives **entirely inside `Bot`** (`src/Game.Bots`) — `GameLogic`, its systems, and the host component stay unaware of it, so `HeadlessRunner`'s existing once-per-day call site needs no change. `Bot`'s constructor still requires a seeded `Random` (`BotRng.Create`); the Unity host supplies a fresh, unpersisted per-session seed since `discoverAndControl` consumes no randomness.
- Bot action log: a single `[Savable]` singleton `BotActionLog { string[] Entries }` in `src/Game.Components`, created unconditionally by `InitSystem` (empty, regardless of whether a bot is attached), resolved via `GameLogic.RefreshSingletonEntities()` (never cached across save/load). A new `GameLogic.RecordBotAction(orgId, featureId, actionId, countryId)` method hides the entity lookup and does the append/FIFO-trim; it is called from the Unity host's `BotCommandSink` `BotEmissionCallback` (the same mechanism `HeadlessRunner.cs:89` already uses for its own results-JSON log), which is the only place with both mutable-`World` access and the emitted action's data. Records are delimiter-encoded (`date\x1EorgId\x1EfeatureId\x1EactionId\x1EcountryId`, `\x1E` distinct from the `\x1F` `SaveSystem`/`LoadSystem` already use to join `string[]`), oldest-first, no separate ordering key. `[Savable]`-omission does **not** apply — this history is not config-derivable.
- Retention cap: a configurable `game_settings.json` value (proposed default 500), FIFO eviction trimming the array's front on overflow.
- Observability: the existing ECS Viewer already shows the singleton automatically — no new UI panel is built (explicit out-of-scope item).

Explicitly resolved / not to be re-litigated: no UI indicator for which org is AI-controlled; RNG seed persistence across save/load is deferred (fresh seed per session only); log entries are written only on an actual play, never as a per-tick heartbeat; retention cap is global (one bot org exists today); log storage is one singleton component, not one entity per action.

## Goal

Make the `Map` scene run a second, bot-controlled org alongside the player using only already-built `src/Game.Bots` infrastructure, and give every bot play a permanent, save-persisted record for post-hoc debugging — without adding any bot intelligence beyond "discover, then raise control" or any new UI.

## Approach

1. **Unblock packaging**: `src/Game.Bots` currently has no `OutputPath`, so its DLL never reaches `Assets/Plugins/Core/` and is invisible to Unity. Add the standard `OutputPath` block (per `.claude/rules/unity/plugins.md`) and rebuild.
2. **Data layer first**: add `BotActionLog` (`src/Game.Components`), the retention-cap config field (`src/Game.Configs`/`Assets/Configs/game_settings.json`), `InitSystem` unconditional creation, and `GameLogic.RecordBotAction` — all headless-testable in `src/Game.Tests` before touching `Assets/`.
3. **Bot-assembly changes**: extend `Bot`/`BotObservation`/`BotViews` with day-gating and effect classification, add `DiscoverAndControlFeature`, register it. Keep `Bot.ExecuteDecisionTick(world, actionConfig)`'s public signature byte-for-byte identical (the spec requires mirroring `HeadlessRunner`'s call site exactly) by adding the new `EffectConfig` dependency as an **optional trailing constructor parameter** on `Bot` instead — this keeps all five existing `new Bot(...)` call sites and all eleven existing `BotObservation.Build(...)` call sites in `src/Game.Tests` compiling unchanged.
4. **Unity wiring last**: `GameLifetimeScope` computes the bot org id and both participating org ids from `organizations.json`, registers a new `BotSessionConfig` plain-data instance and a new `BotHostComponent` MonoBehaviour that owns construction/ticking of the `Bot` — mirroring `GameLoopRunner`'s pause guard via the same shared `PauseToken` singleton (per `.claude/rules/unity/vcontainer.md`'s "Sharing State Between MonoBehaviour and Pure C# Classes" pattern), never a second one.
5. Tests accompany every `src/` change per project convention; a handful of manual Unity Editor checks close out anything that can't be asserted headlessly (visible bot play in Play mode, ECS Viewer visibility, save/load round trip through the real save UI).

## Section 1 — Agent Steps

- [ ] **Package `Game.Bots` into `Assets/Plugins/Core`** — Add the `Release`-configuration `OutputPath`/`AppendTargetFrameworkToOutputPath` `PropertyGroup` to `src/Game.Bots/Game.Bots.csproj`, matching every other Unity-consumed `netstandard2.1` project (see `src/Game.Main/Game.Main.csproj` for the exact block shape). Run `dotnet build src/GlobalStrategy.Core.sln -c Release` and confirm `Assets/Plugins/Core/Game.Bots.dll` (+ `.deps.json`) now exists.

- [ ] **Add `BotActionLog` component** — New file `src/Game.Components/BotActionLog.cs`: `[Savable] public struct BotActionLog { public string[] Entries; }`, following the exact shape of `Character.NamePartKeys` (existing `string[]` `[Savable]` field precedent — no `SaveSystem`/`LoadSystem` changes needed).

- [ ] **Add retention-cap config field** — `src/Game.Configs/GameSettings.cs`: add `public int BotActionLogRetentionCap { get; set; } = 500;`. `Assets/Configs/game_settings.json`: add `"botActionLogRetentionCap": 500`.

- [ ] **`InitSystem` creates `BotActionLog` unconditionally** — In `src/Game.Main/InitSystem.cs`'s `Run(...)`, next to the existing `gameTimeEntity`/`localeEntity`/`settingsEntity` singleton creation block, add: `int botActionLogEntity = world.Create(); world.Add(botActionLogEntity, new BotActionLog { Entries = Array.Empty<string>() });`. Unconditional — created regardless of whether any bot org ends up participating this session.

- [ ] **`GameLogic.RecordBotAction`** — In `src/Game.Main/GameLogic.cs`: add field `int _botActionLogEntity = -1;` and `double`-style cached setting `int _botActionLogRetentionCap;` (loaded in the constructor next to `_countryScoreCoefficient`, from `settings.BotActionLogRetentionCap`). Add `_botActionLogEntity = FindEntityWith<BotActionLog>();` to `RefreshSingletonEntities()`. Add a new public method:
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
  This is the only code path anything outside `GameLogic` needs to call — the Unity host's emission callback calls this and nothing else touches `BotActionLog`.

- [ ] **Extract shared "read current date" helper in `BotObservation`** — In `src/Game.Bots/BotObservation.cs`, factor the existing `GameTime` archetype scan (lines building `currentDate` at the top of `Build`) into `internal static DateTime ReadCurrentDate(IReadOnlyWorld world)`, and call it both from `Build()` and (next step) from `Bot`. Keeps "one place that knows how to read the game date" per the codebase's existing single-shared-evaluation convention (spec 51 precedent).

- [ ] **Day-boundary gating inside `Bot`** — In `src/Game.Bots/Bot.cs`: add field `DateTime? _lastActedDate;`. At the top of `ExecuteDecisionTick`, before `_sink.BeginDecisionPhase()`: read `DateTime currentDate = BotObservation.ReadCurrentDate(world);`; if `_lastActedDate.HasValue && currentDate.Date == _lastActedDate.Value.Date`, return immediately (no sink call, no observation build, no feature invocation). Otherwise set `_lastActedDate = currentDate;` and proceed as today. This makes `ExecuteDecisionTick` safe to call at any cadence ≥ once/day — `HeadlessRunner`'s existing once-per-day call site and every existing `src/Game.Tests` call site (all of which already tick exactly once per simulated day) are unaffected.

- [ ] **Thread `EffectConfig` into `Bot` without breaking its public call-site contract** — Add an **optional trailing** constructor parameter: `public Bot(string orgId, IReadOnlyList<IBotFeature> features, Random rng, BotCommandSink sink, EffectConfig? effectConfig = null)`. Store `_effectConfig = effectConfig ?? new EffectConfig();`. Change the internal `BotObservation.Build(world, actionConfig, OrgId)` call to `BotObservation.Build(world, actionConfig, OrgId, _effectConfig)`. `Bot.ExecuteDecisionTick(IReadOnlyWorld, ActionConfig)`'s own signature is untouched — this preserves the exact `bot.ExecuteDecisionTick(logic.World, logic.ActionConfig)` mirror the spec requires.

- [ ] **Effect classification on `BotCardView`** — In `src/Game.Bots/BotViews.cs`, add two fields to `BotCardView`: `public bool DiscoversCountry;` and `public bool RaisesControl;`. In `src/Game.Bots/BotObservation.cs`, add an optional trailing parameter to `Build`: `EffectConfig? effectConfig = null` (defaults keep all 11 existing test call sites compiling). Add a private static helper next to the existing per-card loop:
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

- [ ] **`DiscoverAndControlFeature`** — New file `src/Game.Bots/DiscoverAndControlFeature.cs`:
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

- [ ] **Register the new feature** — `src/Game.Bots/BotFeatureRegistry.cs`, in `CreateDefault()`: `registry.Register(DiscoverAndControlFeature.Id, parameters => new DiscoverAndControlFeature());` (alongside, not replacing, `baselineCardPlay`).

- [ ] **Update `HeadlessRunner`'s `Bot` construction** — `src/Game.ConsoleRunner/HeadlessRunner.cs:99`: `bot = new Bot(orgId, features, rng, sink, logic.EffectConfig);` — passes the now-available `EffectConfig` so any headless profile using `discoverAndControl` classifies cards correctly too (headless runs are otherwise untouched, per spec's explicit out-of-scope on `HeadlessRunner` changes — this is the one required plumbing line, not new behavior).

- [ ] **`BotSessionConfig`** — New file `Assets/Scripts/Unity/DI/BotSessionConfig.cs`:
  ```csharp
  namespace GS.Unity.DI {
      public class BotSessionConfig {
          public string BotOrgId = "";
          public bool HasBot => !string.IsNullOrEmpty(BotOrgId);
      }
  }
  ```
  Plain data holder, registered via `builder.RegisterInstance(...)` — not a service, so no VContainer-DI concerns beyond that.

- [ ] **`BotHostComponent`** — New file `Assets/Scripts/Unity/DI/BotHostComponent.cs`. `[Inject]`-method-injected MonoBehaviour (per `.claude/rules/unity/vcontainer.md`) taking `GameLogic`, `ECS.Viewer.PauseToken`, `BotSessionConfig`. If `!sessionConfig.HasBot`, it does nothing for the rest of the session (no `Bot` constructed, `Update()` no-ops). Otherwise, in the `[Inject]` method: build `BotFeatureRegistry.CreateDefault()`, a single-feature `BotProfile { OrgId = botOrgId, Features = [{ FeatureId = DiscoverAndControlFeature.Id, Enabled = true }] }`, instantiate its one enabled feature via the registry, derive a fresh per-session seed (e.g. `unchecked((int)DateTime.UtcNow.Ticks)` — explicitly not persisted, per the spec's deferred-RNG-persistence decision) through `BotRng.Create`, and construct `BotCommandSink(botOrgId, logic.Commands, new UnityGameLogger(), emissionCallback)` where `emissionCallback` closes over the not-yet-assigned `Bot` local (same `Bot bot = null; BotEmissionCallback emissionCallback = (actionId, countryId) => logic.RecordBotAction(botOrgId, bot.CurrentFeatureId, actionId, countryId); ...; bot = new Bot(...);` two-step pattern `HeadlessRunner.cs:88-99` already uses). `Update()`: no-op if no bot constructed or `pauseToken.IsPaused`; otherwise `_bot.ExecuteDecisionTick(_logic.World, _logic.ActionConfig)`. Contains no card-selection/eligibility logic of any kind — purely construction plumbing + a one-line per-frame call, matching the Constitution's "MonoBehaviours are presentation and input glue only."
  **Ordering note (flag for implementer):** `GameLoopRunner.Tick()` (which calls `GameLogic.Update`) is a pure-C# `ITickable` dispatched by VContainer's own `PlayerLoopSystem` hook, not a `MonoBehaviour.Update()`. This component's `Update()` runs via Unity's native script-update phase instead. The relative order between the two within one rendered frame is not verified by reading source in this plan — confirm empirically in Play mode (see Section 2, step 1). Because `Bot`'s day-gating limits it to one real decision per simulated day, a worst-case one-frame skew (bot's push landing one frame after that frame's `GameLogic.Update` already ran) has no observable gameplay effect — it is simply processed on the very next frame's `Update` instead — but note this if the literal "same frame" wording matters for a stricter future consumer.

- [ ] **Wire `GameLifetimeScope`** — `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, in `Configure`:
  - Load `organizationConfig = new TextAssetConfig<OrganizationConfig>(_organizationsConfigAsset).Load();` (mirrors the existing `domainCountryConfig` load two lines above).
  - Compute `participatingOrgIds` and `botOrgId`: if `organizationConfig.Organizations.Count >= 2`, `participatingOrgIds = [initialOrgId, ...every other org id in config order]` and `botOrgId` = the first org id that isn't `initialOrgId`; otherwise `participatingOrgIds = null` and `botOrgId = ""` (preserves today's single-org behavior exactly, per the spec's additive-only requirement).
  - Pass `participatingOrganizationIds: participatingOrgIds` into the existing `new GameLogicContext(...)` call (constructor already accepts this parameter — currently just never supplied here).
  - `builder.RegisterInstance(new BotSessionConfig { BotOrgId = botOrgId });`
  - `builder.RegisterComponentInHierarchy<BotHostComponent>();`
  - Add a `BotHostComponent` GameObject under the `Map` scene's `GameLifetimeScope` hierarchy (or confirm `RegisterComponentInHierarchy` can resolve it if the scene doesn't yet have one — Unity scene edit is covered in Section 2 if no MCP session is available at implementation time; prefer doing it via MCP `manage_gameobject`/`manage_components` per `.claude/rules/unity/mcp_usage.md` if Unity Editor is connected).

- [ ] **Assembly reference for `Game.Bots.dll`** — After Step 1's rebuild, once Unity has imported `Assets/Plugins/Core/Game.Bots.dll` and generated its `.meta` (its GUID is not knowable before that import), refresh Unity and check `read_console(types=["error"])`. If `GS.Game.Bots` types are unresolved in `GS.Unity.DI`, add `"GUID:<Game.Bots.dll's generated guid>"` to `Assets/Scripts/Unity/DI/GS.Unity.DI.asmdef`'s `references` array, following the existing `Game.Main.dll` precedent (`GUID:c07e33f0394f63c4cb1851b66e1c137e` is already listed there for the same reason). If no error appears, the plugin's "Auto Referenced" import default already covers it and this sub-step is a no-op.

- [ ] **`src/Game.Tests` additions** (see Tests section below for the full list) — write alongside each corresponding `src/` change above, not as a separate pass at the end.

## Section 2 — User Steps

### 1. Verify the bot visibly acts in a live Play-mode session

Enter Play mode on the `Map` scene with an org selected in `CountrySelection` (so `organizations.json`'s two orgs both participate). Let time run at normal or fast speed for a few in-game days. Confirm via the ECS Viewer (see step 2) or console logging that the non-player org's hand size decreases / control appears in a country it didn't start with — i.e. the bot is actually playing cards, not just idling. If nothing happens after several in-game days, check `read_console` for a `BotFeatureException` and re-check the `discoverAndControl` card-classification wiring (Step "Effect classification on `BotCardView`" above) against the shipped `discover_country`/`sphere_of_pressure`-style action IDs in `Assets/Configs/action_config.json` and `Assets/Configs/effect_config.json`.

### 2. Confirm `BotActionLog` is visible in the ECS Viewer

Open the in-game debug panel's ECS Viewer (per `Docs/Plans/14_in-game-debug-menu.md`) during the same Play-mode session and locate the `BotActionLog` singleton entity. Confirm `Entries` grows (one delimiter-encoded string per bot play) as the bot acts, with no dedicated viewer code needed — this is the acceptance criterion's "no bot-log-specific code required" claim, verify it holds in practice.

### 3. Confirm save/load round-trip in the Editor

With a non-empty `BotActionLog` (from steps 1–2), trigger a manual save via the existing in-game save UI, then load that save (fresh session or in-session load). Confirm in the ECS Viewer that `Entries` is restored intact and in order, and that any further bot plays after the load append after the restored entries rather than replacing or duplicating them.

### 4. Confirm pause correctness

While paused, watch the ECS Viewer's `BotActionLog`/hand-size state for the bot org across several real-time seconds — confirm nothing changes (no observation built, no entries logged) until unpaused, matching `GameLoopRunner.Tick()`'s existing pause behavior.

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

- **`SaveLoadRoundTripTests.cs` addition**: `round_trip_preserves_bot_action_log_entries_and_order` — build a world with a `BotActionLog` singleton holding several delimiter-encoded entries (including one whose `countryId` field is empty, mirroring an org-card play), snapshot, restore, assert `Entries` matches exactly in order.

- **`BotActionLogTests.cs`** (new file, exercises `GameLogic.RecordBotAction` directly, modeled on `GameLogicOrgTests.cs`/`InitSystemTests.cs`):
  - A fresh `GameLogic` (post `Update(0f)`) has a `BotActionLog` singleton with empty `Entries` — proves `InitSystem` creates it unconditionally even with zero bot orgs attached.
  - `RecordBotAction` appends one correctly-delimited record (`date\x1EorgId\x1EfeatureId\x1EactionId\x1EcountryId`) and increments `Entries.Length` by exactly one per call.
  - Retention cap: with `BotActionLogRetentionCap` set low (e.g. 3) via a bespoke `GameSettings`, calling `RecordBotAction` more times than the cap trims from the front (FIFO), leaving the newest N entries in original relative order.
  - After a save/load cycle, `RecordBotAction` continues appending after the restored entries (not resetting, not overwriting) — exercises `RefreshSingletonEntities()` re-resolving `_botActionLogEntity` post-load.

- **`BotActionLogEncodingTests.cs`** (new file): the delimiter-safety assumption is a real constraint, not just documentation —
  - Unit test: a record built from field values containing no `\x1E` round-trips through `SaveSystem`/`LoadSystem` byte-for-byte.
  - Config-sanity test (per `.claude/rules/config_validation.md`'s spirit): every `organizationId` in `organizations.json`, every `actionId` in `action_config.json`, every `countryId` in `country_config.json`, and every registered `BotFeatureRegistry` feature id contains no `\x1E` character — guards the "field values never contain `\x1E` in practice" assumption the spec calls out as worth testing.

- **`HeadlessRunner`/existing test compile check**: confirm all pre-existing `new Bot(...)` (5 call sites) and `BotObservation.Build(...)` (11 call sites) in `src/Game.Tests` and `src/Game.ConsoleRunner` still compile unchanged against the new optional trailing parameters — this is a compile-time assertion, not a new runtime test, but must be verified by running the full `src/Game.Tests` suite (`dotnet test src/GlobalStrategy.Core.sln`) after the `Bot`/`BotObservation` signature changes.

Run the full suite (`dotnet test src/GlobalStrategy.Core.sln`) before moving to the Unity-side steps — all of the above must be green first, since the Unity host is a thin, logic-free consumer of code this test pass already proves correct.

## Constitution Check

- **ECS for all game logic (`src/`)** — satisfied. `BotHostComponent` (MonoBehaviour) does construction/wiring/ticking only; the actual "which card to play" decision lives entirely in `DiscoverAndControlFeature` (`src/Game.Bots`) and the day-gating lives entirely in `Bot` (`src/Game.Bots`). The log append/trim logic lives in `GameLogic.RecordBotAction` (`src/Game.Main`), not in the MonoBehaviour's emission-callback lambda — the lambda is a one-line forwarding call.
- **VContainer is the sole DI mechanism** — satisfied for all cross-system dependencies (`GameLogic`, `PauseToken`, `BotSessionConfig` all resolved through the container). **Judgment call flagged for attention:** the `Bot`/`BotCommandSink`/`BotFeatureRegistry`/seeded `Random` instances inside `BotHostComponent`'s `[Inject]` method are constructed with `new`, not registered in the container. This mirrors the existing `HeadlessRunner.Run` precedent exactly (which also directly `new`s `Bot`/`BotCommandSink` rather than resolving them from any container) — these are per-session runtime *values*, not cross-cutting singleton *services* other components need to resolve, so this reading treats them as outside the "no `new` for singleton services" rule's intent rather than a violation. Flagging so the user can confirm this reading before implementation, since it's the one place this plan deviates from strict "everything through the container."
- **UI Toolkit only** — satisfied; no UI of any kind is added (explicit spec out-of-scope item).
- **Planning discipline** — this plan itself is the required artifact; spec 53 was approved before this plan was written, per `.claude/commands/plan.md`'s ordering.
- **Specification discipline** — satisfied; this is feature work and it went through `/specify` already (`Docs/Specs/53_bot-opponent-unity/spec.md`).
- **File organisation** — this plan lives at `Docs/Specs/53_bot-opponent-unity/plan.md`, alongside its spec, index 53 unchanged.
- **Assembly structure (one `.asmdef` per feature folder under `Assets/Scripts/`)** — satisfied; no new `Assets/Scripts/` folder is created. `BotSessionConfig`/`BotHostComponent` join the existing `Assets/Scripts/Unity/DI/` folder (`GS.Unity.DI.asmdef`), which is the correct home for composition-root/session-wiring types alongside `GameLifetimeScope`/`GameLoopRunner`.
- **C# code style** — tabs, `_`-prefixed private members, always-braced control flow, no redundant access modifiers apply to every new/edited file above; the implementer must follow `.claude/rules/csharp/code_style.md` throughout (the code sketches in this plan are illustrative of structure/logic, not final formatting).
- **Unresolved risk flagged for the user, not a constitution violation per se:** the `BotHostComponent.Update()` vs. `GameLoopRunner.Tick()` same-frame ordering question (see the `BotHostComponent` step above) cannot be resolved by reading source in this environment — it needs empirical confirmation in Play mode (Section 2, step 1) or a follow-up decision to move the tick call site if strict same-frame ordering turns out to matter more than this plan currently assumes.

Use /implement to start working on the plan or request changes.
