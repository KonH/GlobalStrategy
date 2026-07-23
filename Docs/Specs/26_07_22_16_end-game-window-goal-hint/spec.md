# Spec: End Game Window and Goal Hint

## Feature Intent

As a player, I want the game to explain the configured victory conditions before organization selection and present my final result and score comparison when play ends, so that I understand both what I am pursuing and how my finished campaign performed.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- A game is still in progress.
  - The player views the game map => no end-game window is shown => the existing game interface remains usable.
  - Score or control values change without completing the game => the game publishes its latest state => no premature victory or defeat presentation appears.
- The player organization wins.
  - The game publishes its terminal result => the end-game window appears once over a fully black, full-screen backdrop => the header identifies the player organization in the localized equivalent of `{orgName} owns all the World!`.
  - The end-game window is visible => the player reviews it => the final organization leaderboard and comparison block use the same frozen final scores from the completing update.
- Another organization wins.
  - The game publishes its terminal result => the end-game window appears once over a fully black, full-screen backdrop => the header identifies the player organization in the localized equivalent of `{orgName} doomed...`.
  - The end-game window is visible => the player reviews it => the winning organization and every other participant remain visible in the final organization leaderboard.
- A completed saved game is loaded.
  - Its terminal state is restored => the map scene presents the matching victory or defeat end-game window => the same frozen final leaderboard and comparison are shown without requiring another gameplay update.
- The end-game organization leaderboard is rendered.
  - Participating organizations have different scores => the rows are shown from highest score to lowest => each row shows its sequential place, organization identity, and final score.
  - Two or more organizations have equal scores => the rows are rendered repeatedly or after a locale refresh => the tied organizations stay in a stable, repeatable order and their displayed places remain sequential.
  - No organization score rows are available => the block is rendered => a localized empty-state message appears and the rest of the end-game window remains usable.
- The shipped end-game comparison configuration is used.
  - The comparison block is rendered => the player organization is inserted among exactly nine predefined comparison entries => all ten rows are sorted from highest score to lowest.
  - The player organization is inserted => its row is rendered => it shows the player's final organization score and may show the player's organization emblem.
  - A predefined comparison entry is rendered => its row is rendered => it shows its localized name and configured expected score without an icon.
  - Two comparison rows have equal scores => the block is rendered repeatedly or after a locale refresh => their order remains stable and their displayed places remain sequential.
  - Comparison configuration is absent or empty => the block is rendered => the player row is still shown, no blank comparison rows appear, and the window remains usable.
  - A comparison name has no translation => the row is rendered => a readable fallback is shown rather than an empty label.
- The predefined comparison scores are calibrated.
  - The deterministic calibration is run against the committed game configuration => both a player-win scenario and a player-loss scenario reach their terminal states through normal updates and supported debug commands => each scenario records the player's final organization score.
  - Both calibration scenarios have completed => their results are evaluated => the higher player score is recorded as the achievable calibration maximum together with the inputs and outputs needed to reproduce it.
  - The nine comparator scores are generated => their thresholds span inclusively from 5% to 120% of the calibration maximum => the least-popular researched entry receives the lowest threshold and the most-popular receives the highest.
  - The configuration or score rules change and calibration is rerun => new output is produced => stale or non-reproducible comparison values are detected before they are accepted.
- The predefined comparison identities are selected.
  - Research is performed => nine figures or organizations associated with claims in conspiracy folklore are compared by worldwide Google Trends popularity => the committed order is backed by dated source and query metadata rather than unsupported ranking claims.
  - Research results are recorded => a reviewer reads the evidence => it states worldwide geography, search type and time window, term-versus-topic choices, and the normalization or shared-anchor method used to make samples comparable.
  - Comparison names and descriptions are shown in the game or its committed evidence => the player or reviewer reads them => conspiracy allegations are framed as folklore, mythology, or claims and are never presented as established facts.
- The end-game window is open.
  - The player attempts to interact with the map or other game controls => pointer input reaches the terminal overlay => underlying world and UI interactions remain blocked.
  - The player selects `Exit` => the action is released within the button bounds => the game returns to the main menu.
  - The player does not select `Exit` => the terminal presentation remains open => it cannot be dismissed back into the completed game by an outside click or close control.
- The organization-selection scene is shown with the current configured completion conditions.
  - The scene opens => a top-right panel appears with the localized header `Win conditions` => the panel is visible without selecting an organization.
  - The configuration contains an 80% total-control condition => its row is rendered => it shows the localized equivalent of `Control 80% of the World`.
  - The configuration contains a full-control threshold of 15 and 20 countries are available => its row is rendered => it shows the localized equivalent of `Control completely at least 15/20 countries`.
  - Multiple conditions belong to the configured alternative group => the rows are rendered => the presentation makes clear that satisfying any one of them is sufficient to win.
- Completion-condition configuration is recursive or changes later.
  - Nested alternative groups contain supported leaf conditions => the goal hint is built => supported leaves appear once in configuration order regardless of nesting depth.
  - Another supported condition row is added => the scene is opened => the new localized row can appear without redesigning the panel or changing unrelated condition rows.
  - The completion condition is absent, empty, or contains no displayable leaf => the scene is opened => the panel shows a localized unavailable message instead of blank values or an error.
- Country availability changes in configuration.
  - The organization-selection scene is reopened => a country-count goal row is rebuilt => its denominator equals the number of countries currently marked available, with no separately maintained total.
  - Unavailable countries remain in the country configuration => the goal hint is rendered => they do not increase the displayed denominator.
- The active locale is English or Russian.
  - Either affected scene opens or its locale state refreshes => all new headers, buttons, empty states, condition phrases, result formats, and comparison names refresh => no raw localization keys are displayed in the shipped configuration.

## Tech Notes

- Terminal state and score sources:
  - Keep terminal authority in `src/Game.Components/GameCompletion.cs`, `src/Game.Components/OrganizationGameOutcome.cs`, and `src/Game.Systems/GameCompletionSystem.cs`; the UI consumes the existing `VisualState.GameCompletion` projection produced by `VisualStateConverter.UpdateGameCompletion`.
  - Reuse `VisualState.Leaderboard.Organizations`, populated by `VisualStateConverter.UpdateLeaderboards`, for final organization rows. Preserve `SortAndAssignPlaces` semantics: descending score, then stable ordinal identity/name tie-breaks, then sequential places.
  - `GameLogic.Update` already freezes simulation after completion and publishes the completing update before the freeze. The end-game binding must also react to restored completion state after `GameLogic.LoadState`.
- Comparison configuration and projection:
  - Extend `src/Game.Configs/GameSettings.cs` with a small end-game comparison configuration containing entries with exactly `comparisonElementId` and `score`; populate it in `Assets/Configs/game_settings.json` with the nine calibrated shipped entries.
  - Expose the loaded immutable `GameSettings` instance from `GameLogic` and register it from `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, rather than deserializing the same TextAsset again in a UI component.
  - Add a focused comparison projection in `src/Game.Main` that combines the configured entries with `VisualState.PlayerOrganization` and the player's matching final `LeaderboardEntryState`, sorts descending, and applies an ordinal stable-key tie-break. Missing entries produce a player-only projection.
  - Reuse the organization-row score formatting and flag lookup from `Assets/Scripts/Unity/UI/LeaderboardWindowView.cs` through a focused shared row/score helper; comparator rows explicitly omit the flag element.
- End-game UI Toolkit surface:
  - Add `Assets/UI/Modal/EndGameWindow/EndGameWindow.uxml` and `EndGameWindow.uss`, importing `Assets/UI/Shared/SharedStyles.uss` first and keeping feature USS limited to layout.
  - Add `Assets/Scripts/Unity/UI/EndGameWindowDocument.cs` as the injected binding MonoBehaviour and `EndGameWindowView.cs` as the plain UI Toolkit view. Subscribe/unsubscribe through `OnEnable`/`OnDisable` to `VisualState.GameCompletion`, `Leaderboard`, `PlayerOrganization`, and `Locale`, with an immediate refresh.
  - Give the `UIDocument` an explicit sorting order above `FlyTextNotifierDocument`'s default `1000` so the opaque terminal screen covers all prior game presentation. Hold `ModalState.IsModalOpen` while visible; no hide path exists except scene exit.
  - Register `EndGameWindowDocument` in `GameLifetimeScope`, add its UIDocument to `Assets/Scenes/Map.unity`, and save scene changes through Unity MCP followed by refresh and console checks.
  - Wire `Exit` with `PointerUpEvent`, left-button filtering, and `ContainsPoint`; call `SceneLoader.LoadMainMenu()`. Do not use `Button.clicked` or `ClickEvent`.
- Goal-hint projection:
  - Add a lightweight `WinConditionHintState`/row model to `src/Game.Main/VisualState.cs` and a focused projector that recursively flattens `CompletionConditionConfig.Type == "any"` while preserving configured leaf order.
  - Handle `total_control` and `full_control_countries` with a direct, explicit mapping to typed row values; adding a future supported leaf requires one mapping case and localization format, not a new framework.
  - Calculate the country denominator with `CountryConfig.Countries` entries where `CountryEntry.IsAvailable` is true. Do not add a denominator field to `GameSettings` or the completion-condition config.
  - Extend `SelectOrgLogic` to publish the immutable goal rows from the loaded `GameSettings` and `CountryConfig`; keep simulation/domain completion evaluation in ECS and presentation formatting out of MonoBehaviours.
  - Extend `SelectCountryLifetimeScope` with a serialized `Assets/Configs/game_settings.json` TextAsset, load/register it through `TextAssetConfig<GameSettings>`, and wire the new scene field in `Assets/Scenes/CountrySelection.unity` via Unity MCP.
- Select Organization UI:
  - Extend `Assets/UI/Modal/SelectCountry/SelectCountry.uxml` and `.uss` with an independently positioned top-right goal panel, a header, a dynamic rows container, an alternative-condition cue, and an empty-state label.
  - Extend `SelectOrgDocument` as the binding and introduce a small plain view/helper for the rows if needed; format numeric values through localized templates, refresh on locale changes, and keep the existing selection panel behavior unchanged.
  - While touching `SelectOrgDocument`, replace its existing `btn-back.clicked` and `_btnStart.clicked` handlers with the required bounded `PointerUpEvent` handlers.
- Localization:
  - Add matching `end_game.*` and `select_org.win_conditions.*` keys to `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, including win/lose format strings, `Exit`, empty/fallback text, alternative wording, both supported condition formats, and all nine `comparisonElementId` values.
  - Follow the established locale-state refresh flow; format the organization name as data inside localized result templates and never construct a translated sentence by concatenating fragments.
- Calibration skill and evidence:
  - Add `.codex/skills/end-game-score-calibration/SKILL.md` with the exact build command, fixed seed/config/org inputs, debug-command sequence, terminal assertions, output paths, maximum selection, score generation, and update procedure.
  - Add only the minimal deterministic headless-runner/tool support needed to drive player-win and player-loss scenarios through `DebugDiscoverAllCountriesCommand`, `ChangeControlCommand`, and `GameLogic.Update`; continue recording score through `HeadlessRunner.BuildOrgMetrics` / `OrgMetricsResult.Score` instead of a parallel calculation.
  - Generate nine inclusive linear thresholds by research rank with `factor(i) = 0.05 + i * (1.20 - 0.05) / 8` for `i = 0..8`, applying the same explicit rounding policy used by the committed config and display.
  - Commit calibration outputs and Google Trends evidence under the skill's `references/` folder, including run commands/results and dated research metadata. Google Trends values are relative and sampled, so use one documented comparable query set or a shared anchor with overlap checks; never combine raw values from unrelated samples.
- Verification:
  - Add pure C# tests under `src/Game.Tests` for win/lose comparison insertion, descending order and ties, player-only fallback, recursive condition flattening, alternatives, formatting values, unavailable-country exclusion, empty/unknown conditions, and deterministic calibration output.
  - Run `dotnet test src/GlobalStrategy.Core.sln` and the calibration skill's documented command, checking that both terminal scenarios complete and reproduce the committed nine values.
  - In Unity, verify English and Russian layouts in `CountrySelection.unity` and `Map.unity`, both terminal outcomes, a restored completed save, empty-config fallbacks, pointer blocking, and Exit-to-main-menu behavior; refresh Unity and require a clean console after saved scene changes.

## Out of Scope

- Changing win/lose evaluation, completion thresholds, winner tie-breaking, terminal simulation freezing, organization/country score formulas, or country availability rules established by the existing completion feature.
- Adding new completion-condition semantics beyond displaying the currently supported recursive `any`, `total_control`, and `full_control_countries` configuration.
- A post-game continue mode, restart/rematch button, campaign history, score upload, online leaderboard, achievements, end-game animation, sound, or cinematic sequence.
- Icons or portraits for predefined comparison entries; only the player organization may use its existing emblem in that block.
- A country leaderboard tab or interactive tabs/filters in the end-game window; the existing standalone leaderboard window remains unchanged.
- Runtime Google Trends access, automatic live reranking, or presenting conspiracy claims as factual biographies.
- Redesigning the Select Organization scene, organization details panel, map selection flow, or start/back navigation beyond adding the top-right goal hint and correcting touched button input wiring.
