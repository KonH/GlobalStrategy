# Hidden Council

## Summary

A grand-strategy game set in the world of 1880, where you don't play a nation — you play a secret organization spreading its influence across 160+ historical countries. Built with Unity 6, a custom C# ECS, and a fully AI-assisted development workflow.

This repository is both a game and a case study in **structured AI-driven software development**: every feature here was specified, planned, implemented, reviewed, and shipped through a custom Claude Code workflow — including fully autonomous multi-hour implementation runs.

## Demos

**▶ [Play the game in your browser (Unity WebGL)](https://play.unity.com/en/games/e1953a2d-a3eb-40b1-b8ac-75282d4cf315/global-strategy)**

**▶ [Run the debug web client (experimental)](https://konh.github.io/GlobalStrategy/)**

---

## The Game (briefly)

- **Secret organization gameplay** — pick one of three organizations (Masons, Illuminati, or the Black Hand), operate from its HQ nation, and expand control across 26 playable countries against rival organizations.
- **Historical 1880 map** — 160+ countries reconstructed from historical GeoJSON, subdivided into provinces with mutable runtime ownership; four map lenses (Political, Organization, Geographic, Province).
- **Card-driven actions** — per-country action card hands with costs, success rolls, cooldowns, and a draw/discard deck system; gain control, sway characters' opinions, sell arms, declare war.
- **War and rivalries** — build and break rivalries between countries, declare and prosecute wars with damage/durability combat, resolve peace, and pursue revenge.
- **Characters & opinion** — each country has AI-generated character portraits (generals, diplomats, advisors) whose opinion of your organization you can influence.
- **Rival AI organizations** — other organizations run on their own bot AI, competing for control, declaring wars, and playing the same card economy you do.
- **Scoring, goals & leaderboards** — country/org scoring feeds a live leaderboard and a goals/tasks system, with an end-game comparison against your rivals.
- **Living simulation** — game time with speed controls, monthly income, resource effects, an in-game tutorial, autosaves, full save/load; localized in English and Russian.

## Tech Stack

| Layer | Technology |
|---|---|
| Engine | Unity 6 (URP, UI Toolkit, Input System, WebGL target) |
| Game logic | **Custom archetype-based ECS**, engine-independent, in [`src/`](src/) — Unity never owns game state |
| Codegen | Roslyn source generators for ECS queries and command handling |
| DI | VContainer as the single composition root |
| Async/UI | UniTask, UXML/USS with a shared style kit |
| Tooling | .NET 8 console runner, web-based live ECS state viewer, Python geo/asset pipelines |

Architecture is enforced by a written [Constitution](Docs/Constitution.md) — ECS-only game logic, MonoBehaviours as presentation glue only, UI Toolkit only, DI-only wiring — and every plan is checked against it before implementation starts.

## Milestones

### 1. World Domination — 2026-08-15

- Playable core loop across 26 countries — pick the Masons, Illuminati, or Black Hand and compete through action cards for control, diplomacy, and war.
- Complete war system: declare war, fight multi-stage battles, negotiate peace.
- Rivalries — build and break rival relationships with dedicated cards.
- AI bot opponents that play the full game via a dedicated bot API and eval harness.
- Goals & win conditions — live progress tracking, end-game comparison scores, and a leaderboard.
- Guided tutorial for new players.
- Full English + Russian localization; playable in-browser via WebGL.

---

## AI-Assisted Development — How This Project Is Actually Built

The interesting part of this repo isn't just the game — it's the **development system around it**. The project treats an AI coding agent (Claude Code) as a full team member with a defined process, guardrails, institutional memory, and autonomy levels. Everything below is checked into the repo and visible in the git history.

### 1. Spec-Driven Development with custom slash commands

Feature work follows a strict pipeline of custom Claude Code commands ([`.claude/commands/`](.claude/commands/)), each with explicit human approval gates:

```
/specify  →  spec.md        (intent + Given/When/Then acceptance criteria)   [user approves]
/plan     →  plan.md        (step-by-step plan, checked against Constitution) [user approves]
/plan-review                (independent review pass on the plan)             [user approves]
/implement                  (execution, phased via developer sub-agents)
/code-review                (review of the session's diff vs. project rules)  [user approves fixes]
/commit  /pr                (version bump, commit conventions, PR creation)
```

- Specs and plans are **timestamped, permanent artifacts**: [`Docs/Specs/`](Docs/Specs/) and [`Docs/Plans/`](Docs/Plans/) hold 45+ of them, so every feature in the game traces back to a written spec, its acceptance criteria, and the plan that shipped it.
- `/specify` and `/implement` **orchestrate sub-agents**: an architect sub-agent writes specs; developer sub-agents are spawned per implementation phase, each briefed with the plan, the relevant rules, and current file context — with the orchestrator verifying results between phases.
- The [Constitution](Docs/Constitution.md) defines non-negotiable architecture principles that `/plan` must validate before any plan is finalized — preventing the classic AI failure mode of quietly drifting from the intended architecture.

### 2. "Ralph loop" — autonomous fresh-context implementation

The most advanced piece: [`scripts/automation/claude/ralph.ps1`](scripts/automation/claude/ralph.ps1) + [`.ralph/`](.ralph/) implement an **autonomous agent loop** that turns an approved plan into working, committed code without a human in the loop:

1. `/create-prd` converts a plan into a machine-readable task list (`.ralph/prd.md`) where **every task carries a verification gate** — `dotnet build`, `dotnet test`, a Python config-validation script, or a Unity compile/console check via MCP.
2. The loop runs `claude -p` with a **fresh context per iteration**: each iteration reads the task list and an activity journal, picks exactly one unfinished task, implements it, runs its gate, and may only mark the task done if the gate actually passes (with output evidence journaled).
3. State lives in files, not in the model's memory — `.ralph/activity.md` carries decisions, gotchas, and blockers between iterations, so the loop is resumable and auditable.
4. `/complete-prd` finishes the run: commits leftovers and opens the PR. Per-iteration cost/token/duration metrics are logged to CSV.
5. Tasks that can't be honestly machine-verified (visual/UX outcomes) are explicitly flagged for manual check — the system never fabricates a green gate.

Real features (e.g. the province ownership system, [PR #9](../../pull/9)) were shipped this way.

### 3. Institutional memory: a curated rules knowledge base

[`.claude/rules/`](.claude/rules/) contains **25+ living rule documents** that make the agent effective on *this specific codebase*: ECS patterns, VContainer registration recipes, UI Toolkit gotchas (including version-specific Unity event bugs and their workarounds), WebGL pitfalls, scene/prefab YAML editing rules, asmdef conventions, config cross-validation, and more.

The `/learn` command closes the loop: whenever work surfaces a reusable lesson — a project decision, a recurring pattern, a hard-won debugging insight — it's distilled into a rule file (with human approval) so the *next* session starts smarter. The knowledge base is grown from real incidents, not written speculatively.

### 4. The agent drives the Unity Editor directly (MCP)

Via Unity MCP, Claude works inside the running editor rather than blindly editing files: creating and modifying scenes, prefabs, and ScriptableObjects; triggering asset refreshes; and — critically — **reading the editor console after every change** so compilation errors are caught and fixed within the same session. Usage rules and known pitfalls are codified in [`.claude/rules/unity/mcp_usage.md`](.claude/rules/unity/mcp_usage.md).

### 5. AI-powered content & data pipelines

- **Character portraits** — generated locally with ComfyUI + FLUX via scripted batch pipelines ([`scripts/utils/generate_image.py`](scripts/utils/generate_image.py)), with a documented prompt recipe per character role and region.
- **Province generation** — a two-stage geo pipeline ([`scripts/utils/generate_provinces.py`](scripts/utils/generate_provinces.py)): Python (geopandas/shapely/scipy) reconstructs 1880 borders, intersects them with Natural Earth admin regions or falls back to deterministic seeded Voronoi tessellation, names provinces from nearest historical settlements, simplifies geometry for WebGL — then a C# stage cross-validates and emits runtime configs. Fully reproducible, warnings-audited, and localized (including automated Latin→Cyrillic handling).
- **Flag assets** — era-accurate flags scripted from Wikimedia Commons with resolution checks and fallbacks.

### 6. Guardrails that make AI output trustworthy

- **Config cross-validation** rules treat every `countryId` as a foreign key, with scripted set-difference checks — because config mismatches fail *silently* at runtime.
- **Approval checkpoints are explicit and non-skippable**: specs, plans, reviews, and destructive actions always stop for the human; routine file edits never do. The boundary is written down, not vibes.
- **Code style, commit, and versioning conventions** are enforced through command definitions, so agent output is indistinguishable in form from hand-written work.

### Why this matters

This setup demonstrates AI adoption as an **engineering discipline**, not autocomplete: decomposing work so an agent can verify itself, encoding architecture as machine-checkable constraints, building feedback loops (console checks, gates, journals) instead of trusting output, and compounding productivity by turning every lesson into reusable context. The result is a solo-built game with the process rigor — specs, reviews, traceability — of a much larger team.

---

## Repository Layout

```
Assets/            Unity project (scenes, prefabs, UI Toolkit assets, configs, generated art)
src/               Engine-independent C# solution: custom ECS, game systems, source
                   generators, tests, console runner, live web-based ECS viewer
scripts/           automation/ (Ralph loop + GitHub issue automation, common + per-provider),
                   utils/ (province geo-generation, flag download, image generation)
Docs/              Constitution, 45+ numbered specs & plans (the project's paper trail)
.claude/           The AI workflow itself: 16 custom commands, 25+ rule documents
.ralph/            Autonomous loop state: prompt, task list (PRD), activity journal
```

## Running Locally

- Open the project in **Unity 6000.4.x**; the core DLLs are prebuilt into `Assets/Plugins/Core/`.
- To rebuild game logic: `dotnet build src/GlobalStrategy.Core.sln -c Release` (outputs directly into the Unity project).
- Tests: `dotnet test src/GlobalStrategy.Core.sln`.

## Standalone Web Client

[`src/Game.WebClient/`](src/Game.WebClient/) is a Blazor WebAssembly (.NET 8) standalone build of the same `src/` simulation — no Unity, no map, a text terminal for actions instead. It's the client deployed at [konh.github.io/GlobalStrategy](https://konh.github.io/GlobalStrategy/).

To run it locally:

```
dotnet run --project src/Game.WebClient
```

Then open the URL printed in the console (e.g. `http://localhost:5000`) in a browser. Saves are stored per-browser (IndexedDB); no Unity Editor is required.

## License

[MIT](LICENSE)
