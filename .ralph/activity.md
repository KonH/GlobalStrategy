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

---

## 2026-07-23 — visualstate-scalar

Task: Add early-return-on-equal to Set(...) for scalar-field state classes.

Note: found `.ralph/activity.md` had an uncommitted local modification that stripped the previous entry (the `state-equality-helper` journal entry existed in HEAD/`c0d0f62` but not in the working tree). Restored it with `git checkout -- .ralph/activity.md` before appending this entry, so no prior journal content is lost.

Changed `src/Game.Main/VisualState.cs`:
- `SelectedCountryState.Set`: early-return when `IsValid`/`CountryId` both match stored values.
- `PlayerOrganizationState.Set`: early-return when `IsValid`/`OrgId`/`DisplayName`/`HqCountryId` all match.
- `SelectedOrganizationState.Set`: early-return when `IsValid`/`OrgId`/`DisplayName`/`InitialGold` all match.
- `SelectedProvinceState.Set`: early-return when `IsValid`/`ProvinceId` both match.

Changed `src/Game.Main/TimeState.cs`:
- `TimeState.Set`: early-return when `CurrentTime`/`IsPaused`/`MultiplierIndex` all match.

Each follows the existing `LocaleState`/`MapLensState` pattern: `if (equal) { return; }` before any field assignment, with assignment + `PropertyChanged` invoke only on the changed path. `LocaleState`/`MapLensState` themselves untouched.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Set `visualstate-scalar` task's `"passes"` to `true` in `.ralph/prd.md`.

Next iteration: pick up `visualstate-list` (structural-equality early-return for CountryControlState, CountryCharactersState, OrgCharactersState, OrgMapState, OrgActionsState, CountryActionsState, LeaderboardState, GameLogState, VisualEffectCollection, CountryResourcesState) in `src/Game.Main/VisualState.cs`, using the `StateEquality` helper's `ListEquals` with the matching per-entry comparer. Remember `CountryControlState.Set` needs `usedChanged` captured BEFORE calling `UsedControl.SetActual(used)` since `SetActual` must still run unconditionally.

---

## 2026-07-23 — visualstate-list

Task: Add structural-equality early-return to Set(...) for list-holding state classes, preserving unconditional SetActual calls.

Changed `src/Game.Main/VisualState.cs`:
- `CountryControlState.Set`: captures `usedChanged` before the unconditional `UsedControl.SetActual(used)` call, combines it with `StateEquality.ListEquals(OrgEntries, entries, OrgControlEntryEquals)` to decide the early return.
- `CountryCharactersState.Set`, `OrgCharactersState.Set`, `OrgMapState.Set`, `GameLogState.Set`: single-list `ListEquals` early-return with the matching per-entry comparer (`CharacterStateEntryEquals`, `OrgCharacterSlotEntryEquals`, `OrgCountryEntryEquals`, `GameLogEntryEquals`).
- `VisualEffectCollection.Set`: normalizes the `effects ?? new List<...>()` null-coalesce first, then compares via `ListEquals`/`VisualResourceChangeEffectEquals` before assigning.
- `LeaderboardState.Set`: `ListEquals` over both `Organizations` and `Countries` (`LeaderboardEntryStateEquals`) combined with AND.
- `OrgActionsState.Set` and `CountryActionsState.Set`: `HandSize`/`CurrentTime` are assigned unconditionally every call (matching the `UsedControl.SetActual` pattern) since they're not logically tied to list contents — `CountryActionsState.CurrentTime` in particular changes almost every tick regardless of card-list changes, so folding it into the equality check would defeat the optimization entirely; the PRD step explicitly scopes `CountryActionsState.Set` to "(Hand and Deck)" for this reason, and `OrgActionsState.Set` was made symmetric with the same reasoning even though not explicitly parenthesized in the PRD step, since it has the same `HandSize` shape. `PropertyChanged` fires only when `Hand`/`Deck` `ListEquals` (with `ActionCardEntryEquals`) says something changed.

Changed `src/Game.Main/ResourcesState.cs`:
- `CountryResourcesState.Set`: early-return combines `IsValid`/`CountryId` scalar equality with `ListEquals` over `Resources` (`ResourceStateEntryEquals`) and `ControlIncomes` (`ControlIncomeEntryEquals`); included `IsValid`/`CountryId` in the check (beyond the PRD step's literal "over ResourceStateEntry/ControlIncomeEntry lists" wording) because selecting a different country/org with an identical resource list would otherwise be wrongly treated as a no-op. No `SetActual` capture needed — confirmed `AnimatableDouble.SetActual` happens in `VisualStateConverter.BuildResources`, outside this method.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Set `visualstate-list` task's `"passes"` to `true` in `.ralph/prd.md`.

Next iteration: pick up `visualstate-dictionary-set` (durable-content-only equality early-return for `ProvinceOwnershipState.Set`, `ProvinceOccupationState.Set`, `CountryScoreState.Set` using `StateEquality.DictionaryContentEquals`, and `DiscoveredCountriesState.Set` using `HashSet.SetEquals`), excluding the transient `Recent*`/`RecentlyDiscovered` fields from the equality check but still assigning them unconditionally every call (same pattern used here for `CountryActionsState.CurrentTime`/`HandSize`).

---

## 2026-07-23 — visualstate-dictionary-set

Task: Add durable-content-only equality early-return to Set(...) for dictionary- and HashSet-holding state classes, excluding transient recent-event fields.

Changed `src/Game.Main/VisualState.cs`:
- `ProvinceOwnershipState.Set`: captures `equal = StateEquality.DictionaryContentEquals(OwnerByProvinceId, ownerByProvinceId)` before assigning `RecentProvinceId`/`RecentOldOwnerId`/`RecentNewOwnerId` (which happen unconditionally every call, same as `CountryActionsState.CurrentTime`); early-returns before assigning `OwnerByProvinceId`/firing `PropertyChanged` when `equal`.
- `ProvinceOccupationState.Set`: same pattern for `OccupierByProvinceId` / `RecentProvinceId`/`RecentOldOccupierId`/`RecentNewOccupierId`.
- `CountryScoreState.Set`: early-return via `StateEquality.DictionaryContentEquals(ScoreByCountryId, scoreByCountryId)` — no transient fields to preserve here.
- `DiscoveredCountriesState.Set`: captures `equal = CountryIds.SetEquals(ids)` before assigning `RecentlyDiscovered` unconditionally; early-returns before assigning `CountryIds`/firing `PropertyChanged` when `equal`.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Set `visualstate-dictionary-set` task's `"passes"` to `true` in `.ralph/prd.md`. (Note: the `Edit` tool repeatedly failed to match the surrounding JSON text for this specific block despite `Read` showing byte-identical content — used `sed -i '57s/.../'` on the line number instead; worth a look if this recurs.)

Next iteration: pick up `call-site-review` (re-confirm transient-field call-site safety for `ProvinceOwnership.Set`/`ProvinceOccupation.Set`/`DiscoveredCountries` in `GameLogic.cs` and `VisualStateConverter.cs` against the equality-check changes made in `visualstate-list` and `visualstate-dictionary-set`). No code changes expected unless a call site has drifted — this task's gate is still `dotnet build`, so if nothing needs fixing, just confirm and flip `passes` to `true`.

---

## 2026-07-23 — call-site-review

Task: Re-confirm transient-field call-site safety against current main before finalizing equality checks.

No code changes — pure review. Confirmed all three call-site groups:

- `GameLogic.cs`: both `VisualState.ProvinceOwnership.Set(...)` (line 154) and the two `VisualState.ProvinceOccupation.Set(...)` calls (lines 164, 174) are still inside `if (changed) { ... }` blocks guarded by `ProvinceOwnershipSystem.ChangeOwner`/`ProvinceOccupationSystem.SetOccupier`/`ClearOccupier`'s own `changed` return value — a `Recent*` field is only ever set in the same call where the durable dictionary entry actually changed.
- `VisualStateConverter.UpdateProvinceOwnership`/`UpdateProvinceOccupation` (lines 626-662): both still pass `_state.ProvinceOwnership.Recent*`/`_state.ProvinceOccupation.Recent*` straight through unchanged (reading back the currently-stored value, not computing a new one) — confirmed by direct read of the current file.
- `VisualStateConverter.UpdateDiscoveredCountries` (lines 464-486): `pendingRecently` is only set to a new `recently` value when the loop actually finds a `_previousDiscoveredIds`-absent id; otherwise it falls through to `_state.DiscoveredCountries.RecentlyDiscovered` (the existing stored value) — confirmed unchanged from the `visualstate-dictionary-set` iteration's expectations.

No drift found — all three call sites still satisfy the invariant the equality-check changes depend on (a transient field only takes a genuinely new value in the same call where durable data also changes).

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Set `call-site-review` task's `"passes"` to `true` in `.ralph/prd.md` (used `sed -i '69s/.../'` on the line number — `Edit` tool's exact-match on this JSON block has failed for two consecutive iterations now despite `Read` showing byte-identical surrounding text; worth investigating if it recurs a third time, but `sed` on the known line number is a reliable workaround).

Next iteration: pick up the first `benchmarks` task — add `src/Game.Benchmarks/ScalarVisualStateSetBenchmarks.cs` (`[MemoryDiagnoser]`, following `VisualStateConverterBenchmarks.cs`'s `GameWorldFixture.Build()` + one warm `Update(...)` pass convention) with `<ClassName>_NoOp`/`<ClassName>_Update` benchmark pairs for `SelectedCountryState`, `SelectedOrganizationState`, `SelectedProvinceState`, `PlayerOrganizationState`, `TimeState`, `LocaleState`, `MapLensState`.

---

## 2026-07-23 — benchmarks (ScalarVisualStateSetBenchmarks)

Task: Add ScalarVisualStateSetBenchmarks covering scalar-field state classes including the LocaleState/MapLensState reference implementations.

Created `src/Game.Benchmarks/ScalarVisualStateSetBenchmarks.cs`, `[MemoryDiagnoser]`, following `VisualStateConverterBenchmarks.cs`'s `GameWorldFixture.Build()` fixture-construction convention:
- `[GlobalSetup]` builds the fixture, reads `fixture.Logic.VisualState` directly (the real `SelectedCountryState`/`SelectedOrganizationState`/`SelectedProvinceState`/`PlayerOrganizationState`/`TimeState`/`LocaleState`/`MapLensState` instances the game itself uses, not hand-built copies), and does one warm `Set(...)` call per class to establish baseline stored values — this is the "one warm Update(...) pass" equivalent for state classes with no `Update` method of their own.
- Loads `organizations.json` via `FileConfig<OrganizationConfig>` (same as `VisualStateConverterBenchmarks.Setup`) to get a second, genuinely-different org id (`Masons` alongside `Illuminati`); uses `fixture.Logic.ProvinceConfig.Provinces[1]` for a second country/province id.
- `<ClassName>_NoOp` benchmarks call `Set(...)` with the exact stored baseline values every call (true no-op, matches the PRD step literally).
- `<ClassName>_Update` benchmarks use a per-class `bool` toggle flipped each call, alternating between the baseline value and a genuinely different alternate value (country/org/province id, `DateTime`, locale string, `MapLens` enum member) — guarantees every single invocation is a real change relative to what's currently stored, following the reset-per-call pattern `TimeSystemBenchmarks.Update` already uses in this project (there: reset a `GameTime` struct field-by-field before each call so `TimeSystem.Update` always does real work; here: toggle instead of reset since flipping between two known-different values is cheaper than a stateful reset and works because `Set`'s own equality check is exactly what's being measured).

Namespace gotcha caught before build: `GS.Main` (not `GS.Game.Main`) for the `VisualState`/`TimeState`/`LocaleState`/`MapLensState`/`SelectedCountryState` etc. classes, and `GS.Game.Commands` (not `GS.Commands`) for `MapLens` — confirmed via `head`/`grep` on `VisualState.cs`, `TimeState.cs`, `ChangeLensCommand.cs` before finalizing usings.

Gate: `dotnet build src/GlobalStrategy.Core.sln -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).

Set the first `benchmarks` task's `"passes"` to `true` in `.ralph/prd.md`. Note: `Edit`'s exact-match again failed on this JSON block (same tab-indentation issue noted in the last two entries) — used `sed -i '80s/.../'` on the line number, then re-verified the edited line's tab count matched its sibling entries (first sed pass over-indented to 3 tabs instead of 2, caught and corrected with a second sed pass) and re-parsed the whole `prd.md` JSON block with `python3 -c "json.loads(...)"` to confirm the file is still valid JSON with all 11 tasks present before moving on. This tab-mismatch pattern has now recurred on 3 consecutive iterations — a future iteration should consider switching to a Python `json.load`/`json.dump` round-trip via `.tmp/run.py` instead of `Edit`/`sed` line-splicing, since `json.dump` would normalize indentation and remove the recurring exact-match failure mode entirely.

Next iteration: pick up the second `benchmarks` task — add `src/Game.Benchmarks/ListVisualStateSetBenchmarks.cs` (`[MemoryDiagnoser]`, same `GameWorldFixture`-based convention) with `<ClassName>_NoOp`/`<ClassName>_Update` pairs for `CountryControlState`, `CountryCharactersState`, `OrgCharactersState`, `OrgMapState`, `OrgActionsState`, `CountryActionsState`, `LeaderboardState`, `GameLogState`, `CountryResourcesState`, `VisualEffectCollection`, using the fixture's real ~163-country list volume (not empty/single-element lists) and Update variants that append/mutate one entry on a copy of the stored collection.
