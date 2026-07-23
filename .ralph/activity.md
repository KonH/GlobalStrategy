# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-23 — config-schema

Task: Add the end-game comparison entry config schema (empty placeholder array).

Changes:
- `src/Game.Configs/GameSettings.cs`: added `EndGameComparisonEntry` class
  (`ComparisonElementId`, `Score`) and `List<EndGameComparisonEntry> EndGameComparisons`
  property on `GameSettings`.
- `Assets/Configs/game_settings.json`: added `"endGameComparisons": []`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0 errors.

Next iteration: pick up `gamelogic-settings` task — expose `GameSettings` from
`GameLogic` in `src/Game.Main/GameLogic.cs` (do not touch
`Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, out of scope for headless run).

---

## 2026-07-23 — gamelogic-settings

Task: Expose the loaded GameSettings instance from GameLogic for downstream src/ consumers.

Changes:
- `src/Game.Main/GameLogic.cs`: added `public GameSettings GameSettings { get; private set; }`
  property, assigned from the existing local `settings` variable right after
  `context.GameSettings.Load()` in the constructor (same pattern as ResourceConfig/CharacterConfig/etc.).
  Did not touch `Assets/Scripts/Unity/DI/GameLifetimeScope.cs` (Unity, out of scope).

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0 errors.

Next iteration: pick up `comparison-projector` task — add `EndGameComparisonRowState` to
`src/Game.Main/VisualState.cs` and `src/Game.Main/EndGameComparisonProjector.cs`.

---

## 2026-07-23 — comparison-projector

Task: Add EndGameComparisonRowState and the pure EndGameComparisonProjector.

Changes:
- `src/Game.Main/VisualState.cs`: added `EndGameComparisonRowState` (Place,
  ComparisonElementId, IsPlayer, DisplayName, Score), mirroring `LeaderboardEntryState`'s
  constructor shape. Not registered on `VisualState` — the task only specifies the row
  model + projector, not a wrapping `INotifyPropertyChanged` state (unlike the upcoming
  goal-hint task, which explicitly adds `WinConditionHintState` to `VisualState`).
- `src/Game.Main/EndGameComparisonProjector.cs`: new static `Build(configuredEntries,
  playerOrgId, playerDisplayName, playerScore)` returning
  `List<EndGameComparisonRowState>`. One row per configured entry (IsPlayer=false) plus
  one player row (IsPlayer=true); sorted descending by Score, tie-break by
  ComparisonElementId ordinal, then IsPlayer (false before true) as the deterministic
  secondary/tertiary key; 1-based sequential Place assigned after sort. Null
  `configuredEntries` yields a single player-only row (no null-check needed for empty
  lists since the loop is a no-op).

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — succeeded, 0 warnings, 0 errors.

Next iteration: pick up `goal-hint-projector` task — add `WinConditionHintKind`,
`WinConditionHintRowState`, `WinConditionHintState` (registered on `VisualState`) to
`src/Game.Main/VisualState.cs` and `src/Game.Main/WinConditionHintProjector.cs`.
