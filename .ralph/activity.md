# Ralph Activity Journal

Append-only log written by each loop iteration. Newest entry last.

---

## 2026-07-23 — state-equality-helper

Task: Add the centralized StateEquality helper class with list/dictionary comparers and per-entry-type comparer functions.

`src/Game.Main/StateEquality.cs` was already written and committed by a prior iteration (`b7d5dee: ralph: add StateEquality helper (unverified, dotnet toolchain unavailable)`), but that iteration's `dotnet` toolchain was unavailable, so `prd.md` still had `"passes": false` for this task and the gate had never actually been run.

This iteration: verified the dotnet toolchain is now available (`dotnet --version` -> 8.0.419) and ran the gate.

Verified the file contents match every step in the task:
- `ListEquals<T>` and `DictionaryContentEquals<TValue>` present with the specified signatures.
- Named comparers present for all twelve listed entry types (OrgControlEntry, SkillEntry, CharacterStateEntry, OrgCharacterSlotEntry, OrgCountryEntry, ActionCardEntry, VisualResourceChangeEffect, LeaderboardEntryState, GameLogEntry, ResourceStateEntry, ControlIncomeEntry, EffectStateEntry).
- `CharacterStateEntryEquals` compares `Opinion.Actual`, `ResourceStateEntryEquals` compares `Value.Actual` — both by `.Actual`, never by reference or `.Display`.
- `OrgCharacterSlotEntryEquals` calls `CharacterStateEntryEquals` directly, satisfying the reuse requirement.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Set `state-equality-helper` task's `"passes"` to `true` in `.ralph/prd.md`.

Next iteration: pick up `visualstate-scalar` (early-return-on-equal for SelectedCountryState, SelectedOrganizationState, SelectedProvinceState, PlayerOrganizationState, TimeState), following the existing LocaleState/MapLensState pattern in `src/Game.Main/VisualState.cs` and `src/Game.Main/TimeState.cs`.
