# Hidden Council Milestone 1: World Domination
# tags: hidden-council, game-dev, unity, ecs, ai-assisted-development

<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/cover-image.png" alt="Hidden Council gameplay screenshot: the Illuminati organization's HUD showing gold and control resources, action cards for making Italy a rival and declaring war on Serbia and the Ottoman Empire, the action log, and the world map centered on Europe." />

This is the first milestone report for **Hidden Council**, a grand-strategy game set in 1880.

Everything below covers the project's entire history so far: April-August 2026, about four and a half months, from an empty Unity project to a playable, 26-country, three-organization strategy game with an AI opponent.

**[Play the demo](https://play.unity.com/en/games/e1953a2d-a3eb-40b1-b8ac-75282d4cf315/global-strategy)** · **[Source on GitHub](https://github.com/KonH/GlobalStrategy)**

## The game, briefly

You don't play a nation. You play a **secret organization**: the Masons, the Illuminati, or the Black Hand, operating out of one country's HQ and spreading control across the rest of the world through **action cards**.

Gain control of a country, sway a character's opinion, sell arms, or declare and prosecute war. Rival organizations run on their own bot AI and play the same card economy you do.

- **26 playable countries** on a historical 1880 map, subdivided into provinces with mutable ownership. Control shifts hands as cards, wars, and peace deals play out.
- **War**: declare war, fight it out over multiple battle rounds, and negotiate peace.
- **Rivalries**: build and break relationships between countries with dedicated cards.
- **Characters**: every country has AI-generated portraits (generals, diplomats, advisors) whose opinion of your organization you can move.
- **Goals, scoring, and a leaderboard**: live progress toward victory, with an end-game comparison against every rival.
- **Guided tutorial**, full English + Russian localization, autosave, and a browser build (Unity WebGL via Unity Play).

The goal for the project as a whole: a **secret-society 4X strategy game** built around broad, expressive card mechanics. This milestone is the first playable slice of that, not the finished picture (more on that at the end).

## Architecture: one engine-independent core, several consumers

The game logic lives entirely in `src/` as a pure C# solution with **no Unity dependency at all**: a custom archetype-based ECS, compiled with `dotnet build` and dropped into `Assets/Plugins/Core/` as a DLL. Unity is a consumer of that DLL, not the owner of any game state.

<figure>
<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/architecture.svg" alt="GlobalStrategy layered architecture: ECS.Core underlies a Foundation layer of shared data and config projects, which underlies Game.Systems, which underlies Game.Main, the orchestrator. Four independent entry points, Game.Bots, Game.ConsoleRunner, Game.WebClient, and Game.Benchmarks/Evals, plus the Unity Player/Editor, all depend on Game.Main. Unity specifically consumes it as a compiled DLL under Assets/Plugins/Core/." />
<figcaption>Every arrow points from a layer to the layer it depends on. Four independent front-ends (bot AI, a debug CLI, a standalone web client, and a benchmark/eval harness) sit on top of the same <code>Game.Main</code> orchestrator as the Unity player itself: Unity just consumes it one build step removed, as a compiled DLL.</figcaption>
</figure>

That last arrow is the one the architecture actually hinges on. Because `Game.Main` and everything under it has zero Unity references, the exact same game logic runs headless in a benchmark harness, in a bot-vs-bot evaluation loop, and in a Blazor WebAssembly debug terminal with no map and no visuals at all, with no behavior fork between any of them and the "real" game.

## A small deep dive: how resources actually work

Population, gold, recruits, country score, org score, war damage, and war durability: seven visibly different numbers on screen are all **one generic pipeline**, not seven bespoke systems.

<figure>
<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/resources-flow.svg" alt="Resources flow: ResourceDefinitions config feeds ECS components (Resource, ResourceOwner, ResourceLink, ResourceEffect), which feed the ResourceCollectorRegistry of pluggable formulas (population, score, recruits, damage, durability, base income), which feeds ResourceSystem.Update (instant, daily, or monthly effects, with clamping, caps, and history), which feeds VisualState via ResourceQuery into the UI Toolkit HUD." />
<figcaption>Adding a new resource means writing one small <code>IResourceCollector</code> implementation and registering it. The engine that applies it, clamps it, caps it, and journals its history for the action log is shared.</figcaption>
</figure>

This is the concrete shape of `.claude/rules/unity/ecs_patterns.md`'s "composition over parallel lookup entities" rule in practice: `country_score` and `org_score` used to be their own systems and were folded into this same pipeline mid-milestone once the pattern proved out.

## Building this with AI: the process, and the numbers

### A two-day zero-to-map prototype

The very first real proof of concept, before any of the architecture above existed, was the world map itself: parsing a real 1880 GeoJSON border file and rendering it as a scrollable, zoomable, seamlessly-looping world with visible country borders.

That went from an empty repo to working in two days, without deep prior familiarity with the GeoJSON format or geo-rendering specifics: the kind of gap an AI coding agent closes fast because it already knows the format and the pitfalls.

### The feature pipeline, end to end

Every feature that shipped this milestone went through the same pipeline, whether a human or an automation cron job drove it.

<figure>
<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/feature-pipeline.svg" alt="Feature pipeline: a GitHub issue flows through Spec, an optional Remote Session for spec review, Plan, and Implement, all running inside a Linux VPS running agents, tagged ai-specify, ai-plan, and ai-implement along the way, producing spec.md and plan.md as artifacts. It then moves to a Dev Machine running Unity and agents for PR review and a manual Unity smoke test, then to main, then to Unity Play." />
<figcaption>Specs and plans (<code>Docs/Specs/</code>) are checked-in, timestamped artifacts. The same pipeline runs unattended for GitHub-issue-triggered work: labels drive a state machine that takes an issue from spec to a mergeable PR without a human in the loop for the mechanical parts, with the final PR review and a manual Unity smoke test always happening on a real dev machine before merge.</figcaption>
</figure>

### The numbers

Overall: 88 specs shipped, at an estimated total cost of **~$680** (estimated: dataset covers 24% of specs).

<figure>
<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/loc-code.svg" alt="Code base lines of code, log scale: .cs 71,667 lines, .py 8,729, .ps1 266, .sh 151." />
<figcaption>The hand-authored surface of the game: <code>.cs</code> is the game and tooling logic, <code>.py</code> is the province/geo generation and asset scripts, <code>.ps1</code>/<code>.sh</code> are small automation wrappers.</figcaption>
</figure>

<figure>
<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/loc-other.svg" alt="Other lines of code, log scale: Configs .json 438,652 lines, Specs and Docs .md 41,484, Unity .asset, .unity, and .prefab combined 37,405." />
<figcaption><code>Configs (.json)</code> is almost entirely generated map and province geometry, not hand-written config.</figcaption>
</figure>

<figure>
<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/provider-share.svg" alt="Tool usage by provider: Claude 74.8 percent, Cursor 22.5 percent, Codex 2.7 percent." />
<figcaption>Share of AI coding tool usage across the milestone.</figcaption>
</figure>

<figure>
<img style="max-width:100%;height:auto;display:block;margin:0 auto;" src="images/avg-cost.svg" alt="Average cost per pipeline stage, estimated: dataset covers 24 percent of specs. Spec 4 dollars 69 cents, plan 4 dollars 34 cents, implement 12 dollars 75 cents." />
<figcaption>Implementation costs roughly 3x what spec or plan authoring costs, consistent with it being where most of the actual token volume (code, test output, build/console logs fed back to the agent) lives.</figcaption>
</figure>

Spec and plan size, and the size of what actually shipped, across all 88 specs:

| Metric | Count | Min | Max | Avg |
|---|---:|---:|---:|---:|
| Spec size (tokens) | 77 | 570 | 7,479 | 2,881 |
| Plan size (tokens) | 76 | 296 | 17,632 | 5,519 |
| Diff size (lines, final) | 86 | 0 | 86,567 | 3,320 |

Plans average almost 2x the size of the spec they came from, which tracks with plans being where architectural decisions actually get spelled out file-by-file.

## Open questions

- **Is a short spec better, or a long one?** The 570-7,479 token range is wide, and nothing here measures whether a longer spec produced a better outcome or just cost more tokens.
- **Do features need a plan this detailed?** Plans average almost 2x the size of the spec they came from. Whether that level of upfront detail pays for itself in fewer implementation surprises, versus a lighter plan plus more agent judgment at implement time, is an open question.

Both would need controlled, repeatable experiments (same feature, varied spec/plan depth, compared outcomes) to actually answer: out of scope for this milestone, but worth running before the next one.

## Tech debt

Nothing here is a surprise for a solo-built, AI-accelerated first milestone, but it's worth being honest about it going into the next one.

- **UI Toolkit usage is too straightforward, and possibly the wrong shape.**
  - Screens were built mostly ad hoc, panel by panel, as features landed, with no shared layout/state abstraction.
  - That shows up as recurring classes of bugs: tooltip layering and stale-tooltip cleanup, modal windows needing two clicks to close, an accordion collapsing onto the wrong task when the expanded one disappears. Each got fixed individually; none of the fixes generalize.
  - There's also a more conceptual question underneath the bug list: controllers are big classes hard-coupled to their UXML, and data binding isn't used anywhere. A binding-based approach might scale better than the current direct-manipulation style.
- **Performance has known, specific costs.**
  - The `VisualState` pattern (ECS state to `INotifyPropertyChanged` to UI Toolkit) that makes the UI layer clean also adds real memory pressure from change notifications.
  - Some condition/query recomputation that could be lazy currently isn't.
  - Both are already flagged in the benchmark harness output, just not yet acted on.
- **A long tail of little annoying things.**
  - WebGL-specific rendering quirks: Russian-text tofu, emoji arrows, IndexedDB flush timing.
  - Animation barrier edge cases.
  - Save/load duplicate-counter bugs.
  - The kind of thing that's individually cheap to fix and collectively erodes polish if it isn't scheduled real time.

## Conclusion

This milestone is a genuine success by the metric that matters most: a solo developer shipped a playable, 26-country, three-organization strategy game with a working AI opponent, a full war/diplomacy/card system, and a browser build, in about four and a half months, largely by directing AI agents through a structured spec-plan-implement pipeline instead of hand-writing most of the code. The process held up at scale: 88 specs, one architecture that never needed a rewrite.

It's not free of cost, though. The UI layer and some performance patterns accumulated debt that a from-scratch approach might have avoided, and the open questions above (spec size, plan depth) are real unknowns, not solved problems. The next milestone needs both a refactoring pass on the debt above and a clearer product vision for what secret-society 4X grows into next: this milestone proved the pipeline works, it didn't yet prove where it's taking the game.
