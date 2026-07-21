# Spec: VisualState update optimization

## Feature Intent

As a developer maintaining the UI Toolkit bindings that subscribe to `VisualState`'s `INotifyPropertyChanged` state classes (`src/Game.Main/VisualState.cs`, `ResourcesState.cs`, `TimeState.cs`), I want every state class's `Set(...)` method to raise `PropertyChanged` only when the values it is given actually differ from the values already stored, so that per-tick calls from `VisualStateConverter.Update` (which currently fire unconditionally, every tick, regardless of whether anything changed) stop forcing UI bindings to rebuild/redraw when the underlying game data is unchanged — while proving, via dedicated BenchmarkDotNet benchmarks, that the added equality-check cost itself stays minimal on the no-op path.

## Acceptance Criteria

### Scalar-field classes

- **Given** a state class whose fields are all simple scalars (bool/string/int/double/enum/`DateTime`) — e.g. `SelectedCountryState`, `SelectedOrganizationState`, `SelectedProvinceState`, `PlayerOrganizationState`, `TimeState`, `MapLensState`, `LocaleState` — **When** `Set(...)` is called with argument values equal to the currently stored values **Then** `PropertyChanged` is not invoked.
- **Given** the same classes **When** `Set(...)` is called with at least one argument value different from the currently stored value **Then** `PropertyChanged` fires exactly once, as it does today.
- **Given** `LocaleState.Set` and `MapLensState.Set` already implement this early-return-on-equal pattern **Then** they are left as the reference implementation and are not required to change, only to remain the model the other classes follow.

### List-holding classes (structural, not reference, equality)

- **Given** a state class holding an `IReadOnlyList<T>` of a plain-data record (`OrgControlEntry` on `CountryControlState`, `CharacterStateEntry` on `CountryCharactersState`, `OrgCharacterSlotEntry` on `OrgCharactersState`, `OrgCountryEntry` on `OrgMapState`, `ActionCardEntry` on `OrgActionsState`/`CountryActionsState` (`Hand`/`Deck`), `LeaderboardEntryState` on `LeaderboardState` (`Organizations`/`Countries`), `GameLogEntry` on `GameLogState`, `ResourceStateEntry`/`ControlIncomeEntry` on `CountryResourcesState`) **When** `Set(...)` is called with a newly-allocated list instance whose elements are, in the same order, field-by-field equal to the elements of the currently stored list **Then** `PropertyChanged` is not invoked — reference inequality of the list/array instance alone must not be treated as a change, since `VisualStateConverter` allocates a fresh list on every call.
- **Given** the same classes **When** the new list differs from the old in element count, element order, or any single element's field values **Then** `PropertyChanged` fires.
- **Given** `CountryControlState.Set`/`CountryResourcesState.Set` also unconditionally call an `AnimatableInt`/`AnimatableDouble` `SetActual(...)` before the list is compared **Then** those `SetActual(...)` calls continue to run every time `Set(...)` is invoked, independent of whether the equality check suppresses `PropertyChanged` — the animatable's own barrier/animation bookkeeping must not be skipped just because the logical value didn't change.

### Dictionary- and set-holding classes

- **Given** `ProvinceOwnershipState.OwnerByProvinceId` (`IReadOnlyDictionary<string,string>`), `ProvinceOccupationState.OccupierByProvinceId` (`IReadOnlyDictionary<string,string>`), and `CountryScoreState.ScoreByCountryId` (`IReadOnlyDictionary<string,double>`) **When** `Set(...)` is called with a new dictionary instance containing the same set of key/value pairs as the current one, in any enumeration order **Then** `PropertyChanged` is not invoked.
- **Given** `DiscoveredCountriesState.CountryIds` (`HashSet<string>`) **When** `Set(...)` is called with a new set containing the same members as the current one, in any order **Then** `PropertyChanged` is not invoked based on set-equality, not reference or enumeration-order equality.

### Existing upstream version-gating is unaffected

- **Given** `VisualStateConverter.UpdateProvinceOwnership`/`UpdateProvinceOccupation` already skip calling `Set(...)` entirely when `ProvinceOwnershipSystem.GetVersion`/`ProvinceOccupationSystem.GetVersion` is unchanged since the last call **Then** this feature adds the equality check inside `Set(...)` itself in addition to that existing gate, and does not remove, weaken, or duplicate the version-check gating already in place.

### Benchmark coverage (first-class acceptance criterion, not incidental)

- **Given** the existing BenchmarkDotNet harness at `src/Game.Benchmarks` (see `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`) **Then** for each state class covered by this feature, a `[Benchmark]` method pair exists (no-op: `Set(...)` called with values identical to the instance's current state; update: `Set(...)` called with genuinely different values), each under `[MemoryDiagnoser]` per the harness's existing convention, following the same fixture-construction pattern already used by the harness's other benchmark classes (e.g. `VisualStateConverterBenchmarks.cs`).
- **Given** list/dictionary/set-holding classes specifically **Then** their no-op/update benchmark pairs are exercised with realistically-sized collections (matching production data volume — e.g. the full country/org/province counts the existing harness fixture already builds), not empty or single-element collections, so the structural-equality comparison's actual cost is measured, not hidden by trivial input size.
- **Given** the harness's existing `--compare`/`--update-baseline`/`dotnet test` workflow **Then** no new numeric acceptance threshold is introduced by this feature beyond the harness's existing default regression gate (`epsilonRelative = 0.05`) — the "minimal overhead" bar from the originating issue is satisfied by the no-op-path benchmarks being added to the suite and passing that existing gate once a baseline is captured, not by a bespoke new threshold.

### Test coverage

- **Given** `src/Game.Tests` already exercises `VisualState`/`VisualStateConverter` behavior **Then** unit tests are added asserting, per state class, that `PropertyChanged` does not fire on a value-identical `Set(...)` call and does fire on a value-different one — covering at minimum one scalar class, one list-holding class, one dictionary-holding class, and the `HashSet`-holding class, as representative cases of each equality-check shape above.

## Out of Scope

- Changing the public signature of any `Set(...)` method, or any call site in `VisualStateConverter.cs`/`GameLogic.cs` — this feature only changes *whether* `PropertyChanged` fires inside `Set(...)`, not what callers pass in or how often they call it.
- Per-property change granularity in `PropertyChanged` event args (e.g. naming which field changed) — events continue to fire with `PropertyChangedEventArgs(null)` exactly as today; only the fire/don't-fire decision changes.
- Any change to `AnimatableInt`/`AnimatableDouble`'s own internal animation/barrier logic (`Hold`/`Release`/`Tick`/`SetActual`) — those are called exactly as often as they are today.
- Any change to `ProvinceOwnershipSystem.GetVersion`/`ProvinceOccupationSystem.GetVersion` or the upstream skip-`Set()`-entirely gating already present in `VisualStateConverter` for those two states.
- Any UI-side (`Assets/Scripts/Unity/UI/`) binding/`Refresh()` change — this feature is confined to `src/Game.Main`'s state-notification layer.
- CI wiring, or any regression-threshold change to the BenchmarkDotNet harness beyond adding new benchmark methods that use its existing gate.

## Ambiguities

- [NEEDS CLARIFICATION: Do the transient "recent event" fields — `DiscoveredCountriesState.RecentlyDiscovered`, `ProvinceOwnershipState.RecentProvinceId`/`RecentOldOwnerId`/`RecentNewOwnerId`, `ProvinceOccupationState.RecentProvinceId`/`RecentOldOccupierId`/`RecentNewOccupierId` — participate in the equality comparison? Including them means a coincidentally-identical repeat event could be silently suppressed even though the UI may need to react to it as a distinct occurrence (e.g. a toast/flash); excluding them means the equality check is really "did the durable data change," and these transient signals need a separate re-fire mechanism (or an explicit decision that they're acceptable to compare like any other field, given `ClearRecentlyDiscovered()`-style clearing already exists for `DiscoveredCountriesState`).]
- [NEEDS CLARIFICATION: Is `SaveResultState` in scope for the same equality-check treatment? Unlike the tick-driven states, `Set(...)` here is only called from `GameLogic.cs` on a discrete save attempt (success/failure), not every frame — two consecutive identical outcomes (e.g. two failed saves with the same `ErrorType`) might legitimately each deserve their own UI notification as separate events, rather than the second one being suppressed as "no change."]
- [NEEDS CLARIFICATION: Does "each class" from the issue mean literally every one of the ~22 `INotifyPropertyChanged` state classes across `VisualState.cs`, `ResourcesState.cs`, and `TimeState.cs` (including ones like `VisualEffectCollection`/`LastFrameEffects`, whose `Effects` list is typically empty most ticks and is therefore a strong optimization candidate, and `GameLogState`, whose caller already skips `Set(...)` when there are zero new entries), or only the subset whose `Set(...)` is called unconditionally every tick with no existing upstream gate?]
- [NEEDS CLARIFICATION: The issue's "minimal overhead" bar has no concrete number attached. Is a qualitative bar (equality-check overhead is small relative to a typical `PropertyChanged` handler, verified only by the benchmark suite passing the harness's existing 5%-regression `--compare` gate once a baseline exists) sufficient, or does this feature need its own explicit numeric threshold (e.g. an absolute nanosecond or percentage ceiling) called out in the plan?]
