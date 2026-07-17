# Project Constitution

Non-negotiable architectural principles. The `/plan` command checks these before finalising any plan.

## Rendering

- **Unity 6 + Universal Render Pipeline only.** No Built-in RP shaders, materials, or camera-stack patterns; migrating away from URP is not an option.

## Game Logic

- **ECS for all game logic, living in `src/`.** No game state, simulation, or domain rules inside Unity MonoBehaviours; MonoBehaviours are presentation and input glue only.

## Dependency Injection

- **VContainer is the sole DI mechanism.** No `new` for singleton services, no `FindObjectOfType`, no static mutable singletons outside the container; every cross-system dependency is resolved through the VContainer composition root.

## UI

- **UI Toolkit only.** No Canvas, UGUI, or uGUI components anywhere in the project; all UI is authored in UXML/USS and bound via MonoBehaviour + View class pairs.

## Planning Discipline

- **Plan before implement.** No code or asset changes without an approved plan file; this prevents scope drift and keeps the git history reviewable.
- **Bot-feature carve-out.** Bot features implemented via `/implement-bot-feature` — `IBotFeature` implementations in `src/Game.Bots`, their registrations, and their `Docs/BotFeatures/` eval configs and history — use the skill's directly-written PRD plus the committed eval history as their planning artifact, under the standing spec/plan pair `Docs/Specs/52_bot-feature-eval-harness/`. Everything outside that surface still requires its own approved plan.

## Specification Discipline

- **Spec before plan for feature work.** Feature additions must start with a `/specify` pass that captures intent and acceptance criteria; purely technical tasks (migrations, refactors, infra) may skip the spec and go straight to `/plan`.

## File Organisation

- **`Docs/Specs/<index>_<name>/` for all new plans, spec-backed or not.** A technical-only plan (migration, refactor, infra) still gets a `Docs/Specs/<index>_<name>/plan.md`, simply with no `spec.md` alongside it. `Docs/Plans/<index>_<name>.md` is a legacy flat-file location retained for existing entries only — do not add new files there. The numeric index is shared across both directories and always increments; no two plans or specs share a prefix.

## Assembly Structure

- **One `.asmdef` per feature folder under `Assets/Scripts/`.** No cross-folder assemblies; each feature compiles independently and references others by GUID.

## C# Code Style

- **Tabs for indentation, `_` prefix for private members, braces always, no redundant access modifiers.** These rules are enforced project-wide; no per-file exceptions.
