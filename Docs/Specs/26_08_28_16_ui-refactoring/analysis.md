# UI Refactoring — Options Analysis

Pre-spec analysis. No decision made yet.

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

## Options

### A. Gallery scene
Dedicated scene + `GalleryLifetimeScope` that swaps one registration (`GameLifetimeScope.cs:79`) for a hand-built `VisualState`. Named scenarios (at-war, broke, empty-hand) switchable without restart.

- **Payoff:** directly fixes the loop. Previews *everything*, including imperative cards/tooltips/animation.
- **Cost:** ~half a day. One scene, one scope, one samples class.
- **Risk:** low — additive, touches no existing UI code.

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

## Dependency order

```
A (gallery) ──── independent, unblocks everything else's feedback loop
B (templates) ── prerequisite for C and E
   ├── C (ListView)
   └── E (binding)
D (tokens) ───── independent
F (fixes) ────── independent
```

## Open questions

1. Is A enough on its own? It may resolve the stated pain without B–E.
2. Does E earn its cost once A exists, or is the boilerplate better killed with a subscription helper (~10 lines replacing ~60)?
3. If E goes ahead — pilot on MainMenu and Time first, per the suggested sequencing?
4. `.clicked`: migrate all 22, or narrow the rule to cases that actually reproduced?
