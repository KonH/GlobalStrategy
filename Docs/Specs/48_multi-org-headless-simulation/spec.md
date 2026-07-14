# Spec: Multi-Org Headless Simulation Foundation

## Feature Intent

As a developer building AI-controlled ("bot") organizations, I want the game logic to support multiple simultaneously active organizations, a seedable deterministic RNG, and a fast non-interactive console simulation runner that emits structured JSON results, so that later parts of the bot-org initiative (bot API in part 2, eval harness in part 3) can run reproducible headless competitions between orgs and score them by total control.

This is part 1 of 3. It delivers only the simulation foundation — no bot decision logic, no scoring formula, no eval statistics.

**Scoring reference.** The scoring logic for the bot initiative must build on the existing (not yet merged) scoring work rather than inventing a parallel mechanism: `Docs/Specs/47_country-scoring/` (spec + plan on branch `claude/country-scoring-spec-tpf83x`), which itself depends on `Docs/Specs/46_province-population/` (branch `feature/province-population-spec`). That plan establishes the patterns any later org-scoring work should follow: a derived, non-`[Savable]` score component; a static system with a month-boundary-gated `Update` plus a forced ungated `Recompute` invoked at init and on load; a global coefficient in `game_settings.json`; and a `GetScore`-style query API. This spec deliberately emits only raw per-org metrics (control, gold) in its results JSON — the derived score arrives in part 3 and should be consistent with that plan.

## Acceptance Criteria

### Multi-org world initialization

- **Given** a `GameLogicContext` configured with a set of participating organization ids (a new context input; when unspecified it defaults to exactly the existing single `InitialOrganizationId`, preserving current behaviour) **When** `InitSystem` runs **Then** for *each* participating org (validated against `organizations.json`, e.g. `Illuminati` and `Masons`) the world contains: an `Organization` entity, a `gold` `Resource` entity with the org's `InitialGold`, a base `ControlEffect` (`base_{orgId}`) in its `HqCountryId`, org character slots (`master` + `InitialAgentSlots` agents, filled the same way the player org's slots are today), an org `CardDeck`/`CardHand` with the initial hand drawn from the org's action pool, and per-country `CardDeck`s with country action cards — all keyed by that org's id (`ActionCard.OwnerId` / `OrgContext.OrgId` / `CardDeck.OrgId`).
- **Given** a participating org id that does not exist in `organizations.json` **When** the session starts **Then** the run fails fast with a clear error (logged via `IGameLogger` and, in headless mode, a non-zero exit code) rather than silently continuing with fewer orgs.
- **Given** the existing Unity flow (a single `InitialOrganizationId`, no participating-orgs input) **When** a game starts **Then** the world is initialized exactly as today — one org, identical entity set — so no Unity-side (`Assets/`) change is required.

### Multi-org gameplay via commands

- **Given** two participating orgs each holding cards in hand **When** each pushes `PlayCardActionCommand { OrgId = <own id>, ActionId, CountryId }` in the same or different ticks **Then** the full card pipeline (condition check, cost deduction against that org's gold, `ControlEffect`/`ResourceEffect` creation, removal from that org's hand, redraw into that org's hand via `DrawCardSystem`) resolves independently per org, with no cross-org interference in hands, decks, gold, or effects.
- **Given** two orgs with `ControlEffect`s in the same country **When** a game-month boundary passes **Then** `ControlSystem` grants each org its proportional share of the country's base monthly gold and deducts the total from the country (this is already org-keyed today — the criterion is a regression test in `src/Game.Tests` covering ≥2 orgs in one country).
- **Given** a multi-org session **When** `ChangeControlCommand` / `DebugChangeGoldCommand` are pushed with a specific `OrgId` **Then** they mutate only that org's control/gold.
- **Given** a multi-org session **When** global (non-org-owned) commands are pushed — `PauseCommand`, `UnpauseCommand`, `ChangeTimeMultiplierCommand`, `ChangeLocaleCommand`, `ChangeLensCommand`, `SaveGameCommand`, selection commands — **Then** they behave as today and are understood to be *runner-owned*: nothing in this part prevents a future bot from pushing them (enforcement is part 2's observation/command facade), but the spec records that they are not per-org actions.
- **Given** a multi-org session in Unity **When** `VisualStateConverter.Update` runs **Then** `VisualState` continues to reflect only the single designated player/view org (the existing `_orgEntity` selection); no multi-org data is added to `VisualState`.

### Per-org discovery

- **Given** the current model — a global `[Savable] IsDiscovered` country flag and a `DiscoverCountryEffect` created with no org attribution — **When** this feature lands **Then** discovery is tracked *per org*: which countries an org has discovered is that org's own state, and one org's discovery does not unlock anything for another org.
- **Given** an org plays a card whose effect is `DiscoverCountryEffectParams` **When** `CreateActionEffectSystem` creates the `DiscoverCountryEffect` **Then** the effect carries the acting org's id (today it is created org-blind, discarding the `orgId` already available at the creation site), and `DiscoverCountrySystem` adds the discovered country to *that org's* discovered set only.
- **Given** `DiscoverCountrySystem`'s proximity weighting (currently anchored to the globally selected player country) **When** a discovery roll resolves for an org **Then** the weighting anchor is per-org: the player/view org keeps today's behaviour (the selected player country, when one exists), and any other org anchors to its own `HqCountryId`; with no anchor available the weighting falls back to uniform, as it does today.
- **Given** the existing single-org Unity flow **When** the game runs with only the player org participating **Then** observable discovery behaviour is unchanged, and `VisualState`'s discovered-countries data (same shape as today) is sourced from the view org's discovered set.
- **Given** the discovery component's savable shape changes for per-org tracking **Then** no backward compatibility with pre-change save files is required — old saves may lose discovery progress or fail to load, and no migration/attribution logic is written. The save *system* (header, storage, serializer) is otherwise unchanged.

### Deterministic seeded RNG

- **Given** a `GameLogicContext` carrying an integer RNG seed (a new optional context input) **When** `GameLogic` is constructed **Then** its `Random` is created from that seed instead of the current unseeded `new Random()`; when no seed is provided, behaviour is unchanged (unseeded — Unity path unaffected).
- **Given** two `GameLogic` instances created with the same seed, same configs, and driven with an identical sequence of `Update(deltaTime)` calls and identical command sequences **When** both runs complete **Then** their end states are identical on all reported metrics: per-org total control, per-org gold, per-country control breakdown, game date, and hand contents. This must be verified by an xunit test in `src/Game.Tests` (following the style of `GameLogicOrgTests`).
- **Given** the same paired-run setup **When** per-month timeline samples are compared **Then** the full timelines (not just end states) are identical.
- The determinism contract covers gameplay state only. `_sessionId` (`Guid.NewGuid()`) and `SaveSystem`'s `DateTime.UtcNow` are cosmetic/save-only and excluded — headless runs do not save. If the determinism test exposes other nondeterminism (e.g. unordered dictionary iteration in a system), fixing that specific source is in scope, because the test is the acceptance gate.

### Headless console runner

- **Given** the current `src/Game.ConsoleRunner` **When** it is run today **Then** it crashes at startup: `Program.cs` constructs `FileConfig` sources for `data/game_settings.json`, `data/resource_config.json`, and `data/organizations.json`, but `data/` ships only `geojson_world.json`, `map_entry_config.json`, and `country_config.json`. Fixing this — loading the full config set the current `GameLogicContext` needs (`game_settings`, `resource_config`, `organizations`, plus `character_config`, `action_config`, `effect_config`, `province_config`, which currently silently fall back to empty configs and would leave a headless run with no cards) — is in scope.
- **Given** the runner needs game configs **When** no config directory is specified **Then** it loads them from the repo's `Assets/Configs/` (single source of truth — the checked-in `data/` snapshot already drifted and broke the runner); a CLI flag can point at a different config directory. The `data/` snapshot copies of configs that exist in `Assets/Configs/` are removed rather than maintained in parallel.
- **Given** a non-view participating org in a headless session **When** its character slots are created **Then** they are initialized the same way the player org's are today (including `IsAvailable`), so future bots can fill and use agent slots identically to the player.
- **Given** the runner invoked in headless mode (e.g. `--headless` plus arguments; exact CLI syntax is a plan decision) with a seed, an output path, and at least one stop condition **When** it runs with no explicit org list **Then** all orgs defined in `organizations.json` participate (currently `Illuminati` + `Masons`); an optional CLI flag narrows the set to explicitly listed org ids.
- **Given** the runner invoked in headless mode with a seed, participating org ids, an output path, and at least one stop condition **When** it runs **Then** it executes `GameLogic.Update` in a tight loop with a fixed `deltaTime` — no `Console.ReadLine`, no artificial sleeps — and no `ViewerServer` is started (the interactive mode with ViewerServer remains available as the default / behind the existing behaviour).
- **Given** headless mode defaults **When** no explicit tick parameters are passed **Then** each tick advances exactly one game day (`deltaTime = 1.0` with speed multiplier `24`, or equivalent). The tick contract must never advance more than one month per tick: `ResourceSystem`/`ControlSystem`/`OpinionSystem` detect only a single month-boundary crossing per update, so a coarser tick (e.g. multiplier `720` = 30 days) silently skips monthly income. A guard or documented parameter validation must prevent month-skipping tick configurations.
- **Given** a configured end game-date **When** the simulated `GameTime` reaches or passes it **Then** the run stops with end reason `dateReached`.
- **Given** a configured max tick count **When** that many `Update` calls have executed **Then** the run stops with end reason `maxTicks`.
- **Given** a wall-clock safety timeout (always active; sensible default if unspecified) **When** real elapsed time exceeds it **Then** the run stops with end reason `timeout` and still writes the results JSON with whatever was sampled so far.
- **Given** a completed headless run **Then** the process exits with code `0`; a startup/config failure exits non-zero with the error on stderr.

### Structured JSON results

- **Given** a completed headless run **When** the results file is written to the specified output path **Then** it is a single JSON document with camelCase field names containing at minimum:
  - `seed` and the full session parameters (participating org ids, deltaTime/speed settings, stop-condition configuration);
  - `tickCount` (number of `Update` calls executed), `endReason` (`dateReached` | `maxTicks` | `timeout`), and the final game date;
  - per-org end metrics: total control (sum of that org's `ControlEffect` values across all countries) and gold;
  - a timeline sampled once per game month: for each sample, the game date and each org's total control and gold at that point.
- **Given** the raw per-org metrics above **Then** no derived score is computed — scoring formula refinement is a later part; consumers get raw numbers only.
- **Given** the same seed and parameters **When** the runner executes twice **Then** the two results files contain identical metric values (JSON-level equality of everything except any wall-clock fields, which should be avoided or clearly separated).

## Out of Scope

- **VisualState changes.** `VisualState` stays targeted at the single player/view org — it only matters for Unity (non-headless) runs. Multi-org state does not flow into `VisualState`, and no new `VisualState` fields are added.
- **Save-system changes.** The save header's `OrganizationId` remains the player/target org, and the save machinery (header, storage, serializer, autosave) is untouched. Headless runs do not save or load; `GameLogicContext.Storage`/`Serializer` stay null in ConsoleRunner and autosave stays inert there. The one deliberate exception: the discovery component's savable shape changes for per-org discovery (see Per-org discovery criteria); no backward compatibility with pre-change saves is required.
- **Bot decision logic, observation facade, bot command API, `BotProfile` feature flags** — part 2. This includes enforcing that bots cannot push global commands (pause/time/locale/save); in part 1 the runner is the only command source.
- **Eval harness, paired-seed statistics, scoring formula, `/implement-bot-feature` skill, parameter/genetic search** — part 3. When the scoring formula is defined there, it must reference the country-scoring plan (`Docs/Specs/47_country-scoring/`, see Scoring reference above) instead of introducing an unrelated scoring mechanism.
- **Any Unity-side (`Assets/`) changes.** This is `src/`-only work; the Unity build must be behaviourally identical (netstandard2.1 DLLs still build to `Assets/Plugins/Core/`; ConsoleRunner remains net8.0 and is not shipped to Plugins).
- **Win conditions.** Stop conditions are end game-date, max tick count, and wall-clock timeout only.
- **Removing or redesigning the interactive ConsoleRunner mode** — it is preserved alongside the new headless mode.

## Resolved Decisions

Originally raised as ambiguities; resolved with the user on 2026-07-14:

- **Default participating orgs:** all orgs in `organizations.json`; optional CLI flag narrows the set.
- **Discovery:** made per-org in this part (not deferred to part 2) — see the Per-org discovery criteria.
- **Character slots:** every participating org is initialized like the player org, including `IsAvailable`.
- **Runner configs:** loaded from a configurable directory defaulting to `Assets/Configs/`; the drifted `data/` snapshot copies are removed.
- **Save compatibility:** not required — pre-change saves may lose discovery progress or fail to load; no migration logic is written (resolved 2026-07-14, superseding the earlier compatibility criterion).
