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

## Specification Discipline

- **Spec before plan for feature work.** Feature additions must start with a `/specify` pass that captures intent and acceptance criteria; purely technical tasks (migrations, refactors, infra) may skip the spec and go straight to `/plan`.

## File Organisation

- **`Docs/Specs/<index>_<name>/` for spec+plan pairs; `Docs/Plans/<index>_<name>.md` for technical-only plans.** The numeric index is shared across both directories and always increments; no two plans or specs share a prefix.

## Assembly Structure

- **One `.asmdef` per feature folder under `Assets/Scripts/`.** No cross-folder assemblies; each feature compiles independently and references others by GUID.

## C# Code Style

- **Tabs for indentation, `_` prefix for private members, braces always, no redundant access modifiers.** These rules are enforced project-wide; no per-file exceptions.
