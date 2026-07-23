# Spec: Standalone Web Client

## Feature Intent

As the game's developer, I want a standalone browser client that runs the existing game simulation without Unity — with a minimal debug UI (menu, org selection, game controls, actions log) and a friendly auto-completing terminal for issuing game commands — so that I can play-test, debug, and drive the simulation from any browser, and ship new debug commands with nothing more than a new build.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- The app is opened in a browser (screen A — main menu).
  - The main menu renders => NEW GAME, CONTINUE, and SETTINGS entries are visible.
  - No save exists in this browser => CONTINUE is not available (hidden or disabled); NEW GAME and SETTINGS still work.
  - At least one save (manual or auto) exists => CONTINUE is available.
  - CONTINUE is activated => the most recent save loads directly into the game view (screen C) with its organization, game date, and in-game settings restored — no save-picker appears.
  - NEW GAME is activated => the organization selection screen (screen B) opens.
  - SETTINGS is activated => the settings screen opens.
- The settings screen is open.
  - A language (English / Russian) is chosen => all currently visible UI text switches to that language immediately, and the choice survives a page reload.
  - An auto-save interval (daily / monthly / yearly) is chosen => the choice survives a page reload and is applied to newly started games.
  - Back is activated => the main menu (screen A) is shown again with the chosen language applied.
- The organization selection screen (screen B) is shown.
  - The screen renders => a dropdown lists every playable organization by its display name.
  - An organization is selected and confirmed => a new game starts in the game view (screen C) as that organization, at the configured start date, running unpaused at normal (X1) speed.
- A game is running in the game view (screen C).
  - The view renders => MENU, PAUSE/PLAY, X1/X2/X3 speed buttons, the current game date, the actions log, and the terminal are all visible; the active speed button is visually highlighted; there is no map.
  - PAUSE is pressed => the game date stops advancing; pressing PLAY resumes it from the same date.
  - X2 or X3 is pressed => the game date advances proportionally faster; X1 returns to normal speed; the highlighted button follows the selection.
  - The game runs unpaused across an auto-save interval boundary (per the configured interval) => an auto-save is written and becomes the most recent save that CONTINUE would load.
  - The game sits paused across what would be an auto-save boundary => no auto-save fires while paused.
  - The win/lose objective is reached => the simulation freezes, a plain completion status (win or lose, and the winning organization) is shown, and further speed/gameplay input has no effect; saving still works.
- The actions log is visible in the game view.
  - A loggable event occurs (country discovered, control increased, opinion increased, new character in a role) => a new line appears at the bottom of the log with the same wording, line formats, and number formatting as the Unity action log, in the current language.
  - The log reaches its configured maximum entry count => the oldest line is removed so the cap is never exceeded, newest entries stay visible at the bottom.
  - A game is loaded via CONTINUE => the log starts empty for that session; no historical events replay from the save.
- The terminal is focused in the game view.
  - The input is empty and Tab is pressed => a list of all available commands is shown; picking one fills the input with that command's name.
  - A partial command name is typed and Tab is pressed => the list filters to matching commands; a single remaining match completes inline.
  - A complete command name is entered and Tab is pressed => the command's argument names are suggested and the picked one is inserted as `argName=`.
  - The cursor sits after `argName=` (with or without a partial value) and Tab is pressed => labeled value suggestions from live game data appear (e.g. `CountryName1 (CountryId1)`); picking one inserts the underlying id value.
  - An argument's type is a fixed set of choices (e.g. an enum) => its possible values are suggested without any extra setup.
  - The input matches no command, argument, or value and Tab is pressed => nothing is suggested and nothing breaks.
  - A valid command with valid arguments is submitted => it takes effect in the running game on the next update, observable in the UI (e.g. pausing, changing gold, switching language).
  - An unknown command, a malformed argument, or a missing required argument is submitted => a friendly error line is printed in the terminal and the game is unaffected.
  - A new command type is added to the game code and a new web build is published => the command, its arguments, and its value auto-complete appear in the terminal with zero web client code changes.
- The in-game menu (screen D) is opened via MENU.
  - The menu renders => SAVE and EXIT entries are visible; the game behind it is unaffected until one is chosen.
  - SAVE is activated (running or paused) => the game is saved in this browser's storage and a success/failure notice is shown.
  - EXIT is activated => the app returns to the main menu (screen A) without saving implicitly.
  - The player saves, exits, then activates CONTINUE => the game view reopens with exactly the saved organization, game date, and state.
  - The player makes progress after the last save and exits without saving => CONTINUE returns to the last saved state; the unsaved progress is lost (accepted debug behaviour).

## Tech Notes

- Tech stack decision — **Blazor WebAssembly standalone (.NET 8), whole simulation in-browser, static-file deploy**:
  - A new `src/Game.WebClient/` Blazor WASM project is added to `src/GlobalStrategy.Core.sln`, referencing the existing `netstandard2.1` projects unchanged (`Game.Main`, `Game.Bots`, `Game.Commands`, `Game.Components`, `Game.Configs`, `Core.Configs`, `Core.Configs.IO`, `Core.Map`, `ECS.Core`); .NET 8 matches `Game.ConsoleRunner`'s `net8.0` target and is LTS.
  - Rejected — ASP.NET Core server hosting the sim with a thin JS client (the `src/ECS.Viewer.Server/ViewerServer.cs` pattern): requires an always-running server, per-user session management, and server-side save storage; deploy/update means operating a host rather than replacing static files. `ECS.Viewer.Server` stays as-is for local desktop debugging.
  - Rejected — .NET Native AOT / experimental wasm AOT toolchains: AOT restricts or complicates the runtime reflection this feature depends on (terminal command discovery, `SaveSystem`'s `[Savable]` scan, generic `Push<T>` dispatch) and adds toolchain risk for no benefit at debug scope.
  - Publish settings: default IL interpreter (`RunAOTCompilation` off) and `PublishTrimmed=false`, so reflection over `Game.Commands` (terminal), `Game.Components` (`SaveSystem` static constructor scans `typeof(SavableAttribute).Assembly` for `[Savable]` types via `src/Game.Main/SaveSystem.cs`), and `MethodInfo.MakeGenericMethod` all keep working. Trimming with explicit `TrimmerRootAssembly` roots is a later optimization, not part of this feature.
  - Deployment: `dotnet publish -c Release` emits a fully static site; a new GitHub Actions workflow (the repo has no `.github/` yet) publishes it to GitHub Pages on push to the default branch (rewriting `<base href>` for the Pages subpath). "Update" = merge + automatic republish; "new commands appear" = the same publish carries the freshly built game assemblies.
- Session construction and game loop (screens B/C, speed/pause):
  - Config JSONs are copied at build time from `Assets/Configs/` into `wwwroot/configs/` (the same file set `Program.BuildContext` in `src/Game.ConsoleRunner/Program.cs` names: `geojson_world.json`, `map_entry_config.json`, `country_config.json`, `game_settings.json`, `resource_config.json`, `organizations.json`, `character_config.json`, `action_config.json`, `effect_config.json`, `province_config.json`), fetched via `HttpClient` at startup.
  - A new `StringConfig<TConfig> : IConfigSource<TConfig>` is added beside `src/Core.Configs.IO/FileConfig.cs`, sharing `FileConfig`'s exact `JsonSerializerOptions` (`PropertyNameCaseInsensitive`, `JsonStringEnumConverter`, `ActionEffectDefinitionListConverter`) but taking an already-downloaded JSON string; map geometry parses the fetched `geojson_world.json` through `GS.Core.Map.GeoJsonParser.Parse`, mirroring `src/Game.ConsoleRunner/MapGeometryFileConfig.cs`.
  - `GameLogicContext` is built like `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` builds it: selected org as `initialOrganizationId`, `participatingOrganizationIds` ordered player-first then the remaining orgs from `OrganizationConfig.Organizations`; then `new GameLogic(ctx)` plus `BotSession.Create(logic, rngSeed, logger)` (`src/Game.Bots`), exactly as `GameLifetimeScope`/`GameLoopRunner` do, so bot opponents act and feed the actions log.
  - The loop is a ~30 Hz timer service calling `botSession.Update(elapsedSeconds)` with real elapsed wall-time (the counterpart of `GameLoopRunner.Tick`'s `Time.deltaTime`), then re-rendering. The loop keeps ticking while the game is paused — pause lives inside `TimeSystem` via the `GameTime.IsPaused` flag, and `GameLogic.Update` must keep running so `ProcessSaveCommands` and command handling work while paused. The Unity-side `PauseToken` (ECS-viewer debug gate) is not reproduced.
  - Speed/pause controls push existing commands via `GameLogic.Commands` (`IWriteOnlyCommandAccessor`): `PauseCommand`, `UnpauseCommand`, `ChangeTimeMultiplierCommand(0|1|2)` indexing `GameSettings.SpeedMultipliers` (`[1, 24, 720]` in `Assets/Configs/game_settings.json`). Button state and the date label read `VisualState.Time` (`CurrentTime`, `IsPaused`, `MultiplierIndex`). Completion status reads `VisualState.GameCompletion` (`IsCompleted`, `WinnerOrganizationId`, `Result`); `GameLogic` already freezes itself after completion while still processing save commands.
  - Constitution alignment: the web client is presentation/input glue only — it pushes `ICommand`s and reads `VisualState`/configs; no game state or domain rules live in it. The URP/UI Toolkit/VContainer principles are Unity-project-scoped and untouched.
- Main menu, CONTINUE, and saves in the browser (screens A/D):
  - CONTINUE availability and target come from `SaveFileManager.GetLastSave()` (`src/Game.Main/SaveFileManager.cs`, sorted by `Header.SavedAt`), mirroring `MainMenuDocument.RefreshSaveButtons`/`OnResume` in `Assets/Scripts/Unity/UI/MainMenuDocument.cs`. CONTINUE builds a session passing the save header's `OrganizationId` (from `SaveFileInfo`) as `initialOrganizationId`, then calls `GameLogic.LoadState(saveName)`.
  - `IPersistentStorage` (`src/Game.Main/IPersistentStorage.cs`) is a synchronous API but IndexedDB is async-only, and localStorage's ~5 MB quota is unsafe for snapshots that include one `[Savable]` `ProvinceOwnership`/`ProvinceOccupation` entity per province (5,492 provinces in `province_config.json`). Therefore `BrowserStorage : IPersistentStorage` keeps an in-memory cache of the `Saves/` directory preloaded at app start via JS interop, serves reads/`Exists`/`List` synchronously from the cache, and write-behinds mutations to IndexedDB asynchronously.
  - The snapshot serializer is a web-side `ISnapshotSerializer` using Newtonsoft.Json with the same settings as `Assets/Scripts/Unity/Save/NewtonsoftSnapshotSerializer.cs` (indented), keeping the on-disk `WorldSnapshot` format (`src/Game.Main/WorldSnapshot.cs`) byte-compatible with Unity saves; Newtonsoft already runs outside Unity (`Game.ConsoleRunner` takes the package).
  - Save naming and "latest" semantics come for free from `SaveSystem.BuildSnapshot` (manual name `{orgId}_{yyyy-MM-dd}`) and `GameLogic.SaveGame` (autosave name `autosave_{orgId}_{sessionId}`); screen D's SAVE pushes `SaveGameCommand()` and shows the outcome from `VisualState.SaveResult`.
  - Auto-save uses the existing pipeline unchanged: `AutoSaveSystem.Update` (`src/Game.Main/AutoSaveSystem.cs`) pushes `SaveGameCommand(IsAutoSave: true)` on Daily/Monthly/Yearly boundaries and returns early while paused; the interval lives in the `[Savable]` `AppSettings` component (`src/Game.Components/AppSettings.cs`) and is changed via `ChangeAutoSaveIntervalCommand` → `ChangeAutoSaveIntervalSystem`.
- Settings and localization:
  - The settings screen persists language and auto-save interval as web-local preferences (localStorage). On session start the client pushes `ChangeLocaleCommand(locale)` and, for new games, `ChangeAutoSaveIntervalCommand(interval)` on the first tick — the same commands `Assets/Scripts/Unity/UI/SettingsWindowDocument.cs` pushes (interval strings `"daily"`/`"monthly"`/`"yearly"`). Menu-screen language switching is applied client-side immediately, mirroring the `StaticGameLogic` pattern for pre-game scenes.
  - Locale data source: `Assets/Localization/en.asset` / `ru.asset` are simple Unity-YAML `Entries: [{Key, Value}]` lists; a publish-time build step extracts them into `wwwroot/locales/{locale}.json`. A small web `Localization` service replicates `ILocalization.Get(key)`; menu/settings text reuses the existing `menu.*`, `settings.*`, `game_menu.*` keys, org/country names reuse `organization_name.{id}` / `country_name.{id}`.
- Actions log parity (per `Docs/Specs/26_07_18_07_action-log-ui/spec.md`):
  - Renders `VisualState.GameLog.Entries` (`GameLogEntry`: `SequenceId`, `Kind`, `OrgId`, `CountryId`, `RoleId`, `NamePartKeys`, `Delta`, `Total`, `IsOrgRole`), diffing by `SequenceId` exactly like `Assets/Scripts/Unity/UI/ActionLogView.cs`, with CSS opacity transitions approximating the Unity fade-in/fade-out.
  - Line text uses the same locale format keys (`game_log.discovered_format`, `game_log.control_increased_format`, `game_log.opinion_increased_format`, `game_log.new_character_format`) and the same `"0.#"` invariant number formatting; character names join `NamePartKeys` locale lookups. The entry cap is `gameLog.maxLogEntries` from `game_settings.json`, already enforced by `VisualStateConverter`; the empty-log-after-load behaviour is likewise already guaranteed by the existing spec's non-persistence rule.
- Terminal — command discovery, execution, and auto-complete:
  - Discovery: reflect over `typeof(GS.Game.Commands.ICommand).Assembly` for non-abstract `ICommand` implementors — the identical closed set `src/Game.SourceGenerators/CommandGenerator.cs` enumerates at compile time to generate `CommandAccessor`, which is what guarantees "new command = just a new build". Command name = type name minus the `Command` suffix; parameters = public fields plus record positional properties (e.g. `ChangeTimeMultiplierCommand(int Index)`, `DebugImproveOpinionCommand { CountryId, OrgId }`). All commands are exposed; no allowlist.
  - Execution: parse `Name key=value ...`, coerce values (string/int/double/bool/enum), construct the command instance, and submit through `IWriteOnlyCommandAccessor.Push<T>` closed over the runtime type via `MakeGenericMethod`; the command is consumed by `GameLogic.Update` on the next tick.
  - Parameter suggestion attributes (new, in `src/Game.Commands`, following C# code style — tabs, braces, no redundant access modifiers): an abstract `ParamSuggestionAttribute` base plus sealed `CountryIdAttribute`, `OrgIdAttribute`, `ProvinceIdAttribute`, `ActionIdAttribute`, `RoleIdAttribute`, `CharacterOwnerIdAttribute` (org-or-country owners, e.g. `DebugCycleCharacterCommand.OwnerId`), `LocaleIdAttribute`, and a literal-set `OneOfAttribute(params string[] values)` (e.g. `ChangeAutoSaveIntervalCommand.Interval` → `[OneOf("daily", "monthly", "yearly")]`). Targets are fields and properties; record positional parameters annotate as `[property: CountryId]`. Sketch:

	```csharp
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class CountryIdAttribute : ParamSuggestionAttribute { }

	public struct DebugImproveOpinionCommand : ICommand {
		[CountryId] public string CountryId;
		[OrgId] public string OrgId;
	}
	```

  - Retrofitting every existing command's domain-id parameters with these attributes is part of this feature (e.g. `PlayCardActionCommand.ActionId/OrgId/CountryId`, `DebugChangeProvinceOwnerCommand.ProvinceId/NewOwnerId`, `SelectCountryCommand.CountryId`, `ChangeLocaleCommand.Locale`).
  - Suggestion providers live in `Game.WebClient` (presentation-side, read-only — constitution-safe), keyed by attribute type: CountryId → `CountryConfig.Countries` labeled via `country_name.{id}`; OrgId → `OrganizationConfig.Organizations` display names; ProvinceId → `ProvinceConfig`; ActionId → `ActionConfig`; RoleId → `CharacterConfig` roles; enum parameters auto-suggest their names with no attribute. Providers may also read live state through `GameLogic.World`/existing public query helpers where config data is insufficient. A command parameter with no attribute and no enum type simply offers no value suggestions — never an error.
  - Project-rules deliverable: a new `.claude/rules/terminal_commands.md` (indexed from `CLAUDE.md`) mandating that every new `ICommand` parameter carrying a domain id must be annotated with an existing suggestion attribute (or `OneOf`), and that introducing a genuinely new id kind means adding the attribute plus its provider in the same change.

## Out of Scope

- Map rendering or any map view in the web client — the game view is controls, actions log, and terminal only.
- Polished/production UI: visual parity with the Unity UI kit, animations beyond simple CSS fades, responsive/mobile/touch layouts.
- Any Unity client behaviour change — the `src/` additions (attributes, `StringConfig`) are additive and the Unity project does not consume them yet.
- Per-entity coloured org/country names in the actions log — those colours live in Unity `ScriptableObject` visual configs (`CountryVisualConfig`/`OrgVisualConfig`); web log lines render names bold, uncoloured.
- Multiplayer, accounts, or any server-side persistence — saves are strictly per-browser.
- A save-slot picker, load window, or save-management UI (delete/rename); CONTINUE always takes the latest save.
- Save import/export between browser and Unity clients (format is kept compatible; a transfer UI is a possible follow-up).
- Terminal conveniences beyond Tab completion: command history, scripting/macros, output paging.
- An in-game settings screen — in-game language/interval changes are already reachable through the terminal (`ChangeLocale`, `ChangeAutoSaveInterval`).
- WASM AOT compilation, trimming, bundle-size or simulation-performance optimization; PWA/offline support.
- Bot configuration UI — bot opponents run with the same defaults as the Unity client.

## Ambiguities

- [NEEDS CLARIFICATION: Hosting target — is GitHub Pages via a GitHub Actions workflow acceptable (requires the repo to be public or on a plan with private-repo Pages), or should the workflow target another static host (Cloudflare Pages, Netlify, a personal server)?]
- [NEEDS CLARIFICATION: Must web saves be interchangeable with Unity saves (import/export of the identical `WorldSnapshot` JSON), or is per-browser isolation with a merely-compatible format enough for now?]
- [NEEDS CLARIFICATION: Should the main-menu auto-save-interval preference also override the interval stored inside a save loaded via CONTINUE, or apply only to new games (the save's own `AppSettings` value winning on load, as in Unity today)?]
