# UI Refactoring — Options Analysis

Pre-spec analysis. Option **G** is agreed; everything else is still open.

## Problem

Two feedback loops, nothing between them:

- `Design/01_prototype/design-final.html` — instant, zero Unity fidelity
- The running game — full fidelity, but every change costs a restart plus navigating to the target element (select a country, start a war, draw a card)

Secondary: document classes carry a lot of subscribe/refresh boilerplate (`HUDDocument.cs` is 1765 lines, ~30 subscribe/unsubscribe pairs).

## Constraining facts

Measured this session — these rule options in or out.

| Fact | Source | Consequence |
|---|---|---|
| `src/` has zero Unity references, `netstandard2.1`, shipped as a plugin DLL | `Game.Main.csproj` | Unity's property-bag source generator cannot run on `VisualState`; direct UXML binding to it falls back to reflection (AOT risk on WebGL) |
| All 36 change notifications are `PropertyChangedEventArgs(null)` | `src/Game.Main/VisualState.cs` | No granularity — any binding projector must re-project and diff to raise precise property names |
| `VisualState` = plain object, 30 public `Set` methods, no ECS needed to construct | `src/Game.Main/VisualState.cs` | Any UI state can be built in a few lines without running the game |
| 144 test files in `src/Game.Tests`, projector-test culture; **zero** Unity test assemblies | `src/Game.Tests/` | Logic moved to Unity-side ScriptableObjects leaves the fast `dotnet test` loop |
| 223 elements built in C# vs ~370 in UXML; only 11 `<ui:Instance>` | `Assets/Scripts/Unity/UI/`, `Assets/UI/` | Content-bearing UI (cards, rows, chips) has no UXML — cannot be previewed or bound at all |
| Prototype CSS tokens and `SharedStyles.uss` literals are identical values | `.claude/commands/design.md`, `SharedStyles.uss` | A shared token vocabulary is nearly free; 184 colour/font literals across 23 stylesheets today |
| Four documented UI Toolkit defects in 6000.4.1f1 | `.claude/rules/unity/uitoolkit.md` | Newest UIT subsystems deserve a pilot before broad adoption |

## Measured: where a frame actually goes

`GameLogic.Update` was split into `UpdateLogic` (simulation) + `UpdateVisualState` (projection into
`VisualState`), with `GameLoopRunner` wrapping each half in its own `ProfilerMarker`
(`GameLoop.UpdateLogic` / `GameLoop.UpdateVisualState`). One representative Editor-Profiler frame,
**no windows open**:

| Half | Time | Alloc |
|---|---|---|
| `UpdateLogic` | 8 ms | 40 KB |
| `UpdateVisualState` | **4 ms** | **200 KB** |

So the projection is **~1/3 of the loop's time and 5× its allocation**, while displaying nothing that
isn't already on screen. That is the number that justifies work here.

**`Docs/Benchmarks/baseline.json` is not a usable proxy for this.** Its
`VisualStateConverterBenchmarks.Update` reads 45 µs / 18 KB — roughly two orders of magnitude below
what Unity measures. Do not size UI work from it. Candidate causes, none yet verified: the benchmark
fixture's world is smaller than a real game's; BenchmarkDotNet runs .NET 8 JIT rather than Mono;
Editor profiling overhead inflates the Unity side. Worth one pass to find out, because if the gap is
mostly fixture size, the benchmark can be made representative and kept as the fast feedback loop.

Also unverified: how much of the 4 ms / 200 KB survives in a player build rather than the Editor.
Re-measure before treating the ratio as final.

## What `VisualState` actually is

The "it just caches the world, so read the world instead" framing is only half true. Three distinct
categories live in one class, and only the first can be pulled on demand:

**1. Level-triggered projections** — pure functions of the world, recomputed and diffed every tick no
matter who is looking. `UpdateLeaderboards` (`VisualStateConverter.cs:1021`) allocates two lists over
every org plus all 154 countries and sorts both, every tick, with the leaderboard window closed. Same
shape: Goals, SelectedWar, DebugOrgCardAvailability, EndGameComparison. **This is the on-demand
opportunity, and where the 200 KB lives.**

**2. Edge-triggered observations** — cannot be pulled, ever. `UpdateGameLog` reads
`ControlEffectApplied` / `OpinionEffectApplied` / `RoleChangeApplied` marker entities that
`CleanupEffectNotificationsSystem` destroys on the *next* tick; `LastFrameEffects` reads
`ResourceChange` the same way; `WarResults` / `CountryDestroyedResults` / `OrgDestroyedResults` are
queues with `Enqueue`/`AcknowledgeCurrent`; `ProvinceOwnership.Recent*` names the province that just
changed hands. A UI that reads the world when it happens to render misses everything in between.
These need a per-tick observer — that observer *is* `VisualState`.

**3. State with no world backing at all** — `MapLens` is written straight into `VisualState` from
`ChangeLensCommand` (`GameLogic.cs:191`), with no ECS component behind it. Likewise
`SelectedWar.PendingWarId`, `SaveResult`, and the `AnimatableInt`/`AnimatableDouble` + barrier system,
which is an interpolation with history ticked by `deltaTime` and held by the card-play animator.

Consequence: `VisualState` shrinks, it does not disappear. Categories 2 and 3 are irreducible.

## The laziness pattern already exists

`CountryActionsVisibility.ActionsPanelOpen` and `DebugOrgCardVisibility`'s four flags are UI-set hints
that make the converter skip per-card `ActionPlayability` evaluation while a panel is collapsed —
wired from `CountryActionsView.cs:223` and `HUDDocument.cs:258`, ~13 lines per gate. "Don't compute
card details when no card UI is present" is therefore already shipped for cards. The open question is
whether to keep generalising that flag-per-panel pattern or invert to a real pull model (option G).

## Options

### A. Gallery scene — **prototype built, verdict pending**

Original sketch: dedicated scene + `GalleryLifetimeScope` that swaps one registration
(`GameLifetimeScope.cs:79`) for a hand-built `VisualState`. Named scenarios (at-war, broke,
empty-hand) switchable without restart.

- **Payoff:** directly fixes the loop. Previews *everything*, including imperative cards/tooltips/animation.
- **Cost:** ~half a day. One scene, one scope, one samples class.
- **Risk:** low — additive, touches no existing UI code.

#### A as built

Scoped to one element — the action card — to get a verdict cheaply. Files:
`Assets/Scenes/Gallery.unity`, `Assets/Scripts/Unity/Gallery/{GalleryDocument.cs,
GS.Unity.Gallery.asmdef}`, `Assets/UI/Gallery/{Gallery.uxml, Gallery.uss}`.

**It needed neither the lifetime scope nor `VisualState`.** The sketch assumed a `GalleryLifetimeScope`
substituting a hand-built `VisualState` into DI. The prototype skips both: it constructs an
`ActionCardEntry` directly — one `new` per state, no ECS world, no `GameLogic`, no save, no bots — and
hands it to the production `ActionCardBuilder`. Constraining fact #3 held in practice, and further than
expected: previewing UI turns out not to require the state container at all, only the small DTO the
view actually reads. That is the same shape option G moves toward, arrived at independently.

Structure that emerged, and which later elements should follow:

- A page header, then **one `ui:Foldout` block per element**. Adding the next element (leaderboard row,
  character card) is one more sibling foldout, not a new scene.
- Two dropdowns per block: **which instance** (all 16 action ids from `action_config.json`) and
  **which state**. Seven states are enumerated for the card: playable, unaffordable gold, requirements
  failed, on cooldown, war-odds badge, multi-country target, discard hint. Each is a few lines in
  `BuildEntry` — the switch *is* the gallery's entire state layer.
- Real data throughout: real localization, `ActionVisualConfig` art, `CountryVisualConfig` flags, and
  the card's own `OrgActions.uss`. What the gallery renders is what the HUD renders.

**Live-edit rebinding is what makes the loop fast, and it is not free.** Editing the UXML or USS of a
running `UIDocument` makes Unity rebuild the document's whole visual tree from source, detaching every
element the C# side bound and discarding all control state. Without handling that, every style save
silently reset the dropdowns and collapsed the block — the loop stayed slow for the exact edits it
exists to serve. `GalleryDocument` therefore checks one detached-element flag per frame, rebinds to the
fresh tree, and restores selection and foldout state from `[SerializeField, HideInInspector]` fields
(serialized so a script recompile's domain reload survives too). **Any future gallery block must do the
same** — it is the difference between "edit USS, see it" and "edit USS, navigate back to where you
were".

**Cost paid to production code:** one word — `ActionConditionText` in `CountryActionsView.cs` went
internal → public so requirement rows use the same localized text as the HUD. `ComposeFaceData` is
duplicated (~25 lines) because the production copy is a private member of a view that also owns
gestures, tooltips and a hand container; extract the shared version if A graduates past prototype
rather than growing the copy.

**What this already tells us about E:** the gallery previews the C#-built content — cards, badges,
cooldown overlays, requirement rows — that has no UXML and that UI Builder therefore cannot show at
all. That is the preview payoff E was partly wanted for, delivered without binding, without property
bags and without the AOT question. E now has to earn its cost on boilerplate reduction alone.

### B. UXML template extraction
Author `ActionCard.uxml`, `LeaderboardRow.uxml`, `CharacterCard.uxml`; instantiate via `VisualTreeAsset.Instantiate()`.

- **Payoff:** UI Builder previewability for the content that currently has none. Prerequisite for C and for any binding.
- **Cost:** medium, per-template, incremental.
- **Risk:** low-medium — touches working view code.

### C. ListView virtualization
Replace `ScrollView` + full `Clear()`/rebuild. Worst case today: 154 country rows × ~4 elements rebuilt per tick while the leaderboard is open.

- **Payoff:** performance; removes the rebuild pattern.
- **Cost:** low *after* B (`makeItem` returns a template instance).
- **Risk:** low.

### D. USS design tokens
`--gs-*` custom properties on `:root` in `SharedStyles.uss`, mirroring the prototype's token names. Project uses zero USS variables today.

- **Payoff:** palette change becomes one block instead of 184 literals; shared vocabulary with the HTML prototype.
- **Cost:** low, mechanical.
- **Risk:** low.

### E. Presentation-model data binding
Unity-side `[CreateProperty]` model + `INotifyBindablePropertyChanged`, fed by a projector from `VisualState`. Bind UXML to that, not to `VisualState`. Preview via a sample `.asset` data source.

- **Payoff:** less document boilerplate; UI Builder preview of bound text; bound `ListView` item templates.
- **Cost:** high — new layer (model + projector + plumbing per screen). Logic relocates rather than disappears.
- **Risk:** medium. Three caveats:
  1. Null property names mean the projector must diff, or raise-all and lose the precision that justified the layer. `StateEquality.cs` may be reusable.
  2. Keep projection pure and in `src/` emitting a DTO, or the 144-test `dotnet test` loop is lost. Localized text can't fully move (`ILocalization` is Unity-side).
  3. `ScriptableObject` for runtime mutable state — use `CreateInstance<>()`, reserve the `.asset` for preview only. Binding does not require ScriptableObject at runtime.
- **Does not** help the 223 C#-built elements; B remains a prerequisite.

### F. Standalone fixes
Independent of the above, small.

- `HUDDocument.cs:737` still uses `EventSystem.IsPointerOverGameObject()`; `UIPointerState.IsPointerOverUI` is used everywhere else. One line.
- `HUDPanelSettings.asset:41` `m_MaxSubTextureSize: 64` excludes all flags (128×128) from the dynamic atlas.
- 22 `.clicked` sites predate the rule banning them (added 2026-05-16); 58 sites use `PointerUpEvent`. Migrate, or narrow the rule.
- `WarIcons.uxml` is the only UXML not importing `SharedStyles.uss`, yet uses `gs-btn`.
- `HUD.uxml:18` `data-source-type` is dead metadata from an abandoned 2026-04-07 attempt.
- `.claude/rules/unity/uitoolkit.md:160-173` contradicts `localization.md` on pointer-over-UI detection.

### G. Pull model for cold panels — **agreed**

Take the category-1 projections that feed rarely-open windows off the per-tick path: Leaderboard,
Goals, SelectedWar, DebugOrgCardAvailability, EndGameComparison. Each becomes a pure
`Project(IReadOnlyWorld, …) -> DTO` called when the window opens plus a coarse refresh while it stays
open, instead of every tick forever.

- **Payoff:** the largest measured slice of the 4 ms / 200 KB, for panels nobody is looking at. Also
  deletes their `StateEquality` diffing and their subscribe/unsubscribe pairs — the diff machinery
  exists only to suppress notifications that a pull model never raises.
- **Cost:** medium, per panel, incremental. Mostly moving existing code, not writing new logic.
- **Risk:** low, on one condition — **the projection functions stay in `src/`**. `src/Game.Tests` has
  144 test files, 15 of them projector-specific (`VisualStateConverterLeaderboardTests`,
  `GoalsProjectorTests`, `SelectedWarProjectorTests`, …) against **zero** Unity test assemblies.
  Relocating projection logic Unity-side would move exactly the trickiest rules out of `dotnet test`.
- **Prerequisites:** none. Independent of A–F.
- Access is already there: `GameLogic.World`, `IReadOnlyWorld`, `Resources`, `Relations` are all
  public and reachable from the Unity side today.

**Open, deliberately not decided: the full pull model.** Extending G to *everything*, deleting
`VisualState` outright, is not on the table until categories 2 and 3 above have an answer — the
edge-triggered queues and the animation barriers still need a per-tick observer. Revisit after G
lands and the profiler is re-read; if the always-visible HUD turns out to dominate what remains, the
question becomes real again.

## Dependency order

```
A (gallery) ──── card block built; independent; the gate on E
B (templates) ── prerequisite for C and E
   ├── C (ListView)
   └── E (binding)  ← blocked on A's prototype verdict
D (tokens) ───── independent
F (fixes) ────── independent
G (pull model) ─ independent; agreed
```

## Decisions so far

- **G is agreed** — pull model for cold panels, projections staying in `src/`.
- **A is built for one element** (the action card) and awaiting a verdict in the editor. It came out
  cheaper than sketched — no lifetime scope, no `VisualState` — and it previews the C#-built content
  nothing else can. Extending it is one foldout block per element.
- **E (native binding) is still wanted**, but gated on that verdict, and its case is now narrower: A
  already delivers the preview payoff, so E stands or falls on boilerplate reduction. Its three known
  caveats stand — reflection property bags for `src/` types under IL2CPP/WebGL, the mirror layer needed
  to avoid them, and the fact that UI Toolkit's runtime binding re-reads its source every frame unless
  the source implements `INotifyBindablePropertyChanged` (so binding gives panel-level gating, not
  per-access laziness — it does not by itself deliver what G delivers).
- **Full pull model / deleting `VisualState`** — not decided, see G's note.

## Open questions

1. Does A's card block resolve the feedback-loop pain in daily use, making B–E optional? Which element
   is worth the second block?
2. Does E earn its cost once A exists, or is the boilerplate better killed with a subscription helper (~10 lines replacing ~60)?
3. If E goes ahead — pilot on MainMenu and Time first, per the suggested sequencing?
4. `.clicked`: migrate all 22, or narrow the rule to cases that actually reproduced?
5. Why does the BenchmarkDotNet harness read ~100× cheaper than the Unity Profiler for the same
   projection, and can the fixture be made representative enough to keep as the fast loop?
6. How much of the 4 ms / 200 KB survives in a player build rather than the Editor?
