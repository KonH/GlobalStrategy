# Plan: VisualState update optimization

## Spec

(Verbatim summary of `spec.md` — see that file for full text.)

**Feature Intent:** Every `INotifyPropertyChanged` state class's `Set(...)` method
(`src/Game.Main/VisualState.cs`, `ResourcesState.cs`, `TimeState.cs`) should raise
`PropertyChanged` only when the values it is given actually differ from the values already
stored, so `VisualStateConverter.Update`'s unconditional per-tick calls stop forcing UI
Toolkit bindings to rebuild when nothing changed — proven via dedicated BenchmarkDotNet
benchmarks showing the added equality-check cost is minimal on the no-op path.

**Acceptance Criteria:**
- Scalar-field classes (`SelectedCountryState`, `SelectedOrganizationState`,
  `SelectedProvinceState`, `PlayerOrganizationState`, `TimeState`, plus the already-compliant
  `MapLensState`/`LocaleState` reference implementations): no `PropertyChanged` on a
  value-identical `Set(...)` call; fires exactly once when any argument differs.
- List-holding classes (structural, not reference, equality): a freshly-allocated list whose
  elements are field-by-field equal, in the same order, to the stored list must not fire
  `PropertyChanged`. Any difference in count, order, or element fields must fire. Covers
  `CountryControlState` (`OrgControlEntry`), `CountryCharactersState` (`CharacterStateEntry`),
  `OrgCharactersState` (`OrgCharacterSlotEntry`), `OrgMapState` (`OrgCountryEntry`),
  `OrgActionsState`/`CountryActionsState` (`ActionCardEntry`, `Hand`/`Deck`),
  `LeaderboardState` (`LeaderboardEntryState`, `Organizations`/`Countries`), `GameLogState`
  (`GameLogEntry`), `CountryResourcesState` (`ResourceStateEntry`/`ControlIncomeEntry`),
  `VisualEffectCollection` (`VisualResourceChangeEffect`, `Effects`).
  `CountryControlState.Set`/`CountryResourcesState.Set`'s existing unconditional
  `AnimatableInt`/`AnimatableDouble` `SetActual(...)` calls must keep running every invocation,
  independent of whether the equality check suppresses `PropertyChanged`.
- Dictionary-/set-holding classes: `ProvinceOwnershipState.OwnerByProvinceId`,
  `ProvinceOccupationState.OccupierByProvinceId`, `CountryScoreState.ScoreByCountryId`
  (key/value-pair equality, any enumeration order) and `DiscoveredCountriesState.CountryIds`
  (`HashSet<string>` set-equality) must not fire on same-content/different-instance `Set(...)`
  calls. Transient "recent event" fields
  (`DiscoveredCountriesState.RecentlyDiscovered`, `ProvinceOwnershipState.Recent*`,
  `ProvinceOccupationState.Recent*`) are excluded from the comparison entirely — only durable
  dictionary/set content decides whether `PropertyChanged` fires. Call sites for these fields
  must be reviewed to confirm the durable data always changes in the same call, so excluding
  the transient fields never silently swallows a distinct event the UI needs.
- Upstream version-gating (`VisualStateConverter.UpdateProvinceOwnership`/
  `UpdateProvinceOccupation` skipping `Set(...)` entirely when
  `ProvinceOwnershipSystem.GetVersion`/`ProvinceOccupationSystem.GetVersion` is unchanged) is
  unaffected — this feature adds equality-checking inside `Set(...)` in addition to, not
  instead of, that gate.
- Benchmark coverage is a first-class acceptance criterion: for each covered state class, a
  `[Benchmark]` method pair (no-op `Set(...)` call with identical values; update `Set(...)`
  call with genuinely different values) exists under `[MemoryDiagnoser]`, following
  `VisualStateConverterBenchmarks.cs`'s fixture-construction convention. List/dictionary/set
  classes' pairs use realistically-sized collections (matching the harness's existing
  163-country fixture volume), not empty/single-element ones. No new numeric threshold is
  introduced beyond the harness's existing `--compare` gate (`epsilonRelative = 0.05`).
- Test coverage: unit tests assert, per state class, that `PropertyChanged` does not fire on a
  value-identical `Set(...)` call and does fire on a value-different one — covering at minimum
  one scalar class, one list-holding class, one dictionary-holding class, and the
  `HashSet`-holding class.

**Out of Scope:** `Set(...)` signatures/call sites in `VisualStateConverter.cs`/`GameLogic.cs`;
per-property change granularity in `PropertyChangedEventArgs`; `AnimatableInt`/`AnimatableDouble`'s
own animation/barrier logic; `ProvinceOwnershipSystem.GetVersion`/`ProvinceOccupationSystem.GetVersion`
or the existing upstream skip-gate; any `Assets/Scripts/Unity/UI/` binding/`Refresh()` change; CI
wiring or benchmark regression-threshold changes beyond adding new methods; `SaveResultState.Set(...)`
— excluded entirely, keeps firing unconditionally every call, since each save attempt is a distinct
event regardless of whether its outcome matches the previous attempt.

**Resolved Decisions:** transient recent-event fields and `SaveResultState` are both excluded from
the equality-check optimization (implementation must review usages to confirm this is safe); scope
is literally all ~22 `INotifyPropertyChanged` state classes across the three files; the qualitative
"no-op benchmarks pass the existing 5%-regression gate" bar is sufficient, no bespoke numeric
threshold needed.

## Goal

Make every in-scope `VisualState`/`ResourcesState`/`TimeState` class's `Set(...)` a no-op
(no `PropertyChanged` fire) when called with logically-unchanged data, using structural
(not reference) equality for list/dictionary/set-shaped fields, while leaving call sites,
event-arg shape, and `AnimatableInt`/`AnimatableDouble`'s own animation logic untouched.

## Approach

1. **Centralize comparison logic in one static helper class** (`src/Game.Main/StateEquality.cs`)
   rather than adding `Equals`/`GetHashCode` overrides to the ~12 plain-data record classes
   (`OrgControlEntry`, `SkillEntry`, `CharacterStateEntry`, `OrgCharacterSlotEntry`,
   `OrgCountryEntry`, `ActionCardEntry`, `VisualResourceChangeEffect`, `LeaderboardEntryState`,
   `GameLogEntry`, `ResourceStateEntry`, `ControlIncomeEntry`, `EffectStateEntry`). Overriding
   `Equals`/`GetHashCode` on that many classes risks an inconsistent equality contract (e.g. a
   class used as a dictionary/hashset key elsewhere picking up unintended semantics) for no
   benefit, since the only consumer of this equality is each state class's own `Set()`. A
   generic helper keeps the comparison logic next to the feature that needs it:
   - `ListEquals<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> elementEquals)` —
     count check, then per-index `elementEquals`.
   - `DictionaryContentEquals<TValue>(IReadOnlyDictionary<string, TValue> a, IReadOnlyDictionary<string, TValue> b)` —
     count check, then for every key in `a`, `b` must contain that key with an
     `EqualityComparer<TValue>.Default`-equal value.
   - `HashSet<string>.SetEquals(...)` (BCL method, no wrapper needed) for
     `DiscoveredCountriesState.CountryIds`.
   - Named per-entry-type comparer functions (`SkillEntryEquals`, `CharacterStateEntryEquals`,
     `ActionCardEntryEquals`, etc.) so each is written once and reused wherever that entry type
     appears (`CharacterStateEntryEquals` is shared between `CountryCharactersState.Set` and the
     nested `Character` field inside `OrgCharacterSlotEntry` compared by `OrgCharactersState.Set`).

2. **`AnimatableInt`/`AnimatableDouble` fields compare by `.Actual`, never by reference or
   `.Display`.** This is the one non-obvious correctness point in the whole feature:
   - In `CountryCharactersState`'s path, `VisualStateConverter.UpdateCharacters` reuses the same
     `AnimatableInt` instance per `characterId` across ticks (via its
     `_characterOpinionAnimatables` cache) and always calls `SetActual` on it before building the
     `CharacterStateEntry`. So the *same instance* is referenced by both the old and new entry
     when nothing changed — reference equality would trivially "pass" but tell us nothing (it
     would also trivially "pass" if `Actual` *did* change, since it's still the same object).
   - In `VisualStateConverter.UpdateOrgCharacters`, by contrast, a **brand-new** `AnimatableInt()`
     is constructed every tick for each `OrgCharacterSlotEntry.Character` (never cached, never
     `SetActual`-ed — `Actual` stays `0`). Reference equality would report "different" on every
     tick even though nothing meaningful changed, defeating the optimization for `OrgCharactersState`.
   - Comparing `.Actual` (an `int`/`double` value, not the object) is correct in both shapes:
     equal when the underlying number is equal, regardless of whether the caller reused an
     instance or allocated a fresh one. `.Display` (which adds transient animation-barrier
     offsets) must not be used — barrier state is decoupled, ticked, and notified independently
     via the `AnimatableInt`/`AnimatableDouble`'s own `PropertyChanged` (see
     `HUDDocument.HandleControlTickChanged` subscribing directly to
     `Control.UsedControl.PropertyChanged`), which this feature must not touch.

3. **Per-class-shape `Set()` edits**, following the existing `LocaleState`/`MapLensState`
   early-return pattern (`if (equal) { return; }` before any assignment; assign fields and fire
   `PropertyChanged` only in the changed path):
   - **Scalar classes** — direct `&&`-chained field comparison against the incoming arguments.
   - **List classes** — `StateEquality.ListEquals(...)` with the relevant entry comparer.
     `CountryControlState.Set` is the one case needing care: `UsedControl.SetActual(used)` must
     still run unconditionally on every call (per spec), so capture
     `bool usedChanged = UsedControl.Actual != used;` *before* calling `SetActual`, call
     `SetActual` unconditionally next, then combine `usedChanged` with the `OrgEntries` list
     comparison to decide whether `CountryControlState`'s own `PropertyChanged` fires.
     `CountryResourcesState.Set` needs no equivalent capture — its `AnimatableDouble.SetActual`
     calls happen one level up, inside `VisualStateConverter.BuildResources`, entirely outside
     `CountryResourcesState.Set` itself, and are already untouched by this feature.
   - **Dictionary classes** (`ProvinceOwnershipState`, `ProvinceOccupationState`,
     `CountryScoreState`) — `StateEquality.DictionaryContentEquals(...)` on the durable
     dictionary only; the transient `Recent*` parameters are read/assigned as today but never
     enter the equality check.
   - **Set class** (`DiscoveredCountriesState`) — `CountryIds.SetEquals(ids)`; `RecentlyDiscovered`
     excluded from the check the same way.
   - **Left unchanged**: `LocaleState`, `MapLensState` (already the reference implementation),
     `SaveResultState` (explicitly excluded — keeps firing unconditionally).

4. **Call-site safety review for excluded transient fields** (spec requires this be confirmed,
   not merely assumed) — already traced during planning, to be re-confirmed against `main` at
   implementation time in case of drift:
   - `GameLogic.cs`'s two `VisualState.ProvinceOwnership.Set(...)`/`ProvinceOccupation.Set(...)`
     call sites (debug cheat commands) are both guarded by `if (changed) { ... }`, where `changed`
     comes from `ProvinceOwnershipSystem.ChangeOwner`/`ProvinceOccupationSystem.SetOccupier`/
     `ClearOccupier` — so a `Recent*` field is only ever set in the same call where the durable
     dictionary content also actually changed.
   - `VisualStateConverter.UpdateProvinceOwnership`/`UpdateProvinceOccupation` always pass the
     *previous* `Recent*` values straight through unchanged (they only get a new value via the
     `GameLogic.cs` cheat-command path above) — so no additional risk there.
   - `VisualStateConverter.UpdateDiscoveredCountries` computes `recently` only when a genuinely
     new id appears in the freshly-rebuilt `ids` set (compared against `_previousDiscoveredIds`);
     when nothing new is found it carries forward the *existing*
     `_state.DiscoveredCountries.RecentlyDiscovered` value unchanged. So `RecentlyDiscovered`
     only takes on a new value in the same call where `CountryIds`'s content also gained a
     member.
   - `SaveResultState`'s two call sites (`GameLogic.SaveGame`'s success/failure branches) are
     unaffected since this class is excluded from equality-check treatment entirely.

5. **Benchmarks** — add new `[MemoryDiagnoser]` benchmark classes under `src/Game.Benchmarks/`,
   grouped by equality shape (matches how the harness already groups by system, e.g.
   `ControlSystemBenchmarks`), each following `VisualStateConverterBenchmarks.cs`'s
   `GameWorldFixture.Build()` + one warm `Update(...)` pass fixture-construction convention so
   list/dictionary/set benchmarks exercise the real ~163-country data volume, not synthetic
   minimal data:
   - `ScalarVisualStateSetBenchmarks.cs` — `SelectedCountryState`, `SelectedOrganizationState`,
     `SelectedProvinceState`, `PlayerOrganizationState`, `TimeState`, plus `LocaleState`/
     `MapLensState` (already-compliant reference classes — benchmarked too, since the spec's
     Benchmark Coverage criterion names them as covered classes even though their code is
     unchanged, and their no-op cost is the direct baseline the new classes are held to).
   - `ListVisualStateSetBenchmarks.cs` — `CountryControlState`, `CountryCharactersState`,
     `OrgCharactersState`, `OrgMapState`, `OrgActionsState`, `CountryActionsState`,
     `LeaderboardState`, `GameLogState`, `CountryResourcesState`, `VisualEffectCollection`.
   - `DictionaryAndSetVisualStateSetBenchmarks.cs` — `ProvinceOwnershipState`,
     `ProvinceOccupationState`, `CountryScoreState`, `DiscoveredCountriesState`.

   Each state class gets a `<ClassName>_NoOp` and `<ClassName>_Update` `[Benchmark]` method pair:
   `NoOp` calls `Set(...)` with the exact values/collections currently stored on a pre-populated
   instance (fetched from the fixture's real `VisualState` after a warm `Update` with a selected
   country/org); `Update` calls `Set(...)` with a deliberately different value (e.g. one extra or
   one field-mutated entry appended to a copy of the same collection) so BenchmarkDotNet measures
   both the pass-through-suppressed path and the fires-normally path. `SaveResultState` gets no
   benchmark pair (excluded from this feature's equality-check scope entirely).

   Run via the `dotnet-benchmark` skill (`--update-baseline` once for the new methods, since they
   have no prior baseline entry, then `--compare` to confirm the harness's existing 5% gate
   passes on subsequent runs) rather than raw `dotnet run`/log parsing.

6. **Tests** — new `src/Game.Tests/VisualStateChangeNotificationTests.cs`, instantiating state
   classes directly (no `GameLogic`/ECS `World` needed — these are plain C# classes), subscribing
   a counting `PropertyChanged` handler, and asserting fire-count behavior. See Tests section below
   for the exact class list and why each was chosen.

## Section 1 — Agent Steps

- [ ] **Add `StateEquality` helper class** — create `src/Game.Main/StateEquality.cs` with
  `ListEquals<T>`, `DictionaryContentEquals<TValue>`, and named per-entry-type comparer functions
  for every plain-data record class listed in the Approach section, including the
  `AnimatableInt.Actual`/`AnimatableDouble.Actual`-based comparison for `CharacterStateEntry.Opinion`
  and `ResourceStateEntry.Value`.
- [ ] **Update scalar-field classes** — add early-return-on-equal to `Set(...)` in
  `SelectedCountryState`, `SelectedOrganizationState`, `SelectedProvinceState`,
  `PlayerOrganizationState`, `TimeState` (all in `VisualState.cs`/`TimeState.cs`), matching the
  `LocaleState`/`MapLensState` pattern exactly.
- [ ] **Update list-holding classes** — add structural-equality early-return to `Set(...)` in
  `CountryControlState` (preserving the unconditional `UsedControl.SetActual(used)` call via the
  pre-`SetActual` capture described in Approach step 3), `CountryCharactersState`,
  `OrgCharactersState`, `OrgMapState`, `OrgActionsState`, `CountryActionsState`, `LeaderboardState`,
  `GameLogState`, `CountryResourcesState`, `VisualEffectCollection`.
- [ ] **Update dictionary-holding classes** — add durable-content-only equality early-return to
  `Set(...)` in `ProvinceOwnershipState`, `ProvinceOccupationState`, `CountryScoreState`, excluding
  the transient `Recent*` parameters from the comparison.
- [ ] **Update the HashSet-holding class** — add `CountryIds.SetEquals(ids)`-based early-return to
  `DiscoveredCountriesState.Set(...)`, excluding `RecentlyDiscovered` from the comparison.
- [ ] **Re-confirm transient-field call-site safety** — re-read the current (possibly drifted since
  this plan was written) `GameLogic.cs`/`VisualStateConverter.cs` call sites for
  `ProvinceOwnership.Set`/`ProvinceOccupation.Set`/`DiscoveredCountries.Set`/`SaveResult.Set` and
  confirm the Approach step 4 analysis still holds before finalizing the equality checks; if a new
  call site has been added that sets a transient field without the durable data also changing,
  surface it rather than silently proceeding.
- [ ] **Add benchmark classes** — create `ScalarVisualStateSetBenchmarks.cs`,
  `ListVisualStateSetBenchmarks.cs`, `DictionaryAndSetVisualStateSetBenchmarks.cs` under
  `src/Game.Benchmarks/`, each `[MemoryDiagnoser]`, following `VisualStateConverterBenchmarks.cs`'s
  `GameWorldFixture`-based setup convention, with a `<ClassName>_NoOp`/`<ClassName>_Update`
  `[Benchmark]` method pair per covered state class as described in Approach step 5.
- [ ] **Add unit tests** — create `src/Game.Tests/VisualStateChangeNotificationTests.cs` per the
  Tests section below.
- [ ] **Run the full test suite** — via the `dotnet-test` skill, confirm all existing and new
  `src/Game.Tests` pass (no regression in any test that reads `VisualState` sub-state values,
  since assigned values are unchanged — only the fire/don't-fire decision changes).
- [ ] **Establish the new benchmark baseline** — via the `dotnet-benchmark` skill, run
  `--update-baseline` once (the new `<ClassName>_NoOp`/`<ClassName>_Update` methods have no prior
  baseline entry to compare against), then run `--compare` to confirm the harness's existing
  5%-regression gate passes cleanly on a second run, and that no *existing* (non-`VisualState`)
  benchmark regressed from the `Set(...)` changes (e.g. `VisualStateConverterBenchmarks.Update`,
  which now exercises every changed `Set(...)` path each tick).
- [ ] **Commit** — via the `commit` skill, once tests and benchmarks both pass.

## Section 2 — User Steps

None — this feature is confined to `src/Game.Main` C# state classes, `src/Game.Benchmarks`
benchmark classes, and `src/Game.Tests` unit tests. It requires no Unity Editor interaction,
no scene/prefab/asset changes, and no visual inspection — `Set(...)` call sites, argument
shapes, and `PropertyChangedEventArgs(null)` payload are all explicitly unchanged (Out of
Scope), so no UI-side behavior needs manual verification beyond what the automated tests and
benchmarks already cover.

## Tests

New file `src/Game.Tests/VisualStateChangeNotificationTests.cs`. Each state class is
instantiated directly (no `GameLogic`/ECS `World` needed) with a counting `PropertyChanged`
handler subscribed, matching the spec's literal "at minimum one scalar class, one list-holding
class, one dictionary-holding class, and the `HashSet`-holding class" plus two extra tests for
the acceptance criteria's most subtle nuances:

1. **`TimeState`** (scalar) — `Set(t, false, 0)` twice with identical arguments fires
   `PropertyChanged` 0 times on the second call; `Set(t, true, 0)` (one field different) fires
   exactly once more.
2. **`CountryControlState`** (list, plus the "`SetActual` always runs" nuance) — construct two
   field-equal-but-reference-distinct `List<OrgControlEntry>` instances; calling `Set(used, list2)`
   after `Set(used, list1)` with the same `used` fires `PropertyChanged` 0 additional times, but
   `UsedControl.Actual` is still correctly `used` and `UsedControl`'s own `PropertyChanged` still
   fired from the `SetActual` call (asserted separately by subscribing to `UsedControl.PropertyChanged`
   too). A subsequent `Set(differentUsed, list2)` fires `CountryControlState.PropertyChanged` once.
3. **`CountryScoreState`** (dictionary) — two separately-constructed `Dictionary<string, double>`
   instances with the same keys/values (insertion order differs) passed to consecutive `Set(...)`
   calls fire 0 additional times; a value or key difference fires once.
4. **`ProvinceOwnershipState`** (dictionary + transient-exclusion nuance) — same durable
   `OwnerByProvinceId` content across two `Set(...)` calls but different
   `recentProvinceId`/`recentOldOwnerId`/`recentNewOwnerId` arguments still fires 0 additional
   times, confirming the transient fields are excluded from the comparison as required.
5. **`DiscoveredCountriesState`** (HashSet) — two `HashSet<string>` instances with the same
   members in different insertion order fire 0 additional times; a member added/removed fires
   once; passing a different `recentlyDiscovered` string alongside an unchanged `CountryIds` set
   still fires 0 additional times (transient-field exclusion, same as above).

The remaining ~14 classes covered by this feature (list-holding `CountryCharactersState`,
`OrgCharactersState`, `OrgMapState`, `OrgActionsState`, `CountryActionsState`, `LeaderboardState`,
`GameLogState`, `CountryResourcesState`, `VisualEffectCollection`; scalar
`SelectedCountryState`, `SelectedOrganizationState`, `SelectedProvinceState`,
`PlayerOrganizationState`; dictionary `ProvinceOccupationState`) follow the exact same
`StateEquality`-based pattern validated by the five tests above and by the benchmark suite's
no-op/update pairs (which exercise every one of them); they do not each get a bespoke unit test,
mirroring the existing project convention that `LocaleState`/`MapLensState` — the reference
implementation this whole feature is modeled on — have never had a dedicated equality unit test
of their own.

## Constitution Check

No conflicts found — plan aligns with all principles. Specifically:
- **Game Logic (ECS in `src/`)** — untouched; this feature only changes `src/Game.Main`'s
  presentation-adjacent state-notification layer (`VisualState`), not ECS systems/components.
- **UI Toolkit only** — no `Assets/UI/`, prefab, scene, or MonoBehaviour changes; explicitly
  Out of Scope per the spec.
- **VContainer / DI** — no new singletons, no `FindObjectOfType`, no container wiring changes.
- **Planning Discipline** — this plan itself satisfies "plan before implement"; the feature is a
  behind-the-scenes optimization of an already-implemented mechanism, not a new bot feature or a
  `/optimize-performance`-gated existing-system tweak, so it does not fall under either carve-out
  and correctly goes through a full plan.
- **Specification Discipline** — an approved `spec.md` already exists for this feature.
- **File Organisation** — this plan lives at
  `Docs/Specs/26_07_21_08_visualstate-update-optimization/plan.md`, alongside its `spec.md`.
- **C# Code Style** — all new/edited code (tabs, `_`-prefixed private fields, always-braced
  control flow, no redundant access modifiers) will follow `.claude/rules/csharp/code_style.md`.
- **Rendering (URP)** — not implicated; no rendering code touched.
- **Assembly Structure** — no new `Assets/Scripts/` feature folders or `.asmdef` files; all new
  files live in existing `src/Game.Main`, `src/Game.Benchmarks`, `src/Game.Tests` projects, which
  already glob-include new `.cs` files via their SDK-style `.csproj`s.

Use the implement skill to start working on the plan or request changes.
